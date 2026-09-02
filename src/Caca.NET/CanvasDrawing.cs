/*
 *  Caca.NET      a managed port of libcaca's canvas, dithering and terminal output
 *  Ported from libcaca's box.c, triangle.c, conic.c and line.c (WTFPL);
 *  see Colors.cs for the full notice.
 */

namespace Caca;

public sealed partial class Canvas
{
    /// <summary>Draws a line between two points using Bresenham's algorithm.</summary>
    public void DrawLine(int x1, int y1, int x2, int y2, int ch)
    {
        int dx = Math.Abs(x2 - x1);
        int dy = Math.Abs(y2 - y1);
        int sx = x1 < x2 ? 1 : -1;
        int sy = y1 < y2 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            PutChar(x1, y1, ch);

            if (x1 == x2 && y1 == y2)
                break;

            int e2 = err * 2;

            if (e2 > -dy)
            {
                err -= dy;
                x1 += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                y1 += sy;
            }
        }
    }

    /// <summary>Fills an axis-aligned box.</summary>
    public void FillBox(int x, int y, int w, int h, int ch)
    {
        int x2 = x + w - 1;
        int y2 = y + h - 1;

        if (x > x2)
            (x, x2) = (x2, x);

        if (y > y2)
            (y, y2) = (y2, y);

        int xmax = Width - 1;
        int ymax = Height - 1;

        if (x2 < 0 || y2 < 0 || x > xmax || y > ymax)
            return;

        if (x < 0) x = 0;
        if (y < 0) y = 0;
        if (x2 > xmax) x2 = xmax;
        if (y2 > ymax) y2 = ymax;

        for (int j = y; j <= y2; j++)
        {
            int row = j * Width;
            _chars.AsSpan(row + x, x2 - x + 1).Fill(ch);
            _attrs.AsSpan(row + x, x2 - x + 1).Fill(_curattr);
        }
    }

    /// <summary>
    /// Fills a triangle. Scanline rasteriser with the vertices sorted by y and
    /// the edge slopes held in 16.16 fixed point, as in libcaca.
    /// </summary>
    public void FillTriangle(int x1, int y1, int x2, int y2, int x3, int y3, int ch)
    {
        /* Sort so that y1 <= y2 <= y3. */
        if (y1 > y2)
        {
            FillTriangle(x2, y2, x1, y1, x3, y3, ch);
            return;
        }

        if (y2 > y3)
        {
            FillTriangle(x1, y1, x3, y3, x2, y2, ch);
            return;
        }

        int sl21 = y2 == y1 ? 0 : (x2 - x1) * 0x10000 / (y2 - y1);
        int sl31 = y3 == y1 ? 0 : (x3 - x1) * 0x10000 / (y3 - y1);
        int sl32 = y3 == y2 ? 0 : (x3 - x2) * 0x10000 / (y3 - y2);

        x1 *= 0x10000;
        x2 *= 0x10000;
        x3 *= 0x10000;

        int ymin = y1 < 0 ? 0 : y1;
        int ymax = y3 + 1 < Height ? y3 + 1 : Height;

        int xa, xb;

        if (ymin < y2)
        {
            xa = x1 + sl21 * (ymin - y1);
            xb = x1 + sl31 * (ymin - y1);
        }
        else if (ymin == y2)
        {
            xa = x2;
            xb = y1 == y3 ? x3 : x1 + sl31 * (ymin - y1);
        }
        else
        {
            xa = x3 + sl32 * (ymin - y3);
            xb = x3 + sl31 * (ymin - y3);
        }

        for (int y = ymin; y < ymax; y++)
        {
            /* Rescale xa and xb, recentering the division. */
            int xx1, xx2;

            if (xa < xb)
            {
                xx1 = (xa + 0x800) / 0x10000;
                xx2 = (xb + 0x801) / 0x10000;
            }
            else
            {
                xx1 = (xb + 0x800) / 0x10000;
                xx2 = (xa + 0x801) / 0x10000;
            }

            int xmin = xx1 < 0 ? 0 : xx1;
            int xlimit = xx2 + 1 < Width ? xx2 + 1 : Width;

            for (int x = xmin; x < xlimit; x++)
                PutChar(x, y, ch);

            xa += y < y2 ? sl21 : sl32;
            xb += sl31;
        }
    }

    /// <summary>Fills an ellipse of semi-axes <paramref name="a"/> and <paramref name="b"/>.</summary>
    public void FillEllipse(int xo, int yo, int a, int b, int ch)
    {
        int x = 0;
        int y = b;
        int d1 = b * b - (a * a * b) + (a * a / 4);

        while (a * a * y - a * a / 2 > b * b * (x + 1))
        {
            if (d1 < 0)
            {
                /* libcaca notes that "Computer Graphics" has + 3 here. */
                d1 += b * b * (2 * x + 1);
            }
            else
            {
                d1 += b * b * (2 * x * 1) + a * a * (-2 * y + 2);
                DrawLine(xo - x, yo - y, xo + x, yo - y, ch);
                DrawLine(xo - x, yo + y, xo + x, yo + y, ch);
                y--;
            }

            x++;
        }

        DrawLine(xo - x, yo - y, xo + x, yo - y, ch);
        DrawLine(xo - x, yo + y, xo + x, yo + y, ch);

        int d2 = (int)(b * b * (x + 0.5) * (x + 0.5) + a * a * (y - 1) * (y - 1) - a * a * b * b);

        while (y > 0)
        {
            if (d2 < 0)
            {
                d2 += b * b * (2 * x + 2) + a * a * (-2 * y + 3);
                x++;
            }
            else
            {
                d2 += a * a * (-2 * y + 3);
            }

            y--;
            DrawLine(xo - x, yo - y, xo + x, yo - y, ch);
            DrawLine(xo - x, yo + y, xo + x, yo + y, ch);
        }
    }
}
