using Distaff.Scumm.Sound.Towns;

namespace Distaff.Scumm.Sound;

/// <summary>
/// The FM Towns' sound player. Mirrors ScummVM's <c>Player_Towns_v1</c> for version 3: a sound
/// resource's type, its 14th byte, says what it is. Type 0 is sampled sound on the Towns' PCM
/// chip, a sample for each of its channels, played on the PCM channels kept for effects
/// (<c>playPcmTrack</c>); type 1 is music on its FM chip, a Euphony track with its instruments
/// (<c>playEuphonyTrack</c>); type 2 plays part of a CD track: its volume, unless the scripts set
/// the sound's own, then the track, how many times (255 for ever), and its start and end as
/// minutes, seconds and frames (<c>playCdaTrack</c>). The chips are ScummVM's emulation
/// (<see cref="TownsAudio"/>, <see cref="EuphonyPlayer"/>), heard in stereo. The CD's volume
/// reaches the CD as ScummVM's Towns audio sets its CD channel (<c>intf_setOutputVolume</c>,
/// <c>updateOutputVolumeInternal</c>).
/// </summary>
/// <remarks>
/// The chips run in the audio's samples: the music's timers and an effect's end fall where the
/// audio has been rendered to, by the host or, without one, by the sound system in step with the
/// game, so a sound's status follows what has been heard, as in ScummVM. The music and effects
/// volumes are the full 256 (<c>syncSoundSettings</c> at full volume); the host's master volume
/// applies to the output instead. Loom's scripts send the player no commands (<c>doCommand</c>,
/// which only <c>o5_soundKludge</c> reaches), so restarting looping sounds and the commands' own
/// starts and stops are left out, as is the saved state (<c>saveLoadWithSerializer</c>,
/// <c>restoreAfterLoad</c>).
/// </remarks>
public sealed class PlayerTowns : MusicPlayer
{
    private const int NumSoundMax = 200;

    private readonly object _sync = new();
    private readonly CdPlayer _cd;
    private readonly Action<int, int, int, int> _playCd;
    private readonly Action _stopCd;
    private readonly Action<int> _stopSound;
    private readonly TownsAudio _intf;
    private readonly EuphonyPlayer _player;

    // the volume and note the scripts set for a sound's next start (_soundOverride)
    private readonly (int Left, int Right, int Note)[] _overrides = new (int, int, int)[NumSoundMax];

    // the sound on each PCM channel, 1 to 8 (_pcmCurrentSound)
    private readonly PcmCurrentSound[] _pcmCurrentSound = [new(), new(), new(), new(), new(), new(), new(), new(), new()];

    private int _eupCurrentSound;
    private bool _eupLooping;
    private int _eupVolLeft, _eupVolRight;

    // how many times the CD sound plays (_cdaNumLoops), and the CD sound and its count a save holds
    private int _cdaNumLoops;
    private int _cdaCurrentSoundTemp, _cdaNumLoopsTemp;

    /// <summary>The size of the player's state in a save: the CD's and Euphony's eight bytes, then nine PCM channels of 13 each.</summary>
    public const int SaveStateSize = 8 + 9 * 13;

    /// <param name="sampleRate">The output's rate.</param>
    /// <param name="cd">The CD, whose volume the player sets.</param>
    /// <param name="playCd">Plays a track from a start for a length in CD frames, a number of times (<c>Sound::playCDTrack</c>).</param>
    /// <param name="stopCd">Stops the CD (<c>Sound::stopCD</c>).</param>
    /// <param name="stopSound">Stops a sound everywhere, the sound queue too (<c>Sound::stopSound</c>); without it, the player's own stop.</param>
    public PlayerTowns(int sampleRate, CdPlayer cd, Action<int, int, int, int> playCd, Action stopCd, Action<int>? stopSound = null)
        : base(sampleRate)
    {
        _cd = cd;
        _playCd = playCd;
        _stopCd = stopCd;
        _stopSound = stopSound ?? StopSound;

        // init: the driver keeps all 8 PCM channels for effects, and every channel counts as an effect's
        _intf = new TownsAudio(sampleRate);
        _player = new EuphonyPlayer(_intf);
        _player.Driver.ReserveSoundEffectChannels(8);
        _intf.SetSoundEffectChanMask(-1);
        SetVolumeCd(255, 255);

        // at the full music and effects volumes until the settings give theirs (syncSoundSettings)
        SetVolumes(TownsFmSynth.MaxMixerVolume, TownsFmSynth.MaxMixerVolume);
    }

