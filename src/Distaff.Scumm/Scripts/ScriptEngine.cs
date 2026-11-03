namespace Distaff.Scumm.Scripts;

/// <summary>
/// The script engine of the EGA release: 80 slots of cooperative threads with their local
/// variables, the global variables (with the bit variables inside them), the cutscene stack and
/// the sentence queue, and the instructions that work on them. Mirrors ScummVM's
/// <c>script.cpp</c> (<c>runScript</c>, <c>runObjectScript</c>, <c>runScriptNested</c>,
/// <c>executeScript</c>, <c>runAllScripts</c>, <c>decreaseScriptDelay</c>, <c>stopScript</c>,
/// <c>stopObjectScript</c>, <c>stopObjectCode</c>, <c>freezeScripts</c>, <c>beginCutscene</c>,
/// <c>endCutscene</c>, <c>abortCutscene</c>, <c>doSentence</c>, <c>checkAndRunSentenceScript</c>)
/// for version 3, and the <c>o5_</c> handlers that touch only script state. The rest goes to an
/// <see cref="IScriptHost"/>.
/// </summary>
internal sealed class ScriptEngine
{
    /// <summary>Mirrors <c>NUM_SCRIPT_SLOT</c>.</summary>
    public const int SlotCount = 80;

    /// <summary>The numbers rooms' scripts run under. Mirrors <c>kScriptNumEXCD</c> and <c>kScriptNumENCD</c>.</summary>
    public const int ExitScriptNumber = 10001, EntryScriptNumber = 10002;

    /// <summary>The first local script's number (<c>_numGlobalScripts</c> of <c>ScummEngine_v4::readMAXS</c>).</summary>
    public const int GlobalScriptCount = 200;

    /// <summary>Global script code starts after the resource header (<c>_resourceHeaderSize</c>): 4 bytes for version 3, 6 for version 4.</summary>
    public int ScriptHeaderSize { get; }

    /// <summary>The SCUMM version the scripts are written for: 3 or 4.</summary>
    public int Version { get; }

    /// <summary>Mirrors <c>kMaxCutsceneNum</c>.</summary>
    public const int CutsceneLevels = 5;

    /// <summary>Mirrors <c>kMaxScriptNesting</c>.</summary>
    public const int NestingLimit = 15;

    /// <summary>Mirrors <c>NUM_SENTENCE</c>.</summary>
    public const int SentenceLimit = 6;

    /// <summary>A script running this many instructions without yielding is taken to be stuck.</summary>
    public const int InstructionBudget = 100_000;

    private readonly IScriptHost _host;
    private readonly ScriptDecoder _decoder;
    private readonly List<(int Number, ScriptKind Kind, int Slot)> _nested = [];
    private readonly List<Sentence> _sentences = [];
    private readonly Stack<int> _stack = new();
    private bool _discardResult;

    /// <summary>The game's random numbers, which the scripts and the dissolve share (<c>_rnd</c>).</summary>
    public ScummRandom Random { get; internal set; }

    public ScriptEngine(IScriptHost host, int seed = 1, int version = 3, bool fmTowns = false)
    {
        _host = host;
        Version = version;
        ScriptHeaderSize = version == 4 || fmTowns ? 6 : 4;
        FmTowns = fmTowns;
        _decoder = new ScriptDecoder(version, fmTowns);
        Random = new ScummRandom((uint)seed);
        for (int i = 0; i < Slots.Length; i++)
        {
            Slots[i] = new ScriptSlot();
        }
    }

    /// <summary>The global variables; the bit variables are their bits (<c>_scummVars</c>).</summary>
    public int[] Variables { get; } = new int[Vars.Count];

    public ScriptSlot[] Slots { get; } = new ScriptSlot[SlotCount];

    /// <summary>The slot running now, or -1 (ScummVM's <c>_currentScript == 0xFF</c>).</summary>
    public int CurrentSlot { get; private set; } = -1;

    public int CurrentScriptNumber => CurrentSlot < 0 ? 0 : Slots[CurrentSlot].Number;

    /// <summary>The instruction running now, while the host carries it out.</summary>
    public Instruction? CurrentInstruction { get; private set; }

    /// <summary>The cutscene stack: the level, and for each level its argument, the overriding slot and where it skips to (<c>vm.cutScene*</c>).</summary>
    public int CutsceneLevel { get; private set; }

    private readonly int[] _cutsceneData = new int[CutsceneLevels];
    private readonly int[] _cutsceneSlot = new int[CutsceneLevels];
    private readonly int[] _cutsceneOffset = new int[CutsceneLevels];

    /// <summary>The slot that began the cutscene whose start script is running, or -1 (<c>vm.cutSceneScriptIndex</c>).</summary>
    private int _cutsceneStarter = -1;

