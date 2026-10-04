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

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The Crystal Net (#2045/#2046): crystal conduits carry a plain ON/OFF signal between the devices they touch.
/// <para>A <b>network</b> is a connected component of conduit + device cells (six-face adjacency); a network is ON
/// when any of its sources is ON. <b>Gates</b> (logic and timer blocks) are members of no network: they read the
/// networks on their input faces and drive the one on their output face, one beat later. <b>Sinks</b> (lamps,
/// sound devices, machines) follow their network's level; <b>doors</b> are entities over air cells and attach
/// through their neighbouring cells. Everything is evaluated on a slow logic beat (100 ms) and a slower sensor
/// beat (500 ms), only on occupied worlds — nothing runs while the player is away (the base wakes up with them).</para>
/// <para>State never rides in the voxel: a conduit is a plain block, a device's owner / mode / config is a
/// cell-keyed row (<see cref="StoredCrystalCell"/>, like beacons and beam pads), and the client learns which cells
/// glow through one <see cref="CrystalNetList"/> per change instead of a <see cref="BlockChanged"/> per cell.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Per-world Crystal Net state (lives on <see cref="LoadedWorld"/>, so each resident world ticks its own).</summary>
    internal sealed class CrystalNetState
    {
        public Dictionary<Vector3i, ServerCrystalCell> Cells { get; } = new();
        public Dictionary<int, CrystalNetwork> Nets { get; } = new();
        public int NextNetId { get; set; } = 1;
        public int NextDeviceId { get; set; } = 1;
        public double NextLogicBeat { get; set; }
        public double NextSensorBeat { get; set; }
        public bool NetListDirty { get; set; }

        /// <summary>The level of every network as this world's players were last told it — the logic beat sends the
        /// list again when a level differs. #2226: it is this world's own. It was one table for the server, keyed by
        /// net ids that start at 1 on every world: two occupied worlds read each other's levels, sent their whole
        /// list on every beat when the levels differed, and missed a real change when they happened to agree.</summary>
        public Dictionary<int, bool> LastSentLevel { get; } = new();

        public bool DeviceListDirty { get; set; }
        public bool Subscribed { get; set; }
        public bool ClonesRespawned { get; set; }

        /// <summary>Water spouts a conduit switched off: <c>PourFromSpout</c> skips them.</summary>
        public HashSet<Vector3i> ClosedSpouts { get; } = new();

        /// <summary>Energy gates a conduit switched ON: fauna may pass them.</summary>
        public HashSet<Vector3i> OpenGates { get; } = new();

        /// <summary>Callers pulsed recently: cell → uptime until which they call (#2057).</summary>
        public Dictionary<Vector3i, double> ActiveCallers { get; } = new();

        /// <summary>A machine moved or spawned a creature this beat: the creature list should go out.</summary>
        public bool CreatureListDirtyHint { get; set; }

        /// <summary>Sentry posts a conduit switched OFF: they hold their fire.</summary>
        public HashSet<Vector3i> DisabledSentries { get; } = new();

        /// <summary>#2261: heal tanks a conduit switched OFF: they do not heal.</summary>
        public HashSet<Vector3i> DisabledHealTanks { get; } = new();

        /// <summary>#2261: energy fence cells a conduit switched ON: fauna may pass them.</summary>
        public HashSet<Vector3i> OpenFences { get; } = new();

        /// <summary>#2266: the lifts of this world, by their motor's cell.</summary>
        public Dictionary<Vector3i, ServerLift> Lifts { get; } = new();

        public bool LiftListDirty { get; set; }
        public double LiftBroadcastIn { get; set; }

        /// <summary>#2267: each device as this world's players were last told it (a signature per cell) — the base of the
        /// device deltas.</summary>
        public Dictionary<Vector3i, string> LastSentDevice { get; } = new();
    }

    /// <summary>One network: its cells and its level. <see cref="Level"/> is re-derived every logic beat.</summary>
    internal sealed class CrystalNetwork
    {
        public int Id { get; init; }
        public HashSet<Vector3i> Cells { get; } = new();
        public bool Level { get; set; }

        /// <summary>#2092: the level of the previous logic beat — an edge device acts on Level && !PrevLevel.</summary>
        public bool PrevLevel { get; set; }
    }

    /// <summary>A conduit or device cell of the Crystal Net. Conduits carry no output; a device's <see cref="Output"/>
    /// is its own contribution (a switch's lever, a plate's occupancy, a gate's result), and <see cref="Applied"/>
    /// remembers the level an actuator last acted on.</summary>
    internal sealed class ServerCrystalCell
    {
        public int Id;
        public Vector3i Cell;
        public CrystalDeviceKind Kind;
        public string BlockKey = string.Empty;
        public string OwnerId = string.Empty;
        public int Mode;
        public string Config = string.Empty;
        public string Label = string.Empty;
        public int Yaw;                 // gates + watchers: the stored quarter turn (output / watched face)
        public int NetId;               // 0 = none (a gate, or an inert cell over the cap)
        public bool Inert;              // registered over a cap: it exists, it does nothing
        public bool Output;
        public double PulseUntil;       // pulse sources: fall back after this uptime
        public bool Applied;            // level actuators: the level last applied to the world
        public bool Synced;             // lamps: the world block was read once and matches Applied (#2096)
        public double LastActuated = -1000; // actuators: uptime of the last action (rate limit); "long ago" at start
        public bool Looping;            // sound devices: a loop is playing
        public TimerState? Timer;       // timer blocks
        public double NextBeat;         // machines: the next move / craft / mine
        public double Progress;         // machines: seconds into the current job
        public int Cursor;              // auto-drill: the next cell index of its volume
        public string CloneTag = string.Empty; // clone tank: the tag its clones carry (CombatEntity.CloneOf)
        public int CloneCount;          // clone tank: its clones that live, as last counted (#2214)
        public List<string>? CloneWaiting; // clone tank: clones its row lists that are not beside it yet — from the row's load to the world's first beat (#2214, #2226)
        public int ChoiceStamp;         // clone tank: a signature of what its owner may pick, as the sensor beat last saw it (#2214)
        public bool WaitTold;           // clone tank: the owner was told that the result waits for room (once per wait)
        public bool RemoteOn;           // signal receiver: what a remote control set (#2263) — persisted as remote=1
        public bool DiceLastInput;      // dice block: the input level of the previous beat (#2263)
        public int Extended;            // bridge motor: deck cells out (#2265) — persisted as ext=
        public bool Pushed;             // piston: its head is out (#2265) — persisted as out=1

        public bool IsConduit => Kind == CrystalDeviceKind.Conduit;
        public bool IsGate => CrystalNetRules.IsGate(Kind);
    }

    private CrystalNetState CrystalNet => _worlds.Active.CrystalNet;

    /// <summary>Test seam: every network of the active world as (id, level, cell count).</summary>
    public IReadOnlyList<(int Id, bool On, int Cells)> CrystalNetSnapshots
        => CrystalNet.Nets.Values.Select(n => (n.Id, n.Level, n.Cells.Count)).ToList();

    /// <summary>Test seam: a device's own output (a switch's state, a plate's occupancy) or null when no device sits there.</summary>
    public bool? CrystalDeviceOutput(Vector3i cell)
        => CrystalNet.Cells.TryGetValue(cell, out var c) && !c.IsConduit ? c.Output : null;

    /// <summary>Test seam: the level of the network a cell belongs to (null when the cell is no net cell / a gate).</summary>
    public bool? CrystalLevelAt(Vector3i cell)
        => CrystalNet.Cells.TryGetValue(cell, out var c) && c.NetId != 0 && CrystalNet.Nets.TryGetValue(c.NetId, out var n) ? n.Level : null;

    /// <summary>Test seam: a device's config line (<c>key=value;key=value</c>), or null when no device sits there.</summary>
    public string? CrystalDeviceConfig(Vector3i cell)
        => CrystalNet.Cells.TryGetValue(cell, out var c) && !c.IsConduit ? c.Config : null;

    /// <summary>Test seam: a device's picked mode, or null when no device sits there.</summary>
    public int? CrystalDeviceModeForTest(Vector3i cell)
        => CrystalNet.Cells.TryGetValue(cell, out var c) && !c.IsConduit ? c.Mode : null;

    /// <summary>Test seam: the number of registered cells (conduits + devices) in the active world.</summary>
    public int CrystalCellCount => CrystalNet.Cells.Count;

    /// <summary>Test seam: the stored quarter turn of a device (a gate's output / a watcher's eye), or null.</summary>
    public int? CrystalDeviceYaw(Vector3i cell)
        => CrystalNet.Cells.TryGetValue(cell, out var c) && !c.IsConduit ? c.Yaw : null;

    // ------------------------------------------------------------------------------------------------------
    // Registration: place / mine / load
    // ------------------------------------------------------------------------------------------------------

    /// <summary>A block was placed: a conduit or a new device always joins the net; a lamp or an existing port (a beacon,
    /// a beam pad, a sentry, a fire, a force field, a seat, …) joins only when a net cell already touches it — so an old
    /// base's lamps stay ordinary lamps until a conduit is laid beside them.</summary>
    private void OnCrystalBlockPlaced(PlayerSession session, Vector3i pos, BlockDefinition def, string label, int intentYaw)
    {
        var kind = CrystalKindAt(pos, def);
        if (kind == CrystalDeviceKind.None)
        {
            return;
        }

        if (CrystalNetRules.IsPassivePort(kind) && !HasCrystalNeighbour(pos) && !HasSpreadingNeighbour(pos, kind))
        {
            return; // an ordinary lamp / beacon / fire / … until a conduit meets it
        }

        if (CrystalNetRules.IsPlanetOnly(kind) && _world.Planet.Void)
        {
            SendVegaLine(session, "vega.sys.crystal_planet_only", 3);
            return; // a caller / clone tank on a station: creatures never tick there — the block stays decoration
        }

        if (CrystalNetRules.IsShipOnly(kind))
        {
            CrystalSystemHintOnce(session, "crystal_ship_only"); // #2268: the ship sensor reads a ship — here it is decoration
            return;
        }

        string owner = session.State.PlayerId;
        string clean = string.IsNullOrEmpty(label) ? string.Empty : (ScreenPlayerName(session, SanitizeBeamName(label), "crystal") ?? string.Empty);

        // A directional device points the way the player looked when placing it (#2093): the quarter turn comes from the
        // intent (the client's rotate key; #2267: or up / down) or, failing that, from the player's facing — and rides in
        // the config row.
        int yaw = CrystalNetRules.ClampYaw(intentYaw >= 0 ? intentYaw : (((int)Math.Round(session.State.Yaw / 90.0) % 4) + 4) % 4);
        string config = CrystalNetRules.IsDirectional(kind) ? "yaw=" + yaw : string.Empty;
        if (kind == CrystalDeviceKind.BridgeMotor)
        {
            config = CrystalConfigWith(config, "len", CrystalNetRules.BridgeDefaultLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var cell = RegisterCrystalCell(pos, kind, def.Key, owner, mode: 0, config, clean, yaw, persist: true);
        if (cell.Inert)
        {
            SendVegaLine(session, "vega.sys.crystal_cap", 3);
        }
        else if (kind == CrystalDeviceKind.Conduit)
        {
            CrystalSystemHintOnce(session, "crystal_first"); // #2254: once per PLAYER — it used to be once per world
        }

        DiscoverCrystalNeighbours(pos, owner);
        OnCrystalCellPlacedHints(session, cell); // #2257: this player's first door on a wire, first arrow, …
    }

    /// <summary>The kind a placed cell plays: by its block key (and its category, for lamps), or — #2261 — a seat by its
    /// form (a chair or a bench is any material formed into a seat).</summary>
    private CrystalDeviceKind CrystalKindAt(Vector3i pos, BlockDefinition? def)
    {
        var kind = CrystalNetRules.KindOf(def);
        if (kind == CrystalDeviceKind.None && def is not null && SeatStillThere(pos))
        {
            return CrystalDeviceKind.Seat;
        }

        return kind;
    }

    /// <summary>A one-time line of VEGA's, sent as a system line (kind 3): a player who switched VEGA's hints off still
    /// learns why something does nothing — a muted hint would burn the flag unseen (the #2219 pattern).</summary>
    private void CrystalSystemHintOnce(PlayerSession session, string hintId)
    {
        var p = session.State;
        if (p.Milestones.Add("vega:hint:" + hintId))
        {
            _repo.SavePlayer(p);
            SendVegaLine(session, "vega.hint." + hintId, 3);
        }
    }

    /// <summary>A block was mined / blasted: its cell leaves the net (row deleted, network split), and the lamps or ports
    /// left without a real net cell beside them — #2261: together with every port they reach through other ports, so a
    /// field wall whose conduit was mined dissolves as a whole — go back to being ordinary blocks.</summary>
    private void OnCrystalBlockRemoved(Vector3i pos, BlockDefinition def)
    {
        if (!CrystalNet.Cells.TryGetValue(pos, out var cell))
        {
            return;
        }

        _ = def;
        UnregisterCrystalCell(cell, relight: false);
        foreach (var face in CrystalNetRules.Faces)
        {
            var n = pos + face;
            if (CrystalNet.Cells.TryGetValue(n, out var other) && CrystalNetRules.IsPassivePort(other.Kind) && !HasCrystalNeighbour(n, excludePassive: true))
            {
                DissolvePortCluster(other); // an orphaned lamp lights up again, an orphaned sentry fires again
            }
        }
    }

    /// <summary>#2261: takes a port out of the net together with every port it reaches through other ports — when none of
    /// them touches a real net cell any more.</summary>
    private void DissolvePortCluster(ServerCrystalCell start)
    {
        var state = CrystalNet;
        var cluster = new List<ServerCrystalCell>();
        var seen = new HashSet<Vector3i> { start.Cell };
        var queue = new Queue<ServerCrystalCell>();
        queue.Enqueue(start);
        while (queue.Count > 0 && cluster.Count <= CrystalNetRules.MaxCellsPerNet)
        {
            var c = queue.Dequeue();
            cluster.Add(c);
            foreach (var face in CrystalNetRules.Faces)
            {
                var n = c.Cell + face;
                if (!state.Cells.TryGetValue(n, out var other) || other.Inert)
                {
                    continue;
                }

                if (!CrystalNetRules.IsPassivePort(other.Kind))
                {
                    return; // the cluster still touches a real net cell: it stays wired
                }

                if (seen.Add(n))
                {
                    queue.Enqueue(other);
                }
            }
        }

        foreach (var c in cluster)
        {
            if (state.Cells.ContainsKey(c.Cell))
            {
                UnregisterCrystalCell(c, relight: true);
            }
        }
    }

    /// <summary>Whether a net cell touches this cell. With <paramref name="excludePassive"/> only conduits and real
    /// devices count — two lamps beside each other do not keep each other in the net.</summary>
    private bool HasCrystalNeighbour(Vector3i pos, bool excludePassive = false)
    {
        foreach (var face in CrystalNetRules.Faces)
        {
            if (CrystalNet.Cells.TryGetValue(pos + face, out var c) && !c.Inert && (!excludePassive || !CrystalNetRules.IsPassivePort(c.Kind)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>#2261: whether a wired cell of the same spreading kind touches this cell — a field cell placed into a wired
    /// field wall joins it.</summary>
    private bool HasSpreadingNeighbour(Vector3i pos, CrystalDeviceKind kind)
    {
        if (!CrystalNetRules.SpreadsToOwnKind(kind))
        {
            return false;
        }

        foreach (var face in CrystalNetRules.Faces)
        {
            if (CrystalNet.Cells.TryGetValue(pos + face, out var c) && !c.Inert && c.Kind == kind)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Registers the lamps and ports beside a fresh net cell (6 block reads, loaded chunks only). #2261: a force
    /// field or an energy fence carries the join on through its own kind, so a wired wall switches as one (bounded by the
    /// net cap).</summary>
    private void DiscoverCrystalNeighbours(Vector3i pos, string owner)
    {
        var queue = new Queue<Vector3i>();
        queue.Enqueue(pos);
        int spread = 0;
        while (queue.Count > 0)
        {
            var from = queue.Dequeue();
            bool viaSpreader = from != pos;
            var fromKind = viaSpreader && CrystalNet.Cells.TryGetValue(from, out var via) ? via.Kind : CrystalDeviceKind.None;
            foreach (var face in CrystalNetRules.Faces)
            {
                var n = from + face;
                if (CrystalNet.Cells.ContainsKey(n))
                {
                    continue;
                }

                var def = _content.BlockById(_world.GetBlockIfLoaded(n));
                if (def is null)
                {
                    continue;
                }

                var kind = CrystalKindAt(n, def);
                if (!CrystalNetRules.IsPassivePort(kind) || (viaSpreader && kind != fromKind))
                {
                    continue; // beyond the fresh cell only a spreading kind carries the join on, and only to its own kind
                }

                var cell = RegisterCrystalCell(n, kind, def.Key, owner, 0, string.Empty, string.Empty, 0, persist: true);
                if (!cell.Inert && CrystalNetRules.SpreadsToOwnKind(kind) && ++spread < CrystalNetRules.MaxCellsPerNet)
                {
                    queue.Enqueue(n);
                }
            }
        }
    }

    private ServerCrystalCell RegisterCrystalCell(Vector3i pos, CrystalDeviceKind kind, string blockKey, string owner, int mode, string config, string label, int yaw, bool persist)
    {
        var state = CrystalNet;
        var cell = new ServerCrystalCell
        {
            Id = state.NextDeviceId++,
            Cell = pos,
            Kind = kind,
            BlockKey = blockKey,
            OwnerId = owner,
            Mode = mode,
            Config = config,
            Label = label,
            Yaw = CrystalNetRules.ClampYaw(yaw),
        };
        if (kind == CrystalDeviceKind.TimerBlock)
        {
            cell.Timer = new TimerState();
        }

        if (kind is CrystalDeviceKind.AutoDrill or CrystalDeviceKind.Seat or CrystalDeviceKind.Bed && CrystalConfigValue(cell.Config, "key") is null)
        {
            // A multi-key kind remembers WHICH block it is: a Mk3 drill must not come back from its row as a Mk1.
            cell.Config = CrystalConfigWith(cell.Config, "key", blockKey);
        }

        // Level sinks start in their world state: a sentry fires, a spout pours, a heal tank heals — so an OFF network is a
        // change the first beat applies (Applied = the level the world currently shows). A lamp or a twinned block (phase
        // block, trapdoor, field, fire) is NOT assumed (#2096): it may have been saved as either twin, so the first beat
        // reads the block that actually stands there (Synced).
        cell.Applied = kind is CrystalDeviceKind.Sentry or CrystalDeviceKind.Spout or CrystalDeviceKind.HealTank;

        if (kind == CrystalDeviceKind.Switch)
        {
            cell.Output = mode == 1; // a switch keeps its lever across reloads (mode 1 = ON)
        }

        // #2263 / #2265: what a remote set, how far a bridge is out, whether a piston's head is out — from the row.
        cell.RemoteOn = kind == CrystalDeviceKind.SignalReceiver && CrystalConfigValue(cell.Config, "remote") == "1";
        cell.Extended = kind == CrystalDeviceKind.BridgeMotor ? Math.Max(0, CrystalConfigInt(cell.Config, "ext", 0)) : 0;
        cell.Pushed = kind == CrystalDeviceKind.Piston && CrystalConfigValue(cell.Config, "out") == "1";
        if (cell.Pushed)
        {
            cell.Applied = true; // a piston saved out: the first OFF beat pulls its head back in
        }

        cell.Inert = OverCrystalCap(cell);
        state.Cells[pos] = cell;
        if (!cell.Inert && !cell.IsGate)
        {
            JoinCrystalNet(cell);
        }

        if (kind == CrystalDeviceKind.CloneTank)
        {
            InitCloneTank(cell); // #2214: its clones' tag and list; a tank that was growing starts its wait over
        }

        if (kind == CrystalDeviceKind.LiftMotor && !cell.Inert)
        {
            LiftOf(cell); // #2266: the platform appears resting on the motor (or where the row says it waited)
        }

        if (persist)
        {
            SaveCrystalCell(cell);
        }

        state.DeviceListDirty = true;
        state.NetListDirty = true;
        return cell;
    }

    /// <summary>The caps that keep a base from turning a beat into a sweep: networks and sensors per world (#2267: and at
    /// most half of each per player, so one builder cannot lock everyone else out), and the per-owner machine caps. A
    /// world circuit (#2260) has a small budget of its own and never counts against the players. A cell over a cap
    /// registers inert — it exists, it is listed, it does nothing — and the player is told; mining something frees the
    /// slot.</summary>
    private bool OverCrystalCap(ServerCrystalCell cell)
    {
        var state = CrystalNet;
        bool brandNewNet = !cell.IsGate && !HasCrystalNeighbour(cell.Cell);
        if (CrystalNetRules.IsWorldOwner(cell.OwnerId))
        {
            if (CrystalNetRules.IsSensor(cell.Kind)
                && state.Cells.Values.Count(c => !c.Inert && CrystalNetRules.IsSensor(c.Kind) && CrystalNetRules.IsWorldOwner(c.OwnerId)) >= CrystalNetRules.MaxWorldCircuitSensors)
            {
                return true;
            }

            return brandNewNet && CountNets(owner: null, worldOnly: true) >= CrystalNetRules.MaxWorldCircuitNets;
        }

        if (CrystalNetRules.IsSensor(cell.Kind))
        {
            int players = 0, mine = 0;
            foreach (var c in state.Cells.Values)
            {
                if (c.Inert || !CrystalNetRules.IsSensor(c.Kind) || CrystalNetRules.IsWorldOwner(c.OwnerId))
                {
                    continue;
                }

                players++;
                if (c.OwnerId == cell.OwnerId)
                {
                    mine++;
                }
            }

            if (players >= CrystalNetRules.MaxSensorsPerWorld || mine >= CrystalNetRules.MaxSensorsPerPlayer)
            {
                return true;
            }
        }

        int ownerCap = cell.Kind switch
        {
            CrystalDeviceKind.AutoDrill => CrystalNetRules.MaxAutoDrillsPerOwner,
            CrystalDeviceKind.MatterSender => CrystalNetRules.MaxMatterSendersPerOwner,
            CrystalDeviceKind.Fabricator => CrystalNetRules.MaxFabricatorsPerOwner,
            CrystalDeviceKind.CloneTank => CrystalNetRules.MaxCloneTanksPerOwner,
            CrystalDeviceKind.DrillLaser => CrystalNetRules.MaxDrillLasersPerOwner,
            CrystalDeviceKind.BridgeMotor => CrystalNetRules.MaxBridgeMotorsPerOwner,
            CrystalDeviceKind.Piston => CrystalNetRules.MaxPistonsPerOwner,
            CrystalDeviceKind.LiftMotor => CrystalNetRules.MaxLiftsPerOwner,
            CrystalDeviceKind.SignalSender => CrystalNetRules.MaxSignalSendersPerOwner,
            _ => 0,
        };
        if (ownerCap > 0 && state.Cells.Values.Count(c => !c.Inert && c.Kind == cell.Kind && c.OwnerId == cell.OwnerId) >= ownerCap)
        {
            return true;
        }

        // A brand-new network over the world cap, or over this player's share of it (#2267).
        return brandNewNet
               && (CountNets(owner: null, worldOnly: false) >= CrystalNetRules.MaxNetsPerWorld
                   || (cell.OwnerId.Length > 0 && CountNets(cell.OwnerId, worldOnly: false) >= CrystalNetRules.MaxNetsPerPlayer));
    }

    /// <summary>The networks of the world's players (<paramref name="owner"/> null) or of one player — a network counts for
    /// every owner of a real cell in it — or, with <paramref name="worldOnly"/>, the networks of world circuits (#2260).</summary>
    private int CountNets(string? owner, bool worldOnly)
    {
        var state = CrystalNet;
        int count = 0;
        foreach (var net in state.Nets.Values)
        {
            bool world = false, player = false, match = false;
            foreach (var pos in net.Cells)
            {
                if (!state.Cells.TryGetValue(pos, out var c) || CrystalNetRules.IsPassivePort(c.Kind))
                {
                    continue;
                }

                if (CrystalNetRules.IsWorldOwner(c.OwnerId))
                {
                    world = true;
                }
                else
                {
                    player = true;
                    match |= owner is null || c.OwnerId == owner;
                }
            }

            if (worldOnly ? world && !player : player && match)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Adds a non-gate cell to the net: the networks of its neighbours merge into one (the smallest id
    /// survives), or a new network is opened. A merge over the cell cap leaves the cell inert instead.</summary>
    private void JoinCrystalNet(ServerCrystalCell cell)
    {
        var state = CrystalNet;
        var neighbours = new List<CrystalNetwork>();
        foreach (var face in CrystalNetRules.Faces)
        {
            if (state.Cells.TryGetValue(cell.Cell + face, out var n) && n.NetId != 0 && state.Nets.TryGetValue(n.NetId, out var net) && !neighbours.Contains(net))
            {
                neighbours.Add(net);
            }
        }

        int total = 1 + neighbours.Sum(n => n.Cells.Count);
        if (total > CrystalNetRules.MaxCellsPerNet)
        {
            cell.Inert = true;
            return;
        }

        CrystalNetwork target;
        if (neighbours.Count == 0)
        {
            target = new CrystalNetwork { Id = state.NextNetId++ };
            state.Nets[target.Id] = target;
        }
        else
        {
            neighbours.Sort((a, b) => a.Id.CompareTo(b.Id));
            target = neighbours[0];
            for (int i = 1; i < neighbours.Count; i++)
            {
                foreach (var c in neighbours[i].Cells)
                {
                    target.Cells.Add(c);
                    state.Cells[c].NetId = target.Id;
                }

                state.Nets.Remove(neighbours[i].Id);
            }
        }

        target.Cells.Add(cell.Cell);
        cell.NetId = target.Id;
    }

    /// <summary>Takes a cell out of the net. Its network is re-flooded from the remaining cells; every component
    /// beyond the first becomes a network of its own (a cut conduit splits a line in two).</summary>
    private void UnregisterCrystalCell(ServerCrystalCell cell, bool relight)
    {
        var state = CrystalNet;
        StopCrystalActuator(cell, relight);
        state.Cells.Remove(cell.Cell);
        _repo.DeleteCrystalCell(_world.LocationId, cell.Cell.X, cell.Cell.Y, cell.Cell.Z);
        state.ClosedSpouts.Remove(cell.Cell);
        state.OpenGates.Remove(cell.Cell);
        state.DisabledSentries.Remove(cell.Cell);
        state.DisabledHealTanks.Remove(cell.Cell);
        state.OpenFences.Remove(cell.Cell);
        if (cell.NetId != 0 && state.Nets.TryGetValue(cell.NetId, out var net))
        {
            net.Cells.Remove(cell.Cell);
            if (net.Cells.Count == 0)
            {
                state.Nets.Remove(net.Id);
            }
            else
            {
                SplitCrystalNet(net);
            }
        }

        state.DeviceListDirty = true;
        state.NetListDirty = true;
    }

    private void SplitCrystalNet(CrystalNetwork net)
    {
        var state = CrystalNet;
        var remaining = new HashSet<Vector3i>(net.Cells);
        bool first = true;
        var stack = new Stack<Vector3i>();
        while (remaining.Count > 0)
        {
            var seed = remaining.First();
            var component = new HashSet<Vector3i>();
            stack.Push(seed);
            remaining.Remove(seed);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                component.Add(c);
                foreach (var face in CrystalNetRules.Faces)
                {
                    var n = c + face;
                    if (remaining.Remove(n))
                    {
                        stack.Push(n);
                    }
                }
            }

            if (first)
            {
                first = false;
                if (component.Count == net.Cells.Count)
                {
                    return; // still one piece
                }

                net.Cells.Clear();
                foreach (var c in component)
                {
                    net.Cells.Add(c);
                }
            }
            else
            {
                var fresh = new CrystalNetwork { Id = state.NextNetId++ };
                state.Nets[fresh.Id] = fresh;
                foreach (var c in component)
                {
                    fresh.Cells.Add(c);
                    state.Cells[c].NetId = fresh.Id;
                }
            }
        }
    }

    private void SaveCrystalCell(ServerCrystalCell cell)
        => _repo.SaveCrystalCell(new StoredCrystalCell
        {
            Planet = _world.LocationId,
            X = cell.Cell.X,
            Y = cell.Cell.Y,
            Z = cell.Cell.Z,
            Kind = cell.Kind.ToString(),
            OwnerId = cell.OwnerId,
            Mode = cell.Mode,
            Config = cell.Config,
            Label = cell.Label,
        });

    /// <summary>Rebuilds the active world's Crystal Net from its rows alone (no chunk is touched): every cell comes
    /// back with its owner / mode / config, the networks are re-flooded, runtime state (pulses, timers, loops)
    /// starts OFF — a switch keeps its lever. Idempotent: clears first, so it is safe on every world activation.</summary>
    private void LoadCrystalNet()
    {
        var state = CrystalNet;
        state.Cells.Clear();
        state.Nets.Clear();
        state.ClosedSpouts.Clear();
        state.OpenGates.Clear();
        state.DisabledSentries.Clear();
        state.DisabledHealTanks.Clear();
        state.OpenFences.Clear();
        state.Lifts.Clear();
        state.LiftListDirty = true;
        if (state.NextDeviceId < 1)
        {
            state.NextDeviceId = 1;
        }

        // #2214: which machine of an owner is over a cap follows the order the cells register in — and the rows come
        // back in the store's order (by coordinate), not in the order they were placed. A clone tank with a job
        // running or with clones in its list goes first, so a reload never gives its place inside the cap to an idle
        // tank and leaves the job hanging. The sort is stable: every other row keeps its place.
        var rows = _repo.ListCrystalCells(_world.LocationId).OrderByDescending(CloneTankRowInUse);
        foreach (var row in rows)
        {
            if (!Enum.TryParse<CrystalDeviceKind>(row.Kind, out var kind) || kind == CrystalDeviceKind.None)
            {
                continue;
            }

            var pos = new Vector3i(row.X, row.Y, row.Z);
            string blockKey = CrystalConfigValue(row.Config, "key") ?? KeyForKind(kind);
            int yaw = CrystalConfigInt(row.Config, "yaw", 0);
            RegisterCrystalCell(pos, kind, blockKey, row.OwnerId, row.Mode, row.Config, row.Label, yaw, persist: false);
        }

        MigrateCrystalPairs(); // #2252: an old numeric pair= becomes the partner's cell

        if (!state.Subscribed)
        {
            state.Subscribed = true;
            _world.BlockSet += OnCrystalWatchedCellChanged; // #2050: watchers pulse when the cell in front of them changes
        }

        state.ClonesRespawned = false;
        state.NextLogicBeat = _uptime;
        state.NextSensorBeat = _uptime;
    }

    /// <summary>Whether a stored row is a clone tank that is growing or has clones in its list.</summary>
    private static bool CloneTankRowInUse(StoredCrystalCell row)
        => row.Kind == nameof(CrystalDeviceKind.CloneTank)
           && (CrystalConfigValue(row.Config, "growing") == "1" || TankClones(row.Config).Count > 0);

    /// <summary>The block key a kind places when the row carries none (old rows, the single-key kinds).</summary>
    private static string KeyForKind(CrystalDeviceKind kind) => kind switch
    {
        CrystalDeviceKind.Conduit => CrystalNetRules.ConduitBlockKey,
        CrystalDeviceKind.RailStop => "rail_stop", // #2113
        CrystalDeviceKind.Switch => "crystal_switch",
        CrystalDeviceKind.Button => "crystal_button",
        CrystalDeviceKind.StepPlate => "step_plate",
        CrystalDeviceKind.ProximitySensor => "proximity_sensor",
        CrystalDeviceKind.DaylightSensor => "daylight_sensor",
        CrystalDeviceKind.StorageSensor => "storage_sensor",
        CrystalDeviceKind.Watcher => "watcher",
        CrystalDeviceKind.LogicBlock => "logic_block",
        CrystalDeviceKind.TimerBlock => "timer_block",
        CrystalDeviceKind.AlarmSiren => "alarm_siren",
        CrystalDeviceKind.Chime => "chime",
        CrystalDeviceKind.Horn => "horn",
        CrystalDeviceKind.MelodyBlock => "melody_block",
        CrystalDeviceKind.Announcer => "announcer",
        CrystalDeviceKind.Fabricator => "fabricator",
        CrystalDeviceKind.Caller => "caller",
        CrystalDeviceKind.CloneTank => "clone_tank",
        CrystalDeviceKind.AutoDrill => "auto_drill_1",
        CrystalDeviceKind.DrillLaser => "drill_laser",
        CrystalDeviceKind.MatterSender => "matter_sender",
        CrystalDeviceKind.MatterReceiver => "matter_receiver",
        CrystalDeviceKind.Beacon => "radio_beacon",
        CrystalDeviceKind.BeamPad => "beam_block",
        CrystalDeviceKind.Sentry => "sentry_post",
        CrystalDeviceKind.Thumper => "thumper",
        CrystalDeviceKind.Spout => "water_spout",
        CrystalDeviceKind.EnergyGate => "energy_gate",
        CrystalDeviceKind.HydroTray => "hydro_tray",
        CrystalDeviceKind.DeviceEye => "device_eye",
        CrystalDeviceKind.PhaseBlock => "phase_block",
        CrystalDeviceKind.Trapdoor => "trapdoor",
        CrystalDeviceKind.ForceField => "force_field",
        CrystalDeviceKind.EnergyFence => "energy_fence",
        CrystalDeviceKind.Campfire => "campfire",
        CrystalDeviceKind.Forge => "forge",
        CrystalDeviceKind.HealTank => "heal_tank",
        CrystalDeviceKind.FlowerPot => "flower_pot",
        CrystalDeviceKind.Bed => "bed",
        CrystalDeviceKind.BridgeMotor => "bridge_motor",
        CrystalDeviceKind.Piston => "piston",
        CrystalDeviceKind.LiftMotor => "lift_motor",
        CrystalDeviceKind.LiftStop => "lift_stop",
        CrystalDeviceKind.SignalDisplay => "signal_display",
        CrystalDeviceKind.DiceBlock => "dice_block",
        CrystalDeviceKind.SignalSender => "signal_sender",
        CrystalDeviceKind.SignalReceiver => "signal_receiver",
        CrystalDeviceKind.EnvironmentSensor => "environment_sensor",
        CrystalDeviceKind.ShipSensor => "ship_sensor",
        _ => string.Empty,
    };

    // ------------------------------------------------------------------------------------------------------
    // Pairs (#2252): a partner is named by its CELL — device and beam ids are handed out afresh on every load.
    // ------------------------------------------------------------------------------------------------------

    /// <summary>The cell a <c>pair=</c> names (<c>x,y,z</c>), or null for none / an old numeric id.</summary>
    private static Vector3i? CrystalPairCell(string config)
    {
        string? v = CrystalConfigValue(config, "pair");
        if (v is null)
        {
            return null;
        }

        var parts = v.Split(',');
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return parts.Length == 3
               && int.TryParse(parts[0], System.Globalization.NumberStyles.Integer, inv, out int x)
               && int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, inv, out int y)
               && int.TryParse(parts[2], System.Globalization.NumberStyles.Integer, inv, out int z)
            ? new Vector3i(x, y, z)
            : null;
    }

    /// <summary>A cell as a <c>pair=</c> value.</summary>
    private static string CrystalPairValue(Vector3i cell)
        => string.Join(",", cell.X.ToString(System.Globalization.CultureInfo.InvariantCulture), cell.Y.ToString(System.Globalization.CultureInfo.InvariantCulture), cell.Z.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>#2252: a row written before pairs were cells holds the partner's id of that day. It is resolved once against
    /// the ids of this load (best effort: the order the rows come back in is the order they were numbered in most saves)
    /// and rewritten as the partner's cell; when nothing fits, the pair is dropped and the sender's light tells the
    /// player to pick the partner again.</summary>
    private void MigrateCrystalPairs()
    {
        foreach (var c in CrystalNet.Cells.Values.ToList())
        {
            string? raw = CrystalConfigValue(c.Config, "pair");
            if (raw is null || raw.Length == 0 || raw.Contains(','))
            {
                continue;
            }

            Vector3i? partner = null;
            if (int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int id))
            {
                partner = c.Kind switch
                {
                    CrystalDeviceKind.MatterSender => CrystalNet.Cells.Values.FirstOrDefault(d => d.Id == id && d.Kind == CrystalDeviceKind.MatterReceiver)?.Cell,
                    CrystalDeviceKind.BeamPad => _beams.FirstOrDefault(b => b.Id == id)?.Cell,
                    _ => null,
                };
            }

            c.Config = partner is { } p ? CrystalConfigWith(c.Config, "pair", CrystalPairValue(p)) : CrystalConfigWithout(c.Config, new[] { "pair" });
            SaveCrystalCell(c);
        }
    }

    // ------------------------------------------------------------------------------------------------------
    // Config helpers ("key=value;key=value")
    // ------------------------------------------------------------------------------------------------------

    private static string? CrystalConfigValue(string config, string key)
    {
        if (string.IsNullOrEmpty(config))
        {
            return null;
        }

        foreach (var part in config.Split(';'))
        {
            int eq = part.IndexOf('=');
            if (eq > 0 && string.CompareOrdinal(part, 0, key, 0, eq) == 0 && key.Length == eq)
            {
                return part.Substring(eq + 1);
            }
        }

        return null;
    }

    private static int CrystalConfigInt(string config, string key, int fallback)
        => int.TryParse(CrystalConfigValue(config, key), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int v) ? v : fallback;

    private static double CrystalConfigDouble(string config, string key, double fallback)
        => double.TryParse(CrystalConfigValue(config, key), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : fallback;

    private static string CrystalConfigWith(string config, string key, string value)
    {
        var parts = new List<string>();
        bool set = false;
        if (!string.IsNullOrEmpty(config))
        {
            foreach (var part in config.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0 && part.Substring(0, eq) == key)
                {
                    parts.Add(key + "=" + value);
                    set = true;
                }
                else if (part.Length > 0)
                {
                    parts.Add(part);
                }
            }
        }

        if (!set)
        {
            parts.Add(key + "=" + value);
        }

        return string.Join(";", parts);
    }

    /// <summary>A config line without the given keys — what the client sent, before it is cleaned and cut to length
    /// (bounded first, so a hostile line costs nothing to split).</summary>
    private static string CrystalConfigWithout(string? config, string[] keys)
    {
        if (string.IsNullOrEmpty(config))
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var part in (config!.Length > 1024 ? config.Substring(0, 1024) : config).Split(';'))
        {
            int eq = part.IndexOf('=');
            if (part.Length > 0 && (eq <= 0 || Array.IndexOf(keys, part.Substring(0, eq)) < 0))
            {
                parts.Add(part);
            }
        }

        return string.Join(";", parts);
    }

    /// <summary>A config line the client typed: printable, short, no separators that would break the line.</summary>
    private static string SanitizeCrystalConfig(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var clean = StripControlChars(raw).Replace('\n', ' ').Replace('|', ' ');
        return clean.Length > 96 ? clean.Substring(0, 96) : clean;
    }

    // ------------------------------------------------------------------------------------------------------
    // Intents
    // ------------------------------------------------------------------------------------------------------

    /// <summary>The player toggles a switch, presses a button / starts a machine, or configures a device they look
    /// at. Reach is checked like every block action; configuring needs ownership (or an alliance, or admin).</summary>
    private void HandleSetCrystalDevice(PlayerSession session, SetCrystalDeviceIntent intent)
    {
        var pos = new Vector3i(intent.X, intent.Y, intent.Z);
        if (!CrystalNet.Cells.TryGetValue(pos, out var cell) || cell.IsConduit)
        {
            Reject(session, "crystal", "@srv.crystal.gone");
            return;
        }

        if (!WithinReach(session.State, pos))
        {
            Reject(session, "crystal", "@out_of_reach");
            return;
        }

        string me = session.State.PlayerId;

        // #2256: only the owner and their alliance (and an admin) OPERATE a device — a stranger can no longer flip someone's
        // alarm off. A world circuit (#2260) is usable by everyone; configuring it is the admin's.
        bool configure = intent.Action is 2 or 3;
        if (configure ? !CanConfigureCrystal(cell, me, session.State.IsAdmin) : !CanOperateCrystal(cell, me, session.State.IsAdmin))
        {
            Reject(session, "crystal", "@srv.crystal.owner_only");
            OnCrystalRefusedHint(session); // #2257: the first refusal explains the rule once
            return;
        }

        switch (intent.Action)
        {
            case 0 when cell.Kind == CrystalDeviceKind.Switch:
                cell.Output = !cell.Output;
                cell.Mode = cell.Output ? 1 : 0;
                SaveCrystalCell(cell);
                CrystalNet.DeviceListDirty = true;
                BroadcastToWorld(new SoundFx { SoundId = "crystal_switch", X = pos.X + 0.5f, Y = pos.Y + 0.5f, Z = pos.Z + 0.5f, SourceId = cell.Id });
                return;
            case 1 when cell.Kind == CrystalDeviceKind.Button:
                PulseCrystalCell(cell);
                BroadcastToWorld(new SoundFx { SoundId = "crystal_button", X = pos.X + 0.5f, Y = pos.Y + 0.5f, Z = pos.Z + 0.5f, SourceId = cell.Id });
                return;
            case 1 when cell.Kind is CrystalDeviceKind.Fabricator or CrystalDeviceKind.MatterSender or CrystalDeviceKind.CloneTank
                or CrystalDeviceKind.AutoDrill or CrystalDeviceKind.Caller or CrystalDeviceKind.Thumper or CrystalDeviceKind.HydroTray
                or CrystalDeviceKind.DrillLaser or CrystalDeviceKind.RailStop // #2113: a press on the stop departs the train
                or CrystalDeviceKind.FlowerPot or CrystalDeviceKind.LiftMotor or CrystalDeviceKind.LiftStop: // #2261 / #2266
                TriggerCrystalMachine(cell, session); // a manual start, same path as a signal's rising edge
                return;
            case 3 when CrystalNetRules.IsDirectional(cell.Kind):
                TurnCrystalDevice(cell); // #2267: turn after placing — no more mining and re-placing
                return;
            case 4 when cell.Kind == CrystalDeviceKind.SignalDisplay:
                cell.Config = CrystalConfigWith(cell.Config, "n", "0"); // #2263: the counter starts over
                SaveCrystalCell(cell);
                CrystalNet.DeviceListDirty = true;
                return;
            case 2:

                int modes = CrystalNetRules.ModeCount(cell.Kind);
                if (modes > 0)
                {
                    cell.Mode = Math.Max(0, Math.Min(modes - 1, intent.Mode));
                }

                // #2214: a tank's own keys leave what the client sent BEFORE the length cut — the client echoes the whole
                // config, and the list of living clones must never push the player's own choice (sp, x) over the edge.
                string fresh = SanitizeCrystalConfig(cell.Kind == CrystalDeviceKind.CloneTank
                    ? CrystalConfigWithout(intent.Config, TankOwnedKeys)
                    : intent.Config);
                if (CrystalNetRules.IsDirectional(cell.Kind))
                {
                    // The orientation is not the configure path's to overwrite (#2267: the menu's Turn button is).
                    fresh = CrystalConfigWith(fresh, "yaw", cell.Yaw.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                // Nor is what the server keeps in the row: a drill's tier, a laser's depth, a bridge's extension, a piston's
                // head, a remote's state, a counter's count (#2263 / #2265).
                foreach (string owned in CrystalServerKeys)
                {
                    fresh = CrystalConfigValue(cell.Config, owned) is { } kept
                        ? CrystalConfigWith(fresh, owned, kept)
                        : CrystalConfigWithout(fresh, new[] { owned });
                }

                if (cell.Kind == CrystalDeviceKind.CloneTank)
                {
                    // #2207: what a tank is growing and which clones it holds is the server's to say — a client
                    // that set it could release a species it never paid for.
                    foreach (string owned in TankOwnedKeys)
                    {
                        string? kept = CrystalConfigValue(cell.Config, owned);
                        if (kept is not null || CrystalConfigValue(fresh, owned) is not null)
                        {
                            fresh = CrystalConfigWith(fresh, owned, kept ?? string.Empty);
                        }
                    }
                }

                cell.Config = fresh;
                if (intent.Label is { Length: > 0 })
                {
                    if (ScreenPlayerName(session, SanitizeBeamName(intent.Label), "crystal") is not { } screened)
                    {
                        return; // refused by the content screen (#1221) — the player has been told
                    }

                    cell.Label = screened;
                }

                cell.Timer?.Reset();
                if (cell.Kind == CrystalDeviceKind.Switch)
                {
                    cell.Output = cell.Mode == 1;
                }

                SaveCrystalCell(cell);
                CrystalNet.DeviceListDirty = true;
                return;
            default:
                return;
        }
    }

    /// <summary>Config keys only the server writes (the configure path keeps their server value, whatever the client sent).</summary>
    private static readonly string[] CrystalServerKeys = { "key", "depth", "ext", "out", "remote", "n" };

    /// <summary>Who may change a device's settings: its owner, an ally (alliance or crew), an admin; an ownerless device
    /// (the intercity rail stops) anyone. A world circuit (#2260) only an admin — a vault puzzle must not be re-wired.</summary>
    private bool CanConfigureCrystal(ServerCrystalCell cell, string playerId, bool admin)
        => admin || cell.OwnerId.Length == 0 || cell.OwnerId == playerId || AreAllied(cell.OwnerId, playerId);

    /// <summary>#2256: who may OPERATE a device (toggle, press, start, call a lift, use a remote): its owner, an ally, an
    /// admin — and everyone on an ownerless device or a world circuit (#2260), whose puzzles must be solvable.</summary>
    private bool CanOperateCrystal(ServerCrystalCell cell, string playerId, bool admin)
        => CanConfigureCrystal(cell, playerId, admin) || CrystalNetRules.IsWorldOwner(cell.OwnerId);

    /// <summary>#2267: the menu's Turn button — the next of the six directions (four quarter turns, up, down). The gate's
    /// timer starts over; a piston or bridge that is out pulls back first (it pushes the new way on the next ON beat).</summary>
    private void TurnCrystalDevice(ServerCrystalCell cell)
    {
        if (cell.Kind == CrystalDeviceKind.Piston && cell.Pushed)
        {
            PistonRetract(cell, sticky: false);
        }

        if (cell.Kind == CrystalDeviceKind.BridgeMotor && cell.Extended > 0)
        {
            BridgeRetractAll(cell);
        }

        cell.Yaw = CrystalNetRules.NextYaw(cell.Yaw);
        cell.Config = CrystalConfigWith(cell.Config, "yaw", cell.Yaw.ToString(System.Globalization.CultureInfo.InvariantCulture));
        cell.Timer?.Reset();
        cell.Applied = false;
        SaveCrystalCell(cell);
        CrystalNet.DeviceListDirty = true;
        CrystalNet.NetListDirty = true;
    }

    private void PulseCrystalCell(ServerCrystalCell cell)
    {
        cell.Output = true;
        cell.PulseUntil = _uptime + CrystalNetRules.PulseSeconds;
        CrystalNet.DeviceListDirty = true;
    }

    /// <summary>A port block's own output, driven from the code that owns it (a sentry that found a target, a beam
    /// pad someone arrived at). No-op for cells that are not in the net.</summary>
    private void CrystalPortLevel(Vector3i cell, bool on)
    {
        if (CrystalNet.Cells.TryGetValue(cell, out var c) && c.Output != on)
        {
            c.Output = on;
            c.PulseUntil = 0;
            CrystalNet.DeviceListDirty = true;
        }
    }

    private void CrystalPortPulse(Vector3i cell)
    {
        if (CrystalNet.Cells.TryGetValue(cell, out var c))
        {
            PulseCrystalCell(c);
        }
    }

    /// <summary>Whether a sentry post holds its fire because a conduit beside it is OFF (#2053).</summary>
    private bool CrystalSentryDisabled(Vector3i cell) => CrystalNet.DisabledSentries.Contains(cell);

    /// <summary>#2053: the sentry's firing pass through the Crystal Net — a disabled post holds its fire, and the
    /// post's port reads "has a target" (ON while it is shooting) for an alarm circuit.</summary>
    private (bool Enemies, bool Creatures) FireSentryLinked(Vector3i cell, ServerBase home)
    {
        if (CrystalSentryDisabled(cell))
        {
            CrystalPortLevel(cell, false);
            return (false, false);
        }

        var result = FireSentry(cell, home);
        CrystalPortLevel(cell, result.Enemies || result.Creatures);
        return result;
    }

    /// <summary>Whether a water spout is switched off by a conduit (#2053).</summary>
    private bool CrystalSpoutClosed(Vector3i cell) => CrystalNet.ClosedSpouts.Contains(cell);

    /// <summary>Whether an energy gate is held open for fauna by a conduit (#2053).</summary>
    private bool CrystalGateOpen(Vector3i cell) => CrystalNet.OpenGates.Contains(cell);

    /// <summary>Test/util entrypoint: mirrors the intent (toggle / press / configure) for a player.</summary>
    public void SetCrystalDeviceForTest(PlayerSession session, Vector3i cell, int action, int mode = 0, string config = "", string label = "")
        => HandleSetCrystalDevice(session, new SetCrystalDeviceIntent { X = cell.X, Y = cell.Y, Z = cell.Z, Action = action, Mode = mode, Config = config, Label = label });

    // ------------------------------------------------------------------------------------------------------
    // The tick: logic beat + sensor beat
    // ------------------------------------------------------------------------------------------------------

    /// <summary>Under <c>Guard</c> in the per-world loop. Cost: nothing at all on a world without net cells; on a
    /// built base one pass over the cells every 100 ms plus the sensor queries every 500 ms.</summary>
    private void TickCrystalNet(double dt)
    {
        _drillBlocksThisTick = 0;
        _pistonPushesThisTick = 0;
        var state = CrystalNet;
        if (state.Cells.Count == 0)
        {
            return;
        }

        if (!state.ClonesRespawned)
        {
            state.ClonesRespawned = true;
            RespawnCrystalClones(); // #2057: the roster exists by now; the tanks' clones come back beside their tanks
        }

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

        TickLifts(dt); // #2266: moving platforms advance every tick (smooth for the riders), not on the beat

        if (state.NetListDirty)
        {
            state.NetListDirty = false;
            BroadcastToWorld(CrystalNetMessage());
        }

        if (state.CreatureListDirtyHint)
        {
            state.CreatureListDirtyHint = false;
            BroadcastCreatures();
        }

        if (state.DeviceListDirty)
        {
            state.DeviceListDirty = false;
            BroadcastCrystalDeviceChanges();
        }
    }

    /// <summary>#2267: the devices that changed since the world was last told, as a delta — a flickering clock costs a few
    /// devices per beat instead of the whole base. The comparison is against what was sent (a signature per cell), so a
    /// change made and undone within one beat sends nothing. When most devices changed, the whole list goes out instead.</summary>
    private void BroadcastCrystalDeviceChanges()
    {
        var state = CrystalNet;
        var changed = new List<NetCrystalDevice>();
        var seen = new HashSet<Vector3i>();
        foreach (var c in state.Cells.Values)
        {
            if (c.IsConduit)
            {
                continue;
            }

            seen.Add(c.Cell);
            var net = ToNetCrystalDevice(c);
            string sig = CrystalDeviceSignature(net);
            if (!state.LastSentDevice.TryGetValue(c.Cell, out var was) || was != sig)
            {
                state.LastSentDevice[c.Cell] = sig;
                changed.Add(net);
            }
        }

        var removed = new List<int>();
        foreach (var cell in state.LastSentDevice.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            state.LastSentDevice.Remove(cell);
            removed.Add(cell.X);
            removed.Add(cell.Y);
            removed.Add(cell.Z);
        }

        if (changed.Count == 0 && removed.Count == 0)
        {
            return;
        }

        if (changed.Count * 2 > seen.Count)
        {
            BroadcastToWorld(CrystalDeviceMessage()); // most of the base changed (a load, a reset): the whole list
            return;
        }

        BroadcastToWorld(new CrystalDeviceDelta { Changed = changed.ToArray(), Removed = removed.ToArray() });
    }

    private static string CrystalDeviceSignature(NetCrystalDevice d)
        => string.Join("|", d.Id, d.Kind, d.Mode, d.Output ? 1 : 0, d.OwnerId, d.Label, d.Config, string.Join(",", d.Choices));

    private void CrystalLogicBeat()
    {
        var state = CrystalNet;

        // 1. Pulses fall back.
        foreach (var c in state.Cells.Values)
        {
            if (c.PulseUntil > 0 && _uptime >= c.PulseUntil)
            {
                c.PulseUntil = 0;
                c.Output = false;
                state.DeviceListDirty = true;
            }
        }

        // 2. Network levels: OR over member outputs, plus the gates that drive into the network (their output is
        //    the result of the PREVIOUS beat — one beat of delay per gate, so loops are well-defined).
        foreach (var net in state.Nets.Values)
        {
            net.Level = false;
        }

        foreach (var c in state.Cells.Values)
        {
            // #2092: only sources and gates drive a network. Every other device only LISTENS — its report stays a status
            // (its light, a Device Eye) and never reads back as its own command.
            if (c.Inert || c.IsConduit || !c.Output || !(c.IsGate || CrystalNetRules.IsSource(c.Kind)))
            {
                continue;
            }

            int netId = c.IsGate ? CrystalGateOutputNet(c) : c.NetId;
            if (netId != 0 && state.Nets.TryGetValue(netId, out var net))
            {
                net.Level = true;
            }
        }

        // 3. Gates compute their next output from this beat's levels.
        foreach (var c in state.Cells.Values)
        {
            if (c.Inert || !c.IsGate)
            {
                continue;
            }

            bool next;
            if (c.Kind == CrystalDeviceKind.DiceBlock)
            {
                StepCrystalDice(c, CrystalGateInputs(c)); // #2263: its output is a pulse the pulse step ends
                continue;
            }

            if (c.Kind == CrystalDeviceKind.DeviceEye)
            {
                next = CrystalEyeReads(c); // #2092: what the device (or door) in front of it is doing
                if (next != c.Output)
                {
                    c.Output = next;
                    state.DeviceListDirty = true;
                }

                continue;
            }

            var inputs = CrystalGateInputs(c);
            if (c.Kind == CrystalDeviceKind.LogicBlock)
            {
                next = LogicGate.Evaluate((LogicMode)c.Mode, inputs);
            }
            else
            {
                bool input = inputs.Count == 0 || inputs.Any(i => i); // an unconnected timer runs (a free clock)
                c.Timer ??= new TimerState();
                c.Timer.Step((TimerMode)c.Mode, input, CrystalNetRules.LogicBeatSeconds,
                    CrystalConfigDouble(c.Config, "period", 2.0), CrystalConfigInt(c.Config, "count", 4));
                next = c.Timer.Output;
            }

            if (next != c.Output)
            {
                c.Output = next;
                state.DeviceListDirty = true;
            }
        }

        // 3b. #2263: signal receivers repeat their sender's level of this beat (they drive their own network next beat).
        foreach (var c in state.Cells.Values)
        {
            if (c.Kind == CrystalDeviceKind.SignalReceiver && !c.Inert)
            {
                StepCrystalReceiver(c);
            }
        }

        // 4. Actuators follow their network.
        bool anyLevelChanged = false;
        foreach (var c in state.Cells.Values)
        {
            if (c.Inert || c.IsConduit || c.IsGate || c.NetId == 0 || !state.Nets.TryGetValue(c.NetId, out var net))
            {
                continue;
            }

            if (CrystalNetRules.IsEdgeSink(c.Kind))
            {
                // #2092: an edge device acts on the rising edge of the network itself, not on its own last-applied state —
                // a 0.5 s clock rings a chime every 0.5 s. A small floor keeps a 0.1 s flicker from double-firing.
                if (net.Level && !net.PrevLevel && _uptime - c.LastActuated >= CrystalEdgeMinIntervalSeconds)
                {
                    ApplyCrystalActuator(c, true);
                    c.LastActuated = _uptime;
                }

                continue;
            }

            if (c.Kind == CrystalDeviceKind.SignalDisplay)
            {
                UpdateCrystalDisplay(c, net); // #2263: the light mirrors the level; a counter counts rising edges
                continue;
            }

            if (c.Kind is CrystalDeviceKind.BridgeMotor or CrystalDeviceKind.SignalSender)
            {
                continue; // a bridge moves on its own beat (CrystalMachineBeat); a sender is only read by its receivers
            }

            if (IsCrystalTwinKind(c.Kind) && !c.Synced)
            {
                // #2096: a lamp's (and #2251: any twinned block's) first beat reads the block that actually stands there;
                // an unloaded chunk — or a body in a cell that would close — retries.
                if (SwapCrystalTwin(c, net.Level))
                {
                    c.Synced = true;
                    c.Applied = net.Level;
                    c.LastActuated = _uptime;
                }

                continue;
            }

            if (net.Level != c.Applied && _uptime - c.LastActuated >= CrystalNetRules.ActuatorMinIntervalSeconds)
            {
                // A swap that would close onto someone, or a piston with no room, did not happen: it tries again after the
                // actuator interval, still wanting the new level.
                if (ApplyCrystalActuator(c, net.Level))
                {
                    c.Applied = net.Level;
                }

                c.LastActuated = _uptime;
            }
        }

        // 5. Doors attach through their neighbouring cells.
        CrystalDoorBeat();

        // 6. Machines that work while ON (matter link, fabricator, drill) advance on their own beat.
        CrystalMachineBeat();

        // The list goes out when a level differs from what the clients were last told.
        foreach (var net in state.Nets.Values)
        {
            if (net.Level != state.LastSentLevel.GetValueOrDefault(net.Id))
            {
                anyLevelChanged = true;
            }
        }

        if (anyLevelChanged)
        {
            state.NetListDirty = true;
        }

        foreach (var net in state.Nets.Values)
        {
            net.PrevLevel = net.Level; // #2092: the edge detector's memory
        }
    }

    /// <summary>#2092: the shortest gap between two actions of one edge device (a 0.1 s flicker must not double-fire).</summary>
    private const double CrystalEdgeMinIntervalSeconds = 0.2;

    /// <summary>#2092: the Device Eye reads the status of the Crystal Net device in front of it (blocked, arrived, owner
    /// near, has a target, ripe, growing, ready, done — whatever that device reports), or whether a door in front of it
    /// is open. Nothing in front → OFF.</summary>
    private bool CrystalEyeReads(ServerCrystalCell eye)
    {
        var front = eye.Cell + CrystalNetRules.OutputFace(eye.Yaw);
        if (CrystalNet.Cells.TryGetValue(front, out var dev))
        {
            return !dev.IsConduit && !dev.Inert && dev.Output;
        }

        foreach (var door in _doors)
        {
            var floor = door.Pos.ToBlock();
            if (front.Y < floor.Y || front.Y > floor.Y + 1)
            {
                continue;
            }

            float dx = front.X + 0.5f - door.Pos.X, dz = front.Z + 0.5f - door.Pos.Z;
            float half = System.Math.Max(0.5f, door.Width * 0.5f) + 0.05f;
            if (System.Math.Abs(dx) <= half && System.Math.Abs(dz) <= half)
            {
                return door.Open;
            }
        }

        return false;
    }

    private int CrystalGateOutputNet(ServerCrystalCell gate)
    {
        var outCell = gate.Cell + CrystalNetRules.DriveFace(gate.Kind, gate.Yaw);
        return CrystalNet.Cells.TryGetValue(outCell, out var c) && !c.IsGate ? c.NetId : 0;
    }

    private List<bool> CrystalGateInputs(ServerCrystalCell gate)
    {
        var result = new List<bool>(5);
        var seen = new HashSet<int>();
        var outFace = CrystalNetRules.OutputFace(gate.Yaw);
        int outNet = CrystalGateOutputNet(gate);
        foreach (var face in CrystalNetRules.Faces)
        {
            if (face == outFace)
            {
                continue;
            }

            if (CrystalNet.Cells.TryGetValue(gate.Cell + face, out var c))
            {
                if (c.IsGate)
                {
                    // Gate to gate: read the neighbour's output directly — but only when it points at this gate.
                    if (c.Cell + CrystalNetRules.DriveFace(c.Kind, c.Yaw) == gate.Cell)
                    {
                        result.Add(c.Output);
                    }
                }
                else if (c.NetId != 0 && c.NetId != outNet && seen.Add(c.NetId) && CrystalNet.Nets.TryGetValue(c.NetId, out var net))
                {
                    result.Add(net.Level);
                }
            }
        }

        return result;
    }

    /// <summary>Doors obey the conduit beside them: ON = held open, OFF = locked, none = normal (#2048). Two cells are
    /// checked per door (the gap's floor cell and the one above it), six faces each — a few dictionary lookups.</summary>
    private void CrystalDoorBeat()
    {
        if (_doors.Count == 0)
        {
            return;
        }

        var state = CrystalNet;
        bool changed = false;
        foreach (var door in _doors)
        {
            var mode = DoorMode.Normal;
            var floor = door.Pos.ToBlock();
            bool any = false, on = false;
            for (int dy = 0; dy < 2 && !on; dy++)
            {
                var cell = new Vector3i(floor.X, floor.Y + dy, floor.Z);
                foreach (var face in CrystalNetRules.Faces)
                {
                    if (state.Cells.TryGetValue(cell + face, out var c) && !c.Inert && c.NetId != 0 && state.Nets.TryGetValue(c.NetId, out var net)
                        && CrystalMayDriveDoor(door, c))
                    {
                        any = true;
                        if (net.Level)
                        {
                            on = true;
                            break;
                        }
                    }
                }
            }

            if (any)
            {
                mode = on ? DoorMode.HeldOpen : DoorMode.Locked;
            }

            if (door.Mode != mode)
            {
                door.Mode = mode;
                changed = true;
            }
        }

        if (changed)
        {
            BroadcastDoors(); // TickDoors applies the mode on its next pass
        }
    }

    /// <summary>#2253: whether this net cell may lock or hold open this door. A door a player hung follows only the cells of
    /// its owner and the owner's alliance — a stranger's conduit beside it is ignored. A door from an older save has no
    /// owner yet: it adopts the owner of the first player's cell that touches it (persisted). A stamped door (settlement,
    /// station, ship layout) follows only world circuits and public cells.</summary>
    private bool CrystalMayDriveDoor(ServerDoor door, ServerCrystalCell c)
    {
        bool cellPublic = c.OwnerId.Length == 0 || CrystalNetRules.IsWorldOwner(c.OwnerId);
        if (!door.PlayerBuilt)
        {
            return cellPublic;
        }

        if (door.Owner.Length == 0)
        {
            if (cellPublic)
            {
                return true;
            }

            door.Owner = c.OwnerId; // an old save's door: the first player who wires it is taken for its builder
            var at = door.Pos.ToBlock();
            _repo.SaveDoor(new StoredDoor { Planet = _world.LocationId, X = at.X, Y = at.Y, Z = at.Z, Kind = door.Kind, AxisX = door.AxisX, Owner = door.Owner });
            return true;
        }

        return c.OwnerId == door.Owner || AreAllied(door.Owner, c.OwnerId);
    }

    // ------------------------------------------------------------------------------------------------------
    // Sensors (500 ms)
    // ------------------------------------------------------------------------------------------------------

    private readonly List<(Vector3f Pos, PresenceFilter Kind, string PlayerId)> _crystalPresence = new();

    /// <summary>Polls every sensor and every port that reads the world. The entity lists are gathered ONCE per beat
    /// into a flat presence list, so a base with twenty plates costs one pass over the players, NPCs and creatures.</summary>
    private void CrystalSensorBeat()
    {
        var state = CrystalNet;
        bool needPresence = false;
        foreach (var c in state.Cells.Values)
        {
            if (!c.Inert && c.Kind is CrystalDeviceKind.StepPlate or CrystalDeviceKind.ProximitySensor or CrystalDeviceKind.Beacon
                or CrystalDeviceKind.HealTank or CrystalDeviceKind.Bed or CrystalDeviceKind.EnvironmentSensor)
            {
                needPresence = true;
                break;
            }
        }

        if (needPresence)
        {
            GatherCrystalPresence();
        }

        foreach (var c in state.Cells.Values)
        {
            if (c.Kind == CrystalDeviceKind.CloneTank)
            {
                // #2214: what its owner may pick now, and which of its clones still live. An inert tank too: its menu
                // opens like any other's.
                WatchCloneTank(c);
            }

            if (c.Inert)
            {
                continue;
            }

            bool? next = c.Kind switch
            {
                CrystalDeviceKind.StepPlate => PresenceInCell(c.Cell, (PresenceFilter)Math.Min(3, c.Mode), c.OwnerId),
                CrystalDeviceKind.ProximitySensor => PresenceWithin(c.Cell, CrystalNetRules.ProximityRadii[Math.Max(0, Math.Min(2, CrystalConfigInt(c.Config, "r", 1)))], (PresenceFilter)c.Mode, c.OwnerId),
                CrystalDeviceKind.DaylightSensor => IsNightAt(new Vector3f(c.Cell.X + 0.5f, c.Cell.Y, c.Cell.Z + 0.5f)) == (c.Mode == 1),
                CrystalDeviceKind.StorageSensor => StorageSensorReads(c),
                CrystalDeviceKind.Beacon => PresenceWithin(c.Cell, CrystalNetRules.BeaconOwnerRange, PresenceFilter.Owner, c.OwnerId),
                CrystalDeviceKind.HydroTray => HydroTrayRipe(c.Cell),
                _ => CrystalSensorReads2(c), // #2261 / #2263: port statuses and the environment sensor
            };
            if (next is { } level && level != c.Output && c.PulseUntil == 0)
            {
                c.Output = level;
                state.DeviceListDirty = true;
            }
        }

        CrystalDiscoveryBeat(); // #2257: the first own network ON, the first amber light, the first old world circuit
    }

    private void GatherCrystalPresence()
    {
        _crystalPresence.Clear();
        foreach (var s in JoinedInActiveWorld())
        {
            if (!InSpace(s.State.PlayerId))
            {
                _crystalPresence.Add((s.State.Position, PresenceFilter.Players, s.State.PlayerId));
            }
        }

        foreach (var npc in _npcs)
        {
            _crystalPresence.Add((npc.Pos, PresenceFilter.Npcs, string.Empty));
        }

        foreach (var cr in _creatures)
        {
            _crystalPresence.Add((cr.Position, cr.IsCompanion ? PresenceFilter.TameCreatures : (cr.Hostile ? PresenceFilter.Hostile : PresenceFilter.WildCreatures), cr.OwnerId));
        }

        foreach (var e in _planetEnemies)
        {
            _crystalPresence.Add((e.Position, PresenceFilter.Hostile, string.Empty));
        }

        foreach (var b in _bandits)
        {
            _crystalPresence.Add((b.Position, PresenceFilter.Hostile, string.Empty));
        }
    }

    private bool PresenceMatches(PresenceFilter filter, PresenceFilter kind, string entityOwner, string deviceOwner) => filter switch
    {
        PresenceFilter.Anyone => true,
        PresenceFilter.Players => kind == PresenceFilter.Players,

        // #2254: "only you" means you and your alliance (allies and crew) — every other Crystal Net permission counts them.
        PresenceFilter.Owner => kind == PresenceFilter.Players && (entityOwner == deviceOwner || (deviceOwner.Length > 0 && AreAllied(deviceOwner, entityOwner))),
        PresenceFilter.WildCreatures => kind == PresenceFilter.WildCreatures,
        PresenceFilter.TameCreatures => kind == PresenceFilter.TameCreatures,
        PresenceFilter.Hostile => kind == PresenceFilter.Hostile,
        PresenceFilter.Npcs => kind == PresenceFilter.Npcs,
        _ => false,
    };

    /// <summary>The step plate's filter picker is a 4-entry subset of <see cref="PresenceFilter"/>: anyone, players,
    /// owner, creatures (wild + tame).</summary>
    private bool PresenceInCell(Vector3i cell, PresenceFilter filter, string owner)
    {
        var centre = new Vector3f(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
        foreach (var p in _crystalPresence)
        {
            bool match = filter == PresenceFilter.WildCreatures
                ? p.Kind is PresenceFilter.WildCreatures or PresenceFilter.TameCreatures
                : PresenceMatches(filter, p.Kind, p.PlayerId, owner);
            if (!match)
            {
                continue;
            }

            double dy = p.Pos.Y - centre.Y;
            if (dy < -0.6 || dy > 1.6)
            {
                continue;
            }

            if (WrapDistSq(new Vector3f(p.Pos.X, centre.Y, p.Pos.Z), centre) <= 0.8f * 0.8f)
            {
                return true;
            }
        }

        return false;
    }

    private bool PresenceWithin(Vector3i cell, float radius, PresenceFilter filter, string owner)
    {
        var centre = new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
        double r2 = radius * radius;
        foreach (var p in _crystalPresence)
        {
            if (PresenceMatches(filter, p.Kind, p.PlayerId, owner) && WrapDistSq(p.Pos, centre) <= r2)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The storage sensor reads the crate(s) beside it: full (no room for one more of anything it holds),
    /// empty, or "holds at least N of its first filter item".</summary>
    private bool StorageSensorReads(ServerCrystalCell c)
    {
        var crate = AdjacentCrystalCrate(c.Cell, out _);
        if (crate is null)
        {
            return false;
        }

        switch ((StorageSensorMode)c.Mode)
        {
            case StorageSensorMode.Empty:
                return crate.Items.Count == 0;
            case StorageSensorMode.HasFilterItem:
                {
                    string? item = crate.Filter.FirstOrDefault() ?? CrystalConfigValue(c.Config, "item");
                    int want = CrystalConfigInt(c.Config, "n", 1);
                    return item is not null && crate.Items.Where(s => s.Item == item).Sum(s => s.Count) >= want;
                }
            default:
                return CrateIsFull(crate);
        }
    }

    /// <summary>A crate is "full" for the sensor when the wood box has all its stacks and none can grow, or when a
    /// workshop crate holds 32 stacks (its practical ceiling — the sensor needs a line to draw).</summary>
    private static bool CrateIsFull(StoredContainer crate)
    {
        int limit = crate.Kind == "wood_crate" ? 8 : 32;
        if (crate.Items.Count < limit)
        {
            return false;
        }

        return crate.Items.All(s => s.Count >= 64);
    }

    /// <summary>#2262: every container beside a device (six faces, each once) — a machine takes from and fills any of them,
    /// like the fabricator always did.</summary>
    private List<StoredContainer> AdjacentCrystalCrates(Vector3i cell)
    {
        var result = new List<StoredContainer>(2);
        foreach (var face in CrystalNetRules.Faces)
        {
            if (ContainerAt(cell + face) is { } crate && !result.Contains(crate))
            {
                result.Add(crate);
            }
        }

        return result;
    }

    /// <summary>#2262: the first crate beside a device that would take these items, or null.</summary>
    private StoredContainer? AdjacentCrateWithRoom(Vector3i cell, IReadOnlyList<ItemAmount> items)
    {
        foreach (var crate in AdjacentCrystalCrates(cell))
        {
            if (NpcCrateHasRoom(crate, items))
            {
                return crate;
            }
        }

        return null;
    }

    /// <summary>#2262: whether a block of this key stands on one of a cell's six faces.</summary>
    private bool BlockBesideCrystalCell(Vector3i cell, string blockKey)
    {
        var def = _content.GetBlock(blockKey);
        if (def is null)
        {
            return false;
        }

        foreach (var face in CrystalNetRules.Faces)
        {
            if (_world.GetBlockIfLoaded(cell + face).Value == def.NumericId.Value)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The first container beside a device (six faces), or null.</summary>
    private StoredContainer? AdjacentCrystalCrate(Vector3i cell, out Vector3i crateCell)
    {
        foreach (var face in CrystalNetRules.Faces)
        {
            var n = cell + face;
            if (ContainerAt(n) is { } crate)
            {
                crateCell = n;
                return crate;
            }
        }

        crateCell = cell;
        return null;
    }

    /// <summary>A hydro tray reports ripe while a crop stands on it (harvesting leaves air until the regrow).</summary>
    private bool HydroTrayRipe(Vector3i tray)
    {
        var above = new Vector3i(tray.X, tray.Y + 1, tray.Z);
        var id = _world.GetBlockIfLoaded(above);
        return !id.IsAir && IsFlora(id.Value);
    }

    /// <summary>#2050: a watcher pulses when the cell in front of it changes — a mined block, a placed one, a crop
    /// that ripened, a door that opened (its cell stays air; doors are announced through <see cref="CrystalDoorBeat"/>).</summary>
    private void OnCrystalWatchedCellChanged(Vector3i changed)
    {
        var state = _worlds.Active.CrystalNet;
        if (state.Cells.Count == 0)
        {
            return;
        }

        foreach (var face in CrystalNetRules.Faces)
        {
            if (state.Cells.TryGetValue(changed + face, out var c) && c.Kind == CrystalDeviceKind.Watcher && !c.Inert
                && c.Cell + CrystalNetRules.OutputFace(c.Yaw) == changed)
            {
                PulseCrystalCell(c);
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------
    // Actuators
    // ------------------------------------------------------------------------------------------------------

    /// <summary>Applies a network level to a sink. Level sinks (lamp, siren, sentry, spout, gate) follow it; edge
    /// sinks (chime, horn, melody, announcer, thumper, tray, beam pad, machines) act on the rising edge only.</summary>
    private bool ApplyCrystalActuator(ServerCrystalCell c, bool on)
    {
        var state = CrystalNet;
        switch (c.Kind)
        {
            case CrystalDeviceKind.Light:
                return SwapCrystalLight(c, on);
            case CrystalDeviceKind.PhaseBlock:
            case CrystalDeviceKind.Trapdoor:
            case CrystalDeviceKind.ForceField:
            case CrystalDeviceKind.Campfire:
            case CrystalDeviceKind.Forge:
                return SwapCrystalTwin(c, on); // #2261 / #2264: signal ON = open / burning
            case CrystalDeviceKind.Piston:
                return on ? PistonPush(c) : PistonRetract(c, (PistonMode)c.Mode == PistonMode.Sticky); // #2265
            case CrystalDeviceKind.HealTank:
                if (on)
                {
                    state.DisabledHealTanks.Remove(c.Cell);
                }
                else
                {
                    state.DisabledHealTanks.Add(c.Cell);
                }

                break;
            case CrystalDeviceKind.EnergyFence:
                if (on)
                {
                    state.OpenFences.Add(c.Cell);
                }
                else
                {
                    state.OpenFences.Remove(c.Cell);
                }

                break;
            case CrystalDeviceKind.FlowerPot:
                if (on)
                {
                    HarvestHydroTray(c); // a pot harvests like a tray: the plant above it into the crate beside it
                }

                break;
            case CrystalDeviceKind.LiftMotor:
            case CrystalDeviceKind.LiftStop:
                if (on)
                {
                    TriggerCrystalMachine(c, null); // #2266: a stop calls the platform, the motor sends it on
                }

                break;
            case CrystalDeviceKind.AlarmSiren:
                SetCrystalLoop(c, on, "alarm_siren_" + Math.Max(0, Math.Min(2, c.Mode)));
                break;
            case CrystalDeviceKind.Chime:
                if (on)
                {
                    PlayCrystalSound(c, "chime_" + Math.Max(0, Math.Min(3, c.Mode)), 1f);
                    EmitVibration(new Vector3f(c.Cell.X + 0.5f, c.Cell.Y - 0.5f, c.Cell.Z + 0.5f), VibrationSource.Horn, c.OwnerId); // #2077: the worm hears it
                }

                break;
            case CrystalDeviceKind.Horn:
                if (on)
                {
                    PlayCrystalSound(c, "horn_" + Math.Max(0, Math.Min(2, c.Mode)), 1f);
                    EmitVibration(new Vector3f(c.Cell.X + 0.5f, c.Cell.Y - 0.5f, c.Cell.Z + 0.5f), VibrationSource.Horn, c.OwnerId); // #2077: the worm hears it
                }

                break;
            case CrystalDeviceKind.MelodyBlock:
                if (on)
                {
                    int instrument = Math.Max(0, Math.Min(3, CrystalConfigInt(c.Config, "inst", 0)));
                    PlayCrystalSound(c, "note_" + instrument + "_" + Math.Max(0, Math.Min(7, c.Mode)), 1f);
                }

                break;
            case CrystalDeviceKind.Announcer:
                if (on)
                {
                    AnnounceCrystal(c);
                }

                break;
            case CrystalDeviceKind.Sentry:
                if (on)
                {
                    state.DisabledSentries.Remove(c.Cell);
                }
                else
                {
                    state.DisabledSentries.Add(c.Cell);
                }

                break;
            case CrystalDeviceKind.Spout:
                if (on)
                {
                    state.ClosedSpouts.Remove(c.Cell);
                    _activeFluid.Add(c.Cell); // wake it so it pours again
                }
                else
                {
                    state.ClosedSpouts.Add(c.Cell);
                }

                break;
            case CrystalDeviceKind.EnergyGate:
                if (on)
                {
                    state.OpenGates.Add(c.Cell);
                }
                else
                {
                    state.OpenGates.Remove(c.Cell);
                }

                break;
            case CrystalDeviceKind.Beacon:
                SetBeaconAlarm(c.Cell, on);
                break;
            case CrystalDeviceKind.Thumper:
                if (on)
                {
                    StartThumperAt(c.Cell, c.OwnerId);
                }

                break;
            case CrystalDeviceKind.HydroTray:
                if (on)
                {
                    HarvestHydroTray(c);
                }

                break;
            case CrystalDeviceKind.BeamPad:
                if (on)
                {
                    BeamStandingPlayers(c);
                }

                break;
            case CrystalDeviceKind.Fabricator:
            case CrystalDeviceKind.MatterSender:
            case CrystalDeviceKind.CloneTank:
            case CrystalDeviceKind.AutoDrill:
            case CrystalDeviceKind.DrillLaser:
            case CrystalDeviceKind.Caller:
                if (on)
                {
                    TriggerCrystalMachine(c, null); // the rising edge is one job; a held level keeps the machine on its own beat
                }

                break;
        }

        return true;
    }

    /// <summary>What a sink does when its cell leaves the net (mined, or orphaned): loops stop, a dark lamp lights up
    /// again, a disabled sentry fires again, a closed spout pours again.</summary>
    private void StopCrystalActuator(ServerCrystalCell c, bool relight)
    {
        if (c.Looping)
        {
            SetCrystalLoop(c, false, string.Empty);
        }

        if (c.Kind == CrystalDeviceKind.Light && relight)
        {
            SwapCrystalLight(c, true); // no-op when it is already lit
        }

        if (c.Kind == CrystalDeviceKind.Beacon)
        {
            SetBeaconAlarm(c.Cell, false);
        }

        if (c.Kind == CrystalDeviceKind.CloneTank)
        {
            ReleaseClonesOfTank(c.Cell); // #2057: a mined tank sets its clones free
        }

        if (c.Kind == CrystalDeviceKind.Spout)
        {
            _activeFluid.Add(c.Cell);
        }

        if (relight && CrystalNetRules.PlainKeyFor(c.Kind) is not null)
        {
            RestoreCrystalTwin(c); // #2261: an unwired field switches on again, a fire burns again, a phase block closes
        }

        if (c.Kind == CrystalDeviceKind.BridgeMotor)
        {
            BridgeRetractAll(c); // #2265: a mined motor takes its deck with it
        }

        if (c.Kind == CrystalDeviceKind.LiftMotor)
        {
            RemoveLift(c.Cell); // #2266: and a mined lift motor its platform
        }

        if (c.Kind == CrystalDeviceKind.Piston && c.Pushed)
        {
            PistonRetract(c, sticky: false);
        }
    }

    /// <summary>Swaps a lamp between its lit block and its unlit twin, keeping dye, glow and form (#2048). One
    /// <see cref="BlockChanged"/> per swap — the reason actuators are rate-limited.</summary>
    private bool SwapCrystalLight(ServerCrystalCell c, bool on)
    {
        var id = _world.GetBlockIfLoaded(c.Cell);
        var current = _content.BlockById(id);
        if (current is null || id.IsAir)
        {
            return false; // chunk not loaded (or the lamp is gone): try again next beat
        }

        string wantKey = on ? CrystalNetRules.LightOnKey(current.Key) : CrystalNetRules.LightOffKey(CrystalNetRules.LightOnKey(current.Key));
        if (wantKey == current.Key)
        {
            return true;
        }

        var want = _content.GetBlock(wantKey);
        if (want is null)
        {
            return true; // a lamp without a twin (a modded pack) simply stays lit
        }

        var (tint, glow) = _world.GetModifier(c.Cell);
        int shape = _world.GetShape(c.Cell);
        _world.SetBlock(c.Cell, want.NumericId, tint, glow, shape);
        BroadcastToWorld(new BlockChanged { X = c.Cell.X, Y = c.Cell.Y, Z = c.Cell.Z, Block = want.NumericId.Value, Tint = tint, Glow = glow, Shape = shape });
        WriteBackStationCell(c.Cell, want.NumericId, tint, glow, shape);
        c.BlockKey = want.Key;
        return true;
    }

    private void PlayCrystalSound(ServerCrystalCell c, string soundId, float pitch)
    {
        if (CrystalNet.Cells.Values.Count(d => d.Looping) >= CrystalNetRules.MaxSoundDevicesPlayingPerWorld)
        {
            return;
        }

        BroadcastToWorld(new SoundFx { SoundId = soundId, X = c.Cell.X + 0.5f, Y = c.Cell.Y + 0.5f, Z = c.Cell.Z + 0.5f, Pitch = pitch, SourceId = c.Id });
    }

    private void SetCrystalLoop(ServerCrystalCell c, bool on, string soundId)
    {
        if (on == c.Looping)
        {
            return;
        }

        if (on && CrystalNet.Cells.Values.Count(d => d.Looping) >= CrystalNetRules.MaxSoundDevicesPlayingPerWorld)
        {
            return; // the world cap: no ninth siren
        }

        c.Looping = on;
        BroadcastToWorld(new SoundFx { SoundId = soundId, X = c.Cell.X + 0.5f, Y = c.Cell.Y + 0.5f, Z = c.Cell.Z + 0.5f, Loop = on, Stop = !on, SourceId = c.Id });
    }

    /// <summary>The announcer speaks one preset line (its mode) — or its own screened label — to the owner and the
    /// owner's allies on this world, as a server toast; strangers never hear it (chat rules stay intact).</summary>
    private void AnnounceCrystal(ServerCrystalCell c)
    {
        string text = c.Label.Length > 0 ? "@srv.crystal.announce_custom:" + c.Label : "@srv.crystal.announce_" + Math.Max(0, Math.Min(5, c.Mode));
        foreach (var s in JoinedInActiveWorld())
        {
            if (s.State.PlayerId == c.OwnerId || AreAllied(c.OwnerId, s.State.PlayerId))
            {
                Send(s, new ServerMessage { Text = text });
            }
        }

        PlayCrystalSound(c, "ai_blip", 1f);
    }

    /// <summary>A rising edge on a hydro tray harvests the crop standing on it into the crate beside the tray — the
    /// gardener's job on a signal (#2053).</summary>
    private void HarvestHydroTray(ServerCrystalCell c)
    {
        var above = new Vector3i(c.Cell.X, c.Cell.Y + 1, c.Cell.Z);
        var id = _world.GetBlockIfLoaded(above);
        var def = _content.BlockById(id);
        if (def is null || id.IsAir || !IsFlora(id.Value))
        {
            return;
        }

        // #2209: a bred plant yields what its body parent's form yields, not the bred-plant block's own drops.
        uint bredSeed = IsBredPlant(id.Value) ? BredSeedAt(above) : 0;
        var yield = bredSeed != 0 ? BredYield(bredSeed) : def.Drops;
        var crate = AdjacentCrateWithRoom(c.Cell, yield); // #2262: any crate beside the tray or pot
        if (crate is null || !NpcDepositToContainer(crate, yield))
        {
            return; // no crate, or no room: the crop stays standing
        }

        var (tint, _) = _world.GetModifier(above);
        _world.SetBlock(above, BlockId.Air);
        BroadcastToWorld(new BlockChanged { X = above.X, Y = above.Y, Z = above.Z, Block = BlockId.AirValue });
        WriteBackStationCell(above, BlockId.Air);
        ScheduleFloraRegrow(above, id.Value, tint);
    }

    /// <summary>A rising edge on a beam pad beams everyone standing on it to its paired pad (config <c>pair=x,y,z</c> — #2252:
    /// the partner's cell, beam ids are handed out afresh on every load), no menu, no energy — the pad's owner or an ally
    /// must own the target, as with a hand beam (#2053).</summary>
    private void BeamStandingPlayers(ServerCrystalCell c)
    {
        var at = CrystalPairCell(c.Config);
        var target = at is { } cell ? _beams.FirstOrDefault(b => b.Cell == cell) : null;
        if (target is null || !CanUseBeam(target, c.OwnerId))
        {
            return;
        }

        var top = new Vector3f(c.Cell.X + 0.5f, c.Cell.Y + 1f, c.Cell.Z + 0.5f);
        var to = BeamArrivalSpot(target.Cell);
        foreach (var s in JoinedInActiveWorld().ToList())
        {
            if (InSpace(s.State.PlayerId) || WrapDistSq(s.State.Position, top) > 1.2 * 1.2)
            {
                continue;
            }

            if (_beamCooldown.GetValueOrDefault(s.State.PlayerId) > 0)
            {
                continue; // #2092: whoever just arrived stays put — no ping-pong between wired pads, whatever the wiring
            }

            _beamCooldown[s.State.PlayerId] = BeamCooldownSeconds;
            s.State.Position = to;
            StreamFootingNow(s, to);
            SendPlayerState(s);
            Send(s, new BeamTeleported { X = to.X, Y = to.Y, Z = to.Z });
            BroadcastToWorld(new BeamFx { FromX = top.X, FromY = top.Y, FromZ = top.Z, ToX = to.X, ToY = to.Y, ToZ = to.Z });
            CrystalPortPulse(target.Cell); // "someone arrived" on the far pad
        }
    }

    // ------------------------------------------------------------------------------------------------------
    // Wire
    // ------------------------------------------------------------------------------------------------------

    /// <summary>Every network with its level. Broadcast (<paramref name="baseline"/>) it is what the world was told; sent to
    /// one joining player it leaves the baseline alone (#2267) — or a level change still waiting for this beat's list would
    /// never reach the others.</summary>
    private CrystalNetList CrystalNetMessage(bool baseline = true)
    {
        var state = CrystalNet;
        var nets = new List<NetCrystalNet>(state.Nets.Count);
        if (baseline)
        {
            state.LastSentLevel.Clear();
        }

        foreach (var net in state.Nets.Values)
        {
            var cells = new int[net.Cells.Count * 3];
            int i = 0;
            foreach (var c in net.Cells)
            {
                cells[i++] = c.X;
                cells[i++] = c.Y;
                cells[i++] = c.Z;
            }

            nets.Add(new NetCrystalNet { Id = net.Id, On = net.Level, Cells = cells });
            if (baseline)
            {
                state.LastSentLevel[net.Id] = net.Level;
            }
        }

        return new CrystalNetList { Nets = nets.ToArray() };
    }

    /// <summary>The whole device list. Broadcast to the world (<paramref name="baseline"/>), it is also what every client was
    /// told from now on — the deltas compare against it (#2267); sent to one joining player it leaves the baseline alone,
    /// or the others would miss the changes still waiting for this tick's delta.</summary>
    private CrystalDeviceList CrystalDeviceMessage(bool baseline = true)
    {
        var state = CrystalNet;
        var devices = state.Cells.Values.Where(c => !c.IsConduit).Select(ToNetCrystalDevice).ToArray();
        if (baseline)
        {
            state.LastSentDevice.Clear();
            foreach (var d in devices)
            {
                state.LastSentDevice[new Vector3i(d.X, d.Y, d.Z)] = CrystalDeviceSignature(d);
            }
        }

        return new CrystalDeviceList { Devices = devices };
    }

    private NetCrystalDevice ToNetCrystalDevice(ServerCrystalCell c) => new()
    {
        Id = c.Id,
        X = c.Cell.X,
        Y = c.Cell.Y,
        Z = c.Cell.Z,
        Kind = c.Kind.ToString(),
        Mode = c.Mode,
        Config = c.Config,
        Label = c.Label,
        OwnerId = c.OwnerId,
        Output = c.Output,
        Choices = c.Kind == CrystalDeviceKind.CloneTank ? CloneChoicesFor(c.OwnerId) : System.Array.Empty<string>(),
    };

    /// <summary>#2097: the species a tank's owner may clone here, as "id|coined name" — the device menu lists exactly these.</summary>
    private string[] CloneChoicesFor(string ownerId)
        => CloneableSpeciesFor(ownerId).Select(sp => sp.SpeciesId + "|" + sp.Name).ToArray();

    private void SendCrystalNet(PlayerSession session)
    {
        Send(session, CrystalNetMessage(baseline: false));
        Send(session, CrystalDeviceMessage(baseline: false)); // #2267: the joiner gets the whole list, the baseline stays
        Send(session, LiftMessage()); // #2266
    }
}
