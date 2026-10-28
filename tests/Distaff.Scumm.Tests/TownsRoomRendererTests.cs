using System.Buffers.Binary;
using System.Security.Cryptography;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Rooms;

namespace Distaff.Scumm.Tests;

/// <summary>Drawing the FM Towns release's rooms and objects. Tests against the game data pass without checking when it is absent.</summary>
public class TownsRoomRendererTests
{
    /// <summary>
    /// Every room's picture and z-plane decode, and every object image does, over the room drawn
    /// with all its objects. Every room has 2 buffers, the picture and one z-plane, which 53 of
    /// the 75 have; the strips use only the FM Towns' own codecs.
    /// </summary>
    [Fact]
    public void Every_room_and_object_image_decodes()
    {
        if (TownsResourceTests.Open() is not { } loader)
        {
            return;
        }

        var codecs = new SortedDictionary<int, int>();
        int images = 0, planes = 0;
        foreach (var room in TownsResourceTests.Rooms(loader))
        {
            var image = RoomRenderer.RenderAll(room);
            Assert.Equal((room.Width, room.Height), (image.Pixels.Width, image.Pixels.Height));
            Assert.Equal(2, RoomRenderer.ZBuffers(room));
            var picture = room.Data.Span[room.ImageOffset..];
            planes += BinaryPrimitives.ReadInt32LittleEndian(picture[BinaryPrimitives.ReadInt32LittleEndian(picture)..]) != 0 ? 1 : 0;
            Count(picture, room.Width / 8, codecs);
            foreach (var obj in room.Objects.Where(o => RoomRenderer.OwnsImage(room, o)))
            {
                Count(room.Data.Span[(obj.ImageOffset + 8)..], obj.Width / 8, codecs);
                images++;
            }
        }

        Assert.Equal((599, 53), (images, planes));
        Assert.Equal(new SortedDictionary<int, int> { [1] = 201, [2] = 291, [3] = 1224, [4] = 743, [7] = 4734 }, codecs);
    }

    /// <summary>The cliff, drawn as the game starts it, its z-plane, and its palette.</summary>
    [Fact]
    public void The_cliff_draws_as_before()
    {
        if (TownsResourceTests.Open()?.LoadRoom(2) is not { } room)
        {
            return;
        }

        var image = RoomRenderer.Render(room, _ => 0);
        Assert.Equal(CliffPixels, Hash(image.Pixels.Pixels));
        Assert.Equal(CliffMask, Hash(image.Mask.Pixels));
        Assert.Equal(CliffPalette, Hash([.. RoomRenderer.PaletteOf(room).SelectMany(c => new[] { c.R, c.G, c.B })]));
    }

    /// <summary>Codec 1 for the FM Towns is a byte a pixel down the columns, not along the rows.</summary>
    [Fact]
    public void Raw_pixels_run_down_the_columns()
    {
        var target = Draw([1, .. Enumerable.Range(1, 16).Select(i => (byte)i)], 2);
        Assert.Equal([1, 3, 5, 7, 9, 11, 13, 15, 2, 4, 6, 8, 10, 12, 14, 16], target);
    }

    /// <summary>Codec 2: runs of a count less one and a colour, the second column going on from the first.</summary>
    [Fact]
    public void Runs_fill_the_columns_in_turn()
    {
        var target = Draw([2, 2, 5, 12, 6], 2);
        Assert.Equal([5, 5, 6, 6, 6, 6, 6, 6, 5, 6, 6, 6, 6, 6, 6, 6], target);
    }

    /// <summary>
    /// Codec 3, 4 bits at a time from each byte's low bit: 8 sets the high nibble to 1, 1 then 3
    /// draws three pixels of 0x13, 4 then 9 draws 0x19, and 3 then 2 draws five pixels of 0x12.
    /// </summary>
    [Fact]
    public void Nibbles_set_a_high_nibble_and_draw_runs_and_colours()
    {
        // nibbles, low first: 8 1 | 1 3 | 4 9 | 3 2 | 3 2 | 3 2
        var target = Draw([3, 0x18, 0x31, 0x94, 0x23, 0x23, 0x23], 2);
        Assert.Equal([0x13, 0x13, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x13, 0x19, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12], target);
    }

    /// <summary>Codec 4: two colours, 0 and 1 picking them, then 4 (two past the count) repeating 9 three times.</summary>
    [Fact]
    public void A_table_picks_colours_and_repeats_others()
    {
        var target = Draw([4, 2, 20, 30, 0, 1, 4, 9, 15, 7], 1);
        Assert.Equal([20, 30, 9, 9, 9, 7, 7, 7], target);
    }

    /// <summary>
    /// Codec 7: colour 10, then bits low first: 0 keeps it, 1 0 turns the step about, from down
    /// to up, and takes it, 1 1 0 takes it again, and 1 1 1 reads 8 bits, 40.
    /// </summary>
    [Fact]
    public void Steps_turn_about_go_on_or_read_a_colour()
    {
        // bits, low first: 0 | 1 0 | 1 1 0 | 1 1 1 | 40 (0 0 0 1 0 1 0 0) | 0 0 0
        var target = Draw([7, 10, 0b1101_1010, 0b0101_0001, 0], 1);
        Assert.Equal([10, 10, 11, 12, 40, 40, 40, 40], target);
    }

    /// <summary>Draws one strip of a picture with no z-plane, <paramref name="height"/> rows, and returns its pixels row by row.</summary>
    private static byte[] Draw(byte[] strip, int height)
    {
        byte[] picture = [8, 0, 0, 0, 8, 0, 0, 0, .. strip, 0, 0, 0, 0];
        var target = new MaskedImage(new Surface8(8, height), new Surface8(8, height));
        VgaBitmap.Draw(picture, 0, 1, height, target, 0, 0, zBuffers: 1, old256: true);
        return target.Pixels.Pixels;
    }

    /// <summary>Counts the codec of each strip of a picture.</summary>
    private static void Count(ReadOnlySpan<byte> picture, int strips, SortedDictionary<int, int> codecs)
    {
        for (int i = 0; i < strips; i++)
        {
            int code = picture[BinaryPrimitives.ReadInt32LittleEndian(picture[(4 + i * 4)..])];
            codecs[code] = codecs.GetValueOrDefault(code) + 1;
        }
    }

    private const string CliffPixels = "7498f82ea5a2ebad", CliffMask = "d97bee4f8ad95809", CliffPalette = "1a86d6ac9cfa44b0";

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes))[..16];
}
