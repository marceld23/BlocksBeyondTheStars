using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Spacecraft.Shared.Definitions;

namespace Spacecraft.Client
{
    /// <summary>
    /// The redesigned Crafting / Tech / Ship screens (UX concept) — a uGUI three-pane UI built in code on
    /// a DPI-independent canvas (UiKit.CreateCanvas: ScaleWithScreenSize @1920×1080, Expand), so it looks
    /// right on a high-DPI 4K monitor and on a normal 1080p screen alike. Replaces the cramped IMGUI lists
    /// with a category sidebar, a searchable / "craftable now"-filterable card list, and a detail pane that
    /// shows ingredients (have/need, pooled from inventory + cargo), required station/blueprint, the benefit,
    /// and a clear reason when an action is blocked. Driven by <see cref="GameMenu"/>; location-bound
    /// (crafting=workshop, tech=lab, ship=ship console) with a hint when you're not at the station.
    /// </summary>
    public sealed class CraftingTechShipUI : MonoBehaviour
    {
        public GameBootstrap Game;
        public GameMenu Menu;

        public enum Mode { Crafting = 1, Tech = 2, Ship = 3 } // values match GameMenu.Tab

        private Canvas _canvas;
        private RectTransform _sidebar, _listContent, _detail, _header;
        private Text _footer, _hint;
        private Mode _mode = Mode.Crafting;
        private string _category = "all";
        private string _selected = string.Empty;
        private string _search = string.Empty;
        private bool _craftableOnly;
        private int _lastDataHash = -1;
        private bool _built;

        private const float W = 1920f, H = 1080f;

        // --- public control (from GameMenu) ---

        public void ShowMode(Mode mode)
        {
            EnsureBuilt();
            bool changed = _mode != mode || !_canvas.enabled;
            if (!changed)
            {
                return; // already showing this mode; Update() handles live refresh
            }

            _mode = mode;
            _category = "all";
            _selected = string.Empty;
            _search = string.Empty;
            _craftableOnly = false;
            _lastDataHash = -1;
            _canvas.enabled = true;
            BuildHeader();
            RebuildSidebar();
            RebuildList();
            RebuildDetail();
        }

        public void Hide()
        {
            if (_canvas != null)
            {
                _canvas.enabled = false;
            }
        }

        private void Update()
        {
            if (!_built || _canvas == null || !_canvas.enabled || Game == null)
            {
                return;
            }

            // Refresh when the authoritative inventory / unlocked set changes (cheap hash).
            int h = (Game.Personal?.Length ?? 0) * 7 + (Game.Cargo?.Length ?? 0) * 13 + Game.UnlockedBlueprints.Count * 31
                    + (Game.Personal?.Sum(s => s.Count) ?? 0) + (Game.OwnedShips?.Length ?? 0) * 101
                    + (string.IsNullOrEmpty(Game.NearbyStation) ? 0 : Game.NearbyStation.GetHashCode());
            if (h != _lastDataHash)
            {
                _lastDataHash = h;
                BuildHeader();
                RebuildList();
                RebuildDetail();
            }
        }

        // --- construction ---

        private void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas("CraftingTechShipUI");
            _canvas.sortingOrder = 50;
            var root = _canvas.transform;

            // Full-screen dim backdrop.
            UiKit.AddImage(root, 0, 0, W, H, UiKit.SolidSprite, new Color(0.02f, 0.04f, 0.08f, 0.96f));

            UiKit.AddLogo(root, 40, 14, 360, 40, "SPACECRAFT", 22);

            _header = new GameObject("Header", typeof(RectTransform)).GetComponent<RectTransform>();
            _header.SetParent(root, false);
            UiKit.Place(_header.gameObject, 0, 0, W, 132);

            // Panels.
            UiKit.AddPanel(root, 40, 150, 320, 820, UiKit.Panel);    // sidebar
            UiKit.AddPanel(root, 380, 150, 820, 820, UiKit.Panel);   // list
            UiKit.AddPanel(root, 1220, 150, 660, 820, UiKit.Panel);  // detail

