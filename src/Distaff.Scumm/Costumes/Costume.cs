using System.Buffers.Binary;

namespace Distaff.Scumm.Costumes;

/// <summary>What an animation does to one limb: play a run of commands, or hide the limb.</summary>
/// <param name="Limb">The limb, 0 being drawn first.</param>
/// <param name="Start">Index of the first command, or null when the animation hides the limb.</param>
/// <param name="Length">How many commands follow the first one.</param>
/// <param name="Loop">Whether the run starts again after its last command.</param>
public readonly record struct LimbAnimation(int Limb, int? Start, int Length, bool Loop);

/// <summary>A picture of one limb, and how it is placed. Offsets are in pixels.</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="RelX">Where the cel's left edge is, from the running position.</param>
/// <param name="RelY">Where the cel's top edge is, from the running position.</param>
/// <param name="MoveX">How far the running position moves right for the next limb.</param>
/// <param name="MoveY">How far the running position moves up for the next limb.</param>
/// <param name="DataOffset">Where the cel's pixel data starts in the costume.</param>
public readonly record struct CostumeCel(int Width, int Height, int RelX, int RelY, int MoveX, int MoveY, int DataOffset);

/// <summary>
/// A costume: a character's animations. Mirrors ScummVM's <c>ClassicCostumeLoader::loadCostume</c>
/// and <c>costumeDecodeData</c> for <c>GF_OLD_BUNDLE</c> and <c>GF_SMALL_HEADER</c>, and the cel
/// headers read in <c>ClassicCostumeRenderer::drawLimb</c>. Ported by way of Maniacal, whose
/// version 2 costumes have the EGA's layout.
/// </summary>
/// <remarks>
/// <para>From the size word: the number of animations less one at 4 and the format at 5
/// (<c>0x58</c> or <c>0x59</c>; bit 7: the costume has its own left-facing pictures and is never
/// mirrored), and one colour byte at 6. Then the offset of the command bytes, one frame-table
/// offset for each of the 16 limbs, and one offset per animation; offsets count from the size
/// word. The VGA CD's costumes start with a 32-bit size and the tag <c>CO</c>, have every field 2
/// bytes further on, and a palette of 16 colours (format <c>0x58</c>) or 32 (<c>0x59</c>) where the
/// EGA has its colour byte. The FM Towns' are laid out as the VGA CD's. Animation
/// <c>frame * 4 + direction</c> lists the limbs it changes: for each, the index of its first
/// command (a byte, or for the VGA CD a word) and how many follow.
/// Each command picks a cel from the limb's frame table; commands <see cref="Hide"/>,
/// <see cref="Stop"/> and <see cref="Start"/> are not cels.</para>
/// <para>A cel: width, height, the offsets of its corner and of the next limb, as six words, then
/// its pixels: columns top to bottom, a byte being a colour in its high 4 bits and a run length in
/// its low 4 (0: the length is the next byte), or with 32 colours in its high 5 and low 3.</para>
/// </remarks>
public sealed class Costume
{
    public const int LimbCount = 16;

    /// <summary>The limb shows nothing for this command. Mirrors the <c>0x7B</c> check in <c>drawLimb</c>.</summary>
    public const byte Hide = 0x7B;

    /// <summary>The limb stops animating. Mirrors the <c>0x79</c> check in <c>costumeDecodeData</c>.</summary>
    public const byte Stop = 0x79;

    /// <summary>The limb starts animating again. Mirrors the <c>0x7A</c> check in <c>costumeDecodeData</c>.</summary>
    public const byte Start = 0x7A;

    /// <summary>Counts a sound cue; passed over when stepping. Mirrors the <c>0x78</c> check in <c>increaseAnim</c>.</summary>
    public const byte Sound = 0x78;

    /// <summary>Counts an animation step; passed over when stepping. Mirrors the <c>0x7C</c> check in <c>increaseAnim</c>.</summary>
    public const byte Counter = 0x7C;

    private readonly int _commands;
    private readonly int _frameOffsets;
    private readonly int _animationOffsets;

    private Costume(int number, ReadOnlyMemory<byte> data, bool smallHeader, int version, bool old256 = false)
    {
        var span = data.Span;
        int shift = smallHeader ? 2 : 0;
        if (span.Length < shift + 8)
        {
            throw new InvalidDataException($"Costume {number} is too short ({span.Length} bytes).");
        }

        Number = number;
        Data = data;
        Version = version;
        SmallHeader = smallHeader;
        Old256 = old256;
        AnimationCount = span[shift + 4] + 1;
        Format = span[shift + 5] & 0x7F;
        HasOwnLeftPictures = (span[shift + 5] & 0x80) != 0;
        if (Format is not (0x58 or 0x59))
        {
            throw new InvalidDataException($"Costume {number} has format 0x{Format:X2}, which is not a classic costume.");
        }

        // the EGA's palette is one colour byte (_numColors 1 for GF_OLD_BUNDLE)
        ColorCount = !smallHeader ? 1 : Format == 0x59 ? 32 : 16;
        int palette = shift + 6;
        if (palette + ColorCount + 34 + 2 * AnimationCount > span.Length)
        {
            throw new InvalidDataException($"Costume {number}: its tables run past the end of the costume.");
        }

        Palette = data.Slice(palette, ColorCount);
        ColorIndex = span[palette];
        int commandOffset = palette + ColorCount;
        _commands = Word(span, commandOffset);
        _frameOffsets = commandOffset + 2;
        _animationOffsets = commandOffset + 34;
        if (_commands > span.Length)
        {
            throw new InvalidDataException($"Costume {number}: its commands start past the end of the costume.");
        }

        // the command bytes run up to the first limb frame table
        int firstFrameTable = span.Length;
        for (int limb = 0; limb < LimbCount; limb++)
        {
            int table = Word(span, _frameOffsets + 2 * limb);
            if (table >= _commands && table < firstFrameTable)
            {
                firstFrameTable = table;
            }
        }

        CommandCount = firstFrameTable - _commands;
    }

