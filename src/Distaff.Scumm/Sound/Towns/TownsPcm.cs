namespace Distaff.Scumm.Sound.Towns;

/// <summary>
/// One of the FM Towns' eight PCM channels: 8-bit samples stepped through at a rate set by the
/// note, with a loop, a volume and a pan of 0 to 7 on each side. A channel reserved for sound
/// effects plays a sample given whole at the velocity as its volume; a keyed note on one not
/// reserved has an envelope. Mirrors ScummVM's <c>TownsAudio_PCMChannel</c> on <c>PCMChannel_Base</c>.
/// </summary>
/// <remarks>
/// A keyed note plays from the wave tables the driver loads (<c>intf_loadWaveTable</c>), which
/// Loom never loads: its sounds are all effects. The tables and the notes keyed from them
/// (<c>loadDataFromWaveTable</c>, the end of <c>pcmKeyOn</c>) are left out; the envelope, which
/// key off and the level still reach, is kept.
/// </remarks>
internal sealed class TownsPcmChannel
{
    private static readonly ushort[] PcmPhase1 = [0x0F37, 0x1F58, 0x306E, 0x4288, 0x55B6, 0x6A08, 0x7F8F, 0x965E, 0xAE88, 0xC882, 0xE341];
    private static readonly ushort[] PcmPhase2 = [0xF1A0, 0xE411, 0xD744, 0xCB2F, 0xBFC7, 0xB504, 0xAAE2, 0xA144, 0x9827, 0x8FAC, 0x879B];

    // PCMChannel_Base
    private byte _panLeft = 7, _panRight = 7;
    private byte _vol;
    private bool _activeOutput;
    private uint _loopStart, _loopLen, _dataEnd, _pos;
    private ushort _step;
    private sbyte[] _data = [];

    // TownsAudio_PCMChannel
    private byte[]? _curInstrument;
    private int _curInstrumentOffset;
    private byte _note, _velo, _level;
    private ushort _stepNote = 0x4000, _stepPitch = 0x4000;
    private byte _envTotalLevel, _envAttackRate, _envDecayRate, _envSustainLevel, _envSustainRate, _envReleaseRate;
    private short _envStep, _envCurrentLevel;
    private EnvelopeState _envState;

    public bool KeyPressed, Reserved, ActiveKey, ActiveEffect;

    /// <summary>Whether the channel still plays (<c>isPlaying</c>).</summary>
    public bool IsPlaying => ActiveKey || ActiveEffect;

    /// <summary>Whether the channel is heard (<c>isActive</c>): a channel that stops is heard for one more sample.</summary>
    public bool IsActive => _activeOutput;

    /// <summary>Mirrors <c>clear</c> of both classes.</summary>
    public void Clear()
    {
        _vol = 0;
        _data = [];
        _dataEnd = 0;
        _loopLen = 0;
        _pos = 0;
        _loopStart = 0;
        _step = 0;
        _panLeft = _panRight = 7;
        _activeOutput = false;

        _curInstrument = null;
        _note = _level = _velo = 0;
        _stepNote = 0x4000;
        _stepPitch = 0x4000;
        _envTotalLevel = _envAttackRate = _envDecayRate = _envSustainLevel = _envSustainRate = _envReleaseRate = 0;
        _envStep = _envCurrentLevel = 0;
        _envState = EnvelopeState.Ready;
        ActiveKey = ActiveEffect = KeyPressed = Reserved = false;
    }

    /// <summary>Mirrors <c>activate</c>.</summary>
    public void Activate() => _activeOutput = true;

    /// <summary>Mirrors <c>deactivate</c>.</summary>
    public void Deactivate() => _activeOutput = false;

    /// <summary>Steps through the sample, looping or stopping at its end. Mirrors <c>updateOutput</c>.</summary>
    public void UpdateOutput()
    {
        if (!IsPlaying)
        {
            return;
        }

        _pos += _step;

        if (_pos >= _dataEnd)
        {
            if (_loopStart != _dataEnd)
            {
                _pos = _loopStart;
                _dataEnd = _loopStart + _loopLen;
            }
            else
            {
                _pos = 0;
                ActiveKey = ActiveEffect = false;
            }
        }
    }

    /// <summary>Mirrors <c>currentSampleLeft</c>.</summary>
    public int CurrentSampleLeft => _activeOutput && _panLeft != 0 ? (_data[_pos >> 11] * _vol * _panLeft) >> 3 : 0;

