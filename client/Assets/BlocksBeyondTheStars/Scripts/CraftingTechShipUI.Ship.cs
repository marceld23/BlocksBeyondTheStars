// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using BlocksBeyondTheStars.Client.Portal;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Ship tab of <see cref="CraftingTechShipUI"/>: the module list with the cargo tiers and the fit summary,
    /// the hull-paint page with its live ship preview, and the module detail with the ship's stats.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        private ShipPreviewRig _shipPreview; // live ship preview for the Ship paint tab (item 32)

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
                if (_category == "modules")
                {
                    y = AddFitSummary(y); // what the active ship carries + the one-of-each rule (#1268)
                }

                // Same reachability ordering as the craft list (#826): buildable → materials missing →
                // blueprint locked, simpler modules first inside each tier. Fitted modules come first —
                // the tab used to be a pure build catalogue that showed "Craftable" for a module already
                // aboard, with a Build button the server then refused (#1268).
                var modules = Game.Content.ShipModules.Values.Where(m => !m.Mandatory)
                    .OrderByDescending(ModuleFitted)
                    .ThenBy(m => ReachTier(m.RequiredBlueprint, m.BuildCost))
                    .ThenBy(m => m.BuildCost.Count)
                    .ThenBy(m => Game.Content.MaxInputDepth(m.BuildCost))
                    .ThenBy(m => m.BuildCost.Sum(c => c.Count));
                foreach (var m in modules)
                {
                    if (!MatchesSearch(L($"module.{m.Key}.name")))
                    {
                        continue;
                    }

                    bool fitted = ModuleFitted(m);
                    bool can = FreeCrafting() || (HasAll(m.BuildCost) && BlueprintOk(m.RequiredBlueprint));
                    if (_craftableOnly && !can && !fitted)
                    {
                        continue;
                    }

                    string badge = fitted ? L("ui.ship.installed") : can ? L("ui.craft.ready") : L("ui.craft.blocked");
                    var badgeColor = fitted ? UiKit.Cyan : can ? UiKit.Ok : new Color(1f, 0.5f, 0.5f);
                    AddCard(y, L($"module.{m.Key}.name"), "cat_modules", badge, badgeColor,
                        "mod:" + m.Key, () => { _selected = "mod:" + m.Key; RebuildDetail(); }, contentKey: m.Key);
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

                    bool can = FreeCrafting() || (HasAll(s.CraftCost) && BlueprintOk(s.RequiredBlueprint));
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

        /// <summary>Whether the ACTIVE ship carries this module (the combat status streams its fit).</summary>
        private bool ModuleFitted(BlocksBeyondTheStars.Shared.Definitions.ShipModuleDefinition m)
            => Game.ShipCombat?.Modules != null && System.Array.IndexOf(Game.ShipCombat.Modules, m.Key) >= 0;

        /// <summary>#1471: "Cargo hold: 72 slots = 48 + 24 · tier 1 of 3 · next: Cargo Expansion II (+32)" — the
        /// active ship's hold make-up, so it is visible that 72 already CONTAINS tier I and what the next step
        /// is. Empty when no fit is known yet (the combat status streams the module list).</summary>
        private string CargoTierLine()
        {
            var fitted = Game.ShipCombat?.Modules;
            if (fitted == null || Game.Content == null)
            {
                return string.Empty;
            }

            var tiers = Game.Content.ShipModules.Values
                .Where(m => m.Key.StartsWith("cargo_hold_", System.StringComparison.Ordinal) && m.Stats.ContainsKey("cargo_slots"))
                .OrderBy(m => m.Key == "cargo_hold_basic" ? 0 : 1)
                .ThenBy(m => m.Key, System.StringComparer.Ordinal)
                .ToList();
            int total = 0, fittedTiers = 0, tierCount = 0;
            var parts = new List<string>();
            BlocksBeyondTheStars.Shared.Definitions.ShipModuleDefinition next = null;
            foreach (var m in tiers)
            {
                bool isTier = m.Key != "cargo_hold_basic";
                if (isTier)
                {
                    tierCount++;
                }

                if (System.Array.IndexOf(fitted, m.Key) >= 0)
                {
                    int slots = (int)m.Stats["cargo_slots"];
                    total += slots;
                    parts.Add(slots.ToString());
                    if (isTier)
                    {
                        fittedTiers++;
                    }
                }
                else if (isTier && next == null)
                {
                    next = m;
                }
            }

            if (parts.Count == 0)
            {
                return string.Empty;
            }

            string line = L("ui.ship.cargo_makeup")
                .Replace("{total}", total.ToString())
                .Replace("{parts}", string.Join(" + ", parts))
                .Replace("{n}", fittedTiers.ToString())
                .Replace("{max}", tierCount.ToString());
            line += " · " + (next != null
                ? L("ui.ship.cargo_next").Replace("{name}", L($"module.{next.Key}.name")).Replace("{slots}", ((int)next.Stats["cargo_slots"]).ToString())
                : L("ui.ship.cargo_maxed"));
            return line;
        }

        /// <summary>Localised, comma-joined names of a module list (mandatory hull essentials skipped).</summary>
        private string FitList(string[] modules)
        {
            var names = new List<string>();
            foreach (var key in modules ?? System.Array.Empty<string>())
            {
                if (Game.Content.GetShipModule(key) is { Mandatory: false })
                {
                    names.Add(L($"module.{key}.name"));
                }
            }

            return names.Count == 0 ? L("ui.ship.fitted_none") : L("ui.ship.fitted").Replace("{list}", string.Join(", ", names));
        }

        /// <summary>The active ship's fit + the one-of-each rule at the top of the Modules tab (#1268): the
        /// question "how much can a ship hold?" had no answer because there is no slot budget — only
        /// "one of each" — and nothing ever said so.</summary>
        private float AddFitSummary(float y)
        {
            var t = UiKit.AddText(_listContent, 8, y, 752, 52, FitList(Game.ShipCombat?.Modules), 18, UiKit.TextCol, TextAnchor.UpperLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            y += 56f;
            string cargoLine = CargoTierLine();
            if (!string.IsNullOrEmpty(cargoLine))
            {
                var cargo = UiKit.AddText(_listContent, 8, y, 752, 26, cargoLine, 18, UiKit.Cyan, TextAnchor.UpperLeft);
                cargo.horizontalOverflow = HorizontalWrapMode.Wrap;
                y += 30f;
            }

            var rule = UiKit.AddText(_listContent, 8, y, 752, 48,
                L("ui.ship.module_rule").Replace("{pct}", Mathf.RoundToInt(BlocksBeyondTheStars.Shared.Definitions.ShipModuleDefinition.SalvageRate * 100f).ToString()),
                16, UiKit.CyanDim, TextAnchor.UpperLeft);
            rule.horizontalOverflow = HorizontalWrapMode.Wrap;
            y += 56f;
            return y;
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

                // What this ship carries (#1268) — so you know which hangar ship has the breaker before switching.
                var fit = UiKit.AddText(_detail, 8, y, 620, 60, FitList(s.Modules), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                fit.horizontalOverflow = HorizontalWrapMode.Wrap;
                y += 66f;

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
                    y = AddDetailDescription(desc, y);
                }

                // #1471: on a cargo module, spell out what the hold is made of and what the next tier adds — a
                // hauler owner saw "72" next to a fitted tier I and read it as a broken upgrade.
                if (m.Key.StartsWith("cargo_hold_", System.StringComparison.Ordinal))
                {
                    string cargoLine = CargoTierLine();
                    if (!string.IsNullOrEmpty(cargoLine))
                    {
                        var c = UiKit.AddText(_detail, 8, y, 620, 52, cargoLine, 18, UiKit.Cyan, TextAnchor.UpperLeft);
                        c.horizontalOverflow = HorizontalWrapMode.Wrap;
                        y += 56f;
                    }
                }

                if (ModuleFitted(m))
                {
                    // Already aboard (#1268): no Build button (the server refused it anyway) — instead the way
                    // out, with the salvage share (#1269). Hull essentials, stations and the basic hold stay.
                    UiKit.AddText(_detail, 8, y, 620, 26, L("ui.ship.installed"), 20, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
                    y += 32f;
                    if (m.Key == "planet_scanner")
                    {
                        // #2140: scan the body the ship is at right from the module; other bodies via the star map.
                        y = AddPlanetScanSection(y, string.Empty); // "" = where the ship is (server-resolved, also in the interior)
                        UiKit.AddText(_detail, 8, y, 620, 26, L("ui.planetscan.map_hint"), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                        y += 34f;
                    }

                    if (!m.Removable)
                    {
                        UiKit.AddText(_detail, 8, y, 620, 26, L("ui.ship.not_removable"), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                        y += 30f;
                        return y;
                    }

                    if (!ShipBuildOkNow())
                    {
                        UiKit.AddText(_detail, 8, y, 620, 26, NeedStationText("shipbuild"), 18, new Color(1f, 0.8f, 0.4f), TextAnchor.UpperLeft);
                        y += 30f;
                    }

                    string pct = Mathf.RoundToInt(BlocksBeyondTheStars.Shared.Definitions.ShipModuleDefinition.SalvageRate * 100f).ToString();
                    var remove = UiKit.AddButton(_detail, 8, y, 320, 56, L("ui.action.uninstall").Replace("{pct}", pct),
                        () => { Game.Network.SendUninstallModule(m.Key); });
                    SetInteractable(remove, ShipBuildOkNow());
                    y += 70f;
                    return y;
                }

                y = CostBlock(m.BuildCost, m.RequiredBlueprint, y);
                bool ready = FreeCrafting() || (HasAll(m.BuildCost) && BlueprintOk(m.RequiredBlueprint));
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
                bool can = FreeCrafting() || (HasAll(def.CraftCost) && BlueprintOk(def.RequiredBlueprint));
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
            UiKit.AddText(_detail, 8, y, 620, 26, $"{L("ui.ship.speed")}: {def.FlightSpeed:0.0}    {L("ui.ship.handling")}: {def.Handling:0.0}    {L("ui.ship.cargo")}: {Game.Content.StartCargoSlots(def)}", 20, UiKit.TextCol, TextAnchor.UpperLeft);
            y += 36f;
            return y;
        }
    }
}
