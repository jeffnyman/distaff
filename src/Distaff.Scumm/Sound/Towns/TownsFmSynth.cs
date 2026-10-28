namespace Distaff.Scumm.Sound.Towns;

/// <summary>An envelope's stage, for the FM operators and the PCM channels both (<c>EnvelopeState</c>).</summary>
internal enum EnvelopeState
{
    Ready,
    Attacking,
    Decaying,
    Sustaining,
    Releasing,
}

/// <summary>
/// The FM Towns' FM chip, a YM2612 of six four-operator channels with two timers, as ScummVM
/// emulates it. Mirrors <c>TownsPC98_FmSynth</c> for the Towns (<c>kTypeTowns</c>: six channels,
/// no square-wave or rhythm sources): the chip runs at its own rate, 7670454 / 72 samples a
/// second, and its output is taken to the output's rate by keeping the sample each output sample
/// falls on (<c>readBuffer</c>). The timers count in the chip's samples, and their callbacks run
/// between the samples they fall on, so what the driver does in them is as exact as the samples.
/// </summary>
/// <remarks>
/// The chip's busy-flag wait cycles (<c>ENABLE_SNDTOWNS98_WAITCYCLES</c>) are off in ScummVM, and
/// are left out, as are the LFO, the DAC and the PC-98's sources, which the Towns has not.
/// </remarks>
internal abstract class TownsFmSynth
{
    /// <summary>The volume the mixer counts as full (<c>Audio::Mixer::kMaxMixerVolume</c>).</summary>
    public const int MaxMixerVolume = 256;

    private const int NumChannels = 6;
    private const int RateScale = 1;
    private const int InternalRate = 7670454;

    private static readonly int[] OperatorOrder = [0, 2, 1, 3];

    private readonly Channel[] _channels = new Channel[NumChannels];
    private readonly ChipTimer[] _timers = [new(), new()];
    private readonly byte[,] _registers = new byte[255, 2];
    private readonly int _outputRate;
    private readonly int _outRateMult;
    private readonly float _predSmpCount;

    private int _volMaskA, _volMaskB;
    private int _volumeA = 255, _volumeB = 255;

    private int[] _renderBuffer = [];
    private int _numPending, _offsPending;
    private int _rateConvCnt;

    /// <summary>Mirrors the constructor and <c>init</c>: the tables, the channels and their operators, and the timers.</summary>
    protected TownsFmSynth(int outputRate)
    {
        _outputRate = outputRate;
        _rateConvCnt = _outRateMult = _outputRate * 72;
        _predSmpCount = (InternalRate / 72.0f) / _outputRate;

        for (int i = 0; i < NumChannels; i++)
        {
            _channels[i] = new Channel();
            for (int j = 0; j < 4; j++)
            {
                _channels[i].Operators[j] = new TownsFmOperator(48 >> RateScale, 0x90);
            }
        }
    }

    /// <summary>The output's rate, which the chip's samples are taken to.</summary>
    public int OutputRate => _outputRate;

    /// <summary>
    /// Clears the channels and their operators and the registers, and stops both timers.
    /// Mirrors <c>reset</c>.
    /// </summary>
    public virtual void Reset()
    {
        foreach (var c in _channels)
        {
            foreach (var o in c.Operators)
            {
                o.Reset();
            }

            c.ClearFeedback();
            c.Algorithm = 0;
            c.EnableLeft = c.EnableRight = true;
            c.UpdateEnvelopeParameters = false;
        }

        Array.Clear(_registers);
        WriteReg(0, 0x27, 0x30);
    }

    /// <summary>Writes a register of one of the chip's two parts. Mirrors <c>writeReg</c>.</summary>
    public void WriteReg(int part, int regAddress, int value)
    {
        if (part > (NumChannels >> 2))
        {
            return;
        }

        WriteRegInternal(part, (byte)regAddress, (byte)value);
    }

