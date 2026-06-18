using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Player-built space stations (item 20 S4). The player deploys a <b>station core</b> on a spacewalk, builds a
/// hull + an airlock around it (the free-EVA build flow, S2), and once it has an airlock + a minimum size it is
/// <b>commissioned</b>: registered as a boardable body on the star map, given a dock contact, and persisted.
/// Boarding reuses the orbital-station void world, stamping the player's own voxel build as the interior.
/// </summary>
public sealed partial class GameServer
{
    private const int MinStationBlocks = 12;        // a core + a small hull + an airlock
    private const string StationCoreBlock = "station_core";

    private int _nextStationSeq;

    /// <summary>Commissioned player stations by id — their live voxel cells (for stamping the boardable interior
    /// + persistence).</summary>
    private readonly Dictionary<string, SpaceStructure> _playerStationCells = new();

    /// <summary>Persisted player stations grouped by the body whose space instance they float in, so they are
    /// re-created when that instance is next entered.</summary>
    private readonly Dictionary<string, List<StoredSpaceStructure>> _persistedStationsByLocation = new();

    /// <summary>Host body each player station orbits (station id → body id). Drives the travel-screen "you have a
    /// station here" badge and where a menu-boarded station undocks to.</summary>
    private readonly Dictionary<string, string> _stationHostBody = new();

    /// <summary>Deploys a station core a few units ahead of the suit: a new owned station structure seeded with
    /// the core block. The player then builds a hull + airlock around it to commission it.</summary>
    public void DeployStationCore(string playerId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return;
        }

        if (!_playerInstance.TryGetValue(playerId, out var iid) || !_spaceInstances.TryGetValue(iid, out var instance))
        {
            Reject(session, "station", "You are not in space.");
            return;
        }

        if (!session.State.InEva)
        {
            Reject(session, "station", "Step outside (EVA) to deploy a station core.");
            return;
        }

        var core = _content.GetBlock(StationCoreBlock)?.NumericId ?? BlockId.Air;
        if (core.IsAir)
        {
            Reject(session, "station", "Station core block is missing from content.");
            return;
        }

        bool free = !Rules.CraftingCostsMaterials || session.State.InstantBuild;
        var pool = new MaterialPool(_content, session.State, _ship);
        if (!free)
        {
            if (pool.Count(StationCoreBlock) < 1)
            {
                Reject(session, "station", "You need a station core (craft one at a workshop).");
                return;
            }

            pool.Remove(new[] { new ItemAmount(StationCoreBlock, 1) });
            SendInventory(session);
        }

        // Place it a few units ahead of the suit, on its heading.
        float yaw = instance.PlayerPoses.TryGetValue(playerId, out var pose) ? pose.Yaw : 0f;
        double rad = yaw * System.Math.PI / 180.0;
        var suit = instance.ShipPosition;
        var at = new Vector3f(suit.X + (float)System.Math.Sin(rad) * 5f, suit.Y, suit.Z + (float)System.Math.Cos(rad) * 5f);

        var s = new SpaceStructure
        {
            Id = "pstation:" + playerId + ":" + (_nextStationSeq++),
            Kind = "station",
            OwnerId = playerId,
            Position = at,
        };
        s.Set(new Vector3i(0, 0, 0), core);
        s.Width = s.Height = s.Length = 1;
        instance.Structures[s.Id] = s;

        foreach (var pid in instance.Players)
        {
            if (FindSessionByPlayerId(pid) is { } sess)
            {
                SendShipDesign(sess, s);
            }
        }

