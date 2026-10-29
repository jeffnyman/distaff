namespace Distaff.Scumm.Sound;

/// <summary>
/// A MIDI device emulated in software, as ScummVM plays one: its output at its own rate is read
/// 256 frames at a time, as the mixer's rate converter reads a stream (512 samples), each read made
/// in pieces between the ticks of its timer, 250 a second unless set (<c>MidiDriver_Emulated</c>,
/// whose timer runs with or without a callback); the output converted to the output's rate as the
/// mixer converts it, at full volume (<c>RateConverter_Impl</c>: copied at the same rate,
/// interpolated linearly with a 14-bit fraction otherwise). A device's output can depend on how
/// its rendering is split, as Munt's does, so it is split as there.
/// </summary>
public abstract class EmulatedMidiDevice : IMidiDevice
{
    // the mixer's rate converter's fraction (rate.cpp's FRAC_BITS_LOW) and its reads
    private const int FracBits = 14;
    private const int FracOne = 1 << FracBits;
    private const int FracHalf = 1 << (FracBits - 1);
    private const int ReadFrames = 256;

    // the timer, in the device's frames, 16.16 (MidiDriver_Emulated's FIXP_SHIFT)
    private const int TimerFracBits = 16;
    private const int DefaultTimerFrequency = 250;

    private readonly int _nativeRate;
    private readonly int _step;
    private readonly short[] _read = new short[ReadFrames * 2];
    private int _readPos = ReadFrames;
    private int _opos = FracOne;
    private int _lastL, _lastR, _curL, _curR;
    private Action? _tick;
    private int _samplesPerTick;
    private int _nextTick;

    /// <param name="nativeRate">The rate the device renders at.</param>
    /// <param name="outputRate">The rate it is played at.</param>
    protected EmulatedMidiDevice(int nativeRate, int outputRate)
    {
        _nativeRate = nativeRate;
        OutputRate = outputRate;
        _step = (nativeRate << FracBits) / outputRate;
        SetTimer(DefaultTimerFrequency, null);
    }

    public int OutputRate { get; }

    public abstract void Send(uint message);

    public abstract void SysEx(ReadOnlySpan<byte> message);

    /// <summary>Renders <paramref name="frames"/> frames of interleaved stereo at the device's own rate (<c>generateSamples</c>).</summary>
    protected abstract void GenerateSamples(Span<short> interleavedStereo, int frames);

    /// <summary>
    /// The timer, counted in the device's own frames: called before the frame each tick falls on,
    /// as <c>MidiDriver_Emulated::readBuffer</c> calls it.
    /// </summary>
    public void SetTimer(int ticksPerSecond, Action? tick)
    {
        _tick = tick;
        _samplesPerTick = ((_nativeRate / ticksPerSecond) << TimerFracBits) + ((_nativeRate % ticksPerSecond) << TimerFracBits) / ticksPerSecond;
        _nextTick = 0;
    }

    /// <summary>A read of the device's frames, rendered in pieces between the timer's ticks. Mirrors <c>MidiDriver_Emulated::readBuffer</c>.</summary>
    private void Read()
    {
        int done = 0;
        do
        {
            int step = Math.Min(ReadFrames - done, _nextTick >> TimerFracBits);
            if (step > 0)
            {
                GenerateSamples(_read.AsSpan(done * 2), step);
            }

            done += step;
            _nextTick -= step << TimerFracBits;
            if ((_nextTick >> TimerFracBits) == 0)
            {
                _tick?.Invoke();
                _nextTick += _samplesPerTick;
            }
        }
        while (done < ReadFrames);
    }

    /// <summary>
    /// The device's output at the output's rate, as ScummVM's mixer converts a stream's rate: copied
    /// at the same rate, and otherwise interpolated linearly with a 14-bit fraction, rounded,
    /// starting a frame in (<c>RateConverter_Impl::copyConvert</c> and <c>interpolateConvert</c>).
    /// </summary>
    public void Render(Span<short> interleavedStereo)
    {
        int frames = interleavedStereo.Length / 2;
        if (OutputRate == _nativeRate)
        {
            for (int i = 0; i < frames; i++)
            {
                NextFrame();
                interleavedStereo[i * 2] = (short)_curL;
                interleavedStereo[i * 2 + 1] = (short)_curR;
            }

            return;
        }

        for (int i = 0; i < frames; i++)
        {
            while (_opos >= FracOne)
            {
                NextFrame();
                _opos -= FracOne;
            }

            interleavedStereo[i * 2] = (short)(_lastL + (((_curL - _lastL) * _opos + FracHalf) >> FracBits));
            interleavedStereo[i * 2 + 1] = (short)(_lastR + (((_curR - _lastR) * _opos + FracHalf) >> FracBits));
            _opos += _step;
        }
    }

    private void NextFrame()
    {
        if (_readPos == ReadFrames)
        {
            Read();
            _readPos = 0;
        }

        _lastL = _curL;
        _lastR = _curR;
        _curL = _read[_readPos * 2];
        _curR = _read[_readPos * 2 + 1];
        _readPos++;
    }
}
