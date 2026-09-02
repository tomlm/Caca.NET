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
}
