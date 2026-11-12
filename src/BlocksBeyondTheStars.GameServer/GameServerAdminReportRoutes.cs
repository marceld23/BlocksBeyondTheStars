// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The routes that file a row in the world's report list (#3508; the list itself is <c>GameServerAdminReports.cs</c>):
/// <list type="bullet">
/// <item><b>A player report</b> names a player this world KNOWS — online or offline, any letter case. The answer is the same
/// either way, so a report is no probe for whether an invisible admin is around; a name nobody ever played under is refused
/// (the server route) or ignored (the portal mirror). The row's place is where the reported player stands — for an observer
/// the admin mode's entry point (never where they hover), for an offline player their save.</item>
/// <item><b>F1/F2 feedback</b> files the dialog's title field only (<see cref="BumpReport.FeedbackTitle"/>) — the description
/// is never parsed for it.</item>
/// <item><b>"Call an admin"</b> always answers the same neutral line, whether an admin is online or not; on an official hosted
/// world it also goes to the operators like a player report.</item>
/// </list>
/// Every route shares the report rate limit (<c>TakeReportSlot</c>), and every free text is control-stripped, capped and
/// screened before an admin reads it.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The longest help text and feedback title kept in a row.</summary>
    private const int MaxCallAdminLength = 200;

    private const int MaxFeedbackTitleLength = 80;

    /// <summary>Test seam: the JSON of the last "Call an admin" forwarded to the report inbox (an official hosted world).</summary>
    internal string? LastAdminCallJsonForTest { get; private set; }

    /// <summary>A player this world knows: the live session when they are online, else the saved record.</summary>
    internal sealed record KnownPlayer(string Id, string Name, PlayerSession? Session, PlayerState? Saved);

    /// <summary>The player of <paramref name="name"/> (any letter case): the joined session, or the saved record of a player
    /// who is offline; null for a name nobody played under on this world.</summary>
    private KnownPlayer? FindKnownPlayer(string name)
    {
        if (name.Length == 0)
        {
            return null;
        }

        if (FindJoinedSessionByName(name) is { } online)
        {
            return new KnownPlayer(online.State.PlayerId, online.State.Name ?? online.State.PlayerId, online, null);
        }

        foreach (string id in _repo.ListPlayerIds())
        {
            if (SamePlayer(id, name) && _repo.LoadPlayer(id) is { } saved)
            {
                return new KnownPlayer(saved.PlayerId, string.IsNullOrEmpty(saved.Name) ? saved.PlayerId : saved.Name, null, saved);
            }
        }

        return null;
    }

    /// <summary>A report target the staff-only rules cover: a fleet admin, or anyone observing right now.</summary>
    private bool IsStaffTarget(KnownPlayer target)
        => target.Session is { } s ? s.Spectating || s.IsFleetAdmin : IsFleetAdminName(target.Name);

    /// <summary>A player report's row: the reported player, their place (see the class summary) and their recent lines as
    /// evidence (none for an offline player — their lines left with their session).</summary>
    private void FilePlayerReportRow(PlayerSession reporter, KnownPlayer target, string note)
    {
        var row = NewWorldReport(AdminReportKinds.Player, reporter);
        row.TargetId = target.Id;
        row.TargetName = target.Name;
        row.Note = ScreenForAdmins(note);
        row.Evidence = target.Session is null ? string.Empty : string.Join("\n", RecentLines(target.Session));
        PlaceOfReportTarget(row, target);
        FileWorldReport(row, staffOnly: IsStaffTarget(target));
    }

    /// <summary>Where the row says the reported player is: on the body where they stand; an observer at the admin mode's entry
    /// point (no place at all for a <c>/spectate</c> without one); an offline player where their save has them.</summary>
    private static void PlaceOfReportTarget(StoredPlayerReport row, KnownPlayer target)
    {
        var s = target.Session;
        if (s is null)
        {
            row.Planet = target.Saved?.CurrentLocationId ?? string.Empty;
            row.HasPosition = row.Planet.Length > 0;
            SetRowPosition(row, target.Saved?.Position ?? default);
            return;
        }

        if (!s.Spectating)
        {
            row.Planet = s.CurrentLocationId;
            row.HasPosition = true;
            SetRowPosition(row, s.State.Position);
            return;
        }

        row.Planet = s.Admin.EntryLocationId;
        row.HasPosition = row.Planet.Length > 0;
        SetRowPosition(row, row.HasPosition ? s.Admin.EntryPosition : default);
    }

    private static void SetRowPosition(StoredPlayerReport row, Shared.Geometry.Vector3f at)
    {
        row.X = at.X;
        row.Y = at.Y;
        row.Z = at.Z;
    }

    /// <summary>F1/F2 feedback (the <c>[feedback]</c> bump the client sends beside its own upload): the title field and the
    /// reporter's place — never the description, the screenshot or the e-mail. An older client sends no title field: the row
    /// then says only that feedback was sent. Rate-limited like a report, quietly (the feedback itself went through).</summary>
    private void FileFeedbackReport(PlayerSession session, string description, string? feedbackTitle)
    {
        if (!description.StartsWith("[feedback] ", StringComparison.Ordinal))
        {
            return; // a developer's /bump, not the F1 dialog
        }

        if (!TakeReportSlot(session, tellWhenFull: false))
        {
            return;
        }

        string title = StripControlChars(feedbackTitle).Trim();
        if (title.Length > MaxFeedbackTitleLength)
        {
            title = title.Substring(0, MaxFeedbackTitleLength);
        }

        var row = NewWorldReport(AdminReportKinds.Feedback, session);
        row.Note = ScreenForAdmins(title);
        FileWorldReport(row);
    }

    /// <summary>"Call an admin": a help request without naming a player. An admin (who has the admin view) and an empty text
    /// file nothing; otherwise screened like a chat line (it reaches an admin the caller may not know), rate-limited like
    /// <c>/report</c>, forwarded to the operators on an official hosted world, and answered with one neutral line — whether
    /// an admin is online is nobody's business.</summary>
    private void HandleCallAdmin(PlayerSession session, string? text)
    {
        if (!TakeChatSlot(session))
        {
            return;
        }

        if (MayUseAdminView(session))
        {
            Send(session, new ServerMessage { Text = "@srv.admin.call_is_admin" });
            return;
        }

        string note = StripControlChars(text).Trim();
        if (note.Length > MaxCallAdminLength)
        {
            note = note.Substring(0, MaxCallAdminLength);
        }

        if (note.Length == 0)
        {
            Send(session, new ServerMessage { Text = "@srv.admin.call_usage" });
            return;
        }

        if (ScreenChatLine(session, note) is not { } screened || !TakeReportSlot(session, tellWhenFull: true))
        {
            return; // blocked (the caller was told) or too many reports
        }

        var row = NewWorldReport(AdminReportKinds.Help, session);
        row.Note = screened;
        FileWorldReport(row);
        if (OfficialHostedWorld)
        {
            ForwardAdminCall(session, screened);
        }

        Send(session, new ServerMessage { Text = "@srv.admin.call_sent" });
    }

    /// <summary>An official hosted world's owner is just some registered player, and may not be online for days: the call
    /// also goes to the operators — one ping and one inbox row, like a player report.</summary>
    private void ForwardAdminCall(PlayerSession caller, string text)
    {
        string who = caller.State.Name ?? "?";
        string description = $"Player '{who}' called an admin in world '{_meta.WorldName}': {text}";
        NotifyOperator($"admin call [{_meta.WorldName}]", description, "sos");
        LastAdminCallJsonForTest = PostReportToInbox($"admin call [{_meta.WorldName}]: '{who}'", description, caller, "admin-call",
            new { caller = who, callerInstallId = caller.InstallId, text });
    }

    /// <summary>A player report by name (<see cref="PlayerReportIntent"/>): the name and the note are control-stripped and
    /// capped, the name resolved to a known player. A portal mirror only files the row — the portal already has the report
    /// and told the player — and stays silent (also for a name nobody knows); anything else is the server route of
    /// <c>/report</c>.</summary>
    private void HandlePlayerReportIntent(PlayerSession session, PlayerReportIntent intent)
    {
        if (!TakeChatSlot(session))
        {
            return; // the same slot clock as every chat command (#3187)
        }

        string name = CleanReportName(StripControlChars(intent.TargetName));
        if (name.Length > MaxPlayerNameLength)
        {
            name = name.Substring(0, MaxPlayerNameLength).Trim();
        }

        string note = StripControlChars(intent.Note).Trim();
        if (note.Length > MaxReportNoteLength)
        {
            note = note.Substring(0, MaxReportNoteLength);
        }

        var target = FindKnownPlayer(name);
        if (!intent.AlsoFiledWithPortal)
        {
            if (name.Length == 0)
            {
                Send(session, new ServerMessage { Text = "@srv.report.usage" });
                return;
            }

            SubmitPlayerReport(session, target, note);
            return;
        }

        if (target is not null && !SamePlayer(target.Id, session.State.PlayerId) && TakeReportSlot(session, tellWhenFull: false))
        {
            FilePlayerReportRow(session, target, note);
        }
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: "Call an admin" as if the client sent <see cref="CallAdminIntent"/>.</summary>
    internal void CallAdminForTest(PlayerSession session, string text) => HandleCallAdmin(session, text);
}
