namespace Distaff.Scumm.Saves;

/// <summary>
/// The engine state a ScummVM save holds for Loom, versions 3 and 4, field for field and in
/// ScummVM's order, so a save made here loads in ScummVM and one made there loads here. Mirrors
/// <c>ScummEngine::saveLoadWithSerializer</c> and <c>ScummEngine_v5::saveLoadWithSerializer</c>
/// with the structures they sync (<c>Actor</c>, <c>Actor_v3</c> for version 3 only,
/// <c>ScriptSlot</c>, <c>ObjectData</c>, <c>VerbSlot</c>, <c>NestedScript</c>,
/// <c>SentenceTab</c>, <c>StringTab</c>, <c>ColorCycle</c>), for the branches the DOS releases
/// take. Ported by way of Maniacal.
/// </summary>
/// <remarks>
/// Versions are ScummVM's save versions: 108 is what ScummVM 2.7 writes, 124 what it writes now.
/// Many fields are ScummVM's own bookkeeping; those this interpreter does not keep are written as
/// ScummVM would have them and ignored when read.
/// </remarks>
internal sealed class ScummState
{
    // the sizes ScummVM gives version 3 (ScummEngine_v4::readMAXS, allocateArrays, _numActors)
    public const int ActorCount = 13, SlotCount = 80, LocalVariables = 26, LocalObjectCount = 200, VerbCount = 100,
        SentenceCount = 6, StringCount = 6, ColorCycleCount = 16, ScaleSlotCount = 20, VariableCount = 800,
        BitVariableBytes = 4096 / 8, NewNameCount = 50, InventoryCount = 80;

    // ResType values
    public const int TypeSound = 4, TypeInventory = 5, TypeString = 7, TypeVerb = 8, TypeActorName = 9, TypeBuffer = 10,
        TypeScaleTable = 11, TypeTemp = 12, TypeFlObject = 13, TypeMatrix = 14, TypeBox = 15, TypeObjectName = 16, TypeSpoolBuffer = 21;

    /// <summary>The resource types a save lists: those not read from the game files, without the temporary ones.</summary>
    public static readonly int[] SavedTypes = [TypeSound, TypeInventory, TypeString, TypeVerb, TypeActorName, TypeScaleTable, TypeFlObject, TypeMatrix, TypeBox, TypeObjectName, TypeSpoolBuffer];

    /// <param name="globalObjects">How many objects the game has.</param>
    /// <param name="gameVersion">The game's SCUMM version, 3 or 4: version 3's actors save four more fields.</param>
    /// <param name="fmTowns">Whether the game is the FM Towns', whose screen saves more.</param>
    public ScummState(int globalObjects, int gameVersion = 3, bool fmTowns = false)
    {
        GlobalObjects = globalObjects;
        GameVersion = gameVersion;
        FmTowns = fmTowns;
        ObjectOwners = new byte[globalObjects];
        ObjectStates = new byte[globalObjects];
        ClassData = new int[globalObjects];
        for (int i = 0; i < ActorCount; i++)
        {
            Actors[i] = new ActorState();
        }

        for (int i = 0; i < SlotCount; i++)
        {
            Slots[i] = new SlotState();
        }

        for (int i = 0; i < LocalObjectCount; i++)
        {
            Objects[i] = new ObjectState();
        }

        for (int i = 0; i < VerbCount; i++)
        {
            Verbs[i] = new VerbState();
        }

        for (int i = 0; i < Nested.Length; i++)
        {
            Nested[i] = new NestedState();
        }

        for (int i = 0; i < SentenceCount; i++)
        {
            Sentences[i] = new SentenceState();
        }

        for (int i = 0; i < StringCount; i++)
        {
            Strings[i] = new StringState();
        }
    }

    public int GlobalObjects { get; }

    public int GameVersion { get; }

    /// <summary>Whether the save is the FM Towns release's.</summary>
    public bool FmTowns { get; }

    public byte[] GameMd5 = new byte[16];
    public int CurrentPaletteIndex, CurrentRoom, RoomResource, ObjectsInRoom, CurrentScript = 0xFF;
    public int[] LocalVars = new int[SlotCount * LocalVariables];
    public byte[] ResourceMapper = new byte[128];
    public byte[] CharsetColorMap = new byte[16];
    public byte[] CharsetData = new byte[23 * 16];

    public int CameraDestX, CameraDestY, CameraX, CameraY, CameraLastX, CameraLastY, CameraAccelX, CameraAccelY;
    public int ScreenStartStrip, ScreenEndStrip, CameraMode, CameraFollows, CameraLeftTrigger = 10, CameraRightTrigger = 30, CameraMovingToActor;
    public int CameraIsFrozen, MacScreenCorrectHeight;

