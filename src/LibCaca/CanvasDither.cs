/*
 *  LibCaca       a managed port of libcaca's canvas, dithering and terminal output
 *  Ported from libcaca's dither.c (WTFPL); see Colors.cs for the full notice.
 */

namespace Caca;

public sealed partial class Canvas
{
    /* Error-diffusion rows, kept between calls so a 50fps render loop is not
     * allocating three arrays a frame. Indexed [i + 1] so that index -1, which
     * the diffusion step writes to, stays in bounds. */
    private int[] _fsR = [];
    private int[] _fsG = [];
    private int[] _fsB = [];

    /// <summary>
    /// Renders a bitmap into the given rectangle as coloured characters, using
    /// Floyd-Steinberg error diffusion.
    /// </summary>
    /// <remarks>
    /// This implements libcaca's default "full16" colour mode: for every cell a
    /// background and a foreground are chosen from the 16 ANSI colours, then the
    /// glyph whose ink coverage best matches the cell's average colour.
    /// </remarks>
    public void DitherBitmap(int x, int y, int w, int h, Dither d, ReadOnlySpan<byte> pixels)
    {
        ArgumentNullException.ThrowIfNull(d);

        if (pixels.IsEmpty)
            return;

        uint savedAttr = _curattr;

        int x1 = x, x2 = x + w - 1;
        int y1 = y, y2 = y + h - 1;

        int dw = d.Width;
        int dh = d.Height;
        int deltax = x2 - x1 + 1;
        int deltay = y2 - y1 + 1;
        int dchmax = d.Glyphs.Length;

        if (deltax <= 0 || deltay <= 0 || dw <= 0 || dh <= 0)
            return;

        /* Denominator for the glyph ramp: coverage runs 0..2*dchmax-1. */
        int scale = 2 * dchmax - 1;

        int fsLength = (Width <= x2 ? Width : x2) + 1;
        EnsureErrorRows(fsLength + 2);

        int[] fsR = _fsR, fsG = _fsG, fsB = _fsB;
        Array.Clear(fsR);
        Array.Clear(fsG);
        Array.Clear(fsB);

        int[] glyphs = d.Glyphs;
        int[] palette = Dither.RgbPalette;

        for (y = y1 > 0 ? y1 : 0; y <= y2 && y <= Height; y++)
        {
            int remainR = 0, remainG = 0, remainB = 0;

            for (x = x1 > 0 ? x1 : 0; x <= x2 && x <= Width; x++)
            {
                int r = 0, g = 0, b = 0, a = 0;

                int fromx = (int)((long)(x - x1) * dw / deltax);
                int fromy = (int)((long)(y - y1) * dh / deltay);
                int tox = (int)((long)(x - x1 + 1) * dw / deltax);
                int toy = (int)((long)(y - y1 + 1) * dh / deltay);

                if (d.Antialias)
                {
                    /* We want at least one pixel. */
                    if (tox == fromx) tox++;
                    if (toy == fromy) toy++;

                    int dots = 0;

                    for (int myx = fromx; myx < tox; myx++)
                        for (int myy = fromy; myy < toy; myy++)
                        {
                            dots++;
                            GetRgba(d, pixels, myx, myy, ref r, ref g, ref b, ref a);
                        }

                    r /= dots;
                    g /= dots;
                    b /= dots;
                    a /= dots;
                }
                else
                {
                    GetRgba(d, pixels, (fromx + tox) / 2, (fromy + toy) / 2,
                            ref r, ref g, ref b, ref a);
                }

                if (d.HasAlpha && a < 0x800)
                {
                    remainR = remainG = remainB = 0;
                    fsR[x + 1] = 0;
                    fsG[x + 1] = 0;
                    fsB[x + 1] = 0;
                    continue;
                }

                r += remainR;
                g += remainG;
                b += remainB;

                /* Pick the background: nearest of the 16 ANSI colours. */
                int outbg = 0, distmin = int.MaxValue;

                for (int i = 0; i < 16; i++)
                {
                    int dr = r - palette[i * 3];
                    int dg = g - palette[i * 3 + 1];
                    int db = b - palette[i * 3 + 2];
                    int dist = dr * dr + dg * dg + db * db;

                    if (dist < distmin)
                    {
                        outbg = i;
                        distmin = dist;
                    }
                }

                int bgR = palette[outbg * 3];
                int bgG = palette[outbg * 3 + 1];
                int bgB = palette[outbg * 3 + 2];

                /* Pick the foreground: nearest of the remaining fifteen. */
                int outfg = 0;
                distmin = int.MaxValue;

                for (int i = 0; i < 16; i++)
                {
                    if (i == outbg)
                        continue;

                    int dr = r - palette[i * 3];
                    int dg = g - palette[i * 3 + 1];
                    int db = b - palette[i * 3 + 2];
                    int dist = dr * dr + dg * dg + db * db;

                    if (dist < distmin)
                    {
                        outfg = i;
                        distmin = dist;
                    }
                }

                int fgR = palette[outfg * 3];
                int fgG = palette[outfg * 3 + 1];
                int fgB = palette[outfg * 3 + 2];

                /* Pick the glyph whose ink coverage best mixes the two. */
                int ch = 0;
                distmin = int.MaxValue;

                for (int i = 0; i < dchmax - 1; i++)
                {
                    int newr = i * fgR + (scale - i) * bgR;
                    int newg = i * fgG + (scale - i) * bgG;
                    int newb = i * fgB + (scale - i) * bgB;
                    int dist = Math.Abs(r * scale - newr)
                             + Math.Abs(g * scale - newg)
                             + Math.Abs(b * scale - newb);

                    if (dist < distmin)
                    {
                        ch = i;
                        distmin = dist;
                    }
                }

                int errorR = r - (fgR * ch + bgR * (scale - ch)) / scale;
                int errorG = g - (fgG * ch + bgG * (scale - ch)) / scale;
                int errorB = b - (fgB * ch + bgB * (scale - ch)) / scale;

                /* Spread the error over the neighbours, 7/3/5/1 sixteenths.
                 * The +1 on every index is the offset that keeps x-1 in range. */
                remainR = fsR[x + 2] + 7 * errorR / 16;
                remainG = fsG[x + 2] + 7 * errorG / 16;
                remainB = fsB[x + 2] + 7 * errorB / 16;
                fsR[x] += 3 * errorR / 16;
                fsG[x] += 3 * errorG / 16;
                fsB[x] += 3 * errorB / 16;
                fsR[x + 1] = 5 * errorR / 16;
                fsG[x + 1] = 5 * errorG / 16;
                fsB[x + 1] = 5 * errorB / 16;
                fsR[x + 2] = 1 * errorR / 16;
                fsG[x + 2] = 1 * errorG / 16;
                fsB[x + 2] = 1 * errorB / 16;

                if (d.Invert)
                {
                    outfg = 15 - outfg;
                    outbg = 15 - outbg;
                }

                SetColorAnsi(outfg, outbg);
                PutChar(x, y, glyphs[ch]);
            }
        }

        _curattr = savedAttr;
    }

