using Distaff.Scumm.Game;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Hosting;
using Distaff.Scumm.Input;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Sound;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm;

/// <summary>
/// Loom for a host: a step of the world each tick and the screen presented. Mirrors ScummVM's
/// <c>ScummEngine::go</c> loop, which waits a step's ticks of the game's clock between steps.
/// </summary>
public sealed class ScummEngine : Engine
{
    private GameWorld? _world;
    private DifficultyChooser? _chooser;

    public ScummEngine(IHost host, IGameContainer game, DetectedGame detected)
        : base(host, game, detected)
    {
    }

    /// <summary>Whether the game's copy protection is kept. Off by default, as in ScummVM. Set before <see cref="Initialize"/>.</summary>
    public bool CopyProtection { get; set; }

    /// <summary>Whether the FM Towns' picture scrolls smoothly, as ScummVM has it by default (<see cref="GameWorld.SmoothScrolling"/>). Set before <see cref="Initialize"/>.</summary>
    public bool SmoothScrolling { get; set; } = true;

    /// <summary>A save to start from instead of the boot script, as ScummVM's launcher loads one. Set before <see cref="Initialize"/>.</summary>
    public byte[]? StartingSave { get; set; }

    /// <summary>
    /// What the boot script is started with (ScummVM's <c>boot_param</c>), or null for none: for the
    /// FM Towns, the proficiency level, 0 for Practice, 1 for Standard and 2 for Expert. Without it
    /// and without a starting save, the FM Towns asks for the level first, as ScummVM does in a
    /// dialog of its own (<see cref="DifficultyChooser"/>). Set before <see cref="Initialize"/>.
    /// </summary>
    public int? BootParam { get; set; }

    /// <summary>The proficiency level dialog, while it is up.</summary>
    public DifficultyChooser? Chooser => _chooser;

    /// <summary>The name save files start with (<c>loom-vga.s01</c>), or null for the release's own, as ScummVM names it. Set before <see cref="Initialize"/>.</summary>
    public string? SaveTarget { get; set; }

    /// <summary>The device the sounds play on. Set before <see cref="Initialize"/>.</summary>
    public SoundDevice SoundDevice { get; set; } = SoundDevice.AdLib;

    /// <summary>Opens a track of the CD's audio from the game's folder, for the VGA CD. Set before <see cref="Initialize"/>; without it the CD is silent.</summary>
    public Func<int, ICdTrack?>? CdTracks { get; set; }

    /// <summary>The world being played, from <see cref="Initialize"/> on.</summary>
    public GameWorld World => _world ?? throw new InvalidOperationException("The engine is not initialized.");

    public override void Initialize()
    {
        var loader = ResourceLoader.Open(Game);
        _world = new GameWorld(loader, Host.RandomSeed ?? Environment.TickCount, SoundDevice, Host.Audio.SampleRate, CdTracks)
        {
            CopyProtection = CopyProtection,
            SmoothScrolling = SmoothScrolling,
            SaveStore = Host.Saves,
        };
        if (SaveTarget is not null)
        {
            _world.SaveTarget = SaveTarget;
        }

        Host.Graphics.SetMode(GameWorld.ScreenWidth, _world.ScreenHeight);
        Host.Graphics.SetTitle($"Distaff - {Detected.Title}");
        StartAudio(_world);
        if (StartingSave is { } save)
        {
            _world.Load(save);
        }
        else if (BootParam is null && loader.FmTowns)
        {
            var font = loader.LoadCharset(0) is { } data ? Charset.Parse(data) : null;
            _chooser = new DifficultyChooser(font, GameWorld.ScreenWidth, _world.ScreenHeight);
        }
        else
        {
            _world.Boot(BootParam ?? 0);
        }
    }

    public override void Dispose()
    {
        Host.Audio.Stop();
        _world?.Sound.Dispose();
        base.Dispose();
    }

    /// <summary>No audio device is not fatal: the sound clocks itself, so scripts that wait for music still go on.</summary>
    private void StartAudio(GameWorld world)
    {
        try
        {
            Host.Audio.Start(world.Sound.Output);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Host.Log.Warning("distaff.sound", $"Audio output unavailable, running silent: {ex.Message}");
        }
    }

    public override TickResult Tick(TimeSpan elapsed)
    {
        var world = World;
        if (_chooser is { } chooser)
        {
            return Choose(world, chooser);
        }

        while (Host.Input.TryRead(out var input))
        {
            switch (input.Type)
            {
                case InputEventType.Quit:
                    return TickResult.Quit;
                case InputEventType.MouseMove:
                    world.MoveMouse(input.X, input.Y);
                    break;
                case InputEventType.MouseDown when input.Button is MouseButton.Left or MouseButton.Right && !world.Paused:
                    world.Click(input.X, input.Y, right: input.Button == MouseButton.Right);
                    break;
                case InputEventType.KeyDown when world.Paused:
                    world.Paused = false;
                    break;
                case InputEventType.KeyDown:
                    world.KeyDown(input.Key, input.Modifiers, input.Ascii);
                    break;
            }
        }

        if (!world.Paused)
        {
            world.Step();
        }

        Host.Graphics.Present(world.Frame());
        return world.QuitRequested ? TickResult.Quit : TickResult.After(world.StepLength);
    }

    /// <summary>The proficiency level dialog: the mouse and keys go to it until a level is chosen, and the game then boots with it.</summary>
    private TickResult Choose(GameWorld world, DifficultyChooser chooser)
    {
        while (chooser.Chosen is null && Host.Input.TryRead(out var input))
        {
            switch (input.Type)
            {
                case InputEventType.Quit:
                    return TickResult.Quit;
                case InputEventType.MouseMove:
                    chooser.MoveMouse(input.X, input.Y);
                    break;
                case InputEventType.MouseDown when input.Button == MouseButton.Left:
                    chooser.Click(input.X, input.Y);
                    break;
                case InputEventType.KeyDown:
                    chooser.KeyDown(input.Key);
                    break;
            }
        }

        if (chooser.Chosen is { } level)
        {
            _chooser = null;
            world.Boot(level);
            Host.Graphics.Present(world.Frame());
        }
        else
        {
            Host.Graphics.Present(chooser.Frame());
        }

        return TickResult.After(world.StepLength);
    }
}
