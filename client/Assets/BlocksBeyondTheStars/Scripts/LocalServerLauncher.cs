// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Diagnostics;
using System.IO;
using BlocksBeyondTheStars.Build;
using UnityEngine;
using Debug = UnityEngine.Debug;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Singleplayer hosting (Option A — see docs/developer/CLIENT_COMPLETION.md). Launches the
    /// bundled dedicated server as a child process bound to loopback, so "Singleplayer" runs
    /// the exact same authoritative server as multiplayer. The server reads CLI overrides
    /// (<c>--port/--saves/--data/--world/--name</c>); we point it at the client's synced
    /// content and a private per-user saves folder. Always stopped when leaving or quitting.
    ///
    /// Prerequisite: the server must be published into <c>StreamingAssets/server/</c> first
    /// (run <c>scripts/publish-local-server.ps1</c>).
    /// </summary>
    public sealed class LocalServerLauncher : IDisposable
    {
        public const int DefaultPort = 31550;

        private Process _process;

        public string Host { get; } = "127.0.0.1";
        public int Port { get; private set; } = DefaultPort;
        public bool IsRunning => _process != null && !_process.HasExited;

        // Set from the stdout reader thread when the server prints its "started on port" line — the
        // moment it accepts connections. Volatile: read on the main thread by GameBootstrap's retry loop.
        private volatile bool _ready;

        /// <summary>True once the spawned server has reported that it listens (its startup log line). A
        /// fresh world can take 15 s+ to generate first; this is what lets the client connect the instant the
        /// server is there instead of guessing with a fixed retry budget (see <see cref="ConnectRetryPolicy"/>).</summary>
        public bool Ready => _ready;

        // #1988: the server reports every boot pass as "[boot] 4/12 landing pads (4120 ms)". Read off the same
        // stdout reader as the ready line, this is how far the world actually is — the loading bar used to be a
        // timer with no relation to the work.
        private volatile int _bootStage;
        private volatile int _bootStages;

        /// <summary>How far the spawned server's boot has come, 0..1 — or a negative value while it has not
        /// reported a pass yet (an older server, or the very first moments). Snaps to 1 with <see cref="Ready"/>.</summary>
        public float BootProgress
        {
            get
            {
                if (_ready)
                {
                    return 1f;
                }

                int stages = _bootStages;
                return stages > 0 ? Math.Min(1f, _bootStage / (float)stages) : -1f;
            }
        }

        // #3260: the server announces each pass before running it ("[boot] > 7/12 landing_pads"); the id of the latest one
        // is what the loading screen names. Volatile: written by the stdout reader thread, read on the main thread.
        private volatile string _bootStageId;

        /// <summary>The stable id of the boot pass or step the spawned server is running now (<c>landing_pads</c>,
        /// <c>settlements</c>, … <c>ready</c>), or null while it has announced none — an older server never does, and
        /// the loading screen then keeps its flavour line (<see cref="BootStageText.StatusKey"/>).</summary>
        public string BootStageId => _bootStageId;

        // #2223: latched when the server refused the world because a newer build saved it — its log line behind
        // GameServer.SaveTooNewMarker (read off the same stdout reader as the ready line) or its exit code
        // GameServer.SaveTooNewExitCode. Volatile: set from the reader thread, read on the main thread.
        private volatile bool _saveTooNew;

        /// <summary>True when the spawned server did not open the world because it was saved by a newer version of
        /// the game — the world was left untouched, and the player needs "update the game", not "the server could
        /// not start". Stays set after <see cref="Stop"/> (the shell reads it on its way back to the menu) until
        /// the next launch is prepared.</summary>
        public bool SaveTooNew
        {
            get
            {
                NoteExit(_process); // the exit code is there the moment the process is gone; the log line may still be on its way
                return _saveTooNew;
            }
        }

        private void NoteExit(Process process)
        {
            try
            {
                if (process != null && process.HasExited && process.ExitCode == SvGameServer.SaveTooNewExitCode)
                {
                    _saveTooNew = true;
                }
            }
            catch
            {
                // No exit code to read (never started, already disposed) — the marker line is the other signal.
            }
        }

        /// <summary>Root folder holding the singleplayer save worlds (one subfolder per world).</summary>
        public static string SavesRoot => Path.Combine(AppPaths.Root, "singleplayer-saves");

        private static WorldListCache _worldList;

        /// <summary>The singleplayer world list, read on a background task (#3259): the shell prefetches it when the main
        /// menu shows, the world picker builds from it, and creating, playing or deleting a world drops it. Created on the
        /// main thread on first use (<see cref="SavesRoot"/> reads the app's data root).</summary>
        public static WorldListCache WorldList
        {
            get
            {
                string root = SavesRoot;
                if (_worldList == null || _worldList.SavesRoot != root)
                {
                    _worldList = new WorldListCache(root);
                }

                return _worldList;
            }
        }

        /// <summary>Drops the cached world list (a world was created, played or left), without touching the disk or the
        /// app paths — safe from any thread. The next menu visit reads it again.</summary>
        public static void InvalidateWorldList() => _worldList?.Invalidate();

        /// <summary>Existing singleplayer world names (subfolders that contain a world.db), newest first. Reads the disk
        /// on the calling thread — one timestamp per world, sorted on the collected values (#3259: the old comparator
        /// stat-ed both files on every comparison). The picker uses <see cref="WorldList"/> instead.</summary>
        public static string[] ListWorlds()
        {
            var worlds = WorldListCache.Scan(SavesRoot, withPlaytime: false);
            var names = new string[worlds.Count];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = worlds[i].Name;
            }

            return names;
        }

        /// <summary>Reads a world's accumulated playtime (seconds) from its <c>world.meta.json</c> sidecar.
        /// Returns -1 when no sidecar exists yet (e.g. a save from before playtime tracking) so the caller can
        /// simply omit the figure rather than show "0".</summary>
        public static long ReadWorldPlaytimeSeconds(string worldName)
            => WorldListCache.ReadPlaytimeSeconds(Path.Combine(SavesRoot, worldName));

        /// <summary>Permanently deletes a singleplayer world's save folder (its whole directory). Returns true
        /// if it was removed. Guards against empty names / paths outside the saves root.</summary>
        public static bool DeleteWorld(string worldName)
        {
            if (string.IsNullOrWhiteSpace(worldName))
            {
                return false;
            }

            try
            {
                string dir = Path.GetFullPath(Path.Combine(SavesRoot, worldName));
                // Only ever delete a direct child of the saves root (never escape it via ".." etc.).
                if (Path.GetDirectoryName(dir) != Path.GetFullPath(SavesRoot) || !Directory.Exists(dir))
                {
                    return false;
                }

                Directory.Delete(dir, recursive: true);
                _worldList?.Forget(worldName); // the picker rebuilt after the delete shows at once, without a rescan (#3259)
                Debug.Log($"Deleted singleplayer world '{worldName}'.");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to delete world '{worldName}': {e.Message}");
                return false;
            }
        }

        private ProcessStartInfo _pendingPsi;

        /// <summary>Main-thread step: validate the bundled server EXE + build the launch info. Must run on the
        /// Unity main thread (it reads <c>Application.*</c> paths). Returns false only if the EXE is missing.
        /// Pair with <see cref="LaunchPrepared"/> (which can run off the main thread).
        /// In-game multiplayer hosting reuses this path with <paramref name="maxPlayers"/> &gt; 1, an
        /// optional <paramref name="password"/>, and <paramref name="adminName"/> = the host's player name
        /// (passed as <c>--admins</c> so the host is admin even on a save where someone else joined first).</summary>
        public bool Prepare(int port = DefaultPort, int viewDistanceChunks = 0, string worldName = "singleplayer", long seed = 0,
            bool creativeUnlockAll = false, bool creativeAllShips = false, bool creativeKit = false,
            bool sandbox = false,
            string worldOptionArgs = null,
            int maxPlayers = 1, string password = null, string serverName = "Singleplayer", string adminName = null,
            bool creativeFlight = false, bool festivalsOff = false)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.LogWarning("Local world hosting is unavailable in WebGL; browsers cannot launch the bundled server process.");
            return false;
