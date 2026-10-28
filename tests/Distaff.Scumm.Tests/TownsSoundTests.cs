using System.Security.Cryptography;
using Distaff.Scumm.Game;
using Distaff.Scumm.Input;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Sound;

namespace Distaff.Scumm.Tests;

/// <summary>
/// The FM Towns release's sampled sounds and Euphony music, on the port of ScummVM's emulation
/// of its PCM and FM chips. Each script in <c>tools/towns-reference/scripts</c> is played as
/// <c>towns-reference</c> plays it through ScummVM's own <c>Player_Towns_v1</c>, whose output's
/// SHA-256 and reported statuses are pinned here: the port gives the same samples. Tests against
/// the game data pass without checking when it is absent.
/// </summary>
public class TownsSoundTests
{
    private const int Rate = 44100;
    private const int Leaf = 154, CliffToVillage = 153, VillageToTents = 161, TentsToPath = 201, PathToSanctuary = 252, SanctuaryDoor = 254, Egg = 270, Distaff = 271;
    private const int NoteC = 1, NoteD = 2, NoteE = 3, MagicOk = 24;

    /// <summary>
    /// Sound 24, the sample heard when a draft works: one PCM channel, its velocity the sound's
    /// own halved (64), its note the sample's base note, for 4.2 seconds, then silence; the
    /// sound is reported running until its channel's sample has ended.
    /// </summary>
    [Fact]
    public void A_sampled_sound_plays_as_scummvm_plays_it()
    {
        Check("magicok",
            "d86f410d4e72942cf2bc79344ee9a4521eaa3e0428a0dda216dbe43c6e2ba84f",
            "status 24 1 at 1024",
            "status 24 1 at 185344",
            "status 24 0 at 193536");
    }

    /// <summary>
    /// Sampled sounds together, each on a PCM channel of its own: sound 9 at the volume and note
    /// the scripts gave it (<c>setSoundVolume</c>, <c>setSoundNote</c>), an octave up and over
    /// in a tenth of a second; sound 11 looping until it is stopped; sound 68 started later.
    /// </summary>
    [Fact]
    public void Sampled_sounds_play_together_with_the_scripts_volume_and_note()
    {
        Check("effects",
            "c15fffb566c2f6b3624b9ea6bc631274b11402a22b27b40c2ca645f13c61f901",
            "status 9 0 at 8704",
            "status 11 1 at 8704",
            "status 68 1 at 8704",
            "status 9 0 at 39424",
            "status 11 1 at 39424",
            "status 68 0 at 39424",
            "status 11 0 at 39424");
    }

    /// <summary>
    /// The notes of the draft Opening, E C E D, each a Euphony track of one note on the FM chip,
    /// a second apart; each is reported running for its bar, 0.9 seconds. A note started while
    /// another plays does not start, and the sound reported is the new one's, as in ScummVM.
    /// </summary>
    [Fact]
    public void Euphony_notes_play_as_scummvm_plays_them()
    {
        Check("draft",
            "31f78b0307163376986299549f22dddddda6532094b86f8ccaf7685c36bc268c",
            "status 3 1 at 1024",
            "status 3 0 at 45056",
            "status 2 1 at 133120",
            "status 2 1 at 167936",
            "status 2 0 at 177152",
            "status 5 1 at 187392",
            "status 8 0 at 187392",
            "status 8 0 at 228352");
    }

    /// <summary>
    /// Practice level. Bobbin takes the distaff in the sanctuary and spins Opening on the egg,
    /// and the game is heard as a host hears it, its audio read after each step: each note's
    /// Euphony sound is reported running while it plays, on the FM chip, in stereo, and then
    /// sound 24's sample is heard on the PCM chip until it ends.
    /// </summary>
    [Fact]
    public void The_draft_and_its_answer_are_heard()
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
        w.Click(Egg);

