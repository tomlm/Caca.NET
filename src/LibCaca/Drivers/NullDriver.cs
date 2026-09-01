/*
 *  LibCaca       a managed port of libcaca's canvas, dithering and terminal output
 *  See Colors.cs for the full notice.
 */

namespace Caca.Drivers;

/// <summary>
/// Renders nothing. Useful for running headless, for benchmarking the effects
/// without the terminal in the way, and for tests. Selected with CACA_DRIVER=null.
/// </summary>
internal sealed class NullDriver : IDriver
{
    public NullDriver(int width = 80, int height = 24)
    {
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    public void Refresh(Canvas canvas)
    {
    }

    public Event PollEvent() => Event.None;

    public void SetTitle(string title)
    {
    }

    public void Dispose()
    {
    }
}
