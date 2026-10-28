using Distaff.Scumm.Graphics;

namespace Distaff.Scumm.Game;

/// <summary>
/// The mouse cursor: four 16 by 16 pictures, one of them chosen, drawn in a colour that cycles.
/// Mirrors ScummVM's <c>o5_cursorCommand</c> for version 3 and, from <c>cursor.cpp</c>,
/// <c>resetCursors</c>, <c>redefineBuiltinCursorFromChar</c> (Loom builds its cursor from a
/// character of its font), <c>redefineBuiltinCursorHotspot</c>, <c>setBuiltinCursor</c> and
/// <c>ScummEngine_v5::animateCursor</c>.
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>The colours the cursor cycles through (<c>default_cursor_colors</c>).</summary>
    private static readonly byte[] CursorColors = [15, 15, 7, 8];

    // cross-hair, hourglass, arrow and hand, each row a word with the leftmost pixel in the top bit
    private readonly ushort[][] _cursorImages =
    [
        [0x0080, 0x0080, 0x0080, 0x0080, 0x0080, 0x0080, 0x0000, 0x7e3f, 0x0000, 0x0080, 0x0080, 0x0080, 0x0080, 0x0080, 0x0080, 0x0000],
        [0x0000, 0x7ffe, 0x6006, 0x300c, 0x1818, 0x0c30, 0x0660, 0x03c0, 0x0660, 0x0c30, 0x1998, 0x33cc, 0x67e6, 0x7ffe, 0x0000, 0x0000],
        [0x0000, 0x4000, 0x6000, 0x7000, 0x7800, 0x7c00, 0x7e00, 0x7f00, 0x7f80, 0x78c0, 0x7c00, 0x4600, 0x0600, 0x0300, 0x0300, 0x0180],
        [0x1e00, 0x1200, 0x1200, 0x1200, 0x1200, 0x13ff, 0x1249, 0x1249, 0xf249, 0x9001, 0x9001, 0x9001, 0x8001, 0x8001, 0x8001, 0xffff],
    ];

    private readonly (int X, int Y)[] _cursorHotspots = [(8, 7), (8, 7), (1, 1), (5, 0)];
    private int _currentCursor;
    private int _cursorAnimateIndex;
    private byte _cursorColor = CursorColors[0];

    /// <summary>The cursor's hotspot, which the mouse position points at.</summary>
    public (int X, int Y) CursorHotspot => _cursorHotspots[_currentCursor];

    /// <summary>The colour the cursor is drawn in now.</summary>
    public byte CursorColor => _cursorColor;

    /// <summary>Mirrors <c>o5_cursorCommand</c> for version 3: the cursor and the player's control, counted on and off, and Loom's cursor picture.</summary>
    private void CursorCommand(int op, int[] a)
    {
        switch (op)
        {
            case 1:
                CursorState = 1;
                break;
            case 2:
                CursorState = 0;
                break;
            case 3:
                UserPut = 1;
                break;
            case 4:
                UserPut = 0;
                break;
            case 5:
                CursorState++;
                break;
            case 6:
                CursorState--;
                break;
            case 7:
                UserPut++;
                break;
            case 8:
                UserPut--;
                break;
            case 10:
                RedefineCursorFromChar(a[0], a[1]);
                break;
            case 11:
                _cursorHotspots[CheckCursor(a[0])] = (a[1], a[2]);
                break;
            case 12:
                _currentCursor = CheckCursor(a[0]);
                break;
            case 13:
                // initCharset: the EGA has one font, and the slots' default charset is already 0
                if (_loader.Version == 4)
                {
                    InitCharset(a[0]);
                }

                break;
            case 14:
                // version 4: the current charset's colours (_charsetColorMap, _charsetData); version 3 passes two values over
                if (_loader.Version == 4)
                {
                    for (int i = 0; i < CharsetColorMap.Length && i < a.Length; i++)
                    {
                        CharsetColorMap[i] = _charsetData[_charsetId][i] = (byte)a[i];
                    }
                }

                break;
        }
    }

    /// <summary>
    /// Makes a cursor of a character of the font. Mirrors <c>redefineBuiltinCursorFromChar</c>:
    /// the character is drawn as text is drawn now, shadow included, and every pixel it sets is
    /// the cursor's. The VGA CD draws it in charset 1, whichever the text is in.
    /// </summary>
    private void RedefineCursorFromChar(int index, int chr)
    {
        var rows = _cursorImages[CheckCursor(index)];
        Array.Clear(rows);
        var font = _loader.Version == 4 && _charsetId != 1
            ? _loader.LoadCharset(1) is { } data ? Charset.ParseClassic(data) : null
            : _font;
        if (font is null)
        {
            return;
        }

        const byte Unset = 123;
        int width = font.CharWidth(chr);
        var surface = new Surface8(Math.Max(1, width), font.Height);
        surface.Fill(Unset);
        if (font.IsClassic)
        {
            // CharsetRendererClassic::drawChar, through the colour map as it stands
            if (font.ClassicGlyph(chr) is { } glyph)
            {
                font.DrawBitsN(surface, 0, 0, glyph, CharsetColorMap);
            }
        }
        else
        {
            Charset.DrawBits1(surface, 0, 0, font.Glyph(chr), width, 8, _textColor, _shadow);
        }
        for (int y = 0; y < surface.Height && y < rows.Length; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (surface[x, y] != Unset)
                {
                    rows[y] |= (ushort)(1 << (15 - x));
                }
            }
        }
    }

    /// <summary>
    /// Moves the cursor's colour on, every other step, through white, white, grey and dark grey.
    /// Mirrors <c>ScummEngine_v5::animateCursor</c> and the colour <c>setBuiltinCursor</c> picks.
    /// </summary>
    private void AnimateCursor()
    {
        if ((_cursorAnimateIndex & 1) == 0)
        {
            _cursorColor = CursorColors[(_cursorAnimateIndex >> 1) & 3];
        }

        _cursorAnimateIndex++;
    }

    /// <summary>Draws the cursor over a frame at the mouse, when it shows (<c>_cursor.state</c> above 0).</summary>
    public void DrawCursor(Surface8 frame)
    {
        foreach (var (x, y) in CursorPixels(frame.Width, frame.Height))
        {
            frame[x, y] = _cursorColor;
        }
    }

    /// <summary>The points of a screen of the given size the cursor covers, none when it does not show.</summary>
    private IEnumerable<(int X, int Y)> CursorPixels(int width, int height)
    {
        if (CursorState <= 0)
        {
            yield break;
        }

        var rows = _cursorImages[_currentCursor];
        var (hx, hy) = CursorHotspot;
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                int px = MouseX - hx + x;
                int py = MouseY - hy + y;
                if ((rows[y] & (1 << (15 - x))) != 0 && px >= 0 && px < width && py >= 0 && py < height)
                {
                    yield return (px, py);
                }
            }
        }
    }

    private static int CheckCursor(int index)
        => index is >= 0 and < 4 ? index : throw new InvalidOperationException($"Cursor {index} is out of range (0 to 3).");
}
