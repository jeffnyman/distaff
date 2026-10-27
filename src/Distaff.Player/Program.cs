using Distaff.Scumm.Detection;
using Distaff.Scumm.Game;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Rooms;
using Distaff.Scumm.Scripts;
using Distaff.Scumm.Storage;

namespace Distaff.Player;

internal static class Program
{
    private static int Main(string[] args)
    {
        string? gamePath = null;
        int? roomNumber = null;
        int? script = null;
        int? roomScripts = null;
        int? run = null;
        string? screenshot = null;
        bool copyProtection = false;
        var clicks = new List<(int Step, int X, int Y)>();
        int? traceActor = null;
        string objects = "initial";
        bool mask = false;
        int zoom = 1;
        string? output = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--room" when i + 1 < args.Length && int.TryParse(args[i + 1], out int number):
                    roomNumber = number;
                    i++;
                    break;
                case "--script" when i + 1 < args.Length && int.TryParse(args[i + 1], out int s):
                    script = s;
                    i++;
                    break;
                case "--room-scripts" when i + 1 < args.Length && int.TryParse(args[i + 1], out int rs):
                    roomScripts = rs;
                    i++;
                    break;
                case "--run":
                    run = i + 1 < args.Length && int.TryParse(args[i + 1], out int steps) ? steps : 600;
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out _))
                    {
                        i++;
                    }

                    break;
                case "--screenshot" when i + 1 < args.Length:
                    screenshot = args[++i];
                    break;
                case "--click" when i + 1 < args.Length && ParseClick(args[i + 1]) is { } click:
                    clicks.Add(click);
                    i++;
                    break;
                case "--trace-actor" when i + 1 < args.Length && int.TryParse(args[i + 1], out int traced):
                    traceActor = traced;
                    i++;
                    break;
                case "--copy-protection":
                    copyProtection = true;
                    break;
                case "--objects" when i + 1 < args.Length && args[i + 1] is "none" or "initial" or "all":
                    objects = args[++i];
                    break;
                case "--mask":
                    mask = true;
                    break;
                case "--zoom" when i + 1 < args.Length && int.TryParse(args[i + 1], out int z) && z is >= 1 and <= 8:
                    zoom = z;
                    i++;
                    break;
                case "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "-h" or "--help":
                    return Usage();
                default:
                    if (gamePath is not null || args[i].StartsWith('-'))
                    {
                        return Usage();
                    }

                    gamePath = args[i];
                    break;
            }
        }

        if (gamePath is null)
        {
            return Usage();
        }

        if (!Directory.Exists(gamePath))
        {
            Console.Error.WriteLine($"distaff: no such folder: {gamePath}");
            return 1;
        }

        var game = new DirectoryGameContainer(gamePath);
        if (ScummDetector.Identify(game) is not { } release)
        {
            Console.Error.WriteLine($"distaff: no Loom release found in {gamePath}");
            return 1;
        }

        Console.WriteLine($"Loom ({release.Variant}), SCUMM version {release.Version}, {release.Layout} layout");
        Console.WriteLine($"language {release.Language ?? "unknown"}, platform {release.Platform ?? "unknown"}, index MD5 {release.Entry.Md5}");
        try
        {
            if (run is { } runSteps)
            {
                return Run(ResourceLoader.Open(game), runSteps, copyProtection, clicks, screenshot, zoom, traceActor);
            }

            if (script is { } scriptNo)
            {
                return ListScript(ResourceLoader.Open(game), scriptNo);
            }

            if (roomScripts is { } roomScriptsNo)
            {
                return ListRoomScripts(ResourceLoader.Open(game), roomScriptsNo);
            }

            if (roomNumber is { } roomNo)
            {
                return WriteRoom(ResourceLoader.Open(game), roomNo, objects, mask, zoom, output ?? $"room-{roomNo:D2}{(mask ? "-mask" : "")}.png");
            }

            return 0;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidDataException)
        {
            Console.Error.WriteLine($"distaff: {ex.Message}");
            return 1;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: distaff <game folder>");
        Console.Error.WriteLine("       distaff <game folder> --room N [--objects none|initial|all] [--mask] [--zoom N] [--output file.png]");
        Console.Error.WriteLine("       distaff <game folder> --script N | --room-scripts N");
        Console.Error.WriteLine("       distaff <game folder> --run [steps] [--click step:x,y]... [--trace-actor N] [--copy-protection] [--screenshot file.png] [--zoom N]");
        return 2;
    }

    /// <summary>Runs the game headless from boot for some steps, and reports where it got to.</summary>
    /// <summary>A click given as <c>step:x,y</c>, in screen pixels.</summary>
    private static (int Step, int X, int Y)? ParseClick(string text)
    {
        var parts = text.Split(':', ',');
        return parts.Length == 3 && int.TryParse(parts[0], out int step) && int.TryParse(parts[1], out int x) && int.TryParse(parts[2], out int y)
            ? (step, x, y)
            : null;
    }

    private static int Run(ResourceLoader loader, int steps, bool copyProtection, List<(int Step, int X, int Y)> clicks, string? screenshot, int zoom, int? traceActor)
    {
        var world = new GameWorld(loader) { CopyProtection = copyProtection };
        world.Boot();
        int room = -1;
        for (int step = 0; step < steps && !world.QuitRequested; step++)
        {
            foreach (var click in clicks.Where(c => c.Step == step))
            {
                world.Click(click.X, click.Y);
            }

            world.Step();
            if (traceActor is { } t)
            {
                var a = world.Actors[t];
                Console.WriteLine($"step {world.Steps,5}: actor {t} room {a.Room} at ({a.X},{a.Y}) box {a.WalkBox} moving {a.Moving} facing {a.Facing} frame {a.Frame} visible {a.Visible} camera {world.Camera.X}");
            }

            if (world.CurrentRoomNumber != room)
            {
                room = world.CurrentRoomNumber;
                Console.WriteLine($"step {world.Steps,5} ({world.Time.TotalSeconds:F1}s): room {room}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"after {world.Steps} steps ({world.Time.TotalSeconds:F1}s): room {world.CurrentRoomNumber}, camera at {world.Camera.X}");
        Console.WriteLine($"messages: {string.Join(" | ", world.MessageLog.Append(world.MessageText).Where(m => m.Length > 0))}");
        Console.WriteLine($"sounds started: {string.Join(' ', world.SoundsStarted)}");
        Console.WriteLine($"running scripts: {string.Join(' ', world.Engine.Slots.Where(s => !s.IsDead).Select(s => $"{s.Number}({s.Kind}{(s.Status == 1 ? $" wait {s.Delay}" : "")})"))}");
        foreach (var group in world.Unhandled.GroupBy(u => u[(u.IndexOf(']') + 2)..].Split('(')[0]))
        {
            Console.WriteLine($"unhandled {group.Key}: {group.Count()}, first {group.First()}");
        }

        if (screenshot is not null)
        {
            File.WriteAllBytes(screenshot, PngWriter.Encode(world.Screen(), world.DisplayPalette(), zoom, zoom));
            Console.WriteLine($"wrote {screenshot}");
        }

        return 0;
    }

    /// <summary>Disassembles a global script, from its start along every path execution can take.</summary>
    private static int ListScript(ResourceLoader loader, int number)
    {
        if (loader.LoadScript(number) is not { } code)
        {
            Console.Error.WriteLine($"distaff: there is no script {number}");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"Script {number}: {code.Length} bytes, in room {loader.Index.Locate(ResourceType.Script, number).Room}");
        Print(new ScriptDecoder().DecodeReachable(code, [0]).Values, code.Length);
        return 0;
    }

    /// <summary>Disassembles a room's entry, exit and local scripts and its objects' verb scripts.</summary>
    private static int ListRoomScripts(ResourceLoader loader, int number)
    {
        if (loader.LoadRoom(number) is not { } room)
        {
            Console.Error.WriteLine($"distaff: there is no room {number}");
            return 1;
        }

        var decoder = new ScriptDecoder();
        void Room(string title, int offset)
        {
            if (offset == 0)
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"{title}, at {offset} in room {number}:");
            Print(decoder.DecodeReachable(room.Data, [offset]).Values, null);
        }

        Room("Exit script", room.ExitScriptOffset);
        Room("Entry script", room.EntryScriptOffset);
        foreach (var (script, offset) in room.LocalScriptOffsets)
        {
            Room($"Local script {script}", offset);
        }

        foreach (var obj in room.Objects.Where(o => o.Verbs.Count > 0))
        {
            Console.WriteLine();
            string verbs = string.Join(", ", obj.Verbs.Select(v => $"{(v.Verb == RoomObject.FallbackVerb ? "*" : v.Verb.ToString())} at {v.Offset:X4}"));
            Console.WriteLine($"Object {obj.Number} {(obj.Name.Length > 0 ? $"\"{obj.Name}\"" : "")}, verbs {verbs}:");
            Print(decoder.DecodeReachable(obj.Code, obj.Verbs.Select(v => v.Offset)).Values, null);
        }

        return 0;
    }

    private static void Print(IEnumerable<Instruction> instructions, int? length)
    {
        int count = 0, covered = 0;
        foreach (var instruction in instructions)
        {
            Console.WriteLine($"  {instruction}");
            count++;
            covered += instruction.Length;
        }

        if (length is { } total && covered < total)
        {
            Console.WriteLine($"  ({total - covered} of {total} bytes not reached)");
        }
    }

    /// <summary>Writes a room as a PNG, with its objects as the game starts, none, or all, and lists it.</summary>
    private static int WriteRoom(ResourceLoader loader, int number, string objects, bool mask, int zoom, string output)
    {
        if (loader.LoadRoom(number) is not { } room)
        {
            Console.Error.WriteLine($"distaff: there is no room {number}");
            return 1;
        }

        var states = loader.Index.ObjectStates;
        int StateOf(int obj) => obj < states.Count ? states[obj] : 0;

        var image = objects switch
        {
            "none" => RoomRenderer.DrawBackground(room),
            "all" => RoomRenderer.RenderAll(room),
            _ => RoomRenderer.Render(room, StateOf),
        };

        // the mask shows white where it hides actors
        var surface = mask ? MaskPicture(image.Mask) : image.Pixels;
        File.WriteAllBytes(output, PngWriter.Encode(surface, Palettes.Ega, zoom, zoom));
        Console.WriteLine($"wrote {output}");

        ListRoom(room, StateOf);
        return 0;
    }

    private static Surface8 MaskPicture(Surface8 mask)
    {
        var picture = new Surface8(mask.Width, mask.Height);
        for (int i = 0; i < mask.Pixels.Length; i++)
        {
            picture.Pixels[i] = mask.Pixels[i] != 0 ? (byte)15 : (byte)0;
        }

        return picture;
    }

    private static void ListRoom(Room room, Func<int, int> stateOf)
    {
        Console.WriteLine();
        Console.WriteLine($"Room {room.Number}: {room.Width}x{room.Height}, {room.Data.Length} bytes, picture at {room.ImageOffset}");
        Console.WriteLine($"  sounds: {string.Join(' ', room.Sounds)}");
        Console.WriteLine($"  scripts: {string.Join(' ', room.Scripts)}");
        Console.WriteLine($"  local scripts: {string.Join(' ', room.LocalScriptOffsets.Select(s => $"{s.Key}@{s.Value}"))}");
        Console.WriteLine($"  exit script at {room.ExitScriptOffset} ({room.ExitScript.Length} bytes), entry script at {room.EntryScriptOffset} ({room.EntryScript.Length} bytes)");
        Console.WriteLine($"  walkboxes: {room.WalkBoxes.Count}, matrix {room.WalkBoxes.Matrix.Length} bytes");
        Console.WriteLine($"  objects: {room.Objects.Count}");
        foreach (var o in room.Objects)
        {
            string name = o.Name.Length > 0 ? $"\"{o.Name}\"" : "(no name)";
            string verbs = string.Join(' ', o.Verbs.Select(v => v.Verb == RoomObject.FallbackVerb ? "*" : v.Verb.ToString()));
            Console.WriteLine($"    {o.Number,4} {name,-20} state {stateOf(o.Number)}, at ({o.X},{o.Y}) {o.Width}x{o.Height}, walk to ({o.WalkX},{o.WalkY}), parent {o.Parent}, verbs {verbs}");
        }
    }
}
