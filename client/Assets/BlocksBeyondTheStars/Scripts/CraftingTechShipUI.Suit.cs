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
    /// The Suit tab of <see cref="CraftingTechShipUI"/> (#2288): the paper-doll figure with its equipment slots,
    /// the active-gear section and the slot picker (#2289) that wears a piece from the backpack or the cargo hold.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        // ---------------- #2288: the Suit tab — a paper doll ----------------

        // The figure's geometry in list coordinates: its centre line, the two slot columns and the row pitch of a slot
        // (cell + its name). Left: head, chest, legs, feet — right: back, tank, liner — the modules in a row under it.
        private const float DollCx = 384f, DollLeftX = 40f, DollRightX = 652f, DollRow = 100f;

        private static readonly EquipSlot[] DollLeft = { EquipSlot.Head, EquipSlot.Chest, EquipSlot.Legs, EquipSlot.Feet };
        private static readonly EquipSlot[] DollRight = { EquipSlot.Back, EquipSlot.Tank, EquipSlot.Liner };

        /// <summary>The Suit tab as a paper doll (#2288): the status line, a friendly figure with its slots around it and
        /// the four module slots under it, the passive hint, and what the worn gear can actively do. Any slot opens the
        /// picker (#2289); a worn piece also fills the detail pane (its description and Take off). The slots are Buttons,
        /// so the pad walks them by geometry like the backpack grid.</summary>
        private float BuildSuitPage()
        {
            float top = AddSuitStatus(0f) + 4f;

            // The thin lines first, so the figure and the slots cover their ends: each slot points at what it dresses.
            for (int i = 0; i < DollLeft.Length; i++)
            {
                var (ax, ay) = DollAnchor(DollLeft[i]);
                AddDollLine(DollLeftX + GridCell, top + i * DollRow + GridCell / 2f, DollCx + ax, top + ay);
            }

            for (int i = 0; i < DollRight.Length; i++)
            {
                var (ax, ay) = DollAnchor(DollRight[i]);
                AddDollLine(DollRightX, top + i * DollRow + GridCell / 2f, DollCx + ax, top + ay);
            }

            AddDollFigure(top);
            for (int i = 0; i < DollLeft.Length; i++)
            {
                AddSuitSlot(DollLeftX, top + i * DollRow, DollLeft[i]);
            }

            for (int i = 0; i < DollRight.Length; i++)
            {
                AddSuitSlot(DollRightX, top + i * DollRow, DollRight[i]);
            }

            // The modules (#2293): four slots in a row under the figure.
            float y = top + DollLeft.Length * DollRow + 4f;
            UiKit.AddText(_listContent, 8, y, 752, 26, L("ui.equip.modules"), 18, UiKit.Cyan, TextAnchor.UpperCenter, FontStyle.Bold);
            y += 30f;
            const float modulePitch = GridCell + 20f;
            int modules = EquipSlots.ModuleSlots.Count;
            float mx = DollCx - (modules * modulePitch - 20f) / 2f;
            for (int i = 0; i < modules; i++)
            {
                AddSuitSlot(mx + i * modulePitch, y, EquipSlots.ModuleSlots[i]);
            }

            y += GridCell + 30f;

            // Nothing worn at all (an old save, everything taken off): the getting-started line instead of the rules.
            var hint = UiKit.AddText(_listContent, 8, y, 752, 40, L(Game.WornKeys().Any() ? "ui.suit.passive_hint" : "ui.suit.none"),
                16, UiKit.CyanDim, TextAnchor.UpperLeft);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            y += Mathf.Max(40f, hint.preferredHeight) + 16f;
            return AddActiveGearSection(y);
        }

        /// <summary>Where a slot's line meets the figure, relative to its centre line and its top.</summary>
        private static (float X, float Y) DollAnchor(EquipSlot slot) => slot switch
        {
            EquipSlot.Head => (-30f, 40f),
            EquipSlot.Chest => (-30f, 140f),
            EquipSlot.Legs => (-27f, 285f),
            EquipSlot.Feet => (-30f, 348f),
            EquipSlot.Back => (40f, 100f),
            EquipSlot.Tank => (40f, 155f),
            _ => (40f, 210f), // the liner
        };

        /// <summary>The figure: a round helmet with its visor, the body, two arms, two legs and boots — soft holo shapes in
        /// the HUD's dim cyan. Not a mannequin; it only shows where each piece goes. Purely decorative (no raycasts).</summary>
        private void AddDollFigure(float top)
        {
            var fill = new Color(UiKit.CyanDim.r, UiKit.CyanDim.g, UiKit.CyanDim.b, 0.22f);
            AddDollPart(DollCx - 90f, top + 90f, 28f, 136f, 14f, fill);  // the arms first: the body covers their shoulders
            AddDollPart(DollCx + 62f, top + 90f, 28f, 136f, 14f, fill);
            AddDollPart(DollCx - 50f, top + 228f, 46f, 112f, 16f, fill); // the legs
            AddDollPart(DollCx + 4f, top + 228f, 46f, 112f, 16f, fill);
            AddDollPart(DollCx - 60f, top + 334f, 58f, 26f, 10f, fill);  // the boots
            AddDollPart(DollCx + 2f, top + 334f, 58f, 26f, 10f, fill);
            AddDollPart(DollCx - 56f, top + 84f, 112f, 150f, 22f, fill); // the body
            AddDollPart(DollCx - 36f, top + 4f, 72f, 72f, 36f, fill);    // the round helmet
            AddDollPart(DollCx - 24f, top + 26f, 48f, 26f, 12f, new Color(0.04f, 0.10f, 0.20f, 0.85f)); // its visor
        }

        private void AddDollPart(float x, float y, float w, float h, float radius, Color fill)
        {
            var part = UiHolo.AddPanel(_listContent, x, y, w, h, fill, radius, 1.5f, 0.8f);
            part.raycastTarget = false;
        }

        /// <summary>A thin line from (x1, y1) to (x2, y2) in list coordinates (y down): one image, turned about its start.</summary>
        private void AddDollLine(float x1, float y1, float x2, float y2)
        {
            float dx = x2 - x1, dy = y2 - y1;
            var line = UiKit.AddImage(_listContent, x1, y1, Mathf.Sqrt(dx * dx + dy * dy), 2f, UiKit.SolidSprite,
                new Color(UiKit.CyanDim.r, UiKit.CyanDim.g, UiKit.CyanDim.b, 0.7f));
            var rt = line.rectTransform;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(x1, -y1);
            rt.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(dy, dx) * Mathf.Rad2Deg); // UI space turns y up
        }

        /// <summary>One slot of the figure: the worn piece's icon (an empty frame when nothing is worn) and the slot's name.</summary>
        private void AddSuitSlot(float x, float y, EquipSlot slot)
        {
            int s = (int)slot;
            string worn = Game.ItemInEquipSlot(s);
            var b = AddGridSlot(x, y, worn, 1, () => OnSuitSlotClicked(s));
            if (!string.IsNullOrEmpty(worn) && _selected == "inv:" + worn)
            {
                Highlight(b);
            }

            UiKit.AddText(_listContent, x - 10f, y + GridCell + 2f, GridCell + 20f, 18, L(EquipSlots.LabelKey(slot)), 12, UiKit.CyanDim, TextAnchor.UpperCenter);
        }

        /// <summary>A slot of the figure was clicked: a worn piece fills the detail pane, and the picker opens (#2289).</summary>
        private void OnSuitSlotClicked(int slot)
        {
            ClearPick();
            string worn = Game.ItemInEquipSlot(slot);
            if (!string.IsNullOrEmpty(worn))
            {
                _selected = "inv:" + worn;
                RebuildList(); // the highlight follows the selection
                RebuildDetail();
            }

            ShowSlotPicker(slot, opening: true);
        }

        /// <summary>"Can be used actively" (#2288): the worn gear that does something on a key — the lamp, the jetpack, the
        /// glider, the stealth suit — with how to use it, in the HUD gear strip's order (<see cref="HudUi.ActiveGear"/>).</summary>
        private float AddActiveGearSection(float y)
        {
            UiKit.AddText(_listContent, 8, y, 752, 26, L("ui.suit.active_title"), 18, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 32f;
            bool any = false;
            foreach (var key in HudUi.ActiveGear)
            {
                if (!Game.Wears(key))
                {
                    continue;
                }

                any = true;
                var sprite = IconResolver.Resolve(key, Game);
                if (sprite != null)
                {
                    UiKit.AddIconSprite(_listContent, 12, y, 36, sprite, Color.white);
                }

                UiKit.AddText(_listContent, 58, y, 230, 36, ItemName(key), 18, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                var how = UiKit.AddText(_listContent, 296, y, 464, 36, ActiveGearHowTo(key), 17, UiKit.CyanDim, TextAnchor.MiddleLeft);
                UiKit.FitLabel(how, 11, 17);
                y += 44f;
            }

            if (!any)
            {
                var none = UiKit.AddText(_listContent, 8, y, 752, 40, L("ui.suit.active_none"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
                none.horizontalOverflow = HorizontalWrapMode.Wrap;
                y += Mathf.Max(40f, none.preferredHeight) + 8f;
            }

            return y;
        }

        /// <summary>How an active piece is used, with the player's real control in it (#2288).</summary>
        private string ActiveGearHowTo(string key) => key switch
        {
            "suit_lamp" => L("ui.suit.active_lamp").Replace("{key}", ActionKeyText(InputAction.ToggleLamp)),
            "jetpack" => L("ui.suit.active_jetpack").Replace("{key}", HudUi.JumpGlyph(Game.Localizer)),
            "glider" => L("ui.suit.active_glider").Replace("{key}", HudUi.JumpGlyph(Game.Localizer)),
            _ => L("ui.suit.active_stealth").Replace("{key}", ActionKeyText(InputAction.ToggleStealth)),
        };

        /// <summary>The control of an action as the HUD names it; on a tablet the ACT list, which carries the lamp and the cloak.</summary>
        private string ActionKeyText(InputAction action)
            => InputMap.ActiveDevice == InputDeviceKind.Touch ? L("ui.touch.actions") : HudUi.ActionGlyph(Game.Localizer, action);

        // ---------------- #2289: the slot picker ----------------

        private GameObject _pickerCanvas; // the open picker's own canvas, or null
        private int _pickerSlot = -1;     // the equipment slot it was opened for
        private int _pickerSig;           // the inventory it was built from (see Update)

        // Effects that are not a number: what a piece does on a key or by being worn (#2289).
        private static readonly Dictionary<string, string> KeyEffects = new Dictionary<string, string>
        {
            ["suit_lamp"] = "ui.equip.effect.lamp",
            ["jetpack"] = "ui.equip.effect.jetpack",
            ["glider"] = "ui.equip.effect.glider",
            ["stealth_suit"] = "ui.equip.effect.stealth",
            ["comm_radio"] = "ui.equip.effect.radio_planet",
            ["system_radio"] = "ui.equip.effect.radio_system",
            ["galaxy_radio"] = "ui.equip.effect.radio_galaxy",
            ["radar_scanner"] = "ui.equip.effect.radar",
            ["oxygen_extractor"] = "ui.equip.effect.extractor",
        };

        /// <summary>The slot picker (#2289): a modal over the menu with every piece that fits <paramref name="slot"/> — from
        /// the backpack, and aboard from the cargo hold too — what it does and where it lies. The piece worn there now sits
        /// on top with Take off. A pick wears it (the server checks the slot and the hold); with nothing to wear the picker
        /// points at the recipe, or at the research. Esc / pad B close it before the menu (<see cref="CloseSlotPicker"/>).
        /// <paramref name="opening"/> plays the modal's open effect — false when an inventory change rebuilds it in place.</summary>
        private void ShowSlotPicker(int slot, bool opening = false)
        {
            CloseSlotPicker();
            if (slot < 0 || slot >= EquipSlots.Count || Game?.Content == null || _canvas == null)
            {
                return;
            }

            var equipSlot = (EquipSlot)slot;
            string slotName = L(EquipSlots.LabelKey(equipSlot));
            string worn = Game.ItemInEquipSlot(slot);

            var canvas = UiKit.CreateCanvas("Suit Slot Picker");
            canvas.sortingOrder = 61; // above the Tab menu (50) — the discard confirm's tier
            _pickerCanvas = canvas.gameObject;
            _pickerSlot = slot;
            UiNav.SetSuspended(_canvas.gameObject, true); // the pad belongs to the picker until it closes
            UiNav.Enable(canvas.gameObject);

            const float pw = 760f, ph = 700f, rowH = 80f;
            var (overlay, panel) = UiKit.AddModalOverlay(canvas.transform, (W - pw) / 2f, (H - ph) / 2f, pw, ph);
            if (opening)
            {
                UiKit.OpenModal(overlay); // #2302: the open effect — on opening only; an inventory echo rebuilds it without
            }

            var title = UiKit.AddText(panel, 24f, 18f, pw - 48f, 44f, L("ui.equip.picker_title").Replace("{slot}", slotName),
                26, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabel(title, 14, 26);

            var list = MakeScroll(panel, 24f, 74f, pw - 48f, ph - 74f - 88f);
            float rowW = pw - 48f - 14f; // clear of the inline scrollbar
            float y = 0f;

            // What is worn here now, on top: marked, with Take off.
            if (!string.IsNullOrEmpty(worn))
            {
                UiKit.AddPanel(list, 0f, y, rowW, rowH, new Color(UiKit.SlotSelected.r, UiKit.SlotSelected.g, UiKit.SlotSelected.b, 0.45f));
                AddPickerRowContent(list, y, rowW - 340f, worn);
                UiKit.AddText(list, rowW - 330f, y, 130f, rowH, L("ui.equip.title"), 18, UiKit.Ok, TextAnchor.MiddleRight, FontStyle.Bold);
                UiKit.AddButton(list, rowW - 190f, y + 15f, 180f, 50f, L("ui.equip.unequip"), () =>
                {
                    Game.Network?.SendUnequipItem(slot, -1);
                    CloseSlotPicker();
                    ClearPick();
                });
                y += rowH + 8f;
            }

            foreach (var row in PickerRows(equipSlot))
            {
                var stack = row.Stack;
                bool fromCargo = row.Cargo;
                var b = UiKit.AddButton(list, 0f, y, rowW, rowH, string.Empty, () =>
                {
                    Game.Network?.SendEquipItem(stack.Slot, slot, fromCargo);
                    ClientAudio.Instance?.Cue("gear_equip");
                    _selected = "inv:" + stack.Item; // the detail pane follows the piece now worn
                    CloseSlotPicker();
                    ClearPick();
                });
                AddPickerRowContent(b.transform, 0f, rowW - 220f, stack.Item);
                UiKit.AddText(b.transform, rowW - 206f, 0f, 190f, rowH, L(fromCargo ? "ui.cargo.title" : "ui.inventory.backpack"),
                    16, UiKit.Cyan, TextAnchor.MiddleRight);
                y += rowH + 8f;
            }

            // Nothing to wear here and nothing worn: what it takes — the recipe once its blueprint is known, else research.
            if (y <= 0f)
            {
                var none = UiKit.AddText(list, 8f, y, rowW - 16f, 34f, L("ui.equip.picker_empty").Replace("{slot}", slotName),
                    22, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
                none.horizontalOverflow = HorizontalWrapMode.Wrap;
                y += Mathf.Max(34f, none.preferredHeight) + 12f;
                var recipe = PickerRecipeFor(equipSlot);
                if (recipe != null)
                {
                    string recipeKey = recipe.Key;
                    UiKit.AddButton(list, 8f, y, 320f, 52f, L("ui.equip.picker_recipe"), () =>
                    {
                        CloseSlotPicker();
                        JumpToRecipe(recipeKey);
                    });
                    y += 64f;
                }
                else
                {
                    var research = UiKit.AddText(list, 8f, y, rowW - 16f, 30f,
                        L("ui.equip.picker_research").Replace("{where}", L("ui.tab.tech") + " → " + L("ui.tech.cat_suit")),
                        18, UiKit.CyanDim, TextAnchor.UpperLeft);
                    research.horizontalOverflow = HorizontalWrapMode.Wrap;
                    y += 40f;
                }
            }

            // On foot the hold is out of reach — more may wait there.
            if (!AboardShipNow())
            {
                var cargoHint = UiKit.AddText(list, 8f, y + 4f, rowW - 16f, 30f, L("ui.equip.picker_cargo_hint"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
                cargoHint.horizontalOverflow = HorizontalWrapMode.Wrap;
                y += 44f;
            }

            SetContentHeight(list, y);
            UiKit.AddButton(panel, pw - 24f - 220f, ph - 72f, 220f, 52f, L("ui.action.close"), () => CloseSlotPicker());
        }

        /// <summary>Closes the slot picker (#2289) and hands the pad back to the menu. True when one was open — GameMenu lets
        /// Esc / pad B close the picker first, so the menu behind it stays.</summary>
        public bool CloseSlotPicker()
        {
            _pickerSlot = -1;
            if (_pickerCanvas == null)
            {
                return false;
            }

            Destroy(_pickerCanvas);
            _pickerCanvas = null;
            if (_canvas != null)
            {
                UiNav.SetSuspended(_canvas.gameObject, false);
            }

            return true;
        }

        /// <summary>The pieces a slot can take (#2289): one row per stack in the backpack and — aboard — the cargo hold whose
        /// item names this slot; for a module slot without the modules already worn in the other three. Backpack first,
        /// then the hold, the best piece first in each.</summary>
        private List<(NetItemStack Stack, bool Cargo)> PickerRows(EquipSlot slot)
        {
            var otherModules = new HashSet<string>();
            if (EquipSlots.IsModule(slot))
            {
                foreach (var m in EquipSlots.ModuleSlots)
                {
                    string w = m == slot ? string.Empty : Game.ItemInEquipSlot((int)m);
                    if (!string.IsNullOrEmpty(w))
                    {
                        otherModules.Add(ItemKey.Base(w));
                    }
                }
            }

            var rows = new List<(NetItemStack Stack, bool Cargo, float Rank)>();
            void Collect(NetItemStack[] stacks, bool cargo)
            {
                if (stacks == null)
                {
                    return;
                }

                foreach (var s in stacks)
                {
                    if (s == null || s.Count <= 0 || string.IsNullOrEmpty(s.Item))
                    {
                        continue;
                    }

                    string key = ItemKey.Base(s.Item);
                    var def = Game.Content.GetItem(key);
                    if (def != null && EquipSlots.Accepts(slot, def.EquipSlot) && !otherModules.Contains(key))
                    {
                        rows.Add((s, cargo, SuitEquipment.Rank(def)));
                    }
                }
            }

            Collect(Game.Personal, false);
            if (AboardShipNow())
            {
                Collect(Game.Cargo, true); // wearing straight from the hold is aboard only (the server checks it too)
            }

            return rows.OrderBy(r => r.Cargo ? 1 : 0).ThenByDescending(r => r.Rank).Select(r => (r.Stack, r.Cargo)).ToList();
        }

        /// <summary>A picker row's icon, name and effect summary at <paramref name="y"/> in <paramref name="parent"/>.</summary>
        private void AddPickerRowContent(Transform parent, float y, float textW, string item)
        {
            var sprite = IconResolver.Resolve(item, Game);
            if (sprite != null)
            {
                UiKit.AddIconSprite(parent, 14f, y + 14f, 52f, sprite, IconResolver.Tint(item, Game));
            }
            else
            {
                UiKit.AddIcon(parent, 14f, y + 14f, 52f, IconFor(ItemKey.Base(item)) ?? "cat_suit");
            }

            var name = UiKit.AddText(parent, 80f, y + 6f, textW - 80f, 36f, ItemName(item), 22, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabel(name, 12, 22);
            var def = Game.Content.GetItem(ItemKey.Base(item));
            var effect = UiKit.AddText(parent, 80f, y + 42f, textW - 80f, 28f, def != null ? EffectSummary(def) : string.Empty,
                16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.FitLabel(effect, 10, 16);
        }

        /// <summary>A piece's effect in a few words (#2289): its numbers ("Armour +20 % · Warmth +15 %"), and for a piece that
        /// works on a key or just by being worn, what it does ("Light", "Cloak", "Contacts on the compass").</summary>
        private string EffectSummary(ItemDefinition def)
        {
            var parts = new List<string>(4);
            void Add(string key, int value)
            {
                if (value > 0)
                {
                    parts.Add(L(key).Replace("{value}", value.ToString()));
                }
            }

            Add("ui.equip.effect.armor", Mathf.RoundToInt(def.ArmorResistance * 100f));
            Add("ui.equip.effect.warmth", Mathf.RoundToInt(def.ThermalInsulation * 100f));
            Add("ui.equip.effect.oxygen", Mathf.RoundToInt(def.OxygenBonus));
            Add("ui.equip.effect.acid", Mathf.RoundToInt(def.CorrosionResistance * 100f));
            Add("ui.equip.effect.fall", Mathf.RoundToInt(def.FallProtection * 100f));
            Add("ui.equip.effect.grip", Mathf.RoundToInt(def.ClimbGrip * 100f));
            if (def.ClimbIce)
            {
                parts.Add(L("ui.equip.effect.ice"));
            }

            Add("ui.equip.effect.jump", Mathf.RoundToInt(def.JumpBoost * 100f));
            Add("ui.equip.effect.energy", Mathf.RoundToInt(def.SuitEnergyBonus));
            if (KeyEffects.TryGetValue(def.Key, out var effectKey))
            {
                parts.Add(L(effectKey));
            }

            return string.Join("  ·  ", parts);
        }

        /// <summary>The first recipe whose piece fits <paramref name="slot"/>, is not worn yet and whose blueprint is known —
        /// the picker's "Show recipe" (#2289); null while none is known.</summary>
        private RecipeDefinition PickerRecipeFor(EquipSlot slot)
        {
            foreach (var r in Game.Content.Recipes.Values)
            {
                var output = r.Outputs.FirstOrDefault();
                if (output == null || r.Station == BlocksBeyondTheStars.Shared.Definitions.CraftingStation.Market)
                {
                    continue;
                }

                string key = ItemKey.Base(output.Item);
                var def = Game.Content.GetItem(key);
                if (def != null && EquipSlots.Accepts(slot, def.EquipSlot) && !Game.Wears(key)
                    && (FreeCrafting() || BlueprintOk(r.RequiredBlueprint)))
                {
                    return r;
                }
            }

            return null;
        }

        /// <summary>The equipment slot a worn item sits in, or −1.</summary>
        private int WornSlotOf(string item)
        {
            if (Game.Equipment == null)
            {
                return -1;
            }

            foreach (var s in Game.Equipment)
            {
                if (s.Item == item && s.Count > 0)
                {
                    return s.Slot;
                }
            }

            return -1;
        }

        /// <summary>The suit's current passive effects — armour, maximum oxygen, insulation — computed with the
        /// server's own formula (<see cref="BlocksBeyondTheStars.Shared.State.SuitEquipment"/>) from what is worn
        /// (#1270, #2110): the line on top of the Suit tab's figure (#2288).</summary>
        private float AddSuitStatus(float y)
        {
            var defs = Game.Content.Items.Values;
            bool Carried(string key) => Game.Wears(key); // #2110: worn, not carried

            // #2206: what the bio lab changed on a worn piece joins the formula of its stat, under that formula's cap —
            // the server's sums, so the line shows the numbers the suit really has.
            var worn = Game.WornKeys();
            int armor = Mathf.RoundToInt(Mathf.Min(BlocksBeyondTheStars.Shared.State.SuitEquipment.MaxArmorResistance,
                BlocksBeyondTheStars.Shared.State.SuitEquipment.ArmorResistance(defs, Carried)
                + BlocksBeyondTheStars.Shared.Bio.GearMods.Bonus(worn, BlocksBeyondTheStars.Shared.Bio.ModStat.Armor)) * 100f);
            int oxygen = Mathf.RoundToInt(BlocksBeyondTheStars.Shared.State.SuitEquipment.MaxOxygen(defs, Carried)
                + BlocksBeyondTheStars.Shared.Bio.GearMods.Bonus(worn, BlocksBeyondTheStars.Shared.Bio.ModStat.Oxygen));
            int insulation = Mathf.RoundToInt(Mathf.Min(BlocksBeyondTheStars.Shared.State.SuitEquipment.MaxThermalInsulation,
                BlocksBeyondTheStars.Shared.State.SuitEquipment.ThermalInsulation(defs, Carried)
                + BlocksBeyondTheStars.Shared.Bio.GearMods.Bonus(worn, BlocksBeyondTheStars.Shared.Bio.ModStat.Insulation)) * 100f);
            string line = L("ui.suit.status_armor").Replace("{value}", armor.ToString())
                + "   ·   " + L("ui.suit.status_oxygen").Replace("{value}", oxygen.ToString())
                + "   ·   " + L("ui.suit.status_insulation").Replace("{value}", insulation.ToString());
            UiKit.AddText(_listContent, 8, y, 752, 32, line, 18, UiKit.TextCol, TextAnchor.MiddleLeft);
            return y + 38f;
        }
    }
}
