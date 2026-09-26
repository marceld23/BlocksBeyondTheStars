// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// What a cell does in the Crystal Net (#2045). A crystal conduit carries a plain ON/OFF signal between the
/// sources (switches, plates, sensors), the gates (logic and timer blocks, which sit BETWEEN networks) and the
/// sinks (lamps, sound devices, machines). Existing blocks that gained a port — a beam pad, a beacon, a sentry
/// post, the thumper, the water spout, the energy gate, a hydro tray, every lamp — are devices too. Doors are
/// server entities over air cells, not blocks, so they attach through their neighbouring cells instead.
/// </summary>
public enum CrystalDeviceKind
{
    None = 0,
    Conduit,
    Switch,
    Button,
    StepPlate,
    ProximitySensor,
    DaylightSensor,
    StorageSensor,
    Watcher,
    LogicBlock,
    TimerBlock,
    AlarmSiren,
    Chime,
    Horn,
    MelodyBlock,
    Announcer,
    Fabricator,
    Caller,
    CloneTank,
    AutoDrill,
    MatterSender,
    MatterReceiver,

    // Existing blocks with a port.
    Light,
    Beacon,
    BeamPad,
    Sentry,
    Thumper,
    Spout,
    EnergyGate,
    HydroTray,
}

/// <summary>How a door reacts to the Crystal Net: no conduit beside it → <see cref="Normal"/>; a conduit beside it
/// whose network is OFF → <see cref="Locked"/> (never opens, the hand toggle is refused); ON → <see cref="HeldOpen"/>
/// (never closes). Derived every beat, never configured — a kid reads it as "signal on = door open".</summary>
public enum DoorMode
{
    Normal = 0,
    Locked = 1,
    HeldOpen = 2,
}

/// <summary>Who trips a step plate / a proximity sensor (mode picker on the device).</summary>
public enum PresenceFilter
{
    Anyone = 0,
    Players = 1,
    Owner = 2,
    WildCreatures = 3,
    TameCreatures = 4,
    Hostile = 5,
    Npcs = 6,
}

/// <summary>The logic block's modes (mode picker).</summary>
public enum LogicMode
{
    And = 0,
    Or = 1,
    Not = 2,
    Xor = 3,
}

/// <summary>The timer block's modes (mode picker).</summary>
public enum TimerMode
{
    Delay = 0,
    Clock = 1,
    Counter = 2,
    Toggle = 3,
}

/// <summary>The storage sensor's modes.</summary>
public enum StorageSensorMode
{
    Full = 0,
    Empty = 1,
    HasFilterItem = 2,
}

/// <summary>The auto-drill's modes (#2055): "only ore" leaves the terrain standing, "everything" digs a pit.</summary>
public enum AutoDrillMode
{
    OnlyOre = 0,
    Everything = 1,
}

/// <summary>One auto-drill tier (#2055): the square below the device, how deep it goes, how fast, and the drill
/// tier it mines up to. The device is stationary — Marcel's decision — and never mines above its own level.</summary>
public readonly struct AutoDrillTier
{
    public readonly int Radius;      // square half-size around the device column (2 → 5×5)
    public readonly int Depth;       // layers below the device
    public readonly double Beat;     // seconds per mined block
    public readonly int ToolTier;    // mines blocks up to this drill tier

    public AutoDrillTier(int radius, int depth, double beat, int toolTier)
    {
        Radius = radius;
        Depth = depth;
        Beat = beat;
        ToolTier = toolTier;
    }
}

/// <summary>
/// The Crystal Net's rules and caps (#2046) — Unity-free statics the server and the client share. Every number
/// here is a stated cost (mission.md: "new server features must state their tick/memory cost"): the net is
/// evaluated on a slow beat over dirty networks only, sensors poll on a slower beat inside a capped radius, and
/// hard caps keep a relay-tiled base from turning a beat into a world sweep (the power relay's 32-hop precedent).
/// </summary>
public static class CrystalNetRules
{
    /// <summary>Seconds between logic beats: gates and timers advance, network levels are re-derived.</summary>
    public const double LogicBeatSeconds = 0.1;

    /// <summary>Seconds between sensor beats: plates, proximity, daylight, storage and the existing ports are polled.</summary>
    public const double SensorBeatSeconds = 0.5;

    /// <summary>A pulse source (button, watcher, arrival) stays ON this long, then falls back.</summary>
    public const double PulseSeconds = 0.5;

