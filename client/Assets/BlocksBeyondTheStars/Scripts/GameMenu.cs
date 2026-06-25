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

        private void Update()
        {
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

                // Don't let Tab open the menu while the death / ship-destruction prompt is up — only its
                // "Weiter" button proceeds.
                if (Input.GetKeyDown(KeyCode.Tab) && !Game.AwaitingRespawnConfirm && !Game.ChatTyping)
                {
                    SetOpen(!_open);
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
            Game.MenuOpen = _open;
            Cursor.lockState = _open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = _open;
            if (_open)
            {
                SwitchTo(_tab); // refresh data for the current tab
            }
            else
            {
                _browser = BrowserScreen.None;
                CloseFaceEditor(); // the modal face editor is owned by the menu — don't let it linger after close
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

        private static readonly Color[] Palette =
        {
            new Color(0.85f, 0.68f, 0.55f), new Color(0.55f, 0.40f, 0.28f), new Color(0.90f, 0.85f, 0.80f),
            new Color(0.80f, 0.20f, 0.20f), new Color(0.20f, 0.45f, 0.80f), new Color(0.20f, 0.65f, 0.35f),
            new Color(0.90f, 0.75f, 0.20f), new Color(0.55f, 0.30f, 0.70f), new Color(0.25f, 0.25f, 0.32f),
            new Color(0.92f, 0.92f, 0.95f),
        };

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

            Settings.HullColor = NextColor(Settings.HullColor);
            ApplyAppearance();
        }

        /// <summary>Cycles a body colour (0=skin 1=torso 2=arms 3=legs) — called from the uGUI Character tab.</summary>
        public void CycleAppearance(int which)
        {
            if (Settings == null)
            {
                return;
            }

            switch (which)
            {
                case 0: Settings.SkinColor = NextColor(Settings.SkinColor); break;
                case 1: Settings.TorsoColor = NextColor(Settings.TorsoColor); break;
                case 2: Settings.ArmColor = NextColor(Settings.ArmColor); break;
                default: Settings.LegColor = NextColor(Settings.LegColor); break;
            }

            ApplyAppearance();
        }

        private FaceEditor _faceEditor;

        /// <summary>Opens the in-game pixel-face editor (from the Character tab). No-op if already open.</summary>
        public void OpenFaceEditor()
        {
            if (_faceEditor != null)
            {
                return;
            }

            var go = new GameObject("FaceEditor");
            go.transform.SetParent(transform, false);
            _faceEditor = go.AddComponent<FaceEditor>();
            _faceEditor.InitialFace = Settings?.FacePixels;
            _faceEditor.Localizer = key => Game?.Localizer?.Get(key) ?? key;
            _faceEditor.OnApply = ApplyFace; // persists locally, updates the in-world avatar + tells the server
        }

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
                Game.Network?.SendFace(Settings.FacePixels);
            }
        }

        private static int Rgb(Color c)
            => (Mathf.RoundToInt(c.r * 255f) << 16) | (Mathf.RoundToInt(c.g * 255f) << 8) | Mathf.RoundToInt(c.b * 255f);

        private static Color NextColor(Color current)
        {
            int idx = -1;
            for (int i = 0; i < Palette.Length; i++)
            {
                if (Mathf.Approximately(Palette[i].r, current.r) &&
                    Mathf.Approximately(Palette[i].g, current.g) &&
                    Mathf.Approximately(Palette[i].b, current.b))
                {
                    idx = i;
                    break;
                }
            }

            return Palette[(idx + 1) % Palette.Length];
        }

    }
}
