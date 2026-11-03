using System.Buffers.Binary;
using System.Text;

namespace Distaff.Scumm.Resources;

/// <summary>The resource types the index locates. Mirrors ScummVM's <c>rtRoom</c>, <c>rtCostume</c>, <c>rtScript</c> and <c>rtSound</c>.</summary>
internal enum ResourceType
{
    Room,
    Costume,
    Script,
    Sound,
}

/// <summary>
/// Where a resource lives: the room file that holds it and its offset in that file. Rooms
/// themselves always start at offset 0 of their own file.
/// </summary>
internal readonly record struct ResourceLocation(int Room, int Offset)
{
    /// <summary>Offset value meaning "no resource". Mirrors ScummVM's <c>0xFFFF</c> check before <c>RES_INVALID_OFFSET</c>.</summary>
    public const int Invalid = 0xFFFF;

    public static readonly ResourceLocation None = new(0, Invalid);

    public bool IsPresent => Offset != Invalid;
}

/// <summary>
/// The index in room 0 (<c>00.LFL</c>, or <c>000.LFL</c> for the VGA CD): the classes, initial
/// owner and state of every global object, and where every room, costume, script and sound is.
/// Mirrors ScummVM's <c>ScummEngine_v3old::readIndexFile</c>, <c>ScummEngine_v4::readIndexFile</c>,
/// <c>ScummEngine_v4::readGlobalObjects</c> and the <c>readResTypeList</c> of both.
/// </summary>
/// <remarks>
/// The index is taken at face value, as ScummVM takes it. Entry 0 of each type is never used, and
/// some entries point at no real resource: rooms that were cut, whose files are not on the disks,
/// and resources left over from them, whose offsets run past the end of their files.
/// </remarks>
internal sealed class ResourceIndex
{
    /// <summary>First word of the index. Mirrors the magic check of <c>readIndexFile</c>.</summary>
    public const ushort Magic = 0x0100;

    /// <summary>Mirrors ScummVM's <c>OF_OWNER_MASK</c>.</summary>
    private const byte OwnerMask = 0x0F;

    /// <summary>Mirrors ScummVM's <c>OF_STATE_SHL</c>.</summary>
    private const int StateShift = 4;

    /// <summary>The offset meaning "no resource" in a version 4 index (<c>RES_INVALID_OFFSET</c>).</summary>
    private const uint InvalidV4 = 0xFFFFFFFF;

    private readonly ResourceLocation[][] _locations;

    private ResourceIndex(int[] classes, byte[] owners, byte[] states, ResourceLocation[][] locations, int[] disks, IReadOnlyDictionary<int, string> roomNames)
    {
        ObjectClasses = classes;
        ObjectOwners = owners;
        ObjectStates = states;
        _locations = locations;
        RoomDisks = disks;
        RoomNames = roomNames;
    }

    /// <summary>The initial class bits of each global object (24 bits, <c>_classData</c>).</summary>
    public IReadOnlyList<int> ObjectClasses { get; }

    /// <summary>The initial owner of each global object: an actor, or 0x0F for "in its room".</summary>
    public IReadOnlyList<byte> ObjectOwners { get; }

    /// <summary>The initial state of each global object.</summary>
    public IReadOnlyList<byte> ObjectStates { get; }

    public int GlobalObjectCount => ObjectOwners.Count;

    /// <summary>The disk each room is on, for the VGA CD (<c>_roomno</c> of a room); empty for the EGA release.</summary>
    public IReadOnlyList<int> RoomDisks { get; }

    /// <summary>The rooms' names, which the VGA CD's index keeps (its <c>RN</c> block); empty for the EGA release.</summary>
    public IReadOnlyDictionary<int, string> RoomNames { get; }

    public IReadOnlyList<ResourceLocation> this[ResourceType type] => _locations[(int)type];

    public int Count(ResourceType type) => _locations[(int)type].Length;

    public ResourceLocation Locate(ResourceType type, int index)
    {
        var locations = _locations[(int)type];
        return index >= 0 && index < locations.Length ? locations[index] : ResourceLocation.None;
    }

    public static ResourceIndex Parse(ReadOnlySpan<byte> data)
    {
        var reader = new IndexReader(data);
        ushort magic = reader.Word();
        if (magic != Magic)
        {
            throw new InvalidDataException($"Unknown index magic 0x{magic:X4}; expected 0x{Magic:X4}.");
        }

        int count = reader.Word();
        var classes = new int[count];
        var owners = new byte[count];
        var states = new byte[count];
        for (int i = 0; i < count; i++)
        {
            var bytes = reader.Bytes(4);
            classes[i] = bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
            owners[i] = (byte)(bytes[3] & OwnerMask);
            states[i] = (byte)(bytes[3] >> StateShift);
        }

        var locations = new[]
        {
            ReadResourceList(ref reader, ResourceType.Room),
            ReadResourceList(ref reader, ResourceType.Costume),
            ReadResourceList(ref reader, ResourceType.Script),
            ReadResourceList(ref reader, ResourceType.Sound),
        };
        return new ResourceIndex(classes, owners, states, locations, [], new Dictionary<int, string>());
    }

