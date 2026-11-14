// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3642: a lost civilization's site on the planet map (POI type <c>civilization</c>, its name from the server once a
    /// player came near) — the ruin icon in its own colour, with its own legend entry (<c>ui.map.kind_civilization</c>).
    /// </summary>
    public sealed partial class WorldMap
    {
        /// <summary>The colour of a lost civilization's site — an old verdigris, apart from the landmark's pale gold and the
        /// fallen settlement's grey-brown (the same icon) and the settlements' and pads' lighter green.</summary>
        public static readonly Color CivilizationCol = new Color(0.36f, 0.80f, 0.70f);

        /// <summary>The site's look on the map: the glyph of its list line, the colour and the icon.</summary>
        private static (string glyph, Color col, string icon) CivilizationLook => ("Ψ", CivilizationCol, "map_ruin");
    }
}