    /// <summary>The sentences waiting to run, the last first (<c>_sentence</c>, <c>_sentenceNum</c>).</summary>
    public IReadOnlyList<Sentence> Sentences => _sentences;

    /// <summary>
    /// Whether the game's copy protection is kept. Off by default, as ScummVM's
    /// <c>copy_protection</c> option is: then the DOS releases' room 69 starts script 205 where it
    /// asks for 201, as the LucasArts Classic Adventures release did (<c>o5_startScript</c>), and
    /// the FM Towns' bit 15 of variable 214 reads 0 (<c>readVar</c>).
    /// </summary>
    public bool CopyProtection { get; set; }

    /// <summary>Whether the scripts are the FM Towns release's.</summary>
    public bool FmTowns { get; }

    /// <summary>The room whose script 201 asks for a draft from the Book of Patterns.</summary>
    public const int CopyProtectionRoom = 69;

    /// <summary>The room on screen, as the host sets it on a room change (<c>_currentRoom</c>).</summary>
    public int CurrentRoom { get; set; }

    /// <summary>The variable number an operand names: its number, with its index added.</summary>
    public int VariableNumber(Operand operand)
    {
        int number = operand.Value;
        if (operand.Index is { } index)
        {
            number += index.Kind == OperandKind.Variable ? ReadVar(index.Value) : index.Value;
        }

        return number;
    }

    /// <summary>The value of an operand.</summary>
    public int Evaluate(Operand operand) => operand.Kind switch
    {
        OperandKind.Variable => ReadVar(VariableNumber(operand)),
        OperandKind.Constant => operand.Value,
        _ => 0,
    };

    /// <summary>
    /// The version 4 bit variables (<c>_bitVars</c>), 4096 of them, eight to a byte; version 3 keeps
    /// its bit variables in the bits of the global variables instead.
    /// </summary>
    public byte[] BitVariables { get; } = new byte[BitVariableCount / 8];

    /// <summary>How many bit variables version 4 has (<c>_numBitVariables</c> of <c>ScummEngine_v4::readMAXS</c>).</summary>
    public const int BitVariableCount = 4096;

    /// <summary>Reads a variable by its number, flags included. Mirrors <c>ScummEngine::readVar</c> for versions 3 and 4.</summary>
    public int ReadVar(int number)
    {
        if ((number & 0xF000) == 0)
        {
            return Variables[CheckVariable(number)];
        }

        if ((number & Operand.BitVariable) != 0 && Version == 4)
        {
            int index = CheckBitVariable(number & 0x7FFF);
            return (BitVariables[index >> 3] & (1 << (index & 7))) != 0 ? 1 : 0;
        }

        if ((number & Operand.BitVariable) != 0)
        {
            int bit = number & 0x0F;
            int variable = CheckVariable((number >> 4) & 0xFF);
            if (!CopyProtection && FmTowns && variable == 214 && bit == 15)
            {
                return 0;
            }

            return (Variables[variable] & (1 << bit)) != 0 ? 1 : 0;
        }

        if ((number & Operand.LocalVariable) != 0)
        {
            return Slots[RunningSlot()].Locals[CheckLocal(number & 0x0FFF)];
        }

        throw new InvalidOperationException($"Variable 0x{number:X4} has no kind.");
    }

    /// <summary>Writes a variable by its number, flags included. Mirrors <c>ScummEngine::writeVar</c> for versions 3 and 4.</summary>
    public void WriteVar(int number, int value)
    {
        if ((number & 0xF000) == 0)
        {
            if (number == Vars.CharInc && CharIncWriting is { } charInc)
            {
                value = charInc(value);
            }

            Variables[CheckVariable(number)] = value;
            return;
        }

        if ((number & Operand.BitVariable) != 0 && Version == 4)
        {
            int index = CheckBitVariable(number & 0x7FFF);
            BitVariables[index >> 3] = (byte)(value != 0 ? BitVariables[index >> 3] | (1 << (index & 7)) : BitVariables[index >> 3] & ~(1 << (index & 7)));
            return;
        }

        if ((number & Operand.BitVariable) != 0)
        {
            int bit = number & 0x0F;
            int variable = CheckVariable((number >> 4) & 0xFF);
            Variables[variable] = value != 0 ? Variables[variable] | (1 << bit) : Variables[variable] & ~(1 << bit);
            return;
        }

        if ((number & Operand.LocalVariable) != 0)
        {
            Slots[RunningSlot()].Locals[CheckLocal(number & 0x0FFF)] = value;
            return;
        }

        throw new InvalidOperationException($"Variable 0x{number:X4} has no kind.");
    }

