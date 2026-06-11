using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Spacecraft.Client
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
        /// <summary>Builds the (initially hidden) overlay; returns its root for the caller to toggle.</summary>
        public static GameObject Build(AppShell shell, Transform root, WorldCreationOptions opt)
        {
            var dim = UiKit.AddImage(root, 0f, 0f, 1920f, 1080f, UiKit.SolidSprite, new Color(0f, 0f, 0f, 0.72f));
            dim.raycastTarget = true;
            var overlay = dim.gameObject;

            var panel = UiKit.AddPanel(overlay.transform, 160f, 90f, 1600f, 900f, UiKit.Panel).transform;
            UiKit.AddText(panel, 30f, 20f, 800f, 34f, shell.L("ui.worldopt.title"), 26, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            // Two stacked views inside the panel: the main sliders and the advanced per-type list.
            var main = UiKit.AddPanel(panel, 0f, 64f, 1600f, 770f, new Color(0f, 0f, 0f, 0f)).gameObject;
            var advanced = UiKit.AddPanel(panel, 0f, 64f, 1600f, 770f, new Color(0f, 0f, 0f, 0f)).gameObject;
            advanced.SetActive(false);

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
            const float RowH = 62f;

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
            ly += 10f;
            UiKit.AddText(main.transform, lx, ly, 700f, 24f, shell.L("ui.worldopt.col_survival"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            ly += 34f;
            Row(true, shell.L("ui.worldopt.oxygen"), L4("ui.worldopt.o2"), () => opt.Oxygen, v => opt.Oxygen = v);
            Row(true, shell.L("ui.worldopt.hunger"), new[] { shell.L("ui.toggle.off"), shell.L("ui.toggle.on") },
                () => opt.Hunger ? 1 : 0, v => opt.Hunger = v == 1);
            Row(true, shell.L("ui.worldopt.hazards"), L4("ui.worldopt.hz"), () => opt.Hazards, v => opt.Hazards = v);
            Row(true, shell.L("ui.worldopt.death"), L4("ui.worldopt.dp"), () => opt.DeathPenalty, v => opt.DeathPenalty = v);

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
            Row(false, shell.L("ui.worldopt.universe"), L4("ui.worldopt.size"), () => opt.UniverseSize, v => opt.UniverseSize = v);

            UiKit.AddButton(main.transform, rx, ry + 8f, 740f, 44f, shell.L("ui.worldopt.advanced"), () =>
            {
                main.SetActive(false);
                advanced.SetActive(true);
            });

            // ── Advanced: per-planet-type frequencies ──────────────────────────────────────
            BuildAdvanced(shell, advanced.transform, opt, freqSteps, () =>
            {
                advanced.SetActive(false);
                main.SetActive(true);
            });

            UiKit.AddButton(panel, 1290f, 842f, 280f, 48f, shell.L("ui.worldopt.done"), () => overlay.SetActive(false), "btn_singleplayer");

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

            var types = shell.Content?.Planets.Values
                .Where(p => p.Selectable)
                .OrderBy(p => p.Exotic)
                .ThenBy(p => p.Key, System.StringComparer.Ordinal)
                .ToList();
            if (types is null)
            {
                onBack();
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

            UiKit.AddButton(parent, 30f, 706f, 280f, 44f, shell.L("ui.worldopt.advanced_reset"), () => opt.PlanetTypes.Clear());
            UiKit.AddButton(parent, 330f, 706f, 280f, 44f, shell.L("ui.menu.back"), onBack);
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
