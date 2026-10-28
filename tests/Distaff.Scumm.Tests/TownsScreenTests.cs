using System.Security.Cryptography;
using Distaff.Scumm.Game;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.View;

namespace Distaff.Scumm.Tests;

/// <summary>The FM Towns' screen: its height, its two layers and their colours. Tests against the game data pass without checking when it is absent.</summary>
public class TownsScreenTests
{
    private static readonly Rgb TextWhite = GameWorld.To15Bit(GameWorld.TownsTextPalette[15]), TextShadow = GameWorld.To15Bit(GameWorld.TownsTextPalette[8]);

    /// <summary>The screen is 240 rows; the boot script puts the room in the top 144 and the verbs below, with no text band.</summary>
    [Fact]
    public void The_screen_has_the_room_above_the_verbs_in_240_rows()
    {
        if (Run(1) is not { } world)
        {
            return;
        }

        Assert.Equal(240, world.ScreenHeight);
        Assert.Equal((0, 144), (world.ViewTop, world.ViewHeight));
        Assert.Equal((0, 96), (world.Screens.Text.Height, world.Screens.Verb.Height));
        var frame = world.Frame();
        Assert.Equal((320, 240), (frame.Width, frame.Height));
    }

    /// <summary>The logo, in the room's own colours through 15-bit colour, and black below it.</summary>
    [Fact]
    public void The_logo_shows_in_the_rooms_colours()
    {
        if (Run(150) is not { } world)
        {
            return;
        }

        Assert.Equal(1, world.CurrentRoomNumber);
        var frame = world.Frame(cursor: false);
        Assert.Equal(Logo, Hash(frame));
        Assert.All(frame.Pixels.AsSpan(144 * 320).ToArray(), p => Assert.Equal(Rgb.Black, p));
    }

    /// <summary>
    /// The cliff, with the opening's first line under it in the verb band: white in the text
    /// layer's colours, with its shadow in colour 8 right of and below each pixel.
    /// </summary>
    [Fact]
    public void The_openings_text_shows_in_the_text_layers_colours()
    {
        if (Run(580) is not { } world)
        {
            return;
        }

        Assert.Equal(2, world.CurrentRoomNumber);
        Assert.Equal("Never seen one so bright before.", world.MessageText);
        var frame = world.Frame(cursor: false);
        Assert.Equal(Cliff, Hash(frame));
        var band = frame.Pixels.AsSpan(144 * 320).ToArray();
        Assert.Equal(new HashSet<Rgb> { Rgb.Black, TextWhite, TextShadow }, band.ToHashSet());
        for (int i = 0; i < band.Length - 321; i++)
        {
            if (band[i] == TextShadow)
            {
                // a shadow pixel lies right of or below a white one
                Assert.True(band[i - 1] == TextWhite || band[i - 320] == TextWhite, $"shadow at {i % 320},{144 + i / 320}");
            }
        }
    }

    /// <summary>A colour as ScummVM's 15-bit screen shows it: 5 bits a component, widened again.</summary>
    [Theory]
    [InlineData(0xFF, 0xFF)]
    [InlineData(0xAB, 0xAD)]
    [InlineData(0x57, 0x52)]
    [InlineData(0x07, 0x00)]
    public void Colours_are_shown_in_15_bits(byte value, byte shown)
        => Assert.Equal(new Rgb(shown, shown, shown), GameWorld.To15Bit(new Rgb(value, value, value)));

    /// <summary>
    /// Outside the room, the text layer shows the band's low four bits where the text surface is
    /// 0, and the text surface's colour elsewhere, once what was drawn there is brought up.
    /// </summary>
    [Fact]
    public void The_text_layer_shows_the_bands_low_bits_under_the_text()
    {
        var screens = new VirtScreens(240, fmTowns: true);
        screens.Init(0, 144, 320);
        screens.FlushTownsLayer();
        screens.Verb.Front!.FillRect(new Rect(0, 0, 16, 1), 0x2B);
        screens.TextSurface[8, 144] = 0x77;
        Assert.Equal(0, screens.TownsLayer![0, 144]);

        screens.MarkDirty(screens.Verb, 0, 16, 0, 1);
        screens.FlushTownsLayer();
        Assert.Equal((0x0B, 0x07, 0x0B, 0), (screens.TownsLayer[0, 144], screens.TownsLayer[8, 144], screens.TownsLayer[15, 144], screens.TownsLayer[16, 144]));
        Assert.Equal(0, screens.TownsLayer[24, 144]);
    }

    /// <summary>
    /// A box drawn into the text layer stays until the band under it is drawn again: one in colour
    /// 0 hides what the band holds.
    /// </summary>
    [Fact]
    public void A_box_in_the_text_layer_stays_until_the_band_is_drawn_again()
    {
        var screens = new VirtScreens(240, fmTowns: true);
        screens.Init(0, 144, 320);
        screens.Verb.Front!.Fill(0x0C);
        screens.FlushTownsLayer();
        screens.TownsLayer!.FillRect(new Rect(0, 144, 320, 96), 0);
        screens.FlushTownsLayer();
        var colors = Enumerable.Range(0, 256).Select(i => new Rgb((byte)i, 0, 0)).ToArray();
        var frame = screens.ComposeTowns(null, 0, 144, colors, GameWorld.TownsTextPalette);
        Assert.Equal(Rgb.Black, frame[100, 200]);

        screens.MarkDirty(screens.Verb, 96, 103, 50, 60);
        screens.FlushTownsLayer();
        frame = screens.ComposeTowns(null, 0, 144, colors, GameWorld.TownsTextPalette);
        Assert.Equal((GameWorld.TownsTextPalette[12], Rgb.Black), (frame[100, 200], frame[110, 200]));
    }

    /// <summary>The FM Towns' flashlight rounds each corner by one curve, mirrored: one pixel left at the top row's corner, all eight at the eighth row.</summary>
    [Fact]
    public void The_flashlight_rounds_its_corners_by_the_towns_curve()
    {
        var lit = new Surface8(32, 32);
        lit.Fill(9);
        var dark = new Surface8(32, 32);
        Flashlight.Copy(lit, dark, new Rect(4, 4, 24, 24), fmTowns: true);

        int Lit(int y, int from, int to) => Enumerable.Range(from, to - from).Count(x => dark[x, y] == 9);
        Assert.Equal((1, 1, 1, 1), (Lit(4, 4, 12), Lit(4, 20, 28), Lit(27, 4, 12), Lit(27, 20, 28)));
        Assert.Equal((0, 9), (dark[4, 4], dark[11, 4]));
        Assert.Equal((7, 8), (Lit(10, 4, 12), Lit(11, 4, 12)));
    }

    // the cliff's frame is reached later with the smooth scrolling, the messenger at another point of her flight
    private const string Logo = "a978b0f902ec14fd", Cliff = "0f6e49e94151884f";

    private static string Hash(RgbFrame frame)
        => Convert.ToHexStringLower(SHA256.HashData(frame.Pixels.SelectMany(p => new[] { p.R, p.G, p.B }).ToArray()))[..16];

    /// <summary>The game booted and run for some steps.</summary>
    private static GameWorld? Run(int steps)
    {
        if (TownsResourceTests.Open() is not { } loader)
        {
            return null;
        }

        var world = new GameWorld(loader);
        world.Boot();
        for (int i = 0; i < steps; i++)
        {
            world.StepFrame();
        }

        return world;
    }
}
