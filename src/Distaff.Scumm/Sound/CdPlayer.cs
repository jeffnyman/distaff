using Distaff.Scumm.Hosting;

namespace Distaff.Scumm.Sound;

/// <summary>
/// Plays part of a CD track from a file, as ScummVM does when the disc's audio is in the game's
/// folder: <c>DefaultAudioCDManager::play</c> opens the track and plays it from a start to an end
/// in CD frames (75 a second) a number of times (<c>makeLoopingAudioStream</c> over a
/// <c>SubSeekableAudioStream</c>), and the mixer converts its rate to the output's
/// (<c>LinearRateConverter</c>). Without a way to open tracks, or without the track, it is silent.
/// The game thread starts and stops it while the host's audio thread reads it.
/// </summary>
public sealed class CdPlayer(Func<int, ICdTrack?>? openTrack, int outputRate) : IAudioSource, IDisposable
{
    /// <summary>CD frames a second.</summary>
    public const int FramesPerSecond = 75;

    private const int Chunk = 1024;

    private readonly Lock _lock = new();
    private readonly float[] _decoded = new float[Chunk * 2];
    private ICdTrack? _track;
    private int _trackNumber;
    private bool _playing;
    private long _start, _end, _position;
    private int _loops, _iterations;
    private (int Track, int Start, int Duration, int NumLoops) _asked;

    // what was decoded and not yet used, and the rate converter's two frames and its place between them
    private int _decodedAt, _decodedCount;
    private float _prevLeft, _prevRight, _nextLeft, _nextRight;
    private long _step, _frac;

    /// <summary>Whether a part of a track is playing (<c>pollCD</c>).</summary>
    public bool IsPlaying
    {
        get
        {
            lock (_lock)
            {
                return _playing;
            }
        }
    }

    /// <summary>
    /// The part last asked for and whether it still plays, as a save keeps it: what was asked
    /// stays after it stops. Mirrors <c>DefaultAudioCDManager::getStatus</c>.
    /// </summary>
    public (bool Playing, int Track, int Start, int Duration, int NumLoops) Status
    {
        get
        {
            lock (_lock)
            {
                return (_playing, _asked.Track, _asked.Start, _asked.Duration, _asked.NumLoops);
            }
        }
    }

    /// <summary>
    /// Plays a track from <paramref name="startFrame"/> for <paramref name="duration"/> CD frames,
    /// to its end when the duration is 0, <paramref name="numLoops"/> times: 0 and 1 both once, -1
    /// for ever. What was playing stops first. Mirrors <c>DefaultAudioCDManager::play</c>.
    /// </summary>
    public void Play(int track, int startFrame, int duration, int numLoops)
    {
        lock (_lock)
        {
            _playing = false;
            if (numLoops == 0 && startFrame == 0)
            {
                return;
            }

            _asked = (track, startFrame, duration, numLoops);
            if (Open(track) is not { } stream)
            {
                return;
            }

            // convertTimeToStreamPos: to the nearest sample, an even one for stereo
            _start = StreamFrame(startFrame, stream);
            _end = duration != 0 ? StreamFrame(startFrame + duration, stream) : stream.Frames;
            if (_start < 0 || _start >= _end)
            {
                return;
            }

            _loops = numLoops < 1 ? numLoops + 1 : numLoops;
            _iterations = 0;
            stream.Seek(_position = _start);
            _decodedAt = _decodedCount = 0;
            _step = ((long)stream.SampleRate << 16) / outputRate;
            _frac = 0;
            _playing = true;
            if (_step != 1 << 16)
            {
                _playing = NextFrame(out _prevLeft, out _prevRight) && NextFrame(out _nextLeft, out _nextRight);
            }
        }
    }

    /// <summary>Stops the track (<c>stopCD</c>).</summary>
    public void Stop()
    {
        lock (_lock)
        {
            _playing = false;
        }
    }

