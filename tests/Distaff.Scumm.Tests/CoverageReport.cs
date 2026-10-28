using System.Reflection;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>
/// What the EGA walkthrough and its detours leave unplayed, written to the file the
/// DISTAFF_COVERAGE environment variable names (and nothing done without it): the rooms never
/// entered, the global scripts never started, and for each room the local scripts never started
/// and each object's verb entries never run. Draft verbs are among them, and 86 is an object's
/// answer to any draft it has no entry for. It is how the detours were found. Run it on its own
/// (<c>--filter CoverageReport</c>): the walkthrough's sections are played once per run, and a
/// section another test played first is not seen.
/// </summary>
public class CoverageReport
{
    [Fact]
    public void Write_what_the_walkthrough_leaves_unplayed()
    {
        var output = Environment.GetEnvironmentVariable("DISTAFF_COVERAGE");
        if (string.IsNullOrEmpty(output) || GameFolders.Find(GameFolders.Ega) is not { } path)
        {
            return;
        }

        var rooms = new HashSet<int>();
        var scripts = new HashSet<int>();
        var objects = new HashSet<(int Obj, int Verb)>();
        Walkthrough.Watch = world => world.Engine.ScriptStarted = (number, verb) =>
        {
            rooms.Add(world.CurrentRoomNumber);
            if (verb != 0)
            {
                objects.Add((number, verb));
            }
            else
            {
                scripts.Add(number);
            }
        };

        try
        {
            var ending = typeof(WalkthroughTests).GetField("Ending", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            _ = ending.GetType().GetProperty("Value")!.GetValue(ending);
            var tests = new WalkthroughTests();
            tests.The_opening_plays_to_the_end();
            tests.The_distaff_answers_a_draft_gone_wrong();
            tests.The_waterspout_takes_bobbin_out_to_sea();
            tests.The_graves_and_the_chalice();
            tests.Dyeing_on_wool_that_is_already_dyed();
            tests.The_caves_other_passages();
            tests.A_draft_on_the_dead_one();
            tests.The_log_takes_bobbin_back_to_the_dock();
        }
        finally
        {
            Walkthrough.Watch = null;
        }

        var loader = ResourceLoader.Open(new DirectoryGameContainer(path));
        var globals = Enumerable.Range(1, loader.Index.Count(ResourceType.Script) - 1)
            .Where(s => loader.Index.Locate(ResourceType.Script, s).IsPresent && !scripts.Contains(s));
        var lines = new List<string> { string.Empty, "global scripts never run: " + string.Join(' ', globals) };
        var unvisited = new List<int>();
        for (int r = 1; r < loader.Index.Count(ResourceType.Room); r++)
        {
            if (!loader.Index.Locate(ResourceType.Room, r).IsPresent || loader.LoadRoom(r) is not { } room)
            {
                continue;
            }

            if (!rooms.Contains(r))
            {
                unvisited.Add(r);
            }

            var locals = room.LocalScriptOffsets.Keys.Where(k => !scripts.Contains(k)).Order();
            var verbs = new List<string>();
            foreach (var obj in room.Objects)
            {
                var ran = objects.Where(o => o.Obj == obj.Number).Select(o => obj.FindVerb((byte)o.Verb)).ToHashSet();
                var missing = obj.Verbs.Where(e => !ran.Contains(e.Offset)).Select(e => e.Verb == 0xFF ? "any" : e.Verb.ToString()).ToList();
                if (missing.Count > 0)
                {
                    verbs.Add($"{obj.Number}[{string.Join(',', missing)}]");
                }
            }

            lines.Add($"room {r}: local scripts never run: {string.Join(' ', locals)}; object verbs never run: {string.Join(' ', verbs)}");
        }

        lines[0] = "rooms never entered: " + string.Join(' ', unvisited);
        File.WriteAllLines(output, lines);
    }
}