    private void EnsureErrorRows(int length)
    {
        if (_fsR.Length >= length)
            return;

        _fsR = new int[length];
        _fsG = new int[length];
        _fsB = new int[length];
    }

    /// <summary>Reads one source pixel and accumulates its 12-bit components.</summary>
    private static void GetRgba(Dither d, ReadOnlySpan<byte> pixels, int x, int y,
                                ref int r, ref int g, ref int b, ref int a)
    {
        int bytesPerPixel = d.Bpp / 8;
        int offset = bytesPerPixel * x + d.Pitch * y;

        if (offset < 0 || offset + bytesPerPixel > pixels.Length)
            return;

        uint bits;

        switch (bytesPerPixel)
        {
            case 4:
                bits = BitConverter.ToUInt32(pixels.Slice(offset, 4));
                break;
            case 3:
                bits = BitConverter.IsLittleEndian
                    ? ((uint)pixels[offset + 2] << 16) | ((uint)pixels[offset + 1] << 8) | pixels[offset]
                    : ((uint)pixels[offset] << 16) | ((uint)pixels[offset + 1] << 8) | pixels[offset + 2];
                break;
            case 2:
                bits = BitConverter.ToUInt16(pixels.Slice(offset, 2));
                break;
            default:
                bits = pixels[offset];
                break;
        }

        if (d.HasPalette)
        {
            int i = (int)(bits & 0xff);
            r += d.GammaTab[d.Red[i]];
            g += d.GammaTab[d.Green[i]];
            b += d.GammaTab[d.Blue[i]];
            a += d.Alpha[i];
        }
        else
        {
            r += d.GammaTab[((bits & d.RMask) >> d.RRight) << d.RLeft];
            g += d.GammaTab[((bits & d.GMask) >> d.GRight) << d.GLeft];
            b += d.GammaTab[((bits & d.BMask) >> d.BRight) << d.BLeft];
            a += (int)(((bits & d.AMask) >> d.ARight) << d.ALeft);
        }
    }
}
