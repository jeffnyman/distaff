using Distaff.Scumm.Sound.Opl;

namespace Distaff.Scumm.Sound;

/// <summary>
/// The AdLib music and sound player of Loom and Indiana Jones 3: one OPL2 chip, ticked 472 times
/// a second. A sound whose third byte is 0x80 is music, a MIDI-like track of note events over
/// up to 16 instruments, rhythm instruments among them; any other is an effect of up to three
/// channels of instrument and note definitions with their own envelopes. Music takes channels
/// before effects. Mirrors ScummVM's <c>Player_AD</c>, with the timer callbacks of
/// <c>EmulatedChip::readBuffer</c>.
/// </summary>
/// <remarks>
/// ScummVM scales each operator's level by the music or effects volume of its configuration
/// (<c>setupVolume</c>); here both are the full 255, and the host's master volume applies to the
/// output instead. The saved state of the player (<c>saveLoadWithSerializer</c>) is not kept.
/// </remarks>
public sealed class PlayerAd : MusicPlayer
{
    private const int CallbackFrequency = 472, FixpShift = 16, MaxVolume = 255;

    private const int NoteStatePreInit = -1, NoteStateAttack = 0, NoteStateSustain = 2, NoteStateRelease = 3, NoteStateOff = 4;

    private const int ChannelStateOff = 0, ChannelStateParse = 1, ChannelStatePlay = 2;

    private static readonly int[] OperatorOffsetToChannel = [0, 1, 2, 0, 1, 2, -1, -1, 3, 4, 5, 3, 4, 5, -1, -1, 6, 7, 8, 6, 7, 8];
    private static readonly int[] OperatorOffsetTable = [0, 3, 1, 4, 2, 5, 8, 11, 9, 12, 10, 13, 16, 19, 17, 20, 18, 21];
    private static readonly int[] NoteFrequencies = [0x200, 0x21E, 0x23F, 0x261, 0x285, 0x2AB, 0x2D4, 0x300, 0x32E, 0x35E, 0x390, 0x3C7];
    private static readonly int[] MdvdrTable = [0x00, 0x10, 0x08, 0x04, 0x02, 0x01];
    private static readonly int[] RhythmOperatorTable = [0x00, 0x00, 0x14, 0x12, 0x15, 0x11];
    private static readonly int[] RhythmChannelTable = [0x00, 0x00, 0x07, 0x08, 0x08, 0x07];
    private static readonly int[] NoteBiasTable = [0x00, 0x00, 0x3F, 0x00, 0x3F, 0x00, 0x00];
    private static readonly int[] NumStepsTable = [1, 4, 6, 8, 10, 14, 18, 24, 36, 64, 100, 160, 240, 340, 600, 1200];
    private static readonly int[] NoteAdjustScaleTable = [255, 7, 63, 15, 63, 15, 63];
    private static readonly int[] NoteAdjustTable = [0, 4369, 8738, 13107, 17476, 21845, 26214, 30583, 34952, 39321, 43690, 48059, 52428, 56797, 61166, 65535];
    private static readonly bool[] UseOperatorTable = [false, false, true, true, true, true, false];
    private static readonly int[] ChannelOffsetTable = [0, 1, 2, 3, 4, 5, 6, 7, 8, 8, 7];
    private static readonly int[] ChannelOperatorOffsetTable = [0, 0, 1, 1, 0, 0, 0];
    private static readonly int[] BaseRegisterTable = [0xA0, 0xC0, 0x40, 0x20, 0x40, 0x20, 0x00];
    private static readonly int[] RegisterMaskTable = [0xFF, 0x0E, 0x3F, 0x0F, 0x3F, 0x0F, 0x00];
    private static readonly int[] RegisterShiftTable = [0, 1, 0, 0, 0, 0, 0];

    private readonly object _sync = new();
    private readonly IOpl _opl;
    private readonly int _samplesPerTick;
    private readonly byte[] _registers = new byte[256];
    private readonly HardwareChannel[] _hwChannels = [new(), new(), new(), new(), new(), new(), new(), new(), new()];
    private readonly VoiceChannel[] _voiceChannels = [new(), new(), new(), new(), new(), new(), new(), new(), new()];
    private readonly SfxSlot[] _sfx = [new(), new(), new()];
    private readonly int[] _instrumentOffset = new int[16];
    private int _musicVolume = MaxVolume, _sfxVolume = MaxVolume;

    private int _nextTick;
    private int _numHWChannels = 9;
    private int _musicResource = -1;
    private byte[] _musicData = [];
    private int _engineMusicTimer;
    private int _timerLimit, _musicTicks, _musicTimer, _internalMusicTimer;
    private bool _loopFlag;
    private int _musicLoopStart;
    private int _mdvdrState;
    private int _curOffset;
    private int _nextEventTimer;

