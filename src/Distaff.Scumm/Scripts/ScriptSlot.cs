namespace Distaff.Scumm.Scripts;

/// <summary>Mirrors ScummVM's <c>ssDead</c>, <c>ssPaused</c> and <c>ssRunning</c>; <see cref="Frozen"/> is the 0x80 bit <c>freezeScripts</c> adds.</summary>
public static class SlotStatus
{
    public const byte Dead = 0;
    public const byte Paused = 1;
    public const byte Running = 2;
    public const byte Frozen = 0x80;
}

/// <summary>What a slot's code belongs to. Mirrors ScummVM's <c>WIO_GLOBAL</c>, <c>WIO_LOCAL</c>, <c>WIO_ROOM</c> and <c>WIO_INVENTORY</c>.</summary>
public enum ScriptKind
{
    /// <summary>A numbered global script.</summary>
    Global,

    /// <summary>A numbered script of the current room, 200 and up.</summary>
    Local,

    /// <summary>Code in the current room: its entry or exit script, or an object's verb script.</summary>
    Room,

    /// <summary>The verb script of a carried object.</summary>
    Inventory,
}

/// <summary>One of the cooperative threads scripts run in. Mirrors ScummVM's <c>ScriptSlot</c> and the slot's row of <c>vm.localvar</c>.</summary>
public sealed class ScriptSlot
{
    /// <summary>Mirrors <c>NUM_SCRIPT_LOCAL</c>.</summary>
    public const int LocalCount = 25;

    /// <summary>The script, object or pseudo-number (<see cref="ScriptEngine.EntryScriptNumber"/>, <see cref="ScriptEngine.ExitScriptNumber"/>) running here.</summary>
    public int Number { get; internal set; }

    public byte Status { get; internal set; } = SlotStatus.Dead;

    public ScriptKind Kind { get; internal set; }

    /// <summary>The code the slot runs; offsets count from its start: the script resource, the room, or a carried object's code block.</summary>
    public ReadOnlyMemory<byte> Code { get; internal set; }

    /// <summary>Where execution resumes.</summary>
    public int Offset { get; internal set; }

    /// <summary>
    /// Where the slot was when it last began a turn or started a nested script: what ScummVM's
    /// <c>offs</c> holds while the slot runs, since <c>updateScriptPtr</c> only stores the script
    /// pointer then. A save made by a running script keeps this.
    /// </summary>
    public int ResumeOffset { get; internal set; }

    /// <summary>Ticks left to wait while paused.</summary>
    public int Delay { get; internal set; }

    public bool FreezeResistant { get; internal set; }

    public bool Recursive { get; internal set; }

    /// <summary>How many times <c>freezeScripts</c> has frozen the slot without a matching thaw.</summary>
    public int FreezeCount { get; internal set; }

    /// <summary>How many cutscenes and overrides the slot has begun and not ended (<c>cutsceneOverride</c>).</summary>
    public int CutsceneOverride { get; internal set; }

    public int[] Locals { get; } = new int[LocalCount];

    public bool IsDead => Status == SlotStatus.Dead;

    internal void Kill()
    {
        Number = 0;
        Status = SlotStatus.Dead;
        Code = ReadOnlyMemory<byte>.Empty;
    }
}
