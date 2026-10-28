namespace Distaff.Scumm.Sound.Towns;

/// <summary>
/// One operator of the FM Towns' FM chip: its phase, its envelope (attack, decay, sustain,
/// release, and the SSG-type shapes) and its output through the log-sine and level tables.
/// Mirrors ScummVM's <c>TownsPC98_FmSynthOperator</c>.
/// </summary>
internal sealed class TownsFmOperator
{
    private readonly uint _tickLength;
    private readonly uint _envDuration;

    private EnvelopeState _state;
    private bool _keyOn;
    private int _feedbackLevel;
    private uint _multiple;
    private uint _totalLevel;
    private byte _keyScale1;
    private byte _keyScale2;
    private uint _specifiedAttackRate;
    private uint _specifiedDecayRate;
    private uint _specifiedSustainRate;
    private uint _specifiedReleaseRate;
    private uint _envelopeShapeSpecs;
    private uint _tickCount;
    private uint _sustainLevel;

    private uint _frequency;
    private ushort _freqTemp;
    private byte _kcode;
    private uint _phase;
    private uint _phaseIncrement;
    private uint _shapeState;
    private byte _shapeScale;
    private int _detune;

    private int _currentLevel = 1023;
    private uint _timer;

    private (byte Rate, byte Shift) _fsA, _fsD, _fsS, _fsR;

    public TownsFmOperator(uint tickLength, uint envDuration)
    {
        _tickLength = tickLength;
        _envDuration = envDuration;
        Reset();
    }

    /// <summary>Mirrors <c>keyOn</c>: the phase starts again and the envelope attacks.</summary>
    public void KeyOn()
    {
        if (_keyOn)
        {
            return;
        }

        _keyOn = true;
        _phase = 0;
        _state = EnvelopeState.Attacking;
        _shapeState = _envelopeShapeSpecs;
    }

    /// <summary>Mirrors <c>keyOff</c>.</summary>
    public void KeyOff()
    {
        if (!_keyOn)
        {
            return;
        }

        _keyOn = false;
        if (_state != EnvelopeState.Ready)
        {
            _state = EnvelopeState.Releasing;
        }
    }

    /// <summary>The block and the frequency's high bits, which the low byte's write takes up (<c>frequencyHi</c>).</summary>
    public void FrequencyHi(byte frqH) => _freqTemp = (ushort)((_freqTemp & 0xff) | ((frqH & 0x3f) << 8));

    /// <summary>Mirrors <c>frequencyLo</c>.</summary>
    public void FrequencyLo(byte frqL)
    {
        _freqTemp = (ushort)((_freqTemp & 0xff00) | frqL);
        Frequency(_freqTemp);
    }

    /// <summary>Mirrors <c>updatePhaseIncrement</c>: the phase's step, and the rates again when the key scale changes.</summary>
    public void UpdatePhaseIncrement()
    {
        _phaseIncrement = unchecked((_frequency + (uint)TownsFmTables.Detune[_detune + _kcode]) * _multiple) >> 1;
        byte keyscale = (byte)(_kcode >> _keyScale1);
        if (_keyScale2 != keyscale)
        {
            _keyScale2 = keyscale;
            RecalculateRates();
        }
    }

