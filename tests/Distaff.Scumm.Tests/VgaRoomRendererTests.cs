using System.Security.Cryptography;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Rooms;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>Drawing the VGA CD's rooms and objects. Each test passes without checking when the game data is absent.</summary>
public class VgaRoomRendererTests
{
    /// <summary>
    /// Every room's picture and z-planes decode, and every object image does, over the room drawn
    /// with all its objects; the strips use codecs 1, 14 to 18 and 24 to 28. Rooms have 1 to 3
    /// buffers: the picture and up to two z-planes.
    /// </summary>
    [Fact]
    public void Every_room_and_object_image_decodes()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var buffers = new Dictionary<int, int>();
        int images = 0;
        foreach (var room in Rooms(loader))
        {
            var image = RoomRenderer.RenderAll(room);
            Assert.Equal((room.Width, room.Height), (image.Pixels.Width, image.Pixels.Height));
            images += room.Objects.Count(o => RoomRenderer.OwnsImage(room, o));
            int count = RoomRenderer.ZBuffers(room);
            buffers[count] = buffers.GetValueOrDefault(count) + 1;
        }

        Assert.Equal(new Dictionary<int, int> { [1] = 26, [2] = 50, [3] = 2 }, buffers);
        Assert.Equal(Images, images);
    }

    /// <summary>The cliff, drawn as the game starts it, its z-plane, and its palette.</summary>
    [Fact]
    public void The_cliff_draws_as_before()
    {
        if (Open()?.LoadRoom(2) is not { } room)
        {
            return;
        }

        var image = RoomRenderer.Render(room, _ => 0);
        Assert.Equal(CliffPixels, Hash(image.Pixels.Pixels));
        Assert.Equal(CliffMask, Hash(image.Mask.Pixels));
        Assert.Equal(CliffPalette, Hash([.. RoomRenderer.PaletteOf(room).SelectMany(c => new[] { c.R, c.G, c.B })]));
    }

    /// <summary>
    /// The complex codec, which Loom's own pictures do not use, on a strip built by hand: colour 5,
    /// then bits (low first) to keep it, step it by +1, and read the 4-bit colour 9, which then stays.
    /// </summary>
    [Fact]
    public void The_complex_codec_steps_and_reads_colours()
    {
        // the z-plane offset (just past one strip's entry), the strip's offset, then the strip
        byte[] picture = [8, 0, 0, 0, 8, 0, 0, 0, 64, 5, 0x6E, 0x09, 0, 0, 0, 0];
        var target = new MaskedImage(new Surface8(8, 1), new Surface8(8, 1));
        VgaBitmap.Draw(picture, 0, 1, 1, target, 0, 0, zBuffers: 1);
        Assert.Equal([5, 5, 6, 9, 9, 9, 9, 9], target.Pixels.Pixels);
    }

    /// <summary>The same with 8-bit colours reading 255: codec 68 draws it, its transparent form 88 leaves it undrawn.</summary>
    [Theory]
    [InlineData(68, new byte[] { 5, 5, 6, 255, 255, 255, 255, 255 })]
    [InlineData(88, new byte[] { 5, 5, 6, 7, 7, 7, 7, 7 })]
    public void The_transparent_codec_leaves_colour_255_undrawn(byte codec, byte[] row)
    {
        byte[] picture = [8, 0, 0, 0, 8, 0, 0, 0, codec, 5, 0x6E, 0xFF, 0, 0, 0, 0];
        var target = new MaskedImage(new Surface8(8, 1), new Surface8(8, 1));
        target.Pixels.Pixels.AsSpan().Fill(7);
        VgaBitmap.Draw(picture, 0, 1, 1, target, 0, 0, zBuffers: 1);
        Assert.Equal(row, target.Pixels.Pixels);
    }

    private const int Images = 694;
    private const string CliffPixels = "728ca788686a03b2", CliffMask = "4f9a51ef15da7ffa", CliffPalette = "ebe4ad9790e81cd6";

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes))[..16];

    private static IEnumerable<Room> Rooms(ResourceLoader loader)
    {
        for (int number = 1; number < loader.Index.Count(ResourceType.Room); number++)
        {
            if (loader.HasRoomFile(number) && loader.LoadRoom(number) is { } room)
            {
                yield return room;
            }
        }
    }

    private static ResourceLoader? Open()
        => GameFolders.Find(GameFolders.Vga) is { } path ? ResourceLoader.Open(new DirectoryGameContainer(path)) : null;
}
