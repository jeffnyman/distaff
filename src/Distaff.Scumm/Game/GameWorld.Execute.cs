using Distaff.Scumm.Graphics;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Rooms;
using Distaff.Scumm.Scripts;
using Distaff.Scumm.Sound;
using Distaff.Scumm.View;

namespace Distaff.Scumm.Game;

/// <summary>The instructions the script engine hands over: the world's side of the <c>o5_</c>, <c>o4_</c> and <c>o3_</c> handlers.</summary>
public sealed partial class GameWorld
{
    public int Execute(ScriptEngine engine, Instruction instruction, IReadOnlyList<int> arguments, IReadOnlyList<int[]> subArguments)
    {
        int Arg(int i) => arguments[i];

        switch (instruction.Handler)
        {
            // objects
            case "o5_setState":
                PutState(Arg(0), Arg(1));
                MarkObjectRectAsDirty(Arg(0));
                return 0;
            case "o4_ifState":
                return GetState(Arg(0)) == Arg(1) ? 1 : 0;
            case "o4_ifNotState":
                return GetState(Arg(0)) != Arg(1) ? 1 : 0;
            case "o5_getObjectOwner":
                return GetOwner(Arg(0));
            case "o5_setOwnerOf":
                SetOwnerOf(Arg(0), Arg(1));
                return 0;
            case "o5_setClass":
                foreach (int cls in subArguments[0])
                {
                    if (cls == 0)
                    {
                        // class 0 clears every class; an actor also loses its box and clip flags
                        _classes[CheckObject(Arg(0))] = 0;
                        if (Arg(0) is >= 1 and < ActorCount)
                        {
                            Actors[Arg(0)].IgnoreBoxes = false;
                            Actors[Arg(0)].ForceClip = 0;
                        }
                    }
                    else
                    {
                        PutClass(Arg(0), cls, (cls & 0x80) != 0);
                    }
                }

                return 0;
            case "o5_ifClassOfIs":
                foreach (int cls in subArguments[0])
                {
                    if (GetClass(Arg(0), cls) != ((cls & 0x80) != 0))
                    {
                        return 0;
                    }
                }

                return 1;
            case "o5_drawObject":
                DrawObject(Arg(0), Arg(1), Arg(2));
                return 0;
            case "o5_setObjectName":
                _newNames[Arg(0)] = instruction.Operands[1].Text!;
                return 0;
            case "o5_findObject":
                return FindObject(Arg(0), Arg(1));
            case "o5_getVerbEntrypoint":
                return VerbEntrypoint(Arg(0), Arg(1));
            case "o4_pickupObject":
                PickupObject(Arg(0));
                return 0;
            case "o5_findInventory":
                return FindInventory(Arg(0), Arg(1));
            case "o5_getInventoryCount":
                return _inventory.Count(o => o != 0 && GetOwner(o) == Arg(0));

            // rooms and the camera
            case "o5_loadRoom":
                if (Arg(0) != CurrentRoomNumber)
                {
                    StartScene(Arg(0));
                }

                return 0;
            case "o5_loadRoomWithEgo":
                LoadRoomWithEgo(Arg(0), Arg(1), Arg(2), Arg(3));
                return 0;
            case "o5_pseudoRoom":
                for (int i = 1; i < arguments.Count; i++)
                {
                    if (Arg(i) >= 0x80)
                    {
                        _resourceMapper[Arg(i) & 0x7F] = (byte)Arg(0);
                    }
                }

                return 0;
            case "o5_setCameraAt":
                Camera.SetAtEx(Arg(0), Engine.Variables[Vars.CameraMinX], Engine.Variables[Vars.CameraMaxX]);
                return 0;
            case "o5_panCameraTo":
                Camera.PanTo(Arg(0));
                return 0;
            case "o5_actorFollowCamera":
                FollowActor(Actors[CheckActor(Arg(0))]);
                Camera.MovingToActor = false;
                return 0;
            case "o5_roomOps":
                RoomOps(instruction.SubOps[0], subArguments[0]);
                return 0;
            case "o5_lights":
                if (Arg(2) == 0)
                {
                    Engine.Variables[Vars.CurrentLights] = Arg(0);
                }
                else if (Arg(2) == 1)
                {
                    _flashlightStrips = (Arg(0), Arg(1));
                }

                RedrawRoom();
                return 0;
            case "o4_oldRoomEffect":
                return 0;

            // actors
            case "o5_actorOps":
                ActorOps(Actors[CheckActor(Arg(0))], instruction.SubOps, subArguments);
                return 0;
            case "o5_putActor":
                PutActor(Actors[CheckActor(Arg(0))], Arg(1), Arg(2));
                return 0;
            case "o5_putActorInRoom":
                {
                    var actor = Actors[CheckActor(Arg(0))];
                    if (actor.Visible && CurrentRoomNumber != Arg(1) && TalkingActor == actor.Number)
                    {
                        StopTalk();
                    }

                    actor.Room = Arg(1);
                    if (Arg(1) == 0)
                    {
                        PutActor(actor, 0, 0, 0);
                    }
                }

                return 0;
            case "o5_putActorAtObject":
                {
                    var actor = Actors[CheckActor(Arg(0))];
                    var (x, y) = WhereIsObject(Arg(1)) != ObjectPlace.NotFound && ObjectPosition(Arg(1)) is { } at ? (at.X, at.Y) : (240, 120);
                    PutActor(actor, x, y);
                }

                return 0;
            case "o5_animateActor":
                if (Arg(0) is > 0 and < ActorCount)
                {
                    Actors[Arg(0)].Animate(Arg(1));
                }

                return 0;
            case "o5_faceActor":
                if (Arg(0) is > 0 and < ActorCount)
                {
                    FaceActor(Actors[Arg(0)], Arg(1));
                }

                return 0;
            case "o5_walkActorTo":
                Actors[CheckActor(Arg(0))].StartWalk(Arg(1), Arg(2), -1);
                return 0;
            case "o5_walkActorToObject":
                if (WhereIsObject(Arg(1)) != ObjectPlace.NotFound && ObjectWalkTo(Arg(1)) is { } walkTo)
                {
                    Actors[CheckActor(Arg(0))].StartWalk(walkTo.X, walkTo.Y, walkTo.Direction);
                }

                return 0;
            case "o5_walkActorToActor":
                if (Arg(0) is > 0 and < ActorCount && Arg(1) is > 0 and < ActorCount
                    && Actors[Arg(0)].IsInCurrentRoom && Actors[Arg(1)].IsInCurrentRoom)
                {
                    WalkActorToActor(Actors[Arg(0)], Actors[Arg(1)], Arg(2));
                }

                return 0;
            case "o5_getActorRoom":
                return Arg(0) is > 0 and < ActorCount ? Actors[Arg(0)].Room : 0;
            case "o5_getActorCostume":
                return Actors[CheckActor(Arg(0))].Costume;
            case "o5_getActorElevation":
                return Actors[CheckActor(Arg(0))].Elevation;
            case "o5_getActorFacing":
                return Actors[CheckActor(Arg(0))].Facing;
            case "o5_getActorMoving":
                return Actors[CheckActor(Arg(0))].Moving;
            case "o5_getActorWalkBox":
                return Actors[CheckActor(Arg(0))].WalkBox;
            case "o5_getActorWidth":
                return Actors[CheckActor(Arg(0))].Width;
            case "o5_getActorX":
                return ObjectOrActorPosition(Arg(0))?.X ?? -1;
            case "o5_getActorY":
                return ObjectOrActorPosition(Arg(0))?.Y ?? -1;
            case "o5_isActorInBox":
                {
                    var actor = Actors[CheckActor(Arg(0))];
                    return CurrentRoom is { } room && room.WalkBoxes.Contains(Arg(1), actor.X, actor.Y) ? 1 : 0;
                }

            case "o5_getDist":
                return ObjectDistance(Arg(0), Arg(1));
            case "o3_setBoxFlags":
                SetBoxFlags(Arg(0), Arg(1));
                return 0;
            case "o5_matrixOps":
                // version 4: Loom's scripts only set box flags; box scales and rebuilding the matrix are left out
                if (instruction.SubOps[0].Code != 1)
                {
                    throw new NotSupportedException($"matrixOps sub-operation {instruction.SubOps[0].Code} is not ported; Loom does not use it.");
                }

                SetBoxFlags(subArguments[0][0], subArguments[0][1]);
                return 0;
            case "o3_waitForActor":
                return 0;

            // waiting
            case "o5_wait":
                Wait(engine, instruction.SubOps[0], subArguments[0]);
                return 0;
            case "o3_waitForSentence":
                if (engine.Sentences.Count > 0
                    ? engine.Sentences[^1].FreezeCount == 0 || engine.IsScriptInUse(Engine.Variables[Vars.SentenceScript])
                    : engine.IsScriptInUse(Engine.Variables[Vars.SentenceScript]))
                {
                    engine.Repeat();
                }

                return 0;

            // text
            case "o5_print":
                ActorToPrintFor = Arg(0);
                DecodeParseString(instruction.SubOps, subArguments);
                return 0;
            case "o5_printEgo":
                ActorToPrintFor = Engine.Variables[Vars.Ego];
                DecodeParseString(instruction.SubOps, subArguments);
                return 0;
            case "o5_stringOps":
                return StringOps(instruction.SubOps[0], subArguments[0]);
            case "o5_getStringWidth":
                return _strings[CheckString(Arg(0))] is { } measured ? StringWidth(0, measured) : 0;
            case "o5_drawBox":
                DrawBox(Arg(0), Arg(1), Arg(2), Arg(3), Arg(4));
                return 0;

            // verbs and the cursor
            case "o5_verbOps":
                VerbOps(Arg(0), instruction.SubOps, subArguments);
                return 0;
            case "o5_saveRestoreVerbs":
                SaveRestoreVerbs(instruction.SubOps[0].Code, subArguments[0]);
                return 0;
            case "o5_cursorCommand":
                CursorCommand(instruction.SubOps[0].Code, subArguments[0]);
                return 0;
            case "o5_doSentence":
                return 0;

            // sound, for the sound system to come
            // o5_startSound restarts the music timer; both note the sound (Sound::startSound)
            case "o5_startSound":
                Engine.Variables[Vars.MusicTimer] = 0;
                StartSound(Arg(0));
                return 0;
            case "o5_startMusic":
                StartSound(Arg(0));
                return 0;
            case "o5_stopSound":
                Sound.Stop(Arg(0));
                return 0;
            case "o5_stopMusic":
                Sound.StopAll();
                return 0;
            case "o5_isSoundRunning":
                return Arg(0) != 0 && Sound.IsRunning(Arg(0)) ? 1 : 0;

            // resources are loaded on demand here
            case "o5_resourceRoutines":
                return 0;

            case "o4_saveLoadGame":
                return SaveLoadGame(Arg(0), instruction);
            case "o4_saveLoadVars":
                SaveLoadVars(Arg(0), instruction.SubOps, subArguments);
                return 0;

            case "o5_systemOps":
                QuitRequested = true;
                engine.BreakHere();
                return 0;

            case "o5_debug":
                return 0;

            default:
                _unhandled.Add($"script {engine.CurrentScriptNumber} [{instruction.Offset:X4}] {instruction.Text}");
                return 0;
        }
    }