    public int ActorToPrintStrFor, CharsetColor, CharsetBufPos, HaveMessage, HaveActorSpeechMessage, UseTalkAnims;
    public int TalkDelay, DefaultTextSpeed = 6, SentenceNumber;

    public int CutSceneStackPointer;
    public int[] CutScenePtr = new int[5];
    public byte[] CutSceneScript = new byte[5];
    public int[] CutSceneData = new int[5];
    public int CutSceneScriptIndex = 0xFF;

    public int NestedScripts, UserPut, UserState, CursorState, CurrentCursor;
    public byte[] GrabbedCursor = new byte[8192];
    public int CursorWidth, CursorHeight, CursorHotspotX, CursorHotspotY, CursorAnimate, CursorAnimateIndex, MouseX, MouseY;

    public byte[] ColorUsedByCycle = new byte[256];
    public int DoEffect, SwitchRoomEffect, NewEffect, SwitchRoomEffect2, BackgroundNeedsRedraw;
    public int PalManipStart, PalManipEnd, PalManipCounter;
    public int[] GfxUsageBits = new int[3 * 410];
    public byte[] CurrentPalette = new byte[768];
    public byte[] DarkenPalette = new byte[768];
    public byte[] CharsetBuffer = new byte[256];
    public int EgoPositioned, ScreenEffectFlag, ShakeEnabled, ShakeFrame, KeepText, ScreenB, ScreenH, NesCostumeSet;

    public ActorState[] Actors = new ActorState[ActorCount];

    public int CurrentCdSound, CurrentMusic;

    public SlotState[] Slots = new SlotState[SlotCount];
    public ObjectState[] Objects = new ObjectState[LocalObjectCount];
    public VerbState[] Verbs = new VerbState[VerbCount];
    public NestedState[] Nested = new NestedState[40];
    public SentenceState[] Sentences = new SentenceState[SentenceCount];
    public StringState[] Strings = new StringState[StringCount];
    public int[] ColorCycles = new int[ColorCycleCount * 5];
    public int[] ScaleSlots = new int[ScaleSlotCount * 6];

    /// <summary>The resources the save carries, by type and index: their bytes, and for inventory and new names the object number.</summary>
    public SortedDictionary<(int Type, int Index), Resource> Resources = [];

    public byte[] ObjectOwners;
    public byte[] ObjectStates;
    public byte[] ShadowPalette = new byte[256];

    // the FM Towns' screen: its text layer's colours, the colour-cycling fields, the text's place,
    // the classic charset's colours, the shadow colour, and the palette, layer and clearing flags
    public bool HasTownsData;
    public byte[] TownsTextPalette = new byte[48];
    public int[] TownsCycleRects = new int[10 * 4];
    public int[] TownsCurStringRect = [-1, -1, -1, -1];
    public byte[] TownsCharsetColorMap = new byte[16];
    public int TownsOverrideShadowColor, TownsCycleRectCount, TownsPaletteFlags, TownsClearLayerFlag = 1, TownsActiveLayerFlags = 3;
    public byte[] RoomPalette = new byte[256];
    public byte[] PalManipPalette = new byte[0x300];
    public byte[] PalManipIntermediatePalette = new byte[0x600];
    public int[] ClassData;
    public int[] Variables = new int[VariableCount];
    public byte[] BitVariables = new byte[BitVariableBytes];
    public List<(int Type, int Index)> Locked = [];
    public int[] AudioCdStatus = new int[5];
    public byte[] SoundEngineData = [];

    /// <summary>The version the state was read at, which the sound engine's state is read at too.</summary>
    public int SaveVersion = SaveFile.CurrentVersion;
    public int CharsetId, CharsetRendererColor;

    /// <summary>The four cursor pictures, 16 rows of 16 bits each, and their hotspots as x and y bytes (<c>_cursorImages</c>, <c>_cursorHotspots</c>).</summary>
    public int[] CursorImages = new int[4 * 16];
    public byte[] CursorHotspots = new byte[8];

    /// <summary>A resource the save carries. Mirrors <c>saveResource</c> for dynamic resources; a sound carries nothing but its number.</summary>
    public sealed record Resource(byte[] Data, int Number = 0);

