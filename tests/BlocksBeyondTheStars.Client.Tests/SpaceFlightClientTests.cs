// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The flight view's data path, driven by the real NetworkClient against the real in-process server: what the client
/// receives in <see cref="SpaceState"/> and how the Unity-free helpers the flight view runs every frame read it — a
/// co-op partner west of the origin (#2707), a collected drop leaving the scene (#2708) and a spacewalking partner with
/// the floating ship (#2709).
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class SpaceFlightClientTests
{
    private static GameContent LoadContent() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    /// <summary>The harness client's flight: the newest <see cref="SpaceState"/> addressed to it. The loopback has one
    /// client, so the states the server sends to the in-process partner players arrive here too — those list the
    /// harness pilot among their players and are skipped.</summary>
    private sealed class Flight
    {
        public string Id = string.Empty;
        public SpaceState? Last;
    }

    /// <summary>Joins, puts the pilot aboard, runs <paramref name="beforeLaunch"/> (fit modules) and launches.</summary>
    private static Flight Launch(ClientServerHarness h, Action? beforeLaunch = null)
    {
        var flight = new Flight();
        h.Client.SpaceStateReceived += s =>
        {
            if (flight.Id.Length > 0 && s.Players.All(p => p.PlayerId != flight.Id) && s.SystemPilots.All(p => p.PlayerId != flight.Id))
            {
                flight.Last = s;
            }
        };
        h.Join("Pilot");
        flight.Id = h.JoinAccepted!.PlayerId;
        h.Server.Sessions.Values.First(s => s.State.PlayerId == flight.Id).State.AboardShip = true;
        beforeLaunch?.Invoke();
        h.Server.EnterSpace(flight.Id);
        Assert.True(h.PumpUntil(() => flight.Last != null, 10), "the client should receive its space state");
        return flight;
    }

    [Fact]
    public void APartnerWestOfTheOrigin_IsSampledWhereTheServerSaysItIs()
    {
        // #2707: the flight view sampled every remote ship with int.MaxValue as "no wrap", which moved every negative X
        // ~2.1 billion units away — two friends next to an anomaly at X = -107 could not see each other.
        using var h = new ClientServerHarness(LoadContent(), c => c.Rules.FreeSpaceFlight = true);
        var flight = Launch(h);
        h.Server.AddLocalPlayer("Wingmate");
        h.Server.EnterSpace("Wingmate");

        // Mirror SpaceView.SyncRemotePlayers: one sample per received snapshot on the client clock, the same 0.3 s delay.
        var interp = new RemoteEntityInterpolator(0.3);
        double clock = 0;
        float reportedX = 0f;
        for (int i = 0; i < 8; i++)
        {
            reportedX = -106f + (i * 0.25f);
            h.Server.ShipMove("Wingmate", reportedX, 0f, 3f);
            h.Tick(0.25); // past the 0.2 s pilot beat, so every step re-broadcasts the poses
            clock += 0.25;
            // The client drains a bounded number of messages per poll — the partner's own traffic shares the loopback.
            float x = reportedX;
            Assert.True(h.PumpUntil(() => flight.Last!.Players.Any(p => p.PlayerId == "Wingmate" && p.X == x), 5, 0.01),
                $"the partner's pose {x} should reach the client");
            var wing = Assert.Single(flight.Last!.Players, p => p.PlayerId == "Wingmate");
            interp.Push(clock, new Vector3f(wing.X, wing.Y, wing.Z), wing.Yaw);
        }

        Assert.True(interp.Sample(clock, RemoteEntityInterpolator.NoWrap, out var drawn, out _));
        Assert.InRange(drawn.X, reportedX - 1f, reportedX + 0.001f); // a little behind the newest pose — never 2.1e9 off
        Assert.Equal(3f, drawn.Z, 3);
    }

    [Fact]
    public void ACollectedDrop_LeavesTheSnapshot_AndItsViewIsStale_WhileTheRocksStay()
    {
        // #2708: a drop has no "destroyed" message — missing from the next snapshot is its only exit. The view skipped
        // that scan while view-less rocks were around, so the collected drop stayed on screen.
        using var h = new ClientServerHarness(LoadContent(), c =>
        {
            c.Rules.FreeSpaceFlight = true;
            c.Rules.AsteroidDestruction = AsteroidDestructionMode.MiningOnly;
        });
        var flight = Launch(h, () =>
        {
            h.Server.Ship.Modules.Add("asteroid_breaker");
            h.Server.Ship.Modules.Add("tractor_beam"); // with a tractor, the smallest chunks float as salvage
        });
        string id = flight.Id;

        // Break rocks until a salvage drop floats (the tick first lets the weapon cooldown cycle, as in SpaceCombatTests).
        CombatEntity? drop = null;
        for (int i = 0; i < 24 && drop == null; i++)
        {
            h.Tick(2.0);
            var rock = h.Server.SpaceEntitiesFor(id).FirstOrDefault(e => e.Kind == CombatEntityKind.Asteroid);
            if (rock != null)
            {
                h.Server.FireWeapon(id, "asteroid_breaker", rock.Id);
            }

            drop = h.Server.SpaceEntitiesFor(id).FirstOrDefault(e => e.Kind == CombatEntityKind.ResourceDrop);
        }

        Assert.True(drop != null, "breaking rocks should leave a salvage drop; rejections: "
            + string.Join(", ", h.Rejections.Select(r => r.Reason)));
        h.Client.Poll();
        var before = flight.Last!;
        Assert.Contains(before.Entities, e => e.Id == drop!.Id);

        // The flight view's models: one per entity that gets one — asteroids and debris are voxel bodies without.
        var views = new Dictionary<string, object>();
        foreach (var e in before.Entities)
        {
            if (e.Kind != "Asteroid" && e.Kind != "Debris")
            {
                views[e.Id] = new object();
            }
        }

        Assert.True(views.ContainsKey(drop!.Id));

        // Fly onto the drop: the tractor stows it, and the next snapshot no longer lists it.
        h.Server.ShipMove(id, drop.Position.X, drop.Position.Y, drop.Position.Z);
        h.Tick(0.1);
        var after = flight.Last!;
        Assert.NotSame(before, after);
        Assert.DoesNotContain(after.Entities, e => e.Id == drop.Id);
        Assert.Contains(after.Entities, e => e.Kind == "Asteroid");

        var seen = new HashSet<string>(after.Entities.Select(e => e.Id));
        var stale = new List<string>();
        Assert.True(SpaceEntitySync.CollectStale(views, seen, stale) >= 1);
        Assert.Contains(drop.Id, stale);
    }

    [Fact]
    public void ASpacewalkingPartner_ArrivesWithTheSuitAndTheFloatingShip()
    {
        // #2709: the client receives the suit pose, the floating ship pose and the suit look of a partner outside.
        using var h = new ClientServerHarness(LoadContent(), c => c.Rules.FreeSpaceFlight = true);
        var flight = Launch(h);
        var mate = h.Server.AddLocalPlayer("Mate");
        h.Server.EnterSpace("Mate");
        h.Server.ShipMove("Mate", -124f, 0f, 8f, 180f);
        mate.State.InEva = true;
        h.Server.ShipMove("Mate", -127f, 1.5f, 9f, 0f);
        h.Tick(0.3);

        var partner = Assert.Single(flight.Last!.Players, p => p.PlayerId == "Mate");
        Assert.True(SpaceRadarMath.IsRadarPilot(partner.PlayerId, flight.Id)); // #2710: on the radar, spacewalk or not
        Assert.True(partner.Eva);
        Assert.Equal(-127f, partner.X, 3);
        Assert.True(partner.HasShipPose);
        Assert.Equal(-124f, partner.ShipX, 3);
        Assert.Equal(8f, partner.ShipZ, 3);
        Assert.Equal(180f, partner.ShipYaw, 3);
        Assert.NotEqual(0, partner.Skin | partner.Torso | partner.Arms | partner.Legs);
    }

    [Fact]
    public void AMateFlyingNearAnotherBody_ArrivesAsASystemPilot_AndIsLockable()
    {
        // #2737: a friend who launched from another body of the system flies in another flight instance — the solo pilot
        // still receives them (converted into its own frame) as a navigation target, never as a ship in the scene.
        using var h = new ClientServerHarness(LoadContent(), c => c.Rules.FreeSpaceFlight = true);
        var flight = Launch(h);
        var here = h.Server.Galaxy.FindBody(h.Server.Sessions.Values.First(s => s.State.PlayerId == flight.Id).CurrentLocationId);
        Assert.NotNull(here);
        var there = h.Server.Galaxy.AllBodies().First(b => b.SystemId == here!.SystemId && b.Id != here.Id
            && b.Kind == CelestialKind.Planet && !string.IsNullOrEmpty(b.PlanetType));

        h.Server.AddLocalPlayer("Mate");
        h.Server.SetInstantTravelForTest(true);
        Assert.True(h.Server.QuickTravelForTest("Mate", there.Id));
        h.Server.EnterSpace("Mate");
        h.Server.ShipMove("Mate", 4f, 2f, -6f);

        Assert.True(h.PumpUntil(() => flight.Last!.SystemPilots.Any(p => p.PlayerId == "Mate"), 30),
            "the solo pilot should hear about the mate within about a second");
        var mate = flight.Last!.SystemPilots.Single(p => p.PlayerId == "Mate");
        Assert.Equal(there.Id, mate.AnchorBodyId);
        Assert.Equal(4f + (there.SystemX - here!.SystemX) * SystemBodyLayout.FlightViewScale, mate.X, 2);
        Assert.DoesNotContain(flight.Last.Players, p => p.PlayerId == "Mate"); // no ghost ship, no docking

        // The lock offers the mate at any distance — a navigation target, friendly.
        var candidate = TargetCandidate.FromPilot(mate);
        Assert.Equal(TargetDisposition.Friendly, candidate.Disposition);
        Assert.Contains(SpaceTargeting.Order(new[] { candidate }, 0f, 0f, 0f, 130f, pingActive: false), c => c.Id == "Mate");
    }
}
