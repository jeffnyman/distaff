using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Distaff.Scumm.Game;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Scripts;
using Distaff.Scumm.Sound;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>Sound on the PC speaker, the PCjr and the AdLib. Tests against the game data pass without checking when it is absent.</summary>
public class SoundTests
{
    /// <summary>The scripts read which card plays (<c>setSoundCardVarToCurrentConfig</c>): 0, 1 and 3.</summary>
    [Theory]
    [InlineData(SoundDevice.Speaker, 0)]
    [InlineData(SoundDevice.Pcjr, 1)]
    [InlineData(SoundDevice.AdLib, 3)]
    public void The_sound_card_variable_names_the_device(SoundDevice device, int value)
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader, sound: device);
        Assert.Equal(value, world.Engine.Variables[Vars.SoundCard]);
    }

    /// <summary>
    /// The proficiency screen is silent; with PRACTICE chosen, the title's music (sound 25) starts
    /// and plays on through the opening, and the scripts see it running. Its first seconds are
    /// pinned for each device.
    /// </summary>
    [Theory]
    [InlineData(SoundDevice.Speaker, "12fdde4a7ecf765b")]
    [InlineData(SoundDevice.Pcjr, "b6d0b1d66966526b")]
    [InlineData(SoundDevice.AdLib, "886e7bc1dba85688")]
    public void The_title_music_plays_from_the_title_on(SoundDevice device, string firstSeconds)
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader, sound: device);
        var audio = new List<short>();
        world.Boot();
        Run(world, 100, audio);
        Assert.All(audio, s => Assert.Equal(0, s));

        world.Click(160, 92);
        audio.Clear();
        Run(world, 300, audio);

        Assert.Contains(25, world.SoundsStarted);
        Assert.True(world.Sound.IsRunning(25));
        Assert.Contains(audio, s => s != 0);
        Assert.Equal(firstSeconds, Hash(audio));
    }

    /// <summary>
    /// A save keeps the AdLib's music and where it is (<c>Player_AD::saveLoadWithSerializer</c>),
    /// and a load starts it again there: the title's music sounds from the load's first moment, as
    /// loud as played on from the save, its notes coming and going with it (the chip's notes are
    /// struck again and its timer begins afresh, so not to the sample). ScummVM, loading the same save, plays the same samples as
    /// Distaff; loading a save of its own made at the same place it varies by a sample from one
    /// run to the next, which Distaff does not.
    /// </summary>
    [Fact]
    public void The_adlib_music_plays_on_from_a_save()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader, sound: SoundDevice.AdLib);
        world.Boot();
        Run(world, 60, []);
        world.Click(160, 92);
        Run(world, 120, []);
        var save = world.Save("mid", new DateTime(2026, 10, 28));
        var played = new List<short>();
        Run(world, 50, played);

        var (_, state) = Saves.SaveFile.Read(save, loader.Index.ObjectOwners.Count, 3);
        Assert.Equal(PlayerAd.SaveStateSize, state.SoundEngineData.Length);
        Assert.Equal(25, BitConverter.ToInt32(state.SoundEngineData, 0));

        var loaded = new GameWorld(loader, sound: SoundDevice.AdLib);
        loaded.Load(save);
        var resumed = new List<short>();
        Run(loaded, 50, resumed);

        static double Rms(IEnumerable<short> s) => Math.Sqrt(s.Average(x => (double)x * x));
        Assert.True(Rms(resumed.Take(4410)) > 100, "the music sounds at once");
        Assert.InRange(Rms(resumed) / Rms(played), 0.9, 1.1);
        // the notes as they come and go: the loudness of every 50 milliseconds, which the chip's
        // phase, begun afresh at the load, leaves alone
        int windows = Math.Min(played.Count, resumed.Count) / 2205;
        double[] Envelope(List<short> x) => [.. Enumerable.Range(0, windows).Select(w => Rms(x.Skip(w * 2205).Take(2205)))];
        double[] p = Envelope(played), r = Envelope(resumed);
        double pm = p.Average(), rm = r.Average();
        double c = p.Zip(r, (x, y) => (x - pm) * (y - rm)).Sum() / Math.Sqrt(p.Sum(x => (x - pm) * (x - pm)) * r.Sum(y => (y - rm) * (y - rm)));
        Assert.True(c > 0.8, $"the notes' loudness agrees to {c:F2}");
    }

    /// <summary>
    /// The music plays at ScummVM's music volume, 192 of 256 unless set (<c>music_volume</c>),
    /// which each player takes as ScummVM's does: the speaker's and the PCjr's volume table is
    /// built from it, so their music is 192/255 of its loudness at full volume; the AdLib's
    /// operators are turned down by it. At 0 the music is silent (effects have a volume of their own). Recorded from the same save,
    /// ScummVM's music at its default volume and Distaff's agree in loudness on every device.
    /// </summary>
    [Theory]
    [InlineData(SoundDevice.Speaker)]
    [InlineData(SoundDevice.Pcjr)]
    [InlineData(SoundDevice.AdLib)]
    public void The_music_plays_at_the_music_volume(SoundDevice device)
    {
        if (Open() is not { } loader)
        {
            return;
        }

        double Loudness(int volume)
        {
            var world = new GameWorld(loader, sound: device);
            Assert.Equal(192, world.Sound.MusicVolume);
            world.Sound.MusicVolume = volume;

            // the AdLib plays the click on the proficiency screen (sound 9) as an effect, at the effects' volume
            world.Sound.SfxVolume = 0;
            var audio = new List<short>();
            world.Boot();
            Run(world, 100, []);
            world.Click(160, 92);
            Run(world, 200, audio);
            return Math.Sqrt(audio.Average(x => (double)x * x));
        }

        double full = Loudness(255), normal = Loudness(192);
        Assert.Equal(0, Loudness(0));
        Assert.True(normal < full * 0.8, $"{normal} at 192, {full} at 255");
        if (device != SoundDevice.AdLib)
        {
            Assert.Equal(192.0 / 255, normal / full, 2);
        }
    }

    /// <summary>
    /// The title's music (sound 25) played alone for 25 seconds, read 1024 frames at a time, is
    /// sample for sample what ScummVM's own <c>Player_V2</c> renders (<c>tools/pcjr-reference</c>
    /// builds it to play the same sound; the hashes are the SHA-256 of its output). In the game the
    /// music starts wherever the game's call lands among the player's 236 ticks a second, which in
    /// ScummVM is where its mixer happens to be: recordings of the two from the same save agree to
    /// the sample for some seconds and then by note and level only, and each is this rendering
    /// started at some point between ticks.
    /// </summary>
    [Theory]
    [InlineData(true, "674b284b7fb1391895e457d85531bf2300ddd258b1df79b5b49d1db1cdff9fd2")]
    [InlineData(false, "6aea8a24ae8044138c047eaccf1f0ecede348bec8c6d91f0e6ec32fbf7160f0d")]
    public void The_title_music_renders_as_scummvms_player_v2(bool pcjr, string referenceHash)
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var player = new PlayerV2(GameWorld.DefaultSampleRate, pcjr);
        player.StartSound(25, loader.LoadSound(25)!.Speaker.ToArray());
        var samples = new short[25 * 44100 * 2];
        for (int o = 0; o < samples.Length; o += 2048)
        {
            player.Read(samples.AsSpan(o, Math.Min(2048, samples.Length - o)));
        }

        Assert.Equal(referenceHash, Convert.ToHexStringLower(SHA256.HashData(MemoryMarshal.AsBytes(samples.AsSpan()))));
    }

    /// <summary>
    /// A load sets the game's time to the save's play time, and the sound goes on from there: the
    /// steps after it render what they play, not the time jumped over. Rendering that (as the self
    /// clock once did, taking the jump for sound no host had read) ran the FM Towns' CD music on by
    /// the save's whole play time, so that it came back partway into its track where ScummVM starts
    /// it again from the beginning, and used up the sounds the load started.
    /// </summary>
    [Fact]
    public void A_load_renders_no_sound_for_the_time_it_jumps()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        world.Boot();
        Run(world, 100, []);
        world.Click(160, 92);
        Run(world, 200, []);
        var save = world.Save("later", new DateTime(2026, 10, 28));

        var loaded = new GameWorld(loader);
        loaded.Boot();
        loaded.Step();
        long before = loaded.Sound.Output.FramesRendered;
        loaded.Load(save);
        Assert.True(loaded.Time > TimeSpan.FromSeconds(20));
        var from = loaded.Time;
        for (int i = 0; i < 20; i++)
        {
            loaded.Step();
        }

        // the self clock keeps the sound with the game when nothing reads it, from the load on
        long played = (long)((loaded.Time - from).TotalSeconds * GameWorld.DefaultSampleRate);
        Assert.InRange(loaded.Sound.Output.FramesRendered - before, played - 44100 / 4, played);
    }

    private static string Hash(List<short> samples)
        => Convert.ToHexStringLower(SHA256.HashData(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(samples))))[..16];

    /// <summary>Steps the world, reading its sound as a host does, a step's worth after each step.</summary>
    private static void Run(GameWorld world, int steps, List<short> audio)
    {
        long read = (long)(world.Time.TotalSeconds * GameWorld.DefaultSampleRate);
        for (int i = 0; i < steps; i++)
        {
            world.StepFrame();
            long due = (long)(world.Time.TotalSeconds * GameWorld.DefaultSampleRate);
            var buffer = new short[(due - read) * 2];
            world.Sound.Output.Read(buffer);
            for (int j = 0; j < buffer.Length; j += 2)
            {
                audio.Add(buffer[j]);
            }

            read = due;
        }
    }

    private static ResourceLoader? Open()
        => GameFolders.Find(GameFolders.Ega) is { } path ? ResourceLoader.Open(new DirectoryGameContainer(path)) : null;
}
