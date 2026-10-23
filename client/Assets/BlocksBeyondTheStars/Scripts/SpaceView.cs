// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Real space view (M25b). When the server reports the player is in space, this builds a
    /// lightweight space scene far above the world (black sky + starfield + a planet + a blocky
    /// flyable ship + the server's entities) and takes over the camera. The ship is flyable
    /// (WASD + mouse) in third-person or cockpit view (V cycles). A launch sequence plays on
    /// entry; pressing L (or "Return to surface") flies you home with a landing sequence.
    /// On-foot control is frozen meanwhile. Presentation only; combat stays server-authoritative.
    /// </summary>
    public sealed partial class SpaceView : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        /// <summary>The flight system chart (#597), opened with the FlightMap action while cruising.</summary>
        public SpaceMap Map;

        private static readonly Vector3 SceneOrigin = new Vector3(0f, 6000f, 0f);
        private const float SeqDuration = 1.6f;
        private const float LandDuration = 2.2f;  // landing flies the ship away toward the planet — a touch longer so the shrink reads
        private const float BoardDuration = 1.2f; // dock-approach animation before boarding a station
        private const float TransitWarpHold = 2.2f; // #1614: a transit's arrival from the warp holds past the warp's flash (1.95 s) before it lands
        private const float TransitFallback = 9f;   // #1614: no answer this long after reporting a stage (server gives up at 6 s) → controls back
        private const float DockApproachStandOff = 14f;  // #1917: the approach lines up this far in front of the hangar mouth
        private const float DockApproachArrive = 12f;    // #1917: autopilot / chart arrival radius around that point
        private const float DockMouthStandOff = 2.5f;    // #1917: the dock animation ends this far in front of the mouth's field
        private const float DockFlySpeed = 32f;          // #1917: dock-approach speed (flight units per second)
        private const float DockApproachMaxSeconds = 7f; // #1917: the longest dock approach, however far round the hull
        private readonly List<Vector3> _boardPath = new List<Vector3>(); // #1917: planned dock approach (empty = classic fly-in)
        private float _boardDuration = BoardDuration;    // #1917: how long this dock approach takes
        private const float MoveSpeed = 14f;
        private const float LookSpeed = 2.2f;
        private const float Bounds = 130f;
        private const float EvaSpeed = 6.5f;       // suit thrust speed on a spacewalk (slower than the ship)
        private const float EvaShipBoardReach = 5f; // how close the suit must get to the ship's hull (its box) to board it — #2703
        private const float ShipKeepOut = 3.5f;    // suit can't fly into its own ship — bounce off the hull shell

        // Transit (#1614): an automatic transit has just warped into the destination's system — hands-off until it lands.
        private enum Phase { Launch, Cruise, Landing, Boarding, Transit }

        private GameObject _root;
        private GameObject _ship;
        private BlocksBeyondTheStars.Networking.Messages.SpaceShipDesign _builtDesign; // the design the current _ship mesh was built from (item 20 S1)

        // item 20 S2: the player's own ship voxel grid, kept client-side for EVA collision + build/mine aim.
        private Dictionary<Vector3i, BlockId> _shipCells; // null when the cube fallback ship is shown
        // Authored per-voxel dye/glow + shape for the own ship (empty for plain hulls).
        private Dictionary<Vector3i, (int Tint, int Glow)> _shipMods = new();
        private Dictionary<Vector3i, int> _shipShapes = new();
        private Vector3 _shipCentre;        // design → ship-local offset used when meshing
        private Vector3 _shipHalf;          // #2703: half the hull's box (design frame) — the spacewalk's board reach is measured to it
        private string _shipStructureId;    // structure id these cells belong to (matches server edits)
        private Transform _shipVox;         // container holding the voxel chunk meshes (child of _ship)
        private bool _structSubscribed;     // subscribed to structure block-change events
        private Vector3Int _evaAimHit, _evaAimPlace; // aimed solid cell + the empty cell before it (design coords)
        private bool _evaHasAim;
        private string _evaAimStructId;      // id of the structure currently aimed at (ship or asteroid)
        private GameObject _aimHighlight;    // marker on the aimed cell
        private Material _aimHighlightMat;   // marker material — tinted by mining progress (#685)
        private PlacementGhost _evaDoorGhost; // #2119: the door a held door block will hang, shown on the aimed cell
        private List<SpaceDoors.Door> _shipDoors = new();        // #2607: the doors built into the own ship (cells are air)
        private HashSet<Vector3i> _shipDoorCells = new();        // …the cells they fill — the spacewalk aim stops on them
        private Material _doorPanelMat, _doorRimMat, _doorFieldMat; // #2607: shared by every door drawn in space
        private string _evaMineProgressStructId; // structure of the last server mining-progress report (#685)
        private Vector3Int _evaMineProgressCell; // its structure-local cell
        private float _evaMineProgressFraction;  // 0..1 of the way to breaking
        private float _evaMineProgressAt;        // Time.time of the report (stale reports are ignored)
        private float _nextEvaMine;          // hold-to-mine cadence, matches the on-foot drill pace (#685)
        private const float EvaMinePace = 0.28f; // seconds between mining hits while LMB is held (#685)
        private const float EvaReach = 6f;   // how far the suit can build/mine
        private const float SuitRadius = 0.45f; // suit collision radius vs the voxel hull
        private const float FarStructUnload = 95f; // S5: drop a voxel body's mesh beyond this (data kept)
        private const float FarStationUnload = 900f; // #1917: a station hull stays drawn this far from its box — a landmark
        private const float StationReachMargin = 80f; // #1917: flight room kept beyond the farthest station hull
        private const float ShipHullRadius = 2.5f;    // #1917: the flight ship's collision radius against station hulls
        private float _stationReach;                  // #1917: how far out the farthest station hull (plus room) reaches

        /// <summary>The flight clamp: the system's reach, grown to take in every station hull (#1917).</summary>
        private float FlightClamp => Mathf.Max(_bounds, _stationReach);
        private const float FlightShipScale = 0.5f; // voxel ship is shown half-size while piloting (1:1 on EVA)

        // item 20 S3: voxel ore asteroids — static structures at world positions, separate from the own ship.
        private sealed class VoxStruct
        {
            public Dictionary<Vector3i, BlockId> Cells;
            public Vector3 Centre;
            public Vector3 Pos;     // root-local position of the structure
            public GameObject Root; // null until (re)built
            public bool MeshDirty;

            public string Kind;     // "asteroid" | "station" | "wreck"
            public Dictionary<Vector3i, (int Tint, int Glow)> Mods; // authored dye/glow (a station's blue solar cells)
            public Dictionary<Vector3i, int> Shapes;                // authored shapes (slabs, furniture seen through a window)
            public Vector3 Half;    // half the cells' box, centred on Pos

            // #1917: a generated station's hangar mouth — root-local centre + the direction pointing out of it.
            public bool HasDock;
            public Vector3 Dock;
            public Vector3 DockOut;

            // #2607: a player station's doors (drawn as doors, aimed at on a spacewalk) and its owner (the door preview
            // shows only where the suit may build: the owner and their allies).
            public List<SpaceDoors.Door> Doors = new();
            public HashSet<Vector3i> DoorCells = new();
            public string OwnerId = string.Empty;

            // #2676: a derelict station's hull gets the broken-look extras (sparks, tumbling pieces, a flickering beacon).
            public string Id = string.Empty;
            public bool Derelict;

            public bool IsStation => Kind == "station";
        }

        private readonly Dictionary<string, VoxStruct> _structs = new Dictionary<string, VoxStruct>();
        private readonly List<string> _structRemove = new List<string>();

        private Transform _exhaust;
        private ParticleSystem _thruster; // real engine-flame particles (replaces the old Unlit cube exhaust bits)
        private GameObject _hatchDoor; // closed door panel capping the rear hatch opening (slides open on an EVA)
        private Material _hatchMat; // glowing entry-hatch marker on the ship's tail (pulses on an EVA)
        private Material _hullMat;  // the ship's hull material — re-tinted when the player picks a colour (item 32)
        private int _appliedHullRgb = -1; // last hull colour applied (cube material tint or voxel re-mesh), to detect live changes
        private AudioSource _engine;
        private readonly Dictionary<string, GameObject> _entities = new Dictionary<string, GameObject>();
        // Reused by the per-frame SyncEntities pass (same pattern as _remoteSeen/_remoteRemove below).
        private readonly HashSet<string> _entitySeen = new HashSet<string>();
        private readonly List<string> _entityRemove = new List<string>();

        // Other players sharing this space instance, drawn as a ship or a floating EVA suit (R2 visibility). #2709: the suit
        // is the pilot's own suited figure (Body, built on the first spacewalk), and the ship stays where it floats meanwhile.
        private sealed class RemoteAvatar
        {
            public GameObject Root; public GameObject Ship; public GameObject Suit; public Material HullMat; public bool Voxel; public int HullRgb = -1;
            public string Name = string.Empty; public double LastSeen; public ParticleSystem Plume; public bool Parked; public bool PlumesOff;
            public PlayerAvatar Body; public bool Eva; public int SuitSkin = -1, SuitTorso = -1, SuitArms = -1, SuitLegs = -1;
        }
        private Vector3 _launchOffset; // #2440: where this launch rises (beside the ships already out here)
        private readonly Dictionary<string, RemoteAvatar> _remotePlayers = new Dictionary<string, RemoteAvatar>();
        private readonly HashSet<string> _remoteSeen = new HashSet<string>();
        private readonly List<string> _remoteRemove = new List<string>();

        // Buffered snapshot interpolation for everything the server moves in space (#756): SpaceState
        // arrives at ~5 Hz and used to be applied as a raw per-frame snap — the only entity family in the
        // game without smoothing, right next to the buttery locally-simulated own ship. Same interpolator
        // the surface RemotePlayers pass uses. Space instances don't wrap, so it samples in its no-wrap mode — a
        // straight-line lerp. #2707: this used to pass int.MaxValue as "effectively infinite", and WrapX moved every
        // negative X up by that much: ships, drones and drops west of the origin were drawn ~2.1 billion units away.
        private readonly Dictionary<string, RemoteEntityInterpolator> _entityLerp = new Dictionary<string, RemoteEntityInterpolator>();
        private readonly Dictionary<string, RemoteEntityInterpolator> _remoteLerp = new Dictionary<string, RemoteEntityInterpolator>();
        private object _lastEntitySnapshot, _lastPlayersSnapshot;
        private const int SpaceNoWrap = RemoteEntityInterpolator.NoWrap;
        // #2175: the bodies of the flight scene with their live look — the planet map baked off the main thread, the
        // weather-driven cloud shell, the haze rim in the world's own sky colour and the spin to its time of day.
        private sealed class OrbitBody
        {
            public string BodyId = string.Empty;
            public string Key = string.Empty;
            public BlocksBeyondTheStars.Shared.Definitions.PlanetType Planet;
            public OrbitAtmosphere Atmosphere;
            public Transform Sphere;
            public Vector3 Pos;
            public float Diameter;
            public Vector3 SunLocal;
            public Transform Ring;          // the planetary ring disc — held still while the sphere turns (#2177)
            public Quaternion RingBase;
            public Transform Shell;
            public Material ShellMat;
            public Texture2D ShellTex;
            public Color32[] ShellBuffer;
            public float[] Noise;
            public float ShellPhaseDeg;
            public Material HazeMat;
            public Color HazeColor;
            public string HazeState = string.Empty;
            public PlanetMapData Map;
            public WeatherMapPixels Pixels;
            public readonly List<int> StormPixels = new List<int>();
            public string State = "clear";
            public int SeenVersion = -1;
            public int SpunVersion = -1;
            public double NextCompose;
        }

        private readonly List<OrbitBody> _orbitBodies = new List<OrbitBody>();
        private readonly List<Object> _orbitOwned = new List<Object>(); // materials + textures the bodies created (destroyed on Exit)
        private float _nextLightning;
        private float _nextMeteor;

        // #1663: the system's landable asteroid bodies (sphere + its true orbit diameter). Their real size is
        // 11–15 units, which at belt distances (500–1300 units) is an 8–20 px speck nobody can find — so each
        // frame they are held at a minimum on-screen diameter (KeepFarAsteroidsVisible); up close they are
        // exactly their true size again.
        private readonly List<(Transform Body, float Diameter)> _asteroidBodies = new List<(Transform, float)>();
        private const float MinAsteroidScreenPx = 20f;

        private Transform _camPrevParent;
        private Vector3 _camPrevLocalPos;
        private Quaternion _camPrevLocalRot;
        private float _camPrevFar;
        private CameraClearFlags _camPrevClear;
        private Color _camPrevBg;

        // Landing fly-away: the camera is frozen at the pose it had when the descent began, while the ship
        // flies off toward the planet — so it visibly shrinks into the distance instead of sinking in-frame.
        private Vector3 _landCamPos;
        private Quaternion _landCamRot;
        private Vector3 _landShipStart;
        private Vector3 _landTargetPos;

        private bool _active;
        private Phase _phase;
        private int _viewMode; // 0 = third-person, 1 = cockpit
        private float _seq;
        private float _yaw, _pitch;
        private float _shipSpeedMul = 1f; // cruise-speed factor from the active ship design
        private float _shipTurnMul = 1f;  // turn-rate factor from the active ship design

        private bool _confirmLand;        // a landing prompt is up — the pad chooser map (item 38)
        private string _choosePadBody;    // body whose pads the chooser is showing (null = no chooser)
        private float _padRequestAt;      // when the pad list was requested — drives the no-reply timeout (#956)
        private bool _padRequestRetried;  // one silent re-request before giving up
        private string _landDestBody;     // confirmed landing destination of the in-flight descent (survives until
                                          // the animation reads it; "" = home body, null = no landing committed).
                                          // Separate from _choosePadBody, which is cleared the moment a pad is picked
                                          // — reading it in BeginLandingFlyAway() would yield "" → wrong (home) planet.
        private GameObject _landMapGo;    // the on-screen planet map of landing pads (built while choosing)
        private string _landMapBody;      // which body the currently-built land map is for
        private string _boardTargetId;    // station being boarded during the dock-approach animation
        private Vector3 _boardTargetPos, _boardStartPos;
        private bool _boardSent;          // the board intent was sent (now waiting for the server)
        private float _boardWait;         // safety timeout so a rejected board never leaves a black screen

        private bool _eva;                // on an EVA spacewalk: first-person 6-DOF float, ship parked
        private Vector3 _evaPos;          // suit position (space-local), independent of the parked ship
        private float _evaYaw, _evaPitch; // suit free-look
        private bool _evaNearShip;        // within boarding range of the parked ship this frame
        private float _evaShipDist;       // distance from the suit to the ship's hull box (the board reach + "fly to your ship")
        private bool _enteringInterior;   // stepping into the ship interior — tear the flight view down at once
                                          // (no landing descent) when the server closes space

        // #2724: the docking visit — what K, U, E and the prompt line see this frame (ShipVisitRules), and a visit on its way.
        private BlocksBeyondTheStars.Client.Core.ShipVisitView _visit;
        private BlocksBeyondTheStars.Networking.Messages.NetSpacePlayer _dockPilot; // K's target: the nearest other pilot in docking range
        private bool _interactGoesOver;   // E goes over to the docked partner's ship this frame (the prompt and the Actions list ask)
        private bool _interactAsksPartner; // E asks the docked partner at the helm to let us over (the server tells them)
        private float _visitWait = -1f;   // seconds since a visit was sent behind the veil; -1 = none on its way
        private int _visitRefusalsSeen;   // GameBootstrap.VisitRefusals when the visit was sent

        private float _moveSendTimer;     // throttles authoritative position reports
        private float _hitFlash;          // red damage flash on a collision/hit
        private float _shake;             // camera shake on a hit

        // VFX overhaul (#2156/#2157): the shield bubble, one plume per engine nozzle, the engine glow light, the
        // ship's velocity for the space dust, the low-hull smoke, destroyed-entity kinds and the planet-scan sweep.
        private FxShield _shield;
        private readonly List<ParticleSystem> _extraThrusters = new List<ParticleSystem>();
        private readonly List<Vector3> _nozzles = new List<Vector3>(); // ship-local plume positions
        private int _engineLight;
        private Vector3 _lastShipWorld;
        private bool _hasLastShipWorld;
        private float _flightFov;
        private float _smokeTimer;
        private float _trailAcc;
        private float _prevThrottle;
        private Bounds _shipBounds;
        private GameObject _shipBoundsFor;
        private int _seenPlanetScan = -1;
        private MeshRenderer _reentry;
        private readonly Dictionary<string, string> _entityKinds = new Dictionary<string, string>();
        private readonly Dictionary<string, float> _entityHull = new Dictionary<string, float>(); // hull drops → hit flash
        private readonly Dictionary<string, Transform> _bodySpheres = new Dictionary<string, Transform>();
        private float _lastHull = -1f, _lastShield = -1f;
        private bool _combatSubscribed;
        private bool _hyperjumpSubscribed;
        private bool _hyperjumping; // a hyperspace jump is tearing down the view (warp covers it, no landing)
        private bool _transitLaunchDone; // #1614: this flight's transit stage was reported to the server (once per flight)
        private float _transitWait;      // #1614: seconds since that report — the fallback hands the controls back
        private string _sceneInstance; // flight instance the current scene was built for (#1677)
        private bool _shipDestroyed; // the ship blew up in space — tear down at once (explosion stays, no landing descent)

        private readonly HashSet<string> _dropIds = new HashSet<string>(); // tracked ResourceDrop entities
        private readonly List<TractorBeam> _beams = new List<TractorBeam>();
        private float _cargoFlash;        // pulses the cargo readout when salvage is tractored in

        // Ship-systems quick-bar (1–9 select, LMB use): fire the laser, sweep the tractor beam, …
        private float _fireCd;            // shot/use cooldown
        private string _fireTargetId;     // the entity currently in the firing solution (for the reticle)
        private int _selectedSystem;      // index into _systems
        private readonly List<ShipSystem> _systems = new List<ShipSystem>();
        private const string FlightWeapon = "ship_laser_basic";
        private const float WeaponRange = 45f;  // fallback when the module data is unavailable
        private const float FireRate = 0.45f;   // fallback seconds between shots

        // #693: with the AutoAim world rule ON the laser acquires targets in a tight forward cone; OFF
        // requires a genuine boresight line — the nose ray must pass through the target's body.
        private const float SpaceAutoAimCone = 0.866f; // cos ~30° (was effectively ±75°)

        /// <summary>The fitted module's real range (#694 — the client used to hardcode 45, crippling the
        /// laser_cannon_2's range advantage of 70).</summary>
        private float WeaponRangeFor(string weaponKey)
        {
            var stats = Game.Content?.GetShipModule(weaponKey)?.Stats;
            return stats != null && stats.TryGetValue("weapon_range", out var v) ? (float)v : WeaponRange;
        }

        /// <summary>The fitted module's real fire cadence — the server enforces it now (#694), so firing
        /// faster client-side would only get shots swallowed.</summary>
        private float WeaponCooldownFor(string weaponKey)
        {
            var stats = Game.Content?.GetShipModule(weaponKey)?.Stats;
            return stats != null && stats.TryGetValue("weapon_cooldown", out var v) ? (float)v : FireRate;
        }

        private struct ShipSystem { public string Label; public string Kind; public string WeaponKey; }

        // System sun: a bright billboard far off in a fixed direction, tinted by the star's colour, plus a
        // screen-space lens flare that blooms when you look toward it.
        private Transform _sun;
        private Material _sunMat;
        private Color _sunColor = new Color(1f, 0.96f, 0.88f);
        private static readonly Vector3 SunDir = new Vector3(-0.45f, 0.32f, 1f).normalized;
        // The system star's position in the scene (the bodies are laid out relative to the launched-from body,
        // so the star sits at -currentSystemPos·scale). Drives the sun-lit phase on each body + aims the visible
        // sun billboard at the true star. Falls back to the fixed SunDir when there's no star map.
        private Vector3 _starLocal;
        private bool _hasStar;

        private sealed class TractorBeam { public GameObject Go; public float Life; public float Max; }

        /// <summary>Whether the flight controls (cruise, EVA, the V camera toggle) may read input this frame —
        /// the one gate behind all three (<see cref="InputGate.FlightControlAllowed"/>, #1858): no menu-style
        /// dialog, no destruction prompt, no chat box, no focused text field.</summary>
        private bool FlightInputAllowed // a cutscene holds the ship still like a menu (#2461: the ending can be replayed in flight)
            => InputGate.FlightControlAllowed(Game.MenuOpen || GameBootstrap.CutsceneActive, Game.AwaitingRespawnConfirm, Game.ChatTyping, InputMap.TextEntryActive);

        /// <summary>#2724: true while the flight verbs of docking may act — cruising at the helm or floating on a spacewalk,
        /// not launching, landing, boarding, behind the pad chooser or with a visit on its way. <see cref="PlayerInteractions"/>
        /// reads K and U in flight only then, and the Actions list offers them only then.</summary>
        public bool DockVerbsLive => _active && _phase == Phase.Cruise && _ship != null && !_confirmLand && _visitWait < 0f;

        /// <summary>#2724: the docking-visit picture of this frame (docked? the partner flying, or parked within reach?).</summary>
        public BlocksBeyondTheStars.Client.Core.ShipVisitView Visit => _visit;

        /// <summary>#2724: K's target this frame — the nearest other pilot within the docking range (never an NPC trader), or null.</summary>
        public BlocksBeyondTheStars.Networking.Messages.NetSpacePlayer DockPilot => _dockPilot;

        /// <summary>#2724: E goes over to the docked partner's ship this frame — the Actions list offers "go over" then.</summary>
        public bool InteractGoesOver => DockVerbsLive && _interactGoesOver;

        private void Update()
        {
            if (Game == null || Camera == null)
            {
                return;
            }

            // item 20 S2/S3: subscribe to voxel-structure edits/designs/removals as soon as the network exists
            // (kept for the rig's lifetime) — early enough that the first asteroid batch on space-entry isn't missed.
            if (Game.Network != null && !_structSubscribed)
            {
                Game.Network.StructureBlockChangedReceived += OnStructureBlockChanged;
                Game.Network.StructureMiningProgressReceived += OnStructureMiningProgress; // #685
                Game.Network.SpaceShipDesignReceived += OnStructureDesign;
                Game.Network.SpaceEntityDestroyed += OnStructEntityDestroyed;
                Game.Network.SpaceEntityDestroyed += OnEntityDestroyedFx; // #2156: explosions instead of vanishing
                Game.Network.SpaceEntityDestroyed += OnLockedEntityDestroyed; // #2277: a killed lock moves on to the next attacker
                _structSubscribed = true;
            }

            if (Game.InSpace && !_active)
            {
                Enter();
            }

            if (!_active)
            {
                return;
            }

            UpdateOrbitBodies(); // #2175/#2177: weather-driven cloud shells, spin to the time of day, lightning
            UpdatePendingVisit(); // #2724: a refused (or unanswered) visit lifts the veil and hands the controls back

            // #1677: a hyperjump made IN FLIGHT never turns the flag off for a frame we get to see — the server
            // sends SpaceClosed, SpaceState and the new star map in one tick, and the client pump applies all
            // three before Update runs. Watching Game.InSpace alone therefore missed the whole transition: the
            // scene kept the DEPARTURE system's star, planets and landables, and landing on one of those bodies
            // was a second cross-system jump straight back to the old planet. The flight INSTANCE is the honest
            // signal — it is keyed on the body the pilot flies over, so a jump always changes it.
            string instance = Game.Space != null ? Game.Space.InstanceId : null;
            if (!string.IsNullOrEmpty(instance) && !string.IsNullOrEmpty(_sceneInstance)
                && !string.Equals(instance, _sceneInstance, System.StringComparison.Ordinal))
            {
                Exit();  // same teardown a jump-with-a-visible-gap would have run…
                Enter(); // …and the same rebuild, now against the arrived system's map
                return;  // the rest of this frame belongs to the old scene
            }

            // item 20 S1: the voxel ship design arrives just after we enter space (separate message), so rebuild
            // the ship mesh once it (or a switched ship's design) is available.
            if (!ReferenceEquals(_builtDesign, Game.ShipDesign))
            {
                RebuildShipModel();
            }

            // item 20 S3: build/update/remove the voxel asteroid bodies as their designs arrive.
            ReconcileStructs();

            KeepFarAsteroidsVisible(); // #1663: a belt body must never shrink to an unfindable speck

            // Live hull re-tint: the player can change their ship colour from the menu mid-flight (item 32).
            // Cube fallback: re-tint the material; voxel ship: the paint lives in the mesh's tint stream,
            // so re-mesh the (small) ship voxels with the new colour.
            if (Game.HullRgb != _appliedHullRgb)
            {
                if (_hullMat != null)
                {
                    _hullMat.color = ShaderColor.Srgb(Rgb(Game.HullRgb));
                    _appliedHullRgb = Game.HullRgb;
                }
                else if (_shipVox != null)
                {
                    RebuildShipVoxels(); // updates _appliedHullRgb
                }
            }

            // Server says we left. A hyperspace jump (its full-screen warp covers the transition) or a
            // station board tears down immediately — no surface-landing descent. Otherwise fly the
            // landing sequence back down to the body we launched from, then tear down.
            // When the ship was destroyed, hold here (explosion + hidden hull stay on screen) until the
            // player clicks "Weiter" on the destruction prompt — then tear down at once (no recovery descent).
            if (!Game.InSpace && !Game.AwaitingRespawnConfirm)
            {
                if (_hyperjumping || _phase == Phase.Boarding || _enteringInterior || _shipDestroyed)
                {
                    Exit(); // jump / station dock / stepping inside the ship / ship blown up: no landing descent
                    return;
                }

                // #1614: a transit's take-off toward another system ends with this flight closing for the warp — the
                // new system's flight state is on its way (normally in the same network batch). No landing in between.
                bool warpComing = _phase == Phase.Launch && _transitLaunchDone && _transitWait < TransitFallback && TransitWarpsNext();
                if (_phase != Phase.Landing && !warpComing)
                {
                    _phase = Phase.Landing;
                    _seq = 0f;
                    ClientAudio.Instance?.Cue("ship_landing");
                    BeginLandingFlyAway();
                }
            }

            // V: first/third person. Behind the same gate as the cruise/EVA controls (#1858) — this sat above
            // every early-out, so a "v" typed into the chat box or the feedback dialog flipped the camera.
            // #2568: and not while the same key is the held push-to-talk key (a rebind onto it).
            if (FlightInputAllowed && InputMap.Down(InputAction.ToggleThirdPerson) && !VoiceChat.TalkKeyClaims(InputAction.ToggleThirdPerson))
            {
                _viewMode = 1 - _viewMode;
            }

            SyncEntities();
            SyncRemotePlayers();
            DrainWarpFx();
            UpdateBeams(Time.deltaTime);
            if (_phase == Phase.Cruise)
            {
                UpdateHostileFire(Time.deltaTime); // draw the enemy's shots (server damage is an invisible aura)
            }

            // EVA is server-driven now (you step out through the ship's airlock): mirror its InEva state so
            // the suit float begins/ends in sync with the server.
            if (_phase == Phase.Cruise)
            {
                if (Game.InEva && !_eva) { BeginEvaMode(); }
                else if (!Game.InEva && _eva) { _eva = false; }
            }

            // Render the voxel ship compact while flying (so a 1:1 hull doesn't dwarf the system), but at full
            // 1:1 on an EVA — the normal "as on a world" scale for floating up to it + building (bug fix).
            if (_ship != null && _shipCells != null && _shipCells.Count > 0)
            {
                _ship.transform.localScale = Vector3.one * (_eva ? 1f : FlightShipScale);
            }

            // The rear hatch energy field is up while piloting (so the stern isn't an open hole) and drops —
            // hidden — while you're on an EVA so you can float out and board.
            if (_hatchDoor != null)
            {
                _hatchDoor.SetActive(!_eva);
            }

            switch (_phase)
            {
                case Phase.Launch: UpdateSequence(rising: true); break;
                case Phase.Landing: UpdateSequence(rising: false); break;
                case Phase.Boarding: UpdateBoarding(); break;
                case Phase.Transit: UpdateTransitArrival(); break;
                default: if (_eva) { UpdateEva(); } else { UpdateCruise(); } break;
            }

            if (!_eva)
            {
                _evaDoorGhost?.Hide(); // #2119: the door preview belongs to the spacewalk only
            }

            PlaceCamera();
            BillboardSun();

            // Engine loop: swells in cruise, throttle nudges the pitch.
            if (_engine != null)
            {
                _engine.volume = Mathf.MoveTowards(_engine.volume, (_phase == Phase.Cruise && !_eva) ? 0.25f : 0.08f, Time.deltaTime * 0.5f);
                _engine.pitch = 1f + 0.2f * Mathf.Clamp01(Mathf.Abs(CaptureForward()));
            }

            // Engine flame: a real ParticleSystem thruster (BuildThruster) whose emission + jet speed track the
            // throttle. The small Unlit nozzle core (_exhaust) just glows at the vent; the old stretching flame +
            // cube exhaust bits are retired in favour of the particle jet.
            float throttle = _phase == Phase.Cruise ? Mathf.Clamp01(CaptureForward()) : 0f;
            if (_exhaust != null)
            {
                _exhaust.localScale = new Vector3(0.5f, 0.5f, 0.55f + throttle * 0.5f); // steady nozzle glow core
            }

            if (_thruster != null)
            {
                bool firing = _phase == Phase.Cruise && !_eva;
                var em = _thruster.emission;
                em.rateOverTime = firing ? Mathf.Lerp(20f, 130f, throttle) : 0f;
                var main = _thruster.main;
                main.startSpeed = 5f + throttle * 9f;
                foreach (var extra in _extraThrusters)
                {
                    if (extra != null)
                    {
                        var xem = extra.emission;
                        xem.rateOverTime = em.rateOverTime;
                        var xmain = extra.main;
                        xmain.startSpeed = main.startSpeed;
                    }
                }
            }

            UpdateFlightFx(throttle);
        }

        private string Loc(string key, string fallback) => Game.Localizer != null ? Game.Localizer.Get(key) : fallback;

        private void Enter()
        {
            _active = true;
            // Taking the helm again from inside the ship (or any "already airborne" entry) drops straight into
            // free flight — no take-off sequence, since you never landed.
            _phase = Game.SpaceSkipLaunch ? Phase.Cruise : Phase.Launch;
            if (Game.SpaceSkipLaunch && Game.SpaceAutomaticTransit)
            {
                _phase = Phase.Transit; // #1614: a transit's warp arrival — no controls until it has landed
            }

            _seq = 0f;
            _yaw = 0f;
            _pitch = 0f;
            _confirmLand = false;
            HideLandMap();
            _boardSent = false;
            _hyperjumping = false;
            _transitLaunchDone = false;
            _transitWait = 0f;
            _shipDestroyed = false;
            _eva = false;
            _enteringInterior = false;
            ResetVisit(); // #2724
            _landTargetId = null;
            _landTargetName = null;
            Game.SpaceViewActive = true;

            ResolveShipFlight();
            BuildScene();
            _sceneInstance = Game.Space != null ? Game.Space.InstanceId : null; // what this scene depicts (#1677)

            // #1614: a transit's landing descent heads for its destination, not for whatever lies ahead of the nose. The
            // body this flight is anchored on is the scene's home body, which the landables list as "".
            string transitTo = Game.SpaceAutomaticTransit ? Game.SpaceTransitDestination : string.Empty;
            _landDestBody = string.IsNullOrEmpty(transitTo) ? null
                : transitTo == Game.StarMap?.ActiveLocationId ? string.Empty
                : transitTo;

            _flightFov = Camera.fieldOfView;
            _hasLastShipWorld = false;
            _seenPlanetScan = -1;
            FxSpaceDust.Show(Camera.transform); // #2157: the speed cue

            // #2118: a flight that resumes (the helm again from inside, out through the airlock, undocked back to an
            // EVA) starts where the ship was left floating and pointing — not at the launch point, whose first move
            // report used to overwrite the spot the server remembered.
            if (Game.SpaceSkipLaunch && Game.SpaceHasResume && _ship != null)
            {
                _ship.transform.localPosition = Game.SpaceResumePos;
                _yaw = Game.SpaceResumeYaw;
                _ship.transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            }

            // #2440: a launch into an orbit that already holds other ships starts a few units to the side of them — the
            // server hands the offset on the resume fields — so two friends lifting off together no longer sit inside each
            // other at the launch point. The take-off sequence rises along that offset column.
            _launchOffset = !Game.SpaceSkipLaunch && Game.SpaceHasResume
                ? new Vector3(Game.SpaceResumePos.x, 0f, Game.SpaceResumePos.z)
                : Vector3.zero;

            // React to server-reported hull/shield damage (collisions, enemy fire) with a flash + shake,
            // and to ship destruction with an explosion burst at the hull.
            if (Game.Network != null && !_combatSubscribed)
            {
                Game.Network.ShipCombatStatusChanged += OnShipCombat;
                Game.Network.SpaceClosed += OnSpaceClosedFx;
                Game.Network.ActionFxReceived += OnRemoteShipFx; // #2158: other pilots' shots
                _combatSubscribed = true;
            }

            // A hyperspace jump while flying tears the view down without a surface-landing descent (its
            // warp overlay covers the transition); the subscription is kept for the rig's lifetime.
            if (!_hyperjumpSubscribed)
            {
                Game.HyperjumpStarted += OnHyperjump;
                Game.WormholeArrived += OnWormholeArrived; // #2242: through a rift — same teardown, the rift transit instead of the warp
                _hyperjumpSubscribed = true;
            }

            _lastHull = Game.ShipCombat?.Hull ?? -1f;
            _lastShield = Game.ShipCombat?.Shield ?? -1f;

            // Launch roar + a looping engine bed for the flight. No roar when we skip the take-off (helm).
            if (!Game.SpaceSkipLaunch)
            {
                ClientAudio.Instance?.Cue("ship_launch");
            }
            var engClip = Resources.Load<AudioClip>("audio/engine_idle") ?? ProceduralAudio.Generate("engine_idle");
            if (engClip != null)
            {
                _engine = gameObject.AddComponent<AudioSource>();
                _engine.clip = engClip;
                _engine.loop = true;
                _engine.spatialBlend = 0f;
                _engine.volume = 0f;
                _engine.Play();
            }

            _camPrevParent = Camera.transform.parent;
            _camPrevLocalPos = Camera.transform.localPosition;
            _camPrevLocalRot = Camera.transform.localRotation;
            _camPrevFar = Camera.farClipPlane;
            _camPrevClear = Camera.clearFlags;
            _camPrevBg = Camera.backgroundColor;

            Camera.transform.SetParent(_root.transform, false);
            Camera.farClipPlane = 3000f;
            Camera.clearFlags = CameraClearFlags.SolidColor;     // black space, not the blue sky
            Camera.backgroundColor = new Color(0.02f, 0.03f, 0.06f);
        }

        private void Exit()
        {
            // The pad chooser can still be up when space closes under us (ship destroyed while choosing,
            // #413 M15): its map lives on the persistent _ui canvas and would survive Exit as a dead
            // overlay with live click-to-land raycast targets. Tear it down with the view.
            CancelLandChooser();
            _landDestBody = null; // descent finished — don't let a stale target steer a later recovery landing
            StopScanner();          // #2237: no lock brackets or charging fan outlive the flight view
            ResetTargetLock();      // #2277: landing, docking, the interior, a jump, a new instance — the lock goes
            _nearWormholeId = null; // #2242
            _anomalyHint.Reset();   // #2588: the next flight's first look at an anomaly is a fresh approach
            FxSpaceDust.Hide();
            Sky.SpaceSunDir = Vector3.zero;
            UrpScenePost.Instance?.SetMotion(0f);
            if (_flightFov > 1f)
            {
                Camera.fieldOfView = _flightFov;
            }

            if (_engineLight != 0)
            {
                FxLights.Remove(_engineLight);
                _engineLight = 0;
            }

            _shield = null;
            _reentry = null;
            _extraThrusters.Clear();
            _nozzles.Clear();
            _bodySpheres.Clear();
            _orbitBodies.Clear();
            foreach (var owned in _orbitOwned)
            {
                if (owned != null)
                {
                    Destroy(owned);
                }
            }

            _orbitOwned.Clear();
            _entityKinds.Clear();
            _entityHull.Clear();
            Camera.transform.SetParent(_camPrevParent, false);
            Camera.transform.localPosition = _camPrevLocalPos;
            Camera.transform.localRotation = _camPrevLocalRot;
            Camera.farClipPlane = _camPrevFar;
            Camera.clearFlags = _camPrevClear;
            Camera.backgroundColor = _camPrevBg;

            if (_root != null)
            {
                // Deactivate before destroying: Unity frees it at the END of the frame, and a same-frame
                // rebuild (#1677) would otherwise draw the old system's planets over the new one for a frame.
                _root.SetActive(false);
                Destroy(_root);
                _root = null;
            }

            if (_engine != null)
            {
                Destroy(_engine);
                _engine = null;
            }

            if (Game.Network != null && _combatSubscribed)
            {
                Game.Network.ShipCombatStatusChanged -= OnShipCombat;
                Game.Network.SpaceClosed -= OnSpaceClosedFx;
                Game.Network.ActionFxReceived -= OnRemoteShipFx;
                _combatSubscribed = false;
            }

            _entities.Clear();
            _dropIds.Clear();
            _beams.Clear(); // their GameObjects were children of _root (already destroyed)
            _asteroidMat = null; // material lived under the destroyed view; rebuild next launch
            if (_derelictDebrisMat != null)
            {
                Destroy(_derelictDebrisMat); // #2676: the tumbling pieces' material (its pieces went with _root)
                _derelictDebrisMat = null;
            }
            ClearStructs();      // item 20 S3: asteroid bodies lived under _root (destroyed) — drop the refs
            _shipCells = null;   // ship voxel grid was under the destroyed _ship
            _shipVox = null;
            _aimHighlight = null; // marker lived under _root (destroyed)
            _aimHighlightMat = null; // its material dies with it (#685)
            _evaDoorGhost?.Destroy(); // #2119: the door preview is a scene object of its own
            _evaDoorGhost = null;
            _doorPanelMat = _doorRimMat = _doorFieldMat = null; // #2607: rebuilt with the next view, like the asteroid material
            _shipDoors.Clear();
            _shipDoorCells.Clear();
            _ship = null;
            _exhaust = null;
            _thruster = null; // particle thruster lived under the destroyed _ship
            _hatchDoor = null; // lived under the destroyed _ship
            _sun = null; // sun billboard lived under _root (destroyed); flare sprites persist on _ui
            _sunMat = null;
            _hatchMat = null; // hatch marker material lived under the destroyed ship
            _hullMat = null;
            _appliedHullRgb = -1;
            _active = false;
            _eva = false;
            _enteringInterior = false;
            ResetVisit(); // #2724
            _shipDestroyed = false;
            _remotePlayers.Clear(); // their GameObjects are children of _root, destroyed with the scene
            _remoteLerp.Clear();
            _entityLerp.Clear();
            _lastEntitySnapshot = null;
            _lastPlayersSnapshot = null;
            _shake = 0f;
            _hitFlash = 0f;
            _cargoFlash = 0f;
            _sceneInstance = null;
            Game.SpaceViewActive = false;
        }

        private void OnHyperjump() => _hyperjumping = true;

        /// <summary>Reads the active ship's design (data/ships.json) so heavier ships fly slower and
        /// turn more sluggishly than light scouts (presentation only; combat stays server-side).</summary>
        private void ResolveShipFlight()
        {
            _shipSpeedMul = 1f;
            _shipTurnMul = 1f;

            string type = null;
            var owned = Game?.OwnedShips;
            if (owned != null)
            {
                foreach (var s in owned)
                {
                    if (s.Active)
                    {
                        // A self-built ship has no data/ships.json entry — its geometry-derived stats ride
                        // the fleet message instead (#949).
                        if (s.FlightSpeed > 0f || s.Handling > 0f)
                        {
                            _shipSpeedMul = s.FlightSpeed > 0f ? s.FlightSpeed : 1f;
                            _shipTurnMul = s.Handling > 0f ? s.Handling : 1f;
                            return;
                        }

                        type = s.Type;
                        break;
                    }
                }
            }

            var def = type != null ? Game?.Content?.GetShip(type) : null;
            if (def != null)
            {
                _shipSpeedMul = def.FlightSpeed > 0f ? def.FlightSpeed : 1f;
                _shipTurnMul = def.Handling > 0f ? def.Handling : 1f;
            }
        }

        private string _nearStationId;
        private string _nearStationName;
        private const float BoardRange = 66f; // just inside the server's 70-unit board range
        private const float StationKeepOut = 6f; // ship-collision shell around a station hub (far inside BoardRange)

        // System-scale flight: the other bodies of the current system, placed at their (scaled) system
        // coordinates so you can fly between them and land on any one. The body nearest within
        // LandApproachRange is the land target; if none, landing returns you to the current body.
        private readonly List<(string Id, string Name, Vector3 Pos, float Radius)> _landables = new();

        /// <summary>The system's landable bodies (scene-local positions + names) — the radar reads these to
        /// draw a bearing marker toward each planet/moon you can fly to.</summary>
        public IReadOnlyList<(string Id, string Name, Vector3 Pos, float Radius)> Landables => _landables;

        /// <summary>Scene-local star position (meaningful when <see cref="HasStar"/>) — the system
        /// chart (#597) marks the star with it.</summary>
        public Vector3 StarPosition => _starLocal;
        public bool HasStar => _hasStar;

        /// <summary>The flight-volume clamp radius; the system chart keeps free waypoints inside it.</summary>
        public float FlightBounds => FlightClamp;

        /// <summary>Waypoint id for the launch body (#597) — its <see cref="Landables"/> entry carries an
        /// empty id (it doubles as "no travel target"), so the chart pins it under this sentinel instead.</summary>
        public const string HomeWaypointId = "~home";

        // Every body (landable + decorative) as a keep-out sphere: the ship slides along it instead of
        // flying into the planet, so there is no "fell into the planet / auto-landed" — you stop at the
        // approach distance and press E to land. (Pos, keep-out radius.)
        private readonly List<(Vector3 Pos, float Radius)> _keepOut = new();
        private string _landTargetId;   // body the ship is close enough to land on (null = the current world)
        private string _landTargetName;
        private float _landTargetSq;    // squared distance to the land target (for station-vs-body priority)
        private float _nearStationSq;   // squared distance to the near station
        private float _bounds = Bounds; // flight clamp, enlarged to span the resident system
        private const float SystemViewScale = SystemBodyLayout.FlightViewScale; // system units → flight-view units
                                                     // (kept compact so neighbouring planets are a short cruise
                                                     // apart, not minutes; shared — the server parks belt rock
                                                     // clusters at the same transform, #683)
        private const float KeepOutMargin = 10f;     // how far outside a body's surface the ship is held
        private const float LandBand = 40f;          // land prompt shows within (body radius + margin + band)

        /// <summary>Where the pilot IS in the flight scene: the suit on an EVA, else the ship (the camera before the
        /// scene is built). The radar measures bearings and heights from here (#1880) — not from the chase camera
        /// 13 units behind and 4.5 above the hull.</summary>
        public Vector3 PilotPosition
            => _eva ? _evaPos
                : _ship != null ? _ship.transform.localPosition
                : Camera != null ? Camera.transform.localPosition
                : Vector3.zero;

        private void OnDestroy()
        {
            if (_active)
            {
                Exit();
            }

            if (_hyperjumpSubscribed && Game != null)
            {
                Game.HyperjumpStarted -= OnHyperjump;
                _hyperjumpSubscribed = false;
            }

            if (_ui != null)
            {
                Destroy(_ui.gameObject);
            }

            if (_fx != null)
            {
                Destroy(_fx.gameObject);
            }
        }
    }
}
