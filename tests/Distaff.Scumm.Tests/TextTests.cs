using System.Security.Cryptography;
using Distaff.Scumm.Game;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Storage;
using Distaff.Scumm.View;

namespace Distaff.Scumm.Tests;

/// <summary>The charset and the text the game draws. Tests against the game data pass without checking when it is absent.</summary>
public class TextTests
{
    /// <summary>99.LFL holds 128 characters 8 pixels high; a string's width stops at a line break or a wait and skips '@' and a colour's word.</summary>
    [Fact]
    public void The_charset_measures_text()
    {
        if (Open()?.LoadCharset(0) is not { } data)
        {
            return;
        }

        var charset = Charset.Parse(data);
        Assert.Equal((128, 8), (charset.Count, charset.Height));
        Assert.Equal(4, charset.CharWidth(' '));
        Assert.Equal(PracticeWidth, charset.StringWidth(0, "PRACTICE"u8));
        Assert.Equal(PracticeWidth, charset.StringWidth(0, [.. "PRAC@TICE"u8, 0xFF, 1, .. "More"u8]));
        Assert.Equal(PracticeWidth, charset.StringWidth(0, [.. "PRACTICE"u8, 0xFF, 3, .. "More"u8]));
        Assert.Equal(PracticeWidth, charset.StringWidth(0, [.. "PRACT"u8, 0xFF, 12, 9, 0, .. "ICE"u8]));
    }

    /// <summary>A shadow falls right, below and below right; an outline moves the pixel a step right and down and rings it.</summary>
    [Fact]
    public void Shadows_and_outlines_ring_the_pixels()
    {
        byte[] dot = [0x80, 0, 0, 0, 0, 0, 0, 0];
        Assert.Equal(["......", "......", "..F0..", "..00..", "......", "......"], Drawn(dot, ShadowType.Normal));
        Assert.Equal(["......", "......", "...0..", "..0F0.", "...00.", "......"], Drawn(dot, ShadowType.Outline));
        Assert.Equal(["......", "......", "..F...", "......", "......", "......"], Drawn(dot, ShadowType.None));
    }

    /// <summary>
    /// The proficiency screen: the three levels are drawn into the room's band, boxed by its
    /// objects, and the narration below, its two lines each centred.
    /// </summary>
    [Fact]
    public void The_proficiency_screen_shows_its_levels_and_the_narration()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        world.Boot();
        Run(world, 60);

        Assert.Equal("Select a Proficiency Level. Refer to your Loom]^ manual for help.", world.MessageText);
        Assert.Equal(ProficiencyScreen, Hash(world.Screen()));
    }

    /// <summary>
    /// In the opening, Bobbin's line shows centred in the band below the room, where the line
    /// before it left its place, and goes when its time is up.
    /// </summary>
    [Fact]
    public void Bobbin_speaks_below_the_room_until_his_line_is_done()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        world.Boot();
        Run(world, 100);
        world.Click(160, 92);
        Run(world, 699);

        Assert.Equal("One of the Elders must've woven it.", world.MessageText);
        Assert.Equal(VirtScreenNumber.Verb, world.Screens.Find(world.TextSlots[0].Y)?.Number);
        Assert.Equal(BobbinsLine, Hash(world.Screen()));

        Run(world, 300);
        Assert.All(world.Screens.Verb.Front!.Pixels, p => Assert.Equal(0, p));
    }

    private const int PracticeWidth = 52;

    private const string ProficiencyScreen = "71e98c2a36552048";

    private const string BobbinsLine = "88fe6c75335921e2";

    /// <summary>A one-pixel-wide glyph drawn at (2, 2) on a small surface, row by row: F the colour, 0 the shadow.</summary>
    private static string[] Drawn(byte[] glyph, ShadowType shadow)
    {
        var surface = new Surface8(6, 6);
        surface.Fill((byte)'.');
        Charset.DrawBits1(surface, 2, 2, glyph, 1, 8, 0x0F, shadow, (byte)'0');
        return [.. Enumerable.Range(0, surface.Height).Select(y => new string([.. surface.Row(y).ToArray().Select(p => p == 0x0F ? 'F' : (char)p)]))];
    }

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
