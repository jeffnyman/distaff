namespace Distaff.Scumm.Sound.Towns;

/// <summary>What the Towns' audio calls on its timers: a music driver (<c>TownsAudioInterfacePluginDriver</c>).</summary>
internal interface ITownsAudioDriver
{
    /// <summary>Timer A (0) or timer B (1) has fired (<c>timerCallback</c>).</summary>
    void TimerCallback(int timerId);
}

/// <summary>
/// The FM Towns' sound hardware as its BIOS drives it: the FM chip's six channels with their
/// instruments, levels and pitches, and the PCM chip's eight channels, those reserved for sound
/// effects playing samples given whole. Mirrors ScummVM's <c>TownsAudioInterfaceInternal</c>,
/// whose numbered commands (<c>callback</c>) are methods here, each naming its command. Channels
/// 0 to 5 are the FM chip's; 0x40 to 0x47 the PCM chip's. Timer A runs the music driver, timer B
/// the driver too and the PCM notes' envelopes.
/// </summary>
/// <remarks>
/// Left out, as Loom's player does not reach them: the commands to write and read the chip's
/// registers directly, to turn a channel off, to load samples and wave tables and key PCM notes
/// from them (see <see cref="TownsPcmChannel"/>), and the output volumes and mutes, which act
/// only on the CD, whose volume <see cref="PlayerTowns"/> sets as <c>updateOutputVolumeInternal</c>
/// works it out.
/// </remarks>
internal sealed class TownsAudio : TownsFmSynth
{
    private static readonly byte[] ChanFlags = [0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80];
    private static readonly ushort[] FmFrequency = [0x028C, 0x02B4, 0x02DC, 0x030A, 0x0338, 0x0368, 0x039C, 0x03D4, 0x040E, 0x044A, 0x048C, 0x04D0];
    private static readonly byte[] Carrier = [0x10, 0x10, 0x10, 0x10, 0x30, 0x70, 0x70, 0xF0];

    private static readonly byte[] FmDefaultInstrument =
    [
        0x45, 0x4C, 0x45, 0x50, 0x49, 0x41, 0x4E, 0x4F, 0x01, 0x0A, 0x02, 0x01,
        0x1E, 0x32, 0x05, 0x00, 0x9C, 0xDC, 0x9C, 0xDC, 0x07, 0x03, 0x14, 0x08,
        0x00, 0x03, 0x05, 0x05, 0x55, 0x45, 0x27, 0xA7, 0x04, 0xC0, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    ];

    private readonly byte[][] _fmSaveReg = [new byte[256], new byte[256]];
    private readonly byte[] _fmInstruments = new byte[128 * 48];
    private readonly byte[] _pcmInstruments = new byte[32 * 128];
    private readonly TownsPcmChannel[] _pcmChan = [new(), new(), new(), new(), new(), new(), new(), new()];
    private readonly TownsPcmDevice _pcmDev;
    private readonly byte[] _fmChanNote = new byte[6];
    private readonly short[] _fmChanPitch = new short[6];

    private byte _fmChanPlaying;
    private int _numReservedChannels;
    private int _musicVolume = MaxMixerVolume, _sfxVolume = MaxMixerVolume;

    /// <summary>Mirrors <c>init</c>: the chips, the volume masks, and a reset (command 0).</summary>
    public TownsAudio(int outputRate)
        : base(outputRate)
    {
        _pcmDev = new TownsPcmDevice(_pcmChan);
        SetVolumeChannelMasks(-1, 0);
        ResetAll();
    }

    /// <summary>The music driver the timers call, or none (<c>assignPluginDriver</c>).</summary>
    public ITownsAudioDriver? Driver { get; set; }

    /// <summary>Command 0: resets the FM and PCM channels (<c>intf_reset</c>).</summary>
    public void ResetAll()
    {
        FmReset();
        PcmReset();
    }

    /// <summary>Command 1: keys a note on (<c>intf_keyOn</c>).</summary>
    public int KeyOn(int chan, int note, int velo) => (chan & 0x40) != 0 ? PcmKeyOn(chan, note, velo) : FmKeyOn(chan, note, velo);

    /// <summary>Command 2: keys a note off (<c>intf_keyOff</c>).</summary>
    public int KeyOff(int chan) => (chan & 0x40) != 0 ? PcmKeyOff(chan) : FmKeyOff(chan);

