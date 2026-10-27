using Distaff.Scumm.Graphics;

namespace Distaff.Scumm.Costumes;

/// <summary>
/// Draws costume figures in EGA colour numbers. Mirrors ScummVM's <c>ClassicCostumeRenderer</c>
/// for <c>GF_OLD_BUNDLE</c>: <c>setPalette</c>, <c>setFacing</c>, <c>drawLimb</c> and
/// <c>byleRLEDecode</c>, with the cel offsets counting from 72 pixels left of and 100 above the
/// actor's feet (<c>BaseCostumeRenderer::drawCostume</c>). Ported by way of Maniacal.
/// </summary>
public static class CostumeRenderer
{
    /// <summary>Directions, as ScummVM numbers them for animations (<c>newDirToOldDir</c>).</summary>
    public const int Left = 0, Right = 1, Front = 2, Back = 3;

    /// <summary>
    /// The colours a figure is drawn with: the actor's first 16, or all grey but black when the
    /// lights do not show actors' colours; the costume's colour byte takes colour 0's. Mirrors
    /// <c>ClassicCostumeRenderer::setPalette</c> for <c>GF_OLD_BUNDLE</c> in version 3.
    /// </summary>
    public static byte[] Palette(Costume costume, ReadOnlySpan<byte> actorPalette, bool actorColors = true)
    {
        var palette = new byte[16];
        if (actorColors)
        {
            actorPalette[..16].CopyTo(palette);
        }
        else
        {
            Array.Fill(palette, (byte)8);
            palette[12] = 0;
        }

        palette[costume.ColorIndex & 0x0F] = palette[0];
        return palette;
    }

    /// <summary>
    /// Draws the figure a costume state shows into a picture of the room, the actor's feet at
    /// (<paramref name="x"/>, <paramref name="y"/>): each limb with a command that is not stopped,
    /// in limb order, every cel placed from where the one before left the running position. Facing
    /// left mirrors facing right unless the costume has its own left pictures. When
    /// <paramref name="masked"/>, the room's mask hides the figure where it is set
    /// (<c>_zbuf</c>). Colour 0 is transparent.
    /// </summary>
    public static void Draw(Costume costume, CostumeState state, int direction, byte[] palette, MaskedImage target, int x, int y, bool masked)
    {
        bool toRight = direction != Left || costume.HasOwnLeftPictures;
        int moveX = -72;
        int moveY = -100;
        for (int limb = 0; limb < Costume.LimbCount; limb++)
        {
            int position = state.Position(limb);
            if (position == CostumeState.None || state.IsStopped(limb))
            {
                continue;
            }

            int code = costume.Command(position & ~CostumeState.NoLoop) & 0x7F;
            if (code == Costume.Hide)
            {
                continue;
            }

            var cel = costume.Cel(limb, code);
            int celX = moveX + cel.RelX;
            int celY = moveY + cel.RelY;
            moveX += cel.MoveX;
            moveY -= cel.MoveY;

            int left = toRight ? x + celX : x - celX - cel.Width;
            DrawCel(costume.Data.Span, cel, palette, target, left, y + celY, !toRight, masked);
        }
    }

    /// <summary>
    /// The pixels of a cel: runs fill columns top to bottom and carry on into the next column;
    /// colour 0 is transparent.
    /// </summary>
    private static void DrawCel(ReadOnlySpan<byte> data, CostumeCel cel, byte[] palette, MaskedImage target, int left, int top, bool mirror, bool masked)
    {
        var pixels = target.Pixels;
        int position = cel.DataOffset;
        int x = 0;
        int y = 0;
        while (x < cel.Width)
        {
            byte value = Next(data, ref position);
            int color = value >> 4;
            int length = value & 0x0F;
            if (length == 0)
            {
                length = Next(data, ref position);
            }

            for (; length > 0 && x < cel.Width; length--)
            {
                int px = left + (mirror ? cel.Width - 1 - x : x);
                int py = top + y;
                if (color != 0 && px >= 0 && px < pixels.Width && py >= 0 && py < pixels.Height
                    && !(masked && target.Mask[px, py] != 0))
                {
                    pixels[px, py] = palette[color];
                }

                if (++y == cel.Height)
                {
                    y = 0;
                    x++;
                }
            }
        }
    }

    private static byte Next(ReadOnlySpan<byte> data, ref int position)
    {
        if (position >= data.Length)
        {
            throw new InvalidDataException($"A costume cel runs past the end of the costume, at offset {position}.");
        }

        return data[position++];
    }
}
