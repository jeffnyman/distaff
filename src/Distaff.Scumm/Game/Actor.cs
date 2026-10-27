using Distaff.Scumm.Costumes;
using Distaff.Scumm.Rooms;

namespace Distaff.Scumm.Game;

/// <summary>What an actor needs from the world: the room on screen, its boxes and their flags, costumes, classes.</summary>
public interface IActorWorld
{
    int CurrentRoomNumber { get; }

    /// <summary>The current room's boxes, or null when there is no room.</summary>
    WalkBoxes? Boxes { get; }

    /// <summary>A box's flags as the scripts have left them, or 0 for no box (<c>getBoxFlags</c>).</summary>
    int BoxFlags(int box);

    /// <summary>Whether an object or actor has a class (<c>getClass</c>).</summary>
    bool GetClass(int obj, int cls);

    /// <summary>A costume, or null when there is none.</summary>
    Costume? LoadCostume(int number);
}

/// <summary>
/// An actor: where it is, its costume and animation, and its walking. Mirrors ScummVM's
/// <c>Actor</c> and <c>Actor_v3</c> for version 3: <c>initActor</c>, <c>putActor</c>,
/// <c>showActor</c>, <c>hideActor</c>, <c>setActorCostume</c>, <c>startAnimActor</c>,
/// <c>animateActor</c>, <c>animateCostume</c>, <c>setDirection</c>, <c>turnToDirection</c>,
/// <c>updateActorDirection</c>, <c>remapDirection</c>, <c>adjustXYToBeInBox</c>,
/// <c>adjustActorPos</c>, <c>startWalkActor</c>, <c>walkActor</c>, <c>calcMovementFactor</c>,
/// <c>actorWalkStep</c> and <c>findPathTowardsOld</c>.
/// </summary>
public sealed class Actor(int number, IActorWorld world)
{
    /// <summary>Mirrors <c>MF_NEW_LEG</c>, <c>MF_IN_LEG</c>, <c>MF_TURN</c> and <c>MF_LAST_LEG</c>.</summary>
    public const int NewLeg = 1, InLeg = 2, Turn = 4, LastLeg = 8;

    /// <summary>Mirrors <c>kBoxXFlip</c>, <c>kBoxYFlip</c>, <c>kBoxPlayerOnly</c>, <c>kBoxLocked</c> and <c>kBoxInvisible</c>.</summary>
    public const int BoxXFlip = 0x08, BoxYFlip = 0x10, BoxPlayerOnly = 0x20, BoxLocked = 0x40, BoxInvisible = 0x80;

    /// <summary>The frames scripts ask for by role. Mirrors <c>CHORE_REDIRECT_INIT</c> to <c>CHORE_REDIRECT_STOP_TALK</c>.</summary>
    public const int RedirectInit = 56, RedirectWalk = 57, RedirectStand = 58, RedirectStartTalk = 59, RedirectStopTalk = 60;

    /// <summary>The classes that matter to actors, as scripts number them. Mirrors <c>kObjectClassNeverClip</c>, <c>kObjectClassYFlip</c>, <c>kObjectClassXFlip</c> and <c>kObjectClassPlayer</c>.</summary>
    public const int ClassNeverClip = 20, ClassYFlip = 29, ClassXFlip = 30, ClassPlayer = 31;

    private const int NoPoint = 32000;

    private readonly IActorWorld _world = world;

    // walkdata
    private Point _dest = new(-1, 0);
    private int _destBox;
    private int _destDir;
    private Point _next;
    private Point _point3 = new(NoPoint, 0);
    private int _curBox;
    private int _xFrac, _yFrac, _xAdd, _yAdd, _deltaXFactor, _deltaYFactor;

    // Actor_v3's step along the longer axis and the threshold of the fractions
    private int _stepX = 1;
    private int _stepThreshold;
    private int _targetFacing = 180;
    private int _lastValidX, _lastValidY;
    private bool _costumeNeedsInit;
    private int _animProgress;

    public int Number { get; } = number;

