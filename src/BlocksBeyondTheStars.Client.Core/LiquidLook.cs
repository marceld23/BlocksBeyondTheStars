// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The look a liquid takes from inside it — the screen wash and the underwater fog (#2134, #3335).</summary>
    public enum LiquidLookKind
    {
        /// <summary>Water and every other liquid (oil): the blue wash in the world's water colour.</summary>
        Water = 0,

        /// <summary>A gas giant's gas sea (#2134): the amber haze.</summary>
        Gas = 1,

        /// <summary>The dense gas under the gas sea (#2134): a dark, churning body.</summary>
        DenseGas = 2,

        /// <summary>#3335: Ressaw, the still liquid of the buried G.D.S. ruins — milky white and glowing, never the world's
        /// water colour.</summary>
        Ressaw = 3,
    }

    /// <summary>
    /// Which liquids look different from water (#2134, #3335). Every liquid swims, sinks and drowns like water — the block's
    /// <c>Liquid</c> flag decides that (<see cref="WaterProbe"/>) — but three of them are drawn in a look of their own: the gas
    /// sea and the dense gas under it, and Ressaw, which the chunk mesher draws milky and glowing (its own shader mode), the
    /// screen wash and the underwater fog paint milky white instead of the world's water colour. Unity-free so the headless
    /// suite pins the mapping; <c>WeatherFx</c>, <c>Sky.Mist</c> and <c>ChunkMesher</c> read it.
    /// </summary>
    public static class LiquidLook
    {
        /// <summary>#3335: the block key of Ressaw ("Wasser" backwards).</summary>
        public const string RessawKey = "ressaw";

        /// <summary>The look of the liquid with this block key (anything unknown, oil included, looks like water).</summary>
        public static LiquidLookKind Of(string? blockKey) => blockKey switch
        {
            "gas" => LiquidLookKind.Gas,
            "gas_dense" => LiquidLookKind.DenseGas,
            RessawKey => LiquidLookKind.Ressaw,
            _ => LiquidLookKind.Water,
        };
    }
}
