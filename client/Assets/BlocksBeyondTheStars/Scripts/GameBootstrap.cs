// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Entry point MonoBehaviour. Loads data-driven content, connects to a server, and turns
    /// incoming chunk/state messages into the rendered world. Attach to a single GameObject
    /// in the scene and assign a material for chunk meshes.
    /// </summary>
    public sealed partial class GameBootstrap : MonoBehaviour
    {
        [Header("Connection")]
        public string Host = "127.0.0.1";
        public int Port = 31415;
        public string PlayerName = "Pilot";
        public string Password = "";

        /// <summary>In-process singleplayer wire (browser builds): when set, the client talks to the
        /// in-process <see cref="BrowserLocalServer"/> over this in-memory link — no sockets at all.
        /// Null for every networked join. Not serialized: handed over by WorldRig at build time.</summary>
        [System.NonSerialized]
        public BlocksBeyondTheStars.Networking.Transport.LoopbackLink Loopback;

        /// <summary>Per-install name-verification secret (see <see cref="ClientSettings.PlayerToken"/>).</summary>
        public string Token = "";

        /// <summary>Control-plane join grant for OFFICIAL hosted worlds (short-lived HMAC token from the
        /// worlds portal). Empty everywhere else — singleplayer, LAN and self-hosted joins don't use it.</summary>
        public string HostedToken = "";

        /// <summary>Portal base URL + bearer session, carried into the game so in-game portal actions
        /// (the "report player" button) work while on a hosted world. Empty otherwise.</summary>
        public string PortalUrl = "";
        public string PortalSession = "";

        /// <summary>The joined hosted world's id (12 hex chars), attached to player reports so the
        /// operators know which world's logs to check. Empty outside official hosted worlds.</summary>
        public string HostedWorldId = "";

        /// <summary>The player's chosen render distance in chunks, forwarded to the server in the JoinRequest so a
        /// remote/dedicated host streams terrain at this radius (not just the local fog). 0 = let the server decide.</summary>
        public int ViewDistanceChunks = 0;

        /// <summary>While this client hosts the server in-game: the "ip:port" friends join over the LAN
        /// (announced in chat + as a toast). Empty when joining a remote server or in singleplayer.</summary>
        public string HostInfo = "";

        /// <summary>The active locale for all key-based text. Deliberately NOT serialized: a scene-baked value
        /// would override whatever <c>WorldRig</c> assigns from the player's settings (the Launcher.unity
        /// PlayerName trap). Every text surface resolves through <see cref="Localizer"/> / locale-code maps,
        /// so all of <see cref="GameLocale"/> is supported — the old DE/EN <c>German</c> bool is gone.</summary>
        [System.NonSerialized]
        public GameLocale Locale = GameLocale.English;

        [Header("Rendering")]
        public Material ChunkMaterial;
        public Material ChunkMaterialTransparent; // submesh 1: see-through glass + energy fields
        public Material ChunkMaterialPaint;       // submesh 2: player-painted design faces (paint atlas, #819)

        // Our avatar colours (packed 0xRRGGBB), set by WorldRig; sent to the server on join.
        public int SkinRgb = 0xD9AE8C;
        public int TorsoRgb = 0x3372CC;
        public int ArmRgb = 0x3372CC;
        public int LegRgb = 0x40404F;

        /// <summary>Our custom pixel face (16×16 palette-index string; empty = default), set by WorldRig and
        /// sent to the server on join so other players see it. Kept here so the edit path can re-send it.</summary>
        public string FacePixels = "";

        /// <summary>Our avatar body paintings (#874), indexed by BodyPaint part (torso/arms/legs/helmet);
        /// empty = unpainted. Set by WorldRig, sent per painted part on join, updated by the edit path.</summary>
        public string[] BodyPaintPixels = { "", "", "", "" };

        // Our ship hull colour (packed 0xRRGGBB), set by WorldRig; sent on join and read by the flight view
        // to tint the ship (item 32). Default = the steel tint the hull used before hull colours existed.
        public int HullRgb = 0xD1D6E0;

        public GameContent Content { get; private set; }
        public Localizer Localizer { get; private set; }

        /// <summary>Switches the LIVE world's language. The world keeps its own <see cref="Localizer"/>
        /// (created once at bootstrap from the <see cref="Locale"/> snapshot), so a language change in the
        /// in-game pause menu's settings would otherwise only show after leaving and re-entering the world
        /// (Severin playtest follow-up). Reuses the already-loaded <see cref="Content"/> — no content reload,
        /// which also keeps this cheap on WebGL where StreamingAssets live in a remote-fetched cache. The HUD
        /// re-reads its keys every frame; windows (Tab menu, dialogs) pick the language up on their next open.</summary>
        public void SetLanguage(GameLocale locale)
        {
            Locale = locale;
            if (Content != null)
            {
                Localizer = Content.CreateLocalizer(locale);
            }
        }
        public NetworkClient Network { get; private set; }
        public ClientWorld World { get; private set; }
        public BlockTextureAtlas Atlas { get; private set; }

        private WaterProbe _waterProbe;

        /// <summary>True when the cell holding <paramref name="point"/> is under water — plain water, or a plant, ladder
        /// or building form the water surrounds (#1902). The underwater wash, the audio muffle and swimming all ask
        /// this, with the same rule as the server's oxygen drain.</summary>
        public bool IsWaterAt(Vector3 point)
        {
            if (World == null || Content == null)
            {
                return false;
            }

            if (_waterProbe == null || !_waterProbe.IsFor(World, Content))
            {
                _waterProbe = new WaterProbe(World, Content);
            }

            return _waterProbe.IsWet(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y), Mathf.FloorToInt(point.z));
        }

        /// <summary>Runtime atlas of player-painted block designs (#819) — fed from the server's design
        /// registry (join list + live additions/wipes); the mesher samples it via a thread-safe snapshot.</summary>
        public PaintDesignAtlas PaintAtlas { get; private set; }

        /// <summary>This save's player-designed forms (#844) — the geometry counterpart of the paint atlas.</summary>
        public CustomShapeRegistry CustomShapes { get; } = new CustomShapeRegistry();

        // Per-species flora tints for the current world (seed + location → one colour per flora block),
        // resolved by the chunk mesher into TEXCOORD2.yzw. Rebuilt on join and on every world change.
        private long _worldSeed;

        /// <summary>The world seed (from JoinAccepted) — drives the per-world flora colours, also for
        /// OTHER bodies seen from orbit (FloraTints is a pure function of seed + location + species).</summary>
        public long WorldSeed => _worldSeed;

        /// <summary>Whether this save was created with continents (#704, from JoinAccepted) — the local
        /// preview generators (minimap/orbit bakes) must apply the same gate as the server.</summary>
        public bool TerrainContinents { get; private set; }

        /// <summary>The terrain generation this save was created with (#1644, from JoinAccepted) — the local
        /// preview generators apply it like <see cref="TerrainContinents"/>.</summary>
        public int TerrainGeneration { get; private set; }

        /// <summary>Whether this save has lava-core volcanoes (#1631, from JoinAccepted) — the planet map previews apply
        /// it like <see cref="TerrainContinents"/> (#2172).</summary>
        public bool TerrainLavaCoreVolcanoes { get; private set; }

        /// <summary>Total seconds this world has been played (from JoinAccepted, server-accumulated). The live
        /// in-game total adds <see cref="SessionSeconds"/> on top so the HUD readout ticks up in real time.</summary>
        public long CumulativePlaytimeSeconds { get; private set; }

        // Real wall-clock baseline for this in-game session, captured on join (-1 until then). Uses
        // realtimeSinceStartup so it's immune to pause / timescale and never goes backwards.
        private float _sessionStartRealtime = -1f;

        /// <summary>Seconds elapsed in the current in-game session (real wall-clock, 0 before joining).</summary>
        public float SessionSeconds => _sessionStartRealtime < 0f ? 0f : Time.realtimeSinceStartup - _sessionStartRealtime;

        /// <summary>Live total playtime = the server's saved cumulative seconds plus this session so far.</summary>
        public long TotalPlaytimeSeconds => CumulativePlaytimeSeconds + (long)SessionSeconds;

        /// <summary>The player's current world position and heading, written by the controller for the HUD.</summary>
        public Vector3 PlayerPosition;
        public float PlayerYaw;

        /// <summary>The player's vertical speed (blocks/s, negative = falling), written by the controller each on-foot
        /// frame. The chunk pipeline cooks colliders AHEAD of a fast descent with it (#1583).</summary>
        public float PlayerVerticalVelocity;

        /// <summary>The held hotbar item places a rotatable block (a crafted shape or furniture) — written by
        /// the controller each on-foot frame; the HUD appends its "R — rotate" control hint while set (#863).</summary>
        public bool HoldingRotatableBlock;

        /// <summary>An optional world-map waypoint (XZ); the HUD compass points to it when set.</summary>
        public Vector3? Waypoint;

        /// <summary>World-X span over which the local time-of-day shifts a full cycle. This is the world
        /// circumference (X is a wrapping longitude), so one walk around the world is exactly one day and
        /// the planet has a real day/night terminator: a player far east can be in daylight while one far
        /// west is in night, on the same world.</summary>
        /// <summary>This world's walkable circumference (longitude wrap + day span). Sized per body (asteroids
        /// small, planets large) — sent by the server in <see cref="WorldEnvironment"/>; used for every X wrap.</summary>
        public int Circumference { get; private set; } = WorldConstants.Circumference;

        /// <summary>Time-of-day at the player's position — the server's global day fraction shifted by the
        /// player's longitude (world X), wrapped to 0..1. Drives the sky + HUD clock, so two players at
        /// different X see different times (one's day side, the other's night side). Aboard a station there is no
        /// longitude (#1869): the station clock is the local clock, the same one its crew sleeps by and its deck lights
        /// dim to — walking across the deck no longer moves the HUD clock.</summary>
        public float LocalTimeOfDay
            => Mathf.Repeat((Environment != null ? Environment.TimeOfDay : 0.5f)
                            + (string.IsNullOrEmpty(StationName) ? PlayerPosition.x / Circumference : 0f), 1f);

        /// <summary>
        /// Maps an authoritative (canonical) world X to the Unity scene X nearest the player. World-X is a
        /// wrapping longitude; the player's transform runs unbounded as it laps the world, so every object
        /// (chunks, remote players, creatures, NPCs) is drawn at the copy closest to the player. In the near
        /// field this just returns the canonical X; only objects across the seam are shifted by ±Circumference
        /// (and those that would flip are beyond view distance anyway, so the flip is never seen).
        /// </summary>
        public float SceneX(double worldX)
            => (float)(PlayerPosition.x + WorldConstants.WrapDeltaX(worldX - PlayerPosition.x, Circumference));

        /// <summary>Maps an authoritative world Z to the scene Z nearest the player — the latitude twin of
        /// <see cref="SceneX"/> (round worlds: Z wraps at the latitude period like X at the circumference).</summary>
        public float SceneZ(double worldZ)
            => (float)(PlayerPosition.z + WorldConstants.WrapDeltaZ(worldZ - PlayerPosition.z, Circumference));

        /// <summary>Convenience: a full world position mapped to the nearest scene position (X and Z wrap).</summary>
        public Vector3 ScenePos(float worldX, float worldY, float worldZ)
            => new Vector3(SceneX(worldX), worldY, SceneZ(worldZ));

        /// <summary>Local client settings (wired by <see cref="WorldRig"/>) — read live for the auto-stow comfort
        /// option. Never authoritative; the server decides what actually moves.</summary>
        public ClientSettings Settings;

        /// <summary>True while the chat input is focused; the player controller pauses look/move.</summary>
        public bool ChatTyping;

        /// <summary>True while the space view owns the camera (on-foot control is frozen).</summary>
        public bool SpaceViewActive;

        /// <summary>Set when the server refused our join (wrong password, name in use/verified, full…).
        /// AppShell watches this and returns to the menu showing the reason.</summary>
        public string JoinRejectedReason { get; private set; } = string.Empty;

        /// <summary>Set when the transport never connected after every retry (the bundled singleplayer
        /// server never came up, or a mistyped multiplayer host/port). AppShell watches this and returns
        /// to the menu showing the reason — without it the player would sit in a chunk-less void once
        /// the loading veil times out, with no hint anything went wrong (#409).</summary>
        public string ConnectFailedReason { get; private set; } = string.Empty;

        /// <summary>Bumped each time the world is rebuilt (travel); the player re-snaps to the new spawn.</summary>
        public int WorldEpoch { get; private set; }

        private bool _joinSent;
        private bool _joinSendFailureLogged;
        private float _retryTimer;
        private int _retries;
        private float _connectWaited;   // seconds since the initial dial (the local-server ceiling)
        private bool _localReadyDialed; // the launcher's "started on port" signal has been acted on

        /// <summary>The bundled server this client spawned (set by WorldRig from the shell), or null for a
        /// remote host. Switches the connect loop to the patient local budget — see <see cref="ConnectRetryPolicy"/>.</summary>
        public LocalServerLauncher LocalServer;

        private void Start()
        {
            if (!StreamingAssetsCache.IsReady && !StreamingAssetsCache.UsesRemoteStreamingAssets)
            {
                StreamingAssetsCache.EnsureLocalReady();
            }

            if (!StreamingAssetsCache.IsReady)
            {
                Debug.LogError("Bundled content is not ready; aborting game bootstrap.");
                enabled = false;
                return;
            }

            string dataDir = StreamingAssetsCache.DataDir;
            try
            {
                // #1522: the shell parsed this exact data directory at startup — reuse that snapshot (one
                // GameContent per process) instead of parsing ~4 MB of JSON again on every world entry.
                var shellContent = FindAnyObjectByType<AppShell>();
                Content = shellContent != null && shellContent.Content != null
                          && string.Equals(shellContent.ContentDataDir, dataDir, System.StringComparison.Ordinal)
                    ? shellContent.Content
                    : ContentLoader.LoadFromDirectory(dataDir, eagerLocale: Locale);
            }
            catch (System.Exception e)
            {
                // A malformed data file must not strand the player in a half-built world (#422 M8):
                // surface it through the same bail-out AppShell already watches for failed connects —
                // back to the menu with a reason. The shell's own content snapshot is still in memory,
                // so its localizer is available for the message.
                Debug.LogError($"Content re-load on world entry failed from '{dataDir}': {e}");
                var shell = FindAnyObjectByType<AppShell>();
                ConnectFailedReason = shell != null && shell.Localizer != null
                    ? shell.L("ui.content.load_failed")
                    : "Game content failed to load — the install may be damaged.";
                enabled = false;
                return;
            }

            Localizer = Content.CreateLocalizer(Locale);
            World = new ClientWorld();
            _chunkArrays.Clear(); // #1555: a new world — the old arrays are unreachable and simply garbage
            _retiredChunkArrays.Clear();
            // Index dedicated light blocks (+ placed glow blocks) as coloured light sources for the mesher.
            World.SetBlockLightResolver(id => ChunkMesher.BlockLightColor(Content, new BlockId(id), 0));

            // Procedurally generate the block texture atlas and a material that samples it
            // (M27). Falls back to whatever WorldRig assigned if the shader is missing.
            Atlas = BlockTextureAtlas.Acquire(Content); // #1523: one atlas per process, shared with menu/intro/editors

            // #1521: compile the world shaders' variants now, behind the opaque loading veil, instead of one
            // hitch each when the weather/sun/preset first switches a keyword mid-game. Once per process.
            int warmedVariants = ShaderPrewarm.WarmUp();
            if (warmedVariants > 0)
            {
                Debug.Log($"[Shaders] Prewarmed {warmedVariants} variants.");
            }
            var atlasShader = Shader.Find("BlocksBeyondTheStars/BlockAtlas");
            if (atlasShader != null)
            {
                ChunkMaterial = new Material(atlasShader) { mainTexture = Atlas.Texture };
                Atlas.BindNormals(ChunkMaterial); // per-pixel normal mapping; stays bound across a repaint (#1952)
                Atlas.Changed += OnAtlasRepainted;
                _animationVersionSeen = Atlas.AnimationVersion; // what is meshed from now on already knows this layout (#1957)

                // Alpha-blended material for the see-through submesh (glass viewports + energy fields).
                var transparentShader = Shader.Find("BlocksBeyondTheStars/BlockAtlasTransparent");
                if (transparentShader != null)
                {
                    ChunkMaterialTransparent = new Material(transparentShader) { mainTexture = Atlas.Texture };
                }

                // Paint material (#819): the SAME opaque atlas shader over the paint-design atlas, with a
                // flat normal map (the Sobel normals of the block atlas would map garbage onto design slots).
                PaintAtlas = new PaintDesignAtlas();
                ChunkMaterialPaint = new Material(atlasShader) { mainTexture = PaintAtlas.Texture };
                ChunkMaterialPaint.SetTexture("_NormalTex", PaintAtlas.FlatNormal);
            }
            else
            {
                Atlas.Release(); // drop this owner's reference — nothing here will ever sample it
                Atlas = null; // no atlas shader → fall back to the flat palette + vertex-colour material
            }

            // Held blocks show their REAL atlas tile in the hand (graphics quick-win): resolve a block key
            // to the atlas texture + its tile UV rect; HeldItem falls back to the flat tint without it.
            HeldItem.BlockTileResolver = key =>
                Atlas != null && Content?.GetBlock(key) is { } b && b.NumericId.Value != 0
                    ? (Atlas.Texture, Atlas.TileUv(b.NumericId.Value))
                    : null;

            // A tool's own look comes from the item data (#1962).
            HeldItem.ModelResolver = key => Content?.GetItem(key)?.HeldModel;

            // #2534: the held vehicle / plant miniatures mesh with the world's own atlas + chunk materials.
            HeldItem.MeshKitResolver = () => (Content, Atlas, ChunkMaterial, ChunkMaterialTransparent);
            // #2776: and a held painted block shows its design from the paint atlas.
            HeldItem.PaintKitResolver = () => (PaintAtlas, ChunkMaterialPaint);

            // The empty-slot hand (#1033) wears a glove in the player's suit arm colour — and the player's
            // own arm painting from the appearance editor, so first person matches third person (#1427).
            HeldItem.HandTintResolver = () => Settings?.ArmColor;
            HeldItem.HandPaintResolver = () => Settings?.ArmPixels;

            if (Loopback != null)
            {
                // In-process singleplayer: the server lives in THIS process (BrowserLocalServer) — the
                // in-memory loopback pair replaces every socket, on WebGL and in the editor alike.
                Network = new NetworkClient(new BlocksBeyondTheStars.Networking.Transport.LoopbackClientTransport(Loopback));
                Network.ContentFingerprint = Content.BlockFingerprint; // #2222: the block set this client decodes chunks with
            }
            else if (string.Equals(Host, AppShell.BrowserLoopbackHost, System.StringComparison.OrdinalIgnoreCase))
            {
                // The shell asked for the in-process world but handed over no wire — the browser host
                // failed to start (or was torn down mid-boot). "loopback" is a placeholder, not a
                // reachable address, so dialing it would burn ~12 s of retries and then blame the
                // player's server address; say what actually happened instead (#771).
                Debug.LogError("Browser singleplayer was requested without a running in-process server — aborting the world entry.");
                var shell = FindAnyObjectByType<AppShell>();
                ConnectFailedReason = shell != null && shell.Localizer != null
                    ? shell.L("ui.sp.browser_failed")
                    : "The world could not be started in this browser.";
                enabled = false;
                return;
            }
            else
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                Network = new NetworkClient(new BrowserWebSocketClientTransport());
#else
                Network = new NetworkClient();
#endif
                // #2222: the fingerprint of the block set this client decodes chunks with travels in the join; a server
                // with another block set refuses the join, and this client refuses a server that answers with another.
                Network.ContentFingerprint = Content.BlockFingerprint;
            }
            Network.JoinAccepted += m =>
            {
                _inventoryBaselined = false; // next InventoryUpdate is the join baseline, not a pickup (#745)
                PickupGains.Clear();
                ResearchAvailable.Clear();
                AchievementsUnlocked.Clear(); // #2597
                LocalPlayerId = m.PlayerId;
                LocationName = string.IsNullOrEmpty(m.SystemName)
                    ? m.PlanetName
                    : $"{m.SystemName} · {m.PlanetName}";
                Debug.Log($"Joined as {m.PlayerId} at {LocationName} (seed {m.WorldSeed}).");
                LoadingPlanetType = m.PlanetType;
                _worldSeed = m.WorldSeed;
                TerrainContinents = m.TerrainContinents; // #704: previews must match the server's gate
                TerrainGeneration = m.TerrainGeneration; // #1644: same for the landform generation
                TerrainLavaCoreVolcanoes = m.TerrainLavaCoreVolcanoes; // #2172: and the save's volcanoes
                CumulativePlaytimeSeconds = m.CumulativePlaytimeSeconds; // saved world total; session ticks on top
                if (_sessionStartRealtime < 0f)
                {
                    _sessionStartRealtime = Time.realtimeSinceStartup; // start the session clock on first join
                }

                RebuildFloraTints(); // per-species flora colours for this world (seed + location)
                WorldReady = false;          // hold the loading overlay until we settle onto the first world
                _lastChunkArrivalTime = Time.time; // reset the view-settle gate so it can't pass before this world streams (#390)
                WorldLoadStarted?.Invoke();
            };
            Network.JoinRejected += m =>
            {
                Debug.LogWarning($"Join rejected: {m.Reason}");
                JoinRejectedReason = string.IsNullOrEmpty(m.Reason) ? "Join rejected." : m.Reason;
            };
            Network.ChunkReceived += OnChunk;

            // Player-painted block designs (#819): the server pushes the full registry before any chunk on
            // join and every new design BEFORE the BlockChanged referencing it (ordered stream), so a normal
            // remesh always finds the bitmap. Only a WIPE (empty pixels, moderation) invalidates already-built
            // chunks — rare enough that re-meshing every loaded chunk is fine.
            Network.PaintDesignListReceived += m =>
            {
                if (PaintAtlas == null || m.Ids == null || m.Pixels == null)
                {
                    return;
                }

                for (int i = 0; i < m.Ids.Length && i < m.Pixels.Length; i++)
                {
                    PaintAtlas.Register(m.Ids[i], m.Pixels[i],
                        m.Owners != null && i < m.Owners.Length ? m.Owners[i] : string.Empty);
                }
            };
            // Player-designed forms (#844): the same contract as the paint registry — the full list lands
            // before the first chunk, single forms before anything can reference them. A wipe (empty voxels)
            // is the one case that invalidates already-built chunks, so those fall back to plain cubes.
            Network.CustomShapeListReceived += m =>
            {
                CustomShapes?.RegisterAll(m.Ids, m.Voxels, m.Names, m.Owners);
            };
            // World textures (#1959): pages after the join become ONE batch (one atlas repaint), a publish or wipe
            // while playing a batch of one. The atlas repaints in place, so no chunk has to re-mesh for it.
            Network.WorldTextureListReceived += m => ApplyWorldTextures(_worldTextureInbox.Accept(m));
            Network.WorldTextureReceived += m => ApplyWorldTextures(_worldTextureInbox.Accept(m));
            Network.CustomShapeReceived += m =>
            {
                if (CustomShapes == null)
                {
                    return;
                }

                CustomShapes.Register(m.Id, m.Voxels, m.Name, m.Owner);
                foreach (var c in _chunkObjects.Keys)
                {
                    _dirty.Add(c); // a new or wiped form changes geometry, so loaded chunks must re-mesh
                }

                ShapeIconFactory.ClearCache(); // icons are keyed by (tile, form) — a reused id must not serve stale art
                HeldMiniatures.ForgetBlocks(); // #2776: so are the held blocks' meshes
                if (BlocksBeyondTheStars.Shared.State.ItemKey.Shape(ItemInSlot(SelectedHotbarSlot) ?? string.Empty) == m.Id)
                {
                    HeldItemDirty = true; // … and the block in hand is this very form
                }
            };
            Network.PaintDesignReceived += m =>
            {
                if (PaintAtlas == null)
                {
                    return;
                }

                PaintAtlas.Register(m.Id, m.Pixels, m.Owner);
                if (string.IsNullOrEmpty(m.Pixels))
                {
                    foreach (var c in _chunkObjects.Keys)
                    {
                        _dirty.Add(c); // wiped design: rebuild loaded chunks so its faces fall back to the block texture
                    }

                }

                // #2776: the painted block in hand may have been waiting for this design (anyone's other design leaves the
                // hand alone — a rebuild would cut a swing or a draw short).
                if (BlocksBeyondTheStars.Shared.State.ItemKey.Design(ItemInSlot(SelectedHotbarSlot) ?? string.Empty) == m.Id)
                {
                    HeldItemDirty = true;
                }
            };
            Network.BlockChanged += OnBlockChanged;
            Network.PlayerStateUpdated += OnPlayerState;
            Network.InventoryUpdated += Bio.OnInventory; // #2201: the sample case rides the inventory update
            Network.InventoryUpdated += m =>
            {
                // Pickup feed (#745): surface what this update ADDED. The first update after a join is
                // the baseline (the whole inventory is "new" then, not a pickup), and gains while a
                // menu is open are dropped on purpose — craft/trade results have their own in-menu
                // feedback, and re-announcing them after the menu closes would double up.
                bool baseline = !_inventoryBaselined;
                if (baseline)
                {
                    _inventoryBaselined = true;
                }
                else if (!MenuOpen)
                {
                    foreach (var (item, gained) in InventoryDiff.Gains(Personal, m.Personal))
                    {
                        PickupGains.Enqueue((item, gained));
                    }
                }

                Personal = m.Personal;
                Equipment = m.Equipment ?? System.Array.Empty<NetItemStack>(); // #2110
                if (m.PersonalSlotCount > 0)
                {
                    PersonalSlots = m.PersonalSlotCount;
                }

                RefreshSuitStats();
                Cargo = m.Cargo;
                CargoSlots = m.CargoSlotCount;
                if (!m.BlueprintsUnchanged) // #1535: an omitted list means "same as before", not "none"
                {
                    UnlockedBlueprints = new System.Collections.Generic.HashSet<string>(m.UnlockedBlueprints ?? System.Array.Empty<string>());
                }

                // Research toast (#763): a knowledge gain that crosses a still-locked blueprint's
                // threshold makes it newly researchable — queue it for the HUD. Threshold-only on
                // purpose (prerequisites may still be missing): "you now know enough for X" is true
                // either way, and advertising deeper tree nodes is the point. The join snapshot
                // baselines silently, and a huge jump (admin grant) is capped so the HUD doesn't
                // announce toasts for minutes.
                if (!baseline && m.KnowledgePoints > Knowledge && Content != null)
                {
                    var crossed = new List<Shared.Definitions.BlueprintDefinition>();
                    foreach (var bp in Content.Blueprints.Values)
                    {
                        if (bp.KnowledgeCost > Knowledge && bp.KnowledgeCost <= m.KnowledgePoints
                            && !UnlockedBlueprints.Contains(bp.Key))
                        {
                            crossed.Add(bp);
                        }
                    }

                    crossed.Sort((a, b) => a.KnowledgeCost.CompareTo(b.KnowledgeCost));
                    for (int i = 0; i < crossed.Count && ResearchAvailable.Count < 6; i++)
                    {
                        ResearchAvailable.Enqueue(crossed[i].Key);
                    }
                }

                Knowledge = m.KnowledgePoints;
            };
            // The server's answer to our pause intent (#908). Honours Allowed implicitly: a declined hold comes
            // back with Paused = false, so the client keeps simulating exactly as it did before.
            Network.PauseStateReceived += m =>
            {
                _worldClock.SetPaused(m.Paused);
                PauseHoldingPlayers = m.HoldingPlayers;
                PauseJoinedPlayers = m.JoinedPlayers;
                PauseWaitingFor = m.WaitingFor ?? string.Empty;
            };
            Network.ShipPlacementReceived += m => ShipPosition = new Vector3(m.X, m.Y, m.Z);
            Network.ShipStationsReceived += m => Stations = m.Stations;
            Network.StationsInReachReceived += m =>
            {
                StationsAvailable.Clear();
                foreach (var s in m.Available ?? System.Array.Empty<string>())
                {
                    StationsAvailable.Add(s);
                }

                ResearchOk = m.ResearchOk;
                ShipBuildOk = m.ShipBuildOk;
                StationsKnown = true;
                StationsInReachChanged?.Invoke();
            };
            Network.StationLocationReceived += m =>
            {
                LastStationLocation = m;
                StationLocated?.Invoke(m);
            };
            Network.PlanetPoisReceived += m => PlanetPois = m.Pois;
            Network.BeaconsReceived += m => Beacons = m.Beacons ?? System.Array.Empty<NetBeacon>();
            Network.BeamsReceived += m => Beams = m.Beams ?? System.Array.Empty<NetBeam>();
            Crystal.FrameOrigin = frame => LandedShips.TryGetValue(frame, out var parked) ? parked.Origin : (Vector3i?)null;
            Crystal.Circumference = () => Circumference;
            Network.CrystalNetsReceived += Crystal.OnNets;
            Network.CrystalDevicesReceived += Crystal.OnDevices;
            Network.CrystalDeviceDeltaReceived += Crystal.OnDelta; // #2267
            Network.LiftsReceived += m =>
            {
                Lifts = m.Lifts ?? System.Array.Empty<NetLift>();
                LiftsReceivedAt = Time.unscaledTime;
            };
            LandedShipsChanged += () =>
            {
                if (Crystal.HasFrames)
                {
                    Crystal.Recompose(); // #2268: a ship arrived, moved or left — its net follows it
                }
            };
            Network.BioBookReceived += Bio.OnBook; // #2203: the research book
            Network.BioLabResultReceived += m => { LastBioLabResult = m; BioLabResultCount++; };
            Network.SoundFxReceived += m => ClientAudio.Instance?.Fx(m, ScenePos(m.X, m.Y, m.Z)); // #2052
            Network.BeamTeleportedReceived += m => RespawnTarget = new Vector3(m.X, m.Y, m.Z); // snap the body onto the destination pad
            Network.BasesReceived += m => Bases = m.Bases ?? System.Array.Empty<NetBase>();
            Network.FactoriesReceived += m => Factories = m.Factories ?? System.Array.Empty<NetFactory>();
            Network.LandingPadsReceived += m =>
            {
                LandingPads = m.Pads ?? System.Array.Empty<NetLandingPad>();
                LandingPadsBody = m.BodyId ?? string.Empty;
                LandingPadsTimeOfDay = m.TimeOfDay;
                LandingPadBook.Apply(m); // #2740: per body — the world map reads the active body's list from here
            };
            Network.SystemWeatherReceived += m => SystemWeather.Apply(m, Time.realtimeSinceStartupAsDouble); // #2173
            Network.StarMapReceived += m => { StarMap = m; RebuildWikiState(); };
            Network.ExploredMapReceived += m => { if (!string.IsNullOrEmpty(m.BodyId)) ExploredMaps[m.BodyId] = m; };
            Network.DataCubesReceived += m => DataCubes = m.Cubes ?? System.Array.Empty<NetDataCube>();
            Network.GameUnlocksReceived += m =>
            {
                var incoming = new System.Collections.Generic.HashSet<string>(m.Unlocked ?? System.Array.Empty<string>());
                // Only a list that grows AFTER the join seed is a real pickup worth the badge + toast (#1069) —
                // otherwise every join with any unlocked game flashed "Data fragment recovered!" and re-badged the
                // Arcade entry.
                bool grew = _unlocksSeeded && incoming.Count > UnlockedGames.Count;
                _unlocksSeeded = true;
                UnlockedGames = incoming;
                RebuildWikiState();
                if (grew)
                {
                    NewArcadeUnseen = true; // badge the Arcade menu entry until the player opens it
                    LastMessage = Localizer?.Get("ui.arcade.downloaded") ?? "Game downloaded to your Arcade.";
                }
            };
            Network.MissionsReceived += m => Missions = m;
            Network.AllianceListReceived += m => Alliances = m ?? new AllianceList();
            Network.CrewListReceived += m => Crew = m ?? new CrewList();
            Network.CrewInviteReceived += m =>
            {
                string t = Localizer?.Get("ui.crew.invite_from") ?? "{name} invites you to “{crew}”";
                ShowMessage(t.Replace("{name}", m.FromName).Replace("{crew}", m.CrewName));
            };
            Network.MarkerListReceived += m => Markers = m?.Markers ?? System.Array.Empty<NetMarker>();
            Network.NoteListReceived += m =>
            {
                Notes = m?.Notes ?? System.Array.Empty<NetNote>();
                NotesVersion++;
            };
            Network.CompanionsReceived += m => Companions = m ?? new CompanionList();
            Network.SpeedersReceived += m => Speeders = m.Speeders ?? System.Array.Empty<NetSpeeder>();
            Network.RailsReceived += m => Rails = m.Lines ?? System.Array.Empty<NetRailLine>(); // #2113
            Network.TrainsReceived += m =>
            {
                Trains = m.Trains ?? System.Array.Empty<NetTrain>();
                TrainsReceivedAt = Time.timeAsDouble;
            };
            Network.SpeederFxReceived += m =>
            {
                PendingSpeederFx.Add(m);
                if (PendingSpeederFx.Count > 24)
                {
                    PendingSpeederFx.RemoveAt(0); // safety cap — never let a backlog grow
                }
            };
            Network.TameProgressReceived += m => TameState = (m != null && m.Active) ? m : null;
            Network.TameResultReceived += m =>
            {
                TameState = null; // the ritual ended — close the HUD prompt
                if (m == null) return;
                if (m.Success) NewCompanionUnseen = true; // badge the Companions tab
                if (!string.IsNullOrEmpty(m.MessageKey)) ShowMessage(Localizer?.Get(m.MessageKey) ?? m.MessageKey);
            };
            Network.AllianceRequestReceived += m =>
            {
                string who = string.IsNullOrEmpty(m.RequesterName) ? m.RequesterId : m.RequesterName;
                ShowMessage((Localizer?.Get("ui.alliance.request_from") ?? "Alliance request from {name}").Replace("{name}", who));
            };
            Network.ChatReceived += m =>
            {
                // A muted sender is dropped from the Funk mirror too (#1209) — hiding a line in the chat
                // overlay but leaving it in the Alliances tab would only move the problem one tab across.
                if (!m.IsNpcCall && Settings != null && Settings.IsMuted(m.SenderId, m.Sender))
                {
                    return;
                }

                _recentChat.Add(m);
                if (_recentChat.Count > RecentChatMax) _recentChat.RemoveAt(0);
                if (m.IsNpcCall)
                {
                    _recentNpcCalls.Add(m); // mirrored into the Missions tab's ☎ block (#1158)
                    if (_recentNpcCalls.Count > RecentNpcCallsMax) _recentNpcCalls.RemoveAt(0);
                }
            };
            Network.ShipCombatStatusChanged += m => ShipCombat = m;
            Network.SpaceStateReceived += m =>
            {
                if (!InSpace)
                {
                    SpaceSkipLaunch = m.SkipLaunch; // latched on entry only (later updates don't re-trigger Enter)
                    SpaceHasResume = m.HasResumePose; // #2118: latched with it — the flight view places the ship there on Enter
                    SpaceResumePos = new Vector3(m.ResumeX, m.ResumeY, m.ResumeZ);
                    SpaceResumeYaw = m.ResumeYaw;
                    _spaceHasEvaStart = m.HasEvaStart; // #2636: …and the suit's own spot, when it fell out of a derelict
                    _spaceEvaStartPos = new Vector3(m.EvaStartX, m.EvaStartY, m.EvaStartZ);
                    _spaceEvaStartYaw = m.EvaStartYaw;
                    if (m.Hyperjump)
                    {
                        HyperjumpStarted?.Invoke(); // warp VFX as we arrive in flight in a new system
                    }
                    else if (m.Wormhole)
                    {
                        WormholeArrived?.Invoke(); // #2242: through a rift — the rift transit instead of the warp
                    }
                }

                // #1614: not latched — the server re-sends the state with the transit off when it ends in flight (a
                // refused landing), and the flight view hands the controls back the moment it reads that.
                SpaceAutomaticTransit = m.AutomaticTransit;
                SpaceTransitDestination = m.TransitDestinationBodyId ?? string.Empty;

                // #1565: an in-flight hyperjump lands nowhere, so no WorldReset renames the HUD — the flight
                // state names its system + anchor body itself. Same composition as OnWorldReset.
                if (!string.IsNullOrEmpty(m.BodyName))
                {
                    LocationName = string.IsNullOrEmpty(m.SystemName) ? m.BodyName : $"{m.SystemName} · {m.BodyName}";
                }

                Space = m;
                InSpace = true;
            };
            // item 20 S1/S3: voxel structures for the flight view. Only the player's OWN ship drives the _ship
            // mesh (Game.ShipDesign); asteroid bodies (Kind != "ship") are handled directly by SpaceView.
            Network.SpaceShipDesignReceived += m =>
            {
                if (m.Kind == "ship" || string.IsNullOrEmpty(m.Kind))
                {
                    ShipDesign = m;
                }
                else if (m.Kind == "ship_remote" && m.Id.StartsWith("ship:", System.StringComparison.Ordinal))
                {
                    _remoteShipDesigns[m.Id.Substring(5)] = m; // keyed by the owning player's id (incl. "npc:<id>" traders)
                }
            };
            // Peaceful NPC traders warping in/out — queue the flash for the flight view to play.
            Network.SpaceWarpReceived += m =>
            {
                if (!InSpace)
                {
                    return;
                }

                PendingWarpFx.Add(m);
                if (PendingWarpFx.Count > 24)
                {
                    PendingWarpFx.RemoveAt(0); // safety cap — never let a backlog grow
                }
            };
            // Ship-as-object: ships parked on this world arrive/leave as placed structure objects.
            Network.LandedShipReceived += m =>
            {
                if (m.Removed)
                {
                    LandedShips.Remove(m.StructureId);
                }
                else
                {
                    var ship = new LandedShipModel
                    {
                        StructureId = m.StructureId,
                        OwnerId = m.PlayerId,
                        Origin = new Vector3i(m.OriginX, m.OriginY, m.OriginZ),
                        Hull = m.Hull,
                        Width = m.Width, Height = m.Height, Length = m.Length,
                    };
                    bool hasTint = m.Tint != null && m.Tint.Length == m.Block.Length;
                    bool hasGlow = m.Glow != null && m.Glow.Length == m.Block.Length;
                    bool hasShape = m.Shape != null && m.Shape.Length == m.Block.Length;
                    for (int i = 0; i < m.Block.Length; i++)
                    {
                        var cell = new Vector3i(m.X[i], m.Y[i], m.Z[i]);
                        ship.Cells[cell] = new BlockId(m.Block[i]);
                        int tint = hasTint ? m.Tint[i] : 0, glow = hasGlow ? m.Glow[i] : 0;
                        if (tint != 0 || glow != 0) ship.Mods[cell] = (tint, glow);
                        if (hasShape && m.Shape[i] != 0) ship.Shapes[cell] = m.Shape[i];
                    }

                    LandedShips[m.StructureId] = ship;
                }

                LandedShipsChanged?.Invoke();
            };
            // A parked ship's cell changed (on-foot furnishing / repairs) — update the model + re-mesh.
            Network.StructureBlockChangedReceived += m =>
            {
                if (LandedShips.TryGetValue(m.StructureId, out var ship))
                {
                    var cell = new Vector3i(m.X, m.Y, m.Z);
                    ship.Set(cell, new BlockId(m.Block));
                    if (m.Block != 0)
                    {
                        if (m.Tint != 0 || m.Glow != 0) ship.Mods[cell] = (m.Tint, m.Glow); else ship.Mods.Remove(cell);
                        if (m.Shape != 0) ship.Shapes[cell] = m.Shape; else ship.Shapes.Remove(cell);
                    }

                    ship.DirtyCells.Add(cell); // #2255: the view re-meshes only the chunks this cell reaches
                    LandedShipsChanged?.Invoke();
                }
            };
            Network.SpaceClosed += m => { InSpace = false; Space = null; LastMessage = ServerMessageText(m.Reason); };
            Network.StationBoardedReceived += m => { LastMessage = $"Boarded {m.Name}."; CurrentStationId = m.StationId ?? string.Empty; };
            Network.PlanetEnemiesReceived += m =>
            {
                PlanetEnemies = m.Enemies;
                PlanetEnemiesServerTime = m.ServerTime;
                PlanetEnemiesLocalTime = EntityClock.ToLocal(m.ServerTime, WorldTime); // #2670: one stamp per list
            };
            Network.CreaturesReceived += m =>
            {
                Creatures = m.Creatures;
                CreaturesServerTime = m.ServerTime;
                CreaturesLocalTime = EntityClock.ToLocal(m.ServerTime, WorldTime); // #2665: one stamp per list
            };
            Network.WorldFxReceived += m =>
            {
                if (PendingWorldFx.Count < 64)
                {
                    PendingWorldFx.Add(m);
                }
            };
            Network.NpcsReceived += m => Npcs = m.Npcs;
            Network.NpcStandingsReceived += m =>
            {
                NpcStandings.Clear();
                for (int i = 0; i < m.NpcIds.Length && i < m.StageKeys.Length; i++)
                {
                    NpcStandings[m.NpcIds[i]] = m.StageKeys[i];
                }

                // #2601: the wish markers ride on the same per-player message (null from an older server's payload).
                NpcWishMarks.Clear();
                foreach (int id in m.WishNpcIds ?? System.Array.Empty<int>())
                {
                    NpcWishMarks[id] = 1;
                }

                foreach (int id in m.WishReadyNpcIds ?? System.Array.Empty<int>())
                {
                    NpcWishMarks[id] = 2;
                }
            };
            Network.KnownNpcsReceived += m => KnownNpcs = m.People ?? System.Array.Empty<NetKnownNpc>();
            Network.ContainersReceived += m => OnContainers(m);
            Network.OwnedShipsReceived += m => OwnedShips = m.Ships;
            Network.WorldEnvironmentReceived += m =>
            {
                Environment = m;
                _envAge = 0f; // restart the extrapolation window (#900)
                Circumference = m.Circumference > 0 ? m.Circumference : WorldConstants.Circumference;
                World?.SetCircumference(Circumference); // chunk/block wrap at this world's size
                ChunkMesher.GroveCircumference = Circumference; // #2856: so does the grove colour drift
                if (CaptureEnvActive)
                {
                    ApplyCaptureEnv(Environment); // keep marketing shots clear-weather daylight across re-broadcasts
                }
            };
            Network.WeatherForecastReceived += OnWeatherForecast;
            Network.WorldResetReceived += OnWorldReset;
            Network.ShipAiLineReceived += m =>
            {
                // The settings button offers "skip" while the tutorial runs and "restart" after it (#2462, B03): the server
                // says so outright, whatever the chip shows — the finale may sit on it in the Guardian system while the
                // lessons are unfinished. An older server is read from the chip's key prefix as before.
                OnboardingActive = ObjectiveSourceRules.TutorialRunning(m.ObjectiveSource, m.ObjectiveKey, m.TutorialActive);
                bool storyLine = m.Kind == 2 && !string.IsNullOrEmpty(m.LineKey);
                if (storyLine)
                {
                    NewStoryUnseen = true;
                    // Only the pack's beats go to the beat list (#2462, B08); the other narrator lines (insights, the
                    // Guardian reveal, relay lines, old memories) stay re-readable in the VEGA log below.
                    storyLine = _storyBeats.OnStoryLine(ActiveStoryPack(), m.LineKey);
                }

                // Tips log (#737): onboarding (0), advisor (1) and system (3) lines become re-readable the
                // moment they arrive, and so do the narrator lines that are not beats. Kind 4 (prologue) is
                // intro-only by design. #2471: a line that only points at the Journal (or sets a flight target) is not kept.
                bool logged = (m.Kind is 0 or 1 or 3 or 5 || (m.Kind == 2 && !storyLine)) && !VegaText.IsTransientLine(m.LineKey);
                if (logged && !string.IsNullOrEmpty(m.LineKey) && !VegaLogKeys.Contains(m.LineKey))
                {
                    VegaLogKeys.Add(m.LineKey);
                }

                // #2471 (B09): "N new entries in your Journal" — the beats went to the Journal silently; badge its tab.
                if (m.LineKey == BlocksBeyondTheStars.Shared.Story.StoryNarratorLines.JournalNews)
                {
                    NewStoryUnseen = true;
                }

                // #2471 (B6.4): the dock lesson's help sets the station as the flight target — unless the player set one.
                if (ObjectiveTracker.WaypointToAdopt(SpaceWaypointId, SpaceWaypointPos.HasValue, m.SpaceWaypointId) is { } waypoint)
                {
                    SpaceWaypointId = waypoint;
                }
            };
            Network.StoryPackStatesReceived += OnStoryPackStates; // #2471: the Journal's stories
            Network.ObjectiveListReceived += m =>
            {
                Objectives = m; // #2471: the objectives in the chip order (live progress, "show all objectives")
                JournalVersion++;
            };
            Network.VegaJournalReceived += m =>
            {
                // The persisted snapshot replaces any live-collected keys — it is the authoritative set. The basic training's
                // lessons come from the tutorial pack's own steps (#2469; the client's copy of the old stage list is gone).
                VegaLogKeys.Clear();
                VegaLogKeys.AddRange(VegaText.JournalKeys(m.Milestones, Content?.Stories.Values));
                MergeRelayInsightsIntoLog(); // #2470: the snapshot does not carry the save's relay insights
            };
            Network.RelayNetworkReceived += m => RelayNetwork = m; // #1125: server-authoritative, render-only
            Network.StoryStateReceived += m =>
            {
                Story = m;
                UnknownStories.Observe(UnknownStoryPacks.PackIdsOf(m), HasStoryPack); // #2500: a world story this client lacks
                // Rejoin-proof logs (#1110/#1111): the snapshot carries the found KEYS; texts resolve from
                // the pack. Replaces the live-collected lists — the server is authoritative.
                var pack = ActiveStoryPack();
                if (pack != null)
                {
                    StoryLogFragments.Clear();
                    StoryLogFragments.AddRange(BlocksBeyondTheStars.Client.Core.StoryLog.Fragments(pack, m.FoundFragmentKeys));
                    StoryLogMemories.Clear();
                    StoryLogMemories.AddRange(BlocksBeyondTheStars.Client.Core.StoryLog.Memories(pack, m.PlayerMemoryKeys));
                    StoryLogLore.Clear();
                    StoryLogLore.AddRange(BlocksBeyondTheStars.Client.Core.StoryLog.Lore(pack, m.FoundLoreKeys));
                }

                // The beats this player heard (#2462, B08) — the beat list survives a rejoin now.
                _storyBeats.OnStoryState(pack, m.StoryId, m.HeardBeats);
                MergeRelayInsightsIntoLog(); // #2470: relay insights spoken before this player's story track started
            };
            Network.NetFragmentsReceived += m => NetFragments = m.Fragments;
            Network.NetFragmentRevealedReceived += m =>
            {
                if (!StoryLogFragments.Contains((m.Category, m.TextKey)))
                {
                    StoryLogFragments.Add((m.Category, m.TextKey));
                }

                NewStoryUnseen = true; // badge the Story menu entry until the player reads it
                // The reader panel (#1110) replaces the old toast; the Story Log keeps the text to re-read.
                OpenReader(Localizer?.Get("ui.reader.fragment") ?? "Net fragment",
                    Localizer?.Get("lore.cat." + m.Category) ?? m.Category, m.TextKey);
            };
            Network.PlayerMemoryReceived += m =>
            {
                if (!StoryLogMemories.Contains(m.TextKey))
                {
                    StoryLogMemories.Add(m.TextKey);
                }

                NewStoryUnseen = true;
                OpenReader(Localizer?.Get("ui.reader.memory") ?? "Memory", string.Empty, m.TextKey);
            };
            Network.LoreTextRevealedReceived += m =>
            {
                if (!StoryLogLore.Contains((m.Site, m.TextKey)))
                {
                    StoryLogLore.Add((m.Site, m.TextKey));
                }

                NewStoryUnseen = true;
                OpenReader(Localizer?.Get("ui.lore.site." + m.Site) ?? m.Site, string.Empty, m.TextKey);
            };
            Network.PlanetScanReceived += m =>
            {
                LastPlanetScan = m;
                PlanetScanVersion++;
                if (!string.IsNullOrEmpty(m.BodyId))
                {
                    PlanetOverviews[m.BodyId] = m; // #2239: kept for the Map tab
                }

                if (m.KnowledgeGained > 0)
                {
                    LastMessage = string.Format(Localizer?.Get("ui.overview.knowledge_gain") ?? "+{0} knowledge", m.KnowledgeGained);
                }

                if (OpenOverviewOnNextPlanetScan)
                {
                    OpenOverviewOnNextPlanetScan = false;
                    PlanetOverviewCard.Show(this, m); // #2239: the flight scanner read a planet — what awaits there
                    // #2247: the first card says once what the colours mean and where the report lives on — "once"
                    // counting only a line that came up (#2462, B31: muted, it waits for a later card; #2551: dropped
                    // unseen after waiting behind other lines, too).
                    if (Settings != null && !Settings.PlanetCardHintShown && m.Rows != null && m.Rows.Length > 0)
                    {
                        var settings = Settings;
                        VegaPanel.Instance?.SayLocal("vega.hint.planet_card", () =>
                        {
                            settings.PlanetCardHintShown = true;
                            settings.Save();
                        });
                    }
                }
            };
            Network.ScanResultReceived += m =>
            {
                LastScan = m;
                LastScanAt = Time.time;
                LastScanFromShip = SpaceViewActive && !InEva;
                if (LastScanFromShip)
                {
                    PlanetOverviewCard.ShowReadout(this, m); // #2247: the ship scanner's readouts open on the right
                }

                if (m.FirstTime && m.KnowledgeGained > 0)
                {
                    // Localized (#484) — this toast used to be an English literal in the German build too.
                    LastMessage = string.Format(
                        Localizer?.Get("ui.scan.knowledge_gain") ?? "+{0} knowledge ({1})",
                        m.KnowledgeGained,
                        m.KnowledgeTotal);
                }
            };
            // Achievements: the server owns the counters and the payouts; the client just renders them.
            Network.AchievementsReceived += m =>
            {
                Achievements = m?.Items?.ToArray() ?? System.Array.Empty<NetAchievement>();
                AchievementCounters = m?.Counters ?? new System.Collections.Generic.Dictionary<string, int>();
            };
            Network.AchievementUnlockedReceived += m =>
            {
                if (m == null || string.IsNullOrEmpty(m.Key))
                {
                    return;
                }

                // #2597: a celebration banner (HudUi, the research toast's slot) replaces the status line; the success
                // sound plays when the banner appears, not here.
                if (AchievementsUnlocked.Count < AchievementBannerQueueCap)
                {
                    AchievementsUnlocked.Enqueue(m.Key);
                }
            };
            Network.AchievementRewardDeferredReceived += m =>
            {
                // Earned but unpaid — say so, because the reward is the point of the achievement.
                LastMessage = Localizer?.Get("ui.achv.reward_full")
                    ?? "Achievement earned — but no room for the reward! Make space in your inventory.";
            };

            Network.DiscoveryLogReceived += m =>
            {
                if (m.Full)
                {
                    Discoveries.Clear(); // the join snapshot replaces whatever a previous session left
                    DiscoveryWhere.Clear();
                }

                var entries = m.Entries ?? System.Array.Empty<string>();
                var names = m.Names ?? System.Array.Empty<string>();
                // Where each was found (#1843) — parallel arrays an older server leaves empty, so index-guard.
                var bodyNames = m.BodyNames ?? System.Array.Empty<string>();
                var systemNames = m.SystemNames ?? System.Array.Empty<string>();
                for (int i = 0; i < entries.Length; i++)
                {
                    Discoveries[entries[i]] = i < names.Length ? names[i] ?? string.Empty : string.Empty;

                    string bodyName = i < bodyNames.Length ? bodyNames[i] ?? string.Empty : string.Empty;
                    string systemName = i < systemNames.Length ? systemNames[i] ?? string.Empty : string.Empty;
                    if (bodyName.Length > 0 || systemName.Length > 0)
                    {
                        DiscoveryWhere[entries[i]] = (bodyName, systemName);
                    }
                }
            };
            Network.WreckRepairStatusChanged += m => Wreck = m.Claimed ? null : m;
            Network.ShipRepairStatusChanged += m => ShipRepair = m.NeedsRepair ? m : null;
            // An incoming trade invitation (#981) needs an answer, so it is held as state — a toast alone
            // could not be acted on. It clears the moment the trade opens or the invitation is answered.
            // The toast comes with the ServerMessage the server sends alongside it.
            Network.TradeRequested += m => PendingTradeFrom = m.Requester;
            Network.TradeUpdated += m => { Trade = m; TradeActive = true; PendingTradeFrom = string.Empty; };
            Network.TradeClosedReceived += m =>
            {
                TradeActive = false;
                Trade = null;
                PendingTradeFrom = string.Empty;
                LastMessage = m.Completed ? (Localizer?.Get("ui.trade.complete") ?? "Trade complete.") : ServerMessageText(m.Reason);
            };
            Network.DockRequested += m => { PendingDockFrom = m.Requester; LastMessage = (Localizer?.Get("ui.dock.requests_toast") ?? "{0} requests docking.").Replace("{0}", m.Requester); };
            Network.DockStatusChanged += m =>
            {
                Dock = m;
                // #2720: the same "Docked with" prefix the docked hint uses (PlayerInteractions), localized — it was English.
                LastMessage = m.Docked ? $"{Localizer?.Get("ui.dock.docked") ?? "Docked with"} {m.Partner}" : ServerMessageText(m.Reason);
            };
            // #1071: rejections arrive as @srv.mission.* tokens — localize them instead of pushing the raw token at the player.
            // #2462 (B24): localized, named after the mission, and "complete" only on a real turn-in.
            Network.MissionResultReceived += m => LastMessage = m.Success
                ? MissionToast.Text(m, FindMission(m.MissionId), key => Localizer?.Get(key) ?? key, key => Localizer?.Has(key) ?? false)
                : ServerMessageText(m.Reason);
            Network.RespawnNoticeReceived += m =>
            {
                LastMessage = ServerMessageText(m.Reason);
                RespawnTarget = new Vector3(m.X, m.Y, m.Z); // teleport the body to the heal-tank on respawn
                if (m.Reason == BlocksBeyondTheStars.Shared.Definitions.ReturnToShipRules.DoneToken)
                {
                    ReturnToShipArrivals++; // #2559: the pause menu that asked closes now
                }
            };
            Network.RespawnOptionsReceived += m => LastMessage = ServerMessageText(m.Reason); // deferred death: reason shows under the choice modal
            Network.ServerRulesReceived += m => { Rules = m; LastMessage = $"Mode: {m.GameMode} · PvP: {m.Pvp}"; };
            Network.CraftCompleted += m => LastMessage = m.Success ? CraftedMessage(m.RecipeKey) : CraftFailMessage(m.Reason);
            Network.ActionRejected += m =>
            {
                Debug.Log($"Action '{m.Action}' rejected: {m.Reason}");
                LastMessage = RejectionMessage(m.Action, m.Reason);
                if (m.Action == "seat")
                {
                    SeatRejections++; // #2122: somebody sits there — PlayerController stands us back up
                }
                else if (m.Action == "glider")
                {
                    GliderRejections++; // #2296: no glider worn as the server sees it — PlayerController folds the wing
                }
                else if (m.Action == "visit")
                {
                    VisitRefusals++; // #2724: the flight view drops the veil it raised for the visit
                }
                else if (m.Action == "return_ship")
                {
                    // #2559: the open pause menu shows why instead of closing — and a fight refusal carries the seconds
                    // left, so the row greys out at once even if no state update said so yet.
                    ReturnToShipRefusalText = LastMessage;
                    ReturnToShipRefusals++;
                    float fight = (float)BlocksBeyondTheStars.Shared.Definitions.ReturnToShipRules.FightSecondsOf(m.Reason);
                    if (fight > 0f)
                    {
                        _returnToShipFightUntil = Mathf.Max(_returnToShipFightUntil, Time.realtimeSinceStartup + fight);
                    }
                }

                // The server is authoritative: if a dig is rejected because the cell is "already empty", the
                // client's view of it is stale — a ghost block (a cell some server path cleared to air without a
                // broadcast the client applied). Clear it directly so the phantom vanishes at once and the next
                // dig hits whatever is actually behind it, instead of "mining" a block that isn't there (B32).
                if (m.Action == "mine" && !string.IsNullOrEmpty(m.Reason) && m.Reason.Contains("empty")
                    && World != null)
                {
                    var c = LastMineCell;
                    if (World.ApplyBlockChange(c.x, c.y, c.z, 0, out var ghostCoord))
                    {
                        // Neighbours too: a ghost on a chunk border leaves the adjacent chunk's
                        // now-exposed wall faces missing otherwise (see-through hole when mining fast).
                        MarkChunkAndNeighborsDirty(ghostCoord);
                    }
                }
            };
            Network.ServerMessageReceived += m => { Debug.Log(m.Text); LastMessage = ServerMessageText(m.Text); };

            // Connect now; the join handshake is sent once the transport reports Connected
            // (UDP connect is asynchronous — sending the join before that would be dropped).
            Network.Connect(Host, Port);
        }

        private void Update()
        {
            Network?.Poll();
            _worldHold?.Tick(Time.realtimeSinceStartup); // #2461: one keep-alive for every holder (menu, feedback, …)
            _worldClock.Advance(Time.deltaTime); // after Poll, so a PauseState that just landed takes effect now
            Bio.Tick(Time.deltaTime); // #2202: the effect seconds run between two server updates
            BioSenses.Ensure(this);   // #2202: night sight and perception — the component arrives with the first such effect
            TickWeatherSmoothing();
            WorldMinimap.Pump(); // #2172: planet map bakes — worker threads on desktop, time slices in the browser

            _skyScanTimer -= Time.deltaTime;
            if (_skyScanTimer <= 0f)
            {
                _skyScanTimer = 0.2f;
                ExposedToSky = ComputeExposedToSky();
                AtHome = ComputeAtHome();
            }

            if (Network != null && !_joinSent)
            {
                if (Network.Connected)
                {
                    try
                    {
                        Debug.Log($"Sending join request to {Host}:{Port} as {PlayerName}.");
                        // The arcade install id travels with the join (#1222): a glitch.fun guest has no
                        // portal account, so it is the only identity their /report can carry. Empty everywhere else.
                        string installId = GlitchIntegration.ArcadeInstallId;
                        // #2468: this client plays story-pack cutscenes when its world rig has the director — the server then
                        // sends CutscenePlay (tag 295) instead of stamping every scene seen at once.
                        Network.PlaysCutscenes = CutsceneDirector.Instance != null;
                        // #2499: VEGA's panel draws a line's buttons (the first-start question, Start now / Later on a course
                        // offer) — every world rig has the panel, so this client always announces choices1.
                        Network.ShowsChoices = true;
                        // #2616 (dialog agent): NpcDialogUi closes on the server's "close" and sends the leave — dialogclose1.
                        Network.ClosesDialogs = true;
                        Network.Join(PlayerName, string.IsNullOrEmpty(Password) ? null : Password, Locale.Code(),
                            string.IsNullOrEmpty(Token) ? null : Token, ViewDistanceChunks,
                            string.IsNullOrEmpty(HostedToken) ? null : HostedToken,
                            string.IsNullOrEmpty(installId) ? null : installId);
                        Network.SendAppearance(SkinRgb, TorsoRgb, ArmRgb, LegRgb, HullRgb);
                        // #1119: the server initiates NPC calls, so it must know the preference — and VEGA's hints mute
                        // (#2462, B31), so a muted hint waits instead of burning its once-flag unseen.
                        Network.SendSetNpcCalls(Settings?.NpcCalls ?? 0, Settings != null && !Settings.VegaHints);
                        if (!string.IsNullOrEmpty(FacePixels))
                        {
                            Network.SendFace(FacePixels); // tell others our custom face (server persists + relays)
                        }

                        for (int part = 0; part < BodyPaintPixels.Length; part++)
                        {
                            if (!string.IsNullOrEmpty(BodyPaintPixels[part]))
                            {
                                Network.SendBodyPaint(part, BodyPaintPixels[part]); // body paintings, same path (#874)
                            }
                        }

                        _joinSent = true;
                        _joinSendFailureLogged = false;
                    }
                    catch (System.Exception ex)
                    {
                        if (!_joinSendFailureLogged)
                        {
                            Debug.LogError($"Join request could not be encoded or sent: {ex}");
                            _joinSendFailureLogged = true;
                        }
                    }
                }
                else
                {
                    // Re-dial until the server answers — ConnectRetryPolicy decides how patiently: a remote
                    // host gets a few attempts, the bundled local server gets as long as a fresh world takes
                    // to generate (it used to get the remote budget and lost the race by seconds).
                    _retryTimer += Time.deltaTime;
                    _connectWaited += Time.deltaTime;
                    bool local = LocalServer != null;
                    bool readySignal = local && !_localReadyDialed && LocalServer.Ready;
                    switch (ConnectRetryPolicy.Next(local, readySignal, _retryTimer, _retries, _connectWaited))
                    {
                        case ConnectRetryPolicy.Verdict.Retry:
                            if (readySignal)
                            {
                                _localReadyDialed = true;
                                Debug.Log($"Local server reports ready after {_connectWaited:0.0} s — connecting.");
                            }

                            _retryTimer = 0f;
                            _retries++;
                            Network.Connect(Host, Port);
                            break;

                        case ConnectRetryPolicy.Verdict.GiveUp:
                            if (string.IsNullOrEmpty(ConnectFailedReason))
                            {
                                // All retries spent and still no connection: give up loudly. Disconnected
                                // never fires for a never-connected session, so this flag is the only
                                // signal AppShell gets to bail back to the menu with a message (#409).
                                Debug.LogError($"Could not connect to {Host}:{Port} after {_retries} retries ({_connectWaited:0} s) — giving up.");
                                ConnectFailedReason = Localizer?.Get("ui.connect.failed") ?? "Could not connect to the server.";
                            }

                            break;
                    }
                }
            }

            // Upload + assign chunk geometry whose async build finished (A2), then assign collision meshes whose
            // async bake finished (both cheap on the main thread — the heavy work ran on worker threads).
            ReturnRetiredChunkArrays(); // #1555
            DrainBuiltChunks();
            DrainBakedColliders();
            DrainSyncCooks(); // #1819: browser collider cooks within their own frame budget
            SampleMotion();   // #1818: the build order looks ahead along the player's velocity

            // Kick off off-thread (re)builds for chunks that changed, but cap how many we DISPATCH per frame (P1)
            // so a burst of chunks arriving while moving fast spreads over several frames instead of stalling one.
            // Nearest chunks build first; chunks past the budget stay queued for the next frames.
            PromoteDeferredDirty(); // #1529
            PromoteWaterReachDirty(); // #1903
            if (_dirty.Count > 0)
            {
                _dirtyScratch.Clear();
                _dirtyScratch.AddRange(_dirty);
                var pp = PlayerPosition;

                // #1515: nearest-first WITHOUT sorting the whole set — the distances are computed once and the
                // `budget` nearest entries are picked by partial selection (O(n·k), k ≤ MeshChunksPerFrame),
                // instead of an O(n log n) sort with a closure and two seam-aware distances per comparison to
                // take four. Same order as the sort produced.
                _dirtyDistScratch.Clear();
                for (int i = 0; i < _dirtyScratch.Count; i++)
                {
                    _dirtyDistScratch.Add(BuildOrderKey(_dirtyScratch[i], pp)); // #1818: nearest first, then ahead
                }

                int budget = Mathf.Max(1, MeshChunksPerFrame);
                int built = 0;
                _frameWork.Restart(); // #1819: the browser builds inline — a time budget, not a count, decides
                while (BuildBudgetLeft(built, budget) && _dirtyScratch.Count > 0)
                {
                    int best = 0;
                    float bestDist = _dirtyDistScratch[0];
                    for (int i = 1; i < _dirtyScratch.Count; i++)
                    {
                        if (_dirtyDistScratch[i] < bestDist)
                        {
                            bestDist = _dirtyDistScratch[i];
                            best = i;
                        }
                    }

                    var coord = _dirtyScratch[best];
                    int last = _dirtyScratch.Count - 1;
                    _dirtyScratch[best] = _dirtyScratch[last];
                    _dirtyDistScratch[best] = _dirtyDistScratch[last];
                    _dirtyScratch.RemoveAt(last);
                    _dirtyDistScratch.RemoveAt(last);

                    // Neighbours not yet streamed are no-ops — flush them without spending budget; they
                    // re-mark themselves dirty once their own data arrives.
                    _dirty.Remove(coord);
                    if (DispatchChunkBuild(coord))
                    {
                        built++;
                    }
                }
            }

            // Round worlds: keep every loaded chunk drawn at the copy nearest the player as it laps the world
            // in EITHER direction. Near chunks resolve to their canonical position (a no-op write); only
            // chunks across a seam shift by ±Circumference (X) / ±LatitudePeriod (Z). Throttled to once per
            // block of movement on any axis. Y is included because the renderer-cull and near-only-collider
            // toggling in RepositionChunks are 3D distance-based: a straight-down jetpack descent must re-enable
            // the ground colliders below before the player reaches them, or they'd fall through.
            int chunkAnchorX = Mathf.FloorToInt(PlayerPosition.x);
            int chunkAnchorY = Mathf.FloorToInt(PlayerPosition.y);
            int chunkAnchorZ = Mathf.FloorToInt(PlayerPosition.z);
            // …plus a slow heartbeat, because this pass is also the ONLY thing that unloads far chunks
            // (#966): a player who stands still — parked at a station, stuck, AFK — never released a single
            // chunk, however much the world streamed to them while they stood there.
            _sinceReposTick += Time.deltaTime;
            bool moved = chunkAnchorX != _lastReposX || chunkAnchorY != _lastReposY || chunkAnchorZ != _lastReposZ;
            if (moved || _sinceReposTick >= ReposIdleInterval)
            {
                _lastReposX = chunkAnchorX;
                _lastReposY = chunkAnchorY;
                _lastReposZ = chunkAnchorZ;
                _sinceReposTick = 0f;
                RepositionChunks();
            }

            UpdateVisibility(); // #1823: re-walks when the camera changes chunk or chunk connectivity changed
        }

        /// <summary>The active world changed (travel): drop all chunks/meshes so the new planet streams in.</summary>

        private void OnWorldReset(BlocksBeyondTheStars.Networking.Messages.WorldReset m)
        {
            LocationName = string.IsNullOrEmpty(m.SystemName) ? m.PlanetName : $"{m.SystemName} · {m.PlanetName}";
            // #2725: a docked friend's ship interior names its owner — we walk in it as a guest; every other world clears it.
            VisitingHost = m.ShipOwnerId ?? string.Empty;
            EntityClock.Reset(); // #2665: a world change may mean another server clock (a hosted world, a reconnect)
            RebuildFloraTints(); // a new world ⇒ its own per-species flora colours
            SessionExploredCells.Clear(); // #1113: live-explored cells are per body

            // Parked ship objects belong to the world we just left; the new world re-sends its own.
            LandedShips.Clear();
            LandedShipsChanged?.Invoke();

            // Same for every world-scoped entity list: the new world re-sends its own, but only lists it
            // actually HAS — a peaceful destination never sends a PlanetEnemyList, so without this the old
            // world's robots keep growling/firing here, stale beacons/bases/factories linger on the map, and
            // ghost speeders keep offering their board prompt (issue #412 M5). The views prune ids missing
            // from these arrays, so clearing despawns the stale objects.
            PlanetEnemies = System.Array.Empty<NetCombatEntity>();
            Creatures = System.Array.Empty<NetCreature>();
            Npcs = System.Array.Empty<NetNpc>();
            NpcStandings.Clear(); // #1118: per-world NPC ids — the new world re-sends its own standings
            NpcWishMarks.Clear(); // #2601: same ids, same reason
            Containers = System.Array.Empty<NetContainer>();
            Beacons = System.Array.Empty<NetBeacon>();
            Markers = System.Array.Empty<NetMarker>(); // per-world — the server re-sends the new body's set (#1217)
            Beams = System.Array.Empty<NetBeam>();
            Crystal.Clear();
            Lifts = System.Array.Empty<NetLift>();
            Bases = System.Array.Empty<NetBase>();
            Factories = System.Array.Empty<NetFactory>();
            DataCubes = System.Array.Empty<NetDataCube>();
            NetFragments = System.Array.Empty<NetStoryFragment>();
            Speeders = System.Array.Empty<NetSpeeder>();
            Rails = System.Array.Empty<NetRailLine>(); // #2113: per-world — the new world re-sends its lines and trains
            Trains = System.Array.Empty<NetTrain>();
            PendingSpeederFx.Clear(); // queued one-shot FX would play at old-world coordinates
            Waypoint = null; // a map-click waypoint is old-world coordinates too — without this the HUD
                             // compass kept pointing at a meaningless spot on every next planet (#592)
            // #1308: the landed-ship position is old-world coordinates as well, and the server sends NO
            // ShipPlacement when no ship is placed on the new world (observer, unplaced fleet ship) — so the
            // compass kept its blip + distance line on the PREVIOUS landing site. Clear it; a real placement
            // re-arrives right after this reset whenever there is one.
            ShipPosition = null;
            SpaceWaypointId = null; // the space waypoint is scoped to the space layout it was set in —
            SpaceWaypointPos = null; // a new world means a new system view, so it dies with it (#597)

            foreach (var view in _chunkObjects.Values)
            {
                if (view?.Go != null)
                {
                    // SetActive(false) takes effect immediately; Destroy is deferred to frame end. Without
                    // this the settle-freeze raycast (PlayerController) could still hit a stale collider from
                    // the *old* world this frame, "find ground", release early, and drop the player into the
                    // void before the new world's floor chunk has streamed in (the station-boarding fall).
                    view.Go.SetActive(false);
                }

                DestroyChunkView(view); // frees the chunk's render + collision meshes, not just the GameObject
            }

            _chunkObjects.Clear();
            _dirty.Clear();
            _deferredDirty.Clear();
            _waterReachDirty.Clear();
            // Mesh/bake bookkeeping for the old world is now stale; WorldEpoch (bumped below) fences any
            // in-flight off-thread builds + bakes so they're dropped in DrainBuiltChunks/DrainBakedColliders
            // instead of landing on the new world's chunks.
            _colliderGen.Clear();
            _colliderAppliedGen.Clear();
            _meshGen.Clear();
            _meshFailCounts.Clear();
            foreach (var parked in _syncCooks.Values) // #1819: browser cooks of the old world
            {
                if (parked.Collider != null)
                {
                    Destroy(parked.Collider);
                }
            }

            _syncCooks.Clear();
            _connectivity.Clear(); // #1823
            _walkVisible.Clear();
            _visibilityDirty = true;
            FarView?.ResetWorld(); // #1820: the new world's info + tiles arrive after this reset
            World.Clear();

            ServerSpawn = null; // re-snap at the new spawn once the next PlayerState arrives
            WorldEpoch++;
            LastMessage = m.Hyperjump ? $"Hyperjump → {m.PlanetName}…" : $"Arriving at {m.PlanetName}…";

            LoadingPlanetType = m.PlanetType;
            WorldReady = false; // hold the loading overlay until the player settles onto the new world

            if (m.Hyperjump)
            {
                HyperjumpStarted?.Invoke(); // the warp VFX masks the build-up for jumps
            }
            else
            {
                WorldLoadStarted?.Invoke(); // landings & station boardings get the loading overlay
            }
        }

        /// <summary>Raised when a hyperspace jump to another star system begins (drives the warp VFX).</summary>
        public event System.Action HyperjumpStarted;

        /// <summary>#2242: the flight state of the twin system arrived through a wormhole (the rift transit plays).</summary>
        public event System.Action WormholeArrived;

        /// <summary>#2240: until this time (Time.time) the radar pins every scannable object of the system — the Quantum
        /// scanner's system ping.</summary>
        public float SpaceSystemPingUntil;

        /// <summary>#2239: set by the flight scanner when it reads a planet — the next planet report opens the overview card.</summary>
        public bool OpenOverviewOnNextPlanetScan;

        /// <summary>#2239: the latest overview of every body this session (the Map tab shows it beside the body).</summary>
        public readonly System.Collections.Generic.Dictionary<string, PlanetScanResult> PlanetOverviews = new();

        /// <summary>Planet-type key of the world currently streaming in (e.g. "rocky", "orbital_station").
        /// Drives the loading overlay's destination label.</summary>
        public string LoadingPlanetType { get; private set; } = string.Empty;

        /// <summary>False while a new world streams in after a join/landing/boarding; set true by
        /// <see cref="PlayerController"/> once the player has settled onto solid ground. Drives the
        /// loading overlay's dismiss so the world is only revealed when it is ready.</summary>
        public bool WorldReady { get; private set; } = true;

        /// <summary>Called by <see cref="PlayerController"/> when the settle-freeze releases (solid ground
        /// is under the player) — tells the loading overlay the world is ready to reveal.</summary>
        public void NotifyWorldReady() => WorldReady = true;

        /// <summary>Raised when a non-hyperspace world load begins (join, landing, station boarding) —
        /// drives the loading overlay. Hyperspace jumps fire <see cref="HyperjumpStarted"/> instead, since
        /// the warp VFX already masks those.</summary>
        public event System.Action WorldLoadStarted;

        /// <summary>Raised the instant the client sends a world-changing intent that has no in-space descent to
        /// mask the swap (boarding a station, stepping into the ship interior), so the loading overlay can veil
        /// the screen immediately instead of briefly showing the old view first (B34). The overlay auto-clears
        /// the pre-raised veil if no <see cref="WorldLoadStarted"/> confirms the transition shortly after.</summary>
        public event System.Action WorldTransitionStarted;

        /// <summary>Pre-raises the loading overlay for an immediate (descent-less) world transition — see
        /// <see cref="WorldTransitionStarted"/>.</summary>
        public void BeginWorldTransition() => WorldTransitionStarted?.Invoke();

        /// <summary>#2724: raised when the server refused a descent-less transition the client already veiled for (a ship
        /// visit) — the loading overlay drops its pre-raised veil at once instead of waiting out its confirm timeout.</summary>
        public event System.Action WorldTransitionCancelled;

        /// <summary>Drops a pre-raised, still unconfirmed transition veil — see <see cref="WorldTransitionCancelled"/>.</summary>
        public void CancelWorldTransition() => WorldTransitionCancelled?.Invoke();

        private PlayerController _playerController;

        /// <summary>The local player rig (created by <see cref="WorldRig"/>), looked up once and cached. Null in
        /// the menu, and while a world transition is tearing the old rig down.</summary>
        private PlayerController Player
        {
            get
            {
                if (_playerController == null)
                {
                    _playerController = FindAnyObjectByType<PlayerController>();
                }

                return _playerController;
            }
        }

        /// <summary>Frees the session's procedural assets alongside the network. Unity never garbage-collects
        /// assets created via <c>new</c> (Texture2D/Material/Mesh), so without this every menu↔world cycle
        /// leaks both atlas textures + the chunk materials (#423). The static icon caches and the
        /// <see cref="HeldItem.BlockTileResolver"/> delegate MUST be cleared first — they wrap/pin this
        /// session's atlas (and the whole GameBootstrap object graph) across sessions. Everything else the
        /// world created (sky/starfield meshes+materials, chunk render meshes, icon sprites) becomes
        /// unreferenced with the world root and is swept by AppShell.ReturnToMenu's
        /// <c>Resources.UnloadUnusedAssets</c> pass.</summary>

        private readonly WorldTextureInbox _worldTextureInbox = new WorldTextureInbox();

        private readonly Dictionary<string, List<BlocksBeyondTheStars.Shared.Definitions.HeldModelPart>> _localToolLooks
            = new Dictionary<string, List<BlocksBeyondTheStars.Shared.Definitions.HeldModelPart>>(System.StringComparer.Ordinal);

        /// <summary>The local player's own look for the tool behind <paramref name="itemKey"/> (#1963), or null for
        /// the standard model. Looks are edited in the main menu only, so the merged parts are cached per session.</summary>
        public IReadOnlyList<BlocksBeyondTheStars.Shared.Definitions.HeldModelPart> LocalToolLook(string itemKey)
        {
            if (string.IsNullOrEmpty(itemKey) || Settings == null)
            {
                return null;
            }

            string baseKey = BlocksBeyondTheStars.Shared.State.ItemKey.Base(itemKey);
            if (!_localToolLooks.TryGetValue(baseKey, out var parts))
            {
                string model = Settings.GetToolLook(baseKey);
                parts = string.IsNullOrEmpty(model) ? null : BlocksBeyondTheStars.Shared.State.ToolLook.ToParts(model);
                _localToolLooks[baseKey] = parts != null && parts.Count > 0 ? parts : null;
            }

            return _localToolLooks[baseKey];
        }

        private static void ApplyWorldTextures(WorldTextureBatch batch)
        {
            if (batch == null)
            {
                return;
            }

            var changes = new Dictionary<string, TextureFrames>(batch.Changes.Count, System.StringComparer.Ordinal);
            foreach (var kv in batch.Changes)
            {
                changes[kv.Key] = kv.Value == null
                    ? null
                    : new TextureFrames(kv.Value.Frames, kv.Value.Fps, TextureLayer.World) { Owner = kv.Value.Owner };
            }

            GameTextures.ApplyWorldBatch(changes, batch.Complete);
        }

        /// <summary>True when this player may publish world textures: the server offers them, the world rule is on
        /// and the player is a world admin (the server fills the mode roster for admins only, #1121). The server
        /// checks again — this only decides whether the buttons are offered.</summary>
        public bool CanPublishWorldTextures
            => Rules != null && string.Equals(Rules.WorldTextures, "Admins", System.StringComparison.Ordinal)
               && Rules.PlayerModeNames != null && Rules.PlayerModeNames.Length > 0;

        private void OnDestroy()
        {
            Network?.Dispose();
            _worldHold?.Forget(); // the world is gone — nothing left to hold or to tell (#2461)
            Cutscenes.Clear();    // #2461: a cinematic torn down with its world must not keep the next one's HUD hidden

            // The atlas and the texture source are shared with the menu and its editors — a world's textures
            // must not outlive the world (#1959).
            _worldTextureInbox.Reset();
            GameTextures.ClearWorldLayer();

            IconResolver.ClearCache();
            ShapeIconFactory.ClearCache();
            HeldItem.BlockTileResolver = null;
            HeldItem.ModelResolver = null;
            HeldItem.MeshKitResolver = null; // #2534 (captures this object graph, like the resolvers above)
            HeldItem.PaintKitResolver = null; // #2776
            HeldItem.ReleasePartMaterials(); // also frees the held miniatures' meshes
            PropTextures.Release(); // the doors and machines that used them are going away with the world
            HeldItem.HandTintResolver = null;  // both closures capture Settings (and this object graph) — #1464
            HeldItem.HandPaintResolver = null;
            HeldItem.ReleaseHandAtlas();

            // The sampler's baked creature voices are code-created AudioClips held by a STATIC cache, so
            // they fall in exactly the same trap as the icon caches above: UnloadUnusedAssets cannot free
            // what a live static reference still pins (#901).
            SampleKit.ClearCache();
            CreatureVoiceBank.Clear();
            WorldMinimap.ClearCache(); // baked body previews — same static-cache trap (#966)
            SystemWeather.Clear();     // #2173: the old system's weather must not paint the next world's planets
            LandingPadBook.Clear();    // #2740: nor its pad lists the next world's map

            if (ChunkMaterial != null)
            {
                Destroy(ChunkMaterial);
            }

            if (ChunkMaterialTransparent != null)
            {
                Destroy(ChunkMaterialTransparent);
            }

            if (ChunkMaterialPaint != null)
            {
                Destroy(ChunkMaterialPaint);
            }

            if (Atlas != null)
            {
                Atlas.Changed -= OnAtlasRepainted;
            }

            // #2832: the atlas outlives this world (shared per process) — the menu, intro and editors show every plant's own tile.
            Atlas?.SetLooks(new System.Collections.Generic.Dictionary<ushort, string>());
            Atlas?.Release(); // shared per process (#1523) — the last owner decides, not this world
            PaintAtlas?.Destroy();
            CustomShapes?.Clear(); // the next world registers its own forms under the same indices (#423)
        }
    }
}
