using Distaff.Scumm.Scripts;

namespace Distaff.Scumm.Tests;

/// <summary>The script engine on small hand-made scripts.</summary>
public class ScriptEngineTests
{
    // 0x1A move, 0x9A move from a variable, 0x0A startScript, 0x80 breakHere, 0x00 stopObjectCode,
    // 0x40 cutscene, 0x58 beginOverride, 0xC0 endCutscene
    private static byte[] Script(params byte[] code) => [0, 0, 0, 0, .. code];

    [Fact]
    public void A_started_script_runs_until_it_yields_and_its_caller_carries_on()
    {
        var host = new Host
        {
            [1] = Script(0x1A, 10, 0, 5, 0, 0x0A, 2, 0xFF, 0x1A, 11, 0, 7, 0, 0x00),
            [2] = Script(0x80, 0x1A, 13, 0, 1, 0, 0x00),
        };
        var engine = new ScriptEngine(host);

        engine.RunScript(1);

        Assert.Equal((5, 7, 0), (engine.Variables[10], engine.Variables[11], engine.Variables[13]));
        Assert.True(engine.IsScriptRunning(2));
        Assert.False(engine.IsScriptRunning(1));

        engine.RunAllScripts();

        Assert.Equal(1, engine.Variables[13]);
        Assert.False(engine.IsScriptRunning(2));
    }

    [Fact]
    public void Bit_variables_are_bits_of_the_variables()
    {
        var engine = new ScriptEngine(new Host());
        int bit3435 = Operand.BitVariable | 3435;

        engine.WriteVar(bit3435, 1);

        Assert.Equal(1 << 11, engine.Variables[214]);
        Assert.Equal(1, engine.ReadVar(bit3435));
    }

    [Fact]
    public void An_indexed_variable_adds_its_index()
    {
        var engine = new ScriptEngine(new Host());
        engine.Variables[3] = 4;

        Assert.Equal(12, engine.VariableNumber(Operand.Variable(5, Operand.Constant(7))));
        Assert.Equal(9, engine.VariableNumber(Operand.Variable(5, Operand.Variable(3))));
    }

    [Fact]
    public void Arguments_are_the_local_variables()
    {
        var host = new Host { [3] = Script(0x9A, 20, 0, 1, 0x40, 0x00) };
        var engine = new ScriptEngine(host);

        engine.RunScript(3, args: [0, 42]);

        Assert.Equal(42, engine.Variables[20]);
    }

    [Fact]
    public void Frozen_scripts_wait_until_thawed_as_often()
    {
        var host = new Host { [2] = Script(0x80, 0x1A, 13, 0, 1, 0, 0x00) };
        var engine = new ScriptEngine(host);
        engine.RunScript(2);

        engine.FreezeScripts(1);
        engine.FreezeScripts(1);
        engine.UnfreezeScripts();
        engine.RunAllScripts();
        Assert.Equal(0, engine.Variables[13]);

        engine.UnfreezeScripts();
        engine.RunAllScripts();
        Assert.Equal(1, engine.Variables[13]);
    }

    /// <summary>Skipping a cutscene sends the script that set the override to the jump after it.</summary>
    [Fact]
    public void Aborting_a_cutscene_takes_the_override_jump()
    {
        var host = new Host
        {
            [4] = Script(
                0x40, 0xFF,                   // 04 cutscene()
                0x58, 0x01, 0x18, 0x08, 0x00, // 06 beginOverride, jump to 13
                0x80,                         // 0B breakHere
                0x1A, 30, 0, 1, 0,            // 0C Var[30] = 1
                0xC0, 0x00,                   // 11 endCutscene, stop
                0x1A, 31, 0, 1, 0,            // 13 Var[31] = 1
                0xC0, 0x00),                  // 18 endCutscene, stop
        };
        var engine = new ScriptEngine(host);
        engine.RunScript(4);
        Assert.Equal(1, engine.CutsceneLevel);

        engine.AbortCutscene();
        Assert.Equal(1, engine.Variables[Vars.Override]);
        engine.RunAllScripts();

        Assert.Equal((0, 1), (engine.Variables[30], engine.Variables[31]));
        Assert.Equal(0, engine.CutsceneLevel);
        Assert.Equal(0, engine.Variables[Vars.Override]);
    }

    private sealed class Host : IScriptHost, IEnumerable<KeyValuePair<int, byte[]>>
    {
        private readonly Dictionary<int, byte[]> _scripts = [];

        public byte[] this[int number]
        {
            set => _scripts[number] = value;
        }

        public ReadOnlyMemory<byte>? GlobalScript(int number) => _scripts.TryGetValue(number, out var code) ? code : null;

        public (ReadOnlyMemory<byte> Code, int Offset)? LocalScript(int number) => null;

        public (ReadOnlyMemory<byte> Code, int Offset, ScriptKind Kind)? ObjectScript(int obj, int verb) => null;

        public int Execute(ScriptEngine engine, Instruction instruction, IReadOnlyList<int> arguments, IReadOnlyList<int[]> subArguments) => 0;

        public IEnumerator<KeyValuePair<int, byte[]>> GetEnumerator() => _scripts.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
