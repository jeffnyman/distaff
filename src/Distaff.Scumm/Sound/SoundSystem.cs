
namespace Distaff.Scumm.Sound;

/// <summary>The sound devices the EGA release plays on (ScummVM's <c>MDT_PCSPK</c>, <c>MDT_PCJR</c> and <c>MDT_ADLIB</c>).</summary>
public enum SoundDevice
{
    /// <summary>The PC speaker.</summary>
    Speaker,

    /// <summary>The PCjr's or the Tandy's three voices.</summary>
    Pcjr,

    /// <summary>The AdLib, the EGA's default.</summary>
    AdLib,

    /// <summary>The Creative Music System (the Game Blaster).</summary>
    Cms,

    /// <summary>The Roland MT-32 or CM-32L of the EGA's Roland Upgrade, played by iMuse (<see cref="PlayerImuse"/>).</summary>
    Mt32,
}

/// <summary>
/// The sounds the scripts ask for: queued during a step and handed to the player at its end, as
/// ScummVM's <c>Sound</c> does for these versions (<c>startSound</c>, <c>addSoundToQueue</c>,
/// <c>processSoundQueues</c>, <c>triggerSound</c>, <c>stopSound</c>, <c>stopAllSounds</c> and
/// <c>isSoundRunning</c>). The PC speaker and the PCjr play the sounds' first block with
/// <see cref="PlayerV2"/>, the AdLib their second with <see cref="PlayerAd"/> on an emulated
/// OPL2, MAME's, as ScummVM picks by default (<c>setupMusic</c>), and the CMS one or the other
/// with <see cref="PlayerV2Cms"/>. Ported by way of Maniacal.
/// The VGA CD's voices and music come from its CD track instead (<see cref="Cd"/>), mixed in. The
/// FM Towns has a player of its own, which plays its music from the CD's tracks and its effects
/// on an emulation of its PCM and FM chips (<see cref="PlayerTowns"/>).
/// </summary>
internal sealed class SoundSystem : IDisposable
{
    private readonly Func<int, byte[]?> _load;
    private readonly List<int> _queue = [];

    /// <param name="device">The device the sounds play on.</param>
    /// <param name="load">A sound's data as the device plays it, or null when there is none.</param>
    /// <param name="sampleRate">The output's rate.</param>
    /// <param name="cdTracks">Opens a track of the CD from the game's folder, or gives null; none leaves the CD silent.</param>
    /// <param name="player">Makes a player of the release's own, given the CD, in place of the device's.</param>
    /// <param name="mt32">Makes the MT-32 at the output's rate, for <see cref="SoundDevice.Mt32"/>.</param>
    public SoundSystem(SoundDevice device, Func<int, byte[]?> load, int sampleRate, Func<int, ICdTrack?>? cdTracks = null, Func<CdPlayer, MusicPlayer>? player = null, Func<int, IMidiDevice>? mt32 = null)
    {
        _load = load;
        Device = device;
        Cd = new CdPlayer(cdTracks, sampleRate);
        Player = player?.Invoke(Cd) ?? device switch
        {
            SoundDevice.AdLib => new PlayerAd(sampleRate, new Opl.MameOpl(sampleRate)),
            SoundDevice.Cms => new PlayerV2Cms(sampleRate),
            SoundDevice.Mt32 => new PlayerImuse(sampleRate, (mt32 ?? throw new ArgumentException("The MT-32 needs its ROMs.", nameof(mt32)))(sampleRate), Welcome),
            _ => new PlayerV2(sampleRate, device == SoundDevice.Pcjr),
        };
        _mix = new AudioMix(Player, Cd);
        Output = new ClockedSource(_mix, sampleRate);
        SyncVolumes();
    }

    /// <summary>What the MT-32's display shows when iMuse sets it up, as ScummVM shows its name and version.</summary>
    private static string Welcome => $"Distaff {typeof(SoundSystem).Assembly.GetName().Version?.ToString(3)}";

    private int _musicVolume = Hosting.AudioVolume.Default, _sfxVolume = Hosting.AudioVolume.Default;

