// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The shell phases: splash, main menu, settings, credits, loading, in-game.</summary>
    public enum ShellPhase { Splash, MainMenu, Settings, Credits, Loading, InGame, ShipEditor, AvatarEditor, StructureEditor, ContentEditor, MaterialEditor, Editors, SaveSelect, Studio, Intro }

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
        /// <summary>The build version shown in the UI. Single source of truth = <see cref="Application.version"/>
        /// (PlayerSettings.bundleVersion), which the release CI sets from the git tag at build time
        /// (via GameCI's versioning). Local/dev builds show the committed bundleVersion
        /// (e.g. <c>0.1.0-dev</c>).</summary>
        public static string Version => Application.version;

        public ShellPhase Phase { get; private set; } = ShellPhase.Studio; // studio splash → title splash → menu
        public ClientSettings Settings { get; private set; }
        public GameContent Content { get; private set; }
        public Localizer Localizer { get; private set; }

        /// <summary>The live in-game world (its <see cref="GameBootstrap"/>), or null in the shell screens.
        /// Read by the persistent <see cref="ClientMusic"/> director to pick context music.</summary>
        public GameBootstrap CurrentBoot { get; private set; }

        /// <summary>Default port of official/dedicated servers. Named in the join dialog's hint, but NOT its
        /// prefill: official worlds bring their own host + port from the portal, so the only thing typed into
        /// that dialog by hand is a friend's "Host Game" world, which listens on
        /// <see cref="LocalServerLauncher.DefaultPort"/> (#978).</summary>
        public const int DefaultServerPort = 31415;

        // Live join target — whatever the next StartJoin dials. Written by ALL join routes: the connect
        // dialog, the portal (official worlds), the WebGL/arcade defaults and in-game hosting.
        // PlayerName is loaded from / persisted to ClientSettings (Awake / the connect dialog);
        // Password is session-only.
        public string Host = "127.0.0.1";
        public string Port = "31415"; // kept as a string (it is edited in the connect dialog)

        /// <summary>What the connect dialog prefills — deliberately NOT <see cref="Host"/>/<see cref="Port"/>
        /// (#978). Those are the live join target and get overwritten by the portal join and by in-game
        /// hosting, so after one visit to an official world the dialog would offer that world's address back
        /// as the "default". Only the connect dialog writes these, and the port starts on the value the
        /// dialog is actually used for: a friend's hosted world on the LAN.</summary>
        public string ManualJoinHost = "127.0.0.1";
        public string ManualJoinPort = LocalServerLauncher.DefaultPort.ToString();
        public string PlayerName = ""; // empty until chosen — the menu gates play actions on it (#221)
        public string Password = "";

        /// <summary>Join grant for an OFFICIAL hosted world (set by the Official-Worlds menu right before
        /// StartJoin; short-lived). Must stay empty for singleplayer/LAN/self-host joins.</summary>
        public string HostedToken = "";

        /// <summary>Id of the joined OFFICIAL hosted world (set together with <see cref="HostedToken"/>);
        /// attached to in-game player reports. Must stay empty for singleplayer/LAN/self-host joins.</summary>
        public string HostedWorldId = "";

        /// <summary>Name-claim token for a glitch.fun arcade session (install-derived, from the session
        /// gateway). Overrides the browser-local <see cref="ClientSettings.PlayerToken"/> for the join,
        /// because that storage resets with every Glitch deployment and would lock returning guests out
        /// of their own claimed name. Must stay empty for every non-arcade join.</summary>
        public string ArcadeNameToken = "";

        /// <summary>One-shot notice shown on the main menu (e.g. why the last join was refused).</summary>
        public string MenuNotice = "";

        /// <summary>Browser builds cannot launch the bundled native server, but they can join a hosted WebSocket server.</summary>
        public static bool BrowserLocalServerBlocked
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        private SplashScreen _splash;
        private StudioSplash _studio;
        private LoadingScreen _loading;
        private IntroCinematic _intro;

        private readonly LocalServerLauncher _localServer = new LocalServerLauncher();
        private bool _hostLocal;

        /// <summary>The in-process singleplayer host (browser builds; usable in the editor for testing).
        /// Null until the first browser-singleplayer start; survives returns to the menu stopped.</summary>
        public BrowserLocalServer BrowserServer { get; private set; }

        /// <summary>Placeholder host for the in-process browser world: it is never resolved or dialed —
        /// the rig recognises it and takes the in-memory wire instead. Nothing listens on this name.</summary>
        public const string BrowserLoopbackHost = "loopback";

        /// <summary>True while <see cref="BootBrowserSingleplayer"/> is still bringing the in-process world
        /// up. The loading screen must not hand off to <see cref="LaunchGame"/> before this clears: the rig
        /// captures the loopback wire once, and a launch that beats the boot ends up with no wire at all,
        /// dialing the "loopback" placeholder host over WebSocket until it gives up (#771).</summary>
        public bool BrowserWorldBooting { get; private set; }
        private bool _serverPending;                          // prepared, waiting to spawn once the screen is up
        private System.Threading.Tasks.Task<bool> _serverLaunch; // the off-thread spawn (so Process.Start can't freeze us)
        private GameObject _gameRoot;

        public bool ContentReady { get; private set; }

        /// <summary>Non-empty after a failed content load (malformed local file) or a failed WebGL content
        /// download — drives the blocking error+retry overlay instead of a dead shell (#422 M8/M9).</summary>
        public string ContentLoadError { get; private set; } = "";

        private bool _splashSoundDone;
        private bool _autoJoinWhenReady;

        private void Awake()
        {
            MigrateRenamedPersistentData();
            Settings = ClientSettings.Load();

            // Install the global crash reporter early (on its own DontDestroyOnLoad object) so unhandled client
            // exceptions are captured app-wide and reported automatically — see CrashReporter. AddComponent runs
            // its Awake (hooks the log callback) synchronously; we pass Settings right after for the report id.
            var crashGo = new GameObject("CrashReporter");
            crashGo.AddComponent<CrashReporter>().Settings = Settings;
            InputMap.Use(Settings); // route remappable controls through the loaded bindings (Stream C)
            Settings.Apply();
            if (StreamingAssetsCache.UsesRemoteStreamingAssets)
            {
                StartCoroutine(LoadContentForStartup());
            }
            else
            {
                StreamingAssetsCache.EnsureLocalReady();
                LoadLocalizer();
            }

            if (!string.IsNullOrWhiteSpace(Settings.PlayerName))
            {
                PlayerName = Settings.PlayerName.Trim();
            }

            ApplyGlitchServerDefaults();
            ConfigureOptionalWebAutoJoin();

            // Quiet update check (#543), fired during the splash so the answer is usually in before the
            // menu appears. Editor/WebGL/portable runs no-op inside; failures are silent by design.
            if (Settings.UpdateCheckOnStart)
            {
                ClientUpdater.CheckForNoticeOnStartup(Settings.UpdateFeedUrl);
            }

            // The 3D renders at native resolution (crisp on 4K); the IMGUI UI keeps a readable
            // physical size via UiScale (virtual 1080p layout) instead of a blunt resolution cap.
            _splash = new SplashScreen(this);
            _studio = new StudioSplash(this);
            _loading = new LoadingScreen(this);
            _intro = new IntroCinematic(this);

            GlitchIntegration.InstallIfConfigured();
            if (ContentReady)
            {
                EnsureMenuBackground();
            }

            // Persistent background-music director: spans splash → menu → loading → in-game so the shell
            // screens get music too, and cross-fades context tracks (synth or the AI track library).
            gameObject.AddComponent<ClientMusic>().Shell = this;
        }

        private void ConfigureOptionalWebAutoJoin()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            _autoJoinWhenReady = GlitchIntegration.AutoJoinRequested;
            string playerName = GlitchIntegration.AutoJoinPlayerName;
            if (!string.IsNullOrWhiteSpace(playerName))
            {
                PlayerName = playerName.Trim();
            }

            // Official-worlds deep-link (the portal's Play button): the join grant rides the page URL
            // and is threaded through the same HostedToken path the native Official-Worlds menu uses —
            // without it the hosted instance's token gate rejects the browser join.
            string hostedToken = GlitchIntegration.AutoJoinHostedToken;
            if (!string.IsNullOrWhiteSpace(hostedToken))
            {
                HostedToken = hostedToken.Trim();
                HostedWorldId = (GlitchIntegration.AutoJoinHostedWorldId ?? "").Trim();
            }
            else if (GlitchIntegration.SingleplayerRequested)
            {
                // ?singleplayer=1 deep-link: straight into the in-browser world once content is ready.
                _autoSingleplayerWhenReady = true;
            }

            // glitch.fun deliberately does NOT auto-join anymore: the menu offers the choice between
            // the shared arcade worlds and the in-browser singleplayer — jumping straight into
            // multiplayer hid that singleplayer exists (first live feedback). The menu's arcade
            // button calls RetryArcadeJoin(); still one click, but a chosen one.