    /// <summary>Reads or writes the whole state. Mirrors <c>ScummEngine::saveLoadWithSerializer</c>, then <c>ScummEngine_v5</c>'s.</summary>
    public void Sync(SaveSerializer s)
    {
        if (s.IsLoading)
        {
            SaveVersion = s.Version;
        }

        s.Bytes(GameMd5, 16, 39);
        s.Skip(2, 8, 50);
        s.Skip(2, 8, 50);
        s.Skip(4, 8, 50);
        s.Skip(4, 8, 50);
        s.Skip(4, 8, 50);
        s.Skip(4, 8, 50);
        s.Skip(4, 8, 9);
        s.Skip(4, 8, 50);
        s.Byte(ref CurrentPaletteIndex, 8);
        s.Byte(ref CurrentRoom, 8);
        s.Byte(ref RoomResource, 8);
        s.Byte(ref ObjectsInRoom, 8);
        s.Byte(ref CurrentScript, 8);
        RequireModern(s);

        s.Array(LocalVars, SlotCount * LocalVariables, (x, i) => x.Uint32(ref LocalVars[i]), 20);
        s.Bytes(ResourceMapper, 128, 8);
        s.Bytes(CharsetColorMap, 16, 8);
        s.Bytes(CharsetData, 10 * 16, 8);
        if (s.Has(10))
        {
            var more = new byte[5 * 16];
            Array.Copy(CharsetData, 10 * 16, more, 0, more.Length);
            s.Bytes(more, more.Length, 10);
            Array.Copy(more, 0, CharsetData, 10 * 16, more.Length);
        }

        if (s.Has(67))
        {
            var more = new byte[8 * 16];
            Array.Copy(CharsetData, 15 * 16, more, 0, more.Length);
            s.Bytes(more, more.Length, 67);
            Array.Copy(more, 0, CharsetData, 15 * 16, more.Length);
        }

        s.Skip(2, 8, 62);

        s.Int16(ref CameraDestX, 8);
        s.Int16(ref CameraDestY, 8);
        s.Int16(ref CameraX, 8);
        s.Int16(ref CameraY, 8);
        s.Int16(ref CameraLastX, 8);
        s.Int16(ref CameraLastY, 8);
        s.Int16(ref CameraAccelX, 8);
        s.Int16(ref CameraAccelY, 8);
        s.Int16(ref ScreenStartStrip, 8);
        s.Int16(ref ScreenEndStrip, 8);
        s.Byte(ref CameraMode, 8);
        s.Byte(ref CameraFollows, 8);
        s.Int16(ref CameraLeftTrigger, 8);
        s.Int16(ref CameraRightTrigger, 8);
        s.Uint16(ref CameraMovingToActor, 8);
        s.Byte(ref CameraIsFrozen, 108);
        s.Skip(2, 112, 121);
        s.Byte(ref MacScreenCorrectHeight, 112);

        s.Byte(ref ActorToPrintStrFor, 8);
        s.Byte(ref CharsetColor, 8);
        s.Byte(ref CharsetBufPos, 8, 9);
        s.Int16(ref CharsetBufPos, 10);
        s.Byte(ref HaveMessage, 8);
        s.Byte(ref HaveActorSpeechMessage, 61);
        s.Byte(ref UseTalkAnims, 8);
        s.Int16(ref TalkDelay, 8);
        s.Int16(ref DefaultTextSpeed, 8);
        s.Skip(2, 8, 27);
        s.Byte(ref SentenceNumber, 8);

        s.Byte(ref CutSceneStackPointer, 8);
        s.Array(CutScenePtr, 5, (x, i) => x.Uint32(ref CutScenePtr[i]), 8);
        s.Bytes(CutSceneScript, 5, 8);
        s.Array(CutSceneData, 5, (x, i) => x.Int16(ref CutSceneData[i]), 8);
        s.Int16(ref CutSceneScriptIndex, 8);

        s.Byte(ref NestedScripts, 8);
        s.Byte(ref UserPut, 8);
        s.Uint16(ref UserState, 17);
        s.Byte(ref CursorState, 8);
        s.Skip(1, 8, 20);
        s.Byte(ref CurrentCursor, 8);
        s.Bytes(GrabbedCursor, 8192, 20);
        s.Int16(ref CursorWidth, 20);
        s.Int16(ref CursorHeight, 20);
        s.Int16(ref CursorHotspotX, 20);
        s.Int16(ref CursorHotspotY, 20);
        s.Byte(ref CursorAnimate, 20);
        s.Byte(ref CursorAnimateIndex, 20);
        s.Int16(ref MouseX, 20);
        s.Int16(ref MouseY, 20);

        s.Bytes(ColorUsedByCycle, 256, 60);
        s.Byte(ref DoEffect, 8);
        s.Byte(ref SwitchRoomEffect, 8);
        s.Byte(ref NewEffect, 8);
        s.Byte(ref SwitchRoomEffect2, 8);
        s.Byte(ref BackgroundNeedsRedraw, 8);
        s.Byte(ref PalManipStart, 10);
        s.Byte(ref PalManipEnd, 10);
        s.Uint16(ref PalManipCounter, 10);
        s.Array(GfxUsageBits, 3 * 410, (x, i) => x.Uint32(ref GfxUsageBits[i]), 14);
        s.Skip(1, 8, 50);
        s.Bytes(CurrentPalette, 768, 8);
        s.Bytes(DarkenPalette, 768, 53);
        s.Skip(256, 8, 33);
        s.Bytes(CharsetBuffer, 256, 8);
        s.Byte(ref EgoPositioned, 8);
        s.Skip(2 * 4, 8, 9);
        s.Skip(2 * 5, 10, 26);
        s.Skip(1, 8, 26);
        s.Byte(ref ScreenEffectFlag, 8);
        s.Skip(4, 8, 9);
        s.Skip(4, 8, 9);
        s.Int16(ref ShakeEnabled, 8, 9);
        s.Byte(ref ShakeEnabled, 10);
        s.Uint32(ref ShakeFrame, 10);
        s.Byte(ref KeepText, 8);
        s.Uint16(ref ScreenB, 8);
        s.Uint16(ref ScreenH, 8);
        s.Uint16(ref NesCostumeSet, 47);
        s.Skip(2, 9, 9);
        s.Skip(2, 9, 9);
        s.Skip(2, 9, 9);
        s.Skip(2, 9, 9);

        foreach (var actor in Actors)
        {
            actor.Sync(s, GameVersion);
        }

        // Sound::saveLoadWithSerializer
        s.Int16(ref CurrentCdSound, 35);
        s.Int16(ref CurrentMusic, 35);

        foreach (var slot in Slots)
        {
            slot.Sync(s);
        }

        foreach (var obj in Objects)
        {
            obj.Sync(s);
        }

        foreach (var verb in Verbs)
        {
            verb.Sync(s);
        }

        int nested = s.Has(120) ? 40 : 16;
        for (int i = 0; i < nested; i++)
        {
            Nested[i].Sync(s);
        }

        foreach (var sentence in Sentences)
        {
            sentence.Sync(s);
        }

        foreach (var text in Strings)
        {
            text.Sync(s);
        }

        for (int i = 0; i < ColorCycleCount; i++)
        {
            s.Uint16(ref ColorCycles[i * 5], 8);
            s.Uint16(ref ColorCycles[i * 5 + 1], 8);
            s.Uint16(ref ColorCycles[i * 5 + 2], 8);
            s.Byte(ref ColorCycles[i * 5 + 3], 8);
            s.Byte(ref ColorCycles[i * 5 + 4], 8);
        }

        s.Array(ScaleSlots, ScaleSlotCount * 6, (x, i) => x.Uint16(ref ScaleSlots[i]), 13);

        SyncResources(s);

        s.Bytes(ObjectOwners, GlobalObjects);
        s.Bytes(ObjectStates, GlobalObjects);
        if (FmTowns)
        {
            SyncTowns(s);
        }

        // the shadow palette (_shadowPaletteSize is 256 from ScummEngine_v4::readMAXS), then for versions below 5 the room palette
        s.Bytes(ShadowPalette, 256);
        s.Bytes(RoomPalette, 256, 21);
        if (PalManipCounter != 0)
        {
            s.Bytes(PalManipPalette, 0x300);
            s.Bytes(PalManipIntermediatePalette, 0x600);
        }

        s.Array(ClassData, GlobalObjects, (x, i) => x.Uint32(ref ClassData[i]));

        // no room variables in these versions; the variables have been 32 bits since version 15
        s.Array(Variables, VariableCount, (x, i) => x.Int32(ref Variables[i]), 15);
        s.Bytes(BitVariables, BitVariableBytes);

        SyncLocked(s);
        for (int i = 0; i < 5; i++)
        {
            s.Int32(ref AudioCdStatus[i], 24);
        }

        SyncSoundEngine(s);

        // CharsetRenderer::saveLoadWithSerializer
        s.Byte(ref CharsetId, 73, 73);
        s.Int32(ref CharsetId, 74);
        s.Byte(ref CharsetRendererColor, 73);

        // ScummEngine_v5::saveLoadWithSerializer: Loom's cursors
        s.Array(CursorImages, 4 * 16, (x, i) => x.Uint16(ref CursorImages[i]), 44);
        s.Bytes(CursorHotspots, 8, 44);
    }