    /// <summary>
    /// Fills the buffer with interleaved stereo samples at the output's rate, running the timers'
    /// callbacks on the chip's samples they fall on. Samples made past the end of the buffer wait
    /// for the next call. Mirrors <c>readBuffer</c>, whose sample count is the buffer's length.
    /// </summary>
    public void ReadBuffer(Span<short> buffer)
    {
        int numSamples = buffer.Length;
        int requiredSize = Math.Max(numSamples, (int)(_predSmpCount * numSamples) + 2);
        if (_renderBuffer.Length < requiredSize)
        {
            // samples waiting are lost when the buffer grows, as ScummVM loses them
            if (_numPending != 0)
            {
                _numPending = _offsPending = 0;
            }

            _renderBuffer = new int[requiredSize];
        }

        int outSamplesLeft = numSamples >> 1;
        int inSamplesLeft = (int)(_predSmpCount * outSamplesLeft) + 1;
        int output = 0;

        while (outSamplesLeft != 0)
        {
            int render = inSamplesLeft;

            for (int i = 0; i < 2; i++)
            {
                var t = _timers[i];
                if (t.Enabled)
                {
                    if (t.SmpTillCb == 0)
                    {
                        int spc = i != 0 ? ((0x100 - t.Value) << 4) << RateScale : (0x400 - t.Value) << RateScale;
                        if (spc < 1)
                        {
                            spc = 1;
                        }

                        t.SmpPerCb = spc;
                        t.SmpPerCbRem = (uint)((spc - (float)t.SmpPerCb) * 1000000.0f);

                        if (i == 0)
                        {
                            TimerCallbackA();
                        }
                        else
                        {
                            TimerCallbackB();
                        }

                        t.SmpTillCb = t.SmpPerCb;
                        t.SmpTillCbRem += t.SmpPerCbRem;
                        while (t.SmpTillCbRem >= 1000000)
                        {
                            t.SmpTillCb++;
                            t.SmpTillCbRem -= 1000000;
                        }

                        t.Enabled = (_registers[0x27, 0] & (4 << i)) != 0;
                    }

                    render = Math.Min(render, t.SmpTillCb);
                }
            }

            render = Math.Max(render - (_offsPending + _numPending), 0);
            int pos = _numPending != 0 ? (_offsPending + _numPending) << 1 : 0;

            foreach (var t in _timers)
            {
                if (t.Enabled)
                {
                    t.SmpTillCb -= render;
                }
            }

            if (render != 0)
            {
                // every pass adds its samples to what is there, so the buffer is cleared first
                Array.Clear(_renderBuffer, pos, render << 1);
                NextTick(_renderBuffer, pos, render);
                NextTickEx(_renderBuffer, pos, render);
            }

            pos = _numPending != 0 ? _offsPending << 1 : 0;

            // to the output's rate, here rather than in the mixer, for the timers' sake
            int consumed = 0;
            for (; consumed < render + _numPending && outSamplesLeft != 0; ++consumed)
            {
                for (_rateConvCnt -= _outRateMult; _rateConvCnt <= 0 && outSamplesLeft != 0; --outSamplesLeft)
                {
                    _rateConvCnt += InternalRate;
                    buffer[output++] = RenderedSample(pos + (consumed << 1));
                    buffer[output++] = RenderedSample(pos + (consumed << 1) + 1);
                }
            }

            inSamplesLeft -= render;
            _offsPending = consumed - _numPending;
            _numPending = render + _numPending - consumed;
        }

        buffer[output..].Clear();
    }

    /// <summary>
    /// A rendered sample, clipped. The samples left over from a read are found again where
    /// <c>readBuffer</c> reckons them (<c>_offsPending = consumed - _numPending</c>), which is
    /// before the buffer's start when a read of a few samples is made of fewer of them than were
    /// left over; ScummVM then reads what lies before its buffer, and here that is silence.
    /// </summary>
    private short RenderedSample(int index) => index >= 0 ? (short)Math.Clamp(_renderBuffer[index], -32767, 32767) : (short)0;

