using System.Buffers.Binary;

namespace Distaff.Scumm.Graphics;

/// <summary>
/// The picture format of the VGA CD's rooms and objects: 8-pixel strips of 256-colour pixels, each
/// compressed by the codec its first byte names, and up to three z-planes, the masks that hide
/// actors. Mirrors ScummVM's <c>Gdi::drawBitmap</c>, <c>getZPlanes</c>, <c>drawStrip</c>,
/// <c>decodeMask</c> and <c>decompressBitmap</c> for <c>GF_SMALL_HEADER</c> without
/// <c>GF_16COLOR</c>, with the codecs <c>drawStripRaw</c>, <c>drawStripBasicV</c>,
/// <c>drawStripBasicH</c> and <c>drawStripComplex</c> (by way of <c>MajMinCodec</c>), and
/// <c>decompressMaskImg</c>.
/// </summary>
/// <remarks>
/// <para>Layout: a 32-bit offset of the first z-plane, then a 32-bit offset per strip; offsets are
/// from the start of the picture. Each z-plane starts with a word giving the offset of the next,
/// then a word per strip giving where that strip's mask starts, from the start of the plane, or
/// 0 for a strip with nothing masked.</para>
/// <para>A strip's codec byte gives the colour width in its last digit (<c>code % 10</c> bits).
/// 1 is raw pixels, row by row. 14 to 18 run down the columns and 24 to 28 along the rows: from a
/// first colour, each pixel's bits keep the colour (0), read a new one (10), or step it by one
/// in the current direction (110) or the other (111). 64 to 68 and 104 to 108 also step by -4 to
/// +3 and repeat runs. Codecs 20 higher are the same with colour 255 left transparent.</para>
/// <para>The mask records the planes as bits: bit 0 for plane 1, bit 1 for plane 2, bit 2 for plane 3.</para>
/// </remarks>
public static class VgaBitmap
{
    /// <summary>The colour the transparent codecs leave undrawn: no <c>TRNS</c> block in version 4 rooms, so <c>setupRoomSubBlocks</c>' 255.</summary>
    public const byte TransparentColor = 255;

    /// <summary>
    /// How many buffers a room picture has: the picture itself and each z-plane, at most 4.
    /// Mirrors the <c>GF_SMALL_HEADER</c> count of <c>_numZBuffer</c> in <c>initRoomSubBlocks</c>.
    /// </summary>
    public static int ZBufferCount(ReadOnlySpan<byte> picture)
    {
        int count = 0, at = 0;
        int next = Int32(picture, 0);
        while (next != 0 && count < 4)
        {
            count++;
            at += next;
            next = Word(picture, at);
        }

        return count;
    }

    /// <summary>
    /// Draws strips <paramref name="firstStrip"/> onwards of a picture into <paramref name="target"/>,
    /// the first at (<paramref name="x"/>, <paramref name="y"/>), and its z-planes, the first
    /// <paramref name="zBuffers"/> - 1 of them, into the target's mask; strips and rows outside the
    /// target are skipped. Colours are drawn through <paramref name="colorMap"/> when there is one
    /// (<c>_roomPalette</c>). A plane whose strip has no mask clears its bit there.
    /// </summary>
    public static void Draw(ReadOnlySpan<byte> picture, int firstStrip, int strips, int height, MaskedImage target, int x, int y, int zBuffers, byte[]? colorMap = null)
    {
        int smapLength = Int32(picture, 0);
        var planes = new int[Math.Max(zBuffers, 1)];
        for (int i = 1; i < zBuffers; i++)
        {
            planes[i] = i == 1 ? smapLength : planes[i - 1] + Word(picture, planes[i - 1]);
        }

        for (int i = 0; i < strips; i++)
        {
            int strip = firstStrip + i;
            int left = x + i * 8;
            if (left < 0 || left + 8 > target.Pixels.Width)
            {
                continue;
            }

            if (strip * 4 + 4 >= smapLength)
            {
                throw new InvalidDataException($"Strip {strip} is past the picture's strip table.");
            }

            int offset = Int32(picture, strip * 4 + 4);
            if (offset >= picture.Length)
            {
                throw new InvalidDataException($"Strip {strip} starts at {offset}, past the end of the picture ({picture.Length} bytes).");
            }

            DrawStrip(picture, offset, height, target.Pixels, left, y, colorMap);
            for (int plane = 1; plane < zBuffers; plane++)
            {
                DrawMaskStrip(picture, planes[plane], Word(picture, planes[plane] + 2 + strip * 2), height, target.Mask, left, y, (byte)(1 << (plane - 1)));
            }
        }
    }

