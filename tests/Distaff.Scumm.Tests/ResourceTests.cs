using Distaff.Scumm.Resources;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>The EGA release's index and resources. Each test passes without checking when the game data is absent.</summary>
public class ResourceTests
{
    [Fact]
    public void Index_holds_the_global_objects_and_every_directory()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var index = loader.Index;
        Assert.Equal(1000, index.GlobalObjectCount);
        Assert.Equal(100, index.Count(ResourceType.Room));
        Assert.Equal(200, index.Count(ResourceType.Costume));
        Assert.Equal(200, index.Count(ResourceType.Script));
        Assert.Equal(80, index.Count(ResourceType.Sound));

        Assert.Equal(87, index[ResourceType.Room].Count(l => l.IsPresent));
        Assert.Equal(176, index[ResourceType.Costume].Count(l => l.IsPresent));
        Assert.Equal(117, index[ResourceType.Script].Count(l => l.IsPresent));
        Assert.Equal(64, index[ResourceType.Sound].Count(l => l.IsPresent));
    }

    /// <summary>Every room file is the room followed by the resources stored with it, each starting with its size word.</summary>
    [Fact]
    public void Every_room_file_is_a_chain_of_resources()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        int files = 0;
        for (int room = 1; room < loader.Index.Count(ResourceType.Room); room++)
        {
            if (!loader.Index.Locate(ResourceType.Room, room).IsPresent || !LflFile.Exists(loader.Game, room))
            {
                continue;
            }

            var file = loader.RoomFile(room).Span;
            int offset = 0;
            while (offset + 2 <= file.Length)
            {
                int size = file[offset] | (file[offset + 1] << 8);
                Assert.True(size >= ResourceLoader.OldHeaderSize, $"room {room}: size {size} at {offset}");
                offset += size;
            }

            Assert.Equal(file.Length, offset);
            files++;
        }

        Assert.Equal(76, files);
    }

    /// <summary>
    /// The entries that point at real resources load; the rest are cut rooms, whose files are not
    /// on the disks, and resources left over from them, whose offsets run past their files.
    /// </summary>
    [Theory]
    [InlineData(nameof(ResourceType.Room), 76, 11, 0)]
    [InlineData(nameof(ResourceType.Costume), 163, 2, 11)]
    [InlineData(nameof(ResourceType.Script), 115, 0, 2)]
    public void Index_entries_load_or_are_accounted_for(string typeName, int loaded, int missingFile, int broken)
    {
        var type = Enum.Parse<ResourceType>(typeName);

        if (Open() is not { } loader)
        {
            return;
        }

        int ok = 0, missing = 0, bad = 0;
        for (int i = 0; i < loader.Index.Count(type); i++)
        {
            if (!loader.Index.Locate(type, i).IsPresent)
            {
                continue;
            }

            try
            {
                if (loader.Load(type, i) is null)
                {
                    missing++;
                }
                else
                {
                    ok++;
                }
            }
            catch (InvalidDataException)
            {
                bad++;
            }
        }

        Assert.Equal((loaded, missingFile, broken), (ok, missing, bad));
    }

    [Fact]
    public void Every_room_parses()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        int rooms = 0;
        for (int number = 1; number < loader.Index.Count(ResourceType.Room); number++)
        {
            if (!LflFile.Exists(loader.Game, number) || loader.LoadRoom(number) is not { } room)
            {
                continue;
            }

            Assert.True(room.Width > 0 && room.Height > 0, $"room {number}: {room.Width}x{room.Height}");
            Assert.All(room.Objects, o => Assert.InRange(o.Number, 1, loader.Index.GlobalObjectCount - 1));
            Assert.All(room.LocalScriptOffsets.Keys, s => Assert.InRange(s, 200, 255));
            Assert.All(room.LocalScriptOffsets.Keys, s => Assert.False(room.LocalScript(s)!.Value.IsEmpty));
            rooms++;
        }

        Assert.Equal(76, rooms);
    }

    [Fact]
    public void The_first_room_has_its_objects_sounds_and_scripts()
    {
        if (Open()?.LoadRoom(1) is not { } room)
        {
            return;
        }

        Assert.Equal((320, 144), (room.Width, room.Height));
        Assert.Equal(53, room.Objects.Count);
        Assert.Equal(100, room.Objects[0].Number);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], room.Sounds);
        Assert.Empty(room.Scripts);
        Assert.Equal(18094, room.LocalScriptOffsets[200]);
        Assert.Equal(1, room.WalkBoxes.Count);
        Assert.Equal(4, room.WalkBoxes.Matrix.Length);
        Assert.Equal(18084, room.ExitScriptOffset);
        Assert.Equal(18085, room.EntryScriptOffset);
        Assert.Equal(1, room.ExitScript.Length);
    }

    [Fact]
    public void Objects_have_their_names()
    {
        if (Open()?.LoadRoom(2) is not { } room)
        {
            return;
        }

        Assert.Equal(960, room.Width);
        Assert.Equal(15, room.WalkBoxes.Count);
        Assert.Equal("leaf", room.FindObject(154)?.Name);
        Assert.Equal("sky", room.FindObject(156)?.Name);
    }

    /// <summary>Room 69 holds the copy protection, script 201, and the script ScummVM starts in its place, 205.</summary>
    [Fact]
    public void The_copy_protection_room_has_both_scripts()
    {
        if (Open()?.LoadRoom(69) is not { } room)
        {
            return;
        }

        Assert.NotNull(room.LocalScript(201));
        Assert.NotNull(room.LocalScript(205));
    }

    /// <summary>
    /// Sounds 1 to 32 have a speaker and an AdLib block, but for 24, whose entry points into the
    /// middle of room 69. Sounds 33 to 64 are the Roland Upgrade's, one block each, in rooms 82 to 85.
    /// </summary>
    [Fact]
    public void Sounds_have_a_speaker_and_an_adlib_block_or_are_the_roland_upgrade()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        for (int i = 1; i <= 32; i++)
        {
            if (i == 24)
            {
                Assert.Throws<InvalidDataException>(() => loader.LoadSound(i));
                continue;
            }

            var sound = loader.LoadSound(i);
            Assert.NotNull(sound);
            Assert.True(sound.Speaker.Length >= 4 && sound.AdLib.Length >= 4, $"sound {i}");
        }

        for (int i = 33; i <= 64; i++)
        {
            Assert.InRange(loader.Index.Locate(ResourceType.Sound, i).Room, 82, 85);
            Assert.NotNull(loader.Load(ResourceType.Sound, i));
        }
    }

    [Fact]
    public void Scripts_and_the_charset_load()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        Assert.Null(loader.LoadScript(117));
        var script = loader.LoadScript(1);
        Assert.NotNull(script);
        Assert.Equal(loader.Load(ResourceType.Script, 1)!.Value.Length - ResourceLoader.OldHeaderSize, script.Value.Length);

        var charset = loader.LoadCharset(0);
        Assert.NotNull(charset);
        Assert.Equal(1158, charset.Value.Length);
        Assert.Null(loader.LoadCharset(1));
    }

    /// <summary>
    /// The glassmaker's greeting (script 67) cannot be reached: it plays in room 22 for a visitor
    /// who comes from the graveyard (room 17), and the graveyard's way to room 22, exit 353 at its
    /// box 10, is reached only through its box 8, where Goodmold greets Bobbin first (script 66,
    /// which sets bit 3513 that both ask after). Without box 8, no box of the graveyard leads to
    /// box 10, from where Bobbin comes in by the other two exits (box 0 and box 11) or anywhere else.
    /// </summary>
    [Fact]
    public void The_graveyards_way_to_the_chalice_passes_goodmold()
    {
        if (Open()?.LoadRoom(17) is not { } room)
        {
            return;
        }

        var boxes = room.WalkBoxes;
        var reached = new HashSet<int> { 0, 11 };
        for (bool more = true; more;)
        {
            more = false;
            foreach (int from in reached.ToList())
            {
                for (int to = 0; to < boxes.Count; to++)
                {
                    if (to != 8 && boxes.NextBox(from, to) == to && reached.Add(to))
                    {
                        more = true;
                    }
                }
            }
        }

        Assert.DoesNotContain(10, reached);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 11], reached.Order());
        Assert.True(boxes.Contains(10, 336, 141), "exit 353 is walked to in box 10");
    }

    private static ResourceLoader? Open()
        => GameFolders.Find(GameFolders.Ega) is { } path ? ResourceLoader.Open(new DirectoryGameContainer(path)) : null;
}
