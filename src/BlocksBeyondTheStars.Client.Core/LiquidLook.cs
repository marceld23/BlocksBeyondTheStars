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

        /// <summary>#3644: the Grand Technicians' red fluid — Ressaw's still, glowing look in red. Decoration only: none of
        /// Ressaw's mechanics (they are keyed to the block key <c>ressaw</c> on the server).</summary>
        RedFluid = 4,
    }

    /// <summary>
    /// Which liquids look different from water (#2134, #3335). Every liquid swims, sinks and drowns like water — the block's
    /// <c>Liquid</c> flag decides that (<see cref="WaterProbe"/>) — but four of them are drawn in a look of their own: the gas
    /// sea and the dense gas under it, and the two still, glowing liquids (<see cref="GlowsStill(LiquidLookKind)"/>): Ressaw
    /// and the Grand Technicians' red fluid (#3644). The chunk mesher draws those see-through in its milky shader mode, in
    /// their tile's own colour and glowing, and the screen wash and the underwater fog take their colour (milky white, red)
    /// instead of the world's water colour. Unity-free so the headless suite pins the mapping; <c>WeatherFx</c>,
    /// <c>Sky.Mist</c> and <c>ChunkMesher</c> read it.
    /// </summary>
    public static class LiquidLook
    {
        /// <summary>#3335: the block key of Ressaw ("Wasser" backwards).</summary>
        public const string RessawKey = "ressaw";

        /// <summary>#3644: the block key of the Grand Technicians' red fluid.</summary>
        public const string RedFluidKey = "gt_fluid";

        /// <summary>The look of the liquid with this block key (anything unknown, oil included, looks like water).</summary>
        public static LiquidLookKind Of(string? blockKey) => blockKey switch
        {
            "gas" => LiquidLookKind.Gas,
            "gas_dense" => LiquidLookKind.DenseGas,
            RessawKey => LiquidLookKind.Ressaw,
            RedFluidKey => LiquidLookKind.RedFluid,
            _ => LiquidLookKind.Water,
        };

        /// <summary>#3644: whether a liquid of this look is one of the still, glowing ones — Ressaw and the red fluid. They
        /// never bob or foam; the mesher draws them see-through in the milky mode, and from inside them the veil and the
        /// murk take their own colour and reach only a few metres.</summary>
        public static bool GlowsStill(LiquidLookKind kind) => kind is LiquidLookKind.Ressaw or LiquidLookKind.RedFluid;

        /// <summary>#3644: <see cref="GlowsStill(LiquidLookKind)"/> by block key.</summary>
        public static bool GlowsStill(string? blockKey) => GlowsStill(Of(blockKey));
    }
}
