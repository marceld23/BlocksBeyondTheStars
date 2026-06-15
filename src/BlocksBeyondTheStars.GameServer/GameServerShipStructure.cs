using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The ship as a physical, enterable place (ship-as-object). The landed ship is a placed voxel structure
/// OBJECT — the same per-player <see cref="SpaceStructure"/> the flight view renders (design + persisted
/// player edits) — anchored on the player's landing pad. Nothing is stamped into the world block grid
/// anymore: the client renders + collides the structure mesh, gameplay anchors (stations, heal-tank,
/// doors, aboard checks) are derived from the structure cells + the world anchor, and pad terrain is
/// levelled by worldgen (<c>FlattenLandingPads</c>) instead of per-landing terrain mutation.
///
/// Whether a player is "aboard" is derived authoritatively from standing inside the structure bounds,
/// which gates cargo crafting, module building and oxygen regeneration as before.
/// </summary>
public sealed partial class GameServer
{
    private const float ShipStationReach = 3f;
    private const int PadGroundProtectDepth = 9; // pad ground under a parked ship is not mineable

    /// <summary>The landed-ship record of the player currently being served (the ship cursor). Each player
    /// has their own parked ship in their world; per-player ops (place, heal-tank, stations) use this one.</summary>
    private LandedShip CurLanded => _worlds.Active.LandedFor(_current?.State.PlayerId ?? string.Empty);

    private Vector3f _healTank => CurLanded.HealTank;
    private bool _shipPlaced => CurLanded.Placed;

    /// <summary>The medbay heal-tank position inside the ship (respawn point), if a ship is placed.</summary>
    public Vector3f HealTank => _healTank;
    public bool HasShip => _shipPlaced;

    /// <summary>Parks the served player's ship as a placed structure object on their landing pad. Replaces
    /// the old block-grid stamp: builds the structure from the active design + persisted edits, anchors it
    /// ON the (worldgen-levelled) pad surface, derives the gameplay anchors, and announces it to everyone
    /// on the world. Idempotent — re-placing (ship switch, reload) just replaces the record + re-broadcasts.</summary>
    private void PlaceLandedShip()
    {
        if (_current is null)
        {
            return;
        }

        string playerId = _current.State.PlayerId;
        var rec = CurLanded;

        var pad = PlayerPad(_current);
        int y0 = PadGroundY(pad.CenterX, pad.CenterZ);

        var s = BuildShipStructure(playerId);
        if (s.Cells.Count == 0)
        {
            rec.Placed = false;
            _log.Info("Ship not placed: the structure is empty ('iron_wall' block missing from content?).");
            return;
        }

        rec.Structure = s;
        // The structure sits ON the levelled pad surface (origin.y = first cell layer above the ground).
        rec.Origin = new Vector3i(pad.CenterX - s.Width / 2, y0 + 1, pad.CenterZ - s.Length / 2);

        rec.Stations.Clear();
        foreach (var (type, cell) in s.StationCells)
        {
            rec.Stations.Add((type, new Vector3f(
                rec.Origin.X + cell.X + 0.5f, rec.Origin.Y + cell.Y, rec.Origin.Z + cell.Z + 0.5f)));
        }

        rec.Doors.Clear();
        foreach (var cell in s.DoorCells)
        {
            rec.Doors.Add(new Vector3f(
                rec.Origin.X + cell.X + 0.5f, rec.Origin.Y + cell.Y, rec.Origin.Z + cell.Z + 0.5f));
        }

        rec.HealTank = s.MedbayCell is { } mb
            ? new Vector3f(rec.Origin.X + mb.X + 0.5f, rec.Origin.Y + mb.Y + 1f, rec.Origin.Z + mb.Z + 0.5f)
            : new Vector3f(rec.Origin.X + s.Width / 2 + 0.5f, rec.Origin.Y + 1f, rec.Origin.Z + s.Length / 2 + 0.5f);

        CleanLegacyStampResidue(rec); // pre-object saves carry the old stamped hull as world block edits

        rec.Placed = true;
        _log.Info($"Ship parked at ({rec.Origin.X}, {rec.Origin.Y}, {rec.Origin.Z}) — {s.Cells.Count} cells, {rec.Stations.Count} stations.");

        BroadcastToWorld(LandedShipMessage(playerId, rec, removed: false));
        RegisterDoors(); // pick up the ship's doors (+ keep settlement/other-ship doors in sync)
    }

