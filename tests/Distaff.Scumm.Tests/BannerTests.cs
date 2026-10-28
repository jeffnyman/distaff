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

    /// <summary>A restart answered yes starts the game again: the engine boots a new world, back on the proficiency screen.</summary>
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

        host.Input.PushKey(Key.F8);
        host.Input.PushKey(Key.Y, KeyModifiers.None, 'y');
        host.Continue(engine, 3);
        Assert.NotSame(played, engine.World);
        host.Continue(engine, 5);
        Assert.Equal(69, engine.World.CurrentRoomNumber);
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
