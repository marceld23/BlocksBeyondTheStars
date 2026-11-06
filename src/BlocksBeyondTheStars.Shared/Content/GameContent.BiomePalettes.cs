// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>#3289: the load-time checks of the biome palettes (<see cref="BiomePalettes"/>) in <c>data/blocks.json</c>.</summary>
public sealed partial class GameContent
{
    /// <summary>A <c>biomePalette</c> must name a known palette, and a block with <c>biomeLooks</c> must be a solid block whose
    /// look keys are no block keys (a look tile would otherwise shadow, or be shadowed by, a real block's tile).</summary>
    private void ValidateBiomePalettes(List<string> problems)
    {
        foreach (var block in _blocks.Values)
        {
            if (block.BiomePalette != null && BiomePalettes.IndexOf(block.BiomePalette) < 0)
            {
                problems.Add($"Block '{block.Key}' names unknown biome palette '{block.BiomePalette}' " +
                             $"(known: {string.Join(", ", BiomePalettes.Names)}).");
            }

            if (!block.BiomeLooks)
            {
                continue;
            }

            if (!block.Solid)
            {
                problems.Add($"Block '{block.Key}' wears biome looks but is not solid — only solid blocks take a biome's colours.");
            }

            foreach (string look in BiomePalettes.LookKeysOf(block))
            {
                if (_blocks.ContainsKey(look))
                {
                    problems.Add($"Block '{block.Key}': its biome look '{look}' is also a block key.");
                }
            }
        }
    }
}
