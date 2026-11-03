using System.Buffers.Binary;

namespace Distaff.Scumm.Rooms;

/// <summary>
/// A room: its size, objects, walkboxes, entry, exit and local scripts, and the global sounds and
/// scripts loaded with it. The graphics are read when the room is drawn. Mirrors ScummVM's
/// <c>ScummEngine_v3old::setupRoomSubBlocks</c>, <c>resetRoomSubBlocks</c> and
/// <c>resetRoomObjects</c> for the EGA release, and the <c>GF_SMALL_HEADER</c> paths of
/// <c>ScummEngine::setupRoomSubBlocks</c>, <c>resetRoomSubBlocks</c> and
/// <c>ScummEngine_v4::resetRoomObjects</c> for the VGA CD and the FM Towns release, whose rooms
/// are laid out alike (see <see cref="ParseV4"/>).
/// </summary>
/// <remarks>
/// Header, from the room's size word: width and height at 4 and 6 (words, in pixels), the
/// picture's offset at 10, the object count at 20, the walkbox offset at 21 (a word), the sound and
/// script counts at 23 and 24, the exit and entry script offsets at 25 and 27, and from 29 the
/// object image offsets, the object code offsets, the sound numbers, the script numbers, and the
/// local scripts: a number and a word offset each, ended by a 0.
/// </remarks>
internal sealed class Room
{
    private const int HeaderSize = 29;

    /// <summary>The size of a VGA CD block's header: a 32-bit size and a 2-letter tag.</summary>
    private const int SmallHeaderSize = 6;

    private readonly int[] _codeStarts;

    /// <summary>
    /// Reads a VGA CD room: a chain of blocks, each a 32-bit size and a 2-letter tag, after the
    /// room's own header. <c>HD</c> gives the width, height and object count; <c>BM</c> is the
    /// picture; <c>BX</c> the walkboxes and matrix; <c>EX</c> and <c>EN</c> the exit and entry
    /// scripts; <c>LS</c> a local script, its number first; <c>OC</c> and <c>OI</c> an object's code
    /// and image; <c>NL</c> and <c>SL</c> the sounds and scripts loaded with the room, a count first.
    /// Offsets are of each block's data, after its header, as <c>findResourceData</c> gives them; an
    /// object's are of its blocks' starts.
    /// </summary>
    private Room(int number, ReadOnlyMemory<byte> data, int blockHeaderSize, bool old256)
    {
        var span = data.Span;
        Number = number;
        Data = data;
        Version = old256 ? 3 : 4;
        SmallHeader = true;
        Old256 = old256;
        var single = new Dictionary<string, int>();
        var codes = new List<int>();
        var images = new Dictionary<int, int>();
        var locals = new SortedDictionary<int, int>();
        var starts = new List<int> { span.Length };
        for (int at = blockHeaderSize; at < span.Length;)
        {
            int size = at + blockHeaderSize <= span.Length ? BinaryPrimitives.ReadInt32LittleEndian(span[at..]) : 0;
            if (size < blockHeaderSize || at + size > span.Length)
            {
                throw new InvalidDataException($"Room {number}: the block at {at} has a bad size {size}.");
            }

            string tag = $"{(char)span[at + 4]}{(char)span[at + 5]}";
            int body = at + blockHeaderSize;
            starts.Add(at);
            switch (tag)
            {
                case "OC":
                    codes.Add(at);
                    break;
                case "OI":
                    images[BinaryPrimitives.ReadUInt16LittleEndian(span[body..])] = at;
                    break;
                case "LS":
                    locals[span[body]] = body + 1;
                    break;
                default:
                    single.TryAdd(tag, body);
                    break;
            }

            at += size;
        }

        BlockOffsets = single;
        if (!single.TryGetValue("HD", out int header))
        {
            throw new InvalidDataException($"Room {number} has no header block.");
        }

        Width = Word(span, header);
        Height = Word(span, header + 2);
        int objectCount = Word(span, header + 4);
        if (objectCount != codes.Count)
        {
            throw new InvalidDataException($"Room {number}: its header gives {objectCount} objects, but it holds {codes.Count} code blocks.");
        }

        ImageOffset = single.GetValueOrDefault("BM");
        Objects = [.. codes.Select(code => RoomObject.Parse(data, images.GetValueOrDefault(Word(data.Span, code + blockHeaderSize)), code, smallHeader: true))];
        Sounds = single.TryGetValue("NL", out int sounds) ? Numbers(span.Slice(sounds + 1, span[sounds])) : [];
        Scripts = single.TryGetValue("SL", out int scripts) ? Numbers(span.Slice(scripts + 1, span[scripts])) : [];
        LocalScriptOffsets = locals;

        BoxOffset = single.GetValueOrDefault("BX");
        WalkBoxes = BoxOffset == 0 ? WalkBoxes.Empty
            : WalkBoxes.Parse(data, BoxOffset, BoxOffset - blockHeaderSize + BinaryPrimitives.ReadInt32LittleEndian(span[(BoxOffset - blockHeaderSize)..]),
                old256 ? WalkBoxes.BoxSize : WalkBoxes.BoxSizeV4);
        ScaleSlots = single.TryGetValue("SA", out int scales)
            ? ScaleSlot.ReadRoomSlots(span.Slice(scales, BinaryPrimitives.ReadInt32LittleEndian(span[(scales - blockHeaderSize)..]) - blockHeaderSize))
            : new Dictionary<int, ScaleSlot>();
        ExitScriptOffset = single.GetValueOrDefault("EX");
        EntryScriptOffset = single.GetValueOrDefault("EN");

        // a script runs to the end of its block
        _codeStarts = [.. starts.Order()];
    }

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

    /// <summary>The SCUMM version of the release the room is from: 3 for the EGA and FM Towns, 4 for the VGA CD.</summary>
    public int Version { get; } = 3;

    /// <summary>Whether the room is a chain of blocks with 6-byte headers, as in the VGA CD and FM Towns releases (<c>GF_SMALL_HEADER</c>).</summary>
    public bool SmallHeader { get; }

    /// <summary>Whether the room is from the FM Towns release, whose pictures are drawn as ScummVM's <c>GF_OLD256</c> games' are.</summary>
    public bool Old256 { get; }

    /// <summary>
    /// Where each block a VGA CD room has one of starts, by tag, after its header: <c>HD</c>,
    /// <c>BM</c>, <c>PA</c> (the palette), <c>SP</c>, <c>CC</c> (colour cycling), <c>SA</c> (the
    /// scale slots), <c>BX</c>, <c>EX</c>, <c>EN</c>, <c>NL</c>, <c>SL</c> and <c>LC</c>. Empty for the EGA
    /// release.
    /// </summary>
    public IReadOnlyDictionary<string, int> BlockOffsets { get; } = new Dictionary<string, int>();

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

    /// <summary>The scale slots a VGA CD room sets as it is entered, by number from 1; none for the EGA.</summary>
    public IReadOnlyDictionary<int, ScaleSlot> ScaleSlots { get; } = new Dictionary<int, ScaleSlot>();

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

    /// <summary>Parses a VGA CD room, or with <paramref name="old256"/> an FM Towns one, from its <c>RO</c> block's header.</summary>
    public static Room ParseV4(int number, ReadOnlyMemory<byte> data, bool old256 = false) => new(number, data, SmallHeaderSize, old256);

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
