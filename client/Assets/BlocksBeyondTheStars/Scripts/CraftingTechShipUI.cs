// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using BlocksBeyondTheStars.Client.Portal;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
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
        public enum Mode { Inventory = 0, Crafting = 1, Tech = 2, Ship = 3, Map = 4, Missions = 5, Character = 6, Alliances = 7, Story = 8, Companions = 9, Photos = 10, Achievements = 11 }

        // Tab bar display order (left→right), decoupled from the Mode enum value so tabs can be reordered without
        // renumbering the modes: each entry carries its Mode (used for activation, routing and badges) and label
        // key. Core build/travel loop first, then the world/social cluster (Story/Creatures/Alliances), with
        // Settings pinned far right (config convention). Note Mode.Character is labelled "Settings".
        private static readonly (Mode Mode, string Loc)[] Tabs =
        {
            (Mode.Inventory, "ui.inventory.title"),
            (Mode.Crafting,  "ui.crafting.title"),
            (Mode.Tech,      "ui.tab.tech"),
            (Mode.Ship,      "ui.tab.ship"),
            (Mode.Map,       "ui.tab.map"),
            (Mode.Missions,  "ui.tab.missions"),
            (Mode.Story,     "ui.tab.story"),
            (Mode.Companions, "ui.tab.companions"),
            (Mode.Alliances, "ui.tab.alliances"),
            (Mode.Achievements, "ui.tab.achievements"),
            (Mode.Photos,    "ui.tab.photos"),
            (Mode.Character, "ui.tab.settings"),
        };

        // Quick-bar = the first N personal-inventory slots (must match the server's HotbarSlots / HudUi Slots).
        private const int QuickSlots = 9;

        // The personal inventory's fixed size (quick-bar 0..8 + backpack 9..23) — must match the server's
        // PlayerState inventory; the snapshot only carries occupied slots, so free slots derive from this.
        private const int PersonalSlotTotal = 24;

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

        // Alliances tab: the radio (Funk) input draft + the live scrollback Text (refreshed each frame so new
        // messages appear without rebuilding the pane, which would drop the input's focus while typing).
        private string _funkDraft = string.Empty;
        private Text _funkLog;

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
                    // #1070: the server-published station gates + the "where is it" answer drive the tab dimming, hint row and reasons.
                    + StationsSig() * 3
                    + (Game.StarMap?.Systems.Length ?? 0) * 211 + (Game.StarMap?.ActiveLocationId?.GetHashCode() ?? 0)
                    + (Game.Missions?.Available.Length ?? 0) * 307 + (Game.Missions?.Active.Length ?? 0) * 401
                    + (Game.Space?.Entities.Length ?? 0) * 503 + (Game.InSpace ? 7777 : 0)
                    // Aboard / ship-interior state drives the Map tab dimming + travel-button gating, so a change
                    // (board/leave the ship with the menu open) must rebuild the header + map buttons.
                    + (Game.Aboard ? 8887 : 0) + (Game.LoadingPlanetType?.GetHashCode() ?? 0)
                    + (Game.LastMessage?.GetHashCode() ?? 0)
                    // Alliances tab: roster + pending changes refresh the lists; the online-player count drives the
                    // "find players" picker. The radio (Funk) feed is deliberately NOT hashed — its log refreshes in
                    // place each frame so an incoming message never rebuilds the pane and steals the input's focus.
                    + (Game.Alliances?.Allies.Length ?? 0) * 601 + (Game.Alliances?.Incoming.Length ?? 0) * 701
                    + (Game.Alliances?.Outgoing.Length ?? 0) * 809 + (Game.StarMap?.Players.Length ?? 0) * 53
                    // Knowledge total + the new-content flags drive the Tech/Story/Arcade menu badges.
                    + Game.Knowledge * 131 + (Game.NewArcadeUnseen ? 1201 : 0) + (Game.NewStoryUnseen ? 1303 : 0)
                    // Companions tab: roster length + present-count + the "new companion" badge flag.
                    + (Game.Companions?.Companions.Length ?? 0) * 907 + (Game.NewCompanionUnseen ? 1409 : 0)
                    + (Game.Companions?.Companions.Count(c => c.Present) ?? 0) * 67
                    // The local custom pixel face + body paintings: applying one in the editor must rebuild the
                    // Character tab so the live preview re-applies it (SetFace/SetBodyPaint run on rebuild).
                    + (Game.FacePixels?.GetHashCode() ?? 0)
                    + (Game.BodyPaintPixels[0]?.GetHashCode() ?? 0) * 3 + (Game.BodyPaintPixels[1]?.GetHashCode() ?? 0) * 5
                    + (Game.BodyPaintPixels[2]?.GetHashCode() ?? 0) * 7 + (Game.BodyPaintPixels[3]?.GetHashCode() ?? 0) * 11
                    // …and the four base colours, which the appearance screen can now change while it is open
                    // (#899) — the Character card shows them as swatches and the preview tints from them.
                    + (Menu?.Settings?.SkinColor.GetHashCode() ?? 0) * 13 + (Menu?.Settings?.TorsoColor.GetHashCode() ?? 0) * 17
                    + (Menu?.Settings?.ArmColor.GetHashCode() ?? 0) * 19 + (Menu?.Settings?.LegColor.GetHashCode() ?? 0) * 23;
            if (h != _lastDataHash)
            {
                _lastDataHash = h;
                BuildHeader();
                RebuildSidebar(); // map systems / sections can arrive async
                RebuildList();
                RebuildDetail();
            }

            // Live radio scrollback: keep the Funk view current without a rebuild (preserves typing focus).
            if (_mode == Mode.Alliances && _category == "funk" && _funkLog != null)
            {
                _funkLog.text = ComposeFunkLog();
            }

            // Live "where is it" readout (#1072): distance + arrow follow the player without a rebuild, and the
            // locate request is re-armed every few seconds so the nearest station tracks the player walking.
            if (Time.unscaledTime - _whereRefreshedAt > 0.2f && (_mode == Mode.Crafting || _mode == Mode.Tech || _mode == Mode.Ship))
            {
                _whereRefreshedAt = Time.unscaledTime;
                string missing = MissingStation();
                if (missing != null)
                {
                    RequestLocate(missing);
                    _whereText.text = WhereText(missing);
                }
            }
        }

        private float _whereRefreshedAt;

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

            // The transmuter gets its own matter-synthesis sting over the generic craft feedback.
            if (recipe.Station == BlocksBeyondTheStars.Shared.Definitions.CraftingStation.Transmuter)
            {
                ClientAudio.Instance?.Cue("matter_synth", 0.8f);
            }

            // A factory craft is a heavy machine stamping out the goods — play the press clunk.
            if (recipe.Station == BlocksBeyondTheStars.Shared.Definitions.CraftingStation.Factory)
            {
                ClientAudio.Instance?.Cue("factory_craft", 0.9f);
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

            // Gate row between the tab bar and the panels (#1071/#1072): "what do I need" (hint), "where is it"
            // (live distance + arrow), a Show-on-compass button and a "craft one" jump. Used to sit INSIDE the
            // list panel at the search box's exact rect, so in Crafting mode the two overlapped.
            _hint = UiKit.AddText(root, 380, 112, 800, 36, string.Empty, 20, new Color(1f, 0.8f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
            _whereText = UiKit.AddText(root, 1190, 112, 300, 36, string.Empty, 20, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _whereShow = UiKit.AddButton(root, 1500, 114, 120, 32, L("ui.craft.where_show"), () => MarkLocatedStation(MissingStation()));
            _whereCraft = UiKit.AddButton(root, 1630, 114, 250, 32, L("ui.craft.where_craft"), () =>
            {
                var r = RecipeForStationBlock(MissingStation());
                if (r != null)
                {
                    JumpToRecipe(r.Key);
                }
            });
            _whereShow.gameObject.SetActive(false);
            _whereCraft.gameObject.SetActive(false);
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
            //
            // The bar is split across two rows so the Close button can no longer overlap the right-most tabs:
            //   • a top utility strip (next to the "Ship Interface" logo) carries Codex / Arcade / Close, and
            //   • the main row below carries the content tabs only.
            // Freeing the right side of the tab row also lets the tabs stay full-width/readable instead of being
            // tightened to squeeze the Codex/Arcade/Close buttons onto the same line.
            float tw = 150f;
            float step = 158f;
            float x = 40f;
            for (int i = 0; i < Tabs.Length; i++)
            {
                int tab = (int)Tabs[i].Mode; // routing/activation/badges key on the Mode, not the display position
                bool active = (int)_mode == tab;
                var b = UiKit.AddButton(p, x, 64, tw, 46, L(Tabs[i].Loc), () => OnTab(tab));
                if (active)
                {
                    b.GetComponent<Image>().color = UiKit.Cyan;
                }
                else if (!IsTabAvailable(Tabs[i].Mode))
                {
                    // Context not met (Map needs you aboard; Crafting/Tech/Ship need their station): dim the tab so
                    // it reads as out-of-reach. It stays CLICKABLE so the player can still browse the tab's
                    // content — the action buttons inside enforce the actual gate. The small icon in the corner
                    // names the BLOCK the tab is waiting for (#1071): workbench / cockpit / workshop module.
                    b.GetComponent<Image>().color = UiKit.TabLocked;
                    var dimLbl = b.GetComponentInChildren<Text>();
                    if (dimLbl != null)
                    {
                        dimLbl.color = UiKit.CyanDim;
                    }

                    var badgeSprite = StationSprite(TabStation(Tabs[i].Mode));
                    if (badgeSprite != null)
                    {
                        var ic = UiKit.AddIconSprite(b.transform, tw - 24, 4, 20, badgeSprite, new Color(1f, 1f, 1f, 0.85f));
                        if (ic != null)
                        {
                            ic.raycastTarget = false;
                        }
                    }
                }

                // Auto-fit the label so a long localized tab (e.g. German "Einstellungen") shrinks to fit the
                // fixed-width button instead of spilling over its graphic (B28); short labels keep full size.
                UiKit.FitLabel(b.GetComponentInChildren<Text>(), 12, 22);

                // Badge a tab that has new content waiting behind it: the Tech tab when research is affordable
                // now, the Story tab when an unread beat/fragment/memory arrived. Cleared by opening the tab.
                bool badge = (tab == (int)Mode.Tech && AnyBlueprintUnlockable())
                             || (tab == (int)Mode.Story && (Game?.NewStoryUnseen ?? false))
                             || (tab == (int)Mode.Companions && (Game?.NewCompanionUnseen ?? false));
                if (badge && !active)
                {
                    UiKit.AddBadge(b, tw);
                }

                x += step;
            }

            // Utility strip on the top row (right of the "Ship Interface" logo, at the logo's y): the
            // always-available browser screens (Codex/wiki + Arcade, separate full-screen overlays) and Close.
            // Kept off the tab row so Close can sit in the top-right corner without overlapping the tabs.
            const float topY = 14f;     // matches the logo's y so the strip aligns with the heading
            const float topH = 40f;
            UiKit.AddButton(p, W - 150, topY, 110, topH, L("ui.action.close"), () => Menu?.CloseFromUi());
            UiKit.AddButton(p, W - 298, topY, 140, topH, L("ui.tab.wiki"), () => Menu?.OpenWiki());
            var arcadeBtn = UiKit.AddButton(p, W - 446, topY, 140, topH, L("ui.tab.arcade"), () => Menu?.OpenArcade());
            if (Game?.NewArcadeUnseen ?? false)
            {
                UiKit.AddBadge(arcadeBtn, 140f); // a freshly downloaded data-cube game is waiting in the Arcade
            }

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
                    UiKit.FitLabel(h, 11, 17);
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
                    list.Add(("color", L("ui.craft.cat_color"), "cat_blocks"));
                    list.Add(("shape", L("ui.craft.cat_shape"), "cat_blocks"));
                    list.Add(("market", L("ui.craft.cat_market"), "cat_cargo"));
                    break;
                case Mode.Tech:
                    foreach (var c in Game.Content.Blueprints.Values.Select(b => b.Category).Where(c => !string.IsNullOrEmpty(c)).Distinct())
                    {
                        list.Add((c, IdLabel("ui.tech.cat_", c), "cat_tech"));
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
                    list.Add(("personal", L("ui.inventory.backpack"), "cat_inventory"));
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
                case Mode.Alliances:
                    list.Clear();
                    if (_category != "allies" && _category != "find" && _category != "funk")
                    {
                        _category = "allies"; // the tab opens with "all" by default — land on the roster
                    }

                    list.Add(("allies", L("ui.alliance.cat_allies"), "cat_mission"));
                    list.Add(("find", L("ui.alliance.cat_find"), "cat_tech"));
                    list.Add(("funk", L("ui.alliance.cat_funk"), "cat_cargo"));
                    break;
                case Mode.Story:
                    list.Clear();
                    list.Add(("log", L("ui.story.cat_log"), "cat_mission")); // the Story Log (read-only)
                    break;
                case Mode.Companions:
                    list.Clear();
                    if (_category != "here" && _category != "all")
                    {
                        _category = "here"; // open on the companions present on this world
                    }

                    list.Add(("here", L("ui.companions.cat_here"), "cat_mission"));
                    list.Add(("all", L("ui.companions.cat_all"), "cat_inventory"));
                    break;
                case Mode.Photos:
                    list.Clear();
                    list.Add(("all", L("ui.photos.cat_all"), "cat_inventory"));
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
            RefreshGateRow(production);

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
                case Mode.Alliances: y = BuildAlliancesList(); break;
                case Mode.Story: y = BuildStoryList(); break;
                case Mode.Companions: y = BuildCompanionsList(); break;
                case Mode.Photos: y = BuildPhotosList(); break;
                case Mode.Achievements: y = BuildAchievementList(); break;
            }

            SetContentHeight(_listContent, y);
            string listPage = _mode + "|" + _category;
            if (listPage != _listPage)
            {
                _listPage = listPage;
                ScrollToTop(_listContent); // a new page starts at the top, not wherever the last one was scrolled
            }

            _footer.text = production ? L("ui.craft.source") + "   |   " + InReachText() : string.Empty;
            if (_feedback != null)
            {
                _feedback.text = Game.LastMessage ?? string.Empty;
            }
        }

        /// <summary>The gate row (#1071/#1072): hint ("Hand recipes only here — everything else needs a
        /// Workbench" / "Research happens at your ship's cockpit" / …), the live "where" readout, and the
        /// Show / craft-one buttons. Asks the server for the nearest station whenever a gate is missing.</summary>
        private void RefreshGateRow(bool production)
        {
            string missing = production ? MissingStation() : null;
            if (_mode == Mode.Map && !AboardShipNow())
            {
                _hint.text = L("ui.map.need_ship");
            }
            else
            {
                _hint.text = GateHint(missing);
            }

            if (missing != null)
            {
                RequestLocate(missing);
            }

            var loc = CurrentLocation(missing);
            _whereText.text = missing == null ? string.Empty : WhereText(missing);
            _whereShow.gameObject.SetActive(loc != null && loc.Found);
            _whereCraft.gameObject.SetActive(missing != null && loc != null && !loc.Found && RecipeForStationBlock(missing) != null);
        }

        // Colour palette for the always-available Dye/Glow action (swatch grid; 0xRRGGBB): a spread of
        // vivid hues plus a greyscale row, matching the user's "palette + fine picker" choice.
        private static readonly int[] ColorPalette =
        {
            0xE03A3A, 0xE07A2A, 0xE0C020, 0x8FD030, 0x35C04A, 0x2FB0A0,
            0x2F8FE0, 0x2F4FE0, 0x6A3FE0, 0xB23FE0, 0xE03FA0, 0xE83060,
            0xFFFFFF, 0xC8D0D8, 0x8A94A0, 0x4A5260, 0x2A3038, 0x12161A,
        };

        private static Color RgbToColor(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        private float BuildCraftingList()
        {
            if (_category == "color")
            {
                return BuildColorList();
            }

            if (_category == "shape")
            {
                return BuildShapeList();
            }

            float y = 0f;
            var entries = new List<(RecipeDefinition r, string outItem, bool can)>();
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

                entries.Add((r, outItem.Item, can));
            }

            // Reachability ordering (#826): craftable first, blueprint-unlocked-but-missing-materials in the
            // middle, blueprint-locked last; within a tier simpler recipes first. The within-tier key is
            // static (inventory-independent), so cards only move when they change tier. Market keeps the
            // authored order — barter offers are a vendor's stall, not a progression list.
            if (_category != "market")
            {
                entries = entries
                    .OrderBy(e => ReachTier(e.r.RequiredBlueprint, e.r.Inputs))
                    .ThenBy(e => e.r.Inputs.Count)
                    .ThenBy(e => Game.Content.MaxInputDepth(e.r.Inputs))
                    .ThenBy(e => e.r.Inputs.Sum(i => i.Count))
                    .ToList();
            }

            foreach (var e in entries)
            {
                string key = e.r.Key;
                AddCard(y, ItemName(e.outItem), IconFor(e.outItem), e.can ? L("ui.craft.ready") : L("ui.craft.blocked"),
                    e.can ? UiKit.Ok : new Color(1f, 0.5f, 0.5f), key, () => { _selected = key; RebuildDetail(); }, contentKey: e.outItem);
                y += 88f;
            }

            return y;
        }

        /// <summary>Lists the player's tintable building materials for the always-available Dye/Glow action.</summary>
        private float BuildColorList()
        {
            float y = 0f;
            var seen = new HashSet<string>();
            if (Game.Personal != null)
            {
                foreach (var s in Game.Personal)
                {
                    if (s == null || string.IsNullOrEmpty(s.Item) || !seen.Add(s.Item))
                    {
                        continue;
                    }

                    var item = Game.Content.GetItem(s.Item);
                    if (item == null || string.IsNullOrEmpty(item.PlacesBlock))
                    {
                        continue;
                    }

                    var blk = Game.Content.GetBlock(item.PlacesBlock);
                    if (blk == null || !blk.Tintable || !MatchesSearch(ItemName(s.Item)))
                    {
                        continue;
                    }

                    string key = "color:" + s.Item;
                    AddCard(y, ItemName(s.Item), IconFor(s.Item), "×" + Owned(s.Item), UiKit.CyanDim, key,
                        () => { _selected = key; RebuildDetail(); }, contentKey: s.Item);
                    y += 88f;
                }
            }

            if (y == 0f)
            {
                UiKit.AddText(_listContent, 8, 8, 700, 30, L("ui.color.none"), 22, UiKit.CyanDim, TextAnchor.UpperLeft);
                y = 40f;
            }

            return y;
        }

        /// <summary>Detail pane for the Dye/Glow action: a colour palette that recolours the selected material
        /// (top) or turns it into a coloured light source consuming a crystal (bottom).</summary>
        private float DetailColor()
        {
            string src = _selected.Substring("color:".Length);
            float y = 0f;
            UiKit.AddText(_detail, 8, y, 620, 40, ItemName(src), 30, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 46f;
            UiKit.AddText(_detail, 8, y, 620, 48, "×" + Owned(src) + "   ·   " + L("ui.color.help"), 18, UiKit.CyanDim, TextAnchor.UpperLeft).horizontalOverflow = HorizontalWrapMode.Wrap;
            y += 54f;

            // Dye (surface tint) — a free 1:1 recolour.
            UiKit.AddText(_detail, 8, y, 620, 28, L("ui.color.dye"), 22, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 34f;
            y = AddSwatchGrid(y, src, glow: false);
            y += 14f;

            // Glow (coloured light source) — consumes a crystal per unit.
            UiKit.AddText(_detail, 8, y, 620, 28, L("ui.color.glow"), 22, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 32f;
            int crystals = Owned("crystal");
            UiKit.AddText(_detail, 8, y, 620, 24, L("ui.color.glow_cost") + "  " + crystals + "/1", 18,
                crystals > 0 ? UiKit.CyanDim : new Color(1f, 0.6f, 0.4f), TextAnchor.UpperLeft);
            y += 30f;
            y = AddSwatchGrid(y, src, glow: true);
            return y + 8f;
        }

        /// <summary>A grid of colour swatch buttons; clicking one sends the Dye (or Glow) craft for the source.</summary>
        private float AddSwatchGrid(float y, string src, bool glow)
        {
            const int cols = 6;
            const float sw = 64f, gap = 10f;
            bool blocked = glow && Owned("crystal") < 1;
            for (int i = 0; i < ColorPalette.Length; i++)
            {
                int rgb = ColorPalette[i];
                float bx = 8 + (i % cols) * (sw + gap);
                float by = y + (i / cols) * (sw + gap);
                var b = UiKit.AddButton(_detail, bx, by, sw, sw, string.Empty,
                    () => { Game.Network.SendTintCraft(src, glow ? 0 : rgb, glow ? rgb : 0); });
                if (b.GetComponent<Image>() is { } img)
                {
                    img.color = RgbToColor(rgb);
                }

                if (blocked)
                {
                    SetInteractable(b, false);
                }
            }

            int rows = (ColorPalette.Length + cols - 1) / cols;
            return y + rows * (sw + gap);
        }

        // The forms the always-available "Shape" action can craft (shape index → locale key). 0 = plain cube
        // (reverts a shaped material). Indices match BlocksBeyondTheStars.Shared.World.BlockShape.
        private static readonly (int Shape, string Loc)[] ShapeOptions =
        {
            (0, "ui.shape.cube"), (1, "ui.shape.slab"), (2, "ui.shape.pyramid"), (3, "ui.shape.dome"),
            (4, "ui.shape.sphere"), (5, "ui.shape.ramp"), (6, "ui.shape.stairs"), (7, "ui.shape.cone"),
            (8, "ui.shape.cylinder"), (9, "ui.shape.panel"), (10, "ui.shape.post"), (11, "ui.shape.beam"),
            (12, "ui.shape.lowramp"), (13, "ui.shape.quartercube"),
            (14, "ui.shape.table"), (15, "ui.shape.chair"), (16, "ui.shape.fence"),
            (17, "ui.shape.sheet"), (18, "ui.shape.pot"),
        };

        /// <summary>Lists the player's shapeable building materials for the always-available Shape action.</summary>
        private float BuildShapeList()
        {
            float y = 0f;
            var seen = new HashSet<string>();
            if (Game.Personal != null)
            {
                foreach (var s in Game.Personal)
                {
                    if (s == null || string.IsNullOrEmpty(s.Item) || !seen.Add(s.Item))
                    {
                        continue;
                    }

                    var item = Game.Content.GetItem(s.Item);
                    if (item == null || string.IsNullOrEmpty(item.PlacesBlock))
                    {
                        continue;
                    }

                    var blk = Game.Content.GetBlock(item.PlacesBlock);
                    if (blk == null || !blk.Shapeable || !MatchesSearch(ItemName(s.Item)))
                    {
                        continue;
                    }

                    string key = "shape:" + s.Item;
                    AddCard(y, ItemName(s.Item), IconFor(s.Item), "×" + Owned(s.Item), UiKit.CyanDim, key,
                        () => { _selected = key; RebuildDetail(); }, contentKey: s.Item);
                    y += 88f;
                }
            }

            if (y == 0f)
            {
                UiKit.AddText(_listContent, 8, 8, 700, 30, L("ui.shape.none"), 22, UiKit.CyanDim, TextAnchor.UpperLeft);
                y = 40f;
            }

            return y;
        }

        /// <summary>Detail pane for the Shape action: a grid of form buttons that re-form the selected material
        /// (the current form is shown disabled; "cube" reverts it). Free 1:1, like dyeing. The placement
        /// direction is decided from where the player faces, so it isn't chosen here.</summary>
        private float DetailShape()
        {
            string src = _selected.Substring("shape:".Length);
            int current = ItemKey.Shape(src);
            float y = 0f;
            UiKit.AddText(_detail, 8, y, 620, 40, ItemName(src), 30, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 46f;
            UiKit.AddText(_detail, 8, y, 620, 48, "×" + Owned(src) + "   ·   " + L("ui.shape.help"), 18, UiKit.CyanDim, TextAnchor.UpperLeft).horizontalOverflow = HorizontalWrapMode.Wrap;
            y += 54f;
            return AddShapeGrid(y, src, current);
        }

        /// <summary>A grid of form buttons; clicking one sends the Shape craft for the source material. The
        /// button for the material's current form is shown disabled.</summary>
        private float AddShapeGrid(float y, string src, int current)
        {
            const int cols = 2;
            const float bw = 300f, bh = 50f, gap = 10f;
            for (int i = 0; i < ShapeOptions.Length; i++)
            {
                var (shape, loc) = ShapeOptions[i];
                float bx = 8 + (i % cols) * (bw + gap);
                float by = y + (i / cols) * (bh + gap);
                bool isCurrent = shape == current;
                int target = shape; // capture for the closure
                var b = UiKit.AddButton(_detail, bx, by, bw, bh, L(loc),
                    () => { Game.Network.SendShapeCraft(src, target); });
                if (isCurrent)
                {
                    SetInteractable(b, false);
                }
            }

            int rows = (ShapeOptions.Length + cols - 1) / cols;
            y += rows * (bh + gap) + 16f;
            return AddCustomFormSection(y, src, current);
        }

        /// <summary>"My forms" (#845): the player's own saved forms, craftable out of the selected material
        /// exactly like a built-in one. Only shown once the shaping tool is in the inventory — that tool is
        /// what unlocks designing; the built-in forms above stay free for everyone.</summary>
        private float AddCustomFormSection(float y, string src, int current)
        {
            UiKit.AddText(_detail, 8, y, 620, 30, L("ui.shape.custom.section"), 20, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 36f;

            if (!HasShapeTool())
            {
                UiKit.AddText(_detail, 8, y, 620, 44, L("ui.shape.custom.locked"), 16, UiKit.CyanDim, TextAnchor.UpperLeft)
                    .horizontalOverflow = HorizontalWrapMode.Wrap;
                return y + 50f;
            }

            const float bw = 300f, bh = 50f, gap = 10f;
            UiKit.AddButton(_detail, 8, y, bw, bh, L("ui.shape.custom.design_new"), () => OpenFormEditor(src, string.Empty, string.Empty));
            y += bh + gap;

            var forms = CustomShapeLibrary.List();
            if (forms.Count == 0)
            {
                UiKit.AddText(_detail, 8, y, 620, 44, L("ui.shape.custom.empty"), 16, UiKit.CyanDim, TextAnchor.UpperLeft)
                    .horizontalOverflow = HorizontalWrapMode.Wrap;
                return y + 50f;
            }

            for (int i = 0; i < forms.Count; i++)
            {
                var form = forms[i];
                float bx = 8 + (i % 2) * (bw + gap);
                float by = y + (i / 2) * (bh + gap);
                // Craft it straight from the material; the editor is only needed to CHANGE a form.
                var b = UiKit.AddButton(_detail, bx, by, bw - 108f, bh, form.Name,
                    () => Game.Network.SendCustomShapeCraft(src, form.Voxels, form.Name));
                UiKit.AddButton(_detail, bx + bw - 100f, by, 48f, bh, "✎", () => OpenFormEditor(src, form.Voxels, form.Name));
                // Stamp it onto a blank stencil to give it away (#846) — the same craft, a different source.
                UiKit.AddButton(_detail, bx + bw - 48f, by, 48f, bh, "▤", () => StampStencil(form.Voxels, form.Name));

                // A form the material already carries cannot be re-crafted into itself.
                if (current != 0 && Game.CustomShapes != null
                    && Game.CustomShapes.TryGetVoxels(current, out string currentVoxels) && currentVoxels == form.Voxels)
                {
                    SetInteractable(b, false);
                }
            }

            int formRows = (forms.Count + 1) / 2;
            return y + formRows * (bh + gap) + 8f;
        }

        /// <summary>True when the shaping tool is anywhere in the player's inventory (holding it is only
        /// needed for the in-world actions). The server re-checks this — the button is a courtesy, not a gate.</summary>
        private bool HasShapeTool()
        {
            foreach (var stack in Game.Personal)
            {
                if (stack != null && ItemKey.Base(stack.Item) == "shape_tool")
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Stamps a form onto a blank stencil so it can be handed to another player (#846). The
        /// server runs the same 1:1 exchange it runs for a material — the stencil's item key just carries a
        /// form index instead of a block doing so.</summary>
        private void StampStencil(string voxels, string name)
        {
            bool hasBlank = false;
            foreach (var stack in Game.Personal)
            {
                if (stack != null && stack.Item == "shape_stencil")
                {
                    hasBlank = true;
                    break;
                }
            }

            if (!hasBlank)
            {
                Game.ShowMessage(L("ui.shape.custom.stencil_none"));
                return;
            }

            Game.Network.SendCustomShapeCraft("shape_stencil", voxels, name);
            Game.ShowMessage(L("ui.shape.custom.stencil_done"));
        }

        /// <summary>Opens the form editor from the crafting menu, where a material IS selected — so Apply
        /// crafts the form out of it (and keeps it in the library).</summary>
        private void OpenFormEditor(string src, string voxels, string name)
        {
            var go = new GameObject("ShapeEditor");
            var editor = go.AddComponent<ShapeEditor>();
            editor.Game = Game;
            editor.InitialVoxels = voxels;
            editor.InitialName = name;
            editor.Localizer = key => Game.Localizer?.Get(key) ?? key;
            editor.OnSaveDesign = CustomShapeLibrary.Save;
            editor.LibraryProvider = CustomShapeLibrary.List;
            editor.OnApply = (v, n) =>
            {
                CustomShapeLibrary.Save(v, n);
                Game.Network.SendCustomShapeCraft(src, v, n);
            };
            editor.OnClosed = RebuildList; // the new form should show up in the list behind it
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
                // Same reachability ordering as the craft list (#826): buildable → materials missing →
                // blueprint locked, simpler modules first inside each tier.
                var modules = Game.Content.ShipModules.Values.Where(m => !m.Mandatory)
                    .OrderBy(m => ReachTier(m.RequiredBlueprint, m.BuildCost))
                    .ThenBy(m => m.BuildCost.Count)
                    .ThenBy(m => Game.Content.MaxInputDepth(m.BuildCost))
                    .ThenBy(m => m.BuildCost.Sum(c => c.Count));
                foreach (var m in modules)
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

            // Cargo transfer controls. The hold only exists while aboard the ship (in flight or in the landed
            // cabin), so the bulk move buttons + capacity readout show only then; on foot the cargo tab explains
            // why it's empty instead of looking dead.
            if (AboardShipNow() && _category == "cargo")
            {
                int used = items?.Length ?? 0;
                UiKit.AddText(_listContent, 8, y, 400, 44, $"{L("ui.cargo.capacity")}: {used}/{Game.CargoSlots}", 18, UiKit.CyanDim, TextAnchor.MiddleLeft);
                UiKit.AddButton(_listContent, 412, y, 348, 44, L("ui.cargo.take_all"),
                    () => Game.Network?.SendMoveCargoItem(toCargo: false, item: string.Empty, bulkAll: true));
                y += 56f;
            }
            else if (_category == "cargo")
            {
                UiKit.AddText(_listContent, 8, y, 752, 30, L("ui.cargo.not_aboard"), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                y += 40f;
            }
            else if (AboardShipNow() && _category == "personal")
            {
                UiKit.AddButton(_listContent, 8, y, 752, 44, L("ui.cargo.stow_all"),
                    () => Game.Network?.SendMoveCargoItem(toCargo: true, item: string.Empty, bulkAll: true));
                y += 56f;
            }

            if (items == null || items.Length == 0)
            {
                UiKit.AddText(_listContent, 8, y + 8, 700, 30, "—", 22, UiKit.CyanDim, TextAnchor.UpperLeft);
                return y + 40f;
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
                if (!AboardShipNow())
                {
                    SetInteractable(jump, false); // travel happens from your ship — board it first
                }

                y += 76f;
                return y;
            }

            // The selected system's bodies (reachable targets).
            foreach (var b in sys.Bodies)
            {
                bool here = b.Id == map.ActiveLocationId;
                bool isStation = b.Kind == "SpaceStation";
                string kindLabel = isStation ? L("ui.map.kind_station") : IdLabel("ui.map.kind_", b.Kind);
                string status = here ? L("ui.map.here") : $"{kindLabel}  {IdLabel("ui.map.status_", b.Status)}";

                // A space station shows whose it is: yours, another player's, or none (procedural/NPC).
                if (isStation && !string.IsNullOrEmpty(b.OwnerName))
                {
                    status += b.OwnerName == Game.LocalPlayerId
                        ? "   ◆ " + L("ui.map.your_station")
                        : "   ◆ " + L("ui.map.station_of") + " " + b.OwnerName;
                }

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

                // The player's own colour mark, so a marked planet is spottable straight from the list.
                int listMark = MarkerColorOf(b.Id);
                if (listMark >= 0 && listMark < PlanetMarkerPalette.Count)
                {
                    status += "   ● " + L(PlanetMarkerPalette.NameKeys[listMark]);
                }

                // Your own claims on this body: a station orbiting it, and/or a founded base on it.
                if (Game.HasMyStation(b.Id))
                {
                    status += "   ◆ " + L("ui.map.station_here");
                }

                string mapBase = Game.MyBaseName(b.Id);
                if (mapBase != null)
                {
                    status += "   ⌂ " + L("ui.map.base_here") + (string.IsNullOrEmpty(mapBase) ? string.Empty : ": " + mapBase);
                }

                // Locked: a place you can't quick-travel to yet. Surfaces unlock by landing; stations by docking once.
                if (!here && !TravelUnlocked(b))
                {
                    if (isStation)
                    {
                        status += "   · " + L("ui.map.visit_to_unlock");
                    }
                    else if (!string.IsNullOrEmpty(b.PlanetType))
                    {
                        status += "   · " + L("ui.map.fly_to_unlock");
                    }
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
                UiKit.AddText(c, 12, y, 600, 32, $"{IdLabel("ui.missions.objtype_", o.Type)}  {o.Required}× {ItemName(o.Target)}", 18, UiKit.TextCol, TextAnchor.MiddleLeft);
                UiKit.AddButton(c, 700, y, 60, 32, "✕", () => { _pmObjectives.RemoveAt(idx); RebuildList(); });
                y += 38f;
            }

            // Builder row: type / target / count / add.
            // The cycler shows the localized label; PmTypes stays the wire value (NetMissionObjective.Type).
            UiKit.AddButton(c, 0, y, 150, 38, IdLabel("ui.missions.objtype_", PmTypes[_pmType]), () => { _pmType = (_pmType + 1) % PmTypes.Length; RebuildList(); });
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
            Color[] cols = Menu != null && Menu.Settings != null
                ? new[] { Menu.Settings.SkinColor, Menu.Settings.TorsoColor, Menu.Settings.ArmColor, Menu.Settings.LegColor }
                : new[] { Color.gray, Color.gray, Color.gray, Color.gray };

            // Recolour the live preview as the player cycles a part (this list rebuilds on every cycle).
            if (_avatarPreview != null && Menu?.Settings != null)
            {
                _avatarPreview.SetColors(cols[0], cols[1], cols[2], cols[3]);
                _avatarPreview.SetFace(Menu.Settings.FacePixels);
                for (int part = 0; part < BodyPaintKit.PartCount; part++)
                {
                    _avatarPreview.SetBodyPaint(part, Menu.Settings.GetBodyPaint(part)); // body paintings (#874)
                }
            }

            // ONE appearance card (#899). This list used to carry nine: four "cycle this colour" rows and five
            // "open that editor" rows — the colour of a part and the painting on it edited two menu levels
            // apart, though they are the same decision (unpainted pixels ARE the base colour). The editor now
            // holds both, with the parts as tabs and a live figure beside the canvas.
            var card = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () => Menu?.OpenAppearanceEditor());
            UiKit.AddText(card.transform, 16, 0, 380, 78, L("ui.appearance.title"), 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            for (int i = 0; i < 4; i++)
            {
                UiKit.AddImage(card.transform, 400 + i * 44, 24, 38, 30, UiKit.SolidSprite, cols[i]); // the four base colours at a glance
            }

            UiKit.AddText(card.transform, 600, 0, 170, 78, L("ui.face.open"), 18, UiKit.Cyan, TextAnchor.MiddleLeft);
            y += 96f;

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
            void StepRow(string label, string[] steps, string labelPrefix, string current, System.Action<string> send)
            {
                int idx = System.Array.IndexOf(steps, current);
                if (idx < 0) idx = 2;
                string stepName = L(labelPrefix + idx);
                UiKit.AddText(_listContent, 16, y, 360, 56, label, 20, UiKit.TextCol, TextAnchor.MiddleLeft);
                UiKit.AddText(_listContent, 380, y, 180, 56, stepName, 20, UiKit.Cyan, TextAnchor.MiddleCenter);
                UiKit.AddButton(_listContent, 570, y + 6, 80, 44, "−", () =>
                {
                    if (idx > 0) { send(steps[idx - 1]); Invoke(nameof(RebuildList), 0.35f); }
                });
                UiKit.AddButton(_listContent, 660, y + 6, 80, 44, "+", () =>
                {
                    if (idx < steps.Length - 1) { send(steps[idx + 1]); Invoke(nameof(RebuildList), 0.35f); }
                });
                y += 62f;
            }

            void RuleRow(string label, string current, System.Action<string> send)
                => StepRow(label, WorldCreationOptions.Activity, "ui.worldopt.aa.", current, send);

            var rules = Game?.Rules;
            RuleRow(L("ui.worldopt.creatures"), rules?.CreatureAbundance ?? "Normal", v => Game?.Network?.SendSetWorldRules(creatures: v));
            RuleRow(L("ui.worldopt.planet_enemies"), rules?.PlanetEnemies ?? "Normal", v => Game?.Network?.SendSetWorldRules(planetEnemies: v));
            RuleRow(L("ui.worldopt.space_npcs"), rules?.SpaceNpcEnemies ?? "Normal", v => Game?.Network?.SendSetWorldRules(spaceNpcs: v));
            RuleRow(L("ui.worldopt.ufos"), rules?.AlienUfos ?? "Off", v => Game?.Network?.SendSetWorldRules(ufos: v));
            RuleRow(L("ui.worldopt.bandits"), rules?.Bandits ?? "Normal", v => Game?.Network?.SendSetWorldRules(bandits: v));
            // Environmental hazards (#670): the live switch for the temperature survival hazard — Off
            // disables it on a running world, Light/Hard soften/sharpen the drain and exposure damage.
            StepRow(L("ui.worldopt.hazards"), WorldCreationOptions.HazardSteps, "ui.worldopt.hz.",
                rules?.EnvironmentalHazards ?? "Normal", v => Game?.Network?.SendSetWorldRules(hazards: v));

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

            // Keep ship on destruction (world option): when on (default) a ship lost in space combat is recovered
            // intact to base; when off it is left a wreck the owner must repair before flying again.
            bool keepShip = rules?.KeepShipOnDeath ?? true;
            var keepShipBtn = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () =>
            {
                Game?.Network?.SendSetWorldRules(keepShip: keepShip ? "Off" : "On");
                Invoke(nameof(RebuildList), 0.35f);
            });
            UiKit.AddText(keepShipBtn.transform, 16, 0, 520, 78, L("ui.worldopt.keep_ship"), 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(keepShipBtn.transform, 560, 0, 200, 78, keepShip ? L("ui.toggle.on") : L("ui.toggle.off"), 22,
                keepShip ? UiKit.Ok : UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 96f;

            // Auto-aim (world option, #693): when on (default) weapons acquire targets in a forward cone by
            // themselves; when off only what is actually under the crosshair can be hit — for everyone in
            // this world. The server enforces the admin gate and validates shots accordingly.
            bool autoAim = rules?.AutoAim ?? true;
            var autoAimBtn = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () =>
            {
                Game?.Network?.SendSetWorldRules(autoAim: autoAim ? "Off" : "On");
                Invoke(nameof(RebuildList), 0.35f);
            });
            UiKit.AddText(autoAimBtn.transform, 16, 0, 520, 78, L("ui.worldopt.auto_aim"), 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(autoAimBtn.transform, 560, 0, 200, 78, autoAim ? L("ui.toggle.on") : L("ui.toggle.off"), 22,
                autoAim ? UiKit.Ok : UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 96f;

            // Starter teleporter (world option, #1056): when on, every player who joins without a suit teleporter
            // is handed one (and everyone online gets one the moment it is switched on) — a multiplayer crew can
            // beam to allies on the same body and back to their ships without grinding the blueprint first.
            bool starterTp = rules?.StarterTeleporter ?? false;
            var starterTpBtn = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () =>
            {
                Game?.Network?.SendSetWorldRules(starterTeleporter: starterTp ? "Off" : "On");
                Invoke(nameof(RebuildList), 0.35f);
            });
            UiKit.AddText(starterTpBtn.transform, 16, 0, 520, 78, L("ui.worldopt.starter_teleporter"), 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(starterTpBtn.transform, 560, 0, 200, 78, starterTp ? L("ui.toggle.on") : L("ui.toggle.off"), 22,
                starterTp ? UiKit.Ok : UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
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
                _avatarPreview.SetFace(s.FacePixels);
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

        // --- Alliances tab (player alliances + radio/Funk chat) ---

        private float BuildAlliancesList()
        {
            _funkLog = null; // only the Funk view owns the live scrollback Text
            switch (_category)
            {
                case "find": return BuildAllianceFindList();
                case "funk": return BuildFunkLog();
                default: return BuildAllyList();
            }
        }

        /// <summary>The roster view: incoming requests (accept/decline), current allies (end), and outgoing requests.</summary>
        private float BuildAllyList()
        {
            var a = Game.Alliances ?? new AllianceList();
            float y = 0f;

            if (a.Incoming != null && a.Incoming.Length > 0)
            {
                y = AllianceSection(L("ui.alliance.incoming"), y);
                foreach (var r in a.Incoming)
                {
                    string id = r.PlayerId;
                    UiKit.AddText(_listContent, 8, y + 8, 420, 40, AllianceName(r.PlayerName, r.PlayerId), 22, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                    var acc = UiKit.AddButton(_listContent, 440, y + 6, 150, 44, L("ui.alliance.accept"), () => Game.Network?.SendAllianceResponse(id, true));
                    acc.GetComponent<Image>().color = new Color(0.2f, 0.5f, 0.36f);
                    UiKit.AddButton(_listContent, 600, y + 6, 150, 44, L("ui.alliance.decline"), () => Game.Network?.SendAllianceResponse(id, false));
                    y += 58f;
                }

                y += 10f;
            }

            y = AllianceSection(L("ui.alliance.cat_allies"), y);
            if (a.Allies == null || a.Allies.Length == 0)
            {
                UiKit.AddText(_listContent, 8, y, 760, 30, L("ui.alliance.none"), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                y += 40f;
            }
            else
            {
                foreach (var al in a.Allies)
                {
                    string id = al.PartnerId;
                    string dot = al.Online ? "<color=#52E0A0>●</color> " : "<color=#6A7480>●</color> ";
                    UiKit.AddText(_listContent, 8, y + 8, 560, 40, dot + AllianceName(al.PartnerName, al.PartnerId), 22, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                    UiKit.AddButton(_listContent, 600, y + 6, 150, 44, L("ui.alliance.end"), () => Game.Network?.SendDissolveAlliance(id));
                    y += 58f;
                }
            }

            if (a.Outgoing != null && a.Outgoing.Length > 0)
            {
                y += 10f;
                y = AllianceSection(L("ui.alliance.outgoing"), y);
                foreach (var r in a.Outgoing)
                {
                    UiKit.AddText(_listContent, 8, y, 760, 36, AllianceName(r.PlayerName, r.PlayerId) + "  —  " + L("ui.alliance.waiting"), 18, UiKit.CyanDim, TextAnchor.MiddleLeft);
                    y += 40f;
                }
            }

            return y;
        }

        /// <summary>The "find players" picker: online players you can still propose an alliance to (self, current
        /// allies and pending requests are filtered out). Player id == display name in this game, so the name is
        /// the target id. <see cref="StarMapData.Players"/> carries every online player, not just nearby ones.</summary>
        private float BuildAllianceFindList()
        {
            float y = AllianceSection(L("ui.alliance.cat_find"), 0f);
            var a = Game.Alliances ?? new AllianceList();

            var taken = new HashSet<string>();
            if (a.Allies != null) foreach (var al in a.Allies) taken.Add(al.PartnerId);
            if (a.Incoming != null) foreach (var r in a.Incoming) taken.Add(r.PlayerId);
            if (a.Outgoing != null) foreach (var r in a.Outgoing) taken.Add(r.PlayerId);

            string me = Game.LocalPlayerId ?? string.Empty;
            var players = Game.StarMap?.Players ?? System.Array.Empty<NetPlayerLocation>();
            var seen = new HashSet<string>();
            int shown = 0;
            foreach (var p in players)
            {
                string id = p.Name; // player id == name
                if (string.IsNullOrEmpty(id) || id == me || taken.Contains(id) || !seen.Add(id))
                {
                    continue;
                }

                // On official hosted worlds a "report player" button sits next to each name — kids need a
                // one-tap way to flag misbehaviour (reviewed by the operators; nobody is auto-punished).
                bool canReport = !string.IsNullOrEmpty(Game.HostedToken) && !string.IsNullOrEmpty(Game.PortalSession);
                float nameW = canReport ? 340 : 480;
                UiKit.AddText(_listContent, 8, y + 8, nameW, 40, AllianceName(p.Name, id), 22, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                var btn = UiKit.AddButton(_listContent, canReport ? 360 : 500, y + 6, canReport ? 220 : 250, 44,
                    L("ui.alliance.propose"), () => Game.Network?.SendRequestAlliance(id));
                btn.GetComponent<Image>().color = new Color(0.2f, 0.45f, 0.7f);
                if (canReport)
                {
                    Button reportBtn = null;
                    reportBtn = UiKit.AddButton(_listContent, 592, y + 6, 160, 44, L("ui.portal.report"), () => ReportPlayer(id, reportBtn));
                    reportBtn.GetComponent<Image>().color = new Color(0.55f, 0.25f, 0.2f);
                }

                y += 58f;
                shown++;
            }

            if (shown == 0)
            {
                UiKit.AddText(_listContent, 8, y, 760, 30, L("ui.alliance.no_players"), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                y += 40f;
            }

            return y;
        }

        /// <summary>Files a player report against the worlds portal (official hosted worlds only — the button
        /// exists only when a portal session + hosted join are present). One tap, category "other"; the
        /// button itself becomes the confirmation. Reports are reviewed by the operators, never auto-punish.</summary>
        private async void ReportPlayer(string playerName, Button button)
        {
            string portalUrl = Game.PortalUrl;
            string session = Game.PortalSession;
            string worldId = Game.HostedWorldId;
            var portal = new PortalClient(portalUrl);
            var result = await Task.Run(() => portal.Report(session, playerName, "other", "in-game report", worldId));
            if (button == null)
            {
                return; // menu closed while the request ran
            }

            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = result.Ok ? L("ui.portal.reported") : L("ui.portal.report_failed");
            }

            if (result.Ok)
            {
                button.interactable = false; // one report per player per menu visit is plenty
            }
        }

        /// <summary>The radio (Funk) scrollback in the list pane — a live Text refreshed each frame (see Update),
        /// reusing the existing global chat feed. The input + send button live in the detail pane.</summary>
        private float BuildFunkLog()
        {
            _funkLog = UiKit.AddText(_listContent, 0, 0, 780, 700, ComposeFunkLog(), 18, UiKit.TextCol, TextAnchor.UpperLeft);
            _funkLog.horizontalOverflow = HorizontalWrapMode.Wrap;
            _funkLog.verticalOverflow = VerticalWrapMode.Overflow;
            _funkLog.supportRichText = true;
            return 700f;
        }

        private string ComposeFunkLog()
        {
            var chat = Game?.RecentChat;
            if (chat == null || chat.Count == 0)
            {
                return L("ui.funk.empty");
            }

            var sb = new System.Text.StringBuilder();
            int from = Mathf.Max(0, chat.Count - 30);
            for (int i = from; i < chat.Count; i++)
            {
                sb.AppendLine($"<b>{chat[i].Sender}:</b> {chat[i].Text}");
            }

            return sb.ToString();
        }

        private float DetailAlliances()
        {
            if (_category == "funk")
            {
                return DetailFunk();
            }

            float y = 0f;
            UiKit.AddText(_detail, 8, y, 620, 32, L("ui.alliance.title"), 24, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 44f;
            UiKit.AddText(_detail, 8, y, 620, 260, L("ui.alliance.about"), 17, UiKit.TextCol, TextAnchor.UpperLeft);
            y += 270f;
            return y;
        }

        private float DetailFunk()
        {
            float y = 0f;
            UiKit.AddText(_detail, 8, y, 620, 32, L("ui.funk.title"), 24, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 44f;
            UiKit.AddText(_detail, 8, y, 620, 60, L("ui.funk.hint"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
            y += 70f;
            UiKit.AddInput(_detail, 8, y, 620, 46, _funkDraft, v => _funkDraft = v, L("ui.funk.placeholder"));
            y += 56f;
            var send = UiKit.AddButton(_detail, 8, y, 220, 48, L("ui.funk.send"), SendFunk);
            send.GetComponent<Image>().color = new Color(0.2f, 0.5f, 0.36f);
            y += 60f;
            return y;
        }

        private void SendFunk()
        {
            string t = (_funkDraft ?? string.Empty).Trim();
            if (t.Length == 0)
            {
                return;
            }

            Game.Network?.SendChat(t);
            _funkDraft = string.Empty;
            RebuildDetail(); // clear the input box
        }

        /// <summary>A non-selectable section heading inside the Alliances list; returns the y below it.</summary>
        private float AllianceSection(string text, float y)
        {
            UiKit.AddText(_listContent, 4, y, 760, 30, text, 18, UiKit.Cyan, TextAnchor.LowerLeft, FontStyle.Bold);
            return y + 36f;
        }

        private static string AllianceName(string name, string id) => string.IsNullOrEmpty(name) ? id : name;

        // --- Companions tab (tamed creatures): roster with rename + release; design docs/developer/CREATURE_TAMING.md ---

        /// <summary>Per-companion in-progress rename text (keyed by companion id), so typing survives a rebuild.</summary>
        private readonly System.Collections.Generic.Dictionary<string, string> _companionDraft = new();

        private float BuildCompanionsList()
        {
            var all = Game.Companions?.Companions ?? System.Array.Empty<NetCompanion>();
            bool hereOnly = _category != "all";
            var shown = all.Where(c => !hereOnly || c.Present).ToList();

            float y = 0f;
            if (shown.Count == 0)
            {
                UiKit.AddText(_listContent, 8, y, 760, 64, L("ui.companions.empty"), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                return y + 74f;
            }

            foreach (var c in shown)
            {
                string id = c.Id;
                string dot = c.Present ? "<color=#52E0A0>●</color> " : "<color=#6A7480>●</color> ";
                string title = string.IsNullOrEmpty(c.Name) ? c.SpeciesName : c.Name;
                UiKit.AddText(_listContent, 8, y + 4, 700, 26, dot + title, 22, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);

                string state = c.Present ? L("ui.companions.present") : L("ui.companions.away");
                string sub = $"{c.SpeciesName}  ·  {L("ui.companions.home")}: {c.HomeBodyName}  ·  {L("ui.companions.bond")} {c.Bond}  ·  {state}";
                UiKit.AddText(_listContent, 28, y + 30, 700, 22, sub, 14, UiKit.CyanDim, TextAnchor.MiddleLeft);

                string draft = _companionDraft.TryGetValue(id, out var d) ? d : c.Name;
                var field = UiKit.AddInput(_listContent, 8, y + 56, 360, 44, draft, v => _companionDraft[id] = v, L("ui.companion.placeholder"));
                field.characterLimit = 24;
                field.lineType = InputField.LineType.SingleLine;
                UiKit.AddButton(_listContent, 376, y + 56, 150, 44, L("ui.companions.rename"), () =>
                {
                    string nm = _companionDraft.TryGetValue(id, out var dd) ? dd : c.Name;
                    Game.Network?.SendSetCompanionName(id, nm);
                });
                var rel = UiKit.AddButton(_listContent, 536, y + 56, 150, 44, L("ui.companions.release"), () => Game.Network?.SendReleaseCompanion(id));
                rel.GetComponent<Image>().color = new Color(0.5f, 0.22f, 0.22f);

                y += 116f;
            }

            return y;
        }

        // --- Photos tab (local camera gallery: thumbnails + editable per-photo note) ---

        private PhotoStore _photoStore;
        private long _photoSeed = long.MinValue;
        private readonly System.Collections.Generic.Dictionary<string, string> _photoNoteDraft = new();

        /// <summary>The photo store for the current world (cached; rebuilt when the world seed changes), always
        /// re-scanned so shots taken since the menu opened appear.</summary>
        private PhotoStore PhotoStoreNow()
        {
            long seed = Game != null ? Game.WorldSeed : 0L;
            if (_photoStore == null || _photoSeed != seed)
            {
                _photoStore?.UnloadTextures();
                _photoStore = PhotoStore.Open(seed);
                _photoSeed = seed;
            }
            else
            {
                _photoStore.Reload();
            }

            return _photoStore;
        }

        private static string PhotoWhen(PhotoStore.Entry e)
        {
            try { return new System.DateTime(e.TakenUtcTicks, System.DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm"); }
            catch { return string.Empty; }
        }

        /// <summary>Places a photo texture into the parent, letter-boxed to fit the given box (keeps aspect).</summary>
        private static void AddPhoto(Transform parent, float x, float y, float boxW, float boxH, Texture2D tex)
        {
            float w = boxW, h = boxH;
            if (tex != null && tex.width > 0 && tex.height > 0)
            {
                float ar = (float)tex.width / tex.height;
                if (boxW / boxH > ar) { w = boxH * ar; }
                else { h = boxW / ar; }
            }

            var go = new GameObject("Photo", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, x + (boxW - w) * 0.5f, y + (boxH - h) * 0.5f, w, h);
            var ri = go.AddComponent<RawImage>();
            ri.texture = tex;
            ri.color = tex != null ? Color.white : new Color(0.1f, 0.16f, 0.24f, 1f);
            ri.raycastTarget = false;
        }

        private float BuildPhotosList()
        {
            var store = PhotoStoreNow();
            var shots = store.Entries;

            float y = 0f;
            UiKit.AddText(_listContent, 8, y, 760, 28, string.Format(L("ui.photos.count"), shots.Count), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 34f;

            if (shots.Count == 0)
            {
                var empty = UiKit.AddText(_listContent, 8, y, 760, 80, L("ui.photos.empty"), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                empty.horizontalOverflow = HorizontalWrapMode.Wrap;
                return y + 90f;
            }

            // Selecting nothing yet? Land on the newest so the preview pane isn't empty on open.
            if (string.IsNullOrEmpty(_selected) || shots.All(s => s.File != _selected))
            {
                _selected = shots[0].File;
            }

            foreach (var e in shots)
            {
                string file = e.File;
                var card = UiKit.AddButton(_listContent, 0, y, 780, 104, string.Empty, () => { _selected = file; RebuildList(); RebuildDetail(); });
                if (_selected == file)
                {
                    card.GetComponent<Image>().color = UiKit.Cyan;
                }

                AddPhoto(card.transform, 10, 10, 148, 84, store.GetTexture(file));
                UiKit.AddText(card.transform, 172, 12, 596, 26, PhotoWhen(e), 18, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                string note = string.IsNullOrEmpty(e.Note) ? L("ui.photos.note_placeholder") : e.Note;
                var nt = UiKit.AddText(card.transform, 172, 42, 596, 52, note, 15,
                    string.IsNullOrEmpty(e.Note) ? UiKit.CyanDim : UiKit.TextCol, TextAnchor.UpperLeft);
                nt.horizontalOverflow = HorizontalWrapMode.Wrap;

                y += 112f;
            }

            return y;
        }

        private float BuildPhotosDetail()
        {
            var store = PhotoStoreNow();
            var e = string.IsNullOrEmpty(_selected) ? null : store.Entries.FirstOrDefault(x => x.File == _selected);
            if (e == null)
            {
                var hint = UiKit.AddText(_detail, 8, 16, 620, 120, L("ui.photos.select_hint"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
                hint.horizontalOverflow = HorizontalWrapMode.Wrap;
                return 140f;
            }

            float y = 8f;
            UiKit.AddText(_detail, 8, y, 620, 28, L("ui.photos.taken") + ": " + PhotoWhen(e), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 36f;

            AddPhoto(_detail, 8, y, 624, 360, store.GetTexture(e.File));
            y += 372f;

            UiKit.AddText(_detail, 8, y, 620, 24, L("ui.photos.note"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 28f;

            string draft = _photoNoteDraft.TryGetValue(e.File, out var d) ? d : e.Note;
            string file = e.File;
            var field = UiKit.AddInput(_detail, 8, y, 624, 96, draft, v => _photoNoteDraft[file] = v, L("ui.photos.note_placeholder"), 280);
            field.lineType = InputField.LineType.MultiLineNewline;
            var fieldText = field.textComponent;
            if (fieldText != null) { fieldText.alignment = TextAnchor.UpperLeft; }
            y += 104f;

            UiKit.AddButton(_detail, 8, y, 200, 46, L("ui.photos.save_note"), () =>
            {
                string nm = _photoNoteDraft.TryGetValue(file, out var dd) ? dd : string.Empty;
                store.SetNote(file, nm);
                RebuildList(); // refresh the list note preview
            });

            var del = UiKit.AddButton(_detail, 432, y, 200, 46, L("ui.photos.delete"), () =>
            {
                store.Delete(file);
                _photoNoteDraft.Remove(file);
                _selected = string.Empty;
                RebuildList();
                RebuildDetail();
            });
            del.GetComponent<Image>().color = new Color(0.5f, 0.22f, 0.22f);

            return y + 60f;
        }

        // --- Story Log tab (read-only: progress meter + VEGA beats + recovered net fragments + memories) ---

        /// <summary>
        /// The achievements page: every achievement grouped by category, with a progress bar and an "x/y" count
        /// so it doubles as a "what can I do next?" list — a player asked for achievements with rewards, and the
        /// progress is what makes an unearned one useful rather than just a locked box.
        /// <para>
        /// Rows are laid out at ABSOLUTE offsets (no LayoutGroup): a VerticalLayoutGroup with wrapped text
        /// overflows its rows here, which is a trap this codebase has hit before.
        /// </para>
        /// </summary>
        private float BuildAchievementList()
        {
            float y = 8f;
            var all = Game?.Achievements;
            if (all == null || all.Length == 0)
            {
                UiKit.AddText(_listContent, 8, y, 760, 34, L("ui.achv.none"), 20, UiKit.CyanDim, TextAnchor.MiddleLeft);
                return y + 44f;
            }

            int done = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].Earned) done++;
            }

            UiKit.AddText(_listContent, 8, y, 760, 36,
                L("ui.achv.summary").Replace("{done}", done.ToString()).Replace("{total}", all.Length.ToString()),
                24, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 46f;

            // Group by category, keeping the authored order inside each group.
            var seen = new System.Collections.Generic.List<string>();
            foreach (var a in all)
            {
                string cat = a.Category ?? string.Empty;
                if (!seen.Contains(cat)) seen.Add(cat);
            }

            foreach (var cat in seen)
            {
                y = AllianceSection(string.IsNullOrEmpty(cat) ? L("ui.achv.title") : L("achv.category." + cat), y);

                foreach (var a in all)
                {
                    if ((a.Category ?? string.Empty) != cat)
                    {
                        continue;
                    }

                    y = AchievementRow(y, a);
                }

                y += 8f;
            }

            return y + 12f;
        }

        /// <summary>One achievement row: name, description, a filled bar and the tally (or "Done").</summary>
        private float AchievementRow(float y, BlocksBeyondTheStars.Networking.Messages.NetAchievement a)
        {
            const float RowW = 760f;
            var nameCol = a.Earned ? UiKit.Ok : UiKit.TextCol;

            UiKit.AddText(_listContent, 8, y, RowW - 150f, 28, (a.Earned ? "✓ " : "") + L($"achv.{a.Key}.name"),
                20, nameCol, TextAnchor.MiddleLeft, FontStyle.Bold);

            string tally = a.Earned
                ? L("ui.achv.done")
                : L("ui.achv.progress")
                    .Replace("{done}", a.Progress.ToString())
                    .Replace("{total}", a.Target.ToString());
            UiKit.AddText(_listContent, RowW - 140f, y, 140f, 28, tally, 18,
                a.Earned ? UiKit.Ok : UiKit.CyanDim, TextAnchor.MiddleRight);
            y += 28f;

            UiKit.AddText(_listContent, 20, y, RowW - 28f, 24, L($"achv.{a.Key}.desc"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 26f;

            // Progress bar: a dim track with a filled portion on top. Earned rows read as full and green.
            float frac = a.Target > 0 ? Mathf.Clamp01((float)a.Progress / a.Target) : 0f;
            if (a.Earned)
            {
                frac = 1f;
            }

            UiKit.AddImage(_listContent, 20, y, RowW - 28f, 8f, null, new Color(1f, 1f, 1f, 0.10f));
            if (frac > 0f)
            {
                UiKit.AddImage(_listContent, 20, y, (RowW - 28f) * frac, 8f, null,
                    a.Earned ? UiKit.Ok : UiKit.Cyan);
            }

            return y + 20f;
        }

        private float BuildStoryList()
        {
            float y = 8f;
            var s = Game?.Story;
            bool storyOn = s != null && s.Active;

            if (storyOn)
            {
                int pct = s.ProgressTarget > 0 ? Mathf.Clamp(Mathf.RoundToInt(100f * s.Progress / s.ProgressTarget), 0, 100) : 0;
                UiKit.AddText(_listContent, 8, y, 760, 36, L("ui.story.meter") + ": " + pct + "%", 24, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
                y += 44f;
                UiKit.AddText(_listContent, 8, y, 760, 28,
                    L("ui.story.fragments") + ": " + s.FragmentsFound + "   ·   " + L("ui.story.kills") + ": " + s.MachineKills + "   ·   " + L("ui.story.beats") + ": " + s.BeatsRevealed,
                    16, UiKit.CyanDim, TextAnchor.MiddleLeft);
                y += 40f;

                y = AllianceSection(L("ui.story.beats"), y);
                y = StoryEntries(y, Game.StoryLogBeats);

                y += 10f;
                y = AllianceSection(L("ui.story.fragments"), y);
                if (Game.StoryLogFragments == null || Game.StoryLogFragments.Count == 0)
                {
                    y = StoryEmpty(y);
                }
                else
                {
                    foreach (var (cat, key) in Game.StoryLogFragments)
                    {
                        y = StoryEntry(y, "[" + IdLabel("lore.cat.", cat) + "] " + L(key));
                    }
                }

                y += 10f;
                y = AllianceSection(L("ui.story.memories"), y);
                y = StoryEntries(y, Game.StoryLogMemories);
            }
            else
            {
                UiKit.AddText(_listContent, 8, y, 760, 34, L("ui.story.off"), 20, UiKit.CyanDim, TextAnchor.MiddleLeft);
                y += 44f;
            }

            // VEGA tips (#737): every onboarding lesson and advisor hint already received, rebuilt from the
            // server's milestone snapshot — a dismissed tip used to be gone for good. Shown with or without
            // an active story pack (the tutorial runs in the sandbox too).
            y += 10f;
            y = AllianceSection(L("ui.story.vega"), y);
            bool anyTip = false;
            if (Game?.VegaLogKeys != null)
            {
                foreach (var key in Game.VegaLogKeys)
                {
                    if (Game?.Localizer == null || !Game.Localizer.Has(key))
                    {
                        continue; // a future server hint this client has no translation for — no raw "[key]" (#428)
                    }

                    y = StoryEntry(y, L(key));
                    anyTip = true;
                }
            }

            if (!anyTip)
            {
                UiKit.AddText(_listContent, 12, y, 756, 28, L("ui.story.vega.none"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
                y += 34f;
            }

            return y + 12f;
        }

        private float StoryEntries(float y, System.Collections.Generic.List<string> keys)
        {
            if (keys == null || keys.Count == 0)
            {
                return StoryEmpty(y);
            }

            foreach (var k in keys)
            {
                y = StoryEntry(y, L(k));
            }

            return y;
        }

        private static readonly TextGenerator StoryMeasurer = new TextGenerator();

        private float StoryEntry(float y, string text)
        {
            // Measured height instead of the old length/64 guess (which underestimated long German lines),
            // and wrap actually enabled — the UiKit default (Overflow) let a long entry run past the pane.
            string row = "• " + text;
            var settings = new TextGenerationSettings
            {
                font = UiKit.Font,
                fontSize = 17,
                fontStyle = FontStyle.Normal,
                richText = false,
                scaleFactor = 1f,
                lineSpacing = 1f,
                horizontalOverflow = HorizontalWrapMode.Wrap,
                verticalOverflow = VerticalWrapMode.Overflow,
                generationExtents = new Vector2(756f, 0f),
                textAnchor = TextAnchor.UpperLeft,
                pivot = new Vector2(0f, 1f),
                color = Color.white,
            };
            float h = 8f + StoryMeasurer.GetPreferredHeight(row, settings);
            var t = UiKit.AddText(_listContent, 12, y, 756, h, row, 17, UiKit.TextCol, TextAnchor.UpperLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return y + h + 6f;
        }

        private float StoryEmpty(float y)
        {
            UiKit.AddText(_listContent, 12, y, 756, 28, L("ui.story.none"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
            return y + 34f;
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
                _discardArmed = string.Empty; // navigating away disarms a half-confirmed discard (#599)
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

            // The Alliances detail pane is informational (allies/find) or the radio input (funk) — shown even with
            // nothing selected, so branch before the generic "pick an entry" placeholder.
            if (_mode == Mode.Alliances)
            {
                SetContentHeight(_detail, DetailAlliances());
                return;
            }

            // The Companions list carries its own per-row actions (rename/release), so the detail pane is a short
            // informational blurb rather than the generic "pick an entry" placeholder.
            if (_mode == Mode.Companions)
            {
                UiKit.AddText(_detail, 8, 16, 620, 30, L("ui.companions.title"), 22, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
                var info = UiKit.AddText(_detail, 8, 54, 620, 160, L("ui.companions.empty"), 15, UiKit.CyanDim, TextAnchor.UpperLeft);
                info.horizontalOverflow = HorizontalWrapMode.Wrap;
                SetContentHeight(_detail, 220);
                return;
            }

            // The Photos gallery: the detail pane is the full-size preview + editable note + delete for the
            // selected photo (or a hint when none is picked yet).
            if (_mode == Mode.Photos)
            {
                SetContentHeight(_detail, BuildPhotosDetail());
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
            if (_selected.StartsWith("color:", System.StringComparison.Ordinal))
            {
                return DetailColor();
            }

            if (_selected.StartsWith("shape:", System.StringComparison.Ordinal))
            {
                return DetailShape();
            }

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
                y = IngredientRow(inp, y, 20);
            }

            y += 8f;
            // "Station: [icon] Workbench ✓/✗" — the BLOCK's name and tile (#1071), ticked when the server says
            // it is in reach right now (hand/market/factory keep their own wording).
            {
                string stKey = StationKeyOf(r.Station);
                bool gated = r.Station != CraftingStation.Hand && r.Station != CraftingStation.Market && r.Station != CraftingStation.Factory;
                var stSprite = gated ? StationSprite(stKey) : null;
                float sx = 8f;
                string label = L("ui.craft.station") + ": ";
                if (stSprite != null)
                {
                    UiKit.AddText(_detail, sx, y, 100, 26, label, 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                    sx += 92f;
                    UiKit.AddIconSprite(_detail, sx, y + 1, 22, stSprite, Color.white);
                    sx += 26f;
                    label = string.Empty;
                }

                string stName = gated ? StationName(stKey) : L("ui.craft.station_" + stKey);
                bool ok = !gated || StationAvailable(r.Station);
                UiKit.AddText(_detail, sx, y, 620 - sx, 26, label + stName + (gated ? (ok ? "  ✓" : "  ✗") : string.Empty), 18,
                    ok ? UiKit.CyanDim : new Color(1f, 0.8f, 0.4f), TextAnchor.UpperLeft);
            }

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
            UiKit.AddButton(_detail, 214, y, 74, 56, L("ui.craft.max"), () => { _craftCount = maxCraft; RebuildDetail(); });
            y += 66f;

            int n = _craftCount;
            var btn = UiKit.AddButton(_detail, 8, y, 280, 56, L("ui.action.craft") + (maxCraft > 1 ? " ×" + n : string.Empty), () => { Game.Network.SendCraft(r.Key, n); });
            SetInteractable(btn, can);
            y += 70f;
            return y;
        }

        /// <summary>How many of a recipe the player can currently afford, from their owned inputs. Capped at a
        /// full stack — that is also what the server accepts in one craft order.</summary>
        private int MaxCraftable(RecipeDefinition r)
        {
            int cap = BlocksBeyondTheStars.Shared.Definitions.ItemDefinition.DefaultMaxStack;
            int m = cap;
            foreach (var inp in r.Inputs)
            {
                if (inp.Count <= 0)
                {
                    continue;
                }

                m = Mathf.Min(m, Owned(inp.Item) / inp.Count);
            }

            return Mathf.Clamp(m, 0, cap);
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

            if (bp.UnlockCost.Count > 0 || bp.KnowledgeCost > 0)
            {
                y += 6f;
                UiKit.AddText(_detail, 8, y, 620, 26, L("ui.tech.cost"), 20, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
                y += 30f;
                // Knowledge points (earned by scanning) are a separate cost from the material list — show it too,
                // otherwise the player can't see why a researched-looking node still won't unlock.
                if (bp.KnowledgeCost > 0)
                {
                    bool kok = Game.Knowledge >= bp.KnowledgeCost;
                    UiKit.AddText(_detail, 20, y, 620, 26, $"{(kok ? "✓" : "✗")} {L("ui.tech.knowledge")}  {Game.Knowledge}/{bp.KnowledgeCost}", 18,
                        kok ? UiKit.Ok : new Color(1f, 0.5f, 0.5f), TextAnchor.UpperLeft);
                    y += 28f;
                }

                foreach (var c in bp.UnlockCost)
                {
                    y = IngredientRow(c, y, 18);
                }
            }

            y += 10f;
            bool already = Game.UnlockedBlueprints.Contains(bp.Key);
            bool ready = !already && bp.Prerequisites.All(Game.UnlockedBlueprints.Contains) && HasAll(bp.UnlockCost)
                       && Game.Knowledge >= bp.KnowledgeCost;
            // Research is done at the cockpit (#1074): the server rejects elsewhere, so the button says so
            // instead of staying live and failing with a toast.
            bool can = ready && ResearchOkNow();
            if (ready && !ResearchOkNow())
            {
                UiKit.AddText(_detail, 8, y, 620, 26, NeedStationText("research"), 18, new Color(1f, 0.8f, 0.4f), TextAnchor.UpperLeft);
                y += 30f;
            }

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

                // A self-built ship has no content definition — show its geometry-derived flight stats
                // from the fleet message instead (#949).
                if (def == null && (s.FlightSpeed > 0f || s.Handling > 0f))
                {
                    UiKit.AddText(_detail, 8, y, 620, 26,
                        $"{L("ui.ship.speed")}: {s.FlightSpeed:0.0}    {L("ui.ship.handling")}: {s.Handling:0.0}", 20,
                        UiKit.TextCol, TextAnchor.UpperLeft);
                    y += 36f;
                }

                // An un-commissioned construction can't be switched to — it isn't a ship yet (#950).
                if (!s.Active && s.Commissioned)
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
                bool ready = HasAll(m.BuildCost) && BlueprintOk(m.RequiredBlueprint);
                // Modules are built aboard, at the workshop module (#1074) — say so instead of a failing toast.
                bool can = ready && ShipBuildOkNow();
                if (ready && !ShipBuildOkNow())
                {
                    UiKit.AddText(_detail, 8, y, 620, 26, NeedStationText("shipbuild"), 18, new Color(1f, 0.8f, 0.4f), TextAnchor.UpperLeft);
                    y += 30f;
                }

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

        /// <summary>The detail entry whose Discard button is currently armed, or "" when none is (#599). Cleared
        /// whenever the detail pane switches to another entry, so an armed button never survives navigation.</summary>
        private string _discardArmed = string.Empty;

        /// <summary>The first slot holding <paramref name="item"/> in the backpack (or the ship's hold), or -1.
        /// The discard intent addresses a slot, since a dyed/shaped stack carries a composite item key.</summary>
        private int SlotOfItem(string item, bool inCargo)
        {
            var slots = inCargo ? Game.Cargo : Game.Personal;
            if (slots != null)
            {
                foreach (var s in slots)
                {
                    if (s.Item == item)
                    {
                        return s.Slot;
                    }
                }
            }

            return -1;
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

            // Cargo transfer: move this one item between the personal inventory and the ship's hold (aboard only).
            // Direction follows the tab you're viewing it from — cargo view pulls it out, personal view stows it.
            if (AboardShipNow())
            {
                bool fromCargo = _category == "cargo";
                UiKit.AddButton(_detail, 8, y, 320, 46, L(fromCargo ? "ui.cargo.to_inventory" : "ui.cargo.to_cargo"),
                    () => Game.Network?.SendMoveCargoItem(toCargo: !fromCargo, item: item, bulkAll: false));
                y += 54f;
            }

            // Discard (#599): the only action that actually destroys an item — every other one just moves it
            // somewhere else. Two-step, because it cannot be undone: the first click arms the button, the second
            // one sends. The starter kit gets no button at all (and the server refuses it anyway — it never
            // trusts the client). Works on the cargo tab too: the hold is where "stow all" piles the junk up.
            bool inCargo = _category == "cargo";
            int discardSlot = SlotOfItem(item, inCargo);
            if (discardSlot >= 0 && !StarterKit.IsProtected(item))
            {
                bool armed = _discardArmed == _selected;
                var discardBtn = UiKit.AddButton(_detail, 8, y, 320, 46,
                    L(armed ? "ui.inventory.discard_confirm" : "ui.inventory.discard"),
                    () =>
                    {
                        if (!armed)
                        {
                            _discardArmed = _selected; // first click only asks
                            RebuildDetail();
                            return;
                        }

                        _discardArmed = string.Empty;
                        Game.Network?.SendDiscardItem(discardSlot, inCargo);
                        _selected = string.Empty; // the entry is about to vanish from the list
                        RebuildList();
                        RebuildDetail();
                    });
                var discardImg = discardBtn.GetComponent<Image>();
                if (discardImg != null)
                {
                    discardImg.color = armed ? new Color(0.62f, 0.20f, 0.20f) : new Color(0.40f, 0.26f, 0.26f);
                }

                y += 54f;
                if (armed)
                {
                    var warn = UiKit.AddText(_detail, 8, y, 620, 48, L("ui.inventory.discard_warn"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
                    warn.horizontalOverflow = HorizontalWrapMode.Wrap;
                    y += 52f;
                }
            }

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
                // The server disassembles wherever the WORKSHOP station is available (base workbench or the
                // ship's workshop module) — same set as crafting (#1070).
                bool atWorkshop = StationAvailable(CraftingStation.Workshop);
                bool can = anyYield && atWorkshop && Owned(item) >= 1;
                var btn = UiKit.AddButton(_detail, 8, y, 280, 50, L("ui.action.disassemble"), () => { Game.Network.SendDisassemble(item); });
                SetInteractable(btn, can);
                y += 56f;
                if (anyYield && !atWorkshop)
                {
                    UiKit.AddText(_detail, 8, y, 620, 24, NeedStationText("workshop"), 16, new Color(1f, 0.8f, 0.4f), TextAnchor.UpperLeft);
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
                _systemMap.Show(sys.Bodies, map.ActiveLocationId, sel, MarkerColorOf);
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

            bool isStation = body.Kind == "SpaceStation";
            UiKit.AddText(_detail, 8, y, 620, 40, body.Name, 30, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 48f;
            UiKit.AddText(_detail, 8, y, 620, 28, $"{L("ui.map.kind")}: {(isStation ? L("ui.map.kind_station") : IdLabel("ui.map.kind_", body.Kind))}", 20, UiKit.CyanDim, TextAnchor.UpperLeft);
            y += 32f;
            if (!string.IsNullOrEmpty(body.PlanetType))
            {
                UiKit.AddText(_detail, 8, y, 620, 28, $"{L("ui.map.type")}: {L($"planet.{body.PlanetType}.name")}", 20, UiKit.CyanDim, TextAnchor.UpperLeft);
                y += 32f;
            }

            bool here = body.Id == map.ActiveLocationId;
            UiKit.AddText(_detail, 8, y, 620, 28, here ? L("ui.map.here") : IdLabel("ui.map.status_", body.Status), 20, here ? UiKit.Cyan : UiKit.CyanDim, TextAnchor.UpperLeft);
            y += 40f;

            // Colour-mark this body. Asked for by a player who wanted to mark planets in space in different
            // colours: unlike the single surface waypoint, any number of bodies can carry a mark at once, and the
            // star map haloes each in its colour. A fixed named palette (not a colour picker) keeps it readable
            // and translatable. Purely local — no server involvement.
            int mark = MarkerColorOf(body.Id);
            string markLabel = mark >= 0 && mark < PlanetMarkerPalette.Count
                ? L("ui.marker.marked") + ": " + L(PlanetMarkerPalette.NameKeys[mark])
                : L("ui.marker.unmarked");
            var markBtn = UiKit.AddButton(_detail, 8, y, 330, 44, markLabel, () => CyclePlanetMarker(body.Id));
            if (mark >= 0 && mark < PlanetMarkerPalette.Count && markBtn != null)
            {
                // Tint the button itself so the current choice is visible without reading the label.
                var img = markBtn.GetComponent<UnityEngine.UI.Image>();
                if (img != null)
                {
                    img.color = Color.Lerp(img.color, PlanetMarkerPalette.Colors[mark], 0.55f);
                }
            }

            if (mark >= 0)
            {
                UiKit.AddButton(_detail, 346, y, 180, 44, L("ui.marker.clear"), () => SetPlanetMarker(body.Id, -1));
            }

            y += 52f;

            // A space station: show its owner, board it (if visited), and rename it (if it's yours).
            if (isStation)
            {
                bool mine = !string.IsNullOrEmpty(body.OwnerName) && body.OwnerName == Game.LocalPlayerId;
                if (!string.IsNullOrEmpty(body.OwnerName))
                {
                    UiKit.AddText(_detail, 8, y, 620, 28,
                        mine ? "◆ " + L("ui.map.your_station") : $"{L("ui.map.owner")}: {body.OwnerName}",
                        20, mine ? UiKit.Cyan : UiKit.CyanDim, TextAnchor.UpperLeft);
                    y += 36f;
                }

                if (!here)
                {
                    if (TravelUnlocked(body))
                    {
                        var boardBtn = UiKit.AddButton(_detail, 8, y, 280, 56, L("ui.map.board"), () => Game.Network?.SendTravel(body.Id));
                        if (!AboardShipNow())
                        {
                            SetInteractable(boardBtn, false); // board your ship before travelling
                        }

                        y += 64f;
                    }
                    else
                    {
                        UiKit.AddText(_detail, 8, y, 600, 50, L("ui.map.visit_to_unlock"), 18, new Color(1f, 0.8f, 0.45f), TextAnchor.UpperLeft);
                        y += 58f;
                    }
                }

                if (mine)
                {
                    y = AddRenameRow(y, body.Name, L("ui.map.rename"), name => Game.Network?.SendSetStationName(body.Id, name));
                }

                return y;
            }

            // A landable world: show your own claims on it (a station orbiting and/or a base on the surface).
            if (Game.HasMyStation(body.Id))
            {
                UiKit.AddText(_detail, 8, y, 620, 28, "◆ " + L("ui.map.station_here"), 20, UiKit.Cyan, TextAnchor.UpperLeft);
                y += 32f;
            }

            string myBase = Game.MyBaseName(body.Id);
            if (myBase != null)
            {
                string label = "⌂ " + L("ui.map.base_here") + (string.IsNullOrEmpty(myBase) ? string.Empty : ": " + myBase);
                UiKit.AddText(_detail, 8, y, 620, 28, label, 20, UiKit.Cyan, TextAnchor.UpperLeft);
                y += 36f;
                y = AddRenameRow(y, myBase, L("ui.map.rename_base"), name => Game.Network?.SendSetBaseName(body.Id, name));
            }

            if (here || string.IsNullOrEmpty(body.PlanetType))
            {
                return y; // you're already here, or it isn't a landable world (belts dock differently)
            }

            if (TravelUnlocked(body))
            {
                // A reachable destination — quick-travel (a cross-system one is a hyperspace jump).
                var destSystem = map.Systems.FirstOrDefault(s => s.Bodies.Any(b => b.Id == body.Id));
                bool crossSystem = destSystem != null && destSystem.Id != CurrentSystemId();
                var travelBtn = UiKit.AddButton(_detail, 8, y, 280, 56, crossSystem ? L("ui.map.hyperjump") : L("ui.map.travel"), () => Game.Network?.SendTravel(body.Id));
                if (!AboardShipNow())
                {
                    SetInteractable(travelBtn, false); // travel happens from your ship — board it first
                }

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

        /// <summary>An inline name field + confirm button for renaming an owned station or base from the Map detail
        /// pane (no modal, so it never desyncs the menu's own open/cursor state). Sends the typed name on click.</summary>
        private float AddRenameRow(float y, string current, string buttonLabel, System.Action<string> onConfirm)
        {
            var field = UiKit.AddInput(_detail, 8, y, 400, 48, current ?? string.Empty, null, L("ui.base.placeholder"));
            field.characterLimit = 24;
            field.lineType = InputField.LineType.SingleLine;
            UiKit.AddButton(_detail, 416, y, 180, 48, buttonLabel, () => onConfirm?.Invoke(field.text ?? string.Empty));
            return y + 56f;
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
                // Wrap the flavour/instructions so long objectives (e.g. the first-iron hint
                // "…dig straight down to find it.") are not clipped by the RectMask2D viewport,
                // and advance y by the actual wrapped height so it never overlaps the objectives.
                var desc = UiKit.AddText(_detail, 8, y, 620, 60, m2.FreeText ? m2.Description : L(m2.Description), 17, UiKit.CyanDim, TextAnchor.UpperLeft);
                desc.horizontalOverflow = HorizontalWrapMode.Wrap;
                y += Mathf.Max(64f, desc.preferredHeight + 8f);
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

        /// <summary>One ✓/✗ have/need ingredient row plus a source tag (#1016): tells the player whether the
        /// material is itself craftable or a raw resource to find in the world. A craftable ingredient the
        /// player is still short of also lists what crafting the missing amount takes, indented beneath —
        /// one recipe level deep, enough to see what to actually gather without rendering a whole tree.</summary>
        private float IngredientRow(ItemAmount inp, float y, int size)
        {
            int have = Owned(inp.Item);
            bool ok = have >= inp.Count;
            bool craftable = Game.Content.CraftDepth(inp.Item) > 0;
            UiKit.AddText(_detail, 20, y, 620, size + 8, $"{(ok ? "✓" : "✗")} {ItemName(inp.Item)}  {have}/{inp.Count}", size,
                ok ? UiKit.Ok : new Color(1f, 0.5f, 0.5f), TextAnchor.UpperLeft);
            // Right-anchored, so its right edge must clear the detail viewport (636 wide, RectMask2D) AND the
            // 8-px inline scrollbar that overlays the viewport's right edge — x = 20 + 620 = 640 lost the last
            // glyph ("craftabl|e", #1057). 20 + 596 = 616 leaves 20 px, well clear of both.
            UiKit.AddText(_detail, 20, y, 596, size + 8, L(craftable ? "ui.craft.src_craftable" : "ui.craft.src_raw"),
                size - 4, UiKit.CyanDim, TextAnchor.UpperRight);
            y += size + 10f;
            if (!craftable || ok)
            {
                return y;
            }

            var (sub, perCraft) = DisassembleRecipe(inp.Item);
            if (sub == null)
            {
                return y; // craftable per CraftDepth, but its recipe has no inputs — nothing to break down
            }

            int crafts = (inp.Count - have + perCraft - 1) / perCraft;
            foreach (var si in sub.Inputs)
            {
                int need = si.Count * crafts;
                int subHave = Owned(si.Item);
                bool subOk = subHave >= need;
                bool subCraftable = Game.Content.CraftDepth(si.Item) > 0;
                UiKit.AddText(_detail, 44, y, 596, size + 4,
                    $"{(subOk ? "✓" : "✗")} {ItemName(si.Item)}  {subHave}/{need} · {L(subCraftable ? "ui.craft.src_craftable" : "ui.craft.src_raw")}",
                    size - 4, UiKit.CyanDim, TextAnchor.UpperLeft);
                y += size + 6f;
            }

            return y;
        }

        private float CostBlock(IEnumerable<ItemAmount> cost, string blueprint, float y)
        {
            UiKit.AddText(_detail, 8, y, 620, 26, L("ui.craft.needs"), 20, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 32f;
            foreach (var c in cost)
            {
                y = IngredientRow(c, y, 18);
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

            if (!HasAll(bp.UnlockCost) || Game.Knowledge < bp.KnowledgeCost)
            {
                return (L("ui.tech.materials_missing"), new Color(1f, 0.7f, 0.3f));
            }

            return (L("ui.tech.unlockable"), UiKit.Cyan);
        }

        /// <summary>True when at least one blueprint is unlockable right now (prerequisites met, materials owned and
        /// enough knowledge points) — drives the "new research available" badge on the Tech menu entry.</summary>
        private bool AnyBlueprintUnlockable()
        {
            if (Game?.Content?.Blueprints == null)
            {
                return false;
            }

            foreach (var bp in Game.Content.Blueprints.Values)
            {
                if (!Game.UnlockedBlueprints.Contains(bp.Key)
                    && bp.Prerequisites.All(Game.UnlockedBlueprints.Contains)
                    && HasAll(bp.UnlockCost)
                    && Game.Knowledge >= bp.KnowledgeCost)
                {
                    return true;
                }
            }

            return false;
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
            else if (r.Station == BlocksBeyondTheStars.Shared.Definitions.CraftingStation.Factory)
            {
                // A factory recipe is craftable only while standing at a factory terminal that offers it on
                // its roster (the server enforces the same). Factory terminals only exist in spawned factories.
                string[] roster = System.Array.Empty<string>();
                bool atFactory = FactoryView.Instance != null && FactoryView.Instance.PlayerAtTerminal(out roster);
                if (!atFactory || System.Array.IndexOf(roster, r.Key) < 0)
                {
                    reason = L("ui.craft.go_to_factory");
                    return false;
                }
            }
            else if (!StationAvailable(r.Station))
            {
                // Per RECIPE, from the server's station set (#1070) — a forge recipe says "forge", not "workshop".
                reason = NeedStationText(StationKeyOf(r.Station));
                return false;
            }

            // The result must also FIT somewhere (server: MaterialPool.CanFit, #600) — otherwise the card
            // reads "craftable" and the button is live, but the server refuses with @inventory_full and the
            // only feedback is an easy-to-miss toast (LAN playtest: full 24-slot backpack, the inputs only
            // shrink stacks without freeing a slot, and the new tool needs a fresh one).
            if (!ResultFits(r))
            {
                reason = L("ui.craft.inventory_full");
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>Client-side dry-run of the server's <c>MaterialPool.CanFit</c> for a single craft:
        /// every output must fit on top of what is held right now — stack top-up first, then free slots,
        /// spilling into the ship's cargo hold only while aboard (the exact pool the server hands the
        /// crafted items to). Deliberately as pessimistic as the server: inputs are NOT removed first, so
        /// a craft whose inputs would free a slot still reads as blocked — the server refuses it too.</summary>
        private bool ResultFits(RecipeDefinition r)
        {
            var personal = SimStacks(Game.Personal);
            var cargo = Game.Aboard ? SimStacks(Game.Cargo) : null;
            int freePersonal = Mathf.Max(0, PersonalSlotTotal - personal.Count);
            int freeCargo = cargo != null ? Mathf.Max(0, Game.CargoSlots - cargo.Count) : 0;

            foreach (var output in r.Outputs)
            {
                if (output.Count <= 0)
                {
                    continue;
                }

                int maxStack = Game.Content.MaxStackOf(output.Item);
                int remaining = SimAdd(personal, ref freePersonal, output.Item, output.Count, maxStack);
                if (remaining > 0 && cargo != null)
                {
                    remaining = SimAdd(cargo, ref freeCargo, output.Item, remaining, maxStack);
                }

                if (remaining > 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Mutable copy of an inventory snapshot for the fit dry-run (occupied slots only).</summary>
        private sealed class SimStack
        {
            public string Item = string.Empty;
            public int Count;
        }

        private static List<SimStack> SimStacks(BlocksBeyondTheStars.Networking.Messages.NetItemStack[] slots)
        {
            var list = new List<SimStack>();
            if (slots != null)
            {
                foreach (var s in slots)
                {
                    list.Add(new SimStack { Item = s.Item, Count = s.Count });
                }
            }

            return list;
        }

        /// <summary>Adds <paramref name="count"/> of an item to the simulated stacks the way the server's
        /// <c>Inventory.Add</c> does (top up same-item stacks, then open new stacks in free slots) and
        /// returns what found no room. Outputs of one recipe compete for the same free slots.</summary>
        private static int SimAdd(List<SimStack> stacks, ref int freeSlots, string item, int count, int maxStack)
        {
            foreach (var s in stacks)
            {
                if (count <= 0)
                {
                    break;
                }

                if (s.Item != item || s.Count >= maxStack)
                {
                    continue;
                }

                int put = System.Math.Min(maxStack - s.Count, count);
                s.Count += put;
                count -= put;
            }

            while (count > 0 && freeSlots > 0)
            {
                int put = System.Math.Min(maxStack, count);
                stacks.Add(new SimStack { Item = item, Count = put });
                freeSlots--;
                count -= put;
            }

            return count;
        }

        /// <summary>Reachability tier for the list ordering (#826): 0 = craftable now (blueprint unlocked +
        /// materials owned), 1 = blueprint unlocked but materials missing, 2 = blueprint locked. Station
        /// proximity is deliberately NOT part of the tier — the order must not reshuffle while walking
        /// around; the station only keeps gating the status colour and the craft button.</summary>
        private int ReachTier(string requiredBlueprint, List<BlocksBeyondTheStars.Shared.Definitions.ItemAmount> cost)
        {
            if (!BlueprintOk(requiredBlueprint))
            {
                return 2;
            }

            return HasAll(cost) ? 0 : 1;
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
            if (def.ArmorResistance > 0f || def.OxygenBonus > 0f || def.ThermalInsulation > 0f)
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

        // ------------------------------------------------------------------------------------------------
        // Station gates (#1070/#1071/#1072/#1074).
        //
        // The SERVER decides which stations are in reach (Game.StationsAvailable / ResearchOk / ShipBuildOk,
        // from StationsInReach). The client used to guess from ship station markers only — so a base
        // workbench, a placed forge and even hand recipes read as blocked here while the server would have
        // crafted. It also gated per TAB ("go to the workshop") while the server gates per RECIPE (a forge
        // recipe needs a forge). Everything below names the BLOCK the player has to stand at, with its icon.
        // ------------------------------------------------------------------------------------------------

        /// <summary>The server's lower-case name of a recipe's station ("workshop", "refinery", …).</summary>
        private static string StationKeyOf(CraftingStation s) => s.ToString().ToLowerInvariant();

        /// <summary>The placed world block that provides a crafting station (mirrors the server's
        /// <c>StationBlockFor</c>); "research" and "shipbuild" are the two non-crafting gates.</summary>
        private static string StationBlockKey(string station) => station switch
        {
            "workshop" => "workbench",
            "refinery" => "forge",
            "detoxifier" => "detoxifier",
            "transmuter" => "matter_forge",
            "algaetank" => "algae_tank",
            "campfire" => "campfire",
            "factory" => "factory_terminal",
            "research" => "data_cache", // the cockpit's terminal block (#1009)
            "shipbuild" => "workbench", // the workshop module's bench aboard
            _ => null,
        };

        /// <summary>The ship module that provides a crafting station aboard, or null.</summary>
        private static string StationModuleKey(string station) => station switch
        {
            "workshop" => "workshop",
            "refinery" => "refinery",
            "detoxifier" => "detoxifier",
            "transmuter" => "transmuter",
            _ => null,
        };

        /// <summary>Player-facing name of a station gate — the BLOCK's name (Workbench, Forge, …), the
        /// cockpit for research, the workshop module for ship building.</summary>
        private string StationName(string station)
        {
            switch (station)
            {
                case "research": return L("ui.station.cockpit");
                case "shipbuild": return L("module.workshop.name");
                default:
                    string block = StationBlockKey(station);
                    return block == null ? station : L("block." + block + ".name");
            }
        }

        /// <summary>The icon sprite for a station gate (the block's atlas tile / item icon), or null.</summary>
        private Sprite StationSprite(string station)
        {
            string block = StationBlockKey(station);
            return block == null ? null : IconResolver.Resolve(block, Game);
        }

        // A host from before #1070 never sends StationsInReach. Until the first one arrives the menu falls back
        // to the old ship-marker heuristic (workshop marker = crafting; research ungated; console/cockpit =
        // ship building) so a new client on an old LAN host isn't locked out of every bench.
        private bool LegacyAtWorkshop() => (Game.NearbyStation ?? string.Empty) == "workshop";

        private bool AnyStationInReach() => Game.StationsKnown ? Game.StationsAvailable.Count > 0 : LegacyAtWorkshop();

        private bool ResearchOkNow() => !Game.StationsKnown || Game.ResearchOk;

        private bool ShipBuildOkNow() => Game.StationsKnown ? Game.ShipBuildOk : (Game.NearbyStation ?? string.Empty) is "console" or "cockpit";

        /// <summary>True when the server says this recipe's station is usable right now (hand: always).</summary>
        private bool StationAvailable(CraftingStation s)
            => s == CraftingStation.Hand || (Game.StationsKnown ? Game.StationsAvailable.Contains(StationKeyOf(s)) : LegacyAtWorkshop());

        /// <summary>The station gate the current tab is missing, or null when nothing is missing: the selected
        /// recipe's station (Crafting), "research" (Tech) or "shipbuild" (Ship). With no recipe selected the
        /// Crafting tab falls back to the everyday bench when NO station at all is in reach.</summary>
        private string MissingStation()
        {
            switch (_mode)
            {
                case Mode.Tech:
                    return ResearchOkNow() ? null : "research";
                case Mode.Ship:
                    return ShipBuildOkNow() ? null : "shipbuild";
                case Mode.Crafting:
                    if (!string.IsNullOrEmpty(_selected) && Game.Content.Recipes.TryGetValue(_selected, out var r)
                        && r.Station != CraftingStation.Hand && r.Station != CraftingStation.Market
                        && r.Station != CraftingStation.Factory && !StationAvailable(r.Station))
                    {
                        return StationKeyOf(r.Station);
                    }

                    return !AnyStationInReach() ? "workshop" : null;
                default:
                    return null;
            }
        }

        /// <summary>The reason line for a recipe whose station is out of reach: "Needs a 🔧 Workbench nearby
        /// — or your ship's Workshop module." (module clause only for stations a module can provide).</summary>
        private string NeedStationText(string station)
        {
            if (station == "research")
            {
                return L("ui.craft.need_research");
            }

            if (station == "shipbuild")
            {
                return AboardShipNow() ? L("ui.craft.hint_shipbuild_module") : L("ui.craft.hint_shipbuild_aboard");
            }

            string module = StationModuleKey(station);
            string t = module != null ? L("ui.craft.need_block_or_module") : L("ui.craft.need_block");
            return t.Replace("{block}", StationName(station))
                .Replace("{module}", module != null ? L("module." + module + ".name") : string.Empty);
        }

        /// <summary>Header hint for a tab whose gate is not met (empty when it is).</summary>
        private string GateHint(string missing)
        {
            switch (missing)
            {
                case null: return string.Empty;
                case "research": return L("ui.craft.hint_research");
                case "shipbuild":
                    return AboardShipNow() ? L("ui.craft.hint_shipbuild_module") : L("ui.craft.hint_shipbuild_aboard");
                default:
                    // No station at all in reach → "hand recipes only here"; a specific recipe's station → its block.
                    string t = !AnyStationInReach() && missing == "workshop" && !SelectedRecipeNeeds(missing)
                        ? L("ui.craft.hint_hand_only")
                        : L("ui.craft.hint_need_block");
                    return t.Replace("{block}", StationName(missing));
            }
        }

        private bool SelectedRecipeNeeds(string station)
            => !string.IsNullOrEmpty(_selected) && Game.Content.Recipes.TryGetValue(_selected, out var r)
               && StationKeyOf(r.Station) == station;

        /// <summary>Footer: the stations the server says are in reach right now, by block name.</summary>
        private string InReachText()
        {
            if (!AnyStationInReach())
            {
                return L("ui.craft.in_reach_none");
            }

            var names = new System.Collections.Generic.List<string>();
            foreach (var s in new[] { "workshop", "refinery", "detoxifier", "transmuter", "algaetank", "campfire", "factory" })
            {
                if (Game.StationsAvailable.Contains(s))
                {
                    names.Add(StationName(s));
                }
            }

            return L("ui.craft.in_reach").Replace("{list}", string.Join(", ", names));
        }

        // The player counts as "aboard" for travel when on/in the ship, piloting in space, or inside the ship
        // interior floating in space — i.e. not on foot out on a surface.
        private bool AboardShipNow() => Game.Aboard || Game.InSpace || Game.LoadingPlanetType == "ship_interior";

        // Whether a tab's function is usable right now. Used only to DIM out-of-reach tabs in the header — they
        // stay clickable so the player can still browse content (the action buttons inside enforce the gate).
        // Crafting dims only when NO station is in reach (hand recipes always work); Tech needs the cockpit,
        // Ship the workshop module aboard, Map the ship. Tabs without a context requirement are always available.
        private bool IsTabAvailable(Mode mode) => mode switch
        {
            Mode.Crafting => AnyStationInReach(),
            Mode.Tech => ResearchOkNow(),
            Mode.Ship => ShipBuildOkNow(),
            Mode.Map => AboardShipNow(),
            _ => true,
        };

        /// <summary>The station a dimmed tab is waiting for (icon badge on the tab button).</summary>
        private static string TabStation(Mode mode) => mode switch
        {
            Mode.Crafting => "workshop",
            Mode.Tech => "research",
            Mode.Ship => "shipbuild",
            _ => null,
        };

        // --- "Where?" locator (#1072) ---

        private string _locateStation;   // the station the last request asked about
        private float _locateSentAt = -99f;
        private Text _whereText;
        private Button _whereShow, _whereCraft;

        /// <summary>Asks the server where the nearest matching station is (once per station + every few
        /// seconds while the hint stays up, so walking around refreshes the distance).</summary>
        private void RequestLocate(string station)
        {
            if (Game?.Network == null || string.IsNullOrEmpty(station))
            {
                return;
            }

            if (station != _locateStation || Time.unscaledTime - _locateSentAt > 4f)
            {
                _locateStation = station;
                _locateSentAt = Time.unscaledTime;
                Game.Network.SendLocateStation(station);
            }
        }

        /// <summary>The locate answer that belongs to the station currently missing, or null.</summary>
        private StationLocation CurrentLocation(string missing)
        {
            var loc = Game?.LastStationLocation;
            return loc != null && !string.IsNullOrEmpty(missing) && loc.Station == missing ? loc : null;
        }

        /// <summary>"Workbench · 12 m ↗" / "aboard your ship · 40 m ←" / "none within 24 m", live-updated
        /// while the menu is open (distance + arrow follow the player).</summary>
        private string WhereText(string missing)
        {
            var loc = CurrentLocation(missing);
            if (loc == null)
            {
                return string.Empty;
            }

            if (!loc.Found)
            {
                return L("ui.craft.where_none");
            }

            var target = Game.ScenePos(loc.X + 0.5f, loc.Y, loc.Z + 0.5f);
            float dx = target.x - Game.PlayerPosition.x, dz = target.z - Game.PlayerPosition.z;
            int dist = Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dz * dz));
            float rel = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg - Game.PlayerYaw; // 0 = straight ahead
            string arrow = DirectionArrow(rel);
            string what = loc.Kind == "ship" ? L("ui.craft.where_ship") : StationName(missing);
            return L("ui.craft.where").Replace("{what}", what).Replace("{dist}", dist.ToString()).Replace("{dir}", arrow);
        }

        /// <summary>One of eight arrows for a bearing relative to the player's facing (0 = ahead).</summary>
        private static string DirectionArrow(float relDeg)
        {
            int oct = Mathf.RoundToInt(Mathf.Repeat(relDeg, 360f) / 45f) % 8;
            return oct switch { 0 => "↑", 1 => "↗", 2 => "→", 3 => "↘", 4 => "↓", 5 => "↙", 6 => "←", _ => "↖" };
        }

        /// <summary>The unlocked recipe that produces the block a missing station needs, or null.</summary>
        private RecipeDefinition RecipeForStationBlock(string missing)
        {
            string block = StationBlockKey(missing);
            if (block == null || missing is "research" or "shipbuild" || Game?.Content == null)
            {
                return null;
            }

            foreach (var r in Game.Content.Recipes.Values)
            {
                foreach (var o in r.Outputs)
                {
                    if (o.Item == block && BlueprintOk(r.RequiredBlueprint))
                    {
                        return r;
                    }
                }
            }

            return null;
        }

        /// <summary>Puts the located station on the HUD compass (surface waypoint) and closes nothing —
        /// the player keeps browsing, the compass now points at the bench.</summary>
        private void MarkLocatedStation(string missing)
        {
            var loc = CurrentLocation(missing);
            if (loc == null || !loc.Found)
            {
                return;
            }

            Game.Waypoint = new Vector3(loc.X + 0.5f, 0f, loc.Z + 0.5f);
            Game.ShowMessage(L("ui.craft.where_marked"));
        }

        /// <summary>Called by GameMenu when the menu closes: if the player was just told where a station is,
        /// drop a through-wall marker on it for a few seconds so "over there" is visible in the world.</summary>
        public void OnMenuClosed()
        {
            var loc = Game?.LastStationLocation;
            if (loc == null || !loc.Found || _locateStation == null || loc.Station != _locateStation)
            {
                return;
            }

            var target = Game.ScenePos(loc.X + 0.5f, loc.Y, loc.Z + 0.5f);
            if ((target - Game.PlayerPosition).sqrMagnitude > 40f * 40f)
            {
                return; // too far to be worth a marker (and out of the locate radius anyway)
            }

            OreScanView.Instance?.ShowStationMarker(loc.X, loc.Y, loc.Z, 8f);
            _locateStation = null; // one marker per hint, not on every close
        }

        /// <summary>Jump to the recipe that produces the missing station's block ("craft one →").</summary>
        private void JumpToRecipe(string recipeKey)
        {
            Menu?.OpenCrafting();
            _selected = recipeKey;
            _category = "all";
            _search = string.Empty;
            RebuildSidebar();
            RebuildList();
            RebuildDetail();
        }

        /// <summary>Signature of the station gate state for the refresh hash (#1070).</summary>
        private int StationsSig()
        {
            int sig = (Game.ResearchOk ? 17 : 0) + (Game.ShipBuildOk ? 29 : 0);
            foreach (var s in Game.StationsAvailable)
            {
                unchecked { sig += s.GetHashCode(); }
            }

            var loc = Game.LastStationLocation;
            if (loc != null)
            {
                unchecked { sig += loc.Station.GetHashCode() * 3 + (loc.Found ? loc.X * 5 + loc.Y * 7 + loc.Z * 11 : 13); }
            }

            return sig;
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
            if (def.ArmorResistance > 0f || def.OxygenBonus > 0f || def.ThermalInsulation > 0f) return "cat_suit";
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
            UiKit.AddInlineScrollbar(sr); // sidebar/list/detail all get a position indicator (#664)
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

        /// <summary>Localized label for a raw data/enum identifier (blueprint category, body kind/status,
        /// objective type, story-fragment category): resolves <paramref name="prefix"/> + the lower-cased id
        /// and falls back to the raw id for values without a key, so new data/enum members degrade to
        /// today's behaviour instead of showing a bracketed key.</summary>
        private string IdLabel(string prefix, string id)
            => string.IsNullOrEmpty(id) || Game?.Localizer?.Has(prefix + id.ToLowerInvariant()) != true
                ? id
                : L(prefix + id.ToLowerInvariant());

        // --- Coloured planet marks in the star map ---------------------------------------------------------
        // A player wanted to mark planets in space, each in its own colour — several at once, unlike the single
        // surface waypoint. Stored locally in ClientSettings (never sent to the server) and grouped by world, so
        // marks from one save don't bleed into another (body ids like "sys0-p5" repeat across saves).

        /// <summary>The save these marks belong to. Set when a local world is started or hosted; for a remote
        /// server it keeps the last local name, which only ever groups marks slightly oddly — they stay local
        /// cosmetics either way.</summary>
        private string MarkerWorld => Menu?.Settings?.LastWorld ?? string.Empty;

        /// <summary>The colour index marking a body, or -1 when unmarked.</summary>
        private int MarkerColorOf(string bodyId)
            => Menu?.Settings?.GetPlanetMarker(MarkerWorld, bodyId) ?? -1;

        /// <summary>Steps a body through the palette and off the end back to unmarked, so one button both marks
        /// and recolours — the fewest controls for the youngest player.</summary>
        private void CyclePlanetMarker(string bodyId)
        {
            int next = MarkerColorOf(bodyId) + 1;
            SetPlanetMarker(bodyId, next >= PlanetMarkerPalette.Count ? -1 : next);
        }

        private void SetPlanetMarker(string bodyId, int color)
        {
            if (Menu?.Settings == null)
            {
                return;
            }

            Menu.Settings.SetPlanetMarker(MarkerWorld, bodyId, color);
            Menu.Settings.Save();
            RebuildDetail(); // redraw the button label/tint and re-halo the orrery
        }

        /// <summary>A mission's display title: FreeText (player missions, L3 LLM board texts) verbatim,
        /// otherwise the locale key resolved.</summary>
        private string MissionText(NetMission m) => m.FreeText ? m.Title : L(m.Title);
        // The shared display-name helper (#927) owns the base-key strip + modifier suffixes; the resolver
        // names player-designed forms after their creator's label instead of the generic "own form".
        private string ItemName(string item)
            => BlocksBeyondTheStars.Shared.Localization.ItemNames.Display(Game.Localizer, item,
                _customFormName ??= idx => Game.CustomShapes?.NameOf(idx));

        private System.Func<int, string> _customFormName;
        private string Desc(string key)
        {
            // Localizer.Get returns "[key]" (never the bare key) on a miss, so comparing against the key
            // can't detect one — ask Has() instead, like WikiUI does, and show nothing for absent texts.
            return Game?.Localizer?.Has(key) == true ? L(key) : string.Empty;
        }
    }
}
