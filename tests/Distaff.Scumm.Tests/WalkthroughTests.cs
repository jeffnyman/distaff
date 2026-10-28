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
        // the proficiency screen prints on, so it is waited out; PRACTICE, then Escape skips the opening
        w.Wait(60).ClickAt(160, 92).Wait(100).Key(Key.Escape, (char)27).Ready();
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

    /// <summary>Plays a section from where the one before it saved, or from boot, and saves at its end.</summary>
    private static Played? Play(Played? before, Action<Walkthrough> section)
    {
        if (GameFolders.Find(GameFolders.Ega) is not { } path)
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
        return new Played(world, Walkthrough.Export("loom-ega", section, world.Save(section.Method.Name, new DateTime(2026, 10, 27))), first);
    }

    /// <summary>What was said in the section.</summary>
    private static List<string> Said(Played played) => [.. played.World.MessageLog.Skip(played.FirstMessage)];

    private static bool Learned(Played played, int draft) => (played.World.Engine.Variables[50 + 2 * draft] & 0x2000) != 0;

    private static bool Used(Played played, int draft) => (played.World.Engine.Variables[50 + 2 * draft] & 0x4000) != 0;
}
