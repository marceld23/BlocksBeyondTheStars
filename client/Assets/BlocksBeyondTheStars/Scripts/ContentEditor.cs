using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Item + recipe designer (menu tool, sibling of the ship/avatar/structure editors). A data form to
    /// define a new item (key/name/category/stats), its crafting recipe (station, inputs, output count)
    /// and optional blueprint gating (knowledge + unlock cost). <b>Save</b> writes a content bundle
    /// (content.json) a developer folds into the game with tools/merge_recipe.py (→ data/items.json,
    /// recipes.json, blueprints.json + locale placeholders). Self-contained on the client; modern uGUI.
    /// </summary>
    public sealed class ContentEditor : MonoBehaviour
    {
        public AppShell Shell;

        private static readonly string[] Categories = { "material", "block", "tool", "consumable", "component" };
        private static readonly string[] ToolKinds = { "none", "drill", "blockPlacer", "scanner", "repair", "weapon" };
        private static readonly string[] Stations = { "hand", "workshop", "refinery", "lab", "machineRoom", "detoxifier", "market" };

        // --- item ---
        private string _key = "my_item", _name = "My Item", _desc = "A custom item.", _placesBlock = string.Empty;
        private int _category, _maxStack = 99;
        private int _toolKind;
        private float _tier = 1, _miningPower = 1, _damage, _range, _energy;
        private float _consumeHealth, _consumeHunger, _armor, _oxygen, _scan = 1f;
        // --- recipe ---
        private int _station = 1; // workshop
        private int _outputCount = 1;
        private readonly List<Amount> _inputs = new() { new Amount() };
        // --- blueprint ---
        private bool _hasBlueprint;
        private int _knowledgeCost;
        private readonly List<Amount> _unlock = new();

        private sealed class Amount { public string Item = "iron_plate"; public int Count = 1; }

        private Canvas _canvas;
        private Text _status;
        private RectTransform _inputList, _unlockList;
        private readonly List<RowUi> _inputRows = new();
        private readonly List<RowUi> _unlockRows = new();
        private Text _bpToggleLabel;

        private sealed class RowUi { public GameObject Go; public InputField ItemF, CountF; public Amount Bound; }

        private void Start() => BuildUi();

        private void OnDestroy()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        private void BuildUi()
        {
            _canvas = UiKit.CreateCanvas("Content Editor UI");
            _canvas.sortingOrder = 5;
            var root = _canvas.transform;

            // ── Left panel: item + stats ──────────────────────────────────────────────────────
            var left = UiKit.AddPanel(root, 16f, 16f, 470f, 1048f, UiKit.PanelFill).transform;
            UiKit.AddText(left, 16f, 12f, 440f, 26f, L("ui.content.item"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            float y = 50f;
            InputRow(left, ref y, L("ui.content.key"), _key, v => _key = v);
            InputRow(left, ref y, L("ui.content.name"), _name, v => _name = v);
            InputRow(left, ref y, L("ui.content.desc"), _desc, v => _desc = v);
            CycleRow(left, ref y, L("ui.content.category"), Categories, () => _category, i => _category = i);
            Stepper(left, ref y, L("ui.content.max_stack"), () => _maxStack, v => _maxStack = (int)v, 1, 999, 1, "0");
            InputRow(left, ref y, L("ui.content.places_block"), _placesBlock, v => _placesBlock = v);

            Header(left, ref y, L("ui.content.tool_stats"));
            CycleRow(left, ref y, L("ui.content.tool_kind"), ToolKinds, () => _toolKind, i => _toolKind = i);
            Stepper(left, ref y, L("ui.content.tier"), () => _tier, v => _tier = v, 1, 5, 1, "0");
            Stepper(left, ref y, L("ui.content.mining_power"), () => _miningPower, v => _miningPower = v, 0, 8, 0.5f, "0.0");
            Stepper(left, ref y, L("ui.content.damage"), () => _damage, v => _damage = v, 0, 200, 5, "0");
            Stepper(left, ref y, L("ui.content.range"), () => _range, v => _range = v, 0, 80, 1, "0");
            Stepper(left, ref y, L("ui.content.energy"), () => _energy, v => _energy = v, 0, 20, 1, "0");

            Header(left, ref y, L("ui.content.effects"));
            Stepper(left, ref y, L("ui.content.consume_health"), () => _consumeHealth, v => _consumeHealth = v, -50, 100, 5, "0");
            Stepper(left, ref y, L("ui.content.consume_hunger"), () => _consumeHunger, v => _consumeHunger = v, 0, 100, 5, "0");
            Stepper(left, ref y, L("ui.content.armor"), () => _armor, v => _armor = v, 0, 0.75f, 0.05f, "0.00");
            Stepper(left, ref y, L("ui.content.oxygen"), () => _oxygen, v => _oxygen = v, 0, 100, 5, "0");

            // ── Right panel: recipe + blueprint + footer ──────────────────────────────────────
            var right = UiKit.AddPanel(root, 502f, 16f, 470f, 1048f, UiKit.PanelFill).transform;
            UiKit.AddText(right, 16f, 12f, 440f, 26f, L("ui.content.recipe"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            float ry = 50f;
            CycleRow(right, ref ry, L("ui.content.station"), Stations, () => _station, i => _station = i);
            Stepper(right, ref ry, L("ui.content.output_count"), () => _outputCount, v => _outputCount = (int)v, 1, 64, 1, "0");

            // Recipe inputs (dynamic list).
            UiKit.AddText(right, 16f, ry, 300f, 24f, L("ui.content.inputs"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddButton(right, 330f, ry - 4f, 124f, 30f, L("ui.content.add"), () => { _inputs.Add(new Amount()); RefreshRows(_inputs, _inputRows, _inputList); });
            ry += 30f;
            _inputList = UiKit.ScrollList(right, 12f, ry, 446f, 190f);
            ry += 200f;

            // Blueprint gating.
            UiKit.AddText(right, 16f, ry, 300f, 26f, L("ui.content.blueprint"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            var tgl = UiKit.AddButton(right, 300f, ry - 2f, 154f, 30f, OffOn(_hasBlueprint), ToggleBlueprint);
            _bpToggleLabel = tgl.GetComponentInChildren<Text>();
            ry += 34f;
            Stepper(right, ref ry, L("ui.content.knowledge"), () => _knowledgeCost, v => _knowledgeCost = (int)v, 0, 50, 1, "0");
            UiKit.AddText(right, 16f, ry, 300f, 24f, L("ui.content.unlock_cost"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddButton(right, 330f, ry - 4f, 124f, 30f, L("ui.content.add"), () => { _unlock.Add(new Amount()); RefreshRows(_unlock, _unlockRows, _unlockList); });
            ry += 30f;
            _unlockList = UiKit.ScrollList(right, 12f, ry, 446f, 150f);

            // Footer.
            _status = UiKit.AddText(right, 16f, 1048f - 150f, 446f, 70f, string.Empty, 13, UiKit.Ok, TextAnchor.UpperLeft);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddButton(right, 16f, 1048f - 70f, 260f, 40f, L("ui.content.save"), Export);
            UiKit.AddButton(right, 286f, 1048f - 70f, 168f, 40f, L("ui.menu.back"), () => Shell?.CloseContentEditor());

            RefreshRows(_inputs, _inputRows, _inputList);
            RefreshRows(_unlock, _unlockRows, _unlockList);

            // Controls hint.
            UiKit.AddText(root, 16f, 1072f - 28f, 1200f, 24f, L("ui.content.hint"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
        }

        private void ToggleBlueprint()
        {
            _hasBlueprint = !_hasBlueprint;
            _bpToggleLabel.text = OffOn(_hasBlueprint);
        }

        private string OffOn(bool on) => on ? L("ui.avatar.on") : L("ui.avatar.off");

        // ── dynamic cost rows ──────────────────────────────────────────────────────────────────

        private void RefreshRows(List<Amount> model, List<RowUi> pool, RectTransform list)
        {
            int i = 0;
            foreach (var a in model)
            {
                var ui = i < pool.Count ? pool[i] : MakeRow(pool, list);
                ui.Bound = null;
                ui.ItemF.SetTextWithoutNotify(a.Item);
                ui.CountF.SetTextWithoutNotify(a.Count.ToString());
                ui.Bound = a;
                ui.Go.SetActive(true);
                i++;
            }

            for (; i < pool.Count; i++) pool[i].Go.SetActive(false);
        }

        private RowUi MakeRow(List<RowUi> pool, RectTransform list)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(list, false);
            go.AddComponent<LayoutElement>().minHeight = 30f;
            var ui = new RowUi { Go = go };
            ui.ItemF = UiKit.AddInput(go.transform, 4f, 2f, 280f, 26f, string.Empty, null, "item key");
            ui.CountF = UiKit.AddInput(go.transform, 290f, 2f, 80f, 26f, string.Empty, null);
            ui.CountF.contentType = InputField.ContentType.IntegerNumber;
            UiKit.AddButton(go.transform, 376f, 2f, 30f, 26f, "×", () =>
            {
                if (ui.Bound != null)
                {
                    if (pool == _inputRows) { _inputs.Remove(ui.Bound); RefreshRows(_inputs, _inputRows, _inputList); }
                    else { _unlock.Remove(ui.Bound); RefreshRows(_unlock, _unlockRows, _unlockList); }
                }
            });
            ui.ItemF.onValueChanged.AddListener(v => { if (ui.Bound != null) ui.Bound.Item = v; });
            ui.CountF.onValueChanged.AddListener(v => { if (ui.Bound != null && int.TryParse(v, out var c)) ui.Bound.Count = Mathf.Max(0, c); });
            pool.Add(ui);
            return ui;
        }

        // ── export ─────────────────────────────────────────────────────────────────────────────

        [Serializable] private sealed class AmountJson { public string item; public int count; }
        [Serializable]
        private sealed class ContentBundle
        {
            public string key, name, desc, category, placesBlock, toolKind, station;
            public int maxStack, tier, outputCount, knowledgeCost;
            public float miningPower, damage, range, energy, consumeHealth, consumeHunger, armor, oxygen, scan;
            public bool hasBlueprint;
            public List<AmountJson> inputs = new();
            public List<AmountJson> unlockCost = new();
        }

        private void Export()
        {
            string key = Slug(_key);
            if (string.IsNullOrEmpty(key))
            {
                SetStatus(L("ui.content.need_key"));
                return;
            }

            var b = new ContentBundle
            {
                key = key, name = _name, desc = _desc, category = Categories[_category],
                placesBlock = string.IsNullOrWhiteSpace(_placesBlock) ? null : _placesBlock.Trim(),
                toolKind = ToolKinds[_toolKind], station = Stations[_station],
                maxStack = _maxStack, tier = (int)_tier, outputCount = _outputCount, knowledgeCost = _knowledgeCost,
                miningPower = _miningPower, damage = _damage, range = _range, energy = _energy,
                consumeHealth = _consumeHealth, consumeHunger = _consumeHunger, armor = _armor, oxygen = _oxygen, scan = _scan,
                hasBlueprint = _hasBlueprint,
            };
            foreach (var a in _inputs)
                if (!string.IsNullOrWhiteSpace(a.Item) && a.Count > 0) b.inputs.Add(new AmountJson { item = a.Item.Trim(), count = a.Count });
            foreach (var a in _unlock)
                if (!string.IsNullOrWhiteSpace(a.Item) && a.Count > 0) b.unlockCost.Add(new AmountJson { item = a.Item.Trim(), count = a.Count });

            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "content_exports", key);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "content.json"), JsonUtility.ToJson(b, true));
                SetStatus($"{L("ui.content.exported")}\n{dir}");
            }
            catch (Exception e)
            {
                SetStatus("Export failed: " + e.Message);
            }
        }

        private void SetStatus(string s) { if (_status != null) _status.text = s; }

        // ── small uGUI form helpers (manual y layout) ──────────────────────────────────────────

        private static void Header(Transform p, ref float y, string text)
        {
            y += 6f;
            UiKit.AddText(p, 16f, y, 440f, 24f, text, 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 28f;
        }

        private static void InputRow(Transform p, ref float y, string label, string value, Action<string> onChange)
        {
            UiKit.AddText(p, 16f, y, 180f, 30f, label, 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(p, 200f, y, 256f, 30f, value, onChange);
            y += 38f;
        }

        private static void Stepper(Transform p, ref float y, string label, Func<float> get, Action<float> set, float min, float max, float step, string fmt)
        {
            UiKit.AddText(p, 16f, y, 200f, 30f, label, 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            var val = UiKit.AddText(p, 300f, y, 80f, 30f, get().ToString(fmt), 15, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(p, 268f, y + 1f, 28f, 28f, "−", () => { set(Mathf.Clamp(get() - step, min, max)); val.text = get().ToString(fmt); });
            UiKit.AddButton(p, 384f, y + 1f, 28f, 28f, "+", () => { set(Mathf.Clamp(get() + step, min, max)); val.text = get().ToString(fmt); });
            y += 38f;
        }

        private static void CycleRow(Transform p, ref float y, string label, string[] options, Func<int> get, Action<int> set)
        {
            UiKit.AddText(p, 16f, y, 150f, 30f, label, 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            var val = UiKit.AddText(p, 200f, y, 180f, 30f, options[get()], 15, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddButton(p, 384f, y + 1f, 28f, 28f, "→", () => { set((get() + 1) % options.Length); val.text = options[get()]; });
            y += 38f;
        }

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