    /// <summary>Command 3: a channel's pan, 0 (left) to 127, 64 the middle (<c>intf_setPanPos</c>).</summary>
    public int SetPanPos(int chan, int mode) => (chan & 0x40) != 0 ? PcmSetPanPos(chan, mode) : FmSetPanPos(chan, mode);

    /// <summary>Command 4: a channel's instrument (<c>intf_setInstrument</c>).</summary>
    public int SetInstrument(int chan, int instrId) => (chan & 0x40) != 0 ? PcmSetInstrument(chan, instrId) : FmSetInstrument(chan, instrId);

    /// <summary>Command 5: loads an instrument, a PCM one for a channel type with 0x40, an FM one otherwise (<c>intf_loadInstrument</c>).</summary>
    public int LoadInstrument(int chanType, int instrId, ReadOnlySpan<byte> data)
        => (chanType & 0x40) != 0 ? PcmLoadInstrument(instrId, data) : FmLoadInstrument(instrId, data);

    /// <summary>Command 7: a channel's pitch bend, -8192 to 8191 (<c>intf_setPitch</c>).</summary>
    public int SetPitch(int chan, int pitch)
    {
        short p = (short)(pitch & 0xffff);
        return (chan & 0x40) != 0 ? PcmSetPitch(chan, p) : FmSetPitch(chan, p);
    }

    /// <summary>Command 8: a channel's level, 0 to 127 (<c>intf_setLevel</c>).</summary>
    public int SetLevel(int chan, int lvl) => (chan & 0x40) != 0 ? PcmSetLevel(chan, lvl) : FmSetLevel(chan, lvl);

    /// <summary>Command 21: starts timer A at a setting, or stops it (<c>intf_setTimerA</c>).</summary>
    public void SetTimerA(bool enable, int tempo)
    {
        if (enable)
        {
            BufferedWriteReg(0, 0x25, tempo & 3);
            BufferedWriteReg(0, 0x24, (tempo >> 2) & 0xff);
            BufferedWriteReg(0, 0x27, _fmSaveReg[0][0x27] | 0x05);
        }
        else
        {
            BufferedWriteReg(0, 0x27, (_fmSaveReg[0][0x27] & 0xfa) | 0x10);
        }
    }

    /// <summary>Command 22: starts timer B at a setting, or stops it (<c>intf_setTimerB</c>).</summary>
    public void SetTimerB(bool enable, int tempo)
    {
        if (enable)
        {
            BufferedWriteReg(0, 0x26, tempo & 0xff);
            BufferedWriteReg(0, 0x27, _fmSaveReg[0][0x27] | 0x0A);
        }
        else
        {
            BufferedWriteReg(0, 0x27, (_fmSaveReg[0][0x27] & 0xf5) | 0x20);
        }
    }

    /// <summary>
    /// Command 33: reserves the last <paramref name="numChan"/> PCM channels for sound effects
    /// (<c>intf_reserveEffectChannels</c>). Without wave tables loaded the wave memory is all free.
    /// </summary>
    public int ReserveEffectChannels(int numChan)
    {
        if (numChan > 8)
        {
            return 3;
        }

        if ((numChan << 13) > 65536)
        {
            return 5;
        }

        if (numChan == _numReservedChannels)
        {
            return 0;
        }

        if (numChan < _numReservedChannels)
        {
            int c = 8 - _numReservedChannels;
            for (int i = numChan; i != 0; i--)
            {
                _pcmChan[c--].ActiveEffect = false;
            }
        }
        else
        {
            int c = 7 - _numReservedChannels;
            for (int i = numChan - _numReservedChannels; i != 0; i--)
            {
                _pcmChan[c].KeyPressed = false;
                _pcmChan[c--].ActiveKey = false;
            }
        }

        _numReservedChannels = numChan;
        for (int i = 0; i < 8; i++)
        {
            _pcmChan[i].Reserved = i >= 8 - _numReservedChannels;
        }

        return 0;
    }

    /// <summary>
    /// Command 37: plays a sample, its 32-byte header first, on a reserved PCM channel at a note
    /// and velocity (<c>intf_pcmPlayEffect</c>). 2 when the channel is still playing an effect.
    /// </summary>
    public int PcmPlayEffect(int chan, int note, int velo, ReadOnlySpan<byte> data)
    {
        if (chan < 0x40 || chan > 0x47)
        {
            return 1;
        }

        if ((note & 0x80) != 0 || (velo & 0x80) != 0)
        {
            return 3;
        }

        var p = _pcmChan[chan - 0x40];
        if (!p.Reserved)
        {
            return 7;
        }

        if (p.ActiveEffect)
        {
            return 2;
        }

        var w = TownsWaveHeader.Read(data);
        if (w.Size < w.LoopStart + w.LoopLen)
        {
            return 13;
        }

        if (w.Size == 0)
        {
            return 6;
        }

        p.LoadData(data[32..], w.Size);
        p.KeyOn((byte)note, (byte)velo, w);
        return 0;
    }

