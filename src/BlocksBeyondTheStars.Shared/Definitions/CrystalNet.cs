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

    /// <summary>#2092: reads what the device (or door) in front of it is doing and drives the network behind it.</summary>
    DeviceEye,

    /// <summary>#2108: the drill laser — lasers a 1×1 shaft straight down from its own column, one block per beat, the
    /// spoils into the crate beside it (ore and oil; in "only ore" mode the rock is vaporised). Appended last: the kind
    /// travels by name, but an enum index must never shift.</summary>
    DrillLaser,

    /// <summary>#2113: a monorail stop — a train on autopilot halts beside it for a while; a signal's rising edge on its
    /// port departs the halted train at once. Appended last.</summary>
    RailStop,

    // Crystal Net 2 (#2251). Appended in this order; the kind travels by name, an index must never shift.

    /// <summary>#2264: a wall block that turns into shimmering, passable air while its network is ON (swapped with
    /// <c>phase_block_open</c>, keeping its dye) — secret doors. A member of the net like a conduit, so a whole cluster
    /// of phase blocks opens together.</summary>
    PhaseBlock,

    /// <summary>#2264: a floor hatch — closed a walkable plate, open (signal ON) a folded flap you drop through.</summary>
    Trapdoor,

    /// <summary>#2261: the force field as a port — ON switches the field off (passable, not airtight).</summary>
    ForceField,

    /// <summary>#2261: the energy fence as a port — ON lets animals through, like the energy gate.</summary>
    EnergyFence,

    /// <summary>#2261: the campfire as a port — OFF puts it out: no light, no warmth, no cooking.</summary>
    Campfire,

    /// <summary>#2261: the forge as a port — OFF puts it out: no light, no smelting.</summary>
    Forge,

    /// <summary>#2261: the heal tank as a port — OFF stops the healing; its status reads "someone heals here".</summary>
    HealTank,

    /// <summary>#2261: the flower pot as a port — a rising edge harvests its plant into the crate beside it.</summary>
    FlowerPot,

    /// <summary>#2261: a bed or bunk as a port — status only: "someone lies here".</summary>
    Bed,

    /// <summary>#2261: a chair or bench (a seat shape) as a port — status only: "someone sits here".</summary>
    Seat,

    /// <summary>#2265: extends a bridge straight ahead, one deck block per step, while its network is ON; pulls it back
    /// in reverse when OFF.</summary>
    BridgeMotor,

    /// <summary>#2265: pushes the line of blocks in front of it by one cell while ON (mode sticky pulls the first one
    /// back when OFF).</summary>
    Piston,

    /// <summary>#2266: the bottom of a lift shaft — a rising edge sends the platform to the next stop.</summary>
    LiftMotor,

    /// <summary>#2266: a lift stop beside the shaft — a rising edge calls the platform here; status "platform is here".</summary>
    LiftStop,

    /// <summary>#2263: shows a symbol, a line of text or a counter above itself.</summary>
    SignalDisplay,

    /// <summary>#2263: a gate that answers each rising input edge with a pulse — by chance (1 in 2 / 3 / 4 / 6).</summary>
    DiceBlock,

    /// <summary>#2263: listens to its network; its paired signal receivers repeat that level anywhere on the world.</summary>
    SignalSender,

    /// <summary>#2263: a source that repeats its paired sender's level (or the state a remote control set).</summary>
    SignalReceiver,

    /// <summary>#2263: a sensor for the surroundings — no air, too hot, too cold, storm, toxic air, hostile in the base,
    /// owner or alliance in the base.</summary>
    EnvironmentSensor,

    /// <summary>#2268: a sensor aboard the own ship — hull damaged, hull low, shield empty, landed, docked.</summary>
    ShipSensor,
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

    /// <summary>The device's owner and the owner's alliance (allies and crew, #2254).</summary>
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

/// <summary>The display's modes (#2263): what it shows above itself.</summary>
public enum DisplayMode
{
    /// <summary>One symbol while ON, another while OFF (config <c>on=</c>, <c>off=</c>: a symbol index).</summary>
    Symbol = 0,

    /// <summary>Its own screened line while ON (the device's label), nothing while OFF.</summary>
    Text = 1,

    /// <summary>Counts the rising edges of its network, 0..<see cref="CrystalNetRules.DisplayCounterMax"/> (config <c>n=</c>).</summary>
    Counter = 2,
}

