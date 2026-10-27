using Distaff.Scumm.Detection;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Rooms;
using Distaff.Scumm.Storage;

// For now the player identifies a release and lists rooms; it gains a window once rooms can be drawn.
if (args.Length is not (1 or 3) || args[0] is "-h" or "--help" || (args.Length == 3 && args[1] != "--room"))
{
    Console.Error.WriteLine("usage: distaff <game folder> [--room N]");
    return 2;
}

if (!Directory.Exists(args[0]))
{
    Console.Error.WriteLine($"distaff: no such folder: {args[0]}");
    return 1;
}

var game = new DirectoryGameContainer(args[0]);
if (ScummDetector.Identify(game) is not { } release)
{
    Console.Error.WriteLine($"distaff: no Loom release found in {args[0]}");
    return 1;
}

Console.WriteLine($"Loom ({release.Variant}), SCUMM version {release.Version}, {release.Layout} layout");
Console.WriteLine($"language {release.Language ?? "unknown"}, platform {release.Platform ?? "unknown"}, index MD5 {release.Entry.Md5}");
if (args.Length == 1)
{
    return 0;
}

if (!int.TryParse(args[2], out int number))
{
    Console.Error.WriteLine($"distaff: not a room number: {args[2]}");
    return 2;
}

try
{
    if (ResourceLoader.Open(game).LoadRoom(number) is not { } room)
    {
        Console.Error.WriteLine($"distaff: there is no room {number}");
        return 1;
    }

    ListRoom(room);
    return 0;
}
catch (Exception ex) when (ex is NotSupportedException or InvalidDataException)
{
    Console.Error.WriteLine($"distaff: {ex.Message}");
    return 1;
}

static void ListRoom(Room room)
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
        Console.WriteLine($"    {o.Number,4} {name,-20} at ({o.X},{o.Y}) {o.Width}x{o.Height}, walk to ({o.WalkX},{o.WalkY}), parent {o.Parent}, verbs {verbs}");
    }
}
