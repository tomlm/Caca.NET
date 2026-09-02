![Icon](https://raw.githubusercontent.com/tomlm/Caca.NET/main/icon.png)

# CacaDemo.NET

This is a dotnet port of [cacademo](https://github.com/cacalabs/libcaca/blob/main/src/cacademo.c), the demo-effects program that ships with libcaca, which cycles in order through a
set of full-screen ASCII/ANSI effects, wiping between them with randomly chosen transitions, written using [Caca.NET](https://github.com/tomlm/caca.net).

![Caca](https://raw.githubusercontent.com/tomlm/Caca.NET/main/caca.gif)

## Installation

```dotnet tool install CacaDemo.NET```

## Run

```CacaDemo.NET```

## Effects

| Effect        | Description                                                  |
| ------------- | ------------------------------------------------------------ |
| `--Plasma`    | Three sine-distance fields summed together under a cycling palette |
| `--Metaballs` | Additively blended blobs wandering on Lissajous-like paths   |
| `--Moire`     | Two XORed concentric-ring discs sliding over each other      |
| --`Matrix`    | Falling columns of glyphs, brightest at the head             |
| `--Rotozoom`  | A rotating, pulsing zoom over a 256x256 texture, in 24:8 fixed point |
| `--Langton`   | Langton's ants leaving fading trails — built, but as upstream, left out of the rotation |

Transitions: circle, star, square, vertical lines, horizontal lines.

## Controls

| Key                       | Action                           |
| ------------------------- | -------------------------------- |
| `Space`                   | Pause / resume                   |
| `Right`, `Enter`          | Skip forward to the next effect  |
| `Left`                    | Skip back to the previous effect |
| `Esc`, `Ctrl-C`, `Ctrl-Z` | Quit                             |

## Licence

cacademo and libcaca are distributed under the [WTFPL](http://www.wtfpl.net/), and so is this port. See `LICENSE`.