    /// <summary>The music and effects volumes, 0 to 256 (<c>setVolumeIntern</c>).</summary>
    protected void SetVolumeIntern(int volA, int volB)
    {
        _volumeA = Math.Clamp(volA, 0, MaxMixerVolume);
        _volumeB = Math.Clamp(volB, 0, MaxMixerVolume);
    }

    /// <summary>Which channels the music volume scales and which the effects volume (<c>setVolumeChannelMasks</c>).</summary>
    protected void SetVolumeChannelMasks(int channelMaskA, int channelMaskB)
    {
        _volMaskA = channelMaskA;
        _volMaskB = channelMaskB;
    }

    /// <summary>Output a driver adds to each pass's samples (<c>nextTickEx</c>).</summary>
    protected virtual void NextTickEx(int[] buffer, int offset, int count)
    {
    }

    /// <summary>Timer A's callback (<c>timerCallbackA</c>).</summary>
    protected abstract void TimerCallbackA();

    /// <summary>Timer B's callback (<c>timerCallbackB</c>).</summary>
    protected abstract void TimerCallbackB();

    /// <summary>Mirrors <c>writeRegInternal</c>: the register and what it sets in the channels, their operators and the timers.</summary>
    private void WriteRegInternal(int part, byte regAddress, byte value)
    {
        _registers[regAddress, part] = value;

        int h = regAddress & 0xf0;
        int l = regAddress & 0x0f;

        int channel = (l & 3) + 3 * part;
        TownsFmOperator? o = null;
        bool checkAddress = false;
        if (regAddress == 0x28)
        {
            channel = (value & 3) + ((value & 4) != 0 ? 3 : 0);
            checkAddress = true;
        }
        else if (h == 0xa0 && (_registers[0x27, 0] & 0x40) != 0 && (l > 7 || l == 2 || l == 6))
        {
            channel = 3 * part + 2;
            o = _channels[channel].Operators[l < 8 ? 3 : 2 - (l & 3)];
        }
        else if (h != 0xa0 && h > 0x20)
        {
            checkAddress = true;
            if ((l & 3) != 3)
            {
                o = _channels[channel].Operators[OperatorOrder[(l & ~3) >> 2]];
            }
        }

        if (checkAddress && (l & 3) == 3)
        {
            // an invalid write: the fourth slot of a group names no channel
            return;
        }

        // the address of a third part's channel names none (ScummVM reads past its channels; nothing here writes one)
        if (channel >= NumChannels)
        {
            return;
        }

        var c = _channels[channel];
        var co = c.Operators;

        switch (h)
        {
            case 0x20:
                if (part != 0)
                {
                }
                else if (l == 8)
                {
                    // key on and off
                    for (int i = 0; i < 4; i++)
                    {
                        if (((value >> (4 + i)) & 1) != 0)
                        {
                            if (i == 0)
                            {
                                c.ClearFeedback();
                            }

                            co[i].KeyOn();
                        }
                        else
                        {
                            co[i].KeyOff();
                        }
                    }
                }
                else if (l == 4)
                {
                    _timers[0].Value = (ushort)((_timers[0].Value & 3) | (value << 2));
                }
                else if (l == 5)
                {
                    _timers[0].Value = (ushort)((_timers[0].Value & 0x3fc) | (value & 3));
                }
                else if (l == 6)
                {
                    _timers[1].Value = value;
                }
                else if (l == 7)
                {
                    if ((value & 1) != 0 && !_timers[0].Enabled)
                    {
                        StartTimer(_timers[0], Math.Max((0x400 - _timers[0].Value) << RateScale, 1));
                    }
                    else if ((value & 1) == 0)
                    {
                        _timers[0].Enabled = false;
                    }

                    if ((value & 2) != 0 && !_timers[1].Enabled)
                    {
                        StartTimer(_timers[1], Math.Max(((0x100 - _timers[1].Value) << 4) << RateScale, 1));
                    }
                    else if ((value & 2) == 0)
                    {
                        _timers[1].Enabled = false;
                    }
                }

                break;

            case 0x30:
                // detune, multiple
                o!.Detune((value >> 4) & 7);
                o.Multiple((uint)(value & 0x0f));
                c.UpdateEnvelopeParameters = true;
                break;

            case 0x40:
                o!.TotalLevel((uint)(value & 0x7f));
                break;

            case 0x50:
                // rate scaling, attack rate
                o!.AttackRate((uint)(value & 0x1f));
                if (o.ScaleRate((byte)(value >> 6)))
                {
                    c.UpdateEnvelopeParameters = true;
                }

                break;

            case 0x60:
                // first decay rate, amplitude modulation
                o!.DecayRate((uint)(value & 0x1f));
                o.AmpModulation((value & 0x80) != 0);
                break;

            case 0x70:
                // second decay rate
                o!.SustainRate((uint)(value & 0x1f));
                break;

            case 0x80:
                // sustain level, release rate
                o!.SustainLevel((uint)(value >> 4));
                o.ReleaseRate((uint)(value & 0x0f));
                break;

            case 0x90:
                o!.EnvelopeShape((uint)(value & 0x0f));
                break;

            case 0xa0:
                // frequency
                l &= ~3;
                if (l == 0)
                {
                    c.UpdateEnvelopeParameters = true;
                    if (o is not null)
                    {
                        o.FrequencyLo(value);
                    }
                    else
                    {
                        foreach (var op in co)
                        {
                            op.FrequencyLo(value);
                        }
                    }
                }
                else if (l == 4)
                {
                    if (o is not null)
                    {
                        o.FrequencyHi(value);
                    }
                    else
                    {
                        foreach (var op in co)
                        {
                            op.FrequencyHi(value);
                        }
                    }
                }
                else if (l == 8 && o is not null)
                {
                    c.UpdateEnvelopeParameters = true;
                    o.FrequencyLo(value);
                }
                else if (l == 12 && o is not null)
                {
                    o.FrequencyHi(value);
                }

                break;

            case 0xb0:
                l &= ~3;
                if (l == 0)
                {
                    // feedback, algorithm
                    co[0].FeedbackLevel((value >> 3) & 7);
                    c.Algorithm = value & 7;
                }
                else if (l == 4)
                {
                    // stereo
                    c.EnableLeft = (value & 0x80) != 0;
                    c.EnableRight = (value & 0x40) != 0;
                }

                break;
        }
    }

