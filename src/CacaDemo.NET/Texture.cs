/*
 *  CacaDemo.NET  various demo effects for libcaca, ported to .NET
 *  Ported from cacademo.c (WTFPL); see Program.cs for the full notice.
 */

using System.Reflection;

namespace CacaDemo;

/// <summary>
/// The 256x256 image the rotozoom spins. libcaca ships it as a generated C
/// header (src/texture.h); here the same pixels live in an embedded blob of
/// little-endian 0x00RRGGBB words.
/// </summary>
internal static class Texture
{
    public const int Size = 256;

    private static readonly Lazy<uint[]> Lazy = new(Load);

    public static uint[] Pixels => Lazy.Value;

    private static uint[] Load()
    {
        using Stream? stream = Assembly.GetExecutingAssembly()
                                       .GetManifestResourceStream("texture.bin")
            ?? throw new InvalidOperationException("Embedded resource 'texture.bin' is missing.");

        var bytes = new byte[Size * Size * sizeof(uint)];
        stream.ReadExactly(bytes);

        var pixels = new uint[Size * Size];
        Buffer.BlockCopy(bytes, 0, pixels, 0, bytes.Length);
        return pixels;
    }
}
