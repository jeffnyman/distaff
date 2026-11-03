using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Hosting;

/// <summary>
/// What an engine wants the host to do after a tick. <see cref="Delay"/> counts from the moment
/// the host called <see cref="Engine.Tick"/>, not from its return: the time the tick itself took
/// (interpreting, drawing, presenting) is part of the wait, so a host sleeps only the remainder.
/// </summary>
public readonly record struct TickResult(bool Continue, TimeSpan Delay)
{
    /// <summary>Keep running; call <see cref="Engine.Tick"/> again roughly <paramref name="delay"/> after this tick began.</summary>
    public static TickResult After(TimeSpan delay) => new(true, delay);

    /// <summary>Keep running; call <see cref="Engine.Tick"/> again at once.</summary>
    public static TickResult Immediately => new(true, TimeSpan.Zero);

    /// <summary>Stop: the game has ended or the player quit.</summary>
    public static TickResult Quit => new(false, TimeSpan.Zero);
}

/// <summary>
/// Base class for an interpreter. Engines are tick driven: the host calls <see cref="Tick"/>
/// repeatedly and the engine advances as far as it can without blocking, then returns.
/// Engines never sleep, spin, or wait for input inside a tick.
/// </summary>
public abstract class Engine : IDisposable
{
    /// <summary>An engine for a detected game in a container, running on a host.</summary>
    protected Engine(IHost host, IGameContainer game, DetectedGame detected)
    {
        Host = host;
        Game = game;
        Detected = detected;
    }

    /// <summary>The platform the engine runs on.</summary>
    public IHost Host { get; }

    /// <summary>The game's files.</summary>
    public IGameContainer Game { get; }

    /// <summary>What detection found the game to be.</summary>
    public DetectedGame Detected { get; }

    /// <summary>Loads resources and sets the display mode. Called once before the first tick.</summary>
    public abstract void Initialize();

    /// <summary>Advances the game as far as it can without blocking, and says when to call again.</summary>
    /// <param name="elapsed">Wall-clock time since the previous tick, or zero on the first tick.</param>
    public abstract TickResult Tick(TimeSpan elapsed);

    /// <summary>Releases what the engine holds of the host, such as its audio.</summary>
    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
