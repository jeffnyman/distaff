using System.Buffers.Binary;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Resources;

/// <summary>
/// A disk file of the VGA CD release, <c>DISK01.LEC</c>: every byte XOR-ed with
/// <see cref="EncodingKey"/>, one <c>LE</c> block holding an <c>FO</c> block of room offsets and,
/// at each offset, the room's <c>LF</c> block: its number, then the room and the scripts,
/// costumes and sounds stored with it. Mirrors ScummVM's <c>generateFilename</c> and
/// <c>getEncByte</c> for version 4, and <c>readRoomsOffsets</c>.
/// </summary>
internal sealed class DiskBundle
{
    public const byte EncodingKey = 0x69;

    /// <summary>
    /// Where a room's resources start in its <c>LF</c> block: after the block's size, tag and room
    /// number. Mirrors the seek <c>loadResource</c> makes for version 4.
    /// </summary>
    public const int RoomHeaderSize = 8;

    /// <summary>Where the count of the room offsets is: after the <c>LE</c> and <c>FO</c> headers (<c>readRoomsOffsets</c> with <c>GF_SMALL_HEADER</c>).</summary>
    private const int OffsetTable = 12;

    private readonly Dictionary<int, int> _rooms;

    private DiskBundle(int disk, byte[] data, Dictionary<int, int> rooms)
    {
        Disk = disk;
        Data = data;
        _rooms = rooms;
    }

    public int Disk { get; }

    /// <summary>The decoded file.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>The rooms the file holds, in order.</summary>
    public IEnumerable<int> Rooms => _rooms.Keys.Order();

    /// <summary>The file holding a disk: <c>DISKnn.LEC</c>.</summary>
    public static string NameOf(int disk) => $"DISK{disk:D2}.LEC";

    public static bool Exists(IGameContainer game, int disk) => game.Exists(NameOf(disk));

    public static DiskBundle Read(IGameContainer game, int disk)
    {
        var data = game.ReadAllBytes(NameOf(disk));
        for (int i = 0; i < data.Length; i++)
        {
            data[i] ^= EncodingKey;
        }

        if (data.Length <= OffsetTable || data[4] != 'L' || data[5] != 'E' || data[10] != 'F' || data[11] != 'O')
        {
            throw new InvalidDataException($"{NameOf(disk)} does not start with an LE block holding an FO block.");
        }

        int count = data[OffsetTable];
        if (OffsetTable + 1 + count * 5 > data.Length)
        {
            throw new InvalidDataException($"{NameOf(disk)}: the room offsets run past the end of the file.");
        }

        var rooms = new Dictionary<int, int>();
        for (int i = 0; i < count; i++)
        {
            int at = OffsetTable + 1 + i * 5;
            rooms[data[at]] = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at + 1));
        }

        return new DiskBundle(disk, data, rooms);
    }

    public bool Holds(int room) => _rooms.ContainsKey(room);

    /// <summary>Where a room's resources start in the file: its <c>LF</c> block, after the block's header.</summary>
    public int RoomStart(int room)
        => _rooms.TryGetValue(room, out int offset)
            ? offset + RoomHeaderSize
            : throw new InvalidDataException($"Room {room} is not in {NameOf(Disk)}.");
}
