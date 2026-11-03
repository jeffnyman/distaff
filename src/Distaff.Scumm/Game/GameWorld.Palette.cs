using Distaff.Scumm.Rooms;

namespace Distaff.Scumm.Game;

/// <summary>
/// The VGA CD's colour cycling: up to 16 runs of colours that turn round one step a frame, through
/// the shadow palette, for water, fire and glows. Mirrors ScummVM's <c>initCycl</c> and
/// <c>cyclePalette</c> for <c>GF_SMALL_HEADER</c>, where a cycle's delay only says whether it runs.
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>How many colour cycles a room has (<c>_colorCycle</c>).</summary>
    internal const int ColorCycleCount = 16;

    /// <summary>A cycle's delay meaning "do not run", as Loom's rooms mark their unused cycles.</summary>
    private const int UnusedCycle = 0x0AAA;

    private readonly ColorCycle[] _cycles = new ColorCycle[ColorCycleCount];

    /// <summary>A colour cycle: its range, and where it has turned to, 0 when it does not run (<c>ColorCycle</c>).</summary>
    private struct ColorCycle
    {
        public int Delay;
        public int Counter;
        public byte Start;
        public byte End;
    }

    /// <summary>
    /// Sets the room's cycles from its <c>CC</c> block: for each, a big-endian delay, then the first
    /// and last colour. A cycle with a delay other than 0 and 0x0AAA runs, from its first colour
    /// (<c>initCycl</c>).
    /// </summary>
    internal void InitCycles(Room room)
    {
        Array.Clear(_cycles);
        if (!room.BlockOffsets.TryGetValue("CC", out int at))
        {
            return;
        }

        var span = room.Data.Span;
        for (int i = 0; i < ColorCycleCount; i++, at += 4)
        {
            ref var cycle = ref _cycles[i];
            cycle.Delay = (span[at] << 8) | span[at + 1];
            cycle.Start = span[at + 2];
            cycle.End = span[at + 3];
            cycle.Counter = cycle.Delay != 0 && cycle.Delay != UnusedCycle ? cycle.Start : 0;
        }
    }

    /// <summary>
    /// The cycles as a save holds them, five numbers each: the delay, the counter, the flags (none
    /// in version 4), the first and the last colour (<c>syncWithSerializer(ColorCycle)</c>).
    /// </summary>
    internal void SaveCycles(int[] saved)
    {
        for (int i = 0; i < ColorCycleCount; i++)
        {
            var cycle = _cycles[i];
            (saved[i * 5], saved[i * 5 + 1], saved[i * 5 + 2], saved[i * 5 + 3], saved[i * 5 + 4]) = (cycle.Delay, cycle.Counter, 0, cycle.Start, cycle.End);
        }
    }

    /// <summary>The cycles from a save, as <see cref="SaveCycles"/> writes them.</summary>
    internal void LoadCycles(int[] saved)
    {
        for (int i = 0; i < ColorCycleCount; i++)
        {
            _cycles[i] = new ColorCycle { Delay = saved[i * 5], Counter = saved[i * 5 + 1], Start = (byte)saved[i * 5 + 3], End = (byte)saved[i * 5 + 4] };
        }
    }

    /// <summary>
    /// Turns each running cycle one step: its counter moves on, back to its first colour after its
    /// last, and the shadow palette maps its colours from the counter down, wrapping (<c>cyclePalette</c>).
    /// </summary>
    internal void CyclePalette()
    {
        for (int i = 0; i < ColorCycleCount; i++)
        {
            ref var cycle = ref _cycles[i];
            if (cycle.Counter == 0)
            {
                continue;
            }

            if (++cycle.Counter > cycle.End)
            {
                cycle.Counter = cycle.Start;
            }

            if (cycle.Start > cycle.End)
            {
                continue;
            }

            byte value = (byte)cycle.Counter;
            for (int j = cycle.Start; j <= cycle.End; j++)
            {
                ShadowPalette[j] = value--;
                if (value < cycle.Start)
                {
                    value = cycle.End;
                }
            }
        }
    }
}
