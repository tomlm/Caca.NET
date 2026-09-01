/*
 *  CacaDemo.NET  various demo effects for libcaca, ported to .NET
 *  Ported from cacademo.c (WTFPL); see Program.cs for the full notice.
 */

using System.Runtime.InteropServices;
using Caca;
using static CacaDemo.Sizes;

namespace CacaDemo.Demos;

/// <summary>A rotating, pulsing zoom over a tiled 256x256 texture, in 24:8 fixed point.</summary>
internal sealed class Rotozoom : IDemo
{
    private const int TEXTURE_SIZE = 256;
    private const int TABLE_SIZE = 65536;
    private const int PRECISION = 8;

    private static int Fmul(int a, int b) => (a * b) >> PRECISION;
    private static int ToFix(double d) => (int)(d * (1 << PRECISION));

    private readonly uint[] _screen = new uint[XSIZ * YSIZ];
    private readonly int[] _cosTab = new int[TABLE_SIZE];
    private readonly int[] _sinTab = new int[TABLE_SIZE];
    private readonly int[] _yTab = new int[TEXTURE_SIZE];

    private int _alphaF;
    private int _tF;

    private Dither? _dither;

    public void Prepare(Canvas cv)
    {
        for (int x = 0; x < TABLE_SIZE; x++)
        {
            /* Not radians: the original scales by 360/TABLE_SIZE and feeds that
             * straight to cos/sin, which is what gives the fast spin. */
            float angle = x * (360.0f / TABLE_SIZE);
            _cosTab[x] = ToFix(Math.Cos(angle));
            _sinTab[x] = ToFix(Math.Sin(angle));
        }

        for (int x = 0; x < TEXTURE_SIZE; x++)
            _yTab[x] = x * TEXTURE_SIZE;    /* start of lines offsets */
    }

    public void Init(Canvas cv)
    {
        _dither = new Dither(32, XSIZ, YSIZ, XSIZ * 4,
                             0x00FF0000, 0x0000FF00, 0x000000FF, 0x00000000);
    }

    public void Update(Canvas cv, int frame)
    {
        uint[] texture = Texture.Pixels;

        unchecked
        {
            _alphaF += 4;
            _tF += 3;

            int scaleF = Fmul(_sinTab[_tF & 0xFFFF], ToFix(3)) + ToFix(4);
            uint xxF = (uint)Fmul(_cosTab[_alphaF & 0xFFFF], scaleF);
            uint yyF = (uint)Fmul(_sinTab[_alphaF & 0xFFFF], scaleF);

            uint uF = 0, vF = 0, uF_ = 0, vF_ = 0;
            int p = 0;

            for (int y = YSIZ; y-- > 0;)
            {
                for (int x = XSIZ; x-- > 0;)
                {
                    uF += xxF;
                    vF += yyF;

                    uint vu = (uF >> PRECISION) & 0xFF;
                    uint vv = (vF >> PRECISION) & 0xFF;

                    _screen[p++] = texture[vu + _yTab[vv]];
                }

                uF = uF_ -= yyF;
                vF = vF_ += xxF;
            }
        }
    }

    public void Render(Canvas cv)
    {
        /* The dither reads 32bpp words; hand it the buffer as raw bytes. */
        cv.DitherBitmap(0, 0, cv.Width, cv.Height, _dither!,
                        MemoryMarshal.AsBytes<uint>(_screen));
    }

    public void Free()
    {
        _dither = null;
    }
}
