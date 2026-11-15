// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3676/#3681: the guardian giant on the server — it only wakes where planet machines are active (also after the finale),
/// flickers an eye at the stir range and rises at the wake range (removing only its own blocks), lies down lit and healed when
/// everyone left, falls for good with a crate for each fighter, is laid back down after a restart, and its blows are
/// announced, fall off with distance, are stopped by cover and never change a block.
/// </summary>
public sealed class GuardianGiantServerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_guardiangiant_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private SvGameServer Started(string world, RecordingTransport transport, Action<GameRules>? configure = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 9,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceBanditCamps = false,
            ViewDistanceChunks = 1,
        };
        config.Rules.PlanetEnemies = AlienActivity.Normal;
        config.Rules.PlanetDrones = false;
        config.Rules.Bandits = AlienActivity.Off;
        config.Rules.CreatureAbundance = AlienActivity.Off;
        configure?.Invoke(config.Rules);
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    private static PlayerSession Player(SvGameServer server, string name)
    {
        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = false;
        return p;
    }

    /// <summary>A giant lying on flat, dry ground some way off the player (the seed's start is an island), facing +Z — its
    /// head toward −Z, its feet at the anchor.</summary>
    private static GuardianGiantRecord Lay(SvGameServer server, PlayerSession p, bool canWake = true)
    {
        int px = (int)p.State.Position.X, pz = (int)p.State.Position.Z;
        var spot = (from r in Enumerable.Range(3, 10)
                    from a in Enumerable.Range(0, 16)
                    let x = px + (int)Math.Round(Math.Cos(a * Math.PI / 8) * r * 16)
                    let z = pz + (int)Math.Round(Math.Sin(a * Math.PI / 8) * r * 16)
                    let spread = server.HunterSpotSpreadForTest(x, z)
                    where spread is >= 0 and <= 1
                    select (X: x, Z: z)).First();
        p.State.Position = new Vector3f(spot.X + 200.5f, 80f, spot.Z + 0.5f); // far away while it is laid
        return server.LayGuardianGiantForTest(spot.X, spot.Z, facing: 0, canWake);
    }

    /// <summary>The lying body's flat centre.</summary>
    private static (float X, float Z) Centre(GuardianGiantRecord rec)
    {
        var (cx, cz) = GuardianGiantBody.LyingCentre(rec.Facing);
        return (rec.X + cx, rec.Z + cz);
    }

    /// <summary>Stands the player on the ground <paramref name="dx"/>/<paramref name="dz"/> blocks from a point.</summary>
    private static void Stand(SvGameServer server, PlayerSession p, float x, float z, float dx, float dz)
    {
        int cx = (int)Math.Floor(x + dx), cz = (int)Math.Floor(z + dz);
        p.State.Position = new Vector3f(cx + 0.5f, server.GroundFeetForTest(cx, cz), cz + 0.5f);
    }

    /// <summary>Stands the player beside the lying body (to its +X side) at <paramref name="distance"/> from its centre.</summary>
    private static void StandBeside(SvGameServer server, PlayerSession p, GuardianGiantRecord rec, float distance)
    {
        var (cx, cz) = Centre(rec);
        Stand(server, p, cx, cz, distance, 0f);
    }

    /// <summary>Wakes the giant with the player 15 blocks beside it and lets it start to rise.</summary>
    private static CombatEntity Awake(SvGameServer server, PlayerSession p, GuardianGiantRecord rec)
    {
        StandBeside(server, p, rec, 15f);
        server.TickGuardianGiantsForTest(1.5);
        return Assert.IsType<CombatEntity>(server.GuardianGiantEntityForTest());
    }

    private string KeyAt(SvGameServer server, Vector3i cell) => _content.BlockById(server.World.GetBlock(cell))?.Key ?? "air";

    private static IEnumerable<Vector3i> BodyCells(GuardianGiantRecord rec, bool corpse = false)
    {
        var (ax, ay, az, f) = corpse ? (rec.CorpseX, rec.CorpseY, rec.CorpseZ, rec.CorpseFacing) : (rec.X, rec.Y, rec.Z, rec.Facing);
        return GuardianGiantBody.Lying(rec.Seed, lit: false, f).Select(c => new Vector3i(ax + c.Dx, ay + c.Dy, az + c.Dz));
    }

    private static IEnumerable<Vector3i> LightCells(GuardianGiantRecord rec)
        => GuardianGiantBody.Lying(rec.Seed, lit: false, rec.Facing).Where(c => c.IsLight)
            .Select(c => new Vector3i(rec.X + c.Dx, rec.Y + c.Dy, rec.Z + c.Dz));

    [Theory]
    [InlineData("peaceful")]
    [InlineData("creative")]
    public void WithoutPlanetMachines_AGiantOnlyLiesThere(string mode)
    {
        var server = Started("gate_" + mode, new RecordingTransport(), r =>
        {
            if (mode == "peaceful") { r.PlanetEnemies = AlienActivity.Off; } else { r.GameMode = GameMode.Creative; }
        });
        var p = Player(server, "Justus");
        var rec = Lay(server, p);

        StandBeside(server, p, rec, 10f);
        server.TickGuardianGiantsForTest(4.0);

        Assert.Equal(GuardianGiantRecord.Dormant, rec.State);
        Assert.Null(server.GuardianGiantEntityForTest());
        Assert.True(rec.Found); // it can still be found and marked on the map
        Assert.All(LightCells(rec), c => Assert.Equal(GuardianGiantBody.LightOff, KeyAt(server, c)));
    }

    [Fact]
    public void AfterTheFinale_AGiantStillWakes()
    {
        var server = Started("gate_remnant", new RecordingTransport());
        var p = Player(server, "Justus");
        var rec = Lay(server, p);
        server.MarkGuardianDefeatedForTest(); // the Remnant era — the ordinary walking robots are gone, the giants are not

        Awake(server, p, rec);

        Assert.Equal(GuardianGiantRecord.Awake, rec.State);
    }

    [Fact]
    public void AtTheStirRange_OneEyeFlickers_AndVegaWarnsOncePerApproach()
    {
        var transport = new RecordingTransport();
        var server = Started("stir", transport);
        var p = Player(server, "Justus");
        var rec = Lay(server, p);

        StandBeside(server, p, rec, 30f);
        transport.Sent.Clear();
        server.TickGuardianGiantsForTest(6.0);

        Assert.Equal(GuardianGiantRecord.Dormant, rec.State);
        Assert.Null(server.GuardianGiantEntityForTest());
        int swaps = transport.Messages().OfType<BlockChanged>().Count();
        Assert.InRange(swaps, 1, 4 * 6); // a real block swap, a few a second at most
        Assert.Single(transport.LineKeys(p), k => GiantLine(k, "stir"));

        StandBeside(server, p, rec, 70f); // away — the eye goes dark again …
        server.TickGuardianGiantsForTest(2.0);
        Assert.All(LightCells(rec), c => Assert.Equal(GuardianGiantBody.LightOff, KeyAt(server, c)));
        StandBeside(server, p, rec, 30f); // … and a new approach is warned again
        server.TickGuardianGiantsForTest(2.0);
        Assert.Equal(2, transport.LineKeys(p).Count(k => GiantLine(k, "stir")));
    }

    [Fact]
    public void AtTheWakeRange_EveryLightGoesOn_ThenItRises_RemovingOnlyItsOwnBlocks()
    {
        var transport = new RecordingTransport();
        var server = Started("wake", transport);
        var p = Player(server, "Justus");
        var rec = Lay(server, p);
        var cells = BodyCells(rec).ToList();
        var foreign = cells.First(c => KeyAt(server, c) == GuardianGiantBody.Dark);
        server.World.SetBlock(foreign, _content.GetBlock("stone")!.NumericId, 0, 0, 0, "Justus"); // a player's block in the body

        StandBeside(server, p, rec, 15f);
        server.TickGuardianGiantsForTest(0.5);
        Assert.Null(server.GuardianGiantEntityForTest()); // the lights first …
        Assert.All(LightCells(rec), c => Assert.Equal(GuardianGiantBody.LightOn, KeyAt(server, c)));
        Assert.Contains(transport.LineKeys(p), k => GiantLine(k, "rise"));

        server.TickGuardianGiantsForTest(1.0); // … then it rises
        var giant = Assert.IsType<CombatEntity>(server.GuardianGiantEntityForTest());
        Assert.Equal(GuardianGiantMove.Rise, server.GuardianGiantMoveForTest());
        Assert.Equal(GuardianGiantRecord.Awake, rec.State);
        Assert.Equal(CombatEntityKind.GuardianGiant, giant.Kind);
        Assert.Equal(GuardianGiantRules.EnemyKey, giant.EnemyKey);
        Assert.Equal(_content.Enemy(GuardianGiantRules.EnemyKey).Tier(EnemyTiers.Normal).Hull, giant.HullMax);
        Assert.Equal((float)rec.X, giant.Position.X); // the root: the anchor's column corner, between the feet
        Assert.Equal((float)rec.Z, giant.Position.Z);
        Assert.Equal(rec.Facing * 90f, giant.Yaw);
        Assert.Contains(server.PlanetEnemies, e => e.Id == giant.Id);
        Assert.Equal("stone", KeyAt(server, foreign)); // the player's block stays
        Assert.All(cells.Where(c => c != foreign), c => Assert.Equal("air", KeyAt(server, c)));
    }

    [Fact]
    public void BreakingABodyBlock_WakesAGiantThatCanWake()
    {
        var server = Started("mine", new RecordingTransport());
        var p = Player(server, "Justus");
        var rec = Lay(server, p);
        var cell = BodyCells(rec).First(c => KeyAt(server, c) == GuardianGiantBody.Dark && KeyAt(server, c + new Vector3i(0, 1, 0)) == "air");
        p.State.Inventory.SetSlot(0, new ItemStack("basic_drill", 1));
        p.State.Position = new Vector3f(cell.X + 0.5f, cell.Y + 1f, cell.Z + 0.5f); // standing on it

        server.MineBlock(p.State.PlayerId, cell.X, cell.Y, cell.Z);
        Assert.Equal("air", KeyAt(server, cell));
        server.TickGuardianGiantsForTest(1.2);

        Assert.NotNull(server.GuardianGiantEntityForTest());
        Assert.Equal(GuardianGiantRecord.Awake, rec.State);
    }

    [Fact]
    public void WhenEveryoneLeaves_ItLiesDownLit_AndRisesAgainHealed()
    {
        var server = Started("leash", new RecordingTransport());
        var p = Player(server, "Justus");
        var rec = Lay(server, p);
        var giant = Awake(server, p, rec);
        server.DamageGuardianGiantForTest(150f, p.State.PlayerId);
        Assert.Equal(giant.HullMax - 150f, giant.Hull);

        StandBeside(server, p, rec, 300f); // out of its fight range — after a while it goes home
        server.TickGuardianGiantsForTest(9.0); // eight seconds alone, then (unwatched) it lies down at once
        Assert.Equal(GuardianGiantMove.LieDown, server.GuardianGiantMoveForTest());
        server.TickGuardianGiantsForTest(GuardianGiantRules.Spec(GuardianGiantMove.LieDown).Duration + 0.2);

        Assert.Null(server.GuardianGiantEntityForTest());
        Assert.DoesNotContain(server.PlanetEnemies, e => e.Id == giant.Id);
        Assert.Equal(GuardianGiantRecord.Lit, rec.State);
        Assert.All(LightCells(rec), c => Assert.Equal(GuardianGiantBody.LightOn, KeyAt(server, c))); // lying again, lit
        Assert.Contains(BodyCells(rec), c => KeyAt(server, c) == GuardianGiantBody.Dark);

        StandBeside(server, p, rec, 15f); // a lit giant rises at once (on the next scan) — and healed
        server.TickGuardianGiantsForTest(1.5);
        var again = Assert.IsType<CombatEntity>(server.GuardianGiantEntityForTest());
        Assert.Equal(again.HullMax, again.Hull);
    }

    [Fact]
    public void AnAwakeGiant_IsLaidBackDownLit_WhenTheWorldComesBack()
    {
        var server = Started("restore", new RecordingTransport());
        var p = Player(server, "Justus");
        var rec = Lay(server, p);
        Awake(server, p, rec);
        Assert.Equal(GuardianGiantRecord.Awake, rec.State);

        server.ForgetGuardianGiantRuntimeForTest(); // the server stopped mid-fight
        StandBeside(server, p, rec, 60f);
        server.TickGuardianGiantsForTest(1.1);

        Assert.Equal(GuardianGiantRecord.Lit, rec.State);
        Assert.Null(server.GuardianGiantEntityForTest());
        Assert.All(LightCells(rec), c => Assert.Equal(GuardianGiantBody.LightOn, KeyAt(server, c)));
    }

    [Fact]
    public void ADefeatedGiant_LiesWhereItFell_ForGood_AndEveryFighterGetsACrate()
    {
        var transport = new RecordingTransport();
        var server = Started("defeat", transport);
        var a = Player(server, "Justus");
        var b = Player(server, "Marcel");
        var events = new RecordingGameEventListener();
        server.AddGameEventListener(GameEventKeys.GuardianGiantDefeated, events);
        var rec = Lay(server, a);
        var giant = Awake(server, a, rec);
        b.State.UnlockedBlueprints.UnionWith(new[] { "guardian_gear", "guardian_sword", "guardian_drill" });

        server.DamageGuardianGiantForTest(20f, a.State.PlayerId); // Justus hits it …
        Stand(server, b, giant.Position.X, giant.Position.Z, 3f, 0f); // … Marcel is hit by a stomp
        server.StartGuardianGiantMoveForTest(GuardianGiantMove.Stomp, giant.Position);
        server.TickGuardianGiantsForTest(GuardianGiantRules.Spec(GuardianGiantMove.Stomp).Impact + 0.1);
        Assert.Equal(new[] { "Justus", "Marcel" }, server.GuardianGiantFightersForTest().OrderBy(n => n));

        server.DamageGuardianGiantForTest(10_000f, a.State.PlayerId);
        Assert.Equal(GuardianGiantMove.Collapse, server.GuardianGiantMoveForTest());
        Assert.False(giant.Hostile);
        server.TickGuardianGiantsForTest(GuardianGiantRules.Spec(GuardianGiantMove.Collapse).Duration + 0.2);

        Assert.Equal(GuardianGiantRecord.Defeated, rec.State);
        Assert.True(rec.DefeatedAt > 0);
        Assert.Null(server.GuardianGiantEntityForTest());
        Assert.Contains(transport.Messages().OfType<PlanetEnemyDefeated>(), d => d.Id == giant.Id);
        string[] bodyBlocks = { GuardianGiantBody.Plate, GuardianGiantBody.Rust, GuardianGiantBody.Dark, GuardianGiantBody.Trim, GuardianGiantBody.LightOff, GuardianGiantBody.LightOn };
        var corpse = BodyCells(rec, corpse: true).Where(c => bodyBlocks.Contains(KeyAt(server, c))).ToList();
        Assert.True(corpse.Count > 100, "the body lies where it fell");
        Assert.DoesNotContain(corpse, c => KeyAt(server, c) == GuardianGiantBody.LightOn); // lights out

        AssertCrate(server, rec, "Justus", expectPiece: false);
        AssertCrate(server, rec, "Marcel", expectPiece: true);
        Assert.Single(a.State.UnlockedBlueprints, k => k.StartsWith("guardian_", StringComparison.Ordinal)); // one he lacked, learned
        Assert.Equal(new[] { "Justus", "Marcel" }, rec.Rewarded.OrderBy(n => n));
        Assert.Equal(1, a.State.AchievementCounters[SvGameServer.GuardianGiantDefeatCounter]);
        Assert.Equal(2, events.Of(GameEventKeys.GuardianGiantDefeated).Count(e => e.Arg == rec.SiteId));

        StandBeside(server, a, rec, 5f); // it never wakes again
        server.TickGuardianGiantsForTest(3.0);
        Assert.Null(server.GuardianGiantEntityForTest());
        Assert.Equal(GuardianGiantRecord.Defeated, rec.State);
    }

    /// <summary>The fighter's owner-locked crate near the corpse: the data's loot (12–16 guardian plates) — and, for a fighter
    /// who knows every guardian blueprint, a finished guardian piece.</summary>
    private void AssertCrate(SvGameServer server, GuardianGiantRecord rec, string owner, bool expectPiece)
    {
        var crate = Assert.Single(server.Containers, c => c.Owner == owner);
        var (cx, cz) = GuardianGiantBody.LyingCentre(rec.CorpseFacing);
        float dx = crate.Position.X - (rec.CorpseX + cx), dz = crate.Position.Z - (rec.CorpseZ + cz);
        Assert.InRange(Math.Sqrt((dx * dx) + (dz * dz)), 6.0, 17.0);
        Assert.InRange(crate.Items.Where(i => i.Item == EnemyCatalog.GuardianPlate).Sum(i => i.Count), 12, 16);
        string[] pieces = { "guardian_helmet", "guardian_chest", "guardian_legs", "guardian_sword", "guardian_drill" };
        Assert.Equal(expectPiece ? 1 : 0, crate.Items.Count(i => pieces.Contains(i.Item)));
        Assert.Equal(_content.GetBlock("crate")!.NumericId, server.World.GetBlock(crate.Position));
    }

    [Fact]
    public void AStomp_FallsOffWithDistance_AndNoAttackChangesABlock()
    {
        var transport = new RecordingTransport();
        var server = Started("stomp", transport);
        var near = Player(server, "Near");
        var edge = Player(server, "Edge");
        var outside = Player(server, "Outside");
        var rec = Lay(server, near);
        var giant = Awake(server, near, rec);
        Stand(server, near, giant.Position.X, giant.Position.Z, 1f, 0f);
        Stand(server, edge, giant.Position.X, giant.Position.Z, 5f, 0f);
        Stand(server, outside, giant.Position.X, giant.Position.Z, 0f, -9f);
        float nearBefore = near.State.Health, edgeBefore = edge.State.Health, outsideBefore = outside.State.Health;

        transport.Sent.Clear();
        server.StartGuardianGiantMoveForTest(GuardianGiantMove.Stomp, giant.Position);
        server.TickGuardianGiantsForTest(GuardianGiantRules.Spec(GuardianGiantMove.Stomp).Impact - 0.2);
        Assert.Equal(nearBefore, near.State.Health); // the raised foot is the warning — nothing yet
        server.TickGuardianGiantsForTest(0.3);

        float nearLoss = nearBefore - near.State.Health, edgeLoss = edgeBefore - edge.State.Health;
        Assert.True(nearLoss > edgeLoss && edgeLoss > 0f, $"near {nearLoss}, edge {edgeLoss}");
        Assert.Equal(outsideBefore, outside.State.Health);
        Assert.Contains(transport.Messages().OfType<WorldFx>(), fx => fx.Kind == GuardianGiantRules.WaveFx);
        Assert.Empty(transport.Messages().OfType<BlockChanged>());
        Assert.Equal(GuardianGiantRules.StompDamage, GuardianGiantRules.WaveDamage(GuardianGiantRules.StompDamage, GuardianGiantRules.StompRadius, 0f));
        Assert.Equal(0f, GuardianGiantRules.WaveDamage(GuardianGiantRules.StompDamage, GuardianGiantRules.StompRadius, 6.5f));
    }

    [Fact]
    public void TheLaser_HitsAlongItsBeam_ButAWallBetweenStopsIt()
    {
        var server = Started("laser", new RecordingTransport());
        var p = Player(server, "Justus");
        var rec = Lay(server, p);
        var giant = Awake(server, p, rec); // facing +Z: the front is +Z
        Stand(server, p, giant.Position.X, giant.Position.Z, 0f, 12f);
        var aim = new Vector3f(p.State.Position.X, p.State.Position.Y + 1f, p.State.Position.Z);
        float before = p.State.Health;

        server.StartGuardianGiantMoveForTest(GuardianGiantMove.Laser, aim);
        server.TickGuardianGiantsForTest(GuardianGiantRules.Spec(GuardianGiantMove.Laser).Impact + 0.1);
        Assert.True(p.State.Health < before, "the beam hits a player in the open");

        int wallZ = (int)Math.Floor(giant.Position.Z) + 7; // a wall across the beam, between the visor and the player
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int x = (int)giant.Position.X - 3; x <= (int)giant.Position.X + 3; x++)
        {
            for (int y = (int)giant.Position.Y; y <= (int)giant.Position.Y + 16; y++)
            {
                server.World.SetBlock(new Vector3i(x, y, wallZ), stone);
            }
        }

        float sheltered = p.State.Health;
        server.TickGuardianGiantsForTest(GuardianGiantRules.Spec(GuardianGiantMove.Laser).Duration);
        server.StartGuardianGiantMoveForTest(GuardianGiantMove.Laser, aim);
        server.TickGuardianGiantsForTest(GuardianGiantRules.Spec(GuardianGiantMove.Laser).Impact + 0.1);
        Assert.Equal(sheltered, p.State.Health);
    }

    [Fact]
    public void TheStatus_CarriesTheMoveItsClockTheAimAndTheBossBar_OnlyToPlayersNear()
    {
        var transport = new RecordingTransport();
        var server = Started("status", transport);
        var p = Player(server, "Justus");
        var far = Player(server, "Far");
        var rec = Lay(server, p);
        far.State.Position = new Vector3f(rec.X + 400.5f, 80f, rec.Z + 0.5f);
        var giant = Awake(server, p, rec);

        var rise = transport.To<GuardianGiantStatus>(p).First(s => s.Move == (byte)GuardianGiantMove.Rise);
        Assert.Equal(rec.SiteId, rise.SiteId);
        Assert.Equal(giant.Id, rise.EntityId);
        Assert.Equal(giant.HullMax, rise.HullMax);
        Assert.Equal((rec.X, rec.Y, rec.Z, rec.Facing, rec.Seed), (rise.AnchorX, rise.AnchorY, rise.AnchorZ, (int)rise.Facing, rise.Seed));
        Assert.Empty(transport.To<GuardianGiantStatus>(far));

        var landing = new Vector3f(giant.Position.X, giant.Position.Y, giant.Position.Z + 14f);
        transport.Sent.Clear();
        server.StartGuardianGiantMoveForTest(GuardianGiantMove.JumpAttack, landing);
        var jump = Assert.Single(transport.To<GuardianGiantStatus>(p));
        Assert.Equal((byte)GuardianGiantMove.JumpAttack, jump.Move);
        Assert.Equal(0f, jump.MoveElapsed);
        Assert.Equal((landing.X, landing.Y, landing.Z), (jump.AimX, jump.AimY, jump.AimZ));

        server.TickGuardianGiantsForTest(1.0); // twice a second while awake, the clock running on
        var later = transport.To<GuardianGiantStatus>(p).Last();
        Assert.True(later.MoveElapsed > 0.4f && later.ServerTime > jump.ServerTime);
    }

    [Fact]
    public void AFoundGiant_GoesOnTheMap_AndVegaNamesTheFirstOneOnce()
    {
        var transport = new RecordingTransport();
        var server = Started("found", transport, r => r.PlanetEnemies = AlienActivity.Off);
        var p = Player(server, "Justus");
        var rec = Lay(server, p, canWake: false);
        Assert.DoesNotContain(server.PlanetPoisForTest(p.State.PlayerId), poi => poi.Type == GuardianGiantRules.PoiType);

        StandBeside(server, p, rec, 40f);
        server.TickGuardianGiantsForTest(3.0);

        Assert.True(rec.Found);
        var poi = Assert.Single(server.PlanetPoisForTest(p.State.PlayerId), x => x.Type == GuardianGiantRules.PoiType);
        Assert.Equal(GuardianGiantRules.PoiLying, poi.State);
        Assert.Single(transport.LineKeys(p), k => GiantLine(k, "found"));
        Assert.Contains(SvGameServer.GuardianGiantFoundMilestone, p.State.Milestones);
    }

    /// <summary>A giant line of <paramref name="moment"/>: the neutral key, or the VEGA story's variant by what the player's
    /// story knows (#3685, <c>story.vega.giant.&lt;moment&gt;.&lt;level&gt;</c>).</summary>
    private static bool GiantLine(string key, string moment)
        => key == "vega.giant." + moment || key.StartsWith("story.vega.giant." + moment + ".", System.StringComparison.Ordinal);
}
