using Distaff.Scumm.Hosting;

namespace Distaff.Scumm.Sound;

/// <summary>
/// Adds sources together, clipping the sum, as ScummVM's mixer does its channels (<c>clampedAdd</c>);
/// paused, it is silent and its sources keep their place (<c>pauseAll</c>).
/// </summary>
public sealed class AudioMix(params IAudioSource[] sources) : IAudioSource
{
    private short[] _buffer = [];
    private volatile bool _paused;

    public bool Paused
    {
        get => _paused;
        set => _paused = value;
    }

    public void Read(Span<short> interleavedStereo)
    {
        if (_paused)
        {
            interleavedStereo.Clear();
            return;
        }

        sources[0].Read(interleavedStereo);
        for (int s = 1; s < sources.Length; s++)
        {
            if (_buffer.Length < interleavedStereo.Length)
            {
                _buffer = new short[interleavedStereo.Length];
            }

            var more = _buffer.AsSpan(0, interleavedStereo.Length);
            sources[s].Read(more);
            for (int i = 0; i < interleavedStereo.Length; i++)
            {
                interleavedStereo[i] = (short)Math.Clamp(interleavedStereo[i] + more[i], short.MinValue, short.MaxValue);
            }
        }
    }
}
