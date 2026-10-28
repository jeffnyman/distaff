using Distaff.Scumm.Graphics;
using Distaff.Scumm.Input;

namespace Distaff.Scumm.Game;

/// <summary>
/// Asks for the FM Towns release's proficiency level before it boots, which the release itself
/// never asks. Mirrors ScummVM's <c>LoomTownsDifficultyDialog</c>, which ScummVM shows when it has
/// neither a save to load nor a boot parameter: its two lines and its three buttons, Standard (1),
/// Practice (0) and Expert (2), here in the game's own font. A button is chosen with the mouse, by
/// its initial, or with the arrow keys and Return; Escape closes the dialog as ScummVM's does,
/// leaving the level at 0, Practice.
/// </summary>
public sealed class DifficultyChooser
{
    private const int ButtonWidth = 96, ButtonHeight = 16, ButtonGap = 6;
    private const byte Background = 0, Text = 1, Border = 2, Lit = 3, Outline = 4;

    private static readonly Rgb[] Palette =
    [
        new(0x00, 0x00, 0x00),
        new(0xFF, 0xFF, 0xFF),
        new(0x88, 0x88, 0x88),
        new(0x33, 0x33, 0x99),
        new(0x00, 0x00, 0x00),
    ];

    /// <summary>The buttons in ScummVM's order, with the level each picks and the key that picks it.</summary>
    private static readonly (string Label, int Level, Key Key)[] Buttons =
    [
        ("Standard", 1, Key.S),
        ("Practice", 0, Key.P),
        ("Expert", 2, Key.E),
    ];

    private static readonly string[] Lines = ["Select a Proficiency Level.", "Refer to your Loom(TM) manual for help."];

    // the arrow drawn for the mouse: '#' white, 'o' its outline
    private static readonly string[] Arrow =
    [
        "o",
        "oo",
        "o#o",
        "o##o",
        "o###o",
        "o####o",
        "o#####o",
        "o###ooo",
        "o#o#o",
        "oo o#o",
        "    o#o",
        "     o",
    ];

    private readonly Charset? _font;
    private readonly int _width;
    private readonly int _height;
    private int _mouseX = -1, _mouseY = -1;

    /// <param name="font">The game's charset, or null to draw no text.</param>
    /// <param name="width">The screen's width.</param>
    /// <param name="height">The screen's height.</param>
    public DifficultyChooser(Charset? font, int width, int height)
    {
        _font = font;
        _width = width;
        _height = height;
    }

    /// <summary>The lit button, 0 to 2 in ScummVM's order: Practice at first, then the one the arrow keys or the mouse moved to.</summary>
    public int Selected { get; private set; } = 1;

    /// <summary>The level chosen (0 Practice, 1 Standard, 2 Expert), or null while the dialog is up.</summary>
    public int? Chosen { get; private set; }

    /// <summary>Where a button is on the screen.</summary>
    public Rect ButtonRect(int index)
    {
        int top = _height / 2 - 16;
        return new Rect((_width - ButtonWidth) / 2, top + index * (ButtonHeight + ButtonGap), ButtonWidth, ButtonHeight);
    }

    public void MoveMouse(int x, int y)
    {
        (_mouseX, _mouseY) = (x, y);
        if (ButtonAt(x, y) is { } index)
        {
            Selected = index;
        }
    }

    public void Click(int x, int y)
    {
        MoveMouse(x, y);
        if (ButtonAt(x, y) is { } index)
        {
            Chosen = Buttons[index].Level;
        }
    }

    public void KeyDown(Key key)
    {
        switch (key)
        {
            case Key.Escape:
                Chosen = 0;
                break;
            case Key.Up:
                Selected = (Selected + Buttons.Length - 1) % Buttons.Length;
                break;
            case Key.Down or Key.Tab:
                Selected = (Selected + 1) % Buttons.Length;
                break;
            case Key.Return or Key.KeypadEnter or Key.Space:
                Chosen = Buttons[Selected].Level;
                break;
            default:
                int at = Array.FindIndex(Buttons, b => b.Key == key);
                if (at >= 0)
                {
                    Chosen = Buttons[at].Level;
                }

                break;
        }
    }

    /// <summary>The dialog as the host shows it, with the mouse's arrow when it is in the window.</summary>
    public RgbFrame Frame()
    {
        var screen = new Surface8(_width, _height);
        int y = ButtonRect(0).Top - 2 * 12 - 8;
        foreach (var line in Lines)
        {
            DrawText(screen, (_width - TextWidth(line)) / 2, y, line);
            y += 12;
        }

        for (int i = 0; i < Buttons.Length; i++)
        {
            var r = ButtonRect(i);
            screen.FillRect(r, Border);
            screen.FillRect(new Rect(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), i == Selected ? Lit : Background);
            DrawText(screen, r.X + (r.Width - TextWidth(Buttons[i].Label)) / 2, r.Y + (r.Height - 8) / 2, Buttons[i].Label);
        }

        DrawArrow(screen);
        return RgbFrame.FromIndexed(screen, Palette);
    }

    private int? ButtonAt(int x, int y)
    {
        for (int i = 0; i < Buttons.Length; i++)
        {
            if (ButtonRect(i).Contains(x, y))
            {
                return i;
            }
        }

        return null;
    }

    private int TextWidth(string text) => _font is null ? 0 : text.Sum(c => _font.CharWidth(c));

    private void DrawText(Surface8 screen, int x, int y, string text)
    {
        if (_font is null)
        {
            return;
        }

        foreach (char c in text)
        {
            int width = _font.CharWidth(c);
            Charset.DrawBits1(screen, x, y, _font.Glyph(c), width, 8, Text, ShadowType.None);
            x += width;
        }
    }

    private void DrawArrow(Surface8 screen)
    {
        if (_mouseX < 0 || _mouseY < 0 || _mouseX >= _width || _mouseY >= _height)
        {
            return;
        }

        for (int row = 0; row < Arrow.Length; row++)
        {
            for (int col = 0; col < Arrow[row].Length; col++)
            {
                int x = _mouseX + col, y = _mouseY + row;
                if (Arrow[row][col] != ' ' && x < _width && y < _height)
                {
                    screen[x, y] = Arrow[row][col] == '#' ? Text : Outline;
                }
            }
        }
    }
}
