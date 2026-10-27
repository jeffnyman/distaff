using Distaff.Scumm.Graphics;

namespace Distaff.Scumm.Costumes;

/// <summary>
/// Draws costume figures in room colour numbers. Mirrors ScummVM's <c>ClassicCostumeRenderer</c>:
/// <c>setPalette</c>, <c>setFacing</c> and <c>drawLimb</c>, with the cel offsets counting from 72
/// pixels left of and 100 above the actor's feet for the EGA and from the feet for the VGA CD
/// (<c>BaseCostumeRenderer::drawCostume</c>), and
/// <c>paintCelByleRLE</c> by way of <c>paintCelByleRLECommon</c>, <c>skipCelLines</c> and
/// <c>byleRLEDecode</c>: the scaling, clipping and drawing they share. Ported by way of Maniacal.
/// </summary>
public static class CostumeRenderer
{
    /// <summary>Directions, as ScummVM numbers them for animations (<c>newDirToOldDir</c>).</summary>
    public const int Left = 0, Right = 1, Front = 2, Back = 3;

    /// <summary>A scale that draws a figure at its full size, which the EGA's always are.</summary>
    public const int FullScale = 255;

    /// <summary>The colour a figure's pixel is drawn in from the shadow table, when there is one (<c>pcolor == 13</c>).</summary>
    public const int ShadowColor = 13;

    /// <summary>
    /// Which columns and rows of a scaled figure are drawn: those whose entry is below the scale.
    /// The table spreads them evenly, each row and column taking the next entry. Mirrors
    /// <c>smallCostumeScaleTable</c>.
    /// </summary>
    private static readonly byte[] ScaleTable =
    [
        0xFF, 0xFD, 0x7D, 0xBD, 0x3D, 0xDD, 0x5D, 0x9D, 0x1D, 0xED, 0x6D, 0xAD, 0x2D, 0xCD, 0x4D, 0x8D,
        0x0D, 0xF5, 0x75, 0xB5, 0x35, 0xD5, 0x55, 0x95, 0x15, 0xE5, 0x65, 0xA5, 0x25, 0xC5, 0x45, 0x85,
        0x05, 0xF9, 0x79, 0xB9, 0x39, 0xD9, 0x59, 0x99, 0x19, 0xE9, 0x69, 0xA9, 0x29, 0xC9, 0x49, 0x89,
        0x09, 0xF1, 0x71, 0xB1, 0x31, 0xD1, 0x51, 0x91, 0x11, 0xE1, 0x61, 0xA1, 0x21, 0xC1, 0x41, 0x81,
        0x01, 0xFB, 0x7B, 0xBB, 0x3B, 0xDB, 0x5B, 0x9B, 0x1B, 0xEB, 0x6B, 0xAB, 0x2B, 0xCB, 0x4B, 0x8B,
        0x0B, 0xF3, 0x73, 0xB3, 0x33, 0xD3, 0x53, 0x93, 0x13, 0xE3, 0x63, 0xA3, 0x23, 0xC3, 0x43, 0x83,
        0x03, 0xF7, 0x77, 0xB7, 0x37, 0xD7, 0x57, 0x97, 0x17, 0xE7, 0x67, 0xA7, 0x27, 0xC7, 0x47, 0x87,
        0x07, 0xEF, 0x6F, 0xAF, 0x2F, 0xCF, 0x4F, 0x8F, 0x0F, 0xDF, 0x5F, 0x9F, 0x1F, 0xBF, 0x3F, 0x7F,
        0x00, 0x80, 0x40, 0xC0, 0x20, 0xA0, 0x60, 0xE0, 0x10, 0x90, 0x50, 0xD0, 0x30, 0xB0, 0x70, 0xF0,
        0x08, 0x88, 0x48, 0xC8, 0x28, 0xA8, 0x68, 0xE8, 0x18, 0x98, 0x58, 0xD8, 0x38, 0xB8, 0x78, 0xF8,
        0x04, 0x84, 0x44, 0xC4, 0x24, 0xA4, 0x64, 0xE4, 0x14, 0x94, 0x54, 0xD4, 0x34, 0xB4, 0x74, 0xF4,
        0x0C, 0x8C, 0x4C, 0xCC, 0x2C, 0xAC, 0x6C, 0xEC, 0x1C, 0x9C, 0x5C, 0xDC, 0x3C, 0xBC, 0x7C, 0xFC,
        0x02, 0x82, 0x42, 0xC2, 0x22, 0xA2, 0x62, 0xE2, 0x12, 0x92, 0x52, 0xD2, 0x32, 0xB2, 0x72, 0xF2,
        0x0A, 0x8A, 0x4A, 0xCA, 0x2A, 0xAA, 0x6A, 0xEA, 0x1A, 0x9A, 0x5A, 0xDA, 0x3A, 0xBA, 0x7A, 0xFA,
        0x06, 0x86, 0x46, 0xC6, 0x26, 0xA6, 0x66, 0xE6, 0x16, 0x96, 0x56, 0xD6, 0x36, 0xB6, 0x76, 0xF6,
        0x0E, 0x8E, 0x4E, 0xCE, 0x2E, 0xAE, 0x6E, 0xEE, 0x1E, 0x9E, 0x5E, 0xDE, 0x3E, 0xBE, 0x7E, 0xFE,
    ];

