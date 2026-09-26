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
    }

    /// <summary>One network: its cells and its level. <see cref="Level"/> is re-derived every logic beat.</summary>
    internal sealed class CrystalNetwork
    {
        public int Id { get; init; }
        public HashSet<Vector3i> Cells { get; } = new();
        public bool Level { get; set; }
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
        public bool Applied;            // actuators: the level last applied to the world
        public double LastActuated;     // actuators: uptime of the last world change (rate limit)
        public bool Looping;            // sound devices: a loop is playing
        public TimerState? Timer;       // timer blocks
        public double NextBeat;         // machines: the next move / craft / mine
        public double Progress;         // machines: seconds into the current job
        public int Cursor;              // auto-drill: the next cell index of its volume

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

    /// <summary>Test seam: the number of registered cells (conduits + devices) in the active world.</summary>
    public int CrystalCellCount => CrystalNet.Cells.Count;

    // ------------------------------------------------------------------------------------------------------
    // Registration: place / mine / load
    // ------------------------------------------------------------------------------------------------------

    /// <summary>A block was placed: a conduit or a new device always joins the net; a lamp or an existing port
    /// (beacon, beam pad, sentry, thumper, spout, gate, tray) joins only when a net cell already touches it — so an
    /// old base's lamps stay ordinary lamps until a conduit is laid beside them.</summary>
    private void OnCrystalBlockPlaced(PlayerSession session, Vector3i pos, BlockDefinition def, string label, int intentYaw)
    {
        var kind = CrystalNetRules.KindOf(def);
        if (kind == CrystalDeviceKind.None)
        {
            return;
        }

        bool passive = kind is CrystalDeviceKind.Light or CrystalDeviceKind.Beacon or CrystalDeviceKind.BeamPad
            or CrystalDeviceKind.Sentry or CrystalDeviceKind.Thumper or CrystalDeviceKind.Spout or CrystalDeviceKind.EnergyGate
            or CrystalDeviceKind.HydroTray;
        if (passive && !HasCrystalNeighbour(pos))
        {
            return; // an ordinary lamp / beacon / … until a conduit meets it
        }

        if (CrystalNetRules.IsPlanetOnly(kind) && _world.Planet.Void)
        {
            SendVegaLine(session, "vega.sys.crystal_planet_only", 3);
            return; // a caller / clone tank on a station: creatures never tick there — the block stays decoration
        }

        string owner = session.State.PlayerId;
        string clean = string.IsNullOrEmpty(label) ? string.Empty : (ScreenPlayerName(session, SanitizeBeamName(label), "crystal") ?? string.Empty);
        // A gate's output and a watcher's eye face away from the player who placed it: the quarter turn comes from the
        // intent (the client's rotate key) or, failing that, from the player's facing — and rides in the config row.
        int yaw = intentYaw >= 0 ? intentYaw & 3 : (((int)Math.Round(session.State.Yaw / 90.0) % 4) + 4) % 4;
        string config = CrystalNetRules.IsGate(kind) || kind == CrystalDeviceKind.Watcher ? "yaw=" + yaw : string.Empty;
        var cell = RegisterCrystalCell(pos, kind, def.Key, owner, mode: 0, config, clean, yaw, persist: true);
        if (cell.Inert)
        {
            SendVegaLine(session, "vega.sys.crystal_cap", 3);
        }
        else if (kind == CrystalDeviceKind.Conduit && CrystalNet.Cells.Count == 1)
        {
            SendVegaLine(session, "vega.sys.crystal_first", 3); // the first conduit ever: "connect it to a lamp and a switch"
        }

        DiscoverCrystalNeighbours(pos, owner);
    }

    /// <summary>A block was mined / blasted: its cell leaves the net (row deleted, network split), and lamps or ports
    /// left without any net cell beside them go back to being ordinary blocks.</summary>
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
            if (CrystalNet.Cells.TryGetValue(n, out var other) && IsPassiveKind(other.Kind) && !HasCrystalNeighbour(n, excludePassive: true))
            {
                UnregisterCrystalCell(other, relight: true); // an orphaned lamp lights up again, an orphaned sentry fires again
            }
        }
    }

    private static bool IsPassiveKind(CrystalDeviceKind kind) => kind is CrystalDeviceKind.Light or CrystalDeviceKind.Beacon
        or CrystalDeviceKind.BeamPad or CrystalDeviceKind.Sentry or CrystalDeviceKind.Thumper or CrystalDeviceKind.Spout
        or CrystalDeviceKind.EnergyGate or CrystalDeviceKind.HydroTray;

    /// <summary>Whether a net cell touches this cell. With <paramref name="excludePassive"/> only conduits and real
    /// devices count — two lamps beside each other do not keep each other in the net.</summary>
    private bool HasCrystalNeighbour(Vector3i pos, bool excludePassive = false)
    {
        foreach (var face in CrystalNetRules.Faces)
        {
            if (CrystalNet.Cells.TryGetValue(pos + face, out var c) && !c.Inert && (!excludePassive || !IsPassiveKind(c.Kind)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Registers the lamps and ports beside a fresh net cell (6 block reads, loaded chunks only).</summary>
    private void DiscoverCrystalNeighbours(Vector3i pos, string owner)
    {
        foreach (var face in CrystalNetRules.Faces)
        {
            var n = pos + face;
            if (CrystalNet.Cells.ContainsKey(n))
            {
                continue;
            }

            var def = _content.BlockById(_world.GetBlockIfLoaded(n));
            var kind = CrystalNetRules.KindOf(def);
            if (def is null || !IsPassiveKind(kind))
            {
                continue;
            }

            RegisterCrystalCell(n, kind, def.Key, owner, 0, string.Empty, string.Empty, 0, persist: true);
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
            Yaw = yaw,
        };
        if (kind == CrystalDeviceKind.TimerBlock)
        {
            cell.Timer = new TimerState();
        }

        if (kind == CrystalDeviceKind.AutoDrill && CrystalConfigValue(cell.Config, "key") is null)
        {
            // A multi-key kind remembers WHICH block it is: a Mk3 drill must not come back from its row as a Mk1.
            cell.Config = CrystalConfigWith(cell.Config, "key", blockKey);
        }

        // Level sinks start in their world state: a lamp is lit, a sentry fires, a spout pours — so an OFF network
        // is a change the first beat applies (Applied = the level the world currently shows).
        cell.Applied = kind is CrystalDeviceKind.Light or CrystalDeviceKind.Sentry or CrystalDeviceKind.Spout;

        if (kind == CrystalDeviceKind.Switch)
        {
            cell.Output = mode == 1; // a switch keeps its lever across reloads (mode 1 = ON)
        }

        cell.Inert = OverCrystalCap(cell);
        state.Cells[pos] = cell;
        if (!cell.Inert && !cell.IsGate)
        {
            JoinCrystalNet(cell);
        }

        if (persist)
        {
            SaveCrystalCell(cell);
        }

        state.DeviceListDirty = true;
        state.NetListDirty = true;
        return cell;
    }

    /// <summary>The caps that keep a base from turning a beat into a sweep: networks per world, sensors per world, and
    /// the per-owner machine caps. A cell over a cap registers inert — it exists, it is listed, it does nothing —
    /// and the player is told; mining something frees the slot.</summary>
    private bool OverCrystalCap(ServerCrystalCell cell)
    {
        var state = CrystalNet;
        if (CrystalNetRules.IsSensor(cell.Kind) && state.Cells.Values.Count(c => !c.Inert && CrystalNetRules.IsSensor(c.Kind)) >= CrystalNetRules.MaxSensorsPerWorld)
        {
            return true;
        }

        int ownerCap = cell.Kind switch
        {
            CrystalDeviceKind.AutoDrill => CrystalNetRules.MaxAutoDrillsPerOwner,
            CrystalDeviceKind.MatterSender => CrystalNetRules.MaxMatterSendersPerOwner,
            CrystalDeviceKind.Fabricator => CrystalNetRules.MaxFabricatorsPerOwner,
            CrystalDeviceKind.CloneTank => CrystalNetRules.MaxCloneTanksPerOwner,
            _ => 0,
        };
        if (ownerCap > 0 && state.Cells.Values.Count(c => !c.Inert && c.Kind == cell.Kind && c.OwnerId == cell.OwnerId) >= ownerCap)
        {
            return true;
        }

        if (!cell.IsGate && !HasCrystalNeighbour(cell.Cell) && state.Nets.Count >= CrystalNetRules.MaxNetsPerWorld)
        {
            return true; // a brand-new network over the world cap
        }

        return false;
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
        if (state.NextDeviceId < 1)
        {
            state.NextDeviceId = 1;
        }

        var rows = _repo.ListCrystalCells(_world.LocationId);
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

        if (!state.Subscribed)
        {
            state.Subscribed = true;
            _world.BlockSet += OnCrystalWatchedCellChanged; // #2050: watchers pulse when the cell in front of them changes
        }

        state.ClonesRespawned = false;
        state.NextLogicBeat = _uptime;
        state.NextSensorBeat = _uptime;
    }

    /// <summary>The block key a kind places when the row carries none (old rows, the single-key kinds).</summary>
    private static string KeyForKind(CrystalDeviceKind kind) => kind switch
    {
        CrystalDeviceKind.Conduit => CrystalNetRules.ConduitBlockKey,
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
        CrystalDeviceKind.MatterSender => "matter_sender",
        CrystalDeviceKind.MatterReceiver => "matter_receiver",
        CrystalDeviceKind.Beacon => "radio_beacon",
        CrystalDeviceKind.BeamPad => "beam_block",
        CrystalDeviceKind.Sentry => "sentry_post",
        CrystalDeviceKind.Thumper => "thumper",
        CrystalDeviceKind.Spout => "water_spout",
        CrystalDeviceKind.EnergyGate => "energy_gate",
        CrystalDeviceKind.HydroTray => "hydro_tray",
        _ => string.Empty,
    };

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
                or CrystalDeviceKind.AutoDrill or CrystalDeviceKind.Caller or CrystalDeviceKind.Thumper or CrystalDeviceKind.HydroTray:
                if (!CanConfigureCrystal(cell, me, session.State.IsAdmin))
                {
                    Reject(session, "crystal", "@srv.crystal.owner_only");
                    return;
                }

                TriggerCrystalMachine(cell, session); // a manual start, same path as a signal's rising edge
                return;
            case 2:
                if (!CanConfigureCrystal(cell, me, session.State.IsAdmin))
                {
                    Reject(session, "crystal", "@srv.crystal.owner_only");
                    return;
                }

                int modes = CrystalNetRules.ModeCount(cell.Kind);
                if (modes > 0)
                {
                    cell.Mode = Math.Max(0, Math.Min(modes - 1, intent.Mode));
                }

                string fresh = SanitizeCrystalConfig(intent.Config);
                if (cell.IsGate || cell.Kind == CrystalDeviceKind.Watcher)
                {
                    fresh = CrystalConfigWith(fresh, "yaw", cell.Yaw.ToString(System.Globalization.CultureInfo.InvariantCulture)); // the orientation is not the player's to overwrite
                }

                if (cell.Kind == CrystalDeviceKind.AutoDrill)
                {
                    fresh = CrystalConfigWith(fresh, "key", cell.BlockKey); // nor is the drill's tier
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

    private bool CanConfigureCrystal(ServerCrystalCell cell, string playerId, bool admin)
        => admin || cell.OwnerId.Length == 0 || cell.OwnerId == playerId || AreAllied(cell.OwnerId, playerId);

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
        _ = dt;
        _drillBlocksThisTick = 0;
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
            BroadcastToWorld(CrystalDeviceMessage());
        }
    }

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
            if (c.Inert || c.IsConduit || !c.Output)
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

            var inputs = CrystalGateInputs(c);
            bool next;
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

        // 4. Actuators follow their network.
        bool anyLevelChanged = false;
        foreach (var c in state.Cells.Values)
        {
            if (c.Inert || c.IsConduit || c.IsGate || c.NetId == 0 || !state.Nets.TryGetValue(c.NetId, out var net))
            {
                continue;
            }

            if (net.Level != c.Applied && _uptime - c.LastActuated >= CrystalNetRules.ActuatorMinIntervalSeconds)
            {
                ApplyCrystalActuator(c, net.Level);
                c.Applied = net.Level;
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
            if (net.Level != _crystalLastSentLevel.GetValueOrDefault(net.Id))
            {
                anyLevelChanged = true;
            }
        }

        if (anyLevelChanged)
        {
            state.NetListDirty = true;
        }
    }

    private readonly Dictionary<int, bool> _crystalLastSentLevel = new();

    private int CrystalGateOutputNet(ServerCrystalCell gate)
    {
        var outCell = gate.Cell + CrystalNetRules.OutputFace(gate.Yaw);
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
                    result.Add(c.Output); // gate to gate: read the neighbour's output directly
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
                    if (state.Cells.TryGetValue(cell + face, out var c) && !c.Inert && c.NetId != 0 && state.Nets.TryGetValue(c.NetId, out var net))
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
            if (!c.Inert && c.Kind is CrystalDeviceKind.StepPlate or CrystalDeviceKind.ProximitySensor or CrystalDeviceKind.Beacon)
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
                _ => null,
            };
            if (next is { } level && level != c.Output && c.PulseUntil == 0)
            {
                c.Output = level;
                state.DeviceListDirty = true;
            }
        }
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

    private static bool PresenceMatches(PresenceFilter filter, PresenceFilter kind, string entityOwner, string deviceOwner) => filter switch
    {
        PresenceFilter.Anyone => true,
        PresenceFilter.Players => kind == PresenceFilter.Players,
        PresenceFilter.Owner => kind == PresenceFilter.Players && entityOwner == deviceOwner,
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
    private void ApplyCrystalActuator(ServerCrystalCell c, bool on)
    {
        var state = CrystalNet;
        switch (c.Kind)
        {
            case CrystalDeviceKind.Light:
                SwapCrystalLight(c, on);
                break;
            case CrystalDeviceKind.AlarmSiren:
                SetCrystalLoop(c, on, "alarm_siren_" + Math.Max(0, Math.Min(2, c.Mode)));
                break;
            case CrystalDeviceKind.Chime:
                if (on)
                {
                    PlayCrystalSound(c, "chime_" + Math.Max(0, Math.Min(3, c.Mode)), 1f);
                }

                break;
            case CrystalDeviceKind.Horn:
                if (on)
                {
                    PlayCrystalSound(c, "horn_" + Math.Max(0, Math.Min(2, c.Mode)), 1f);
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
            case CrystalDeviceKind.Caller:
                if (on)
                {
                    TriggerCrystalMachine(c, null); // the rising edge is one job; a held level keeps the machine on its own beat
                }

                break;
        }
    }

    /// <summary>What a sink does when its cell leaves the net (mined, or orphaned): loops stop, a dark lamp lights up
    /// again, a disabled sentry fires again, a closed spout pours again.</summary>
    private void StopCrystalActuator(ServerCrystalCell c, bool relight)
    {
        if (c.Looping)
        {
            SetCrystalLoop(c, false, string.Empty);
        }

        if (c.Kind == CrystalDeviceKind.Light && relight && c.Applied == false)
        {
            SwapCrystalLight(c, true);
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
    }

    /// <summary>Swaps a lamp between its lit block and its unlit twin, keeping dye, glow and form (#2048). One
    /// <see cref="BlockChanged"/> per swap — the reason actuators are rate-limited.</summary>
    private void SwapCrystalLight(ServerCrystalCell c, bool on)
    {
        var current = _content.BlockById(_world.GetBlockIfLoaded(c.Cell));
        if (current is null)
        {
            return;
        }

        string wantKey = on ? CrystalNetRules.LightOnKey(current.Key) : CrystalNetRules.LightOffKey(CrystalNetRules.LightOnKey(current.Key));
        if (wantKey == current.Key)
        {
            return;
        }

        var want = _content.GetBlock(wantKey);
        if (want is null)
        {
            return; // a lamp without a twin (a modded pack) simply stays lit
        }

        var (tint, glow) = _world.GetModifier(c.Cell);
        int shape = _world.GetShape(c.Cell);
        _world.SetBlock(c.Cell, want.NumericId, tint, glow, shape);
        BroadcastToWorld(new BlockChanged { X = c.Cell.X, Y = c.Cell.Y, Z = c.Cell.Z, Block = want.NumericId.Value, Tint = tint, Glow = glow, Shape = shape });
        WriteBackStationCell(c.Cell, want.NumericId, tint, glow, shape);
        c.BlockKey = want.Key;
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

        var crate = AdjacentCrystalCrate(c.Cell, out _);
        if (crate is null || !NpcDepositToContainer(crate, def.Drops))
        {
            return; // no crate, or no room: the crop stays standing
        }

        var (tint, _) = _world.GetModifier(above);
        _world.SetBlock(above, BlockId.Air);
        BroadcastToWorld(new BlockChanged { X = above.X, Y = above.Y, Z = above.Z, Block = BlockId.AirValue });
        WriteBackStationCell(above, BlockId.Air);
        ScheduleFloraRegrow(above, id.Value, tint);
    }

    /// <summary>A rising edge on a beam pad beams everyone standing on it to its paired pad (config <c>pair=&lt;id&gt;</c>),
    /// no menu, no energy — the pad's owner or an ally must own the target, as with a hand beam (#2053).</summary>
    private void BeamStandingPlayers(ServerCrystalCell c)
    {
        int pairId = CrystalConfigInt(c.Config, "pair", 0);
        var target = _beams.FirstOrDefault(b => b.Id == pairId);
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

    private CrystalNetList CrystalNetMessage()
    {
        var state = CrystalNet;
        var nets = new List<NetCrystalNet>(state.Nets.Count);
        _crystalLastSentLevel.Clear();
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
            _crystalLastSentLevel[net.Id] = net.Level;
        }

        return new CrystalNetList { Nets = nets.ToArray() };
    }

    private CrystalDeviceList CrystalDeviceMessage()
        => new()
        {
            Devices = CrystalNet.Cells.Values.Where(c => !c.IsConduit).Select(c => new NetCrystalDevice
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
            }).ToArray(),
        };

    private void SendCrystalNet(PlayerSession session)
    {
        Send(session, CrystalNetMessage());
        Send(session, CrystalDeviceMessage());
    }
}