    /// <summary>Removes a player's parked ship from the active world (launch into space / logout) and tells
    /// everyone on the world to despawn the object. No terrain to restore — the world was never touched.</summary>
    private void RemoveLandedShip(PlayerSession session)
    {
        var rec = _worlds.Active.LandedFor(session.State.PlayerId);
        if (!rec.Placed)
        {
            return;
        }

        rec.Placed = false;
        BroadcastToWorld(new LandedShipState
        {
            PlayerId = session.State.PlayerId,
            StructureId = "ship:" + session.State.PlayerId,
            Removed = true,
        });
        RegisterDoors(); // drop the ship's doors from the registry
    }

    /// <summary>Sends every ship parked on the active world to one session (join / world-enter snapshot).</summary>
    private void SendLandedShips(PlayerSession session)
    {
        foreach (var (ownerId, rec) in _worlds.Active.LandedShips)
        {
            if (rec.Placed)
            {
                Send(session, LandedShipMessage(ownerId, rec, removed: false));
            }
        }
    }

    private LandedShipState LandedShipMessage(string ownerId, LandedShip rec, bool removed)
    {
        var s = rec.Structure;
        var msg = new LandedShipState
        {
            PlayerId = ownerId,
            StructureId = s.Id,
            Removed = removed,
            OriginX = rec.Origin.X, OriginY = rec.Origin.Y, OriginZ = rec.Origin.Z,
            // A landed NPC trader has no session; resolve its hull tint from the trader registry instead.
            Hull = ownerId.StartsWith("npc:", System.StringComparison.Ordinal)
                ? NpcLandedHull(ownerId)
                : FindSessionByPlayerId(ownerId)?.HullColor ?? 0,
            Width = s.Width, Height = s.Height, Length = s.Length,
        };

        if (!removed)
        {
            int n = s.Cells.Count;
            msg.X = new int[n]; msg.Y = new int[n]; msg.Z = new int[n]; msg.Block = new ushort[n];
            int i = 0;
            foreach (var (cell, block) in s.Cells)
            {
                msg.X[i] = cell.X; msg.Y[i] = cell.Y; msg.Z[i] = cell.Z; msg.Block[i] = block.Value;
                i++;
            }
        }

        return msg;
    }

    /// <summary>One-shot migration per placement: pre-object saves persisted the STAMPED hull as world
    /// block edits — delete any edits inside the parked ship's volume (incl. the old silhouette margin and
    /// foundation) and regenerate the affected chunks, so the old block hull doesn't stand inside the new
    /// object. Pad volumes are reserved (no building there), so no legitimate player edits are lost.</summary>
    private void CleanLegacyStampResidue(LandedShip rec)
    {
        var s = rec.Structure;
        var min = new Vector3i(rec.Origin.X - 4, rec.Origin.Y - 8, rec.Origin.Z - 4);
        var max = new Vector3i(rec.Origin.X + s.Width + 4, rec.Origin.Y + s.Height + 3, rec.Origin.Z + s.Length + 4);
        _repo.DeleteBlockEdits(_world.LocationId, min, max);
        _world.ForgetChunksIn(min, max);

        // Re-stream the affected chunks to everyone on this world so stale hull blocks vanish client-side.
        var seen = new HashSet<ChunkCoord>();
        for (int x = min.X; x <= max.X; x += WorldConstants.ChunkSize)
        for (int y = min.Y; y <= max.Y; y += WorldConstants.ChunkSize)
        for (int z = min.Z; z <= max.Z; z += WorldConstants.ChunkSize)
        {
            seen.Add(WorldConstants.CanonicalChunk(WorldConstants.WorldToChunk(new Vector3i(x, y, z)), _world.Circumference));
        }

        seen.Add(WorldConstants.CanonicalChunk(WorldConstants.WorldToChunk(max), _world.Circumference));
        foreach (var session in JoinedInActiveWorld())
        {
            foreach (var coord in seen)
            {
                session.SentChunks.Remove(coord);
            }
        }
    }

    private static string StationBlockKey(string station) => station switch
    {
        "medbay" => "ice",
        "cockpit" => "data_cache",
        "workshop" => "stone",
        "quarters" => "carbon",
        _ => "iron_wall",
    };

    /// <summary>True when the player has stepped out of the ship's structure bounds — through the hatch, off
    /// the edge, or off the floor into the surrounding void. Used to turn "walk out the door in the in-space
    /// ship interior" into an EVA instead of a fall.</summary>
    private bool SteppedOutOfShipHull(Vector3f pos)
    {
        var rec = CurLanded;
        if (!rec.Placed)
        {
            return false;
        }

        const float margin = 0.4f;
        var s = rec.Structure;
        double dx = WorldConstants.WrapDeltaX(pos.X - rec.Origin.X, _world.Circumference);
        return pos.Y < rec.Origin.Y - margin
            || dx < -margin || dx > s.Width + margin
            || pos.Z < rec.Origin.Z - margin || pos.Z > rec.Origin.Z + s.Length + margin;
    }

