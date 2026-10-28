using System.Security.Cryptography;
using Distaff.Audio;
using Distaff.Scumm.Costumes;
using Distaff.Scumm.Game;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Input;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Saves;
using Distaff.Scumm.Scripts;
using Distaff.Scumm.Sound;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>The FM Towns release's costumes, scripts and interface, played from the opening. Tests against the game data pass without checking when it is absent.</summary>
public class TownsGameLoopTests
{
    private static readonly byte[] OwnColors = [.. Enumerable.Repeat((byte)255, 32)];

    /// <summary>
    /// Every costume parses as the VGA CD's are laid out, with 16 colours, and every frame of every
    /// animation draws; commands 0x79 to 0x7B draw nothing (<c>GF_OLD256</c>).
    /// </summary>
    [Fact]
    public void Every_costume_frame_draws()
    {
        if (TownsResourceTests.Open() is not { } loader)
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

            var costume = Costume.ParseTowns(number, data);
            Assert.Equal((3, true, true, 16), (costume.Version, costume.SmallHeader, costume.Old256, costume.ColorCount));
            costumes++;
            for (int frame = 0; frame * 4 < costume.AnimationCount; frame++)
            {
                for (int direction = 0; direction < 4; direction++)
                {
                    var state = new CostumeState();
                    state.Decode(costume, frame, direction);
                    var target = new MaskedImage(new Surface8(320, 240), new Surface8(320, 240));
                    CostumeRenderer.Draw(costume, state, direction, CostumeRenderer.Palette(costume, OwnColors), target, 160, 150, zPlane: 0);
                    frames++;
                }
            }
        }