    /// <summary>Command 39: stops a PCM channel (<c>intf_pcmChanOff</c>).</summary>
    public void PcmChanOff(int chan)
    {
        if (chan < 0x40 || chan > 0x47)
        {
            return;
        }

        var p = _pcmChan[chan - 0x40];
        p.KeyPressed = p.ActiveEffect = p.ActiveKey = false;
        p.Deactivate();
    }

    /// <summary>Command 40: 1 while a PCM channel plays an effect (<c>intf_pcmEffectPlaying</c>).</summary>
    public int PcmEffectPlaying(int chan)
    {
        if (chan < 0x40 || chan > 0x47)
        {
            return 1;
        }

        return _pcmChan[chan - 0x40].ActiveEffect ? 1 : 0;
    }

    /// <summary>Mirrors <c>setMusicVolume</c>: the volume of the channels outside the effects mask.</summary>
    public void SetMusicVolume(int volume)
    {
        _musicVolume = Math.Clamp(volume, 0, MaxMixerVolume);
        _pcmDev.SetMusicVolume(_musicVolume);
        SetVolumeIntern(_musicVolume, _sfxVolume);
    }

    /// <summary>Mirrors <c>setSoundEffectVolume</c>.</summary>
    public void SetSoundEffectVolume(int volume)
    {
        _sfxVolume = Math.Clamp(volume, 0, MaxMixerVolume);
        _pcmDev.SetSfxVolume(_sfxVolume);
        SetVolumeIntern(_musicVolume, _sfxVolume);
    }

    /// <summary>The channels the effects volume scales: the 6 FM channels in the low bits, then the 8 PCM ones (<c>setSoundEffectChanMask</c>).</summary>
    public void SetSoundEffectChanMask(int mask)
    {
        _pcmDev.SetSfxChanMask(mask >> 6);
        mask &= 0x3f;
        SetVolumeChannelMasks(~mask, mask);
    }

    protected override void NextTickEx(int[] buffer, int offset, int count) => _pcmDev.ReadBuffer(buffer, offset, count);

    protected override void TimerCallbackA() => Driver?.TimerCallback(0);

    protected override void TimerCallbackB()
    {
        Driver?.TimerCallback(1);

        // command 80 (intf_pcmUpdateEnvelopeGenerator)
        foreach (var p in _pcmChan)
        {
            p.UpdateEnvelopeGenerator();
        }
    }

    /// <summary>Mirrors <c>fmReset</c>: the chip, the saved registers, the default instrument everywhere, and each channel off at full level.</summary>
    private void FmReset()
    {
        Reset();

        _fmChanPlaying = 0;
        Array.Clear(_fmChanNote);
        Array.Clear(_fmChanPitch);

        Array.Clear(_fmSaveReg[0], 0, 240);
        Array.Fill(_fmSaveReg[0], (byte)0x7f, 240, 16);
        Array.Clear(_fmSaveReg[1]);
        Array.Fill(_fmSaveReg[1], (byte)0x7f, 240, 16);
        foreach (var r in _fmSaveReg)
        {
            r[243] = r[247] = r[251] = r[255] = 0xff;
        }

        for (int i = 0; i < 128; i++)
        {
            FmLoadInstrument(i, FmDefaultInstrument);
        }

        BufferedWriteReg(0, 0x21, 0);
        BufferedWriteReg(0, 0x2C, 0x80);
        BufferedWriteReg(0, 0x2B, 0);
        BufferedWriteReg(0, 0x27, 0x30);

        for (int i = 0; i < 6; i++)
        {
            FmKeyOff(i);
            FmSetInstrument(i, 0);
            FmSetLevel(i, 127);
        }
    }

