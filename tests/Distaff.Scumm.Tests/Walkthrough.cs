using Distaff.Scumm.Game;
using Distaff.Scumm.Input;
using Distaff.Scumm.Scripts;

namespace Distaff.Scumm.Tests;

/// <summary>
/// Plays Loom as a player would, a command at a time: waits for the game to hand control back,
/// clicks objects by number (scrolling to them first), double-clicks them to examine or use them,
/// and spins drafts on the distaff. A draft's notes are read from the game's own table (variables
/// 50 to 81, two for each of the sixteen drafts: four 3-bit notes, 0 for c, and flags; then the
/// verbs it sends), so the walkthrough does not depend on the notes the game chose.
/// </summary>
internal sealed class Walkthrough(GameWorld world)
{
    /// <summary>The longest a command waits before the walkthrough is taken to be stuck.</summary>
    public const int Patience = 6000;

    public GameWorld World { get; } = Watched(world);

    /// <summary>Told of each world a walkthrough plays, on the thread that plays it: for tests that watch what the game runs.</summary>
    [ThreadStatic]
    public static Action<GameWorld>? Watch;

    /// <summary>Told each time the walkthrough has control again (<see cref="Ready"/>), on the thread that plays it: for tests that save where it goes.</summary>
    [ThreadStatic]
    public static Action<Walkthrough>? WatchReady;

    private static GameWorld Watched(GameWorld world)
    {
        Watch?.Invoke(world);
        return world;
    }

    /// <summary>The proficiency level played: 0 Practice, 1 Standard, 2 Expert, as the FM Towns' boot parameter numbers them.</summary>
    public int Level { get; init; }

    /// <summary>Where the proficiency screen's button for <see cref="Level"/> is, on the EGA's and the VGA CD's screen (room 69).</summary>
    public (int X, int Y) LevelButton => (160, Level switch { 1 => 71, 2 => 119, _ => 92 });

    /// <summary>How much higher the game's own variables are numbered: 50 on the VGA CD, whose engine keeps more of its own below them.</summary>
    public int Shift => World.Version == 4 ? 50 : 0;

    /// <summary>The object Bobbin points at, for the distaff (variable 103 on the EGA).</summary>
    public int Pointed => 103 + Shift;

    /// <summary>Whether the player can act and Bobbin waits: no screen effect showing, control on, the cursor shown, no cutscene, no message, no sentence running, the ego standing and the camera still.</summary>
    public bool InControl
        => !World.InEffect && World.UserPut > 0 && World.CursorState > 0 && World.Engine.CutsceneLevel == 0 && World.HaveMessage == 0
           && !World.Engine.IsScriptRunning(World.Engine.Variables[Vars.SentenceScript])
           && World.Actors[World.Engine.Variables[Vars.Ego]].Moving == 0 && World.Camera.X == World.Camera.LastX;

    /// <summary>
    /// Writes a section's save to the folder the DISTAFF_EXPORT_SAVES environment variable names,
    /// when it names one, as <c>release-NN-Section.sNN</c>, with the level's name after the release
    /// above Practice: for loading in ScummVM, or in the player.
    /// </summary>
    public static byte[] Export(string release, Action<Walkthrough> section, byte[] save, int level = 0)
    {
        release += level switch { 1 => "-standard", 2 => "-expert", _ => "" };
        var folder = Environment.GetEnvironmentVariable("DISTAFF_EXPORT_SAVES");
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
            lock (Exported)
            {
                int n = Exported.TryGetValue(release, out int last) ? last + 1 : 1;
                Exported[release] = n;
                File.WriteAllBytes(Path.Combine(folder, $"{release}-{n:D2}-{section.Method.Name}.s{n:D2}"), save);
            }
        }

