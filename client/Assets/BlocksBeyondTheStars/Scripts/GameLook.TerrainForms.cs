// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The look's terrain forms (#3964, LOOK.md §17; <c>TerrainForms</c> in Client.Core, <c>ChunkMesher.TerrainForms.cs</c>).</summary>
    public static partial class GameLook
    {
        /// <summary>True: natural terrain cells are drawn as forms out of micro cubes where the tier allows it.</summary>
        public static bool TerrainForms => true;

        /// <summary>The density of the terrain forms (1 = the look as tuned in the playtest of 2026-11-23).</summary>
        public const float TerrainFormsDensity = 1f;

        /// <summary>The world seed the forms hash with, so every client of a world draws the same forms. Set on join
        /// (GameBootstrap), before the first chunk is meshed.</summary>
        public static long TerrainFormsSalt;

        /// <summary>True when the planet chunks draw terrain forms: a desktop-class client on Medium or High. Low, Potato and
        /// the browser keep the cube and the mesher does not look for forms there. Safe on any thread.</summary>
        public static bool TerrainFormsNow => TerrainForms && _desktopClass && FxKit.Preset >= QualityPreset.Medium;
    }
}