    /// <summary>Whether the music is being wound on to where a save left it, its notes kept silent meanwhile (<c>_isSeeking</c>).</summary>
    private bool _isSeeking;
    private int _sfxTimer = 4;
    private byte _rndSeed = 1;

    public PlayerAd(int sampleRate, IOpl opl)
        : base(sampleRate)
    {
        _opl = opl;
        WriteReg(0x01, 0x00);
        WriteReg(0xBD, 0x00);
        WriteReg(0x08, 0x00);
        WriteReg(0x01, 0x20);

        // EmulatedChip::setCallbackFrequency
        _samplesPerTick = ((sampleRate / CallbackFrequency) << FixpShift) + ((sampleRate % CallbackFrequency) << FixpShift) / CallbackFrequency;
    }

    /// <summary>The music's timer, counted every 121 of its ticks. Mirrors <c>getMusicTimer</c>.</summary>
    public override int MusicTimer
    {
        get
        {
            lock (_sync)
            {
                return _engineMusicTimer;
            }
        }
    }

    /// <summary>Music replaces the music playing; an effect takes a free slot or the lowest of no higher priority. Mirrors <c>startSound</c>.</summary>
    public override void StartSound(int number, byte[] data)
    {
        lock (_sync)
        {
            if (data.Length > 2 && data[2] == 0x80)
            {
                StopMusic();
                _musicResource = number;
                _musicData = data;
                StartMusic();
                return;
            }

            if (AllocateSfxSlot(data[0]) is not { } sfx)
            {
                return;
            }

            sfx.Resource = number;
            sfx.Priority = data[0];
            if (!StartSfx(sfx, data))
            {
                sfx.Resource = -1;
                foreach (var channel in sfx.Channels)
                {
                    channel.State = ChannelStateOff;
                    if (channel.HardwareChannel != -1)
                    {
                        FreeHWChannel(channel.HardwareChannel);
                        channel.HardwareChannel = -1;
                    }
                }
            }
        }
    }

    public override void StopSound(int number)
    {
        lock (_sync)
        {
            if (number == _musicResource)
            {
                StopMusic();
                return;
            }

            foreach (var sfx in _sfx)
            {
                if (sfx.Resource == number)
                {
                    StopSfx(sfx);
                }
            }
        }
    }

    public override void StopAllSounds()
    {
        lock (_sync)
        {
            StopMusic();
            foreach (var sfx in _sfx)
            {
                StopSfx(sfx);
            }
        }
    }

    /// <summary>Mirrors <c>getSoundStatus</c>.</summary>
    public override bool IsPlaying(int number)
    {
        lock (_sync)
        {
            return number == _musicResource || _sfx.Any(s => s.Resource == number);
        }
    }

    /// <summary>The chip's output, the timer callback run between its samples as often as it falls due. Mirrors <c>EmulatedChip::readBuffer</c>.</summary>
    protected override void RenderMono(Span<short> buffer)
    {
        lock (_sync)
        {
            while (buffer.Length > 0)
            {
                int step = Math.Min(buffer.Length, _nextTick >> FixpShift);
                _opl.Generate(buffer[..step]);
                _nextTick -= step << FixpShift;
                if ((_nextTick >> FixpShift) == 0)
                {
                    OnTimer();
                    _nextTick += _samplesPerTick;
                }

                buffer = buffer[step..];
            }
        }
    }

    /// <summary>Mirrors <c>onTimer</c>.</summary>
    private void OnTimer()
    {
        if (_curOffset != 0)
        {
            UpdateMusic();
        }

        UpdateSfx();
    }

    /// <summary>A free hardware channel, or the one of the lowest priority no higher than this, not the owner's own. Mirrors <c>allocateHWChannel</c>.</summary>
    private int AllocateHWChannel(int priority, SfxSlot? owner = null)
    {
        int channel = -1;
        int minPrio = priority;
        for (int i = 0; i < _numHWChannels; ++i)
        {
            if (!_hwChannels[i].Allocated)
            {
                channel = i;
                break;
            }

            if (_hwChannels[i].Priority <= minPrio && _hwChannels[i].SfxOwner != owner)
            {
                minPrio = _hwChannels[i].Priority;
                channel = i;
            }
        }

        if (channel != -1)
        {
            if (_hwChannels[channel].Allocated && _hwChannels[channel].SfxOwner is { } previous)
            {
                StopSfx(previous);
            }

            _hwChannels[channel].Allocated = true;
            _hwChannels[channel].Priority = priority;
            _hwChannels[channel].SfxOwner = owner;
        }

        return channel;
    }

    private void FreeHWChannel(int channel)
    {
        _hwChannels[channel].Allocated = false;
        _hwChannels[channel].SfxOwner = null;
    }

