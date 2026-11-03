using Distaff.Scumm.Game;
using Distaff.Scumm.Graphics;

namespace Distaff.Scumm.Hosting;

/// <summary>Where a recording goes: the frames, each shown for some frames of the video, and the sound.</summary>
public interface IVideoEncoder : IDisposable
{
    /// <summary>Writes a frame, shown for <paramref name="count"/> frames of the video.</summary>
    void WriteFrame(RgbFrame frame, int count);

    /// <summary>Writes the sound that plays with the frames written, as stereo samples, left and right in turn.</summary>
    void WriteAudio(ReadOnlySpan<short> interleavedStereo);

    /// <summary>Ends the recording and writes it out.</summary>
    void Finish();
}

/// <summary>
/// Records the game as a window host shows and plays it: after each step the frame is shown for
/// the step's length (<see cref="GameWorld.StepLength"/>, as <c>ScummEngine.Tick</c> waits), and
/// the sound is read over the same time, as the audio device pulls it. The video runs at a fixed
/// rate, each of its frames the game's frame showing at its time. The worlds of a walkthrough's
/// sections, each loaded from the save the one before made, are recorded one after another into
/// the same video.
/// </summary>
public sealed class VideoRecorder : IDisposable
{
    /// <summary>The video's frames a second, unless told otherwise.</summary>
    public const int DefaultFrameRate = 60;

    private readonly IVideoEncoder _encoder;
    private readonly int _sampleRate;
    private readonly int _frameRate;
    private readonly bool _cursor;
    private GameWorld? _world;
    private readonly short[] _audio = new short[4096 * 2];

    /// <summary>A recorder writing to an encoder.</summary>
    /// <param name="encoder">Where the video goes.</param>
    /// <param name="sampleRate">The game's sound's rate, in stereo frames a second.</param>
    /// <param name="frameRate">The video's frames a second.</param>
    /// <param name="cursor">Whether the frames show the mouse's cursor.</param>
    public VideoRecorder(IVideoEncoder encoder, int sampleRate = GameWorld.DefaultSampleRate, int frameRate = DefaultFrameRate, bool cursor = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sampleRate, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(frameRate, 0);
        _encoder = encoder;
        _sampleRate = sampleRate;
        _frameRate = frameRate;
        _cursor = cursor;
    }

    /// <summary>The time recorded.</summary>
    public TimeSpan Length { get; private set; }

    /// <summary>The frames of the video written.</summary>
    public long FramesWritten { get; private set; }

    /// <summary>The stereo frames of sound written.</summary>
    public long AudioFramesWritten { get; private set; }

    /// <summary>Records a world's steps from now on, in place of the world recorded before.</summary>
    public void Record(GameWorld world)
    {
        Stop();
        _world = world;
        world.Stepped += Capture;
    }

    /// <summary>Stops recording and writes the video out.</summary>
    public void Finish()
    {
        Stop();
        _encoder.Finish();
    }

    /// <summary>Stops recording and lets the encoder go, without writing the video out unless <see cref="Finish"/> has.</summary>
    public void Dispose()
    {
        Stop();
        _encoder.Dispose();
    }

    private void Stop()
    {
        if (_world is { } world)
        {
            world.Stepped -= Capture;
            _world = null;
        }
    }

    private void Capture(GameWorld world)
    {
        var frame = world.Frame(_cursor);
        Length += world.StepLength;

        long frames = Length.Ticks * _frameRate / TimeSpan.TicksPerSecond;
        if (frames > FramesWritten)
        {
            _encoder.WriteFrame(frame, (int)(frames - FramesWritten));
            FramesWritten = frames;
        }

        long samples = Length.Ticks * _sampleRate / TimeSpan.TicksPerSecond;
        while (samples > AudioFramesWritten)
        {
            int n = (int)Math.Min(_audio.Length / 2, samples - AudioFramesWritten);
            var chunk = _audio.AsSpan(0, n * 2);
            world.Sound.Output.Read(chunk);
            _encoder.WriteAudio(chunk);
            AudioFramesWritten += n;
        }
    }
}
