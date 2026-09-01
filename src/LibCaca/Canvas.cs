/*
 *  LibCaca       a managed port of libcaca's canvas, dithering and terminal output
 *  Ported from libcaca's canvas.c and string.c (WTFPL); see Colors.cs for the notice.
 */

using System.Text;

namespace Caca;

/// <summary>
/// A grid of character cells, each holding a UTF-32 codepoint and a 32-bit
/// attribute. Drawing primitives live in the other half of this class.
/// </summary>
/// <remarks>
/// libcaca tracks fullwidth (CJK) glyphs with a magic filler cell. Nothing in
/// this port draws them, so cells are treated as uniformly single-width.
/// </remarks>
public sealed partial class Canvas
{
    private int[] _chars = [];
    private uint[] _attrs = [];

    /// <summary>
    /// The attribute new characters are drawn with. A fresh canvas starts on
    /// the driver's default foreground over a transparent background, as
    /// caca_create_canvas() does.
    /// </summary>
    private uint _curattr = Attr.FromAnsi((int)AnsiColor.Default, (int)AnsiColor.Transparent);

    public Canvas() : this(0, 0) { }

    public Canvas(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        Resize(width, height);
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    internal int[] Chars => _chars;

    internal uint[] Attrs => _attrs;

    /// <summary>Current drawing attribute.</summary>
    public uint CurrentAttr
    {
        get => _curattr;
        set => _curattr = value;
    }

    /// <summary>
    /// Resizes the canvas, keeping whatever content still fits and filling any
    /// newly exposed cells with blanks in the current attribute.
    /// </summary>
    public void Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        if (width == Width && height == Height)
            return;

        var chars = new int[width * height];
        var attrs = new uint[width * height];

        chars.AsSpan().Fill(' ');
        attrs.AsSpan().Fill(_curattr);

        int copyW = Math.Min(width, Width);
        int copyH = Math.Min(height, Height);

        for (int y = 0; y < copyH; y++)
        {
            _chars.AsSpan(y * Width, copyW).CopyTo(chars.AsSpan(y * width, copyW));
            _attrs.AsSpan(y * Width, copyW).CopyTo(attrs.AsSpan(y * width, copyW));
        }

        _chars = chars;
        _attrs = attrs;
        Width = width;
        Height = height;
    }

    /// <summary>Sets the drawing colours from a pair of ANSI indices.</summary>
    public void SetColorAnsi(AnsiColor fg, AnsiColor bg) => SetColorAnsi((int)fg, (int)bg);

    /// <summary>Sets the drawing colours from a pair of ANSI indices.</summary>
    public void SetColorAnsi(int fg, int bg)
    {
        if ((uint)fg > 0x20 || (uint)bg > 0x20)
            throw new ArgumentOutOfRangeException(nameof(fg), "ANSI colour indices must be 0x00-0x20.");

        /* Keep the style nibble, replace both colours. */
        _curattr = (_curattr & 0x0000000f) | Attr.FromAnsi(fg, bg);
    }

    /// <summary>Fills the whole canvas with blanks in the current attribute.</summary>
    public void Clear()
    {
        _chars.AsSpan().Fill(' ');
        _attrs.AsSpan().Fill(_curattr);
    }

    /// <summary>
    /// Writes one character in the current attribute. Coordinates outside the
    /// canvas are silently ignored, as in libcaca.
    /// </summary>
    public void PutChar(int x, int y, int ch)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return;

        int i = x + y * Width;
        _chars[i] = ch;
        _attrs[i] = _curattr;
    }

    /// <summary>The character at the given cell, or a space if out of bounds.</summary>
    public int GetChar(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return ' ';

        return _chars[x + y * Width];
    }

    /// <summary>The attribute at the given cell, or the current one if out of bounds.</summary>
    public uint GetAttr(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return _curattr;

        return _attrs[x + y * Width];
    }

    /// <summary>Writes a string left to right, clipping at the canvas edge.</summary>
    public void PutStr(int x, int y, string s)
    {
        ArgumentNullException.ThrowIfNull(s);

        if ((uint)y >= (uint)Height)
            return;

        foreach (Rune rune in s.EnumerateRunes())
        {
            PutChar(x, y, rune.Value);
            x++;

            if (x >= Width)
                break;
        }
    }

    /// <summary>
    /// Copies <paramref name="src"/> onto this canvas at the given offset. When
    /// <paramref name="mask"/> is supplied, source cells whose corresponding
    /// mask cell holds a space are left untouched.
    /// </summary>
    public void Blit(int x, int y, Canvas src, Canvas? mask = null)
    {
        ArgumentNullException.ThrowIfNull(src);

        if (mask is not null && (src.Width != mask.Width || src.Height != mask.Height))
            throw new ArgumentException("Mask must be the same size as the source canvas.", nameof(mask));

        int startI = x < 0 ? -x : 0;
        int startJ = y < 0 ? -y : 0;
        int endI = x + src.Width >= Width ? Width - x : src.Width;
        int endJ = y + src.Height >= Height ? Height - y : src.Height;

        if (startI >= endI || startJ >= endJ)
            return;

        int stride = endI - startI;

        for (int j = startJ; j < endJ; j++)
        {
            int dst = (j + y) * Width + startI + x;
            int s = j * src.Width + startI;

            if (mask is null)
            {
                src._chars.AsSpan(s, stride).CopyTo(_chars.AsSpan(dst, stride));
                src._attrs.AsSpan(s, stride).CopyTo(_attrs.AsSpan(dst, stride));
                continue;
            }

            for (int i = 0; i < stride; i++)
            {
                if (mask._chars[s + i] == ' ')
                    continue;

                _chars[dst + i] = src._chars[s + i];
                _attrs[dst + i] = src._attrs[s + i];
            }
        }
    }
}