    /// <summary>The oldest save version read: the layouts before it (local variables, resources) are not ported.</summary>
    public const int OldestVersion = 108;

    private static void RequireModern(SaveSerializer s)
    {
        if (s.IsLoading && s.Version < OldestVersion)
        {
            throw new InvalidDataException($"Save version {s.Version} is older than the oldest supported, {OldestVersion} (ScummVM 2.7).");
        }
    }

    /// <summary>
    /// Each listed type, then each of its resources by index, ended by 0xFFFF; then 0xFFFF. A dynamic
    /// resource is its size and bytes, with the object number for inventory and new names. Mirrors
    /// the resource part of <c>saveLoadWithSerializer</c>, <c>saveResource</c> and <c>loadResource</c>.
    /// </summary>
    private void SyncResources(SaveSerializer s)
    {
        const int End = 0xFFFF;
        if (s.IsSaving)
        {
            foreach (int type in SavedTypes)
            {
                int t = type;
                s.Uint16(ref t);
                foreach (var ((resourceType, index), resource) in Resources)
                {
                    if (resourceType != type)
                    {
                        continue;
                    }

                    int i = index;
                    s.Uint16(ref i);
                    WriteResource(s, type, resource);
                }

                int end = End;
                s.Uint16(ref end);
            }

            int last = End;
            s.Uint16(ref last);
            return;
        }

        Resources.Clear();
        int typeRead = 0;
        while (true)
        {
            s.Uint16(ref typeRead);
            if (typeRead == End)
            {
                break;
            }

            int index = 0;
            while (true)
            {
                s.Uint16(ref index);
                if (index == End)
                {
                    break;
                }

                Resources[(typeRead, index)] = ReadResource(s, typeRead);
            }
        }
    }