    /// <summary>An actuator changes its world state at most this often (a lamp swap re-meshes a chunk on every client).</summary>
    public const double ActuatorMinIntervalSeconds = 0.5;

    /// <summary>The matter link and the fabricator move / craft one stack per this many seconds while held ON.</summary>
    public const double MoveBeatSeconds = 2.0;

    /// <summary>Items a matter link moves per shot.</summary>
    public const int MoveStackSize = 16;

    public const int MaxCellsPerNet = 256;
    public const int MaxNetsPerWorld = 64;
    public const int MaxSensorsPerWorld = 32;
    public const int MaxSoundDevicesPlayingPerWorld = 8;
    public const int MaxAutoDrillsPerOwner = 4;
    public const int MaxMatterSendersPerOwner = 4;
    public const int MaxFabricatorsPerOwner = 4;
    public const int MaxCloneTanksPerOwner = 2;
    public const int MaxLivingClonesPerOwner = 6;

    /// <summary>Mined blocks per world per tick across every auto-drill (a wake-set style budget).</summary>
    public const int MaxDrillBlocksPerTick = 2;

    /// <summary>How far a caller's pulse reaches and how long the animals stay.</summary>
    public const float CallerRange = 24f;
    public const double CallerHoldSeconds = 20.0;

    /// <summary>Seconds a clone takes to grow (#2057).</summary>
    public const double CloneGrowSeconds = 60.0;

    /// <summary>A beacon's "owner near" port radius.</summary>
    public const float BeaconOwnerRange = 12f;

    /// <summary>Proximity sensor radius per tier index (mode picker "near / mid / far").</summary>
    public static readonly float[] ProximityRadii = { 4f, 6f, 8f };

    public static readonly AutoDrillTier[] DrillTiers =
    {
        new(radius: 2, depth: 8, beat: 2.0, toolTier: 1),
        new(radius: 3, depth: 16, beat: 1.0, toolTier: 2),
        new(radius: 4, depth: 32, beat: 0.5, toolTier: 3),
    };

    public const string ConduitBlockKey = "crystal_conduit";
    public const string LightOffSuffix = "_off";

    private static readonly Dictionary<string, CrystalDeviceKind> KindByBlockKey = new(StringComparer.Ordinal)
    {
        [ConduitBlockKey] = CrystalDeviceKind.Conduit,
        ["crystal_switch"] = CrystalDeviceKind.Switch,
        ["crystal_button"] = CrystalDeviceKind.Button,
        ["step_plate"] = CrystalDeviceKind.StepPlate,
        ["proximity_sensor"] = CrystalDeviceKind.ProximitySensor,
        ["daylight_sensor"] = CrystalDeviceKind.DaylightSensor,
        ["storage_sensor"] = CrystalDeviceKind.StorageSensor,
        ["watcher"] = CrystalDeviceKind.Watcher,
        ["logic_block"] = CrystalDeviceKind.LogicBlock,
        ["timer_block"] = CrystalDeviceKind.TimerBlock,
        ["alarm_siren"] = CrystalDeviceKind.AlarmSiren,
        ["chime"] = CrystalDeviceKind.Chime,
        ["horn"] = CrystalDeviceKind.Horn,
        ["melody_block"] = CrystalDeviceKind.MelodyBlock,
        ["announcer"] = CrystalDeviceKind.Announcer,
        ["fabricator"] = CrystalDeviceKind.Fabricator,
        ["caller"] = CrystalDeviceKind.Caller,
        ["clone_tank"] = CrystalDeviceKind.CloneTank,
        ["auto_drill_1"] = CrystalDeviceKind.AutoDrill,
        ["auto_drill_2"] = CrystalDeviceKind.AutoDrill,
        ["auto_drill_3"] = CrystalDeviceKind.AutoDrill,
        ["matter_sender"] = CrystalDeviceKind.MatterSender,
        ["matter_receiver"] = CrystalDeviceKind.MatterReceiver,
        ["radio_beacon"] = CrystalDeviceKind.Beacon,
        ["beam_block"] = CrystalDeviceKind.BeamPad,
        ["sentry_post"] = CrystalDeviceKind.Sentry,
        ["thumper"] = CrystalDeviceKind.Thumper,
        ["water_spout"] = CrystalDeviceKind.Spout,
        ["energy_gate"] = CrystalDeviceKind.EnergyGate,
        ["hydro_tray"] = CrystalDeviceKind.HydroTray,
    };