    /// <summary>
    /// Advances the envelope by the chip's sample and adds the operator's output to
    /// <paramref name="output"/>: modulated by <paramref name="phaseBuffer"/>, or, for a channel's
    /// first operator, by its own last two outputs in <paramref name="feed"/>. Mirrors <c>generateOutput</c>.
    /// </summary>
    public void GenerateOutput(int phaseBuffer, int[]? feed, ref int output)
    {
        if (_state == EnvelopeState.Ready)
        {
            return;
        }

        var rates = TownsFmTables.AttackDecay;
        _timer += _tickLength;
        while (_timer >= _envDuration)
        {
            _timer -= _envDuration;
            ++_tickCount;

            int levelIncrement = 0;
            uint targetTime = 0;
            int targetLevel = 0;
            var nextState = EnvelopeState.Ready;

            for (bool loop = true; loop;)
            {
                switch (_state)
                {
                    case EnvelopeState.Ready:
                        return;

                    case EnvelopeState.Attacking:
                        targetLevel = 0;
                        nextState = _sustainLevel != 0 ? EnvelopeState.Decaying : EnvelopeState.Sustaining;
                        if ((_specifiedAttackRate << 1) + _keyScale2 < 62)
                        {
                            targetTime = (1u << _fsA.Shift) - 1;
                            levelIncrement = (~_currentLevel * rates[_fsA.Rate + ((_tickCount >> _fsA.Shift) & 7)]) >> 4;
                        }
                        else
                        {
                            _currentLevel = targetLevel;
                            _state = nextState;
                            continue;
                        }

                        break;

                    case EnvelopeState.Decaying:
                        targetTime = (1u << _fsD.Shift) - 1;
                        nextState = EnvelopeState.Sustaining;
                        targetLevel = (int)_sustainLevel;
                        levelIncrement = rates[_fsD.Rate + ((_tickCount >> _fsD.Shift) & 7)] << _shapeScale;
                        break;

                    case EnvelopeState.Sustaining:
                        targetTime = (1u << _fsS.Shift) - 1;
                        nextState = EnvelopeState.Sustaining;
                        targetLevel = _shapeScale != 0 ? 832 : 1023;
                        levelIncrement = rates[_fsS.Rate + ((_tickCount >> _fsS.Shift) & 7)] << _shapeScale;
                        break;

                    case EnvelopeState.Releasing:
                        targetTime = (1u << _fsR.Shift) - 1;
                        nextState = EnvelopeState.Ready;
                        targetLevel = 1023;
                        levelIncrement = rates[_fsR.Rate + ((_tickCount >> _fsR.Shift) & 7)];
                        break;
                }

                loop = false;
            }

            if ((_tickCount & targetTime) == 0)
            {
                _currentLevel += levelIncrement;
                if ((_state == EnvelopeState.Attacking && _currentLevel <= targetLevel) || (_state != EnvelopeState.Attacking && _currentLevel >= targetLevel))
                {
                    if (_state != EnvelopeState.Decaying)
                    {
                        _currentLevel = targetLevel;
                    }

                    if (_state == EnvelopeState.Sustaining && _shapeScale != 0)
                    {
                        _currentLevel += 191;

                        if ((_shapeState & 1) != 0)
                        {
                            if ((_shapeState & 0x10) == 0)
                            {
                                _shapeState |= 0x40;
                            }
                        }
                        else
                        {
                            nextState = EnvelopeState.Attacking;
                            _phase = 0;
                            _currentLevel = 511;
                            _shapeState &= ~0x40u;
                        }
                    }

                    _state = nextState;
                }
            }
        }

        uint lvlout = unchecked(_totalLevel + ((uint)_currentLevel ^ (_state != EnvelopeState.Releasing ? (uint)(((_shapeScale * (_shapeState & 4)) >> 3) * 1023) : 0)));
        _shapeState ^= ((_shapeState & 0x40) >> 2) | ((_shapeState & 2) << 1);

        int phaseShift;
        int value;
        if (feed is not null)
        {
            phaseShift = _feedbackLevel != 0 ? (feed[0] + feed[1]) << _feedbackLevel : 0;
            feed[0] = feed[1];
        }
        else
        {
            phaseShift = phaseBuffer << 15;
        }

        if (lvlout < 832)
        {
            uint index = (lvlout << 3) + TownsFmTables.Sine[(unchecked((int)(_phase & 0xffff0000) + phaseShift) >> 16) & 0x3ff];
            value = index < 6656 ? TownsFmTables.LevelOut[index] : 0;
        }
        else
        {
            value = 0;
        }

        _phase += _phaseIncrement;
        if (feed is not null)
        {
            // the output is the one before; the new one waits in the feedback
            feed[1] = value;
            output += feed[0];
        }
        else
        {
            output += value;
        }
    }

    /// <summary>Mirrors <c>feedbackLevel</c>.</summary>
    public void FeedbackLevel(int level) => _feedbackLevel = level != 0 ? level + 6 : 0;

    /// <summary>Mirrors <c>detune</c>: the row of the detune table.</summary>
    public void Detune(int value) => _detune = value << 5;

    /// <summary>Mirrors <c>multiple</c>.</summary>
    public void Multiple(uint value) => _multiple = value != 0 ? value << 1 : 1;

    /// <summary>Mirrors <c>attackRate</c>.</summary>
    public void AttackRate(uint value) => _specifiedAttackRate = value;

