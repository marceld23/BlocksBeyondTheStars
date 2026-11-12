// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Client.Admin;
using BlocksBeyondTheStars.Networking.Messages;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The admin view's Unity-free models (#3502): the roster grouped by body, the event log (backlog, live events without
/// duplicates, the cap, the category filter, the line format), the direct-chat threads (per partner, the <c>/r</c> target,
/// unread counts, the reply window) and the report list (open first, the local Done).
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class AdminModelsTests
{
    private static NetAdminPlayer Player(string id, string body, string bodyName)
        => new() { PlayerId = id, Name = id, LocationId = body, LocationName = bodyName };

    [Fact]
    public void TheRoster_GroupsByBody_SortedByName()
    {
        var model = new AdminRosterModel();
        model.Apply(new AdminRoster
        {
            Players = new[] { Player("Zoe", "p2", "Vega"), Player("Anna", "p1", "Kepler"), Player("Ben", "p2", "Vega") },
        });

        var groups = model.ByBody();

        Assert.Equal(new[] { "Kepler", "Vega" }, groups.Select(g => g.LocationName));
        Assert.Equal(new[] { "Ben", "Zoe" }, groups[1].Players.Select(p => p.Name));
        Assert.Equal("p1", model.Find("Anna")!.LocationId);
        Assert.Null(model.Find("Nobody"));
        int version = model.Version;
        model.Clear();
        Assert.Empty(model.Players);
        Assert.True(model.Version > version);
    }

    private static NetAdminEvent Ev(long id, string category, string key = "admin.ev.join", params string[] args)
        => new() { Id = id, Category = category, TextKey = key, Args = args };

    [Fact]
    public void TheLog_TakesTheBacklog_ThenLiveEvents_WithoutDuplicates_NewestFirst()
    {
        var log = new AdminEventLogModel();
        log.ApplyBacklog(new[] { Ev(1, AdminEventCategories.Join), Ev(2, AdminEventCategories.Pvp) });

        Assert.True(log.Add(Ev(3, AdminEventCategories.Death)));
        Assert.False(log.Add(Ev(2, AdminEventCategories.Pvp))); // already there

        Assert.Equal(new long[] { 3, 2, 1 }, log.Newest().Select(e => e.Id));
        log.SetShown(AdminEventCategories.Pvp, false);
        Assert.Equal(new long[] { 3, 1 }, log.Newest().Select(e => e.Id));
        Assert.False(log.IsShown(AdminEventCategories.Pvp));
        log.SetShown(AdminEventCategories.Pvp, true);
        Assert.Equal(3, log.Newest().Count);
    }

    [Fact]
    public void TheLog_KeepsTheNewest500()
    {
        var log = new AdminEventLogModel();
        for (int i = 1; i <= AdminEventLogModel.Capacity + 25; i++)
        {
            log.Add(Ev(i, AdminEventCategories.Join));
        }

        Assert.Equal(AdminEventLogModel.Capacity, log.Count);
        Assert.Equal(AdminEventLogModel.Capacity + 25, log.Newest(1).Single().Id);
        Assert.False(log.Add(Ev(AdminEventLogModel.Capacity + 25, AdminEventCategories.Join)));
    }

    [Fact]
    public void AnEventLine_FillsItsPlaceholders_AndLooksUpKeyArguments()
    {
        var texts = new System.Collections.Generic.Dictionary<string, string>
        {
            ["admin.ev.death"] = "{0} died: {1}",
            ["admin.cause.fall"] = "a fall",
        };
        string Localize(string key) => texts.TryGetValue(key, out var t) ? t : "[" + key + "]";

        string line = AdminEventLogModel.Format(Ev(1, AdminEventCategories.Death, "admin.ev.death", "Justus", "@admin.cause.fall"), Localize);

        Assert.Equal("Justus died: a fall", line);
        Assert.Equal("x  y", AdminEventLogModel.Fill("x {0} y", Array.Empty<string>(), Localize)); // a missing argument is empty
    }

    /// <summary>#3502 review 17: a player's text in an event is literal — a doubled '@' (the server's escape) reads as one '@'
    /// and is never looked up, and a placeholder written inside an argument is never filled.</summary>
    [Fact]
    public void APlayersTextInAnEvent_IsLiteral_NeitherAKeyNorAPlaceholder()
    {
        var texts = new System.Collections.Generic.Dictionary<string, string>
        {
            ["admin.ev.report_help"] = "{0} calls an admin: {1}",
            ["srv.admin.mode_on"] = "SPOOFED",
        };
        string Localize(string key) => texts.TryGetValue(key, out var t) ? t : "[" + key + "]";

        Assert.Equal("Justus calls an admin: @srv.admin.mode_on", AdminEventLogModel.Fill(texts["admin.ev.report_help"],
            new[] { "Justus", "@@srv.admin.mode_on" }, Localize));
        Assert.Equal("{1}{0} calls an admin: x {0} {9}", AdminEventLogModel.Fill(texts["admin.ev.report_help"],
            new[] { "{1}{0}", "x {0} {9}" }, Localize));
        Assert.Equal("a {b} {} {x1} c", AdminEventLogModel.Fill("a {b} {} {x1} {0}", new[] { "c" }, Localize)); // no placeholder: as written
    }

    private static DirectChatMessage Line(string from, string to, bool fromAdmin, long unix, string text = "hi")
        => new() { FromId = from, FromName = from, ToId = to, ToName = to, FromAdmin = fromAdmin, UnixTime = unix, Text = text };

    [Fact]
    public void ThePlayersThreads_KeyByTheAdmin_SetTheReplyTarget_AndCountUnread()
    {
        var threads = new DirectChatThreads();

        threads.Add(Line("Owner", "Justus", fromAdmin: true, unix: 1000), "Justus");
        threads.Add(Line("Justus", "Owner", fromAdmin: false, unix: 1010), "Justus");

        var thread = Assert.Single(threads.Threads);
        Assert.Equal("Owner", thread.PartnerId);
        Assert.Equal(2, thread.Lines.Count);
        Assert.Equal(1, thread.Unread); // the own answer is not unread
        Assert.Equal("Owner", threads.ReplyTargetId);
        Assert.True(threads.CanAnswer("Owner", 1000 + DirectChatThreads.ReplyWindowSeconds));
        Assert.False(threads.CanAnswer("Owner", 1001 + DirectChatThreads.ReplyWindowSeconds));
        threads.MarkRead("Owner");
        Assert.Equal(0, threads.UnreadTotal);
    }

    [Fact]
    public void AnAdminsThreads_KeyByThePlayer_AlsoForAnotherAdminsConversation()
    {
        var threads = new DirectChatThreads();

        threads.Add(Line("Mira", "Justus", fromAdmin: true, unix: 5), "Owner");  // another admin writes to Justus
        threads.Add(Line("Justus", "Mira", fromAdmin: false, unix: 6), "Owner"); // and he answers
        threads.Add(Line("Jonah", "Owner", fromAdmin: false, unix: 7), "Owner"); // an answer to me

        Assert.Equal(new[] { "Jonah", "Justus" }, threads.Threads.Select(t => t.PartnerId));
        Assert.Equal(2, threads.Find("Justus")!.Lines.Count);
        Assert.Equal("Jonah", threads.ReplyTargetId);
    }

    [Fact]
    public void TheReportList_SortsOpenFirst_AndMarksDoneLocally()
    {
        var model = new AdminReportsModel();
        model.Apply(new AdminReportList
        {
            Reports = new[]
            {
                new NetAdminReport { Id = 3, Kind = AdminReportKinds.Help },
                new NetAdminReport { Id = 2, Kind = AdminReportKinds.Player },
                new NetAdminReport { Id = 1, Kind = AdminReportKinds.Paint, Done = true },
            },
        });
        Assert.Equal(2, model.OpenCount);

        model.MarkLocally(3, done: true);

        Assert.Equal(new long[] { 2, 3, 1 }, model.Reports.Select(r => r.Id));
        Assert.Equal(1, model.OpenCount);
        Assert.True(model.Find(3)!.Done);
    }
}
