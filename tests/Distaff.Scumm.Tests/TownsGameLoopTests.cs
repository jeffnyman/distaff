using System.Security.Cryptography;
using Distaff.Scumm.Costumes;
using Distaff.Scumm.Game;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Input;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Scripts;

namespace Distaff.Scumm.Tests;

/// <summary>The FM Towns release's costumes, scripts and interface, played from the opening. Tests against the game data pass without checking when it is absent.</summary>
public class TownsGameLoopTests
{
    private static readonly byte[] OwnColors = [.. Enumerable.Repeat((byte)255, 32)];

    /// <summary>
    /// Every costume parses as the VGA CD's are laid out, with 16 colours, and every frame of every
    /// animation draws; commands 0x79 to 0x7B draw nothing (<c>GF_OLD256</c>).
    /// </summary>
    [Fact]
    public void Every_costume_frame_draws()
    {
        if (TownsResourceTests.Open() is not { } loader)
        {
            return;
        }

        int costumes = 0, frames = 0;
        for (int number = 0; number < loader.Index.Count(ResourceType.Costume); number++)
        {
            if (loader.Load(ResourceType.Costume, number) is not { } data)
            {
                continue;
            }

            var costume = Costume.ParseTowns(number, data);
            Assert.Equal((3, true, true, 16), (costume.Version, costume.SmallHeader, costume.Old256, costume.ColorCount));
            costumes++;
            for (int frame = 0; frame * 4 < costume.AnimationCount; frame++)
            {
                for (int direction = 0; direction < 4; direction++)
                {
                    var state = new CostumeState();
                    state.Decode(costume, frame, direction);
                    var target = new MaskedImage(new Surface8(320, 240), new Surface8(320, 240));
                    CostumeRenderer.Draw(costume, state, direction, CostumeRenderer.Palette(costume, OwnColors), target, 160, 150, zPlane: 0);
                    frames++;
                }
            }
        }

        Assert.Equal((162, 5564), (costumes, frames));
    }

    /// <summary>
    /// The boot script takes the proficiency level ScummVM's dialog sets as the boot parameter
    /// (Practice 0, Standard 1, Expert 2) into variable 123.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void The_boot_parameter_is_the_proficiency_level(int level)
    {
        if (TownsResourceTests.Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        world.Boot(level);
        world.Step();
        Assert.Equal(level, world.Engine.Variables[123]);
        Assert.Equal(42, world.Engine.Variables[Vars.VideoMode]);
    }

    /// <summary>
    /// The copy protection is skipped as ScummVM skips it for the FM Towns: bit 15 of variable 214
    /// reads 0, so the game welcomes the player, and with the protection kept it reads as it is.
    /// </summary>
    [Fact]
    public void The_copy_protection_is_skipped()
    {
        if (TownsResourceTests.Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        world.Boot();
        for (int i = 0; i < 10; i++)
        {
            world.Step();
        }

        Assert.Equal("Welcome to the Age of the Great Guilds.", world.MessageText);
        const int Bit = Operand.BitVariable | (214 << 4) | 15;
        world.Engine.Variables[214] |= 0x8000;
        Assert.Equal(0, world.Engine.ReadVar(Bit));
        world.CopyProtection = true;
        Assert.Equal(1, world.Engine.ReadVar(Bit));
    }

    /// <summary>
    /// Practice level, the opening skipped. Bobbin follows the leaf to the sanctuary, takes the
    /// distaff, whose staff of notes the verb band shows in the text layer, and weaves Opening on
    /// the egg.
    /// </summary>
    [Fact]
    public void The_elders()
    {
        if (TownsResourceTests.Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        var w = new Walkthrough(world);
        world.Boot();
        w.Wait(100).Key(Key.Escape, (char)27).Ready();
        w.Use(Leaf);
        w.Use(CliffToVillage).InRoom(3).Ready();
        w.Use(VillageToTents).InRoom(5).Ready();
        w.Use(TentsToPath).InRoom(7).Ready();
        w.Use(PathToSanctuary).InRoom(8).Ready();
        w.Use(SanctuaryDoor).InRoom(9).Ready(20000);
        w.Use(Distaff);
        Assert.Equal(TheDistaff, Hash(world.Frame(cursor: false)));
        w.Click(Egg).Spin(0).Ready(20000);

        Assert.Contains("Last leaf of the year.", world.MessageLog);
        Assert.Contains("A swan's egg. Curious destiny.", world.MessageLog);
        Assert.Contains("I'd hoped the swan might come to save you.", world.MessageLog);
        Assert.Equal(9, world.CurrentRoomNumber);
        Assert.Equal(3, world.Engine.Variables[Known]);
        Assert.NotEqual(0, world.Engine.Variables[50] & 0x2000);
    }

    /// <summary>F5 reaches the game as the FM Towns' special key 0x8005, and its scripts show the save screen, room 70.</summary>
    [Fact]
    public void F5_shows_the_games_own_save_screen()
    {
        if (TownsResourceTests.Open() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        var w = new Walkthrough(world);
        world.Boot();
        w.Wait(100).Key(Key.Escape, (char)27).Ready();
        w.Key(Key.F5, '\0').Wait(50);
        Assert.Equal(70, world.CurrentRoomNumber);
        Assert.Contains(world.MessageLog, m => m.StartsWith("Insert save-load disk in Drive A."));
    }

    private const int Known = 122;
    private const int CliffToVillage = 153, Leaf = 154, VillageToTents = 161, TentsToPath = 201, PathToSanctuary = 252, SanctuaryDoor = 254, Egg = 270, Distaff = 271;
    private const string TheDistaff = "165020946ec95022";

    private static string Hash(RgbFrame frame)
        => Convert.ToHexStringLower(SHA256.HashData(frame.Pixels.SelectMany(p => new[] { p.R, p.G, p.B }).ToArray()))[..16];
}