    /// <summary>
    /// Sets the key scale; true when it changed, for the channel to update. Otherwise the attack's
    /// rate is worked out again from the rate just set. Mirrors <c>scaleRate</c>.
    /// </summary>
    public bool ScaleRate(byte value)
    {
        value = (byte)(3 - value);
        if (_keyScale1 != value)
        {
            _keyScale1 = value;
            return true;
        }

        int k = _keyScale2;
        int r = _specifiedAttackRate != 0 ? (int)(_specifiedAttackRate << 1) + 0x20 : 0;
        _fsA = (r + k) < 94 ? (TownsFmTables.Rates[r + k], TownsFmTables.RateShifts[r + k]) : ((byte)136, (byte)0);
        return false;
    }

    /// <summary>Mirrors <c>decayRate</c>.</summary>
    public void DecayRate(uint value)
    {
        _specifiedDecayRate = value;
        RecalculateRates();
    }

    /// <summary>Mirrors <c>sustainRate</c>.</summary>
    public void SustainRate(uint value)
    {
        _specifiedSustainRate = value;
        RecalculateRates();
    }

    /// <summary>Mirrors <c>sustainLevel</c>.</summary>
    public void SustainLevel(uint value) => _sustainLevel = value == 0x0f ? 0x3e0u : value << 5;

    /// <summary>Mirrors <c>releaseRate</c>.</summary>
    public void ReleaseRate(uint value)
    {
        _specifiedReleaseRate = value;
        RecalculateRates();
    }

    /// <summary>Mirrors <c>envelopeShape</c>.</summary>
    public void EnvelopeShape(uint value)
    {
        _envelopeShapeSpecs = value;
        _shapeScale = (byte)((value & 8) >> 2);
    }

    /// <summary>Mirrors <c>totalLevel</c>.</summary>
    public void TotalLevel(uint value) => _totalLevel = value << 3;

    /// <summary>Amplitude modulation, which needs the LFO the emulation has not (<c>ampModulation</c>).</summary>
    public void AmpModulation(bool enable)
    {
    }

    /// <summary>Mirrors <c>reset</c>.</summary>
    public void Reset()
    {
        KeyOff();
        _timer = 0;
        _keyScale2 = 0;
        _currentLevel = 1023;

        Frequency(0);
        Detune(0);
        ScaleRate(0);
        Multiple(0);
        UpdatePhaseIncrement();
        AttackRate(0);
        DecayRate(0);
        ReleaseRate(0);
        SustainRate(0);
        EnvelopeShape(0);
        FeedbackLevel(0);
        TotalLevel(127);
        AmpModulation(false);
    }

    /// <summary>Mirrors <c>frequency</c>: the key code and the frequency from a block and an F-number.</summary>
    private void Frequency(int freq)
    {
        byte block = (byte)(freq >> 11);
        int pos = freq & 0x7ff;
        int c = pos >> 7;

        _kcode = (byte)((block << 2) | (c < 7 ? 0 : (c > 8 ? 3 : c - 6)));
        _frequency = TownsFmTables.Frequencies[pos << 1] >> (7 - block);
        _freqTemp = 0;
    }

    /// <summary>Mirrors <c>recalculateRates</c>.</summary>
    private void RecalculateRates()
    {
        var rates = TownsFmTables.Rates;
        var shifts = TownsFmTables.RateShifts;
        int k = _keyScale2;
        int r = _specifiedAttackRate != 0 ? (int)(_specifiedAttackRate << 1) + 0x20 : 0;
        _fsA = (r + k) < 94 ? (rates[r + k], shifts[r + k]) : ((byte)136, (byte)0);

        r = _specifiedDecayRate != 0 ? (int)(_specifiedDecayRate << 1) + 0x20 : 0;
        _fsD = (rates[r + k], shifts[r + k]);

        r = _specifiedSustainRate != 0 ? (int)(_specifiedSustainRate << 1) + 0x20 : 0;
        _fsS = (rates[r + k], shifts[r + k]);

        r = (int)(_specifiedReleaseRate << 2) + 0x22;
        _fsR = (rates[r + k], shifts[r + k]);
    }
}

/// <summary>
/// The FM chip's tables for the Towns (six channels, a rate scale of 1), as ScummVM works them
/// out (<c>TownsPC98_FmSynth::generateTables</c>): the envelope's rates and their shifts, its
/// steps, the frequencies, the log-sine table, the output levels and the detunes.
/// </summary>
internal static class TownsFmTables
{
    private static readonly uint[] AdtStat =
    [
        0x00010001, 0x00010001, 0x00010001, 0x01010001,
        0x00010101, 0x00010101, 0x00010101, 0x01010101,
        0x01010101, 0x01010101, 0x01010102, 0x01010102,
        0x01020102, 0x01020102, 0x01020202, 0x01020202,
        0x02020202, 0x02020202, 0x02020204, 0x02020204,
        0x02040204, 0x02040204, 0x02040404, 0x02040404,
        0x04040404, 0x04040404, 0x04040408, 0x04040408,
        0x04080408, 0x04080408, 0x04080808, 0x04080808,
        0x08080808, 0x08080808, 0x10101010, 0x10101010,
    ];