    private static void WriteResource(SaveSerializer s, int type, Resource resource)
    {
        if (type == TypeSound)
        {
            return;
        }

        int size = resource.Data.Length;
        s.Uint32(ref size);
        s.Bytes(resource.Data, size);
        if (type is TypeInventory or TypeObjectName)
        {
            int number = resource.Number;
            s.Uint16(ref number);
        }
    }

    private static Resource ReadResource(SaveSerializer s, int type)
    {
        if (type == TypeSound)
        {
            return new Resource([]);
        }

        int size = 0;
        s.Uint32(ref size);
        var data = new byte[size];
        s.Bytes(data, size);
        int number = 0;
        if (type is TypeInventory or TypeObjectName)
        {
            s.Uint16(ref number);
        }

        return new Resource(data, number);
    }

    /// <summary>The locked resources, a type byte and index word each, ended by 0xFF.</summary>
    private void SyncLocked(SaveSerializer s)
    {
        if (s.IsSaving)
        {
            foreach (var (type, index) in Locked)
            {
                int t = type, i = index;
                s.Byte(ref t);
                s.Uint16(ref i);
            }

            int end = 0xFF;
            s.Byte(ref end);
            return;
        }

        Locked.Clear();
        int typeRead = 0;
        while (true)
        {
            s.Byte(ref typeRead);
            if (typeRead == 0xFF)
            {
                break;
            }

            int index = 0;
            s.Uint16(ref index);
            Locked.Add((typeRead, index));
        }
    }

    /// <summary>
    /// The music engine's block: from version 124 its size comes first; before that it is whatever
    /// lies between here and the fixed-size tail (the charset state and the cursors). The PC
    /// speaker's player saves nothing; the AdLib player's block is kept as it is. Mirrors the sound
    /// part of <c>saveLoadWithSerializer</c> and <c>checkSoundEngineSaveDataSize</c>.
    /// </summary>
    /// <summary>
    /// The FM Towns' screen, after a byte that says it is there (version 88 on). The 16-bit palette
    /// is not saved for the FM Towns from version 88. Mirrors the FM Towns part of
    /// <c>ScummEngine::saveLoadWithSerializer</c>; a <c>Common::Rect</c> is its left, top, right
    /// and bottom.
    /// </summary>
    private void SyncTowns(SaveSerializer s)
    {
        int has = HasTownsData ? 1 : 0;
        s.Byte(ref has);
        HasTownsData = has != 0;
        if (!HasTownsData)
        {
            return;
        }

        s.Bytes(TownsTextPalette, 48);
        s.Array(TownsCycleRects, TownsCycleRects.Length, (x, i) => x.Int16(ref TownsCycleRects[i]));
        s.Array(TownsCurStringRect, 4, (x, i) => x.Int16(ref TownsCurStringRect[i]));
        s.Bytes(TownsCharsetColorMap, 16);
        s.Byte(ref TownsOverrideShadowColor);
        s.Byte(ref TownsCycleRectCount);
        s.Byte(ref TownsPaletteFlags);
        s.Byte(ref TownsClearLayerFlag);
        s.Byte(ref TownsActiveLayerFlags);
    }

