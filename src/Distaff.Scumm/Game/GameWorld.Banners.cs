using System.Text;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Input;
using Distaff.Scumm.Scripts;

namespace Distaff.Scumm.Game;

/// <summary>
/// The banners of version 3's original interface, as ScummVM draws them
/// (<c>showOldStyleBannerAndPause</c>): a band 12 rows high across the screen, edged above and
/// below in the banner's colour, the words in charset 0 in the same colour. On the EGA it is
/// black, at row 80, the words centred; on the FM Towns it is in colour 8, 12 rows above the main
/// band's middle, in the text layer, the words from where half their width (at least 100) is left
/// of the middle. The game pauses, the sound with it, until a key or a click, or the banner's
/// time is up. The VGA CD's banners are its menu's (<see cref="ShowBanner"/>).
/// </summary>
public sealed partial class GameWorld
{
    private const int BannerHeight = 12;

    /// <summary>A version 3 banner up: its words, its band and where it is, the quarter-frames it has left (-1 until a key), and what follows.</summary>
    private sealed class OldStyleBanner(string text, int top, Surface8 pixels, int waitTime, Action<(Key Key, char Ascii, KeyModifiers Modifiers)?> then)
    {
        public string Text { get; } = text;
        public int Top { get; } = top;
        public Surface8 Pixels { get; } = pixels;
        public int WaitTime { get; set; } = waitTime;
        public Action<(Key Key, char Ascii, KeyModifiers Modifiers)?> Then { get; } = then;
    }

    private OldStyleBanner? _oldBanner;

    /// <summary>Whether the screen snaps to where the camera goes instead of scrolling (<c>_snapScroll</c>, Alt+I on version 3, Ctrl+R on version 4).</summary>
    private bool _snapScroll;

    /// <summary>Whether a restart was asked for, by the restart key's question or a script (<c>restart</c>); the host starts the game again.</summary>
    public bool RestartRequested { get; private set; }

    /// <summary>
    /// Shows a banner until a key or a click, or for <paramref name="waitTime"/> quarter-frames
    /// (-1 for no limit), then does what follows with the key. Mirrors
    /// <c>showOldStyleBannerAndPause</c>, which for Loom VGA is <c>showBannerAndPause</c>.
    /// </summary>
    private void ShowOldStyleBanner(string text, int color, int waitTime, Action<(Key Key, char Ascii, KeyModifiers Modifiers)?> then)
    {
        if (_loader.Version == 4)
        {
            ShowBanner(text, then, waitTime);
            return;
        }

        PauseSounds(true);
        int top = FmTowns ? (Screens.Main.Top + Screens.Main.Top + Screens.Main.Height) / 2 - BannerHeight : 80;
        var pixels = new Surface8(ScreenWidth, BannerHeight);
        pixels.Fill(FmTowns ? (byte)8 : (byte)0);
        pixels.Row(0).Fill((byte)color);
        pixels.Row(BannerHeight - 1).Fill((byte)color);

        // drawGUIText in charset 0: centred on the EGA, from the left of a measured box on the FM Towns
        if (_font is { } font)
        {
            byte[] chars = Encoding.Latin1.GetBytes(text);
            int width = chars.Sum(c => font.CharWidth(c));
            int x = FmTowns ? (ScreenWidth - Math.Max(width, 100)) / 2 : ScreenWidth / 2 - width / 2;
            int low = color & 0x0F;
            byte ink = FmTowns ? (byte)(low | (low << 4)) : (byte)low;
            foreach (byte c in chars)
            {
                int w = font.CharWidth(c);
                if (c != '@' && x + w <= ScreenWidth)
                {
                    Charset.DrawBits1(pixels, x, 2, font.Glyph(c), w, 8, ink, ShadowType.None);
                }

                x += w;
            }
        }

        _oldBanner = new OldStyleBanner(text, top, pixels, waitTime, then);
        _guiClick = null;
        _guiKey = null;
    }

    /// <summary>The banner goes, the game and its sound go on, and what was clicked or pressed meanwhile goes too (<c>clearClickedStatus</c>).</summary>
    private void EndOldStyleBanner(OldStyleBanner banner, (Key Key, char Ascii, KeyModifiers Modifiers)? key)
    {
        _oldBanner = null;
        PauseSounds(_menuActive);
        _input = 0;
        banner.Then(key);
    }

    /// <summary>A step while a version 3 banner is up: its time runs down, and a key or click ends it (<c>waitForBannerInput</c>).</summary>
    private void OldStyleBannerStep(OldStyleBanner banner, int quarterFrames, (int X, int Y, bool Right)? click, (Key Key, char Ascii, KeyModifiers Modifiers)? key)
    {
        if (click is not null || key is not null)
        {
            EndOldStyleBanner(banner, key);
        }
        else if (banner.WaitTime > 0 && (banner.WaitTime -= quarterFrames) <= 0)
        {
            EndOldStyleBanner(banner, null);
        }
    }

    /// <summary>The banner over the EGA's screen, in its band's rows.</summary>
    private void DrawOldStyleBanner(Surface8 screen)
    {
        if (_oldBanner is { } banner && !FmTowns)
        {
            for (int y = 0; y < BannerHeight && banner.Top + y < screen.Height; y++)
            {
                banner.Pixels.Row(y).CopyTo(screen.Row(banner.Top + y));
            }
        }
    }

    /// <summary>The banner in the FM Towns' text layer, which it covers where it is (<c>towns_swapVirtScreenArea</c>).</summary>
    private void DrawOldStyleBanner(RgbFrame frame, ReadOnlySpan<Rgb> textColors)
    {
        if (_oldBanner is { } banner && FmTowns)
        {
            for (int y = 0; y < BannerHeight && banner.Top + y < frame.Height; y++)
            {
                var row = banner.Pixels.Row(y);
                var dest = frame.Row(banner.Top + y);
                for (int x = 0; x < ScreenWidth; x++)
                {
                    if (row[x] != 0)
                    {
                        dest[x] = textColors[row[x] & 0x0F];
                    }
                }
            }
        }
    }

    /// <summary>
    /// Asks whether to restart, the game's question but for its last letter, which is the answer
    /// that restarts. Mirrors <c>queryRestart</c>: Loom restarts without fading out on version 3;
    /// the VGA CD's dissolve out (134) is not shown, the host starting the game afresh.
    /// </summary>
    private void QueryRestart()
    {
        string prompt = GuiString(GsRestart);
        if (prompt.Length == 0)
        {
            return;
        }

        char yes = char.ToLowerInvariant(prompt[^1]);
        ShowOldStyleBanner(prompt[..^1], 12, -1, key =>
        {
            if (key is { } k && char.ToLowerInvariant(k.Ascii) == yes)
            {
                RestoreCharsetBg();
                RestartRequested = true;
            }
        });
    }

    /// <summary>The screen snaps to the camera, or scrolls again, and says so; the camera's variable follows (Alt+I, Ctrl+R on version 4).</summary>
    private void ToggleSnapScroll()
    {
        _snapScroll = !_snapScroll;
        string text = _loader.Version == 4
            ? (_snapScroll ? "Horizontal Screen Snap" : "Horizontal Screen Scroll")
            : (_snapScroll ? "Screen reposition instantly" : "Screen reposition by Scrolling");
        ShowOldStyleBanner(text, 9, 90, _ => Engine.Variables[Vars.CameraFastX] = _snapScroll ? 1 : 0);
    }
}
