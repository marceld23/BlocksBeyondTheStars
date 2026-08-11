// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text;
using System.Threading;

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// Process-lifetime counters plus the Prometheus text exposition for <c>GET /metrics</c>. Hand-rolled
/// (no client library): the format is trivial and the metric set is small — enough for a Prometheus
/// scrape + capacity alerting on the single-host fleet. The endpoint stays on the loopback bind and is
/// deliberately NOT routed through Caddy.
/// </summary>
public sealed class WorldHostMetrics
{
    private long _joinsGranted;
    private long _wakes;
    private long _reaped;
    private long _archived;
    private long _rateLimited;
    private long _namesBlocked;
    private long _namesFlagged;

    public void JoinGranted() => Interlocked.Increment(ref _joinsGranted);

    public void Woke() => Interlocked.Increment(ref _wakes);

    public void Reaped(int count) => Interlocked.Add(ref _reaped, count);

    public void Archived(int count) => Interlocked.Add(ref _archived, count);

    public void RateLimited() => Interlocked.Increment(ref _rateLimited);

    /// <summary>A name was rejected by the block list (#938) — previously these attempts left no trace at all.</summary>
    public void NameBlocked() => Interlocked.Increment(ref _namesBlocked);

    /// <summary>A name matched the watch list and was allowed through with an operator flag (#938).</summary>
    public void NameFlagged() => Interlocked.Increment(ref _namesFlagged);

    /// <summary>Renders the scrape body: live gauges from the registry + the process counters.</summary>
    public string Render(HostRegistry registry)
    {
        var counts = registry.CountForMetrics();
        var sb = new StringBuilder(512);

        sb.Append("# TYPE bbs_accounts_total gauge\n");
        sb.Append("bbs_accounts_total ").Append(counts.Accounts).Append('\n');
        sb.Append("# TYPE bbs_reports_open gauge\n");
        sb.Append("bbs_reports_open ").Append(counts.OpenReports).Append('\n');
        sb.Append("# TYPE bbs_worlds gauge\n");
        foreach (var (status, count) in counts.WorldsByStatus)
        {
            sb.Append("bbs_worlds{status=\"").Append(status).Append("\"} ").Append(count).Append('\n');
        }

        sb.Append("# TYPE bbs_joins_granted_total counter\n");
        sb.Append("bbs_joins_granted_total ").Append(Interlocked.Read(ref _joinsGranted)).Append('\n');
        sb.Append("# TYPE bbs_world_wakes_total counter\n");
        sb.Append("bbs_world_wakes_total ").Append(Interlocked.Read(ref _wakes)).Append('\n');
        sb.Append("# TYPE bbs_worlds_reaped_total counter\n");
        sb.Append("bbs_worlds_reaped_total ").Append(Interlocked.Read(ref _reaped)).Append('\n');
        sb.Append("# TYPE bbs_worlds_archived_total counter\n");
        sb.Append("bbs_worlds_archived_total ").Append(Interlocked.Read(ref _archived)).Append('\n');
        sb.Append("# TYPE bbs_rate_limited_total counter\n");
        sb.Append("bbs_rate_limited_total ").Append(Interlocked.Read(ref _rateLimited)).Append('\n');
        sb.Append("# TYPE bbs_names_blocked_total counter\n");
        sb.Append("bbs_names_blocked_total ").Append(Interlocked.Read(ref _namesBlocked)).Append('\n');
        sb.Append("# TYPE bbs_names_flagged_total counter\n");
        sb.Append("bbs_names_flagged_total ").Append(Interlocked.Read(ref _namesFlagged)).Append('\n');

        return sb.ToString();
    }
}