    /// <summary>Mirrors <c>currentSampleRight</c>.</summary>
    public int CurrentSampleRight => _activeOutput && _panRight != 0 ? (_data[_pos >> 11] * _vol * _panRight) >> 3 : 0;

    /// <summary>
    /// Takes a sample whole: each byte is a sign and a magnitude, the sign bit set for the
    /// positive. Mirrors <c>loadData</c>.
    /// </summary>
    public void LoadData(ReadOnlySpan<byte> buffer, uint size)
    {
        var data = new sbyte[size];
        for (int i = 0; i < size; i++)
        {
            byte b = buffer[i];
            data[i] = (b & 0x80) != 0 ? (sbyte)(b & 0x7f) : (sbyte)-(sbyte)b;
        }

        _data = data;
        _dataEnd = size << 11;
        _pos = 0;
    }

    /// <summary>
    /// Looks up the instrument's part for the note: its envelope and its transposition. Mirrors
    /// <c>initInstrument</c> as far as its wave table, which none is loaded to give: 8 when no
    /// part takes the note, otherwise 9 (<c>kNoMatchingWaveTable</c>).
    /// </summary>
    public int InitInstrument(ref byte note)
    {
        var instr = _curInstrument!;
        int o = _curInstrumentOffset;
        int i = 0;
        for (; i < 8; i++)
        {
            if (note <= instr[o + 16 + 2 * i])
            {
                break;
            }
        }

        if (i == 8)
        {
            return 8;
        }

        int d = o + (i << 3) + 64;
        _envTotalLevel = instr[d];
        _envAttackRate = instr[d + 1];
        _envDecayRate = instr[d + 2];
        _envSustainLevel = instr[d + 3];
        _envSustainRate = instr[d + 4];
        _envReleaseRate = instr[d + 5];
        _envStep = 0;
        note = (byte)(note + instr[d + 6]);

        return 9;
    }

    /// <summary>
    /// Plays the sample from its start, looping from its loop start when it has a loop length,
    /// at the note's rate and the velocity's volume. Mirrors <c>keyOn</c>.
    /// </summary>
    public void KeyOn(byte note, byte velo, in TownsWaveHeader w)
    {
        SetupLoop(w.LoopLen != 0 ? w.LoopStart : w.Size, w.LoopLen);
        SetNote(note, w, Reserved);
        SetVelo(velo);

        if (Reserved)
        {
            ActiveEffect = true;
        }
        else
        {
            KeyPressed = ActiveKey = true;
        }

        Activate();
    }

    /// <summary>Mirrors <c>keyOff</c>.</summary>
    public void KeyOff()
    {
        KeyPressed = false;
        EnvRelease();
    }

    /// <summary>A step of a keyed note's envelope, at timer B's rate. Mirrors <c>updateEnvelopeGenerator</c>.</summary>
    public void UpdateEnvelopeGenerator()
    {
        if (_envCurrentLevel == 0)
        {
            ActiveKey = false;
            _envState = EnvelopeState.Ready;
        }

        if (!ActiveKey)
        {
            return;
        }

        switch (_envState)
        {
            case EnvelopeState.Attacking:
                if (((_envCurrentLevel + _envStep) >> 8) > _envTotalLevel)
                {
                    EnvDecay();
                    return;
                }

                _envCurrentLevel += _envStep;
                break;

            case EnvelopeState.Decaying:
                if (((_envCurrentLevel - _envStep) >> 8) < _envSustainLevel)
                {
                    EnvSustain();
                    return;
                }

                _envCurrentLevel -= _envStep;
                break;

            case EnvelopeState.Sustaining:
            case EnvelopeState.Releasing:
                _envCurrentLevel -= _envStep;
                if (_envCurrentLevel <= 0)
                {
                    _envCurrentLevel = 0;
                }

                break;
        }

        _vol = (byte)((_envCurrentLevel >> 8) << 1);
    }

    /// <summary>The instrument, 128 bytes of a table, the channel's notes are keyed from (<c>setInstrument</c>).</summary>
    public void SetInstrument(byte[] table, int offset)
    {
        _curInstrument = table;
        _curInstrumentOffset = offset;
    }

