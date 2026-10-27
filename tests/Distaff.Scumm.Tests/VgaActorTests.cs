using System.Security.Cryptography;
using Distaff.Scumm.Costumes;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Rooms;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>The VGA CD's costumes, drawn at full size and scaled, and the scales boxes give. Tests against the game data pass without checking when it is absent.</summary>
public class VgaActorTests
{
    /// <summary>An actor's colours as an actor starts with them: 255 for each, so the costume's own (<c>initActor</c>).</summary>
    private static readonly byte[] OwnColors = [.. Enumerable.Repeat((byte)255, 32)];

    /// <summary>
    /// Every costume parses, all of them with 16 colours, and every frame of every animation
    /// draws, at full size and at half.
    /// </summary>
    [Fact]
    public void Every_costume_frame_draws_at_full_size_and_scaled()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        int costumes = 0, frames = 0;
        for (int number = 0; number < loader.Index.Count(ResourceType.Costume); number++)
        {
            if (loader.Load(ResourceType.Costume, number) is not { } data)
            {
                continue;
            }

            var costume = Costume.ParseV4(number, data);
            Assert.Equal(16, costume.ColorCount);
            costumes++;
            for (int frame = 0; frame * 4 < costume.AnimationCount; frame++)
            {
                for (int direction = 0; direction < 4; direction++)
                {
                    var state = new CostumeState();
                    state.Decode(costume, frame, direction);
                    var target = new MaskedImage(new Surface8(320, 200), new Surface8(320, 200));
                    var palette = CostumeRenderer.Palette(costume, OwnColors);
                    CostumeRenderer.Draw(costume, state, direction, palette, target, 160, 150, zPlane: 0);
                    CostumeRenderer.Draw(costume, state, direction, palette, target, 160, 150, zPlane: 0, scaleX: 128, scaleY: 128);
                    frames++;
                }
            }
        }

        Assert.Equal((165, 6188), (costumes, frames));
    }

    /// <summary>
    /// A seated figure (costume 6, its first and standing animations) facing right and left, at full
    /// size and at half: the VGA CD's cels count from the feet, and half the scale draws every
    /// other row and column.
    /// </summary>
    [Theory]
    [InlineData(CostumeRenderer.Right, 255, "7e0b832dcb8903a2", 140, 121, 36, 30)]
    [InlineData(CostumeRenderer.Left, 255, "9eabc7ab92e5347f", 144, 121, 36, 30)]
    [InlineData(CostumeRenderer.Right, 128, "7004381844a8889b", 149, 135, 19, 16)]
    [InlineData(CostumeRenderer.Left, 128, "ae6db7ffa4186739", 151, 135, 19, 16)]
    public void A_figure_draws_from_its_feet_and_scales(int direction, int scale, string pixels, int left, int top, int width, int height)
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var costume = Costume.ParseV4(6, loader.Load(ResourceType.Costume, 6)!.Value);
        var state = new CostumeState();
        state.Decode(costume, 1, direction);
        state.Decode(costume, 3, direction);
        var target = new MaskedImage(new Surface8(320, 200), new Surface8(320, 200));
        var bounds = CostumeRenderer.Draw(costume, state, direction, CostumeRenderer.Palette(costume, OwnColors), target, 160, 150, zPlane: 0, scaleX: scale, scaleY: scale);
        Assert.Equal((left, top, width, height), (bounds.Left, bounds.Top, bounds.Width, bounds.Height));
        Assert.Equal(bounds, CostumeRenderer.Bounds(costume, state, direction, 160, 150, scale, scale));
        Assert.Equal(pixels, Hash(target.Pixels.Pixels));
    }

    /// <summary>An actor's colour 255 takes the costume's own; any other replaces it; with the lights off, all are grey but black.</summary>
    [Fact]
    public void The_actor_s_colours_replace_the_costume_s_but_255()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var costume = Costume.ParseV4(6, loader.Load(ResourceType.Costume, 6)!.Value);
        byte[] actor = [.. OwnColors];
        actor[3] = 42;
        var palette = CostumeRenderer.Palette(costume, actor);
        Assert.Equal(16, palette.Length);
        Assert.Equal(42, palette[3]);
        Assert.Equal(costume.Palette.Span[4], palette[4]);
        Assert.All(CostumeRenderer.Palette(costume, actor, actorColors: false).Select((c, i) => (c, i)), p => Assert.Equal(p.i == 12 ? 0 : 8, p.c));
    }

    /// <summary>
    /// A slot's scale follows y between its two points, clamped to 1 to 255; a box whose scale has
    /// its top bit set takes slot <c>(scale &amp; 0x7FFF) + 1</c>, others their own scale.
    /// </summary>
    [Fact]
    public void Scales_follow_a_slot_or_the_box()
    {
        var slot = new ScaleSlot(0, 100, 50, 0, 140, 200);
        Assert.Equal((50, 125, 200, 255, 13, 1), (slot.ScaleAt(0, 100), slot.ScaleAt(0, 120), slot.ScaleAt(0, 140), slot.ScaleAt(0, 160), slot.ScaleAt(0, 90), slot.ScaleAt(0, 50)));

        var boxed = new WalkBox(0, 0, 10, 0, 10, 10, 0, 10, 0, 0, 0x8002);
        Assert.Equal(125, WalkBoxes.Scale(boxed, 5, 120, n => n == 3 ? slot : throw new InvalidOperationException($"slot {n}")));
        Assert.Equal(180, WalkBoxes.Scale(boxed with { Scale = 180 }, 5, 120, _ => throw new InvalidOperationException()));
    }

    /// <summary>The VGA CD's rooms set no scale: every box is full size and every room's scale slots are empty; scripts would have to scale actors.</summary>
    [Fact]
    public void No_room_scales_its_actors()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        for (int number = 1; number < loader.Index.Count(ResourceType.Room); number++)
        {
            if (loader.HasRoomFile(number) && loader.LoadRoom(number) is { } room)
            {
                Assert.All(room.WalkBoxes.Boxes, b => Assert.Equal(255, b.Scale));
                Assert.Empty(room.ScaleSlots);
            }
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes))[..16];

    private static ResourceLoader? Open()
        => GameFolders.Find(GameFolders.Vga) is { } path ? ResourceLoader.Open(new DirectoryGameContainer(path)) : null;
}
