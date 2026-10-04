// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The Crystal Net aboard the own ship (#2268). A ship is a structure OBJECT, not cells of the world grid, so each
/// parked own ship (landed on a planet, or the walkable interior out in space) carries a net of its own: a
/// <see cref="CrystalNetState"/> in ship-LOCAL cells, persisted as <c>crystal_cell</c> rows under the ship's store id
/// (<c>ship:&lt;pid&gt;</c> / <c>ship:&lt;pid&gt;#&lt;shipId&gt;</c>), rebuilt whenever the ship is parked (every landing, every
/// step inside) and ticked with the world it is parked in — "the ship wakes up when you step in"; in flight it rests.
/// <para>The net code runs unchanged inside a <b>frame</b> (<see cref="_crystalFrame"/>): <see cref="CrystalNet"/> is the
/// ship's state, block reads and the twin swaps go to the ship structure (a swap is not persisted — the first beat after
/// a rebuild applies the level again), positions turn into world positions for sounds and bodies, and every list goes
/// out tagged with the ship's structure id so the client draws it in the ship's frame.</para>
/// <para>What works aboard is <see cref="CrystalNetRules.WorksAboard"/>; everything else built into a ship stays
/// decoration (and VEGA says so once, #2219). The ship's doors follow its net; the ship sensor reads its ship.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>One parked ship's net: the ship, its owner, its store id (persistence) and its structure id (the wire).</summary>
    internal sealed class CrystalShipFrame
    {
        public string OwnerId = string.Empty;
        public string StoreId = string.Empty;
        public string StructureId = string.Empty;
        public LandedShip Rec = null!;
        public CrystalNetState Net { get; } = new();
    }

    /// <summary>The frame the net code runs in right now; null = the world grid.</summary>
    private CrystalShipFrame? _crystalFrame;

    /// <summary>The net the code works on: the frame's ship net, or the active world's.</summary>
    private CrystalNetState CrystalNet => _crystalFrame?.Net ?? _worlds.Active.CrystalNet;

    /// <summary>Where rows of the current frame are stored: the ship's store id, or the world's location id.</summary>
    private string CrystalStoreId => _crystalFrame?.StoreId ?? _world.LocationId;

    /// <summary>A cell of the current frame as a world cell (a ship's local cell plus its origin, wrapped).</summary>
    private Vector3i CrystalToWorld(Vector3i cell)
        => _crystalFrame is { } f
            ? new Vector3i(WorldConstants.WrapX(f.Rec.Origin.X + cell.X, _world.Circumference), f.Rec.Origin.Y + cell.Y, f.Rec.Origin.Z + cell.Z)
            : cell;

    /// <summary>A world cell in the current frame's cells.</summary>
    private Vector3i CrystalFromWorld(Vector3i world) => _crystalFrame is { } f ? f.Rec.ToLocal(world, _world.Circumference) : world;

    /// <summary>The centre of a frame cell as a world position (sounds, effects, bodies).</summary>
    private Vector3f CrystalWorldCentre(Vector3i cell)
    {
        var w = CrystalToWorld(cell);
        return new Vector3f(w.X + 0.5f, w.Y + 0.5f, w.Z + 0.5f);
    }

    /// <summary>The block of a frame cell (a world cell only when its chunk is loaded).</summary>
    private BlockId CrystalReadBlock(Vector3i cell) => _crystalFrame is { } f ? f.Rec.Structure.Get(cell) : _world.GetBlockIfLoaded(cell);

    private (int Tint, int Glow) CrystalReadModifier(Vector3i cell)
        => _crystalFrame is { } f ? (f.Rec.Structure.Mods.TryGetValue(cell, out var m) ? m : (0, 0)) : _world.GetModifier(cell);

    private int CrystalReadShape(Vector3i cell)
        => _crystalFrame is { } f ? (f.Rec.Structure.Shapes.TryGetValue(cell, out int s) ? s : 0) : _world.GetShape(cell);

    /// <summary>The tag of the current frame on the wire: empty for the world, the ship's structure id aboard.</summary>
    private string CrystalFrameTag => _crystalFrame?.StructureId ?? string.Empty;

    /// <summary>The ship frames of the active world, by owner.</summary>
    private Dictionary<string, CrystalShipFrame> CrystalShipFrames => _worlds.Active.CrystalNet.ShipFrames;

    /// <summary>Evaluates <paramref name="f"/> inside a ship's frame.</summary>
    private T InFrame<T>(CrystalShipFrame frame, Func<T> f)
    {
        T result = default!;
        InCrystalFrame(frame, () => result = f());
        return result;
    }

    /// <summary>Runs <paramref name="action"/> inside a ship's frame and returns to the world afterwards.</summary>
    private void InCrystalFrame(CrystalShipFrame frame, Action action)
    {
        var previous = _crystalFrame;
        _crystalFrame = frame;
        try
        {
            action();
        }
        finally
        {
            _crystalFrame = previous;
        }
    }

    // ------------------------------------------------------------------------------------------------------
    // Life cycle: a parked ship's net is rebuilt from its rows when the ship is parked, dropped when it leaves
    // ------------------------------------------------------------------------------------------------------

    /// <summary>Device ids of ship nets start far above the world's, so a ship's looping siren never shares its sound id
    /// with a world device (the client stops a loop by that id).</summary>
    private int _nextCrystalFrameIdBase = 1_000_000;

    /// <summary>A ship was parked on the active world (landing, joining, stepping inside in space, commissioning): its net
    /// comes back from its rows. A device cell without a row — built on the keel, on a spacewalk or before #2268 — joins
    /// it too (the owner's, pointing its default way), and the lamps beside the devices are found again.</summary>
    private void LoadCrystalShipNet(string ownerId, LandedShip rec)
    {
        if (_crystalFrame is not null || ownerId.Length == 0 || IsConstructionKey(ownerId) || ownerId.StartsWith("npc:", StringComparison.Ordinal))
        {
            return;
        }

        DropCrystalShipNet(ownerId);
        var frame = new CrystalShipFrame
        {
            OwnerId = ownerId,
            StoreId = StructureEditStoreId(rec.Structure),
            StructureId = rec.Structure.Id,
            Rec = rec,
        };
        frame.Net.NextDeviceId = _nextCrystalFrameIdBase;
        _nextCrystalFrameIdBase += 100_000;
        CrystalShipFrames[ownerId] = frame;
        InCrystalFrame(frame, () =>
        {
            var state = CrystalNet;
            foreach (var row in _repo.ListCrystalCells(frame.StoreId))
            {
                var pos = new Vector3i(row.X, row.Y, row.Z);
                if (!Enum.TryParse<CrystalDeviceKind>(row.Kind, out var kind) || kind == CrystalDeviceKind.None)
                {
                    continue;
                }

                // A row whose block is gone (a wreck carved it, a hull rebuilt without it) is dropped, not resurrected.
                var def = _content.BlockById(rec.Structure.Get(pos));
                if (def is null || CrystalNetRules.KindOf(def) != kind || !CrystalNetRules.WorksAboard(kind))
                {
                    _repo.DeleteCrystalCell(frame.StoreId, row.X, row.Y, row.Z);
                    continue;
                }

                RegisterCrystalCell(pos, kind, def.Key, row.OwnerId, row.Mode, row.Config, row.Label, CrystalConfigInt(row.Config, "yaw", 0), persist: false);
            }

            var fresh = new List<Vector3i>();
            foreach (var (pos, id) in rec.Structure.Cells.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.Z).ThenBy(kv => kv.Key.X))
            {
                var def = _content.BlockById(id);
                var kind = CrystalNetRules.KindOf(def);
                if (def is null || kind == CrystalDeviceKind.None || state.Cells.ContainsKey(pos) || !CrystalNetRules.WorksAboard(kind)
                    || CrystalNetRules.IsPassivePort(kind))
                {
                    continue;
                }

                string config = CrystalNetRules.IsDirectional(kind) ? "yaw=0" : string.Empty;
                RegisterCrystalCell(pos, kind, def.Key, ownerId, 0, config, string.Empty, 0, persist: true);
                fresh.Add(pos);
            }

            foreach (var pos in fresh)
            {
                DiscoverCrystalNeighbours(pos, ownerId);
            }

            state.NextLogicBeat = _uptime;
            state.NextSensorBeat = _uptime;
            state.NetListDirty = true;
            state.DeviceListDirty = true;
        });
    }

    /// <summary>A ship left the active world (launch, logout, switch): its net goes with it — loops stop, the clients drop
    /// its lists.</summary>
    private void DropCrystalShipNet(string ownerId)
    {
        if (!CrystalShipFrames.Remove(ownerId, out var frame))
        {
            return;
        }

        foreach (var c in frame.Net.Cells.Values.Where(c => c.Looping))
        {
            BroadcastToWorld(new SoundFx { SoundId = string.Empty, Stop = true, SourceId = c.Id });
        }

        BroadcastToWorld(new CrystalNetList { Frame = frame.StructureId, Nets = Array.Empty<NetCrystalNet>() });
        BroadcastToWorld(new CrystalDeviceList { Frame = frame.StructureId, Devices = Array.Empty<NetCrystalDevice>() });
    }

    /// <summary>Every tick, after the world's net: the parked ships' nets — beats and lists, no machines, no catch-up.</summary>
    private void TickCrystalShipNets()
    {
        if (CrystalShipFrames.Count == 0)
        {
            return;
        }

        foreach (var frame in CrystalShipFrames.Values.ToList())
        {
            if (!frame.Rec.Placed || frame.Net.Cells.Count == 0)
            {
                continue;
            }

            InCrystalFrame(frame, () =>
            {
                var state = CrystalNet;
                if (_uptime >= state.NextSensorBeat)
                {
                    state.NextSensorBeat = _uptime + CrystalNetRules.SensorBeatSeconds;
                    CrystalSensorBeat();
                }

                if (_uptime >= state.NextLogicBeat)
                {
                    state.NextLogicBeat = _uptime + CrystalNetRules.LogicBeatSeconds;
                    CrystalLogicBeat();
                }

                if (state.NetListDirty)
                {
                    state.NetListDirty = false;
                    BroadcastToWorld(CrystalNetMessage());
                }

                if (state.DeviceListDirty)
                {
                    state.DeviceListDirty = false;
                    BroadcastCrystalDeviceChanges();
                }
            });
        }
    }

    /// <summary>On join: every parked ship's net of the world, tagged with its ship.</summary>
    private void SendCrystalShipNets(PlayerSession session)
    {
        foreach (var frame in CrystalShipFrames.Values)
        {
            InCrystalFrame(frame, () =>
            {
                Send(session, CrystalNetMessage(baseline: false));
                Send(session, CrystalDeviceMessage(baseline: false));
            });
        }
    }

    /// <summary>The frame of the player's own parked ship on this world, or null.</summary>
    private CrystalShipFrame? OwnShipFrame(string ownerId)
        => CrystalShipFrames.TryGetValue(ownerId, out var f) && f.Rec.Placed ? f : null;

    /// <summary>The frame a client named (a ship's structure id), or null for the world / an unknown frame.</summary>
    private CrystalShipFrame? FrameByTag(string? tag)
        => string.IsNullOrEmpty(tag) ? null : CrystalShipFrames.Values.FirstOrDefault(f => f.StructureId == tag && f.Rec.Placed);

    // ------------------------------------------------------------------------------------------------------
    // Edits aboard: the landed-ship edit paths report placed and mined cells
    // ------------------------------------------------------------------------------------------------------

    /// <summary>A cell was built into the player's own parked ship: a device that works aboard joins the ship's net.</summary>
    private void OnCrystalShipCellPlaced(PlayerSession session, LandedShip rec, Vector3i pos, BlockDefinition def, int intentYaw)
    {
        var kind = CrystalNetRules.KindOf(def);
        if (kind == CrystalDeviceKind.None || !CrystalNetRules.WorksAboard(kind) || OwnShipFrame(session.State.PlayerId) is not { } frame
            || !ReferenceEquals(frame.Rec, rec))
        {
            return;
        }

        InCrystalFrame(frame, () => OnCrystalBlockPlaced(session, pos, def, string.Empty, intentYaw));
    }

    /// <summary>A cell of the player's own parked ship was mined: it leaves the ship's net.</summary>
    private void OnCrystalShipCellRemoved(string ownerId, LandedShip rec, Vector3i pos)
    {
        if (OwnShipFrame(ownerId) is not { } frame || !ReferenceEquals(frame.Rec, rec))
        {
            return;
        }

        InCrystalFrame(frame, () => OnCrystalBlockRemoved(pos, null));
    }

    /// <summary>A self-built ship's edit rebuilt its hull from the stored blob: a hull grown toward −X / −Y / −Z moved every
    /// cell by <paramref name="shift"/> (the net is re-keyed and rebuilt); otherwise the swapped twins went back to their
    /// stored block and are re-applied on the next beat. Then the edited cell joins or leaves the net.</summary>
    private void OnCustomShipCellsCommitted(PlayerSession session, LandedShip rec, Vector3i shift, Vector3i editedCell, ushort editedBlock, int deviceDir)
    {
        string ownerId = session.State.PlayerId;
        if (OwnShipFrame(ownerId) is not { } frame || !ReferenceEquals(frame.Rec, rec))
        {
            return;
        }

        if (shift != Vector3i.Zero)
        {
            ShiftCrystalShipRows(frame.StoreId, shift);
            LoadCrystalShipNet(ownerId, rec);
            frame = OwnShipFrame(ownerId)!;
        }
        else
        {
            foreach (var c in frame.Net.Cells.Values)
            {
                if (IsCrystalTwinKind(c.Kind))
                {
                    c.Synced = false; // the rebuilt hull shows the stored block again
                }
            }
        }

        var cell = editedCell + shift;
        if (editedBlock == BlockId.AirValue)
        {
            InCrystalFrame(frame, () => OnCrystalBlockRemoved(cell, null));
        }
        else if (_content.BlockById(new BlockId(editedBlock)) is { } def)
        {
            InCrystalFrame(frame, () => OnCrystalBlockPlaced(session, cell, def, string.Empty, deviceDir));
        }
    }

    /// <summary>Moves every row of a ship's net by the same step — the cells and the partners their <c>pair=</c> names.
    /// All rows go first, then all come back: a row moved onto a neighbour's old cell must not be overwritten.</summary>
    private void ShiftCrystalShipRows(string store, Vector3i shift)
    {
        var rows = _repo.ListCrystalCells(store).ToList();
        foreach (var row in rows)
        {
            _repo.DeleteCrystalCell(store, row.X, row.Y, row.Z);
        }

        foreach (var row in rows)
        {
            row.X += shift.X;
            row.Y += shift.Y;
            row.Z += shift.Z;
            if (CrystalPairCell(row.Config) is { } partner)
            {
                row.Config = CrystalConfigWith(row.Config, "pair", CrystalPairValue(partner + shift));
            }

            _repo.SaveCrystalCell(row);
        }
    }

    // ------------------------------------------------------------------------------------------------------
    // Doors aboard and the ship sensor
    // ------------------------------------------------------------------------------------------------------

    /// <summary>Whether a door follows the net of the current frame: a parked own ship's doorway follows its ship's net,
    /// every other door (a world door, a trader's hatch, a keel's opening) the world's.</summary>
    private bool DoorInCurrentFrame(ServerDoor door)
        => _crystalFrame is { } f
            ? door.ShipOwner == f.OwnerId
            : door.ShipOwner.Length == 0 || !CrystalShipFrames.ContainsKey(door.ShipOwner);

    /// <summary>A device's one-shot sound at its cell (in world space aboard a ship).</summary>
    private void CrystalSoundAt(ServerCrystalCell c, string soundId)
    {
        var at = CrystalWorldCentre(c.Cell);
        BroadcastToWorld(new SoundFx { SoundId = soundId, X = at.X, Y = at.Y, Z = at.Z, SourceId = c.Id });
    }

    /// <summary>#2268: what the ship sensor of the current frame reads from its ship.</summary>
    private bool ShipSensorReads(ServerCrystalCell c)
    {
        if (_crystalFrame is not { } frame || FindSessionByPlayerId(frame.OwnerId) is not { } owner
            || !owner.Ships.TryGetValue(owner.ActiveShipId, out var ship))
        {
            return false;
        }

        float max = ShipHullMaxFor(ship);
        return (ShipSensorMode)c.Mode switch
        {
            ShipSensorMode.HullDamaged => ship.Hull < max - 0.01f,
            ShipSensorMode.HullLow => ship.Hull < max * CrystalNetRules.ShipHullLowFraction,
            ShipSensorMode.ShieldEmpty => ship.Shield <= 0.01f,
            ShipSensorMode.Landed => !_world.Planet.Void,
            ShipSensorMode.Docked => _docked.ContainsKey(frame.OwnerId),
            _ => false,
        };
    }

    /// <summary>Test seam: the own parked ship's frame, with the cursor on the owner's world.</summary>
    private CrystalShipFrame? ShipFrameForTest(string ownerId)
    {
        if (FindSessionByPlayerId(ownerId) is { } session)
        {
            Serve(session);
        }

        return OwnShipFrame(ownerId);
    }

    /// <summary>Test seam: the level of a network aboard a player's parked ship (ship-local cell), or null.</summary>
    public bool? CrystalShipLevelAtForTest(string ownerId, Vector3i localCell)
        => ShipFrameForTest(ownerId) is { } frame ? InFrame(frame, () => CrystalLevelAt(localCell)) : null;

    /// <summary>Test seam: a device's own output aboard a player's parked ship, or null.</summary>
    public bool? CrystalShipDeviceOutputForTest(string ownerId, Vector3i localCell)
        => ShipFrameForTest(ownerId) is { } frame ? InFrame(frame, () => CrystalDeviceOutput(localCell)) : null;

    /// <summary>Test seam: the block key a registered net cell aboard a player's parked ship stands for, or null.</summary>
    public string? CrystalShipDeviceKeyForTest(string ownerId, Vector3i localCell)
        => ShipFrameForTest(ownerId) is { } frame && frame.Net.Cells.TryGetValue(localCell, out var c) ? c.BlockKey : null;

    /// <summary>Test seam: the block key standing in a cell of a player's parked ship right now (the live structure).</summary>
    public string? ParkedShipBlockKeyForTest(string ownerId, Vector3i localCell)
    {
        if (FindSessionByPlayerId(ownerId) is { } session)
        {
            Serve(session);
        }

        return _worlds.Active.LandedShips.TryGetValue(ownerId, out var rec) && rec.Placed ? _content.BlockById(rec.Structure.Get(localCell))?.Key : null;
    }

    /// <summary>Test seam: what the Crystal Net makes of a parked ship's own doors (the first of them).</summary>
    public DoorMode? ShipDoorModeForTest(string ownerId)
    {
        if (FindSessionByPlayerId(ownerId) is { } session)
        {
            Serve(session);
        }

        return _doors.FirstOrDefault(d => d.ShipOwner == ownerId)?.Mode;
    }

    /// <summary>Test seam: runs a device intent aboard the ship parked by <paramref name="shipOwner"/> (ship-local cell),
    /// as <paramref name="session"/>.</summary>
    public void SetCrystalShipDeviceForTest(PlayerSession session, string shipOwner, Vector3i localCell, int action, int mode = 0, string config = "")
    {
        Serve(session);
        if (OwnShipFrame(shipOwner) is { } frame)
        {
            HandleSetCrystalDevice(session, new SetCrystalDeviceIntent { Frame = frame.StructureId, X = localCell.X, Y = localCell.Y, Z = localCell.Z, Action = action, Mode = mode, Config = config });
        }
    }
}
