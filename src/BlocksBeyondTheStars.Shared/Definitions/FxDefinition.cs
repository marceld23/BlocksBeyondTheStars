// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The look of a tool's or a ship module's action effect (#2152): the <c>"fx"</c> object of a tool item in
/// <c>data/items.json</c> or of a module in <c>data/ship_modules.json</c>. Purely cosmetic — the server never reads it
/// for a gameplay decision. The client picks the effect builder by <see cref="Style"/> (one of <see cref="FxStyles"/>)
/// and tints it with the colours, so a new tool gets its look from data alone, without a code change.
/// </summary>
public sealed class FxDefinition
{
    /// <summary>The effect builder, one of <see cref="FxStyles"/> (e.g. <c>"laser"</c>, <c>"drill_hot"</c>). An unknown
    /// style makes the client fall back to its key/kind heuristics.</summary>
    public string Style { get; set; } = string.Empty;

    /// <summary>Primary colour as <c>#rrggbb</c> (sRGB) — the glow, the beam, the trail.</summary>
    public string? Color { get; set; }

    /// <summary>Optional secondary colour as <c>#rrggbb</c> (sRGB) — the hot core / highlight. Missing = a lightened
    /// <see cref="Color"/>.</summary>
    public string? Color2 { get; set; }

    /// <summary>Seconds of cosmetic charge-up glow before the shot leaves (the gauss coils, a heavy beam); 0 = none.
    /// Never delays the authoritative hit — the server resolves the shot when the intent arrives.</summary>
    public float Charge { get; set; }

    /// <summary>Scale factor of the whole effect; 1 = the style's normal size.</summary>
    public float Size { get; set; } = 1f;

    /// <summary>Travelling styles only (<c>slug</c>, <c>plasma</c>, <c>plasma_bolt</c>, <c>twin_pulse</c>): projectile
    /// speed in blocks per second; 0 = the style's default.</summary>
    public float Speed { get; set; }
}

/// <summary>The effect styles an <see cref="FxDefinition"/> may name (#2152). Each one is a distinct builder on the
/// client; content picks one by its string.</summary>
public static class FxStyles
{
    // On-foot weapons.
    public const string Slug = "slug";
    public const string Rail = "rail";
    public const string Laser = "laser";
    public const string Plasma = "plasma";
    public const string Slash = "slash";
    public const string Vibro = "vibro";
    public const string PlasmaBlade = "plasma_blade";
    public const string Fist = "fist";
    public const string ShockPush = "shock_push";   // #2278: the shock gloves' two-handed push — a forward air ring
    public const string EnergyFist = "energy_fist"; // #2278: the energy gloves' left-right punches — gold arcs

    // Drills.
    public const string Drill = "drill";
    public const string DrillHot = "drill_hot";
    public const string DrillCrystal = "drill_crystal";
    public const string MiningBeam = "mining_beam";

    // Scanners.
    public const string Scan = "scan";
    public const string ScanPro = "scan_pro";

    // Gadgets.
    public const string Blueprint = "blueprint";
    public const string HealPulse = "heal_pulse";
    public const string Stasis = "stasis";
    public const string Blast = "blast";
    public const string Pump = "pump";
    public const string TerrainScan = "terrain_scan";
    public const string Translate = "translate";
    public const string WeatherScan = "weather_scan";
    public const string Generic = "generic";

    // Ship modules.
    public const string TwinPulse = "twin_pulse";
    public const string PlasmaBolt = "plasma_bolt";
    public const string HeavyBeam = "heavy_beam";
    public const string DrillBeam = "drill_beam";
    public const string Tractor = "tractor";
    public const string PlanetScan = "planet_scan";
    public const string Shield = "shield";
    public const string Warp = "warp";
    public const string ShipScan = "ship_scan"; // #2237: the ship scanner in the flight hotbar (cockpit, Quantum scanner)

    private static readonly HashSet<string> Known = new(System.StringComparer.Ordinal)
    {
        Slug, Rail, Laser, Plasma, Slash, Vibro, PlasmaBlade, Fist, ShockPush, EnergyFist,
        Drill, DrillHot, DrillCrystal, MiningBeam,
        Scan, ScanPro,
        Blueprint, HealPulse, Stasis, Blast, Pump, TerrainScan, Translate, WeatherScan, Generic,
        TwinPulse, PlasmaBolt, HeavyBeam, DrillBeam, Tractor, PlanetScan, Shield, Warp, ShipScan,
    };

    /// <summary>Every known style.</summary>
    public static IReadOnlyCollection<string> All => Known;

    /// <summary>True for a style the client has a builder for (case-sensitive, as written in the data).</summary>
    public static bool IsKnown(string? style) => style != null && Known.Contains(style);
}

/// <summary>What a cosmetic action effect shows (#2158) — the <c>Kind</c> byte of the <c>FxIntent</c> / <c>ActionFx</c>
/// messages. 0 is never valid, so a default-constructed message is dropped.</summary>
public static class FxActionKinds
{
    /// <summary>A ranged weapon shot (or a ship weapon in space).</summary>
    public const byte Shot = 1;

    /// <summary>A melee swing (blade, fists).</summary>
    public const byte Melee = 2;

    /// <summary>Drilling / mining a block.</summary>
    public const byte Mine = 3;

    /// <summary>A scanner pulse.</summary>
    public const byte Scan = 4;

    /// <summary>A gadget use (the server-confirmed outcome travels with <c>ActionFx.Outcome</c> = true).</summary>
    public const byte Gadget = 5;

    /// <summary>Placing a block.</summary>
    public const byte Place = 6;

    /// <summary>True for one of the kinds above.</summary>
    public static bool IsValid(byte kind) => kind is >= Shot and <= Place;
}
