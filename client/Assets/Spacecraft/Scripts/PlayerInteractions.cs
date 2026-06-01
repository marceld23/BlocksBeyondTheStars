using System.Collections.Generic;
using Spacecraft.Networking.Messages;
using UnityEngine;

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

            if (modal || Game.MenuOpen || Game.SpaceViewActive)
            {
                return; // don't start new interactions while a panel/space view is up
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

        private void OnGUI()
        {
            if (Game?.Localizer == null)
            {
                return;
            }

            var loc = Game.Localizer;

            if (!string.IsNullOrEmpty(Game.PendingDockFrom))
            {
                DrawDockRequest(loc);
                return;
            }

            if (Game.TradeActive && Game.Trade != null)
            {
                DrawTrade(loc);
                return;
            }

            // Non-modal hints (no cursor): undock, or trade/dock a nearby player.
            if (Game.MenuOpen || Game.SpaceViewActive)
            {
                return;
            }

            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            if (Game.Dock != null && Game.Dock.Docked)
            {
                string label = $"{loc.Get("ui.dock.docked")} {Game.Dock.Partner} · {loc.Get("ui.dock.undock_hint")}";
                GUI.Label(new Rect(Screen.width / 2f - 200, Screen.height / 2f + 72, 400, 22), label, style);
                return;
            }

            string near = NearbyPlayer();
            if (!string.IsNullOrEmpty(near))
            {
                GUI.Label(new Rect(Screen.width / 2f - 200, Screen.height / 2f + 72, 400, 22),
                    $"{loc.Get("ui.interact.trade_dock")} {near}", style);
            }
        }

        private void DrawDockRequest(Spacecraft.Shared.Localization.Localizer loc)
        {
            const float w = 320f, h = 120f;
            var area = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(area, loc.Get("ui.dock.title"));
            GUILayout.BeginArea(new Rect(area.x + 14, area.y + 30, w - 28, h - 44));
            GUILayout.Label(Game.PendingDockFrom);
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(loc.Get("ui.action.accept")))
            {
                Game.Network.SendDockResponse(Game.PendingDockFrom, true);
                Game.PendingDockFrom = string.Empty;
            }

            if (GUILayout.Button(loc.Get("ui.action.decline")))
            {
                Game.Network.SendDockResponse(Game.PendingDockFrom, false);
                Game.PendingDockFrom = string.Empty;
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private Vector2 _tradeScroll;

        private void DrawTrade(Spacecraft.Shared.Localization.Localizer loc)
        {
            var trade = Game.Trade;
            const float w = 560f, h = 420f;
            var area = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(area, $"{loc.Get("ui.trade.title")} — {trade.Partner}");
            GUILayout.BeginArea(new Rect(area.x + 12, area.y + 28, w - 24, h - 40));

            GUILayout.BeginHorizontal();

            // Left column: my offer (editable) + the add-from-inventory list.
            GUILayout.BeginVertical(GUILayout.Width(264));
            GUILayout.Label($"{loc.Get("ui.trade.your_offer")}  {(trade.MyConfirmed ? loc.Get("ui.trade.ready") : string.Empty)}");
            foreach (var it in trade.MyOffer)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{ItemName(loc, it.Item)} ×{it.Count}", GUILayout.Width(180));
                if (GUILayout.Button("−", GUILayout.Width(28)))
                {
                    Adjust(it.Item, -1);
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.Label(loc.Get("ui.trade.add"));
            _tradeScroll = GUILayout.BeginScrollView(_tradeScroll, GUILayout.Height(180));
            foreach (var s in Game.Personal)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{ItemName(loc, s.Item)} ×{s.Count}", GUILayout.Width(180));
                if (GUILayout.Button("+", GUILayout.Width(28)))
                {
                    Adjust(s.Item, +1);
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            // Right column: their offer (read-only).
            GUILayout.BeginVertical(GUILayout.Width(264));
            GUILayout.Label($"{loc.Get("ui.trade.their_offer")}  {(trade.TheirConfirmed ? loc.Get("ui.trade.ready") : loc.Get("ui.trade.waiting"))}");
            foreach (var it in trade.TheirOffer)
            {
                GUILayout.Label($"  {ItemName(loc, it.Item)} ×{it.Count}");
            }

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(loc.Get("ui.action.confirm"), GUILayout.Width(120)))
            {
                Game.Network.SendTradeConfirm();
            }

            if (GUILayout.Button(loc.Get("ui.action.cancel"), GUILayout.Width(120)))
            {
                Game.Network.SendTradeCancel();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
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

        private string ItemName(Spacecraft.Shared.Localization.Localizer loc, string itemKey) => loc.Get($"item.{itemKey}.name");
    }
}
