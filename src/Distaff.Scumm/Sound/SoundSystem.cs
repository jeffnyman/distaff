
namespace Distaff.Scumm.Sound;

/// <summary>The sound devices the EGA release plays on (ScummVM's <c>MDT_PCSPK</c>, <c>MDT_PCJR</c> and <c>MDT_ADLIB</c>).</summary>
public enum SoundDevice
{
    Speaker,
    Pcjr,
    AdLib,
}

/// <summary>
/// The sounds the scripts ask for: queued during a step and handed to the player at its end, as
/// ScummVM's <c>Sound</c> does for these versions (<c>startSound</c>, <c>addSoundToQueue</c>,
/// <c>processSoundQueues</c>, <c>triggerSound</c>, <c>stopSound</c>, <c>stopAllSounds</c> and
/// <c>isSoundRunning</c>). The PC speaker and the PCjr play the sounds' first block with
/// <see cref="PlayerV2"/>, the AdLib their second with <see cref="PlayerAd"/> on an emulated
/// OPL2, MAME's, as ScummVM picks by default (<c>setupMusic</c>). Ported by way of Maniacal.
/// The VGA CD's voices and music come from its CD track instead (<see cref="Cd"/>), mixed in.
/// </summary>
public sealed class SoundSystem : IDisposable
{
    private readonly Func<int, byte[]?> _load;
    private readonly List<int> _queue = [];

    /// <param name="device">The device the sounds play on.</param>
    /// <param name="load">A sound's data as the device plays it, or null when there is none.</param>
    /// <param name="sampleRate">The output's rate.</param>
    /// <param name="cdTracks">Opens a track of the CD from the game's folder, or gives null; none leaves the CD silent.</param>
    public SoundSystem(SoundDevice device, Func<int, byte[]?> load, int sampleRate, Func<int, ICdTrack?>? cdTracks = null)
    {
        _load = load;
        Device = device;
        Player = device == SoundDevice.AdLib
            ? new PlayerAd(sampleRate, new Opl.MameOpl(sampleRate))
            : new PlayerV2(sampleRate, device == SoundDevice.Pcjr);
        Cd = new CdPlayer(cdTracks, sampleRate);
        Output = new ClockedSource(new AudioMix(Player, Cd), sampleRate);
    }

    public SoundDevice Device { get; }

    /// <summary>The value of the sound card variable for the device. Mirrors <c>setSoundCardVarToCurrentConfig</c>.</summary>
    public int SoundCardValue => Device switch
    {
        SoundDevice.Pcjr => 1,
        SoundDevice.AdLib => 3,
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

    /// <summary>Mirrors <c>Sound::stopSound</c>: the sound stops, and leaves the queue.</summary>
    public void Stop(int sound)
    {
        Player.StopSound(sound);
        _queue.RemoveAll(s => s == sound);
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
