// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3893: a guide tower on the planet map (POI type <see cref="SpsTowerRules.MapType"/>, its name line from the server) —
    /// the beacon icon, because a guide tower WAS a beacon ("the sky-lights"), in the blue its lamps once had; the radio
    /// beacon keeps its amber. Its own legend entry (<see cref="SpsTowerKindKey"/>). No new image: an existing icon in
    /// another colour, like the landmark and the giant wreck.
    /// </summary>
    public sealed partial class WorldMap
    {
        /// <summary>The legend's label of a guide tower.</summary>
        private const string SpsTowerKindKey = "ui.map.kind_sps_tower";

        /// <summary>The colour of a guide tower's marker (and its legend entry) — the sky-lights' blue, apart from the giant
        /// wreck's paler steel blue (the wreck icon) and the radio beacon's amber (the same icon).</summary>
        public static readonly Color SpsTowerCol = new Color(0.30f, 0.60f, 1f);

        /// <summary>The tower's look on the map: the glyph of its list line, the colour and the icon.</summary>
        private static (string glyph, Color col, string icon) SpsTowerLook => ("♜", SpsTowerCol, "map_beacon");
    }
}
