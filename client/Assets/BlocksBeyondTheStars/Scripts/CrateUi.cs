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
    /// The crate screen (#2436, Justus: "I miss seeing my inventory at the bottom, the crate inventory at the top, and
    /// moving everything like in the inventory"). Opens on E at a storage crate or wood box: the crate's stacks in a grid
    /// at the top, the backpack (quick-bar included) in the nine-wide slot grid at the bottom. A click moves that stack
    /// across; Shift-click moves every stack of that kind. Any item category goes in by hand — the server's rule (the
    /// crate's filter, a wood box's eight kinds, the reach) decides, and the screen just shows what it answers.
    /// <para>
    /// The server sends a crate's contents only while someone has it open (<see cref="OpenContainerIntent"/> →
    /// <see cref="ContainerContents"/>, re-sent after every change), so the count-only container broadcast stays small.
    /// The bulk buttons are the H and G sweeps; the filter button hands over to <see cref="ContainerFilterUi"/> (#1032).
    /// Modal like the filter panel: control freeze + cursor release via the arbiter (#413); pad: the stick walks the
    /// grids, A moves, B closes.
    /// </para>
    /// </summary>
    public sealed class CrateUi : MonoBehaviour
    {
        public static CrateUi Instance { get; private set; }
        public GameBootstrap Game;

        private const int CrateCols = 9;
        private const int MaxCrateRows = 4;           // 36 kinds shown — a wood box holds 8, a workshop crate rarely more
        private const float Cell = 76f, Pitch = 84f;  // the inventory grid's cell + pitch (#2110), so both halves read alike
        private const float PanelW = 1000f, PanelH = 920f, X0 = 32f;

        private Canvas _canvas;
        private string _containerId = string.Empty;
        private string[] _filter = System.Array.Empty<string>();
        private readonly List<NetItemStack> _contents = new List<NetItemStack>();
        private int _stackLimit;
        private bool _contentsKnown;
        private bool _subscribed;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            Close(notifyServer: false);
        }

        /// <summary>True while the screen is open (gameplay hotkeys stand down).</summary>
        public bool IsOpen => _canvas != null;

        /// <summary>Opens the screen for a crate: asks the server for its contents and shows the backpack at once.</summary>
        public void Open(string containerId, string[] currentFilter)
        {
            if (IsOpen || Game == null || string.IsNullOrEmpty(containerId))
            {
                return;
            }

            _containerId = containerId;
            _filter = currentFilter ?? System.Array.Empty<string>();
            _contents.Clear();
            _contentsKnown = false;
            _stackLimit = 0;

            Subscribe();
            Game.Network?.SendOpenContainer(containerId, open: true);

            _canvas = UiKit.CreateCanvas("CrateUi");
            _canvas.sortingOrder = 58; // above the HUD/chat, below the world map (60) — the filter panel's shelf
            UiNav.Enable(_canvas.gameObject); // pad: stick walks the grids, A picks (#940)
            Game.SetMenuOwner(this, true); // freezes player control + frees the cursor via the arbiter (#413)
            UiKit.OpenModal(Build()); // #2302: the open effect — only here; a refresh comes back without it
        }

        private void Update()
        {
            // #2303: the menu verb — Escape or pad B, like the filter panel and the bio lab.
            if (IsOpen && InputMap.Down(InputAction.UiCancel))
            {
                Game?.MarkMenuInputHandled(); // this Esc is consumed — don't also pop the quit prompt (#413 N1)
                Close();
            }
        }

        private void Subscribe()
        {
            if (_subscribed || Game?.Network == null)
            {
                return;
            }

            Game.Network.ContainerContentsReceived += OnContents;
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

            Game.Network.ContainerContentsReceived -= OnContents;
            Game.Network.InventoryUpdated -= OnInventory;
            Game.Network.WorldResetReceived -= OnWorldReset;
            _subscribed = false;
        }

        private void OnContents(ContainerContents m)
        {
            if (!IsOpen || m == null || m.ContainerId != _containerId)
            {
                return;
            }

            // An empty answer for a crate the server no longer has (it was mined under us) closes the screen.
            if ((m.Items == null || m.Items.Length == 0) && _contentsKnown && m.StackLimit == 0 && (m.Filter == null || m.Filter.Length == 0)
                && !ContainerStillListed())
            {
                Close(notifyServer: false);
                return;
            }

            _contents.Clear();
            if (m.Items != null)
            {
                _contents.AddRange(m.Items);
            }

            _stackLimit = m.StackLimit;
            _filter = m.Filter ?? System.Array.Empty<string>();
            _contentsKnown = true;
            Rebuild();
        }

        private bool ContainerStillListed()
        {
            if (Game?.Containers == null)
            {
                return false;
            }

            foreach (var c in Game.Containers)
            {
                if (c.Id == _containerId)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnInventory(InventoryUpdate m)
        {
            if (IsOpen)
            {
                Rebuild(); // the backpack half follows every inventory change the server confirms
            }
        }

        private void OnWorldReset(WorldReset m)
        {
            if (IsOpen)
            {
                Close(notifyServer: false); // the crate stayed on the world we just left
            }
        }

        private void Close(bool notifyServer = true)
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
                _canvas = null;
                Game?.SetMenuOwner(this, false);
            }

            if (notifyServer && _containerId.Length > 0)
            {
                Game?.Network?.SendOpenContainer(_containerId, open: false);
            }

            Unsubscribe();
            _containerId = string.Empty;
            _contents.Clear();
            _contentsKnown = false;
        }

        /// <returns>The overlay, for the open effect.</returns>
        private GameObject Build()
        {
            var (overlay, panel) = UiKit.AddModalOverlay(_canvas.transform, (1920f - PanelW) / 2f, 80f, PanelW, PanelH);
            float y = 28f;

            string title = CrateTitle();
            var head = UiKit.AddText(panel, X0, y, 700f, 40f, title, 26, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddOutline(head);
            if (_stackLimit > 0)
            {
                string cap = string.Format(L("ui.crate.capacity_fmt"), _contents.Count, _stackLimit);
                UiKit.AddText(panel, PanelW - X0 - 300f, y, 300f, 40f, cap, 18, UiKit.CyanDim, TextAnchor.MiddleRight);
            }

            y += 48f;
            UiKit.AddText(panel, X0, y, PanelW - 2f * X0, 40f, L("ui.crate.hint"), 15, UiKit.CyanDim, TextAnchor.UpperLeft)
                .horizontalOverflow = HorizontalWrapMode.Wrap;
            y += 46f;

            // --- The crate's half ---
            UiKit.AddText(panel, X0, y, 500f, 26f, L("ui.crate.contents"), 18, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 30f;
            int shown = Mathf.Min(_contents.Count, CrateCols * MaxCrateRows);
            int crateRows = Mathf.Max(1, (shown + CrateCols - 1) / CrateCols);
            for (int k = 0; k < shown; k++)
            {
                var stack = _contents[k];
                float x = X0 + (k % CrateCols) * Pitch;
                float yy = y + (k / CrateCols) * Pitch;
                string item = stack.Item;
                AddItemSlot(panel, x, yy, item, stack.Count, () => Move(item, toContainer: false));
            }

            if (_contents.Count == 0)
            {
                UiKit.AddText(panel, X0, y + 24f, PanelW - 2f * X0, 30f, _contentsKnown ? L("ui.crate.empty") : "…", 18, UiKit.CyanDim, TextAnchor.MiddleCenter);
            }

            y += crateRows * Pitch + 8f;

            // --- The bulk moves + the filter, between the halves ---
            float bw = (PanelW - 2f * X0 - 2f * 12f) / 3f;
            UiKit.AddButton(panel, X0, y, bw, 44f, L("ui.crate.stash_all"), () => Game.Network?.SendDepositContainer(_containerId));
            UiKit.AddButton(panel, X0 + bw + 12f, y, bw, 44f, L("ui.crate.take_all"), () => Game.Network?.SendLootContainer(_containerId));
            UiKit.AddButton(panel, X0 + 2f * (bw + 12f), y, bw, 44f, L("ui.crate.filter"), () =>
            {
                string id = _containerId;
                string[] filter = _filter;
                Close();
                ContainerFilterUi.Instance?.Open(id, filter);
            });
            y += 56f;

            // --- The backpack half: the nine-wide slot grid, quick-bar row last like the inventory tab (#2110) ---
            UiKit.AddText(panel, X0, y, 500f, 26f, L("ui.inventory.backpack"), 18, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 30f;
            int total = Game.PersonalSlots;
            int quick = Mathf.Min(9, total);
            int packSlots = Mathf.Max(0, total - quick);
            int packRows = (packSlots + CrateCols - 1) / CrateCols;
            for (int i = 0; i < packSlots; i++)
            {
                int index = quick + i;
                float x = X0 + (i % CrateCols) * Pitch;
                float yy = y + (i / CrateCols) * Pitch;
                string item = Game.ItemInSlot(index);
                AddItemSlot(panel, x, yy, item, Game.CountInSlot(index), () => Move(item, toContainer: true));
            }

            y += packRows * Pitch + 6f;
            var line = new GameObject("Separator", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(panel, false);
            UiKit.Place(line, X0, y, PanelW - 2f * X0, 2f);
            line.GetComponent<Image>().color = UiKit.CyanDim;
            line.GetComponent<Image>().raycastTarget = false;
            y += 10f;
            for (int k = 0; k < quick; k++)
            {
                int index = k;
                float x = X0 + k * Pitch;
                string item = Game.ItemInSlot(index);
                var b = AddItemSlot(panel, x, y, item, Game.CountInSlot(index), () => Move(item, toContainer: true));
                var num = UiKit.AddText(b.transform, 5f, 3f, 24f, 18f, (k + 1).ToString(), 12, UiKit.CyanDim, TextAnchor.UpperLeft, FontStyle.Bold);
                UiKit.AddOutline(num);
            }

            y += Cell + 14f;
            UiKit.AddButton(panel, PanelW - X0 - 180f, Mathf.Max(y, PanelH - 70f), 180f, 48f, L("ui.crate.close"), () =>
            {
                Game?.MarkMenuInputHandled();
                Close();
            });
            return overlay;
        }

        private void Move(string item, bool toContainer)
        {
            if (string.IsNullOrEmpty(item) || _containerId.Length == 0)
            {
                return;
            }

            bool all = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            Game.Network?.SendMoveContainerItem(_containerId, item, toContainer, all);
            ClientAudio.Instance?.Cue("loot", 0.35f);
        }

        private void Rebuild()
        {
            if (_canvas == null)
            {
                return;
            }

            foreach (Transform child in _canvas.transform)
            {
                Destroy(child.gameObject);
            }

            Build();
        }

        private string CrateTitle()
        {
            string key = "crate";
            if (Game?.Containers != null && Game.World != null && Game.Content != null)
            {
                foreach (var c in Game.Containers)
                {
                    if (c.Id == _containerId)
                    {
                        key = Game.Content.BlockById(Game.World.GetBlock((int)c.X, (int)c.Y, (int)c.Z))?.Key ?? "crate";
                        break;
                    }
                }
            }

            return BlocksBeyondTheStars.Shared.Localization.ItemNames.Display(Game.Localizer, key, null, seed => Game.Bio.SpeciesName(seed));
        }

        /// <summary>One slot button with the item's icon and count — the hotbar's icon resolution (shape silhouette →
        /// painted design → atlas tile → generated icon); an empty slot is a plain frame that does nothing.</summary>
        private Button AddItemSlot(Transform parent, float x, float y, string item, int count, System.Action onClick)
        {
            bool empty = string.IsNullOrEmpty(item);
            var b = UiKit.AddButton(parent, x, y, Cell, Cell, string.Empty, empty ? () => { } : onClick);
            if (empty)
            {
                return b;
            }

            var go = new GameObject("ItemIcon", typeof(RectTransform));
            go.transform.SetParent(b.transform, false);
            UiKit.Place(go, 8f, 8f, Cell - 16f, Cell - 16f);
            var raw = go.AddComponent<RawImage>();
            raw.raycastTarget = false;

            var blockDef = Game.Content?.GetBlock(item);
            if (blockDef == null && Game.Content?.GetItem(ItemKey.Base(item))?.PlacesBlock is string pb && pb.Length > 0)
            {
                blockDef = Game.Content?.GetBlock(pb);
            }

            int shape = ItemKey.Shape(item);
            Texture2D shapeTex = (blockDef != null && Game.Atlas != null && shape > 0)
                ? ShapeIconFactory.ForBlock(Game.Atlas, (ushort)blockDef.NumericId.Value, shape, Game.CustomShapes)
                : null;
            int design = ItemKey.Design(item);
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
                raw.texture = Game.Atlas.Texture;
                raw.uvRect = Game.Atlas.TileUv(blockDef.NumericId.Value);
            }
            else
            {
                Texture2D itemTex = IconResolver.ItemTexture(item);
                var kind = Game.Content?.GetItem(ItemKey.Base(item))?.Tool?.Kind ?? BlocksBeyondTheStars.Shared.Definitions.ToolKind.None;
                raw.texture = itemTex != null ? itemTex : IconFactory.ForItem(item, kind);
            }

            raw.color = IconResolver.Tint(item, Game);
            if (count > 1)
            {
                var cnt = UiKit.AddText(b.transform, 4f, 2f, Cell - 8f, 18f, count.ToString(), 13, UiKit.TextCol, TextAnchor.UpperRight, FontStyle.Bold);
                UiKit.AddOutline(cnt);
            }

            return b;
        }

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;
    }
}