    /// <summary>Mirrors <c>Sound::startSound</c>: the sound is the last asked for, and waits in the queue until the step's end.</summary>
    private void StartSound(int sound)
    {
        SoundsStarted.Add(sound);
        Engine.Variables[Vars.LastSound] = sound;
        Sound.Start(sound);
    }

    /// <summary>
    /// The first actor in the room whose costume reached a sound cue starts its sound, and every
    /// actor's cue count goes. Mirrors <c>playActorSounds</c>.
    /// </summary>
    private void PlayActorSounds()
    {
        foreach (var actor in Actors.Skip(1))
        {
            if (actor.Cost.SoundCounter != 0 && actor.IsInCurrentRoom)
            {
                Engine.BreakHere();
                StartSound(actor.Sound);
                foreach (var other in Actors.Skip(1))
                {
                    other.Cost.SoundCounter = 0;
                }

                return;
            }
        }
    }

    /// <summary>
    /// A sound's data as the device plays it: the speaker and PCjr block, or the AdLib block after
    /// its 4-byte header (<c>readSoundResourceSmallHeader</c>). Some index entries point past their
    /// room file, and the Roland Upgrade's sounds are single blocks; these play nothing.
    /// </summary>
    private byte[]? LoadSound(int number)
    {
        if (!_sounds.TryGetValue(number, out var data))
        {
            try
            {
                data = _loader.Index.Locate(ResourceType.Sound, number).IsPresent && _loader.LoadSound(number) is { } resource
                    ? Sound.Device == SoundDevice.AdLib ? resource.AdLib[4..].ToArray() : resource.Speaker.ToArray()
                    : null;
            }
            catch (InvalidDataException)
            {
                data = null;
            }

            _sounds[number] = data;
        }

        return data;
    }

