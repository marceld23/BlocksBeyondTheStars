// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// In-game gameplay UI (M22), toggled with Tab: inventory + cargo, crafting, blueprint
    /// unlock (Tech) and ship-module build. A thin driver over the modern uGUI screen
    /// (<see cref="CraftingTechShipUI"/>): it owns open/close + the active tab and the character
    /// colour cycling; every action sends an authoritative intent the server validates. While
    /// open, the cursor is freed and the player controller pauses (via <c>GameBootstrap.MenuOpen</c>).
    /// </summary>
    public sealed class GameMenu : MonoBehaviour
    {
        public GameBootstrap Game;
        public ClientSettings Settings;   // for in-game character customization
        public PlayerAvatar Avatar;       // local avatar, recoloured live

        private enum Tab { Inventory, Crafting, Tech, Ship, Map, Missions, Character, Alliances, Story, Companions, Photos }

        /// <summary>Which full-screen browser sub-screen (if any) replaces the tab view while the menu is open.
        /// The Wiki ("Codex") and Arcade are reached from buttons in the menu header.</summary>
        private enum BrowserScreen { None, Wiki, Arcade }

        private Tab _tab = Tab.Inventory;
        private BrowserScreen _browser = BrowserScreen.None;
        private bool _open;
        private bool _wasInSpaceView;
        private bool _hyperjumpHooked;
        private bool _typingPrev; // text field focused last frame (an Esc that unfocuses it clears isFocused the same frame)

        private void Update()
        {
            PumpAppearanceQueue(); // paced appearance sends drain even after the editor is gone

            if (Game != null)
            {
                // Close the menu when a transition animation begins so the player can see it:
                // a hyperspace warp (subscribed once) and a launch/landing flight sequence (from a
                // planet or a station, which flips SpaceViewActive on).
                if (!_hyperjumpHooked)
                {
                    Game.HyperjumpStarted += CloseForTransition;
                    _hyperjumpHooked = true;
                }

                if (Game.SpaceViewActive && !_wasInSpaceView)
                {
                    CloseForTransition();
                }

                _wasInSpaceView = Game.SpaceViewActive;

                // Esc/Tab while typing in a menu text field (crafting search, alliance picker …) must only
                // leave the field, not close the whole menu (#413 N5). The InputField may process the same
                // Esc first and clear isFocused before we run, so remember the previous frame's focus too
                // (same trick AppShell uses for the chat input).
                bool typing = UiKit.TextFieldFocused();
                bool typingRecent = typing || _typingPrev;
                _typingPrev = typing;

                // Full-screen menu/browser panes must be escapable before the app shell sees Esc
                // as "leave game", otherwise the player can get trapped behind overlapping modals.
                if (_open && Input.GetKeyDown(KeyCode.Escape) && !Game.ChatTyping && !typingRecent)
                {
                    Game.MarkMenuInputHandled();
                    SetOpen(false);
                    return;
                }

                // Don't let Tab open the menu while the death / ship-destruction prompt is up — only its
                // "Weiter" button proceeds. Also not while another modal owns the input (trade, beacon
                // naming, dock request …): stacking the crafting menu on top of those was the root of a
                // family of cursor-fights (#413) — the modal's own Esc/Tab handling closes it instead.
                // Tab (keyboard) or Start (gamepad button 7) toggles the menu.
                if ((Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.JoystickButton7))
                    && !Game.AwaitingRespawnConfirm && !Game.ChatTyping && !typingRecent
                    && !Game.MenuInputHandledThisFrame && (_open || !Game.MenuOpen))
                {
                    Game.MarkMenuInputHandled();
                    SetOpen(!_open);
                    return;
                }
            }

            // Drive the uGUI screen (CraftingTechShipUI renders every tab; Wiki/Arcade are separate screens).
            if (!_open || Game?.Localizer == null || Game.Content == null)
            {
                _ui?.Hide();
                _wikiUi?.Hide();
                _arcadeUi?.Hide();
                return;
            }

            if (_browser == BrowserScreen.Wiki)
            {
                _ui?.Hide();
                _arcadeUi?.Hide();
                EnsureWikiUi();
                _wikiUi.Show();
                return;
            }

            if (_browser == BrowserScreen.Arcade)
            {
                _ui?.Hide();
                _wikiUi?.Hide();
                EnsureArcadeUi();
                _arcadeUi.Show();
                return;
            }

            _wikiUi?.Hide();
            _arcadeUi?.Hide();
            EnsureUi();
            _ui.ShowMode((CraftingTechShipUI.Mode)_tab);
        }

        // Public entry points used by station interactions (cockpit → map, etc.).
        public void OpenInventory() => OpenAt(Tab.Inventory);
        public void OpenCrafting() => OpenAt(Tab.Crafting);
        public void OpenMap() => OpenAt(Tab.Map);
        public void OpenTech() => OpenAt(Tab.Tech);
        public void OpenShip() => OpenAt(Tab.Ship);
        public void OpenMissions() => OpenAt(Tab.Missions);

        /// <summary>Automation/capture hook (<see cref="ScreenshotDirector"/>): open/close the in-game menu exactly
        /// as the Tab key does (at the current tab), so a marketing shot can show the Tab menu over the cockpit.</summary>
        public void SetMenuOpen(bool open) => SetOpen(open);

        /// <summary>Opens the in-game Wiki ("Codex") screen — an always-available menu point.</summary>
        public void OpenWiki() { _browser = BrowserScreen.Wiki; SetOpen(true); }

        /// <summary>Opens the Arcade collection screen — an always-available menu point.</summary>
        public void OpenArcade() { Game?.MarkArcadeSeen(); _browser = BrowserScreen.Arcade; SetOpen(true); }

        /// <summary>Returns from a browser sub-screen (Wiki/Arcade) to the normal menu tabs.</summary>
        public void CloseBrowser() => _browser = BrowserScreen.None;

        /// <summary>Opens the dedicated vendor trade (barter) screen — a focused "give X → get Y" view, not the
        /// full crafting menu (B22). The two are mutually exclusive (see <see cref="SetOpen"/>).</summary>
        public void OpenMarket()
        {
            EnsureVendorUi();
            _vendorUi.Open();
        }

        private VendorTradeUI _vendorUi;

        private void EnsureVendorUi()
        {
            if (_vendorUi != null)
            {
                return;
            }

            var go = new GameObject("VendorTradeUI");
            go.transform.SetParent(transform, false);
            _vendorUi = go.AddComponent<VendorTradeUI>();
            _vendorUi.Game = Game;
            _vendorUi.Menu = this;
        }

        private void OpenAt(Tab tab)
        {
            SwitchTo(tab);
            SetOpen(true);
        }

        private void SetOpen(bool open)
        {
            if (open)
            {
                _vendorUi?.Close(); // the vendor trade screen + the crafting menu are mutually exclusive
            }

            _open = open;
            Game.SetMenuOwner(this, _open); // the cursor arbiter frees/locks from the owner set (#413)
            if (_open)
            {
                UiNav.Enable(gameObject); // gamepad can drive the menu (covers every tab; inert on KB/mouse)
                SwitchTo(_tab); // refresh data for the current tab
            }
            else
            {
                Game.MenuTabKey = null; // the music director's "crafting/tech tab open" signal (#1174)
                _browser = BrowserScreen.None;
                CloseFaceEditor(); // the modal face editor is owned by the menu — don't let it linger after close
                _ui?.OnMenuClosed(); // #1072: a located station gets a through-wall marker once the menu is gone
                _ui?.Hide();
                _wikiUi?.Hide();
                _arcadeUi?.Hide();
            }
        }

        /// <summary>Switches tab and (re)requests server data for data-driven tabs.</summary>
        private void SwitchTo(Tab tab)
        {
            CloseFaceEditor(); // navigating to any tab dismisses the (Character-tab) face editor overlay
            _tab = tab;
            Game.MenuTabKey = tab.ToString().ToLowerInvariant(); // music director: crafting / tech beds (#1174)
            if (tab == Tab.Map)
            {
                Game.Network?.SendRequestStarMap();
            }
            else if (tab == Tab.Missions)
            {
                Game.Network?.SendRequestMissions();
            }
            else if (tab == Tab.Alliances)
            {
                Game.Network?.SendRequestAllianceList(); // refresh the roster (allies + pending) on open
                Game.Network?.SendRequestStarMap();      // the "find players" picker needs the online-player list
            }
            else if (tab == Tab.Story)
            {
                Game?.MarkStorySeen(); // opening the Story tab clears its "new content" badge
            }
            else if (tab == Tab.Companions)
            {
                Game.Network?.SendRequestCompanions(); // refresh the companion roster on open
                Game?.MarkCompanionsSeen();            // clear the "new companion" badge
            }
        }

        private CraftingTechShipUI _ui;
        private WikiUI _wikiUi;
        private ArcadeUI _arcadeUi;

        private void EnsureWikiUi()
        {
            if (_wikiUi != null) return;
            var go = new GameObject("WikiUI");
            go.transform.SetParent(transform, false);
            _wikiUi = go.AddComponent<WikiUI>();
            _wikiUi.Game = Game;
            _wikiUi.Menu = this;
        }

        private void EnsureArcadeUi()
        {
            if (_arcadeUi != null) return;
            var go = new GameObject("ArcadeUI");
            go.transform.SetParent(transform, false);
            _arcadeUi = go.AddComponent<ArcadeUI>();
            _arcadeUi.Game = Game;
            _arcadeUi.Menu = this;
            _arcadeUi.Settings = Settings;
        }

        private void EnsureUi()
        {
            if (_ui != null)
            {
                return;
            }

            var go = new GameObject("CraftTechShipUI");
            go.transform.SetParent(transform, false);
            _ui = go.AddComponent<CraftingTechShipUI>();
            _ui.Game = Game;
            _ui.Menu = this;
        }

        /// <summary>Switches the active tab from the uGUI screen (Crafting/Tech/Ship bar). Also leaves any open
        /// browser sub-screen so a tab click always returns to the tab view.</summary>
        public void SwitchFromUi(int tab) { _browser = BrowserScreen.None; SwitchTo((Tab)tab); }

        /// <summary>Closes the whole menu from the uGUI screen's X button.</summary>
        public void CloseFromUi() => SetOpen(false);

        /// <summary>Closes the menu (if open) when a launch/landing/hyperjump animation starts.</summary>
        private void CloseForTransition()
        {
            _vendorUi?.Close();
            if (_open)
            {
                SetOpen(false);
            }
        }

        private void OnDestroy()
        {
            if (Game != null && _hyperjumpHooked)
            {
                Game.HyperjumpStarted -= CloseForTransition;
            }
        }

        // --- Character appearance (driven by the uGUI Character tab) ---

        /// <summary>Applies the edited colours to the local avatar, persists them, and tells the server.</summary>
        private void ApplyAppearance()
        {
            Avatar?.ApplyColors(Settings);
            if (Game != null)
            {
                Game.HullRgb = Rgb(Settings.HullColor); // keep the flight view's hull tint in sync (item 32)
            }

            Settings.Save();
            Game.Network?.SendAppearance(Rgb(Settings.SkinColor), Rgb(Settings.TorsoColor),
                Rgb(Settings.ArmColor), Rgb(Settings.LegColor), Rgb(Settings.HullColor));
        }

        /// <summary>Cycles the ship hull colour — called from the uGUI Ship tab's paint category (item 32).</summary>
        public void CycleHull()
        {
            if (Settings == null)
            {
                return;
            }

            Settings.HullColor = AppearancePalette.Next(Settings.HullColor);
            ApplyAppearance();
        }

        /// <summary>Cycles a body colour (0=skin 1=torso 2=arms 3=legs) — the keyboard/controller path that
        /// predates the appearance screen's swatch grid.</summary>
        public void CycleAppearance(int which) => SetAppearanceColor(which, AppearancePalette.Next(GetAppearanceColor(which)));

        /// <summary>The current colour of a body part (0=skin 1=torso 2=arms 3=legs).</summary>
        public Color GetAppearanceColor(int which) => which switch
        {
            0 => Settings?.SkinColor ?? Color.gray,
            1 => Settings?.TorsoColor ?? Color.gray,
            2 => Settings?.ArmColor ?? Color.gray,
            _ => Settings?.LegColor ?? Color.gray,
        };

        /// <summary>Sets a body part's base colour — any RGB, not just palette entries: the colours travel as
        /// plain RGB, so the appearance screen's colour wheel is free to hand over whatever it likes.</summary>
        public void SetAppearanceColor(int which, Color color)
        {
            if (Settings == null)
            {
                return;
            }

            switch (which)
            {
                case 0: Settings.SkinColor = color; break;
                case 1: Settings.TorsoColor = color; break;
                case 2: Settings.ArmColor = color; break;
                default: Settings.LegColor = color; break;
            }

            ApplyAppearance();
        }

        private FaceEditor _faceEditor;

        /// <summary>
        /// Opens the appearance screen (#899): face, torso, arms, legs and helmet as tabs of ONE editor, each
        /// with its base colour beside the canvas it tints. It replaces nine separate Character-tab cards —
        /// four "cycle this colour" rows and five "open that editor" rows — which split apart two halves of
        /// the same decision and made choosing a skin tone a matter of clicking an arrow ten times.
        /// No-op if already open.
        /// </summary>
        public void OpenAppearanceEditor()
        {
            if (_faceEditor != null || Settings == null)
            {
                return;
            }

            var go = new GameObject("AppearanceEditor");
            go.transform.SetParent(transform, false);
            _faceEditor = go.AddComponent<FaceEditor>();
            _faceEditor.Localizer = key => Game?.Localizer?.Get(key) ?? key;
            _faceEditor.Subjects = AppearanceSubjects.Build(
                () => Settings.FacePixels, ApplyFace,
                part => Settings.GetBodyPaint(part), ApplyBodyPaint,
                GetAppearanceColor, SetAppearanceColor);
            _faceEditor.PreviewState = () => AppearanceSubjects.Snapshot(
                GetAppearanceColor, () => Settings.FacePixels, part => Settings.GetBodyPaint(part));
        }

        /// <summary>Kept for the older entry points (and any host that only wants the face): the appearance
        /// screen opens on its face tab.</summary>
        public void OpenFaceEditor() => OpenAppearanceEditor();

        /// <summary>Tears down the pixel-face editor overlay if it is open. The editor builds its own canvas, so
        /// it must be destroyed when the menu closes or the player navigates away — otherwise it stays painted
        /// over the screen and can't be dismissed.</summary>
        private void CloseFaceEditor()
        {
            if (_faceEditor != null)
            {
                Destroy(_faceEditor.gameObject);
                _faceEditor = null;
            }
        }

        /// <summary>Opens the appearance screen on a body-paint part (#874 entry point, kept for callers that
        /// name a part). Parts are tabs of one screen since #899.</summary>
        public void OpenBodyPaintEditor(int part) => OpenAppearanceEditor();

        /// <summary>Applies an edited body painting: persists it locally, shows it on the local figure, and
        /// tells the server (which persists + relays). The face's sibling path (#874).</summary>
        public void ApplyBodyPaint(int part, string pixels)
        {
            if (Settings == null)
            {
                return;
            }

            Settings.SetBodyPaint(part, pixels ?? string.Empty);
            Settings.Save();
            Avatar?.SetBodyPaint(part, Settings.GetBodyPaint(part));
            if (Game != null)
            {
                Game.BodyPaintPixels[part] = Settings.GetBodyPaint(part);
                QueueAppearanceSend(part, Settings.GetBodyPaint(part));
            }
        }

        /// <summary>Applies an edited pixel face: persists it locally, shows it on the local figure, and tells
        /// the server (which persists + relays it to other players). Called by the <see cref="FaceEditor"/>.</summary>
        public void ApplyFace(string pixels)
        {
            if (Settings == null)
            {
                return;
            }

            Settings.FacePixels = pixels ?? string.Empty;
            Settings.Save();
            Avatar?.SetFace(Settings.FacePixels);
            if (Game != null)
            {
                Game.FacePixels = Settings.FacePixels;
                QueueAppearanceSend(-1, Settings.FacePixels);
            }
        }

        // ── appearance send queue ────────────────────────────────────────────────────────────────
        //
        // The server accepts ONE appearance edit (face or any body painting) every 2 s per player and drops
        // the rest silently — sensible anti-spam when each card was its own screen, but the appearance screen
        // commits a part every time the player switches tab, so painting a torso and an arm in the same
        // half-minute used to mean the arm never reached anyone else. The pending payload per part is kept
        // (newest wins) and sent as the window opens; everything else — local figure, settings, preview — has
        // already been updated, so this only paces what goes on the wire.
        private const double AppearanceSendInterval = 2.1; // the server's 2 s + a margin for clock drift
        private readonly Dictionary<int, string> _pendingAppearance = new(); // -1 = face, 0..3 = body part
        private double _nextAppearanceSend;

        private void QueueAppearanceSend(int slot, string pixels)
        {
            _pendingAppearance[slot] = pixels ?? string.Empty;
            PumpAppearanceQueue();
        }

        /// <summary>Sends the next queued appearance payload if the rate-limit window is open. Called from the
        /// menu's Update, so a queue left behind by a closed editor still drains.</summary>
        private void PumpAppearanceQueue()
        {
            if (_pendingAppearance.Count == 0 || Game?.Network == null || Time.unscaledTimeAsDouble < _nextAppearanceSend)
            {
                return;
            }

            int slot = int.MaxValue;
            foreach (int key in _pendingAppearance.Keys)
            {
                slot = Mathf.Min(slot, key); // face (-1) first, then parts in order — a stable, boring order
            }

            string pixels = _pendingAppearance[slot];
            _pendingAppearance.Remove(slot);
            _nextAppearanceSend = Time.unscaledTimeAsDouble + AppearanceSendInterval;
            if (slot < 0)
            {
                Game.Network.SendFace(pixels);
            }
            else
            {
                Game.Network.SendBodyPaint(slot, pixels);
            }
        }

        private static int Rgb(Color c)
            => (Mathf.RoundToInt(c.r * 255f) << 16) | (Mathf.RoundToInt(c.g * 255f) << 8) | Mathf.RoundToInt(c.b * 255f);
    }
}
