using System.Buffers.Binary;
using System.Text;
using Distaff.Scumm.Detection;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Rooms;
using Distaff.Scumm.Saves;
using Distaff.Scumm.Scripts;
using Distaff.Scumm.Sound;
using Distaff.Scumm.Storage;
using Distaff.Scumm.View;

namespace Distaff.Scumm.Game;

/// <summary>
/// Saving and loading in ScummVM's format. Mirrors what <c>ScummEngine::saveState</c> captures and
/// what <c>loadState</c> does with it (the room set up without its scripts, everything else put
/// back, the screen's bands cleared), Loom's own save screen (<c>o4_saveLoadGame</c>,
/// <c>o4_saveLoadVars</c>, the save names in strings 9 to 20), and what ScummVM does after a load
/// from outside that screen (<c>ScummEngine_v3::scummLoop_handleSaveLoad</c>,
/// <c>terminateSaveMenuScript</c>).
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>The room Loom's save screen is in (<c>saveRoom</c>).</summary>
    public const int SaveRoom = 70;

    /// <summary>The string holding the first save's name; the others follow (<c>STRINGID_SAVENAME1_LOOM</c>).</summary>
    public const int SaveNameString = 9;

    // where a script's code is (WIO_INVENTORY, WIO_ROOM, WIO_GLOBAL, WIO_LOCAL)
    private const int WhereInventory = 0, WhereRoom = 1, WhereGlobal = 2, WhereLocal = 3;

    // a load from outside the save screen is put right at the start of the next step (_loadFromLauncher)
    private bool _loadFromLauncher;

    /// <summary>Where the game's save screen keeps its saves; with none, saving fails and no save exists.</summary>
    public ISaveStore? SaveStore { get; set; }

    /// <summary>
    /// The name save files start with, as ScummVM names them after the game's target: by default
    /// the release's own (<c>loom-ega.s01</c>, <c>loom-vga.s01</c>), or "loom" for one not identified.
    /// </summary>
    public string SaveTarget { get; set; }

    /// <summary>The save version the save screen writes: 108, ScummVM 2.7's, which every ScummVM since reads.</summary>
    public int SaveVersion { get; set; } = SaveFile.CompatibleVersion;

    /// <summary>A save's file name for a slot. Mirrors <c>makeSavegameName</c>.</summary>
    public string SaveFileName(int slot) => SaveFile.FileName(SaveTarget, slot);

    /// <summary>Saves the game as a ScummVM save file, as ScummVM's own menu does between steps.</summary>
    public byte[] Save(string name, DateTime saved, int version = SaveFile.CompatibleVersion)
        => SaveFile.Write(Capture(), name, Time, saved, version);

    /// <summary>
    /// Loads a ScummVM save file, as a load from ScummVM's launcher does: the state is put back now,
    /// and at the start of the next step the game goes on from it as Loom's save screen would.
    /// </summary>
    public SaveInfo Load(byte[] file)
    {
        var (info, state) = SaveFile.Read(file, _owners.Length, _loader.Version, FmTowns);
        Restore(state);
        SetPlayTime(info.PlayTime);

        // the script the save was made in is the current one, for terminateSaveMenuScript (_currentScript)
        int saved = state.CurrentScript < ScriptEngine.SlotCount ? state.CurrentScript : -1;
        Engine.ContinueAfterLoad(saved, saved >= 0 ? Engine.Slots[saved].Offset : 0);
        _loadFromLauncher = true;
        return info;
    }

    /// <summary>
    /// The game's time becomes the save's play time. The sound plays on from where it is: the time
    /// jumped over is not sound that went unheard, which the self clock would render to catch up
    /// (running the FM Towns' CD music on by the save's whole play time).
    /// </summary>
    private void SetPlayTime(TimeSpan time)
    {
        Time = time;
        Sound.Output.Resync(time);
        ResetScroll();

        // the load's full redraw brings the settings back (syncSoundSettings): the text speed is the setting's, not the save's
        Engine.Variables[Vars.CharInc] = 9 - TextSpeed;
    }

    /// <summary>
    /// After a load from outside the save screen, at the start of the step. Mirrors
    /// <c>ScummEngine_v3::scummLoop_handleSaveLoad</c> for Loom on DOS: a save made in the save
    /// room ends the save screen's script; one made where the save screen could have been opened
    /// goes through the save room inside a cutscene, as if loaded there, with the ego's box and
    /// place kept for the camera. The VGA CD has no save screen and takes <c>ScummEngine_v5</c>'s
    /// instead: the verbs are drawn again, and variable 66 set, on which Loom's scripts draw the
    /// notes' letters and the name over the pointed-at object's image again. The FM Towns' save
    /// room starts its sounds again with script 38, given the sound in variable 163, and so does
    /// the path through it; otherwise its sound player starts again what the save had going
    /// (<c>restoreAfterLoad</c>).
    /// </summary>
    private void HandleSaveLoad()
    {
        if (!_loadFromLauncher)
        {
            return;
        }

        _loadFromLauncher = false;
        _hasMask = false;
        if (_loader.Version == 4)
        {
            RedrawVerbs();
            Engine.Variables[66] = 1;
            return;
        }

        bool restoreSounds = true;
        const int SaveLoadVar = 100, VerbFlags = 214, SoundVar = 163;
        var v = Engine.Variables;
        if (CurrentRoomNumber == SaveRoom)
        {
            TerminateSaveMenuScript();
        }
        else if (UserPut > 0 && !Engine.IsScriptRunning(v[Vars.VerbScript]))
        {
            int prevFlag = v[VerbFlags] & 0x6000;
            Engine.BeginCutscene([]);
            int blockVerbsFlag = v[VerbFlags] & (0x6000 ^ prevFlag);
            if (v[Vars.Ego] is > 0 and < ActorCount)
            {
                var ego = Actors[v[Vars.Ego]];
                (v[171], v[172], v[173]) = (ego.WalkBox, ego.X, ego.Y);
            }

            StartScene(SaveRoom);
            v[SaveLoadVar] = 0;
            v[VerbFlags] &= ~blockVerbsFlag;
            Engine.EndCutscene();
            if (FmTowns && v[SoundVar] != 0)
            {
                Engine.RunScript(38, args: [v[SoundVar]]);
            }

            restoreSounds = false;
        }
        else if (v[SaveLoadVar] == 2)
        {
            Engine.RunScript(FmTowns ? 17 : 18, args: [2]);
            v[SaveLoadVar + 2] = v[SaveLoadVar + 3] = 0;
        }

        if (FmTowns && restoreSounds)
        {
            TownsPlayer.RestoreAfterLoad(LoadSound);
        }
    }

    /// <summary>
    /// Ends the save screen's script as it would end itself after a load. Mirrors the DOS Loom part
    /// of <c>terminateSaveMenuScript</c>: the verb script comes back from its local variable 2, the
    /// save names go, the names script stops, the screen's flags clear, the cutscene ends, the
    /// cursor and the player's control return, and script 6 takes the save script's place. The flag
    /// cleared is bit 14 of variable 214 (<c>Bit[3438]</c>), which this release's own save script
    /// clears when the player leaves the screen: ScummVM clears bit 13 on DOS (and 14 only on the FM
    /// Towns), and with bit 14 left set the room's entry script does not bring the distaff back.
    /// The FM Towns' also stops the names script first, takes the screen's objects 909, 908, 903
    /// and 904 away, starts the sounds again with script 38, given the sound in variable 163, and
    /// puts script 5 in the save script's place.
    /// </summary>
    private void TerminateSaveMenuScript()
    {
        if (Engine.CurrentSlot < 0)
        {
            return;
        }

        if (FmTowns)
        {
            Engine.StopScript(202);
        }

        Engine.Variables[Vars.VerbScript] = Engine.ReadVar(Operand.LocalVariable | 2);
        for (int i = SaveNameString; i < SaveNameString + 12; i++)
        {
            _strings[i] = [0];
        }

        Engine.StopScript(202);
        Engine.WriteVar(Operand.BitVariable | 0xD6E, 0);
        Engine.Variables[100] = 0;
        if (FmTowns)
        {
            foreach (int obj in (int[])[909, 908, 903, 904])
            {
                PutState(obj, 0);
                _drawnObjects.Remove(obj);
            }
        }

        Engine.EndCutscene();
        if (FmTowns && Engine.Variables[163] != 0)
        {
            Engine.RunScript(38, args: [Engine.Variables[163]]);
        }

        CursorState = 1;
        UserPut = 1;
        Engine.ChainCurrent(FmTowns ? 5 : 6, [0]);
    }

    /// <summary>
    /// The save screen's instruction: how many slots, which drive, whether a slot has a save (for the
    /// FM Towns' slot 0, whether any has, as its LOAD asks before listing them), save
    /// to a slot under the name in its string, load from one. A load replaces the world; the script
    /// that asked carries on after the instruction in the loaded state, or, when the save was not
    /// made in the save room, the game starts again from the boot script, is loaded once more, and
    /// goes on as after a load from the launcher. Mirrors <c>o4_saveLoadGame</c> for version 3.
    /// </summary>
    private int SaveLoadGame(int argument, Instruction instruction)
    {
        int slot = argument & 0x1F;
        switch (argument & 0xE0)
        {
            case 0x00:
                return 100;
            case 0x20:
                return 3;
            case 0xC0:
                // the FM Towns' screen asks about slot 0 before LOAD: there, any save will do
                return ReadSave(slot) is not null || (FmTowns && slot == 0 && AnySave()) ? 6 : 7;
            case 0x80:
                return WriteSave(slot, SaveName(slot)) ? 0 : 2;

            case 0x40:
                {
                    // the name can be changed before loading, as the original's screen allowed
                    if (ReadSave(slot) is not { } file)
                    {
                        return 5;
                    }

                    file = SaveFile.Rename(file, SaveName(slot));
                    using (var output = SaveStore!.OpenWrite(SaveFileName(slot)))
                    {
                        output.Write(file);
                    }

                    SaveInfo info;
                    ScummState state;
                    try
                    {
                        (info, state) = SaveFile.Read(file, _owners.Length, _loader.Version, FmTowns);
                    }
                    catch (InvalidDataException)
                    {
                        return 5;
                    }

                    Restore(state);
                    SetPlayTime(info.PlayTime);
                    int saved = state.CurrentScript < ScriptEngine.SlotCount ? state.CurrentScript : -1;
                    if (state.CurrentRoom != SaveRoom)
                    {
                        // a save made outside the save room: the game starts over, then loads again
                        Engine.ContinueAfterLoad(-1, 0);
                        Boot();
                        Restore(state);
                        Engine.ContinueAfterLoad(saved, saved >= 0 ? Engine.Slots[saved].Offset : 0);
                        _loadFromLauncher = true;
                        Engine.DiscardResult();
                        return 0;
                    }

                    // the loaded state's current script carries on from where the loading one stands,
                    // and the result goes to it (setResult and updateScriptPtr after loadState)
                    Engine.ContinueAfterLoad(saved, instruction.End);
                    if (saved >= 0 && instruction.Result is { } result)
                    {
                        Engine.WriteVar(Engine.VariableNumber(result), 3);
                    }

                    Engine.DiscardResult();
                    return 0;
                }

            default:
                throw new InvalidOperationException($"saveLoadGame has no operation {argument & 0xE0:X2}.");
        }
    }

    /// <summary>
    /// The save screen's names. Mirrors <c>o4_saveLoadVars</c> for Loom: saving writes nothing
    /// (each save carries its name); loading fills strings with the names of saves 1 on, letters
    /// and digits kept and others made '_', as far as each string holds, and empties the strings of
    /// slots with no save.
    /// </summary>
    private void SaveLoadVars(int operation, IReadOnlyList<SubOperation> subOps, IReadOnlyList<int[]> args)
    {
        if (operation == 1)
        {
            return;
        }

        for (int i = 0; i < subOps.Count; i++)
        {
            if (subOps[i].Code != 2)
            {
                continue;
            }

            int first = args[i][0], last = args[i][1];
            for (int s = first; s <= last; s++)
            {
                if (_strings[CheckString(s)] is not { Length: > 0 } content)
                {
                    continue;
                }

                if (ReadSave(s - first + 1) is { } file && SaveFile.ReadInfo(file).Name is var name)
                {
                    int pos = 0;
                    for (; pos < content.Length - 2 && pos < name.Length; pos++)
                    {
                        char c = name[pos];
                        content[pos] = c is >= ' ' and <= 'z' and not '@' ? (byte)c : (byte)'_';
                    }

                    content[pos] = 0;
                }
                else
                {
                    content[0] = 0;
                }
            }
        }
    }

    /// <summary>The name in a slot's string, up to its zero and 31 characters (the save header's name).</summary>
    private string SaveName(int slot)
    {
        var text = _strings[CheckString(SaveNameString + slot - 1)] ?? [];
        int end = Array.IndexOf(text, (byte)0);
        return Encoding.Latin1.GetString(text, 0, Math.Min(end < 0 ? text.Length : end, 31));
    }

    /// <summary>Saves the game to a slot under a name (<c>saveState</c>); false with nowhere to keep saves.</summary>
    private bool WriteSave(int slot, string name)
    {
        if (SaveStore is null)
        {
            return false;
        }

        var data = SaveFile.Write(Capture(), name, Time, DateTime.Now, SaveVersion);
        using var output = SaveStore.OpenWrite(SaveFileName(slot));
        output.Write(data);
        return true;
    }

    /// <summary>
    /// Whether any slot, 0 to 99, has a save file: the files named after the target and a slot.
    /// Mirrors <c>listSavegames</c> as <c>o4_saveLoadGame</c> uses it for Loom's FM Towns release.
    /// </summary>
    private bool AnySave()
    {
        string prefix = SaveFileName(0)[..^2];
        return SaveStore?.List(prefix).Any(name => name.Length == prefix.Length + 2
            && int.TryParse(name.AsSpan(prefix.Length), System.Globalization.NumberStyles.None, null, out _)) == true;
    }

    /// <summary>A slot's save file, or null when it has none.</summary>
    private byte[]? ReadSave(int slot)
    {
        if (SaveStore?.OpenRead(SaveFileName(slot)) is not { } input)
        {
            return null;
        }

        using (input)
        {
            using var copy = new MemoryStream();
            input.CopyTo(copy);
            return copy.ToArray();
        }
    }

    /// <summary>The world as a ScummVM save state. Mirrors the state <c>saveLoadWithSerializer</c> writes for Loom version 3.</summary>
    public ScummState Capture()
    {
        var s = new ScummState(_owners.Length, _loader.Version, FmTowns);
        var room = CurrentRoom;
        s.GameMd5 = ScummDetector.IndexMd5(_loader.Game);
        s.CurrentRoom = CurrentRoomNumber;
        s.RoomResource = Engine.Variables[Vars.RoomResource];
        s.ObjectsInRoom = room?.Objects.Count ?? 0;
        s.CurrentScript = Engine.CurrentSlot < 0 ? 0xFF : Engine.CurrentSlot;
        for (int i = 0; i < ScummState.SlotCount; i++)
        {
            for (int j = 0; j < ScriptSlot.LocalCount; j++)
            {
                s.LocalVars[i * ScummState.LocalVariables + j] = Engine.Slots[i].Locals[j];
            }
        }

        _resourceMapper.CopyTo(s.ResourceMapper, 0);

        // the camera: y is the screen's middle in version 3
        (s.CameraX, s.CameraDestX, s.CameraLastX) = (Camera.X, Camera.DestX, Camera.LastX);
        s.CameraY = s.CameraDestY = s.CameraLastY = ScreenHeight / 2;
        (s.ScreenStartStrip, s.ScreenEndStrip) = (Camera.StartStrip, Camera.EndStrip);
        (s.CameraMode, s.CameraFollows, s.CameraMovingToActor) = ((int)Camera.Mode, Camera.Follows, Camera.MovingToActor ? 1 : 0);

        // the message
        (s.ActorToPrintStrFor, s.CharsetColor, s.CharsetBufPos, s.HaveMessage) = (ActorToPrintFor, _charsetColor, _charsetBufPos, _haveMsg);
        (s.HaveActorSpeechMessage, s.UseTalkAnims, s.TalkDelay, s.KeepText) = (_haveActorSpeechMsg ? 1 : 0, _useTalkAnims ? 1 : 0, _talkDelay, _keepText ? 1 : 0);
        s.DefaultTextSpeed = Math.Clamp(9 - Engine.Variables[Vars.CharInc], 0, 9);
        _charsetBuffer.AsSpan(0, Math.Min(_charsetBuffer.Length, s.CharsetBuffer.Length - 1)).CopyTo(s.CharsetBuffer);
        for (int i = 0; i < TextSlots.Length; i++)
        {
            TextSlots[i].SaveTo(s.Strings[i]);
        }

        // the charset and its colours: version 3 has the one font, and no colours for it
        (s.CharsetId, s.CharsetRendererColor) = (_charsetId, _textColor);
        if (_loader.Version == 4)
        {
            CharsetColorMap.CopyTo(s.CharsetColorMap, 0);
            for (int i = 0; i < _charsetData.Length; i++)
            {
                _charsetData[i].CopyTo(s.CharsetData, i * 16);
            }
        }

        // the cutscenes, the nesting and the sentences
        var (data, slots, offsets) = Engine.CutsceneStack;
        s.CutSceneStackPointer = Engine.CutsceneLevel;
        for (int i = 0; i < ScriptEngine.CutsceneLevels; i++)
        {
            (s.CutSceneData[i], s.CutSceneScript[i], s.CutScenePtr[i]) = (data[i], (byte)slots[i], offsets[i]);
        }

        s.CutSceneScriptIndex = Engine.CutsceneStarter < 0 ? 0xFF : Engine.CutsceneStarter;
        s.NestedScripts = Engine.Nested.Count;
        for (int i = 0; i < Engine.Nested.Count; i++)
        {
            var (number, kind, slot) = Engine.Nested[i];
            (s.Nested[i].Number, s.Nested[i].Where, s.Nested[i].Slot) = number == 0 ? (0, 0xFF, 0) : (number, Where(kind), slot);
        }

        s.SentenceNumber = Engine.Sentences.Count;
        for (int i = 0; i < Engine.Sentences.Count; i++)
        {
            var sentence = Engine.Sentences[i];
            (s.Sentences[i].Verb, s.Sentences[i].ObjectA, s.Sentences[i].ObjectB, s.Sentences[i].FreezeCount) = (sentence.Verb, sentence.ObjectA, sentence.ObjectB, sentence.FreezeCount);
        }

        // the player's control and the cursor, as setBuiltinCursor leaves _grabbedCursor
        (s.UserPut, s.CursorState, s.CurrentCursor) = (UserPut, CursorState, _currentCursor);
        Array.Fill(s.GrabbedCursor, (byte)0xFF);
        var rows = _cursorImages[_currentCursor];
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                if ((rows[y] & (1 << (15 - x))) != 0)
                {
                    s.GrabbedCursor[y * 16 + x] = _cursorColor;
                }
            }
        }

        (s.CursorWidth, s.CursorHeight, s.CursorHotspotX, s.CursorHotspotY) = (16, 16, CursorHotspot.X, CursorHotspot.Y);
        (s.CursorAnimate, s.CursorAnimateIndex, s.MouseX, s.MouseY) = (1, _cursorAnimateIndex & 0xFF, MouseX, MouseY);
        for (int i = 0; i < 4; i++)
        {
            for (int y = 0; y < 16; y++)
            {
                s.CursorImages[i * 16 + y] = _cursorImages[i][y];
            }

            (s.CursorHotspots[i * 2], s.CursorHotspots[i * 2 + 1]) = ((byte)_cursorHotspots[i].X, (byte)_cursorHotspots[i].Y);
        }

        // the screen: the EGA's colours as resetPalette sets them, or the VGA CD's as the room and the
        // scripts set them; the darkened palette, which only later versions fill, stays black; the
        // VGA CD's colour cycles; the room's view
        Rgb[] colors = _loader.Version == 4 || FmTowns ? CurrentPalette : Palettes.Ega;
        for (int i = 0; i < colors.Length; i++)
        {
            (s.CurrentPalette[i * 3], s.CurrentPalette[i * 3 + 1], s.CurrentPalette[i * 3 + 2]) = (colors[i].R, colors[i].G, colors[i].B);
        }

        SaveCycles(s.ColorCycles);
        RoomPalette.CopyTo(s.RoomPalette, 0);
        ShadowPalette.CopyTo(s.ShadowPalette, 0);
        if (FmTowns)
        {
            CaptureTowns(s);
        }
        else if (Sound.Player is PlayerAd adLib)
        {
            // the AdLib's music and effects, and where the music is (Player_AD::saveLoadWithSerializer)
            s.SoundEngineData = adLib.SaveState();
        }
        (s.EgoPositioned, s.ScreenEffectFlag, s.ScreenB, s.ScreenH) = (_egoPositioned ? 1 : 0, _screenEffectFlag ? 1 : 0, ViewTop, ViewTop + ViewHeight);
        (s.DoEffect, s.SwitchRoomEffect, s.NewEffect, s.SwitchRoomEffect2) = (_doEffect ? 1 : 0, _switchRoomEffect, _newEffect, _switchRoomEffect2);

        for (int i = 0; i < ScummState.ActorCount; i++)
        {
            Actors[i].SaveTo(s.Actors[i]);
            if (Actors[i].Name is { } name)
            {
                s.Resources[(ScummState.TypeActorName, i)] = new ScummState.Resource([.. Terminated(name)]);
            }
        }

        for (int i = 0; i < ScummState.SlotCount; i++)
        {
            SaveSlot(i, s.Slots[i]);
        }

        if (room is not null)
        {
            for (int i = 0; i < room.Objects.Count && i + 1 < ScummState.LocalObjectCount; i++)
            {
                SaveObject(room.Objects[i], s.Objects[i + 1]);
            }
        }

        for (int i = 0; i < VerbCount; i++)
        {
            SaveVerb(i, s);
        }

        SaveResources(s, room);
        _owners.CopyTo(s.ObjectOwners, 0);
        _states.CopyTo(s.ObjectStates, 0);
        _classes.CopyTo(s.ClassData, 0);
        Array.Copy(Engine.Variables, s.Variables, ScummState.VariableCount);
        Engine.BitVariables.CopyTo(s.BitVariables, 0);

        // what the CD last played, and whether it still plays (getStatus)
        var cd = Sound.Cd.Status;
        (s.AudioCdStatus[0], s.AudioCdStatus[1], s.AudioCdStatus[2], s.AudioCdStatus[3], s.AudioCdStatus[4]) = (cd.Playing ? 1 : 0, cd.Track, cd.Start, cd.Duration, cd.NumLoops);
        return s;
    }

    /// <summary>
    /// Puts the world in the state a save holds. Mirrors <c>loadState</c>: the room is set up
    /// without running its scripts, its boxes as the save has them, everything else is taken from
    /// the save, and the screen's bands are cleared to be drawn again. The mouse stays where it is,
    /// as with ScummVM's original interface.
    /// </summary>
    public void Restore(ScummState s)
    {
        Sound.StopAll();
        Sound.Cd.Stop();
        CdTrack = null;
        foreach (var slot in Engine.Slots)
        {
            slot.Kill();
        }

        Array.Copy(s.ObjectOwners, _owners, Math.Min(s.ObjectOwners.Length, _owners.Length));
        Array.Copy(s.ObjectStates, _states, Math.Min(s.ObjectStates.Length, _states.Length));
        Array.Copy(s.ClassData, _classes, Math.Min(s.ClassData.Length, _classes.Length));
        Array.Copy(s.Variables, Engine.Variables, ScummState.VariableCount);
        s.BitVariables.CopyTo(Engine.BitVariables, 0);
        s.ResourceMapper.CopyTo(_resourceMapper, 0);
        s.RoomPalette.CopyTo(RoomPalette, 0);
        s.ShadowPalette.CopyTo(ShadowPalette, 0);

        // the room, entered without its scripts (setupRoomSubBlocks), with the boxes and objects as the save has them
        _moved.Clear();
        CurrentRoomNumber = s.CurrentRoom;
        Engine.CurrentRoom = s.CurrentRoom;
        CurrentRoom = s.CurrentRoom == 0 ? null
            : _loader.LoadRoom(s.RoomResource) ?? throw new InvalidDataException($"The save is in room {s.RoomResource}, which this release does not have.");
        var room = CurrentRoom;
        _boxFlags = room?.WalkBoxes.Boxes.Select(b => (int)b.Flags).ToArray() ?? [];
        if (room is not null)
        {
            if (s.Resources.TryGetValue((ScummState.TypeMatrix, 2), out var boxes))
            {
                for (int i = 0; i < _boxFlags.Length && 1 + i * BoxSize + 17 < boxes.Data.Length; i++)
                {
                    _boxFlags[i] = boxes.Data[1 + i * BoxSize + 17];
                }
            }

            for (int i = 0; i < room.Objects.Count && i + 1 < ScummState.LocalObjectCount; i++)
            {
                var saved = s.Objects[i + 1];
                var obj = room.Objects[i];
                if (saved.Number == obj.Number && (saved.X, saved.Y) != (obj.X, obj.Y))
                {
                    _moved[obj.Number] = (saved.X, saved.Y);
                }
            }
        }

        // the camera, its last place its place (loadState)
        Camera.RoomWidth = room?.Width ?? Camera.ScreenWidth;
        (Camera.X, Camera.DestX, Camera.LastX) = (s.CameraX, s.CameraDestX, s.CameraX);
        (Camera.Mode, Camera.Follows, Camera.MovingToActor) = (Enum.IsDefined((CameraMode)s.CameraMode) ? (CameraMode)s.CameraMode : CameraMode.Normal, s.CameraFollows, s.CameraMovingToActor != 0);
        Camera.Moved();

        for (int i = 0; i < ScummState.ActorCount; i++)
        {
            Actors[i].LoadFrom(s.Actors[i]);
            Actors[i].Name = s.Resources.TryGetValue((ScummState.TypeActorName, i), out var name) ? Unterminated(name.Data) : null;
        }

        RestoreResources(s);
        for (int i = 0; i < ScummState.SlotCount; i++)
        {
            RestoreSlot(s.Slots[i], Engine.Slots[i]);
            for (int j = 0; j < ScriptSlot.LocalCount; j++)
            {
                Engine.Slots[i].Locals[j] = s.LocalVars[i * ScummState.LocalVariables + j];
            }
        }

        var (data, slots, offsets) = Engine.CutsceneStack;
        for (int i = 0; i < ScriptEngine.CutsceneLevels; i++)
        {
            (data[i], slots[i], offsets[i]) = (s.CutSceneData[i], s.CutSceneScript[i], s.CutScenePtr[i]);
        }

        Engine.CutsceneStarter = s.CutSceneScriptIndex < ScriptEngine.SlotCount ? s.CutSceneScriptIndex : -1;
        var sentences = Enumerable.Range(0, Math.Min(s.SentenceNumber, ScummState.SentenceCount))
            .Select(i => new Sentence(s.Sentences[i].Verb, s.Sentences[i].ObjectA, s.Sentences[i].ObjectB) { FreezeCount = s.Sentences[i].FreezeCount });
        Engine.Restore(Math.Min(s.CutSceneStackPointer, ScriptEngine.CutsceneLevels - 1), sentences);

        for (int i = 0; i < VerbCount; i++)
        {
            RestoreVerb(i, s);
        }

        // the message, with nothing shown: the screen is drawn again
        (ActorToPrintFor, _charsetColor, _charsetBufPos, _haveMsg) = (s.ActorToPrintStrFor, s.CharsetColor, s.CharsetBufPos, s.HaveMessage);
        (_haveActorSpeechMsg, _useTalkAnims, _talkDelay, _keepText) = (s.HaveActorSpeechMessage != 0, s.UseTalkAnims != 0, s.TalkDelay, s.KeepText != 0);
        _charsetBuffer = [.. s.CharsetBuffer];
        _shown.Clear();
        for (int i = 0; i < TextSlots.Length; i++)
        {
            TextSlots[i].LoadFrom(s.Strings[i]);
        }

        // the VGA CD's charset, the one slot 0 starts from, with the colours the save has
        if (_loader.Version == 4)
        {
            UseCharset(TextSlots[0].DefaultCharset);
            s.CharsetColorMap.CopyTo(CharsetColorMap, 0);
            for (int i = 0; i < _charsetData.Length; i++)
            {
                s.CharsetData.AsSpan(i * 16, 16).CopyTo(_charsetData[i]);
            }
        }

        SetColor(s.CharsetRendererColor);
        _egoPositioned = s.EgoPositioned != 0;
        (_doEffect, _switchRoomEffect, _newEffect, _switchRoomEffect2, _screenEffectFlag) = (s.DoEffect != 0, s.SwitchRoomEffect, s.NewEffect, s.SwitchRoomEffect2, s.ScreenEffectFlag != 0);
        _effectFrames.Clear();
        _display = null;

        (UserPut, CursorState, _currentCursor) = (s.UserPut, s.CursorState, s.CurrentCursor & 3);
        _cursorAnimateIndex = s.CursorAnimateIndex;

        // the colour the last even step of animateCursor chose
        _cursorColor = CursorColors[_cursorAnimateIndex == 0 ? 0 : (((_cursorAnimateIndex - 1) & ~1) >> 1) & 3];
        for (int i = 0; i < 4; i++)
        {
            for (int y = 0; y < 16; y++)
            {
                _cursorImages[i][y] = (ushort)s.CursorImages[i * 16 + y];
            }

            _cursorHotspots[i] = (s.CursorHotspots[i * 2], s.CursorHotspots[i * 2 + 1]);
        }

        // the VGA CD's colours and cycles as the save has them: the room's are not set up again
        if (_loader.Version == 4 || FmTowns)
        {
            CurrentPalette = [.. Enumerable.Range(0, 256).Select(i => new Rgb(s.CurrentPalette[i * 3], s.CurrentPalette[i * 3 + 1], s.CurrentPalette[i * 3 + 2]))];
            LoadCycles(s.ColorCycles);
        }

        // the FM Towns' screen and sound: where the text last drew, and the sound player's state
        if (FmTowns)
        {
            var r = s.TownsCurStringRect;
            _curStringRect = s.HasTownsData ? Rect.FromBounds(r[0], r[1], r[2], r[3]) : Rect.FromBounds(-1, -1, -1, -1);
            TownsPlayer.LoadState(s.SoundEngineData);
        }
        else if (Sound.Player is PlayerAd adLib)
        {
            // the AdLib's music starts again where the save left it, and its effects with it
            adLib.LoadState(s.SoundEngineData, LoadSound);
        }

        // initScreens with the save's bands clears them; nothing drawn over the room is left
        Screens.Init(s.ScreenB, s.ScreenH, room?.Width ?? Camera.ScreenWidth);
        Screens.ClearTextSurface();
        _hasMask = false;
        _drawnObjects.Clear();
        _input = 0;

        // the CD part that was playing plays on (restoreCDAudioAfterLoad); the FM Towns' player sees to its own
        if (s.AudioCdStatus[0] != 0 && !FmTowns)
        {
            RestoreCdAfterLoad(s.AudioCdStatus[1], s.AudioCdStatus[2], s.AudioCdStatus[3], s.AudioCdStatus[4]);
        }
    }

    /// <summary>A box's size: version 3's end with their flags (<c>SIZEOF_BOX_V3</c>), version 4's with a scale after them (<c>SIZEOF_BOX</c>).</summary>
    private int BoxSize => _loader.Version == 4 ? WalkBoxes.BoxSizeV4 : WalkBoxes.BoxSize;

    private static int Where(ScriptKind kind) => kind switch
    {
        ScriptKind.Global => WhereGlobal,
        ScriptKind.Local => WhereLocal,
        ScriptKind.Inventory => WhereInventory,
        _ => WhereRoom,
    };

    /// <summary>A slot as ScummVM's <c>ScriptSlot</c> has it: the running one where it last yielded or nested, as <c>offs</c> is.</summary>
    private void SaveSlot(int index, ScummState.SlotState saved)
    {
        var slot = Engine.Slots[index];
        if (slot.IsDead)
        {
            return;
        }

        saved.Number = slot.Number;
        saved.Status = slot.Status;
        saved.Where = Where(slot.Kind);
        saved.Offset = index == Engine.CurrentSlot ? slot.ResumeOffset : slot.Offset;
        saved.Delay = slot.Delay;
        saved.FreezeResistant = slot.FreezeResistant ? 1 : 0;
        saved.Recursive = slot.Recursive ? 1 : 0;
        saved.FreezeCount = slot.FreezeCount;
        saved.CutsceneOverride = slot.CutsceneOverride;
        saved.Cycle = 1;
    }

    /// <summary>A slot from a save: its code is found again from its number and where it lives (<c>getScriptBaseAddress</c>).</summary>
    private void RestoreSlot(ScummState.SlotState saved, ScriptSlot slot)
    {
        if (saved.Status == SlotStatus.Dead || saved.Number == 0)
        {
            return;
        }

        var (kind, code) = saved.Where switch
        {
            WhereGlobal => (ScriptKind.Global, GlobalScript(saved.Number)),
            WhereInventory => (ScriptKind.Inventory, _inventoryCode.TryGetValue(saved.Number, out var carried) ? carried : (ReadOnlyMemory<byte>?)null),
            WhereLocal => (ScriptKind.Local, CurrentRoom?.Data),
            _ => (ScriptKind.Room, CurrentRoom?.Data),
        };

        if (code is null)
        {
            return;
        }

        slot.Number = saved.Number;
        slot.Kind = kind;
        slot.Code = code.Value;
        slot.Status = (byte)saved.Status;
        slot.Offset = slot.ResumeOffset = saved.Offset;
        slot.Delay = saved.Delay;
        slot.FreezeResistant = saved.FreezeResistant != 0;
        slot.Recursive = saved.Recursive != 0;
        slot.FreezeCount = saved.FreezeCount;
        slot.CutsceneOverride = saved.CutsceneOverride;
    }

    /// <summary>A room object as ScummVM's <c>ObjectData</c> has it: its offsets, rectangle and walk-to point, moved if drawn elsewhere.</summary>
    private void SaveObject(RoomObject obj, ScummState.ObjectState saved)
    {
        var (x, y) = PositionOf(obj);
        (saved.ImageOffset, saved.CodeOffset, saved.Number, saved.X, saved.Y) = (obj.ImageOffset, obj.CodeOffset, obj.Number, x, y);
        (saved.WalkX, saved.WalkY) = (obj.WalkX + x - obj.X, obj.WalkY + y - obj.Y);
        (saved.Width, saved.Height, saved.ActorDir, saved.ParentState, saved.Parent) = (obj.Width, obj.Height, obj.ActorDir, obj.ParentState, obj.Parent);
        saved.State = GetState(obj.Number);
    }

    /// <summary>A verb slot as ScummVM's <c>VerbSlot</c> has it, with its name or image as its <c>rtVerb</c> resource.</summary>
    private void SaveVerb(int index, ScummState s)
    {
        var vs = Verbs[index];
        var saved = s.Verbs[index];
        (saved.Left, saved.Top, saved.Right, saved.Bottom) = (vs.CurRect.Left, vs.CurRect.Top, vs.CurRect.Right, vs.CurRect.Bottom);
        if (vs.OldRect is { } old)
        {
            (saved.OldLeft, saved.OldTop, saved.OldRight, saved.OldBottom) = (old.Left, old.Top, old.Right, old.Bottom);
        }

        (saved.Id, saved.Color, saved.HiColor, saved.DimColor, saved.BkColor) = (vs.VerbId, vs.Color, vs.HiColor, vs.DimColor, vs.BackColor);
        (saved.Type, saved.Charset, saved.Mode, saved.SaveId, saved.Key) = (vs.Type, 0, vs.Mode, vs.SaveId, vs.Key);
        (saved.Center, saved.ImageIndex) = (vs.Center ? 1 : 0, vs.ImageObject);
        if (index == 0)
        {
            return;
        }

        byte[]? resource = vs.Type == 1 ? vs.Image : vs.Name is { } name ? [.. Terminated(name)] : null;
        if (resource is not null)
        {
            s.Resources[(ScummState.TypeVerb, index)] = new ScummState.Resource(resource);
        }
    }

    private void RestoreVerb(int index, ScummState s)
    {
        var vs = Verbs[index];
        var saved = s.Verbs[index];
        vs.CurRect = Rect.FromBounds(saved.Left, saved.Top, saved.Right, saved.Bottom);
        vs.OldRect = saved.OldLeft == -1 ? null : Rect.FromBounds(saved.OldLeft, saved.OldTop, saved.OldRight, saved.OldBottom);
        (vs.VerbId, vs.Color, vs.HiColor, vs.DimColor, vs.BackColor) = (saved.Id, saved.Color, saved.HiColor, saved.DimColor, saved.BkColor);
        (vs.Type, vs.Mode, vs.SaveId, vs.Key, vs.Center, vs.ImageObject) = (saved.Type, saved.Mode, saved.SaveId, saved.Key, saved.Center != 0, saved.ImageIndex);
        var resource = s.Resources.TryGetValue((ScummState.TypeVerb, index), out var r) ? r.Data : null;
        vs.Image = vs.Type == 1 ? resource : null;
        vs.Name = vs.Type != 1 && resource is not null ? Unterminated(resource) : null;
    }

    /// <summary>
    /// The resources ScummVM keeps outside the game files: carried objects' code, the strings, the
    /// verbs (written with their slots), actors' names, objects' new names, and the room's boxes
    /// with their flags as the scripts left them and its box matrix. Sounds are reloaded when
    /// started, so none is listed.
    /// </summary>
    private void SaveResources(ScummState s, Room? room)
    {
        for (int i = 0; i < InventorySize; i++)
        {
            int obj = _inventory[i];
            if (obj != 0 && _inventoryCode.TryGetValue(obj, out var code))
            {
                s.Resources[(ScummState.TypeInventory, i)] = new ScummState.Resource(code.ToArray(), obj);
            }
        }

        for (int i = 0; i < StringCount; i++)
        {
            if (_strings[i] is { } text)
            {
                s.Resources[(ScummState.TypeString, i)] = new ScummState.Resource([.. text]);
            }
        }

        int nameIndex = 0;
        foreach (var (obj, name) in _newNames)
        {
            s.Resources[(ScummState.TypeObjectName, nameIndex++)] = new ScummState.Resource([.. Terminated(name)], obj);
        }

        if (room is not null && room.BoxOffset != 0)
        {
            var data = room.Data.Span;
            int count = data[room.BoxOffset];
            int size = count * BoxSize + 1;
            var boxes = data.Slice(room.BoxOffset, size).ToArray();
            for (int i = 0; i < count && i < _boxFlags.Length; i++)
            {
                boxes[1 + i * BoxSize + 17] = (byte)_boxFlags[i];
            }

            s.Resources[(ScummState.TypeMatrix, 2)] = new ScummState.Resource(boxes);

            // ScummVM's own reckoning of the matrix: from the boxes' end to the picture in the EGA's
            // rooms, to the end of the boxes' block in those with small headers (its size less its 6-byte header)
            int matrix = room.SmallHeader
                ? BinaryPrimitives.ReadInt32LittleEndian(data[(room.BoxOffset - 6)..]) - 6 - size
                : room.ImageOffset - room.BoxOffset - size;
            if (matrix > 0)
            {
                s.Resources[(ScummState.TypeMatrix, 1)] = new ScummState.Resource(data.Slice(room.BoxOffset + size, matrix).ToArray());
            }
        }
    }

    /// <summary>The carried objects with their code, the strings and the new names, from a save's resources.</summary>
    private void RestoreResources(ScummState s)
    {
        Array.Clear(_inventory);
        _inventoryCode.Clear();
        _newNames.Clear();
        Array.Clear(_strings);
        foreach (var ((type, index), resource) in s.Resources)
        {
            switch (type)
            {
                case ScummState.TypeInventory when index < InventorySize && resource.Number != 0:
                    _inventory[index] = resource.Number;
                    _inventoryCode[resource.Number] = resource.Data;
                    break;
                case ScummState.TypeString when index < StringCount:
                    _strings[index] = resource.Data;
                    break;
                case ScummState.TypeObjectName when resource.Number != 0:
                    _newNames[resource.Number] = Unterminated(resource.Data);
                    break;
            }
        }
    }

    private static IEnumerable<byte> Terminated(byte[] text) => text.Length > 0 && text[^1] == 0 ? text : [.. text, 0];

    private static byte[] Unterminated(byte[] data)
    {
        int end = Array.IndexOf(data, (byte)0);
        return end < 0 ? data : data[..end];
    }
}
