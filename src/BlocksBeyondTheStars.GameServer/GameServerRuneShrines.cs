// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3416: the rune shrines of a world of terrain generation 36 or later. The world generator raises them in the chunks (see
/// <c>WorldGenerator.RuneShrinesGen36.cs</c> — a ring of rune stones around an ancient-brick altar on a worn brick floor); a
/// complete one is a ring the Sounders' shrine steps accept (<see cref="MelodySiteKindRing"/>). This is the server's seam for
/// the story: where the nearest complete shrine of the active world stands, so a hint can point the player there.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far (blocks) <see cref="NearestRuneShrine"/> looks by default. On a normal world a complete shrine stands
    /// every 220–250 blocks, so the nearest is usually 30–200 blocks away; the search stops at the first ring of cells that
    /// cannot hold a nearer one.</summary>
    internal const int RuneShrineSearchRadius = 480;

    /// <summary>The half width of a shrine's footprint (its floor disc is eleven across) for the overlap tests.</summary>
    private const int RuneShrineHalfExtent = 6;

    /// <summary>
    /// The altar (the ancient brick in the middle of the ring) of the complete rune shrine nearest to <paramref name="from"/> on
    /// the active world, within <paramref name="maxDistance"/> blocks horizontally — or null: an older world (no shrines), a
    /// world type without them (no air, no solid ground, a structure whitelist without monuments), or none near enough. A shrine
    /// a settlement, a rail line, a lodge, a buried ruin or a monument was stamped over is skipped. Pure seed arithmetic plus the
    /// memoised ground samples of the cells in reach: no chunk is loaded, and a stone a player mined since is not noticed.
    /// </summary>
    internal Vector3i? NearestRuneShrine(Vector3f from, int maxDistance = RuneShrineSearchRadius)
    {
        int x = (int)System.Math.Floor(from.X), z = (int)System.Math.Floor(from.Z);
        return _generator.TryFindNearestRuneShrine(_world.Planet, x, z, maxDistance, s => s.Complete && !RuneShrineBuiltOver(s),
            out var shrine)
            ? shrine.Altar
            : null;
    }

    /// <summary>Whether a structure stamp of the active world covers the shrine's footprint (it levelled the shrine away).</summary>
    private bool RuneShrineBuiltOver(RuneShrine shrine)
    {
        if (OverlapsAnySettlement(shrine.X, shrine.Z, RuneShrineHalfExtent))
        {
            return true;
        }

        int circ = _world.Circumference;
        foreach (var m in _monuments)
        {
            int relX = WorldConstants.WrapDeltaX(shrine.X - m.Min.X, circ);
            if (relX >= -RuneShrineHalfExtent && relX <= m.Max.X - m.Min.X + RuneShrineHalfExtent
                && shrine.Z >= m.Min.Z - RuneShrineHalfExtent && shrine.Z <= m.Max.Z + RuneShrineHalfExtent)
            {
                return true;
            }
        }

        return false;
    }
}
