using Distaff.Scumm.Sound;
using NVorbis;

namespace Distaff.Audio;

/// <summary>A CD track in Ogg Vorbis, decoded by NVorbis (ScummVM's <c>makeVorbisStream</c>, on libvorbis).</summary>
internal sealed class VorbisTrack(Stream stream) : ICdTrack
{
    private readonly VorbisReader _reader = new(stream, closeOnDispose: true);

    public int SampleRate => _reader.SampleRate;

    public int Channels => _reader.Channels;

    public long Frames => _reader.TotalSamples;

    public void Seek(long frame) => _reader.SamplePosition = frame;

    public int Read(Span<float> interleaved) => _reader.ReadSamples(interleaved) / Channels;

    public void Dispose() => _reader.Dispose();
}