        return save;
    }

    private static readonly Dictionary<string, int> Exported = [];

    /// <summary>Steps until the player has had control for a few steps running.</summary>
    public Walkthrough Ready(int max = Patience)
    {
        int quiet = 0;
        Until(() => (quiet = InControl ? quiet + 1 : 0) >= 8, max, "control");
        WatchReady?.Invoke(this);
        return this;
    }

    public Walkthrough Wait(int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            World.Step();
        }

        return this;
    }

    public Walkthrough Until(Func<bool> done, int max = Patience, string what = "the condition")
    {
        for (int i = 0; i < max; i++)
        {
            if (done())
            {
                return this;
            }

            World.Step();
        }

        throw new InvalidOperationException($"Waited {max} steps for {what} in room {World.CurrentRoomNumber}, step {World.Steps}; last message \"{World.MessageLog.LastOrDefault()}\".");
    }

    public Walkthrough InRoom(int room, int max = Patience) => Until(() => World.CurrentRoomNumber == room, max, $"room {room}");

    /// <summary>A click on the screen, then a step for the game to take it.</summary>
    public Walkthrough ClickAt(int x, int y)
    {
        World.Click(x, y);
        World.Step();
        return this;
    }

    /// <summary>Clicks an object of the room: Bobbin walks to it and points at it.</summary>
    public Walkthrough Click(int obj)
    {
        var (x, y) = OnScreen(obj);
        return ClickAt(x, y).Ready();
    }

    /// <summary>
    /// Leaves by an exit of the room for another room. A walk that crosses into a box where the
    /// room takes Bobbin over in a cutscene ends there, so the exit is clicked again.
    /// </summary>
    public Walkthrough Exit(int obj, int room, int max = Patience)
    {
        int from = World.CurrentRoomNumber;
        for (int tries = 0; tries < 4 && World.CurrentRoomNumber == from; tries++)
        {
            var (x, y) = OnScreen(obj);
            ClickAt(x, y).Ready(max);
        }

        return InRoom(room, max).Ready(max);
    }

    /// <summary>
    /// Points at an object of the room while the game talks on, as a player may: clicks it and
    /// steps until Bobbin stands pointing at it, without waiting for the talk to end.
    /// </summary>
    public Walkthrough PointAt(int obj)
    {
        var (x, y) = OnScreen(obj);
        ClickAt(x, y);
        var ego = World.Actors[World.Engine.Variables[Vars.Ego]];
        return Until(() => World.Engine.Variables[Pointed] == obj && ego.Moving == 0, Patience, $"Bobbin to point at {obj}");
    }

    /// <summary>Double-clicks an object of the room: Bobbin looks at it, picks it up, or goes through it. Without waiting, the game is left to answer.</summary>
    public Walkthrough Use(int obj, bool wait = true)
    {
        var (x, y) = OnScreen(obj);
        World.Click(x, y);
        World.Step();
        World.Step();
        World.Click(x, y);
        World.Step();
        return wait ? Ready() : this;
    }

    /// <summary>Clicks a point of the room, scrolling to it first: Bobbin walks there.</summary>
    public Walkthrough WalkTo(int roomX, int roomY)
    {
        ScrollTo(roomX);
        return ClickAt(roomX - World.Camera.Left, roomY + World.ViewTop).Ready();
    }

    /// <summary>Spins a draft of the game's table on what Bobbin points at, backwards for its opposite. Without waiting, the game is left to answer.</summary>
    public Walkthrough Spin(int draft, bool backwards = false, bool wait = true)
    {
        int[] notes = Notes(draft);
        if (backwards)
        {
            Array.Reverse(notes);
        }

        return PlayNotes(notes, wait);
    }

    /// <summary>
    /// Plays notes on the distaff (1 for c to 8 for the high C), clicking each where it shows, and
    /// unless told not to waits for the game to take them as a draft and the object to answer.
    /// </summary>
    public Walkthrough PlayNotes(int[] notes, bool wait = true)
    {
        foreach (int note in notes)
        {
            var verb = World.Verbs.First(v => v.VerbId == note && v.Mode == 1 && v.SaveId == 0);
            World.Click(verb.CurRect.Left + verb.CurRect.Width / 2, verb.CurRect.Top + 4);
            World.Step();
            Wait(25);
        }

        if (!wait)
        {
            return this;
        }

        // the notes are played back and the draft looked up and sent (scripts 20 to 27), then the object answers
        int[] spinning = [20, 21, 22, 26, 27];
        Until(() => World.Engine.Variables[98 + Shift] == 0 && !spinning.Any(World.Engine.IsScriptRunning), Patience, "the draft");
        return Wait(10).Ready();
    }

    /// <summary>A draft's notes, 1 for c to 8 for the high C, as the game keeps them.</summary>
    public int[] Notes(int draft)
    {
        int packed = World.Engine.Variables[50 + Shift + 2 * draft];
        return [.. Enumerable.Range(0, 4).Select(k => ((packed >> (3 * k)) & 7) + 1)];
    }

    public Walkthrough Key(Key key, char ascii)
    {
        World.KeyDown(key, KeyModifiers.None, ascii);
        World.Step();
        return this;
    }

    /// <summary>Where on the screen an object of the room is, the camera brought to it first.</summary>
    public (int X, int Y) OnScreen(int obj)
    {
        var o = World.CurrentRoom?.FindObject(obj) ?? throw new InvalidOperationException($"Object {obj} is not in room {World.CurrentRoomNumber}.");
        var (x, y) = World.PositionOf(o);
        int cx = x + o.Width / 2, cy = y + o.Height / 2;
        ScrollTo(cx);
        return (Math.Clamp(cx - World.Camera.Left, 0, 319), cy + World.ViewTop);
    }

    /// <summary>Walks toward a point of the room until the camera shows it.</summary>
    private void ScrollTo(int roomX)
    {
        for (int tries = 0; tries < 20 && (roomX < World.Camera.Left || roomX >= World.Camera.Left + 320); tries++)
        {
            int edge = roomX < World.Camera.Left ? 2 : 317;
            var ego = World.Actors[World.Engine.Variables[Vars.Ego]];
            World.Click(edge, Math.Clamp(ego.Y + World.ViewTop, World.ViewTop, World.ViewTop + World.ViewHeight - 1));
            World.Step();
            int left = World.Camera.Left;
            Ready();
            if (World.Camera.Left == left && ego.Moving == 0)
            {
                break;
            }
        }
    }
}