    private void SendShipStations(PlayerSession session)
    {
        if (!_shipPlaced)
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

    private List<(string Type, Vector3f Pos)> _stations => CurLanded.Stations;

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

    /// <summary>True if the world cell is protected because a parked ship rests on it: the pad ground
    /// directly under any ship's footprint cannot be mined out (the object must keep its foundation).
    /// The hull itself is no longer world blocks, so this guards only the ground.</summary>
    private bool IsShipBlock(Vector3i p)
    {
        foreach (var rec in _worlds.Active.LandedShips.Values)
        {
            if (!rec.Placed)
            {
                continue;
            }

            var s = rec.Structure;
            int dx = WorldConstants.WrapDeltaX(p.X - rec.Origin.X, _world.Circumference);
            if (dx >= -1 && dx <= s.Width
                && p.Z >= rec.Origin.Z - 1 && p.Z <= rec.Origin.Z + s.Length
                && p.Y < rec.Origin.Y && p.Y >= rec.Origin.Y - PadGroundProtectDepth)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Test/diagnostic accessor: whether a world cell is protected ship structure/foundation.</summary>
    public bool IsProtectedShipBlock(int x, int y, int z) => IsShipBlock(new Vector3i(x, y, z));

    /// <summary>The ship anchor (floor-centre at pad-ground height) block, or default if no ship is placed.</summary>
    public Vector3i ShipAnchorBlock => AnchorOf(CurLanded);

    /// <summary>A specific player's ship anchor in the active world (test/inspection) — each player has
    /// their own ship at their own landing pad.</summary>
    public Vector3i ShipAnchorOf(string playerId) => AnchorOf(_worlds.Active.LandedFor(playerId));

    private static Vector3i AnchorOf(LandedShip rec) => rec.Placed
        ? new Vector3i(rec.Origin.X + rec.Structure.Width / 2, rec.Origin.Y - 1, rec.Origin.Z + rec.Structure.Length / 2)
        : default;

    /// <summary>Test/diagnostic: whether a block cell lies inside a ship interior (cell-centre probe).</summary>
    public bool ShipInteriorContainsCellForTest(int x, int y, int z)
        => ShipInteriorContains(new Vector3f(x + 0.5f, y + 0.5f, z + 0.5f));

    /// <summary>True if the position is within ANY player's parked-ship bounds in this world (so no one
    /// builds world blocks inside a ship, no flora/creatures spawn there, and standing inside any ship
    /// counts as "aboard").</summary>
    private bool ShipInteriorContains(Vector3f p)
    {
        foreach (var rec in _worlds.Active.LandedShips.Values)
        {
            if (!rec.Placed)
            {
                continue;
            }

            var s = rec.Structure;
            double dx = WorldConstants.WrapDeltaX(p.X - rec.Origin.X, _world.Circumference); // longitude wraps
            if (dx >= 0 && dx <= s.Width
                && p.Y >= rec.Origin.Y && p.Y <= rec.Origin.Y + s.Height + 1
                && p.Z >= rec.Origin.Z && p.Z <= rec.Origin.Z + s.Length)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True if a position is inside (or right against) a parked ship, so creatures and settlement
    /// NPCs can be kept from walking into the player's ship. Uses a small margin around the bounds;
    /// positions above the hull (flying creatures) are allowed to pass over.
    /// </summary>
    private bool EntityBlockedByShip(Vector3f p)
    {
        const float m = 0.5f;
        foreach (var rec in _worlds.Active.LandedShips.Values)
        {
            if (!rec.Placed)
            {
                continue;
            }

            var s = rec.Structure;
            double dx = WorldConstants.WrapDeltaX(p.X - rec.Origin.X, _world.Circumference);
            if (dx >= -m && dx <= s.Width + m
                && p.Y >= rec.Origin.Y - 1 - m && p.Y <= rec.Origin.Y + s.Height + m
                && p.Z >= rec.Origin.Z - m && p.Z <= rec.Origin.Z + s.Length + m)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Tells the client where the ship stands so it can show a compass/minimap to it.</summary>
    private void SendShipPlacement(PlayerSession session)
    {
        if (_shipPlaced)
        {
            var a = AnchorOf(CurLanded);
            Send(session, new ShipPlacement { X = a.X + 0.5f, Y = a.Y, Z = a.Z + 0.5f });
        }
    }

    /// <summary>Derives the player's aboard state from their position; resends inventory/state on change.</summary>
    private void UpdateAboard(PlayerSession session)
    {
        if (!_shipPlaced)
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
