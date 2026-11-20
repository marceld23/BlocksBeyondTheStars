// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration.Sources;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Where a design of the structure editor ends up (#3523): its "Use as" in the editor it is built in. Each target
    /// reads a different part of a template when the server stamps it (<see cref="StructureStampRules"/>).</summary>
    public enum StampTarget
    {
        /// <summary>A whole settlement template on a planet's surface (town editor, whole structure).</summary>
        SettlementWhole,

        /// <summary>A whole template only the underground stamper seats (town editor, "Underground").</summary>
        UndergroundWhole,

        /// <summary>A plot module of a settlement (a plot role or a settlement kit's module).</summary>
        SettlementModule,

        /// <summary>A district module of the walled city (a city role, the metropolis tier or a city kit's module).</summary>
        CityModule,

        /// <summary>A giant building the landmark stamper seats on its own.</summary>
        Landmark,

        /// <summary>A whole space station template (station editor, whole structure).</summary>
        StationWhole,

        /// <summary>A module of a station kit (station editor, kit module).</summary>
        StationModule,

        /// <summary>A generator entry of the settlement pool (the metropolis tier: a city world's city).</summary>
        SettlementEntry,

        /// <summary>A generator entry of the underground pool.</summary>
        UndergroundEntry,

        /// <summary>A generator entry of the landmark pool.</summary>
        LandmarkEntry,

        /// <summary>A generator entry of the station pool.</summary>
        StationEntry,

        /// <summary>#3616: a GIANT SHIP (town editor, <see cref="GiantShipRules"/>) — never built by anyone: it lies wrecked on a
        /// planet (ruin profile crash) and drifts wrecked in space (profile hull). Appended last so the older values keep theirs.</summary>
        GiantShip,

        /// <summary>#3646: a CIVILIZATION template (town editor, a landmark-style template that names a civilization,
        /// docs/developer/CIVILIZATIONS.md) — a site of its pool (giant, city, small) or a prop a prop marker places. Only the
        /// civilization stage stamps it, with the civilization's materials and ruin look; nobody lives there, it is never protected.</summary>
        Civilization,
    }

    /// <summary>A marker cell of a design: its type and its settings line (empty for most).</summary>
    public readonly struct StampMarker
    {
        public StampMarker(string id, string config, int x = 0, int y = 0, int z = 0)
        {
            Id = id ?? string.Empty;
            Config = config ?? string.Empty;
            X = x;
            Y = y;
            Z = z;
        }

        public string Id { get; }

        public string Config { get; }

        public int X { get; }

        public int Y { get; }

        public int Z { get; }
    }

    /// <summary>A warning (or a refusal) about a design: its locale key and how many cells it is about (<c>{0}</c>).</summary>
    public readonly struct StampWarning
    {
        public StampWarning(string key, int count)
        {
            Key = key;
            Count = count;
        }

        public string Key { get; }

        public int Count { get; }
    }

    /// <summary>What the editor knows about a design, Unity-free: its target, its form fields and a summary of its cells.</summary>
    public sealed class StampDesign
    {
        public StampTarget Target { get; set; }

        /// <summary>The size tier (settlement / station) the template carries.</summary>
        public string Tier { get; set; } = string.Empty;

        /// <summary>A settlement module built for aliens (its materials and furniture follow).</summary>
        public bool Alien { get; set; }

        /// <summary>The biome surface block the material tokens and the rubble follow (the editor's surface field).</summary>
        public string Surface { get; set; } = "stone";

        /// <summary>A kit module's function (stations: hub, cabins …).</summary>
        public string Function { get; set; } = string.Empty;

        public int KeepCells { get; set; }

        public int CavityCells { get; set; }

        /// <summary>#3646: air-pocket cells (<see cref="CivilizationRules.AirKind"/>) — they stay air under water.</summary>
        public int AirCells { get; set; }

        public int PortCells { get; set; }

        /// <summary>Cells per material token (<c>@wall</c> …).</summary>
        public Dictionary<string, int> TokenCells { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Every marker cell with its settings line.</summary>
        public List<StampMarker> Markers { get; } = new List<StampMarker>();

        // --- the landmark form

        public bool Ruin { get; set; }

        public string RuinProfile { get; set; } = string.Empty;

        public double RuinStrength { get; set; }

        public int GroundRow { get; set; }

        /// <summary>How a landmark is seated: empty = the guaranteed search, <see cref="LandmarkRules.SeatSpan"/> = across water
        /// or a valley (terrain generation 39).</summary>
        public string Seat { get; set; } = string.Empty;

        /// <summary>A landmark's ruin zones (template coordinates, inclusive corners; terrain generation 39).</summary>
        public List<RuinZone> RuinZones { get; } = new List<RuinZone>();

        // --- a civilization template (#3646)

        /// <summary>The civilization's key (<see cref="StructureTemplate.Civilization"/>).</summary>
        public string Civilization { get; set; } = string.Empty;

        /// <summary>The civilization pool: giant, city, small or prop.</summary>
        public string CivPool { get; set; } = string.Empty;

        /// <summary>A prop's prop pool (<c>statue</c>, <c>remains</c> …).</summary>
        public string PropPool { get; set; } = string.Empty;

        // --- every whole template and generator entry

        /// <summary>The template's protection word (terrain generation 39): empty or <see cref="LandmarkRules.ProtectionAuto"/> =
        /// the rule of its kind, <see cref="LandmarkRules.ProtectionProtected"/>, <see cref="LandmarkRules.ProtectionOpen"/>.</summary>
        public string Protection { get; set; } = string.Empty;

        // --- a generator entry

        /// <summary>The source key of a generator entry.</summary>
        public string Source { get; set; } = string.Empty;

        public int Weight { get; set; } = 1;

        /// <summary>Cells that use any material token.</summary>
        public int TokenCount
        {
            get
            {
                int n = 0;
                foreach (var kv in TokenCells)
                {
                    n += kv.Value;
                }

                return n;
            }
        }

        /// <summary>How many markers have one of <paramref name="ids"/> as their type.</summary>
        public int CountMarkers(params string[] ids)
        {
            int n = 0;
            foreach (var m in Markers)
            {
                n += Array.IndexOf(ids, m.Id) >= 0 ? 1 : 0;
            }

            return n;
        }
    }

    /// <summary>
    /// What each <see cref="StampTarget"/> reads of a template when the server stamps it (#3523, the editor's "honest tools"):
    /// the keep flag only where a ruin pass runs, the cavity only in a landmark, the marker settings (chest, sound, den) where a
    /// generation-38 world honours them, the material tokens where a resolver runs, the docking ports only in a station kit
    /// module. The palette hides what the target ignores, Save warns about what the design still carries, and a station
    /// module refuses tokens (its composer has no resolver: a token cell would be air). Kept in one Unity-free place so the
    /// headless suite pins it against the server's code paths.
    /// </summary>
    public static class StructureStampRules
    {
        public const string PoolSettlement = "settlement";
        public const string PoolUnderground = "underground";
        public const string PoolLandmark = "landmark";
        public const string PoolStation = "station";

        /// <summary>The pools a generator entry of each editor can join, in the order the editor's stepper walks them.</summary>
        public static readonly string[] TownPools = { PoolSettlement, PoolUnderground, PoolLandmark };

        public static readonly string[] StationPools = { PoolStation };

        /// <summary>The palette kinds of the editor's tools.</summary>
        public const string KindKeep = "keep";
        public const string KindCavity = LandmarkRules.CavityKind;
        public const string KindSettings = "settings";
        public const string KindPort = "port";
        public const string KindDevice = "device";

        /// <summary>The zone tool: two clicks in the room set a landmark's ruin zone (terrain generation 39).</summary>
        public const string KindZone = "zone";

        /// <summary>#3646: the air brush — an air pocket that stays air under water (a civilization template's).</summary>
        public const string KindAir = CivilizationRules.AirKind;

        /// <summary>#3616: the relic cache marker — a loot marker a giant ship keeps (<see cref="GiantShipRules.Markers"/>); the
        /// palette offers it to a giant ship only.</summary>
        public const string RelicCacheMarker = "relic_cache";

        /// <summary>The target of a design: the editor (station or town), its "Use as" and the pool of an entry.
        /// <paramref name="giantShip"/> (#3616) is the town editor's "Use as: Giant ship"; <paramref name="civilization"/> (#3646)
        /// a landmark that names a civilization.</summary>
        public static StampTarget TargetFor(bool station, bool module, bool landmark, bool entry, bool underground, bool cityModule, string? pool,
            bool giantShip = false, bool civilization = false)
        {
            if (entry)
            {
                return station ? StampTarget.StationEntry : pool switch
                {
                    PoolUnderground => StampTarget.UndergroundEntry,
                    PoolLandmark => StampTarget.LandmarkEntry,
                    _ => StampTarget.SettlementEntry,
                };
            }

            if (station)
            {
                return module ? StampTarget.StationModule : StampTarget.StationWhole;
            }

            if (giantShip)
            {
                return StampTarget.GiantShip;
            }

            if (module)
            {
                return cityModule ? StampTarget.CityModule : StampTarget.SettlementModule;
            }

            if (landmark)
            {
                return civilization ? StampTarget.Civilization : StampTarget.Landmark;
            }

            return underground ? StampTarget.UndergroundWhole : StampTarget.SettlementWhole;
        }

        /// <summary>Whether the target is a generator entry (no cells: its source builds it).</summary>
        public static bool IsEntry(StampTarget t)
            => t is StampTarget.SettlementEntry or StampTarget.UndergroundEntry or StampTarget.LandmarkEntry or StampTarget.StationEntry;

        /// <summary>Whether the target is a station (the station editor's targets).</summary>
        public static bool IsStation(StampTarget t) => t is StampTarget.StationWhole or StampTarget.StationModule or StampTarget.StationEntry;

        /// <summary>The pool name <see cref="SourceTemplates.Problem"/> checks an entry of the target against, and whether it is
        /// underground.</summary>
        public static (string Pool, bool Underground) RegistryPool(StampTarget t) => t switch
        {
            StampTarget.StationEntry => (StructureKit.KindStation, false),
            StampTarget.LandmarkEntry => (StructureCatalog.LandmarkPool, false),
            StampTarget.UndergroundEntry => (StructureKit.KindSettlement, true),
            _ => (StructureKit.KindSettlement, false),
        };

        /// <summary>Whether a source of <paramref name="kind"/> builds for the pool of the entry target <paramref name="t"/> —
        /// the registry's own rule (<see cref="SourceTemplates.Problem"/>): stations take space sources, the settlement and
        /// landmark pools surface ones, the underground pool surface or underground ones.</summary>
        public static bool PoolTakes(StampTarget t, StructureSourceKind kind) => t switch
        {
            StampTarget.StationEntry => kind == StructureSourceKind.Space,
            StampTarget.UndergroundEntry => kind is StructureSourceKind.Surface or StructureSourceKind.Underground,
            _ => kind == StructureSourceKind.Surface,
        };

        /// <summary>Whether the pool of the entry target <paramref name="t"/> takes <paramref name="source"/>: its kind fits
        /// (<see cref="PoolTakes(StampTarget, StructureSourceKind)"/>) and it may stand as an entry at all
        /// (<see cref="SourceTemplates.CanBeEntry"/> — a station kit composes from the live pool and may not).</summary>
        public static bool PoolTakes(StampTarget t, IStructureSource source)
            => source != null && SourceTemplates.CanBeEntry(source.Key) && PoolTakes(t, source.Kind);

        /// <summary>Whether the editor of <paramref name="station"/> bakes a source of <paramref name="kind"/>: the station editor
        /// space sources, the town editor surface and underground ones (a ship is never offered).</summary>
        public static bool EditorBakes(bool station, StructureSourceKind kind)
            => station ? kind == StructureSourceKind.Space : kind is StructureSourceKind.Surface or StructureSourceKind.Underground;

        /// <summary>Where a ruin pass may run over the template's own cells: a fallen whole settlement, a settlement module of a
        /// fallen settlement, a landmark, a giant ship (always a wreck), a civilization's site.</summary>
        public static bool ReadsKeep(StampTarget t)
            => t is StampTarget.SettlementWhole or StampTarget.SettlementModule or StampTarget.Landmark or StampTarget.GiantShip
                or StampTarget.Civilization;

        /// <summary>A landmark carves cavity cells (its underground part); a giant ship's cavity is carved air on a planet and
        /// plain air in space; a civilization's site is built like a landmark (a sunken one floods its cavities).</summary>
        public static bool ReadsCavity(StampTarget t) => t is StampTarget.Landmark or StampTarget.GiantShip or StampTarget.Civilization;

        /// <summary>#3646: only a civilization's site keeps air pockets — air even under water.</summary>
        public static bool ReadsAir(StampTarget t) => t == StampTarget.Civilization;

        /// <summary>Where chest, sound and den markers carry their settings (terrain generation 38): settlement whole templates,
        /// settlement and city modules, whole station templates, landmarks, giant ships (chests and sounds) — not a station kit
        /// module (it reads its sound marker only, <see cref="ReadsSoundMarker"/>), not an underground template (enclosed as
        /// built). A civilization's site reads them like a landmark.</summary>
        public static bool ReadsMarkerSettings(StampTarget t) => t is StampTarget.SettlementWhole or StampTarget.SettlementModule
            or StampTarget.CityModule or StampTarget.StationWhole or StampTarget.Landmark or StampTarget.GiantShip or StampTarget.Civilization;

        /// <summary>Whether the target reads the settings of a marker of type <paramref name="marker"/>: a chest, a sound and a den
        /// where <see cref="ReadsMarkerSettings(StampTarget)"/> (a giant ship has no den — a wreck is abandoned —, nor has a lost
        /// civilization); a vendor's theme and name only in a landmark; a doorway's axis (<see cref="GiantShipRules.AxisSetting"/>)
        /// only in a giant ship; a prop marker's pool or template (#3646) only in a civilization's site.</summary>
        public static bool ReadsMarkerSettings(StampTarget t, string? marker) => marker switch
        {
            "vendor" => t == StampTarget.Landmark,
            GiantShipRules.DoorMarker => t == StampTarget.GiantShip,
            CivilizationRules.PropMarker => t == StampTarget.Civilization,
            LandmarkRules.DenMarker => t is not (StampTarget.GiantShip or StampTarget.Civilization) && ReadsMarkerSettings(t),
            LandmarkRules.ChestMarker => ReadsMarkerSettings(t),
            LandmarkRules.SoundMarker => ReadsSoundMarker(t),
            _ => false,
        };

        /// <summary>Where a sound marker is read: wherever chests, sounds and dens are
        /// (<see cref="ReadsMarkerSettings(StampTarget)"/>) — and, since the lively places, in a station kit module: the kit
        /// composer carries a module's sound marker with its settings into the station, so a room has a sound of its own. It
        /// is the one marker with settings a kit module reads; chests and dens stay a whole template's.</summary>
        public static bool ReadsSoundMarker(StampTarget t) => ReadsMarkerSettings(t) || t == StampTarget.StationModule;

        /// <summary>Where material tokens resolve: every settlement-side target (its planet's or the city's materials), a
        /// landmark (village style), a whole station template (the station hull, generation 38) and a civilization's site (its
        /// civilization's materials). A station kit module does not — its composer writes block keys only.</summary>
        public static bool ResolvesTokens(StampTarget t) => t is StampTarget.SettlementWhole or StampTarget.UndergroundWhole
            or StampTarget.SettlementModule or StampTarget.CityModule or StampTarget.Landmark or StampTarget.StationWhole
            or StampTarget.Civilization;

        /// <summary>Docking ports are read by the station kit composer only.</summary>
        public static bool ReadsPorts(StampTarget t) => t == StampTarget.StationModule;

        /// <summary>Only a landmark's ruin pass runs zone by zone (its ruin zones, terrain generation 39) — a civilization's site
        /// is built by the same pass.</summary>
        public static bool ReadsZones(StampTarget t) => t is StampTarget.Landmark or StampTarget.Civilization;

        /// <summary>Whether the target names a protection of its own (terrain generation 39): every whole template — a settlement,
        /// an underground structure, a landmark, a station — and every generator entry. A kit module goes with the structure it
        /// is part of; a giant ship's wreck and a civilization's site are always open.</summary>
        public static bool TakesProtection(StampTarget t)
            => t is not (StampTarget.SettlementModule or StampTarget.CityModule or StampTarget.StationModule or StampTarget.GiantShip
                or StampTarget.Civilization);

        /// <summary>
        /// The sentence that says what the design's protection does (the form's line under the picker and the stamping panel's
        /// step): protected, open, or — for <see cref="LandmarkRules.ProtectionAuto"/> — the rule of its kind as the server keeps
        /// it: an intact settlement and a station protected, a fallen settlement and an underground find open, a landmark
        /// protected while it stands intact. A module's protection is its structure's.
        /// </summary>
        public static string ProtectionKey(StampDesign d)
        {
            if (d.Target == StampTarget.GiantShip)
            {
                return "ui.stamp.protection.giant_ship"; // #3616: a wreck names no protection — players may always take it apart
            }

            if (d.Target == StampTarget.Civilization)
            {
                return "ui.stamp.protection.civilization"; // #3646: the server never protects a civilization's site
            }

            if (!TakesProtection(d.Target))
            {
                return "ui.stamp.protection.module";
            }

            switch (LandmarkRules.ProtectionOf(d.Protection))
            {
                case LandmarkRules.ProtectionProtected:
                    return "ui.stamp.protection.protected";
                case LandmarkRules.ProtectionOpen:
                    return "ui.stamp.protection.open";
            }

            return d.Target switch
            {
                StampTarget.StationWhole or StampTarget.StationEntry => "ui.stamp.protection.auto_station",
                StampTarget.UndergroundWhole or StampTarget.UndergroundEntry => "ui.stamp.protection.auto_underground",
                StampTarget.Landmark => d.Ruin ? "ui.stamp.protection.auto_landmark_ruin" : "ui.stamp.protection.auto_landmark_intact",
                StampTarget.LandmarkEntry => "ui.stamp.protection.auto_landmark",
                _ => "ui.stamp.protection.auto_settlement",
            };
        }

        /// <summary>#3615: the palette word of a giant ship's way in — just "Entrance": the settlement word
        /// (<c>ui.marker.entrance</c>, "Entrance (road side)") names a module's road side, and a ship has no road.</summary>
        public const string GiantEntranceLabelKey = "ui.ed.giantship.entrance";

        /// <summary>The locale key of a marker's palette label for the target: <c>ui.marker.&lt;id&gt;</c>, but the giant
        /// ship's own word for its <see cref="GiantShipRules.EntranceMarker"/> (<see cref="GiantEntranceLabelKey"/>).</summary>
        public static string MarkerLabelKey(StampTarget t, string? id)
            => t == StampTarget.GiantShip && id == GiantShipRules.EntranceMarker ? GiantEntranceLabelKey : "ui.marker." + id;

        /// <summary>Whether the palette shows an entry of <paramref name="kind"/> / <paramref name="id"/> for the target: a tool
        /// or marker the target ignores is hidden. A generator entry has no cells, so nothing but the plain blocks and markers
        /// stays (the room is read-only anyway). A giant ship shows exactly its own markers (<see cref="GiantShipRules.Markers"/>);
        /// the relic cache is a giant ship's only. A civilization's site (#3646) shows the markers a lost civilization may carry
        /// (<see cref="CivilizationRules.LostMarkerAllowed"/> — the prop marker among them, the giant ship's relic cache not) and
        /// the air brush.</summary>
        public static bool ShowsInPalette(StampTarget t, string? kind, string? id)
        {
            if (kind == "block" && MaterialTokens.IsToken(id))
            {
                return ResolvesTokens(t);
            }

            if (kind == "marker" && t == StampTarget.GiantShip)
            {
                return Array.IndexOf(GiantShipRules.Markers, id) >= 0;
            }

            if (kind == "marker" && t == StampTarget.Civilization)
            {
                return id != RelicCacheMarker && CivilizationRules.LostMarkerAllowed(id); // every civilization so far is a lost one
            }

            return kind switch
            {
                KindPort => ReadsPorts(t),
                KindKeep => ReadsKeep(t),
                KindCavity => ReadsCavity(t),
                KindSettings => ReadsSoundMarker(t), // a station kit module: for its sound marker (Warnings names any other)
                KindDevice => !IsEntry(t) && t != StampTarget.GiantShip,
                KindZone => ReadsZones(t),
                KindAir => ReadsAir(t),
                "marker" when id is RelicCacheMarker or CivilizationRules.PropMarker => false,
                "marker" when id == LandmarkRules.SoundMarker => ReadsSoundMarker(t),
                "marker" when id == LandmarkRules.DenMarker => ReadsMarkerSettings(t, id),
                "marker" when id == LandmarkRules.ChestMarker && IsStation(t) => t == StampTarget.StationWhole,
                _ => true,
            };
        }

        /// <summary>What the design carries that its target does not read — Save goes ahead and names it in the status line.</summary>
        public static List<StampWarning> Warnings(StampDesign d)
        {
            var list = new List<StampWarning>();
            var t = d.Target;
            Add(list, "ui.ed.warn.keep", ReadsKeep(t) ? 0 : d.KeepCells);
            Add(list, "ui.ed.warn.cavity", ReadsCavity(t) ? 0 : d.CavityCells);
            Add(list, "ui.ed.warn.air", ReadsAir(t) ? 0 : d.AirCells);
            Add(list, "ui.ed.warn.ports", ReadsPorts(t) ? 0 : d.PortCells);
            int settings = 0, life = 0;
            foreach (var m in d.Markers)
            {
                bool lifeMarker = m.Id is LandmarkRules.SoundMarker or LandmarkRules.DenMarker;
                if (lifeMarker && !(m.Id == LandmarkRules.SoundMarker ? ReadsSoundMarker(t) : ReadsMarkerSettings(t)))
                {
                    life++;
                }
                else if (m.Config.Trim().Length > 0 && !ReadsMarkerSettings(t, m.Id))
                {
                    settings++;
                }
            }

            Add(list, "ui.ed.warn.life", life);
            Add(list, "ui.ed.warn.settings", settings);
            return list;
        }

        /// <summary>What blocks a save: material tokens where nothing resolves them (they would stamp as air). Null when the
        /// design may be saved.</summary>
        public static StampWarning? Refusal(StampDesign d)
            => !IsEntry(d.Target) && !ResolvesTokens(d.Target) && d.TokenCount > 0 ? new StampWarning("ui.ed.refuse.tokens", d.TokenCount) : (StampWarning?)null;

        private static void Add(List<StampWarning> list, string key, int count)
        {
            if (count > 0)
            {
                list.Add(new StampWarning(key, count));
            }
        }
    }
}