            _sidebar = MakeScroll(root, 50, 162, 300, 796);
            _listContent = MakeScroll(root, 392, 220, 796, 742);
            _detail = MakeScroll(root, 1232, 162, 636, 796);

            _hint = UiKit.AddText(root, 392, 168, 796, 44, string.Empty, 20, new Color(1f, 0.8f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
            _footer = UiKit.AddText(root, 40, 980, 1840, 36, string.Empty, 20, UiKit.CyanDim, TextAnchor.MiddleLeft);

            _built = true;
        }

        /// <summary>Tab bar + (for crafting) the search box + "craftable now" toggle, rebuilt per mode.</summary>
        private void BuildHeader()
        {
            ClearChildren(_header);
            var p = _header;

            string[] tabs = { "ui.inventory.title", "ui.crafting.title", "ui.tab.tech", "ui.tab.ship", "ui.tab.map", "ui.tab.missions", "ui.settings.character", "ui.tab.space" };
            float x = 40f;
            for (int i = 0; i < tabs.Length; i++)
            {
                int tab = i; // Tab enum index
                bool active = (int)_mode == tab;
                var b = UiKit.AddButton(p, x, 64, 150, 46, L(tabs[i]), () => OnTab(tab));
                if (active)
                {
                    b.GetComponent<Image>().color = UiKit.Cyan;
                }

                x += 158f;
            }

            UiKit.AddButton(p, W - 150, 64, 110, 46, L("ui.action.close"), () => Menu?.CloseFromUi());

            // Search + craftable filter (crafting + ship lists benefit; tech uses status instead).
            if (_mode != Mode.Tech)
            {
                AddSearchBox(p, 392, 168, 470, 44);
                var t = UiKit.AddButton(p, 880, 168, 300, 44,
                    (_craftableOnly ? "[x] " : "[ ] ") + L("ui.craft.craftable_now"),
                    () => { _craftableOnly = !_craftableOnly; BuildHeader(); RebuildList(); });
                if (_craftableOnly)
                {
                    t.GetComponent<Image>().color = UiKit.Cyan;
                }
            }
        }

        private void OnTab(int tab) => Menu?.SwitchFromUi(tab); // GameMenu owns the active tab

        // --- sidebar (categories) ---

        private void RebuildSidebar()
        {
            ClearChildren(_sidebar);
            var cats = Categories();
            float y = 0f;
            foreach (var (key, label, icon) in cats)
            {
                string k = key;
                var b = UiKit.AddButton(_sidebar, 0, y, 290, 52, label, () => { _category = k; _selected = string.Empty; RebuildList(); RebuildDetail(); }, icon);
                if (_category == key)
                {
                    b.GetComponent<Image>().color = UiKit.Cyan;
                }

                y += 58f;
            }

            SetContentHeight(_sidebar, y);
        }

        private List<(string key, string label, string icon)> Categories()
        {
            var list = new List<(string, string, string)> { ("all", L("ui.craft.cat_all"), "cat_all") };
            switch (_mode)
            {
                case Mode.Crafting:
                    list.Add(("tool", L("ui.craft.cat_tools"), "cat_tools"));
                    list.Add(("weapon", L("ui.craft.cat_weapons"), "cat_weapons"));
                    list.Add(("suit", L("ui.craft.cat_suit"), "cat_suit"));
                    list.Add(("consumable", L("ui.craft.cat_consumable"), "cat_medicine"));
                    list.Add(("component", L("ui.craft.cat_components"), "cat_components"));
                    list.Add(("block", L("ui.craft.cat_blocks"), "cat_blocks"));
                    break;
                case Mode.Tech:
                    foreach (var c in Game.Content.Blueprints.Values.Select(b => b.Category).Where(c => !string.IsNullOrEmpty(c)).Distinct())
                    {
                        list.Add((c, c, "cat_tech"));
                    }

                    break;
                case Mode.Ship:
                    list.Add(("modules", L("ui.ship.cat_modules"), "cat_modules"));
                    list.Add(("fleet", L("ui.ship.cat_fleet"), "cat_fleet"));
                    list.Add(("build", L("ui.ship.cat_build"), "cat_buildship"));
                    break;
            }

            return list;
        }

        // --- middle list ---

        private void RebuildList()
        {
            if (!_built)
            {
                return;
            }

            ClearChildren(_listContent);
            bool atStation = AtStation();
            _hint.text = atStation ? string.Empty : L("ui.craft.go_to_" + StationKey());

            float y = 0f;
            switch (_mode)
            {
                case Mode.Crafting: y = BuildCraftingList(); break;
                case Mode.Tech: y = BuildTechList(); break;
                case Mode.Ship: y = BuildShipList(); break;
            }

            SetContentHeight(_listContent, y);
            _footer.text = L("ui.craft.source") + "   |   " + L("ui.craft.station_" + StationKey());
        }

        private float BuildCraftingList()
        {
            float y = 0f;
            foreach (var r in Game.Content.Recipes.Values)
            {
                var outItem = r.Outputs.FirstOrDefault();
                if (outItem == null)
                {
                    continue;
                }

                if (!MatchesCategory(outItem.Item) || !MatchesSearch(ItemName(outItem.Item)))
                {
                    continue;
                }

                bool can = CanCraft(r, out _);
                if (_craftableOnly && !can)
                {
                    continue;
                }

                AddCard(y, ItemName(outItem.Item), IconFor(outItem.Item), can ? L("ui.craft.ready") : L("ui.craft.blocked"),
                    can ? UiKit.Ok : new Color(1f, 0.5f, 0.5f), r.Key, () => { _selected = r.Key; RebuildDetail(); });
                y += 88f;
            }

            return y;
        }

        private float BuildTechList()
        {
            // A progression tree: order by tier (prerequisite depth) and indent each tier, so the unlock
            // chain reads top-to-bottom / left-to-right like a tech tree.
            var shown = Game.Content.Blueprints.Values
                .Where(bp => (_category == "all" || bp.Category == _category) && MatchesSearch(L($"blueprint.{bp.Key}.name")))
                .OrderBy(TechTier).ThenBy(bp => L($"blueprint.{bp.Key}.name"))
                .ToList();

            float y = 0f;
            int lastTier = -1;
            foreach (var bp in shown)
            {
                int tier = TechTier(bp);
                if (tier != lastTier)
                {
                    UiKit.AddText(_listContent, 0, y, 760, 24, L("ui.tech.tier") + " " + (tier + 1), 16, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
                    y += 26f;
                    lastTier = tier;
                }

                var (label, col) = TechStatus(bp);
                AddCard(y, L($"blueprint.{bp.Key}.name"), "cat_tech", label, col, bp.Key, () => { _selected = bp.Key; RebuildDetail(); }, Mathf.Min(tier, 4) * 28f);
                y += 88f;
            }

            return y;
        }

        private readonly Dictionary<string, int> _tierCache = new();

        /// <summary>Tier = longest prerequisite chain depth (0 = no prerequisites). Memoised, cycle-safe.</summary>
        private int TechTier(BlueprintDefinition bp)
        {
            if (_tierCache.TryGetValue(bp.Key, out var t))
            {
                return t;
            }

            _tierCache[bp.Key] = 0; // guard against cycles
            int max = 0;
            foreach (var pre in bp.Prerequisites)
            {
                var pd = Game.Content.GetBlueprint(pre);
                if (pd != null)
                {
                    max = Mathf.Max(max, TechTier(pd) + 1);
                }
            }

            _tierCache[bp.Key] = max;
            return max;
        }

        private float BuildShipList()
        {
            float y = 0f;
            if (_category == "all" || _category == "fleet")
            {
                foreach (var s in Game.OwnedShips)
                {
                    string key = "fleet:" + s.Id;
                    AddCard(y, L($"ship.{s.Type}.name"), "cat_fleet", s.Active ? L("ui.ships.active") : L("ui.ships.switch"),
                        s.Active ? UiKit.Cyan : UiKit.TextCol, key, () => { _selected = key; RebuildDetail(); });
                    y += 88f;
                }
            }

            if (_category == "all" || _category == "modules")
            {
                foreach (var m in Game.Content.ShipModules.Values.Where(m => !m.Mandatory))
                {
                    if (!MatchesSearch(L($"module.{m.Key}.name")))
                    {
                        continue;
                    }

                    bool can = HasAll(m.BuildCost) && BlueprintOk(m.RequiredBlueprint);
                    if (_craftableOnly && !can)
                    {
                        continue;
                    }

                    AddCard(y, L($"module.{m.Key}.name"), "cat_modules", can ? L("ui.craft.ready") : L("ui.craft.blocked"),
                        can ? UiKit.Ok : new Color(1f, 0.5f, 0.5f), "mod:" + m.Key, () => { _selected = "mod:" + m.Key; RebuildDetail(); });
                    y += 88f;
                }
            }

            if (_category == "all" || _category == "build")
            {
                foreach (var s in Game.Content.Ships.Values.Where(s => !string.IsNullOrEmpty(s.RequiredBlueprint)))
                {
                    if (!MatchesSearch(L($"ship.{s.Key}.name")))
                    {
                        continue;
                    }

                    bool can = HasAll(s.CraftCost) && BlueprintOk(s.RequiredBlueprint);
                    if (_craftableOnly && !can)
                    {
                        continue;
                    }

                    AddCard(y, L($"ship.{s.Key}.name"), "cat_buildship", can ? L("ui.craft.ready") : L("ui.craft.blocked"),
                        can ? UiKit.Ok : new Color(1f, 0.5f, 0.5f), "newship:" + s.Key, () => { _selected = "newship:" + s.Key; RebuildDetail(); });
                    y += 88f;
                }
            }

            return y;
        }

        private void AddCard(float y, string title, string icon, string status, Color statusCol, string key, System.Action onClick, float indent = 0f)
        {
            var card = UiKit.AddButton(_listContent, indent, y, 780 - indent, 78, string.Empty, onClick);
            if (_selected == key)
            {
                card.GetComponent<Image>().color = UiKit.Cyan;
            }

            float cw = 780f - indent;
            float tx = 16f;
            if (UiKit.AddIcon(card.transform, 14, 14, 50, icon) != null)
            {
                tx = 78f;
            }

            UiKit.AddText(card.transform, tx, 8, cw - tx - 16, 40, title, 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(card.transform, tx, 44, cw - tx - 16, 28, status, 18, statusCol, TextAnchor.MiddleLeft);
        }

        // --- detail pane ---

        private void RebuildDetail()
        {
            if (!_built)
            {
                return;
            }

            ClearChildren(_detail);
            if (string.IsNullOrEmpty(_selected))
            {
                UiKit.AddText(_detail, 8, 20, 620, 30, L("ui.craft.pick"), 22, UiKit.CyanDim, TextAnchor.UpperLeft);
                SetContentHeight(_detail, 60);
                return;
            }

            float y = 0f;
            switch (_mode)
            {
                case Mode.Crafting: y = DetailCrafting(); break;
                case Mode.Tech: y = DetailTech(); break;
                case Mode.Ship: y = DetailShip(); break;
            }

            SetContentHeight(_detail, y + 20f);
        }

        private float DetailCrafting()
        {
            var r = Game.Content.GetRecipe(_selected);
            if (r == null)
            {
                return 0f;
            }

            var outItem = r.Outputs.First();
            float y = 0f;
            UiKit.AddText(_detail, 8, y, 620, 40, ItemName(outItem.Item) + (outItem.Count > 1 ? $"  ×{outItem.Count}" : ""), 30, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 48f;
            string desc = Desc($"item.{outItem.Item}.desc");
            if (!string.IsNullOrEmpty(desc))
            {
                var t = UiKit.AddText(_detail, 8, y, 620, 80, desc, 20, UiKit.CyanDim, TextAnchor.UpperLeft);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                y += 84f;
            }

            UiKit.AddText(_detail, 8, y, 620, 28, L("ui.craft.needs"), 22, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 34f;
            foreach (var inp in r.Inputs)
            {
                int have = Owned(inp.Item);
                bool ok = have >= inp.Count;
                UiKit.AddText(_detail, 20, y, 620, 28, $"{(ok ? "✓" : "✗")} {ItemName(inp.Item)}  {have}/{inp.Count}", 20,
                    ok ? UiKit.Ok : new Color(1f, 0.5f, 0.5f), TextAnchor.UpperLeft);
                y += 30f;
            }

            y += 8f;
            UiKit.AddText(_detail, 8, y, 620, 26, L("ui.craft.station") + ": " + L("ui.craft.station_" + r.Station.ToString().ToLowerInvariant()), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
            y += 30f;
            if (!string.IsNullOrEmpty(r.RequiredBlueprint))
            {
                bool bp = BlueprintOk(r.RequiredBlueprint);
                UiKit.AddText(_detail, 8, y, 620, 26, $"{(bp ? "✓" : "✗")} {L("ui.craft.blueprint")}: {L($"blueprint.{r.RequiredBlueprint}.name")}", 18,
                    bp ? UiKit.Ok : new Color(1f, 0.5f, 0.5f), TextAnchor.UpperLeft);
                y += 30f;
            }

            y += 10f;
            bool can = CanCraft(r, out string reason);
            if (!can)
            {
                UiKit.AddText(_detail, 8, y, 620, 26, reason, 18, new Color(1f, 0.6f, 0.4f), TextAnchor.UpperLeft);
                y += 30f;
            }

            var btn = UiKit.AddButton(_detail, 8, y, 280, 56, L("ui.action.craft"), () => { Game.Network.SendCraft(r.Key, 1); });
            SetInteractable(btn, can);
            y += 70f;
            return y;
        }

        private float DetailTech()
        {
            var bp = Game.Content.GetBlueprint(_selected);
            if (bp == null)
            {
                return 0f;
            }

            float y = 0f;
            UiKit.AddText(_detail, 8, y, 620, 40, L($"blueprint.{bp.Key}.name"), 30, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 48f;
            string desc = Desc($"blueprint.{bp.Key}.desc");
            if (!string.IsNullOrEmpty(desc))
            {
                var t = UiKit.AddText(_detail, 8, y, 620, 80, desc, 20, UiKit.CyanDim, TextAnchor.UpperLeft);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                y += 84f;
            }

            var (status, col) = TechStatus(bp);
            UiKit.AddText(_detail, 8, y, 620, 28, L("ui.tech.status") + ": " + status, 20, col, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 36f;

            if (bp.Prerequisites.Count > 0)
            {
                UiKit.AddText(_detail, 8, y, 620, 26, L("ui.tech.prereqs"), 20, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
                y += 30f;
                foreach (var pre in bp.Prerequisites)
                {
                    bool ok = Game.UnlockedBlueprints.Contains(pre);
                    UiKit.AddText(_detail, 20, y, 620, 26, $"{(ok ? "✓" : "✗")} {L($"blueprint.{pre}.name")}", 18,
                        ok ? UiKit.Ok : new Color(1f, 0.5f, 0.5f), TextAnchor.UpperLeft);
                    y += 28f;
                }
            }

            if (bp.UnlockCost.Count > 0)
            {
                y += 6f;
                UiKit.AddText(_detail, 8, y, 620, 26, L("ui.tech.cost"), 20, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
                y += 30f;
                foreach (var c in bp.UnlockCost)
                {
                    int have = Owned(c.Item);
                    bool ok = have >= c.Count;
                    UiKit.AddText(_detail, 20, y, 620, 26, $"{(ok ? "✓" : "✗")} {ItemName(c.Item)}  {have}/{c.Count}", 18,
                        ok ? UiKit.Ok : new Color(1f, 0.5f, 0.5f), TextAnchor.UpperLeft);
                    y += 28f;
                }
            }

            y += 10f;
            bool already = Game.UnlockedBlueprints.Contains(bp.Key);
            bool can = !already && bp.Prerequisites.All(Game.UnlockedBlueprints.Contains) && HasAll(bp.UnlockCost);
            var btn = UiKit.AddButton(_detail, 8, y, 280, 56, already ? L("ui.tech.unlocked") : L("ui.action.unlock"), () => { Game.Network.SendUnlock(bp.Key); });
            SetInteractable(btn, can);
            y += 70f;
            return y;
        }

        private float DetailShip()
        {
            float y = 0f;
            if (_selected.StartsWith("fleet:"))
            {
                string id = _selected.Substring(6);
                var s = Game.OwnedShips.FirstOrDefault(o => o.Id == id);
                if (s == null)
                {
                    return 0f;
                }

                var def = Game.Content.GetShip(s.Type);
                UiKit.AddText(_detail, 8, y, 620, 40, L($"ship.{s.Type}.name"), 30, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
                y += 48f;
                y = ShipStats(def, y);
                if (!s.Active)
                {
                    UiKit.AddButton(_detail, 8, y, 280, 56, L("ui.ships.switch"), () => { Game.Network.SendSwitchShip(s.Id); });
                    y += 70f;
                }

                return y;
            }

            if (_selected.StartsWith("mod:"))
            {
                var m = Game.Content.GetShipModule(_selected.Substring(4));
                if (m == null)
                {
                    return 0f;
                }

                UiKit.AddText(_detail, 8, y, 620, 40, L($"module.{m.Key}.name"), 30, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
                y += 48f;
                string desc = Desc($"module.{m.Key}.desc");
                if (!string.IsNullOrEmpty(desc))
                {
                    var t = UiKit.AddText(_detail, 8, y, 620, 80, desc, 20, UiKit.CyanDim, TextAnchor.UpperLeft);
                    t.horizontalOverflow = HorizontalWrapMode.Wrap;
                    y += 84f;
                }

                y = CostBlock(m.BuildCost, m.RequiredBlueprint, y);
                bool can = HasAll(m.BuildCost) && BlueprintOk(m.RequiredBlueprint);
                var btn = UiKit.AddButton(_detail, 8, y, 280, 56, L("ui.action.build"), () => { Game.Network.SendBuildModule(m.Key); });
                SetInteractable(btn, can);
                y += 70f;
                return y;
            }

            if (_selected.StartsWith("newship:"))
            {
                var def = Game.Content.GetShip(_selected.Substring(8));
                if (def == null)
                {
                    return 0f;
                }

                UiKit.AddText(_detail, 8, y, 620, 40, L($"ship.{def.Key}.name"), 30, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
                y += 48f;
                y = ShipStats(def, y);
                y = CostBlock(def.CraftCost, def.RequiredBlueprint, y);
                bool can = HasAll(def.CraftCost) && BlueprintOk(def.RequiredBlueprint);
                var btn = UiKit.AddButton(_detail, 8, y, 280, 56, L("ui.action.craft"), () => { Game.Network.SendCraftShip(def.Key); });
                SetInteractable(btn, can);
                y += 70f;
            }

            return y;
        }

        private float ShipStats(ShipDefinition def, float y)
        {
            if (def == null)
            {
                return y;
            }

            UiKit.AddText(_detail, 8, y, 620, 26, $"{L("ui.ship.hull")}: {def.BaseHull:0}    {L("ui.ship.shield")}: {def.BaseShield:0}", 20, UiKit.TextCol, TextAnchor.UpperLeft);
            y += 30f;
            UiKit.AddText(_detail, 8, y, 620, 26, $"{L("ui.ship.speed")}: {def.FlightSpeed:0.0}    {L("ui.ship.handling")}: {def.Handling:0.0}    {L("ui.ship.cargo")}: {def.CargoSlots}", 20, UiKit.TextCol, TextAnchor.UpperLeft);
            y += 36f;
            return y;
        }

        private float CostBlock(IEnumerable<ItemAmount> cost, string blueprint, float y)
        {
            UiKit.AddText(_detail, 8, y, 620, 26, L("ui.craft.needs"), 20, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 32f;
            foreach (var c in cost)
            {
                int have = Owned(c.Item);
                bool ok = have >= c.Count;
                UiKit.AddText(_detail, 20, y, 620, 26, $"{(ok ? "✓" : "✗")} {ItemName(c.Item)}  {have}/{c.Count}", 18,
                    ok ? UiKit.Ok : new Color(1f, 0.5f, 0.5f), TextAnchor.UpperLeft);
                y += 28f;
            }

            if (!string.IsNullOrEmpty(blueprint))
            {
                bool bp = BlueprintOk(blueprint);
                UiKit.AddText(_detail, 8, y, 620, 26, $"{(bp ? "✓" : "✗")} {L("ui.craft.blueprint")}: {L($"blueprint.{blueprint}.name")}", 18,
                    bp ? UiKit.Ok : new Color(1f, 0.5f, 0.5f), TextAnchor.UpperLeft);
                y += 30f;
            }

            return y + 8f;
        }

        // --- logic helpers ---

        private (string, Color) TechStatus(BlueprintDefinition bp)
        {
            if (Game.UnlockedBlueprints.Contains(bp.Key))
            {
                return (L("ui.tech.unlocked"), UiKit.Ok);
            }

            if (!bp.Prerequisites.All(Game.UnlockedBlueprints.Contains))
            {
                return (L("ui.tech.locked"), new Color(0.6f, 0.6f, 0.7f));
            }

            if (!HasAll(bp.UnlockCost))
            {
                return (L("ui.tech.materials_missing"), new Color(1f, 0.7f, 0.3f));
            }

            return (L("ui.tech.unlockable"), UiKit.Cyan);
        }

        private bool CanCraft(RecipeDefinition r, out string reason)
        {
            if (!BlueprintOk(r.RequiredBlueprint))
            {
                reason = L("ui.craft.need_blueprint");
                return false;
            }

            if (!HasAll(r.Inputs))
            {
                reason = L("ui.craft.need_materials");
                return false;
            }

            if (!AtStation())
            {
                reason = L("ui.craft.go_to_" + StationKey());
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private bool BlueprintOk(string bp) => string.IsNullOrEmpty(bp) || Game.UnlockedBlueprints.Contains(bp);
        private bool HasAll(IEnumerable<ItemAmount> cost) => cost.All(c => Owned(c.Item) >= c.Count);

        private int Owned(string item)
        {
            int n = 0;
            if (Game.Personal != null)
            {
                foreach (var s in Game.Personal) if (s.Item == item) n += s.Count;
            }

            if (Game.Cargo != null)
            {
                foreach (var s in Game.Cargo) if (s.Item == item) n += s.Count;
            }

            return n;
        }

        private bool MatchesCategory(string item)
        {
            if (_category == "all")
            {
                return true;
            }

            var def = Game.Content.GetItem(item);
            if (def == null)
            {
                return false;
            }

            return _category switch
            {
                "weapon" => def.Tool?.Kind == ToolKind.Weapon,
                "tool" => def.Category == ItemCategory.Tool && def.Tool?.Kind != ToolKind.Weapon,
                "suit" => def.ArmorResistance > 0f || def.OxygenBonus > 0f,
                "consumable" => def.Category == ItemCategory.Consumable,
                "component" => def.Category == ItemCategory.Component || def.Category == ItemCategory.Material,
                "block" => def.Category == ItemCategory.Block || !string.IsNullOrEmpty(def.PlacesBlock),
                _ => true,
            };
        }

        private bool MatchesSearch(string label)
            => string.IsNullOrEmpty(_search) || label.ToLowerInvariant().Contains(_search.ToLowerInvariant());

        private string StationKey() => _mode switch { Mode.Tech => "lab", Mode.Ship => "console", _ => "workshop" };

        private bool AtStation()
        {
            string at = Game.NearbyStation ?? string.Empty;
            // The lab doubles as a research bench at the workshop; the ship console doubles at the cockpit
            // — so designed ships without a dedicated lab/console tile still work.
            return _mode switch
            {
                Mode.Tech => at == "lab" || at == "workshop",
                Mode.Ship => at == "console" || at == "cockpit",
                _ => at == "workshop", // crafting
            };
        }

        private string IconFor(string item)
        {
            var def = Game.Content.GetItem(item);
            if (def == null)
            {
                return null;
            }

            if (def.Tool?.Kind == ToolKind.Weapon) return "cat_weapons";
            if (def.Category == ItemCategory.Tool) return "cat_tools";
            if (def.Category == ItemCategory.Consumable) return "cat_medicine";
            if (def.ArmorResistance > 0f || def.OxygenBonus > 0f) return "cat_suit";
            if (def.Category == ItemCategory.Block || !string.IsNullOrEmpty(def.PlacesBlock)) return "cat_blocks";
            return "cat_components";
        }

        // --- uGUI scaffolding ---

        private static RectTransform MakeScroll(Transform parent, float x, float y, float w, float h)
        {
            var viewGo = new GameObject("Scroll", typeof(RectTransform));
            viewGo.transform.SetParent(parent, false);
            UiKit.Place(viewGo, x, y, w, h);
            var sr = viewGo.AddComponent<ScrollRect>();
            sr.horizontal = false;
            var mask = viewGo.AddComponent<RectMask2D>();

            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewGo.transform, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.sizeDelta = new Vector2(w, h);
            content.anchoredPosition = Vector2.zero;
            sr.content = content;
            sr.viewport = viewGo.GetComponent<RectTransform>();
            sr.scrollSensitivity = 30f;
            return content;
        }

        private static void SetContentHeight(RectTransform content, float h)
        {
            content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(h, content.GetComponent<RectTransform>().rect.height));
        }

        private void AddSearchBox(Transform parent, float x, float y, float w, float h)
        {
            var go = new GameObject("Search", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, x, y, w, h);
            var img = go.AddComponent<Image>();
            img.sprite = UiKit.ButtonSprite;
            img.type = Image.Type.Sliced;
            img.color = new Color(0.05f, 0.12f, 0.24f, 0.95f);

            var input = go.AddComponent<InputField>();
            var text = UiKit.AddText(go.transform, 14, 0, w - 24, h, _search, 22, UiKit.TextCol, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            var ph = UiKit.AddText(go.transform, 14, 0, w - 24, h, L("ui.craft.search"), 22, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Italic);
            input.textComponent = text;
            input.placeholder = ph;
            input.text = _search;
            input.onValueChanged.AddListener(s => { _search = s; RebuildList(); });
        }

        private static void SetInteractable(Button b, bool on)
        {
            b.interactable = on;
            var img = b.GetComponent<Image>();
            if (!on)
            {
                img.color = new Color(0.3f, 0.34f, 0.4f, 0.8f);
            }
        }

        private static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                Destroy(t.GetChild(i).gameObject);
            }
        }

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;
        private string ItemName(string item) => L($"item.{item}.name");
        private string Desc(string key)
        {
            string s = L(key);
            return s == key ? string.Empty : s;
        }
    }
}
