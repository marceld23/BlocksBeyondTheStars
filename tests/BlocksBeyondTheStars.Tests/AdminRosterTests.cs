// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3505 the Players tab: the roster covers every body, observers are listed only to an admin who may see them, it streams
/// only to an open panel, and the jump — same body and another body — works only in the invisible admin mode.
/// </summary>
public sealed class AdminRosterTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>Another landable planet than the one the players start on.</summary>
    private static CelestialBody OtherPlanet(AdminTestWorld w, string here)
        => w.Server.Galaxy.Systems.SelectMany(s => s.Bodies)
            .First(b => b.Id != here && b.Kind == CelestialKind.Planet && !string.IsNullOrEmpty(b.PlanetType));

    /// <summary>Moves a fleet admin (visible) to another body the way <c>/goto</c> does.</summary>
    private static void Goto(AdminTestWorld w, BlocksBeyondTheStars.GameServer.PlayerSession who, CelestialBody body)
        => w.Server.HandleForTest(who, new AdminCommandIntent { Command = "goto", StringArg = body.Id + " 0 120 0" });

    [Fact]
    public void TheRoster_CoversPlayersOnOtherBodies_WithTheirPlaceAndState()
    {
        using var w = new AdminTestWorld(Content, fleetAdmins: "Traveller");
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        var player = w.Join("Justus");
        var traveller = w.Join("Traveller");
        var other = OtherPlanet(w, admin.CurrentLocationId);
        Goto(w, traveller, other);
        Assert.Equal(other.Id, traveller.CurrentLocationId);

        var roster = w.Server.AdminRosterForTest(admin).Players;

        Assert.Equal(new[] { "Justus", "Owner", "Traveller" }, roster.Select(r => r.Name).OrderBy(n => n));
        var row = roster.Single(r => r.Name == "Traveller");
        Assert.Equal(other.Id, row.LocationId);
        Assert.Equal(other.Name, row.LocationName);
        Assert.True(row.FleetAdmin);
        Assert.Equal(AdminPlayerStates.OnFoot, roster.Single(r => r.Name == "Justus").State);
        Assert.Equal("WorldAdmin", roster.Single(r => r.Name == "Owner").Role);
        Assert.Equal(player.State.Health, roster.Single(r => r.Name == "Justus").Health);
    }

    [Fact]
    public void AnObserver_IsListedToAnAdminWhoMaySeeIt_ButNotToAHostedWorldsOwner()
    {
        using var w = new AdminTestWorld(Content, hosted: true, fleetAdmins: "Operator");
        var owner = w.Join("Owner", PlayerRole.WorldAdmin);
        var op = w.Join("Operator");
        w.Server.SetAdminModeForTest(op, true);
        Assert.True(op.Spectating);

        Assert.DoesNotContain(w.Server.AdminRosterForTest(owner).Players, r => r.Name == "Operator");
        var seen = Assert.Single(w.Server.AdminRosterForTest(op).Players, r => r.Name == "Operator");
        Assert.Equal(AdminPlayerStates.Observer, seen.State);
    }

    [Fact]
    public void TheRoster_StreamsOnlyToAnOpenPanel_AndNeverToAPlayer()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        var player = w.Join("Justus");

        w.Server.SetAdminPanelForTest(player, true); // a player cannot open it
        w.Server.SetAdminPanelForTest(admin, true);  // nor an admin outside admin mode
        w.Server.TickAdminViewForTest();
        Assert.Empty(w.To<AdminRoster>(player));
        Assert.Empty(w.To<AdminRoster>(admin));

        w.Server.SetAdminModeForTest(admin, true);
        w.Server.SetAdminPanelForTest(admin, true);
        Assert.Single(w.To<AdminRoster>(admin));         // at once when it opens
        Assert.Single(w.To<AdminEventBacklog>(admin));
        Assert.Single(w.To<AdminReportList>(admin));
        w.Server.TickAdminViewForTest();
        Assert.Equal(2, w.To<AdminRoster>(admin).Count); // and once a second after
        Assert.Empty(w.To<AdminRoster>(player));

        w.Server.SetAdminPanelForTest(admin, false);
        w.Server.TickAdminViewForTest();
        Assert.Equal(2, w.To<AdminRoster>(admin).Count);
    }

    [Fact]
    public void TheJump_OnTheSameBody_LandsBehindAndAboveThePlayer()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        var player = w.Join("Justus");
        player.State.Position = AdminTestWorld.Offset(player, 30f, 0f, 30f);
        player.State.Yaw = 0f; // facing +Z: "behind" is -Z
        w.Server.SetAdminModeForTest(admin, true);

        w.Server.AdminJumpForTest(admin, new AdminJumpIntent { PlayerId = player.State.PlayerId });

        var at = admin.State.Position;
        Assert.InRange(at.Z - player.State.Position.Z, -5f, -3f);
        Assert.InRange(at.X - player.State.Position.X, -1f, 1f);
        Assert.True(at.Y >= player.State.Position.Y + 3f, $"the jump arrived at {at.Y}, the player stands at {player.State.Position.Y}");
        Assert.True(admin.Spectating);
        Assert.Contains(w.Server.AdminEventsForTest, e => e.TextKey == "admin.ev.cheat" && e.Args.Length > 1 && e.Args[1].Contains("Justus"));
    }

    [Fact]
    public void TheJump_ToAnotherBody_TravelsThereSilently()
    {
        using var w = new AdminTestWorld(Content, fleetAdmins: "Traveller");
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        var traveller = w.Join("Traveller");
        var other = OtherPlanet(w, admin.CurrentLocationId);
        Goto(w, traveller, other);
        w.Server.SetAdminModeForTest(admin, true);
        string home = admin.CurrentLocationId;

        w.Server.AdminJumpForTest(admin, new AdminJumpIntent { PlayerId = traveller.State.PlayerId });

        Assert.Equal(other.Id, admin.CurrentLocationId);
        Assert.True(admin.Spectating); // still invisible over there

        // Leaving admin mode brings the admin home — the jump was no free teleport.
        w.Server.SetAdminModeForTest(admin, false);
        Assert.Equal(home, admin.CurrentLocationId);
    }

    [Fact]
    public void APlayer_CannotJump()
    {
        using var w = new AdminTestWorld(Content);
        w.Join("Owner", PlayerRole.WorldAdmin);
        var player = w.Join("Justus");
        var start = player.State.Position;

        w.Server.HandlePayloadForTest(player.ConnectionId, BlocksBeyondTheStars.Networking.NetCodec.Encode(
            new AdminJumpIntent { LocationId = player.CurrentLocationId, HasPosition = true, X = start.X + 200, Y = start.Y, Z = start.Z }));

        Assert.Equal(start, player.State.Position);
        Assert.Contains("@srv.admin.jump_invisible_only", w.Said(player));
    }
}
