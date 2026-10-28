using Distaff.Scumm.Game;

namespace Distaff.Scumm.Tests;

/// <summary>Steps the world a game frame at a time.</summary>
internal static class Frames
{
    /// <summary>
    /// One frame of the game: the steps of the FM Towns' scroll while it waits for the frame, the
    /// frame's step, and the steps after it that show a screen effect the frame drew.
    /// </summary>
    public static void StepFrame(this GameWorld world)
    {
        int frames = world.Frames;
        while (world.Frames == frames)
        {
            world.Step();
        }

        while (world.InEffect)
        {
            world.Step();
        }
    }
}