    /// <summary>
    /// The music volume goes to the FM music, the effects volume to the sampled sounds. Mirrors
    /// <c>Player_Towns_v1::setMusicVolume</c> (the Euphony driver's, <c>setMusicVolume</c>) and
    /// <c>Player_Towns::setSfxVolume</c> (<c>setSoundEffectVolume</c>).
    /// </summary>
    public override void SetVolumes(int music, int sfx)
    {
        lock (_sync)
        {
            _intf.SetMusicVolume(music);
            _intf.SetSoundEffectVolume(sfx);
        }
    }

    /// <summary>The sound playing on the CD, or 0 (<c>_cdaCurrentSound</c>).</summary>
    public int CurrentCdSound { get; private set; }

    /// <summary>The CD's volume, left and right (<c>_cdaVolLeft</c>, <c>_cdaVolRight</c>).</summary>
    public (int Left, int Right) CdVolume { get; private set; }

    /// <summary>The CD's volume as one number (<c>getCurrentCdaVolume</c>).</summary>
    public int CurrentCdVolume => (CdVolume.Left + CdVolume.Right + 1) >> 1;

    /// <summary>The sound whose Euphony track was last started, or 0 (<c>_eupCurrentSound</c>).</summary>
    public int CurrentEuphonySound
    {
        get
        {
            lock (_sync)
            {
                return _eupCurrentSound;
            }
        }
    }

    public override int MusicTimer => 0;

    /// <summary>Mirrors <c>startSound</c>: by its type, a sampled sound, a Euphony track or part of the CD.</summary>
    public override void StartSound(int number, byte[] data)
    {
        if (data.Length < 14)
        {
            return;
        }

        lock (_sync)
        {
            var o = number >= 0 && number < NumSoundMax ? _overrides[number] : default;
            int type = data[13];
            if (type == 0)
            {
                int velocity = (byte)(o.Left + o.Right);
                velocity = velocity != 0 ? velocity >> 2 : data[14] >> 1;
                int len = BitConverter.ToUInt16(data, 0) + 2;
                int note = o.Note != 0 ? o.Note : len > 50 && data.Length > 50 ? data[50] : 60;
                PlayPcmTrack(number, data, velocity, 64, note, BitConverter.ToUInt16(data, 10));
            }
            else if (type == 1)
            {
                PlayEuphonyTrack(number, data);
            }
            else if (type == 2 && data.Length >= 6 + 24)
            {
                PlayCdaTrack(number, data.AsSpan(6));
            }

            if (number >= 0 && number < NumSoundMax)
            {
                _overrides[number] = default;
            }
        }
    }

    /// <summary>Mirrors <c>stopSound</c>: 0 stops the CD and every sampled sound.</summary>
    public override void StopSound(int number)
    {
        lock (_sync)
        {
            if (number == 0 || number == CurrentCdSound)
            {
                CurrentCdSound = 0;
                _stopCd();
            }

            if (number != 0 && number == _eupCurrentSound)
            {
                _eupCurrentSound = 0;
                _eupLooping = false;
                _player.Stop();
            }

            StopPcmTrack(number);
        }
    }

    /// <summary>Mirrors <c>stopAllSounds</c>.</summary>
    public override void StopAllSounds()
    {
        lock (_sync)
        {
            CurrentCdSound = 0;
            _stopCd();

            _eupCurrentSound = 0;
            _eupLooping = false;
            _player.Stop();

            StopPcmTrack(0);
        }
    }

