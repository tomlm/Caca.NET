/*
 *  Caca.NET      a managed port of libcaca's canvas, dithering and terminal output
 *  Ported from libcaca's graphics.c (WTFPL); see Colors.cs for the full notice.
 */

using System.Diagnostics;
using Caca.Drivers;

namespace Caca;

/// <summary>
/// Attaches a canvas to an output backend, paces the frame rate, and hands
/// back input events.
/// </summary>
public sealed class Display : IDisposable
{
    private readonly IDriver _driver;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private long _nextFrameTicks;
    private bool _disposed;

    /// <summary>Creates a display for a new, driver-sized canvas.</summary>
    public Display() : this(new Canvas())
    {
    }

    /// <summary>
    /// Creates a display for the given canvas on the driver named by
    /// CACA_DRIVER, resizing the canvas to fit the output.
    /// </summary>
    public Display(Canvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        /* Built after the null check: constructing a driver takes over the
         * terminal, which would be rude to do and then throw. */
        Canvas = canvas;
        _driver = CreateDriver();
        Canvas.Resize(_driver.Width, _driver.Height);
    }

    /// <summary>
    /// Creates a display on a driver of your own, ignoring CACA_DRIVER. The
    /// display takes ownership: disposing it disposes the driver.
    /// </summary>
    public Display(Canvas canvas, IDriver driver)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(driver);

        Canvas = canvas;
        _driver = driver;
        Canvas.Resize(driver.Width, driver.Height);
    }

    /// <summary>The canvas this display paints.</summary>
    public Canvas Canvas { get; }

    /// <summary>
    /// Minimum time between refreshes, in microseconds. <see cref="Refresh"/>
    /// waits out whatever is left of it. Zero runs flat out.
    /// </summary>
    public int DisplayTime { get; set; }

    /// <summary>Sets the window title where the backend has one.</summary>
    public string Title
    {
        set => _driver.SetTitle(value);
    }

    /// <summary>Paints the canvas, then waits out the rest of the frame interval.</summary>
    public void Refresh()
    {
        _driver.Refresh(Canvas);

        if (DisplayTime <= 0)
            return;

        long interval = DisplayTime * Stopwatch.Frequency / 1_000_000;
        long now = _clock.ElapsedTicks;

        if (_nextFrameTicks == 0)
            _nextFrameTicks = now;

        _nextFrameTicks += interval;

        long remaining = _nextFrameTicks - now;

        if (remaining > 0)
        {
            var delay = TimeSpan.FromTicks(remaining * TimeSpan.TicksPerSecond / Stopwatch.Frequency);
            Thread.Sleep(delay);
        }
        else
        {
            /* We are behind; give up on catching up rather than spinning. */
            _nextFrameTicks = now;
        }
    }

    /// <summary>
    /// Returns the next pending event matching <paramref name="mask"/>, or
    /// <see cref="Event.None"/> when the queue is empty. Resize events are
    /// applied to the canvas whether or not the caller asked for them.
    /// </summary>
    public Event GetEvent(EventType mask)
    {
        while (true)
        {
            Event ev = _driver.PollEvent();

            if (ev.Type == EventType.None)
                return Event.None;

            /* The canvas has to follow the terminal even if the caller is only
             * listening for keys. */
            if (ev.Type == EventType.Resize)
                Canvas.Resize(ev.Width, ev.Height);

            if ((ev.Type & mask) != 0)
                return ev;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _driver.Dispose();
    }

    private static IDriver CreateDriver()
    {
        string? name = Environment.GetEnvironmentVariable("CACA_DRIVER");

        return name switch
        {
            "null" or "none" => new NullDriver(),
            "ansi" or "ncurses" or "slang" or "terminfo" => new AnsiDriver(),
            null or "" => Console.IsOutputRedirected ? new NullDriver() : new AnsiDriver(),
            _ => throw new NotSupportedException(
                $"Unknown CACA_DRIVER '{name}'. This port supports 'ansi' and 'null'."),
        };
    }
}
