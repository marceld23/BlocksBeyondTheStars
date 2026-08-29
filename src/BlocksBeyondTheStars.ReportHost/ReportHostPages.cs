// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Net;
using System.Text;
using System.Text.Json;

namespace BlocksBeyondTheStars.ReportHost;

/// <summary>
/// Server-rendered admin pages for triaging reports: a filterable list and a detail view with the
/// screenshot, status buttons and delete. Self-contained (inline CSS, plain form POSTs, no script) like
/// the WorldHost portal pages. Every player-controlled string is HTML-encoded — report content is
/// hostile input rendered in the operator's browser.
/// </summary>
public static class ReportHostPages
{
    public static string List(IReadOnlyList<BugReportRecord> items, Dictionary<string, int> counts, string? status, string? category)
    {
        var sb = new StringBuilder();
        int total = counts.Values.Sum();
        sb.Append($"<h1>Bug reports <span class='sub'>{total} total");
        foreach (var pair in counts.OrderBy(p => p.Key))
        {
            sb.Append($" · {E(pair.Key)} {pair.Value}");
        }

        sb.Append("</span></h1>");

        // Operators keep confusing the two inboxes — say explicitly what lands here vs. in the fleet admin (#379).
        sb.Append("<p class='hint'>This inbox receives in-game feedback (the F1/F2 dialog), crash reports and " +
                  "/bump snapshots. Player reports and the portal website's feedback form live in the fleet " +
                  "admin — <code>play.&lt;your domain&gt;/admin</code>.</p>");

        sb.Append("<form method='get' action='/admin' class='filters'>");
        sb.Append("<select name='status'><option value=''>all statuses</option>");
        foreach (var s in BugReportStatus.All)
        {
            sb.Append($"<option value='{s}'{(s == status ? " selected" : "")}>{s}</option>");
        }

        sb.Append("</select> <select name='category'><option value=''>all categories</option>");
        foreach (var c in new[] { "feedback", "crash" })
        {
            sb.Append($"<option value='{c}'{(c == category ? " selected" : "")}>{c}</option>");
        }

        sb.Append("</select> <button>Filter</button> ");
        // Same filters, exported as one JSON download (all matching, not just the page's 200).
        string exportQuery = $"status={Uri.EscapeDataString(status ?? string.Empty)}&category={Uri.EscapeDataString(category ?? string.Empty)}";
        sb.Append($"<a href='/admin/export?{exportQuery}'><button type='button'>Download JSON</button></a></form>");

        if (items.Count == 0)
        {
            sb.Append("<p class='hint'>No reports match.</p>");
        }
        else
        {
            var groups = GroupDuplicates(items);
            int duplicateRows = items.Count - groups.Count;
            if (duplicateRows > 0)
            {
                sb.Append($"<p class='hint'>{groups.Count} report(s) in {items.Count} rows — every in-game F1 report " +
                          "arrives twice (once straight from the client, once as the server's /bump snapshot). " +
                          "The pairs are shown as one row below; the read API still returns both.</p>");
            }

            sb.Append("<table><tr><th>When (UTC)</th><th>Category</th><th>Title</th><th>Player</th><th>Version</th><th>📷</th><th>Status</th></tr>");
            foreach (var group in groups)
            {
                // Show the cleanest title (the client-direct row carries what the player actually typed; the
                // server forward prefixes "Bump [world]: [feedback] …"), but link to the richest row — the one
                // with the screenshot and the /bump snapshot attached.
                var display = group.OrderBy(r => r.Title.Length).First();
                var primary = group.OrderByDescending(r => r.ScreenshotFile.Length > 0)
                                   .ThenByDescending(r => r.ReportJson.Length)
                                   .First();

                string when = DateTimeOffset.FromUnixTimeSeconds(primary.CreatedUnix).ToString("yyyy-MM-dd HH:mm");
                string cat = primary.Kind.Length > 0 ? $"{primary.Category}/{primary.Kind}" : primary.Category;
                sb.Append($"<tr><td>{when}</td><td>{E(cat)}</td>");
                sb.Append($"<td><a href='/admin/report/{primary.Id}'>{E(Shorten(display.Title.Length > 0 ? display.Title : display.Description, 70))}</a>");

                // The other rows of the pair stay reachable — they are separate records with their own status.
                foreach (var other in group.Where(r => r.Id != primary.Id))
                {
                    sb.Append($" <a class='dup' href='/admin/report/{other.Id}' title='duplicate row: {E(other.Source.Length > 0 ? other.Source : "client")}'>+1</a>");
                }

                sb.Append("</td>");
                sb.Append($"<td>{E(primary.PlayerName)}</td><td>{E(primary.GameVersion)}</td>");
                sb.Append($"<td>{(group.Any(r => r.ScreenshotFile.Length > 0) ? "📷" : "")}</td>");

                // If the pair was triaged apart, show that rather than pretending one status covers both.
                string statuses = string.Join("/", group.Select(r => r.Status).Distinct());
                sb.Append($"<td class='st-{primary.Status}'>{E(statuses)}</td></tr>");
            }

            sb.Append("</table>");
        }

        return Shell("Bug reports — Blocks Beyond the Stars", sb.ToString());
    }

