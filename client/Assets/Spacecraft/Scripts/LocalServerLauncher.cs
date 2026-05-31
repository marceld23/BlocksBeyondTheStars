using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Spacecraft.Client
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

        public bool Start(int port = DefaultPort)
        {
            if (IsRunning)
            {
                return true;
            }

            Port = port;

            bool windows = Application.platform == RuntimePlatform.WindowsPlayer
                           || Application.platform == RuntimePlatform.WindowsEditor;
            string exeName = windows ? "Spacecraft.GameServer.exe" : "Spacecraft.GameServer";
            string exe = Path.Combine(Application.streamingAssetsPath, "server", exeName);

            if (!File.Exists(exe))
            {
                Debug.LogError($"Local server executable not found at '{exe}'. " +
                               "Run scripts/publish-local-server.ps1 to bundle it.");
                return false;
            }

            string saves = Path.Combine(Application.persistentDataPath, "singleplayer-saves");
            string data = Path.Combine(Application.streamingAssetsPath, "data");
            Directory.CreateDirectory(saves);

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"--port {Port} --name Singleplayer --world singleplayer " +
                            $"--max-players 1 --saves \"{saves}\" --data \"{data}\"",
                WorkingDirectory = Path.GetDirectoryName(exe),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.Log($"[server] {e.Data}"); };
            _process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.LogWarning($"[server] {e.Data}"); };

            try
            {
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                Debug.Log($"Local server started (pid {_process.Id}) on {Host}:{Port}.");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to start local server: {e.Message}");
                _process = null;
                return false;
            }
        }

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