    /// <summary>
    /// Mirrors <c>fmKeyOn</c>: the note's block and F-number with the channel's pitch bend, the
    /// carriers' levels from the velocity and the channel's level, and the key on.
    /// </summary>
    private int FmKeyOn(int chan, int note, int velo)
    {
        if (chan > 5)
        {
            return 1;
        }

        if (note < 12 || note > 107 || (velo & 0x80) != 0)
        {
            return 3;
        }

        if ((_fmChanPlaying & ChanFlags[chan]) != 0)
        {
            return 2;
        }

        _fmChanPlaying |= ChanFlags[chan];
        note -= 12;

        _fmChanNote[chan] = (byte)note;
        short pitch = _fmChanPitch[chan];

        int part = chan > 2 ? 1 : 0;
        if (chan > 2)
        {
            chan -= 3;
        }

        int frq;
        int bl = 0;

        if (note != 0)
        {
            frq = FmFrequency[(note - 1) % 12];
            bl = (note - 1) / 12;
        }
        else
        {
            frq = 616;
        }

        frq += pitch;

        if (frq < 616)
        {
            if (bl == 0)
            {
                frq = 616;
            }
            else
            {
                frq += 616;
                --bl;
            }
        }
        else if (frq > 1232)
        {
            if (bl == 7)
            {
                frq = 15500;
            }
            else
            {
                frq -= 616;
                ++bl;
            }
        }

        frq |= bl << 11;

        BufferedWriteReg(part, chan + 0xa4, (frq >> 8) & 0xff);
        BufferedWriteReg(part, chan + 0xa0, frq & 0xff);

        velo = (velo >> 2) + 96;
        int c = Carrier[_fmSaveReg[part][0xb0 + chan] & 7];
        _fmSaveReg[part][0xe0 + chan] = (byte)velo;

        for (int reg = 0x40 + chan; reg < 0x50; reg += 4)
        {
            c += c;
            if ((c & 0x100) != 0)
            {
                c &= 0xff;
                BufferedWriteReg(part, reg, (((((((_fmSaveReg[part][0x80 + reg] ^ 0x7f) * velo) >> 7) + 1) * _fmSaveReg[part][0xd0 + chan]) >> 7) + 1) ^ 0x7f);
            }
        }

        int v = chan;
        if (part != 0)
        {
            v |= 4;
        }

        for (int reg = 0x80 + chan; reg < 0x90; reg += 4)
        {
            WriteReg(part, reg, _fmSaveReg[part][reg] | 0x0f);
        }

        WriteReg(0, 0x28, v);

        for (int reg = 0x80 + chan; reg < 0x90; reg += 4)
        {
            WriteReg(part, reg, _fmSaveReg[part][reg]);
        }

        BufferedWriteReg(0, 0x28, v | 0xf0);

        return 0;
    }

    /// <summary>Mirrors <c>fmKeyOff</c>.</summary>
    private int FmKeyOff(int chan)
    {
        if (chan > 5)
        {
            return 1;
        }

        _fmChanPlaying &= (byte)~ChanFlags[chan];
        if (chan > 2)
        {
            chan++;
        }

        BufferedWriteReg(0, 0x28, chan);
        return 0;
    }

    /// <summary>Mirrors <c>fmSetPanPos</c>: left, right or both.</summary>
    private int FmSetPanPos(int chan, int value)
    {
        if (chan > 5)
        {
            return 1;
        }

        int part = chan > 2 ? 1 : 0;
        if (chan > 2)
        {
            chan -= 3;
        }

        if (value > 0x40)
        {
            value = 0x40;
        }
        else if (value < 0x40)
        {
            value = 0x80;
        }
        else
        {
            value = 0xC0;
        }

        BufferedWriteReg(part, 0xb4 + chan, (_fmSaveReg[part][0xb4 + chan] & 0x3f) | value);
        return 0;
    }

    /// <summary>
    /// Mirrors <c>fmSetInstrument</c>: the instrument's operator registers, its carriers' levels
    /// kept and written silent until a key on, its algorithm and feedback, and its sides.
    /// </summary>
    private int FmSetInstrument(int chan, int instrId)
    {
        if (chan > 5)
        {
            return 1;
        }

        if (instrId > 127)
        {
            return 3;
        }

        int part = chan > 2 ? 1 : 0;
        if (chan > 2)
        {
            chan -= 3;
        }

        int src = instrId * 48 + 8;

        int c = Carrier[_fmInstruments[src + 24] & 7];
        int reg = 0x30 + chan;

        for (; reg < 0x40; reg += 4)
        {
            BufferedWriteReg(part, reg, _fmInstruments[src++]);
        }

        for (; reg < 0x50; reg += 4)
        {
            byte v = _fmInstruments[src++];
            _fmSaveReg[part][0x80 + reg] = _fmSaveReg[part][reg] = v;
            c += c;
            if ((c & 0x100) != 0)
            {
                c &= 0xff;
                v = 127;
            }

            WriteReg(part, reg, v);
        }

        for (; reg < 0x90; reg += 4)
        {
            BufferedWriteReg(part, reg, _fmInstruments[src++]);
        }

        reg += 0x20;
        BufferedWriteReg(part, reg, _fmInstruments[src++]);

        int last = _fmInstruments[src];
        reg += 4;
        if (last < 64)
        {
            last |= _fmSaveReg[part][reg] & 0xc0;
        }

        BufferedWriteReg(part, reg, last);
        return 0;
    }

