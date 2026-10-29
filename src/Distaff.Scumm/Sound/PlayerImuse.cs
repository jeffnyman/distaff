using Distaff.Scumm.Saves;
using Distaff.Scumm.Sound.IMuse;

namespace Distaff.Scumm.Sound;

/// <summary>
/// The Roland Upgrade's music and sounds: iMuse playing the RO sounds on an MT-32, as ScummVM's
/// <c>setupMusic</c> sets it up for an MT-32 (<c>IMuseDriver_MT32</c>, the old system, a native
/// MT-32, the tempo at 100%). The device's timer, 250 times a second of its own output, moves
/// iMuse on (<c>MidiDriver_MT32</c>, a <c>MidiDriver_Emulated</c>).
/// </summary>
/// <remarks>
/// The game thread starts and stops sounds while the host's audio thread renders, so both go
/// through one lock, as ScummVM's iMuse locks the mixer's mutex.
/// </remarks>
public sealed class PlayerImuse : MusicPlayer
{
    /// <summary>How often the MT-32 driver calls iMuse (<c>MidiDriver_Emulated</c>'s <c>_baseFreq</c>).</summary>
    public const int TimerFrequency = 250;

    private readonly object _sync = new();
    private readonly IMidiDevice _device;
    private readonly IMuseEngine _imuse;
    private int _musicVolume;

    /// <param name="sampleRate">The output's rate, which the device renders at.</param>
    /// <param name="device">The MT-32.</param>
    /// <param name="welcome">What the module's display shows when it is set up, as ScummVM shows "ScummVM" and its version.</param>
    public PlayerImuse(int sampleRate, IMidiDevice device, string welcome)
        : base(sampleRate)
    {
        _device = device;
        var driver = new IMuseDriverMt32(device, TimerFrequency);
        driver.Open(welcome);
        _imuse = new IMuseEngine(driver);
        device.SetTimer(TimerFrequency, _imuse.OnTimer);
    }

    /// <summary>
    /// A sound as iMuse takes it, from its Roland block (<see cref="Resources.ResourceLoader.LoadRolandSound"/>):
    /// "RO", then the block after its 4-byte header, in a buffer 6 bytes longer than that holds,
    /// the rest 0. Mirrors the <c>isRolandLoom</c> branch of <c>readSoundResourceSmallHeader</c>.
    /// </summary>
    public static byte[] RoResource(ReadOnlySpan<byte> block)
    {
        var data = new byte[block.Length + 2];
        data[0] = (byte)'R';
        data[1] = (byte)'O';
        block[4..].CopyTo(data.AsSpan(2));
        return data;
    }

    /// <summary>The most any sound's music timer has counted: for an RO sound, the markers it has passed. Mirrors <c>IMuseInternal::getMusicTimer</c>.</summary>
    public override int MusicTimer
    {
        get
        {
            lock (_sync)
            {
                return _imuse.MusicTimer;
            }
        }
    }

    /// <summary>Starts a sound, its data "RO" and then its track (<c>readSoundResourceSmallHeader</c> for <c>isRolandLoom</c>).</summary>
    public override void StartSound(int number, byte[] data)
    {
        lock (_sync)
        {
            _imuse.StartSound(number, data);
        }
    }

    public override void StopSound(int number)
    {
        lock (_sync)
        {
            _imuse.StopSound(number);
        }
    }

    public override void StopAllSounds()
    {
        lock (_sync)
        {
            _imuse.StopAllSounds();
        }
    }

    public override bool IsPlaying(int number)
    {
        lock (_sync)
        {
            return _imuse.IsPlaying(number);
        }
    }

    /// <summary>The music volume, 0 to 255 (<c>setMusicVolume</c>); iMuse has no effects volume of its own.</summary>
    public override void SetVolumes(int music, int sfx)
    {
        lock (_sync)
        {
            _musicVolume = music;
            _imuse.SetMusicVolume(music);
        }
    }

    /// <summary>iMuse's state as a save of a version keeps it (<c>saveLoadIMuse</c>).</summary>
    public byte[] SaveState(int version)
    {
        lock (_sync)
        {
            var s = SaveSerializer.ForSaving(version);
            _imuse.SaveLoad(s, _ => null);
            return s.ToArray();
        }
    }

    /// <summary>
    /// The first of a load, before every sound stops: the music volume to 0, as <c>loadState</c>
    /// sets it "to minimize stuttering". <see cref="LoadState"/> sets it again.
    /// </summary>
    public void BeginLoad()
    {
        lock (_sync)
        {
            _imuse.SetMusicVolume(0);
        }
    }

    /// <summary>
    /// The rest of a load, the sounds stopped: iMuse paused, its state read when the save has one
    /// (the sounds it plays read again from <paramref name="sound"/>, as RO resources), started
    /// again, and the music volume set again, as <c>syncSoundSettings</c> sets it after the load.
    /// A save without iMuse's state, such as the AdLib's, leaves the music stopped; ScummVM would
    /// read it as iMuse's state all the same.
    /// </summary>
    /// <param name="data">What <see cref="SaveState"/> wrote.</param>
    /// <param name="version">The save's version.</param>
    /// <param name="sound">A sound by number, as iMuse takes it.</param>
    public void LoadState(byte[] data, int version, Func<int, byte[]?> sound)
    {
        lock (_sync)
        {
            _imuse.Pause(true);
            if (data.Length >= MinimumStateSize)
            {
                _imuse.SaveLoad(SaveSerializer.ForLoading(data, version), sound);
            }

            _imuse.Pause(false);
            _imuse.SetMusicVolume(_musicVolume);
        }
    }

    /// <summary>The bytes of iMuse's state with nothing playing, at the oldest version read.</summary>
    private static int MinimumStateSize { get; } = new PlayerImuse(1, new NullDevice(), "").SaveState(ScummState.OldestVersion).Length;

    /// <summary>Silences the music and turns its notes off while the game is paused (<c>IMuseInternal::pause</c>).</summary>
    public void Pause(bool paused)
    {
        lock (_sync)
        {
            _imuse.Pause(paused);
        }
    }

    protected override void RenderStereo(Span<short> interleavedStereo)
    {
        lock (_sync)
        {
            _device.Render(interleavedStereo);
        }
    }

    /// <summary>A device that takes everything and plays nothing.</summary>
    private sealed class NullDevice : IMidiDevice
    {
        public void Send(uint message)
        {
        }

        public void SysEx(ReadOnlySpan<byte> message)
        {
        }

        public void Render(Span<short> interleavedStereo) => interleavedStereo.Clear();

        public void SetTimer(int ticksPerSecond, Action? tick)
        {
        }
    }
}
