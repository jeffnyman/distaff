using System.Globalization;
using System.Text;

namespace Distaff.Scumm.Scripts;

internal enum OperandKind
{
    /// <summary>A number in the script.</summary>
    Constant,

    /// <summary>A variable; <see cref="Operand.Value"/> is its number, flags included.</summary>
    Variable,

    /// <summary>Text in the script, up to and without its zero byte.</summary>
    Text,
}

/// <summary>
/// An operand. A variable's number carries its kind, as ScummVM's <c>readVar</c> reads it: bit
/// 0x8000 makes it a bit variable, 0x4000 a local variable of the running script, otherwise a
/// global variable. A variable whose number had bit 0x2000 has an <see cref="Index"/>, a constant
/// or another variable, added to its number when it is read.
/// </summary>
internal readonly record struct Operand(OperandKind Kind, int Value, Operand[]? IndexHolder = null, byte[]? Text = null)
{
    /// <summary>Mirrors the <c>0x8000</c> test of <c>readVar</c>.</summary>
    public const int BitVariable = 0x8000;

    /// <summary>Mirrors the <c>0x4000</c> test of <c>readVar</c>.</summary>
    public const int LocalVariable = 0x4000;

    /// <summary>Mirrors the <c>0x2000</c> test of <c>readVar</c> and <c>getResultPos</c>.</summary>
    public const int Indexed = 0x2000;

    public static Operand Constant(int value) => new(OperandKind.Constant, value);

    public static Operand Variable(int number, Operand? index = null)
        => new(OperandKind.Variable, number, index is { } i ? [i] : null);

    public static Operand String(byte[] text) => new(OperandKind.Text, text.Length, null, text);

    /// <summary>What is added to a variable's number when it is read, or null.</summary>
    public Operand? Index => IndexHolder is [var index] ? index : null;

    public override string ToString() => Kind switch
    {
        OperandKind.Variable => VariableName(Value, Index),
        OperandKind.Text => $"\"{Escape(Text!)}\"",
        _ => Value.ToString(CultureInfo.InvariantCulture),
    };

    private static string VariableName(int number, Operand? index)
    {
        string plus = index is { } i ? $" + {i}" : string.Empty;
        return (number & BitVariable) != 0 ? $"Bit[{number & 0x7FFF}{plus}]"
            : (number & LocalVariable) != 0 ? $"Local[{number & 0x0FFF}{plus}]"
            : $"Var[{number}{plus}]";
    }

    private static string Escape(byte[] text)
    {
        var builder = new StringBuilder();
        foreach (byte b in text)
        {
            builder.Append(b is >= 32 and < 127 && b != '"' && b != '\\' ? (char)b : $"\\x{b:X2}");
        }

        return builder.ToString();
    }
}

/// <summary>
/// One sub-operation of an instruction that takes a list of them, such as <c>actorOps</c> or
/// <c>print</c>, or that has one, such as <c>roomOps</c>.
/// </summary>
/// <param name="Code">The sub-operation number the handler switches on: the byte's low bits, and for <c>actorOps</c> after <c>convertTable</c>.</param>
/// <param name="Name">ScummVM's name for it, such as <c>SO_COSTUME</c>, without the prefix.</param>
/// <param name="Operands">Its operands in the order the handler fetches them.</param>
/// <param name="Nested">For <c>expression</c>'s sub-operation 6, the instruction it runs.</param>
internal sealed record SubOperation(int Code, string Name, IReadOnlyList<Operand> Operands, Instruction? Nested = null)
{
    public override string ToString()
        => Nested is { } nested ? $"{Name}({nested.Text})" : Operands.Count == 0 ? Name : $"{Name}({string.Join(", ", Operands)})";
}

/// <summary>One decoded script instruction.</summary>
/// <param name="Offset">Where it starts in the script.</param>
/// <param name="Opcode">Its first byte; for most opcodes the top 3 bits say which operands are variables.</param>
/// <param name="Handler">The ScummVM handler, such as <c>o5_putActor</c>.</param>
/// <param name="Operands">Its operands in the order the handler fetches them, before any sub-operations.</param>
/// <param name="Length">Its length in bytes.</param>
/// <param name="Result">The variable it writes, if any.</param>
/// <param name="JumpTarget">Where it jumps when its condition fails (or always, for <c>jumpRelative</c>).</param>
/// <param name="SubOperations">Its sub-operations, in order; empty for an instruction that has none.</param>
internal sealed record Instruction(
    int Offset,
    byte Opcode,
    string Handler,
    IReadOnlyList<Operand> Operands,
    int Length,
    Operand? Result = null,
    int? JumpTarget = null,
    IReadOnlyList<SubOperation>? SubOperations = null)
{
    /// <summary>The handler without its version prefix: <c>putActor</c>.</summary>
    public string Name => Handler[(Handler.IndexOf('_') + 1)..];

    public int End => Offset + Length;

    public IReadOnlyList<SubOperation> SubOps => SubOperations ?? [];

    /// <summary>
    /// Whether execution never falls through to the next instruction: stopping the script
    /// (including <c>stopScript(0)</c>, which stops the script running it), an unconditional
    /// jump, or chaining to another script.
    /// </summary>
    public bool EndsFlow => Handler is "o5_stopObjectCode" or "o5_jumpRelative" or "o5_chainScript"
        || (Handler == "o5_stopScript" && Operands[0] is { Kind: OperandKind.Constant, Value: 0 });

    /// <summary>The instruction without its offset: <c>Var[1] = getActorX(2)</c>.</summary>
    public string Text
    {
        get
        {
            var text = new StringBuilder();
            if (Result is { } result)
            {
                text.Append(result).Append(" = ");
            }

            text.Append(Name).Append('(').Append(string.Join(", ", Operands));
            if (SubOps.Count > 0)
            {
                text.Append(Operands.Count > 0 ? ", " : string.Empty).Append('[').Append(string.Join(", ", SubOps)).Append(']');
            }

            text.Append(')');
            if (JumpTarget is { } target)
            {
                text.Append($" -> [{target:X4}]");
            }

            return text.ToString();
        }
    }

    public override string ToString() => $"[{Offset:X4}] {Text}";
}
