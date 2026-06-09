using System.Linq;
using Spacecraft.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace Spacecraft.Client
{
    /// <summary>
    /// A dedicated vendor trade (barter) screen, opened with E at a settlement/station vendor (B22) instead of
    /// the full crafting menu. It lists the market exchanges (the <c>station:"market"</c> recipes — "give X →
    /// get Y") as trade cards; ones the player can't afford are greyed out, and clicking a tradeable one sends
    /// the existing <see cref="NetworkClient.SendCraft"/> intent (the server validates the vendor proximity +
    /// materials). The economy is pure barter — there's no currency. Refreshes on every inventory update.
    /// </summary>
    public sealed class VendorTradeUI : MonoBehaviour
    {
        public GameBootstrap Game;
        public GameMenu Menu;

        private const float W = 1920f, H = 1080f;
        private const float PanelW = 1000f, PanelH = 660f;
        private const float RowH = 84f, RowsTop = 156f;

        private Canvas _canvas;
        private Transform _root;
        private Transform _rows;   // recipe cards live here; cleared + rebuilt
        private Text _title;
        private bool _built, _open, _subscribed;
        private int _openedFrame;

        public bool IsOpen => _open;

        public void Open()
        {
            Build();
            Menu?.CloseFromUi();           // mutually exclusive with the crafting/Tab menu
            _open = true;
            _openedFrame = Time.frameCount; // so the same E press that opened it doesn't close it this frame
            _canvas.enabled = true;
            if (Game != null)
            {
                Game.MenuOpen = true;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
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

            if (Game != null)
            {
                Game.MenuOpen = false;
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
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
                Close();
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

        private string ItemName(string item) => L($"item.{item}.name");

        private void Build()
        {
            if (_built)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas("VendorTradeUI");
            _canvas.sortingOrder = 60; // above the crafting menu (50)
            _root = _canvas.transform;

            UiKit.AddImage(_root, 0, 0, W, H, UiKit.SolidSprite, new Color(0.02f, 0.04f, 0.08f, 0.72f)); // dim backdrop

            float px = (W - PanelW) * 0.5f, py = (H - PanelH) * 0.5f;
            UiKit.AddPanel(_root, px, py, PanelW, PanelH, UiKit.Panel);

            _title = UiKit.AddText(_root, px + 40, py + 26, PanelW - 80, 40, string.Empty, 32, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(_root, px + 40, py + 78, PanelW - 80, 26, L("ui.vendor.subtitle"), 18, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.AddText(_root, px + 40, py + 116, 380, 22, L("ui.vendor.give"), 15, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(_root, px + 470, py + 116, 320, 22, L("ui.vendor.get"), 15, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);

            var rowsGo = new GameObject("Rows", typeof(RectTransform));
            rowsGo.transform.SetParent(_root, false);
            UiKit.Place(rowsGo, px, py + RowsTop, PanelW, PanelH - RowsTop - 80);
            _rows = rowsGo.transform;

            UiKit.AddText(_root, px + 40, py + PanelH - 50, PanelW - 320, 26, L("ui.vendor.hint"), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.AddButton(_root, px + PanelW - 220, py + PanelH - 64, 180, 46, L("ui.action.close"), Close);

            _built = true;
        }

        private void Rebuild()
        {
            if (!_built)
            {
                return;
            }

            for (int i = _rows.childCount - 1; i >= 0; i--)
            {
                Destroy(_rows.GetChild(i).gameObject);
            }

            _title.text = NearestVendorName();

            // A vendor only posts goods for its settlement's trade (mining/trading/research/settler); themeless
            // recipes barter everywhere. Aboard the ship console (no vendor) only the themeless deals show.
            string vendorTheme = NearestVendorTheme();

            int row = 0;
            foreach (var r in Game.Content.Recipes.Values)
            {
                if (r.Station != Spacecraft.Shared.Definitions.CraftingStation.Market
                    || r.Outputs.Count == 0 || r.Inputs.Count == 0)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(r.MarketTheme)
                    && !string.Equals(r.MarketTheme, vendorTheme, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue; // this trade belongs to a different kind of settlement
                }

                AddRow(row++, r);
            }

            if (row == 0)
            {
                UiKit.AddText(_rows, 20, 10, PanelW - 40, 30, L("ui.vendor.empty"), 18, UiKit.CyanDim, TextAnchor.MiddleLeft);
            }
        }

        private void AddRow(int index, Spacecraft.Shared.Definitions.RecipeDefinition r)
        {
            float y = index * RowH;
            bool afford = r.Inputs.All(inp => Pooled(inp.Item) >= inp.Count);

            UiKit.AddPanel(_rows, 20, y, PanelW - 40, RowH - 12, new Color(0.06f, 0.14f, 0.26f, 0.85f));

            string give = string.Join("   ", r.Inputs.Select(a => $"{a.Count}× {ItemName(a.Item)}"
                + (Pooled(a.Item) < a.Count ? $"  ({Pooled(a.Item)})" : string.Empty)));
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

        /// <summary>The name of the closest vendor NPC (for the screen title), or a generic "Trader" fallback.</summary>
        private string NearestVendorName()
        {
            var p = Game.PlayerPosition;
            string best = null;
            float bestSq = float.MaxValue;
            foreach (var n in Game.Npcs)
            {
                if (n.Role != "vendor")
                {
                    continue;
                }

                float sq = (Game.ScenePos(n.X, n.Y, n.Z) - p).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = string.IsNullOrEmpty(n.Name) ? null : n.Name;
                }
            }

            return best ?? L("ui.vendor.title");
        }

        /// <summary>The trade theme of the closest vendor NPC (miners/traders/researchers/settlers), or empty
        /// when none is in reach (aboard the ship's own console) — used to show only that settlement's offers.</summary>
        private string NearestVendorTheme()
        {
            var p = Game.PlayerPosition;
            string theme = string.Empty;
            float bestSq = 3.6f * 3.6f; // same reach as Game.NearVendor
            foreach (var n in Game.Npcs)
            {
                if (n.Role != "vendor")
                {
                    continue;
                }

                float sq = (Game.ScenePos(n.X, n.Y, n.Z) - p).sqrMagnitude;
                if (sq <= bestSq)
                {
                    bestSq = sq;
                    theme = n.Theme ?? string.Empty;
                }
            }

            return theme;
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.InventoryUpdated -= OnInventory;
                Game.Network.WorldResetReceived -= OnWorldReset;
            }
        }
    }
}
