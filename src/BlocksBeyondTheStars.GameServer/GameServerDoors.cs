// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Server-authoritative doors. A doorway opening stays air in the voxel world; a door is a marker-driven
/// entity that fills it, rendered + collided client-side (movement is client-side, so the collider lives
/// on the client). Two kinds:
/// <list type="bullet">
/// <item><b>slide</b> — sci-fi auto doors (cities/towns, stations, the ship): the server opens them when a
/// player is within range and auto-closes them a short moment after the last player leaves.</item>
/// <item><b>hinge</b> — manual village/hamlet doors: a player toggles one by pressing E while standing at it.</item>
/// </list>
/// Doors are built from <c>door_slide</c>/<c>door_hinge</c>/<c>door_energy</c> markers when a structure is
/// stamped; their wall axis + gap width are inferred by probing the surrounding blocks, so they work
/// regardless of facing. The <b>energy</b> kind (auto-open like slide, a passable blue field) is the one
/// door that keeps air inside a sealed base room (#793/#794).
/// </summary>
public sealed partial class GameServer
{
    private const float SlideDoorOpenRange = 2.8f;   // a slide door opens for a player within this range (B23:
                                                     // was 4.5 — too wide, so doors in tight station/village rooms
                                                     // stayed permanently open; now they open as you reach them)
    private const float ShipHatchOpenRange = 2.6f;   // …but the ship's own hatch opens only as you walk up to it,
                                                     // so it stays sealed where you spawn (centre) yet reliably
                                                     // opens before you reach it on the way out (B?: was 1.8 — so
                                                     // tight the hatch could stay shut against you, blocking the exit)
    private const double SlideDoorAutoClose = 1.4;   // …and closes this many seconds after the last one leaves
    private const float HingeDoorReach = 3f;         // how close a player must stand to toggle a hinge door

    /// <summary>A door living in the world. Only on the server; the client sees a <see cref="NetDoor"/>.</summary>
    internal sealed class ServerDoor
    {
        public int Id;
        public string Kind = "slide";   // "slide" | "hinge" | "wood" | "energy"  (wood swings by hand like a hinge)
        public Vector3f Pos;            // doorway-gap centre, floor level
        public bool AxisX;             // wall runs along X (else along Z)
        public float Width = 2f;       // gap width in blocks along the wall axis
        public bool Open;
        public double AutoCloseTimer;  // slide doors: counts down once no player is near
        public bool PlayerBuilt;       // placed by a player (persisted + removable by mining), not stamped
        public float OpenRange = 4.5f; // proximity at which a slide door opens (SlideDoorOpenRange; tighter for the hatch)
        public double NpcHeldUntil;    // #1866: a hand door an NPC swung open closes after this uptime (0 = a player's, left alone)
    }

    /// <summary>
    /// Double doors (#1852): two hand-operated doors a player set side by side in one wall are one gateway,
    /// so E on either leaf swings both — the client already hangs the right-hand leaf on the far jamb
    /// (#1729), but until now each leaf still toggled alone. The server keeps no pair record; like the
    /// client's <c>DoorPairs</c> the partner is inferred from positions alone. The rule is a pure static so
    /// it is covered by plain tests.
    /// </summary>
    internal static class DoorPairing
    {
        /// <summary>Slack on every coordinate comparison — positions are block centres, so anything short of
        /// half a block is noise.</summary>
        private const float Tolerance = 0.05f;

        /// <summary>
        /// True when <paramref name="other"/> is the other leaf of a double door with <paramref name="door"/>:
        /// both player-built and hand-operated, the same kind, the same wall axis, the same floor, and exactly
        /// one block apart ALONG the wall (one block apart across it is two parallel walls, not one doorway).
        /// A door two blocks off, a slide door, a stamped settlement door, or a door on another floor never
        /// partners.
        /// </summary>
        public static bool IsPartner(ServerDoor door, ServerDoor other)
        {
            if (ReferenceEquals(door, other) || !door.PlayerBuilt || !other.PlayerBuilt)
            {
                return false;
            }

            if (!DoorBlocks.IsHandOperated(door.Kind) || door.Kind != other.Kind || door.AxisX != other.AxisX)
            {
                return false;
            }

            if (System.Math.Abs(other.Pos.Y - door.Pos.Y) > Tolerance)
            {
                return false;
            }

            // The wall axis is the direction the leaf runs along: a door in an X wall has its neighbour at X ± 1.
            float along = door.AxisX ? other.Pos.X - door.Pos.X : other.Pos.Z - door.Pos.Z;
            float across = door.AxisX ? other.Pos.Z - door.Pos.Z : other.Pos.X - door.Pos.X;
            return System.Math.Abs(across) <= Tolerance && System.Math.Abs(System.Math.Abs(along) - 1f) <= Tolerance;
        }
    }