        Assert.Equal((162, 5564), (costumes, frames));
    }

    /// <summary>
    /// The boot script takes the proficiency level ScummVM's dialog sets as the boot parameter
    /// (Practice 0, Standard 1, Expert 2) into variable 123.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void The_boot_parameter_is_the_proficiency_level(int level)
    {
        if (TownsResourceTests.Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        world.Boot(level);
        world.Step();
        Assert.Equal(level, world.Engine.Variables[123]);
        Assert.Equal(42, world.Engine.Variables[Vars.VideoMode]);
    }

    /// <summary>
    /// The copy protection is skipped as ScummVM skips it for the FM Towns: bit 15 of variable 214
    /// reads 0, so the game welcomes the player, and with the protection kept it reads as it is.
    /// </summary>
    [Fact]
    public void The_copy_protection_is_skipped()
    {
        if (TownsResourceTests.Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        world.Boot();
        for (int i = 0; i < 10; i++)
        {
            world.Step();
        }

        Assert.Equal("Welcome to the Age of the Great Guilds.", world.MessageText);
        const int Bit = Operand.BitVariable | (214 << 4) | 15;
        world.Engine.Variables[214] |= 0x8000;
        Assert.Equal(0, world.Engine.ReadVar(Bit));
        world.CopyProtection = true;
        Assert.Equal(1, world.Engine.ReadVar(Bit));
    }

    /// <summary>
    /// Practice level, the opening skipped. Bobbin follows the leaf to the sanctuary, takes the
    /// distaff, whose staff of notes the verb band shows in the text layer, and weaves Opening on
    /// the egg.
    /// </summary>
    [Fact]
    public void The_elders()
    {
        if (TownsResourceTests.Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        var w = new Walkthrough(world);
        world.Boot();
        w.Wait(100).Key(Key.Escape, (char)27).Ready();
        w.Use(Leaf);
        w.Use(CliffToVillage).InRoom(3).Ready();
        w.Use(VillageToTents).InRoom(5).Ready();
        w.Use(TentsToPath).InRoom(7).Ready();
        w.Use(PathToSanctuary).InRoom(8).Ready();
        w.Use(SanctuaryDoor).InRoom(9).Ready(20000);
        w.Use(Distaff);
        Assert.Equal(TheDistaff, Hash(world.Frame(cursor: false)));
        w.Click(Egg).Spin(0).Ready(20000);

        Assert.Contains("Last leaf of the year.", world.MessageLog);
        Assert.Contains("A swan's egg. Curious destiny.", world.MessageLog);
        Assert.Contains("I'd hoped the swan might come to save you.", world.MessageLog);
        Assert.Equal(9, world.CurrentRoomNumber);
        Assert.Equal(3, world.Engine.Variables[Known]);
        Assert.NotEqual(0, world.Engine.Variables[50] & 0x2000);
    }

    /// <summary>F5 reaches the game as the FM Towns' special key 0x8005, and its scripts show the save screen, room 70.</summary>
    [Fact]
    public void F5_shows_the_games_own_save_screen()
    {
        if (TownsResourceTests.Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        var w = new Walkthrough(world);
        world.Boot();
        w.Wait(100).Key(Key.Escape, (char)27).Ready();
        w.Key(Key.F5, '\0').Wait(50);
        Assert.Equal(70, world.CurrentRoomNumber);
        Assert.Contains(world.MessageLog, m => m.StartsWith("Insert save-load disk in Drive A."));
    }

    /// <summary>
    /// The FM Towns' own save screen: in the sanctuary, Opening learned, F5, SAVE, the first slot,
    /// a name typed, Enter and OK save under that name. In another game, on the cliff, F5, LOAD
    /// (which asks whether slot 0 has a save, as the FM Towns' screen does, and goes on when any
    /// slot has), the slot and OK load it, and the game goes on in the sanctuary with Opening.
    /// Loaded as ScummVM's launcher loads it, the same save ends the save screen and goes on there
    /// too.
    /// </summary>
    [Fact]
    public void The_save_screen_saves_and_loads()
    {
        if (TownsResourceTests.Open() is not { } loader)
        {
            return;
        }

        var store = new MemorySaveStore();
        var world = new GameWorld(loader) { SaveStore = store };
        var w = new Walkthrough(world);
        world.Boot();
        w.Wait(100).Key(Key.Escape, (char)27).Ready();
        w.Use(Leaf);
        w.Use(CliffToVillage).InRoom(3).Ready();
        w.Use(VillageToTents).InRoom(5).Ready();
        w.Use(TentsToPath).InRoom(7).Ready();
        w.Use(PathToSanctuary).InRoom(8).Ready();
        w.Use(SanctuaryDoor).InRoom(9).Ready(20000);
        w.Use(Distaff);
        w.Click(Egg).Spin(0).Ready(20000);

        w.Key(Key.F5, '\0').Wait(50);
        w.ClickAt(SaveButton.X, SaveButton.Y).Wait(40).ClickAt(FirstSlot.X, FirstSlot.Y).Wait(30);
        foreach (char c in "egg")
        {
            w.Key((Key)c, c).Wait(5);
        }

        w.Key(Key.Return, (char)13).Wait(60).ClickAt(OkButton.X, OkButton.Y).Wait(80);
        Assert.Contains("The game was saved.", world.MessageLog);
        var file = ReadAll(store, "loom-fm.s01");
        Assert.Equal("egg", SaveFile.ReadInfo(file).Name);

        var other = new GameWorld(loader) { SaveStore = store };
        var o = new Walkthrough(other);
        other.Boot();
        o.Wait(100).Key(Key.Escape, (char)27).Ready();
        o.Key(Key.F5, '\0').Wait(50);
        o.ClickAt(LoadButton.X, LoadButton.Y).Wait(40);
        Assert.Contains(other.MessageLog, m => m.StartsWith("Select game to load, then select"));
        o.ClickAt(FirstSlot.X, FirstSlot.Y).Wait(30).ClickAt(OkButton.X, OkButton.Y).InRoom(9).Ready();
        InTheSanctuaryWithOpening(other);

        var launched = new GameWorld(loader) { SaveStore = store };
        launched.Load(file);
        Assert.Equal(GameWorld.SaveRoom, launched.CurrentRoomNumber);
        new Walkthrough(launched).InRoom(9).Ready();
        InTheSanctuaryWithOpening(launched);
    }

    private static void InTheSanctuaryWithOpening(GameWorld world)
    {
        Assert.Equal(9, world.CurrentRoomNumber);
        Assert.Equal((1, 1), (world.UserPut, world.CursorState));
        Assert.Equal(3, world.Engine.Variables[Known]);
        Assert.NotEqual(0, world.Engine.Variables[50] & 0x2000);
    }

    private static readonly (int X, int Y) SaveButton = (232, 47), LoadButton = (284, 47), FirstSlot = (100, 21), OkButton = (259, 87);

    private static byte[] ReadAll(ISaveStore store, string name)
    {
        using var input = store.OpenRead(name) ?? throw new InvalidOperationException($"{name} was not saved.");
        using var copy = new MemoryStream();
        input.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>
    /// The boot starts sound 29, which plays the overture from the CD: track 2 from its start for
    /// 9259 CD frames, once, at the sound's volume of 192 on each side, which the Towns' audio
    /// makes the CD channel's 169 of 255. What is heard is the track's samples at that volume.
    /// </summary>
    [Fact]
    public void The_overture_plays_from_the_cd()
    {
        if (TownsResourceTests.Open() is not { } loader)
        {
            return;
        }

        string folder = GameFolders.Find(GameFolders.FmTowns)!;
        var world = new GameWorld(loader, cdTracks: CdTrackFiles.In(folder));
        world.Boot();
        world.Step();
        var player = (PlayerTowns)world.Sound.Player;
        Assert.Equal((2, 0, 9259), world.CdTrack);
        Assert.Equal((29, (192, 192), 192), (player.CurrentCdSound, player.CdVolume, player.CurrentCdVolume));
        Assert.True(world.Sound.Cd.IsPlaying && world.Sound.IsRunning(29));

        var heard = new short[4410 * 2];
        world.Sound.Cd.Read(heard);
        using var track = new FlacTrack(File.OpenRead(Path.Combine(folder, "Track2.flac")));
        var decoded = new float[4410 * 2];
        track.Read(decoded);
        Assert.Equal(decoded.Select(f => (short)((int)MathF.Floor(f * 32767f + 0.5f) * 169 / 256)), heard);

        world.Sound.Stop(29);
        Assert.False(world.Sound.Cd.IsPlaying);
        Assert.Null(world.CdTrack);
        Assert.Equal(0, player.CurrentCdSound);
    }

    private const int Known = 122;
    private const int CliffToVillage = 153, Leaf = 154, VillageToTents = 161, TentsToPath = 201, PathToSanctuary = 252, SanctuaryDoor = 254, Egg = 270, Distaff = 271;
    private const string TheDistaff = "165020946ec95022";

    private static string Hash(RgbFrame frame)
        => Convert.ToHexStringLower(SHA256.HashData(frame.Pixels.SelectMany(p => new[] { p.R, p.G, p.B }).ToArray()))[..16];
}
