
namespace Distaff.Scumm.Sound;

/// <summary>
/// What the music players of versions 1 and 2 share: the sound being played and the one queued
/// after it, a tick clock that steps the player's sequencer, and the synthesis of the PC speaker
/// (an 8253 timer driving a square wave, smoothed by a low-pass filter) and of the PCjr (three
/// square-wave voices and a noise generator). Mirrors the parts of ScummVM's <c>Player_V2Base</c>
/// and <c>Player_V2</c> that <c>Player_V1</c> reuses.
/// </summary>
/// <remarks>
/// The game thread starts and stops sounds while the host's audio thread renders samples, so
/// both go through <see cref="Sync"/>, as ScummVM's players lock their mutex.
/// </remarks>
public abstract class SpeakerSynth : MusicPlayer
{
    protected const int FixpShift = 16;

    // noise feedback for white and periodic noise (FB_WNOISE, FB_PNOISE)
    protected const int WhiteNoise = 0x12000, PeriodicNoise = 0x08000;

    // the noise generator's preset (NG_PRESET) and the filter's decay at 30 kHz (SPK_DECAY, PCJR_DECAY)
    private const uint NoisePreset = 0x0F35, Decay = 0xA000;

    private readonly uint _updateStep;
    private readonly uint _decay;
    private readonly int[] _timerCount = new int[4];
    private int _timerOutput;
    private uint _rng = NoisePreset;
    private int _level;
    private uint _nextTick;

    protected SpeakerSynth(int sampleRate, bool pcjr, int ticksPerSecond)
        : base(sampleRate)
    {
        Pcjr = pcjr;
        TickLength = (uint)(((long)sampleRate << FixpShift) / ticksPerSecond);
        _updateStep = (uint)(((long)sampleRate << FixpShift) / ((pcjr ? 111860 : 1193000) * 2));

        // the decay is for 30 kHz or more: it is squared for each halving of the rate below that
        _decay = Decay;
        for (int i = 0; ((long)sampleRate << i) < 30000; i++)
        {
            _decay = _decay * _decay / 65536;
        }

        SetVolume(255);
    }

    /// <summary>Whether this is the PCjr's sound chip rather than the PC speaker.</summary>
    public bool Pcjr { get; }

    protected object Sync { get; } = new();

    /// <summary>The sound playing, or 0. Mirrors <c>_current_nr</c>.</summary>
    protected int CurrentNumber { get; set; }

    /// <summary>The data of the sound playing. Mirrors <c>_current_data</c>.</summary>
    protected byte[]? CurrentData { get; set; }

    /// <summary>The sound to play when this one ends, or 0. Mirrors <c>_next_nr</c>.</summary>
    protected int NextNumber { get; set; }

    protected byte[]? NextData { get; set; }

    /// <summary>Samples between sequencer ticks, in 16.16 fixed point. Mirrors <c>_tick_len</c>.</summary>
    protected uint TickLength { get; set; }

    /// <summary>The volume of each of the 16 attenuation steps, 2 dB apart; the last is silence. Mirrors <c>_volumetable</c>.</summary>
    protected uint[] VolumeTable { get; } = new uint[16];

    /// <summary>Whether a sound plays or waits to. Mirrors <c>Player_V2::getSoundStatus</c>.</summary>
    public override bool IsPlaying(int number)
    {
        lock (Sync)
        {
            return CurrentNumber == number || NextNumber == number;
        }
    }

    /// <summary>Builds the volume table for a volume of 0 to 255. Mirrors <c>Player_V2::setMusicVolume</c>.</summary>
    public void SetVolume(int volume)
    {
        double output = Math.Min(volume, 255) * 128.0 / 3.0;
        for (int i = 0; i < 15; i++)
        {
            VolumeTable[i] = output > 0xFFFF ? 0xFFFF : (uint)output;
            output /= 1.258925412;
        }

        VolumeTable[15] = 0;
    }

    /// <summary>The sequencer's step: what the sound does next.</summary>
    protected abstract void NextTick();

    protected abstract void GenerateSpeakerSamples(Span<short> samples);

    protected abstract void GeneratePcjrSamples(Span<short> samples);

    /// <summary>Plays a sound from its start.</summary>
    protected abstract void ChainSound(int number, byte[]? data);