    public int Number { get; }

    /// <summary>The SCUMM version of the release the costume is from: 3 for the EGA and FM Towns, 4 for the VGA CD.</summary>
    public int Version { get; }

    /// <summary>Whether the costume starts with a 6-byte header and has a palette of its own (<c>GF_SMALL_HEADER</c> without <c>GF_OLD_BUNDLE</c>): the VGA CD's and the FM Towns'.</summary>
    public bool SmallHeader { get; }

    /// <summary>Whether the costume is the FM Towns', whose commands 0x79 to 0x7B draw nothing (<c>GF_OLD256</c> in <c>drawLimb</c>).</summary>
    public bool Old256 { get; }

    /// <summary>The costume resource, from its size word.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    public int Format { get; }

    /// <summary>Whether the costume has its own left-facing pictures; otherwise facing left mirrors facing right (<c>_mirror</c>).</summary>
    public bool HasOwnLeftPictures { get; }

    public int AnimationCount { get; }

    /// <summary>How many command bytes there are, up to the first limb frame table.</summary>
    public int CommandCount { get; }

    /// <summary>The EGA's colour number drawn as the actor's colour 0 instead of itself. Mirrors <c>_palette[_loaded._palette[0]] = _palette[0]</c> in <c>setPalette</c>.</summary>
    public int ColorIndex { get; }

    /// <summary>How many colours the costume has (<c>_numColors</c>): 1 for the EGA's colour byte, 16 or 32 for the VGA CD.</summary>
    public int ColorCount { get; }

    /// <summary>The costume's colours, as room colour numbers (<c>_loaded._palette</c>); the EGA's is its colour byte.</summary>
    public ReadOnlyMemory<byte> Palette { get; }

    /// <summary>How far a pixel byte's colour is shifted up; the rest is the run length. Mirrors <c>shr</c> in <c>paintCelByleRLECommon</c>.</summary>
    public int ColorShift => ColorCount == 32 ? 3 : 4;

    /// <summary>Parses an EGA costume, from its size word.</summary>
    public static Costume Parse(int number, ReadOnlyMemory<byte> data) => new(number, data, smallHeader: false, version: 3);

    /// <summary>Parses a VGA CD costume, from its 6-byte header.</summary>
    public static Costume ParseV4(int number, ReadOnlyMemory<byte> data) => new(number, data, smallHeader: true, version: 4);

    /// <summary>Parses an FM Towns costume, from its 6-byte header.</summary>
    public static Costume ParseTowns(int number, ReadOnlyMemory<byte> data) => new(number, data, smallHeader: true, version: 3, old256: true);

    /// <summary>The command byte at an index; the low 7 bits pick a cel.</summary>
    public byte Command(int index)
    {
        var span = Data.Span;
        if (_commands + index >= span.Length)
        {
            throw new InvalidDataException($"Costume {Number}: command {index} is past the end of the costume.");
        }

        return span[_commands + index];
    }

    /// <summary>The limbs an animation changes, or an empty list when the costume has no such animation.</summary>
    public IReadOnlyList<LimbAnimation> Animation(int index)
    {
        var span = Data.Span;
        if (index < 0 || index >= AnimationCount)
        {
            return [];
        }

        int position = Word(span, _animationOffsets + 2 * index);
        if (position == 0)
        {
            return [];
        }

        int mask = Word(span, position);
        position += 2;
        var limbs = new List<LimbAnimation>();
        for (int limb = 0; (mask & 0xFFFF) != 0; limb++, mask <<= 1)
        {
            if ((mask & 0x8000) == 0)
            {
                continue;
            }

            // version 4 gives the first command as a word, with 0xFFFF for none
            int start = Version == 4 ? Word(span, position) : span[position];
            position += Version == 4 ? 2 : 1;
            if (start == (Version == 4 ? 0xFFFF : 0xFF))
            {
                limbs.Add(new LimbAnimation(limb, null, 0, false));
                continue;
            }

            byte extra = span[position++];
            limbs.Add(new LimbAnimation(limb, start, extra & 0x7F, (extra & 0x80) == 0));
        }

        return limbs;
    }

    /// <summary>The cel a command picks for a limb.</summary>
    public CostumeCel Cel(int limb, int code)
    {
        var span = Data.Span;
        int frames = Word(span, _frameOffsets + 2 * limb);
        int cel = Word(span, frames + 2 * code);
        if (cel + 12 > span.Length)
        {
            throw new InvalidDataException($"Costume {Number}: the cel of limb {limb}, command {code}, is past the end of the costume.");
        }

        return new CostumeCel(
            Word(span, cel),
            Word(span, cel + 2),
            (short)Word(span, cel + 4),
            (short)Word(span, cel + 6),
            (short)Word(span, cel + 8),
            (short)Word(span, cel + 10),
            cel + 12);
    }

    private static int Word(ReadOnlySpan<byte> span, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
}
