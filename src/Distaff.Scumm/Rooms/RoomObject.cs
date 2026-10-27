using System.Buffers.Binary;
using System.Text;

namespace Distaff.Scumm.Rooms;

/// <summary>One entry of an object's verb table: the verb and where its script starts in the object's code block.</summary>
public readonly record struct VerbEntry(byte Verb, int Offset);

/// <summary>
/// An object as its room defines it: the object code block (OBCD). Positions and sizes are in
/// pixels, as ScummVM's <c>ObjectData</c> keeps them. Mirrors the <c>GF_OLD_BUNDLE</c> branches
/// of <c>ScummEngine_v4::resetRoomObject</c>, <c>getObjOrActorName</c> and <c>getVerbEntrypoint</c>
/// for version 3.
/// </summary>
/// <remarks>
/// The code block starts with its size word. Bytes 4 and 5 hold the object number, 7 to 10 the
/// position, width and parent (in 8-pixel units), 11 to 14 the walk-to point (two words, in
/// pixels), 15 the actor's direction and the height, 16 the offset of the name, and from 17 the
/// verb table: a verb and a word offset each, ended by a 0 verb.
/// </remarks>
public sealed class RoomObject
{
    /// <summary>The verb-table entry that matches any verb. Mirrors the <c>0xFF</c> check in <c>getVerbEntrypoint</c>.</summary>
    public const byte FallbackVerb = 0xFF;

    private const int VerbTable = 17;

    private RoomObject(ReadOnlyMemory<byte> code, int imageOffset, int codeOffset)
    {
        var block = code.Span;
        Code = code;
        ImageOffset = imageOffset;
        CodeOffset = codeOffset;
        Number = BinaryPrimitives.ReadUInt16LittleEndian(block[4..]);
        X = block[7] * 8;
        Y = (block[8] & 0x7F) * 8;
        ParentState = (block[8] & 0x80) != 0 ? 1 : 0;
        Width = block[9] * 8;
        Parent = block[10];
        WalkX = BinaryPrimitives.ReadInt16LittleEndian(block[11..]);
        WalkY = BinaryPrimitives.ReadInt16LittleEndian(block[13..]);
        ActorDir = block[15] & 0x07;
        Height = block[15] & 0xF8;
        Name = ReadName(block, block[16]);
        Verbs = ReadVerbs(block);
    }

    /// <summary>The global object number.</summary>
    public int Number { get; }

    /// <summary>Offset of the object's image in the room (OBIM), used when the object draws itself.</summary>
    public int ImageOffset { get; }

    /// <summary>Offset of the object's code block in the room (OBCD).</summary>
    public int CodeOffset { get; }

    /// <summary>The whole code block, from its size word; verb offsets are relative to its start.</summary>
    public ReadOnlyMemory<byte> Code { get; }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>1-based index in the room's object list of the object this one depends on, or 0.</summary>
    public int Parent { get; }

    /// <summary>The state the parent must be in for this object to be visible: 0 or 1.</summary>
    public int ParentState { get; }

    public int WalkX { get; }

    public int WalkY { get; }

    /// <summary>The direction the actor faces to use the object (the low 3 bits of byte 15).</summary>
    public int ActorDir { get; }

    public string Name { get; }

    public IReadOnlyList<VerbEntry> Verbs { get; }

    /// <summary>
    /// The offset of the script that handles a verb, or null when the object has none. The fallback
    /// entry matches any verb.
    /// </summary>
    public int? FindVerb(byte verb)
    {
        foreach (var entry in Verbs)
        {
            if (entry.Verb == verb || entry.Verb == FallbackVerb)
            {
                return entry.Offset;
            }
        }

        return null;
    }

    internal static RoomObject Parse(ReadOnlyMemory<byte> room, int imageOffset, int codeOffset)
    {
        // the header up to the verb table, and the 0 that ends it
        var span = room.Span;
        const int smallest = VerbTable + 1;
        if (codeOffset + smallest > span.Length)
        {
            throw new InvalidDataException($"Object code block at {codeOffset} is past the end of the room ({span.Length} bytes).");
        }

        int size = BinaryPrimitives.ReadUInt16LittleEndian(span[codeOffset..]);
        if (size < smallest || codeOffset + size > span.Length)
        {
            throw new InvalidDataException($"Object code block at {codeOffset} has a bad size {size}.");
        }

        return new RoomObject(room.Slice(codeOffset, size), imageOffset, codeOffset);
    }

    private static string ReadName(ReadOnlySpan<byte> block, int offset)
    {
        if (offset == 0 || offset >= block.Length)
        {
            return string.Empty;
        }

        int end = block[offset..].IndexOf((byte)0);
        var name = end < 0 ? block[offset..] : block.Slice(offset, end);
        return Encoding.Latin1.GetString(name);
    }

    private static VerbEntry[] ReadVerbs(ReadOnlySpan<byte> block)
    {
        var verbs = new List<VerbEntry>();
        for (int i = VerbTable; i + 2 < block.Length && block[i] != 0; i += 3)
        {
            verbs.Add(new VerbEntry(block[i], BinaryPrimitives.ReadUInt16LittleEndian(block[(i + 1)..])));
        }

        return [.. verbs];
    }
}
