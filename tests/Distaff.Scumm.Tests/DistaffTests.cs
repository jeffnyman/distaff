using System.Security.Cryptography;
using Distaff.Scumm.Game;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Input;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Scripts;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>The distaff and the player's input: verbs, keys, the cursor and the flashlight. Tests against the game data pass without checking when it is absent.</summary>
public class DistaffTests
{
    /// <summary>
    /// From the cliff down through the village and the tents to the Sanctuary, where the council
    /// plays out and everyone flies away; then double-clicks on the distaff. The clicks are at
    /// steps, in screen pixels.
    /// </summary>
    internal static readonly (int Step, int X, int Y)[] ToTheDistaff =
    [
        (100, 160, 92), (1300, 110, 115), (1400, 10, 120), (1550, 10, 125), (1700, 30, 40), (1800, 231, 110),
        (1900, 10, 100), (2100, 315, 120), (2300, 315, 120), (2500, 315, 120), (2700, 315, 120), (2900, 315, 120),
        (3100, 315, 120), (7000, 240, 128), (7002, 240, 128),
    ];

    /// <summary>
    /// Bobbin picks up the distaff: it shows below the room with its eight parts as verbs and the
    /// staff under them. With the egg pointed at, e c e d on the distaff weaves Opening, and
    /// Hetchel speaks from the egg.
    /// </summary>
    [Fact]
    public void Bobbin_takes_the_distaff_and_opens_the_egg()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        world.Boot();
        Play(world, ToTheDistaff, 7400);

        Assert.Contains("Feels heavier than it looks.", world.MessageLog);
        var on = world.Verbs.Where(v => v.VerbId != 0 && v.Mode == 1 && v.SaveId == 0).Select(v => v.VerbId).ToList();
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 49, 50], on);
        Assert.Equal(new Rect(16, 144, 32, 24), world.Verbs.First(v => v.VerbId == 1).CurRect);
        Assert.Equal(1, world.Verbs[world.FindVerbAtPos(30, 150)].VerbId);
        Assert.Equal(TheDistaff, Hash(world.Screen()));

        int sounds = world.SoundsStarted.Count;
        Play(world, [(7400, 138, 126), (7500, 84, 155), (7530, 30, 155), (7560, 84, 155), (7590, 60, 155)], 8000);
        Assert.Equal([3, 1, 3, 2], world.SoundsStarted.Skip(sounds).Take(4));
        Assert.Contains("Thank goodness you're still here.", world.MessageLog);
    }

    /// <summary>Escape in the opening skips it: Bobbin is on the cliff at once, and nothing has been said.</summary>
    [Fact]
    public void Escape_skips_the_opening()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        world.Boot();
        Play(world, [(100, 160, 92)], 200);
        world.KeyDown(Key.Escape, KeyModifiers.None, (char)27);
        Run(world, 10);

        Assert.Equal((2, 828, 60), (world.CurrentRoomNumber, world.Actors[1].X, world.Actors[1].Y));
        Assert.DoesNotContain("Rise, son of Cygna.", world.MessageLog);
    }

    /// <summary>
    /// Loom's cursor is the font's character 1, its hotspot the top left, shown from the
    /// proficiency screen on; its colour changes every other step through white, white, grey and
    /// dark grey.
    /// </summary>
    [Fact]
    public void The_cursor_is_a_character_of_the_font_in_cycling_colours()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        world.Boot();
        Run(world, 60);
        Assert.True(world.CursorState > 0);
        Assert.Equal((0, 0), world.CursorHotspot);

        var colors = new List<byte>();
        for (int i = 0; i < 8; i++)
        {
            world.Step();
            colors.Add(world.CursorColor);
        }

        Assert.Equal((4, 2, 2), (colors.Count(c => c == 15), colors.Count(c => c == 7), colors.Count(c => c == 8)));

        var charset = Charset.Parse(loader.LoadCharset(0)!.Value);
        var glyph = charset.Glyph(1);
        world.MoveMouse(100, 50);
        var frame = new Surface8(320, 200);
        world.DrawCursor(frame);
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < charset.CharWidth(1); x++)
            {
                Assert.Equal((glyph[y] & (0x80 >> x)) != 0, frame[100 + x, 50 + y] != 0);
            }
        }
    }

    /// <summary>The flashlight's corners are rounded, a little more on the right than the left, as the original's masks have it.</summary>
    [Fact]
    public void The_flashlight_rounds_its_corners()
    {
        var lit = new Surface8(56, 56);
        lit.Fill(9);
        var dark = new Surface8(56, 56);
        Flashlight.Copy(lit, dark, lit.Bounds);

        string Row(int y) => new([.. Enumerable.Range(0, 56).Select(x => dark[x, y] == 0 ? '.' : '#')]);
        Assert.Equal(new string('.', 8) + new string('#', 40) + new string('.', 8), Row(0));
        Assert.Equal(new string('.', 5) + new string('#', 45) + new string('.', 6), Row(1));
        Assert.Equal(Row(0), Row(55));
        Assert.Equal(new string('#', 56), Row(8));
    }

    /// <summary>
    /// With the room's lights off the room is black but for the actors; with the flashlight on, a
    /// window of 7 by 7 strips around Bobbin shows the room.
    /// </summary>
    [Fact]
    public void A_dark_room_shows_only_what_the_flashlight_lights()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        world.Boot();
        Play(world, [(100, 160, 92)], 200);
        world.KeyDown(Key.Escape, KeyModifiers.None, (char)27);
        Run(world, 10);
        var lit = world.Screen();

        world.Engine.Variables[Vars.CurrentLights] = GameWorld.LightsActorColors;
        var dark = world.Screen();
        Assert.Equal(0, dark[10, 10]);
        Assert.NotEqual(0, lit[10, 10]);

        world.Engine.Variables[Vars.CurrentLights] = GameWorld.LightsActorColors | GameWorld.LightsFlashlightOn;
        var torch = world.Screen();

        // Bobbin at (828, 60), on the strip at 824, the view's left at 640: the window is 56 square from (160, 20)
        var window = new Rect(160, 20, 56, 56);
        for (int y = 0; y < 144; y++)
        {
            for (int x = 0; x < 320; x++)
            {
                bool inside = window.Contains(x, y) && !(y < window.Y + 8 || y >= window.Bottom - 8);
                if (inside)
                {
                    Assert.Equal(lit[x, y], torch[x, y]);
                }
                else if (!window.Contains(x, y))
                {
                    Assert.Equal(dark[x, y], torch[x, y]);
                }
            }
        }
    }

    private const string TheDistaff = "c8d2056ad0421b1e";

    private static string Hash(Surface8 surface) => Convert.ToHexStringLower(SHA256.HashData(surface.Pixels))[..16];

    /// <summary>Runs from where the world is to <paramref name="until"/> steps, clicking at the steps given.</summary>
    private static void Play(GameWorld world, (int Step, int X, int Y)[] clicks, int until)
    {
        while (world.Steps < until)
        {
            foreach (var (_, x, y) in clicks.Where(c => c.Step == world.Steps))
            {
                world.Click(x, y);
            }

            world.Step();
        }
    }

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
