using Distaff.Audio;
using Distaff.Scumm.Game;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>
/// ScummVM's subtitles setting on the VGA CD: off, a line is not drawn while the CD plays, though it
/// stays up as long (Loom's case in <c>CHARSET_1</c>). Passes without checking when the game or its
/// track is absent.
/// </summary>
public class SubtitlesTests
{
    /// <summary>
    /// The proficiency screen's spoken line, with the subtitles on and off: the same line, up as
    /// long, drawn only with them on.
    /// </summary>
    [Fact]
    public void Without_subtitles_the_lines_the_cd_says_are_not_drawn()
    {
        if (GameFolders.Find(GameFolders.Vga) is not { } path || CdTrackFiles.Find(path, 1) is null)
        {
            return;
        }

        GameWorld World(bool subtitles)
        {
            var world = new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path)), cdTracks: CdTrackFiles.In(path)) { Subtitles = subtitles };
            world.Boot();
            return world;
        }

        var on = World(true);
        var off = World(false);
        var before = on.Screen().Pixels.ToArray();
        for (int i = 0; i < 100 && on.MessageText.Length == 0; i++)
        {
            before = on.Screen().Pixels.ToArray();
            on.Step();
            off.Step();
        }

        Assert.Equal("Please choose your skill level.", on.MessageText);
        Assert.Equal(on.MessageText, off.MessageText);
        Assert.True(off.Sound.Cd.IsPlaying);
        Assert.NotEqual(before, on.Screen().Pixels.ToArray());
        Assert.Equal(before, off.Screen().Pixels.ToArray());

        for (int i = 0; i < 2000 && on.MessageText.Length > 0; i++)
        {
            on.Step();
            off.Step();
            Assert.Equal(on.MessageText, off.MessageText);
        }

        Assert.Equal("", off.MessageText);
        on.Sound.Dispose();
        off.Sound.Dispose();
    }
}
