using System.Text;
using Distaff.Scumm.Costumes;
using Distaff.Scumm.Detection;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Rooms;
using Distaff.Scumm.Scripts;
using Distaff.Scumm.Sound;
using Distaff.Scumm.View;

namespace Distaff.Scumm.Game;

/// <summary>
/// The game as it runs: the scripts, the room on screen and its objects, the actors, the camera,
/// verbs, strings and messages. Mirrors ScummVM's <c>ScummEngine</c> loop (<c>scummLoop</c>,
/// <c>startScene</c>) for the EGA release, and carries out the instructions the script engine
/// hands over.
/// </summary>
public sealed partial class GameWorld : IScriptHost, IActorWorld
{
    /// <summary>Mirrors <c>_numActors</c> for version 3; actor 0 is not used.</summary>
    public const int ActorCount = 13;

    /// <summary>Mirrors <c>_numInventory</c> of <c>ScummEngine_v4::readMAXS</c>.</summary>
    public const int InventorySize = 80;

    /// <summary>Mirrors <c>_numVerbs</c>.</summary>
    public const int VerbCount = 100;

    /// <summary>Mirrors <c>_numArray</c>: the string resources.</summary>
    public const int StringCount = 50;

    /// <summary>The screen's width; its height is the release's (<see cref="ScreenHeight"/>).</summary>
    public const int ScreenWidth = 320;

    /// <summary>The bits of <c>VAR_CURRENT_LIGHTS</c>: the room lit, the flashlight on, actors in their colours (<c>LIGHTMODE_</c>).</summary>
    public const int LightsRoomOn = 2, LightsFlashlightOn = 4, LightsActorColors = 8;

    /// <summary>The owner meaning "in its room" (<c>OF_OWNER_ROOM</c>).</summary>
    public const int OwnerRoom = 0x0F;

    /// <summary>The DOS timer's quarter-frame rate for versions 2 to 4: the PIT's 1193182 Hz over 5041 (<c>setTimerAndShakeFrequency</c>).</summary>
    public const double TimerFrequency = 1193182.0 / 5041.0;

    private readonly ResourceLoader _loader;
    private readonly byte[] _owners;
    private readonly byte[] _states;
    private readonly int[] _classes;
    private readonly Dictionary<int, (int X, int Y)> _moved = [];

    // objects drawObject drew over the room since their strips were drawn again, in order
    private readonly List<int> _drawnObjects = [];
    private readonly Dictionary<int, byte[]> _newNames = [];
    private readonly byte[]?[] _strings = new byte[]?[StringCount];
    private readonly int[] _inventory = new int[InventorySize];
    private readonly Dictionary<int, ReadOnlyMemory<byte>> _inventoryCode = [];
    private readonly byte[] _resourceMapper = new byte[128];
    private readonly List<string> _unhandled = [];
    private readonly Dictionary<int, Costumes.Costume?> _costumes = [];
    private int[] _boxFlags = [];
    private Charset? _font;

    // the flashlight's size in strips (ScummEngine_v5's _flashlight.xStrips and yStrips)
    private (int X, int Y) _flashlightStrips = (7, 7);

    /// <summary>The rate sound is made at unless a host asks for another.</summary>
    public const int DefaultSampleRate = 44100;

    private readonly Dictionary<int, byte[]?> _sounds = [];