    public bool IsScriptRunning(int number)
        => Slots.Any(s => s.Number == number && s.Kind is ScriptKind.Global or ScriptKind.Local && !s.IsDead);

    /// <summary>Whether any slot holds the number, dead or alive. Mirrors <c>isScriptInUse</c>.</summary>
    public bool IsScriptInUse(int number) => Slots.Any(s => s.Number == number);

    /// <summary>
    /// Told of each script started: a global or local script's number with verb 0, an entry or
    /// exit script's pseudo-number with verb 0, or an object's number with the verb asked of it.
    /// For tests that ask what the game has run.
    /// </summary>
    internal Action<int, int>? ScriptStarted { get; set; }

    /// <summary>What a script's write of the text speed (<c>VAR_CHARINC</c>) becomes, for the world's text speed setting to have its say (<c>writeVar</c>).</summary>
    internal Func<int, int>? CharIncWriting { get; set; }

    /// <summary>
    /// Starts a global or local script and runs it until it yields; then the caller, if any,
    /// carries on. Unless recursive, other copies are stopped first. Mirrors <c>runScript</c>.
    /// </summary>
    public void RunScript(int number, bool freezeResistant = false, bool recursive = false, IReadOnlyList<int>? args = null)
    {
        if (number == 0)
        {
            return;
        }

        if (!recursive)
        {
            StopScript(number);
        }

        ScriptStarted?.Invoke(number, 0);
        if (number < GlobalScriptCount)
        {
            var code = _host.GlobalScript(number) ?? throw new InvalidOperationException($"Script {number} does not exist.");
            Start(number, ScriptKind.Global, code, ScriptHeaderSize, freezeResistant, recursive, args);
        }
        else
        {
            var (code, offset) = _host.LocalScript(number) ?? throw new InvalidOperationException($"Local script {number} is not in the room.");
            Start(number, ScriptKind.Local, code, offset, freezeResistant, recursive, args);
        }
    }

    /// <summary>Runs a room's entry or exit script, from <paramref name="offset"/> in the room's code.</summary>
    public void RunRoomScript(int pseudoNumber, ReadOnlyMemory<byte> room, int offset)
    {
        if (offset != 0)
        {
            ScriptStarted?.Invoke(pseudoNumber, 0);
            Start(pseudoNumber, ScriptKind.Room, room, offset, false, false, null);
        }
    }

    /// <summary>
    /// Runs an object's script for a verb, when it has one. Unless recursive, its other scripts are
    /// stopped first. Mirrors <c>runObjectScript</c>.
    /// </summary>
    public void RunObjectScript(int obj, int verb, bool freezeResistant = false, bool recursive = false, IReadOnlyList<int>? args = null)
    {
        if (obj == 0)
        {
            return;
        }

        if (!recursive)
        {
            StopObjectScript(obj);
        }

        if (_host.ObjectScript(obj, verb) is { } script)
        {
            ScriptStarted?.Invoke(obj, verb);
            Start(obj, script.Kind, script.Code, script.Offset, freezeResistant, recursive, args);
        }
    }

    /// <summary>Stops every copy of a global or local script, running or nested. Mirrors <c>stopScript</c>.</summary>
    public void StopScript(int number)
    {
        if (number == 0)
        {
            return;
        }

        StopWhere(number, kind => kind is ScriptKind.Global or ScriptKind.Local);
    }

    /// <summary>Stops every script of an object, running or nested. Mirrors <c>stopObjectScript</c>.</summary>
    public void StopObjectScript(int obj)
    {
        if (obj == 0)
        {
            return;
        }

        StopWhere(obj, kind => kind is ScriptKind.Room or ScriptKind.Inventory);
    }

    /// <summary>Runs every running slot once, from the first to the last. Mirrors <c>runAllScripts</c>.</summary>
    public void RunAllScripts()
    {
        var executed = new bool[SlotCount];
        CurrentSlot = -1;
        for (int i = 0; i < SlotCount; i++)
        {
            // a slot started by another this turn has run already (didexec)
            if (Slots[i].Status == SlotStatus.Running && !executed[i])
            {
                CurrentSlot = i;
                Slots[i].ResumeOffset = Slots[i].Offset;
                Execute(executed);
            }
        }
    }

    /// <summary>Counts down paused slots and wakes those whose delay has run out. Mirrors <c>decreaseScriptDelay</c>.</summary>
    public void DecreaseScriptDelay(int ticks)
    {
        foreach (var slot in Slots)
        {
            if (slot.Status == SlotStatus.Paused)
            {
                slot.Delay -= ticks;
                if (slot.Delay < 0)
                {
                    slot.Status = SlotStatus.Running;
                    slot.Delay = 0;
                }
            }
        }
    }

