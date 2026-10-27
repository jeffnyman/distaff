using System.Buffers.Binary;

namespace Distaff.Scumm.Graphics;

/// <summary>A picture with its mask: one byte per pixel, 1 where the mask hides actors.</summary>
public sealed record MaskedImage(Surface8 Pixels, Surface8 Mask);

/// <summary>
/// The picture format of the EGA release's rooms and objects: 8-pixel strips, each run-length
/// encoded column by column from the top, followed by one mask. Mirrors ScummVM's
/// <c>Gdi::drawBitmap</c>, <c>drawStrip</c> and <c>decodeMask</c> for <c>GF_16COLOR</c> and
/// <c>GF_OLD_BUNDLE</c>, <c>Gdi::drawStripEGA</c> and <c>Gdi::decompressMaskImg</c>.
/// </summary>
/// <remarks>
/// <para>Layout: a word giving the offset of the mask, then a word per strip giving where the
/// strip starts; offsets are from the start of the picture. The mask has a word per strip giving
/// where that strip's mask starts, from the start of the mask, or 0 for a strip with no mask.</para>
/// <para>A strip: a byte with bit 7 clear is a run of <c>byte &gt;&gt; 4</c> pixels of colour
/// <c>byte &amp; 0x0F</c>. With bits 7 and 6 set it is a run of <c>byte &amp; 0x3F</c> pixels
/// alternating between the two colours of the next byte, high nibble first. With bit 7 set and 6
/// clear it is a run of pixels each copying the one to its left. A run length of 0 means the
/// length is in the next byte. Runs go down a column and on to the top of the next.</para>
/// <para>A mask strip: one byte per row, bit 7 the leftmost pixel. A length byte with bit 7 set
/// repeats the next byte <c>length &amp; 0x7F</c> times; with bit 7 clear it is followed by that
/// many bytes.</para>
/// </remarks>
public static class EgaBitmap
{
    /// <summary>
    /// Draws strips <paramref name="firstStrip"/> onwards of a picture into <paramref name="target"/>,
    /// the first at (<paramref name="x"/>, <paramref name="y"/>), and their mask into the target's
    /// mask; strips and rows that fall outside the target are skipped. Strips are drawn left to
    /// right into the target itself, because a copying run in a strip's first column takes the
    /// pixels of the column to its left: the previous strip's, or the target's own. Colours are
    /// drawn through <paramref name="colorMap"/> when there is one (<c>_roomPalette</c>).
    /// </summary>
    public static void Draw(ReadOnlySpan<byte> picture, int firstStrip, int strips, int height, MaskedImage target, int x, int y, byte[]? colorMap = null)
    {
        if (picture.Length < 2)
        {
            throw new InvalidDataException("A picture is too short for its header.");
        }

        int maskOffset = Word(picture, 0);
        for (int i = 0; i < strips; i++)
        {
            int strip = firstStrip + i;
            int left = x + i * 8;
            if (left < 0 || left + 8 > target.Pixels.Width)
            {
                continue;
            }

            DrawStrip(picture, StripOffset(picture, maskOffset, strip), height, target.Pixels, left, y, colorMap);
            DrawMaskStrip(picture, maskOffset, strip, height, target.Mask, left, y);
        }
    }

    /// <summary>
    /// How many strips a picture has: its first strip's data starts right after the strip table.
    /// 0 when the picture is too short to say.
    /// </summary>
    public static int StripCount(ReadOnlySpan<byte> picture)
        => picture.Length >= 4 ? Math.Max(0, (Word(picture, 2) - 2) / 2) : 0;

    private static int StripOffset(ReadOnlySpan<byte> picture, int maskOffset, int strip)
    {
        // the strip table runs up to the mask, whose offset is the table's first word
        if (strip * 2 + 2 >= maskOffset || strip * 2 + 4 > picture.Length)
        {
            throw new InvalidDataException($"Strip {strip} is past the picture's strip table.");
        }

        int offset = Word(picture, strip * 2 + 2);
        if (offset >= picture.Length)
        {
            throw new InvalidDataException($"Strip {strip} starts at {offset}, past the end of the picture ({picture.Length} bytes).");
        }

        return offset;
    }

    private static void DrawStrip(ReadOnlySpan<byte> picture, int offset, int height, Surface8 target, int left, int top, byte[]? colorMap)
    {
        int Map(int color) => colorMap is null ? color : colorMap[color];

        var pixels = target.Pixels;
        int pitch = target.Width;
        int at = offset;
        int x = 0, y = 0;

        // where a pixel of the strip lands in the target's pixels, or -1 when below its bottom
        int Index(int px, int py) => top + py < target.Height && top + py >= 0 ? (top + py) * pitch + left + px : -1;

        void Put(int color)
        {
            int index = Index(x, y);
            if (index >= 0)
            {
                pixels[index] = (byte)color;
            }

            if (++y >= height)
            {
                y = 0;
                x++;
            }
        }

        while (x < 8)
        {
            byte code = Next(picture, ref at);
            if ((code & 0x80) != 0)
            {
                int run = code & 0x3F;
                if ((code & 0x40) != 0)
                {
                    byte colors = Next(picture, ref at);
                    if (run == 0)
                    {
                        run = Next(picture, ref at);
                    }

                    for (int z = 0; z < run && x < 8; z++)
                    {
                        Put(Map((z & 1) != 0 ? colors & 0x0F : colors >> 4));
                    }
                }
                else
                {
                    if (run == 0)
                    {
                        run = Next(picture, ref at);
                    }

                    for (int z = 0; z < run && x < 8; z++)
                    {
                        // ScummVM reads the byte before this pixel in the buffer, whatever it is
                        int index = Index(x, y);
                        Put(index > 0 ? pixels[index - 1] : 0);
                    }
                }
            }
            else
            {
                int run = code >> 4;
                if (run == 0)
                {
                    run = Next(picture, ref at);
                }

                for (int z = 0; z < run && x < 8; z++)
                {
                    Put(Map(code & 0x0F));
                }
            }
        }
    }

    private static void DrawMaskStrip(ReadOnlySpan<byte> picture, int maskOffset, int strip, int height, Surface8 mask, int left, int top)
    {
        if (maskOffset + strip * 2 + 2 > picture.Length)
        {
            throw new InvalidDataException($"Strip {strip}'s mask is past the end of the picture.");
        }

        int offset = Word(picture, maskOffset + strip * 2);
        int at = maskOffset + offset;
        int row = 0;

        void Put(byte bits)
        {
            int y = top + row++;
            if (y < 0 || y >= mask.Height)
            {
                return;
            }

            var line = mask.Row(y);
            for (int bit = 0; bit < 8; bit++)
            {
                line[left + bit] = (byte)((bits >> (7 - bit)) & 1);
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
        => at < picture.Length ? picture[at++] : throw new InvalidDataException($"A strip or its mask runs past the end of the picture ({picture.Length} bytes).");

    private static int Word(ReadOnlySpan<byte> span, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
}
