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
        foreach (var s in new[] { BugReportStatus.New, BugReportStatus.Triaged, BugReportStatus.Done })
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
            sb.Append("<table><tr><th>When (UTC)</th><th>Category</th><th>Title</th><th>Player</th><th>Version</th><th>📷</th><th>Status</th></tr>");
            foreach (var r in items)
            {
                string when = DateTimeOffset.FromUnixTimeSeconds(r.CreatedUnix).ToString("yyyy-MM-dd HH:mm");
                string cat = r.Kind.Length > 0 ? $"{r.Category}/{r.Kind}" : r.Category;
                sb.Append($"<tr><td>{when}</td><td>{E(cat)}</td>");
                sb.Append($"<td><a href='/admin/report/{r.Id}'>{E(Shorten(r.Title.Length > 0 ? r.Title : r.Description, 70))}</a></td>");
                sb.Append($"<td>{E(r.PlayerName)}</td><td>{E(r.GameVersion)}</td>");
                sb.Append($"<td>{(r.ScreenshotFile.Length > 0 ? "📷" : "")}</td><td class='st-{r.Status}'>{E(r.Status)}</td></tr>");
            }

            sb.Append("</table>");
        }

        return Shell("Bug reports — Blocks Beyond the Stars", sb.ToString());
    }

    public static string Detail(BugReportRecord r)
    {
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

        sb.Append("<div class='card actions'>");
        foreach (var s in new[] { BugReportStatus.New, BugReportStatus.Triaged, BugReportStatus.Done })
        {
            sb.Append($"<form method='post' action='/admin/report/{r.Id}/status'><input type='hidden' name='status' value='{s}'><button{(s == r.Status ? " disabled" : "")}>mark {s}</button></form>");
        }

        sb.Append($"<form method='post' action='/admin/report/{r.Id}/delete' onsubmit=\"return confirm('Delete this report permanently?')\"><button class='danger'>delete</button></form>");
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
</style></head><body>
{body}
</body></html>";
}
