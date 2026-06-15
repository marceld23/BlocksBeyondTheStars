using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Singleplayer hosting (Option A — see docs/CLIENT_COMPLETION_PLAN.md). Launches the
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

        /// <summary>Root folder holding the singleplayer save worlds (one subfolder per world).</summary>
        public static string SavesRoot => Path.Combine(Application.persistentDataPath, "singleplayer-saves");

        /// <summary>Existing singleplayer world names (subfolders that contain a world.db), newest first.</summary>
        public static string[] ListWorlds()
        {
            if (!Directory.Exists(SavesRoot))
            {
                return Array.Empty<string>();
            }

            var dirs = new System.Collections.Generic.List<string>();
            foreach (var dir in Directory.GetDirectories(SavesRoot))
            {
                if (File.Exists(Path.Combine(dir, "world.db")))
                {
                    dirs.Add(dir);
                }
            }

            dirs.Sort((a, b) => File.GetLastWriteTimeUtc(Path.Combine(b, "world.db")).CompareTo(File.GetLastWriteTimeUtc(Path.Combine(a, "world.db"))));
            return dirs.ConvertAll(Path.GetFileName).ToArray();
        }

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
            string worldOptionArgs = null,
            int maxPlayers = 1, string password = null, string serverName = "Singleplayer", string adminName = null)
        {
            if (IsRunning)
            {
                return true;
            }

            Port = port;
            if (string.IsNullOrWhiteSpace(worldName))
            {
                worldName = "singleplayer";
            }

            bool windows = Application.platform == RuntimePlatform.WindowsPlayer
                           || Application.platform == RuntimePlatform.WindowsEditor;
            string exeName = windows ? "BlocksBeyondTheStars.GameServer.exe" : "BlocksBeyondTheStars.GameServer";
            string exe = Path.Combine(Application.streamingAssetsPath, "server", exeName);

            if (!File.Exists(exe))
            {
                Debug.LogError($"Local server executable not found at '{exe}'. " +
                               "Run scripts/publish-local-server.ps1 to bundle it.");
                return false;
            }

            string saves = Path.Combine(Application.persistentDataPath, "singleplayer-saves");
            string data = Path.Combine(Application.streamingAssetsPath, "data");
            // Writable folder where the in-game structure editor drops its templates; the server merges
            // them into the world-gen pools so player-built stations/towns appear in new worlds with no
            // Python merge or rebuild (StructureEditor writes <kind>_templates/<key>.json here).
            string userContent = Path.Combine(Application.persistentDataPath, "usercontent");
            Directory.CreateDirectory(saves);
            Directory.CreateDirectory(userContent);

            string viewArg = viewDistanceChunks > 0 ? $" --view-distance {viewDistanceChunks}" : string.Empty;
            string seedArg = seed != 0 ? $" --seed {seed}" : string.Empty;
            // Singleplayer enables free space flight + PvE space combat so it's reachable solo.
            const string spaceArgs = " --free-flight true --space-combat PvE --space-npcs Normal";
            // Solo/host convenience: guarantee a data cube next to the start landing pad (only this bundled
            // launcher sets it; dedicated/shared servers use the normal random scatter).
            const string startCubeArg = " --guarantee-start-cube true";
            // "Creative" world options (only set when the player picked them at creation; the server bakes them
            // into the save on first launch, so they persist regardless of later launches).
            string creativeArgs =
                (creativeUnlockAll ? " --unlock-all-blueprints true" : string.Empty) +
                (creativeAllShips ? " --start-all-ships true" : string.Empty) +
                (creativeKit ? " --creative-kit true" : string.Empty);
            // World options (sliders at creation): non-default values only; the server bakes them into the
            // new save's rules/description, so later launches don't need to repeat them.
            string optionArgs = string.IsNullOrEmpty(worldOptionArgs) ? string.Empty : worldOptionArgs;
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
                            $"--max-players {Mathf.Max(1, maxPlayers)} --saves \"{saves}\" --data \"{data}\" --usercontent \"{userContent}\"" + viewArg + seedArg + spaceArgs + startCubeArg + creativeArgs + optionArgs + hostArgs,
                WorkingDirectory = Path.GetDirectoryName(exe),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            return true;
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

            var proc = new Process { StartInfo = _pendingPsi, EnableRaisingEvents = true };
            proc.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.Log($"[server] {e.Data}"); };
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
                    _process.Kill();
                    _process.WaitForExit(2000);
                }
            }
            catch
            {
                // Process already gone — nothing to do.
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }

        public void Dispose() => Stop();
    }
}