    private void LimitHWChannels(int newCount)
    {
        for (int i = newCount; i < _hwChannels.Length; ++i)
        {
            if (_hwChannels[i].Allocated)
            {
                FreeHWChannel(i);
            }
        }

        _numHWChannels = newCount;
    }

    /// <summary>
    /// Writes a register, keeping a copy: an operator's level is scaled by the music or effects
    /// volume, and a key on is dropped when that volume is 0. Mirrors <c>writeReg</c>, which also
    /// drops key ons while the music seeks after a load (<c>musicSeekTo</c>), not ported.
    /// </summary>
    private void WriteReg(int r, int v)
    {
        if (r is >= 0 and < 256)
        {
            _registers[r] = (byte)v;
        }

        if (r is >= 0x40 and <= 0x55)
        {
            int operatorOffset = r - 0x40;
            int channel = OperatorOffsetToChannel[operatorOffset];
            if (channel != -1)
            {
                bool twoOPOutput = (ReadReg(0xC0 + channel) & 0x01) != 0;
                int scale = MaxVolume;
                if (twoOPOutput || operatorOffset == OperatorOffsetTable[channel * 2 + 1])
                {
                    scale = _hwChannels[channel].SfxOwner is not null ? _sfxVolume : _musicVolume;
                }

                int vol = 0x3F - (v & 0x3F);
                vol = vol * scale / MaxVolume;
                v &= 0xC0;
                v |= 0x3F - vol;
            }
        }

        if (r is >= 0xB0 and <= 0xB8)
        {
            int channel = r - 0xB0;
            bool mute = _hwChannels[channel].SfxOwner is not null ? _sfxVolume == 0 : _musicVolume == 0 || _isSeeking;
            if (mute)
            {
                v &= ~0x20;
            }
        }

        _opl.WriteRegister(r, v & 0xFF);
    }

    /// <summary>
    /// The music and effects volumes, each to 255, and every operator's level and every note
    /// written again at them. Mirrors <c>Player_AD::setupVolume</c>, which <c>setMusicVolume</c>
    /// calls with the settings' music and effects volumes.
    /// </summary>
    public override void SetVolumes(int music, int sfx)
    {
        lock (_sync)
        {
            _musicVolume = Math.Clamp(music, 0, MaxVolume);
            _sfxVolume = Math.Clamp(sfx, 0, MaxVolume);
            foreach (int offset in OperatorOffsetTable)
            {
                WriteReg(0x40 + offset, ReadReg(0x40 + offset));
            }

            for (int i = 0; i < _hwChannels.Length; i++)
            {
                WriteReg(0xB0 + i, ReadReg(0xB0 + i));
            }
        }
    }

    /// <summary>
    /// What a save keeps of the player: the music and the three effects playing (-1 for none),
    /// then the music's timers and where it is. Mirrors <c>Player_AD::saveLoadWithSerializer</c>
    /// from save version 96 on, its numbers little-endian.
    /// </summary>
    public byte[] SaveState()
    {
        lock (_sync)
        {
            var data = new byte[SaveStateSize];
            int[] values = [_musicResource, _sfx[0].Resource, _sfx[1].Resource, _sfx[2].Resource, _engineMusicTimer, _musicTimer, _internalMusicTimer, _curOffset, _nextEventTimer];
            for (int i = 0; i < values.Length; i++)
            {
                BitConverter.TryWriteBytes(data.AsSpan(i * 4, 4), values[i]);
            }

            return data;
        }
    }

    /// <summary>The bytes <see cref="SaveState"/> writes.</summary>
    public const int SaveStateSize = 9 * 4;

    /// <summary>
    /// Plays on from a save's state: the music starts again and is wound on, silent, to where
    /// the save left it, with its timers as they were; then the effects start again. Mirrors the
    /// loading half of <c>Player_AD::saveLoadWithSerializer</c> and <c>musicSeekTo</c>. A save
    /// without the state, or with another player's (an MT-32 save's iMuse state), leaves the player
    /// as it is; ScummVM would read the other's as its own, and play from what it makes of it.
    /// </summary>
    /// <param name="data">What <see cref="SaveState"/> wrote.</param>
    /// <param name="resource">A sound's data as <see cref="StartSound"/> takes it, or null.</param>
    public void LoadState(byte[] data, Func<int, byte[]?> resource)
    {
        if (data.Length != SaveStateSize)
        {
            return;
        }

        int Read(int i) => BitConverter.ToInt32(data, i * 4);
        lock (_sync)
        {
            int music = Read(0);
            if (music != -1 && resource(music) is { } musicData)
            {
                StartSound(music, musicData);
            }

            int startOffset = _curOffset;
            (_engineMusicTimer, _musicTimer, _internalMusicTimer, _nextEventTimer) = (Read(4), Read(5), Read(6), Read(8));
            if (_musicData is not null && _musicResource != -1)
            {
                MusicSeekTo(Read(7));
            }
            else
            {
                _curOffset = startOffset;
            }

            for (int i = 1; i < 4; i++)
            {
                if (Read(i) != -1 && resource(Read(i)) is { } sfxData)
                {
                    StartSound(Read(i), sfxData);
                }
            }
        }
    }

