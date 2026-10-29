using Distaff.Scumm.Graphics;

namespace Distaff.Scumm.Game;

/// <summary>
/// The screen effects of a room change (ScummVM's <c>fadeOut</c>, <c>fadeIn</c> and
/// <c>dissolveEffect</c>). Loom's scripts pick them with <c>o4_oldRoomEffect</c>: 128 dissolves
/// (pixel by pixel on the VGA CD, in 8 by 4 blocks otherwise; <c>dissolveEffectSelector</c>), 129
/// blacks the room out at once, 0 does nothing. ScummVM draws an effect on its screen as it goes,
/// stopping a quarter-frame now and then to show it, while the game itself waits; Distaff keeps
/// the screen as each stop shows it, and the steps after the frame show them one by one, each for
/// its quarter-frame, before the frame ends with its sounds (<see cref="Step"/>). The iris and box
/// wipes and the scrolls are not ported: Loom does not use them.
/// </summary>
public sealed partial class GameWorld
{
    // the room effects (_switchRoomEffect, _switchRoomEffect2, _newEffect, _doEffect, _screenEffectFlag)
    private int _switchRoomEffect;
    private int _switchRoomEffect2;
    private int _newEffect = 129;
    private bool _doEffect;
    private bool _screenEffectFlag;

    /// <summary>Whether the frame showed a new room by its effect, after which what was clicked meanwhile goes.</summary>
    private bool _clearClicks;

    /// <summary>The screens an effect stopped on, each with the quarter-frames it shows for, still to show.</summary>
    private readonly Queue<(RgbFrame Frame, TimeSpan Length, int QuarterFrames)> _effectFrames = new();

    /// <summary>What ScummVM's screen shows while an effect is drawn on it, from the frame before the effect on.</summary>
    private RgbFrame? _display;

    /// <summary>The last screen shown, without the cursor, and the step it was shown after: where an effect starts from.</summary>
    private Surface8? _shownScreen;
    private long _shownStep = -1;
    private Rgb[]? _shownColors;
    private RgbFrame? _shownTownsFrame;

    /// <summary>Whether the screen is showing an effect, the frame waiting to end until it has.</summary>
    public bool InEffect => _effectFrames.Count > 0;

    /// <summary>
    /// Sets the effects of the next room change, or, given 0, shows the room now with the effect
    /// waiting for it. Mirrors <c>o4_oldRoomEffect</c>: the low byte is the effect that shows the
    /// new room, the high byte the one that hides the old.
    /// </summary>
    private void OldRoomEffect(int a)
    {
        if (a != 0)
        {
            _switchRoomEffect = a & 0xFF;
            _switchRoomEffect2 = (a >> 8) & 0xFF;
        }
        else
        {
            FadeIn(_newEffect);
        }
    }

    /// <summary>
    /// Hides the room before another is loaded. Mirrors <c>fadeOut</c>: unless the screen is
    /// already hidden, the room band goes to colour 0 by the effect asked for (129 at once,
    /// without showing it; 128 dissolving; 134 dissolving pixel by pixel, as the VGA CD's restart
    /// does).
    /// </summary>
    private void FadeOut(int effect)
    {
        WaitForScroll(0);
        if (_screenEffectFlag && effect != 0)
        {
            var display = Display();
            var black = DisplayPalette()[0];
            switch (effect)
            {
                case 128:
                    DissolveEffectSelector(display, (x, y) => black);
                    break;
                case 129:
                    FillMain(display, black);
                    break;
                case 134:
                    DissolveEffect(display, (x, y) => black, 1, 1);
                    break;
                default:
                    _unhandled.Add($"fadeOut({effect})");
                    FillMain(display, black);
                    break;
            }
        }

        _screenEffectFlag = false;
    }

    /// <summary>
    /// Shows the room by the effect asked for. Mirrors <c>fadeIn</c>: 128 dissolves from what the
    /// screen shows to the room as it is now drawn; 0 and 129 leave it to the frame's drawing.
    /// </summary>
    private void FadeIn(int effect)
    {
        WaitForScroll(0);
        if (effect == 128)
        {
            var display = Display();
            UpdateTownsPicture();
            var room = ComposeFrame();
            DissolveEffectSelector(display, (x, y) => room[x, y]);
        }
        else if (effect is not (0 or 129))
        {
            _unhandled.Add($"fadeIn({effect})");
        }

        _screenEffectFlag = true;
    }

    /// <summary>Loom's dissolve: pixel by pixel on the VGA CD, in blocks of 8 by 4 otherwise (<c>dissolveEffectSelector</c>).</summary>
    private void DissolveEffectSelector(RgbFrame display, Func<int, int, Rgb> source)
    {
        if (_loader.Version == 4)
        {
            DissolveEffect(display, source, 1, 1);
        }
        else
        {
            DissolveEffect(display, source, 8, 4);
        }
    }

