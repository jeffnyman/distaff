using System.Text;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Rooms;
using Distaff.Scumm.Scripts;
using Distaff.Scumm.View;

namespace Distaff.Scumm.Game;

/// <summary>
/// The game as it runs: the scripts, the room on screen and its objects, the actors, the camera,
/// verbs, strings and messages. Mirrors ScummVM's <c>ScummEngine</c> loop (<c>scummLoop</c>,
/// <c>startScene</c>) for the EGA release, and carries out the instructions the script engine
/// hands over. Actors are state only so far: they are not drawn and arrive where they walk at once.
/// </summary>
public sealed partial class GameWorld : IScriptHost
{
    /// <summary>Mirrors <c>_numActors</c> for version 3; actor 0 is not used.</summary>
    public const int ActorCount = 13;

    /// <summary>Mirrors <c>_numInventory</c> of <c>ScummEngine_v4::readMAXS</c>.</summary>
    public const int InventorySize = 80;

    /// <summary>Mirrors <c>_numVerbs</c>.</summary>
    public const int VerbCount = 100;

    /// <summary>Mirrors <c>_numArray</c>: the string resources.</summary>
    public const int StringCount = 50;

    /// <summary>The screen's size.</summary>
    public const int ScreenWidth = 320, ScreenHeight = 200;

    /// <summary>The owner meaning "in its room" (<c>OF_OWNER_ROOM</c>).</summary>
    public const int OwnerRoom = 0x0F;

    /// <summary>The DOS timer's quarter-frame rate for versions 2 to 4: the PIT's 1193182 Hz over 5041 (<c>setTimerAndShakeFrequency</c>).</summary>
    public const double TimerFrequency = 1193182.0 / 5041.0;

    private readonly ResourceLoader _loader;
    private readonly byte[] _owners;
    private readonly byte[] _states;
    private readonly int[] _classes;
    private readonly Dictionary<int, (int X, int Y)> _moved = [];
    private readonly Dictionary<int, byte[]> _newNames = [];
    private readonly byte[]?[] _strings = new byte[]?[StringCount];
    private readonly int[] _inventory = new int[InventorySize];
    private readonly Dictionary<int, ReadOnlyMemory<byte>> _inventoryCode = [];
    private readonly byte[] _resourceMapper = new byte[128];
    private readonly List<string> _unhandled = [];

    public GameWorld(ResourceLoader loader, int seed = 1)
    {
        _loader = loader;
        var index = loader.Index;
        _owners = [.. index.ObjectOwners];
        _states = [.. index.ObjectStates];
        _classes = [.. index.ObjectClasses];
        Engine = new ScriptEngine(this, seed);
        Actors = [.. Enumerable.Range(0, ActorCount).Select(i => new Actor(i))];
        Verbs = [.. Enumerable.Range(0, VerbCount).Select(_ => new VerbSlot())];
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

    /// <summary>Where the room's view starts on the screen and how tall it is (<c>initScreens</c>): the text above it, the verbs below.</summary>
    public int ViewTop { get; private set; } = 16;

    public int ViewHeight { get; private set; } = 128;

    /// <summary>What the room's colours are drawn as (<c>_roomPalette</c>), which <c>roomOps</c> changes.</summary>
    public byte[] RoomPalette { get; } = new byte[256];

    /// <summary>What actors' colours are drawn as (<c>_shadowPalette</c>).</summary>
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
    /// sentence script runs; the camera moves.
    /// </summary>
    public void Step()
    {
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

        Engine.RunAllScripts();
        CheckExecVerbs();
        Engine.CheckAndRunSentenceScript();

        if (CurrentRoomNumber != 0)
        {
            Camera.Move(Engine.Variables[Vars.CameraMinX], Engine.Variables[Vars.CameraMaxX],
                Engine.Variables[Vars.CameraFastX] != 0, Camera.Mode == CameraMode.FollowActor ? () => Actors[Camera.Follows].X : null);
        }

        Camera.LastX = Camera.X;
        Steps++;
    }

    /// <summary>
    /// The screen as the game shows it: the room's view, with the objects the states show, in the
    /// view's place. Actors, text and verbs are not drawn yet.
    /// </summary>
    public Surface8 Screen()
    {
        var screen = new Surface8(ScreenWidth, ScreenHeight);
        if (CurrentRoom is not { } room)
        {
            return screen;
        }

        var picture = RoomPicture(room);
        int height = Math.Min(ViewHeight, room.Height);
        screen.Blit(picture.Pixels, new Rect(Camera.Left, 0, Camera.ScreenWidth, height), 0, ViewTop);
        return screen;
    }

    /// <summary>The whole room with its objects as their states and moves show them, in the room's colours.</summary>
    public MaskedImage RoomPicture(Room room)
    {
        var image = RoomRenderer.DrawBackground(room, RoomPalette);
        foreach (var obj in RoomRenderer.VisibleObjects(room, GetState))
        {
            RoomRenderer.DrawObject(room, obj, image, RoomPalette, _moved.TryGetValue(obj.Number, out var at) ? at : null);
        }

        return image;
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
        Engine.LeaveRoomScript();

        RunExitScript();
        Engine.KillRoomScripts();
        foreach (var obj in _newNames.Keys.Where(o => GetOwner(o) is 0 or OwnerRoom).ToList())
        {
            _newNames.Remove(obj);
        }

        foreach (var actor in Actors.Skip(1))
        {
            actor.Visible = false;
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
            return;
        }

        CurrentRoom = _loader.LoadRoom(resource) ?? throw new InvalidOperationException($"Room {resource} does not exist.");
        Engine.Variables[Vars.CameraMinX] = Camera.ScreenWidth / 2;
        Engine.Variables[Vars.CameraMaxX] = CurrentRoom.Width - Camera.ScreenWidth / 2;
        Camera.Reset(CurrentRoom.Width);

        foreach (var actor in Actors.Skip(1))
        {
            if (actor.IsInRoom(room))
            {
                actor.Visible = true;
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

    /// <summary>The values the engine variables start with. Mirrors <c>resetScummVars</c> for Loom DOS EGA with the PC speaker.</summary>
    private void ResetVariables()
    {
        var v = Engine.Variables;
        v[Vars.SoundCard] = 0;
        v[Vars.VideoMode] = 19;
        v[Vars.SoundResourceCount] = 80;
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

/// <summary>A verb on screen, as the scripts set it up. Mirrors ScummVM's <c>VerbSlot</c>.</summary>
public sealed class VerbSlot
{
    public int VerbId { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Color { get; set; } = 2;

    public int HiColor { get; set; }

    public int DimColor { get; set; }

    public int BackColor { get; set; }

    /// <summary>0 for off, 1 for on, 2 for dimmed (<c>curmode</c>).</summary>
    public int Mode { get; set; }

    /// <summary>0 for text, 1 for an image (<c>type</c>).</summary>
    public int Type { get; set; }

    public int Key { get; set; }

    public bool Center { get; set; }

    /// <summary>Non-zero while saved by <c>saveRestoreVerbs</c> (<c>saveid</c>).</summary>
    public int SaveId { get; set; }

    /// <summary>The object whose image the verb shows, and the room it is in.</summary>
    public int ImageObject { get; set; }

    public int ImageRoom { get; set; }

    public byte[]? Name { get; set; }

    public void Reset()
    {
        VerbId = 0;
        X = Y = 0;
        Color = 2;
        HiColor = 0;
        DimColor = 0;
        BackColor = 0;
        Mode = 0;
        Type = 0;
        Key = 0;
        Center = false;
        SaveId = 0;
        ImageObject = ImageRoom = 0;
        Name = null;
    }
}