    /// <summary>
    /// Winds the music on to a place in it, its commands run and its notes kept silent; then the
    /// notes that are on sound again. An end of the track on the way stops the winding. Mirrors
    /// <c>musicSeekTo</c>.
    /// </summary>
    private void MusicSeekTo(int position)
    {
        _isSeeking = true;
        while (_curOffset != position)
        {
            if (ParseCommand() || _musicData is null || _musicResource == -1)
            {
                break;
            }

            ParseVLQ();
        }

        _isSeeking = false;
        for (int i = 0; i < _voiceChannels.Length; i++)
        {
            if (_voiceChannels[i].LastEvent != 0)
            {
                WriteReg(0xB0 + i, ReadReg(0xB0 + i));
            }
        }
    }

    private byte ReadReg(int r) => r is >= 0 and < 256 ? _registers[r] : (byte)0;

    /// <summary>Mirrors <c>setupChannel</c>: the connection, then both operators.</summary>
    private void SetupChannel(int channel, byte[] data, int instrOffset)
    {
        instrOffset += 2;
        WriteReg(0xC0 + channel, data[instrOffset++]);
        SetupOperator(OperatorOffsetTable[channel * 2 + 0], data, ref instrOffset);
        SetupOperator(OperatorOffsetTable[channel * 2 + 1], data, ref instrOffset);
    }

    private void SetupOperator(int opr, byte[] data, ref int instrOffset)
    {
        WriteReg(0x20 + opr, data[instrOffset++]);
        WriteReg(0x40 + opr, data[instrOffset++]);
        WriteReg(0x60 + opr, data[instrOffset++]);
        WriteReg(0x80 + opr, data[instrOffset++]);
        WriteReg(0xE0 + opr, data[instrOffset++]);
    }

    /// <summary>The instruments, rhythm mode when one is a drum, the tempo and the loop. Mirrors <c>startMusic</c> for Loom.</summary>
    private void StartMusic()
    {
        Array.Clear(_instrumentOffset);
        bool hasRhythmData = false;
        int instruments = _musicData[10];
        for (int i = 0; i < instruments; ++i)
        {
            int instrIndex = _musicData[11 + i] - 1;
            if (instrIndex is >= 0 and < 16)
            {
                _instrumentOffset[instrIndex] = i * 16 + 16 + 3;
                hasRhythmData |= _musicData[_instrumentOffset[instrIndex] + 13] != 0;
            }
        }

        if (hasRhythmData)
        {
            _mdvdrState = 0x20;
            LimitHWChannels(6);
        }
        else
        {
            _mdvdrState = 0;
            LimitHWChannels(9);
        }

        _curOffset = 0x93;
        _nextEventTimer = 40;
        _engineMusicTimer = 0;
        _internalMusicTimer = 0;
        _musicTimer = 0;
        WriteReg(0xBD, _mdvdrState);

        _timerLimit = 473;
        _musicTicks = _musicData[3] * 2;
        _loopFlag = _musicData[4] == 0;
        _musicLoopStart = _curOffset + (_musicData[5] | (_musicData[6] << 8));
    }

    private void StopMusic()
    {
        if (_musicResource == -1)
        {
            return;
        }

        _musicResource = -1;
        _curOffset = 0;
        for (int i = 0; i < _voiceChannels.Length; ++i)
        {
            if (_voiceChannels[i].LastEvent != 0)
            {
                NoteOff(i);
            }
        }

        WriteReg(0xBD, 0x00);
        LimitHWChannels(9);
    }

    /// <summary>The music's clock, and the events that fall due. Mirrors <c>updateMusic</c> for Loom.</summary>
    private void UpdateMusic()
    {
        _musicTimer += _musicTicks;
        if (_musicTimer < _timerLimit)
        {
            return;
        }

        _musicTimer -= _timerLimit;
        ++_internalMusicTimer;
        if (_internalMusicTimer > 120)
        {
            _internalMusicTimer = 0;
            ++_engineMusicTimer;
        }

        if (--_nextEventTimer != 0)
        {
            return;
        }

        while (true)
        {
            if (ParseCommand())
            {
                if (_musicResource == -1)
                {
                    return;
                }

                continue;
            }

            if (_musicData[_curOffset] != 0)
            {
                break;
            }

            ++_curOffset;
        }

        _nextEventTimer = ParseVLQ() >> 2;
        if (_nextEventTimer == 0)
        {
            _nextEventTimer = 1;
        }
    }