    /// <param name="loader">The game's resources.</param>
    /// <param name="seed">The random number generator's seed.</param>
    /// <param name="sound">The device the sounds play on.</param>
    /// <param name="sampleRate">The rate sound is made at.</param>
    /// <param name="cdTracks">Opens a track of the VGA CD's audio from the game's folder; without it the CD is silent.</param>
    public GameWorld(ResourceLoader loader, int seed = 1, SoundDevice sound = SoundDevice.Speaker, int sampleRate = DefaultSampleRate, Func<int, ICdTrack?>? cdTracks = null)
    {
        _loader = loader;
        Screens = loader.FmTowns ? new VirtScreens(TownsScreenHeight, fmTowns: true) : new VirtScreens();
        SaveTarget = ScummDetector.Identify(loader.Game)?.PreferredTarget ?? "loom";
        var index = loader.Index;
        _owners = [.. index.ObjectOwners];
        _states = [.. index.ObjectStates];
        _classes = [.. index.ObjectClasses];
        Engine = new ScriptEngine(this, seed, loader.Version, loader.FmTowns);
        Sound = new SoundSystem(sound, LoadSound, sampleRate, cdTracks);
        Actors = [.. Enumerable.Range(0, ActorCount).Select(i => new Actor(i, this))];
        Verbs = [.. Enumerable.Range(0, VerbCount).Select(_ => new VerbSlot())];
        // version 4 has no charset 0; its scripts pick one before any text (initCharset)
        _font = loader.Version == 3 && loader.LoadCharset(0) is { } font ? Charset.Parse(font) : null;
        for (int i = 0; i < RoomPalette.Length; i++)
        {
            RoomPalette[i] = (byte)i;
            ShadowPalette[i] = (byte)i;
        }

        foreach (var actor in Actors)
        {
            actor.Init(-1);
        }

        ResetVariables();
    }

    public ScriptEngine Engine { get; }

    public Actor[] Actors { get; }

    public VerbSlot[] Verbs { get; }

    public Camera Camera { get; } = new();

    /// <summary>The room on screen, or null for room 0, no room.</summary>
    public Room? CurrentRoom { get; private set; }

    /// <summary>The room number on screen (<c>_currentRoom</c>); the resource can differ for pseudo-rooms.</summary>
    public int CurrentRoomNumber { get; private set; }

    /// <summary>The screen's bands: text, the room, verbs.</summary>
    public VirtScreens Screens { get; }

    /// <summary>Where the room's view starts on the screen and how tall it is (<c>initScreens</c>): the text above it, the verbs below.</summary>
    public int ViewTop => Screens.Main.Top;

    public int ViewHeight => Screens.Main.Height;

    /// <summary>What the room's colours are drawn as (<c>_roomPalette</c>), which <c>roomOps</c> changes.</summary>
    public byte[] RoomPalette { get; } = new byte[256];

    /// <summary>What each colour on screen shows as (<c>_shadowPalette</c>, which <c>updatePalette</c> applies in version 3).</summary>
    public byte[] ShadowPalette { get; } = new byte[256];

    /// <summary>The cursor: whether it shows (<c>_cursor.state</c>) and whether the player can act (<c>_userPut</c>).</summary>
    public int CursorState { get; private set; }

    public int UserPut { get; private set; }

    /// <summary>The steps run so far.</summary>
    public int Steps { get; private set; }

    /// <summary>The game time the steps so far have taken.</summary>
    public TimeSpan Time { get; private set; }

    /// <summary>Whether a script asked to restart or quit (<c>o5_systemOps</c>).</summary>
    public bool QuitRequested { get; private set; }

    /// <summary>The ticks the next step waits (<c>VAR_TIMER_NEXT</c>, at least 1).</summary>
    public int StepTicks => Math.Max(1, Engine.Variables[Vars.TimerNext]);

    /// <summary>The time the next step takes: its ticks of 4 quarter-frames each (<c>waitForTimer(delta * 4)</c>).</summary>
    public TimeSpan StepLength => TimeSpan.FromSeconds(StepTicks * 4 / TimerFrequency);

    /// <summary>The world instructions with no subsystem yet, as the scripts ran them.</summary>
    public IReadOnlyList<string> Unhandled => _unhandled;

    /// <summary>The sounds the scripts started, in order, for the sound system to come.</summary>
    public List<int> SoundsStarted { get; } = [];

    /// <summary>The sounds the scripts start, stop and ask after, and the player that plays them.</summary>
    public SoundSystem Sound { get; }

    /// <summary>Whether the game's copy protection is kept. Set before <see cref="Boot"/>.</summary>
    public bool CopyProtection
    {
        get => Engine.CopyProtection;
        set => Engine.CopyProtection = value;
    }