    /// <summary>Mirrors <c>setOwnerOf</c>: an owner of 0 also takes the object out of the inventory.</summary>
    private void SetOwnerOf(int obj, int owner)
    {
        if (owner == 0)
        {
            ClearOwnerOf(obj);
        }

        PutOwner(obj, owner);
        RunInventoryScript(0);
    }

    /// <summary>Mirrors <c>clearOwnerOf</c>: the object's scripts stop and it leaves the inventory, which closes up.</summary>
    private void ClearOwnerOf(int obj)
    {
        Engine.StopObjectScript(obj);
        if (GetOwner(obj) == OwnerRoom)
        {
            return;
        }

        int index = Array.IndexOf(_inventory, obj);
        if (index < 0)
        {
            return;
        }

        _inventory[index] = 0;
        _inventoryCode.Remove(obj);
        for (int i = 0; i < InventorySize - 1; i++)
        {
            if (_inventory[i] == 0 && _inventory[i + 1] != 0)
            {
                _inventory[i] = _inventory[i + 1];
                _inventory[i + 1] = 0;
            }
        }
    }

    /// <summary>Mirrors <c>o4_pickupObject</c>.</summary>
    private void PickupObject(int obj)
    {
        if (CurrentRoom?.FindObject(obj) is not { } o || WhereIsObject(obj) == ObjectPlace.Inventory)
        {
            return;
        }

        int slot = Array.IndexOf(_inventory, 0);
        if (slot < 0)
        {
            throw new InvalidOperationException("The inventory is full.");
        }

        _inventory[slot] = obj;
        _inventoryCode[obj] = o.Code.ToArray();
        MarkObjectRectAsDirty(obj);
        PutOwner(obj, Engine.Variables[Vars.Ego]);
        PutClass(obj, 32, true);
        PutState(obj, 1);
        RunInventoryScript(1);
    }

