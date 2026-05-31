using Spacecraft.Networking.Messages;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Primitives;

namespace Spacecraft.GameServer;

/// <summary>
/// The ship as a physical, enterable place (technical requirements / `anf_space_flight.md`;
/// see `docs/CLIENT_COMPLETION_PLAN.md` M23a). The server stamps a small hollow ship hull out
/// of blocks at the start landing zone, so it renders and is walkable like any other terrain.
/// Whether a player is "aboard" is derived authoritatively from standing inside the hull, which
/// is what already gates cargo crafting, module building and oxygen regeneration.
///
/// MVP scope: one shared ship anchored at the first landing zone (singleplayer-focused);
/// per-player ships and an explicit "press E to enter" / lift-off come later.
/// </summary>
public sealed partial class GameServer
{
    // Hull half-extents, derived from the active ship design in StampShip (defaults = starter).
    private int _shipHalfX = 2;
    private int _shipHeight = 4;
    private int _shipHalfZ = 3;

    private const float ShipStationReach = 3f;

    private Vector3i _shipAnchor;
    private Vector3f _healTank;
    private bool _shipStamped;
    private readonly List<(string Type, Vector3f Pos)> _stations = new();

    /// <summary>The medbay heal-tank position inside the ship (respawn point), if a ship is placed.</summary>
    public Vector3f HealTank => _healTank;
    public bool HasShip => _shipStamped;

    /// <summary>Writes the ship hull into the world at the start landing zone (idempotent enough to self-heal).</summary>
    private void StampShip()
    {
        int cx = 0, cz = 0;
        foreach (var zone in _landingZones.Values)
        {
            cx = zone.CenterX;
            cz = zone.CenterZ;
            break; // anchor at the first landing zone
        }

        int y0 = _generator.SurfaceHeight(_world.Planet, cx, cz);
        _shipAnchor = new Vector3i(cx, y0, cz);

        // Hull size from the active ship's design (data/ships.json), falling back to the starter.
        var design = _content.GetShip(_ship.ShipType) ?? _content.GetShip("starter");
        if (design != null)
        {
            _shipHalfX = System.Math.Max(2, design.InteriorWidth / 2);
            _shipHalfZ = System.Math.Max(2, design.InteriorLength / 2);
            _shipHeight = System.Math.Max(3, design.Height);
        }

        var wall = _content.GetBlock("iron_wall")?.NumericId ?? BlockId.Air;
        var glass = _content.GetBlock("glass")?.NumericId ?? wall;
        if (wall.IsAir)
        {
            _log.Info("Ship hull not placed: 'iron_wall' block missing from content.");
            return;
        }

        for (int x = cx - _shipHalfX; x <= cx + _shipHalfX; x++)
        for (int y = y0; y <= y0 + _shipHeight; y++)
        for (int z = cz - _shipHalfZ; z <= cz + _shipHalfZ; z++)
        {
            var pos = new Vector3i(x, y, z);

            bool shell = x == cx - _shipHalfX || x == cx + _shipHalfX
                         || y == y0 || y == y0 + _shipHeight
                         || z == cz - _shipHalfZ || z == cz + _shipHalfZ;

            if (!shell)
            {
                _world.SetBlock(pos, BlockId.Air); // hollow interior
                continue;
            }

            // Door: a 1-wide, 2-tall gap in the -Z wall, centred on x.
            bool door = z == cz - _shipHalfZ && x == cx && (y == y0 + 1 || y == y0 + 2);
            if (door)
            {
                _world.SetBlock(pos, BlockId.Air);
                continue;
            }

            // Glass viewport: the +Z wall's middle row (excluding the corners).
            bool viewport = z == cz + _shipHalfZ && y == y0 + 2 && x > cx - _shipHalfX && x < cx + _shipHalfX;
            _world.SetBlock(pos, viewport ? glass : wall);
        }

        // Interior station markers on the floor. The whole hull is mining-protected, so these
        // stay put. The logical stations also exist as ship modules (workshop gates crafting,
        // medbay = respawn, cargo = shared hold); these tiles add interaction points.
        int floor = y0 + 1;
        int dx = _shipHalfX - 1, dz = _shipHalfZ - 1; // keep stations inside the walls
        _stations.Clear();
        AddStation("medbay", cx - dx, floor, cz - dz, "ice");          // heal-tank (heal + respawn)
        AddStation("cockpit", cx, floor, cz + dz, "data_cache");       // star map / travel
        AddStation("workshop", cx + dx, floor, cz, "stone");           // crafting bench
        AddStation("cargo", cx - dx, floor, cz, "iron_wall");          // shared cargo hold
        AddStation("quarters", cx + dx, floor, cz - dz, "carbon");     // sleep / set respawn

        // Respawn at an open tile in the middle of the ship (next to the heal-tank).
        _healTank = new Vector3f(cx + 0.5f, y0 + 2f, cz + 0.5f);

        _shipStamped = true;
        _log.Info($"Ship hull placed at ({cx}, {y0}, {cz}) with {_stations.Count} stations.");
    }

