// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Touchdown height (#1318): a player landed "suddenly underground" and was dug out a second later. A
/// 75-seed probe found no case where the pad's generated spread entombs anyone — the pads are nudged flat
/// and levelled to the median at generation. What the median ignores is what the player BUILT over the pad
/// since (Lyxette paved his landing site): the ship and the spawn now sit on the real ground, never below it,
/// and the rescue notice — a toast the next message overwrote — also lands in the chat scrollback.
/// </summary>
public sealed class PadSpawnTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public PadSpawnTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_padspawn_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo, string world, bool ship, IServerTransport? transport = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 31,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = ship,
            PlaceSettlements = false,
            PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, transport ?? new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static bool CellSolid(SvGameServer server, int x, int y, int z) => !server.World.GetBlock(new Vector3i(x, y, z)).IsAir;

    private static bool Entombed(SvGameServer server, Vector3f pos)
    {
        int x = (int)Math.Floor(pos.X), y = (int)Math.Floor(pos.Y), z = (int)Math.Floor(pos.Z);
        return CellSolid(server, x, y, z) && CellSolid(server, x, y + 1, z);
    }

    [Fact]
    public void APavedPad_WithoutAShip_SpawnsThePlayerOnTopOfThePaving()
    {
        var server = Started(out var repo, "paved", ship: false);
        using (repo)
        {
            var (px, py, pz) = server.LandingPadForTest(0);
            var concrete = _content.GetBlock("concrete")!.NumericId;
            for (int y = py + 1; y <= py + 3; y++)
            {
                server.World.SetBlock(new Vector3i(px, y, pz), concrete); // three courses over the pad centre
            }

            var p = server.AddLocalPlayer("Newcomer");
            var pos = p.State.Position;
            Assert.False(Entombed(server, pos), $"the spawn must not sit inside the paving (at {pos})");
            Assert.Equal(py + 3 + 2, (int)Math.Floor(pos.Y)); // the old spawn was median + 2 = inside the concrete
        }
    }

    [Fact]
    public void APavedPad_WithAShip_ParksTheShipOnThePaving_AndThePilotAboveIt()
    {
        var server = Started(out var repo, "pavedship", ship: true);
        using (repo)
        {
            var (px, py, pz) = server.LandingPadForTest(0);
            var concrete = _content.GetBlock("concrete")!.NumericId;
            for (int dx = -4; dx <= 4; dx++)
                for (int dz = -4; dz <= 4; dz++)
                    for (int y = py + 1; y <= py + 2; y++)
                    {
                        server.World.SetBlock(new Vector3i(px + dx, y, pz + dz), concrete); // a 9×9 paved yard, two high
                    }

            var p = server.AddLocalPlayer("Host");
            var (origin, _) = server.LandedShipBoundsForTest("Host");
            Assert.Equal(py + 2 + 1, origin.Y); // the hull's first layer sits ON the paving, not inside it
            Assert.False(Entombed(server, p.State.Position), $"the pilot must not spawn inside the paving (at {p.State.Position})");
        }
    }

    [Fact]
    public void AStaircaseBesideTheHull_DoesNotLiftTheParkedShip()
    {
        // #3286: a school-club player built a concrete staircase up the side of the starter ship; on the next join the
        // ship hung five cells in the air. The touchdown samples at the pad corners (±4) lie outside the 5×7 hull.
        var server = Started(out var repo, "stairs", ship: true);
        using (repo)
        {
            var (px, py, pz) = server.LandingPadForTest(0);
            var concrete = _content.GetBlock("concrete")!.NumericId;
            foreach (var (dx, dz) in new[] { (-4, -4), (4, -4), (-4, 4), (4, 4) })
                for (int y = py + 1; y <= py + 5; y++)
                {
                    server.World.SetBlock(new Vector3i(px + dx, y, pz + dz), concrete); // stair posts beside every corner
                }

            server.AddLocalPlayer("Host");
            var (origin, size) = server.LandedShipBoundsForTest("Host");
            Assert.True(size.X / 2 < 4 && size.Z / 2 < 4, $"the premise needs a hull narrower than the corner samples (size {size})");
            Assert.Equal(py + 1, origin.Y); // still on the levelled pad — nothing was built under it
        }
    }

    [Fact]
    public void ABlockUnderTheHullsEdge_StillLiftsTheParkedShip()
    {
        // #3286: the hull height reads every column of its own footprint — also edge columns the five touchdown samples
        // never looked at — so a block under the hull can never end up inside its floor (#1318).
        var server = Started(out var repo, "underedge", ship: true);
        using (repo)
        {
            var (px, py, pz) = server.LandingPadForTest(0);
            var concrete = _content.GetBlock("concrete")!.NumericId;
            for (int y = py + 1; y <= py + 3; y++)
            {
                server.World.SetBlock(new Vector3i(px + 2, y, pz - 3), concrete); // under the starter box's corner cell
            }

            server.AddLocalPlayer("Host");
            var (origin, _) = server.LandedShipBoundsForTest("Host");
            Assert.Equal(py + 3 + 1, origin.Y);
        }
    }

    [Fact]
    public void AnUntouchedPad_KeepsTheOldTouchdownHeight()
    {
        var server = Started(out var repo, "plain", ship: false);
        using (repo)
        {
            var (_, py, _) = server.LandingPadForTest(0);
            var p = server.AddLocalPlayer("Newcomer");
            Assert.Equal(py + 2, (int)Math.Floor(p.State.Position.Y)); // exactly what every existing save expects
        }
    }

    [Fact]
    public void TheDugOutRescue_AlsoLandsInTheChat()
    {
        var transport = new RecordingTransport();
        var server = Started(out var repo, "rescue", ship: false, transport);
        using (repo)
        {
            var p = server.AddLocalPlayer("Buried");
            p.State.AboardShip = false;
            var (px, py, pz) = server.LandingPadForTest(0);
            p.State.Position = new Vector3f(px + 0.5f, py - 6, pz + 0.5f); // sealed in the rock under the pad
            Assert.True(Entombed(server, p.State.Position));

            server.RunVoidRescueForTest();

            Assert.False(Entombed(server, p.State.Position));
            Assert.Contains(transport.Messages(), m => m is RespawnNotice n && n.Reason == "@srv.misc.dug_out");
            var chat = transport.Messages().OfType<ServerMessage>().Select(m => m.Text).ToList();
            Assert.Contains("You were stuck in the rock — dug out.", chat); // plain text → chat scrollback, not just the toast
        }
    }

    [Fact]
    public void TpPad_LandsOnTopOfThePaving()
    {
        var server = Started(out var repo, "tppad", ship: false);
        using (repo)
        {
            var (px, py, pz) = server.LandingPadForTest(0);
            var concrete = _content.GetBlock("concrete")!.NumericId;
            for (int y = py + 1; y <= py + 3; y++)
            {
                server.World.SetBlock(new Vector3i(px, y, pz), concrete);
            }

            var p = server.AddLocalPlayer("Admin");
            var pad = server.TeleportTargetsForTest(p.State.PlayerId).First(t => t.Kind == "pad" && t.Number == 1);
            Assert.Equal(py + 3 + 2, (int)Math.Floor(pad.Position.Y)); // the old target was median + 2 = inside the concrete (#1367)
            Assert.False(Entombed(server, pad.Position));
        }
    }

    [Fact]
    public void ALegacyStampedHull_IsCleanedBeforeTheTouchdownHeightIsRead()
    {
        // Where the ship parks and what the pad's median is are deterministic from the seed — probe them.
        int px, py, pz;
        string location;
        var probe = Started(out var probeRepo, "residueprobe", ship: true);
        using (probeRepo)
        {
            (px, py, pz) = probe.LandingPadForTest(0);
            location = probe.World.LocationId;
            probe.AddLocalPlayer("Pilot");
            var (origin, _) = probe.LandedShipBoundsForTest("Pilot");
            Assert.Equal(py + 1, origin.Y);
        }

        // A pre-ship-as-object save: born without the clean flag, with the old stamped hull persisted as a world
        // block edit two cells over the pad's median — inside today's ship volume AND inside the touchdown scan.
        var setup = Started(out var setupRepo, "residuelegacy", ship: true);
        setup.Stop();
        using (setupRepo)
        {
            var meta = setupRepo.LoadMetadata()!;
            meta.CreatedWithShipObjects = false;
            setupRepo.SaveMetadata(meta);
            setupRepo.SetBlock(location, new Vector3i(px, py + 2, pz), _content.GetBlock("iron_wall")!.NumericId.Value);
        }

        var legacy = Started(out var repo, "residuelegacy", ship: true);
        using (repo)
        {
            legacy.AddLocalPlayer("Pilot");
            var (origin, _) = legacy.LandedShipBoundsForTest("Pilot");
            Assert.Equal(py + 1, origin.Y); // on the median — the ghost hull was cleaned BEFORE the raised surface was read (#1367)
            Assert.True(legacy.World.GetBlock(new Vector3i(px, py + 2, pz)).IsAir);
        }
    }

    [Fact]
    public void TheRescue_FiresOncePerEntombment_WhenThePadFallbackIsWalledInToo()
    {
        var transport = new RecordingTransport();
        var server = Started(out var repo, "entombedpad", ship: false, transport);
        using (repo)
        {
            var (px, py, pz) = server.LandingPadForTest(0);
            var concrete = _content.GetBlock("concrete")!.NumericId;
            for (int y = py + 1; y <= py + 270; y++)
            {
                server.World.SetBlock(new Vector3i(px, y, pz), concrete); // a tower over the pad taller than the dig-out ever looks
            }

            var p = server.AddLocalPlayer("Buried");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(px + 0.5f, py - 6, pz + 0.5f); // sealed in the rock under the pad, the tower above
            Assert.True(Entombed(server, p.State.Position));

            for (int i = 0; i < 4; i++)
            {
                server.RunVoidRescueForTest(); // four seconds of rescue ticks
            }

            int Rescues() => transport.Messages().OfType<RespawnNotice>().Count(n => n.Reason == "@srv.misc.dug_out");
            Assert.Equal(1, Rescues()); // one rescue per episode (#1367) — not one per second into the same blocked spot
            Assert.Equal(py + 8 + 2, (int)Math.Floor(p.State.Position.Y)); // the pad fallback, itself inside the tower
            Assert.True(Entombed(server, p.State.Position));

            // The player digs themselves out: the episode ends, and nothing more is announced.
            server.World.SetBlock(new Vector3i(px, py + 10, pz), BlocksBeyondTheStars.Shared.Primitives.BlockId.Air);
            server.World.SetBlock(new Vector3i(px, py + 11, pz), BlocksBeyondTheStars.Shared.Primitives.BlockId.Air);
            server.RunVoidRescueForTest();
            Assert.False(Entombed(server, p.State.Position));
            Assert.Equal(1, Rescues());
        }
    }

    /// <summary>
    /// #1678: a player's hull is never stamped onto ground another hull already holds. Reproduces the desync
    /// from the report — a trader's ship standing on pad 0 with its reservation gone — and asserts the arriving
    /// player is parked on clear ground instead of inside it.
    /// </summary>
    [Fact]
    public void AHullOnThePad_WithoutItsReservation_SendsTheArrivingShipToAnotherPad()
    {
        var server = Started(out var repo, "twohulls", ship: true);
        using (repo)
        {
            var first = server.AddLocalPlayer("Host"); // loads the world and takes a pad
            Assert.True(server.LandTraderForTest(first.CurrentLocationId));
            Assert.True(server.ForceTraderPadForTest(first.CurrentLocationId, 1));
            Assert.True(server.MaterializeLandedTraderForTest());
            string traderOwner = server.PlacedHullOwnersForTest().First(o => o.StartsWith("npc:", StringComparison.Ordinal));
            var (traderOrigin, traderSize) = server.LandedShipBoundsForTest(traderOwner);

            // The reservation disappears while the hull keeps standing — what the sweep used to leave behind.
            Assert.True(server.ReleaseLandedTraderRecordForTest(first.CurrentLocationId));

            var second = server.AddLocalPlayer("Guest");
            var (origin, size) = server.LandedShipBoundsForTest("Guest");
            bool overlaps = origin.X < traderOrigin.X + traderSize.X && traderOrigin.X < origin.X + size.X
                         && origin.Z < traderOrigin.Z + traderSize.Z && traderOrigin.Z < origin.Z + size.Z;
            Assert.False(overlaps, $"the arriving hull at {origin} must not sit inside the parked one at {traderOrigin}");
            Assert.False(Entombed(server, second.State.Position));
        }
    }

    /// <summary>
    /// #1681: hulls are placed OBJECTS, not world blocks, so the entombment rescue was blind to them and
    /// SafeSpawnPoint kept returning the heal tank inside the very overlap that trapped the player. Two hulls
    /// on one pad is exactly what a player reported: "I cannot leave the ship and am stuck".
    /// </summary>
    [Fact]
    public void APlayerWedgedBetweenTwoHulls_IsMovedOutIntoTheOpen()
    {
        var server = Started(out var repo, "wedged", ship: true);
        using (repo)
        {
            var p = server.AddLocalPlayer("Host");
            var (origin, _) = server.LandedShipBoundsForTest("Host");

            // Stamp a second hull on the same pad — the pre-#1678 behaviour, reproduced on purpose.
            Assert.True(server.LandTraderForTest(p.CurrentLocationId));
            Assert.True(server.ForceTraderPadForTest(p.CurrentLocationId, server.AssignedPadForTest("Host")));
            Assert.True(server.MaterializeLandedTraderForTest(ignoreOverlap: true));
            Assert.Equal(2, server.PlacedHullOwnersForTest().Count);

            p.State.Position = server.HealTank; // where the respawn/rescue kept putting him back
            server.Tick(1.1);                   // the 1 Hz rescue

            var freed = p.State.Position;
            Assert.False(server.ShipInteriorContainsCellForTest((int)Math.Floor(freed.X), (int)Math.Floor(freed.Y), (int)Math.Floor(freed.Z)),
                $"the rescue must put the player outside every hull (ended at {freed}, own hull at {origin})");
        }
    }

    // ---------------- #3135: Lyxette's ship hovered a block up after loading in winter ----------------

    [Fact]
    public void WeatherSnowOnThePad_DoesNotLiftTheParkedShip()
    {
        var server = Started(out var repo, "snowpad", ship: true);
        using (repo)
        {
            var (px, py, pz) = server.LandingPadForTest(0);
            server.DepositWeatherSnowForTest(px + 4, py + 1, pz + 4); // a drift on a sampled corner column …
            server.DepositWeatherSnowForTest(px, py + 1, pz);         // … and one on the centre, both melting a minute later

            server.AddLocalPlayer("Host");
            var (origin, _) = server.LandedShipBoundsForTest("Host");
            Assert.Equal(py + 1, origin.Y); // on the ground, not on the snow
        }
    }

    [Fact]
    public void ABuiltBlockUnderSnow_StillRaisesTheParkedShip()
    {
        var server = Started(out var repo, "snowonpaving", ship: true);
        using (repo)
        {
            var (px, py, pz) = server.LandingPadForTest(0);
            // Under the hull's corner cell (#3286: a block BESIDE the hull no longer lifts it).
            server.World.SetBlock(new Vector3i(px + 2, py + 1, pz + 3), _content.GetBlock("concrete")!.NumericId); // built
            server.DepositWeatherSnowForTest(px + 2, py + 2, pz + 3); // snowed on

            server.AddLocalPlayer("Host");
            var (origin, _) = server.LandedShipBoundsForTest("Host");
            Assert.Equal(py + 2, origin.Y); // the paving counts (#1318), the snow on it does not
        }
    }

    [Fact]
    public void TheSnowPass_NeverLaysADriftInsideAParkedHull_NorInAPlayersCell()
    {
        var server = Started(out var repo, "snowhull", ship: true);
        using (repo)
        {
            var p = server.AddLocalPlayer("Host");
            var (origin, size) = server.LandedShipBoundsForTest("Host");
            int cx = origin.X + size.X / 2, cz = origin.Z + size.Z / 2;
            Assert.True(server.ShipInteriorContainsCellForTest(cx, origin.Y, cz)); // the cell over the pad ground is in the hull

            p.State.AboardShip = false;
            p.State.Position = new Vector3f(origin.X + 60.5f, origin.Y + 30f, origin.Z + 60.5f); // well away from the ship
            Assert.False(server.SnowSurfaceForTest(cx, origin.Y + 2, cz, out var inHull), $"a drift inside the hull at {inHull}");

            // Out in the open the same pass still finds the ground — and not where the player stands.
            int ox = origin.X - 8, oz = cz;
            Assert.True(server.SnowSurfaceForTest(ox, origin.Y + 2, oz, out var open), "snow settles beside the ship");
            p.State.Position = new Vector3f(open.X + 0.5f, open.Y, open.Z + 0.5f);
            Assert.False(server.SnowSurfaceForTest(ox, origin.Y + 2, oz, out _), "never in the cell the player stands in");
        }
    }

    [Fact]
    public void APlayerLoadedInsideTheHullFloor_IsLiftedOntoIt()
    {
        var server = Started(out var repo, "hullfloor", ship: true);
        using (repo)
        {
            var p = server.AddLocalPlayer("Host");
            var stand = p.State.Position; // standing aboard on the hull floor
            var (origin, _) = server.LandedShipBoundsForTest("Host");
            Assert.True(server.ShipInteriorContainsCellForTest((int)Math.Floor(stand.X), (int)Math.Floor(stand.Y), (int)Math.Floor(stand.Z)),
                $"the pilot spawns aboard (at {stand}, hull at {origin})");

            // The ship was re-parked a block higher than when the position was saved: the feet are in the floor now.
            p.State.Position = new Vector3f(stand.X, stand.Y - 1f, stand.Z);
            server.EnsureSafeSpawnForTest(p);

            Assert.Equal(Math.Floor(stand.Y), Math.Floor(p.State.Position.Y));
            Assert.Equal(stand.X, p.State.Position.X);
            Assert.Equal(stand.Z, p.State.Position.Z);
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
