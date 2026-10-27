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

    /// <summary>The box number that means no box (<c>kOldInvalidBox</c>).</summary>
    public const int InvalidBox = 255;

    /// <summary>
    /// Whether a point lies in a box: on the inner side of each of its four edges, taken in turn
    /// around it. Mirrors <c>checkXYInBoxBounds</c> for version 3, which has no special case for
    /// boxes that are lines.
    /// </summary>
    public bool Contains(int box, int x, int y)
    {
        if (box < 0 || box == InvalidBox || box > Boxes.Count || Boxes.Count == 0)
        {
            return false;
        }

        var b = Box(box);
        if (x < b.UpperLeftX && x < b.UpperRightX && x < b.LowerRightX && x < b.LowerLeftX)
        {
            return false;
        }

        if (x > b.UpperLeftX && x > b.UpperRightX && x > b.LowerRightX && x > b.LowerLeftX)
        {
            return false;
        }

        if (y < b.UpperLeftY && y < b.UpperRightY && y < b.LowerRightY && y < b.LowerLeftY)
        {
            return false;
        }

        if (y > b.UpperLeftY && y > b.UpperRightY && y > b.LowerRightY && y > b.LowerLeftY)
        {
            return false;
        }

        return CompareSlope(b.UpperLeftX, b.UpperLeftY, b.UpperRightX, b.UpperRightY, x, y)
            && CompareSlope(b.UpperRightX, b.UpperRightY, b.LowerRightX, b.LowerRightY, x, y)
            && CompareSlope(b.LowerRightX, b.LowerRightY, b.LowerLeftX, b.LowerLeftY, x, y)
            && CompareSlope(b.LowerLeftX, b.LowerLeftY, b.UpperLeftX, b.UpperLeftY, x, y);
    }

    /// <summary>
    /// A box by number; the number one past the last means the last, as <c>getBoxBaseAddr</c> takes
    /// it for version 4 and before.
    /// </summary>
    public WalkBox Box(int box) => Boxes[box == Boxes.Count ? box - 1 : box];

    /// <summary>
    /// The box to go to next on the way from one box to another, or -1 for no way. The matrix
    /// has a row per box: triplets of a lowest and a highest box and the box to go through for
    /// them, ended by 0xFF. Mirrors <c>getNextBox</c> for version 3.
    /// </summary>
    public int NextBox(int from, int to)
    {
        if (from == to)
        {
            return to;
        }

        if (to == InvalidBox)
        {
            return -1;
        }

        if (from == InvalidBox)
        {
            return to;
        }

        var m = Matrix.Span;
        int at = 0;
        for (int i = 0; i < from && at < m.Length; i++)
        {
            while (at < m.Length && m[at] != 0xFF)
            {
                at += 3;
            }

            at++;
        }

        int dest = -1;
        while (at + 2 < m.Length && m[at] != 0xFF)
        {
            if (m[at] <= to && to <= m[at + 1])
            {
                dest = (sbyte)m[at + 2];
            }

            at += 3;
        }

        return dest;
    }

    /// <summary>Mirrors <c>compareSlope</c>: whether the point is on the inner side of the edge from 1 to 2.</summary>
    private static bool CompareSlope(int x1, int y1, int x2, int y2, int x3, int y3)
        => (y2 - y1) * (x3 - x1) <= (y3 - y1) * (x2 - x1);

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
