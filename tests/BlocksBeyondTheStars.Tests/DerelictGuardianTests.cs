// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Derelict guardians (#2635): every generated derelict holds a guardian bay that releases a Guardian robot or a
/// scan-drone on a slow cadence while a player is aboard — never more than three of its own alive, never in Peaceful or
/// Creative, never again once the bay is mined (across a restart too), and only inside the hull. The guardians are the
/// planet machines: killable with the usual weapons, with the usual salvage.
/// </summary>
public sealed class DerelictGuardianTests : IDisposable
{
    private const double Step = 0.25;

    private readonly string _root;
    private readonly GameContent _content;

    public DerelictGuardianTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_guardian_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private SvGameServer NewServer(string world, out SqliteWorldRepository repo, Action<GameRules>? rules = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig { WorldName = world, Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        rules?.Invoke(config.Rules);
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>Flies a fresh player up to the start system's guaranteed derelict and boards it; returns its id.</summary>
    private static string BoardGuaranteed(SvGameServer server, PlayerSession p)
    {
        string id = DerelictPlacer.BodyId(server.Galaxy.FindBody(server.Metadata.ActiveLocationId)!.SystemId, 0);
        string pid = p.State.PlayerId;
        server.EnterSpace(pid);
        var contact = server.SpaceEntitiesFor(pid).First(e => e.Id == id);
        var layout = server.DerelictLayoutForTest(id)!;
        server.ShipMove(pid, contact.Position.X, contact.Position.Y, contact.Position.Z - (layout.Length / 2f + 8f));
        server.BoardStation(pid, id);
        Assert.True(server.InStation(pid));
        return id;
    }

    /// <summary>Runs the enemy tick of the active world (the derelict's interior) for <paramref name="seconds"/>.</summary>
    private static void Advance(SvGameServer server, double seconds)
    {
        for (double t = 0; t < seconds; t += Step)
        {
            server.TickEnemiesForTest(Step);
        }
    }

    private string KeyAt(SvGameServer server, Vector3i cell) => _content.BlockById(server.World.GetBlock(cell))?.Key ?? "air";

    /// <summary>A free cell beside the bay a player can stand in.</summary>
    private static Vector3i BesideTheBay(SvGameServer server, Vector3i bay)
    {
        foreach (var (dx, dz) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, 1), (-1, -1), (1, -1) })
        {
            var c = new Vector3i(bay.X + dx, bay.Y, bay.Z + dz);
            if (server.World.GetBlock(c).IsAir && server.World.GetBlock(new Vector3i(c.X, c.Y + 1, c.Z)).IsAir)
            {
                return c;
            }
        }

        throw new InvalidOperationException("the bay is walled in");
    }

