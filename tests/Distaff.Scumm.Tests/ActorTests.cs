using System.Security.Cryptography;
using Distaff.Scumm.Costumes;
using Distaff.Scumm.Game;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Rooms;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>Costumes, boxes and walking. Tests against the game data pass without checking when it is absent.</summary>
public class ActorTests
{
    /// <summary>
    /// Every costume parses, and its frames draw facing each way. Four entries that load are not
    /// costumes: leftovers pointing into the middle of other resources (66, 89, 136, 173).
    /// </summary>
    [Fact]
    public void Every_costume_draws_its_frames()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        int costumes = 0, frames = 0;
        var notCostumes = new List<int>();
        for (int number = 1; number < loader.Index.Count(ResourceType.Costume); number++)
        {
            ReadOnlyMemory<byte>? data;
            try
            {
                data = loader.Load(ResourceType.Costume, number);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            if (data is not { } bytes)
            {
                continue;
            }

            Costume costume;
            try
            {
                costume = Costume.Parse(number, bytes);
            }
            catch (InvalidDataException)
            {
                notCostumes.Add(number);
                continue;
            }

            costumes++;
            for (int frame = 0; frame * 4 < costume.AnimationCount; frame++)
            {
                for (int direction = 0; direction < 4; direction++)
                {
                    var state = new CostumeState();
                    state.Decode(costume, frame, direction);
                    var target = new MaskedImage(new Surface8(320, 200), new Surface8(320, 200));
                    CostumeRenderer.Draw(costume, state, direction, CostumeRenderer.Palette(costume, IdentityPalette), target, 160, 150, masked: false);
                    state.Increase(costume);
                    frames++;
                }
            }
        }

        Assert.Equal(159, costumes);
        Assert.Equal([66, 89, 136, 173], notCostumes);
        Assert.Equal(CostumeFrames, frames);
    }

    [Fact]
    public void The_closest_point_on_a_line_is_clamped_to_its_ends()
    {
        Assert.Equal(new Point(5, 0), BoxGeometry.ClosestPointOnLine(new(0, 0), new(10, 0), new(5, 7)));
        Assert.Equal(new Point(10, 0), BoxGeometry.ClosestPointOnLine(new(0, 0), new(10, 0), new(20, 7)));
        Assert.Equal(new Point(0, 4), BoxGeometry.ClosestPointOnLine(new(0, 0), new(0, 10), new(-3, 4)));
    }

    /// <summary>The box matrix of the opening room leads from the cliff top to the path below.</summary>
    [Fact]
    public void The_box_matrix_leads_from_box_to_box()
    {
        if (Open()?.LoadRoom(2) is not { } room)
        {
            return;
        }

        var boxes = room.WalkBoxes;
        var path = new List<int> { 0 };
        while (path[^1] != 6 && path.Count < boxes.Count)
        {
            path.Add(boxes.NextBox(path[^1], 6));
        }

        Assert.Equal(6, path[^1]);
        Assert.Equal(3, boxes.NextBox(3, 3));
        Assert.Equal(-1, boxes.NextBox(0, WalkBoxes.InvalidBox));
    }

    /// <summary>
    /// After the opening, Bobbin stands on the cliff; a click on the path below sends him along
    /// the rock, down the way the room's boxes lead, and he stops at the point of the boxes
    /// nearest the click.
    /// </summary>
    [Fact]
    public void Bobbin_walks_down_from_the_cliff()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        world.Boot();
        Run(world, 100);
        world.Click(160, 92);
        Run(world, 1199);

        var bobbin = world.Actors[1];
        Assert.Equal((2, 828, 60, 0), (bobbin.Room, bobbin.X, bobbin.Y, bobbin.Moving));
        Assert.True(bobbin.Visible);
        Assert.Equal(BobbinOnTheCliff, Hash(world.Screen()));

        world.Click(110, 115);
        var boxes = new List<int>();
        for (int i = 0; i < 80; i++)
        {
            world.Step();
            if (boxes.Count == 0 || boxes[^1] != bobbin.WalkBox)
            {
                boxes.Add(bobbin.WalkBox);
            }
        }

        Assert.Equal([0, 1, 2, 3, 4, 5, 6], boxes);
        Assert.Equal((756, 107, 0, 270), (bobbin.X, bobbin.Y, bobbin.Moving, bobbin.Facing));
    }

    private const int CostumeFrames = 5360;

    private const string BobbinOnTheCliff = "3c1a99027a2415ba";

    private static readonly byte[] IdentityPalette = [.. Enumerable.Range(0, 16).Select(i => (byte)i)];

    private static string Hash(Surface8 surface) => Convert.ToHexStringLower(SHA256.HashData(surface.Pixels))[..16];

    private static void Run(GameWorld world, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            world.Step();
        }
    }

    private static ResourceLoader? Open()
        => GameFolders.Find(GameFolders.Ega) is { } path ? ResourceLoader.Open(new DirectoryGameContainer(path)) : null;
}
