using Distaff.Scumm.Graphics;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Rooms;
using Distaff.Scumm.Scripts;

namespace Distaff.Scumm.Inspection;

/// <summary>Which of a room's objects a <see cref="GameInspector.Room"/> picture draws.</summary>
public enum RoomObjects
{
    /// <summary>The room's background alone.</summary>
    None,

    /// <summary>The objects shown as the game starts, in the states the index gives them.</summary>
    Initial,

    /// <summary>Every object with a picture, each in its first state.</summary>
    All,
}

/// <summary>A verb an object answers, and where in the object's code its script starts.</summary>
/// <param name="Verb">The verb's number, or <see cref="Fallback"/> for the script that answers any other.</param>
/// <param name="Offset">Where the verb's script starts in the object's code.</param>
public readonly record struct ObjectVerb(int Verb, int Offset)
{
    /// <summary>The number of the entry that answers the verbs without one of their own.</summary>
    public const int Fallback = RoomObject.FallbackVerb;

    /// <summary>Whether this is the entry that answers the verbs without one of their own.</summary>
    public bool IsFallback => Verb == Fallback;
}

/// <summary>An object of a room, as the room's data has it.</summary>
public sealed record ObjectInfo
{
    /// <summary>The object's number.</summary>
    public int Number { get; init; }

    /// <summary>Its name, empty when it has none.</summary>
    public string Name { get; init; } = "";

    /// <summary>The state the game starts it in.</summary>
    public int State { get; init; }

    /// <summary>The left of its picture in the room, in pixels.</summary>
    public int X { get; init; }

    /// <summary>The top of its picture in the room, in pixels.</summary>
    public int Y { get; init; }

    /// <summary>Its width, in pixels.</summary>
    public int Width { get; init; }

    /// <summary>Its height, in pixels.</summary>
    public int Height { get; init; }

    /// <summary>Where an actor walks to, to reach it.</summary>
    public int WalkX { get; init; }

    /// <summary>Where an actor walks to, to reach it.</summary>
    public int WalkY { get; init; }

    /// <summary>The object it is drawn only with, by its index in the room plus one, or 0 for none.</summary>
    public int Parent { get; init; }

    /// <summary>The verbs it answers.</summary>
    public IReadOnlyList<ObjectVerb> Verbs { get; init; } = [];
}

/// <summary>A room's picture, and what the room holds.</summary>
public sealed record RoomPicture
{
    /// <summary>The room's number.</summary>
    public int Number { get; init; }

    /// <summary>The room's width, in pixels.</summary>
    public int Width { get; init; }

    /// <summary>The room's height, in pixels.</summary>
    public int Height { get; init; }

    /// <summary>The room's resource, in bytes.</summary>
    public int Size { get; init; }

    /// <summary>The room drawn in its own colours, with the objects asked for.</summary>
    public required RgbFrame Picture { get; init; }

    /// <summary>The room's mask: white where it hides the actors that stand behind it, black elsewhere.</summary>
    public required RgbFrame Mask { get; init; }

    /// <summary>The sounds the room holds.</summary>
    public IReadOnlyList<int> Sounds { get; init; } = [];

    /// <summary>The global scripts the room holds.</summary>
    public IReadOnlyList<int> Scripts { get; init; } = [];

    /// <summary>The room's local scripts, by number, and where each starts in the room.</summary>
    public IReadOnlyDictionary<int, int> LocalScripts { get; init; } = new Dictionary<int, int>();

    /// <summary>Where the script run on entering the room starts in it, 0 for none.</summary>
    public int EntryScript { get; init; }

    /// <summary>Where the script run on leaving the room starts in it, 0 for none.</summary>
    public int ExitScript { get; init; }

    /// <summary>The walk boxes the actors walk in.</summary>
    public int WalkBoxes { get; init; }

    /// <summary>The room's objects.</summary>
    public IReadOnlyList<ObjectInfo> Objects { get; init; } = [];
}

/// <summary>An instruction of a script, as a listing shows it.</summary>
/// <param name="Offset">Where it starts in its script.</param>
/// <param name="Length">Its length, in bytes.</param>
/// <param name="Text">The instruction, as ScummVM's descumm writes it.</param>
public readonly record struct ScriptLine(int Offset, int Length, string Text)
{
    /// <summary>The line as a listing shows it: <c>[0012] putActor(...)</c>.</summary>
    public override string ToString() => $"[{Offset:X4}] {Text}";
}

/// <summary>A script disassembled, from its start along every path its execution can take.</summary>
public sealed record ScriptListing
{
    /// <summary>What the script is: <c>Script 12</c>, <c>Entry script</c>, <c>Object 345 "door"</c>.</summary>
    public string Title { get; init; } = "";

    /// <summary>The script's length, in bytes, when it is a script of its own.</summary>
    public int? Size { get; init; }

    /// <summary>The room a global script is kept in.</summary>
    public int? Room { get; init; }

    /// <summary>The verbs an object's script answers, and where each starts.</summary>
    public IReadOnlyList<ObjectVerb> Verbs { get; init; } = [];

    /// <summary>Its instructions, in order.</summary>
    public IReadOnlyList<ScriptLine> Lines { get; init; } = [];

    /// <summary>The bytes of a script of its own that no path reaches.</summary>
    public int Unreached { get; init; }
}

/// <summary>
/// A look inside a release, without playing it: its rooms' pictures and objects, and its scripts
/// disassembled. Opened by <see cref="Loom.Inspect"/>.
/// </summary>
public sealed class GameInspector
{
    private readonly ResourceLoader _loader;

