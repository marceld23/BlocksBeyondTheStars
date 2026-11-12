// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The world the admin-view tests (#3502) run in: a real server on a SQLite save, a <see cref="RecordingTransport"/>, no
/// starter ship (players stand on foot), and helpers to make an admin, a player, a fleet admin or an official hosted world
/// (a join-token instance). Not a test class — the admin-view test classes share it.
/// </summary>
internal sealed class AdminTestWorld : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_adminview_" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteWorldRepository> _repos = new();
    private readonly GameContent _content;
    private readonly Action<ServerConfig>? _configure;

    public AdminTestWorld(GameContent content, bool hosted = false, Action<ServerConfig>? configure = null, params string[] fleetAdmins)
    {
        _content = content;
        Hosted = hosted;
        FleetAdmins = fleetAdmins;
        _configure = configure;
        Repo = new SqliteWorldRepository(new SaveGamePaths(_root, "admin"));
        _repos.Add(Repo);
        Server = Boot(Repo, Transport);
    }

    /// <summary>A unique world name — the bump files a test may write carry it (and Dispose removes them).</summary>
    public string WorldName { get; } = "adminview_" + Guid.NewGuid().ToString("N").Substring(0, 8);

    public bool Hosted { get; }

    public string[] FleetAdmins { get; }

    public RecordingTransport Transport { get; } = new();

    public SqliteWorldRepository Repo { get; }

    public SvGameServer Server { get; private set; }

    private SvGameServer Boot(IWorldRepository repo, RecordingTransport transport)
    {
        var config = new ServerConfig
        {
            WorldName = WorldName,
            Seed = 1,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            FleetAdminPlayers = FleetAdmins.ToList(),
            JoinTokenSecret = Hosted ? "test-secret-of-a-hosted-world" : string.Empty,
        };
        _configure?.Invoke(config);
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>A second server on the same save — what a restart sees.</summary>
    public SvGameServer Restart(RecordingTransport transport)
    {
        Server = Boot(Repo, transport);
        return Server;
    }

    /// <summary>A player on foot at the world's first spawn, with <paramref name="role"/>.</summary>
    public PlayerSession Join(string name, PlayerRole role = PlayerRole.Player)
    {
        var s = Server.AddLocalPlayer(name);
        s.State.Role = role;
        s.State.AboardShip = false;
        s.State.Health = 100f;
        return s;
    }

    /// <summary>Everything sent to <paramref name="who"/> since the transport was last cleared.</summary>
    public List<T> To<T>(PlayerSession who) => Transport.To<T>(who);

    /// <summary>The <see cref="ServerMessage"/> and <see cref="ActionRejected"/> texts sent to <paramref name="who"/>.</summary>
    public List<string> Said(PlayerSession who)
        => Transport.To(who).Select(m => m switch
        {
            ServerMessage s => s.Text,
            ActionRejected r => r.Reason,
            RespawnNotice n => n.Reason,
            _ => null,
        }).Where(t => t is not null).Select(t => t!).ToList();

    /// <summary>A place a little away from <paramref name="who"/> (blocks).</summary>
    public static Vector3f Offset(PlayerSession who, float dx, float dy = 0f, float dz = 0f)
        => new(who.State.Position.X + dx, who.State.Position.Y + dy, who.State.Position.Z + dz);

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        // A feedback bump writes its snapshot to <repo>/bugreports/server inside a checkout — only this world's files go.
        string bumps = Path.Combine(TestPaths.RepoRoot(), "bugreports", "server");
        if (Directory.Exists(bumps))
        {
            foreach (string file in Directory.GetFiles(bumps, "bump_" + WorldName + "_*"))
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // best-effort cleanup
                }
            }
        }

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A test host still holding the file is not a test failure.
        }
    }
}
