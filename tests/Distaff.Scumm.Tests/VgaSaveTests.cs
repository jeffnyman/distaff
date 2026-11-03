using System.Security.Cryptography;
using Distaff.Audio;
using Distaff.Scumm.Game;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Saves;
using Distaff.Scumm.Scripts;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>Saves of the VGA CD release in ScummVM's format. Tests against the game data pass without checking when it is absent.</summary>
public class VgaSaveTests
{
    private static readonly DateTime When = new(2026, 10, 27, 12, 0, 0);

    /// <summary>
    /// A save read back into a new world saves again to the same state, field for field, the mouse
    /// put back: on the cliff after the opening, and in the sanctuary with the distaff. The part
    /// the CD last played is left out: it has stopped, and a stopped part is not played again, so
    /// the new world's CD has played nothing, as a new ScummVM's has not.
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

        var world = new GameWorld(loader);
        var w = Skip(world);
        if (withDistaff)
        {
            ToTheDistaff(w);
        }

        var file = world.Save("test", When);
        var again = new GameWorld(loader);
        again.Load(file);
        again.MoveMouse(world.MouseX, world.MouseY);

        var expected = WithoutCdPart(file, loader);
        var actual = WithoutCdPart(again.Save("test", When), loader);
        int at = Enumerable.Range(0, Math.Min(expected.Length, actual.Length)).FirstOrDefault(i => expected[i] != actual[i], -1);
        Assert.True(at < 0 && expected.Length == actual.Length, $"the states differ at byte {at} of {expected.Length} ({actual.Length} again)");
    }

    /// <summary>
    /// Version 4's actors are ScummVM's <c>Actor</c>, not <c>Actor_v3</c>: each saves without the
    /// last four words version 3's do.
    /// </summary>
    [Fact]
    public void Version_4_actors_save_without_version_3s_fields()
    {
        var v3 = SaveSerializer.ForSaving(SaveFile.CompatibleVersion);
        var v4 = SaveSerializer.ForSaving(SaveFile.CompatibleVersion);
        new ScummState.ActorState().Sync(v3, 3);
        new ScummState.ActorState().Sync(v4, 4);
        Assert.Equal(8, v3.Length - v4.Length);
    }

    /// <summary>
    /// A save holds what version 4 keeps apart from version 3: the room's 256 colours as the game
    /// set them, the bit variables, the charset's colours, and the room's boxes at 20 bytes each.
    /// </summary>
    [Fact]
    public void A_save_holds_the_vga_state()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        ToTheDistaff(Skip(world));
        var (_, state) = SaveFile.Read(world.Save("test", When), loader.Index.ObjectOwners.Count, 4);

        var palette = world.CurrentPalette;
        Assert.Equal(palette.SelectMany(c => new[] { c.R, c.G, c.B }), state.CurrentPalette);
        Assert.Contains(state.BitVariables, b => b != 0);
        Assert.Equal(world.Engine.BitVariables, state.BitVariables);
        Assert.Equal(world.CharsetColorMap, state.CharsetColorMap);
        Assert.Equal(world.CurrentRoom!.WalkBoxes.Boxes.Count * 20 + 1, state.Resources[(ScummState.TypeMatrix, 2)].Data.Length);
    }

    /// <summary>
    /// A save in the sanctuary with the distaff, loaded into a new world, plays on as the game did:
    /// the next step shows the same screen, and the opening draft spun on the egg says the same and
    /// leaves the same screen. The loaded game also draws the notes' letters again first.
    /// </summary>
    [Fact]
    public void A_loaded_save_plays_on_as_the_game_did()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        ToTheDistaff(Skip(world));
        var file = world.Save("sanctuary", When);
        var loaded = new GameWorld(loader);
        loaded.Load(file);
        loaded.MoveMouse(world.MouseX, world.MouseY);

        world.Step();
        loaded.Step();
        Assert.Equal(Hash(world), Hash(loaded));

        int before = world.MessageLog.Count;
        foreach (var w in new[] { world, loaded })
        {
            new Walkthrough(w).Click(Egg).Spin(0).Ready(20000).Wait(100);
        }

        Assert.Equal(Said(world.MessageLog.Skip(before)), Said(loaded.MessageLog));
        Assert.Contains("Goodbye, Bobbin!  I must follow the swans.", loaded.MessageLog);
        Assert.Equal(Hash(world), Hash(loaded));
    }

    /// <summary>
    /// A save made while the proficiency screen speaks holds the CD's part and that it plays; loaded,
    /// the part plays on from 1.25 frames for each count of the music timer the save holds, that
    /// many counts shorter (<c>restoreCDAudioAfterLoad</c>).
    /// </summary>
    [Fact]
    public void A_save_made_while_the_cd_plays_plays_it_on()
    {
        if (GameFolders.Find(GameFolders.Vga) is not { } folder)
        {
            return;
        }

        var loader = ResourceLoader.Open(new DirectoryGameContainer(folder));
        var world = new GameWorld(loader, cdTracks: CdTrackFiles.In(folder));
        world.Boot();
        new Walkthrough(world).Until(() => world.Sound.Cd.IsPlaying && world.Engine.Variables[Vars.MusicTimer] > 30);
        Assert.True(world.Sound.Cd.IsPlaying);
        int timer = world.Engine.Variables[Vars.MusicTimer];
        Assert.True(timer > 0);

        var file = world.Save("speaking", When);
        var (_, state) = SaveFile.Read(file, loader.Index.ObjectOwners.Count, 4);
        Assert.Equal([1, 1, 22, 185, 1], state.AudioCdStatus);

        var loaded = new GameWorld(loader, cdTracks: CdTrackFiles.In(folder));
        loaded.Load(file);
        Assert.True(loaded.Sound.Cd.IsPlaying);
        Assert.Equal((1, 22 + (int)(timer * 1.25), 185 - timer), loaded.CdTrack);
        world.Sound.Dispose();
        loaded.Sound.Dispose();
    }

    private const int Egg = 195;

    // the objects on the way to the distaff (see VgaGameLoopTests)
    private const int CliffToVillage = 78, Leaf = 79, VillageToTents = 86, TentsToPath = 126, PathToSanctuary = 177;
    private const int SanctuaryDoor = 179, Distaff = 196;

    /// <summary>The proficiency screen, PRACTICE, and the opening skipped with Escape and its message about the F9 key answered, to Bobbin on the cliff in control.</summary>
    /// <summary>
    /// The sound state of a VGA CD save is ScummVM's iMuse's (the release plays its MIDI device),
    /// which Distaff does not have: whatever the device, ScummVM's saves load and play on, the
    /// state passed over (with the AdLib it had been read as the AdLib player's, whose music, sound
    /// 0, the VGA CD cannot give), and Distaff's own saves carry none, which ScummVM skips.
    /// </summary>
    [Fact]
    public void The_sound_state_is_scummvms_imuses_and_passed_over()
    {
        if (Open() is not { } loader || GameFolders.Find("scummvm-saves") is not { } saves)
        {
            return;
        }

        foreach (string file in Directory.GetFiles(saves, "loom-vga-*.s00"))
        {
            var world = new GameWorld(loader, sound: Sound.SoundDevice.AdLib);
            world.Load(File.ReadAllBytes(file));
            for (int i = 0; i < 40; i++)
            {
                world.Step();
            }

            var (_, state) = SaveFile.Read(world.Save("again", When), loader.Index.ObjectOwners.Count, 4);
            Assert.Empty(state.SoundEngineData);
        }
    }

    private static Walkthrough Skip(GameWorld world)
    {
        world.Boot();
        return new Walkthrough(world).Wait(300).ClickAt(160, 95).Wait(100).Key(Input.Key.Escape, (char)27).Answer(Walkthrough.VgaTextKeyMessage, 8000).Ready(8000);
    }

    /// <summary>From the cliff to the sanctuary, the distaff taken.</summary>
    private static Walkthrough ToTheDistaff(Walkthrough w)
    {
        w.Use(Leaf);
        w.Use(CliffToVillage).InRoom(3).Ready();
        w.Use(VillageToTents).InRoom(5).Ready();
        w.Use(TentsToPath).InRoom(7).Ready();
        w.Use(PathToSanctuary).InRoom(8).Ready();
        w.Use(SanctuaryDoor).InRoom(9).Ready(20000);
        return w.Use(Distaff);
    }

    /// <summary>The lines with words in them: what is said, not the notes' letters.</summary>
    private static string[] Said(IEnumerable<string> log) => [.. log.Where(m => m.Count(char.IsLetter) > 3)];

    private static string Hash(GameWorld world)
    {
        var palette = world.DisplayPalette();
        var rgb = world.Screen().Pixels.SelectMany(p => new[] { palette[p].R, palette[p].G, palette[p].B }).ToArray();
        return Convert.ToHexStringLower(SHA256.HashData(rgb))[..16];
    }

    /// <summary>A save's state written again with the CD's part, all but whether it plays, cleared.</summary>
    private static byte[] WithoutCdPart(byte[] file, ResourceLoader loader)
    {
        var (info, state) = SaveFile.Read(file, loader.Index.ObjectOwners.Count, 4);
        Assert.Equal(0, state.AudioCdStatus[0]);
        state.AudioCdStatus.AsSpan(1).Clear();
        var s = SaveSerializer.ForSaving(info.Version);
        state.Sync(s);
        return s.ToArray();
    }

    private static ResourceLoader? Open()
        => GameFolders.Find(GameFolders.Vga) is { } path ? ResourceLoader.Open(new DirectoryGameContainer(path)) : null;
}
