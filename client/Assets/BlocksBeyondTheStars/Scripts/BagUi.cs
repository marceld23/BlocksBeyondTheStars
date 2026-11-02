// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.State;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The storage bag screen (#3099, Justus: "an item in the inventory that you can open inside the inventory and that carries
    /// at most 1000 items"). Opened from the inventory's detail pane ("Open the bag") or with the place key while the bag is in
    /// the hand: the bag's stacks at the top with "n/1000 items", the backpack (quick-bar included) at the bottom — the crate
    /// screen's layout and its slot widget (<see cref="CrateUi.AddItemSlot(GameBootstrap, Transform, float, float, string, int, System.Action)"/>).
    /// A click moves that stack across; Shift-click moves every stack of that kind. The server decides (the 1000, no bag in a
    /// bag) and answers every move with the contents again (<see cref="BagContents"/>); nothing is held open on the server.
    /// Modal like the crate screen: control freeze + cursor release via the arbiter (#413); Escape or pad B closes it.
    /// </summary>
    public sealed class BagUi : UiPanel
    {
        private const int Cols = 9;
        private const int MaxRows = 4;                // 36 kinds shown; more are named in a line below the grid
        private const float Pitch = 84f, Cell = 76f;  // CrateUi's cell + pitch
        private const float PanelW = 1000f, PanelH = 920f, X0 = 32f;

        private int _bagSlot = -1;
        private string _bagKey = string.Empty;
        private readonly List<NetItemStack> _contents = new List<NetItemStack>();
        private int _total;
        private int _capacity = CarriedContents.BagCapacity;
        private bool _contentsKnown;
        private bool _subscribed;

        protected override string CanvasName => "BagUi";

        protected override int CanvasSortingOrder => 58; // the crate screen's shelf: above the HUD/chat, below the world map

        protected override void OnDestroy()
        {
            Close();
            base.OnDestroy();
        }

        /// <summary>True while the screen is open (gameplay hotkeys stand down).</summary>
        public override bool IsOpen => IsBuilt;

        /// <summary>Opens the bag in backpack slot <paramref name="slot"/>: asks the server for its contents and shows the
        /// backpack at once. Does nothing when the slot holds no bag.</summary>
        public void Open(int slot)
        {
            if (IsOpen || Game == null || !CarriedContents.IsBag(Game.ItemInSlot(slot)))
            {
                return;
            }

            _bagSlot = slot;
            _bagKey = Game.ItemInSlot(slot);
            _contents.Clear();
            _contentsKnown = false;
            _total = 0;
            _capacity = CarriedContents.BagCapacity;

            Subscribe();
            Game.Network?.SendOpenBag(slot);

            CreateCanvasNow();
            SetMenuOwned(true); // freezes player control + frees the cursor via the arbiter (#413)
            Build();
            PlayOpenEffect();
        }

        private void Update()
        {
            if (IsOpen && CancelPressed(skipOpenFrame: false))
            {
                ConsumeMenuInput(); // this Esc is consumed — don't also pop the quit prompt (#413 N1)
                Close();
            }
        }

        private void Subscribe()
        {
            if (_subscribed || Game?.Network == null)
            {
                return;
            }

            Game.Network.BagContentsReceived += OnContents;
            Game.Network.InventoryUpdated += OnInventory;
            Game.Network.WorldResetReceived += OnWorldReset;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || Game?.Network == null)
            {
                _subscribed = false;
                return;
            }

            Game.Network.BagContentsReceived -= OnContents;
            Game.Network.InventoryUpdated -= OnInventory;
            Game.Network.WorldResetReceived -= OnWorldReset;
            _subscribed = false;
        }

        private void OnContents(BagContents m)
        {
            if (!IsOpen || m == null || m.Slot != _bagSlot)
            {
                return;
            }

            if (string.IsNullOrEmpty(m.BagKey))
            {
                Close(); // the slot holds no bag any more (it was dropped, traded or lost meanwhile)
                return;
            }

            _bagKey = m.BagKey;
            _contents.Clear();
            if (m.Items != null)
            {
                _contents.AddRange(m.Items);
            }

            _total = m.Total;
            _capacity = m.Capacity > 0 ? m.Capacity : CarriedContents.BagCapacity;
            _contentsKnown = true;
            Rebuild();
        }

        private void OnInventory(InventoryUpdate m)
        {
            if (!IsOpen)
            {
                return;
            }

            if (!CarriedContents.IsBag(Game.ItemInSlot(_bagSlot)))
            {
                Close(); // the bag left its slot (a death, a trade) — the screen goes with it
                return;
            }

            Rebuild(); // the backpack half follows every inventory change the server confirms
        }

        private void OnWorldReset(WorldReset m)
        {
            if (IsOpen)
            {
                Close(); // a trip to another world: the bag travels along, the screen does not
            }
        }

        private void Close()
        {
            if (IsBuilt)
            {
                DestroyCanvas();
                SetMenuOwned(false);
            }

            Unsubscribe();
            _bagSlot = -1;
            _bagKey = string.Empty;
            _contents.Clear();
            _contentsKnown = false;
        }

        private void Build()
        {
            var panel = AddModal((1920f - PanelW) / 2f, 80f, PanelW, PanelH);
            float y = 28f;

            string title = BlocksBeyondTheStars.Shared.Localization.ItemNames.Display(Game.Localizer, _bagKey.Length > 0 ? _bagKey : CarriedContents.Bag);
            var head = UiKit.AddText(panel, X0, y, 640f, 40f, title, 26, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddOutline(head);
            string cap = string.Format(L("ui.bag.capacity_fmt"), _total, _capacity);
            UiKit.AddText(panel, PanelW - X0 - 300f, y, 300f, 40f, cap, 18, _total >= _capacity ? UiKit.Cyan : UiKit.CyanDim, TextAnchor.MiddleRight);

            y += 48f;
            UiKit.AddText(panel, X0, y, PanelW - 2f * X0, 40f, L("ui.bag.hint"), 15, UiKit.CyanDim, TextAnchor.UpperLeft)
                .horizontalOverflow = HorizontalWrapMode.Wrap;
            y += 46f;

            y = BuildContents(panel, y);

            // --- The bulk moves, between the halves ---
            float bw = (PanelW - 2f * X0 - 12f) / 2f;
            UiKit.AddButton(panel, X0, y, bw, 44f, L("ui.bag.store_all"), () => MoveAll(toBag: true));
            UiKit.AddButton(panel, X0 + bw + 12f, y, bw, 44f, L("ui.bag.take_all"), () => MoveAll(toBag: false));
            y += 56f;

            y = BuildBackpack(panel, y);
            UiKit.AddButton(panel, PanelW - X0 - 180f, Mathf.Max(y, PanelH - 70f), 180f, 48f, L("ui.crate.close"), () =>
            {
                Game?.MarkMenuInputHandled();
                Close();
            });
        }

        /// <summary>The bag's half: its stacks in the nine-wide grid; returns the y below it.</summary>
        private float BuildContents(Transform panel, float y)
        {
            UiKit.AddText(panel, X0, y, 500f, 26f, L("ui.bag.contents"), 18, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 30f;
            int shown = Mathf.Min(_contents.Count, Cols * MaxRows);
            int rows = Mathf.Max(1, (shown + Cols - 1) / Cols);
            for (int k = 0; k < shown; k++)
            {
                var stack = _contents[k];
                string item = stack.Item;
                CrateUi.AddItemSlot(Game, panel, X0 + (k % Cols) * Pitch, y + (k / Cols) * Pitch, item, stack.Count,
                    () => Move(item, toBag: false));
            }

            if (_contents.Count == 0)
            {
                UiKit.AddText(panel, X0, y + 24f, PanelW - 2f * X0, 30f, _contentsKnown ? L("ui.bag.empty") : "…", 18, UiKit.CyanDim, TextAnchor.MiddleCenter);
            }

            y += rows * Pitch + 8f;
            if (_contents.Count > shown)
            {
                // More kinds than the grid shows: they are still in the bag — "Take everything out" reaches them.
                UiKit.AddText(panel, X0, y - 6f, PanelW - 2f * X0, 22f, "+" + (_contents.Count - shown), 15, UiKit.CyanDim, TextAnchor.UpperRight);
                y += 16f;
            }

            return y;
        }

        /// <summary>The backpack half: the nine-wide slot grid, quick-bar row last like the inventory tab (#2110). The bag's
        /// own slot shows the bag and does nothing.</summary>
        private float BuildBackpack(Transform panel, float y)
        {
            UiKit.AddText(panel, X0, y, 500f, 26f, L("ui.inventory.backpack"), 18, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 30f;
            int total = Game.PersonalSlots;
            int quick = Mathf.Min(Cols, total);
            int packSlots = Mathf.Max(0, total - quick);
            for (int i = 0; i < packSlots; i++)
            {
                AddBackpackSlot(panel, quick + i, X0 + (i % Cols) * Pitch, y + (i / Cols) * Pitch);
            }

            y += ((packSlots + Cols - 1) / Cols) * Pitch + 6f;
            var line = new GameObject("Separator", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(panel, false);
            UiKit.Place(line, X0, y, PanelW - 2f * X0, 2f);
            line.GetComponent<Image>().color = UiKit.CyanDim;
            line.GetComponent<Image>().raycastTarget = false;
            y += 10f;
            for (int k = 0; k < quick; k++)
            {
                var b = AddBackpackSlot(panel, k, X0 + k * Pitch, y);
                var num = UiKit.AddText(b.transform, 5f, 3f, 24f, 18f, (k + 1).ToString(), 12, UiKit.CyanDim, TextAnchor.UpperLeft, FontStyle.Bold);
                UiKit.AddOutline(num);
            }

            return y + Cell + 14f;
        }

        private Button AddBackpackSlot(Transform panel, int index, float x, float y)
        {
            string item = Game.ItemInSlot(index);
            var b = CrateUi.AddItemSlot(Game, panel, x, y, item, Game.CountInSlot(index),
                index == _bagSlot ? (System.Action)(() => { }) : () => Move(item, toBag: true, fromSlot: index));
            var img = b.GetComponent<Image>();
            if (index == _bagSlot && img != null)
            {
                img.color = UiKit.Cyan; // the open bag itself
            }

            return b;
        }

        /// <param name="fromSlot">The backpack slot that was clicked — exactly that stack goes in. -1 for the bag's own stacks.</param>
        private void Move(string item, bool toBag, int fromSlot = -1)
        {
            if (string.IsNullOrEmpty(item) || _bagSlot < 0)
            {
                return;
            }

            bool all = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            Game.Network?.SendMoveBagItem(_bagSlot, item, toBag, all, fromSlot);
            ClientAudio.Instance?.Cue("loot", 0.35f);
        }

        /// <summary>"Store all materials" / "Take everything out": every kind at once (an empty item with All).</summary>
        private void MoveAll(bool toBag)
        {
            if (_bagSlot < 0)
            {
                return;
            }

            Game.Network?.SendMoveBagItem(_bagSlot, string.Empty, toBag, all: true);
            ClientAudio.Instance?.Cue("loot", 0.35f);
        }

        private void Rebuild()
        {
            if (!IsBuilt)
            {
                return;
            }

            foreach (Transform child in Canvas.transform)
            {
                Destroy(child.gameObject);
            }

            Build();
        }
    }
}
