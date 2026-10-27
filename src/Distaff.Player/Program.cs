using Distaff.Audio;
using Distaff.Player.Sdl;
using Distaff.Scumm;
using Distaff.Scumm.Detection;
using Distaff.Scumm.Game;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Hosting;
using Distaff.Scumm.Input;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Rooms;
using Distaff.Scumm.Scripts;
using Distaff.Scumm.Sound;
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
        var keys = new List<(int Step, Key Key, char Ascii)>();
        int scale = 3;
        bool fullscreen = false;
        string? load = null;
        var saves = new List<(int Step, string Path)>();
        string savesDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Distaff", "saves");
        string target = "loom";
        var sound = SoundDevice.AdLib;
        string? wav = null;
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
                case "--key" when i + 1 < args.Length && ParseKey(args[i + 1]) is { } key:
                    keys.Add(key);
                    i++;
                    break;
                case "--scale" when i + 1 < args.Length && int.TryParse(args[i + 1], out int sc) && sc is >= 1 and <= 8:
                    scale = sc;
                    i++;
                    break;
                case "--fullscreen":
                    fullscreen = true;
                    break;
                case "--load" when i + 1 < args.Length:
                    load = args[++i];
                    break;
                case "--save" when i + 1 < args.Length && args[i + 1].IndexOf(':') is > 0 and var colon && int.TryParse(args[i + 1][..colon], out int saveStep):
                    saves.Add((saveStep, args[++i][(colon + 1)..]));
                    break;
                case "--saves" when i + 1 < args.Length:
                    savesDirectory = args[++i];
                    break;
                case "--target" when i + 1 < args.Length:
                    target = args[++i];
                    break;
                case "--sound" when i + 1 < args.Length && Enum.TryParse<SoundDevice>(args[i + 1], ignoreCase: true, out var device):
                    sound = device;
                    i++;
                    break;
                case "--wav" when i + 1 < args.Length:
                    wav = args[++i];
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
                var store = new FileSaveStore(savesDirectory);
                return Run(ResourceLoader.Open(game), runSteps, copyProtection, clicks, keys, screenshot, zoom, traceActor, load, saves, store, target, sound, wav, CdTrackFiles.In(gamePath));
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

            return Play(game, release, copyProtection, scale, fullscreen, load is null ? null : File.ReadAllBytes(load), savesDirectory, target, sound, CdTrackFiles.In(gamePath));
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidDataException)
        {
            Console.Error.WriteLine($"distaff: {ex.Message}");
            return 1;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: distaff <game folder> [--scale N] [--fullscreen] [--copy-protection] [--sound speaker|pcjr|adlib] [--load file] [--saves folder] [--target name]");
        Console.Error.WriteLine("       distaff <game folder> --room N [--objects none|initial|all] [--mask] [--zoom N] [--output file.png]");
        Console.Error.WriteLine("       distaff <game folder> --script N | --room-scripts N");
        Console.Error.WriteLine("       distaff <game folder> --run [steps] [--click step:x,y]... [--key step:K]... [--load file] [--save step:file]... [--sound speaker|pcjr|adlib] [--wav file.wav] [--trace-actor N] [--copy-protection] [--screenshot file.png] [--zoom N]");
        return 2;
    }

    /// <summary>
    /// Plays the game in a window: the mouse and keys go to the game, which draws its own cursor.
    /// Alt+Enter or F11 switches to full screen. The game's save screen (F5) keeps its saves in
    /// ScummVM's format in the saves folder, named after the target (<c>loom.s01</c>); a save
    /// given starts the game as ScummVM's launcher does. The VGA CD's audio plays from its track
    /// file in the game's folder.
    /// </summary>
    private static int Play(DirectoryGameContainer game, ScummGame release, bool copyProtection, int scale, bool fullscreen, byte[]? load, string savesDirectory, string target, SoundDevice sound,
        Func<int, ICdTrack?> cdTracks)
    {
        using var host = new SdlHost(new SdlHostOptions { Title = "Distaff", Scale = scale, Fullscreen = fullscreen, Saves = new FileSaveStore(savesDirectory) });
        var detected = new DetectedGame("scumm", "loom", "Loom", release.Variant, release.Language, release.Platform);
        using var engine = new ScummEngine(host, game, detected) { CopyProtection = copyProtection, StartingSave = load, SaveTarget = target, SoundDevice = sound, CdTracks = cdTracks };
        SDL.SDL3.SDL_HideCursor();
        host.Run(engine);
        return 0;
    }

    /// <summary>A click given as <c>step:x,y</c>, in screen pixels.</summary>
    private static (int Step, int X, int Y)? ParseClick(string text)
    {
        var parts = text.Split(':', ',');
        return parts.Length == 3 && int.TryParse(parts[0], out int step) && int.TryParse(parts[1], out int x) && int.TryParse(parts[2], out int y)
            ? (step, x, y)
            : null;
    }

    /// <summary>A key given as <c>step:K</c>: a character, <c>esc</c>, <c>space</c>, <c>enter</c> or <c>f1</c> to <c>f9</c>.</summary>
    private static (int Step, Key Key, char Ascii)? ParseKey(string text)
    {
        int colon = text.IndexOf(':');
        if (colon < 1 || !int.TryParse(text[..colon], out int step))
        {
            return null;
        }

        string name = text[(colon + 1)..];
        return name.ToLowerInvariant() switch
        {
            "esc" => (step, Key.Escape, (char)27),
            "space" => (step, Key.Space, ' '),
            "enter" => (step, Key.Return, (char)13),
            ['f', >= '1' and <= '9'] => (step, Key.F1 + (name[1] - '1'), (char)(315 + name[1] - '1')),
            [var c] => (step, (Key)char.ToLowerInvariant(c), c),
            _ => null,
        };
    }

    /// <summary>
    /// Runs the game headless from boot, or from a save, for some steps, with clicks and keys at
    /// given steps, saving at others as ScummVM's menu saves, and reports where it got to. With a
    /// WAV file, the sound is read as a host would, a step's worth after each step, and written in
    /// stereo, the VGA CD's track mixed in.
    /// </summary>
    private static int Run(ResourceLoader loader, int steps, bool copyProtection, List<(int Step, int X, int Y)> clicks, List<(int Step, Key Key, char Ascii)> keys, string? screenshot, int zoom, int? traceActor,
        string? load, List<(int Step, string Path)> saves, ISaveStore store, string target, SoundDevice sound, string? wav, Func<int, ICdTrack?> cdTracks)
    {
        var world = new GameWorld(loader, sound: sound, cdTracks: wav is null ? null : cdTracks) { CopyProtection = copyProtection, SaveStore = store, SaveTarget = target };
        var audio = new List<short>();
        long framesRead = 0;
        void ReadAudio()
        {
            long due = (long)(world.Time.TotalSeconds * GameWorld.DefaultSampleRate);
            if (wav is null || due <= framesRead)
            {
                return;
            }

            var buffer = new short[(due - framesRead) * 2];
            world.Sound.Output.Read(buffer);
            audio.AddRange(buffer);

            framesRead = due;
        }

        if (load is not null)
        {
            var info = world.Load(File.ReadAllBytes(load));
            Console.WriteLine($"loaded \"{info.Name}\" (save version {info.Version}, {info.PlayTime.TotalSeconds:F0}s played): room {world.CurrentRoomNumber}");
        }
        else
        {
            world.Boot();
        }

        int room = -1;
        for (int step = 0; step < steps && !world.QuitRequested; step++)
        {
            foreach (var click in clicks.Where(c => c.Step == step))
            {
                world.Click(click.X, click.Y);
            }

            foreach (var key in keys.Where(k => k.Step == step))
            {
                world.KeyDown(key.Key, KeyModifiers.None, key.Ascii);
            }

            foreach (var (_, path) in saves.Where(sv => sv.Step == step))
            {
                File.WriteAllBytes(path, world.Save(Path.GetFileNameWithoutExtension(path), DateTime.Now));
                Console.WriteLine($"step {world.Steps,5}: saved {path}");
            }

            world.Step();
            ReadAudio();
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
        Console.WriteLine($"verbs on: {string.Join(' ', world.Verbs.Where(v => v.VerbId != 0 && v.Mode == 1 && v.SaveId == 0).Select(v => $"{v.VerbId}@({v.CurRect.Left},{v.CurRect.Top})-({v.CurRect.Right},{v.CurRect.Bottom})"))}");
        Console.WriteLine($"running scripts: {string.Join(' ', world.Engine.Slots.Where(s => !s.IsDead).Select(s => $"{s.Number}({s.Kind}{(s.Status == 1 ? $" wait {s.Delay}" : "")})"))}");
        foreach (var group in world.Unhandled.GroupBy(u => u[(u.IndexOf(']') + 2)..].Split('(')[0]))
        {
            Console.WriteLine($"unhandled {group.Key}: {group.Count()}, first {group.First()}");
        }

        if (wav is not null)
        {
            File.WriteAllBytes(wav, WavWriter.Encode(audio.ToArray(), GameWorld.DefaultSampleRate, 2));
            Console.WriteLine($"wrote {wav}: {audio.Count / 2.0 / GameWorld.DefaultSampleRate:F1} seconds, peak {audio.DefaultIfEmpty().Max(s => Math.Abs((int)s))}");
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
        Print(new ScriptDecoder(loader.Version).DecodeReachable(code, [0]).Values, code.Length);
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

        var decoder = new ScriptDecoder(loader.Version);
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
        File.WriteAllBytes(output, PngWriter.Encode(surface, mask ? Palettes.Ega : RoomRenderer.PaletteOf(room), zoom, zoom));
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
