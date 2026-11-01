// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The landing pads on the planet map: the body's communal pads (item 38) and, #3070, the pads players built at their
    /// bases — each at its spot near its base with its name and state, grey when its owner shuts this player out.
    /// </summary>
    public sealed partial class WorldMap
    {
        private int _padsSeen = -1; // the pad book version the markers were drawn from (#3070: redraw on a change)

        /// <summary>Draws the landing pads of the body the player is on and lists them with their distance.</summary>
        private void DrawLandingPads(System.Text.StringBuilder poiLines)
        {
            _padsSeen = Game.LandingPadBook.Version;

            // #2740: the pads of the body you are on — never the last list the flight pad chooser happened to ask about (a
            // padless body, or the body you left). Before the first star map arrives there is only the world-entry list.
            var bodyPads = Game.CurrentBodyPads;
            if (bodyPads == null)
            {
                return;
            }

            DrawCommunalPads(bodyPads, poiLines);
            DrawBasePads(bodyPads, poiLines);
        }

        /// <summary>Fixed landing pads (item 38): ALWAYS shown — at true position when in view, else pinned to the map edge
        /// pointing the way (pads are spread round the body, so most sit outside this local window). Green = free, red =
        /// another player is on it, cyan = your own pad (#977). Also listed with distance so every pad is findable. #3070:
        /// only the communal pads — a base pad (index -1) is no "Pad 0".</summary>
        private void DrawCommunalPads(BlocksBeyondTheStars.Networking.Messages.NetLandingPad[] bodyPads, System.Text.StringBuilder poiLines)
        {
            foreach (var pad in BasePadView.Communal(bodyPads))
            {
                var col = pad.Mine ? new Color(0.45f, 0.85f, 1f)
                    : pad.Occupied ? new Color(1f, 0.45f, 0.4f)
                    : new Color(0.5f, 0.9f, 0.6f);
                PadMarker(pad.X, pad.Z, col);
                int dist = Mathf.RoundToInt(GroundDistance(pad.X, pad.Z));
                string occ = pad.Occupied || pad.Mine ? $" ({pad.Occupant})" : string.Empty;
                string wet = pad.Lava ? $" · {L("ui.space.pad_lava")}" // a pad standing in lava (old saves)
                    : pad.Wet ? (pad.Depth > 0 ? $" · {L("ui.space.pad_wet")} · {pad.Depth} m" : $" · {L("ui.space.pad_wet")}") : string.Empty; // seabed pad + depth (#1454/#1622)
                poiLines.Append($"\n⊕ {L("ui.map.pad")} {pad.Index + 1}{occ}{wet}  —  {dist} m");
            }
        }

        /// <summary>#3070: the bases' landing pads — a pad marker at the pad (only while in view: they stand beside their
        /// base, whose own marker leads the way there), its name and state below it, and a line in the list. Cyan = your
        /// ship's pad, red = taken, green = free, grey = locked for you.</summary>
        private void DrawBasePads(BlocksBeyondTheStars.Networking.Messages.NetLandingPad[] bodyPads, System.Text.StringBuilder poiLines)
        {
            foreach (var g in BasePadView.GroupByBase(bodyPads))
            {
                string baseName = BasePadText.BaseName(g.BaseName, L);
                for (int i = 0; i < g.Pads.Count; i++)
                {
                    var p = g.Pads[i];
                    var col = BasePadText.MarkerColor(p);
                    string name = BasePadText.Name(p, i + 1, L);
                    string state = BasePadText.State(p, L);
                    Marker(p.CellX, p.CellZ, 26f, col, "⊕", "map_pad");
                    MarkerLabel(p.CellX, p.CellZ, $"{name} · {state}", col);
                    int dist = Mathf.RoundToInt(GroundDistance(p.CellX, p.CellZ));
                    poiLines.Append($"\n⊕ {name} ({baseName}) · {state}  —  {dist} m");
                }
            }
        }
    }
}