    private static void StartTimer(ChipTimer t, int spc)
    {
        t.SmpPerCb = spc;
        t.SmpPerCbRem = (uint)((spc - (float)t.SmpPerCb) * 1000000.0f);
        t.SmpTillCb = t.SmpPerCb;
        t.SmpTillCbRem = t.SmpPerCbRem;
        t.Enabled = true;
    }

    /// <summary>Adds each channel's samples, through its algorithm's connection of its operators. Mirrors <c>nextTick</c>.</summary>
    private void NextTick(int[] buffer, int offset, int count)
    {
        for (int i = 0; i < NumChannels; i++)
        {
            var c = _channels[i];
            var o = c.Operators;
            var feed = c.Feedback;

            if (c.UpdateEnvelopeParameters)
            {
                c.UpdateEnvelopeParameters = false;
                foreach (var op in o)
                {
                    op.UpdatePhaseIncrement();
                }
            }

            for (int ii = 0; ii < count; ii++)
            {
                int phbuf1 = 0, phbuf2 = 0, output = 0;

                switch (c.Algorithm)
                {
                    case 0:
                        o[0].GenerateOutput(0, feed, ref phbuf1);
                        o[2].GenerateOutput(feed[2], null, ref phbuf2);
                        feed[2] = 0;
                        o[1].GenerateOutput(phbuf1, null, ref feed[2]);
                        o[3].GenerateOutput(phbuf2, null, ref output);
                        break;
                    case 1:
                        o[0].GenerateOutput(0, feed, ref phbuf1);
                        o[2].GenerateOutput(feed[2], null, ref phbuf2);
                        o[1].GenerateOutput(0, null, ref phbuf1);
                        o[3].GenerateOutput(phbuf2, null, ref output);
                        feed[2] = phbuf1;
                        break;
                    case 2:
                        o[0].GenerateOutput(0, feed, ref phbuf2);
                        o[2].GenerateOutput(feed[2], null, ref phbuf2);
                        o[1].GenerateOutput(0, null, ref phbuf1);
                        o[3].GenerateOutput(phbuf2, null, ref output);
                        feed[2] = phbuf1;
                        break;
                    case 3:
                        o[0].GenerateOutput(0, feed, ref phbuf2);
                        o[2].GenerateOutput(0, null, ref feed[2]);
                        o[1].GenerateOutput(phbuf2, null, ref phbuf1);
                        o[3].GenerateOutput(feed[2], null, ref output);
                        feed[2] = phbuf1;
                        break;
                    case 4:
                        o[0].GenerateOutput(0, feed, ref phbuf1);
                        o[2].GenerateOutput(0, null, ref phbuf2);
                        o[1].GenerateOutput(phbuf1, null, ref output);
                        o[3].GenerateOutput(phbuf2, null, ref output);
                        feed[2] = 0;
                        break;
                    case 5:
                        o[0].GenerateOutput(0, feed, ref phbuf1);
                        o[2].GenerateOutput(feed[2], null, ref output);
                        o[1].GenerateOutput(phbuf1, null, ref output);
                        o[3].GenerateOutput(phbuf1, null, ref output);
                        feed[2] = phbuf1;
                        break;
                    case 6:
                        o[0].GenerateOutput(0, feed, ref phbuf1);
                        o[2].GenerateOutput(0, null, ref output);
                        o[1].GenerateOutput(phbuf1, null, ref output);
                        o[3].GenerateOutput(0, null, ref output);
                        feed[2] = 0;
                        break;
                    case 7:
                        o[0].GenerateOutput(0, feed, ref output);
                        o[2].GenerateOutput(0, null, ref output);
                        o[1].GenerateOutput(0, null, ref output);
                        o[3].GenerateOutput(0, null, ref output);
                        feed[2] = 0;
                        break;
                }

                // six FM channels and no square-wave ones: (6 + 0 - 3) / 3 is 1
                int finOut = output << 2;

                if (((1 << i) & _volMaskA) != 0)
                {
                    finOut = finOut * _volumeA / MaxMixerVolume;
                }

                if (((1 << i) & _volMaskB) != 0)
                {
                    finOut = finOut * _volumeB / MaxMixerVolume;
                }

                if (c.EnableLeft)
                {
                    buffer[offset + ii * 2] += finOut;
                }

                if (c.EnableRight)
                {
                    buffer[offset + ii * 2 + 1] += finOut;
                }
            }
        }
    }

    /// <summary>A channel: its four operators, the feedback of its first, its algorithm and its sides (<c>ChanInternal</c>).</summary>
    private sealed class Channel
    {
        public readonly TownsFmOperator[] Operators = new TownsFmOperator[4];

        // the first operator's last two outputs, and the algorithms' delayed sample
        public readonly int[] Feedback = new int[3];

        public bool EnableLeft, EnableRight, UpdateEnvelopeParameters;
        public int Algorithm;

        public void ClearFeedback() => Array.Clear(Feedback);
    }

    /// <summary>One of the chip's two timers, counting in the chip's samples (<c>ChipTimer</c>).</summary>
    private sealed class ChipTimer
    {
        public bool Enabled;
        public ushort Value;
        public int SmpTillCb;
        public uint SmpTillCbRem;
        public int SmpPerCb;
        public uint SmpPerCbRem;
    }
}
