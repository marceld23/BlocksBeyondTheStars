// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The structure stage's shared placement plumbing: the footprint registry of the active world (#3514, see
/// <see cref="StructureFootprints"/>) and the placement rules of <c>data/structure_types.json</c> (#3517, see
/// <see cref="StructureTypeRules"/>).
/// <para><b>Registration</b> runs on every generation and every load: the pads and the planned crash site first
/// (<see cref="RegisterBootFootprints"/>), then each stamper's boxes as it stamps or replays them. <b>Queries</b> through the
/// registry run on terrain generation 38+ only (<see cref="UsesFootprintRegistry"/>): there every keep-clear check — the
/// placement searches' reserved lists (<see cref="KeepClearFor"/>) and the point checks of the vaults, cubes, chests, unique
/// sites and the wreck (<see cref="KeepClearBlocked"/>) — sees every structure stamped before it, the monuments, ruins, camps,
/// factories and labs included. Older worlds keep exactly the checks they were stamped with.</para>
/// </summary>
public sealed partial class GameServer
{
    private StructureFootprints Footprints => _worlds.Active.Footprints;

    /// <summary>Whether the active world keeps clear through the registry (terrain generation 38+).</summary>
    private bool UsesFootprintRegistry => _generator.TerrainGeneration >= WorldDescription.StructurePipelineGeneration;

    /// <summary>A kind's placement rules (the shipped file over the C# defaults).</summary>
    private StructureTypeDefinition StructureType(string kind) => _content.StructureTypes.Get(kind);

    /// <summary>The margins a placement of <paramref name="kind"/> keeps, per registered box (its kind and own margin).</summary>
    private Func<string, int, int> KeepClearMargins(string kind)
    {
        var type = StructureType(kind);
        return (other, own) => type.MarginFrom(other, own);
    }

    // ---------------------------------------------------------------- registering

    /// <summary>Registers a box (centre, half extents) with its kind's margin.</summary>
    private void RegisterFootprint(string kind, (int Cx, int Cz, int Hw, int Hl) rect, string? owner = null)
        => Footprints.Add(kind, rect, StructureType(kind).KeepClear, owner);

    /// <summary>Registers a structure's box from its origin corner and size (the shape every stamper reserved).</summary>
    private void RegisterFootprint(string kind, int originX, int originZ, int width, int length, string? owner = null)
        => RegisterFootprint(kind, (originX + (width / 2), originZ + (length / 2), (width / 2) + 1, (length / 2) + 1), owner);

    /// <summary>Registers a structure's box from its corner cells (an instance's Min/Max).</summary>
    private void RegisterFootprintBox(string kind, Vector3i min, Vector3i max, string? owner = null)
        => Footprints.Add(kind, min.X, min.Z, max.X, max.Z, StructureType(kind).KeepClear, owner);

    /// <summary>The structure stage's first step: a fresh registry with the landing pads and the planned crash site — what
    /// every stamper has kept clear of since the pads existed.</summary>
    private void RegisterBootFootprints()
    {
        Footprints.Clear();
        Footprints.Circumference = _world.Circumference;
        foreach (var pad in _landingPads)
        {
            RegisterFootprint(StructureTypeRules.Pad, (pad.CenterX, pad.CenterZ, LandingPadRadius + 2, LandingPadRadius + 2));
        }

        RegisterFootprint(StructureTypeRules.WreckSite, PlannedWreckSite());
    }

    /// <summary>The crash site kept free for the wreck: a generation-38 wreck that stands already reserves its recorded box
    /// (<see cref="PinWreckBox"/>); otherwise the box around the anchor at a fixed offset from pad 0, as large as the hull a
    /// generation-38 wreck is built from (it is centred on the anchor), else the classic reservation.</summary>
    private (int Cx, int Cz, int Hw, int Hl) PlannedWreckSite()
    {
        if (UsesFootprintRegistry && FindPlacementRecord("wreck", 0) is { Placed: true, Width: > 0, Length: > 0 } rec)
        {
            return (rec.X + (rec.Width / 2), rec.Z + (rec.Length / 2), (rec.Width / 2) + 1, (rec.Length / 2) + 1);
        }

        var (ax, az) = WreckAnchorFor(_landingPads);
        if (UsesFootprintRegistry && LayoutWreckPick() is { } pick && pick.Layout.Cells.Count > 0)
        {
            int w = pick.Layout.Cells.Max(c => c.X) - pick.Layout.Cells.Min(c => c.X) + 1;
            int l = pick.Layout.Cells.Max(c => c.Z) - pick.Layout.Cells.Min(c => c.Z) + 1;
            return (ax, az, (w / 2) + 1, (l / 2) + 1);
        }

        return (ax, az, WreckReservedHalfExtent, WreckReservedHalfExtent);
    }

    /// <summary>Once the wreck stands: its own box replaces the crash-site reservation (a layout wreck whose layout is gone has
    /// no box — the reservation stays).</summary>
    private void RegisterWreckFootprint(WreckStructure? structure, int originX, int originZ)
    {
        if (structure is null)
        {
            return;
        }

        Footprints.RemoveKind(StructureTypeRules.WreckSite);
        RegisterFootprint(StructureTypeRules.Wreck, originX, originZ, structure.Width, structure.Length);
    }

