/*
 *  LibCaca       a managed port of libcaca's canvas, dithering and terminal output
 *  Ported from libcaca's canvas.c (WTFPL); see Colors.cs for the full notice.
 */

namespace Caca;

/// <summary>Bits of libcaca that do not belong to any object.</summary>
public static class Libcaca
{
    private static readonly Random Rng = new();

    /// <summary>
    /// A random integer in [<paramref name="min"/>, <paramref name="max"/>),
    /// matching caca_rand's half-open range.
    /// </summary>
    public static int Rand(int min, int max)
    {
        if (max <= min)
            return min;

        lock (Rng)
            return Rng.Next(min, max);
    }

    /// <summary>The version of this managed port.</summary>
    public static string Version => "libcaca 0.99.beta20 (managed port)";
}
