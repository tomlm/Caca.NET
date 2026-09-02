/*
 *  Caca.NET      a managed port of libcaca's canvas, dithering and terminal output
 *  See Colors.cs for the full notice.
 */

using System.Runtime.InteropServices;
using System.Text;

namespace Caca.Drivers;

/// <summary>
/// Paints the canvas to a terminal with ANSI escape sequences, redrawing only
/// the cells that changed since the previous frame.
/// </summary>
internal sealed class AnsiDriver : IDriver
{
    private const string Esc = "[";

    /// <summary>
    /// libcaca orders its colours the DOS way (black, blue, green, cyan, red,
    /// magenta, brown, light gray); ANSI SGR orders them black, red, green,
    /// yellow, blue, magenta, cyan, white. This maps one onto the other.
    /// </summary>
    private static readonly int[] ToAnsiIndex = [0, 4, 2, 6, 1, 5, 3, 7];

    /// <summary>Private mode 2026: hold the frame back until it is complete.</summary>
    private const string BeginSyncUpdate = Esc + "?2026h";
    private const string EndSyncUpdate = Esc + "?2026l";

    private const char Escape = '\u001b';

    /// <summary>How long to wait for the terminal to answer the mode query.</summary>
    private const int QueryTimeoutMs = 100;

    private readonly StringBuilder _out = new(1 << 16);
    private readonly TextWriter _writer;
    private readonly bool _ownsConsole;
    private readonly bool _syncUpdate;

    /// <summary>Keystrokes read while waiting for a query reply, kept for
    /// <see cref="PollEvent"/> rather than thrown away.</summary>
    private readonly Queue<int> _pendingKeys = new();

    private int[] _prevChars = [];
    private uint[] _prevAttrs = [];
    private int _prevWidth = -1;
    private int _prevHeight = -1;

    private bool _pendingResize;
    private bool _quitRequested;
    private bool _disposed;