    /// <summary>
    /// When the running script belongs to the room (a local script, its entry or exit script, or an
    /// object's), it stops running now: it is about to be killed with the room. Mirrors the start
    /// of <c>startScene</c>.
    /// </summary>
    public void LeaveRoomScript()
    {
        if (CurrentSlot >= 0 && Slots[CurrentSlot].Kind is ScriptKind.Room or ScriptKind.Local)
        {
            CurrentSlot = -1;
        }
    }

    /// <summary>Kills every script of the room being left; scripts of carried objects live on. Mirrors <c>killScriptsAndResources</c>.</summary>
    public void KillRoomScripts()
    {
        foreach (var slot in Slots)
        {
            if (slot.Kind is ScriptKind.Room or ScriptKind.Local && !slot.IsDead)
            {
                slot.CutsceneOverride = 0;
                slot.Kill();
            }
        }
    }

    /// <summary>
    /// Freezes every slot but the running one; freeze-resistant ones too when
    /// <paramref name="flag"/> is 0x80 or more. Mirrors <c>freezeScripts</c> for version 3.
    /// </summary>
    public void FreezeScripts(int flag)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            var slot = Slots[i];
            if (i != CurrentSlot && !slot.IsDead && (!slot.FreezeResistant || flag >= 0x80))
            {
                slot.Status |= SlotStatus.Frozen;
                slot.FreezeCount++;
            }
        }

        foreach (var sentence in _sentences)
        {
            sentence.FreezeCount++;
        }

        if (_cutsceneStarter >= 0)
        {
            Slots[_cutsceneStarter].Status &= unchecked((byte)~SlotStatus.Frozen);
            Slots[_cutsceneStarter].FreezeCount = 0;
        }
    }

    /// <summary>Thaws one level of freezing. Mirrors <c>unfreezeScripts</c>.</summary>
    public void UnfreezeScripts()
    {
        foreach (var slot in Slots)
        {
            if ((slot.Status & SlotStatus.Frozen) != 0 && --slot.FreezeCount == 0)
            {
                slot.Status &= unchecked((byte)~SlotStatus.Frozen);
            }
        }

        foreach (var sentence in _sentences)
        {
            if (sentence.FreezeCount > 0)
            {
                sentence.FreezeCount--;
            }
        }
    }

    /// <summary>
    /// Skips the running cutscene: the script that set the override goes to its jump. Mirrors
    /// <c>abortCutscene</c>.
    /// </summary>
    public void AbortCutscene()
    {
        int level = CutsceneLevel;
        if (_cutsceneOffset[level] == 0)
        {
            return;
        }

        var slot = Slots[_cutsceneSlot[level]];
        slot.Offset = _cutsceneOffset[level];
        slot.Status = SlotStatus.Running;
        slot.FreezeCount = 0;
        if (slot.CutsceneOverride > 0)
        {
            slot.CutsceneOverride--;
        }

        Variables[Vars.Override] = 1;
        _cutsceneOffset[level] = 0;
    }

    /// <summary>Queues a sentence. Mirrors <c>doSentence</c>.</summary>
    public void DoSentence(int verb, int objectA, int objectB)
    {
        if (_sentences.Count >= SentenceLimit)
        {
            throw new InvalidOperationException("Too many sentences queued.");
        }

        _sentences.Add(new Sentence(verb, objectA, objectB));
    }

    /// <summary>
    /// Runs the sentence script on the last queued sentence, unless it is running unfrozen or the
    /// sentence is frozen. Mirrors <c>checkAndRunSentenceScript</c>.
    /// </summary>
    public void CheckAndRunSentenceScript()
    {
        int script = Variables[Vars.SentenceScript];
        if (Slots.Any(s => s.Number == script && !s.IsDead && s.FreezeCount == 0))
        {
            return;
        }

        if (_sentences.Count == 0 || _sentences[^1].FreezeCount != 0)
        {
            return;
        }

        var sentence = _sentences[^1];
        _sentences.RemoveAt(_sentences.Count - 1);
        if (sentence.ObjectB != 0 && sentence.ObjectB == sentence.ObjectA)
        {
            return;
        }

        CurrentSlot = -1;
        if (script != 0)
        {
            RunScript(script, args: [sentence.Verb, sentence.ObjectA, sentence.ObjectB]);
        }
    }

    /// <summary>Ends the running script's turn; it carries on from the next instruction next time. Mirrors <c>o5_breakHere</c>.</summary>
    public void BreakHere() => CurrentSlot = -1;

    /// <summary>For a host: the running instruction is to run again next time, as the wait instructions do.</summary>
    public void Repeat()
    {
        if (CurrentSlot >= 0 && CurrentInstruction is { } instruction)
        {
            Slots[CurrentSlot].Offset = instruction.Offset;
            BreakHere();
        }
    }

    /// <summary>Stops the running script. Mirrors <c>stopObjectCode</c> for version 3.</summary>
    public void StopObjectCode()
    {
        var slot = Slots[RunningSlot()];
        if (slot.Kind is ScriptKind.Room or ScriptKind.Inventory)
        {
            StopObjectScript(slot.Number);
        }
        else
        {
            slot.Kill();
        }

        CurrentSlot = -1;
    }

    /// <summary>
    /// After a load in the middle of an instruction: the slot the save was running carries on from
    /// <paramref name="offset"/>, as ScummVM's <c>o4_saveLoadGame</c> puts the old script pointer
    /// into the loaded state's current script (<c>updateScriptPtr</c>); -1 for no slot. The nesting
    /// is gone: no caller is left to go back to.
    /// </summary>
    internal void ContinueAfterLoad(int slot, int offset)
    {
        _nested.Clear();
        CurrentSlot = slot;
        if (slot >= 0)
        {
            Slots[slot].Offset = offset;
        }
    }

    /// <summary>The cutscene stack, for saves: each level's argument, overriding slot and where it skips to (<c>vm.cutSceneData</c>, <c>cutSceneScript</c>, <c>cutScenePtr</c>).</summary>
    internal (int[] Data, int[] Slot, int[] Offset) CutsceneStack => (_cutsceneData, _cutsceneSlot, _cutsceneOffset);

    /// <summary>The slot that began the cutscene whose start script runs, or -1 (<c>vm.cutSceneScriptIndex</c>), for saves.</summary>
    internal int CutsceneStarter
    {
        get => _cutsceneStarter;
        set => _cutsceneStarter = value;
    }

    /// <summary>The scripts waiting for nested ones to return, for saves (<c>vm.nest</c>).</summary>
    internal IReadOnlyList<(int Number, ScriptKind Kind, int Slot)> Nested => _nested;

    /// <summary>Puts the cutscene level and the sentences a save holds back.</summary>
    internal void Restore(int cutsceneLevel, IEnumerable<Sentence> sentences)
    {
        CutsceneLevel = cutsceneLevel;
        _sentences.Clear();
        _sentences.AddRange(sentences);
        _nested.Clear();
        CurrentSlot = -1;
    }

    private void StopWhere(int number, Func<ScriptKind, bool> kind)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            var slot = Slots[i];
            if (slot.Number == number && !slot.IsDead && kind(slot.Kind))
            {
                slot.Kill();
                if (CurrentSlot == i)
                {
                    CurrentSlot = -1;
                }
            }
        }

        for (int i = 0; i < _nested.Count; i++)
        {
            if (_nested[i].Number == number && kind(_nested[i].Kind))
            {
                _nested[i] = (0, ScriptKind.Global, -1);
            }
        }
    }

    private void Start(int number, ScriptKind kind, ReadOnlyMemory<byte> code, int offset, bool freezeResistant, bool recursive, IReadOnlyList<int>? args)
    {
        int index = FreeSlot();
        var slot = Slots[index];
        slot.Number = number;
        slot.Kind = kind;
        slot.Code = code;
        slot.Offset = offset;
        slot.Status = SlotStatus.Running;
        slot.FreezeResistant = freezeResistant;
        slot.Recursive = recursive;
        slot.FreezeCount = 0;

        // runScript leaves the delay: a script stopped while it waited leaves its delay in the slot,
        // and the saves keep it; it is set again before it is read
        Array.Clear(slot.Locals);
        for (int i = 0; args is not null && i < args.Count && i < ScriptSlot.LocalCount; i++)
        {
            slot.Locals[i] = args[i];
        }

        RunNested(index);
    }

    /// <summary>Mirrors <c>getScriptSlot</c>: the first dead slot from 1.</summary>
    private int FreeSlot()
    {
        for (int i = 1; i < SlotCount; i++)
        {
            if (Slots[i].IsDead)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"Too many scripts running, {SlotCount} at most.");
    }

    /// <summary>Mirrors <c>runScriptNested</c>: run a slot until it yields, then go back to the caller if it is still there.</summary>
    private void RunNested(int index)
    {
        if (_nested.Count >= NestingLimit)
        {
            throw new InvalidOperationException("Too many nested scripts.");
        }

        int caller = CurrentSlot;
        _nested.Add(caller < 0 ? (0, ScriptKind.Global, -1) : (Slots[caller].Number, Slots[caller].Kind, caller));
        int level = _nested.Count - 1;

        // the caller's place is kept (updateScriptPtr), and the new script starts
        if (caller >= 0)
        {
            Slots[caller].ResumeOffset = Slots[caller].Offset;
        }

        CurrentSlot = index;
        Slots[index].ResumeOffset = Slots[index].Offset;
        Execute(null);

        if (_nested.Count <= level)
        {
            CurrentSlot = -1;
            return;
        }

        var nest = _nested[level];
        _nested.RemoveAt(level);
        if (nest.Number != 0)
        {
            var slot = Slots[nest.Slot];
            if (slot.Number == nest.Number && slot.Kind == nest.Kind && !slot.IsDead && slot.FreezeCount == 0)
            {
                CurrentSlot = nest.Slot;
                return;
            }
        }

        CurrentSlot = -1;
    }

    /// <summary>Mirrors <c>executeScript</c>: run instructions until the script yields or stops.</summary>
    private void Execute(bool[]? executed)
    {
        int budget = InstructionBudget;
        while (CurrentSlot >= 0)
        {
            if (executed is not null)
            {
                executed[CurrentSlot] = true;
            }

            var slot = Slots[CurrentSlot];
            if (--budget < 0)
            {
                throw new InvalidOperationException($"Script {slot.Number} runs {InstructionBudget} instructions without yielding.");
            }

            var instruction = _decoder.Decode(slot.Code.Span, slot.Offset);
            slot.Offset = instruction.End;
            Run(slot, instruction);
        }
    }

    private void Run(ScriptSlot slot, Instruction instruction)
    {
        var previous = CurrentInstruction;
        CurrentInstruction = instruction;
        try
        {
            RunInstruction(slot, instruction);
        }
        finally
        {
            CurrentInstruction = previous;
        }
    }

    private void RunInstruction(ScriptSlot slot, Instruction instruction)
    {
        int[] args = [.. instruction.Operands.Select(Evaluate)];
        int Arg(int i) => args[i];
        int ResultNumber() => VariableNumber(instruction.Result!.Value);
        void SetResult(int value) => WriteVar(ResultNumber(), value);
        int[] SubArgs(int i) => [.. instruction.SubOps[i].Operands.Select(Evaluate)];

        // jumpRelative: carry on when the condition holds, otherwise jump
        void Jump(bool condition)
        {
            if (!condition)
            {
                slot.Offset = instruction.JumpTarget!.Value;
            }
        }

        switch (instruction.Handler)
        {
            case "o5_stopObjectCode":
                StopObjectCode();
                break;
            case "o5_breakHere":
                BreakHere();
                break;
            case "o5_jumpRelative":
                Jump(false);
                break;

            // the comparisons work on 16 bits: the variable first, the value second
            case "o5_isEqual":
                Jump((short)Arg(1) == (short)Arg(0));
                break;
            case "o5_isNotEqual":
                Jump((short)Arg(1) != (short)Arg(0));
                break;
            case "o5_isGreater":
                Jump((short)Arg(1) > (short)Arg(0));
                break;
            case "o5_isGreaterEqual":
                Jump((short)Arg(1) >= (short)Arg(0));
                break;
            case "o5_isLess":
                Jump((short)Arg(1) < (short)Arg(0));
                break;
            case "o5_isLessEqual":
                Jump((short)Arg(1) <= (short)Arg(0));
                break;
            case "o5_equalZero":
                Jump(Arg(0) == 0);
                break;
            case "o5_notEqualZero":
                Jump(Arg(0) != 0);
                break;

            case "o5_move":
                SetResult(Arg(0));
                break;
            case "o5_add":
                SetResult(ReadVar(ResultNumber()) + Arg(0));
                break;
            case "o5_subtract":
                SetResult(ReadVar(ResultNumber()) - Arg(0));
                break;
            case "o5_multiply":
                SetResult(ReadVar(ResultNumber()) * Arg(0));
                break;
            case "o5_divide":
                SetResult(Arg(0) == 0 ? 0 : ReadVar(ResultNumber()) / Arg(0));
                break;
            case "o5_and":
                SetResult(ReadVar(ResultNumber()) & Arg(0));
                break;
            case "o5_or":
                SetResult(ReadVar(ResultNumber()) | Arg(0));
                break;
            case "o5_increment":
                SetResult(ReadVar(ResultNumber()) + 1);
                break;
            case "o5_decrement":
                SetResult(ReadVar(ResultNumber()) - 1);
                break;
            case "o5_setVarRange":
                {
                    int first = ResultNumber();
                    for (int i = 0; i < args.Length; i++)
                    {
                        WriteVar(first + i, Arg(i));
                    }
                }

                break;
            case "o5_getRandomNr":
                SetResult(Random.Next(Arg(0)));
                break;
            case "o5_expression":
                SetResult(Expression(slot, instruction));
                break;

            case "o5_delay":
                slot.Delay = Arg(0);
                slot.Status = SlotStatus.Paused;
                BreakHere();
                break;
            case "o5_delayVariable":
                slot.Delay = Arg(0);
                slot.Status = SlotStatus.Paused;
                BreakHere();
                break;

            case "o5_startScript":
                {
                    int script = Arg(0);

                    // ScummVM skips the copy protection as the LucasArts Classic Adventures release did
                    if (!CopyProtection && !FmTowns && script == 201 && CurrentRoom == CopyProtectionRoom)
                    {
                        script = 205;
                    }

                    RunScript(script, (instruction.Opcode & 0x20) != 0, (instruction.Opcode & 0x40) != 0, SubArgs(0));
                }

                break;
            case "o5_chainScript":
                {
                    int script = Arg(0);
                    bool freezeResistant = slot.FreezeResistant, recursive = slot.Recursive;
                    var vars = SubArgs(0);
                    slot.Number = 0;
                    slot.Status = SlotStatus.Dead;
                    CurrentSlot = -1;
                    RunScript(script, freezeResistant, recursive, vars);
                }

                break;
            case "o5_stopScript":
                if (Arg(0) == 0)
                {
                    StopObjectCode();
                }
                else
                {
                    StopScript(Arg(0));
                }

                break;
            case "o5_startObject":
                RunObjectScript(Arg(0), Arg(1), args: SubArgs(0));
                break;
            case "o5_stopObjectScript":
                StopObjectScript(Arg(0));
                break;
            case "o5_isScriptRunning":
                SetResult(IsScriptRunning(Arg(0)) ? 1 : 0);
                break;
            case "o5_freezeScripts":
                if (Arg(0) != 0)
                {
                    FreezeScripts(Arg(0));
                }
                else
                {
                    UnfreezeScripts();
                }

                break;

            case "o5_cutscene":
                BeginCutscene(SubArgs(0));
                break;
            case "o5_endCutscene":
                EndCutscene();
                break;
            case "o5_beginOverride":
                if (Arg(0) != 0)
                {
                    // the override skips to the jump that follows (beginOverride records where it is)
                    _cutsceneOffset[CutsceneLevel] = instruction.Offset + 2;
                    _cutsceneSlot[CutsceneLevel] = CurrentSlot;
                }
                else
                {
                    _cutsceneOffset[CutsceneLevel] = 0;
                    _cutsceneSlot[CutsceneLevel] = 0;
                    Variables[Vars.Override] = 0;
                }

                break;

            case "o5_doSentence":
                if (Arg(0) == 0xFE)
                {
                    _sentences.Clear();
                    StopScript(Variables[Vars.SentenceScript]);
                    _host.Execute(this, instruction, args, []);
                }
                else
                {
                    DoSentence(Arg(0), Arg(1), Arg(2));
                }

                break;

            default:
                {
                    var subArgs = Enumerable.Range(0, instruction.SubOps.Count).Select(SubArgs).ToList();
                    int value = _host.Execute(this, instruction, args, subArgs);
                    if (instruction.JumpTarget is not null)
                    {
                        Jump(value != 0);
                    }
                    else if (instruction.Result is { } result)
                    {
                        WriteResult(slot, result, value);
                    }
                }

                break;
        }
    }

    /// <summary>
    /// Writes a host's result. A local variable belongs to the slot that ran the instruction,
    /// which may no longer be the running one when the instruction started another script.
    /// </summary>
    private void WriteResult(ScriptSlot slot, Operand result, int value)
    {
        if (_discardResult)
        {
            _discardResult = false;
            return;
        }

        int number = VariableNumber(result);
        if ((number & 0xF000) == Operand.LocalVariable)
        {
            slot.Locals[CheckLocal(number & 0x0FFF)] = value;
        }
        else
        {
            WriteVar(number, value);
        }
    }

    /// <summary>Mirrors <c>o5_expression</c>: a stack machine over values, the four operations and nested instructions.</summary>
    private int Expression(ScriptSlot slot, Instruction instruction)
    {
        _stack.Clear();
        foreach (var sub in instruction.SubOps)
        {
            switch (sub.Code)
            {
                case 1:
                    _stack.Push(Evaluate(sub.Operands[0]));
                    break;
                case 2:
                    {
                        int i = _stack.Pop();
                        _stack.Push(i + _stack.Pop());
                    }

                    break;
                case 3:
                    {
                        int i = _stack.Pop();
                        _stack.Push(_stack.Pop() - i);
                    }

                    break;
                case 4:
                    {
                        int i = _stack.Pop();
                        _stack.Push(i * _stack.Pop());
                    }

                    break;
                case 5:
                    {
                        int i = _stack.Pop();
                        if (i == 0)
                        {
                            throw new InvalidOperationException("Divide by zero in an expression.");
                        }

                        _stack.Push(_stack.Pop() / i);
                    }

                    break;
                case 6:
                    {
                        // the nested instruction's own result goes to its variable; the expression takes variable 0
                        var saved = _stack.ToArray();
                        Run(slot, sub.Nested!);
                        _stack.Clear();
                        for (int i = saved.Length - 1; i >= 0; i--)
                        {
                            _stack.Push(saved[i]);
                        }

                        _stack.Push(Variables[0]);
                    }

                    break;
            }
        }

        return _stack.Pop();
    }

    /// <summary>
    /// For a host: the instruction running now sets no result, as <c>o4_saveLoadGame</c> sets none
    /// when a load starts the game over.
    /// </summary>
    internal void DiscardResult() => _discardResult = true;

    /// <summary>
    /// Stops the running script and starts another in its place, with its freeze resistance and
    /// recursion. Mirrors <c>o5_chainScript</c>, as <c>terminateSaveMenuScript</c> calls it.
    /// </summary>
    internal void ChainCurrent(int script, int[] args)
    {
        var slot = Slots[RunningSlot()];
        bool freezeResistant = slot.FreezeResistant, recursive = slot.Recursive;
        slot.Number = 0;
        slot.Status = SlotStatus.Dead;
        CurrentSlot = -1;
        RunScript(script, freezeResistant, recursive, args);
    }

    /// <summary>
    /// Mirrors <c>beginCutscene</c>. Outside any script, as after a load, no slot counts the
    /// cutscene (ScummVM then writes past its slots).
    /// </summary>
    internal void BeginCutscene(int[] args)
    {
        int scr = CurrentSlot;
        if (scr >= 0)
        {
            Slots[scr].CutsceneOverride++;
        }

        if (++CutsceneLevel >= CutsceneLevels)
        {
            throw new InvalidOperationException("Cutscene stack overflow.");
        }

        _cutsceneData[CutsceneLevel] = args.Length > 0 ? args[0] : 0;
        _cutsceneSlot[CutsceneLevel] = 0;
        _cutsceneOffset[CutsceneLevel] = 0;

        _cutsceneStarter = scr;
        if (Variables[Vars.CutsceneStartScript] != 0)
        {
            RunScript(Variables[Vars.CutsceneStartScript], args: args);
        }

        _cutsceneStarter = -1;
    }

    /// <summary>Mirrors <c>endCutscene</c>; outside any script no slot counts it.</summary>
    internal void EndCutscene()
    {
        var slot = CurrentSlot >= 0 ? Slots[CurrentSlot] : null;
        if (slot?.CutsceneOverride > 0)
        {
            slot.CutsceneOverride--;
        }

        int[] args = [_cutsceneData[CutsceneLevel]];
        Variables[Vars.Override] = 0;
        if (_cutsceneOffset[CutsceneLevel] != 0 && slot?.CutsceneOverride > 0)
        {
            slot.CutsceneOverride--;
        }

        _cutsceneSlot[CutsceneLevel] = 0;
        _cutsceneOffset[CutsceneLevel] = 0;
        if (CutsceneLevel == 0)
        {
            throw new InvalidOperationException("Cutscene stack underflow.");
        }

        CutsceneLevel--;
        if (Variables[Vars.CutsceneEndScript] != 0)
        {
            RunScript(Variables[Vars.CutsceneEndScript], args: args);
        }
    }

    private int RunningSlot() => CurrentSlot >= 0 ? CurrentSlot : throw new InvalidOperationException("No script is running.");

    private static int CheckVariable(int number)
        => number is >= 0 and < Vars.Count ? number : throw new InvalidOperationException($"Variable {number} is out of range (0 to {Vars.Count - 1}).");

    private static int CheckBitVariable(int number)
        => number is >= 0 and < BitVariableCount ? number : throw new InvalidOperationException($"Bit variable {number} is out of range (0 to {BitVariableCount - 1}).");

    private static int CheckLocal(int number)
        => number is >= 0 and <= 20 ? number : throw new InvalidOperationException($"Local variable {number} is out of range (0 to 20).");
}

/// <summary>A queued sentence: a verb and up to two objects. Mirrors ScummVM's <c>SentenceTab</c>.</summary>
internal sealed class Sentence(int verb, int objectA, int objectB)
{
    public int Verb { get; } = verb;

    public int ObjectA { get; } = objectA;

    public int ObjectB { get; } = objectB;

    public int FreezeCount { get; internal set; }
}