    /// <summary>The kind a block key plays in the net, or <see cref="CrystalDeviceKind.None"/> for an ordinary block.
    /// Lamps are recognised by their category, so every <c>category: light</c> block (and its unlit twin) is a
    /// sink without a table entry — the beam pad is a port of its own and keeps its light.</summary>
    public static CrystalDeviceKind KindOf(BlockDefinition? def)
    {
        if (def is null)
        {
            return CrystalDeviceKind.None;
        }

        if (KindByBlockKey.TryGetValue(def.Key, out var kind))
        {
            return kind;
        }

        if (def.Category == "light")
        {
            return CrystalDeviceKind.Light;
        }

        return IsLightOffKey(def.Key) ? CrystalDeviceKind.Light : CrystalDeviceKind.None;
    }

    /// <summary>The kind a block key plays without a definition at hand (persistence, tests).</summary>
    public static CrystalDeviceKind KindOfKey(string key)
        => KindByBlockKey.TryGetValue(key, out var kind) ? kind : (IsLightOffKey(key) ? CrystalDeviceKind.Light : CrystalDeviceKind.None);

    /// <summary>The auto-drill tier index (0..2) of a block key, or -1.</summary>
    public static int DrillTierOf(string key) => key switch
    {
        "auto_drill_1" => 0,
        "auto_drill_2" => 1,
        "auto_drill_3" => 2,
        _ => -1,
    };

    public static bool IsLightOffKey(string key) => key.EndsWith(LightOffSuffix, StringComparison.Ordinal);

    /// <summary>The unlit twin of a lamp key and back: <c>light_white</c> ↔ <c>light_white_off</c>.</summary>
    public static string LightOffKey(string litKey) => litKey + LightOffSuffix;

    public static string LightOnKey(string offKey)
        => IsLightOffKey(offKey) ? offKey.Substring(0, offKey.Length - LightOffSuffix.Length) : offKey;

    /// <summary>A gate sits between networks: it is no member of any, it reads its input faces and drives its output face.</summary>
    public static bool IsGate(CrystalDeviceKind kind) => kind is CrystalDeviceKind.LogicBlock or CrystalDeviceKind.TimerBlock;

    /// <summary>Devices the sensor beat polls (world queries, capped per world).</summary>
    public static bool IsSensor(CrystalDeviceKind kind) => kind is CrystalDeviceKind.StepPlate
        or CrystalDeviceKind.ProximitySensor or CrystalDeviceKind.DaylightSensor or CrystalDeviceKind.StorageSensor;

    /// <summary>Devices that carry a persisted row (owner, mode, config). Conduits and lamps do not: a conduit is
    /// just a block, a lamp only reacts. The existing ports register a row when a conduit meets them, so an old
    /// lamp or beacon joins the net the moment a conduit is laid beside it.</summary>
    public static bool NeedsRow(CrystalDeviceKind kind) => kind != CrystalDeviceKind.None && kind != CrystalDeviceKind.Conduit && kind != CrystalDeviceKind.Light;

    /// <summary>Devices whose menu (E) opens a picker or a list.</summary>
    public static bool IsConfigurable(CrystalDeviceKind kind) => kind is CrystalDeviceKind.StepPlate
        or CrystalDeviceKind.ProximitySensor or CrystalDeviceKind.DaylightSensor or CrystalDeviceKind.StorageSensor
        or CrystalDeviceKind.LogicBlock or CrystalDeviceKind.TimerBlock or CrystalDeviceKind.AlarmSiren
        or CrystalDeviceKind.Chime or CrystalDeviceKind.Horn or CrystalDeviceKind.MelodyBlock or CrystalDeviceKind.Announcer
        or CrystalDeviceKind.Fabricator or CrystalDeviceKind.CloneTank or CrystalDeviceKind.AutoDrill
        or CrystalDeviceKind.MatterSender or CrystalDeviceKind.BeamPad;

    /// <summary>Devices only a planet, moon or asteroid surface can host — creatures never tick on a void (station) world.</summary>
    public static bool IsPlanetOnly(CrystalDeviceKind kind) => kind is CrystalDeviceKind.Caller or CrystalDeviceKind.CloneTank;