    /// <summary>Decodes one strip by its codec. Mirrors <c>decompressBitmap</c>.</summary>
    private static void DrawStrip(ReadOnlySpan<byte> picture, int offset, int height, Surface8 target, int left, int top, byte[]? colorMap)
    {
        byte code = picture[offset];
        int shift = code % 10;
        var strip = new Strip(picture, offset + 1, height, target, left, top, colorMap, code is >= 34 and <= 48 or >= 84 and <= 88 or >= 124 and <= 128);
        switch (code)
        {
            case 1:
                strip.Raw();
                break;
            case >= 14 and <= 18 or >= 34 and <= 38:
                strip.Basic(shift, vertical: true);
                break;
            case >= 24 and <= 28 or >= 44 and <= 48:
                strip.Basic(shift, vertical: false);
                break;
            case >= 64 and <= 68 or >= 84 and <= 88 or >= 104 and <= 108 or >= 124 and <= 128:
                strip.Complex(shift);
                break;
            default:
                throw new InvalidDataException($"Strip codec {code} at {offset} is not one the VGA CD's pictures use.");
        }
    }

    /// <summary>
    /// One plane's mask for a strip: as <c>decompressMaskImg</c>, a byte per row with bit 7 the
    /// leftmost pixel; a length byte with bit 7 set repeats the next byte, otherwise that many bytes
    /// follow. The plane's bit is set or cleared in the target mask; an offset of 0 clears it.
    /// </summary>
    private static void DrawMaskStrip(ReadOnlySpan<byte> picture, int plane, int offset, int height, Surface8 mask, int left, int top, byte bit)
    {
        int at = plane + offset;
        int row = 0;

        void Put(byte bits)
        {
            int y = top + row++;
            if (y < 0 || y >= mask.Height)
            {
                return;
            }

            var line = mask.Row(y);
            for (int b = 0; b < 8; b++)
            {
                line[left + b] = ((bits >> (7 - b)) & 1) != 0 ? (byte)(line[left + b] | bit) : (byte)(line[left + b] & ~bit);
            }
        }

        if (offset == 0)
        {
            while (row < height)
            {
                Put(0);
            }

            return;
        }

        // the run counts down in a byte, as in ScummVM, so a run of 0 is a run of 256
        while (row < height)
        {
            byte count = Next(picture, ref at);
            byte run = (byte)(count & 0x7F);
            if ((count & 0x80) != 0)
            {
                byte bits = Next(picture, ref at);
                do
                {
                    Put(bits);
                }
                while (--run != 0 && row < height);
            }
            else
            {
                do
                {
                    Put(Next(picture, ref at));
                }
                while (--run != 0 && row < height);
            }
        }
    }

    private static byte Next(ReadOnlySpan<byte> picture, ref int at)
        => at < picture.Length ? picture[at++] : throw new InvalidDataException($"A strip's mask runs past the end of the picture ({picture.Length} bytes).");

    private static int Int32(ReadOnlySpan<byte> span, int offset)
        => offset >= 0 && offset + 4 <= span.Length ? BinaryPrimitives.ReadInt32LittleEndian(span[offset..]) : throw new InvalidDataException($"The picture ends before offset {offset}.");