    // The door vocabulary (kinds, block keys, marker ids, hand-operated) is the shared DoorBlocks (#1975): one
    // list for the server, the renderer, the placement ghost and the editors.

    private List<ServerDoor> _doors => _worlds.Active.Doors;
    private readonly List<Vector3f> _doorTargets = new(); // reused per tick (no per-tick LINQ alloc)
    private int _nextDoorId { get => _worlds.Active.NextDoorId; set => _worlds.Active.NextDoorId = value; }

    /// <summary>Door states (id/kind/pos/open) for tests + inspection.</summary>
    public IReadOnlyList<(int Id, string Kind, Vector3f Pos, bool Open)> DoorSnapshots
        => _doors.Select(d => (d.Id, d.Kind, d.Pos, d.Open)).ToList();

    /// <summary>Number of doors registered in the active world.</summary>
    public int DoorCount => _doors.Count;

    /// <summary>How each door ended up hanging — its wall axis and the gap width it fills (#1986). For tests +
    /// inspection: a leaf turned across its doorway shows up here as the wrong axis, or as a width wider than
    /// the opening the building was given.</summary>
    public IReadOnlyList<(int Id, string Kind, Vector3f Pos, bool AxisX, float Width, bool PlayerBuilt)> DoorFits
        => _doors.Select(d => (d.Id, d.Kind, d.Pos, d.AxisX, d.Width, d.PlayerBuilt)).ToList();

    /// <summary>(Re)builds the door registry for the active world from every structure stamped into it:
    /// settlement buildings (slide for towns/cities, hinge for villages) and designed ships (slide doors from
    /// the ship editor). Slide vs hinge comes from the marker; the wall axis comes from the generator that cut
    /// the doorway where it recorded one (#1986) and from the blocks around the opening otherwise, and the gap
    /// width is always measured. Idempotent — safe to call after any settlement/ship stamp.</summary>
    private void RegisterDoors()
    {
        _doors.Clear();
        _nextDoorId = 1;

        // Settlement doorways. #1986/#1994: the structure measured its own doorways on its layout when it was
        // stamped — wall axis and gap width — so hanging them reads no world block at all. A marker without
        // such a record (an older stamp in a world being re-entered) falls back to probing the blocks.
        var authored = _worlds.Active.SettlementDoorFits;
        foreach (var (type, pos) in _settlementMarkers)
        {
            if (type == "door_slide" || type == "door_hinge" || type == "door_energy")
            {
                string kind = DoorBlocks.KindForMarker(type);
                _doors.Add(authored.TryGetValue(WorldConstants.CanonicalBlock(pos.ToBlock(), _world.Circumference), out var fit)
                    ? new ServerDoor
                    {
                        Id = _nextDoorId++,
                        Kind = kind,
                        Pos = fit.Centre,
                        AxisX = fit.AxisX,
                        Width = fit.Width,
                        OpenRange = SlideDoorOpenRange,
                    }
                    : MakeDoor(kind, pos));
            }
        }

        // Ship doorways (every player's ship PARKED on this world — ship-as-object: the hull is a placed
        // structure, so the door's jamb/gap probe reads the structure grid, not the world). The ship's own
        // hatch gets a tighter open range so it stays sealed/closed where you spawn inside, opening only when
        // you walk right up to it to leave.
        foreach (var rec in _worlds.Active.LandedShips.Values)
        {
            if (!rec.Placed)
            {
                continue;
            }

            foreach (var (kind, pos) in rec.Doors)
            {
                // An authored ship's doors are all energy doors (item 35): a slide door with a passable blue
                // energy field shown in the opening while open, auto-open like a slide door (handled above).
                // A SELF-BUILT ship's doors keep the kind of the door block the player placed, so a wooden or
                // metal hinge door still swings by hand with E (#1021). The hatch is the wide gap in the
                // ship's rear (-Z) wall, which runs along X → force that door across X so it sits parallel to
                // the opening, not lengthwise in the cabin (B41a; a ±1 jamb probe at the centre of a wide gap
                // is ambiguous). Only the rear-wall hatch gets the forced axis: interior doors of a
                // multi-room layout (hammerhead) can face ±X, and their narrower openings make the jamb probe
                // unambiguous — let it decide.
                var ship = rec;
                var local = ship.ToLocal(new Vector3i(
                    (int)System.Math.Floor(pos.X), (int)System.Math.Floor(pos.Y), (int)System.Math.Floor(pos.Z)),
                    _world.Circumference);
                _doors.Add(MakeDoor(kind, pos, ShipHatchOpenRange, forceAxisX: local.Z == 0 ? true : (bool?)null,
                    solid: (x, y, z) => !ship.Structure.Get(ship.ToLocal(new Vector3i(x, y, z), _world.Circumference)).IsAir));
            }
        }

        LoadPlayerDoors(); // re-add persisted player-built doors after the deterministic rebuild

        if (_doors.Count > 0)
        {
            _log.Info($"Registered {_doors.Count} doors in the active world.");
        }

        // Everyone already on this world must see the rebuilt registry: TickDoors only re-broadcasts on an
        // open/close change, so without this a launched/removed ship left its hatch floating as a ghost door
        // where the ship stood (and a freshly parked ship's hatch stayed missing) until some other door
        // toggled (issue #412 S12). DoorList fully replaces the client's doors; an empty world is a no-op.
        BroadcastDoors();
    }

