// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Measurement switches for the look (#4050): each turns one cost-bearing part of the picture off so PerfProbe's
    /// <c>-perfFeature</c> sweep can tell what that part costs on a preset. Nothing in play sets them, no setting stores
    /// them, and all are off by default — the game looks and runs exactly as without this class. They exist as statics
    /// because the parts they switch are republished from <see cref="GameLook"/> every frame, so a one-off override from
    /// outside would not hold.
    /// </summary>
    public static class LookProbe
    {
        /// <summary>The near crown clusters draw nothing (the far cubes stay): <c>crowns=off</c>.</summary>
        public static bool CrownsOff;

        /// <summary>The grass clumps and the ground cover of bare ground draw nothing: <c>clumps=off</c>.</summary>
        public static bool ClumpsOff;

        /// <summary>The plant bodies draw nothing (the crossed pictures stay): <c>plants=off</c>.</summary>
        public static bool PlantsOff;

        /// <summary>The short felt on furry animals draws nothing: <c>felt=off</c>.</summary>
        public static bool FeltOff;

        /// <summary>Crown clusters and plant bodies cast no shadows of their own (the far forms cast instead):
        /// <c>nearshadows=off</c>.</summary>
        public static bool NearShadowsOff;

        /// <summary>Grass takes one sun-shadow lookup instead of the softened three: <c>grassshadow=single</c>.</summary>
        public static bool GrassShadowSingle;

        /// <summary>The sun keeps the pipeline's soft-shadow quality on desktop High too (9 taps instead of 16):
        /// <c>sunshadow=pipeline</c>.</summary>
        public static bool SunShadowPipelineQuality;

        /// <summary>No depth of field: <c>dof=off</c>.</summary>
        public static bool FocusBlurOff;
    }
}