    internal GameInspector(ResourceLoader loader) => _loader = loader;

    private List<int>? _rooms;
    private List<int>? _scripts;

    /// <summary>The release's SCUMM version: 3, or 4 for the VGA CD.</summary>
    public int Version => _loader.Version;

    /// <summary>The numbers of the release's rooms, in order: those <see cref="Room"/> draws.</summary>
    public IReadOnlyList<int> Rooms => _rooms ??= Numbers(ResourceType.Room);

    /// <summary>The numbers of the release's global scripts, in order: those <see cref="Script"/> lists.</summary>
    public IReadOnlyList<int> Scripts => _scripts ??= Numbers(ResourceType.Script);

    /// <summary>
    /// A room's picture, with no objects, the objects the game starts with, or all of them, and
    /// what the room holds; null when there is no such room.
    /// </summary>
    public RoomPicture? Room(int number, RoomObjects objects = RoomObjects.Initial)
    {
        if (_loader.LoadRoom(number) is not { } room)
        {
            return null;
        }

        var image = objects switch
        {
            RoomObjects.None => RoomRenderer.DrawBackground(room),
            RoomObjects.All => RoomRenderer.RenderAll(room),
            _ => RoomRenderer.Render(room, StateOf),
        };

        return new RoomPicture
        {
            Number = room.Number,
            Width = room.Width,
            Height = room.Height,
            Size = room.Data.Length,
            Picture = RgbFrame.FromIndexed(image.Pixels, RoomRenderer.PaletteOf(room)),
            Mask = MaskPicture(image.Mask),
            Sounds = room.Sounds,
            Scripts = room.Scripts,
            LocalScripts = room.LocalScriptOffsets,
            EntryScript = room.EntryScriptOffset,
            ExitScript = room.ExitScriptOffset,
            WalkBoxes = room.WalkBoxes.Count,
            Objects = [.. room.Objects.Select(o => new ObjectInfo
            {
                Number = o.Number,
                Name = o.Name,
                State = StateOf(o.Number),
                X = o.X,
                Y = o.Y,
                Width = o.Width,
                Height = o.Height,
                WalkX = o.WalkX,
                WalkY = o.WalkY,
                Parent = o.Parent,
                Verbs = Verbs(o),
            })],
        };
    }

    /// <summary>A global script disassembled; null when there is no such script.</summary>
    public ScriptListing? Script(int number)
    {
        if (_loader.LoadScript(number) is not { } code)
        {
            return null;
        }

        var lines = Decode(code, [0]);
        return new ScriptListing
        {
            Title = $"Script {number}",
            Size = code.Length,
            Room = _loader.Index.Locate(ResourceType.Script, number).Room,
            Lines = lines,
            Unreached = code.Length - lines.Sum(l => l.Length),
        };
    }

    /// <summary>
    /// A room's own scripts disassembled: its exit and entry scripts, its local scripts, and its
    /// objects' verb scripts; null when there is no such room.
    /// </summary>
    public IReadOnlyList<ScriptListing>? RoomScripts(int number)
    {
        if (_loader.LoadRoom(number) is not { } room)
        {
            return null;
        }

        var listings = new List<ScriptListing>();
        void Part(string title, int offset)
        {
            if (offset != 0)
            {
                listings.Add(new ScriptListing { Title = $"{title}, at {offset} in room {number}", Lines = Decode(room.Data, [offset]) });
            }
        }

        Part("Exit script", room.ExitScriptOffset);
        Part("Entry script", room.EntryScriptOffset);
        foreach (var (script, offset) in room.LocalScriptOffsets)
        {
            Part($"Local script {script}", offset);
        }

        foreach (var obj in room.Objects.Where(o => o.Verbs.Count > 0))
        {
            listings.Add(new ScriptListing
            {
                Title = $"Object {obj.Number}{(obj.Name.Length > 0 ? $" \"{obj.Name}\"" : "")}",
                Verbs = Verbs(obj),
                Lines = Decode(obj.Code, obj.Verbs.Select(v => v.Offset)),
            });
        }

        return listings;
    }

    /// <summary>The entries of the index that load: an entry's file may be missing, or its data not what the index says.</summary>
    private List<int> Numbers(ResourceType type)
    {
        var numbers = new List<int>();
        for (int n = 0; n < _loader.Index.Count(type); n++)
        {
            try
            {
                if (_loader.Index.Locate(type, n).IsPresent && _loader.Load(type, n) is not null)
                {
                    numbers.Add(n);
                }
            }
            catch (InvalidDataException)
            {
            }
        }

        return numbers;
    }

    private int StateOf(int obj)
    {
        var states = _loader.Index.ObjectStates;
        return obj < states.Count ? states[obj] : 0;
    }

    private static List<ObjectVerb> Verbs(RoomObject obj) => [.. obj.Verbs.Select(v => new ObjectVerb(v.Verb, v.Offset))];

    private List<ScriptLine> Decode(ReadOnlyMemory<byte> code, IEnumerable<int> entries)
        => [.. new ScriptDecoder(_loader.Version, _loader.FmTowns).DecodeReachable(code, entries).Values.Select(i => new ScriptLine(i.Offset, i.Length, i.Text))];

    private static RgbFrame MaskPicture(Surface8 mask)
    {
        var picture = new RgbFrame(mask.Width, mask.Height);
        for (int i = 0; i < mask.Pixels.Length; i++)
        {
            picture.Pixels[i] = mask.Pixels[i] != 0 ? Rgb.White : Rgb.Black;
        }

        return picture;
    }
}