    /// <summary>(Re)builds the active (station) world's door registry from a station's door markers — called
    /// when a player boards, since a station is its own void world (no settlement/ship doors there).</summary>
    private void RegisterStationDoors(System.Collections.Generic.IEnumerable<(string Type, Vector3f Pos)> markers)
    {
        _doors.Clear();
        _nextDoorId = 1;
        foreach (var (type, pos) in markers)
        {
            if (type == "door_slide" || type == "door_hinge" || type == "door_energy")
            {
                _doors.Add(MakeDoor(DoorBlocks.KindForMarker(type), pos));
            }
        }

        LoadPlayerDoors(); // a player may have built doors in this station's void world too
    }

    /// <summary>Builds a door at a marker, probing the surrounding blocks to find the wall axis and the full
    /// width of the air gap so the panel/collider lines up with the doorway regardless of how it was cut.</summary>
    private ServerDoor MakeDoor(string kind, Vector3f markerPos, float openRange = SlideDoorOpenRange, bool? forceAxisX = null,
        System.Func<int, int, int, bool>? solid = null)
    {
        solid ??= IsSolidBlock; // default probe = the world grid; ship doors probe their structure instead

        int bx = (int)System.Math.Floor(markerPos.X);
        int by = (int)System.Math.Floor(markerPos.Y);
        int bz = (int)System.Math.Floor(markerPos.Z);

        // The wall axis + gap width are the shared door rule (#1975): the same probe the placement ghost and the
        // build editors run over their own grids, so a previewed door and a hung door cannot disagree. A caller
        // that already knows the axis forces it — the ship hatch is a wide gap in the front wall, where a ±1 jamb
        // probe at the centre is ambiguous and would wrongly default to Z, putting the door lengthwise in the
        // cabin (B41a).
        var fit = DoorProbe.Measure(solid, bx, by, bz, forceAxisX);
        var pos = new Vector3f(fit.CentreX(bx), by, fit.CentreZ(bz));
        return new ServerDoor { Id = _nextDoorId++, Kind = kind, Pos = pos, AxisX = fit.AxisX, Width = fit.Width, OpenRange = openRange };
    }

    private bool IsSolidBlock(int x, int y, int z) => !_world.GetBlock(new Vector3i(x, y, z)).IsAir;

