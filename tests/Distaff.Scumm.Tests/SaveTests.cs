using System.IO.Compression;
using Distaff.Scumm.Game;
using Distaff.Scumm.Input;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Saves;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>Saves in ScummVM's format, and Loom's own save screen. Tests against the game data pass without checking when it is absent.</summary>
public class SaveTests
{
    private static readonly DateTime When = new(2026, 10, 27, 12, 0, 0);

    /// <summary>
    /// A save read back into a new world saves again to the same state, field for field, the mouse
    /// put back: on the cliff after the opening, and in the Loom with the distaff.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_save_reads_back_to_the_same_save(bool withDistaff)
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var world = withDistaff ? AtTheDistaff(loader) : OnTheCliff(loader);
        var file = world.Save("test", When);
        var again = new GameWorld(loader);
        again.Load(file);

        // with the original interface ScummVM leaves the mouse where it is
        again.MoveMouse(world.MouseX, world.MouseY);

        Assert.Equal(State(file), State(again.Save("test", When)));
        var info = SaveFile.ReadInfo(file);
        Assert.Equal(("test", SaveFile.CompatibleVersion), (info.Name, info.Version));
    }

    /// <summary>
    /// A save loaded as ScummVM's launcher loads one plays on as the game did: on the cliff, a click
    /// on the path walks Bobbin to the same place.
    /// </summary>
    [Fact]
    public void A_loaded_save_plays_on_as_the_game_did()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var file = OnTheCliff(loader).Save("cliff", When);
        var world = new GameWorld(loader);
        world.Load(file);
        world.Step();
        world.Click(110, 115);
        Run(world, 88);

        var bobbin = world.Actors[1];
        Assert.Equal((2, 756, 107, 0, 270), (world.CurrentRoomNumber, bobbin.X, bobbin.Y, bobbin.Moving, bobbin.Facing));
    }

    /// <summary>
    /// Loom's save screen: F5 opens it; SAVE, the first slot, a name typed, Enter and OK save
    /// under that name; in another game, LOAD, the slot and OK load it, and the game goes on in
    /// the Loom with the distaff. Loaded as ScummVM's launcher loads it, the same save ends the
    /// save screen and goes on there too.
    /// </summary>
    [Fact]
    public void The_save_screen_saves_and_loads()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var store = new MemorySaveStore();
        var world = AtTheDistaff(loader, store);
        world.KeyDown(Key.F5, KeyModifiers.None, (char)0);
        Play(world, [(7450, 262, 48), (7500, 110, 20)], 7530);
        foreach (char c in "dist")
        {
            world.KeyDown((Key)c, KeyModifiers.None, c);
            Run(world, 5);
        }

        world.KeyDown(Key.Return, KeyModifiers.None, (char)13);
        Play(world, [(7600, 262, 96)], 7700);

        Assert.Contains("The game was saved.", world.MessageLog);
        var file = ReadAll(store, "loom-ega.s01");
        Assert.Equal("dist", SaveFile.ReadInfo(file).Name);

        var other = OnTheCliff(loader, store);
        other.KeyDown(Key.F5, KeyModifiers.None, (char)0);
        Play(other, [(other.Steps + 40, 262, 72), (other.Steps + 60, 110, 20), (other.Steps + 100, 262, 96)], other.Steps + 200);
        Assert.Equal(9, other.CurrentRoomNumber);
        Assert.Equal(DistaffVerbs, VerbsOn(other));

        var launched = new GameWorld(loader) { SaveStore = store };
        launched.Load(file);
        Assert.Equal(GameWorld.SaveRoom, launched.CurrentRoomNumber);
        Run(launched, 30);
        Assert.Equal(9, launched.CurrentRoomNumber);
        Assert.Equal(DistaffVerbs, VerbsOn(launched));
    }

    /// <summary>
    /// A save made between steps, as ScummVM's own menu makes them, loaded from the launcher goes
    /// through the save room as if the save screen had loaded it, and the distaff comes back.
    /// </summary>
    [Fact]
    public void A_menu_save_loads_through_the_save_room()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var file = AtTheDistaff(loader).Save("menu", When);
        var world = new GameWorld(loader);
        world.Load(file);
        Run(world, 30);

        Assert.Equal(9, world.CurrentRoomNumber);
        Assert.Equal(DistaffVerbs, VerbsOn(world));
    }

    private static readonly int[] DistaffVerbs = [1, 2, 3, 4, 5, 6, 7, 8, 49, 50];

    private static int[] VerbsOn(GameWorld world)
        => [.. world.Verbs.Where(v => v.VerbId != 0 && v.Mode == 1 && v.SaveId == 0 && v.VerbId != 54).Select(v => v.VerbId)];

    /// <summary>On the cliff after the opening, skipped with Escape.</summary>
    private static GameWorld OnTheCliff(ResourceLoader loader, ISaveStore? store = null)
    {
        var world = new GameWorld(loader) { SaveStore = store };
        world.Boot();
        Play(world, [(100, 160, 92)], 200);
        world.KeyDown(Key.Escape, KeyModifiers.None, (char)27);
        Run(world, 100);
        return world;
    }

    /// <summary>In the Loom, the distaff picked up, as in <see cref="DistaffTests"/>.</summary>
    private static GameWorld AtTheDistaff(ResourceLoader loader, ISaveStore? store = null)
    {
        var world = new GameWorld(loader) { SaveStore = store };
        world.Boot();
        Play(world, DistaffTests.ToTheDistaff, 7400);
        return world;
    }

    /// <summary>A save's state, uncompressed, after its header and information section.</summary>
    private static byte[] State(byte[] file)
    {
        using var input = new GZipStream(new MemoryStream(file), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray()[(12 + 32 + 26)..];
    }

    private static byte[] ReadAll(ISaveStore store, string name)
    {
        using var input = store.OpenRead(name) ?? throw new InvalidOperationException($"{name} was not saved.");
        using var copy = new MemoryStream();
        input.CopyTo(copy);
        return copy.ToArray();
    }

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
