using System.Security.Cryptography;
using Distaff.Scumm.Game;
using Distaff.Scumm.Input;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Saves;
using Distaff.Scumm.Scripts;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>The VGA CD's own menu and banners, as ScummVM rebuilds the original interface. Tests against the game data pass without checking when it is absent.</summary>
public class VgaMenuTests
{
    // the middle of the main band, which the menu is laid out around (yConstant)
    private const int Middle = 72;

    // a point on each button, and on the first slot
    private static readonly (int X, int Y) SaveButton = (262, Middle - 17), LoadButton = (262, Middle - 2), PlayButton = (262, Middle + 13),
        QuitButton = (262, Middle + 28), FirstSlot = (60, Middle - 41), ArrowDownBottom = (214, Middle + 52);

    private const string MainPage = "fd963e1917c5b99e";

    /// <summary>
    /// F5, the key the game puts in variable 50, opens the menu over the room with the cursor on;
    /// Play closes it, and the screen is as it was, the cursor aside.
    /// </summary>
    [Fact]
    public void F5_opens_the_menu_and_play_closes_it()
    {
        if (OnTheCliff() is not { } world)
        {
            return;
        }

        string before = Hash(world, cursor: false);
        world.KeyDown(Key.F5, KeyModifiers.None, '\0');
        world.Step();
        Assert.True(world.MenuActive);
        Assert.Equal(1, world.CursorState);
        Assert.Equal(MainPage, Hash(world));

        world.Click(PlayButton.X, PlayButton.Y);
        new Walkthrough(world).Until(() => !world.MenuActive, 20, "the menu to close");
        Assert.Equal(before, Hash(world, cursor: false));
    }

    /// <summary>
    /// Save lists the slots numbered; a slot clicked takes a typed name, and Enter shows "Saving"
    /// a moment, saves under that name in ScummVM's format, and closes the menu.
    /// </summary>
    [Fact]
    public void A_game_is_saved_under_the_name_typed()
    {
        var store = new MemorySaveStore();
        if (OnTheCliff(store) is not { } world)
        {
            return;
        }

        world.KeyDown(Key.F5, KeyModifiers.None, '\0');
        world.Step();
        Click(world, SaveButton, 20);
        Assert.Equal(" 1. ", world.SavegameLabels[0]);
        Assert.Equal(" 9. ", world.SavegameLabels[8]);

        Click(world, FirstSlot, 1);
        Type(world, "On the cliff");
        world.KeyDown(Key.Backspace, KeyModifiers.None, '\b');
        world.Step();
        world.KeyDown(Key.Return, KeyModifiers.None, '\r');
        world.Step();
        Assert.True(world.MenuActive);
        Run(world, 20);
        Assert.False(world.MenuActive);

        using var input = store.OpenRead("loom-vga.s01")!;
        using var copy = new MemoryStream();
        input.CopyTo(copy);
        Assert.Equal("On the clif", SaveFile.ReadInfo(copy.ToArray()).Name);
    }

    /// <summary>
    /// Load lists the saves by name; a click on one loads it, and the game goes on from it, the
    /// distaff and notes drawn again.
    /// </summary>
    [Fact]
    public void A_saved_game_is_loaded_by_clicking_it()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var store = new MemorySaveStore();
        var saved = new GameWorld(loader) { SaveStore = store };
        ToTheDistaff(Skip(saved));
        using (var output = store.OpenWrite("loom-vga.s01"))
        {
            output.Write(saved.Save("Sanctuary", new DateTime(2026, 10, 27)));
        }