#endif
        }

        private bool _autoSingleplayerWhenReady;

#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>Menu retry for the glitch.fun arcade: the auto-join runs on page load, but when it
        /// fails (gateway hiccup, full worlds) the menu's Play button re-requests a session instead of
        /// dialing the meaningless default host — on Glitch the player never picks a server.</summary>
        public void RetryArcadeJoin()
        {
            MenuNotice = "";
            StartCoroutine(RequestArcadeJoin());
        }

        private IEnumerator RequestArcadeJoin()
        {
            yield return GlitchIntegration.RequestArcadeSession(PlayerName, (session, error, errorCode) =>
            {
                if (session == null)
                {
                    Debug.LogWarning($"[Glitch] Arcade session failed: {error}");
                    // "glitch_full" = every arcade world is at its player cap, "no_capacity" = the
                    // fleet's RAM budget is spent — either way capacity, not an outage, so point the
                    // player at singleplayer (which needs no server slot) instead of "try again".
                    MenuNotice = errorCode == "glitch_full" || errorCode == "no_capacity"
                        ? L("ui.glitch.arcade_full")
                        : L("ui.glitch.arcade_failed");
                    return;
                }

                PlayerName = session.playerName;
                Host = session.wssUrl;
                HostedToken = session.joinToken;
                HostedWorldId = session.worldId;
                ArcadeNameToken = session.nameToken ?? "";
                _autoJoinWhenReady = true; // the Update() gate joins once content + menu are ready
            });
        }
#endif

        private void ApplyGlitchServerDefaults()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!GlitchIntegration.TryGetConfiguredServer(out string host, out string port, out string password))
            {
                return;
            }

            Host = host;
            if (!string.IsNullOrWhiteSpace(port))
            {
                Port = port;
            }

            Password = password ?? string.Empty;
            Debug.Log($"[Glitch] Applied WebGL server defaults: {Host}:{Port}.");
