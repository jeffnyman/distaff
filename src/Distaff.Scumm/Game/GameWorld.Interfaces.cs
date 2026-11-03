using Distaff.Scumm.Costumes;
using Distaff.Scumm.Rooms;
using Distaff.Scumm.Scripts;

namespace Distaff.Scumm.Game;

// What the script engine and the actors ask of the world, implemented explicitly so that it stays
// out of the world's public members: the members it forwards to are internal.
public sealed partial class GameWorld
{
    ReadOnlyMemory<byte>? IScriptHost.GlobalScript(int number) => GlobalScript(number);

    (ReadOnlyMemory<byte> Code, int Offset)? IScriptHost.LocalScript(int number) => LocalScript(number);

    (ReadOnlyMemory<byte> Code, int Offset, ScriptKind Kind)? IScriptHost.ObjectScript(int obj, int verb) => ObjectScript(obj, verb);

    int IScriptHost.Execute(ScriptEngine engine, Instruction instruction, IReadOnlyList<int> arguments, IReadOnlyList<int[]> subArguments)
        => Execute(engine, instruction, arguments, subArguments);

    WalkBoxes? IActorWorld.Boxes => Boxes;

    int IActorWorld.BoxFlags(int box) => BoxFlags(box);

    int IActorWorld.BoxScale(int box, int x, int y) => BoxScale(box, x, y);

    bool IActorWorld.GetClass(int obj, int cls) => GetClass(obj, cls);

    Costume? IActorWorld.LoadCostume(int number) => LoadCostume(number);

    bool IActorWorld.OldBundle => OldBundle;
}
