// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Player-to-player interactions (M24): ship docking (M18) and trading (server-authoritative).
    /// Initiation is key-driven (no cursor needed): <b>T</b> requests a trade and <b>K</b> a dock
    /// with a nearby player, <b>U</b> undocks. The two interactive windows — an incoming docking
    /// request and the open trade — are modal: they free the cursor (via <c>GameBootstrap.MenuOpen</c>,
    /// which pauses the player controller) and are driven with the mouse. Every action sends an
    /// existing intent; the server validates range, ownership and the atomic swap.
    /// #2724: in the flight view and on a spacewalk <b>K</b> docks with the nearest other pilot and <b>U</b> undocks
    /// (T is the target key there). #2725: as a guest in a docked friend's ship <b>U</b> goes back to the own ship, the
    /// host's <b>U</b> sends the guest home (it undocks), and <b>T</b> trades while docked.
    /// </summary>
    public sealed class PlayerInteractions : MonoBehaviour
    {
        public GameBootstrap Game;
        public RemotePlayers Remotes;

        /// <summary>#2724: the flight view — K's nearest pilot and the docking-visit picture come from it.</summary>
        public SpaceView Space;

        /// <summary>How close another player must be before T/K are offered. Matches the server's trade range
        /// (8 m): a shorter client range swallowed the keypress silently just outside it — no window, no
        /// message, no sound — which reads as a broken key rather than "step closer" (#981).</summary>
        public float InteractRange = 8f;

        private void Update()
        {
            if (Game?.Network == null)
            {
                return;
            }

            // Our windows are modal: while one is up, free the cursor and pause on-foot control.
            // Level-triggered on the server-driven state (a dock/trade request can arrive or resolve
            // remotely at any time, even under the Tab menu). The per-owner registration is idempotent,
            // and the arbiter keeps the cursor free while ANY owner — us or the menu above us — is still
            // open, so a remote resolve can no longer strand a locked cursor (#407 → #413).
            bool modal = Game.TradeActive
                || !string.IsNullOrEmpty(Game.PendingDockFrom)
                || !string.IsNullOrEmpty(Game.PendingTradeFrom);
            Game.SetMenuOwner(this, modal);

            // The text-entry gate is already inside InputMap.Down (#1858); named here too so a "U" typed into
            // any field stays a letter even if a future verb here reads a raw key.
            if (modal || Game.MenuOpen || Game.ChatTyping || InputMap.TextEntryActive || GameBootstrap.CutsceneActive)
            {
                return; // don't start new interactions while a panel/chat/text field/cutscene (#2461) is up
            }

            // #2724: in flight and on a spacewalk K docks with the nearest other pilot and U undocks — both keys are free
            // there (T is the flight target key, so trading stays an on-foot verb). E (go over) belongs to the flight view.
            if (Game.SpaceViewActive)
            {
                if (FlightDockLive)
                {
                    if (Game.Docked)
                    {
                        if (InputMap.Down(InputAction.Disembark))
                        {
                            Game.Network.SendUndock();
                        }
                    }
                    else if (Space.DockPilot != null && InputMap.Down(InputAction.RequestDock))
                    {
                        Game.Network.SendDockRequest(Space.DockPilot.PlayerId);
                    }
                }

                return;
            }

            // Leave a boarded space station (returns you to your ship). Boarding it is otherwise a one-way trip.
            if (!string.IsNullOrEmpty(Game.StationName))
            {
                if (InputMap.Down(InputAction.Disembark))
                {
                    Game.Network.SendLeaveStation();
                }
                else if (CanToggleStationZeroG && InputMap.Down(InputAction.ToggleStationZeroG))
                {
                    // #1842: zero-g construction mode on a player-built station — the server owns the flag and
                    // answers with the player state; the HUD hint comes from that, not from here.
                    Game.Network.SendSetStationZeroG(!Game.StationZeroG);
                }

                return;
            }

            // #2725: a guest in a docked friend's ship — U takes us back to our own ship (the server ends the visit).
            if (Game.Visiting)
            {
                if (InputMap.Down(InputAction.Disembark))
                {
                    GoHome();
                }
            }
            else if (Game.Docked && InputMap.Down(InputAction.Disembark))
            {
                Game.Network.SendUndock(); // undock — on the host's side this also sends a visiting friend home
            }

            // Target a nearby player for a trade (T) — #2725: also while docked, the friend stands beside us in the ship —
            // or a dock (K) request, which only means something while not docked yet.
            string target = NearbyPlayer();
            if (string.IsNullOrEmpty(target))
            {
                return;
            }

            if (InputMap.Down(InputAction.RequestTrade))
            {
                Game.Network.SendTradeRequest(target);
            }
            else if (!Game.Docked && InputMap.Down(InputAction.RequestDock))
            {
                Game.Network.SendDockRequest(target);
            }
        }

        /// <summary>#2725: "back to my ship" for a guest — the existing step into the own ship's interior (an
        /// <c>EnterShipIntent</c>), veiled at once like every descent-less world change (B34). A refusal lets the veil drop
        /// itself (<see cref="WorldLoadingOverlay"/>'s confirm timeout).</summary>
        public void GoHome()
        {
            if (Game?.Network == null || !Game.Visiting)
            {
                return;
            }

            Game.BeginWorldTransition();
            Game.Network.SendEnterShip();
        }

        /// <summary>#2724: the flight view takes the docking verbs right now (cruising or on a spacewalk, nothing else under way).</summary>
        private bool FlightDockLive => Game != null && Game.SpaceViewActive && Space != null && Space.DockVerbsLive;

        private string NearbyPlayer()
        {
            if (Remotes == null || Game == null)
            {
                return null;
            }

            return Remotes.FirstPlayerWithin(Game.PlayerPosition, InteractRange);
        }

        /// <summary>True while a trade request could be sent right now: a player in reach and we are not aboard a station —
        /// #2725: docked too (the friend stands beside us in the ship). Gates the context-actions entry (#1042/#1043).</summary>
        public bool CanRequestTrade =>
            Game != null && string.IsNullOrEmpty(Game.StationName) && !string.IsNullOrEmpty(NearbyPlayer());

        /// <summary>True while a dock request could be sent right now: a player in reach, and we are neither docked nor
        /// aboard a station. Gates the context-actions entry (#1042/#1043).</summary>
        public bool CanRequestDock =>
            Game != null && string.IsNullOrEmpty(Game.StationName) && !Game.Docked && !string.IsNullOrEmpty(NearbyPlayer());

        /// <summary>True while <see cref="InputAction.Disembark"/> would do something on foot (docked, visiting a friend's
        /// ship, or aboard a station).</summary>
        public bool CanDisembark =>
            Game != null && (!string.IsNullOrEmpty(Game.StationName) || Game.Docked || Game.Visiting);

        /// <summary>#2724: K would dock in flight or on a spacewalk right now (another pilot within the docking range, not
        /// docked yet) — gates the Actions-list entry for pads and tablets.</summary>
        public bool CanDockInFlight => FlightDockLive && !Game.Docked && Space.DockPilot != null;

        /// <summary>#2724: U would undock in flight or on a spacewalk right now — gates the Actions-list entry.</summary>
        public bool CanUndockInFlight => FlightDockLive && Game.Docked;

        /// <summary>#2724: E would go over to the docked friend's ship right now (the flight view's pick) — gates the
        /// Actions-list entry "go over".</summary>
        public bool CanGoOverInFlight => FlightDockLive && Space.InteractGoesOver;

        /// <summary>True while <see cref="InputAction.ToggleStationZeroG"/> would do something (#1842): boarded on a
        /// PLAYER-built station — NPC / template stations keep their decks' gravity, and the server ignores the
        /// intent there anyway. Gates the key and the context-actions entry.</summary>
        public bool CanToggleStationZeroG =>
            Game != null && !string.IsNullOrEmpty(Game.StationName)
            && Game.CurrentStationId.StartsWith("pstation:", System.StringComparison.Ordinal);

        // ── uGUI build ────────────────────────────────────────────────────────────────────────
        // Restyled for #1058: the three windows (trade, incoming trade request, incoming dock request)
        // are proper modal dialogs — UiKit.AddModalOverlay (scrim + opaque dialog, #588), fade-in, the
        // same title/subtitle/typography and ≥44 px controls as the rest of the game, item icons via
        // IconResolver, and Esc / pad B as the cancel verb. Layout is in 1920×1080 reference units.
        private const float DialogW = 900f, DialogH = 640f;
        private const float DialogX = (1920f - DialogW) * 0.5f, DialogY = (1080f - DialogH) * 0.5f;
        private const float AskW = 540f, AskH = 236f;
        private const float AskX = (1920f - AskW) * 0.5f, AskY = (1080f - AskH) * 0.5f;
        private const float ColLeftX = 28f, ColLeftW = 404f, ColRightX = 456f, ColRightW = 416f;
        private const float InvRowH = 56f, OfferRowH = 40f;

        private Canvas _canvas;
        private Text _hint;
        private GameObject _dockOverlay, _tradeAskOverlay, _tradeOverlay;
        private Text _dockName, _tradeAskName;
        private Text _tradeTitle, _givePill, _getPill, _getTitle, _myKnow, _theirKnow, _knowHint, _footHint;
        private Image _givePillBg, _getPillBg;
        private GameObject _knowControls;
        private Button _confirmBtn;
        private Text _confirmLabel;
        private Image _confirmImg;
        private RectTransform _invContent, _giveContent, _getContent;
        private Text _invEmpty, _giveEmpty, _getEmpty;
        private readonly List<InvRow> _invRows = new List<InvRow>();
        private readonly List<OfferRow> _giveRows = new List<OfferRow>();
        private readonly List<OfferRow> _getRows = new List<OfferRow>();
        private bool _tradeShown, _tradeAskShown, _dockShown;
        private TradeUpdate _renderedTrade;
        private NetItemStack[] _renderedPersonal;
        private BlocksBeyondTheStars.Shared.Localization.Localizer _renderedLoc;

        /// <summary>One inventory line: icon + name + owned count + the −/n/+ offer control.</summary>
        private sealed class InvRow
        {
            public GameObject Go;
            public Image Back, Icon;
            public Text Name, Have, Offered;
            public string Item;
        }

        /// <summary>One read-only offer line (give / get boxes): icon + name + count.</summary>
        private sealed class OfferRow
        {
            public GameObject Go;
            public Image Icon;
            public Text Name, Count;
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
            // One window at a time: both are centred, so a trade invitation waits behind an open trade or a
            // docking request rather than stacking on top of it (the state survives, it just shows later).
            bool tradeAsk = !trade && !dock && !string.IsNullOrEmpty(Game.PendingTradeFrom);

            // Esc (keyboard) / B (pad) is the cancel verb of whichever window is up — the same pair every
            // other in-game modal uses. AppShell swallows the Esc anyway while a menu owner is registered;
            // MarkMenuInputHandled makes that explicit. Chat typing keeps its own Esc.
            bool cancelKey = !Game.ChatTyping && InputMap.Down(InputAction.UiCancel);
            if (cancelKey && (trade || dock || tradeAsk))
            {
                Game.MarkMenuInputHandled();
                if (trade)
                {
                    Game.Network.SendTradeCancel();
                }
                else if (dock)
                {
                    RespondDock(false);
                }
                else
                {
                    RespondTrade(false);
                }
            }

            ShowOverlay(_dockOverlay, dock, ref _dockShown);
            ShowOverlay(_tradeOverlay, trade, ref _tradeShown);
            ShowOverlay(_tradeAskOverlay, tradeAsk, ref _tradeAskShown);

            if (dock)
            {
                _dockName.text = Game.PendingDockFrom;
            }

            if (tradeAsk)
            {
                _tradeAskName.text = Game.PendingTradeFrom;
            }

            if (trade)
            {
                RefreshTrade(loc);
            }
            else
            {
                _renderedTrade = null; // next open re-renders from scratch
            }

            // Non-modal centre hint (no cursor): undock, go home from a visit, or trade/dock a nearby player — and in flight
            // (#2724) the docked line or whom K docks with. Formatted only when what it says changes.
            string hint = null;
            if (!dock && !trade && !tradeAsk && !Game.MenuOpen && !GameBootstrap.CutsceneActive)
            {
                hint = CurrentHint(loc);
            }

            _hint.gameObject.SetActive(hint != null);
            if (hint != null && !ReferenceEquals(hint, _hint.text))
            {
                _hint.text = hint;
            }
        }

        // The hint's last inputs and text — rebuilt only when one of them changes (it runs every frame).
        private enum HintKind { None, Station, Guest, Host, Docked, Near, FlightDocked, FlightDock }
        private HintKind _hintKind;
        private string _hintName;
        private bool _hintKeys, _hintTrade;
        private object _hintLoc;
        private (ClientSettings, int, PadGlyphSet, bool) _hintStamp;
        private string _hintText;

        /// <summary>
        /// What the centre hint says right now, or null. The keyboard hints name the bound keys ({key:Disembark} /
        /// {key:RequestTrade} / {key:RequestDock} tokens since #2502, U / T / K by default); on pad and touch those verbs live
        /// in the context-actions list, so the hint points there instead (#1042/#1043).
        /// </summary>
        private string CurrentHint(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            bool keys = InputMap.ActiveDevice == InputDeviceKind.KeyboardMouse;
            HintKind kind = HintKind.None;
            string name = null;
            bool tradeToo = false;
            if (Game.SpaceViewActive)
            {
                // #2724: docked — the line names the partner and the undock key; not docked — whom K would dock with.
                if (FlightDockLive)
                {
                    if (Game.Docked)
                    {
                        kind = HintKind.FlightDocked;
                        name = !string.IsNullOrEmpty(Space.Visit.PartnerName) ? Space.Visit.PartnerName : Game.Dock.Partner;
                    }
                    else if (Space.DockPilot != null)
                    {
                        kind = HintKind.FlightDock;
                        name = BlocksBeyondTheStars.Client.Core.ShipVisitRules.DisplayName(Space.DockPilot, Space.DockPilot.PlayerId);
                    }
                }
            }
            else if (!string.IsNullOrEmpty(Game.StationName))
            {
                kind = HintKind.Station;
            }
            else
            {
                string near = NearbyPlayer();
                if (Game.Visiting)
                {
                    kind = HintKind.Guest; // #2725: "Visiting Lena's ship · U: back to my ship"
                    name = Game.VisitingHost;
                    tradeToo = !string.IsNullOrEmpty(near);
                }
                else if (Game.Docked)
                {
                    // #2725: the host whose docked friend walks in the own ship reads "Tom is visiting · U: send home".
                    kind = Remotes != null && Remotes.IsPresent(Game.Dock.Partner) && Game.LoadingPlanetType == "ship_interior"
                        ? HintKind.Host
                        : HintKind.Docked;
                    name = Game.Dock.Partner;
                    tradeToo = !string.IsNullOrEmpty(near);
                }
                else if (!string.IsNullOrEmpty(near))
                {
                    kind = HintKind.Near;
                    name = near;
                }
            }

            if (kind == HintKind.None)
            {
                return null;
            }

            tradeToo &= keys; // pad and touch: trading is in the Actions list already
            var stamp = HudUi.GlyphStamp;
            if (_hintText != null && kind == _hintKind && keys == _hintKeys && tradeToo == _hintTrade && ReferenceEquals(loc, _hintLoc)
                && string.Equals(name, _hintName, System.StringComparison.Ordinal) && stamp.Equals(_hintStamp))
            {
                return _hintText;
            }

            _hintKind = kind;
            _hintName = name;
            _hintKeys = keys;
            _hintTrade = tradeToo;
            _hintLoc = loc;
            _hintStamp = stamp;
            string Expand(string key) => HudUi.ExpandKeyTokens(loc, loc.Get(key));
            string text = kind switch
            {
                HintKind.Station => Expand(keys ? "ui.station.leave_hint" : "ui.station.leave_hint_actions"),
                HintKind.Guest => Expand(keys ? "ui.visit.guest_hint" : "ui.visit.guest_hint_actions").Replace("{name}", name),
                HintKind.Host => Expand(keys ? "ui.visit.host_hint" : "ui.visit.host_hint_actions").Replace("{name}", name),
                HintKind.Docked or HintKind.FlightDocked =>
                    $"{loc.Get("ui.dock.docked")} {name} · {Expand(keys ? "ui.dock.undock_hint" : "ui.dock.undock_hint_actions")}",
                HintKind.FlightDock => $"{Expand(keys ? "ui.dock.dock_hint" : "ui.dock.dock_hint_actions")} {name}",
                _ => $"{Expand(keys ? "ui.interact.trade_dock" : "ui.interact.trade_dock_actions")} {name}",
            };
            if (tradeToo)
            {
                text += " · " + Expand("ui.dock.trade_hint");
            }

            _hintText = text;
            return text;
        }

        /// <summary>Toggles an overlay and plays the shared fade/rise-in on the frame it opens.</summary>
        private static void ShowOverlay(GameObject overlay, bool show, ref bool shown)
        {
            if (show == shown)
            {
                return;
            }

            shown = show;
            overlay.SetActive(show);
            if (show)
            {
                UiKit.OpenModal(overlay); // the scrim fades in place, the dialog panel rises
            }
        }

        private void RespondTrade(bool accept)
        {
            Game.Network.SendTradeRespond(accept);
            Game.PendingTradeFrom = string.Empty; // the trade panel takes over once the server confirms
        }

        private void RespondDock(bool accept)
        {
            Game.Network.SendDockResponse(Game.PendingDockFrom, accept);
            Game.PendingDockFrom = string.Empty;
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

            _dockOverlay = BuildAskDialog(root, loc, "ui.dock.title", "ui.dock.request_body", out _dockName,
                () => RespondDock(true), () => RespondDock(false));
            _dockOverlay.name = "Dock Request";
            // The incoming-trade invitation (#981): without it a trade request was unanswerable.
            _tradeAskOverlay = BuildAskDialog(root, loc, "ui.trade.request_title", "ui.trade.request_body", out _tradeAskName,
                () => RespondTrade(true), () => RespondTrade(false));
            _tradeAskOverlay.name = "Trade Request";
            BuildTradeDialog(root, loc);
        }

        /// <summary>A yes/no request dialog: title, the requesting player's name, one line saying what they
        /// want, Accept / Decline. Both the trade and the dock invitation use it so they look identical.</summary>
        private static GameObject BuildAskDialog(Transform root, BlocksBeyondTheStars.Shared.Localization.Localizer loc,
            string titleKey, string bodyKey, out Text nameLabel, System.Action accept, System.Action decline)
        {
            var (overlay, panel) = UiKit.AddModalOverlay(root, AskX, AskY, AskW, AskH);
            UiKit.AddText(panel, 28f, 22f, AskW - 56f, 34f, loc.Get(titleKey), 26, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            nameLabel = UiKit.AddText(panel, 28f, 66f, AskW - 56f, 32f, string.Empty, 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(panel, 28f, 100f, AskW - 56f, 26f, loc.Get(bodyKey), 17, UiKit.CyanDim, TextAnchor.MiddleLeft);
            float bw = (AskW - 56f - 16f) * 0.5f;
            UiKit.AddButton(panel, 28f, AskH - 72f, bw, 48f, loc.Get("ui.action.accept"), accept);
            UiKit.AddButton(panel, 28f + bw + 16f, AskH - 72f, bw, 48f, loc.Get("ui.action.decline"), decline);
            UiNav.Enable(overlay); // pad: stick walks Accept / Decline (#1043)
            overlay.SetActive(false);
            return overlay;
        }

        private void BuildTradeDialog(Transform root, BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            var (overlay, panel) = UiKit.AddModalOverlay(root, DialogX, DialogY, DialogW, DialogH);
            _tradeOverlay = overlay;
            overlay.name = "Trade";

            _tradeTitle = UiKit.AddText(panel, 28f, 22f, DialogW - 56f, 36f, loc.Get("ui.trade.title"), 30, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(panel, 28f, 62f, DialogW - 56f, 24f, loc.Get("ui.trade.subtitle"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);

            // Left column: my inventory as icon cards, each with the −/n/+ offer control.
            UiKit.AddText(panel, ColLeftX, 100f, ColLeftW, 24f, loc.Get("ui.trade.inventory"), 18, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            _invContent = UiKit.ScrollList(panel, ColLeftX, 130f, ColLeftW, DialogH - 130f - 96f, 4f); // ScrollList brings its own inline scrollbar (#1387)
            _invEmpty = UiKit.AddText(panel, ColLeftX + 12f, 140f, ColLeftW - 24f, 40f, loc.Get("ui.trade.inventory_empty"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);

            // Right column, top: what I give (+ knowledge control) with my ready state.
            UiKit.AddText(panel, ColRightX, 100f, ColRightW - 140f, 24f, loc.Get("ui.trade.you_give"), 18, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            _givePillBg = UiKit.AddPanel(panel, ColRightX + ColRightW - 132f, 98f, 132f, 28f, UiKit.SlotIdle);
            _givePill = UiKit.AddText(panel, ColRightX + ColRightW - 132f, 98f, 132f, 28f, string.Empty, 14, UiKit.CyanDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            _giveContent = UiKit.ScrollList(panel, ColRightX, 130f, ColRightW, 148f, 3f);
            _giveEmpty = UiKit.AddText(panel, ColRightX + 12f, 138f, ColRightW - 24f, 40f, loc.Get("ui.trade.give_empty"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);

            // Knowledge row (item 11): teach knowledge for goods. Mine adjustable; theirs read-only.
            _myKnow = UiKit.AddText(panel, ColRightX, 286f, ColRightW - 156f, 40f, string.Empty, 16, UiKit.Cyan, TextAnchor.MiddleLeft);
            _knowControls = new GameObject("KnowledgeControls", typeof(RectTransform));
            _knowControls.transform.SetParent(panel, false);
            UiKit.Place(_knowControls, ColRightX + ColRightW - 152f, 286f, 152f, 40f);
            UiKit.AddButton(_knowControls.transform, 0f, 0f, 44f, 40f, "−", () => AdjustKnowledge(-1));
            UiKit.AddButton(_knowControls.transform, 48f, 0f, 44f, 40f, "+", () => AdjustKnowledge(+1));
            UiKit.AddButton(_knowControls.transform, 96f, 0f, 56f, 40f, loc.Get("ui.trade.max"), () => SetKnowledgeMax());
            _knowHint = UiKit.AddText(panel, ColRightX, 286f, ColRightW, 40f, loc.Get("ui.trade.nothing_to_teach"), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);

            // Right column, bottom: what I get from the partner with their ready state.
            _getTitle = UiKit.AddText(panel, ColRightX, 336f, ColRightW - 140f, 24f, string.Empty, 18, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            _getPillBg = UiKit.AddPanel(panel, ColRightX + ColRightW - 132f, 334f, 132f, 28f, UiKit.SlotIdle);
            _getPill = UiKit.AddText(panel, ColRightX + ColRightW - 132f, 334f, 132f, 28f, string.Empty, 14, UiKit.CyanDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            _getContent = UiKit.ScrollList(panel, ColRightX, 366f, ColRightW, 148f, 3f);
            _getEmpty = UiKit.AddText(panel, ColRightX + 12f, 374f, ColRightW - 24f, 40f, loc.Get("ui.trade.get_empty"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
            _theirKnow = UiKit.AddText(panel, ColRightX, 518f, ColRightW, 24f, string.Empty, 16, UiKit.Cyan, TextAnchor.MiddleLeft);

            // Footer: Confirm (turns green once I confirmed) · input hint · Cancel.
            _confirmBtn = UiKit.AddButton(panel, ColLeftX, DialogH - 76f, 320f, 48f, loc.Get("ui.action.confirm"), () => Game.Network.SendTradeConfirm());
            _confirmImg = _confirmBtn.GetComponent<Image>();
            _confirmLabel = _confirmBtn.GetComponentInChildren<Text>();
            _footHint = UiKit.AddText(panel, ColLeftX + 336f, DialogH - 76f, DialogW - ColLeftX - 336f - 232f, 48f, string.Empty, 14, UiKit.CyanDim, TextAnchor.MiddleCenter);
            UiKit.AddButton(panel, DialogW - 28f - 200f, DialogH - 76f, 200f, 48f, loc.Get("ui.action.cancel"), () => Game.Network.SendTradeCancel());
            UiNav.Enable(overlay); // pad: stick walks the −/+ rows, Confirm / Cancel (#1043)
            overlay.SetActive(false);
        }

        /// <summary>Re-renders the trade dialog from the authoritative offers. The row rebuild only runs when
        /// the server state, the inventory or the language actually changed; the input hint follows the device.</summary>
        private void RefreshTrade(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            var trade = Game.Trade;
            var personal = Game.Personal;
            if (ReferenceEquals(trade, _renderedTrade) && ReferenceEquals(personal, _renderedPersonal) && ReferenceEquals(loc, _renderedLoc))
            {
                RefreshFootHint(loc); // the active input device can change without any trade traffic
                return;
            }

            _renderedTrade = trade;
            _renderedPersonal = personal;
            _renderedLoc = loc;

            _tradeTitle.text = loc.Get("ui.trade.with").Replace("{0}", trade.Partner);
            _getTitle.text = loc.Get("ui.trade.you_get").Replace("{0}", trade.Partner);
            StylePill(_givePillBg, _givePill, trade.MyConfirmed, loc);
            StylePill(_getPillBg, _getPill, trade.TheirConfirmed, loc);

            // Confirm button reflects MY state: green "Confirmed — waiting for {partner}" once pressed, so a
            // silent reset (the server clears both confirmations whenever an offer changes) is visible too.
            _confirmImg.color = trade.MyConfirmed ? new Color(0.30f, 0.75f, 0.45f) : UiKit.PanelFill;
            _confirmLabel.text = trade.MyConfirmed
                ? loc.Get("ui.trade.confirmed_waiting").Replace("{0}", trade.Partner)
                : loc.Get("ui.action.confirm");

            // Knowledge offered each way. The control only shows when there is something to teach.
            bool canTeach = trade.MyKnowledgeMax > 0;
            _knowControls.SetActive(canTeach);
            _myKnow.gameObject.SetActive(canTeach);
            _knowHint.gameObject.SetActive(!canTeach);
            if (canTeach)
            {
                _myKnow.text = loc.Get("ui.trade.teach").Replace("{0}", trade.MyKnowledgeOffered.ToString()).Replace("{1}", trade.MyKnowledgeMax.ToString());
            }

            _theirKnow.text = trade.TheirKnowledgeOffered > 0
                ? loc.Get("ui.trade.taught").Replace("{0}", trade.Partner).Replace("{1}", trade.TheirKnowledgeOffered.ToString())
                : string.Empty;

            // Left: every owned item as an icon card with its offer count.
            var offered = new Dictionary<string, int>();
            foreach (var it in trade.MyOffer)
            {
                offered[it.Item] = it.Count;
            }

            int i = 0;
            foreach (var s in personal)
            {
                var row = InvRowAt(i++);
                row.Item = s.Item;
                int give = offered.TryGetValue(s.Item, out var o) ? o : 0;
                SetIcon(row.Icon, s.Item);
                row.Name.text = ItemName(loc, s.Item);
                row.Have.text = $"×{s.Count}";
                row.Offered.text = give > 0 ? give.ToString() : "–";
                row.Offered.color = give > 0 ? UiKit.Ok : UiKit.CyanDim;
                row.Back.color = give > 0 ? new Color(0.10f, 0.32f, 0.52f, 0.95f) : new Color(0.06f, 0.14f, 0.26f, 0.85f);
                row.Go.SetActive(true);
            }

            for (; i < _invRows.Count; i++)
            {
                _invRows[i].Go.SetActive(false);
            }

            _invEmpty.gameObject.SetActive(personal.Length == 0);

            // Right: my offer and theirs, read-only.
            FillOffer(_giveRows, _giveContent, trade.MyOffer, loc);
            FillOffer(_getRows, _getContent, trade.TheirOffer, loc);
            _giveEmpty.gameObject.SetActive(trade.MyOffer.Length == 0);
            _getEmpty.gameObject.SetActive(trade.TheirOffer.Length == 0);
            RefreshFootHint(loc);
        }

        private void RefreshFootHint(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            string key = InputMap.ActiveDevice switch
            {
                InputDeviceKind.KeyboardMouse => "ui.trade.hint_keys",
                InputDeviceKind.Gamepad => "ui.trade.hint_pad",
                _ => null, // touch: the two buttons are the whole story
            };
            _footHint.text = key != null ? loc.Get(key) : string.Empty;
        }

        private static void StylePill(Image bg, Text label, bool ready, BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            label.text = ready ? loc.Get("ui.trade.ready") : loc.Get("ui.trade.waiting");
            label.color = ready ? Color.white : UiKit.CyanDim;
            bg.color = ready ? new Color(0.16f, 0.55f, 0.32f, 0.95f) : UiKit.SlotIdle;
        }

        private void FillOffer(List<OfferRow> rows, RectTransform content, NetTradeItem[] offer, BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            int j = 0;
            foreach (var it in offer)
            {
                var row = OfferRowAt(rows, content, j++);
                SetIcon(row.Icon, it.Item);
                row.Name.text = ItemName(loc, it.Item);
                row.Count.text = $"×{it.Count}";
                row.Go.SetActive(true);
            }

            for (; j < rows.Count; j++)
            {
                rows[j].Go.SetActive(false);
            }
        }

        /// <summary>Points a pooled icon Image at the item's content art (or hides it when there is none —
        /// an Image without a sprite would draw a white square).</summary>
        private void SetIcon(Image icon, string item)
        {
            var sprite = IconResolver.Resolve(item, Game);
            icon.enabled = sprite != null;
            if (sprite != null)
            {
                icon.sprite = sprite;
                icon.color = IconResolver.Tint(item, Game);
            }
        }

        private InvRow InvRowAt(int index)
        {
            while (index >= _invRows.Count)
            {
                float w = ColLeftW - 8f; // list padding
                var go = new GameObject("Row", typeof(RectTransform));
                go.transform.SetParent(_invContent, false);
                go.GetComponent<RectTransform>().sizeDelta = new Vector2(w, InvRowH);
                var le = go.AddComponent<LayoutElement>();
                le.minHeight = le.preferredHeight = InvRowH;

                var row = new InvRow { Go = go };
                row.Back = UiKit.AddPanel(go.transform, 0f, 0f, w, InvRowH, new Color(0.06f, 0.14f, 0.26f, 0.85f));
                row.Icon = UiKit.AddImage(go.transform, 8f, 8f, 40f, 40f, null, Color.white);
                row.Icon.enabled = false;
                // Name + owned count to the left of the control; the control itself is three 44 px touch targets.
                float ctrlX = w - 140f;
                row.Name = UiKit.AddText(go.transform, 56f, 5f, ctrlX - 56f - 8f, 26f, string.Empty, 19, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                row.Have = UiKit.AddText(go.transform, 56f, 30f, ctrlX - 56f - 8f, 22f, string.Empty, 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
                UiKit.AddButton(go.transform, ctrlX, 6f, 44f, 44f, "−", () => Adjust(row.Item, -1));
                row.Offered = UiKit.AddText(go.transform, ctrlX + 44f, 6f, 48f, 44f, "–", 20, UiKit.CyanDim, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.AddButton(go.transform, ctrlX + 92f, 6f, 44f, 44f, "+", () => Adjust(row.Item, +1));
                _invRows.Add(row);
            }

            return _invRows[index];
        }

        private static OfferRow OfferRowAt(List<OfferRow> rows, RectTransform content, int index)
        {
            while (index >= rows.Count)
            {
                float w = ColRightW - 8f;
                var go = new GameObject("Row", typeof(RectTransform));
                go.transform.SetParent(content, false);
                go.GetComponent<RectTransform>().sizeDelta = new Vector2(w, OfferRowH);
                var le = go.AddComponent<LayoutElement>();
                le.minHeight = le.preferredHeight = OfferRowH;

                var row = new OfferRow { Go = go };
                row.Icon = UiKit.AddImage(go.transform, 6f, 6f, 28f, 28f, null, Color.white);
                row.Icon.enabled = false;
                row.Name = UiKit.AddText(go.transform, 44f, 0f, w - 44f - 80f, OfferRowH, string.Empty, 17, UiKit.TextCol);
                row.Count = UiKit.AddText(go.transform, w - 80f, 0f, 72f, OfferRowH, string.Empty, 17, UiKit.Ok, TextAnchor.MiddleRight, FontStyle.Bold);
                rows.Add(row);
            }

            return rows[index];
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

        // The shared display-name helper (#927), so a traded item reads as it does in the backpack: a preparation of the
        // bio lab names what it does, a seedling its species, a changed tool says that it was changed (#2201–#2206).
        private string ItemName(BlocksBeyondTheStars.Shared.Localization.Localizer loc, string itemKey)
            => BlocksBeyondTheStars.Shared.Localization.ItemNames.Display(loc, itemKey, null,
                _speciesName ??= seed => Game.Bio.SpeciesName(seed));

        private System.Func<uint, string> _speciesName;
    }
}
