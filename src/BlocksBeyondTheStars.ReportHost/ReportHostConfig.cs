// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.ReportHost;

/// <summary>
/// Operator configuration for the bug-report inbox ("ReportHost"). Everything is set through
/// <c>BBS_REPORTS_*</c> environment variables. All three credentials default to EMPTY, which fails
/// closed: no write key = ingest rejects everything, no read key = the read API is off, no admin
/// password = the admin UI is off. A self-hoster only enables what they need.
/// </summary>
public sealed class ReportHostConfig
{
    /// <summary>Bind address; loopback by default so the service is never accidentally public — in the
    /// intended deployment a reverse proxy (Caddy) terminates TLS in front of it.</summary>
    public string BindAddress { get; set; } = "127.0.0.1";

    /// <summary>31418 = one above the WorldHost control plane (31417), same private-port family.</summary>
    public int Port { get; set; } = 31418;

    /// <summary>Directory holding the report database (reports.db) and the screenshots/ folder.</summary>
    public string DataDir { get; set; } = "reporthost";

    /// <summary>Spam-gate key clients present in <c>x-bugreport-key</c> (the same header the game already
    /// sends to the Wix endpoint — it is a gate, not a secret; it ships inside the client). Empty (the
    /// default) rejects ALL ingest, so a misconfigured deployment can't silently collect anonymous spam.</summary>
    public string WriteKey { get; set; } = string.Empty;

    /// <summary>Separate, independently rotatable key for the read API (<c>x-report-read-key</c>) used by
    /// pull scripts / CI. Empty (the default) disables the read API entirely.</summary>
    public string ReadKey { get; set; } = string.Empty;

    /// <summary>Basic-Auth credentials for the admin UI (and the status/delete API). Both empty (the
    /// default) disables the admin surface entirely.</summary>
    public string AdminUser { get; set; } = string.Empty;

    public string AdminPassword { get; set; } = string.Empty;

    /// <summary>Kestrel request-body cap. 4 MB comfortably fits the client's report JSON with its ~1.5 MB
    /// (2M base64 chars) screenshot and bounds abuse.</summary>
    public long MaxBodyBytes { get; set; } = 4_000_000;

    /// <summary>Screenshot base64 cap, mirroring the client-side <c>FeedbackUploader</c> cap — an oversized
    /// image is dropped (the report is kept) rather than rejected.</summary>
    public int MaxScreenshotBase64Length { get; set; } = 2_000_000;

    /// <summary>Description cap, matching the Wix endpoint contract the game was built against.</summary>
    public int MaxDescriptionLength { get; set; } = 5000;

    public int MaxTitleLength { get; set; } = 200;

    /// <summary>Ingest rate limit per client IP per minute (fixed window). Generous for real players —
    /// the in-game dialog is hand-typed — while blunting scripted floods.</summary>
    public int IngestPerMinute { get; set; } = 10;

    /// <summary>Days to keep reports; 0 (default) keeps them forever. Pruning also removes the screenshot
    /// file (reports carry an optional e-mail, so retention is a privacy lever, not just disk hygiene).</summary>
    public int RetentionDays { get; set; }

    /// <summary>When true, the FIRST address in <c>X-Forwarded-For</c> is used as the client IP for rate
    /// limiting. Only enable behind a trusted reverse proxy that overwrites the header; otherwise clients
    /// could spoof their way past the limiter.</summary>
    public bool TrustProxy { get; set; }

    /// <summary>Operator push-notification URL (<c>BBS_REPORTS_NOTIFY_URL</c>, issue #938) — an ntfy
    /// topic URL or any webhook accepting a plain-text POST; pinged once per stored report. Empty
    /// (default) = off, matching the fail-closed credential defaults above.</summary>
    public string NotifyUrl { get; set; } = string.Empty;

    /// <summary>Loads config from BBS_REPORTS_* environment variables over the defaults.</summary>
    public static ReportHostConfig FromEnvironment()
    {
        var c = new ReportHostConfig();

        static string? Env(string name)
        {
            var v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(v) ? null : v;
        }

        if (Env("BBS_REPORTS_BIND") is { } bind) { c.BindAddress = bind; }
        if (Env("BBS_REPORTS_PORT") is { } portStr && int.TryParse(portStr, out var port)) { c.Port = port; }
        if (Env("BBS_REPORTS_DATA_DIR") is { } dataDir) { c.DataDir = dataDir; }
        if (Env("BBS_REPORTS_WRITE_KEY") is { } writeKey) { c.WriteKey = writeKey; }
        if (Env("BBS_REPORTS_READ_KEY") is { } readKey) { c.ReadKey = readKey; }
        if (Env("BBS_REPORTS_ADMIN_USER") is { } adminUser) { c.AdminUser = adminUser; }
        if (Env("BBS_REPORTS_ADMIN_PASSWORD") is { } adminPass) { c.AdminPassword = adminPass; }
        if (Env("BBS_REPORTS_MAX_BODY_BYTES") is { } mbStr && long.TryParse(mbStr, out var mb)) { c.MaxBodyBytes = mb; }
        if (Env("BBS_REPORTS_INGEST_PER_MINUTE") is { } rlStr && int.TryParse(rlStr, out var rl)) { c.IngestPerMinute = rl; }
        if (Env("BBS_REPORTS_RETENTION_DAYS") is { } rdStr && int.TryParse(rdStr, out var rd)) { c.RetentionDays = rd; }
        if (Env("BBS_REPORTS_TRUST_PROXY") is { } tpStr && bool.TryParse(tpStr, out var tp)) { c.TrustProxy = tp; }
        if (Env("BBS_REPORTS_NOTIFY_URL") is { } notifyUrl) { c.NotifyUrl = notifyUrl; }

        return c;
    }
}
