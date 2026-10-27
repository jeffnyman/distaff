using System.Security.Cryptography;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Rooms;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>Drawing the EGA release's rooms. Tests against the game data pass without checking when it is absent.</summary>
public class RoomRendererTests
{
    /// <summary>Every room's picture, and every image that belongs to its object, decodes within its data.</summary>
    [Fact]
    public void Every_room_and_object_image_decodes()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        int rooms = 0, images = 0;
        foreach (var room in Rooms(loader))
        {
            var image = RoomRenderer.DrawBackground(room);
            Assert.Equal((room.Width, room.Height), (image.Pixels.Width, image.Pixels.Height));
            Assert.All(image.Pixels.Pixels, p => Assert.InRange(p, 0, 15));
            Assert.All(image.Mask.Pixels, p => Assert.InRange(p, 0, 1));

            foreach (var obj in room.Objects.Where(o => RoomRenderer.OwnsImage(room, o)))
            {
                RoomRenderer.DrawObject(room, obj, image);
                images++;
            }

            rooms++;
        }

        Assert.Equal(76, rooms);
        Assert.Equal(ImagesWithPictures, images);
    }

    /// <summary>The title room, the Lucasfilm Games logo over the sea, as the game starts.</summary>
    [Fact]
    public void The_title_room_draws_as_before()
    {
        if (Open()?.LoadRoom(1) is not { } room)
        {
            return;
        }

        var image = RoomRenderer.DrawBackground(room);

        Assert.Equal(TitleRoomPixels, Hash(image.Pixels));
        Assert.Equal(TitleRoomMask, Hash(image.Mask));
    }

    [Fact]
    public void Objects_are_drawn_by_state_and_parent()
    {
        if (Open()?.LoadRoom(2) is not { } room)
        {
            return;
        }

        // the opening's objects all start in state 0: the room as the game starts is its background
        Assert.Empty(RoomRenderer.VisibleObjects(room, _ => 0));

        // with every state on, the objects are drawn from the last to the first
        var all = RoomRenderer.VisibleObjects(room, _ => 1).Select(o => o.Number).ToList();
        Assert.Equal(room.Objects.Select(o => o.Number).Reverse(), all);
    }

    [Fact]
    public void A_child_shows_only_while_its_parent_is_in_the_state_it_expects()
    {
        var parent = Fake(number: 10, parent: 0, parentState: 0);
        var child = Fake(number: 11, parent: 1, parentState: 1);

        Assert.True(RoomRenderer.ParentsAllow([parent, child], child, n => 1));
        Assert.False(RoomRenderer.ParentsAllow([parent, child], child, n => n == 10 ? 0 : 1));
    }

    /// <summary>A copying run takes the pixel to its left, across the edge of a strip.</summary>
    [Fact]
    public void A_copying_run_continues_the_column_to_its_left()
    {
        // two strips one row high: strip 0 is eight pixels of colour 5, strip 1 copies them
        byte[] picture =
        [
            0x0A, 0x00,             // the mask at 10
            0x06, 0x00,             // strip 0 at 6
            0x08, 0x00,             // strip 1 at 8
            0x05, 0x08,             // strip 0: colour 5, a run length of 0, so the length, 8, follows
            0x88, 0x00,             // strip 1: a copying run of 8
            0x00, 0x00, 0x00, 0x00, // the mask: neither strip has one
        ];
        var target = new MaskedImage(new Surface8(16, 1), new Surface8(16, 1));

        EgaBitmap.Draw(picture, 0, 2, 1, target, 0, 0);

        Assert.All(target.Pixels.Pixels, p => Assert.Equal(5, p));
    }

    private const int ImagesWithPictures = 618;

    private const string TitleRoomPixels = "7e8134b090e236f7";

    private const string TitleRoomMask = "7e36bec0af80ff78";

    private static string Hash(Surface8 surface) => Convert.ToHexStringLower(SHA256.HashData(surface.Pixels))[..16];

    private static RoomObject Fake(int number, int parent, int parentState)
    {
        // an object code block: size, number, geometry, the parent and its state, no name, no verbs
        var block = new byte[18];
        block[0] = 18;
        block[4] = (byte)number;
        block[8] = (byte)(parentState << 7);
        block[10] = (byte)parent;
        return RoomObject.Parse(block, 0, 0);
    }

    private static IEnumerable<Room> Rooms(ResourceLoader loader)
    {
        for (int number = 1; number < loader.Index.Count(ResourceType.Room); number++)
        {
            if (loader.Index.Locate(ResourceType.Room, number).IsPresent && LflFile.Exists(loader.Game, number) && loader.LoadRoom(number) is { } room)
            {
                yield return room;
            }
        }
    }

    private static ResourceLoader? Open()
        => GameFolders.Find(GameFolders.Ega) is { } path ? ResourceLoader.Open(new DirectoryGameContainer(path)) : null;
}
