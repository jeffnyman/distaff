using Distaff.Scumm.Game;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Saves;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>The screen effects of a room change (<c>fadeOut</c>, <c>fadeIn</c>, <c>dissolveEffect</c>). Tests against the game data pass without checking when it is absent.</summary>
public class EffectTests
{
    /// <summary>
    /// The EGA dissolves a new room in over the old in blocks of 8 by 4 (effect 128): the screen
    /// stops 18 times, a quarter-frame each, every block showing either the old room or the new,
    /// more of the new each time; the frame then ends with the new room.
    /// </summary>
    [Fact]
    public void The_ega_dissolves_in_blocks()
    {
        if (Boot(GameFolders.Ega, 60) is not var (world, before, effect))
        {
            return;
        }

        var after = world.Frame(cursor: false);
        Assert.Equal(1, world.CurrentRoomNumber);
        Assert.Equal(18, effect.Count);
        Assert.All(effect, e => Assert.Equal(1.0 / GameWorld.TimerFrequency, e.Seconds, 6));

        int last = -1;
        foreach (var (frame, _) in effect)
        {
            int fresh = 0;
            for (int by = 0; by < world.ViewHeight; by += 4)
            {
                for (int bx = 0; bx < 320; bx += 8)
                {
                    bool old = Block(frame, before, bx, world.ViewTop + by), now = Block(frame, after, bx, world.ViewTop + by);
                    Assert.True(old || now, $"block at {bx},{by} is neither room's");
                    fresh += now && !old ? 1 : 0;
                }
            }

            Assert.True(fresh >= last, "the new room's blocks never go");
            last = fresh;
        }

        Assert.True(last > 0);
    }

    /// <summary>
    /// The VGA CD dissolves pixel by pixel, out to black and in again (0x8080): from the cliff to
    /// the Elders' messenger, 18 stops out and 18 in, a quarter-frame each; going out, each pixel
    /// shows the cliff or black.
    /// </summary>
    [Fact]
    public void The_vga_cd_dissolves_out_and_in_by_pixels()
    {
        if (Boot(GameFolders.Vga, 300, toRoom: 53) is not var (world, before, effect))
        {
            return;
        }

        var black = world.DisplayPalette()[0];
        Assert.Equal(36, effect.Count);
        for (int i = 0; i < 18; i++)
        {
            var frame = effect[i].Frame;
            for (int y = world.ViewTop; y < world.ViewTop + world.ViewHeight; y++)
            {
                for (int x = 0; x < 320; x++)
                {
                    Assert.True(frame[x, y] == before[x, y] || frame[x, y] == black, $"pixel {x},{y} going out");
                }
            }
        }

        var after = world.Frame(cursor: false);
        Assert.Equal(53, world.CurrentRoomNumber);
        Assert.True(Same(effect[^1].Frame, after) > Same(effect[18].Frame, after));
    }

    /// <summary>Saves keep the effects the scripts set, and loading puts them back (<c>_switchRoomEffect</c> and the rest, version 8 on).</summary>
    [Fact]
    public void Saves_keep_the_room_effects()
    {
        if (Boot(GameFolders.Vga, 300, toRoom: 53) is not var (world, _, _))
        {
            return;
        }

        int objects = ResourceLoader.Open(new DirectoryGameContainer(GameFolders.Find(GameFolders.Vga)!)).Index.ObjectOwners.Count;
        var (_, state) = SaveFile.Read(world.Save("effects", new DateTime(2026, 10, 28)), objects, 4);
        Assert.Equal((128, 128, 128, 1), (state.SwitchRoomEffect, state.SwitchRoomEffect2, state.NewEffect, state.ScreenEffectFlag));
    }

    /// <summary>Boots a release, picks Practice at a frame, and plays to the first effect into a room: the screen before it, and the effect's screens with their times.</summary>
    private static (GameWorld World, RgbFrame Before, List<(RgbFrame Frame, double Seconds)> Effect)? Boot(string folder, int click, int toRoom = -1)
    {
        if (GameFolders.Find(folder) is not { } path)
        {
            return null;
        }

        var world = new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path)));
        world.Boot();
        var before = world.Frame(cursor: false);
        for (int i = 0; i < 4000; i++)
        {
            if (world.Frames == click)
            {
                world.Click(160, 92);
            }

            world.Step();
            if (world.InEffect && (toRoom < 0 || world.CurrentRoomNumber == toRoom))
            {
                var effect = new List<(RgbFrame, double)>();
                while (world.InEffect)
                {
                    effect.Add((world.Frame(cursor: false), world.StepLength.TotalSeconds));
                    world.Step();
                }

                return (world, before, effect);
            }

            if (!world.InEffect)
            {
                before = world.Frame(cursor: false);
            }
        }

        throw new InvalidOperationException("No effect.");
    }

    private static bool Block(RgbFrame a, RgbFrame b, int x0, int y0)
    {
        for (int y = y0; y < y0 + 4; y++)
        {
            for (int x = x0; x < x0 + 8; x++)
            {
                if (a[x, y] != b[x, y])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static int Same(RgbFrame a, RgbFrame b) => a.Pixels.Where((p, i) => p == b.Pixels[i]).Count();
}
