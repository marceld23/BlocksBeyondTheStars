using Spacecraft.GameServer;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;

// Spacecraft dedicated game server — standalone .NET host (no Unity runtime), so it runs
// on Windows, Linux x64 and Linux ARM64 / Raspberry Pi 5 (technical requirements §3.2, §9).

string installDir = AppContext.BaseDirectory;
string configPath = Path.Combine(installDir, "config", "server.json");

var config = ServerConfig.Load(configPath);

string dataDir = ResolveDataDir(installDir, config.DataDir);
string savesRoot = Path.IsPathRooted(config.SavesRoot) ? config.SavesRoot : Path.Combine(installDir, config.SavesRoot);

var logger = new ConsoleGameLogger(Path.Combine(savesRoot, config.WorldName, "logs", "server.log"));

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
using var transport = new LiteNetLibServerTransport(config.MaxPlayers);

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
