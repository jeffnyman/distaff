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
