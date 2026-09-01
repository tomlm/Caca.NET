/*
 *  CacaDemo.NET  various demo effects for libcaca, ported to .NET
 *  Ported from cacademo.c (WTFPL); see Program.cs for the full notice.
 */

using Caca;

namespace CacaDemo.Demos;

/// <summary>Falling columns of glyphs, brightest at the head.</summary>
internal sealed class Matrix : IDemo
{
    private const int MAXDROPS = 500;
    private const int MINLEN = 15;
    private const int MAXLEN = 30;

    private struct Drop
    {
        public int X, Y, Speed, Len;
        public char[] Str;
    }

    private readonly Drop[] _drop = new Drop[MAXDROPS];

    public void Prepare(Canvas cv)
    {
        for (int i = 0; i < MAXDROPS; i++)
        {
            _drop[i].X = Libcaca.Rand(0, 1000);
            _drop[i].Y = Libcaca.Rand(0, 1000);
            _drop[i].Speed = 5 + Libcaca.Rand(0, 30);
            _drop[i].Len = MINLEN + Libcaca.Rand(0, MAXLEN - MINLEN);
            _drop[i].Str = new char[MAXLEN];

            for (int j = 0; j < MAXLEN; j++)
                _drop[i].Str[j] = (char)Libcaca.Rand('0', 'z');
        }
    }

    public void Init(Canvas cv)
    {
    }

    public void Update(Canvas cv, int frame)
    {
        int w = cv.Width, h = cv.Height;

        for (int i = 0; i < MAXDROPS && i < (w * h / 32); i++)
        {
            _drop[i].Y += _drop[i].Speed;

            if (_drop[i].Y > 1000)
            {
                _drop[i].Y -= 1000;
                _drop[i].X = Libcaca.Rand(0, 1000);
            }
        }
    }

    public void Render(Canvas cv)
    {
        int w = cv.Width, h = cv.Height;

        cv.SetColorAnsi(AnsiColor.Black, AnsiColor.Black);
        cv.Clear();

        for (int i = 0; i < MAXDROPS && i < (w * h / 32); i++)
        {
            int x = _drop[i].X * w / 1000 / 2 * 2;
            int y = _drop[i].Y * (h + MAXLEN) / 1000;

            for (int j = 0; j < _drop[i].Len; j++)
            {
                AnsiColor fg;

                if (j < 2)
                    fg = AnsiColor.White;
                else if (j < _drop[i].Len / 4)
                    fg = AnsiColor.LightGreen;
                else if (j < _drop[i].Len * 4 / 5)
                    fg = AnsiColor.Green;
                else
                    fg = AnsiColor.DarkGray;

                cv.SetColorAnsi(fg, AnsiColor.Black);

                /* y - j goes negative near the top of a drop. C's % would hand
                 * back a negative index and read out of bounds; use a floored
                 * modulo so the glyph just wraps within the drop instead. */
                int k = (y - j) % _drop[i].Len;
                if (k < 0)
                    k += _drop[i].Len;

                cv.PutChar(x, y - j, _drop[i].Str[k]);
            }
        }
    }

    public void Free()
    {
    }
}