/// <summary>The environment sensor's modes (#2263).</summary>
public enum EnvironmentSensorMode
{
    NoAir = 0,
    TooHot = 1,
    TooCold = 2,
    Storm = 3,
    ToxicAir = 4,
    HostileInBase = 5,
    AllianceInBase = 6,
}

/// <summary>The ship sensor's modes (#2268).</summary>
public enum ShipSensorMode
{
    HullDamaged = 0,
    HullLow = 1,
    ShieldEmpty = 2,
    Landed = 3,
    Docked = 4,
}

/// <summary>The piston's modes (#2265): a sticky piston pulls the first block back when its network goes OFF.</summary>
public enum PistonMode
{
    Push = 0,
    Sticky = 1,
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

    /// <summary>Living clones on one world, whoever owns them (#2207): a world full of tanks must never crowd out its own
    /// wildlife or the tick.</summary>
    public const int MaxLivingClonesPerWorld = 16;

    /// <summary>Mined blocks per world per tick across every auto-drill and drill laser (a wake-set style budget).</summary>
    public const int MaxDrillBlocksPerTick = 2;

    /// <summary>The drill laser (#2108): how deep its shaft goes below the device, seconds per block, the drill tier it
    /// cuts up to, and how many one owner may run. Stopped for good by water, lava, bedrock, a protected cell or the
    /// crate never emptied; the depth reached is persisted in the cell's config (<c>depth=</c>) so a reload resumes.</summary>
    public const int DrillLaserDepth = 128;
    public const double DrillLaserBeat = 0.5;
    public const int DrillLaserToolTier = 3;
    public const int MaxDrillLasersPerOwner = 2;

    /// <summary>How far a caller's pulse reaches and how long the animals stay.</summary>
    public const float CallerRange = 24f;
    public const double CallerHoldSeconds = 20.0;

    /// <summary>Seconds a clone takes to grow (#2057).</summary>
    public const double CloneGrowSeconds = 60.0;

    /// <summary>Seconds between two tries of a clone tank whose result cannot be handed over yet (#2214): its owner is
    /// away, or the owner's sample case has no slot for the sample of the new species.</summary>
    public const double CloneHandOverRetrySeconds = 1.0;

    /// <summary>A beacon's "owner near" port radius.</summary>
    public const float BeaconOwnerRange = 12f;

    // ---- Crystal Net 2 (#2251) ----

    /// <summary>#2260: the owner of a pre-built world circuit (settlements, stations, crystal vaults). Anyone may operate
    /// such a device (a vault puzzle must be solvable); only an admin may re-configure it. An EMPTY owner keeps its old
    /// meaning — fully public (the intercity rail stops).</summary>
    public const string WorldOwnerId = "@world";

    /// <summary>#2260: world circuits have a budget of their own and never count against the players' caps.</summary>
    public const int MaxWorldCircuitNets = 8;
    public const int MaxWorldCircuitSensors = 8;

    /// <summary>#2267: one player may use at most this share of a world's networks and sensors, so a single builder cannot
    /// lock everyone else out.</summary>
    public const int MaxNetsPerPlayer = MaxNetsPerWorld / 2;
    public const int MaxSensorsPerPlayer = MaxSensorsPerWorld / 2;

    /// <summary>#2265: the bridge motor — one deck block per step, a length between min and max (menu, config <c>len=</c>).</summary>
    public const double BridgeStepSeconds = 0.25;
    public const int BridgeMinLength = 2;
    public const int BridgeMaxLength = 12;
    public const int BridgeDefaultLength = 6;
    public const int MaxBridgeMotorsPerOwner = 8;

    /// <summary>#2265: the piston pushes a line of at most this many blocks, at most once per interval; a world budget of
    /// pushes per tick across every piston.</summary>
    public const int PistonMaxPush = 4;
    public const double PistonMinIntervalSeconds = 0.5;
    public const int MaxPistonsPerOwner = 8;
    public const int MaxPistonPushesPerTick = 4;

    /// <summary>#2266: the lift — a 3×3 platform, a shaft of at most this height, this many cells per second.</summary>
    public const int LiftPlatformRadius = 1;
    public const int LiftMaxHeight = 48;
    public const float LiftSpeed = 3f;
    public const int MaxLiftsPerOwner = 4;

    /// <summary>#2266: how far beside the shaft a lift stop may stand to belong to it (horizontal cells from the platform's
    /// edge).</summary>
    public const int LiftStopReach = 1;