    /// <summary>Where the scale table is entered for a figure's first row and column (<c>scaletableSize</c>).</summary>
    private const int ScaleTableStart = 128;

    /// <summary>
    /// The colours a figure is drawn with. For the EGA: the actor's first 16, or all grey but black
    /// when the lights do not show actors' colours, the costume's colour byte taking colour 0's.
    /// For the VGA CD: the actor's colours, each 255 among them the costume's own, or all grey
    /// but black. Mirrors <c>ClassicCostumeRenderer::setPalette</c>.
    /// </summary>
    public static byte[] Palette(Costume costume, ReadOnlySpan<byte> actorPalette, bool actorColors = true)
    {
        if (costume.Version == 4)
        {
            var colors = new byte[costume.ColorCount];
            if (actorColors)
            {
                var own = costume.Palette.Span;
                for (int i = 0; i < colors.Length; i++)
                {
                    colors[i] = actorPalette[i] == 255 ? own[i] : actorPalette[i];
                }
            }
            else
            {
                Array.Fill(colors, (byte)8);
                colors[12] = 0;
            }

            return colors;
        }

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
    /// in limb order, every cel placed from where the one before left the running position, and
    /// scaled down when a scale is below <see cref="FullScale"/>. Facing left mirrors facing right
    /// unless the costume has its own left pictures. The room's mask hides the figure where its
    /// plane <paramref name="zPlane"/> is set, none for 0 (<c>_zbuf</c>). Colour 0 is transparent;
    /// colour <see cref="ShadowColor"/> takes the shadow table's colour for the pixel under it,
    /// when there is a table. Returns the rectangle the figure covers.
    /// </summary>
    public static Rect Draw(Costume costume, CostumeState state, int direction, byte[] palette, MaskedImage target, int x, int y,
        int zPlane, int scaleX = FullScale, int scaleY = FullScale, byte[]? shadow = null)
        => Paint(costume, state, direction, x, y, scaleX, scaleY, new Drawing(palette, target, zPlane, shadow));

    /// <summary>The rectangle the figure covers, or an empty one when no cel shows (the actor's <c>_left</c> to <c>_bottom</c>).</summary>
    public static Rect Bounds(Costume costume, CostumeState state, int direction, int x, int y, int scaleX = FullScale, int scaleY = FullScale)
        => Paint(costume, state, direction, x, y, scaleX, scaleY, null);

    /// <summary>Where to draw a figure and with what; none when only measuring it.</summary>
    private sealed record Drawing(byte[] Palette, MaskedImage Target, int ZPlane, byte[]? Shadow);

    private static Rect Paint(Costume costume, CostumeState state, int direction, int x, int y, int scaleX, int scaleY, Drawing? drawing)
    {
        bool toRight = direction != Left || costume.HasOwnLeftPictures;
        // the EGA's cels count from 72 pixels left of and 100 above the feet; the VGA CD's from the feet
        int moveX = costume.Version == 4 ? 0 : -72;
        int moveY = costume.Version == 4 ? 0 : -100;
        Rect bounds = default;
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
            var rect = PaintCel(costume, cel, celX, celY, x, y, toRight, scaleX, scaleY, drawing);
            bounds = bounds.IsEmpty ? rect : Rect.FromBounds(Math.Min(bounds.Left, rect.Left), Math.Min(bounds.Top, rect.Top),
                Math.Max(bounds.Right, rect.Right), Math.Max(bounds.Bottom, rect.Bottom));
        }

        return bounds;
    }

