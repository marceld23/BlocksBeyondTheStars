// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.WorldHost;

// Hosted-worlds control plane ("WorldHost"): accounts, world registry, wake-on-demand allocation and
// join-token issuing for a fleet of one-container-per-world dedicated servers. See
// docs/developer/HOSTED_WORLDS.md for the architecture (routing, DNS, certificates, lifecycle).
//
// Deliberately NOT part of the per-instance admin Api: this service owns MANY worlds and the Docker
// socket; the Api serves ONE installation. Bound to loopback by default — the public portal domain is
// proxied onto it by Caddy. The routes live in WorldHostApp.Create and the *Endpoints route groups so the
// test suite can start the same app in-process (#2902); this file reads the environment, builds the
// collaborators, starts the background work and runs the app.

var config = WorldHostConfig.FromEnvironment();
var registry = new HostRegistry(config);
IInstanceLauncher launcher = new DockerCliLauncher(config);
var metrics = new WorldHostMetrics();
var orchestrator = new WorldOrchestrator(config, registry, launcher, metrics: metrics);
var glitch = new GlitchGateway(config, registry, orchestrator);

// Operator push notifications (#938): fire-and-forget pings for new reports and name-screen hits.
// Off by default (empty BBS_WH_NOTIFY_URL) — everything it announces is also in the logs/admin UI.
var notifier = new BlocksBeyondTheStars.Shared.Notifications.AdminNotifier(config.NotifyUrl, "worldhost");

using var services = new WorldHostServices(config, registry, launcher, metrics, orchestrator, glitch, notifier);
await using var app = WorldHostApp.Create(services, args);
var log = app.Logger;

// ---------------- Background reaper ----------------

// Reconcile registry vs Docker every 30 s: instances that exited themselves (idle shutdown — the normal
// sleep path) get marked stopped so the next join wakes them and world lists stay truthful.
// Keep-awake arcade worlds: wake the pool once at startup (fire-and-forget — worldgen on a fresh
// pool can take a minute per world) so the first glitch.fun visitor lands in a RUNNING world.
if (config.GlitchKeepAwake && glitch.Enabled)
{
    _ = Task.Run(async () =>
    {
        try
        {
            int woken = await glitch.WakePoolAsync();
            log.LogInformation("glitch.fun keep-awake: {Count} arcade world(s) woken at startup.", woken);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "glitch.fun keep-awake startup pass failed (the reaper retries).");
        }
    });
}

var reaper = Task.Run(async () =>
{
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
    int ticks = 0;
    while (await timer.WaitForNextTickAsync())
    {
        try
        {
            int reaped = orchestrator.Reap();
            if (reaped > 0)
            {
                log.LogInformation("Reaper: {Count} idle-stopped world(s) marked stopped.", reaped);
            }

            // Keep-awake arcade worlds: re-wake anything the reaper just found dead (crash/OOM/host
            // reboot) — with idle shutdown off these should never stop on their own.
            int rewoken = await glitch.WakePoolAsync();
            if (rewoken > 0)
            {
                log.LogInformation("glitch.fun keep-awake: {Count} arcade world(s) re-woken.", rewoken);
            }

            // #1706: a world that will not survive its wake is no longer restarted forever — say so, loudly
            // and once, because until it is mentioned somewhere nobody knows to go and look at it.
            foreach (var deadWorld in glitch.DrainGivenUpWorlds())
            {
                log.LogError(
                    "glitch.fun keep-awake: giving up on world {WorldId} — {Count} restarts in a row did not survive. " +
                    "It stays down until someone looks at it.",
                    deadWorld, GlitchGateway.MaxConsecutiveWakeFailures);
            }

            // #2464: a world whose save the server image refused as too new (exit code 3) is not restarted —
            // but the operator must hear that the fleet image lags behind a player's save. Once per refusal.
            foreach (var (tooNewWorld, image) in orchestrator.DrainSaveTooNewWorlds())
            {
                log.LogError(
                    "World {WorldId}: the server image {Image} refused its save as written by a newer version of the game " +
                    "(exit code 3, save untouched). It is not restarted on this image — update the fleet image, or have the owner " +
                    "upload an older save; the admin page's wake button retries.",
                    tooNewWorld, image);
            }

            // Archive sweep once an hour (120 × 30 s): long-inactive stopped worlds move to the archive.
            if (++ticks % 120 == 0)
            {
                int archived = orchestrator.ArchiveSweep(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                if (archived > 0)
                {
                    log.LogInformation("Archive sweep: {Count} world(s) archived after {Months} months of inactivity.",
                        archived, config.ArchiveAfterMonths);
                }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Reaper pass failed (will retry).");
        }
    }
});

log.LogInformation(
    "WorldHost up on {Bind}:{Port} — domain {Domain}, image {Image}, quotas: {Worlds} worlds/account, {Players} players, idle {Idle} min.",
    config.BindAddress, config.Port, config.BaseDomain, config.ServerImage,
    config.MaxWorldsPerAccount, config.MaxPlayersPerWorld, config.IdleShutdownMinutes);

if (config.GlitchEnabled && !config.GlitchConfigured)
{
    log.LogWarning("BBS_WH_GLITCH_ENABLED is set but the title id/token are missing — the glitch.fun gateway stays OFF.");
}
else if (glitch.Enabled)
{
    log.LogInformation("glitch.fun arcade gateway ON — {Worlds} pool world(s), {Players} players each.",
        config.GlitchWorldCount, config.GlitchMaxPlayers);
}

app.Run();
