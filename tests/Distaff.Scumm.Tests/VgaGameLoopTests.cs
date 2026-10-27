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

    /// <summary>A script host with no scripts, for working the engine's variables alone.</summary>
    private sealed class NullHost : IScriptHost
    {
        public ReadOnlyMemory<byte>? GlobalScript(int number) => null;

        public (ReadOnlyMemory<byte> Code, int Offset)? LocalScript(int number) => null;

        public (ReadOnlyMemory<byte> Code, int Offset, ScriptKind Kind)? ObjectScript(int obj, int verb) => null;

        public int Execute(ScriptEngine engine, Instruction instruction, IReadOnlyList<int> arguments, IReadOnlyList<int[]> subArguments) => 0;
    }

    private const string ProficiencyScreen = "2dd53cf928a17bed", BobbinOnTheCliff = "3cdf8e8514652f77";

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