    /// <summary>
    /// Places one cel, scaled, and draws what of it falls in the picture. Mirrors
    /// <c>paintCelByleRLECommon</c>, with the picture as the bounds, and <c>byleRLEDecode</c>. A
    /// figure facing left is drawn from its right edge leftwards. Returns the cel's rectangle,
    /// before clipping.
    /// </summary>
    private static Rect PaintCel(Costume costume, CostumeCel cel, int xMoveCur, int yMoveCur, int actorX, int actorY, bool toRight, int scaleX, int scaleY, Drawing? drawing)
    {
        int width = cel.Width, height = cel.Height;
        int x = actorX, y = actorY;
        int boundsRight = drawing?.Target.Pixels.Width ?? int.MaxValue;
        int boundsBottom = drawing?.Target.Pixels.Height ?? int.MaxValue;
        int left, right, top, bottom, startX, startY;
        int linesToSkip = 0;
        bool scaled = scaleX != FullScale || scaleY != FullScale;
        if (scaled)
        {
            int xStep = -1;
            if (xMoveCur < 0)
            {
                xMoveCur = -xMoveCur;
                xStep = 1;
            }

            int j;
            if (toRight)
            {
                startX = j = (ScaleTableStart - xMoveCur) & 0xFF;
                for (int i = 0; i < xMoveCur; i++)
                {
                    if (ScaleTable[j++ & 0xFF] < scaleX)
                    {
                        x -= xStep;
                    }
                }

                left = right = x;
                j = startX;
                for (int i = 0; i < width; i++)
                {
                    if (right < 0)
                    {
                        linesToSkip++;
                        startX = j;
                    }

                    if (ScaleTable[j++ & 0xFF] < scaleX)
                    {
                        right++;
                    }
                }
            }
            else
            {
                startX = j = (ScaleTableStart + xMoveCur) & 0xFF;
                for (int i = 0; i < xMoveCur; i++)
                {
                    if (ScaleTable[j-- & 0xFF] < scaleX)
                    {
                        x += xStep;
                    }
                }

                left = right = x;
                j = startX;
                for (int i = 0; i < width; i++)
                {
                    if (left >= boundsRight)
                    {
                        startX = j;
                        linesToSkip++;
                    }

                    if (ScaleTable[j-- & 0xFF] < scaleX)
                    {
                        left--;
                    }
                }
            }

            if (linesToSkip > 0)
            {
                linesToSkip--;
            }

            int yStep = -1;
            if (yMoveCur < 0)
            {
                yMoveCur = -yMoveCur;
                yStep = 1;
            }

            startY = j = (ScaleTableStart - yMoveCur) & 0xFF;
            for (int i = 0; i < yMoveCur; i++)
            {
                if (ScaleTable[j++ & 0xFF] < scaleY)
                {
                    y -= yStep;
                }
            }

            top = bottom = y;
            j = startY;
            for (int i = 0; i < height; i++)
            {
                if (ScaleTable[j++ & 0xFF] < scaleY)
                {
                    bottom++;
                }
            }
        }
        else
        {
            x += toRight ? xMoveCur : -xMoveCur;
            y += yMoveCur;
            (left, right) = toRight ? (x, x + width) : (x - width, x);
            top = y;
            bottom = top + height;
            startX = startY = ScaleTableStart;
        }

        var rect = Rect.FromBounds(left, top, right, bottom);
        if (drawing is null || top >= boundsBottom || bottom <= 0 || left >= boundsRight || right <= 0)
        {
            return rect;
        }

        var data = costume.Data.Span;
        var run = new Run(cel.DataOffset, costume.ColorShift, height);
        int skipWidth = width;

        // the columns off the picture's edge where drawing starts are skipped
        if (toRight)
        {
            if (!scaled)
            {
                linesToSkip = 0 - x;
            }

            if (linesToSkip > 0)
            {
                skipWidth -= linesToSkip;
                run.SkipColumns(data, linesToSkip);
                x = 0;
            }
            else if ((linesToSkip = right - boundsRight) > 0)
            {
                skipWidth -= linesToSkip;
            }
        }
        else
        {
            if (!scaled)
            {
                linesToSkip = right - boundsRight;
            }

            if (linesToSkip > 0)
            {
                skipWidth -= linesToSkip;
                run.SkipColumns(data, linesToSkip);
                x = boundsRight - 1;
            }
            else if ((linesToSkip = -1 - left) > 0)
            {
                skipWidth -= linesToSkip;
            }
        }

        if (skipWidth <= 0 || height + Math.Max(top, 0) >= 256)
        {
            return rect;
        }

        run.Decode(data, x, y, startX, startY, toRight ? 1 : -1, skipWidth, scaleX, scaleY, costume.Version, drawing);
        return rect;
    }

