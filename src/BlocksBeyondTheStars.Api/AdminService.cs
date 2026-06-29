// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Missions;

namespace BlocksBeyondTheStars.Api;

/// <summary>Outcome of an admin content operation: success plus any validation problems.</summary>
public sealed class ContentOpResult
{
    public bool Success { get; set; }
    public List<string> Problems { get; set; } = new();
    public string? Message { get; set; }
}

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
    public string PersistenceBackend { get; set; } = string.Empty;
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

    public ServerConfig LoadConfig()
    {
        // Same precedence as the game server: server.json overlaid by BBS_* environment variables, so a
        // containerised admin UI honours BBS_ADMIN_BIND / BBS_ADMIN_PORT / BBS_ADMIN_PASSWORD. Saving config
        // (PUT /api/config) deserialises the request body instead, so env values are never written to disk.
        var config = ServerConfig.Load(_configPath);
        config.ApplyEnvironment();
        return config;
    }

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
            PersistenceBackend = WorldRepositoryFactory.DisplayName(config),
            WorldExists = WorldRepositoryFactory.IsPostgreSql(config) || File.Exists(paths.DatabaseFile),
        };

        if (status.WorldExists)
        {
            if (!WorldRepositoryFactory.IsPostgreSql(config))
            {
                var info = new FileInfo(paths.DatabaseFile);
                status.WorldSizeBytes = info.Length;
                status.LastModifiedUtc = info.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss");
            }

            try
            {
                using var repo = WorldRepositoryFactory.Create(config, paths);
                repo.Initialize();
                status.RegisteredPlayers = repo.ListPlayerIds().Count;
            }
            catch (Exception ex)
            {
                status.Warning = "Could not open the configured world database: " + ex.Message;
            }
        }

        if (Directory.Exists(paths.BackupsDirectory))
        {
            status.BackupCount = Directory.GetFiles(paths.BackupsDirectory, WorldRepositoryFactory.BackupSearchPattern(config)).Length;
        }

        if (!status.AdminPasswordSet)
        {
            string adminWarning = "No admin password is set. Keep the admin port bound to localhost/LAN only.";
            status.Warning = string.IsNullOrEmpty(status.Warning) ? adminWarning : status.Warning + " " + adminWarning;
        }

        return status;
    }

    public IReadOnlyList<BackupInfo> ListBackups()
    {
        var config = LoadConfig();
        var paths = PathsFor(config);
        if (!Directory.Exists(paths.BackupsDirectory))
        {
            return Array.Empty<BackupInfo>();
        }

        return Directory.GetFiles(paths.BackupsDirectory, WorldRepositoryFactory.BackupSearchPattern(config))
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
        var config = LoadConfig();
        var paths = PathsFor(config);
        if (!WorldRepositoryFactory.IsPostgreSql(config) && !File.Exists(paths.DatabaseFile))
        {
            throw new InvalidOperationException("No world database exists to back up.");
        }

        using var repo = WorldRepositoryFactory.Create(config, paths);
        repo.Initialize();
        var label = "backup_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        return Path.GetFileName(repo.CreateBackup(label));
    }

    // --- Admin extension editor: missions & content packs (anf_admin_blueprinf.md §5–13) ---

    private GameContent? _content;

    private GameContent Content()
    {
        if (_content is not null)
        {
            return _content;
        }

        var configured = LoadConfig().DataDir;
        var dataDir = Path.IsPathRooted(configured) ? configured : Path.Combine(_installDir, configured);
        if (!Directory.Exists(dataDir))
        {
            // Developer layout: walk up to find the repo's data directory.
            var dir = new DirectoryInfo(_installDir);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "data");
                if (File.Exists(Path.Combine(candidate, "blocks.json")))
                {
                    dataDir = candidate;
                    break;
                }

                dir = dir.Parent;
            }
        }

        _content = ContentLoader.LoadFromDirectory(dataDir);
        return _content;
    }

    private IWorldRepository OpenRepo()
    {
        var config = LoadConfig();
        var repo = WorldRepositoryFactory.Create(config, PathsFor(config));
        repo.Initialize();
        return repo;
    }

    public IReadOnlyList<MissionDefinition> ListAdminMissions()
    {
        using var repo = OpenRepo();
        return repo.ListMissions();
    }

    /// <summary>Validates and stores an admin mission (server-wide). Takes effect on next server start.</summary>
    public ContentOpResult SaveAdminMission(MissionDefinition mission)
    {
        if (string.IsNullOrWhiteSpace(mission.Id))
        {
            mission.Id = "am_" + Guid.NewGuid().ToString("N");
        }

        mission.Source = MissionSource.Admin;

        var problems = MissionValidator.Validate(mission, Content());
        if (problems.Count > 0)
        {
            return new ContentOpResult { Success = false, Problems = problems };
        }

        using var repo = OpenRepo();
        repo.SaveMission(mission);
        return new ContentOpResult { Success = true, Message = mission.Id };
    }

    public void DeleteAdminMission(string id)
    {
        using var repo = OpenRepo();
        repo.DeleteMission(id);
    }

    public string ExportContentPack()
    {
        using var repo = OpenRepo();
        var pack = new ContentPack { Name = LoadConfig().WorldName + "-content", Missions = repo.ListMissions().ToList() };
        return JsonSerializer.Serialize(pack, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Imports a content pack: every mission is validated, then valid ones are stored.</summary>
    public ContentOpResult ImportContentPack(string json)
    {
        ContentPack? pack;
        try
        {
            pack = JsonSerializer.Deserialize<ContentPack>(json);
        }
        catch (Exception ex)
        {
            return new ContentOpResult { Success = false, Problems = { "Invalid JSON: " + ex.Message } };
        }

        if (pack is null)
        {
            return new ContentOpResult { Success = false, Problems = { "Empty content pack." } };
        }

        var content = Content();
        var problems = new List<string>();
        int imported = 0;
        using var repo = OpenRepo();
        foreach (var mission in pack.Missions)
        {
            var issues = MissionValidator.Validate(mission, content);
            if (issues.Count > 0)
            {
                problems.AddRange(issues.Select(p => $"[{mission.Id}] {p}"));
                continue;
            }

            repo.SaveMission(mission);
            imported++;
        }

        return new ContentOpResult
        {
            Success = problems.Count == 0,
            Problems = problems,
            Message = $"Imported {imported}/{pack.Missions.Count} missions.",
        };
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
