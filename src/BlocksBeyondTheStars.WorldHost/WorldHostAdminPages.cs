// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text;

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>One live instance row on the admin page: the world plus its owner and, when running, the
/// live joined-player count from the instance's /status endpoint (null = unreachable/not running).</summary>
public sealed record AdminWorldRow(WorldRecord World, string OwnerName, int? JoinedPlayers);

/// <summary>
/// Operator admin UI (Basic Auth, /admin): the fleet instance overview with stop/wake, the open
/// player-report queue and account ban management — the browser front-end to what the X-Admin-Token
/// API exposes for scripts. Server-rendered like the portal pages; operator-facing, so English-only.
/// </summary>
public static class WorldHostAdminPages
{
    private static string E(string? value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    /// <summary>The reviewed/dismiss close-form pair every report and feedback row ends with.</summary>
    private static string CloseButtons(long reportId) =>
        $"<form method='post' action='/admin/reports/{reportId}/close' style='display:inline'><input type='hidden' name='status' value='reviewed'><button>reviewed</button></form>" +
        $"<form method='post' action='/admin/reports/{reportId}/close' style='display:inline'><input type='hidden' name='status' value='dismissed'><button>dismiss</button></form>";

    /// <summary>
    /// The per-row delete control, folded into a <c>&lt;details&gt;</c> so it never competes with stop/wake
    /// for a mis-click. Deleting needs the world's name typed into the box — checked server-side, because
    /// this is the one action on the page with no undo. Two submit buttons share the one input: the second
    /// carries <c>purge=true</c> and erases the saves as well.
    /// </summary>
    private static string DeleteForm(WorldRecord world)
    {
        // Arcade worlds have no owner and the gateway tops the pool back up, so deleting one is really a
        // RESET — say so instead of pretending the world is gone for good.
        bool glitch = world.Channel == WorldChannel.Glitch;
        return $"<details><summary class='del'>{(glitch ? "reset" : "delete")}…</summary>" +
               $"<form method='post' action='/admin/worlds/{world.Id}/delete'>" +
               $"<input name='confirm' size='14' placeholder='type: {E(world.DisplayName)}' aria-label='confirm world name'>" +
               "<button class='danger' title='stop the world, drop it from the registry, keep its saves on disk'>delete</button>" +
               "<button class='danger' name='purge' value='true' title='the same, but also erase the saves (live + archive) — unrecoverable'>purge saves</button>" +
               (glitch ? "<p class='hint'>The arcade pool refills itself — this world comes back empty.</p>" : string.Empty) +
               "</form></details>";
    }

    private static string Ago(long unix)
    {
        if (unix <= 0)
        {
            return "never";
        }

        var span = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unix);
        return span.TotalDays >= 1 ? $"{(int)span.TotalDays} d ago"
            : span.TotalHours >= 1 ? $"{(int)span.TotalHours} h ago"
            : $"{Math.Max(0, (int)span.TotalMinutes)} min ago";
    }