    private void TickDoors(double dt)
    {
        if (_doors.Count == 0)
        {
            return;
        }

        // Any player on foot (incl. someone standing inside their ship at the hatch) opens a slide door near
        // them; only players piloting in space are excluded. (The old NPC-style aboard-ship filter kept the
        // ship's own hatch from ever opening for the player inside it.)
        // Reuse a field list instead of allocating a fresh Where(...).Select(...).ToList() every tick (15 Hz).
        _doorTargets.Clear();
        foreach (var s in JoinedInActiveWorld())
        {
            if (!InSpace(s.State.PlayerId))
            {
                _doorTargets.Add(s.State.Position);
            }
        }

        // #1866: an NPC walking a route opens the slide doors on it like a player; idle NPCs don't (a vendor standing
        // beside a doorway must not hold it open all day).
        foreach (var npc in _npcs)
        {
            if (npc.Goal is not null)
            {
                _doorTargets.Add(npc.Pos);
            }
        }

        var targets = _doorTargets;
        bool changed = false;
        foreach (var door in _doors)
        {
            if (door.Kind != "slide" && door.Kind != "energy")
            {
                // hinge doors are manual (HandleDoorInteract); slide + energy auto-open on proximity. A hand door an NPC
                // swung open (#1866) closes behind them once nobody stands in its gap.
                if (door.Open && door.NpcHeldUntil > 0 && _uptime >= door.NpcHeldUntil)
                {
                    bool someoneInGap = false;
                    for (int i = 0; i < targets.Count && !someoneInGap; i++)
                    {
                        someoneInGap = WrapDistSq(targets[i], door.Pos) <= 1.2f * 1.2f;
                    }

                    if (!someoneInGap)
                    {
                        door.Open = false;
                        door.NpcHeldUntil = 0;
                        MarkBaseWallsDirty(_world, door.Pos.ToBlock());
                        changed = true;
                    }
                }

                continue;
            }

            bool near = false;
            foreach (var p in targets)
            {
                if (WrapDistSq(p, door.Pos) <= door.OpenRange * door.OpenRange)
                {
                    near = true;
                    break;
                }
            }

            if (near)
            {
                door.AutoCloseTimer = SlideDoorAutoClose;
                if (!door.Open) { door.Open = true; changed = true; }
            }
            else if (door.Open)
            {
                door.AutoCloseTimer -= dt;
                if (door.AutoCloseTimer <= 0) { door.Open = false; changed = true; }
            }
        }

        if (changed)
        {
            BroadcastDoors();
        }
    }

    private void HandleDoorInteract(PlayerSession session, DoorInteractIntent intent)
    {
        var door = _doors.FirstOrDefault(d => d.Id == intent.DoorId);
        if (door is null || !DoorBlocks.IsHandOperated(door.Kind))
        {
            return; // unknown door, or a slide door (those are server-automatic)
        }

        if (WrapDistSq(session.State.Position, door.Pos) > HingeDoorReach * HingeDoorReach)
        {
            return; // too far to reach the latch
        }

        door.Open = !door.Open;
        door.NpcHeldUntil = 0; // #1866: the player owns this door's state now — no NPC closes it behind them
        MarkBaseWallsDirty(_world, door.Pos.ToBlock()); // #1367: a shut gate is a wall to the fill, an open one a gap

        // #1852: the other leaf of a double door follows this one, so one E swings the whole gateway. The
        // DoorList broadcast below carries every door, so both leaves reach the clients in one message.
        foreach (var other in _doors)
        {
            if (other.Open != door.Open && DoorPairing.IsPartner(door, other))
            {
                other.Open = door.Open;
                MarkBaseWallsDirty(_world, other.Pos.ToBlock());
            }
        }

        BroadcastDoors();
    }

    /// <summary>Test/util entrypoint: a player toggles a hinge door they're standing at (mirrors pressing E).</summary>
    public void InteractDoorForTest(PlayerSession session, int doorId)
        => HandleDoorInteract(session, new DoorInteractIntent { DoorId = doorId });

    /// <summary>A player builds a door: it fills the (air) cell as a 1-wide entity — no solid block — and is
    /// persisted by its cell so the deterministic door rebuild can re-add it. Wall axis comes from the
    /// surrounding jambs if there's a clear one, else from the player's facing.</summary>
    private void PlaceDoor(PlayerSession session, Vector3i pos, string kind)
    {
        // The shared placed-door rule (#1975): jambs on exactly one axis decide, else the wall faces the player —
        // the same rule the placement ghost runs, so the hologram and the hung door agree.
        bool axisX = DoorProbe.AxisForPlacedDoor(IsSolidBlock, pos.X, pos.Y, pos.Z, session.State.Yaw);

        _doors.Add(new ServerDoor
        {
            Id = _nextDoorId++,
            Kind = kind,
            Pos = new Vector3f(pos.X + 0.5f, pos.Y, pos.Z + 0.5f),
            AxisX = axisX,
            Width = 1f,
            PlayerBuilt = true,
        });
        _repo.SaveDoor(new StoredDoor { Planet = _world.LocationId, X = pos.X, Y = pos.Y, Z = pos.Z, Kind = kind, AxisX = axisX });
        BroadcastDoors();
        RefreshStationBoundsAfterDoorChange(); // a doorway on a station's outer face is part of its box (#1559)
    }

