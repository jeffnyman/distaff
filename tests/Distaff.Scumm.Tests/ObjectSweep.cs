using System.Collections.Concurrent;
using Distaff.Scumm.Game;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>
/// Every object's answers, tried where the walkthrough finds it: the first time the walkthrough
/// has control in a room, the room is saved; then for each object there that can be clicked, a
/// world loaded from that save looks at it (a double click), and for each draft Bobbin knows by
/// then and can play there, another spins it at the object, forwards and then backwards. Each try starts from the
/// save, so what one try changes does not reach the next. What the tries run is told to
/// <see cref="Walkthrough.Watch"/>'s watchers, as the walkthrough's own play is; what goes wrong
/// (a command that throws or waits too long, an instruction the engine does not handle) is
/// returned. Part of <see cref="CoverageReport"/>, which runs it with the game data present.
/// </summary>
internal static class ObjectSweep
{
    /// <summary>The longest a try waits for the game to answer before it is set down as stuck.</summary>
    private const int TryPatience = 3000;

    /// <summary>
    /// Saves, as the walkthrough plays, the first time it has control in each room, and again each
    /// time it comes back knowing drafts it did not before.
    /// </summary>
    public sealed class Snapshots
    {
        private readonly ConcurrentDictionary<(int Room, int Drafts), byte[]> _saves = new();

        public IReadOnlyDictionary<(int Room, int Drafts), byte[]> Saves => _saves;

        public void Take(Walkthrough w)
        {
            int room = w.World.CurrentRoomNumber;
            int drafts = Learned(w);
            if (room != 0 && !_saves.Keys.Any(k => k.Room == room && (drafts & ~k.Drafts) == 0))
            {
                _saves.TryAdd((room, drafts), w.World.Save($"sweep-{room}", new DateTime(2026, 10, 28)));
            }
        }
    }

    /// <summary>The drafts Bobbin knows, a bit each.</summary>
    private static int Learned(Walkthrough w) =>
        Enumerable.Range(0, 16).Where(d => (w.World.Engine.Variables[50 + w.Shift + 2 * d] & 0x2000) != 0).Sum(d => 1 << d);

    /// <summary>A try that went wrong: where, on what, doing what, and how.</summary>
    public sealed record Finding(int Room, int Object, string Action, string What);

