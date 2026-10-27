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

    public ScummEngine(IHost host, IGameContainer game, DetectedGame detected)
        : base(host, game, detected)
    {
    }

    /// <summary>Whether the game's copy protection is kept. Off by default, as in ScummVM. Set before <see cref="Initialize"/>.</summary>
    public bool CopyProtection { get; set; }

    /// <summary>A save to start from instead of the boot script, as ScummVM's launcher loads one. Set before <see cref="Initialize"/>.</summary>
    public byte[]? StartingSave { get; set; }

    /// <summary>The name save files start with (<c>loom.s01</c>). Set before <see cref="Initialize"/>.</summary>
    public string SaveTarget { get; set; } = "loom";

    /// <summary>The device the sounds play on. Set before <see cref="Initialize"/>.</summary>
    public SoundDevice SoundDevice { get; set; } = SoundDevice.AdLib;

    /// <summary>The world being played, from <see cref="Initialize"/> on.</summary>
    public GameWorld World => _world ?? throw new InvalidOperationException("The engine is not initialized.");

    public override void Initialize()
    {
        _world = new GameWorld(ResourceLoader.Open(Game), Host.RandomSeed ?? Environment.TickCount, SoundDevice, Host.Audio.SampleRate)
        {
            CopyProtection = CopyProtection,
            SaveStore = Host.Saves,
            SaveTarget = SaveTarget,
        };
        Host.Graphics.SetMode(GameWorld.ScreenWidth, GameWorld.ScreenHeight);
        Host.Graphics.SetTitle($"Distaff - {Detected.Title}");
        Host.Graphics.SetPalette(Palettes.Ega);
        StartAudio(_world);
        if (StartingSave is { } save)
        {
            _world.Load(save);
        }
        else
        {
            _world.Boot();
        }
    }

    public override void Dispose()
    {
        Host.Audio.Stop();
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

        var frame = world.Screen();
        world.DrawCursor(frame);
        Host.Graphics.SetPalette(world.DisplayPalette());
        Host.Graphics.Present(frame);
        return world.QuitRequested ? TickResult.Quit : TickResult.After(world.StepLength);
    }
}
