using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
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

        // Values match GameMenu.Tab so the whole in-game menu runs on this one uGUI screen. (Launching into
        // space lives on the Map tab now — there is no separate Space tab.)
        public enum Mode { Inventory = 0, Crafting = 1, Tech = 2, Ship = 3, Map = 4, Missions = 5, Character = 6 }

        // Quick-bar = the first N personal-inventory slots (must match the server's HotbarSlots / HudUi Slots).
        private const int QuickSlots = 9;

        private Canvas _canvas;
        private RectTransform _sidebar, _listContent, _detail, _header;
        private Text _footer, _hint, _feedback;
        private Mode _mode = Mode.Crafting;
        private string _category = "all";
        private string _selected = string.Empty;
        private string _search = string.Empty;

        // Celebration state (craft/unlock juice): the card with this content key pulses until the
        // deadline and a floating label announces the result. Fed by CraftCompleted + the
        // unlocked-blueprints diff in Update.
        private string _celebrateKey;
        private float _celebrateUntil;
        private System.Collections.Generic.HashSet<string> _knownBlueprints;
        private bool _craftHooked;
        private bool _craftableOnly;
        private int _lastDataHash = -1;
        private bool _built;
        // The page (mode+category / selection) last rendered into each scroll view. A rebuild that changes
        // the page scrolls back to the top; an in-place refresh (live data, volume/colour cycling) keeps the
        // player's scroll position. Without this, switching from a scrolled-down page (e.g. Settings) to a
        // short one (e.g. Space) leaves it scrolled past the top, hiding the first rows (the launch button).
        private string _listPage, _detailPage;
        private AvatarPreviewRig _avatarPreview; // live faced-avatar preview for the colour menu (B25)
        private ShipPreviewRig _shipPreview; // live ship preview for the Ship paint tab (item 32)

        // Player-created mission form state (item 31, Missions tab "create" category).
        private static readonly string[] PmTypes = { "Mine", "Collect", "Deliver" };
        private static readonly string[] PmTargets = { "iron_ore", "copper_ore", "titanium_ore", "crystal", "carbon", "silicate", "stone", "ice" };
        private static readonly string[] PmRewards = { "iron_ore", "copper_ore", "titanium_ore", "crystal", "carbon", "plant_fiber", "berries", "iron_plate" };
        private string _pmTitle = string.Empty, _pmDesc = string.Empty;
        private int _pmType, _pmTarget, _pmCount = 5, _pmRewardItem = 3, _pmRewardCount = 1;
        private readonly System.Collections.Generic.List<NetMissionObjective> _pmObjectives = new();

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
            _avatarPreview?.SetActive(mode == Mode.Character); // only render the live preview on the colour tab
            _shipPreview?.SetActive(false); // re-enabled by the paint detail pane when that category is shown
            _category = string.IsNullOrEmpty(_pendingCategory) ? "all" : _pendingCategory;
            _pendingCategory = null;
            _selected = string.Empty;
            _search = string.Empty;
            _craftableOnly = false;
            _lastDataHash = -1;
            _listPage = _detailPage = null; // a fresh open / tab switch always scrolls both panes to the top
            _canvas.enabled = true;
            BuildHeader();
            RebuildSidebar();
            RebuildList();
            RebuildDetail();
            UiKit.TransitionIn(_canvas.gameObject); // fade-in on open + tab change
        }

        private string _pendingCategory; // a category to select when the mode next opens (e.g. "market")
        private int _craftCount = 1;          // how many of the selected recipe to craft at once
        private string _craftCountKey = string.Empty; // recipe the count belongs to (reset on a new selection)

        /// <summary>Requests a category be selected when this panel opens (used to jump straight to the
        /// market when the player talks to a vendor).</summary>
        public void RequestCategory(string category)
        {
            _pendingCategory = category;
            if (_canvas != null && _canvas.enabled && !string.IsNullOrEmpty(category))
            {
                _category = category;
                _pendingCategory = null;
                _selected = string.Empty;
                RebuildSidebar();
                RebuildList();
                RebuildDetail();
            }
        }

        public void Hide()
        {
            if (_canvas != null)
            {
                _canvas.enabled = false;
            }

            _avatarPreview?.SetActive(false); // stop rendering the preview camera while the menu is closed
            _shipPreview?.SetActive(false);
        }

        private void Update()
        {
            if (!_built || _canvas == null || !_canvas.enabled || Game == null)
            {
                return;
            }

            if (!_craftHooked && Game.Network != null)
            {
                Game.Network.CraftCompleted += OnCraftResult;
                _craftHooked = true;
            }

            DetectBlueprintUnlocks();

            // Item↔slot signature so a pure quick-bar swap (count + length unchanged) still triggers a rebuild
            // (B58). Manual unchecked loop — LINQ Sum on large hash products would overflow.
            int slotSig = 0;
            if (Game.Personal != null)
            {
                foreach (var s in Game.Personal)
                {
                    unchecked { slotSig = slotSig * 31 + s.Slot * 92821 + (s.Item?.GetHashCode() ?? 0); }
                }
            }

            // Refresh when the authoritative data the screen shows changes (cheap hash).
            int h = (Game.Personal?.Length ?? 0) * 7 + (Game.Cargo?.Length ?? 0) * 13 + Game.UnlockedBlueprints.Count * 31
                    + (Game.Personal?.Sum(s => s.Count) ?? 0) + (Game.OwnedShips?.Length ?? 0) * 101 + slotSig
                    + (string.IsNullOrEmpty(Game.NearbyStation) ? 0 : Game.NearbyStation.GetHashCode())
                    + (Game.StarMap?.Systems.Length ?? 0) * 211 + (Game.StarMap?.ActiveLocationId?.GetHashCode() ?? 0)
                    + (Game.Missions?.Available.Length ?? 0) * 307 + (Game.Missions?.Active.Length ?? 0) * 401
                    + (Game.Space?.Entities.Length ?? 0) * 503 + (Game.InSpace ? 7777 : 0)
                    + (Game.LastMessage?.GetHashCode() ?? 0);
            if (h != _lastDataHash)
            {
                _lastDataHash = h;
                BuildHeader();
                RebuildSidebar(); // map systems / sections can arrive async
                RebuildList();
                RebuildDetail();
            }
        }

        /// <summary>Craft success → the crafted item's card pulses + a "+ item" label floats up (the
        /// failure path already reads via the feedback line + error tone).</summary>
        private void OnCraftResult(BlocksBeyondTheStars.Networking.Messages.CraftResult m)
        {
            if (!m.Success || Game?.Content == null
                || !Game.Content.Recipes.TryGetValue(m.RecipeKey ?? string.Empty, out var recipe))
            {
                return;
            }

            var output = recipe.Outputs.FirstOrDefault();
            if (output == null)
            {
                return;
            }

            _celebrateKey = output.Item;
            _celebrateUntil = Time.unscaledTime + 2.5f;
            SpawnFloatLabel("+ " + ItemName(output.Item), UiKit.Ok);
            _lastDataHash = 0; // force a rebuild so the pulsing card attaches
        }

        /// <summary>Client-side blueprint-unlock detection: the server confirms an unlock only via the
        /// inventory snapshot (no dedicated message), so diff the unlocked set. The first observation
        /// just baselines, and a multi-key jump is a join/admin snapshot — neither fires the fanfare.</summary>
        private void DetectBlueprintUnlocks()
        {
            var cur = Game.UnlockedBlueprints;
            if (cur == null)
            {
                return;
            }

            if (_knownBlueprints == null)
            {
                _knownBlueprints = new System.Collections.Generic.HashSet<string>(cur);
                return;
            }

            string fresh = null;
            int freshCount = 0;
            foreach (var key in cur)
            {
                if (!_knownBlueprints.Contains(key))
                {
                    fresh = key;
                    freshCount++;
                }
            }

            if (freshCount == 0)
            {
                return;
            }

            _knownBlueprints = new System.Collections.Generic.HashSet<string>(cur);
            if (freshCount > 1)
            {
                return;
            }

            _celebrateKey = fresh;
            _celebrateUntil = Time.unscaledTime + 3f;
            ClientAudio.Instance?.Cue("tech_unlock", 0.8f);
            SpawnFloatLabel(L($"blueprint.{fresh}.name") + " — " + L("ui.tech.unlocked"), UiKit.Cyan);
            _lastDataHash = 0; // rebuild so the unlocked node pulses + statuses refresh
        }

        /// <summary>A celebration label that rises from the centre of the menu and fades out.</summary>
        private void SpawnFloatLabel(string text, Color color)
        {
            if (_canvas == null)
            {
                return;
            }

            var t = UiKit.AddText(_canvas.transform, W * 0.5f - 400f, H * 0.42f, 800f, 44f, text, 30,
                color, TextAnchor.MiddleCenter, FontStyle.Bold);
            t.raycastTarget = false;
            t.gameObject.AddComponent<FloatLabel>();
        }

        private sealed class FloatLabel : MonoBehaviour
        {
            private const float Life = 1.6f;
            private float _t;
            private Text _text;

            private void Awake() => _text = GetComponent<Text>();

            private void Update()
            {
                _t += Time.unscaledDeltaTime;
                transform.localPosition += Vector3.up * (Time.unscaledDeltaTime * 46f);
                if (_text != null)
                {
                    var c = _text.color;
                    c.a = Mathf.Clamp01(1f - _t / Life);
                    _text.color = c;
                }

                if (_t >= Life)
                {
                    Destroy(gameObject);
                }
            }
        }

        /// <summary>Pulses a card's background toward a celebratory green-cyan until the deadline.</summary>
        private sealed class CardPulse : MonoBehaviour
        {
            public float Until;

            private Image _img;
            private Color _base;

            private void Awake()
            {
                _img = GetComponent<Image>();
                if (_img != null)
                {
                    _base = _img.color;
                }
            }

            private void Update()
            {
                if (_img == null || Time.unscaledTime >= Until)
                {
                    if (_img != null)
                    {
                        _img.color = _base;
                    }

                    Destroy(this);
                    return;
                }

                float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f);
                _img.color = Color.Lerp(_base, new Color(0.30f, 0.95f, 0.75f, 0.95f), k * 0.6f);
            }
        }

        // --- construction ---

        private void OnDestroy()
        {
            if (_craftHooked && Game?.Network != null)
            {
                Game.Network.CraftCompleted -= OnCraftResult;
            }

            // Top-level canvas — destroy it with the component so the menu doesn't linger after teardown.
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }

        private void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas("CraftingTechShipUI");
            _canvas.sortingOrder = 50;
            var root = _canvas.transform;

            // Full-screen dim backdrop — translucent so the world/HUD shows through (holographic-overlay look,
            // matching the diegetic HUD) rather than a solid modal; still dark enough to keep panels readable.
            UiKit.AddImage(root, 0, 0, W, H, UiKit.SolidSprite, new Color(0.02f, 0.04f, 0.08f, 0.6f));

            // The in-game menu is framed as the ship's computer, so it carries its own heading
            // ("Ship Interface", localized) instead of the game title.
            UiKit.AddLogo(root, 40, 14, 360, 40, L("ui.shipmenu.title"), 22);

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
            // Server feedback (craft/unlock/build result) — shown here since the HUD toast is hidden while a menu is open.
            _feedback = UiKit.AddText(root, 40, 1018, 1840, 30, string.Empty, 22, UiKit.Ok, TextAnchor.MiddleCenter, FontStyle.Bold);

            // Top-most "visor glass" overlay: the HUD's helmet look (cyan rim glow + faint scanlines), no
            // curvature, click-through — so the menu reads as inside the helmet without displacing its buttons.
            VisorMenuGlass.Add(root);

            _built = true;
        }

        /// <summary>Tab bar + (for crafting) the search box + "craftable now" toggle, rebuilt per mode.</summary>
        private void BuildHeader()
        {
            ClearChildren(_header);
            var p = _header;

            // Launching into space now lives at the top of the Map tab (the travel hub), so there is no
            // separate Space tab — its combat status is on the HUD and firing is done in the flight view.
            string[] tabs = { "ui.inventory.title", "ui.crafting.title", "ui.tab.tech", "ui.tab.ship", "ui.tab.map", "ui.tab.missions", "ui.tab.settings" };
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

                // Auto-fit the label so a long localized tab (e.g. German "Einstellungen") shrinks to fit the
                // fixed-width button instead of spilling over its graphic (B28); short labels keep full size.
                var lbl = b.GetComponentInChildren<Text>();
                if (lbl != null)
                {
                    lbl.resizeTextForBestFit = true;
                    lbl.resizeTextMaxSize = 22;
                    lbl.resizeTextMinSize = 12;
                }

                x += 158f;
            }

            // Always-available browser screens (separate full-screen overlays): the Codex (wiki) + the Arcade.
            UiKit.AddButton(p, x, 64, 140, 46, L("ui.tab.wiki"), () => Menu?.OpenWiki());
            UiKit.AddButton(p, x + 148f, 64, 140, 46, L("ui.tab.arcade"), () => Menu?.OpenArcade());

            UiKit.AddButton(p, W - 150, 64, 110, 46, L("ui.action.close"), () => Menu?.CloseFromUi());

            // Search + craftable filter (crafting + ship lists benefit; other modes don't need it).
            if (_mode == Mode.Crafting || _mode == Mode.Ship)
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
                // A "head:" entry is a non-selectable section heading (e.g. the travel screen's
                // "Current system" / "Hyperspace"). Auto-fit so a long localized heading stays inside the column.
                if (key.StartsWith("head:", System.StringComparison.Ordinal))
                {
                    if (y > 0f)
                    {
                        y += 10f; // a little air above a new section
                    }

                    var h = UiKit.AddText(_sidebar, 10, y, 270, 30, label, 17, UiKit.Cyan, TextAnchor.LowerLeft, FontStyle.Bold);
                    h.resizeTextForBestFit = true;
                    h.resizeTextMaxSize = 17;
                    h.resizeTextMinSize = 11;
                    y += 36f;
                    continue;
                }

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

        /// <summary>The id of the star system the player is currently in (contains the active location).</summary>
        private string CurrentSystemId()
        {
            var map = Game.StarMap;
            if (map?.Systems == null)
            {
                return string.Empty;
            }

            foreach (var sys in map.Systems)
            {
                if (sys.Bodies.Any(b => b.Id == map.ActiveLocationId))
                {
                    return sys.Id;
                }
            }

            return map.Systems.Length > 0 ? map.Systems[0].Id : string.Empty;
        }

        /// <summary>The system selected in the travel sidebar — the one keyed by <see cref="_category"/>,
        /// defaulting to the player's current system.</summary>
        private NetStarSystem SelectedSystem()
        {
            var map = Game.StarMap;
            if (map?.Systems == null || map.Systems.Length == 0)
            {
                return null;
            }

            var byCat = map.Systems.FirstOrDefault(s => "sys:" + s.Name == _category);
            if (byCat != null)
            {
                return byCat;
            }

            string curId = CurrentSystemId();
            return map.Systems.FirstOrDefault(s => s.Id == curId) ?? map.Systems[0];
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
                    list.Add(("market", L("ui.craft.cat_market"), "cat_cargo"));
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
                    list.Add(("paint", L("ui.ship.cat_paint"), "cat_suit"));
                    break;
                case Mode.Inventory:
                    list.Clear();
                    list.Add(("personal", L("ui.inventory.title"), "cat_inventory"));
                    list.Add(("cargo", L("ui.cargo.title"), "cat_cargo"));
                    break;
                case Mode.Missions:
                    list.Clear();
                    list.Add(("available", L("ui.missions.available"), "cat_mission"));
                    list.Add(("active", L("ui.missions.active"), "cat_tech"));
                    list.Add(("create", L("ui.missions.create"), "cat_buildship"));
                    break;
                case Mode.Map:
                    list.Clear();
                    if (Game.StarMap != null && Game.StarMap.Systems.Length > 0)
                    {
                        // Default the selection to the current system if nothing valid is chosen yet, so the
                        // travel screen opens on the reachable in-system targets (and the sidebar highlights it).
                        if (!Game.StarMap.Systems.Any(s => "sys:" + s.Name == _category))
                        {
                            _category = "sys:" + (SelectedSystem()?.Name ?? Game.StarMap.Systems[0].Name);
                        }

                        string curId = CurrentSystemId();
                        var current = Game.StarMap.Systems.FirstOrDefault(s => s.Id == curId);
                        var distant = Game.StarMap.Systems.Where(s => s.Id != curId).ToList();

                        // Current system first, under its own heading — only the in-system targets, no jump.
                        list.Add(("head:current", L("ui.map.current_system"), string.Empty));
                        if (current != null)
                        {
                            list.Add(("sys:" + current.Name, "★ " + current.Name, "cat_planet"));
                        }

                        // Distant systems under a Hyperspace heading. Unknown ones read as a single
                        // "unexplored" entry (their bodies stay hidden until you hyperjump there).
                        if (distant.Count > 0)
                        {
                            list.Add(("head:hyper", L("ui.map.hyperspace"), string.Empty));
                            foreach (var sys in distant)
                            {
                                string label = Game.KnowsSystem(sys.Id)
                                    ? "★ " + sys.Name
                                    : sys.Name + "  ·  " + L("ui.map.unexplored");
                                list.Add(("sys:" + sys.Name, label, "cat_planet"));
                            }
                        }
                    }

                    break;
                case Mode.Character:
                    list.Clear();
                    list.Add(("appearance", L("ui.settings.character"), "cat_suit"));
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
            bool production = _mode == Mode.Crafting || _mode == Mode.Tech || _mode == Mode.Ship;
            _hint.text = (production && !AtStation()) ? L("ui.craft.go_to_" + StationKey()) : string.Empty;

            float y = 0f;
            switch (_mode)
            {
                case Mode.Crafting: y = BuildCraftingList(); break;
                case Mode.Tech: y = BuildTechList(); break;
                case Mode.Ship: y = BuildShipList(); break;
                case Mode.Inventory: y = BuildInventoryList(); break;
                case Mode.Map: y = BuildMapList(); break;
                case Mode.Missions: y = BuildMissionsList(); break;
                case Mode.Character: y = BuildCharacterList(); break;
            }

            SetContentHeight(_listContent, y);
            string listPage = _mode + "|" + _category;
            if (listPage != _listPage)
            {
                _listPage = listPage;
                ScrollToTop(_listContent); // a new page starts at the top, not wherever the last one was scrolled
            }

            _footer.text = production ? L("ui.craft.source") + "   |   " + L("ui.craft.station_" + StationKey()) : string.Empty;
            if (_feedback != null)
            {
                _feedback.text = Game.LastMessage ?? string.Empty;
            }
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

                // Market (barter) recipes live only under the "market" category; everything else hides them.
                bool isMarket = r.Station == BlocksBeyondTheStars.Shared.Definitions.CraftingStation.Market;
                if (_category == "market")
                {
                    if (!isMarket || !MatchesSearch(ItemName(outItem.Item)))
                    {
                        continue;
                    }
                }
                else if (isMarket || !MatchesCategory(outItem.Item) || !MatchesSearch(ItemName(outItem.Item)))
                {
                    continue;
                }

                bool can = CanCraft(r, out _);
                if (_craftableOnly && !can)
                {
                    continue;
                }

                AddCard(y, ItemName(outItem.Item), IconFor(outItem.Item), can ? L("ui.craft.ready") : L("ui.craft.blocked"),
                    can ? UiKit.Ok : new Color(1f, 0.5f, 0.5f), r.Key, () => { _selected = r.Key; RebuildDetail(); }, contentKey: outItem.Item);
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
                AddCard(y, L($"blueprint.{bp.Key}.name"), "cat_tech", label, col, bp.Key, () => { _selected = bp.Key; RebuildDetail(); }, Mathf.Min(tier, 4) * 28f, contentKey: bp.Key);
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
            if (_category == "paint")
            {
                return BuildHullPaintList();
            }

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
                        can ? UiKit.Ok : new Color(1f, 0.5f, 0.5f), "mod:" + m.Key, () => { _selected = "mod:" + m.Key; RebuildDetail(); }, contentKey: m.Key);
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

        private float BuildInventoryList()
        {
            var items = _category == "cargo" ? Game.Cargo : Game.Personal;
            float y = 0f;
            if (items == null || items.Length == 0)
            {
                UiKit.AddText(_listContent, 8, 8, 700, 30, "—", 22, UiKit.CyanDim, TextAnchor.UpperLeft);
                return 40f;
            }

            foreach (var s in items)
            {
                AddCard(y, ItemName(s.Item), IconFor(s.Item), "×" + s.Count, UiKit.CyanDim, "inv:" + s.Item, () => { _selected = "inv:" + s.Item; RebuildDetail(); }, contentKey: s.Item);
                y += 88f;
            }

            return y;
        }

        private float BuildMapList()
        {
            var map = Game.StarMap;
            if (map == null || map.Systems.Length == 0)
            {
                UiKit.AddText(_listContent, 8, 0, 700, 30, L("ui.map.loading"), 22, UiKit.CyanDim, TextAnchor.UpperLeft);
                return 40f;
            }

            var sys = SelectedSystem();
            if (sys == null)
            {
                return 0f;
            }

            bool isCurrent = sys.Id == CurrentSystemId();
            float y = 0f;

            // The flight context action (launch into space / take helm / leave space) lives in the CURRENT
            // system view — that's where you enter/leave flight from.
            if (isCurrent)
            {
                y = BuildFlightAction();
            }

            // A distant system you've NEVER entered hides its bodies — it's a single "hyperjump here" target.
            if (!isCurrent && !Game.KnowsSystem(sys.Id))
            {
                UiKit.AddText(_listContent, 8, y, 760, 56, L("ui.map.system_unexplored"), 19, UiKit.CyanDim, TextAnchor.UpperLeft);
                y += 64f;
                var jump = UiKit.AddButton(_listContent, 0, y, 760, 60, L("ui.map.hyperjump_here"), () => Game.Network?.SendHyperjumpSystem(sys.Id));
                jump.GetComponent<Image>().color = new Color(0.30f, 0.18f, 0.46f); // hyperspace-violet accent
                y += 76f;
                return y;
            }

            // The selected system's bodies (reachable targets).
            foreach (var b in sys.Bodies)
            {
                bool here = b.Id == map.ActiveLocationId;
                string status = here ? L("ui.map.here") : $"{b.Kind}  {b.Status}";

                // Show the party: which players are currently on this body.
                if (map.Players != null)
                {
                    var names = map.Players.Where(p => p.LocationId == b.Id).Select(p => p.Name).ToList();
                    if (names.Count > 0)
                    {
                        status += "   ◈ " + string.Join(", ", names);
                    }
                }

                // Fixed landing pads (item 38): show free/total, or a FULL warning when every pad is taken.
                if (b.PadsTotal > 0)
                {
                    status += b.PadsFree == 0
                        ? "   ⊕ " + L("ui.map.pads_full")
                        : $"   ⊕ {b.PadsFree}/{b.PadsTotal}";
                }

                // Locked: a landable world you haven't reached yet (Instant Travel off + never landed there) —
                // quick-travel is unavailable until you fly there and land manually.
                if (!here && !string.IsNullOrEmpty(b.PlanetType) && !TravelUnlocked(b))
                {
                    status += "   · " + L("ui.map.fly_to_unlock");
                }

                AddCard(y, b.Name, "cat_planet", status, here ? UiKit.Cyan : UiKit.CyanDim,
                    "body:" + b.Id, () => { _selected = "body:" + b.Id; RebuildDetail(); });
                y += 88f;
            }

            return y;
        }

        /// <summary>True when the travel screen may quick-travel to this body: Instant Travel is on, or the
        /// player has already landed there (the current body is always reachable).</summary>
        private bool TravelUnlocked(NetBody b)
            => Game.InstantTravel || b.Id == Game.StarMap?.ActiveLocationId || Game.HasLandedOn(b.Id);

        private float BuildMissionsList()
        {
            if (_category == "create")
            {
                return BuildMissionForm();
            }

            var list = Game.Missions;
            if (list == null)
            {
                UiKit.AddText(_listContent, 8, 8, 700, 30, L("ui.map.loading"), 22, UiKit.CyanDim, TextAnchor.UpperLeft);
                return 40f;
            }

            var missions = _category == "active" ? list.Active : list.Available;
            float y = 0f;
            foreach (var m in missions)
            {
                string status = m.Objectives.Length > 0 ? $"{m.Objectives[0].Progress}/{m.Objectives[0].Required}" : string.Empty;
                AddCard(y, MissionText(m), "cat_mission", status, UiKit.CyanDim, "mis:" + m.Id, () => { _selected = "mis:" + m.Id; RebuildDetail(); });
                y += 88f;
            }

            return y;
        }

        /// <summary>The "post a mission" form (item 31): title + description, an objectives builder (type / target /
        /// count, add multiple), and a staked reward. Posting sends a <c>CreateMissionIntent</c>; the server
        /// escrows the stake, and when someone else completes it the poster gets a multiple of the stake back.</summary>
        private float BuildMissionForm()
        {
            var c = _listContent;
            const float W = 780f;
            float y = 0f;

            UiKit.AddText(c, 0, y, W, 24, L("ui.missions.create_hint"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 30f;
            UiKit.AddInput(c, 0, y, W, 42, _pmTitle, v => _pmTitle = v, L("ui.missions.title_ph"));
            y += 50f;
            UiKit.AddInput(c, 0, y, W, 42, _pmDesc, v => _pmDesc = v, L("ui.missions.desc_ph"));
            y += 58f;

            UiKit.AddText(c, 0, y, W, 22, L("ui.missions.objectives"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 28f;
            for (int i = 0; i < _pmObjectives.Count; i++)
            {
                int idx = i;
                var o = _pmObjectives[i];
                UiKit.AddText(c, 12, y, 600, 32, $"{o.Type}  {o.Required}× {ItemName(o.Target)}", 18, UiKit.TextCol, TextAnchor.MiddleLeft);
                UiKit.AddButton(c, 700, y, 60, 32, "✕", () => { _pmObjectives.RemoveAt(idx); RebuildList(); });
                y += 38f;
            }

            // Builder row: type / target / count / add.
            UiKit.AddButton(c, 0, y, 150, 38, PmTypes[_pmType], () => { _pmType = (_pmType + 1) % PmTypes.Length; RebuildList(); });
            UiKit.AddButton(c, 158, y, 210, 38, ItemName(PmTargets[_pmTarget]), () => { _pmTarget = (_pmTarget + 1) % PmTargets.Length; RebuildList(); });
            UiKit.AddButton(c, 376, y, 44, 38, "−", () => { _pmCount = Mathf.Max(1, _pmCount - 1); RebuildList(); });
            UiKit.AddText(c, 422, y, 54, 38, _pmCount.ToString(), 20, UiKit.TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(c, 478, y, 44, 38, "+", () => { _pmCount++; RebuildList(); });
            UiKit.AddButton(c, 560, y, 200, 38, L("ui.missions.add_objective"), () =>
            {
                _pmObjectives.Add(new NetMissionObjective { Type = PmTypes[_pmType], Target = PmTargets[_pmTarget], Required = _pmCount });
                RebuildList();
            });
            y += 56f;

            UiKit.AddText(c, 0, y, W, 22, L("ui.missions.stake"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 28f;
            UiKit.AddButton(c, 0, y, 210, 38, ItemName(PmRewards[_pmRewardItem]), () => { _pmRewardItem = (_pmRewardItem + 1) % PmRewards.Length; RebuildList(); });
            UiKit.AddButton(c, 218, y, 44, 38, "−", () => { _pmRewardCount = Mathf.Max(1, _pmRewardCount - 1); RebuildList(); });
            UiKit.AddText(c, 264, y, 54, 38, _pmRewardCount.ToString(), 20, UiKit.TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(c, 320, y, 44, 38, "+", () => { _pmRewardCount++; RebuildList(); });
            UiKit.AddText(c, 380, y, 380, 38, $"(×{Owned(PmRewards[_pmRewardItem])})", 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 58f;

            var post = UiKit.AddButton(c, 0, y, 320, 50, L("ui.missions.post"), PostMission);
            post.GetComponent<Image>().color = new Color(0.2f, 0.5f, 0.36f);
            y += 60f;
            return y;
        }

        private void PostMission()
        {
            if (string.IsNullOrWhiteSpace(_pmTitle) || _pmObjectives.Count == 0)
            {
                if (_feedback != null) _feedback.text = L("ui.missions.need_fields");
                return;
            }

            var rewards = new[] { new NetReward { Item = PmRewards[_pmRewardItem], Count = _pmRewardCount } };
            Game.Network?.SendCreateMission(_pmTitle, _pmDesc, _pmObjectives.ToArray(), rewards);
            _pmObjectives.Clear();
            _pmTitle = string.Empty;
            _pmDesc = string.Empty;
            _category = "available"; // jump to the board so the poster sees it appear
            Game.Network?.SendRequestMissions();
            RebuildList();
            RebuildSidebar();
        }

        private float BuildCharacterList()
        {
            float y = 0f;
            string[] labels = { L("ui.settings.skin"), L("ui.settings.torso"), L("ui.settings.arms"), L("ui.settings.legs") };
            Color[] cols = Menu != null && Menu.Settings != null
                ? new[] { Menu.Settings.SkinColor, Menu.Settings.TorsoColor, Menu.Settings.ArmColor, Menu.Settings.LegColor }
                : new[] { Color.gray, Color.gray, Color.gray, Color.gray };

            // Recolour the live preview as the player cycles a part (this list rebuilds on every cycle).
            if (_avatarPreview != null && Menu?.Settings != null)
            {
                _avatarPreview.SetColors(cols[0], cols[1], cols[2], cols[3]);
            }

            for (int i = 0; i < 4; i++)
            {
                int which = i;
                var card = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () => { Menu?.CycleAppearance(which); RebuildList(); });
                UiKit.AddText(card.transform, 16, 0, 360, 78, labels[i], 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.AddImage(card.transform, 420, 19, 120, 40, UiKit.SolidSprite, cols[i]);
                UiKit.AddText(card.transform, 560, 0, 200, 78, L("ui.settings.next_color"), 18, UiKit.Cyan, TextAnchor.MiddleLeft);
                y += 88f;
            }

            // Master volume — − / + adjust the audio bus live (and persist).
            int pct = Mathf.RoundToInt((Menu?.Settings?.MasterVolume ?? 0.8f) * 100f);
            UiKit.AddText(_listContent, 16, y, 380, 78, $"{L("ui.settings.volume")}: {pct}%", 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddButton(_listContent, 420, y + 11, 80, 56, "−", () => { AdjustVolume(-0.1f); RebuildList(); });
            UiKit.AddButton(_listContent, 510, y + 11, 80, 56, "+", () => { AdjustVolume(0.1f); RebuildList(); });
            y += 96f;

            // Visor HUD effect on/off — toggles the holographic styling live (better readability when off).
            bool visorOn = Menu?.Settings?.VisorEffects ?? true;
            var visorBtn = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () =>
            {
                if (Menu?.Settings != null)
                {
                    Menu.Settings.VisorEffects = !Menu.Settings.VisorEffects;
                    Menu.Settings.Save();
                    RebuildList();
                }
            });
            UiKit.AddText(visorBtn.transform, 16, 0, 520, 78, L("ui.settings.visor"), 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(visorBtn.transform, 560, 0, 200, 78, visorOn ? L("ui.toggle.on") : L("ui.toggle.off"), 22,
                visorOn ? UiKit.Ok : UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 96f;

            // World rules (world options, live edit): creatures + the three enemy activities. The server
            // enforces the admin gate (non-admins get a reject toast); the rows re-render when the
            // re-broadcast ServerRules lands.
            UiKit.AddText(_listContent, 16, y, 760, 30, L("ui.worldopt.live_title"), 22, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 40f;
            void RuleRow(string label, string current, System.Action<string> send)
            {
                int idx = System.Array.IndexOf(WorldCreationOptions.Activity, current);
                if (idx < 0) idx = 2;
                string stepName = L("ui.worldopt.aa." + idx);
                UiKit.AddText(_listContent, 16, y, 360, 56, label, 20, UiKit.TextCol, TextAnchor.MiddleLeft);
                UiKit.AddText(_listContent, 380, y, 180, 56, stepName, 20, UiKit.Cyan, TextAnchor.MiddleCenter);
                UiKit.AddButton(_listContent, 570, y + 6, 80, 44, "−", () =>
                {
                    if (idx > 0) { send(WorldCreationOptions.Activity[idx - 1]); Invoke(nameof(RebuildList), 0.35f); }
                });
                UiKit.AddButton(_listContent, 660, y + 6, 80, 44, "+", () =>
                {
                    if (idx < WorldCreationOptions.Activity.Length - 1) { send(WorldCreationOptions.Activity[idx + 1]); Invoke(nameof(RebuildList), 0.35f); }
                });
                y += 62f;
            }

            var rules = Game?.Rules;
            RuleRow(L("ui.worldopt.creatures"), rules?.CreatureAbundance ?? "Normal", v => Game?.Network?.SendSetWorldRules(creatures: v));
            RuleRow(L("ui.worldopt.planet_enemies"), rules?.PlanetEnemies ?? "Normal", v => Game?.Network?.SendSetWorldRules(planetEnemies: v));
            RuleRow(L("ui.worldopt.space_npcs"), rules?.SpaceNpcEnemies ?? "Normal", v => Game?.Network?.SendSetWorldRules(spaceNpcs: v));
            RuleRow(L("ui.worldopt.ufos"), rules?.AlienUfos ?? "Off", v => Game?.Network?.SendSetWorldRules(ufos: v));

            // Instant Travel (world option): when on, the travel screen may quick-travel anywhere; when off
            // (default) it is limited to worlds you've already landed on. The server enforces the admin gate.
            bool instant = rules?.InstantTravel ?? false;
            var instantBtn = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () =>
            {
                Game?.Network?.SendSetWorldRules(instantTravel: instant ? "Off" : "On");
                Invoke(nameof(RebuildList), 0.35f);
            });
            UiKit.AddText(instantBtn.transform, 16, 0, 520, 78, L("ui.worldopt.instant_travel"), 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(instantBtn.transform, 560, 0, 200, 78, instant ? L("ui.toggle.on") : L("ui.toggle.off"), 22,
                instant ? UiKit.Ok : UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 96f;
            y += 16f;

            // VEGA advisor hints on/off — mutes the ship AI's optional coaching (onboarding chip stays).
            bool vegaOn = Menu?.Settings?.VegaHints ?? true;
            var vegaBtn = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () =>
            {
                if (Menu?.Settings != null)
                {
                    Menu.Settings.VegaHints = !Menu.Settings.VegaHints;
                    Menu.Settings.Save();
                    RebuildList();
                }
            });
            UiKit.AddText(vegaBtn.transform, 16, 0, 520, 78, L("ui.settings.vega_hints"), 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(vegaBtn.transform, 560, 0, 200, 78, vegaOn ? L("ui.toggle.on") : L("ui.toggle.off"), 22,
                vegaOn ? UiKit.Ok : UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 96f;

            // Skip the running tutorial / restart a finished one. Lives HERE (not on the HUD chip) because
            // gameplay captures the mouse — the menu is where the cursor is free to click.
            bool onboarding = Game?.OnboardingActive ?? false;
            var tut = UiKit.AddButton(_listContent, 0, y, 780, 78, onboarding ? L("ui.vega.skip") : L("ui.vega.restart"), () =>
            {
                Game?.Network?.SendSkipOnboarding(restart: !onboarding);
                Invoke(nameof(RebuildList), 0.35f);
            });
            tut.GetComponent<Image>().color = new Color(0.16f, 0.28f, 0.40f);
            y += 96f;

            // Explicit save (on top of the periodic autosave).
            var save = UiKit.AddButton(_listContent, 0, y, 780, 78, L("ui.settings.save_game"), () =>
            {
                Game.Network?.SendSaveGame();
                if (_feedback != null) _feedback.text = L("ui.settings.saved");
            });
            save.GetComponent<Image>().color = new Color(0.2f, 0.5f, 0.36f);
            y += 96f;

            return y;
        }

        /// <summary>Builds (once) the live faced-avatar preview rig, coloured from the player's current settings.</summary>
        private void EnsureAvatarPreview()
        {
            if (_avatarPreview != null)
            {
                return;
            }

            var go = new GameObject("AvatarPreviewRig");
            go.transform.SetParent(transform, false);
            _avatarPreview = go.AddComponent<AvatarPreviewRig>();
            var s = Menu?.Settings;
            _avatarPreview.EnsureBuilt(
                s?.SkinColor ?? Color.gray, s?.TorsoColor ?? Color.gray, s?.ArmColor ?? Color.gray, s?.LegColor ?? Color.gray);
        }

        /// <summary>Shows the rotating faced-avatar preview (rendered to a texture) in the colour tab's detail
        /// pane, so the player sees their colour choices on the actual figure — with a face (B25).</summary>
        private float BuildCharacterPreview()
        {
            EnsureAvatarPreview();
            _avatarPreview.SetActive(true);
            var s = Menu?.Settings;
            if (s != null)
            {
                _avatarPreview.SetColors(s.SkinColor, s.TorsoColor, s.ArmColor, s.LegColor);
            }

            UiKit.AddPanel(_detail, 64, 16, 500, 716, new Color(0.03f, 0.06f, 0.11f, 0.92f));
            UiKit.AddText(_detail, 64, 24, 500, 26, L("ui.settings.preview"), 18, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);

            var go = new GameObject("AvatarPreview", typeof(RectTransform));
            go.transform.SetParent(_detail, false);
            UiKit.Place(go, 94, 58, 440, 660);
            var img = go.AddComponent<RawImage>();
            img.texture = _avatarPreview.Texture;
            return 744f;
        }

        /// <summary>The Ship paint tab's list: a hull-colour swatch + a cycle button (item 32), mirroring the
        /// avatar colour cards. The live ship preview in the detail pane re-tints as you cycle.</summary>
        private float BuildHullPaintList()
        {
            float y = 0f;
            UiKit.AddText(_listContent, 16, y, 760, 40, L("ui.ship.hull_color"), 26, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 56f;

            Color hull = Menu?.Settings?.HullColor ?? Color.gray;
            var card = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () => { Menu?.CycleHull(); RebuildList(); RebuildDetail(); });
            UiKit.AddText(card.transform, 16, 0, 360, 78, L("ui.ship.hull_color"), 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddImage(card.transform, 420, 19, 120, 40, UiKit.SolidSprite, hull);
            UiKit.AddText(card.transform, 560, 0, 200, 78, L("ui.settings.next_color"), 18, UiKit.Cyan, TextAnchor.MiddleLeft);
            y += 96f;
            return y;
        }

        /// <summary>Builds (once) the live ship preview rig, tinted from the player's current hull colour.</summary>
        private void EnsureShipPreview()
        {
            if (_shipPreview != null)
            {
                return;
            }

            var go = new GameObject("ShipPreviewRig");
            go.transform.SetParent(transform, false);
            _shipPreview = go.AddComponent<ShipPreviewRig>();
            _shipPreview.Game = Game; // so the preview can render the player's real voxel ship
            _shipPreview.EnsureBuilt(Menu?.Settings?.HullColor ?? Color.gray);
        }

        /// <summary>Shows the rotating ship preview (rendered to a texture) in the paint tab's detail pane so the
        /// player sees the hull colour on the actual ship (item 32).</summary>
        private float BuildShipPreview()
        {
            EnsureShipPreview();
            _shipPreview.SetActive(true);
            if (Menu?.Settings != null)
            {
                _shipPreview.SetHullColor(Menu.Settings.HullColor);
            }

            UiKit.AddPanel(_detail, 64, 16, 500, 460, new Color(0.03f, 0.06f, 0.11f, 0.92f));
            UiKit.AddText(_detail, 64, 24, 500, 26, L("ui.ship.preview"), 18, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);

            var go = new GameObject("ShipPreview", typeof(RectTransform));
            go.transform.SetParent(_detail, false);
            UiKit.Place(go, 74, 58, 480, 400);
            var img = go.AddComponent<RawImage>();
            img.texture = _shipPreview.Texture;
            return 488f;
        }

        /// <summary>Nudges master volume and applies + persists it immediately.</summary>
        private void AdjustVolume(float delta)
        {
            var s = Menu?.Settings;
            if (s == null)
            {
                return;
            }

            s.MasterVolume = Mathf.Clamp01(s.MasterVolume + delta);
            s.Apply(); // pushes AudioListener.volume
            s.Save();
        }

        /// <summary>The flight context action at the top of the Map tab: launch into space from a surface,
        /// take the helm again from inside the parked ship (switch back to the flight view with NO take-off,
        /// since you never landed), or leave space to land on the body you're orbiting. Returns the y below it.</summary>
        private float BuildFlightAction()
        {
            string label;
            System.Action act;
            if (Game.InSpace)
            {
                label = L("ui.space.leave");
                act = () => Game.Network?.SendLeaveSpace();
            }
            else if (Game.LoadingPlanetType == "ship_interior")
            {
                // Inside the ship while it floats in space: take the helm again — this just switches back to
                // the flight view (no planet take-off — you never landed), so you simply fly on.
                label = L("ui.station.helm");
                act = () => Game.Network?.SendExitShip();
            }
            else
            {
                label = L("ui.space.enter");
                act = () => Game.Network?.SendEnterSpace();
            }

            var btn = UiKit.AddButton(_listContent, 0, 0, 760, 60, label, act);
            btn.GetComponent<Image>().color = new Color(0.13f, 0.34f, 0.52f); // space-blue accent = the primary action
            return 76f;
        }

        private void AddCard(float y, string title, string icon, string status, Color statusCol, string key, System.Action onClick, float indent = 0f, string contentKey = null)
        {
            var card = UiKit.AddButton(_listContent, indent, y, 780 - indent, 78, string.Empty, onClick);
            if (_selected == key)
            {
                card.GetComponent<Image>().color = UiKit.Cyan;
            }

            float cw = 780f - indent;
            float tx = 16f;
            // Prefer the real content-styled icon (item/material/module art); fall back to the category icon.
            var sprite = string.IsNullOrEmpty(contentKey) ? null : IconResolver.Resolve(contentKey, Game);
            bool placed = sprite != null
                ? UiKit.AddIconSprite(card.transform, 14, 14, 50, sprite, IconResolver.Tint(contentKey, Game)) != null
                : UiKit.AddIcon(card.transform, 14, 14, 50, icon) != null;
            if (placed)
            {
                tx = 78f;
            }

            UiKit.AddText(card.transform, tx, 8, cw - tx - 16, 40, title, 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(card.transform, tx, 44, cw - tx - 16, 28, status, 18, statusCol, TextAnchor.MiddleLeft);

            // Freshly crafted / freshly unlocked → the card pulses for a moment (celebration juice).
            if (!string.IsNullOrEmpty(contentKey) && contentKey == _celebrateKey && Time.unscaledTime < _celebrateUntil)
            {
                card.gameObject.AddComponent<CardPulse>().Until = _celebrateUntil;
            }
        }

        // --- detail pane ---

        private void RebuildDetail()
        {
            if (!_built)
            {
                return;
            }

            ClearChildren(_detail);

            // A new detail page (different tab / category / selected entry) starts at the top — top-anchored
            // content makes y=0 the top regardless of the size set further down, so it's safe to reset here.
            string detailPage = _mode + "|" + _category + "|" + _selected;
            if (detailPage != _detailPage)
            {
                _detailPage = detailPage;
                ScrollToTop(_detail);
            }

            // Exactly one preview rig may be live at a time, else each rig's camera also picks up the OTHER rig's
            // model and they bleed into each other (B53: the colour tab showed the ship, the paint tab showed both).
            bool showAvatar = _mode == Mode.Character;
            bool showShip = _mode == Mode.Ship && _category == "paint";
            _avatarPreview?.SetActive(showAvatar);
            _shipPreview?.SetActive(showShip);

            if (_mode == Mode.Character)
            {
                SetContentHeight(_detail, BuildCharacterPreview()); // a live, rotating faced-avatar preview (B25)
                return;
            }

            if (_mode == Mode.Ship && _category == "paint")
            {
                SetContentHeight(_detail, BuildShipPreview()); // a live, rotating ship preview (item 32)
                return;
            }

            // The travel screen shows the selected system's animated mini star map even with no body picked.
            if (_mode == Mode.Map)
            {
                SetContentHeight(_detail, DetailMap());
                return;
            }

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
                case Mode.Inventory: y = DetailInventory(); break;
                case Mode.Missions: y = DetailMissions(); break;
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

            // Quantity stepper — craft more than one at a time (the server crafts N in a single action).
            if (_craftCountKey != r.Key)
            {
                _craftCount = 1;
                _craftCountKey = r.Key;
            }

            int maxCraft = Mathf.Max(1, MaxCraftable(r));
            _craftCount = Mathf.Clamp(_craftCount, 1, maxCraft);

            UiKit.AddButton(_detail, 8, y, 50, 56, "-", () => { _craftCount = Mathf.Max(1, _craftCount - 1); RebuildDetail(); });
            UiKit.AddText(_detail, 62, y, 92, 56, _craftCount.ToString(), 24, UiKit.TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(_detail, 158, y, 50, 56, "+", () => { _craftCount = Mathf.Min(maxCraft, _craftCount + 1); RebuildDetail(); });
            UiKit.AddButton(_detail, 214, y, 74, 56, "Max", () => { _craftCount = maxCraft; RebuildDetail(); });
            y += 66f;

            int n = _craftCount;
            var btn = UiKit.AddButton(_detail, 8, y, 280, 56, L("ui.action.craft") + (maxCraft > 1 ? " ×" + n : string.Empty), () => { Game.Network.SendCraft(r.Key, n); });
            SetInteractable(btn, can);
            y += 70f;
            return y;
        }

        /// <summary>How many of a recipe the player can currently afford (0..99), from their owned inputs.</summary>
        private int MaxCraftable(RecipeDefinition r)
        {
            int m = 99;
            foreach (var inp in r.Inputs)
            {
                if (inp.Count <= 0)
                {
                    continue;
                }

                m = Mathf.Min(m, Owned(inp.Item) / inp.Count);
            }

            return Mathf.Clamp(m, 0, 99);
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

        private float DetailInventory()
        {
            string item = _selected.Substring(4);
            float y = 0f;
            UiKit.AddText(_detail, 8, y, 620, 40, ItemName(item), 30, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 48f;
            string desc = Desc($"item.{item}.desc");
            if (!string.IsNullOrEmpty(desc))
            {
                var t = UiKit.AddText(_detail, 8, y, 620, 80, desc, 20, UiKit.CyanDim, TextAnchor.UpperLeft);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                y += 84f;
            }

            UiKit.AddText(_detail, 8, y, 620, 28, $"{L("ui.craft.source")}: {Owned(item)}", 20, UiKit.Cyan, TextAnchor.UpperLeft);
            y += 40f;

            // Quick-bar assignment (B58): for a personal-inventory item, let the player drop it onto a quick-slot
            // (the quick-bar = inventory slots 0..8). Click a slot to assign/swap; the ✕ stows it to the backpack.
            if (_category != "cargo")
            {
                int fromSlot = -1;
                if (Game.Personal != null)
                {
                    foreach (var s in Game.Personal)
                    {
                        if (s.Item == item) { fromSlot = s.Slot; break; }
                    }
                }

                if (fromSlot >= 0)
                {
                    UiKit.AddText(_detail, 8, y, 620, 26, L("ui.inventory.quickbar"), 18, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
                    y += 32f;
                    for (int k = 0; k < QuickSlots; k++)
                    {
                        int kk = k;
                        string slotItem = Game.ItemInSlot(k);
                        string ic = string.IsNullOrEmpty(slotItem) ? null : IconFor(slotItem);
                        var b = UiKit.AddButton(_detail, 8 + k * 68f, y, 62, 62, (k + 1).ToString(),
                            () => { if (fromSlot != kk) Game.Network?.SendMoveItem(fromSlot, kk); }, ic);
                        if (fromSlot == k)
                        {
                            var img = b.GetComponent<Image>();
                            if (img != null) img.color = UiKit.Cyan; // the selected item already sits here
                        }
                    }

                    y += 70f;
                    if (fromSlot < QuickSlots) // already in the quick-bar → offer to stow it back to the backpack
                    {
                        UiKit.AddButton(_detail, 8, y, 300, 46, L("ui.inventory.remove_quickslot"),
                            () => Game.Network?.SendMoveItem(fromSlot, -1));
                        y += 54f;
                    }
                    else
                    {
                        UiKit.AddText(_detail, 8, y, 620, 24, L("ui.inventory.quickbar_hint"), 15, UiKit.CyanDim, TextAnchor.UpperLeft);
                        y += 28f;
                    }
                }
            }

            // Disassembly: if a (non-market) recipe builds this item, offer to break one back down into a
            // portion of its components at a workshop. Mirrors GameServer.Disassemble.
            var (recipe, perCraft) = DisassembleRecipe(item);
            if (recipe != null)
            {
                UiKit.AddText(_detail, 8, y, 620, 26, L("ui.craft.disassemble_yields"), 18, UiKit.CyanDim, TextAnchor.UpperLeft, FontStyle.Bold);
                y += 30f;
                bool anyYield = false;
                foreach (var inp in recipe.Inputs)
                {
                    int recovered = Mathf.FloorToInt(inp.Count * DisassemblyRecoveryRate / perCraft);
                    if (recovered <= 0)
                    {
                        continue;
                    }

                    anyYield = true;
                    UiKit.AddText(_detail, 24, y, 600, 24, $"{ItemName(inp.Item)}  ×{recovered}", 18, UiKit.TextCol, TextAnchor.UpperLeft);
                    y += 26f;
                }

                if (!anyYield)
                {
                    UiKit.AddText(_detail, 24, y, 600, 24, L("ui.craft.disassemble_nothing"), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                    y += 26f;
                }

                y += 8f;
                bool atWorkshop = (Game.NearbyStation ?? string.Empty) == "workshop";
                bool can = anyYield && atWorkshop && Owned(item) >= 1;
                var btn = UiKit.AddButton(_detail, 8, y, 280, 50, L("ui.action.disassemble"), () => { Game.Network.SendDisassemble(item); });
                SetInteractable(btn, can);
                y += 56f;
                if (anyYield && !atWorkshop)
                {
                    UiKit.AddText(_detail, 8, y, 620, 24, L("ui.craft.go_to_workshop"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
                    y += 28f;
                }
            }

            return y;
        }

        /// <summary>Fraction of a crafted item's recipe inputs recovered on disassembly (mirrors the server).</summary>
        private const float DisassemblyRecoveryRate = 0.5f;

        /// <summary>The non-market crafting recipe that produces <paramref name="item"/> (so it can be
        /// disassembled), plus its per-craft output count; (null, 1) when the item isn't craftable.</summary>
        private (RecipeDefinition, int) DisassembleRecipe(string item)
        {
            foreach (var r in Game.Content.Recipes.Values)
            {
                if (r.Station == CraftingStation.Market || r.Inputs.Count == 0)
                {
                    continue;
                }

                var output = r.Outputs.FirstOrDefault(o => o.Item == item);
                if (output != null)
                {
                    return (r, Mathf.Max(1, output.Count));
                }
            }

            return (null, 1);
        }

        private SystemMapWidget _systemMap; // the rotating mini-orrery; rebuilt each time the detail pane is

        private float DetailMap()
        {
            var map = Game.StarMap;
            var sys = SelectedSystem();
            float y = 0f;

            // The selected system's animated mini star map (only once you've been to the system — an unexplored
            // one shows nothing but its single "hyperjump here" entry in the list).
            bool known = sys != null && (sys.Id == CurrentSystemId() || Game.KnowsSystem(sys.Id));
            if (known)
            {
                UiKit.AddText(_detail, 8, y, 600, 30, "★ " + sys.Name, 22, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
                y += 36f;
                _systemMap = SystemMapWidget.Create(_detail, 40, y, 500, 380);
                string sel = !string.IsNullOrEmpty(_selected) && _selected.StartsWith("body:", System.StringComparison.Ordinal)
                    ? _selected.Substring(5) : string.Empty;
                _systemMap.Show(sys.Bodies, map.ActiveLocationId, sel);
                y += 396f;
            }

            // Below the map: the selected body's detail + a (gated) travel button. With no body picked, a hint.
            if (string.IsNullOrEmpty(_selected) || !_selected.StartsWith("body:", System.StringComparison.Ordinal))
            {
                UiKit.AddText(_detail, 8, y, 600, 30, L("ui.map.pick_destination"), 19, UiKit.CyanDim, TextAnchor.UpperLeft);
                return y + 40f;
            }

            string id = _selected.Substring(5);
            var body = map?.Systems.SelectMany(s => s.Bodies).FirstOrDefault(b => b.Id == id);
            if (body == null)
            {
                return y;
            }

            UiKit.AddText(_detail, 8, y, 620, 40, body.Name, 30, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 48f;
            UiKit.AddText(_detail, 8, y, 620, 28, $"{L("ui.map.kind")}: {body.Kind}", 20, UiKit.CyanDim, TextAnchor.UpperLeft);
            y += 32f;
            if (!string.IsNullOrEmpty(body.PlanetType))
            {
                UiKit.AddText(_detail, 8, y, 620, 28, $"{L("ui.map.type")}: {L($"planet.{body.PlanetType}.name")}", 20, UiKit.CyanDim, TextAnchor.UpperLeft);
                y += 32f;
            }

            bool here = body.Id == map.ActiveLocationId;
            UiKit.AddText(_detail, 8, y, 620, 28, here ? L("ui.map.here") : body.Status, 20, here ? UiKit.Cyan : UiKit.CyanDim, TextAnchor.UpperLeft);
            y += 40f;

            if (here || string.IsNullOrEmpty(body.PlanetType))
            {
                return y; // you're already here, or it isn't a landable world (stations/belts dock differently)
            }

            if (TravelUnlocked(body))
            {
                // A reachable destination — quick-travel (a cross-system one is a hyperspace jump).
                var destSystem = map.Systems.FirstOrDefault(s => s.Bodies.Any(b => b.Id == body.Id));
                bool crossSystem = destSystem != null && destSystem.Id != CurrentSystemId();
                UiKit.AddButton(_detail, 8, y, 280, 56, crossSystem ? L("ui.map.hyperjump") : L("ui.map.travel"), () => Game.Network?.SendTravel(body.Id));
                y += 64f;
                if (crossSystem)
                {
                    UiKit.AddText(_detail, 8, y, 620, 24, L("ui.map.hyperjump_hint"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
                    y += 30f;
                }
            }
            else
            {
                // Locked: never landed here + Instant Travel off — you must fly there and land manually.
                UiKit.AddText(_detail, 8, y, 600, 56, L("ui.map.locked_hint"), 18, new Color(1f, 0.8f, 0.45f), TextAnchor.UpperLeft);
                y += 64f;
            }

            return y;
        }

        private float DetailMissions()
        {
            var list = Game.Missions;
            if (list == null)
            {
                return 0f;
            }

            string id = _selected.Substring(4);
            var avail = list.Available.FirstOrDefault(m => m.Id == id);
            var active = list.Active.FirstOrDefault(m => m.Id == id);
            var m2 = avail ?? active;
            if (m2 == null)
            {
                return 0f;
            }

            float y = 0f;
            UiKit.AddText(_detail, 8, y, 620, 40, MissionText(m2), 28, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 46f;
            // Mission-giver NPC (item 13): "Mission from {Name}".
            if (!string.IsNullOrEmpty(m2.GiverName))
            {
                UiKit.AddText(_detail, 8, y, 620, 24, $"{L("ui.missions.giver")} {m2.GiverName}", 16, UiKit.Cyan, TextAnchor.UpperLeft);
                y += 28f;
            }

            // The mission's flavour/instructions. System missions send a locale key (resolved via L);
            // player-posted missions and L3 LLM board texts send display text (FreeText) shown verbatim.
            if (!string.IsNullOrEmpty(m2.Description))
            {
                UiKit.AddText(_detail, 8, y, 620, 60, m2.FreeText ? m2.Description : L(m2.Description), 17, UiKit.CyanDim, TextAnchor.UpperLeft);
                y += 64f;
            }

            foreach (var o in m2.Objectives)
            {
                UiKit.AddText(_detail, 8, y, 620, 28, $"{o.Progress}/{o.Required}", 20, UiKit.CyanDim, TextAnchor.UpperLeft);
                y += 30f;
            }

            y += 10f;
            if (avail != null)
            {
                UiKit.AddButton(_detail, 8, y, 280, 56, L("ui.action.accept"), () => Game.Network.SendAcceptMission(m2.Id));
            }
            else
            {
                UiKit.AddButton(_detail, 8, y, 280, 56, L("ui.action.turn_in"), () => Game.Network.SendTurnInMission(m2.Id));
            }

            return y + 70f;
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

            // Market (barter) trades need a vendor (or your ship's trade console); everything else needs
            // the mode's crafting station.
            if (r.Station == BlocksBeyondTheStars.Shared.Definitions.CraftingStation.Market)
            {
                if (!Game.MarketAvailable)
                {
                    reason = L("ui.craft.need_market");
                    return false;
                }
            }
            else if (!AtStation())
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
                "suit" => IsSuitGear(def),
                "consumable" => def.Category == ItemCategory.Consumable,
                "component" => (def.Category == ItemCategory.Component || def.Category == ItemCategory.Material) && !IsSuitGear(def),
                "block" => def.Category == ItemCategory.Block || !string.IsNullOrEmpty(def.PlacesBlock),
                _ => true,
            };
        }

        /// <summary>Suit gear: armour / oxygen items plus the wearable suit modules (lamp, jetpack,
        /// extractors, stealth, teleporter, comms/scanners) — so the "suit" filter shows all of them, not
        /// just armour.</summary>
        private static bool IsSuitGear(BlocksBeyondTheStars.Shared.Definitions.ItemDefinition def)
        {
            if (def.ArmorResistance > 0f || def.OxygenBonus > 0f)
            {
                return true;
            }

            switch (def.Key)
            {
                case "suit_lamp":
                case "jetpack":
                case "oxygen_extractor":
                case "stealth_suit":
                case "suit_teleporter":
                case "comm_radio":
                case "radar_scanner":
                    return true;
                default:
                    return false;
            }
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

        private const float ContentBottomPad = 28f; // breathing room so the last row clears the mask edge

        private static void SetContentHeight(RectTransform content, float h)
        {
            // Floor the content at the VIEWPORT height (so it fills the masked area) but let it SHRINK back
            // for short pages — flooring at the content's own current height (the old code) never shrank, so
            // a tall page left the scroll range stuck large. Add bottom padding so the last row isn't clipped.
            float viewportH = content.parent is RectTransform vp ? vp.rect.height : 0f;
            content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(h + ContentBottomPad, viewportH));
        }

        /// <summary>Scrolls a list/detail view back to the top — called when its page changes so a position
        /// carried over from a previous (taller) page can't hide the new page's first rows.</summary>
        private static void ScrollToTop(RectTransform content)
        {
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, 0f);
            if (content.parent != null && content.parent.GetComponent<ScrollRect>() is { } sr)
            {
                sr.velocity = Vector2.zero; // kill any fling momentum so it stays at the top
            }
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

        /// <summary>A mission's display title: FreeText (player missions, L3 LLM board texts) verbatim,
        /// otherwise the locale key resolved.</summary>
        private string MissionText(NetMission m) => m.FreeText ? m.Title : L(m.Title);
        private string ItemName(string item) => L($"item.{item}.name");
        private string Desc(string key)
        {
            string s = L(key);
            return s == key ? string.Empty : s;
        }
    }
}
