// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The on-board console's frame of <see cref="CraftingTechShipUI"/> (#3671). Twelve tabs in a row ran into the screen
    /// edge, the active one was hard to tell apart, and Pause and Close sat among them looking like tabs. Now:
    /// <list type="bullet">
    /// <item>the title row: "Ship Interface" top left, Pause and Close as quiet icon buttons at its right end;</item>
    /// <item>eight main tabs with icons in one row — Inventory, Crafting, Blueprints, Ship, Map, Logbook (Missions ·
    /// Journal · Achievements), Knowledge (Codex · Creatures · DataQubes) and Me (Character · Photos · Alliances) — the
    /// selected one drawn as the primary button;</item>
    /// <item>under it a segmented sub-tab bar when the group has more than one page (the Inventory shows its pages there:
    /// Backpack, Suit, Cargo hold, Samples) — otherwise that row carries the station gate row (#1071/#1072);</item>
    /// <item>the panes: sidebar · list · detail, or list · detail for the Inventory, whose list takes the sidebar's width.</item>
    /// </list>
    /// LB / RB walk the eight main tabs. The Codex and the DataQubes stay full-screen browsers; their sub-tabs open them the
    /// way the header buttons used to.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        // Geometry in the 1920×1080 reference space.
        private const float TitleY = 12f, TitleH = 46f;                 // row 1: the title, Pause and Close
        private const float MainTabY = 64f, MainTabH = 56f;             // row 2: the eight main tabs
        private const float SubTabY = 128f, SubTabH = 44f, SubTabW = 300f; // row 3: sub-tabs / inventory pages / gate row
        private const float TabGap = 8f;
        private const float PaneTop = 184f, PaneH = 786f;               // the panes' frames
        private const float StripY = PaneTop + 14f, StripH = 44f;       // the list pane's search / filter strip
        private const float ListTop = PaneTop + 70f, ListH = PaneH - 82f; // the list scroll view
        private const float ListX = 392f, ListW = 796f;                 // …beside the sidebar
        private const float WideListX = 52f, WideListW = 1136f;         // …without one (the Inventory)

        /// <summary>Sub-tab targets that are no <see cref="Mode"/>: the Knowledge group's two full-screen browsers.</summary>
        private const int CodexTarget = -1, DataQubesTarget = -2;

        private static (int Target, string Loc, string Icon) Page(Mode mode, string loc, string icon) => ((int)mode, loc, icon);

        private static (int Target, string Loc, string Icon) Browser(int target, string loc, string icon) => (target, loc, icon);

        /// <summary>The eight main tabs, left to right, each with its pages (sub-tabs). A group with one page shows no sub-tab
        /// bar. The Mode values still match GameMenu.Tab; the groups only decide how the pages are reached.</summary>
        private static readonly (string Loc, string Icon, (int Target, string Loc, string Icon)[] Subs)[] Groups =
        {
            ("ui.inventory.title", "cat_inventory", new[] { Page(Mode.Inventory, "ui.inventory.title", "cat_inventory") }),
            ("ui.crafting.title", "cat_tools", new[] { Page(Mode.Crafting, "ui.crafting.title", "cat_tools") }),
            ("ui.tab.tech", "cat_tech", new[] { Page(Mode.Tech, "ui.tab.tech", "cat_tech") }),
            ("ui.tab.ship", "map_ship", new[] { Page(Mode.Ship, "ui.tab.ship", "map_ship") }),
            ("ui.tab.map", "cat_planet", new[] { Page(Mode.Map, "ui.tab.map", "cat_planet") }),
            ("ui.tab.logbook", "cat_mission", new[]
            {
                Page(Mode.Missions, "ui.tab.missions", "cat_mission"),
                Page(Mode.Story, "ui.tab.story", "btn_feedback"),
                Page(Mode.Achievements, "ui.tab.achievements", "btn_credits"),
            }),
            ("ui.tab.knowledge", "info_multiplayer", new[]
            {
                Browser(CodexTarget, "ui.tab.wiki", "info_multiplayer"),
                Page(Mode.Companions, "ui.tab.companions", "ov_animals"),
                Browser(DataQubesTarget, "ui.tab.arcade", "btn_dice"),
            }),
            ("ui.tab.me", "cat_suit", new[]
            {
                Page(Mode.Character, "ui.tab.settings", "cat_suit"),
                Page(Mode.Photos, "ui.tab.photos", "set_display"),
                Page(Mode.Alliances, "ui.tab.alliances", "btn_multiplayer"),
            }),
        };

        /// <summary>The page last shown per group (group index → Mode), so a group opens where the player left it.</summary>
        private readonly Dictionary<int, int> _lastSub = new Dictionary<int, int>();

        private bool _selectActiveTabOnBuild; // set by a tab click / CycleTab, consumed by the next BuildHeader
        private bool _selectSubTabOnBuild;    // set by a sub-tab click, consumed by the next BuildHeader

        /// <summary>The list pane spans the sidebar's width too: the Inventory has no sidebar (its pages are the sub-tabs).</summary>
        private bool WideList => _mode == Mode.Inventory;

        private float ListViewX => WideList ? WideListX : ListX;

        private float ListViewW => WideList ? WideListW : ListW;

        /// <summary>The width of a list card (<see cref="AddCard"/>): the list view less its scrollbar margin.</summary>
        private float CardW => ListViewW - 16f;

        /// <summary>The main-tab group a page belongs to.</summary>
        private static int GroupOf(Mode mode)
        {
            for (int g = 0; g < Groups.Length; g++)
            {
                foreach (var sub in Groups[g].Subs)
                {
                    if (sub.Target == (int)mode)
                    {
                        return g;
                    }
                }
            }

            return 0;
        }

        /// <summary>The label key of a page (the cross-tab search names the tab it found something in).</summary>
        private static string ModeLoc(Mode mode)
        {
            foreach (var group in Groups)
            {
                foreach (var sub in group.Subs)
                {
                    if (sub.Target == (int)mode)
                    {
                        return sub.Loc;
                    }
                }
            }

            return "ui.inventory.title";
        }

        /// <summary>Tab bar + sub-tab row + (per page) the list pane's strip, rebuilt per mode.</summary>
        private void BuildHeader()
        {
            ClearChildren(_header);
            ApplyPaneLayout();
            AddMainTabs(_header); // first in the hierarchy: the pad's first focus is a tab (UiNav), never Close
            if (_mode == Mode.Inventory)
            {
                if (_category == "catalog" && !CatalogAvailable())
                {
                    _category = "personal"; // the world left Sandbox while the page was open — before the page bar marks one
                }

                AddInventoryPages(_header); // the Backpack / Suit / Cargo hold / Samples pages as the sub-tabs
            }
            else
            {
                AddSubTabs(_header);
            }

            AddListStrip(_header);
            AddHeaderActions(_header);
        }

        /// <summary>Two panes or three: the Inventory's list takes the sidebar's place (#3671), every other page keeps
        /// sidebar · list · detail. The detail pane never moves.</summary>
        private void ApplyPaneLayout()
        {
            bool wide = WideList;
            if (_frames[0] != null)
            {
                _frames[0].gameObject.SetActive(!wide);
            }

            _sidebar.parent.gameObject.SetActive(!wide);
            if (_frames[1] != null)
            {
                UiKit.Place(_frames[1].gameObject, wide ? 40f : 380f, PaneTop, wide ? 1160f : 820f, PaneH);
            }

            UiKit.Place(_listContent.parent.gameObject, ListViewX, ListTop, ListViewW, ListH);
            _listContent.sizeDelta = new Vector2(ListViewW, _listContent.sizeDelta.y);
        }

        /// <summary>Pause and Close: actions, not pages — quiet icon buttons at the right end of the title row, apart from
        /// the tabs. The Esc pause menu (Resume / Settings / Quit) has no other pad route: Esc is keyboard-only in-game and
        /// every stock pad button is spoken for, while Start opens THIS screen — so a pad player looks for it here. Pause closes
        /// the tab menu first: the two are separate owners in the cursor arbiter and must not stack.</summary>
        private void AddHeaderActions(Transform p)
        {
            const float closeW = 190f, pauseW = 240f;
            float closeX = W - 40f - closeW;
            UiKit.AddTierButton(p, closeX, TitleY, closeW, TitleH, L("ui.action.close"), () => Menu?.CloseFromUi(),
                ButtonTier.Quiet, "btn_close");
            UiKit.AddTierButton(p, closeX - 12f - pauseW, TitleY, pauseW, TitleH, L("ui.tab.pause"), () =>
            {
                Menu?.CloseFromUi();
                FindAnyObjectByType<AppShell>()?.OpenPauseMenu();
            }, ButtonTier.Quiet, "btn_pause");
        }

        /// <summary>The eight main tabs: the selected one primary, the rest quiet. A tab whose station is out of reach is
        /// dimmed but stays clickable (#1071) and names the block it waits for; a group with something new behind it gets the
        /// amber dot.</summary>
        private void AddMainTabs(Transform p)
        {
            int current = GroupOf(_mode);
            float tw = (W - 80f - (Groups.Length - 1) * TabGap) / Groups.Length;
            for (int g = 0; g < Groups.Length; g++)
            {
                int group = g;
                bool active = g == current;
                var b = UiKit.AddButton(p, 40f + g * (tw + TabGap), MainTabY, tw, MainTabH, L(Groups[g].Loc), () => OnGroup(group),
                    Groups[g].Icon);
                UiKit.StyleTier(b, active ? ButtonTier.Primary : ButtonTier.Quiet);
                if (active)
                {
                    if (_selectActiveTabOnBuild)
                    {
                        _selectActiveTabOnBuild = false;
                        SelectOnBuild(b);
                    }

                    continue;
                }

                if (Groups[g].Subs.Length == 1 && Groups[g].Subs[0].Target >= 0 && !IsTabAvailable((Mode)Groups[g].Subs[0].Target))
                {
                    DimLockedTab(b, tw, (Mode)Groups[g].Subs[0].Target);
                }

                if (GroupBadge(g))
                {
                    UiKit.AddBadge(b, tw);
                }
            }
        }

        /// <summary>Context not met (Map needs you aboard; Crafting / Blueprints / Ship need their station): the tab reads
        /// as out of reach. It stays CLICKABLE so the player can still browse it — the actions inside enforce the gate. The
        /// small icon in its corner names the BLOCK the tab waits for (#1071): workbench / cockpit / workshop module.</summary>
        private void DimLockedTab(Button b, float tw, Mode mode)
        {
            b.GetComponent<Image>().color = UiKit.TabLocked;
            var label = b.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.color = UiKit.CyanDim;
            }

            var sprite = StationSprite(TabStation(mode));
            if (sprite != null)
            {
                var icon = UiKit.AddIconSprite(b.transform, tw - 26f, MainTabH - 26f, 22f, sprite, new Color(1f, 1f, 1f, 0.85f));
                if (icon != null)
                {
                    icon.raycastTarget = false;
                }
            }
        }

        /// <summary>The sub-tab bar of a group with more than one page (Logbook, Knowledge, Me), with the amber dot on a page
        /// that has something new.</summary>
        private void AddSubTabs(Transform p)
        {
            int g = GroupOf(_mode);
            var subs = Groups[g].Subs;
            if (subs.Length < 2)
            {
                return;
            }

            var labels = new string[subs.Length];
            var icons = new string[subs.Length];
            int selected = -1;
            for (int i = 0; i < subs.Length; i++)
            {
                labels[i] = L(subs[i].Loc);
                icons[i] = subs[i].Icon;
                if (subs[i].Target == (int)_mode)
                {
                    selected = i;
                }
            }

            var bar = UiKit.AddSegmented(p, 40f, SubTabY, subs.Length * SubTabW + (subs.Length - 1) * TabGap, SubTabH, labels, selected,
                i => OnSubTab(g, i), icons, TabGap);
            for (int i = 0; i < subs.Length; i++)
            {
                if (i != selected && SubBadge(subs[i].Target))
                {
                    UiKit.AddBadge(bar.Buttons[i], SubTabW);
                }
            }

            if (_selectSubTabOnBuild && selected >= 0)
            {
                _selectSubTabOnBuild = false;
                SelectOnBuild(bar.Buttons[selected]);
            }
        }

        /// <summary>The list pane's strip (search, filters) of the current page.</summary>
        private void AddListStrip(Transform p)
        {
            if (_mode == Mode.Crafting || _mode == Mode.Ship || _mode == Mode.Tech)
            {
                // Search + the "Craftable now" filter (Crafting, Ship) or "Researchable now" (Blueprints, #2563; "Enough
                // knowledge" before), which keeps only what the player can make right now — prerequisites, knowledge AND
                // materials, the card status's own test. #3104: the Blueprints strip carries a search like the Crafting tab's;
                // its knowledge balance and data fragments live in the list header (BuildTechList).
                AddSearchBox(p, ListX, StripY, 470, StripH);
                string toggle = _mode == Mode.Tech ? L("ui.tech.researchable_now") : L("ui.craft.craftable_now");
                var t = UiKit.AddButton(p, ListX + 488f, StripY, 300, StripH, (_craftableOnly ? "[x] " : "[ ] ") + toggle,
                    () => { _craftableOnly = !_craftableOnly; BuildHeader(); RebuildList(); });
                if (_craftableOnly)
                {
                    t.GetComponent<Image>().color = UiKit.Cyan;
                }
            }
            else if (_mode == Mode.Inventory && _category == "samples")
            {
                AddSampleFilters(p, ListViewX, StripY); // #2300: the sample case's filter, shared with the bio lab
            }
            else if (_mode == Mode.Inventory && (_category == "personal" || _category == "cargo"
                                                 || (_category == "catalog" && CatalogAvailable())))
            {
                AddInventoryStrip(p); // #3110/#3111: search + category filter (+ sort in the hold); the catalog's search (#1930)
            }
        }

        /// <summary>Whether a page has something new waiting: research affordable now, an unread story beat, a new companion,
        /// a freshly downloaded data-cube game.</summary>
        private bool SubBadge(int target)
        {
            if (target == DataQubesTarget)
            {
                return Game?.NewArcadeUnseen ?? false;
            }

            return (Mode)target switch
            {
                Mode.Tech => AnyBlueprintUnlockable(),
                Mode.Story => Game?.NewStoryUnseen ?? false,
                Mode.Companions => Game?.NewCompanionUnseen ?? false,
                _ => false,
            };
        }

        private bool GroupBadge(int g)
        {
            foreach (var sub in Groups[g].Subs)
            {
                if (SubBadge(sub.Target))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The page a main tab opens on: one with something new waiting, else the one last shown in this group, else
        /// its first page (never a browser — the Knowledge tab opens on the Creatures, the Codex is one click away).</summary>
        private int DefaultTarget(int g)
        {
            var subs = Groups[g].Subs;
            foreach (var sub in subs)
            {
                if (sub.Target >= 0 && SubBadge(sub.Target))
                {
                    return sub.Target;
                }
            }

            if (_lastSub.TryGetValue(g, out int last))
            {
                return last;
            }

            foreach (var sub in subs)
            {
                if (sub.Target >= 0)
                {
                    return sub.Target;
                }
            }

            return (int)Mode.Inventory;
        }

        private void OnTab(int tab) => Menu?.SwitchFromUi(tab); // GameMenu owns the active tab

        private void OnGroup(int g)
        {
            if (g == GroupOf(_mode))
            {
                return; // already there
            }

            _selectActiveTabOnBuild = true; // the pad stays on the tab row
            OnTab(DefaultTarget(g));
        }

        /// <summary>A sub-tab: a page of the group, or one of the Knowledge group's full-screen browsers — the Codex and the
        /// DataQubes open over the menu as their header buttons did; their "Back to menu" returns here.</summary>
        private void OnSubTab(int g, int index)
        {
            int target = Groups[g].Subs[index].Target;
            if (target == CodexTarget)
            {
                Menu?.OpenWiki();
            }
            else if (target == DataQubesTarget)
            {
                Menu?.OpenArcade();
            }
            else if (target != (int)_mode)
            {
                _selectSubTabOnBuild = true;
                OnTab(target);
            }
        }

        /// <summary>Steps the main tab by <paramref name="dir"/> (wrapping), through the same path a tab click takes, so
        /// badges, station gating and data refresh behave identically. Dimmed (context-not-met) tabs are stepped onto like a
        /// click reaches them: they stay browsable (#1071).</summary>
        private void CycleTab(int dir)
        {
            int next = (GroupOf(_mode) + dir + Groups.Length) % Groups.Length;
            _selectActiveTabOnBuild = true;
            UiSound.Click();
            OnTab(DefaultTarget(next));
        }

        /// <summary>A shoulder-button or tab step lands the pad ON the new tab, not wherever the remembered index happens to
        /// point in a differently shaped page (#1409).</summary>
        private void SelectOnBuild(Button b)
        {
            EventSystem.current?.SetSelectedGameObject(b.gameObject);
            _canvas.GetComponent<UiNavFocus>()?.NoteSelection(b.gameObject);
        }
    }
}
