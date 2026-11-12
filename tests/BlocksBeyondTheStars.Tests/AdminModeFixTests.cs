// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The review fixes of the admin mode itself (#3502): <c>/players</c> and <c>/where</c> keep an observer from an admin who may
/// not see one; animals do not react to an observer; the ship's body is saved and restored with the entry point; admin mode
/// waits while something else holds the admin; a refused way back keeps admin mode (and a built-over entry cell is stepped
/// over); and watching a body is no progress.
/// </summary>
public sealed class AdminModeFixTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>A planet in the admin's own system other than the one they stand on (the home world's location id may be its
    /// legacy alias, e.g. "rocky" — compared by the body it resolves to).</summary>
    private static CelestialBody OtherPlanet(AdminTestWorld w, PlayerSession who)
    {
        var here = w.Server.Galaxy.FindBody(who.CurrentLocationId)!;
        return w.Server.Galaxy.Systems.Single(s => s.Id == here.SystemId).Bodies
            .First(b => b.Kind == CelestialKind.Planet && !string.IsNullOrEmpty(b.PlanetType) && b.Id != here.Id);
    }

    private static string BodyOf(BlocksBeyondTheStars.GameServer.GameServer server, string locationId)
        => server.Galaxy.FindBody(locationId)?.Id ?? locationId;

    private static void JumpTo(AdminTestWorld w, PlayerSession admin, CelestialBody body)
        => w.Server.AdminJumpForTest(admin, new AdminJumpIntent { LocationId = body.Id, HasPosition = true, X = 0f, Y = 140f, Z = 0f });

    // ---------------- 8: /players and /where ----------------

    [Fact]
    public void PlayersAndWhere_LeaveAnObserverOut_ForAnAdminWhoMayNotSeeOne()
    {
        using var w = new AdminTestWorld(Content, hosted: true, fleetAdmins: "Operator");
        var owner = w.Join("Owner", PlayerRole.WorldAdmin);
        var op = w.Join("Operator");
        w.Join("Justus");
        w.Server.SaveAllForTest(); // the operator has a record too
        w.Server.SetAdminModeForTest(op, true);
        w.Transport.Sent.Clear();

        w.Server.HandleForTest(owner, new AdminCommandIntent { Command = "players" });
        w.Server.HandleForTest(owner, new AdminCommandIntent { Command = "where", StringArg = "Operator" });
        w.Server.HandleForTest(op, new AdminCommandIntent { Command = "players" });

        var ownerLines = w.Said(owner);
        Assert.Contains(ownerLines, t => t.StartsWith("Justus", StringComparison.Ordinal));
        Assert.DoesNotContain(ownerLines, t => t.Contains("Operator", StringComparison.Ordinal));
        Assert.Contains("@srv.obs.unknown_player", ownerLines);
        Assert.Contains(w.Said(op), t => t.StartsWith("Operator", StringComparison.Ordinal)); // the fleet admin sees them
    }

    [Fact]
    public void PlayersAndWhere_GiveTheHostedOwnerTheBodyButNoPosition()
    {
        using var w = new AdminTestWorld(Content, hosted: true, fleetAdmins: "Operator");
        var owner = w.Join("Owner", PlayerRole.WorldAdmin);
        var op = w.Join("Operator");
        var kid = w.Join("Justus");
        kid.State.Position = new Vector3f(123f, 64f, -45f);
        w.Transport.Sent.Clear();

        w.Server.HandleForTest(owner, new AdminCommandIntent { Command = "players" });
        w.Server.HandleForTest(owner, new AdminCommandIntent { Command = "where", StringArg = "Justus" });
        w.Server.HandleForTest(op, new AdminCommandIntent { Command = "where", StringArg = "Justus" });

        var ownerLines = w.Said(owner);
        Assert.Contains(ownerLines, t => t.StartsWith("Justus", StringComparison.Ordinal));
        Assert.DoesNotContain(ownerLines, t => t.Contains("123", StringComparison.Ordinal) || t.StartsWith("/goto", StringComparison.Ordinal));
        Assert.Contains(w.Said(op), t => t.StartsWith("/goto", StringComparison.Ordinal)); // the fleet admin keeps the position
    }

    // ---------------- 9: animals ignore an observer ----------------

    [Fact]
    public void ASkittishAnimal_DoesNotFleeFromAnObserver_ButFromAPlayer()
    {
        using var w = new AdminTestWorld(Content, configure: c => { c.StartPlanet = "jungle"; c.Seed = 4242; });
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        var sp = w.Server.SpeciesRoster.First();
        sp.Habitat = CreatureHabitat.Land;
        sp.Legs = 4;
        sp.LocoStyle = LocomotionStyle.Strider;
        sp.Temperament = CreatureTemperament.Skittish;
        sp.Activity = CreatureActivity.Cathemeral;
        sp.SocialGroupSize = 1;

        const int cx = -120, cz = -120;
        int padY = BuildPad(w, cx, cz, 9);
        w.Server.SetAdminModeForTest(admin, true);
        admin.State.Position = new Vector3f(cx + 3f, padY + 1, cz + 0.5f); // inside the flee reflex
        string id = w.Server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f));
        w.Server.PauseCreatureForTest(id, 30f);
        var animal = w.Server.Creatures.First(c => c.Id == id);

        for (int i = 0; i < 10; i++)
        {
            w.Server.TickForTest(0.1);
        }

        Assert.NotEqual(MoveMode.Flee, animal.Loco.Mode);

        var player = w.Join("Justus");
        player.State.Position = admin.State.Position;
        for (int i = 0; i < 10 && animal.Loco.Mode != MoveMode.Flee; i++)
        {
            w.Server.TickForTest(0.1);
        }

        Assert.Equal(MoveMode.Flee, animal.Loco.Mode);
    }

    /// <summary>A stone floor well above the terrain around (<paramref name="cx"/>, <paramref name="cz"/>); returns its y.</summary>
    private static int BuildPad(AdminTestWorld w, int cx, int cz, int r)
    {
        int top = int.MinValue;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                for (int y = 200; y > -200; y--)
                {
                    if (!w.Server.World.GetBlock(new Vector3i(cx + dx, y, cz + dz)).IsAir)
                    {
                        top = Math.Max(top, y);
                        break;
                    }
                }
            }
        }

        int padY = top + 8;
        var stone = Content.GetBlock("stone")!.NumericId;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                w.Server.World.SetBlock(new Vector3i(cx + dx, padY, cz + dz), stone);
            }
        }

        return padY;
    }

    // ---------------- 10 + 15: the ship's body; no progress by watching ----------------

    [Fact]
    public void WatchingAnotherBody_SavesTheShipWhereItStood_AndIsNoProgress()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        string entryBody = admin.CurrentLocationId;
        var ship = admin.Ships[admin.ActiveShipId];
        string shipBody = ship.CurrentLocationId;
        var other = OtherPlanet(w, admin);
        admin.State.LandedBodies.Remove(other.Id);

        w.Server.SetAdminModeForTest(admin, true);
        JumpTo(w, admin, other);
        Assert.Equal(other.Id, admin.CurrentLocationId);
        Assert.DoesNotContain(other.Id, admin.State.LandedBodies); // #15: no quick-travel target by watching
        w.Server.SaveAllForTest();

        var after = w.Restart(new RecordingTransport());
        var again = after.AddLocalPlayer("Owner");
        Assert.Equal(entryBody, again.CurrentLocationId);
        Assert.Equal(shipBody, again.Ships[again.ActiveShipId].CurrentLocationId); // not the watched body: a respawn goes there
        again.State.AboardShip = false;
        after.KillPlayerForTest(again, "@srv.death.fall");
        Assert.Equal(BodyOf(after, entryBody), BodyOf(after, again.CurrentLocationId)); // the entry body, not the watched one
        Assert.NotEqual(other.Id, BodyOf(after, again.CurrentLocationId));
    }

    [Fact]
    public void LeavingAdminMode_PutsTheShipBackOnItsBody()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        string shipBody = admin.Ships[admin.ActiveShipId].CurrentLocationId;
        w.Server.SetAdminModeForTest(admin, true);
        JumpTo(w, admin, OtherPlanet(w, admin));

        w.Server.SetAdminModeForTest(admin, false);

        Assert.Equal(AdminModes.Off, admin.Admin.Mode);
        Assert.Equal(shipBody, admin.Ships[admin.ActiveShipId].CurrentLocationId);
    }

    // ---------------- 13: busy ----------------

    [Fact]
    public void AdminMode_WaitsWhileSeatedOrOnAMovingLift()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        admin.State.Seated = true;
        w.Server.SetAdminModeForTest(admin, true);
        Assert.Equal(AdminModes.Off, admin.Admin.Mode);

        admin.State.Seated = false;
        admin.MovingFallGraceUntil = double.MaxValue; // riding a moving platform
        w.Server.SetAdminModeForTest(admin, true);
        Assert.Equal(AdminModes.Off, admin.Admin.Mode);
        Assert.Equal(2, w.Said(admin).Count(t => t == "@srv.admin.mode_busy"));
        Assert.False(admin.Spectating);

        admin.MovingFallGraceUntil = 0;
        w.Server.SetAdminModeForTest(admin, true);
        Assert.Equal(AdminModes.Invisible, admin.Admin.Mode);
    }

    // ---------------- 14: the way back ----------------

    [Fact]
    public void ARefusedWayBack_KeepsAdminModeAndTheEntryPoint()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        string home = admin.CurrentLocationId;
        w.Server.SetAdminModeForTest(admin, true);
        admin.Admin.EntryLocationId = "no-such-body"; // a body the travel refuses

        w.Server.SetAdminModeForTest(admin, false);

        Assert.Equal(AdminModes.Invisible, admin.Admin.Mode);
        Assert.True(admin.Spectating);
        Assert.Equal("no-such-body", admin.Admin.EntryLocationId);
        Assert.Contains("@srv.admin.mode_return_failed", w.Said(admin));

        admin.Admin.EntryLocationId = home;
        w.Server.SetAdminModeForTest(admin, false);
        Assert.Equal(AdminModes.Off, admin.Admin.Mode);
        Assert.False(admin.Spectating);
    }

    [Fact]
    public void ABuiltOverEntryCell_PutsTheAdminOnTheFirstFreeCellAbove()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        var entry = admin.State.Position;
        w.Server.SetAdminModeForTest(admin, true);
        var stone = Content.GetBlock("stone")!.NumericId;
        int bx = (int)Math.Floor(entry.X), by = (int)Math.Floor(entry.Y), bz = (int)Math.Floor(entry.Z);
        for (int dy = 0; dy < 3; dy++)
        {
            w.Server.World.SetBlock(new Vector3i(bx, by + dy, bz), stone); // somebody built a pillar where the admin stood
        }

        w.Server.SetAdminModeForTest(admin, false);

        Assert.Equal(AdminModes.Off, admin.Admin.Mode);
        Assert.Equal(by + 3, (int)Math.Floor(admin.State.Position.Y));
        Assert.Equal(entry.X, admin.State.Position.X);
    }
}
