// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Derelict stations (#2632, #2634, #2674, #2675): broken old stations — one guaranteed in every world's start system,
/// otherwise rare; every new one the wreck of a beta station (the old boxy flight model: hub, collar, crossed arms with
/// pods, solar wings on ±X, a mast) with closed and breached rooms, doors, a broken trading post and the spots the later
/// phases use, while one a save already stored keeps its old layout; nobody lives there and no trading rule counts them;
/// no room breathes (dead life support) — the suit tank is the air and the ship refills it — falling out is a spacewalk;
/// mined blocks stay mined across a restart and the hull seen from space follows.
/// </summary>
public sealed class DerelictStationTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public DerelictStationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_derelict_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    /// <summary>Records every server send, so a test can see which warning and which VEGA line were told.</summary>
    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<object> Sent = new();

        public void Start(int port) { }

        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add(m);
        }

        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add(m);
        }

        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }

        public List<string> Lines() => Sent.OfType<ServerMessage>().Select(m => m.Text).ToList();

        public List<string> Vega() => Sent.OfType<ShipAiLine>().Select(l => l.LineKey).ToList();
    }

    private SvGameServer NewServer(string world, out SqliteWorldRepository repo, long seed = 7, Frequency derelicts = Frequency.Normal,
        IServerTransport? transport = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig { WorldName = world, Seed = seed, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        config.World.Derelicts = derelicts;
        var server = new SvGameServer(config, _content, transport ?? new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>A layout of the given kind: <c>beta</c> (every new derelict), <c>legacy-procedural</c> (an old guaranteed
    /// one) or <c>legacy</c> (an old one of any source).</summary>
    private DerelictLayout Layout(string kind, long seed) => kind switch
    {
        "beta" => DerelictGenerator.Generate(seed, _content),
        "legacy-procedural" => DerelictGenerator.GenerateLegacy(seed, _content, proceduralOnly: true),
        _ => DerelictGenerator.GenerateLegacy(seed, _content),
    };

    private static string StartSystemOf(SvGameServer server) => server.Galaxy.FindBody(server.Metadata.ActiveLocationId)!.SystemId;

    /// <summary>The guaranteed derelict of the start system.</summary>
    private static string GuaranteedDerelict(SvGameServer server) => DerelictPlacer.BodyId(StartSystemOf(server), 0);

    /// <summary>Flies a fresh player up to the start system's derelict and boards it; returns its id.</summary>
    private static string BoardGuaranteed(SvGameServer server, PlayerSession p)
    {
        string id = GuaranteedDerelict(server);
        string pid = p.State.PlayerId;
        server.EnterSpace(pid);
        var contact = server.SpaceEntitiesFor(pid).First(e => e.Id == id);
        var layout = server.DerelictLayoutForTest(id)!;
        server.ShipMove(pid, contact.Position.X, contact.Position.Y, contact.Position.Z - (layout.Length / 2f + 8f));
        server.BoardStation(pid, id);
        Assert.True(server.InStation(pid), "the derelict should be boardable by anyone");
        return id;
    }

    private static void Equip(PlayerSession p, string tool)
    {
        p.State.Inventory.SetSlot(0, new ItemStack(tool, 1));
        p.State.SelectedHotbarSlot = 0;
    }

    private List<StarSystem> Galaxy(long seed, int systems = 12)
    {
        var desc = new WorldDescription { StarSystemCount = systems };
        return new UniverseGenerator(seed, desc, _content).Generate(systems).Systems;
    }

    private static int SystemIndex(string bodyId)
    {
        string rest = bodyId.Substring(DerelictPlacer.IdPrefix.Length + 3); // "derelict:sys" stripped
        return int.Parse(rest.Substring(0, rest.IndexOf('-')), System.Globalization.CultureInfo.InvariantCulture);
    }

    // ---------------- placement ----------------

    [Fact]
    public void Placer_GuaranteesTheStartSystem_IsRareElsewhere_AndOffPlacesNone()
    {
        int systemsWithOne = 0, candidates = 0;
        for (long seed = 1; seed <= 40; seed++) // small typed seeds on purpose: they must roll extras too
        {
            var systems = Galaxy(seed);
            DerelictPlacer.Place(systems, seed, Frequency.Normal, systems.Count, "sys0");
            var sys0 = systems.First(s => s.Id == "sys0");
            Assert.Contains(sys0.Bodies, b => b.Id == DerelictPlacer.BodyId("sys0", 0) && b.Kind == CelestialKind.SpaceStation);

            foreach (var sys in systems.Where(s => s.Id != "sys0"))
            {
                int n = sys.Bodies.Count(b => DerelictPlacer.IsDerelictId(b.Id));
                Assert.InRange(n, 0, DerelictPlacer.MaxPerSystem);
                candidates++;
                systemsWithOne += n > 0 ? 1 : 0;
            }

            // Idempotent: a second pass over the same galaxy places nothing.
            Assert.Equal(0, DerelictPlacer.Place(systems, seed, Frequency.Normal, systems.Count, "sys0"));

            var off = Galaxy(seed);
            Assert.Equal(0, DerelictPlacer.Place(off, seed, Frequency.Off, off.Count, "sys0"));
            Assert.DoesNotContain(off.SelectMany(s => s.Bodies), b => DerelictPlacer.IsDerelictId(b.Id));
        }

        double share = (double)systemsWithOne / candidates;
        Assert.True(share < 0.3, $"derelicts should be rare outside the start system, found them in {share:P0} of the systems");
        Assert.True(systemsWithOne > 0, "rare is not never: some system beyond the start must hold a derelict");
    }

    [Fact]
    public void Placer_IsSeedStable_AndNeverTouchesAGrownSystem()
    {
        var a = Galaxy(42, systems: 14);
        var b = Galaxy(42, systems: 14);
        DerelictPlacer.Place(a, 42, Frequency.Frequent, fixedCount: 10, "sys2");
        DerelictPlacer.Place(b, 42, Frequency.Frequent, fixedCount: 10, "sys2");
        var ida = a.SelectMany(s => s.Bodies).Where(x => DerelictPlacer.IsDerelictId(x.Id)).Select(x => (x.Id, x.Name, x.SystemX, x.SystemZ)).ToList();
        var idb = b.SelectMany(s => s.Bodies).Where(x => DerelictPlacer.IsDerelictId(x.Id)).Select(x => (x.Id, x.Name, x.SystemX, x.SystemZ)).ToList();
        Assert.Equal(ida, idb);
        Assert.Contains(ida, d => d.Id == DerelictPlacer.BodyId("sys2", 0));
        Assert.All(ida, d => Assert.True(SystemIndex(d.Id) < 10, $"{d.Id} sits in a system beyond the fixed ten"));
        Assert.All(ida, d => Assert.False(string.IsNullOrWhiteSpace(d.Name)));
    }

    [Fact]
    public void AnOldSave_GainsItsDerelicts_WithoutMovingAnyOtherBody_OrTheStartStation()
    {
        var off = NewServer("drl_off", out var repoOff, seed: 11, derelicts: Frequency.Off);
        List<(string Id, string Name, float X, float Z)> before;
        using (repoOff)
        {
            Assert.Empty(off.DerelictIdsForTest);
            before = off.Galaxy.AllBodies().Select(b => (b.Id, b.Name, b.SystemX, b.SystemZ)).ToList();
            off.Stop();
        }

        var on = NewServer("drl_on", out var repoOn, seed: 11, derelicts: Frequency.Normal);
        using (repoOn)
        {
            var after = on.Galaxy.AllBodies().Where(b => !DerelictPlacer.IsDerelictId(b.Id) || b.Id == SvGameServer.DerelictStationId)
                .Select(b => (b.Id, b.Name, b.SystemX, b.SystemZ)).ToList();
            Assert.Equal(before, after);
            Assert.Contains(GuaranteedDerelict(on), on.DerelictIdsForTest);

            // The start system still has its own trading station (a derelict never stands in for it).
            string sys = StartSystemOf(on);
            Assert.Contains(on.Galaxy.Systems.First(s => s.Id == sys).Bodies,
                b => b.Kind == CelestialKind.SpaceStation && !DerelictPlacer.IsDerelictId(b.Id));
            on.Stop();
        }
    }

    // ---------------- layout ----------------

    [Theory]
    [InlineData("beta")]
    [InlineData("legacy-procedural")]
    public void Layout_IsSeedStable_AndCarriesTheHooksOfTheLaterPhases(string kind)
    {
        var a = Layout(kind, 1234);
        var b = Layout(kind, 1234);
        Assert.Equal((a.Width, a.Height, a.Length, a.Source), (b.Width, b.Height, b.Length, b.Source));
        for (int x = 0; x < a.Width; x++)
            for (int y = 0; y < a.Height; y++)
                for (int z = 0; z < a.Length; z++)
                {
                    Assert.Equal(a.Get(x, y, z), b.Get(x, y, z));
                    Assert.Equal(a.GetModifier(x, y, z), b.GetModifier(x, y, z));
                }

        Assert.Equal(a.CrateSpots, b.CrateSpots);
        Assert.Equal(a.SpawnerSpot, b.SpawnerSpot);
    }

    [Theory]
    [InlineData("beta")]
    [InlineData("legacy-procedural")]
    [InlineData("legacy")]
    public void Layouts_AreBrokenButBoardable_WithClosedAndBreachedRooms_PostsAndSpots(string kind)
    {
        ushort vendor = _content.GetBlock("station_vendor")!.NumericId.Value;
        var forbidden = new HashSet<ushort>(_content.Blocks.Values
            .Where(d => d.Category == "flora" || d.Key is "force_field" or "data_cache" or "heal_tank")
            .Select(d => d.NumericId.Value));
        int templates = 0;
        for (long seed = 1; seed <= 16; seed++)
        {
            var l = Layout(kind, seed * 7919);
            Assert.Equal(kind == "beta", l.IsBeta);
            bool template = l.Source.StartsWith("template:", StringComparison.Ordinal);
            templates += template ? 1 : 0;

            Assert.NotEmpty(l.TraderPosts);
            Assert.All(l.TraderPosts, p => Assert.Equal(vendor, l.Get(p.X, p.Y, p.Z)));
            Assert.True(l.CrateSpots.Count >= 1, $"seed {seed}: no crate spot");
            Assert.All(l.CrateSpots, c => Assert.True(l.Get(c.X, c.Y, c.Z) == 0 && l.Get(c.X, c.Y - 1, c.Z) != 0, $"seed {seed}: crate spot {c} is no empty deck cell"));
            Assert.NotNull(l.SpawnerSpot);

            for (int x = 0; x < l.Width; x++)
                for (int y = 0; y < l.Height; y++)
                    for (int z = 0; z < l.Length; z++)
                    {
                        Assert.DoesNotContain(l.Get(x, y, z), forbidden);
                    }

            if (!template)
            {
                Assert.True(l.ArrivalSealed, $"seed {seed} ({l.Source}): the command room must stay closed");
                Assert.True(l.BreachedRoomCount >= 1, $"seed {seed} ({l.Source}): no room is open to space");
                Assert.True(l.SealedRoomCount >= 1);
                Assert.NotEmpty(l.Doors);
            }
        }

        if (kind != "legacy")
        {
            Assert.Equal(0, templates);
        }
    }

    /// <summary>#2674: from outside a new derelict is the old beta station, broken — the cube hub with its dark collar and
    /// glass band, the crossed arms with a pod at each end, the flat blue solar wings on ±X (one of them half torn off),
    /// the mast on the roof (snapped, its dead beacon floating beside the stump), rust and a few loose blocks.</summary>
    [Fact]
    public void ABetaWreck_HasTheOldStationsSilhouette_Broken()
    {
        ushort Id(string key) => _content.GetBlock(key)!.NumericId.Value;
        ushort carbon = Id("carbon"), glass = Id("glass"), rust = Id("rusted_panel"), beaconOff = Id("light_white_off"), beaconOn = Id("light_white");
        var c = BetaStationGenerator.Centre;
        for (long seed = 1; seed <= 8; seed++)
        {
            var l = DerelictGenerator.Generate(seed * 104729, _content);
            Assert.Equal((BetaStationGenerator.Width, BetaStationGenerator.Height, BetaStationGenerator.Length), (l.Width, l.Height, l.Length));
            Assert.Equal(BetaStationGenerator.SizeTier, l.SizeTier);
            int Count(int x0, int x1, int y0, int y1, int z0, int z1, Func<ushort, int, bool> what)
            {
                int n = 0;
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                        for (int z = z0; z <= z1; z++)
                        {
                            n += what(l.Get(c.X + x, c.Y + y, c.Z + z), l.GetModifier(c.X + x, c.Y + y, c.Z + z).Tint) ? 1 : 0;
                        }

                return n;
            }

            // The hub: its roof stands, the command deck is a hall, and its walls carry the glass band.
            int h = BetaStationGenerator.HubHalf;
            Assert.True(Count(-h, h, BetaStationGenerator.HubTop, BetaStationGenerator.HubTop, -h, h, (b, _) => b != 0) > 180, $"seed {seed}: the hub's roof");
            Assert.True(Count(-h + 2, h - 2, 0, 3, -h + 2, h - 2, (b, _) => b == 0) > 300, $"seed {seed}: the command deck is a hall");
            Assert.True(Count(-h, h, BetaStationGenerator.BandBottom, BetaStationGenerator.BandTop, -h, -h, (b, _) => b == glass) >= 20, $"seed {seed}: the view band");

            // The collar ring: dark blocks round the hub on all four sides.
            foreach (var (ax, az) in BetaStationGenerator.Arms)
            {
                // A stretch of the ring on this side, clear of the arm's tube: 3 out × 5 along × 3 high.
                int a0 = h + 1, a1 = BetaStationGenerator.CollarReach;
                int x0 = ax > 0 ? a0 : ax < 0 ? -a1 : 3, x1 = ax > 0 ? a1 : ax < 0 ? -a0 : 7;
                int z0 = az > 0 ? a0 : az < 0 ? -a1 : 3, z1 = az > 0 ? a1 : az < 0 ? -a0 : 7;
                Assert.True(Count(x0, x1, BetaStationGenerator.CollarBottom, BetaStationGenerator.CollarTop, z0, z1, (b, _) => b == carbon) >= 30,
                    $"seed {seed}: the collar beside the hub on ({ax},{az})");
            }

            // A pod at the end of every arm, the arm's tube leading to it.
            foreach (var (ax, az) in BetaStationGenerator.Arms)
            {
                int p = BetaStationGenerator.PodCentre, r = BetaStationGenerator.PodHalf;
                int x0 = ax != 0 ? ax * p - r : -r, x1 = ax != 0 ? ax * p + r : r, z0 = az != 0 ? az * p - r : -r, z1 = az != 0 ? az * p + r : r;
                Assert.True(Count(x0, x1, BetaStationGenerator.PodBottom, BetaStationGenerator.PodTop, z0, z1, (b, _) => b != 0) > 300,
                    $"seed {seed}: no pod at the end of the ({ax},{az}) arm");
                int t = (BetaStationGenerator.HubHalf + BetaStationGenerator.PodCentre - BetaStationGenerator.PodHalf) / 2;
                int tx = ax * t, tz = az * t;
                Assert.True(Count(tx - 2, tx + 2, BetaStationGenerator.Deck, BetaStationGenerator.ArmTop, tz - 2, tz + 2, (b, _) => b == carbon) >= 6,
                    $"seed {seed}: no arm toward ({ax},{az})");
            }

            // The solar wings: blue cells on +X and −X only, both still there near the pods — one half torn off.
            bool Solar(ushort b, int tint) => b == glass && tint == StationKitExterior.SolarTint;
            int wi = BetaStationGenerator.WingInner, wo = BetaStationGenerator.WingOuter, span = BetaStationGenerator.WingHalfSpan;
            int plusNear = Count(wi, wi + 4, -1, 1, -span, span, Solar), minusNear = Count(-wi - 4, -wi, -1, 1, -span, span, Solar);
            int plusFar = Count(wo - 3, wo, -3, 3, -span, span, Solar), minusFar = Count(-wo, -wo + 3, -3, 3, -span, span, Solar);
            Assert.True(plusNear >= 12 && minusNear >= 12, $"seed {seed}: the wings by the pods ({plusNear} / {minusNear} solar cells)");
            Assert.True(Math.Min(plusFar, minusFar) <= 4 && Math.Max(plusFar, minusFar) >= 16,
                $"seed {seed}: exactly one wing is torn off at its far end ({plusFar} / {minusFar} solar cells out there)");
            Assert.Equal(0, Count(-h, h, -8, 18, h + 1, 25, Solar) + Count(-h, h, -8, 18, -25, -h - 1, Solar)); // nothing out on ±Z

            // The mast: snapped — its stump on the roof, the top piece with the dead beacon floating beside it, no lit beacon.
            Assert.True(Count(0, 0, BetaStationGenerator.MastBottom, BetaStationGenerator.MastBottom + 2, 0, 0, (b, _) => b == carbon) >= 1, $"seed {seed}: the stump");
            Assert.Equal(1, Count(-1, 1, BetaStationGenerator.MastBottom, BetaStationGenerator.BeaconLayer, -1, 1, (b, _) => b == beaconOff));
            Assert.Equal(0, Count(0, 0, BetaStationGenerator.MastBottom, BetaStationGenerator.BeaconLayer, 0, 0, (b, _) => b == beaconOff)); // not on the stump
            Assert.Equal(0, Count(-h, h, BetaStationGenerator.HubTop, BetaStationGenerator.BeaconLayer, -h, h, (b, _) => b == beaconOn));

            // Rust on the hull.
            Assert.True(Count(-wo, wo, -8, 18, -25, 25, (b, _) => b == rust) >= 4, $"seed {seed}: no rusted patches");
        }
    }

    /// <summary>#2674: a derelict a save stored before the beta stations is rebuilt from <see cref="DerelictGenerator.GenerateLegacy"/>
    /// — its arrival, crate spots, guardian bay and flight position must match the build the save holds. So that generator
    /// must keep producing exactly the same layouts: these fingerprints (block keys, dyes, shapes, spots, doors) were taken
    /// before the beta stations came. If this fails, something the legacy layouts are made from changed (the decay pass,
    /// <see cref="StationGenerator.Generate"/>, a classic template in <c>data/station_templates.json</c>): every stored old
    /// derelict would then be rebuilt from a layout that no longer matches its build. Undo that for the legacy path (or
    /// decide on a migration) before updating the pins.</summary>
    [Theory]
    [InlineData(1, true, "66263ade5c653445")]
    [InlineData(2, true, "866fa37e86a6bb0e")]
    [InlineData(3, true, "c40999f658e43cdc")]
    [InlineData(1, false, "3587f10e55a4daa3")]
    [InlineData(5, false, "dd001425a549f008")]
    [InlineData(6, false, "068f072427dba254")]
    [InlineData(9, false, "668a44bd4b8385a7")]
    public void TheLegacyGenerator_StillBuildsTheLayoutsOldSavesWereMadeFrom(long seed, bool proceduralOnly, string fingerprint)
    {
        var l = DerelictGenerator.GenerateLegacy(seed * 7919, _content, proceduralOnly);
        Assert.Equal(fingerprint, Fingerprint(l));
    }

    /// <summary>A stable fingerprint of a layout: FNV-1a over its source, size, spots, doors and every block (by key, so
    /// a shift of the numeric block ids does not change it) with its dye and shape.</summary>
    private string Fingerprint(DerelictLayout l)
    {
        ulong h = 1469598103934665603UL;
        void Mix(string s)
        {
            foreach (char ch in s)
            {
                h = (h ^ ch) * 1099511628211UL;
            }

            h = (h ^ '|') * 1099511628211UL;
        }

        Mix(l.Source);
        Mix(l.SizeTier);
        Mix($"{l.Width},{l.Height},{l.Length}");
        Mix(l.Arrival.ToString());
        Mix(l.SpawnerSpot?.ToString() ?? "none");
        foreach (var c in l.CrateSpots) Mix(c.ToString());
        foreach (var p in l.TraderPosts) Mix(p.ToString());
        foreach (var d in l.Doors) Mix(d.Cell + ":" + d.AxisX);
        for (int x = 0; x < l.Width; x++)
            for (int y = 0; y < l.Height; y++)
                for (int z = 0; z < l.Length; z++)
                {
                    ushort b = l.Get(x, y, z);
                    if (b != 0)
                    {
                        var m = l.GetModifier(x, y, z);
                        Mix($"{x},{y},{z}:{_content.BlockById(new BlockId(b))?.Key}:{m.Tint}:{m.Glow}:{l.GetShape(x, y, z)}");
                    }
                }

        return h.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }

    // ---------------- the server ----------------

    [Fact]
    public void Boarding_ADerelict_FindsNobody_NoTrade_AndArrivesOnTheCommandDeck()
    {
        var server = NewServer("drl_board", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            string id = BoardGuaranteed(server, p);
            var layout = server.DerelictLayoutForTest(id)!;
            Assert.True(layout.IsBeta, "the guaranteed derelict is a beta wreck too");

            // #2675: set down on the command deck — the layout's arrival — standing, no air needed.
            var arrival = server.DerelictCellToWorldForTest(id, layout.Arrival);
            Assert.Equal(arrival, p.State.Position.ToBlock());
            Assert.True(server.World.GetBlock(arrival).IsAir && !server.World.GetBlock(new Vector3i(arrival.X, arrival.Y - 1, arrival.Z)).IsAir);

            // Nobody lives here: no crew, no visiting trader, no NPC at the old trading post — not even after a while.
            for (int i = 0; i < 10; i++)
            {
                server.TickForTest(0.5);
            }

            Assert.Equal(0, server.NpcCount);
            Assert.Empty(server.SpaceStationMarkers.Where(m => m.Type is "vendor" or "mission_board"));

            // The broken trading post: a station_vendor block, but nothing trades there.
            var post = server.DerelictCellToWorldForTest(id, layout.TraderPosts[0]);
            Assert.Equal("station_vendor", _content.BlockById(server.World.GetBlock(post))!.Key);
            p.State.Position = new Vector3f(post.X + 0.5f, post.Y, post.Z + 1.5f);
            Assert.False(server.NearSpaceStationVendor(p.State));

            // The hooks of the later phases resolve into the interior world once it is stamped.
            var crates = server.DerelictCrateSpotsForTest(id);
            Assert.Equal(layout.CrateSpots.Count, crates.Count);
            Assert.NotNull(server.DerelictSpawnerSpotForTest(id));
            Assert.All(crates, c => Assert.Equal("crate", _content.BlockById(server.World.GetBlock(c))!.Key)); // #2633: the old supply crates

            // Nobody's home: a heal tank set up aboard (on top of a crate) does not take a home spawn.
            p.State.InstantBuild = true;
            var spot = new Vector3i(crates[0].X, crates[0].Y + 1, crates[0].Z);
            p.State.Position = new Vector3f(spot.X + 0.5f, spot.Y, spot.Z + 2.4f); // beside it, not in the cell
            server.PlaceBlock("Justus", spot.X, spot.Y, spot.Z, "heal_tank");
            Assert.Equal("heal_tank", _content.BlockById(server.World.GetBlock(spot))!.Key);
            server.SetSpawnPoint("Justus", spot.X, spot.Y, spot.Z);
            Assert.True(string.IsNullOrEmpty(p.State.CustomSpawnBodyId), "a derelict is never a home spawn");
            server.Stop();
        }
    }

    [Fact]
    public void TradingRules_NeverCountADerelict()
    {
        var server = NewServer("drl_trade", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot");
            string id = GuaranteedDerelict(server);
            server.EnterSpace("Pilot");
            Assert.Contains(server.SpaceEntitiesFor("Pilot"), e => e.Id == id && e.Kind == CombatEntityKind.SpaceStation);

            // The dock lesson never points at it, even from right beside its hull.
            var contact = server.SpaceEntitiesFor("Pilot").First(e => e.Id == id);
            server.ShipMove("Pilot", contact.Position.X, contact.Position.Y, contact.Position.Z - 30f);
            Assert.NotEqual(id, server.NearestStationForTest(p));
            server.Stop();
        }
    }

    [Fact]
    public void NoRoomBreathes_NotEvenAClosedOne_TheTankDrains_VegaAndTheWarningSaySo_AndFallingOutIsASpacewalk()
    {
        var transport = new RecordingTransport();
        var server = NewServer("drl_air", out var repo, transport: transport);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            string id = BoardGuaranteed(server, p);
            var arrival = p.State.Position;

            // #2675: the command room is closed all round — and still holds no air: the life support is dead.
            Assert.True(server.StationCellSealedForTest(id, arrival.ToBlock()), "the command room is a closed room");
            p.State.Oxygen = 40f;
            for (int i = 0; i < 4; i++)
            {
                p.State.Position = arrival;
                server.TickForTest(0.5);
            }

            Assert.True(p.State.Oxygen < 40f, $"a closed room aboard a derelict gives no air (O2 {p.State.Oxygen})");
            Assert.Equal(0, p.State.LifeSupportSource);

            // Told once: the warning names the suit tank, and VEGA's first-boarding hint says there is no air aboard.
            Assert.Single(transport.Lines(), l => l == "@srv.derelict.no_air");
            Assert.Contains("vega.hint." + SvGameServer.DerelictNoAirHint, transport.Vega());
            Assert.DoesNotContain("vega.derelict.boarded", transport.Vega()); // the hint stands in for the plain line
            foreach (var code in new[] { "en", "de" })
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", code + ".json")));
                string tank = code == "de" ? "Anzugtank" : "suit tank";
                Assert.Contains(tank, doc.RootElement.GetProperty("srv.derelict.no_air").GetString(), StringComparison.Ordinal);
                Assert.Contains(tank, doc.RootElement.GetProperty("vega.hint.derelict_no_air").GetString(), StringComparison.Ordinal);
            }

            var breached = BreachedDeckCell(server, id);
            float o2 = p.State.Oxygen;
            for (int i = 0; i < 4; i++)
            {
                p.State.Position = new Vector3f(breached.X + 0.5f, breached.Y, breached.Z + 0.5f);
                server.TickForTest(0.5);
            }

            Assert.True(p.State.Oxygen < o2, $"a breached room holds no air (O2 {o2} → {p.State.Oxygen})");
            Assert.Single(transport.Lines(), l => l == "@srv.derelict.no_air"); // still told only once

            // Back to the ship: its life support fills the tank up again.
            server.LeaveStation("Justus");
            Assert.False(server.InStation("Justus"));
            float drained = p.State.Oxygen;
            for (int i = 0; i < 4; i++)
            {
                server.TickForTest(0.5);
            }

            Assert.True(p.State.Oxygen > drained, $"the ship refills the suit tank (O2 {drained} → {p.State.Oxygen})");

            // Aboard again: the warning comes once more, VEGA's hint does not.
            var contact = server.SpaceEntitiesFor("Justus").First(e => e.Id == id);
            server.ShipMove("Justus", contact.Position.X, contact.Position.Y, contact.Position.Z - (server.DerelictLayoutForTest(id)!.Length / 2f + 8f));
            server.BoardStation("Justus", id);
            Assert.True(server.InStation("Justus"));
            server.TickForTest(0.5);
            Assert.Equal(2, transport.Lines().Count(l => l == "@srv.derelict.no_air"));
            Assert.Single(transport.Vega(), k => k == "vega.hint." + SvGameServer.DerelictNoAirHint);
            arrival = p.State.Position;

            // Out through a hole and away: no fall for ever — since #2636 a real spacewalk beside the hull
            // (DerelictSpacewalkTests has the details).
            p.State.Position = new Vector3f(arrival.X, arrival.Y - 120f, arrival.Z);
            for (int i = 0; i < 4; i++)
            {
                server.TickForTest(0.5);
            }

            Assert.False(server.InStation("Justus"));
            Assert.True(server.InSpace("Justus") && p.State.InEva, "falling out of the hull is a spacewalk");
            server.Stop();
        }
    }

    [Fact]
    public void MinedBlocks_StayMined_AcrossARestart_TheHullFollows_AndTheBrokenPostDropsAVendor()
    {
        string id;
        Vector3i post, postCell;
        {
            var s1 = NewServer("drl_mine", out var repo1);
            using (repo1)
            {
                var p = s1.AddLocalPlayer("Justus");
                id = BoardGuaranteed(s1, p);
                Equip(p, "diamond_drill");
                var layout = s1.DerelictLayoutForTest(id)!;
                postCell = layout.TraderPosts[0];
                post = s1.DerelictCellToWorldForTest(id, postCell);
                p.State.Position = new Vector3f(post.X + 0.5f, post.Y, post.Z + 1.5f);
                s1.MineBlock("Justus", post.X, post.Y, post.Z);

                Assert.True(s1.World.GetBlock(post).IsAir);
                Assert.Equal(1, p.State.Inventory.CountOf("station_vendor")); // the old post still works once rebuilt
                Assert.False(s1.StationCellsForTest(id).ContainsKey(postCell)); // the hull seen from space lost it too
                s1.Stop();
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var s2 = NewServer("drl_mine", out var repo2);
        using (repo2)
        {
            Assert.NotNull(s2.DerelictLayoutForTest(id)); // builds it (a derelict is built on first need)
            Assert.NotEmpty(s2.StationCellsForTest(id));
            Assert.False(s2.StationCellsForTest(id).ContainsKey(postCell), "the derelict's build keeps the mined cell after a restart");
            var p = s2.AddLocalPlayer("Justus");
            s2.LeaveStation("Justus"); // the rejoin may have put the player back aboard
            Assert.False(s2.InStation("Justus"));
            BoardGuaranteed(s2, p);
            Assert.True(s2.World.GetBlock(post).IsAir, "the interior keeps the mined post after a restart");
            s2.Stop();
        }
    }

    /// <summary>#2674: a boarded (stored) beta wreck stays a beta wreck across a restart; a derelict nobody stored is a beta
    /// wreck; one a save stored before the beta stations (its row, no beta marker) keeps its old layout and its hull — and
    /// breathes no more than a new one (#2675).</summary>
    [Fact]
    public void AStoredBetaWreck_StaysBeta_AndAnOldStoredDerelict_KeepsItsOldLayout_WithoutAir()
    {
        string id;
        {
            var s1 = NewServer("drl_shape_a", out var repo1);
            using (repo1)
            {
                var p = s1.AddLocalPlayer("Justus");
                id = BoardGuaranteed(s1, p);
                Assert.True(s1.DerelictLayoutForTest(id)!.IsBeta);
                Assert.Contains("derelict-shape:beta:" + id, s1.Metadata.GeneratedLoot);
                s1.Stop();
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var s2 = NewServer("drl_shape_a", out var repo2);
        using (repo2)
        {
            Assert.True(s2.DerelictLayoutForTest(id)!.IsBeta, "a stored beta wreck is rebuilt as one");
            s2.Stop();
        }

        // A world whose derelict was entered before #2674: a row without the beta marker.
        DerelictLayout old;
        {
            var b1 = NewServer("drl_shape_b", out var repoB1);
            using (repoB1)
            {
                id = GuaranteedDerelict(b1);
                Assert.True(b1.DerelictLayoutForTest(id)!.IsBeta, "a derelict nobody stored is a beta wreck");
                old = DerelictGenerator.GenerateLegacy(b1.Metadata.Seed ^ WorldGenerator.StableHash("derelict:" + id), _content, proceduralOnly: true);
                string system = StartSystemOf(b1);
                b1.Stop();
                var cells = new System.Text.StringBuilder();
                for (int x = 0; x < old.Width; x++)
                    for (int y = 0; y < old.Height; y++)
                        for (int z = 0; z < old.Length; z++)
                        {
                            if (old.Get(x, y, z) != 0)
                            {
                                cells.Append(cells.Length > 0 ? ";" : string.Empty).Append($"{x}:{y}:{z}:{old.Get(x, y, z)}");
                            }
                        }

                repoB1.SaveSpaceStructure(new StoredSpaceStructure { Id = id, Location = system, Boardable = true, Blocks = cells.ToString() });
                repoB1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var b2 = NewServer("drl_shape_b", out var repoB2);
        using (repoB2)
        {
            var layout = b2.DerelictLayoutForTest(id)!;
            Assert.False(layout.IsBeta, "an old stored derelict keeps its old layout");
            Assert.Equal(old.Source, layout.Source);
            Assert.Equal(old.SpawnerSpot, layout.SpawnerSpot);

            var p = b2.AddLocalPlayer("Justus");
            b2.LeaveStation("Justus");
            BoardGuaranteed(b2, p);
            Assert.Equal(b2.DerelictCellToWorldForTest(id, old.Arrival), p.State.Position.ToBlock()); // its old command room
            p.State.Oxygen = 40f;
            for (int i = 0; i < 4; i++)
            {
                b2.TickForTest(0.5);
            }

            Assert.True(p.State.Oxygen < 40f, "an old derelict's life support is dead too");
            Assert.DoesNotContain("derelict-shape:beta:" + id, b2.Metadata.GeneratedLoot); // boarding it wrote no beta marker
            b2.Stop();
        }
    }

    [Fact]
    public void Spacewalk_MinesTheHull_AndTheInteriorFollows()
    {
        var server = NewServer("drl_eva", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            string id = GuaranteedDerelict(server);
            server.EnterSpace("Justus");
            Equip(p, "diamond_drill");
            p.State.InEva = true;
            var contact = server.SpaceEntitiesFor("Justus").First(e => e.Id == id);
            server.ShipMove("Justus", contact.Position.X, contact.Position.Y + 12f, contact.Position.Z);

            // An outer cell: the highest solid cell of the build.
            var cells = server.StationCellsForTest(id);
            var target = cells.Keys.OrderByDescending(c => c.Y).ThenBy(c => c.X).ThenBy(c => c.Z).First();
            for (int i = 0; i < 12 && cells.ContainsKey(target); i++)
            {
                server.HandleStructureEditForTest("Justus", new StructureEditIntent { StructureId = id, X = target.X, Y = target.Y, Z = target.Z, Mine = true });
            }

            Assert.False(server.StationCellsForTest(id).ContainsKey(target), "anyone may salvage a derelict's hull on a spacewalk");

            // Boarding later stamps the build as it is now.
            p.State.InEva = false;
            server.ShipMove("Justus", contact.Position.X, contact.Position.Y, contact.Position.Z - 30f);
            server.BoardStation("Justus", id);
            Assert.True(server.InStation("Justus"));
            Assert.True(server.World.GetBlock(server.DerelictCellToWorldForTest(id, target)).IsAir);
            server.Stop();
        }
    }

    [Fact]
    public void TheLongQuiet_StillBreathes_AndCatchesABoarderWhoFallsOffItsOpenCorner()
    {
        var server = NewServer("drl_quiet", out var repo, seed: 5);
        using (repo)
        {
            string host = server.UniqueSiteBodyForTest("derelict_host")!;
            var p = server.AddLocalPlayer("Salvager");
            if (p.CurrentLocationId != host)
            {
                if (!server.Ship.Modules.Contains("jump_generator"))
                {
                    server.Ship.Modules.Add("jump_generator");
                }

                server.Travel("Salvager", host);
                Assert.Equal(host, p.CurrentLocationId);
            }

            server.EnterSpace("Salvager");
            var contact = server.SpaceEntitiesFor("Salvager").First(e => e.Id == SvGameServer.DerelictStationId);
            server.ShipMove("Salvager", contact.Position.X, contact.Position.Y, contact.Position.Z - 6f);
            server.BoardStation("Salvager", SvGameServer.DerelictStationId);
            Assert.True(server.InStation("Salvager"));
            var deck = p.State.Position;

            // #2675: the story site keeps its blanket air — the tank fills up aboard.
            p.State.Oxygen = 40f;
            for (int i = 0; i < 4; i++)
            {
                p.State.Position = deck;
                server.TickForTest(0.5);
            }

            Assert.True(p.State.Oxygen > 40f, $"The Long Quiet still breathes (O2 {p.State.Oxygen})");

            // Walked off the torn stern corner and fell: before #2632 nothing ever caught this.
            p.State.Position = new Vector3f(deck.X + 10f, deck.Y - 150f, deck.Z + 6f);
            for (int i = 0; i < 4; i++)
            {
                server.TickForTest(0.5);
            }

            Assert.True(Math.Abs(p.State.Position.Y - deck.Y) < 4f, $"expected the boarder back on the deck, at {p.State.Position}");
            server.Stop();
        }
    }

    [Fact]
    public void WorldOption_ReadsFromTheCommandLine()
    {
        var config = new ServerConfig();
        Assert.Equal(Frequency.Normal, config.World.Derelicts);
        var applied = config.ApplyCommandLine(new[] { "--derelicts", "off" });
        Assert.Contains("derelicts", applied);
        Assert.Equal(Frequency.Off, config.World.Derelicts);
    }

    /// <summary>A deck cell of the guaranteed derelict that sits under the station's roof in a pocket that is not closed — a
    /// room the decay opened to space (the hangar at the least).</summary>
    private static Vector3i BreachedDeckCell(SvGameServer server, string id)
    {
        var layout = server.DerelictLayoutForTest(id)!;
        for (int x = 1; x < layout.Width - 1; x++)
            for (int z = 1; z < layout.Length - 1; z++)
                for (int y = 1; y < layout.Height - 2; y++)
                {
                    bool deck = layout.Get(x, y, z) == 0 && layout.Get(x, y + 1, z) == 0 && layout.Get(x, y - 1, z) != 0;
                    bool roofed = Enumerable.Range(2, 6).Any(k => layout.Get(x, y + k, z) != 0);
                    if (!deck || !roofed)
                    {
                        continue;
                    }

                    var w = server.DerelictCellToWorldForTest(id, new Vector3i(x, y, z));
                    if (server.World.GetBlock(w).IsAir && !server.StationCellSealedForTest(id, w))
                    {
                        return w;
                    }
                }

        throw new InvalidOperationException("the derelict has no breached room");
    }
}
