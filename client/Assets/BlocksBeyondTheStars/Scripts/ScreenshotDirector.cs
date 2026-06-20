using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Automated marketing-screenshot capture. When the game is launched with the <c>-captureShots</c>
    /// command-line flag (a built player run, the canonical path) — or triggered from the editor
    /// "BlocksBeyondTheStars → Capture Screenshots" menu — this self-installs, drives the real client
    /// through a fixed sequence of scenes and writes a PNG of each to <c>marketing/screenshots/&lt;lang&gt;/</c>:
    /// <list type="number">
    ///   <item>start_screen — the main menu</item>
    ///   <item>planet_surface — the player on foot on the home world</item>
    ///   <item>cockpit_hud — the ship cockpit HUD</item>
    ///   <item>space_flight_1..3 — three in-flight vantage points in space (HUD + ship + a planet behind)</item>
    /// </list>
    /// ONE language per run (set with <c>-lang de|en</c>): the in-game HUD language is fixed when the world
    /// starts (WorldRig sets <c>boot.German</c> from <c>Settings.Language</c>), so a clean DE/EN set is two runs.
    ///
    /// Poses are reached with ordinary gameplay intents (SendExitShip / SendEnterShip / SendEnterSpace /
    /// SendShipMove) — no admin/cheat commands. Capture reuses the proven full-frame recipe
    /// (<see cref="ScreenCapture.CaptureScreenshotAsTexture"/>, which includes the ScreenSpaceOverlay HUD,
    /// like the /bump screenshot). The timings and the three flight framings are the parts most likely to
    /// need tuning on a real run — adjust the constants below.
    /// </summary>
    public sealed class ScreenshotDirector : MonoBehaviour
    {
        // --- tunables ---
        private const string WorldName = "MarketingShots";
        private const long DefaultSeed = 424242L;     // a fixed, reproducible world; override with -seed
        private const int ShotWidth = 1920;            // web resolution (Full HD)
        private const int ShotHeight = 1080;
        private const float MenuSettle = 2.0f;         // after the menu appears, before the start_screen shot
        private const float WorldLoadTimeout = 90f;    // give up waiting for the world to load
        private const float ChunkSettle = 5.0f;        // after WorldReady, let chunks mesh / the veil fully lower
        private const float PoseSettle = 2.5f;         // after a pose change, before the shot
        private const float FlightHeading = 0f; // flight heading that frames the asteroids/planet behind the ship

        private string _lang = "en";
        private long _seed = DefaultSeed;
        private string _outDir;
        private bool _headless; // true = launched via the -captureShots command line (exit the process when done)

        /// <summary>Self-install at startup when capture is requested. Reload-safe: reads config fresh from the
        /// command line (player/headless) or EditorPrefs (editor menu) rather than relying on static state.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInstall()
        {
            if (!CaptureRequested(out string lang, out string outDir, out long seed, out bool headless))
            {
                return;
            }

            var go = new GameObject("ScreenshotDirector");
            DontDestroyOnLoad(go);
            var d = go.AddComponent<ScreenshotDirector>();
            d._lang = lang;
            d._outDir = outDir;
            d._seed = seed;
            d._headless = headless;
        }

        private static bool CaptureRequested(out string lang, out string outDir, out long seed, out bool headless)
        {
            lang = "en";
            outDir = null;
            seed = DefaultSeed;
            headless = false;

            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.Equals(a, "-captureShots", StringComparison.OrdinalIgnoreCase))
                {
                    headless = true;
                }
                else if (string.Equals(a, "-lang", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    lang = args[i + 1];
                }
                else if (string.Equals(a, "-shotOut", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    outDir = args[i + 1];
                }
                else if (string.Equals(a, "-seed", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length
                         && long.TryParse(args[i + 1], out var s))
                {
                    seed = s;
                }
            }

            bool on = headless;
#if UNITY_EDITOR
            // Editor menu-item runs (no command-line flag): fall back to a one-shot EditorPrefs trigger.
            if (!on && UnityEditor.EditorPrefs.GetBool("bbs_capture", false))
            {
                on = true;
                lang = UnityEditor.EditorPrefs.GetString("bbs_capture_lang", "en");
                UnityEditor.EditorPrefs.SetBool("bbs_capture", false); // consume it
            }
#endif
            lang = lang == "de" ? "de" : "en";
            return on;
        }

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            Screen.SetResolution(ShotWidth, ShotHeight, false);

            var shell = FindFirstObjectByType<AppShell>();
            if (shell == null)
            {
                Debug.LogError("[Capture] No AppShell in the scene.");
                Quit(1);
                yield break;
            }

            // Force the capture language before anything localized is built (menu + HUD both follow this).
            shell.Settings.Language = _lang;
            shell.LoadLocalizer();

            string dir = ResolveOutDir();
            Directory.CreateDirectory(dir);
            Debug.Log($"[Capture] lang={_lang} seed={_seed} out={dir}");

            // 1) Start screen — wait for the studio/title splash chain to auto-advance to the main menu.
            yield return WaitForPhase(shell, ShellPhase.MainMenu, 30f);
            yield return new WaitForSecondsRealtime(MenuSettle);
            yield return Capture(Path.Combine(dir, "start_screen.png"));

            // 2) Start a fixed singleplayer world. Needs the bundled local server in StreamingAssets
            //    (publish-local-server.ps1 / a full build); otherwise the world never connects.
            shell.StartSingleplayerWorld(WorldName, _seed, creativeUnlockAll: true, creativeAllShips: true, creativeKit: true);

            yield return WaitForPhase(shell, ShellPhase.InGame, WorldLoadTimeout);
            var boot = shell.CurrentBoot;
            if (boot == null || boot.Network == null)
            {
                Debug.LogError("[Capture] World did not start (bundled server missing?). Aborting.");
                Quit(1);
                yield break;
            }

            yield return WaitUntil(() => boot.WorldReady, WorldLoadTimeout);
            yield return new WaitForSecondsRealtime(ChunkSettle);

            // 3) Cockpit HUD — a fresh world starts the player INSIDE the ship (the onboarding cockpit), so just
            //    capture the spawn view. No intent: ExitShip/EnterShip are space-only here and only pop a toast.
            yield return Capture(Path.Combine(dir, "cockpit_hud.png"));

            // 3b) In-game menu (the Tab menu) over the cockpit — open it exactly as Tab does, capture, close again
            //     so the following shots aren't covered by the menu. (The OS cursor isn't in a ScreenCapture RT.)
            var menu = FindFirstObjectByType<GameMenu>();
            if (menu != null)
            {
                menu.SetMenuOpen(true);
                yield return new WaitForSecondsRealtime(PoseSettle);
                Cursor.visible = false;
                yield return Capture(Path.Combine(dir, "cockpit_menu.png"));
                menu.SetMenuOpen(false);
            }

            // 4) Space flight — take off while still cleanly ABOARD (right after spawn, BEFORE stepping outside).
            //    Stepping out of the hull clears the server's aboard state, after which EnterSpace is refused and
            //    you'd stay on the planet — so flight must come first. One shot. SpaceView re-sends ShipMove at
            //    12 Hz from its own _yaw, so we set _yaw (SetFlightYaw) to choose the heading.
            boot.Network.SendEnterSpace();
            yield return WaitUntil(() => boot.InSpace, 25f);
            yield return new WaitForSecondsRealtime(ChunkSettle);

            var space = FindFirstObjectByType<SpaceView>();
            if (space != null)
            {
                space.SetFlightYaw(FlightHeading);
            }

            yield return new WaitForSecondsRealtime(PoseSettle);
            yield return Capture(Path.Combine(dir, "space_flight.png"));

            // 5) Planet surface — land back on the home world, then step the on-foot player OUT of the ship onto
            //    open terrain. No-arg LeaveSpace lands but keeps you inside the hull in the PLANET world, so the
            //    capture-pose step works (no input → the on-foot player can't move otherwise); gravity settles
            //    the player onto the ground, looking back at the landed ship.
            boot.Network.SendLeaveSpace();
            yield return WaitUntil(() => !boot.InSpace, 25f);
            yield return WaitUntil(() => boot.WorldReady, WorldLoadTimeout);
            yield return new WaitForSecondsRealtime(ChunkSettle);

            var pc = FindFirstObjectByType<PlayerController>();
            if (pc != null)
            {
                var p = boot.PlayerPosition;
                pc.SetCapturePose(new Vector3(p.x + 16f, p.y + 1f, p.z + 16f), 225f, 4f);
            }

            yield return new WaitForSecondsRealtime(ChunkSettle);
            yield return Capture(Path.Combine(dir, "planet_surface.png"));

            Debug.Log("[Capture] Done.");
            Quit(0);
        }

        /// <summary>Output folder: an explicit -shotOut, else <c>&lt;repo&gt;/docs/screenshots/&lt;lang&gt;</c> in the
        /// editor, else <c>persistentDataPath/docs/screenshots/&lt;lang&gt;</c> in a player build (the ps1 passes -shotOut).</summary>
        private string ResolveOutDir()
        {
            if (!string.IsNullOrEmpty(_outDir))
            {
                return _outDir;
            }

#if UNITY_EDITOR
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..")); // dataPath = <repo>/client/Assets
            return Path.Combine(repo, "docs", "screenshots", _lang);
#else
            return Path.Combine(Application.persistentDataPath, "docs", "screenshots", _lang);
#endif
        }

        private IEnumerator Capture(string path)
        {
            yield return new WaitForEndOfFrame(); // let the pipeline finish the frame before reading it back
            Texture2D tex = null;
            try
            {
                tex = ScreenCapture.CaptureScreenshotAsTexture(); // full composited frame, incl. the overlay HUD
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.Log($"[Capture] wrote {path}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Capture] failed {path}: {e.Message}");
            }
            finally
            {
                if (tex != null)
                {
                    Destroy(tex);
                }
            }
        }

        private static IEnumerator WaitForPhase(AppShell shell, ShellPhase phase, float timeout)
        {
            float t = 0f;
            while (shell.Phase != phase && t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private static IEnumerator WaitUntil(Func<bool> cond, float timeout)
        {
            float t = 0f;
            while (!cond() && t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private void Quit(int code)
        {
#if UNITY_EDITOR
            if (_headless)
            {
                UnityEditor.EditorApplication.Exit(code); // batch/headless editor run
            }
            else
            {
                UnityEditor.EditorApplication.isPlaying = false; // menu run — just leave play mode, keep the editor open
            }
#else
            Application.Quit(code);
#endif
        }
    }
}
