/*
 *  CacaDemo.NET  various demo effects for libcaca, ported to .NET
 *  Ported from cacademo.c (WTFPL); see Program.cs for the full notice.
 *
 *  As in the C original, this effect is built but left out of the rotation.
 */

using Caca;

namespace CacaDemo.Demos;

/// <summary>Langton's ants, each leaving a fading trail in its own colour.</summary>
internal sealed class Langton : IDemo
{
    private const int ANTS = 15;
    private const int ITER = 2;

    private static readonly char[] Gradient =
    [
        ' ', ' ', '.', '.', ':', ':', 'x', 'x',
        'X', 'X', '&', '&', 'W', 'W', '@', '@',
    ];

    private static readonly int[,] Steps = { { 0, 1 }, { 1, 0 }, { 0, -1 }, { -1, 0 } };

    private readonly int[] _ax = new int[ANTS];
    private readonly int[] _ay = new int[ANTS];
    private readonly int[] _dir = new int[ANTS];

    private byte[]? _screen;
    private int _width, _height;

    public void Prepare(Canvas cv)
    {
        _width = cv.Width;
        _height = cv.Height;

        for (int i = 0; i < ANTS; i++)
        {
            _ax[i] = CacaNet.Rand(0, _width);
            _ay[i] = CacaNet.Rand(0, _height);
            _dir[i] = CacaNet.Rand(0, 4);
        }
    }

    public void Init(Canvas cv)
    {
        _screen = new byte[_width * _height];
    }

    public void Update(Canvas cv, int frame)
    {
        byte[] screen = _screen!;

        for (int i = 0; i < ITER; i++)
        {
            for (int x = 0; x < _width * _height; x++)
            {
                byte p = screen[x];
                if ((p & 0x0f) > 1)
                    screen[x] = (byte)(p - 1);
            }

            for (int a = 0; a < ANTS; a++)
            {
                byte p = screen[_ax[a] + _width * _ay[a]];

                if ((p & 0x0f) != 0)
                {
                    _dir[a] = (_dir[a] + 1) % 4;
                    screen[_ax[a] + _width * _ay[a]] = (byte)(a << 4);
                }
                else
                {
                    _dir[a] = (_dir[a] + 3) % 4;
                    screen[_ax[a] + _width * _ay[a]] = (byte)((a << 4) | 0x0f);
                }

                _ax[a] = (_width + _ax[a] + Steps[_dir[a], 0]) % _width;
                _ay[a] = (_height + _ay[a] + Steps[_dir[a], 1]) % _height;
            }
        }
    }

    public void Render(Canvas cv)
    {
        byte[] screen = _screen!;

        for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++)
            {
                byte p = screen[x + _width * y];

                if ((p & 0x0f) != 0)
                    cv.SetColorAnsi((int)AnsiColor.White, p >> 4);
                else
                    cv.SetColorAnsi(AnsiColor.Black, AnsiColor.Black);

                cv.PutChar(x, y, Gradient[p & 0x0f]);
            }
    }

    public void Free()
    {
        _screen = null;
    }
}
