using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;

// BlocksBeyondTheStars dedicated game server — standalone .NET host (no Unity runtime), so it runs
// on Windows, Linux x64 and Linux ARM64 / Raspberry Pi 5 (technical requirements §3.2, §9).

string installDir = AppContext.BaseDirectory;
string configPath = Path.Combine(installDir, "config", "server.json");

var config = ServerConfig.Load(configPath);

// Command-line overrides (e.g. the client's local singleplayer host passes --port/--saves/--data).
var appliedOverrides = config.ApplyCommandLine(args);

string dataDir = ResolveDataDir(installDir, config.DataDir);
string savesRoot = Path.IsPathRooted(config.SavesRoot) ? config.SavesRoot : Path.Combine(installDir, config.SavesRoot);

var logger = new ConsoleGameLogger(Path.Combine(savesRoot, config.WorldName, "logs", "server.log"));

if (appliedOverrides.Count > 0)
{
    logger.Info($"Applied command-line overrides: {string.Join(", ", appliedOverrides)}.");
}

GameContent content;
try
{
    content = ContentLoader.LoadFromDirectory(dataDir);
    logger.Info($"Loaded content: {content.Blocks.Count} blocks, {content.Items.Count} items, {content.Recipes.Count} recipes, {content.Planets.Count} planets.");
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
server.Start();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    logger.Info("Shutdown requested...");
    server.Stop();
    Environment.Exit(0);
};

logger.Info("Press Ctrl+C to stop the server.");
server.Run();
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
