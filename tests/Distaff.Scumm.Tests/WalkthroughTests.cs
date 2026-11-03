using Distaff.Scumm.Game;
using Distaff.Scumm.Input;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>
/// The whole game, played a section at a time as a player would. Each section starts from the save
/// the section before it ended with, loaded into a new world, so every save on the way is played
/// on from. Tests against the game data pass without checking when it is absent.
/// </summary>
public class WalkthroughTests
{
    /// <summary>Where a section left the game: its world, still running, and the save made there.</summary>
    private sealed record Played(GameWorld World, byte[] Save, int FirstMessage);

    private static readonly Lazy<Played?> Elders = new(() => Play(null, TheElders));
    private static readonly Lazy<Played?> Island = new(() => Play(Elders.Value, TheIsland));
    private static readonly Lazy<Played?> Leaving = new(() => Play(Island.Value, LeavingTheIsland));
    private static readonly Lazy<Played?> Glassmakers = new(() => Play(Leaving.Value, Crystalgard));
    private static readonly Lazy<Played?> Shepherds = new(() => Play(Glassmakers.Value, TheDragon));
    private static readonly Lazy<Played?> Caves = new(() => Play(Shepherds.Value, TheCaves));
    private static readonly Lazy<Played?> Smiths = new(() => Play(Caves.Value, TheForge));
    private static readonly Lazy<Played?> Clerics = new(() => Play(Smiths.Value, TheBishop));
    private static readonly Lazy<Played?> Void = new(() => Play(Clerics.Value, TheVoid));
    private static readonly Lazy<Played?> Ending = new(() => Play(Void.Value, TheLoom));

    /// <summary>
    /// Practice level, the opening skipped. Bobbin follows the leaf to the sanctuary, where the
    /// Elders meet and turn to swans; he takes the distaff and weaves Opening on the egg.
    /// </summary>
    [Fact]
    public void The_elders()
    {
        if (Elders.Value is not { } played)
        {
            return;
        }

        var said = Said(played);
        Assert.Contains("Last leaf of the year.", said);
        Assert.Contains("Feels heavier than it looks.", said);
        Assert.Contains("Thank goodness you're still here.", said);
        Assert.Equal(9, played.World.CurrentRoomNumber);
        Assert.Equal(3, played.World.Engine.Variables[Known]);
        Assert.True(Learned(played, Opening));
    }

    /// <summary>
    /// The drafts of the island: Dyeing from the dye pot, spun on the white wool; Emptying from the
    /// flask; Night Vision from the owls, spun on the dark tent; Straw into Gold from the wheel,
    /// spun on the straw.
    /// </summary>
    [Fact]
    public void The_island()
    {
        if (Island.Value is not { } played)
        {
            return;
        }

        var said = Said(played);
        Assert.Contains("I changed the color!", said);
        Assert.Contains("Oops!", said);
        Assert.Contains("Poor little fella.", said);
        Assert.Contains("This old wheel is pretty squeaky.", said);
        Assert.Equal(62, played.World.CurrentRoomNumber);
        Assert.All([Dyeing, Emptying, NightVision, StrawIntoGold], d => Assert.True(Learned(played, d)));
        Assert.All([Dyeing, NightVision, StrawIntoGold], d => Assert.True(Used(played, d)));
    }

    /// <summary>
    /// Opening on the sky splits the tree, and the distaff gains f; Opening on the clam feeds the
    /// gull. Bobbin jumps from the dock onto the log, learns Twisting from the waterspout, gains g,
    /// and spins Un-Twisting to calm it; the log carries him to the shore.
    /// </summary>
    [Fact]
    public void Leaving_the_island()
    {
        if (Leaving.Value is not { } played)
        {
            return;
        }

        var said = Said(played);
        Assert.Contains("Is it over now?", said);
        Assert.Contains("Whew! Listen to that twisty wind!", said);
        Assert.Equal(15, played.World.CurrentRoomNumber);
        Assert.Equal(5, played.World.Engine.Variables[Known]);
        Assert.True(Used(played, Opening));
        Assert.True(Used(played, Twisting));
    }

    /// <summary>
    /// The shepherds in the woods hide and show themselves, and Bobbin learns Invisibility; they
    /// demand proof that he is a wizard. In Crystalgard he spins Invisibility on the workers on the
    /// tower, overhears the Bishop and Master Crucible, rides the crystal up to learn Sharpening from
    /// the scythe and the bell down to the sphere, whose vision of the Shepherd guard teaches
    /// Terror. Back in the woods, Terror sends the shepherds running.
    /// </summary>
    [Fact]
    public void Crystalgard_and_the_shepherds()
    {
        if (Glassmakers.Value is not { } played)
        {
            return;
        }

        var said = Said(played);
        Assert.Contains("Prove you're a wizard or you're not getting any farther.", said);
        Assert.Contains("That felt right. I think it worked.", said);
        Assert.Contains("EIGHT hours. See to it, Master Crucible.", said);
        Assert.Contains("That thing is SHARP.", said);
        Assert.Contains("What fun.", said);
        Assert.Equal(23, played.World.CurrentRoomNumber);
        Assert.All([Invisibility, Terror, Sharpening], d => Assert.True(Learned(played, d)));
        Assert.True(Used(played, Terror));
        Assert.False(played.World.GetClass(ShepherdsWest, Untouchable));
    }

    /// <summary>
    /// Fleece, in the shepherds' house, tells of the dragon, and her lamb teaches Healing; a
    /// shepherd sings the sheep to sleep, which teaches Sleep. Dyeing the flock green brings the
    /// dragon down on Bobbin instead. In its lair Un-Straw into Gold turns the gold to straw and
    /// Sleep puts the dragon to sleep on it; the townsfolk's torches set it running, and A joins
    /// the distaff.
    /// </summary>
    [Fact]
    public void The_dragon()
    {
        if (Shepherds.Value is not { } played)
        {
            return;
        }

        var said = Said(played);
        Assert.Contains("I am named Fleece, First Chosen of the Guild of Shepherds.", said);
        Assert.Contains("I guess I'm not very good yet.", said);
        Assert.Contains("Thought you felt a little scrawny.", said);
        Assert.Contains("My! Aren't we clever today!", said);
        Assert.Contains("Eeeek! Fire!", said);
        Assert.Equal(28, played.World.CurrentRoomNumber);
        Assert.Equal(6, played.World.Engine.Variables[Known]);
        Assert.All([Healing, Sleep], d => Assert.True(Learned(played, d)));
        Assert.True(Used(played, Sleep));
    }

