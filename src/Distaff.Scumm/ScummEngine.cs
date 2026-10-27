using Distaff.Scumm.Game;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Hosting;
using Distaff.Scumm.Input;
using Distaff.Scumm.Resources;
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

    /// <summary>The world being played, from <see cref="Initialize"/> on.</summary>
    public GameWorld World => _world ?? throw new InvalidOperationException("The engine is not initialized.");

    public override void Initialize()
    {
        _world = new GameWorld(ResourceLoader.Open(Game), Host.RandomSeed ?? Environment.TickCount)
        {
            CopyProtection = CopyProtection,
        };
        Host.Graphics.SetMode(GameWorld.ScreenWidth, GameWorld.ScreenHeight);
        Host.Graphics.SetTitle($"Distaff - {Detected.Title}");
        Host.Graphics.SetPalette(Palettes.Ega);
        _world.Boot();
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
