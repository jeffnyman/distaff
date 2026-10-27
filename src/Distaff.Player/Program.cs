using Distaff.Scumm.Detection;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Rooms;
using Distaff.Scumm.Storage;

namespace Distaff.Player;

internal static class Program
{
    private static int Main(string[] args)
    {
        string? gamePath = null;
        int? roomNumber = null;
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
        if (roomNumber is not { } roomNo)
        {
            return 0;
        }

        try
        {
            return WriteRoom(ResourceLoader.Open(game), roomNo, objects, mask, zoom, output ?? $"room-{roomNo:D2}{(mask ? "-mask" : "")}.png");
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
        return 2;
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