        var world = OnTheCliff(store)!;
        world.KeyDown(Key.F5, KeyModifiers.None, '\0');
        world.Step();
        Click(world, LoadButton, 20);
        Assert.Equal(" 1. Sanctuary", world.SavegameLabels[0]);
        Click(world, FirstSlot, 20);
        Assert.False(world.MenuActive);
        world.Step();
        Assert.Equal(9, world.CurrentRoomNumber);
        Assert.Contains(world.Verbs, v => v.VerbId == 1 && v.Mode == 1);
    }

    /// <summary>The down arrow scrolls the list by how far down it is pressed: at its foot, six slots (LOOM VGA's own scrolling).</summary>
    [Fact]
    public void The_arrows_scroll_the_list()
    {
        if (OnTheCliff() is not { } world)
        {
            return;
        }

        world.KeyDown(Key.F5, KeyModifiers.None, '\0');
        world.Step();
        Click(world, LoadButton, 20);
        Click(world, ArrowDownBottom, 20);
        Assert.Equal(" 7. ", world.SavegameLabels[0]);
        Assert.Equal("15. ", world.SavegameLabels[8]);
    }

    /// <summary>
    /// Quit quits, as ScummVM does by default; asked to confirm, it shows the game's question,
    /// and any answer but its last letter, Y, leaves the menu up.
    /// </summary>
    [Fact]
    public void Quit_quits_or_asks_first()
    {
        if (OnTheCliff() is not { } world)
        {
            return;
        }

        world.KeyDown(Key.F5, KeyModifiers.None, '\0');
        world.Step();
        Click(world, QuitButton, 20);
        Assert.True(world.QuitRequested);

        var asking = OnTheCliff()!;
        asking.ConfirmExit = true;
        asking.KeyDown(Key.F5, KeyModifiers.None, '\0');
        asking.Step();
        Click(asking, QuitButton, 20);
        Assert.Equal("Are you sure you want to quit? (Y or N)", asking.BannerText);
        asking.KeyDown(Key.N, KeyModifiers.None, 'n');
        asking.Step();
        Assert.Null(asking.BannerText);
        Assert.True(asking.MenuActive);
        Assert.False(asking.QuitRequested);

        Click(asking, QuitButton, 20);
        asking.KeyDown(Key.Y, KeyModifiers.None, 'y');
        asking.Step();
        Assert.True(asking.QuitRequested);
    }

    /// <summary>The pause key shows the game's pause banner with the cursor off; a key goes on, the screen and cursor as they were.</summary>
    [Fact]
    public void The_pause_key_shows_its_banner()
    {
        if (OnTheCliff() is not { } world)
        {
            return;
        }

        string before = Hash(world);
        world.KeyDown(Key.Space, KeyModifiers.None, ' ');
        world.Step();
        Assert.Equal("GAME PAUSED. Press SPACEBAR to continue.", world.BannerText);
        Assert.Equal(0, world.CursorState);
        int ticks = world.Engine.Variables[Vars.TimerTotal];
        Run(world, 10);
        Assert.Equal(ticks, world.Engine.Variables[Vars.TimerTotal]);

        world.KeyDown(Key.Space, KeyModifiers.None, ' ');
        world.Step();
        Assert.Null(world.BannerText);
        Assert.Equal(1, world.CursorState);
        Assert.Equal(before, Hash(world));
    }

    /// <summary>
    /// The menu pauses the sounds, and on the VGA CD that stops the CD timer only if the music
    /// timer has counted (<c>pauseCDSounds</c>): opened while Bobbin's line on the leaf plays, the
    /// music timer stands still while the menu is up and goes on from where it was after.
    /// </summary>
    [Fact]
    public void The_menu_stops_the_cd_timer_once_it_has_counted()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        var w = Skip(world);
        var track = world.CdTrack;
        w.Use(79, wait: false);
        w.Until(() => world.CdTrack != track && world.Engine.Variables[Vars.MusicTimer] > 20, 2000, "a voice, and the music timer to count");
        int before = world.Engine.Variables[Vars.MusicTimer];
        world.KeyDown(Key.F5, KeyModifiers.None, '\0');
        Assert.True(world.MenuActive);
        Run(world, 200);
        world.Click(PlayButton.X, PlayButton.Y);
        w.Until(() => !world.MenuActive, 20, "the menu to close");
        int frames = world.Frames;
        w.Until(() => world.Frames > frames, 20, "a frame");
        Assert.InRange(world.Engine.Variables[Vars.MusicTimer] - before, 1, 12);
    }

    /// <summary>
    /// Opened just as Bobbin's line on the leaf starts, before the music timer has counted, the menu leaves the CD
    /// timer running, as ScummVM's does: after it, the music timer has counted the time the menu
    /// was up, at 60 counts a second (240 calls of the timer, one count for every four).
    /// </summary>
    [Fact]
    public void The_menu_leaves_the_cd_timer_running_before_it_has_counted()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        var w = Skip(world);
        var track = world.CdTrack;
        w.Use(79, wait: false);
        w.Until(() => world.CdTrack != track, 2000, "a voice to start");
        Assert.Equal(0, world.Engine.Variables[Vars.MusicTimer]);
        world.KeyDown(Key.F5, KeyModifiers.None, '\0');
        Assert.True(world.MenuActive);
        int up = world.Steps;
        Run(world, 200);
        world.Click(PlayButton.X, PlayButton.Y);
        w.Until(() => !world.MenuActive, 20, "the menu to close");
        up = world.Steps - up;
        int frames = world.Frames;
        w.Until(() => world.Frames > frames, 20, "a frame");

        // each step of the menu is 6 ticks of the DOS timer, at 236.7 a second; the frame after adds its own
        int counts = (int)((up + 1) * 6 * 60 / GameWorld.TimerFrequency * 4);
        Assert.True(up > 200);
        Assert.InRange(world.Engine.Variables[Vars.MusicTimer], counts - 1, counts + 1);
    }

    /// <summary>On the cliff after the opening, skipped with Escape.</summary>
    private static GameWorld? OnTheCliff(ISaveStore? store = null)
    {
        if (Open() is not { } loader)
        {
            return null;
        }

        var world = new GameWorld(loader) { SaveStore = store };
        Skip(world);
        return world;
    }

    /// <summary>The proficiency screen, PRACTICE, and the opening skipped with Escape, to Bobbin on the cliff in control.</summary>
    private static Walkthrough Skip(GameWorld world)
    {
        world.Boot();
        return new Walkthrough(world).Wait(300).ClickAt(160, 95).Wait(100).Key(Key.Escape, (char)27).Ready(8000);
    }

    /// <summary>From the cliff to the sanctuary, the distaff taken (see <see cref="VgaGameLoopTests"/>).</summary>
    private static void ToTheDistaff(Walkthrough w)
    {
        w.Use(79);
        w.Use(78).InRoom(3).Ready();
        w.Use(86).InRoom(5).Ready();
        w.Use(126).InRoom(7).Ready();
        w.Use(177).InRoom(8).Ready();
        w.Use(179).InRoom(9).Ready(20000);
        w.Use(196);
    }

    private static void Click(GameWorld world, (int X, int Y) at, int steps)
    {
        world.Click(at.X, at.Y);
        Run(world, Math.Max(1, steps));
    }

    private static void Type(GameWorld world, string text)
    {
        foreach (char c in text)
        {
            world.KeyDown((Key)char.ToLowerInvariant(c), KeyModifiers.None, c);
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

    private static string Hash(GameWorld world, bool cursor = true)
    {
        var frame = world.Screen();
        if (cursor)
        {
            world.DrawCursor(frame);
        }

        var palette = world.DisplayPalette();
        var rgb = frame.Pixels.SelectMany(p => new[] { palette[p].R, palette[p].G, palette[p].B }).ToArray();
        return Convert.ToHexStringLower(SHA256.HashData(rgb))[..16];
    }

    private static ResourceLoader? Open()
        => GameFolders.Find(GameFolders.Vga) is { } path ? ResourceLoader.Open(new DirectoryGameContainer(path)) : null;
}
