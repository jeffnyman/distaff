using System.Buffers.Binary;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Rooms;
using Distaff.Scumm.Scripts;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>The VGA CD release's index, disk file and resources. Each test passes without checking when the game data is absent.</summary>
public class VgaResourceTests
{
    private readonly ScriptDecoder _decoder = new(4);

    /// <summary>
    /// <c>000.LFL</c> holds the room names, the directories and the global objects. Every entry has
    /// an offset; those for rooms the disk does not hold, and their resources, say room 0.
    /// </summary>
    [Fact]
    public void Index_holds_the_room_names_the_directories_and_the_global_objects()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var index = loader.Index;
        Assert.Equal(1000, index.GlobalObjectCount);
        Assert.Equal((99, 199, 199, 199), (index.Count(ResourceType.Room), index.Count(ResourceType.Costume), index.Count(ResourceType.Script), index.Count(ResourceType.Sound)));
        Assert.Equal(78, index.RoomNames.Count);
        Assert.Equal(("logo", "pan", "village", "cp"), (index.RoomNames[1], index.RoomNames[2], index.RoomNames[5], index.RoomNames[69]));
        Assert.Equal([0, 1], index.RoomDisks.Distinct().Order());
        Assert.Equal(78, index.RoomDisks.Count(d => d == 1));
    }

    /// <summary>
    /// <c>DISK01.LEC</c> holds the 78 rooms, each an <c>LF</c> block of the room followed by the
    /// scripts, costumes and sounds stored with it; two sounds are bare blocks without a tag.
    /// </summary>
    [Fact]
    public void The_disk_holds_each_room_and_what_is_stored_with_it()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var disk = loader.Disk(1);
        var data = disk.Data.Span;
        var tags = new Dictionary<string, int>();
        foreach (int room in disk.Rooms)
        {
            int start = disk.RoomStart(room) - DiskBundle.RoomHeaderSize;
            Assert.Equal(("LF", room), ($"{(char)data[start + 4]}{(char)data[start + 5]}", (int)BinaryPrimitives.ReadUInt16LittleEndian(data[(start + 6)..])));
            int end = start + BinaryPrimitives.ReadInt32LittleEndian(data[start..]);
            int at = disk.RoomStart(room);
            Assert.Equal("RO", $"{(char)data[at + 4]}{(char)data[at + 5]}");
            for (; at < end; at += BinaryPrimitives.ReadInt32LittleEndian(data[at..]))
            {
                string tag = data[at + 4] == 0 ? "bare" : $"{(char)data[at + 4]}{(char)data[at + 5]}";
                tags[tag] = tags.GetValueOrDefault(tag) + 1;
            }

            Assert.Equal(end, at);
        }

        Assert.Equal(78, disk.Rooms.Count());
        Assert.Equal(new Dictionary<string, int> { ["RO"] = 78, ["SC"] = 122, ["CO"] = 166, ["SO"] = 30, ["bare"] = 2 }, tags);
    }

    /// <summary>The entries the disk holds load; the rest are in room 0, which has no room file.</summary>
    [Theory]
    [InlineData(ResourceType.Room, 78, 21)]
    [InlineData(ResourceType.Costume, 165, 34)]
    [InlineData(ResourceType.Script, 122, 77)]
    [InlineData(ResourceType.Sound, 32, 167)]
    public void Index_entries_load_or_are_in_no_room(ResourceType type, int loaded, int missing)
    {
        if (Open() is not { } loader)
        {
            return;
        }

        int ok = 0, none = 0;
        for (int i = 0; i < loader.Index.Count(type); i++)
        {
            if (loader.Load(type, i) is null)
            {
                none++;
            }
            else
            {
                ok++;
            }
        }

        Assert.Equal((loaded, missing), (ok, none));
    }

    [Fact]
    public void Every_room_parses()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var rooms = Rooms(loader).ToList();
        Assert.Equal(78, rooms.Count);
        Assert.All(rooms, room =>
        {
            Assert.True(room.Width > 0 && room.Height > 0, $"room {room.Number}: {room.Width}x{room.Height}");
            Assert.True(room.ImageOffset > 0 && room.BlockOffsets.ContainsKey("PA"), $"room {room.Number} has no picture or palette");
            Assert.All(room.Objects, o => Assert.InRange(o.Number, 1, loader.Index.GlobalObjectCount - 1));
            Assert.All(room.LocalScriptOffsets.Keys, s => Assert.InRange(s, 200, 255));
            Assert.All(room.LocalScriptOffsets.Keys, s => Assert.False(room.LocalScript(s)!.Value.IsEmpty));
        });
    }

    /// <summary>The logo room, as tall as the screen: its objects, the sounds stored with it, its scripts and blocks.</summary>
    [Fact]
    public void The_first_room_has_its_objects_sounds_and_scripts()
    {
        if (Open()?.LoadRoom(1) is not { } room)
        {
            return;
        }

        Assert.Equal((320, 200), (room.Width, room.Height));
        Assert.Equal(61, room.Objects.Count);
        Assert.Equal(69, room.Objects[0].Number);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 24], room.Sounds);
        Assert.Empty(room.Scripts);
        Assert.Equal([200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211], room.LocalScriptOffsets.Keys);
        Assert.Equal((47679, 47692), (room.ExitScriptOffset, room.EntryScriptOffset));
        Assert.Equal(1, room.WalkBoxes.Count);
        Assert.Equal(["HD", "CC", "SP", "BX", "PA", "SA", "BM", "NL", "SL", "EX", "EN", "LC"], room.BlockOffsets.Keys);
    }

    /// <summary>The cliff: its objects, with the VGA CD's own numbers, and its boxes, which have a scale.</summary>
    [Fact]
    public void Objects_have_their_names_and_numbers()
    {
        if (Open()?.LoadRoom(2) is not { } room)
        {
            return;
        }

        Assert.Equal((960, 144), (room.Width, room.Height));
        Assert.Equal(9, room.Objects.Count);
        Assert.Equal(("leaf", "sky"), (room.FindObject(79)?.Name, room.FindObject(81)?.Name));
        var sky = room.FindObject(81)!;
        Assert.Equal((616, 8, 104, 40, 808, 59), (sky.X, sky.Y, sky.Width, sky.Height, sky.WalkX, sky.WalkY));
        Assert.Equal([54, 57, 58, 59, 64, 86], sky.Verbs.Select(v => (int)v.Verb));
        Assert.Equal(15, room.WalkBoxes.Count);
        Assert.Equal(144, room.WalkBoxes.Matrix.Length);
    }

    /// <summary>The charsets are <c>901.LFL</c> to <c>904.LFL</c>, not encoded; there is no charset 0.</summary>
    [Fact]
    public void The_charsets_load()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        Assert.Null(loader.LoadCharset(0));
        Assert.Equal([2609, 4189, 2015, 4384], Enumerable.Range(1, 4).Select(c => loader.LoadCharset(c)!.Value.Length));
        Assert.Null(loader.LoadCharset(5));
    }

    /// <summary>Global scripts start after the 6-byte header of a 32-bit size and the tag <c>SC</c>.</summary>
    [Fact]
    public void Scripts_load_after_their_header()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var resource = loader.Load(ResourceType.Script, 1)!.Value;
        Assert.Equal("SC", $"{(char)resource.Span[4]}{(char)resource.Span[5]}");
        Assert.Equal(resource.Length - ResourceLoader.SmallHeaderSize, loader.LoadScript(1)!.Value.Length);
        Assert.Throws<NotSupportedException>(() => loader.LoadSound(1));
    }

    /// <summary>
    /// Every global script decodes with version 4's layouts as one straight run of instructions
    /// that ends exactly where the script does.
    /// </summary>
    [Fact]
    public void Every_global_script_decodes_straight_to_its_end()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var failures = new List<string>();
        int scripts = 0, instructions = 0;
        for (int number = 1; number < loader.Index.Count(ResourceType.Script); number++)
        {
            if (loader.LoadScript(number) is { } code)
            {
                instructions += Check($"script {number}", code, failures);
                scripts++;
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.Equal((122, 10512), (scripts, instructions));
    }

    /// <summary>The same for the rooms' scripts, each to the end of its block, and the objects' verb scripts, from the first to the end of the object's code block.</summary>
    [Fact]
    public void Every_room_script_decodes_straight_to_its_end()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var failures = new List<string>();
        int scripts = 0, instructions = 0;
        void Run(string what, ReadOnlyMemory<byte> code)
        {
            instructions += Check(what, code, failures);
            scripts++;
        }

        foreach (var room in Rooms(loader))
        {
            if (!room.ExitScript.IsEmpty)
            {
                Run($"room {room.Number} exit", room.ExitScript);
            }

            if (!room.EntryScript.IsEmpty)
            {
                Run($"room {room.Number} entry", room.EntryScript);
            }

            foreach (int number in room.LocalScriptOffsets.Keys)
            {
                Run($"room {room.Number} local {number}", room.LocalScript(number)!.Value);
            }

            foreach (var obj in room.Objects.Where(o => o.Verbs.Count > 0))
            {
                Run($"room {room.Number} object {obj.Number}", obj.Code[obj.Verbs.Min(v => v.Offset)..]);
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.Equal((910, 18698), (scripts, instructions));
    }

    /// <summary>Decodes code straight through, noting a failure when it does not decode or does not end at its end; returns the instructions decoded.</summary>
    private int Check(string what, ReadOnlyMemory<byte> code, List<string> failures)
    {
        try
        {
            var instructions = _decoder.DecodeAll(code).ToList();
            if (instructions[^1].End != code.Length)
            {
                failures.Add($"{what}: ends at {instructions[^1].End:X4} of {code.Length:X4}");
            }

            return instructions.Count;
        }
        catch (InvalidDataException ex)
        {
            failures.Add($"{what}: {ex.Message}");
            return 0;
        }
    }

    private static IEnumerable<Room> Rooms(ResourceLoader loader)
    {
        for (int number = 1; number < loader.Index.Count(ResourceType.Room); number++)
        {
            if (loader.HasRoomFile(number) && loader.LoadRoom(number) is { } room)
            {
                yield return room;
            }
        }
    }

    private static ResourceLoader? Open()
        => GameFolders.Find(GameFolders.Vga) is { } path ? ResourceLoader.Open(new DirectoryGameContainer(path)) : null;
}