    /// <summary>Starts the game: runs script 1 with the boot parameter. Mirrors <c>runBootscript</c>.</summary>
    public void Boot(int bootParam = 0) => Engine.RunScript(1, args: [bootParam]);

    /// <summary>
    /// One step of the game. Mirrors <c>scummLoop</c> for version 3: the timers, the script delays
    /// and the message count down by the step's ticks; the message moves on; the engine variables
    /// are brought up to date; every script runs; the click or key goes to the verb script; the
    /// sentence script runs; the camera moves. While the VGA CD's menu or a banner is up, the game
    /// waits and the step is theirs.
    /// </summary>
    public void Step()
    {
        if (InInterface)
        {
            InterfaceStep();
            Steps++;
            return;
        }

        int delta = StepTicks;
        Time += StepLength;
        Engine.Variables[Vars.Timer] = delta;
        Engine.Variables[Vars.TimerTotal] += delta;
        Engine.Variables[Vars.Timer1] += delta;
        Engine.Variables[Vars.Timer2] += delta;
        Engine.Variables[Vars.Timer3] += delta;

        delta = Math.Min(delta, 15);
        Engine.DecreaseScriptDelay(delta);
        _talkDelay = Math.Max(0, _talkDelay - delta);
        DisplayDialog();

        // scummLoop_updateScummVars
        Engine.Variables[Vars.CameraPosX] = Camera.X;
        Engine.Variables[Vars.HaveMessage] = _haveMsg;
        UpdateMouseVariables();
        HandleSaveLoad();

        // updateMusicTimer: the CD's timer for the VGA CD (GF_AUDIOTRACKS); the player's otherwise, in quarter-frames of 240 a second
        AdvanceCdTimer(delta * 4);
        Engine.Variables[Vars.MusicTimer] = _loader.Version == 4 ? _cdMusicTimer : (int)(Sound.Player.MusicTimer * TimerFrequency / 240.0);

        Engine.RunAllScripts();
        CheckExecVerbs();
        Engine.CheckAndRunSentenceScript();

        if (CurrentRoomNumber != 0)
        {
            foreach (var actor in Actors.Skip(1).Where(a => a.IsInCurrentRoom))
            {
                actor.Walk();
            }

            Camera.Move(Engine.Variables[Vars.CameraMinX], Engine.Variables[Vars.CameraMaxX],
                Engine.Variables[Vars.CameraFastX] != 0, Camera.Mode == CameraMode.FollowActor ? () => Actors[Camera.Follows].X : null);
        }

        if (CurrentRoomNumber != 0)
        {
            RedrawForCamera();

            // processActors draws each actor and then moves its animation on; what was drawn over
            // the room where it stands goes (resetActorBgs)
            foreach (var actor in SortedActors())
            {
                if (actor.Visible && LoadCostume(actor.Costume) is { } costume)
                {
                    var bounds = CostumeRenderer.Bounds(costume, actor.Cost, Actor.OldDirection(actor.Facing), actor.X, actor.Y - actor.Elevation);
                    if (!bounds.IsEmpty)
                    {
                        int left = bounds.Left & ~7;
                        Screens.RestoreMain(Rect.FromBounds(left, bounds.Top, (bounds.Right + 7) & ~7, bounds.Bottom));
                    }
                }

                actor.AnimateCostume();
            }
        }

        Camera.LastX = Camera.X;
        PlayActorSounds();

        // scummLoop_handleEffects: colour cycling, from version 4 on
        if (_loader.Version == 4)
        {
            CyclePalette();
        }

        // scummLoop_handleSound: the sounds asked for go to the player
        Sound.ProcessQueue();
        Sound.Update(Time);
        AnimateCursor();

        // drawDirtyScreenParts: what was drawn in the text and verb bands shows in the FM Towns' text layer
        Screens.FlushTownsLayer();
        Steps++;
    }

