// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using SvEntityKind = BlocksBeyondTheStars.GameServer.CombatEntityKind;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;
using SvSession = BlocksBeyondTheStars.GameServer.PlayerSession;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The small server follow-ups of the bio lab fix round (#2214, #2216, #2219, #2220, #2221), through the real server:
/// VEGA's planting hint is kept for a seedling that found no place, the ration dispenser neither stores nor dispenses a
/// preparation, <c>/give</c> hands out no blank lab item, a clone tank started inside the "clone ready" pulse keeps its
/// light on, a self-built hull is measured by its blocks and not by the holes a removed block left, <c>/setweather</c>
/// tells the players of that world only, the lab is not used from inside the ship, a void world reads the cabin's
/// temperature, and a clone lost to fire or to a sentry post leaves its tank's list at once.
/// </summary>
public sealed class BioFixFollowUpTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>High up in the air above the origin: empty cells, everything a test places is in reach.</summary>
    private static readonly Vector3f HighUp = new(0, 200, 0);

    private static readonly Vector3i Tank = new(1, 200, 0);

    /// <summary>The once-flag of VEGA's planting hint on a player, and the line it speaks.</summary>
    private const string PlantHintFlag = "vega:hint:plant_refused";
    private const string PlantHintLine = "vega.hint.plant_refused";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_biofollow_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort
        }
    }

    // ---------------- Harness ----------------

    private SvGameServer NewServer(string world, string planet = "jungle", Action<ServerConfig>? tune = null, IServerTransport? transport = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig { WorldName = world, Seed = 77, StartPlanet = planet, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        tune?.Invoke(config);
        var server = new SvGameServer(config, Content, transport ?? new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>The world option the cheat commands need.</summary>
    private static void WithCheats(ServerConfig config)
    {
        config.PlaceSettlements = false;
        config.Rules.AdminCheats = true;
        config.Rules.AllowCheatsInSurvival = true;
    }

    /// <summary>A player on foot at <paramref name="at"/> with an empty backpack, an empty ration dispenser and the given
    /// items — no starter kit, so a test counts exactly what it hands out.</summary>
    private static SvSession Player(SvGameServer server, string name, Vector3f at, params string[] items)
    {
        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = false; // no physical ship in these worlds: the flag stays as a test sets it
        p.State.Position = at;
        p.State.SuitEnergy = 100f;
        Empty(p.State.Inventory);
        Empty(p.State.RationStore);
        foreach (string key in items)
        {
            Assert.Equal(0, p.State.Inventory.Add(key, 1, 1));
        }

        return p;
    }

    private static void Empty(Inventory inventory)
    {
        for (int i = 0; i < inventory.SlotCount; i++)
        {
            inventory.SetSlot(i, null);
        }
    }

    private static BlockId Block(string key) => Content.GetBlock(key)!.NumericId;

    private static void Ticks(SvGameServer server, double seconds, double step = 0.1)
    {
        for (double t = 0; t < seconds; t += step)
        {
            server.TickForTest(step);
        }
    }

    private static IEnumerable<T> SentTo<T>(NpcLifeWorld.RecordingTransport transport, SvSession who)
        => transport.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<T>();

    private static List<string> Rejections(NpcLifeWorld.RecordingTransport transport, SvSession who)
        => SentTo<ActionRejected>(transport, who).Select(r => r.Reason).ToList();

    private static List<string> PlantHints(NpcLifeWorld.RecordingTransport transport, SvSession who)
        => SentTo<ShipAiLine>(transport, who).Select(l => l.LineKey).Where(k => k == PlantHintLine).ToList();

    private static string Prep(BioEffect effect, int level, int seconds = 120, BioForm form = BioForm.Injector)
        => BioItems.PreparationItem(new Compound { Form = form, Effect = effect, Level = level, DurationSeconds = seconds });

    /// <summary>Two peaceful land or air species of the test world — animals that need no water beside the tank.</summary>
    private static List<CreatureSpecies> LandSpecies(SvGameServer server)
    {
        var list = server.SpeciesRoster
            .Where(s => !s.Hostile && !s.IsGiant && s.Habitat is CreatureHabitat.Land or CreatureHabitat.Air)
            .Take(2).ToList();
        Assert.Equal(2, list.Count);
        return list;
    }

    // ---------------- 1. VEGA's planting hint ----------------

    /// <summary>The hint explains where a seedling grows. A seedling that carries no species, or one of a species that
    /// is no single plant, would grow nowhere: it gets its own line, and the hint — said only once — is kept for the
    /// seedling it can help.</summary>
    [Fact]
    public void VegasPlantingHint_IsNotSpentOnASeedlingThatIsNoPlant()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("hint_blank", transport: t);
        var p = Player(server, "Gardener", HighUp);
        server.World.SetBlock(new Vector3i(1, 199, 2), Block("dirt")); // clean soil under breathable air: the place is fine
        string unknown = ItemKey.WithSeed(BioItems.Seedling, 0xDEADBEEF);
        uint animal = server.RegisterForeignCreatureForTest(LandSpecies(server)[0], "elsewhere");
        string noPlant = ItemKey.WithSeed(BioItems.Seedling, animal);
        foreach (string key in new[] { BioItems.Seedling, unknown, noPlant })
        {
            Assert.Equal(0, p.State.Inventory.Add(key, 1, 64));
            server.PlaceBlock("Gardener", 1, 200, 2, key);
        }

        Assert.Equal(new[] { "@srv.bio.seedling_empty", "@srv.bio.seedling_empty", "@srv.bio.not_plantable" }, Rejections(t, p));
        Assert.DoesNotContain(PlantHintFlag, p.State.Milestones);
        Assert.Empty(PlantHints(t, p));

        // The hint is still to come: the first real seedling that finds no soil gets it.
        uint fern = server.GiveFloraSampleForTest(p, "flora_fern", 1);
        string seedling = ItemKey.WithSeed(BioItems.Seedling, fern);
        Assert.Equal(0, p.State.Inventory.Add(seedling, 1, 64));
        server.World.SetBlock(new Vector3i(0, 199, 2), Block("steel_floor"));
        server.PlaceBlock("Gardener", 0, 200, 2, seedling);

        Assert.Equal("@srv.bio.plant_soil", Rejections(t, p).Last());
        Assert.Contains(PlantHintFlag, p.State.Milestones);
        Assert.Single(PlantHints(t, p));
    }

    [Theory]
    [InlineData("jungle", "steel_floor", "@srv.bio.plant_soil")] // air to breathe, but no soil
    [InlineData("crystal", "dirt", "@srv.bio.plant_air")]        // soil, on an airless world
    public void ASeedlingThatFindsNoPlace_GetsVegasHint_Once(string planet, string ground, string refusal)
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("hint_" + planet, planet, transport: t);
        var p = Player(server, "Gardener", HighUp);
        uint fern = server.GiveFloraSampleForTest(p, "flora_fern", 1);
        string seedling = ItemKey.WithSeed(BioItems.Seedling, fern);
        Assert.Equal(0, p.State.Inventory.Add(seedling, 2, 64));
        server.World.SetBlock(new Vector3i(1, 199, 2), Block(ground));

        server.PlaceBlock("Gardener", 1, 200, 2, seedling);
        server.PlaceBlock("Gardener", 1, 200, 2, seedling);

        Assert.Equal(new[] { refusal, refusal }, Rejections(t, p));
        Assert.Equal(2, p.State.Inventory.CountOf(seedling));
        Assert.Contains(PlantHintFlag, p.State.Milestones);
        Assert.Single(PlantHints(t, p)); // said once, however often the place is refused
    }

    [Fact]
    public void ASeedlingRefusedByTheWorldsCap_GetsVegasHint()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("hint_cap", transport: t);
        var p = Player(server, "Gardener", HighUp);
        uint fern = server.GiveFloraSampleForTest(p, "flora_fern", 1);
        string seedling = ItemKey.WithSeed(BioItems.Seedling, fern);
        Assert.Equal(0, p.State.Inventory.Add(seedling, BioRules.MaxBredPlantsPerWorld + 1, 1024));

        // A field of seedlings, one per cell, until the world holds all it can — and one more.
        const int Row = 16;
        for (int i = 0; i <= BioRules.MaxBredPlantsPerWorld; i++)
        {
            int x = i % Row, z = 2 + (i / Row);
            server.World.SetBlock(new Vector3i(x, 199, z), Block("dirt"));
            p.State.Position = new Vector3f(x + 0.5f, 201f, z + 0.5f); // right above the cell: always in reach
            server.PlaceBlock("Gardener", x, 200, z, seedling);
        }

        Assert.Equal(1, p.State.Inventory.CountOf(seedling)); // every seedling but the last one grew
        Assert.Equal(new[] { "@srv.bio.plant_cap" }, Rejections(t, p));
        Assert.Contains(PlantHintFlag, p.State.Milestones);
        Assert.Single(PlantHints(t, p));
    }

    // ---------------- 2. The ration dispenser ----------------

    /// <summary>A bar sates hunger, so the dispenser's "food only" rule let it in — and would have dispensed the hunger
    /// without the effect. A preparation, real or blank, is not plain food.</summary>
    [Fact]
    public void TheRationDispenser_TakesNoPreparation_ARealBarOrABlankOne()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("ration_load", transport: t);
        string bar = Prep(BioEffect.Jump, 5, form: BioForm.Bar);
        var p = Player(server, "Spacer", HighUp, bar, "prep_bar", "berries");
        Assert.True(Content.GetItem(bar)!.ConsumeHunger > 0f, "the test needs a preparation that feeds");

        server.LoadRation("Spacer", bar, 1);
        server.LoadRation("Spacer", "prep_bar", 1);

        Assert.Equal(new[] { "@srv.ration.food_only", "@srv.ration.food_only" }, Rejections(t, p));
        Assert.Equal(1, p.State.Inventory.CountOf(bar));
        Assert.Equal(1, p.State.Inventory.CountOf("prep_bar"));
        Assert.DoesNotContain(p.State.RationStore.Slots, s => s is { IsEmpty: false });

        // Plain food goes in as before.
        server.LoadRation("Spacer", "berries", 1);
        Assert.Equal(1, p.State.RationStore.CountOf("berries"));
        Assert.Equal(0, p.State.Inventory.CountOf("berries"));
    }

    [Fact]
    public void TheSuit_NeverAutoEatsAPreparation_ThatLiesInTheDispenser()
    {
        var server = NewServer("ration_auto");
        string bar = Prep(BioEffect.Jump, 5, form: BioForm.Bar);
        var p = Player(server, "Spacer", HighUp);

        // A dispenser an older build let a bar into, in front of the berries.
        p.State.RationStore.SetSlot(0, new ItemStack(bar, 2));
        p.State.RationStore.SetSlot(1, new ItemStack("berries", 2));
        p.State.Hunger = 10f; // below the auto-feed threshold

        server.TickForTest(1.0);

        Assert.Equal(2, p.State.RationStore.CountOf(bar));       // passed over: neither eaten nor lost
        Assert.Equal(1, p.State.RationStore.CountOf("berries")); // the food behind it was dispensed
        Assert.True(p.State.Hunger > 20f, "the berries feed");
        Assert.Empty(p.State.Effects);

        // With nothing but preparations in it the suit goes hungry rather than hand one out.
        p.State.RationStore.SetSlot(1, null);
        p.State.Hunger = 10f;
        server.TickForTest(1.0);

        Assert.Equal(2, p.State.RationStore.CountOf(bar));
        Assert.True(p.State.Hunger <= 10f);
        Assert.Empty(p.State.Effects);

        // Taken deliberately, the same bar feeds and starts its effect.
        Assert.Equal(0, p.State.Inventory.Add(bar, 1, 1));
        server.ConsumeItem("Spacer", bar);
        Assert.Equal(BioEffect.Jump, Assert.Single(p.State.Effects).Effect);
        Assert.True(p.State.Hunger > 10f);
    }

    // ---------------- 3. /give ----------------

    private static SvSession Admin(SvGameServer server, string name = "Admin")
    {
        var s = Player(server, name, new Vector3f(300f, 120f, 0f));
        s.State.Role = PlayerRole.WorldAdmin;
        return s;
    }

    private static List<(int Conn, object Msg)> Run(SvGameServer server, NpcLifeWorld.RecordingTransport t, SvSession who,
        string command, string? arg, int count = 0, string? target = null)
    {
        int before = t.Sent.Count;
        server.HandleForTest(who, new AdminCommandIntent { Command = command, StringArg = arg, IntArg = count, TargetPlayer = target });
        return t.Sent.Skip(before).ToList();
    }

    private static List<string> LinesTo(List<(int Conn, object Msg)> sent, SvSession who)
        => sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ServerMessage>().Select(m => m.Text).ToList();

    private static bool HoldsAnything(SvSession p)
        => p.State.Inventory.Slots.Concat(p.State.SampleCase.Slots).Any(s => s is { IsEmpty: false });

    [Fact]
    public void Give_HandsOutNoBlankLabItem_ToTheAdminOrToAnyoneElse()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("give_blank", "rocky", WithCheats, t);
        var admin = Admin(server);
        var guest = Player(server, "Guest", new Vector3f(300f, 120f, 0f));
        string[] blanks =
        {
            BioItems.Sample, BioItems.MineralSample, BioItems.Seedling,
            "prep_injector", "prep_gel", "prep_bar", "prep_capsule", "prep_coating",
            "bio_sample#x00000000",      // a seed that is none
            "prep_injector#x6350008000", // a payload that names an effect no version knows
        };

        foreach (string key in blanks)
        {
            Assert.True(BioItems.NeedsPayload(key), key);
            foreach (string? target in new[] { null, "Guest" })
            {
                var sent = Run(server, t, admin, "give_item", key, count: 3, target: target);

                Assert.Equal("@srv.catalog.needs_content", Assert.Single(sent.Select(s => s.Msg).OfType<ActionRejected>()).Reason);
                Assert.All(sent, s => Assert.Equal(admin.ConnectionId, s.Conn)); // the admin is told, nobody else
                Assert.Empty(LinesTo(sent, admin));                              // and no "gave" line
            }
        }

        Assert.False(HoldsAnything(admin));
        Assert.False(HoldsAnything(guest));
    }

    [Fact]
    public void Give_AnItemThatCarriesItsContent_IsGivenAsBefore()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("give_real", "jungle", WithCheats, t);
        var admin = Admin(server);
        admin.State.Position = HighUp;

        // A preparation whose key is typed by hand: Speed, full strength, ten minutes.
        const string Injector = "prep_injector#x01f0028000";
        var compound = BioItems.CompoundOf(Injector);
        Assert.NotNull(compound);
        var sent = Run(server, t, admin, "give_item", Injector, count: 2);
        Assert.Empty(sent.Select(s => s.Msg).OfType<ActionRejected>());
        Assert.Single(LinesTo(sent, admin));
        Assert.Equal(2, admin.State.Inventory.CountOf(Injector));
        server.ConsumeItem("Admin", Injector);
        Assert.Equal(compound!.Effect, Assert.Single(admin.State.Effects).Effect);

        // A seedling of a species this save knows: given, and it grows.
        uint fern = server.GiveFloraSampleForTest(admin, "flora_fern", 1);
        string seedling = ItemKey.WithSeed(BioItems.Seedling, fern);
        sent = Run(server, t, admin, "give_item", seedling, count: 1);
        Assert.Empty(sent.Select(s => s.Msg).OfType<ActionRejected>());
        Assert.Equal(1, admin.State.Inventory.CountOf(seedling));
        server.World.SetBlock(new Vector3i(1, 199, 2), Block("dirt"));
        server.PlaceBlock("Admin", 1, 200, 2, seedling);
        Assert.Equal(fern, server.BredSeedAtForTest(new Vector3i(1, 200, 2)));

        // And the lab block itself, which never needed a payload.
        Run(server, t, admin, "give_item", BioItems.Lab, count: 1);
        Assert.Equal(1, admin.State.Inventory.CountOf(BioItems.Lab));
    }

    // ---------------- 4. The clone tank's light ----------------

    /// <summary>The player "Keeper" with matter dust and a clone tank standing at <see cref="Tank"/>.</summary>
    private static SvSession TankOwner(SvGameServer server)
    {
        var p = Player(server, "Keeper", HighUp, "clone_tank");
        Assert.Equal(0, p.State.Inventory.Add("matter_dust", 64, 1024));
        server.PlaceBlock("Keeper", Tank.X, Tank.Y, Tank.Z, "clone_tank");
        return p;
    }

    private static string Choice(uint seed) => SvGameServer.TankChoiceForTest(seed);

    private static List<CombatEntity> Clones(SvGameServer server) => server.Creatures.Where(c => c.CloneOf.Length > 0).ToList();

    private static void Configure(SvGameServer server, SvSession p, string config)
        => server.SetCrystalDeviceForTest(p, Tank, action: 2, mode: 0, config: config);

    private static void Start(SvGameServer server, SvSession p)
        => server.SetCrystalDeviceForTest(p, Tank, action: 1);

    private static string? ConfigValue(SvGameServer server, string key)
        => (server.CrystalDeviceConfig(Tank) ?? string.Empty).Split(';')
            .Where(part => part.StartsWith(key + "=", StringComparison.Ordinal))
            .Select(part => part.Substring(key.Length + 1))
            .FirstOrDefault();

    /// <summary>Grows one clone of a sample species in the tank and returns it.</summary>
    private static CombatEntity GrowClone(SvGameServer server, SvSession p, uint seed)
    {
        var before = Clones(server).Select(c => c.Id).ToHashSet();
        Configure(server, p, "sp=" + Choice(seed));
        Start(server, p);
        Assert.True(server.CrystalDeviceOutput(Tank), "the tank did not start");
        Ticks(server, CrystalNetRules.CloneGrowSeconds + 1.0, 0.5);
        return Assert.Single(Clones(server), c => !before.Contains(c.Id));
    }

    /// <summary>The finished job pulses the tank's port for half a second ("clone ready"). A start inside that pulse
    /// found the light already on and left the pulse armed — so the light went out when the pulse fell back and stayed
    /// out for the whole new job.</summary>
    [Fact]
    public void ATankStartedInsideTheReadyPulse_KeepsItsLightOn_ForTheWholeNewJob()
    {
        var server = NewServer("pulse");
        var p = TankOwner(server);
        uint seed = server.GiveCreatureSampleForTest(p, LandSpecies(server)[0], 4);
        Configure(server, p, "sp=" + Choice(seed));
        Start(server, p);
        Assert.Equal("1", ConfigValue(server, "growing"));

        // The first job, tick by tick, until the very beat it finishes on.
        for (int i = 0; i < 700 && ConfigValue(server, "growing") == "1"; i++)
        {
            server.TickForTest(0.1);
        }

        Assert.Equal("0", ConfigValue(server, "growing"));
        Assert.Single(Clones(server));
        Assert.True(server.CrystalDeviceOutput(Tank), "the finished job pulses the port");

        // Start again right now — inside the pulse.
        Start(server, p);
        Assert.Equal("1", ConfigValue(server, "growing"));

        // Long after the pulse would have fallen back, and through the whole job: the light is on.
        Ticks(server, CrystalNetRules.PulseSeconds * 4);
        Assert.True(server.CrystalDeviceOutput(Tank), "the light went out with the pulse of the job before");
        Ticks(server, CrystalNetRules.CloneGrowSeconds / 2);
        Assert.Equal("1", ConfigValue(server, "growing"));
        Assert.True(server.CrystalDeviceOutput(Tank));

        // The second job ends like the first: its own pulse, then the light is off.
        Ticks(server, CrystalNetRules.CloneGrowSeconds / 2);
        Assert.Equal("0", ConfigValue(server, "growing"));
        Assert.Equal(2, Clones(server).Count);
        Ticks(server, CrystalNetRules.PulseSeconds * 4);
        Assert.False(server.CrystalDeviceOutput(Tank));
    }

    // ---------------- 5. The hull of a self-built ship ----------------

    private static ushort Id(string key) => Content.GetBlock(key)!.NumericId.Value;

    /// <summary>A hull string as the save holds it: walls along the keel line, engines above them, and — what the
    /// block-id remap of a save leaves where a block was removed from the game — air entries (id 0).</summary>
    private static string Hull(int walls, int engines, int holes)
    {
        var parts = new List<string>();
        for (int i = 0; i < walls; i++)
        {
            parts.Add($"{i % 15}:0:{i / 15}:{Id("iron_wall")}");
        }

        for (int i = 0; i < engines; i++)
        {
            parts.Add($"{i}:1:0:{Id("ship_engine")}");
        }

        for (int i = 0; i < holes; i++)
        {
            parts.Add($"{i % 15}:2:{i / 15}:0");
        }

        return string.Join(";", parts);
    }

    private static int Holes(ShipState ship)
        => ship.BuiltCells.Split(';').Count(cell => cell.EndsWith(":0", StringComparison.Ordinal));

    [Fact]
    public void TheStatsOfASelfBuiltHull_CountItsBlocks_NotTheHolesARemovedBlockLeft()
    {
        var server = NewServer("hull_stats");
        var pilot = Player(server, "Pilot", HighUp);
        string holed = Hull(walls: 30, engines: 2, holes: 60);
        pilot.Ships["plain"] = new ShipState { ShipType = ShipState.CustomShipType, BuiltCells = Hull(30, 2, 0) };
        pilot.Ships["holed"] = new ShipState { ShipType = ShipState.CustomShipType, BuiltCells = holed };
        pilot.Ships["bigger"] = new ShipState { ShipType = ShipState.CustomShipType, BuiltCells = Hull(90, 2, 0) };

        var plain = server.CustomShipStatsForTest("Pilot", "plain")!.Value;
        var withHoles = server.CustomShipStatsForTest("Pilot", "holed")!.Value;
        var bigger = server.CustomShipStatsForTest("Pilot", "bigger")!.Value;

        // Sixty real blocks more are another ship: more hull, slower. Sixty holes are not.
        Assert.True(bigger.HullMax > plain.HullMax && bigger.FlightSpeed < plain.FlightSpeed && bigger.Handling < plain.Handling);
        Assert.Equal(plain, withHoles);

        // The holes stay where they are: they hold the hull's coordinate frame.
        Assert.Equal(holed, pilot.Ships["holed"].BuiltCells);
    }

    [Fact]
    public void TheSizeOfASelfBuiltHull_IsThatOfItsBlocks_AndTheHolesStayInTheSave()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("hull_size", transport: t);
        var pilot = Player(server, "Pilot", HighUp);
        pilot.State.InstantBuild = true; // the build needs no materials
        var core = new Vector3i(40, 200, 40);
        server.World.SetBlock(new Vector3i(core.X, core.Y - 1, core.Z), Block("stone")); // a keel needs ground under it
        pilot.State.Position = new Vector3f(core.X + 1.5f, core.Y, core.Z + 0.5f);       // at the helm-to-be
        server.PlaceShipCoreForTest("Pilot", core.X, core.Y, core.Z);
        var ship = pilot.Ships.Values.Single(s => s.IsCustom);

        void Build(int x, int y, int z, string item)
            => server.HandleStructureEditForTest("Pilot", new StructureEditIntent { StructureId = "shipyard:Pilot", X = x, Y = y, Z = z, ItemKey = item });

        string? Commission()
        {
            int before = t.Sent.Count;
            server.CommissionShipForTest("Pilot");
            return t.Sent.Skip(before).Where(s => s.Conn == pilot.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>().LastOrDefault()?.Reason;
        }

        // The keel, a helm, an engine and two walls: five blocks.
        Build(1, 0, 0, "ship_helm");
        Build(2, 0, 0, "ship_engine");
        Build(3, 0, 0, "iron_wall");
        Build(4, 0, 0, "iron_wall");
        Assert.Equal(5, ship.BuiltCells.Split(';').Length);

        // Twenty cells whose block is gone from the game, as the block-id remap leaves them: air entries.
        const int Gone = 20;
        ship.BuiltCells += ";" + string.Join(";", Enumerable.Range(0, Gone).Select(i => $"{i % 5}:{2 + (i / 5)}:1:0"));
        Assert.Equal("@srv.ship.too_small:20", Commission()); // five blocks and twenty holes are five blocks

        // Building on keeps the holes in the save — they are the frame the hull is anchored in.
        for (int x = 0; x < 5; x++)
        {
            for (int z = 1; z <= 3; z++)
            {
                Build(x, 0, z, "iron_wall");
            }
        }

        Assert.Equal(Gone, Holes(ship));
        Assert.Equal(20 + Gone, ship.BuiltCells.Split(';').Length);
        Assert.Equal(core, server.ConstructionBoundsForTest("Pilot")!.Value.Origin);

        // Twenty blocks now. A hole far outside them is no part of the hull's extents either: the build is not "too
        // big", it is refused for what it really lacks.
        ship.BuiltCells += ";0:0:19:0";
        Assert.Equal("@srv.ship.need_door", Commission());
    }

    // ---------------- 6. /setweather ----------------

    /// <summary>The forced weather is one world's: the players under that sky read the line, a player on another body
    /// reads nothing.</summary>
    [Fact]
    public void SetWeather_TellsThePlayersOfThatWorld_NotThoseOnAnotherBody()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("setweather_worlds", "rocky", WithCheats, t);
        var admin = Admin(server);
        var guest = Player(server, "Guest", new Vector3f(300f, 120f, 0f));
        var away = Player(server, "Away", new Vector3f(300f, 120f, 0f));
        away.CurrentLocationId = server.Galaxy.AllBodies().First(b => b.Id != admin.CurrentLocationId).Id;

        var sent = Run(server, t, admin, "set_weather", "storm");

        Assert.Equal("storm", server.WeatherSimForTest.State);
        Assert.Equal("@srv.admin.weather_set:storm", Assert.Single(LinesTo(sent, admin)));
        Assert.Equal("@srv.admin.weather_set:storm", Assert.Single(LinesTo(sent, guest)));
        Assert.DoesNotContain(sent, s => s.Conn == away.ConnectionId);
        Assert.DoesNotContain(sent, s => s.Conn == int.MinValue); // nothing server-wide
    }

    // ---------------- 7. The lab from inside the ship ----------------

    /// <summary>A species whose sample is toxic on any world: it yields a poison gland.</summary>
    private static CreatureSpecies Venomous() => new()
    {
        Id = "venom",
        Name = "Test venom",
        VoiceSeed = 4242,
        DropKind = CreatureDropKind.Poison,
        DropItem = "toxic_gland",
    };

    /// <summary>The lab's reach is a box around the player and reads through a hull. From inside the ship the lab
    /// answered — and the wash then looked for the ship's detoxifier module instead of the block beside the lab.</summary>
    [Fact]
    public void TheLab_IsNotUsedFromInsideTheShip_AndFromOutsideItsWashReadsTheDetoxifierBesideIt()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("lab_hull", tune: c => c.PlaceStarterShip = true, transport: t);
        var p = server.AddLocalPlayer("Pilot");
        Assert.True(server.HasShip);
        Empty(p.State.Inventory);
        Assert.Equal(0, p.State.Inventory.Add("carbon", 5, 1024));

        // A lab and a detoxifier standing against the hull, outside; a spot just inside the wall, and one outside.
        var (origin, size) = server.LandedShipBoundsForTest("Pilot");
        int y = origin.Y + 1, z = origin.Z + (size.Z / 2);
        var lab = new Vector3i(origin.X - 2, y, z);
        var inside = new Vector3f(origin.X + 1.5f, y, z + 0.5f);
        var outside = new Vector3f(origin.X - 3.5f, y, z + 0.5f);
        server.World.SetBlock(lab, Block(BioItems.Lab));
        server.World.SetBlock(new Vector3i(lab.X, y, z + 1), Block("detoxifier"));
        Assert.True(server.ShipInteriorContainsCellForTest(origin.X + 1, y, z));
        Assert.False(server.ShipInteriorContainsCellForTest(lab.X, y, z));
        Assert.False(server.ShipInteriorContainsCellForTest(origin.X - 4, y, z));

        uint seed = server.GiveCreatureSampleForTest(p, Venomous(), 4);
        Assert.True(server.BioProfileForTest(seed)!.Toxicity > 0, "the test needs a toxic sample");
        string sample = ItemKey.WithSeed(BioItems.Sample, seed);
        var mix = new BioLabIntent { Action = BioLabIntent.Mix, Sample = seed };

        // Inside the hull — aboard the own ship, or a visitor in a parked one, who has no aboard flag: no lab.
        p.State.Position = inside;
        foreach (bool aboard in new[] { true, false })
        {
            p.State.AboardShip = aboard;
            server.BioLabForTest(p, mix);
            server.BioLabForTest(p, new BioLabIntent { Action = BioLabIntent.Analyse, Sample = seed });
        }

        var refused = SentTo<BioLabResult>(t, p).ToList();
        Assert.Equal(4, refused.Count);
        Assert.All(refused, r => Assert.True(r is { Success: false, MessageKey: "srv.bio.need_lab" }));
        Assert.Equal(4, p.State.SampleCase.CountOf(sample));
        Assert.Equal(5, p.State.Inventory.CountOf("carbon"));
        Assert.Empty(server.BioReactionsForTest(p.State.PlayerId));
        Assert.False(server.BioAnalysedForTest(p.State.PlayerId, seed));

        // Outside, beside the lab: it answers, and the mix is washed by the detoxifier block that stands there.
        p.State.Position = outside;
        p.State.AboardShip = false;
        server.BioLabForTest(p, mix);

        var result = SentTo<BioLabResult>(t, p).Last();
        Assert.NotEqual("srv.bio.need_lab", result.MessageKey);
        Assert.True(result.Washed);
        Assert.Equal(3, p.State.SampleCase.CountOf(sample));
        Assert.Equal(4, p.State.Inventory.CountOf("carbon"));
        Assert.Equal(new[] { Synthesis.Signature(seed, BioForm.Injector, 0, string.Empty, 0, washed: true) },
            server.BioReactionsForTest(p.State.PlayerId));
    }

    /// <summary>In a world without a placed ship the aboard flag keeps its default (true) and is never updated. It
    /// must not count there, or the lab would be refused everywhere on such a server.</summary>
    [Fact]
    public void InAWorldWithoutAPlacedShip_TheLabAnswers_WhateverTheAboardFlagSays()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("lab_shipless", transport: t);
        Assert.False(server.HasShip);
        var p = server.AddLocalPlayer("Walker");
        Assert.True(p.State.AboardShip); // the default nobody clears here

        var at = new Vector3f(40.5f, 200f, 40.5f);
        p.State.Position = at;
        server.World.SetBlock(new Vector3i(42, 200, 40), Block(BioItems.Lab));
        uint seed = server.GiveCreatureSampleForTest(p, Venomous(), 2);

        server.BioLabForTest(p, new BioLabIntent { Action = BioLabIntent.Analyse, Sample = seed });

        var result = SentTo<BioLabResult>(t, p).Last();
        Assert.True(result.Success, result.MessageKey);
        Assert.True(server.BioAnalysedForTest(p.State.PlayerId, seed));
    }

    /// <summary>A pilot in space never reaches a lab — in the cockpit or on a spacewalk. A launch leaves the on-foot
    /// position where it was (here: just inside the hull, three cells from a lab on the ground), the planet stays the
    /// pilot's world, and a spacewalk used to cancel the cabin test: the lab on the world below answered from orbit.</summary>
    [Fact]
    public void TheLab_IsNotUsedFromSpace_AlsoNotOnASpacewalk()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("lab_orbit", tune: c =>
        {
            c.PlaceStarterShip = true;
            c.Rules.FreeSpaceFlight = true;
        }, transport: t);
        var p = server.AddLocalPlayer("Pilot");
        Assert.True(server.HasShip);
        Empty(p.State.Inventory);
        Assert.Equal(0, p.State.Inventory.Add("carbon", 5, 1024));

        // The lab stands against the hull, outside; the pilot just inside the wall, aboard — the lab is in reach.
        var (origin, size) = server.LandedShipBoundsForTest("Pilot");
        int y = origin.Y + 1, z = origin.Z + (size.Z / 2);
        server.World.SetBlock(new Vector3i(origin.X - 2, y, z), Block(BioItems.Lab));
        p.State.Position = new Vector3f(origin.X + 1.5f, y, z + 0.5f);
        p.State.AboardShip = true;
        uint seed = server.GiveCreatureSampleForTest(p, Venomous(), 4);
        string sample = ItemKey.WithSeed(BioItems.Sample, seed);

        server.EnterSpace("Pilot");
        Assert.True(server.InSpace("Pilot"));
        foreach (bool spacewalk in new[] { false, true })
        {
            p.State.InEva = spacewalk;
            server.BioLabForTest(p, new BioLabIntent { Action = BioLabIntent.Mix, Sample = seed });
            server.BioLabForTest(p, new BioLabIntent { Action = BioLabIntent.Analyse, Sample = seed });
        }

        var refused = SentTo<BioLabResult>(t, p).ToList();
        Assert.Equal(4, refused.Count);
        Assert.All(refused, r => Assert.True(r is { Success: false, MessageKey: "srv.bio.need_lab" }, r.MessageKey));
        Assert.Equal(4, p.State.SampleCase.CountOf(sample));
        Assert.Equal(5, p.State.Inventory.CountOf("carbon"));
        Assert.Empty(server.BioReactionsForTest(p.State.PlayerId));
        Assert.False(server.BioAnalysedForTest(p.State.PlayerId, seed));
    }

    // ---------------- 8. The air of a void world ----------------

    /// <summary>A station deck is a void world: the temperature its environment reports and the temperature the status
    /// effects feel in a cabin are one value.</summary>
    [Fact]
    public void AVoidWorld_ReadsTheTemperatureTheEffectsFeelInACabin()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("deck_air", tune: c =>
        {
            c.PlaceSettlements = false;
            c.PlaceWrecks = false;
            c.World = new WorldDescription { SpaceStations = Frequency.Frequent };
            c.Rules.FreeSpaceFlight = true;
        }, transport: t);
        var p = server.AddLocalPlayer("Pilot");
        server.EnterSpace("Pilot");
        var station = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == SvEntityKind.SpaceStation);
        server.ShipMove("Pilot", station.Position.X, station.Position.Y, station.Position.Z - 8f); // into docking range
        t.Sent.Clear();
        server.BoardStation("Pilot", station.Id);
        Assert.True(server.InStation("Pilot"));
        Assert.False(p.State.AboveAtmosphere);

        var deck = SentTo<WorldEnvironment>(t, p).Last(); // the environment the boarding sends
        Assert.Equal(server.AmbientTemperatureForTest(p), deck.Temperature);
        Assert.InRange(deck.Temperature, BioRules.ColdBelow + 1f, BioRules.HotAbove - 1f); // a comfortable cabin
    }

    // ---------------- 9. A clone lost to fire or to a sentry post ----------------

    /// <summary>How many clones the tank's saved row lists (its "cl" list, one entry per clone).</summary>
    private static int ListedClones(SvGameServer server)
        => (ConfigValue(server, "cl") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Length;

    [Fact]
    public void ACloneThatBurns_LeavesItsTanksList_AtOnce()
    {
        var server = NewServer("clone_burn");
        var p = TankOwner(server);
        uint seed = server.GiveCreatureSampleForTest(p, LandSpecies(server)[0], 2);
        var clone = GrowClone(server, p, seed);
        Assert.Equal(1, ListedClones(server));

        // A pool of lava, and the clone in it.
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                server.World.SetBlock(new Vector3i(20 + dx, 299, 20 + dz), Block("stone"));
                server.World.SetBlock(new Vector3i(20 + dx, 300, 20 + dz), Block("lava"));
            }
        }

        var pool = new Vector3f(20.5f, 300.5f, 20.5f);
        for (int i = 0; i < 80 && server.Creatures.Contains(clone); i++)
        {
            clone.Position = pool; // it does not get to walk out of the test
            server.TickBurningForTest(0.5f);
        }

        // No beat of the Crystal Net has run since: the death itself brought the list up to date.
        Assert.DoesNotContain(clone, server.Creatures);
        Assert.Equal(0, ListedClones(server));
        Assert.Equal("0", ConfigValue(server, "clones"));
    }

    [Fact]
    public void ACloneASentryBringsDown_LeavesItsTanksList_AtOnce()
    {
        var server = NewServer("clone_sentry", tune: c => c.PlaceSettlements = false);
        var p = TankOwner(server);
        uint seed = server.GiveCreatureSampleForTest(p, LandSpecies(server)[0], 2);
        var clone = GrowClone(server, p, seed);
        Assert.Equal(1, ListedClones(server));

        // The owner's base with a sentry post beside its core, the owner at home.
        var core = new Vector3i(3, 204, 0);
        server.PlaceBaseForTest(p, core);
        int baseId = server.BaseSnapshots.Single(b => b.OwnerId == p.State.PlayerId).Id;
        var sentry = new Vector3i(core.X + 1, core.Y, core.Z);
        server.World.SetBlock(sentry, Block("sentry_post"), 0, 0, 0, p.State.Name);
        Assert.Equal(1, server.SentryCountForTest(baseId));

        // The clone was provoked and hunts its owner: a turret shoots that, clone or not.
        var near = new Vector3f(sentry.X + 4f, sentry.Y + 0.5f, sentry.Z + 0.5f);
        for (int i = 0; i < 80 && server.Creatures.Contains(clone); i++)
        {
            clone.Position = near;
            clone.ProvokeTimer = 30f;
            server.TickSentriesForTest();
        }

        Assert.DoesNotContain(clone, server.Creatures);
        Assert.Equal(0, ListedClones(server));
        Assert.Equal("0", ConfigValue(server, "clones"));
    }
}