    /// <summary>The music's volume, 0 to 256: ScummVM's <c>music_volume</c>, 192 unless set.</summary>
    public int MusicVolume
    {
        get => _musicVolume;
        set
        {
            _musicVolume = Hosting.AudioVolume.Clamp(value);
            SyncVolumes();
        }
    }

    /// <summary>The sound effects' volume, 0 to 256: ScummVM's <c>sfx_volume</c>, 192 unless set.</summary>
    public int SfxVolume
    {
        get => _sfxVolume;
        set
        {
            _sfxVolume = Hosting.AudioVolume.Clamp(value);
            SyncVolumes();
        }
    }

    /// <summary>
    /// The volumes go where ScummVM's settings take them (<c>syncSoundSettings</c>): to the player,
    /// which plays at them itself, and the music's to the mixer's music, which the CD plays as.
    /// </summary>
    private void SyncVolumes()
    {
        Player.SetVolumes(_musicVolume, _sfxVolume);
        Cd.MusicVolume = _musicVolume;
    }

    private readonly AudioMix _mix;

    public SoundDevice Device { get; }

    /// <summary>Whether every sound waits where it is, silent, while the game waits on its menu (<c>pauseSounds</c>).</summary>
    public bool Paused
    {
        get => _mix.Paused;
        set => _mix.Paused = value;
    }

    /// <summary>The value of the sound card variable for the device. Mirrors <c>setSoundCardVarToCurrentConfig</c>.</summary>
    public int SoundCardValue => Device switch
    {
        SoundDevice.Pcjr => 1,
        SoundDevice.Cms => 2,
        SoundDevice.AdLib => 3,

        // the default for Loom's EGA with a MIDI device: the Roland Upgrade
        SoundDevice.Mt32 => 4,
        _ => 0,
    };

    public MusicPlayer Player { get; }

    /// <summary>The CD's audio, played from a track file.</summary>
    public CdPlayer Cd { get; }

    /// <summary>What a host plays; it renders the sound itself in step with the game when the host reads none.</summary>
    public ClockedSource Output { get; }

    /// <summary>The last sound asked for. Mirrors <c>_lastSound</c>.</summary>
    public int LastSound { get; private set; }

    /// <summary>The sounds started this step, waiting for its end.</summary>
    public IReadOnlyList<int> Queue => _queue;

    /// <summary>Mirrors <c>Sound::startSound</c>: the sound waits in the queue until the step's end.</summary>
    public void Start(int sound)
    {
        LastSound = sound;
        _queue.Add(sound);
    }

    /// <summary>The queued sounds go to the player, the last asked for first. Mirrors <c>processSoundQueues</c> and <c>triggerSound</c>.</summary>
    public void ProcessQueue()
    {
        for (int i = _queue.Count - 1; i >= 0; i--)
        {
            int sound = _queue[i];
            if (sound != 0 && _load(sound) is { } data)
            {
                Player.StartSound(sound, data);
            }
        }

        _queue.Clear();
    }

    /// <summary>
    /// Mirrors <c>Sound::stopSound</c>: the sound stops, and its places in the queue are emptied,
    /// as the FM Towns' player may stop a sound while the queue is being played.
    /// </summary>
    public void Stop(int sound)
    {
        Player.StopSound(sound);
        for (int i = 0; i < _queue.Count; i++)
        {
            if (_queue[i] == sound)
            {
                _queue[i] = 0;
            }
        }
    }

    /// <summary>Mirrors <c>Sound::stopAllSounds</c>, which <c>stopMusic</c> calls.</summary>
    public void StopAll()
    {
        LastSound = 0;
        _queue.Clear();
        Player.StopAllSounds();
    }

    /// <summary>Whether a sound waits in the queue or in the player, or plays. Mirrors <c>Sound::isSoundRunning</c>.</summary>
    public bool IsRunning(int sound) => _queue.Contains(sound) || Player.IsPlaying(sound);

    /// <summary>Keeps the sound going in step with the game when the host plays none. Called once a step.</summary>
    public void Update(TimeSpan gameTime) => Output.Update(gameTime);

    public void Dispose() => Cd.Dispose();
}
