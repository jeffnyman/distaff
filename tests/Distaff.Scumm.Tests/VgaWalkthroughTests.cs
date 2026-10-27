using Distaff.Scumm.Game;
using Distaff.Scumm.Input;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>
/// The whole VGA CD release, played a section at a time as a player would, as
/// <see cref="WalkthroughTests"/> plays the EGA's. Each section starts from the save the section
/// before it ended with, loaded into a new world, so every save on the way is played on from. The
/// CD speaks its own lines, and moves the shepherds' scene through the void to a room of its own.
/// Tests against the game data pass without checking when it is absent.
/// </summary>
public class VgaWalkthroughTests
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
        Assert.Contains("The last leaf of autumn.", said);
        Assert.Contains("It's heavier than it looks.", said);
        Assert.Contains("There's my boy!", said);
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
        Assert.Contains("Grass green.  I hate that color!", said);
        Assert.Contains("The color changed!", said);
        Assert.Contains("All the holes are full now.", said);
        Assert.Contains("Destiny shall draw the Lightning down from Heaven,", said);
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
        Assert.Contains("Is it over?", said);
        Assert.Contains("I think that's close enough.", said);
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
        Assert.Contains("Come on, then, Wizard, let's see some magic. OR ELSE.", said);
        Assert.Contains("What's going on up there?", said);
        Assert.Contains("EIGHT hours, Master Crucible. See to it.", said);
        Assert.Contains("That scythe is even sharper than a Weaver's spindle.", said);
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
        Assert.Contains("My name is Fleece, First Chosen of the Guild of Shepherds.", said);
        Assert.Contains("I wish I were better with them.", said);
        Assert.Contains("I thought you looked a bit scrawny. Why, you'd hardly make a decent kindling!", said);
        Assert.Contains("Incinerate you? My! Aren't you the foxy one!", said);
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
        Assert.Contains("My own reflection.", said);
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
        Assert.Contains("Rusty Nailbender.", said);
        Assert.Contains("Hello, young Nailbender!", said);
        Assert.Contains("I'm done dealing with the likes of you, Nailbender.", said);
        Assert.Contains("What a mess.  I can't do anything without my distaff.", said);
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
        Assert.Contains("A witch's curse has twisted the final blade!", said);
        Assert.Contains("I am Bishop Mandible, Transultimate Apostle of the Antisecular Conclave of Clerics.", said);
        Assert.Contains("Keep away from that!", said);
        Assert.Contains("Well, he can't say he wasn't warned.", said);
        Assert.Contains("I am Choas.", said);
        Assert.Contains("You've torn the Pattern completely open!", said);
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
        Assert.Contains("Hail and farewell.", said);
        Assert.Contains("Above all else ... clarity.", said);
        Assert.Contains("How I've longed to know you, and you to know me, my son.", said);
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
        Assert.Contains("Thank goodness! Now, Bobbin, quickly: the threads you need are", said);
        Assert.Contains("Hetchel's black feather! She left one behind!", said);
        Assert.Contains("The Loom is unmade!", said);
        Assert.Contains("Come, Loom Child. It is time for us to begin our destinies anew.", said);
        Assert.Equal("The End", said[^1]);
        Assert.Equal(8, played.World.Engine.Variables[Known]);
        Assert.All([Silence, Shaping], d => Assert.True(Used(played, d)));
        Assert.True(Learned(played, Unmaking));
    }

    /// <summary>The drafts as the game numbers them in its table.</summary>
    private const int Opening = 0, StrawIntoGold = 1, Dyeing = 2, NightVision = 3, Twisting = 4, Sleep = 5, Emptying = 6;
    private const int Invisibility = 7, Terror = 8, Sharpening = 9, Reflection = 10, Healing = 11;
    private const int Silence = 12, Shaping = 13, Unmaking = 14, Transcendence = 15;

    /// <summary>The class the scripts set on what cannot be clicked.</summary>
    private const int Untouchable = 32;

    /// <summary>The notes the distaff plays, 3 for c d e (variable 122 on the EGA).</summary>
    private const int Known = 172;

    private static void TheElders(Walkthrough w)
    {
        // the proficiency screen prints on, so it is waited out; PRACTICE, then Escape skips the opening
        w.Wait(300).ClickAt(160, 95).Wait(100).Key(Key.Escape, (char)27).Ready(8000);
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
        w.Use(96).Use(97).Use(98).Use(99);
        w.Use(Thorns).Ready(20000);
        w.Use(99).Use(96);
        w.Use(Grave).Ready(20000);

        // exits wait for the click to end, so they are clicked once
        w.Use(GravestoneOut).InRoom(4).Ready().Click(WoodsToVillage).InRoom(3).Ready().Use(VillageToTents).InRoom(5).Ready();
        w.Use(TentsToDark).InRoom(63).Ready();
        w.Click(Darkness).Spin(NightVision).Ready(20000);
        w.Use(Wheel).Ready(20000);
        w.Click(Straw).Spin(StrawIntoGold).Ready(20000);
    }

    private static void LeavingTheIsland(Walkthrough w)
    {
        w.Click(WheelOut).InRoom(5).Ready().Click(TentsToVillage).InRoom(3).Ready().Click(VillageToCliff).InRoom(2).Ready();

        // the path down the cliff takes Bobbin over in a cutscene, and the sky is clicked again from
        // there; on the VGA CD it takes a third click to point at
        w.Click(Sky).Click(Sky).Click(Sky).Spin(Opening).Ready(20000);
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

        // the walkway along the front leads west: beside the tower a click lands nearest the
        // glassmakers' own path (box 18), which joins no other, and Bobbin would not move
        w.ClickAt(2, 133 + w.World.ViewTop).Ready(20000).ClickAt(2, 133 + w.World.ViewTop).Ready(20000);
        w.Exit(CrystalgardToWoods, 23);

        // the path down from Crystalgard ends in a cutscene halfway
        w.WalkTo(110, 120).Ready(20000).WalkTo(110, 120).Ready(20000);
        w.Click(Shepherd).Spin(Terror).Ready(20000);
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

        // the tunnels: 440 to the east ledge, the east cave and back, 429 to the ledge over the pool
        w.Click(440).Ready(20000);
        w.Exit(CaveEast, 32, 20000);
        w.Exit(EastCaveBack, 30, 20000);
        w.Click(429).Ready(20000);
        w.Exit(CaveLedge, 31, 20000);
        w.Use(Pool).Ready(20000);
        w.Click(Pool).Spin(Emptying).Ready(20000);
        w.Use(PoolSphere).Ready(20000).Use(PoolSphere).Ready(20000).Use(PoolSphere).Ready(20000);
        w.Exit(PoolOut, 30, 20000);
        w.Click(430).Ready(20000);
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
        w.Use(HoleToShepherds).InRoom(93).Ready(20000);
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

    // the objects, by room, numbered apart from the EGA's: 75 lower up to the woods' shepherds, 74
    // from the pasture on, 68 in the rooms from 62. The cliff (2), the village (3), the woods (4),
    // the tents (5), Hetchel's tent (6), the path (7), outside the sanctuary (8), the sanctuary (9),
    // the dock (10), the waterspout (13), the wheel's tent (62), the dark tent (63), the gravestone (64)
    private const int CliffToVillage = 78, Leaf = 79, Sky = 81;
    private const int VillageToCliff = 85, VillageToTents = 86, VillageToWoods = 87, VillageToDock = 88;
    private const int WoodsToVillage = 92, Thorns = 101, Grave = 102;
    private const int TentsToHetchel = 124, TentsToVillage = 125, TentsToPath = 126, TentsToDark = 128;
    private const int HetchelOut = 129, DyePot = 130, WhiteWool = 132, Flask = 139;
    private const int PathToTents = 176, PathToSanctuary = 177, SanctuaryToPath = 178, SanctuaryDoor = 179;
    private const int SanctuaryOut = 192, Egg = 195, Distaff = 196;
    private const int DockWater = 220, Clam = 221;
    private const int WaterspoutOut = 248, Waterspout = 250;
    private const int WheelOut = 739, Wheel = 742, Straw = 741;
    private const int Darkness = 754, GravestoneOut = 757;

    // the shore (15), Crystalgard (16), the tower (18), the top of the tower (20), the woods (23),
    // the pasture (24), the shepherds' house (25), the dragon's lair (28), the caves (30, 32, 65),
    // the pool (31), the steps (33)
    private const int ShoreToWoods = 263, ShoreToCrystalgard = 264;
    private const int CrystalgardToTower = 271, CrystalgardToWoods = 272, Workers = 275;
    private const int TowerOut = 292, TowerBell = 294, Sphere = 295, Crystal = 296, Conversation = 297;
    private const int Scythe = 311, BellDownToSphere = 313, BellDownToCrystal = 314;
    private const int WoodsToShore = 343, ShepherdsWest = 344, Shepherd = 347;
    private const int PastureToHouse = 356, SleepySheep = 358, Flock = 359, HouseOut = 372, Lamb = 373;
    private const int LairToCaves = 391, Dragon = 392, Gold = 396;
    private const int CaveToSteps = 432, CaveLedge = 434, CaveEast = 435, EastCaveBack = 464;
    private const int PoolOut = 447, Pool = 449, PoolSphere = 451;
    private const int StepsUp = 469, Steps = 470, CaveDarkness = 760;

    // the halls of the forge (34), its gate (35), the forge (36, 37), Stoke's room (38), the sword
    // (41), the Bishop's castle (43, 44, 45), the void (46), the Shore of Wonder (47), the shepherds
    // (93, the CD's own room; 26 on the EGA) and Crystalgard (49) through the void, the way back
    // (50), the sanctuary (9), the rift (51, 66)
    private const int HallsToGate = 484, HallsToVoid = 485, Boy = 487, DeadRusty = 512;
    private const int ForgeGate = 515, OpenGate = 522, ForgeEast = 525;
    private const int ToTheSword = 529, Woodbin = 530, CellOut = 535, SleepyStraw = 536, DistaffUnderDoor = 537, CellDoor = 543;
    private const int TalkAgain = 557, Talk = 558, QuietSword = 559;
    private const int CastleOut = 565, CastleSphere = 566, Cage = 569, Orb = 576, Mandible = 579, SummoningOut = 581, DroppedDistaff = 582;
    private const int VoidWest = 607, HoleToCrystalgard = 608, HoleToShepherds = 609, HoleToForge = 610, HoleToCastle = 611;
    private const int ShepherdsHoleOut = 899, DeadShepherds = 900;
    private const int WonderWest = 621, Cygna = 623, GoodmoldOut = 630, Goodmold = 633, BackToTheIsland = 647;
    private const int Loom = 197, Hetchel = 198, Feather = 199, Rift = 667, Myself = 765;

    /// <summary>2 once the dragon has flown from its lair (variable 202 on the EGA: not 50 apart, as most are).</summary>
    private const int DragonGone = 251;

    /// <summary>Plays a section from where the one before it saved, or from boot, and saves at its end.</summary>
    private static Played? Play(Played? before, Action<Walkthrough> section)
    {
        if (GameFolders.Find(GameFolders.Vga) is not { } path)
        {
            return null;
        }

        var world = new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path)));
        var w = new Walkthrough(world);
        if (before is null)
        {
            world.Boot();
        }
        else
        {
            world.Load(before.Save);
            w.Ready();
        }

        int first = world.MessageLog.Count;
        section(w);
        return new Played(world, world.Save(section.Method.Name, new DateTime(2026, 10, 27)), first);
    }

    /// <summary>What was said in the section.</summary>
    private static List<string> Said(Played played) => [.. played.World.MessageLog.Skip(played.FirstMessage)];

    private static bool Learned(Played played, int draft) => (played.World.Engine.Variables[100 + 2 * draft] & 0x2000) != 0;

    private static bool Used(Played played, int draft) => (played.World.Engine.Variables[100 + 2 * draft] & 0x4000) != 0;
}
