// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// A world's generated tree species: a coined name + a toxic/edible trait layered onto the fixed tree
/// blocks (<c>wood_log</c> trunk + <c>tree_leaves</c> crown). The world grows one tree identity — the trunk
/// and the leaves read as the same species, so scanning either counts as one discovery. Derived
/// deterministically from the world seed + planet (see <c>TreeGenerator</c>), so a given world always names
/// and classifies its trees the same way without storing anything. The counterpart to <see cref="FloraSpecies"/>
/// for the (non-catalogued) tree blocks.
/// </summary>
public sealed class TreeSpecies
{
    /// <summary>Per-world species id ("tr0").</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Coined, pronounceable tree name (e.g. "Skarnwood"), shown to the player on scan.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether the tree is toxic (vs. edible/benign) — reported on scan, mirrors flora classification.</summary>
    public bool Toxic { get; set; }
}
