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
    /// The Inventory tab of <see cref="CraftingTechShipUI"/>: the backpack grid with the quick-bar and its
    /// click-to-pick moves (#2110), the cargo hold's stacks, the Creative "All items" catalog (#1930) and the
    /// "throw away this gear?" dialog.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        // Quick-bar = the first N personal-inventory slots (must match the server's HotbarSlots / HudUi Slots).
        private const int QuickSlots = 9;

        // The personal inventory's size (quick-bar 0..8 + the backpack) — the server sends it (#2110: 36); the snapshot
        // only carries occupied slots, so free slots derive from this.
        private int PersonalSlotTotal => Game != null ? Game.PersonalSlots : 24;

        // #2110: the grid's click-to-pick state — the backpack/quick-bar slot a click picked up ("inv" + its index), or
        // none. The next click puts it down or swaps. Wearing lives on the Suit tab's figure since #2288.
        private string _pickKind = string.Empty;
        private int _pickIndex = -1;

        // #2729: the stack the detail pane's "This stack" moves — the backpack slot last picked on the backpack page, the
        // hold slot last clicked on the cargo page. Resolved against the live snapshot (CargoStackPicker.ResolveSlot), so
        // a stale slot falls back to the first stack of the selected item.
        private int _stackSlot = -1;

        private float BuildInventoryList()
        {
            if (_category == "catalog")
            {
                if (CatalogAvailable())
                {
                    return BuildCatalogList();
                }

                _category = "personal"; // the world left Sandbox while the page was open
            }

            if (_category == "personal")
            {
                return BuildInventoryGrid(); // #2110: the nine-wide slot grid
            }

            if (_category == "suit")
            {
                return BuildSuitPage(); // #2288: the paper doll
            }

            if (_category == "samples")
            {
                return BuildSampleList(); // #2201: the sample case
            }

            var items = _category == "cargo" ? Game.Cargo : Game.Personal;
            float y = 0f;

            // Cargo transfer controls. The hold only exists while aboard the ship (in flight or in the landed
            // cabin), so the bulk move buttons + capacity readout show only then; on foot the cargo tab explains
            // why it's empty instead of looking dead.
            if (AboardShipNow() && _category == "cargo")
            {
                int used = items?.Length ?? 0;
                UiKit.AddText(_listContent, 8, y, 752, 32, $"{L("ui.cargo.capacity")}: {used}/{Game.CargoSlots}", 18, UiKit.CyanDim, TextAnchor.MiddleLeft);
                y += 40f;

                // Both directions on the hold's own page — E at the cargo station opens here (#2138).
                UiKit.AddButton(_listContent, 8, y, 372, 44, L("ui.cargo.stow_all"),
                    () => Game.Network?.SendMoveCargoItem(toCargo: true, item: string.Empty, bulkAll: true));
                UiKit.AddButton(_listContent, 388, y, 372, 44, L("ui.cargo.take_all"),
                    () => Game.Network?.SendMoveCargoItem(toCargo: false, item: string.Empty, bulkAll: true));
                y += 56f;
            }
            else if (_category == "cargo")
            {
                UiKit.AddText(_listContent, 8, y, 752, 30, L("ui.cargo.not_aboard"), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                y += 40f;
            }

            if (items == null || items.Length == 0)
            {
                UiKit.AddText(_listContent, 8, y + 8, 700, 30, "—", 22, UiKit.CyanDim, TextAnchor.UpperLeft);
                return y + 40f;
            }

            // #2729: aboard, every stack in the hold gets its own "Take" button — exactly that stack into the backpack
            // (Shift: every stack of that kind), like a click on the crate screen (#2436). The card itself opens the detail
            // pane: this stack, all of this kind, or how many — the same choices for pad and touch, which have no Shift.
            bool perStack = AboardShipNow() && _category == "cargo";
            if (perStack)
            {
                var hint = UiKit.AddText(_listContent, 8, y, 752, 44, L("ui.cargo.stack_hint"), 14, UiKit.CyanDim, TextAnchor.UpperLeft);
                hint.horizontalOverflow = HorizontalWrapMode.Wrap;
                y += 50f;
            }

            string selectedItem = _selected.StartsWith("inv:", System.StringComparison.Ordinal) ? _selected.Substring(4) : string.Empty;
            int litSlot = perStack ? CargoStackPicker.ResolveSlot(items, selectedItem, _stackSlot) : -1;
            foreach (var s in items)
            {
                var stack = s;
                // Only the chosen stack lights up, not every stack of the same item (the key only drives the highlight).
                string key = !perStack || stack.Slot == litSlot ? "inv:" + stack.Item : "inv:" + stack.Item + "@" + stack.Slot;
                AddCard(y, ItemName(stack.Item), IconFor(stack.Item), "×" + stack.Count, UiKit.CyanDim, key, () =>
                {
                    _selected = "inv:" + stack.Item;
                    _stackSlot = stack.Slot;
                    RebuildList();
                    RebuildDetail();
                }, contentKey: stack.Item, trimRight: perStack ? TakeButtonW + 8f : 0f);
                if (perStack)
                {
                    UiKit.AddButton(_listContent, 780f - TakeButtonW, y, TakeButtonW, 78f, L("ui.cargo.take_stack"),
                        () => MoveCargoStack(false, stack.Slot, stack.Item, ShiftHeld()));
                }

                y += 88f;
            }

            return y;
        }

        // #2729: the width of a hold card's "Take" button.
        private const float TakeButtonW = 132f;

        private static bool ShiftHeld() => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        /// <summary>#2729: moves exactly one stack between the backpack and the hold — the one in <paramref name="slot"/> of
        /// the source side — or, with <paramref name="allOfKind"/> (Shift, or the "All of this kind" button), every stack of
        /// <paramref name="item"/>; with <paramref name="count"/> &gt; 0 that many of the item instead ("take 5 along").
        /// The server checks what fits and answers with the new inventory.</summary>
        private void MoveCargoStack(bool toCargo, int slot, string item, bool allOfKind, int count = 0)
        {
            if (string.IsNullOrEmpty(item))
            {
                return;
            }

            if (allOfKind)
            {
                Game.Network?.SendMoveCargoItem(toCargo: toCargo, item: item);
            }
            else if (count > 0)
            {
                Game.Network?.SendMoveCargoItem(toCargo: toCargo, item: item, count: count);
            }
            else if (slot >= 0)
            {
                Game.Network?.SendMoveCargoItem(toCargo: toCargo, item: item, fromSlot: slot);
            }
            else
            {
                return; // no stack of it on this side any more
            }

            if (toCargo && _pickKind == "inv")
            {
                ClearPick(); // the picked backpack stack is on its way into the hold
            }

            ClientAudio.Instance?.Cue("loot", 0.35f);
        }

        // ---------------- #2110: the inventory grid (Justus: "wie in Minecraft") ----------------

        private const float GridCell = 76f, GridPitch = 84f, GridX0 = 8f;

        /// <summary>The personal inventory as slots: the backpack rows and, under a line, the quick-bar — nine wide.
        /// Click-to-pick / click-to-place (the hotbar swap's model): identical on mouse, touch and gamepad (the buttons
        /// navigate by geometry). Two clicks move or swap. What is worn lives on the Suit tab's figure (#2288); a piece
        /// picked here still gets Wear in the detail pane.</summary>
        private float BuildInventoryGrid()
        {
            float y = 0f;
            if (AboardShipNow())
            {
                UiKit.AddButton(_listContent, 8, y, 752, 44, L("ui.cargo.stow_all"),
                    () => Game.Network?.SendMoveCargoItem(toCargo: true, item: string.Empty, bulkAll: true));
                y += 56f;
            }

            // The backpack rows (slots 9..N-1).
            UiKit.AddText(_listContent, 8, y, 752, 26, L("ui.inventory.backpack"), 18, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 30f;
            int total = PersonalSlotTotal;
            int packSlots = Mathf.Max(0, total - QuickSlots);
            int rows = (packSlots + QuickSlots - 1) / QuickSlots;
            for (int i = 0; i < packSlots; i++)
            {
                int index = QuickSlots + i;
                float x = GridX0 + (i % QuickSlots) * GridPitch;
                float yy = y + (i / QuickSlots) * GridPitch;
                var b = AddGridSlot(x, yy, Game.ItemInSlot(index), Game.CountInSlot(index), () => OnInventorySlotClicked(index));
                if (_pickKind == "inv" && _pickIndex == index)
                {
                    Highlight(b);
                }
            }

            y += rows * GridPitch + 6f;

            // A clean line, then the quick-bar row (slots 0..8, numbered).
            var line = new GameObject("Separator", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(_listContent, false);
            UiKit.Place(line, 8f, y, 752f, 2f);
            line.GetComponent<Image>().color = UiKit.CyanDim;
            line.GetComponent<Image>().raycastTarget = false;
            y += 10f;
            UiKit.AddText(_listContent, 8, y, 752, 26, L("ui.inventory.hotbar"), 18, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 30f;
            for (int k = 0; k < QuickSlots; k++)
            {
                int index = k;
                float x = GridX0 + k * GridPitch;
                var b = AddGridSlot(x, y, Game.ItemInSlot(index), Game.CountInSlot(index), () => OnInventorySlotClicked(index));
                var num = UiKit.AddText(b.transform, 5f, 3f, 24f, 18f, (k + 1).ToString(), 12, UiKit.CyanDim, TextAnchor.UpperLeft, FontStyle.Bold);
                UiKit.AddOutline(num);
                if (_pickKind == "inv" && _pickIndex == index)
                {
                    Highlight(b);
                }
            }

            y += GridCell + 12f;
            var hint = UiKit.AddText(_listContent, 8, y, 752, 44, L("ui.inventory.pick_hint"), 14, UiKit.CyanDim, TextAnchor.UpperLeft);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            return y + 50f;
        }

        private static void Highlight(Button b)
        {
            var img = b.GetComponent<Image>();
            if (img != null)
            {
                img.color = UiKit.Cyan;
            }
        }

        /// <summary>One slot button: the item's icon (shape silhouette → painted design → atlas tile → generated icon,
        /// the hotbar's resolution) and its count; empty slots are plain frames.</summary>
        private Button AddGridSlot(float x, float y, string item, int count, System.Action onClick)
        {
            var b = UiKit.AddButton(_listContent, x, y, GridCell, GridCell, string.Empty, onClick);
            if (string.IsNullOrEmpty(item))
            {
                return b;
            }

            var go = new GameObject("ItemIcon", typeof(RectTransform));
            go.transform.SetParent(b.transform, false);
            UiKit.Place(go, 8f, 8f, GridCell - 16f, GridCell - 16f);
            var raw = go.AddComponent<RawImage>();
            raw.raycastTarget = false;

            var blockDef = Game.Content?.GetBlock(item);
            if (blockDef == null && Game.Content?.GetItem(BlocksBeyondTheStars.Shared.State.ItemKey.Base(item))?.PlacesBlock is string pb && pb.Length > 0)
            {
                blockDef = Game.Content?.GetBlock(pb);
            }

            int shape = BlocksBeyondTheStars.Shared.State.ItemKey.Shape(item);
            Texture2D shapeTex = (blockDef != null && Game.Atlas != null && shape > 0)
                ? ShapeIconFactory.ForBlock(Game.Atlas, (ushort)blockDef.NumericId.Value, shape, Game.CustomShapes)
                : null;
            int design = BlocksBeyondTheStars.Shared.State.ItemKey.Design(item);
            if (design != 0 && Game.PaintAtlas != null && Game.PaintAtlas.TryGetUv(design, out var designUv))
            {
                raw.texture = Game.PaintAtlas.Texture;
                raw.uvRect = designUv;
            }
            else if (shapeTex != null)
            {
                raw.texture = shapeTex;
            }
            else if (blockDef != null && Game.Atlas != null)
            {
                BlockCubeIcon.Apply(raw, Game.Atlas, blockDef.NumericId.Value); // a block whose faces differ is a cube (#2744)
            }
            else
            {
                Texture2D itemTex = IconResolver.ItemTexture(item);
                var kind = Game.Content?.GetItem(BlocksBeyondTheStars.Shared.State.ItemKey.Base(item))?.Tool?.Kind ?? BlocksBeyondTheStars.Shared.Definitions.ToolKind.None;
                raw.texture = itemTex != null ? itemTex : IconFactory.ForItem(item, kind);
            }

            raw.color = IconResolver.Tint(item, Game);
            if (count > 1)
            {
                var cnt = UiKit.AddText(b.transform, 4f, 2f, GridCell - 8f, 18f, count.ToString(), 13, UiKit.TextCol, TextAnchor.UpperRight, FontStyle.Bold);
                UiKit.AddOutline(cnt);
            }

            return b;
        }

        private void ClearPick()
        {
            _pickKind = string.Empty;
            _pickIndex = -1;
        }

        /// <summary>A backpack / quick-bar slot was clicked.</summary>
        private void OnInventorySlotClicked(int index)
        {
            string item = Game.ItemInSlot(index);
            if (_pickKind == "inv")
            {
                int from = _pickIndex;
                ClearPick();
                if (from != index)
                {
                    Game.Network?.SendMoveItem(from, index); // move or swap; the server validates
                    ClientAudio.Instance?.Cue("ui_click");
                    return;
                }

                RebuildList(); // the same slot again: just put it down
                return;
            }

            if (string.IsNullOrEmpty(item))
            {
                return; // nothing to pick up
            }

            _pickKind = "inv";
            _pickIndex = index;
            _stackSlot = index; // #2729: the detail pane's "This stack" means this one
            _selected = "inv:" + item;
            ClientAudio.Instance?.Cue("ui_click");
            RebuildList();
            RebuildDetail();
        }

        /// <summary>#1930 ("please unlock everything in Sandbox — you shouldn't have to craft anything any more"): every item of
        /// the game, tools first and raw materials last, filtered by the search box. Picking one opens the take buttons.
        /// #2216: a lab item that is nothing without what it carries — a sample or a seedling without a species, a
        /// preparation without a compound — is left out: the plain key is a blank, and the server refuses to hand it out.</summary>
        private float BuildCatalogList()
        {
            float y = 0f;
            var hint = UiKit.AddText(_listContent, 8, y, 752, 52, L("ui.inventory.catalog_hint"), 17, UiKit.CyanDim, TextAnchor.UpperLeft);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            y += 60f;
            var entries = Game.Content.Items.Values
                .Where(d => !BlocksBeyondTheStars.Shared.Bio.BioItems.NeedsPayload(d.Key))
                .Select(d => (Def: d, Name: ItemName(d.Key)))
                .Where(e => MatchesSearch(e.Name))
                .OrderBy(e => CatalogRank(e.Def.Category))
                .ThenBy(e => e.Name, NameOrder); // #2566: the game language's A–Z, not the PC's culture
            foreach (var (def, name) in entries)
            {
                string key = def.Key;
                AddCard(y, name, IconFor(key), L("ui.inventory.catalog_take"), UiKit.CyanDim, "cat:" + key,
                    () => { _selected = "cat:" + key; RebuildDetail(); }, contentKey: key);
                y += 88f;
            }

            return y;
        }

        private static int CatalogRank(BlocksBeyondTheStars.Shared.Definitions.ItemCategory category) => category switch
        {
            BlocksBeyondTheStars.Shared.Definitions.ItemCategory.Tool => 0,
            BlocksBeyondTheStars.Shared.Definitions.ItemCategory.Consumable => 1,
            BlocksBeyondTheStars.Shared.Definitions.ItemCategory.Block => 2,
            BlocksBeyondTheStars.Shared.Definitions.ItemCategory.Component => 3,
            _ => 4,
        };

        /// <summary>The take buttons of a catalog item (#1930): one, or a full stack. The server hands it out.</summary>
        private float DetailCatalog()
        {
            string item = _selected.Substring(4);
            float y = 0f;
            UiKit.AddText(_detail, 8, y, 620, 40, ItemName(item), 30, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 48f;
            string desc = Desc($"item.{item}.desc");
            if (!string.IsNullOrEmpty(desc))
            {
                y = AddDetailDescription(desc, y);
            }

            UiKit.AddText(_detail, 8, y, 620, 28, $"{L("ui.craft.source")}: {Owned(item)}", 20, UiKit.Cyan, TextAnchor.UpperLeft);
            y += 40f;
            int stack = Mathf.Max(1, Game.Content.MaxStackOf(item));
            UiKit.AddButton(_detail, 8, y, 300, 50, L("ui.inventory.catalog_take_one"), () => Game.Network?.SendCreativeTakeItem(item, 1));
            if (stack > 1)
            {
                UiKit.AddButton(_detail, 320, y, 300, 50, L("ui.inventory.catalog_take_stack").Replace("{count}", stack.ToString()),
                    () => Game.Network?.SendCreativeTakeItem(item, stack));
            }

            return y + 58f;
        }

        /// <summary>The detail entry whose Discard button is currently armed, or "" when none is (#599). Cleared
        /// whenever the detail pane switches to another entry, so an armed button never survives navigation.</summary>
        private string _discardArmed = string.Empty;
        private GameObject _discardDialog; // #1484: the suit-gear confirm, one at a time

        /// <summary>The suit-gear confirm (#1484): a modal that names the piece and offers Keep / Throw away.
        /// Only this dialog sends the discard for suit gear; closing the menu drops it.</summary>
        private void ShowDiscardGearDialog(string item, int slot, bool inCargo)
        {
            CloseDiscardGearDialog();
            var canvas = UiKit.CreateCanvas("Discard Gear");
            canvas.sortingOrder = 61; // above the Tab menu
            _discardDialog = canvas.gameObject;
            UiNav.Enable(canvas.gameObject); // pad: stick walks Keep / Throw away, A picks

            var (_, panel) = UiKit.AddModalOverlay(canvas.transform, 660f, 380f, 600f, 300f);
            UiKit.AddText(panel.transform, 24f, 24f, 552f, 44f,
                L("ui.inventory.discard_gear_title"), 26, UiKit.TextCol, TextAnchor.MiddleCenter);
            var body = UiKit.AddText(panel.transform, 24f, 76f, 552f, 90f,
                L("ui.inventory.discard_gear_text").Replace("{item}", ItemName(item)), 18, UiKit.CyanDim, TextAnchor.UpperCenter);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddButton(panel.transform, 40f, 200f, 240f, 56f, L("ui.inventory.discard_gear_keep"), CloseDiscardGearDialog);
            var yes = UiKit.AddButton(panel.transform, 320f, 200f, 240f, 56f,
                L("ui.inventory.discard_gear_yes").Replace("{item}", ItemName(item)),
                () =>
                {
                    CloseDiscardGearDialog();
                    Game.Network?.SendDiscardItem(slot, inCargo);
                    _selected = string.Empty; // the entry is about to vanish from the list
                    RebuildList();
                    RebuildDetail();
                });
            var yesImg = yes.GetComponent<Image>();
            if (yesImg != null)
            {
                yesImg.color = new Color(0.62f, 0.20f, 0.20f);
            }
        }

        private void CloseDiscardGearDialog()
        {
            if (_discardDialog != null)
            {
                Destroy(_discardDialog);
                _discardDialog = null;
            }
        }

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
            // The description hangs on the BASE key — a preparation, a seedling or a changed tool carries a payload.
            string desc = Desc($"item.{BlocksBeyondTheStars.Shared.State.ItemKey.Base(item)}.desc");
            if (!string.IsNullOrEmpty(desc))
            {
                y = AddDetailDescription(desc, y);
            }

            y = AddBioItemInfo(item, y);
            y = AddDerelictLogText(item, y);

            UiKit.AddText(_detail, 8, y, 620, 28, $"{L("ui.craft.source")}: {Owned(item)}", 20, UiKit.Cyan, TextAnchor.UpperLeft);
            y += 40f;

            // #2202: a preparation is taken from here too (the right-click of the held one, as a button). The full key
            // goes out — the compound rides in it and the server finds the stack by it. Not from the hold, and not the
            // coating (that goes onto a tool in the lab).
            if (_category != "cargo" && BlocksBeyondTheStars.Shared.Bio.BioItems.CompoundOf(item) is { } preparation
                && preparation.Form != BlocksBeyondTheStars.Shared.Bio.BioForm.Coating && SlotOfItem(item, false) >= 0)
            {
                UiKit.AddButton(_detail, 8, y, 320, 46, L("ui.bio.use"), () =>
                {
                    Game.Network?.SendConsume(item);
                    ClientAudio.Instance?.Cue("eat");
                });
                y += 54f;
            }

            // #2110: wear / take off. A wearable piece in the backpack gets "Wear" (its own slot; a module the first free
            // module slot); a worn piece gets "Take off" (into the first free backpack slot).
            if (_category != "cargo" && Game.Content.GetItem(BlocksBeyondTheStars.Shared.State.ItemKey.Base(item)) is { EquipSlot: { Length: > 0 } })
            {
                int wornSlot = WornSlotOf(item);
                int packSlot = SlotOfItem(item, false);
                if (wornSlot >= 0)
                {
                    UiKit.AddButton(_detail, 8, y, 320, 46, L("ui.equip.unequip"),
                        () => { Game.Network?.SendUnequipItem(wornSlot, -1); ClearPick(); });
                    y += 54f;
                }
                else if (packSlot >= 0)
                {
                    UiKit.AddButton(_detail, 8, y, 320, 46, L("ui.equip.equip"),
                        () => { Game.Network?.SendEquipItem(packSlot, -1); ClientAudio.Instance?.Cue("gear_equip"); ClearPick(); });
                    y += 54f;
                }
            }

            // Cargo transfer: move this item between the personal inventory and the ship's hold (aboard only).
            // Direction follows the tab you're viewing it from — cargo view pulls it out, personal view stows it.
            // #2729 (Lyxette: "take 5 along, the other 15 into the hold"): "This stack" moves the one picked (the backpack
            // slot, the hold card), "All of this kind" every stack of it, "How many?" a part of it — buttons all, so pad
            // and touch get every choice the mouse gets with Shift.
            if (AboardShipNow())
            {
                bool fromCargo = _category == "cargo";
                var side = fromCargo ? Game.Cargo : Game.Personal;
                int stackSlot = CargoStackPicker.ResolveSlot(side, item, _stackSlot);
                int inStack = CargoStackPicker.CountInSlot(side, stackSlot);
                int total = CargoStackPicker.Total(side, item);
                if (stackSlot >= 0)
                {
                    UiKit.AddText(_detail, 8, y, 620, 26, L(fromCargo ? "ui.cargo.to_inventory" : "ui.cargo.to_cargo"), 18, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
                    y += 30f;
                    UiKit.AddButton(_detail, 8, y, 300, 46, L("ui.cargo.this_stack").Replace("{n}", inStack.ToString()),
                        () => MoveCargoStack(!fromCargo, stackSlot, item, ShiftHeld()));
                    if (total > inStack)
                    {
                        UiKit.AddButton(_detail, 320, y, 300, 46, L("ui.cargo.all_of_kind").Replace("{n}", total.ToString()),
                            () => MoveCargoStack(!fromCargo, stackSlot, item, true));
                    }

                    y += 54f;
                    var amounts = CargoStackPicker.Amounts(total);
                    if (amounts.Count > 0)
                    {
                        UiKit.AddText(_detail, 8, y, 140, 46, L("ui.cargo.how_many"), 18, UiKit.TextCol, TextAnchor.MiddleLeft);
                        for (int i = 0; i < amounts.Count; i++)
                        {
                            int n = amounts[i].Amount;
                            string label = amounts[i].Half ? L("ui.cargo.half").Replace("{n}", n.ToString()) : n.ToString();
                            UiKit.AddButton(_detail, 156 + i * 158, y, 150, 46, label, () => MoveCargoStack(!fromCargo, stackSlot, item, false, n));
                        }

                        y += 54f;
                    }
                }
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
                // #1484: the confirm NAMES the item — a second click on "Really throw away?" used to destroy
                // whatever had scrolled under the mouse meanwhile. Suit gear (the helmet lamp, a jetpack module)
                // gets a proper dialog on top: it is the one kind of item a player cannot craft back in a minute.
                bool suitGear = Game.Content.GetItem(item) is { } gearDef && BlocksBeyondTheStars.Shared.State.SuitEquipment.IsSuitGear(gearDef);
                string discardLabel = armed
                    ? L("ui.inventory.discard_confirm").Replace("{item}", ItemName(item))
                    : L("ui.inventory.discard");
                var discardBtn = UiKit.AddButton(_detail, 8, y, 320, 46, discardLabel,
                    () =>
                    {
                        if (!armed)
                        {
                            _discardArmed = _selected; // first click only asks
                            RebuildDetail();
                            return;
                        }

                        _discardArmed = string.Empty;
                        if (suitGear)
                        {
                            ShowDiscardGearDialog(item, discardSlot, inCargo);
                            return;
                        }

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
                    y = AddWrappedDetailLine(NeedStationText("workshop"), y, 24f, 16, new Color(1f, 0.8f, 0.4f)) + 4f;
                }
            }

            return y;
        }
    }
}