    /// <summary>Mirrors <c>fmLoadInstrument</c>: 48 bytes, a name and the registers.</summary>
    private int FmLoadInstrument(int instrId, ReadOnlySpan<byte> data)
    {
        if (instrId > 127)
        {
            return 3;
        }

        data[..48].CopyTo(_fmInstruments.AsSpan(instrId * 48));
        return 0;
    }

    /// <summary>Mirrors <c>fmSetPitch</c>: the note's F-number moved by a 13th of the bend, into the next block when it leaves this one.</summary>
    private int FmSetPitch(int chan, int pitch)
    {
        if (chan > 5)
        {
            return 1;
        }

        int bl = _fmChanNote[chan];
        int frq;

        if (pitch < 0)
        {
            if (bl != 0)
            {
                if (pitch < -8008)
                {
                    pitch = -8008;
                }

                pitch *= -1;
                pitch /= 13;
                frq = FmFrequency[(bl - 1) % 12] - pitch;
                bl = (bl - 1) / 12;
                _fmChanPitch[chan] = (short)-pitch;

                if (frq < 616)
                {
                    if (bl != 0)
                    {
                        frq += 616;
                        bl--;
                    }
                    else
                    {
                        frq = 616;
                        bl = 0;
                    }
                }
            }
            else
            {
                frq = 616;
                bl = 0;
            }
        }
        else if (pitch > 0)
        {
            if (bl < 96)
            {
                if (pitch > 8008)
                {
                    pitch = 8008;
                }

                pitch /= 13;

                if (bl != 0)
                {
                    frq = FmFrequency[(bl - 1) % 12] + pitch;
                    bl = (bl - 1) / 12;
                }
                else
                {
                    frq = 616;
                    bl = 0;
                }

                _fmChanPitch[chan] = (short)pitch;

                if (frq > 1232)
                {
                    if (bl < 7)
                    {
                        frq -= 616;
                        bl++;
                    }
                    else
                    {
                        frq = 1164;
                        bl = 7;
                    }
                }
                else if (bl >= 7 && frq > 1164)
                {
                    frq = 1164;
                }
            }
            else
            {
                frq = 1164;
                bl = 7;
            }
        }
        else
        {
            _fmChanPitch[chan] = 0;
            if (bl != 0)
            {
                frq = FmFrequency[(bl - 1) % 12];
                bl = (bl - 1) / 12;
            }
            else
            {
                frq = 616;
                bl = 0;
            }
        }

        int part = chan > 2 ? 1 : 0;
        if (chan > 2)
        {
            chan -= 3;
        }

        frq |= bl << 11;

        BufferedWriteReg(part, chan + 0xa4, frq >> 8);
        BufferedWriteReg(part, chan + 0xa0, frq & 0xff);
        return 0;
    }

    /// <summary>Mirrors <c>fmSetLevel</c>: the carriers' levels from the channel's level and the last key on's velocity.</summary>
    private int FmSetLevel(int chan, int lvl)
    {
        if (chan > 5)
        {
            return 1;
        }

        if (lvl > 127)
        {
            return 3;
        }

        int part = chan > 2 ? 1 : 0;
        if (chan > 2)
        {
            chan -= 3;
        }

        int c = Carrier[_fmSaveReg[part][0xb0 + chan] & 7];
        _fmSaveReg[part][0xd0 + chan] = (byte)lvl;

        for (int reg = 0x40 + chan; reg < 0x50; reg += 4)
        {
            c += c;
            if ((c & 0x100) != 0)
            {
                c &= 0xff;
                BufferedWriteReg(part, reg, (((((((_fmSaveReg[part][0x80 + reg] ^ 0x7f) * lvl) >> 7) + 1) * _fmSaveReg[part][0xe0 + chan]) >> 7) + 1) ^ 0x7f);
            }
        }

        return 0;
    }