    /// <summary>
    /// Reads the VGA CD's index, <c>000.LFL</c>: blocks of a 32-bit size and a 2-letter tag. <c>RN</c>
    /// holds the room names, <c>0R</c>, <c>0S</c>, <c>0N</c> and <c>0C</c> the rooms, scripts, sounds and
    /// costumes (a count, then a room and a 32-bit offset each; a room's "room" is its disk), and
    /// <c>0O</c> the global objects.
    /// </summary>
    public static ResourceIndex ParseV4(ReadOnlySpan<byte> data)
    {
        int[] classes = [];
        byte[] owners = [];
        byte[] states = [];
        int[] disks = [];
        var names = new Dictionary<int, string>();
        var locations = new ResourceLocation[4][];
        for (int at = 0; at < data.Length;)
        {
            if (at + 6 > data.Length)
            {
                throw new InvalidDataException($"The index ends inside a block header at offset {at}.");
            }

            int size = BinaryPrimitives.ReadInt32LittleEndian(data[at..]);
            if (size < 6 || at + size > data.Length)
            {
                throw new InvalidDataException($"Index block at {at} has a bad size {size}.");
            }

            var reader = new IndexReader(data.Slice(at + 6, size - 6));
            switch ((char)data[at + 4], (char)data[at + 5])
            {
                case ('R', 'N'):
                    // a room number and its name in 9 bytes XOR 0xFF, ended by room 0
                    for (int room; (room = reader.Byte()) != 0;)
                    {
                        var name = reader.Bytes(9).ToArray();
                        for (int i = 0; i < name.Length; i++)
                        {
                            name[i] ^= 0xFF;
                        }

                        int end = Array.IndexOf(name, (byte)0);
                        names[room] = Encoding.Latin1.GetString(name, 0, end < 0 ? name.Length : end);
                    }

                    break;
                case ('0', 'R'):
                    (locations[(int)ResourceType.Room], disks) = ReadResourceListV4(ref reader, ResourceType.Room);
                    break;
                case ('0', 'S'):
                    locations[(int)ResourceType.Script] = ReadResourceListV4(ref reader, ResourceType.Script).Locations;
                    break;
                case ('0', 'N'):
                    locations[(int)ResourceType.Sound] = ReadResourceListV4(ref reader, ResourceType.Sound).Locations;
                    break;
                case ('0', 'C'):
                    locations[(int)ResourceType.Costume] = ReadResourceListV4(ref reader, ResourceType.Costume).Locations;
                    break;
                case ('0', 'O'):
                    int count = reader.Word();
                    classes = new int[count];
                    owners = new byte[count];
                    states = new byte[count];
                    for (int i = 0; i < count; i++)
                    {
                        var bytes = reader.Bytes(4);
                        classes[i] = bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
                        owners[i] = (byte)(bytes[3] & OwnerMask);
                        states[i] = (byte)(bytes[3] >> StateShift);
                    }

                    break;
                default:
                    throw new InvalidDataException($"Unknown index block '{(char)data[at + 4]}{(char)data[at + 5]}' at offset {at}.");
            }

            at += size;
        }

        if (locations.Any(l => l is null))
        {
            throw new InvalidDataException("The index lacks a directory of rooms, scripts, sounds or costumes.");
        }

        return new ResourceIndex(classes, owners, states, locations, disks, names);
    }

    /// <summary>
    /// A count, a list of room numbers, then a list of 16-bit offsets. For rooms the first list
    /// holds disk numbers, which ScummVM skips: each room is at the start of its own file.
    /// </summary>
    private static ResourceLocation[] ReadResourceList(ref IndexReader reader, ResourceType type)
    {
        int count = reader.Byte();
        var rooms = reader.Bytes(count);
        var locations = new ResourceLocation[count];
        for (int i = 0; i < count; i++)
        {
            int offset = reader.Word();
            locations[i] = offset == ResourceLocation.Invalid
                ? ResourceLocation.None
                : type == ResourceType.Room ? new ResourceLocation(i, 0) : new ResourceLocation(rooms[i], offset);
        }

        return locations;
    }

    /// <summary>A count, then a room (for rooms, the disk) and a 32-bit offset for each entry.</summary>
    private static (ResourceLocation[] Locations, int[] Rooms) ReadResourceListV4(ref IndexReader reader, ResourceType type)
    {
        int count = reader.Word();
        var locations = new ResourceLocation[count];
        var rooms = new int[count];
        for (int i = 0; i < count; i++)
        {
            rooms[i] = reader.Byte();
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(reader.Bytes(4));
            locations[i] = offset == InvalidV4
                ? ResourceLocation.None
                : type == ResourceType.Room ? new ResourceLocation(i, 0) : new ResourceLocation(rooms[i], checked((int)offset));
        }

        return (locations, rooms);
    }

    private ref struct IndexReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position;

        public byte Byte() => Take(1)[0];

        public ushort Word() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

        public ReadOnlySpan<byte> Bytes(int count) => Take(count);

        private ReadOnlySpan<byte> Take(int count)
        {
            if (_position + count > _data.Length)
            {
                throw new InvalidDataException($"The index ends early: {count} bytes wanted at offset {_position} of {_data.Length}.");
            }

            var slice = _data.Slice(_position, count);
            _position += count;
            return slice;
        }
    }
}
