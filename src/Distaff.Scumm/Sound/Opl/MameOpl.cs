using static Distaff.Scumm.Sound.Opl.MameOplTables;

namespace Distaff.Scumm.Sound.Opl;

/// <summary>
/// The OPL2 (YM3812) emulator ScummVM picks for the AdLib music player: mirrors ScummVM's
/// <c>OPL::MAME::OPL</c> and the FM-OPL core it wraps (audio/softsynth/opl/mame.cpp, MAME's
/// <c>fmopl.c</c> V0.37a by Tatsuyuki Satoh, by way of AdPlug), with the same integer arithmetic,
/// so the samples are the same as ScummVM's. GPL-licensed like the rest of Distaff.
/// </summary>
/// <remarks>
/// Only the YM3812 paths are ported. The timers have no handler (ScummVM's wrapper sets none),
/// so they only latch their registers. The rhythm section's white noise comes from ScummVM's
/// <c>Common::RandomSource</c>, which ScummVM seeds from the clock; here the seed is a parameter.
/// </remarks>
internal sealed class MameOpl : IOpl
{
    /// <summary>The noise generator's seed when none is given.</summary>
    public const uint DefaultRandomSeed = 1;

    // the YM3812's clock, as makeAdLibOPL creates it, and the attack and decay rate constants (OPL_ARRATE, OPL_DRRATE)
    private const int Clock = 3579545;
    private const int ArRate = 141280;
    private const int DrRate = 1956000;

    // envelope phases (ENV_MOD_RR, ENV_MOD_DR, ENV_MOD_AR)
    private const byte EnvModRr = 0x00, EnvModDr = 0x01, EnvModAr = 0x02;

    // AR_TABLE, DR_TABLE and RATE_0 side by side; a slot's AR, DR and RR are offsets into this
    private const int ArTable = 0, DrTable = 76, Rate0 = 152;

    private readonly int _rate;
    private readonly int[] _rates = new int[Rate0 + 16];
    private readonly uint[] _fnTable = new uint[1024];
    private readonly Channel[] _channels = new Channel[9];
    private readonly int _amsIncr;
    private readonly int _vibIncr;
    private uint _randSeed;

    private byte _address;
    private byte _status;
    private byte _statusMask;
    private uint _mode;
    private readonly int[] _timers = new int[2];
    private readonly byte[] _timerEnabled = new byte[2];
    private byte _rhythm;
    private int _amsTable;
    private int _vibTable;
    private int _amsCnt;
    private int _vibCnt;
    private byte _waveSel;

    // the file-level state of mame.cpp: the LFO tables are latched when a chip first generates (cur_chip)
    private bool _current;
    private int _curAmsIncr, _curVibIncr, _curAmsTable, _curVibTable;
    private int _outd, _ams, _vib, _feedback2;

    /// <summary>Creates the chip and resets it, as <c>OPL::init</c> does with the mixer's output rate.</summary>
    /// <param name="sampleRate">The rate of the samples <see cref="Generate"/> produces.</param>
    /// <param name="randomSeed">The seed of the rhythm section's noise generator.</param>
    public MameOpl(int sampleRate, uint randomSeed = DefaultRandomSeed)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        _rate = sampleRate;
        _randSeed = randomSeed == 0 ? 1 : randomSeed;
        for (int c = 0; c < _channels.Length; c++)
        {
            _channels[c] = new Channel();
        }

        // OPL_initalize
        double freqBase = (double)Clock / _rate / 72;
        InitTimeTables(freqBase);
        for (int fn = 0; fn < 1024; fn++)
        {
            _fnTable[fn] = (uint)(freqBase * fn * FreqRate * (1 << 7) / 2);
        }

        _amsIncr = (int)((double)AmsEnt * (1 << AmsShift) / _rate * 3.7 * ((double)Clock / 3600000));
        _vibIncr = (int)((double)VibEnt * (1 << VibShift) / _rate * 6.4 * ((double)Clock / 3600000));

