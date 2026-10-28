using System.Diagnostics;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Hosting;
using Distaff.Scumm.Input;
using Distaff.Scumm.Storage;
using SDL;
using static SDL.SDL3;

namespace Distaff.Player.Sdl;

public sealed class SdlHostOptions
{
    public string Title { get; init; } = "Distaff";

    /// <summary>Integer scale factor for the window's initial size; the window can be resized afterwards.</summary>
    public int Scale { get; init; } = 3;

    /// <summary>Starts in full screen. Alt+Enter and F11 switch between full screen and the window at any time.</summary>
    public bool Fullscreen { get; init; }

    /// <summary>
    /// Shows 320x200 and 640x400 frames at 4:3, as a CRT did, instead of with square pixels.
    /// Ctrl+Alt+A switches it at any time.
    /// </summary>
    public bool AspectRatioCorrection { get; init; }

    public int SampleRate { get; init; } = 44100;

    /// <summary>
    /// Master volume over everything the game plays, 0 to <see cref="AudioVolume.Unity"/>, full by
    /// default: the game's music and effects play at ScummVM's volumes of their own
    /// (<c>SoundSystem.MusicVolume</c> and <c>SfxVolume</c>). Ctrl+Alt+Up and Ctrl+Alt+Down change
    /// it while playing and Ctrl+Alt+M mutes.
    /// </summary>
    public int Volume { get; init; } = AudioVolume.Unity;

    public ILog Log { get; init; } = NullLog.Instance;

    public ISaveStore Saves { get; init; } = new MemorySaveStore();
}

/// <summary>
/// Desktop host: one SDL window, keyboard and mouse input, and audio output. Owns the main loop
/// that drives an <see cref="Engine"/> through <see cref="Engine.Tick"/>.
/// </summary>
public sealed class SdlHost : IHost, IDisposable
{
    private readonly SdlGraphicsDriver _graphics;
    private readonly SdlInputDriver _input;
    private readonly SdlAudioDriver _audio;
    private bool _disposed;

    public SdlHost(SdlHostOptions? options = null)
    {
        options ??= new SdlHostOptions();
        Log = options.Log;
        Saves = options.Saves;

        SdlException.ThrowIfFalse(
            SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO | SDL_InitFlags.SDL_INIT_AUDIO | SDL_InitFlags.SDL_INIT_EVENTS),
            "SDL_Init");

        _graphics = new SdlGraphicsDriver(options.Title, options.Scale, options.Fullscreen, options.AspectRatioCorrection);
        _audio = new SdlAudioDriver(options.SampleRate) { Volume = AudioVolume.Clamp(options.Volume) };
        _input = new SdlInputDriver(_graphics, _audio);
    }

    public IGraphicsDriver Graphics => _graphics;

    public IInputDriver Input => _input;

    public IAudioDriver Audio => _audio;

    public ISaveStore Saves { get; }

    public ILog Log { get; }

    /// <summary>Longest the loop will sleep between ticks, so input stays responsive whatever the engine asks for.</summary>
    public TimeSpan MaxSleep { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Runs the engine until it quits, the window is closed, <paramref name="maxTicks"/> ticks have
    /// run, or <paramref name="duration"/> has passed. Returns the number of ticks executed.
    /// </summary>
    public long Run(Engine engine, int? maxTicks = null, TimeSpan? duration = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ObjectDisposedException.ThrowIf(_disposed, this);

        engine.Initialize();

        long ticks = 0;
        long start = Stopwatch.GetTimestamp();
        long last = start;
        var schedule = new TickSchedule(() => Stopwatch.GetElapsedTime(start)) { MaxSleep = MaxSleep };
        while (true)
        {
            _input.Pump();

            long now = Stopwatch.GetTimestamp();
            var elapsed = ticks == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(last, now);
            last = now;

            var result = engine.Tick(elapsed);
            ticks++;

            _audio.Pump();

            if (!result.Continue || _input.QuitRequested || (maxTicks.HasValue && ticks >= maxTicks.Value)
                || (duration.HasValue && Stopwatch.GetElapsedTime(start) >= duration.Value))
            {
                break;
            }

            // the next tick is due on the schedule, never early; the input and the audio are
            // pumped between the slices of a long sleep
            schedule.Next(result.Delay);
            while (schedule.SleepMilliseconds() is var sleep && sleep > 0)
            {
                SDL_Delay((uint)sleep);
                _input.Pump();
                _audio.Pump();
                if (_input.QuitRequested)
                {
                    break;
                }
            }
        }

        Log.Info("host", $"Engine stopped after {ticks} ticks.");
        return ticks;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _audio.Dispose();
        _graphics.Dispose();
        SDL_Quit();
    }
}
