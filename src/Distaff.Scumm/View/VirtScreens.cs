using Distaff.Scumm.Graphics;

namespace Distaff.Scumm.View;

/// <summary>The parts of the screen (ScummVM's <c>VirtScreenNumber</c>).</summary>
public enum VirtScreenNumber
{
    Main = 0,
    Text = 1,
    Verb = 2,
}

/// <summary>
/// A band of the screen with its own pixels. Mirrors ScummVM's <c>VirtScreen</c>. The main one
/// shows the room; its <see cref="Front"/> holds, in room coordinates, only what is drawn over the
/// room (text and boxes), <see cref="VirtScreens.Transparent"/> elsewhere, because Distaff draws
/// the room afresh each frame. The text and verb bands keep everything drawn into them.
/// </summary>
public sealed class VirtScreen(VirtScreenNumber number, int top, int height, bool hasTwoBuffers, Surface8? front)
{
    public VirtScreenNumber Number { get; } = number;

    /// <summary>Where the band starts on the screen (<c>topline</c>).</summary>
    public int Top { get; } = top;

    public int Height { get; } = height;

    /// <summary>Whether the band keeps a clean copy of what it shows (<c>hasTwoBuffers</c>): only the main one.</summary>
    public bool HasTwoBuffers { get; } = hasTwoBuffers;

    /// <summary>The band's pixels, or null when it has no height.</summary>
    public Surface8? Front { get; } = front;
}

/// <summary>
/// The screen's bands and the text drawn over them. Mirrors ScummVM's <c>initScreens</c>,
/// <c>findVirtScreen</c>, the <c>_textSurface</c> that actors' speech over the room is drawn into,
/// and the composition of <c>drawStripToScreen</c>, or for the FM Towns of
/// <c>towns_drawStripToScreen</c> and <c>TownsScreen</c>.
/// </summary>
public sealed class VirtScreens
{
    /// <summary>A pixel nothing is drawn in (<c>CHARSET_MASK_TRANSPARENCY</c>).</summary>
    public const byte Transparent = 0xFD;

    public const int ScreenWidth = 320;

    /// <summary>The FM Towns' picture layer's width (<c>setupLayer(0, 512, ...)</c>).</summary>
    public const int TownsPictureWidth = 512;

    private const int Strips = ScreenWidth / 8;

    // the rows of each strip of the text and verb bands drawn since the FM Towns' text layer was
    // last brought up to date with them (tdirty and bdirty)
    private readonly int[][] _dirtyTop = [new int[Strips], new int[Strips], new int[Strips]];
    private readonly int[][] _dirtyBottom = [new int[Strips], new int[Strips], new int[Strips]];

    /// <param name="height">The screen's height: 200, or the FM Towns' 240.</param>
    /// <param name="fmTowns">Whether the screen is the FM Towns', with its text layer; its text surface holds 0 where nothing is drawn.</param>
    public VirtScreens(int height = 200, bool fmTowns = false)
    {
        ScreenHeight = height;
        TextTransparent = fmTowns ? (byte)0 : Transparent;
        TextSurface = new Surface8(ScreenWidth, height);
        TextSurface.Fill(TextTransparent);
        TownsLayer = fmTowns ? new Surface8(ScreenWidth, height) : null;
        TownsPicture = fmTowns ? new Surface8(TownsPictureWidth, height) : null;
        Init(16, 144, ScreenWidth);
    }

    /// <summary>The screen's height.</summary>
    public int ScreenHeight { get; }

    /// <summary>What the text surface holds where nothing is drawn.</summary>
    public byte TextTransparent { get; }

    public VirtScreen Main { get; private set; } = null!;

    public VirtScreen Text { get; private set; } = null!;

    public VirtScreen Verb { get; private set; } = null!;

    /// <summary>
    /// Text drawn over the main band, in screen coordinates (<c>_textSurface</c>). The FM Towns
    /// draws all its text here, whatever the band, and boxes too, in colours of 4 bits doubled.
    /// </summary>
    public Surface8 TextSurface { get; }

    /// <summary>
    /// The FM Towns' text layer (the <c>TownsScreen</c>'s layer 1) where it lies over the text and
    /// verb bands, null for the other releases. It is brought up to date with what was drawn in
    /// the bands at the end of a step (<see cref="FlushTownsLayer"/>), and boxes are drawn into it
    /// at once. Over the main band the text surface shows as it is.
    /// </summary>
    public Surface8? TownsLayer { get; }

    /// <summary>
    /// The FM Towns' picture layer (the <c>TownsScreen</c>'s layer 0), null for the other releases:
    /// 512 columns going round, into which the camera's view is copied from a column that moves 8
    /// when the camera moves a strip (<c>_scrollDestOffset</c>), and which the screen shows from a
    /// column of its own that follows a pixel a tick (<c>hScroll</c>). The columns the screen shows
    /// behind the camera keep what was last copied there. Loom's never clears it.
    /// </summary>
    public Surface8? TownsPicture { get; }