        Send(session, new ServerMessage { Text = "Station core deployed — build a hull + an airlock door around it." });
    }

    private void HandleDeployStationCore(PlayerSession session) => DeployStationCore(session.State.PlayerId);

    private bool StationHasAirlock(SpaceStructure s)
    {
        ushort slide = _content.GetBlock("door_slide")?.NumericId.Value ?? 0;
        ushort hinge = _content.GetBlock("door_hinge")?.NumericId.Value ?? 0;
        if (slide == 0 && hinge == 0)
        {
            return false;
        }

        foreach (var b in s.Cells.Values)
        {
            if (b.Value == slide || b.Value == hinge)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Commissions a station once it has a hull (min blocks) + an airlock: registers it as a boardable
    /// body on the star map + a dock contact, and persists it.</summary>
    private void TryCommissionStation(SpaceInstance instance, SpaceStructure s, PlayerSession owner)
    {
        if (s.Boardable || s.Cells.Count < MinStationBlocks || !StationHasAirlock(s))
        {
            return;
        }

        s.Boardable = true;
        s.Name = string.IsNullOrEmpty(s.Name) ? ((owner?.State.Name ?? "Player") + "'s Station") : s.Name;
        _playerStationCells[s.Id] = s;

        // Registry entry so BoardStation can dock it; its interior is stamped from the player's cells.
        _stationsById[s.Id] = new BoardableStation
        {
            Id = s.Id,
            Name = s.Name,
            SizeTier = "small",
            SpacePosition = s.Position,
            Origin = new Vector3i(8, 64, 8),
        };

        // A neutral dock contact so EVA "press E to board" works (like the NPC stations).
        if (!instance.Entities.Any(e => e.Id == s.Id))
        {
            instance.Entities.Add(new CombatEntity
            {
                Id = s.Id,
                Kind = CombatEntityKind.SpaceStation,
                Name = s.Name,
                Hostile = false,
                Hull = 1f,
                HullMax = 1f,
                Position = s.Position,
            });
        }

        AddStationBodyToGalaxy(s.Id, s.Name);
        PersistStation(instance, s);
        BroadcastSpaceState(instance);

        if (owner is not null)
        {
            Send(owner, new ServerMessage { Text = $"Station commissioned: {s.Name} — now on the star map + boardable." });
        }

        _log.Info($"Player station '{s.Name}' ({s.Id}) commissioned with {s.Cells.Count} blocks.");
    }

    private void AddStationBodyToGalaxy(string id, string name)
    {
        var current = _galaxy.FindBody(_meta.ActiveLocationId);
        var sys = _galaxy.Systems.FirstOrDefault(x => x.Id == current?.SystemId) ?? _galaxy.Systems.FirstOrDefault();
        if (sys is null || sys.Bodies.Any(b => b.Id == id))
        {
            return;
        }

        sys.Bodies.Add(new CelestialBody
        {
            Id = id,
            Name = name,
            Kind = CelestialKind.SpaceStation,
            SystemId = sys.Id,
            Status = GenerationStatus.Discovered,
            SystemX = current?.SystemX ?? 0f,
            SystemY = current?.SystemY ?? 0f,
            SystemZ = current?.SystemZ ?? 0f,
        });
    }

    private void PersistStation(SpaceInstance instance, SpaceStructure s)
    {
        string loc = instance.Id.StartsWith("space:") ? instance.Id.Substring("space:".Length) : instance.Id;
        _stationHostBody[s.Id] = loc; // remember the body it orbits (travel-screen badge + menu-board return)
        _repo.SaveSpaceStructure(new StoredSpaceStructure
        {
            Id = s.Id,
            OwnerId = s.OwnerId,
            Name = s.Name,
            Location = loc,
            PosX = s.Position.X,
            PosY = s.Position.Y,
            PosZ = s.Position.Z,
            Boardable = s.Boardable,
            Blocks = SerializeCells(s.Cells),
        });
    }

    private static string SerializeCells(Dictionary<Vector3i, BlockId> cells)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var kv in cells)
        {
            if (sb.Length > 0)
            {
                sb.Append(';');
            }

            sb.Append(kv.Key.X).Append(':').Append(kv.Key.Y).Append(':').Append(kv.Key.Z).Append(':').Append(kv.Value.Value);
        }

        return sb.ToString();
    }

    private static void DeserializeCells(string blocks, SpaceStructure into)
    {
        if (string.IsNullOrEmpty(blocks))
        {
            return;
        }

        foreach (var cell in blocks.Split(';'))
        {
            var p = cell.Split(':');
            if (p.Length == 4
                && int.TryParse(p[0], out var x) && int.TryParse(p[1], out var y)
                && int.TryParse(p[2], out var z) && ushort.TryParse(p[3], out var b))
            {
                into.Set(new Vector3i(x, y, z), new BlockId(b));
            }
        }
    }

    /// <summary>Loads persisted player stations at startup: reconstructs each, registers it as boardable + on the
    /// star map, and groups them by location so they're re-created when their space instance is next entered.</summary>
    private void LoadPlayerStations()
    {
        _persistedStationsByLocation.Clear();
        foreach (var row in _repo.ListSpaceStructures())
        {
            if (!_persistedStationsByLocation.TryGetValue(row.Location, out var list))
            {
                list = _persistedStationsByLocation[row.Location] = new List<StoredSpaceStructure>();
            }

            list.Add(row);
            _stationHostBody[row.Id] = row.Location; // host body for the travel-screen badge + menu-board return

            var s = new SpaceStructure
            {
                Id = row.Id,
                Kind = "station",
                OwnerId = row.OwnerId,
                Name = row.Name,
                Boardable = row.Boardable,
                Position = new Vector3f(row.PosX, row.PosY, row.PosZ),
            };
            DeserializeCells(row.Blocks, s);
            _playerStationCells[row.Id] = s;
            _stationsById[row.Id] = new BoardableStation
            {
                Id = row.Id,
                Name = row.Name,
                SizeTier = "small",
                SpacePosition = s.Position,
                Origin = new Vector3i(8, 64, 8),
            };
            AddStationBodyToGalaxy(row.Id, row.Name);
        }

        if (_playerStationCells.Count > 0)
        {
            _log.Info($"Loaded {_playerStationCells.Count} persisted player station(s).");
        }
    }

    /// <summary>Re-creates persisted player stations in a freshly created space instance + their dock contacts.</summary>
    private void AddPersistedStations(SpaceInstance instance)
    {
        string loc = instance.Id.StartsWith("space:") ? instance.Id.Substring("space:".Length) : instance.Id;
        if (!_persistedStationsByLocation.TryGetValue(loc, out var rows))
        {
            return;
        }

        foreach (var row in rows)
        {
            if (!_playerStationCells.TryGetValue(row.Id, out var s))
            {
                continue;
            }

            instance.Structures[s.Id] = s;
            if (!instance.Entities.Any(e => e.Id == s.Id))
            {
                instance.Entities.Add(new CombatEntity
                {
                    Id = s.Id,
                    Kind = CombatEntityKind.SpaceStation,
                    Name = s.Name,
                    Hostile = false,
                    Hull = 1f,
                    HullMax = 1f,
                    Position = s.Position,
                });
            }
        }
    }

    /// <summary>Stamps a player-built station's voxel cells into its void world for boarding — the void-world
    /// analogue of the procedural <see cref="StampStation"/>, using the player's build as the interior.</summary>
    private void StampPlayerStation(BoardableStation station, SpaceStructure src)
    {
        if (station.Stamped)
        {
            return;
        }

        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
        foreach (var c in src.Cells.Keys)
        {
            if (c.X < minX) minX = c.X; if (c.Y < minY) minY = c.Y; if (c.Z < minZ) minZ = c.Z;
            if (c.X > maxX) maxX = c.X; if (c.Y > maxY) maxY = c.Y; if (c.Z > maxZ) maxZ = c.Z;
        }

        if (src.Cells.Count == 0)
        {
            minX = minY = minZ = maxX = maxY = maxZ = 0;
        }

        // Spawn at the build's centre with a guaranteed floor pad + headroom (never fall through into the void).
        int cx = station.Origin.X + (maxX - minX) / 2;
        int cz = station.Origin.Z + (maxZ - minZ) / 2;
        int fy = station.Origin.Y;
        var hull = _content.GetBlock("iron_wall")?.NumericId ?? BlockId.Air;

        // Stamp the whole build in one transaction: a station can be hundreds of voxels and each SetBlock is
        // otherwise its own WAL commit — that loop stalls the tick thread for as long as it runs.
        _repo.RunInTransaction(() =>
        {
            foreach (var kv in src.Cells)
            {
                var w = new Vector3i(station.Origin.X + kv.Key.X - minX, station.Origin.Y + kv.Key.Y - minY, station.Origin.Z + kv.Key.Z - minZ);
                _world.SetBlock(w, kv.Value);
            }

            if (!hull.IsAir)
            {
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        _world.SetBlock(new Vector3i(cx + dx, fy, cz + dz), hull);
                    }
            }

            _world.SetBlock(new Vector3i(cx, fy + 1, cz), BlockId.Air);
            _world.SetBlock(new Vector3i(cx, fy + 2, cz), BlockId.Air);
        });

        station.Spawn = new Vector3f(cx + 0.5f, fy + 1f, cz + 0.5f);
        station.Markers.Clear();
        station.Markers.Add(("spawn", station.Spawn));

        // Manual placeables (Feature 2): vendor / mission-board / container blocks the owner built into their
        // station become interaction points, reusing the SAME trade/mission/loot code paths procedural
        // stations use (SpawnStationNpcs staffs the vendor/board markers; placed containers are lootable).
        RegisterPlayerStationPlaceables(station, src, minX, minY, minZ);

        station.Structure = null; // player station: no procedural structure bounds
        station.Stamped = true;
        _log.Info($"Player station '{station.Name}' stamped into its void world at ({station.Origin.X},{station.Origin.Y},{station.Origin.Z}).");
    }

    /// <summary>Scans a player station's own placed cells for the manual placeable blocks and wires them as
    /// interaction points in the stamped void world: a <c>station_vendor</c> / <c>mission_board</c> becomes a
    /// station marker (so <see cref="SpawnStationNpcs"/> staffs it and <see cref="NearSpaceStationVendor"/> /
    /// <see cref="NearSpaceStationMissionBoard"/> fire for boarders), and a <c>station_container</c> becomes a
    /// lootable/stash-able container reusing the existing crate code paths. The blocks themselves persist via
    /// the station's cells, so these interaction points are re-derived on every board (nothing auto-spawns).</summary>
    private void RegisterPlayerStationPlaceables(BoardableStation station, SpaceStructure src, int minX, int minY, int minZ)
    {
        var vendor = _content.GetBlock("station_vendor")?.NumericId ?? BlockId.Air;
        var board = _content.GetBlock("mission_board")?.NumericId ?? BlockId.Air;
        var container = _content.GetBlock("station_container")?.NumericId ?? BlockId.Air;

        bool hasBoard = false;
        foreach (var kv in src.Cells)
        {
            var w = new Vector3i(station.Origin.X + kv.Key.X - minX, station.Origin.Y + kv.Key.Y - minY, station.Origin.Z + kv.Key.Z - minZ);
            var center = new Vector3f(w.X + 0.5f, w.Y + 0.5f, w.Z + 0.5f);

            if (!vendor.IsAir && kv.Value.Value == vendor.Value)
            {
                station.Markers.Add(("vendor", center));
            }
            else if (!board.IsAir && kv.Value.Value == board.Value)
            {
                station.Markers.Add(("mission_board", center));
                hasBoard = true;
            }
            else if (!container.IsAir && kv.Value.Value == container.Value)
            {
                RegisterStationContainer(station, w);
            }
        }

        // Seed the mission board's first window so it offers jobs even before any player opens the list; the
        // per-player window then slides it (item 13). Mirrors the procedural-station stamp.
        if (hasBoard)
        {
            string prefix = $"station_{(uint)BlocksBeyondTheStars.WorldGeneration.WorldGenerator.StableHash(station.Id) % 100000u}_";
            StockBoard(prefix, station.Id, _stationMissionIds, CoinGiverName(station.Id));
        }
    }

    /// <summary>Registers a player-placed station container as an (empty) lootable/stash-able crate in the
    /// active station world, reusing the existing container code paths. The container BLOCK persists via the
    /// station's cells, so the entity is re-derived per board (its in-session contents are runtime only).</summary>
    private void RegisterStationContainer(BoardableStation station, Vector3i pos)
    {
        string id = "scontainer_" + station.Id + "_" + pos.X + "_" + pos.Y + "_" + pos.Z;
        if (_containers.Any(c => c.Id == id))
        {
            return;
        }

        _containers.Add(new StoredContainer
        {
            Id = id,
            Planet = _world.LocationId,
            Kind = "crate",
            Position = pos,
            Items = new List<Shared.State.ItemStack>(),
        });
        BroadcastContainers();
    }

    /// <summary>The owner (or an admin) renames a commissioned station they built — via the Map detail "Rename"
    /// button or pressing E at the station core. Updates the runtime structure, the boardable registry, the star-map
    /// body, any live space contact, and the persisted row, then refreshes every player's star map.</summary>
    private void HandleSetStationName(PlayerSession session, SetStationNameIntent intent)
    {
        if (!_playerStationCells.TryGetValue(intent.StationId, out var s))
        {
            Reject(session, "station", "No such station — only stations you built can be renamed.");
            return;
        }

        if (!session.State.IsAdmin && s.OwnerId != session.State.PlayerId)
        {
            Reject(session, "station", "Only the owner can rename this station.");
            return;
        }

        string name = SanitizeStationName(intent.Name);
        if (string.IsNullOrEmpty(name))
        {
            name = (string.IsNullOrWhiteSpace(s.OwnerId) ? "Player" : s.OwnerId) + "'s Station";
        }

        s.Name = name;
        if (_stationsById.TryGetValue(s.Id, out var reg))
        {
            reg.Name = name;
        }

        if (_galaxy?.FindBody(s.Id) is { } body)
        {
            body.Name = name; // the star-map entry
        }

        foreach (var inst in _spaceInstances.Values)
        {
            var contact = inst.Entities.FirstOrDefault(e => e.Id == s.Id);
            if (contact is not null)
            {
                contact.Name = name; // the live space-flight dock contact
            }
        }

        if (_stationHostBody.TryGetValue(s.Id, out var loc))
        {
            _repo.SaveSpaceStructure(new StoredSpaceStructure
            {
                Id = s.Id,
                OwnerId = s.OwnerId,
                Name = name,
                Location = loc,
                PosX = s.Position.X,
                PosY = s.Position.Y,
                PosZ = s.Position.Z,
                Boardable = s.Boardable,
                Blocks = SerializeCells(s.Cells),
            });
        }

        BroadcastStarMap(); // the renamed station updates for everyone (the star map is shared)
        Send(session, new ServerMessage { Text = $"Station renamed to {name}." });
    }

    /// <summary>Host bodies (planet/moon/asteroid) where the given player has a commissioned station orbiting —
    /// the travel screen badges these "you have a station here".</summary>
    private string[] MyStationBodyIds(string ownerId)
        => _playerStationCells.Values
            .Where(s => s.OwnerId == ownerId && s.Boardable && _stationHostBody.ContainsKey(s.Id))
            .Select(s => _stationHostBody[s.Id])
            .Where(loc => !string.IsNullOrEmpty(loc))
            .Distinct()
            .ToArray();

    /// <summary>The owning player's name for a station body (the player id is the display name), or empty for a
    /// procedural/NPC station.</summary>
    private string StationOwnerName(string stationId)
        => _playerStationCells.TryGetValue(stationId, out var s) ? s.OwnerId : string.Empty;

    /// <summary>Trims a player-typed station name to a single short line (drops newlines, clamps length).</summary>
    private static string SanitizeStationName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return trimmed.Length > BaseNameMaxLength ? trimmed.Substring(0, BaseNameMaxLength) : trimmed;
    }

    // ---------------- Test hooks ----------------

    public void SetStationNameForTest(PlayerSession session, string stationId, string name)
        => HandleSetStationName(session, new SetStationNameIntent { StationId = stationId, Name = name });

    public void DeployStationCoreForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            Serve(s);
            DeployStationCore(playerId);
        }
    }

    /// <summary>Test/inspection: the id of the station the player owns in their instance, or null.</summary>
    public string? OwnedStationIdForTest(string playerId)
    {
        if (_playerInstance.TryGetValue(playerId, out var iid) && _spaceInstances.TryGetValue(iid, out var inst))
        {
            foreach (var st in inst.Structures.Values)
            {
                if (st.Kind == "station" && st.OwnerId == playerId)
                {
                    return st.Id;
                }
            }
        }

        return null;
    }

    /// <summary>Test/inspection: whether a station id is commissioned (boardable + registered).</summary>
    public bool StationIsBoardableForTest(string id)
        => _stationsById.ContainsKey(id) && _playerStationCells.TryGetValue(id, out var s) && s.Boardable;
}
