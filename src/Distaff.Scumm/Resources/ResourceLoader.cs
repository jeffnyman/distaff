using System.Buffers.Binary;
using Distaff.Scumm.Detection;
using Distaff.Scumm.Rooms;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Resources;

/// <summary>
/// Loads resources from the files the index points to. In the EGA release every resource starts
/// with a 4-byte header whose first word is its size, the header included, and a room file is the
/// room followed by the costumes, scripts and sounds stored with it (<c>GF_OLD_BUNDLE</c>). In the
/// VGA CD release a resource's header is a 32-bit size and a 2-letter tag (<c>GF_SMALL_HEADER</c>),
/// and the rooms and what is stored with them are in a disk file's <c>LF</c> blocks. The FM Towns
/// release has the VGA CD's headers and index, and the EGA's room files, not encoded
/// (<c>GF_OLD256</c>, which leaves out <c>GF_USE_KEY</c>), each room at its file's start. Mirrors
/// ScummVM's <c>ScummEngine::loadResource</c> and <c>openRoom</c>. Decoded files are cached.
/// </summary>
public sealed class ResourceLoader
{
    /// <summary>The size of a resource's header in the EGA release (<c>_resourceHeaderSize</c> of <c>ScummEngine_v3old</c>).</summary>
    public const int OldHeaderSize = 4;

    /// <summary>The size of a resource's header in the VGA CD release (<c>_resourceHeaderSize</c> of <c>ScummEngine_v4</c>).</summary>
    public const int SmallHeaderSize = 6;

    private readonly Dictionary<int, byte[]> _files = [];
    private readonly Dictionary<int, DiskBundle> _disks = [];

    public ResourceLoader(IGameContainer game, ResourceIndex index, ResourceLayout layout = ResourceLayout.V3Old)
    {
        Game = game;
        Index = index;
        Layout = layout;
    }

    public ResourceIndex Index { get; }

    /// <summary>The files of the release.</summary>
    public IGameContainer Game { get; }

    public ResourceLayout Layout { get; }

    /// <summary>The SCUMM version the release's scripts are written for: 3, or 4 for the VGA CD.</summary>
    public int Version => Layout == ResourceLayout.V4 ? 4 : 3;

    /// <summary>Whether the release is the FM Towns', whose scripts differ in places.</summary>
    public bool FmTowns => Layout == ResourceLayout.V3Old256;

    /// <summary>The size of every resource's header.</summary>
    public int HeaderSize => Layout == ResourceLayout.V3Old ? OldHeaderSize : SmallHeaderSize;

    /// <summary>Whether each room is in a file of its own, <c>NN.LFL</c>, as in the EGA and FM Towns releases.</summary>
    private bool RoomFiles => Layout != ResourceLayout.V4;

    /// <summary>
    /// Reads the index, from <c>00.LFL</c> or <c>000.LFL</c>, and returns a loader over it. The
    /// EGA, VGA CD and FM Towns layouts are read; the PC Engine's throws <see cref="NotSupportedException"/>.
    /// </summary>
    public static ResourceLoader Open(IGameContainer game)
    {
        var release = ScummDetector.Identify(game)
            ?? throw new InvalidDataException($"No Loom release found in {game.Name}.");
        return release.Layout switch
        {
            ResourceLayout.V3Old => new(game, ResourceIndex.Parse(LflFile.Read(game, 0)), release.Layout),

            // 000.LFL is not encoded (getEncByte gives version 4's room 0 no key)
            ResourceLayout.V4 => new(game, ResourceIndex.ParseV4(game.ReadAllBytes("000.LFL")), release.Layout),

            // the FM Towns' index is laid out as the VGA CD's (ScummEngine_v3 takes v4's readIndexFile)
            ResourceLayout.V3Old256 when release.Platform == "fmtowns" => new(game, ResourceIndex.ParseV4(game.ReadAllBytes("00.LFL")), release.Layout),
            _ => throw new NotSupportedException($"Loom ({release.Variant}) is not supported; only the EGA, VGA CD and FM Towns releases' resources are read."),
        };
    }

    /// <summary>
    /// Whether the file holding a room is there: its own file, or for the VGA CD its disk file,
    /// which must hold it. Room 0's is the index; what the index puts in room 0 is in no file
    /// (<c>loadResource</c> looks for it in the current room).
    /// </summary>
    public bool HasRoomFile(int room)
    {
        if (RoomFiles)
        {
            return room > 0 && LflFile.Exists(Game, room);
        }

        return room >= 0 && room < Index.RoomDisks.Count && DiskBundle.Exists(Game, Index.RoomDisks[room]) && Disk(Index.RoomDisks[room]).Holds(room);
    }

    /// <summary>The decoded contents of an EGA or FM Towns room file.</summary>
    public ReadOnlyMemory<byte> RoomFile(int room)
    {
        if (!_files.TryGetValue(room, out var data))
        {
            data = Layout == ResourceLayout.V3Old ? LflFile.Read(Game, room) : Game.ReadAllBytes(LflFile.NameOf(room));
            _files[room] = data;
        }

        return data;
    }

    /// <summary>A decoded VGA CD disk file.</summary>
    public DiskBundle Disk(int disk)
    {
        if (!_disks.TryGetValue(disk, out var bundle))
        {
            bundle = DiskBundle.Read(Game, disk);
            _disks[disk] = bundle;
        }

        return bundle;
    }