        var player = (PlayerTowns)world.Sound.Player;
        long framesRead = world.Sound.Output.FramesRendered;
        var heard = new Dictionary<int, (int Steps, int Peak, int PeakLeft, int PeakRight)>();
        void StepAndListen(int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                world.Step();
                long due = (long)(world.Time.TotalSeconds * Rate);
                var buffer = new short[Math.Max(0, due - framesRead) * 2];
                world.Sound.Output.Read(buffer);
                framesRead = Math.Max(framesRead, due);

                foreach (int sound in new[] { NoteC, NoteD, NoteE, MagicOk })
                {
                    if (world.Sound.IsRunning(sound))
                    {
                        heard.TryGetValue(sound, out var h);
                        int left = Enumerable.Range(0, buffer.Length / 2).Max(f => Math.Abs((int)buffer[2 * f]));
                        int right = Enumerable.Range(0, buffer.Length / 2).Max(f => Math.Abs((int)buffer[2 * f + 1]));
                        heard[sound] = (h.Steps + 1, Math.Max(h.Peak, Math.Max(left, right)), Math.Max(h.PeakLeft, left), Math.Max(h.PeakRight, right));
                    }
                }
            }
        }

        int[] notes = w.Notes(0);
        Assert.Equal([NoteE, NoteC, NoteE, NoteD], notes);
        foreach (int note in notes)
        {
            var verb = world.Verbs.First(v => v.VerbId == note && v.Mode == 1 && v.SaveId == 0);
            world.Click(verb.CurRect.Left + verb.CurRect.Width / 2, verb.CurRect.Top + 4);
            StepAndListen(6);
            Assert.True(world.Sound.IsRunning(note), $"note {note} is not reported running");
            Assert.Equal(note, player.CurrentEuphonySound);
            StepAndListen(20);
            Assert.False(world.Sound.IsRunning(note), $"note {note} is still reported running");
        }

        for (int i = 0; i < 400 && !(heard.ContainsKey(MagicOk) && !world.Sound.IsRunning(MagicOk)); i++)
        {
            StepAndListen(1);
        }

        Assert.Equal([NoteE, NoteC, NoteE, NoteD, MagicOk], world.SoundsStarted.SkipWhile(s => s != NoteE).Where(s => s is NoteC or NoteD or NoteE or MagicOk));
        Assert.All(new[] { NoteC, NoteD, NoteE }, n => Assert.True(heard[n].Steps >= 5 && heard[n].PeakLeft > 500 && heard[n].PeakRight > 500, $"note {n}: {heard[n]}"));
        Assert.True(heard[MagicOk].Steps >= 30 && heard[MagicOk].PeakLeft > 5000 && heard[MagicOk].PeakRight > 5000, $"sound 24: {heard[MagicOk]}");
        Assert.False(world.Sound.IsRunning(MagicOk));
    }

    /// <summary>Plays a reference script through the port, and checks the output's SHA-256 and the statuses it reports.</summary>
    private static void Check(string script, string sha256, params string[] statuses)
    {
        if (TownsResourceTests.Open() is not { } loader)
        {
            return;
        }

        var (output, reported) = Play(loader, File.ReadAllLines(Path.Combine(ScriptsFolder(), script + ".txt")));
        Assert.Equal(statuses, reported);
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(output)));
    }

    /// <summary>
    /// Plays a script of <c>towns-reference</c>'s through <see cref="PlayerTowns"/> at 44100 Hz,
    /// the sounds loaded from the game rather than from the files it names, and gives the 16-bit
    /// stereo samples (little-endian) and the statuses it printed.
    /// </summary>
    private static (byte[] Output, List<string> Statuses) Play(ResourceLoader loader, IEnumerable<string> lines)
    {
        var player = new PlayerTowns(Rate, new CdPlayer(null, Rate), (_, _, _, _) => { }, () => { });
        var sounds = new Dictionary<int, byte[]>();
        var output = new MemoryStream();
        var statuses = new List<string>();
        long total = 0;
        foreach (var line in lines)
        {
            var t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 0 || t[0].StartsWith('#'))
            {
                continue;
            }

            int a = t.Length > 1 ? int.Parse(t[1]) : 0;
            switch (t[0])
            {
                case "load":
                    sounds[a] = loader.Load(ResourceType.Sound, a)!.Value.ToArray();
                    break;
                case "start":
                    player.StartSound(a, sounds[a]);
                    break;
                case "stop":
                    player.StopSound(a);
                    break;
                case "stopall":
                    player.StopAllSounds();
                    break;
                case "vol":
                    player.SetSoundVolume(a, int.Parse(t[2]), int.Parse(t[3]));
                    break;
                case "note":
                    player.SetSoundNote(a, int.Parse(t[2]));
                    break;
                case "status":
                    statuses.Add($"status {a} {(player.IsPlaying(a) ? 1 : 0)} at {total}");
                    break;
                case "g":
                    int chunk = t.Length > 2 ? int.Parse(t[2]) : a;
                    for (int left = a; left > 0;)
                    {
                        int n = Math.Min(left, chunk);
                        var buffer = new short[n * 2];
                        player.Read(buffer);
                        foreach (short s in buffer)
                        {
                            output.WriteByte((byte)s);
                            output.WriteByte((byte)(s >> 8));
                        }

                        left -= n;
                        total += n;
                    }

                    break;
            }
        }

        return (output.ToArray(), statuses);
    }

    /// <summary>The reference scripts, in <c>tools/towns-reference/scripts</c> above the test binary.</summary>
    private static string ScriptsFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "tools", "towns-reference", "scripts");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("tools/towns-reference/scripts was not found above the test binary.");
    }
}