    /// <summary>If a player-built door fills the mined cell's column (its ~3-tall opening), remove it, return the
    /// door item to the miner and forget it. Returns true if it handled the mine (a player door was there).</summary>
    /// <summary>#1746: a stamped (station / settlement) door occupies the cell. Since the client aims at doors,
    /// a mine intent can land on one of these too — it is protected like the walls around it and must be
    /// answered as such, not with the "ghost block" heal an air cell would get.</summary>
    private bool StampedDoorAt(Vector3i pos)
    {
        foreach (var d in _doors)
        {
            if (d.PlayerBuilt)
            {
                continue;
            }

            int by = (int)System.Math.Floor(d.Pos.Y);
            if (pos.Y < by || pos.Y > by + 2)
            {
                continue;
            }

            // The doorway is Width cells wide along its wall axis and one cell deep across it.
            float half = System.Math.Max(0.5f, d.Width * 0.5f);
            bool along = d.AxisX
                ? pos.X + 0.5f > d.Pos.X - half && pos.X + 0.5f < d.Pos.X + half && (int)System.Math.Floor(d.Pos.Z) == pos.Z
                : pos.Z + 0.5f > d.Pos.Z - half && pos.Z + 0.5f < d.Pos.Z + half && (int)System.Math.Floor(d.Pos.X) == pos.X;
            if (along)
            {
                return true;
            }
        }

        return false;
    }

    private bool RemovePlayerDoorAt(PlayerSession session, Vector3i pos)
    {
        var door = _doors.FirstOrDefault(d => d.PlayerBuilt
            && (int)System.Math.Floor(d.Pos.X) == pos.X
            && (int)System.Math.Floor(d.Pos.Z) == pos.Z
            && pos.Y >= (int)System.Math.Floor(d.Pos.Y) && pos.Y <= (int)System.Math.Floor(d.Pos.Y) + 2);
        if (door is null)
        {
            return false;
        }

        if (!WithinReach(session.State, pos))
        {
            Reject(session, "mine", "@out_of_reach");
            return true;
        }

        // Room for the returned door block before the door is removed — otherwise picking one up with a full
        // inventory destroyed it outright.
        string doorItem = DoorBlocks.ItemFor(door.Kind);
        var pool = new MaterialPool(_content, session.State, _ship);
        if (!pool.CanFit(new[] { new ItemAmount(doorItem, 1) }))
        {
            Reject(session, "mine", "@inventory_full");
            return true;
        }

        int bx = (int)System.Math.Floor(door.Pos.X), by = (int)System.Math.Floor(door.Pos.Y), bz = (int)System.Math.Floor(door.Pos.Z);
        _doors.Remove(door);
        _repo.DeleteDoor(_world.LocationId, bx, by, bz);

        pool.Add(doorItem, 1); // give the door block back
        BroadcastDoors();
        RefreshStationBoundsAfterDoorChange(); // #1559
        SendInventory(session);
        return true;
    }

    /// <summary>Re-adds this world's persisted player-built doors (idempotent — drops any already loaded first),
    /// so a settlement/ship stamp's deterministic rebuild never wipes them.</summary>
    private void LoadPlayerDoors()
    {
        _doors.RemoveAll(d => d.PlayerBuilt);
        if (_nextDoorId < 1)
        {
            _nextDoorId = 1; // door id 0 means "none" to the client's NearestHinge — never hand it out
        }

        foreach (var sd in _repo.ListDoors(_world.LocationId))
        {
            _doors.Add(new ServerDoor
            {
                Id = _nextDoorId++,
                Kind = sd.Kind,
                Pos = new Vector3f(sd.X + 0.5f, sd.Y, sd.Z + 0.5f),
                AxisX = sd.AxisX,
                Width = 1f,
                PlayerBuilt = true,
            });
        }
    }

    private void BroadcastDoors() => BroadcastToWorld(new DoorList { Doors = _doors.Select(ToNetDoor).ToArray() });

    private void SendDoors(PlayerSession session)
        => Send(session, new DoorList { Doors = _doors.Select(ToNetDoor).ToArray() });

    private static NetDoor ToNetDoor(ServerDoor d) => new()
    {
        Id = d.Id,
        Kind = d.Kind,
        X = d.Pos.X,
        Y = d.Pos.Y,
        Z = d.Pos.Z,
        AxisX = d.AxisX,
        Width = d.Width,
        Open = d.Open,
    };
}
