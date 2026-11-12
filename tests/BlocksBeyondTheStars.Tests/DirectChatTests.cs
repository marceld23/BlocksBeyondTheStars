// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3507 the direct chat: only the addressee and the admins see a line; a player cannot start a conversation (no whispers
/// between players), may answer an admin for 30 minutes after that admin's last line, with <c>/r</c> too; the screening
/// applies to a player's answers and, on an official hosted world, to the owner's lines as well.
/// </summary>
public sealed class DirectChatTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private const string PhoneLine = "call me 0151 2345678 ok"; // personal data: masked by the default chat screening

    private long _clockMs = 1_000_000;

    /// <summary>Moves the chat clock past the per-line throttle before every line.</summary>
    private void Tick(SvGameServer server, long ms = 1_000)
    {
        _clockMs += ms;
        server.SetChatClockForTest(_clockMs);
    }

    private static List<DirectChatMessage> Lines(AdminTestWorld w, PlayerSession who) => w.To<DirectChatMessage>(who);

    [Fact]
    public void AnAdminsLine_ReachesTheAddressee_TheSender_AndAdminsWithThePanelOpen_OnlyThem()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        var watcher = w.Join("Mira", PlayerRole.Admin);
        var player = w.Join("Justus");
        var bystander = w.Join("Jonah");
        w.Server.SetAdminModeForTest(admin, true); // #3502: an admin writes from the admin view
        w.Server.SetAdminModeForTest(watcher, true);
        w.Server.SetAdminPanelForTest(watcher, true);
        w.Transport.Sent.Clear();
        Tick(w.Server);

        w.Server.DirectChatForTest(admin, player.State.PlayerId, "Hello Justus, all fine?");

        var line = Assert.Single(Lines(w, player));
        Assert.True(line.FromAdmin);
        Assert.Equal("Owner", line.FromName);
        Assert.Equal("Justus", line.ToName);
        Assert.Single(Lines(w, admin));
        Assert.Single(Lines(w, watcher));
        Assert.Empty(Lines(w, bystander));
        Assert.DoesNotContain(w.Transport.Sent, x => x.Msg is ChatMessage); // never the open channel
    }

    [Fact]
    public void APlayer_CannotStartAConversation_NeitherWithAnotherPlayerNorWithAnAdmin()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        var player = w.Join("Justus");
        var friend = w.Join("Jonah");
        w.Transport.Sent.Clear();

        Tick(w.Server);
        w.Server.DirectChatForTest(player, friend.State.PlayerId, "psst");
        Tick(w.Server);
        w.Server.DirectChatForTest(player, admin.State.PlayerId, "hi admin");

        Assert.Empty(Lines(w, friend));
        Assert.Empty(Lines(w, admin));
        Assert.Equal(2, w.Said(player).Count(t => t == "@srv.dm.no_conversation"));
    }

    [Fact]
    public void APlayer_AnswersWithSlashR_UntilTheReplyWindowCloses()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        var player = w.Join("Justus");
        w.Server.SetAdminModeForTest(admin, true);
        Tick(w.Server);
        w.Server.DirectChatForTest(admin, player.State.PlayerId, "Did someone break your house?");

        Tick(w.Server);
        w.Server.HandlePayloadForTest(player.ConnectionId, NetCodec.Encode(new ChatIntent { Text = "/r yes, Jonah did" }));
        var answer = Assert.Single(Lines(w, admin), l => !l.FromAdmin);
        Assert.Equal("yes, Jonah did", answer.Text);
        Assert.Contains("yes, Jonah did", player.Chat.RecentChatLines); // kept as report evidence

        Tick(w.Server, SvGameServer.DirectReplyWindowMs + 1_000); // 30 minutes later
        w.Transport.Sent.Clear();
        w.Server.HandlePayloadForTest(player.ConnectionId, NetCodec.Encode(new ChatIntent { Text = "/r still there?" }));
        Assert.Empty(Lines(w, admin));
        Assert.Contains("@srv.dm.not_online", w.Said(player)); // past the window, the invisible admin reads as not online again
    }

    [Fact]
    public void APlayer_MayAnswerAnInvisibleAdminWhoWroteToThem_ButNobodyElseFindsThem()
    {
        using var w = new AdminTestWorld(Content, hosted: true, fleetAdmins: "Operator");
        var op = w.Join("Operator");
        var player = w.Join("Justus");
        var owner = w.Join("Owner", PlayerRole.WorldAdmin);
        w.Server.SetAdminModeForTest(op, true);
        Assert.True(op.Spectating);

        Tick(w.Server);
        w.Server.DirectChatForTest(owner, op.State.PlayerId, "are you there?"); // the hosted owner may not see the observer
        Assert.Contains("@srv.dm.not_online", w.Said(owner));

        Tick(w.Server);
        w.Server.DirectChatForTest(op, player.State.PlayerId, "Hi, I am the operator.");
        Tick(w.Server);
        w.Server.DirectChatForTest(player, op.State.PlayerId, "hello!");

        Assert.Contains(Lines(w, op), l => !l.FromAdmin && l.Text == "hello!");
    }

    [Fact]
    public void ThePlayersAnswer_IsScreened_TheSelfHostedAdminsLineIsNot()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        var player = w.Join("Justus");
        w.Server.SetAdminModeForTest(admin, true);
        Tick(w.Server);
        w.Server.DirectChatForTest(admin, player.State.PlayerId, PhoneLine);
        Assert.Equal(PhoneLine, Lines(w, player).Last().Text); // the host of a family world is not screened

        Tick(w.Server);
        w.Server.DirectChatForTest(player, admin.State.PlayerId, PhoneLine);
        string relayed = Lines(w, admin).Last(l => !l.FromAdmin).Text;
        Assert.DoesNotContain("2345678", relayed);
        Assert.Contains(w.Server.AdminEventsForTest, e => e.Category == AdminEventCategories.Moderation && e.ActorId == string.Empty
                                                         && e.TargetId == "Justus");
    }

    [Fact]
    public void OnAnOfficialHostedWorld_TheOwnersLinesAreScreenedToo()
    {
        using var w = new AdminTestWorld(Content, hosted: true);
        var owner = w.Join("Owner", PlayerRole.WorldAdmin);
        var player = w.Join("Justus");
        w.Server.SetAdminModeForTest(owner, true); // the visible variant
        Tick(w.Server);

        w.Server.DirectChatForTest(owner, player.State.PlayerId, PhoneLine);

        Assert.DoesNotContain("2345678", Lines(w, player).Single().Text);
    }

    /// <summary>#3502 review 11b: a direct line comes from the admin view only — outside admin mode an admin is a player here and
    /// cannot start a conversation (on any world, in either variant).</summary>
    [Fact]
    public void AnAdminOutsideAdminMode_CannotStartAConversation()
    {
        using var w = new AdminTestWorld(Content, hosted: true);
        var owner = w.Join("Owner", PlayerRole.WorldAdmin);
        var player = w.Join("Justus");
        w.Transport.Sent.Clear();
        Tick(w.Server);

        w.Server.DirectChatForTest(owner, player.State.PlayerId, "hi, come to my base");

        Assert.Empty(Lines(w, player));
        Assert.Contains("@srv.dm.admin_view_only", w.Said(owner));

        w.Server.SetAdminModeForTest(owner, true);
        Tick(w.Server);
        w.Server.DirectChatForTest(owner, player.State.PlayerId, "all fine here?");
        Assert.Single(Lines(w, player));
    }

    /// <summary>#3502 review 6: an admin whom a player reported never reads along in that player's conversation with another
    /// admin — that conversation may be about exactly that report.</summary>
    [Fact]
    public void AReportedAdmin_DoesNotReadAlong_InTheReportersConversation()
    {
        using var w = new AdminTestWorld(Content);
        var owner = w.Join("Owner", PlayerRole.WorldAdmin);
        var mira = w.Join("Mira", PlayerRole.Admin);
        var other = w.Join("Lena", PlayerRole.Admin);
        var player = w.Join("Justus");
        Tick(w.Server);
        w.Server.HandlePayloadForTest(player.ConnectionId, NetCodec.Encode(new ChatIntent { Text = "/report mira she bullies me" }));
        foreach (var admin in new[] { owner, mira, other })
        {
            w.Server.SetAdminModeForTest(admin, true);
            Tick(w.Server, 3_000);
            w.Server.SetAdminPanelForTest(admin, true);
        }

        w.Transport.Sent.Clear();
        Tick(w.Server);
        w.Server.DirectChatForTest(owner, player.State.PlayerId, "Tell me what happened with Mira.");
        Tick(w.Server);
        w.Server.DirectChatForTest(player, owner.State.PlayerId, "she broke my house");

        Assert.Empty(Lines(w, mira));
        Assert.Equal(2, Lines(w, other).Count); // an admin nobody reported reads along
        Assert.Equal(2, Lines(w, owner).Count);
    }
}