    private static readonly byte[] DetSrc =
    [
        0x02, 0x02, 0x02, 0x02, 0x02, 0x03, 0x03, 0x03,
        0x04, 0x04, 0x04, 0x05, 0x05, 0x06, 0x06, 0x07,
        0x08, 0x08, 0x08, 0x08, 0x01, 0x01, 0x01, 0x01,
        0x02, 0x02, 0x02, 0x02, 0x02, 0x03, 0x03, 0x03,
        0x04, 0x04, 0x04, 0x05, 0x05, 0x06, 0x06, 0x07,
        0x08, 0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e,
        0x10, 0x10, 0x10, 0x10, 0x02, 0x02, 0x02, 0x02,
        0x02, 0x03, 0x03, 0x03, 0x04, 0x04, 0x04, 0x05,
        0x05, 0x06, 0x06, 0x07, 0x08, 0x08, 0x09, 0x0a,
        0x0b, 0x0c, 0x0d, 0x0e, 0x10, 0x11, 0x13, 0x14,
        0x16, 0x16, 0x16, 0x16,
    ];

    public static readonly byte[] Rates = new byte[130];
    public static readonly byte[] RateShifts = new byte[130];
    public static readonly byte[] AttackDecay = new byte[152];
    public static readonly uint[] Frequencies = new uint[0x1000];
    public static readonly uint[] Sine = new uint[1024];
    public static readonly int[] LevelOut = new int[0x1a00];
    public static readonly int[] Detune = new int[256];

    static TownsFmTables()
    {
        const int rateScale = 1;

        Array.Fill(Rates, (byte)0x90, 0, 32);
        WriteBe(Rates, 32, 0x90900000);
        WriteBe(Rates, 36, 0x00001010);
        Array.Fill(Rates, (byte)0x80, 96, 34);
        for (int i = 0; i < 40; i += 4)
        {
            WriteBe(Rates, 40 + i, 0x00081018);
        }

        for (int i = 0; i < 16; i++)
        {
            int v = i < 12 ? i : 12;
            Rates[80 + i] = (byte)((4 + v) << 3);
        }

        for (int i = 11, dst = 32; i != 0; i--, dst += 4)
        {
            Array.Fill(RateShifts, (byte)i, dst, 4);
        }

        for (uint i = 0; i < 0x1000; i++)
        {
            Frequencies[i] = i << (11 - rateScale);
        }

        for (int i = 0; i < 36; i++)
        {
            WriteBe(AttackDecay, i << 2, AdtStat[i]);
        }

        for (int i = 0; i < 1024; i++)
        {
            double val = Math.Sin(((i << 1) + 1) * Math.PI / 1024.0);
            double dDcb = Math.Log(1.0 / Math.Abs(val)) / Math.Log(2.0) * 256.0;
            int iDcb = (int)(2.0 * dDcb);
            iDcb = (iDcb & 1) != 0 ? (iDcb >> 1) + 1 : iDcb >> 1;
            Sine[i] = (uint)((iDcb << 1) + (val >= 0.0 ? 0 : 1));
        }

        for (int i = 0; i < 256; i++)
        {
            double val = Math.Floor(65536.0 / Math.Pow(2.0, 0.00390625 * (1 + i)));
            int valInt = (int)val >> 4;
            LevelOut[i << 1] = (valInt & 1) != 0 ? ((valInt >> 1) + 1) << 2 : (valInt >> 1) << 2;
            LevelOut[(i << 1) + 1] = -LevelOut[i << 1];
            for (int ii = 1; ii < 13; ++ii)
            {
                LevelOut[(i << 1) + (ii << 9)] = LevelOut[i << 1] >> ii;
                LevelOut[(i << 1) + (ii << 9) + 1] = -LevelOut[(i << 1) + (ii << 9)];
            }
        }

        var dtt = new byte[128];
        Array.Fill(dtt, (byte)1, 36, 8);
        DetSrc.CopyTo(dtt, 44);
        for (int i = 0; i < 128; i++)
        {
            Detune[i] = dtt[i] << (6 - rateScale);
            Detune[i + 128] = -Detune[i];
        }
    }

    private static void WriteBe(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}
