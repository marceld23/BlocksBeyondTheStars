// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Debris fields (#2353): a star-map location placed after the generator, a cloud of wreckage with salvage
/// capsules and a flight recorder in flight; the shield taps (#2355); combat debris (#2356); raiders with real hulls (#2357);
/// the salvage ledger (#2354).</summary>
public sealed class DebrisFieldTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public DebrisFieldTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_debris_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    // ---------------- The placer (pure) ----------------

    private static List<StarSystem> SyntheticGalaxy(int count)
    {
        var systems = new List<StarSystem>();
        for (int i = 0; i < count; i++)
        {
            var sys = new StarSystem { Id = "sys" + i, Name = "S" + i, MapX = i * 90, MapY = (i % 3) * 120 };
            sys.Bodies.Add(new CelestialBody { Id = sys.Id + "-p0", Name = "P", Kind = CelestialKind.Planet, PlanetType = "rocky", SystemId = sys.Id, SystemX = 600f, SystemZ = 0f });
            sys.Bodies.Add(new CelestialBody { Id = sys.Id + "-p1", Name = "Q", Kind = CelestialKind.Planet, PlanetType = "ice", SystemId = sys.Id, SystemX = -1100f, SystemZ = 300f });
            sys.Bodies.Add(new CelestialBody { Id = sys.Id + "-m0", Name = "M", Kind = CelestialKind.Moon, PlanetType = "rocky", SystemId = sys.Id, SystemX = 700f, SystemZ = 80f, ParentId = sys.Id + "-p0" });
            sys.Bodies.Add(new CelestialBody { Id = sys.Id + "-st", Name = "Station", Kind = CelestialKind.SpaceStation, SystemId = sys.Id, SystemX = 0f, SystemZ = 900f });
            systems.Add(sys);
        }

        systems.Add(new StarSystem
        {
            Id = "guardian_finale",
            Name = "Guardian Core",
            MapX = 1600,
            MapY = 0,
            Bodies = { new CelestialBody { Id = "guardian_finale-core", SystemId = "guardian_finale", Kind = CelestialKind.Planet, PlanetType = "rocky", SystemX = 400f } },
        });
        return systems;
    }

    private static List<(string Id, float X, float Z)> Snapshot(IEnumerable<StarSystem> systems)
        => systems.SelectMany(s => s.Bodies).Where(b => b.Kind != CelestialKind.DebrisField).Select(b => (b.Id, b.SystemX, b.SystemZ)).ToList();

    [Fact]
    public void Placer_IsSeedStable_LeavesEveryExistingBodyAlone_AndRespectsOff()
    {
        var a = SyntheticGalaxy(12);
        var before = Snapshot(a);
        int placed = DebrisFieldPlacer.Place(a, 3, Frequency.Frequent, 12, _content.SpaceSalvage);
        Assert.True(placed >= 3, $"Frequent should litter a 12-system galaxy with fields (got {placed})");
        Assert.Equal(before, Snapshot(a)); // appended, never moved
        Assert.Equal(placed, a.Count(s => s.Bodies.Any(b => b.Kind == CelestialKind.DebrisField)));
        Assert.All(a, s => Assert.True(s.Bodies.Count(b => b.Kind == CelestialKind.DebrisField) <= 1, "at most one field per system"));

        var b = SyntheticGalaxy(12);
        DebrisFieldPlacer.Place(b, 3, Frequency.Frequent, 12, _content.SpaceSalvage);
        var fieldsA = a.SelectMany(s => s.Bodies).Where(x => x.Kind == CelestialKind.DebrisField).Select(x => (x.Id, x.SystemX, x.SystemZ)).ToList();
        var fieldsB = b.SelectMany(s => s.Bodies).Where(x => x.Kind == CelestialKind.DebrisField).Select(x => (x.Id, x.SystemX, x.SystemZ)).ToList();
        Assert.Equal(fieldsA, fieldsB);

        // Another seed places differently; a second pass over the same galaxy adds nothing.
        var c = SyntheticGalaxy(12);
        DebrisFieldPlacer.Place(c, 4, Frequency.Frequent, 12, _content.SpaceSalvage);
        Assert.NotEqual(fieldsA, c.SelectMany(s => s.Bodies).Where(x => x.Kind == CelestialKind.DebrisField).Select(x => (x.Id, x.SystemX, x.SystemZ)).ToList());
        Assert.Equal(0, DebrisFieldPlacer.Place(a, 3, Frequency.Frequent, 12, _content.SpaceSalvage));

        var off = SyntheticGalaxy(12);
        Assert.Equal(0, DebrisFieldPlacer.Place(off, 3, Frequency.Off, 12, _content.SpaceSalvage));
        Assert.DoesNotContain(off.SelectMany(s => s.Bodies), x => x.Kind == CelestialKind.DebrisField);
    }

    [Fact]
    public void Placer_NeverTouchesAStorySystem_AndKeepsClearOfEveryBody()
    {
        var systems = SyntheticGalaxy(12);
        DebrisFieldPlacer.Place(systems, 7, Frequency.Frequent, 12, _content.SpaceSalvage);
        Assert.DoesNotContain(systems.First(s => s.Id == "guardian_finale").Bodies, b => b.Kind == CelestialKind.DebrisField);
        Assert.False(DebrisFieldPlacer.MayHoldDebrisField("guardian_finale"));
        Assert.True(DebrisFieldPlacer.MayHoldDebrisField("sys4"));

        foreach (var sys in systems)
        {
            var field = sys.Bodies.FirstOrDefault(b => b.Kind == CelestialKind.DebrisField);
            if (field is null)
            {
                continue;
            }

            Assert.Equal(sys.Id + "-d", field.Id);
            Assert.False(string.IsNullOrEmpty(field.Name));
            foreach (var body in sys.Bodies.Where(b => b.Kind is CelestialKind.Planet or CelestialKind.Moon or CelestialKind.AsteroidField or CelestialKind.SpaceStation))
            {
                float dx = field.SystemX - body.SystemX, dz = field.SystemZ - body.SystemZ;
                Assert.True(Math.Sqrt(dx * dx + dz * dz) >= DebrisFieldPlacer.BodyClearance - 0.01, $"{field.Id} sits inside {body.Id}");
            }
        }
    }

    [Fact]
    public void Theme_IsSeedPure_AndOneOfTheContentThemes()
    {
        string t1 = DebrisFieldPlacer.ThemeFor(11, "sys3-d", "Standard", _content.SpaceSalvage);
        string t2 = DebrisFieldPlacer.ThemeFor(11, "sys3-d", "Standard", _content.SpaceSalvage);
        Assert.Equal(t1, t2);
        Assert.Contains(t1, _content.SpaceSalvage.Themes.Keys);

        // Across many fields every theme comes up — the weights are not degenerate.
        var seen = new HashSet<string>();
        for (int i = 0; i < 200; i++)
        {
            seen.Add(DebrisFieldPlacer.ThemeFor(i, "sys" + (i % 12) + "-d", i % 2 == 0 ? "PirateHaven" : "Hub", _content.SpaceSalvage));
        }

        Assert.True(seen.Count >= 4, $"only {seen.Count} themes rolled in 200 fields");
    }

    // ---------------- The server ----------------

    private SvGameServer NewServer(string name, long seed, Frequency fields, Action<GameRules>? rules, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = name, Seed = seed, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.AsteroidDestruction = AsteroidDestructionMode.MiningOnly;
        config.World.DebrisFields = fields;
        rules?.Invoke(config.Rules);
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Puts the pilot at a landable body that shares its system with a debris field (the home system when it has
    /// one, else the first that does — via a temporary jump generator) and returns both.</summary>
    private static (CelestialBody Field, CelestialBody Anchor) ParkNextToAField(SvGameServer server, PlayerSession pilot)
    {
        var systems = server.Galaxy.Systems
            .OrderBy(s => s.Bodies.Any(b => b.Id == server.ActiveLocationId) ? 0 : 1)
            .ToList();
        foreach (var sys in systems)
        {
            var field = sys.Bodies.FirstOrDefault(b => b.Kind == CelestialKind.DebrisField);
            var anchor = sys.Bodies.FirstOrDefault(b =>
                b.Kind is CelestialKind.Planet or CelestialKind.Moon or CelestialKind.AsteroidField && !string.IsNullOrEmpty(b.PlanetType));
            if (field is null || anchor is null)
            {
                continue;
            }

            if (pilot.CurrentLocationId != anchor.Id)
            {
                if (!server.Ship.Modules.Contains("jump_generator"))
                {
                    server.Ship.Modules.Add("jump_generator");
                }

                server.Travel(pilot.State.PlayerId, anchor.Id);
                Assert.Equal(anchor.Id, pilot.CurrentLocationId);
            }

            return (field, anchor);
        }

        throw new Xunit.Sdk.XunitException("the Frequent setting should litter at least one system with a debris field");
    }

    [Fact]
    public void Galaxy_GainsFields_WithoutMovingAnyOtherBody()
    {
        var withFields = NewServer("gal_on", 11, Frequency.Frequent, null, out var repoA);
        var without = NewServer("gal_off", 11, Frequency.Off, null, out var repoB);
        using (repoA)
        using (repoB)
        {
            Assert.Contains(withFields.Galaxy.AllBodies(), b => b.Kind == CelestialKind.DebrisField);
            Assert.DoesNotContain(without.Galaxy.AllBodies(), b => b.Kind == CelestialKind.DebrisField);
            Assert.Equal(Snapshot(without.Galaxy.Systems), Snapshot(withFields.Galaxy.Systems)); // every other body is where it was
            Assert.Equal(without.Galaxy.Wormholes.Select(w => (w.Id, w.SystemX, w.SystemZ)), withFields.Galaxy.Wormholes.Select(w => (w.Id, w.SystemX, w.SystemZ)));
        }
    }

    [Fact]
    public void EnterSpace_ParksTheField_WithFragmentsAndCapsules_AtItsChartPosition()
    {
        var server = NewServer("field_exists", 11, Frequency.Frequent, null, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Salvager");
            var (field, anchor) = ParkNextToAField(server, pilot);
            server.EnterSpace("Salvager");

            var entities = server.SpaceEntitiesFor("Salvager");
            var marker = entities.FirstOrDefault(e => e.Id == field.Id);
            Assert.NotNull(marker);
            Assert.Equal(CombatEntityKind.DebrisField, marker!.Kind);
            Assert.False(marker.Hostile);
            Assert.Equal(field.Name, marker.Name);
            Assert.Equal((field.SystemX - anchor.SystemX) * SystemBodyLayout.FlightViewScale, marker.Position.X, 3);
            Assert.Equal((field.SystemZ - anchor.SystemZ) * SystemBodyLayout.FlightViewScale, marker.Position.Z, 3);

            var fields = _content.SpaceSalvage.Fields;
            var fragments = entities.Where(e => e.Kind == CombatEntityKind.Debris && e.FieldId == field.Id).ToList();
            Assert.InRange(fragments.Count, fields.FragmentsMin, fields.FragmentsMax);
            foreach (var f in fragments)
            {
                Assert.True(server.StructureBlockCountForTest(f.Id) > 0, "a fragment is a voxel hull");
                Assert.True(marker.Position.DistanceSquared(f.Position) <= fields.Radius * fields.Radius + 0.01f);
                Assert.True(f.Hull >= 4f);
            }

            var capsules = entities.Where(e => e.Kind == CombatEntityKind.SalvageCapsule && e.FieldId == field.Id).ToList();
            Assert.InRange(capsules.Count, fields.CapsulesMin, fields.CapsulesMax);
            Assert.All(capsules, c => Assert.NotEmpty(c.Loot));
            Assert.All(capsules, c => Assert.StartsWith(field.Id + "-c", c.Id, StringComparison.Ordinal));

            // The same field on the next entry (fragments rebuilt per instance like belt rocks).
            server.LeaveSpace("Salvager");
            server.EnterSpace("Salvager");
            Assert.Equal(fragments.Count, server.DebrisFragmentCountForTest("Salvager", field.Id));
        }
    }

    [Fact]
    public void FlyingUpToTheField_ReadsTheRecorder_Once_AndMarksItVisited()
    {
        var server = NewServer("field_visit", 11, Frequency.Frequent, null, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Salvager");
            var (field, _) = ParkNextToAField(server, pilot);
            server.EnterSpace("Salvager");
            var marker = server.SpaceEntitiesFor("Salvager").First(e => e.Id == field.Id);

            Assert.DoesNotContain("debris:" + field.Id, pilot.State.Scanned);
            int knowledgeBefore = pilot.State.KnowledgePoints;
            server.ShipMove("Salvager", marker.Position.X, marker.Position.Y, marker.Position.Z - 30f);
            Assert.Contains("debris:" + field.Id, pilot.State.Scanned);
            Assert.Contains(field.Id, pilot.State.LandedBodies);
            Assert.Contains(pilot.State.Milestones, m => m.StartsWith("lore:debris", StringComparison.Ordinal));
            Assert.True(pilot.State.KnowledgePoints > knowledgeBefore);

            var result = server.ScanSpaceEntity("Salvager", field.Id);
            Assert.Equal("debris_field", result.Kind);
            Assert.Equal(0, result.KnowledgeGained); // the approach already banked it
        }
    }

    [Fact]
    public void VegaTip_NamesTheRecorder_OnceTheFieldIsCarvedOut()
    {
        var server = NewServer("field_recorder_tip", 11, Frequency.Frequent, null, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Salvager");
            var (field, _) = ParkNextToAField(server, pilot);
            server.EnterSpace("Salvager");
            var marker = server.SpaceEntitiesFor("Salvager").First(e => e.Id == field.Id);

            // Parked at the recorder with the rubble still around: the approach reads it, the tip stays quiet.
            server.ShipMove("Salvager", marker.Position.X + 3f, marker.Position.Y, marker.Position.Z);
            Assert.Contains("debris:" + field.Id, pilot.State.Scanned);
            Assert.DoesNotContain("debris_recorder", server.VegaTipCandidatesForTest("Salvager").Candidates);

            // Every fragment carved, every capsule pulled in (#2366, Layex' "was ist das?"): the box left behind gets its line.
            server.ClearDebrisFieldSalvageForTest("Salvager", field.Id);
            Assert.Contains("debris_recorder", server.VegaTipCandidatesForTest("Salvager").Candidates);

            // Away from the field it is not the moment.
            server.ShipMove("Salvager", marker.Position.X + 80f, marker.Position.Y, marker.Position.Z);
            Assert.DoesNotContain("debris_recorder", server.VegaTipCandidatesForTest("Salvager").Candidates);
        }
    }

    [Fact]
    public void MiningLaser_BreaksAFragment_AndPaysTheThemesScrap()
    {
        var server = NewServer("field_mine", 11, Frequency.Frequent, null, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Salvager");
            server.Ship.Modules.Add("asteroid_breaker");
            server.Ship.Modules.Remove("tractor_beam");
            var (field, _) = ParkNextToAField(server, pilot);
            server.EnterSpace("Salvager");
            var fragment = server.SpaceEntitiesFor("Salvager").First(e => e.Kind == CombatEntityKind.Debris && e.FieldId == field.Id && e.Loot.Count > 0);
            var loot = fragment.Loot.Select(l => (l.Item, l.Count)).ToList();
            var before = loot.Select(l => pilot.State.Inventory.CountOf(l.Item)).ToList();

            server.ShipMove("Salvager", fragment.Position.X + 5f, fragment.Position.Y, fragment.Position.Z);
            for (int i = 0; i < 20 && server.SpaceEntitiesFor("Salvager").Any(e => e.Id == fragment.Id); i++)
            {
                server.FireWeapon("Salvager", "asteroid_breaker", fragment.Id);
                server.TickForTest(2.0);
            }

            Assert.DoesNotContain(server.SpaceEntitiesFor("Salvager"), e => e.Id == fragment.Id);
            Assert.Equal(0, server.StructureBlockCountForTest(fragment.Id));
            for (int i = 0; i < loot.Count; i++)
            {
                Assert.True(pilot.State.Inventory.CountOf(loot[i].Item) >= before[i] + loot[i].Count, $"the fragment should pay {loot[i].Item}");
            }
        }
    }

    [Fact]
    public void Capsule_PaysOnce_AcrossReentry_AndARestart()
    {
        string worldName = "field_capsule";
        var server = NewServer(worldName, 11, Frequency.Frequent, null, out var repo);
        string fieldId, capsuleId;
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Salvager");
            var (field, _) = ParkNextToAField(server, pilot);
            fieldId = field.Id;
            server.EnterSpace("Salvager");
            var capsule = server.SpaceEntitiesFor("Salvager").First(e => e.Kind == CombatEntityKind.SalvageCapsule && e.FieldId == fieldId);
            capsuleId = capsule.Id;
            var first = capsule.Loot[0];
            Assert.Equal(-1, server.SpaceSalvageLedgerForTest("debris:" + fieldId));

            // The starter ship carries a tractor beam: flying within its passive range pulls the capsule into the hold.
            server.ShipMove("Salvager", capsule.Position.X + 4f, capsule.Position.Y, capsule.Position.Z);
            server.TickForTest(0.5);
            Assert.DoesNotContain(server.SpaceEntitiesFor("Salvager"), e => e.Id == capsuleId);
            Assert.True(server.Ship.Cargo.CountOf(first.Item) >= first.Count, "the capsule's loot goes into the hold");
            Assert.True(server.SpaceSalvageLedgerForTest("debris:" + fieldId) > 0, "the collected capsule is in the ledger");

            server.LeaveSpace("Salvager");
            server.EnterSpace("Salvager");
            Assert.DoesNotContain(server.SpaceEntitiesFor("Salvager"), e => e.Id == capsuleId);
            server.LeaveSpace("Salvager");
            repo.Flush();
        }

        server = NewServer(worldName, 11, Frequency.Frequent, null, out repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Salvager");
            ParkNextToAField(server, pilot);
            server.EnterSpace("Salvager");
            Assert.DoesNotContain(server.SpaceEntitiesFor("Salvager"), e => e.Id == capsuleId);
            Assert.True(server.SpaceSalvageLedgerForTest("debris:" + fieldId) > 0);
        }
    }

    [Fact]
    public void DriftingDebris_TapsTheShield_ButNeverTheHull_AndOnlyInsideTheField()
    {
        var server = NewServer("field_bump", 11, Frequency.Frequent, null, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Salvager");
            var (field, _) = ParkNextToAField(server, pilot);
            server.EnterSpace("Salvager");
            var marker = server.SpaceEntitiesFor("Salvager").First(e => e.Id == field.Id);
            float shieldFull = server.Ship.Shield;
            float hull = server.Ship.Hull;
            Assert.True(shieldFull > 3f, "the launch refills the shield");

            // Cruise around inside the field: the rubble taps the shield (3 points on an interval; regen is 2/s, so a
            // tick right after a tap sees at least 2.5 missing). The hull stays untouched throughout.
            float minShield = shieldFull;
            for (int i = 0; i < 100; i++)
            {
                float k = i * 0.7f;
                server.ShipMove("Salvager", marker.Position.X + 3f + k % 9f, marker.Position.Y, marker.Position.Z + k % 7f);
                server.TickForTest(0.25);
                minShield = Math.Min(minShield, server.Ship.Shield);
                Assert.Equal(hull, server.Ship.Hull);
            }

            Assert.True(minShield <= shieldFull - 2.5f, $"the shield should have been tapped inside the field (min {minShield} of {shieldFull})");

            // Well outside the field the same cruise leaves the shield alone.
            float r = _content.SpaceSalvage.Fields.Radius;
            for (int i = 0; i < 40; i++)
            {
                server.ShipMove("Salvager", marker.Position.X + r * 3f + i % 5, marker.Position.Y, marker.Position.Z + r * 3f);
                server.TickForTest(0.25);
            }

            float settled = server.Ship.Shield;
            for (int i = 0; i < 60; i++)
            {
                server.ShipMove("Salvager", marker.Position.X + r * 3f + i % 5, marker.Position.Y, marker.Position.Z + r * 3f);
                server.TickForTest(0.25);
                Assert.True(server.Ship.Shield >= settled - 0.01f, "no taps outside the field");
                settled = server.Ship.Shield;
            }

            Assert.Equal(hull, server.Ship.Hull);
        }
    }

    [Fact]
    public void DestroyedDrone_LeavesWreckageFragments()
    {
        var server = NewServer("combat_debris", 1, Frequency.Off, r =>
        {
            r.SpaceCombat = SpaceCombatMode.PvE;
            r.SpaceNpcEnemies = AlienActivity.Rare; // 1 drone, hull 40
            r.ShipWeapons = ShipWeaponMode.NpcsOnly;
        }, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            server.Ship.Modules.Add("ship_cannon_1"); // 20 dmg
            server.Ship.Modules.Remove("tractor_beam");
            server.EnterSpace("Pilot");

            var drone = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == CombatEntityKind.Drone);
            server.ShipMove("Pilot", drone.Position.X, drone.Position.Y, drone.Position.Z);
            server.FireWeapon("Pilot", "ship_cannon_1", drone.Id);
            server.TickForTest(1.1);
            server.FireWeapon("Pilot", "ship_cannon_1", drone.Id);
            Assert.DoesNotContain(server.SpaceEntitiesFor("Pilot"), e => e.Id == drone.Id);

            var def = _content.SpaceSalvage.CombatDebris;
            int fragments = server.DebrisFragmentCountForTest("Pilot", string.Empty);
            Assert.InRange(fragments, def.FragmentsMin, def.FragmentsMax);
            foreach (var f in server.SpaceEntitiesFor("Pilot").Where(e => e.Kind == CombatEntityKind.Debris))
            {
                Assert.True(server.StructureBlockCountForTest(f.Id) > 0, "combat debris is a voxel fragment");
                Assert.True(drone.Position.DistanceSquared(f.Position) <= 6f * 6f, "the wreckage scatters around the kill");
                Assert.False(f.Hostile);
            }
        }
    }

    [Fact]
    public void Raider_FliesARealHull_InLivery_FacesItsCourse_AndLeavesWreckageWhenDestroyed()
    {
        var server = NewServer("raider_hull", 1, Frequency.Off, r =>
        {
            r.SpaceCombat = SpaceCombatMode.PvE;
            r.ShipWeapons = ShipWeaponMode.NpcsOnly;
            r.Bandits = AlienActivity.Normal;
            r.SpaceNpcEnemies = AlienActivity.Off;
        }, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            server.Ship.Modules.Add("ship_cannon_1");
            server.Ship.Modules.Remove("tractor_beam");
            server.EnterSpace("Pilot");
            server.ShipMove("Pilot", 0f, 0f, 0f);
            server.SpawnBanditShipForTest("Pilot");

            var raider = server.BanditShipForTest("Pilot");
            Assert.NotNull(raider);
            string hullId = "ship:bandit:" + raider!.Id;
            int cells = server.StructureBlockCountForTest(hullId);
            Assert.True(cells > 50, $"the raider should fly a real content hull ({cells} cells)");
            Assert.True(server.StructureDyedCellCountForTest(hullId) > cells / 2, "most plating cells carry the raider livery dye");
            Assert.InRange(raider.HullMax, 55f, 120f);
            Assert.Equal(Math.Clamp(40f + cells / 6f, 55f, 120f), raider.HullMax, 2);

            // It faces the pilot it came for (the server's heading, degrees about Y).
            float expected = (float)(Math.Atan2(-raider.Position.X, -raider.Position.Z) * 180.0 / Math.PI);
            float delta = ((raider.Yaw - expected) % 360f + 540f) % 360f - 180f;
            Assert.True(Math.Abs(delta) < 0.5f, $"yaw {raider.Yaw} should face the ship ({expected})");

            // Shoot it down: the hull structure goes with it, wreckage cut from it stays behind.
            server.ShipMove("Pilot", raider.Position.X + 10f, raider.Position.Y, raider.Position.Z);
            for (int i = 0; i < 40 && server.BanditShipForTest("Pilot") is not null; i++)
            {
                server.FireWeapon("Pilot", "ship_cannon_1", raider.Id);
                server.TickForTest(1.1);
            }

            Assert.Null(server.BanditShipForTest("Pilot"));
            Assert.Equal(0, server.StructureBlockCountForTest(hullId));
            Assert.True(server.DebrisFragmentCountForTest("Pilot", string.Empty) >= _content.SpaceSalvage.CombatDebris.FragmentsMin);
        }
    }

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
            // Best effort: a lingering SQLite handle on Windows can hold the directory for a moment.
        }
    }
}
