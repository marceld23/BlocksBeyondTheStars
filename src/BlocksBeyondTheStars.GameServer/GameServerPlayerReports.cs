// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Text;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Reporting a player from inside the world, for people who have no account (#1222).
///
/// <c>/report</c> used to be purely client-side and gated on a portal session, which meant the players who
/// most need it could not use it: an arcade guest on glitch.fun joins with an install id and nothing else,
/// so the command answered "not available here" and left them with no recourse at all on a public world
/// full of strangers. The command now also exists on the SERVER — intercepted in chat like <c>/bump</c> and
/// <c>/reportpaint</c>, before the radio gate, because reporting must never depend on owning equipment.
///
/// The report carries its own evidence: the reported player's own recent chat lines, kept in RAM per
/// session (<see cref="PlayerSession.RecentChatLines"/>) and copied out only into a report a human actually
/// filed. Nothing about it is broadcast — the reported player is never told, and no other player sees it.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How many of the reported player's lines are quoted. Matches the client-side portal path
    /// (<c>ReportChatCommand.MaxQuotedLines</c>) so both routes produce the same kind of row.</summary>
    private const int ReportQuotedLines = 10;

    /// <summary>Reports one session may file inside <see cref="ReportWindowSeconds"/>. Reporting is a safety
    /// valve, not a weapon: a handful is plenty for a real incident, and the cap stops someone from turning
    /// the operator's inbox into the thing that needs moderating.</summary>
    private const int MaxReportsPerWindow = 3;
    private const double ReportWindowSeconds = 600.0;

    private const int MaxReportNoteLength = 200;

    /// <summary>Test seam: the JSON of the last report handed to the inbox sink. The real send is
    /// fire-and-forget on a background thread (like every other outbound report), which a test cannot wait
    /// on without a race — so the payload is recorded synchronously as it is built.</summary>
    public string? LastPlayerReportJsonForTest { get; private set; }

    /// <summary>Remembers one relayed line as report evidence, oldest dropped first. Called with the text
    /// the OTHER players saw, i.e. after screening — masked words stay masked in the excerpt too, because
    /// the operator is reviewing behaviour, not collecting the unfiltered version.
    ///
    /// A line the filter BLOCKED never gets here at all, and that is deliberate: it was never said to
    /// anyone, and copying it into a report would re-transmit exactly the content the filter refused. That
    /// player is not invisible either — a blocked line is logged server-side (#1207) and repeated hits earn
    /// an automatic mute that does ping the operator (#1208).</summary>
    private static void NoteChatLineForEvidence(PlayerSession session, string text)
    {
        session.RecentChatLines.Add(text);
        if (session.RecentChatLines.Count > PlayerSession.MaxRecentChatLines)
        {
            session.RecentChatLines.RemoveAt(0);
        }
    }

    /// <summary>Handles <c>/report [name] [note]</c> typed in chat.</summary>
    private void HandlePlayerReport(PlayerSession session, string arguments)
    {
        if (arguments.Length == 0)
        {
            Send(session, new ServerMessage { Text = "@srv.report.usage" });
            return;
        }

        var (target, note) = ResolveReportTarget(arguments);
        if (target is null)
        {
            Send(session, new ServerMessage { Text = "@srv.report.no_target" });
            return;
        }

        if (ReferenceEquals(target, session))
        {
            // Not an error worth an operator's time, and answering "you cannot report yourself" is friendlier
            // than filing it and having a human work out what happened.
            Send(session, new ServerMessage { Text = "@srv.report.self" });
            return;
        }

        TrimWindow(session.RecentReportsAt, ReportWindowSeconds);
        if (session.RecentReportsAt.Count >= MaxReportsPerWindow)
        {
            Send(session, new ServerMessage { Text = "@srv.report.too_many" });
            return;
        }

        session.RecentReportsAt.Add(_uptime);
        ForwardPlayerReport(session, target, note);
        Send(session, new ServerMessage { Text = "@srv.report.sent:" + (target.State.Name ?? "?") });
    }

    /// <summary>Splits the argument into a reported player and a note. The whole argument is tried as a name
    /// first: player names may contain spaces (#980), so "/report mincraft Fan" is far more likely to be one
    /// name than a name plus the note "Fan". Only when that finds nobody is the first token taken as the
    /// name and the rest as the note.</summary>
    private (PlayerSession? Target, string Note) ResolveReportTarget(string arguments)
    {
        string whole = CleanReportName(arguments);
        if (FindJoinedSessionByName(whole) is { } exact)
        {
            return (exact, string.Empty);
        }

        int space = arguments.IndexOfAny(new[] { ' ', '\t' });
        if (space < 0)
        {
            return (null, string.Empty);
        }

        string name = CleanReportName(arguments.Substring(0, space));
        string note = arguments.Substring(space + 1).Trim();
        if (note.Length > MaxReportNoteLength)
        {
            note = note.Substring(0, MaxReportNoteLength);
        }

        return (FindJoinedSessionByName(name), note);
    }

    /// <summary>Trims a typed name the way the admin commands do (#980): no quotes, no leading "@".</summary>
    private static string CleanReportName(string raw)
        => raw.Trim().Trim('"').Trim().TrimStart('@').Trim();

    /// <summary>Files the report: one operator ping and one row in the report inbox, carrying the world, who
    /// reported whom (with the arcade install id, which for a guest is the only identity there is), the
    /// note, and the reported player's own recent lines as evidence.</summary>
    private void ForwardPlayerReport(PlayerSession reporter, PlayerSession target, string note)
    {
        string reporterName = reporter.State.Name ?? "?";
        string targetName = target.State.Name ?? "?";
        string excerpt = ChatExcerpt(target);
        string description = $"Player '{reporterName}' reported '{targetName}' in world '{_meta.WorldName}'." +
                             (note.Length > 0 ? $" Note: {note}" : string.Empty) +
                             (excerpt.Length > 0 ? $" | chat: {excerpt}" : " | no recent chat from the reported player.");

        _log.Info($"Player report: '{reporterName}' reported '{targetName}'.");
        NotifyOperator($"player report [{_meta.WorldName}]", description, "triangular_flag_on_post");

        var report = new
        {
            reporter = reporterName,
            reporterInstallId = reporter.InstallId,
            reported = targetName,
            reportedInstallId = target.InstallId,
            note,
            chat = RecentLines(target),
        };

        LastPlayerReportJsonForTest = PostReportToInbox(
            $"player report [{_meta.WorldName}]: '{targetName}'",
            description,
            reporter,
            "player-report",
            report);
    }

    /// <summary>The reported player's last lines as one quoted string for the human-readable description.</summary>
    private static string ChatExcerpt(PlayerSession target)
    {
        var lines = RecentLines(target);
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(" / ");
            }

            sb.Append('"').Append(lines[i]).Append('"');
        }

        return sb.ToString();
    }

    private static List<string> RecentLines(PlayerSession target)
    {
        var all = target.RecentChatLines;
        int from = Math.Max(0, all.Count - ReportQuotedLines);
        var kept = new List<string>(all.Count - from);
        for (int i = from; i < all.Count; i++)
        {
            kept.Add(all[i]);
        }

        return kept;
    }
}
