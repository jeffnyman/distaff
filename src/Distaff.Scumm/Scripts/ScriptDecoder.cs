namespace Distaff.Scumm.Scripts;

/// <summary>
/// Decodes script instructions: which handler an opcode runs, and the operands and sub-operations
/// it fetches. The layouts mirror the fetches of ScummVM's <c>o5_</c>, <c>o4_</c> and <c>o3_</c>
/// handlers as <c>ScummEngine_v3old</c> runs them for the EGA release (version 3, with
/// <c>GF_SMALL_HEADER</c>, not on the PC Engine) and <c>ScummEngine_v4</c> for the VGA CD. Version 4
/// has <c>matrixOps</c> where version 3 has <c>setBoxFlags</c>, and no <c>waitForActor</c> or
/// <c>waitForSentence</c>; its <c>roomOps</c> fetches the operands after the sub-operation, and its
/// <c>print</c> left-aligns where version 3 sets a height.
/// </summary>
/// <remarks>
/// <para>Operand kinds (<c>ScummEngine_v5::getVar</c>, <c>getVarOrDirectByte</c>,
/// <c>getVarOrDirectWord</c>, <c>getResultPos</c>, <c>jumpRelative</c>, and
/// <c>ScummEngine::readVar</c>): a variable is a word, followed by a second word when it has bit
/// 0x2000, which is a constant to add to it, or with 0x2000 of its own a variable whose value is
/// added. A "variable or byte" or "variable or word" is a variable when the opcode (or, inside a
/// list, the sub-operation byte) has the operand's bit (0x80, 0x40, 0x20 for the first three),
/// otherwise a byte or a signed word. A result is a variable. A jump is a signed word added to
/// the position after it.</para>
/// <para>Text is bytes up to a zero; 0xFF takes the next byte, and two more unless it is 1, 2,
/// 3 or 8 (<c>resStrLen</c>). The file names of <c>roomOps</c> are plain bytes up to a zero.</para>
/// <para>Two layouts depend on values: <c>doSentence</c> stops after its verb when that is the
/// constant 0xFE (when it is a variable, the full layout is assumed), and <c>expression</c>'s
/// sub-operation 6 runs a whole instruction of its own.</para>
/// </remarks>
public sealed class ScriptDecoder(int version = 3)
{
    private const byte Param1 = 0x80, Param2 = 0x40, Param3 = 0x20;

    private readonly string?[] _handlers = version switch
    {
        3 => OpcodeTable.Handlers,
        4 => OpcodeTable.HandlersV4,
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Only versions 3 and 4 are decoded."),
    };

    /// <summary>The SCUMM version whose layouts are decoded: 3 or 4.</summary>
    public int Version { get; } = version;

    /// <summary>The sub-operation numbers of <c>actorOps</c> in small-header games. Mirrors <c>convertTable</c> in <c>o5_actorOps</c>.</summary>
    private static readonly byte[] ActorOpsConvertTable = [1, 0, 0, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 20];

    /// <summary>The ScummVM handler an opcode runs.</summary>
    public string HandlerOf(byte opcode)
        => _handlers[opcode] ?? throw new InvalidDataException($"Opcode 0x{opcode:X2} has no handler.");

    public Instruction Decode(ReadOnlySpan<byte> code, int offset)
    {
        var r = new Reader(code, offset);
        return Decode(ref r);
    }

    /// <summary>Decodes instructions one after another from <paramref name="start"/> to the end of the code.</summary>
    public IEnumerable<Instruction> DecodeAll(ReadOnlyMemory<byte> code, int start = 0)
    {
        for (int offset = start; offset < code.Length;)
        {
            var instruction = Decode(code.Span, offset);
            yield return instruction;
            offset = instruction.End;
        }
    }

    /// <summary>
    /// Decodes the instructions execution can reach from the entry points: each instruction falls
    /// through to the next unless it ends the flow, and conditions and jumps also lead to their
    /// targets. Bytes no path reaches are left out. Throws <see cref="InvalidDataException"/> when
    /// a reachable instruction does not decode, or a jump leaves the code.
    /// </summary>
    public SortedDictionary<int, Instruction> DecodeReachable(ReadOnlyMemory<byte> code, IEnumerable<int> entries)
    {
        var decoded = new SortedDictionary<int, Instruction>();
        var pending = new Stack<int>(entries);
        while (pending.Count > 0)
        {
            int offset = pending.Pop();
            if (decoded.ContainsKey(offset))
            {
                continue;
            }

            if (offset < 0 || offset >= code.Length)
            {
                throw new InvalidDataException($"Execution reaches {offset:X4}, outside the code (0000 to {code.Length:X4}).");
            }

            var instruction = Decode(code.Span, offset);
            decoded[offset] = instruction;
            if (!instruction.EndsFlow)
            {
                pending.Push(instruction.End);
            }

            if (instruction.JumpTarget is { } target)
            {
                pending.Push(target);
            }
        }

        return decoded;
    }

