/*
 *  Caca.NET      a managed port of libcaca's canvas, dithering and terminal output
 *  Ported from libcaca's dither.c (WTFPL); see Colors.cs for the full notice.
 */

namespace Caca;

/// <summary>
/// Describes how a bitmap is turned into coloured characters: the pixel
/// format on the way in, the palette, and the glyph ramp on the way out.
/// </summary>
public sealed class Dither
{
    /// <summary>The 16 ANSI colours as 12-bit RGB, used to pick fg/bg pairs.</summary>
    internal static readonly int[] RgbPalette =
    [
        0x0,   0x0,   0x0,
        0x0,   0x0,   0x7ff,
        0x0,   0x7ff, 0x0,
        0x0,   0x7ff, 0x7ff,
        0x7ff, 0x0,   0x0,
        0x7ff, 0x0,   0x7ff,
        0x7ff, 0x7ff, 0x0,
        0xaaa, 0xaaa, 0xaaa,
        0x555, 0x555, 0x555,
        0x000, 0x000, 0xfff,
        0x000, 0xfff, 0x000,
        0x000, 0xfff, 0xfff,
        0xfff, 0x000, 0x000,
        0xfff, 0x000, 0xfff,
        0xfff, 0xfff, 0x000,
        0xfff, 0xfff, 0xfff,
    ];

    /// <summary>Density ramp, lightest first.</summary>
    internal static readonly int[] AsciiGlyphs =
        [' ', '.', ':', ';', 't', '%', 'S', 'X', '@', '8', '?'];

    internal static readonly int[] ShadesGlyphs = [' ', 0xb7, 0x2591, 0x2592, '?'];

    internal static readonly int[] BlocksGlyphs = [' ', 0x2598, 0x259a, '?'];

    private readonly int[] _red = new int[256];
    private readonly int[] _green = new int[256];
    private readonly int[] _blue = new int[256];
    private readonly int[] _alpha = new int[256];
    private readonly int[] _gammatab = new int[4097];

    private float _gamma = 1.0f;

    public Dither(int bpp, int width, int height, int pitch,
                  uint rmask, uint gmask, uint bmask, uint amask)
    {
        if (bpp is < 8 or > 32)
            throw new ArgumentOutOfRangeException(nameof(bpp), "Bits per pixel must be 8-32.");

        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        ArgumentOutOfRangeException.ThrowIfNegative(pitch);

        Bpp = bpp;
        Width = width;
        Height = height;
        Pitch = pitch;

        RMask = rmask;
        GMask = gmask;
        BMask = bmask;
        AMask = amask;
        HasAlpha = amask != 0;

        if ((rmask | gmask | bmask | amask) != 0)
        {
            (RRight, RLeft) = MaskToShift(rmask);
            (GRight, GLeft) = MaskToShift(gmask);
            (BRight, BLeft) = MaskToShift(bmask);
            (ARight, ALeft) = MaskToShift(amask);
        }

        /* In 8bpp mode, default to a grayscale palette. */
        if (bpp == 8)
        {
            HasPalette = true;
            HasAlpha = false;

            for (int i = 0; i < 256; i++)
                _red[i] = _green[i] = _blue[i] = i * 0xfff / 256;
        }

        for (int i = 0; i < 4096; i++)
            _gammatab[i] = i;

        Glyphs = AsciiGlyphs;
    }

    /// <summary>
    /// An 8-bit indexed dither. The palette starts out grayscale; call
    /// <see cref="SetPalette"/> to supply your own. <paramref name="pitch"/>
    /// defaults to <paramref name="width"/>, one byte per pixel.
    /// </summary>
    public static Dither Indexed8(int width, int height, int pitch = 0) =>
        new(8, width, height, pitch > 0 ? pitch : width, 0, 0, 0, 0);

    /// <summary>
    /// A 24-bit dither over packed RGB triples. <paramref name="pitch"/>
    /// defaults to three bytes per pixel.
    /// </summary>
    public static Dither Rgb24(int width, int height, int pitch = 0) =>
        new(24, width, height, pitch > 0 ? pitch : width * 3,
            0xFF0000, 0x00FF00, 0x0000FF, 0);

    /// <summary>
    /// A 32-bit dither over 0x00RRGGBB words, alpha ignored — the layout a
    /// little-endian machine sees as B, G, R, unused. <paramref name="pitch"/>
    /// defaults to four bytes per pixel.
    /// </summary>
    public static Dither Rgb32(int width, int height, int pitch = 0) =>
        new(32, width, height, pitch > 0 ? pitch : width * 4,
            0x00FF0000, 0x0000FF00, 0x000000FF, 0);

