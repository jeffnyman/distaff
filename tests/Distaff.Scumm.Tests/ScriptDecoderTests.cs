using Distaff.Scumm.Resources;
using Distaff.Scumm.Rooms;
using Distaff.Scumm.Scripts;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>Decoding the EGA release's scripts. Tests against the game data pass without checking when it is absent.</summary>
public class ScriptDecoderTests
{
    private readonly ScriptDecoder _decoder = new();

    [Fact]
    public void Every_opcode_has_a_handler_and_a_layout()
    {
        for (int opcode = 0; opcode < 256; opcode++)
        {
            string handler = _decoder.HandlerOf((byte)opcode);

            // a script of the opcode and enough zero bytes for any operands; a layout that is
            // missing throws InvalidOperationException, a value the layout rejects InvalidDataException
            var code = new byte[64];
            code[0] = (byte)opcode;
            code[63] = 0xFF;
            try
            {
                _decoder.Decode(code, 0);
            }
            catch (InvalidDataException)
            {
            }
            catch (InvalidOperationException ex)
            {
                Assert.Fail($"0x{opcode:X2} {handler}: {ex.Message}");
            }
        }
    }

    [Fact]
    public void Variables_carry_their_kind_and_index()
    {
        // move: the result, then a variable or word; 0x9A has bit 0x80, so the operand is a variable
        byte[] code =
        [
            0x9A,
            0x05, 0x20, 0x07, 0x00, // Var[5 + 7]
            0x02, 0x60, 0x03, 0x20, // Local[2 + Var[3]]
        ];

        var instruction = _decoder.Decode(code, 0);

        Assert.Equal("Var[5 + 7] = move(Local[2 + Var[3]])", instruction.Text);
        Assert.Equal(code.Length, instruction.Length);
    }

    [Fact]
    public void Print_ends_at_its_text()
    {
        // print to actor 1: a colour, then the text with an escape that takes two more bytes
        byte[] code = [0x14, 0x01, 0x01, 0x0F, 0x0F, (byte)'H', (byte)'i', 0xFF, 0x04, 0x10, 0x00, 0x00];

        var instruction = _decoder.Decode(code, 0);

        Assert.Equal(code.Length, instruction.Length);
        Assert.Equal(["COLOR", "TEXTSTRING"], instruction.SubOps.Select(s => s.Name));
    }

    /// <summary>The boot script sets up the cursor and the strings, then waits in the copy protection room.</summary>
    [Fact]
    public void The_boot_script_decodes()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var code = loader.LoadScript(1)!.Value;
        var instructions = _decoder.DecodeReachable(code, [0]).Values.ToList();

        Assert.Equal("roomOps([ROOM_SCREEN(0, 144)])", instructions[0].Text);
        Assert.Contains(instructions, i => i.Text == "loadRoom(69)");
    }

    /// <summary>
    /// Every global script, every room's entry, exit and local scripts, and every object's verb
    /// scripts decode along every path execution can take from their starts.
    /// </summary>
    [Fact]
    public void Every_script_decodes()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var failures = new List<string>();
        int scripts = 0, instructions = 0;

        void Check(string what, ReadOnlyMemory<byte> code, IEnumerable<int> entries)
        {
            try
            {
                instructions += _decoder.DecodeReachable(code, entries).Count;
                scripts++;
            }
            catch (InvalidDataException ex)
            {
                failures.Add($"{what}: {ex.Message}");
            }
        }

        for (int number = 1; number < loader.Index.Count(ResourceType.Script); number++)
        {
            try
            {
                if (loader.LoadScript(number) is { } code)
                {
                    Check($"script {number}", code, [0]);
                }
            }
            catch (InvalidDataException)
            {
                // the two leftover entries the index points past their files
            }
        }

        foreach (var room in Rooms(loader))
        {
            foreach (int offset in new[] { room.ExitScriptOffset, room.EntryScriptOffset }.Concat(room.LocalScriptOffsets.Values).Where(o => o != 0))
            {
                Check($"room {room.Number} at {offset}", room.Data, [offset]);
            }

            foreach (var obj in room.Objects.Where(o => o.Verbs.Count > 0))
            {
                Check($"room {room.Number} object {obj.Number}", obj.Code, obj.Verbs.Select(v => v.Offset));
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.Equal(ScriptCount, scripts);
        Assert.Equal(InstructionCount, instructions);
    }

    /// <summary>
    /// Decoded straight through, one instruction after another, every global script ends exactly
    /// where its resource does: a layout one byte off would run past it or stop short.
    /// </summary>
    [Fact]
    public void Every_global_script_decodes_straight_to_its_end()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var failures = new List<string>();
        int scripts = 0;
        for (int number = 1; number < loader.Index.Count(ResourceType.Script); number++)
        {
            ReadOnlyMemory<byte>? code;
            try
            {
                code = loader.LoadScript(number);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            if (code is not { } script)
            {
                continue;
            }

            try
            {
                int end = _decoder.DecodeAll(script).Last().End;
                if (end != script.Length)
                {
                    failures.Add($"script {number}: ends at {end:X4} of {script.Length:X4}");
                }
            }
            catch (InvalidDataException ex)
            {
                failures.Add($"script {number}: {ex.Message}");
            }

            scripts++;
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.Equal(114, scripts);
    }

    /// <summary>
    /// The same for the rooms' scripts, each up to where the next piece of its room starts, and
    /// for the objects' verb scripts, from the first to the end of the object's code block.
    /// </summary>
    [Fact]
    public void Every_room_script_decodes_straight_to_its_end()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var failures = new List<string>();
        int scripts = 0;

        void Check(string what, ReadOnlyMemory<byte> code)
        {
            try
            {
                int end = _decoder.DecodeAll(code).Last().End;
                if (end != code.Length)
                {
                    failures.Add($"{what}: ends at {end:X4} of {code.Length:X4}");
                }
            }
            catch (InvalidDataException ex)
            {
                failures.Add($"{what}: {ex.Message}");
            }

            scripts++;
        }

        foreach (var room in Rooms(loader))
        {
            if (!room.ExitScript.IsEmpty)
            {
                Check($"room {room.Number} exit", room.ExitScript);
            }

            if (!room.EntryScript.IsEmpty)
            {
                Check($"room {room.Number} entry", room.EntryScript);
            }

            foreach (int number in room.LocalScriptOffsets.Keys)
            {
                Check($"room {room.Number} local {number}", room.LocalScript(number)!.Value);
            }

            foreach (var obj in room.Objects.Where(o => o.Verbs.Count > 0))
            {
                Check($"room {room.Number} object {obj.Number}", obj.Code[obj.Verbs.Min(v => v.Offset)..]);
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.Equal(RoomScriptCount, scripts);
    }

    private const int RoomScriptCount = 873;

    private const int ScriptCount = 987;

    private const int InstructionCount = 25127;

    private static IEnumerable<Room> Rooms(ResourceLoader loader)
    {
        for (int number = 1; number < loader.Index.Count(ResourceType.Room); number++)
        {
            if (loader.Index.Locate(ResourceType.Room, number).IsPresent && LflFile.Exists(loader.Game, number) && loader.LoadRoom(number) is { } room)
            {
                yield return room;
            }
        }
    }

    private static ResourceLoader? Open()
        => GameFolders.Find(GameFolders.Ega) is { } path ? ResourceLoader.Open(new DirectoryGameContainer(path)) : null;
}