    private Instruction Decode(ref Reader r)
    {
        int offset = r.Position;
        byte opcode = r.Byte();
        string handler = HandlerOf(opcode);
        var operands = new List<Operand>();
        var subOps = new List<SubOperation>();
        Operand? result = null;
        int? jump = null;

        Operand VarOrByte(ref Reader reader, byte op, byte mask) => (op & mask) != 0 ? reader.Variable() : Operand.Constant(reader.Byte());
        Operand VarOrWord(ref Reader reader, byte op, byte mask) => (op & mask) != 0 ? reader.Variable() : Operand.Constant(reader.SignedWord());
        void Byte1(ref Reader reader) => operands.Add(VarOrByte(ref reader, opcode, Param1));
        void Byte2(ref Reader reader) => operands.Add(VarOrByte(ref reader, opcode, Param2));
        void Word1(ref Reader reader) => operands.Add(VarOrWord(ref reader, opcode, Param1));
        void Word2(ref Reader reader) => operands.Add(VarOrWord(ref reader, opcode, Param2));
        void Word3(ref Reader reader) => operands.Add(VarOrWord(ref reader, opcode, Param3));
        void Result(ref Reader reader) => result = reader.Variable();
        void Jump(ref Reader reader)
        {
            int delta = reader.SignedWord();
            jump = reader.Position + delta;
        }

        // getWordVararg: words until 0xFF, each a variable when its own lead byte has bit 0x80
        void Varargs(ref Reader reader)
        {
            var args = new List<Operand>();
            for (byte lead = reader.Byte(); lead != 0xFF; lead = reader.Byte())
            {
                args.Add(VarOrWord(ref reader, lead, Param1));
            }

            subOps.Add(new SubOperation(0, "args", args));
        }

        SubOperation Sub(int code, string name, params Operand[] values) => new(code, name, values);

        switch (handler)
        {
            // no operands
            case "o5_stopObjectCode" or "o5_breakHere" or "o5_stopMusic" or "o5_endCutscene" or "o3_waitForSentence":
                break;

            // one variable or byte
            case "o5_actorFollowCamera" or "o5_freezeScripts" or "o5_loadRoom" or "o5_startMusic" or "o5_startSound"
                or "o5_stopScript" or "o5_stopSound" or "o3_waitForActor":
                Byte1(ref r);
                break;

            // one variable or word
            case "o5_debug" or "o5_panCameraTo" or "o5_setCameraAt" or "o5_stopObjectScript" or "o4_pickupObject":
                Word1(ref r);
                break;

            // result only
            case "o5_increment" or "o5_decrement":
                Result(ref r);
                break;

            // result, then one variable or byte
            case "o5_getActorCostume" or "o5_getActorElevation" or "o5_getActorFacing" or "o5_getActorMoving"
                or "o5_getActorRoom" or "o5_getActorWalkBox" or "o5_getActorWidth" or "o5_getInventoryCount"
                or "o5_getRandomNr" or "o5_getStringWidth" or "o5_isScriptRunning" or "o5_isSoundRunning"
                or "o4_saveLoadGame":
                Result(ref r);
                Byte1(ref r);
                break;

            // result, then one variable or word
            case "o5_add" or "o5_and" or "o5_divide" or "o5_getActorX" or "o5_getActorY" or "o5_getClosestObjActor"
                or "o5_getObjectOwner" or "o5_move" or "o5_multiply" or "o5_or" or "o5_subtract":
                Result(ref r);
                Word1(ref r);
                break;

            // result, then two variables or bytes
            case "o5_findInventory" or "o5_findObject":
                Result(ref r);
                Byte1(ref r);
                Byte2(ref r);
                break;

            // result, then two variables or words
            case "o5_actorFromPos" or "o5_getDist" or "o5_getVerbEntrypoint":
                Result(ref r);
                Word1(ref r);
                Word2(ref r);
                break;

            // two variables or bytes
            case "o5_animateActor" or "o5_putActorInRoom":
                Byte1(ref r);
                Byte2(ref r);
                break;

            // a variable or byte, then a variable or word
            case "o5_faceActor" or "o5_putActorAtObject" or "o5_walkActorToObject":
                Byte1(ref r);
                Word2(ref r);
                break;

            // a variable or word, then a variable or byte
            case "o5_setOwnerOf" or "o5_setState":
                Word1(ref r);
                Byte2(ref r);
                break;

            // an actor and a position
            case "o5_putActor" or "o5_walkActorTo":
                Byte1(ref r);
                Word2(ref r);
                Word3(ref r);
                break;

            // GF_SMALL_HEADER: the object and where to draw it
            case "o5_drawObject":
                Word1(ref r);
                Word2(ref r);
                Word3(ref r);
                break;

            case "o5_walkActorToActor":
                Byte1(ref r);
                Byte2(ref r);
                operands.Add(Operand.Constant(r.Byte()));
                break;

            case "o5_lights":
                Byte1(ref r);
                operands.Add(Operand.Constant(r.Byte()));
                operands.Add(Operand.Constant(r.Byte()));
                break;

            case "o3_setBoxFlags":
                Byte1(ref r);
                operands.Add(Operand.Constant(r.Byte()));
                break;

            case "o5_matrixOps":
                {
                    byte sub = r.Byte();
                    subOps.Add((sub & 0x1F) switch
                    {
                        1 => Sub(1, "SET_BOX_FLAGS", VarOrByte(ref r, sub, Param1), VarOrByte(ref r, sub, Param2)),
                        2 => Sub(2, "SET_BOX_SCALE", VarOrByte(ref r, sub, Param1), VarOrByte(ref r, sub, Param2)),
                        3 => Sub(3, "SET_BOX_SCALE_SLOT", VarOrByte(ref r, sub, Param1), VarOrByte(ref r, sub, Param2)),
                        4 => Sub(4, "CREATE_BOX_MATRIX"),
                        _ => Sub(sub & 0x1F, "NOTHING"),
                    });
                }

                break;

            case "o5_loadRoomWithEgo":
                Word1(ref r);
                Byte2(ref r);
                operands.Add(Operand.Constant(r.SignedWord()));
                operands.Add(Operand.Constant(r.SignedWord()));
                break;

            // three bytes of ticks, low first
            case "o5_delay":
                operands.Add(Operand.Constant(r.Byte() | (r.Byte() << 8) | (r.Byte() << 16)));
                break;

            case "o5_delayVariable":
                operands.Add(r.Variable());
                break;

            // beginOverride (not 0) takes the jump after it as its own (ScummEngine::beginOverride):
            // execution goes on after the jump, which runs only when the player skips the cutscene
            case "o5_beginOverride":
                operands.Add(Operand.Constant(r.Byte()));
                if (operands[0].Value != 0)
                {
                    r.Byte();
                    Jump(ref r);
                }

                break;

            case "o5_systemOps":
                {
                    byte sub = r.Byte();
                    subOps.Add(Sub(sub, sub switch { 1 => "RESTART", 2 => "PAUSE", 3 => "QUIT", _ => $"unknown{sub}" }));
                }

                break;

            case "o4_oldRoomEffect":
                {
                    byte sub = r.Byte();
                    subOps.Add((sub & 0x1F) == 3 ? Sub(3, "ROOM_EFFECT", VarOrWord(ref r, sub, Param1)) : Sub(sub & 0x1F, $"unknown{sub & 0x1F}"));
                }

                break;

            // comparisons: a variable, a variable or word, and where to go when it fails
            case "o5_isEqual" or "o5_isNotEqual" or "o5_isGreater" or "o5_isGreaterEqual" or "o5_isLess" or "o5_isLessEqual":
                operands.Add(r.Variable());
                Word1(ref r);
                Jump(ref r);
                break;

            case "o5_equalZero" or "o5_notEqualZero":
                operands.Add(r.Variable());
                Jump(ref r);
                break;

            case "o5_jumpRelative":
                Jump(ref r);
                break;

            case "o5_isActorInBox":
                Byte1(ref r);
                Byte2(ref r);
                Jump(ref r);
                break;

            case "o4_ifState" or "o4_ifNotState":
                Word1(ref r);
                Byte2(ref r);
                Jump(ref r);
                break;

            case "o5_ifClassOfIs":
                Word1(ref r);
                {
                    var classes = new List<Operand>();
                    for (byte lead = r.Byte(); lead != 0xFF; lead = r.Byte())
                    {
                        classes.Add(VarOrWord(ref r, lead, Param1));
                    }

                    subOps.Add(new SubOperation(0, "classes", classes));
                }

                Jump(ref r);
                break;

            case "o5_setClass":
                Word1(ref r);
                {
                    var classes = new List<Operand>();
                    for (byte lead = r.Byte(); lead != 0xFF; lead = r.Byte())
                    {
                        classes.Add(VarOrWord(ref r, lead, Param1));
                    }

                    subOps.Add(new SubOperation(0, "classes", classes));
                }

                break;

            case "o5_startScript" or "o5_chainScript":
                Byte1(ref r);
                Varargs(ref r);
                break;

            case "o5_startObject":
                Word1(ref r);
                Byte2(ref r);
                Varargs(ref r);
                break;

            case "o5_cutscene":
                Varargs(ref r);
                break;

            case "o5_doSentence":
                Byte1(ref r);
                if (operands[0] is { Kind: OperandKind.Constant, Value: 0xFE })
                {
                    break;
                }

                Word2(ref r);
                Word3(ref r);
                break;

            case "o5_setVarRange":
                {
                    Result(ref r);
                    int count = r.Byte();
                    if (count == 0)
                    {
                        throw new InvalidDataException($"setVarRange at {offset:X4} sets no variables.");
                    }

                    for (int i = 0; i < count; i++)
                    {
                        operands.Add(Operand.Constant((opcode & Param1) != 0 ? r.SignedWord() : r.Byte()));
                    }
                }

                break;

            case "o5_pseudoRoom":
                operands.Add(Operand.Constant(r.Byte()));
                for (byte room = r.Byte(); room != 0; room = r.Byte())
                {
                    operands.Add(Operand.Constant(room));
                }

                break;

            case "o5_setObjectName":
                Word1(ref r);
                operands.Add(r.ResourceString());
                break;

            case "o5_drawBox":
                {
                    Word1(ref r);
                    Word2(ref r);
                    byte second = r.Byte();
                    operands.Add(VarOrWord(ref r, second, Param1));
                    operands.Add(VarOrWord(ref r, second, Param2));
                    operands.Add(VarOrByte(ref r, second, Param3));
                }

                break;

            case "o5_print":
                Byte1(ref r);
                DecodeParseString(ref r, subOps, Version);
                break;

            case "o5_printEgo":
                DecodeParseString(ref r, subOps, Version);
                break;

            case "o5_expression":
                Result(ref r);
                for (byte sub = r.Byte(); sub != 0xFF; sub = r.Byte())
                {
                    switch (sub & 0x1F)
                    {
                        case 1:
                            subOps.Add(Sub(1, "value", VarOrWord(ref r, sub, Param1)));
                            break;
                        case 2:
                            subOps.Add(Sub(2, "add"));
                            break;
                        case 3:
                            subOps.Add(Sub(3, "sub"));
                            break;
                        case 4:
                            subOps.Add(Sub(4, "mul"));
                            break;
                        case 5:
                            subOps.Add(Sub(5, "div"));
                            break;
                        case 6:
                            subOps.Add(new SubOperation(6, "opcode", [], Decode(ref r)));
                            break;
                        default:
                            subOps.Add(Sub(sub & 0x1F, $"unknown{sub & 0x1F}"));
                            break;
                    }
                }

                break;

            case "o5_actorOps":
                Byte1(ref r);
                for (byte raw = r.Byte(); raw != 0xFF; raw = r.Byte())
                {
                    int index = (raw & 0x1F) - 1;
                    if (index < 0 || index >= ActorOpsConvertTable.Length)
                    {
                        throw new InvalidDataException($"actorOps sub-operation {raw & 0x1F} at {offset:X4} is outside the small-header table.");
                    }

                    byte sub = (byte)((raw & 0xE0) | ActorOpsConvertTable[index]);
                    subOps.Add((sub & 0x1F) switch
                    {
                        0 => Sub(0, "dummy", VarOrByte(ref r, sub, Param1)),
                        1 => Sub(1, "COSTUME", VarOrByte(ref r, sub, Param1)),
                        2 => Sub(2, "STEP_DIST", VarOrByte(ref r, sub, Param1), VarOrByte(ref r, sub, Param2)),
                        3 => Sub(3, "SOUND", VarOrByte(ref r, sub, Param1)),
                        4 => Sub(4, "WALK_ANIMATION", VarOrByte(ref r, sub, Param1)),
                        5 => Sub(5, "TALK_ANIMATION", VarOrByte(ref r, sub, Param1), VarOrByte(ref r, sub, Param2)),
                        6 => Sub(6, "STAND_ANIMATION", VarOrByte(ref r, sub, Param1)),
                        7 => Sub(7, "ANIMATION", VarOrByte(ref r, sub, Param1), VarOrByte(ref r, sub, Param2), VarOrByte(ref r, sub, Param3)),
                        8 => Sub(8, "DEFAULT"),
                        9 => Sub(9, "ELEVATION", VarOrWord(ref r, sub, Param1)),
                        10 => Sub(10, "ANIMATION_DEFAULT"),
                        11 => Sub(11, "PALETTE", VarOrByte(ref r, sub, Param1), VarOrByte(ref r, sub, Param2)),
                        12 => Sub(12, "TALK_COLOR", VarOrByte(ref r, sub, Param1)),
                        13 => Sub(13, "ACTOR_NAME", r.ResourceString()),
                        14 => Sub(14, "INIT_ANIMATION", VarOrByte(ref r, sub, Param1)),
                        16 => Sub(16, "ACTOR_WIDTH", VarOrByte(ref r, sub, Param1)),
                        17 => Sub(17, "ACTOR_SCALE", VarOrByte(ref r, sub, Param1), VarOrByte(ref r, sub, Param2)),
                        18 => Sub(18, "NEVER_ZCLIP"),
                        19 => Sub(19, "ALWAYS_ZCLIP", VarOrByte(ref r, sub, Param1)),
                        20 => Sub(20, "IGNORE_BOXES"),
                        _ => throw new InvalidDataException($"actorOps sub-operation {sub & 0x1F} at {offset:X4} is not one ScummVM knows."),
                    });
                }

                break;

            case "o5_cursorCommand":
                {
                    byte sub = r.Byte();
                    subOps.Add((sub & 0x1F) switch
                    {
                        1 => Sub(1, "CURSOR_ON"),
                        2 => Sub(2, "CURSOR_OFF"),
                        3 => Sub(3, "USERPUT_ON"),
                        4 => Sub(4, "USERPUT_OFF"),
                        5 => Sub(5, "CURSOR_SOFT_ON"),
                        6 => Sub(6, "CURSOR_SOFT_OFF"),
                        7 => Sub(7, "USERPUT_SOFT_ON"),
                        8 => Sub(8, "USERPUT_SOFT_OFF"),
                        10 => Sub(10, "CURSOR_IMAGE", VarOrByte(ref r, sub, Param1), VarOrByte(ref r, sub, Param2)),
                        11 => Sub(11, "CURSOR_HOTSPOT", VarOrByte(ref r, sub, Param1), VarOrByte(ref r, sub, Param2), VarOrByte(ref r, sub, Param3)),
                        12 => Sub(12, "CURSOR_SET", VarOrByte(ref r, sub, Param1)),
                        13 => Sub(13, "CHARSET_SET", VarOrByte(ref r, sub, Param1)),
                        14 => Sub(14, "unknown14", VarOrByte(ref r, sub, Param1), VarOrByte(ref r, sub, Param2)),
                        _ => throw new InvalidDataException($"cursorCommand sub-operation {sub & 0x1F} at {offset:X4} is not one ScummVM knows."),
                    });
                }

                break;

            case "o5_roomOps":
                {
                    // version 3 fetches the first two operands before the sub-operation, with the
                    // opcode's bits; version 4 fetches them after it, with its bits, for the first four
                    Operand[] ab = Version == 3 ? [VarOrWord(ref r, opcode, Param1), VarOrWord(ref r, opcode, Param2)] : [];
                    byte sub = r.Byte();
                    if (Version != 3 && (sub & 0x1F) is >= 1 and <= 4)
                    {
                        ab = [VarOrWord(ref r, sub, Param1), VarOrWord(ref r, sub, Param2)];
                    }

                    switch (sub & 0x1F)
                    {
                        case 1:
                            subOps.Add(Sub(1, "ROOM_SCROLL", ab));
                            break;
                        case 2:
                            subOps.Add(Sub(2, "ROOM_COLOR", ab));
                            break;
                        case 3:
                            subOps.Add(Sub(3, "ROOM_SCREEN", ab));
                            break;
                        case 4:
                            subOps.Add(Sub(4, "ROOM_PALETTE", ab));
                            break;
                        case 5:
                            subOps.Add(Sub(5, "ROOM_SHAKE_ON", ab));
                            break;
                        case 6:
                            subOps.Add(Sub(6, "ROOM_SHAKE_OFF", ab));
                            break;
                        case 7:
                            {
                                var values = new List<Operand>(ab) { VarOrByte(ref r, sub, Param1), VarOrByte(ref r, sub, Param2) };
                                byte second = r.Byte();
                                values.Add(VarOrByte(ref r, second, Param1));
                                values.Add(VarOrByte(ref r, second, Param2));
                                byte third = r.Byte();
                                values.Add(VarOrByte(ref r, third, Param2));
                                subOps.Add(new SubOperation(7, "ROOM_SCALE", values));
                            }

                            break;
                        case 8:
                            subOps.Add(Sub(8, "ROOM_INTENSITY", [.. ab, VarOrByte(ref r, sub, Param1), VarOrByte(ref r, sub, Param2), VarOrByte(ref r, sub, Param3)]));
                            break;
                        case 9:
                            subOps.Add(Sub(9, "ROOM_SAVEGAME", [.. ab, VarOrByte(ref r, sub, Param1), VarOrByte(ref r, sub, Param2)]));
                            break;
                        case 10:
                            subOps.Add(Sub(10, "ROOM_FADE", [.. ab, VarOrWord(ref r, sub, Param1)]));
                            break;
                        case 11 or 12:
                            {
                                var values = new List<Operand>(ab) { VarOrWord(ref r, sub, Param1), VarOrWord(ref r, sub, Param2), VarOrWord(ref r, sub, Param3) };
                                byte second = r.Byte();
                                values.Add(VarOrByte(ref r, second, Param1));
                                values.Add(VarOrByte(ref r, second, Param2));
                                subOps.Add(new SubOperation(sub & 0x1F, (sub & 0x1F) == 11 ? "RGB_ROOM_INTENSITY" : "ROOM_SHADOW", values));
                            }

                            break;
                        case 13 or 14:
                            subOps.Add(Sub(sub & 0x1F, (sub & 0x1F) == 13 ? "SAVE_STRING" : "LOAD_STRING", [.. ab, VarOrByte(ref r, sub, Param1), r.PlainString()]));
                            break;
                        case 15:
                            {
                                var values = new List<Operand>(ab) { VarOrByte(ref r, sub, Param1) };
                                byte second = r.Byte();
                                values.Add(VarOrByte(ref r, second, Param1));
                                values.Add(VarOrByte(ref r, second, Param2));
                                byte third = r.Byte();
                                values.Add(VarOrByte(ref r, third, Param1));
                                subOps.Add(new SubOperation(15, "ROOM_TRANSFORM", values));
                            }

                            break;
                        case 16:
                            subOps.Add(Sub(16, "CYCLE_SPEED", [.. ab, VarOrByte(ref r, sub, Param1), VarOrByte(ref r, sub, Param2)]));
                            break;
                        default:
                            throw new InvalidDataException($"roomOps sub-operation {sub & 0x1F} at {offset:X4} is not one ScummVM knows.");
                    }
                }

                break;

            case "o5_resourceRoutines":
                {
                    byte sub = r.Byte();
                    Operand? resource = sub != 17 ? VarOrByte(ref r, sub, Param1) : null;
                    Operand[] Res(params Operand[] more) => resource is { } id ? [id, .. more] : more;
                    int op = sub & 0x3F;
                    subOps.Add(op switch
                    {
                        1 => Sub(1, "LOAD_SCRIPT", Res()),
                        2 => Sub(2, "LOAD_SOUND", Res()),
                        3 => Sub(3, "LOAD_COSTUME", Res()),
                        4 => Sub(4, "LOAD_ROOM", Res()),
                        5 => Sub(5, "NUKE_SCRIPT", Res()),
                        6 => Sub(6, "NUKE_SOUND", Res()),
                        7 => Sub(7, "NUKE_COSTUME", Res()),
                        8 => Sub(8, "NUKE_ROOM", Res()),
                        9 => Sub(9, "LOCK_SCRIPT", Res()),
                        10 => Sub(10, "LOCK_SOUND", Res()),
                        11 => Sub(11, "LOCK_COSTUME", Res()),
                        12 => Sub(12, "LOCK_ROOM", Res()),
                        13 => Sub(13, "UNLOCK_SCRIPT", Res()),
                        14 => Sub(14, "UNLOCK_SOUND", Res()),
                        15 => Sub(15, "UNLOCK_COSTUME", Res()),
                        16 => Sub(16, "UNLOCK_ROOM", Res()),
                        17 => Sub(17, "CLEAR_HEAP"),
                        18 => Sub(18, "LOAD_CHARSET", Res()),
                        19 => Sub(19, "NUKE_CHARSET", Res()),
                        20 => Sub(20, "LOAD_OBJECT", Res(VarOrWord(ref r, sub, Param2))),
                        32 => Sub(32, "unknown32", Res()),
                        33 => Sub(33, "unknown33", Res()),
                        35 => Sub(35, "CD_VOLUME", Res(VarOrByte(ref r, sub, Param2))),
                        36 => Sub(36, "SOUND_VOLUME", Res(VarOrByte(ref r, sub, Param2), Operand.Constant(r.Byte()))),
                        37 => Sub(37, "SOUND_NOTE", Res(VarOrByte(ref r, sub, Param2))),
                        _ => throw new InvalidDataException($"resourceRoutines sub-operation {op} at {offset:X4} is not one ScummVM knows."),
                    });
                }

                break;

            case "o5_saveRestoreVerbs":
                {
                    byte sub = r.Byte();
                    var values = new[] { VarOrByte(ref r, sub, Param1), VarOrByte(ref r, sub, Param2), VarOrByte(ref r, sub, Param3) };
                    subOps.Add(sub switch
                    {
                        1 => Sub(1, "SAVE_VERBS", values),
                        2 => Sub(2, "RESTORE_VERBS", values),
                        3 => Sub(3, "DELETE_VERBS", values),
                        _ => throw new InvalidDataException($"saveRestoreVerbs sub-operation {sub} at {offset:X4} is not one ScummVM knows."),
                    });
                }

                break;

            case "o5_stringOps":
                {
                    byte sub = r.Byte();
                    switch (sub & 0x1F)
                    {
                        case 1:
                            subOps.Add(Sub(1, "LOAD_STRING", VarOrByte(ref r, sub, Param1), r.ResourceString()));
                            break;
                        case 2:
                            subOps.Add(Sub(2, "COPY_STRING", VarOrByte(ref r, sub, Param1), VarOrByte(ref r, sub, Param2)));
                            break;
                        case 3:
                            subOps.Add(Sub(3, "SET_STRING_CHAR", VarOrByte(ref r, sub, Param1), VarOrByte(ref r, sub, Param2), VarOrByte(ref r, sub, Param3)));
                            break;
                        case 4:
                            Result(ref r);
                            subOps.Add(Sub(4, "GET_STRING_CHAR", VarOrByte(ref r, sub, Param1), VarOrByte(ref r, sub, Param2)));
                            break;
                        case 5:
                            subOps.Add(Sub(5, "CREATE_STRING", VarOrByte(ref r, sub, Param1), VarOrByte(ref r, sub, Param2)));
                            break;
                        default:
                            subOps.Add(Sub(sub & 0x1F, $"unknown{sub & 0x1F}"));
                            break;
                    }
                }

                break;

            case "o5_verbOps":
                Byte1(ref r);
                for (byte sub = r.Byte(); sub != 0xFF; sub = r.Byte())
                {
                    subOps.Add((sub & 0x1F) switch
                    {
                        1 => Sub(1, "VERB_IMAGE", VarOrWord(ref r, sub, Param1)),
                        2 => Sub(2, "VERB_NAME", r.ResourceString()),
                        3 => Sub(3, "VERB_COLOR", VarOrByte(ref r, sub, Param1)),
                        4 => Sub(4, "VERB_HICOLOR", VarOrByte(ref r, sub, Param1)),
                        5 => Sub(5, "VERB_AT", VarOrWord(ref r, sub, Param1), VarOrWord(ref r, sub, Param2)),
                        6 => Sub(6, "VERB_ON"),
                        7 => Sub(7, "VERB_OFF"),
                        8 => Sub(8, "VERB_DELETE"),
                        9 => Sub(9, "VERB_NEW"),
                        16 => Sub(16, "VERB_DIMCOLOR", VarOrByte(ref r, sub, Param1)),
                        17 => Sub(17, "VERB_DIM"),
                        18 => Sub(18, "VERB_KEY", VarOrByte(ref r, sub, Param1)),
                        19 => Sub(19, "VERB_CENTER"),
                        20 => Sub(20, "VERB_NAME_STR", VarOrWord(ref r, sub, Param1)),
                        22 => Sub(22, "VERB_ASSIGN_OBJECT", VarOrWord(ref r, sub, Param1), VarOrByte(ref r, sub, Param2)),
                        23 => Sub(23, "VERB_BACKCOLOR", VarOrByte(ref r, sub, Param1)),
                        _ => throw new InvalidDataException($"verbOps sub-operation {sub & 0x1F} at {offset:X4} is not one ScummVM knows."),
                    });
                }

                break;

            case "o5_wait":
                {
                    byte sub = r.Byte();
                    subOps.Add((sub & 0x1F) switch
                    {
                        1 => Sub(1, "WAIT_FOR_ACTOR", VarOrByte(ref r, sub, Param1)),
                        2 => Sub(2, "WAIT_FOR_MESSAGE"),
                        3 => Sub(3, "WAIT_FOR_CAMERA"),
                        4 => Sub(4, "WAIT_FOR_SENTENCE"),
                        _ => throw new InvalidDataException($"wait sub-operation {sub & 0x1F} at {offset:X4} is not one ScummVM knows."),
                    });
                }

                break;

            case "o4_saveLoadVars":
                operands.Add(Operand.Constant(r.Byte()));
                for (byte sub = r.Byte(); sub != 0; sub = r.Byte())
                {
                    int op = sub & 0x1F;
                    if (op == 1)
                    {
                        subOps.Add(Sub(1, "VARS", r.Variable(), r.Variable()));
                    }
                    else if (op == 2)
                    {
                        subOps.Add(Sub(2, "STRINGS", VarOrByte(ref r, sub, Param1), VarOrByte(ref r, sub, Param2)));
                    }
                    else if (op == 3)
                    {
                        subOps.Add(Sub(3, "OPEN_FILE", r.ResourceString()));
                    }
                    else if (op is 4 or 0x1F)
                    {
                        subOps.Add(Sub(op, op == 4 ? "END" : "CLOSE_FILE"));
                        break;
                    }
                    else
                    {
                        subOps.Add(Sub(op, $"unknown{op}"));
                    }
                }

                break;

            default:
                throw new InvalidOperationException($"No operand layout for {handler}.");
        }

        return new Instruction(offset, opcode, handler, operands, r.Position - offset, result, jump, subOps.Count > 0 ? subOps : null);
    }

