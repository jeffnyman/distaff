using System.Security.Cryptography;
using Distaff.Audio;

namespace Distaff.Scumm.Tests;

/// <summary>The FLAC decoder, on the FM Towns release's CD tracks. Tests against the game data pass without checking when it is absent.</summary>
public class FlacTrackTests
{
    /// <summary>
    /// Track 1 decodes whole, at 44100 Hz in stereo, to the samples of the WAV copy of the CD's
    /// track: its first 7073792 frames, as 16-bit samples, hash the same. The FLAC file ends there,
    /// a frame short of the 7076580 its stream information gives; the WAV's last 2788 are silence.
    /// </summary>
    [Fact]
    public void A_track_decodes_to_the_cds_samples()
    {
        if (Open(1) is not { } track)
        {
            return;
        }

        using (track)
        {
            Assert.Equal((44100, 2, 7076580L), (track.SampleRate, track.Channels, track.Frames));
            var samples = Samples(track, track.Frames);
            Assert.Equal(7073792 * 2, samples.Length);
            Assert.Equal("93d330e27bcbcd60", Hash(samples));
        }
    }

    /// <summary>A seek lands on the sample asked for, whether ahead of what was decoded, behind it, or inside a frame.</summary>
    [Fact]
    public void A_seek_lands_on_its_sample()
    {
        if (Open(1) is not { } whole || Open(1) is not { } seeking)
        {
            return;
        }

        using (whole)
        using (seeking)
        {
            var all = Samples(whole, 44100 * 30);
            foreach (long at in (long[])[44100 * 20 + 123, 4608, 44100 * 29, 0, 1])
            {
                seeking.Seek(at);
                var part = Samples(seeking, 1000);
                Assert.Equal(all.AsSpan((int)at * 2, 2000).ToArray(), part);
            }
        }
    }

    /// <summary>The track files are looked for as ScummVM does, FLAC before Ogg Vorbis (<c>STREAM_FILEFORMATS</c>).</summary>
    [Fact]
    public void Flac_is_taken_before_ogg_vorbis()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"distaff-flac-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "track1.ogg"), []);
            File.WriteAllBytes(Path.Combine(folder, "track1.flac"), []);
            File.WriteAllBytes(Path.Combine(folder, "track2.fla"), []);
            Assert.Equal("track1.flac", Path.GetFileName(CdTrackFiles.Find(folder, 1)));
            Assert.Equal("track2.fla", Path.GetFileName(CdTrackFiles.Find(folder, 2)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Reads frames as 16-bit samples, rounded as the CD player rounds them, which gives back ScummVM's FLAC stream's.</summary>
    private static short[] Samples(FlacTrack track, long frames)
    {
        var floats = new float[frames * 2];
        int read = 0;
        while (read < frames)
        {
            int got = track.Read(floats.AsSpan(read * 2));
            if (got == 0)
            {
                break;
            }

            read += got;
        }

        return [.. floats.Take(read * 2).Select(f => (short)Math.Clamp((int)MathF.Floor(f * 32767f + 0.5f), short.MinValue, short.MaxValue))];
    }

    private static string Hash(short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return Convert.ToHexStringLower(SHA256.HashData(bytes))[..16];
    }

    private static FlacTrack? Open(int number)
        => GameFolders.Find(GameFolders.FmTowns) is { } path && Path.Combine(path, $"Track{number}.flac") is var file && File.Exists(file)
            ? new FlacTrack(File.OpenRead(file))
            : null;
}
