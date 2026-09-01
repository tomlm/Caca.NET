/*
 *  LibCaca       a managed port of libcaca's canvas, dithering and terminal output
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

    private readonly StringBuilder _out = new(1 << 16);
    private readonly TextWriter _writer;
    private readonly bool _ownsConsole;

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

        /* Reset attributes, show the cursor, leave the alternate buffer. */
        Write($"{Esc}0m{Esc}?25h{Esc}?1049l");
        Flush();
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
