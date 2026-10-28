using Distaff.Player;

namespace Distaff.Scumm.Tests;

/// <summary>Distaff's own icon (<see cref="AppIcon"/>), which the window shows and <c>tools/icon</c> writes into the executable's <c>.ico</c>.</summary>
public class AppIconTests
{
    /// <summary>At every size the .ico holds, a tile of night sky with rounded, see-through corners, its middle lit by the bundle of thread.</summary>
    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(256)]
    public void The_icon_is_a_rounded_tile_lit_in_the_middle(int size)
    {
        var pixels = AppIcon.Render(size);
        Assert.Equal(size * size * 4, pixels.Length);

        byte Alpha(int x, int y) => pixels[(y * size + x) * 4 + 3];
        Assert.Equal(0, Alpha(0, 0));
        Assert.Equal(0, Alpha(size - 1, size - 1));
        Assert.Equal(255, Alpha(size / 2, size / 2));

        // the bundle, up and right of the middle, is bright and blue-white; the sky at the bottom left of it is dark
        int bx = (int)(size * 0.585), by = (int)(size * 0.345);
        int i = (by * size + bx) * 4, sky = ((int)(size * 0.75) * size + (int)(size * 0.6)) * 4;
        Assert.True(pixels[i + 2] > 200 && pixels[i + 1] > 150, $"bundle {pixels[i]},{pixels[i + 1]},{pixels[i + 2]}");
        Assert.True(pixels[sky] + pixels[sky + 1] < pixels[i] + pixels[i + 1]);
    }
}
