using Distaff.Scumm.Game;

namespace Distaff.Scumm.Tests;

/// <summary>
/// The FM Towns' smooth scrolling, as ScummVM has it by default (<see cref="GameWorld.SmoothScrolling"/>),
/// in the opening, whose camera climbs the cliff after the messenger. Tests against the game data
/// pass without checking when it is absent.
/// </summary>
public class TownsScrollingTests
{
    /// <summary>
    /// The picture falls behind the camera as it climbs, never by more than the 184 pixels the
    /// layer allows and the strip asked for, and is with the camera again once it stops.
    /// </summary>
    [Fact]
    public void The_picture_follows_the_camera_a_pixel_at_a_time()
    {
        if (Boot(smooth: true) is not { } world)
        {
            return;
        }

        int most = 0;
        while (world.Time.TotalSeconds < 31)
        {
            world.Step();
            if (world.CurrentRoomNumber == 2 && !world.InEffect)
            {
                int lag = world.Camera.Left - world.ShownLeft;
                Assert.InRange(lag, -8, 184 + 8);
                most = Math.Max(most, lag);
            }
        }

        Assert.True(most > 100, $"the picture fell {most} pixels behind at most");
        while (world.CurrentRoomNumber == 2)
        {
            world.Step();
        }

        Assert.Equal(world.Camera.Left, world.ShownLeft);
    }

    /// <summary>Without smooth scrolling the picture is where the camera is, a strip at a time.</summary>
    [Fact]
    public void Without_it_the_picture_is_where_the_camera_is()
    {
        if (Boot(smooth: false) is not { } world)
        {
            return;
        }

        while (world.Time.TotalSeconds < 31)
        {
            world.Step();
            Assert.Equal(world.Camera.Left, world.ShownLeft);
        }
    }

    /// <summary>
    /// The game slows to keep with the scroll, and the messenger's room waits until the climb has
    /// scrolled to its end: her first words come 3.6 seconds later than without it. ScummVM's
    /// window, captured through the opening, shows her words and the climb at the smooth timings
    /// (its start a little over 2 seconds behind), not at the others.
    /// </summary>
    [Theory]
    [InlineData(true, 32.91)]
    [InlineData(false, 29.32)]
    public void The_messengers_words_come_when_the_climb_has_scrolled(bool smooth, double seconds)
    {
        if (Boot(smooth) is not { } world)
        {
            return;
        }

        while (world.MessageText != "Rise, son of Cygna.")
        {
            world.Step();
            Assert.True(world.Time.TotalSeconds < 60, "no words");
        }

        Assert.Equal(53, world.CurrentRoomNumber);
        Assert.Equal(seconds, world.Time.TotalSeconds, 2);
    }

    private static GameWorld? Boot(bool smooth)
    {
        if (TownsResourceTests.Open() is not { } loader)
        {
            return null;
        }

        var world = new GameWorld(loader) { SmoothScrolling = smooth };
        world.Boot();
        return world;
    }
}
