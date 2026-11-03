using Distaff.Scumm.Game;
using Distaff.Scumm.Inspection;

namespace Distaff.Scumm.Tests;

/// <summary>
/// The libraries' front door and what it gives, as code using the package calls them: a release
/// opened and named, a room drawn and a script listed, and a game played headless and looked at.
/// Tests against the game data pass without checking when it is absent.
/// </summary>
public class PublicApiTests
{
    [Theory]
    [InlineData(GameFolders.Ega, "Loom (EGA)", 3, "loom-ega", "37f56ceb13e401a7ac7d9e6b37fecaf7")]
    [InlineData(GameFolders.FmTowns, "Loom (FM-TOWNS)", 3, "loom-fm", "c5d10e190d4b4d59114b824f2fdbd00e")]
    [InlineData(GameFolders.Vga, "Loom (VGA)", 4, "loom-vga", "5d88b9d6a88e6f8e90cded9d01b7f082")]
    public void A_release_opens_and_names_itself(string folder, string description, int version, string target, string md5)
    {
        if (GameFolders.Find(folder) is not { } path)
        {
            return;
        }

        var game = Loom.Open(path);
        Assert.Equal(description, game.Description);
        Assert.Equal(description, game.ToString());
        Assert.Equal(version, game.Version);
        Assert.Equal(target, game.Target);
        Assert.Equal(md5, game.IndexMd5);
        Assert.Equal("en", game.Language);
    }

    [Fact]
    public void A_folder_without_a_release_is_refused()
    {
        string empty = Directory.CreateTempSubdirectory("distaff-empty").FullName;
        try
        {
            Assert.Throws<FileNotFoundException>(() => Loom.Open(empty));
            Assert.Throws<DirectoryNotFoundException>(() => Loom.Open(Path.Combine(empty, "none")));
        }
        finally
        {
            Directory.Delete(empty);
        }
    }

    /// <summary>
    /// The inspector draws a room in its size, with its mask in black and white, lists its
    /// objects, and disassembles a global script whole and a room's own scripts.
    /// </summary>
    [Fact]
    public void The_inspector_draws_rooms_and_lists_scripts()
    {
        if (GameFolders.Find(GameFolders.Ega) is not { } path)
        {
            return;
        }

        var inspector = Loom.Open(path).Inspect();
        Assert.Equal(76, inspector.Rooms.Count);
        Assert.Equal(115, inspector.Scripts.Count);
        Assert.All(inspector.Rooms.Take(5), n => Assert.NotNull(inspector.Room(n, RoomObjects.None)));
        var room = inspector.Room(1)!;
        Assert.Equal((320, 144), (room.Picture.Width, room.Picture.Height));
        Assert.Equal((room.Width, room.Height), (room.Mask.Width, room.Mask.Height));
        Assert.All(room.Mask.Pixels, p => Assert.True(p == Graphics.Rgb.Black || p == Graphics.Rgb.White));
        Assert.Equal(53, room.Objects.Count);
        Assert.Equal(12, room.LocalScripts.Count);
        Assert.NotEqual(0, room.EntryScript);
        Assert.Null(inspector.Room(250));

        var script = inspector.Script(1)!;
        Assert.Equal("Script 1", script.Title);
        Assert.Equal(0, script.Lines[0].Offset);
        Assert.Equal(script.Size, script.Lines.Sum(l => l.Length) + script.Unreached);
        Assert.StartsWith("[0000] ", script.Lines[0].ToString());

        var scripts = inspector.RoomScripts(1)!;
        Assert.Equal("Exit script, at 18084 in room 1", scripts[0].Title);
        Assert.Contains(scripts, s => s.Title.StartsWith("Local script 211", StringComparison.Ordinal));
    }

    /// <summary>
    /// A game played headless from the front door: past the proficiency screen into the title,
    /// whose music plays, with the opening's scripts running and Bobbin on the cliff.
    /// </summary>
    [Fact]
    public void A_game_plays_headless_and_shows_what_it_is_doing()
    {
        if (GameFolders.Find(GameFolders.Ega) is not { } path)
        {
            return;
        }

        var world = Loom.Open(path).Play();
        world.Boot();
        for (int i = 0; i < 100; i++)
        {
            world.Step();
        }

        world.Click(160, 92);
        for (int i = 0; i < 1500; i++)
        {
            world.Step();
        }

        Assert.Contains(25, world.SoundsStarted);
        Assert.NotEmpty(world.RunningScripts());
        Assert.All(world.RunningScripts(), s => Assert.Equal(Scripts.ScriptKind.Global, s.Kind));
        var bobbin = world.GetActor(1);
        Assert.Equal(world.CurrentRoomNumber, bobbin.Room);
        Assert.Contains(world.ActorsHere(), a => a.Number == 1);

        var samples = new short[2048];
        world.Audio.Read(samples);
        Assert.Contains(samples, s => s != 0);
    }
}
