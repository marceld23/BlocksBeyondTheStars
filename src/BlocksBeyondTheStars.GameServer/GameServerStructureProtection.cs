// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// PROTECTION, settable per template (terrain generation 39, the school club's landmarks): every whole template —
/// a settlement, a station, a landmark, an underground structure, a generator entry — names its
/// <see cref="StructureTemplate.Protection"/>: <c>auto</c> (the default, exactly the rule its kind always had), <c>protected</c>
/// or <c>open</c>.
/// <list type="bullet">
/// <item><b>Protected</b> boxes go into the footprint registry (a <see cref="StructureFootprints.GuardBox"/> on the structure's own
/// entry, so the keep-clear checks see the same boxes as before) and are asked through ONE query,
/// <see cref="IsProtectedStructureCell(Vector3i)"/>, added beside the station check at every protection site: mining, the area
/// burst and the drills (<c>IsBreakProtected</c>), placing, blueprints, the construction guard, multi-cell forms, the blaster
/// and the pump, the Crystal Net's bridges and pistons, fire, the Guardian's reward, the sound chambers and the NPC miners and
/// gatherers. Where a player acts, admins pass.</item>
/// <item><b>Auto</b> keeps each kind's own check for its kind (the settlement box, the station hull, the factory claim …); a
/// landmark's auto rule is "protected while intact" and lives in the registry too.</item>
/// <item><b>Open</b> switches a kind's own check off for that one structure (an intact settlement's box, a station's hull).</item>
/// </list>
/// A world below generation 39 registers no guard and switches nothing off: every template behaves exactly as before.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Whether the cell lies in a structure box the footprint registry protects (terrain generation 39; never on an
    /// older world). For the world's own processes — fire, NPC work, a device — that have no admin to let through.</summary>
    private bool IsProtectedStructureCell(Vector3i pos)
        => SchoolClubLandmarkWorld && Footprints.IsGuarded(pos.X, pos.Y, pos.Z);

    /// <summary>The same for a player's action: an admin passes (as admins pass a factory's or a base's protection).</summary>
    private bool IsProtectedStructureCell(Vector3i pos, bool actorIsAdmin) => !actorIsAdmin && IsProtectedStructureCell(pos);

    /// <summary>Test seam: <see cref="IsProtectedStructureCell(Vector3i, bool)"/>.</summary>
    internal bool IsProtectedStructureCellForTest(Vector3i pos, bool isAdmin = false) => IsProtectedStructureCell(pos, isAdmin);

    /// <summary>Registers a structure's box (centre, half extents — the keep-clear entry it always had) with its protected cells.</summary>
    private void RegisterProtectedFootprint(string kind, (int Cx, int Cz, int Hw, int Hl) rect, StructureFootprints.GuardBox guard,
        string? owner = null)
        => Footprints.Add(kind, rect, StructureType(kind).KeepClear, owner, guard);

    /// <summary>Registers a structure's box from its corner cells (an instance's Min/Max), the keep-clear entry it always had,
    /// with its protected cells.</summary>
    private void RegisterProtectedFootprintBox(string kind, Vector3i min, Vector3i max, StructureFootprints.GuardBox guard,
        string? owner = null)
        => Footprints.Add(kind, min.X, min.Z, max.X, max.Z, StructureType(kind).KeepClear, owner, guard);

    /// <summary>A template's protection on the active world: its word on generation 39 and later, <c>auto</c> on an older world
    /// (and for no template at all).</summary>
    private string ProtectionOnThisWorld(StructureTemplate? template)
        => SchoolClubLandmarkWorld ? LandmarkRules.ProtectionOf(template) : LandmarkRules.ProtectionAuto;

    /// <summary>The generator entry a station was built from (its record names <c>source:&lt;key&gt;</c>), or null.</summary>
    private StructureTemplate? StationEntryOf(BoardableStation station)
        => _meta.StationTemplates.TryGetValue(station.Id, out var record) && WorldGeneration.Sources.SourceTemplates.IsRecord(record)
            ? _content.TemplateByKey(StructureKit.KindStation, WorldGeneration.Sources.SourceTemplates.KeyOfRecord(record))
            : null;

    /// <summary>The template or generator entry an underground structure was built from (<c>template:&lt;key&gt;</c> or
    /// <c>source:&lt;key&gt;</c>), or null for a G.D.S. ruin and a composed one.</summary>
    private StructureTemplate? UndergroundTemplateOf(string source)
    {
        if (source.StartsWith("template:", System.StringComparison.Ordinal))
        {
            return _content.TemplateByKey(StructureKit.KindSettlement, source.Substring("template:".Length));
        }

        return WorldGeneration.Sources.SourceTemplates.IsRecord(source)
            ? _content.TemplateByKey(StructureKit.KindSettlement, WorldGeneration.Sources.SourceTemplates.KeyOfRecord(source))
            : null;
    }

    /// <summary>An underground structure's place in the registry: its keep-clear box as always; with <c>protected</c>
    /// (generation 39, an authored template or a generator entry) its whole box from the floor to the surface is protected
    /// (an underground structure is open by its kind's rule — <c>auto</c> and <c>open</c> change nothing).</summary>
    private void RegisterUndergroundFootprint(AncientRuinSite site)
    {
        if (!site.Gds && ProtectionOnThisWorld(UndergroundTemplateOf(site.Source)) == LandmarkRules.ProtectionProtected)
        {
            int top = System.Math.Max(site.Origin.Y + site.Height, site.Entrance.Y + 1);
            RegisterProtectedFootprint(StructureTypeRules.Underground, site.Rect, new StructureFootprints.GuardBox(
                site.Origin.X, site.Origin.Y - 1, site.Origin.Z, site.Origin.X + site.Width - 1, top, site.Origin.Z + site.Length - 1));
            return;
        }

        RegisterFootprint(StructureTypeRules.Underground, site.Rect); // #3514
    }

    /// <summary>A settlement's place in the registry (Phase C of the stamp): its keep-clear box as always; with
    /// <c>protected</c> (generation 39) the box down through the foundation (the 48 rows the settlement rule guards) is
    /// protected, with <c>open</c> the settlement rule lets the instance go (<see cref="SettlementInstance.Unguarded"/>).</summary>
    private void RegisterSettlementFootprint(SettlementInstance inst, string protection)
    {
        if (protection == LandmarkRules.ProtectionProtected)
        {
            RegisterProtectedFootprintBox(StructureTypeRules.Settlement, inst.Min, inst.Max,
                new StructureFootprints.GuardBox(inst.Min.X, inst.Min.Y - 48, inst.Min.Z, inst.Max.X, inst.Max.Y, inst.Max.Z));
            return;
        }

        inst.Unguarded = protection == LandmarkRules.ProtectionOpen;
        RegisterFootprintBox(StructureTypeRules.Settlement, inst.Min, inst.Max); // #3514
    }
}