    /// <summary>
    /// Lays the bands out: text above <paramref name="b"/>, the room to <paramref name="h"/>, verbs
    /// below, every band cleared and to be shown again. Mirrors <c>initScreens</c>, which for the
    /// FM Towns also clears the text surface and the text layer.
    /// </summary>
    public void Init(int b, int h, int roomWidth)
    {
        Text = Band(VirtScreenNumber.Text, 0, b, ScreenWidth, 0);
        Main = Band(VirtScreenNumber.Main, b, h - b, Math.Max(roomWidth, ScreenWidth), Transparent);
        Verb = Band(VirtScreenNumber.Verb, h, ScreenHeight - h, ScreenWidth, 0);
        foreach (var vs in (VirtScreen[])[Text, Verb])
        {
            Array.Fill(_dirtyTop[(int)vs.Number], 0);
            Array.Fill(_dirtyBottom[(int)vs.Number], vs.Height);
        }

        if (TownsLayer is not null)
        {
            ClearTextSurface();
            TownsLayer.Fill(0);
        }
    }

    /// <summary>
    /// Notes that rows <paramref name="top"/> to <paramref name="bottom"/> of a band, between screen
    /// columns <paramref name="left"/> and <paramref name="right"/>, were drawn, for the FM Towns'
    /// text layer: every strip the columns touch, the right one included. Mirrors
    /// <c>markRectAsDirty</c>.
    /// </summary>
    public void MarkDirty(VirtScreen vs, int left, int right, int top, int bottom)
    {
        if (vs.Number == VirtScreenNumber.Main || left > right || top > bottom || top > vs.Height || bottom < 0)
        {
            return;
        }

        top = Math.Max(top, 0);
        bottom = Math.Min(bottom, vs.Height);
        int lp = left / 8, rp = right / 8;
        if (lp >= Strips || rp < 0)
        {
            return;
        }

        for (int strip = Math.Max(lp, 0); strip <= Math.Min(rp, Strips - 1); strip++)
        {
            _dirtyTop[(int)vs.Number][strip] = Math.Min(_dirtyTop[(int)vs.Number][strip], top);
            _dirtyBottom[(int)vs.Number][strip] = Math.Max(_dirtyBottom[(int)vs.Number][strip], bottom);
        }
    }

    /// <summary>
    /// Brings the FM Towns' text layer up to date with the text and verb bands where they were
    /// drawn: the text surface's colour where it has one, the band's low four bits elsewhere.
    /// Mirrors <c>updateDirtyScreen</c> and <c>towns_drawStripToScreen</c> for those bands.
    /// </summary>
    public void FlushTownsLayer()
    {
        foreach (var vs in (VirtScreen[])[Text, Verb])
        {
            var tops = _dirtyTop[(int)vs.Number];
            var bottoms = _dirtyBottom[(int)vs.Number];
            for (int strip = 0; strip < Strips; strip++)
            {
                if (bottoms[strip] == 0)
                {
                    continue;
                }

                if (TownsLayer is not null && vs.Front is { } front)
                {
                    for (int y = tops[strip]; y < bottoms[strip]; y++)
                    {
                        for (int x = strip * 8; x < strip * 8 + 8; x++)
                        {
                            int text = TextSurface[x, vs.Top + y] & 0x0F;
                            TownsLayer[x, vs.Top + y] = (byte)(text != 0 ? text : front[x, y] & 0x0F);
                        }
                    }
                }

                tops[strip] = vs.Height;
                bottoms[strip] = 0;
            }
        }
    }

    /// <summary>A new room: the main band takes its width, with nothing drawn over it.</summary>
    public void NewRoom(int roomWidth)
        => Main = Band(VirtScreenNumber.Main, Main.Top, Main.Height, Math.Max(roomWidth, ScreenWidth), Transparent);

    /// <summary>The band a screen line is in. Mirrors <c>findVirtScreen</c>.</summary>
    public VirtScreen? Find(int y)
    {
        foreach (var vs in (VirtScreen[])[Main, Text, Verb])
        {
            if (y >= vs.Top && y < vs.Top + vs.Height)
            {
                return vs;
            }
        }

        return null;
    }

    /// <summary>Mirrors <c>clearTextSurface</c>.</summary>
    public void ClearTextSurface() => TextSurface.Fill(TextTransparent);

    /// <summary>The room is drawn again over the columns of the main band from <paramref name="left"/> on (<c>redrawBGStrip</c>).</summary>
    public void RedrawMain(int left = 0, int width = int.MaxValue)
        => Main.Front?.FillRect(new Rect(left, 0, Math.Min(width, Main.Front.Width), Main.Front.Height), Transparent);

