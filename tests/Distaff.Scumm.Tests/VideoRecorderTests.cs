using Distaff.Scumm.Game;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Hosting;
using Distaff.Scumm.Input;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Sound;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>
/// The video recorder: each frame shown for its step's length at a fixed rate, the sound read
/// over the same time, from world to world, and on while the game is paused; and ffmpeg's encoder,
/// when ffmpeg is to be had. Tests against the game data pass without checking when it is absent.
/// </summary>
public class VideoRecorderTests
{
    /// <summary>
    /// After each step the frame is shown for the step's length, as a window host waits it: the
    /// video's frames and the sound's are the time so far at their rates, and the frame last
    /// written is the screen as the host shows it.
    /// </summary>
    [Fact]
    public void A_recording_holds_each_frame_for_its_step()
    {
        if (Booted() is not { } world)
        {
            return;
        }

        var encoder = new Counting();
        using var recorder = new VideoRecorder(encoder);
        recorder.Record(world);
        var length = TimeSpan.Zero;
        for (int i = 0; i < 600; i++)
        {
            world.Step();
            length += world.StepLength;
        }

        Assert.Equal(length, recorder.Length);
        Assert.Equal(length.Ticks * 60 / TimeSpan.TicksPerSecond, recorder.FramesWritten);
        Assert.Equal(length.Ticks * GameWorld.DefaultSampleRate / TimeSpan.TicksPerSecond, recorder.AudioFramesWritten);
        Assert.Equal(recorder.FramesWritten, encoder.Frames);
        Assert.Equal(2 * recorder.AudioFramesWritten, encoder.Samples);
        Assert.True(encoder.Loud > 0);
        Assert.Equal(world.Frame().Pixels, encoder.Last!.Pixels);

        recorder.Finish();
        Assert.True(encoder.Finished);
    }

    /// <summary>
    /// A walkthrough's next section is a world loaded from the save the one before made: the
    /// recording goes on from where it was, and the world before is recorded no longer.
    /// </summary>
    [Fact]
    public void A_recording_goes_on_from_world_to_world()
    {
        if (Booted() is not { } first || GameFolders.Find(GameFolders.Ega) is not { } path)
        {
            return;
        }

        var encoder = new Counting();
        using var recorder = new VideoRecorder(encoder);
        recorder.Record(first);
        Steps(first, 300);
        var before = recorder.Length;

        var second = new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path)), sound: SoundDevice.AdLib);
        second.Load(first.Save("video", new DateTime(2026, 10, 29)));
        recorder.Record(second);
        Steps(first, 100);
        Assert.Equal(before, recorder.Length);

        var length = before;
        for (int i = 0; i < 300; i++)
        {
            second.Step();
            length += second.StepLength;
        }

        Assert.Equal(length, recorder.Length);
        Assert.Equal(length.Ticks * 60 / TimeSpan.TicksPerSecond, encoder.Frames);
        Assert.Equal(2 * (length.Ticks * GameWorld.DefaultSampleRate / TimeSpan.TicksPerSecond), encoder.Samples);
    }

    /// <summary>The game's time stands while it is paused, but a window still shows the banner: the recording goes on.</summary>
    [Fact]
    public void A_recording_runs_on_while_the_game_is_paused()
    {
        if (Booted() is not { } world)
        {
            return;
        }

        var encoder = new Counting();
        using var recorder = new VideoRecorder(encoder);
        Steps(world, 200);
        recorder.Record(world);
        world.KeyDown(Key.Space, KeyModifiers.None, ' ');
        var time = world.Time;
        Steps(world, 120);

        Assert.Equal(time, world.Time);
        Assert.True(recorder.Length > TimeSpan.FromSeconds(1));
        Assert.Equal(world.Frame().Pixels, encoder.Last!.Pixels);
    }

    /// <summary>
    /// ffmpeg, on the PATH or named by DISTAFF_FFMPEG, puts two seconds of frames and sound
    /// together into an MP4, and leaves nothing else behind. Passes without checking when there is
    /// no ffmpeg.
    /// </summary>
    [Fact]
    public void Ffmpeg_writes_the_video()
    {
        if (Ffmpeg() is not { } ffmpeg)
        {
            return;
        }

        string folder = Directory.CreateTempSubdirectory("distaff-video").FullName;
        try
        {
            string output = Path.Combine(folder, "test.mp4");
            using (var encoder = new FfmpegEncoder(output, scale: 2, ffmpeg: ffmpeg))
            {
                var frame = new RgbFrame(320, 200);
                var tone = new short[GameWorld.DefaultSampleRate / 60 * 2];
                for (int i = 0; i < 120; i++)
                {
                    frame[i, 100] = Rgb.White;
                    encoder.WriteFrame(frame, 1);
                    for (int s = 0; s < tone.Length; s++)
                    {
                        tone[s] = (short)(4000 * Math.Sin(s / 2 * 440.0 * 2 * Math.PI / GameWorld.DefaultSampleRate));
                    }

                    encoder.WriteAudio(tone);
                }

                encoder.Finish();
            }

            Assert.Equal(["test.mp4"], Directory.GetFiles(folder).Select(Path.GetFileName));
            var mp4 = File.ReadAllBytes(output);
            Assert.True(mp4.Length > 1000);
            Assert.Equal("ftyp"u8.ToArray(), mp4[4..8]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>An encoder that keeps count, and the last frame.</summary>
    private sealed class Counting : IVideoEncoder
    {
        public long Frames { get; private set; }

        public long Samples { get; private set; }

        /// <summary>The loudest sample.</summary>
        public int Loud { get; private set; }

        public RgbFrame? Last { get; private set; }

        public bool Finished { get; private set; }

        public void WriteFrame(RgbFrame frame, int count)
        {
            Frames += count;
            Last = frame;
        }

        public void WriteAudio(ReadOnlySpan<short> interleavedStereo)
        {
            Samples += interleavedStereo.Length;
            foreach (short s in interleavedStereo)
            {
                Loud = Math.Max(Loud, Math.Abs((int)s));
            }
        }

        public void Finish() => Finished = true;

        public void Dispose()
        {
        }
    }

    /// <summary>The EGA booted on the AdLib, and PRACTICE chosen on the silent proficiency screen: the title's music starts.</summary>
    private static GameWorld? Booted()
    {
        if (GameFolders.Find(GameFolders.Ega) is not { } path)
        {
            return null;
        }

        var world = new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path)), sound: SoundDevice.AdLib);
        world.Boot();
        Steps(world, 100);
        world.Click(160, 92);
        return world;
    }

    private static void Steps(GameWorld world, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            world.Step();
        }
    }

    private static string? Ffmpeg()
    {
        if (Environment.GetEnvironmentVariable("DISTAFF_FFMPEG") is { Length: > 0 } named)
        {
            return named;
        }

        string exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(dir => Path.Combine(dir, exe)).FirstOrDefault(File.Exists);
    }
}