    /// <summary>#2263: the counter display counts up to this and then stays.</summary>
    public const int DisplayCounterMax = 999;

    /// <summary>#2263: how many symbols the display's picker offers (indices 0..N-1; the client draws them).</summary>
    public const int DisplaySymbolCount = 16;

    /// <summary>#2263: the dice block's chances, one per mode: a pulse with chance 1 in N.</summary>
    public static readonly int[] DiceChances = { 2, 3, 4, 6 };

    /// <summary>#2263: signal senders one player may run (each feeds any number of receivers).</summary>
    public const int MaxSignalSendersPerOwner = 8;

    /// <summary>#2264: seconds after a moving block opened under a player in which a fall does not hurt (kid rule).</summary>
    public const double MovingFallGraceSeconds = 3.0;

    /// <summary>#2268: the caps of a ship's own net (a ship is small).</summary>
    public const int MaxShipNets = 16;
    public const int MaxShipCellsPerNet = 128;
    public const int MaxShipSensors = 8;

    /// <summary>#2268: the ship sensor's "hull low" line, as a fraction of the hull's maximum (VEGA's hull_low uses the same).</summary>
    public const float ShipHullLowFraction = 0.4f;

    /// <summary>#2269: catch-up on return — a gap shorter than this is no absence; edits per tick while catching up; the
    /// per-machine bounds of one return.</summary>
    public const double CatchUpMinGapSeconds = 30.0;
    public const int CatchUpEditsPerTick = 32;
    public const int CatchUpMaxDrillBlocks = 256;
    public const int CatchUpMaxCrafts = 64;
    public const int CatchUpMaxShots = 64;
    public const int CatchUpMaxHarvests = 32;

    /// <summary>#2269: during a catch-up an iron crate / station container counts as full at this many stacks (the live
    /// rule lets them grow without bound).</summary>
    public const int CatchUpCrateStacks = 32;

    /// <summary>#2269: the world rule's choices in minutes (0 = off) and its default.</summary>
    public static readonly int[] CatchUpChoicesMinutes = { 0, 30, 60, 120, 240 };
    public const int CatchUpDefaultMinutes = 60;

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
        ["drill_laser"] = CrystalDeviceKind.DrillLaser,
        ["rail_stop"] = CrystalDeviceKind.RailStop, // #2113
        ["radio_beacon"] = CrystalDeviceKind.Beacon,
        ["beam_block"] = CrystalDeviceKind.BeamPad,
        ["sentry_post"] = CrystalDeviceKind.Sentry,
        ["thumper"] = CrystalDeviceKind.Thumper,
        ["water_spout"] = CrystalDeviceKind.Spout,
        ["energy_gate"] = CrystalDeviceKind.EnergyGate,
        ["hydro_tray"] = CrystalDeviceKind.HydroTray,
        ["device_eye"] = CrystalDeviceKind.DeviceEye,