    /// <summary>
    /// The screen as the game shows it: the room's view, with the objects the states show and the
    /// actors, in the view's place, what was drawn over it, the bands above and below, and the text.
    /// Verbs are not drawn yet.
    /// </summary>
    public Surface8 Screen()
        => Screens.Compose(CurrentRoom is { } room ? RoomPicture(room).Pixels : null, Camera.Left, Math.Min(ViewHeight, CurrentRoom?.Height ?? 0));

    /// <summary>
    /// What was drawn over the room goes where the room is drawn again: everything when the camera
    /// jumps, and the columns that scroll out of view when it moves a strip. On the VGA CD, whose
    /// text is drawn over the room, a camera that moves stops the talk first. Mirrors
    /// <c>redrawBGAreas</c> for versions before 7.
    /// </summary>
    private void RedrawForCamera()
    {
        int diff = Camera.X - Camera.LastX;
        if (_loader.Version == 4 && diff != 0 && _hasMask)
        {
            StopTalk();
        }

        if (diff is 8 or -8)
        {
            RedrawRoom(0, Camera.Left);
            RedrawRoom(Camera.Left + Camera.ScreenWidth);
        }
        else if (diff != 0)
        {
            RedrawRoom();
        }
    }

    /// <summary>
    /// The room is drawn again over columns of the main band (<c>redrawBGStrip</c> and
    /// <c>drawRoomObjects</c>): what was drawn over it goes, and objects drawn there since are
    /// back in the room's own order.
    /// </summary>
    private void RedrawRoom(int left = 0, int width = int.MaxValue)
    {
        Screens.RedrawMain(left, width);
        long right = (long)left + width;
        _drawnObjects.RemoveAll(number => CurrentRoom?.FindObject(number) is not { } o
            || (PositionOf(o).X < right && PositionOf(o).X + o.Width > left));
    }

    /// <summary>
    /// The whole room with its objects as their states and moves show them, and its actors, in the
    /// room's colours: the objects from the last to the first, then those <c>drawObject</c> drew
    /// since their strips were last drawn, in that order, over the rest whatever their states
    /// (the draw queue of <c>processDrawQue</c>). With the room's lights off it is black but for
    /// the flashlight, when that is on; the actors still show.
    /// </summary>
    public MaskedImage RoomPicture(Room room)
    {
        var image = RoomRenderer.DrawBackground(room, RoomPalette);
        foreach (var obj in RoomRenderer.VisibleObjects(room, GetState))
        {
            RoomRenderer.DrawObject(room, obj, image, RoomPalette, _moved.TryGetValue(obj.Number, out var at) ? at : null);
        }

        foreach (int number in _drawnObjects)
        {
            if (room.FindObject(number) is { } drawn)
            {
                RoomRenderer.DrawObject(room, drawn, image, RoomPalette, _moved.TryGetValue(number, out var at) ? at : null);
            }
        }

        int lights = Engine.Variables[Vars.CurrentLights];
        if ((lights & LightsRoomOn) == 0)
        {
            var dark = new Surface8(image.Pixels.Width, image.Pixels.Height);
            if ((lights & LightsFlashlightOn) != 0 && FlashlightRect() is { } light)
            {
                Flashlight.Copy(image.Pixels, dark, light.Intersect(dark.Bounds), FmTowns);
            }

            image = image with { Pixels = dark };
        }

        bool actorColors = (Engine.Variables[Vars.CurrentLights] & 8) != 0;
        foreach (var actor in SortedActors())
        {
            if (actor.Visible && LoadCostume(actor.Costume) is { } costume)
            {
                CostumeRenderer.Draw(costume, actor.Cost, Actor.OldDirection(actor.Facing),
                    CostumeRenderer.Palette(costume, actor.Palette, actorColors), image, actor.X, actor.Y - actor.Elevation, zPlane: ZPlaneOf(actor));
            }
        }

        return image;
    }