    /// <summary>One event: end of track, a tempo change, note on or note off. True at the end of the track. Mirrors <c>parseCommand</c>.</summary>
    private bool ParseCommand()
    {
        int command = _musicData[_curOffset++];
        if (command == 0xFF)
        {
            command = _musicData[_curOffset++];
            if (command == 47)
            {
                if (_loopFlag)
                {
                    _curOffset = _musicLoopStart;
                    _nextEventTimer = 0;
                }
                else
                {
                    StopMusic();
                }

                return true;
            }

            if (command == 88)
            {
                _curOffset += 5;
            }
            else if (command == 81)
            {
                int timing = _musicData[_curOffset + 2] | (_musicData[_curOffset + 1] << 8);
                _musicTicks = 0x73000 / timing;
                command = _musicData[_curOffset++];
                _curOffset += command;
            }
            else
            {
                command = _musicData[_curOffset++];
                _curOffset += command;
            }

            return false;
        }

        if (command >= 0x90)
        {
            command -= 0x90;
            int instrOffset = _instrumentOffset[command & 0x0F];
            if (instrOffset != 0)
            {
                if (_musicData[instrOffset + 13] != 0)
                {
                    SetupRhythm(_musicData[instrOffset + 13], instrOffset);
                }
                else
                {
                    int channel = AllocateHWChannel(256);
                    if (channel != -1)
                    {
                        SetupChannel(channel, _musicData, instrOffset);
                        _voiceChannels[channel].LastEvent = command + 0x90;
                        _voiceChannels[channel].Frequency = _musicData[_curOffset];
                        SetupFrequency(channel, (sbyte)_musicData[_curOffset]);
                    }
                }
            }
        }
        else
        {
            int note = _musicData[_curOffset];
            command += 0x10;
            int channel = 0xFF;
            for (int i = 0; i < _voiceChannels.Length; ++i)
            {
                if (_voiceChannels[i].Frequency == note && _voiceChannels[i].LastEvent == command)
                {
                    channel = i;
                    break;
                }
            }

            if (channel != 0xFF)
            {
                NoteOff(channel);
            }
            else
            {
                command -= 0x90;
                int instrOffset = command is >= 0 and < 16 ? _instrumentOffset[command] : 0;
                if (instrOffset != 0 && _musicData[instrOffset + 13] != 0)
                {
                    int rhythmInstr = _musicData[instrOffset + 13];
                    if (rhythmInstr < 6)
                    {
                        _mdvdrState &= MdvdrTable[rhythmInstr] ^ 0xFF;
                        WriteReg(0xBD, _mdvdrState);
                    }
                }
            }
        }

        _curOffset += 2;
        return false;
    }

    private int ParseVLQ()
    {
        int vlq = _musicData[_curOffset++];
        if ((vlq & 0x80) != 0)
        {
            vlq -= 0x80;
            vlq <<= 7;
            vlq |= _musicData[_curOffset++];
        }

        return vlq;
    }

    private void NoteOff(int channel)
    {
        WriteReg(0xB0 + channel, _voiceChannels[channel].B0Reg & 0xDF);
        FreeVoiceChannel(channel);
    }

    /// <summary>A note as frequency and octave, keyed on. Mirrors <c>setupFrequency</c>, whose note is a signed byte.</summary>
    private void SetupFrequency(int channel, sbyte frequency)
    {
        frequency = (sbyte)(frequency - 31);
        if (frequency < 0)
        {
            frequency = 0;
        }

        int octave = 0;
        while (frequency >= 12)
        {
            frequency -= 12;
            ++octave;
        }

        int noteFrequency = NoteFrequencies[frequency];
        octave <<= 2;
        octave |= noteFrequency >> 8;
        octave |= 0x20;
        WriteReg(0xA0 + channel, noteFrequency & 0xFF);
        _voiceChannels[channel].B0Reg = octave;
        WriteReg(0xB0 + channel, octave);
    }

    /// <summary>Mirrors <c>setupRhythm</c>: the bass drum on channel 6, the others on an operator of 7 or 8.</summary>
    private void SetupRhythm(int rhythmInstr, int instrOffset)
    {
        if (rhythmInstr == 1)
        {
            SetupChannel(6, _musicData, instrOffset);
            WriteReg(0xA6, _musicData[instrOffset++]);
            WriteReg(0xB6, _musicData[instrOffset] & 0xDF);
            _mdvdrState |= 0x10;
            WriteReg(0xBD, _mdvdrState);
        }
        else if (rhythmInstr < 6)
        {
            int secondOperatorOffset = instrOffset + 8;
            SetupOperator(RhythmOperatorTable[rhythmInstr], _musicData, ref secondOperatorOffset);
            WriteReg(0xA0 + RhythmChannelTable[rhythmInstr], _musicData[instrOffset++]);
            WriteReg(0xB0 + RhythmChannelTable[rhythmInstr], _musicData[instrOffset++] & 0xDF);
            WriteReg(0xC0 + RhythmChannelTable[rhythmInstr], _musicData[instrOffset]);
            _mdvdrState |= MdvdrTable[rhythmInstr];
            WriteReg(0xBD, _mdvdrState);
        }
    }