    /// <summary>Mirrors <c>runInventoryScript</c>.</summary>
    private void RunInventoryScript(int arg)
    {
        if (Engine.Variables[Vars.InventoryScript] != 0)
        {
            Engine.RunScript(Engine.Variables[Vars.InventoryScript], args: [arg]);
        }
    }

    /// <summary>Mirrors <c>findInventory</c>: the owner's <paramref name="index"/>th object, from 1.</summary>
    private int FindInventory(int owner, int index)
    {
        int count = 1;
        foreach (int obj in _inventory)
        {
            if (obj != 0 && GetOwner(obj) == owner && count++ == index)
            {
                return obj;
            }
        }

        return 0;
    }

    /// <summary>
    /// Mirrors <c>o5_drawObject</c> for small-header games: the object moves when a position other
    /// than 255 is given, every object of the same place and size is switched off, and it is on.
    /// </summary>
    private void DrawObject(int obj, int x, int y)
    {
        if (CurrentRoom?.FindObject(obj) is not { } o)
        {
            return;
        }

        if (x != 0xFF)
        {
            _moved[obj] = (x * 8, y * 8);
        }

        var (px, py) = PositionOf(o);
        foreach (var other in CurrentRoom.Objects)
        {
            var (ox, oy) = PositionOf(other);
            if (ox == px && oy == py && other.Width == o.Width && other.Height == o.Height)
            {
                PutState(other.Number, 0);
            }
        }

        PutState(obj, 1);

        // the object is drawn over whatever was drawn there (drawObject from the draw queue)
        var (x0, y0) = PositionOf(o);
        Screens.RestoreMain(new Rect(x0, y0, o.Width, o.Height));
        _drawnObjects.Remove(obj);
        _drawnObjects.Add(obj);
    }

    /// <summary>
    /// The room is drawn again in the strips of an object of the room on screen. Mirrors
    /// <c>markObjectRectAsDirty</c>.
    /// </summary>
    private void MarkObjectRectAsDirty(int obj)
    {
        if (CurrentRoom?.FindObject(obj) is not { Width: not 0 } o)
        {
            return;
        }

        int left = Math.Max(Camera.StartStrip, PositionOf(o).X / 8);
        int right = Math.Min(Camera.EndStrip + 1, PositionOf(o).X / 8 + o.Width / 8);
        if (right > left)
        {
            RedrawRoom(left * 8, (right - left) * 8);
        }
    }

