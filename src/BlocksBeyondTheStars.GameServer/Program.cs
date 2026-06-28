// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;

// BlocksBeyondTheStars dedicated game server — standalone .NET host (no Unity runtime), so it runs
// on Windows, Linux x64 and Linux ARM64 (technical requirements §3.2, §9).

string installDir = AppContext.BaseDirectory;
string configPath = Path.Combine(installDir, "config", "server.json");

var config = ServerConfig.Load(configPath);

// Configuration precedence: server.json < BBS_* environment variables < command line. The env layer
// is the container channel (Docker/Compose), the CLI is what the in-game singleplayer host passes.
var appliedEnv = config.ApplyEnvironment();

// Command-line overrides (e.g. the client's local singleplayer host passes --port/--saves/--data).
var appliedOverrides = config.ApplyCommandLine(args);

string dataDir = ResolveDataDir(installDir, config.DataDir);
string savesRoot = Path.IsPathRooted(config.SavesRoot) ? config.SavesRoot : Path.Combine(installDir, config.SavesRoot);
string? userContentDir = string.IsNullOrEmpty(config.UserContentDir)
    ? null
    : (Path.IsPathRooted(config.UserContentDir) ? config.UserContentDir : Path.Combine(installDir, config.UserContentDir));

var logger = new ConsoleGameLogger(Path.Combine(savesRoot, config.WorldName, "logs", "server.log"));

if (appliedEnv.Count > 0)
{
    logger.Info($"Applied environment overrides: {string.Join(", ", appliedEnv)}.");
}

if (appliedOverrides.Count > 0)
{
    logger.Info($"Applied command-line overrides: {string.Join(", ", appliedOverrides)}.");
}

GameContent content;
try
{
    content = ContentLoader.LoadFromDirectory(dataDir, userContentDir);
    logger.Info($"Loaded content: {content.Blocks.Count} blocks, {content.Items.Count} items, {content.Recipes.Count} recipes, {content.Planets.Count} planets.");
    if (userContentDir != null && Directory.Exists(userContentDir))
    {
        logger.Info($"User content '{userContentDir}': {content.StationTemplates.Count} station + {content.SettlementTemplates.Count} settlement template(s) in pool.");
    }
}
catch (Exception ex)
{
    logger.Error($"Failed to load content from '{dataDir}': {ex.Message}");
    return 1;
}

var paths = new SaveGamePaths(savesRoot, config.WorldName);
using var repo = new SqliteWorldRepository(paths);

// Native UDP for the Windows client; optionally also WebSocket for browser clients
// (same protocol, same authoritative server). Both share the gameplay port number.
var native = new LiteNetLibServerTransport(config.MaxPlayers);
using IServerTransport transport = config.EnableWebSocket
    ? new CompositeServerTransport(native, new WebSocketServerTransport(config.WebSocketBindAddress))
    : native;

if (config.EnableWebSocket)
{
    logger.Info($"WebSocket gateway enabled on {config.WebSocketBindAddress}:{config.GameplayPort} (browser clients).");
}

var server = new GameServer(config, content, transport, repo, logger);

// Last-resort crash capture. The per-tick Guards (GameServerResilience) already contain simulation faults;
// these handlers catch what those can't see — exceptions on background threads / tasks, or anything that
// still escapes the run loop. Each writes a durable, endpoint-independent crash report to disk (best effort,
// never throws). We deliberately do NOT attempt an emergency SaveAll here: from an arbitrary thread mid-fault
// the world state may be half-mutated, so persisting it could write corruption — the last autosave + the
// report are the safer record.
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    try
    {
        var ex = e.ExceptionObject as Exception;
        string? path = ex != null ? server.CrashWriter.Write("unhandled-exception", null, ex) : null;
        logger.Error($"FATAL unhandled exception (terminating={e.IsTerminating}): {e.ExceptionObject}"
            + (path != null ? $" — crash report written: {path}" : string.Empty));
    }
    catch
    {
        // never let the crash handler throw on the way down
    }
};

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    try
    {
        string? path = server.CrashWriter.Write("unobserved-task", null, e.Exception);
        logger.Error($"Unobserved task exception (contained): {e.Exception}"
            + (path != null ? $" — crash report written: {path}" : string.Empty));
        e.SetObserved(); // we've logged + recorded it; don't let it escalate
    }
    catch
    {
        // best-effort
    }
};

server.Start();

// Automatic crash upload — opt-in. When config supplies an endpoint + key, the server sends queued reports
// to the website: once now (catching crashes the previous run couldn't send, e.g. a fatal exit) and then
// periodically in-session (driven by the tick, on a background thread so it never blocks simulation). When
// unconfigured (the default) reports stay on disk for a manual send. The local files remain the source of
// truth either way; a sent file is moved to crashreports/sent/, not deleted.
var crashUploader = new CrashReportUploader(config.CrashReportEndpoint, config.CrashReportApiKey);
int pendingCrashes = server.CrashWriter.CountPending();
if (crashUploader.IsConfigured)
{
    server.CrashUploader = crashUploader;
    if (pendingCrashes > 0)
    {
        var writer = server.CrashWriter;
        logger.Info($"Uploading {pendingCrashes} queued crash report(s) to {config.CrashReportEndpoint}...");
        _ = Task.Run(() =>
        {
            try
            {
                int sent = writer.FlushPending(crashUploader);
                logger.Info($"Crash report upload: {sent}/{pendingCrashes} sent; the rest stay queued for a later retry.");
            }
            catch
            {
                // best-effort startup catch-up
            }
        });
    }
}
else if (pendingCrashes > 0)
{
    logger.Warn($"{pendingCrashes} unsent crash report(s) in {server.CrashWriter.DirectoryPath}. " +
                "Attach them to a bug report, or set CrashReportEndpoint + CrashReportApiKey to upload them automatically.");
}

Console.CancelKeyPress += (_, e) =>
{
    // Only REQUEST the stop here (this runs on the SIGINT handler thread). The run loop notices the flag,
    // drains + saves on the tick thread, then returns from Run() — so the save never races a live Tick().
    e.Cancel = true;
    logger.Info("Shutdown requested...");
    server.RequestStop();
};

logger.Info("Press Ctrl+C to stop the server.");
server.Run(); // returns once the shutdown request has been drained + saved on the tick thread
return 0;

// Resolves the content directory: configured path, else next to the executable, else by
// walking up the tree (developer runs from the repository).
static string ResolveDataDir(string installDir, string configured)
{
    if (Path.IsPathRooted(configured) && Directory.Exists(configured))
    {
        return configured;
    }

    var local = Path.Combine(installDir, configured);
    if (Directory.Exists(local))
    {
        return local;
    }

    var dir = new DirectoryInfo(installDir);
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, "data");
        if (File.Exists(Path.Combine(candidate, "blocks.json")))
        {
            return candidate;
        }

        dir = dir.Parent;
    }

    return local; // will fail loudly in the loader with a clear message
}