    private void FreeVoiceChannel(int channel)
    {
        FreeHWChannel(channel);
        _voiceChannels[channel].LastEvent = 0;
        _voiceChannels[channel].B0Reg = 0;
        _voiceChannels[channel].Frequency = 0;
    }

    /// <summary>A free effect slot, or the one of the lowest priority no higher than this, stopped. Mirrors <c>allocateSfxSlot</c>.</summary>
    private SfxSlot? AllocateSfxSlot(int priority)
    {
        SfxSlot? sfx = null;
        int minPrio = priority;
        foreach (var slot in _sfx)
        {
            if (slot.Resource == -1)
            {
                return slot;
            }

            if (slot.Priority <= minPrio)
            {
                minPrio = slot.Priority;
                sfx = slot;
            }
        }

        if (sfx is not null)
        {
            StopSfx(sfx);
        }

        return sfx;
    }

    /// <summary>The first channel starts after the header; each start-of-channel byte begins another. Mirrors <c>startSfx</c>.</summary>
    private bool StartSfx(SfxSlot sfx, byte[] resource)
    {
        WriteReg(0xBD, 0x00);
        sfx.Data = resource;
        foreach (var channel in sfx.Channels)
        {
            channel.State = ChannelStateOff;
        }

        var first = sfx.Channels[0];
        first.HardwareChannel = AllocateHWChannel(sfx.Priority, sfx);
        if (first.HardwareChannel == -1)
        {
            return false;
        }

        first.CurrentOffset = first.StartOffset = 2;
        first.State = ChannelStateParse;

        int curChannel = 1;
        int bufferPosition = 2;
        int command;
        while ((command = resource[bufferPosition]) != 0xFF)
        {
            switch (command)
            {
                case 1:
                    bufferPosition += 15;
                    break;
                case 2:
                    bufferPosition += 11;
                    break;
                case 0x80:
                    bufferPosition += 1;
                    break;
                default:
                    bufferPosition += 1;
                    if (curChannel >= 3)
                    {
                        throw new InvalidDataException($"AdLib sound {sfx.Resource} uses more than 3 channels.");
                    }

                    var channel = sfx.Channels[curChannel];
                    channel.HardwareChannel = AllocateHWChannel(sfx.Priority, sfx);
                    if (channel.HardwareChannel == -1)
                    {
                        return false;
                    }

                    channel.CurrentOffset = channel.StartOffset = bufferPosition;
                    channel.State = ChannelStateParse;
                    ++curChannel;
                    break;
            }
        }

        return true;
    }

    private void StopSfx(SfxSlot sfx)
    {
        if (sfx.Resource == -1)
        {
            return;
        }

        foreach (var channel in sfx.Channels)
        {
            if (channel.State != ChannelStateOff)
            {
                ClearChannel(channel);
                channel.State = ChannelStateOff;
            }

            if (channel.HardwareChannel != -1)
            {
                FreeHWChannel(channel.HardwareChannel);
                channel.HardwareChannel = -1;
            }
        }

        sfx.Resource = -1;
    }

    /// <summary>Every fourth tick, each effect's channels move on; an effect with none left stops. Mirrors <c>updateSfx</c>.</summary>
    private void UpdateSfx()
    {
        if (--_sfxTimer != 0)
        {
            return;
        }

        _sfxTimer = 4;
        foreach (var sfx in _sfx)
        {
            if (sfx.Resource == -1)
            {
                continue;
            }

            bool hasActiveChannel = false;
            foreach (var channel in sfx.Channels)
            {
                if (channel.State != ChannelStateOff)
                {
                    hasActiveChannel = true;
                    if (channel.State == ChannelStateParse)
                    {
                        ParseSlot(sfx, channel);
                    }
                    else
                    {
                        UpdateSlot(sfx, channel);
                    }
                }
            }

            if (!hasActiveChannel)
            {
                StopSfx(sfx);
            }
        }
    }

    private void ClearChannel(Channel channel)
    {
        WriteReg(0xA0 + channel.HardwareChannel, 0x00);
        WriteReg(0xB0 + channel.HardwareChannel, 0x00);
    }