    /// <summary>Writes a register and keeps its value (<c>bufferedWriteReg</c>).</summary>
    private void BufferedWriteReg(int part, int regAddress, int value)
    {
        _fmSaveReg[part][regAddress] = (byte)value;
        WriteReg(part, regAddress, value);
    }

    /// <summary>Mirrors <c>pcmReset</c>: no channels reserved, every channel cleared, and the instruments emptied.</summary>
    private void PcmReset()
    {
        _numReservedChannels = 0;

        foreach (var p in _pcmChan)
        {
            p.Clear();
        }

        Array.Clear(_pcmInstruments);
        ReadOnlySpan<byte> name = "No Data!"u8;
        for (int i = 0; i < 32; i++)
        {
            name.CopyTo(_pcmInstruments.AsSpan(i * 128));
        }

        for (int i = 0x40; i < 0x48; i++)
        {
            PcmSetInstrument(i, 0);
            PcmSetLevel(i, 127);
        }
    }

    /// <summary>
    /// Mirrors <c>pcmKeyOn</c>: a reserved channel, or one with a key held, takes no note; any
    /// other looks for the note's wave table, of which Loom loads none (see <see cref="TownsPcmChannel.InitInstrument"/>).
    /// </summary>
    private int PcmKeyOn(int chan, int note, int velo)
    {
        if (chan < 0x40 || chan > 0x47)
        {
            return 1;
        }

        if ((note & 0x80) != 0 || (velo & 0x80) != 0)
        {
            return 3;
        }

        var p = _pcmChan[chan - 0x40];
        if (p.Reserved || p.KeyPressed)
        {
            return 2;
        }

        byte noteT = (byte)note;
        return p.InitInstrument(ref noteT);
    }

    private int PcmKeyOff(int chan)
    {
        if (chan < 0x40 || chan > 0x47)
        {
            return 1;
        }

        _pcmChan[chan - 0x40].KeyOff();
        return 0;
    }

    /// <summary>Mirrors <c>pcmSetPanPos</c>: the pan as the left's and right's levels, 0 to 7.</summary>
    private int PcmSetPanPos(int chan, int mode)
    {
        if (chan > 0x47)
        {
            return 1;
        }

        if ((mode & 0x80) != 0)
        {
            return 3;
        }

        int blc = 0x77;
        if (mode > 64)
        {
            mode -= 64;
            blc = ((blc ^ (mode >> 3)) + (mode << 4)) & 0xff;
        }
        else if (mode < 64)
        {
            mode = (mode >> 3) ^ 7;
            blc = ((119 + mode) ^ (mode << 4)) & 0xff;
        }

        _pcmChan[chan - 0x40].SetBalance((byte)blc);
        return 0;
    }

    private int PcmSetInstrument(int chan, int instrId)
    {
        if (chan > 0x47)
        {
            return 1;
        }

        if (instrId > 31)
        {
            return 3;
        }

        _pcmChan[chan - 0x40].SetInstrument(_pcmInstruments, instrId * 128);
        return 0;
    }

    private int PcmLoadInstrument(int instrId, ReadOnlySpan<byte> data)
    {
        if (instrId > 31)
        {
            return 3;
        }

        data[..128].CopyTo(_pcmInstruments.AsSpan(instrId * 128));
        return 0;
    }

    /// <summary>Mirrors <c>pcmSetPitch</c>: the bend as a factor of the note's rate, 0x4000 for none.</summary>
    private int PcmSetPitch(int chan, int pitch)
    {
        if (chan > 0x47)
        {
            return 1;
        }

        if (pitch < -8192 || pitch > 8191)
        {
            return 3;
        }

        uint pts = 0x4000;
        if (pitch < 0)
        {
            pts = (uint)(0x20000000 / (-pitch + 0x2001)) >> 2;
        }
        else if (pitch > 0)
        {
            pts = (uint)(((pitch + 0x2001) << 16) / 0x2000) >> 2;
        }

        _pcmChan[chan - 0x40].SetPitch(pts);
        return 0;
    }

    private int PcmSetLevel(int chan, int lvl)
    {
        if (chan > 0x47)
        {
            return 1;
        }

        if ((lvl & 0x80) != 0)
        {
            return 3;
        }

        _pcmChan[chan - 0x40].SetLevel((byte)lvl);
        return 0;
    }
}