        // Crystal Net 2 (#2251): devices, and the existing blocks that gained a port — each with its unlit / open twin.
        ["phase_block"] = CrystalDeviceKind.PhaseBlock,
        ["phase_block_open"] = CrystalDeviceKind.PhaseBlock,
        ["trapdoor"] = CrystalDeviceKind.Trapdoor,
        ["trapdoor_open"] = CrystalDeviceKind.Trapdoor,
        ["force_field"] = CrystalDeviceKind.ForceField,
        ["force_field_off"] = CrystalDeviceKind.ForceField,
        ["energy_fence"] = CrystalDeviceKind.EnergyFence,
        ["campfire"] = CrystalDeviceKind.Campfire,
        ["campfire_off"] = CrystalDeviceKind.Campfire,
        ["forge"] = CrystalDeviceKind.Forge,
        ["forge_off"] = CrystalDeviceKind.Forge,
        ["heal_tank"] = CrystalDeviceKind.HealTank,
        ["flower_pot"] = CrystalDeviceKind.FlowerPot,
        ["bed"] = CrystalDeviceKind.Bed,
        ["crew_bunk"] = CrystalDeviceKind.Bed,
        ["bridge_motor"] = CrystalDeviceKind.BridgeMotor,
        ["piston"] = CrystalDeviceKind.Piston,
        ["lift_motor"] = CrystalDeviceKind.LiftMotor,
        ["lift_stop"] = CrystalDeviceKind.LiftStop,
        ["signal_display"] = CrystalDeviceKind.SignalDisplay,
        ["dice_block"] = CrystalDeviceKind.DiceBlock,
        ["signal_sender"] = CrystalDeviceKind.SignalSender,
        ["signal_receiver"] = CrystalDeviceKind.SignalReceiver,
        ["environment_sensor"] = CrystalDeviceKind.EnvironmentSensor,
        ["ship_sensor"] = CrystalDeviceKind.ShipSensor,
    };

    /// <summary>#2251: the twin a block swaps to when its network says <paramref name="on"/> — the open phase block, the
    /// open trapdoor, the switched-off force field, the cold campfire and forge; null when the kind has no twin (lamps use
    /// <see cref="LightOffKey"/>). Signal ON = passage open; for a fire, ON = burning.</summary>
    public static string? TwinKeyFor(CrystalDeviceKind kind, bool on) => kind switch
    {
        CrystalDeviceKind.PhaseBlock => on ? "phase_block_open" : "phase_block",
        CrystalDeviceKind.Trapdoor => on ? "trapdoor_open" : "trapdoor",
        CrystalDeviceKind.ForceField => on ? "force_field_off" : "force_field",
        CrystalDeviceKind.Campfire => on ? "campfire" : "campfire_off",
        CrystalDeviceKind.Forge => on ? "forge" : "forge_off",
        _ => null,
    };

    /// <summary>#2251: the block a twinned port places back when it leaves the net (its plain, unwired state): a closed
    /// phase block and trapdoor, a field that is on, a burning fire.</summary>
    public static string? PlainKeyFor(CrystalDeviceKind kind) => kind switch
    {
        CrystalDeviceKind.PhaseBlock => "phase_block",
        CrystalDeviceKind.Trapdoor => "trapdoor",
        CrystalDeviceKind.ForceField => "force_field",
        CrystalDeviceKind.Campfire => "campfire",
        CrystalDeviceKind.Forge => "forge",
        _ => null,
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

    /// <summary>A gate sits between networks: it is no member of any, it reads its input faces (the Device Eye: the device
    /// in front of it) and drives the network on its drive face.</summary>
    public static bool IsGate(CrystalDeviceKind kind) => kind is CrystalDeviceKind.LogicBlock or CrystalDeviceKind.TimerBlock
        or CrystalDeviceKind.DeviceEye or CrystalDeviceKind.DiceBlock;

    /// <summary>#2092: the kinds whose own output drives their network — the switch, the button, the step plate, the
    /// sensors and the watcher, and (#2263) the signal receiver. Every other member only LISTENS: its report (blocked,
    /// arrived, owner near, has a target, ripe, growing, …) stays a status for its light and for a Device Eye, and never
    /// reads back as its own command.</summary>
    public static bool IsSource(CrystalDeviceKind kind) => kind is CrystalDeviceKind.Switch or CrystalDeviceKind.Button
        or CrystalDeviceKind.StepPlate or CrystalDeviceKind.ProximitySensor or CrystalDeviceKind.DaylightSensor
        or CrystalDeviceKind.StorageSensor or CrystalDeviceKind.Watcher or CrystalDeviceKind.SignalReceiver
        or CrystalDeviceKind.EnvironmentSensor or CrystalDeviceKind.ShipSensor;

    /// <summary>#2092: listeners that act once on a rising edge of their network (a ring, a start, a beam, a harvest). The
    /// rest follow the level (a lamp, a siren, a door, a sentry, a spout, a gate for animals, a beacon's alarm, a phase
    /// block, a bridge, a piston).</summary>
    public static bool IsEdgeSink(CrystalDeviceKind kind) => kind is CrystalDeviceKind.Chime or CrystalDeviceKind.Horn
        or CrystalDeviceKind.MelodyBlock or CrystalDeviceKind.Announcer or CrystalDeviceKind.Thumper
        or CrystalDeviceKind.HydroTray or CrystalDeviceKind.BeamPad or CrystalDeviceKind.Fabricator
        or CrystalDeviceKind.MatterSender or CrystalDeviceKind.CloneTank or CrystalDeviceKind.AutoDrill
        or CrystalDeviceKind.Caller or CrystalDeviceKind.DrillLaser or CrystalDeviceKind.RailStop // #2113: a signal departs the train
        or CrystalDeviceKind.FlowerPot or CrystalDeviceKind.LiftMotor or CrystalDeviceKind.LiftStop;

    /// <summary>Devices the sensor beat polls (world queries, capped per world).</summary>
    public static bool IsSensor(CrystalDeviceKind kind) => kind is CrystalDeviceKind.StepPlate
        or CrystalDeviceKind.ProximitySensor or CrystalDeviceKind.DaylightSensor or CrystalDeviceKind.StorageSensor
        or CrystalDeviceKind.EnvironmentSensor or CrystalDeviceKind.ShipSensor;

    /// <summary>Existing blocks that join the net only when a real net cell (a conduit or a device) touches them, and go
    /// back to their plain behaviour when the last one is mined — so an old base's lamps, fires and fields stay ordinary
    /// blocks until their owner wires them.</summary>
    public static bool IsPassivePort(CrystalDeviceKind kind) => kind is CrystalDeviceKind.Light or CrystalDeviceKind.Beacon
        or CrystalDeviceKind.BeamPad or CrystalDeviceKind.Sentry or CrystalDeviceKind.Thumper or CrystalDeviceKind.Spout
        or CrystalDeviceKind.EnergyGate or CrystalDeviceKind.HydroTray or CrystalDeviceKind.ForceField
        or CrystalDeviceKind.EnergyFence or CrystalDeviceKind.Campfire or CrystalDeviceKind.Forge
        or CrystalDeviceKind.HealTank or CrystalDeviceKind.FlowerPot or CrystalDeviceKind.Bed or CrystalDeviceKind.Seat;

    /// <summary>Ports on blocks that do their job by themselves wherever they stand — a lamp shines, a sentry guards, a
    /// fire warms, a heal tank heals, a bed is a bed — and only gain a switch when a conduit meets them. Unlike a device
    /// (or a beacon, a beam pad, a thumper, a spout, which need the world's place handler for their own function) they
    /// are no decoration in a ship and no "build it aboard" refusal on a station spacewalk (#2219).</summary>
    public static bool IsPlainBlockPort(CrystalDeviceKind kind) => kind is CrystalDeviceKind.Light or CrystalDeviceKind.Sentry
        or CrystalDeviceKind.EnergyGate or CrystalDeviceKind.HydroTray or CrystalDeviceKind.ForceField
        or CrystalDeviceKind.EnergyFence or CrystalDeviceKind.Campfire or CrystalDeviceKind.Forge
        or CrystalDeviceKind.HealTank or CrystalDeviceKind.FlowerPot or CrystalDeviceKind.Bed or CrystalDeviceKind.Seat;

    /// <summary>#2261: passive ports that carry the join on to their own kind — a wired force field switches the whole
    /// field wall it is part of, not one cell of it.</summary>
    public static bool SpreadsToOwnKind(CrystalDeviceKind kind) => kind is CrystalDeviceKind.ForceField or CrystalDeviceKind.EnergyFence;

    /// <summary>Devices that carry a persisted row (owner, mode, config). Conduits and lamps do not: a conduit is
    /// just a block, a lamp only reacts. The existing ports register a row when a conduit meets them, so an old
    /// lamp or beacon joins the net the moment a conduit is laid beside it.</summary>
    public static bool NeedsRow(CrystalDeviceKind kind) => kind != CrystalDeviceKind.None && kind != CrystalDeviceKind.Conduit && kind != CrystalDeviceKind.Light;

    /// <summary>#2267: devices that point somewhere — they store a direction (<c>yaw=</c> 0..5) and wear an arrow.</summary>
    public static bool IsDirectional(CrystalDeviceKind kind) => IsGate(kind) || kind is CrystalDeviceKind.Watcher
        or CrystalDeviceKind.BridgeMotor or CrystalDeviceKind.Piston;

    /// <summary>Devices whose menu (E) opens a picker or a list.</summary>
    public static bool IsConfigurable(CrystalDeviceKind kind) => kind is CrystalDeviceKind.StepPlate
        or CrystalDeviceKind.ProximitySensor or CrystalDeviceKind.DaylightSensor or CrystalDeviceKind.StorageSensor
        or CrystalDeviceKind.LogicBlock or CrystalDeviceKind.TimerBlock or CrystalDeviceKind.AlarmSiren
        or CrystalDeviceKind.Chime or CrystalDeviceKind.Horn or CrystalDeviceKind.MelodyBlock or CrystalDeviceKind.Announcer
        or CrystalDeviceKind.Fabricator or CrystalDeviceKind.CloneTank or CrystalDeviceKind.AutoDrill
        or CrystalDeviceKind.MatterSender or CrystalDeviceKind.BeamPad or CrystalDeviceKind.DrillLaser
        or CrystalDeviceKind.DeviceEye or CrystalDeviceKind.Watcher or CrystalDeviceKind.MatterReceiver
        or CrystalDeviceKind.BridgeMotor or CrystalDeviceKind.Piston or CrystalDeviceKind.LiftMotor or CrystalDeviceKind.LiftStop
        or CrystalDeviceKind.SignalDisplay or CrystalDeviceKind.DiceBlock or CrystalDeviceKind.SignalSender
        or CrystalDeviceKind.SignalReceiver or CrystalDeviceKind.EnvironmentSensor or CrystalDeviceKind.ShipSensor;

    /// <summary>Devices only a planet, moon or asteroid surface can host — creatures never tick on a void (station) world.</summary>
    public static bool IsPlanetOnly(CrystalDeviceKind kind) => kind is CrystalDeviceKind.Caller or CrystalDeviceKind.CloneTank;

    /// <summary>#2268: what works aboard the own ship. Everything else built into a ship is decoration (and VEGA says so
    /// once): a drill has no ground there, a watcher hears only the block changes of a world grid, and a bridge, a piston
    /// or a lift would edit a hull that is meshed in three places.</summary>
    public static bool WorksAboard(CrystalDeviceKind kind) => kind is CrystalDeviceKind.Conduit or CrystalDeviceKind.Switch
        or CrystalDeviceKind.Button or CrystalDeviceKind.StepPlate or CrystalDeviceKind.ProximitySensor
        or CrystalDeviceKind.LogicBlock or CrystalDeviceKind.TimerBlock
        or CrystalDeviceKind.DeviceEye or CrystalDeviceKind.DiceBlock or CrystalDeviceKind.AlarmSiren or CrystalDeviceKind.Chime
        or CrystalDeviceKind.Horn or CrystalDeviceKind.MelodyBlock or CrystalDeviceKind.Announcer or CrystalDeviceKind.Light
        or CrystalDeviceKind.PhaseBlock or CrystalDeviceKind.Trapdoor or CrystalDeviceKind.SignalDisplay
        or CrystalDeviceKind.SignalSender or CrystalDeviceKind.SignalReceiver or CrystalDeviceKind.ShipSensor;

    /// <summary>#2268: the ship sensor reads its ship; anywhere else it is decoration.</summary>
    public static bool IsShipOnly(CrystalDeviceKind kind) => kind == CrystalDeviceKind.ShipSensor;

    /// <summary>#2262: the block that lends its recipes to a fabricator standing right beside it — the forge its refinery
    /// recipes, a lit campfire its cooking, and so on. Workshop and hand recipes need no neighbour; market and factory
    /// recipes never run in a fabricator (null).</summary>
    public static string? FabricatorStationBlock(CraftingStation station) => station switch
    {
        CraftingStation.Refinery => "forge",
        CraftingStation.Campfire => "campfire",
        CraftingStation.Detoxifier => "detoxifier",
        CraftingStation.Transmuter => "matter_forge",
        CraftingStation.AlgaeTank => "algae_tank",
        CraftingStation.Decontaminator => "decontaminator",
        _ => null,
    };

    /// <summary>#2262: whether a fabricator may craft a recipe of this station at all (with the station block beside it
    /// where one is needed).</summary>
    public static bool FabricatorRuns(CraftingStation station)
        => station is CraftingStation.Workshop or CraftingStation.Hand || FabricatorStationBlock(station) is not null;

    /// <summary>#2260: a pre-built world circuit's owner (see <see cref="WorldOwnerId"/>).</summary>
    public static bool IsWorldOwner(string? owner) => owner == WorldOwnerId;

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
        CrystalDeviceKind.DrillLaser => 2,         // AutoDrillMode too: only ore (the rock is vaporised) / everything
        CrystalDeviceKind.CloneTank => 2,          // release automatically / on signal
        CrystalDeviceKind.Piston => 2,             // PistonMode: push / sticky
        CrystalDeviceKind.SignalDisplay => 3,      // DisplayMode: symbol / text / counter
        CrystalDeviceKind.DiceBlock => 4,          // DiceChances: 1 in 2 / 3 / 4 / 6
        CrystalDeviceKind.EnvironmentSensor => 7,  // EnvironmentSensorMode
        CrystalDeviceKind.ShipSensor => 5,         // ShipSensorMode
        _ => 0,
    };

    /// <summary>The six face neighbours of a cell, in a fixed order (+X, −X, +Y, −Y, +Z, −Z).</summary>
    public static readonly Geometry.Vector3i[] Faces =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    /// <summary>#2267: the directions a directional device may point to — yaw 0..3 are the quarter turns (0 = +Z,
    /// 1 = +X, 2 = −Z, 3 = −X), 4 = up, 5 = down.</summary>
    public const int YawUp = 4;
    public const int YawDown = 5;
    public const int DirectionCount = 6;

    /// <summary>The direction a block points to from its stored yaw — the way the player looked when placing it (yaw 0 =
    /// +Z, 1 = +X; #2267: 4 = up, 5 = down). A logic / timer block sends that way; a watcher and a Device Eye look that
    /// way; a bridge motor and a piston push that way. The client draws an arrow on that face (#2093).</summary>
    public static Geometry.Vector3i OutputFace(int yaw) => yaw switch
    {
        YawUp => new Geometry.Vector3i(0, 1, 0),
        YawDown => new Geometry.Vector3i(0, -1, 0),
        _ => (yaw & 3) switch
        {
            0 => new Geometry.Vector3i(0, 0, 1),
            1 => new Geometry.Vector3i(1, 0, 0),
            2 => new Geometry.Vector3i(0, 0, -1),
            _ => new Geometry.Vector3i(-1, 0, 0),
        },
    };

    /// <summary>#2267: a direction stored by a client or an old row, made valid (0..5).</summary>
    public static int ClampYaw(int yaw) => yaw is YawUp or YawDown ? yaw : ((yaw % 4) + 4) % 4;

    /// <summary>#2267: the next direction in the "Turn" cycle of a device menu: the four quarter turns, then up, then down.</summary>
    public static int NextYaw(int yaw) => (ClampYaw(yaw) + 1) % DirectionCount;

    /// <summary>#2267: the direction a placement points to: looking steeply up or down (pitch beyond ±50°) points the device
    /// up or down, else the horizontal quarter turn the player faces. <paramref name="pitchDegrees"/> &gt; 0 looks up.</summary>
    public static int PlacementYaw(float yawDegrees, float pitchDegrees)
    {
        if (pitchDegrees > 50f)
        {
            return YawUp;
        }

        if (pitchDegrees < -50f)
        {
            return YawDown;
        }

        return (((int)Math.Round(yawDegrees / 90.0) % 4) + 4) % 4;
    }

    /// <summary>The face a gate drives: the pointed-to face for logic and timer blocks, the BACK for a Device Eye (it
    /// looks at a device in front and reports behind itself, #2092).</summary>
    public static Geometry.Vector3i DriveFace(CrystalDeviceKind kind, int yaw)
    {
        var f = OutputFace(yaw);
        return kind == CrystalDeviceKind.DeviceEye ? new Geometry.Vector3i(-f.X, -f.Y, -f.Z) : f;
    }
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
    public double Elapsed;     // clock: seconds into the period
    public int Count;          // counter: pulses seen since the last fire

    /// <summary>Delay line (#2095): the timer's own clock and the input edges still travelling through the delay.</summary>
    public double Now;
    private readonly Queue<(double At, bool Level)> _edges = new();

    /// <summary>At most this many edges travel through one delay at a time (a 10 s delay fed by a 0.1 s clock holds 100).</summary>
    public const int MaxDelayEdges = 128;

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
                // A real delay line (#2095): the output is the input as it was `period` seconds ago — rises AND falls,
                // so a button pulse comes out as the same pulse, later. Edges queue with their time; the oldest are
                // released once they are `period` old. The queue is bounded; on overflow the oldest edge is released now.
                Now += dt;
                if (input != LastInput)
                {
                    if (_edges.Count >= MaxDelayEdges)
                    {
                        Output = _edges.Dequeue().Level;
                    }

                    _edges.Enqueue((Now, input));
                }

                while (_edges.Count > 0 && Now - _edges.Peek().At + 1e-9 >= period)
                {
                    Output = _edges.Dequeue().Level;
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
        Now = 0;
        _edges.Clear();
    }
}
