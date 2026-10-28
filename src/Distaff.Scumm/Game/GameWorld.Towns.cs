using Distaff.Scumm.Graphics;
using Distaff.Scumm.Sound;
using Distaff.Scumm.View;

namespace Distaff.Scumm.Game;

/// <summary>The FM Towns' screen: its height, its two layers and the colours they show in (<c>gfx_towns.cpp</c>).</summary>
public sealed partial class GameWorld
{
    /// <summary>The FM Towns' screen's height: 240 rows, which Loom uses all of.</summary>
    public const int TownsScreenHeight = 240;

    /// <summary>
    /// The 16 colours of the FM Towns' text layer for Loom, which ScummVM sets as the game starts
    /// (<c>tableTownsLoomPalette</c>, <c>towns_setTextPaletteFromPtr</c>).
    /// </summary>
    public static readonly Rgb[] TownsTextPalette =
    [
        new(0x00, 0x00, 0x00), new(0x00, 0x00, 0xAB), new(0x00, 0xAB, 0x00), new(0x00, 0xAB, 0xAB),
        new(0xAB, 0x00, 0x00), new(0x69, 0x29, 0x45), new(0x8C, 0x4D, 0x14), new(0xAB, 0xAB, 0xAB),
        new(0x57, 0x3F, 0x57), new(0x57, 0x57, 0xFF), new(0x57, 0xFF, 0x57), new(0x57, 0xFF, 0xFF),
        new(0xFF, 0x57, 0x57), new(0xD6, 0x94, 0x40), new(0xFF, 0xFF, 0x57), new(0xFF, 0xFF, 0xFF),
    ];

    /// <summary>The colour of the FM Towns' text shadows, and of text in colour 0 (<c>_shadowColor</c> of <c>CharsetRendererTownsV3</c>).</summary>
    private const byte TownsShadowColor = 0x88;

    /// <summary>The screen's height: 200, or the FM Towns' 240.</summary>
    public int ScreenHeight => Screens.ScreenHeight;

    /// <summary>Whether the release is the FM Towns', with its own screen.</summary>
    private bool FmTowns => _loader.FmTowns;

    /// <summary>
    /// The screen in colours, as the host shows it, with the cursor unless <paramref name="cursor"/>
    /// is false. The FM Towns' puts its two layers together, the room in its palette, through the
    /// shadow palette, and the text layer in <see cref="TownsTextPalette"/>, with the cursor in the
    /// text layer's colours (<c>setBuiltinCursor</c>); all in the 15-bit colour ScummVM shows it in.
    /// </summary>
    public RgbFrame Frame(bool cursor = true)
    {
        if (InEffect)
        {
            return _effectFrames.Peek().Frame;
        }

        if (!FmTowns)
        {
            var screen = Screen();
            var colors = DisplayPalette();
            (_shownScreen, _shownColors, _shownStep) = (screen, colors, Steps);
            if (cursor)
            {
                screen = screen.Clone();
                DrawCursor(screen);
            }

            return RgbFrame.FromIndexed(screen, colors);
        }

        Rgb[] textColors = [.. TownsTextPalette.Select(To15Bit)];
        var frame = Screens.ComposeTowns(CurrentRoom is { } room ? RoomPicture(room).Pixels : null, ShownLeft,
            Math.Min(ViewHeight, CurrentRoom?.Height ?? 0), DisplayPalette(), textColors);
        DrawOldStyleBanner(frame, textColors);
        (_shownTownsFrame, _shownStep) = (frame, Steps);
        if (cursor)
        {
            frame = frame.Clone();
            foreach (var (x, y) in CursorPixels(frame.Width, frame.Height))
            {
                frame[x, y] = textColors[_cursorColor & 0x0F];
            }
        }

        return frame;
    }

