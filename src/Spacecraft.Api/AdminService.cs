using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;

namespace Spacecraft.Api;

/// <summary>Snapshot of server/world status shown on the admin dashboard.</summary>
public sealed class AdminStatus
{
    public string ServerName { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public int GameplayPort { get; set; }
    public int AdminPort { get; set; }
    public int MaxPlayers { get; set; }
    public bool WorldExists { get; set; }
    public long WorldSizeBytes { get; set; }
    public string? LastModifiedUtc { get; set; }
    public int RegisteredPlayers { get; set; }
    public int BackupCount { get; set; }
    public bool AdminPasswordSet { get; set; }
    public string? Warning { get; set; }
}

public sealed class BackupInfo
{
    public string Name { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string ModifiedUtc { get; set; } = string.Empty;
}

/// <summary>
/// Filesystem/DB-backed admin operations over a server installation directory: status,
/// configuration, backups and logs (technical requirements §13). Live operations that
/// require a running game server (kick/ban, start/stop) are intentionally out of MVP
/// scope and would be added via an IPC channel to the game-server process.
/// </summary>
public sealed class AdminService
{
    private readonly string _installDir;
    private readonly string _configPath;

    public AdminService(string installDir)
    {
        _installDir = installDir;
        _configPath = Path.Combine(installDir, "config", "server.json");
    }

    public ServerConfig LoadConfig() => ServerConfig.Load(_configPath);

    public void SaveConfig(ServerConfig config) => config.Save(_configPath);

    private SaveGamePaths PathsFor(ServerConfig config)
    {
        var savesRoot = Path.IsPathRooted(config.SavesRoot)
            ? config.SavesRoot
            : Path.Combine(_installDir, config.SavesRoot);
        return new SaveGamePaths(savesRoot, config.WorldName);
    }

    public AdminStatus GetStatus()
    {
        var config = LoadConfig();
        var paths = PathsFor(config);

        var status = new AdminStatus
        {
            ServerName = config.ServerName,
            WorldName = config.WorldName,
            GameplayPort = config.GameplayPort,
            AdminPort = config.AdminPort,
            MaxPlayers = config.MaxPlayers,
            AdminPasswordSet = !string.IsNullOrEmpty(config.AdminPassword),
            WorldExists = File.Exists(paths.DatabaseFile),
        };

        if (status.WorldExists)
        {
            var info = new FileInfo(paths.DatabaseFile);
            status.WorldSizeBytes = info.Length;
            status.LastModifiedUtc = info.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss");

            using var repo = new SqliteWorldRepository(paths);
            repo.Initialize();
            status.RegisteredPlayers = repo.ListPlayerIds().Count;
        }

        if (Directory.Exists(paths.BackupsDirectory))
        {
            status.BackupCount = Directory.GetFiles(paths.BackupsDirectory, "*.db").Length;
        }

        if (!status.AdminPasswordSet)
        {
            status.Warning = "No admin password is set. Keep the admin port bound to localhost/LAN only.";
        }

        return status;
    }

    public IReadOnlyList<BackupInfo> ListBackups()
    {
        var paths = PathsFor(LoadConfig());
        if (!Directory.Exists(paths.BackupsDirectory))
        {
            return Array.Empty<BackupInfo>();
        }

        return Directory.GetFiles(paths.BackupsDirectory, "*.db")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new BackupInfo
            {
                Name = f.Name,
                SizeBytes = f.Length,
                ModifiedUtc = f.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
            })
            .ToList();
    }

    public string CreateBackup()
    {
        var paths = PathsFor(LoadConfig());
        if (!File.Exists(paths.DatabaseFile))
        {
            throw new InvalidOperationException("No world database exists to back up.");
        }

        using var repo = new SqliteWorldRepository(paths);
        repo.Initialize();
        var label = "backup_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        return Path.GetFileName(repo.CreateBackup(label));
    }

    public IReadOnlyList<string> TailLog(int lines)
    {
        var paths = PathsFor(LoadConfig());
        var logFile = Path.Combine(paths.LogsDirectory, "server.log");
        if (!File.Exists(logFile))
        {
            return Array.Empty<string>();
        }

        // Read the whole file then take the tail; server logs are small for self-hosting.
        var all = File.ReadAllLines(logFile);
        return all.Length <= lines ? all : all[^lines..];
    }
}
