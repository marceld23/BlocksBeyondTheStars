// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using UnityEngine;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The browser singleplayer host: runs the REAL authoritative <see cref="SvGameServer"/> IN-PROCESS
    /// (the same simulation the fleet and desktop run), pumped from Unity's Update loop over the
    /// in-memory <see cref="LoopbackTransport"/> — no child process, no sockets, so it works under
    /// WebGL/WASM. Persistence is the fully managed <see cref="MemoryWorldRepository"/>; every server
    /// save flushes the world as a gzip'd snapshot blob to <see cref="Application.persistentDataPath"/>
    /// (IndexedDB in the browser) and raises <see cref="BlobPersisted"/> for the optional cloud sync.
    /// Desktop singleplayer keeps its child-process model (ADR 0005); this component is the WebGL path.
    /// </summary>
    public sealed class BrowserLocalServer : MonoBehaviour
    {
        private const string WorldName = "browser";

        /// <summary>Never simulate more than this many ticks in one frame — after a long tab stall we
        /// drop the backlog instead of freezing the frame trying to catch up (singleplayer: no one else
        /// depends on the lost time).</summary>
        private const int MaxTicksPerFrame = 5;

        /// <summary>Durable-save cadence driven by THIS host (SaveAll + Flush → blob → IndexedDB/cloud).
        /// Deliberately tighter than the server's internal 5-min autosave: a browser tab can vanish
        /// without any shutdown callback, so this bounds the maximum progress loss.</summary>
        private const float DurableSaveSeconds = 120f;

        private SvGameServer _server;
        private MemoryWorldRepository _repo;
        private double _accumulator;
        private double _stepSeconds = 1.0 / 20.0;
        private float _sinceDurableSave;

        /// <summary>The in-memory wire the in-game <see cref="GameBootstrap"/> client connects through.</summary>
        public LoopbackLink Link { get; private set; }

        public bool Running => _server != null;

        /// <summary>Raised after each durable local save with the fresh snapshot blob — the Glitch cloud
        /// sync uploads exactly these bytes, so cloud and IndexedDB can never diverge.</summary>
        public event Action<byte[]> BlobPersisted;

        private const string SaveFolder = "browser-singleplayer";
        private const string BlobFile = "world.blob";
        private const string CloudMetaFile = "cloud.meta.json"; // GlitchCloudSaves' last-synced version, next to the blob

        public static string SaveDirectory => Path.Combine(Application.persistentDataPath, SaveFolder);
        public static string SaveBlobPath => Path.Combine(SaveDirectory, BlobFile);

        /// <summary>Reads the locally persisted save blob, or null when this browser has none yet.
        /// A new glitch.fun deployment starts with an empty storage folder (#1177): before declaring this
        /// browser save-less, adopt the world (and its cloud-version meta) the previous deployment left in
        /// a sibling folder — no-op off WebGL, never overwrites a blob this deployment already has.</summary>
        public static byte[] LoadLocalBlob()
        {
            try
            {
                // A pending "New world" reset (#1181) must not be undone by adopting an older deployment's copy.
                if (!File.Exists(SaveBlobPath)
                    && !BrowserWorldReset.IsPending(SaveDirectory)
                    && WebGlStorage.TryAdoptFromPreviousDeployment(Path.Combine(SaveFolder, BlobFile), Path.Combine(SaveFolder, CloudMetaFile)))
                {
                    Debug.Log("[BrowserSP] Migrated the singleplayer world from the previous deployment's storage.");
                }

                return File.Exists(SaveBlobPath) ? File.ReadAllBytes(SaveBlobPath) : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BrowserSP] Could not read the local save blob: {ex.Message}");
                return null;
            }
        }

        /// <summary>"New world" (#1181): deletes the world saved in this browser and arms the reset marker
        /// that keeps the deployment migration (#1177) and the cloud fetch from bringing the old world back
        /// until the fresh one has been persisted (<see cref="PersistBlob"/> clears it). Name, settings and
        /// the cloud-version meta stay. Call only while no world runs — the menu does, then starts
        /// singleplayer. Returns true when a local world existed and was deleted.</summary>
        public static bool ResetLocalWorld()
        {
            bool deleted = BrowserWorldReset.Reset(SaveDirectory, BlobFile);
            WebGlStorage.Sync(); // the delete + marker must be durable before the fresh world boots
            Debug.Log(deleted
                ? "[BrowserSP] Local world deleted on request — starting over."
                : "[BrowserSP] No local world to delete — starting over (cloud copy, if any, is replaced on the next save).");
            return deleted;
        }

        /// <summary>Builds and starts the in-process world. <paramref name="saveBlob"/> (from IndexedDB
        /// or the cloud) restores an existing world — its seed and rules live in the blob's metadata, so
        /// <paramref name="freshSeed"/> only matters for a brand-new world. Synchronous: the initial
        /// worldgen runs inline, so call this behind the loading screen. False = failed (logged).</summary>
        public bool StartServer(GameContent content, byte[] saveBlob, long freshSeed)
        {
            try
            {
                var paths = new SaveGamePaths(SaveDirectory, WorldName);
                _repo = new MemoryWorldRepository(paths);
                if (saveBlob is { Length: > 0 })
                {
                    _repo.ImportSnapshotBlob(saveBlob);
                }

                var config = new ServerConfig
                {
                    WorldName = WorldName,
                    Seed = freshSeed,
                    MaxPlayers = 1,
                    EnableWebSocket = false,   // the loopback IS the wire; no gateway, no sockets
                    IdleShutdownMinutes = 0,   // the tab's lifetime is the session; never self-exit
                    AiLevel = AiLevel.Off,     // the LLM backend is unreachable from a browser (internal-only)

                    // The tick runs ON the render thread here, and each first-visit chunk generates
                    // synchronously inside StreamChunks — up to ChunkStreamPerTick (16) gens in one frame,
                    // multiplied by the MaxTicksPerFrame catch-up, is the main browser-SP hitch source.
                    // A wall-clock budget keeps cheap ticks streaming at full speed but cuts generation
                    // bursts off mid-loop (rest resumes next tick, nearest-first order unchanged).
                    ChunkStreamBudgetMs = 6.0,
                };

                // Same as the native bundled host (#642): the solo player is the WorldAdmin, so admin
                // cheat commands (/tp, /give, /fly …) work out of the box in browser singleplayer too.
                config.Rules.AdminCheats = true;
                config.Rules.AllowCheatsInSurvival = true;

#if UNITY_WEBGL && !UNITY_EDITOR
                // IL2CPP/WASM cannot run MessagePack's contractless runtime formatters (the reason the
                // WebSocket edge speaks the JSON envelope). The in-process server shares this process,
                // so BOTH loopback directions must use the JSON envelope too — without this the very
                // first server broadcast (vault-loot containers during worldgen) dies in the encoder.
                // NetCodec.Decode auto-detects the envelope, so the flag alone switches the whole wire.
                BlocksBeyondTheStars.Networking.NetCodec.UseJsonEncoding = true;
#endif

                Link = new LoopbackLink();
                _server = new SvGameServer(config, content, new LoopbackServerTransport(Link), _repo,
                    new UnityGameLogger(), aiProvider: new BlocksBeyondTheStars.GameServer.NullAiMissionProvider());
                _repo.Flushed += PersistBlob; // server autosaves + shutdown both end in Flush()
                _server.Start();
                _stepSeconds = 1.0 / Math.Max(1, config.TickRate);
                Debug.Log($"[BrowserSP] In-process world up (seed {freshSeed}, save blob: {(saveBlob is { Length: > 0 } ? saveBlob.Length + " B" : "fresh world")}).");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BrowserSP] In-process server failed to start: {ex}");
                StopAndSave(save: false);
                return false;
            }
        }

        private void Update()
        {
            if (_server == null)
            {
                return;
            }

            _accumulator += Time.unscaledDeltaTime;
            int steps = 0;
            while (_accumulator >= _stepSeconds && steps < MaxTicksPerFrame)
            {
                _server.Tick(_stepSeconds);
                _accumulator -= _stepSeconds;
                steps++;
            }

            if (steps == MaxTicksPerFrame)
            {
                _accumulator = 0; // long stall (hidden tab): drop the backlog rather than freeze catching up
            }

            _sinceDurableSave += Time.unscaledDeltaTime;
            if (_sinceDurableSave >= DurableSaveSeconds)
            {
                _sinceDurableSave = 0f;
                _server.SaveNow(); // SaveAll + Flush → PersistBlob via the Flushed hook
            }
        }

        /// <summary>Browser tabs get no reliable shutdown callback — save durably the moment the tab is
        /// hidden/backgrounded (Unity raises pause on visibilitychange), so a close right after loses nothing.</summary>
        private void OnApplicationPause(bool paused)
        {
            if (paused && _server != null)
            {
                _sinceDurableSave = 0f;
                _server.SaveNow();
            }
        }

        /// <summary>Current world state as a snapshot blob (for an on-demand cloud upload), or null.</summary>
        public byte[] ExportBlob() => _repo?.ExportSnapshotBlob();

        /// <summary>Stops the world; with <paramref name="save"/> the server drains + saves synchronously
        /// first (its shutdown path flushes, which persists the blob).</summary>
        public void StopAndSave(bool save = true)
        {
            if (_server != null)
            {
                try
                {
                    if (save)
                    {
                        _server.Stop(); // no run loop → synchronous drain + SaveAll + Flush on this thread
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[BrowserSP] Stop/save failed: {ex.Message}");
                }
            }

            if (_repo != null)
            {
                _repo.Flushed -= PersistBlob;
                _repo.Dispose();
            }

            _server = null;
            _repo = null;
            Link = null;
        }

        private void PersistBlob()
        {
            if (_repo == null)
            {
                return;
            }

            try
            {
                byte[] blob = _repo.ExportSnapshotBlob();
                Directory.CreateDirectory(SaveDirectory);
                File.WriteAllBytes(SaveBlobPath, blob);
                BrowserWorldReset.ClearPending(SaveDirectory); // the fresh world is on disk — a pending reset (#1181) has held
                WebGlStorage.Sync(); // IDBFS writes are in-memory until synced — make the save durable
                Debug.Log($"[BrowserSP] World saved ({blob.Length} B).");
                BlobPersisted?.Invoke(blob);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BrowserSP] Persisting the save blob failed: {ex.Message}");
            }
        }

        private void OnDestroy() => StopAndSave();
    }

    /// <summary>Routes the in-process server's log lines into Unity's console.</summary>
    internal sealed class UnityGameLogger : BlocksBeyondTheStars.GameServer.IGameLogger
    {
        public void Info(string message) => Debug.Log($"[SP-Server] {message}");
        public void Warn(string message) => Debug.LogWarning($"[SP-Server] {message}");
        public void Error(string message) => Debug.LogError($"[SP-Server] {message}");
    }
}