    /// <summary>
    /// Where the flashlight shines in the room, or null when it has no size or there is no ego.
    /// Mirrors <c>drawFlashlight</c>: around the ego, on a strip and an even line, Loom version 3's
    /// 4 pixels right and 12 up, kept within the view; the FM Towns' just around the ego.
    /// </summary>
    private Rect? FlashlightRect()
    {
        int ego = Engine.Variables[Vars.Ego];
        if (_flashlightStrips.X == 0 || _flashlightStrips.Y == 0 || ego is <= 0 or >= ActorCount)
        {
            return null;
        }

        int w = _flashlightStrips.X * 8;
        int h = _flashlightStrips.Y * 8;
        int x = Actors[ego].X - w / 2 - Camera.Left;
        int y = Actors[ego].Y - h / 2;
        if (!FmTowns)
        {
            x = (Actors[ego].X & ~7) - w / 2 - Camera.Left + 4;
            y = (Actors[ego].Y & ~1) - h / 2 - 12;
        }

        x = x < 0 ? 0 : Math.Min(x, Camera.ScreenWidth - w);
        y = y < 0 ? 0 : Math.Min(y, ViewHeight - h);
        return new Rect(x + Camera.Left, y, w, h);
    }

    /// <summary>
    /// The colours on the screen. The EGA's are its 16 through the shadow palette; the VGA CD's
    /// are the 256 of <see cref="CurrentPalette"/>, each through the shadow palette (<c>updatePalette</c>
    /// with <c>_shadowPalRemap</c>); the FM Towns' are those of its picture layer, likewise, in
    /// 15-bit colour (<c>_16BitPalette</c>).
    /// </summary>
    public Rgb[] DisplayPalette() => FmTowns
        ? [.. Enumerable.Range(0, 256).Select(i => To15Bit(CurrentPalette[ShadowPalette[i]]))]
        : _loader.Version == 4
        ? [.. Enumerable.Range(0, 256).Select(i => CurrentPalette[ShadowPalette[i]])]
        : [.. Enumerable.Range(0, 16).Select(i => Palettes.Ega[ShadowPalette[i] & 0x0F])];

    /// <summary>The SCUMM version the game is: 3 for the EGA, 4 for the VGA CD.</summary>
    public int Version => _loader.Version;

    /// <summary>Whether the game is the EGA release, its resources bundled the old way (<c>GF_OLD_BUNDLE</c>).</summary>
    public bool OldBundle => _loader.Layout == ResourceLayout.V3Old;

    /// <summary>The VGA CD's 256 colours as the game has set them (<c>_currentPalette</c>): the room's palette, as the scripts change it.</summary>
    public Rgb[] CurrentPalette { get; private set; } = new Rgb[256];

    /// <summary>The actors of the current room with a costume, from the back to the front. Mirrors the sort of <c>processActors</c>.</summary>
    private IEnumerable<Actor> SortedActors()
        => Actors.Skip(1).Where(a => a.IsInCurrentRoom && a.Costume != 0).OrderBy(a => a.Y);

    /// <summary>
    /// Whether the room's mask hides the actor: unless it is forced either way or never clipped,
    /// as its box says. Mirrors the <c>_zbuf</c> of <c>prepareDrawActorCostume</c> for version 3,
    /// which has one mask.
    /// </summary>
    private bool ClipsActor(Actor actor)
    {
        if (actor.ForceClip != 0)
        {
            return true;
        }

        if (GetClass(actor.Number, Actor.ClassNeverClip))
        {
            return false;
        }

        return actor.WalkBox == WalkBoxes.InvalidBox || (CurrentRoom is { } room && actor.WalkBox <= room.WalkBoxes.Count && room.WalkBoxes.Count > 0 && room.WalkBoxes.Box(actor.WalkBox).Mask != 0);
    }

