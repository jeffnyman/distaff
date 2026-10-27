using System.Buffers.Binary;

namespace Distaff.Scumm.Resources;

/// <summary>
/// A sound of the EGA release: two blocks one after the other, each starting with its own size
/// word. The first is the PC speaker and PCjr data (ScummVM's "WA"), the second the AdLib data
/// ("AD"). Mirrors the <c>GF_OLD_BUNDLE</c> branch of <c>readSoundResourceSmallHeader</c>, which
/// hands the speaker block, size word included, to <c>Player_V2</c>, and the AdLib block from
/// its fifth byte to <c>Player_AD</c>.
/// </summary>
/// <remarks>
/// The sounds of the Roland Upgrade, in <c>82.LFL</c> to <c>85.LFL</c>, are single tagless blocks
/// that only an MT-32 plays. ScummVM reads every sound as such a block when the sound card
/// variable says Roland (<c>isRolandLoom</c>), so the scripts must start these in place of the
/// others then. They are not read as two blocks.
/// </remarks>
public sealed class SoundResource
{
    private SoundResource(int number, ReadOnlyMemory<byte> speaker, ReadOnlyMemory<byte> adlib)
    {
        Number = number;
        Speaker = speaker;
        AdLib = adlib;
    }

    public int Number { get; }

    /// <summary>The PC speaker and PCjr block, from its size word.</summary>
    public ReadOnlyMemory<byte> Speaker { get; }

    /// <summary>The AdLib block, from its size word.</summary>
    public ReadOnlyMemory<byte> AdLib { get; }

    /// <summary>
    /// Reads the two blocks starting at <paramref name="offset"/> of a room file. Throws
    /// <see cref="InvalidDataException"/> when either does not fit.
    /// </summary>
    internal static SoundResource Parse(int number, ReadOnlyMemory<byte> file, int offset)
    {
        var speaker = Block(number, "speaker", file, offset);
        var adlib = Block(number, "AdLib", file, offset + speaker.Length);
        return new SoundResource(number, speaker, adlib);
    }

    private static ReadOnlyMemory<byte> Block(int number, string what, ReadOnlyMemory<byte> file, int offset)
    {
        if (offset + 2 > file.Length)
        {
            throw new InvalidDataException($"Sound {number}: the {what} block at {offset} is past the end of its file ({file.Length} bytes).");
        }

        int size = BinaryPrimitives.ReadUInt16LittleEndian(file.Span[offset..]);
        if (size < 4 || offset + size > file.Length)
        {
            throw new InvalidDataException($"Sound {number}: the {what} block at {offset} has a bad size {size}.");
        }

        return file.Slice(offset, size);
    }
}
