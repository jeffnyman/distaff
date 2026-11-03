using Distaff.Scumm.Game;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Hosting;
using Distaff.Scumm.Input;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Scripts;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>
/// Version 3's banners on the EGA and the FM Towns (<c>showOldStyleBannerAndPause</c>): the pause
/// key's, the restart key's question, and the snap scroll key's. Tests against the game data pass
/// without checking when it is absent.
/// </summary>
public class BannerTests
{
    /// <summary>
    /// The space bar pauses with the game's own line in a band 12 rows high, edged in colour 12:
    /// at row 80 on black on the EGA, 12 rows above the main band's middle on colour 8 on the FM
    /// Towns. The game stands still until a key, and then goes on.
    /// </summary>
    [Theory]
    [InlineData(GameFolders.Ega)]
    [InlineData(GameFolders.FmTowns)]
    public void The_pause_key_shows_its_banner_until_a_key(string folder)
    {
        if (Playing(folder) is not { } world)
        {
            return;
        }

        int frames = world.Frames;
        world.KeyDown(Key.Space, KeyModifiers.None, ' ');
        Assert.Equal("Game paused. Press SPACEBAR to continue.", world.BannerText);
        for (int i = 0; i < 30; i++)
        {
            world.Step();
        }

        Assert.Equal(frames, world.Frames);

        var frame = world.Frame(cursor: false);
        bool towns = folder == GameFolders.FmTowns;
        int top = towns ? (2 * world.ViewTop + world.ViewHeight) / 2 - 12 : 80;
        Rgb edge = towns ? GameWorld.To15Bit(GameWorld.TownsTextPalette[12]) : world.DisplayPalette()[12];
        Rgb box = towns ? GameWorld.To15Bit(GameWorld.TownsTextPalette[8]) : world.DisplayPalette()[0];
        Assert.All(frame.Row(top).ToArray(), p => Assert.Equal(edge, p));
        Assert.All(frame.Row(top + 11).ToArray(), p => Assert.Equal(edge, p));
        Assert.Equal(box, frame[0, top + 5]);
        Assert.Contains(edge, frame.Row(top + 5).ToArray());

        world.KeyDown(Key.Space, KeyModifiers.None, ' ');
        world.Step();
        Assert.Null(world.BannerText);
        world.StepFrame();
        Assert.True(world.Frames > frames);
    }

    /// <summary>
    /// F8 asks whether to restart, the game's question but for its last letter, which is the
    /// answer that restarts: N goes on, Y asks for the game to start again.
    /// </summary>
    [Theory]
    [InlineData(GameFolders.Ega)]
    [InlineData(GameFolders.FmTowns)]
    public void F8_asks_whether_to_restart(string folder)
    {
        if (Playing(folder) is not { } world)
        {
            return;
        }

        world.KeyDown(Key.F8, KeyModifiers.None, '\0');
        Assert.Equal("Are you sure you want to restart? (Y or N)", world.BannerText);
        world.KeyDown(Key.N, KeyModifiers.None, 'n');
        world.Step();
        Assert.Null(world.BannerText);
        Assert.False(world.RestartRequested);

        world.KeyDown(Key.F8, KeyModifiers.None, '\0');
        world.KeyDown(Key.Y, KeyModifiers.None, 'y');
        world.Step();
        Assert.True(world.RestartRequested);
    }

