using Distaff.Scumm.Hosting;

namespace Distaff.Scumm.Tests;

/// <summary>When a host's ticks are due (<see cref="TickSchedule"/>), against a clock the tests move.</summary>
public class TickScheduleTests
{
    /// <summary>A frame of a fifteenth of a second, which no whole number of milliseconds makes.</summary>
    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1 / 15.0);

    /// <summary>
    /// A host that works a few milliseconds a tick and wakes up to 2 ms late never starts a tick
    /// before it is due, and does not drift: after a minute of frames the game's time is the clock's.
    /// </summary>
    [Fact]
    public void Ticks_are_never_early_and_lateness_does_not_add_up()
    {
        var clock = TimeSpan.Zero;
        var schedule = new TickSchedule(() => clock);
        var random = new Random(1);
        var game = TimeSpan.Zero;
        for (int tick = 0; tick < 900; tick++)
        {
            Assert.True(clock >= schedule.Due, $"tick {tick} began {(schedule.Due - clock).TotalMilliseconds:F2} ms early");
            Assert.True(clock - schedule.Due < TimeSpan.FromMilliseconds(5), $"tick {tick} began {(clock - schedule.Due).TotalMilliseconds:F2} ms late");
            clock += TimeSpan.FromMilliseconds(random.Next(1, 8));
            schedule.Next(Frame);
            game += Frame;
            while (schedule.SleepMilliseconds() is var sleep && sleep > 0)
            {
                clock += TimeSpan.FromMilliseconds(sleep + random.NextDouble() * 2);
            }
        }

        Assert.InRange((clock - game).TotalMilliseconds, 0, 5);
    }

    /// <summary>A long delay is slept in slices no longer than <see cref="TickSchedule.MaxSleep"/>, and the last part of a millisecond is still slept.</summary>
    [Fact]
    public void Sleeps_are_sliced_and_rounded_up()
    {
        var clock = TimeSpan.Zero;
        var schedule = new TickSchedule(() => clock) { MaxSleep = TimeSpan.FromMilliseconds(50) };
        schedule.Next(TimeSpan.FromMilliseconds(120.3));
        var sleeps = new List<int>();
        while (schedule.SleepMilliseconds() is var sleep && sleep > 0)
        {
            sleeps.Add(sleep);
            clock += TimeSpan.FromMilliseconds(sleep);
        }

        Assert.Equal([50, 50, 21], sleeps);
    }

    /// <summary>A host a little behind runs its ticks back to back until it is on time again.</summary>
    [Fact]
    public void A_host_a_little_behind_catches_up()
    {
        var clock = TimeSpan.Zero;
        var schedule = new TickSchedule(() => clock);
        clock += TimeSpan.FromMilliseconds(200);
        schedule.Next(Frame);
        Assert.Equal(0, schedule.SleepMilliseconds());
        schedule.Next(Frame);
        Assert.Equal(0, schedule.SleepMilliseconds());
        schedule.Next(Frame);
        Assert.Equal(0, schedule.SleepMilliseconds());
        schedule.Next(Frame);
        Assert.True(schedule.SleepMilliseconds() > 0);
        Assert.Equal(4 * Frame, schedule.Due);
    }

    /// <summary>A host far behind (a debugger, a stalled machine) carries on from now instead of hurrying through the frames it missed.</summary>
    [Fact]
    public void A_host_far_behind_carries_on_from_now()
    {
        var clock = TimeSpan.Zero;
        var schedule = new TickSchedule(() => clock);
        clock += TimeSpan.FromSeconds(1);
        schedule.Next(Frame);
        Assert.Equal(clock, schedule.Due);
        Assert.Equal(0, schedule.SleepMilliseconds());
        schedule.Next(Frame);
        Assert.Equal(clock + Frame, schedule.Due);
    }
}
