namespace Distaff.Scumm.Graphics;

/// <summary>How a character's shadow is drawn (ScummVM's <c>ShadowType</c>).</summary>
public enum ShadowType
{
    None,
    Normal,
    Horizontal,
    Outline,
}

/// <summary>
/// A version 3 font: 8 pixels high, one bit a pixel, a width for each character. Mirrors ScummVM's
/// <c>CharsetRendererV3</c>: <c>setCurID</c> reads the count at 4 and the height at 5, the widths
/// follow at 6, and then 8 bytes a character (<c>_fontPtr + chr * 8</c>).
/// </summary>
public sealed class Charset
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly int _glyphs;

    private Charset(ReadOnlyMemory<byte> data, int count, int height)
    {
        _data = data;
        Count = count;
        Height = height;
        _glyphs = 6 + count;
    }

    /// <summary>The characters the font holds (<c>_numChars</c>).</summary>
    public int Count { get; }

    /// <summary>The font's line height (<c>_fontHeight</c>); each character draws 8 rows.</summary>
    public int Height { get; }

    /// <summary>Reads a charset resource, as <see cref="Resources.ResourceLoader.LoadCharset"/> returns it.</summary>
    public static Charset Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length < 6 || span.Length < 6 + span[4])
        {
            throw new InvalidDataException($"A charset of {span.Length} bytes is too short for its header and widths.");
        }

        return new Charset(data, span[4], span[5]);
    }

    /// <summary>
    /// How far a character moves the pen. Mirrors <c>CharsetRendererV3::getCharWidth</c>, which reads
    /// the table without a bound: past the widths come the glyphs' bytes.
    /// </summary>
    public int CharWidth(int c)
    {
        int at = 6 + (c & 0xFF);
        return at < _data.Length ? _data.Span[at] : 0;
    }

    /// <summary>A character's 8 rows of bits, the leftmost pixel in the high bit; empty past the font's end.</summary>
    public ReadOnlySpan<byte> Glyph(int c)
    {
        int at = _glyphs + (c & 0xFF) * 8;
        return at + 8 <= _data.Length ? _data.Span.Slice(at, 8) : [];
    }

    /// <summary>
    /// The width of the text up to its end, a line break or a wait. Mirrors
    /// <c>CharsetRenderer::getStringWidth</c> for version 3, which adds nothing to the width: '@'
    /// draws nothing, code 8 ends a verb's text (<paramref name="arg"/> 1) or skips the spaces
    /// after it, and codes 10, 12, 13, 14 and 21 carry a word.
    /// </summary>
    public int StringWidth(int arg, ReadOnlySpan<byte> text)
    {
        int width = 0;
        int pos = 0;
        byte Next(ReadOnlySpan<byte> t, ref int p) => p < t.Length ? t[p++] : (byte)0;

        while (true)
        {
            int chr = Next(text, ref pos);
            if (chr == 0 || chr == '\n' || chr == '\r')
            {
                break;
            }

            if (chr == '@')
            {
                continue;
            }

            if (chr is 0xFF or 0xFE)
            {
                chr = Next(text, ref pos);
                if (chr == 3)
                {
                    break;
                }

                if (chr == 8)
                {
                    if (arg == 1)
                    {
                        break;
                    }

                    while (Next(text, ref pos) == ' ')
                    {
                    }

                    continue;
                }

                if (chr is 10 or 21 or 12 or 13 or 14)
                {
                    pos += 2;
                    continue;
                }

                if (chr is 9 or 1 or 2)
                {
                    break;
                }
            }

            width += CharWidth(chr);
        }

        return width;
    }

    /// <summary>
    /// Draws a character's bits at (<paramref name="x"/>, <paramref name="y"/>), with its shadow in
    /// colour 0. Mirrors <c>CharsetRendererPC::drawBits1</c>: a normal shadow falls right, below and
    /// below right of each pixel; an outline moves the character a pixel right and down and rings
    /// it, shading the pixel above where the row above is clear. Pixels off the surface are left out.
    /// </summary>
    public static void DrawBits1(Surface8 dest, int x, int y, ReadOnlySpan<byte> src, int width, int height, byte color, ShadowType shadow, byte shadowColor = 0)
    {
        if (shadow == ShadowType.Outline)
        {
            x++;
            y++;
        }

        void Put(int px, int py, byte value)
        {
            if (px >= 0 && px < dest.Width && py >= 0 && py < dest.Height)
            {
                dest[px, py] = value;
            }
        }

        int at = 0;
        byte bits = 0;
        byte prevBits = 0;
        bool leftShadePixel = false;
        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                if (column % 8 == 0)
                {
                    prevBits = (byte)~bits;
                    bits = at < src.Length ? src[at++] : (byte)0;
                    leftShadePixel = true;
                }

                int mask = 0x80 >> (column % 8);
                int px = x + column;
                int py = y + row;
                if ((bits & mask) != 0)
                {
                    switch (shadow)
                    {
                        case ShadowType.Normal:
                            Put(px + 1, py, shadowColor);
                            Put(px + 1, py + 1, shadowColor);
                            Put(px, py + 1, shadowColor);
                            break;
                        case ShadowType.Horizontal:
                            Put(px + 1, py, shadowColor);
                            break;
                        case ShadowType.Outline:
                            Put(px + 1, py, shadowColor);
                            Put(px, py + 1, shadowColor);
                            Put(px + 1, py + 1, shadowColor);
                            if (leftShadePixel)
                            {
                                Put(px - 1, py, shadowColor);
                                leftShadePixel = false;
                            }

                            if ((prevBits & mask) != 0)
                            {
                                Put(px, py - 1, shadowColor);
                            }

                            break;
                    }

                    Put(px, py, color);
                }
                else
                {
                    leftShadePixel = true;
                }
            }
        }
    }
}
