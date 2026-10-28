namespace Distaff.Scumm.Sound;

/// <summary>
/// The FM Towns' sound player. Mirrors ScummVM's <c>Player_Towns_v1</c> for version 3: a sound
/// resource's type, its 14th byte, says what it is. Type 2 plays part of a CD track: its volume,
/// unless the scripts set the sound's own, then the track, how many times (255 for ever), and its
/// start and end as minutes, seconds and frames (<c>playCdaTrack</c>). Types 0, sampled sounds on
/// the Towns' PCM chip, and 1, music on its FM chip (Euphony), are not played yet. The CD's volume
/// reaches the CD as ScummVM's Towns audio sets its CD channel (<c>intf_setOutputVolume</c>,
/// <c>updateOutputVolumeInternal</c>).
/// </summary>
public sealed class PlayerTowns : MusicPlayer
{
    private readonly CdPlayer _cd;
    private readonly Action<int, int, int, int> _playCd;
    private readonly Action _stopCd;

    // the volume and note the scripts set for a sound's next start (_soundOverride)
    private readonly (int Left, int Right, int Note)[] _overrides = new (int, int, int)[200];

    /// <param name="sampleRate">The output's rate.</param>
    /// <param name="cd">The CD, whose volume the player sets.</param>
    /// <param name="playCd">Plays a track from a start for a length in CD frames, a number of times (<c>Sound::playCDTrack</c>).</param>
    /// <param name="stopCd">Stops the CD (<c>Sound::stopCD</c>).</param>
    public PlayerTowns(int sampleRate, CdPlayer cd, Action<int, int, int, int> playCd, Action stopCd)
        : base(sampleRate)
    {
        _cd = cd;
        _playCd = playCd;
        _stopCd = stopCd;
        SetVolumeCd(255, 255);
    }

    /// <summary>The sound playing on the CD, or 0 (<c>_cdaCurrentSound</c>).</summary>
    public int CurrentCdSound { get; private set; }

    /// <summary>The CD's volume, left and right (<c>_cdaVolLeft</c>, <c>_cdaVolRight</c>).</summary>
    public (int Left, int Right) CdVolume { get; private set; }

    /// <summary>The CD's volume as one number (<c>getCurrentCdaVolume</c>).</summary>
    public int CurrentCdVolume => (CdVolume.Left + CdVolume.Right + 1) >> 1;

    public override int MusicTimer => 0;

    public override void StartSound(int number, byte[] data)
    {
        if (data.Length < 14)
        {
            return;
        }

        if (data[13] == 2 && data.Length >= 6 + 24)
        {
            PlayCdaTrack(number, data.AsSpan(6));
        }

        if (number >= 0 && number < _overrides.Length)
        {
            _overrides[number] = default;
        }
    }

    public override void StopSound(int number)
    {
        if (number == 0 || number == CurrentCdSound)
        {
            CurrentCdSound = 0;
            _stopCd();
        }
    }

    public override void StopAllSounds()
    {
        CurrentCdSound = 0;
        _stopCd();
    }

    public override bool IsPlaying(int number) => number != 0 && number == CurrentCdSound && _cd.IsPlaying;

    /// <summary>
    /// Sets the CD's volume, left and right from 0 to 255. Mirrors <c>setVolumeCD</c>, which hands
    /// half of each to the Towns' audio, which keeps 6 bits of that: the CD plays at the louder
    /// side's level, to the power of 1.5, with the balance toward the louder.
    /// </summary>
    public void SetVolumeCd(int left, int right)
    {
        CdVolume = (left & 0xFF, right & 0xFF);
        int l = ((CdVolume.Left >> 1) & 0x7E) >> 1;
        int r = ((CdVolume.Right >> 1) & 0x7E) >> 1;
        int max = Math.Max(l, r);
        int volume = (int)(MathF.Pow(max, 1.5f) * 255.0f / MathF.Pow(63.0f, 1.5f));
        int balance = max != 0 ? (int)((r - l) * 127 / (float)max) : 0;
        _cd.SetVolume(volume, balance);
    }

    /// <summary>The volume the scripts give a sound for its next start, 0 and 0 for its own (<c>setSoundVolume</c>).</summary>
    public void SetSoundVolume(int sound, int left, int right)
    {
        if (sound > 0 && sound < _overrides.Length)
        {
            _overrides[sound] = _overrides[sound] with { Left = left, Right = right };
        }
    }

    /// <summary>The note the scripts give a sampled sound for its next start (<c>setSoundNote</c>).</summary>
    public void SetSoundNote(int sound, int note)
    {
        if (sound > 0 && sound < _overrides.Length)
        {
            _overrides[sound] = _overrides[sound] with { Note = note };
        }
    }

    protected override void RenderMono(Span<short> buffer) => buffer.Clear();

    /// <summary>Mirrors <c>playCdaTrack</c>: a sound already playing on the CD goes on.</summary>
    private void PlayCdaTrack(int sound, ReadOnlySpan<byte> data)
    {
        if (sound == 0)
        {
            return;
        }

        var o = sound < _overrides.Length ? _overrides[sound] : default;
        if (o.Left + o.Right != 0)
        {
            SetVolumeCd(o.Left, o.Right);
        }
        else
        {
            SetVolumeCd(data[8], data[9]);
        }

        if (sound == CurrentCdSound && _cd.IsPlaying)
        {
            return;
        }

        var p = data[16..];
        int loops = p[1];
        int start = ((p[2] * 60) + p[3]) * CdPlayer.FramesPerSecond + p[4];
        int end = ((p[5] * 60) + p[6]) * CdPlayer.FramesPerSecond + p[7];
        _playCd(p[0], start, end <= start ? 0 : end - start, loops == 0xFF ? -1 : loops);
        CurrentCdSound = sound;
    }
}
