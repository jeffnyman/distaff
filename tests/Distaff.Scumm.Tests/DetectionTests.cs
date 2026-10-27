using Distaff.Scumm.Detection;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>
/// Identifies the game data folders. A test passes without checking when its folder is absent.
/// </summary>
public class DetectionTests
{
    [Theory]
    [InlineData(GameFolders.Ega, "EGA", 3, ResourceLayout.V3Old, "dos", "37f56ceb13e401a7ac7d9e6b37fecaf7")]
    [InlineData(GameFolders.Vga, "VGA", 4, ResourceLayout.V4, "dos", "5d88b9d6a88e6f8e90cded9d01b7f082")]
    [InlineData(GameFolders.FmTowns, "FM-TOWNS", 3, ResourceLayout.V3Old256, "fmtowns", "c5d10e190d4b4d59114b824f2fdbd00e")]
    public void Identifies_each_release(string folder, string variant, int version, ResourceLayout layout, string platform, string md5)
    {
        if (GameFolders.Find(folder) is not { } path)
        {
            return;
        }

        var game = ScummDetector.Identify(new DirectoryGameContainer(path));

        Assert.NotNull(game);
        Assert.Equal(variant, game.Variant);
        Assert.Equal(version, game.Version);
        Assert.Equal(layout, game.Layout);
        Assert.Equal("en", game.Language);
        Assert.Equal(platform, game.Platform);
        Assert.Equal(md5, game.Entry.Md5);
        Assert.False(game.IsDemo);
    }

    [Fact]
    public void An_unknown_index_file_is_not_identified()
    {
        var container = new MemoryGameContainer("other").Add("00.LFL", new byte[5748]);

        Assert.Null(ScummDetector.Identify(container));
        Assert.Equal(new byte[16], ScummDetector.IndexMd5(new MemoryGameContainer("empty")));
    }

    [Fact]
    public void Md5_table_holds_every_scummvm_row()
    {
        Assert.Equal(34, ScummMd5Table.Entries.Length);
        Assert.All(ScummMd5Table.Entries, e => Assert.Equal("loom", e.GameId));
    }
}
