// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The placement rules of every stamped structure kind (#3517): <c>data/structure_types.json</c> over the C# defaults below.
/// <para><b>The file.</b> One JSON object; each property is a kind (the names below), its value the fields of a
/// <see cref="StructureTypeDefinition"/> in camelCase. A field left out keeps the kind's default, a kind left out keeps all of
/// them; properties starting with <c>_</c> are comments. A broken entry — unreadable, an unknown kind or field, a number out of
/// range — is reported and dropped (its kind keeps the defaults); the load never fails. The shipped file equals the defaults
/// (a test holds them together), so it documents every number.</para>
/// <para><b>Which fields a kind reads.</b> Every kind: <c>enabled</c>, <c>keepClear</c> and <c>keepClearFrom</c> (generation
/// 38+), <c>minGeneration</c> where it is not 0 — a file can raise it, never lower it below the C# default (that would switch
/// a kind on in older worlds and change their replays). <c>conditions</c>: every kind but <c>monument</c>, <c>wreck</c>,
/// <c>unique_site</c>, <c>text_tablet</c>, <c>sounder_site</c> and <c>net_fragment</c> (a monument stands on airless bodies
/// too; the others follow their type, their own rules or the story). <c>whitelist</c>: every kind but <c>text_tablet</c> and
/// <c>sounder_site</c>, which stand only on their own planet type. Besides:</para>
/// <list type="bullet">
/// <item><c>settlement</c>, <c>lodge</c>, <c>outpost</c>: <c>density</c>, <c>cap</c>, <c>seatPolicy</c> (a ruined settlement
/// seats like a <c>ruin</c>);</item>
/// <item><c>ruin</c>, <c>monument</c>, <c>factory</c>: cumulative <c>thresholds</c> over one roll, <c>cap</c>,
/// <c>seatPolicy</c>; <c>datacube</c>, <c>chest</c>: cumulative <c>thresholds</c>;</item>
/// <item><c>banditcamp</c>: <c>thresholds</c> = the "no camp" odds for Rare, Normal, Frequent, Extreme bandits (four), <c>cap</c>,
/// <c>seatPolicy</c>;</item>
/// <item><c>vault</c>: <c>thresholds</c> = the chance of a first and of a second vault, <c>thresholdCaps</c> their bounds after
/// the Vaults option scales them (two each);</item>
/// <item><c>spslab</c>: <c>minCount</c>..<c>cap</c>, <c>seatPolicy</c>; <c>rail_ruin</c>: <c>chance</c>, <c>seatPolicy</c>;
/// <c>intercity</c>, <c>landmark</c>, <c>underground</c>: <c>chance</c>;</item>
/// <item><c>wreck</c>, <c>unique_site</c>, <c>text_tablet</c>, <c>sounder_site</c>, <c>net_fragment</c>: nothing more.</item>
/// </list>
/// <para>Draw order and the number of draws never depend on these numbers, so the defaults reproduce the worlds stamped before
/// the file existed. The keep-clear margins only act on terrain generation 38+ (the footprint registry); older worlds keep the
/// checks they were stamped with.</para>
/// </summary>
public sealed partial class StructureTypeRules
{
    // ---------------------------------------------------------------- kinds

    public const string Settlement = "settlement";
    public const string Ruin = "ruin";
    public const string RailRuin = "rail_ruin";
    public const string Intercity = "intercity";
    public const string BanditCamp = "banditcamp";
    public const string Lodge = "lodge";
    public const string Outpost = "outpost";
    public const string SpsLab = "spslab";
    public const string Monument = "monument";
    public const string Factory = "factory";
    public const string Wreck = "wreck";
    public const string Landmark = "landmark";
    public const string Underground = "underground";
    public const string Vault = "vault";
    public const string DataCube = "datacube";
    public const string Chest = "chest";
    public const string UniqueSite = "unique_site";
    public const string TextTablet = "text_tablet";
    public const string SounderSite = "sounder_site";
    public const string NetFragment = "net_fragment";

    /// <summary>Not a stamped kind: the landing pads, as the footprint registry and <c>keepClearFrom</c> name them.</summary>
    public const string Pad = "pad";

    /// <summary>Not a stamped kind: the planned crash site of the wreck, reserved before anything else is placed.</summary>
    public const string WreckSite = "wreck_site";

