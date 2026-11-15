// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3676: a found guardian giant on the planet map (POI type <see cref="GuardianGiantRules.PoiType"/>, its name line from
    /// the server) — the danger mark, tinted by the marker's state (<see cref="NetPoi.State"/>): a muted red while it lies
    /// there, the Guardian machines' bright red while it is awake and walks (the marker follows it), grey once defeated. Its own
    /// legend entry (<see cref="GuardianGiantKindKey"/>) shows the awake red.
    /// </summary>
    public sealed partial class WorldMap
    {
        /// <summary>The legend's label of a guardian giant.</summary>
        private const string GuardianGiantKindKey = "ui.map.kind_guardian_giant";

        /// <summary>The colour of an awake guardian giant's marker (and its legend entry) — a warm Guardian red, apart from the
        /// hunter shuttle camp's (the ship icon) and the Guardian core's rose (a glyph).</summary>
        public static readonly Color GuardianGiantCol = new Color(1f, 0.32f, 0.24f);

        /// <summary>A lying giant's marker: the same red, muted — it may sleep, it may be dead for good.</summary>
        public static readonly Color GuardianGiantLyingCol = new Color(0.78f, 0.46f, 0.4f);

        /// <summary>A defeated giant's marker: grey, the corpse where it fell.</summary>
        public static readonly Color GuardianGiantDefeatedCol = new Color(0.62f, 0.62f, 0.66f);

        /// <summary>The giant's look on the map: the glyph of its list line, the colour and the icon.</summary>
        private static (string glyph, Color col, string icon) GuardianGiantLook => ("⚠", GuardianGiantCol, "map_mark_danger");

        /// <summary>A marker's colour by its state when it is a guardian giant's; every other marker keeps
        /// <paramref name="col"/>.</summary>
        private static Color GuardianGiantTint(NetPoi p, Color col)
        {
            if (p == null || p.Type != GuardianGiantRules.PoiType)
            {
                return col;
            }

            return p.State switch
            {
                GuardianGiantRules.PoiAwake => GuardianGiantCol,
                GuardianGiantRules.PoiDefeated => GuardianGiantDefeatedCol,
                _ => GuardianGiantLyingCol,
            };
        }
    }
}
