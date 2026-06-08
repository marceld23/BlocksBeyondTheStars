using System.Text.Json;
using Microsoft.Data.Sqlite;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Missions;
using Spacecraft.Shared.State;
using Spacecraft.Shared.World;

namespace Spacecraft.Persistence;

/// <summary>
/// SQLite-backed savegame repository. Stores world metadata, per-block player edits and
/// player/ship snapshots. Uses WAL mode for durable, low-overhead writes suitable for
/// small self-hosted servers (including a Raspberry Pi 5).
/// </summary>
public sealed class SqliteWorldRepository : IWorldRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly SaveGamePaths _paths;
    private readonly object _gate = new();
    private SqliteConnection? _connection;

    public string WorldDirectory => _paths.WorldDirectory;

    public SqliteWorldRepository(SaveGamePaths paths)
    {
        _paths = paths;
    }

    private SqliteConnection Connection =>
        _connection ?? throw new InvalidOperationException("Repository is not initialized. Call Initialize() first.");

    public void Initialize()
    {
        _paths.EnsureDirectories();

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _paths.DatabaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
        }.ToString();

        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        Execute("PRAGMA journal_mode=WAL;");
        Execute("PRAGMA synchronous=NORMAL;");
        Execute("PRAGMA foreign_keys=ON;");

        Execute(@"
            CREATE TABLE IF NOT EXISTS world_meta (id INTEGER PRIMARY KEY CHECK (id = 0), json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS block_edit (
                planet TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL,
                block INTEGER NOT NULL, PRIMARY KEY (planet, x, y, z));
            CREATE TABLE IF NOT EXISTS player (id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS ship (id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS container (
                id TEXT PRIMARY KEY, planet TEXT NOT NULL, kind TEXT NOT NULL,
                x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS door (
                planet TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL,
                kind TEXT NOT NULL, axisx INTEGER NOT NULL, PRIMARY KEY (planet, x, y, z));
            CREATE TABLE IF NOT EXISTS beacon (
                planet TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL,
                label TEXT NOT NULL, owner TEXT NOT NULL, PRIMARY KEY (planet, x, y, z));
            CREATE TABLE IF NOT EXISTS location_status (id TEXT PRIMARY KEY, status TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS mission (id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS landing_zone (
                player_id TEXT NOT NULL, location_id TEXT NOT NULL,
                cx INTEGER NOT NULL, cz INTEGER NOT NULL, radius INTEGER NOT NULL, protected INTEGER NOT NULL,
                PRIMARY KEY (player_id, location_id));");
    }

    // --- Metadata ---

    public WorldMetadata? LoadMetadata()
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT json FROM world_meta WHERE id = 0;";
            var json = cmd.ExecuteScalar() as string;
            return json is null ? null : JsonSerializer.Deserialize<WorldMetadata>(json, JsonOptions);
        }
    }

    public void SaveMetadata(WorldMetadata metadata)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO world_meta (id, json) VALUES (0, $json) " +
                              "ON CONFLICT(id) DO UPDATE SET json = excluded.json;";
            cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(metadata, JsonOptions));
            cmd.ExecuteNonQuery();
        }
    }

    // --- Block edits ---

    public void SetBlock(string planet, Vector3i worldPosition, ushort block)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO block_edit (planet, x, y, z, block) VALUES ($p, $x, $y, $z, $b) " +
                              "ON CONFLICT(planet, x, y, z) DO UPDATE SET block = excluded.block;";
            cmd.Parameters.AddWithValue("$p", planet);
            cmd.Parameters.AddWithValue("$x", worldPosition.X);
            cmd.Parameters.AddWithValue("$y", worldPosition.Y);
            cmd.Parameters.AddWithValue("$z", worldPosition.Z);
            cmd.Parameters.AddWithValue("$b", block);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<BlockEdit> LoadChunkEdits(string planet, ChunkCoord chunk)
    {
        var origin = WorldConstants.ChunkOrigin(chunk);
        int maxX = origin.X + WorldConstants.ChunkSize - 1;
        int maxY = origin.Y + WorldConstants.ChunkSize - 1;
        int maxZ = origin.Z + WorldConstants.ChunkSize - 1;

        var result = new List<BlockEdit>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT x, y, z, block FROM block_edit WHERE planet = $p " +
                              "AND x BETWEEN $minx AND $maxx AND y BETWEEN $miny AND $maxy AND z BETWEEN $minz AND $maxz;";
            cmd.Parameters.AddWithValue("$p", planet);
            cmd.Parameters.AddWithValue("$minx", origin.X);
            cmd.Parameters.AddWithValue("$maxx", maxX);
            cmd.Parameters.AddWithValue("$miny", origin.Y);
            cmd.Parameters.AddWithValue("$maxy", maxY);
            cmd.Parameters.AddWithValue("$minz", origin.Z);
            cmd.Parameters.AddWithValue("$maxz", maxZ);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var pos = new Vector3i(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
                result.Add(new BlockEdit(pos, (ushort)reader.GetInt32(3)));
            }
        }

        return result;
    }

    // --- Players ---

    public PlayerState? LoadPlayer(string playerId)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT json FROM player WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", playerId);
            var json = cmd.ExecuteScalar() as string;
            if (json is null)
            {
                return null;
            }

            var snapshot = JsonSerializer.Deserialize<PlayerSnapshot>(json, JsonOptions)!;
            return StateMapper.FromSnapshot(snapshot);
        }
    }

    public void SavePlayer(PlayerState player)
    {
        var json = JsonSerializer.Serialize(StateMapper.ToSnapshot(player), JsonOptions);
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO player (id, json) VALUES ($id, $json) " +
                              "ON CONFLICT(id) DO UPDATE SET json = excluded.json;";
            cmd.Parameters.AddWithValue("$id", player.PlayerId);
            cmd.Parameters.AddWithValue("$json", json);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<string> ListPlayerIds()
    {
        var ids = new List<string>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT id FROM player;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }
        }

        return ids;
    }

    // --- Ship ---

    public ShipState? LoadShip(string shipId)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT json FROM ship WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", shipId);
            var json = cmd.ExecuteScalar() as string;
            if (json is null)
            {
                return null;
            }

            var snapshot = JsonSerializer.Deserialize<ShipSnapshot>(json, JsonOptions)!;
            return StateMapper.FromSnapshot(snapshot);
        }
    }

    public void SaveShip(string shipId, ShipState ship)
    {
        var json = JsonSerializer.Serialize(StateMapper.ToSnapshot(ship), JsonOptions);
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO ship (id, json) VALUES ($id, $json) " +
                              "ON CONFLICT(id) DO UPDATE SET json = excluded.json;";
            cmd.Parameters.AddWithValue("$id", shipId);
            cmd.Parameters.AddWithValue("$json", json);
            cmd.ExecuteNonQuery();
        }
    }

    // --- Containers ---

    public void SaveContainer(StoredContainer container)
    {
        var json = JsonSerializer.Serialize(container.Items, JsonOptions);
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO container (id, planet, kind, x, y, z, json) " +
                              "VALUES ($id, $p, $k, $x, $y, $z, $json) " +
                              "ON CONFLICT(id) DO UPDATE SET planet=excluded.planet, kind=excluded.kind, " +
                              "x=excluded.x, y=excluded.y, z=excluded.z, json=excluded.json;";
            cmd.Parameters.AddWithValue("$id", container.Id);
            cmd.Parameters.AddWithValue("$p", container.Planet);
            cmd.Parameters.AddWithValue("$k", container.Kind);
            cmd.Parameters.AddWithValue("$x", container.Position.X);
            cmd.Parameters.AddWithValue("$y", container.Position.Y);
            cmd.Parameters.AddWithValue("$z", container.Position.Z);
            cmd.Parameters.AddWithValue("$json", json);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredContainer> ListContainers(string planet)
    {
        var result = new List<StoredContainer>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT id, kind, x, y, z, json FROM container WHERE planet = $p;";
            cmd.Parameters.AddWithValue("$p", planet);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new StoredContainer
                {
                    Id = reader.GetString(0),
                    Planet = planet,
                    Kind = reader.GetString(1),
                    Position = new Vector3i(reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)),
                    Items = JsonSerializer.Deserialize<List<ItemStack>>(reader.GetString(5), JsonOptions) ?? new List<ItemStack>(),
                });
            }
        }

        return result;
    }

    public void DeleteContainer(string id)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM container WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    // --- Doors (player-built) ---

    public void SaveDoor(StoredDoor door)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO door (planet, x, y, z, kind, axisx) " +
                              "VALUES ($p, $x, $y, $z, $k, $a) " +
                              "ON CONFLICT(planet, x, y, z) DO UPDATE SET kind=excluded.kind, axisx=excluded.axisx;";
            cmd.Parameters.AddWithValue("$p", door.Planet);
            cmd.Parameters.AddWithValue("$x", door.X);
            cmd.Parameters.AddWithValue("$y", door.Y);
            cmd.Parameters.AddWithValue("$z", door.Z);
            cmd.Parameters.AddWithValue("$k", door.Kind);
            cmd.Parameters.AddWithValue("$a", door.AxisX ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredDoor> ListDoors(string planet)
    {
        var result = new List<StoredDoor>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT x, y, z, kind, axisx FROM door WHERE planet = $p;";
            cmd.Parameters.AddWithValue("$p", planet);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new StoredDoor
                {
                    Planet = planet,
                    X = reader.GetInt32(0),
                    Y = reader.GetInt32(1),
                    Z = reader.GetInt32(2),
                    Kind = reader.GetString(3),
                    AxisX = reader.GetInt32(4) != 0,
                });
            }
        }

        return result;
    }

    public void DeleteDoor(string planet, int x, int y, int z)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM door WHERE planet = $p AND x = $x AND y = $y AND z = $z;";
            cmd.Parameters.AddWithValue("$p", planet);
            cmd.Parameters.AddWithValue("$x", x);
            cmd.Parameters.AddWithValue("$y", y);
            cmd.Parameters.AddWithValue("$z", z);
            cmd.ExecuteNonQuery();
        }
    }

    // --- Beacons (placed radio beacons, item 37) ---

    public void SaveBeacon(StoredBeacon beacon)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO beacon (planet, x, y, z, label, owner) " +
                              "VALUES ($p, $x, $y, $z, $l, $o) " +
                              "ON CONFLICT(planet, x, y, z) DO UPDATE SET label=excluded.label, owner=excluded.owner;";
            cmd.Parameters.AddWithValue("$p", beacon.Planet);
            cmd.Parameters.AddWithValue("$x", beacon.X);
            cmd.Parameters.AddWithValue("$y", beacon.Y);
            cmd.Parameters.AddWithValue("$z", beacon.Z);
            cmd.Parameters.AddWithValue("$l", beacon.Label);
            cmd.Parameters.AddWithValue("$o", beacon.OwnerId);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredBeacon> ListBeacons(string planet)
    {
        var result = new List<StoredBeacon>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT x, y, z, label, owner FROM beacon WHERE planet = $p;";
            cmd.Parameters.AddWithValue("$p", planet);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new StoredBeacon
                {
                    Planet = planet,
                    X = reader.GetInt32(0),
                    Y = reader.GetInt32(1),
                    Z = reader.GetInt32(2),
                    Label = reader.GetString(3),
                    OwnerId = reader.GetString(4),
                });
            }
        }

        return result;
    }

    public void DeleteBeacon(string planet, int x, int y, int z)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM beacon WHERE planet = $p AND x = $x AND y = $y AND z = $z;";
            cmd.Parameters.AddWithValue("$p", planet);
            cmd.Parameters.AddWithValue("$x", x);
            cmd.Parameters.AddWithValue("$y", y);
            cmd.Parameters.AddWithValue("$z", z);
            cmd.ExecuteNonQuery();
        }
    }

    // --- Location status ---

    public void SetLocationStatus(string locationId, string status)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO location_status (id, status) VALUES ($id, $s) " +
                              "ON CONFLICT(id) DO UPDATE SET status = excluded.status;";
            cmd.Parameters.AddWithValue("$id", locationId);
            cmd.Parameters.AddWithValue("$s", status);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyDictionary<string, string> LoadLocationStatuses()
    {
        var map = new Dictionary<string, string>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT id, status FROM location_status;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                map[reader.GetString(0)] = reader.GetString(1);
            }
        }

        return map;
    }

    // --- Missions (player/admin-created) ---

    public void SaveMission(MissionDefinition mission)
    {
        var json = JsonSerializer.Serialize(mission, JsonOptions);
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO mission (id, json) VALUES ($id, $json) " +
                              "ON CONFLICT(id) DO UPDATE SET json = excluded.json;";
            cmd.Parameters.AddWithValue("$id", mission.Id);
            cmd.Parameters.AddWithValue("$json", json);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<MissionDefinition> ListMissions()
    {
        var result = new List<MissionDefinition>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT json FROM mission;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var m = JsonSerializer.Deserialize<MissionDefinition>(reader.GetString(0), JsonOptions);
                if (m is not null)
                {
                    result.Add(m);
                }
            }
        }

        return result;
    }

    public void DeleteMission(string id)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM mission WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    // --- Landing zones ---

    public void SaveLandingZone(LandingZone zone)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO landing_zone (player_id, location_id, cx, cz, radius, protected) " +
                              "VALUES ($pid, $loc, $cx, $cz, $r, $p) " +
                              "ON CONFLICT(player_id, location_id) DO UPDATE SET cx=excluded.cx, cz=excluded.cz, " +
                              "radius=excluded.radius, protected=excluded.protected;";
            cmd.Parameters.AddWithValue("$pid", zone.PlayerId);
            cmd.Parameters.AddWithValue("$loc", zone.LocationId);
            cmd.Parameters.AddWithValue("$cx", zone.CenterX);
            cmd.Parameters.AddWithValue("$cz", zone.CenterZ);
            cmd.Parameters.AddWithValue("$r", zone.Radius);
            cmd.Parameters.AddWithValue("$p", zone.Protected ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<LandingZone> ListLandingZones(string locationId)
    {
        var result = new List<LandingZone>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT player_id, cx, cz, radius, protected FROM landing_zone WHERE location_id = $loc;";
            cmd.Parameters.AddWithValue("$loc", locationId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new LandingZone
                {
                    PlayerId = reader.GetString(0),
                    LocationId = locationId,
                    CenterX = reader.GetInt32(1),
                    CenterZ = reader.GetInt32(2),
                    Radius = reader.GetInt32(3),
                    Protected = reader.GetInt32(4) != 0,
                });
            }
        }

        return result;
    }

    // --- Maintenance ---

    public void Flush()
    {
        lock (_gate)
        {
            // Checkpoint the WAL into the main database file so a copy is fully consistent.
            Execute("PRAGMA wal_checkpoint(TRUNCATE);");
        }
    }

    public string CreateBackup(string label)
    {
        lock (_gate)
        {
            Flush();
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                label = label.Replace(c, '_');
            }

            var target = Path.Combine(_paths.BackupsDirectory, label + ".db");
            if (File.Exists(target))
            {
                File.Delete(target);
            }

            // VACUUM INTO produces a transactionally consistent standalone copy.
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "VACUUM INTO $target;";
            cmd.Parameters.AddWithValue("$target", target);
            cmd.ExecuteNonQuery();
            return target;
        }
    }

    private void Execute(string sql)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_connection is not null)
            {
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    cmd.ExecuteNonQuery();
                }
                catch
                {
                    // best-effort checkpoint on close
                }

                _connection.Dispose();
                _connection = null;
            }

            // Release the pooled native connection handles so the file can be deleted (tests).
            SqliteConnection.ClearAllPools();
        }
    }
}
