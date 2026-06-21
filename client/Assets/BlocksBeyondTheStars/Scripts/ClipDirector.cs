using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Automated short-clip (video + audio) capture — the moving-picture sibling of
    /// <see cref="ScreenshotDirector"/>. Launched with the <c>-captureClip</c> command-line flag (a built
    /// player run), this self-installs, drives the client into a scene, then renders a deterministic offline
    /// clip: it fixes <see cref="Time.captureFramerate"/> and, via <see cref="ClipFrameWriter"/>, writes one
    /// PNG per frame plus a frame-synced WAV of the game audio. An external FFmpeg step (capture-clips.ps1)
    /// muxes <c>frame_%05d.png</c> + <c>audio.wav</c> into an MP4 afterwards.
    ///
    /// ONE clip per run, selected from a JSON manifest (<c>-clipManifest &lt;path&gt; -clipName &lt;name&gt;</c>),
    /// which keeps each run a single proven world start; capture-clips.ps1 loops the player over every clip.
    /// With no manifest, a built-in default ("space_pan") is captured — the original P1 proof clip. Each clip
    /// chooses its scene (space / surface / cockpit), HUD on/off, length/fps and a simple camera move; the
    /// richer parametric camera moves and the server-lockstep for action clips come in later phases.
    ///
    /// Capture must run HEADED (a real audio device) — under <c>-batchmode -nographics</c> the audio is silent.
    /// </summary>
    public sealed class ClipDirector : MonoBehaviour
    {
        private const string WorldName = "ClipShots";
        private const long DefaultSeed = 424242L;
        private const int ClipWidth = 1920;
        private const int ClipHeight = 1080;
        private const float WorldLoadTimeout = 90f;
        private const float ChunkSettle = 5f;          // let chunks mesh / the veil lower before rolling
        private const float CaptureReadyTimeout = 14f; // surface: time to settle on solid, dry, alive ground

        private string _lang = "en";
        private long _seed = DefaultSeed;
        private string _outDir;
        private bool _headless;
        private string _manifestPath;
        private string _clipName;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInstall()
        {
            if (!ClipRequested(out var cfg))
            {
                return;
            }

            var go = new GameObject("ClipDirector");
            DontDestroyOnLoad(go);
            var d = go.AddComponent<ClipDirector>();
            d._lang = cfg.lang;
            d._outDir = cfg.outDir;
            d._seed = cfg.seed;
            d._headless = cfg.headless;
            d._manifestPath = cfg.manifest;
            d._clipName = cfg.clipName;
        }

        private struct Config
        {
            public string lang;
            public string outDir;
            public long seed;
            public bool headless;
            public string manifest;
            public string clipName;
        }

        private static bool ClipRequested(out Config cfg)
        {
            cfg = new Config { lang = "en", seed = DefaultSeed };

            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.Equals(a, "-captureClip", StringComparison.OrdinalIgnoreCase))
                {
                    cfg.headless = true;
                }
                else if (string.Equals(a, "-lang", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    cfg.lang = args[i + 1];
                }
                else if (string.Equals(a, "-clipOut", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    cfg.outDir = args[i + 1];
                }
                else if (string.Equals(a, "-clipManifest", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    cfg.manifest = args[i + 1];
                }
                else if (string.Equals(a, "-clipName", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    cfg.clipName = args[i + 1];
                }
                else if (string.Equals(a, "-seed", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length
                         && long.TryParse(args[i + 1], out var s))
                {
                    cfg.seed = s;
                }
            }

            bool on = cfg.headless;
#if UNITY_EDITOR
            if (!on && UnityEditor.EditorPrefs.GetBool("bbs_clip", false))
            {
                on = true;
                cfg.lang = UnityEditor.EditorPrefs.GetString("bbs_clip_lang", "en");
                UnityEditor.EditorPrefs.SetBool("bbs_clip", false); // consume it
            }
#endif
            cfg.lang = cfg.lang == "de" ? "de" : "en";
            return on;
        }

        /// <summary>The built-in clip when no manifest is supplied: the original P1 proof — a slow yaw pan in space.</summary>
        private static ClipSpec DefaultClip() => new ClipSpec
        {
            name = "space_pan",
            scene = "space",
            lengthSeconds = 8f,
            fps = 30,
            hud = false,
            camera = "yaw_sweep",
            yawStart = -20f,
            yawEnd = 20f,
        };

        private ClipSpec ResolveClip()
        {
            if (string.IsNullOrEmpty(_manifestPath))
            {
                return DefaultClip();
            }

            var manifest = ClipManifest.Load(_manifestPath);
            if (manifest == null)
            {
                return null;
            }

            // No -clipName → the first clip in the manifest.
            return string.IsNullOrEmpty(_clipName) ? manifest.clips[0] : manifest.Find(_clipName);
        }

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            Application.runInBackground = true; // keep rendering even if the window loses focus mid-capture
            Screen.SetResolution(ClipWidth, ClipHeight, false);

            var shell = FindAnyObjectByType<AppShell>();
            if (shell == null)
            {
                Debug.LogError("[Clip] No AppShell in the scene.");
                Quit(1);
                yield break;
            }

            shell.Settings.Language = _lang;
            shell.LoadLocalizer();

            ClipSpec clip = ResolveClip();
            if (clip == null)
            {
                Debug.LogError($"[Clip] clip '{_clipName}' not found in manifest '{_manifestPath}'. Aborting.");
                Quit(1);
                yield break;
            }

            string clipDir = Path.Combine(ResolveOutDir(), clip.name);
            Debug.Log($"[Clip] lang={_lang} seed={_seed} clip={clip.name} scene={clip.scene} hud={clip.hud} out={clipDir}");

            // Intro clip: film the boot chain itself (studio → splash → menu → New Game → loading), so it must
            // record from the very first frame, BEFORE the menu — and it starts the world itself.
            if (string.Equals(clip.scene, "intro", StringComparison.OrdinalIgnoreCase))
            {
                yield return RecordIntroClip(clip, clipDir, shell);
                Debug.Log("[Clip] Done.");
                Quit(0);
                yield break;
            }

            // ONE world per run (proven path). Surface clips pin the world to a planet type. Enemies are forced
            // off for every capture world so a clip never gets interrupted (or fast-forwarded) by hostiles —
            // but world options only apply at CREATION, so delete any existing save first to force a fresh,
            // peaceful world (otherwise a leftover save from an earlier run keeps its old, hostile settings).
            yield return WaitForPhase(shell, ShellPhase.MainMenu, 30f);
            string worldName = WorldName + "_" + clip.name;
            LocalServerLauncher.DeleteWorld(worldName);
            shell.StartSingleplayerWorld(worldName, _seed,
                creativeUnlockAll: true, creativeAllShips: true, creativeKit: true,
                worldOptions: CaptureWorldOptions(clip));

            yield return WaitForPhase(shell, ShellPhase.InGame, WorldLoadTimeout);
            var boot = shell.CurrentBoot;
            if (boot == null || boot.Network == null)
            {
                Debug.LogError("[Clip] World did not start (bundled server missing?). Aborting.");
                Quit(1);
                yield break;
            }

            yield return WaitUntil(() => boot.WorldReady, WorldLoadTimeout);
            yield return new WaitForSecondsRealtime(ChunkSettle);

            // Reach the requested scene.
            SpaceView space = null;
            switch (clip.scene)
            {
                case "space":
                case "land":
                    boot.Network.SendEnterSpace();
                    yield return WaitUntil(() => boot.InSpace, 25f);
                    yield return new WaitForSecondsRealtime(ChunkSettle);
                    space = FindAnyObjectByType<SpaceView>();
                    break;

                case "surface":
                    bool ready = false;
                    yield return PlaceOnSurface(boot, r => ready = r);
                    if (!ready)
                    {
                        Debug.LogWarning($"[Clip] {clip.name}: no safe footing — recording the spawn view anyway.");
                    }

                    break;

                // "cockpit" (and any unknown scene): a fresh world spawns the player INSIDE the ship cockpit on
                // the surface, so the spawn view IS the cockpit — just settle, optionally open the menu, and record.
                default:
                    yield return new WaitForSecondsRealtime(ChunkSettle);
                    if (clip.openMenu)
                    {
                        var menu = FindAnyObjectByType<GameMenu>();
                        if (menu != null)
                        {
                            menu.SetMenuOpen(true);
                            Cursor.visible = false; // the OS cursor isn't in the capture anyway
                            yield return new WaitForSecondsRealtime(1f);
                        }
                    }

                    break;
            }

            if (string.Equals(clip.scene, "land", StringComparison.OrdinalIgnoreCase) && space != null)
            {
                yield return RecordLandingClip(clip, clipDir, boot, space);
            }
            else
            {
                yield return RecordClip(clip, clipDir, space);
            }

            Debug.Log("[Clip] Done.");
            Quit(0);
        }

        /// <summary>Step the on-foot player out of the spawn hull onto real, dry, solid ground near the ship
        /// (terrain-aware, like <see cref="ScreenshotDirector"/>), reporting whether a safe pose was reached.</summary>
        private IEnumerator PlaceOnSurface(GameBootstrap boot, Action<bool> done)
        {
            var pc = FindAnyObjectByType<PlayerController>();
            if (pc == null)
            {
                done(false);
                yield break;
            }

            var p = boot.PlayerPosition;
            var anchor = new Vector3(p.x, p.y, p.z);
            bool placed = false;
            float t = 0f;
            while (t < CaptureReadyTimeout)
            {
                if (!placed)
                {
                    placed = pc.PlaceForCaptureNear(anchor, pitch: 4f);
                }

                bool alive = !boot.AwaitingRespawnConfirm && boot.Health > 0f;
                if (placed && pc.IsCaptureGrounded && !pc.IsHeadUnderwater() && alive)
                {
                    yield return new WaitForSecondsRealtime(ChunkSettle);
                    done(true);
                    yield break;
                }

                t += Time.unscaledDeltaTime;
                yield return null;
            }

            done(false);
        }

        /// <summary>The capture loop: fix the frame clock, then write exactly one frame + one slice of audio per
        /// iteration. Per frame it applies the diegetic motion (ship cruise / character walk — all client-side,
        /// so deterministic under captureFramerate) and the recording-camera move, before the frame is rendered.</summary>
        private IEnumerator RecordClip(ClipSpec clip, string clipDir, SpaceView space)
        {
            string framesDir = Path.Combine(clipDir, "frames");
            Directory.CreateDirectory(framesDir);

            var pc = FindAnyObjectByType<PlayerController>();
            Camera viewCam = pc != null ? pc.Camera : Camera.main;
            Camera hudFreeSource = (clip.hud || viewCam == null) ? null : viewCam;
            if (hudFreeSource == null && !clip.hud)
            {
                Debug.LogWarning("[Clip] No view camera found — falling back to full-frame (HUD) capture.");
            }

            int fps = clip.fps > 0 ? clip.fps : 30;
            int totalFrames = Mathf.Max(1, Mathf.RoundToInt(clip.lengthSeconds * fps));
            string wavPath = Path.Combine(clipDir, "audio.wav");
            string cam = (clip.camera ?? "static").ToLowerInvariant();

            // Cockpit menu navigation: step through the requested tabs over the clip so the menu reads as
            // interactive (the OS cursor isn't captured, but the panel changes ARE visible).
            bool cycleTabs = clip.openMenu && clip.menuTabs != null && clip.menuTabs.Length > 0;
            GameMenu menu = cycleTabs ? FindAnyObjectByType<GameMenu>() : null;
            int lastSeg = -1;

            using (var writer = new ClipFrameWriter(framesDir, ClipWidth, ClipHeight, hudFreeSource))
            {
                Time.captureFramerate = fps;
                writer.StartAudio();

                for (int f = 0; f < totalFrames; f++)
                {
                    float u = totalFrames > 1 ? f / (float)(totalFrames - 1) : 0f;

                    if (menu != null)
                    {
                        int seg = Mathf.Clamp(Mathf.FloorToInt(u * clip.menuTabs.Length), 0, clip.menuTabs.Length - 1);
                        if (seg != lastSeg)
                        {
                            menu.SwitchFromUi(clip.menuTabs[seg]);
                            lastSeg = seg;
                        }
                    }

                    ApplyDiegeticMotion(clip, cam, space, pc, u);

                    Vector3 cpos = default;
                    Quaternion crot = Quaternion.identity;
                    bool overridePose = hudFreeSource != null
                        && ComputeCameraPose(cam, clip, viewCam, u, out cpos, out crot);

                    yield return new WaitForEndOfFrame();
                    writer.CaptureFrame(overridePose, cpos, crot);
                }

                Time.captureFramerate = 0;
                space?.SetFlightThrottle(-1f); // release the forced controls
                pc?.ClearWalkInput();

                string wrote = writer.FinishAudio(wavPath);
                Debug.Log($"[Clip] {clip.name}: wrote {writer.FrameCount} frames to {framesDir}; audio={(wrote ?? "(none)")}");
            }
        }

        /// <summary>Records the "land" scene as a small captured state machine: cruise toward the body, open the
        /// landing-pad chooser (the "pick a spot" beat), hold on the map, then commit a touchdown and film the
        /// descent until the ship leaves space. Always full-frame (the pad map + HUD are part of the shot), so the
        /// nebula/stars come through too. Phase lengths are framed in frames so they stay deterministic under
        /// captureFramerate.</summary>
        private IEnumerator RecordLandingClip(ClipSpec clip, string clipDir, GameBootstrap boot, SpaceView space)
        {
            string framesDir = Path.Combine(clipDir, "frames");
            Directory.CreateDirectory(framesDir);

            int fps = clip.fps > 0 ? clip.fps : 30;
            int reachCap = Mathf.RoundToInt(16f * fps);   // max time to cruise toward the planet
            int padWaitCap = Mathf.RoundToInt(6f * fps);  // max wait for the pad map to render
            int descentCap = Mathf.RoundToInt(6f * fps);  // max descent (fly-down) film time
            int surfaceTail = Mathf.RoundToInt(6f * fps); // arrival on the surface after leaving space
            string wavPath = Path.Combine(clipDir, "audio.wav");

            // Always HUD/full-frame: the landing-pad map + descent HUD are screen-space and must be in the shot.
            using (var writer = new ClipFrameWriter(framesDir, ClipWidth, ClipHeight, null))
            {
                Time.captureFramerate = fps;
                writer.StartAudio();

                // 1) Gentle approach: steer at the nearest PLANET but throttle DOWN before landing range so the
                //    planet grows in view and the ship STAYS in space (a full-speed dive auto-leaves space — the
                //    bug that killed the descent). Stop once we're ~1.6× the landing range away.
                for (int i = 0; i < reachCap; i++)
                {
                    float ratio = space.CaptureSteerToNearestBody(0.6f);
                    yield return new WaitForEndOfFrame();
                    writer.CaptureFrame();
                    if (ratio >= 0f && ratio <= 1.6f)
                    {
                        break;
                    }
                }

                space.SetFlightThrottle(0f);

                // 2) Open the planet/pad map and wait for it to render.
                bool opened = space.CaptureOpenLandMap();
                for (int i = 0; i < padWaitCap && !space.CaptureLandMapShowing; i++)
                {
                    yield return new WaitForEndOfFrame();
                    writer.CaptureFrame();
                }

                Debug.Log($"[Clip] land: opened={opened} mapShowing={space.CaptureLandMapShowing}");

                // 3) A short beat on the map, then a synthetic cursor glides onto a free pad and "clicks" it.
                for (int i = 0; i < Mathf.RoundToInt(0.7f * fps); i++)
                {
                    yield return new WaitForEndOfFrame();
                    writer.CaptureFrame();
                }

                if (space.CaptureBeginPadClick())
                {
                    int moveF = Mathf.RoundToInt(1.3f * fps);
                    for (int i = 0; i < moveF; i++)
                    {
                        space.CaptureCursorProgress(moveF > 1 ? i / (float)(moveF - 1) : 1f);
                        yield return new WaitForEndOfFrame();
                        writer.CaptureFrame();
                    }

                    for (int i = 0; i < Mathf.RoundToInt(0.4f * fps); i++) // press beat
                    {
                        yield return new WaitForEndOfFrame();
                        writer.CaptureFrame();
                    }

                    space.CaptureCommitPadClick();
                }
                else
                {
                    space.CaptureLandOnPad(0);
                }

                // 4) Film straight through the touchdown: the leave-space transition, the loading, and the arrival
                //    ON the planet surface. boot.InSpace flips almost instantly (the fly-down isn't gated by it),
                //    so we capture a FIXED window rather than polling — that's what reads as "the landing".
                int postLand = descentCap + surfaceTail;
                for (int i = 0; i < postLand; i++)
                {
                    yield return new WaitForEndOfFrame();
                    writer.CaptureFrame();
                }

                Debug.Log($"[Clip] land: post-land {postLand} frames captured, inSpace={boot.InSpace}");

                Time.captureFramerate = 0;
                space.SetFlightThrottle(-1f);
                string wrote = writer.FinishAudio(wavPath);
                Debug.Log($"[Clip] {clip.name}: wrote {writer.FrameCount} frames to {framesDir}; audio={(wrote ?? "(none)")}");
            }
        }

        /// <summary>Apply the clip's in-world motion for frame fraction <paramref name="u"/> (0..1): ship heading /
        /// pitch / throttle in space, or walk + look on the surface. All of this is client-side.</summary>
        private static void ApplyDiegeticMotion(ClipSpec clip, string cam, SpaceView space, PlayerController pc, float u)
        {
            if (space != null)
            {
                if (cam == "yaw_sweep")
                {
                    space.SetFlightYaw(Mathf.Lerp(clip.yawStart, clip.yawEnd, u));
                }

                if (Mathf.Abs(clip.shipPitch) > 0.001f)
                {
                    space.SetFlightPitch(clip.shipPitch);
                }

                if (clip.shipThrottle > 0f)
                {
                    space.SetFlightThrottle(clip.shipThrottle);
                }
            }

            if (pc != null && string.Equals(clip.scene, "surface", StringComparison.OrdinalIgnoreCase))
            {
                if (Mathf.Abs(clip.walkForward) > 0.001f || Mathf.Abs(clip.walkStrafe) > 0.001f)
                {
                    pc.SetWalkInput(clip.walkStrafe, clip.walkForward);
                }

                if (Mathf.Abs(clip.lookYawStart - clip.lookYawEnd) > 0.001f || Mathf.Abs(clip.lookPitch) > 0.001f)
                {
                    pc.SetLookAngles(Mathf.Lerp(clip.lookYawStart, clip.lookYawEnd, u), clip.lookPitch);
                }
            }
        }

        /// <summary>Compute the recording camera's pose for a cinematic move (orbit / dolly / pan) relative to the
        /// live view camera at frame fraction <paramref name="u"/>. Returns false for static / yaw_sweep (the clone
        /// just mirrors the live view).</summary>
        private static bool ComputeCameraPose(string cam, ClipSpec clip, Camera viewCam, float u,
            out Vector3 pos, out Quaternion rot)
        {
            pos = default;
            rot = default;
            if (viewCam == null)
            {
                return false;
            }

            Transform vt = viewCam.transform;
            switch (cam)
            {
                case "orbit":
                {
                    Vector3 focus = vt.position + vt.forward * clip.orbitRadius;
                    float ang = Mathf.Lerp(clip.yawStart, clip.yawEnd, u) * Mathf.Deg2Rad;
                    Vector3 offset = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * clip.orbitRadius
                                     + Vector3.up * clip.orbitHeight;
                    pos = focus + offset;
                    rot = Quaternion.LookRotation((focus - pos).normalized, Vector3.up);
                    return true;
                }

                case "dolly":
                {
                    pos = vt.position + vt.forward * (clip.dollyDistance * u);
                    rot = vt.rotation;
                    return true;
                }

                case "pan":
                {
                    pos = vt.position;
                    rot = vt.rotation * Quaternion.Euler(
                        Mathf.Lerp(clip.pitchStart, clip.pitchEnd, u),
                        Mathf.Lerp(clip.yawStart, clip.yawEnd, u),
                        0f);
                    return true;
                }

                default:
                    return false; // static / yaw_sweep
            }
        }

        /// <summary>World options for a capture world: the full Peaceful preset (PlanetEnemies + UFOs + SpaceNpcs
        /// off AND SpaceCombat=false — so nothing shoots the ship in space either), planet-pinned for surface
        /// clips. This is why a clip is never interrupted or fast-forwarded by hostiles.</summary>
        private static WorldCreationOptions CaptureWorldOptions(ClipSpec clip)
        {
            var opts = WorldCreationOptions.Peaceful();
            if (clip != null && string.Equals(clip.scene, "surface", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(clip.planet))
            {
                opts.StartPlanetType = clip.planet;
            }

            return opts;
        }

        /// <summary>Records the boot chain itself: from the first frame through the studio splash → title splash →
        /// main menu, then "clicks" New Game (starts a world) and films the loading screen until the world is
        /// ready. Always full-frame (it is all screen-space UI). The splash chain advances on scaled time
        /// (Time.deltaTime), so captureFramerate keeps it at natural pace.</summary>
        private IEnumerator RecordIntroClip(ClipSpec clip, string clipDir, AppShell shell)
        {
            string framesDir = Path.Combine(clipDir, "frames");
            Directory.CreateDirectory(framesDir);

            int fps = clip.fps > 0 ? clip.fps : 30;
            int menuWaitCap = Mathf.RoundToInt(15f * fps); // max wait for the splash chain to reach the menu
            int menuHold = Mathf.RoundToInt(2.5f * fps);   // dwell on the menu before "clicking" New Game
            int loadCap = Mathf.RoundToInt(12f * fps);     // max wait for the world to load
            int tail = Mathf.RoundToInt(1.5f * fps);       // a moment of the loaded world
            string wavPath = Path.Combine(clipDir, "audio.wav");

            using (var writer = new ClipFrameWriter(framesDir, ClipWidth, ClipHeight, null))
            {
                Time.captureFramerate = fps;
                writer.StartAudio();

                // 1) Studio + splash chain → main menu.
                for (int i = 0; i < menuWaitCap && shell.Phase != ShellPhase.MainMenu; i++)
                {
                    yield return new WaitForEndOfFrame();
                    writer.CaptureFrame();
                }

                // 2) Dwell on the menu.
                for (int i = 0; i < menuHold; i++)
                {
                    yield return new WaitForEndOfFrame();
                    writer.CaptureFrame();
                }

                // 3) "Click" New Game — start a fresh (peaceful) world.
                LocalServerLauncher.DeleteWorld(WorldName + "_intro");
                shell.StartSingleplayerWorld(WorldName + "_intro", _seed,
                    creativeUnlockAll: true, creativeAllShips: true, creativeKit: true,
                    worldOptions: CaptureWorldOptions(clip));

                // 4) Film the loading screen until the world is ready (then a short tail of the world).
                for (int i = 0; i < loadCap; i++)
                {
                    yield return new WaitForEndOfFrame();
                    writer.CaptureFrame();
                    var boot = shell.CurrentBoot;
                    if (shell.Phase == ShellPhase.InGame && boot != null && boot.WorldReady)
                    {
                        break;
                    }
                }

                for (int i = 0; i < tail; i++)
                {
                    yield return new WaitForEndOfFrame();
                    writer.CaptureFrame();
                }

                Time.captureFramerate = 0;
                string wrote = writer.FinishAudio(wavPath);
                Debug.Log($"[Clip] {clip.name}: wrote {writer.FrameCount} frames to {framesDir}; audio={(wrote ?? "(none)")}");
            }
        }

        /// <summary>Output root: explicit -clipOut, else <c>&lt;repo&gt;/marketing/clips</c> in the editor, else
        /// <c>persistentDataPath/clips</c> in a player build (the ps1 passes -clipOut).</summary>
        private string ResolveOutDir()
        {
            if (!string.IsNullOrEmpty(_outDir))
            {
                return _outDir;
            }

#if UNITY_EDITOR
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..")); // dataPath = <repo>/client/Assets
            return Path.Combine(repo, "marketing", "clips");
#else
            return Path.Combine(Application.persistentDataPath, "clips");
#endif
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
            Time.captureFramerate = 0;
#if UNITY_EDITOR
            if (_headless)
            {
                UnityEditor.EditorApplication.Exit(code);
            }
            else
            {
                UnityEditor.EditorApplication.isPlaying = false;
            }
#else
            Application.Quit(code);
#endif
        }
    }
}