    /// <summary>Instrument definitions and loops until a note definition starts playing, or another channel's start ends this one. Mirrors <c>parseSlot</c>.</summary>
    private void ParseSlot(SfxSlot sfx, Channel channel)
    {
        var data = sfx.Data;
        while (true)
        {
            int curOffset = channel.CurrentOffset;
            switch (data[curOffset])
            {
                case 1:
                    ++curOffset;
                    channel.InstrumentData[0] = data[curOffset + 0];
                    channel.InstrumentData[1] = data[curOffset + 2];
                    channel.InstrumentData[2] = data[curOffset + 9];
                    channel.InstrumentData[3] = data[curOffset + 8];
                    channel.InstrumentData[4] = data[curOffset + 4];
                    channel.InstrumentData[5] = data[curOffset + 3];
                    channel.InstrumentData[6] = 0;
                    SetupChannel(channel.HardwareChannel, data, curOffset);
                    WriteReg(0xA0 + channel.HardwareChannel, data[curOffset + 0]);
                    WriteReg(0xB0 + channel.HardwareChannel, data[curOffset + 1] & 0xDF);
                    channel.CurrentOffset += 15;
                    break;
                case 2:
                    ++curOffset;
                    channel.State = ChannelStatePlay;
                    NoteOffOn(channel.HardwareChannel);
                    ParseNote(channel.Notes[0], channel, data, curOffset + 0);
                    ParseNote(channel.Notes[1], channel, data, curOffset + 5);
                    return;
                case 0x80:
                    channel.CurrentOffset = channel.StartOffset;
                    break;
                default:
                    ClearChannel(channel);
                    channel.State = ChannelStateOff;
                    return;
            }
        }
    }

    /// <summary>Each of the two notes moves along its envelope, or waits out its sustain, and the next definition comes when it ends. Mirrors <c>updateSlot</c>.</summary>
    private void UpdateSlot(SfxSlot sfx, Channel channel)
    {
        var data = sfx.Data;
        int curOffset = channel.CurrentOffset + 1;
        for (int num = 0; num <= 1; ++num, curOffset += 5)
        {
            if ((data[curOffset] & 0x80) == 0)
            {
                continue;
            }

            var note = channel.Notes[num];
            bool updateNote = false;
            if (note.State == NoteStateSustain)
            {
                if (--note.SustainTimer == 0)
                {
                    updateNote = true;
                }
            }
            else
            {
                updateNote = ProcessNoteEnvelope(note);
                WriteRegisterSpecial(channel.HardwareChannel, (byte)(note.Bias != 0 ? note.Bias - note.InstrumentValue : note.InstrumentValue), data[curOffset] & 0x07);
            }

            if (updateNote)
            {
                if (ProcessNote(note, channel, data, curOffset))
                {
                    if ((data[curOffset] & 0x08) == 0)
                    {
                        channel.CurrentOffset += 11;
                        channel.State = ChannelStateParse;
                        continue;
                    }

                    if ((data[curOffset] & 0x10) != 0)
                    {
                        NoteOffOn(channel.HardwareChannel);
                    }

                    note.State = NoteStatePreInit;
                    ProcessNote(note, channel, data, curOffset);
                }
            }

            if ((data[curOffset] & 0x20) != 0 && --note.PlayTime == 0)
            {
                channel.CurrentOffset += 11;
                channel.State = ChannelStateParse;
            }
        }
    }

    private void ParseNote(Note note, Channel channel, byte[] data, int offset)
    {
        if ((data[offset] & 0x80) == 0)
        {
            return;
        }

        note.State = NoteStatePreInit;
        ProcessNote(note, channel, data, offset);
        note.PlayTime = 0;
        if ((data[offset] & 0x20) != 0)
        {
            note.PlayTime = (data[offset + 4] >> 4) * 118;
            note.PlayTime += (data[offset + 4] & 0x0F) * 8;
        }
    }

