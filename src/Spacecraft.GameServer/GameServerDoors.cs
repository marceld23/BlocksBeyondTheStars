using System.Collections.Generic;
using System.Linq;
using Spacecraft.Networking.Messages;
using Spacecraft.Shared.Geometry;

namespace Spacecraft.GameServer;

/// <summary>
/// Server-authoritative doors. A doorway opening stays air in the voxel world; a door is a marker-driven
/// entity that fills it, rendered + collided client-side (movement is client-side, so the collider lives
/// on the client). Two kinds:
/// <list type="bullet">
/// <item><b>slide</b> — sci-fi auto doors (cities/towns, stations, the ship): the server opens them when a
/// player is within range and auto-closes them a short moment after the last player leaves.</item>
/// <item><b>hinge</b> — manual village/hamlet doors: a player toggles one by pressing E while standing at it.</item>
/// </list>
/// Doors are built from <c>door_slide</c>/<c>door_hinge</c> markers when a structure is stamped; their wall
/// axis + gap width are inferred by probing the surrounding blocks, so they work regardless of facing.
/// </summary>
public sealed partial class GameServer
{
    private const float SlideDoorOpenRange = 4.5f;   // a slide door opens for a player within this range
    private const double SlideDoorAutoClose = 1.4;   // …and closes this many seconds after the last one leaves
    private const float HingeDoorReach = 3f;         // how close a player must stand to toggle a hinge door

    /// <summary>A door living in the world. Only on the server; the client sees a <see cref="NetDoor"/>.</summary>
    internal sealed class ServerDoor
    {
        public int Id;
        public string Kind = "slide";   // "slide" | "hinge"
        public Vector3f Pos;            // doorway-gap centre, floor level
        public bool AxisX;             // wall runs along X (else along Z)
        public float Width = 2f;       // gap width in blocks along the wall axis
        public bool Open;
        public double AutoCloseTimer;  // slide doors: counts down once no player is near
    }

    private List<ServerDoor> _doors => _worlds.Active.Doors;
    private int _nextDoorId { get => _worlds.Active.NextDoorId; set => _worlds.Active.NextDoorId = value; }

    /// <summary>Door states (id/kind/pos/open) for tests + inspection.</summary>
    public IReadOnlyList<(int Id, string Kind, Vector3f Pos, bool Open)> DoorSnapshots
        => _doors.Select(d => (d.Id, d.Kind, d.Pos, d.Open)).ToList();

    /// <summary>Number of doors registered in the active world.</summary>
    public int DoorCount => _doors.Count;

    /// <summary>(Re)builds the door registry for the active world from every structure stamped into it:
    /// settlement buildings (slide for towns/cities, hinge for villages) and designed ships (slide doors from
    /// the ship editor). Slide vs hinge comes from the marker; the wall axis + gap width are inferred from the
    /// blocks around the opening. Idempotent — safe to call after any settlement/ship stamp.</summary>
    private void RegisterDoors()
    {
        _doors.Clear();
        _nextDoorId = 1;

        // Settlement doorways.
        foreach (var (type, pos) in _settlementMarkers)
        {
            if (type == "door_slide" || type == "door_hinge")
            {
                _doors.Add(MakeDoor(type == "door_hinge" ? "hinge" : "slide", pos));
            }
        }

        // Designed-ship doorways (every player's ship stamped into this world — sci-fi sliders).
        foreach (var stamp in _worlds.Active.ShipStamps.Values)
        {
            foreach (var pos in stamp.Doors)
            {
                _doors.Add(MakeDoor("slide", pos));
            }
        }

        if (_doors.Count > 0)
        {
            _log.Info($"Registered {_doors.Count} doors in the active world.");
        }
    }

    /// <summary>Builds a door at a marker, probing the surrounding blocks to find the wall axis and the full
    /// width of the air gap so the panel/collider lines up with the doorway regardless of how it was cut.</summary>
    private ServerDoor MakeDoor(string kind, Vector3f markerPos)
    {
        int bx = (int)System.Math.Floor(markerPos.X);
        int by = (int)System.Math.Floor(markerPos.Y);
        int bz = (int)System.Math.Floor(markerPos.Z);

        // The jambs are solid along the wall axis; the passage is open along the other. Decide which.
        bool xJamb = IsSolidBlock(bx - 1, by, bz) || IsSolidBlock(bx + 1, by, bz);
        bool zJamb = IsSolidBlock(bx, by, bz - 1) || IsSolidBlock(bx, by, bz + 1);
        bool axisX = xJamb && !zJamb ? true : (zJamb && !xJamb ? false : xJamb);

        // Scan the contiguous air gap along the wall axis (bounded, so a fully open area can't run away).
        int lo = 0, hi = 0;
        for (int s = 1; s <= 3; s++)
        {
            if (IsSolidBlock(axisX ? bx - s : bx, by, axisX ? bz : bz - s)) { break; }
            lo = -s;
        }
        for (int s = 1; s <= 3; s++)
        {
            if (IsSolidBlock(axisX ? bx + s : bx, by, axisX ? bz : bz + s)) { break; }
            hi = s;
        }

        float centre = (lo + hi) * 0.5f;
        float width = hi - lo + 1;
        var pos = new Vector3f(
            (axisX ? bx + centre : bx) + 0.5f,
            by,
            (axisX ? bz : bz + centre) + 0.5f);

        return new ServerDoor { Id = _nextDoorId++, Kind = kind, Pos = pos, AxisX = axisX, Width = width };
    }

    private bool IsSolidBlock(int x, int y, int z) => !_world.GetBlock(new Vector3i(x, y, z)).IsAir;

    private void TickDoors(double dt)
    {
        if (_doors.Count == 0)
        {
            return;
        }

        var targets = JoinedInActiveWorld()
            .Where(s => (!_shipStamped || !s.State.AboardShip) && !InSpace(s.State.PlayerId))
            .Select(s => s.State.Position)
            .ToList();

        bool changed = false;
        foreach (var door in _doors)
        {
            if (door.Kind != "slide")
            {
                continue; // hinge doors are manual (HandleDoorInteract)
            }

            bool near = targets.Any(p => p.DistanceSquared(door.Pos) <= SlideDoorOpenRange * SlideDoorOpenRange);
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
        if (door is null || door.Kind != "hinge")
        {
            return; // unknown door, or a slide door (those are server-automatic)
        }

        if (session.State.Position.DistanceSquared(door.Pos) > HingeDoorReach * HingeDoorReach)
        {
            return; // too far to reach the latch
        }

        door.Open = !door.Open;
        BroadcastDoors();
    }

    /// <summary>Test/util entrypoint: a player toggles a hinge door they're standing at (mirrors pressing E).</summary>
    public void InteractDoorForTest(PlayerSession session, int doorId)
        => HandleDoorInteract(session, new DoorInteractIntent { DoorId = doorId });

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
