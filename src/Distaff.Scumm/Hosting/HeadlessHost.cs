using Distaff.Scumm.Graphics;
using Distaff.Scumm.Input;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Hosting;

/// <summary>
/// A host with no window, no audio device and scripted input. Keeps the last presented frame and
/// palette so tests and command line tools can inspect what an engine drew.
/// </summary>
public sealed class HeadlessHost : IHost
{
    /// <summary>A headless host logging to <paramref name="log"/> (nowhere by default) and saving to <paramref name="saves"/> (memory by default).</summary>
    public HeadlessHost(ILog? log = null, ISaveStore? saves = null)
    {
        Log = log ?? NullLog.Instance;
        Saves = saves ?? new MemorySaveStore();
    }

    /// <summary>The display, which keeps the last frame.</summary>
    public HeadlessGraphics Graphics { get; } = new();

    /// <summary>The input, which events are pushed into.</summary>
    public HeadlessInput Input { get; } = new();

    /// <summary>The audio, which plays nothing until pulled.</summary>
    public HeadlessAudio Audio { get; } = new();

    /// <inheritdoc />
    public ISaveStore Saves { get; }

    /// <inheritdoc />
    public ILog Log { get; }

    /// <summary>On by default: a headless run is the same every time, whatever the machine is doing.</summary>
    public bool DeterministicTime { get; set; } = true;

    /// <summary>The games' random numbers follow this seed; null for a different sequence every run.</summary>
    public int? RandomSeed { get; set; } = 1;

    IGraphicsDriver IHost.Graphics => Graphics;

    IInputDriver IHost.Input => Input;

    IAudioDriver IHost.Audio => Audio;

    /// <summary>Default simulated time between ticks.</summary>
    public TimeSpan TickInterval { get; set; } = TimeSpan.FromMilliseconds(1000.0 / 60);

    /// <summary>Initialises the engine and runs up to <paramref name="maxTicks"/> ticks. Returns the number of ticks run.</summary>
    public int Run(Engine engine, int maxTicks)
    {
        engine.Initialize();
        return Continue(engine, maxTicks);
    }

    /// <summary>Runs more ticks on an already initialised engine.</summary>
    public int Continue(Engine engine, int maxTicks)
    {
        int ticks = 0;
        while (ticks < maxTicks)
        {
            var result = engine.Tick(ticks == 0 ? TimeSpan.Zero : TickInterval);
            ticks++;
            if (!result.Continue)
            {
                break;
            }
        }

        return ticks;
    }
}

/// <summary>A display that keeps the last frame presented to it.</summary>
public sealed class HeadlessGraphics : IGraphicsDriver
{
    /// <summary>The framebuffer width the engine set.</summary>
    public int Width { get; private set; }

    /// <summary>The framebuffer height the engine set.</summary>
    public int Height { get; private set; }

    /// <summary>The title the engine set.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>A copy of the most recently presented frame, or null before the first present.</summary>
    public RgbFrame? LastFrame { get; private set; }

    /// <summary>How many frames have been presented.</summary>
    public int PresentCount { get; private set; }

    /// <inheritdoc />
    public void SetMode(int width, int height)
    {
        Width = width;
        Height = height;
        LastFrame = null;
    }

    /// <inheritdoc />
    public void Present(RgbFrame frame)
    {
        LastFrame = frame.Clone();
        PresentCount++;
    }

    /// <inheritdoc />
    public void SetTitle(string title) => Title = title;

    /// <summary>
    /// The last frame as a PNG, enlarged by the scales. Throws
    /// <see cref="InvalidOperationException"/> before the first frame.
    /// </summary>
    public byte[] EncodePng(int scaleX = 1, int scaleY = 1)
    {
        if (LastFrame is null)
        {
            throw new InvalidOperationException("Nothing has been presented yet.");
        }

        return PngWriter.Encode(LastFrame, scaleX, scaleY);
    }
}

/// <summary>Input given by the caller: events pushed in order.</summary>
public sealed class HeadlessInput : IInputDriver
{
    private readonly Queue<InputEvent> _queue = new();

    /// <summary>Where the mouse is and which buttons are down, as <see cref="GetMouseState"/> reports it.</summary>
    public MouseState Mouse { get; set; }

    /// <summary>Queues an event.</summary>
    public void Push(InputEvent inputEvent) => _queue.Enqueue(inputEvent);

    /// <summary>Queues a key press: the key going down, then up.</summary>
    public void PushKey(Key key, KeyModifiers modifiers = KeyModifiers.None, char ascii = default)
    {
        _queue.Enqueue(InputEvent.KeyDown(key, modifiers, ascii));
        _queue.Enqueue(InputEvent.KeyUp(key, modifiers, ascii));
    }

    /// <inheritdoc />
    public bool TryRead(out InputEvent inputEvent) => _queue.TryDequeue(out inputEvent);

    /// <inheritdoc />
    public MouseState GetMouseState() => Mouse;
}

/// <summary>An audio output that plays nothing: the sound is there to be pulled.</summary>
public sealed class HeadlessAudio : IAudioDriver
{
    /// <inheritdoc />
    public int SampleRate => 44100;

    /// <summary>The source started, or null when stopped.</summary>
    public IAudioSource? Source { get; private set; }

    /// <inheritdoc />
    public int Volume { get; set; } = AudioVolume.Default;

    /// <inheritdoc />
    public void Start(IAudioSource source) => Source = source;

    /// <inheritdoc />
    public void Stop() => Source = null;

    /// <summary>Pulls <paramref name="frames"/> stereo frames from the current source, for tests that check sound output.</summary>
    public short[] Pull(int frames)
    {
        var buffer = new short[frames * 2];
        Source?.Read(buffer);
        AudioVolume.Apply(buffer, Volume);
        return buffer;
    }
}