    public static string Index(
        WorldHostConfig config,
        IReadOnlyList<AdminWorldRow> worlds,
        IReadOnlyList<ReportRecord> openReports,
        IReadOnlyList<AccountRecord> banned,
        AccountRecord? lookedUp,
        string? lookupQuery,
        IReadOnlyList<GlitchGuestRecord>? glitchGuests = null,
        IReadOnlyList<GlitchBanRecord>? glitchBans = null,
        string? notice = null)
    {
        glitchGuests ??= Array.Empty<GlitchGuestRecord>();
        glitchBans ??= Array.Empty<GlitchBanRecord>();
        int active = worlds.Count(w => w.World.Status is WorldStatus.Running or WorldStatus.Starting);
        var playerReports = openReports.Where(r => r.Category != "feedback").ToList();
        var feedback = openReports.Where(r => r.Category == "feedback").ToList();
        var sb = new StringBuilder();

        sb.Append($"<h1>Fleet <span class='o'>admin</span> <span class='sub'>· {E(config.BaseDomain)}</span></h1>");
        sb.Append($"<p class='hint'>{worlds.Count} worlds · <b>{active}</b>/{(config.MaxActiveInstances > 0 ? config.MaxActiveInstances.ToString() : "∞")} instances awake · " +
                  $"{playerReports.Count} open report(s) · {feedback.Count} open feedback · {banned.Count} banned account(s)</p>");

        // Outcome of the last destructive action (redirect carries ?notice=…) — the page has no other
        // channel back to the operator.
        string? noticeText = notice switch
        {
            "confirm" => "Nothing deleted — the typed name did not match the world's name.",
            "deleted" => "World deleted. Its saves are still on disk.",
            "purged" => "World deleted and its saves erased.",
            _ => null,
        };
        if (noticeText is { })
        {
            sb.Append($"<p class='beta'>{E(noticeText)}</p>");
        }

        // ---- Server health (filled by JS from /admin/stats.json AFTER the page renders — the
        // docker-stats sample behind it takes ~1-2 s and must not stall the page) ----
        sb.Append("<div class='card'><h2>Server health</h2><div id='sh' class='hint'>loading…</div></div>");

        // ---- Instances ----
        sb.Append("<div class='card'><h2>Instances</h2>");
        if (worlds.Count == 0)
        {
            sb.Append("<p class='hint'>No worlds yet.</p>");
        }
        else
        {
            sb.Append("<table><tr><th>World</th><th>Owner</th><th>Status</th><th>Players</th><th>Port</th><th>Last started</th><th></th></tr>");
            foreach (var row in worlds)
            {
                var w = row.World;
                int maxPlayers = w.Channel == WorldChannel.Glitch ? config.GlitchMaxPlayers : config.MaxPlayersPerWorld;
                string badge = w.Channel == WorldChannel.Glitch ? " <span class='st'>glitch</span>" : string.Empty;
                string players = w.Status == WorldStatus.Running
                    ? (row.JoinedPlayers is { } n ? $"{n}/{maxPlayers}" : "?")
                    : "—";
                string action = w.Status is WorldStatus.Running or WorldStatus.Starting
                    ? $"<form method='post' action='/admin/worlds/{w.Id}/restart' style='display:inline'><button title='warn players, then stop after a 10-minute countdown'>restart in 10 min</button></form> " +
                      $"<form method='post' action='/admin/worlds/{w.Id}/stop' style='display:inline'><button class='danger' title='stop immediately, players get no warning'>stop</button></form>"
                    : $"<form method='post' action='/admin/worlds/{w.Id}/wake'><button>wake</button></form>";
                sb.Append($"<tr><td><b>{E(w.DisplayName)}</b>{badge}<br><code>{w.Id}</code></td><td>{E(row.OwnerName)}</td>" +
                          $"<td><span class='st {E(w.Status)}'>{E(w.Status)}</span></td><td>{players}</td>" +
                          $"<td>{w.HostPort}</td><td>{Ago(w.LastStartedUnix)}</td><td>{action}{DeleteForm(w)}</td></tr>");
            }

            sb.Append("</table>");
            sb.Append("<p class='hint'>Deleting stops the instance and drops the world from the registry. " +
                      "<b>delete</b> leaves its saves in the worlds directory (recoverable by hand), " +
                      "<b>purge saves</b> erases them including the archive copy. Both need the world's " +
                      "name typed into the box first.</p>");
        }

        sb.Append("</div>");

        // ---- Maintenance announcements (#249): banner/countdown pushed into running instances ----
        sb.Append("<div class='card'><h2>Announce</h2>");
        if (string.IsNullOrEmpty(config.AnnounceToken))
        {
            sb.Append("<p class='hint'>Announcements are off — set BBS_WH_ANNOUNCE_TOKEN (worlds pick it up on their next wake).</p>");
        }
        else
        {
            sb.Append("<form method='post' action='/admin/announce'>" +
                      "<input name='message' placeholder='message shown to players (optional for restarts)' maxlength='200' size='48'> " +
                      "<input name='minutes' placeholder='restart in min (empty = info only)' size='22'> " +
                      "<select name='worldId'><option value=''>whole fleet</option>");
            foreach (var row in worlds.Where(r => r.World.Status is WorldStatus.Running or WorldStatus.Starting))
            {
                sb.Append($"<option value='{row.World.Id}'>{E(row.World.DisplayName)}</option>");
            }

            sb.Append("</select> " +
                      "<button>announce</button> " +
                      "<button name='action' value='cancel' title='clears a scheduled restart countdown'>cancel restart</button>" +
                      "</form>" +
                      "<p class='hint'>With minutes: players see a countdown banner and the instance stops itself gracefully at zero. " +
                      "Without: a one-off message every player must acknowledge. Only awake instances are reachable.</p>");
        }

        sb.Append("</div>");

        // ---- Open player reports (game feedback lives in its own card below) ----
        sb.Append("<div class='card'><h2>Open player reports</h2>");
        if (playerReports.Count == 0)
        {
            sb.Append("<p class='hint'>Nothing to review. 🎉</p>");
        }
        else
        {
            sb.Append("<table><tr><th>#</th><th>Filed</th><th>World</th><th>Reported name</th><th>Category</th><th>Message</th><th></th></tr>");
            foreach (var r in playerReports)
            {
                sb.Append($"<tr><td>{r.Id}</td><td>{Ago(r.CreatedUnix)}</td><td><code>{E(r.WorldId)}</code></td>" +
                          $"<td><a href='/admin?acct={Uri.EscapeDataString(r.ReportedName)}'>{E(r.ReportedName)}</a></td>" +
                          $"<td>{E(r.Category)}</td><td>{E(r.Message)}</td><td>" +
                          CloseButtons(r.Id) +
                          "</td></tr>");
            }

            sb.Append("</table>");
        }

        sb.Append("<p class='hint'>The reported name links to the account lookup below (names match only when the player used their account name in-game).</p></div>");

        // ---- Game feedback & ideas (same report table, category 'feedback' — no reported player) ----
        sb.Append("<div class='card'><h2>Feedback &amp; ideas</h2>");
        if (feedback.Count == 0)
        {
            sb.Append("<p class='hint'>No open feedback.</p>");
        }
        else
        {
            sb.Append("<table><tr><th>#</th><th>Filed</th><th>Message</th><th></th></tr>");
            foreach (var r in feedback)
            {
                sb.Append($"<tr><td>{r.Id}</td><td>{Ago(r.CreatedUnix)}</td><td>{E(r.Message)}</td><td>" +
                          CloseButtons(r.Id) +
                          "</td></tr>");
            }

            sb.Append("</table>");
        }

        sb.Append("<p class='hint'>This card lists the portal website's “Feedback &amp; ideas” form only. " +
                  "In-game feedback (the F1/F2 dialog), crash reports and /bump snapshots go to the separate " +
                  "ReportHost inbox — <code>reports.&lt;your domain&gt;/admin</code>.</p></div>");

        // ---- Ban management ----
        sb.Append("<div class='card'><h2>Accounts &amp; bans</h2>");
        sb.Append($"<form method='get' action='/admin'><input name='acct' placeholder='account name' value='{E(lookupQuery)}'><button>look up</button></form>");
        if (!string.IsNullOrEmpty(lookupQuery))
        {
            if (lookedUp is null)
            {
                sb.Append($"<p class='hint'>No account named “{E(lookupQuery)}”.</p>");
            }
            else
            {
                string state = lookedUp.IsBanned ? $"BANNED ({E(lookedUp.BanReason)})" : "active";
                sb.Append($"<p><b>{E(lookedUp.Name)}</b> — {state}{(lookedUp.IsDeveloper ? " · developer" : string.Empty)}</p>");
                sb.Append($"<form method='post' action='/admin/ban'><input type='hidden' name='accountId' value='{E(lookedUp.Id)}'>" +
                          $"<input type='hidden' name='banned' value='{(lookedUp.IsBanned ? "false" : "true")}'>" +
                          (lookedUp.IsBanned
                              ? "<button>unban</button>"
                              : "<input name='reason' placeholder='reason (shown to the player)'><button class='danger'>ban account</button>") +
                          "</form>");
            }
        }

        if (banned.Count > 0)
        {
            sb.Append("<h2>Currently banned</h2><table><tr><th>Name</th><th>Reason</th><th></th></tr>");
            foreach (var a in banned)
            {
                sb.Append($"<tr><td>{E(a.Name)}</td><td>{E(a.BanReason)}</td><td>" +
                          $"<form method='post' action='/admin/ban'><input type='hidden' name='accountId' value='{E(a.Id)}'>" +
                          "<input type='hidden' name='banned' value='false'><button>unban</button></form></td></tr>");
            }

            sb.Append("</table>");
        }

        sb.Append("</div>");

        // ---- glitch.fun arcade guests & install bans (only when the channel is in use) ----
        if (glitchGuests.Count > 0 || glitchBans.Count > 0)
        {
            sb.Append("<div class='card'><h2>glitch.fun arcade</h2>");
            if (glitchGuests.Count > 0)
            {
                sb.Append("<table><tr><th>Player</th><th>Install id</th><th>Last seen</th><th>Sessions</th><th></th></tr>");
                foreach (var g in glitchGuests)
                {
                    bool isBanned = glitchBans.Any(b => b.InstallId == g.InstallId);
                    string action = isBanned
                        ? "<span class='st'>banned</span>"
                        : $"<form method='post' action='/admin/glitch/ban' style='display:inline'>" +
                          $"<input type='hidden' name='installId' value='{E(g.InstallId)}'>" +
                          $"<input type='hidden' name='playerName' value='{E(g.PlayerName)}'>" +
                          "<input type='hidden' name='banned' value='true'>" +
                          "<input name='reason' placeholder='reason' size='14'>" +
                          "<button class='danger'>ban install</button></form>";
                    sb.Append($"<tr><td><b>{E(g.PlayerName)}</b></td><td><code>{E(g.InstallId)}</code></td>" +
                              $"<td>{Ago(g.LastSeenUnix)}</td><td>{g.Sessions}</td><td>{action}</td></tr>");
                }

                sb.Append("</table>");
            }

            if (glitchBans.Count > 0)
            {
                sb.Append("<h2>Banned installs</h2><table><tr><th>Player</th><th>Install id</th><th>Reason</th><th></th></tr>");
                foreach (var b in glitchBans)
                {
                    sb.Append($"<tr><td>{E(b.PlayerName)}</td><td><code>{E(b.InstallId)}</code></td><td>{E(b.Reason)}</td><td>" +
                              $"<form method='post' action='/admin/glitch/ban'><input type='hidden' name='installId' value='{E(b.InstallId)}'>" +
                              "<input type='hidden' name='banned' value='false'><button>unban</button></form></td></tr>");
                }

                sb.Append("</table>");
            }

            sb.Append("<p class='hint'>Bans key on Glitch's install id (arcade guests have no account). A banned install gets no new " +
                      "session and its next heartbeat answers 403 — the client stops the game on that.</p></div>");
        }

        sb.Append("<p><a href='/'>← Portal</a></p>");
        sb.Append("<style>table{width:100%;border-collapse:collapse} th,td{padding:6px 8px;text-align:left;border-bottom:1px solid var(--line);vertical-align:top} form{margin:0}" +
                  "summary.del{color:#e05c5c} td details{margin:4px 0} td details input{display:inline-block;width:auto;margin:4px 4px 0 0}</style>");

        // Server-health card renderer. Thresholds mirror the ops alerting levels (<70 % green,
        // <85 % amber, else red). Values interpolated into innerHTML are numbers plus docker container
        // names, whose charset docker itself restricts — nothing player-controlled reaches this card.
        sb.Append(@"<script>
(function () {
  var el = document.getElementById('sh');
  function bar(label, frac, text) {
    var pct = Math.max(0, Math.min(100, Math.round(frac * 100)));
    var color = pct < 70 ? '#7dff9e' : pct < 85 ? '#ff8c26' : '#e05c5c';
    return ""<div style='margin:6px 0'>"" + label + "" <span class='sub'>"" + text + ""</span>"" +
      ""<div style='height:8px;border:1px solid var(--line);border-radius:4px;overflow:hidden'>"" +
      ""<div style='height:100%;width:"" + pct + ""%;background:"" + color + ""'></div></div></div>"";
  }
  fetch('/admin/stats.json').then(function (r) { return r.json(); }).then(function (s) {
    var h = s.host || {}, html = '';
    if (h.load1 != null) { html += bar('CPU load', h.cores ? h.load1 / h.cores : 0, h.load1.toFixed(2) + ' (1 min) / ' + h.cores + ' cores'); }
    if (h.memTotalKb) {
      var usedKb = h.memTotalKb - (h.memAvailableKb || 0);
      html += bar('RAM', usedKb / h.memTotalKb, (usedKb / 1048576).toFixed(1) + ' / ' + (h.memTotalKb / 1048576).toFixed(1) + ' GB');
    }
    if (h.diskTotalBytes) {
      var usedB = h.diskTotalBytes - (h.diskFreeBytes || 0);
      html += bar('Disk (worlds)', usedB / h.diskTotalBytes, (usedB / 1073741824).toFixed(1) + ' / ' + (h.diskTotalBytes / 1073741824).toFixed(1) + ' GB');
    }
    if (!html) { html = ""<p class='hint'>No host metrics on this platform.</p>""; }
    if (s.containers && s.containers.length) {
      html += ""<table><tr><th>Container</th><th>CPU</th><th>Memory</th></tr>"";
      s.containers.forEach(function (c) {
        html += ""<tr><td><code>"" + c.name + ""</code></td><td>"" + c.cpuPercent.toFixed(1) + "" %</td><td>"" +
          (c.memUsedBytes / 1048576).toFixed(0) + "" / "" + (c.memLimitBytes / 1048576).toFixed(0) + "" MB</td></tr>"";
      });
      html += ""</table>"";
    }
    html += ""<p class='hint'>"" + s.fleet.playersOnline + "" player(s) online · "" + s.fleet.accounts +
      "" account(s) · "" + s.fleet.reportsOpen + "" open report(s)</p>"";
    el.innerHTML = html;
  }).catch(function () { el.textContent = 'stats unavailable'; });
})();
</script>");

        return WorldHostPortalPages.Shell("Fleet admin — Blocks Beyond the Stars", sb.ToString(), "de", config);
    }
}
