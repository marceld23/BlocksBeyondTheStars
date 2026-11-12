// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// #3516: from terrain generation <see cref="WorldDescription.StructurePipelineGeneration"/> (38) the fallen settlements
/// (and the standalone ruins), the monuments, the buried G.D.S. ruins and the SPS labs decay through the shared ruin pass
/// (<see cref="StructureRuiner"/>) instead of a decay of their own. Every older world keeps its own decay byte for byte.
/// </summary>
internal static class StructureDecay
{
    /// <summary>The ruin-pass revision a generation-38 structure decays with — pinned here, not the newest one: a monument's
    /// relic cache and a G.D.S. ruin's crates are re-derived from the decayed shape on every load, so a later revision of the
    /// pass must never reach a structure that already stands.</summary>
    public const int Revision = 1;

    /// <summary>Whether a world of <paramref name="terrainGeneration"/> decays its structures through the shared pass.</summary>
    public static bool Applies(int terrainGeneration) => terrainGeneration >= WorldDescription.StructurePipelineGeneration;

    /// <summary>The seed of one caller's ruin pass: the instance seed on a lane of its own (<c>ruin:&lt;lane&gt;</c>), so the
    /// pass never draws what the generator's own stream draws.</summary>
    public static long Seed(string lane, long seed) => unchecked((long)StableHashing.Mixed("ruin:" + lane, seed));
}