    /// <summary>
    /// Fills a rectangle of the band its top is in, corners included, clipped to the band. Mirrors
    /// <c>drawBox</c> for version 3, which draws over what the band shows.
    /// </summary>
    private void DrawBox(int x, int y, int x2, int y2, int color)
    {
        if (Screens.Find(y) is not { Front: { } front } vs)
        {
            return;
        }

        if (x > x2)
        {
            (x, x2) = (x2, x);
        }

        if (y > y2)
        {
            (y, y2) = (y2, y);
        }

        x2++;
        y2++;
        y -= vs.Top;
        y2 -= vs.Top;
        int width = vs.Number == VirtScreenNumber.Main ? Camera.ScreenWidth : front.Width;
        if (x >= width || x2 < 0 || y > vs.Height || y2 < 0)
        {
            return;
        }

        x = Math.Max(x, 0);
        x2 = Math.Min(x2, width);
        y = Math.Max(y, 0);
        y2 = Math.Min(y2, vs.Height);
        int xstart = vs.Number == VirtScreenNumber.Main ? Camera.Left : 0;
        front.FillRect(Rect.FromBounds(x + xstart, y, x2 + xstart, y2), (byte)color);
    }

    /// <summary>
    /// The topmost touchable object at a point of the room whose parents show it. Mirrors
    /// <c>findObject</c> for version 3.
    /// </summary>
    private int FindObject(int x, int y)
    {
        if (CurrentRoom is not { } room)
        {
            return 0;
        }

        foreach (var obj in room.Objects)
        {
            if (obj.Number < 1 || GetClass(obj.Number, 32) || !RoomRenderer.ParentsAllow(room.Objects, obj, GetState))
            {
                continue;
            }

            var (ox, oy) = PositionOf(obj);
            if (ox <= x && obj.Width + ox > x && oy <= y && obj.Height + oy > y)
            {
                return obj.Number;
            }
        }

        return 0;
    }

    /// <summary>Mirrors <c>getVerbEntrypoint</c>: the offset of an object's script for a verb, or 0.</summary>
    private int VerbEntrypoint(int obj, int verb) => WhereIsObject(obj) switch
    {
        ObjectPlace.Room => CurrentRoom!.FindObject(obj)!.FindVerb((byte)verb) ?? 0,
        ObjectPlace.Inventory => RoomObject.Parse(_inventoryCode[obj], 0, 0).FindVerb((byte)verb) ?? 0,
        _ => 0,
    };

    /// <summary>Where an actor stands to use an object of the room, and which way it faces. Mirrors <c>getObjectXYPos</c> for version 3.</summary>
    public (int X, int Y, int Direction)? ObjectWalkTo(int obj)
        => CurrentRoom?.FindObject(obj) is { } o && ObjectPosition(obj) is { } at ? (at.X, at.Y, Actor.NewDirection(o.ActorDir & 3)) : null;

    /// <summary>Where an actor stands to use an object of the room.</summary>
    public (int X, int Y)? ObjectPosition(int obj)
    {
        if (CurrentRoom?.FindObject(obj) is not { } o)
        {
            return null;
        }

        var (x, y) = PositionOf(o);
        return (o.WalkX + x - o.X, o.WalkY + y - o.Y);
    }

    /// <summary>Where an actor or object is: an actor's position, a carried object's owner's, or an object's walk-to point. Mirrors <c>getObjectOrActorXY</c>.</summary>
    public (int X, int Y)? ObjectOrActorPosition(int obj)
    {
        if (obj is > 0 and < ActorCount)
        {
            return (Actors[obj].X, Actors[obj].Y);
        }

        return WhereIsObject(obj) switch
        {
            ObjectPlace.Inventory when GetOwner(obj) is > 0 and < ActorCount => (Actors[GetOwner(obj)].X, Actors[GetOwner(obj)].Y),
            ObjectPlace.Room => ObjectPosition(obj),
            _ => null,
        };
    }