    private static int Word(ReadOnlySpan<byte> span, int offset)
        => offset >= 0 && offset + 2 <= span.Length ? BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]) : throw new InvalidDataException($"The picture ends before offset {offset}.");

    /// <summary>A strip being decoded into its 8 columns of the target.</summary>
    private ref struct Strip(ReadOnlySpan<byte> source, int at, int height, Surface8 target, int left, int top, byte[]? colorMap, bool transparent)
    {
        private readonly ReadOnlySpan<byte> _source = source;
        private int _at = at;

        /// <summary>Codec 1: 8 bytes a row. Mirrors <c>drawStripRaw</c>.</summary>
        public void Raw()
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    Put(x, y, Take());
                }
            }
        }

        /// <summary>
        /// Codecs 14 to 18 and 24 to 28: a colour, then bits, read low first, for each pixel after
        /// the one drawn. Mirrors <c>drawStripBasicV</c> and <c>drawStripBasicH</c>, which read a byte
        /// whenever 8 bits or fewer are left.
        /// </summary>
        public void Basic(int shift, bool vertical)
        {
            int mask = 0xFF >> (8 - shift);
            byte color = Take();
            uint bits = Take();
            int count = 8;
            int inc = -1;

            bool Bit(ref uint bits, ref int count)
            {
                count--;
                bool set = (bits & 1) != 0;
                bits >>= 1;
                return set;
            }

            for (int outer = 0; outer < (vertical ? 8 : height); outer++)
            {
                for (int inner = 0; inner < (vertical ? height : 8); inner++)
                {
                    Fill(ref bits, ref count);
                    if (vertical)
                    {
                        Put(outer, inner, color);
                    }
                    else
                    {
                        Put(inner, outer, color);
                    }

                    if (!Bit(ref bits, ref count))
                    {
                    }
                    else if (!Bit(ref bits, ref count))
                    {
                        Fill(ref bits, ref count);
                        color = (byte)(bits & mask);
                        bits >>= shift;
                        count -= shift;
                        inc = -1;
                    }
                    else if (!Bit(ref bits, ref count))
                    {
                        color = (byte)(color + inc);
                    }
                    else
                    {
                        inc = -inc;
                        color = (byte)(color + inc);
                    }
                }
            }
        }

        /// <summary>
        /// Codecs 64 to 68 and 104 to 108, row by row: after each pixel, a 0 bit keeps the colour; 1 1
        /// and three bits less 4 step it, or with a step of 0 repeat it for the count of the next 8
        /// bits; 1 0 reads a new one. Mirrors <c>drawStripComplex</c> and <c>MajMinCodec</c>.
        /// </summary>
        public void Complex(int shift)
        {
            byte color = Take();
            uint bits = (uint)(Take() | (Take() << 8));
            int count = 16;
            bool repeat = false;
            int repeatCount = 0;

            int Read(int n, ref uint bits, ref int count, ref Strip strip)
            {
                if (count <= 8)
                {
                    bits |= (uint)strip.Take() << count;
                    count += 8;
                }

                int value = (int)(bits & ((1u << n) - 1));
                count -= n;
                bits >>= n;
                return value;
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    Put(x, y, color);
                    if (!repeat)
                    {
                        if (Read(1, ref bits, ref count, ref this) != 0)
                        {
                            if (Read(1, ref bits, ref count, ref this) != 0)
                            {
                                byte diff = (byte)(Read(3, ref bits, ref count, ref this) - 4);
                                if (diff != 0)
                                {
                                    color += diff;
                                }
                                else
                                {
                                    repeat = true;
                                    repeatCount = Read(8, ref bits, ref count, ref this) - 1;
                                }
                            }
                            else
                            {
                                color = (byte)Read(shift, ref bits, ref count, ref this);
                            }
                        }
                    }
                    else if (--repeatCount == 0)
                    {
                        repeat = false;
                    }
                }
            }
        }

        /// <summary>Tops the bit buffer up by a byte when 8 bits or fewer are left (<c>FILL_BITS</c>).</summary>
        private void Fill(ref uint bits, ref int count)
        {
            if (count <= 8)
            {
                bits |= (uint)Take() << count;
                count += 8;
            }
        }

        /// <summary>
        /// The next byte of the strip. The codecs read a byte ahead of the bits they use, so the
        /// last strip of a picture may read past its end, where ScummVM reads what lies beyond in
        /// memory; this reads 0, which no drawn pixel depends on.
        /// </summary>
        private byte Take() => _at < _source.Length ? _source[_at++] : (byte)0;

        private readonly void Put(int x, int y, byte color)
        {
            if (transparent && color == TransparentColor)
            {
                return;
            }

            int row = top + y;
            if (row >= 0 && row < target.Height)
            {
                target.Pixels[row * target.Width + left + x] = colorMap is null ? color : colorMap[color];
            }
        }
    }
}
