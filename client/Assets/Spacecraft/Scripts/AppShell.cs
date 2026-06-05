using System.IO;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Localization;
using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>The shell phases: splash, main menu, settings, credits, loading, in-game.</summary>
    public enum ShellPhase { Splash, MainMenu, Settings, Credits, Loading, InGame, ShipEditor, AvatarEditor, StructureEditor, ContentEditor, MaterialEditor, Editors, SaveSelect }

    /// <summary>
    /// Client front-end state machine (M20 / `anf_textures.md`): drives splash → main menu →
    /// settings → loading → in-game, owns the local <see cref="ClientSettings"/> and the
    /// bilingual <see cref="Localizer"/>, and hands off to <see cref="GameBootstrap"/> to start
    /// playing. Presentation only — the .NET server stays authoritative.
    ///
    /// Scaffold note: IMGUI (matching the existing HUD); real uGUI/UI-Toolkit art comes later.
    /// Attach this single component to a GameObject in the launcher scene.
    /// </summary>
    public sealed class AppShell : MonoBehaviour
    {
        public const string Version = "0.20.0-dev";

        public ShellPhase Phase { get; private set; } = ShellPhase.Splash;
        public ClientSettings Settings { get; private set; }
        public GameContent Content { get; private set; }
        public Localizer Localizer { get; private set; }

        // Join target edited on the main menu.
        public string Host = "127.0.0.1";
        public string Port = "31415";
        public string PlayerName = "Pilot";

        private SplashScreen _splash;
        private LoadingScreen _loading;

        private readonly LocalServerLauncher _localServer = new LocalServerLauncher();
        private bool _hostLocal;
        private GameObject _gameRoot;

        private bool _splashSoundDone;

        private void Awake()
        {
            Settings = ClientSettings.Load();
            Settings.Apply();
            LoadLocalizer();

            // The 3D renders at native resolution (crisp on 4K); the IMGUI UI keeps a readable
            // physical size via UiScale (virtual 1080p layout) instead of a blunt resolution cap.
            _splash = new SplashScreen(this);
            _loading = new LoadingScreen(this);

            EnsureMenuBackground();
        }

        private GameObject _menuBackground;

        /// <summary>Spawns the animated space-scene backdrop shown behind the shell screens.</summary>
        private void EnsureMenuBackground()
        {
            if (_menuBackground == null)
            {
                _menuBackground = new GameObject("MenuBackground");
                _menuBackground.AddComponent<MenuBackground>();
            }
        }

        private void DestroyMenuBackground()
        {
            if (_menuBackground != null)
            {
                UnityEngine.Object.Destroy(_menuBackground);
                _menuBackground = null;
            }
        }

        /// <summary>Plays the bombastic intro sting over the splash screen (ensures a listener exists).</summary>
        private void PlaySplashSound()
        {
            var clip = Resources.Load<AudioClip>("audio/splash_intro");
            if (clip == null)
            {
                return;
            }

            if (FindFirstObjectByType<AudioListener>() == null)
            {
                gameObject.AddComponent<AudioListener>();
            }

            var src = gameObject.AddComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.volume = Mathf.Clamp01(Settings?.MasterVolume ?? 0.8f);
            src.PlayOneShot(clip);
        }

        /// <summary>(Re)loads content and the localizer for the currently selected language.</summary>
        public void LoadLocalizer()
        {
            string dataDir = Path.Combine(Application.streamingAssetsPath, "data");
            Content = ContentLoader.LoadFromDirectory(dataDir);
            var locale = Settings.Language == "de" ? GameLocale.German : GameLocale.English;
            Localizer = Content.CreateLocalizer(locale);
        }

        /// <summary>Localize, falling back to the key before content is loaded.</summary>
        public string L(string key) => Localizer != null ? Localizer.Get(key) : key;

        public void GoTo(ShellPhase phase) => Phase = phase;

        public void OpenSettings() => Phase = ShellPhase.Settings;

        public void CloseSettings()
        {
            Settings.Save();
            Settings.Apply();
            LoadLocalizer(); // language may have changed
            Phase = ShellPhase.MainMenu;
        }

        /// <summary>Opens the singleplayer world picker (choose an existing save or start a new one).</summary>
        public void StartSingleplayer() => Phase = ShellPhase.SaveSelect;

        /// <summary>Launches singleplayer on a specific world (creates it if new); seed 0 = derive from name.</summary>
        public void StartSingleplayerWorld(string worldName, long seed = 0)
        {
            // Singleplayer hosts the bundled dedicated server as a child process bound to
            // loopback (Option A), then connects to it like any other server.
            _hostLocal = true;
            Settings.LastWorld = worldName;
            Settings.Save();
            if (_localServer.Start(LocalServerLauncher.DefaultPort, Settings.ViewDistanceChunks, worldName, seed))
            {
                Host = _localServer.Host;
                Port = _localServer.Port.ToString();
                _loading.MinShow = 2.5f; // give the server time to start listening
            }
            else
            {
                // No bundled server (not published yet): fall back to a manually started one.
                Host = "127.0.0.1";
            }

            Phase = ShellPhase.Loading;
        }

        public void StartJoin()
        {
            _hostLocal = false;
            _loading.MinShow = 0.6f;
            Phase = ShellPhase.Loading;
        }

        public void Quit()
        {
            StopLocalServer();
            Application.Quit();
        }

        private void StopLocalServer()
        {
            if (_hostLocal)
            {
                _localServer.Stop();
                _hostLocal = false;
            }
        }

        private void OnApplicationQuit() => _localServer.Stop();

        private void OnDestroy() => _localServer.Stop();

        /// <summary>Builds the in-game rig (player + camera + world + HUD) and enters play.</summary>
        public void LaunchGame()
        {
            DestroyMenuBackground();
            _gameRoot = WorldRig.Build(this);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Phase = ShellPhase.InGame;
        }

        /// <summary>Tears down the in-game world, stops the local server, and returns to the menu.</summary>
        public void ReturnToMenu()
        {
            if (_gameRoot != null)
            {
                UnityEngine.Object.Destroy(_gameRoot); // GameBootstrap.OnDestroy disconnects
                _gameRoot = null;
            }

            StopLocalServer();
            EnsureMenuBackground();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _confirmQuit = false;
            if (_quitDialog != null)
            {
                UnityEngine.Object.Destroy(_quitDialog);
                _quitDialog = null;
            }

            Phase = ShellPhase.MainMenu; // leaving the game returns to the main menu
        }

        private bool _confirmQuit; // showing the "quit to menu?" confirmation over the game
        private bool _chatTypingPrev; // chat focus last frame (so closing chat with Esc doesn't pop quit)
        private GameObject _quitDialog;

        private GameBootstrap Boot() => _gameRoot != null ? _gameRoot.GetComponentInChildren<GameBootstrap>() : null;

        private void CancelQuit()
        {
            _confirmQuit = false;
            ShowQuitDialog(false);
            var boot = Boot();
            if (boot != null)
            {
                boot.MenuOpen = false; // hands control back to the player (re-locks the cursor)
            }
        }

        private GameObject _uiMenu;
        private GameObject _uiLoading;
        private GameObject _uiSettings;
        private GameObject _uiCredits;
        private GameObject _uiEditors;
        private GameObject _uiSaveSelect;
        private GameObject _editorRoot;

        /// <summary>Opens the standalone ship-type editor (build a ship design + save it).</summary>
        public void OpenShipEditor()
        {
            DestroyMenuBackground();
            _editorRoot = new GameObject("ShipEditor");
            _editorRoot.AddComponent<ShipEditor>().Shell = this;
            Phase = ShellPhase.ShipEditor;
        }

        /// <summary>Closes the ship editor and returns to the main menu.</summary>
        public void CloseShipEditor()
        {
            if (_editorRoot != null)
            {
                Destroy(_editorRoot);
                _editorRoot = null;
            }

            EnsureMenuBackground();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Phase = ShellPhase.Editors; // back to the editors submenu
        }

        /// <summary>Opens the avatar skin designer (edit per-part colours + export a skin).</summary>
        public void OpenAvatarEditor()
        {
            DestroyMenuBackground();
            _editorRoot = new GameObject("AvatarEditor");
            _editorRoot.AddComponent<AvatarEditor>().Shell = this;
            Phase = ShellPhase.AvatarEditor;
        }

        /// <summary>Closes the avatar editor and returns to the main menu.</summary>
        public void CloseAvatarEditor()
        {
            if (_editorRoot != null)
            {
                Destroy(_editorRoot);
                _editorRoot = null;
            }

            EnsureMenuBackground();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Phase = ShellPhase.Editors; // back to the editors submenu
        }

        /// <summary>Opens the station / settlement structure editor (build a template + save it).</summary>
        public void OpenStructureEditor(StructureEditor.Mode mode)
        {
            DestroyMenuBackground();
            _editorRoot = new GameObject("StructureEditor");
            var ed = _editorRoot.AddComponent<StructureEditor>();
            ed.Shell = this;
            ed.EditorMode = mode;
            Phase = ShellPhase.StructureEditor;
        }

        public void OpenStationEditor() => OpenStructureEditor(StructureEditor.Mode.Station);
        public void OpenSettlementEditor() => OpenStructureEditor(StructureEditor.Mode.Settlement);

        /// <summary>Opens the item + recipe designer.</summary>
        public void OpenContentEditor()
        {
            DestroyMenuBackground();
            _editorRoot = new GameObject("ContentEditor");
            _editorRoot.AddComponent<ContentEditor>().Shell = this;
            Phase = ShellPhase.ContentEditor;
        }

        /// <summary>Closes the item + recipe designer and returns to the editors submenu.</summary>
        public void CloseContentEditor()
        {
            if (_editorRoot != null)
            {
                Destroy(_editorRoot);
                _editorRoot = null;
            }

            EnsureMenuBackground();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Phase = ShellPhase.Editors;
        }

        /// <summary>Opens the material designer (paint/load a texture, set frequency + world type, mechanics).</summary>
        public void OpenMaterialEditor()
        {
            DestroyMenuBackground();
            _editorRoot = new GameObject("MaterialEditor");
            _editorRoot.AddComponent<MaterialEditor>().Shell = this;
            Phase = ShellPhase.MaterialEditor;
        }

        /// <summary>Closes the material designer and returns to the editors submenu.</summary>
        public void CloseMaterialEditor()
        {
            if (_editorRoot != null)
            {
                Destroy(_editorRoot);
                _editorRoot = null;
            }

            EnsureMenuBackground();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Phase = ShellPhase.Editors;
        }

        /// <summary>Closes the structure editor and returns to the main menu.</summary>
        public void CloseStructureEditor()
        {
            if (_editorRoot != null)
            {
                Destroy(_editorRoot);
                _editorRoot = null;
            }

            EnsureMenuBackground();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Phase = ShellPhase.Editors; // back to the editors submenu
        }

        /// <summary>Time-based loading progress (0..1) for the uGUI loading bar.</summary>
        public float LoadingProgress => _loading.Progress;

        private void Update()
        {
            _splash.Update();
            _loading.Update();

            // Keep the (procedural) UI click/hover volume in step with the audio settings.
            if (Settings != null)
            {
                UiSound.Volume = Mathf.Clamp01(Settings.MasterVolume * Settings.SfxVolume) * 0.6f;
            }

            // The main menu + loading are uGUI (M27): spawn each for its phase, tear it down otherwise.
            if (Phase == ShellPhase.MainMenu && _uiMenu == null)
            {
                _uiMenu = UiMainMenu.Build(this);

                // Land the bombastic intro sting on the first menu reveal (logo + full UI), rather
                // than during the mandatory black Unity engine splash that precedes it.
                if (!_splashSoundDone)
                {
                    _splashSoundDone = true;
                    PlaySplashSound();
                }
            }
            else if (Phase != ShellPhase.MainMenu && _uiMenu != null)
            {
                Destroy(_uiMenu);
                _uiMenu = null;
            }

            if (Phase == ShellPhase.Loading && _uiLoading == null)
            {
                _uiLoading = UiLoading.Build(this);
            }
            else if (Phase != ShellPhase.Loading && _uiLoading != null)
            {
                Destroy(_uiLoading);
                _uiLoading = null;
            }

            // Settings + credits are uGUI now too (the whole shell is one design).
            if (Phase == ShellPhase.Settings && _uiSettings == null)
            {
                _uiSettings = UiSettings.Build(this);
            }
            else if (Phase != ShellPhase.Settings && _uiSettings != null)
            {
                Destroy(_uiSettings);
                _uiSettings = null;
            }

            if (Phase == ShellPhase.Credits && _uiCredits == null)
            {
                _uiCredits = UiCredits.Build(this);
            }
            else if (Phase != ShellPhase.Credits && _uiCredits != null)
            {
                Destroy(_uiCredits);
                _uiCredits = null;
            }

            if (Phase == ShellPhase.Editors && _uiEditors == null)
            {
                _uiEditors = UiEditors.Build(this);
            }
            else if (Phase != ShellPhase.Editors && _uiEditors != null)
            {
                Destroy(_uiEditors);
                _uiEditors = null;
            }

            if (Phase == ShellPhase.SaveSelect && _uiSaveSelect == null)
            {
                _uiSaveSelect = UiSaveSelect.Build(this);
            }
            else if (Phase != ShellPhase.SaveSelect && _uiSaveSelect != null)
            {
                Destroy(_uiSaveSelect);
                _uiSaveSelect = null;
            }

            // Track chat focus across frames: an Esc that closes the chat clears ChatTyping in the SAME
            // frame (the InputField's end-edit), so by the time we read it here it may already be false.
            // Remembering the previous frame's state keeps that Esc from also popping the quit dialog.
            var igBoot = Phase == ShellPhase.InGame && _gameRoot != null ? _gameRoot.GetComponentInChildren<GameBootstrap>() : null;
            bool chatActive = (igBoot != null && igBoot.ChatTyping) || _chatTypingPrev;
            _chatTypingPrev = igBoot != null && igBoot.ChatTyping;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (Phase == ShellPhase.InGame)
                {
                    var boot = igBoot;
                    if (chatActive)
                    {
                        // The chat handled its own Esc (or just closed) — don't quit to the menu.
                    }
                    else if (_confirmQuit)
                    {
                        CancelQuit(); // Esc again dismisses the confirmation
                    }
                    else
                    {
                        // Ask before leaving the game (rather than quitting instantly).
                        _confirmQuit = true;
                        ShowQuitDialog(true);
                        if (boot != null)
                        {
                            boot.MenuOpen = true; // freezes player control + frees the cursor for the buttons
                        }

                        Cursor.lockState = CursorLockMode.None;
                        Cursor.visible = true;
                    }
                }
                else if (Phase == ShellPhase.Settings)
                {
                    CloseSettings();
                }
                else if (Phase == ShellPhase.Credits)
                {
                    Phase = ShellPhase.MainMenu;
                }
                else if (Phase == ShellPhase.Editors)
                {
                    Phase = ShellPhase.MainMenu;
                }
                else if (Phase == ShellPhase.SaveSelect)
                {
                    Phase = ShellPhase.MainMenu;
                }
                else if (Phase == ShellPhase.ShipEditor)
                {
                    CloseShipEditor();
                }
                else if (Phase == ShellPhase.AvatarEditor)
                {
                    CloseAvatarEditor();
                }
                else if (Phase == ShellPhase.StructureEditor)
                {
                    CloseStructureEditor();
                }
                else if (Phase == ShellPhase.ContentEditor)
                {
                    CloseContentEditor();
                }
                else if (Phase == ShellPhase.MaterialEditor)
                {
                    CloseMaterialEditor();
                }
            }
        }

        /// <summary>Fills the whole screen with an opaque background so menu screens never bleed through.</summary>
        public void DrawBackground()
        {
            // Semi-transparent so the animated space-scene backdrop shows through behind the menu.
            var prev = GUI.color;
            GUI.color = new Color(0.03f, 0.06f, 0.13f, 0.45f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        /// <summary>Returns from the credits screen to the main menu.</summary>
        public void CloseCredits() => Phase = ShellPhase.MainMenu;

        /// <summary>Builds the "leave the game?" confirmation as a uGUI overlay (consistent with the rest
        /// of the menus, instead of an IMGUI box) and shows/hides it with the confirmation state.</summary>
        private void ShowQuitDialog(bool show)
        {
            if (show && _quitDialog == null)
            {
                BuildQuitDialog();
            }

            if (_quitDialog != null)
            {
                _quitDialog.SetActive(show);
            }
        }

        private void BuildQuitDialog()
        {
            bool de = Settings != null && Settings.Language == "de";
            var canvas = UiKit.CreateCanvas("Quit Confirm");
            canvas.sortingOrder = 60; // above the in-game HUD/menu
            _quitDialog = canvas.gameObject;

            var bg = UiKit.AddImage(canvas.transform, 0, 0, 1920, 1080, UiKit.SolidSprite, new Color(0f, 0f, 0f, 0.6f));
            bg.raycastTarget = true; // swallow clicks behind the dialog

            var panel = UiKit.AddPanel(canvas.transform, 720f, 430f, 480f, 220f, UiKit.Panel);
            UiKit.AddText(panel.transform, 24f, 26f, 432f, 72f,
                de ? "Spiel verlassen und zurück zum Hauptmenü?" : "Leave the game and return to the main menu?",
                22, UiKit.TextCol, TextAnchor.MiddleCenter);
            UiKit.AddButton(panel.transform, 30f, 132f, 200f, 58f, de ? "Ja, verlassen" : "Yes, leave", ReturnToMenu);
            UiKit.AddButton(panel.transform, 250f, 132f, 200f, 58f, de ? "Nein, weiter" : "No, keep playing", CancelQuit);
        }
    }
}
