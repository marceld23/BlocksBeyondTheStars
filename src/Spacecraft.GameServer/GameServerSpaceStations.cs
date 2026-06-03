using Spacecraft.Networking.Messages;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Primitives;
using Spacecraft.Shared.World;
using Spacecraft.WorldGeneration;

namespace Spacecraft.GameServer;

/// <summary>
/// Boardable procedural space stations. Stations appear as neutral contacts in the local space
/// instance; when the player boards one, the server stamps its voxel interior into a reserved
/// world area and moves the player inside. The client still only renders blocks and sends intents.
/// </summary>
public sealed partial class GameServer
{
    private const float StationBoardRange = 70f;
    private const float StationMarkerReach = 4f;

    private readonly Dictionary<string, BoardableStation> _stationsById = new();
    private readonly Dictionary<string, string> _boardedStation = new();
    private readonly HashSet<string> _stationMissionIds = new();

    private sealed class BoardableStation
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string SizeTier { get; init; } = "medium";
        public Vector3f SpacePosition { get; init; }
        public Vector3i Origin { get; init; }
        public bool Stamped { get; set; }
        public StationStructure? Structure { get; set; }
        public List<(string Type, Vector3f Pos)> Markers { get; } = new();
        public Vector3f Spawn { get; set; }
    }

    /// <summary>Station markers for tests/inspection; empty until the station is stamped.</summary>
    public IReadOnlyList<(string Type, Vector3f Pos)> SpaceStationMarkers
        => _stationsById.Values.SelectMany(s => s.Markers).ToList();

    /// <summary>True while the player is walking inside a boarded station.</summary>
    public bool InStation(string playerId) => _boardedStation.ContainsKey(playerId);

    /// <summary>Name of the station the player is boarded on, or empty when not on one.</summary>
    private string CurrentStationName(string playerId)
        => _boardedStation.TryGetValue(playerId, out var id) && _stationsById.TryGetValue(id, out var st)
            ? st.Name
            : string.Empty;

    /// <summary>Mission ids offered by station boards (test/inspection).</summary>
    public IReadOnlyCollection<string> StationMissionIds => _stationMissionIds;

    /// <summary>Adds station contacts to the given space instance, derived from the galaxy when possible.</summary>
    private void AddStationContacts(SpaceInstance instance)
    {
        foreach (var station in StationContactsForCurrentSystem())
        {
            instance.Entities.Add(new CombatEntity
            {
                Id = station.Id,
                Kind = CombatEntityKind.SpaceStation,
                Name = station.Name,
                Hostile = false,
                Hull = 1f,
                HullMax = 1f,
                Position = station.SpacePosition,
            });
        }
    }

    private IEnumerable<BoardableStation> StationContactsForCurrentSystem()
    {
        var current = _galaxy.FindBody(_meta.ActiveLocationId);
        string systemId = current?.SystemId ?? _galaxy.Systems.FirstOrDefault()?.Id ?? "sys0";
        var bodies = _galaxy.Systems.FirstOrDefault(s => s.Id == systemId)?.Bodies
            .Where(b => b.Kind == CelestialKind.SpaceStation)
            .ToList() ?? new List<CelestialBody>();

        if (bodies.Count == 0 && _meta.Description.SpaceStations != Frequency.Off)
        {
            bodies.Add(new CelestialBody
            {
                Id = systemId + "-st-local",
                Name = "Orbital Station",
                Kind = CelestialKind.SpaceStation,
                SystemId = systemId,
            });
        }

        int i = 0;
        foreach (var body in bodies)
        {
            yield return GetOrCreateStation(body.Id, body.Name, i++);
        }
    }

    private BoardableStation GetOrCreateStation(string id, string name, int index)
    {
        if (_stationsById.TryGetValue(id, out var existing))
        {
            return existing;
        }

        long sSeed = _meta.Seed ^ WorldGenerator.StableHash("station:" + id);
        string tier = StationTier(sSeed);
        var station = new BoardableStation
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(name) ? "Orbital Station" : name,
            SizeTier = tier,
            SpacePosition = new Vector3f(0f, 0f, 42f + index * 30f),
            Origin = new Vector3i(900 + index * 120, 96, 900),
        };
        _stationsById[id] = station;
        return station;
    }

    private static string StationTier(long seed)
    {
        int roll = (int)(System.Math.Abs(seed) % 100);
        return roll switch
        {
            < 45 => "small",
            < 80 => "medium",
            < 95 => "large",
            _ => "huge",
        };
    }

    /// <summary>Boards a station from the player's current space instance, after range validation.</summary>
    public void BoardStation(string playerId, string stationId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return;
        }

        if (!_playerInstance.TryGetValue(playerId, out var instanceId) ||
            !_spaceInstances.TryGetValue(instanceId, out var instance))
        {
            Reject(session, "station", "You are not in space.");
            return;
        }

        var contact = instance.Entities.FirstOrDefault(e => e.Id == stationId && e.Kind == CombatEntityKind.SpaceStation);
        if (contact is null || !_stationsById.TryGetValue(stationId, out var station))
        {
            Reject(session, "station", "No such station contact.");
            return;
        }

        if (contact.Position.DistanceSquared(instance.ShipPosition) > StationBoardRange * StationBoardRange)
        {
            Reject(session, "station", "Move closer to dock with the station.");
            return;
        }

        StampStation(station);

        instance.Players.Remove(playerId);
        _playerInstance.Remove(playerId);
        _boardedStation[playerId] = station.Id;

        session.State.Position = station.Spawn;
        session.State.AboardShip = false;

        Send(session, new SpaceClosed { Reason = "Docked with station.", ShipDisabled = false });
        Send(session, new StationBoarded
        {
            StationId = station.Id,
            Name = station.Name,
            X = station.Spawn.X,
            Y = station.Spawn.Y,
            Z = station.Spawn.Z,
        });
        SendPlayerState(session);
        SendInventory(session);
        SendNpcs(session); // show the station's crew (vendor / quartermaster / dockhands)
        _log.Info($"Player '{session.State.Name}' boarded station '{station.Name}'.");
    }

    /// <summary>Leaves a boarded station and returns the player to the ship's respawn point.</summary>
    public void LeaveStation(string playerId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null || !_boardedStation.Remove(playerId))
        {
            return;
        }

        session.State.Position = _shipStamped ? _healTank : session.State.RespawnPoint;
        session.State.AboardShip = true;
        Send(session, new ServerMessage { Text = "Returned to your ship." });
        SendPlayerState(session);
        SendInventory(session);
    }

    private void StampStation(BoardableStation station)
    {
        if (station.Stamped)
        {
            return;
        }

        long sSeed = _meta.Seed ^ WorldGenerator.StableHash("station:" + station.Id);
        var structure = StationGenerator.Generate(station.SizeTier, sSeed, _content);
        station.Structure = structure;

        for (int x = 0; x < structure.Width; x++)
        for (int y = 0; y < structure.Height; y++)
        for (int z = 0; z < structure.Length; z++)
        {
            ushort b = structure.Get(x, y, z);
            if (b != 0)
            {
                _world.SetBlock(new Vector3i(station.Origin.X + x, station.Origin.Y + y, station.Origin.Z + z), new BlockId(b));
            }
        }

        station.Markers.Clear();
        bool hasBoard = false;
        foreach (var m in structure.Markers)
        {
            var pos = new Vector3f(station.Origin.X + m.LocalPos.X + 0.5f,
                station.Origin.Y + m.LocalPos.Y + 0.5f,
                station.Origin.Z + m.LocalPos.Z + 0.5f);
            station.Markers.Add((m.Type, pos));
            if (m.Type == "mission_board")
            {
                hasBoard = true;
            }
        }

        station.Spawn = station.Markers.FirstOrDefault(m => m.Type == "hangar").Pos;
        if (station.Spawn.Equals(Vector3f.Zero))
        {
            station.Spawn = new Vector3f(station.Origin.X + 3.5f, station.Origin.Y + 2f, station.Origin.Z + 3.5f);
        }

        if (hasBoard)
        {
            GenerateStationMissions(station);
        }

        SpawnStationNpcs(station);

        station.Stamped = true;
        _log.Info($"Station '{station.Name}' stamped at ({station.Origin.X}, {station.Origin.Y}, {station.Origin.Z}) with {station.Markers.Count} markers.");
    }

    /// <summary>
    /// Populates a boarded station with crew NPCs from its markers — a vendor at the trade post, a
    /// quartermaster at the mission board, and dockhands at the hangar/quarters. They live at the
    /// station's (far-away) interior coordinates, so they coexist with any planet-side settlement NPCs;
    /// only the ones near the player are visible. Deterministic from the station seed.
    /// </summary>
    private void SpawnStationNpcs(BoardableStation station)
    {
        var rng = new System.Random(unchecked((int)(_meta.Seed ^ WorldGenerator.StableHash("station-npc:" + station.Id))));
        int added = 0;
        foreach (var (type, pos) in station.Markers)
        {
            string? role = type switch
            {
                "vendor" => "vendor",
                "mission_board" => "quartermaster",
                "quarters" => "settler",
                "hangar" => "settler", // a dockhand
                _ => null,
            };

            if (role is null)
            {
                continue; // heal-tank / structural markers don't get a crew member
            }

            _npcs.Add(MakeNpc(role, "traders", robotic: false, pos, rng));
            added++;
        }

        if (added > 0)
        {
            _log.Info($"Spawned {added} crew NPCs at station '{station.Name}'.");
        }
    }

    private void GenerateStationMissions(BoardableStation station)
    {
        string prefix = $"station_{(uint)WorldGenerator.StableHash(station.Id) % 100000u}_";
        if (_stationMissionIds.Any(id => id.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return;
        }

        (string Need, int Target, string Reward, int RewardN)[] templates =
        {
            ("iron_ore", 12, "titanium_plate", 1),
            ("carbon", 10, "energy_cell_1", 1),
            ("data_fragment", 2, "medpack", 2),
        };

        int baseId = unchecked((int)WorldGenerator.StableHash(station.Id));
        for (int i = 0; i < 2; i++)
        {
            var tpl = templates[System.Math.Abs(baseId + i) % templates.Length];
            if (_content.GetItem(tpl.Need) is null || _content.GetItem(tpl.Reward) is null)
            {
                continue;
            }

            var def = new Shared.Missions.MissionDefinition
            {
                Id = $"station_{(uint)WorldGenerator.StableHash(station.Id) % 100000u}_{i}",
                Source = Shared.Missions.MissionSource.System,
                NameKey = "mission.settlement.gather.title",
                DescriptionKey = "mission.settlement.gather.desc",
                Objectives =
                {
                    new Shared.Missions.MissionObjective
                    {
                        Type = Shared.Missions.MissionObjectiveType.Deliver,
                        Target = tpl.Need,
                        Required = tpl.Target,
                    },
                },
                Rewards = { new Shared.Definitions.ItemAmount(tpl.Reward, tpl.RewardN) },
                Active = true,
            };

            _missionDefs[def.Id] = def;
            _stationMissionIds.Add(def.Id);
        }
    }

    private bool NearStationMarker(Shared.State.PlayerState player, string type, float reach)
    {
        if (!_boardedStation.TryGetValue(player.PlayerId, out var stationId) ||
            !_stationsById.TryGetValue(stationId, out var station))
        {
            return false;
        }

        return station.Markers.Any(m => m.Type == type && player.Position.DistanceSquared(m.Pos) <= reach * reach);
    }

    /// <summary>True if the player is at a station vendor, enabling market barter there.</summary>
    public bool NearSpaceStationVendor(Shared.State.PlayerState player)
        => NearStationMarker(player, "vendor", StationMarkerReach);

    /// <summary>True if the player is at a station mission board.</summary>
    public bool NearSpaceStationMissionBoard(Shared.State.PlayerState player)
        => NearStationMarker(player, "mission_board", StationMarkerReach);

    /// <summary>True if a mission is offered by a space station board.</summary>
    public bool IsStationMission(string missionId) => _stationMissionIds.Contains(missionId);

    private bool IsStationBlock(Vector3i pos)
    {
        foreach (var station in _stationsById.Values)
        {
            if (!station.Stamped || station.Structure is null)
            {
                continue;
            }

            if (pos.X >= station.Origin.X && pos.X < station.Origin.X + station.Structure.Width &&
                pos.Y >= station.Origin.Y && pos.Y < station.Origin.Y + station.Structure.Height &&
                pos.Z >= station.Origin.Z && pos.Z < station.Origin.Z + station.Structure.Length)
            {
                return true;
            }
        }

        return false;
    }

    private void HandleBoardStation(PlayerSession session, BoardStationIntent intent)
        => BoardStation(session.State.PlayerId, intent.StationId);

    private void HandleLeaveStation(PlayerSession session)
        => LeaveStation(session.State.PlayerId);
}
