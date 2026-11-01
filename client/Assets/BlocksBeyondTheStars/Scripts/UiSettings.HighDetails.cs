// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The "Details (High)" group of the graphics settings (#3061).</summary>
    public sealed partial class UiSettings
    {
        /// <summary>One switch per desktop-class extra of the High preset (epic #3066). Absent in the browser and on
        /// mobile, where the extras never run; below High a single line says where they live instead of switches that
        /// would do nothing. Every switch applies live.</summary>
        private void HighDetailRows(ref float y)
        {
            if (!HighDetails.DesktopClass)
            {
                return;
            }

            Head(ref y, L("ui.settings.high_details"));
            if (S.Preset != QualityPreset.High)
            {
                UiKit.AddText(_content, _x, y, _rowW, 24, L("ui.settings.high_details.hint"), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
                y += 28f;
                return;
            }

            HighToggle(ref y, "relief", S.HighRelief, v => S.HighRelief = v);
            HighToggle(ref y, "grass", S.HighGrass, v => S.HighGrass = v);
            HighToggle(ref y, "flowers", S.HighFlowers, v => S.HighFlowers = v);
            HighToggle(ref y, "room_reflections", S.HighRoomReflections, v => S.HighRoomReflections = v);
            HighToggle(ref y, "decals", S.HighDecals, v => S.HighDecals = v);
            HighToggle(ref y, "volumetric_light", S.HighVolumetricLight, v => S.HighVolumetricLight = v);
        }

        private void HighToggle(ref float y, string key, bool on, System.Action<bool> set)
        {
            Toggle(ref y, L("ui.settings.high." + key), on, () =>
            {
                set(!on);
                S.Apply();
                ApplyLiveWorld();
                Rebuild();
            });
        }
    }
}