    /// <summary>An effect's volume, or a keyed note's level scaling its envelope (<c>setLevel</c>).</summary>
    public void SetLevel(byte lvl)
    {
        if (Reserved)
        {
            _velo = lvl;
            _vol = (byte)(lvl << 1);
        }
        else
        {
            int t = _envStep * lvl;
            if (_level != 0)
            {
                t /= _level;
            }

            _envStep = (short)t;
            t = _envCurrentLevel * lvl;
            if (_level != 0)
            {
                t /= _level;
            }

            _envCurrentLevel = (short)t;
            _level = lvl;
            _vol = (byte)(_envCurrentLevel >> 8);
        }
    }

    /// <summary>The pitch bend's factor, 0x4000 for none, on the note's rate (<c>setPitch</c>).</summary>
    public void SetPitch(uint pt)
    {
        _stepPitch = (ushort)(pt & 0xffff);
        ushort step = (ushort)((_stepNote * _stepPitch) >> 14);
        _step = ActiveEffect && step > 2048 ? (ushort)2048 : step;
    }

    /// <summary>The pan, the left's level in the low nibble and the right's in the high (<c>setBalance</c>).</summary>
    public void SetBalance(byte blc)
    {
        _panLeft = (byte)(blc & 0x0f);
        _panRight = (byte)(blc >> 4);
    }

    private void SetupLoop(uint loopStart, uint loopLen)
    {
        _loopStart = loopStart << 11;
        _loopLen = loopLen << 11;
    }

    /// <summary>
    /// The rate for a note: the sample's own at its base note, an octave a doubling, and the
    /// steps between from the phase tables. An effect's step is held to 2048 (the sample's rate
    /// once over). Mirrors <c>setNote</c>.
    /// </summary>
    private void SetNote(byte note, in TownsWaveHeader w, bool stepLimit)
    {
        _note = note;
        sbyte diff = (sbyte)(_note - w.BaseNote);
        ushort r = (ushort)(w.Rate + w.RateOffs);
        int bl;
        uint s;

        if (diff < 0)
        {
            diff = (sbyte)-diff;
            bl = diff % 12;
            diff /= 12;
            s = (uint)(r >> diff);
            if (bl != 0)
            {
                s = (s * PcmPhase2[bl - 1]) >> 16;
            }
        }
        else if (diff > 0)
        {
            bl = diff % 12;
            diff /= 12;
            s = (uint)(r << diff);
            if (bl != 0)
            {
                s += (s * PcmPhase1[bl - 1]) >> 16;
            }
        }
        else
        {
            s = r;
        }

        _stepNote = (ushort)s;
        ushort step = (ushort)((s * _stepPitch) >> 14);
        _step = stepLimit && step > 2048 ? (ushort)2048 : step;
    }

    private void SetVelo(byte velo)
    {
        if (Reserved)
        {
            _velo = velo;
            _vol = (byte)(velo << 1);
        }
        else
        {
            _velo = velo;
            uint lvl = (uint)(_level * _velo);
            _envTotalLevel = (byte)((_envTotalLevel * lvl) >> 14);
            _envSustainLevel = (byte)((_envSustainLevel * lvl) >> 14);
            EnvAttack();
            _vol = (byte)((_envCurrentLevel >> 8) << 1);
        }
    }

    private void EnvAttack()
    {
        _envState = EnvelopeState.Attacking;
        short t = (short)(_envTotalLevel << 8);
        if (_envAttackRate == 127)
        {
            _envCurrentLevel = _envStep = 0;
        }
        else if (_envAttackRate != 0)
        {
            _envStep = (short)(t / _envAttackRate);
            _envCurrentLevel = 1;
        }
        else
        {
            _envCurrentLevel = t;
            EnvDecay();
        }
    }

    private void EnvDecay()
    {
        _envState = EnvelopeState.Decaying;
        short t = (short)(_envTotalLevel - _envSustainLevel);
        if (t < 0 || _envDecayRate == 127)
        {
            _envStep = 0;
        }
        else if (_envDecayRate != 0)
        {
            _envStep = (short)((t << 8) / _envDecayRate);
        }
        else
        {
            _envCurrentLevel = (short)(_envSustainLevel << 8);
            EnvSustain();
        }
    }

    private void EnvSustain()
    {
        _envState = EnvelopeState.Sustaining;
        if (_envSustainLevel != 0 && _envSustainRate != 0)
        {
            _envStep = _envSustainRate == 127 ? (short)0 : (short)((_envCurrentLevel / _envSustainRate) >> 1);
        }
        else
        {
            _envStep = _envCurrentLevel = 1;
        }
    }

