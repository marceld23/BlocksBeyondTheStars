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

        public bool Protected { get; set; }

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

        /// <summary>The target of a design: the editor (station or town), its "Use as" and the pool of an entry.</summary>
        public static StampTarget TargetFor(bool station, bool module, bool landmark, bool entry, bool underground, bool cityModule, string? pool)
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

            if (module)
            {
                return cityModule ? StampTarget.CityModule : StampTarget.SettlementModule;
            }

            return landmark ? StampTarget.Landmark : underground ? StampTarget.UndergroundWhole : StampTarget.SettlementWhole;
        }

        /// <summary>Whether the target is a generator entry (no cells: its source builds it).</summary>
        public static bool IsEntry(StampTarget t) => t >= StampTarget.SettlementEntry;

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
        /// fallen settlement, a landmark.</summary>
        public static bool ReadsKeep(StampTarget t) => t is StampTarget.SettlementWhole or StampTarget.SettlementModule or StampTarget.Landmark;

        /// <summary>Only a landmark carves cavity cells (its underground part).</summary>
        public static bool ReadsCavity(StampTarget t) => t == StampTarget.Landmark;

        /// <summary>Where chest, sound and den markers carry their settings (terrain generation 38): settlement whole templates,
        /// settlement and city modules, whole station templates, landmarks — not a station kit module, not an underground
        /// template (enclosed as built).</summary>
        public static bool ReadsMarkerSettings(StampTarget t) => t is StampTarget.SettlementWhole or StampTarget.SettlementModule
            or StampTarget.CityModule or StampTarget.StationWhole or StampTarget.Landmark;

        /// <summary>Whether the target reads the settings of a marker of type <paramref name="marker"/>: a chest, a sound and a den
        /// where <see cref="ReadsMarkerSettings(StampTarget)"/>; a vendor's theme and name only in a landmark.</summary>
        public static bool ReadsMarkerSettings(StampTarget t, string? marker) => marker switch
        {
            "vendor" => t == StampTarget.Landmark,
            LandmarkRules.ChestMarker or LandmarkRules.SoundMarker or LandmarkRules.DenMarker => ReadsMarkerSettings(t),
            _ => false,
        };

        /// <summary>Where material tokens resolve: every settlement-side target (its planet's or the city's materials), a
        /// landmark (village style) and a whole station template (the station hull, generation 38). A station kit module does
        /// not — its composer writes block keys only.</summary>
        public static bool ResolvesTokens(StampTarget t) => t is StampTarget.SettlementWhole or StampTarget.UndergroundWhole
            or StampTarget.SettlementModule or StampTarget.CityModule or StampTarget.Landmark or StampTarget.StationWhole;

        /// <summary>Docking ports are read by the station kit composer only.</summary>
        public static bool ReadsPorts(StampTarget t) => t == StampTarget.StationModule;

        /// <summary>Whether the palette shows an entry of <paramref name="kind"/> / <paramref name="id"/> for the target: a tool
        /// or marker the target ignores is hidden. A generator entry has no cells, so nothing but the plain blocks and markers
        /// stays (the room is read-only anyway).</summary>
        public static bool ShowsInPalette(StampTarget t, string? kind, string? id)
        {
            if (kind == "block" && MaterialTokens.IsToken(id))
            {
                return ResolvesTokens(t);
            }

            return kind switch
            {
                KindPort => ReadsPorts(t),
                KindKeep => ReadsKeep(t),
                KindCavity => ReadsCavity(t),
                KindSettings => ReadsMarkerSettings(t),
                KindDevice => !IsEntry(t),
                "marker" when id is LandmarkRules.SoundMarker or LandmarkRules.DenMarker => ReadsMarkerSettings(t),
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
            Add(list, "ui.ed.warn.ports", ReadsPorts(t) ? 0 : d.PortCells);
            int settings = 0, life = 0;
            foreach (var m in d.Markers)
            {
                bool lifeMarker = m.Id is LandmarkRules.SoundMarker or LandmarkRules.DenMarker;
                if (lifeMarker && !ReadsMarkerSettings(t))
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
