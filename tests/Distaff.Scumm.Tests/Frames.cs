using Distaff.Scumm.Game;

namespace Distaff.Scumm.Tests;

/// <summary>Steps the world a game frame at a time.</summary>
internal static class Frames
{
    /// <summary>One frame of the game: a step, and the steps after it that show a screen effect the frame drew.</summary>
    public static void StepFrame(this GameWorld world)
    {
        world.Step();
        while (world.InEffect)
        {
            world.Step();
        }
    }
}
