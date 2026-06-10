using System.Collections.Generic;
using System.IO;
using Spacecraft.Networking.Messages;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Localization;
using Spacecraft.Shared.World;
using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Entry point MonoBehaviour. Loads data-driven content, connects to a server, and turns
    /// incoming chunk/state messages into the rendered world. Attach to a single GameObject
    /// in the scene and assign a material for chunk meshes.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("Connection")]
        public string Host = "127.0.0.1";
        public int Port = 31415;
        public string PlayerName = "Pilot";
        public string Password = "";

        [Header("Localization")]
        public bool German = false;

        [Header("Rendering")]
        public Material ChunkMaterial;
        public Material ChunkMaterialTransparent; // submesh 1: see-through glass + energy fields

        // Our avatar colours (packed 0xRRGGBB), set by WorldRig; sent to the server on join.
        public int SkinRgb = 0xD9AE8C;
        public int TorsoRgb = 0x3372CC;
        public int ArmRgb = 0x3372CC;
        public int LegRgb = 0x40404F;

        // Our ship hull colour (packed 0xRRGGBB), set by WorldRig; sent on join and read by the flight view
        // to tint the ship (item 32). Default = the steel tint the hull used before hull colours existed.
        public int HullRgb = 0xD1D6E0;

        public GameContent Content { get; private set; }
        public Localizer Localizer { get; private set; }
        public NetworkClient Network { get; private set; }
        public ClientWorld World { get; private set; }
        public BlockTextureAtlas Atlas { get; private set; }

        // Latest authoritative player vitals for the HUD.
        public float Health { get; private set; } = 100f;
        public float Oxygen { get; private set; } = 100f;
        public float SuitEnergy { get; private set; } = 100f;
        public float Hunger { get; private set; } = 100f;

        /// <summary>Our own player id (from the join handshake), used to pick our state out of broadcasts.</summary>
        public string LocalPlayerId { get; private set; }

        /// <summary>Friendly current location ("System · Planet") for the HUD.</summary>
        public string LocationName { get; private set; } = string.Empty;
        public string StationName { get; private set; } = string.Empty; // non-empty while boarded on a station

        /// <summary>Whether we're currently inside the ship (authoritative; enables cargo crafting).</summary>
        public bool Aboard { get; private set; }
        public bool InEva { get; private set; } // server-authoritative: floating outside the ship in space

        /// <summary>When set, the on-foot player is above the atmosphere (zero-g) and must float instead of
        /// fall — <see cref="PlayerController"/> drops gravity. Groundwork for item 10 (building a structure up
        /// into space); nothing sets it yet, so it's a no-op until that lands.</summary>
        public bool OnFootInSpace { get; set; }

        /// <summary>World position of the player's ship (for the HUD minimap / compass), once known.</summary>
        public Vector3? ShipPosition { get; private set; }

        /// <summary>The player's current world position and heading, written by the controller for the HUD.</summary>
        public Vector3 PlayerPosition;
        public float PlayerYaw;

        /// <summary>An optional world-map waypoint (XZ); the HUD compass points to it when set.</summary>
        public Vector3? Waypoint;

        /// <summary>Interactive ship stations, and the one the player is currently next to (or empty).</summary>
        public NetShipStation[] Stations { get; private set; } = System.Array.Empty<NetShipStation>();

        /// <summary>Planet points of interest (settlement, …) for the world map.</summary>
        public NetPoi[] PlanetPois { get; private set; } = System.Array.Empty<NetPoi>();

        /// <summary>Placed radio beacons (labelled waypoints) for the world map + compass (item 37).</summary>
        public NetBeacon[] Beacons { get; private set; } = System.Array.Empty<NetBeacon>();

        /// <summary>Fixed landing pads + occupancy for the most-recently-known body (item 38): the active body
        /// (world map markers) or the body the pad chooser asked about. Keyed by <see cref="LandingPadsBody"/>.</summary>
        public NetLandingPad[] LandingPads { get; private set; } = System.Array.Empty<NetLandingPad>();
        public string LandingPadsBody { get; private set; } = string.Empty;
        public string NearbyStation;

        // Navigation, missions & rules (M23).
        public StarMapData StarMap { get; private set; }
        public MissionList Missions { get; private set; }
        public ServerRules Rules { get; private set; }

        // Space flight & combat (M25).
        public ShipCombatStatus ShipCombat { get; private set; }
        public SpaceState Space { get; private set; }       // current space instance (null when not flying)
        public SpaceShipDesign ShipDesign { get; private set; } // item 20 S1: own ship as a voxel structure (flight view)
        public bool InSpace { get; private set; }
        public bool SpaceSkipLaunch { get; private set; }    // entered space already airborne (helm) → no take-off anim
        public NetCombatEntity[] PlanetEnemies { get; private set; } = System.Array.Empty<NetCombatEntity>();

        /// <summary>Live procedural creatures near the player (fauna), with their species descriptor.</summary>
        public NetCreature[] Creatures { get; private set; } = System.Array.Empty<NetCreature>();

        /// <summary>Settlement / station NPCs (vendors, quartermasters, settlers) near the player.</summary>
        public NetNpc[] Npcs { get; private set; } = System.Array.Empty<NetNpc>();

        /// <summary>True when a settlement/station vendor stands within trading reach — enables market
        /// barter at their stall (the server also allows it aboard your ship).</summary>
        public bool NearVendor
        {
            get
            {
                var p = PlayerPosition;
                foreach (var n in Npcs)
                {
                    if (n.Role != "vendor")
                    {
                        continue;
                    }

                    var np = ScenePos(n.X, n.Y, n.Z);
                    if ((np - p).sqrMagnitude <= 3.6f * 3.6f)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Whether the player can barter at a market right now (aboard ship or at a vendor).</summary>
        public bool MarketAvailable => Aboard || NearVendor;

        /// <summary>Lootable containers on the planet (salvage capsules / corpses).</summary>
        public NetContainer[] Containers { get; private set; } = System.Array.Empty<NetContainer>();

        /// <summary>The player's owned ships and which is active (M-ships).</summary>
        public NetOwnedShip[] OwnedShips { get; private set; } = System.Array.Empty<NetOwnedShip>();

        /// <summary>Latest day/night + weather + sun colour (World systems).</summary>
        public WorldEnvironment Environment { get; private set; }

        /// <summary>World-X span over which the local time-of-day shifts a full cycle. This is the world
        /// circumference (X is a wrapping longitude), so one walk around the world is exactly one day and
        /// the planet has a real day/night terminator: a player far east can be in daylight while one far
        /// west is in night, on the same world.</summary>
        /// <summary>This world's walkable circumference (longitude wrap + day span). Sized per body (asteroids
        /// small, planets large) — sent by the server in <see cref="WorldEnvironment"/>; used for every X wrap.</summary>
        public int Circumference { get; private set; } = WorldConstants.Circumference;

        /// <summary>Time-of-day at the player's position — the server's global day fraction shifted by the
        /// player's longitude (world X), wrapped to 0..1. Drives the sky + HUD clock, so two players at
        /// different X see different times (one's day side, the other's night side).</summary>
        public float LocalTimeOfDay
            => Mathf.Repeat((Environment != null ? Environment.TimeOfDay : 0.5f) + PlayerPosition.x / Circumference, 1f);

        /// <summary>
        /// Maps an authoritative (canonical) world X to the Unity scene X nearest the player. World-X is a
        /// wrapping longitude; the player's transform runs unbounded as it laps the world, so every object
        /// (chunks, remote players, creatures, NPCs) is drawn at the copy closest to the player. In the near
        /// field this just returns the canonical X; only objects across the seam are shifted by ±Circumference
        /// (and those that would flip are beyond view distance anyway, so the flip is never seen).
        /// </summary>
        public float SceneX(double worldX)
            => (float)(PlayerPosition.x + WorldConstants.WrapDeltaX(worldX - PlayerPosition.x, Circumference));

        /// <summary>Convenience: a full world position mapped to the nearest scene position (only X wraps).</summary>
        public Vector3 ScenePos(float worldX, float worldY, float worldZ)
            => new Vector3(SceneX(worldX), worldY, worldZ);

        /// <summary>Most recent handheld/ship scan readout for the HUD, and when it arrived (for auto-hide).</summary>
        public ScanResult LastScan { get; private set; }
        public float LastScanAt { get; private set; }

        /// <summary>Repair progress of the wreck the player is standing in (null until a wreck reports it).</summary>
        public WreckRepairStatus Wreck { get; private set; }

        /// <summary>The open player-to-player trade (both offers + ready states), or null when no trade is active.</summary>
        public TradeUpdate Trade { get; private set; }
        public bool TradeActive { get; private set; }

        /// <summary>A player who has asked to dock with us (awaiting our accept/decline), or empty.</summary>
        public string PendingDockFrom { get; set; } = string.Empty;

        /// <summary>Latest authoritative docking state (partner + docked flag), or null.</summary>
        public DockStatus Dock { get; private set; }

        /// <summary>Type of the nearest station within <paramref name="range"/> blocks, or empty.</summary>
        public string NearestStationType(Vector3 pos, float range)
        {
            string best = string.Empty;
            float bestSq = range * range;
            foreach (var s in Stations)
            {
                float dx = (float)WorldConstants.WrapDeltaX(s.X - pos.x, Circumference), dy = s.Y - pos.y, dz = s.Z - pos.z; // longitude wraps
                float d = dx * dx + dy * dy + dz * dz;
                if (d < bestSq)
                {
                    bestSq = d;
                    best = s.Type;
                }
            }

            return best;
        }

        /// <summary>The station the player is looking AT: raycast forward and return the station nearest the
        /// hit point (so a cramped ship's many stations don't all read as the one you happen to stand on).
        /// Empty if the aim doesn't land on a station. Used in preference to <see cref="NearestStationType"/>.</summary>
        public string LookedStationType(Camera cam, float range)
        {
            if (cam == null || Stations.Length == 0
                || !Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, range))
            {
                return string.Empty;
            }

            string best = string.Empty;
            float bestSq = 1.6f * 1.6f; // the hit must land on (or right next to) a station tile
            foreach (var s in Stations)
            {
                float dx = (float)WorldConstants.WrapDeltaX(s.X - hit.point.x, Circumference), dy = s.Y - hit.point.y, dz = s.Z - hit.point.z;
                float d = dx * dx + dy * dy + dz * dz;
                if (d < bestSq)
                {
                    bestSq = d;
                    best = s.Type;
                }
            }

            return best;
        }

        /// <summary>The authoritative spawn the server reported for us; the rig snaps to it once.</summary>
        public Vector3? ServerSpawn { get; private set; }

        /// <summary>Set when the server respawns us (death) — the player controller snaps the camera/body
        /// to this point (the ship's heal-tank) on the next frame, then clears it.</summary>
        public Vector3? RespawnTarget;

        // Latest authoritative inventory (personal + ship cargo) for the UI.
        public NetItemStack[] Personal { get; private set; } = System.Array.Empty<NetItemStack>();
        public NetItemStack[] Cargo { get; private set; } = System.Array.Empty<NetItemStack>();

        /// <summary>Blueprint keys the player has unlocked (synced from the server) — drives craftable/locked UI.</summary>
        public System.Collections.Generic.HashSet<string> UnlockedBlueprints { get; private set; } = new();

        /// <summary>Current knowledge total, kept in sync via inventory updates (item 11 — shown in the trade UI).</summary>
        public int Knowledge { get; private set; }

        /// <summary>Hotbar slot the player has selected (written by the player controller).</summary>
        public int SelectedHotbarSlot;

        /// <summary>True while an in-game UI panel is open; the player controller pauses look/move.</summary>
        public bool MenuOpen;

        /// <summary>True while the chat input is focused; the player controller pauses look/move.</summary>
        public bool ChatTyping;

        /// <summary>True while the space view owns the camera (on-foot control is frozen).</summary>
        public bool SpaceViewActive;

        /// <summary>Last server feedback line (craft result / rejection / message) for a HUD toast.</summary>
        public string LastMessage { get; private set; } = string.Empty;

        /// <summary>The last cell the player tried to mine (set by the controller) — if the server rejects the
        /// dig as already-empty, the client clears its ghost block there to heal a stale chunk view (B32).</summary>
        public Vector3Int LastMineCell;

        /// <summary>The item key in a personal inventory slot, or empty if none.</summary>
        public string ItemInSlot(int slot)
        {
            foreach (var s in Personal)
            {
                if (s.Slot == slot)
                {
                    return s.Item;
                }
            }

            return string.Empty;
        }

        private readonly Dictionary<ChunkCoord, GameObject> _chunkObjects = new Dictionary<ChunkCoord, GameObject>();
        private readonly HashSet<ChunkCoord> _dirty = new HashSet<ChunkCoord>();

        /// <summary>Bumped each time the world is rebuilt (travel); the player re-snaps to the new spawn.</summary>
        public int WorldEpoch { get; private set; }

        private bool _joinSent;
        private float _retryTimer;
        private int _retries;

        private void Start()
        {
            string dataDir = Path.Combine(Application.streamingAssetsPath, "data");
            Content = ContentLoader.LoadFromDirectory(dataDir);
            Localizer = Content.CreateLocalizer(German ? GameLocale.German : GameLocale.English);
            World = new ClientWorld();

            // Procedurally generate the block texture atlas and a material that samples it
            // (M27). Falls back to whatever WorldRig assigned if the shader is missing.
            Atlas = new BlockTextureAtlas(Content);
            var atlasShader = Shader.Find("Spacecraft/BlockAtlas");
            if (atlasShader != null)
            {
                ChunkMaterial = new Material(atlasShader) { mainTexture = Atlas.Texture };
                ChunkMaterial.SetTexture("_NormalTex", Atlas.NormalTexture); // per-pixel normal mapping

                // Alpha-blended material for the see-through submesh (glass viewports + energy fields).
                var transparentShader = Shader.Find("Spacecraft/BlockAtlasTransparent");
                if (transparentShader != null)
                {
                    ChunkMaterialTransparent = new Material(transparentShader) { mainTexture = Atlas.Texture };
                }
            }
            else
            {
                Atlas = null; // no atlas shader → fall back to the flat palette + vertex-colour material
            }

            Network = new NetworkClient();
            Network.JoinAccepted += m =>
            {
                LocalPlayerId = m.PlayerId;
                LocationName = string.IsNullOrEmpty(m.SystemName)
                    ? m.PlanetName
                    : $"{m.SystemName} · {m.PlanetName}";
                Debug.Log($"Joined as {m.PlayerId} at {LocationName} (seed {m.WorldSeed}).");
                LoadingPlanetType = m.PlanetType;
                WorldReady = false;          // hold the loading overlay until we settle onto the first world
                WorldLoadStarted?.Invoke();
            };
            Network.JoinRejected += m => Debug.LogWarning($"Join rejected: {m.Reason}");
            Network.ChunkReceived += OnChunk;
            Network.BlockChanged += OnBlockChanged;
            Network.PlayerStateUpdated += OnPlayerState;
            Network.InventoryUpdated += m =>
            {
                Personal = m.Personal;
                Cargo = m.Cargo;
                UnlockedBlueprints = new System.Collections.Generic.HashSet<string>(m.UnlockedBlueprints ?? System.Array.Empty<string>());
                Knowledge = m.KnowledgePoints;
            };
            Network.ShipPlacementReceived += m => ShipPosition = new Vector3(m.X, m.Y, m.Z);
            Network.ShipStationsReceived += m => Stations = m.Stations;
            Network.PlanetPoisReceived += m => PlanetPois = m.Pois;
            Network.BeaconsReceived += m => Beacons = m.Beacons ?? System.Array.Empty<NetBeacon>();
            Network.LandingPadsReceived += m => { LandingPads = m.Pads ?? System.Array.Empty<NetLandingPad>(); LandingPadsBody = m.BodyId ?? string.Empty; };
            Network.StarMapReceived += m => StarMap = m;
            Network.MissionsReceived += m => Missions = m;
            Network.ShipCombatStatusChanged += m => ShipCombat = m;
            Network.SpaceStateReceived += m =>
            {
                if (!InSpace)
                {
                    SpaceSkipLaunch = m.SkipLaunch; // latched on entry only (later updates don't re-trigger Enter)
                }

                Space = m;
                InSpace = true;
            };
            // item 20 S1/S3: voxel structures for the flight view. Only the player's OWN ship drives the _ship
            // mesh (Game.ShipDesign); asteroid bodies (Kind != "ship") are handled directly by SpaceView.
            Network.SpaceShipDesignReceived += m => { if (m.Kind == "ship" || string.IsNullOrEmpty(m.Kind)) { ShipDesign = m; } };
            Network.SpaceClosed += m => { InSpace = false; Space = null; LastMessage = m.Reason; };
            Network.StationBoardedReceived += m => LastMessage = $"Boarded {m.Name}.";
            Network.PlanetEnemiesReceived += m => PlanetEnemies = m.Enemies;
            Network.CreaturesReceived += m => Creatures = m.Creatures;
            Network.NpcsReceived += m => Npcs = m.Npcs;
            Network.ContainersReceived += m => Containers = m.Containers;
            Network.OwnedShipsReceived += m => OwnedShips = m.Ships;
            Network.WorldEnvironmentReceived += m =>
            {
                Environment = m;
                Circumference = m.Circumference > 0 ? m.Circumference : WorldConstants.Circumference;
                World?.SetCircumference(Circumference); // chunk/block wrap at this world's size
            };
            Network.WorldResetReceived += OnWorldReset;
            Network.ScanResultReceived += m =>
            {
                LastScan = m;
                LastScanAt = Time.time;
                if (m.FirstTime && m.KnowledgeGained > 0)
                {
                    LastMessage = $"+{m.KnowledgeGained} knowledge ({m.KnowledgeTotal})";
                }
            };
            Network.WreckRepairStatusChanged += m => Wreck = m.Claimed ? null : m;
            Network.TradeUpdated += m => { Trade = m; TradeActive = true; };
            Network.TradeClosedReceived += m =>
            {
                TradeActive = false;
                Trade = null;
                LastMessage = m.Completed ? "Trade complete." : m.Reason;
            };
            Network.DockRequested += m => { PendingDockFrom = m.Requester; LastMessage = $"{m.Requester} requests docking."; };
            Network.DockStatusChanged += m =>
            {
                Dock = m;
                LastMessage = m.Docked ? $"Docked with {m.Partner}" : m.Reason;
            };
            Network.MissionResultReceived += m => LastMessage = m.Success ? $"Mission '{m.MissionId}' complete!" : $"Mission: {m.Reason}";
            Network.RespawnNoticeReceived += m =>
            {
                LastMessage = m.Reason;
                RespawnTarget = new Vector3(m.X, m.Y, m.Z); // teleport the body to the heal-tank on respawn
            };
            Network.ServerRulesReceived += m => { Rules = m; LastMessage = $"Mode: {m.GameMode} · PvP: {m.Pvp}"; };
            Network.CraftCompleted += m => LastMessage = m.Success ? $"Crafted {m.RecipeKey}" : $"Craft failed: {m.Reason}";
            Network.ActionRejected += m =>
            {
                Debug.Log($"Action '{m.Action}' rejected: {m.Reason}");
                LastMessage = $"{m.Action}: {m.Reason}";

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
                        _dirty.Add(ghostCoord);
                    }
                }
            };
            Network.ServerMessageReceived += m => { Debug.Log(m.Text); LastMessage = m.Text; };

            // Connect now; the join handshake is sent once the transport reports Connected
            // (UDP connect is asynchronous — sending the join before that would be dropped).
            Network.Connect(Host, Port);
        }

        /// <summary>True when the player has open sky overhead (no roof/cave ceiling). Drives weather
        /// suppression in caves / indoors. Refreshed a few times a second.</summary>
        public bool ExposedToSky { get; private set; } = true;
        private float _skyScanTimer;

        private void Update()
        {
            Network?.Poll();

            _skyScanTimer -= Time.deltaTime;
            if (_skyScanTimer <= 0f)
            {
                _skyScanTimer = 0.2f;
                ExposedToSky = ComputeExposedToSky();
            }

            if (Network != null && !_joinSent)
            {
                if (Network.Connected)
                {
                    Network.Join(PlayerName, string.IsNullOrEmpty(Password) ? null : Password, German ? "de" : "en");
                    Network.SendAppearance(SkinRgb, TorsoRgb, ArmRgb, LegRgb, HullRgb);
                    _joinSent = true;
                }
                else
                {
                    // Safety net: re-attempt the connection a few times (e.g. the local
                    // singleplayer server is still starting up).
                    _retryTimer += Time.deltaTime;
                    if (_retryTimer >= 2f && _retries < 6)
                    {
                        _retryTimer = 0f;
                        _retries++;
                        Network.Connect(Host, Port);
                    }
                }
            }

            // Rebuild any chunk meshes that changed this frame.
            if (_dirty.Count > 0)
            {
                foreach (var coord in _dirty)
                {
                    RebuildChunk(coord);
                }

                _dirty.Clear();
            }

            // Longitude wraps: keep every loaded chunk drawn at the copy nearest the player as it laps the
            // world. Near chunks resolve to their canonical X (a no-op write); only chunks across the seam
            // shift by ±Circumference. Throttled to once per block of X movement.
            int chunkAnchor = Mathf.FloorToInt(PlayerPosition.x);
            if (chunkAnchor != _lastReposX)
            {
                _lastReposX = chunkAnchor;
                RepositionChunks();
            }
        }

        private int _lastReposX = int.MinValue;

        /// <summary>Re-places every loaded chunk GameObject at the seam-aware scene position for the player's
        /// current longitude (see <see cref="SceneX"/>).</summary>
        private void RepositionChunks()
        {
            foreach (var kv in _chunkObjects)
            {
                if (kv.Value == null)
                {
                    continue;
                }

                var origin = WorldConstants.ChunkOrigin(kv.Key);
                kv.Value.transform.position = new Vector3(SceneX(origin.X), origin.Y, origin.Z);
            }
        }

        private void OnChunk(Spacecraft.Networking.Messages.ChunkDataMessage m)
        {
            var coord = new ChunkCoord(m.Cx, m.Cy, m.Cz);
            World.StoreChunk(coord, m.Blocks);
            _dirty.Add(coord);
        }

        /// <summary>Scans straight up from the player's head for a roof/ceiling — false = covered (cave/indoors).</summary>
        private bool ComputeExposedToSky()
        {
            if (World == null)
            {
                return true;
            }

            int px = Mathf.FloorToInt(PlayerPosition.x);
            int py = Mathf.FloorToInt(PlayerPosition.y);
            int pz = Mathf.FloorToInt(PlayerPosition.z);
            for (int y = py + 2; y <= py + 50; y++)
            {
                if (!World.GetBlock(px, y, pz).IsAir)
                {
                    return false; // a solid block overhead = no open sky
                }
            }

            return true;
        }

        /// <summary>The active world changed (travel): drop all chunks/meshes so the new planet streams in.</summary>
        private void OnWorldReset(Spacecraft.Networking.Messages.WorldReset m)
        {
            LocationName = string.IsNullOrEmpty(m.SystemName) ? m.PlanetName : $"{m.SystemName} · {m.PlanetName}";

            foreach (var go in _chunkObjects.Values)
            {
                if (go != null)
                {
                    // SetActive(false) takes effect immediately; Destroy is deferred to frame end. Without
                    // this the settle-freeze raycast (PlayerController) could still hit a stale collider from
                    // the *old* world this frame, "find ground", release early, and drop the player into the
                    // void before the new world's floor chunk has streamed in (the station-boarding fall).
                    go.SetActive(false);
                    Destroy(go);
                }
            }

            _chunkObjects.Clear();
            _dirty.Clear();
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

        private static readonly (int X, int Y, int Z)[] _faceDirs =
        {
            (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1),
        };

        private void OnBlockChanged(Spacecraft.Networking.Messages.BlockChanged m)
        {
            if (!World.ApplyBlockChange(m.X, m.Y, m.Z, m.Block, out var coord))
            {
                return;
            }

            _dirty.Add(coord);

            // A block on a chunk edge also changes the NEIGHBOUR chunk's face toward it. Re-mesh that
            // neighbour too — otherwise mining a boundary block leaves the adjacent chunk's now-exposed
            // face missing (a hole you can see through), and placing leaves a stale hidden face.
            foreach (var d in _faceDirs)
            {
                var nc = new ChunkCoord(
                    WorldConstants.CanonicalChunkX(WorldConstants.WorldToChunk(m.X + d.X), Circumference),
                    WorldConstants.WorldToChunk(m.Y + d.Y),
                    WorldConstants.WorldToChunk(m.Z + d.Z));
                if (!nc.Equals(coord))
                {
                    _dirty.Add(nc);
                }
            }
        }

        private void OnPlayerState(Spacecraft.Networking.Messages.PlayerStateUpdate m)
        {
            // Only our own authoritative state drives the HUD and the one-time spawn snap.
            if (!string.IsNullOrEmpty(LocalPlayerId) && m.PlayerId != LocalPlayerId)
            {
                return;
            }

            Health = m.Health;
            Oxygen = m.Oxygen;
            SuitEnergy = m.SuitEnergy;
            Hunger = m.Hunger;
            Aboard = m.AboardShip;
            InEva = m.InEva;

            // Built/climbed above the atmosphere → zero-g float on foot + space sky (item 10).
            if (m.AboveAtmosphere != OnFootInSpace)
            {
                OnFootInSpace = m.AboveAtmosphere;
                LastMessage = Localizer?.Get(m.AboveAtmosphere ? "hud.atmosphere.left" : "hud.atmosphere.entered") ?? LastMessage;
            }

            // Boarding or leaving a space station is a server-side teleport (to the station interior, or
            // back to the ship). Snap the body to the new authoritative position — otherwise the player
            // stayed where they were and appeared to "land on the planet" instead of docking.
            if (m.StationName != StationName)
            {
                RespawnTarget = new Vector3(m.X, m.Y, m.Z);
            }

            StationName = m.StationName;
            ServerSpawn ??= new Vector3(m.X, m.Y, m.Z);
        }

        private void RebuildChunk(ChunkCoord coord)
        {
            if (!World.TryGetChunk(coord, out var chunk))
            {
                return;
            }

            var (mesh, collider) = ChunkMesher.Build(chunk, Content, World.GetBlock, Atlas);

            if (!_chunkObjects.TryGetValue(coord, out var go))
            {
                go = new GameObject($"Chunk {coord.X},{coord.Y},{coord.Z}");
                go.transform.SetParent(transform);
                var origin = WorldConstants.ChunkOrigin(coord);
                go.transform.position = new Vector3(SceneX(origin.X), origin.Y, origin.Z); // seam-aware (longitude wraps)
                go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                // Submesh 0 → opaque atlas, submesh 1 → see-through atlas (glass/fields). Fall back to a
                // single material if the transparent shader is missing (the spare submesh just won't draw).
                mr.sharedMaterials = ChunkMaterialTransparent != null
                    ? new[] { ChunkMaterial, ChunkMaterialTransparent }
                    : new[] { ChunkMaterial };
                go.AddComponent<MeshCollider>();
                _chunkObjects[coord] = go;
            }

            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            // The collider uses the solid-only mesh (fluids excluded) so the player can swim into water/lava;
            // null (a chunk of only fluids/air) clears the collider so nothing blocks there.
            go.GetComponent<MeshCollider>().sharedMesh = collider;
        }

        private void OnDestroy() => Network?.Dispose();
    }
}
