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

    /// <summary>Chance to place a hand-designed template (when the pool is non-empty) instead of a
    /// procedurally generated station/settlement; the rest stay procedural.</summary>
    private const double StructureTemplateChance = 0.35;

    /// <summary>Planet type of the void world that backs every orbital station (space sky, life support,
    /// no terrain/weather/clouds). See data/planets.json.</summary>
    private const string StationPlanetType = "orbital_station";

    private readonly Dictionary<string, BoardableStation> _stationsById = new();
    private readonly Dictionary<string, string> _boardedStation = new();
    // Where to send a player back when they leave a station: (planet location id, planet type key).
    private readonly Dictionary<string, (string Loc, string Type)> _boardedReturn = new();
    // Players who docked a station while on an EVA: their ship stayed floating at this position, so undocking
    // returns them to the float (not a re-launch). Keyed by player id → the ship's parked space position.
    private readonly Dictionary<string, Vector3f> _dockedFromEva = new();
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
            // The station lives in its own void world now, so a clean grounded origin is fine (the void
            // gives it the free-floating-in-space look; no planet terrain anywhere near).
            Origin = new Vector3i(8, 64, 8),
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

        // Remember the planet to return to, then leave the space instance.
        _boardedReturn[playerId] = (session.CurrentLocationId, _world.PlanetKey);

        // Docking on an EVA: the ship stays floating where it is — remember its spot so undocking returns you
        // to the float next to it, rather than re-launching.
        if (session.State.InEva)
        {
            _dockedFromEva[playerId] = instance.ShipPosition;
        }

        instance.Players.Remove(playerId);
        _playerInstance.Remove(playerId);

        // Boarding is a world transition into the station's own free-floating void world (space all around,
        // life support, no weather) — the same robust WorldReset path planet travel uses, so the player no
        // longer falls through to the planet.
        string stationLoc = "station:" + station.Id;
        LoadWorld(StationPlanetType, stationLoc); // loads/creates the void world + sets the Active cursor
        SetCurrent(session);
        if (_playerStationCells.TryGetValue(station.Id, out var playerCells))
        {
            StampPlayerStation(station, playerCells); // item 20 S4: the player's own build is the interior
        }
        else
        {
            StampStation(station);                 // procedural station: stamps the structure once; computes Spawn
        }
        RegisterStationDoors(station.Markers);     // sliding doors fill the station's module doorways
        if (_npcs.Count == 0)
        {
            SpawnStationNpcs(station);             // (re)populate the crew when the station world is fresh
        }

        _boardedStation[playerId] = station.Id;
        session.CurrentLocationId = stationLoc;
        session.State.Position = station.Spawn;
        session.State.AboardShip = false;
        session.State.InEva = false; // docking ends any spacewalk — the station has life support
        session.SentChunks.Clear();

        Send(session, new SpaceClosed { Reason = "Docked with station.", ShipDisabled = false });
        Send(session, new WorldReset { PlanetType = StationPlanetType, PlanetName = station.Name, SystemName = string.Empty, Hyperjump = false });
        SendPlayerState(session);
        SendEnvironment(session);
        SendInventory(session);
        SendNpcs(session); // the station's crew (vendor / quartermaster / dockhands)
        SendDoors(session); // the station's sliding doors
        Send(session, new StationBoarded
        {
            StationId = station.Id,
            Name = station.Name,
            X = station.Spawn.X,
            Y = station.Spawn.Y,
            Z = station.Spawn.Z,
        });
        ShipAiOnStationBoarded(session); // VEGA onboarding: first station visit
        _log.Info($"Player '{session.State.Name}' boarded station '{station.Name}' (own world '{stationLoc}').");
        CheckpointSave($"docked at {station.Name}"); // auto-save when docking a station
    }

    /// <summary>Leaves a boarded station and undocks straight back into <b>space flight</b> around the
    /// planet the station orbits — you return to your ship's view, not to the planet surface. The planet
    /// world is restored underneath (so a later landing drops you there), then the player relaunches into
    /// the system's space instance near where they docked.</summary>
    public void LeaveStation(string playerId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null || !_boardedStation.Remove(playerId))
        {
            return;
        }

        string stationLoc = session.CurrentLocationId; // the station world being left
        var (returnLoc, returnType) = _boardedReturn.TryGetValue(playerId, out var r)
            ? r
            : (_meta.ActiveLocationId, _meta.DefaultPlanetType);
        _boardedReturn.Remove(playerId);

        // Restore the planet world the station orbits (the proven arrival path) and place the player at
        // their ship, so that if they later land from space they touch down here.
        LoadWorld(returnType, returnLoc);
        SetCurrent(session);
        if (_config.PlaceStarterShip)
        {
            StampShip();
        }

        session.CurrentLocationId = returnLoc;
        session.State.Position = _shipStamped ? _healTank : session.State.RespawnPoint;
        session.State.AboardShip = true;
        session.SentChunks.Clear();

        var (systemName, planetName) = ActiveLocationNames();
        Send(session, new WorldReset { PlanetType = returnType, PlanetName = planetName, SystemName = systemName, Hyperjump = false });
        SendPlayerState(session);
        SendShipPlacement(session);
        SendShipStations(session);
        SendPlanetPois(session);
        SendEnvironment(session);
        SendCreatures(session);
        SendContainers(session);
        SendNpcs(session);
        SendInventory(session);

        // Drop the now-empty station world from memory (its structure is persisted, NPCs re-spawn next visit).
        if (!OccupiedLocations().Contains(stationLoc))
        {
            _worlds.Unload(stationLoc);
        }

        // Undock into space flight. If you docked while on an EVA, your ship stayed floating where it was —
        // return you to the float next to it (no take-off); otherwise relaunch the ship as before.
        bool fromEva = _dockedFromEva.Remove(playerId, out var floatShipPos);
        EnterSpace(playerId, skipLaunch: fromEva);
        if (fromEva && _playerInstance.TryGetValue(playerId, out var iid) && _spaceInstances.TryGetValue(iid, out var inst))
        {
            inst.ShipPosition = floatShipPos;
            inst.ShipLastPosition = floatShipPos;
            session.State.InEva = true; // back outside the ship, floating where you left it
            SendPlayerState(session);
        }

        Send(session, new ServerMessage { Text = fromEva ? "Back outside — your ship waited in space." : "Undocked into space." });
    }

    private void StampStation(BoardableStation station)
    {
        if (station.Stamped)
        {
            return;
        }

        long sSeed = _meta.Seed ^ WorldGenerator.StableHash("station:" + station.Id);

        // Chance to stamp a hand-designed template from the pool (when one exists) instead of generating.
        // The roll uses its own RNG so it never disturbs the procedural generator's determinism.
        StationStructure structure;
        var pool = _content.StationTemplates;
        var roll = new System.Random(unchecked((int)(sSeed ^ (sSeed >> 32))));
        if (pool.Count > 0 && roll.NextDouble() < StructureTemplateChance)
        {
            structure = StationGenerator.FromTemplate(pool[roll.Next(pool.Count)], _content);
        }
        else
        {
            structure = StationGenerator.Generate(station.SizeTier, sSeed, _content);
        }

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
        foreach (var m in structure.Markers)
        {
            var pos = new Vector3f(station.Origin.X + m.LocalPos.X + 0.5f,
                station.Origin.Y + m.LocalPos.Y + 0.5f,
                station.Origin.Z + m.LocalPos.Z + 0.5f);
            station.Markers.Add((m.Type, pos));
        }

        // Arrive in the enclosed central hub (solid floor, no open wall), NOT the hangar — the hangar's
        // -Z wall is opened to space, so spawning there can drop the player out into the void.
        var spawnMarker = station.Markers.FirstOrDefault(m => m.Type == "spawn");
        if (spawnMarker.Pos.Equals(Vector3f.Zero))
        {
            spawnMarker = station.Markers.FirstOrDefault(m => m.Type == "vendor" || m.Type == "heal_tank" || m.Type == "quarters");
        }

        station.Spawn = !spawnMarker.Pos.Equals(Vector3f.Zero)
            ? spawnMarker.Pos
            : new Vector3f(station.Origin.X + 3.5f, station.Origin.Y + 2f, station.Origin.Z + 3.5f);

        // Guarantee solid ground + headroom at the spawn so the player can never fall straight through the
        // station into the void (some markers sit in an open bay). A small hull pad does the job.
        var hull = _content.GetBlock("iron_wall")?.NumericId ?? BlockId.Air;
        if (!hull.IsAir)
        {
            var sp = station.Spawn.ToBlock();
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                _world.SetBlock(new Vector3i(sp.X + dx, sp.Y - 1, sp.Z + dz), hull); // floor pad
            }

            _world.SetBlock(sp, BlockId.Air);                                   // stand-in space
            _world.SetBlock(new Vector3i(sp.X, sp.Y + 1, sp.Z), BlockId.Air);   // headroom
        }

        // The station's mission board offers an endless rolling set of jobs: seed the first window now; the
        // per-player mission-giver window (item 13) slides it so it never runs dry.
        if (station.Markers.Any(m => m.Type == "mission_board"))
        {
            string prefix = $"station_{(uint)WorldGenerator.StableHash(station.Id) % 100000u}_";
            StockBoard(prefix, station.Id, _stationMissionIds, CoinGiverName(station.Id));
        }

        // NPCs are runtime (cleared when the station world reloads), so the caller (BoardStation) spawns
        // them per visit rather than here in the one-time structure stamp.
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
        int vendorIndex = 0;
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

            // Each station vendor gets its own profession (B55) so multiple traders on one station sell different
            // goods; other crew stay "traders"-themed. The first vendor keeps the station's "traders" identity.
            string npcTheme = role == "vendor" ? VendorThemeFor(station.Id, vendorIndex++, "traders") : "traders";
            bool robotic = npcTheme == "researchers"; // research staff are service androids

            // Markers sit centred in the air cell above the floor (+0.5); drop the NPC's feet onto the
            // floor surface (the integer Y) so the crew stands on the deck instead of floating over it.
            var standing = new Vector3f(pos.X, (float)System.Math.Floor(pos.Y), pos.Z);
            var npc = MakeNpc(role, npcTheme, robotic, standing, rng);
            if (role == "quartermaster")
            {
                npc.Name = CoinGiverName(station.Id); // the mission-giver's name matches its missions (item 13)
            }

            _npcs.Add(npc);
            added++;
        }

        // Extra wandering civilians/dockhands so the station feels populated — scaled by size, some are
        // maintenance androids, with a little size variation. Placed near existing markers (valid rooms).
        int extra = station.SizeTier switch { "small" => 2, "large" => 7, "huge" => 11, _ => 4 };
        var spots = station.Markers.Select(m => m.Pos).ToList();
        for (int i = 0; i < extra && spots.Count > 0; i++)
        {
            var b = spots[rng.Next(spots.Count)];
            var home = new Vector3f(b.X + (float)(rng.NextDouble() * 4 - 2), (float)System.Math.Floor(b.Y), b.Z + (float)(rng.NextDouble() * 4 - 2));
            bool robot = rng.NextDouble() < 0.3; // ~30% androids
            var npc = MakeNpc("settler", "traders", robot, home, rng);
            npc.Size = 0.9f + (float)rng.NextDouble() * 0.22f;
            _npcs.Add(npc);
            added++;
        }

        if (added > 0)
        {
            _log.Info($"Spawned {added} crew NPCs at station '{station.Name}'.");
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
