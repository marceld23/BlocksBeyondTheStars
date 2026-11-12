// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3504: an invisible observer (the admin mode, or a fleet admin's <c>/spectate</c>) used to give itself away in four
/// places — slide doors opened for it, Crystal Net presence sensors counted it, the <c>/bump</c> snapshot (which can be
/// forwarded to the report inbox) listed it under <c>nearby.players</c>, and the admins' player-mode roster named it. One
/// test per channel, each against a visible baseline first. Plus the intent gates of the new admin intents.
/// </summary>
public sealed class ObserverLeakTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void ASlideDoor_DoesNotOpenForAnObserver()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        var spot = admin.State.Position;
        Assert.Contains(spot, w.Server.DoorOpenersForTest()); // visible: a door near them opens

        w.Server.SetAdminModeForTest(admin, true);

        Assert.DoesNotContain(admin.State.Position, w.Server.DoorOpenersForTest());
    }

    [Fact]
    public void ACrystalPresenceSensor_DoesNotCountAnObserver()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        Assert.Contains(admin.State.PlayerId, w.Server.CrystalPresencePlayersForTest());

        w.Server.SetAdminModeForTest(admin, true);

        Assert.DoesNotContain(admin.State.PlayerId, w.Server.CrystalPresencePlayersForTest());
    }

    [Fact]
    public void ABumpSnapshot_DoesNotListAnObserverAsANearbyPlayer()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        var player = w.Join("Justus");
        Assert.Contains("Owner", w.Server.BumpNearbyPlayerNamesForTest(player));

        w.Server.SetAdminModeForTest(admin, true);

        Assert.DoesNotContain("Owner", w.Server.BumpNearbyPlayerNamesForTest(player));
    }

    [Fact]
    public void TheAdminsPlayerModeRoster_DoesNotNameAnObserver()
    {
        using var w = new AdminTestWorld(Content, fleetAdmins: "Operator");
        var owner = w.Join("Owner", PlayerRole.WorldAdmin);
        var op = w.Join("Operator");

        w.Transport.Sent.Clear();
        w.Server.HandleForTest(op, new AdminCommandIntent { Command = "spectate", StringArg = "on" });

        var rules = w.To<ServerRules>(owner);
        Assert.NotEmpty(rules); // entering re-sent the roster to the admins
        Assert.DoesNotContain("Operator", rules.Last().PlayerModeNames);
        Assert.Contains("Owner", rules.Last().PlayerModeNames);
    }

    // ---------------- The gates of the new intents ----------------

    [Fact]
    public void TheAdminIntents_PassTheObserverFilter_AndTheHeldWorld()
    {
        object[] observerMay =
        {
            new AdminModeIntent(), new AdminPanelIntent(), new AdminJumpIntent(), new DirectChatIntent(), new AdminReportAction(),
            new CallAdminIntent(), new PlayerReportIntent(),
        };
        foreach (var intent in observerMay)
        {
            Assert.True(SvGameServer.SpectatorMayHandleForTest(intent), intent.GetType().Name + " is dropped while observing");
        }

        object[] pauseMay =
        {
            new AdminModeIntent(), new AdminPanelIntent(), new DirectChatIntent(), new AdminReportAction(), new CallAdminIntent(),
            new PlayerReportIntent(),
        };
        foreach (var intent in pauseMay)
        {
            Assert.True(SvGameServer.ChoicePendingMayHandleForTest(intent), intent.GetType().Name + " is dropped in a held world");
        }
    }

    [Fact]
    public void TheAdminButton_ReachesTheServer_FromAHeldSingleplayerWorld()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        w.Server.PauseForTest(admin, true);
        Assert.True(w.Server.IsPaused);

        w.Server.HandlePayloadForTest(admin.ConnectionId,
            BlocksBeyondTheStars.Networking.NetCodec.Encode(new AdminModeIntent { On = true }));

        Assert.Equal(AdminModes.Invisible, admin.Admin.Mode);
    }
}
