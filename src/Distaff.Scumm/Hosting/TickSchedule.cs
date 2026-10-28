namespace Distaff.Scumm.Hosting;

/// <summary>
/// When a host's next tick is due. Each tick is due when the delays before it have passed since
/// the first, so the time a tick and the host's own work take is part of its delay, and waking
/// late now and then does not add up. No tick may come early, or the game would run ahead of the
/// sound card and the audio skip to keep up: the sleeps it asks for are whole milliseconds rounded
/// up, and a long delay is slept in slices so the host can pump its input and audio between them.
/// A host that has fallen far behind (a debugger, a stalled machine) carries on from now instead
/// of hurrying to catch up.
/// </summary>
public sealed class TickSchedule
{
    /// <summary>How far behind its schedule a host may fall before it carries on from now.</summary>
    public static readonly TimeSpan CatchUpLimit = TimeSpan.FromMilliseconds(250);

    private readonly Func<TimeSpan> _clock;

    /// <param name="clock">The time since the first tick began.</param>
    public TickSchedule(Func<TimeSpan> clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>Longest a sleep may be, so input stays responsive whatever the engine asks for.</summary>
    public TimeSpan MaxSleep { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>When the next tick is due, from the first tick's start.</summary>
    public TimeSpan Due { get; private set; }

    /// <summary>The next tick is due <paramref name="delay"/> after the one just run was.</summary>
    public void Next(TimeSpan delay)
    {
        Due += delay;
        var now = _clock();
        if (now - Due > CatchUpLimit)
        {
            Due = now;
        }
    }

    /// <summary>The whole milliseconds to sleep before looking again, at most <see cref="MaxSleep"/>; 0 once the next tick is due.</summary>
    public int SleepMilliseconds()
    {
        var remaining = Due - _clock();
        if (remaining <= TimeSpan.Zero)
        {
            return 0;
        }

        var sleep = remaining < MaxSleep ? remaining : MaxSleep;
        return Math.Max(1, (int)Math.Ceiling(sleep.TotalMilliseconds));
    }
}
