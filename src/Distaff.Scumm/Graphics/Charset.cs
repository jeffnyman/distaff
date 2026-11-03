namespace Distaff.Scumm.Graphics;

/// <summary>How a character's shadow is drawn (ScummVM's <c>ShadowType</c>).</summary>
internal enum ShadowType
{
    None,
    Normal,
    Horizontal,
    Outline,

    /// <summary>The FM Towns' normal shadow: right of and below each pixel, not diagonally (<c>CharsetRendererTownsV3::drawBits1</c>).</summary>
    RightAndBelow,
}

/// <summary>
/// A version 3 font: 8 pixels high, one bit a pixel, a width for each character. Mirrors ScummVM's
/// <c>CharsetRendererV3</c>: <c>setCurID</c> reads the count at 4 and the height at 5, the widths
/// follow at 6, and then 8 bytes a character (<c>_fontPtr + chr * 8</c>).
/// </summary>
internal sealed class Charset
{
    /// <summary>Where a version 4 font starts in its charset resource (<c>_fontPtr += 17</c> in <c>setCurID</c>).</summary>
    private const int ClassicFont = 17;

    private readonly ReadOnlyMemory<byte> _data;
    private readonly int _glyphs;

    private Charset(ReadOnlyMemory<byte> data, int count, int height)
    {
        _data = data;
        Count = count;
        Height = height;
        _glyphs = 6 + count;
    }

    private Charset(ReadOnlyMemory<byte> font, int bitsPerPixel, int height, int count)
    {
        _data = font;
        IsClassic = true;
        BitsPerPixel = bitsPerPixel;
        Height = height;
        Count = count;
    }

    /// <summary>
    /// Whether this is a version 4 font (<c>CharsetRendererClassic</c>): from its start, the bits per
    /// pixel, the height, a word count of characters, and a 32-bit offset per character from the
    /// font's start, 0 for none. A character there is its width, height, and signed x and y offsets,
    /// then its pixels, row by row, <see cref="BitsPerPixel"/> each, the high bits first.
    /// </summary>
    public bool IsClassic { get; }

    /// <summary>The bits a version 4 font's pixels take (<c>_bitsPerPixel</c>): 1, 2, 4 or 8; 1 for version 3.</summary>
    public int BitsPerPixel { get; } = 1;

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

    /// <summary>Reads a version 4 charset resource, as <see cref="Resources.ResourceLoader.LoadCharset"/> returns it.</summary>
    public static Charset ParseClassic(ReadOnlyMemory<byte> data)
    {
        if (data.Length < ClassicFont + 4)
        {
            throw new InvalidDataException($"A charset of {data.Length} bytes is too short for its header.");
        }

        var font = data[ClassicFont..];
        var span = font.Span;
        int bits = span[0];
        if (bits is not (1 or 2 or 4 or 8))
        {
            throw new InvalidDataException($"A charset has {bits} bits a pixel.");
        }

        return new Charset(font, bits, span[1], System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(span[2..]));
    }

    /// <summary>A version 4 character: its size, offsets, and where its pixels start in the font.</summary>
    public readonly record struct Glyph4(int Width, int Height, int OffsetX, int OffsetY, int Data);

    /// <summary>A version 4 character, or null when the font has none. Mirrors <c>CharsetRendererClassic::prepareDraw</c>.</summary>
    public Glyph4? ClassicGlyph(int c)
    {
        int entry = 4 + (c & 0xFF) * 4;
        if (!IsClassic || entry + 4 > _data.Length)
        {
            return null;
        }

        var span = _data.Span;
        int offset = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(span[entry..]);
        if (offset == 0 || offset + 4 > span.Length)
        {
            return null;
        }

        return new Glyph4(span[offset], span[offset + 1], (sbyte)span[offset + 2], (sbyte)span[offset + 3], offset + 4);
    }

    /// <summary>
    /// Draws a version 4 character at (<paramref name="x"/>, <paramref name="y"/>), each pixel's
    /// value through <paramref name="colorMap"/>, 0 transparent. Mirrors <c>drawBitsN</c>; pixels off
    /// the surface are left out.
    /// </summary>
    public void DrawBitsN(Surface8 dest, int x, int y, Glyph4 glyph, ReadOnlySpan<byte> colorMap)
    {
        var src = _data.Span;
        int at = glyph.Data;
        int bpp = BitsPerPixel;
        byte bits = at < src.Length ? src[at++] : (byte)0;
        int numBits = 8;
        for (int row = 0; row < glyph.Height && y + row < dest.Height; row++)
        {
            for (int col = 0; col < glyph.Width; col++)
            {
                int color = (bits >> (8 - bpp)) & 0xFF;
                int px = x + col, py = y + row;
                if (color != 0 && py >= 0 && px >= 0 && px < dest.Width)
                {
                    dest[px, py] = colorMap[color];
                }

                bits = (byte)(bits << bpp);
                numBits -= bpp;
                if (numBits == 0)
                {
                    bits = at < src.Length ? src[at++] : (byte)0;
                    numBits = 8;
                }
            }
        }
    }

    /// <summary>
    /// How far a character moves the pen. Mirrors <c>CharsetRendererV3::getCharWidth</c>, which reads
    /// the table without a bound: past the widths come the glyphs' bytes.
    /// </summary>
    public int CharWidth(int c)
    {
        if (IsClassic)
        {
            // version 4: the width and the x offset (CharsetRendererClassic::getCharWidth)
            return ClassicGlyph(c) is { } glyph ? glyph.Width + glyph.OffsetX : 0;
        }

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
    /// <c>CharsetRenderer::getStringWidth</c>, which adds nothing to the width for version 3 and 1
    /// from version 4 on: '@' draws nothing, code 8 ends a verb's text (<paramref name="arg"/> 1) or skips the spaces
    /// after it, and codes 10, 12, 13, 14 and 21 carry a word.
    /// </summary>
    public int StringWidth(int arg, ReadOnlySpan<byte> text)
    {
        int width = IsClassic ? 1 : 0;
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
                        case ShadowType.RightAndBelow:
                            Put(px + 1, py, shadowColor);
                            Put(px, py + 1, shadowColor);
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
