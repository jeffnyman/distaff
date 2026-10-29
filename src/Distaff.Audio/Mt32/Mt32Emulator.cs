using Distaff.Scumm.Hosting;
using Distaff.Scumm.Sound;

namespace Distaff.Audio.Mt32;

/// <summary>
/// The MT-32 / CM-32L emulator as a MIDI device: Munt's synth driven the way ScummVM's
/// MidiDriver_MT32 drives it (audio/softsynth/mt32.cpp) - messages played immediately, Roland data
/// sets written straight to memory, the output gains at ScummVM's default - and its native 32 kHz
/// output converted to the host's rate as ScummVM's mixer converts it.
/// </summary>
public sealed class Mt32Emulator : IMidiDevice
{
    // the mixer's rate converter's fraction (rate.cpp's FRAC_BITS_LOW)
    private const int FracBits = 14;
    private const int FracOne = 1 << FracBits;
    private const int FracHalf = 1 << (FracBits - 1);
    // the synth renders 128 frames at a time, as ScummVM's MidiDriver_MT32 renders it: its rate
    // converter reads 256 frames at a time, which MidiDriver_Emulated splits at its timer's ticks,
    // 250 a second; Munt's output depends on how its rendering is split
    private const int BlockFrames = 128;

    // SysEx commands MidiDriver_MT32::sysEx writes directly
    private const byte SysexCmdDt1 = 0x12;
    private const byte SysexCmdDat = 0x42;

    private readonly Synth _synth;
    private readonly ILog _log;
    private readonly int _step;
    private readonly short[] _block = new short[BlockFrames * 2];
    private int _blockPos = BlockFrames;
    private int _opos = FracOne;
    private int _lastL;
    private int _lastR;
    private int _curL;
    private int _curR;

    // the timer, in the synth's frames, 16.16 (MidiDriver_Emulated's FIXP_SHIFT)
    private const int TimerFracBits = 16;
    private Action? _tick;
    private int _samplesPerTick;
    private int _nextTick;

    /// <summary>Opens the emulator with a pair of ROMs. Throws <see cref="InvalidDataException"/> when the synth refuses them.</summary>
    public Mt32Emulator(Mt32RomSet roms, int outputRate, ILog? log = null)
    {
        ArgumentNullException.ThrowIfNull(roms);
        _log = log ?? NullLog.Instance;
        _synth = new Synth(new LogReportHandler(_log));
        if (!_synth.Open(roms.Control, roms.Pcm))
        {
            throw new InvalidDataException($"The MT-32 emulator could not start with {roms.Control.Info.Description} and {roms.Pcm.Info.Description}");
        }

        // MidiDriver_MT32::open: midi_gain 100 for both outputs, and the messages played at once
        // because ScummVM's MIDI handling is synchronous to sample generation
        _synth.SetOutputGain(1.0f);
        _synth.SetReverbOutputGain(1.0f);
        _synth.MIDIDelayMode = MIDIDelayMode.Immediate;

        OutputRate = outputRate;
        Description = roms.Description;
        _step = (Synth.SampleRate << FracBits) / outputRate;
    }

    public int OutputRate { get; }

    /// <summary>The module emulated, e.g. "CM-32L/LAPC-I Control v1.02".</summary>
    public string Description { get; }

    /// <summary>The 20 characters of the emulated LCD.</summary>
    public string DisplayText
    {
        get
        {
            _synth.GetDisplayState(out string text);
            return text;
        }
    }

    /// <summary>Whether partials are sounding or events are pending; for tests.</summary>
    public bool IsActive => _synth.IsActive();

    /// <summary>Where the pitch timer's jitter comes from (Munt uses rand()); seeded for repeatable runs.</summary>
    public Random Random
    {
        get => _synth.Random;
        set => _synth.Random = value;
    }

    public void Send(uint message) => _synth.PlayMsg(message);

    /// <summary>
    /// A system exclusive message without its F0 and F7. As in MidiDriver_MT32::sysEx, Roland data
    /// sets (DT1 and DAT) are written to the synth's memory directly, without the checksum.
    /// </summary>
    public void SysEx(ReadOnlySpan<byte> message)
    {
        if (message.Length > 0 && message[0] == 0xF0)
        {
            _synth.PlaySysex(message);
            return;
        }

        if (message.Length >= 5 && message[3] is SysexCmdDt1 or SysexCmdDat)
        {
            _synth.WriteSysex(message[1], message[4..^1]);
        }
        else
        {
            _log.Warning("mt32", $"Unused sysEx command {(message.Length > 3 ? message[3] : 0)}");
        }
    }

    /// <summary>Sets a channel's pitch bend range through the patch temp area, as MidiDriver_MT32::setPitchBendRange does.</summary>
    public void SetPitchBendRange(int channel, int range)
    {
        if (range > 24)
        {
            _log.Warning("mt32", $"setPitchBendRange() called with range > 24: {range}");
        }

        _synth.WriteSysex((byte)channel, [0, 0, 4, (byte)range]);
    }

    /// <summary>
    /// The timer, counted in the synth's own frames at 32 kHz: before the frame each tick falls on,
    /// as <c>MidiDriver_Emulated::readBuffer</c> calls it, whose rate is the synth's
    /// (<c>getActualStereoOutputSamplerate</c>).
    /// </summary>
    public void SetTimer(int ticksPerSecond, Action? tick)
    {
        _tick = tick;
        _samplesPerTick = ((Synth.SampleRate / ticksPerSecond) << TimerFracBits) + ((Synth.SampleRate % ticksPerSecond) << TimerFracBits) / ticksPerSecond;
        _nextTick = 0;
    }

    /// <summary>A block of the synth's frames, the timer called where it falls among them.</summary>
    private void RenderBlock()
    {
        if (_tick is null)
        {
            _synth.Render(_block, BlockFrames);
            return;
        }

        int done = 0;
        do
        {
            int step = Math.Min(BlockFrames - done, _nextTick >> TimerFracBits);
            if (step > 0)
            {
                _synth.Render(_block.AsSpan(done * 2), step);
            }

            done += step;
            _nextTick -= step << TimerFracBits;
            if ((_nextTick >> TimerFracBits) == 0)
            {
                _tick();
                _nextTick += _samplesPerTick;
            }
        }
        while (done < BlockFrames);
    }

    /// <summary>
    /// The synth's output at the output's rate, as ScummVM's mixer converts a stream's rate: copied
    /// at the same rate, and otherwise interpolated linearly with a 14-bit fraction, rounded,
    /// starting a frame in (<c>RateConverter_Impl::copyConvert</c> and <c>interpolateConvert</c>).
    /// </summary>
    public void Render(Span<short> interleavedStereo)
    {
        int frames = interleavedStereo.Length / 2;
        if (OutputRate == Synth.SampleRate)
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
        if (_blockPos == BlockFrames)
        {
            RenderBlock();
            _blockPos = 0;
        }

        _lastL = _curL;
        _lastR = _curR;
        _curL = _block[_blockPos * 2];
        _curR = _block[_blockPos * 2 + 1];
        _blockPos++;
    }

    /// <summary>Sends the LCD messages and debug output to the log.</summary>
    private sealed class LogReportHandler(ILog log) : ReportHandler
    {
        public override void PrintDebug(string message) => log.Debug("mt32", message);

        public override void ShowLCDMessage(string message) => log.Info("mt32", $"LCD: {message}");
    }
}
