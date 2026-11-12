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
/// #3508 the world's own report list: every route files a row (<c>/report</c>, the portal mirror, the content reports, the
/// F1 feedback title, "Call an admin"); the rows survive a restart; the reported player is never told and never sees a
/// report about themselves; "Call an admin" shares the report rate limit; only admins receive the list, and an admin outside
/// admin mode gets a notice instead.
/// </summary>
public sealed class WorldReportTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private long _clockMs = 2_000_000;

    private void Say(AdminTestWorld w, PlayerSession who, string text)
    {
        _clockMs += 1_000; // past the per-line throttle
        w.Server.SetChatClockForTest(_clockMs);
        w.Server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(new ChatIntent { Text = text }));
    }

    private void Send(AdminTestWorld w, PlayerSession who, object message)
    {
        _clockMs += 1_000;
        w.Server.SetChatClockForTest(_clockMs);
        w.Server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(message));
    }

    [Fact]
    public void EveryRoute_FilesARow()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        var reporter = w.Join("Justus");
        var rude = w.Join("Jonah");
        Say(w, rude, "/admin hello"); // Jonah calls an admin too — and his line is evidence only if he chatted

        Say(w, reporter, "/report Jonah he broke my house");
        Send(w, reporter, new PlayerReportIntent { TargetName = "Jonah", Note = "via the portal", AlsoFiledWithPortal = true });
        w.Server.ContentReportForTest("paint", reporter, 12, "Jonah", "Jonah", 1, 2, 3);
        w.Server.ContentReportForTest("shape", reporter, 7, "Jonah", "Jonah", 4, 5, 6);
        w.Server.ContentReportForTest("texture", reporter, 0, "Jonah", "Jonah / stone", 7, 8, 9);

        var rows = w.Repo.ListPlayerReports();
        Assert.Equal(new[] { "help", "player", "player", "paint", "shape", "texture" }, rows.Select(r => r.Kind));
        var report = rows[1];
        Assert.Equal("Justus", report.ReporterName);
        Assert.Equal("Jonah", report.TargetId);
        Assert.Equal("he broke my house", report.Note);
        Assert.True(report.HasPosition);
        Assert.Equal("via the portal", rows[2].Note);
        Assert.Equal("#12", rows[3].Note);
        Assert.Equal(1.5f, rows[3].X);
        Assert.Equal("stone", rows[5].Note);
        Assert.Equal("Jonah", rows[5].TargetName);
        Assert.Contains("@srv.report.sent:Jonah", w.Said(reporter));
        Assert.Single(w.Said(reporter), t => t.StartsWith("@srv.report.sent", StringComparison.Ordinal)); // the mirror stays silent
    }

    [Fact]
    public void F1Feedback_FilesOnlyTheTitleAndThePlace_NeverTheDescription()
    {
        using var w = new AdminTestWorld(Content);
        w.Join("Owner", PlayerRole.WorldAdmin);
        var player = w.Join("Justus");
        Send(w, player, new BumpReport
        {
            Description = "[feedback] Door stuck — my mail is kid@example.org and the door at my base",
            FeedbackTitle = "Door stuck",
        });

        var row = Assert.Single(w.Repo.ListPlayerReports());
        Assert.Equal(AdminReportKinds.Feedback, row.Kind);
        Assert.Equal("Door stuck", row.Note);
        Assert.True(row.HasPosition);
        Assert.Equal(player.State.Position.X, row.X);
        Assert.DoesNotContain("example.org", row.Note + row.Evidence + row.TargetName);
    }

    [Fact]
    public void Rows_SurviveARestart_AndTheirState()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        var player = w.Join("Justus");
        w.Server.CallAdminForTest(player, "I am lost");
        long id = w.Repo.ListPlayerReports().Single().Id;
        w.Server.AdminReportActionForTest(admin, id, done: true);

        var after = w.Restart(new RecordingTransport());
        var owner = after.AddLocalPlayer("Owner");
        owner.State.Role = PlayerRole.WorldAdmin;

        var listed = Assert.Single(after.AdminReportListForTest(owner).Reports);
        Assert.Equal("I am lost", listed.Note);
        Assert.True(listed.Done);
        Assert.Equal("Owner", listed.HandledBy);
    }

    [Fact]
    public void TheReportedPlayer_IsNeverTold_AndAnAdminNeverSeesAReportAboutThemselves()
    {
        using var w = new AdminTestWorld(Content);
        var owner = w.Join("Owner", PlayerRole.WorldAdmin);
        var mira = w.Join("Mira", PlayerRole.Admin);
        var reporter = w.Join("Justus");
        w.Transport.Sent.Clear();

        Say(w, reporter, "/report Mira she is mean");

        Assert.DoesNotContain(w.To<ServerMessage>(mira), m => m.Text == "@srv.admin.new_report");
        Assert.Empty(w.To<AdminReportList>(mira));
        Assert.Empty(w.Server.AdminReportListForTest(mira).Reports);
        Assert.Single(w.Server.AdminReportListForTest(owner).Reports);
        Assert.Contains(w.To<ServerMessage>(owner), m => m.Text == "@srv.admin.new_report"); // not in admin mode: a notice

        w.Server.SetAdminModeForTest(mira, true);
        w.Transport.Sent.Clear();
        w.Server.SetAdminPanelForTest(mira, true);
        Assert.DoesNotContain(w.To<AdminEventBacklog>(mira).Single().Events, e => e.Category == AdminEventCategories.Report);
    }

    [Fact]
    public void CallAnAdmin_SharesTheReportRateLimit()
    {
        using var w = new AdminTestWorld(Content);
        w.Join("Owner", PlayerRole.WorldAdmin);
        var player = w.Join("Justus");

        for (int i = 0; i < 5; i++)
        {
            Say(w, player, "/admin help " + i);
        }

        Assert.Equal(3, w.Repo.ListPlayerReports().Count);
        Assert.Contains("@srv.report.too_many", w.Said(player));
        Assert.Equal(3, w.Said(player).Count(t => t == "@srv.admin.call_sent"));
    }

    [Fact]
    public void OnlyAdminsReceiveTheList_AndAnOpenPanelGetsItOnChange()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        var player = w.Join("Justus");
        var other = w.Join("Jonah");
        w.Server.SetAdminModeForTest(admin, true);
        w.Server.SetAdminPanelForTest(admin, true);
        w.Transport.Sent.Clear();

        w.Server.CallAdminForTest(player, "where is the shop?");

        Assert.Single(w.To<AdminReportList>(admin));
        Assert.DoesNotContain(w.To<ServerMessage>(admin), m => m.Text == "@srv.admin.new_report"); // the panel shows it
        Assert.Empty(w.To<AdminReportList>(player));
        Assert.Empty(w.To<AdminReportList>(other));

        w.Server.AdminReportActionForTest(player, w.Repo.ListPlayerReports().Single().Id, done: true); // refused
        Assert.Equal("open", w.Repo.ListPlayerReports().Single().State);
    }

    /// <summary>#3502 review 7b: the answer never tells whether an admin is online — the same neutral line either way.</summary>
    [Fact]
    public void TheCallerGetsTheSameAnswer_WhetherAnAdminIsOnlineOrNot()
    {
        using var w = new AdminTestWorld(Content);
        var player = w.Join("Justus");

        Send(w, player, new CallAdminIntent { Text = "I am lost" });
        var alone = w.Said(player).ToList();
        var owner = w.Join("Owner", PlayerRole.WorldAdmin);
        w.Server.SetAdminModeForTest(owner, true); // even invisible
        w.Transport.Sent.Clear();
        Send(w, player, new CallAdminIntent { Text = "still lost" });

        Assert.Equal(new[] { "@srv.admin.call_sent" }, alone);
        Assert.Equal(new[] { "@srv.admin.call_sent" }, w.Said(player));
        Assert.Equal(2, w.Repo.ListPlayerReports().Count);
    }

    /// <summary>#3502 review 16: "/admin" without text, or from an admin (who has the admin view), files nothing.</summary>
    [Fact]
    public void AnEmptyCall_OrOneFromAnAdmin_FilesNothing()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        var player = w.Join("Justus");

        Say(w, player, "/admin");
        Say(w, player, "/admin    ");
        Say(w, admin, "/admin help me");

        Assert.Empty(w.Repo.ListPlayerReports());
        Assert.Equal(2, w.Said(player).Count(t => t == "@srv.admin.call_usage"));
        Assert.Contains("@srv.admin.call_is_admin", w.Said(admin));
    }
}
