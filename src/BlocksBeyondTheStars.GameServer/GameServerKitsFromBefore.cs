// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The lively places, the kits from before (docs/developer/LIVELY_PLACES.md §2.1): the settlement kits that shipped
/// before terrain generation 46 — the plot-grid kits and the first planned ones, two to four per tier — end their
/// regular draw at generation 45 (<c>StructureKit.MaxTerrainGeneration</c>), because a new world's settlements are
/// built from their successors. They are not dead content, though (the maintainer, 2026-11-20: "can they not be used
/// after all, rarely?"): a world that has outgrown them still builds a small share of its settlements from them.
/// <list type="bullet">
/// <item><b>The share.</b> <see cref="KitsFromBeforeShare"/> of the kit picks of a lively world go to the outgrown kits
/// of the tier, drawn among themselves by their own weights; every other pick is the regular one. A share, not a
/// weight: a kit somebody adds (user content, a pack) keeps its weight against the current kits either way.</item>
/// <item><b>What such a settlement is.</b> Its own old houses. A planned kit from before is planned and baked like every
/// settlement of the world (street furniture, the place's colour); a plot-grid kit is built by the plot grid it was
/// made for.</item>
/// <item><b>Older worlds.</b> Not a draw more: a world before generation 46 has outgrown nothing, so its unvisited
/// bodies draw exactly what they always would have.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The share of a lively world's settlement kit picks that goes to the kits from before: about one in twenty
    /// (the maintainer's number, 2026-11-20: "rather more rarely" than the first one in eight).</summary>
    internal const double KitsFromBeforeShare = 0.05;

    /// <summary>The kits one settlement pick draws among: this world's regular kits of the tier — or, in a lively world,
    /// with <see cref="KitsFromBeforeShare"/> the ones it has outgrown (one draw on <paramref name="lane"/>, made only
    /// when there are such kits). A tier without a regular kit takes the ones from before.</summary>
    private IReadOnlyList<StructureKit> KitsOfThisPick(string tier, IReadOnlyCollection<string>? packs, System.Random lane)
    {
        int generation = _meta.Description.TerrainGeneration;
        var regular = _content.KitsFor(StructureKit.KindSettlement, tier, packs, _world.Planet.Key, generation);
        if (!LivelyWorld)
        {
            return regular;
        }

        var before = _content.OutgrownKitsFor(StructureKit.KindSettlement, tier, packs, _world.Planet.Key, generation);
        if (before.Count == 0)
        {
            return regular;
        }

        bool fromBefore = lane.NextDouble() < KitsFromBeforeShare;
        return fromBefore || regular.Count == 0 ? before : regular;
    }

    /// <summary>Test seam: the kits one settlement pick of <paramref name="tier"/> draws among, on <paramref name="lane"/>.</summary>
    internal IReadOnlyList<StructureKit> KitsOfThisPickForTest(string tier, System.Random lane)
        => KitsOfThisPick(tier, _meta.Description.EnabledStructurePacks, lane);
}
