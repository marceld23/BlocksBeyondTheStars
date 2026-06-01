using System.IO;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Localization;
using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>The shell phases: splash, main menu, settings, credits, loading, in-game.</summary>
    public enum ShellPhase { Splash, MainMenu, Settings, Credits, Loading, InGame }

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
        private MainMenu _menu;
        private SettingsScreen _settings;
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

            // IMGUI renders at native pixels; on a high-DPI / 4K display that makes the whole UI
            // tiny. Cap the render resolution so menus + HUD stay a readable physical size.
            var cur = Screen.currentResolution;
            if (cur.width > 1920)
            {
                Screen.SetResolution(1920, 1080, Screen.fullScreenMode);
            }

            _splash = new SplashScreen(this);
            _menu = new MainMenu(this);
            _settings = new SettingsScreen(this);
            _loading = new LoadingScreen(this);
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
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Phase = ShellPhase.MainMenu;
        }

        private void Update()
        {
            _splash.Update();
            _loading.Update();

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (Phase == ShellPhase.InGame)
                {
                    ReturnToMenu();
                }
                else if (Phase == ShellPhase.Settings)
                {
                    CloseSettings();
                }
                else if (Phase == ShellPhase.Credits)
                {
                    Phase = ShellPhase.MainMenu;
                }
            }
        }

        /// <summary>Fills the whole screen with an opaque background so menu screens never bleed through.</summary>
        public void DrawBackground()
        {
            var prev = GUI.color;
            GUI.color = new Color(0.04f, 0.08f, 0.16f, 1f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private void OnGUI()
        {
            switch (Phase)
            {
                case ShellPhase.Splash:
                    // Play the intro sting on the first frame our splash actually renders (the
                    // mandatory Unity engine logo blocks game rendering until it's done), so the
                    // sound lands with the title rather than during the engine splash.
                    if (!_splashSoundDone)
                    {
                        _splashSoundDone = true;
                        PlaySplashSound();
                    }

                    _splash.Draw();
                    break;
                case ShellPhase.MainMenu: _menu.Draw(); break;
                case ShellPhase.Settings: _settings.Draw(); break;
                case ShellPhase.Credits: DrawCredits(); break;
                case ShellPhase.Loading: _loading.Draw(); break;
                case ShellPhase.InGame: break; // GameBootstrap + Hud own the screen
            }
        }

        private void DrawCredits()
        {
            DrawBackground();
            float x = Screen.width / 2f - 200, y = Screen.height / 2f - 60;
            GUI.Label(new Rect(x, y, 400, 30), L("ui.credits.title"));
            GUI.Label(new Rect(x, y + 34, 400, 60), L("ui.credits.body"));
            if (GUI.Button(new Rect(x, y + 100, 200, 30), L("ui.menu.back")))
            {
                Phase = ShellPhase.MainMenu;
            }
        }
    }
}
