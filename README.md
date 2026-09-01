# CacaDemo.NET

A .NET port of [cacademo](https://github.com/cacalabs/libcaca/blob/main/src/cacademo.c),
the demo-effects program that ships with [libcaca](https://github.com/cacalabs/libcaca).
It cycles in order through a set of full-screen ASCII/ANSI effects, wiping
between them with randomly chosen transitions.

**There is no native dependency.** The parts of libcaca the demo needs — the
canvas, the drawing primitives, the bitmap dithering engine and a terminal
driver — are ported to managed C# in `src/LibCaca`. It is pure IL: clone,
`dotnet run`, done, on Windows, Linux or macOS.

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

`CACA_DRIVER` picks the output backend, as it does for libcaca:

| Value | Behaviour |
| --- | --- |
| `ansi` | ANSI escape sequences on the terminal (the default when stdout is a TTY) |
| `null` | Render nothing; useful headless, for benchmarks and tests |

## Layout

```
src/
  LibCaca/              the managed port of libcaca
    Canvas.cs           cell grid, colours, blitting
    CanvasDrawing.cs    lines, boxes, triangles, ellipses
    CanvasDither.cs     bitmap -> coloured characters
    Dither.cs           pixel format, palette, glyph ramp
    Attr.cs             the 32-bit cell attribute and colour conversions
    Display.cs          frame pacing and event dispatch
    Drivers/            AnsiDriver (terminal), NullDriver (headless)
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

`LibCaca` covers what cacademo exercises, not all of libcaca. In particular it
implements the default `full16` colour mode — the only one upstream fully
honours anyway — and treats every cell as single-width, since nothing here draws
fullwidth CJK glyphs. Brightness and contrast are accepted and reported back but
do nothing, matching upstream, where both setters are marked `FIXME`.

The only `DllImport` left anywhere is to `kernel32` on Windows, to turn on
escape-sequence processing in the legacy console. That is an OS call, not a
library dependency.

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
