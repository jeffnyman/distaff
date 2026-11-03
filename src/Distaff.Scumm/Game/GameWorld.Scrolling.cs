using Distaff.Scumm.Scripts;

namespace Distaff.Scumm.Game;

/// <summary>
/// The FM Towns' smooth scrolling, which ScummVM has on by default (its <c>smooth_scroll</c>
/// option). A camera that moves a strip draws the new strip at once and asks the picture layer to
/// scroll the 8 pixels (<c>requestScroll</c>); the layer then scrolls a pixel at each tick of a
/// 60th of a second, counted in whole milliseconds (<c>towns_updateGfx</c>, <c>scrollLayer</c>),
/// so the picture shows the room as far behind the camera as the scroll has still to go. While it
/// scrolls the game slows to keep with it: each tick that leaves the layer still scrolling makes
/// the next frame's wait count it as three quarters of a tick (<c>_scrollDeltaAdjust</c> in
/// <c>ScummEngine::go</c>). The game waits for the scroll where it would otherwise outrun it: a
/// camera that turns waits for the scroll the other way to end, one that has run 184 pixels ahead
/// waits until it is less (the layer is 512 pixels wide), and a room is not hidden or shown until
/// the scroll ends (<c>towns_waitForScroll</c>). Those waits show the scroll as the screen effects
/// show their steps; the waits between frames are steps of their own, a tick each.
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>The picture layer's width (<c>setupLayer(0, 512, ...)</c>).</summary>
    private const int TownsLayerWidth = 512;

    /// <summary>A tick of the scroll: a 60th of a second in whole milliseconds (<c>1000 / 60</c>).</summary>
    private static readonly TimeSpan ScrollTick = TimeSpan.FromMilliseconds(1000 / 60);

    // the layer's scroll still to go and the request not yet taken (scrollRemainder, _scrollRequest),
    // the ticks the next frame's wait counts short (_scrollDeltaAdjust), and the next tick (_scrollTimer)
    private int _scrollRemainder;
    private int _scrollRequest;
    private int _scrollDeltaAdjust;
    private TimeSpan _scrollTimer;

    // the picture layer's column the camera's view is copied to (_scrollDestOffset)
    private int _layerOffset;

    /// <summary>When the frame being waited for runs, and its ticks, once its wait has begun.</summary>
    private TimeSpan? _waitEnd;
    private int _waitTicks;

    /// <summary>The quarter-frames the waits within a frame have run, not yet counted whole.</summary>
    private double _scrollQuarterFrames;

    /// <summary>
    /// Whether the FM Towns' picture scrolls smoothly, as ScummVM has it by default; off, the camera
    /// moves a strip at a time. Other releases always move a strip at a time.
    /// </summary>
    public bool SmoothScrolling { get; set; } = true;

    private bool Smooth => FmTowns && SmoothScrolling;

    /// <summary>
    /// Whether the FM Towns' picture may scroll 4 pixels a tick instead of 1 while the game runs
    /// without waiting between frames, as the opening's fast camera does; ScummVM's
    /// <c>semi_smooth_scroll</c>, off by default.
    /// </summary>
    public bool SemiSmoothScrolling { get; set; }

    /// <summary>The picture layer's column at the screen's left (<c>hScroll</c>): where the view is copied, as far behind as the scroll has still to go.</summary>
    private int PictureLeft => (_layerOffset + _scrollRemainder + _scrollRequest) & (TownsLayerWidth - 1);

    /// <summary>The camera's view goes into the picture layer, as ScummVM draws what changed to the screen.</summary>
    private void UpdateTownsPicture()
    {
        if (FmTowns)
        {
            Screens.CopyToTownsPicture(CurrentRoom is { } room ? RoomPicture(room).Pixels : null, Camera.Left,
                Math.Min(ViewHeight, CurrentRoom?.Height ?? 0), _layerOffset);
        }
    }

    private bool Scrolling => _scrollRemainder != 0 || _scrollRequest != 0;

    /// <summary>The room's column at the screen's left: the camera's, or as far behind it as the picture has still to scroll.</summary>
    internal int ShownLeft
    {
        get
        {
            int left = Camera.Left + _scrollRemainder + _scrollRequest;
            return Math.Clamp(left, 0, Math.Max(0, (CurrentRoom?.Width ?? ScreenWidth) - ScreenWidth));
        }
    }

    /// <summary>A frame's wait in ticks of the game's timer, at least 1: <c>VAR_TIMER_NEXT</c>, the ticks the scroll held back counted three quarters each.</summary>
    private int FrameTicks()
    {
        int delta = Engine.Variables[Vars.TimerNext];
        if (_scrollDeltaAdjust != 0)
        {
            delta = Math.Max(0, delta - _scrollDeltaAdjust) + (Math.Min(_scrollDeltaAdjust, delta) << 2) / 3;
        }

        return Math.Max(1, delta);
    }

    private TimeSpan FrameLength(int ticks) => TimeSpan.FromSeconds(ticks * 4 / (FmTowns ? 240.0 : TimerFrequency));

    /// <summary>
    /// The time to the next step while the FM Towns scrolls smoothly: to the next tick that scrolls
    /// within the frame's wait, or to the wait's end.
    /// </summary>
    private TimeSpan SmoothStepLength()
    {
        var end = _waitEnd ?? Time + FrameLength(FrameTicks());
        return Scrolling && _scrollTimer <= end ? _scrollTimer - Time : end - Time;
    }

    /// <summary>
    /// A step of the wait before a frame, if one is due: the layer scrolls a pixel on the tick. The
    /// frame's ticks are fixed when its wait begins, and the scroll's count for the next starts
    /// again. Returns false when the frame runs now, its ticks in <paramref name="delta"/>.
    /// </summary>
    private bool WaitStep(out int delta)
    {
        if (_waitEnd is null)
        {
            _waitTicks = FrameTicks();
            _scrollDeltaAdjust = 0;
            _waitEnd = Time + FrameLength(_waitTicks);
        }

        var end = _waitEnd.Value;
        if (Scrolling && _scrollTimer <= end)
        {
            Time = _scrollTimer;
            TickScroll();
            Sound.Update(Time);
            delta = 0;
            return true;
        }

        SkipIdleTicks(end);
        Time = end;
        delta = _waitTicks;
        _waitEnd = null;
        return false;
    }

    /// <summary>
    /// A tick: the layer takes the request and scrolls a pixel towards it, or 4 with semi-smooth
    /// scrolling while the game does not wait between frames (<c>VAR_TIMER_NEXT</c> 0), and if it
    /// still has to scroll the next frame waits longer.
    /// </summary>
    private void TickScroll()
    {
        _scrollRemainder += _scrollRequest;
        _scrollRequest = 0;
        if (_scrollRemainder != 0)
        {
            int step = _scrollRemainder > 0 ? -1 : 1;
            _scrollRemainder += SemiSmoothScrolling && Engine.Variables[Vars.TimerNext] == 0 ? step * 4 : step;
        }

        if (_scrollRemainder != 0)
        {
            _scrollDeltaAdjust++;
        }

        _scrollTimer += ScrollTick;
    }

    /// <summary>The ticks up to <paramref name="until"/> pass with nothing to scroll.</summary>
    private void SkipIdleTicks(TimeSpan until)
    {
        if (!Scrolling && _scrollTimer <= until)
        {
            _scrollTimer += ScrollTick * (((until - _scrollTimer).Ticks / ScrollTick.Ticks) + 1);
        }
    }

    /// <summary>
    /// The camera moved a strip: the picture scrolls to it, once the scroll the other way has ended
    /// and the layer is not too far behind. Mirrors <c>requestScroll</c> (-1 when the camera moves
    /// right, <c>scrollLeft</c>).
    /// </summary>
    private void RequestScroll(int dir)
    {
        WaitForScroll(-dir);
        WaitForScroll(dir, (TownsLayerWidth - ScreenWidth - 8) * dir);
        _layerOffset = (_layerOffset - 8 * dir) & (TownsLayerWidth - 1);
        _scrollRequest += 8 * dir;
    }

    /// <summary>
    /// Waits within a frame while a request has not been taken or the layer scrolls in a direction
    /// (0 either) beyond a threshold (<c>towns_waitForScroll</c>, <c>isScrolling</c>). Each tick
    /// waited shows, as an effect's steps do; the game's timers do not count the time.
    /// </summary>
    private void WaitForScroll(int direction, int threshold = 0)
    {
        if (!Smooth)
        {
            return;
        }

        var now = Time + TimeSpan.FromTicks(_effectFrames.Sum(f => f.Length.Ticks));
        SkipIdleTicks(now);
        while (_scrollRequest != 0 || direction switch
        {
            0 => _scrollRemainder != threshold,
            1 => _scrollRemainder > threshold,
            _ => _scrollRemainder < threshold,
        })
        {
            var length = _scrollTimer - now;
            _scrollQuarterFrames += length.TotalSeconds * QuarterFrameRate;
            int quarterFrames = (int)_scrollQuarterFrames;
            _scrollQuarterFrames -= quarterFrames;
            _effectFrames.Enqueue((Display().Clone(), length, quarterFrames));
            now = _scrollTimer;
            TickScroll();
            _display = ComposeFrame();
        }
    }

    /// <summary>The scroll starts afresh, the camera's view in the picture layer: after a load, whose time is the save's.</summary>
    private void ResetScroll()
    {
        _scrollRemainder = _scrollRequest = _scrollDeltaAdjust = 0;
        _scrollTimer = Time;
        _waitEnd = null;
        UpdateTownsPicture();
    }
}
