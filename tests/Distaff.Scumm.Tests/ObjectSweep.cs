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
    {
        var w = Load(loader, save);
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