    private void AddStation(string type, int x, int y, int z, string blockKey)
    {
        if (_content.GetBlock(blockKey) is { } def)
        {
            _world.SetBlock(new Vector3i(x, y, z), def.NumericId);
        }

        _stations.Add((type, new Vector3f(x + 0.5f, y, z + 0.5f)));
    }

    private void SendShipStations(PlayerSession session)
    {
        if (!_shipStamped)
        {
            return;
        }

        Send(session, new ShipStations
        {
            Stations = _stations.Select(s => new NetShipStation
            {
                Type = s.Type, X = s.Pos.X, Y = s.Pos.Y, Z = s.Pos.Z,
            }).ToArray(),
        });
    }

    /// <summary>Test/diagnostic: the world position of a ship station, or null if absent.</summary>
    public Vector3f? StationPosition(string type)
    {
        foreach (var s in _stations)
        {
            if (s.Type == type)
            {
                return s.Pos;
            }
        }

        return null;
    }

    /// <summary>Handles using a ship station the player is standing next to (server-authoritative).</summary>
    public void UseStation(string playerId, string station)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return;
        }

        var p = session.State;
        var pos = StationPosition(station);
        if (pos is null)
        {
            Reject(session, "station", "No such station.");
            return;
        }

        if (!p.AboardShip || p.Position.DistanceSquared(pos.Value) > ShipStationReach * ShipStationReach)
        {
            Reject(session, "station", "Too far from the station.");
            return;
        }

        switch (station)
        {
            case "medbay":
                if (!_ship.HasModule("medbay"))
                {
                    Send(session, new ServerMessage { Text = "No medbay module aboard." });
                    return;
                }

                p.Health = 100f;
                p.Oxygen = 100f;
                p.SuitEnergy = 100f;
                SendPlayerState(session);
                Send(session, new ServerMessage { Text = "Healed at the medbay heal-tank." });
                break;

            case "quarters":
                p.RespawnPoint = pos.Value;
                Send(session, new ServerMessage { Text = "Respawn point set to your quarters." });
                break;

            case "workshop":
                Send(session, new ServerMessage { Text = "Workshop ready — open the menu (Tab) to craft." });
                break;

            case "cargo":
                Send(session, new ServerMessage { Text = "Cargo hold — open the menu (Tab) to manage it." });
                break;

            case "cockpit":
                Send(session, new ServerMessage { Text = "Cockpit — star map travel is coming soon." });
                break;
        }
    }

    private void HandleUseStation(PlayerSession session, UseStationIntent intent)
        => UseStation(session.State.PlayerId, intent.Station);

    /// <summary>True if the cell is part of the (indestructible) ship hull/interior fittings.</summary>
    private bool IsShipBlock(Vector3i p)
    {
        if (!_shipStamped)
        {
            return false;
        }

        int cx = _shipAnchor.X, y0 = _shipAnchor.Y, cz = _shipAnchor.Z;
        return p.X >= cx - _shipHalfX && p.X <= cx + _shipHalfX
               && p.Y >= y0 && p.Y <= y0 + _shipHeight
               && p.Z >= cz - _shipHalfZ && p.Z <= cz + _shipHalfZ;
    }

    /// <summary>Test/diagnostic accessor: whether a world cell is protected ship structure.</summary>
    public bool IsProtectedShipBlock(int x, int y, int z) => IsShipBlock(new Vector3i(x, y, z));

    /// <summary>The ship hull anchor (floor centre) block, or default if no ship is placed.</summary>
    public Vector3i ShipAnchorBlock => _shipAnchor;

    /// <summary>True if the position is within the ship hull's bounding box.</summary>
    private bool ShipInteriorContains(Vector3f p)
    {
        if (!_shipStamped)
        {
            return false;
        }

        int cx = _shipAnchor.X, y0 = _shipAnchor.Y, cz = _shipAnchor.Z;
        return p.X >= cx - _shipHalfX && p.X <= cx + _shipHalfX
               && p.Y >= y0 && p.Y <= y0 + _shipHeight
               && p.Z >= cz - _shipHalfZ && p.Z <= cz + _shipHalfZ;
    }

    /// <summary>Tells the client where the ship stands so it can show a compass/minimap to it.</summary>
    private void SendShipPlacement(PlayerSession session)
    {
        if (_shipStamped)
        {
            Send(session, new ShipPlacement { X = _shipAnchor.X + 0.5f, Y = _shipAnchor.Y, Z = _shipAnchor.Z + 0.5f });
        }
    }

    /// <summary>Derives the player's aboard state from their position; resends inventory/state on change.</summary>
    private void UpdateAboard(PlayerSession session)
    {
        if (!_shipStamped)
        {
            return; // no physical ship → keep the default aboard semantics (e.g. tests, ship disabled)
        }

        bool aboard = ShipInteriorContains(session.State.Position);
        if (aboard != session.State.AboardShip)
        {
            session.State.AboardShip = aboard;
            SendInventory(session);   // cargo is only included while aboard
            SendPlayerState(session);
        }
    }
}