    /// <summary>
    /// Night Vision lights the caves under the mountain. The tunnels lead round by the east cave to
    /// the ledge, and Bobbin drops to the pool, which teaches Reflection; Emptying drains it to show
    /// the dragon's sphere, and behind the rock the tunnels lead to the twisted steps. Un-Twisting
    /// them climbs to the halls of the forge.
    /// </summary>
    [Fact]
    public void The_caves()
    {
        if (Caves.Value is not { } played)
        {
            return;
        }

        var said = Said(played);
        Assert.Contains("Guess I won't be going back that way.", said);
        Assert.Contains("Is that MY reflection? Ugh.", said);
        Assert.Contains("Repair costs must be spiraling.", said);
        Assert.Equal(34, played.World.CurrentRoomNumber);
        Assert.True(Learned(played, Reflection));
    }

    /// <summary>
    /// Bobbin wakes the boy in the halls of the forge, Rusty Nailbender, who talks and dozes off
    /// again; Reflection gives Bobbin his looks. The gatekeeper lets "Rusty" in, and Stoke, taking
    /// the stick in the woodbin for all the firewood his apprentice gathered, locks him in a room
    /// with a pile of straw. It has a sleep draft spun into it, and Bobbin dreams of the dragon
    /// finding Rusty; Hetchel pushes the distaff under the door, and Opening lets him out.
    /// </summary>
    [Fact]
    public void The_forge()
    {
        if (Smiths.Value is not { } played)
        {
            return;
        }

        var said = Said(played);
        Assert.Contains("My real name is Wellwrought, but everyone calls me Rusty.  Rusty Nailbender.", said);
        Assert.Contains("Let me open the gate for you.", said);
        Assert.Contains("I'll be back, Nailbender.", said);
        Assert.Contains("This straw must have a sleep draft spun into it.", said);
        Assert.Contains("Now I'm back in business!", said);
        Assert.Equal(37, played.World.CurrentRoomNumber);
        Assert.True(Used(played, Reflection));
    }

    /// <summary>
    /// Bobbin overhears the Bishop and the Foreman over the hammering, sets the Foreman asking
    /// after the blade, and in the quiet while Edgewise answers spins Twisting on it. Taken to
    /// the Bishop's castle and caged, he spins Opening on the cage, and Mandible has the distaff
    /// from him. Cob trades a look into the Sphere for one under Bobbin's hood, and dies of it;
    /// Mandible calls up the Dead with the distaff, and Chaos comes. Bobbin is thrown into the
    /// void.
    /// </summary>
    [Fact]
    public void The_bishop()
    {
        if (Clerics.Value is not { } played)
        {
            return;
        }

        var said = Said(played);
        Assert.Contains("The edge has been mischievously twisted!", said);
        Assert.Contains("I am Bishop Mandible, Transultimate Apostle of the Antisecular Conclave of Clerics.", said);
        Assert.Contains("Give me that!", said);
        Assert.Contains("Can't say I didn't warn him.", said);
        Assert.Contains("I am Chaos.", said);
        Assert.Contains("Yipes!", said);
        Assert.Equal(46, played.World.CurrentRoomNumber);
    }

    /// <summary>
    /// In the void Healing closes the holes the Opening tore: the one to the castle; the forge's,
    /// once Rusty, killed by the dragon, is healed; the shepherds' once they are; and Crystalgard's,
    /// where Goodmold tells of its fall. Westward on the Shore of Wonder the swan is Lady Cygna,
    /// Bobbin's mother.
    /// </summary>
    [Fact]
    public void The_void()
    {
        if (Void.Value is not { } played)
        {
            return;
        }

        var said = Said(played);
        Assert.Contains("You did it.", said);
        Assert.Contains("Shepherds have long memories, wizard. I shall not forget your kindness.", said);
        Assert.Contains("Above all else ... clarity.", said);
        Assert.Contains("I've missed you ... my son.", said);
        Assert.Equal(47, played.World.CurrentRoomNumber);
        Assert.All([HoleToCrystalgard, HoleToShepherds, HoleToForge, HoleToCastle], h => Assert.True(played.World.GetClass(h, Untouchable)));
    }

    /// <summary>
    /// Back in the sanctuary Chaos has the Loom. Its threads teach Silence, and Un-Silence frees
    /// Hetchel's voice; Shaping, and Un-Shaping undoes what Chaos made of her; Unmaking, spun on
    /// the Loom, unmakes it and tears the Pattern in two. On the swans' side of the rift, the
    /// distaff's high C, Transcendence spun on himself takes Bobbin away with them: the end.
    /// </summary>
    [Fact]
    public void The_loom()
    {
        if (Ending.Value is not { } played)
        {
            return;
        }

        var said = Said(played);
        Assert.Contains("Thanks goodness! I can talk again!", said);
        Assert.Contains("The Loom is unmade, beyond Evil's reach!", said);
        Assert.Contains("Guess I'm ready to go.", said);
        Assert.Contains("Bobbin. It's time!", said);
        Assert.Equal("The End", said[^1]);
        Assert.Equal(8, played.World.Engine.Variables[Known]);
        Assert.All([Silence, Shaping], d => Assert.True(Used(played, d)));
        Assert.True(Learned(played, Unmaking));
    }

