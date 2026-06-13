using System.IO;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The shell phases: splash, main menu, settings, credits, loading, in-game.</summary>
    public enum ShellPhase { Splash, MainMenu, Settings, Credits, Loading, InGame, ShipEditor, AvatarEditor, StructureEditor, ContentEditor, MaterialEditor, Editors, SaveSelect, Studio }

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

        public ShellPhase Phase { get; private set; } = ShellPhase.Studio; // studio splash → title splash → menu
        public ClientSettings Settings { get; private set; }
        public GameContent Content { get; private set; }
        public Localizer Localizer { get; private set; }

        /// <summary>The live in-game world (its <see cref="GameBootstrap"/>), or null in the shell screens.
        /// Read by the persistent <see cref="ClientMusic"/> director to pick context music.</summary>
        public GameBootstrap CurrentBoot { get; private set; }

        // Join target edited on the main menu. PlayerName is loaded from / persisted to
        // ClientSettings (Awake / the connect dialog); Password is session-only.
        public string Host = "127.0.0.1";
        public string Port = "31415";
        public string PlayerName = "Pilot";
        public string Password = "";

        /// <summary>One-shot notice shown on the main menu (e.g. why the last join was refused).</summary>
        public string MenuNotice = "";

        private SplashScreen _splash;
        private StudioSplash _studio;
        private LoadingScreen _loading;

        private readonly LocalServerLauncher _localServer = new LocalServerLauncher();
        private bool _hostLocal;
        private bool _serverPending;                          // prepared, waiting to spawn once the screen is up
        private System.Threading.Tasks.Task _serverLaunch;    // the off-thread spawn (so Process.Start can't freeze us)
        private GameObject _gameRoot;

        private bool _splashSoundDone;

        private void Awake()
        {
            MigrateRenamedPersistentData();
            Settings = ClientSettings.Load();
            Settings.Apply();
            LoadLocalizer();
            if (!string.IsNullOrWhiteSpace(Settings.PlayerName))
            {
                PlayerName = Settings.PlayerName.Trim();
            }

            // The 3D renders at native resolution (crisp on 4K); the IMGUI UI keeps a readable
            // physical size via UiScale (virtual 1080p layout) instead of a blunt resolution cap.
            _splash = new SplashScreen(this);
            _studio = new StudioSplash(this);
            _loading = new LoadingScreen(this);

            EnsureMenuBackground();

            // Persistent background-music director: spans splash → menu → loading → in-game so the shell
            // screens get music too, and cross-fades context tracks (synth or the AI track library).
            gameObject.AddComponent<ClientMusic>().Shell = this;
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

        public void OpenSettings() => Phase = ShellPhase.Settings;

        public void CloseSettings()
        {
            Settings.Save();
            Settings.Apply();
            LoadLocalizer(); // language may have changed
            Phase = ShellPhase.MainMenu;
        }

        /// <summary>True while the save-select screen is picking a world to HOST (multiplayer)
        /// instead of singleplayer — set by the main menu, read by <see cref="UiSaveSelect"/>.</summary>
        public bool HostMode { get; private set; }

        /// <summary>While hosting: the LAN address friends can join ("ip:port"), shown in-game. Else empty.</summary>
        public string HostInfo { get; private set; } = "";

        /// <summary>Opens the singleplayer world picker (choose an existing save or start a new one).</summary>
        public void StartSingleplayer()
        {
            HostMode = false;
            Phase = ShellPhase.SaveSelect;
        }

        /// <summary>Opens the world picker in host mode (any singleplayer save can be hosted, "open to LAN" style).</summary>
        public void StartHost()
        {
            HostMode = true;
            Phase = ShellPhase.SaveSelect;
        }

        /// <summary>Launches singleplayer on a specific world (creates it if new); seed 0 = derive from name. The
        /// creative flags are only honoured when the world is first created (the server bakes them into the save).</summary>
        public void StartSingleplayerWorld(string worldName, long seed = 0,
            bool creativeUnlockAll = false, bool creativeAllShips = false, bool creativeKit = false,
            WorldCreationOptions worldOptions = null)
            => StartLocalWorld(worldName, seed, creativeUnlockAll, creativeAllShips, creativeKit, worldOptions,
                maxPlayers: 1, password: null);

        /// <summary>Hosts a multiplayer world in-game: launches the bundled server on a singleplayer save
        /// with the chosen player cap (+ optional join password) and joins it immediately. The host's
        /// player name is passed as <c>--admins</c>, so the host is always an admin (the very first
        /// player of a fresh world is its WorldAdmin anyway).</summary>
        public void StartHostWorld(string worldName, int maxPlayers, string password, long seed = 0,
            bool creativeUnlockAll = false, bool creativeAllShips = false, bool creativeKit = false,
            WorldCreationOptions worldOptions = null)
            => StartLocalWorld(worldName, seed, creativeUnlockAll, creativeAllShips, creativeKit, worldOptions,
                Mathf.Clamp(maxPlayers, 2, 16), password);

        private void StartLocalWorld(string worldName, long seed,
            bool creativeUnlockAll, bool creativeAllShips, bool creativeKit, WorldCreationOptions worldOptions,
            int maxPlayers, string password)
        {
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
                    creativeUnlockAll, creativeAllShips, creativeKit, worldOptions?.ToArgs(),
                    maxPlayers, password, hosting ? worldName : "Singleplayer", PlayerName))
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

        /// <summary>The machine's LAN IPv4 (the address friends on the same network join), or loopback.</summary>
        private static string LocalLanIp()
        {
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up
                        || ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }

                    foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                            && !addr.Address.ToString().StartsWith("169.254.")) // skip link-local
                        {
                            return addr.Address.ToString();
                        }
                    }
                }
            }
            catch
            {
                // Fall through to loopback — the host can still read the port from the dialog.
            }

            return "127.0.0.1";
        }

        public void StartJoin()
        {
            _hostLocal = false;
            HostInfo = "";
            MenuNotice = "";
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
            DestroyMenuBackground();
            _gameRoot = WorldRig.Build(this);
            CurrentBoot = Boot(); // hand the live world to the music director
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Phase = ShellPhase.InGame;
        }

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
            _studio.Update();
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

            // With the loading screen now on screen, spawn the prepared local server on a background thread —
            // so a blocking Process.Start (Defender first-scan of the freshly-built EXE) can't freeze the menu
            // or the loading bar. The connect happens after MinShow, by which time it's listening.
            if (_serverPending && _uiLoading != null)
            {
                _serverPending = false;
                _serverLaunch = System.Threading.Tasks.Task.Run(() => _localServer.LaunchPrepared());
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

            // The server refused our join (wrong password, name in use / verified by someone else, full):
            // bail back to the menu and show the reason there instead of waiting on the loading overlay.
            if (igBoot != null && !string.IsNullOrEmpty(igBoot.JoinRejectedReason))
            {
                MenuNotice = igBoot.JoinRejectedReason;
                ReturnToMenu();
                return;
            }

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
