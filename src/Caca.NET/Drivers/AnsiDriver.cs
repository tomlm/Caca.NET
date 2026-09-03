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

    /// <summary>The frame under construction, reused for the life of the driver.</summary>
    /// <remarks>
    /// <para>A char array rather than a StringBuilder, because everything the builder offered was
    /// being paid for and then thrown away. The frame has to LEAVE it to be written, and every way
    /// out costs something: ToString copies the whole frame into a fresh string, tens of thousands
    /// of characters and twice that in bytes, which crosses the large-object threshold on a big
    /// enough screen; handing the builder to TextWriter skips the copy but writes it a chunk at a
    /// time, and a console write is charged per call as well as per byte.</para>
    /// <para>Building into one array and handing over one span of it has neither the copy nor the
    /// extra calls, and drops the builder's per-Append chunk bookkeeping as well.</para>
    /// </remarks>
    private char[] _buf = new char[1 << 16];

    /// <summary>How much of <see cref="_buf"/> the frame so far occupies.</summary>
    private int _len;

    /// <summary>How many finished frames may be waiting to go out before one is skipped.</summary>
    /// <remarks>
    /// <para>Eight by default -- 160ms of lag at fifty frames a second -- and CACA_QUEUE overrides
    /// it. Two was the first answer, on the argument that the queue exists to stop the terminal
    /// setting the pace, not to buffer a backlog. What two frames of slack could not absorb was a
    /// drain that is BURSTY rather than slow: Windows' pseudoconsole accepts the same output at
    /// better than frame rate on average and still stalls past forty milliseconds at a time, so
    /// frames were skipped against a pipeline that was, on average, keeping up.</para>
    /// <para>The proof is the WSL relay, which pushes identical bytes through the identical
    /// pseudoconsole into the identical terminal with no skips at all -- not by being faster, but
    /// by being DEEPER: the kernel pty buffer and the relay in front of it absorb exactly the
    /// stalls this queue was too shallow for. This is that depth, in-process.</para>
    /// <para>Latency only shows when the queue actually fills, so a terminal that keeps up sees
    /// frames as fresh as it ever did. A genuinely slow terminal shows frames up to the queue's
    /// depth stale -- for a demo, a far better trade than losing them.</para>
    /// </remarks>
    private static readonly int MaxPending =
        int.TryParse(Environment.GetEnvironmentVariable("CACA_QUEUE"), out var depth) && depth > 0
            ? depth
            : 8;

    private readonly object _qlock = new();
    private readonly Queue<(char[] Buffer, int Length)> _pending = new();
    private readonly Stack<char[]> _spare = new();
    private readonly bool _async;
    private Thread? _writerThread;
    private bool _writerStop;
    private long _skipped;
    private long _delivered;
    /// <summary>What the console's output encoding was before the driver changed it, if it did.</summary>
    private Encoding? _previousOutputEncoding;
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
    private bool _raisedTimerResolution;

    public AnsiDriver()
    {
        /* Our own writer over the raw stream, not Console.Out. The writer behind Console.Out
         * carries a 256-character buffer with AutoFlush on, so a frame-sized write leaves the
         * process as a thousand syscall-sized fragments -- each one separately parsed and
         * re-serialized by the pseudoconsole on Windows, plus a synchronization lock per call.
         * Measured against a live pseudoconsole with full-screen frames: 14 frames a second
         * through Console.Out, around a hundred through this. That gap was the entire difference
         * between the demo run natively and the same demo relayed from WSL, whose native relay
         * never chunks. This writer buffers 64K and flushes when the DRIVER says so, which it
         * already does explicitly everywhere it matters.
         *
         * One managed path for every platform, deliberately. WriteConsoleW measured no faster, and
         * everything it would have bought is had more simply: the encoding switch below makes the
         * console read this writer's UTF-8 correctly, and the switch is UNDONE in Dispose rather
         * than left changing the code page of whatever shell the demo was launched from. Guarded,
         * because a redirected handle can refuse it -- bytes to a file need no code page. */
        try
        {
            _previousOutputEncoding = Console.OutputEncoding;
            Console.OutputEncoding = new UTF8Encoding(false);
        }
        catch (Exception e) when (e is System.IO.IOException or System.Security.SecurityException)
        {
            _previousOutputEncoding = null;
        }

        _writer = new System.IO.StreamWriter(
            Console.OpenStandardOutput(), new UTF8Encoding(false), 1 << 16, leaveOpen: true)
        {
            AutoFlush = false,
        };

        _ownsConsole = true;

        /* On by default. Opt out with CACA_ASYNC=0 for a caller that would rather have the write
         * accounted to the frame that caused it -- a test comparing output, say. */
        _async = Environment.GetEnvironmentVariable("CACA_ASYNC") != "0";

        EnableVirtualTerminal();

        _raisedTimerResolution = RaiseTimerResolution();

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

        /* Started after the setup above has gone out synchronously, so nothing it wrote can be
         * overtaken by a frame. */
        if (_async)
        {
            _writerThread = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "caca-ansi-writer",
            };

            _writerThread.Start();
        }
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public void Refresh(Canvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        /* Asked BEFORE the frame is built, and that ordering is the whole correctness argument.
         *
         * This driver sends differences: _prevChars and _prevAttrs describe what the TERMINAL is
         * showing, and a cell is only sent when it differs from them. Building a frame updates
         * them. So a frame that is built and then thrown away has already recorded its changes as
         * delivered when they never went anywhere, and every one of those cells is wrong on screen
         * until something else happens to touch it -- permanently, for a cell nothing writes to
         * again.
         *
         * Skipping before the build leaves _prev exactly as it was, still describing what was last
         * SENT. The canvas meanwhile keeps animating, so the next frame that is built diffs against
         * the screen and naturally carries everything that accumulated while we were behind. The
         * skip costs a frame of animation, not correctness. */
        if (_async && PendingIsFull())
        {
            _skipped++;
            return;
        }

        int w = canvas.Width;
        int h = canvas.Height;

        bool full = w != _prevWidth || h != _prevHeight;

        _len = 0;

        /* Written BEFORE the frame rather than inserted in front of it afterwards. Insert(0, ...)
         * shifts every character already in the buffer to make room for eight. */
        if (_syncUpdate)
            Put(BeginSyncUpdate);

        /* What an empty frame looks like now that it may already carry that opening half. */
        int prefix = _len;

        if (full)
        {
            _prevChars = new int[w * h];
            _prevAttrs = new uint[w * h];
            _prevChars.AsSpan().Fill(-1);
            _prevWidth = w;
            _prevHeight = h;
            Put(Esc);
            Put("2J");
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
                    Put(Esc);
                    Put(y + 1);
                    Put(';');
                    Put(x + 1);
                    Put('H');
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
                    Put(' ');
                else if (ch < 0x10000)
                    Put((char)ch);
                else
                    Put(char.ConvertFromUtf32(ch));

                cursorX++;
            }
        }

        if (_len == prefix)
        {
            _len = 0;
            return;
        }

        /* Everything between BSU and ESU is presented as one frame, so the
         * terminal never paints a half-drawn canvas. */
        if (_syncUpdate)
            Put(EndSyncUpdate);

        if (_async)
        {
            /* The buffer goes with the frame; this thread takes another one, so the writer is
             * never reading an array the next frame is being built into. */
            Enqueue(_buf, _len);
        }
        else
        {
            Write(_buf, _len);
            Flush();
            Interlocked.Increment(ref _delivered);
        }

        _len = 0;
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
        /* Behind whatever frames are already queued, so the title does not land in the middle of
         * one. */
        Drain();

        /* OSC 0: set both icon name and window title. */
        Write($"]0;{title}");
        Flush();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        /* Everything queued goes out before the teardown below, which must be the last thing the
         * terminal sees. */
        StopWriter();

        if (_raisedTimerResolution)
        {
            try { timeEndPeriod(TimerResolutionMs); }
            catch { /* nothing to give back if the call was never there */ }

            _raisedTimerResolution = false;
        }
        Console.CancelKeyPress -= OnCancelKeyPress;

        try
        {
            Console.TreatControlCAsInput = false;
        }
        catch (IOException)
        {
            /* Never got it in the first place. */
        }

        /* Nothing flushes this writer but us, and the reset below must actually arrive. */
        try { _writer.Flush(); } catch { }

        /* Close any update still open, reset attributes, show the cursor,
         * leave the alternate buffer. */
        if (_syncUpdate)
            Write(EndSyncUpdate);

        Write($"{Esc}0m{Esc}?25h{Esc}?1049l");
        Flush();

        /* The code page belongs to the console the demo was launched from, not to the demo. */
        if (_previousOutputEncoding is not null)
        {
            try { Console.OutputEncoding = _previousOutputEncoding; }
            catch (Exception e) when (e is System.IO.IOException or System.Security.SecurityException)
            {
            }

            _previousOutputEncoding = null;
        }
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
        Put(Esc);
        Put('0');

        if ((style & AnsiStyle.Bold) != 0) Put(";1");
        if ((style & AnsiStyle.Italics) != 0) Put(";3");
        if ((style & AnsiStyle.Underline) != 0) Put(";4");
        if ((style & AnsiStyle.Blink) != 0) Put(";5");

        int fgIndex = ToAnsiIndex[fg & 0x7];
        int bgIndex = ToAnsiIndex[bg & 0x7];

        Put(';');
        Put(fg < 8 ? 30 + fgIndex : 90 + fgIndex);
        Put(';');
        Put(bg < 8 ? 40 + bgIndex : 100 + bgIndex);
        Put('m');
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

    /// <inheritdoc />
    public long SkippedFrames => Interlocked.Read(ref _skipped);

    /// <inheritdoc />
    public long DeliveredFrames => Interlocked.Read(ref _delivered);

    /// <summary>Hands a finished frame to the writer thread and takes a fresh buffer.</summary>
    private void Enqueue(char[] buffer, int length)
    {
        lock (_qlock)
        {
            _pending.Enqueue((buffer, length));

            _buf = _spare.Count > 0
                ? _spare.Pop()
                : new char[buffer.Length];

            Monitor.PulseAll(_qlock);
        }
    }

    /// <summary>Whether the writer is far enough behind that this frame should be skipped.</summary>
    private bool PendingIsFull()
    {
        lock (_qlock)
            return _pending.Count >= MaxPending;
    }

    /// <summary>Waits for every queued frame to reach the terminal.</summary>
    private void Drain()
    {
        if (!_async)
            return;

        lock (_qlock)
        {
            while (_pending.Count > 0)
                Monitor.Wait(_qlock);
        }
    }

    /// <summary>Drains the queue, then stops the writer thread and waits for it.</summary>
    private void StopWriter()
    {
        Thread? thread = _writerThread;

        if (thread is null)
            return;

        _writerThread = null;

        lock (_qlock)
        {
            _writerStop = true;
            Monitor.PulseAll(_qlock);
        }

        /* The loop finishes what is queued before it sees the stop, so this is the drain as well.
         * Bounded, because a terminal that has stopped consuming must not stop us exiting. */
        thread.Join(TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// Writes finished frames, off the thread that builds them.
    /// </summary>
    /// <remarks>
    /// A write to a console handle costs whatever the terminal takes to consume it -- measured at
    /// fifteen times a write to a pipe, and far more than that when the terminal is rendering and
    /// the pseudoconsole's buffer fills, at which point the write simply blocks. Done on the demo's
    /// own thread that makes the terminal the pacemaker: the animation slows to whatever the
    /// terminal can draw. Doing it here is what a relay process does for a program running under
    /// WSL, and is why the same program appears to hold its frame rate there.
    /// </remarks>
    private void WriterLoop()
    {
        while (true)
        {
            char[] buffer;
            int length;

            lock (_qlock)
            {
                while (_pending.Count == 0 && !_writerStop)
                    Monitor.Wait(_qlock);

                if (_pending.Count == 0)
                    return;

                (buffer, length) = _pending.Dequeue();
                Monitor.PulseAll(_qlock);
            }

            try
            {
                Write(buffer, length);
                Flush();

                /* Counted after the write completes, so a frame is "delivered" only once the
                 * terminal has accepted every byte of it -- which is also why this number lags the
                 * demo's own rate when the terminal is slow: the gap between the two IS the
                 * slowness. */
                Interlocked.Increment(ref _delivered);
            }
            catch
            {
                /* The terminal has gone. There is nothing this thread can usefully do about it,
                 * and throwing here would take the process down from a background thread. */
            }

            lock (_qlock)
            {
                _spare.Push(buffer);
                Monitor.PulseAll(_qlock);
            }
        }
    }

    private void Write(string s)
    {
        if (_ownsConsole)
            _writer.Write(s);
    }

    /// <summary>Writes the frame as one span of the buffer it was built in.</summary>
    /// <remarks>
    /// Nothing platform-specific, on purpose. A direct WriteConsoleW path was tried and measured
    /// NO faster than this writer -- the killer was only ever Console.Out fragmenting the frame,
    /// and any strategy that puts it out in a few large writes lands in the same place. The
    /// writer's own 64K buffering also keeps each underlying write under the size a console
    /// has historically refused in one piece.
    /// </remarks>
    private void Write(char[] buffer, int count)
    {
        if (_ownsConsole)
            _writer.Write(buffer, 0, count);
    }

    /// <summary>Makes room for <paramref name="extra"/> more characters.</summary>
    /// <remarks>
    /// Doubling rather than growing to fit, so a frame that creeps up in size does not copy the
    /// whole buffer on every one of its steps. The initial 64K covers a full screen of per-cell
    /// colour, so in practice this never runs after the first frame.
    /// </remarks>
    private void Ensure(int extra)
    {
        if (_len + extra <= _buf.Length)
            return;

        int size = _buf.Length;

        while (size < _len + extra)
            size *= 2;

        Array.Resize(ref _buf, size);
    }

    private void Put(char c)
    {
        Ensure(1);
        _buf[_len++] = c;
    }

    private void Put(string s)
    {
        Ensure(s.Length);
        s.CopyTo(0, _buf, _len, s.Length);
        _len += s.Length;
    }

    /// <summary>Formats a number straight into the frame, allocating nothing.</summary>
    /// <remarks>
    /// A cursor move carries two of these and a colour change two more, so a full screen formats
    /// thousands. The digits land in the frame itself rather than in a builder to be copied out.
    /// </remarks>
    private void Put(int value)
    {
        /* Eleven is the widest an int can format to, and nothing here is ever negative. */
        Ensure(11);
        value.TryFormat(_buf.AsSpan(_len), out int written);
        _len += written;
    }

    private void Flush() => _writer.Flush();

    /// <summary>
    /// Turns on escape-sequence processing for the Windows console. This is an
    /// OS call, not a third-party library, so it costs us no dependency.
    /// Windows Terminal has it on already; the legacy conhost does not.
    /// </summary>
    /// <summary>
    /// Asks Windows for a one millisecond timer tick, and reports whether it got one.
    /// </summary>
    /// <remarks>
    /// <para>The frame pacing in Display.Refresh sleeps out whatever is left of the frame
    /// interval, and Thread.Sleep cannot wake earlier than the system timer tick. That tick
    /// defaults to about 15.6ms on Windows, so EVERY sleep shorter than that lasts 15.6ms:
    /// measured here, Sleep(1ms) took 15.64ms and Sleep(14.3ms) took 15.93ms.</para>
    /// <para>At fifty frames a second the interval is 20ms and the work is nearer 6, so the
    /// driver asks for a ~14ms sleep and is given ~16 -- the frame lands at 21.6ms and the demo
    /// runs at 46fps instead of 50. It gets worse as the work grows: at 16ms of work the
    /// leftover 4ms sleep still costs a full tick, which is 32ms a frame and 31fps. The same
    /// binary under WSL reaches 50, because a Linux tick is about a millisecond.</para>
    /// <para>So this is not a micro-optimisation, it is the difference between hitting the
    /// requested frame rate and missing it by a quarter. The resolution is process-wide while
    /// held and given back in Dispose, which is why the flag is tracked rather than the call
    /// simply repeated.</para>
    /// </remarks>
    private static bool RaiseTimerResolution()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            /* 0 is TIMERR_NOERROR. Anything else means the period was refused, and
             * timeEndPeriod must NOT then be called for it. */
            return timeBeginPeriod(TimerResolutionMs) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

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

    private const uint TimerResolutionMs = 1;

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern uint timeEndPeriod(uint uPeriod);

    private const int StdOutputHandle = -11;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);

}
