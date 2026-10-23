// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections;
using System.IO;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BlocksBeyondTheStars.Client.Tests.PlayMode
{
    /// <summary>
    /// True end-to-end smoke test on the shipping path: launches the REAL bundled dedicated server
    /// (the published exe in StreamingAssets/server) as a child process over loopback UDP, then drives a
    /// real <see cref="NetworkClient"/> through connect → join → first chunks — exactly as the player does.
    /// Requires the published server (run scripts/publish-local-server.ps1); skipped gracefully otherwise.
    /// See docs/developer/CLIENT_TESTING.md.
    /// </summary>
    public sealed class ServerRoundtripPlayModeTests
    {
        private const int TestPort = 31599;

        /// <summary>The ceiling on the server's boot: a fresh world took 16 s on a fast machine (see
        /// <see cref="ConnectRetryPolicy"/>); the measured ~8 s of this small test world leave ample headroom.</summary>
        private const float ServerBootBudgetSeconds = 60f;

        /// <summary>The ceiling on the handshake once the server reports ready.</summary>
        private const float ConnectBudgetSeconds = 10f;

        [UnityTest]
        public IEnumerator Join_AgainstRealServer_AcceptsAndStreamsChunks()
        {
            string exeName = Application.platform is RuntimePlatform.WindowsPlayer or RuntimePlatform.WindowsEditor
                ? "BlocksBeyondTheStars.GameServer.exe"
                : "BlocksBeyondTheStars.GameServer";
            string exe = Path.Combine(Application.streamingAssetsPath, "server", exeName);
            if (!File.Exists(exe))
            {
                Assert.Ignore("Bundled server not present — run scripts/publish-local-server.ps1 first.");
                yield break;
            }

            // #2222: a join carries the fingerprint of the client's block set, and the server refuses one that differs
            // from its own. The launcher points the server at this same StreamingAssets/data, so the content loaded
            // here is the content it runs.
            string dataDir = Path.Combine(Application.streamingAssetsPath, "data");
            if (!File.Exists(Path.Combine(dataDir, "blocks.json")))
            {
                Assert.Ignore("StreamingAssets/data not present — run scripts/sync-client-libs.ps1 first.");
                yield break;
            }

            string fingerprint = ContentLoader.LoadFromDirectory(dataDir).BlockFingerprint;

            var launcher = new LocalServerLauncher();
            NetworkClient client = null;
            try
            {
                Assert.IsTrue(launcher.Start(TestPort, 1, "playmode_test", 4242), "Server process should start.");

                // #2923: the server's own ready line, not a guessed pause, decides when to dial.
                yield return WaitForServerReady(launcher);
                Assert.IsTrue(launcher.Ready,
                    $"Server should report ready within {ServerBootBudgetSeconds:0} s (process running: {launcher.IsRunning}).");

                JoinAccepted accepted = null;
                int chunks = 0;
                client = new NetworkClient();
                client.ContentFingerprint = fingerprint;
                client.JoinAccepted += m => accepted = m;
                client.ChunkReceived += _ => chunks++;

                yield return ConnectWithRedial(client);

                Assert.IsTrue(client.Connected, "Client should connect to the real server.");

                client.Join("PlayModeTester");

                float t = 0f;
                while (accepted == null && t < 10f)
                {
                    client.Poll();
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }

                Assert.IsNotNull(accepted, "Server should accept the join.");

                t = 0f;
                while (chunks == 0 && t < 10f)
                {
                    client.Poll();
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }

                Assert.Greater(chunks, 0, "Server should stream at least one chunk after join.");
            }
            finally
            {
                client?.Dispose();
                launcher.Stop();
            }
        }

        /// <summary>
        /// #2923: waits for the server's ready line ("started on port", relayed as <see cref="LocalServerLauncher.Ready"/>;
        /// the server prints it once its UDP socket is bound). A fresh world plus the process start takes ~8 s on a busy
        /// machine — the NetCodec formatter warm-up alone ~5.5 s — and the old fixed 2 s pause plus a 5 s connect budget
        /// lost that race now and then. Bounded, and a server process that exits ends the wait at once, so a wedged or
        /// crashed server still fails the test.
        /// </summary>
        private static IEnumerator WaitForServerReady(LocalServerLauncher launcher)
        {
            float started = Time.realtimeSinceStartup;
            while (!launcher.Ready && launcher.IsRunning && Time.realtimeSinceStartup - started < ServerBootBudgetSeconds)
            {
                yield return null;
            }

            Debug.Log($"[PlayMode] Waited {Time.realtimeSinceStartup - started:0.0} s for the real server (ready: {launcher.Ready}).");
        }

        /// <summary>Dials the ready server, re-dialling once a second as the game does for its bundled server
        /// (<see cref="ConnectRetryPolicy.LocalIntervalSeconds"/>), so a handshake lost while the fresh server settles costs
        /// one interval instead of the test.</summary>
        private static IEnumerator ConnectWithRedial(NetworkClient client)
        {
            client.Connect("127.0.0.1", TestPort);

            float t = 0f;
            float sinceDial = 0f;
            while (!client.Connected && t < ConnectBudgetSeconds)
            {
                client.Poll();
                t += Time.unscaledDeltaTime;
                sinceDial += Time.unscaledDeltaTime;
                if (!client.Connected && sinceDial >= ConnectRetryPolicy.LocalIntervalSeconds)
                {
                    client.Connect("127.0.0.1", TestPort);
                    sinceDial = 0f;
                }

                yield return null;
            }
        }
    }
}