    /// <summary>The room shows again in a rectangle of the main band, in room coordinates (<c>resetBackground</c>, an object drawn).</summary>
    public void RestoreMain(Rect rect) => Main.Front?.FillRect(rect, Transparent);

    /// <summary>
    /// The screen as shown: each band, the room's view under what is drawn over it, and the text
    /// over everything. Mirrors the composition of <c>drawStripToScreen</c>.
    /// </summary>
    public Surface8 Compose(Surface8? roomPicture, int cameraLeft, int viewHeight)
    {
        var screen = new Surface8(ScreenWidth, ScreenHeight);
        if (Text.Front is { } text)
        {
            screen.Blit(text, text.Bounds, 0, Text.Top);
        }

        ComposeMain(screen, roomPicture, cameraLeft, viewHeight);
        if (Verb.Front is { } verb)
        {
            screen.Blit(verb, verb.Bounds, 0, Verb.Top);
        }

        Overlay(screen, TextSurface, 0, 0, ScreenHeight, TextTransparent);
        return screen;
    }

    /// <summary>
    /// The FM Towns' screen, its two layers put together (<c>TownsScreen::updateScreenBuffer</c>).
    /// The picture layer holds the main band alone, the room's view under what is drawn over it,
    /// in <paramref name="roomColors"/>; elsewhere it is black. Over it the text layer shows in
    /// <paramref name="textColors"/> wherever it is not 0: the text surface over the main band, as
    /// <c>towns_drawStripToScreen</c> copies it there, and <see cref="TownsLayer"/> elsewhere.
    /// </summary>
    public RgbFrame ComposeTowns(int pictureLeft, ReadOnlySpan<Rgb> roomColors, ReadOnlySpan<Rgb> textColors)
    {
        var frame = new RgbFrame(ScreenWidth, ScreenHeight);
        for (int y = 0; y < ScreenHeight; y++)
        {
            bool main = y >= Main.Top && y < Main.Top + Main.Height;
            var text = (main ? TextSurface : TownsLayer!).Row(y);
            var frameRow = frame.Row(y);
            var pictureRow = TownsPicture!.Row(y);
            for (int x = 0; x < ScreenWidth; x++)
            {
                if (text[x] != 0)
                {
                    frameRow[x] = textColors[text[x] & 0x0F];
                }
                else if (main)
                {
                    frameRow[x] = roomColors[pictureRow[(pictureLeft + x) & (TownsPictureWidth - 1)]];
                }
            }
        }

        return frame;
    }

    /// <summary>
    /// The camera's view of the main band, the room under what is drawn over it, is copied into the
    /// FM Towns' picture layer from column <paramref name="layerLeft"/> on, going round its 512
    /// (<c>towns_drawStripToScreen</c>, the whole view rather than its strips drawn again).
    /// </summary>
    public void CopyToTownsPicture(Surface8? roomPicture, int cameraLeft, int viewHeight, int layerLeft)
    {
        var picture = new Surface8(ScreenWidth, ScreenHeight);
        ComposeMain(picture, roomPicture, cameraLeft, viewHeight);
        for (int y = Math.Max(0, Main.Top); y < Main.Top + Main.Height && y < ScreenHeight; y++)
        {
            var from = picture.Row(y);
            var to = TownsPicture!.Row(y);
            for (int x = 0; x < ScreenWidth; x++)
            {
                to[(layerLeft + x) & (TownsPictureWidth - 1)] = from[x];
            }
        }
    }

    /// <summary>The room's view, and what is drawn over it, into the main band of a screen.</summary>
    private void ComposeMain(Surface8 screen, Surface8? roomPicture, int cameraLeft, int viewHeight)
    {
        if (roomPicture is not null)
        {
            screen.Blit(roomPicture, new Rect(cameraLeft, 0, ScreenWidth, Math.Min(viewHeight, Main.Height)), 0, Main.Top);
        }

        if (Main.Front is { } main)
        {
            Overlay(screen, main, cameraLeft, Main.Top, Main.Height, Transparent);
        }
    }

    private void Overlay(Surface8 screen, Surface8 layer, int left, int top, int height, byte transparent)
    {
        for (int y = 0; y < height && y < layer.Height && top + y < ScreenHeight; y++)
        {
            for (int x = 0; x < ScreenWidth && left + x < layer.Width; x++)
            {
                byte value = layer[left + x, y];
                if (value != transparent && top + y >= 0)
                {
                    screen[x, top + y] = value;
                }
            }
        }
    }

    private static VirtScreen Band(VirtScreenNumber number, int top, int height, int width, byte fill)
    {
        Surface8? front = null;
        if (height > 0)
        {
            front = new Surface8(width, height);
            front.Fill(fill);
        }

        return new VirtScreen(number, top, height, number == VirtScreenNumber.Main, front);
    }
}
