// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The wreck panel and the ship-repair panel of <see cref="HudUi"/> (right side; docs/developer/SHIP_REPAIR.md).
    /// </summary>
    public sealed partial class HudUi
    {
        private GameObject _wreckPanel, _shipRepairPanel;
        private TMP_Text _wreckName, _wreckProg, _wreckHint;
        private TMP_Text _shipRepairTitle, _shipRepairProg, _shipRepairHint;
        private Image _wreckBar, _shipRepairBar, _shipRepairTrack;
        private Button _wreckClaim, _shipRepairBtn;

        private void BuildWreck(Transform root)
        {
            // Wreck panel (right).
            _wreckPanel = Panel(root, W - 260f, 140, 250, 150).gameObject;
            _wreckName = UiText.Add(_wreckPanel.transform, 10, 26, 230, 18, string.Empty, 14, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiText.Add(_wreckPanel.transform, 10, 6, 230, 18, Game?.Localizer?.Get("ui.hud.wreck") ?? "WRECK", 14, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _wreckBar = UiHolo.AddBar(_wreckPanel.transform, 10, 48, 230, 14, new Color(0.03f, 0.07f, 0.13f), UiKit.Cyan).Fill;
            _wreckProg = UiText.Add(_wreckPanel.transform, 12, 47, 226, 16, string.Empty, 12, UiKit.TextCol, TextAnchor.MiddleLeft);
            _wreckHint = UiText.Add(_wreckPanel.transform, 10, 68, 230, 50, string.Empty, 12, UiKit.CyanDim, TextAnchor.UpperLeft);
            UiText.Wrap(_wreckHint);
            _wreckClaim = UiKit.AddButton(_wreckPanel.transform, 10, 120, 230, 24, string.Empty, () => Game.Network?.SendClaimWreck());
        }

        private void BuildShipRepair(Transform root)
        {
            // Ship-repair panel (right, below the wreck panel) — the cockpit "Repair ship" action: buy hull
            // back + refill EVA-carved hull cells with one click, paid in metal (docs/developer/SHIP_REPAIR.md).
            _shipRepairPanel = Panel(root, W - 260f, 300, 250, 150).gameObject; // tall enough for the bench names + "short" line
            _shipRepairTitle = UiText.Add(_shipRepairPanel.transform, 10, 6, 230, 18, string.Empty, 14, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            (_shipRepairTrack, _shipRepairBar) = UiHolo.AddBar(_shipRepairPanel.transform, 10, 30, 230, 14, new Color(0.03f, 0.07f, 0.13f), UiKit.Cyan);
            _shipRepairProg = UiText.Add(_shipRepairPanel.transform, 12, 29, 226, 16, string.Empty, 12, UiKit.TextCol, TextAnchor.MiddleLeft);
            _shipRepairHint = UiText.Add(_shipRepairPanel.transform, 10, 50, 230, 66, string.Empty, 12, UiKit.CyanDim, TextAnchor.UpperLeft);
            UiText.Wrap(_shipRepairHint);
            _shipRepairBtn = UiKit.AddButton(_shipRepairPanel.transform, 10, 120, 230, 24, string.Empty, () => Game.Network?.SendRepairShip("all"));
        }

        private void RefreshWreck(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            var wreck = Game.Wreck;
            bool show = wreck != null;
            if (_wreckPanel.activeSelf != show)
            {
                _wreckPanel.SetActive(show);
                if (show) { UiKit.TransitionIn(_wreckPanel); }
            }

            if (!show) return;
            _wreckName.text = wreck.WreckName;
            int done = wreck.Total - wreck.Remaining;
            UiHolo.SetBar(_wreckBar, wreck.Total > 0 ? done / (float)wreck.Total : 0f, 230f);
            _wreckProg.text = $"{loc.Get("ui.wreck.progress")}  {done}/{wreck.Total}";
            bool claim = wreck.Claimable;
            _wreckClaim.gameObject.SetActive(claim);
            if (claim)
            {
                var t = _wreckClaim.GetComponentInChildren<Text>();
                if (t != null) t.text = loc.Get("ui.action.claim");
            }

            // While there are breaches left, tell the player how to repair + which blocks are still needed.
            _wreckHint.gameObject.SetActive(!claim);
            if (!claim)
            {
                string needs = string.Empty;
                if (!string.IsNullOrEmpty(wreck.Needs))
                {
                    var keys = wreck.Needs.Split(',');
                    for (int i = 0; i < keys.Length; i++)
                    {
                        keys[i] = loc.Get($"block.{keys[i]}.name");
                    }

                    needs = "  " + string.Join(", ", keys);
                }

                _wreckHint.text = ExpandKeyTokens(loc, loc.Get("ui.wreck.repair_hint")) + needs; // "press {key:RepairWreck}" (#2502)
            }
        }

        private void RefreshShipRepair(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            var sr = Game.ShipRepair;
            // #2725: not while visiting a docked friend's ship — the panel is about OUR ship, and its "at the helm: R" would
            // point at the host's helm, which a guest cannot use. It returns with us.
            bool show = sr != null && sr.NeedsRepair && !Game.Visiting;
            if (_shipRepairPanel.activeSelf != show)
            {
                _shipRepairPanel.SetActive(show);
                if (show) { UiKit.TransitionIn(_shipRepairPanel); }
            }

            if (!show) return;

            _shipRepairTitle.text = loc.Get("ui.shiprepair.title");

            // #1313: a full hull with missing design cells used to sit under a FULL hull bar — a "repair" panel
            // that showed 100/100 and said nothing about what was wrong. Lead with the breach count in that
            // case; the hull bar and its numbers only appear while the hull itself is short.
            bool hullShort = sr.HullMax > 0f && sr.Hull < sr.HullMax;
            _shipRepairBar.gameObject.SetActive(hullShort);
            _shipRepairTrack.gameObject.SetActive(hullShort); // the empty track sat behind the breach text (#1368)
            _shipRepairProg.text = hullShort
                ? $"{loc.Get("ui.shiprepair.hull")}  {(int)sr.Hull}/{(int)sr.HullMax}"
                : string.Format(loc.Get(sr.MissingCells == 1 ? "ui.shiprepair.cells_missing_one" : "ui.shiprepair.cells_missing"), sr.MissingCells); // singular for one ("1 Hüllenzelle fehlt", #1368)
            if (hullShort)
            {
                UiHolo.SetBar(_shipRepairBar, sr.Hull / sr.HullMax, 230f);
            }

            // List the materials the full repair needs (item:count pairs from the server), localized — and the bench
            // each is made at. The panel alone once sent a pilot to a factory: it said "Eisenplatte ×10", the crafting
            // list's first "Eisenplatte" card was the factory twin, so a factory looked required (2026-10-09).
            string needs = string.Empty;
            if (!string.IsNullOrEmpty(sr.Needs))
            {
                var parts = sr.Needs.Split(',');
                var benches = new string[parts.Length];
                bool oneBench = true;
                for (int i = 0; i < parts.Length; i++)
                {
                    var kv = parts[i].Split(':');
                    string name = loc.Get($"item.{kv[0]}.name");
                    if (name.StartsWith("item.")) { name = loc.Get($"block.{kv[0]}.name"); } // fall back for raw block keys
                    parts[i] = kv.Length > 1 ? $"{name} ×{kv[1]}" : name;
                    benches[i] = RepairMaterialBench(kv[0], loc);
                    oneBench &= benches[i] == benches[0];
                }

                if (oneBench && benches[0].Length > 0)
                {
                    needs = "  " + string.Join(", ", parts) + " · " + benches[0]; // one bench for everything: name it once
                }
                else
                {
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (benches[i].Length > 0) { parts[i] += " · " + benches[i]; }
                    }

                    needs = "  " + string.Join(", ", parts);
                }
            }

            // The breach count already leads the panel when the hull is full — only repeat it next to the
            // materials while the top line is busy showing hull numbers.
            string cells = hullShort && sr.MissingCells > 0 ? $"  ({sr.MissingCells} {loc.Get("ui.shiprepair.cells")})" : string.Empty;
            // #1561: say what is still short — the server computes CanAfford but the panel never showed it, so a
            // player with "Material vorhanden" (a hull-plating MODULE, no plates) saw a button that did nothing.
            string missing = sr.CanAfford ? string.Empty : "\n" + loc.Get("ui.shiprepair.missing");

            // How to trigger the repair from HERE. The panel used to say "press R at the cockpit" everywhere — in the
            // pilot seat (where R is the target lock), out on the pad and on a gamepad that left the player stuck
            // ("the button is unreachable", 2026-10-09). The button itself works on touch only: in play the cursor
            // stays locked (#413 arbiter) and the HUD never frees it, so everywhere else it is hidden and the key named.
            bool touch = InputMap.ActiveDevice == InputDeviceKind.Touch;
            string how;
            if (touch)
            {
                how = loc.Get("ui.shiprepair.how_touch");
            }
            else if (Game.SpaceViewActive && !Game.InEva)
            {
                how = loc.Get("ui.shiprepair.how_flight")
                    .Replace("{enter}", KeyPhrase(loc, InputAction.FlightEnterInterior))
                    .Replace("{key}", KeyPhrase(loc, InputAction.RepairWreck));
            }
            else
            {
                string where = Game.LoadingPlanetType == "ship_interior" ? "ui.shiprepair.how_helm" : "ui.shiprepair.how_foot";
                how = loc.Get(where).Replace("{key}", KeyPhrase(loc, InputAction.RepairWreck));
            }

            _shipRepairHint.text = how + "\n" + loc.Get("ui.shiprepair.needs") + needs + cells + missing;

            _shipRepairBtn.gameObject.SetActive(touch);
            var t = _shipRepairBtn.GetComponentInChildren<Text>();
            if (t != null) { t.text = loc.Get("ui.shiprepair.repair"); }
            _shipRepairBtn.interactable = sr.CanAfford;
        }

        /// <summary>How the player triggers an action on the active device, for a hint sentence: "press R" on the
        /// keyboard, "Actions (L3) → Repair (ship / wreck)" on a gamepad — the on-foot verbs have no pad button of
        /// their own and live in the Actions list (<see cref="ContextActionsUi"/>).</summary>
        private static string KeyPhrase(BlocksBeyondTheStars.Shared.Localization.Localizer loc, InputAction action)
            => InputMap.ActiveDevice == InputDeviceKind.Gamepad
                ? loc.Get("ui.shiprepair.key_pad")
                    .Replace("{glyph}", InputMap.Glyph(InputAction.ContextActions))
                    .Replace("{action}", loc.Get(InputMap.LabelKey(action)))
                : loc.Get("ui.shiprepair.key_press").Replace("{glyph}", InputMap.Glyph(action));

        /// <summary>The bench a repair material is made at, named as the crafting menu names stations ("Werkbank"):
        /// the station of the item's shallowest everyday recipe. Factory and vendor recipes don't count — a factory is
        /// a rare find and a barter deal is not a way to MAKE the item. Empty for a raw item (mined or looted) or a
        /// station without a name key.</summary>
        private string RepairMaterialBench(string itemKey, BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            var content = Game.Content;
            if (content == null)
            {
                return string.Empty;
            }

            BlocksBeyondTheStars.Shared.Definitions.RecipeDefinition best = null;
            int bestDepth = int.MaxValue;
            foreach (var r in content.Recipes.Values)
            {
                if (r.Station == BlocksBeyondTheStars.Shared.Definitions.CraftingStation.Factory
                    || r.Station == BlocksBeyondTheStars.Shared.Definitions.CraftingStation.Market)
                {
                    continue;
                }

                bool makesIt = false;
                foreach (var o in r.Outputs)
                {
                    if (o.Item == itemKey) { makesIt = true; break; }
                }

                if (!makesIt)
                {
                    continue;
                }

                int depth = content.MaxInputDepth(r.Inputs);
                if (depth < bestDepth)
                {
                    bestDepth = depth;
                    best = r;
                }
            }

            if (best == null)
            {
                return string.Empty;
            }

            string key = "ui.craft.station_" + best.Station.ToString().ToLowerInvariant();
            return loc.Has(key) ? loc.Get(key) : string.Empty;
        }
    }
}
