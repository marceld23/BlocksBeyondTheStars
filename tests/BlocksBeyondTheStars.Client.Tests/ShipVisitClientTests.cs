// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The docking visit (#2723) from the guest's side, driven by the real <see cref="Core.NetworkClient"/> against the real
/// in-process server: the guest flies next to a friend who walks inside his parked ship, docks, goes over, and finds
/// the friend's ship and the friend there — then "back to my ship" brings them home into their own interior.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class ShipVisitClientTests
{
    private static GameContent LoadContent() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Fact]
    public void TheGuest_GoesOver_SeesTheHostsShipAndTheHost_AndComesHome()
    {
        using var h = new ClientServerHarness(LoadContent(), c =>
        {
            c.Rules.FreeSpaceFlight = true;
            c.Rules.ShipDocking = DockingMode.Free;
            c.Rules.SpaceNpcEnemies = AlienActivity.Off;
        });

        // The loopback has one client: what the server sends the in-process host arrives here too. The captures below
        // keep only what can be the guest's own (a reset naming the host as owner, the host's presence and ship).
        var resets = new List<WorldReset>();
        var hostShips = new List<LandedShipState>();
        var hostSeen = new List<PlayerPresence>();
        DockStatus? docked = null;
        h.Client.WorldResetReceived += m => resets.Add(m);
        h.Client.LandedShipReceived += m =>
        {
            if (m.PlayerId == "Host")
            {
                hostShips.Add(m);
            }
        };
        h.Client.PlayerPresenceReceived += m =>
        {
            if (m.PlayerId == "Host")
            {
                hostSeen.Add(m);
            }
        };
        h.Client.DockStatusChanged += m =>
        {
            if (m.Partner == "Host")
            {
                docked = m;
            }
        };

        h.Join("Guest");
        Assert.NotNull(h.JoinAccepted);
        string guestId = h.JoinAccepted!.PlayerId;
        h.Server.AddLocalPlayer("Host");
        foreach (var s in h.Server.Sessions.Values)
        {
            s.Ships[s.ActiveShipId].Modules.Add("docking_module");
        }

        h.Server.Sessions.Values.First(s => s.State.PlayerId == guestId).State.AboardShip = true;
        h.Server.EnterSpace(guestId);
        h.Server.EnterSpace("Host");
        h.Server.ShipMove("Host", 0f, 0f, 0f);
        h.Server.ShipMove(guestId, 12f, 0f, 0f);
        h.Server.EnterShipInterior("Host"); // "step inside, I'm coming over"

        h.Client.SendDockRequest("Host");
        Assert.True(h.PumpUntil(() => docked is { Docked: true }, 10), "the guest docks with the friend walking his parked ship");
        Assert.True(docked!.CanVisit);

        resets.Clear();
        hostShips.Clear();
        hostSeen.Clear();
        h.Client.SendVisitShip("Host");
        Assert.True(h.PumpUntil(() => resets.Any(r => r.ShipOwnerId == "Host"), 10), "the guest is loaded into the host's ship");
        var visit = resets.Last(r => r.ShipOwnerId == "Host");
        Assert.Equal("ship_interior", visit.PlanetType);
        Assert.True(h.PumpUntil(() => hostShips.Any(s => !s.Removed) && hostSeen.Count > 0, 20),
            "the host's ship object and the host himself reach the guest");
        Assert.Equal("ship:Host", hostShips.Last(s => !s.Removed).StructureId);
        Assert.Equal("Host", h.Server.VisitedHostOf(guestId));
        Assert.Equal(visit.WorldId, h.Client.CurrentWorldId); // the poses the client sends from here carry the host's world

        resets.Clear();
        var guest = h.Server.Sessions.Values.First(s => s.State.PlayerId == guestId);
        var stale = new Shared.Geometry.Vector3f(guest.State.Position.X, guest.State.Position.Y, guest.State.Position.Z - 30f);
        h.Client.SendEnterShip(); // "back to my ship" while visiting
        // …and, before the reset comes back, one more pose from the host's ship: outside the own hull at home, where
        // both interiors share their coordinates. It carries the host's world, so the server leaves it alone.
        h.Client.SendMove(stale, 0f, 0f);
        Assert.True(h.PumpUntil(() => resets.Count > 0, 10), "the guest is loaded back into their own ship");
        var home = resets.Last();
        Assert.Equal("ship_interior", home.PlanetType);
        Assert.Equal(string.Empty, home.ShipOwnerId);
        Assert.Null(h.Server.VisitedHostOf(guestId));
        Assert.Equal("shipint:" + guestId, guest.CurrentLocationId);
        h.Tick(0.2);
        Assert.NotEqual(stale, guest.State.Position);
        Assert.False(guest.State.InEva, "a pose from the friend's ship must not walk the guest out of their own hatch");
        Assert.True(h.Server.InShipInterior(guestId));
    }
}