    public void Read(Span<short> interleavedStereo)
    {
        lock (_lock)
        {
            int i = 0;
            if (_playing)
            {
                i = _step == 1 << 16 ? Copy(interleavedStereo) : Convert(interleavedStereo);
            }

            interleavedStereo[(i * 2)..].Clear();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _playing = false;
            _track?.Dispose();
            _track = null;
        }
    }

    /// <summary>The track, kept open while the same one plays again.</summary>
    private ICdTrack? Open(int track)
    {
        if (_track is not null && _trackNumber == track)
        {
            return _track;
        }

        _track?.Dispose();
        _track = openTrack?.Invoke(track);
        _trackNumber = track;
        return _track;
    }

    /// <summary>
    /// A time in CD frames as a sample frame of the stream: <c>Timestamp::convertToFramerate</c>
    /// to the stream's samples, rounded to the nearest, then down to a whole frame of a stereo stream.
    /// </summary>
    private static long StreamFrame(int cdFrames, ICdTrack stream)
    {
        long rate = (long)stream.SampleRate * stream.Channels;
        long g = Gcd(FramesPerSecond, rate);
        long p = FramesPerSecond / g, q = rate / g;
        long samples = (cdFrames * q + p / 2) / p;
        return samples / stream.Channels;
    }

    private static long Gcd(long a, long b) => b == 0 ? a : Gcd(b, a % b);

    /// <summary>The output's rate is the track's: frames go across as they are.</summary>
    private int Copy(Span<short> output)
    {
        int frames = output.Length / 2;
        int i = 0;
        while (i < frames && _playing && NextFrame(out float left, out float right))
        {
            output[i * 2] = ToSample(left);
            output[i * 2 + 1] = ToSample(right);
            i++;
        }

        return i;
    }

    /// <summary>Linear interpolation between the track's frames at the output's rate (<c>LinearRateConverter</c>).</summary>
    private int Convert(Span<short> output)
    {
        int frames = output.Length / 2;
        int i = 0;
        while (i < frames && _playing)
        {
            float t = _frac / 65536f;
            output[i * 2] = ToSample(_prevLeft + (_nextLeft - _prevLeft) * t);
            output[i * 2 + 1] = ToSample(_prevRight + (_nextRight - _prevRight) * t);
            i++;
            _frac += _step;
            while (_frac >= 1 << 16 && _playing)
            {
                _frac -= 1 << 16;
                (_prevLeft, _prevRight) = (_nextLeft, _nextRight);
                if (!NextFrame(out _nextLeft, out _nextRight))
                {
                    _playing = false;
                }
            }
        }

        return i;
    }

    /// <summary>The next frame of the part playing, from its start again for each further time round; false once it is done.</summary>
    private bool NextFrame(out float left, out float right)
    {
        left = right = 0;
        var track = _track!;
        if (_position >= _end || (_decodedAt == _decodedCount && !Decode(track)))
        {
            if (_loops != 0 && ++_iterations >= _loops)
            {
                _playing = false;
                return false;
            }

            track.Seek(_position = _start);
            _decodedAt = _decodedCount = 0;
            if (!Decode(track))
            {
                _playing = false;
                return false;
            }
        }

        int channels = track.Channels;
        left = _decoded[_decodedAt * channels];
        right = channels == 2 ? _decoded[_decodedAt * channels + 1] : left;
        _decodedAt++;
        if (++_position >= _end && _loops != 0 && _iterations + 1 >= _loops)
        {
            // the last time round is done with its last sample (LoopingAudioStream::endOfData)
            _playing = false;
        }

        return true;
    }

    private bool Decode(ICdTrack track)
    {
        int frames = (int)Math.Min(Chunk, _end - _position);
        _decodedCount = frames > 0 ? track.Read(_decoded.AsSpan(0, frames * track.Channels)) : 0;
        _decodedAt = 0;
        return _decodedCount > 0;
    }

    /// <summary>A decoded sample as 16 bits, rounded and clipped as <c>ov_read</c> does.</summary>
    private static short ToSample(float value) => (short)Math.Clamp((int)MathF.Floor(value * 32767f + 0.5f), short.MinValue, short.MaxValue);
}
