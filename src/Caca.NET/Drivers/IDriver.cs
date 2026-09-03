/*
 *  Caca.NET      a managed port of libcaca's canvas, dithering and terminal output
 *  See Colors.cs for the full notice.
 */

namespace Caca.Drivers;

/// <summary>
/// An output backend. libcaca picks one of these at runtime from the
/// CACA_DRIVER environment variable, and so does this port; implement it to
/// paint somewhere else and hand your driver to
/// <see cref="Display(Canvas, IDriver)"/>.
/// </summary>
public interface IDriver : IDisposable
{
    /// <summary>Current output size, in character cells.</summary>
    int Width { get; }

    /// <summary>Current output size, in character cells.</summary>
    int Height { get; }

    /// <summary>Paints the canvas.</summary>
    void Refresh(Canvas canvas);

    /// <summary>
    /// Returns the next pending event, or <see cref="Event.None"/> if there is
    /// nothing to report.
    /// </summary>
    Event PollEvent();

    /// <summary>Sets the window title, where the backend supports one.</summary>
    void SetTitle(string title);

    /// <summary>
    /// How many frames the backend has declined to paint because the output could not keep up.
    /// </summary>
    /// <remarks>
    /// Zero for a backend that always paints what it is given, which is why this has a default
    /// rather than being required of every implementation. A backend that writes on its own thread
    /// reports what it skipped here, so a caller measuring frame rate can tell a demo that is
    /// genuinely running at fifty frames a second from one that is merely CLAIMING to while the
    /// terminal sees half of them.
    /// </remarks>
    long SkippedFrames => 0;

    /// <summary>
    /// How many frames have actually reached the output.
    /// </summary>
    /// <remarks>
    /// The other half of the story <see cref="SkippedFrames"/> tells. The demo's own rate says how
    /// fast the animation ADVANCES; this says how fast frames are DELIVERED — and under
    /// synchronised output a delivered frame is presented exactly once, so this is as close to a
    /// rendered-frames count as the writing side can ever know. A backend that paints everything
    /// it is given inline reports the same number as its refresh count.
    /// </remarks>
    long DeliveredFrames => 0;
}
