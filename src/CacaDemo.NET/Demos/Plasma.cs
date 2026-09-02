/*
 *  CacaDemo.NET  various demo effects for libcaca, ported to .NET
 *  Ported from cacademo.c (WTFPL); see Program.cs for the full notice.
 */

using Caca;
using static CacaDemo.Sizes;

namespace CacaDemo.Demos;

/// <summary>Three sine-distance fields summed together and run through a cycling palette.</summary>
internal sealed class Plasma : IDemo
{
    private const int TABLEX = XSIZ * 2;
    private const int TABLEY = YSIZ * 2;

    private static readonly byte[] Table = new byte[TABLEX * TABLEY];

    private readonly uint[] _red = new uint[256];
    private readonly uint[] _green = new uint[256];
    private readonly uint[] _blue = new uint[256];
    private readonly uint[] _alpha = new uint[256];
    private readonly double[] _r = new double[3];
    private readonly double[] _bigR = new double[6];

    private byte[]? _screen;
    private Dither? _dither;

    public void Prepare(Canvas cv)
    {
        /* Fill various tables */
        Array.Clear(_red);
        Array.Clear(_green);
        Array.Clear(_blue);
        Array.Clear(_alpha);

        for (int i = 0; i < 3; i++)
            _r[i] = (double)CacaNet.Rand(1, 1000) / 60000 * Math.PI;

        for (int i = 0; i < 6; i++)
            _bigR[i] = (double)CacaNet.Rand(1, 1000) / 10000;

        for (int y = 0; y < TABLEY; y++)
            for (int x = 0; x < TABLEX; x++)
            {
                /* Note the original squares (y - TABLEX / 2), not TABLEY; since
                 * the table is square it makes no difference, but keep it. */
                double tmp = (double)((x - (TABLEX / 2)) * (x - (TABLEX / 2))
                                    + (y - (TABLEX / 2)) * (y - (TABLEX / 2)))
                             * (Math.PI / ((double)TABLEX * TABLEX + (double)TABLEY * TABLEY));

                Table[x + y * TABLEX] = (byte)((1.0 + Math.Sin(12.0 * Math.Sqrt(tmp))) * 256 / 6);
            }
    }

    public void Init(Canvas cv)
    {
        _screen = new byte[XSIZ * YSIZ];
        _dither = Dither.Indexed8(XSIZ, YSIZ);
    }

    public void Update(Canvas cv, int frame)
    {
        for (int i = 0; i < 256; i++)
        {
            double z = (double)i / 256 * 6 * Math.PI;

            _red[i] = (uint)((1.0 + Math.Sin(z + _r[1] * frame)) / 2 * 0xfff);
            _blue[i] = (uint)((1.0 + Math.Cos(z + _r[0] * (frame + 100))) / 2 * 0xfff);
            _green[i] = (uint)((1.0 + Math.Cos(z + _r[2] * (frame + 200))) / 2 * 0xfff);
        }

        /* Set the palette */
        _dither!.SetPalette(_red, _green, _blue, _alpha);

        DoPlasma(_screen!,
                 (1.0 + Math.Sin(frame * _bigR[0])) / 2,
                 (1.0 + Math.Sin(frame * _bigR[1])) / 2,
                 (1.0 + Math.Sin(frame * _bigR[2])) / 2,
                 (1.0 + Math.Sin(frame * _bigR[3])) / 2,
                 (1.0 + Math.Sin(frame * _bigR[4])) / 2,
                 (1.0 + Math.Sin(frame * _bigR[5])) / 2);
    }

    public void Render(Canvas cv)
    {
        cv.DitherBitmap(0, 0, cv.Width, cv.Height, _dither!, _screen!);
    }

    public void Free()
    {
        _screen = null;
        _dither = null;
    }

    private static void DoPlasma(byte[] pixels, double x1, double y1,
                                 double x2, double y2, double x3, double y3)
    {
        int o1 = (int)(x1 * (TABLEX / 2)) + (int)(y1 * (TABLEY / 2)) * TABLEX;
        int o2 = (int)(x2 * (TABLEX / 2)) + (int)(y2 * (TABLEY / 2)) * TABLEX;
        int o3 = (int)(x3 * (TABLEX / 2)) + (int)(y3 * (TABLEY / 2)) * TABLEX;

        for (int y = 0; y < YSIZ; y++)
        {
            int dst = y * YSIZ;
            int ty = y * TABLEX;

            for (int x = 0; x < XSIZ; x++, ty++, dst++)
            {
                /* Deliberate 8-bit wraparound: that is where the banding comes from. */
                pixels[dst] = unchecked((byte)(Table[o1 + ty] + Table[o2 + ty] + Table[o3 + ty]));
            }
        }
    }
}
