namespace Distaff.Scumm.Scripts;

/// <summary>
/// The world the scripts act on: rooms, objects, actors, verbs, text and sound. The script engine
/// runs the instructions that only touch script state (variables, conditions, flow, script
/// control, cutscenes, sentences) and hands every other instruction here, with its operands
/// already evaluated.
/// </summary>
public interface IScriptHost
{
    /// <summary>A global script's code, from its resource header, or null when there is none.</summary>
    ReadOnlyMemory<byte>? GlobalScript(int number);

    /// <summary>The current room's code and where a local script (200 and up) starts in it, or null.</summary>
    (ReadOnlyMemory<byte> Code, int Offset)? LocalScript(int number);

    /// <summary>
    /// Where an object's script for a verb starts: in the room, or in the object's carried copy, or
    /// null when the object is not here or has no script for the verb. Mirrors
    /// <c>whereIsObject</c>, <c>getOBCDOffs</c> and <c>getVerbEntrypoint</c>.
    /// </summary>
    (ReadOnlyMemory<byte> Code, int Offset, ScriptKind Kind)? ObjectScript(int obj, int verb);

    /// <summary>
    /// Carries out an instruction. <paramref name="arguments"/> holds the value of each operand, in
    /// order (0 for text operands, whose bytes are in the instruction), and
    /// <paramref name="subArguments"/> those of each sub-operation. The return value is written to
    /// the instruction's result variable when it has one; for an instruction with a jump target it
    /// is the condition: non-zero carries on, zero jumps. A host makes the script wait with
    /// <see cref="ScriptEngine.Repeat"/>.
    /// </summary>
    int Execute(ScriptEngine engine, Instruction instruction, IReadOnlyList<int> arguments, IReadOnlyList<int[]> subArguments);
}