    /// <summary>
    /// The bytes of a resource, starting with its header, or null when the index has no entry
    /// for it or its room's file is missing. Throws <see cref="InvalidDataException"/> when the
    /// entry points outside its file. For the EGA release, a sound's first block only;
    /// <see cref="LoadSound"/> reads both.
    /// </summary>
    public ReadOnlyMemory<byte>? Load(ResourceType type, int index)
    {
        if (Find(type, index) is not { } found)
        {
            return null;
        }

        var (file, offset) = found;
        int size = Layout == ResourceLayout.V3Old
            ? BinaryPrimitives.ReadUInt16LittleEndian(file.Span[offset..])
            : BinaryPrimitives.ReadInt32LittleEndian(file.Span[offset..]);
        if (size < HeaderSize || offset + size > file.Length)
        {
            throw new InvalidDataException($"{type} {index}: size {size} at offset {offset} does not fit in {FileOf(type, index)} ({file.Length} bytes).");
        }

        return file.Slice(offset, size);
    }

    /// <summary>Loads and parses a room, or returns null when the index has no such room.</summary>
    public Room? LoadRoom(int room)
        => Load(ResourceType.Room, room) is not { } data ? null
            : Layout == ResourceLayout.V3Old ? Room.Parse(room, data) : Room.ParseV4(room, data, Layout == ResourceLayout.V3Old256);

    /// <summary>A global script's code: the resource after its header (<c>getScriptBaseAddress</c> with <c>_resourceHeaderSize</c>).</summary>
    public ReadOnlyMemory<byte>? LoadScript(int script)
    {
        // not a conditional expression: its null would become an empty ReadOnlyMemory, by way of byte[]
        if (Load(ResourceType.Script, script) is not { } data)
        {
            return null;
        }

        return data[HeaderSize..];
    }

    /// <summary>Loads an EGA sound's speaker and AdLib blocks, or returns null when the index has no such sound.</summary>
    public SoundResource? LoadSound(int sound)
    {
        if (Layout != ResourceLayout.V3Old)
        {
            throw new NotSupportedException("The VGA CD release's sounds are not read yet.");
        }

        return Find(ResourceType.Sound, sound) is { } found ? SoundResource.Parse(sound, found.File, found.Offset) : null;
    }

    /// <summary>
    /// A charset, or null when its file is missing. These files are not encoded. For the EGA and
    /// FM Towns releases it is <c>99.LFL</c> for charset 0, counting down, and the data follows the
    /// size word, as <c>ScummEngine_v3::loadCharset</c> reads it. For the VGA CD it is <c>901.LFL</c> for
    /// charset 1, counting up, and the data is the size its first 32 bits give and 11 bytes more,
    /// after them, as <c>ScummEngine_v4::loadCharset</c> reads it.
    /// </summary>
    public ReadOnlyMemory<byte>? LoadCharset(int charset)
    {
        string name = RoomFiles ? LflFile.NameOf(99 - charset) : $"{900 + charset:D3}.LFL";
        if (!Game.Exists(name))
        {
            return null;
        }

        var data = Game.ReadAllBytes(name);
        if (RoomFiles)
        {
            if (data.Length < 2)
            {
                throw new InvalidDataException($"Charset {charset}: {name} is too short.");
            }

            // 99.LFL's size word claims more than the file holds; ScummVM reads what is there
            int size = BinaryPrimitives.ReadUInt16LittleEndian(data);
            return data.AsMemory(2, Math.Min(size, data.Length - 2));
        }

        if (data.Length < 4 || BinaryPrimitives.ReadInt32LittleEndian(data) + 11 > data.Length - 4)
        {
            throw new InvalidDataException($"Charset {charset}: {name} is shorter than its size.");
        }

        return data.AsMemory(4, BinaryPrimitives.ReadInt32LittleEndian(data) + 11);
    }

    /// <summary>The file a resource is in, for messages.</summary>
    private string FileOf(ResourceType type, int index)
    {
        int room = Index.Locate(type, index).Room;
        return RoomFiles ? LflFile.NameOf(room) : $"room {room} of {DiskBundle.NameOf(Index.RoomDisks[room])}";
    }

    /// <summary>The decoded file a resource is in and where it starts there.</summary>
    private (ReadOnlyMemory<byte> File, int Offset)? Find(ResourceType type, int index)
    {
        var location = Index.Locate(type, index);
        if (!location.IsPresent || !HasRoomFile(location.Room))
        {
            return null;
        }

        // the FM Towns' index gives every room its file's start; a file there without a room
        // (82.LFL holds sounds only, 99.LFL the charset) holds none
        if (type == ResourceType.Room && Layout == ResourceLayout.V3Old256
            && RoomFile(location.Room).Span is not [_, _, _, _, (byte)'R', (byte)'O', ..])
        {
            return null;
        }

        ReadOnlyMemory<byte> file;
        int offset;
        if (RoomFiles)
        {
            file = RoomFile(location.Room);
            offset = location.Offset;
        }
        else
        {
            var disk = Disk(Index.RoomDisks[location.Room]);
            file = disk.Data;
            offset = disk.RoomStart(location.Room) + location.Offset;
        }

        if (offset + HeaderSize > file.Length)
        {
            throw new InvalidDataException($"{type} {index}: offset {location.Offset} is past the end of {FileOf(type, index)} ({file.Length} bytes).");
        }

        return (file, offset);
    }
}
