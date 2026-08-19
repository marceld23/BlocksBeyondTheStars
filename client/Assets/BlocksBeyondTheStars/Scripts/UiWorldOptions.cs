// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The "Weltoptionen" overlay of the world picker: quick presets (Friedlich/Standard/Feindselig)
    /// over discrete sliders for creatures, enemies, flora, ore, structures, universe size, exotic
    /// worlds and the survival rules — plus an advanced page with a per-planet-type frequency list.
    /// Pure UI: it edits a <see cref="WorldCreationOptions"/>; the picker turns that into server CLI
    /// overrides at launch, and the server bakes them into the new save.
    /// </summary>
    public static class UiWorldOptions
    {
        /// <summary>View-local y of the shared footer button row (= panel y 842; the views sit at panel
        /// y 64). Every page places its bottom controls on this line so nothing overlaps or misaligns.</summary>
        private const float FooterY = 778f;

        /// <summary>Builds the (initially hidden) overlay; returns its root for the caller to toggle.</summary>
        public static GameObject Build(AppShell shell, Transform root, WorldCreationOptions opt)
        {
            // Shared menu-modal chrome (#588): raising the tint alpha here used to be the "fix", but the panel
            // sprite bakes alpha 0.82, so it rendered 0.795 and the animated menu kept shimmering through.
            var (overlay, panel) = UiKit.AddModalOverlay(root, 160f, 90f, 1600f, 900f);
            UiKit.AddText(panel, 30f, 20f, 800f, 34f, shell.L("ui.worldopt.title"), 26, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            // Stacked views inside the panel: the main sliders, the advanced per-type list, and the
            // authored-structure (template) controls. Each view owns a shared footer row (FooterY):
            // page-navigation on the left, ONE "leave this page" action bottom-right (Done on main,
            // Back on sub-pages) — so only a single page's controls are ever visible at once.
            var main = UiKit.AddPanel(panel, 0f, 64f, 1600f, 826f, new Color(0f, 0f, 0f, 0f)).gameObject;
            var advanced = UiKit.AddPanel(panel, 0f, 64f, 1600f, 826f, new Color(0f, 0f, 0f, 0f)).gameObject;
            var structures = UiKit.AddPanel(panel, 0f, 64f, 1600f, 826f, new Color(0f, 0f, 0f, 0f)).gameObject;
            advanced.SetActive(false);
            structures.SetActive(false);

            // The content's structure-template packs feed the pack picker; the option model translates the
            // disabled set into the enabled list the server wants at launch.
            opt.KnownPacks.Clear();
            if (shell.Content?.StructurePacks != null)
            {
                opt.KnownPacks.AddRange(shell.Content.StructurePacks);
            }

            var rebuilders = new List<System.Action>(); // slider refreshers, run after a preset is applied

            // ── Presets ─────────────────────────────────────────────────────────────────────
            void Preset(float x, string label, System.Func<WorldCreationOptions> make)
                => UiKit.AddButton(main.transform, x, 8f, 300f, 44f, label, () =>
                {
                    opt.CopyFrom(make());
                    foreach (var r in rebuilders)
                    {
                        r();
                    }
                });

            UiKit.AddText(main.transform, 30f, 16f, 200f, 28f, shell.L("ui.worldopt.preset"), 17, UiKit.TextCol, TextAnchor.MiddleLeft);
            Preset(240f, shell.L("ui.worldopt.preset_peaceful"), WorldCreationOptions.Peaceful);
            Preset(560f, shell.L("ui.worldopt.preset_standard"), WorldCreationOptions.Standard);
            Preset(880f, shell.L("ui.worldopt.preset_hostile"), WorldCreationOptions.Hostile);

            // ── Slider rows (two columns) ──────────────────────────────────────────────────
            string[] L5(string prefix) => Enumerable.Range(0, 5).Select(i => shell.L($"{prefix}.{i}")).ToArray();
            string[] L4(string prefix) => Enumerable.Range(0, 4).Select(i => shell.L($"{prefix}.{i}")).ToArray();
            var activitySteps = L5("ui.worldopt.aa");
            var freqSteps = L5("ui.worldopt.fr");

            float lx = 30f, rx = 820f;
            float ly = 78f, ry = 78f;

            // Row pitch. The hard constraint is `78 + headers + rows*RowH <= FooterY` — a column that
            // grows past that line does not scroll or clip, it slides UNDER the footer buttons, which
            // then swallow its clicks (#983: the 11th left row, "keep ship", was unreachable at 62).
            // 56 is the pitch the advanced page already uses and leaves the longer column 22 px of air.
            // WorldOptionsLayoutTests holds the sums, so the next added row fails in CI, not on screen.
            const float RowH = 56f;

            void Row(bool leftCol, string label, string[] steps, System.Func<int> get, System.Action<int> set)
            {
                float x = leftCol ? lx : rx;
                float y = leftCol ? ly : ry;
                AddSliderRow(main.transform, x, y, 740f, label, steps, get, set, rebuilders);
                if (leftCol) { ly += RowH; } else { ry += RowH; }
            }

            // Left column: the living world + threats.
            UiKit.AddText(main.transform, lx, ly, 700f, 24f, shell.L("ui.worldopt.col_life"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            ly += 34f;
            Row(true, shell.L("ui.worldopt.creatures"), activitySteps, () => opt.Creatures, v => opt.Creatures = v);
            Row(true, shell.L("ui.worldopt.planet_enemies"), activitySteps, () => opt.PlanetEnemies, v => opt.PlanetEnemies = v);
            Row(true, shell.L("ui.worldopt.space_npcs"), activitySteps, () => opt.SpaceNpcs, v => opt.SpaceNpcs = v);
            Row(true, shell.L("ui.worldopt.ufos"), activitySteps, () => opt.Ufos, v => opt.Ufos = v);
            Row(true, shell.L("ui.worldopt.bandits"), activitySteps, () => opt.Bandits, v => opt.Bandits = v);
            ly += 10f;
            UiKit.AddText(main.transform, lx, ly, 700f, 24f, shell.L("ui.worldopt.col_survival"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            ly += 34f;
            Row(true, shell.L("ui.worldopt.oxygen"), L4("ui.worldopt.o2"), () => opt.Oxygen, v => opt.Oxygen = v);
            Row(true, shell.L("ui.worldopt.hunger"), L4("ui.worldopt.hg"), () => opt.Hunger, v => opt.Hunger = v);
            Row(true, shell.L("ui.worldopt.hazards"), L4("ui.worldopt.hz"), () => opt.Hazards, v => opt.Hazards = v);
            Row(true, shell.L("ui.worldopt.death"), L4("ui.worldopt.dp"), () => opt.DeathPenalty, v => opt.DeathPenalty = v);
            var onOff = new[] { shell.L("ui.toggle.off"), shell.L("ui.toggle.on") };
            Row(true, shell.L("ui.worldopt.space_combat"), onOff, () => opt.SpaceCombat ? 1 : 0, v => opt.SpaceCombat = v == 1);
            Row(true, shell.L("ui.worldopt.keep_ship"), onOff, () => opt.KeepShip ? 1 : 0, v => opt.KeepShip = v == 1);
            // Auto-aim (#693) intentionally has NO creation row: the page is a fixed grid and both columns
            // are close to the footer line (see RowH). New worlds start with the server default (ON); the
            // world admin flips it live in the in-game world-rules panel, and scripts can pass
            // --auto-aim false at launch.

            // Right column: the generated world.
            UiKit.AddText(main.transform, rx, ry, 700f, 24f, shell.L("ui.worldopt.col_world"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            ry += 34f;
            Row(false, shell.L("ui.worldopt.flora"), freqSteps, () => opt.Flora, v => opt.Flora = v);
            Row(false, shell.L("ui.worldopt.ore"), freqSteps, () => opt.Ore, v => opt.Ore = v);
            Row(false, shell.L("ui.worldopt.settlements"), freqSteps, () => opt.Settlements, v => opt.Settlements = v);
            Row(false, shell.L("ui.worldopt.wrecks"), freqSteps, () => opt.Wrecks, v => opt.Wrecks = v);
            Row(false, shell.L("ui.worldopt.vaults"), freqSteps, () => opt.Vaults, v => opt.Vaults = v);
            Row(false, shell.L("ui.worldopt.stations"), freqSteps, () => opt.Stations, v => opt.Stations = v);
            Row(false, shell.L("ui.worldopt.exotic"), freqSteps, () => opt.Exotic, v => opt.Exotic = v);
            // Five steps since #1123: the last one ("Growing") is a normal 8-system galaxy that appends a
            // new system whenever a player hyperjumps into one of the current outermost ones.
            Row(false, shell.L("ui.worldopt.universe"), L5("ui.worldopt.size"), () => opt.UniverseSize, v => opt.UniverseSize = v);

            // Story (P8): which story pack runs + how fast it unfolds.
            ry += 10f;
            UiKit.AddText(main.transform, rx, ry, 700f, 24f, shell.L("ui.worldopt.col_story"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            ry += 34f;
            Row(false, shell.L("ui.worldopt.story"),
                new[] { shell.L("ui.worldopt.story_default"), shell.L("ui.worldopt.story_none") },
                () => opt.Story, v => opt.Story = v);
            Row(false, shell.L("ui.worldopt.story_density"),
                new[] { shell.L("ui.worldopt.density_sparse"), shell.L("ui.worldopt.density_normal"), shell.L("ui.worldopt.density_dense") },
                () => opt.StoryDensity, v => opt.StoryDensity = v);

            // ── Footer (main page): page navigation left, Done right ───────────────────────
            UiKit.AddButton(main.transform, 30f, FooterY, 560f, 48f, shell.L("ui.worldopt.structures_btn"), () =>
            {
                main.SetActive(false);
                structures.SetActive(true);
            });
            UiKit.AddButton(main.transform, 620f, FooterY, 560f, 48f, shell.L("ui.worldopt.advanced"), () =>
            {
                main.SetActive(false);
                advanced.SetActive(true);
            });
            UiKit.AddButton(main.transform, 1290f, FooterY, 280f, 48f, shell.L("ui.worldopt.done"), () => overlay.SetActive(false), "btn_singleplayer");

            // ── Advanced: per-planet-type frequencies ──────────────────────────────────────
            BuildAdvanced(shell, advanced.transform, opt, freqSteps, () =>
            {
                advanced.SetActive(false);
                main.SetActive(true);
            });

            // ── Authored structures: template-use sliders + pack picker ─────────────────────
            BuildStructures(shell, structures.transform, opt, freqSteps, () =>
            {
                structures.SetActive(false);
                main.SetActive(true);
            });

            overlay.SetActive(false);
            return overlay;
        }

        /// <summary>The advanced page: every selectable planet type with its own frequency slider.
        /// Untouched rows follow the data weights + the simple exotic slider; touched rows write the
        /// per-type override map (which replaces ALL weights server-side once any entry exists).</summary>
        private static void BuildAdvanced(AppShell shell, Transform parent, WorldCreationOptions opt,
            string[] freqSteps, System.Action onBack)
        {
            UiKit.AddText(parent, 30f, 4f, 1000f, 28f, shell.L("ui.worldopt.advanced_title"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            var note = UiKit.AddText(parent, 30f, 34f, 1520f, 44f, shell.L("ui.worldopt.advanced_note"), 14, UiKit.CyanDim, TextAnchor.UpperLeft);
            note.horizontalOverflow = HorizontalWrapMode.Wrap;

            // Back sits in the shared footer slot (bottom-right = "leave this page" on every page) and is
            // built even when the page stays empty, so the sub-page can never trap the player.
            UiKit.AddButton(parent, 1290f, FooterY, 280f, 48f, shell.L("ui.menu.back"), onBack, "btn_singleplayer");

            var types = shell.Content?.Planets.Values
                .Where(p => p.Selectable)
                .OrderBy(p => p.Exotic)
                .ThenBy(p => p.Key, System.StringComparer.Ordinal)
                .ToList();
            if (types is null)
            {
                return;
            }

            // Default index = the type's data weight mapped onto the Frequency scale (display only).
            static int DefaultIndex(int spawnWeight) => spawnWeight switch
            {
                <= 1 => 1,  // VeryRare
                <= 4 => 2,  // Rare
                <= 9 => 3,  // Normal
                _ => 4,     // Frequent
            };

            float x = 30f, y = 86f;
            int column = 0;
            foreach (var p in types)
            {
                string key = p.Key;
                string label = shell.L(p.NameKey) + (p.Exotic ? " ◆" : string.Empty);
                AddSliderRow(parent, x, y, 740f, label, freqSteps,
                    () => opt.PlanetTypes.TryGetValue(key, out var v) ? v : DefaultIndex(p.SpawnWeight),
                    v => opt.PlanetTypes[key] = v,
                    rebuilders: null);

                y += 56f;
                if (y > 640f && column == 0)
                {
                    column = 1;
                    x = 820f;
                    y = 86f;
                }
            }

            UiKit.AddButton(parent, 30f, FooterY, 420f, 48f, shell.L("ui.worldopt.advanced_reset"), () => opt.PlanetTypes.Clear());
        }

        /// <summary>The authored-structures page: how readily hand-designed station/settlement templates are
        /// used in place of the procedural generator, plus a per-pack on/off picker. Empty pools ⇒ a note.</summary>
        private static void BuildStructures(AppShell shell, Transform parent, WorldCreationOptions opt,
            string[] freqSteps, System.Action onBack)
        {
            UiKit.AddText(parent, 30f, 4f, 1000f, 28f, shell.L("ui.worldopt.structures_title"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            var note = UiKit.AddText(parent, 30f, 34f, 1520f, 44f, shell.L("ui.worldopt.structures_note"), 14, UiKit.CyanDim, TextAnchor.UpperLeft);
            note.horizontalOverflow = HorizontalWrapMode.Wrap;

            float y = 96f;
            AddSliderRow(parent, 30f, y, 740f, shell.L("ui.worldopt.station_templates"), freqSteps,
                () => opt.StationTemplates, v => opt.StationTemplates = v, rebuilders: null);
            y += 62f;
            AddSliderRow(parent, 30f, y, 740f, shell.L("ui.worldopt.settlement_templates"), freqSteps,
                () => opt.SettlementTemplates, v => opt.SettlementTemplates = v, rebuilders: null);

            // Galaxy & terrain layout (#1114): the three worldgen regimes every new world ships with.
            // They live here (not the main page — its fixed grid is full, see RowH there) as opt-outs;
            // creation-only, like the template sliders above.
            y += 72f;
            UiKit.AddText(parent, 30f, y, 700f, 24f, shell.L("ui.worldopt.col_galaxy"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 34f;
            var galaxyOnOff = new[] { shell.L("ui.toggle.off"), shell.L("ui.toggle.on") };
            AddSliderRow(parent, 30f, y, 740f, shell.L("ui.worldopt.variance"), galaxyOnOff,
                () => opt.SystemVariance ? 1 : 0, v => opt.SystemVariance = v == 1, rebuilders: null);
            y += 62f;
            AddSliderRow(parent, 30f, y, 740f, shell.L("ui.worldopt.belts"), galaxyOnOff,
                () => opt.AsteroidBelts ? 1 : 0, v => opt.AsteroidBelts = v == 1, rebuilders: null);
            y += 62f;
            AddSliderRow(parent, 30f, y, 740f, shell.L("ui.worldopt.continents"), galaxyOnOff,
                () => opt.TerrainContinents ? 1 : 0, v => opt.TerrainContinents = v == 1, rebuilders: null);

            // Pack picker (right column): one toggle per pack; "on" = enabled = not in DisabledPacks.
            float px = 820f, py = 96f;
            UiKit.AddText(parent, px, py, 740f, 24f, shell.L("ui.worldopt.packs_title"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            py += 34f;
            if (opt.KnownPacks.Count == 0)
            {
                UiKit.AddText(parent, px, py, 740f, 40f, shell.L("ui.worldopt.packs_none"), 14, UiKit.CyanDim, TextAnchor.UpperLeft);
            }
            else
            {
                foreach (var pack in opt.KnownPacks)
                {
                    string p = pack;
                    Text label = null;
                    void Refresh() => label.text = (opt.DisabledPacks.Contains(p) ? "☐  " : "☑  ") + p;
                    UiKit.AddButton(parent, px, py, 56f, 36f, shell.L("ui.worldopt.packs_toggle"), () =>
                    {
                        if (!opt.DisabledPacks.Remove(p)) { opt.DisabledPacks.Add(p); }
                        Refresh();
                    });
                    label = UiKit.AddText(parent, px + 66f, py, 660f, 36f, string.Empty, 15, UiKit.TextCol, TextAnchor.MiddleLeft);
                    Refresh();
                    py += 44f;
                }
            }

            UiKit.AddButton(parent, 1290f, FooterY, 280f, 48f, shell.L("ui.menu.back"), onBack, "btn_singleplayer");
        }

        /// <summary>A labelled discrete slider (whole steps) with the current step's name beside it.</summary>
        private static void AddSliderRow(Transform parent, float x, float y, float w, string label,
            string[] steps, System.Func<int> get, System.Action<int> set, List<System.Action> rebuilders)
        {
            UiKit.AddText(parent, x, y, 280f, 40f, label, 16, UiKit.TextCol, TextAnchor.MiddleLeft);

            var go = new GameObject("Slider", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, x + 290f, y + 12f, w - 290f - 150f, 16f);

            var bg = UiKit.AddImage(go.transform, 0f, 4f, w - 290f - 150f, 8f, UiKit.SolidSprite, new Color(0.10f, 0.16f, 0.24f, 1f));

            var fillArea = new GameObject("Fill", typeof(RectTransform));
            fillArea.transform.SetParent(go.transform, false);
            var fillRt = fillArea.GetComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0f, 0.25f);
            fillRt.anchorMax = new Vector2(1f, 0.75f);
            fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
            var fill = fillArea.AddComponent<Image>();
            fill.sprite = UiKit.SolidSprite;
            fill.color = UiKit.Cyan;

            var handleGo = new GameObject("Handle", typeof(RectTransform));
            handleGo.transform.SetParent(go.transform, false);
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(18f, 26f);
            var handle = handleGo.AddComponent<Image>();
            handle.sprite = UiKit.SolidSprite;
            handle.color = Color.white;

            var slider = go.AddComponent<Slider>();
            slider.targetGraphic = handle;
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.minValue = 0;
            slider.maxValue = steps.Length - 1;
            slider.wholeNumbers = true;
            slider.value = Mathf.Clamp(get(), 0, steps.Length - 1);

            var valueText = UiKit.AddText(parent, x + w - 140f, y, 140f, 40f, steps[(int)slider.value], 15, UiKit.Cyan, TextAnchor.MiddleLeft);

            slider.onValueChanged.AddListener(v =>
            {
                int idx = Mathf.Clamp(Mathf.RoundToInt(v), 0, steps.Length - 1);
                set(idx);
                valueText.text = steps[idx];
            });

            rebuilders?.Add(() =>
            {
                int idx = Mathf.Clamp(get(), 0, steps.Length - 1);
                slider.SetValueWithoutNotify(idx);
                valueText.text = steps[idx];
            });
        }
    }
}
