namespace Distaff.Scumm.Rooms;

/// <summary>
/// A scale slot: a scale at each of two points, from which an actor's scale follows its position
/// in between. Mirrors ScummVM's <c>ScaleSlot</c>, <c>setScaleSlot</c> and <c>getScaleFromSlot</c>.
/// Version 4 rooms set them from their <c>SA</c> block, with x always 0; scripts may set them too.
/// </summary>
internal readonly record struct ScaleSlot(int X1, int Y1, int Scale1, int X2, int Y2, int Scale2)
{
    /// <summary>How many slots there are (<c>_scaleSlots</c>), numbered from 1.</summary>
    public const int Count = 20;

    /// <summary>The slots a room's <c>SA</c> block sets: the scale table resources 1 to 4 (<c>rtScaleTable</c>).</summary>
    public const int RoomSlots = 4;

    /// <summary>
    /// The scale at a point: along y, and along x as well when the slot's points differ in x, the
    /// two averaged; between 1 and 255. Mirrors <c>getScaleFromSlot</c>.
    /// </summary>
    public int ScaleAt(int x, int y)
    {
        if (Y1 == Y2 && X1 == X2)
        {
            throw new InvalidOperationException("The scale slot has both its points at the same place.");
        }

        int scaleY = 0;
        if (Y1 != Y2)
        {
            y = Math.Max(y, 0);
            scaleY = (Scale2 - Scale1) * (y - Y1) / (Y2 - Y1) + Scale1;
        }

        int scale;
        if (X1 == X2)
        {
            scale = scaleY;
        }
        else
        {
            int scaleX = (Scale2 - Scale1) * (x - X1) / (X2 - X1) + Scale1;
            scale = Y1 == Y2 ? scaleX : (scaleX + scaleY) / 2;
        }

        return Math.Clamp(scale, 1, 255);
    }

    /// <summary>
    /// The slots a version 4 room's <c>SA</c> block sets, by number from 1: each entry is the first
    /// scale and y, then the second, as words; an entry of all zeroes sets nothing. Mirrors the scale
    /// part of <c>resetRoomSubBlocks</c>.
    /// </summary>
    public static IReadOnlyDictionary<int, ScaleSlot> ReadRoomSlots(ReadOnlySpan<byte> block)
    {
        var slots = new Dictionary<int, ScaleSlot>();
        for (int i = 1; i <= RoomSlots && i * 8 <= block.Length; i++)
        {
            var entry = block.Slice((i - 1) * 8, 8);
            int s1 = Word(entry, 0), y1 = Word(entry, 2), s2 = Word(entry, 4), y2 = Word(entry, 6);
            if (s1 != 0 || y1 != 0 || s2 != 0 || y2 != 0)
            {
                slots[i] = new ScaleSlot(0, y1, s1, 0, y2, s2);
            }
        }

        return slots;
    }

    private static int Word(ReadOnlySpan<byte> span, int offset) => System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
}
