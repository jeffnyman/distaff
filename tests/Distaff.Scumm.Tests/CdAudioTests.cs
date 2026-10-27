using Distaff.Audio;
using Distaff.Scumm.Game;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Sound;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>The CD's audio played from a track file: the part of the track asked for, its loops, its rate, and the VGA CD's own track. Tests against the game data pass without checking when it is absent.</summary>
public class CdAudioTests
{
    /// <summary>
    /// A part of a track plays from its start to its end in CD frames, 588 sample frames each at
    /// 44100 Hz, once, then the player stops and is silent.
    /// </summary>
    [Fact]
    public void A_part_of_a_track_plays_once_from_its_start_to_its_end()
    {
        using var cd = new CdPlayer(_ => new RampTrack(44100, 2, 100_000), 44100);
        cd.Play(1, 10, 2, 1);
        Assert.True(cd.IsPlaying);
        short[] out1 = Read(cd, 2 * 588 + 10);
        Assert.Equal(Ramp(10 * 588), out1[0]);
        Assert.Equal(Ramp(10 * 588), out1[1]);
        Assert.Equal(Ramp(12 * 588 - 1), out1[(2 * 588 - 1) * 2]);
        Assert.All(out1[(2 * 588 * 2)..], s => Assert.Equal(0, s));
        Assert.False(cd.IsPlaying);
    }

    /// <summary>A part played -1 times goes round for ever; 0 and 1 times both play it once (<c>DefaultAudioCDManager::play</c>).</summary>
    [Fact]
    public void A_part_loops_for_ever_at_minus_one()
    {
        using var cd = new CdPlayer(_ => new RampTrack(44100, 2, 100_000), 44100);
        cd.Play(1, 1, 1, -1);
        short[] out1 = Read(cd, 3 * 588);
        Assert.Equal(Ramp(588), out1[588 * 2]);
        Assert.Equal(Ramp(588), out1[2 * 588 * 2]);
        Assert.True(cd.IsPlaying);

        cd.Play(1, 1, 1, 0);
        Read(cd, 588);
        Assert.False(cd.IsPlaying);
    }

    /// <summary>Played from frame 0 no times, nothing plays; a track that cannot be opened is silent; a stop silences at once.</summary>
    [Fact]
    public void Nothing_plays_without_a_track_or_after_a_stop()
    {
        using var cd = new CdPlayer(t => t == 1 ? new RampTrack(44100, 2, 100_000) : null, 44100);
        cd.Play(1, 0, 10, 0);
        Assert.False(cd.IsPlaying);
        cd.Play(2, 5, 10, 1);
        Assert.False(cd.IsPlaying);
        cd.Play(1, 5, 10, 1);
        cd.Stop();
        Assert.All(Read(cd, 100), s => Assert.Equal(0, s));
    }

    /// <summary>A mono track plays in both channels; a track at half the output's rate is drawn out between its samples (<c>LinearRateConverter</c>).</summary>
    [Fact]
    public void Mono_and_slower_tracks_are_converted()
    {
        using var mono = new CdPlayer(_ => new RampTrack(44100, 1, 100_000), 44100);
        mono.Play(1, 1, 1, 1);
        short[] m = Read(mono, 2);
        Assert.Equal([Ramp(588), Ramp(588), Ramp(589), Ramp(589)], m);

        using var slow = new CdPlayer(_ => new RampTrack(22050, 2, 100_000), 44100);
        slow.Play(1, 2, 1, 1);
        short[] s = Read(slow, 3);
        Assert.Equal(Ramp(588), s[0]);
        Assert.Equal((Ramp(588) + Ramp(589)) / 2, s[2], tolerance: 1);
        Assert.Equal(Ramp(589), s[4]);
    }

    /// <summary>Sources mixed add up, clipped at the ends of 16 bits.</summary>
    [Fact]
    public void A_mix_adds_and_clips()
    {
        var mix = new AudioMix(new Constant(30000), new Constant(10000), new Constant(-5000));
        var buffer = new short[4];
        mix.Read(buffer);
        Assert.All(buffer, s => Assert.Equal(short.MaxValue - 5000, s));
    }

