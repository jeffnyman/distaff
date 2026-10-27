namespace Distaff.Scumm.Game;

/// <summary>
/// An actor's state as the scripts set and read it. Mirrors the fields of ScummVM's
/// <c>Actor</c> and <c>Actor::initActor</c>; costumes, animation and walking come later, so for
/// now an actor that walks arrives at once.
/// </summary>
public sealed class Actor(int number)
{
    public int Number { get; } = number;

    public int Room { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Elevation { get; set; }

    public int Costume { get; set; }

    public bool Visible { get; set; }

    /// <summary>Which way the actor faces, in degrees: 0 up, 90 right, 180 down, 270 left (<c>_facing</c>).</summary>
    public int Facing { get; set; } = 180;

    public int Width { get; set; } = 24;

    public int TalkColor { get; set; } = 15;

    public int InitFrame { get; set; } = 1;

    public int WalkFrame { get; set; } = 2;

    public int StandFrame { get; set; } = 3;

    public int TalkStartFrame { get; set; } = 4;

    public int TalkStopFrame { get; set; } = 5;

    /// <summary>The last animation started (<c>startAnimActor</c>).</summary>
    public int Frame { get; set; }

    public int WalkBox { get; set; }

    public bool Moving { get; set; }

    public bool IgnoreBoxes { get; set; }

    public int ForceClip { get; set; }

    public int AnimSpeed { get; set; }

    public int StepX { get; set; } = 8;

    public int StepY { get; set; } = 2;

    public int ScaleX { get; set; } = 0xFF;

    public int ScaleY { get; set; } = 0xFF;

    public int ShadowMode { get; set; }

    public int Sound { get; set; }

    /// <summary>The actor's colours: what each costume colour is drawn as (<c>_palette</c>).</summary>
    public byte[] Palette { get; } = new byte[256];

    /// <summary>The name a script gave the actor (<c>rtActorName</c>), or null.</summary>
    public byte[]? Name { get; set; }

    public bool IsInRoom(int room) => Room == room && room != 0;

    /// <summary>
    /// Mirrors <c>Actor::initActor</c>: mode -1 resets everything, 1 also the costume, room and
    /// position, 2 the facing; every mode resets the rest.
    /// </summary>
    public void Init(int mode)
    {
        if (mode == -1)
        {
            Visible = false;
            Frame = 0;
            WalkBox = 0;
            Array.Clear(Palette);
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
        ShadowMode = 0;
        Moving = false;
        StepX = 8;
        StepY = 2;
        AnimSpeed = 0;
        IgnoreBoxes = false;
        ForceClip = 0;
        InitFrame = 1;
        WalkFrame = 2;
        StandFrame = 3;
        TalkStartFrame = 4;
        TalkStopFrame = 5;
    }
}