    /// <summary>
    /// Mirrors <c>getSoundStatus</c>: the CD's sound while the CD plays, the Euphony track's while
    /// it plays, and a sampled sound while the first PCM channel it was given plays.
    /// </summary>
    public override bool IsPlaying(int number)
    {
        if (number == 0)
        {
            return false;
        }

        lock (_sync)
        {
            if (number == CurrentCdSound)
            {
                return _cd.IsPlaying;
            }

            if (number == _eupCurrentSound)
            {
                return _player.IsPlaying;
            }

            for (int i = 1; i < 9; i++)
            {
                if (_pcmCurrentSound[i].Index == number)
                {
                    return _intf.PcmEffectPlaying(i + 0x3f) != 0;
                }
            }

            return false;
        }
    }

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
        if (sound > 0 && sound < NumSoundMax)
        {
            lock (_sync)
            {
                _overrides[sound] = _overrides[sound] with { Left = left & 0xFF, Right = right & 0xFF };
            }
        }
    }

    /// <summary>The note the scripts give a sampled sound for its next start (<c>setSoundNote</c>).</summary>
    public void SetSoundNote(int sound, int note)
    {
        if (sound > 0 && sound < NumSoundMax)
        {
            lock (_sync)
            {
                _overrides[sound] = _overrides[sound] with { Note = note & 0xFF };
            }
        }
    }

    /// <summary>The Towns' audio, FM and PCM, in stereo, as the mixer reads its stream (<c>TownsPC98_FmSynth::readBuffer</c>).</summary>
    protected override void RenderStereo(Span<short> interleavedStereo)
    {
        lock (_sync)
        {
            _intf.ReadBuffer(interleavedStereo);
        }
    }

    /// <summary>
    /// Mirrors <c>playPcmTrack</c>: each of the sound's samples, its 32-byte header first, on a
    /// PCM channel of its own at the pan, note and velocity, until no channel can be had.
    /// </summary>
    private void PlayPcmTrack(int sound, byte[] resource, int velo, int pan, int note, int priority)
    {
        // the resource's data follows its 6-byte header; the samples start 16 bytes into that
        int sfx = 6 + 16;
        int numChan = resource[6 + 14];
        for (int i = 0; i < numChan; i++)
        {
            int chan = AllocatePcmChannel(sound, i, priority);
            if (chan == 0)
            {
                return;
            }

            // callback 70 first sets the output's mutes (_unkFlags, 0x33), which leave the CD as it is
            _intf.SetPanPos(chan + 0x3f, pan);
            _intf.PcmPlayEffect(chan + 0x3f, note, velo, resource.AsSpan(sfx));

            var p = _pcmCurrentSound[chan];
            p.Note = note;
            p.Velo = velo;
            p.Pan = pan;
            p.Paused = false;
            p.Looping = BitConverter.ToUInt32(resource, sfx + 20) != 0;

            sfx += (int)BitConverter.ToUInt32(resource, sfx + 12) + 32;
        }
    }

    /// <summary>Mirrors <c>stopPcmTrack</c>: the sound's channels, or all for 0, stop.</summary>
    private void StopPcmTrack(int sound)
    {
        for (int i = 1; i < 9; i++)
        {
            if (sound == _pcmCurrentSound[i].Index || sound == 0)
            {
                _intf.PcmChanOff(i + 0x3f);
                _pcmCurrentSound[i].Index = 0;
            }
        }
    }

    /// <summary>
    /// Mirrors <c>allocatePcmChannel</c>: the lowest channel that is free or whose sound has
    /// ended, every ended sound being stopped on the way (<c>Sound::stopSound</c>); with none, the
    /// highest whose priority is no more than the sound's, its sound stopped.
    /// </summary>
    private int AllocatePcmChannel(int sound, int sfxChanRelIndex, int priority)
    {
        int chan = 0;
        for (int i = 8; i != 0; i--)
        {
            if (_pcmCurrentSound[i].Index == 0)
            {
                chan = i;
                continue;
            }

            if (_intf.PcmEffectPlaying(i + 0x3f) != 0)
            {
                continue;
            }

            chan = i;
            StopChannelSound(chan);
        }

        if (chan == 0)
        {
            for (int i = 1; i < 9; i++)
            {
                if ((uint)priority >= _pcmCurrentSound[i].Priority)
                {
                    chan = i;
                }
            }

            StopChannelSound(chan);
        }

        if (chan != 0)
        {
            var p = _pcmCurrentSound[chan];
            p.Index = sound;
            p.Chan = sfxChanRelIndex;
            p.Priority = (uint)priority;
        }

        return chan;
    }

    /// <summary>A channel's sound stops: a voice (0xffff) on its channel alone, a sound everywhere.</summary>
    private void StopChannelSound(int chan)
    {
        if (_pcmCurrentSound[chan].Index == 0xffff)
        {
            _intf.PcmChanOff(chan + 0x3f);
        }
        else
        {
            _stopSound(_pcmCurrentSound[chan].Index);
        }
    }

    /// <summary>
    /// Mirrors <c>playEuphonyTrack</c>: the 32 parts' settings, the six FM channels' parts, the
    /// instruments, the channels' level from the sound's volume, then the track at its tempo,
    /// looping unless its loop byte is 1. A track still playing goes on, and this one does not start.
    /// </summary>
    private void PlayEuphonyTrack(int sound, byte[] resource)
    {
        const int data = 6;
        int pos = data + 16;
        int src = pos + resource[data + 14] * 48;
        int trackData = src + 150;

        for (int i = 0; i < 32; i++)
        {
            _player.ConfigPartEnable(i, resource[src++]);
        }

        for (int i = 0; i < 32; i++)
        {
            _player.ConfigPartSetType(i, 0xff);
        }

        for (int i = 0; i < 32; i++)
        {
            _player.ConfigPartRemap(i, resource[src++]);
        }

        for (int i = 0; i < 32; i++)
        {
            _player.ConfigPartAdjustVolume(i, resource[src++]);
        }

        for (int i = 0; i < 32; i++)
        {
            _player.ConfigPartSetTranspose(i, resource[src++]);
        }

        src += 8;
        for (int i = 0; i < 6; i++)
        {
            _player.Driver.AssignPartToChannel(i, resource[src++]);
        }

        for (int i = 0; i < resource[data + 14]; i++)
        {
            _player.Driver.LoadInstrument(i, i, resource.AsSpan(pos + i * 48));
            _player.Driver.SetInstrument(i, i);
        }

        var o = sound >= 0 && sound < NumSoundMax ? _overrides[sound] : default;
        _eupVolLeft = o.Left;
        _eupVolRight = o.Right;
        int lvl = o.Left + o.Right;
        if (lvl == 0)
        {
            lvl = resource[data + 8] + resource[data + 9];
        }

        lvl >>= 2;

        for (int i = 0; i < 6; i++)
        {
            _player.Driver.ChannelVolume(i, lvl);
        }

        int trackSize = (int)BitConverter.ToUInt32(resource, src);
        src += 4;
        int startTick = resource[src++];

        _player.SetTempo(resource[src++]);
        _player.StartTrack(resource, trackData, trackSize, startTick);

        _eupLooping = resource[src] != 1;
        _player.SetLoopStatus(_eupLooping);
        _player.Resume();
        _eupCurrentSound = sound;
    }

    /// <summary>
    /// The player's state as a save keeps it (<c>Player_Towns_v1::saveLoadWithSerializer</c> and
    /// <c>Player_Towns::saveLoadWithSerializer</c>): the CD sound, when it still plays and plays
    /// more than once, and its count; the CD's volume; the Euphony sound, unless it has ended and
    /// does not loop, whether it loops, and its volume; then each PCM channel's sound, those that
    /// have ended let go first.
    /// </summary>
    public byte[] SaveState()
    {
        lock (_sync)
        {
            var data = new byte[SaveStateSize];
            data[0] = (byte)(_cd.IsPlaying && _cdaNumLoops > 1 ? CurrentCdSound & 0xFF : 0);
            data[1] = (byte)_cdaNumLoops;
            (data[2], data[3]) = ((byte)CdVolume.Left, (byte)CdVolume.Right);
            if (!_eupLooping && !_player.IsPlaying)
            {
                _eupCurrentSound = 0;
            }

            (data[4], data[5], data[6], data[7]) = ((byte)_eupCurrentSound, (byte)(_eupLooping ? 1 : 0), (byte)_eupVolLeft, (byte)_eupVolRight);
            for (int i = 1; i < 9; i++)
            {
                if (_pcmCurrentSound[i].Index != 0 && _intf.PcmEffectPlaying(i + 0x3f) == 0)
                {
                    _intf.PcmChanOff(i + 0x3f);
                    _pcmCurrentSound[i].Index = 0;
                }
            }

            for (int i = 0; i < 9; i++)
            {
                var p = _pcmCurrentSound[i];
                int at = 8 + i * 13;
                BitConverter.TryWriteBytes(data.AsSpan(at), (short)p.Index);
                BitConverter.TryWriteBytes(data.AsSpan(at + 2), (short)p.Chan);
                (data[at + 4], data[at + 5], data[at + 6]) = ((byte)p.Note, (byte)p.Velo, (byte)p.Pan);
                (data[at + 7], data[at + 8]) = ((byte)(p.Paused ? 1 : 0), (byte)(p.Looping ? 1 : 0));
                BitConverter.TryWriteBytes(data.AsSpan(at + 9), p.Priority);
            }

            return data;
        }
    }

    /// <summary>Takes the player's state from a save, for <see cref="RestoreAfterLoad"/>; a save without it leaves the player as it is.</summary>
    public void LoadState(byte[] data)
    {
        if (data.Length < SaveStateSize)
        {
            return;
        }

        lock (_sync)
        {
            (_cdaCurrentSoundTemp, _cdaNumLoopsTemp) = (data[0], data[1]);
            CdVolume = (data[2], data[3]);
            (_eupCurrentSound, _eupLooping, _eupVolLeft, _eupVolRight) = (data[4], data[5] != 0, data[6], data[7]);
            for (int i = 0; i < 9; i++)
            {
                var p = _pcmCurrentSound[i];
                int at = 8 + i * 13;
                p.Index = BitConverter.ToUInt16(data, at);
                p.Chan = BitConverter.ToInt16(data, at + 2);
                (p.Note, p.Velo, p.Pan) = (data[at + 4], data[at + 5], data[at + 6]);
                (p.Paused, p.Looping) = (data[at + 7] != 0, data[at + 8] != 0);
                p.Priority = BitConverter.ToUInt32(data, at + 9);
            }
        }
    }

    /// <summary>
    /// After a load, the sounds the save had going start again (<c>Player_Towns_v1::restoreAfterLoad</c>
    /// and <c>Player_Towns::restoreAfterLoad</c>): the CD's volume, the CD sound at the volume it
    /// had, the Euphony sound at its volume, and each sampled sound once, as it was started.
    /// </summary>
    /// <param name="resource">A sound's resource, from its 6-byte header, or null.</param>
    public void RestoreAfterLoad(Func<int, byte[]?> resource)
    {
        lock (_sync)
        {
            SetVolumeCd(CdVolume.Left, CdVolume.Right);
            if (_cdaCurrentSoundTemp != 0 && resource(_cdaCurrentSoundTemp) is { Length: >= 6 + 24 } cda && cda[13] == 2)
            {
                PlayCdaTrack(_cdaCurrentSoundTemp, cda.AsSpan(6), skipTrackVelo: true);
                CurrentCdSound = _cdaCurrentSoundTemp;
                _cdaNumLoops = _cdaNumLoopsTemp;
            }

            if (_eupCurrentSound != 0 && resource(_eupCurrentSound) is { Length: > 14 } eup && eup[13] == 1)
            {
                SetSoundVolume(_eupCurrentSound, _eupVolLeft, _eupVolRight);
                PlayEuphonyTrack(_eupCurrentSound, eup);
            }

            var restored = new List<int>();
            for (int i = 1; i < 9; i++)
            {
                var p = _pcmCurrentSound[i];
                if (p.Index == 0 || p.Index == 0xffff || restored.Contains(p.Index))
                {
                    continue;
                }

                restored.Add(p.Index);
                if (resource(p.Index) is { Length: > 14 } pcm && pcm[13] == 0)
                {
                    PlayPcmTrack(p.Index, pcm, p.Velo, p.Pan, p.Note, (int)p.Priority);
                }
            }
        }
    }

    /// <summary>
    /// Mirrors <c>playCdaTrack</c>: the sound's volume, or the scripts' for it, unless restoring
    /// after a load; a sound already playing on the CD goes on.
    /// </summary>
    private void PlayCdaTrack(int sound, ReadOnlySpan<byte> data, bool skipTrackVelo = false)
    {
        if (sound == 0)
        {
            return;
        }

        var o = sound < NumSoundMax ? _overrides[sound] : default;
        if (!skipTrackVelo)
        {
            SetVolumeCd(o.Left + o.Right != 0 ? o.Left : data[8], o.Left + o.Right != 0 ? o.Right : data[9]);
        }

        if (sound == CurrentCdSound && _cd.IsPlaying)
        {
            return;
        }

        var p = data[16..];
        int loops = p[1];
        _cdaNumLoops = loops;
        int start = ((p[2] * 60) + p[3]) * CdPlayer.FramesPerSecond + p[4];
        int end = ((p[5] * 60) + p[6]) * CdPlayer.FramesPerSecond + p[7];
        _playCd(p[0], start, end <= start ? 0 : end - start, loops == 0xFF ? -1 : loops);
        CurrentCdSound = sound;
    }

    /// <summary>A PCM channel's sound, which of its samples, and how it was started (<c>PcmCurrentSound</c>).</summary>
    private sealed class PcmCurrentSound
    {
        public int Index;
        public int Chan;
        public int Note;
        public int Velo;
        public int Pan;
        public bool Paused;
        public bool Looping;
        public uint Priority;
    }
}
