using Distaff.Scumm.Resources;
using Distaff.Scumm.Rooms;
using Distaff.Scumm.Scripts;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>The FM Towns release's index, room files and resources. Each test passes without checking when the game data is absent.</summary>
public class TownsResourceTests
{
    private readonly ScriptDecoder _decoder = new(3, fmTowns: true);

    /// <summary>
    /// <c>00.LFL</c> is laid out as the VGA CD's index, without the room names. Every entry, a
    /// room's too, is in room 0 at offset 0 or names its room: each room is at the start of its own file.
    /// </summary>
    [Fact]
    public void Index_holds_the_directories_and_the_global_objects()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var index = loader.Index;
        Assert.Equal(1000, index.GlobalObjectCount);
        Assert.Equal((100, 200, 200, 200), (index.Count(ResourceType.Room), index.Count(ResourceType.Costume), index.Count(ResourceType.Script), index.Count(ResourceType.Sound)));
        Assert.Empty(index.RoomNames);
        Assert.All(Enumerable.Range(0, 100), r => Assert.Equal(new ResourceLocation(r, 0), index.Locate(ResourceType.Room, r)));
    }

    /// <summary>
    /// The entries whose room file is there load; the rest are in room 0, which is the index, or
    /// in a file that is not there. A file without a room holds no room: 82.LFL holds only sounds,
    /// 99.LFL is the charset.
    /// </summary>
    [Theory]
    [InlineData(nameof(ResourceType.Room), 75, 25)]
    [InlineData(nameof(ResourceType.Costume), 162, 38)]
    [InlineData(nameof(ResourceType.Script), 116, 84)]
    [InlineData(nameof(ResourceType.Sound), 63, 137)]
    public void Index_entries_load_or_are_in_no_room(string typeName, int loaded, int missing)
    {
        var type = Enum.Parse<ResourceType>(typeName);

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
        Assert.Equal(75, rooms.Count);
        Assert.Null(loader.LoadRoom(82));
        Assert.Null(loader.LoadRoom(99));
        Assert.All(rooms, room =>
        {
            Assert.True(room.Width > 0 && room.Height > 0, $"room {room.Number}: {room.Width}x{room.Height}");
            Assert.True(room.ImageOffset > 0 && room.BlockOffsets.ContainsKey("PA"), $"room {room.Number} has no picture or palette");
            Assert.All(room.Objects, o => Assert.InRange(o.Number, 1, loader.Index.GlobalObjectCount - 1));
            Assert.All(room.LocalScriptOffsets.Keys, s => Assert.InRange(s, 200, 255));
            Assert.All(room.LocalScriptOffsets.Keys, s => Assert.False(room.LocalScript(s)!.Value.IsEmpty));
        });
    }

    /// <summary>The cliff: its objects, with the EGA's numbers, and its boxes, 18 bytes each as the EGA's are.</summary>
    [Fact]
    public void Objects_have_the_egas_numbers()
    {
        if (Open()?.LoadRoom(2) is not { } room)
        {
            return;
        }

        Assert.Equal((3, true, true), (room.Version, room.SmallHeader, room.Old256));
        Assert.Equal((960, 144), (room.Width, room.Height));
        Assert.Equal(7, room.Objects.Count);
        Assert.Equal(("leaf", "sky"), (room.FindObject(154)?.Name, room.FindObject(156)?.Name));
        var sky = room.FindObject(156)!;
        Assert.Equal((616, 8, 104, 40, 808, 59), (sky.X, sky.Y, sky.Width, sky.Height, sky.WalkX, sky.WalkY));
        Assert.Equal([54, 57, 58, 59, 64, 86], sky.Verbs.Select(v => (int)v.Verb));
        Assert.Equal(15, room.WalkBoxes.Count);
        Assert.Equal(99, room.WalkBoxes.Matrix.Length);
    }

    /// <summary>The charset is <c>99.LFL</c>, charset 0, after its size word, as the EGA's.</summary>
    [Fact]
    public void The_charset_loads()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        Assert.Equal(1158, loader.LoadCharset(0)!.Value.Length);
        Assert.Null(loader.LoadCharset(1));
    }

    /// <summary>
    /// Every global script decodes with version 3's layouts as one straight run of instructions
    /// that ends exactly where the script does, the FM Towns' CD query (<c>startMusic</c>) with its
    /// result: read as the other releases' <c>startMusic</c>, scripts 87, 97, 104 and 110 do not.
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
        Assert.Equal((116, 9165), (scripts, instructions));
        Assert.Throws<InvalidDataException>(() => new ScriptDecoder(3).DecodeAll(loader.LoadScript(97)!.Value).ToList());
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
        Assert.Equal((879, 16875), (scripts, instructions));
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

    internal static IEnumerable<Room> Rooms(ResourceLoader loader)
    {
        for (int number = 1; number < loader.Index.Count(ResourceType.Room); number++)
        {
            if (loader.HasRoomFile(number) && loader.LoadRoom(number) is { } room)
            {
                yield return room;
            }
        }
    }

    internal static ResourceLoader? Open()
        => GameFolders.Find(GameFolders.FmTowns) is { } path ? ResourceLoader.Open(new DirectoryGameContainer(path)) : null;
}