    /// <summary>How far apart the two rows of one F1 report may be stamped. The client posts directly and the
    /// server forwards its /bump snapshot independently, so they land a moment apart — observed 0–2 s.</summary>
    private const long DuplicateWindowSeconds = 8;

    /// <summary>
    /// Collapses the two database rows that one in-game F1 report produces into a single group.
    /// <para>
    /// Pressing F1 fires two independent uploads by design (see the ReportHost docs): the client posts to
    /// /api/bugreport itself, so feedback arrives even from someone else's dedicated server, AND the game server
    /// forwards the rich /bump snapshot to the same inbox. Both paths were built when only one of them reached
    /// the inbox; since singleplayer got a crash-upload sink they both do, and the list double-counted every
    /// report. Grouping happens at RENDER time only — ingest still stores both rows (it must never drop a player
    /// report) and the read API still returns both.
    /// </para>
    /// Rows pair up when they were stamped within <see cref="DuplicateWindowSeconds"/> of each other, belong to
    /// the same reporter and one description contains the other: the server forward wraps the player's text as
    /// <c>[feedback] &lt;title&gt; — &lt;description&gt;</c>, so the client row's text is a substring of it.
    /// "Same reporter" is NOT the player id (#1359): the client row carries the install token, the server
    /// forward the player name, so the two halves never agreed on it and nothing ever paired. It is the reply
    /// key when both halves carry one (a #1359 client passes the same key through <c>/bump</c>), and the player
    /// name otherwise (older client or server builds).
    /// </summary>
    public static List<List<BugReportRecord>> GroupDuplicates(IReadOnlyList<BugReportRecord> items)
    {
        var groups = new List<List<BugReportRecord>>();
        var taken = new bool[items.Count];

        for (int i = 0; i < items.Count; i++)
        {
            if (taken[i])
            {
                continue;
            }

            var group = new List<BugReportRecord> { items[i] };
            taken[i] = true;

            for (int j = i + 1; j < items.Count; j++)
            {
                if (!taken[j] && IsSameReport(items[i], items[j]))
                {
                    group.Add(items[j]);
                    taken[j] = true;
                }
            }

            groups.Add(group);
        }

        return groups;
    }

    private static bool IsSameReport(BugReportRecord a, BugReportRecord b)
    {
        if (Math.Abs(a.CreatedUnix - b.CreatedUnix) > DuplicateWindowSeconds)
        {
            return false;
        }

        // Only the two halves of ONE report pair up — never a client report with an unrelated crash, and never
        // two rows from different players/builds that happen to collide in time.
        if (a.Category != b.Category || a.GameVersion != b.GameVersion || !SameReporter(a, b))
        {
            return false;
        }

        string da = Normalize(a.Description);
        string db = Normalize(b.Description);
        if (da.Length == 0 || db.Length == 0)
        {
            return false; // nothing to compare — keep them apart rather than guess
        }

        return da.Contains(db, StringComparison.Ordinal) || db.Contains(da, StringComparison.Ordinal);
    }

