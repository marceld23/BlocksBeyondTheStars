using System.Collections.Generic;
using Spacecraft.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace Spacecraft.Client
{
    /// <summary>
    /// Player-to-player interactions (M24): ship docking (M18) and trading (server-authoritative).
    /// Initiation is key-driven (no cursor needed): <b>T</b> requests a trade and <b>K</b> a dock
    /// with a nearby player, <b>U</b> undocks. The two interactive windows — an incoming docking
    /// request and the open trade — are modal: they free the cursor (via <c>GameBootstrap.MenuOpen</c>,
    /// which pauses the player controller) and are driven with the mouse. Every action sends an
    /// existing intent; the server validates range, ownership and the atomic swap.
    /// </summary>
    public sealed class PlayerInteractions : MonoBehaviour
    {
        public GameBootstrap Game;
        public RemotePlayers Remotes;

        public float InteractRange = 6f;

        private bool _managedCursor;

        private void Update()
        {
            if (Game?.Network == null)
            {
                return;
            }

            // Our windows are modal: while one is up, free the cursor and pause on-foot control.
            bool modal = Game.TradeActive || !string.IsNullOrEmpty(Game.PendingDockFrom);
            if (modal && !_managedCursor)
            {
                Game.MenuOpen = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                _managedCursor = true;
            }
            else if (!modal && _managedCursor)
            {
                Game.MenuOpen = false;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                _managedCursor = false;
            }

            if (modal || Game.MenuOpen || Game.SpaceViewActive || Game.ChatTyping)
            {
                return; // don't start new interactions while a panel/space view/chat is up
            }

            // Leave a boarded space station (returns you to your ship). Boarding it is otherwise a one-way trip.
            if (!string.IsNullOrEmpty(Game.StationName))
            {
                if (Input.GetKeyDown(KeyCode.U))
                {
                    Game.Network.SendLeaveStation();
                }

                return;
            }

            // Undock (when currently docked).
            if (Game.Dock != null && Game.Dock.Docked)
            {
                if (Input.GetKeyDown(KeyCode.U))
                {
                    Game.Network.SendUndock();
                }

                return;
            }

            // Otherwise, target a nearby player for a trade (T) or dock (K) request.
            string target = NearbyPlayer();
            if (string.IsNullOrEmpty(target))
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.T))
            {
                Game.Network.SendTradeRequest(target);
            }
            else if (Input.GetKeyDown(KeyCode.K))
            {
                Game.Network.SendDockRequest(target);
            }
        }

        private string NearbyPlayer()
        {
            if (Remotes == null)
            {
                return null;
            }

            var near = Remotes.PlayersWithin(Game.PlayerPosition, InteractRange);
            return near.Count > 0 ? near[0] : null;
        }

        // ── Modern uGUI build (replaces the IMGUI windows) ───────────────────────────────────
        private Canvas _canvas;
        private Text _hint;
        private GameObject _dockPanel;
        private Text _dockName;
        private GameObject _tradePanel;
        private Text _tradeTitle, _myStatus, _theirStatus;
        private Text _myKnow, _theirKnow; // item 11: knowledge offered on each side
        private RectTransform _myContent, _theirContent;
        private readonly List<OfferRow> _myRows = new List<OfferRow>();
        private readonly List<Text> _theirRows = new List<Text>();

        private sealed class OfferRow
        {
            public GameObject Go;
            public Text Label;
            public string Item;
        }

        private void LateUpdate()
        {
            if (Game?.Localizer == null)
            {
                return;
            }

            EnsureBuilt();
            var loc = Game.Localizer;

            bool dock = !string.IsNullOrEmpty(Game.PendingDockFrom);
            bool trade = Game.TradeActive && Game.Trade != null;
            _dockPanel.SetActive(dock);
            _tradePanel.SetActive(trade);

            if (dock)
            {
                _dockName.text = Game.PendingDockFrom;
            }

            if (trade)
            {
                RefreshTrade(loc);
            }

            // Non-modal centre hint (no cursor): undock, or trade/dock a nearby player.
            string hint = null;
            if (!dock && !trade && !Game.MenuOpen && !Game.SpaceViewActive)
            {
                if (!string.IsNullOrEmpty(Game.StationName))
                {
                    hint = loc.Get("ui.station.leave_hint");
                }
                else if (Game.Dock != null && Game.Dock.Docked)
                {
                    hint = $"{loc.Get("ui.dock.docked")} {Game.Dock.Partner} · {loc.Get("ui.dock.undock_hint")}";
                }
                else
                {
                    string near = NearbyPlayer();
                    if (!string.IsNullOrEmpty(near))
                    {
                        hint = $"{loc.Get("ui.interact.trade_dock")} {near}";
                    }
                }
            }

            _hint.gameObject.SetActive(hint != null);
            if (hint != null)
            {
                _hint.text = hint;
            }
        }

        private void OnDestroy()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }

        private void EnsureBuilt()
        {
            if (_canvas != null)
            {
                return;
            }

            var loc = Game.Localizer;
            _canvas = UiKit.CreateCanvas("Player Interactions");
            _canvas.sortingOrder = 22; // above the HUD, below the pause/menu screens
            var root = _canvas.transform;

            // Bottom-centre hint label.
            var hintGo = new GameObject("Hint", typeof(RectTransform));
            hintGo.transform.SetParent(root, false);
            var hrt = hintGo.GetComponent<RectTransform>();
            hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0.5f, 0.5f);
            hrt.sizeDelta = new Vector2(700f, 26f);
            hrt.anchoredPosition = new Vector2(0f, -120f);
            _hint = hintGo.AddComponent<Text>();
            _hint.font = UiKit.Font;
            _hint.fontSize = 20;
            _hint.color = UiKit.TextCol;
            _hint.alignment = TextAnchor.MiddleCenter;
            _hint.fontStyle = FontStyle.Bold;
            _hint.horizontalOverflow = HorizontalWrapMode.Overflow;
            _hint.raycastTarget = false;
            _hint.gameObject.SetActive(false);

            BuildDockPanel(root, loc);
            BuildTradePanel(root, loc);
        }

        private void BuildDockPanel(Transform root, Spacecraft.Shared.Localization.Localizer loc)
        {
            _dockPanel = CenterPanel(root, 380f, 150f, 0f, "Dock Request");
            UiKit.AddText(_dockPanel.transform, 20f, 14f, 340f, 26f, loc.Get("ui.dock.title"), 22, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _dockName = UiKit.AddText(_dockPanel.transform, 20f, 50f, 340f, 24f, string.Empty, 20, UiKit.TextCol);

            UiKit.AddButton(_dockPanel.transform, 20f, 96f, 165f, 36f, loc.Get("ui.action.accept"), () =>
            {
                Game.Network.SendDockResponse(Game.PendingDockFrom, true);
                Game.PendingDockFrom = string.Empty;
            });
            UiKit.AddButton(_dockPanel.transform, 195f, 96f, 165f, 36f, loc.Get("ui.action.decline"), () =>
            {
                Game.Network.SendDockResponse(Game.PendingDockFrom, false);
                Game.PendingDockFrom = string.Empty;
            });
            _dockPanel.SetActive(false);
        }

        private void BuildTradePanel(Transform root, Spacecraft.Shared.Localization.Localizer loc)
        {
            const float w = 620f, h = 480f;
            _tradePanel = CenterPanel(root, w, h, 0f, "Trade");
            var t = _tradePanel.transform;
            _tradeTitle = UiKit.AddText(t, 20f, 14f, w - 40f, 26f, loc.Get("ui.trade.title"), 22, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            _myStatus = UiKit.AddText(t, 20f, 48f, 280f, 22f, loc.Get("ui.trade.your_offer"), 18, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            _theirStatus = UiKit.AddText(t, 320f, 48f, 280f, 22f, loc.Get("ui.trade.their_offer"), 18, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);

            // Knowledge row (item 11): teach knowledge for goods. My side adjustable; theirs read-only.
            _myKnow = UiKit.AddText(t, 20f, 76f, 150f, 22f, string.Empty, 16, UiKit.Cyan, TextAnchor.MiddleLeft);
            UiKit.AddButton(t, 172f, 74f, 26f, 24f, "−", () => AdjustKnowledge(-1));
            UiKit.AddButton(t, 200f, 74f, 26f, 24f, "+", () => AdjustKnowledge(+1));
            UiKit.AddButton(t, 230f, 74f, 60f, 24f, loc.Get("ui.trade.max"), () => SetKnowledgeMax());
            _theirKnow = UiKit.AddText(t, 320f, 76f, 280f, 22f, string.Empty, 16, UiKit.Cyan, TextAnchor.MiddleLeft);

            _myContent = UiKit.ScrollList(t, 16f, 104f, 292f, h - 104f - 64f);
            _theirContent = UiKit.ScrollList(t, 316f, 104f, 288f, h - 104f - 64f);

            UiKit.AddButton(t, 20f, h - 52f, 180f, 38f, loc.Get("ui.action.confirm"), () => Game.Network.SendTradeConfirm());
            UiKit.AddButton(t, 210f, h - 52f, 180f, 38f, loc.Get("ui.action.cancel"), () => Game.Network.SendTradeCancel());
            _tradePanel.SetActive(false);
        }

        /// <summary>Rebuilds the trade lists each frame from the authoritative offers.</summary>
        private void RefreshTrade(Spacecraft.Shared.Localization.Localizer loc)
        {
            var trade = Game.Trade;
            _tradeTitle.text = $"{loc.Get("ui.trade.title")} — {trade.Partner}";
            _myStatus.text = $"{loc.Get("ui.trade.your_offer")}  {(trade.MyConfirmed ? loc.Get("ui.trade.ready") : string.Empty)}";
            _theirStatus.text = $"{loc.Get("ui.trade.their_offer")}  {(trade.TheirConfirmed ? loc.Get("ui.trade.ready") : loc.Get("ui.trade.waiting"))}";

            // Knowledge offered each way: "Knowledge →N / max" on my side, "Knowledge →N" on theirs.
            string know = loc.Get("ui.trade.knowledge");
            _myKnow.text = $"{know} →{trade.MyKnowledgeOffered}/{trade.MyKnowledgeMax}";
            _theirKnow.text = trade.TheirKnowledgeOffered > 0 ? $"{know} →{trade.TheirKnowledgeOffered}" : string.Empty;

            // Left column: every owned item as an adjustable offer row (offered ×N out of owned).
            var offered = new Dictionary<string, int>();
            foreach (var it in trade.MyOffer)
            {
                offered[it.Item] = it.Count;
            }

            int i = 0;
            foreach (var s in Game.Personal)
            {
                var row = MyRow(i++);
                row.Item = s.Item;
                int have = offered.TryGetValue(s.Item, out var o) ? o : 0;
                row.Label.text = have > 0
                    ? $"{ItemName(loc, s.Item)}  ×{s.Count}   →{have}"
                    : $"{ItemName(loc, s.Item)}  ×{s.Count}";
                row.Go.SetActive(true);
            }

            for (; i < _myRows.Count; i++)
            {
                _myRows[i].Go.SetActive(false);
            }

            // Right column: their offer (read-only).
            int j = 0;
            foreach (var it in trade.TheirOffer)
            {
                var txt = TheirRow(j++);
                txt.text = $"{ItemName(loc, it.Item)}  ×{it.Count}";
                txt.gameObject.SetActive(true);
            }

            for (; j < _theirRows.Count; j++)
            {
                _theirRows[j].gameObject.SetActive(false);
            }
        }

        private OfferRow MyRow(int index)
        {
            while (index >= _myRows.Count)
            {
                var go = new GameObject("Row", typeof(RectTransform));
                go.transform.SetParent(_myContent, false);
                var rt = go.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(0f, 28f);
                var le = go.AddComponent<LayoutElement>();
                le.minHeight = le.preferredHeight = 28f;

                var label = UiKit.AddText(go.transform, 4f, 2f, 192f, 24f, string.Empty, 17, UiKit.TextCol);
                label.GetComponent<RectTransform>().anchoredPosition = new Vector2(4f, -2f);

                var row = new OfferRow { Go = go, Label = label };
                UiKit.AddButton(go.transform, 204f, 0f, 36f, 26f, "−", () => Adjust(row.Item, -1));
                UiKit.AddButton(go.transform, 244f, 0f, 36f, 26f, "+", () => Adjust(row.Item, +1));
                _myRows.Add(row);
            }

            return _myRows[index];
        }

        private Text TheirRow(int index)
        {
            while (index >= _theirRows.Count)
            {
                var go = new GameObject("Row", typeof(RectTransform));
                go.transform.SetParent(_theirContent, false);
                go.AddComponent<LayoutElement>().minHeight = 26f;
                var txt = go.AddComponent<Text>();
                txt.font = UiKit.Font;
                txt.fontSize = 17;
                txt.color = UiKit.TextCol;
                txt.alignment = TextAnchor.MiddleLeft;
                txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                txt.raycastTarget = false;
                _theirRows.Add(txt);
            }

            return _theirRows[index];
        }

        /// <summary>A centred panel (anchored to screen centre) with a rounded sci-fi backdrop.</summary>
        private static GameObject CenterPanel(Transform parent, float w, float h, float offY, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(0f, offY);
            var img = go.AddComponent<Image>();
            img.sprite = UiKit.PanelSprite;
            img.type = Image.Type.Sliced;
            img.color = UiKit.PanelFill;
            return go;
        }


        /// <summary>Rebuilds the offer from the server's authoritative <c>MyOffer</c> with one item changed, and pushes it.</summary>
        private void Adjust(string item, int delta)
        {
            var dict = new Dictionary<string, int>();
            foreach (var it in Game.Trade.MyOffer)
            {
                dict[it.Item] = it.Count;
            }

            int next = Mathf.Max(0, (dict.TryGetValue(item, out var cur) ? cur : 0) + delta);
            if (next == 0)
            {
                dict.Remove(item);
            }
            else
            {
                dict[item] = next;
            }

            var offer = new List<NetTradeItem>();
            foreach (var kv in dict)
            {
                offer.Add(new NetTradeItem { Item = kv.Key, Count = kv.Value });
            }

            Game.Network.SendTradeOffer(offer.ToArray());
        }

        /// <summary>Nudges the knowledge offered to teach this partner (server clamps to the give-once cap).</summary>
        private void AdjustKnowledge(int delta)
        {
            int next = Mathf.Clamp(Game.Trade.MyKnowledgeOffered + delta, 0, Game.Trade.MyKnowledgeMax);
            Game.Network.SendTradeKnowledge(next);
        }

        private void SetKnowledgeMax() => Game.Network.SendTradeKnowledge(Game.Trade.MyKnowledgeMax);

        private string ItemName(Spacecraft.Shared.Localization.Localizer loc, string itemKey) => loc.Get($"item.{itemKey}.name");
    }
}