    private void SyncSoundEngine(SaveSerializer s)
    {
        int size = SoundEngineData.Length;
        if (s.IsLoading)
        {
            if (s.Has(124))
            {
                s.Int32BigEndian(ref size);
            }
            else
            {
                int tail = (s.Has(74) ? 4 : s.Has(73) ? 1 : 0) + (s.Has(73) ? 1 : 0) + (s.Has(44) ? 4 * 16 * 2 + 8 : 0);

                size = (int)(s.Length - s.Position) - tail;
            }

            SoundEngineData = new byte[Math.Max(0, size)];
            s.Bytes(SoundEngineData, SoundEngineData.Length);
            return;
        }

        s.Int32BigEndian(ref size, 124);
        s.Bytes(SoundEngineData, size);
    }

    /// <summary>An actor. Mirrors <c>Actor::saveLoadWithSerializer</c>, then for a version 3 game <c>Actor_v3::saveLoadWithSerializer</c> at version 3's revision.</summary>
    public sealed class ActorState
    {
        public int X, Y, HeOffsX, HeOffsY, Top, Bottom, Elevation, Width = 24, Facing = 180, Costume, Room, TalkColor = 15;
        public int TalkFrequency = 256, TalkPan = 64, TalkVolume = 127, BoxScale = 0xFF, ScaleX = 0xFF, ScaleY = 0xFF, Charset;
        public int[] Sound = new int[32];
        public int[] AnimVariable = new int[27];
        public int TargetFacing = 180, Moving, IgnoreBoxes, ForceClip, InitFrame = 2, WalkFrame, StandFrame = 1, TalkStartFrame = 5, TalkStopFrame = 4;
        public int SpeedX = 1, SpeedY = 1, AnimCounter, SoundCounter, DrawToBackBuf, Flip, HeSkipLimbs;
        public int[] Palette = new int[256];
        public int ShadowMode, Visible, Frame, AnimSpeed, AnimProgress, Walkbox, NeedRedraw, NeedBgReset, CostumeNeedsInit;
        public int HeCondMask, HePaletteNum, HeShadow, TalkPosY = -80, TalkPosX, IgnoreTurns, Layer, TalkScript, WalkScript;
        public int DestX, DestY, DestBox, DestDir, CurBox, CurX, CurY, NextX, NextY, DeltaXFactor, DeltaYFactor, XFrac, YFrac, WalkFacing;
        public int Point3X = 32000, Point3Y;
        public byte[] AnimType = new byte[16];
        public int Stopped;
        public int[] CurPos = Filled(16, 0xFFFF), Start = Filled(16, 0xFFFF), End = Filled(16, 0xFFFF), CostumeFrame = Filled(16, 0xFFFF);
        public int[] HeJumpOffsets = new int[16], HeJumpCounts = new int[16], HeCondMasks = new int[16];
        public int XAdd, YAdd, StepX = 1, StepThreshold;

