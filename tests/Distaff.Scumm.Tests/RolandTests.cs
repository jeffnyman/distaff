using Distaff.Audio.Mt32;
using Distaff.Scumm.Game;
using Distaff.Scumm.Input;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Scripts;
using Distaff.Scumm.Sound;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>
/// The EGA's Roland Upgrade in the game: iMuse on an MT-32, which here is a silent device timed as
/// the emulator is (<see cref="EmulatedMidiDevice"/>), so what the scripts see of the music needs
/// no ROMs. Tests against the game data pass without checking when it is absent.
/// </summary>
public class RolandTests
{
    /// <summary>The scripts read 4, the Roland Upgrade, as ScummVM sets it for Loom's EGA with a MIDI device.</summary>
    [Fact]
    public void The_sound_card_variable_is_the_rolands()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader, sound: SoundDevice.Mt32, mt32: rate => new Silent(rate));
        Assert.Equal(4, world.Engine.Variables[Vars.SoundCard]);
    }

    /// <summary>
    /// With PRACTICE chosen, the overture (sound 56, the Upgrade's own) plays on the title and the
    /// scripts follow its music timer, which counts its markers: Lucasfilm's logo at 204, the
    /// overture's end at 278, after which the opening's music (sound 57) starts and the opening
    /// plays out. The times are at the frame each is first seen; the MT-32 emulator, with the ROMs
    /// (see <see cref="Mt32Tests"/>), gives the same.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_overture_leads_the_title_by_its_music_timer(bool emulated)
    {
        Func<int, IMidiDevice> mt32 = rate => new Silent(rate);
        if (emulated)
        {
            if (Mt32Tests.RomFolder() is not { } roms || Mt32RomSet.FromDirectory(roms) is not { } set)
            {
                return;
            }

            mt32 = rate => new Mt32Emulator(set, rate);
        }

        if (Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader, sound: SoundDevice.Mt32, mt32: mt32);
        world.Boot();
        Run(world, 100);
        world.Click(160, 92);

        var reached = new Dictionary<int, double>();
        while (!reached.ContainsKey(278) && world.Time.TotalSeconds < 200)
        {
            Run(world, 1);
            int timer = world.Engine.Variables[Vars.MusicTimer];
            if (timer is 204 or 278 && !reached.ContainsKey(timer))
            {
                reached[timer] = world.Time.TotalSeconds;
                Assert.Equal(2, world.CurrentRoomNumber);
            }
        }

        Assert.Contains(56, world.SoundsStarted);
        Assert.DoesNotContain(57, world.SoundsStarted);
        Assert.Equal(126.01, reached[204], 2);
        Assert.Equal(162.11, reached[278], 2);

        Run(world, 400);
        Assert.Contains(57, world.SoundsStarted);
        Assert.Contains("Rise, son of Cygna.", world.MessageLog);
    }

    /// <summary>
    /// The pause key pauses iMuse before the mixer (<c>ScummEngine::pauseSounds</c>): the parts'
    /// volumes go to 0 and every channel's notes are turned off, and the music timer holds until
    /// the game goes on.
    /// </summary>
    [Fact]
    public void Pausing_silences_the_module_and_holds_the_music_timer()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        Silent? device = null;
        var world = new GameWorld(loader, sound: SoundDevice.Mt32, mt32: rate => device = new Silent(rate));
        world.Boot();
        Run(world, 100);
        world.Click(160, 92);
        Run(world, 500);

        int timer = world.Engine.Variables[Vars.MusicTimer];
        int sent = device!.Sent.Count;
        world.KeyDown(Key.Space, KeyModifiers.None, ' ');
        Run(world, 100, frames: false);

        // volume 0 on the module's parts 8 to 1 (the rhythm part, 9, left), then All Notes Off on all 16
        uint[] expected = [.. Enumerable.Range(1, 8).Reverse().Select(c => 0xB0u | (uint)c | 0x0700u), .. Enumerable.Range(0, 16).Select(c => 0xB0u | (uint)c | 0x7B00u)];
        Assert.Equal(expected, device.Sent.Skip(sent));
        Assert.Equal(timer, world.Engine.Variables[Vars.MusicTimer]);

        world.KeyDown(Key.Space, KeyModifiers.None, ' ');
        Run(world, 100);
        Assert.True(world.Engine.Variables[Vars.MusicTimer] > timer);
    }

    /// <summary>
    /// A save keeps iMuse's state (<c>saveLoadIMuse</c>, as <see cref="ImuseTests"/> checks it
    /// against ScummVM's), at version 124 and at 108, and a load plays the overture on from it:
    /// saved at a music timer of 96, it goes on from 39, as ScummVM takes it back (its parser sent
    /// to a tick counted in markers), and the title follows it from there.
    /// </summary>
    [Theory]
    [InlineData(124, 3492)]
    [InlineData(108, 3372)]
    public void A_save_keeps_the_music_and_a_load_plays_it_on(int version, int stateSize)
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var world = Overture(loader, SoundDevice.Mt32);
        Assert.Equal(96, world.Engine.Variables[Vars.MusicTimer]);
        var state = world.Capture(version);
        Assert.Equal(stateSize, state.SoundEngineData.Length);
        Assert.Equal(((PlayerImuse)world.Sound.Player).SaveState(version), state.SoundEngineData);

        var loaded = Loaded(SoundDevice.Mt32, world.Save("overture", DateTime.Now, version));
        Assert.True(loaded.Sound.IsRunning(56));
        Assert.Equal(39, loaded.Engine.Variables[Vars.MusicTimer]);
        Run(loaded, 300);
        Assert.True(loaded.Sound.IsRunning(56));
        Assert.True(loaded.Engine.Variables[Vars.MusicTimer] > 39);
        Assert.Equal(69, loaded.CurrentRoomNumber);
    }

    /// <summary>
    /// A save made with the AdLib and loaded with the MT-32, or the other way, leaves the music
    /// stopped: neither player reads the other's state. ScummVM reads it all the same, and plays
    /// from what it makes of it.
    /// </summary>
    [Theory]
    [InlineData(SoundDevice.AdLib, SoundDevice.Mt32)]
    [InlineData(SoundDevice.Mt32, SoundDevice.AdLib)]
    public void A_save_of_the_other_device_leaves_the_music_stopped(SoundDevice saved, SoundDevice loading)
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var loaded = Loaded(loading, Overture(loader, saved).Save("overture", DateTime.Now, 124));
        Run(loaded, 100);
        Assert.False(loaded.Sound.IsRunning(25));
        Assert.False(loaded.Sound.IsRunning(56));
        Assert.Equal(0, loaded.Engine.Variables[Vars.MusicTimer]);
    }

    /// <summary>A world playing the title's music, 600 frames after PRACTICE was chosen.</summary>
    private static GameWorld Overture(ResourceLoader loader, SoundDevice device)
    {
        var world = new GameWorld(loader, sound: device, mt32: rate => new Silent(rate));
        world.Boot();
        Run(world, 100);
        world.Click(160, 92);
        Run(world, 600);
        return world;
    }

    /// <summary>A fresh world with a device, a save loaded into it and a frame played.</summary>
    private static GameWorld Loaded(SoundDevice device, byte[] save)
    {
        var world = new GameWorld(Open()!, sound: device, mt32: rate => new Silent(rate));
        world.Boot();
        Run(world, 5);
        world.Load(save);
        Run(world, 1);
        return world;
    }

    /// <summary>The MT-32 without a sound: a device that renders silence on the emulator's timing, and keeps what it is sent.</summary>
    private sealed class Silent(int outputRate) : EmulatedMidiDevice(32000, outputRate)
    {
        public List<uint> Sent { get; } = [];

        public override void Send(uint message) => Sent.Add(message);

        public override void SysEx(ReadOnlySpan<byte> message)
        {
        }

        protected override void GenerateSamples(Span<short> interleavedStereo, int frames) => interleavedStereo[..(frames * 2)].Clear();
    }

    /// <summary>
    /// Steps the world, reading its sound as a host does, a step's worth after each step; by frames,
    /// or by single steps while the game stands still for a banner.
    /// </summary>
    private static void Run(GameWorld world, int steps, bool frames = true)
    {
        long read = (long)(world.Time.TotalSeconds * GameWorld.DefaultSampleRate);
        for (int i = 0; i < steps; i++)
        {
            if (frames)
            {
                world.StepFrame();
            }
            else
            {
                world.Step();
            }

            long due = (long)(world.Time.TotalSeconds * GameWorld.DefaultSampleRate);
            world.Sound.Output.Read(new short[(due - read) * 2]);
            read = due;
        }
    }

    private static ResourceLoader? Open()
        => GameFolders.Find(GameFolders.Ega) is { } path ? ResourceLoader.Open(new DirectoryGameContainer(path)) : null;
}
