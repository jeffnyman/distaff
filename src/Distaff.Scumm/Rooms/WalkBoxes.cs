using System.Buffers.Binary;

namespace Distaff.Scumm.Rooms;

/// <summary>
/// A walkbox: a quadrilateral given by its four corners, in pixels. Mirrors the <c>old</c> layout
/// of ScummVM's <c>Box</c> up to its flags, which is all of its <c>SIZEOF_BOX_V3</c> bytes: version 3
/// boxes have no scale.
/// </summary>
public readonly record struct WalkBox(
    int UpperLeftX, int UpperLeftY,
    int UpperRightX, int UpperRightY,
    int LowerRightX, int LowerRightY,
    int LowerLeftX, int LowerLeftY,
    byte Mask, byte Flags);

/// <summary>
/// A room's walkboxes and the box matrix that tells which box leads to which. Mirrors the version 3
/// branch of <c>ScummEngine_v3old::resetRoomSubBlocks</c>: a count, the boxes, then the matrix,
/// which runs up to the room's picture.
/// </summary>
/// <remarks>The matrix is kept as it is stored; walking reads it.</remarks>
public sealed class WalkBoxes
{
    /// <summary>Mirrors ScummVM's <c>SIZEOF_BOX_V3</c>.</summary>
    public const int BoxSize = 18;

    public static readonly WalkBoxes Empty = new([], ReadOnlyMemory<byte>.Empty);

    private WalkBoxes(WalkBox[] boxes, ReadOnlyMemory<byte> matrix)
    {
        Boxes = boxes;
        Matrix = matrix;
    }

    public IReadOnlyList<WalkBox> Boxes { get; }

    public ReadOnlyMemory<byte> Matrix { get; }

    public int Count => Boxes.Count;

    /// <summary>Reads the boxes at <paramref name="offset"/> of a room, and the matrix from after them up to <paramref name="end"/>.</summary>
    internal static WalkBoxes Parse(ReadOnlyMemory<byte> room, int offset, int end)
    {
        var span = room.Span;
        if (offset >= span.Length)
        {
            throw new InvalidDataException($"The walkboxes at {offset} are past the end of the room ({span.Length} bytes).");
        }

        int count = span[offset];
        int matrix = offset + 1 + count * BoxSize;
        if (matrix > end || end > span.Length)
        {
            throw new InvalidDataException($"The {count} walkboxes at {offset} do not fit before {end}.");
        }

        var boxes = new WalkBox[count];
        for (int i = 0; i < count; i++)
        {
            var b = span.Slice(offset + 1 + i * BoxSize, BoxSize);
            boxes[i] = new WalkBox(
                Int16(b, 0), Int16(b, 2),
                Int16(b, 4), Int16(b, 6),
                Int16(b, 8), Int16(b, 10),
                Int16(b, 12), Int16(b, 14),
                b[16], b[17]);
        }

        return new WalkBoxes(boxes, room[matrix..end]);
    }

    private static int Int16(ReadOnlySpan<byte> span, int offset) => BinaryPrimitives.ReadInt16LittleEndian(span[offset..]);
}
