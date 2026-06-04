using Spacecraft.Networking.Messages;
using Spacecraft.Shared.Definitions;
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

    private Vector3i _shipAnchor { get => _worlds.Active.ShipAnchor; set => _worlds.Active.ShipAnchor = value; }
    private Vector3f _healTank { get => _worlds.Active.HealTank; set => _worlds.Active.HealTank = value; }
    private bool _shipStamped { get => _worlds.Active.ShipStamped; set => _worlds.Active.ShipStamped = value; }
    private bool _shipIsLayout { get => _worlds.Active.ShipIsLayout; set => _worlds.Active.ShipIsLayout = value; } // stamped from a designed voxel layout
    private List<(string Type, Vector3f Pos)> _stations => _worlds.Active.Stations;

    // Exterior cosmetic blocks (wings, rear engine nozzles, cockpit canopy) that give the landed
    // hull the space-ship silhouette. Tracked so they count as protected ship structure, but they
    // are outside the interior box so they don't affect the "aboard" check.
    private HashSet<Vector3i> _shipExtra => _worlds.Active.ShipExtra;

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

        // A designed ship (ship editor) carries a voxel layout — stamp that exact shape, not the box.
        var layout = _content.GetShipLayout(design?.Layout);
        if (layout != null && layout.Cells.Count > 0)
        {
            StampShipLayout(cx, cz, y0, layout);
            return;
        }

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

            // Hatch: a 2-wide, 2-tall gap in the -Z wall, centred on x (a hatch is never single-block).
            bool door = z == cz - _shipHalfZ && (x == cx || x == cx - 1) && (y == y0 + 1 || y == y0 + 2);
            if (door)
            {
                _world.SetBlock(pos, BlockId.Air);
                continue;
            }

            // Glass window panes: a band at eye height (y0+2) along the front (+Z) and both side
            // walls (excluding the corners), so the cabin has proper windows to see out of.
            bool frontWin = z == cz + _shipHalfZ && y == y0 + 2 && x > cx - _shipHalfX && x < cx + _shipHalfX;
            bool sideWin = (x == cx - _shipHalfX || x == cx + _shipHalfX) && y == y0 + 2 && z > cz - _shipHalfZ && z < cz + _shipHalfZ;
            _world.SetBlock(pos, frontWin || sideWin ? glass : wall);
        }

        // Ceiling lights: a row of emissive panels down the centre of the roof so the cabin glows at
        // night (the client renders data_cache as an emissive block). They stay solid hull (protected).
        var lightBlock = _content.GetBlock("data_cache")?.NumericId ?? glass;
        int ceiling = y0 + _shipHeight;
        for (int z = cz - _shipHalfZ + 1; z <= cz + _shipHalfZ - 1; z += 2)
        {
            _world.SetBlock(new Vector3i(cx, ceiling, z), lightBlock);
        }

        // Exterior silhouette so the landed ship reads like the space ship from outside: side wings,
        // rear engine nozzles (dark = engines off) and a raised glass cockpit canopy at the front.
        // The front -Z door stays the open hatch. These sit outside the interior box.
        var dark = _content.GetBlock("carbon")?.NumericId
                   ?? _content.GetBlock("basalt")?.NumericId ?? wall;
        _shipExtra.Clear();

        void Ext(int x, int y, int z, BlockId id)
        {
            var p = new Vector3i(x, y, z);
            _world.SetBlock(p, id);
            _shipExtra.Add(p);
        }

        int wingY = y0 + System.Math.Max(1, _shipHeight / 2);   // wings at hull mid-height
        for (int s = -1; s <= 1; s += 2)                        // both sides
        {
            for (int w = 1; w <= 2; w++)                        // span outward
            {
                for (int zc = cz - 1; zc <= cz + 1; zc++)       // wing chord (centred)
                {
                    Ext(cx + s * (_shipHalfX + w), wingY, zc, wall);
                }
            }

            // Rear engine nozzle (dark, off), paired near the back centre.
            for (int d = 1; d <= 2; d++)
            {
                Ext(cx + s, y0 + 1, cz - _shipHalfZ - d, dark);
            }
        }

        // Cockpit canopy: a small raised glass bump on top, toward the front.
        Ext(cx, y0 + _shipHeight + 1, cz + _shipHalfZ - 1, glass);
        Ext(cx, y0 + _shipHeight + 1, cz + _shipHalfZ - 2, glass);

        // Exterior lighting (emissive): navigation position lights — red to port (-X), green to
        // starboard (+X) at the wingtips — white front headlights below the cockpit, and a tail light.
        var lightW = _content.GetBlock("light_white")?.NumericId ?? glass;
        var lightR = _content.GetBlock("light_red")?.NumericId ?? lightW;
        var lightG = _content.GetBlock("light_green")?.NumericId ?? lightW;
        Ext(cx - (_shipHalfX + 2), wingY, cz, lightR);                 // port (red)
        Ext(cx + (_shipHalfX + 2), wingY, cz, lightG);                 // starboard (green)
        Ext(cx - 1, y0 + 1, cz + _shipHalfZ + 1, lightW);             // front headlight
        Ext(cx + 1, y0 + 1, cz + _shipHalfZ + 1, lightW);             // front headlight
        Ext(cx, y0 + _shipHeight, cz - _shipHalfZ, lightW);          // tail light

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
        AddStation("lab", cx - dx, floor, cz + dz, "data_cache");      // research console (Tech menu)
        AddStation("console", cx + dx, floor, cz + dz, "data_cache");  // ship-expansion console (Ship menu)

        // Respawn at an open tile in the middle of the ship (next to the heal-tank).
        _healTank = new Vector3f(cx + 0.5f, y0 + 2f, cz + 0.5f);

        _shipStamped = true;
        _log.Info($"Ship hull placed at ({cx}, {y0}, {cz}) with {_stations.Count} stations.");
    }

    /// <summary>Stamps a designed voxel ship (from the ship editor) centred on the landing zone: places
    /// each cell's hull/glass/light/engine block, opens hatch cells, and registers station markers.</summary>
    private void StampShipLayout(int cx, int cz, int y0, ShipLayout layout)
    {
        var wall = _content.GetBlock("iron_wall")?.NumericId ?? BlockId.Air;
        var glass = _content.GetBlock("glass")?.NumericId ?? wall;
        var dark = _content.GetBlock("carbon")?.NumericId ?? _content.GetBlock("basalt")?.NumericId ?? wall;
        var lightW = _content.GetBlock("light_white")?.NumericId ?? glass;
        var lightR = _content.GetBlock("light_red")?.NumericId ?? lightW;
        var lightG = _content.GetBlock("light_green")?.NumericId ?? lightW;
        if (wall.IsAir)
        {
            _log.Info("Designed ship not placed: 'iron_wall' block missing from content.");
            return;
        }

        int ox = cx - layout.Width / 2;
        int oz = cz - layout.Length / 2;

        _shipHalfX = System.Math.Max(2, layout.Width / 2);
        _shipHalfZ = System.Math.Max(2, layout.Length / 2);
        _shipHeight = System.Math.Max(3, layout.Height);
        _shipExtra.Clear();
        _stations.Clear();
        _shipIsLayout = true;
        Vector3f? medbay = null;

        // Clear the ship's bounding box to air first — removes any terrain/flora that generated inside
        // the footprint, so nothing intrudes through the hull; the layout cells are stamped on top.
        for (int bx = 0; bx < layout.Width; bx++)
        for (int by = 0; by <= _shipHeight; by++)
        for (int bz = 0; bz < layout.Length; bz++)
        {
            _world.SetBlock(new Vector3i(ox + bx, y0 + by, oz + bz), BlockId.Air);
        }

        foreach (var cell in layout.Cells)
        {
            int wx = ox + cell.X, wy = y0 + cell.Y, wz = oz + cell.Z;
            var p = new Vector3i(wx, wy, wz);

            switch (cell.Id)
            {
                case "hatch":
                    _world.SetBlock(p, BlockId.Air);                                 // the entry opening
                    _world.SetBlock(new Vector3i(wx, wy + 1, wz), BlockId.Air);      // a hatch is never single-block: always ≥2 tall
                    continue;
                case "glass":
                    _world.SetBlock(p, glass);
                    _shipExtra.Add(p);
                    continue;
                case "light":
                case "headlight":
                    _world.SetBlock(p, lightW);
                    _shipExtra.Add(p);
                    continue;
                case "light_red":
                    _world.SetBlock(p, lightR);
                    _shipExtra.Add(p);
                    continue;
                case "light_green":
                    _world.SetBlock(p, lightG);
                    _shipExtra.Add(p);
                    continue;
                case "engine":
                    _world.SetBlock(p, dark);
                    _shipExtra.Add(p);
                    continue;
            }

            if (cell.Kind == "station")
            {
                AddStation(cell.Id, wx, wy, wz, StationBlockKey(cell.Id));
                _shipExtra.Add(p);
                if (cell.Id == "medbay")
                {
                    medbay = new Vector3f(wx + 0.5f, wy + 1f, wz + 0.5f);
                }

                continue;
            }

            // Hull (iron_wall) and anything unknown → solid hull.
            _world.SetBlock(p, wall);
            _shipExtra.Add(p);
        }

        // Guarantee a flush, solid floor across the footprint (fills layout gaps + the cleared terrain)
        // so the player never falls through; the pad counts as protected hull.
        for (int fx = 0; fx < layout.Width; fx++)
        for (int fz = 0; fz < layout.Length; fz++)
        {
            var fp = new Vector3i(ox + fx, y0, oz + fz);
            if (_world.GetBlock(fp).IsAir)
            {
                _world.SetBlock(fp, wall);
                _shipExtra.Add(fp);
            }
        }

        _shipAnchor = new Vector3i(cx, y0, cz);
        _healTank = medbay ?? new Vector3f(cx + 0.5f, y0 + 2f, cz + 0.5f);
        _shipStamped = true;
        _log.Info($"Designed ship '{layout.Key}' stamped at ({cx}, {y0}, {cz}) — {layout.Cells.Count} cells, {_stations.Count} stations.");
    }

    private static string StationBlockKey(string station) => station switch
    {
        "medbay" => "ice",
        "cockpit" => "data_cache",
        "workshop" => "stone",
        "quarters" => "carbon",
        _ => "iron_wall",
    };

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
                Send(session, new ServerMessage { Text = "Cockpit — open the menu (Tab) → Map to travel to another planet." });
                SendStarMap(session);
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

        // A designed (layout) ship protects exactly its placed cells (its shape is irregular); the
        // parametric box ship protects its whole hull box plus the exterior silhouette cells.
        if (_shipIsLayout)
        {
            return _shipExtra.Contains(p);
        }

        int cx = _shipAnchor.X, y0 = _shipAnchor.Y, cz = _shipAnchor.Z;
        bool inHull = p.X >= cx - _shipHalfX && p.X <= cx + _shipHalfX
               && p.Y >= y0 && p.Y <= y0 + _shipHeight
               && p.Z >= cz - _shipHalfZ && p.Z <= cz + _shipHalfZ;
        return inHull || _shipExtra.Contains(p);
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

    /// <summary>
    /// True if a position is inside (or right against) the ship hull, so creatures and settlement
    /// NPCs can be kept from walking into the player's ship. Uses a small margin around the hull
    /// footprint + height; positions above the hull (flying creatures) are allowed to pass over.
    /// </summary>
    private bool EntityBlockedByShip(Vector3f p)
    {
        if (!_shipStamped)
        {
            return false;
        }

        const float m = 0.5f;
        int cx = _shipAnchor.X, y0 = _shipAnchor.Y, cz = _shipAnchor.Z;
        return p.X >= cx - _shipHalfX - m && p.X <= cx + _shipHalfX + m
               && p.Y >= y0 - m && p.Y <= y0 + _shipHeight + m
               && p.Z >= cz - _shipHalfZ - m && p.Z <= cz + _shipHalfZ + m;
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
