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

        private void Awake()
        {
            Settings = ClientSettings.Load();
            Settings.Apply();
            LoadLocalizer();

            _splash = new SplashScreen(this);
            _menu = new MainMenu(this);
            _settings = new SettingsScreen(this);
            _loading = new LoadingScreen(this);
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
            if (_localServer.Start())
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

        /// <summary>Spawns the in-game bootstrap configured from the shell + settings.</summary>
        public void LaunchGame()
        {
            var go = new GameObject("Game");
            var boot = go.AddComponent<GameBootstrap>();
            boot.Host = Host;
            boot.Port = int.TryParse(Port, out var p) && p > 0 ? p : 31415;
            boot.PlayerName = string.IsNullOrWhiteSpace(PlayerName) ? "Pilot" : PlayerName;
            boot.German = Settings.Language == "de";
            Phase = ShellPhase.InGame;
        }

        private void Update()
        {
            _splash.Update();
            _loading.Update();
        }

        private void OnGUI()
        {
            switch (Phase)
            {
                case ShellPhase.Splash: _splash.Draw(); break;
                case ShellPhase.MainMenu: _menu.Draw(); break;
                case ShellPhase.Settings: _settings.Draw(); break;
                case ShellPhase.Credits: DrawCredits(); break;
                case ShellPhase.Loading: _loading.Draw(); break;
                case ShellPhase.InGame: break; // GameBootstrap + Hud own the screen
            }
        }

        private void DrawCredits()
        {
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
