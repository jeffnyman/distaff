using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Distaff.Scumm.Graphics;

namespace Distaff.Scumm.Hosting;

/// <summary>
/// Encodes a recording with ffmpeg, which it runs: the frames go to it as they come, scaled up
/// with square, sharp pixels and encoded as H.264, and the sound to a WAV file beside the
/// output; at the end the two are put together, the sound as AAC, into the output (an MP4, or
/// whatever its extension asks of ffmpeg). The video is the size of its first frame; a later
/// frame of another size is cut or edged in black to it.
/// </summary>
public sealed class FfmpegEncoder : IVideoEncoder
{
    private readonly string _output;
    private readonly string _ffmpeg;
    private readonly int _scale;
    private readonly int _frameRate;
    private readonly int _sampleRate;
    private readonly string _videoPath;
    private readonly string _audioPath;
    private readonly FileStream _audio;
    private readonly StringBuilder _errors = new();
    private Process? _video;
    private byte[] _pixels = [];
    private (int Width, int Height) _size;
    private long _audioBytes;
    private bool _finished;

    /// <param name="output">The video to write.</param>
    /// <param name="scale">How many times larger than the game's screen the video is.</param>
    /// <param name="frameRate">The video's frames a second.</param>
    /// <param name="sampleRate">The sound's rate, in stereo frames a second.</param>
    /// <param name="ffmpeg">The ffmpeg to run: its path, or its name on the PATH.</param>
    public FfmpegEncoder(string output, int scale = 4, int frameRate = VideoRecorder.DefaultFrameRate, int sampleRate = Game.GameWorld.DefaultSampleRate, string ffmpeg = "ffmpeg")
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(scale, 1);
        _output = Path.GetFullPath(output);
        _ffmpeg = ffmpeg;
        _scale = scale;
        _frameRate = frameRate;
        _sampleRate = sampleRate;
        _videoPath = _output + ".video.mkv";
        _audioPath = _output + ".audio.wav";
        _audio = new FileStream(_audioPath, FileMode.Create, FileAccess.ReadWrite);
        _audio.Write(new byte[44]);
    }

    /// <inheritdoc />
    public void WriteFrame(RgbFrame frame, int count)
    {
        _video ??= StartVideo(frame.Width, frame.Height);
        Pack(frame);
        var input = _video.StandardInput.BaseStream;
        try
        {
            for (int i = 0; i < count; i++)
            {
                input.Write(_pixels);
            }
        }
        catch (IOException ex)
        {
            throw Failed("encoding the video", ex);
        }
    }

    /// <inheritdoc />
    public void WriteAudio(ReadOnlySpan<short> interleavedStereo)
    {
        if (!BitConverter.IsLittleEndian)
        {
            var bytes = new byte[interleavedStereo.Length * 2];
            for (int i = 0; i < interleavedStereo.Length; i++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), interleavedStereo[i]);
            }

            _audio.Write(bytes);
        }
        else
        {
            _audio.Write(MemoryMarshal.AsBytes(interleavedStereo));
        }

        _audioBytes += interleavedStereo.Length * 2;
    }

    /// <inheritdoc />
    public void Finish()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        WriteWavHeader();
        _audio.Dispose();
        if (_video is not { } video)
        {
            throw new InvalidOperationException("Nothing was recorded.");
        }

        video.StandardInput.Close();
        video.WaitForExit();
        if (video.ExitCode != 0)
        {
            throw Failed("encoding the video", null);
        }

        // the sound as AAC beside the video as it is, and the index at the front, for playing as it downloads
        Run("-i", _videoPath, "-i", _audioPath, "-map", "0:v", "-map", "1:a", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", _output);
        Delete();
    }

    /// <summary>Stops ffmpeg if it is still running, and removes the files beside the output.</summary>
    public void Dispose()
    {
        _audio.Dispose();
        if (_video is { HasExited: false } video)
        {
            video.Kill();
            video.WaitForExit();
        }

        _video?.Dispose();
        Delete();
    }

    private Process StartVideo(int width, int height)
    {
        _size = (width, height);
        _pixels = new byte[width * height * 3];

        // the frames raw, scaled up by whole pixels; the pixels' sharp edges kept by an encoder tuned for drawn pictures
        return Start(
            "-f", "rawvideo", "-pixel_format", "rgb24", "-video_size", $"{width}x{height}", "-framerate", _frameRate.ToString(CultureInfo.InvariantCulture), "-i", "-",
            "-vf", $"scale=iw*{_scale}:ih*{_scale}:flags=neighbor", "-c:v", "libx264", "-preset", "medium", "-tune", "animation", "-crf", "18", "-pix_fmt", "yuv420p",
            _videoPath);
    }

    private void Pack(RgbFrame frame)
    {
        var (width, height) = _size;
        if (frame.Width != width || frame.Height != height)
        {
            Array.Clear(_pixels);
        }

        int w = Math.Min(width, frame.Width), h = Math.Min(height, frame.Height);
        for (int y = 0; y < h; y++)
        {
            var row = frame.Row(y);
            int at = y * width * 3;
            for (int x = 0; x < w; x++)
            {
                var c = row[x];
                _pixels[at++] = c.R;
                _pixels[at++] = c.G;
                _pixels[at++] = c.B;
            }
        }
    }

    private void WriteWavHeader()
    {
        Span<byte> header = stackalloc byte[44];
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)Math.Min(uint.MaxValue, 36 + _audioBytes));
        "WAVE"u8.CopyTo(header[8..]);
        "fmt "u8.CopyTo(header[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..], 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(header[22..], 2);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], _sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], _sampleRate * 4);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..], 4);
        BinaryPrimitives.WriteInt16LittleEndian(header[34..], 16);
        "data"u8.CopyTo(header[36..]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[40..], (uint)Math.Min(uint.MaxValue, _audioBytes));
        _audio.Position = 0;
        _audio.Write(header);
    }

    private Process Start(params string[] arguments)
    {
        var info = new ProcessStartInfo(_ffmpeg) { RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in (string[])["-hide_banner", "-loglevel", "error", "-y", .. arguments])
        {
            info.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new FfmpegException($"Could not run {_ffmpeg}.");
        }
        catch (Win32Exception ex)
        {
            throw new FfmpegException($"Could not run {_ffmpeg}: recording a video needs ffmpeg, on the PATH or named. {ex.Message}", ex);
        }

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (_errors)
                {
                    _errors.AppendLine(e.Data);
                }
            }
        };
        process.BeginErrorReadLine();
        return process;
    }

    private void Run(params string[] arguments)
    {
        using var process = Start(arguments);
        process.StandardInput.Close();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw Failed("putting the video and its sound together", null);
        }
    }

    private FfmpegException Failed(string doing, Exception? inner)
    {
        lock (_errors)
        {
            return new FfmpegException($"ffmpeg failed {doing}: {_errors.ToString().Trim()}", inner);
        }
    }

    private void Delete()
    {
        File.Delete(_videoPath);
        File.Delete(_audioPath);
    }
}

/// <summary>ffmpeg could not be run, or failed.</summary>
public sealed class FfmpegException(string message, Exception? inner = null) : Exception(message, inner);