    /// <summary>
    /// The mask plane that hides an actor, 0 for none. The EGA's one plane, when
    /// <see cref="ClipsActor"/> says so. For the VGA CD: the plane it is forced behind, none when it
    /// is never clipped, or its box's mask, at most the room's last plane. Mirrors the <c>_zbuf</c>
    /// of <c>prepareDrawActorCostume</c>.
    /// </summary>
    private int ZPlaneOf(Actor actor)
    {
        if (_loader.Version == 3)
        {
            return ClipsActor(actor) ? 1 : 0;
        }

        if (actor.ForceClip != 0)
        {
            return actor.ForceClip;
        }

        if (GetClass(actor.Number, Actor.ClassNeverClip) || CurrentRoom is not { } room
            || actor.WalkBox == WalkBoxes.InvalidBox || actor.WalkBox > room.WalkBoxes.Count || room.WalkBoxes.Count == 0)
        {
            return 0;
        }

        return Math.Min(room.WalkBoxes.Box(actor.WalkBox).Mask, RoomRenderer.ZBuffers(room) - 1);
    }

    // IActorWorld

    public WalkBoxes? Boxes => CurrentRoom?.WalkBoxes;

    public int BoxFlags(int box)
    {
        if (box < 0 || box == WalkBoxes.InvalidBox || _boxFlags.Length == 0 || box > _boxFlags.Length)
        {
            return 0;
        }

        return _boxFlags[box == _boxFlags.Length ? box - 1 : box];
    }

    public int BoxScale(int box, int x, int y)
    {
        if (_loader.Version == 3 || Boxes is not { Count: > 0 } boxes || box < 0 || box == WalkBoxes.InvalidBox || box > boxes.Count)
        {
            return 0xFF;
        }

        return WalkBoxes.Scale(boxes.Box(box), x, y, slot => CurrentRoom!.ScaleSlots[slot]);
    }

    /// <summary>Changes a box's flags for as long as the room is on screen (<c>setBoxFlags</c>).</summary>
    public void SetBoxFlags(int box, int flags)
    {
        if (box >= 0 && box <= _boxFlags.Length && _boxFlags.Length > 0)
        {
            _boxFlags[box == _boxFlags.Length ? box - 1 : box] = flags;
        }
    }

    public Costumes.Costume? LoadCostume(int number)
    {
        if (number == 0)
        {
            return null;
        }

        if (!_costumes.TryGetValue(number, out var costume))
        {
            // a few index entries are leftovers that point into other resources; they draw nothing
            try
            {
                costume = _loader.Load(ResourceType.Costume, number) is not { } data ? null
                    : _loader.FmTowns ? Costumes.Costume.ParseTowns(number, data)
                    : _loader.Version == 4 ? Costumes.Costume.ParseV4(number, data) : Costumes.Costume.Parse(number, data);
            }
            catch (InvalidDataException)
            {
                costume = null;
            }

            _costumes[number] = costume;
        }

        return costume;
    }

    /// <summary>
    /// Changes the room. Mirrors <c>startScene</c> for version 3: the message stops, the exit
    /// scripts run, the room's scripts and its objects' new names go, every actor hides, the
    /// colours reset, the new room loads, the camera centres on its left, its actors show, and
    /// the entry scripts run.
    /// </summary>
    public void StartScene(int room)
    {
        StopTalk();

        // fadeOut: the FM Towns' version 3 clears the text over the room band
        if (FmTowns)
        {
            Screens.TextSurface.FillRect(new Rect(0, ViewTop, ScreenWidth, ViewHeight), 0);
        }

        Engine.LeaveRoomScript();

        RunExitScript();
        Engine.KillRoomScripts();
        foreach (var obj in _newNames.Keys.Where(o => GetOwner(o) is 0 or OwnerRoom).ToList())
        {
            _newNames.Remove(obj);
        }

        foreach (var actor in Actors.Skip(1))
        {
            actor.Hide();
        }

        for (int i = 0; i < RoomPalette.Length; i++)
        {
            RoomPalette[i] = (byte)i;
            ShadowPalette[i] = (byte)i;
        }

        Engine.Variables[Vars.Room] = room;
        CurrentRoomNumber = room;
        Engine.CurrentRoom = room;
        int resource = room >= 0x80 ? _resourceMapper[room & 0x7F] : room;
        Engine.Variables[Vars.RoomResource] = resource;
        _moved.Clear();

        if (room == 0)
        {
            CurrentRoom = null;
            _boxFlags = [];
            return;
        }

        CurrentRoom = _loader.LoadRoom(resource) ?? throw new InvalidOperationException($"Room {resource} does not exist.");
        Screens.NewRoom(CurrentRoom.Width);
        _drawnObjects.Clear();
        _boxFlags = [.. CurrentRoom.WalkBoxes.Boxes.Select(b => (int)b.Flags)];

        // a version 4 room sets the palette from its own (setCurrentPalette in resetRoomSubBlocks)
        if (CurrentRoom.BlockOffsets.ContainsKey("PA"))
        {
            CurrentPalette = RoomRenderer.PaletteOf(CurrentRoom);
        }

        // version 4 rooms' colour cycles (initCycl in resetRoomSubBlocks)
        if (_loader.Version == 4)
        {
            InitCycles(CurrentRoom);
        }
        Engine.Variables[Vars.CameraMinX] = Camera.ScreenWidth / 2;
        Engine.Variables[Vars.CameraMaxX] = CurrentRoom.Width - Camera.ScreenWidth / 2;
        Camera.Reset(CurrentRoom.Width);

        foreach (var actor in Actors.Skip(1))
        {
            if (actor.IsInCurrentRoom)
            {
                actor.Show();
            }
        }

        _egoPositioned = false;
        RunEntryScript();
    }

