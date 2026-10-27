using System.Buffers.Binary;

namespace Distaff.Scumm.Rooms;

/// <summary>
/// A room of the EGA release: its size, objects, walkboxes, entry, exit and local scripts, and the
/// global sounds and scripts loaded with it. The graphics are read when the room is drawn. Mirrors
/// ScummVM's <c>ScummEngine_v3old::setupRoomSubBlocks</c>, <c>resetRoomSubBlocks</c> and
/// <c>resetRoomObjects</c> for version 3.
/// </summary>
/// <remarks>
/// Header, from the room's size word: width and height at 4 and 6 (words, in pixels), the
/// picture's offset at 10, the object count at 20, the walkbox offset at 21 (a word), the sound and
/// script counts at 23 and 24, the exit and entry script offsets at 25 and 27, and from 29 the
/// object image offsets, the object code offsets, the sound numbers, the script numbers, and the
/// local scripts: a number and a word offset each, ended by a 0.
/// </remarks>
public sealed class Room
{
    private const int HeaderSize = 29;

    private readonly int[] _codeStarts;

    private Room(int number, ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length < HeaderSize)
        {
            throw new InvalidDataException($"Room {number} is too short for its header ({span.Length} bytes).");
        }

        Number = number;
        Data = data;
        Width = Word(span, 4);
        Height = Word(span, 6);
        ImageOffset = Word(span, 0x0A);

        int objectCount = span[20];
        int soundCount = span[23];
        int scriptCount = span[24];
        int lists = HeaderSize + objectCount * 4;
        if (lists + soundCount + scriptCount + 1 > span.Length)
        {
            throw new InvalidDataException($"Room {number}: the object, sound and script lists run past the end of the room.");
        }

        var objects = new RoomObject[objectCount];
        for (int i = 0; i < objectCount; i++)
        {
            int image = Word(span, HeaderSize + 2 * i);
            int code = Word(span, HeaderSize + 2 * (objectCount + i));
            objects[i] = RoomObject.Parse(data, image, code);
        }

        Objects = objects;
        Sounds = Numbers(span.Slice(lists, soundCount));
        Scripts = Numbers(span.Slice(lists + soundCount, scriptCount));

        var locals = new SortedDictionary<int, int>();
        int entry = lists + soundCount + scriptCount;
        for (; entry + 3 <= span.Length && span[entry] != 0; entry += 3)
        {
            locals[span[entry]] = Word(span, entry + 1);
        }

        if (entry >= span.Length || span[entry] != 0)
        {
            throw new InvalidDataException($"Room {number}: the local script list runs past the end of the room.");
        }

        LocalScriptOffsets = locals;

        BoxOffset = Word(span, 0x15);
        WalkBoxes = BoxOffset == 0 ? WalkBoxes.Empty : WalkBoxes.Parse(data, BoxOffset, ImageOffset);

        ExitScriptOffset = Word(span, 0x19);
        EntryScriptOffset = Word(span, 0x1B);

        // where each piece of the room starts, so that a script can be cut off where the next
        // piece begins; ScummVM itself only knows where scripts start, and runs them to their end
        _codeStarts = [.. objects.SelectMany(o => new[] { o.ImageOffset, o.CodeOffset }),
            .. locals.Values, ExitScriptOffset, EntryScriptOffset, BoxOffset, ImageOffset, span.Length];
        Array.Sort(_codeStarts);
    }

    public int Number { get; }

    /// <summary>The room resource, from its size word.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>Offset of the background picture and its masks (<c>_IM00_offs</c>).</summary>
    public int ImageOffset { get; }

    /// <summary>Where the walkboxes start in the room: their count, the boxes, then the matrix. 0 for none.</summary>
    public int BoxOffset { get; }

    public IReadOnlyList<RoomObject> Objects { get; }

    public WalkBoxes WalkBoxes { get; }

    /// <summary>Global sounds loaded with the room.</summary>
    public IReadOnlyList<int> Sounds { get; }

    /// <summary>Global scripts loaded with the room.</summary>
    public IReadOnlyList<int> Scripts { get; }

    /// <summary>Where the exit and entry scripts start in the room (<c>_EXCD_offs</c>, <c>_ENCD_offs</c>), 0 for none.</summary>
    public int ExitScriptOffset { get; }

    public int EntryScriptOffset { get; }

    /// <summary>
    /// The room's local scripts by number, from 200 up, and where each starts in the room
    /// (<c>_localScriptOffsets</c>).
    /// </summary>
    public IReadOnlyDictionary<int, int> LocalScriptOffsets { get; }

    public ReadOnlyMemory<byte> ExitScript => CodeAt(ExitScriptOffset);

    public ReadOnlyMemory<byte> EntryScript => CodeAt(EntryScriptOffset);

    /// <summary>A local script's code, or null when the room has no such script.</summary>
    public ReadOnlyMemory<byte>? LocalScript(int number)
        => LocalScriptOffsets.TryGetValue(number, out int offset) ? CodeAt(offset) : null;

    /// <summary>The object with a global number, or null when it is not in this room.</summary>
    public RoomObject? FindObject(int number) => Objects.FirstOrDefault(o => o.Number == number);

    public static Room Parse(int number, ReadOnlyMemory<byte> data) => new(number, data);

    /// <summary>The code from an offset up to where the next piece of the room starts; empty for offset 0.</summary>
    private ReadOnlyMemory<byte> CodeAt(int offset)
    {
        if (offset == 0)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        int next = _codeStarts.FirstOrDefault(start => start > offset, Data.Length);
        return Data[offset..next];
    }

    private static int[] Numbers(ReadOnlySpan<byte> bytes)
    {
        var numbers = new int[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            numbers[i] = bytes[i];
        }

        return numbers;
    }

    private static int Word(ReadOnlySpan<byte> span, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
}
