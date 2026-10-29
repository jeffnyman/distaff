using Distaff.Scumm.Hosting;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>
/// The seed of the game's random numbers, as ScummVM's <c>--random-seed</c>: the engine's when given,
/// over the host's (the headless host's is 1; the window's none, so the clock's).
/// </summary>
public class RandomSeedTests
{
    [Fact]
    public void The_engine_takes_the_seed_given_over_the_hosts()
    {
        if (GameFolders.Find(GameFolders.Ega) is not { } path)
        {
            return;
        }

        int[] Draws(int? engineSeed, int hostSeed)
        {
            var host = new HeadlessHost { RandomSeed = hostSeed };
            using var engine = new ScummEngine(host, new DirectoryGameContainer(path), new DetectedGame("scumm", "loom", "Loom", "EGA")) { RandomSeed = engineSeed };
            host.Run(engine, 300);
            return [.. Enumerable.Range(0, 8).Select(_ => engine.World.Engine.Random.Next(999999))];
        }

        Assert.Equal(Draws(null, 7), Draws(7, 1));
        Assert.NotEqual(Draws(null, 1), Draws(7, 1));
    }
}