    /// <summary>
    /// The text sub-operations of <c>print</c> and <c>printEgo</c>, up to 0xFF or to the text,
    /// which ends them. Mirrors <c>ScummEngine_v5::decodeParseString</c>; in version 3
    /// sub-operation 6 sets the height instead of left-aligning.
    /// </summary>
    private static void DecodeParseString(ref Reader r, List<SubOperation> subOps, int version)
    {
        for (byte sub = r.Byte(); sub != 0xFF; sub = r.Byte())
        {
            Operand Word(ref Reader reader, byte mask) => (sub & mask) != 0 ? reader.Variable() : Operand.Constant(reader.SignedWord());
            Operand Byte(ref Reader reader, byte mask) => (sub & mask) != 0 ? reader.Variable() : Operand.Constant(reader.Byte());

            switch (sub & 0x0F)
            {
                case 0:
                    subOps.Add(new SubOperation(0, "AT", [Word(ref r, Param1), Word(ref r, Param2)]));
                    break;
                case 1:
                    subOps.Add(new SubOperation(1, "COLOR", [Byte(ref r, Param1)]));
                    break;
                case 2:
                    subOps.Add(new SubOperation(2, "CLIPPED", [Word(ref r, Param1)]));
                    break;
                case 3:
                    subOps.Add(new SubOperation(3, "ERASE", [Word(ref r, Param1), Word(ref r, Param2)]));
                    break;
                case 4:
                    subOps.Add(new SubOperation(4, "CENTER", []));
                    break;
                case 6:
                    subOps.Add(version == 3 ? new SubOperation(6, "HEIGHT", [Word(ref r, Param1)]) : new SubOperation(6, "LEFT", []));
                    break;
                case 7:
                    subOps.Add(new SubOperation(7, "OVERHEAD", []));
                    break;
                case 8:
                    subOps.Add(new SubOperation(8, "SAY_VOICE", [Word(ref r, Param1), Word(ref r, Param2)]));
                    break;
                case 15:
                    subOps.Add(new SubOperation(15, "TEXTSTRING", [r.ResourceString()]));
                    return;
                default:
                    throw new InvalidDataException($"print sub-operation {sub & 0x0F} at {r.Position - 1:X4} is not one ScummVM knows.");
            }
        }
    }

