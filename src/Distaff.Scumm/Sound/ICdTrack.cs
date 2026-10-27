namespace Distaff.Scumm.Sound;

/// <summary>
/// A track of the game's CD as a host decodes it from a file in the game's folder, as ScummVM's
/// <c>DefaultAudioCDManager</c> plays a <c>track1.ogg</c> or <c>track1.flac</c> in place of the
/// disc. The interpreter takes no decoder of its own; the player supplies one.
/// </summary>
public interface ICdTrack : IDisposable
{
    /// <summary>Sample frames a second.</summary>
    int SampleRate { get; }

    /// <summary>1 or 2.</summary>
    int Channels { get; }

    /// <summary>The track's length in sample frames.</summary>
    long Frames { get; }

    /// <summary>Moves to a sample frame from the start.</summary>
    void Seek(long frame);

    /// <summary>Decodes interleaved samples from -1 to 1 into <paramref name="interleaved"/>, a whole number of frames; returns the frames read, 0 at the end.</summary>
    int Read(Span<float> interleaved);
}