    /// <summary>
    /// On the VGA CD, Y to the restart question dissolves the room to black pixel by pixel first
    /// (<c>fadeOut(134)</c> in <c>queryRestart</c>), the screen shown after each eighteenth of its
    /// pixels, and the restart is asked for once the dissolve has been shown.
    /// </summary>
    [Fact]
    public void The_vga_cd_dissolves_out_before_it_restarts()
    {
        if (GameFolders.Find(GameFolders.Vga) is not { } path)
        {
            return;
        }

        // PRACTICE, and the opening skipped, to Bobbin on the cliff
        var world = new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path)));
        world.Boot();
        new Walkthrough(world).Wait(300).ClickAt(160, 95).Wait(100).Key(Key.Escape, (char)27).Answer(Walkthrough.VgaTextKeyMessage, 8000).Ready(8000);
        Assert.Equal(2, world.CurrentRoomNumber);

        world.KeyDown(Key.F8, KeyModifiers.None, '\0');
        Assert.Equal("Are you sure you want to restart? (Y or N)", world.BannerText);
        world.KeyDown(Key.Y, KeyModifiers.None, 'y');
        world.Step();
        Assert.Null(world.BannerText);
        Assert.True(world.InEffect);
        Assert.False(world.RestartRequested);

        var black = world.DisplayPalette()[0];
        bool Dark(RgbFrame f) => Enumerable.Range(world.ViewTop, world.ViewHeight).All(y => f.Row(y).ToArray().All(p => p == black));
        var frames = new List<RgbFrame>();
        while (world.InEffect)
        {
            frames.Add(world.Frame(cursor: false));
            Assert.False(world.RestartRequested);
            world.Step();
        }

        Assert.True(world.RestartRequested);
        Assert.Equal(18, frames.Count);
        Assert.False(Dark(frames[0]));
        Assert.True(Dark(frames[^1]));
    }

    /// <summary>
    /// Alt+I switches the screen between snapping to the camera and scrolling, says which for 90
    /// quarter-frames, and sets the camera's variable to match.
    /// </summary>
    [Fact]
    public void Alt_I_switches_snap_scrolling()
    {
        if (Playing(GameFolders.Ega) is not { } world)
        {
            return;
        }

        world.KeyDown(Key.I, KeyModifiers.Alt, 'i');
        Assert.Equal("Screen reposition instantly", world.BannerText);
        int steps = 0;
        while (world.BannerText is not null)
        {
            world.Step();
            steps++;
        }

        Assert.Equal((90 + world.StepTicks * 4 - 1) / (world.StepTicks * 4), steps);
        Assert.Equal(1, world.Engine.Variables[Vars.CameraFastX]);

        world.KeyDown(Key.I, KeyModifiers.Alt, 'i');
        Assert.Equal("Screen reposition by Scrolling", world.BannerText);
        world.KeyDown(Key.Space, KeyModifiers.None, ' ');
        world.Step();
        Assert.Equal(0, world.Engine.Variables[Vars.CameraFastX]);
    }

    /// <summary>A restart answered yes starts the game again: the engine boots a new world, back on the proficiency screen, its random numbers going on.</summary>
    [Fact]
    public void The_engine_restarts_the_game()
    {
        if (GameFolders.Find(GameFolders.Ega) is not { } path)
        {
            return;
        }

        var host = new HeadlessHost();
        using var engine = new ScummEngine(host, new DirectoryGameContainer(path), new DetectedGame("scumm", "loom", "Loom", "EGA"));
        host.Run(engine, 60);
        host.Input.Push(new InputEvent { Type = InputEventType.MouseDown, Button = MouseButton.Left, X = 160, Y = 92 });
        host.Continue(engine, 200);
        var played = engine.World;
        Assert.NotEqual(69, played.CurrentRoomNumber);
        var random = played.Engine.Random;

        host.Input.PushKey(Key.F8);
        host.Input.PushKey(Key.Y, KeyModifiers.None, 'y');
        host.Continue(engine, 3);
        Assert.NotSame(played, engine.World);

        // the random numbers go on from where they were, not seeded again
        Assert.Same(random, engine.World.Engine.Random);
        host.Continue(engine, 5);
        Assert.Equal(69, engine.World.CurrentRoomNumber);
    }

    /// <summary>
    /// The VGA CD's message dialogs (<c>printString</c> in slot 3, <c>showMessageDialog</c>): a
    /// banner until a key, whose code then goes to the scripts' key variable, and the game goes
    /// on. The opening played through ends on one, "Press F9 to toggle text on screen ON/OFF";
    /// F9 then turns the text off and on (script 5, variable 60) and says so in another.
    /// </summary>
    [Fact]
    public void The_vga_cds_text_key_says_so_in_a_message_dialog()
    {
        if (Playing(GameFolders.Vga) is not { } world)
        {
            return;
        }

        var w = new Walkthrough(world).Until(() => world.BannerText is not null, 8000, "the opening's message");
        Assert.Equal(Walkthrough.VgaTextKeyMessage, world.BannerText);
        Assert.Equal(2, world.CurrentRoomNumber);
        world.KeyDown(Key.Space, KeyModifiers.None, ' ');
        world.Step();
        Assert.Null(world.BannerText);
        Assert.Equal(' ', world.Engine.Variables[Vars.KeyPress]);

        // keys reach the scripts once the opening hands Bobbin over
        w.Ready();
        for (int press = 0; press < 2; press++)
        {
            // the scripts' own flag, set off (1) by them to begin with: the key flips it and says which way
            int textOff = world.Engine.Variables[60];
            string said = textOff != 0 ? "Text WILL appear on the screen." : "Text WILL NOT appear on the screen.";
            world.KeyDown(Key.F9, KeyModifiers.None, (char)323);
            for (int i = 0; i < 5 && world.BannerText is null; i++)
            {
                world.Step();
            }

            Assert.Equal(said, world.BannerText);
            Assert.Equal(textOff == 0 ? 1 : 0, world.Engine.Variables[60]);
            int frames = world.Frames;
            for (int i = 0; i < 10; i++)
            {
                world.Step();
            }

            Assert.Equal(frames, world.Frames);
            world.KeyDown(Key.Space, KeyModifiers.None, ' ');
            world.Step();
            Assert.Null(world.BannerText);
            Assert.Equal(' ', world.Engine.Variables[Vars.KeyPress]);
        }

        Assert.Empty(world.Unhandled);
        world.StepFrame();
    }

    /// <summary>A release booted at Practice and played into the opening.</summary>
    private static GameWorld? Playing(string folder)
    {
        if (GameFolders.Find(folder) is not { } path)
        {
            return null;
        }

        var world = new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path)));
        world.Boot();
        for (int i = 0; i < 100; i++)
        {
            world.StepFrame();
        }

        if (world.CurrentRoomNumber == 69)
        {
            world.Click(160, 92);
            for (int i = 0; i < 50; i++)
            {
                world.StepFrame();
            }
        }

        return world;
    }
}