    /// <summary>
    /// How far apart two actors or objects are, the larger of the two axes; 0 for two actors
    /// together in another room, 255 when either cannot be placed. An actor measures to the point
    /// of its boxes nearest an object. Mirrors <c>getObjActToObjActDist</c>.
    /// </summary>
    private int ObjectDistance(int a, int b)
    {
        var actorA = a is > 0 and < ActorCount ? Actors[a] : null;
        var actorB = b is > 0 and < ActorCount ? Actors[b] : null;
        if (actorA is not null && actorB is not null && actorA.Room == actorB.Room && actorA.Room != 0 && !actorA.IsInCurrentRoom)
        {
            return 0;
        }

        if (PlacedPosition(a) is not { } p || PlacedPosition(b) is not { } q)
        {
            return 0xFF;
        }

        if (actorA is not null && actorB is null)
        {
            var (x, y, _) = actorA.AdjustXYToBeInBox(q.X, q.Y);
            q = (x, y);
        }

        return Math.Max(Math.Abs(p.X - q.X), Math.Abs(p.Y - q.Y));
    }

    /// <summary>Where an actor in the room, a carried object's owner in the room, or an object of the room is. Mirrors <c>getObjectOrActorXY</c>.</summary>
    private (int X, int Y)? PlacedPosition(int obj)
    {
        if (obj is > 0 and < ActorCount)
        {
            return Actors[obj].IsInCurrentRoom ? (Actors[obj].X, Actors[obj].Y) : null;
        }

        return WhereIsObject(obj) switch
        {
            ObjectPlace.Inventory when GetOwner(obj) is > 0 and < ActorCount && Actors[GetOwner(obj)].IsInCurrentRoom
                => (Actors[GetOwner(obj)].X, Actors[GetOwner(obj)].Y),
            ObjectPlace.Room => ObjectPosition(obj),
            _ => null,
        };
    }

    /// <summary>
    /// Mirrors <c>o5_loadRoomWithEgo</c> for version 3: the ego goes to the room, stands at the
    /// object unless the entry script placed it, the camera follows it, and it walks on when a
    /// place is given.
    /// </summary>
    private void LoadRoomWithEgo(int obj, int room, int x, int y)
    {
        var ego = Actors[CheckActor(Engine.Variables[Vars.Ego])];
        PutActor(ego, ego.X, ego.Y, room);
        int oldDir = ego.Facing;
        _egoPositioned = false;
        Engine.Variables[Vars.WalkToObject] = obj;
        StartScene(ego.Room);
        Engine.Variables[Vars.WalkToObject] = 0;
        if (!_egoPositioned && ObjectWalkTo(obj) is { } at)
        {
            PutActor(ego, at.X, at.Y, CurrentRoomNumber);
            if (ego.Facing == oldDir)
            {
                ego.SetDirection(at.Direction + 180);
            }
        }

        ego.StopMoving();
        Camera.X = Camera.DestX = ego.X;
        FollowActor(ego);
        if (x != -1)
        {
            ego.StartWalk(x, y, -1);
        }
    }

    /// <summary>Mirrors <c>setCameraFollows</c>.</summary>
    private void FollowActor(Actor actor)
    {
        Camera.Mode = CameraMode.FollowActor;
        Camera.Follows = actor.Number;
        if (!actor.IsInRoom(CurrentRoomNumber))
        {
            StartScene(actor.Room);
            Camera.Mode = CameraMode.FollowActor;
            Camera.X = actor.X;
            Camera.SetAt(Camera.X, Engine.Variables[Vars.CameraMinX], Engine.Variables[Vars.CameraMaxX]);
        }

        if (Camera.OutsideTriggers(actor.X))
        {
            Camera.SetAt(actor.X, Engine.Variables[Vars.CameraMinX], Engine.Variables[Vars.CameraMaxX]);
        }

        RunInventoryScript(0);
    }

    /// <summary>Mirrors <c>Actor::putActor</c>: a talking actor leaving the room stops talking; the ego placed counts as positioned.</summary>
    private void PutActor(Actor actor, int x, int y, int? room = null)
    {
        int newRoom = room ?? actor.Room;
        if (actor.Visible && CurrentRoomNumber != newRoom && TalkingActor == actor.Number)
        {
            StopTalk();
        }

        if (Engine.Variables[Vars.Ego] == actor.Number)
        {
            _egoPositioned = true;
        }

        actor.Put(x, y, newRoom);
    }

