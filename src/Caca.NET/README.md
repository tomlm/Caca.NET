![Icon](https://raw.githubusercontent.com/tomlm/Caca.NET/main/icon.png)

# Caca.NET

A  cross platform .NET port of [cacalabs/libcaca: 💩 Colour ASCII Art Library](https://github.com/cacalabs/libcaca).

**There is no native dependency.** It is pure IL — clone, `dotnet run`, done, on **Windows**, **Linux** or **macOS**.

## Installation

```#sh
dotnet add package Caca.NET
```

## Usage

Everything public lives in the `Caca` namespace, apart from the backend
extension point in `Caca.Drivers`. A whole program:

```csharp
using Caca;

using Display dp = new();          /* owns a canvas sized to the terminal */
Canvas cv = dp.Canvas;
dp.Title = "sample";
dp.DisplayTime = 20000;            /* microseconds per frame; 0 runs flat out */

while (true)
{
    Event ev = dp.GetEvent(EventType.KeyPress | EventType.Quit);
    if (ev.Type == EventType.Quit || ev.KeyCh == (int)EventKey.Escape)
        break;

    cv.SetColorAnsi(AnsiColor.LightGray, AnsiColor.Black);
    cv.Clear();
    cv.SetColorAnsi(AnsiColor.White, AnsiColor.Blue);
    cv.FillBox(2, 1, 24, 3, ' ');
    cv.PutStr(4, 2, $"{cv.Width}x{cv.Height}");
    cv.DrawLine(0, 0, cv.Width - 1, cv.Height - 1, '*');
    dp.Refresh();
}
```

### Canvas

A grid of cells, each one character plus a 32-bit attribute. `new Canvas()`
starts empty and `new Canvas(w, h)` starts sized; `Width` and `Height` are
read-only, changed through `Resize`. A canvas attached to a `Display` is
resized for you when the terminal changes.

| Member | |
| --- | --- |
| `PutChar(x, y, ch)`, `PutStr(x, y, s)` | Write cells in the current attribute. Anything off-canvas is silently dropped, as in libcaca |
| `GetChar(x, y)`, `GetAttr(x, y)` | Read one back |
| `Clear()` | Fill with spaces in the current attribute |
| `SetColorAnsi(fg, bg)` | The attribute later writes use, as `AnsiColor` or `int` |
| `CurrentAttr` | That same attribute, packed |
| `DrawLine`, `FillBox`, `FillEllipse`, `FillTriangle` | Primitives, each taking the character to draw with |
| `Blit(x, y, src, mask)` | Copy `src` in, but only the cells `mask` marks |
| `DitherBitmap(x, y, w, h, dither, pixels)` | Draw an image — see *Dither* below |

`Blit` is how the wipes work: draw a shape into a scratch canvas, then blit the
incoming frame through it as a mask.

### Display

Binds a canvas to an output backend, paces the frame rate and delivers input.
It is `IDisposable`, and disposing it is what puts the terminal back.

| Member | |
| --- | --- |
| `new Display()` | A display over a new canvas, sized to the output |
| `new Display(canvas)` | Adopts your canvas, resizing it to fit |
| `new Display(canvas, driver)` | Adopts a driver too, ignoring `CACA_DRIVER` |
| `Canvas` | The canvas being painted |
| `Refresh()` | Paint, then wait out the rest of the frame interval |
| `DisplayTime` | Minimum microseconds between refreshes; 0 runs flat out |
| `Title` | Window title, where the backend has one (set-only) |
| `GetEvent(mask)` | The next event matching `mask`, or `Event.None` |

`GetEvent` never blocks. Resize events are applied to the canvas whether or not
you asked for them, so `Canvas.Width` is always current.

### Events

`Event` is a struct: `Type`, `KeyCh` for key presses, and `Width`/`Height` for
resizes. `EventType` is a flags enum — `KeyPress`, `Quit`, `Resize`, `Any` and
the rest — used both as the `GetEvent` mask and as the event's own kind.

`KeyCh` carries a character where the key has one, so `' '` and `'
'` compare
directly. Keys that do not are the `EventKey` values: `Escape`, `Up`, `Down`,
`Left`, `Right`, `Home`, `End`, `PageUp`, `PageDown`, `F1`–`F12`, `Delete` and
the `Ctrl-`*x* codes.

### Colour

`AnsiColor` is the sixteen ANSI colours in libcaca's DOS order, plus `Default`
and `Transparent`. `AnsiStyle` is a flags enum of `Bold`, `Italics`,
`Underline` and `Blink`. The static `Attr` class packs and unpacks the 32-bit
cell attribute if you want to work with it directly: `FromAnsi`, `ToAnsiFg`,
`ToAnsiBg`, `ToRgb12Fg`, `ToRgb12Bg`, `ToStyle`.

### Dither

`Dither` describes how a bitmap becomes coloured characters: the pixel format
going in, the palette, and the glyph ramp coming out. Static factories cover the
usual formats, and each takes an optional `pitch` in bytes for rows that are not
tightly packed:

| Factory | Format |
| --- | --- |
| `Dither.Indexed8(w, h, pitch = 0)` | 8bpp through a palette, grayscale until you call `SetPalette` |
| `Dither.Rgb24(w, h, pitch = 0)` | 24bpp packed RGB |
| `Dither.Rgb32(w, h, pitch = 0)` | 32bpp `0x00RRGGBB` words, alpha ignored |
| `Dither.Argb32(w, h, pitch = 0)` | 32bpp `0xAARRGGBB` words, alpha honoured |

The constructor is still there for anything else:
`new Dither(bpp, width, height, pitch, rmask, gmask, bmask, amask)`, matching
`caca_create_dither`. Masks apply to the pixel as a native-endian word, so on a
little-endian machine `Rgb32` is the byte order B, G, R, unused.

Pixels reach `DitherBitmap` as a `ReadOnlySpan<byte>` whatever the format, so a
`uint[]` buffer goes in through `MemoryMarshal.AsBytes`.

| Member | |
| --- | --- |
| `SetPalette(red, green, blue, alpha)` | 256 entries each, 0–0xfff, 8bpp only |
| `SetCharset(name)` | `"ascii"` (the default), `"shades"` or `"blocks"` |
| `Antialias` | Average every source pixel under a cell. On by default |
| `Gamma`, `Invert` | As libcaca |
| `Brightness`, `Contrast` | Accepted and reported back, but do nothing — see *Scope of the library* |

### Drivers

`CACA_DRIVER` picks a built-in backend, exactly as it does for libcaca:

| Value | Behaviour |
| --- | --- |
| `ansi` | ANSI escape sequences on the terminal (the default when stdout is a TTY) |
| `null` | Render nothing; useful headless, for benchmarks and tests |

To paint somewhere else — an in-memory buffer for tests, a GUI, a recording —
implement `Caca.Drivers.IDriver` and hand it to `new Display(canvas, driver)`:

```csharp
public sealed class MyDriver : IDriver
{
    public int Width => 80;
    public int Height => 25;

    public void Refresh(Canvas canvas) { /* canvas.GetChar / GetAttr per cell */ }
    public Event PollEvent() => Event.None;      /* or Event.Key / Quit / Resized */
    public void SetTitle(string title) { }
    public void Dispose() { }
}
```

The display takes ownership: disposing the display disposes the driver.

`CACA_SYNC` overrides the synchronized-output detection described below: `0`
never emits it, `1` always does, and anything else leaves it to the query.

## Licence

cacademo and libcaca are distributed under the [WTFPL](http://www.wtfpl.net/), and so is this port. See `LICENSE`.
