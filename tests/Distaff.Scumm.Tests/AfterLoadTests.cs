using Distaff.Audio;
using Distaff.Scumm.Game;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Scripts;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>
/// The first frames after a load, against ScummVM's. ScummVM loaded a walkthrough save from its
/// launcher (<c>-x1</c>) with its random numbers seeded as Distaff's are (<c>--random-seed=1</c>),
/// played on by itself and saved about 6 seconds on (its autosave). Distaff loads the same save,
/// in the same way, and plays on to the same moment. ScummVM saves partway through a frame
/// (<c>scummLoop_handleSaveLoad</c>), with the frame's timers counted and its delays counted down
/// and its scripts not yet run, so Distaff's state after the frame before is set forward by that
/// much. Then every variable, every script slot (its script, place, delay and state), every
/// object's state and every actor are the same. The saves are kept beside the game data, in
/// <c>games/scummvm-saves</c>, and the tests pass without checking when they are absent.
/// </summary>
public class AfterLoadTests
{
    [Theory]
    [InlineData(GameFolders.Ega, "01-TheElders")]
    [InlineData(GameFolders.Ega, "09-TheVoid")]
    [InlineData(GameFolders.Vga, "01-TheElders")]
    [InlineData(GameFolders.Vga, "09-TheVoid")]
    [InlineData(GameFolders.FmTowns, "03-LeavingTheIsland")]
    [InlineData(GameFolders.FmTowns, "09-TheVoid")]
    public void The_first_frames_after_a_load_run_as_scummvms(string release, string name)
    {
        if (GameFolders.Find(release) is not { } path || GameFolders.Find("scummvm-saves") is not { } saves
            || !File.Exists(Path.Combine(saves, $"{release}-{name}.seed1.s00")))
        {
            return;
        }

        GameWorld Load(string file)
        {
            var world = new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path)), cdTracks: CdTrackFiles.In(path));
            world.Load(File.ReadAllBytes(Path.Combine(saves, file)));
            return world;
        }

        var theirs = Load($"{release}-{name}.seed1.s00");
        var ours = Load($"{release}-{name}.before.sav");
        var v = theirs.Engine.Variables;

        // the mouse where ScummVM's was
        ours.MoveMouse(v[Vars.MouseX], v[Vars.MouseY]);

        // to the frame before ScummVM's save; the music timer, set before the save as before
        // Distaff's scripts, is the next frame's
        int frames = 0;
        while (ours.Engine.Variables[Vars.TimerTotal] + ours.StepTicks < v[Vars.TimerTotal])
        {
            StepFrame(ours);
            frames++;
        }

        int delta = ours.StepTicks;
        var vars = (int[])ours.Engine.Variables.Clone();
        var slots = ours.Engine.Slots.Select(s => Slot(s, Math.Min(delta, 15))).ToList();
        var states = Enumerable.Range(1, 999).Select(ours.GetState).ToList();
        var actors = ours.Actors.Skip(1).Select(Actor).ToList();
        StepFrame(ours);

        Assert.InRange(frames, 50, 56);
        foreach (int t in new[] { Vars.TimerTotal, Vars.Timer1, Vars.Timer2, Vars.Timer3 })
        {
            vars[t] += delta;
        }

        vars[Vars.Timer] = delta;
        vars[Vars.MusicTimer] = ours.Engine.Variables[Vars.MusicTimer];

        // ScummVM's CD timer runs on a thread of its own, at 240 calls a second: a call either side
        if (release == GameFolders.Vga)
        {
            Assert.InRange(vars[Vars.MusicTimer] - v[Vars.MusicTimer], -1, 1);
            vars[Vars.MusicTimer] = v[Vars.MusicTimer];
        }

        Assert.Equal(v, vars);
        Assert.Equal(theirs.Engine.Slots.Select(s => Slot(s, 0)), slots);
        Assert.Equal(Enumerable.Range(1, 999).Select(theirs.GetState), states);
        Assert.Equal(theirs.Actors.Skip(1).Select(Actor), actors);
    }

    /// <summary>ScummVM's random numbers (<c>Common::RandomSource</c>), as worked out from its code.</summary>
    [Fact]
    public void Random_numbers_are_scummvms()
    {
        var one = new ScummRandom(1);
        Assert.Equal([3, 83, 131, 3, 0, 995, 27309, 5], new[] { 9, 99, 255, 7, 1, 999, 65535, 15 }.Select(one.Next));

        // seed 0 is taken as 1
        var zero = new ScummRandom(0);
        Assert.Equal([3, 3, 131, 107], Enumerable.Range(0, 4).Select(_ => zero.Next(255)));

        var other = new ScummRandom(123456789);
        Assert.Equal([31, 196, 40, 214], Enumerable.Range(0, 4).Select(_ => other.Next(255)));
    }

    /// <summary>
    /// After a load from the launcher, the VGA CD's music timer counts again (as <c>loadState</c>
    /// starts the CD timer again when the loaded music timer is above 0), from 0, at 240 calls a
    /// second, one count for every four calls.
    /// </summary>
    [Fact]
    public void The_cd_timer_counts_after_a_load_at_240_calls_a_second()
    {
        if (GameFolders.Find(GameFolders.Vga) is not { } path || GameFolders.Find("scummvm-saves") is not { } saves
            || !File.Exists(Path.Combine(saves, "loom-vga-01-TheElders.before.sav")))
        {
            return;
        }

        var world = new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path)));
        world.Load(File.ReadAllBytes(Path.Combine(saves, "loom-vga-01-TheElders.before.sav")));
        Assert.True(world.Engine.Variables[Vars.MusicTimer] > 0);
        var start = world.Time;
        for (int i = 0; i < 100; i++)
        {
            StepFrame(world);
        }

        double seconds = (world.Time - start).TotalSeconds;
        Assert.InRange(world.Engine.Variables[Vars.MusicTimer], (int)(seconds * 60) - 1, (int)(seconds * 60) + 1);
    }

    private static void StepFrame(GameWorld world)
    {
        int frames = world.Frames;
        while (world.Frames == frames)
        {
            world.Step();
        }
    }

    /// <summary>A slot as the save keeps it, its delay counted down by a frame's ticks as <c>decreaseScriptDelay</c> does.</summary>
    private static string Slot(ScriptSlot s, int ticks)
    {
        if (s.IsDead)
        {
            return "-";
        }

        int delay = s.Delay, status = s.Status;
        if (status == SlotStatus.Paused && ticks > 0)
        {
            delay -= ticks;
            if (delay < 0)
            {
                (delay, status) = (0, SlotStatus.Running);
            }
        }

        return $"{s.Number}@{s.Offset} delay {delay} status {status}";
    }

    private static string Actor(Actor a) => $"room {a.Room} at ({a.X}, {a.Y}) costume {a.Costume} frame {a.Frame} moving {a.Moving} box {a.WalkBox}";
}
