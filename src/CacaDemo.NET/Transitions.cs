/*
 *  CacaDemo.NET  various demo effects for libcaca, ported to .NET
 *  Ported from cacademo.c (WTFPL); see Program.cs for the full notice.
 */

using Caca;

namespace CacaDemo;

/// <summary>
/// Wipes drawn into the mask canvas that reveals the incoming demo.
/// <paramref name="completed"/> runs 0..100 over the transition.
/// </summary>
internal static class Transitions
{
    public const int Circle = 0;
    public const int Star = 1;
    public const int Square = 2;
    public const int VLines = 3;
    public const int HLines = 4;
    public const int Count = 5;

    /* Vertices of a five-pointed star and of a square, as unit coordinates. */
    private static readonly float[] StarShape =
    [
         0.000000f, -1.000000f,
         0.308000f, -0.349000f,
         0.992000f, -0.244000f,
         0.500000f,  0.266000f,
         0.632000f,  0.998000f,
         0.008000f,  0.659000f,
        -0.601000f,  0.995000f,
        -0.496000f,  0.275000f,
        -0.997000f, -0.244000f,
        -0.313000f, -0.349000f,
    ];

    private static readonly float[] SquareShape =
    [
        -1f, -1f,
         1f, -1f,
         1f,  1f,
        -1f,  1f,
    ];

    private static readonly float[] StarRot = new float[StarShape.Length];
    private static readonly float[] SquareRot = new float[SquareShape.Length];

    public static void Draw(Canvas mask, int tmode, int completed)
    {
        int w = mask.Width, h = mask.Height;

        float mulx = 0.0075f * completed * w;
        float muly = 0.0075f * completed * h;
        int w2 = w / 2;
        int h2 = h / 2;
        /* The original uses 3.14 rather than M_PI here; kept for fidelity. */
        float angle = (float)((0.0075f * completed * 360) * 3.14 / 180);

        switch (tmode)
        {
            case Square:
                Rotate(SquareShape, SquareRot, angle);
                mulx *= 1.8f;
                muly *= 1.8f;
                Tri(mask, SquareRot, 0, 1, 2, mulx, muly, w2, h2);
                Tri(mask, SquareRot, 0, 2, 3, mulx, muly, w2, h2);
                break;

            case Star:
                Rotate(StarShape, StarRot, angle);
                mulx *= 1.8f;
                muly *= 1.8f;
                Tri(mask, StarRot, 0, 1, 9, mulx, muly, w2, h2);
                Tri(mask, StarRot, 1, 2, 3, mulx, muly, w2, h2);
                Tri(mask, StarRot, 3, 4, 5, mulx, muly, w2, h2);
                Tri(mask, StarRot, 5, 6, 7, mulx, muly, w2, h2);
                Tri(mask, StarRot, 7, 8, 9, mulx, muly, w2, h2);
                Tri(mask, StarRot, 9, 1, 5, mulx, muly, w2, h2);
                Tri(mask, StarRot, 9, 5, 7, mulx, muly, w2, h2);
                Tri(mask, StarRot, 1, 3, 5, mulx, muly, w2, h2);
                break;

            case Circle:
                mask.FillEllipse(w2, h2, (int)mulx, (int)muly, '#');
                break;

            case VLines:
                for (int i = 0; i < 8; i++)
                {
                    int z = ((i & 1) != 0 ? w : -w / 2) * (100 - completed) / 100;
                    mask.FillBox(i * w / 8, z, (w / 8) + 1, z + h, '#');
                }
                break;

            case HLines:
                for (int i = 0; i < 6; i++)
                {
                    int z = ((i & 1) != 0 ? w : -w / 2) * (100 - completed) / 100;
                    mask.FillBox(z, i * h / 6, z + w, (h / 6) + 1, '#');
                }
                break;
        }
    }

    private static void Rotate(float[] src, float[] dst, float angle)
    {
        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);

        for (int i = 0; i < src.Length / 2; i++)
        {
            float x = src[i * 2];
            float y = src[i * 2 + 1];

            dst[i * 2] = (float)(x * cos - y * sin);
            dst[i * 2 + 1] = (float)(y * cos + x * sin);
        }
    }

    private static void Tri(Canvas mask, float[] rot, int a, int b, int c,
                            float mulx, float muly, int w2, int h2)
    {
        /* The C original truncates the whole "coord * mul + center" expression
         * at the call boundary, so the cast has to go outside the addition. */
        mask.FillTriangle((int)(rot[a * 2] * mulx + w2), (int)(rot[a * 2 + 1] * muly + h2),
                          (int)(rot[b * 2] * mulx + w2), (int)(rot[b * 2 + 1] * muly + h2),
                          (int)(rot[c * 2] * mulx + w2), (int)(rot[c * 2 + 1] * muly + h2),
                          '#');
    }
}
