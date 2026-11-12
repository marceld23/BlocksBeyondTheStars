// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The structure editor's marker settings (#3493): what a landmark's chest holds, what its keeper sells, what a place
    /// sounds like and which animals live there. A click with the settings tool on a placed marker opens this panel; it
    /// reads and writes the marker's settings line (<see cref="MarkerSettings"/>, <c>key=value;…</c>), which travels with
    /// the template to the server:
    /// <list type="bullet">
    /// <item><b>Chest</b> — fixed items (<c>iron_plate:2-4,gold_ingot:1</c>), a loot table of <c>data/loot_tables.json</c>,
    /// or both; the chance that the chest exists; its crate block; "hidden" (a stone on top).</item>
    /// <item><b>Vendor</b> — the market theme of the recipes it sells, "only inside its room", a nameplate key.</item>
    /// <item><b>Sound</b> — the loop, how far it carries and how loud it is at the source.</item>
    /// <item><b>Den</b> — the species (<c>native</c> = the planet's own), the pack size and the radius.</item>
    /// </list>
    /// A modal like <see cref="EditorDevicePanel"/>.
    /// </summary>
    internal sealed class EditorMarkerPanel
    {
        private const float W = 980f, H = 600f;

        /// <summary>The loops a sound marker can pick with the stepper (any other bundled clip can be typed).</summary>
        private static readonly string[] KnownSounds =
        {
            "amb_sounder", SingingPlantChorus.ChimeReedLoop, SingingPlantChorus.BellBloomLoop, SingingPlantChorus.HarpVineLoop,
            // The school club's landmarks (terrain generation 39): the arena's wind, the mine's dripping water, the Soaniri castle
            // and the Sounders' choir at the singer statue.
            "amb_portasseum_wind", "amb_mine_drip", "amb_soaniri_castle", "amb_singer_choir",
        };

        private static readonly string[] CrateBlocks = { "wood_crate", "crate" };

        private readonly AppShell _shell;
        private readonly GameObject _overlay;
        private readonly Transform _panel;
        private readonly Action _onClosed;
        private readonly Action<string> _onApply;
        private readonly string _marker;
        private readonly MarkerSettings _settings;
        private GameObject _body;

        private EditorMarkerPanel(AppShell shell, Transform canvas, string marker, string settings, Action<string> onApply, Action onClosed)
        {
            _shell = shell;
            _marker = marker;
            _settings = MarkerSettings.Parse(settings);
            _onApply = onApply;
            _onClosed = onClosed;
            var (overlay, panel) = UiKit.AddModalOverlay(canvas, (1920f - W) * 0.5f, (1080f - H) * 0.5f, W, H);
            _overlay = overlay;
            _panel = panel;
        }

        /// <summary>Whether a marker type has settings this panel edits.</summary>
        public static bool Edits(string marker)
            => marker == LandmarkRules.ChestMarker || marker == LandmarkRules.SoundMarker || marker == LandmarkRules.DenMarker || marker == "vendor";

        private string L(string key) => _shell != null ? _shell.L(key) : key;

        /// <summary>Opens the panel for one marker cell; <paramref name="onApply"/> gets the new settings line.</summary>
        public static EditorMarkerPanel Show(AppShell shell, Transform canvas, string marker, string settings, Action<string> onApply, Action onClosed = null)
        {
            var panel = new EditorMarkerPanel(shell, canvas, marker, settings, onApply, onClosed);
            panel.Build();
            UiKit.OpenModal(panel._overlay);
            return panel;
        }

        public void Close()
        {
            if (_overlay != null)
            {
                UnityEngine.Object.Destroy(_overlay);
            }

            _onClosed?.Invoke();
        }

        private void Build()
        {
            if (_body != null)
            {
                UnityEngine.Object.Destroy(_body);
            }

            _body = new GameObject("Body", typeof(RectTransform));
            var rt = _body.GetComponent<RectTransform>();
            rt.SetParent(_panel, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var body = _body.transform;

            UiKit.AddText(body, 32f, 20f, W - 64f, 36f, string.Format(L("ui.ed.marker.title"), L("ui.marker." + _marker)), 22, UiKit.Cyan,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            float y = 76f;
            if (_marker == LandmarkRules.ChestMarker)
            {
                y = BuildChest(body, y);
            }
            else if (_marker == "vendor")
            {
                y = BuildVendor(body, y);
            }
            else if (_marker == LandmarkRules.SoundMarker)
            {
                y = BuildSound(body, y);
            }
            else
            {
                y = BuildDen(body, y);
            }

            UiKit.AddText(body, 32f, y + 4f, W - 64f, 26f, _settings.IsEmpty ? L("ui.ed.marker.defaults") : _settings.ToString(), 14, UiKit.CyanDim,
                TextAnchor.MiddleLeft);
            UiKit.AddFitButton(body, 32f, H - 72f, 300f, 48f, L("ui.ed.device.apply"), () =>
            {
                _onApply?.Invoke(_settings.ToString());
                Close();
            });
            UiKit.AddFitButton(body, W - 32f - 220f, H - 72f, 220f, 48f, L("ui.crystal.close"), Close);
        }

        // ---------------------------------------------------------------- rows

        /// <summary>A row's label: 18 px on one line, down to two 13 px lines when a language runs long (the row is 48 high).</summary>
        private void Label(Transform body, float y, string key)
        {
            var label = UiKit.AddText(body, 32f, y, 360f, 48f, L(key), 18, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.FitLabelLines(label, 18, UiKit.EditorMinFontSize, UiKit.EditorMinFontSize);
        }

        /// <summary>A text row: the label and an input bound to one setting.</summary>
        private float TextRow(Transform body, float y, string labelKey, string setting, string placeholder, int maxLength = 96)
        {
            Label(body, y, labelKey);
            UiKit.AddInput(body, 400f, y, 520f, 48f, _settings.Get(setting), v => _settings.Set(setting, v), placeholder, maxLength);
            return y + 60f;
        }

        /// <summary>A stepper row: the label, the current value and a → button that walks <paramref name="values"/> (the
        /// first value is the default and is stored as "no setting").</summary>
        private float StepRow(Transform body, float y, string labelKey, string setting, IReadOnlyList<string> values, Func<string, string> show)
        {
            Label(body, y, labelKey);
            string current = _settings.Get(setting, values[0]);
            UiKit.AddFitButton(body, 400f, y, 520f, 48f, show(current) + "  →", () =>
            {
                int i = 0;
                for (int k = 0; k < values.Count; k++)
                {
                    if (values[k] == current)
                    {
                        i = k;
                    }
                }

                string next = values[(i + 1) % values.Count];
                _settings.Set(setting, next == values[0] ? string.Empty : next);
                Build();
            });
            return y + 60f;
        }

        /// <summary>A number row: the label, the value, − and +.</summary>
        private float NumberRow(Transform body, float y, string labelKey, string setting, double fallback, double step, double min, double max)
        {
            Label(body, y, labelKey);
            double value = _settings.GetDouble(setting, fallback);
            UiKit.AddText(body, 400f, y, 200f, 48f, value.ToString("0.##", CultureInfo.InvariantCulture), 20, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            void Step(double by)
            {
                _settings.Set(setting, Math.Round(Math.Min(max, Math.Max(min, value + by)), 2));
                Build();
            }

            UiKit.AddFitButton(body, 620f, y, 80f, 48f, "−", () => Step(-step));
            UiKit.AddFitButton(body, 710f, y, 80f, 48f, "+", () => Step(step));
            return y + 60f;
        }

        /// <summary>A toggle row bound to a 0/1 setting.</summary>
        private float ToggleRow(Transform body, float y, string labelKey, string setting)
        {
            bool on = _settings.GetBool(setting);
            UiKit.AddFitButton(body, 32f, y, 888f, 48f, (on ? "☑ " : "☐ ") + L(labelKey), () =>
            {
                _settings.Set(setting, on ? string.Empty : "1");
                Build();
            });
            return y + 60f;
        }

        // ---------------------------------------------------------------- the four markers

        private float BuildChest(Transform body, float y)
        {
            y = TextRow(body, y, "ui.ed.marker.items", "items", "iron_plate:2-4,gold_ingot:1", 200);
            var tables = new List<string> { string.Empty };
            if (_shell?.Content != null)
            {
                var keys = new List<string>(_shell.Content.LootTables.Keys);
                keys.Sort(string.CompareOrdinal);
                tables.AddRange(keys);
            }

            y = StepRow(body, y, "ui.ed.marker.table", "table", tables, v => v.Length == 0 ? L("ui.ed.marker.table_default") : v);
            y = NumberRow(body, y, "ui.ed.marker.chance", "chance", 1.0, 0.1, 0.0, 1.0);
            y = StepRow(body, y, "ui.ed.marker.block", "block", CrateBlocks, v => L("block." + v + ".name"));
            return ToggleRow(body, y, "ui.ed.marker.hidden", "hidden");
        }

        private float BuildVendor(Transform body, float y)
        {
            var themes = new List<string> { "settlers" };
            if (_shell?.Content != null)
            {
                foreach (string theme in MarketStock.Themes(_shell.Content.Recipes.Values))
                {
                    if (!themes.Contains(theme))
                    {
                        themes.Add(theme);
                    }
                }
            }

            y = StepRow(body, y, "ui.ed.marker.theme", "theme", themes, v => v);
            y = ToggleRow(body, y, "ui.ed.marker.inside", "inside");
            return TextRow(body, y, "ui.ed.marker.name", "name", "npc.role.vendor");
        }

        private float BuildSound(Transform body, float y)
        {
            y = TextRow(body, y, "ui.ed.marker.sound", "sound", KnownSounds[0]);
            Label(body, y, "ui.ed.marker.sound_pick");
            UiKit.AddFitButton(body, 400f, y, 520f, 48f, "→", () =>
            {
                int i = Array.IndexOf(KnownSounds, _settings.Get("sound"));
                _settings.Set("sound", KnownSounds[(i + 1) % KnownSounds.Length]);
                Build();
            });
            y += 60f;
            y = NumberRow(body, y, "ui.ed.marker.radius", "radius", 48, 8, 8, 256);
            return NumberRow(body, y, "ui.ed.marker.volume", "volume", 1.0, 0.1, 0.1, 1.0);
        }

        private float BuildDen(Transform body, float y)
        {
            var species = new List<string> { "native" };
            if (_shell?.Content != null)
            {
                var keys = new List<string>(_shell.Content.AuthoredCreatures.Keys);
                keys.Sort(string.CompareOrdinal);
                species.AddRange(keys);
            }

            y = StepRow(body, y, "ui.ed.marker.species", "species", species, v => v == "native" ? L("ui.ed.marker.species_native") : v);
            y = NumberRow(body, y, "ui.ed.marker.count", "count", 3, 1, 1, 12);
            return NumberRow(body, y, "ui.ed.marker.radius", "radius", 12, 2, 4, 48);
        }
    }
}