    // ---------------- the layout ----------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EveryGeneratedLayout_HoldsOneGuardianBay_OnItsSpawnerSpot_FacingTheWayIn(bool proceduralOnly)
    {
        ushort bay = _content.GetBlock(SvGameServer.GuardianBayBlockKey)!.NumericId.Value;
        var scrap = new[] { _content.GetBlock("scrap_pile")!.NumericId.Value, _content.GetBlock("scrap_metal")!.NumericId.Value };
        for (long seed = 1; seed <= 12; seed++)
        {
            var l = DerelictGenerator.Generate(seed * 7919, _content, proceduralOnly);
            var spot = l.SpawnerSpot!.Value;
            Assert.Equal(bay, l.Get(spot.X, spot.Y, spot.Z));
            Assert.NotEqual(0, l.Get(spot.X, spot.Y - 1, spot.Z)); // it stands on a deck…
            Assert.DoesNotContain(l.Get(spot.X, spot.Y - 1, spot.Z), scrap); // …not on a scrap pile
            Assert.InRange(CubeFacing.FrontOf(l.GetShape(spot.X, spot.Y, spot.Z)), 2, 5); // a front, toward the arrival
            Assert.Contains(new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }, o => // and there is floor beside it for its guardians
                l.Get(spot.X + o.Item1, spot.Y, spot.Z + o.Item2) == 0 && l.Get(spot.X + o.Item1, spot.Y + 1, spot.Z + o.Item2) == 0
                && l.Get(spot.X + o.Item1, spot.Y - 1, spot.Z + o.Item2) != 0);

            int bays = 0;
            for (int x = 0; x < l.Width; x++)
                for (int y = 0; y < l.Height; y++)
                    for (int z = 0; z < l.Length; z++)
                    {
                        bays += l.Get(x, y, z) == bay ? 1 : 0;
                    }

            Assert.Equal(1, bays);
        }
    }

    [Fact]
    public void TheBay_IsMinedWithADrill_DropsSalvage_AndNobodyCraftsIt()
    {
        var def = _content.GetBlock(SvGameServer.GuardianBayBlockKey)!;
        Assert.True(def.Mineable);
        Assert.Equal(1, def.MinToolTier); // the starter drill opens it
        Assert.Contains(def.Drops, d => d.Item == "circuit_board");
        Assert.DoesNotContain(def.Drops, d => d.Item == def.Key);
        Assert.Null(_content.GetItem(def.Key)); // no item, so no recipe can make one and nobody places one
        Assert.DoesNotContain(_content.Items.Values, i => i.PlacesBlock == def.Key);
    }

    // ---------------- the cadence ----------------

    [Fact]
    public void TheBay_ReleasesOnItsCadence_NeverMoreThanThree_AndOnlyInsideTheHull()
    {
        var server = NewServer("grd_cadence", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            string id = BoardGuaranteed(server, p);
            p.State.GodMode = true; // the machines ignore him: this test counts them, it does not fight them
            var bay = server.DerelictSpawnerSpotForTest(id)!.Value;
            Assert.Equal(SvGameServer.GuardianBayBlockKey, KeyAt(server, bay));

            double t = 0, lastRelease = double.NaN;
            int released = 0;
            int countBefore = 0;
            while (t < SvGameServer.GuardianFirstReleaseSeconds + (3 * SvGameServer.GuardianReleaseIntervalSeconds) + 1)
            {
                countBefore = server.PlanetEnemies.Count;
                server.TickEnemiesForTest(Step);
                t += Step;
                int now = server.GuardiansReleasedForTest(id);
                if (now > released)
                {
                    Assert.True(countBefore < SvGameServer.GuardianCapPerBay, $"a release at {t}s with {countBefore} guardians already aboard");
                    if (released == 0)
                    {
                        Assert.InRange(t, SvGameServer.GuardianFirstReleaseSeconds, SvGameServer.GuardianFirstReleaseSeconds + Step);
                    }
                    else
                    {
                        Assert.True(t - lastRelease >= SvGameServer.GuardianReleaseIntervalSeconds - 1e-6,
                            $"release {now} came {t - lastRelease}s after the one before");
                    }

                    lastRelease = t;
                    released = now;
                }

                Assert.True(server.PlanetEnemies.Count <= SvGameServer.GuardianCapPerBay);
                Assert.All(server.PlanetEnemies, e => Assert.True(server.InsideDerelictHullForTest(id, e.Position), $"a guardian at {e.Position} is outside the hull"));
            }

            Assert.True(released >= 3, $"only {released} guardians in {t}s");
            Assert.Contains(server.PlanetEnemies, e => e.Kind == CombatEntityKind.ScanDrone);  // every other one flies…
            Assert.Contains(server.PlanetEnemies, e => e.Kind != CombatEntityKind.ScanDrone);  // …the others walk
            Assert.Contains("derelict:guardian:" + id, p.State.Milestones); // VEGA warned him

            // At the cap the bay waits: a whole interval more and nothing new comes while all three are about.
            if (server.PlanetEnemies.Count == SvGameServer.GuardianCapPerBay)
            {
                Advance(server, SvGameServer.GuardianReleaseIntervalSeconds);
                Assert.Equal(released, server.GuardiansReleasedForTest(id));
            }

            server.Stop();
        }
    }

    [Theory]
    [InlineData(false)] // Peaceful: hostile machines off
    [InlineData(true)]  // Creative
    public void NoGuardians_InPeaceful_OrCreative(bool creative)
    {
        var server = NewServer("grd_off_" + creative, out var repo, rules =>
        {
            if (creative)
            {
                rules.GameMode = GameMode.Creative;
            }
            else
            {
                rules.PlanetEnemies = AlienActivity.Off;
            }
        });
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            string id = BoardGuaranteed(server, p);
            Assert.Equal(SvGameServer.GuardianBayBlockKey, KeyAt(server, server.DerelictSpawnerSpotForTest(id)!.Value)); // the bay stands…
            Advance(server, SvGameServer.GuardianFirstReleaseSeconds + SvGameServer.GuardianReleaseIntervalSeconds + 1);
            Assert.Empty(server.PlanetEnemies); // …but sends nobody
            Assert.Equal(0, server.GuardiansReleasedForTest(id));
            server.Stop();
        }
    }

    // ---------------- the end of the bay ----------------

    [Fact]
    public void MiningTheBay_DropsSalvage_AndNoGuardianComes_EvenAfterARestart()
    {
        string id;
        Vector3i bay;
        {
            var s1 = NewServer("grd_mine", out var repo1);
            using (repo1)
            {
                var p = s1.AddLocalPlayer("Justus");
                id = BoardGuaranteed(s1, p);
                p.State.GodMode = true;
                bay = s1.DerelictSpawnerSpotForTest(id)!.Value;
                var stand = BesideTheBay(s1, bay);
                p.State.Inventory.SetSlot(0, new ItemStack("basic_drill", 1));
                p.State.SelectedHotbarSlot = 0;
                p.State.Position = new Vector3f(stand.X + 0.5f, stand.Y, stand.Z + 0.5f);
                int boards = p.State.Inventory.CountOf("circuit_board");
                s1.MineBlock("Justus", bay.X, bay.Y, bay.Z);

                Assert.True(s1.World.GetBlock(bay).IsAir, "the starter drill mines the bay");
                Assert.Equal(boards + 2, p.State.Inventory.CountOf("circuit_board"));
                Assert.True(p.State.Inventory.CountOf("power_cell") >= 1);

                Advance(s1, SvGameServer.GuardianFirstReleaseSeconds + SvGameServer.GuardianReleaseIntervalSeconds + 1);
                Assert.Empty(s1.PlanetEnemies);
                Assert.Equal(0, s1.GuardiansReleasedForTest(id));
                s1.Stop();
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var s2 = NewServer("grd_mine", out var repo2);
        using (repo2)
        {
            var p = s2.AddLocalPlayer("Justus");
            s2.LeaveStation("Justus"); // the rejoin may have put him back aboard
            BoardGuaranteed(s2, p);
            p.State.GodMode = true;
            Assert.True(s2.World.GetBlock(bay).IsAir, "the mined bay stays mined after a restart");
            Advance(s2, SvGameServer.GuardianFirstReleaseSeconds + SvGameServer.GuardianReleaseIntervalSeconds + 1);
            Assert.Empty(s2.PlanetEnemies);
            s2.Stop();
        }
    }

    // ---------------- the guardians themselves ----------------

    [Fact]
    public void AGuardian_IsKilledWithTheUsualWeapons_DropsItsSalvage_AndTheNextWaitsAFullInterval()
    {
        var server = NewServer("grd_kill", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            string id = BoardGuaranteed(server, p);
            p.State.GodMode = true;
            Advance(server, SvGameServer.GuardianFirstReleaseSeconds + 1);
            var guardian = Assert.Single(server.PlanetEnemies);
            Assert.Equal(CombatEntityKind.Creature, guardian.Kind); // the first one walks

            p.State.GodMode = false;
            p.State.SuitEnergy = 100f;
            p.State.Inventory.SetSlot(0, new ItemStack("laser_pistol", 1));
            p.State.SelectedHotbarSlot = 0;
            p.State.Position = guardian.Position;
            int carbon = p.State.Inventory.CountOf("carbon");
            server.AttackEntity("Justus", guardian.Id);

            Assert.Empty(server.PlanetEnemies);
            Assert.Equal(carbon + 2, p.State.Inventory.CountOf("carbon")); // the planet machines' salvage

            // The bay is below its cap again, but the next guardian keeps the interval — no instant replacement.
            p.State.GodMode = true;
            Advance(server, SvGameServer.GuardianReleaseIntervalSeconds - 10);
            Assert.Equal(1, server.GuardiansReleasedForTest(id));
            Advance(server, 20);
            Assert.Equal(2, server.GuardiansReleasedForTest(id));
            server.Stop();
        }
    }

    [Fact]
    public void AGuardianOutsideTheHull_IsGone_InsteadOfFallingForEver()
    {
        var server = NewServer("grd_leash", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            string id = BoardGuaranteed(server, p);
            p.State.GodMode = true;
            Advance(server, SvGameServer.GuardianFirstReleaseSeconds + 1);
            var guardian = Assert.Single(server.PlanetEnemies);

            // Out through a breach and down into the void.
            guardian.Position = new Vector3f(guardian.Position.X, guardian.Position.Y - 60f, guardian.Position.Z);
            Assert.False(server.InsideDerelictHullForTest(id, guardian.Position));
            server.TickEnemiesForTest(Step);
            Assert.Empty(server.PlanetEnemies);
            server.Stop();
        }
    }
}
