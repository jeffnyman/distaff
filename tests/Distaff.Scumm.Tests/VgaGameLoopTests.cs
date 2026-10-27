using System.Security.Cryptography;
using Distaff.Scumm.Game;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Scripts;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>The VGA CD release running: its scripts, text, palette, CD timer and actors. Tests against the game data pass without checking when it is absent.</summary>
public class VgaGameLoopTests
{
    /// <summary>
    /// The game boots to the proficiency screen (room 69), the scripts having picked charset 1:
    /// its three choices drawn in the VGA font, in the room's palette.
    /// </summary>
    [Fact]
    public void The_proficiency_screen_draws_in_the_vga_font()
    {
        if (Open() is not { } world)
        {
            return;
        }

        world.Boot();
        Run(world, 300);
        Assert.Equal(69, world.CurrentRoomNumber);
        Assert.All(new[] { "STANDARD", "PRACTICE", "EXPERT", "Please choose your skill level." }, m => Assert.Contains(m, world.MessageLog));
        Assert.Equal(ProficiencyScreen, Hash(world));
    }

    /// <summary>
    /// PRACTICE starts the opening: the first line is spoken from the CD (track 1, from the offset
    /// the script gives in 7.5-frame units less the 22650 frames before Loom's audio, for its length
    /// and 5 frames more), and the scripts wait on the music timer the CD timer counts. The opening
    /// plays through to Bobbin on the cliff, with the player in control.
    /// </summary>
    [Fact]
    public void Practice_plays_the_opening_to_bobbin_on_the_cliff()
    {
        if (Open() is not { } world)
        {
            return;
        }

        world.Boot();
        Run(world, 300);
        world.Click(160, 95);
        (int, int, int)? firstVoice = null;
        var rooms = new List<int>();
        for (int i = 0; i < 3000; i++)
        {
            world.Step();
            firstVoice ??= world.CdTrack;
            if (rooms.Count == 0 || rooms[^1] != world.CurrentRoomNumber)
            {
                rooms.Add(world.CurrentRoomNumber);
            }
        }

        Assert.Equal((1, 22, 185), firstVoice);
        Assert.Equal([69, 2, 53, 2], rooms);
        Assert.Equal(1, world.Engine.Variables[Vars.Ego]);
        Assert.True(world.Engine.IsScriptRunning(6));
        Assert.Equal(BobbinOnTheCliff, Hash(world));
    }

    /// <summary>
    /// Clicking the leaf points Bobbin at it, and the game shows it in the interface (verb 54): its
    /// image copied from the room as version 4 keeps it, its image block and then its code block,
    /// 6 strips wide and 40 rows high at the bottom right, with its name under it.
    /// </summary>
    [Fact]
    public void Pointing_at_the_leaf_shows_its_icon()
    {
        if (Open() is not { } world)
        {
            return;
        }

        var w = Skip(world);
        w.Click(Leaf);
        Assert.Equal(Leaf, world.Engine.Variables[w.Pointed]);
        var icon = world.Verbs.Single(v => v.VerbId == 54 && v.SaveId == 0);
        Assert.Equal(1, icon.Type);
        Assert.Equal(new Graphics.Rect(248, 152, 48, 40), icon.CurRect);
        Assert.Contains("leaf", world.MessageLog);
        Assert.Equal(LeafIcon, Hash(world));
    }

    /// <summary>
    /// Bobbin follows the leaf to the sanctuary and takes the distaff after the Elders' scene: the
    /// distaff and its staff of notes c to C show, the three he knows marked; Opening, spun on the
    /// egg from the game's own table (variables 100 to 131 on the VGA CD), opens it, and Hetchel
    /// sends Bobbin on his way.
    /// </summary>
    [Fact]
    public void The_distaff_shows_once_taken_and_spins_opening_on_the_egg()
    {
        if (Open() is not { } world)
        {
            return;
        }

        var w = Skip(world);
        w.Use(Leaf);
        w.Use(CliffToVillage).InRoom(3).Ready();
        w.Use(VillageToTents).InRoom(5).Ready();
        w.Use(TentsToPath).InRoom(7).Ready();
        w.Use(PathToSanctuary).InRoom(8).Ready();
        w.Use(SanctuaryDoor).InRoom(9).Ready(20000);
        w.Use(Distaff);
        Assert.All(Enumerable.Range(1, 8), note => Assert.Contains(world.Verbs, v => v.VerbId == note && v.Mode == 1 && v.SaveId == 0));
        Assert.Equal(3, world.Engine.Variables[Known]);
        w.Click(Egg);
        Assert.Equal(DistaffTaken, Hash(world));

        w.Spin(0).Ready(20000);
        Assert.Contains("You've already found Atropos' distaff. Good.", world.MessageLog);
        Assert.Contains("Goodbye, Bobbin!  I must follow the swans.", world.MessageLog);
        Assert.Equal(EggOpened, Hash(world));
    }

