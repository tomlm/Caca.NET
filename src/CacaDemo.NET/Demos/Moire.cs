/*
 *  CacaDemo.NET  various demo effects for libcaca, ported to .NET
 *  Ported from cacademo.c (WTFPL); see Program.cs for the full notice.
 */

using Caca;
using static CacaDemo.Sizes;

namespace CacaDemo.Demos;

/// <summary>Two XORed concentric-ring discs sliding over each other.</summary>
internal sealed class Moire : IDemo
{
    private const int DISCSIZ = XSIZ * 2;
    private const int DISCTHICKNESS = XSIZ * 15 / 40;

    private static readonly byte[] Disc = new byte[DISCSIZ * DISCSIZ];

    private readonly float[] _d = new float[6];
    private readonly uint[] _red = new uint[256];
    private readonly uint[] _green = new uint[256];
    private readonly uint[] _blue = new uint[256];
    private readonly uint[] _alpha = new uint[256];

    private byte[]? _screen;
    private Dither? _dither;

    public void Prepare(Canvas cv)
    {
        /* Fill various tables */
        Array.Clear(_red);
        Array.Clear(_green);
        Array.Clear(_blue);
        Array.Clear(_alpha);

        for (int i = 0; i < 6; i++)
            _d[i] = CacaNet.Rand(50, 70) / 1000.0f;

        _red[0] = _green[0] = _blue[0] = 0x777;
        _red[1] = _green[1] = _blue[1] = 0xfff;

        /* Fill the circle */
        for (int i = DISCSIZ * 2; i > 0; i -= DISCTHICKNESS)
        {
            int t = 0, dx = 0, dy = i;

            while (dx <= dy)
            {
                byte color = (byte)((i / DISCTHICKNESS) % 2);
                DrawLine(dx / 3, dy / 3, color);
                DrawLine(dy / 3, dx / 3, color);

                /* t += t > 0 ? dx - dy-- : dx; -- the post-decrement of dy only
                 * happens on the t > 0 branch. */
                if (t > 0)
                {
                    t += dx - dy;
                    dy--;
                }
                else
                {
                    t += dx;
                }

                dx++;
            }
        }
    }

    public void Init(Canvas cv)
    {
        _screen = new byte[XSIZ * YSIZ];
        _dither = Dither.Indexed8(XSIZ, YSIZ);
    }

    public void Update(Canvas cv, int frame)
    {
        Array.Clear(_screen!);

        /* Set the palette */
        _red[0] = (uint)(0.5 * (1 + Math.Sin(_d[0] * (frame + 1000))) * 0xfff);
        _green[0] = (uint)(0.5 * (1 + Math.Cos(_d[1] * frame)) * 0xfff);
        _blue[0] = (uint)(0.5 * (1 + Math.Cos(_d[2] * (frame + 3000))) * 0xfff);

        _red[1] = (uint)(0.5 * (1 + Math.Sin(_d[3] * (frame + 2000))) * 0xfff);
        _green[1] = (uint)(0.5 * (1 + Math.Cos(_d[4] * frame + 5.0)) * 0xfff);
        _blue[1] = (uint)(0.5 * (1 + Math.Cos(_d[5] * (frame + 4000))) * 0xfff);

        _dither!.SetPalette(_red, _green, _blue, _alpha);

        /* Draw circles */
        /* Truncate the whole expression, as the C assignment to int does. */
        int x = (int)(Math.Cos(_d[0] * (frame + 1000)) * 128.0 + (XSIZ / 2));
        int y = (int)(Math.Sin(0.11 * frame) * 128.0 + (YSIZ / 2));
        PutDisc(_screen!, x, y);

        x = (int)(Math.Cos(0.13 * frame + 2.0) * 64.0 + (XSIZ / 2));
        y = (int)(Math.Sin(_d[1] * (frame + 2000)) * 64.0 + (YSIZ / 2));
        PutDisc(_screen!, x, y);
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

    private static void PutDisc(byte[] screen, int x, int y)
    {
        /* The disc buffer is exactly large enough for x and y in 0..DISCSIZ/2;
         * clamp so a rounding wobble can't walk off the end. */
        x = Math.Clamp(x, 0, DISCSIZ / 2);
        y = Math.Clamp(y, 0, DISCSIZ / 2);

        int src = (DISCSIZ / 2 - x) + (DISCSIZ / 2 - y) * DISCSIZ;

        for (int j = 0; j < YSIZ; j++)
            for (int i = 0; i < XSIZ; i++)
                screen[i + XSIZ * j] ^= Disc[src + i + DISCSIZ * j];
    }

    private static void DrawLine(int x, int y, byte color)
    {
        if (x == 0 || y == 0 || y > DISCSIZ / 2)
            return;

        if (x > DISCSIZ / 2)
            x = DISCSIZ / 2;

        int len = 2 * x - 1;
        Disc.AsSpan((DISCSIZ / 2) - x + DISCSIZ * ((DISCSIZ / 2) - y), len).Fill(color);
        Disc.AsSpan((DISCSIZ / 2) - x + DISCSIZ * ((DISCSIZ / 2) + y - 1), len).Fill(color);
    }
}