    /// <summary>Tries every object of every room saved, the saves in parallel; returns what went wrong, once for each room, object and action.</summary>
    public static List<Finding> Run(string path, IReadOnlyDictionary<(int Room, int Drafts), byte[]> saves, Action<GameWorld>? watch)
    {
        var findings = new ConcurrentBag<Finding>();
        Parallel.ForEach(saves, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, entry =>
        {
            Walkthrough.Watch = watch;
            try
            {
                var loader = ResourceLoader.Open(new DirectoryGameContainer(path));
                SweepRoom(loader, entry.Key.Room, entry.Value, findings);
            }
            finally
            {
                Walkthrough.Watch = null;
            }
        });

        return [.. findings.Distinct().OrderBy(f => f.Room).ThenBy(f => f.Object).ThenBy(f => f.Action, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Runs object verb entries the sweep could not reach by playing, each from the save of its
    /// room with the most drafts known: the object's script for the verb is started as the
    /// sentence script would start it (<c>startObject</c>), whether or not the object could be
    /// clicked then, and the game played on until it gives back control. What it finds is in a
    /// state the game may never be in, so it is a lead to look into, not a fault in itself.
    /// </summary>
    public static List<Finding> Force(string path, IReadOnlyDictionary<(int Room, int Drafts), byte[]> saves, IEnumerable<(int Room, int Obj, int Verb)> entries, Action<GameWorld>? watch, ConcurrentBag<string>? answers = null)
    {
        var findings = new ConcurrentBag<Finding>();
        var tries = entries.Where(e => saves.Keys.Any(k => k.Room == e.Room)).ToList();
        Parallel.ForEach(tries, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, entry =>
        {
            Walkthrough.Watch = watch;
            try
            {
                var loader = ResourceLoader.Open(new DirectoryGameContainer(path));
                var save = saves.Where(s => s.Key.Room == entry.Room).MaxBy(s => int.PopCount(s.Key.Drafts)).Value;
                Try(loader, save, entry.Room, entry.Obj, $"forced verb {entry.Verb}", findings, w =>
                {
                    int said = w.World.MessageLog.Count;
                    w.World.Engine.RunObjectScript(entry.Obj, entry.Verb);
                    w.Ready(TryPatience);
                    answers?.Add($"room {entry.Room} object {entry.Obj} verb {entry.Verb}: {string.Join(" | ", w.World.MessageLog.Skip(said))}");
                });
            }
            finally
            {
                Walkthrough.Watch = null;
            }
        });

        return [.. findings.Distinct().OrderBy(f => f.Room).ThenBy(f => f.Object).ThenBy(f => f.Action, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The interface's own objects, played: on the proficiency screen (room 69) STANDARD and
    /// EXPERT, each played on to Bobbin on the cliff; with the copy protection kept, each of the
    /// twelve answers to its question; and on the save screen (room 70), opened with F5 from the
    /// first save, each of its controls after SAVE or LOAD and a slot. Each from the start, or the
    /// save, again.
    /// </summary>
    public static List<Finding> Interface(string path, byte[] save, Action<GameWorld>? watch, ConcurrentBag<string> answers)
    {
        var findings = new ConcurrentBag<Finding>();
        var tries = new List<(int Room, int Obj, string Action, Func<ResourceLoader, Walkthrough> Start, Func<Walkthrough, int[]> Clicks)>();
        foreach (int level in new[] { 877, 878 })
        {
            tries.Add((69, level, "pick", l => Boot(l, false), _ => [level]));
        }

        // the copy protection: each of its twelve threads once; the secret weave asked for, four
        // threads (variables 82 to 85, as its script checks them); and a wrong one three times
        Func<ResourceLoader, Walkthrough> asked = l => Press(Boot(l, true), 876, 400);
        static int[] Weave(Walkthrough w) => [.. Enumerable.Range(82, 4).Select(v => 886 + w.World.Engine.Variables[v])];
        for (int answer = 886; answer <= 897; answer++)
        {
            int a = answer;
            tries.Add((69, a, "answer", asked, _ => [a]));
        }

        tries.Add((69, 886, "the secret weave", asked, Weave));
        tries.Add((69, 886, "a wrong weave three times", asked, w =>
        {
            int[] wrong = [.. Weave(w).Select(o => o == 897 ? 886 : o + 1)];
            return [.. wrong, .. wrong, .. wrong];
        }));

        foreach (int[] clicks in new[] { new[] { 899 }, [902], [900, 899, 902], [900, 910], [900, 911], [901, 899, 902], [901, 910], [901, 911] })
        {
            tries.Add((70, clicks[^1], string.Join(" then ", clicks), l =>
            {
                var w = Load(l, save);
                w.World.SaveStore = new MemorySaveStore();
                w.World.KeyDown(Input.Key.F5, Input.KeyModifiers.None, (char)0);
                return w.InRoom(70, TryPatience).Wait(50);
            }, _ => clicks));
        }

        Parallel.ForEach(tries, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, t =>
        {
            Walkthrough.Watch = watch;
            try
            {
                var loader = ResourceLoader.Open(new DirectoryGameContainer(path));
                Walkthrough? w = null;
                Try(() => w = t.Start(loader), t.Room, t.Obj, t.Action, findings, w2 =>
                {
                    int said = w2.World.MessageLog.Count;
                    foreach (int obj in t.Clicks(w2))
                    {
                        Press(w2, obj, 60);
                    }

                    w2.Wait(t.Room == 69 && t.Action != "answer" ? 1500 : 600);
                    answers.Add($"room {t.Room} {t.Action} {t.Obj}: {string.Join(" | ", w2.World.MessageLog.Skip(said))}{(w2.World.QuitRequested ? " (quits)" : "")}, in room {w2.World.CurrentRoomNumber}");
                });
            }
            finally
            {
                Walkthrough.Watch = null;
            }
        });

        return [.. findings.Distinct().OrderBy(f => f.Room).ThenBy(f => f.Object).ThenBy(f => f.Action, StringComparer.Ordinal)];
    }

    /// <summary>Clicks an object where it shows and plays on for some steps, the game having control or not.</summary>
    private static Walkthrough Press(Walkthrough w, int obj, int steps)
    {
        var (x, y) = w.OnScreen(obj);
        w.World.Click(x, y);
        return w.Wait(steps);
    }

    private static Walkthrough Boot(ResourceLoader loader, bool copyProtection)
    {
        var w = new Walkthrough(new GameWorld(loader) { CopyProtection = copyProtection });
        w.World.Boot();
        return w.Wait(100);
    }

    private static void SweepRoom(ResourceLoader loader, int room, byte[] save, ConcurrentBag<Finding> findings)
    {
        var first = Load(loader, save);
        var world = first.World;
        var targets = world.CurrentRoom!.Objects
            .Where(o => o.Number > 0 && o.Verbs.Count > 0 && o.Width > 0 && o.Height > 0
                && !world.GetClass(o.Number, 32) && RoomRenderer.ParentsAllow(world.CurrentRoom.Objects, o, world.GetState))
            .Select(o => o.Number)
            .ToList();
        // the drafts Bobbin knows and can play here: every note shows on the distaff (none do once it is lost)
        var drafts = Enumerable.Range(0, 16)
            .Where(d => (Learned(first) & (1 << d)) != 0
                && first.Notes(d).All(n => world.Verbs.Any(v => v.VerbId == n && v.Mode == 1 && v.SaveId == 0)))
            .ToList();

        foreach (int obj in targets)
        {
            Try(loader, save, room, obj, "click", findings, w =>
            {
                var (x, y) = w.OnScreen(obj);
                w.ClickAt(x, y).Ready(TryPatience);
            });
            Try(loader, save, room, obj, "look", findings, w => w.Use(obj, wait: false).Ready(TryPatience));
            foreach (int draft in drafts)
            {
                foreach (bool backwards in new[] { false, true })
                {
                    Try(loader, save, room, obj, $"draft {draft}{(backwards ? " backwards" : "")}", findings,
                        w =>
                        {
                            // an exit takes Bobbin out of the room, and there is no draft to spin at it
                            if (w.Click(obj).World.CurrentRoomNumber == room)
                            {
                                w.Spin(draft, backwards, wait: false).Ready(TryPatience);
                            }
                        });
                }
            }
        }
    }

    private static void Try(ResourceLoader loader, byte[] save, int room, int obj, string action, ConcurrentBag<Finding> findings, Action<Walkthrough> act)
        => Try(() => Load(loader, save), room, obj, action, findings, act);

    private static void Try(Func<Walkthrough> start, int room, int obj, string action, ConcurrentBag<Finding> findings, Action<Walkthrough> act)
    {
        Walkthrough w;
        try
        {
            w = start();
        }
        catch (Exception ex)
        {
            findings.Add(new Finding(room, obj, action, $"getting there: {ex.GetType().Name}: {ex.Message}"));
            return;
        }

        int unhandled = w.World.Unhandled.Count;
        try
        {
            act(w);
        }
        catch (Exception ex)
        {
            findings.Add(new Finding(room, obj, action, $"{ex.GetType().Name}: {ex.Message} @@ {string.Join(" / ", (ex.StackTrace ?? "").Split((char)10).Take(6).Select(l => l.Trim()))}"));
        }

        foreach (string u in w.World.Unhandled.Skip(unhandled).Distinct())
        {
            findings.Add(new Finding(room, obj, action, "unhandled " + u));
        }
    }

    private static Walkthrough Load(ResourceLoader loader, byte[] save)
    {
        var w = new Walkthrough(new GameWorld(loader));
        w.World.Load(save);
        w.Ready(TryPatience);
        return w;
    }
}