    /// <summary>
    /// A 32-bit dither over 0xAARRGGBB words, alpha honoured. Otherwise as
    /// <see cref="Rgb32"/>.
    /// </summary>
    public static Dither Argb32(int width, int height, int pitch = 0) =>
        new(32, width, height, pitch > 0 ? pitch : width * 4,
            0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000);

    internal int Bpp { get; }
    internal int Width { get; }
    internal int Height { get; }
    internal int Pitch { get; }

    internal uint RMask { get; }
    internal uint GMask { get; }
    internal uint BMask { get; }
    internal uint AMask { get; }

    internal int RRight { get; }
    internal int GRight { get; }
    internal int BRight { get; }
    internal int ARight { get; }
    internal int RLeft { get; }
    internal int GLeft { get; }
    internal int BLeft { get; }
    internal int ALeft { get; }

    internal bool HasPalette { get; private set; }
    internal bool HasAlpha { get; private set; }

    internal int[] Red => _red;
    internal int[] Green => _green;
    internal int[] Blue => _blue;
    internal int[] Alpha => _alpha;
    internal int[] GammaTab => _gammatab;

    /// <summary>The glyph ramp, from lightest to darkest.</summary>
    internal int[] Glyphs { get; private set; }

    /// <summary>Averages every source pixel that falls under a cell. On by default.</summary>
    public bool Antialias { get; set; } = true;

    /// <summary>Swaps foreground and background indices on output.</summary>
    public bool Invert { get; set; }

    /// <summary>
    /// Brightness and contrast are accepted and reported back, but libcaca
    /// itself does not apply them (both setters are marked FIXME upstream),
    /// and neither does this port.
    /// </summary>
    public float Brightness { get; set; } = 1.0f;

    /// <inheritdoc cref="Brightness"/>
    public float Contrast { get; set; } = 1.0f;

    /// <summary>
    /// Gamma applied to incoming colour components. A negative value inverts
    /// the output as well; zero is rejected.
    /// </summary>
    public float Gamma
    {
        get => _gamma;
        set
        {
            if (value < 0.0f)
            {
                Invert = true;
                value = -value;
            }
            else if (value == 0.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Gamma must be non-zero.");
            }

            _gamma = value;

            /* libcaca hand-rolls a power function to avoid libm; Math.Pow is
             * the same curve, computed more accurately. */
            for (int i = 0; i < 4096; i++)
                _gammatab[i] = (int)(4096.0 * Math.Pow(i / 4096.0, 1.0 / value));
        }
    }

    /// <summary>Chooses the glyph ramp: "ascii", "shades" or "blocks".</summary>
    public void SetCharset(string name)
    {
        Glyphs = name switch
        {
            "ascii" or "default" => AsciiGlyphs,
            "shades" => ShadesGlyphs,
            "blocks" => BlocksGlyphs,
            _ => throw new ArgumentException($"Unknown charset '{name}'.", nameof(name)),
        };
    }

    /// <summary>
    /// Sets the palette of an 8bpp dither. Each array holds 256 entries in the
    /// range 0-0xfff.
    /// </summary>
    public void SetPalette(ReadOnlySpan<uint> red, ReadOnlySpan<uint> green,
                           ReadOnlySpan<uint> blue, ReadOnlySpan<uint> alpha)
    {
        if (Bpp != 8)
            throw new InvalidOperationException("A palette can only be set on an 8bpp dither.");

        if (red.Length < 256 || green.Length < 256 || blue.Length < 256 || alpha.Length < 256)
            throw new ArgumentException("Palette arrays must hold 256 entries.");

        for (int i = 0; i < 256; i++)
        {
            if ((red[i] | green[i] | blue[i] | alpha[i]) >= 0x1000)
                throw new ArgumentOutOfRangeException(nameof(red), "Palette values must be 0-0xfff.");
        }

        bool hasAlpha = false;

        for (int i = 0; i < 256; i++)
        {
            _red[i] = (int)red[i];
            _green[i] = (int)green[i];
            _blue[i] = (int)blue[i];

            if (alpha[i] != 0)
            {
                _alpha[i] = (int)alpha[i];
                hasAlpha = true;
            }
        }

        HasAlpha = hasAlpha;
    }

    /// <summary>
    /// Turns a channel mask into the right-shift that moves it to bit 0 and the
    /// left-shift that scales it up to 12 bits.
    /// </summary>
    private static (int Right, int Left) MaskToShift(uint mask)
    {
        if (mask == 0)
            return (0, 0);

        int right = 0;

        while ((mask & 1) == 0)
        {
            mask >>= 1;
            right++;
        }

        int bits = 0;

        while ((mask & 1) != 0)
        {
            mask >>= 1;
            bits++;
        }

        return (right, 12 - bits);
    }
}
