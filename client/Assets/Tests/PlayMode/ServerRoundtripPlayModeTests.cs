using System.Collections;
using System.IO;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking.Messages;
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

        [UnityTest]
        public IEnumerator Join_AgainstRealServer_AcceptsAndStreamsChunks()
        {
            string exeName = Application.platform == RuntimePlatform.WindowsEditor
                ? "BlocksBeyondTheStars.GameServer.exe"
                : "BlocksBeyondTheStars.GameServer";
            string exe = Path.Combine(Application.streamingAssetsPath, "server", exeName);
            if (!File.Exists(exe))
            {
                Assert.Ignore("Bundled server not present — run scripts/publish-local-server.ps1 first.");
                yield break;
            }

            var launcher = new LocalServerLauncher();
            NetworkClient client = null;
            try
            {
                Assert.IsTrue(launcher.Start(TestPort, 1, "playmode_test", 4242), "Server process should start.");

                // Give the server a moment to bind its UDP socket.
                yield return new WaitForSeconds(2f);

                JoinAccepted accepted = null;
                int chunks = 0;
                client = new NetworkClient();
                client.JoinAccepted += m => accepted = m;
                client.ChunkReceived += _ => chunks++;

                client.Connect("127.0.0.1", TestPort);

                float t = 0f;
                while (!client.Connected && t < 5f)
                {
                    client.Poll();
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }

                Assert.IsTrue(client.Connected, "Client should connect to the real server.");

                client.Join("PlayModeTester");

                t = 0f;
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
    }
}