    /// <summary>
    /// A colour as ScummVM's 16-bit screen shows it: 5 bits for each of red, green and blue
    /// (<c>get16BitColor</c> to RGB555), widened again (<c>PixelFormat::colorToRGB</c>).
    /// </summary>
    public static Rgb To15Bit(Rgb color)
    {
        static byte Widen(byte value) => (byte)((value >> 3 << 3) | (value >> 5));
        return new Rgb(Widen(color.R), Widen(color.G), Widen(color.B));
    }

    /// <summary>
    /// A rectangle of the screen filled at once in the FM Towns' text layer and its text surface,
    /// in a colour of 4 bits doubled (<c>towns_fillTopLayerRect</c> and the text surface's fill in
    /// <c>drawBox</c>).
    /// </summary>
    private void FillTownsTextLayer(Rect rect, byte color)
    {
        Screens.TextSurface.FillRect(rect, color);
        Screens.TownsLayer?.FillRect(rect, color);
    }

    /// <summary>The FM Towns' sound player.</summary>
    private PlayerTowns TownsPlayer => (PlayerTowns)Sound.Player;

    /// <summary>
    /// The FM Towns' question to the CD, which its scripts ask where the others start music.
    /// Mirrors <c>o5_startMusic</c> for FM Towns version 3: 0 asks whether the CD has stopped,
    /// 254 for the sound playing on it, 255 for its volume; the rest (pausing, resuming, a
    /// track's length) answer 0, as in ScummVM.
    /// </summary>
    private int CdQuery(int question) => question switch
    {
        0 => Sound.Cd.IsPlaying ? 0 : 1,
        0xFE => TownsPlayer.CurrentCdSound,
        0xFF => TownsPlayer.CurrentCdVolume,
        _ => 0,
    };

    /// <summary>
    /// <c>resourceRoutines</c>' FM Towns sub-operations, the sound first: 35 sets the CD's volume,
    /// the right then the left; 36 a sound's volume for its next start, the left then the right;
    /// 37 its note.
    /// </summary>
    private void TownsSoundOps(int code, int[] a)
    {
        switch (code)
        {
            case 35:
                TownsPlayer.SetVolumeCd(a[1], a[0]);
                break;
            case 36:
                TownsPlayer.SetSoundVolume(a[0], a[1], a[2]);
                break;
            case 37:
                TownsPlayer.SetSoundNote(a[0], a[1]);
                break;
        }
    }

    /// <summary>
    /// What a save keeps of the FM Towns' screen and sound: the text layer's colours, where the
    /// text last drew, the shadow colour <c>towns_setPaletteFromPtr</c> picks from the room's
    /// palette (the darkest of colours 1 to 15), the flags as Loom leaves them (no cycling fields,
    /// the layers both on), and the sound player's state.
    /// </summary>
    private void CaptureTowns(Saves.ScummState s)
    {
        s.HasTownsData = true;
        for (int i = 0; i < 16; i++)
        {
            (s.TownsTextPalette[i * 3], s.TownsTextPalette[i * 3 + 1], s.TownsTextPalette[i * 3 + 2]) = (TownsTextPalette[i].R, TownsTextPalette[i].G, TownsTextPalette[i].B);
        }

        s.TownsCurStringRect = [_curStringRect.Left, _curStringRect.Top, _curStringRect.Right, _curStringRect.Bottom];
        int shadow = 1, darkest = 48;
        for (int i = 1; i < 16; i++)
        {
            int value = CurrentPalette[i].R + CurrentPalette[i].G + CurrentPalette[i].B;
            if (darkest > value)
            {
                (shadow, darkest) = (i, value);
            }
        }

        s.TownsOverrideShadowColor = shadow;
        s.SoundEngineData = TownsPlayer.SaveState();
    }

    /// <summary>Clears the FM Towns' text surface and text layer (<c>clearLayer(1)</c> with the text surface).</summary>
    private void ClearTownsText()
    {
        Screens.ClearTextSurface();
        Screens.TownsLayer?.Fill(0);
    }
}