    /// <summary>
    /// Draws the room band's pieces of width by height from the source in a random order. Mirrors
    /// <c>dissolveEffect</c>: pixel by pixel, the screen is shown after each eighteenth of them; in
    /// blocks, after each block that starts on the top line within the band's height from the left;
    /// each time for a quarter-frame.
    /// </summary>
    private void DissolveEffect(RgbFrame display, Func<int, int, Rgb> source, int width, int height)
    {
        var vs = Screens.Main;
        int vw = ScreenWidth, vh = vs.Height, pitch = ScreenWidth;
        int w = vw / width + (vw % width != 0 ? 1 : 0);
        int h = vh / height + (vh % height != 0 ? 1 : 0);
        var offsets = new int[w * h];
        bool pixels = width == 1 && height == 1;
        if (pixels)
        {
            for (int i = 0; i < offsets.Length; i++)
            {
                offsets[i] = i;
            }

            for (int i = 1; i < offsets.Length; i++)
            {
                int j = Engine.Random.Next(i - 1);
                offsets[i] = offsets[j];
                offsets[j] = i;
            }
        }
        else
        {
            int n = 0;
            for (int x = 0; x < vw; x += width)
            {
                for (int y = 0; y < vh; y += height)
                {
                    offsets[n++] = y * pitch + x;
                }
            }

            var original = (int[])offsets.Clone();
            for (int i = 1; i < offsets.Length; i++)
            {
                int j = Engine.Random.Next(i - 1);
                offsets[i] = offsets[j];
                offsets[j] = original[i];
            }
        }

        int blitsToFreeze = h / 8;
        int blitsBeforeRefresh = w * h / (blitsToFreeze == 0 ? 18 : blitsToFreeze);
        int blits = 0;
        for (int i = 0; i < offsets.Length; i++)
        {
            int x0 = offsets[i] % pitch, y0 = offsets[i] / pitch;
            for (int y = y0; y < Math.Min(y0 + height, vh); y++)
            {
                for (int x = x0; x < Math.Min(x0 + width, vw); x++)
                {
                    display[x, vs.Top + y] = source(x, vs.Top + y);
                }
            }

            bool halt = pixels ? ++blits >= blitsBeforeRefresh : offsets[i] < vh;
            if (halt)
            {
                blits = 0;
                _effectFrames.Enqueue((display.Clone(), TimeSpan.FromSeconds(1 / QuarterFrameRate), 1));
            }
        }
    }

    /// <summary>What ScummVM's screen shows now: the last screen shown, or the effect drawn over it since.</summary>
    private RgbFrame Display()
    {
        if (_display is null)
        {
            if (_shownStep != Steps)
            {
                // nothing was shown after the last step (a host that does not look, as the tests):
                // the screen as it is drawn now, the old room while a scene starts
                _display = ComposeFrame();
            }
            else if (_shownTownsFrame is { } towns)
            {
                _display = towns.Clone();
            }
            else if (_shownScreen is { } screen && _shownColors is { } colors)
            {
                _display = RgbFrame.FromIndexed(screen, colors);
            }
            else
            {
                _display = ComposeFrame();
            }
        }

        return _display;
    }

    /// <summary>The screen as it is drawn now, without the cursor.</summary>
    private RgbFrame ComposeFrame() => FmTowns
        ? Screens.ComposeTowns(PictureLeft, DisplayPalette(), [.. TownsTextPalette.Select(To15Bit)])
        : RgbFrame.FromIndexed(Screen(), DisplayPalette());

    private void FillMain(RgbFrame display, Rgb color)
    {
        for (int y = Screens.Main.Top; y < Screens.Main.Top + Screens.Main.Height; y++)
        {
            display.Row(y).Fill(color);
        }
    }

    /// <summary>
    /// Shows the next screen an effect stopped on, the time it showed for passing for the sound
    /// and the CD's timer (<c>waitForTimer</c>, whose wait the game's own timers do not count);
    /// after the last, the frame ends.
    /// </summary>
    private void StepEffect()
    {
        var (_, length, quarterFrames) = _effectFrames.Dequeue();
        Time += length;
        AdvanceCdTimer(quarterFrames);
        Sound.Update(Time);
        if (_effectFrames.Count == 0)
        {
            EndFrame();
            RestartRequested |= _restartAfterEffect;
        }
    }

    /// <summary>A quarter-frame's rate: the DOS timer's, or 240 a second on the FM Towns.</summary>
    private double QuarterFrameRate => FmTowns ? 240.0 : TimerFrequency;
}
