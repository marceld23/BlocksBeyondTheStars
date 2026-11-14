// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The intercity line's planning rules by terrain generation (#3629, #3630; the main file is GameServerIntercityRail.cs).
/// Up to generation 41 a line joins two towns at most <see cref="IntercityMaxRoute"/> (960) blocks apart — fewer than one in
/// eight worlds with two towns had such a pair, the median closest pair stands about 2080 blocks apart. From
/// <see cref="WorldDescription.LongIntercityGeneration"/> (42) the route may run 2000 blocks: the public train covers it at
/// its 24 blocks/s top speed in about 90 s (<c>RailRules.PublicTrainSpeedFor</c>, #3632), a round trip with both halts in
/// about four minutes. The corridor's tree budget then grows with the route so a long forest line clears its trees whole
/// instead of slicing the last ones cell by cell. The chance of the line (0.6 → 0.8 from generation 42, #3631) is data:
/// <c>chanceFrom</c> of the <c>intercity</c> kind in <c>data/structure_types.json</c>.
/// Every rule here is read once, when a body is stamped for the first time; a line that stands replays from its placement
/// records, so no older world changes.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The longest free route from terrain generation 42 on (#3629).</summary>
    private const int IntercityLongMaxRoute = 2000;

    /// <summary>The tree cells the corridor may clear from terrain generation 42 on (#3630): this much for the towns' and
    /// stations' surroundings plus <see cref="IntercityVegetationPerBlock"/> per block of route. Measured with no budget on
    /// generation 41 lines: a short forest line cleared up to 33 cells per block (savanna, 615 blocks: 20 233 cells), a jungle
    /// line 28 (1126 blocks: 31 681 cells in a 1.4 s stamp), a 1961-block meadow line 15 — every one about a third under
    /// this budget. A 2000-block jungle line may clear 64 000 cells, about 2.5 s once on the body's first visit.</summary>
    private const int IntercityVegetationBase = 24000;

    /// <summary>Tree cells per block of route on top of <see cref="IntercityVegetationBase"/> (#3630).</summary>
    private const int IntercityVegetationPerBlock = 20;

    /// <summary>The longest free route a line planned on this terrain generation may run (#3629).</summary>
    internal static int IntercityMaxRouteFor(int terrainGeneration)
        => terrainGeneration >= WorldDescription.LongIntercityGeneration ? IntercityLongMaxRoute : IntercityMaxRoute;

    /// <summary>The most tree cells the corridor stamp of a line of this route length may clear on this terrain generation
    /// (#3630): the old fixed <see cref="IntercityVegetationCap"/> up to generation 41 (4 of 21 lines used it up and cut
    /// their last trees cell by cell), from 42 on <see cref="IntercityVegetationBase"/> plus
    /// <see cref="IntercityVegetationPerBlock"/> per block of route.</summary>
    internal static int IntercityVegetationCapFor(int terrainGeneration, float routeLength)
        => terrainGeneration >= WorldDescription.LongIntercityGeneration
            ? IntercityVegetationBase + (int)System.Math.Ceiling(System.Math.Max(0f, routeLength) * IntercityVegetationPerBlock)
            : IntercityVegetationCap;
}