        public void Sync(SaveSerializer s, int gameVersion = 3)
        {
            s.Int16(ref X, 8);
            s.Int16(ref Y, 8);
            s.Int16(ref HeOffsX, 32);
            s.Int16(ref HeOffsY, 32);
            s.Int16(ref Top, 8);
            s.Int16(ref Bottom, 8);
            s.Int16(ref Elevation, 8);
            s.Uint16(ref Width, 8);
            s.Uint16(ref Facing, 8);
            s.Uint16(ref Costume, 8);
            s.Byte(ref Room, 8);
            s.Byte(ref TalkColor, 8);
            s.Int16(ref TalkFrequency, 16);
            s.Int16(ref TalkPan, 24);
            s.Int16(ref TalkVolume, 29);
            s.Uint16(ref BoxScale, 34);
            s.Byte(ref ScaleX, 8);
            s.Byte(ref ScaleY, 8);
            s.Byte(ref Charset, 8);
            s.Array(Sound, 32, (x, i) => x.Uint16(ref Sound[i]), 62);
            s.Array(AnimVariable, 27, (x, i) => x.Uint16(ref AnimVariable[i]), 41);
            s.Uint16(ref TargetFacing, 8);
            s.Byte(ref Moving, 8);
            s.Byte(ref IgnoreBoxes, 8);
            s.Byte(ref ForceClip, 8);
            s.Byte(ref InitFrame, 8);
            s.Byte(ref WalkFrame, 8);
            s.Byte(ref StandFrame, 8);
            s.Byte(ref TalkStartFrame, 8);
            s.Byte(ref TalkStopFrame, 8);
            s.Uint16(ref SpeedX, 8);
            s.Uint16(ref SpeedY, 8);
            s.Uint16(ref AnimCounter, 8);
            s.Byte(ref SoundCounter, 8);
            s.Byte(ref DrawToBackBuf, 32);
            s.Byte(ref Flip, 32);
            s.Byte(ref HeSkipLimbs, 32);
            s.Array(Palette, 256, (x, i) => x.Uint16(ref Palette[i]), 80);
            s.Skip(1, 8, 9);
            s.Byte(ref ShadowMode, 8);
            s.Byte(ref Visible, 8);
            s.Byte(ref Frame, 8);
            s.Byte(ref AnimSpeed, 8);
            s.Byte(ref AnimProgress, 8);
            s.Byte(ref Walkbox, 8);
            s.Byte(ref NeedRedraw, 8);
            s.Byte(ref NeedBgReset, 8);
            s.Byte(ref CostumeNeedsInit, 8);
            s.Uint32(ref HeCondMask, 38);
            s.Uint32(ref HePaletteNum, 59);
            s.Uint32(ref HeShadow, 59);
            s.Int16(ref TalkPosY, 8);
            s.Int16(ref TalkPosX, 8);
            s.Byte(ref IgnoreTurns, 8);
            s.Byte(ref Layer, 8, 57);
            s.Int32(ref Layer, 58);
            s.Uint16(ref TalkScript, 8);
            s.Uint16(ref WalkScript, 8);
            s.Int16(ref DestX, 8);
            s.Int16(ref DestY, 8);
            s.Byte(ref DestBox, 8);
            s.Uint16(ref DestDir, 8);
            s.Byte(ref CurBox, 8);
            s.Int16(ref CurX, 8);
            s.Int16(ref CurY, 8);
            s.Int16(ref NextX, 8);
            s.Int16(ref NextY, 8);
            s.Int32(ref DeltaXFactor, 8);
            s.Int32(ref DeltaYFactor, 8);
            s.Uint16(ref XFrac, 8);
            s.Uint16(ref YFrac, 8);
            s.Int16(ref WalkFacing, 111);
            s.Uint16(ref Point3X, 42);
            s.Uint16(ref Point3Y, 42);
            s.Bytes(AnimType, 16, 8);
            s.Uint16(ref Stopped, 8);
            s.Array(CurPos, 16, (x, i) => x.Uint16(ref CurPos[i]), 8);
            s.Array(Start, 16, (x, i) => x.Uint16(ref Start[i]), 8);
            s.Array(End, 16, (x, i) => x.Uint16(ref End[i]), 8);
            s.Array(CostumeFrame, 16, (x, i) => x.Uint16(ref CostumeFrame[i]), 8);
            s.Array(HeJumpOffsets, 16, (x, i) => x.Uint16(ref HeJumpOffsets[i]), 65);
            s.Array(HeJumpCounts, 16, (x, i) => x.Uint16(ref HeJumpCounts[i]), 65);
            s.Array(HeCondMasks, 16, (x, i) => x.Uint32(ref HeCondMasks[i]), 65);

            // Actor_v3, at version 3's revision; version 4's actors are Actor's
            if (gameVersion != 3)
            {
                return;
            }

            s.Uint16(ref XAdd, 101);
            s.Uint16(ref YAdd, 101);
            s.Uint16(ref StepX, 101);
            s.Uint16(ref StepThreshold, 101);
        }
    }

    /// <summary>Mirrors <c>syncWithSerializer(ScriptSlot)</c>.</summary>
    public sealed class SlotState
    {
        public int Offset, Delay, Number, DelayFrameCount, Status, Where, FreezeResistant, Recursive, FreezeCount, DidExec, CutsceneOverride, Cycle;

        public void Sync(SaveSerializer s)
        {
            s.Uint32(ref Offset, 8);
            s.Int32(ref Delay, 8);
            s.Uint16(ref Number, 8);
            s.Uint16(ref DelayFrameCount, 8);
            s.Byte(ref Status, 8);
            s.Byte(ref Where, 8);
            s.Byte(ref FreezeResistant, 8);
            s.Byte(ref Recursive, 8);
            s.Byte(ref FreezeCount, 8);
            s.Byte(ref DidExec, 8);
            s.Byte(ref CutsceneOverride, 8);
            s.Byte(ref Cycle, 46);
            s.Skip(1, 8, 10);
        }
    }

    /// <summary>Mirrors <c>syncWithSerializer(ObjectData)</c>.</summary>
    public sealed class ObjectState
    {
        public int ImageOffset, CodeOffset, WalkX, WalkY, Number, X, Y, Width, Height, ActorDir, ParentState, Parent, State, FlObjectIndex, Flags;