    public AnsiDriver()
    {
        _writer = Console.Out;
        _ownsConsole = true;

        EnableVirtualTerminal();

        (Width, Height) = ReadSize();

        try
        {
            Console.TreatControlCAsInput = true;
        }
        catch (IOException)
        {
            /* Not a real console; Ctrl-C will arrive as a signal instead. */
        }

        Console.CancelKeyPress += OnCancelKeyPress;

        /* Alternate screen buffer, cursor hidden, cleared. */
        Write($"{Esc}?1049h{Esc}?25l{Esc}0m{Esc}2J");
        Flush();

        _syncUpdate = DetectSyncUpdate();
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public void Refresh(Canvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        int w = canvas.Width;
        int h = canvas.Height;

        bool full = w != _prevWidth || h != _prevHeight;

        if (full)
        {
            _prevChars = new int[w * h];
            _prevAttrs = new uint[w * h];
            _prevChars.AsSpan().Fill(-1);
            _prevWidth = w;
            _prevHeight = h;
            _out.Append(Esc).Append("2J");
        }

        int[] chars = canvas.Chars;
        uint[] attrs = canvas.Attrs;

        /* -1 means "no SGR emitted yet this frame". */
        int curFg = -1, curBg = -1;
        AnsiStyle curStyle = AnsiStyle.None;
        int cursorX = -1, cursorY = -1;

        for (int y = 0; y < h; y++)
        {
            int row = y * w;

            for (int x = 0; x < w; x++)
            {
                int i = row + x;

                if (!full && chars[i] == _prevChars[i] && attrs[i] == _prevAttrs[i])
                    continue;

                _prevChars[i] = chars[i];
                _prevAttrs[i] = attrs[i];

                if (cursorY != y || cursorX != x)
                {
                    _out.Append(Esc).Append(y + 1).Append(';').Append(x + 1).Append('H');
                    cursorX = x;
                    cursorY = y;
                }

                int fg = Attr.ToAnsiFg(attrs[i]);
                int bg = Attr.ToAnsiBg(attrs[i]);
                AnsiStyle style = Attr.ToStyle(attrs[i]);

                if (fg != curFg || bg != curBg || style != curStyle)
                {
                    AppendSgr(fg, bg, style);
                    curFg = fg;
                    curBg = bg;
                    curStyle = style;
                }

                int ch = chars[i];

                /* Control characters would move the cursor; show a blank.
                 * Stay on the char overload for the BMP so a 50fps redraw is
                 * not allocating a string per cell. */
                if (ch < 0x20)
                    _out.Append(' ');
                else if (ch < 0x10000)
                    _out.Append((char)ch);
                else
                    _out.Append(char.ConvertFromUtf32(ch));

                cursorX++;
            }
        }

        if (_out.Length == 0)
            return;

        /* Everything between BSU and ESU is presented as one frame, so the
         * terminal never paints a half-drawn canvas. */
        if (_syncUpdate)
        {
            _out.Insert(0, BeginSyncUpdate);
            _out.Append(EndSyncUpdate);
        }

        Write(_out.ToString());
        _out.Clear();
        Flush();
    }

    public Event PollEvent()
    {
        if (_quitRequested)
        {
            _quitRequested = false;
            return Event.Quit();
        }

        (int w, int h) = ReadSize();

        if (w != Width || h != Height)
        {
            Width = w;
            Height = h;
            _pendingResize = true;
        }

        if (_pendingResize)
        {
            _pendingResize = false;
            return Event.Resized(Width, Height);
        }

        if (_pendingKeys.Count > 0)
            return Event.Key(_pendingKeys.Dequeue());

        try
        {
            if (!Console.KeyAvailable)
                return Event.None;
        }
        catch (InvalidOperationException)
        {
            return Event.None;
        }

        ConsoleKeyInfo key = Console.ReadKey(intercept: true);
        return Event.Key(Translate(key));
    }

    public void SetTitle(string title)
    {
        /* OSC 0: set both icon name and window title. */
        Write($"]0;{title}");
        Flush();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Console.CancelKeyPress -= OnCancelKeyPress;

        try
        {
            Console.TreatControlCAsInput = false;
        }
        catch (IOException)
        {
            /* Never got it in the first place. */
        }

        /* Close any update still open, reset attributes, show the cursor,
         * leave the alternate buffer. */
        if (_syncUpdate)
            Write(EndSyncUpdate);

        Write($"{Esc}0m{Esc}?25h{Esc}?1049l");
        Flush();
    }

    /// <summary>
    /// Asks the terminal whether it knows private mode 2026, synchronized
    /// output, using DECRQM: <c>CSI ? 2026 $ p</c>. A terminal that does
    /// answers <c>CSI ? 2026 ; Ps $ y</c>, where Ps is 0 for "no such mode",
    /// 1 for set and 2 for reset — so 1 or 2 means we can use it.
    ///
    /// A terminal that implements no DECRQM at all simply says nothing, so a
    /// Primary Device Attributes request, <c>CSI c</c>, rides along behind the
    /// query. Every terminal answers that one, and its reply is the signal that
    /// no 2026 answer is coming, which saves waiting out the timeout. Note it
    /// is CSI c, not ESC c: the latter is RIS, and would reset the terminal.
    ///
    /// <c>CACA_SYNC=0</c> forces this off and <c>CACA_SYNC=1</c> forces it on,
    /// for terminals that support the mode but not the query.
    /// </summary>
    private bool DetectSyncUpdate()
    {
        string? forced = Environment.GetEnvironmentVariable("CACA_SYNC");

        if (forced == "0")
            return false;

        if (forced == "1")
            return true;

        if (!_ownsConsole || Console.IsInputRedirected)
            return false;

        /* DECRQM for mode 2026, then DA1. Esc is "ESC [", so this is
         * CSI ? 2026 $ p followed by CSI c. */
        Write($"{Esc}?2026$p{Esc}c");
        Flush();

        bool supported = false;
        StringBuilder seq = new();
        bool inSeq = false;
        long deadline = Environment.TickCount64 + QueryTimeoutMs;

        while (Environment.TickCount64 < deadline)
        {
            try
            {
                if (!Console.KeyAvailable)
                {
                    Thread.Sleep(1);
                    continue;
                }
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            char c = Console.ReadKey(intercept: true).KeyChar;

            if (!inSeq)
            {
                /* Anything the user typed at us meanwhile is a real keystroke. */
                if (c == Escape)
                {
                    inSeq = true;
                    seq.Clear();
                }
                else if (c != '\0')
                {
                    _pendingKeys.Enqueue(c);
                }

                continue;
            }

            seq.Append(c);

            /* A CSI sequence runs until a byte in 0x40..0x7e, which the '['
             * introducer would otherwise satisfy on its own. */
            if (seq.Length == 1 && c == '[')
                continue;

            if (c is < '@' or > '~')
                continue;

            inSeq = false;

            if (IsSyncUpdateReply(seq.ToString()))
                supported = true;

            /* The device attributes reply: the terminal has had its say. */
            if (c == 'c')
                break;
        }

        return supported;
    }

    /// <summary>Recognises <c>CSI ? 2026 ; Ps $ y</c> with Ps of 1 or 2, given
    /// the sequence with its leading escape already stripped.</summary>
    private static bool IsSyncUpdateReply(string seq)
    {
        if (!seq.StartsWith("[?", StringComparison.Ordinal) ||
            !seq.EndsWith("$y", StringComparison.Ordinal))
        {
            return false;
        }

        string body = seq[2..^2];
        int semi = body.IndexOf(';');

        if (semi < 0 || !body.AsSpan(0, semi).SequenceEqual("2026"))
            return false;

        return body[(semi + 1)..] is "1" or "2";
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        _quitRequested = true;
    }

    private void AppendSgr(int fg, int bg, AnsiStyle style)
    {
        _out.Append(Esc).Append('0');

        if ((style & AnsiStyle.Bold) != 0) _out.Append(";1");
        if ((style & AnsiStyle.Italics) != 0) _out.Append(";3");
        if ((style & AnsiStyle.Underline) != 0) _out.Append(";4");
        if ((style & AnsiStyle.Blink) != 0) _out.Append(";5");

        int fgIndex = ToAnsiIndex[fg & 0x7];
        int bgIndex = ToAnsiIndex[bg & 0x7];

        _out.Append(';').Append(fg < 8 ? 30 + fgIndex : 90 + fgIndex);
        _out.Append(';').Append(bg < 8 ? 40 + bgIndex : 100 + bgIndex);
        _out.Append('m');
    }

    private static int Translate(ConsoleKeyInfo key)
    {
        /* A real character beats the key code: this is what carries space,
         * carriage return and everything else the demo listens for. */
        if (key.KeyChar != '\0')
            return key.KeyChar;

        return key.Key switch
        {
            ConsoleKey.UpArrow => (int)EventKey.Up,
            ConsoleKey.DownArrow => (int)EventKey.Down,
            ConsoleKey.LeftArrow => (int)EventKey.Left,
            ConsoleKey.RightArrow => (int)EventKey.Right,
            ConsoleKey.Insert => (int)EventKey.Insert,
            ConsoleKey.Home => (int)EventKey.Home,
            ConsoleKey.End => (int)EventKey.End,
            ConsoleKey.PageUp => (int)EventKey.PageUp,
            ConsoleKey.PageDown => (int)EventKey.PageDown,
            >= ConsoleKey.F1 and <= ConsoleKey.F12 =>
                (int)EventKey.F1 + (key.Key - ConsoleKey.F1),
            _ => (int)EventKey.Unknown,
        };
    }

    private static (int Width, int Height) ReadSize()
    {
        try
        {
            int w = Console.WindowWidth;
            int h = Console.WindowHeight;

            if (w > 0 && h > 0)
                return (w, h);
        }
        catch (IOException)
        {
            /* Output is redirected. */
        }
        catch (ArgumentOutOfRangeException)
        {
            /* Console reports a nonsense size. */
        }

        return (80, 24);
    }

    private void Write(string s)
    {
        if (_ownsConsole)
            _writer.Write(s);
    }

    private void Flush() => _writer.Flush();

    /// <summary>
    /// Turns on escape-sequence processing for the Windows console. This is an
    /// OS call, not a third-party library, so it costs us no dependency.
    /// Windows Terminal has it on already; the legacy conhost does not.
    /// </summary>
    private static void EnableVirtualTerminal()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            nint handle = GetStdHandle(StdOutputHandle);

            if (handle == 0 || handle == -1)
                return;

            if (!GetConsoleMode(handle, out uint mode))
                return;

            SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
        }
        catch (DllNotFoundException)
        {
            /* Not a Windows console after all. */
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private const int StdOutputHandle = -11;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);
}