    /// <summary>The queued sound, if any, starts. Mirrors <c>chainNextSound</c>.</summary>
    protected void ChainNextSound()
    {
        if (NextNumber != 0)
        {
            ChainSound(NextNumber, NextData);
            NextNumber = 0;
            NextData = null;
        }
    }

    /// <summary>Renders, ticking the sequencer between runs of samples. Mirrors <c>Player_V2::readBuffer</c>.</summary>
    protected override void RenderMono(Span<short> buffer)
    {
        lock (Sync)
        {
            while (buffer.Length > 0)
            {
                if ((_nextTick >> FixpShift) == 0)
                {
                    _nextTick += TickLength;
                    try
                    {
                        NextTick();
                    }
                    catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException)
                    {
                        // a program that runs off its sound's data stops it, where the original read on into whatever followed
                        StopSound(CurrentNumber);
                    }
                }

                int step = (int)Math.Min((uint)buffer.Length, _nextTick >> FixpShift);
                if (Pcjr)
                {
                    GeneratePcjrSamples(buffer[..step]);
                }
                else
                {
                    GenerateSpeakerSamples(buffer[..step]);
                }

                buffer = buffer[step..];
                _nextTick -= (uint)step << FixpShift;
            }
        }
    }

    /// <summary>The level an idle speaker has drifted to, for the shortcut when nothing plays.</summary>
    protected int Level => _level;

    /// <summary>Smooths the square waves as the speaker cone does. Mirrors <c>Player_V2::lowPassFilter</c>.</summary>
    protected void LowPassFilter(Span<short> samples)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            _level = unchecked((int)((uint)_level * _decay + (uint)samples[i] * (0x10000 - _decay))) >> 16;
            samples[i] = (short)_level;
        }
    }

    /// <summary>
    /// Adds a timer channel's square wave (or noise, with a feedback) at a divisor and attenuation,
    /// each sample the share of its time the output was high. Mirrors <c>Player_V2::squareGenerator</c>.
    /// </summary>
    protected void SquareGenerator(int channel, int frequency, int volume, int noiseFeedback, Span<short> samples)
    {
        int period = unchecked((int)(_updateStep * (uint)frequency));
        if (period == 0)
        {
            period = (int)_updateStep;
        }

        int bit = 1 << channel;
        long gain = VolumeTable[Math.Clamp(volume, 0, 15)];
        for (int i = 0; i < samples.Length; i++)
        {
            uint duration = 0;
            if ((_timerOutput & bit) != 0)
            {
                duration += (uint)_timerCount[channel];
            }

            _timerCount[channel] -= 1 << FixpShift;
            while (_timerCount[channel] <= 0)
            {
                if (noiseFeedback != 0)
                {
                    if ((_rng & 1) != 0)
                    {
                        _rng ^= (uint)noiseFeedback;
                        _timerOutput ^= bit;
                    }

                    _rng >>= 1;
                }
                else
                {
                    _timerOutput ^= bit;
                }

                if ((_timerOutput & bit) != 0)
                {
                    duration += (uint)period;
                }

                _timerCount[channel] += period;
            }

            if ((_timerOutput & bit) != 0)
            {
                duration -= (uint)_timerCount[channel];
            }

            long sample = samples[i] + ((unchecked((int)(duration - (1u << (FixpShift - 1)))) * gain) >> FixpShift);
            samples[i] = (short)Math.Clamp(sample, short.MinValue, short.MaxValue);
        }
    }

    /// <summary>A silent channel's timer runs down. The part of <c>generatePCjrSamples</c> for a channel that does not sound.</summary>
    protected void IdleTimer(int channel, int samples)
    {
        _timerCount[channel] -= samples << FixpShift;
        if (_timerCount[channel] < 0)
        {
            _timerCount[channel] = 0;
        }
    }

    /// <summary>
    /// A channel playing the same note as an earlier one is put in phase with it, so the two do not
    /// beat. The "HACK" loop of <c>generatePCjrSamples</c>.
    /// </summary>
    protected void SyncPhase(int channel, int earlier)
    {
        _timerCount[channel] = _timerCount[earlier];
        _timerOutput ^= (1 << channel) & (_timerOutput ^ (_timerOutput << (channel - earlier)));
    }
}