#else
            if (IsRunning)
            {
                return true;
            }

            _ready = false; // a previous run's ready flag must not pass as this one's (the shell gates the loading screen on it)
            _saveTooNew = false; // nor its refusal: this launch may open another world
            _bootStage = 0;
            _bootStages = 0;
            _bootStageId = null;

            Port = port;
            if (string.IsNullOrWhiteSpace(worldName))
            {
                worldName = "singleplayer";
            }

            bool windows = Application.platform == RuntimePlatform.WindowsPlayer
                           || Application.platform == RuntimePlatform.WindowsEditor;
            string exeName = windows
                ? "BlocksBeyondTheStars.GameServer.exe"
                : "BlocksBeyondTheStars.GameServer";
            string exe = Path.Combine(Application.streamingAssetsPath, "server", exeName);

            if (!File.Exists(exe))
            {
                Debug.LogError($"Local server executable not found at '{exe}'. " +
                               "Run scripts/publish-local-server.ps1 to bundle it.");
                return false;
            }

            string saves = Path.Combine(AppPaths.Root, "singleplayer-saves");
            string data = Path.Combine(Application.streamingAssetsPath, "data");
            // Writable folder where the in-game structure editor drops its templates; the server merges
            // them into the world-gen pools so player-built stations/towns appear in new worlds with no
            // Python merge or rebuild (StructureEditor writes <kind>_templates/<key>.json here).
            string userContent = Path.Combine(AppPaths.Root, "usercontent");
            Directory.CreateDirectory(saves);
            Directory.CreateDirectory(userContent);

            string viewArg = viewDistanceChunks > 0 ? $" --view-distance {viewDistanceChunks}" : string.Empty;
            string seedArg = seed != 0 ? $" --seed {seed}" : string.Empty;
            // Singleplayer enables free space flight + PvE space combat so it's reachable solo.
            const string spaceArgs = " --free-flight true --space-combat PvE --space-npcs Normal";
            // Voice chat on by default for the bundled host (so hosting a world for friends has working voice
            // out of the box); harmless solo. Players can still mute/disable it client-side. Dedicated servers
            // keep the opt-in default (admins set voiceChatEnabled), so this only affects the local host.
            const string voiceArg = " --voice true";
            // Solo/host convenience: guarantee a data cube next to the start landing pad (only this bundled
            // launcher sets it; dedicated/shared servers use the normal random scatter).
            const string startCubeArg = " --guarantee-start-cube true";
            // Admin cheat commands (/tp, /give, /fly …) work out of the box on the bundled host: the solo
            // player is the WorldAdmin, and on a friend-hosted world guests are still blocked by the admin
            // role. Dedicated servers keep the off default (#642 — /tp never worked in singleplayer).
            const string cheatsArg = " --admin-cheats true";
            // Ignore any config/server.json next to the bundled exe: use pure code defaults + the CLI
            // overrides here. The server writes that file on first run, and the read-only bundle folder is
            // reused across rebuilds — a leftover (e.g. an old "StartPlanet: rocky") would otherwise be read
            // verbatim and override current defaults, so a fresh build could still spawn the old start world.
            const string noConfigArg = " --no-config true";
            // "Creative" world options (only set when the player picked them at creation; the server bakes them
            // into the save on first launch, so they persist regardless of later launches).
            string creativeArgs =
                (creativeUnlockAll ? " --unlock-all-blueprints true" : string.Empty) +
                (creativeAllShips ? " --start-all-ships true" : string.Empty) +
                (creativeKit ? " --creative-kit true" : string.Empty) +
                // True sandbox (#662): free crafting + no oxygen/hunger + peaceful. Creation-only like the
                // creative args — the server bakes the mode into the save's RulesOverride.
                (sandbox ? " --game-mode Creative" : string.Empty) +
                // Free flight. Sandbox gets it from --game-mode above; the "Creative" head-start mode stays
                // Survival, so it needs saying explicitly — and it is the mode a Minecraft player picks when
                // they want to fly.
                (creativeFlight ? " --creative-flight true" : string.Empty);
            // World options (sliders at creation): non-default values only; the server bakes them into the
            // new save's rules/description, so later launches don't need to repeat them.
            string optionArgs = string.IsNullOrEmpty(worldOptionArgs) ? string.Empty : worldOptionArgs;
            // #3001: the client's global "Festivals" setting rides as the OPERATOR override, never into the save — so it
            // applies to every world this client hosts, old saves included, and switching it back on needs no lift.
            string festivalArgs = festivalsOff ? " --festivals off" : string.Empty;
            // Multiplayer hosting: a join password + the host's name as a server-side admin.
            string hostArgs =
                (string.IsNullOrEmpty(password) ? string.Empty : $" --password \"{password}\"") +
                (string.IsNullOrEmpty(adminName) ? string.Empty : $" --admins \"{adminName}\"");
            if (string.IsNullOrWhiteSpace(serverName))
            {
                serverName = "Singleplayer";
            }

            _pendingPsi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"--port {Port} --name \"{serverName}\" --world \"{worldName}\" " +
                            $"--max-players {Mathf.Max(1, maxPlayers)} --saves \"{saves}\" --data \"{data}\" --usercontent \"{userContent}\" --stdin-stop true" + viewArg + seedArg + spaceArgs + voiceArg + startCubeArg + cheatsArg + noConfigArg + creativeArgs + optionArgs + festivalArgs + hostArgs,
                WorkingDirectory = Path.GetDirectoryName(exe),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // Redirected so Stop() can close it as a graceful-shutdown signal (--stdin-stop): the server
                // drains + saves the live player position instead of dying mid-session to Process.Kill().
                RedirectStandardInput = true,
            };

            // Hand the client's baked-in feedback key to the bundled server so its crash reports and /bump
            // snapshots reach the report inbox too (ServerConfig reads BBS_CRASH_REPORT_KEY; the endpoint
            // default already points at the ReportHost). Release builds only — dev builds have an empty key
            // and keep the local-only behavior. The key is the same spam-gate the client itself ships, so
            // passing it to the child process on the same machine exposes nothing new.
            string crashKey = BugReportBuildSecrets.ApiKey;
            if (!string.IsNullOrEmpty(crashKey))
            {
                _pendingPsi.EnvironmentVariables["BBS_CRASH_REPORT_KEY"] = crashKey;
            }

            return true;
