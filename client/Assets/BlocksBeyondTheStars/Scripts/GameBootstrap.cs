using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
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
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("Connection")]
        public string Host = "127.0.0.1";
        public int Port = 31415;
        public string PlayerName = "Pilot";
        public string Password = "";

        /// <summary>Per-install name-verification secret (see <see cref="ClientSettings.PlayerToken"/>).</summary>
        public string Token = "";

        /// <summary>While this client hosts the server in-game: the "ip:port" friends join over the LAN
        /// (announced in chat + as a toast). Empty when joining a remote server or in singleplayer.</summary>
        public string HostInfo = "";

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

        /// <summary>Our custom pixel face (16×16 palette-index string; empty = default), set by WorldRig and
        /// sent to the server on join so other players see it. Kept here so the edit path can re-send it.</summary>
        public string FacePixels = "";

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
        public string CurrentStationId { get; private set; } = string.Empty; // id of the boarded station (for in-station rename)

        /// <summary>AI-core tier of the active ship (1 = bare VEGA, 2 = Mk2, 3 = Mk3) — gates the autopilot.</summary>
        public int AiCoreTier { get; private set; } = 1;

        /// <summary>True while the VEGA onboarding chain is active (an objective chip is showing) — the
        /// Settings tab offers "skip tutorial" then, "restart tutorial" otherwise.</summary>
        public bool OnboardingActive { get; private set; }

        // Per-species flora tints for the current world (seed + location → one colour per flora block),
        // resolved by the chunk mesher into TEXCOORD2.yzw. Rebuilt on join and on every world change.
        private long _worldSeed;

        /// <summary>The world seed (from JoinAccepted) — drives the per-world flora colours, also for
        /// OTHER bodies seen from orbit (FloraTints is a pure function of seed + location + species).</summary>
        public long WorldSeed => _worldSeed;
        private System.Collections.Generic.Dictionary<ushort, Color> _floraTintByBlock;

        /// <summary>Recomputes the world's per-species flora colours (deterministic from seed + location,
        /// so every client agrees). Chunks meshed afterwards pick them up via the tint resolver.</summary>
        private void RebuildFloraTints()
        {
            if (Content == null)
            {
                return;
            }

            var map = new System.Collections.Generic.Dictionary<ushort, Color>();
            foreach (var def in Content.Blocks.Values)
            {
                if (!def.Key.StartsWith("flora_", System.StringComparison.Ordinal) && def.Key != "tree_leaves")
                {
                    continue;
                }

                var (r, g, b) = BlocksBeyondTheStars.Shared.World.FloraTints.For(_worldSeed, LocationName, def.Key);
                // The mesher writes these into TEXCOORD2 and the block shader multiplies them raw —
                // convert the sRGB-authored hue at this boundary (no-op in Gamma space).
                map[def.NumericId.Value] = ShaderColor.Srgb(new Color(r, g, b));
            }

            _floraTintByBlock = map;
        }

        /// <summary>The mesher's tint lookup: a flora block's per-world colour, black (= "use the global
        /// planet hue") when unknown.</summary>
        private Color FloraTintFor(BlockId id)
            => _floraTintByBlock != null && _floraTintByBlock.TryGetValue(id.Value, out var c) ? c : Color.black;

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

        /// <summary>Placed beam blocks (teleporter pads) on the current world, for the map + the transporter panel.</summary>
        public NetBeam[] Beams { get; private set; } = System.Array.Empty<NetBeam>();

        /// <summary>Live hover speeders on the current world (parked + driven). <see cref="SpeederView"/> renders
        /// them; <see cref="PlayerController"/> reads them to board/drive. Server-authoritative.</summary>
        public NetSpeeder[] Speeders { get; private set; } = System.Array.Empty<NetSpeeder>();

        /// <summary>Id of the speeder the local player is currently driving ("" = on foot) — from the authoritative
        /// player state. Drives the vehicle HUD + the drive controls.</summary>
        public string InSpeeder { get; private set; } = string.Empty;

        /// <summary>Current local speeder ground speed (m/s, signed), published by <see cref="PlayerController"/>
        /// while driving so the vehicle HUD can show it.</summary>
        public float SpeederSpeed;

        /// <summary>The local player's driven speeder (null if not driving), for the HUD gauges + drive physics.</summary>
        public NetSpeeder DrivenSpeeder
        {
            get
            {
                if (string.IsNullOrEmpty(InSpeeder))
                {
                    return null;
                }

                foreach (var s in Speeders)
                {
                    if (s != null && s.Id == InSpeeder)
                    {
                        return s;
                    }
                }

                return null;
            }
        }

        /// <summary>Pending one-shot speeder effects (deploy shimmer / destruction burst). <see cref="SpeederView"/>
        /// drains this each frame and plays each at its position.</summary>
        public readonly System.Collections.Generic.List<SpeederFx> PendingSpeederFx = new();

        /// <summary>True if the local player may use this beam block as a source/destination: they own it or are
        /// allied with the owner (admins are handled server-side). Drives the transporter's destination list.</summary>
        public bool CanUseBeam(NetBeam b)
        {
            if (b == null)
            {
                return false;
            }

            if (b.OwnerId == LocalPlayerId)
            {
                return true;
            }

            var allies = Alliances?.Allies;
            if (allies != null)
            {
                foreach (var a in allies)
                {
                    if (a.PartnerId == b.OwnerId)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Player-founded planet bases (Grundstein) on the current world, for the planet map markers.</summary>
        public NetBase[] Bases { get; private set; } = System.Array.Empty<NetBase>();

        /// <summary>Fixed landing pads + occupancy for the most-recently-known body (item 38): the active body
        /// (world map markers) or the body the pad chooser asked about. Keyed by <see cref="LandingPadsBody"/>.</summary>
        public NetLandingPad[] LandingPads { get; private set; } = System.Array.Empty<NetLandingPad>();
        public string LandingPadsBody { get; private set; } = string.Empty;

        /// <summary>The day fraction (0..1) the player arrives at when landing on <see cref="LandingPadsBody"/> —
        /// drives the day/night terminator on the pad chooser map (local time at a pad = this + padX/circ).</summary>
        public float LandingPadsTimeOfDay { get; private set; } = 0.35f;
        public string NearbyStation;

        // Navigation, missions & rules (M23).
        public StarMapData StarMap { get; private set; }
        public MissionList Missions { get; private set; }
        public ServerRules Rules { get; private set; }

        /// <summary>This player's alliance roster (mutual allies + pending requests), for the Alliances menu tab.</summary>
        public AllianceList Alliances { get; private set; } = new AllianceList();

        /// <summary>This player's tamed-creature roster, for the Companions menu tab. Refreshed by the server.</summary>
        public CompanionList Companions { get; private set; } = new CompanionList();

        /// <summary>Live taming-ritual state (decoded mood + what the creature wants now), or null when no
        /// attempt is in progress. Drives the HUD taming prompt + response buttons.</summary>
        public TameProgress TameState { get; private set; }

        /// <summary>Badge flag: a creature was tamed since the player last opened the Companions tab.</summary>
        public bool NewCompanionUnseen { get; private set; }
        public void MarkCompanionsSeen() => NewCompanionUnseen = false;

        /// <summary>Recent radio (Funk) chat lines mirrored for the Alliances-tab chat panel (oldest first, capped).
        /// The standalone <see cref="ChatUi"/> overlay keeps its own buffer; this is the shared feed for the tab.</summary>
        public System.Collections.Generic.IReadOnlyList<ChatMessage> RecentChat => _recentChat;
        private readonly System.Collections.Generic.List<ChatMessage> _recentChat = new();
        private const int RecentChatMax = 60;

        /// <summary>The Instant Travel world option: when on, the travel screen may quick-travel anywhere.</summary>
        public bool InstantTravel => Rules?.InstantTravel ?? false;

        /// <summary>True if THIS player has landed on the body (a quick-travel target when Instant Travel is off).</summary>
        public bool HasLandedOn(string bodyId)
            => StarMap?.LandedBodyIds != null && System.Array.IndexOf(StarMap.LandedBodyIds, bodyId) >= 0;

        /// <summary>True if THIS player has entered the system (its bodies + mini map are revealed; else it is
        /// a single "hyperjump here" entry).</summary>
        public bool KnowsSystem(string systemId)
            => StarMap?.KnownSystemIds != null && System.Array.IndexOf(StarMap.KnownSystemIds, systemId) >= 0;

        /// <summary>True if THIS player has a commissioned space station orbiting the given host body.</summary>
        public bool HasMyStation(string bodyId)
            => StarMap?.MyStationBodyIds != null && System.Array.IndexOf(StarMap.MyStationBodyIds, bodyId) >= 0;

        /// <summary>The name of THIS player's base on the given body, or null if they have no base there.</summary>
        public string MyBaseName(string bodyId)
        {
            if (StarMap?.MyBases == null)
            {
                return null;
            }

            foreach (var b in StarMap.MyBases)
            {
                if (b.BodyId == bodyId)
                {
                    return b.Name;
                }
            }

            return null;
        }

        /// <summary>True if THIS player has founded a base on the given body.</summary>
        public bool HasMyBase(string bodyId) => MyBaseName(bodyId) != null;

        // Space flight & combat (M25).
        public ShipCombatStatus ShipCombat { get; private set; }
        public SpaceState Space { get; private set; }       // current space instance (null when not flying)
        public SpaceShipDesign ShipDesign { get; private set; } // item 20 S1: own ship as a voxel structure (flight view)

        // OTHER players' voxel ship designs (Kind "ship_remote", Id "ship:<playerId>"), cached per pilot —
        // the flight view + the landing/launch FX render their REAL ships instead of generic silhouettes.
        private readonly System.Collections.Generic.Dictionary<string, SpaceShipDesign> _remoteShipDesigns = new();

        /// <summary>The cached voxel design of another player's ship, or null when none arrived yet.</summary>
        public SpaceShipDesign RemoteShipDesignFor(string playerId)
            => !string.IsNullOrEmpty(playerId) && _remoteShipDesigns.TryGetValue(playerId, out var d) ? d : null;

        /// <summary>Pending NPC-trader warp flashes (a localized hyperspace burst). The flight view drains this
        /// each frame and plays each one — so everyone in the instance sees traders arrive/depart.</summary>
        public readonly System.Collections.Generic.List<SpaceWarpFx> PendingWarpFx = new();

        /// <summary>Ships parked on the current world as placed structure objects (ship-as-object), keyed by
        /// structure id ("ship:&lt;playerId&gt;"). <see cref="LandedShipView"/> renders them; aiming and the
        /// weather column scans query them — the hull is NOT part of the world block grid.</summary>
        public readonly System.Collections.Generic.Dictionary<string, LandedShipModel> LandedShips = new();

        /// <summary>Raised whenever a parked ship is placed, removed or one of its cells changes.</summary>
        public event System.Action LandedShipsChanged;

        /// <summary>The parked-ship SOLID cell at a world position, or air. Outputs the ship + the
        /// structure-local cell so on-foot aiming can mine/place via StructureEditIntent.</summary>
        public BlockId LandedShipBlockAt(int x, int y, int z, out LandedShipModel ship, out BlocksBeyondTheStars.Shared.Geometry.Vector3i local)
        {
            foreach (var s in LandedShips.Values)
            {
                int dx = WorldConstants.WrapDeltaX(x - s.Origin.X, Circumference);
                int dy = y - s.Origin.Y, dz = z - s.Origin.Z;
                if (dx < -3 || dy < -1 || dz < -3 || dx > s.Width + 3 || dy > s.Height + 2 || dz > s.Length + 3)
                {
                    continue; // outside the bounds (+ a silhouette margin for wings/nozzles)
                }

                var l = new BlocksBeyondTheStars.Shared.Geometry.Vector3i(dx, dy, dz);
                var b = s.Get(l);
                if (!b.IsAir)
                {
                    ship = s;
                    local = l;
                    return b;
                }
            }

            ship = null;
            local = default;
            return BlockId.Air;
        }

        /// <summary>The parked ship whose interior BOUNDS contain a world cell (for routing block placement
        /// inside the ship to a structure edit), or null.</summary>
        public LandedShipModel LandedShipBoundsAt(int x, int y, int z, out BlocksBeyondTheStars.Shared.Geometry.Vector3i local)
        {
            foreach (var s in LandedShips.Values)
            {
                int dx = WorldConstants.WrapDeltaX(x - s.Origin.X, Circumference);
                int dy = y - s.Origin.Y, dz = z - s.Origin.Z;
                if (dx >= 0 && dx < s.Width && dy >= 0 && dy <= s.Height && dz >= 0 && dz < s.Length)
                {
                    local = new BlocksBeyondTheStars.Shared.Geometry.Vector3i(dx, dy, dz);
                    return s;
                }
            }

            local = default;
            return null;
        }

        /// <summary>True when a parked ship's bounding box roofs this column above the given height — a cheap
        /// cover test so weather treats ship roofs like solid cover without scanning cells.</summary>
        public bool LandedShipCovers(int x, int y, int z)
        {
            foreach (var s in LandedShips.Values)
            {
                int dx = WorldConstants.WrapDeltaX(x - s.Origin.X, Circumference);
                if (dx >= -2 && dx <= s.Width + 2 && z >= s.Origin.Z - 2 && z <= s.Origin.Z + s.Length + 2
                    && y < s.Origin.Y + s.Height)
                {
                    return true;
                }
            }

            return false;
        }
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

        /// <summary>Maps an authoritative world Z to the scene Z nearest the player — the latitude twin of
        /// <see cref="SceneX"/> (round worlds: Z wraps at the latitude period like X at the circumference).</summary>
        public float SceneZ(double worldZ)
            => (float)(PlayerPosition.z + WorldConstants.WrapDeltaZ(worldZ - PlayerPosition.z, Circumference));

        /// <summary>Convenience: a full world position mapped to the nearest scene position (X and Z wrap).</summary>
        public Vector3 ScenePos(float worldX, float worldY, float worldZ)
            => new Vector3(SceneX(worldX), worldY, SceneZ(worldZ));

        /// <summary>Most recent handheld/ship scan readout for the HUD, and when it arrived (for auto-hide).</summary>
        public ScanResult LastScan { get; private set; }
        public float LastScanAt { get; private set; }

        /// <summary>Repair progress of the wreck the player is standing in (null until a wreck reports it).</summary>
        public WreckRepairStatus Wreck { get; private set; }

        /// <summary>Own-ship repair readout (hull + missing hull cells), set when the cockpit reports it; null
        /// when the ship is fully intact.</summary>
        public ShipRepairStatus ShipRepair { get; private set; }

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
                float dx = (float)WorldConstants.WrapDeltaX(s.X - pos.x, Circumference), dy = s.Y - pos.y; // both ground axes wrap (torus)
                float dz = (float)WorldConstants.WrapDeltaZ(s.Z - pos.z, Circumference);
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
                float dx = (float)WorldConstants.WrapDeltaX(s.X - hit.point.x, Circumference), dy = s.Y - hit.point.y;
                float dz = (float)WorldConstants.WrapDeltaZ(s.Z - hit.point.z, Circumference);
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

        /// <summary>Minigame keys the player has downloaded from data cubes (synced from the server) — drives
        /// the Arcade collection menu. Mirrors <see cref="UnlockedBlueprints"/>.</summary>
        public System.Collections.Generic.HashSet<string> UnlockedGames { get; private set; } = new();

        /// <summary>Set when a new data-cube minigame arrives but the Arcade hasn't been opened since — the menu
        /// badges the Arcade entry while true. Cleared by <see cref="MarkArcadeSeen"/> when the Arcade is opened.</summary>
        public bool NewArcadeUnseen { get; private set; }
        public void MarkArcadeSeen() => NewArcadeUnseen = false;

        /// <summary>Set when new story content (a beat / net fragment / memory) arrives but the Story tab hasn't
        /// been opened since — the menu badges the Story entry while true. Cleared by <see cref="MarkStorySeen"/>.</summary>
        public bool NewStoryUnseen { get; private set; }
        public void MarkStorySeen() => NewStoryUnseen = false;

        /// <summary>Data cubes on the current world (synced from the server) — rendered by <c>DataCubeView</c>.</summary>
        public NetDataCube[] DataCubes { get; private set; } = System.Array.Empty<NetDataCube>();

        // --- Story system ("The VEGA Protocol") ---

        /// <summary>The active story's shared progress (story P0) — drives the Story Log meter; null/inactive when off.</summary>
        public StoryStateMessage Story { get; private set; }

        /// <summary>Net fragments on the current world (synced) — rendered by <c>NetFragmentView</c>.</summary>
        public NetStoryFragment[] NetFragments { get; private set; } = System.Array.Empty<NetStoryFragment>();

        /// <summary>Story Log: revealed net-fragment entries (category + locale key), re-readable in the Story tab.</summary>
        public readonly System.Collections.Generic.List<(string Category, string TextKey)> StoryLogFragments = new();

        /// <summary>Story Log: unlocked personal-memory locale keys.</summary>
        public readonly System.Collections.Generic.List<string> StoryLogMemories = new();

        /// <summary>Story Log: the VEGA story-beat lines heard so far (locale keys, ShipAiLine kind 2 = memory/story).</summary>
        public readonly System.Collections.Generic.List<string> StoryLogBeats = new();

        /// <summary>Pre-built JSON of the player's discovered systems/worlds + language for the in-game wiki's
        /// discovery-gated chapters. Built on the main thread (so the loopback content server can read this
        /// immutable string race-free) whenever the star map, language or unlocks change.</summary>
        public string WikiStateJson { get; private set; } = "{}";

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

        /// <summary>Set when the server refused our join (wrong password, name in use/verified, full…).
        /// AppShell watches this and returns to the menu showing the reason.</summary>
        public string JoinRejectedReason { get; private set; } = string.Empty;

        /// <summary>Last server feedback line (craft result / rejection / message) for a HUD toast.</summary>
        public string LastMessage { get; private set; } = string.Empty;

        /// <summary>Shows a transient HUD message from a client-side system (e.g. the VEGA autopilot).</summary>
        public void ShowMessage(string text) => LastMessage = text ?? string.Empty;

        /// <summary>Rebuilds <see cref="WikiStateJson"/> from the current star map for the wiki's discovery-gated
        /// Systems &amp; Worlds chapters: only systems the player has entered and bodies they have landed on are
        /// included. Cheap; called on the main thread when the map/language/unlocks change.</summary>
        private void RebuildWikiState()
        {
            var sb = new System.Text.StringBuilder(512);
            sb.Append("{\"lang\":\"").Append(German ? "de" : "en").Append("\",\"systems\":[");
            var systems = StarMap?.Systems ?? System.Array.Empty<NetStarSystem>();
            var knownSys = new System.Collections.Generic.HashSet<string>(StarMap?.KnownSystemIds ?? System.Array.Empty<string>());
            var landed = new System.Collections.Generic.HashSet<string>(StarMap?.LandedBodyIds ?? System.Array.Empty<string>());

            bool firstSys = true;
            var worlds = new System.Text.StringBuilder(512);
            bool firstWorld = true;
            foreach (var sys in systems)
            {
                if (sys == null || !knownSys.Contains(sys.Id)) continue;
                if (!firstSys) sb.Append(','); firstSys = false;
                sb.Append("{\"id\":\"").Append(JsonEscape(sys.Id)).Append("\",\"name\":\"").Append(JsonEscape(sys.Name)).Append("\"}");

                foreach (var body in sys.Bodies ?? System.Array.Empty<NetBody>())
                {
                    if (body == null || !landed.Contains(body.Id)) continue;
                    if (!firstWorld) worlds.Append(','); firstWorld = false;
                    worlds.Append("{\"id\":\"").Append(JsonEscape(body.Id))
                          .Append("\",\"name\":\"").Append(JsonEscape(body.Name))
                          .Append("\",\"type\":\"").Append(JsonEscape(body.PlanetType ?? string.Empty))
                          .Append("\",\"systemId\":\"").Append(JsonEscape(sys.Id))
                          .Append("\",\"systemName\":\"").Append(JsonEscape(sys.Name)).Append("\"}");
                }
            }

            sb.Append("],\"worlds\":[").Append(worlds).Append("]}");
            WikiStateJson = sb.ToString();
        }

        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new System.Text.StringBuilder(s.Length + 4);
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') { sb.Append('\\').Append(c); }
                else if (c == '\n') { sb.Append("\\n"); }
                else if (c >= ' ') { sb.Append(c); }
            }

            return sb.ToString();
        }

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

        // Performance (P1): cap how many chunk meshes are (re)built per frame so a burst of chunks arriving
        // while moving fast spreads over several frames instead of stalling one. Nearest chunks build first;
        // the rest stay queued in _dirty. Tunable — raise for less pop-in, lower for smoother frame times.
        public int MeshChunksPerFrame = 4;
        private readonly List<ChunkCoord> _dirtyScratch = new List<ChunkCoord>();

        // Performance (P2): assigning MeshCollider.sharedMesh cooks the collision mesh synchronously on the
        // main thread — the single heaviest per-chunk op. Instead we run Physics.BakeMesh on a worker thread
        // and assign the (now-cached) cook back on the main thread in DrainBakedColliders. _colliderGen +
        // WorldEpoch guard against a newer rebuild (or a world change) superseding an in-flight bake.
        private readonly Dictionary<ChunkCoord, int> _colliderGen = new Dictionary<ChunkCoord, int>();
        private readonly System.Collections.Concurrent.ConcurrentQueue<(ChunkCoord Coord, Mesh Collider, int Gen, int Epoch)> _bakedColliders
            = new System.Collections.Concurrent.ConcurrentQueue<(ChunkCoord Coord, Mesh Collider, int Gen, int Epoch)>();

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
            // Index dedicated light blocks (+ placed glow blocks) as coloured light sources for the mesher.
            World.SetBlockLightResolver(id => ChunkMesher.BlockLightColor(Content, new BlockId(id), 0));

            // Procedurally generate the block texture atlas and a material that samples it
            // (M27). Falls back to whatever WorldRig assigned if the shader is missing.
            Atlas = new BlockTextureAtlas(Content);
            var atlasShader = Shader.Find("BlocksBeyondTheStars/BlockAtlas");
            if (atlasShader != null)
            {
                ChunkMaterial = new Material(atlasShader) { mainTexture = Atlas.Texture };
                ChunkMaterial.SetTexture("_NormalTex", Atlas.NormalTexture); // per-pixel normal mapping

                // Alpha-blended material for the see-through submesh (glass viewports + energy fields).
                var transparentShader = Shader.Find("BlocksBeyondTheStars/BlockAtlasTransparent");
                if (transparentShader != null)
                {
                    ChunkMaterialTransparent = new Material(transparentShader) { mainTexture = Atlas.Texture };
                }
            }
            else
            {
                Atlas = null; // no atlas shader → fall back to the flat palette + vertex-colour material
            }

            // Held blocks show their REAL atlas tile in the hand (graphics quick-win): resolve a block key
            // to the atlas texture + its tile UV rect; HeldItem falls back to the flat tint without it.
            HeldItem.BlockTileResolver = key =>
                Atlas != null && Content?.GetBlock(key) is { } b && b.NumericId.Value != 0
                    ? (Atlas.Texture, Atlas.TileUv(b.NumericId.Value))
                    : null;

            Network = new NetworkClient();
            Network.JoinAccepted += m =>
            {
                LocalPlayerId = m.PlayerId;
                LocationName = string.IsNullOrEmpty(m.SystemName)
                    ? m.PlanetName
                    : $"{m.SystemName} · {m.PlanetName}";
                Debug.Log($"Joined as {m.PlayerId} at {LocationName} (seed {m.WorldSeed}).");
                LoadingPlanetType = m.PlanetType;
                _worldSeed = m.WorldSeed;
                RebuildFloraTints(); // per-species flora colours for this world (seed + location)
                WorldReady = false;          // hold the loading overlay until we settle onto the first world
                WorldLoadStarted?.Invoke();
            };
            Network.JoinRejected += m =>
            {
                Debug.LogWarning($"Join rejected: {m.Reason}");
                JoinRejectedReason = string.IsNullOrEmpty(m.Reason) ? "Join rejected." : m.Reason;
            };
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
            Network.BeamsReceived += m => Beams = m.Beams ?? System.Array.Empty<NetBeam>();
            Network.BeamTeleportedReceived += m => RespawnTarget = new Vector3(m.X, m.Y, m.Z); // snap the body onto the destination pad
            Network.BasesReceived += m => Bases = m.Bases ?? System.Array.Empty<NetBase>();
            Network.LandingPadsReceived += m => { LandingPads = m.Pads ?? System.Array.Empty<NetLandingPad>(); LandingPadsBody = m.BodyId ?? string.Empty; LandingPadsTimeOfDay = m.TimeOfDay; };
            Network.StarMapReceived += m => { StarMap = m; RebuildWikiState(); };
            Network.DataCubesReceived += m => DataCubes = m.Cubes ?? System.Array.Empty<NetDataCube>();
            Network.GameUnlocksReceived += m =>
            {
                var incoming = new System.Collections.Generic.HashSet<string>(m.Unlocked ?? System.Array.Empty<string>());
                bool grew = incoming.Count > UnlockedGames.Count;
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
            Network.CompanionsReceived += m => Companions = m ?? new CompanionList();
            Network.SpeedersReceived += m => Speeders = m.Speeders ?? System.Array.Empty<NetSpeeder>();
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
                _recentChat.Add(m);
                if (_recentChat.Count > RecentChatMax) _recentChat.RemoveAt(0);
            };
            Network.ShipCombatStatusChanged += m => ShipCombat = m;
            Network.SpaceStateReceived += m =>
            {
                if (!InSpace)
                {
                    SpaceSkipLaunch = m.SkipLaunch; // latched on entry only (later updates don't re-trigger Enter)
                    if (m.Hyperjump)
                    {
                        HyperjumpStarted?.Invoke(); // warp VFX as we arrive in flight in a new system
                    }
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

                    LandedShipsChanged?.Invoke();
                }
            };
            Network.SpaceClosed += m => { InSpace = false; Space = null; LastMessage = m.Reason; };
            Network.StationBoardedReceived += m => { LastMessage = $"Boarded {m.Name}."; CurrentStationId = m.StationId ?? string.Empty; };
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
            Network.ShipAiLineReceived += m =>
            {
                OnboardingActive = !string.IsNullOrEmpty(m.ObjectiveKey);
                if (m.Kind == 2 && !string.IsNullOrEmpty(m.LineKey)) { StoryLogBeats.Add(m.LineKey); NewStoryUnseen = true; } // a story beat
            };
            Network.StoryStateReceived += m => Story = m;
            Network.NetFragmentsReceived += m => NetFragments = m.Fragments;
            Network.NetFragmentRevealedReceived += m =>
            {
                StoryLogFragments.Add((m.Category, m.TextKey));
                NewStoryUnseen = true; // badge the Story menu entry until the player reads it
                ShowMessage(Localizer?.Get(m.TextKey) ?? m.TextKey); // the archive text (Story Log keeps it to re-read)
            };
            Network.PlayerMemoryReceived += m =>
            {
                StoryLogMemories.Add(m.TextKey);
                NewStoryUnseen = true;
                ShowMessage(Localizer?.Get(m.TextKey) ?? m.TextKey);
            };
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
            Network.ShipRepairStatusChanged += m => ShipRepair = m.NeedsRepair ? m : null;
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
                        // Neighbours too: a ghost on a chunk border leaves the adjacent chunk's
                        // now-exposed wall faces missing otherwise (see-through hole when mining fast).
                        MarkChunkAndNeighborsDirty(ghostCoord);
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
                    Network.Join(PlayerName, string.IsNullOrEmpty(Password) ? null : Password, German ? "de" : "en",
                        string.IsNullOrEmpty(Token) ? null : Token);
                    Network.SendAppearance(SkinRgb, TorsoRgb, ArmRgb, LegRgb, HullRgb);
                    if (!string.IsNullOrEmpty(FacePixels))
                    {
                        Network.SendFace(FacePixels); // tell others our custom face (server persists + relays)
                    }

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

            // Assign any collision meshes whose async bake finished (cheap now — the PhysX cook is cached).
            DrainBakedColliders();

            // Rebuild chunk meshes that changed, but cap how many per frame (P1) so a burst of chunks arriving
            // while moving fast spreads over several frames instead of stalling one. Nearest chunks build first;
            // chunks past the budget stay queued for the next frames.
            if (_dirty.Count > 0)
            {
                _dirtyScratch.Clear();
                _dirtyScratch.AddRange(_dirty);
                var pp = PlayerPosition;
                _dirtyScratch.Sort((a, b) => ChunkDistSqToPlayer(a, pp).CompareTo(ChunkDistSqToPlayer(b, pp)));

                int budget = Mathf.Max(1, MeshChunksPerFrame);
                int built = 0;
                foreach (var coord in _dirtyScratch)
                {
                    if (built >= budget)
                    {
                        break;
                    }

                    // Neighbours not yet streamed are no-ops — flush them without spending budget; they
                    // re-mark themselves dirty once their own data arrives.
                    _dirty.Remove(coord);
                    if (RebuildChunk(coord))
                    {
                        built++;
                    }
                }
            }

            // Round worlds: keep every loaded chunk drawn at the copy nearest the player as it laps the world
            // in EITHER direction. Near chunks resolve to their canonical position (a no-op write); only
            // chunks across a seam shift by ±Circumference (X) / ±LatitudePeriod (Z). Throttled to once per
            // block of movement on either axis.
            int chunkAnchorX = Mathf.FloorToInt(PlayerPosition.x);
            int chunkAnchorZ = Mathf.FloorToInt(PlayerPosition.z);
            if (chunkAnchorX != _lastReposX || chunkAnchorZ != _lastReposZ)
            {
                _lastReposX = chunkAnchorX;
                _lastReposZ = chunkAnchorZ;
                RepositionChunks();
            }
        }

        private int _lastReposX = int.MinValue;
        private int _lastReposZ = int.MinValue;

        /// <summary>Re-places every loaded chunk GameObject at the seam-aware scene position for the player's
        /// current longitude AND latitude (see <see cref="SceneX"/>/<see cref="SceneZ"/>).</summary>
        private void RepositionChunks()
        {
            foreach (var kv in _chunkObjects)
            {
                if (kv.Value == null)
                {
                    continue;
                }

                var origin = WorldConstants.ChunkOrigin(kv.Key);
                kv.Value.transform.position = new Vector3(SceneX(origin.X), origin.Y, SceneZ(origin.Z));
            }
        }

        private void OnChunk(BlocksBeyondTheStars.Networking.Messages.ChunkDataMessage m)
        {
            var coord = new ChunkCoord(m.Cx, m.Cy, m.Cz);
            World.StoreChunk(coord, m.Blocks, m.ModIndex, m.ModTint, m.ModGlow, m.ShapeIndex, m.ShapeData);
            MarkChunkAndNeighborsDirty(coord);
        }

        /// <summary>Marks a chunk AND its six neighbours for re-meshing. A freshly stored/resynced chunk
        /// changes which boundary faces its neighbours must draw (e.g. the stale-chunk resync after a fast
        /// double-mine) — re-meshing only the chunk itself left see-through holes in the neighbours' walls.
        /// Cheap: only chunks that already have a mesh actually rebuild.</summary>
        private void MarkChunkAndNeighborsDirty(ChunkCoord coord)
        {
            _dirty.Add(coord);
            foreach (var d in _faceDirs)
            {
                _dirty.Add(new ChunkCoord(
                    WorldConstants.CanonicalChunkX(coord.X + d.X, Circumference),
                    coord.Y + d.Y,
                    WorldConstants.CanonicalChunkZ(coord.Z + d.Z, Circumference)));
            }
        }

        /// <summary>Scans straight up from the player's head for a roof/ceiling — false = covered (cave/indoors).</summary>
        private bool ComputeExposedToSky()
        {
            if (World == null)
            {
                return true;
            }

            if (Aboard)
            {
                return false; // inside the ship (server-authoritative) — the hull object roofs the player
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

            // Parked ship OBJECTS are not in the world grid — standing under a wing/hull still counts as covered.
            return !LandedShipCovers(px, py + 2, pz);
        }

        /// <summary>The active world changed (travel): drop all chunks/meshes so the new planet streams in.</summary>
        private void OnWorldReset(BlocksBeyondTheStars.Networking.Messages.WorldReset m)
        {
            LocationName = string.IsNullOrEmpty(m.SystemName) ? m.PlanetName : $"{m.SystemName} · {m.PlanetName}";
            RebuildFloraTints(); // a new world ⇒ its own per-species flora colours

            // Parked ship objects belong to the world we just left; the new world re-sends its own.
            LandedShips.Clear();
            LandedShipsChanged?.Invoke();

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
            // Bake bookkeeping for the old world is now stale; WorldEpoch (bumped below) fences any in-flight
            // bakes so they're dropped in DrainBakedColliders instead of landing on the new world's chunks.
            _colliderGen.Clear();
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

        private void OnBlockChanged(BlocksBeyondTheStars.Networking.Messages.BlockChanged m)
        {
            if (!World.ApplyBlockChange(m.X, m.Y, m.Z, m.Block, m.Tint, m.Glow, m.Shape, out var coord))
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
                    WorldConstants.CanonicalChunkZ(WorldConstants.WorldToChunk(m.Z + d.Z), Circumference));
                if (!nc.Equals(coord))
                {
                    _dirty.Add(nc);
                }
            }
        }

        private void OnPlayerState(BlocksBeyondTheStars.Networking.Messages.PlayerStateUpdate m)
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
            InSpeeder = m.InSpeeder ?? string.Empty;

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
            if (string.IsNullOrEmpty(StationName))
            {
                CurrentStationId = string.Empty; // left the station — no in-station rename target
            }
            AiCoreTier = m.AiCoreTier;
            ServerSpawn ??= new Vector3(m.X, m.Y, m.Z);
        }

        /// <summary>(Re)builds one chunk's render + collision mesh. Returns false (no work) when the chunk
        /// isn't streamed yet, so the per-frame mesh budget only counts real builds.</summary>
        private bool RebuildChunk(ChunkCoord coord)
        {
            if (!World.TryGetChunk(coord, out var chunk))
            {
                return false;
            }

            var (mesh, collider) = ChunkMesher.Build(chunk, Content, World.GetBlock, Atlas, FloraTintFor,
                lights: World.LightSourcesNear(coord, ChunkMesher.LightRadius), worldShape: World.GetShape);

            if (!_chunkObjects.TryGetValue(coord, out var go))
            {
                go = new GameObject($"Chunk {coord.X},{coord.Y},{coord.Z}");
                go.transform.SetParent(transform);
                var origin = WorldConstants.ChunkOrigin(coord);
                go.transform.position = new Vector3(SceneX(origin.X), origin.Y, SceneZ(origin.Z)); // seam-aware on both ground axes (torus)
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

            // The collider uses the solid-only mesh (fluids excluded) so the player can swim into water/lava.
            // Baking the collision mesh is the heaviest per-chunk step, so do it off the main thread (P2): bump
            // this chunk's bake generation, cook on a worker via Physics.BakeMesh, then assign the cached cook
            // in DrainBakedColliders. A null collider (only fluids/air) clears the collider immediately.
            int gen = (_colliderGen.TryGetValue(coord, out var g) ? g : 0) + 1;
            _colliderGen[coord] = gen;
            var mcol = go.GetComponent<MeshCollider>();
            if (collider == null)
            {
                if (mcol != null)
                {
                    mcol.sharedMesh = null;
                }
            }
            else
            {
                int meshId = collider.GetInstanceID();
                var capturedCoord = coord;
                var capturedCollider = collider;
                int epoch = WorldEpoch;
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        Physics.BakeMesh(meshId, false);
                    }
                    catch
                    {
                        // Mesh may have been destroyed before the bake ran; DrainBakedColliders drops stale ones.
                    }

                    _bakedColliders.Enqueue((capturedCoord, capturedCollider, gen, epoch));
                });
            }

            return true;
        }

        /// <summary>Assigns collision meshes whose off-thread <see cref="Physics.BakeMesh"/> has finished.
        /// Skips and frees any whose chunk was re-meshed since the bake started, whose world changed, or whose
        /// chunk GameObject is gone — so a stale cook never lands on the wrong chunk.</summary>
        private void DrainBakedColliders()
        {
            while (_bakedColliders.TryDequeue(out var baked))
            {
                bool assigned = false;
                if (baked.Epoch == WorldEpoch
                    && _colliderGen.TryGetValue(baked.Coord, out var gen) && gen == baked.Gen
                    && _chunkObjects.TryGetValue(baked.Coord, out var go) && go != null)
                {
                    var mc = go.GetComponent<MeshCollider>();
                    if (mc != null)
                    {
                        mc.sharedMesh = baked.Collider; // cook cached by BakeMesh → cheap assign, no main-thread stall
                        assigned = true;
                    }
                }

                if (!assigned && baked.Collider != null)
                {
                    Destroy(baked.Collider); // superseded / world changed / chunk gone → free the throwaway mesh
                }
            }
        }

        /// <summary>Squared distance from the player to a chunk's centre, using the same seam-aware scene
        /// mapping the chunk objects are placed with — so "nearest first" is correct on the wrapping torus.</summary>
        private float ChunkDistSqToPlayer(ChunkCoord coord, Vector3 player)
        {
            var o = WorldConstants.ChunkOrigin(coord);
            const float half = WorldConstants.ChunkSize * 0.5f;
            float dx = SceneX(o.X) + half - player.x;
            float dy = o.Y + half - player.y;
            float dz = SceneZ(o.Z) + half - player.z;
            return dx * dx + dy * dy + dz * dz;
        }

        private void OnDestroy() => Network?.Dispose();
    }
}
