/*
 *  Caca.NET      a managed port of libcaca's canvas, dithering and terminal output
 *  Ported from libcaca's attr.c (WTFPL); see Colors.cs for the full notice.
 */

namespace Caca;

/// <summary>
/// Packing and unpacking of libcaca's 32-bit cell attribute, laid out from
/// MSB to LSB as: 3 bits background alpha, 4 red, 4 green, 3 blue; then the
/// same four fields for the foreground; then 4 style bits.
/// </summary>
public static class Attr
{
    /* RGB colours for the ANSI palette, as 4-4-4-4 ARGB. libcaca uses
     * gnome-terminal's values; brown is deliberately 0xfa50, not 0xfaa0. */
    internal static readonly ushort[] AnsiTab16 =
    [
        0xf000, 0xf00a, 0xf0a0, 0xf0aa, 0xfa00, 0xfa0a, 0xfa50, 0xfaaa,
        0xf555, 0xf55f, 0xf5f5, 0xf5ff, 0xff55, 0xff5f, 0xfff5, 0xffff,
    ];

    /* The same palette packed as 3-4-4-3, which is how a colour field is laid
     * out inside an attribute. Used for nearest-colour matching. */
    internal static readonly ushort[] AnsiTab14 =
    [
        0x3800, 0x3805, 0x3850, 0x3855, 0x3d00, 0x3d05, 0x3d28, 0x3d55,
        0x3aaa, 0x3aaf, 0x3afa, 0x3aff, 0x3faa, 0x3faf, 0x3ffa, 0x3fff,
    ];

    /// <summary>Builds an attribute from a pair of ANSI colour indices.</summary>
    public static uint FromAnsi(int fg, int bg, AnsiStyle style = AnsiStyle.None)
    {
        return ((uint)(bg | 0x40) << 18) | ((uint)(fg | 0x40) << 4) | ((uint)style & 0x0f);
    }

    /// <summary>The 12-bit RGB foreground colour carried by an attribute.</summary>
    public static ushort ToRgb12Fg(uint attr)
    {
        uint fg = (attr >> 4) & 0x3fff;

        if (fg < (0x10 | 0x40))
            return (ushort)(AnsiTab16[fg ^ 0x40] & 0x0fff);

        if (fg == ((uint)AnsiColor.Default | 0x40) || fg == ((uint)AnsiColor.Transparent | 0x40))
            return (ushort)(AnsiTab16[(int)AnsiColor.LightGray] & 0x0fff);

        return (ushort)((fg << 1) & 0x0fff);
    }

    /// <summary>The 12-bit RGB background colour carried by an attribute.</summary>
    public static ushort ToRgb12Bg(uint attr)
    {
        uint bg = attr >> 18;

        if (bg < (0x10 | 0x40))
            return (ushort)(AnsiTab16[bg ^ 0x40] & 0x0fff);

        if (bg == ((uint)AnsiColor.Default | 0x40) || bg == ((uint)AnsiColor.Transparent | 0x40))
            return (ushort)(AnsiTab16[(int)AnsiColor.Black] & 0x0fff);

        return (ushort)((bg << 1) & 0x0fff);
    }

    /// <summary>
    /// The ANSI foreground index, for drivers that can only emit the sixteen
    /// ANSI colours. May also return <see cref="AnsiColor.Default"/> or
    /// <see cref="AnsiColor.Transparent"/>.
    /// </summary>
    public static int ToAnsiFg(uint attr) => NearestAnsi((ushort)((attr >> 4) & 0x3fff));

    /// <inheritdoc cref="ToAnsiFg"/>
    public static int ToAnsiBg(uint attr) => NearestAnsi((ushort)(attr >> 18));

    /// <summary>Style bits carried by an attribute.</summary>
    public static AnsiStyle ToStyle(uint attr) => (AnsiStyle)(attr & 0x0f);

    /// <summary>
    /// Maps a raw 14-bit colour field onto an ANSI index. Plain ANSI colours
    /// and the two special values pass straight through; true-colour fields
    /// fall back to a nearest-neighbour search.
    /// </summary>
    private static int NearestAnsi(ushort argb14)
    {
        if (argb14 < (0x10 | 0x40))
            return argb14 ^ 0x40;

        if (argb14 == ((int)AnsiColor.Default | 0x40) ||
            argb14 == ((int)AnsiColor.Transparent | 0x40))
            return argb14 ^ 0x40;

        if (argb14 < 0x0fff)    /* too transparent to be worth matching */
            return (int)AnsiColor.Transparent;

        int best = (int)AnsiColor.Default;
        int dist = 0x3fff;

        for (int i = 0; i < 16; i++)
        {
            int d = 0;

            int a = (AnsiTab14[i] >> 7) & 0xf;
            int b = (argb14 >> 7) & 0xf;
            d += (a - b) * (a - b);

            a = (AnsiTab14[i] >> 3) & 0xf;
            b = (argb14 >> 3) & 0xf;
            d += (a - b) * (a - b);

            a = (AnsiTab14[i] << 1) & 0xf;
            b = (argb14 << 1) & 0xf;
            d += (a - b) * (a - b);

            if (d < dist)
            {
                dist = d;
                best = i;
            }
        }

        return best;
    }
}
