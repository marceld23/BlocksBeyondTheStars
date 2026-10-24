// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A dedicated vendor trade (barter) screen, opened with E at a settlement/station vendor (B22) instead of
    /// the full crafting menu. It lists the market exchanges (the <c>station:"market"</c> recipes — "give X →
    /// get Y") as trade cards; ones the player can't afford are greyed out, and clicking a tradeable one sends
    /// the existing <see cref="NetworkClient.SendCraft"/> intent (the server validates the vendor proximity +
    /// materials). The economy is pure barter — there's no currency. Refreshes on every inventory update.
    /// <para>#2626 / #2629: the screen is bound to the vendor the player chose (its NPC id): the title names that vendor,
    /// a header line says what it deals in (<c>ui.vendor.theme.&lt;theme&gt;</c>), its own goods come first and the
    /// themeless deals after them (<c>MarketStock.ListFor</c> — the server's own stock rule). While the vendor stands within
    /// the server's vendor reach (<see cref="VendorReach.StillServes"/>) the list stays, however it steps about; beyond it
    /// the list gives way to "Step closer to … to trade" and comes back when it returns. Before, every rebuild re-picked
    /// "the nearest vendor within 3.6", so a doctor who strolled a step away left a screen titled with his name listing
    /// only the iron ↔ titanium swaps.</para>
    /// </summary>
    public sealed class VendorTradeUI : MonoBehaviour
    {
        public GameBootstrap Game;
        public GameMenu Menu;

        private const float W = 1920f, H = 1080f;
        private const float PanelW = 1000f, PanelH = 660f;
        private const float RowH = 84f, RowsTop = 156f;

        /// <summary>Seconds between two looks at whether the bound vendor still serves the player.</summary>
        private const float ReachCheckInterval = 0.25f;

        private Canvas _canvas;
        private Transform _root;
        private Transform _rows;   // recipe cards live here; cleared + rebuilt
        private Text _title;
        private Text _subtitle;    // #2629: what the bound vendor deals in (the generic barter line at the ship's console)
        private bool _built, _open, _subscribed;
        private int _openedFrame;

        private int _vendorId;                      // #2626: the vendor NPC this screen trades with (0 = the ship's console)
        private string _vendorTitle = string.Empty; // "Name · Role" as last seen (kept while the vendor is out of sight)
        private string _vendorName = string.Empty;  // the name the "step closer" line uses
        private bool _shownInReach;                 // the reach state the rows were last built for
        private long _shownDay;                     // the market day the rows were last built for (rotation, mixed stock)
        private float _nextReachCheck;

        public bool IsOpen => _open;

        /// <summary>Opens the screen bound to vendor NPC <paramref name="vendorId"/> (0 = the ship's trade console). Opened
        /// without one off the ship, it binds the vendor in reach, if any.</summary>
        public void Open(int vendorId = 0)
        {
            Build();
            Menu?.CloseFromUi();           // mutually exclusive with the crafting/Tab menu
            _vendorId = vendorId;
            if (_vendorId == 0 && Game != null && !Game.Aboard)
            {
                var nearest = Game.NearestTradingVendor();
                _vendorId = nearest != null ? nearest.Id : 0;
            }

            _vendorTitle = string.Empty;
            _vendorName = string.Empty;
            _open = true;
            _openedFrame = Time.frameCount; // so the same E press that opened it doesn't close it this frame
            _canvas.enabled = true;
            Game?.SetMenuOwner(this, true); // the cursor arbiter frees/locks from the owner set (#413)
            Rebuild();
        }

        public void Close()
        {
            if (!_open)
            {
                return;
            }

            _open = false;
            if (_canvas != null)
            {
                _canvas.enabled = false;
            }

            Game?.SetMenuOwner(this, false); // arbiter re-locks only once NO other panel is open (#413)
        }

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.InventoryUpdated += OnInventory;
                Game.Network.WorldResetReceived += OnWorldReset;
                _subscribed = true;
            }

            // Close on Esc/Tab (not E — the same E press that opens it via the controller could otherwise toggle
            // it shut; the frame guard + the close button cover the rest).
            if (_open && Time.frameCount > _openedFrame
                && (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab)))
            {
                Game?.MarkMenuInputHandled(); // this Esc/Tab is consumed — no quit prompt / menu toggle on top (#413 N1)
                Close();
                return;
            }

            // #2626: the bound vendor stepped out of (or back into) the server's reach, or a new market day began — redo the
            // list. Looked at a few times a second, rebuilt only on a change.
            if (_open && Game != null && Time.unscaledTime >= _nextReachCheck)
            {
                _nextReachCheck = Time.unscaledTime + ReachCheckInterval;
                if (BoundVendorServes(out _) != _shownInReach || Game.MarketDay != _shownDay)
                {
                    Rebuild();
                }
            }
        }

        private void OnInventory(InventoryUpdate m)
        {
            if (_open)
            {
                Rebuild(); // a completed trade changed the inventory → refresh affordability + counts
            }
        }

        private void OnWorldReset(WorldReset m) => Close(); // travelling away from the vendor closes the screen

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;

        /// <summary>The vendor this screen is bound to, if it is still around (null at the ship's console or once it left).</summary>
        private NetNpc BoundVendor() => _vendorId != 0 ? VendorReach.ById(Game.Npcs, _vendorId) : null;

        /// <summary>Whether the screen may list trades: at the ship's console always, else while the bound vendor stands
        /// within the server's vendor reach.</summary>
        private bool BoundVendorServes(out NetNpc vendor)
        {
            vendor = BoundVendor();
            if (_vendorId == 0)
            {
                return true;
            }

            var p = Game.PlayerPosition;
            return VendorReach.StillServes(vendor, p.x, p.y, p.z, Game.Circumference);
        }

        /// <summary>Display name for a (possibly composite) item key: base name via the base key, plus
        /// dyed/glowing and shape suffixes — so a market recipe over a modified item never renders a
        /// bracketed key like <c>[item.stone#t3f6fb0.name]</c>. Shape names follow the
        /// <c>ui.shape.&lt;BlockShape&gt;</c> key family (indices match <see cref="BlockShape"/>).</summary>
        private string ItemName(string item)
        {
            var (baseKey, tint, glow) = ItemKey.Parse(item);
            string name = L($"item.{baseKey}.name");
            if (glow != 0)
            {
                name += " · " + L("ui.color.glowing");
            }
            else if (tint != 0)
            {
                name += " · " + L("ui.color.dyed");
            }

            int shape = ItemKey.Shape(item);
            if (shape != 0)
            {
                name += " · " + L("ui.shape." + ((BlockShape)shape).ToString().ToLowerInvariant());
            }

            return name;
        }

        private void Build()
        {
            if (_built)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas("VendorTradeUI");
            _canvas.sortingOrder = 60; // above the crafting menu (50)
            _root = _canvas.transform;
            UiNav.Enable(_canvas.gameObject); // gamepad can drive the trade screen (inert on KB/mouse)

            UiKit.AddImage(_root, 0, 0, W, H, UiKit.SolidSprite, new Color(0.02f, 0.04f, 0.08f, 0.72f)); // dim backdrop

            float px = (W - PanelW) * 0.5f, py = (H - PanelH) * 0.5f;
            UiKit.AddPanel(_root, px, py, PanelW, PanelH, UiKit.Panel);

            _title = UiKit.AddText(_root, px + 40, py + 26, PanelW - 80, 40, string.Empty, 32, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _subtitle = UiKit.AddText(_root, px + 40, py + 78, PanelW - 80, 26, L("ui.vendor.subtitle"), 18, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.AddText(_root, px + 40, py + 116, 380, 22, L("ui.vendor.give"), 15, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(_root, px + 470, py + 116, 320, 22, L("ui.vendor.get"), 15, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);

            // The rows live in a masked scroll viewport — a settlement can post more trades than the
            // fixed panel height fits (they used to clip past the panel bottom).
            var rowsGo = new GameObject("Rows", typeof(RectTransform));
            rowsGo.transform.SetParent(_root, false);
            UiKit.Place(rowsGo, px, py + RowsTop, PanelW, PanelH - RowsTop - 80);
            var scroll = rowsGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            rowsGo.AddComponent<RectMask2D>();

            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(rowsGo.transform, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.sizeDelta = new Vector2(PanelW, PanelH - RowsTop - 80);
            content.anchoredPosition = Vector2.zero;
            scroll.content = content;
            scroll.viewport = (RectTransform)rowsGo.transform;
            UiKit.AddInlineScrollbar(scroll);
            _rows = content;

            UiKit.AddText(_root, px + 40, py + PanelH - 50, PanelW - 320, 26, L("ui.vendor.hint"), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.AddButton(_root, px + PanelW - 220, py + PanelH - 64, 180, 46, L("ui.action.close"), Close);

            _built = true;
        }

        private void Rebuild()
        {
            if (!_built || Game == null)
            {
                return;
            }

            for (int i = _rows.childCount - 1; i >= 0; i--)
            {
                Destroy(_rows.GetChild(i).gameObject);
            }

            // #2626: everything comes from the bound vendor — the name, the header line and the goods.
            bool inReach = BoundVendorServes(out var vendor);
            if (vendor != null)
            {
                string role = !string.IsNullOrEmpty(vendor.NameKey) ? L(vendor.NameKey) : string.Empty;
                _vendorTitle = VendorReach.Label(vendor.Name, role);
                _vendorName = !string.IsNullOrEmpty(vendor.Name) ? vendor.Name : role;
            }

            _title.text = _vendorTitle.Length > 0 ? _vendorTitle : L("ui.vendor.title");
            string headerKey = vendor != null ? VendorReach.ThemeHeaderKey(vendor.Theme) : string.Empty;
            _subtitle.text = headerKey.Length > 0 && Game.Localizer != null && Game.Localizer.Has(headerKey)
                ? L(headerKey)
                : L("ui.vendor.subtitle");

            long day = Game.MarketDay;
            _shownInReach = inReach;
            _shownDay = day;

            int row = 0;
            if (!inReach)
            {
                // #2629: the chosen vendor walked off — say so instead of silently shrinking the list to the common deals.
                string who = _vendorName.Length > 0 ? _vendorName : L("ui.vendor.title");
                UiKit.AddText(_rows, 20, 10, PanelW - 40, 30, L("ui.vendor.step_closer").Replace("{0}", who), 18, UiKit.CyanDim, TextAnchor.MiddleLeft);
            }
            else
            {
                // The vendor's own goods first, then the themeless deals every vendor has (the ship's console: only those).
                // MarketStock is the server's own stock rule — a rotating offer shows on its days, the travelling
                // merchant's mixed stock is the same daily pick the server accepts (#2630).
                string theme = vendor != null ? vendor.Theme : string.Empty;
                foreach (var r in BlocksBeyondTheStars.Shared.Definitions.MarketStock.ListFor(Game.MarketRecipes(), theme, day))
                {
                    AddRow(row++, r);
                }

                if (row == 0)
                {
                    UiKit.AddText(_rows, 20, 10, PanelW - 40, 30, L("ui.vendor.empty"), 18, UiKit.CyanDim, TextAnchor.MiddleLeft);
                }
            }

            // Size the scroll content to the rows (floored at the viewport so short lists don't scroll) and
            // clamp the kept scroll position — an inventory-update rebuild mid-browse must not jump to the top.
            var rows = (RectTransform)_rows;
            float viewportH = ((RectTransform)rows.parent).rect.height;
            float contentH = Mathf.Max(row * RowH + 8f, viewportH);
            rows.sizeDelta = new Vector2(rows.sizeDelta.x, contentH);
            rows.anchoredPosition = new Vector2(rows.anchoredPosition.x,
                Mathf.Clamp(rows.anchoredPosition.y, 0f, contentH - viewportH));
        }

        private void AddRow(int index, BlocksBeyondTheStars.Shared.Definitions.RecipeDefinition r)
        {
            float y = index * RowH;
            bool afford = r.Inputs.All(inp => Pooled(inp) >= inp.Count);

            UiKit.AddPanel(_rows, 20, y, PanelW - 40, RowH - 12, new Color(0.06f, 0.14f, 0.26f, 0.85f));

            string give = string.Join("   ", r.Inputs.Select(a => $"{a.Count}× {IngredientName(a)}"
                + (Pooled(a) < a.Count ? $"  ({Pooled(a)})" : string.Empty)));
            string get = string.Join("   ", r.Outputs.Select(a => $"{a.Count}× {ItemName(a.Item)}"));

            var giveCol = afford ? UiKit.TextCol : new Color(1f, 0.55f, 0.55f);
            UiKit.AddText(_rows, 40, y + 6, 400, RowH - 24, give, 18, giveCol, TextAnchor.MiddleLeft);
            UiKit.AddText(_rows, 440, y + 6, 36, RowH - 24, "→", 22, UiKit.CyanDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddText(_rows, 490, y + 6, 360, RowH - 24, get, 18, UiKit.Ok, TextAnchor.MiddleLeft);

            string key = r.Key;
            var btn = UiKit.AddButton(_rows, PanelW - 230, y + 8, 170, RowH - 28,
                afford ? L("ui.vendor.trade") : L("ui.vendor.cant_afford"),
                afford ? () => Game.Network?.SendCraft(key, 1) : null);
            if (!afford)
            {
                btn.interactable = false;
                var img = btn.GetComponent<Image>();
                if (img != null)
                {
                    img.color = new Color(0.18f, 0.20f, 0.24f, 0.9f);
                }
            }
        }

        /// <summary>#2958: an ingredient in the backpack and hold — a group ingredient (any log) counts every member.</summary>
        private int Pooled(BlocksBeyondTheStars.Shared.Definitions.ItemAmount need)
        {
            int n = Pooled(need.Item);
            foreach (var member in Game.Content.GroupMembersOf(need))
            {
                n += member == need.Item ? 0 : Pooled(member);
            }

            return n;
        }

        private string IngredientName(BlocksBeyondTheStars.Shared.Definitions.ItemAmount need)
            => string.IsNullOrEmpty(need.Group) ? ItemName(need.Item) : L(BlocksBeyondTheStars.Shared.Content.GameContent.ItemGroupNameKey(need.Group));

        private int Pooled(string item)
        {
            int n = 0;
            if (Game.Personal != null)
            {
                foreach (var s in Game.Personal)
                {
                    if (s.Item == item) n += s.Count;
                }
            }

            if (Game.Cargo != null)
            {
                foreach (var s in Game.Cargo)
                {
                    if (s.Item == item) n += s.Count;
                }
            }

            return n;
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.InventoryUpdated -= OnInventory;
                Game.Network.WorldResetReceived -= OnWorldReset;
            }

            // A scene-root canvas, kept (disabled) once built — it goes with the rig (#2936).
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }
    }
}
