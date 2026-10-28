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
    [InlineData(SoundDevice.Speaker, "c96207c9ed4bfa4d")]
    [InlineData(SoundDevice.Pcjr, "98553b4499d400bb")]
    [InlineData(SoundDevice.AdLib, "f0a3c52c7c6b7eb5")]
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