    private void EnvRelease()
    {
        _envState = EnvelopeState.Releasing;
        if (_envReleaseRate == 127)
        {
            _envStep = 0;
        }
        else if (_envReleaseRate != 0)
        {
            _envStep = (short)(_envCurrentLevel / _envReleaseRate);
        }
        else
        {
            _envStep = _envCurrentLevel = 1;
        }
    }
}

/// <summary>
/// The 32-byte header of a sample, as an effect's data starts with it: its name, id, size,
/// loop start and length, rate and its offset, and base note. Mirrors <c>TownsAudio_WaveTable::readHeader</c>.
/// </summary>
internal readonly record struct TownsWaveHeader(int Id, uint Size, uint LoopStart, uint LoopLen, ushort Rate, ushort RateOffs, ushort BaseNote)
{
    public static TownsWaveHeader Read(ReadOnlySpan<byte> b) => new(
        BitConverter.ToInt32(b[8..]),
        BitConverter.ToUInt32(b[12..]),
        BitConverter.ToUInt32(b[16..]),
        BitConverter.ToUInt32(b[20..]),
        BitConverter.ToUInt16(b[24..]),
        BitConverter.ToUInt16(b[26..]),
        (ushort)BitConverter.ToUInt32(b[28..]));
}

/// <summary>
/// The PCM chip's eight channels, stepped at its rate of 19300 a second against the FM chip's
/// samples, and mixed into them. Mirrors ScummVM's <c>PCMDevice_Base</c>.
/// </summary>
internal sealed class TownsPcmDevice
{
    private const int DeviceVolume = 128;

    private readonly TownsPcmChannel[] _channels;
    private readonly uint _intRate = (7670454u << 8) / 19300;
    private readonly uint _extRate = 72 << 8;
    private uint _timer;
    private int _musicVolume = TownsFmSynth.MaxMixerVolume;
    private int _sfxVolume = TownsFmSynth.MaxMixerVolume;
    private int _pcmSfxChanMask;

    public TownsPcmDevice(TownsPcmChannel[] channels) => _channels = channels;

    /// <summary>Mirrors <c>setMusicVolume</c>.</summary>
    public void SetMusicVolume(int vol) => _musicVolume = vol;

    /// <summary>Mirrors <c>setSfxVolume</c>.</summary>
    public void SetSfxVolume(int vol) => _sfxVolume = vol;

    /// <summary>Which channels the effects volume scales; the rest take the music volume (<c>setSfxChanMask</c>).</summary>
    public void SetSfxChanMask(int mask) => _pcmSfxChanMask = mask;

    /// <summary>Adds the channels' samples to the FM chip's (<c>readBuffer</c>).</summary>
    public void ReadBuffer(int[] buffer, int offset, int count)
    {
        for (int i = 0; i < count; i++)
        {
            _timer += _extRate;
            while (_timer >= _intRate)
            {
                _timer -= _intRate;
                foreach (var c in _channels)
                {
                    c.UpdateOutput();
                }
            }

            int finOutL = 0, finOutR = 0;
            for (int ii = 0; ii < _channels.Length; ii++)
            {
                var c = _channels[ii];
                if (c.IsActive)
                {
                    int oL = c.CurrentSampleLeft;
                    int oR = c.CurrentSampleRight;
                    if (((1 << ii) & ~_pcmSfxChanMask) != 0)
                    {
                        oL = oL * _musicVolume / TownsFmSynth.MaxMixerVolume;
                        oR = oR * _musicVolume / TownsFmSynth.MaxMixerVolume;
                    }

                    if (((1 << ii) & _pcmSfxChanMask) != 0)
                    {
                        oL = oL * _sfxVolume / TownsFmSynth.MaxMixerVolume;
                        oR = oR * _sfxVolume / TownsFmSynth.MaxMixerVolume;
                    }

                    finOutL += oL;
                    finOutR += oR;

                    if (!c.IsPlaying)
                    {
                        c.Deactivate();
                    }
                }
            }

            buffer[offset + (i << 1)] += (finOutL * DeviceVolume) >> 7;
            buffer[offset + (i << 1) + 1] += (finOutR * DeviceVolume) >> 7;
        }
    }
}