    /// <summary>
    /// The opening played through instead of skipped: Practice chosen, the island at dawn, then the Elders'
    /// messenger, a nymph, rouses Bobbin (room 53, the only room the way through and its detours
    /// do not enter) and calls him to the Sanctuary. Control comes back with Bobbin on
    /// the cliff (room 2), where the way through starts.
    /// </summary>
    [Fact]
    public void The_opening_plays_to_the_end()
    {
        if (GameFolders.Find(GameFolders.Ega) is not { } path)
        {
            return;
        }

        var world = new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path)));
        var w = new Walkthrough(world);
        world.Boot();
        w.Wait(60).ClickAt(w.LevelButton.X, w.LevelButton.Y);
        w.InRoom(53, 20000).InRoom(2, 20000).Ready(20000);

        var said = world.MessageLog;
        Assert.Contains("Rise, son of Cygna.", said);
        Assert.Contains("I am sent by the Elders to summon you.", said);
        Assert.Contains("The High Council awaits your presence in the Sanctuary.", said);
        Assert.Contains("Better get down to the village.", said);
        Assert.Equal(2, world.CurrentRoomNumber);
    }

    /// <summary>
    /// The distaff's answers to a draft gone wrong, in the sanctuary after the Elders have gone:
    /// nothing pointed at, a draft the object will not take, notes that are no draft, a note typed
    /// that Bobbin has not learned, and a draft spun while Bobbin is still on his way to what he
    /// pointed at.
    /// </summary>
    [Fact]
    public void The_distaff_answers_a_draft_gone_wrong()
    {
        if (Elders.Value is not { } before || Play(before, DistaffMistakes) is not { } played)
        {
            return;
        }

        var said = Said(played);
        Assert.Contains("I suppose I ought to point at something first.", said);
        Assert.Contains("That draft won't work on the Loom.", said);
        Assert.Contains("Hmm. I guess that isn't a draft.", said);
        Assert.Contains("That thread is too high for me! Guess I need more experience.", said);
        Assert.Contains("Maybe I ought to stand a bit closer.", said);
    }

    /// <summary>
    /// Clicking the way out of the waterspout's room while it still spins walks Bobbin into it: it
    /// carries him up and drops him in the sea (room 12), and he swims back. The way through then
    /// goes on as before: Un-Twisting calms the waterspout and the log takes him to the shore.
    /// </summary>
    [Fact]
    public void The_waterspout_takes_bobbin_out_to_sea()
    {
        if (Island.Value is not { } before || Play(before, OutToSea) is not { } played)
        {
            return;
        }

        var said = Said(played);
        Assert.Contains("Whew! Listen to that twisty wind!", said);
        Assert.Contains("Here we go again.", said);
        Assert.Equal(15, played.World.CurrentRoomNumber);
    }

    /// <summary>
    /// Crystalgard's graveyard and the chalice's room, which the way through passes by. Walking
    /// into the graveyard brings Master Goodmold out to greet the stranger; the graves have their
    /// epitaphs and are not to be opened; the chalice beyond tells of the Chromax Conundrum.
    /// </summary>
    [Fact]
    public void The_graves_and_the_chalice()
    {
        if (Leaving.Value is not { } before || Play(before, GravesAndChalice) is not { } played)
        {
            return;
        }

        var said = Said(played);
        Assert.Contains("I am Master Goodmold, thirty-first in the Noble Guild of Glassmakers.", said);
        Assert.Contains("But remember: You break it, you buy it!", said);
        Assert.Contains("+Near this spot, Luscent Bottleblower, Founder of the Noble Guild of Glassmakers, attained his Final Clarity./", said);
        Assert.Contains("+Softshard, Wife of Luscent Bottleblower, here attained Final Clarity./", said);
        Assert.Contains("Maybe I ought not to try that.", said);
        Assert.Contains("Nice! Never seen glass sparkle like this.", said);
        Assert.Contains("That's the Conundrum.", said);
        Assert.Equal(16, played.World.CurrentRoomNumber);
    }

    /// <summary>
    /// Dyeing tried on wool that is already its colour. After the dye pot, Dyeing on the green
    /// wool is a draft not yet used (script 35); once used on the white wool, Dyeing again finds
    /// it green enough, and Dyeing backwards, twice, finds it as pale as it gets.
    /// </summary>
    [Fact]
    public void Dyeing_on_wool_that_is_already_dyed()
    {
        if (Elders.Value is not { } before || Play(before, DyeingAgain) is not { } played)
        {
            return;
        }

        var said = Said(played);
        Assert.Contains("Those threads didn't feel quite right.", said);
        Assert.Contains("Think I'm on the right track, though.", said);
        Assert.Contains("It's plenty green enough already.", said);
        Assert.Contains("It already looks about as pale as it can get.", said);
        Assert.True(Used(played, Dyeing));
    }

    /// <summary>
    /// The caves' other passages, which the way through passes by (scripts 77, 80 and 84): the
    /// one back up to the dragon's lair, the second way into the east cave from where its first
    /// comes out, and the way back up to the ledge over the pool from below it.
    /// </summary>
    [Fact]
    public void The_caves_other_passages()
    {
        if (Shepherds.Value is not { } before || Play(before, OtherPassages) is not { } played)
        {
            return;
        }

        Assert.Equal(31, played.World.CurrentRoomNumber);
    }

    /// <summary>
    /// A draft spun on the Dead One across the rift (script 103): Cygna answers that the other
    /// side of the Pattern is beyond the threads' reach. Looked at, he is close, but not close
    /// enough.
    /// </summary>
    [Fact]
    public void A_draft_on_the_dead_one()
    {
        if (Void.Value is not { } before || Play(before, DraftOnTheDeadOne) is not { } played)
        {
            return;
        }

        var said = Said(played);
        Assert.Contains("Close. But not close enough ...", said);
        Assert.Contains("No use, Bobbin. That side of the Pattern is beyond the reach of our threads now.", said);
        Assert.Equal(51, played.World.CurrentRoomNumber);
    }

    /// <summary>
    /// Opening spun at the grave in the woods: the ground shakes (<c>SO_ROOM_SHAKE_ON</c>), and the
    /// grave stays shut. While it shakes, the whole picture is raised by 0 to 3 rows, black below,
    /// three of the shake's steps to each frame of the game; a save made then keeps it shaking. The
    /// shake stops before Bobbin speaks. Found by the object sweep.
    /// </summary>
    [Fact]
    public void Opening_at_the_grave_shakes_the_ground()
    {
        var offsets = new List<int>();
        var frames = new List<int>();
        byte[]? shaking = null;
        if (Elders.Value is not { } before || Play(before, w =>
        {
            w.Use(SanctuaryOut).InRoom(8).Ready().Use(SanctuaryToPath).InRoom(7).Ready().Use(PathToTents).InRoom(5).Ready();
            w.Use(TentsToVillage).InRoom(3).Ready().Use(VillageToWoods).InRoom(4).Ready();
            w.Click(Grave).Spin(Opening, wait: false);
            w.Until(() =>
            {
                var world = w.World;
                if (world.ShakeEnabled)
                {
                    frames.Add(world.ShakeFrame);
                    int k = world.ShakeOffset;
                    offsets.Add(k);
                    var plain = world.UnshakenFrame(cursor: false);
                    var shaken = world.Frame(cursor: false);
                    for (int y = 0; y < shaken.Height; y++)
                    {
                        Assert.True(y + k < plain.Height ? shaken.Row(y).SequenceEqual(plain.Row(y + k)) : shaken.Row(y).ToArray().All(c => c == default), $"row {y} raised {k}");
                    }

                    shaking ??= world.Save("shaking", new DateTime(2026, 10, 28));
                }

                return w.InControl;
            }, 20000, "the grave's answer").Ready();
        }) is not { } played)
        {
            return;
        }

        Assert.False(played.World.ShakeEnabled);
        Assert.Equal("Maybe I'd better not try that.", Said(played).Last(m => m.Length > 0));
        Assert.True(offsets.Count >= 15, $"shook for {offsets.Count} frames");
        Assert.Equal([1, 4, 7, 2, 5, 0, 3, 6], frames.Take(8));
        Assert.Contains(3, offsets);

        var loaded = new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(GameFolders.Find(GameFolders.Ega)!)));
        loaded.Load(shaking!);
        Assert.True(loaded.ShakeEnabled);
    }

    /// <summary>
    /// Invisibility spun at the shepherds when they first stop Bobbin in the woods: they are not
    /// fooled. On the way, the scene prints a line for actor 0, which ScummVM takes
    /// (<c>derefActor</c>) and does not show, actor 0 being in no room. Found by the object sweep.
    /// </summary>
    [Fact]
    public void Invisibility_at_the_shepherds()
    {
        if (Leaving.Value is not { } before || Play(before, InvisibilityAtTheShepherds) is not { } played)
        {
            return;
        }

        Assert.Contains("That camouflage trick won't work on us.", Said(played));
        Assert.Equal(23, played.World.CurrentRoomNumber);
    }

    /// <summary>
    /// From the waterspout's room, before the waterspout is calmed, the way back along the log
    /// takes Bobbin back up the dock (script 107). The log takes him out again, and the way
    /// through goes on: Un-Twisting calms the waterspout and the log takes him to the shore.
    /// </summary>
    [Fact]
    public void The_log_takes_bobbin_back_to_the_dock()
    {
        if (Island.Value is not { } before || Play(before, BackToTheDock) is not { } played)
        {
            return;
        }

        Assert.Equal(15, played.World.CurrentRoomNumber);
    }

    /// <summary>
    /// Saves made by ScummVM play on here. ScummVM loaded the walkthrough's saves from the end of
    /// the Elders and of the Bishop, played on a few seconds and saved by itself (its autosave,
    /// save version 124); the next section, played on from each, ends as it does from the
    /// walkthrough's own save: in the same room, with the same drafts learned and used. The saves
    /// are kept beside the game data, in <c>games/scummvm-saves</c>, and the test passes without
    /// checking when they are absent.
    /// </summary>
    [Theory]
    [InlineData("01-TheElders", 1)]
    [InlineData("08-TheBishop", 8)]
    public void A_save_made_by_scummvm_plays_on(string name, int next)
    {
        if (GameFolders.Find("scummvm-saves") is not { } folder || !File.Exists(Path.Combine(folder, $"loom-ega-{name}.s00")))
        {
            return;
        }

        var ours = next == 1 ? Island.Value : Void.Value;
        var save = File.ReadAllBytes(Path.Combine(folder, $"loom-ega-{name}.s00"));
        if (ours is null || Play(new Played(ours.World, save, 0), Sections[next]) is not { } played)
        {
            return;
        }

        Assert.Equal(ours.World.CurrentRoomNumber, played.World.CurrentRoomNumber);
        for (int draft = 0; draft < 16; draft++)
        {
            Assert.Equal(Learned(ours, draft), Learned(played, draft));
            Assert.Equal(Used(ours, draft), Used(played, draft));
        }
    }

    private static void DistaffMistakes(Walkthrough w)
    {
        w.WalkTo(200, 130).Ready();
        w.Spin(Opening);
        w.Click(Loom).Spin(Opening);
        w.Click(Loom).PlayNotes([3, 3, 3, 3]);

        // the loom clicked from across the room: Bobbin points at it at once, and walks on
        w.WalkTo(40, 130).Ready();
        var (x, y) = w.OnScreen(Loom);
        w.ClickAt(x, y).Until(() => w.World.Engine.Variables[w.Pointed] == Loom, 200, "Bobbin to point at the Loom");
        w.Spin(Opening);

        // a note typed that Bobbin has not learned yet
        w.Click(Loom).Key(Key.G, 'g').Wait(100).Ready(20000);
    }

    private static void BackToTheDock(Walkthrough w)
    {
        // as the way through leaves the island, as far as the waterspout
        w.Click(WheelOut).InRoom(5).Ready().Click(TentsToVillage).InRoom(3).Ready().Click(VillageToCliff).InRoom(2).Ready();
        w.Click(Sky).Click(Sky).Spin(Opening).Ready(20000);
        w.Click(CliffToVillage).InRoom(3).Ready().Click(VillageToDock).InRoom(10).Ready();
        w.Click(Clam).Spin(Opening).Ready(20000);
        w.WalkTo(182, 53).Ready(20000);
        w.Click(DockWater).InRoom(13).Ready(20000);
        w.Use(Waterspout).Ready(20000);

        // the way back along the log, before the waterspout is calmed, takes Bobbin back to the
        // dock (script 107); the log takes him out again, and Un-Twisting calms it as on the way through
        w.Exit(WaterspoutToDock, 10, 20000);
        w.WalkTo(182, 53).Ready(20000);
        w.Click(DockWater).InRoom(13).Ready(20000);
        w.Click(Waterspout).Spin(Twisting, backwards: true).Ready(20000);
        w.Click(WaterspoutOut).InRoom(15).Ready(20000);
    }

    private static void OutToSea(Walkthrough w)
    {
        // as the way through leaves the island, as far as the waterspout
        w.Click(WheelOut).InRoom(5).Ready().Click(TentsToVillage).InRoom(3).Ready().Click(VillageToCliff).InRoom(2).Ready();
        w.Click(Sky).Click(Sky).Spin(Opening).Ready(20000);
        w.Click(CliffToVillage).InRoom(3).Ready().Click(VillageToDock).InRoom(10).Ready();
        w.Click(Clam).Spin(Opening).Ready(20000);
        w.WalkTo(182, 53).Ready(20000);
        w.Click(DockWater).InRoom(13).Ready(20000);
        w.Use(Waterspout).Ready(20000);

        // the way out walks Bobbin to the log while the waterspout spins, and into it
        w.Click(WaterspoutOut).InRoom(12).Ready(20000);
        w.Use(SeaBack).InRoom(13).Ready(20000);
        w.Click(Waterspout).Spin(Twisting, backwards: true).Ready(20000);
        w.Click(WaterspoutOut).InRoom(15).Ready(20000);
    }

    private static void GravesAndChalice(Walkthrough w)
    {
        w.Exit(ShoreToCrystalgard, 16);
        w.Exit(CrystalgardToGraves, 17, 20000);

        // the walk to the graves brings Goodmold out, whose welcome takes the clicks, so they are looked at again
        w.Use(BottleblowGrave).Ready(20000).Use(SoftshardGrave).Ready(20000);
        w.Use(BottleblowGrave).Ready(20000).Use(SoftshardGrave).Ready(20000);
        w.Click(BottleblowGrave).Spin(Opening).Ready(20000);
        w.Exit(GravesToChalice, 22, 20000);
        w.Use(Chalice).Ready(20000).Use(ShowcaseCrystal).Ready(20000).Use(ShowcaseBell).Ready(20000);
        w.Exit(ChaliceToCrystalgard, 16, 20000);
    }

    private static void DyeingAgain(Walkthrough w)
    {
        w.Use(SanctuaryOut).InRoom(8).Ready().Use(SanctuaryToPath).InRoom(7).Ready().Use(PathToTents).InRoom(5).Ready();
        w.Use(TentsToHetchel).InRoom(6).Ready();
        w.Use(DyePot).Ready(20000);
        w.Click(GreenWool).Spin(Dyeing).Ready(20000);
        w.Click(WhiteWool).Spin(Dyeing).Ready(20000);
        w.Click(WhiteWool).Spin(Dyeing).Ready(20000);
        w.Click(WhiteWool).Spin(Dyeing, backwards: true).Ready(20000);
        w.Click(WhiteWool).Spin(Dyeing, backwards: true).Ready(20000);
    }

    private static void OtherPassages(Walkthrough w)
    {
        w.Click(LairToCaves).InRoom(65).Ready(20000);
        w.Click(CaveDarkness).Spin(NightVision).Ready(20000);

        // each passage can be clicked only from the part of the caves it leads from (room 30's
        // script 200 keeps the others untouchable): the one up to the lair from where Bobbin comes in
        w.Exit(CaveToLair, 28, 20000);
        w.Exit(LairToCaves, 30, 20000);

        // the east cave by its first way, and back out by it, then in by the second
        w.Click(514).Ready(20000);
        w.Exit(CaveEast, 32, 20000);
        w.Exit(EastCaveBack, 30, 20000);
        w.Exit(CaveEastBelow, 32, 20000);
        w.Exit(EastCaveBack, 30, 20000);

        // the ledge over the pool, the pool emptied, and up again from below it
        w.Click(503).Ready(20000);
        w.Exit(CaveLedge, 31, 20000);
        w.Use(Pool).Ready(20000);
        w.Click(Pool).Spin(Emptying).Ready(20000);
        w.Exit(PoolOut, 30, 20000);
        w.Exit(CaveToLedgeBelow, 31, 20000);
    }

    private static void DraftOnTheDeadOne(Walkthrough w)
    {
        // as the way through, as far as the rift
        w.Exit(WonderWest, 50, 20000);
        w.Exit(BackToTheIsland, 7, 20000);
        w.Exit(PathToSanctuary, 9, 20000);
        w.Use(Loom).Ready(20000);
        w.Click(Hetchel).Spin(Silence, backwards: true).Ready(20000);
        w.Use(Loom).Ready(20000);
        w.Click(Hetchel).Spin(Shaping, backwards: true).Ready(20000);
        w.Use(Loom).Ready(20000);
        w.Use(Feather).Ready(20000);
        w.Click(Loom).Spin(Unmaking).InRoom(51).Ready(20000);
        w.Use(DeadOne).Ready(20000);
        w.Click(DeadOne).Spin(Healing).Ready(20000);
    }

    // Hetchel's tent's green wool (6); the caves' other passages (30); the Dead One across the rift (51)
    private const int GreenWool = 206;
    private const int CaveToLair = 507, CaveEastBelow = 510, CaveToLedgeBelow = 518;
    private const int DeadOne = 742;

    // the sea (12) and the way back from it; Crystalgard's graveyard (17) and the chalice's room (22)
    private const int SeaBack = 322;
    private const int CrystalgardToGraves = 349, BottleblowGrave = 357, SoftshardGrave = 358, GravesToChalice = 353, Chalice = 405, ChaliceToCrystalgard = 401;
    private const int ShowcaseBell = 403, ShowcaseCrystal = 404;

    /// <summary>The drafts as the game numbers them in its table.</summary>
    private const int Opening = 0, StrawIntoGold = 1, Dyeing = 2, NightVision = 3, Twisting = 4, Sleep = 5, Emptying = 6;
    private const int Invisibility = 7, Terror = 8, Sharpening = 9, Reflection = 10, Healing = 11;
    private const int Silence = 12, Shaping = 13, Unmaking = 14, Transcendence = 15;

    /// <summary>The class the scripts set on what cannot be clicked.</summary>
    private const int Untouchable = 32;

    /// <summary>The notes the distaff plays, 3 for c d e.</summary>
    private const int Known = 122;

    private static void TheElders(Walkthrough w)
    {
        // the proficiency screen prints on, so it is waited out; the level, then Escape skips the opening
        w.Wait(60).ClickAt(w.LevelButton.X, w.LevelButton.Y).Wait(100).Key(Key.Escape, (char)27).Ready();
        w.Use(Leaf);
        w.Use(CliffToVillage).InRoom(3).Ready();
        w.Use(VillageToTents).InRoom(5).Ready();
        w.Use(TentsToPath).InRoom(7).Ready();
        w.Use(PathToSanctuary).InRoom(8).Ready();
        w.Use(SanctuaryDoor).InRoom(9).Ready(20000);
        w.Use(Distaff);
        w.Click(Egg).Spin(Opening).Ready(20000);
    }

    private static void TheIsland(Walkthrough w)
    {
        w.Use(SanctuaryOut).InRoom(8).Ready().Use(SanctuaryToPath).InRoom(7).Ready().Use(PathToTents).InRoom(5).Ready();
        w.Use(TentsToHetchel).InRoom(6).Ready();
        w.Use(DyePot).Ready(20000);
        w.Click(WhiteWool).Spin(Dyeing).Ready(20000);
        w.Use(Flask).Ready(20000);
        w.Use(HetchelOut).InRoom(5).Ready().Use(TentsToVillage).InRoom(3).Ready().Use(VillageToWoods).InRoom(4).Ready();

        // three holes with owls and an empty one; the thorns bring the fourth, and it is counted
        // when the empty hole is looked at again
        w.Use(171).Use(172).Use(173).Use(174);
        w.Use(Thorns).Ready(20000);
        w.Use(174).Use(171);
        w.Use(Grave).Ready(20000);

        // exits wait for the click to end (bit 3450), so they are clicked once
        w.Use(GravestoneOut).InRoom(4).Ready().Click(WoodsToVillage).InRoom(3).Ready().Use(VillageToTents).InRoom(5).Ready();
        w.Use(TentsToDark).InRoom(63).Ready();
        w.Click(Darkness).Spin(NightVision).Ready(20000);
        w.Use(Wheel).Ready(20000);
        w.Click(Straw).Spin(StrawIntoGold).Ready(20000);
    }

    private static void LeavingTheIsland(Walkthrough w)
    {
        w.Click(WheelOut).InRoom(5).Ready().Click(TentsToVillage).InRoom(3).Ready().Click(VillageToCliff).InRoom(2).Ready();

        // the path down the cliff takes Bobbin over in a cutscene, and the sky is clicked again from there
        w.Click(Sky).Click(Sky).Spin(Opening).Ready(20000);
        w.Click(CliffToVillage).InRoom(3).Ready().Click(VillageToDock).InRoom(10).Ready();
        w.Click(Clam).Spin(Opening).Ready(20000);

        // the end of the dock is a box of its own: walking into it jumps
        w.WalkTo(182, 53).Ready(20000);
        w.Click(DockWater).InRoom(13).Ready(20000);
        w.Use(Waterspout).Ready(20000);
        w.Click(Waterspout).Spin(Twisting, backwards: true).Ready(20000);
        w.Click(WaterspoutOut).InRoom(15).Ready(20000);
    }

    private static void Crystalgard(Walkthrough w)
    {
        // walking far enough into the woods brings the shepherds out
        w.Exit(ShoreToWoods, 23);
        w.WalkTo(110, 120).Ready(20000);

        // back on the shore path, so the walk east keeps clear of the shepherds' clearing
        w.WalkTo(300, 130).Exit(WoodsToShore, 15);
        w.Exit(ShoreToCrystalgard, 16);
        w.Use(Workers);
        w.Click(Workers).Spin(Invisibility).Ready(20000);
        w.Exit(CrystalgardToTower, 18);
        w.Use(Conversation).Ready(20000);
        w.Use(Crystal).Ready(20000);
        w.Use(Scythe).Ready(20000);

        // the left bell rides down to the bell's side of the tower, where the sphere is
        w.Use(BellDownToSphere).Ready(20000);
        w.Use(Sphere).Use(Sphere).Use(Sphere).Ready(20000);
        w.Use(TowerBell).InRoom(20).Ready(20000);
        w.Use(BellDownToCrystal).InRoom(18).Ready(20000);
        w.Exit(TowerOut, 16);
        w.Exit(CrystalgardToWoods, 23);

        // the path down from Crystalgard ends in a cutscene halfway
        w.WalkTo(110, 120).Ready(20000).WalkTo(110, 120).Ready(20000);
        w.Click(Shepherd).Spin(Terror).Ready(20000);
    }

    private static void InvisibilityAtTheShepherds(Walkthrough w)
    {
        w.Exit(ShoreToWoods, 23);
        w.WalkTo(110, 120).Ready(20000);
        w.Click(Shepherd).Spin(Invisibility).Ready(20000);
    }

    private static void TheDragon(Walkthrough w)
    {
        w.Exit(ShepherdsWest, 24);
        w.Exit(PastureToHouse, 25);
        w.Use(Lamb).Ready(20000);
        w.Use(Lamb).Ready(20000);
        w.Exit(HouseOut, 24);
        w.Use(SleepySheep).Ready(20000);
        w.Click(Flock).Spin(Dyeing).Ready(20000);
        w.Use(Dragon).Ready(20000);
        w.Use(Gold).Ready(20000);
        w.Click(Gold).Spin(StrawIntoGold, backwards: true).Ready(20000);
        w.Click(Dragon).Spin(Sleep).Ready(20000);
        w.Until(() => w.World.Engine.Variables[DragonGone] == 2, 20000, "the dragon to leave").Ready(20000);
    }

    private static void TheCaves(Walkthrough w)
    {
        w.Click(LairToCaves).InRoom(65).Ready(20000);
        w.Click(CaveDarkness).Spin(NightVision).Ready(20000);

        // the tunnels: 514 to the east ledge, the east cave and back, 503 to the ledge over the pool
        w.Click(514).Ready(20000);
        w.Exit(CaveEast, 32, 20000);
        w.Exit(EastCaveBack, 30, 20000);
        w.Click(503).Ready(20000);
        w.Exit(CaveLedge, 31, 20000);
        w.Use(Pool).Ready(20000);
        w.Click(Pool).Spin(Emptying).Ready(20000);
        w.Use(PoolSphere).Ready(20000).Use(PoolSphere).Ready(20000).Use(PoolSphere).Ready(20000);
        w.Exit(PoolOut, 30, 20000);
        w.Click(504).Ready(20000);
        w.Exit(CaveToSteps, 33, 20000);
        w.Use(Steps).Ready(20000);
        w.Click(Steps).Spin(Twisting, backwards: true).Ready(20000);
        w.Exit(StepsUp, 34, 20000);
    }

    private static void TheForge(Walkthrough w)
    {
        w.Use(Boy).Ready(20000);
        w.Click(Boy).Spin(Sleep, backwards: true).Ready(20000);

        // asleep again, he is spun into Bobbin's robe
        w.Click(Boy).Spin(Reflection).Ready(20000);
        w.Exit(HallsToGate, 35, 20000);
        w.Click(ForgeGate).Ready(20000);
        w.Exit(OpenGate, 36, 20000);

        // down the stairs to the floor of the forge, where its east way is
        w.WalkTo(440, 126).Ready(20000).WalkTo(400, 124).Ready(20000);
        w.Exit(ForgeEast, 37, 20000);
        w.Use(Woodbin).InRoom(38).Ready(20000);
        w.Use(SleepyStraw).Ready(20000);
        w.Use(DistaffUnderDoor).Ready(20000);
        w.Click(CellDoor).Spin(Opening).Ready(20000);
        w.Exit(CellOut, 37, 20000);
    }

    private static void TheBishop(Walkthrough w)
    {
        w.Exit(ToTheSword, 41, 20000);

        // the Bishop and the Foreman come in to talk over the hammering
        w.Until(() => !w.World.GetClass(Talk, Untouchable), 20000, "the conversation");
        w.Use(Talk).Ready(20000);

        // the Foreman asks after the blade, and Edgewise stops hammering to answer
        w.Until(() => !w.World.GetClass(TalkAgain, Untouchable), 20000, "the conversation to end");
        w.Use(TalkAgain, wait: false);
        w.Until(() => !w.World.GetClass(QuietSword, Untouchable), 20000, "a quiet moment");
        w.PointAt(QuietSword).Spin(Twisting).Ready(20000);
        w.Click(Cage).Spin(Opening).Ready(20000);
        w.Use(Orb).Ready(20000).Use(CastleSphere).Ready(20000).Use(CastleSphere).Ready(20000);
        w.Click(CastleOut).InRoom(44).Ready(20000);
        w.Use(Mandible).InRoom(45).Ready(20000);
        w.Use(DroppedDistaff).Ready(20000);
        w.Click(SummoningOut).InRoom(43).Ready(20000);
        w.Click(CastleOut).InRoom(46).Ready(20000);
    }

    private static void TheVoid(Walkthrough w)
    {
        w.Click(HoleToCastle).Spin(Healing).Ready(20000);
        w.Use(HoleToForge).InRoom(34).Ready(20000);
        w.Use(DeadRusty).Ready(20000);
        w.Click(DeadRusty).Spin(Healing).Ready(20000);
        w.Exit(HallsToVoid, 46, 20000);
        w.Click(HoleToForge).Spin(Healing).Ready(20000);
        w.Use(HoleToShepherds).InRoom(26).Ready(20000);
        w.Click(DeadShepherds).Spin(Healing).Ready(20000);
        w.Exit(ShepherdsHoleOut, 46, 20000);
        w.Click(HoleToShepherds).Spin(Healing).Ready(20000);

        // Goodmold is healed as soon as he can be pointed at
        w.Use(HoleToCrystalgard, wait: false).InRoom(49);
        w.Until(() => !w.World.GetClass(Goodmold, Untouchable) && w.World.Engine.CutsceneLevel == 0, 20000, "Goodmold");
        w.PointAt(Goodmold).Spin(Healing).Ready(20000);
        w.Exit(GoodmoldOut, 46, 20000);
        w.Click(HoleToCrystalgard).Spin(Healing).Ready(20000);
        w.Exit(VoidWest, 47, 20000);
        w.Use(Cygna).Ready(20000);
    }

    private static void TheLoom(Walkthrough w)
    {
        w.Exit(WonderWest, 50, 20000);
        w.Exit(BackToTheIsland, 7, 20000);
        w.Exit(PathToSanctuary, 9, 20000);
        w.Use(Loom).Ready(20000);
        w.Click(Hetchel).Spin(Silence, backwards: true).Ready(20000);
        w.Use(Loom).Ready(20000);
        w.Click(Hetchel).Spin(Shaping, backwards: true).Ready(20000);
        w.Use(Loom).Ready(20000);
        w.Use(Feather).Ready(20000);
        w.Click(Loom).Spin(Unmaking).InRoom(51).Ready(20000);
        w.Click(Rift).InRoom(66).Ready(20000);
        w.Use(Myself).Ready(20000);

        // the swans fly, the credits roll, and the game does not hand control back
        w.Click(Myself).Spin(Transcendence, wait: false);
        w.Until(() => w.World.MessageLog.LastOrDefault() == "The End", 20000, "the end");
    }

    // the objects, by room: the cliff (2), the village (3), the woods (4), the tents (5), Hetchel's
    // tent (6), the path (7), outside the sanctuary (8), the sanctuary (9), the dock (10), the
    // waterspout (13), the wheel's tent (62), the dark tent (63), the gravestone (64)
    private const int CliffToVillage = 153, Leaf = 154, Sky = 156;
    private const int VillageToCliff = 160, VillageToTents = 161, VillageToWoods = 162, VillageToDock = 163;
    private const int WoodsToVillage = 167, Thorns = 176, Grave = 177;
    private const int TentsToHetchel = 199, TentsToVillage = 200, TentsToPath = 201, TentsToDark = 203;
    private const int HetchelOut = 204, DyePot = 205, WhiteWool = 207, Flask = 214;
    private const int PathToTents = 251, PathToSanctuary = 252, SanctuaryToPath = 253, SanctuaryDoor = 254;
    private const int SanctuaryOut = 267, Egg = 270, Distaff = 271;
    private const int DockWater = 295, Clam = 296;
    private const int WaterspoutOut = 323, Waterspout = 325;
    private const int WaterspoutToDock = 324;
    private const int WheelOut = 807, Wheel = 810, Straw = 809;
    private const int Darkness = 822, GravestoneOut = 825;

    // the shore (15), Crystalgard (16), the tower (18), the top of the tower (20), the woods (23),
    // the pasture (24), the shepherds' house (25), the dragon's lair (28), the caves (30, 32, 65),
    // the pool (31), the steps (33)
    private const int ShoreToWoods = 338, ShoreToCrystalgard = 339;
    private const int CrystalgardToTower = 346, CrystalgardToWoods = 347, Workers = 350;
    private const int TowerOut = 367, TowerBell = 369, Sphere = 370, Crystal = 371, Conversation = 372;
    private const int Scythe = 386, BellDownToSphere = 388, BellDownToCrystal = 389;
    private const int WoodsToShore = 418, ShepherdsWest = 419, Shepherd = 422;
    private const int PastureToHouse = 430, SleepySheep = 432, Flock = 433, HouseOut = 446, Lamb = 447;
    private const int LairToCaves = 465, Dragon = 466, Gold = 470;
    private const int CaveToSteps = 506, CaveLedge = 508, CaveEast = 509, EastCaveBack = 538;
    private const int PoolOut = 521, Pool = 523, PoolSphere = 525;
    private const int StepsUp = 543, Steps = 544, CaveDarkness = 828;

    // the halls of the forge (34), its gate (35), the forge (36, 37), Stoke's room (38), the sword
    // (41), the Bishop's castle (43, 44, 45), the void (46), the Shore of Wonder (47), the shepherds
    // (26) and Crystalgard (49) through the void, the way back (50), the sanctuary (9), the rift
    // (51, 66)
    private const int HallsToGate = 558, HallsToVoid = 559, Boy = 561, DeadRusty = 586;
    private const int ForgeGate = 589, OpenGate = 596, ForgeEast = 599;
    private const int ToTheSword = 603, Woodbin = 604, CellOut = 609, SleepyStraw = 610, DistaffUnderDoor = 611, CellDoor = 617;
    private const int TalkAgain = 631, Talk = 632, QuietSword = 633;
    private const int CastleOut = 639, CastleSphere = 640, Cage = 643, Orb = 650, Mandible = 653, SummoningOut = 655, DroppedDistaff = 656;
    private const int VoidWest = 681, HoleToCrystalgard = 682, HoleToShepherds = 683, HoleToForge = 684, HoleToCastle = 685;
    private const int ShepherdsHoleOut = 449, DeadShepherds = 464;
    private const int WonderWest = 695, Cygna = 697, GoodmoldOut = 704, Goodmold = 707, BackToTheIsland = 721;
    private const int Loom = 272, Hetchel = 273, Feather = 274, Rift = 741, Myself = 833;

    /// <summary>2 once the dragon has flown from its lair.</summary>
    private const int DragonGone = 202;

    /// <summary>
    /// The whole game at the Standard and Expert levels: the same route, section by section, each
    /// from the save the one before made. At Expert the distaff has no staff or notes, but the game
    /// still teaches and takes the same drafts, and the level stays in its variable to the end.
    /// No section leaves an instruction unhandled.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Plays_to_the_end(int level)
    {
        // every world the sections play, for what they leave unhandled
        var worlds = new List<GameWorld>();
        Walkthrough.Watch = worlds.Add;
        Played? played = null;
        try
        {
            foreach (var section in Sections)
            {
                played = Play(played, section, level);
                if (played is null)
                {
                    return;
                }
            }
        }
        finally
        {
            Walkthrough.Watch = null;
        }

        Assert.All(worlds, w => Assert.Empty(w.Unhandled));

        var said = Said(played!);
        Assert.Equal("The End", said[^1]);
        Assert.Equal(8, played!.World.Engine.Variables[Known]);
        Assert.True(Learned(played, Unmaking));
        Assert.Equal(level, played.World.Engine.Variables[played.World.Version == 4 ? 173 : 123]);
    }

    /// <summary>The whole game at Practice, recorded as a player would see and hear it, when DISTAFF_RECORD_VIDEO names a folder for the video.</summary>
    [Fact]
    public void Records_the_whole_game() => Walkthrough.Record("loom-ega", () =>
    {
        Played? played = null;
        foreach (var section in Sections)
        {
            played = Play(played, section);
            if (played is null)
            {
                return;
            }
        }
    });

    private static readonly Action<Walkthrough>[] Sections = [TheElders, TheIsland, LeavingTheIsland, Crystalgard, TheDragon, TheCaves, TheForge, TheBishop, TheVoid, TheLoom];

    /// <summary>Plays a section from where the one before it saved, or from boot, and saves at its end.</summary>
    private static Played? Play(Played? before, Action<Walkthrough> section, int level = 0)
    {
        if (GameFolders.Find(GameFolders.Ega) is not { } path)
        {
            return null;
        }

        var world = Walkthrough.NewWorld(path);
        var w = new Walkthrough(world) { Level = level };
        if (before is null)
        {
            world.Boot();
            Walkthrough.Recording?.Record(world);
        }
        else
        {
            world.Load(before.Save);
            Walkthrough.Recording?.Record(world);
            w.Ready();
        }

        int first = world.MessageLog.Count;
        section(w);
        return new Played(world, Walkthrough.Export("loom-ega", section, world.Save(section.Method.Name, new DateTime(2026, 10, 27)), level), first);
    }

    /// <summary>What was said in the section.</summary>
    private static List<string> Said(Played played) => [.. played.World.MessageLog.Skip(played.FirstMessage)];

    private static bool Learned(Played played, int draft) => (played.World.Engine.Variables[50 + 2 * draft] & 0x2000) != 0;

    private static bool Used(Played played, int draft) => (played.World.Engine.Variables[50 + 2 * draft] & 0x4000) != 0;
}
