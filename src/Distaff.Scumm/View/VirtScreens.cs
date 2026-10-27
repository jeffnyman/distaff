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
/// and the composition of <c>drawStripToScreen</c>.
/// </summary>
public sealed class VirtScreens
{
    /// <summary>A pixel nothing is drawn in (<c>CHARSET_MASK_TRANSPARENCY</c>).</summary>
    public const byte Transparent = 0xFD;

    public const int ScreenWidth = 320, ScreenHeight = 200;

    public VirtScreens()
    {
        TextSurface.Fill(Transparent);
        Init(16, 144, ScreenWidth);
    }

    public VirtScreen Main { get; private set; } = null!;

    public VirtScreen Text { get; private set; } = null!;

    public VirtScreen Verb { get; private set; } = null!;

    /// <summary>Text drawn over the main band, in screen coordinates (<c>_textSurface</c>).</summary>
    public Surface8 TextSurface { get; } = new(ScreenWidth, ScreenHeight);

    /// <summary>
    /// Lays the bands out: text above <paramref name="b"/>, the room to <paramref name="h"/>, verbs
    /// below, every band cleared. Mirrors <c>initScreens</c>.
    /// </summary>
    public void Init(int b, int h, int roomWidth)
    {
        Text = Band(VirtScreenNumber.Text, 0, b, ScreenWidth, 0);
        Main = Band(VirtScreenNumber.Main, b, h - b, Math.Max(roomWidth, ScreenWidth), Transparent);
        Verb = Band(VirtScreenNumber.Verb, h, ScreenHeight - h, ScreenWidth, 0);
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
    public void ClearTextSurface() => TextSurface.Fill(Transparent);

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

        if (roomPicture is not null)
        {
            screen.Blit(roomPicture, new Rect(cameraLeft, 0, ScreenWidth, Math.Min(viewHeight, Main.Height)), 0, Main.Top);
        }

        if (Main.Front is { } main)
        {
            Overlay(screen, main, cameraLeft, Main.Top, Main.Height);
        }

        if (Verb.Front is { } verb)
        {
            screen.Blit(verb, verb.Bounds, 0, Verb.Top);
        }

        Overlay(screen, TextSurface, 0, 0, ScreenHeight);
        return screen;
    }

    private static void Overlay(Surface8 screen, Surface8 layer, int left, int top, int height)
    {
        for (int y = 0; y < height && y < layer.Height && top + y < ScreenHeight; y++)
        {
            for (int x = 0; x < ScreenWidth && left + x < layer.Width; x++)
            {
                byte value = layer[left + x, y];
                if (value != Transparent && top + y >= 0)
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
