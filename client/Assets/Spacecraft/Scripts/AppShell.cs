using System.IO;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Localization;
using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>The shell phases: splash, main menu, settings, credits, loading, in-game.</summary>
    public enum ShellPhase { Splash, MainMenu, Settings, Credits, Loading, InGame, ShipEditor }

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

        public void StartSingleplayer()
        {
            // Singleplayer hosts the bundled dedicated server as a child process bound to
            // loopback (Option A), then connects to it like any other server. Start it now so
            // it has the loading-screen window to come up before the client connects.
            _hostLocal = true;
            if (_localServer.Start(LocalServerLauncher.DefaultPort, Settings.ViewDistanceChunks))
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
            Phase = ShellPhase.MainMenu;
        }

        private GameObject _uiMenu;
        private GameObject _uiLoading;
        private GameObject _uiSettings;
        private GameObject _uiCredits;
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
            Phase = ShellPhase.MainMenu;
        }

        /// <summary>Time-based loading progress (0..1) for the uGUI loading bar.</summary>
        public float LoadingProgress => _loading.Progress;

        private void Update()
        {
            _splash.Update();
            _loading.Update();

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

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (Phase == ShellPhase.InGame)
                {
                    // Esc while typing in chat just cancels the chat input, it doesn't quit to the menu.
                    var boot = _gameRoot != null ? _gameRoot.GetComponentInChildren<GameBootstrap>() : null;
                    if (boot == null || !boot.ChatTyping)
                    {
                        ReturnToMenu();
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
                else if (Phase == ShellPhase.ShipEditor)
                {
                    CloseShipEditor();
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
    }
}