        Reset();
    }

    /// <summary>The rate of the samples <see cref="Generate"/> produces.</summary>
    public int SampleRate => _rate;

    /// <summary>Resets the chip (<c>OPLResetChip</c>).</summary>
    public void Reset()
    {
        _mode = 0;
        StatusReset(0x7f);
        WriteRegister(0x01, 0);
        WriteRegister(0x02, 0);
        WriteRegister(0x03, 0);
        WriteRegister(0x04, 0);
        for (int i = 0xff; i >= 0x20; i--)
        {
            WriteRegister(i, 0);
        }

        foreach (var ch in _channels)
        {
            foreach (var slot in ch.Slots)
            {
                slot.Wavetable = 0;
                slot.Evc = EgOff;
                slot.Eve = EgOff + 1;
                slot.Evs = 0;
            }
        }
    }

    /// <summary>Writes to the address (even) or data (odd) port and returns the IRQ flag (<c>OPLWrite</c>).</summary>
    public int Write(int port, int value)
    {
        if ((port & 1) == 0)
        {
            _address = (byte)value;
        }
        else
        {
            WriteRegister(_address, value);
        }

        return _status >> 7;
    }

    /// <summary>Reads the status (even) or data (odd) port (<c>OPLRead</c>).</summary>
    public byte Read(int port)
    {
        if ((port & 1) == 0)
        {
            return (byte)(_status & (_statusMask | 0x80));
        }

        return 0;
    }

    /// <summary>Writes a register (<c>OPLWriteReg</c>).</summary>
    public void WriteRegister(int register, int value)
    {
        int r = register, v = value;
        Channel ch;
        int slot;
        switch (r & 0xe0)
        {
            case 0x00:
                switch (r & 0x1f)
                {
                    case 0x01:
                        // wave selector enable; off restores the sine on every slot
                        _waveSel = (byte)(v & 0x20);
                        if (_waveSel == 0)
                        {
                            foreach (var c in _channels)
                            {
                                c.Slots[0].Wavetable = 0;
                                c.Slots[1].Wavetable = 0;
                            }
                        }

                        return;
                    case 0x02:
                        _timers[0] = (256 - v) * 4;
                        break;
                    case 0x03:
                        _timers[1] = (256 - v) * 16;
                        return;
                    case 0x04:
                        if ((v & 0x80) != 0)
                        {
                            StatusReset(0x7f);
                        }
                        else
                        {
                            // IRQ mask and timer enable; there is no timer handler to call
                            byte st1 = (byte)(v & 1);
                            byte st2 = (byte)((v >> 1) & 1);
                            StatusReset(v & 0x78);
                            StatusMaskSet((~v & 0x78) | 0x01);
                            _timerEnabled[1] = st2;
                            _timerEnabled[0] = st1;
                        }

                        return;
                }

                break;
            case 0x20:
                slot = SlotArray[r & 0x1f];
                if (slot != -1)
                {
                    SetMul(slot, v);
                }

                return;
            case 0x40:
                slot = SlotArray[r & 0x1f];
                if (slot != -1)
                {
                    SetKslTl(slot, v);
                }

                return;
            case 0x60:
                slot = SlotArray[r & 0x1f];
                if (slot != -1)
                {
                    SetArDr(slot, v);
                }

                return;
            case 0x80:
                slot = SlotArray[r & 0x1f];
                if (slot != -1)
                {
                    SetSlRr(slot, v);
                }

                return;
            case 0xa0:
                if (r == 0xbd)
                {
                    WriteRhythm(v);
                    return;
                }

                // key on, block, F-number
                if ((r & 0x0f) > 8)
                {
                    return;
                }

                ch = _channels[r & 0x0f];
                uint blockFnum;
                if ((r & 0x10) == 0)
                {
                    blockFnum = (ch.BlockFnum & 0x1f00) | (uint)v;
                }
                else
                {
                    byte keyon = (byte)((v >> 5) & 1);
                    blockFnum = ((uint)(v & 0x1f) << 8) | (ch.BlockFnum & 0xff);
                    if (ch.Keyon != keyon)
                    {
                        if ((ch.Keyon = keyon) != 0)
                        {
                            ch.Op1Out0 = ch.Op1Out1 = 0;
                            KeyOn(ch.Slots[0]);
                            KeyOn(ch.Slots[1]);
                        }
                        else
                        {
                            KeyOff(ch.Slots[0]);
                            KeyOff(ch.Slots[1]);
                        }
                    }
                }

                if (ch.BlockFnum != blockFnum)
                {
                    int blockRv = (int)(7 - (blockFnum >> 10));
                    int fnum = (int)(blockFnum & 0x3ff);
                    ch.BlockFnum = blockFnum;
                    ch.KslBase = Ksl[blockFnum >> 6];
                    ch.Fc = _fnTable[fnum] >> blockRv;
                    ch.Kcode = (byte)(ch.BlockFnum >> 9);
                    if ((_mode & 0x40) != 0 && (ch.BlockFnum & 0x100) != 0)
                    {
                        ch.Kcode |= 1;
                    }

                    CalcFcSlot(ch, ch.Slots[0]);
                    CalcFcSlot(ch, ch.Slots[1]);
                }

                return;
            case 0xc0:
                // feedback, connection
                if ((r & 0x0f) > 8)
                {
                    return;
                }

                ch = _channels[r & 0x0f];
                int feedback = (v >> 1) & 7;
                ch.Fb = (byte)(feedback != 0 ? 8 + 1 - feedback : 0);
                ch.Con = (byte)(v & 1);
                ch.Connect1ToOutput = ch.Con != 0; // set_algorythm
                return;
            case 0xe0:
                // wave select
                slot = SlotArray[r & 0x1f];
                if (slot == -1)
                {
                    return;
                }

                if (_waveSel != 0)
                {
                    _channels[slot >> 1].Slots[slot & 1].Wavetable = (v & 0x03) * SinEnt;
                }

                return;
        }
    }

    /// <summary>Generates samples (<c>YM3812UpdateOne</c>).</summary>
    public void Generate(Span<short> mono)
    {
        uint amsCnt = (uint)_amsCnt;
        uint vibCnt = (uint)_vibCnt;
        bool rhythm = (_rhythm & 0x20) != 0;
        if (!_current)
        {
            _current = true;
            _curAmsIncr = _amsIncr;
            _curVibIncr = _vibIncr;
            _curAmsTable = _amsTable;
            _curVibTable = _vibTable;
        }

        int melodic = rhythm ? 6 : 9;
        for (int i = 0; i < mono.Length; i++)
        {
            amsCnt += (uint)_curAmsIncr;
            vibCnt += (uint)_curVibIncr;
            _ams = Ams[_curAmsTable + (int)(amsCnt >> AmsShift)];
            _vib = Vib[_curVibTable + (int)(vibCnt >> VibShift)];
            _outd = 0;
            for (int c = 0; c < melodic; c++)
            {
                CalcChannel(_channels[c]);
            }

            if (rhythm)
            {
                CalcRhythm();
            }

            int data = Math.Clamp(_outd, MinOut, MaxOut);
            mono[i] = (short)(data >> OutShift);
        }

        _amsCnt = (int)amsCnt;
        _vibCnt = (int)vibCnt;
    }

    // OP_OUT: the waveform at the phase (offset by con) attenuated by env
    private static int OpOut(Slot slot, uint env, int con)
    {
        int phase = (int)(((slot.Cnt + (uint)con) >> (24 - SinEntShift)) & (SinEnt - 1));
        return Tl[Sin[slot.Wavetable + phase] + (int)env];
    }

    // OPL_KEYON
    private static void KeyOn(Slot slot)
    {
        slot.Cnt = 0;
        slot.Evm = EnvModAr;
        slot.Evs = slot.Evsa;
        slot.Evc = EgAst;
        slot.Eve = EgAed;
    }

    // OPL_KEYOFF, with ScummVM's workaround for a key off at the very start of the attack
    private static void KeyOff(Slot slot)
    {
        if (slot.Evm > EnvModRr)
        {
            if (slot.Evm == EnvModAr && slot.Evc == EgAst)
            {
                slot.Evc = EgDed;
            }
            else if ((slot.Evc & EgDst) == 0)
            {
                slot.Evc = EgDst;
            }

            slot.Eve = EgDed;
            slot.Evs = slot.Evsr;
            slot.Evm = EnvModRr;
        }
    }

    // CALC_FCSLOT
    private void CalcFcSlot(Channel ch, Slot slot)
    {
        slot.Incr = ch.Fc * slot.Mul;
        int ksr = ch.Kcode >> slot.KsrShift;
        if (slot.Ksr != ksr)
        {
            slot.Ksr = (byte)ksr;
            slot.Evsa = _rates[slot.Ar + ksr];
            slot.Evsd = _rates[slot.Dr + ksr];
            slot.Evsr = _rates[slot.Rr + ksr];
        }

        slot.Tll = (int)(slot.Tl + (ch.KslBase >> slot.Ksl));
    }

    private Slot SlotAt(int slot) => _channels[slot >> 1].Slots[slot & 1];

    // set_mul
    private void SetMul(int slot, int v)
    {
        var ch = _channels[slot >> 1];
        var s = ch.Slots[slot & 1];
        s.Mul = Mul[v & 0x0f];
        s.KsrShift = (byte)((v & 0x10) != 0 ? 0 : 2);
        s.EgTyp = (byte)((v & 0x20) >> 5);
        s.Vib = (byte)(v & 0x40);
        s.Ams = (byte)(v & 0x80);
        CalcFcSlot(ch, s);
    }

    // set_ksl_tl
    private void SetKslTl(int slot, int v)
    {
        var ch = _channels[slot >> 1];
        var s = ch.Slots[slot & 1];
        int ksl = v >> 6;
        s.Ksl = (byte)(ksl != 0 ? 3 - ksl : 31);
        s.Tl = (int)((v & 0x3f) * (0.75 / EgStep));
        if ((_mode & 0x80) == 0)
        {
            s.Tll = (int)(s.Tl + (ch.KslBase >> s.Ksl));
        }
    }

    // set_ar_dr
    private void SetArDr(int slot, int v)
    {
        var s = SlotAt(slot);
        int ar = v >> 4;
        int dr = v & 0x0f;
        s.Ar = ar != 0 ? ArTable + (ar << 2) : Rate0;
        s.Evsa = _rates[s.Ar + s.Ksr];
        if (s.Evm == EnvModAr)
        {
            s.Evs = s.Evsa;
        }

        s.Dr = dr != 0 ? DrTable + (dr << 2) : Rate0;
        s.Evsd = _rates[s.Dr + s.Ksr];
        if (s.Evm == EnvModDr)
        {
            s.Evs = s.Evsd;
        }
    }

    // set_sl_rr
    private void SetSlRr(int slot, int v)
    {
        var s = SlotAt(slot);
        int sl = v >> 4;
        int rr = v & 0x0f;
        s.Sl = MameOplTables.Sl[sl];
        if (s.Evm == EnvModDr)
        {
            s.Eve = s.Sl;
        }

        s.Rr = DrTable + (rr << 2);
        s.Evsr = _rates[s.Rr + s.Ksr];
        if (s.Evm == EnvModRr)
        {
            s.Evs = s.Evsr;
        }
    }

    // register 0xBD: tremolo and vibrato depth, rhythm mode and the drums' keys
    private void WriteRhythm(int v)
    {
        byte rkey = (byte)(_rhythm ^ v);
        _amsTable = (v & 0x80) != 0 ? AmsEnt : 0;
        _vibTable = (v & 0x40) != 0 ? VibEnt : 0;
        _rhythm = (byte)(v & 0x3f);
        if ((_rhythm & 0x20) == 0)
        {
            return;
        }

        // bass drum
        if ((rkey & 0x10) != 0)
        {
            if ((v & 0x10) != 0)
            {
                _channels[6].Op1Out0 = _channels[6].Op1Out1 = 0;
                KeyOn(_channels[6].Slots[0]);
                KeyOn(_channels[6].Slots[1]);
            }
            else
            {
                KeyOff(_channels[6].Slots[0]);
                KeyOff(_channels[6].Slots[1]);
            }
        }

        RhythmKey(rkey, v, 0x08, _channels[7].Slots[1]); // snare drum
        RhythmKey(rkey, v, 0x04, _channels[8].Slots[0]); // tom-tom
        RhythmKey(rkey, v, 0x02, _channels[8].Slots[1]); // top cymbal
        RhythmKey(rkey, v, 0x01, _channels[7].Slots[0]); // hi-hat
    }

    private static void RhythmKey(byte rkey, int v, int bit, Slot slot)
    {
        if ((rkey & bit) != 0)
        {
            if ((v & bit) != 0)
            {
                KeyOn(slot);
            }
            else
            {
                KeyOff(slot);
            }
        }
    }

    // OPL_CALC_SLOT: steps the envelope and returns the slot's attenuation
    private uint CalcSlot(Slot slot)
    {
        if ((slot.Evc += slot.Evs) >= slot.Eve)
        {
            switch (slot.Evm)
            {
                case EnvModAr:
                    slot.Evm = EnvModDr;
                    slot.Evc = EgDst;
                    slot.Eve = slot.Sl;
                    slot.Evs = slot.Evsd;
                    break;
                case EnvModDr:
                    slot.Evc = slot.Sl;
                    slot.Eve = EgDed;
                    if (slot.EgTyp != 0)
                    {
                        slot.Evs = 0;
                    }
                    else
                    {
                        slot.Evm = EnvModRr;
                        slot.Evs = slot.Evsr;
                    }

                    break;
                case EnvModRr:
                    slot.Evc = EgOff;
                    slot.Eve = EgOff + 1;
                    slot.Evs = 0;
                    break;
            }
        }

        return (uint)(slot.Tll + EnvCurve[slot.Evc >> EnvBits] + (slot.Ams != 0 ? _ams : 0));
    }

    // the phase generator's step, with vibrato
    private void StepPhase(Slot slot)
    {
        if (slot.Vib != 0)
        {
            slot.Cnt += (slot.Incr * (uint)_vib) >> VibRateShift;
        }
        else
        {
            slot.Cnt += slot.Incr;
        }
    }

    // OPL_CALC_CH
    private void CalcChannel(Channel ch)
    {
        _feedback2 = 0;
        var slot = ch.Slots[0];
        uint envOut = CalcSlot(slot);
        if (envOut < (uint)(EgEnt - 1))
        {
            StepPhase(slot);
            int output;
            if (ch.Fb != 0)
            {
                int feedback1 = (ch.Op1Out0 + ch.Op1Out1) >> ch.Fb;
                ch.Op1Out1 = ch.Op1Out0;
                output = ch.Op1Out0 = OpOut(slot, envOut, feedback1);
            }
            else
            {
                output = OpOut(slot, envOut, 0);
            }

            if (ch.Connect1ToOutput)
            {
                _outd += output;
            }
            else
            {
                _feedback2 += output;
            }
        }
        else
        {
            ch.Op1Out1 = ch.Op1Out0;
            ch.Op1Out0 = 0;
        }

        slot = ch.Slots[1];
        envOut = CalcSlot(slot);
        if (envOut < (uint)(EgEnt - 1))
        {
            StepPhase(slot);
            _outd += OpOut(slot, envOut, _feedback2);
        }
    }

    // OPL_CALC_RH: bass drum on channel 6, the other drums on the slots of channels 7 and 8
    private void CalcRhythm()
    {
        int whitenoise = (int)(GetRandomBit() * (EgEnt >> 4));
        var ch6 = _channels[6];
        var ch7 = _channels[7];
        var ch8 = _channels[8];
        Slot slot71 = ch7.Slots[0], slot72 = ch7.Slots[1], slot81 = ch8.Slots[0], slot82 = ch8.Slots[1];

        // BD: as the FM serial mode, louder
        _feedback2 = 0;
        var slot = ch6.Slots[0];
        int envOut = (int)CalcSlot(slot);
        if (envOut < EgEnt - 1)
        {
            StepPhase(slot);
            if (ch6.Fb != 0)
            {
                int feedback1 = (ch6.Op1Out0 + ch6.Op1Out1) >> ch6.Fb;
                ch6.Op1Out1 = ch6.Op1Out0;
                _feedback2 = ch6.Op1Out0 = OpOut(slot, (uint)envOut, feedback1);
            }
            else
            {
                _feedback2 = OpOut(slot, (uint)envOut, 0);
            }
        }
        else
        {
            _feedback2 = 0;
            ch6.Op1Out1 = ch6.Op1Out0;
            ch6.Op1Out0 = 0;
        }

        slot = ch6.Slots[1];
        envOut = (int)CalcSlot(slot);
        if (envOut < EgEnt - 1)
        {
            StepPhase(slot);
            _outd += OpOut(slot, (uint)envOut, _feedback2) * 2;
        }

        uint envSd = CalcSlot(slot72) + (uint)whitenoise;
        uint envTam = CalcSlot(slot81);
        uint envTop = CalcSlot(slot82);
        uint envHh = CalcSlot(slot71) + (uint)whitenoise;

        uint vib = (uint)_vib;
        if (slot71.Vib != 0)
        {
            slot71.Cnt += (slot71.Incr * vib) >> (VibRateShift - 1);
        }
        else
        {
            slot71.Cnt += 2 * slot71.Incr;
        }

        if (slot72.Vib != 0)
        {
            slot72.Cnt += (ch7.Fc * vib) >> (VibRateShift - 3);
        }
        else
        {
            slot72.Cnt += ch7.Fc * 8;
        }

        if (slot81.Vib != 0)
        {
            slot81.Cnt += (slot81.Incr * vib) >> VibRateShift;
        }
        else
        {
            slot81.Cnt += slot81.Incr;
        }

        if (slot82.Vib != 0)
        {
            slot82.Cnt += (ch8.Fc * 3 * vib) >> (VibRateShift - 4);
        }
        else
        {
            slot82.Cnt += ch8.Fc * 48;
        }

        int tone8 = OpOut(slot82, (uint)whitenoise, 0);

        // SD, TAM, TOP-CY, HH
        if (envSd < (uint)(EgEnt - 1))
        {
            _outd += OpOut(slot71, envSd, 0) * 8;
        }

        if (envTam < (uint)(EgEnt - 1))
        {
            _outd += OpOut(slot81, envTam, 0) * 2;
        }

        if (envTop < (uint)(EgEnt - 1))
        {
            _outd += OpOut(slot72, envTop, tone8) * 2;
        }

        if (envHh < (uint)(EgEnt - 1))
        {
            _outd += OpOut(slot72, envHh, tone8) * 2;
        }
    }

    // init_timetables
    private void InitTimeTables(double freqBase)
    {
        for (int i = 0; i < 4; i++)
        {
            _rates[ArTable + i] = _rates[DrTable + i] = 0;
        }

        for (int i = 4; i <= 60; i++)
        {
            double rate = freqBase;
            if (i < 60)
            {
                rate *= 1.0 + ((i & 3) * 0.25);
            }

            rate *= 1 << ((i >> 2) - 1);
            rate *= (double)(EgEnt << EnvBits);
            _rates[ArTable + i] = (int)(rate / ArRate);
            _rates[DrTable + i] = (int)(rate / DrRate);
        }

        for (int i = 60; i < 76; i++)
        {
            _rates[ArTable + i] = EgAed - 1;
            _rates[DrTable + i] = _rates[DrTable + 60];
        }
    }

    // OPL_STATUS_RESET and OPL_STATUSMASK_SET; there is no IRQ handler to call
    private void StatusSet(int flag)
    {
        _status |= (byte)flag;
        if ((_status & 0x80) == 0 && (_status & _statusMask) != 0)
        {
            _status |= 0x80;
        }
    }

    private void StatusReset(int flag)
    {
        _status &= (byte)~flag;
        if ((_status & 0x80) != 0 && (_status & _statusMask) == 0)
        {
            _status &= 0x7f;
        }
    }

    private void StatusMaskSet(int flag)
    {
        _statusMask = (byte)flag;
        StatusSet(0);
        StatusReset(0);
    }

    // Common::RandomSource::getRandomBit
    private uint GetRandomBit()
    {
        _randSeed ^= _randSeed >> 13;
        _randSeed ^= _randSeed << 21;
        _randSeed ^= _randSeed >> 11;
        return _randSeed & 1;
    }

    // OPL_SLOT
    private sealed class Slot
    {
        public int Tl;
        public int Tll;
        public byte KsrShift;   // KSR
        public int Ar;
        public int Dr;
        public int Sl;
        public int Rr;
        public byte Ksl;
        public byte Ksr;
        public uint Mul;
        public uint Cnt;
        public uint Incr;
        public byte EgTyp;
        public byte Evm;
        public int Evc;
        public int Eve;
        public int Evs;
        public int Evsa;
        public int Evsd;
        public int Evsr;
        public byte Ams;
        public byte Vib;
        public int Wavetable;   // offset into the sine table
    }

    // OPL_CH
    private sealed class Channel
    {
        public readonly Slot[] Slots = [new Slot(), new Slot()];
        public byte Con;
        public byte Fb;
        public bool Connect1ToOutput;   // connect1: the carrier, or slot 2's modulation
        public int Op1Out0, Op1Out1;
        public uint BlockFnum;
        public byte Kcode;
        public uint Fc;
        public uint KslBase;
        public byte Keyon;
    }
}