    // ---------------------------------------------------------------- conditions

    /// <summary>The planet has an atmosphere (not airless).</summary>
    public const string ConditionAtmosphere = "atmosphere";

    /// <summary>The planet's air is breathable.</summary>
    public const string ConditionBreathable = "breathable";

    public const string ConditionNotGas = "notGas";
    public const string ConditionNotVoid = "notVoid";

    /// <summary>Not an asteroid: neither an asteroid-family type nor a body of an asteroid field.</summary>
    public const string ConditionNotAsteroid = "notAsteroid";

    /// <summary>Not a type where nobody lives (<see cref="PlanetType.RuinedSettlementsOnly"/>).</summary>
    public const string ConditionNotRuinedOnly = "notRuinedOnly";

    /// <summary>Not a city world (<see cref="PlanetType.CityWorld"/>).</summary>
    public const string ConditionNotCityWorld = "notCityWorld";

    /// <summary>Only on a type whose <see cref="PlanetType.AllowedStructures"/> names the kind's <c>whitelist</c> key, with or
    /// without a structure whitelist (the SPS labs).</summary>
    public const string ConditionNamedByType = "namedByType";

    public static readonly string[] ConditionNames =
    {
        ConditionAtmosphere, ConditionBreathable, ConditionNotGas, ConditionNotVoid, ConditionNotAsteroid,
        ConditionNotRuinedOnly, ConditionNotCityWorld, ConditionNamedByType,
    };

    /// <summary>The seat policies a kind may name (the presets of the server's guaranteed search).</summary>
    public static readonly string[] SeatPolicies = { "inhabited", "ruin", "factory", "camp", "monument" };

    /// <summary>The kinds whose thresholds are read by position, so the count must stay as shipped.</summary>
    private static readonly string[] PositionalThresholds = { BanditCamp, Vault };

    // ---------------------------------------------------------------- the rules

    private readonly Dictionary<string, StructureTypeDefinition> _types;

    private StructureTypeRules(Dictionary<string, StructureTypeDefinition> types) => _types = types;

    /// <summary>The C# defaults: exactly the constants the stampers held before the file (see <see cref="BuildDefaults"/>).</summary>
    public static StructureTypeRules Defaults { get; } = new(BuildDefaults().ToDictionary(t => t.Kind, StringComparer.Ordinal));

    /// <summary>Every kind the file may name, in the boot's stamping order.</summary>
    public static IReadOnlyList<string> Kinds { get; } = BuildDefaults().Select(t => t.Kind).ToArray();

    /// <summary>Every kind's rules, in <see cref="Kinds"/> order.</summary>
    public IEnumerable<StructureTypeDefinition> All => Kinds.Select(Get);

    /// <summary>A kind's rules — the default for a kind the file did not set. Shared: never mutate the result.</summary>
    public StructureTypeDefinition Get(string kind)
        => _types.TryGetValue(kind, out var t) ? t
            : Defaults._types.TryGetValue(kind, out var d) ? d
            : new StructureTypeDefinition { Kind = kind, KeepClear = DefaultKeepClear };

    /// <summary>The margin every kind keeps unless it says otherwise (the settlement collision margin).</summary>
    public const int DefaultKeepClear = 6;