    /// <summary>A track's file is <c>track1</c>, <c>track01</c>, <c>track_1</c> or <c>track_01</c> in that order, in any case (<c>fillPotentialTrackNames</c>).</summary>
    [Fact]
    public void Track_files_are_found_by_scummvms_names()
    {
        string folder = Directory.CreateTempSubdirectory("distaff-cd").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "TRACK_02.OGG"), []);
            File.WriteAllBytes(Path.Combine(folder, "Track02.ogg"), []);
            File.WriteAllBytes(Path.Combine(folder, "track3.wav"), []);
            Assert.Equal("Track02.ogg", Path.GetFileName(CdTrackFiles.Find(folder, 2)));
            Assert.Null(CdTrackFiles.Find(folder, 1));
            Assert.Null(CdTrackFiles.Find(folder, 3));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// The VGA CD's one track: Ogg Vorbis at 44100 Hz in stereo. The proficiency screen's first
    /// line (from frame 22 for 185) plays sample for sample as the track decodes there, and stops
    /// at its end.
    /// </summary>
    [Fact]
    public void The_vga_cds_track_plays_the_part_asked_for()
    {
        if (GameFolders.Find(GameFolders.Vga) is not { } folder)
        {
            return;
        }

        using (var track = CdTrackFiles.Open(folder, 1)!)
        {
            Assert.Equal((44100, 2), (track.SampleRate, track.Channels));
            Assert.True(track.Frames > 44100L * 60 * 30);
        }

        const int Frames = 185 * 588;
        var expected = new float[Frames * 2];
        using (var track = CdTrackFiles.Open(folder, 1)!)
        {
            track.Seek(22 * 588);
            int read = 0;
            while (read < Frames)
            {
                read += track.Read(expected.AsSpan(read * 2));
            }
        }

        using var cd = new CdPlayer(CdTrackFiles.In(folder), 44100);
        cd.Play(1, 22, 185, 1);
        short[] played = Read(cd, Frames + 100);
        Assert.Equal(expected.Select(f => (short)Math.Clamp((int)MathF.Floor(f * 32767f + 0.5f), short.MinValue, short.MaxValue)), played[..(Frames * 2)]);
        Assert.Contains(played[..(Frames * 2)], s => Math.Abs((int)s) > 8000);
        Assert.All(played[(Frames * 2)..], s => Assert.Equal(0, s));
        Assert.False(cd.IsPlaying);
    }

    /// <summary>The game's <c>SO_SAY_VOICE</c> plays the track through the world's sound, and its output carries it.</summary>
    [Fact]
    public void The_proficiency_screen_speaks_from_the_track()
    {
        if (GameFolders.Find(GameFolders.Vga) is not { } folder)
        {
            return;
        }

        var world = new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(folder)), cdTracks: CdTrackFiles.In(folder));
        world.Boot();
        int peak = 0;
        var buffer = new short[GameWorld.DefaultSampleRate / 60 * 2];
        for (int i = 0; i < 60 && !world.Sound.Cd.IsPlaying; i++)
        {
            world.Step();
        }

        Assert.Equal((1, 22, 185), world.CdTrack);
        Assert.True(world.Sound.Cd.IsPlaying);
        for (int i = 0; i < 60; i++)
        {
            world.Sound.Output.Read(buffer);
            peak = Math.Max(peak, buffer.Max(s => Math.Abs((int)s)));
        }

        Assert.True(peak > 8000);
        world.Sound.Dispose();
    }

    private static short[] Read(CdPlayer cd, int frames)
    {
        var buffer = new short[frames * 2];
        cd.Read(buffer);
        return buffer;
    }

    /// <summary>The value the ramp track holds at a frame, as played.</summary>
    private static short Ramp(int frame) => (short)(frame % 30000);

    /// <summary>A track whose every sample is its frame number (mod 30000) over 32767, so a sample played says where it came from.</summary>
    private sealed class RampTrack(int rate, int channels, long frames) : ICdTrack
    {
        private long _at;

        public int SampleRate => rate;

        public int Channels => channels;

        public long Frames => frames;

        public void Seek(long frame) => _at = frame;

        public int Read(Span<float> interleaved)
        {
            int n = (int)Math.Min(interleaved.Length / channels, frames - _at);
            for (int i = 0; i < n; i++, _at++)
            {
                for (int c = 0; c < channels; c++)
                {
                    interleaved[i * channels + c] = (_at % 30000) / 32767f;
                }
            }

            return n;
        }

        public void Dispose()
        {
        }
    }

    private sealed class Constant(short value) : Hosting.IAudioSource
    {
        public void Read(Span<short> interleavedStereo) => interleavedStereo.Fill(value);
    }
}
