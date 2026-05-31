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
    // Interior footprint: 5 wide (x), 4 tall (y), 7 long (z). Walls sit on the half-extents.
    private const int ShipHalfX = 2;
    private const int ShipHeight = 4;
    private const int ShipHalfZ = 3;

    private Vector3i _shipAnchor;
    private Vector3f _healTank;
    private bool _shipStamped;

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

        var wall = _content.GetBlock("iron_wall")?.NumericId ?? BlockId.Air;
        var glass = _content.GetBlock("glass")?.NumericId ?? wall;
        if (wall.IsAir)
        {
            _log.Info("Ship hull not placed: 'iron_wall' block missing from content.");
            return;
        }

        for (int x = cx - ShipHalfX; x <= cx + ShipHalfX; x++)
        for (int y = y0; y <= y0 + ShipHeight; y++)
        for (int z = cz - ShipHalfZ; z <= cz + ShipHalfZ; z++)
        {
            var pos = new Vector3i(x, y, z);

            bool shell = x == cx - ShipHalfX || x == cx + ShipHalfX
                         || y == y0 || y == y0 + ShipHeight
                         || z == cz - ShipHalfZ || z == cz + ShipHalfZ;

            if (!shell)
            {
                _world.SetBlock(pos, BlockId.Air); // hollow interior
                continue;
            }

            // Door: a 1-wide, 2-tall gap in the -Z wall, centred on x.
            bool door = z == cz - ShipHalfZ && x == cx && (y == y0 + 1 || y == y0 + 2);
            if (door)
            {
                _world.SetBlock(pos, BlockId.Air);
                continue;
            }

            // Glass viewport: the +Z wall's middle row (excluding the corners).
            bool viewport = z == cz + ShipHalfZ && y == y0 + 2 && x > cx - ShipHalfX && x < cx + ShipHalfX;
            _world.SetBlock(pos, viewport ? glass : wall);
        }

        // Interior station markers on the floor (placeholders for proper props/interactions
        // later — see CLIENT_COMPLETION_PLAN M23a). The whole hull is mining-protected, so
        // these stay put. The logical stations already exist as ship modules.
        int floor = y0 + 1;
        PlaceStation(cx - 1, floor, cz - 2, "ice");          // medbay heal-tank (respawn)
        PlaceStation(cx,     floor, cz + 2, "data_cache");   // cockpit console (star map / travel)
        PlaceStation(cx + 1, floor, cz,     "stone");        // workshop bench
        PlaceStation(cx - 1, floor, cz + 1, "iron_wall");    // cargo crates
        PlaceStation(cx + 1, floor, cz - 2, "carbon");       // quarters / bunk

        // The heal-tank tile is the in-ship respawn point.
        _healTank = new Vector3f(cx - 1 + 0.5f, floor + 1f, cz - 2 + 0.5f);

        _shipStamped = true;
        _log.Info($"Ship hull placed at ({cx}, {y0}, {cz}).");
    }

    private void PlaceStation(int x, int y, int z, string blockKey)
    {
        if (_content.GetBlock(blockKey) is { } def)
        {
            _world.SetBlock(new Vector3i(x, y, z), def.NumericId);
        }
    }

    /// <summary>True if the cell is part of the (indestructible) ship hull/interior fittings.</summary>
    private bool IsShipBlock(Vector3i p)
    {
        if (!_shipStamped)
        {
            return false;
        }

        int cx = _shipAnchor.X, y0 = _shipAnchor.Y, cz = _shipAnchor.Z;
        return p.X >= cx - ShipHalfX && p.X <= cx + ShipHalfX
               && p.Y >= y0 && p.Y <= y0 + ShipHeight
               && p.Z >= cz - ShipHalfZ && p.Z <= cz + ShipHalfZ;
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
        return p.X >= cx - ShipHalfX && p.X <= cx + ShipHalfX
               && p.Y >= y0 && p.Y <= y0 + ShipHeight
               && p.Z >= cz - ShipHalfZ && p.Z <= cz + ShipHalfZ;
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
