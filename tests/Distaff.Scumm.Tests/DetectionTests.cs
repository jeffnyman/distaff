using Distaff.Scumm.Detection;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>
/// Identifies the game data folders. A test passes without checking when its folder is absent.
/// </summary>
public class DetectionTests
{
    [Theory]
    [InlineData(GameFolders.Ega, "EGA", 3, nameof(ResourceLayout.V3Old), "dos", "37f56ceb13e401a7ac7d9e6b37fecaf7")]
    [InlineData(GameFolders.Vga, "VGA", 4, nameof(ResourceLayout.V4), "dos", "5d88b9d6a88e6f8e90cded9d01b7f082")]
    [InlineData(GameFolders.FmTowns, "FM-TOWNS", 3, nameof(ResourceLayout.V3Old256), "fmtowns", "c5d10e190d4b4d59114b824f2fdbd00e")]
    public void Identifies_each_release(string folder, string variant, int version, string layoutName, string platform, string md5)
    {
        var layout = Enum.Parse<ResourceLayout>(layoutName);

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

    /// <summary>
    /// Each release is named as ScummVM names its target, which its saves are named after
    /// (<c>generatePreferredTarget</c>): the tag, then the platform unless DOS, then the language
    /// unless English; a German Amiga EGA release, from the table alone, shows the last two.
    /// </summary>
    [Theory]
    [InlineData(GameFolders.Ega, "loom-ega")]
    [InlineData(GameFolders.Vga, "loom-vga")]
    [InlineData(GameFolders.FmTowns, "loom-fm")]
    public void Each_release_has_scummvms_target_name(string folder, string target)
    {
        var amiga = new ScummGame(ScummMd5Table.Find("2fe369ad70f52a8cf7ad6077ee64f81a")!, 3, ResourceLayout.V3Old, false, "ega");
        Assert.Equal("loom-ega-amiga-de", amiga.PreferredTarget);
        if (GameFolders.Find(folder) is not { } path)
        {
            return;
        }

        Assert.Equal(target, ScummDetector.Identify(new DirectoryGameContainer(path))!.PreferredTarget);
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
