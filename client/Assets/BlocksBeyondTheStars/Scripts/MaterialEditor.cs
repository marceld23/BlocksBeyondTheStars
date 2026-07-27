// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Material designer (menu tool, sibling of the ship/avatar/structure/content editors). You paint a
    /// 64×64 block texture directly on a live canvas (or point at a source PNG the merge tool decodes),
    /// set its mining mechanics (hardness, tool, drops), its look (gloss/metal/emission/base colour) and
    /// where it spawns in the world (frequency + depth band + which kind of planet — airless, with
    /// atmosphere, single- or multi-biome). <b>Save</b> writes a material bundle (material.json +
    /// texture.bytes) a developer folds into the game with tools/merge_material.py (→ data/blocks.json,
    /// items.json, planets.json, the bundled Resources texture + locale placeholders). Modern uGUI.
    /// </summary>
    public sealed class MaterialEditor : MonoBehaviour
    {
        public AppShell Shell;

        private const int Tile = 64; // matches BlockTextureAtlas tile size

        // What a block can require to be MINED. data/blocks.json only ever uses these two — a scanner cannot
        // break anything, so offering it produced a block nobody could mine (#511).
        private static readonly string[] Tools = { "none", "drill" };
        // Palette/UI grouping, mirroring the categories every shipped block carries (#510).
        private static readonly string[] BlockCategories = { "ore", "terrain", "building", "machine", "light", "door", "flora" };
        // Where the ore vein gets added by the merge tool: any planet, only airless / only with an
        // atmosphere, only single-biome worlds or only multi-biome worlds.
        private static readonly string[] WorldTypes = { "any", "airless", "atmosphere", "single_biome", "multi_biome" };

        // A small preset palette to paint with (plus the live base colour).
        private static readonly Color[] Swatches =
        {
            new Color(0.55f, 0.55f, 0.57f), new Color(0.32f, 0.22f, 0.14f), new Color(0.16f, 0.16f, 0.18f),
            new Color(0.70f, 0.50f, 0.35f), new Color(0.45f, 0.62f, 0.45f), new Color(0.40f, 0.70f, 0.90f),
            new Color(0.85f, 0.78f, 0.45f), new Color(0.80f, 0.30f, 0.25f), new Color(0.55f, 0.35f, 0.75f),
            new Color(0.95f, 0.95f, 0.92f), new Color(0.10f, 0.10f, 0.12f), new Color(0.30f, 0.85f, 0.60f),
        };

        // --- material ---
        private string _key = "my_material", _name = "My Material", _desc = "A custom material.";
        private float _hardness = 3f;
        private int _tool;            // index into Tools
        private int _minTier;
        private int _category;        // index into BlockCategories
        private bool _tintable = true; // may the player dye/re-shape it (plain materials should)
        private float _gloss = 0.1f, _metal, _emission;
        private int _baseR = 140, _baseG = 140, _baseB = 145;
        // --- world placement ---
        private float _frequency = 0.06f;
        // The shipped veins all run to 2048 — the world is that deep since the worldgen overhaul, and a band
        // that stops at 256 would leave the material out of most of the crust (#509).
        private int _minDepth = 4, _maxDepth = 2048;
        private int _worldType;       // index into WorldTypes
        private string _sourceImage = string.Empty; // optional PNG path the merge tool decodes
        private Text _spawnEstimate;

        // --- paint state ---
        private Texture2D _tex;
        private RawImage _canvas;
        private RectTransform _canvasRt;
        private Color _paint = new Color(0.55f, 0.55f, 0.57f);
        private Image _baseSwatch, _activeSwatch;

        private Canvas _ui;
        private Text _status;

        private void Start()
        {
            _tex = new Texture2D(Tile, Tile, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            FillBase(true);
            BuildUi();
        }

        private void OnDestroy()
        {
            if (_ui != null) Destroy(_ui.gameObject);
            if (_tex != null) Destroy(_tex);
        }

        // ── live painting ────────────────────────────────────────────────────────────────────────

        private void Update()
        {
            if (_canvasRt == null) return;

            bool left = Input.GetMouseButton(0), right = Input.GetMouseButton(1);
            if (!left && !right) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_canvasRt, Input.mousePosition, null)) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, Input.mousePosition, null, out var lp)) return;

            // Place() anchors the rect top-left with pivot (0,1): local x∈[0,w], y∈[-h,0].
            float w = _canvasRt.rect.width, h = _canvasRt.rect.height;
            float u = Mathf.Clamp01(lp.x / w);
            float fromTop = Mathf.Clamp01(-lp.y / h);
            int tx = Mathf.Clamp(Mathf.RoundToInt(u * (Tile - 1)), 0, Tile - 1);
            int ty = Mathf.Clamp(Mathf.RoundToInt((1f - fromTop) * (Tile - 1)), 0, Tile - 1); // top row = ty 63

            var c = right ? new Color(_baseR / 255f, _baseG / 255f, _baseB / 255f) : _paint;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int px = tx + dx, py = ty + dy;
                if (px >= 0 && px < Tile && py >= 0 && py < Tile) _tex.SetPixel(px, py, c);
            }

            _tex.Apply();
        }

        /// <summary>Fills the whole tile with the base colour; <paramref name="grain"/> adds per-pixel noise.</summary>
        private void FillBase(bool grain)
        {
            var baseCol = new Color(_baseR / 255f, _baseG / 255f, _baseB / 255f);
            var rng = new System.Random(12345);
            for (int x = 0; x < Tile; x++)
            for (int y = 0; y < Tile; y++)
            {
                float n = grain ? 0.86f + 0.28f * (float)rng.NextDouble() : 1f;
                var c = new Color(baseCol.r * n, baseCol.g * n, baseCol.b * n, 1f);
                if (grain && (x == 0 || y == 0 || x == Tile - 1 || y == Tile - 1)) c *= 0.7f; // tiled edge
                _tex.SetPixel(x, y, c);
            }

            _tex.Apply();
        }

        // ── UI ───────────────────────────────────────────────────────────────────────────────────

        private void BuildUi()
        {
            _ui = UiKit.CreateCanvas("Material Editor UI");
            _ui.sortingOrder = 5;
            var root = _ui.transform;

            // Developer-tool banner: its output needs a merge + rebuild and does not affect the current game.
            // Sits ABOVE the panels' top edge — the panels are later siblings, so any overlap would cut the
            // bottom off this very warning (#513).
            UiKit.AddText(root, 16f, 0f, 1400f, 26f, L("ui.editors.devbanner"), 15, UiKit.Warn, TextAnchor.MiddleLeft, FontStyle.Bold);

            // ── Left: paint canvas + palette + base colour ───────────────────────────────────────
            var left = UiKit.AddPanel(root, 16f, 30f, 540f, 1010f, UiKit.PanelFill).transform;
            UiKit.AddText(left, 16f, 12f, 500f, 26f, L("ui.material.texture"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            // Live paint surface (RawImage showing the 64×64 texture, point-filtered).
            var canvasGo = new GameObject("PaintCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(left, false);
            _canvasRt = UiKit.Place(canvasGo, 20f, 50f, 480f, 480f);
            _canvas = canvasGo.AddComponent<RawImage>();
            _canvas.texture = _tex;

            // Palette swatches.
            UiKit.AddText(left, 20f, 540f, 200f, 24f, L("ui.material.brush"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            for (int i = 0; i < Swatches.Length; i++)
            {
                int idx = i;
                float sx = 20f + (i % 6) * 44f, sy = 568f + (i / 6) * 44f;
                var sw = MakeSwatch(left, sx, sy, Swatches[i], () => SetPaint(Swatches[idx], _swatchImgs[idx]));
                _swatchImgs[i] = sw;
            }

            // Base colour (drives Fill, the data-driven tint + the right-click eraser).
            float by = 660f;
            UiKit.AddText(left, 20f, by, 200f, 24f, L("ui.material.base_color"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            _baseSwatch = MakeSwatch(left, 230f, by - 2f, BaseColor(), () => SetPaint(BaseColor(), _baseSwatch));
            by += 30f;
            Stepper(left, ref by, "R", () => _baseR, v => { _baseR = (int)v; RefreshBase(); }, 0, 255, 15, "0");
            Stepper(left, ref by, "G", () => _baseG, v => { _baseG = (int)v; RefreshBase(); }, 0, 255, 15, "0");
            Stepper(left, ref by, "B", () => _baseB, v => { _baseB = (int)v; RefreshBase(); }, 0, 255, 15, "0");

            by += 6f;
            UiKit.AddButton(left, 20f, by, 150f, 34f, L("ui.material.fill"), () => FillBase(true));
            UiKit.AddButton(left, 180f, by, 150f, 34f, L("ui.material.flat"), () => FillBase(false));
            UiKit.AddButton(left, 340f, by, 160f, 34f, L("ui.material.clear"), () => { _baseR = _baseG = _baseB = 20; RefreshBase(); FillBase(false); });

            // ── Right: mechanics + look + world placement + footer ───────────────────────────────
            var right = UiKit.AddPanel(root, 572f, 30f, 470f, 1010f, UiKit.PanelFill).transform;
            UiKit.AddText(right, 16f, 12f, 440f, 26f, L("ui.material.material"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            float y = 50f;
            InputRow(right, ref y, L("ui.content.key"), _key, v => _key = v, KeyLimit);
            InputRow(right, ref y, L("ui.content.name"), _name, v => _name = v, NameLimit);
            TextAreaRow(right, ref y, L("ui.content.desc"), _desc, v => _desc = v);

            Header(right, ref y, L("ui.material.mechanics"));
            Stepper(right, ref y, L("ui.material.hardness"), () => _hardness, v => _hardness = v, 0.2f, 12f, 0.2f, "0.0");
            CycleRow(right, ref y, L("ui.material.tool"), Display("blocktool", Tools), () => _tool, i => _tool = i);
            Stepper(right, ref y, L("ui.material.min_tier"), () => _minTier, v => _minTier = (int)v, 0, 3, 1, "0");
            CycleRow(right, ref y, L("ui.material.category"), CategoryLabels(), () => _category, i => _category = i);
            ToggleRow(right, ref y, L("ui.material.tintable"), () => _tintable, v => _tintable = v);

            Header(right, ref y, L("ui.material.look"));
            Stepper(right, ref y, L("ui.material.gloss"), () => _gloss, v => _gloss = v, 0, 1, 0.05f, "0.00");
            Stepper(right, ref y, L("ui.material.metal"), () => _metal, v => _metal = v, 0, 1, 0.05f, "0.00");
            Stepper(right, ref y, L("ui.material.emission"), () => _emission, v => _emission = v, 0, 1, 0.05f, "0.00");

            Header(right, ref y, L("ui.material.world"));
            CycleRow(right, ref y, L("ui.material.world_type"), Display("worldtype", WorldTypes), () => _worldType, i => _worldType = i);
            Stepper(right, ref y, L("ui.material.frequency"), () => _frequency, v => _frequency = v, 0, 0.5f, 0.01f, "0.00", RefreshSpawnEstimate);
            // What the generator actually does with that number — the raw value saturates, so show the share
            // of deep rock this vein ends up claiming (#509).
            _spawnEstimate = UiKit.AddText(right, 16f, y, 440f, 22f, string.Empty, 13, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 26f;
            Stepper(right, ref y, L("ui.material.min_depth"), () => _minDepth, v => _minDepth = (int)v, 0, 256, 2, "0", RefreshSpawnEstimate);
            Stepper(right, ref y, L("ui.material.max_depth"), () => _maxDepth, v => _maxDepth = (int)v, 8, 2048, 64, "0");
            InputRow(right, ref y, L("ui.material.source_png"), _sourceImage, v => _sourceImage = v, PathLimit);
            RefreshSpawnEstimate();

            // Footer.
            _status = UiKit.AddText(right, 16f, 1010f - 152f, 446f, 78f, string.Empty, 13, UiKit.Ok, TextAnchor.UpperLeft);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddButton(right, 16f, 1010f - 70f, 260f, 40f, L("ui.material.save"), Export);
            UiKit.AddButton(right, 286f, 1010f - 70f, 168f, 40f, L("ui.menu.back"), () => Shell?.CloseMaterialEditor());

            // Controls hint.
            UiKit.AddText(root, 16f, 1072f - 28f, 1400f, 24f, L("ui.material.hint"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);

            SetPaint(_paint, _swatchImgs[0]);
        }

        private readonly Image[] _swatchImgs = new Image[12];

        private Color BaseColor() => new Color(_baseR / 255f, _baseG / 255f, _baseB / 255f);

        private void RefreshBase()
        {
            if (_baseSwatch != null) _baseSwatch.color = BaseColor();
        }

        private void SetPaint(Color c, Image swatch)
        {
            _paint = c;
            if (_activeSwatch != null) _activeSwatch.transform.localScale = Vector3.one;
            _activeSwatch = swatch;
            if (_activeSwatch != null) _activeSwatch.transform.localScale = Vector3.one * 1.18f;
        }

        private Image MakeSwatch(Transform parent, float x, float y, Color color, Action onClick)
        {
            var go = new GameObject("Swatch", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, x, y, 38f, 38f);
            var img = go.AddComponent<Image>();
            img.sprite = UiKit.SolidSprite;
            img.color = color;
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            return img;
        }

        // ── export ───────────────────────────────────────────────────────────────────────────────

        [Serializable]
        private sealed class MaterialBundle
        {
            public string key, name, desc, requiredTool, worldType, sourceImage, category;
            public float hardness, gloss, metal, emission, frequency;
            public int minToolTier, minDepth, maxDepth, colorRgb;
            public bool tintable;
        }

        private void Export()
        {
            string key = Slug(_key);
            if (string.IsNullOrEmpty(key))
            {
                SetStatus(L("ui.content.need_key"));
                return;
            }

            var b = new MaterialBundle
            {
                key = key, name = _name, desc = _desc, category = BlockCategories[_category], tintable = _tintable,
                requiredTool = Tools[_tool], worldType = WorldTypes[_worldType],
                sourceImage = string.IsNullOrWhiteSpace(_sourceImage) ? null : _sourceImage.Trim(),
                hardness = _hardness, gloss = _gloss, metal = _metal, emission = _emission, frequency = _frequency,
                minToolTier = _minTier, minDepth = _minDepth, maxDepth = Mathf.Max(_minDepth, _maxDepth),
                colorRgb = (_baseR << 16) | (_baseG << 8) | _baseB,
            };

            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "material_exports", key);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "material.json"), JsonUtility.ToJson(b, true));
                // Raw RGBA32 tile in the exact layout BlockTextureAtlas.LoadRawTextureData expects.
                File.WriteAllBytes(Path.Combine(dir, "texture.bytes"), _tex.GetRawTextureData());
                SetStatus($"{L("ui.material.exported")}\n{dir}");
            }
            catch (Exception e)
            {
                SetStatus("Export failed: " + e.Message);
            }
        }

        private void SetStatus(string s) { if (_status != null) _status.text = s; }

        // ── small uGUI form helpers (mirrors ContentEditor) ────────────────────────────────────────

        private static void Header(Transform p, ref float y, string text)
        {
            y += 6f;
            UiKit.AddText(p, 16f, y, 440f, 24f, text, 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 28f;
        }

        /// <summary>Character limits sized so the typed text always FITS its field instead of scrolling out
        /// of sight (#514); a source path may be longer — it scrolls while you type it.</summary>
        private const int KeyLimit = 28, NameLimit = 32, DescLimit = 160, PathLimit = 120;

        /// <summary>Form-field text size — smaller than the UiKit default so a full key/name fits the box.</summary>
        private const int FieldFont = 15;

        /// <summary>Localized label for a raw option value (<c>ui.opt.&lt;set&gt;.&lt;value&gt;</c>), falling
        /// back to the identifier itself so a newly added option still shows something (#516).</summary>
        private string Opt(string set, string value)
        {
            string key = "ui.opt." + set + "." + value;
            return Shell != null && Shell.Localizer != null && Shell.Localizer.Has(key) ? Shell.L(key) : value;
        }

        /// <summary>The display labels for a whole option set; the exported value stays the raw identifier.</summary>
        private string[] Display(string set, string[] values)
        {
            var labels = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                labels[i] = Opt(set, values[i]);
            }

            return labels;
        }

        /// <summary>Block-category labels reusing the palette section titles (<c>ui.cat.*</c>), so the editor
        /// names a category exactly like the build palettes it will show up in.</summary>
        private string[] CategoryLabels()
        {
            var labels = new string[BlockCategories.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                string key = "ui.cat." + BlockCategories[i];
                labels[i] = Shell != null && Shell.Localizer != null && Shell.Localizer.Has(key) ? Shell.L(key) : BlockCategories[i];
            }

            return labels;
        }

        /// <summary>Restates the frequency slider as the share of deep rock the vein will actually claim:
        /// <c>WorldGenerator.SelectOre</c> scales and caps the raw rarity (0.30/0.08 for a shallow band,
        /// 0.15/0.05 deeper), so the number on the slider is not the number in the world.</summary>
        private void RefreshSpawnEstimate()
        {
            if (_spawnEstimate == null)
            {
                return;
            }

            bool shallow = _minDepth <= 8;
            float scale = shallow ? 0.30f : 0.15f, cap = shallow ? 0.08f : 0.05f;
            float frac = Mathf.Clamp(_frequency * scale, 0f, cap);
            _spawnEstimate.text = string.Format(L("ui.material.spawn_estimate"), (frac * 100f).ToString("0.0"));
        }

        private static void InputRow(Transform p, ref float y, string label, string value, Action<string> onChange, int maxLength)
        {
            UiKit.AddText(p, 16f, y, 180f, 30f, label, 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(p, 200f, y, 256f, 30f, value, onChange, string.Empty, maxLength, FieldFont);
            y += 38f;
        }

        /// <summary>A wrapping multi-line field for sentence-length text (see ContentEditor).</summary>
        private static void TextAreaRow(Transform p, ref float y, string label, string value, Action<string> onChange)
        {
            UiKit.AddText(p, 16f, y, 180f, 30f, label, 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(p, 200f, y, 256f, 64f, value, onChange, string.Empty, DescLimit, FieldFont, multiline: true);
            y += 72f;
        }

        /// <param name="onChanged">Optional extra callback for rows whose value feeds a live readout.</param>
        private static void Stepper(Transform p, ref float y, string label, Func<float> get, Action<float> set,
            float min, float max, float step, string fmt, Action onChanged = null)
        {
            UiKit.AddText(p, 16f, y, 200f, 30f, label, 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            var val = UiKit.AddText(p, 300f, y, 80f, 30f, get().ToString(fmt), 15, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            void Apply(float delta)
            {
                set(Mathf.Clamp(get() + delta, min, max));
                val.text = get().ToString(fmt);
                onChanged?.Invoke();
            }

            UiKit.AddButton(p, 268f, y + 1f, 28f, 28f, "−", () => Apply(-step));
            UiKit.AddButton(p, 384f, y + 1f, 28f, 28f, "+", () => Apply(step));
            y += 38f;
        }

        /// <param name="labels">Localized display labels, one per option — the stored value stays the index
        /// into the raw identifier list.</param>
        private static void CycleRow(Transform p, ref float y, string label, string[] labels, Func<int> get, Action<int> set)
        {
            UiKit.AddText(p, 16f, y, 150f, 30f, label, 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            var val = UiKit.AddText(p, 200f, y, 180f, 30f, labels[get()], 15, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddButton(p, 384f, y + 1f, 28f, 28f, "→", () => { set((get() + 1) % labels.Length); val.text = labels[get()]; });
            y += 38f;
        }

        private void ToggleRow(Transform p, ref float y, string label, Func<bool> get, Action<bool> set)
        {
            UiKit.AddText(p, 16f, y, 180f, 30f, label, 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            var btn = UiKit.AddButton(p, 200f, y, 154f, 30f, OffOn(get()), null);
            var caption = btn.GetComponentInChildren<Text>();
            btn.onClick.AddListener(() =>
            {
                set(!get());
                caption.text = OffOn(get());
            });
            y += 38f;
        }

        private string OffOn(bool on) => on ? L("ui.avatar.on") : L("ui.avatar.off");

        private static string Slug(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var sb = new System.Text.StringBuilder();
            foreach (char c in s.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (c == ' ' || c == '-' || c == '_') sb.Append('_');
            }

            return sb.ToString();
        }

        private string L(string key) => Shell?.L(key) ?? key;
    }
}
