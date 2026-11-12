// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Moderation;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The world's own report list (#3508). Every report route used to end at the operators or the developers — a world admin
/// saw none of them. Now each one ALSO files a row in the world's <c>player_report</c> table, and the admins of this world
/// see the list in the admin view:
/// <list type="bullet">
/// <item><c>/report Name [note]</c> and the Report button (Ship → Alliances) — on an official hosted world the client files
/// with the portal and mirrors it here (<see cref="PlayerReportIntent.AlsoFiledWithPortal"/>), which is not forwarded again;</item>
/// <item><c>/reportpaint</c>, <c>/reportshape</c>, <c>/reporttexture</c> — with the place ("Jump there");</item>
/// <item>F1/F2 feedback as a short line: the title field and the reporter's position only — never the description, the
/// screenshot or the e-mail;</item>
/// <item>"Call an admin" (<see cref="CallAdminIntent"/>, chat <c>/admin Text</c>) — help without naming a player, rate-limited
/// like <c>/report</c>.</item>
/// </list>
/// The routes themselves are in <c>GameServerAdminReportRoutes.cs</c>; this part keeps the list. Admins with the panel open
/// get it (<see cref="AdminReportList"/>) whenever it changes; the others a notice. A report about an admin is never shown to
/// that admin (player ids compare case-insensitively, like names), and the reported player is never told anything. The RAM
/// list holds at most <see cref="WorldReportRamCap"/> rows and forgets done rows after <see cref="DoneReportRetentionDays"/>
/// days (the save keeps them).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How many rows the list sends at most (the open ones first, then the newest done ones).</summary>
    private const int AdminReportListCap = 200;

    /// <summary>How many rows the server keeps in RAM: past it the oldest done row goes first, then the oldest open one.</summary>
    private const int WorldReportRamCap = 500;

    /// <summary>A done row older than this (since it was handled) is not read back from the save any more.</summary>
    private const int DoneReportRetentionDays = 90;

    /// <summary>Report actions (Done / Reopen) one admin may send inside <see cref="ReportActionWindowSeconds"/>.</summary>
    private const int MaxReportActionsPerWindow = 10;

    private const double ReportActionWindowSeconds = 10.0;

    /// <summary>A report toggled again within this many seconds of its last logged action adds no further log event.</summary>
    private const double ReportToggleQuietSeconds = 5.0;

    /// <summary>The world's report rows, oldest first — read from the save the first time they are needed.</summary>
    private List<StoredPlayerReport>? _worldReports;

    /// <summary>Per report id, the admin clock (seconds) of its last logged Done / Reopen event.</summary>
    private readonly Dictionary<long, double> _reportEventAt = new();

    private List<StoredPlayerReport> WorldReports => _worldReports ??= LoadWorldReports();

    private static long UnixNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Two player ids name the same player: ids are player names, and names compare case-insensitively everywhere
    /// (the join's name reservation, <c>/where</c>, the hosted token).</summary>
    private static bool SamePlayer(string? a, string? b)
        => !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The rows the list starts with: the save's, without the done ones past the retention, capped.</summary>
    private List<StoredPlayerReport> LoadWorldReports()
    {
        long cutoff = UnixNow() - (DoneReportRetentionDays * 24L * 3600L);
        var kept = _repo.ListPlayerReports()
            .Where(r => r.State != "done" || Math.Max(r.HandledUnix, r.CreatedUnix) >= cutoff)
            .ToList();
        TrimWorldReports(kept);
        return kept;
    }

    /// <summary>Keeps at most <see cref="WorldReportRamCap"/> rows (oldest first): drops the oldest done row first, then the
    /// oldest open one.</summary>
    private static void TrimWorldReports(List<StoredPlayerReport> rows)
    {
        while (rows.Count > WorldReportRamCap)
        {
            int done = rows.FindIndex(r => r.State == "done");
            rows.RemoveAt(done >= 0 ? done : 0);
        }
    }

    /// <summary>A free text a player wrote for the report list (a note, a feedback title) as the world's chat screening lets it
    /// through — masked, or empty when it would be blocked. Quiet: the text already went where it was going (the operators,
    /// the developers), so the writer is not told and no filter hit is counted; only what the admins of the world read is
    /// cleaned — on an official hosted world that admin is a stranger.</summary>
    private string ScreenForAdmins(string text)
    {
        var mode = EffectiveChatMode;
        if (mode == ChatMode.Open || string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var result = ChatContentScreen.Screen(text, mode);
        return result.Verdict switch
        {
            ChatVerdict.Block => string.Empty,
            ChatVerdict.Mask => result.Text,
            _ => text,
        };
    }

    /// <summary>Files one row: persisted, kept, logged, an event, and every online admin is told — except the one it is
    /// about. <paramref name="staffOnly"/>: the event is about the staff (a fleet admin, an observer) and stays out of the log
    /// of an admin who may not see observers.</summary>
    private void FileWorldReport(StoredPlayerReport row, bool staffOnly = false)
    {
        row.CreatedUnix = UnixNow();
        row.State = "open";
        var reports = WorldReports; // read the save first — a first read after the insert would hold the new row twice
        _repo.AddPlayerReport(row);
        reports.Add(row);
        TrimWorldReports(reports);
        _log.Info($"World report #{row.Id} ({row.Kind}) by '{StripControlChars(row.ReporterName)}'" +
                  (row.TargetName.Length > 0 ? $" about '{StripControlChars(row.TargetName)}'" : string.Empty) + ".");

        var reporter = FindSessionByPlayerId(row.ReporterId);
        var ev = AdminEventAbout(AdminEventCategories.Report, "admin.ev.report_" + row.Kind, reporter, null,
            row.ReporterName, row.Kind is AdminReportKinds.Help or AdminReportKinds.Feedback ? row.Note : row.TargetName);
        ev.TargetId = row.TargetId;
        ev.TargetName = row.TargetName;
        CopyPlace(row, ev);
        LogAdminEvent(ev, staffOnly, writeLog: false); // the line above is its log line

        foreach (var admin in _sessions.Values)
        {
            if (!admin.Joined || !MayUseAdminView(admin) || SamePlayer(admin.State.PlayerId, row.TargetId))
            {
                continue; // nobody hears about a report against them
            }

            if (admin.Admin.PanelOpen)
            {
                SendAdminReportList(admin);
            }
            else if (!SamePlayer(admin.State.PlayerId, row.ReporterId))
            {
                Send(admin, new ServerMessage { Text = "@srv.admin.new_report" }); // the reporter already had the confirmation
            }
        }
    }

    private static void CopyPlace(StoredPlayerReport row, NetAdminEvent ev)
    {
        ev.LocationId = row.Planet;
        ev.HasPosition = row.HasPosition;
        ev.X = row.X;
        ev.Y = row.Y;
        ev.Z = row.Z;
    }

    /// <summary>A new row with the reporter and the reporter's place.</summary>
    private static StoredPlayerReport NewWorldReport(string kind, PlayerSession reporter) => new()
    {
        Kind = kind,
        ReporterId = reporter.State.PlayerId,
        ReporterName = reporter.State.Name ?? string.Empty,
        Planet = reporter.CurrentLocationId,
        HasPosition = true,
        X = reporter.State.Position.X,
        Y = reporter.State.Position.Y,
        Z = reporter.State.Position.Z,
    };

    /// <summary>A paint / shape / texture report's row (from <c>ForwardContentReport</c>): the owner and the reported cell.</summary>
    private void FileContentReportRow(string kind, PlayerSession reporter, int designId, string ownerId, string ownerName,
        string planet, int x, int y, int z)
    {
        var row = NewWorldReport(kind, reporter);
        row.TargetId = ownerId;
        row.TargetName = ownerName;
        row.Note = designId > 0 ? "#" + designId.ToString(CultureInfo.InvariantCulture) : string.Empty;
        int slash = ownerName.IndexOf(" / ", StringComparison.Ordinal);
        if (kind == AdminReportKinds.Texture && slash >= 0)
        {
            row.TargetName = ownerName.Substring(0, slash); // the texture route names "owner / key"
            row.Note = ownerName.Substring(slash + 3);
        }

        row.Planet = planet;
        row.X = x + 0.5f;
        row.Y = y;
        row.Z = z + 0.5f;
        FileWorldReport(row);
    }

    // ---------------- The list ----------------

    /// <summary>The list as <paramref name="viewer"/> may see it: the open rows first, then the newest done ones; never a
    /// report about the viewer; without positions for the visible variant (<see cref="SeesPositions"/>).</summary>
    private AdminReportList BuildAdminReportList(PlayerSession viewer)
    {
        string me = viewer.State.PlayerId;
        bool withPlace = SeesPositions(viewer);
        return new AdminReportList
        {
            Reports = WorldReports
                .Where(r => !SamePlayer(r.TargetId, me))
                .OrderBy(r => r.State == "done" ? 1 : 0)
                .ThenByDescending(r => r.Id)
                .Take(AdminReportListCap)
                .Select(r => ToNetReport(r, withPlace))
                .ToArray(),
        };
    }

    private static NetAdminReport ToNetReport(StoredPlayerReport r, bool withPlace) => new()
    {
        Id = r.Id,
        Kind = r.Kind,
        ReporterId = r.ReporterId,
        ReporterName = r.ReporterName,
        TargetId = r.TargetId,
        TargetName = r.TargetName,
        Note = r.Note,
        LocationId = r.Planet,
        HasPosition = withPlace && r.HasPosition,
        X = withPlace ? r.X : 0f,
        Y = withPlace ? r.Y : 0f,
        Z = withPlace ? r.Z : 0f,
        Evidence = r.Evidence.Length == 0 ? Array.Empty<string>() : r.Evidence.Split('\n'),
        CreatedUnix = r.CreatedUnix,
        Done = r.State == "done",
        HandledBy = r.HandledBy,
    };

    private void SendAdminReportList(PlayerSession viewer) => Send(viewer, BuildAdminReportList(viewer));

    /// <summary>An admin marks a report done (or open again): persisted, an event, and every open panel gets the new list.
    /// At most <see cref="MaxReportActionsPerWindow"/> per <see cref="ReportActionWindowSeconds"/>; a change back and forth
    /// within <see cref="ReportToggleQuietSeconds"/> is logged once.</summary>
    private void HandleAdminReportAction(PlayerSession session, AdminReportAction action)
    {
        if (!MayUseAdminView(session))
        {
            Reject(session, "admin", "@srv.admin.mode_no_access");
            return;
        }

        if (!TakeReportActionSlot(session))
        {
            return; // a flood of clicks — the list the panel shows stays the server's
        }

        var row = WorldReports.FirstOrDefault(r => r.Id == action.Id);
        string state = action.Done ? "done" : "open";
        if (row is null || SamePlayer(row.TargetId, session.State.PlayerId) || row.State == state)
        {
            return; // unknown, about this admin (whom the list never showed it), or nothing changes
        }

        row.State = state;
        row.HandledBy = session.State.Name ?? string.Empty;
        row.HandledUnix = UnixNow();
        _repo.SetPlayerReportState(row.Id, row.State, row.HandledBy, row.HandledUnix);
        double now = AdminClockSeconds;
        if (!_reportEventAt.TryGetValue(row.Id, out double last) || now - last >= ReportToggleQuietSeconds)
        {
            _reportEventAt[row.Id] = now;
            var ev = AdminEventAbout(AdminEventCategories.Report, action.Done ? "admin.ev.report_done" : "admin.ev.report_reopened",
                session, null, row.HandledBy, row.ReporterName);
            ev.TargetId = row.TargetId;
            ev.TargetName = row.TargetName;
            LogAdminEvent(ev);
        }

        foreach (var admin in _sessions.Values)
        {
            if (admin.Joined && admin.Admin.PanelOpen && MayUseAdminView(admin))
            {
                SendAdminReportList(admin);
            }
        }
    }

    /// <summary>Spends one of this admin's report-action slots (see <see cref="HandleAdminReportAction"/>).</summary>
    private bool TakeReportActionSlot(PlayerSession session)
    {
        double now = AdminClockSeconds;
        var times = session.Admin.ReportActionsAt;
        times.RemoveAll(t => now - t >= ReportActionWindowSeconds);
        if (times.Count >= MaxReportActionsPerWindow)
        {
            return false;
        }

        times.Add(now);
        return true;
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: the report list as this admin would receive it.</summary>
    internal AdminReportList AdminReportListForTest(PlayerSession viewer) => BuildAdminReportList(viewer);

    /// <summary>Test seam: how many report rows the server holds in RAM.</summary>
    internal int WorldReportCountForTest => WorldReports.Count;

    /// <summary>Test seam: the shared path of <c>/reportpaint</c>, <c>/reportshape</c> and <c>/reporttexture</c> once each found
    /// what it reports (<c>ForwardContentReport</c>).</summary>
    internal void ContentReportForTest(string kind, PlayerSession session, int designId, string ownerId, string ownerName, int x, int y, int z)
        => ForwardContentReport(kind, session, designId, ownerId, ownerName, session.CurrentLocationId, x, y, z);

    /// <summary>Test seam: a report action as if the client sent <see cref="AdminReportAction"/>.</summary>
    internal void AdminReportActionForTest(PlayerSession session, long id, bool done)
        => HandleAdminReportAction(session, new AdminReportAction { Id = id, Done = done });
}
