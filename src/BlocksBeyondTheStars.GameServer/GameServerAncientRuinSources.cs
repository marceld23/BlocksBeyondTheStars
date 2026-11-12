// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using BlocksBeyondTheStars.WorldGeneration.Sources;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// What goes underground (#3333/#3334) — see GameServerAncientRuins.cs. The candidates of a slot are the underground kits
/// (<c>GameContent.UndergroundKitsFor</c>: city and settlement kinds) and the underground whole templates
/// (<c>GameContent.UndergroundTemplatesFor</c>) the planet may carry, drawn by <c>weight</c>; the city slot draws only
/// the G.D.S. ruin kits. An authored source is composed exactly as on the surface — a city kit by <see cref="CityGenerator"/>,
/// a settlement kit by <see cref="SettlementGenerator"/> with <see cref="SettlementLayoutSpec.FromKit"/>, a whole template by
/// <see cref="SettlementGenerator.FromTemplate"/> — and its markers are honoured underground: doors are hung, containers
/// (<c>loot</c>, <c>chest</c>, <c>data_terminal</c>, <c>relic_cache</c>) become loot once ever, pre-built Crystal Net devices
/// join the net. Markers of living things (residents, vendors, boards, guards, posts) are NOT spawned: an underground
/// structure is a find, like a ruined settlement on the surface, not a village — nobody lives down there.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The structure kind of an authored underground structure's containers.</summary>
    internal const string UndergroundLootKind = "underground";

    /// <summary>The container markers an authored underground structure turns into loot.</summary>
    private static readonly HashSet<string> UndergroundLootMarkers = new(System.StringComparer.Ordinal)
    {
        "loot", "chest", "data_terminal", "relic_cache",
    };

    /// <summary>A slot's source by weight (<see cref="UndergroundPick"/>), planned for the active world. Null when there is none.</summary>
    private AncientRuinPlan? PickUndergroundSource(int slot, long seed)
    {
        var (kit, template) = UndergroundPick(slot, _world.LocationId, _world.Planet.Key);
        if (kit is not null)
        {
            return PlanForKit(kit, slot, seed);
        }

        // #3518: a generator entry is pinned as source:<key>, an authored whole template as template:<key>.
        return template is null
            ? null
            : PlanForSource(new AncientRuinPlan
            {
                Slot = slot,
                Seed = seed,
                Fresh = true,
                Source = (template.IsSource ? SourceTemplates.RecordPrefix : "template:") + template.Key,
            });
    }

    /// <summary>What a slot of body <paramref name="locationId"/> (type <paramref name="planetKey"/>) draws by weight: the
    /// planet's underground kits, then its underground whole templates (pool order) and — from generation 38 (#3518) — its
    /// underground generator entries; the city slot only the G.D.S. ruin kits. (null, null) when there is none. A pure function
    /// of the seed and the content — the stamp and the admin's <c>/ressaw</c> prediction (#3421) both draw through it.</summary>
    private (StructureKit? Kit, StructureTemplate? Template) UndergroundPick(int slot, string locationId, string planetKey)
    {
        var packs = _meta.Description.EnabledStructurePacks;
        bool city = slot == UndergroundCitySlot;
        var kits = _content.UndergroundKitsFor(packs, planetKey).Where(k => !city || AncientRuinGenerator.IsGdsRuinKit(k)).ToList();
        var templates = city ? new List<StructureTemplate>() : _content.UndergroundTemplatesFor(packs, planetKey).ToList();
        if (!city && TemplatePipelineWorld)
        {
            templates.AddRange(_content.UndergroundSourceTemplatesFor(packs, planetKey)); // appended: none = the same draw as before
        }

        int total = kits.Sum(k => System.Math.Max(1, k.Weight)) + templates.Sum(t => System.Math.Max(1, t.Weight));
        if (total == 0)
        {
            return (null, null);
        }

        int r = StableHashing.Index($"underground:{slot}:pick:{locationId}", _meta.Seed, total);
        foreach (var k in kits)
        {
            r -= System.Math.Max(1, k.Weight);
            if (r < 0)
            {
                return (k, null);
            }
        }

        foreach (var t in templates)
        {
            r -= System.Math.Max(1, t.Weight);
            if (r < 0)
            {
                return (null, t);
            }
        }

        return (null, null);
    }

    /// <summary>A fresh plan for a kit: a G.D.S. ruin kit keeps its grid for the ruin composer; an authored city or settlement
    /// kit draws its grid as the surface does and is composed right away.</summary>
    private AncientRuinPlan? PlanForKit(StructureKit kit, int slot, long seed)
    {
        var plan = new AncientRuinPlan { Slot = slot, Seed = seed, Fresh = true, Kit = kit, KitKey = kit.Key };
        if (AncientRuinGenerator.IsGdsRuinKit(kit) || kit.KindOrDefault == StructureKit.KindCity)
        {
            plan.Source = AncientRuinGenerator.IsGdsRuinKit(kit) ? SourceGds : "city";
            plan.KitLayout = CityLayoutSpec.FromKit(kit).Serialize();
        }
        else
        {
            string tier = string.IsNullOrWhiteSpace(kit.Tier) ? "village" : kit.Tier;
            plan.Source = "settlement:" + tier;
            plan.KitLayout = SettlementLayoutSpec.FromKit(kit, tier, RngFor(seed, "kitlayout")).Serialize();
        }

        return PlanForSource(plan);
    }

    /// <summary>Completes a plan from its source: the G.D.S. grid, or the composed authored structure; then the box size.
    /// Null when an authored source cannot be composed (a pinned template that is gone, an unreadable grid).</summary>
    private AncientRuinPlan? PlanForSource(AncientRuinPlan plan)
    {
        if (plan.Gds || plan.Source == "city")
        {
            if (!CityLayoutSpec.TryParse(plan.KitLayout, out var spec))
            {
                return null;
            }

            plan.Spec = spec;
        }

        if (plan.Gds)
        {
            plan.Width = plan.Length = plan.Spec.Footprint;
            plan.Height = plan.Spec.Height + AncientRuinGenerator.Headroom;
            return plan;
        }

        plan.Built = ComposeUndergroundSource(plan);
        if (plan.Built is null)
        {
            _log.Warn($"The underground {plan.Source} ({plan.KitKey}) of '{_world.LocationId}' cannot be composed — skipped.");
            return null;
        }

        plan.Width = plan.Built.Width + 2 * AncientRuinGenerator.EnclosureMargin;
        plan.Length = plan.Built.Length + 2 * AncientRuinGenerator.EnclosureMargin;
        plan.Height = plan.Built.Height + AncientRuinGenerator.Headroom;
        return plan;
    }

    /// <summary>An authored source composed as on the surface (its materials resolved for this planet's surface); the pinned
    /// composition replays, a fresh one records.</summary>
    private WorldGeneration.SettlementStructure? ComposeUndergroundSource(AncientRuinPlan plan)
    {
        var planet = _world.Planet;
        string surface = planet.Biomes.Count > 0 ? planet.Biomes[0].SurfaceBlock : planet.SurfaceBlock;
        if (plan.Source == "city")
        {
            return CityGenerator.Generate(plan.Seed, _content, System.Array.Empty<CityGenerator.OpenZone>(), null, 0.0, plan.Composition,
                _log.Warn, plan.Spec, plan.Kit, ModulePool());
        }

        if (plan.Source.StartsWith("settlement:", System.StringComparison.Ordinal))
        {
            string tier = plan.Source.Substring("settlement:".Length);
            return SettlementLayoutSpec.TryParse(plan.KitLayout, out var layout)
                ? SettlementGenerator.Generate(tier, false, plan.Seed, surface, _content, null, 0.0, plan.Composition, _log.Warn, layout,
                    plan.Kit, ModulePool())
                : null;
        }

        if (plan.Source.StartsWith("template:", System.StringComparison.Ordinal)
            && _content.TemplateByKey(StructureKit.KindSettlement, plan.Source.Substring("template:".Length)) is { IsSource: false } t)
        {
            return SettlementGenerator.FromTemplate(t, _content, ModuleMaterials.ForSettlement(t.Tier, surface, alien: false, _content));
        }

        if (SourceTemplates.IsRecord(plan.Source))
        {
            // #3518: a generator entry, built with every row (the enclosure is underground anyway); one that is gone or no
            // longer builds is skipped like any source that cannot be composed (its blocks stay).
            return BuildUndergroundSource(SourceTemplates.KeyOfRecord(plan.Source), plan.Seed, surface);
        }

        return null;
    }

    /// <summary>The plan pinned in a record (#586): source, kit, grid, composition, origin, floor row and the entrance seat.</summary>
    private AncientRuinPlan? UndergroundPlanFromRecord(StructurePlacementRecord rec)
    {
        var seat = rec.Seat.Split(':');
        if (seat.Length != 4
            || !int.TryParse(seat[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)
            || !int.TryParse(seat[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int j)
            || !int.TryParse(seat[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int entranceY))
        {
            _log.Warn($"The underground record {rec.Index} of '{_world.LocationId}' does not parse (seat '{rec.Seat}') — skipped.");
            return null;
        }

        return PlanForSource(new AncientRuinPlan
        {
            Slot = rec.Index,
            Source = rec.Template,
            Kit = rec.Kit.Length > 0 ? _content.KitByKey(rec.Kit) : null,
            KitKey = rec.Kit,
            KitLayout = rec.KitLayout,
            Composition = rec.Composition is { } pinned ? new List<string>(pinned) : new List<string>(),
            OriginX = rec.X,
            OriginZ = rec.Z,
            GroundY = rec.GroundY,
            CrossI = i,
            CrossJ = j,
            Hatch = seat[0] == "hatch",
            EntranceY = entranceY,
            Seed = UndergroundSeed(rec.Index),
        });
    }

    /// <summary>An authored structure's markers, every load: doorways get their doors (measured on the layout, like a
    /// settlement's), container markers their loot once ever, Crystal Net devices join the net; living markers stay unused.</summary>
    private void ApplyUndergroundMarkers(AncientRuinPlan plan, AncientRuinLayout layout)
    {
        var structure = layout.Source!;
        int m = layout.SourceOffset;
        var placed = new PlacedSettlement
        {
            Structure = structure,
            Origin = new Vector3i(plan.OriginX + m, plan.GroundY, plan.OriginZ + m),
            GroundY = plan.GroundY,
            Tier = string.Empty,
            Ruined = false,
            OnIsland = false,
            Name = string.Empty,
            Rng = RngFor(plan.Seed, "markers"),
        };
        var lootRng = RngFor(plan.Seed, "underground-loot");
        bool doors = false;
        foreach (var marker in structure.Markers)
        {
            var cell = new Vector3i(placed.Origin.X + marker.LocalPos.X, plan.GroundY + marker.LocalPos.Y, placed.Origin.Z + marker.LocalPos.Z);
            if (DoorBlocks.IsDoorMarker(marker.Type))
            {
                _settlementMarkers.Add((marker.Type, new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f)));
                RecordAuthoredDoor(placed, marker);
                doors = true;
            }
            else if (UndergroundLootMarkers.Contains(marker.Type))
            {
                SpawnStructureLoot(UndergroundLootKind, marker.Type,
                    new Vector3f(WorldConstants.WrapX(cell.X, _world.Circumference), cell.Y, cell.Z), lootRng);
            }
            else if (marker.Type == TemplateDevices.Marker)
            {
                QueueWorldCircuitCell(cell, marker.Data);
            }
        }

        if (doors)
        {
            RegisterDoors();
        }
    }
}
