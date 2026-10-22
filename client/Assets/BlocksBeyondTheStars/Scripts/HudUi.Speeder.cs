// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The vehicle HUD of <see cref="HudUi"/> while driving a hover speeder or a boat: the integrity and energy gauges,
    /// the speed and the drive prompt.
    /// </summary>
    public sealed partial class HudUi
    {
        // Hover-speeder vehicle HUD: integrity + energy gauges, speed and the drive prompt (shown while driving).
        private GameObject _speederPanel;
        private Image _speederHull, _speederFuel, _speederFuelBg;
        private TMP_Text _speederTitle, _speederSpeed, _speederHullLabel, _speederFuelLabel, _speederHint;

        private void BuildSpeeder(Transform root)
        {
            // Vehicle HUD (hover speeder): integrity + energy gauges, speed + drive prompt, where the hotbar sits
            // (the hotbar is hidden while driving). Hidden until the player boards a speeder.
            _speederPanel = Panel(root, W / 2f - 170, H - 136, 340, 108).gameObject;
            _speederTitle = UiText.Add(_speederPanel.transform, 12, 6, 220, 18, string.Empty, 14, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _speederSpeed = UiText.Add(_speederPanel.transform, 108, 6, 220, 18, string.Empty, 14, UiKit.TextCol, TextAnchor.MiddleRight, FontStyle.Bold);
            _speederHull = MakeBar(_speederPanel.transform, 12, 30, 316, 16);
            _speederHullLabel = UiText.Add(_speederPanel.transform, 18, 30, 304, 16, string.Empty, 12, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            // The fuel row keeps its own background handle: a boat (FuelMax 0, #1215) hides the whole row.
            _speederFuelBg = UiKit.AddImage(_speederPanel.transform, 12, 52, 316, 16, UiKit.SolidSprite, new Color(0.03f, 0.07f, 0.13f, 0.9f));
            _speederFuel = MakeBar(_speederPanel.transform, 12, 52, 316, 16);
            _speederFuelLabel = UiText.Add(_speederPanel.transform, 18, 52, 304, 16, string.Empty, 12, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            // The key hint was the smallest, dimmest text on the panel (12 pt, dim cyan) and players missed "F: Exit"
            // (#2578): bright bold text with the key itself in the accent colour, shrinking only for a long warning.
            _speederHint = UiText.Add(_speederPanel.transform, 12, 76, 316, 26, string.Empty, 15, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            _speederHint.enableAutoSizing = true;
            _speederHint.fontSizeMin = 11f;
            _speederHint.fontSizeMax = 15f;
            _speederPanel.SetActive(false);
        }

        /// <summary>Vehicle HUD while driving a hover speeder: integrity + energy gauges (colour-graded), the
        /// current speed and the F/R drive prompt. Hidden on foot.</summary>
        private void RefreshSpeeder(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            var s = Game.DrivenSpeeder;
            bool active = !string.IsNullOrEmpty(Game.InSpeeder) && s != null;
            if (_speederPanel.activeSelf != active)
            {
                _speederPanel.SetActive(active);
            }

            if (!active)
            {
                return;
            }

            bool boat = s.Kind == "boat";
            _speederTitle.text = loc.Get(boat ? "item.boat.name" : "item.speeder.name");
            _speederSpeed.text = $"{Mathf.RoundToInt(Mathf.Abs(Game.SpeederSpeed))} m/s";

            float hullFrac = s.HullMax > 0 ? s.Hull / s.HullMax : 0f;
            UiHolo.SetBar(_speederHull, hullFrac, 316f);
            _speederHull.color = hullFrac > 0.5f ? new Color(0.4f, 0.85f, 0.5f)
                : (hullFrac > 0.25f ? new Color(0.95f, 0.8f, 0.3f) : new Color(0.95f, 0.35f, 0.3f));
            _speederHullLabel.text = $"{loc.Get("hud.speeder.integrity")}  {Mathf.RoundToInt(s.Hull)}";

            // A vehicle with no cell (the boat: FuelMax 0) shows no gauge at all rather than an empty one.
            bool hasFuel = s.FuelMax > 0.01f;
            if (_speederFuelBg.gameObject.activeSelf != hasFuel)
            {
                _speederFuelBg.gameObject.SetActive(hasFuel);
                _speederFuel.gameObject.SetActive(hasFuel);
                _speederFuelLabel.gameObject.SetActive(hasFuel);
            }

            if (hasFuel)
            {
                float fuelFrac = s.Fuel / s.FuelMax;
                UiHolo.SetBar(_speederFuel, fuelFrac, 316f);
                _speederFuel.color = fuelFrac > 0.2f ? new Color(0.4f, 0.8f, 1f) : new Color(0.95f, 0.5f, 0.2f);
                _speederFuelLabel.text = $"{loc.Get("hud.speeder.fuel")}  {Mathf.RoundToInt(s.Fuel)}";
            }

            // Key letters come from the live bindings (keyboard key or pad glyph), not hardcoded F/R.
            string hint = KeyHint(InputAction.SpeederExit, loc.Get("hud.speeder.exit"));
            bool warning = false;
            if (hasFuel)
            {
                hint += "  ·  " + KeyHint(InputAction.SpeederRefuel, loc.Get("hud.speeder.refuel"));
                if (s.Fuel <= 0.01f)
                {
                    hint = loc.Get("hud.speeder.nofuel");
                    warning = true;
                }
            }
            else if (Game.VehicleAground)
            {
                hint = loc.Get("hud.boat.aground");
                warning = true;
            }

            _speederHint.text = hint;
            _speederHint.color = warning ? UiKit.Warn : UiKit.TextCol;
        }

        private static readonly string KeyHintColor = ColorUtility.ToHtmlStringRGB(UiKit.Cyan);

        /// <summary>"F: Exit" with the key in the accent colour, so WHICH key stands out from the words (#2578).</summary>
        private static string KeyHint(InputAction action, string label)
            => $"<color=#{KeyHintColor}>{InputMap.Glyph(action)}</color>: {label}";

        /// <summary>A horizontal fill bar (dark track + a coloured fill) for the vehicle gauges.</summary>
        private Image MakeBar(Transform parent, float x, float y, float w, float h)
            => UiHolo.AddBar(parent, x, y, w, h, new Color(0.03f, 0.07f, 0.13f, 0.9f), Color.white).Fill;
    }
}