    /// <summary>Registers a lodge or a pioneer outpost — the hut, and an outpost's boat as part of the same structure (the boxes
    /// the later stampers reserved for them, <see cref="AppendLodgeReservations"/>).</summary>
    private void RegisterLodgeFootprint(LodgeInstance lodge)
    {
        string kind = lodge.Outpost is null ? StructureTypeRules.Lodge : StructureTypeRules.Outpost;
        string owner = kind + "@" + lodge.Origin.X + ":" + lodge.Origin.Z;
        RegisterFootprint(kind, (lodge.Origin.X + (lodge.Layout.Width / 2), lodge.Origin.Z + (lodge.Layout.Length / 2),
            (lodge.Layout.Width / 2) + 2, (lodge.Layout.Length / 2) + 2), owner);
        if (OutpostBoatBox(lodge) is { } boat)
        {
            RegisterFootprint(kind, (boat.Cx, boat.Cz, boat.Hw + 2, boat.Hl + 2), owner);
        }
    }

    // ---------------------------------------------------------------- keeping clear

    /// <summary>The reserved footprints a placement search of <paramref name="kind"/> takes: on generation 38+ the registry's
    /// boxes grown to the kind's margins (<see cref="StructureFootprints.Reserved"/>); on an older world
    /// <paramref name="legacy"/> — the list the stamper always built — untouched.</summary>
    private List<(int Cx, int Cz, int Hw, int Hl)> KeepClearFor(string kind, List<(int Cx, int Cz, int Hw, int Hl)> legacy,
        string? exceptKind = null)
        => UsesFootprintRegistry ? Footprints.Reserved(KeepClearMargins(kind), SettlementCollisionMargin, exceptKind) : legacy;

    /// <summary>The point check of the scattered kinds: whether a square of <paramref name="halfExtent"/> around (x, z) comes too
    /// close to a registered box (generation 38+), else — on an older world — exactly <see cref="OverlapsAnySettlement"/>.</summary>
    private bool KeepClearBlocked(string kind, int x, int z, int halfExtent, string? exceptKind = null)
        => UsesFootprintRegistry
            ? Footprints.Overlaps(x, z, halfExtent, halfExtent, KeepClearMargins(kind), exceptKind)
            : OverlapsAnySettlement(x, z, halfExtent);

    /// <summary>The wreck's nudge off the anchor: the point check of <see cref="KeepClearBlocked"/>, its own crash-site
    /// reservation aside (generation 38+; an older world asks <see cref="OverlapsAnySettlement"/> as it always did).</summary>
    private bool WreckSiteBlocked(int x, int z, int halfExtent)
        => KeepClearBlocked(StructureTypeRules.Wreck, x, z, halfExtent, StructureTypeRules.WreckSite);

    // ---------------------------------------------------------------- the rules

    /// <summary>Whether the boot stamps <paramref name="kind"/> on a type: its whitelist rule (<see cref="StructureTypeRules.AllowedOn"/>).</summary>
    private bool StructureKindAllowed(string kind, PlanetType planet, bool restricted)
        => StructureTypeRules.AllowedOn(StructureType(kind), planet, restricted);

    /// <summary>Whether the active body meets <paramref name="kind"/>'s conditions (<see cref="StructureTypeRules.ConditionsHold"/>).</summary>
    private bool StructureConditionsHold(string kind, PlanetType planet)
    {
        var type = StructureType(kind);
        return StructureTypeRules.ConditionsHold(type, planet,
            asteroid: type.Requires(StructureTypeRules.ConditionNotAsteroid) && IsAsteroidBody(planet));
    }

    /// <summary>The seat policy a kind names, <paramref name="builtIn"/> when it names none.</summary>
    private SeatPolicy SeatPolicyOf(string kind, SeatPolicy builtIn) => StructureType(kind).SeatPolicy switch
    {
        "inhabited" => SeatPolicy.Inhabited,
        "ruin" => SeatPolicy.Ruin,
        "factory" => SeatPolicy.Factory,
        "camp" => SeatPolicy.Camp,
        "monument" => SeatPolicy.Monument,
        _ => builtIn,
    };

    /// <summary>Whether a kind decides anything new on the active world: its data switch is on and the world is of its
    /// <c>minGeneration</c> or newer.</summary>
    private bool StructureKindDue(string kind)
        => StructureType(kind) is { Enabled: true } type && _generator.TerrainGeneration >= type.MinGeneration;

    /// <summary>The chance of a single-roll kind: the server's override when it sets one, else the kind's. An override that
    /// differs from <c>data/structure_types.json</c> is logged once per kind and server run, so an operator sees why a change
    /// to the data file does not show.</summary>
    private double StructureChance(string kind, double? configured)
    {
        double data = StructureType(kind).Chance;
        if (configured is { } chance && chance != data && _loggedChanceOverrides.Add(kind))
        {
            _log.Info($"Structure kind '{kind}': server.json sets its chance to {chance.ToString(System.Globalization.CultureInfo.InvariantCulture)} — data/structure_types.json says {data.ToString(System.Globalization.CultureInfo.InvariantCulture)}; the server setting wins.");
        }

        return configured ?? data;
    }

    /// <summary>The kinds whose chance override was logged (<see cref="StructureChance"/>).</summary>
    private readonly HashSet<string> _loggedChanceOverrides = new(StringComparer.Ordinal);

    // ---------------------------------------------------------------- test seams

    /// <summary>Test seam (#3514): every box the active world registered.</summary>
    internal IReadOnlyList<StructureFootprints.Footprint> FootprintsForTest => Footprints.Entries.ToList();

    /// <summary>Test seam (#3514): the crash site the active world reserves for its wreck before anything is placed.</summary>
    internal (int Cx, int Cz, int Hw, int Hl) PlannedWreckSiteForTest() => PlannedWreckSite();

    /// <summary>Test seam (#3514): the boxes of different structures on the active world that overlap.</summary>
    internal List<(StructureFootprints.Footprint A, StructureFootprints.Footprint B)> FootprintCollisionsForTest() => Footprints.Collisions();
}