    /// <summary>How many picker options a kind's mode has (the client builds the grid from this; 0 = no mode).</summary>
    public static int ModeCount(CrystalDeviceKind kind) => kind switch
    {
        CrystalDeviceKind.StepPlate => 4,          // anyone / players / owner / creatures
        CrystalDeviceKind.ProximitySensor => 7,    // PresenceFilter
        CrystalDeviceKind.DaylightSensor => 2,     // day / night
        CrystalDeviceKind.StorageSensor => 3,      // StorageSensorMode
        CrystalDeviceKind.LogicBlock => 4,         // LogicMode
        CrystalDeviceKind.TimerBlock => 4,         // TimerMode
        CrystalDeviceKind.AlarmSiren => 3,         // three sirens
        CrystalDeviceKind.Chime => 4,              // four chimes
        CrystalDeviceKind.Horn => 3,               // three horns
        CrystalDeviceKind.MelodyBlock => 8,        // eight notes (the instrument rides in Config)
        CrystalDeviceKind.Announcer => 6,          // six preset lines
        CrystalDeviceKind.AutoDrill => 2,          // AutoDrillMode
        CrystalDeviceKind.CloneTank => 2,          // release automatically / on signal
        _ => 0,
    };

    /// <summary>The six face neighbours of a cell, in a fixed order (+X, −X, +Y, −Y, +Z, −Z).</summary>
    public static readonly Geometry.Vector3i[] Faces =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    /// <summary>The horizontal output direction of a gate from its stored yaw (0..3 quarter turns): the face the
    /// player looked at when placing it is the front, so the output leaves "away from the player".</summary>
    public static Geometry.Vector3i OutputFace(int yaw) => (yaw & 3) switch
    {
        0 => new Geometry.Vector3i(0, 0, 1),
        1 => new Geometry.Vector3i(1, 0, 0),
        2 => new Geometry.Vector3i(0, 0, -1),
        _ => new Geometry.Vector3i(-1, 0, 0),
    };
}

/// <summary>The logic block's truth (pure, tested).</summary>
public static class LogicGate
{
    /// <summary>Evaluates a gate over its input levels. NOT is true only when no input is ON (an unconnected NOT
    /// gate is ON — the inverter every airlock needs); AND with no inputs is OFF.</summary>
    public static bool Evaluate(LogicMode mode, IReadOnlyList<bool> inputs)
    {
        int on = 0;
        for (int i = 0; i < inputs.Count; i++)
        {
            if (inputs[i])
            {
                on++;
            }
        }

        return mode switch
        {
            LogicMode.And => inputs.Count > 0 && on == inputs.Count,
            LogicMode.Or => on > 0,
            LogicMode.Not => on == 0,
            LogicMode.Xor => on == 1,
            _ => false,
        };
    }
}

/// <summary>The timer block's state machine (pure, tested). One instance per timer device; stepped on every
/// logic beat with the level of its input network.</summary>
public sealed class TimerState
{
    public bool Output;
    public bool LastInput;
    public double Elapsed;     // delay: seconds since the input rose; clock: seconds into the period
    public int Count;          // counter: pulses seen since the last fire

    /// <summary>Advances the timer by <paramref name="dt"/> seconds. <paramref name="period"/> is the delay /
    /// clock period in seconds (0.5..10), <paramref name="count"/> the counter target (1..64).</summary>
    public void Step(TimerMode mode, bool input, double dt, double period, int count)
    {
        bool rose = input && !LastInput;
        period = Math.Max(0.5, Math.Min(10.0, period));
        count = Math.Max(1, Math.Min(64, count));
        switch (mode)
        {
            case TimerMode.Delay:
                // The output follows the input, but each rise arrives `period` seconds late; a fall is immediate.
                if (input)
                {
                    Elapsed += dt;
                    Output = Elapsed + 1e-9 >= period;
                }
                else
                {
                    Elapsed = 0;
                    Output = false;
                }

                break;
            case TimerMode.Clock:
                // Ticks ON for one beat every `period` seconds while the input is ON (or unconnected = ON).
                if (input)
                {
                    Elapsed += dt;
                    if (Elapsed + 1e-9 >= period)
                    {
                        Elapsed -= period;
                        Output = true;
                    }
                    else
                    {
                        Output = false;
                    }
                }
                else
                {
                    Elapsed = 0;
                    Output = false;
                }

                break;
            case TimerMode.Counter:
                // Fires one beat on the Nth rising edge, then starts over.
                Output = false;
                if (rose)
                {
                    Count++;
                    if (Count >= count)
                    {
                        Count = 0;
                        Output = true;
                    }
                }

                break;
            case TimerMode.Toggle:
                // Each rising edge flips the output — the copper-bulb T-flip-flop, a kid's "click on / click off".
                if (rose)
                {
                    Output = !Output;
                }

                break;
        }

        LastInput = input;
    }

    public void Reset()
    {
        Output = false;
        LastInput = false;
        Elapsed = 0;
        Count = 0;
    }
}