    /// <summary>Where a cel's pixel runs are read from, and a run carried over from skipped columns (<c>repLen</c>, <c>repColor</c>).</summary>
    private struct Run(int at, int shift, int height)
    {
        private int _at = at;
        private byte _length;
        private int _color;

        private readonly int Mask => (1 << shift) - 1;

        /// <summary>Passes over whole columns, keeping what is left of the run they end in. Mirrors <c>skipCelLines</c>.</summary>
        public void SkipColumns(ReadOnlySpan<byte> data, int columns)
        {
            int pixels = columns * height;
            while (true)
            {
                _length = Next(data, ref _at);
                _color = _length >> shift;
                _length = (byte)(_length & Mask);
                if (_length == 0)
                {
                    _length = Next(data, ref _at);
                }

                if ((pixels -= _length) <= 0)
                {
                    _length = (byte)(1 - pixels);
                    return;
                }
            }
        }

        /// <summary>
        /// Runs down each column from the top: a byte is a colour (its high bits) and a run length
        /// (the rest, 0 meaning the next byte), carrying on into the next column. Rows and columns
        /// the scale leaves out are passed over. Mirrors <c>byleRLEDecode</c>; version 4 steps through
        /// the scale table forwards for columns whichever way the figure faces.
        /// </summary>
        public void Decode(ReadOnlySpan<byte> data, int x, int top, int scaleXIndex, int scaleYIndex, int xStep, int skipWidth, int scaleX, int scaleY, int version, Drawing drawing)
        {
            var pixels = drawing.Target.Pixels;
            var mask = drawing.Target.Mask;
            int planeBit = drawing.ZPlane > 0 ? 1 << (drawing.ZPlane - 1) : 0;
            int y = top;
            int rows = height;
            int rowIndex = scaleYIndex;
            byte length = _length;
            int color = _color;
            bool resume = length != 0;
            while (true)
            {
                if (resume)
                {
                    // a run carried over: ScummVM jumps to the end of its loop, which counts it down
                    resume = false;
                    if (--length == 0)
                    {
                        continue;
                    }
                }
                else
                {
                    length = Next(data, ref _at);
                    color = length >> shift;
                    length = (byte)(length & Mask);
                    if (length == 0)
                    {
                        length = Next(data, ref _at);
                    }
                }

                do
                {
                    if (scaleY == FullScale || ScaleTable[rowIndex++ & 0xFF] < scaleY)
                    {
                        bool hidden = y < 0 || y >= pixels.Height || x < 0 || x >= pixels.Width || (mask[x, y] & planeBit) != 0;
                        if (color != 0 && !hidden)
                        {
                            int drawn = drawing.Palette[color];
                            if (drawn == ShadowColor && drawing.Shadow is { } shadow)
                            {
                                drawn = shadow[pixels[x, y]];
                            }

                            pixels[x, y] = (byte)drawn;
                        }

                        y++;
                    }

                    if (--rows == 0)
                    {
                        if (--skipWidth == 0)
                        {
                            return;
                        }

                        rows = height;
                        y = top;
                        rowIndex = scaleYIndex;
                        if (scaleX == FullScale || ScaleTable[scaleXIndex] < scaleX)
                        {
                            x += xStep;
                            if (x < 0 || x >= pixels.Width)
                            {
                                return;
                            }
                        }

                        scaleXIndex = (scaleXIndex + (version == 4 ? 1 : xStep)) & 0xFF;
                    }
                }
                while (--length != 0);
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
