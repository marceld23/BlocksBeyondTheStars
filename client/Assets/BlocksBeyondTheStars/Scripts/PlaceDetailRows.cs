// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The place rows the travel screen (Tab → Map) and the flight chart (M) share (#3131), so a place reads and renames
    /// the same wherever the player looks at it: the generated name in small type under an own name (#2952),
    /// "Your own name — only you see it" with its Name it button, an inline rename row (owned stations and bases), and a
    /// body's colour mark. All rows are laid out top-down from <c>y</c> and return the y below them.
    /// </summary>
    public static class PlaceDetailRows
    {
        public const float RowWidth = 588f; // field 400 + gap 8 + button 180

        /// <summary>An inline name field + confirm button (no modal, so it never desyncs a menu's own open/cursor state).
        /// Sends the typed name on click.</summary>
        public static float AddRenameRow(Transform parent, float x, float y, string current, string buttonLabel,
            System.Action<string> onConfirm, string placeholder)
        {
            var field = UiKit.AddInput(parent, x, y, 400, 48, current ?? string.Empty, null, placeholder ?? string.Empty);
            field.characterLimit = 24;
            field.lineType = InputField.LineType.SingleLine;
            UiKit.AddButton(parent, x + 408, y, 180, 48, buttonLabel, () => onConfirm?.Invoke(field.text ?? string.Empty));
            return y + 56f;
        }

        /// <summary>#2952: under a place the player named for themselves, its generated name in small type — friends still
        /// meet up on the real name. Nothing when <paramref name="original"/> is null.</summary>
        public static float AddOriginalNameLine(Transform parent, float x, float y, string original, System.Func<string, string> localize)
        {
            if (string.IsNullOrEmpty(original))
            {
                return y;
            }

            UiKit.AddText(parent, x, y, 620, 24, string.Format(localize("ui.map.original_name"), original), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
            return y + 24f;
        }

        /// <summary>#2952: "Your own name" for a visited system, planet or moon — private, only this player sees it; an
        /// empty field gives the place its generated name back. The server checks the visit again.</summary>
        public static float AddOwnNameRow(Transform parent, float x, float y, string placeId, string current,
            System.Func<string, string> localize, NetworkClient network)
        {
            UiKit.AddText(parent, x, y, 620, 26, localize("ui.map.own_name"), 18, UiKit.Cyan, TextAnchor.UpperLeft);
            return AddRenameRow(parent, x, y + 28f, current, localize("ui.map.name_place"), name => network?.SendSetPlaceName(placeId, name),
                localize("ui.map.own_name_placeholder"));
        }

        /// <summary>The colour-mark button of a body (and "clear" beside it while marked). Asked for by a player who wanted
        /// to mark planets in space in different colours: any number of bodies can carry a mark at once, and the charts
        /// halo each in its colour. A fixed named palette (not a colour picker) keeps it readable and translatable. Purely
        /// local — no server involvement. <paramref name="changed"/> redraws the caller.</summary>
        public static float AddMarkRow(Transform parent, float x, float y, ClientSettings settings, string bodyId,
            System.Func<string, string> localize, System.Action changed)
        {
            int mark = PlanetMarks.ColorOf(settings, bodyId);
            bool marked = mark >= 0 && mark < PlanetMarkerPalette.Count;
            string markLabel = marked
                ? localize("ui.marker.marked") + ": " + localize(PlanetMarkerPalette.NameKeys[mark])
                : localize("ui.marker.unmarked");
            var markBtn = UiKit.AddButton(parent, x, y, 330, 44, markLabel, () =>
            {
                PlanetMarks.Cycle(settings, bodyId);
                changed?.Invoke();
            });
            if (marked && markBtn != null && markBtn.TryGetComponent<Image>(out var img))
            {
                img.color = Color.Lerp(img.color, PlanetMarkerPalette.Colors[mark], 0.55f); // the choice shows without reading
            }

            if (mark >= 0)
            {
                UiKit.AddButton(parent, x + 338, y, 180, 44, localize("ui.marker.clear"), () =>
                {
                    PlanetMarks.Set(settings, bodyId, -1);
                    changed?.Invoke();
                });
            }

            return y + 52f;
        }
    }

    /// <summary>
    /// The coloured planet marks (stored locally in <see cref="ClientSettings"/>, never sent to the server), grouped by
    /// world so marks from one save don't bleed into another (body ids like "sys0-p5" repeat across saves). The world is
    /// the last local save started or hosted; for a remote server it keeps the last local name, which only ever groups
    /// marks slightly oddly — they stay local cosmetics either way.
    /// </summary>
    public static class PlanetMarks
    {
        private static string World(ClientSettings settings) => settings?.LastWorld ?? string.Empty;

        /// <summary>The colour index marking a body, or -1 when unmarked.</summary>
        public static int ColorOf(ClientSettings settings, string bodyId)
            => settings?.GetPlanetMarker(World(settings), bodyId) ?? -1;

        /// <summary>Steps a body through the palette and off the end back to unmarked, so one button both marks and
        /// recolours — the fewest controls for the youngest player.</summary>
        public static void Cycle(ClientSettings settings, string bodyId)
        {
            int next = ColorOf(settings, bodyId) + 1;
            Set(settings, bodyId, next >= PlanetMarkerPalette.Count ? -1 : next);
        }

        public static void Set(ClientSettings settings, string bodyId, int color)
        {
            if (settings == null)
            {
                return;
            }

            settings.SetPlanetMarker(World(settings), bodyId, color);
            settings.Save();
        }
    }
}