    /// <summary>Mirrors <c>runExitScript</c>.</summary>
    private void RunExitScript()
    {
        Engine.RunScript(Engine.Variables[Vars.ExitScript]);
        if (CurrentRoom is { } room)
        {
            Engine.RunRoomScript(ScriptEngine.ExitScriptNumber, room.Data, room.ExitScriptOffset);
        }

        Engine.RunScript(Engine.Variables[Vars.ExitScript2]);
    }

    /// <summary>Mirrors <c>runEntryScript</c>.</summary>
    private void RunEntryScript()
    {
        Engine.RunScript(Engine.Variables[Vars.EntryScript]);
        if (CurrentRoom is { } room)
        {
            Engine.RunRoomScript(ScriptEngine.EntryScriptNumber, room.Data, room.EntryScriptOffset);
        }

        Engine.RunScript(Engine.Variables[Vars.EntryScript2]);
    }

    /// <summary>
    /// The values the engine variables start with. Mirrors <c>resetScummVars</c> for Loom DOS: the
    /// EGA's count of sound resources in variable 39, which version 4 keeps for its debug mode
    /// (off), and version 4's fixed disk (variable 51), so its menu asks for no save disk.
    /// </summary>
    private void ResetVariables()
    {
        var v = Engine.Variables;
        v[Vars.SoundCard] = Sound.SoundCardValue;
        v[Vars.VideoMode] = FmTowns ? 42 : 19;
        if (_loader.Version == 3)
        {
            v[Vars.SoundResourceCount] = 80;
        }
        else
        {
            v[FixedDisk] = 1;
        }

        v[Vars.HeapSpace] = 1400;
        // LIGHTMODE_actor_use_base_palette, _room_lights_on and _actor_use_colors (ScummEngine_v5::resetScummVars)
        v[Vars.CurrentLights] = 1 | 2 | 8;
        v[Vars.CharInc] = 4;
        v[Vars.TalkActor] = 0;
    }

    // IScriptHost: where scripts' code is

    public ReadOnlyMemory<byte>? GlobalScript(int number)
        => _loader.Load(ResourceType.Script, number) is { } data ? data : null;

    public (ReadOnlyMemory<byte> Code, int Offset)? LocalScript(int number)
        => CurrentRoom is { } room && room.LocalScriptOffsets.TryGetValue(number, out int offset) ? (room.Data, offset) : null;

    public (ReadOnlyMemory<byte> Code, int Offset, ScriptKind Kind)? ObjectScript(int obj, int verb)
    {
        switch (WhereIsObject(obj))
        {
            case ObjectPlace.Room:
                {
                    var o = CurrentRoom!.FindObject(obj)!;
                    return o.FindVerb((byte)verb) is { } entry and not 0 ? (CurrentRoom.Data, o.CodeOffset + entry, ScriptKind.Room) : null;
                }

            case ObjectPlace.Inventory:
                {
                    var code = _inventoryCode[obj];
                    var copy = RoomObject.Parse(code, 0, 0);
                    return copy.FindVerb((byte)verb) is { } entry and not 0 ? (code, entry, ScriptKind.Inventory) : null;
                }

            default:
                return null;
        }
    }