    /// <summary>Whether two rows come from the same reporter — by reply key when both carry one (exact: two
    /// installs never share a key), by player name otherwise. Never by player id, which the two halves of one
    /// report do not share (install token vs. player name, #1359).</summary>
    private static bool SameReporter(BugReportRecord a, BugReportRecord b)
    {
        if (a.ReplyKey.Length > 0 && b.ReplyKey.Length > 0)
        {
            return a.ReplyKey == b.ReplyKey;
        }

        return a.PlayerName.Length > 0 && a.PlayerName == b.PlayerName;
    }

    /// <summary>Collapses whitespace so the two wordings compare cleanly.</summary>
    private static string Normalize(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Where a report's reply key came from — decides what the detail page promises about reaching
    /// the player (#1369).</summary>
    public enum ReplyKeyOrigin
    {
        /// <summary>No key: nothing written here can reach anyone.</summary>
        None,

        /// <summary>The client sent the key with the report — the key it polls with.</summary>
        SentByClient,

        /// <summary>Derived from the stored player id at ingest/back-fill (report filed before the reply
        /// channel). A desktop / play.* install polls with the same value; a glitch.fun arcade install does
        /// NOT (it hashes its Glitch install id, the player id there was the browser-local token).</summary>
        DerivedFromPlayerId,
    }

    /// <summary>Classifies <paramref name="r"/>'s reply key. "Sent by the client" means the stored payload
    /// carries a <c>replyKey</c> node — the only way a key other than the player-id derivation gets in.</summary>
    public static ReplyKeyOrigin KeyOrigin(BugReportRecord r)
    {
        if (r.ReplyKey.Length == 0)
        {
            return ReplyKeyOrigin.None;
        }

        try
        {
            using var doc = JsonDocument.Parse(r.ReportJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("replyKey", out var sent)
                && sent.ValueKind == JsonValueKind.String
                && sent.GetString() == r.ReplyKey)
            {
                return ReplyKeyOrigin.SentByClient;
            }
        }
        catch (JsonException)
        {
            // unreadable payload — fall through to the derivation check
        }

        return r.ReplyKey == BlocksBeyondTheStars.Shared.Feedback.FeedbackReplyKey.Derive(r.PlayerId)
            ? ReplyKeyOrigin.DerivedFromPlayerId
            : ReplyKeyOrigin.SentByClient; // passed through a /bump forward (#1359) — the client's own key
    }

    /// <summary>The Unity platform string of every browser build (play.* and the glitch.fun arcade alike).</summary>
    private const string WebGlPlatform = "WebGLPlayer";

    public static string Detail(BugReportRecord r, IReadOnlyList<ReplyRecord>? replies = null, AdminCsrf? csrf = null)
    {
        replies ??= Array.Empty<ReplyRecord>();
        string csrfField = csrf?.HiddenField() ?? string.Empty;
        var sb = new StringBuilder();
        string when = DateTimeOffset.FromUnixTimeSeconds(r.CreatedUnix).ToString("yyyy-MM-dd HH:mm:ss");
        sb.Append($"<p><a href='/admin'>&larr; back to list</a></p>");
        sb.Append($"<h1>{E(r.Title.Length > 0 ? r.Title : "(no title)")}</h1>");
        sb.Append($"<p class='sub'>{when} UTC · <b class='st-{r.Status}'>{E(r.Status)}</b> · {E(r.Category)}{(r.Kind.Length > 0 ? "/" + E(r.Kind) : "")}{(r.Source.Length > 0 ? " · source " + E(r.Source) : "")}</p>");

        sb.Append("<div class='card'><h2>Description</h2><pre>").Append(E(r.Description)).Append("</pre></div>");

        sb.Append("<div class='card'><h2>Meta</h2><table class='meta'>");
        AppendRow(sb, "Player", $"{r.PlayerName} ({r.PlayerId})");
        AppendRow(sb, "E-mail", r.Email);
        AppendRow(sb, "Version", $"{r.GameVersion} {r.BuildNumber}".Trim());
        AppendRow(sb, "Platform", r.Platform);
        AppendRow(sb, "Session", r.SessionId);
        AppendRow(sb, "Client time", r.ClientTimestamp);
        AppendRow(sb, "Report id", r.Id);
        sb.Append("</table></div>");

        if (r.ScreenshotFile.Length > 0)
        {
            sb.Append($"<div class='card'><h2>Screenshot</h2><a href='/admin/report/{r.Id}/screenshot'><img src='/admin/report/{r.Id}/screenshot' alt='screenshot'></a></div>");
        }

        sb.Append("<div class='card'><h2>Report JSON</h2><pre>").Append(E(Pretty(r.ReportJson))).Append("</pre></div>");

        // Reply thread (#1327): what the player sees in the game, plus the form to add to it. Only reports
        // that carry a reply key can reach a player (server crash reports have none).
        sb.Append("<div class='card'><h2>Conversation with the player</h2>");
        var origin = KeyOrigin(r);
        if (origin == ReplyKeyOrigin.None)
        {
            sb.Append("<p class='hint'>This report carries no reply key (no player id) — nothing you write here can reach a player.</p>");
        }
        else if (origin == ReplyKeyOrigin.DerivedFromPlayerId && r.Platform == WebGlPlatform)
        {
            // A browser report from before the reply channel: the key was derived from the browser-local
            // token, which the glitch.fun arcade never polls with (#1369) — only a play.* install would.
            sb.Append("<p class='hint'><b>No in-game reply possible (probably).</b> This browser report was filed before the reply channel " +
                      "existed, so its reply key was derived from the browser-local player id. A glitch.fun arcade install polls with a " +
                      "different key (its Glitch install id) and will never see an answer written here — reach the reporter through the " +
                      "old channel instead. Only a play.* install would match this key.</p>");
        }
        else if (origin == ReplyKeyOrigin.DerivedFromPlayerId)
        {
            sb.Append("<p class='hint'>Reply key derived from the player id (report filed before the reply channel). A desktop install " +
                      "matches it once the player runs a build with the in-game reply inbox; if the answer never turns <i>read</i>, use the old channel.</p>");
        }

        if (origin != ReplyKeyOrigin.None && replies.Count == 0)
        {
            sb.Append("<p class='hint'>No replies yet. An answer shows up in the player's game on their next start (or within ~10 minutes while playing); " +
                      "a <b>question</b> also lets them answer from inside the game.</p>");
        }

        foreach (var reply in replies)
        {
            string who = reply.Author == ReplyRecord.AuthorDev ? (reply.IsQuestion ? "You asked" : "You") : "Player";
            string stamp = DateTimeOffset.FromUnixTimeSeconds(reply.CreatedUnix).ToString("yyyy-MM-dd HH:mm");
            string seen = reply.Author == ReplyRecord.AuthorDev ? (reply.SeenUnix > 0 ? " · read" : " · unread") : string.Empty;
            sb.Append($"<div class='reply reply-{E(reply.Author)}'><div class='sub'>{who} · {stamp} UTC{seen}</div><pre>{E(reply.Text)}</pre></div>");
        }

        if (r.FixedInVersion.Length > 0)
        {
            sb.Append($"<p class='sub'>Fixed in version: <b>{E(r.FixedInVersion)}</b> (shown to the player with the thread)</p>");
        }

        sb.Append($"<form method='post' action='/admin/report/{r.Id}/reply' class='replyform'>{csrfField}");
        sb.Append("<textarea name='text' rows='4' maxlength='5000' placeholder='Answer, or a follow-up question. Never ask for personal data — the audience includes children.'></textarea>");
        sb.Append("<label><input type='checkbox' name='question' value='1'> this is a question (status → waiting_for_player; the player can answer in-game)</label>");
        sb.Append($"<label>fixed in version <input type='text' name='fixed_in_version' value='{E(r.FixedInVersion)}' placeholder='e.g. 2026.8.23' size='14'></label>");
        sb.Append("<button>send to player</button></form></div>");

        sb.Append("<div class='card actions'>");
        foreach (var s in BugReportStatus.All)
        {
            sb.Append($"<form method='post' action='/admin/report/{r.Id}/status'>{csrfField}<input type='hidden' name='status' value='{s}'><button{(s == r.Status ? " disabled" : "")}>mark {s}</button></form>");
        }

        sb.Append($"<form method='post' action='/admin/report/{r.Id}/delete' onsubmit=\"return confirm('Delete this report permanently?')\">{csrfField}<button class='danger'>delete</button></form>");
        sb.Append("</div>");

        return Shell($"Report {r.Id[..8]} — Blocks Beyond the Stars", sb.ToString());
    }

    private static void AppendRow(StringBuilder sb, string label, string value)
        => sb.Append($"<tr><th>{label}</th><td>{E(value)}</td></tr>");

    private static string Pretty(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private static string E(string s) => WebUtility.HtmlEncode(s);

    private static string Shell(string title, string body) => $@"<!doctype html>
<html lang='en'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'>
<meta name='robots' content='noindex'><title>{E(title)}</title>
<style>
 body {{ background:#0b1020; color:#dbe4ff; font:15px/1.5 system-ui, sans-serif; margin:0 auto; max-width:1080px; padding:1.2rem; }}
 a {{ color:#7fb4ff; }}
 h1 {{ font-size:1.4rem; }} h2 {{ font-size:1.05rem; margin:.2rem 0 .5rem; color:#9fb6e8; }}
 .sub {{ color:#8ea0c9; font-size:.85rem; font-weight:normal; }}
 .hint {{ color:#8ea0c9; }}
 .card {{ background:#141b33; border:1px solid #26314f; border-radius:8px; padding: .8rem 1rem; margin:.8rem 0; }}
 table {{ border-collapse:collapse; width:100%; }}
 th, td {{ text-align:left; padding:.3rem .55rem; border-bottom:1px solid #1f2947; vertical-align:top; }}
 th {{ color:#9fb6e8; font-weight:600; white-space:nowrap; }}
 .meta th {{ width:9rem; }}
 pre {{ white-space:pre-wrap; word-break:break-word; background:#0e1428; border-radius:6px; padding:.6rem; margin:0; max-height:32rem; overflow:auto; }}
 img {{ max-width:100%; border-radius:6px; }}
 .filters, .actions {{ display:flex; gap:.5rem; align-items:center; flex-wrap:wrap; }}
 select, button {{ background:#1d2745; color:#dbe4ff; border:1px solid #33406a; border-radius:6px; padding:.35rem .7rem; font:inherit; cursor:pointer; }}
 button:disabled {{ opacity:.45; cursor:default; }}
 button.danger {{ background:#4a1d24; border-color:#7c2f3c; }}
 .st-new {{ color:#ffd479; }} .st-triaged {{ color:#7fb4ff; }} .st-done {{ color:#77dd9a; }}
 .st-waiting_for_player {{ color:#c9a2ff; }} .st-player_replied {{ color:#ff9f6e; }}
 .reply {{ margin:.5rem 0; padding:.5rem .7rem; border-radius:6px; background:#0e1428; border-left:3px solid #33406a; }}
 .reply-dev {{ border-left-color:#7fb4ff; }} .reply-player {{ border-left-color:#ff9f6e; }}
 .reply pre {{ background:transparent; padding:0; }}
 .replyform {{ display:flex; flex-direction:column; gap:.5rem; margin-top:.6rem; }}
 .replyform textarea, .replyform input[type=text] {{ background:#0e1428; color:#dbe4ff; border:1px solid #33406a; border-radius:6px; padding:.4rem .6rem; font:inherit; }}
 .replyform button {{ align-self:flex-start; }}
 .dup {{ background:#26314f; border-radius:4px; padding:0 .3rem; font-size:.8rem; text-decoration:none; }}
</style></head><body>
{body}
</body></html>";
}
