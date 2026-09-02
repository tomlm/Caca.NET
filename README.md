# CacaDemo.NET

A .NET port of [cacademo](https://github.com/cacalabs/libcaca/blob/main/src/cacademo.c),
the demo-effects program that ships with [libcaca](https://github.com/cacalabs/libcaca).
It cycles in order through a set of full-screen ASCII/ANSI effects, wiping
between them with randomly chosen transitions.

**There is no native dependency.** The parts of libcaca the demo needs — the
canvas, the drawing primitives, the bitmap dithering engine and a terminal
driver — are ported to managed C# in `src/Caca.NET`. It is pure IL: clone,
`dotnet run`, done, on Windows, Linux or macOS.

That port is published on its own as the **Caca.NET** package, so you can build
your own terminal graphics on it without any of the demo; see
[Using the library](#using-the-library).

## Effects

| Effect | Description |
| --- | --- |
| `Plasma` | Three sine-distance fields summed together under a cycling palette |
| `Metaballs` | Additively blended blobs wandering on Lissajous-like paths |
| `Moire` | Two XORed concentric-ring discs sliding over each other |
| `Matrix` | Falling columns of glyphs, brightest at the head |
| `Rotozoom` | A rotating, pulsing zoom over a 256x256 texture, in 24:8 fixed point |
| `Langton` | Langton's ants leaving fading trails — built, but as upstream, left out of the rotation |

Transitions: circle, star, square, vertical lines, horizontal lines.

## Controls

| Key | Action |
| --- | --- |
| `Space` | Pause / resume |
| `Right`, `Enter` | Skip forward to the next effect |
| `Left` | Skip back to the previous effect |
| `Esc`, `Ctrl-C`, `Ctrl-Z` | Quit |

## Building and running

```sh
dotnet build src/CacaDemo.NET.slnx
dotnet run --project src/CacaDemo.NET
```

With no arguments it cycles through the whole rotation in order. Naming an
effect runs just that one, standalone and without transitions; naming several
cycles through only those, in the order given:

```sh
dotnet run --project src/CacaDemo.NET -- --rotozoom
dotnet run --project src/CacaDemo.NET -- --plasma --matrix
```

| Option | Action |
| --- | --- |
| `--plasma`, `--metaballs`, `--moire`, `--matrix`, `--rotozoom`, `--langton` | Run that effect (`--langton` is otherwise left out of the rotation) |
| `--list` | List the effect names, one per line |
| `-h`, `--help` | Show usage |

Both `CACA_DRIVER` and `CACA_SYNC` apply to the demo as they do to anything
else built on the library; see *Drivers* below.

## Using the library

`Caca.NET` is the port on its own, with no dependency on the demo:

```sh
dotnet add package Caca.NET
```

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

`KeyCh` carries a character where the key has one, so `' '` and `''` compare
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

### Miscellany

`CacaNet.Rand(min, max)` is `caca_rand`, half-open like the original.
`CacaNet.Version` reports the libcaca release this port tracks.

## Layout

```
src/
  Caca.NET/             the managed port of libcaca
    Canvas.cs           cell grid, colours, blitting
    CanvasDrawing.cs    lines, boxes, triangles, ellipses
    CanvasDither.cs     bitmap -> coloured characters
    Dither.cs           pixel format, palette, glyph ramp
    Attr.cs             the 32-bit cell attribute and colour conversions
    Display.cs          frame pacing and event dispatch
    Rand.cs             caca_rand and the version string
    Drivers/            IDriver, the public extension point, plus
                        AnsiDriver (terminal) and NullDriver (headless)
  CacaDemo.NET/
    Program.cs          main loop: event handling, demo rotation, transitions
    Transitions.cs      the five wipes
    Texture.cs          loads the rotozoom texture from an embedded blob
    Demos/              one file per effect
```

## Notes on the port

### Verifying the library against the original

The dithering engine is the part where "close enough" would be visible, so it
was checked rather than eyeballed. A harness feeds nine deterministic cases —
8bpp with cycling palettes, the default grayscale ramp, 32bpp RGB, 32bpp with an
alpha channel, and several awkward canvas sizes including 1x1 — through both the
real `libcaca.so.0` and this port, dumping the character and the resolved
foreground and background of every cell. The two dumps are **byte-identical**
across all nine cases.

Two genuine bugs surfaced that way, both since fixed: a fresh canvas has to start
on `DEFAULT` over `TRANSPARENT` rather than light-gray-on-black, and
`nearest_ansi` has to pass those two special colour values straight through
instead of resolving them to the nearest real colour.

### Scope of the library

`Caca.NET` covers what cacademo exercises, not all of libcaca. In particular it
implements the default `full16` colour mode — the only one upstream fully
honours anyway — and treats every cell as single-width, since nothing here draws
fullwidth CJK glyphs. Brightness and contrast are accepted and reported back but
do nothing, matching upstream, where both setters are marked `FIXME`.

Only the ANSI terminal and the headless null backend ship in the box, but
`IDriver` is public, so anything else you want to paint on is a class away.

The only `DllImport` left anywhere is to `kernel32` on Windows, to turn on
escape-sequence processing in the legacy console. That is an OS call, not a
library dependency.

### Synchronized output

A frame is a few thousand escape sequences, and a terminal that repaints while
they are still arriving shows a torn canvas. Private mode 2026 fixes that: the
terminal holds its presentation between `CSI ? 2026 h` and `CSI ? 2026 l`, so
the frame appears whole.

`AnsiDriver` asks for it rather than assuming it, with DECRQM — `CSI ? 2026 $ p`
— at startup. A terminal that knows the mode replies `CSI ? 2026 ; Ps $ y`, and
a `Ps` of 1 (set) or 2 (reset) means we can use it; 0 means it cannot. The
awkward case is a terminal that implements no DECRQM at all and so says nothing,
which would leave us waiting out a timeout for every startup. A Primary Device
Attributes request (`CSI c`, not `ESC c` — that is RIS, a terminal reset)
therefore rides along behind the query: every
terminal answers that one, and its reply is the signal that no 2026 answer is
coming. Keystrokes that arrive while we wait are queued rather than dropped.

If a terminal supports the mode but not the query, `CACA_SYNC=1` forces it on.

### Notes on the effects

The effects follow the C closely, including its deliberate 8-bit wraparound and
fixed-point overflow. Two places needed care because C's out-of-bounds and
negative-index behaviour would throw in .NET rather than quietly scribble: ball
positions in `Metaballs` and disc offsets in `Moire` are clamped to the range
their buffers were sized for, and `Matrix` uses a floored modulo when indexing a
drop's glyphs above the top of the screen.

libcaca ships the rotozoom texture as a generated C header of 65,536 integer
literals. Rather than paste that into a `.cs` file, it is converted to
`texture.bin`, an embedded blob of little-endian `0x00RRGGBB` words.

## Licence

cacademo and libcaca are distributed under the [WTFPL](http://www.wtfpl.net/),
and so is this port. See `LICENSE`.
