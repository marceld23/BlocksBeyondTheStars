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
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The escort cruiser's wreck (#3902, canon C22): the template (loads, validates, never rolled), the FRESH ruin option (no
/// soot, no scrap, red lamps left burning — and every other wreck exactly as before), the runtime registration when the boss
/// falls (one boardable wreck where it fell, one per system, back after a restart with the players' changes), life aboard
/// (the guardian bay, the chests, a crate for every pilot who fought, the boarding event) and the bridge console — locked
/// red without a hacking chip, blue with one, ECHO's goodbye on the story's cruiser and a ship's log on every other.
/// </summary>
public sealed class CruiserWreckTests : IDisposable
{
    private const string Alice = "Alice";
    private const string Bob = "Bob";
    private const string Cannon = "ship_cannon_1";

    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();
    private readonly List<SvGameServer> _servers = new();

    public CruiserWreckTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_cruiser_wreck_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        foreach (var server in _servers)
        {
            server.Stop();
        }

        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // a temp folder the OS still holds — the next run uses a new one
        }
    }

    // ---------------- helpers ----------------

    /// <summary>A server (survival, PvE in space, no ambient hostiles) — with the VEGA story when <paramref name="story"/>.</summary>
    private (SvGameServer Server, RecordingTransport Transport) Start(string name, bool story = false)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig { WorldName = name, Seed = 1, StartPlanet = "rocky", AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.SpaceCombat = SpaceCombatMode.PvE;
        config.Rules.ShipWeapons = ShipWeaponMode.NpcsOnly;
        config.Rules.SpaceNpcEnemies = AlienActivity.Off;
        config.Rules.Bandits = AlienActivity.Off;
        if (!story)
        {
            config.Rules.StoryId = StoryRegistry.NoneStoryId;
        }

        var transport = new RecordingTransport();
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        _servers.Add(server);
        return (server, transport);
    }

    /// <summary>Stops a server and releases its save, so a second one can open the same world.</summary>
    private void Stop(SvGameServer server)
    {
        server.Stop();
        _servers.Remove(server);
        foreach (var repo in _repos)
        {
            repo.Flush();
            repo.Dispose();
        }

        _repos.Clear();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    private static PlayerSession Fly(SvGameServer server, string pilot, float z)
    {
        var s = server.AddLocalPlayer(pilot);
        s.State.GodMode = true; // the guardians aboard are counted, not fought
        s.Ships[s.ActiveShipId].Modules.Add(Cannon);
        server.EnterSpace(pilot);
        Assert.True(server.InSpace(pilot));
        server.ShipMove(pilot, 0f, 0f, z);
        return s;
    }

    /// <summary>The boss between the pilots (standing still, its guns silent), shot down by every pilot named — the last
    /// one fires the last shot. Returns the wreck it leaves.</summary>
    private static string Defeat(SvGameServer server, bool carriesEcho, params string[] fighters)
    {
        var before = server.CruiserWreckIdsForTest;
        var boss = server.SpawnEscortCruiserForTest(fighters[0], carriesEcho, new Vector3f(0f, 0f, 30f))!;
        boss.Boss!.Speed = 0.001f;
        boss.DamagePerSecond = 0f;
        boss.HeavyDamage = 0f;
        boss.Shield = 0f;
        foreach (string fighter in fighters)
        {
            if (fighter == fighters[^1])
            {
                boss.Hull = 1f;
            }

            server.FireWeapon(fighter, Cannon, boss.Id);
        }

        Assert.Null(server.SpaceBossForTest(fighters[0]));
        return Assert.Single(server.CruiserWreckIdsForTest, id => !before.Contains(id));
    }

    /// <summary>Flies the pilot beside the wreck's hull and boards it.</summary>
    private static void Board(SvGameServer server, string pilot, string id)
    {
        if (!server.InSpace(pilot))
        {
            server.EnterSpace(pilot);
        }

        var wreck = server.CruiserWreckForTest(id)!.Value;
        server.ShipMove(pilot, wreck.Position.X, wreck.Position.Y, wreck.Position.Z + wreck.Half.Z + 6f);
        server.BoardStation(pilot, id);
        Assert.True(server.InStation(pilot), "a cruiser's wreck is boardable by anyone");
    }

    private string KeyAt(SvGameServer server, Vector3i cell) => _content.BlockById(server.World.GetBlock(cell))?.Key ?? "air";

    private string BuildKeyAt(SvGameServer server, string id, Vector3i cell)
        => server.StationCellsForTest(id).TryGetValue(cell, out var b) ? _content.BlockById(b)?.Key ?? "?" : "air";

    private StructureTemplate Cruiser => _content.GetGiantShip(CruiserWreckRules.TemplateKey)
        ?? throw new Xunit.Sdk.XunitException("data/giant_ships/escort_cruiser.json is missing or was dropped at load");

    // ---------------- the template ----------------

    [Fact]
    public void TheTemplate_LoadsAndValidates_HasItsBridge_AndIsNeverRolled()
    {
        var t = Cruiser;
        Assert.Empty(_content.GiantShipProblems(t));
        Assert.True(t.PinOnly);
        Assert.True(t.GiantShip);
        Assert.InRange(t.Length, 60, 95); // a hull of 96 or more is broken in two by the ruin pass
        Assert.InRange(t.Width, 20, 40);
        Assert.InRange(t.Height, 16, 32);
        Assert.Single(t.Cells, c => c.Kind == "marker" && c.Id == GiantShipRules.ConsoleMarker);
        Assert.Equal(2, t.Cells.Count(c => c.Kind == "marker" && c.Id == GiantShipRules.LampMarker));
        Assert.InRange(t.Cells.Count(c => c.Kind == "marker" && c.Id == LandmarkRules.ChestMarker), 4, 6);
        Assert.True(t.Cells.Count(c => c.Kind == "marker" && c.Id == GiantShipRules.EntranceMarker) >= 1);
        Assert.DoesNotContain(t.Palette, p => p.Id is "sps_console" or "guardian_spawner" or "capsule_tank"); // placed by the decay, never authored

        // Never a planet wreck and never a giant wreck in space: no pool offers a pin-only ship.
        Assert.DoesNotContain(_content.GiantShipsFor(null, null), s => s.Key == t.Key);
        foreach (var planet in _content.Planets.Keys)
        {
            Assert.DoesNotContain(_content.GiantShipsFor(null, planet), s => s.Key == t.Key);
            Assert.DoesNotContain(_content.GiantShipsFor(null, planet, namedTypeOnly: true), s => s.Key == t.Key);
        }

        var pool = _content.GiantShipsFor(null, null);
        for (long seed = 0; seed < 200; seed++)
        {
            Assert.NotEqual(t.Key, GiantWreckPlacer.PickDesign(pool, seed, GiantWreckPlacer.BodyId("sys0", 0))?.Key);
        }

        var en = _content.CreateLocalizer(GameLocale.English);
        var de = _content.CreateLocalizer(GameLocale.German);
        foreach (string key in new[] { GiantShipRules.NameKey(t.Key), GiantShipRules.DescriptionKey(t.Key), CruiserWreckRules.NameKey })
        {
            Assert.True(en.Has(key) && de.Has(key), key);
        }
    }

    [Fact]
    public void TheBridgeMarkers_AreAWarshipsOnly_AndStayOutOfTheEditorsPalette()
    {
        Assert.DoesNotContain(GiantShipRules.ConsoleMarker, GiantShipRules.Markers);
        Assert.DoesNotContain(GiantShipRules.LampMarker, GiantShipRules.Markers);
        Assert.True(GiantShipRules.IsShipMarker(GiantShipRules.ConsoleMarker) && GiantShipRules.IsShipMarker(GiantShipRules.LampMarker));
        Assert.False(GiantShipRules.IsShipMarker("vendor"));
        Assert.Equal(SpsTowerRules.ConsoleMarker, GiantShipRules.ConsoleMarker); // one word for the Service's console
        Assert.Equal(SpsTowerRules.LampMarker, GiantShipRules.LampMarker);
        foreach (var ship in _content.GiantShips.Where(s => s.Key != CruiserWreckRules.TemplateKey))
        {
            Assert.DoesNotContain(ship.Cells, c => c.Kind == "marker" && GiantShipRules.BridgeMarkers.Contains(c.Id));
        }
    }

    // ---------------- the fresh option ----------------

    /// <summary>The ship-mode decay of shipped ships, pinned (block keys, dyes, shapes, doors, arrival, bay) — the hashes the
    /// build BEFORE the fresh option and the bridge markers produced (#3902 measured all six shipped ships with three seeds
    /// each against it): neither may ever move a wreck that has no bridge and is not fresh.</summary>
    [Theory]
    [InlineData("sample_hulk", 1L, "35e530f0cc6d5545")]
    [InlineData("sample_hulk", 7L, "a4d10c239d058b69")]
    [InlineData("sample_hulk", 20261114L, "171370d6dbd80053")]
    [InlineData("battleship", 1L, "55067bf487f27cf1")]
    [InlineData("carrier", 7L, "d07730324d37d469")]
    public void WithoutTheFreshOption_AWreckComesOutAsItAlwaysDid(string key, long seed, string golden)
    {
        var t = _content.GetGiantShip(key)!;
        var plain = DerelictGenerator.GenerateShip(GiantShipGenerator.BuildSpace(t, seed, _content), seed, _content, alien: false);
        var named = DerelictGenerator.GenerateShip(GiantShipGenerator.BuildSpace(t, seed, _content, abandonBeds: false, fresh: false), seed, _content,
            alien: false, fresh: false);
        Assert.Equal(plain.BaseHash, named.BaseHash);
        Assert.Null(plain.Console);
        Assert.Empty(plain.Lamps);
        Assert.Equal(golden, plain.BaseHash);
    }

    [Fact]
    public void AFreshWreck_HasHolesButNoSootAndNoScrap_AndSomeOfItsLampsStillBurnRed()
    {
        var t = Cruiser;
        ushort Id(string key) => _content.GetBlock(key)!.NumericId.Value;
        ushort red = Id("light_red"), console = Id("sps_console"), scrapMetal = Id("scrap_metal"), scrapPile = Id("scrap_pile"),
            bay = Id(SvGameServer.GuardianBayBlockKey);
        foreach (long seed in new long[] { 1, 7, 20261121 })
        {
            var space = GiantShipGenerator.BuildSpace(t, seed, _content, fresh: true);
            var wreck = DerelictGenerator.GenerateShip(space, seed, _content, alien: false, fresh: true);
            var layout = wreck.Layout;
            Assert.True(space.SolidCells < space.IntactCells, "the ruin pass still tears holes");
            Assert.True(space.SolidCells > space.IntactCells * 0.6, "…but the ship is still a ship");

            int lit = 0, soot = 0, scrap = 0, off = 0;
            for (int x = 0; x < layout.Width; x++)
                for (int y = 0; y < layout.Height; y++)
                    for (int z = 0; z < layout.Length; z++)
                    {
                        ushort b = layout.Get(x, y, z);
                        if (b == 0)
                        {
                            continue;
                        }

                        lit += b == red ? 1 : 0;
                        scrap += b == scrapMetal || b == scrapPile ? 1 : 0;
                        soot += layout.GetModifier(x, y, z).Tint == GiantShipGenerator.SootTint ? 1 : 0;
                        off += _content.BlockById(new BlockId(b))!.Key.EndsWith("_off", StringComparison.Ordinal) ? 1 : 0;
                    }

            Assert.Equal(0, soot);
            Assert.Equal(0, scrap);
            Assert.True(lit >= 4, $"seed {seed}: only {lit} red lamps burn");
            Assert.True(off >= 4, $"seed {seed}: only {off} lamps are dead — not every light survives a fight");

            // The bridge: the console on its deck, facing the room, a red lamp in each lamp cell; one guardian bay.
            Assert.True(wreck.Console.HasValue, $"seed {seed}: no console");
            var at = wreck.Console!.Value;
            Assert.Equal(console, layout.Get(at.X, at.Y, at.Z));
            Assert.NotEqual(0, layout.Get(at.X, at.Y - 1, at.Z));
            Assert.Equal(0, layout.Get(at.X, at.Y + 1, at.Z));
            Assert.Equal(2, wreck.Lamps.Count);
            Assert.All(wreck.Lamps, l => Assert.Equal(red, layout.Get(l.X, l.Y, l.Z)));
            Assert.True(layout.SpawnerSpot.HasValue, $"seed {seed}: no guardian bay ({layout.RoomCount} walkable room(s))");
            var spot = layout.SpawnerSpot!.Value;
            Assert.Equal(bay, layout.Get(spot.X, spot.Y, spot.Z));
            Assert.InRange(wreck.Chests.Count, 4, 6);

            // Deterministic, and another wreck than the weathered one of the same seed.
            var again = DerelictGenerator.GenerateShip(GiantShipGenerator.BuildSpace(t, seed, _content, fresh: true), seed, _content, alien: false, fresh: true);
            Assert.Equal(wreck.BaseHash, again.BaseHash);
            var weathered = DerelictGenerator.GenerateShip(GiantShipGenerator.BuildSpace(t, seed, _content), seed, _content, alien: false);
            Assert.NotEqual(wreck.BaseHash, weathered.BaseHash);
        }
    }

    // ---------------- the wreck in flight ----------------

    [Fact]
    public void ADefeatedBoss_LeavesExactlyOneBoardableWreck_WhereItFell()
    {
        var (server, transport) = Start("cw_one");
        var alice = Fly(server, Alice, 0f);
        string system = server.Galaxy.FindBody(alice.CurrentLocationId)!.SystemId;
        Assert.Empty(server.CruiserWreckIdsForTest);
        transport.Sent.Clear();

        string id = Defeat(server, carriesEcho: false, Alice);

        Assert.True(CruiserWreckRules.IsCruiserWreckId(id));
        Assert.StartsWith(DerelictPlacer.IdPrefix, id); // a derelict by its id: no air, no shield, the guardian bay
        Assert.False(SpaceCombatRules.StationHasShield(id));
        var record = Assert.Single(server.Metadata.CruiserWrecks);
        Assert.Equal((id, system, 1, false, false), (record.Id, record.SystemId, record.Serial, record.CarriesEcho, record.Replaced));
        Assert.Equal(new[] { Alice }, record.Fighters);

        // In the flight at once: the contact, the hull's design, and a body on the star map of its system.
        var contact = Assert.Single(server.SpaceEntitiesFor(Alice), e => e.Id == id);
        Assert.Equal(CombatEntityKind.SpaceStation, contact.Kind);
        Assert.Contains(transport.To<SpaceShipDesign>(alice), d => d.Id == id);
        var body = server.Galaxy.FindBody(id)!;
        Assert.Equal((CelestialKind.SpaceStation, system), (body.Kind, body.SystemId));
        var net = transport.To<StarMapData>(alice).Last().Systems.SelectMany(s => s.Bodies).Single(b => b.Id == id);
        Assert.Equal(_content.CreateLocalizer(GameLocale.English).Get(CruiserWreckRules.NameKey), net.Name);

        // Where it fell (0, 0, 30): raised clear of the planet spheres, pushed clear of the launch column and of every hull.
        var wreck = server.CruiserWreckForTest(id)!.Value;
        Assert.Equal((record.X, record.Y, record.Z), (wreck.Position.X, wreck.Position.Y, wreck.Position.Z));
        Assert.True(wreck.Position.Y - wreck.Half.Y >= 44f - 0.01f, "its lowest block floats over the planet spheres");
        Assert.InRange(MathF.Abs(wreck.Position.X), wreck.Half.X + 19.9f, 400f);
        Assert.InRange(wreck.Position.Z, -400f, 400f);
        var boxes = server.StationHullBoxesForTest(Alice);
        Assert.Contains(id, boxes.Keys);
        foreach (var (other, box) in boxes.Where(b => b.Key != id))
        {
            bool apart = MathF.Abs(box.Centre.X - wreck.Position.X) >= box.Half.X + wreck.Half.X + 29.9f
                         || MathF.Abs(box.Centre.Y - wreck.Position.Y) >= box.Half.Y + wreck.Half.Y + 29.9f
                         || MathF.Abs(box.Centre.Z - wreck.Position.Z) >= box.Half.Z + wreck.Half.Z + 29.9f;
            Assert.True(apart, $"the wreck keeps clear of '{other}'");
        }

        // A later flight of the system holds it too; boarding works, and there is no air aboard.
        server.LeaveSpace(Alice);
        server.EnterSpace(Alice);
        Assert.Single(server.SpaceEntitiesFor(Alice), e => e.Id == id);
        Board(server, Alice, id);
        Assert.Equal("station:" + id, alice.CurrentLocationId);
    }

    [Fact]
    public void TheFlightSpot_KeepsClearOfTheLaunchColumn_ThePlanets_AndEveryHull()
    {
        var half = new Vector3f(15.5f, 12.5f, 44f);
        var free = SvGameServer.CruiserWreckFlightPosition(new Vector3f(200f, 80f, -300f), half, Array.Empty<(Vector3f, Vector3f)>());
        Assert.Equal((200f, 80f, -300f), (free.X, free.Y, free.Z)); // nothing in the way: exactly where it fell

        var low = SvGameServer.CruiserWreckFlightPosition(new Vector3f(200f, -5f, -300f), half, Array.Empty<(Vector3f, Vector3f)>());
        Assert.Equal((200f, 44f + half.Y, -300f), (low.X, low.Y, low.Z));

        var launch = SvGameServer.CruiserWreckFlightPosition(new Vector3f(-3f, 0f, 10f), half, Array.Empty<(Vector3f, Vector3f)>());
        Assert.Equal(-(half.X + 20f), launch.X); // to the side it fell on
        Assert.Equal(10f, launch.Z);

        var station = (Centre: new Vector3f(150f, 60f, 100f), Half: new Vector3f(20f, 15f, 20f));
        var pushed = SvGameServer.CruiserWreckFlightPosition(new Vector3f(140f, 50f, 100f), half, new[] { station });
        bool apart = MathF.Abs(pushed.X - station.Centre.X) >= half.X + station.Half.X + 30f - 0.01f
                     || MathF.Abs(pushed.Z - station.Centre.Z) >= half.Z + station.Half.Z + 30f - 0.01f;
        Assert.True(apart);
    }

    [Fact]
    public void ASecondWreckInTheSameSystem_ReplacesTheFirst_AndItsDataIsRemoved()
    {
        var (server, _) = Start("cw_replace");
        var alice = Fly(server, Alice, 0f);
        alice.State.Inventory.Add(CruiserWreckRules.ChipItem, 1, 16);
        string first = Defeat(server, carriesEcho: false, Alice);
        Board(server, Alice, first);
        Assert.True(server.UseBridgeConsoleForTest(Alice)); // a stored change of the first wreck (its lamps are blue)
        server.LeaveStation(Alice);
        var repo = _repos[0];
        Assert.Contains(repo.ListStructureOverlays(SvGameServer.DerivedDeltaLocation), o => o.Key == "delta:" + first);
        Assert.NotEmpty(repo.ListStructureOverlays("station:" + first));
        Assert.Contains(server.Metadata.GeneratedLoot, k => k.StartsWith(SvGameServer.GiantChestLootPrefix + first + ":", StringComparison.Ordinal));

        if (!server.InSpace(Alice))
        {
            server.EnterSpace(Alice);
        }

        server.ShipMove(Alice, 0f, 0f, 0f);
        for (int i = 0; i < 50; i++)
        {
            server.TickForTest(0.1); // the cannon cools down
        }

        server.ShipMove(Alice, 0f, 0f, 0f);
        string second = Defeat(server, carriesEcho: false, Alice);

        Assert.NotEqual(first, second);
        Assert.Equal(new[] { second }, server.CruiserWreckIdsForTest);
        Assert.Equal(2, Assert.Single(server.Metadata.CruiserWrecks).Serial);
        Assert.Null(server.Galaxy.FindBody(first));
        Assert.DoesNotContain(server.SpaceEntitiesFor(Alice), e => e.Id == first);
        Assert.Single(server.SpaceEntitiesFor(Alice), e => e.Id == second);
        Assert.Empty(server.StationCellsForTest(first));
        Assert.DoesNotContain(repo.ListStructureOverlays(SvGameServer.DerivedDeltaLocation), o => o.Key == "delta:" + first);
        Assert.Empty(repo.ListStructureOverlays("station:" + first));
        Assert.Empty(repo.ListContainers("station:" + first));
        Assert.DoesNotContain(server.Metadata.GeneratedLoot, k => k.Contains(first, StringComparison.Ordinal));
        Assert.False(server.CruiserWreckRecordForTest(second)!.ConsoleOpened); // nothing of the old one carried over
    }

    [Fact]
    public void ARestart_PutsTheWreckBack_WithThePlayersChanges()
    {
        string id;
        Vector3f position;
        List<Vector3i> lamps;
        {
            var (s1, _) = Start("cw_restart");
            var alice = Fly(s1, Alice, 0f);
            alice.State.Inventory.Add(CruiserWreckRules.ChipItem, 1, 16);
            id = Defeat(s1, carriesEcho: false, Alice);
            var wreck = s1.CruiserWreckForTest(id)!.Value;
            position = wreck.Position;
            lamps = wreck.Lamps.ToList();
            Board(s1, Alice, id);
            Assert.True(s1.UseBridgeConsoleForTest(Alice));
            Assert.All(lamps, l => Assert.Equal("light_blue", BuildKeyAt(s1, id, l)));
            s1.LeaveStation(Alice);
            Stop(s1);
        }

        var (s2, _) = Start("cw_restart");
        Assert.Equal(new[] { id }, s2.CruiserWreckIdsForTest);
        Assert.NotNull(s2.Galaxy.FindBody(id));
        var back = s2.CruiserWreckForTest(id)!.Value;
        Assert.Equal((position.X, position.Y, position.Z), (back.Position.X, back.Position.Y, back.Position.Z));
        Assert.All(lamps, l => Assert.Equal("light_blue", BuildKeyAt(s2, id, l))); // the stored changes lie over the rebuilt hull
        Assert.True(s2.DerivedDeltaCountForTest(id) >= 2);
        var record = s2.CruiserWreckRecordForTest(id)!;
        Assert.True(record.ConsoleOpened);
        Assert.Contains(Alice, record.CratesGiven);

        var again = Fly(s2, Alice, 0f);
        Assert.Single(s2.SpaceEntitiesFor(Alice), e => e.Id == id);
        Board(s2, Alice, id);
        var console = s2.DerelictCellToWorldForTest(id, back.Console!.Value);
        Assert.Equal("sps_console", KeyAt(s2, console));
        Assert.All(lamps, l => Assert.Equal("light_blue", KeyAt(s2, s2.DerelictCellToWorldForTest(id, l))));
        Assert.Equal("station:" + id, again.CurrentLocationId);
    }

    // ---------------- aboard ----------------

    [Fact]
    public void Aboard_TheChestsFillOnce_TheBayReleasesRobots_AndEveryFighterFindsACrate()
    {
        var (server, transport) = Start("cw_aboard");
        var alice = Fly(server, Alice, 0f);
        var bob = Fly(server, Bob, 60f);
        var carol = Fly(server, "Carol", 600f);
        var heard = new RecordingGameEventListener();
        server.AddGameEventListener(GameEventKeys.CruiserBoarded, heard);
        string id = Defeat(server, carriesEcho: false, Alice, Bob);

        Board(server, Alice, id);
        Assert.Equal((Alice, CruiserWreckRules.BoardedPlain), Assert.Single(heard.Events.Select(e => (e.Player, e.Arg))));
        Assert.Contains("vega.cruiser.wreck_boarded", transport.LineKeys(alice));

        // The template's chests, once: a crate block and a salvage container each.
        var chests = server.GiantWreckChestsForTest(id);
        Assert.InRange(chests.Count, 3, 6);
        Assert.All(chests, c => Assert.NotEmpty(c.Items));

        // A crate for the fighter, owner-locked, near the way in — guardian plates, sometimes a chip.
        var crate = Assert.Single(server.Containers, c => c.Owner == Alice);
        Assert.Contains(crate.Items, i => i.Item == EnemyCatalog.GuardianPlate && i.Count >= 6);
        Assert.All(crate.Items, i => Assert.Contains(i.Item, new[] { EnemyCatalog.GuardianPlate, CruiserWreckRules.ChipItem }));
        Assert.Contains("@srv.cruiser.crate", transport.TextsTo(alice));
        Assert.Contains(Alice, server.CruiserWreckRecordForTest(id)!.CratesGiven);

        // The guardian bay: where the decay put it, and it wakes while someone is aboard.
        var bay = server.DerelictSpawnerSpotForTest(id)!.Value;
        Assert.Equal(SvGameServer.GuardianBayBlockKey, KeyAt(server, bay));
        for (double t = 0; t <= SvGameServer.GuardianFirstReleaseSeconds + 1; t += 1.0)
        {
            server.TickEnemiesForTest(1.0);
        }

        Assert.True(server.GuardiansReleasedForTest(id) >= 1, "only robots are aboard — and they come out of the bay");
        Assert.NotEmpty(server.PlanetEnemies);

        // Re-boarding refills nothing and lays no second crate; Bob gets his own, Carol (who only watched) none.
        server.LeaveStation(Alice);
        Board(server, Alice, id);
        Assert.Equal(chests.Count, server.GiantWreckChestsForTest(id).Count);
        Assert.Single(server.Containers, c => c.Owner == Alice);
        Board(server, Bob, id);
        Assert.Single(server.Containers, c => c.Owner == Bob);
        Board(server, "Carol", id);
        Assert.DoesNotContain(server.Containers, c => c.Owner == "Carol");
        Assert.DoesNotContain("@srv.cruiser.crate", transport.TextsTo(carol));
        Assert.Equal(new[] { Alice, Bob }, server.CruiserWreckRecordForTest(id)!.CratesGiven.OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(4, heard.Events.Count);
        _ = bob;
    }

    // ---------------- the bridge console ----------------

    [Fact]
    public void TheConsole_IsLockedRedWithoutAChip_AndWithOneTheLampsTurnBlue_Once()
    {
        var (server, transport) = Start("cw_console", story: true);
        var alice = Fly(server, Alice, 0f);
        var freed = new RecordingGameEventListener();
        server.AddGameEventListener(GameEventKeys.EchoFreed, freed);
        string id = Defeat(server, carriesEcho: false, Alice);
        Board(server, Alice, id);
        var wreck = server.CruiserWreckForTest(id)!.Value;
        var lamps = wreck.Lamps.Select(l => server.DerelictCellToWorldForTest(id, l)).ToList();
        Assert.Equal("sps_console", KeyAt(server, server.DerelictCellToWorldForTest(id, wreck.Console!.Value)));
        Assert.All(lamps, l => Assert.Equal("light_red", KeyAt(server, l)));

        // Without a chip: locked, nothing changes.
        Assert.True(server.UseBridgeConsoleForTest(Alice));
        Assert.Contains("@srv.cruiser.console_locked", transport.TextsTo(alice));
        Assert.All(lamps, l => Assert.Equal("light_red", KeyAt(server, l)));
        Assert.False(server.CruiserWreckRecordForTest(id)!.ConsoleOpened);

        // With chips: one is used up, the lamps are blue in the interior and in the hull seen from space.
        alice.State.Inventory.Add(CruiserWreckRules.ChipItem, 3, 16);
        transport.Sent.Clear();
        Assert.True(server.UseBridgeConsoleForTest(Alice));
        Assert.Equal(2, alice.State.Inventory.CountOf(CruiserWreckRules.ChipItem));
        Assert.All(lamps, l => Assert.Equal("light_blue", KeyAt(server, l)));
        Assert.All(wreck.Lamps, l => Assert.Equal("light_blue", BuildKeyAt(server, id, l)));
        Assert.DoesNotContain(server.StationCellsForTest(id).Where(kv => Math.Abs(kv.Key.X - wreck.Console!.Value.X) <= CruiserWreckRules.BridgeLampReach
            && Math.Abs(kv.Key.Z - wreck.Console.Value.Z) <= CruiserWreckRules.BridgeLampReach
            && Math.Abs(kv.Key.Y - wreck.Console.Value.Y) <= CruiserWreckRules.BridgeLampReachY), kv => _content.BlockById(kv.Value)!.Key == "light_red");
        Assert.Contains("@srv.cruiser.console_blue", transport.TextsTo(alice));
        Assert.Contains(transport.To<SoundFx>(alice), fx => fx.SoundId == CruiserWreckRules.LightsToBlueSound);
        Assert.Contains(transport.To<BlockChanged>(alice), c => c.Block == _content.GetBlock("light_blue")!.NumericId.Value);
        var record = server.CruiserWreckRecordForTest(id)!;
        Assert.True(record.ConsoleOpened);
        Assert.False(record.EchoFreed);

        // A later cruiser: one of the ship's logs, never ECHO's record — no core, no event, no ECHO.
        var read = Assert.Single(transport.To<LoreTextRevealed>(alice));
        Assert.Equal(CruiserWreckRules.LoreSite, read.Site);
        Assert.NotEqual(CruiserWreckRules.EchoLoreKey, read.Key);
        Assert.Equal(0, alice.State.Inventory.CountOf(CruiserWreckRules.EchoCoreItem));
        Assert.Empty(freed.Events);
        Assert.DoesNotContain(transport.To<ShipAiLine>(alice), l => SvGameServer.EchoLines.Contains(l.LineKey));

        // Once: a second chip is never taken.
        transport.Sent.Clear();
        Assert.True(server.UseBridgeConsoleForTest(Alice));
        Assert.Equal(2, alice.State.Inventory.CountOf(CruiserWreckRules.ChipItem));
        Assert.Contains("@srv.cruiser.console_done", transport.TextsTo(alice));
        Assert.Empty(transport.To<LoreTextRevealed>(alice));
    }

    [Fact]
    public void OnEchosCruiser_SheSpeaksOnce_EveryoneAboardGetsOneCore_AndVegaReadsItIntoTheJournal()
    {
        var (server, transport) = Start("cw_echo", story: true);
        var alice = Fly(server, Alice, 0f);
        var bob = Fly(server, Bob, 60f);
        var carol = Fly(server, "Carol", 600f);
        var freed = new RecordingGameEventListener();
        server.AddGameEventListener(GameEventKeys.EchoFreed, freed);
        var boarded = new RecordingGameEventListener();
        server.AddGameEventListener(GameEventKeys.CruiserBoarded, boarded);
        string id = Defeat(server, carriesEcho: true, Alice, Bob);
        Assert.True(server.CruiserWreckRecordForTest(id)!.CarriesEcho);

        Board(server, Alice, id);
        Board(server, "Carol", id); // she did not fight, but she is aboard when ECHO speaks
        Assert.All(boarded.Events, e => Assert.Equal(CruiserWreckRules.BoardedEcho, e.Arg));
        alice.State.Inventory.Add(CruiserWreckRules.ChipItem, 1, 16);
        transport.Sent.Clear();

        Assert.True(server.UseBridgeConsoleForTest(Alice));

        var record = server.CruiserWreckRecordForTest(id)!;
        Assert.True(record.ConsoleOpened && record.EchoFreed);
        Assert.NotNull(_content.Stories[StoryRegistry.DefaultStoryId].FindCutscene(CruiserWreckRules.EchoCutscene)); // #3899: the pack's scene
        foreach (var s in new[] { alice, carol })
        {
            Assert.Equal(1, s.State.Inventory.CountOf(CruiserWreckRules.EchoCoreItem));
            Assert.Single(freed.Events, e => e.Player == s.State.PlayerId);
            Assert.Contains("@srv.cruiser.echo_core", transport.TextsTo(s));

            // Her lines always go out with the scene (#3899) — a client that plays it keeps them out of the panel, these
            // local players read them there: under her name, VEGA's two answers under VEGA's — in order.
            var lines = transport.To<ShipAiLine>(s).Where(l => l.LineKey.StartsWith("story.vega.echo.", StringComparison.Ordinal)).ToList();
            Assert.Equal(SvGameServer.EchoLines.Length + SvGameServer.EchoVegaLines.Length, lines.Count);
            Assert.All(lines.Where(l => SvGameServer.EchoLines.Contains(l.LineKey)), l => Assert.Equal("ECHO", l.SpeakerName));
            Assert.All(lines.Where(l => SvGameServer.EchoVegaLines.Contains(l.LineKey)), l => Assert.Equal(string.Empty, l.SpeakerName));
            Assert.Equal(SvGameServer.EchoLines[0], lines[0].LineKey);
            Assert.Equal(SvGameServer.EchoVegaLines[1], lines[^1].LineKey);
        }

        Assert.Empty(transport.To<LoreTextRevealed>(alice).Where(l => l.Key != CruiserWreckRules.EchoLoreKey)); // no ship's log here
        Assert.Equal(0, bob.State.Inventory.CountOf(CruiserWreckRules.EchoCoreItem)); // not aboard yet

        // Carried, VEGA reads the core's record into the Journal — once.
        server.ReadEchoMemoryCoreForTest(alice);
        var read = Assert.Single(transport.To<LoreTextRevealed>(alice));
        Assert.Equal((CruiserWreckRules.EchoLoreKey, CruiserWreckRules.LoreSite), (read.Key, read.Site));
        server.ReadEchoMemoryCoreForTest(alice);
        Assert.Single(transport.To<LoreTextRevealed>(alice));

        // A fighter who comes later still gets theirs; a second use gives nobody another one.
        Board(server, Bob, id);
        Assert.Equal(1, bob.State.Inventory.CountOf(CruiserWreckRules.EchoCoreItem));
        alice.State.Inventory.Add(CruiserWreckRules.ChipItem, 1, 16);
        Assert.True(server.UseBridgeConsoleForTest(Alice));
        Assert.Equal(1, alice.State.Inventory.CountOf(CruiserWreckRules.EchoCoreItem));
        Assert.Equal(1, alice.State.Inventory.CountOf(CruiserWreckRules.ChipItem));
        Assert.Equal(2, freed.Events.Count);
        Assert.Equal(new[] { Alice, Bob, "Carol" }, record.CoresGiven.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryTextOfTheWreck_ExistsInEnglishAndGerman()
    {
        var en = _content.CreateLocalizer(GameLocale.English);
        var de = _content.CreateLocalizer(GameLocale.German);
        var keys = new List<string>
        {
            CruiserWreckRules.NameKey, "srv.cruiser.console_locked", "srv.cruiser.console_blue", "srv.cruiser.console_done",
            "srv.cruiser.crate", "srv.cruiser.echo_core", "vega.cruiser.wreck_boarded", "chr.echo.name",
            "ui.lore.site." + CruiserWreckRules.LoreSite,
        };
        keys.AddRange(SvGameServer.EchoLines);
        keys.AddRange(SvGameServer.EchoVegaLines);
        var lore = _content.Stories[StoryRegistry.DefaultStoryId].LoreSites.Where(l => l.Site == CruiserWreckRules.LoreSite).ToList();
        Assert.Single(lore, l => l.Key == CruiserWreckRules.EchoLoreKey);
        Assert.InRange(lore.Count(l => l.Key != CruiserWreckRules.EchoLoreKey), 2, 3);
        keys.AddRange(lore.Select(l => l.TextKey));
        foreach (string key in keys)
        {
            Assert.True(en.Has(key), $"missing '{key}' in en");
            Assert.True(de.Has(key), $"missing '{key}' in de");
            Assert.NotEqual(key, en.Get(key));
        }

        Assert.InRange(SvGameServer.EchoLines.Length, 4, 6);
        Assert.InRange(SvGameServer.EchoVegaLines.Length, 1, 2);
    }
}