    // objects

    public int GetState(int obj) => obj > 0 && obj < _states.Length ? _states[obj] : 0;

    public void PutState(int obj, int state) => _states[CheckObject(obj)] = (byte)state;

    public int GetOwner(int obj) => obj > 0 && obj < _owners.Length ? _owners[obj] : 0;

    public void PutOwner(int obj, int owner) => _owners[CheckObject(obj)] = (byte)owner;

    /// <summary>
    /// Mirrors <c>getClass</c> for small-header games: classes 32 to 29 (untouchable, player,
    /// x-flip, y-flip) are kept as 24, 23, 19 and 18.
    /// </summary>
    public bool GetClass(int obj, int cls) => (_classes[CheckObject(obj)] & (1 << (MapClass(cls) - 1))) != 0;

    /// <summary>Mirrors <c>putClass</c>, with the actor flags it changes (<c>Actor::classChanged</c>).</summary>
    public void PutClass(int obj, int cls, bool set)
    {
        cls = MapClass(cls);
        _classes[CheckObject(obj)] = set ? _classes[obj] | (1 << (cls - 1)) : _classes[obj] & ~(1 << (cls - 1));
        if (obj >= 1 && obj < ActorCount)
        {
            if (cls == 21)
            {
                Actors[obj].ForceClip = set ? 1 : 0;
            }

            if (cls == 22)
            {
                Actors[obj].IgnoreBoxes = set;
            }
        }
    }

    private static int MapClass(int cls)
    {
        cls &= 0x7F;
        return cls switch
        {
            32 => 24,
            31 => 23,
            30 => 19,
            29 => 18,
            _ => cls is >= 1 and <= 32 ? cls : throw new InvalidOperationException($"Class {cls} is out of range."),
        };
    }

    public enum ObjectPlace
    {
        NotFound,
        Room,
        Inventory,
    }

    /// <summary>Mirrors <c>whereIsObject</c>.</summary>
    public ObjectPlace WhereIsObject(int obj)
    {
        if (obj < 1 || obj >= _owners.Length)
        {
            return ObjectPlace.NotFound;
        }

        if (_owners[obj] != OwnerRoom)
        {
            return _inventory.Contains(obj) ? ObjectPlace.Inventory : ObjectPlace.NotFound;
        }

        return CurrentRoom?.FindObject(obj) is not null ? ObjectPlace.Room : ObjectPlace.NotFound;
    }

    /// <summary>Where an object of the current room is, in pixels: where the room puts it, or where a script moved it.</summary>
    public (int X, int Y) PositionOf(RoomObject obj) => _moved.TryGetValue(obj.Number, out var at) ? at : (obj.X, obj.Y);

    /// <summary>
    /// The name of an actor, or of an object: the name a script gave it, or its own, carried or in
    /// the room. Mirrors <c>getObjOrActorName</c>.
    /// </summary>
    public byte[]? NameOf(int number)
    {
        if (number > 0 && number < ActorCount)
        {
            return Actors[number].Name;
        }

        if (_newNames.TryGetValue(number, out var name))
        {
            return name;
        }

        return WhereIsObject(number) switch
        {
            ObjectPlace.Room => Encoding.Latin1.GetBytes(CurrentRoom!.FindObject(number)!.Name),
            ObjectPlace.Inventory => Encoding.Latin1.GetBytes(RoomObject.Parse(_inventoryCode[number], 0, 0).Name),
            _ => null,
        };
    }

    private int CheckObject(int obj)
        => obj >= 0 && obj < _owners.Length ? obj : throw new InvalidOperationException($"Object {obj} is out of range (0 to {_owners.Length - 1}).");
}
