using Spacecraft.Networking.Messages;
using Spacecraft.Shared.Definitions;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Primitives;
using Spacecraft.Shared.World;

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
    private const float ShipStationReach = 3f;

    /// <summary>The ship-stamp record of the player currently being served (the ship cursor). Each player
    /// has their own ship in their world; per-player ops (stamp, heal-tank, stations) use this one.</summary>
    private ShipStamp CurStamp => _worlds.Active.StampFor(_current?.State.PlayerId ?? string.Empty);

    // Hull half-extents, derived from the active ship design in StampShip (defaults = starter).
    private int _shipHalfX { get => CurStamp.HalfX; set => CurStamp.HalfX = value; }
    private int _shipHeight { get => CurStamp.Height; set => CurStamp.Height = value; }
    private int _shipHalfZ { get => CurStamp.HalfZ; set => CurStamp.HalfZ = value; }

    private Vector3i _shipAnchor { get => CurStamp.Anchor; set => CurStamp.Anchor = value; }
    private Vector3f _healTank { get => CurStamp.HealTank; set => CurStamp.HealTank = value; }
    private bool _shipStamped { get => CurStamp.Stamped; set => CurStamp.Stamped = value; }
    private bool _shipIsLayout { get => CurStamp.IsLayout; set => CurStamp.IsLayout = value; } // designed voxel layout
    private List<(string Type, Vector3f Pos)> _stations => CurStamp.Stations;

    // Exterior cosmetic blocks (wings, rear engine nozzles, cockpit canopy) that give the landed
    // hull the space-ship silhouette. Tracked so they count as protected ship structure, but they
    // are outside the interior box so they don't affect the "aboard" check.
    private HashSet<Vector3i> _shipExtra => CurStamp.Extra;

    /// <summary>The medbay heal-tank position inside the ship (respawn point), if a ship is placed.</summary>
    public Vector3f HealTank => _healTank;
    public bool HasShip => _shipStamped;

    /// <summary>Writes the ship hull into the world at the start landing zone (idempotent enough to self-heal).</summary>
    private void StampShip()
    {
        // Anchor at the SERVED player's own landing pad, so two players on one body each get their own ship at
        // their own pad (never a shared one). The pad is whichever the player claimed when landing (item 38).
        var pad = _current != null ? PlayerPad(_current) : null;
        int cx = pad?.CenterX ?? 0, cz = pad?.CenterZ ?? 0;

        int y0 = _generator.SurfaceHeight(_world.Planet, cx, cz);
        _shipAnchor = new Vector3i(cx, y0, cz);

        // Hull size from the active ship's design (data/ships.json), falling back to the starter.
        var design = _content.GetShip(_ship.ShipType) ?? _content.GetShip("starter");

        // A designed ship (ship editor) carries a voxel layout — stamp that exact shape, not the box.
        var layout = _content.GetShipLayout(design?.Layout);
        if (layout != null && layout.Cells.Count > 0)
        {
            StampShipLayout(cx, cz, y0, layout);
            RegisterDoors(); // pick up any door cells the designed ship carries
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

        ClearShipKeepOut(cx, cz, _shipHalfX, _shipHalfZ, y0, _shipHeight); // B31: no trees on/through the hull

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

            // Hatch: a 3-wide, 2-tall gap in the -Z wall, centred on the ship (cells cx-1..cx+1 are symmetric
            // about the hull centre cx+0.5, unlike the old 2-wide gap which sat half a block off-centre — item 35).
            bool door = z == cz - _shipHalfZ && (x == cx - 1 || x == cx || x == cx + 1) && (y == y0 + 1 || y == y0 + 2);
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

        FillShipFoundation(cx - _shipHalfX, cz - _shipHalfZ, 2 * _shipHalfX + 1, 2 * _shipHalfZ + 1, y0);

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
            _shipExtra.Add(WorldConstants.CanonicalBlock(p)); // canonical so protection matches after longitude wrap
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

            // Rear engine nozzles (dark, off), outboard of the centre hatch so they never block the way out
            // (the rear -Z hatch occupies the three centre columns cx-1..cx+1; engines sit at the rear corners).
            for (int d = 1; d <= 2; d++)
            {
                Ext(cx + s * _shipHalfX, y0 + 1, cz - _shipHalfZ - d, dark);
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

        // Flush rear doorstep: the hull is a flat box anchored at the centre's surface height, so on sloped
        // ground the rear hatch can otherwise hang in the air (or get buried). Carve a short, level pad out
        // the hatch — a solid threshold at cabin-floor level, the doorway cleared above it, and a foundation
        // under it — so the door always meets solid, walkable ground regardless of the terrain it landed on.
        var stepFill = _content.GetBlock("stone")?.NumericId ?? wall;
        for (int d = 1; d <= 2; d++)               // two blocks out from the rear wall
        for (int sx = cx - 1; sx <= cx + 1; sx++)  // the three hatch columns (centred on cx)
        {
            int sz = cz - _shipHalfZ - d;
            Ext(sx, y0, sz, wall);               // flush threshold at floor level (protected like the hull)
            for (int sy = y0 + 1; sy <= y0 + _shipHeight; sy++)
            {
                _world.SetBlock(new Vector3i(sx, sy, sz), BlockId.Air); // clear any rise/flora from the doorway
            }

            for (int dy = 1; dy <= ShipFoundationDepth; dy++)           // support the pad over a dip
            {
                var fp = new Vector3i(sx, y0 - dy, sz);
                if (_world.GetBlock(fp).IsAir)
                {
                    Ext(sx, y0 - dy, sz, stepFill); // protected like the hull (B51: was SetBlock → mineable)
                }
            }
        }

        // An energy door fills the hatch (the 3-wide opening in the -Z wall) so the box ship has a real door
        // like designed ships, not just an open hole. The marker sits in the centre column (cx) so the door
        // panel + field centre on the hull (item 35).
        CurStamp.Doors.Clear();
        CurStamp.Doors.Add(new Vector3f(cx + 0.5f, y0 + 1f, cz - _shipHalfZ + 0.5f));

        _shipStamped = true;
        _log.Info($"Ship hull placed at ({cx}, {y0}, {cz}) with {_stations.Count} stations.");
        RegisterDoors(); // pick up the hatch door (+ keep settlement/other-ship doors in sync)
    }

    /// <summary>Carves the currently-stamped ship back out of the world (hull box + silhouette extras), so a
    /// different design can be stamped on the same pad with no leftover hull from the old one. Used when the
    /// player switches the active ship while landed — the new ship may be smaller or shaped differently.</summary>
    private void ClearStampedShip()
    {
        if (!_shipStamped)
        {
            return;
        }

        var a = _shipAnchor;
        for (int x = a.X - _shipHalfX; x <= a.X + _shipHalfX; x++)
        for (int y = a.Y; y <= a.Y + _shipHeight + 2; y++) // +2 covers the raised cockpit canopy on top
        for (int z = a.Z - _shipHalfZ; z <= a.Z + _shipHalfZ; z++)
        {
            _world.SetBlock(new Vector3i(x, y, z), BlockId.Air);
        }

        foreach (var c in _shipExtra)
        {
            _world.SetBlock(c, BlockId.Air); // wings, engine nozzles, nav lights, canopy outside the box
        }

        _shipExtra.Clear();
        _stations.Clear();
        CurStamp.Doors.Clear();
        _shipStamped = false;
    }

    /// <summary>Forgets the world chunks the ship's footprint occupies on this session so <see cref="StreamChunks"/>
    /// re-sends them next tick — used after re-stamping a switched ship while landed, so the client swaps the old
    /// hull for the new one without a full world reload.</summary>
    private void RestreamShipChunks(PlayerSession session)
    {
        var a = _shipAnchor;
        var seen = new HashSet<ChunkCoord>();
        for (int x = a.X - _shipHalfX - 3; x <= a.X + _shipHalfX + 3; x++)
        for (int y = a.Y - ShipFoundationDepth - 1; y <= a.Y + _shipHeight + 3; y++)
        for (int z = a.Z - _shipHalfZ - 3; z <= a.Z + _shipHalfZ + 3; z++)
        {
            var coord = WorldConstants.CanonicalChunk(WorldConstants.WorldToChunk(new Vector3i(x, y, z)), _world.Circumference);
            if (seen.Add(coord))
            {
                session.SentChunks.Remove(coord);
            }
        }
    }

    /// <summary>Stamps a designed voxel ship (from the ship editor) centred on the landing zone: places
    /// each cell's hull/glass/light/engine block, opens hatch cells, and registers station markers.</summary>
    /// <summary>Clears tree/flora blocks in — and just around and above — the ship's footprint before the hull
    /// is stamped, so vegetation world-gen grew there can't stand on the roof or poke through the hull (B31).
    /// The horizontal margin catches tree crowns whose trunk sits just outside the hull; the headroom catches a
    /// tall trunk/crown left above the roof. Only vegetation (flora_* / wood_log / tree_leaves, via
    /// <see cref="IsFlammable"/>) is removed — terrain and the hull stay. Runs at world-load time before the
    /// player's chunks stream, so (like the hull stamp itself) it needs no BlockChanged broadcast.</summary>
    private void ClearShipKeepOut(int cx, int cz, int halfX, int halfZ, int y0, int height)
    {
        const int margin = 3;    // crown reach beyond the hull
        const int headroom = 9;  // tallest trunk + crown that could sit above the roof
        int yTop = y0 + System.Math.Max(0, height) + headroom;
        for (int x = cx - halfX - margin; x <= cx + halfX + margin; x++)
        for (int z = cz - halfZ - margin; z <= cz + halfZ + margin; z++)
        for (int y = y0; y <= yTop; y++)
        {
            var pos = new Vector3i(x, y, z);
            if (IsFlammable(_world.GetBlock(pos).Value))
            {
                _world.SetBlock(pos, BlockId.Air);
            }
        }
    }

    /// <summary>Test hook: run the ship keep-out clear directly (B31).</summary>
    public void ClearShipKeepOutForTest(int cx, int cz, int halfX, int halfZ, int y0, int height)
        => ClearShipKeepOut(cx, cz, halfX, halfZ, y0, height);

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
        CurStamp.Doors.Clear();
        _shipIsLayout = true;
        Vector3f? medbay = null;

        ClearShipKeepOut(cx, cz, _shipHalfX, _shipHalfZ, y0, _shipHeight); // B31: clear trees above + around the hull

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
                case "door_slide":
                case "door_hinge":
                    // A real door fills this opening: clear it (3 tall) so the panel shows + the player fits,
                    // and record the doorway so RegisterDoors builds a server-authoritative door here.
                    _world.SetBlock(p, BlockId.Air);
                    _world.SetBlock(new Vector3i(wx, wy + 1, wz), BlockId.Air);
                    _world.SetBlock(new Vector3i(wx, wy + 2, wz), BlockId.Air);
                    CurStamp.Doors.Add(new Vector3f(wx + 0.5f, wy, wz + 0.5f));
                    continue;
                case "glass":
                    _world.SetBlock(p, glass);
                    _shipExtra.Add(WorldConstants.CanonicalBlock(p));
                    continue;
                case "light":
                case "headlight":
                    _world.SetBlock(p, lightW);
                    _shipExtra.Add(WorldConstants.CanonicalBlock(p));
                    continue;
                case "light_red":
                    _world.SetBlock(p, lightR);
                    _shipExtra.Add(WorldConstants.CanonicalBlock(p));
                    continue;
                case "light_green":
                    _world.SetBlock(p, lightG);
                    _shipExtra.Add(WorldConstants.CanonicalBlock(p));
                    continue;
                case "engine":
                    _world.SetBlock(p, dark);
                    _shipExtra.Add(WorldConstants.CanonicalBlock(p));
                    continue;
            }

            if (cell.Kind == "station")
            {
                AddStation(cell.Id, wx, wy, wz, StationBlockKey(cell.Id));
                _shipExtra.Add(WorldConstants.CanonicalBlock(p));
                if (cell.Id == "medbay")
                {
                    medbay = new Vector3f(wx + 0.5f, wy + 1f, wz + 0.5f);
                }

                continue;
            }

            // Any block key (iron_wall, carbon cargo, …) → that block; unknown ids fall back to hull.
            _world.SetBlock(p, _content.GetBlock(cell.Id)?.NumericId ?? wall);
            _shipExtra.Add(WorldConstants.CanonicalBlock(p));
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
                _shipExtra.Add(WorldConstants.CanonicalBlock(fp));
            }
        }

        FillShipFoundation(ox, oz, layout.Width, layout.Length, y0);

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

    /// <summary>True when the player has stepped out of the ship's hull box — through the hatch, off the
    /// edge, or off the floor into the surrounding void. Used to turn "walk out the door in the in-space ship
    /// interior" into an EVA instead of a fall. Derives the hull centre/floor from the heal-tank + half-extents.</summary>
    private bool SteppedOutOfShipHull(Vector3f pos)
    {
        if (!_shipStamped)
        {
            return false;
        }

        float cx = _healTank.X - 0.5f, cz = _healTank.Z - 0.5f, y0 = _healTank.Y - 2f;
        const float margin = 0.4f;
        return pos.Y < y0 - margin
            || pos.X < cx - _shipHalfX - margin || pos.X > cx + _shipHalfX + margin
            || pos.Z < cz - _shipHalfZ - margin || pos.Z > cz + _shipHalfZ + margin;
    }

    private const int ShipFoundationDepth = 6; // plug caves this deep under the ship so you can't fall into one

    /// <summary>Fills any cave/void directly under the ship's footprint with solid ground, so a player who
    /// falls through the floor while its chunk collider is still streaming lands at the surface — not deep in
    /// a cavern ("spawning into a cave").</summary>
    private void FillShipFoundation(int ox, int oz, int width, int length, int y0)
    {
        var fill = _content.GetBlock("stone")?.NumericId ?? _content.GetBlock("iron_wall")?.NumericId ?? BlockId.Air;
        if (fill.IsAir)
        {
            return;
        }

        for (int fx = 0; fx < width; fx++)
        for (int fz = 0; fz < length; fz++)
        for (int dy = 1; dy <= ShipFoundationDepth; dy++)
        {
            var p = new Vector3i(ox + fx, y0 - dy, oz + fz);
            if (_world.GetBlock(p).IsAir)
            {
                _world.SetBlock(p, fill);
            }
        }
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

        if (!p.AboardShip || WrapDistSq(p.Position, pos.Value) > ShipStationReach * ShipStationReach)
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
                // Inside the ship while it floats in space, the cockpit is the helm: take it to fly again
                // (no take-off — you never landed). On a surface it's the star map / travel console.
                if (InShipInterior(session.State.PlayerId))
                {
                    ExitShipToFlight(session.State.PlayerId);
                }
                else
                {
                    Send(session, new ServerMessage { Text = "Cockpit — open the menu (Tab) → Map to travel to another planet." });
                    SendStarMap(session);
                }

                break;
        }
    }

    private void HandleUseStation(PlayerSession session, UseStationIntent intent)
        => UseStation(session.State.PlayerId, intent.Station);

    /// <summary>True if the cell is part of the (indestructible) ship hull/interior fittings.</summary>
    private bool IsShipBlock(Vector3i p)
    {
        // Protected if the block belongs to ANY player's ship in this world.
        foreach (var s in _worlds.Active.ShipStamps.Values)
        {
            if (BlockInStamp(s, p))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a block belongs to one ship's structure (hull box + silhouette, or layout cells).</summary>
    private static bool BlockInStamp(ShipStamp s, Vector3i p)
    {
        if (!s.Stamped)
        {
            return false;
        }

        // Longitude wraps: a ship anchored near X=0 has exterior cells at negative X that persist at
        // canonical (wrapped) coordinates, so compare X the short way round and key Extra canonically.
        p = WorldConstants.CanonicalBlock(p);

        if (s.IsLayout)
        {
            return s.Extra.Contains(p); // designed ship protects exactly its placed cells
        }

        var a = s.Anchor;
        int dx = WorldConstants.WrapDeltaX(p.X - a.X);
        bool inHull = dx >= -s.HalfX && dx <= s.HalfX
               && p.Y >= a.Y && p.Y <= a.Y + s.Height
               && p.Z >= a.Z - s.HalfZ && p.Z <= a.Z + s.HalfZ;
        return inHull || s.Extra.Contains(p);
    }

    /// <summary>Test/diagnostic accessor: whether a world cell is protected ship structure.</summary>
    public bool IsProtectedShipBlock(int x, int y, int z) => IsShipBlock(new Vector3i(x, y, z));

    /// <summary>The ship hull anchor (floor centre) block, or default if no ship is placed.</summary>
    public Vector3i ShipAnchorBlock => _shipAnchor;

    /// <summary>A specific player's ship anchor in the active world (test/inspection) — each player has
    /// their own ship at their own landing zone.</summary>
    public Vector3i ShipAnchorOf(string playerId) => _worlds.Active.StampFor(playerId).Anchor;

    /// <summary>Test/diagnostic: whether a block cell lies inside a ship interior (cell-centre probe).</summary>
    public bool ShipInteriorContainsCellForTest(int x, int y, int z)
        => ShipInteriorContains(new Vector3f(x + 0.5f, y + 0.5f, z + 0.5f));

    /// <summary>True if the position is within ANY player's ship hull box in this world (so no one builds
    /// inside a ship, no flora/creatures spawn there, and standing inside any ship counts as "aboard").</summary>
    private bool ShipInteriorContains(Vector3f p)
    {
        foreach (var s in _worlds.Active.ShipStamps.Values)
        {
            if (s.Stamped)
            {
                var a = s.Anchor;
                double dx = WorldConstants.WrapDeltaX(p.X - a.X); // longitude wraps
                if (dx >= -s.HalfX && dx <= s.HalfX
                    && p.Y >= a.Y && p.Y <= a.Y + s.Height
                    && p.Z >= a.Z - s.HalfZ && p.Z <= a.Z + s.HalfZ)
                {
                    return true;
                }
            }
        }

        return false;
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