    /// <summary>The note's next state: attack, decay, sustain (a fixed or random time) and release, then off. Mirrors <c>processNote</c>.</summary>
    private bool ProcessNote(Note note, Channel channel, byte[] data, int offset)
    {
        if (++note.State == NoteStateOff)
        {
            return true;
        }

        int instrumentDataOffset = data[offset] & 0x07;
        note.Bias = NoteBiasTable[instrumentDataOffset];
        byte instrumentDataValue = note.State == NoteStateAttack ? channel.InstrumentData[instrumentDataOffset] : (byte)0;
        byte noteInstrumentValue = ReadRegisterSpecial(channel.HardwareChannel, instrumentDataValue, instrumentDataOffset);
        if (note.Bias != 0)
        {
            noteInstrumentValue = (byte)(note.Bias - noteInstrumentValue);
        }

        note.InstrumentValue = noteInstrumentValue;
        if (note.State == NoteStateSustain)
        {
            note.SustainTimer = NumStepsTable[data[offset + 3] >> 4];
            if ((data[offset] & 0x40) != 0)
            {
                note.SustainTimer = (((GetRnd() << 8) * note.SustainTimer) >> 16) + 1;
            }
        }
        else
        {
            int timer1, timer2;
            if (note.State == NoteStateRelease)
            {
                timer1 = data[offset + 3] & 0x0F;
                timer2 = 0;
            }
            else
            {
                timer1 = data[offset + note.State + 1] >> 4;
                timer2 = data[offset + note.State + 1] & 0x0F;
            }

            int adjustValue = ((NoteAdjustTable[timer2] * NoteAdjustScaleTable[instrumentDataOffset]) >> 16) - noteInstrumentValue;
            SetupNoteEnvelopeState(note, NumStepsTable[timer1], adjustValue);
        }

        return false;
    }

    private void NoteOffOn(int channel)
    {
        byte regValue = ReadReg(0xB0 | channel);
        WriteReg(0xB0 | channel, regValue & 0xDF);
        WriteReg(0xB0 | channel, regValue | 0x20);
    }

    /// <summary>Sets one of the instrument's values, by its place in the channel's or an operator's register. Mirrors <c>writeRegisterSpecial</c>.</summary>
    private void WriteRegisterSpecial(int channel, byte value, int offset)
    {
        if (offset == 6)
        {
            return;
        }

        int regNum = (UseOperatorTable[offset] ? OperatorOffsetTable[ChannelOperatorOffsetTable[offset] + channel * 2] : ChannelOffsetTable[channel]) + BaseRegisterTable[offset];
        regNum &= 0xFF;
        int regValue = ReadReg(regNum) & ~RegisterMaskTable[offset] & 0xFF;
        regValue |= (value << RegisterShiftTable[offset]) & 0xFF;
        WriteReg(regNum, regValue);
    }

    private byte ReadRegisterSpecial(int channel, byte defaultValue, int offset)
    {
        if (offset == 6)
        {
            return 0;
        }

        int regNum = (UseOperatorTable[offset] ? OperatorOffsetTable[ChannelOperatorOffsetTable[offset] + channel * 2] : ChannelOffsetTable[channel]) + BaseRegisterTable[offset];
        regNum &= 0xFF;
        int regValue = defaultValue != 0 ? defaultValue : ReadReg(regNum);
        regValue &= RegisterMaskTable[offset];
        regValue >>= RegisterShiftTable[offset];
        return (byte)regValue;
    }

    private static void SetupNoteEnvelopeState(Note note, int steps, int adjust)
    {
        note.PreIncrease = 0;
        if (Math.Abs(adjust) > steps)
        {
            note.PreIncrease = 1;
            note.Adjust = adjust / steps;
            note.StepIncrease = Math.Abs(adjust % steps);
        }
        else
        {
            note.Adjust = adjust;
            note.StepIncrease = Math.Abs(adjust);
        }

        note.Step = steps;
        note.StepCounter = 0;
        note.Timer = steps;
    }

    private static bool ProcessNoteEnvelope(Note note)
    {
        if (note.PreIncrease != 0)
        {
            note.InstrumentValue += note.Adjust;
        }

        note.StepCounter += note.StepIncrease;
        if (note.StepCounter >= note.Step)
        {
            note.StepCounter -= note.Step;
            if (note.Adjust < 0)
            {
                --note.InstrumentValue;
            }
            else
            {
                ++note.InstrumentValue;
            }
        }

        return --note.Timer == 0;
    }

    private byte GetRnd()
    {
        if ((_rndSeed & 1) != 0)
        {
            _rndSeed >>= 1;
            _rndSeed ^= 0xB8;
        }
        else
        {
            _rndSeed >>= 1;
        }

        return _rndSeed;
    }

    private sealed class HardwareChannel
    {
        public bool Allocated;
        public int Priority;
        public SfxSlot? SfxOwner;
    }

    private sealed class VoiceChannel
    {
        public int LastEvent, Frequency, B0Reg;
    }

    private sealed class Note
    {
        public int State, PlayTime, SustainTimer, InstrumentValue, Bias, PreIncrease, Adjust;
        public int StepIncrease, Step, StepCounter, Timer;
    }

    private sealed class Channel
    {
        public int State;
        public int CurrentOffset, StartOffset;
        public readonly byte[] InstrumentData = new byte[7];
        public readonly Note[] Notes = [new(), new()];
        public int HardwareChannel = -1;
    }

    private sealed class SfxSlot
    {
        public int Resource = -1;
        public int Priority;
        public byte[] Data = [];
        public readonly Channel[] Channels = [new(), new(), new()];
    }
}