        public void Sync(SaveSerializer s)
        {
            s.Uint32(ref ImageOffset, 8);
            s.Uint32(ref CodeOffset, 8);
            s.Uint16(ref WalkX, 8);
            s.Uint16(ref WalkY, 8);
            s.Uint16(ref Number, 8);
            s.Int16(ref X, 8);
            s.Int16(ref Y, 8);
            s.Uint16(ref Width, 8);
            s.Uint16(ref Height, 8);
            s.Byte(ref ActorDir, 8);
            s.Byte(ref ParentState, 8);
            s.Byte(ref Parent, 8);
            s.Byte(ref State, 8);
            s.Byte(ref FlObjectIndex, 8);
            s.Byte(ref Flags, 46);
        }
    }

    /// <summary>Mirrors <c>syncWithSerializer(VerbSlot)</c> for versions below 7.</summary>
    public sealed class VerbState
    {
        public int Left, Top, Right = 319, Bottom, OldLeft = -1, OldTop, OldRight, OldBottom, Id, Color = 2, HiColor, DimColor, BkColor;
        public int Type, Charset = 1, Mode, SaveId, Key, Center, Prep, ImageIndex;

        public void Sync(SaveSerializer s)
        {
            s.Int16(ref Left, 8);
            s.Int16(ref Top, 8);
            s.Int16(ref Right, 8);
            s.Int16(ref Bottom, 8);
            s.Int16(ref OldLeft, 8);
            s.Int16(ref OldTop, 8);
            s.Int16(ref OldRight, 8);
            s.Int16(ref OldBottom, 8);
            s.Byte(ref Id, 8, 11);
            s.Int16(ref Id, 12);
            s.Byte(ref Color, 8);
            s.Byte(ref HiColor, 8);
            s.Byte(ref DimColor, 8);
            s.Byte(ref BkColor, 8);
            s.Byte(ref Type, 8);
            s.Byte(ref Charset, 8);
            s.Byte(ref Mode, 8);
            s.Byte(ref SaveId, 8);
            s.Byte(ref Key, 8);
            s.Byte(ref Center, 8);
            s.Byte(ref Prep, 8);
            s.Uint16(ref ImageIndex, 8);
        }
    }

    /// <summary>Mirrors <c>syncWithSerializer(NestedScript)</c>.</summary>
    public sealed class NestedState
    {
        public int Number, Where, Slot;

        public void Sync(SaveSerializer s)
        {
            s.Uint16(ref Number, 8);
            s.Byte(ref Where, 8);
            s.Byte(ref Slot, 8);
        }
    }

    /// <summary>Mirrors <c>syncWithSerializer(SentenceTab)</c>.</summary>
    public sealed class SentenceState
    {
        public int Verb, Preposition, ObjectA, ObjectB, FreezeCount;

        public void Sync(SaveSerializer s)
        {
            s.Byte(ref Verb, 8);
            s.Byte(ref Preposition, 8);
            s.Uint16(ref ObjectA, 8);
            s.Uint16(ref ObjectB, 8);
            s.Byte(ref FreezeCount, 8);
        }
    }

    /// <summary>Mirrors <c>syncWithSerializer(StringTab)</c>: each field and its default.</summary>
    public sealed class StringState
    {
        public int X, DefaultX, Y, DefaultY, Right, DefaultRight = 319, Color, DefaultColor = 15, Charset, DefaultCharset;
        public int Center, DefaultCenter, Overhead, DefaultOverhead, NoTalkAnim, DefaultNoTalkAnim, Wrapping, DefaultWrapping;

        public void Sync(SaveSerializer s)
        {
            s.Int16(ref X, 8);
            s.Int16(ref DefaultX, 8);
            s.Int16(ref Y, 8);
            s.Int16(ref DefaultY, 8);
            s.Int16(ref Right, 8);
            s.Int16(ref DefaultRight, 8);
            s.SByte(ref Color, 8);
            s.SByte(ref DefaultColor, 8);
            s.SByte(ref Charset, 8);
            s.SByte(ref DefaultCharset, 8);
            s.Byte(ref Center, 8);
            s.Byte(ref DefaultCenter, 8);
            s.Byte(ref Overhead, 8);
            s.Byte(ref DefaultOverhead, 8);
            s.Byte(ref NoTalkAnim, 8);
            s.Byte(ref DefaultNoTalkAnim, 8);
            s.Byte(ref Wrapping, 71);
            s.Byte(ref DefaultWrapping, 71);
        }
    }

    private static int[] Filled(int count, int value)
    {
        var values = new int[count];
        System.Array.Fill(values, value);
        return values;
    }
}