    /// <summary>
    /// The VGA CD's cursor is character 1 of charset 1 (<c>redefineBuiltinCursorFromChar</c>), a
    /// small arrow with its point at the hotspot, in the cursor's colour.
    /// </summary>
    [Fact]
    public void The_cursor_is_an_arrow_from_charset_1()
    {
        if (Open() is not { } world)
        {
            return;
        }

        world.Boot();
        Run(world, 300);
        world.Click(100, 100);
        var frame = new Graphics.Surface8(320, 200);
        world.DrawCursor(frame);
        var rows = Enumerable.Range(0, 9).Select(y => string.Concat(Enumerable.Range(0, 8).Select(x => frame[100 + x, 100 + y] != 0 ? '#' : '.')));
        Assert.Equal(["#.......", "##......", "###.....", "####....", "#####...", "######..", "####....", "##......", "........"], rows);
        Assert.Equal((0, 0), world.CursorHotspot);
    }

    /// <summary>Version 4's bit variables are their own 4096 (<c>_bitVars</c>); version 3's are the bits of the global variables.</summary>
    [Fact]
    public void Version_4_bit_variables_are_their_own()
    {
        var v3 = new ScriptEngine(new NullHost());
        var v4 = new ScriptEngine(new NullHost(), version: 4);
        foreach (var engine in new[] { v3, v4 })
        {
            engine.Variables[1] = 1;
            engine.WriteVar(0x8000 | 26, 1);
            Assert.Equal(1, engine.ReadVar(0x8000 | 26));
        }

        Assert.Equal(1 | (1 << 10), v3.Variables[1]);
        Assert.Equal(1, v4.Variables[1]);
        Assert.Equal(1 << 2, v4.BitVariables[3]);
    }

    /// <summary>
    /// A colour cycle turns one step a frame through the shadow palette, its colours mapped from
    /// its counter down, wrapping; a room built by hand with one cycle over colours 10 to 13. No
    /// room of Loom's own has a cycle that runs: each is unused (0x0AAA), empty, or starts at colour
    /// 0, which leaves its counter at 0, as in ScummVM.
    /// </summary>
    [Fact]
    public void A_colour_cycle_turns_through_the_shadow_palette()
    {
        if (Open() is not { } world)
        {
            return;
        }

        var cycles = new byte[64];
        cycles[0] = 0x00;
        cycles[1] = 0x10;
        cycles[2] = 10;
        cycles[3] = 13;
        byte[] room =
        [
            .. Block("RO", []),
            .. Block("HD", [0x40, 0x01, 0x90, 0x00, 0, 0]),
            .. Block("CC", cycles),
        ];
        BitConverter.GetBytes(room.Length).CopyTo(room, 0);
        world.InitCycles(Rooms.Room.ParseV4(99, room));
        var seen = new List<int[]>();
        for (int i = 0; i < 3; i++)
        {
            world.CyclePalette();
            seen.Add(world.ShadowPalette[10..14].Select(c => (int)c).ToArray());
        }

        Assert.Equal([[11, 10, 13, 12], [12, 11, 10, 13], [13, 12, 11, 10]], seen);
    }

    private static byte[] Block(string tag, byte[] body) => [.. BitConverter.GetBytes(6 + body.Length), (byte)tag[0], (byte)tag[1], .. body];

    /// <summary>A script host with no scripts, for working the engine's variables alone.</summary>
    private sealed class NullHost : IScriptHost
    {
        public ReadOnlyMemory<byte>? GlobalScript(int number) => null;

        public (ReadOnlyMemory<byte> Code, int Offset)? LocalScript(int number) => null;

        public (ReadOnlyMemory<byte> Code, int Offset, ScriptKind Kind)? ObjectScript(int obj, int verb) => null;

        public int Execute(ScriptEngine engine, Instruction instruction, IReadOnlyList<int> arguments, IReadOnlyList<int[]> subArguments) => 0;
    }

    private const string ProficiencyScreen = "2dd53cf928a17bed", BobbinOnTheCliff = "3cdf8e8514652f77";
    private const string LeafIcon = "86fa1cda77b3bfb5", DistaffTaken = "6d7da77340d3b74d", EggOpened = "5ecac3ed1624160f";

    // the objects of the VGA CD, numbered apart from the EGA's: the cliff (2), the village (3), the
    // tents (5), the path (7), outside the sanctuary (8), the sanctuary (9)
    private const int CliffToVillage = 78, Leaf = 79, VillageToTents = 86, TentsToPath = 126, PathToSanctuary = 177;
    private const int SanctuaryDoor = 179, Egg = 195, Distaff = 196;

    /// <summary>The notes the distaff plays, 3 for c d e (variable 122 on the EGA).</summary>
    private const int Known = 172;

    /// <summary>The proficiency screen, PRACTICE, and the opening skipped with Escape, to Bobbin on the cliff in control.</summary>
    private static Walkthrough Skip(GameWorld world)
    {
        world.Boot();
        return new Walkthrough(world).Wait(300).ClickAt(160, 95).Wait(100).Key(Input.Key.Escape, (char)27).Ready(8000);
    }

    private static void Run(GameWorld world, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            world.Step();
        }
    }

    private static string Hash(GameWorld world)
    {
        var palette = world.DisplayPalette();
        var rgb = world.Screen().Pixels.SelectMany(p => new[] { palette[p].R, palette[p].G, palette[p].B }).ToArray();
        return Convert.ToHexStringLower(SHA256.HashData(rgb))[..16];
    }

    private static GameWorld? Open()
        => GameFolders.Find(GameFolders.Vga) is { } path ? new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path))) : null;
}
