using System.Security.Cryptography;
using Distaff.Scumm.Game;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Scripts;
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

        // as long as the lines before it last, at the text speed (ScummVM's 6 by default)
        for (int i = 0; i < 2000 && world.MessageText != "One of the Elders must've woven it."; i++)
        {
            world.Step();
        }

        Assert.Equal("One of the Elders must've woven it.", world.MessageText);
        Assert.Equal(VirtScreenNumber.Verb, world.Screens.Find(world.TextSlots[0].Y)?.Number);
        Assert.Equal(BobbinsLine, Hash(world.Screen()));

        Run(world, 300);
        Assert.All(world.Screens.Verb.Front!.Pixels, p => Assert.Equal(0, p));
    }

    /// <summary>
    /// The text speed is ScummVM's setting, 6 unless given: whatever the boot script puts in
    /// variable 37 (<c>VAR_CHARINC</c>; 5 on the EGA and the VGA CD, 2 on the FM Towns), the game
    /// starts with 9 less the setting, and a load puts it back whatever the save held.
    /// </summary>
    [Theory]
    [InlineData(GameFolders.Ega)]
    [InlineData(GameFolders.Vga)]
    [InlineData(GameFolders.FmTowns)]
    public void The_text_speed_is_the_settings_at_boot_and_after_a_load(string folder)
    {
        if (GameFolders.Find(folder) is not { } path)
        {
            return;
        }

        var loader = ResourceLoader.Open(new DirectoryGameContainer(path));
        var world = new GameWorld(loader);
        world.Boot();
        Run(world, 5);
        Assert.Equal(3, world.Engine.Variables[Vars.CharInc]);

        var fast = new GameWorld(loader) { TextSpeed = 9 };
        fast.Boot();
        Run(fast, 5);
        Assert.Equal(0, fast.Engine.Variables[Vars.CharInc]);

        // a save made at another speed loads at this world's
        var save = fast.Save("fast", new DateTime(2026, 10, 28));
        var loaded = new GameWorld(loader) { TextSpeed = 2 };
        loaded.Load(save);
        Assert.Equal(7, loaded.Engine.Variables[Vars.CharInc]);
    }

    /// <summary>A speed the scripts write once a room is up becomes the setting, as ScummVM keeps it in its own (<c>writeVar</c>).</summary>
    [Fact]
    public void A_speed_the_scripts_write_later_becomes_the_setting()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        world.Boot();
        Run(world, 5);
        Assert.NotEqual(0, world.CurrentRoomNumber);
        world.Engine.WriteVar(Vars.CharInc, 8);
        Assert.Equal((8, 1), (world.Engine.Variables[Vars.CharInc], world.TextSpeed));
        world.Engine.WriteVar(Vars.CharInc, 200);
        Assert.Equal(1, world.TextSpeed);
    }

    private const int PracticeWidth = 52;

    private const string ProficiencyScreen = "71e98c2a36552048";

    private const string BobbinsLine = "384373af7a560820";

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
            world.StepFrame();
        }
    }

    private static ResourceLoader? Open()
        => GameFolders.Find(GameFolders.Ega) is { } path ? ResourceLoader.Open(new DirectoryGameContainer(path)) : null;
}
