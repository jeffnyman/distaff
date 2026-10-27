namespace Distaff.Scumm.View;

/// <summary>Mirrors <c>kNormalCameraMode</c>, <c>kFollowActorCameraMode</c> and <c>kPanningCameraMode</c>.</summary>
public enum CameraMode
{
    Normal = 1,
    FollowActor = 2,
    Panning = 3,
}

/// <summary>
/// Where the view of the room is: the camera's centre, in pixels, and where it is heading. The
/// view is 40 strips (320 pixels) wide. Mirrors ScummVM's <c>CameraData</c> and, for version 3,
/// <c>setCameraAt</c>, <c>setCameraFollows</c>, <c>moveCamera</c>, <c>cameraMoved</c> and
/// <c>panCameraTo</c> in <c>camera.cpp</c>. The camera's limits, the variables
/// <c>VAR_CAMERA_MIN_X</c> and <c>VAR_CAMERA_MAX_X</c>, are given to each call.
/// </summary>
public sealed class Camera
{
    /// <summary>The view's width in strips (<c>_gdi->_numStrips</c>).</summary>
    public const int Strips = 40;

    public const int ScreenWidth = Strips * 8;

    /// <summary>The strips from the view's left edge the followed actor may stray to before the camera goes after it (<c>_leftTrigger</c>, <c>_rightTrigger</c>).</summary>
    public const int LeftTrigger = 10, RightTrigger = 30;

    public int X { get; set; } = ScreenWidth / 2;

    public int DestX { get; set; } = ScreenWidth / 2;

    /// <summary>Where the camera was at the end of the previous step (<c>camera._last</c>).</summary>
    public int LastX { get; set; } = ScreenWidth / 2;

    public CameraMode Mode { get; set; } = CameraMode.Normal;

    /// <summary>The actor the camera follows, or 0.</summary>
    public int Follows { get; set; }

    public bool MovingToActor { get; set; }

    /// <summary>The room's width in pixels.</summary>
    public int RoomWidth { get; set; } = ScreenWidth;

    /// <summary>The first strip of the room in view (<c>_screenStartStrip</c>).</summary>
    public int StartStrip { get; private set; }

    /// <summary>The last strip of the room in view (<c>_screenEndStrip</c>).</summary>
    public int EndStrip => StartStrip + Strips - 1;

    /// <summary>The room's x coordinate at the view's left edge (<c>_virtscr[kMainVirtScreen].xstart</c>).</summary>
    public int Left => StartStrip * 8;

    /// <summary>Puts the camera in a new room: centred on its left screen. Mirrors the camera part of <c>startScene</c>.</summary>
    public void Reset(int roomWidth)
    {
        RoomWidth = roomWidth;
        Mode = CameraMode.Normal;
        X = DestX = ScreenWidth / 2;
        Moved();
    }

    /// <summary>Mirrors <c>setCameraAtEx</c>.</summary>
    public void SetAtEx(int x, int minX, int maxX)
    {
        Mode = CameraMode.Normal;
        X = x;
        SetAt(x, minX, maxX);
        MovingToActor = false;
    }

    /// <summary>Mirrors <c>setCameraAt</c>; running the scroll script is the caller's.</summary>
    public void SetAt(int x, int minX, int maxX)
    {
        if (Mode != CameraMode.FollowActor || Math.Abs(x - X) > ScreenWidth / 2)
        {
            X = x;
        }

        DestX = x;
        if (X < minX)
        {
            X = minX;
        }

        if (X > maxX)
        {
            X = maxX;
        }
    }

    /// <summary>Mirrors <c>panCameraTo</c>.</summary>
    public void PanTo(int x)
    {
        DestX = x;
        Mode = CameraMode.Panning;
        MovingToActor = false;
    }

    /// <summary>
    /// Whether an actor at <paramref name="actorX"/> is outside the strips the camera lets it
    /// wander in, so that the camera has to move to it. Mirrors the trigger test of
    /// <c>setCameraFollows</c> and <c>moveCamera</c>.
    /// </summary>
    public bool OutsideTriggers(int actorX)
    {
        int t = actorX / 8 - StartStrip;
        return t < LeftTrigger || t > RightTrigger;
    }

    /// <summary>
    /// Moves the camera a step: back inside its limits, after the followed actor, or towards
    /// where it is heading, 8 pixels at a time unless it snaps. Returns whether it moved, for
    /// running the scroll script. Mirrors <c>moveCamera</c>.
    /// </summary>
    public bool Move(int minX, int maxX, bool snap, Func<int>? followedX)
    {
        int old = X;
        X &= 0xFFF8;

        if (X < minX)
        {
            X = snap ? minX : X + 8;
            Moved();
            return X != old;
        }

        if (X > maxX)
        {
            X = snap ? maxX : X - 8;
            Moved();
            return X != old;
        }

        if (Mode == CameraMode.FollowActor && followedX is not null)
        {
            int actorX = followedX();
            int t = actorX / 8 - StartStrip;
            if (t < LeftTrigger || t > RightTrigger)
            {
                if (snap)
                {
                    if (t > 40 - 5)
                    {
                        DestX = actorX + 80;
                    }

                    if (t < 5)
                    {
                        DestX = actorX - 80;
                    }
                }
                else
                {
                    MovingToActor = true;
                }
            }
        }

        if (MovingToActor && followedX is not null)
        {
            DestX = followedX();
        }

        if (DestX < minX)
        {
            DestX = minX;
        }

        if (DestX > maxX)
        {
            DestX = maxX;
        }

        if (snap)
        {
            X = DestX;
        }
        else
        {
            if (X < DestX)
            {
                X += 8;
            }

            if (X > DestX)
            {
                X -= 8;
            }
        }

        if (MovingToActor && followedX is not null && X / 8 == followedX() / 8)
        {
            MovingToActor = false;
        }

        Moved();
        return X != old;
    }

    /// <summary>Keeps the camera inside the room and works out the strips in view. Mirrors <c>cameraMoved</c>.</summary>
    public void Moved()
    {
        if (X < ScreenWidth / 2)
        {
            X = ScreenWidth / 2;
        }
        else if (X > RoomWidth - ScreenWidth / 2)
        {
            X = RoomWidth - ScreenWidth / 2;
        }

        StartStrip = X / 8 - Strips / 2;
    }
}