    /// <summary>
    /// Mirrors <c>Actor::faceToObject</c> for version 3: right or left of the actor, or in front or
    /// behind when within half the other's width.
    /// </summary>
    private void FaceActor(Actor actor, int obj)
    {
        if (!actor.IsInCurrentRoom || ObjectOrActorPosition(obj) is not { } at)
        {
            return;
        }

        int width = obj is > 0 and < ActorCount ? Actors[obj].Width : CurrentRoom?.FindObject(obj)?.Width ?? 0;
        int dir = actor.X < at.X ? 1 : 0;
        if (Math.Abs(actor.X - at.X) < width / 2)
        {
            dir = actor.Y > at.Y ? 3 : 2;
        }

        actor.TurnToDirection(Actor.NewDirection(dir));
    }

    /// <summary>Mirrors <c>ScummEngine_v5::walkActorToActor</c> for version 3: beside the other actor, so far off on this side, in a box.</summary>
    private void WalkActorToActor(Actor actor, Actor to, int dist)
    {
        if (dist == 0xFF)
        {
            dist = actor.ScaleX * actor.Width / 0xFF + to.ScaleX * to.Width / 0xFF / 2;
        }

        int x = to.X < actor.X ? to.X + dist : to.X - dist;
        var (ax, ay, _) = actor.AdjustXYToBeInBox(x, to.Y);
        actor.StartWalk(ax, ay, -1);
    }

    /// <summary>Mirrors <c>o5_actorOps</c>: the sub-operations, numbered after <c>convertTable</c>.</summary>
    private void ActorOps(Actor actor, IReadOnlyList<SubOperation> subOps, IReadOnlyList<int[]> args)
    {
        for (int i = 0; i < subOps.Count; i++)
        {
            int[] a = args[i];
            switch (subOps[i].Code)
            {
                case 1:
                    actor.SetCostume(a[0]);
                    break;
                case 2:
                    actor.SetWalkSpeed(a[0], a[1]);
                    break;
                case 3:
                    actor.Sound = a[0];
                    break;
                case 4:
                    actor.WalkFrame = a[0];
                    break;
                case 5:
                    actor.TalkStartFrame = a[0];
                    actor.TalkStopFrame = a[1];
                    break;
                case 6:
                    actor.StandFrame = a[0];
                    break;
                case 8:
                    actor.Init(0);
                    _classes[actor.Number] = 0;
                    break;
                case 9:
                    actor.Elevation = a[0];
                    break;
                case 10:
                    actor.InitFrame = 1;
                    actor.WalkFrame = 2;
                    actor.StandFrame = 3;
                    actor.TalkStartFrame = 4;
                    actor.TalkStopFrame = 5;
                    break;
                case 11:
                    actor.Palette[a[0] & 0xFF] = (byte)a[1];
                    break;
                case 12:
                    actor.TalkColor = a[0];
                    break;
                case 13:
                    actor.Name = subOps[i].Operands[0].Text;
                    break;
                case 14:
                    actor.InitFrame = a[0];
                    break;
                case 16:
                    actor.Width = a[0];
                    break;
                case 17:
                    actor.ScaleX = a[0];
                    actor.ScaleY = a[1];
                    break;
                case 18:
                    actor.ForceClip = 0;
                    break;
                case 19:
                    actor.ForceClip = a[0];
                    break;
                case 20:
                    actor.IgnoreBoxes = true;
                    break;
            }
        }
    }

    /// <summary>Mirrors <c>o5_roomOps</c> for version 3, whose two operands come first.</summary>
    private void RoomOps(SubOperation sub, int[] a)
    {
        switch (sub.Code)
        {
            case 1:
                {
                    int width = CurrentRoom?.Width ?? Camera.ScreenWidth;
                    int Clamp(int x) => Math.Min(Math.Max(x, Camera.ScreenWidth / 2), width - Camera.ScreenWidth / 2);
                    Engine.Variables[Vars.CameraMinX] = Clamp(a[0]);
                    Engine.Variables[Vars.CameraMaxX] = Clamp(a[1]);
                }

                break;
            case 2:
                // _fullRedraw: the room is drawn again in its new colours
                RoomPalette[a[1] & 0xFF] = (byte)a[0];
                RedrawRoom();
                break;
            case 3:
                Screens.Init(a[0], a[1], CurrentRoom?.Width ?? Camera.ScreenWidth);
                break;
            case 4:
                ShadowPalette[a[1] & 0xFF] = (byte)a[0];
                break;
            default:
                _unhandled.Add($"roomOps {sub}");
                break;
        }
    }

