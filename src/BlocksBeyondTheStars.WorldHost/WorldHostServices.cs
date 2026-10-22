// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// Everything the WorldHost routes share (#2902): the collaborators Program.cs builds, the abuse limiters and the
/// cached public-stats snapshot. One instance per process; <see cref="WorldHostApp.Create"/> hands it to every endpoint
/// group, and the tests build their own over a temp registry.
/// </summary>
public sealed class WorldHostServices : IDisposable
{
    public WorldHostServices(
        WorldHostConfig config,
        HostRegistry registry,
        IInstanceLauncher launcher,
        WorldHostMetrics metrics,
        WorldOrchestrator orchestrator,
        GlitchGateway glitch,
        BlocksBeyondTheStars.Shared.Notifications.AdminNotifier notifier)
    {
        Config = config;
        Registry = registry;
        Launcher = launcher;
        Metrics = metrics;
        Orchestrator = orchestrator;
        Glitch = glitch;
        Notifier = notifier;

        // Abuse limits (Phase 3). Signup/login key on the caller IP (real one via X-Forwarded-For — Caddy
        // fronts this service), uploads/reports on the account. See WorldHostConfig for the operator knobs.
        SignupLimit = new RateLimiter(config.SignupPerHourPerIp, TimeSpan.FromHours(1));
        LoginLimit = new RateLimiter(config.LoginPerMinutePerIp, TimeSpan.FromMinutes(1));
        LoginFailLimit = new RateLimiter(config.LoginFailsPer15MinPerAccount, TimeSpan.FromMinutes(15));
        UploadLimit = new RateLimiter(config.UploadsPerHourPerAccount, TimeSpan.FromHours(1));
        ReportLimit = new RateLimiter(config.ReportsPerHourPerAccount, TimeSpan.FromHours(1));
        StatsLimit = new RateLimiter(config.StatsPerMinutePerIp, TimeSpan.FromMinutes(1));
        GlitchSessionLimit = new RateLimiter(config.GlitchSessionsPerMinutePerIp, TimeSpan.FromMinutes(1));

        // Public aggregate snapshot (/api/stats): rebuilt at most once per TTL no matter the request rate —
        // the instance probes behind `online` are the expensive part this cache exists to protect.
        PublicStats = new CachedJson(TimeSpan.FromSeconds(Math.Max(1, config.StatsCacheSeconds)), async () =>
        {
            var counts = Registry.CountForMetrics();
            long created = counts.WorldsByStatus.Sum(s => s.Count);
            long active = counts.WorldsByStatus
                .Where(s => s.Status is WorldStatus.Running or WorldStatus.Starting)
                .Sum(s => s.Count);
            int online = await WorldHostApi.CountPlayersOnlineAsync(Registry, Orchestrator);
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                worlds = new { created, active },
                players = new { registered = counts.Accounts, online },
                updatedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            });
        });
    }

    public WorldHostConfig Config { get; }

    public HostRegistry Registry { get; }

    public IInstanceLauncher Launcher { get; }

    public WorldHostMetrics Metrics { get; }

    public WorldOrchestrator Orchestrator { get; }

    public GlitchGateway Glitch { get; }

    /// <summary>Operator push notifications (#938): fire-and-forget pings for new reports and name-screen hits.
    /// Off by default (empty BBS_WH_NOTIFY_URL) — everything it announces is also in the logs/admin UI.</summary>
    public BlocksBeyondTheStars.Shared.Notifications.AdminNotifier Notifier { get; }

    /// <summary>Signups per caller IP and hour.</summary>
    public RateLimiter SignupLimit { get; }

    /// <summary>Logins (and rescue-code redemptions) per caller IP and minute.</summary>
    public RateLimiter LoginLimit { get; }

    /// <summary>Failed password attempts per account name and 15 minutes (#418).</summary>
    public RateLimiter LoginFailLimit { get; }

    /// <summary>Save uploads per account and hour.</summary>
    public RateLimiter UploadLimit { get; }

    /// <summary>Player reports per account and hour.</summary>
    public RateLimiter ReportLimit { get; }

    /// <summary>/api/stats requests per caller IP and minute.</summary>
    public RateLimiter StatsLimit { get; }

    /// <summary>glitch.fun session grants per caller IP and minute.</summary>
    public RateLimiter GlitchSessionLimit { get; }

    /// <summary>The cached /api/stats JSON (see the constructor).</summary>
    public CachedJson PublicStats { get; }

    public void Dispose() => PublicStats.Dispose();
}