#endif
        }

        /// <summary>Spawns the prepared server process. **Thread-safe** (no Unity APIs) so it can run on a
        /// background thread — then a blocking <c>Process.Start</c> (e.g. a Windows Defender first-scan of the
        /// freshly-built EXE) never freezes the UI. Call <see cref="Prepare"/> on the main thread first.</summary>
        public bool LaunchPrepared()
        {
            if (IsRunning)
            {
                return true;
            }

            if (_pendingPsi == null)
            {
                return false;
            }

            _ready = false;
            _saveTooNew = false;
            _bootStageId = null;
            var proc = new Process { StartInfo = _pendingPsi, EnableRaisingEvents = true };
            proc.OutputDataReceived += (_, e) => OnServerLine(e.Data);
            proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.LogWarning($"[server] {e.Data}"); };

            try
            {
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                _process = proc;
                Debug.Log($"Local server started (pid {proc.Id}) on {Host}:{Port}.");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to start local server: {e.Message}");
                return false;
            }
        }

        /// <summary>One line of the spawned server's stdout (the reader thread — no Unity APIs but the log): relayed to
        /// the player log and read for the signals the loading screen waits on.</summary>
        private void OnServerLine(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            Debug.Log($"[server] {line}");

            // #2223: "[fatal] save-too-new: World '…' was saved by a newer version of the game …" — the server
            // refuses the world, leaves it untouched and exits.
            if (line.Contains(SvGameServer.SaveTooNewMarker))
            {
                _saveTooNew = true;
            }

            // #1988: "[boot] 4/12 landing pads (4120 ms)" — how far the world build has come.
            if (LoadingHandoffPolicy.TryParseBootStage(line, out int stage, out int stages))
            {
                _bootStages = stages;
                _bootStage = stage;
            }

            // #3260: "[boot] > 7/12 landing_pads" / "[boot]   > settlements" — what it is doing right now.
            if (LoadingHandoffPolicy.TryParseBootStart(line, out _, out _, out string stageId))
            {
                _bootStageId = stageId;
            }

            // GameServer.Start's final line: "Server '<name>' started on port <n>, world '<w>' (...)".
            if (!_ready && line.Contains("started on port"))
            {
                _ready = true;
            }
        }

        /// <summary>Convenience: prepare + spawn in one blocking call (kept for non-UI callers/tests).</summary>
        public bool Start(int port = DefaultPort, int viewDistanceChunks = 0, string worldName = "singleplayer", long seed = 0)
            => Prepare(port, viewDistanceChunks, worldName, seed) && LaunchPrepared();

        public void Stop()
        {
            if (_process == null)
            {
                return;
            }

            try
            {
                if (!_process.HasExited)
                {
                    // Ask the server to shut down GRACEFULLY first: closing its redirected stdin trips the
                    // --stdin-stop watcher, which drains + saves (the live player position, ship, world) on the
                    // tick thread before exiting. A hard Kill() here would lose everything since the last
                    // autosave — the player would reload at a stale spawn (e.g. floating above their ship).
                    try { _process.StandardInput.Close(); } catch { /* stdin already gone */ }

                    // Give the drain + save a moment; Kill() only as a last resort if it wedges.
                    if (!_process.WaitForExit(5000) && !_process.HasExited)
                    {
                        _process.Kill();
                        _process.WaitForExit(2000);
                    }
                }
            }
            catch
            {
                // Process already gone — nothing to do.
            }
            finally
            {
                NoteExit(_process); // #2223: keep "saved by a newer build" past the dispose — the exit code goes with the process
                _process.Dispose();
                _process = null;
                _ready = false;
            }
        }

        public void Dispose() => Stop();
    }
}
