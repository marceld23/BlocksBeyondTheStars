// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A land animal in deep water (#3881, F1 report of 2026-11-21: a big arachnid stood on the bed of a pond three blocks deep
/// and turned on the spot). Spawning and walking into water deeper than wading depth are both gated, but an animal that ends
/// up there anyway — whatever the way — found every neighbouring water column blocked as well and was trapped for good. The
/// mirror of the stranded fish (#3552):
/// <list type="bullet">
/// <item><b>Movement</b>: the terrain gate lets it take any step that does not get deeper
/// (<see cref="CreatureBehaviour.TerrainStepBlocked"/>), so it wades along and out.</item>
/// <item><b>Sweep</b>: one still standing in deep water after <see cref="DeepWaterGraceSeconds"/> leaves while no player
/// looks at it (<see cref="SweepBaseIntruders"/>, the town rule's clock pattern).</item>
/// <item><b>Exempt</b>: the town rule's exemptions (<see cref="TownRuleExempt"/>: pets, giants, clones, begging, story fauna,
/// a taming target), a provoked, held or hunting animal, amphibians, water, lava and air life. A column that is not streamed
/// in is not judged.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How long a land animal may stand in deep water before the sweep sends it away unseen — time to wade out.</summary>
    internal const double DeepWaterGraceSeconds = 20.0;

    /// <summary>The sweep half: keeps the animal's deep-water clock (<see cref="CombatEntity.InDeepWaterSince"/>) and answers
    /// whether it has stood in deep water for <see cref="DeepWaterGraceSeconds"/>. The sweep removes it only while nobody
    /// is looking.</summary>
    private bool DeepWaterStayOverdue(CombatEntity c)
    {
        if (!IsLandInDeepWater(c))
        {
            c.InDeepWaterSince = -1;
            return false;
        }

        if (c.InDeepWaterSince < 0)
        {
            c.InDeepWaterSince = _uptime;
            return false;
        }

        return _uptime - c.InDeepWaterSince >= DeepWaterGraceSeconds;
    }

    /// <summary>A wild ground animal (land or cave, not amphibious) whose feet stand in water deeper than it can wade, that
    /// nobody is busy with (see the type summary).</summary>
    private bool IsLandInDeepWater(CombatEntity c)
    {
        if (_creatureWaterId == 0 || !_speciesById.TryGetValue(c.SpeciesId, out var sp)
            || sp.Habitat is not (CreatureHabitat.Land or CreatureHabitat.Cave)
            || !CreatureMotion.ObeysGroundRules(sp, EffectiveMotion(c, sp)))
        {
            return false;
        }

        if (c.ProvokeTimer > 0 || c.FrozenTimer > 0 || c.Loco.Mode == MoveMode.Seek || TownRuleExempt(c, sp))
        {
            return false;
        }

        var cell = c.Position.ToBlock();
        if (!_world.IsChunkLoaded(WorldConstants.WorldToChunk(cell)))
        {
            return false;
        }

        return WaterDepthAtFeet(cell.X, cell.Z, cell.Y) > 1;
    }

    /// <summary>Test seam (#3881): whether the sweep counts this creature as a land animal standing in deep water.</summary>
    internal bool LandInDeepWaterForTest(string creatureId)
        => _creatures.Find(c => c.Id == creatureId) is { } c && IsLandInDeepWater(c);
}
