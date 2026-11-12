// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The admin view on the wire (#3502): the real client learns its role, enters admin mode, opens the panel and gets the
/// roster, the log backlog and the report list through the real codec; a direct line from the admin reaches a player and
/// the answer comes back as the client's reply target.
/// </summary>
public sealed class AdminViewClientTests
{
    [Fact]
    public void TheWorldsAdmin_SeesTheAdminView_AndTalksToAPlayer()
    {
        var content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        using var h = new ClientServerHarness(content, c => c.PlaceStarterShip = false, passObjects: false);
        var said = new System.Collections.Generic.List<string>();
        h.Client.ServerMessageReceived += m => said.Add(m.Text);
        h.Client.ActionRejected += m => said.Add(m.Reason);
        h.Join("Justus"); // the first joiner owns the world
        Assert.NotNull(h.JoinAccepted);
        Assert.True(h.PumpUntil(() => h.Client.HasAdminView), "the client never learned it may open the admin view");
        Assert.Equal((byte)PlayerRole.WorldAdmin, h.Client.Role);
        Assert.Equal(AdminModes.Invisible, h.Client.AdminModeAvailable);

        var me = h.Server.Sessions.Values.Single(s => s.State.PlayerId == h.JoinAccepted!.PlayerId);
        me.State.AboardShip = false;
        var jonah = h.Server.AddLocalPlayer("Jonah");
        jonah.State.AboardShip = false;

        h.Client.SendAdminMode(true);
        Assert.True(h.PumpUntil(() => h.Client.AdminMode == AdminModes.Invisible), "admin mode never started");
        h.Client.SendAdminPanel(true);
        Assert.True(h.PumpUntil(() => h.Client.AdminPlayers.Find("Jonah") is not null), "the roster never arrived");
        Assert.Contains(h.Client.AdminLog.Newest(), e => e.TextKey == "admin.ev.join" && e.ActorName == "Jonah");

        h.Client.SendDirectChat("Jonah", "Hello Jonah!");
        Assert.True(h.PumpUntil(() => h.Client.DirectChats.Find("Jonah") is not null), "the admin's line never came back");
        h.Server.HandlePayloadForTest(jonah.ConnectionId, NetCodec.Encode(new DirectChatIntent { ToPlayerId = "Justus", Text = "hi!" }));
        Assert.True(h.PumpUntil(() => h.Client.DirectChats.ReplyTargetId == "Jonah"), "the player's answer never arrived: "
            + string.Join(" / ", said) + " | lines: " + string.Join(" / ", h.Client.DirectChats.Threads.SelectMany(t => t.Lines).Select(l => l.FromId + ">" + l.ToId + ":" + l.Text)));

        // One chat slot per 700 ms of real time (#3187) — the direct line above took this player's. The admin has the admin
        // view and does not call an admin (#3502); the player does.
        Assert.False(h.Client.OffersCallAdmin);
        System.Threading.Thread.Sleep(BlocksBeyondTheStars.GameServer.PlayerSessionChat.MinLineIntervalMs + 100);
        h.Server.HandlePayloadForTest(jonah.ConnectionId, NetCodec.Encode(new CallAdminIntent { Text = "a test call" }));
        Assert.True(h.PumpUntil(() => h.Client.AdminReports.Reports.Count == 1), "the report list never arrived");
        Assert.Equal(AdminReportKinds.Help, h.Client.AdminReports.Reports[0].Kind);

        h.Client.SendAdminMode(false);
        Assert.True(h.PumpUntil(() => h.Client.AdminMode == AdminModes.Off), "admin mode never ended");
        Assert.Empty(h.Client.AdminPlayers.Players);
    }

    /// <summary>#3502 review 12: "Call an admin" is offered only when the server says it files it (an older server never
    /// does — the client then keeps "/admin" as the admin help) and only to a player who is no admin.</summary>
    [Fact]
    public void CallAnAdmin_IsOfferedOnlyWhenTheServerFilesIt_AndOnlyToAPlayer()
    {
        var content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        using var h = new ClientServerHarness(content, c => { c.PlaceStarterShip = false; c.NoPlayerAdmins = true; }, passObjects: false);
        var said = new System.Collections.Generic.List<string>();
        h.Client.ServerMessageReceived += m => said.Add(m.Text);
        Assert.False(h.Client.AdminCallSupported); // before the join — and what an older server leaves it at
        Assert.False(h.Client.TrySendCallAdmin("help"));

        h.Join("Justus"); // no player admins on this instance: a plain player
        Assert.NotNull(h.JoinAccepted);
        Assert.True(h.JoinAccepted!.AdminCallSupported);
        Assert.True(h.Client.AdminCallSupported);
        Assert.True(h.PumpUntil(() => h.Client.Role == (byte)PlayerRole.Player && !h.Client.HasAdminView));
        Assert.True(h.Client.OffersCallAdmin);
        Assert.False(h.Client.TrySendCallAdmin("   ")); // nothing to send: the chat shows the help instead

        System.Threading.Thread.Sleep(BlocksBeyondTheStars.GameServer.PlayerSessionChat.MinLineIntervalMs + 100);
        Assert.True(h.Client.TrySendCallAdmin("I am stuck"));
        Assert.True(h.PumpUntil(() => said.Contains("@srv.admin.call_sent")), "the call was never filed: " + string.Join(" / ", said));
    }
}
