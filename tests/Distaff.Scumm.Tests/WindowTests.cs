using System.Diagnostics;
using Distaff.Audio;
using Distaff.Player.Sdl;
using Distaff.Scumm.Detection;
using Distaff.Scumm.Hosting;
using Distaff.Scumm.Storage;
using static SDL.SDL3;

namespace Distaff.Scumm.Tests;

/// <summary>
/// The window host, run by SDL offscreen with its dummy sound card: the game in it keeps the
/// clock's pace. Tests against the game data pass without checking when it is absent.
/// </summary>
public class WindowTests
{
    /// <summary>
    /// Each release played in the window for three seconds plays no more than three seconds of game
    /// time (the window once ran the game at twice its speed, and the sound with it), and not much
    /// less.
    /// </summary>
    [Theory]
    [InlineData(GameFolders.Ega)]
    [InlineData(GameFolders.Vga)]
    [InlineData(GameFolders.FmTowns)]
    public void The_window_keeps_the_games_pace(string folder)
    {
        if (GameFolders.Find(folder) is not { } path)
        {
            return;
        }

        var game = new DirectoryGameContainer(path);
        var release = ScummDetector.Identify(game)!;
        SDL_SetHint(SDL_HINT_VIDEO_DRIVER, "offscreen");
        SDL_SetHint(SDL_HINT_AUDIO_DRIVER, "dummy");
        using var host = new SdlHost();
        var detected = new DetectedGame("scumm", "loom", "Loom", release.Variant, release.Language, release.Platform);
        // Practice, past the FM Towns' dialog, whose wait is no game time
        using var engine = new ScummEngine(host, game, detected) { BootParam = 0, CdTracks = CdTrackFiles.In(path) };
        var clock = Stopwatch.StartNew();
        host.Run(engine, duration: TimeSpan.FromSeconds(3));
        double wall = clock.Elapsed.TotalSeconds, played = engine.World.Time.TotalSeconds;
        Assert.True(played <= wall * 1.02 + 0.1, $"{played:F2}s of game time in {wall:F2}s");
        Assert.True(played >= wall * 0.7 - 0.5, $"{played:F2}s of game time in {wall:F2}s");
    }
}