    /// <summary>Whether <paramref name="planet"/> meets every condition of <paramref name="type"/>. <paramref name="asteroid"/>
    /// says whether the body is an asteroid (the type family or the body's kind — only the server knows the body).</summary>
    public static bool ConditionsHold(StructureTypeDefinition type, PlanetType planet, bool asteroid)
    {
        foreach (string c in type.Conditions)
        {
            bool ok = c switch
            {
                ConditionAtmosphere => !planet.IsAirless,
                ConditionBreathable => string.Equals(planet.Atmosphere, "breathable", StringComparison.OrdinalIgnoreCase),
                ConditionNotGas => !planet.IsGasWorld,
                ConditionNotVoid => !planet.Void,
                ConditionNotAsteroid => !asteroid,
                ConditionNotRuinedOnly => !planet.RuinedSettlementsOnly,
                ConditionNotCityWorld => planet.CityWorld.Length == 0,
                ConditionNamedByType => type.Whitelist.Length > 0 && planet.AllowedStructures.Contains(type.Whitelist),
                _ => true, // never reached: the loader drops an entry with an unknown condition
            };
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a type with (<paramref name="restricted"/>) or without a structure whitelist lets the kind stamp: an
    /// unrestricted type always, a restricted one only when its list names the kind's <c>whitelist</c> key.</summary>
    public static bool AllowedOn(StructureTypeDefinition type, PlanetType planet, bool restricted)
        => !restricted || (type.Whitelist.Length > 0 && planet.AllowedStructures.Contains(type.Whitelist));

    /// <summary>Rules from a kind → definition map; kinds not in it keep their defaults.</summary>
    internal static StructureTypeRules From(Dictionary<string, StructureTypeDefinition> types) => new(types);

    // ---------------------------------------------------------------- validation

    /// <summary>What is wrong with a merged definition, one sentence each — empty when it is fine.</summary>
    public static List<string> Problems(StructureTypeDefinition t)
    {
        var problems = new List<string>();
        if (t.MinGeneration < 0 || t.Cap < 0 || t.MinCount < 0 || t.KeepClear < 0)
        {
            problems.Add("minGeneration, cap, minCount and keepClear must not be negative");
        }

        if (t.Chance < 0 || t.Chance > 1 || double.IsNaN(t.Chance) || t.Density < 0 || double.IsNaN(t.Density))
        {
            problems.Add("the chance must lie between 0 and 1 and the density must not be negative");
        }

        if (t.Thresholds.Any(v => v < 0 || v > 1 || double.IsNaN(v)) || t.ThresholdCaps.Any(v => v < 0 || v > 1 || double.IsNaN(v)))
        {
            problems.Add("every threshold and threshold cap must lie between 0 and 1");
        }

        if (Array.IndexOf(PositionalThresholds, t.Kind) < 0 && t.Thresholds.Zip(t.Thresholds.Skip(1), (a, b) => b < a).Any(down => down))
        {
            problems.Add("the thresholds must not decrease");
        }

        var shipped = Defaults.Get(t.Kind);
        if (Array.IndexOf(PositionalThresholds, t.Kind) >= 0
            && (t.Thresholds.Count != shipped.Thresholds.Count || t.ThresholdCaps.Count != shipped.ThresholdCaps.Count))
        {
            problems.Add($"'{t.Kind}' reads its thresholds by position: it needs {shipped.Thresholds.Count} thresholds and {shipped.ThresholdCaps.Count} caps");
        }

        if (t.MinCount > 0 && t.MinCount > t.Cap)
        {
            problems.Add("minCount must not exceed cap");
        }

        if (t.SeatPolicy.Length > 0 && Array.IndexOf(SeatPolicies, t.SeatPolicy) < 0)
        {
            problems.Add($"unknown seat policy '{t.SeatPolicy}' ({string.Join(" | ", SeatPolicies)})");
        }

        problems.AddRange(t.Conditions.Where(c => Array.IndexOf(ConditionNames, c) < 0)
            .Select(c => $"unknown condition '{c}' ({string.Join(" | ", ConditionNames)})"));
        problems.AddRange(t.KeepClearFrom.Where(kv => kv.Value < 0 || (kv.Key != Pad && kv.Key != WreckSite && !Kinds.Contains(kv.Key)))
            .Select(kv => $"keepClearFrom '{kv.Key}' names no kind or is negative"));
        problems.AddRange(t.Site.Problems());
        problems.AddRange(t.Grade.Problems());
        return problems;
    }

    // ---------------------------------------------------------------- the defaults

    /// <summary>
    /// The defaults — the constants of the stampers as they were (#3517): the caps, densities and count thresholds of each
    /// kind, the chances of <c>ServerConfig</c> (rail ruin 0.35, intercity 0.6), <c>LandmarkRules.Chance</c> (0.34) and the
    /// buried ruin's 0.04, the generations the kinds arrived with, the seat policies and the whitelist keys. The keep-clear
    /// margins restate what each stamper reserved (generation 38+ only): 6 everywhere, a hut 40 from towns and camps, 24 from
    /// ruins and other huts, 16 from pads and 14 from the crash site; a landmark 20 from pads and 8 from the crash site; a buried
    /// ruin 10, 30 from pads; a unique site 10.
    /// </summary>
    private static List<StructureTypeDefinition> BuildDefaults()
    {
        (string, int)[] hut = { (Pad, 16), (WreckSite, 14), (Settlement, 40), (BanditCamp, 40), (Ruin, 24), (Lodge, 24), (Outpost, 24) };
        return WithGeography(new List<StructureTypeDefinition>
        {
            Def(TextTablet, minGeneration: WorldDescription.KubaWGeneration),
            Def(SounderSite),
            Def(Settlement, density: 3.0, cap: 8, seat: "inhabited", conditions: new[] { ConditionAtmosphere }),
            Def(Intercity, minGeneration: WorldDescription.IntercityRailGeneration, chance: 0.6,
                conditions: new[] { ConditionNotGas, ConditionNotCityWorld }),
            Def(Ruin, thresholds: new[] { 0.55, 0.85 }, cap: 3, seat: "ruin", conditions: new[] { ConditionAtmosphere }),
            Def(RailRuin, minGeneration: WorldDescription.RailRuinGeneration, chance: 0.35, seat: "factory",
                conditions: new[] { ConditionNotVoid, ConditionAtmosphere, ConditionNotGas }),
            Def(BanditCamp, thresholds: new[] { 0.75, 0.60, 0.45, 0.30 }, cap: 2, seat: "camp",
                conditions: new[] { ConditionAtmosphere, ConditionNotRuinedOnly }),
            Def(Lodge, minGeneration: WorldDescription.WildernessFolkGeneration, density: 2.2, cap: LodgeRules.HardCap, seat: "camp",
                keepClearFrom: Margins(hut),
                conditions: new[] { ConditionNotVoid, ConditionAtmosphere, ConditionNotRuinedOnly, ConditionNotGas, ConditionNotCityWorld }),
            Def(Outpost, minGeneration: WorldDescription.WildernessFolkGeneration, density: 1.4, cap: 2, seat: "camp",
                keepClearFrom: Margins(hut),
                conditions: new[]
                {
                    ConditionNotVoid, ConditionAtmosphere, ConditionNotRuinedOnly, ConditionNotGas, ConditionNotCityWorld, ConditionBreathable,
                }),
            Def(SpsLab, minGeneration: WorldDescription.ExtremePlanetsGeneration, minCount: 3, cap: 6, seat: "camp", whitelist: "sps_labs",
                conditions: new[] { ConditionNotVoid, ConditionNamedByType }),
            Def(Monument, thresholds: new[] { 0.30, 0.75, 0.95 }, cap: 3, seat: "monument", whitelist: "monuments"),
            Def(Factory, thresholds: new[] { 0.70, 0.92 }, cap: 2, seat: "factory", conditions: new[] { ConditionAtmosphere }),
            Def(Wreck),
            Def(Landmark, minGeneration: WorldDescription.LandmarkGeneration, chance: LandmarkRules.Chance,
                whitelist: LandmarkRules.StructureKind, keepClearFrom: Margins((Pad, 20), (WreckSite, 8)),
                conditions: new[] { ConditionNotVoid, ConditionNotGas, ConditionAtmosphere, ConditionNotAsteroid }),
            Def(Underground, minGeneration: WorldDescription.AncientRuinsGeneration, chance: 0.04, whitelist: "gds_ruins", keepClear: 10,
                keepClearFrom: Margins((Pad, 30), (WreckSite, 8)),
                conditions: new[] { ConditionNotVoid, ConditionNotGas }),
            Def(Vault, thresholds: new[] { 0.75, 0.35 }, thresholdCaps: new[] { 0.95, 0.8 }, conditions: new[] { ConditionNotVoid }),
            Def(DataCube, thresholds: new[] { 0.45, 0.80, 0.95 }, conditions: new[] { ConditionNotVoid }),
            Def(NetFragment, whitelist: "net_fragments", keepClear: 0),
            Def(Chest, thresholds: new[] { 0.6, 0.88 }, conditions: new[] { ConditionNotVoid }),
            Def(UniqueSite, keepClear: 10),
        });
    }

    /// <summary>
    /// The geography defaults (terrain generation 40, #3563) laid over the kinds: where each likes to stand and how it
    /// reserves its ground. Settlements by fresh water (20–120 blocks), low to middle ground, flat, on grass or dirt; lodges
    /// by water or the coast, low; bandit camps in a rugged hollow away from water; monuments on a hilltop; outposts on high
    /// ground; factories flat at middle height; landmarks flat with some prominence; ruins, rail ruins and SPS labs neutral.
    /// The grades: a settlement 16 around (cut and fill up to 24), a factory or a landmark likewise, the smaller sites 10–12.
    /// The wreck, the vaults, the tablets, the Sounder sites, the unique sites and the underground kinds reserve nothing.
    /// </summary>
    private static List<StructureTypeDefinition> WithGeography(List<StructureTypeDefinition> kinds)
    {
        foreach (var t in kinds)
        {
            (t.Site, t.Grade) = t.Kind switch
            {
                Settlement => (Site(water: new[] { 20, 120 }, relief: "flat", altitude: new[] { 0.1, 0.6 }, ground: new[] { "grass", "dirt" }),
                    Grade(16, 24, 24)),
                Lodge => (Site(water: new[] { 0, 60 }, coast: new[] { 0, 60 }, prominence: "low"), Grade(10, 12, 12)),
                BanditCamp => (Site(water: new[] { 80, 100000 }, prominence: "low", relief: "rugged",
                    weights: new[] { ("water", 1.0), ("prominence", 2.0) }), Grade(10, 12, 12)),
                Monument => (Site(prominence: "high", weights: new[] { ("prominence", 3.0) }), Grade(10, 16, 16)),
                Outpost => (Site(prominence: "high", altitude: new[] { 0.6, 1.0 }, weights: new[] { ("prominence", 2.0) }), Grade(10, 12, 12)),
                Factory => (Site(relief: "flat", altitude: new[] { 0.3, 0.7 }, weights: new[] { ("relief", 2.0) }), Grade(14, 24, 24)),
                Landmark => (Site(prominence: "high", relief: "flat", weights: new[] { ("relief", 2.0) }), Grade(16, 24, 24)),
                Ruin or RailRuin => (new StructureSiteRule(), Grade(12, 16, 16)),
                SpsLab => (new StructureSiteRule(), Grade(12, 16, 16)),
                _ => (new StructureSiteRule(), new StructureGradeRule()),
            };
        }

        return kinds;
    }

    private static StructureSiteRule Site(int[]? water = null, int[]? coast = null, string prominence = "", string relief = "",
        double[]? altitude = null, string[]? ground = null, (string Term, double Weight)[]? weights = null)
    {
        var rule = new StructureSiteRule
        {
            Water = new List<int>(water ?? Array.Empty<int>()),
            Coast = new List<int>(coast ?? Array.Empty<int>()),
            Prominence = prominence,
            Relief = relief,
            Altitude = new List<double>(altitude ?? Array.Empty<double>()),
            Ground = new List<string>(ground ?? Array.Empty<string>()),
        };
        foreach (var (term, weight) in weights ?? Array.Empty<(string, double)>())
        {
            rule.Weights[term] = weight;
        }

        return rule;
    }

    private static StructureGradeRule Grade(int radius, int maxCut, int maxFill) => new() { Radius = radius, MaxCut = maxCut, MaxFill = maxFill };

    /// <summary>A kind → margin map (a fresh one per call: the defaults never share a dictionary).</summary>
    private static Dictionary<string, int> Margins(params (string Kind, int Margin)[] margins)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (kind, margin) in margins)
        {
            map[kind] = margin;
        }

        return map;
    }

    private static StructureTypeDefinition Def(string kind, int minGeneration = 0, double chance = 0, double density = 0,
        double[]? thresholds = null, double[]? thresholdCaps = null, int minCount = 0, int cap = 0, string seat = "",
        int keepClear = DefaultKeepClear, Dictionary<string, int>? keepClearFrom = null, string[]? conditions = null, string whitelist = "")
        => new()
        {
            Kind = kind,
            MinGeneration = minGeneration,
            Chance = chance,
            Density = density,
            Thresholds = new List<double>(thresholds ?? Array.Empty<double>()),
            ThresholdCaps = new List<double>(thresholdCaps ?? Array.Empty<double>()),
            MinCount = minCount,
            Cap = cap,
            SeatPolicy = seat,
            KeepClear = keepClear,
            KeepClearFrom = keepClearFrom ?? new Dictionary<string, int>(StringComparer.Ordinal),
            Conditions = new List<string>(conditions ?? Array.Empty<string>()),
            Whitelist = whitelist,
        };
}