#endif
        }

        private IEnumerator LoadContentForStartup()
        {
            // Locale files first (#831): the shell screens run on fixed timers — studio splash 5 s, title
            // splash 3.2 s, intro cinematic 28 s — and start on the first frame, while the full content
            // cache is 30+ files / ~1.6 MB of HTTP. Waiting for all of it means they render raw "ui.*"
            // keys for their whole visible window. Two locale files are all they need, so fetch those and
            // publish a bootstrap localizer immediately; LoadLocalizer swaps in the content-backed one.
            yield return BootstrapLocalizer();

            System.Exception failure = null;
            yield return StreamingAssetsCache.EnsureReady(ex => failure = ex);
            if (failure != null)
            {
                // One failed download of the ~20 data files (CDN hiccup, flaky mobile network) must not
                // leave a dead menu with raw ui.* keys forever — surface it with a Retry instead (#422 M9).
                // EnsureReady is re-entrant, so RetryContentLoad can simply run this coroutine again.
                Debug.LogError($"Content startup failed: {failure.Message}");
                ContentLoadError = failure.Message;
                yield break;
            }

            LoadLocalizer();
            EnsureMenuBackground();
        }

        /// <summary>Builds a localizer from just the locale tables (active language + the English fallback),
        /// so the browser's shell screens are localized seconds before the content cache is complete. Purely
        /// best-effort — a failed fetch leaves <see cref="Localizer"/> null and the normal load takes over.</summary>
        private IEnumerator BootstrapLocalizer()
        {
            var locale = GameLocaleExtensions.Parse(Settings.Language);
            System.Collections.Generic.Dictionary<string, string> english = null, active = null;

            yield return StreamingAssetsCache.FetchDataText("locales/en.json", text => english = ParseLocaleTable(text));
            if (locale != GameLocale.English)
            {
                yield return StreamingAssetsCache.FetchDataText($"locales/{locale.Code()}.json", text => active = ParseLocaleTable(text));
            }

            if (english == null && active == null)
            {
                yield break; // offline/404 — the keys stay visible until the full load succeeds or errors out
            }

            Localizer = new Localizer(locale, active ?? english, english ?? active);
            Debug.Log($"Bootstrap localizer ready for '{locale.Code()}' before the content cache.");
        }

        private static System.Collections.Generic.Dictionary<string, string> ParseLocaleTable(string json)
        {
            try
            {
                return ContentLoader.ParseLocaleTable(json);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Bootstrap locale parse failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// One-time migration for the game rename: the old install used "Spacecraft" as the Unity
        /// productName changed, which moved <see cref="Application.persistentDataPath"/> to a new
        /// folder. Adopt everything from the old folder (client settings, singleplayer saves,
        /// editor exports) so existing installs keep their data. Must run before anything reads
        /// or writes the persistent data path.
        /// </summary>
        private static void MigrateRenamedPersistentData()
        {
            try
            {
                string newRoot = Application.persistentDataPath;
                string parent = Path.GetDirectoryName(newRoot);
                if (string.IsNullOrEmpty(parent))
                {
                    return;
                }

                string oldRoot = Path.Combine(parent, "Spacecraft");
                if (!Directory.Exists(oldRoot) || File.Exists(Path.Combine(newRoot, "client_settings.json")))
                {
                    return; // nothing to migrate, or the new folder is already in use
                }

                Directory.CreateDirectory(newRoot);
                foreach (string entry in Directory.GetFileSystemEntries(oldRoot))
                {
                    string target = Path.Combine(newRoot, Path.GetFileName(entry));
                    if (File.Exists(target) || Directory.Exists(target))
                    {
                        continue; // never clobber data already present under the new name
                    }

                    if (Directory.Exists(entry))
                    {
                        Directory.Move(entry, target);
                    }
                    else
                    {
                        File.Move(entry, target);
                    }
                }

                Debug.Log($"Migrated persistent data from '{oldRoot}' to '{newRoot}' (game renamed).");
            }
            catch (System.Exception e)
            {
                // A failed migration must never block startup — the game just starts fresh.
                Debug.LogWarning($"Persistent-data migration from the old 'Spacecraft' folder failed: {e.Message}");
            }
        }

        private GameObject _menuBackground;

        /// <summary>Spawns the animated space-scene backdrop shown behind the shell screens.</summary>
        private void EnsureMenuBackground()
        {
            if (!ContentReady)
            {
                return;
            }

            if (_menuBackground == null)
            {
                _menuBackground = new GameObject("MenuBackground");
                _menuBackground.AddComponent<MenuBackground>().Shell = this; // supplies content + hull colour
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

            if (FindAnyObjectByType<AudioListener>() == null)
            {
                gameObject.AddComponent<AudioListener>();
            }

            var src = gameObject.AddComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.volume = Mathf.Clamp01(Settings?.MasterVolume ?? 0.8f);
            src.PlayOneShot(clip);
        }

        /// <summary>Plays the developer-studio splash whoosh→tada sting (the bespoke ElevenLabs sound when
        /// bundled, else the intro sting as a fallback so the screen is never silent).</summary>
        public void PlayStudioSting()
        {
            var clip = Resources.Load<AudioClip>("audio/jumave_sting")
                       ?? Resources.Load<AudioClip>("audio/splash_intro");
            if (clip == null)
            {
                return;
            }

            if (FindAnyObjectByType<AudioListener>() == null)
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
            if (!StreamingAssetsCache.IsReady)
            {
                if (StreamingAssetsCache.UsesRemoteStreamingAssets)
                {
                    Debug.LogWarning("Remote StreamingAssets content is not ready yet.");
                    return;
                }

                StreamingAssetsCache.EnsureLocalReady();
            }

            string dataDir = StreamingAssetsCache.DataDir;
            GameContent loaded;
            try
            {
                loaded = ContentLoader.LoadFromDirectory(dataDir);
            }
            catch (System.Exception e)
            {
                // One malformed data file (corrupted install, interrupted patch, AV lock) must not escape
                // Awake and brick the shell with per-frame NREs (#422 M8). With content already loaded
                // (e.g. a re-load via Settings→CloseSettings) keep the working in-memory snapshot; on a
                // cold start the error+retry overlay takes over.
                Debug.LogError($"Content load from '{dataDir}' failed: {e}");
                if (!ContentReady)
                {
                    ContentLoadError = e.Message;
                }

                return;
            }

            Content = loaded;
            ContentLoadError = "";
            Debug.Log($"Content loaded from '{dataDir}' ({Content.Blocks.Count} blocks, {Content.Items.Count} items, {Content.Recipes.Count} recipes, {Content.Planets.Count} planet types).");
            var locale = GameLocaleExtensions.Parse(Settings.Language);
            Localizer = Content.CreateLocalizer(locale);
            ContentReady = true;

            // A live world keeps its OWN Localizer (snapshotted at build) — push the change so the running
            // HUD/chat swap language immediately instead of after re-entering the world (Severin playtest).
            CurrentBoot?.SetLanguage(locale);

            // The pause menu caches its texts at build time — drop it so the next show rebuilds localized.
            if (_quitDialog != null)
            {
                Destroy(_quitDialog);
                _quitDialog = null;
            }

            // Same for the cached uGUI shell screens: on WebGL the locale files arrive asynchronously, so the
            // main menu can be built while Localizer is still null and would otherwise show raw ui.* keys until
            // the next phase change (#377). Dropping them here makes Update() rebuild them localized.
            if (_uiMenu != null)
            {
                Destroy(_uiMenu);
                _uiMenu = null;
            }

            if (_uiLoading != null)
            {
                Destroy(_uiLoading);
                _uiLoading = null;
            }
        }

        /// <summary>Retries a failed content load: re-runs the WebGL download when the remote cache never
        /// became ready, else re-reads the local data directory. Wired to the error overlay's button (#422).</summary>
        public void RetryContentLoad()
        {
            ContentLoadError = "";
            if (StreamingAssetsCache.UsesRemoteStreamingAssets && !StreamingAssetsCache.IsReady)
            {
                StartCoroutine(LoadContentForStartup());
            }
            else
            {
                LoadLocalizer();
                if (ContentReady)
                {
                    EnsureMenuBackground();
                }
            }
        }

        /// <summary>Localize, falling back to the key before content is loaded.</summary>
        public string L(string key) => Localizer != null ? Localizer.Get(key) : key;

        public void GoTo(ShellPhase phase) => Phase = phase;

        /// <summary>Title-splash hand-off (#759): the first launch flows through the generic intro
        /// cinematic once; afterwards straight to the menu. Credits can replay it any time. Unattended
        /// capture runs (ScreenshotDirector/ClipDirector) skip it — a first-run cinematic would stall
        /// their phase waits and land in the frames.</summary>
        public void OnTitleSplashDone()
        {
            var args = System.Environment.GetCommandLineArgs();
            bool captureRun = System.Array.IndexOf(args, "-captureShots") >= 0
                              || System.Array.IndexOf(args, "-captureClip") >= 0;
            GoTo(!captureRun && Settings != null && !Settings.IntroSeen ? ShellPhase.Intro : ShellPhase.MainMenu);
        }

        /// <summary>Replays the intro cinematic from the Credits screen (never re-stamps the seen flag).</summary>
        public void PlayIntroFromCredits()
        {
            _intro.BeginReplay();
            GoTo(ShellPhase.Intro);
        }

        /// <summary>Intro finished or was skipped: stamp the once-per-install flag (not on replays) and
        /// land on the main menu — the bombastic menu sting then plays on its first reveal as usual.</summary>
        public void OnIntroFinished(bool replay)
        {
            if (!replay && Settings != null && !Settings.IntroSeen)
            {
                Settings.IntroSeen = true;
                Settings.Save();
            }

            GoTo(ShellPhase.MainMenu);
        }

        /// <summary>Forces the save-select screen to rebuild next frame (e.g. after deleting a world) — the phase
        /// stays SaveSelect, so without this the list wouldn't refresh and a delete looked like it did nothing (B59).</summary>
        public void RefreshSaveSelect()
        {
            if (_uiSaveSelect != null)
            {
                Destroy(_uiSaveSelect);
                _uiSaveSelect = null;
            }
        }

        // Where CloseSettings returns to. Settings is opened both from the main menu and from the in-game pause
        // menu; it must go back to whichever it came from, so the player can change the volume mid-game without
        // quitting the world (Severin playtest).
        private ShellPhase _settingsReturnPhase = ShellPhase.MainMenu;

        public void OpenSettings()
        {
            _settingsReturnPhase = Phase;
            if (Phase == ShellPhase.InGame)
            {
                ShowQuitDialog(false); // tuck the pause menu away while the settings overlay is up
            }

            Phase = ShellPhase.Settings;
        }

        public void CloseSettings()
        {
            Settings.Save();
            Settings.Apply();
            LoadLocalizer(); // language may have changed
            if (_settingsReturnPhase == ShellPhase.InGame && _gameRoot != null)
            {
                Phase = ShellPhase.InGame;
                ShowQuitDialog(true); // back to the (still-paused) pause menu over the live world
            }
            else
            {
                Phase = ShellPhase.MainMenu;
            }
        }

        /// <summary>True while the save-select screen is picking a world to HOST (multiplayer)
        /// instead of singleplayer — set by the main menu, read by <see cref="UiSaveSelect"/>.</summary>
        public bool HostMode { get; private set; }

        /// <summary>While hosting: the LAN address friends can join ("ip:port"), shown in-game. Else empty.</summary>
        public string HostInfo { get; private set; } = "";

        /// <summary>Opens the singleplayer world picker (choose an existing save or start a new one).</summary>
        public void StartSingleplayer()
        {
            if (ShowBrowserLocalServerBlockedNotice())
            {
                return;
            }

            HostMode = false;
            Phase = ShellPhase.SaveSelect;
        }

        /// <summary>Opens the world picker in host mode (any singleplayer save can be hosted, "open to LAN" style).</summary>
        public void StartHost()
        {
            if (ShowBrowserLocalServerBlockedNotice())
            {
                return;
            }

            HostMode = true;
            Phase = ShellPhase.SaveSelect;
        }

        /// <summary>Launches singleplayer on a specific world (creates it if new); seed 0 = derive from name. The
        /// creative flags are only honoured when the world is first created (the server bakes them into the save).</summary>
        public void StartSingleplayerWorld(string worldName, long seed = 0,
            bool creativeUnlockAll = false, bool creativeAllShips = false, bool creativeKit = false,
            bool sandbox = false,
            WorldCreationOptions worldOptions = null, bool creativeFlight = false)
            => StartLocalWorld(worldName, seed, creativeUnlockAll, creativeAllShips, creativeKit, sandbox, worldOptions,
                maxPlayers: 1, password: null, creativeFlight);

        /// <summary>Hosts a multiplayer world in-game: launches the bundled server on a singleplayer save
        /// with the chosen player cap (+ optional join password) and joins it immediately. The host's
        /// player name is passed as <c>--admins</c>, so the host is always an admin (the very first
        /// player of a fresh world is its WorldAdmin anyway).</summary>
        public void StartHostWorld(string worldName, int maxPlayers, string password, long seed = 0,
            bool creativeUnlockAll = false, bool creativeAllShips = false, bool creativeKit = false,
            bool sandbox = false,
            WorldCreationOptions worldOptions = null, bool creativeFlight = false)
            => StartLocalWorld(worldName, seed, creativeUnlockAll, creativeAllShips, creativeKit, sandbox, worldOptions,
                Mathf.Clamp(maxPlayers, 2, 16), password, creativeFlight);

        private void StartLocalWorld(string worldName, long seed,
            bool creativeUnlockAll, bool creativeAllShips, bool creativeKit, bool sandbox, WorldCreationOptions worldOptions,
            int maxPlayers, string password, bool creativeFlight = false)
        {
            if (ShowBrowserLocalServerBlockedNotice())
            {
                return;
            }

            // Singleplayer AND in-game hosting run the bundled dedicated server as a child process
            // (Option A), then connect to it like any other server; hosting just opens the player cap.
            bool hosting = maxPlayers > 1;
            _hostLocal = true;
            MenuNotice = "";
            Settings.LastWorld = worldName;
            Settings.Save();

            // Prepare the launch on the main thread (it reads Unity paths), but DON'T spawn the server yet:
            // show the loading screen first (below), then spawn it on a background thread. Otherwise the
            // blocking Process.Start (a Defender first-scan of the freshly-built EXE can stall it for seconds)
            // would freeze the menu so "nothing happens" before the loading screen appears.
            if (_localServer.Prepare(LocalServerLauncher.DefaultPort, Settings.ViewDistanceChunks, worldName, seed,
                    creativeUnlockAll, creativeAllShips, creativeKit, sandbox, worldOptions?.ToArgs(),
                    maxPlayers, password, hosting ? worldName : "Singleplayer", PlayerName, creativeFlight))
            {
                Host = _localServer.Host;
                Port = _localServer.Port.ToString();
                Password = password ?? "";
                HostInfo = hosting ? $"{LocalLanIp()}:{_localServer.Port}" : "";
                _loading.MinShow = 2.5f; // give the server time to start listening
                _serverPending = true;
            }
            else
            {
                // No bundled server (not published yet): fall back to a manually started one.
                Host = "127.0.0.1";
                Password = "";
                HostInfo = "";
            }

            Phase = ShellPhase.Loading;
        }

        /// <summary>In-process singleplayer for browser builds: WebGL cannot spawn the bundled server
        /// process, so the REAL authoritative server runs inside this client (LoopbackTransport +
        /// MemoryWorldRepository), pumped by <see cref="BrowserLocalServer"/>. One persistent world per
        /// browser (IndexedDB blob), cloud-synced on glitch.fun for logged-in accounts.</summary>
        public void StartBrowserSingleplayer()
        {
            MenuNotice = "";
            HostedToken = "";
            HostedWorldId = "";
            ArcadeNameToken = "";
            Password = "";
            HostInfo = "";
            _hostLocal = false;
            _serverPending = false;
            Host = BrowserLoopbackHost; // GameBootstrap picks the loopback transport off shell.BrowserServer.Link
            _loading.MinShow = 1.2f;
            BrowserWorldBooting = true; // MinShow is a minimum, not a deadline — the boot gates the launch
            Phase = ShellPhase.Loading;
            StartCoroutine(BootBrowserSingleplayer());
        }

        private IEnumerator BootBrowserSingleplayer()
        {
            // Two frames so the loading screen is actually visible before the synchronous initial
            // worldgen blocks the (single) thread.
            yield return null;
            yield return null;

            if (BrowserServer == null)
            {
                var go = new GameObject("BrowserLocalServer");
                DontDestroyOnLoad(go);
                BrowserServer = go.AddComponent<BrowserLocalServer>();
            }
            else if (BrowserServer.Running)
            {
                BrowserServer.StopAndSave(); // restart cleanly (menu → SP → menu → SP)
            }

            byte[] blob = BrowserLocalServer.LoadLocalBlob();

            // glitch.fun cloud save: a newer cloud snapshot wins over the local blob (continuing on
            // another device). Guests and non-Glitch hosts skip this silently — local-only then.
            if (GlitchCloudSaves.Enabled)
            {
                yield return GlitchCloudSaves.FetchLatest(cloudBlob =>
                {
                    if (cloudBlob != null)
                    {
                        blob = cloudBlob;
                    }
                });
            }

            long freshSeed = (long)UnityEngine.Random.Range(1, int.MaxValue) << 16 ^ System.DateTime.UtcNow.Ticks;
            if (!BrowserServer.StartServer(Content, blob, freshSeed))
            {
                BrowserWorldBooting = false;
                ReturnToMenu();
                MenuNotice = L("ui.sp.browser_failed");
                yield break;
            }

            GlitchCloudSaves.Attach(this, BrowserServer); // upload each durable save (no-op off glitch.fun)
            BrowserWorldBooting = false; // the wire exists — the loading screen may hand off now
        }

        /// <summary>The machine's LAN IPv4 (the address friends on the same network join), or loopback.
        /// Enumeration happens here (it needs the platform); WHICH interface wins is decided by the
        /// unit-tested <see cref="LanAddress"/> — "the first one that is up" used to hand out the
        /// Hyper-V/VirtualBox/VPN address on any box that has one (#984).</summary>
        public static string LocalLanIp()
        {
            try
            {
                var candidates = new List<LanCandidate>();
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up
                        || ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }

                    var props = ni.GetIPProperties();
                    bool hasGateway = false;
                    foreach (var gw in props.GatewayAddresses)
                    {
                        // A gateway entry of 0.0.0.0 is Windows' way of saying "none" on some adapters.
                        if (gw?.Address != null
                            && gw.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                            && !System.Net.IPAddress.Any.Equals(gw.Address))
                        {
                            hasGateway = true;
                            break;
                        }
                    }

                    bool physical = ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet
                        || ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211
                        || ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.GigabitEthernet
                        || ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.FastEthernetT
                        || ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.FastEthernetFx;

                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            candidates.Add(new LanCandidate(addr.Address.ToString(), hasGateway, physical,
                                ni.Name + " " + ni.Description));
                        }
                    }
                }

                return LanAddress.Pick(candidates);
            }
            catch
            {
                // Fall through to loopback — the host can still read the port from the dialog.
                return LanAddress.Loopback;
            }
        }

        /// <summary>The "ip:port" a friend on the same network types into Connect — the host screen shows
        /// it BEFORE the world launches (#984), and <see cref="HostInfo"/> repeats it in-game. Reads the
        /// live interface list on each call, so switching from cable to Wi-Fi is picked up.</summary>
        public static string LanJoinAddress(int port = LocalServerLauncher.DefaultPort)
            => LocalLanIp() + ":" + port.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public void StartJoin()
        {
            _hostLocal = false;
            HostInfo = "";
            MenuNotice = "";
            _loading.MinShow = 0.6f;
            Phase = ShellPhase.Loading;
        }

        public bool ShowBrowserLocalServerBlockedNotice()
        {
            if (!BrowserLocalServerBlocked)
            {
                return false;
            }

            MenuNotice = L("ui.webgl.gameplay_blocked");
            HostInfo = "";
            _serverPending = false;
            Phase = ShellPhase.MainMenu;
            if (_uiMenu != null)
            {
                Destroy(_uiMenu);
                _uiMenu = null;
            }

            return true;
        }

        public void Quit()
        {
            StopLocalServer();
            Application.Quit();
        }

        private void StopLocalServer()
        {
            _serverPending = false;
            if (_serverLaunch != null)
            {
                try { _serverLaunch.Wait(3000); } catch { } // let an in-flight spawn finish so we can stop it
                _serverLaunch = null;
            }

            if (_hostLocal)
            {
                _localServer.Stop();
                _hostLocal = false;
            }

            HostInfo = "";
        }

        private void OnApplicationQuit() => _localServer.Stop();

        private void OnDestroy() => _localServer.Stop();

        /// <summary>Builds the in-game rig (player + camera + world + HUD) and enters play.</summary>
        public void LaunchGame()
        {
            if (!ContentReady)
            {
                Debug.LogWarning("Game launch delayed until bundled content is ready.");
                return;
            }

            DestroyMenuBackground();
            _gameRoot = WorldRig.Build(this);
            CurrentBoot = Boot(); // hand the live world to the music director
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Phase = ShellPhase.InGame;
        }

        // NOTE (#413 N2): Alt-Tab cursor re-lock used to live in an OnApplicationFocus handler here — with
        // hand-rolled "is anyone else holding the cursor?" checks that missed space flight. The arbiter in
        // GameBootstrap.LateUpdate now re-asserts the lock every frame from the owner set, which covers
        // focus regain in every mode with no special-casing.

        /// <summary>Tears down the in-game world, stops the local server, and returns to the menu.</summary>
        public void ReturnToMenu()
        {
            CurrentBoot = null; // the music director falls back to shell-phase music
            if (_gameRoot != null)
            {
                UnityEngine.Object.Destroy(_gameRoot); // GameBootstrap.OnDestroy disconnects
                _gameRoot = null;
            }

            StopLocalServer();
            if (BrowserServer != null && BrowserServer.Running)
            {
                BrowserServer.StopAndSave(); // in-process SP: drain + save + persist the blob
            }

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
            StartCoroutine(UnloadDestroyedWorldAssets());
        }

        /// <summary>Sweeps the destroyed world's procedural assets (sky/starfield meshes+materials, chunk
        /// render meshes, icon sprites, the previous MenuBackground's leftovers). This is a single-scene game,
        /// so nothing else ever frees them — without this pass every menu↔world cycle leaks ~20 MB+, enough
        /// to OOM a WebGL tab after repeated world-hopping (#423). Deferred one frame so the
        /// <see cref="UnityEngine.Object.Destroy"/> calls above (end-of-frame) have actually released their
        /// references; GameBootstrap.OnDestroy clears the static caches that would otherwise pin the atlas.</summary>
        private IEnumerator UnloadDestroyedWorldAssets()
        {
            yield return null;
            Resources.UnloadUnusedAssets();
        }

        private bool _confirmQuit; // showing the "quit to menu?" confirmation over the game
        private bool _chatTypingPrev; // chat focus last frame (so closing chat with Esc doesn't pop quit)
        private bool _fieldTypingPrev; // text field focused last frame (an Esc that unfocuses it clears isFocused the same frame)
        private GameObject _quitDialog;

        private GameBootstrap Boot() => _gameRoot != null ? _gameRoot.GetComponentInChildren<GameBootstrap>() : null;

        /// <summary>
        /// Asks the server to hold the world while the Esc menu is up, and to let it run again afterwards.
        /// A player asked for this: the dialog was already called "Pause" with a "Resume" button, but hunger,
        /// creatures and the clock all kept running behind it.
        /// <para>
        /// It has to go through the server — singleplayer runs the bundled server as its own process, so
        /// stopping the client alone would freeze the camera while the world carried on. In multiplayer the
        /// world only actually holds once every player is in their menu (#973); until then this is just an
        /// intent, and the dialog says who it is still waiting for.
        /// </para>
        /// Tied to <see cref="_confirmQuit"/> (the menu session), not to the dialog's visibility — opening
        /// Settings from the pause menu hides the dialog but must not resume the world.
        /// </summary>
        private void SetWorldPaused(bool paused)
        {
            _nextPauseKeepAlive = paused ? Time.realtimeSinceStartup + PauseKeepAliveSeconds : 0f;
            Boot()?.Network?.SendPause(paused);
        }

        /// <summary>How often the held intent is re-sent while the menu stays open. Behind an open menu the
        /// client sends nothing else at all — no movement, no pose — so this repeat is the server's only proof
        /// that the game is still alive, and the one thing that lets it drop a player who crashed mid-pause
        /// instead of leaving the world frozen for everybody else (#973, heartbeat from #964).</summary>
        private const float PauseKeepAliveSeconds = 15f;

        private float _nextPauseKeepAlive;

        /// <summary>Re-sends the pause intent on the keep-alive cadence for as long as the menu session lasts.</summary>
        private void TickPauseKeepAlive()
        {
            if (!_confirmQuit || Time.realtimeSinceStartup < _nextPauseKeepAlive)
            {
                return;
            }

            _nextPauseKeepAlive = Time.realtimeSinceStartup + PauseKeepAliveSeconds;
            Boot()?.Network?.SendPause(true);
        }

        private void CancelQuit()
        {
            _confirmQuit = false;
            SetWorldPaused(false);
            ShowQuitDialog(false);
            // Release our ownership — the arbiter re-locks the cursor this frame unless another panel is
            // still open. Before #413 this path only cleared the shared MenuOpen bool and never re-locked,
            // so "Resume" left a live, visible OS cursor over the game (M3).
            Boot()?.SetMenuOwner(this, false);
        }

        private GameObject _uiMenu;
        private GameObject _uiUpdateNotice;
        private GameObject _uiWhatsNew;
        private bool _whatsNewOpen;
        private bool _whatsNewAutoDone;
        private GameObject _uiLoading;

        /// <summary>Opens the "What's new?" dialog over the main menu (menu button, and the one-shot
        /// auto-open after an update). Spawned/torn down by <see cref="Update"/> with the phase.</summary>
        public void OpenWhatsNew() => _whatsNewOpen = true;

        /// <summary>Closes the "What's new?" dialog (its Back button).</summary>
        public void CloseWhatsNew() => _whatsNewOpen = false;
        private GameObject _uiSettings;
        private GameObject _uiCredits;
        private GameObject _uiEditors;
        private GameObject _uiSaveSelect;
        private GameObject _uiContentError;

        /// <summary>Blocking "content failed to load" overlay with a Retry button (#422 M8/M9). Texts are
        /// hardcoded per-language here — the locale files themselves are part of the content that failed,
        /// so the localizer cannot help. Keep the switch in sync with the shipped languages.</summary>
        private GameObject BuildContentErrorUi()
        {
            string code = GameLocaleExtensions.Parse(Settings?.Language).Code();
            string titleText, bodyText, retryText;
            switch (code)
            {
                case "de":
                    titleText = "Inhalte konnten nicht geladen werden";
                    bodyText = "Das Laden der Spieldaten ist fehlgeschlagen. Prüfe deine Internetverbindung bzw. die Installation und versuche es dann erneut.";
                    retryText = "Erneut versuchen";
                    break;
                case "fr":
                    titleText = "Échec du chargement du contenu";
                    bodyText = "Le chargement des données du jeu a échoué. Vérifie ta connexion Internet ou l'installation, puis réessaie.";
                    retryText = "Réessayer";
                    break;
                case "es":
                    titleText = "No se pudo cargar el contenido";
                    bodyText = "No se pudieron cargar los datos del juego. Comprueba tu conexión a Internet o la instalación y vuelve a intentarlo.";
                    retryText = "Reintentar";
                    break;
                case "it":
                    titleText = "Impossibile caricare i contenuti";
                    bodyText = "Il caricamento dei dati di gioco non è riuscito. Controlla la connessione a Internet o l'installazione e riprova.";
                    retryText = "Riprova";
                    break;
                case "pt":
                    titleText = "Falha ao carregar o conteúdo";
                    bodyText = "Não foi possível carregar os dados do jogo. Verifique sua conexão com a internet ou a instalação e tente novamente.";
                    retryText = "Tentar novamente";
                    break;
                case "pl":
                    titleText = "Nie udało się wczytać zawartości";
                    bodyText = "Wczytywanie danych gry nie powiodło się. Sprawdź połączenie z internetem lub instalację i spróbuj ponownie.";
                    retryText = "Spróbuj ponownie";
                    break;
                case "tr":
                    titleText = "İçerik yüklenemedi";
                    bodyText = "Oyun verileri yüklenemedi. İnternet bağlantını veya kurulumu kontrol edip tekrar dene.";
                    retryText = "Tekrar dene";
                    break;
                case "nl":
                    titleText = "Inhoud kon niet worden geladen";
                    bodyText = "Het laden van de spelgegevens is mislukt. Controleer je internetverbinding of de installatie en probeer het daarna opnieuw.";
                    retryText = "Opnieuw proberen";
                    break;
                case "ru":
                    titleText = "Не удалось загрузить контент";
                    bodyText = "Не удалось загрузить данные игры. Проверь подключение к интернету или установку и попробуй ещё раз.";
                    retryText = "Повторить";
                    break;
                case "uk":
                    titleText = "Не вдалося завантажити вміст";
                    bodyText = "Не вдалося завантажити дані гри. Перевір з'єднання з інтернетом або встановлення і спробуй ще раз.";
                    retryText = "Спробувати ще раз";
                    break;
                case "zh":
                    titleText = "内容加载失败";
                    bodyText = "游戏数据加载失败。请检查网络连接或安装情况，然后重试。";
                    retryText = "重试";
                    break;
                case "ja":
                    titleText = "コンテンツを読み込めませんでした";
                    bodyText = "ゲームデータの読み込みに失敗しました。インターネット接続またはインストールを確認して、もう一度試してください。";
                    retryText = "再試行";
                    break;
                case "ko":
                    titleText = "콘텐츠를 불러오지 못했습니다";
                    bodyText = "게임 데이터를 불러오지 못했습니다. 인터넷 연결이나 설치 상태를 확인한 뒤 다시 시도해 주세요.";
                    retryText = "다시 시도";
                    break;
                default:
                    titleText = "Content failed to load";
                    bodyText = "Loading the game data failed. Check your internet connection or the install, then try again.";
                    retryText = "Retry";
                    break;
            }

            var canvas = UiKit.CreateCanvas("ContentErrorUI");
            canvas.sortingOrder = 90; // above every shell screen — the shell is unusable without content
            var root = canvas.transform;

            const float w = 720f, h = 320f;
            float x = (1920f - w) * 0.5f, y = (1080f - h) * 0.5f;
            UiKit.AddModalOverlay(root, x, y, w, h); // shared scrim + opaque panel (#588)

            var title = UiKit.AddText(root, x + 32, y + 28, w - 64, 34,
                titleText, 26, UiKit.TextCol, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;

            UiKit.AddText(root, x + 32, y + 80, w - 64, 84, bodyText,
                18, UiKit.TextCol, TextAnchor.UpperLeft);

            UiKit.AddText(root, x + 32, y + 168, w - 64, 60, ContentLoadError ?? string.Empty,
                14, UiKit.CyanDim, TextAnchor.UpperLeft);

            UiKit.AddButton(root, x + (w - 280f) * 0.5f, y + h - 82, 280, 56,
                retryText, RetryContentLoad);

            return canvas.gameObject;
        }
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
            // A failed content load/download shows the blocking retry overlay (#422) — handled before
            // anything else so it appears no matter which shell phase the failure hit.
            bool contentError = !ContentReady && !string.IsNullOrEmpty(ContentLoadError);
            if (contentError && _uiContentError == null)
            {
                _uiContentError = BuildContentErrorUi();
            }
            else if (!contentError && _uiContentError != null)
            {
                Destroy(_uiContentError);
                _uiContentError = null;
            }

            // Belt-and-braces (#422 M8): should Awake ever abort before these exist, a per-frame NRE
            // storm would freeze the shell with zero explanation — skip instead.
            if (_studio == null || _splash == null || _loading == null || _intro == null)
            {
                return;
            }

            _studio.Update();
            _splash.Update();
            _loading.Update();
            _intro.Update();

            // The intro renders its own space scene — park the menu backdrop (and its camera) while it
            // plays, or both scenes' objects would show up in both cameras.
            if (_menuBackground != null && _menuBackground.activeSelf != (Phase != ShellPhase.Intro))
            {
                _menuBackground.SetActive(Phase != ShellPhase.Intro);
            }

            // Keep the (procedural) UI click/hover volume in step with the audio settings.
            if (Settings != null)
            {
                UiSound.Volume = Mathf.Clamp01(Settings.MasterVolume * Settings.SfxVolume) * 0.6f;
            }

            if (_autoJoinWhenReady && ContentReady && Phase == ShellPhase.MainMenu)
            {
                _autoJoinWhenReady = false;
                StartJoin();
            }

            if (_autoSingleplayerWhenReady && ContentReady && Phase == ShellPhase.MainMenu)
            {
                _autoSingleplayerWhenReady = false;
                if (string.IsNullOrWhiteSpace(PlayerName))
                {
                    PlayerName = "Explorer"; // the deep-link skips the menu's name gate — save identity needs one
                }

                StartBrowserSingleplayer();
            }

            // glitch.fun arcade ban/license revocation (heartbeat relay answered 403): leave the world
            // and tell the player why — the operator's live kick lever for account-less arcade guests.
            if (GlitchIntegration.ConsumeAccessRevoked())
            {
                if (Phase == ShellPhase.InGame)
                {
                    ReturnToMenu();
                }

                _autoJoinWhenReady = false;
                MenuNotice = L("ui.glitch.access_revoked");
            }

            // The main menu + loading are uGUI (M27): spawn each for its phase, tear it down otherwise.
            if (Phase == ShellPhase.MainMenu && _uiMenu == null)
            {
                _uiMenu = UiMainMenu.Build(this);
                WhatsNew.BeginFetch(this); // one-per-session background load of the release notes (#543)

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

            // Startup update notice (#543): once the menu is up and the quiet check found a version,
            // offer it — on its own canvas, so a result landing late needs no menu rebuild. "Later"
            // sets NoticeDismissed and the else-branch tears the dialog down until the next launch.
            if (Phase == ShellPhase.MainMenu && _uiUpdateNotice == null
                && ClientUpdater.NoticeVersion.Length > 0 && !ClientUpdater.NoticeDismissed)
            {
                _uiUpdateNotice = UiUpdateNotice.Build(this);
            }
            else if ((Phase != ShellPhase.MainMenu || ClientUpdater.NoticeDismissed) && _uiUpdateNotice != null)
            {
                Destroy(_uiUpdateNotice);
                _uiUpdateNotice = null;
            }

            // One-shot "What's new?" after an update (#543): once the release notes are loaded and the
            // update notice is settled (none found, or dismissed — it has priority), compare the last
            // seen version. A fresh install is stamped silently — its player has no "new" to catch up on.
            if (Phase == ShellPhase.MainMenu && !_whatsNewAutoDone && WhatsNew.Entries != null
                && _uiUpdateNotice == null
                && (ClientUpdater.NoticeVersion.Length == 0 || ClientUpdater.NoticeDismissed))
            {
                _whatsNewAutoDone = true;
                string seen = Settings.LastSeenVersion;
                if (seen != Version)
                {
                    Settings.LastSeenVersion = Version;
                    Settings.Save();
                    if (seen.Length > 0 && WhatsNew.Entries.Count > 0)
                    {
                        OpenWhatsNew();
                    }
                }
            }

            if (Phase == ShellPhase.MainMenu && _whatsNewOpen && _uiWhatsNew == null)
            {
                _uiWhatsNew = UiWhatsNew.Build(this);
            }
            else if ((Phase != ShellPhase.MainMenu || !_whatsNewOpen) && _uiWhatsNew != null)
            {
                Destroy(_uiWhatsNew);
                _uiWhatsNew = null;
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

            // With the loading screen now on screen, spawn the prepared local server on a background thread —
            // so a blocking Process.Start (Defender first-scan of the freshly-built EXE) can't freeze the menu
            // or the loading bar. The connect happens after MinShow, by which time it's listening.
            if (_serverPending && _uiLoading != null)
            {
                _serverPending = false;
                _serverLaunch = System.Threading.Tasks.Task.Run(() => _localServer.LaunchPrepared());
            }

            // A failed local-server launch must never strand the player in an empty void (#409): consume
            // the launch task's result (Process.Start refused — AV/SmartScreen block, broken bundle) and
            // watch for an early process exit (port already bound, instant crash) while nothing is
            // connected yet. Either way: back to the menu with a notice instead of a silent chunk-less
            // world. Once the client is connected, a dying server fires Disconnected → DisconnectScreen,
            // so this watcher only owns the never-connected window.
            if (_hostLocal && _serverLaunch != null && _serverLaunch.IsCompleted)
            {
                bool launched = _serverLaunch.Status == System.Threading.Tasks.TaskStatus.RanToCompletion
                                && _serverLaunch.Result;
                var bootForNet = Phase == ShellPhase.InGame ? Boot() : null;
                bool connected = bootForNet != null && bootForNet.Network != null && bootForNet.Network.Connected;
                if (connected)
                {
                    _serverLaunch = null; // handed over to the normal disconnect handling — stop watching
                }
                else if (!launched || !_localServer.IsRunning)
                {
                    _serverLaunch = null;
                    Debug.LogError("Local server failed to launch or exited before the first connect — returning to menu.");
                    ReturnToMenu();
                    MenuNotice = L("ui.sp.server_failed");
                    return;
                }
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

            // The open pause menu keeps telling the server it is still there, and keeps its own status line in
            // step with who else has (not) paused yet (#973). Both are no-ops outside a menu session.
            TickPauseKeepAlive();
            if (_confirmQuit)
            {
                RefreshPauseStatus();
            }

            // Track chat focus across frames: an Esc that closes the chat clears ChatTyping in the SAME
            // frame (the InputField's end-edit), so by the time we read it here it may already be false.
            // Remembering the previous frame's state keeps that Esc from also popping the quit dialog.
            var igBoot = Phase == ShellPhase.InGame && _gameRoot != null ? _gameRoot.GetComponentInChildren<GameBootstrap>() : null;
            bool chatActive = (igBoot != null && igBoot.ChatTyping) || _chatTypingPrev;
            _chatTypingPrev = igBoot != null && igBoot.ChatTyping;

            // The server refused our join (wrong password, name in use / verified by someone else, full):
            // bail back to the menu and show the reason there instead of waiting on the loading overlay.
            if (igBoot != null && !string.IsNullOrEmpty(igBoot.JoinRejectedReason))
            {
                // A reason of the form "@<locale key>" is a message the SERVER wants shown in the player's
                // language — used by the moderation kick (#497), where the text is ours, not free prose.
                // Everything else stays verbatim: those reasons are operator- or owner-authored.
                // Routed through the token resolver so a ":arg" tail fills the template's {name} (#964):
                // a plain L() lookup treated "srv.join.name_online:Justus" as one key and showed the
                // player the raw "[srv.join.name_online:Justus]" instead of a sentence.
                string reason = igBoot.JoinRejectedReason;
                MenuNotice = igBoot.ServerTokenText(reason);
                ReturnToMenu();
                return;
            }

            // Never connected at all (#409): the bundled server never came up (blocked/crashed) or a
            // multiplayer host/port was mistyped. Same bail-out as a rejected join — menu + reason —
            // instead of an empty void once the loading veil times out. The local-server case gets the
            // more helpful "server could not start" text (antivirus hint) over the generic connect error.
            if (igBoot != null && !string.IsNullOrEmpty(igBoot.ConnectFailedReason))
            {
                MenuNotice = _hostLocal ? L("ui.sp.server_failed") : igBoot.ConnectFailedReason;
                ReturnToMenu();
                return;
            }

            // Esc while typing in a text field (e.g. the world name on save-select) must only leave the
            // field, not tear the whole screen down (#413 N5). The InputField may process the same Esc
            // first and clear isFocused before we run, so remember the previous frame's focus too.
            bool fieldTyping = UiKit.TextFieldFocused();
            bool fieldTypingRecent = fieldTyping || _fieldTypingPrev;
            _fieldTypingPrev = fieldTyping;

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
                    else if (boot != null && (boot.MenuOpen || boot.MenuInputHandledThisFrame || boot.AwaitingRespawnConfirm || boot.VegaPrologueActive
                        || (ResolutionCinematic.Instance != null && ResolutionCinematic.Instance.Playing)))
                    {
                        // The in-game menu / a modal owns this Esc press — or the death prompt is up
                        // (#413: the quit dialog would open invisibly BEHIND its backdrop, sortingOrder
                        // 60 vs 85; only its "Weiter" button proceeds). The first-spawn prologue (#738)
                        // and the story-resolution cinematic (#1151) also own Esc as their skip key,
                        // independent of script execution order.
                    }
                    else
                    {
                        // Ask before leaving the game (rather than quitting instantly) — and actually hold the
                        // world while the menu is up, which is what the dialog has always claimed to do.
                        _confirmQuit = true;
                        SetWorldPaused(true);
                        ShowQuitDialog(true);
                        boot?.SetMenuOwner(this, true); // freezes player control + frees the cursor for the buttons (#413)
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
                    if (!fieldTypingRecent)
                    {
                        Phase = ShellPhase.MainMenu;
                    }
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

        // The in-game Esc menu. Was a bare "leave the game?" confirmation; now a small pause menu so the player
        // can reach Settings (and the volume) without quitting the world — the tester had to leave the session
        // just to turn the sound down (Severin playtest). Resume/Settings/Quit, laid out top to bottom.
        private void BuildQuitDialog()
        {
            var canvas = UiKit.CreateCanvas("Pause Menu");
            canvas.sortingOrder = 60; // above the in-game HUD/menu
            _quitDialog = canvas.gameObject;

            var (_, panel) = UiKit.AddModalOverlay(canvas.transform, 720f, 370f, 480f, 340f);
            UiKit.AddText(panel.transform, 24f, 24f, 432f, 44f,
                L("ui.pause.title"), 26, UiKit.TextCol, TextAnchor.MiddleCenter);
            UiKit.AddButton(panel.transform, 90f, 88f, 300f, 56f, L("ui.pause.resume"), CancelQuit);
            UiKit.AddButton(panel.transform, 90f, 152f, 300f, 56f, L("ui.menu.settings"), OpenSettings);
            UiKit.AddButton(panel.transform, 90f, 216f, 300f, 56f, L("ui.pause.quit"), ReturnToMenu);

            // In multiplayer the world only stops once everybody is in their menu (#973), so the dialog has to
            // say which of the two it is instead of silently claiming a pause that is not running.
            _pauseStatusText = UiKit.AddText(panel.transform, 24f, 280f, 432f, 40f,
                string.Empty, 16, UiKit.CyanDim, TextAnchor.MiddleCenter);
            RefreshPauseStatus();
        }

        private UnityEngine.UI.Text _pauseStatusText;

        /// <summary>Keeps the pause dialog's status line in step with the server's tally: held, or still waiting
        /// on the players who are named in it. Blank in singleplayer, where the hold is never in doubt.</summary>
        private void RefreshPauseStatus()
        {
            if (_pauseStatusText == null)
            {
                return;
            }

            var boot = Boot();
            if (boot == null || boot.PauseJoinedPlayers <= 1)
            {
                _pauseStatusText.text = string.Empty; // alone in the world — the hold always applies
                return;
            }

            if (boot.WorldPaused)
            {
                _pauseStatusText.text = L("ui.pause.world_held");
                return;
            }

            string waiting = boot.PauseWaitingFor;
            _pauseStatusText.text = string.IsNullOrEmpty(waiting)
                ? string.Empty
                : string.Format(L("ui.pause.waiting_for"), boot.PauseHoldingPlayers, boot.PauseJoinedPlayers, waiting);
        }
    }
}
