// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3503 admin mode: who may enter it and which variant they get (the invisible mode for world and fleet admins, the visible
/// panel-only variant for the owner of an official hosted world), that it works without cheats, that leaving it puts the
/// admin back where they entered it (also in the save), that mining while observing yields nothing, and that a new session
/// starts without it.
/// </summary>
public sealed class AdminModeTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void APlayer_IsRefused_AndTheClientIsToldThereIsNoAdminView()
    {
        using var w = new AdminTestWorld(Content);
        w.Join("Owner", PlayerRole.WorldAdmin);
        var player = w.Join("Justus");

        w.Server.SetAdminModeForTest(player, true);

        Assert.Equal(AdminModes.Off, player.Admin.Mode);
        Assert.False(player.Spectating);
        Assert.Contains("@srv.admin.mode_no_access", w.Said(player));
        Assert.Equal(AdminModes.Off, w.Server.AdminModeAvailableForTest(player));
    }

    [Fact]
    public void AWorldAdmin_EntersTheInvisibleMode_WithoutCheats()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        Assert.False(w.Server.RulesForTest.CheatsAllowed); // a hosted or family world never has cheats

        w.Server.SetAdminModeForTest(admin, true);

        Assert.Equal(AdminModes.Invisible, admin.Admin.Mode);
        Assert.True(admin.Spectating);
        Assert.True(admin.State.GodMode);
        var state = w.To<PlayerStateUpdate>(admin).Last();
        Assert.Equal(AdminModes.Invisible, state.AdminMode);
        Assert.Equal(AdminModes.Invisible, state.AdminModeAvailable);
        Assert.Equal((byte)PlayerRole.WorldAdmin, state.Role);
        Assert.Contains("@srv.admin.mode_on", w.Said(admin));
    }

    [Fact]
    public void OnAnOfficialHostedWorld_TheOwnerGetsTheVisibleVariant_TheFleetAdminTheInvisibleOne()
    {
        using var w = new AdminTestWorld(Content, hosted: true, fleetAdmins: "Operator");
        var owner = w.Join("Owner", PlayerRole.WorldAdmin);
        var op = w.Join("Operator");

        w.Server.SetAdminModeForTest(owner, true);
        w.Server.SetAdminModeForTest(op, true);

        Assert.Equal(AdminModes.Visible, owner.Admin.Mode);
        Assert.False(owner.Spectating); // no invisibility, no god mode, no flight
        Assert.False(owner.State.GodMode);
        Assert.Contains("@srv.admin.mode_visible_on", w.Said(owner));
        Assert.Equal(AdminModes.Invisible, op.Admin.Mode);
        Assert.True(op.Spectating);

        // The visible variant cannot jump.
        w.Server.AdminJumpForTest(owner, new AdminJumpIntent { PlayerId = op.State.PlayerId });
        Assert.Contains("@srv.admin.jump_invisible_only", w.Said(owner));
    }

    [Fact]
    public void LeavingAdminMode_PutsTheAdminBackWhereTheyEnteredIt()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        var entry = admin.State.Position;
        admin.State.Yaw = 90f;

        w.Server.SetAdminModeForTest(admin, true);
        admin.State.Position = AdminTestWorld.Offset(admin, 40f, 25f, -30f); // flew off while invisible
        admin.State.Yaw = 10f;
        w.Server.SetAdminModeForTest(admin, false);

        Assert.Equal(AdminModes.Off, admin.Admin.Mode);
        Assert.False(admin.Spectating);
        Assert.False(admin.State.GodMode);
        Assert.Equal(entry, admin.State.Position);
        Assert.Equal(90f, admin.State.Yaw);
        Assert.Equal(AdminModes.Off, w.To<PlayerStateUpdate>(admin).Last().AdminMode);
    }

    [Fact]
    public void InTheInvisibleMode_TheSaveKeepsTheEntryPoint_NotWhereTheAdminHovers()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        var entry = admin.State.Position;
        w.Server.SetAdminModeForTest(admin, true);
        var hover = AdminTestWorld.Offset(admin, 60f, 30f, 0f);
        admin.State.Position = hover;

        w.Server.SavePlayerRecordForTest(admin);

        Assert.Equal(entry, w.Repo.LoadPlayer(admin.State.PlayerId)!.Position); // a quit or a crash is no free teleport
        Assert.Equal(hover, admin.State.Position); // the live session stays where it is
    }

    [Fact]
    public void ANewSession_StartsWithoutAdminMode()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        w.Server.SetAdminModeForTest(admin, true);
        Assert.True(admin.Spectating);

        var fresh = new BlocksBeyondTheStars.GameServer.PlayerSession(4711, admin.State); // what a reconnect builds

        Assert.Equal(AdminModes.Off, fresh.Admin.Mode);
        Assert.False(fresh.Spectating);
        Assert.False(fresh.Admin.PanelOpen);
    }

    [Fact]
    public void AdminMode_StartsOnFoot_NotAboardTheShip()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        admin.State.AboardShip = true;

        w.Server.SetAdminModeForTest(admin, true);

        Assert.Equal(AdminModes.Off, admin.Admin.Mode);
        Assert.False(admin.Spectating);
        Assert.Contains("@srv.admin.mode_on_foot", w.Said(admin));
    }

    [Fact]
    public void MiningWhileObserving_RemovesTheBlock_ButYieldsNothing_AndIsLogged()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        w.Server.SetAdminModeForTest(admin, true);
        admin.State.Position = AdminTestWorld.Offset(admin, 50f, 60f, 50f); // up in the sky, away from the pad and any town
        var p = admin.State.Position;
        var cell = new Vector3i((int)Math.Floor(p.X) + 1, (int)Math.Floor(p.Y) + 1, (int)Math.Floor(p.Z));
        var dirt = Content.GetBlock("dirt")!;
        w.Server.World.SetBlock(cell, dirt.NumericId);
        int itemsBefore = admin.State.Inventory.Slots.Where(s => s is { IsEmpty: false }).Sum(s => s!.Count);

        for (int swing = 0; swing < 12 && !w.Server.World.GetBlock(cell).IsAir; swing++)
        {
            w.Server.HandlePayloadForTest(admin.ConnectionId, NetCodec.Encode(new MineBlockIntent { X = cell.X, Y = cell.Y, Z = cell.Z }));
        }

        Assert.True(w.Server.World.GetBlock(cell).IsAir, "the observer could not remove the block");
        int itemsAfter = admin.State.Inventory.Slots.Where(s => s is { IsEmpty: false }).Sum(s => s!.Count);
        Assert.Equal(itemsBefore, itemsAfter);
        var mined = w.Server.AdminEventsForTest.Where(e => e.TextKey == "admin.ev.mined").ToList();
        Assert.True(mined.Count == 1, "events: " + string.Join(" / ", w.Server.AdminEventsForTest.Select(e => e.TextKey + " " + string.Join(",", e.Args)))
            + " | said: " + string.Join(" / ", w.Said(admin)));
        Assert.True(mined[0].HasPosition);
        Assert.Equal(cell.Y, (int)mined[0].Y);
        Assert.Equal("dirt", mined[0].Args[1]);
    }

    [Fact]
    public void TheFleetAdminsSpectate_StillWorks_AndSpectateOffLeavesAdminModeTheProperWay()
    {
        using var w = new AdminTestWorld(Content, fleetAdmins: "Operator");
        var op = w.Join("Operator");
        var entry = op.State.Position;

        w.Server.HandleForTest(op, new AdminCommandIntent { Command = "spectate", StringArg = "on" });
        Assert.True(op.Spectating);
        Assert.Equal(AdminModes.Off, op.Admin.Mode);
        w.Server.HandleForTest(op, new AdminCommandIntent { Command = "spectate", StringArg = "off" });
        Assert.False(op.Spectating);

        w.Server.SetAdminModeForTest(op, true);
        op.State.Position = AdminTestWorld.Offset(op, 0f, 50f, 0f);
        w.Server.HandleForTest(op, new AdminCommandIntent { Command = "spectate", StringArg = "off" });

        Assert.False(op.Spectating);
        Assert.Equal(AdminModes.Off, op.Admin.Mode);
        Assert.Equal(entry, op.State.Position);
    }
}
