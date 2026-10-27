using Distaff.Scumm.Game;
using Distaff.Scumm.Hosting;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>The EGA release run headless from boot. Each test passes without checking when the game data is absent.</summary>
public class GameLoopTests
{
    /// <summary>Where PRACTICE is on the proficiency screen.</summary>
    private const int PracticeX = 160, PracticeY = 92;

    /// <summary>The boot script sets up the screen and goes to room 69, where the player picks a proficiency level.</summary>
    [Fact]
    public void The_game_boots_to_the_proficiency_screen()
    {
        if (Boot() is not { } world)
        {
            return;
        }

        Run(world, 100);

        Assert.Equal(69, world.CurrentRoomNumber);
        Assert.Equal((0, 144), (world.ViewTop, world.ViewHeight));
        Assert.Contains("PRACTICE", world.MessageLog);
        Assert.Equal(1, world.UserPut);
    }

    /// <summary>
    /// After a level is picked, the copy protection is passed over as in ScummVM, the Lucasfilm
    /// Games title shows, and the opening plays to Bobbin waking on the cliff in room 2.
    /// </summary>
    [Fact]
    public void Picking_a_level_runs_the_title_and_the_opening()
    {
        if (Boot() is not { } world)
        {
            return;
        }

        var rooms = new List<int>();
        Run(world, 100);
        world.Click(PracticeX, PracticeY);
        for (int i = 0; i < 2000; i++)
        {
            world.Step();
            if (rooms.Count == 0 || rooms[^1] != world.CurrentRoomNumber)
            {
                rooms.Add(world.CurrentRoomNumber);
            }
        }

        Assert.Equal([69, 1, 2, 1, 2, 53, 2], rooms);
        Assert.Contains("Rise, son of Cygna.", world.MessageLog);
        Assert.Contains("Better get down to the village.", world.MessageLog.Append(world.MessageText));
        Assert.DoesNotContain(world.Unhandled, u => !u.Contains("drawBox"));
    }

    /// <summary>With the copy protection kept, room 69 asks for a draft from the Book of Patterns.</summary>
    [Fact]
    public void The_copy_protection_can_be_kept()
    {
        if (Boot(copyProtection: true) is not { } world)
        {
            return;
        }

        Run(world, 100);
        world.Click(PracticeX, PracticeY);
        Run(world, 400);

        Assert.Equal(69, world.CurrentRoomNumber);
        Assert.Contains(world.MessageLog, m => m.Contains("Book of Patterns"));
    }

    /// <summary>The engine through the headless host: it boots, steps at the game's pace and presents the screen.</summary>
    [Fact]
    public void The_engine_runs_on_a_host()
    {
        if (GameFolders.Find(GameFolders.Ega) is not { } path)
        {
            return;
        }

        var container = new DirectoryGameContainer(path);
        var detected = new DetectedGame("scumm", "loom", "Loom", "EGA");
        var host = new HeadlessHost();
        using var engine = new ScummEngine(host, container, detected);

        int ticks = host.Run(engine, 50);

        Assert.Equal(50, ticks);
        Assert.Equal(50, host.Graphics.PresentCount);
        Assert.Equal((320, 200), (host.Graphics.LastFrame!.Width, host.Graphics.LastFrame.Height));
        Assert.Equal(69, engine.World.CurrentRoomNumber);
        Assert.Equal(TimeSpan.FromSeconds(6 * 4 / GameWorld.TimerFrequency), engine.World.StepLength);
    }

    private static void Run(GameWorld world, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            world.Step();
        }
    }

    private static GameWorld? Boot(bool copyProtection = false)
    {
        if (GameFolders.Find(GameFolders.Ega) is not { } path)
        {
            return null;
        }

        var world = new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path))) { CopyProtection = copyProtection };
        world.Boot();
        return world;
    }
}
