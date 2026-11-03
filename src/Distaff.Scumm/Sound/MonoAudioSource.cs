using Distaff.Scumm.Hosting;

namespace Distaff.Scumm.Sound;

/// <summary>
/// Base for synthesisers that render signed 16-bit audio at a fixed rate, mono unless they render
/// stereo themselves. Duplicates mono into the host's interleaved stereo buffer, applies a master
/// gain and counts frames so an engine can tell whether the host is actually consuming audio.
/// </summary>
internal abstract class MonoAudioSource : IAudioSource
{
    private const int UnityGain = 256;

    private short[] _mono = [];
    private long _framesRendered;
    private int _gain = UnityGain;

    protected MonoAudioSource(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sampleRate, 0);
        SampleRate = sampleRate;
    }

    public int SampleRate { get; }

    /// <summary>Master gain from 0 (silent) to 1 (unity). Safe to set from any thread.</summary>
    public float Gain
    {
        get => Volatile.Read(ref _gain) / (float)UnityGain;
        set => Volatile.Write(ref _gain, (int)Math.Clamp(value * UnityGain, 0, UnityGain));
    }

    /// <summary>Total stereo frames rendered so far through <see cref="Read"/>.</summary>
    public long FramesRendered => Volatile.Read(ref _framesRendered);

    public void Read(Span<short> interleavedStereo)
    {
        int frames = interleavedStereo.Length / 2;
        RenderStereo(interleavedStereo[..(frames * 2)]);

        int gain = Volatile.Read(ref _gain);
        if (gain != UnityGain)
        {
            for (int i = 0; i < frames * 2; i++)
            {
                interleavedStereo[i] = (short)((interleavedStereo[i] * gain) >> 8);
            }
        }

        Interlocked.Add(ref _framesRendered, frames);
    }

    /// <summary>Fills the whole buffer with interleaved stereo samples at <see cref="SampleRate"/>; by default the mono samples on both sides.</summary>
    protected virtual void RenderStereo(Span<short> interleavedStereo)
    {
        int frames = interleavedStereo.Length / 2;
        if (_mono.Length < frames)
        {
            _mono = new short[frames];
        }

        var mono = _mono.AsSpan(0, frames);
        RenderMono(mono);
        for (int i = 0; i < frames; i++)
        {
            interleavedStereo[2 * i] = mono[i];
            interleavedStereo[2 * i + 1] = mono[i];
        }
    }

    /// <summary>Fills the whole buffer with mono samples at <see cref="SampleRate"/>; silence unless overridden.</summary>
    protected virtual void RenderMono(Span<short> buffer) => buffer.Clear();
}
