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
    [InlineData(SoundDevice.Speaker, "941c45d94654e6c9")]
    [InlineData(SoundDevice.Pcjr, "73a3a199f573a056")]
    [InlineData(SoundDevice.AdLib, "acac7160e3fa5592")]
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

    private static string Hash(List<short> samples)
        => Convert.ToHexStringLower(SHA256.HashData(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(samples))))[..16];

    /// <summary>Steps the world, reading its sound as a host does, a step's worth after each step.</summary>
    private static void Run(GameWorld world, int steps, List<short> audio)
    {
        long read = (long)(world.Time.TotalSeconds * GameWorld.DefaultSampleRate);
        for (int i = 0; i < steps; i++)
        {
            world.Step();
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