    /// <summary>Mirrors <c>o5_wait</c>: the instruction repeats while the actor moves, a message shows, the camera pans or a sentence runs.</summary>
    private void Wait(ScriptEngine engine, SubOperation sub, int[] a)
    {
        bool waiting = sub.Code switch
        {
            1 => a[0] is > 0 and < ActorCount && Actors[a[0]].Moving != 0,
            2 => Engine.Variables[Vars.HaveMessage] != 0,
            3 => Camera.X / 8 != Camera.DestX / 8,
            4 => engine.Sentences.Count > 0
                ? !(engine.Sentences[^1].FreezeCount != 0 && !engine.IsScriptInUse(Engine.Variables[Vars.SentenceScript]))
                : engine.IsScriptInUse(Engine.Variables[Vars.SentenceScript]),
            _ => false,
        };

        if (waiting)
        {
            engine.Repeat();
        }
    }

    /// <summary>
    /// Mirrors <c>decodeParseString</c>: the text slot by who prints, its defaults, the
    /// sub-operations, and the text, after which only its place and colour become the defaults.
    /// </summary>
    private void DecodeParseString(IReadOnlyList<SubOperation> subOps, IReadOnlyList<int[]> args)
    {
        int slot = ActorToPrintFor switch { 252 => 3, 253 => 2, 254 => 1, _ => 0 };
        var text = TextSlots[slot];
        text.LoadDefault();
        for (int i = 0; i < subOps.Count; i++)
        {
            int[] a = args[i];
            switch (subOps[i].Code)
            {
                case 0:
                    text.X = a[0];
                    text.Y = a[1];
                    text.Overhead = false;
                    break;
                case 1:
                    text.Color = a[0];
                    break;
                case 2:
                    text.Right = a[0];
                    break;
                case 4:
                    text.Center = true;
                    text.Overhead = false;
                    break;
                case 6 when _loader.Version == 3:
                    text.Height = a[0];
                    break;
                case 6:
                    // version 4: left-aligned
                    text.Center = false;
                    text.Overhead = false;
                    break;
                case 7:
                    text.Overhead = true;
                    break;
                case 8 when _loader.Version == 4:
                    SayVoice((ushort)a[0], (ushort)a[1]);
                    break;
                case 15:
                    PrintString(slot, subOps[i].Operands[0].Text!);

                    // versions 1 to 3 have no defaults of their own: the slot keeps where it printed
                    if (_loader.Version == 3)
                    {
                        text.KeepPlace();
                    }

                    return;
            }
        }

        text.SaveDefault();
    }

    /// <summary>
    /// Mirrors <c>o5_stringOps</c>: a string is its text and the zero after it, as
    /// <c>loadPtrToResource</c> keeps it, or a buffer of a given size; a character read is the result.
    /// </summary>
    private int StringOps(SubOperation sub, int[] a)
    {
        switch (sub.Code)
        {
            case 1:
                _strings[CheckString(a[0])] = sub.Operands[1].Text is { } text ? [.. text, 0] : null;
                break;
            case 2:
                if (_strings[CheckString(a[1])] is { } source)
                {
                    _strings[CheckString(a[0])] = [.. source.AsSpan(0, Math.Min(ResStrLen(source) + 1, source.Length))];
                }

                break;
            case 3:
                if (_strings[CheckString(a[0])] is { } target && a[1] >= 0 && a[1] < target.Length)
                {
                    target[a[1]] = (byte)a[2];
                }

                break;
            case 4:
                return _strings[CheckString(a[0])] is { } read && a[1] >= 0 && a[1] < read.Length ? read[a[1]] : 0;
            case 5:
                _strings[CheckString(a[0])] = a[1] != 0 ? new byte[a[1]] : null;
                break;
            default:
                _unhandled.Add($"stringOps {sub}");
                break;
        }

        return 0;
    }

    /// <summary>The length of a string before its zero, a code's bytes included. Mirrors <c>resStrLen</c>.</summary>
    private static int ResStrLen(ReadOnlySpan<byte> text)
    {
        int at = 0;
        while (at < text.Length && text[at] != 0)
        {
            if (text[at++] == 0xFF && at < text.Length && text[at++] is not (1 or 2 or 3 or 8))
            {
                at += 2;
            }
        }

        return Math.Min(at, text.Length);
    }

    private static int CheckString(int number)
        => number is >= 0 and < StringCount ? number : throw new InvalidOperationException($"String {number} is out of range (0 to {StringCount - 1}).");
}
