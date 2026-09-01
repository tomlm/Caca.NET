/*
 *  CacaDemo.NET  various demo effects for libcaca, ported to .NET
 *  Ported from cacademo.c (WTFPL); see Program.cs for the full notice.
 */

using Caca;
using static CacaDemo.Sizes;

namespace CacaDemo.Demos;

/// <summary>Additively blended blobs wandering on Lissajous-ish paths.</summary>
internal sealed class Metaballs : IDemo
{
    private const int METASIZE = XSIZ / 2;
    private const int METABALLS = 12;
    private const int CROPBALL = 200;   /* Colour index where to crop balls */

    private static readonly byte[] Ball = new byte[METASIZE * METASIZE];

    private readonly uint[] _r = new uint[256];
    private readonly uint[] _g = new uint[256];
    private readonly uint[] _b = new uint[256];
    private readonly uint[] _a = new uint[256];

    private readonly float[] _dd = new float[METABALLS];
    private readonly float[] _di = new float[METABALLS];
    private readonly float[] _dj = new float[METABALLS];
    private readonly float[] _dk = new float[METABALLS];
    private readonly int[] _x = new int[METABALLS];
    private readonly int[] _y = new int[METABALLS];

    private readonly double[] _offset = new double[360 + 80];

    private float _i = 10.0f, _j = 17.0f, _k = 11.0f;
    private int _angleoff;

    private byte[]? _screen;
    private Dither? _dither;

    public void Prepare(Canvas cv)
    {
        /* Make the palette eatable by libcaca */
        Array.Clear(_r);
        Array.Clear(_g);
        Array.Clear(_b);
        Array.Clear(_a);
        _r[255] = _g[255] = _b[255] = 0xfff;

        CreateBall();

        for (int n = 0; n < METABALLS; n++)
        {
            _dd[n] = Libcaca.Rand(0, 100);
            _di[n] = Libcaca.Rand(500, 4000) / 6000.0f;
            _dj[n] = Libcaca.Rand(500, 4000) / 6000.0f;
            _dk[n] = Libcaca.Rand(500, 4000) / 6000.0f;
        }

        _angleoff = Libcaca.Rand(0, 360);

        for (int n = 0; n < 360 + 80; n++)
            _offset[n] = 1.0 + Math.Sin(n * Math.PI / 60);
    }

    public void Init(Canvas cv)
    {
        _screen = new byte[XSIZ * YSIZ];
        /* Create a dither smaller than the pixel buffer, so that we display
         * only the interesting part of it */
        _dither = new Dither(8, XSIZ - METASIZE, YSIZ - METASIZE, XSIZ, 0, 0, 0, 0);
    }

    public void Update(Canvas cv, int frame)
    {
        int angle = (frame + _angleoff) % 360;

        /* Crop the palette */
        for (int n = CROPBALL; n < 255; n++)
        {
            double c1 = _offset[angle];
            double c2 = _offset[angle + 40];
            double c3 = _offset[angle + 80];

            int t1 = n < 0x40 ? 0 : n < 0xc0 ? (n - 0x40) * 0x20 : 0xfff;
            int t2 = n < 0xe0 ? 0 : (n - 0xe0) * 0x80;
            int t3 = n < 0x40 ? n * 0x40 : 0xfff;

            _r[n] = (uint)((c1 * t1 + c2 * t2 + c3 * t3) / 4);
            _g[n] = (uint)((c1 * t2 + c2 * t3 + c3 * t1) / 4);
            _b[n] = (uint)((c1 * t3 + c2 * t1 + c3 * t2) / 4);
        }

        /* Set the palette */
        _dither!.SetPalette(_r, _g, _b, _a);

        /* Silly paths for our balls */
        for (int n = 0; n < METABALLS; n++)
        {
            float u = _di[n] * _i + _dj[n] * _j + _dk[n] * (float)Math.Sin(_di[n] * _k);
            float v = _dd[n] + _di[n] * _j + _dj[n] * _k + _dk[n] * (float)Math.Sin(_dk[n] * _i);
            u = (float)(Math.Sin(_i + u * 2.1) * (1.0 + Math.Sin(u)));
            v = (float)(Math.Sin(_j + v * 1.9) * (1.0 + Math.Sin(v)));

            /* |u| and |v| stay within 2, so these land in 0..XSIZ-METASIZE.
             * Clamp anyway: in C an out-of-range value merely scribbles past
             * the buffer, here it would throw. */
            _x[n] = Math.Clamp((int)((XSIZ - METASIZE) / 2 + u * (XSIZ - METASIZE) / 4), 0, XSIZ - METASIZE);
            _y[n] = Math.Clamp((int)((YSIZ - METASIZE) / 2 + v * (YSIZ - METASIZE) / 4), 0, YSIZ - METASIZE);
        }

        _i += 0.011f;
        _j += 0.017f;
        _k += 0.019f;

        Array.Clear(_screen!);

        for (int n = 0; n < METABALLS; n++)
            DrawBall(_screen!, _x[n], _y[n]);
    }

    public void Render(Canvas cv)
    {
        /* Skip half a ball into the buffer so the clipped border never shows. */
        int offset = (METASIZE / 2) * (1 + XSIZ);
        cv.DitherBitmap(0, 0, cv.Width, cv.Height, _dither!, _screen.AsSpan(offset));
    }

    public void Free()
    {
        _screen = null;
        _dither = null;
    }

    private static void CreateBall()
    {
        for (int y = 0; y < METASIZE; y++)
            for (int x = 0; x < METASIZE; x++)
            {
                float distance = ((METASIZE / 2) - x) * ((METASIZE / 2) - x)
                               + ((METASIZE / 2) - y) * ((METASIZE / 2) - y);
                distance = (float)Math.Sqrt(distance) * 64 / METASIZE;
                /* (255 - distance) * 15 reaches ~3825 and is truncated into a
                 * uint8_t in C, so the 8-bit wraparound is what produces the
                 * gradient. Keep the float maths and wrap the same way. */
                Ball[x + y * METASIZE] =
                    distance > 15 ? (byte)0 : unchecked((byte)(int)((255 - distance) * 15));
            }
    }

    private static void DrawBall(byte[] screen, int bx, int by)
    {
        int b = (by * XSIZ) + bx;
        int e = 0;

        for (int i = 0; i < METASIZE * METASIZE; i++)
        {
            int color = screen[b] + Ball[i];

            if (color > 255)
                color = 255;

            screen[b] = (byte)color;

            if (e == METASIZE)
            {
                e = 0;
                b += XSIZ - METASIZE;
            }

            b++;
            e++;
        }
    }
}
