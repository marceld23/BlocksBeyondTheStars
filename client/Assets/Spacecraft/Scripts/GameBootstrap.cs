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

        // Our avatar colours (packed 0xRRGGBB), set by WorldRig; sent to the server on join.
        public int SkinRgb = 0xD9AE8C;
        public int TorsoRgb = 0x3372CC;
        public int ArmRgb = 0x3372CC;
        public int LegRgb = 0x40404F;

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

        /// <summary>Whether we're currently inside the ship (authoritative; enables cargo crafting).</summary>
        public bool Aboard { get; private set; }

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
        public string NearbyStation;

        // Navigation, missions & rules (M23).
        public StarMapData StarMap { get; private set; }
        public MissionList Missions { get; private set; }
        public ServerRules Rules { get; private set; }

        // Space flight & combat (M25).
        public ShipCombatStatus ShipCombat { get; private set; }
        public SpaceState Space { get; private set; }       // current space instance (null when not flying)
        public bool InSpace { get; private set; }
        public NetCombatEntity[] PlanetEnemies { get; private set; } = System.Array.Empty<NetCombatEntity>();

        /// <summary>Live procedural creatures near the player (fauna), with their species descriptor.</summary>
        public NetCreature[] Creatures { get; private set; } = System.Array.Empty<NetCreature>();

        /// <summary>Lootable containers on the planet (salvage capsules / corpses).</summary>
        public NetContainer[] Containers { get; private set; } = System.Array.Empty<NetContainer>();

        /// <summary>The player's owned ships and which is active (M-ships).</summary>
        public NetOwnedShip[] OwnedShips { get; private set; } = System.Array.Empty<NetOwnedShip>();

        /// <summary>Latest day/night + weather + sun colour (World systems).</summary>
        public WorldEnvironment Environment { get; private set; }

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
                float dx = s.X - pos.x, dy = s.Y - pos.y, dz = s.Z - pos.z;
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

        // Latest authoritative inventory (personal + ship cargo) for the UI.
        public NetItemStack[] Personal { get; private set; } = System.Array.Empty<NetItemStack>();
        public NetItemStack[] Cargo { get; private set; } = System.Array.Empty<NetItemStack>();

        /// <summary>Blueprint keys the player has unlocked (synced from the server) — drives craftable/locked UI.</summary>
        public System.Collections.Generic.HashSet<string> UnlockedBlueprints { get; private set; } = new();

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
            };
            Network.ShipPlacementReceived += m => ShipPosition = new Vector3(m.X, m.Y, m.Z);
            Network.ShipStationsReceived += m => Stations = m.Stations;
            Network.PlanetPoisReceived += m => PlanetPois = m.Pois;
            Network.StarMapReceived += m => StarMap = m;
            Network.MissionsReceived += m => Missions = m;
            Network.ShipCombatStatusChanged += m => ShipCombat = m;
            Network.SpaceStateReceived += m => { Space = m; InSpace = true; };
            Network.SpaceClosed += m => { InSpace = false; Space = null; LastMessage = m.Reason; };
            Network.PlanetEnemiesReceived += m => PlanetEnemies = m.Enemies;
            Network.CreaturesReceived += m => Creatures = m.Creatures;
            Network.ContainersReceived += m => Containers = m.Containers;
            Network.OwnedShipsReceived += m => OwnedShips = m.Ships;
            Network.WorldEnvironmentReceived += m => Environment = m;
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
            Network.RespawnNoticeReceived += m => LastMessage = m.Reason;
            Network.ServerRulesReceived += m => { Rules = m; LastMessage = $"Mode: {m.GameMode} · PvP: {m.Pvp}"; };
            Network.CraftCompleted += m => LastMessage = m.Success ? $"Crafted {m.RecipeKey}" : $"Craft failed: {m.Reason}";
            Network.ActionRejected += m => { Debug.Log($"Action '{m.Action}' rejected: {m.Reason}"); LastMessage = $"{m.Action}: {m.Reason}"; };
            Network.ServerMessageReceived += m => { Debug.Log(m.Text); LastMessage = m.Text; };

            // Connect now; the join handshake is sent once the transport reports Connected
            // (UDP connect is asynchronous — sending the join before that would be dropped).
            Network.Connect(Host, Port);
        }

        private void Update()
        {
            Network?.Poll();

            if (Network != null && !_joinSent)
            {
                if (Network.Connected)
                {
                    Network.Join(PlayerName, string.IsNullOrEmpty(Password) ? null : Password);
                    Network.SendAppearance(SkinRgb, TorsoRgb, ArmRgb, LegRgb);
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
        }

        private void OnChunk(Spacecraft.Networking.Messages.ChunkDataMessage m)
        {
            var coord = new ChunkCoord(m.Cx, m.Cy, m.Cz);
            World.StoreChunk(coord, m.Blocks);
            _dirty.Add(coord);
        }

        /// <summary>The active world changed (travel): drop all chunks/meshes so the new planet streams in.</summary>
        private void OnWorldReset(Spacecraft.Networking.Messages.WorldReset m)
        {
            LocationName = string.IsNullOrEmpty(m.SystemName) ? m.PlanetName : $"{m.SystemName} · {m.PlanetName}";

            foreach (var go in _chunkObjects.Values)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }

            _chunkObjects.Clear();
            _dirty.Clear();
            World.Clear();

            ServerSpawn = null; // re-snap at the new spawn once the next PlayerState arrives
            WorldEpoch++;
            LastMessage = $"Arriving at {m.PlanetName}…";
        }

        private void OnBlockChanged(Spacecraft.Networking.Messages.BlockChanged m)
        {
            if (World.ApplyBlockChange(m.X, m.Y, m.Z, m.Block, out var coord))
            {
                _dirty.Add(coord);
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
            ServerSpawn ??= new Vector3(m.X, m.Y, m.Z);
        }

        private void RebuildChunk(ChunkCoord coord)
        {
            if (!World.TryGetChunk(coord, out var chunk))
            {
                return;
            }

            var mesh = ChunkMesher.Build(chunk, Content, World.GetBlock, Atlas);

            if (!_chunkObjects.TryGetValue(coord, out var go))
            {
                go = new GameObject($"Chunk {coord.X},{coord.Y},{coord.Z}");
                go.transform.SetParent(transform);
                var origin = WorldConstants.ChunkOrigin(coord);
                go.transform.position = new Vector3(origin.X, origin.Y, origin.Z);
                go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ChunkMaterial;
                go.AddComponent<MeshCollider>();
                _chunkObjects[coord] = go;
            }

            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshCollider>().sharedMesh = mesh;
        }

        private void OnDestroy() => Network?.Dispose();
    }
}
