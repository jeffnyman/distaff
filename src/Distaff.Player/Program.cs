using Distaff.Audio;
using Distaff.Audio.Mt32;
using Distaff.Player.Sdl;
using Distaff.Scumm;
using Distaff.Scumm.Game;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Hosting;
using Distaff.Scumm.Input;
using Distaff.Scumm.Inspection;
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
        int? bootParam = null;
        var clicks = new List<(int Step, int X, int Y)>();
        var keys = new List<(int Step, Key Key, char Ascii)>();
        int scale = 3;
        bool fullscreen = false;
        TimeSpan? playFor = null;
        string? load = null;
        var saves = new List<(int Step, string Path)>();
        string savesDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Distaff", "saves");
        string? target = null;
        var sound = SoundDevice.AdLib;
        bool soundGiven = false;
        string? mt32Roms = null;
        var mt32Model = Mt32Model.Auto;
        string? wav = null;
        string? video = null;
        string ffmpeg = "ffmpeg";
        bool smoothScroll = true;
        bool semiSmoothScroll = false;
        int textSpeed = GameWorld.DefaultTextSpeed;
        bool subtitles = true;
        int? randomSeed = null;
        (int Music, int Sfx) volumes = (AudioVolume.Default, AudioVolume.Default);
        int? traceActor = null;
        string objects = "initial";
        bool mask = false;
        int? zoom = null;
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
                case "--for" when i + 1 < args.Length && double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out double seconds) && seconds > 0:
                    playFor = TimeSpan.FromSeconds(seconds);
                    i++;
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
                    soundGiven = true;
                    i++;
                    break;
                case "--wav" when i + 1 < args.Length:
                    wav = args[++i];
                    break;
                case "--video" when i + 1 < args.Length:
                    video = args[++i];
                    break;
                case "--ffmpeg" when i + 1 < args.Length:
                    ffmpeg = args[++i];
                    break;
                case "--trace-actor" when i + 1 < args.Length && int.TryParse(args[i + 1], out int traced):
                    traceActor = traced;
                    i++;
                    break;
                case "--copy-protection":
                    copyProtection = true;
                    break;
                case "--no-smooth-scroll":
                    smoothScroll = false;
                    break;
                case "--semi-smooth-scroll":
                    semiSmoothScroll = true;
                    break;
                case "--mt32-roms" when i + 1 < args.Length:
                    mt32Roms = args[++i];
                    break;
                case "--mt32-model" when i + 1 < args.Length && args[i + 1].ToLowerInvariant() is "mt32" or "cm32l":
                    mt32Model = args[++i].ToLowerInvariant() == "mt32" ? Mt32Model.Mt32 : Mt32Model.Cm32l;
                    break;
                case "--no-subtitles":
                    subtitles = false;
                    break;
                case "--music-volume" when i + 1 < args.Length && int.TryParse(args[i + 1], out int music) && music is >= 0 and <= AudioVolume.Unity:
                    volumes = (music, volumes.Sfx);
                    i++;
                    break;
                case "--sfx-volume" when i + 1 < args.Length && int.TryParse(args[i + 1], out int sfx) && sfx is >= 0 and <= AudioVolume.Unity:
                    volumes = (volumes.Music, sfx);
                    i++;
                    break;
                case "--random-seed" when i + 1 < args.Length && long.TryParse(args[i + 1], out long seed) && seed is >= int.MinValue and <= uint.MaxValue:
                    randomSeed = unchecked((int)seed);
                    i++;
                    break;
                case "--text-speed" when i + 1 < args.Length && int.TryParse(args[i + 1], out int speed) && speed is >= 0 and <= 9:
                    textSpeed = speed;
                    i++;
                    break;
                case "--difficulty" when i + 1 < args.Length && Array.IndexOf(Difficulties, args[i + 1].ToLowerInvariant()) is >= 0 and var level:
                    bootParam = level;
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

        Loom game;
        try
        {
            game = Loom.Open(gamePath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or NotSupportedException or InvalidDataException)
        {
            Console.Error.WriteLine($"distaff: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"{game.Description}, SCUMM version {game.Version}");
        Console.WriteLine($"language {game.Language ?? "unknown"}, platform {game.Platform ?? "unknown"}, index MD5 {game.IndexMd5}");
        target ??= game.Target;

        // the MT-32 of the Roland Upgrade, from the ROMs in a folder, which on their own ask for it
        if (mt32Roms is not null && !soundGiven)
        {
            sound = SoundDevice.Mt32;
        }

        Func<int, IMidiDevice>? mt32 = null;
        if (sound == SoundDevice.Mt32)
        {
            if (mt32Roms is null || !Directory.Exists(mt32Roms) || Mt32RomSet.FromDirectory(mt32Roms, mt32Model) is not { } roms)
            {
                Console.Error.WriteLine("distaff: the MT-32 needs its ROMs: --mt32-roms names a folder holding a control and a PCM ROM of an MT-32 or CM-32L");
                return 2;
            }

            Console.WriteLine($"MT-32: {roms.Description}, {roms.Pcm.Info.Description}");
            mt32 = rate => new Mt32Emulator(roms, rate);
        }

        try
        {
            if (run is { } runSteps)
            {
                var store = new FileSaveStore(savesDirectory);
                return Run(game, runSteps, copyProtection, bootParam, clicks, keys, screenshot, zoom, traceActor, load, saves, store, target, sound, mt32, wav, video, ffmpeg, CdTrackFiles.In(gamePath), smoothScroll, semiSmoothScroll, textSpeed, subtitles, volumes, randomSeed ?? 1);
            }

            if (script is { } scriptNo)
            {
                return ListScript(game.Inspect(), scriptNo);
            }

            if (roomScripts is { } roomScriptsNo)
            {
                return ListRoomScripts(game.Inspect(), roomScriptsNo);
            }

            if (roomNumber is { } roomNo)
            {
                return WriteRoom(game.Inspect(), roomNo, objects, mask, zoom ?? 1, output ?? $"room-{roomNo:D2}{(mask ? "-mask" : "")}.png");
            }

            return Play(game, copyProtection, bootParam, scale, fullscreen, load is null ? null : File.ReadAllBytes(load), savesDirectory, target, sound, mt32, CdTrackFiles.In(gamePath), playFor, smoothScroll, semiSmoothScroll, textSpeed, subtitles, volumes, randomSeed);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidDataException or FfmpegException)
        {
            Console.Error.WriteLine($"distaff: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// The FM Towns' proficiency levels, as ScummVM's dialog sets the boot parameter for them
    /// (<c>LoomTownsDifficultyDialog</c>); the other releases ask in the game.
    /// </summary>
    private static readonly string[] Difficulties = ["practice", "standard", "expert"];

    private static int Usage()
    {
        Console.Error.WriteLine("usage: distaff <game folder> [--scale N] [--fullscreen] [--for seconds] [--copy-protection] [--no-smooth-scroll] [--semi-smooth-scroll] [--text-speed 0-9] [--no-subtitles] [--music-volume 0-256] [--sfx-volume 0-256] [--random-seed N] [--difficulty practice|standard|expert] [--sound speaker|pcjr|cms|adlib|mt32] [--mt32-roms folder] [--mt32-model mt32|cm32l] [--load file] [--saves folder] [--target name]");
        Console.Error.WriteLine("       distaff <game folder> --room N [--objects none|initial|all] [--mask] [--zoom N] [--output file.png]");
        Console.Error.WriteLine("       distaff <game folder> --script N | --room-scripts N");
        Console.Error.WriteLine("       distaff <game folder> --run [steps] [--difficulty practice|standard|expert] [--click step:x,y]... [--key step:K]... [--load file] [--save step:file]... [--sound speaker|pcjr|cms|adlib|mt32] [--mt32-roms folder] [--mt32-model mt32|cm32l] [--wav file.wav] [--video file.mp4] [--ffmpeg path] [--trace-actor N] [--copy-protection] [--no-smooth-scroll] [--semi-smooth-scroll] [--text-speed 0-9] [--no-subtitles] [--music-volume 0-256] [--sfx-volume 0-256] [--random-seed N] [--screenshot file.png] [--zoom N]");
        return 2;
    }

    /// <summary>
    /// Plays the game in a window: the mouse and keys go to the game, which draws its own cursor.
    /// Alt+Enter or F11 switches to full screen. The game's save screen (F5) keeps its saves in
    /// ScummVM's format in the saves folder, named after the target, ScummVM's name for the release unless given (<c>loom-vga.s01</c>); a save
    /// given starts the game as ScummVM's launcher does. The VGA CD's audio plays from its track
    /// file in the game's folder. The random numbers are seeded from the clock, as ScummVM's are,
    /// unless a seed is given (<c>--random-seed</c>).
    /// </summary>
    private static int Play(Loom game, bool copyProtection, int? bootParam, int scale, bool fullscreen, byte[]? load, string savesDirectory, string target, SoundDevice sound, Func<int, IMidiDevice>? mt32,
        Func<int, ICdTrack?> cdTracks, TimeSpan? playFor, bool smoothScroll, bool semiSmoothScroll, int textSpeed, bool subtitles, (int Music, int Sfx) volumes, int? randomSeed)
    {
        using var host = new SdlHost(new SdlHostOptions { Title = "Distaff", Scale = scale, Fullscreen = fullscreen, Saves = new FileSaveStore(savesDirectory) });
        using var engine = game.CreateEngine(host);
        engine.CopyProtection = copyProtection;
        engine.SmoothScrolling = smoothScroll;
        engine.SemiSmoothScrolling = semiSmoothScroll;
        engine.TextSpeed = textSpeed;
        engine.Subtitles = subtitles;
        engine.MusicVolume = volumes.Music;
        engine.SfxVolume = volumes.Sfx;
        engine.BootParam = bootParam;
        engine.StartingSave = load;
        engine.SaveTarget = target;
        engine.SoundDevice = sound;
        engine.Mt32 = mt32;
        engine.CdTracks = cdTracks;
        engine.RandomSeed = randomSeed;
        SDL.SDL3.SDL_HideCursor();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        host.Run(engine, duration: playFor);
        Console.WriteLine($"played {engine.World.Time.TotalSeconds:F1}s of game time in {clock.Elapsed.TotalSeconds:F1}s");
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
    /// stereo, the VGA CD's track mixed in. Its random numbers are seeded with 1, or the seed given,
    /// so a run is the same each time. With a video file, ffmpeg records it as a window shows and
    /// plays it, the screen 4 times its size unless zoomed otherwise.
    /// </summary>
    private static int Run(Loom game, int steps, bool copyProtection, int? bootParam, List<(int Step, int X, int Y)> clicks, List<(int Step, Key Key, char Ascii)> keys, string? screenshot, int? zoom, int? traceActor,
        string? load, List<(int Step, string Path)> saves, ISaveStore store, string target, SoundDevice sound, Func<int, IMidiDevice>? mt32, string? wav, string? video, string ffmpeg, Func<int, ICdTrack?> cdTracks, bool smoothScroll, bool semiSmoothScroll, int textSpeed, bool subtitles, (int Music, int Sfx) volumes, int randomSeed)
    {
        if (wav is not null && video is not null)
        {
            Console.Error.WriteLine("distaff: a run writes its sound to a WAV file or a video, not both");
            return 2;
        }

        var world = game.Play(randomSeed, sound, cdTracks: wav is null && video is null ? null : cdTracks, mt32: mt32);
        world.CopyProtection = copyProtection;
        world.SmoothScrolling = smoothScroll;
        world.SemiSmoothScrolling = semiSmoothScroll;
        world.TextSpeed = textSpeed;
        world.Subtitles = subtitles;
        world.SaveStore = store;
        world.SaveTarget = target;
        world.MusicVolume = volumes.Music;
        world.SfxVolume = volumes.Sfx;
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
            world.Audio.Read(buffer);
            audio.AddRange(buffer);

            framesRead = due;
        }

        if (load is not null)
        {
            var info = world.Load(File.ReadAllBytes(load));

            // the recording starts with the save's play time
            framesRead = (long)(world.Time.TotalSeconds * GameWorld.DefaultSampleRate);
            Console.WriteLine($"loaded \"{info.Name}\" (save version {info.Version}, {info.PlayTime.TotalSeconds:F0}s played): room {world.CurrentRoomNumber}");
        }
        else
        {
            world.Boot(bootParam ?? 0);
        }

        using var recorder = video is null ? null : new VideoRecorder(new FfmpegEncoder(video, zoom ?? 4, ffmpeg: ffmpeg));
        recorder?.Record(world);

        int room = -1;
        for (int step = 0; step < steps && !world.QuitRequested && !world.RestartRequested; step++)
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
                var a = world.GetActor(t);
                Console.WriteLine($"step {world.Steps,5}: actor {t} room {a.Room} at ({a.X},{a.Y}) box {a.WalkBox} moving {a.Moving} facing {a.Facing} frame {a.Frame} visible {a.Visible} camera {world.CameraX}");
            }

            if (world.CurrentRoomNumber != room)
            {
                room = world.CurrentRoomNumber;
                Console.WriteLine($"step {world.Steps,5} ({world.Time.TotalSeconds:F1}s): room {room}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"after {world.Steps} steps ({world.Time.TotalSeconds:F1}s): room {world.CurrentRoomNumber}, camera at {world.CameraX}");
        Console.WriteLine($"messages: {string.Join(" | ", world.MessageLog.Append(world.MessageText).Where(m => m.Length > 0))}");
        Console.WriteLine($"sounds started: {string.Join(' ', world.SoundsStarted)}");
        Console.WriteLine($"verbs on: {string.Join(' ', world.ShownVerbs().Select(v => $"{v.Number}@({v.Left},{v.Top})-({v.Right},{v.Bottom})"))}");
        Console.WriteLine($"running scripts: {string.Join(' ', world.RunningScripts().Select(s => $"{s.Number}({s.Kind}{(s.Waiting ? $" wait {s.Delay}" : "")})"))}");
        foreach (var group in world.Unhandled.GroupBy(u => u[(u.IndexOf(']') + 2)..].Split('(')[0]))
        {
            Console.WriteLine($"unhandled {group.Key}: {group.Count()}, first {group.First()}");
        }

        if (wav is not null)
        {
            File.WriteAllBytes(wav, WavWriter.Encode(audio.ToArray(), GameWorld.DefaultSampleRate, 2));
            Console.WriteLine($"wrote {wav}: {audio.Count / 2.0 / GameWorld.DefaultSampleRate:F1} seconds, peak {audio.DefaultIfEmpty().Max(s => Math.Abs((int)s))}");
        }

        if (recorder is not null)
        {
            recorder.Finish();
            Console.WriteLine($"wrote {video}: {recorder.Length.TotalSeconds:F1} seconds, {recorder.FramesWritten} frames");
        }

        if (screenshot is not null)
        {
            File.WriteAllBytes(screenshot, PngWriter.Encode(world.Frame(cursor: false), zoom ?? 1, zoom ?? 1));
            Console.WriteLine($"wrote {screenshot}");
        }

        return 0;
    }

    /// <summary>Disassembles a global script, from its start along every path execution can take.</summary>
    private static int ListScript(GameInspector inspector, int number)
    {
        if (inspector.Script(number) is not { } script)
        {
            Console.Error.WriteLine($"distaff: there is no script {number}");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"{script.Title}: {script.Size} bytes, in room {script.Room}");
        Print(script);
        return 0;
    }

    /// <summary>Disassembles a room's entry, exit and local scripts and its objects' verb scripts.</summary>
    private static int ListRoomScripts(GameInspector inspector, int number)
    {
        if (inspector.RoomScripts(number) is not { } scripts)
        {
            Console.Error.WriteLine($"distaff: there is no room {number}");
            return 1;
        }

        foreach (var script in scripts)
        {
            Console.WriteLine();
            Console.WriteLine(script.Verbs.Count > 0 ? $"{script.Title}, verbs {string.Join(", ", script.Verbs.Select(v => $"{VerbName(v)} at {v.Offset:X4}"))}:" : $"{script.Title}:");
            Print(script);
        }

        return 0;
    }

    private static string VerbName(ObjectVerb verb) => verb.IsFallback ? "*" : verb.Verb.ToString();

    private static void Print(ScriptListing script)
    {
        foreach (var line in script.Lines)
        {
            Console.WriteLine($"  {line}");
        }

        if (script.Unreached > 0)
        {
            Console.WriteLine($"  ({script.Unreached} of {script.Size} bytes not reached)");
        }
    }

    /// <summary>Writes a room as a PNG, with its objects as the game starts, none, or all, or its mask, and lists it.</summary>
    private static int WriteRoom(GameInspector inspector, int number, string objects, bool mask, int zoom, string output)
    {
        var shown = objects switch { "none" => RoomObjects.None, "all" => RoomObjects.All, _ => RoomObjects.Initial };
        if (inspector.Room(number, shown) is not { } room)
        {
            Console.Error.WriteLine($"distaff: there is no room {number}");
            return 1;
        }

        // the mask shows white where it hides actors
        File.WriteAllBytes(output, PngWriter.Encode(mask ? room.Mask : room.Picture, zoom, zoom));
        Console.WriteLine($"wrote {output}");

        Console.WriteLine();
        Console.WriteLine($"Room {room.Number}: {room.Width}x{room.Height}, {room.Size} bytes");
        Console.WriteLine($"  sounds: {string.Join(' ', room.Sounds)}");
        Console.WriteLine($"  scripts: {string.Join(' ', room.Scripts)}");
        Console.WriteLine($"  local scripts: {string.Join(' ', room.LocalScripts.Select(s => $"{s.Key}@{s.Value}"))}");
        Console.WriteLine($"  exit script at {room.ExitScript}, entry script at {room.EntryScript}");
        Console.WriteLine($"  walkboxes: {room.WalkBoxes}");
        Console.WriteLine($"  objects: {room.Objects.Count}");
        foreach (var o in room.Objects)
        {
            string name = o.Name.Length > 0 ? $"\"{o.Name}\"" : "(no name)";
            Console.WriteLine($"    {o.Number,4} {name,-20} state {o.State}, at ({o.X},{o.Y}) {o.Width}x{o.Height}, walk to ({o.WalkX},{o.WalkY}), parent {o.Parent}, verbs {string.Join(' ', o.Verbs.Select(VerbName))}");
        }

        return 0;
    }
}
