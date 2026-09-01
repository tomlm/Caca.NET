/*
 *  LibCaca       a managed port of libcaca's canvas, dithering and terminal output
 *  Ported from libcaca's event.c (WTFPL); see Colors.cs for the full notice.
 */

namespace Caca;

/// <summary>Kinds of event a display can report. Usable as a filter mask.</summary>
[Flags]
public enum EventType
{
    None = 0x0000,
    KeyPress = 0x0001,
    KeyRelease = 0x0002,
    MousePress = 0x0004,
    MouseRelease = 0x0008,
    MouseMotion = 0x0010,
    Resize = 0x0020,
    Quit = 0x0040,
    Any = 0xffff,
}

/// <summary>Key codes libcaca reports for keys that have no character.</summary>
public enum EventKey
{
    Unknown = 0x00,

    CtrlA = 0x01, CtrlB = 0x02, CtrlC = 0x03, CtrlD = 0x04,
    CtrlE = 0x05, CtrlF = 0x06, CtrlG = 0x07, Backspace = 0x08,
    Tab = 0x09, CtrlJ = 0x0a, CtrlK = 0x0b, CtrlL = 0x0c,
    Return = 0x0d, CtrlN = 0x0e, CtrlO = 0x0f, CtrlP = 0x10,
    CtrlQ = 0x11, CtrlR = 0x12, Pause = 0x13, CtrlT = 0x14,
    CtrlU = 0x15, CtrlV = 0x16, CtrlW = 0x17, CtrlX = 0x18,
    CtrlY = 0x19, CtrlZ = 0x1a, Escape = 0x1b, Delete = 0x7f,

    Up = 0x111, Down = 0x112, Left = 0x113, Right = 0x114,

    Insert = 0x115, Home = 0x116, End = 0x117,
    PageUp = 0x118, PageDown = 0x119,

    F1 = 0x11a, F2 = 0x11b, F3 = 0x11c, F4 = 0x11d,
    F5 = 0x11e, F6 = 0x11f, F7 = 0x120, F8 = 0x121,
    F9 = 0x122, F10 = 0x123, F11 = 0x124, F12 = 0x125,
}

/// <summary>One input event. Which fields are meaningful depends on <see cref="Type"/>.</summary>
public readonly struct Event
{
    private Event(EventType type, int keyCh, int width, int height)
    {
        Type = type;
        KeyCh = keyCh;
        Width = width;
        Height = height;
    }

    public EventType Type { get; }

    /// <summary>For key events, the character or <see cref="EventKey"/> code.</summary>
    public int KeyCh { get; }

    /// <summary>For resize events, the new canvas width.</summary>
    public int Width { get; }

    /// <summary>For resize events, the new canvas height.</summary>
    public int Height { get; }

    public static Event None => default;

    public static Event Key(int ch) => new(EventType.KeyPress, ch, 0, 0);

    public static Event Resized(int width, int height) =>
        new(EventType.Resize, 0, width, height);

    public static Event Quit() => new(EventType.Quit, 0, 0, 0);
}