    public int Room { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Elevation { get; set; }

    public int Costume { get; private set; }

    public bool Visible { get; private set; }

    /// <summary>Which way the actor faces, in degrees: 0 up, 90 right, 180 down, 270 left (<c>_facing</c>).</summary>
    public int Facing { get; private set; } = 180;

    public int Width { get; set; } = 24;

    public int TalkColor { get; set; } = 15;

    public int InitFrame { get; set; } = 1;

    public int WalkFrame { get; set; } = 2;

    public int StandFrame { get; set; } = 3;

    public int TalkStartFrame { get; set; } = 4;

    public int TalkStopFrame { get; set; } = 5;

    /// <summary>The frame last started (<c>_frame</c>).</summary>
    public int Frame { get; private set; }

    public int WalkBox { get; private set; }

    /// <summary>The <see cref="NewLeg"/>, <see cref="InLeg"/>, <see cref="Turn"/> and <see cref="LastLeg"/> bits (<c>_moving</c>).</summary>
    public int Moving { get; private set; }

    public bool IgnoreBoxes { get; set; }

    public int ForceClip { get; set; }

    public int AnimSpeed { get; set; }

    /// <summary>How far the actor moves a step across and down (<c>_speedx</c>, <c>_speedy</c>).</summary>
    public int SpeedX { get; private set; } = 8;

    public int SpeedY { get; private set; } = 2;

    public int ScaleX { get; set; } = 0xFF;

    public int ScaleY { get; set; } = 0xFF;

    public int ShadowMode { get; set; }

    public int Sound { get; set; }

    /// <summary>The actor's colours: what each costume colour is drawn as (<c>_palette</c>).</summary>
    public byte[] Palette { get; } = new byte[256];

    /// <summary>The name a script gave the actor (<c>rtActorName</c>), or null.</summary>
    public byte[]? Name { get; set; }

    /// <summary>Where each limb is in the costume (<c>_cost</c>).</summary>
    public CostumeState Cost { get; } = new();

    public bool IsInCurrentRoom => Room == _world.CurrentRoomNumber && Room != 0;

    public bool IsInRoom(int room) => Room == room && room != 0;

    /// <summary>
    /// Mirrors <c>Actor_v3::initActor</c> and <c>Actor::initActor</c>: mode -1 resets everything,
    /// 1 also the costume, room and position, 2 the facing; every mode resets the rest. The
    /// actor's classes are the world's to clear.
    /// </summary>
    public void Init(int mode)
    {
        if (mode == -1)
        {
            _stepX = 1;
            _stepThreshold = 0;
            Visible = false;
            Frame = 0;
            WalkBox = 0;
            _animProgress = 0;
            Array.Clear(Palette);
            Cost.Reset();
            _point3 = new(NoPoint, 0);
        }

        if (mode is 1 or -1)
        {
            Costume = 0;
            Room = 0;
            X = 0;
            Y = 0;
            Facing = 180;
        }
        else if (mode == 2)
        {
            Facing = 180;
        }

        Elevation = 0;
        Width = 24;
        TalkColor = 15;
        ScaleX = ScaleY = 0xFF;
        Sound = 0;
        _targetFacing = Facing;
        _lastValidX = _lastValidY = 0;
        ShadowMode = 0;
        StopMoving();
        SetWalkSpeed(8, 2);
        AnimSpeed = 0;
        IgnoreBoxes = false;
        ForceClip = 0;
        InitFrame = 1;
        WalkFrame = 2;
        StandFrame = 3;
        TalkStartFrame = 4;
        TalkStopFrame = 5;
    }

    /// <summary>Mirrors <c>setActorWalkSpeed</c>.</summary>
    public void SetWalkSpeed(int x, int y)
    {
        if (x == SpeedX && y == SpeedY)
        {
            return;
        }

        SpeedX = x;
        SpeedY = y;
        if (Moving != 0)
        {
            CalcMovementFactor(_next);
        }
    }

    /// <summary>Mirrors <c>setActorCostume</c> for <c>GF_OLD_BUNDLE</c>: the figure starts over, and the colours are the costume's own.</summary>
    public void SetCostume(int costume)
    {
        _costumeNeedsInit = true;
        if (Visible)
        {
            Hide();
            Cost.Reset();
            Costume = costume;
            Show();
        }
        else
        {
            Costume = costume;
            Cost.Reset();
        }

        for (int i = 0; i < 16; i++)
        {
            Palette[i] = (byte)i;
        }
    }

    /// <summary>Mirrors <c>Actor::putActor</c>, its talk stopping left to the world: it shows when it lands in the current room and hides when it leaves.</summary>
    public void Put(int x, int y, int room)
    {
        X = x;
        Y = y;
        Room = room;
        if (Visible)
        {
            if (IsInCurrentRoom)
            {
                if (Moving != 0)
                {
                    StopMoving();
                    StartAnim(StandFrame);
                }

                AdjustPosition();
            }
            else
            {
                Hide();
            }
        }
        else if (IsInCurrentRoom)
        {
            Show();
        }
    }

    /// <summary>Mirrors <c>showActor</c>.</summary>
    public void Show()
    {
        if (_world.CurrentRoomNumber == 0 || Visible)
        {
            return;
        }

        AdjustPosition();
        if (_costumeNeedsInit)
        {
            StartAnim(InitFrame);
            _costumeNeedsInit = false;
        }

        Visible = true;
    }

    /// <summary>Mirrors <c>hideActor</c>.</summary>
    public void Hide()
    {
        if (!Visible)
        {
            return;
        }

        if (Moving != 0)
        {
            StopMoving();
            StartAnim(StandFrame);
        }

        Visible = false;
    }

    /// <summary>Mirrors <c>stopActorMoving</c>.</summary>
    public void StopMoving() => Moving = 0;

    /// <summary>
    /// Starts a frame, by number or by role: the costume's limbs for it, facing the way the actor
    /// faces. The init frame starts the figure over. Mirrors <c>startAnimActor</c>.
    /// </summary>
    public void StartAnim(int frame)
    {
        frame = frame switch
        {
            RedirectInit => InitFrame,
            RedirectWalk => WalkFrame,
            RedirectStand => StandFrame,
            RedirectStartTalk => TalkStartFrame,
            RedirectStopTalk => TalkStopFrame,
            _ => frame,
        };

        if (IsInCurrentRoom && Costume != 0)
        {
            _animProgress = 0;
            Cost.AnimCounter = 0;
            if (frame == InitFrame)
            {
                Cost.Reset();
            }

            if (_world.LoadCostume(Costume) is { } costume)
            {
                Cost.Decode(costume, frame, OldDirection(Facing));
            }

            Frame = frame;
        }
    }

    /// <summary>
    /// Mirrors <c>Actor::animateActor</c> for version 3: the chore in the top bits, the direction in
    /// the bottom two; chore 2 stops walking, 3 faces at once, 4 turns, others start the frame.
    /// </summary>
    public void Animate(int anim)
    {
        int chore = 0x3F - (anim >> 2) + 2;
        int dir = NewDirection(anim & 3);
        switch (chore)
        {
            case 2:
                if (IsInCurrentRoom)
                {
                    StartAnim(StandFrame);
                    StopMoving();
                }

                break;
            case 3:
                if (IsInCurrentRoom)
                {
                    Moving &= ~Turn;
                }

                SetDirection(dir);
                break;
            case 4:
                if (IsInCurrentRoom)
                {
                    TurnToDirection(dir);
                }

                break;
            default:
                StartAnim(anim);
                break;
        }
    }

    /// <summary>One animation step, every <see cref="AnimSpeed"/> + 1 calls. Mirrors <c>animateCostume</c>.</summary>
    public void AnimateCostume()
    {
        if (Costume == 0)
        {
            return;
        }

        if (++_animProgress >= AnimSpeed)
        {
            _animProgress = 0;
            if (_world.LoadCostume(Costume) is { } costume)
            {
                Cost.Increase(costume);
            }
        }
    }

    /// <summary>
    /// Faces a new way at once: every limb showing a frame for another direction starts it again
    /// for this one. Mirrors <c>setDirection</c> for classic costumes in version 3.
    /// </summary>
    public void SetDirection(int direction)
    {
        direction = (direction + 360) % 360;
        if (Facing == direction)
        {
            return;
        }

        Facing = direction;
        if (Costume == 0 || !IsInCurrentRoom || _world.LoadCostume(Costume) is not { } costume)
        {
            return;
        }

        int mask = 0x8000;
        for (int limb = 0; limb < CostumeState.Limbs; limb++, mask >>= 1)
        {
            int animation = Cost.Animation(limb);
            if (animation == CostumeState.None || (animation & 3) == OldDirection(Facing))
            {
                continue;
            }

            Cost.Decode(costume, animation >> 2, OldDirection(Facing), mask);
        }
    }

    /// <summary>Mirrors <c>turnToDirection</c> for version 3: the actor turns over the next steps.</summary>
    public void TurnToDirection(int direction)
    {
        if (direction == -1)
        {
            return;
        }

        _targetFacing = direction;
        Moving = Turn;
    }

    /// <summary>
    /// Fits a point into the boxes the actor may walk in: the point itself when a box holds it,
    /// else the closest point on the nearest box, looking within 30, then 80 pixels, then anywhere.
    /// Returns the point and its box. Mirrors <c>adjustXYToBeInBox</c> for version 3.
    /// </summary>
    public (int X, int Y, int Box) AdjustXYToBeInBox(int x, int y)
    {
        int[] thresholds = [30, 80, 0];
        var result = (X: _lastValidX, Y: _lastValidY, Box: WalkBoxes.InvalidBox);
        if (IgnoreBoxes)
        {
            return (x, y, WalkBoxes.InvalidBox);
        }

        if (_world.Boxes is not { } boxes)
        {
            return result;
        }

        foreach (int threshold in thresholds)
        {
            int last = boxes.Count - 1;
            if (last < 0)
            {
                return result;
            }

            int bestDist = 0xFFFF;
            int bestBox = WalkBoxes.InvalidBox;
            for (int box = last; box >= 0; box--)
            {
                int flags = _world.BoxFlags(box);
                if ((flags & BoxInvisible) != 0 && !((flags & BoxPlayerOnly) != 0 && !_world.GetClass(Number, ClassPlayer)))
                {
                    continue;
                }

                if (threshold > 0 && BoxGeometry.QuickReject(boxes.Box(box), x, y, threshold))
                {
                    continue;
                }

                if (boxes.Contains(box, x, y))
                {
                    _lastValidX = x;
                    _lastValidY = y;
                    return (x, y, box);
                }

                var (dist, closest) = BoxGeometry.ClosestPointOnBox(boxes.Box(box), x, y);
                if (dist < bestDist)
                {
                    _lastValidX = closest.X;
                    _lastValidY = closest.Y;
                    result = (closest.X, closest.Y, result.Box);
                    if (dist == 0)
                    {
                        return (closest.X, closest.Y, box);
                    }

                    bestDist = dist;
                    bestBox = box;
                }
            }

            if (threshold == 0 || threshold * threshold >= bestDist)
            {
                return (result.X, result.Y, bestBox);
            }
        }

        return result;
    }

    /// <summary>Mirrors <c>adjustActorPos</c>: the actor moves into its boxes and stands.</summary>
    public void AdjustPosition()
    {
        var (x, y, box) = AdjustXYToBeInBox(X, Y);
        X = x;
        Y = y;
        _destBox = box;
        WalkBox = box;
        _dest = new(-1, _dest.Y);
        StopMoving();
        Cost.SoundCounter = 0;
        if (WalkBox != WalkBoxes.InvalidBox && (_world.BoxFlags(WalkBox) & 7) != 0)
        {
            TurnToDirection(Facing);
        }
    }

    /// <summary>
    /// Starts walking to a point, to face a way at the end (-1 for any). An actor outside the
    /// current room gets there at once. Mirrors <c>startWalkActor</c> for version 3.
    /// </summary>
    public void StartWalk(int destX, int destY, int dir)
    {
        var (x, y, box) = (destX, destY, WalkBoxes.InvalidBox);
        if (!IsInCurrentRoom)
        {
            X = x;
            Y = y;
            if (dir != -1)
            {
                Facing = dir;
            }

            return;
        }

        if (IgnoreBoxes)
        {
            box = WalkBoxes.InvalidBox;
            WalkBox = WalkBoxes.InvalidBox;
        }
        else
        {
            if (_world.Boxes is { } boxes && boxes.Contains(_destBox, x, y))
            {
                box = _destBox;
            }
            else
            {
                (x, y, box) = AdjustXYToBeInBox(x, y);
            }

            if (Moving != 0 && _destDir == dir && _dest.X == x && _dest.Y == y)
            {
                return;
            }
        }

        if (X == x && Y == y)
        {
            if (dir != Facing)
            {
                TurnToDirection(dir);
            }

            return;
        }

        _dest = new(x, y);
        _destBox = box;
        _destDir = dir;
        _point3 = new(NoPoint, 0);
        _curBox = WalkBox;
        Moving = (Moving & InLeg) | NewLeg;
    }

    /// <summary>
    /// One step of walking: along the leg, through the boxes towards the destination box and
    /// into it, then standing and facing as asked. Mirrors <c>Actor_v3::walkActor</c>.
    /// </summary>
    public void Walk()
    {
        if (Moving == 0)
        {
            return;
        }

        if ((Moving & NewLeg) == 0)
        {
            if ((Moving & InLeg) != 0 && WalkStep() != 0)
            {
                return;
            }

            if ((Moving & LastLeg) != 0)
            {
                Moving = 0;
                StartAnim(StandFrame);
                if (_targetFacing != _destDir)
                {
                    TurnToDirection(_destDir);
                }

                return;
            }

            if ((Moving & Turn) != 0)
            {
                int newDir = UpdateDirection(false);
                if (Facing != newDir)
                {
                    SetDirection(newDir);
                }
                else
                {
                    Moving = 0;
                }

                return;
            }

            if (_point3.X != NoPoint)
            {
                if (CalcMovementFactor(_point3) != 0)
                {
                    _point3 = new(NoPoint, 0);
                    return;
                }

                _point3 = new(NoPoint, 0);
            }

            WalkBox = _curBox;
            Moving &= InLeg;
        }

        Moving &= ~NewLeg;
        while (true)
        {
            if (WalkBox == WalkBoxes.InvalidBox)
            {
                WalkBox = _destBox;
                _curBox = _destBox;
                break;
            }

            if (WalkBox == _destBox)
            {
                break;
            }

            int nextBox = _world.Boxes?.NextBox(WalkBox, _destBox) ?? -1;
            if (nextBox < 0)
            {
                Moving |= LastLeg;
                return;
            }

            _curBox = nextBox;
            var (p2, p3) = FindPathTowardsOld(WalkBox, nextBox, _destBox);
            if (p2.X == NoPoint && p3.X == NoPoint)
            {
                break;
            }

            if (p2.X != NoPoint && CalcMovementFactor(p2) != 0)
            {
                _point3 = p3;
                return;
            }

            if (CalcMovementFactor(p3) != 0)
            {
                return;
            }

            WalkBox = _curBox;
        }

        Moving |= LastLeg;
        CalcMovementFactor(_dest);
    }

    /// <summary>
    /// Where to walk to get from one box into the next: across the near gate, and through the far
    /// one; neither when the straight line to the destination already passes through. Mirrors
    /// <c>Actor_v3::findPathTowardsOld</c>.
    /// </summary>
    private (Point P2, Point P3) FindPathTowardsOld(int box1, int box2, int finalBox)
    {
        var boxes = _world.Boxes!;
        var (a0, a1, b0, b1) = BoxGeometry.Gates(boxes.Box(box1), boxes.Box(box2));
        var pos = new Point(X, Y);
        var none = new Point(NoPoint, 0);
        if (box2 == finalBox
            && BoxGeometry.CompareSlope(pos, _dest, a0) != BoxGeometry.CompareSlope(pos, _dest, b0)
            && BoxGeometry.CompareSlope(pos, _dest, a1) != BoxGeometry.CompareSlope(pos, _dest, b1))
        {
            return (none, none);
        }

        var p3 = BoxGeometry.ClosestPointOnLine(a1, b1, pos);
        var p2 = BoxGeometry.CompareSlope(pos, p3, a0) == BoxGeometry.CompareSlope(pos, p3, b0)
            ? BoxGeometry.ClosestPointOnLine(a0, b0, pos)
            : none;
        return (p2, p3);
    }

    /// <summary>
    /// Sets up a leg to a point: the step across (the speed, or one more than the step down when the
    /// leg is steep), the fractions, and the way to face; then takes its first step. Returns 0 when
    /// there is no leg to walk. Mirrors <c>Actor_v3::calcMovementFactor</c> for version 3.
    /// </summary>
    private int CalcMovementFactor(Point next)
    {
        if (X == next.X && Y == next.Y)
        {
            return 0;
        }

        int diffX = next.X - X;
        int diffY = next.Y - Y;
        if ((Moving & LastLeg) == 0 && SpeedX > Math.Abs(diffX) && SpeedY > Math.Abs(diffY))
        {
            return 0;
        }

        _stepX = ((Math.Abs(diffY) / SpeedY) >> 1) > (Math.Abs(diffX) / SpeedX) ? SpeedY + 1 : SpeedX;
        _stepThreshold = Math.Max(Math.Abs(diffY) / SpeedY, Math.Abs(diffX) / _stepX);
        int deltaXFactor = diffX < 0 ? -_stepX : _stepX;
        int deltaYFactor = diffY < 0 ? -SpeedY : SpeedY;
        _xFrac = _xAdd = deltaXFactor != 0 ? diffX / deltaXFactor : 0;
        _yFrac = _yAdd = deltaYFactor != 0 ? diffY / deltaYFactor : 0;
        _next = next;
        _deltaXFactor = deltaXFactor;
        _deltaYFactor = deltaYFactor;

        // the facing ratio of version 3 is 3: a leg faces up or down only when it is steep
        _targetFacing = NewDirection(Math.Abs(diffY) * 3 > Math.Abs(diffX) ? 3 - (diffY >= 0 ? 1 : 0) : (diffX >= 0 ? 1 : 0));
        return WalkStep();
    }

    /// <summary>
    /// One step along a leg: the first turns the actor and starts its walk; the rest move it by the
    /// fractions, snapping to the leg's end when close. Returns 0 at the end of the leg. Mirrors
    /// <c>Actor_v3::actorWalkStep</c>.
    /// </summary>
    private int WalkStep()
    {
        int nextFacing = UpdateDirection(true);
        if ((Moving & InLeg) == 0 || Facing != nextFacing)
        {
            if (WalkFrame != Frame || Facing != nextFacing)
            {
                StartWalkAnim(nextFacing);
            }

            Moving |= InLeg;
            return 1;
        }

        if (_next.X - _stepX <= X && _next.X + _stepX >= X)
        {
            X = _next.X;
        }

        if (_next.Y - SpeedY <= Y && _next.Y + SpeedY >= Y)
        {
            Y = _next.Y;
        }

        if (WalkBox != _curBox && _world.Boxes is { } boxes && boxes.Contains(_curBox, X, Y))
        {
            WalkBox = _curBox;
        }

        if (X == _next.X && Y == _next.Y)
        {
            Moving &= ~InLeg;
            return 0;
        }

        if ((_xFrac += _xAdd) >= _stepThreshold)
        {
            if (X != _next.X)
            {
                X += _deltaXFactor;
            }

            _xFrac -= _stepThreshold;
        }

        if ((_yFrac += _yAdd) >= _stepThreshold)
        {
            if (Y != _next.Y)
            {
                Y += _deltaYFactor;
            }

            _yFrac -= _stepThreshold;
        }

        return 1;
    }

    /// <summary>Mirrors <c>startWalkAnim</c> with command 1: face the way and start walking.</summary>
    private void StartWalkAnim(int direction)
    {
        SetDirection(direction);
        StartAnim(WalkFrame);
    }

    /// <summary>
    /// The way to face next while turning towards the target: straight to it, or a quarter turn at a
    /// time. Mirrors <c>updateActorDirection</c>.
    /// </summary>
    private int UpdateDirection(bool walking)
    {
        ReadOnlySpan<byte> turn = [0, 2, 2, 3, 2, 1, 2, 3, 0, 1, 2, 1, 0, 1, 0, 3];
        int dir = RemapDirection(_targetFacing, walking);
        if ((dir & 0x400) != 0)
        {
            dir = NewDirection(turn[OldDirection(dir & 0x3FF) | (OldDirection(Facing) << 2)]);
        }

        return dir;
    }

    /// <summary>
    /// The way a box lets the actor face: flipped by the box or the actor's class, or fixed by the
    /// box; else the way asked, marked 0x400 to be turned to gradually. Mirrors <c>remapDirection</c>
    /// for version 3.
    /// </summary>
    private int RemapDirection(int dir, bool walking)
    {
        int flags = _world.BoxFlags(WalkBox);
        bool flipX = _deltaXFactor > 0;
        bool flipY = _deltaYFactor > 0;
        if ((flags & BoxXFlip) != 0 || _world.GetClass(Number, ClassXFlip))
        {
            dir = 360 - dir;
            flipX = !flipX;
        }

        if ((flags & BoxYFlip) != 0 || _world.GetClass(Number, ClassYFlip))
        {
            dir = 180 - dir;
            flipY = !flipY;
        }

        switch (flags & 7)
        {
            case 1:
                return walking ? (flipX ? 90 : 270) : (dir == 90 ? 90 : 270);
            case 2:
                return walking ? (flipY ? 180 : 0) : (dir == 0 ? 0 : 180);
            case 3:
                return 270;
            case 4:
                return 90;
            case 5:
                return 0;
            case 6:
                return 180;
        }

        return ((dir + 360) % 360) | 0x400;
    }

    /// <summary>Mirrors <c>newDirToOldDir</c>: 0 left, 1 right, 2 front, 3 back.</summary>
    public static int OldDirection(int dir) => dir switch
    {
        >= 71 and <= 109 => 1,
        >= 109 and <= 251 => 2,
        >= 251 and <= 289 => 0,
        _ => 3,
    };

    /// <summary>Mirrors <c>oldDirToNewDir</c>.</summary>
    public static int NewDirection(int dir) => dir switch { 0 => 270, 1 => 90, 2 => 180, _ => 0 };
}