    private ref struct Reader(ReadOnlySpan<byte> code, int position)
    {
        private readonly ReadOnlySpan<byte> _code = code;

        public int Position { get; private set; } = position;

        public byte Byte()
        {
            if (Position >= _code.Length)
            {
                throw new InvalidDataException($"An instruction runs past the end of the script, at {Position:X4}.");
            }

            return _code[Position++];
        }

        public int Word() => Byte() | (Byte() << 8);

        public int SignedWord() => (short)Word();

        /// <summary>A variable word, and its index word when it has bit 0x2000. Mirrors <c>readVar</c> and <c>getResultPos</c>.</summary>
        public Operand Variable()
        {
            int number = Word();
            if ((number & Operand.Indexed) == 0)
            {
                return Operand.Variable(number);
            }

            int index = Word();
            var added = (index & Operand.Indexed) != 0 ? Operand.Variable(index & ~Operand.Indexed) : Operand.Constant(index & 0x0FFF);
            return Operand.Variable(number & ~Operand.Indexed, added);
        }

        /// <summary>
        /// Text up to a zero byte. Mirrors <c>ScummEngine::resStrLen</c>: 0xFF takes the next byte,
        /// and two more unless it is 1, 2, 3 or 8.
        /// </summary>
        public Operand ResourceString()
        {
            int start = Position;
            for (byte c = Byte(); c != 0; c = Byte())
            {
                if (c == 0xFF && Byte() is not (1 or 2 or 3 or 8))
                {
                    Byte();
                    Byte();
                }
            }

            return Operand.String(_code[start..(Position - 1)].ToArray());
        }

        /// <summary>Plain bytes up to a zero, as <c>roomOps</c> reads a file name.</summary>
        public Operand PlainString()
        {
            int start = Position;
            while (Byte() != 0)
            {
            }

            return Operand.String(_code[start..(Position - 1)].ToArray());
        }
    }
}
