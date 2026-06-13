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
    public sealed class SpaceView : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private static readonly Vector3 SceneOrigin = new Vector3(0f, 6000f, 0f);
        private const float SeqDuration = 1.6f;
        private const float BoardDuration = 1.2f; // dock-approach animation before boarding a station
        private const float MoveSpeed = 14f;
        private const float LookSpeed = 2.2f;
        private const float Bounds = 130f;
        private const float EvaSpeed = 6.5f;       // suit thrust speed on a spacewalk (slower than the ship)
        private const float EvaBoardRange = 11f;   // how close the suit must get to the hull to board the ship
        private const float ShipKeepOut = 3.5f;    // suit can't fly into its own ship — bounce off the hull shell

        private enum Phase { Launch, Cruise, Landing, Boarding }

        private GameObject _root;
        private GameObject _ship;
        private BlocksBeyondTheStars.Networking.Messages.SpaceShipDesign _builtDesign; // the design the current _ship mesh was built from (item 20 S1)

        // item 20 S2: the player's own ship voxel grid, kept client-side for EVA collision + build/mine aim.
        private Dictionary<Vector3i, BlockId> _shipCells; // null when the cube fallback ship is shown
        private Vector3 _shipCentre;        // design → ship-local offset used when meshing
        private string _shipStructureId;    // structure id these cells belong to (matches server edits)
        private Transform _shipVox;         // container holding the voxel chunk meshes (child of _ship)
        private bool _structSubscribed;     // subscribed to structure block-change events
        private Vector3Int _evaAimHit, _evaAimPlace; // aimed solid cell + the empty cell before it (design coords)
        private bool _evaHasAim;
        private string _evaAimStructId;      // id of the structure currently aimed at (ship or asteroid)
        private GameObject _aimHighlight;    // marker on the aimed cell
        private const float EvaReach = 6f;   // how far the suit can build/mine
        private const float SuitRadius = 0.45f; // suit collision radius vs the voxel hull
        private const float FarStructUnload = 95f; // S5: drop a voxel body's mesh beyond this (data kept)
        private const float FlightShipScale = 0.5f; // voxel ship is shown half-size while piloting (1:1 on EVA)

        // item 20 S3: voxel ore asteroids — static structures at world positions, separate from the own ship.
        private sealed class VoxStruct
        {
            public Dictionary<Vector3i, BlockId> Cells;
            public Vector3 Centre;
            public Vector3 Pos;     // root-local position of the structure
            public GameObject Root; // null until (re)built
            public bool MeshDirty;
        }

        private readonly Dictionary<string, VoxStruct> _structs = new Dictionary<string, VoxStruct>();
        private readonly List<string> _structRemove = new List<string>();

        private Transform _exhaust;
        private Material _hatchMat; // glowing entry-hatch marker on the ship's tail (pulses on an EVA)
        private Material _hullMat;  // the ship's hull material — re-tinted when the player picks a colour (item 32)
        private int _appliedHullRgb = -1; // last hull colour applied (cube material tint or voxel re-mesh), to detect live changes
        private AudioSource _engine;
        private readonly Dictionary<string, GameObject> _entities = new Dictionary<string, GameObject>();

        // Other players sharing this space instance, drawn as a ship or a floating EVA suit (R2 visibility).
        private sealed class RemoteAvatar { public GameObject Root; public GameObject Ship; public GameObject Suit; public Material HullMat; public bool Voxel; public int HullRgb = -1; }
        private readonly Dictionary<string, RemoteAvatar> _remotePlayers = new Dictionary<string, RemoteAvatar>();
        private readonly HashSet<string> _remoteSeen = new HashSet<string>();
        private readonly List<string> _remoteRemove = new List<string>();
        private readonly List<Transform> _cloudShells = new List<Transform>();

        private Transform _camPrevParent;
        private Vector3 _camPrevLocalPos;
        private Quaternion _camPrevLocalRot;
        private float _camPrevFar;
        private CameraClearFlags _camPrevClear;
        private Color _camPrevBg;

        private bool _active;
        private Phase _phase;
        private int _viewMode; // 0 = third-person, 1 = cockpit
        private float _seq;
        private float _yaw, _pitch;
        private float _shipSpeedMul = 1f; // cruise-speed factor from the active ship design
        private float _shipTurnMul = 1f;  // turn-rate factor from the active ship design

        private bool _confirmLand;        // a landing prompt is up — the pad chooser map (item 38)
        private string _choosePadBody;    // body whose pads the chooser is showing (null = no chooser)
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
        private float _evaShipSq;         // squared distance from the suit to the ship (for ship-vs-station priority)
        private bool _enteringInterior;   // stepping into the ship interior — tear the flight view down at once
                                          // (no landing descent) when the server closes space

        private float _moveSendTimer;     // throttles authoritative position reports
        private float _hitFlash;          // red damage flash on a collision/hit
        private float _shake;             // camera shake on a hit
        private float _lastHull = -1f, _lastShield = -1f;
        private bool _combatSubscribed;
        private bool _hyperjumpSubscribed;
        private bool _hyperjumping; // a hyperspace jump is tearing down the view (warp covers it, no landing)

        private readonly HashSet<string> _dropIds = new HashSet<string>(); // tracked ResourceDrop entities
        private readonly List<TractorBeam> _beams = new List<TractorBeam>();
        private float _cargoFlash;        // pulses the cargo readout when salvage is tractored in

        // Ship-systems quick-bar (1–9 select, LMB use): fire the laser, sweep the tractor beam, …
        private float _fireCd;            // shot/use cooldown
        private string _fireTargetId;     // the entity currently in the firing solution (for the reticle)
        private int _selectedSystem;      // index into _systems
        private readonly List<ShipSystem> _systems = new List<ShipSystem>();
        private Text _systemsBar;
        private Image _systemIcon;        // content-styled icon of the selected ship system
        private const string FlightWeapon = "ship_laser_basic";
        private const float WeaponRange = 45f;  // matches ship_laser_basic weapon_range
        private const float FireRate = 0.45f;   // seconds between shots

        private struct ShipSystem { public string Label; public string Kind; public string WeaponKey; }

        // System sun: a bright billboard far off in a fixed direction, tinted by the star's colour, plus a
        // screen-space lens flare that blooms when you look toward it.
        private Transform _sun;
        private Material _sunMat;
        private Color _sunColor = new Color(1f, 0.96f, 0.88f);
        private static readonly Vector3 SunDir = new Vector3(-0.45f, 0.32f, 1f).normalized;
        private readonly List<Image> _flare = new List<Image>();
        private Sprite _glowSprite;
        private static readonly float[] FlareT = { 0f, 0.35f, 0.62f, 1.0f, 1.32f };       // 0 = at sun … 1 = screen centre
        private static readonly float[] FlareSize = { 150f, 42f, 66f, 30f, 52f };
        private static readonly float[] FlareAlpha = { 0.30f, 0.10f, 0.08f, 0.10f, 0.06f };

        private sealed class TractorBeam { public GameObject Go; public float Life; public float Max; }

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
                Game.Network.SpaceShipDesignReceived += OnStructureDesign;
                Game.Network.SpaceEntityDestroyed += OnStructEntityDestroyed;
                _structSubscribed = true;
            }

            // Drift the cloud cover slowly so planets look alive from space.
            for (int i = 0; i < _cloudShells.Count; i++)
            {
                if (_cloudShells[i] != null)
                {
                    _cloudShells[i].Rotate(0f, Time.deltaTime * (2.5f + i * 1.5f), 0f, Space.Self);
                }
            }

            if (Game.InSpace && !_active)
            {
                Enter();
            }

            if (!_active)
            {
                return;
            }

            // item 20 S1: the voxel ship design arrives just after we enter space (separate message), so rebuild
            // the ship mesh once it (or a switched ship's design) is available.
            if (!ReferenceEquals(_builtDesign, Game.ShipDesign))
            {
                RebuildShipModel();
            }

            // item 20 S3: build/update/remove the voxel asteroid bodies as their designs arrive.
            ReconcileStructs();

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
            if (!Game.InSpace)
            {
                if (_hyperjumping || _phase == Phase.Boarding || _enteringInterior)
                {
                    Exit(); // jump / station dock / stepping inside the ship: no landing descent
                    return;
                }

                if (_phase != Phase.Landing)
                {
                    _phase = Phase.Landing;
                    _seq = 0f;
                    ClientAudio.Instance?.Cue("ship_landing");
                }
            }

            if (Input.GetKeyDown(KeyCode.V))
            {
                _viewMode = 1 - _viewMode;
            }

            SyncEntities();
            SyncRemotePlayers();
            UpdateBeams(Time.deltaTime);

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

            switch (_phase)
            {
                case Phase.Launch: UpdateSequence(rising: true); break;
                case Phase.Landing: UpdateSequence(rising: false); break;
                case Phase.Boarding: UpdateBoarding(); break;
                default: if (_eva) { UpdateEva(); } else { UpdateCruise(); } break;
            }

            PlaceCamera();
            BillboardSun();

            // Engine loop: swells in cruise, throttle nudges the pitch.
            if (_engine != null)
            {
                _engine.volume = Mathf.MoveTowards(_engine.volume, (_phase == Phase.Cruise && !_eva) ? 0.25f : 0.08f, Time.deltaTime * 0.5f);
                _engine.pitch = 1f + 0.2f * Mathf.Clamp01(Mathf.Abs(Input.GetAxis("Vertical")));
            }

            // Thruster exhaust stretches with throttle + a gentle flame shimmer. The flicker is kept small
            // and low-frequency so the flame breathes rather than buzzing — a fast/large pulse here read as
            // the whole ship "wobbling". The front face stays anchored at the engine; only the tail grows.
            if (_exhaust != null)
            {
                float throttle = _phase == Phase.Cruise ? Mathf.Clamp01(Input.GetAxis("Vertical")) : 0.15f;
                float len = 0.6f + throttle * 2.6f + Mathf.Sin(Time.time * 8f) * 0.04f;
                _exhaust.localScale = new Vector3(0.6f, 0.6f, len);
                _exhaust.localPosition = new Vector3(0f, 0f, -2.0f - len * 0.5f);

                // Exhaust particle stream: small fading bits shoot backwards, rate scales with throttle.
                if (_phase == Phase.Cruise && throttle > 0.1f)
                {
                    _exhaustSpawnTimer -= Time.deltaTime;
                    if (_exhaustSpawnTimer <= 0f)
                    {
                        _exhaustSpawnTimer = Mathf.Lerp(0.12f, 0.035f, throttle);
                        SpawnExhaustBit(throttle);
                    }
                }
            }
        }

        private float _exhaustSpawnTimer;
        private Material _exhaustBitMat;
        private Material _hitSparkMat;

        /// <summary>An orange spark burst at the ship when a hit lands (paired with the screen flash).</summary>
        private void SpawnHitSparks(float mag)
        {
            if (Camera == null)
            {
                return;
            }

            _hitSparkMat ??= Unlit(new Color(1f, 0.62f, 0.18f));
            Vector3 at = _ship != null ? _ship.transform.position : Camera.transform.position + Camera.transform.forward * 6f;
            int count = 4 + Mathf.RoundToInt(mag * 6f);
            for (int i = 0; i < count; i++)
            {
                var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                StripCollider(p);
                p.transform.position = at + Random.insideUnitSphere * 1.2f;
                p.transform.localScale = Vector3.one * 0.14f;
                p.GetComponent<Renderer>().sharedMaterial = _hitSparkMat;
                p.AddComponent<ExhaustBit>().Vel = Random.onUnitSphere * (6f + mag * 6f);
            }
        }

        /// <summary>One exhaust bit: spawned at the flame tip, flying backwards with jitter, fading fast.</summary>
        private void SpawnExhaustBit(float throttle)
        {
            if (_exhaust == null)
            {
                return;
            }

            _exhaustBitMat ??= Unlit(new Color(0.65f, 0.88f, 1f));
            var back = _exhaust.parent != null ? -_exhaust.parent.forward : -_exhaust.forward;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            StripCollider(go);
            go.transform.position = _exhaust.position + back * (_exhaust.localScale.z * 0.5f);
            go.transform.localScale = Vector3.one * 0.18f;
            go.GetComponent<Renderer>().sharedMaterial = _exhaustBitMat;
            var bit = go.AddComponent<ExhaustBit>();
            bit.Vel = back * (14f + throttle * 10f) + Random.insideUnitSphere * 1.6f;
        }

        /// <summary>A short-lived exhaust cube: flies backwards, shrinks, self-destroys.</summary>
        private sealed class ExhaustBit : MonoBehaviour
        {
            public Vector3 Vel;

            private const float Life = 0.35f;
            private float _t;

            private void Update()
            {
                _t += Time.deltaTime;
                transform.position += Vel * Time.deltaTime;
                transform.localScale = Vector3.one * 0.18f * Mathf.Max(0f, 1f - _t / Life);
                if (_t >= Life)
                {
                    Destroy(gameObject);
                }
            }
        }

        private void UpdateSequence(bool rising)
        {
            _seq += Time.deltaTime;
            float t = Mathf.Clamp01(_seq / SeqDuration);
            float ease = rising ? 1f - (1f - t) * (1f - t) : t * t;
            float y = rising ? Mathf.Lerp(-40f, 0f, ease) : Mathf.Lerp(0f, -40f, ease);
            if (_ship != null)
            {
                _ship.transform.localPosition = new Vector3(0f, y, 0f);
                _ship.transform.localRotation = Quaternion.identity;
            }

            if (_seq >= SeqDuration)
            {
                if (rising)
                {
                    _phase = Phase.Cruise;
                }
                else
                {
                    Exit();
                }
            }
        }

        // VEGA autopilot (Mk2+ AI core): hands-off cruise toward the nearest station / landable body.
        private bool _autopilot;

        private void SetAutopilot(bool on)
        {
            if (_autopilot == on)
            {
                return;
            }

            _autopilot = on;
            Game.ShowMessage(Loc(on ? "ui.vega.autopilot.on" : "ui.vega.autopilot.off",
                on ? "Autopilot engaged" : "Autopilot disengaged"));
        }

        /// <summary>The autopilot's destination: the nearest space station, else the nearest landable body.
        /// Returns the point to fly at plus the squared arrival distance (just outside dock/land range).</summary>
        private bool TryAutopilotTarget(out Vector3 target, out float arriveSq)
        {
            Vector3 pos = _ship != null ? _ship.transform.localPosition : Vector3.zero;
            target = Vector3.zero;
            arriveSq = 0f;
            float bestSq = float.MaxValue;

            var space = Game.Space;
            if (space != null)
            {
                foreach (var e in space.Entities)
                {
                    if (e.Kind != "SpaceStation")
                    {
                        continue;
                    }

                    var sp = new Vector3(e.X, e.Y, e.Z);
                    float sq = (sp - pos).sqrMagnitude;
                    if (sq < bestSq)
                    {
                        bestSq = sq;
                        target = sp;
                        arriveSq = BoardRange * BoardRange * 0.64f; // well inside dock range before handing over
                    }
                }
            }

            if (bestSq == float.MaxValue)
            {
                foreach (var body in _landables)
                {
                    float sq = (body.Pos - pos).sqrMagnitude;
                    if (sq < bestSq)
                    {
                        bestSq = sq;
                        target = body.Pos;
                        float approach = body.Radius + KeepOutMargin + LandBand * 0.5f;
                        arriveSq = approach * approach;
                    }
                }
            }

            return bestSq != float.MaxValue;
        }

        private void UpdateCruise()
        {
            if (_ship == null)
            {
                return;
            }

            // Hold position while a menu is open (e.g. the Tab star map, used to hyperspace-jump to another
            // system) so flight input doesn't fight the UI.
            if (Game.MenuOpen)
            {
                return;
            }

            // Landing opens a pad chooser (item 38): pick a free landing pad with a number key, Esc cancels.
            // No accidental drop to the surface — you choose where you touch down.
            if (_confirmLand)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    CancelLandChooser();
                    return;
                }

                var pads = Game.LandingPadsBody == _choosePadBody ? Game.LandingPads : null;
                if (pads != null)
                {
                    // Show the planet map with the landing pads on it; the player clicks a free pad to touch down
                    // there. Number keys 1–9 mirror clicking, for keyboard players.
                    ShowLandMap(pads);
                    for (int i = 0; i < pads.Length && i < 9; i++)
                    {
                        if ((Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) && !pads[i].Occupied)
                        {
                            LandOnPad(pads[i].Index);
                            break;
                        }
                    }
                }

                return; // hold position while choosing
            }

            // L opens the pad chooser for the body you launched from (landing on a nearby body is E).
            if (Input.GetKeyDown(KeyCode.L))
            {
                OpenPadChooser(Game.StarMap != null ? Game.StarMap.ActiveLocationId : string.Empty);
                return;
            }

            // F: leave the helm and step inside the ship — walk around its interior while it floats here.
            // (From inside, the airlock starts an EVA — there is no direct cockpit→EVA any more.)
            if (Input.GetKeyDown(KeyCode.F))
            {
                _enteringInterior = true;
                Game.BeginWorldTransition(); // veil immediately — stepping inside has no descent to mask (B34)
                Game.Network?.SendEnterShip();
                return;
            }

            // P: VEGA autopilot (needs an AI Core Mk2 aboard) — flies toward the nearest station, else the
            // nearest landable body, and hands back control on arrival or any manual input.
            if (Input.GetKeyDown(KeyCode.P))
            {
                if (_autopilot)
                {
                    SetAutopilot(false);
                }
                else if (Game.AiCoreTier >= 2)
                {
                    SetAutopilot(true);
                }
                else
                {
                    Game.ShowMessage(Loc("ui.vega.autopilot.none", "Autopilot requires an AI Core Mk2"));
                }
            }

            float fwd, strafe;
            if (_autopilot && TryAutopilotTarget(out var apTarget, out float apArriveSq))
            {
                // Manual input takes the helm back instantly.
                if (Mathf.Abs(Input.GetAxis("Vertical")) > 0.25f || Mathf.Abs(Input.GetAxis("Horizontal")) > 0.25f)
                {
                    SetAutopilot(false);
                }
                else if ((apTarget - _ship.transform.localPosition).sqrMagnitude <= apArriveSq)
                {
                    SetAutopilot(false); // arrived — the E dock/land prompt takes over
                }
                else
                {
                    // Steer the nose onto the target and cruise at full throttle.
                    Vector3 to = (apTarget - _ship.transform.localPosition).normalized;
                    float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                    float wantPitch = -Mathf.Asin(Mathf.Clamp(to.y, -1f, 1f)) * Mathf.Rad2Deg;
                    _yaw = Mathf.MoveTowardsAngle(_yaw, wantYaw, 60f * Time.deltaTime);
                    _pitch = Mathf.MoveTowards(_pitch, Mathf.Clamp(wantPitch, -80f, 80f), 45f * Time.deltaTime);
                }
            }

            if (_autopilot)
            {
                fwd = 1f;
                strafe = 0f;
            }
            else
            {
                _yaw += Input.GetAxis("Mouse X") * LookSpeed * _shipTurnMul;
                _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * LookSpeed * _shipTurnMul, -80f, 80f);
                fwd = Input.GetAxis("Vertical");
                strafe = Input.GetAxis("Horizontal");
            }

            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            _ship.transform.localRotation = rot;
            var move = rot * (Vector3.forward * fwd + Vector3.right * strafe) * (MoveSpeed * _shipSpeedMul * Time.deltaTime);
            var pos = _ship.transform.localPosition + move;
            if (pos.magnitude > _bounds)
            {
                pos = pos.normalized * _bounds;
            }

            // Keep-out: never let the ship penetrate a body — push it back out to the sphere surface, so it
            // slides around the planet instead of flying in. This is the "auto-adjust the flight direction"
            // that stops you from getting too close / accidentally dropping onto the planet.
            foreach (var ob in _keepOut)
            {
                Vector3 d = pos - ob.Pos;
                float dist = d.magnitude;
                if (dist < ob.Radius && dist > 0.0001f)
                {
                    pos = ob.Pos + d / dist * ob.Radius;
                }
            }

            // Stations are solid too — slide around them instead of flying through (the board range is far
            // larger than this shell, so you can still fly up and dock with E).
            var keepSpace = Game.Space;
            if (keepSpace != null)
            {
                foreach (var e in keepSpace.Entities)
                {
                    if (e.Kind != "SpaceStation")
                    {
                        continue;
                    }

                    Vector3 sp = new Vector3(e.X, e.Y, e.Z);
                    Vector3 d = pos - sp;
                    float dist = d.magnitude;
                    float keep = StationKeepOut * Mathf.Max(1f, e.Scale); // bigger tiers have a bigger hull shell
                    if (dist < keep && dist > 0.0001f)
                    {
                        pos = sp + d / dist * keep;
                    }
                }
            }

            _ship.transform.localPosition = pos;

            // System-scale flight: the nearest other body within approach range is the land target; press E
            // to land on it. With none in range, L returns you to the body you launched from.
            _landTargetId = null;
            _landTargetName = null;
            _landTargetSq = float.MaxValue;
            foreach (var body in _landables)
            {
                float approach = body.Radius + KeepOutMargin + LandBand; // just outside its keep-out shell
                float sq = (body.Pos - pos).sqrMagnitude;
                if (sq <= approach * approach && sq < _landTargetSq)
                {
                    _landTargetId = body.Id;
                    _landTargetName = body.Name;
                    _landTargetSq = sq;
                }
            }

            // Report our position so the server runs authoritative collisions against asteroids/entities and
            // the other players in this instance can see our ship.
            _moveSendTimer -= Time.deltaTime;
            if (_moveSendTimer <= 0f)
            {
                _moveSendTimer = 0.08f; // ~12 Hz
                Game.Network?.SendShipMove(_ship.transform.localPosition, _yaw);
            }

            // Boarding: find the nearest station in range; E boards it (the server validates the range).
            _nearStationId = null;
            _nearStationSq = float.MaxValue;
            var space = Game.Space;
            if (space != null)
            {
                float best = BoardRange * BoardRange;
                foreach (var e in space.Entities)
                {
                    if (e.Kind != "SpaceStation")
                    {
                        continue;
                    }

                    float sq = (new Vector3(e.X, e.Y, e.Z) - _ship.transform.localPosition).sqrMagnitude;
                    if (sq < best)
                    {
                        best = sq;
                        _nearStationId = e.Id;
                        _nearStationName = e.Name;
                        _boardTargetPos = new Vector3(e.X, e.Y, e.Z);
                        _nearStationSq = sq;
                    }
                }
            }

            // E is the context action while flying: dock with a station you're next to, or — like docking —
            // land on a planet/moon you've flown up close to (the server flies the descent). L stays as the
            // "return to the body you launched from" shortcut.
            if (Input.GetKeyDown(KeyCode.E))
            {
                // Whichever you're closest to wins: dock the station or land on the body.
                bool stationCloser = _nearStationId != null && (_landTargetId == null || _nearStationSq <= _landTargetSq);
                if (stationCloser)
                {
                    _phase = Phase.Boarding; // short dock-approach animation; board intent sent on completion
                    _seq = 0f;
                    _boardSent = false;
                    _boardWait = 0f;
                    _boardTargetId = _nearStationId;
                    _boardStartPos = _ship.transform.localPosition;
                }
                else if (_landTargetId != null)
                {
                    // One press opens the planet map; you pick the pad to land on there (item 38 / B?).
                    OpenPadChooser(_landTargetId);
                }
            }

            // Ship-systems quick-bar: 1–9 pick the active system, LMB uses it. The laser auto-locks the best
            // target ahead (mines asteroids + fights hostiles); the tractor beam sweeps in nearby salvage.
            RebuildSystems();
            for (int n = 0; n < _systems.Count && n < 9; n++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + n))
                {
                    _selectedSystem = n;
                }
            }

            _fireCd -= Time.deltaTime;
            var sys = _systems[_selectedSystem];
            if (sys.Kind == "laser")
            {
                var target = BestFireTarget();
                _fireTargetId = target?.Id;
                if (target != null && _fireCd <= 0f && Input.GetMouseButton(0))
                {
                    _fireCd = FireRate;
                    FireAt(target, sys.WeaponKey);
                }
            }
            else // tractor
            {
                _fireTargetId = null;
                if (_fireCd <= 0f && Input.GetMouseButtonDown(0))
                {
                    _fireCd = 0.6f;
                    ActivateTractor();
                }
            }
        }

        /// <summary>Opens the landing-pad chooser for a body (item 38): asks the server for its pads + occupancy
        /// and shows the keyboard chooser. An empty body id means the current body (land back where you launched).</summary>
        private void OpenPadChooser(string bodyId)
        {
            _confirmLand = true;
            _choosePadBody = bodyId ?? string.Empty;
            Game.Network?.SendRequestLandingPads(_choosePadBody);
        }

        /// <summary>Closes the landing-pad map without landing (Esc / cancel).</summary>
        private void CancelLandChooser()
        {
            _confirmLand = false;
            _choosePadBody = null;
            HideLandMap();
        }

        /// <summary>Touches down on the chosen pad and tears the chooser down.</summary>
        private void LandOnPad(int padIndex)
        {
            Game.Network?.SendLeaveSpace(_choosePadBody, padIndex);
            _confirmLand = false;
            _choosePadBody = null;
            HideLandMap();
        }

        /// <summary>Builds (once per body) the planet map shown before landing: a top-down plan of the body's
        /// fixed landing pads plotted by their world X/Z, each a clickable marker — green = free (click to land
        /// here), red = taken by another player. The cursor is freed so the player can click a pad (item 38).</summary>
        private void ShowLandMap(BlocksBeyondTheStars.Networking.Messages.NetLandingPad[] pads)
        {
            if (_landMapGo != null && _landMapBody == _choosePadBody)
            {
                return; // already showing this body's pads
            }

            HideLandMap();
            _landMapBody = _choosePadBody;
            EnsureUi();

            var loc = Game.Localizer;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Centre panel in the 1536×864 HUD space (equirect map strip + button rows below).
            const float pw = 760f, ph = 540f;
            float px = (UiKit.HudRefW - pw) * 0.5f, py = (UiKit.HudRefH - ph) * 0.5f;
            var panel = UiKit.AddPanel(_ui.transform, px, py, pw, ph, new Color(0.03f, 0.07f, 0.13f, 0.97f));
            panel.raycastTarget = true; // eat clicks behind the map
            _landMapGo = panel.gameObject;

            string title = loc != null ? loc.Get("ui.space.pad_choose") : "Choose a landing pad";
            string bodyName = string.IsNullOrEmpty(_landTargetName) ? string.Empty : $" — {_landTargetName}";
            UiKit.AddText(panel.transform, 24, 18, pw - 48, 30, title + bodyName, 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);

            // REAL planet map (the request "pads must be where they actually are"): an equirect strip of
            // the whole body — full circumference × full latitude band — baked from the actual world
            // generation (seas, lakes, height-shaded ground, this world's flora hue). Pads sit at their
            // TRUE longitudes on it; before this the pads were normalised into their own bounding box on
            // a blank panel (distorted positions, no terrain reference).
            const float pad = 40f;
            float mapW = pw - pad * 2, mapH = mapW * 0.5f; // equirect 2:1 (latitude period = circ/2)
            float mapTop = 70f;

            var body = FindStarMapBody(_choosePadBody, out string bodySystem);
            string typeKey = body?.PlanetType;
            if (string.IsNullOrEmpty(typeKey)) { typeKey = Game?.Environment?.Biome; }
            string locName = !string.IsNullOrEmpty(body?.Name)
                ? PlanetOrbitLook.LocationKeyFor(bodySystem, body.Name)
                : Game?.LocationName ?? string.Empty;
            int circ = WorldConstants.CircumferenceFor(
                !string.IsNullOrEmpty(_choosePadBody) ? _choosePadBody : Game?.LocationName ?? "home",
                ClassOf(body?.Kind, typeKey));
            int latP = WorldConstants.LatitudePeriodFor(circ);

            var mapGo = new GameObject("PadMap", typeof(RectTransform));
            mapGo.transform.SetParent(panel.transform, false);
            UiKit.Place(mapGo, pad, mapTop, mapW, mapH);
            var raw = mapGo.AddComponent<UnityEngine.UI.RawImage>();
            raw.texture = WorldMinimap.Bake(Game.Content, Game.Atlas, Game.WorldSeed, locName, typeKey, circ, 256, 128);
            raw.raycastTarget = true;

            const float marker = 40f;
            foreach (var p in pads)
            {
                // True position on the strip: u = longitude / circumference, v = latitude in the band
                // (north up). Pads are scattered across BOTH longitude and latitude, so they spread over the
                // map at their real spots (the server sends each pad's true X/Z; this is the same data the
                // ship actually lands on).
                float u = Mathf.Repeat(p.X / (float)circ, 1f);
                float vNorm = Mathf.Clamp01(0.5f + p.Z / (float)latP);
                float mx = pad + u * mapW - marker * 0.5f;
                float my = mapTop + (1f - vNorm) * mapH - marker * 0.5f;
                mx = Mathf.Clamp(mx, pad - marker * 0.5f, pad + mapW - marker * 0.5f);

                bool free = !p.Occupied;
                var col = free ? new Color(0.16f, 0.55f, 0.30f, 0.98f) : new Color(0.45f, 0.12f, 0.12f, 0.98f);
                int padIndex = p.Index;
                string label = (p.Index + 1).ToString();
                if (free)
                {
                    UiKit.AddButton(panel.transform, mx, my, marker, marker, label, () => LandOnPad(padIndex));
                }
                else
                {
                    var occ = UiKit.AddPanel(panel.transform, mx, my, marker, marker, col);
                    occ.raycastTarget = true;
                    UiKit.AddText(occ.transform, 0, 0, marker, marker, label, 18, UiKit.TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);
                    string who = string.IsNullOrEmpty(p.Occupant) ? "—" : p.Occupant;
                    UiKit.AddText(panel.transform, mx - 30, my + marker, marker + 60, 18, who, 12, new Color(1f, 0.6f, 0.55f), TextAnchor.UpperCenter);
                }
            }

            string hint = loc != null ? loc.Get("ui.space.pad_full") : string.Empty;
            bool anyFree = false;
            foreach (var p in pads) { if (!p.Occupied) { anyFree = true; break; } }
            UiKit.AddText(panel.transform, 24, ph - 96, pw - 48, 22,
                anyFree ? (loc != null ? loc.Get("ui.space.pad_choose") : "Click a free (green) pad to land")
                        : (loc != null ? loc.Get("ui.space.pad_full") : "All pads are occupied — wait for one to free up"),
                15, anyFree ? UiKit.CyanDim : new Color(1f, 0.6f, 0.55f), TextAnchor.MiddleCenter);

            UiKit.AddButton(panel.transform, (pw - 220) * 0.5f, ph - 64, 220, 46,
                loc != null ? loc.Get("ui.action.close") : "Cancel (Esc)", CancelLandChooser);
        }

        /// <summary>Finds a star-map body by id (the active system is searched too), or null.
        /// <paramref name="systemName"/> carries the owning system's name for the flora location key.</summary>
        private BlocksBeyondTheStars.Networking.Messages.NetBody FindStarMapBody(string bodyId, out string systemName)
        {
            systemName = string.Empty;
            var map = Game?.StarMap;
            if (map?.Systems == null || string.IsNullOrEmpty(bodyId))
            {
                return null;
            }

            foreach (var sys in map.Systems)
            {
                foreach (var b in sys.Bodies)
                {
                    if (b.Id == bodyId)
                    {
                        systemName = sys.Name;
                        return b;
                    }
                }
            }

            return null;
        }

        /// <summary>Tears down the landing-pad map + re-locks the cursor for flight.</summary>
        private void HideLandMap()
        {
            if (_landMapGo != null)
            {
                Destroy(_landMapGo);
                _landMapGo = null;
            }

            _landMapBody = null;
            if (Game != null && Game.SpaceViewActive)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        /// <summary>Rebuilds the quick-bar of ship systems from the active ship's fitted modules (a weapon
        /// laser, a tractor beam, …). Always offers at least the laser so a fresh ship can fight.</summary>
        private void RebuildSystems()
        {
            _systems.Clear();
            var mods = Game.ShipCombat?.Modules;
            string weapon = null;
            if (mods != null)
            {
                foreach (var m in mods)
                {
                    if (m == "ship_laser_basic" || m == "ship_cannon_1" || m == "laser_cannon_2" || m == "asteroid_breaker")
                    {
                        weapon = m;
                        break;
                    }
                }
            }

            _systems.Add(new ShipSystem { Label = Loc("ui.space.sys_laser", "Laser"), Kind = "laser", WeaponKey = weapon ?? FlightWeapon });
            if (mods != null && System.Array.IndexOf(mods, "tractor_beam") >= 0)
            {
                _systems.Add(new ShipSystem { Label = Loc("ui.space.sys_tractor", "Tractor"), Kind = "tractor" });
            }

            _selectedSystem = Mathf.Clamp(_selectedSystem, 0, _systems.Count - 1);
        }

        private string Loc(string key, string fallback) => Game.Localizer != null ? Game.Localizer.Get(key) : fallback;

        /// <summary>Manual tractor sweep: pulls nearby salvage in (server-authoritative) with a cyan beam + ping.</summary>
        private void ActivateTractor()
        {
            Game.Network?.SendTractorPull();
            Vector3 from = _ship.transform.localPosition;
            Vector3 to = from + _ship.transform.localRotation * Vector3.forward * 12f;
            SpawnLaserBeam(from, to, new Color(0.4f, 0.85f, 1f));
            ClientAudio.Instance?.Cue("scan_ping");
        }

        /// <summary>The best entity to fire on: the nearest asteroid/hostile within weapon range that's
        /// roughly ahead of the ship (so you aim by pointing the nose at it).</summary>
        private BlocksBeyondTheStars.Networking.Messages.NetCombatEntity BestFireTarget()
        {
            var space = Game.Space;
            if (space == null || _ship == null)
            {
                return null;
            }

            Vector3 shipPos = _ship.transform.localPosition;
            Vector3 fwd = _ship.transform.localRotation * Vector3.forward;
            BlocksBeyondTheStars.Networking.Messages.NetCombatEntity best = null;
            float bestScore = 0.25f; // require at least this much forward alignment
            foreach (var e in space.Entities)
            {
                if (e.Kind != "Asteroid" && e.Kind != "Drone" && e.Kind != "Ufo" && e.Kind != "Cruiser")
                {
                    continue;
                }

                Vector3 to = new Vector3(e.X, e.Y, e.Z) - shipPos;
                float dist = to.magnitude;
                if (dist > WeaponRange || dist < 0.001f)
                {
                    continue;
                }

                float align = Vector3.Dot(to / dist, fwd);          // 1 = dead ahead
                float score = align - (dist / WeaponRange) * 0.25f;  // prefer aligned + close
                if (score > bestScore)
                {
                    bestScore = score;
                    best = e;
                }
            }

            return best;
        }

        /// <summary>Sends the fire intent + plays the laser beam, impact flash and sound. Mining (asteroid)
        /// reads amber; combat reads cyan.</summary>
        private void FireAt(BlocksBeyondTheStars.Networking.Messages.NetCombatEntity target, string weaponKey)
        {
            Game.Network?.SendFireWeapon(weaponKey, target.Id);
            bool mining = target.Kind == "Asteroid";
            Color col = mining ? new Color(1f, 0.7f, 0.25f) : new Color(0.45f, 1f, 1f);

            Vector3 muzzle = _ship.transform.localPosition + _ship.transform.localRotation * new Vector3(0f, 0f, 2.2f);
            Vector3 hit = new Vector3(target.X, target.Y, target.Z);
            SpawnLaserBeam(muzzle, hit, col);
            ClientAudio.Instance?.Cue(mining ? "ship_mine" : "ship_laser");
        }

        /// <summary>A bright laser bolt from the ship to the target plus an impact flash, both fading fast.</summary>
        private void SpawnLaserBeam(Vector3 from, Vector3 to, Color color)
        {
            var beam = Cube("LaserBolt", _root.transform, Vector3.zero, Vector3.one, Unlit(color));
            float len = Vector3.Distance(from, to);
            beam.transform.localPosition = (from + to) * 0.5f;
            beam.transform.localRotation = len > 0.001f ? Quaternion.LookRotation(to - from) : Quaternion.identity;
            beam.transform.localScale = new Vector3(0.16f, 0.16f, len);
            _beams.Add(new TractorBeam { Go = beam, Life = 0.1f, Max = 0.1f });

            var flash = Cube("LaserImpact", _root.transform, to, Vector3.one * 1.4f, Unlit(color));
            _beams.Add(new TractorBeam { Go = flash, Life = 0.14f, Max = 0.14f });
        }

        /// <summary>Server reports the player is now on an EVA (stepped out the ship's airlock): set up the
        /// first-person 6-DOF suit float next to the parked ship. The ship stays where it is.</summary>
        private void BeginEvaMode()
        {
            if (_ship == null)
            {
                return;
            }

            var rot = _ship.transform.localRotation;
            _evaYaw = _yaw;
            _evaPitch = _pitch;

            // Start the suit clearly OUTSIDE + behind the hull, facing it, so you actually see your ship. The
            // voxel ship is full-size (1:1) on an EVA, so the offset scales with its length (the old fixed -3.4
            // was tuned for the small cube model and left you spawning inside the big voxel hull → "no ship").
            float backZ = _shipCells != null && _shipCells.Count > 0 ? _shipCentre.z + 6f : 3.4f;
            _evaPos = _ship.transform.localPosition + rot * new Vector3(0f, -1f, -backZ);
            if (_evaPos.magnitude > _bounds)
            {
                _evaPos = _evaPos.normalized * _bounds;
            }

            _eva = true;
            ClientAudio.Instance?.Cue("scan_ping"); // a soft suit blip as the airlock cycles
        }

        /// <summary>Free-floating suit control while on EVA: mouse look + WASD + Space/Ctrl up/down (6-DOF).
        /// Float up to the parked ship (or a station) and press E to board — no take-off animation.</summary>
        private void UpdateEva()
        {
            if (_ship == null || Game.MenuOpen)
            {
                return;
            }

            // Free-look.
            _evaYaw += Input.GetAxis("Mouse X") * LookSpeed;
            _evaPitch = Mathf.Clamp(_evaPitch - Input.GetAxis("Mouse Y") * LookSpeed, -89f, 89f);
            var rot = Quaternion.Euler(_evaPitch, _evaYaw, 0f);

            // 6-DOF thrust: WASD in the look plane, Space/Ctrl for world up/down.
            float fwd = Input.GetAxis("Vertical");
            float strafe = Input.GetAxis("Horizontal");
            float lift = (Input.GetKey(KeyCode.Space) ? 1f : 0f)
                       - ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C)) ? 1f : 0f);
            Vector3 dir = rot * (Vector3.forward * fwd + Vector3.right * strafe) + Vector3.up * lift;
            Vector3 delta = dir * (EvaSpeed * Time.deltaTime);

            // Don't float through the ship or an asteroid. With voxel structures present (S2/S3) collide against
            // their block grids — so you can drift right up to a hull/rock — otherwise fall back to a sphere keep-out.
            if (HasVoxelStructures())
            {
                _evaPos = ResolveEvaVoxelMove(_evaPos, delta);
            }
            else
            {
                _evaPos += delta;
                Vector3 toShip = _evaPos - _ship.transform.localPosition;
                float shipDist = toShip.magnitude;
                if (shipDist < ShipKeepOut && shipDist > 0.0001f)
                {
                    _evaPos = _ship.transform.localPosition + toShip / shipDist * ShipKeepOut;
                }
            }

            // Stay inside the flight bounds and don't drift into a body — slide along its keep-out shell.
            if (_evaPos.magnitude > _bounds)
            {
                _evaPos = _evaPos.normalized * _bounds;
            }

            foreach (var ob in _keepOut)
            {
                Vector3 d = _evaPos - ob.Pos;
                float dist = d.magnitude;
                if (dist < ob.Radius && dist > 0.0001f)
                {
                    _evaPos = ob.Pos + d / dist * ob.Radius;
                }
            }

            // Hotbar select (number keys + scroll) so you can pick the block/tool to build with out here.
            UpdateEvaHotbarSelect();

            // Aim at the ship's voxel grid and build/mine on it (S2).
            UpdateEvaBuild();

            // item 20 S4: deploy a station core (B) to start a player-built station.
            if (!Game.MenuOpen && Input.GetKeyDown(KeyCode.B))
            {
                Game.Network?.SendDeployStationCore();
            }

            // Report the suit's pose so the other players in this instance see us floating (the server tags it
            // as an EVA from our InEva state).
            _moveSendTimer -= Time.deltaTime;
            if (_moveSendTimer <= 0f)
            {
                _moveSendTimer = 0.08f;
                Game.Network?.SendShipMove(_evaPos, _evaYaw);
            }

            // Board targets: the parked ship, or the nearest station in range (whichever is closer wins on E).
            _evaShipSq = (_ship.transform.localPosition - _evaPos).sqrMagnitude;
            _evaNearShip = _evaShipSq <= EvaBoardRange * EvaBoardRange;

            _nearStationId = null;
            _nearStationSq = float.MaxValue;
            var space = Game.Space;
            if (space != null)
            {
                float best = BoardRange * BoardRange;
                foreach (var e in space.Entities)
                {
                    if (e.Kind != "SpaceStation")
                    {
                        continue;
                    }

                    float sq = (new Vector3(e.X, e.Y, e.Z) - _evaPos).sqrMagnitude;
                    if (sq < best)
                    {
                        best = sq;
                        _nearStationId = e.Id;
                        _nearStationName = e.Name;
                        _boardTargetPos = new Vector3(e.X, e.Y, e.Z);
                        _nearStationSq = sq;
                    }
                }
            }

            if (Input.GetKeyDown(KeyCode.E))
            {
                bool stationCloser = _nearStationId != null && (!_evaNearShip || _nearStationSq <= _evaShipSq);
                if (stationCloser)
                {
                    DockStationFromEva();
                }
                else if (_evaNearShip)
                {
                    BoardShipFromEva();
                }
            }
        }

        // ---------------- item 20 S2/S3: free-space build/mine + voxel collision (ship + asteroids) ----------------

        /// <summary>Resolves a structure id to its transform + voxel grid (the own ship, or an asteroid body), or
        /// returns null if it isn't present/built.</summary>
        private Transform StructTransform(string id, out Dictionary<Vector3i, BlockId> cells, out Vector3 centre)
        {
            if (id == _shipStructureId && _shipCells != null && _ship != null)
            {
                cells = _shipCells; centre = _shipCentre; return _ship.transform;
            }

            if (_structs.TryGetValue(id, out var vs) && vs.Root != null)
            {
                cells = vs.Cells; centre = vs.Centre; return vs.Root.transform;
            }

            cells = null; centre = Vector3.zero; return null;
        }

        /// <summary>Root-local point → a structure's design-grid coords (axis-aligned with its voxels), and back.</summary>
        private Vector3 ToDesign(Transform t, Vector3 centre, Vector3 rootPoint)
            => t.InverseTransformPoint(_root.transform.TransformPoint(rootPoint)) + centre;

        private Vector3 FromDesign(Transform t, Vector3 centre, Vector3 designPoint)
            => _root.transform.InverseTransformPoint(t.TransformPoint(designPoint - centre));

        private static bool CellsBlock(Dictionary<Vector3i, BlockId> cells, Vector3 designPos)
        {
            int x0 = Mathf.FloorToInt(designPos.x - SuitRadius), x1 = Mathf.FloorToInt(designPos.x + SuitRadius);
            int y0 = Mathf.FloorToInt(designPos.y - SuitRadius), y1 = Mathf.FloorToInt(designPos.y + SuitRadius);
            int z0 = Mathf.FloorToInt(designPos.z - SuitRadius), z1 = Mathf.FloorToInt(designPos.z + SuitRadius);
            for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
            {
                if (cells.ContainsKey(new Vector3i(x, y, z)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True if a suit-sized sphere at this root-local point overlaps a solid cell of ANY structure.</summary>
        private bool SuitBlockedWorld(Vector3 rootPos)
        {
            if (_shipCells != null && _shipCells.Count > 0 && _ship != null
                && CellsBlock(_shipCells, ToDesign(_ship.transform, _shipCentre, rootPos)))
            {
                return true;
            }

            foreach (var vs in _structs.Values)
            {
                if (vs.Root != null && vs.Cells != null && vs.Cells.Count > 0
                    && CellsBlock(vs.Cells, ToDesign(vs.Root.transform, vs.Centre, rootPos)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True if any voxel structure (ship or asteroid) is present to collide/build against.</summary>
        private bool HasVoxelStructures()
            => (_shipCells != null && _shipCells.Count > 0) || _structs.Count > 0;

        /// <summary>Moves the suit by <paramref name="delta"/> (root-local) but stops it passing through any
        /// structure: resolves each world axis so it slides along the voxels instead of penetrating.</summary>
        private Vector3 ResolveEvaVoxelMove(Vector3 evaPos, Vector3 delta)
        {
            Vector3 p = evaPos;
            var tryX = new Vector3(p.x + delta.x, p.y, p.z);
            if (!SuitBlockedWorld(tryX)) { p.x = tryX.x; }
            var tryY = new Vector3(p.x, p.y + delta.y, p.z);
            if (!SuitBlockedWorld(tryY)) { p.y = tryY.y; }
            var tryZ = new Vector3(p.x, p.y, p.z + delta.z);
            if (!SuitBlockedWorld(tryZ)) { p.z = tryZ.z; }
            return p;
        }

        /// <summary>Ray-marches one structure's voxel grid from the suit's eye (a design-space DDA mirroring the
        /// on-foot AimBlock). Returns the hit cell, the empty cell before it, and the hit distance.</summary>
        private bool RayMarchCells(Transform t, Dictionary<Vector3i, BlockId> cells, Vector3 centre,
            Vector3 rootOrigin, Vector3 rootDir, out Vector3Int hitCell, out Vector3Int placeCell, out float dist)
        {
            hitCell = default; placeCell = default; dist = 0f;
            Vector3 o = ToDesign(t, centre, rootOrigin);
            Vector3 dir = t.InverseTransformDirection(_root.transform.TransformDirection(rootDir)).normalized;

            int x = Mathf.FloorToInt(o.x), y = Mathf.FloorToInt(o.y), z = Mathf.FloorToInt(o.z);
            int px = x, py = y, pz = z;
            int sx = dir.x >= 0 ? 1 : -1, sy = dir.y >= 0 ? 1 : -1, sz = dir.z >= 0 ? 1 : -1;
            float invx = Mathf.Abs(dir.x) > 1e-6f ? 1f / Mathf.Abs(dir.x) : float.PositiveInfinity;
            float invy = Mathf.Abs(dir.y) > 1e-6f ? 1f / Mathf.Abs(dir.y) : float.PositiveInfinity;
            float invz = Mathf.Abs(dir.z) > 1e-6f ? 1f / Mathf.Abs(dir.z) : float.PositiveInfinity;
            float tMaxX = float.IsInfinity(invx) ? float.PositiveInfinity : (dir.x > 0 ? (x + 1 - o.x) : (o.x - x)) * invx;
            float tMaxY = float.IsInfinity(invy) ? float.PositiveInfinity : (dir.y > 0 ? (y + 1 - o.y) : (o.y - y)) * invy;
            float tMaxZ = float.IsInfinity(invz) ? float.PositiveInfinity : (dir.z > 0 ? (z + 1 - o.z) : (o.z - z)) * invz;

            float tt = 0f;
            for (int i = 0; i < 80 && tt <= EvaReach; i++)
            {
                if (cells.ContainsKey(new Vector3i(x, y, z)))
                {
                    hitCell = new Vector3Int(x, y, z);
                    placeCell = new Vector3Int(px, py, pz);
                    dist = tt;
                    return true;
                }

                px = x; py = y; pz = z;
                if (tMaxX <= tMaxY && tMaxX <= tMaxZ) { x += sx; tt = tMaxX; tMaxX += invx; }
                else if (tMaxY <= tMaxZ) { y += sy; tt = tMaxY; tMaxY += invy; }
                else { z += sz; tt = tMaxZ; tMaxZ += invz; }
            }

            return false;
        }

        /// <summary>Aims at the nearest voxel structure (ship or asteroid) the suit is looking at.</summary>
        private bool AimVoxel(out string structId, out Vector3Int hitCell, out Vector3Int placeCell)
        {
            structId = null; hitCell = default; placeCell = default;
            if (_root == null)
            {
                return false;
            }

            Vector3 rootDir = Quaternion.Euler(_evaPitch, _evaYaw, 0f) * Vector3.forward;
            float bestT = float.MaxValue;
            string bestId = null;
            Vector3Int bestHit = default, bestPlace = default;

            if (_shipCells != null && _ship != null && _shipCells.Count > 0
                && RayMarchCells(_ship.transform, _shipCells, _shipCentre, _evaPos, rootDir, out var sh, out var sp, out var sd)
                && sd < bestT)
            {
                bestT = sd; bestId = _shipStructureId; bestHit = sh; bestPlace = sp;
            }

            foreach (var kv in _structs)
            {
                var vs = kv.Value;
                if (vs.Root == null || vs.Cells == null || vs.Cells.Count == 0)
                {
                    continue;
                }

                if (RayMarchCells(vs.Root.transform, vs.Cells, vs.Centre, _evaPos, rootDir, out var hc, out var pc, out var d)
                    && d < bestT)
                {
                    bestT = d; bestId = kv.Key; bestHit = hc; bestPlace = pc;
                }
            }

            if (bestId == null)
            {
                return false;
            }

            structId = bestId; hitCell = bestHit; placeCell = bestPlace;
            return true;
        }

        /// <summary>EVA aim + build/mine: highlights the targeted cell; LMB mines it (ship hull or asteroid ore),
        /// RMB places the held block in the empty cell before it. Server-authoritative — it validates + broadcasts.</summary>
        /// <summary>Lets the EVA player pick the active hotbar slot (1–9 keys or scroll), mirroring the on-foot
        /// controls — so they can choose which block/tool to build with while floating in space.</summary>
        private void UpdateEvaHotbarSelect()
        {
            const int slots = 9;
            for (int i = 0; i < slots; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                {
                    SelectEvaSlot(i);
                }
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll > 0f) { SelectEvaSlot((Game.SelectedHotbarSlot + slots - 1) % slots); }
            else if (scroll < 0f) { SelectEvaSlot((Game.SelectedHotbarSlot + 1) % slots); }
        }

        private void SelectEvaSlot(int slot)
        {
            if (slot == Game.SelectedHotbarSlot) { return; }
            Game.SelectedHotbarSlot = slot;
            Game.Network?.SendSelectHotbar(slot);
        }

        private void UpdateEvaBuild()
        {
            _evaHasAim = AimVoxel(out _evaAimStructId, out _evaAimHit, out _evaAimPlace);
            UpdateAimHighlight();

            if (Game.MenuOpen || !_evaHasAim || string.IsNullOrEmpty(_evaAimStructId))
            {
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                Game.Network?.SendStructureEdit(_evaAimStructId, _evaAimHit.x, _evaAimHit.y, _evaAimHit.z, mine: true);
            }
            else if (Input.GetMouseButtonDown(1))
            {
                string item = Game.ItemInSlot(Game.SelectedHotbarSlot) ?? string.Empty;
                var def = string.IsNullOrEmpty(item) ? null : Game.Content?.GetItem(item);
                if (def != null && !string.IsNullOrEmpty(def.PlacesBlock))
                {
                    Game.Network?.SendStructureEdit(_evaAimStructId, _evaAimPlace.x, _evaAimPlace.y, _evaAimPlace.z, mine: false, item);
                }
            }
        }

        /// <summary>Shows a small glowing marker on the cell the suit is aiming at (build/mine feedback).</summary>
        private void UpdateAimHighlight()
        {
            Transform t = null;
            Vector3 centre = Vector3.zero;
            if (_evaHasAim)
            {
                t = StructTransform(_evaAimStructId, out _, out centre);
            }

            if (!_evaHasAim || _root == null || t == null)
            {
                if (_aimHighlight != null) { _aimHighlight.SetActive(false); }
                return;
            }

            if (_aimHighlight == null)
            {
                _aimHighlight = Cube("AimMarker", _root.transform, Vector3.zero, Vector3.one * 0.22f, Unlit(new Color(0.3f, 0.95f, 1f)));
            }

            _aimHighlight.SetActive(true);
            var cellCentre = new Vector3(_evaAimHit.x + 0.5f, _evaAimHit.y + 0.5f, _evaAimHit.z + 0.5f);
            _aimHighlight.transform.localPosition = FromDesign(t, centre, cellCentre);
        }

        /// <summary>Applies a server structure edit to the local voxel grid (ship or asteroid) + re-meshes.</summary>
        private void OnStructureBlockChanged(BlocksBeyondTheStars.Networking.Messages.StructureBlockChanged m)
        {
            var key = new Vector3i(m.X, m.Y, m.Z);
            if (m.StructureId == _shipStructureId && _shipCells != null)
            {
                if (m.Block == 0) { _shipCells.Remove(key); }
                else { _shipCells[key] = new BlockId(m.Block); }

                RebuildShipVoxels();
                return;
            }

            if (_structs.TryGetValue(m.StructureId, out var vs) && vs.Cells != null)
            {
                if (m.Block == 0) { vs.Cells.Remove(key); }
                else { vs.Cells[key] = new BlockId(m.Block); }

                vs.MeshDirty = true;
            }
        }

        /// <summary>A voxel structure design arrived (item 20 S3 — asteroids). The own ship (Kind "ship") is
        /// handled via Game.ShipDesign; other kinds become static voxel bodies at their world position.</summary>
        private void OnStructureDesign(BlocksBeyondTheStars.Networking.Messages.SpaceShipDesign m)
        {
            if (m.Kind == "ship" || string.IsNullOrEmpty(m.Kind))
            {
                return;
            }

            var cells = CellsFromDesign(m, out var centre);
            if (_structs.TryGetValue(m.Id, out var existing) && existing.Root != null)
            {
                Destroy(existing.Root); // a fresh design replaces the old mesh
            }

            _structs[m.Id] = new VoxStruct
            {
                Cells = cells, Centre = centre, Pos = new Vector3(m.PosX, m.PosY, m.PosZ), Root = null, MeshDirty = true,
            };
        }

        /// <summary>A space entity was destroyed — if it was a voxel asteroid body, drop its mesh (item 20 S3).</summary>
        private void OnStructEntityDestroyed(BlocksBeyondTheStars.Networking.Messages.SpaceEntityDestroyed m)
        {
            if (_structs.ContainsKey(m.Id))
            {
                _structRemove.Add(m.Id);
            }
        }

        /// <summary>Builds/updates/removes the asteroid voxel bodies' GameObjects (item 20 S3). Runs while the
        /// flight view is active so designs that arrived before the scene existed get built once it does.</summary>
        private void ReconcileStructs()
        {
            if (_root == null)
            {
                return;
            }

            if (_structRemove.Count > 0)
            {
                foreach (var id in _structRemove)
                {
                    if (_structs.TryGetValue(id, out var vs))
                    {
                        if (vs.Root != null) { Destroy(vs.Root); }
                        _structs.Remove(id);
                    }
                }

                _structRemove.Clear();
            }

            // S5: unload far voxel bodies (keep their data; rebuild when you come back near) so dozens of
            // structures don't all carry live meshes. The reference is the suit on an EVA, else the ship.
            Vector3 viewer = _eva ? _evaPos : (_ship != null ? _ship.transform.localPosition : Vector3.zero);
            float farSq = FarStructUnload * FarStructUnload;
            foreach (var vs in _structs.Values)
            {
                bool near = (vs.Pos - viewer).sqrMagnitude <= farSq;
                if (near && vs.Root == null)
                {
                    vs.MeshDirty = true; // came back into range → rebuild below
                }
                else if (!near && vs.Root != null)
                {
                    Destroy(vs.Root);
                    vs.Root = null; // out of range → drop the mesh, keep the data
                }
            }

            foreach (var vs in _structs.Values)
            {
                if (!vs.MeshDirty)
                {
                    continue;
                }

                if (vs.Root == null)
                {
                    vs.Root = new GameObject("Asteroid");
                    vs.Root.transform.SetParent(_root.transform, false);
                    vs.Root.transform.localPosition = vs.Pos;
                }

                BuildVoxChunks(vs.Root.transform, vs.Cells, vs.Centre);
                vs.MeshDirty = false;
            }
        }

        /// <summary>Tears down all asteroid voxel bodies (on leaving the flight view).</summary>
        private void ClearStructs()
        {
            foreach (var vs in _structs.Values)
            {
                if (vs.Root != null) { Destroy(vs.Root); }
            }

            _structs.Clear();
            _structRemove.Clear();
        }

        /// <summary>Climbs back in through the airlock from an EVA — into the ship's walkable interior (on
        /// foot), the way you came out. From there the helm takes you back to flying.</summary>
        private void BoardShipFromEva()
        {
            _eva = false;
            _enteringInterior = true;       // tear the flight view down at once (no landing descent)
            Game.BeginWorldTransition();     // veil immediately so the flight view doesn't flash first (B34)
            Game.Network?.SendEnterShip();  // server ends the EVA and loads the ship interior
            ClientAudio.Instance?.Cue("scan_ping");
        }

        /// <summary>Docks the nearby station directly from an EVA: ends the spacewalk and reuses the normal
        /// station-board teardown (the dock-approach is already "done" since you floated over on foot).</summary>
        private void DockStationFromEva()
        {
            // Keep InEva set on the server until BoardStation runs, so it knows the ship stayed floating
            // (undocking returns you to the float). The server clears InEva as it docks you.
            _eva = false;
            _phase = Phase.Boarding;
            _seq = BoardDuration;     // skip the fly-in: you're already at the hull on foot
            _boardSent = false;
            _boardWait = 0f;
            _boardTargetId = _nearStationId;
            _boardStartPos = _ship.transform.localPosition;
        }

        /// <summary>Flies the ship in to dock with the station + fades out, then sends the board intent.
        /// A safety timeout reverts to cruise if the server never confirms (so it can't hang on black).</summary>
        private void UpdateBoarding()
        {
            _seq += Time.deltaTime;
            float t = Mathf.Clamp01(_seq / BoardDuration);
            float ease = 1f - (1f - t) * (1f - t);
            if (_ship != null)
            {
                var dock = Vector3.Lerp(_boardTargetPos, _boardStartPos, 0.14f); // stop just short of the hull
                _ship.transform.localPosition = Vector3.Lerp(_boardStartPos, dock, ease);
                var dir = _boardTargetPos - _ship.transform.localPosition;
                if (dir.sqrMagnitude > 0.001f)
                {
                    var look = Quaternion.LookRotation(dir.normalized, Vector3.up);
                    _ship.transform.localRotation = Quaternion.Slerp(_ship.transform.localRotation, look, Time.deltaTime * 4f);
                }
            }

            if (_seq >= BoardDuration && !_boardSent)
            {
                _boardSent = true;
                Game.BeginWorldTransition(); // veil now (the approach has played) so the station doesn't flash (B34)
                Game.Network?.SendBoardStation(_boardTargetId);
            }

            if (_boardSent)
            {
                _boardWait += Time.deltaTime;
                if (_boardWait > 2.5f) // server didn't confirm (e.g. rejected) — recover instead of hanging
                {
                    _phase = Phase.Cruise;
                    _seq = 0f;
                    _boardSent = false;
                }
            }
        }

        private void PlaceCamera()
        {
            if (_ship == null)
            {
                return;
            }

            // EVA: first-person from the suit, looking where you steer.
            if (_eva)
            {
                Camera.transform.localPosition = _evaPos;
                Camera.transform.localRotation = Quaternion.Euler(_evaPitch, _evaYaw, 0f);
                if (_shake > 0.001f)
                {
                    Camera.transform.localPosition += Random.insideUnitSphere * (_shake * 0.5f);
                }

                return;
            }

            var sp = _ship.transform.localPosition;
            var rot = _ship.transform.localRotation;
            if (_viewMode == 1)
            {
                // Cockpit: just ahead of the ship, facing its heading.
                Camera.transform.localPosition = sp + rot * new Vector3(0f, 0.7f, 1.9f);
                Camera.transform.localRotation = rot;
            }
            else
            {
                // Third-person: behind and above, looking at the ship.
                Camera.transform.localPosition = sp + rot * new Vector3(0f, 4.5f, -13f);
                Camera.transform.localRotation = Quaternion.LookRotation((sp + Vector3.up * 0.5f) - Camera.transform.localPosition, Vector3.up);
            }

            if (_shake > 0.001f)
            {
                Camera.transform.localPosition += Random.insideUnitSphere * (_shake * 0.5f);
            }
        }

        private void Enter()
        {
            _active = true;
            // Taking the helm again from inside the ship (or any "already airborne" entry) drops straight into
            // free flight — no take-off sequence, since you never landed.
            _phase = Game.SpaceSkipLaunch ? Phase.Cruise : Phase.Launch;
            _seq = 0f;
            _yaw = 0f;
            _pitch = 0f;
            _confirmLand = false;
            HideLandMap();
            _boardSent = false;
            _hyperjumping = false;
            _eva = false;
            _enteringInterior = false;
            _landTargetId = null;
            _landTargetName = null;
            Game.SpaceViewActive = true;

            ResolveShipFlight();
            BuildScene();

            // React to server-reported hull/shield damage (collisions, enemy fire) with a flash + shake.
            if (Game.Network != null && !_combatSubscribed)
            {
                Game.Network.ShipCombatStatusChanged += OnShipCombat;
                _combatSubscribed = true;
            }

            // A hyperspace jump while flying tears the view down without a surface-landing descent (its
            // warp overlay covers the transition); the subscription is kept for the rig's lifetime.
            if (!_hyperjumpSubscribed)
            {
                Game.HyperjumpStarted += OnHyperjump;
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
            Camera.transform.SetParent(_camPrevParent, false);
            Camera.transform.localPosition = _camPrevLocalPos;
            Camera.transform.localRotation = _camPrevLocalRot;
            Camera.farClipPlane = _camPrevFar;
            Camera.clearFlags = _camPrevClear;
            Camera.backgroundColor = _camPrevBg;

            if (_root != null)
            {
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
                _combatSubscribed = false;
            }

            _entities.Clear();
            _dropIds.Clear();
            _beams.Clear(); // their GameObjects were children of _root (already destroyed)
            _asteroidMat = null; // material lived under the destroyed view; rebuild next launch
            ClearStructs();      // item 20 S3: asteroid bodies lived under _root (destroyed) — drop the refs
            _shipCells = null;   // ship voxel grid was under the destroyed _ship
            _shipVox = null;
            _aimHighlight = null; // marker lived under _root (destroyed)
            _ship = null;
            _exhaust = null;
            _sun = null; // sun billboard lived under _root (destroyed); flare sprites persist on _ui
            _sunMat = null;
            _hatchMat = null; // hatch marker material lived under the destroyed ship
            _hullMat = null;
            _appliedHullRgb = -1;
            _active = false;
            _eva = false;
            _enteringInterior = false;
            _remotePlayers.Clear(); // their GameObjects are children of _root, destroyed with the scene
            _shake = 0f;
            _hitFlash = 0f;
            _cargoFlash = 0f;
            Game.SpaceViewActive = false;
        }

        private void OnHyperjump() => _hyperjumping = true;

        private void OnShipCombat(BlocksBeyondTheStars.Networking.Messages.ShipCombatStatus s)
        {
            // Scale the jolt + red flash to how hard we were hit, and *set* (not accumulate) them — so a
            // steady trickle of chip damage is a faint rumble, while a real hit is a sharp jolt. Accumulating
            // every tick made continuous fire pin the shake/flash at maximum (the ship "being shaken").
            float shieldDrop = _lastShield >= 0f ? Mathf.Max(0f, _lastShield - s.Shield) : 0f;
            float hullDrop = _lastHull >= 0f ? Mathf.Max(0f, _lastHull - s.Hull) : 0f;
            float drop = Mathf.Max(shieldDrop, hullDrop);
            if (drop > 0.05f && _active)
            {
                float mag = Mathf.Clamp01(drop / 8f); // a full jolt only for a big single hit (~8 dmg)
                _hitFlash = Mathf.Max(_hitFlash, 0.2f + 0.6f * mag);
                _shake = Mathf.Max(_shake, 0.08f + 0.6f * mag);
                SpawnHitSparks(mag); // visible damage at the hull, not just a screen tint
            }

            _lastHull = s.Hull;
            _lastShield = s.Shield;
        }

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

        private void BuildScene()
        {
            _root = new GameObject("SpaceScene");
            _root.transform.position = SceneOrigin;
            _cloudShells.Clear();
            _keepOut.Clear();

            // Background stars are drawn by the shared Starfield dome (soft round dots, varied colours, a
            // gentle twinkle) which follows the camera and is at full brightness in space — so the space view
            // no longer spawns its own blocky star cubes.
            BuildSun(); // the system's star, in its own colour (lens flare added in LateUpdate)

            // Only REAL bodies: the planet you launched from + the system's actual planets/moons (from the
            // star map), all textured + landable. No decorative filler planets.
            BuildSystemBodies();

            _ship = BuildShip(_root.transform);
        }

        /// <summary>Places the other bodies of the current star system at their (scaled) system coordinates,
        /// relative to the body you launched from, so you can fly between them and land on any one.</summary>
        private void BuildSystemBodies()
        {
            _landables.Clear();
            _bounds = Bounds;

            var map = Game?.StarMap;
            BlocksBeyondTheStars.Networking.Messages.NetBody current = null;
            BlocksBeyondTheStars.Networking.Messages.NetStarSystem system = null;
            if (map?.Systems != null)
            {
                foreach (var sys in map.Systems)
                {
                    foreach (var b in sys.Bodies)
                    {
                        if (b.Id == map.ActiveLocationId)
                        {
                            current = b;
                            system = sys;
                            break;
                        }
                    }

                    if (system != null)
                    {
                        break;
                    }
                }
            }

            // The planet you launched from: a real body, rendered "below" you (you rose off its surface),
            // landable (E returns home). Always present, even without a star map, so there is always a real
            // planet to fly back to — never a decorative filler.
            string homeType = !string.IsNullOrEmpty(current?.PlanetType) ? current.PlanetType : Game?.Environment?.Biome;
            var homePos = new Vector3(0f, -150f, -20f);
            // Every body has its own size (deterministic from its id), so worlds + moons look big or small in
            // orbit instead of all identical — an approximate reflection of how varied the bodies are.
            float homeDiameter = OrbitDiameterFor(current?.Id ?? Game?.LocationName ?? "home", current?.Kind, current?.PlanetType) * 3.2f;
            SpawnBody("HomePlanet", current?.Id, current?.Kind, Game?.LocationName ?? "home", homePos, homeDiameter, homeType);
            _landables.Add((string.Empty, Game?.LocationName ?? "home", homePos, homeDiameter * 0.5f));
            _keepOut.Add((homePos, homeDiameter * 0.5f + KeepOutMargin));
            float maxDist = homePos.magnitude;

            // The system's other planets/moons at their (scaled) orbit coords — all real, all landable.
            if (current != null && system != null)
            {
                const float BodyGap = 8f; // clear space kept between two bodies' surfaces

                // Plan every body first (positions + radii), then nudge any overlaps apart, THEN spawn — so no
                // two planets/moons ever clip into each other at the compact view scale.
                var ids = new List<string>();
                var names = new List<string>();
                var locKeys = new List<string>(); // "System · Body" — the key the body's WORLD seeds flora hues with
                var positions = new List<Vector3>();
                var radii = new List<float>();
                var bodyTypes = new List<string>();
                var kinds = new List<string>(); // star-map Kind — keys each body's true circumference

                void Plan(string id, string name, string kind, Vector3 pos, float diameter, string type)
                {
                    ids.Add(id); names.Add(name); kinds.Add(kind); positions.Add(pos); radii.Add(diameter * 0.5f); bodyTypes.Add(type);
                    locKeys.Add(PlanetOrbitLook.LocationKeyFor(system?.Name, name));
                }

                // Pass 1: planets at their scaled orbit coords. Keep system coords + render pos so each moon
                // can be parented to its nearest planet.
                var planets = new List<(float Sx, float Sz, Vector3 Render, float Radius)>
                {
                    (current.SystemX, current.SystemZ, homePos, homeDiameter * 0.5f),
                };
                foreach (var b in system.Bodies)
                {
                    // Render every real planet in the system — even one whose PlanetType string didn't come
                    // through (fall back to the home type for colour); only skip the body you launched from.
                    if (b.Id == current.Id || b.Kind != "Planet")
                    {
                        continue;
                    }

                    string type = string.IsNullOrEmpty(b.PlanetType) ? homeType : b.PlanetType;
                    var pos = new Vector3((b.SystemX - current.SystemX) * SystemViewScale, 0f, (b.SystemZ - current.SystemZ) * SystemViewScale);
                    float diameter = OrbitDiameterFor(b.Id, b.Kind, b.PlanetType);
                    Plan(b.Id, b.Name, b.Kind, pos, diameter, type);
                    planets.Add((b.SystemX, b.SystemZ, pos, diameter * 0.5f));
                }

                // Pass 2: moons, each placed just outside its nearest parent planet's surface.
                foreach (var b in system.Bodies)
                {
                    if (b.Id == current.Id || b.Kind != "Moon")
                    {
                        continue;
                    }

                    string type = string.IsNullOrEmpty(b.PlanetType) ? homeType : b.PlanetType;
                    var parent = planets[0];
                    float bestSq = float.MaxValue;
                    foreach (var p in planets)
                    {
                        float dx = b.SystemX - p.Sx, dz = b.SystemZ - p.Sz;
                        float dsq = dx * dx + dz * dz;
                        if (dsq < bestSq) { bestSq = dsq; parent = p; }
                    }

                    var rel = new Vector3((b.SystemX - parent.Sx) * SystemViewScale, 0f, (b.SystemZ - parent.Sz) * SystemViewScale);
                    float moonDia = OrbitDiameterFor(b.Id, b.Kind, b.PlanetType);
                    float minClear = parent.Radius + moonDia * 0.5f + BodyGap; // outside the planet's surface
                    if (rel.magnitude < minClear)
                    {
                        Vector3 dir = rel.sqrMagnitude > 0.0001f ? rel.normalized : Vector3.right;
                        rel = dir * minClear;
                    }

                    Plan(b.Id, b.Name, b.Kind, parent.Render + rel, moonDia, type);
                }

                // Pass 3: large landable asteroids — scattered like planets; you fly up + press E to land on
                // them (ship or EVA → a small walkable asteroid world). The small mineable rocks are separate
                // space entities; these are the sized, landable bodies.
                foreach (var b in system.Bodies)
                {
                    if (b.Id == current.Id || b.Kind != "AsteroidField")
                    {
                        continue;
                    }

                    string type = string.IsNullOrEmpty(b.PlanetType) ? "asteroid" : b.PlanetType;
                    var pos = new Vector3((b.SystemX - current.SystemX) * SystemViewScale, 0f, (b.SystemZ - current.SystemZ) * SystemViewScale);
                    float diameter = OrbitDiameterFor(b.Id, b.Kind, type);
                    Plan(b.Id, b.Name, b.Kind, pos, diameter, type);
                }

                // Separation pass: relax any overlapping pair apart in the x-z plane until every body has a
                // clear gap to every other. (Home is excluded — it sits far "below" you and never overlaps.)
                for (int iter = 0; iter < 24; iter++)
                {
                    bool moved = false;
                    for (int a = 0; a < positions.Count; a++)
                    {
                        for (int c = a + 1; c < positions.Count; c++)
                        {
                            Vector3 d = positions[a] - positions[c];
                            d.y = 0f;
                            float dist = d.magnitude;
                            float need = radii[a] + radii[c] + BodyGap;
                            if (dist < need)
                            {
                                Vector3 dir = dist > 0.0001f ? d / dist
                                    : new Vector3(Mathf.Cos(a * 2.39996f), 0f, Mathf.Sin(a * 2.39996f)); // co-located → spread by a golden angle
                                float push = (need - dist) * 0.5f;
                                positions[a] += dir * push;
                                positions[c] -= dir * push;
                                moved = true;
                            }
                        }
                    }

                    if (!moved)
                    {
                        break;
                    }
                }

                // Spawn the separated bodies + register them as landable / keep-out.
                for (int k = 0; k < positions.Count; k++)
                {
                    SpawnBody("SystemBody_" + ids[k], ids[k], kinds[k], locKeys[k], positions[k], radii[k] * 2f, bodyTypes[k]);
                    _landables.Add((ids[k], names[k], positions[k], radii[k]));
                    _keepOut.Add((positions[k], radii[k] + KeepOutMargin)); // can't fly into it — slide + press E to land
                    maxDist = Mathf.Max(maxDist, positions[k].magnitude);
                }
            }

            _bounds = Mathf.Max(Bounds, maxDist + 140f); // keep the whole system reachable
        }

        /// <summary>The orbit-view diameter for a body, derived from its real walkable circumference — so a
        /// tiny asteroid reads small, a moon medium and a big planet large, matching how long it'd take to walk
        /// around. Same body → same size, on the surface (server) and in orbit (here).</summary>
        private static float OrbitDiameterFor(string id, string kind, string planetType)
        {
            int circ = WorldConstants.CircumferenceFor(id, ClassOf(kind, planetType));
            return 8f + circ / 220f; // ~13 (asteroid) .. ~23 (moon) .. ~46-62 (planet)
        }

        /// <summary>Maps a NetBody's string kind + planet type to a size class (matches the server's
        /// <see cref="WorldConstants.SizeClassFor"/>).</summary>
        private static WorldConstants.WorldSizeClass ClassOf(string kind, string planetType)
            => string.Equals(planetType, "asteroid", System.StringComparison.OrdinalIgnoreCase) ? WorldConstants.WorldSizeClass.Asteroid
             : kind == "Moon" ? WorldConstants.WorldSizeClass.Moon
             : WorldConstants.WorldSizeClass.Planet;

        /// <summary>Spawns one real celestial body: a sphere textured with its REAL generated world map
        /// (seas, ground, this world's vegetation hue — what you see from orbit IS the world you land on),
        /// plus a per-type cloud shell and an atmosphere haze rim scaled by atmosphere density.
        /// <paramref name="bodyId"/> keys the body's true circumference; <paramref name="locationName"/>
        /// seeds the per-planet flora hue.</summary>
        private void SpawnBody(string name, string bodyId, string kind, string locationName, Vector3 pos, float diameter, string planetType)
        {
            // B37 rest: planets + cloud shells in the orbit view are lit by THIS system's star, so under a
            // red sun the whole system reads warm (a light wash — the biome tint stays recognisable).
            float sm = Mathf.Max(_sunColor.r, Mathf.Max(_sunColor.g, _sunColor.b));
            Color sunHue = sm > 0.001f ? new Color(_sunColor.r / sm, _sunColor.g / sm, _sunColor.b / sm) : Color.white;

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = name;
            StripCollider(sphere);
            sphere.transform.SetParent(_root.transform, false);
            sphere.transform.localPosition = pos;
            sphere.transform.localScale = Vector3.one * diameter;

            var planet = Game?.Content?.GetPlanet(planetType ?? string.Empty);
            if (planet != null && Game != null)
            {
                int circ = WorldConstants.CircumferenceFor(
                    string.IsNullOrEmpty(bodyId) ? locationName ?? "home" : bodyId, ClassOf(kind, planetType));
                var baked = WorldMinimap.Bake(Game.Content, Game.Atlas, Game.WorldSeed, locationName, planetType, circ, 96, 48);
                Color washTint = Color.Lerp(Color.white, sunHue, 0.35f);
                sphere.GetComponent<Renderer>().sharedMaterial = Lit(washTint, baked, new Vector2(1f, 1f));
            }
            else
            {
                var look = PlanetLookFor(planetType, locationName);
                Color bodyTint = Color.Lerp(look.tint, look.tint * sunHue, 0.35f);
                sphere.GetComponent<Renderer>().sharedMaterial = Lit(bodyTint, LoadTex(look.tex), new Vector2(3f, 2f));
            }

            var (cloudCol, cloudDen) = PlanetCloudLook(planetType);
            AddCloudShell(sphere.transform, Color.Lerp(cloudCol, cloudCol * sunHue, 0.35f), cloudDen);

            // Atmosphere haze: a thin translucent shell over everything — a breathable atmosphere reads
            // as a denser, bluer glow than a toxic one; airless bodies stay crisp bare rock.
            if (planet != null && !planet.IsAirless)
            {
                float atm = string.Equals(planet.Atmosphere, "breathable", System.StringComparison.OrdinalIgnoreCase) ? 1f : 0.7f;
                var haze = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                haze.name = "Atmosphere";
                StripCollider(haze);
                haze.transform.SetParent(sphere.transform, false);
                haze.transform.localScale = Vector3.one * 1.06f;
                var hShader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
                var hCol = Color.Lerp(new Color(0.55f, 0.75f, 1f), sunHue, 0.25f);
                var hMat = new Material(hShader) { mainTexture = Texture2D.whiteTexture, renderQueue = 2999 };
                hMat.SetColor("_Color", ShaderColor.Srgb(new Color(hCol.r, hCol.g, hCol.b, 0.08f + 0.08f * atm)));
                var hMr = haze.GetComponent<Renderer>();
                hMr.sharedMaterial = hMat;
                hMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                hMr.receiveShadows = false;
            }
        }

        private GameObject BuildShip(Transform parent)
        {
            // item 20 S1: if the server sent the ship's voxel design, render it as a real 1:1 voxel mesh of the
            // player's editor design instead of the hand-built cube silhouette below.
            var design = Game?.ShipDesign;
            _builtDesign = design;
            if (design != null && design.Block != null && design.Block.Length > 0)
            {
                return BuildVoxelShip(parent, design);
            }

            // Cube fallback (no design yet): no voxel grid to collide/build against.
            _shipCells = null;
            _shipVox = null;
            _shipStructureId = null;

            var ship = new GameObject("Ship");
            ship.transform.SetParent(parent, false);

            // Same block textures as the ship you walk on a planet: iron_wall hull, glass canopy, carbon
            // engine nozzles (the station model already uses these), so it reads as a real hull, not a flat cube.
            // The hull tint is the player's chosen hull colour (item 32; default = the old steel tint).
            var hull = LitTex("iron_wall", Rgb(Game.HullRgb));
            _hullMat = hull;
            _appliedHullRgb = Game.HullRgb;
            var glass = LitTex("glass", new Color(0.7f, 0.9f, 1f));
            var engine = LitTex("carbon", new Color(0.78f, 0.78f, 0.82f));

            Cube("Body", ship.transform, new Vector3(0f, 0f, 0f), new Vector3(1.6f, 0.9f, 3.4f), hull);
            Cube("WingL", ship.transform, new Vector3(-1.3f, 0f, -0.3f), new Vector3(1.2f, 0.2f, 1.4f), hull);
            Cube("WingR", ship.transform, new Vector3(1.3f, 0f, -0.3f), new Vector3(1.2f, 0.2f, 1.4f), hull);
            Cube("Cockpit", ship.transform, new Vector3(0f, 0.5f, 1.2f), new Vector3(0.9f, 0.6f, 1.0f), glass);
            Cube("Engine", ship.transform, new Vector3(0f, 0f, -1.9f), new Vector3(1.0f, 0.7f, 0.5f), engine);

            // Navigation lights at the wingtips (port red / starboard green), like the landed ship.
            Cube("NavL", ship.transform, new Vector3(-1.85f, 0f, -0.3f), new Vector3(0.22f, 0.22f, 0.22f), Unlit(new Color(1f, 0.25f, 0.2f)));
            Cube("NavR", ship.transform, new Vector3(1.85f, 0f, -0.3f), new Vector3(0.22f, 0.22f, 0.22f), Unlit(new Color(0.3f, 1f, 0.3f)));

            // Entry hatch on the tail (where the voxel hatch is) — a glowing cyan frame so you can find where
            // to board back in. It pulses brightly while you're on an EVA (see the hatch pulse in LateUpdate).
            _hatchMat = Unlit(new Color(0.2f, 0.85f, 1f));
            Cube("Hatch", ship.transform, new Vector3(0f, -0.32f, -1.6f), new Vector3(0.95f, 0.5f, 0.18f), _hatchMat);

            // Glowing thruster exhaust (stretches with throttle in Update).
            var ex = Cube("Exhaust", ship.transform, new Vector3(0f, 0f, -2.4f), new Vector3(0.6f, 0.6f, 1f), Unlit(new Color(0.6f, 0.85f, 1f)));
            _exhaust = ex.transform;
            return ship;
        }

        /// <summary>Builds the player's ship as a 1:1 voxel mesh from the server's design (item 20 S1): meshes the
        /// sparse block grid with the same <see cref="ChunkMesher"/> + block atlas the planet world uses, centred on
        /// the ship pivot so it flies + rotates like the old cube model. Keeps the glowing tail hatch marker +
        /// thruster exhaust so the EVA/throttle FX still work. The hull colour (item 32) is painted into the mesh's
        /// tint stream (see <see cref="ShipMeshBuilder.HullPaint"/>) — _hullMat stays null (its use sites are
        /// null-guarded); a live colour change re-meshes the ship voxels instead.</summary>
        private GameObject BuildVoxelShip(Transform parent, BlocksBeyondTheStars.Networking.Messages.SpaceShipDesign d)
        {
            var ship = new GameObject("Ship");
            ship.transform.SetParent(parent, false);

            // Index the cells (kept client-side for EVA collision + build/mine aim, S2) and find the bounds.
            var cells = new Dictionary<Vector3i, BlockId>(d.Block.Length);
            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
            for (int i = 0; i < d.Block.Length; i++)
            {
                int bx = d.X[i], by = d.Y[i], bz = d.Z[i];
                cells[new Vector3i(bx, by, bz)] = new BlockId(d.Block[i]);
                if (bx < minX) minX = bx; if (by < minY) minY = by; if (bz < minZ) minZ = bz;
                if (bx > maxX) maxX = bx; if (by > maxY) maxY = by; if (bz > maxZ) maxZ = bz;
            }

            // Centre the design on the ship pivot (so it rotates about its middle, like the old model). Front = +Z.
            _shipCells = cells;
            _shipCentre = new Vector3((minX + maxX + 1) * 0.5f, (minY + maxY + 1) * 0.5f, (minZ + maxZ + 1) * 0.5f);
            _shipStructureId = d.Id;

            // A child container holds the voxel chunk meshes so an EVA block edit (S2) can rebuild just the voxels.
            _shipVox = new GameObject("Vox").transform;
            _shipVox.SetParent(ship.transform, false);
            RebuildShipVoxels();

            // Tail FX, placed just behind the hull's rear (-Z) face so the EVA hatch pulse + throttle exhaust
            // still read on the voxel ship. Local Z of the rear face after centring:
            float rearZ = minZ - _shipCentre.z;
            float lowY = minY - _shipCentre.y + 0.5f;
            _hatchMat = Unlit(new Color(0.2f, 0.85f, 1f));
            Cube("Hatch", ship.transform, new Vector3(0f, lowY, rearZ + 0.1f), new Vector3(1.0f, 0.6f, 0.25f), _hatchMat);
            var ex = Cube("Exhaust", ship.transform, new Vector3(0f, 0f, rearZ - 0.6f), new Vector3(0.6f, 0.6f, 1f), Unlit(new Color(0.6f, 0.85f, 1f)));
            _exhaust = ex.transform;
            return ship;
        }

        /// <summary>(Re)builds the ship's voxel chunk meshes + colliders from <see cref="_shipCells"/> (item 20
        /// S1/S2). Called on first build and after each EVA block edit. The pivot centre stays fixed so the ship
        /// never jumps when you add/remove a block.</summary>
        private void RebuildShipVoxels()
        {
            if (_shipVox != null)
            {
                BuildVoxChunks(_shipVox, _shipCells, _shipCentre,
                    ShipMeshBuilder.HullPaint(Game.Content, Rgb(Game.HullRgb))); // hull paint (item 32)
                _appliedHullRgb = Game.HullRgb;
            }
        }

        /// <summary>Clears <paramref name="parent"/> and (re)builds voxel chunk meshes + colliders from a sparse
        /// block grid, centred on <paramref name="centre"/> — shared by the ship (S1/S2, with the hull paint
        /// resolver) and asteroids (S3, unpainted).</summary>
        private void BuildVoxChunks(Transform parent, Dictionary<Vector3i, BlockId> cells, Vector3 centre,
            System.Func<BlockId, Color> paint = null)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Destroy(parent.GetChild(i).gameObject);
            }

            if (cells == null || cells.Count == 0)
            {
                return;
            }

            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
            foreach (var c in cells.Keys)
            {
                if (c.X < minX) minX = c.X; if (c.Y < minY) minY = c.Y; if (c.Z < minZ) minZ = c.Z;
                if (c.X > maxX) maxX = c.X; if (c.Y > maxY) maxY = c.Y; if (c.Z > maxZ) maxZ = c.Z;
            }

            BlockId WorldBlock(int x, int y, int z) => cells.TryGetValue(new Vector3i(x, y, z), out var b) ? b : BlockId.Air;

            int cs = WorldConstants.ChunkSize;
            int FloorDiv(int a, int b) => (a >= 0 ? a : a - (b - 1)) / b;
            var mats = Game.ChunkMaterialTransparent != null
                ? new[] { Game.ChunkMaterial, Game.ChunkMaterialTransparent }
                : new[] { Game.ChunkMaterial };

            for (int cx = FloorDiv(minX, cs); cx <= FloorDiv(maxX, cs); cx++)
            for (int cy = FloorDiv(minY, cs); cy <= FloorDiv(maxY, cs); cy++)
            for (int cz = FloorDiv(minZ, cs); cz <= FloorDiv(maxZ, cs); cz++)
            {
                var coord = new ChunkCoord(cx, cy, cz);
                var origin = WorldConstants.ChunkOrigin(coord);
                var chunk = new ChunkData(coord);
                for (int lx = 0; lx < cs; lx++)
                for (int ly = 0; ly < cs; ly++)
                for (int lz = 0; lz < cs; lz++)
                {
                    var b = WorldBlock(origin.X + lx, origin.Y + ly, origin.Z + lz);
                    if (!b.IsAir)
                    {
                        chunk.Set(lx, ly, lz, b);
                    }
                }

                var (mesh, collider) = ChunkMesher.Build(chunk, Game.Content, WorldBlock, Game.Atlas, paintTint: paint);
                if (mesh.vertexCount == 0)
                {
                    continue;
                }

                var go = new GameObject($"VoxChunk {cx},{cy},{cz}");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = new Vector3(origin.X, origin.Y, origin.Z) - centre;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = mats;
                go.AddComponent<MeshCollider>().sharedMesh = collider; // S2/S3: hull/ore collision (suit + future)
            }
        }

        /// <summary>Builds a cell dict + its centre from a structure design message (shared by ship + asteroid).</summary>
        private static Dictionary<Vector3i, BlockId> CellsFromDesign(BlocksBeyondTheStars.Networking.Messages.SpaceShipDesign d, out Vector3 centre)
        {
            var cells = new Dictionary<Vector3i, BlockId>(d.Block.Length);
            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
            for (int i = 0; i < d.Block.Length; i++)
            {
                int bx = d.X[i], by = d.Y[i], bz = d.Z[i];
                cells[new Vector3i(bx, by, bz)] = new BlockId(d.Block[i]);
                if (bx < minX) minX = bx; if (by < minY) minY = by; if (bz < minZ) minZ = bz;
                if (bx > maxX) maxX = bx; if (by > maxY) maxY = by; if (bz > maxZ) maxZ = bz;
            }

            centre = cells.Count == 0 ? Vector3.zero
                : new Vector3((minX + maxX + 1) * 0.5f, (minY + maxY + 1) * 0.5f, (minZ + maxZ + 1) * 0.5f);
            return cells;
        }

        /// <summary>Rebuilds the ship GameObject from the latest design while preserving its current pose (item 20
        /// S1 — the design message lands a frame or two after entering space).</summary>
        private void RebuildShipModel()
        {
            var pos = _ship != null ? _ship.transform.localPosition : Vector3.zero;
            var rot = _ship != null ? _ship.transform.localRotation : Quaternion.identity;
            if (_ship != null)
            {
                Destroy(_ship);
            }

            _exhaust = null;
            _hatchMat = null;
            _hullMat = null;
            _appliedHullRgb = -1;
            _shipVox = null;      // destroyed with the old ship; BuildShip makes a fresh one
            _ship = BuildShip(_root.transform);
            _ship.transform.localPosition = pos;
            _ship.transform.localRotation = rot;
        }

        /// <summary>Lit material textured with a bundled block texture (white-ish tint so the texture reads).</summary>
        private static Material LitTex(string texKey, Color tint, float tile = 2f) => Lit(tint, LoadTex(texKey), new Vector2(tile, tile));

        /// <summary>A real station model: an iron hull hub with a glass viewport collar, docking arms with end
        /// pods, solar wings and a beacon mast — textured (iron/carbon/glass), not a flat cube.</summary>
        private GameObject BuildStationModel(Transform parent)
        {
            var root = new GameObject("Station");
            root.transform.SetParent(parent, false);
            var hull = LitTex("iron_wall", new Color(0.82f, 0.84f, 0.88f));
            var dark = LitTex("carbon", new Color(0.72f, 0.72f, 0.76f));
            var glass = LitTex("glass", new Color(0.7f, 0.9f, 1f));
            var panel = Unlit(new Color(0.18f, 0.34f, 0.68f)); // solar cells
            var light = Unlit(new Color(1f, 0.85f, 0.4f));

            Cube("Hub", root.transform, Vector3.zero, new Vector3(4f, 4f, 4f), hull);
            Cube("Collar", root.transform, Vector3.zero, new Vector3(5.6f, 1f, 5.6f), dark);
            Cube("Viewband", root.transform, new Vector3(0f, 0.5f, 0f), new Vector3(4.2f, 1f, 4.2f), glass);
            Cube("ArmX", root.transform, Vector3.zero, new Vector3(11f, 0.8f, 0.8f), dark);
            Cube("ArmZ", root.transform, Vector3.zero, new Vector3(0.8f, 0.8f, 11f), dark);
            Cube("PodXp", root.transform, new Vector3(5f, 0f, 0f), new Vector3(1.8f, 1.8f, 1.8f), hull);
            Cube("PodXn", root.transform, new Vector3(-5f, 0f, 0f), new Vector3(1.8f, 1.8f, 1.8f), hull);
            Cube("PodZp", root.transform, new Vector3(0f, 0f, 5f), new Vector3(1.8f, 1.8f, 1.8f), hull);
            Cube("PodZn", root.transform, new Vector3(0f, 0f, -5f), new Vector3(1.8f, 1.8f, 1.8f), hull);
            Cube("SolarL", root.transform, new Vector3(-7.4f, 0f, 0f), new Vector3(3.2f, 0.15f, 5.2f), panel);
            Cube("SolarR", root.transform, new Vector3(7.4f, 0f, 0f), new Vector3(3.2f, 0.15f, 5.2f), panel);
            Cube("Mast", root.transform, new Vector3(0f, 2.8f, 0f), new Vector3(0.4f, 2.6f, 0.4f), dark);
            Cube("Beacon", root.transform, new Vector3(0f, 4.4f, 0f), new Vector3(0.7f, 0.7f, 0.7f), light);
            root.AddComponent<Spin>().Configure(Vector3.up, 3f); // a slow, stately rotation
            return root;
        }

        /// <summary>A real drone model: an angular carbon body with a glowing red sensor eye and side pods.</summary>
        private GameObject BuildDroneModel(Transform parent)
        {
            var root = new GameObject("Drone");
            root.transform.SetParent(parent, false);
            var body = LitTex("carbon", new Color(0.55f, 0.5f, 0.58f));
            var trim = LitTex("iron_wall", new Color(0.6f, 0.5f, 0.55f));

            Cube("Core", root.transform, Vector3.zero, new Vector3(1.3f, 0.9f, 1.3f), body);
            Cube("Eye", root.transform, new Vector3(0f, 0f, 0.8f), new Vector3(0.45f, 0.45f, 0.4f), Unlit(new Color(1f, 0.25f, 0.2f)));
            Cube("PodL", root.transform, new Vector3(-0.9f, 0f, -0.1f), new Vector3(0.4f, 0.4f, 1f), trim);
            Cube("PodR", root.transform, new Vector3(0.9f, 0f, -0.1f), new Vector3(0.4f, 0.4f, 1f), trim);
            Cube("Fin", root.transform, new Vector3(0f, 0.7f, -0.3f), new Vector3(0.2f, 0.7f, 0.8f), trim);
            root.AddComponent<Spin>().Configure(Vector3.up, 18f); // restless hover spin
            return root;
        }

        /// <summary>A real UFO model: a flattened metal saucer with a glass dome and glowing underside lights.</summary>
        private GameObject BuildUfoModel(Transform parent)
        {
            var root = new GameObject("Ufo");
            root.transform.SetParent(parent, false);

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Saucer";
            StripCollider(disc);
            disc.transform.SetParent(root.transform, false);
            disc.transform.localScale = new Vector3(2.6f, 0.22f, 2.6f); // wide + flat
            disc.GetComponent<Renderer>().sharedMaterial = LitTex("titanium_ore", new Color(0.8f, 0.82f, 0.88f), 1.5f);

            var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "Dome";
            StripCollider(dome);
            dome.transform.SetParent(root.transform, false);
            dome.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            dome.transform.localScale = new Vector3(1.3f, 0.9f, 1.3f);
            dome.GetComponent<Renderer>().sharedMaterial = LitTex("glass", new Color(0.6f, 0.95f, 0.8f));

            Cube("LightF", root.transform, new Vector3(0f, -0.2f, 1f), new Vector3(0.3f, 0.2f, 0.3f), Unlit(new Color(0.6f, 1f, 0.7f)));
            Cube("LightB", root.transform, new Vector3(0f, -0.2f, -1f), new Vector3(0.3f, 0.2f, 0.3f), Unlit(new Color(0.6f, 1f, 0.7f)));
            Cube("LightL", root.transform, new Vector3(-1f, -0.2f, 0f), new Vector3(0.3f, 0.2f, 0.3f), Unlit(new Color(0.6f, 1f, 0.7f)));
            Cube("LightR", root.transform, new Vector3(1f, -0.2f, 0f), new Vector3(0.3f, 0.2f, 0.3f), Unlit(new Color(0.6f, 1f, 0.7f)));
            root.AddComponent<Spin>().Configure(Vector3.up, 40f); // a fast saucer spin
            return root;
        }

        /// <summary>A real cruiser model: an elongated iron hull with a glass bridge and twin glowing engines.</summary>
        private GameObject BuildCruiserModel(Transform parent)
        {
            var root = new GameObject("Cruiser");
            root.transform.SetParent(parent, false);
            var hull = LitTex("iron_wall", new Color(0.7f, 0.55f, 0.55f));
            var dark = LitTex("carbon", new Color(0.6f, 0.5f, 0.5f));

            Cube("Hull", root.transform, Vector3.zero, new Vector3(2f, 1.2f, 5f), hull);
            Cube("Spine", root.transform, new Vector3(0f, 0.8f, -0.5f), new Vector3(0.8f, 0.6f, 3f), dark);
            Cube("Bridge", root.transform, new Vector3(0f, 0.7f, 1.8f), new Vector3(1f, 0.7f, 1.2f), LitTex("glass", new Color(0.6f, 0.8f, 1f)));
            Cube("EngineL", root.transform, new Vector3(-0.7f, 0f, -2.6f), new Vector3(0.7f, 0.7f, 0.9f), dark);
            Cube("EngineR", root.transform, new Vector3(0.7f, 0f, -2.6f), new Vector3(0.7f, 0.7f, 0.9f), dark);
            Cube("GlowL", root.transform, new Vector3(-0.7f, 0f, -3.1f), new Vector3(0.4f, 0.4f, 0.3f), Unlit(new Color(1f, 0.5f, 0.3f)));
            Cube("GlowR", root.transform, new Vector3(0.7f, 0f, -3.1f), new Vector3(0.4f, 0.4f, 0.3f), Unlit(new Color(1f, 0.5f, 0.3f)));
            return root;
        }

        /// <summary>Builds the system's star far off in a fixed direction as stacked additive billboards: a
        /// coloured corona (the star tint) with a white-hot core on top, so even a deep-red or blue star
        /// reads as a real bright sun disc you can fly toward — not a vague coloured wash.</summary>
        private void BuildSun()
        {
            _sunColor = Game?.Environment != null ? Rgb(Game.Environment.SunColor) : new Color(1f, 0.96f, 0.88f);

            var go = new GameObject("Sun");
            go.transform.SetParent(_root.transform, false);
            _sun = go.transform;

            _sunMat = MakeSunLayer(_sun, _sunColor, 320f);                          // outer corona (star colour)
            MakeSunLayer(_sun, Color.Lerp(_sunColor, Color.white, 0.85f), 150f);    // hot near-white core
            MakeSunLayer(_sun, Color.white, 64f);                                   // blazing white centre
        }

        private Material MakeSunLayer(Transform parent, Color color, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "SunLayer";
            StripCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * size;

            var shader = Shader.Find("BlocksBeyondTheStars/SunGlow") ?? Shader.Find("Unlit/Color");
            var mat = new Material(shader) { mainTexture = GenerateGlowTexture() };
            mat.SetColor("_Color", ShaderColor.Srgb(color));

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mat;
        }

        /// <summary>Keeps the sun at a fixed direction + far distance from the camera (so it stays at the
        /// "horizon" of space) and faces it square-on as a billboard (its layers keep their own sizes).</summary>
        private void BillboardSun()
        {
            if (_sun == null || Camera == null)
            {
                return;
            }

            _sun.localPosition = Camera.transform.localPosition + SunDir * 1500f;
            _sun.rotation = Quaternion.LookRotation(_sun.position - Camera.transform.position, Vector3.up);
            _sun.localScale = Vector3.one;
        }

        /// <summary>A soft radial glow (bright core → transparent rim) for the sun billboard + flare sprites.</summary>
        private static Texture2D GenerateGlowTexture()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Clamp01(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c);
                float core = Mathf.Clamp01(1f - d * 4f);
                float halo = Mathf.Pow(Mathf.Clamp01(1f - d), 2.5f);
                px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(core * 0.85f + halo * 0.6f));
            }

            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>Draws the other players in this space instance — a ship for a pilot, a small suit for an
        /// EVA — from the server's per-player poses (R2). Pooled by player id; stale ones are removed.</summary>
        private void SyncRemotePlayers()
        {
            _remoteSeen.Clear();
            var players = Game.Space?.Players;
            if (players != null && _root != null)
            {
                foreach (var rp in players)
                {
                    if (string.IsNullOrEmpty(rp.PlayerId))
                    {
                        continue;
                    }

                    _remoteSeen.Add(rp.PlayerId);
                    if (!_remotePlayers.TryGetValue(rp.PlayerId, out var av) || av.Root == null)
                    {
                        av = BuildRemoteAvatar();
                        _remotePlayers[rp.PlayerId] = av;
                    }

                    av.Root.transform.localPosition = new Vector3(rp.X, rp.Y, rp.Z);
                    av.Root.transform.localRotation = Quaternion.Euler(0f, rp.Yaw, 0f);

                    // Upgrade the generic hull to the pilot's REAL voxel ship once its design arrived
                    // (the server cross-sends every instance member's design as "ship_remote"), and re-mesh
                    // it when the pilot repaints the hull mid-flight (the paint lives in the mesh, item 32).
                    int hullRgb = rp.Hull != 0 ? rp.Hull : 0xD1D6E0;
                    if ((!av.Voxel || av.HullRgb != hullRgb)
                        && Game.RemoteShipDesignFor(rp.PlayerId) is { } rd && ShipMeshBuilder.HasDesign(rd))
                    {
                        var vox = ShipMeshBuilder.BuildVoxelShip(Game, av.Root.transform, rd, out _, Rgb(hullRgb));
                        if (vox != null)
                        {
                            Destroy(av.Ship);
                            vox.transform.localScale = Vector3.one * FlightShipScale; // same compact flight scale as the own ship
                            av.Ship = vox;
                            av.HullMat = null; // a voxel ship carries its real block textures — paint is meshed in
                            av.Voxel = true;
                            av.HullRgb = hullRgb;
                        }
                    }

                    av.Ship.SetActive(!rp.Eva);
                    av.Suit.SetActive(rp.Eva);
                    if (av.HullMat != null)
                    {
                        av.HullMat.color = ShaderColor.Srgb(Rgb(hullRgb)); // their chosen hull colour (item 32)
                    }
                }
            }

            if (_remotePlayers.Count > _remoteSeen.Count)
            {
                _remoteRemove.Clear();
                foreach (var kv in _remotePlayers)
                {
                    if (!_remoteSeen.Contains(kv.Key))
                    {
                        _remoteRemove.Add(kv.Key);
                    }
                }

                foreach (var id in _remoteRemove)
                {
                    if (_remotePlayers[id].Root != null)
                    {
                        Destroy(_remotePlayers[id].Root);
                    }

                    _remotePlayers.Remove(id);
                }
            }
        }

        private RemoteAvatar BuildRemoteAvatar()
        {
            var root = new GameObject("RemotePlayer");
            root.transform.SetParent(_root.transform, false);

            // Ship: a small textured hull + glass cockpit (same block textures as your own ship), so other
            // players read as real ships out here rather than flat cubes.
            var ship = new GameObject("Ship");
            ship.transform.SetParent(root.transform, false);
            var hullMat = LitTex("iron_wall", new Color(0.82f, 0.84f, 0.88f)); // re-tinted per-player in SyncRemotePlayers (item 32)
            Cube("Body", ship.transform, Vector3.zero, new Vector3(1.6f, 0.9f, 3.4f), hullMat);
            Cube("Cockpit", ship.transform, new Vector3(0f, 0.45f, 1.1f), new Vector3(0.85f, 0.55f, 0.95f), LitTex("glass", new Color(0.7f, 0.9f, 1f)));

            var suit = Cube("Suit", root.transform, Vector3.zero, new Vector3(0.55f, 0.9f, 0.55f), Unlit(new Color(1f, 0.8f, 0.45f)));
            suit.SetActive(false);
            return new RemoteAvatar { Root = root, Ship = ship, Suit = suit, HullMat = hullMat };
        }

        private void SyncEntities()
        {
            var space = Game.Space;
            var seen = new HashSet<string>();
            if (space != null)
            {
                foreach (var e in space.Entities)
                {
                    seen.Add(e.Id);

                    // item 20 S3: asteroids render as voxel ore structures (see ReconcileStructs), not cube
                    // entities — but they stay in Space.Entities so the ship's weapons can still target them.
                    if (e.Kind == "Asteroid")
                    {
                        continue;
                    }

                    if (!_entities.TryGetValue(e.Id, out var go))
                    {
                        // Real textured multi-cube models (mirrors the ship) instead of a flat colour cube.
                        go = e.Kind switch
                        {
                            "SpaceStation" => BuildStationModel(_root.transform),
                            "Drone" => BuildDroneModel(_root.transform),
                            "Ufo" => BuildUfoModel(_root.transform),
                            "Cruiser" => BuildCruiserModel(_root.transform),
                            _ => Cube("Entity", _root.transform, Vector3.zero, EntityScale(e.Kind), Unlit(EntityColor(e.Kind))), // ResourceDrop etc.
                        };

                        // Stations scale by size tier (server-sent) — a colossal hull dwarfs a small one.
                        if (e.Kind == "SpaceStation" && e.Scale > 0.01f)
                        {
                            go.transform.localScale = Vector3.one * e.Scale;
                        }

                        _entities[e.Id] = go;
                    }

                    go.transform.localPosition = new Vector3(e.X, e.Y, e.Z); // rotation is driven by Spin
                    if (e.Kind == "ResourceDrop")
                    {
                        _dropIds.Add(e.Id);
                    }
                }
            }

            if (_entities.Count > seen.Count)
            {
                var stale = new List<string>();
                foreach (var id in _entities.Keys)
                {
                    if (!seen.Contains(id))
                    {
                        stale.Add(id);
                    }
                }

                foreach (var id in stale)
                {
                    // A resource drop that vanished close to the ship was tractored in → beam it.
                    if (_dropIds.Remove(id) && _ship != null)
                    {
                        var dropPos = _entities[id].transform.localPosition;
                        if ((dropPos - _ship.transform.localPosition).sqrMagnitude < 14f * 14f)
                        {
                            SpawnTractorBeam(_ship.transform.localPosition, dropPos);
                            _cargoFlash = 1f;
                        }
                    }

                    Destroy(_entities[id]);
                    _entities.Remove(id);
                }
            }
        }

        private void SpawnTractorBeam(Vector3 from, Vector3 to)
        {
            var go = Cube("TractorBeam", _root.transform, Vector3.zero, Vector3.one, Unlit(new Color(0.4f, 0.85f, 1f)));
            var mid = (from + to) * 0.5f;
            float len = Vector3.Distance(from, to);
            go.transform.localPosition = mid;
            go.transform.localRotation = len > 0.001f ? Quaternion.LookRotation(to - from) : Quaternion.identity;
            go.transform.localScale = new Vector3(0.18f, 0.18f, len);
            _beams.Add(new TractorBeam { Go = go, Life = 0.35f, Max = 0.35f });
        }

        private void UpdateBeams(float dt)
        {
            for (int i = _beams.Count - 1; i >= 0; i--)
            {
                var b = _beams[i];
                b.Life -= dt;
                if (b.Life <= 0f || b.Go == null)
                {
                    if (b.Go != null)
                    {
                        Destroy(b.Go);
                    }

                    _beams.RemoveAt(i);
                    continue;
                }

                float k = b.Life / b.Max; // taper the beam as it fades
                var s = b.Go.transform.localScale;
                b.Go.transform.localScale = new Vector3(0.18f * k, 0.18f * k, s.z);
            }
        }

        // ── Modern uGUI overlay (replaces the IMGUI fade + hint) ─────────────────────────────
        private Canvas _ui;
        private Image _fade;
        private Image _hit;
        private Image _crosshair;
        private Text _hint;
        private Text _board;
        private Text _cargo;
        private Text _instruments;         // flight readout: speed / throttle / heading (+ hull/shield)
        private Vector3 _instLastCamPos;
        private float _instSpeed;          // smoothed speed for a steady readout
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
        // Every body (landable + decorative) as a keep-out sphere: the ship slides along it instead of
        // flying into the planet, so there is no "fell into the planet / auto-landed" — you stop at the
        // approach distance and press E to land. (Pos, keep-out radius.)
        private readonly List<(Vector3 Pos, float Radius)> _keepOut = new();
        private string _landTargetId;   // body the ship is close enough to land on (null = the current world)
        private string _landTargetName;
        private float _landTargetSq;    // squared distance to the land target (for station-vs-body priority)
        private float _nearStationSq;   // squared distance to the near station
        private float _bounds = Bounds; // flight clamp, enlarged to span the resident system
        private const float SystemViewScale = 0.16f; // system units → flight-view units (kept compact so
                                                     // neighbouring planets are a short cruise apart, not minutes)
        private const float KeepOutMargin = 10f;     // how far outside a body's surface the ship is held
        private const float LandBand = 40f;          // land prompt shows within (body radius + margin + band)

        private void EnsureUi()
        {
            if (_ui != null)
            {
                return;
            }

            _ui = UiKit.CreateCanvas("Space View Overlay", UiKit.HudRefW, UiKit.HudRefH); // ~1.25× bigger flight HUD
            _ui.sortingOrder = 12; // above the space HUD, below menus

            // Full-screen launch/landing fade.
            var fadeGo = new GameObject("Fade", typeof(RectTransform));
            fadeGo.transform.SetParent(_ui.transform, false);
            var frt = fadeGo.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = frt.offsetMax = Vector2.zero;
            _fade = fadeGo.AddComponent<Image>();
            _fade.sprite = UiKit.SolidSprite;
            _fade.color = new Color(0f, 0f, 0f, 0f);
            _fade.raycastTarget = false;

            // Red damage flash on a hit/collision.
            var hitGo = new GameObject("Hit", typeof(RectTransform));
            hitGo.transform.SetParent(_ui.transform, false);
            var hrtt = hitGo.GetComponent<RectTransform>();
            hrtt.anchorMin = Vector2.zero;
            hrtt.anchorMax = Vector2.one;
            hrtt.offsetMin = hrtt.offsetMax = Vector2.zero;
            _hit = hitGo.AddComponent<Image>();
            _hit.sprite = UiKit.SolidSprite;
            _hit.color = new Color(1f, 0.2f, 0.2f, 0f);
            _hit.raycastTarget = false;

            // Bottom-centre cruise controls hint.
            var hintGo = new GameObject("Hint", typeof(RectTransform));
            hintGo.transform.SetParent(_ui.transform, false);
            var hrt = hintGo.GetComponent<RectTransform>();
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0f);
            hrt.pivot = new Vector2(0.5f, 0f);
            hrt.sizeDelta = new Vector2(560f, 24f);
            hrt.anchoredPosition = new Vector2(0f, 20f);
            _hint = hintGo.AddComponent<Text>();
            _hint.font = UiKit.Font;
            _hint.fontSize = 18;
            _hint.color = UiKit.TextCol;
            _hint.alignment = TextAnchor.MiddleCenter;
            _hint.horizontalOverflow = HorizontalWrapMode.Overflow;
            _hint.raycastTarget = false;

            // "Press E to board" prompt when near a station (centre, above the crosshair).
            var boardGo = new GameObject("BoardPrompt", typeof(RectTransform));
            boardGo.transform.SetParent(_ui.transform, false);
            var brt = boardGo.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(620f, 28f);
            brt.anchoredPosition = new Vector2(0f, -90f);
            _board = boardGo.AddComponent<Text>();
            _board.font = UiKit.Font;
            _board.fontSize = 22;
            _board.color = UiKit.Cyan;
            _board.alignment = TextAnchor.MiddleCenter;
            _board.fontStyle = FontStyle.Bold;
            _board.horizontalOverflow = HorizontalWrapMode.Overflow;
            _board.raycastTarget = false;
            _board.gameObject.SetActive(false);

            // Cargo readout (top-left), pulses when salvage is tractored in.
            var cargoGo = new GameObject("Cargo", typeof(RectTransform));
            cargoGo.transform.SetParent(_ui.transform, false);
            var crt = cargoGo.GetComponent<RectTransform>();
            crt.anchorMin = crt.anchorMax = new Vector2(0f, 1f);
            crt.pivot = new Vector2(0f, 1f);
            crt.sizeDelta = new Vector2(320f, 26f);
            crt.anchoredPosition = new Vector2(20f, -160f);
            _cargo = cargoGo.AddComponent<Text>();
            _cargo.font = UiKit.Font;
            _cargo.fontSize = 18;
            _cargo.color = UiKit.TextCol;
            _cargo.alignment = TextAnchor.MiddleLeft;
            _cargo.horizontalOverflow = HorizontalWrapMode.Overflow;
            _cargo.raycastTarget = false;

            // Flight instruments (bottom-left): smoothed speed, throttle, heading + hull/shield.
            // Avionics-style abbreviations (SPD/THR/HDG) — identical in DE and EN cockpits.
            var instGo = new GameObject("Instruments", typeof(RectTransform));
            instGo.transform.SetParent(_ui.transform, false);
            var insRt = instGo.GetComponent<RectTransform>();
            insRt.anchorMin = insRt.anchorMax = new Vector2(0f, 0f);
            insRt.pivot = new Vector2(0f, 0f);
            insRt.sizeDelta = new Vector2(620f, 26f);
            insRt.anchoredPosition = new Vector2(20f, 20f);
            _instruments = instGo.AddComponent<Text>();
            _instruments.font = UiKit.Font;
            _instruments.fontSize = 19;
            _instruments.color = UiKit.Cyan;
            _instruments.alignment = TextAnchor.MiddleLeft;
            _instruments.horizontalOverflow = HorizontalWrapMode.Overflow;
            _instruments.raycastTarget = false;

            // Aiming dot at screen centre — brightens cyan when the laser has a target locked.
            var chGo = new GameObject("Crosshair", typeof(RectTransform));
            chGo.transform.SetParent(_ui.transform, false);
            var chrt = chGo.GetComponent<RectTransform>();
            chrt.anchorMin = chrt.anchorMax = chrt.pivot = new Vector2(0.5f, 0.5f);
            chrt.sizeDelta = new Vector2(10f, 10f);
            chrt.anchoredPosition = Vector2.zero;
            _crosshair = chGo.AddComponent<Image>();
            _crosshair.sprite = UiKit.SolidSprite;
            _crosshair.color = new Color(0.6f, 0.7f, 0.8f, 0.35f);
            _crosshair.raycastTarget = false;

            // Ship-systems quick-bar, just above the controls hint (1–9 select, LMB use).
            var barGo = new GameObject("SystemsBar", typeof(RectTransform));
            barGo.transform.SetParent(_ui.transform, false);
            var brt2 = barGo.GetComponent<RectTransform>();
            brt2.anchorMin = brt2.anchorMax = new Vector2(0.5f, 0f);
            brt2.pivot = new Vector2(0.5f, 0f);
            brt2.sizeDelta = new Vector2(760f, 30f);
            brt2.anchoredPosition = new Vector2(0f, 52f);
            _systemsBar = barGo.AddComponent<Text>();
            _systemsBar.font = UiKit.Font;
            _systemsBar.fontSize = 19;
            _systemsBar.alignment = TextAnchor.MiddleCenter;
            _systemsBar.horizontalOverflow = HorizontalWrapMode.Overflow;
            _systemsBar.supportRichText = true;
            _systemsBar.raycastTarget = false;

            // A small content-styled icon of the selected system, centred just above the quick-bar.
            var icoGo = new GameObject("SystemIcon", typeof(RectTransform));
            icoGo.transform.SetParent(_ui.transform, false);
            var irt = icoGo.GetComponent<RectTransform>();
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0f);
            irt.pivot = new Vector2(0.5f, 0f);
            irt.sizeDelta = new Vector2(30f, 30f);
            irt.anchoredPosition = new Vector2(0f, 84f);
            _systemIcon = icoGo.AddComponent<Image>();
            _systemIcon.raycastTarget = false;
            _systemIcon.preserveAspect = true;
            _systemIcon.enabled = false;
        }

        /// <summary>Creates the chain of lens-flare sprites once (a big bloom at the sun + ghosts that march
        /// through the screen centre). Positioned + tinted each frame by <see cref="UpdateLensFlare"/>.</summary>
        private void EnsureFlare()
        {
            if (_flare.Count > 0 || _ui == null)
            {
                return;
            }

            if (_glowSprite == null)
            {
                var tex = GenerateGlowTexture();
                _glowSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }

            for (int i = 0; i < FlareT.Length; i++)
            {
                var go = new GameObject("Flare" + i, typeof(RectTransform));
                go.transform.SetParent(_ui.transform, false);
                var img = go.AddComponent<Image>();
                img.sprite = _glowSprite;
                img.raycastTarget = false;
                img.rectTransform.sizeDelta = new Vector2(FlareSize[i], FlareSize[i]);
                img.enabled = false;
                _flare.Add(img);
            }
        }

        /// <summary>Screen-space lens flare: a bloom on the sun plus ghost discs strung through the screen
        /// centre, brightening as you turn to look into the star. Hidden when the sun is off-screen/behind.</summary>
        private void UpdateLensFlare()
        {
            EnsureFlare();
            if (_sun == null || _flare.Count == 0)
            {
                return;
            }

            Vector3 sc = Camera.WorldToScreenPoint(_sun.position);
            bool onScreen = sc.z > 0f && sc.x >= 0f && sc.x <= Screen.width && sc.y >= 0f && sc.y <= Screen.height;
            if (!onScreen)
            {
                foreach (var g in _flare)
                {
                    g.enabled = false;
                }

                return;
            }

            var sun = new Vector2(sc.x, sc.y);
            var centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float maxD = new Vector2(Screen.width, Screen.height).magnitude * 0.5f;
            float look = Mathf.Clamp01(1f - Vector2.Distance(sun, centre) / maxD); // 1 = looking straight at it

            for (int i = 0; i < _flare.Count; i++)
            {
                var g = _flare[i];
                Vector2 p = sun + (centre - sun) * FlareT[i]; // ghosts ride the sun→centre axis
                g.rectTransform.position = new Vector3(p.x, p.y, 0f);
                // The sun bloom (i==0) is always visible on-screen; the ghost chain swells as you look at it.
                float a = i == 0 ? FlareAlpha[0] * (0.5f + 0.5f * look) : FlareAlpha[i] * Mathf.Pow(look, 1.4f);
                // Desaturate toward white so the flare reads as glare, not a coloured screen wash.
                var col = Color.Lerp(_sunColor, Color.white, 0.5f);
                col.a = a;
                g.color = col;
                g.enabled = a > 0.004f;
            }
        }

        private void LateUpdate()
        {
            if (!_active)
            {
                if (_ui != null && _ui.enabled)
                {
                    _ui.enabled = false;
                }

                return;
            }

            EnsureUi();
            _ui.enabled = true;

            // Entry-hatch beacon: a steady cyan glow normally, a strong pulse while on an EVA so you can find
            // your way back to the ship's hatch from a distance.
            if (_hatchMat != null)
            {
                float b = _eva ? 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.time * 4.5f)) : 0.7f;
                _hatchMat.color = ShaderColor.Srgb(new Color(0.15f * b + 0.05f, 0.85f * b, 1f * b));
            }

            // Decay the hit feedback.
            _shake = Mathf.Max(0f, _shake - Time.deltaTime * 2.5f);
            _hitFlash = Mathf.Max(0f, _hitFlash - Time.deltaTime * 2f);
            _cargoFlash = Mathf.Max(0f, _cargoFlash - Time.deltaTime * 1.5f);
            _hit.color = new Color(1f, 0.2f, 0.2f, _hitFlash * 0.35f);

            if (_phase != Phase.Cruise)
            {
                float dur = _phase == Phase.Boarding ? BoardDuration : SeqDuration;
                float t = Mathf.Clamp01(_seq / dur);
                float alpha = (_phase == Phase.Landing || _phase == Phase.Boarding) ? t : 1f - t;
                _fade.color = new Color(0f, 0f, 0f, alpha);
                _hint.gameObject.SetActive(false);
                _board.gameObject.SetActive(false);
                _cargo.gameObject.SetActive(false);
            }
            else if (_confirmLand)
            {
                // Landing-pad chooser (item 38): the planet map of pads is its own clickable overlay (ShowLandMap);
                // here just clear the fade and show a one-line hint while the pad list streams in.
                _fade.color = new Color(0f, 0f, 0f, 0f);
                var loc = Game.Localizer;
                var pads = Game.LandingPadsBody == _choosePadBody ? Game.LandingPads : null;
                _hint.text = pads == null
                    ? (loc != null ? loc.Get("ui.space.pad_loading") : "Reading landing pads…")
                    : string.Empty; // the map shows the choices
                _hint.gameObject.SetActive(pads == null);
                _board.gameObject.SetActive(false);
                _cargo.gameObject.SetActive(false);
            }
            else if (_eva)
            {
                // Spacewalk HUD: float controls, a board prompt for the ship/station, and the oxygen lifeline.
                _fade.color = new Color(0f, 0f, 0f, 0f);
                var loc = Game.Localizer;
                _hint.text = loc != null ? loc.Get("ui.space.eva_controls")
                    : "EVA — WASD/Mouse float · Space/Ctrl up/down · E board";
                _hint.gameObject.SetActive(true);

                bool stationCloser = _nearStationId != null && (!_evaNearShip || _nearStationSq <= _evaShipSq);
                if (stationCloser)
                {
                    string board = loc != null ? loc.Get("ui.space.board") : "Press E to board";
                    _board.text = $"{board} {_nearStationName}";
                }
                else if (_evaNearShip)
                {
                    _board.text = loc != null ? loc.Get("ui.space.eva_board_ship") : "Press E to board your ship";
                }
                else
                {
                    // Always guide the player home: show how far the parked ship is so they fly back and board it.
                    int dist = Mathf.RoundToInt(Mathf.Sqrt(_evaShipSq));
                    string toShip = loc != null ? loc.Get("ui.space.eva_to_ship") : "Fly to your ship";
                    _board.text = $"{toShip} — {dist} m";
                }

                _board.gameObject.SetActive(true);

                int o2 = Mathf.CeilToInt(Mathf.Clamp(Game.Oxygen, 0f, 999f));
                string oxy = loc != null ? loc.Get("ui.space.eva_oxygen") : "O₂";
                _cargo.text = $"{oxy}: {o2}%";
                _cargo.color = Game.Oxygen <= 25f
                    ? Color.Lerp(new Color(1f, 0.3f, 0.2f), Color.white, Mathf.PingPong(Time.time * 2f, 1f) * 0.6f)
                    : UiKit.TextCol;
                _cargo.gameObject.SetActive(true);
            }
            else
            {
                _fade.color = new Color(0f, 0f, 0f, 0f);
                var loc = Game.Localizer;
                _hint.text = loc != null ? loc.Get("ui.space.controls") : "WASD/Mouse fly · V view · E land/dock · L return · G EVA";
                if (Game.AiCoreTier >= 2)
                {
                    _hint.text += " · " + Loc("ui.vega.autopilot.hint", "[P] Autopilot");
                }

                _hint.gameObject.SetActive(true);

                // Prompt whichever you're closest to: dock a station (E) or land on a body (E) you've flown up to.
                bool haveStation = _nearStationId != null;
                bool haveBody = _landTargetId != null;
                bool stationCloser = haveStation && (!haveBody || _nearStationSq <= _landTargetSq);
                bool showStation = haveStation && stationCloser;
                bool showBody = haveBody && !stationCloser;
                if (showStation)
                {
                    string board = loc != null ? loc.Get("ui.space.board") : "Press E to board";
                    _board.text = $"{board} {_nearStationName}";
                }
                else if (showBody)
                {
                    string land = loc != null ? loc.Get("ui.space.land_prompt") : "Press E to land on";
                    _board.text = $"{land} {_landTargetName}";
                }

                _board.gameObject.SetActive(showStation || showBody);

                string cargoLabel = loc != null ? loc.Get("ui.space.cargo") : "Cargo";
                _cargo.text = $"{cargoLabel}: {Game.Cargo.Length}";
                _cargo.color = Color.Lerp(UiKit.TextCol, UiKit.Cyan, _cargoFlash);
                _cargo.gameObject.SetActive(true);
            }

            UpdateInstruments();

            // Lens flare only during free flight (hidden behind the launch/landing/boarding fades + EVA).
            if (_phase == Phase.Cruise && !_eva)
            {
                UpdateLensFlare();
            }
            else
            {
                foreach (var g in _flare)
                {
                    g.enabled = false;
                }
            }

            // Aiming dot: shown in free flight, cyan when the laser has a target locked.
            if (_crosshair != null)
            {
                bool show = _phase == Phase.Cruise && !_confirmLand && !_eva;
                _crosshair.enabled = show;
                if (show)
                {
                    _crosshair.color = _fireTargetId != null
                        ? new Color(0.5f, 1f, 1f, 0.9f)
                        : new Color(0.6f, 0.7f, 0.8f, 0.35f);
                }
            }

            // Ship-systems quick-bar text: the selected system is highlighted; numbers are the hotkeys.
            if (_systemsBar != null)
            {
                bool show = _phase == Phase.Cruise && !_confirmLand && !_eva && _systems.Count > 0;
                _systemsBar.enabled = show;
                if (show)
                {
                    var sb = new System.Text.StringBuilder();
                    for (int n = 0; n < _systems.Count; n++)
                    {
                        if (n > 0)
                        {
                            sb.Append("    ");
                        }

                        if (n == _selectedSystem)
                        {
                            sb.Append($"<color=#66ffff><b>[{n + 1} {_systems[n].Label}]</b></color>");
                        }
                        else
                        {
                            sb.Append($"<color=#8fa3b8>{n + 1} {_systems[n].Label}</color>");
                        }
                    }

                    _systemsBar.text = sb.ToString();
                }

                // A content-styled icon of the selected system (laser turret / tractor emitter).
                if (_systemIcon != null)
                {
                    var sys = show ? _systems[_selectedSystem] : default;
                    string iconKey = !show ? null : sys.Kind == "tractor" ? "tractor_beam" : sys.WeaponKey ?? FlightWeapon;
                    var sprite = iconKey != null ? IconResolver.Resolve(iconKey, Game) : null;
                    _systemIcon.enabled = show && sprite != null;
                    if (sprite != null)
                    {
                        _systemIcon.sprite = sprite;
                    }
                }
            }
        }

        /// <summary>Flight instruments: smoothed speed (camera delta — the camera rides the ship),
        /// throttle, heading, plus hull/shield while combat data is present. Shown in cruise only.</summary>
        private void UpdateInstruments()
        {
            if (_instruments == null)
            {
                return;
            }

            bool show = _phase == Phase.Cruise && !_eva && Camera != null;
            if (_instruments.gameObject.activeSelf != show)
            {
                _instruments.gameObject.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float raw = (Camera.transform.position - _instLastCamPos).magnitude / dt;
            _instLastCamPos = Camera.transform.position;
            _instSpeed = Mathf.Lerp(_instSpeed, Mathf.Min(raw, 999f), 0.15f);

            int thr = Mathf.RoundToInt(Mathf.Clamp01(Input.GetAxis("Vertical")) * 100f);
            int hdg = Mathf.RoundToInt(Mathf.Repeat(_yaw, 360f));
            string text = $"SPD {_instSpeed:0.0}   THR {thr}%   HDG {hdg:000}°";
            var combat = Game?.ShipCombat;
            if (combat != null)
            {
                text += $"   HULL {Mathf.CeilToInt(combat.Hull)}   SHD {Mathf.CeilToInt(combat.Shield)}";
            }

            _instruments.text = text;
        }

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
        }

        private static Vector3 EntityScale(string kind) => kind switch
        {
            "Asteroid" => Vector3.one * 2.4f,
            "Ufo" => new Vector3(2.4f, 0.7f, 2.4f),
            "Cruiser" => new Vector3(3f, 1.5f, 5f),
            "SpaceStation" => new Vector3(8f, 5f, 8f),
            "ResourceDrop" => Vector3.one * 0.7f,
            _ => Vector3.one * 1.1f,
        };

        private static Color EntityColor(string kind) => kind switch
        {
            "Asteroid" => new Color(0.45f, 0.42f, 0.38f),
            "Ufo" => new Color(0.6f, 0.35f, 0.8f),
            "Cruiser" => new Color(0.7f, 0.3f, 0.3f),
            "SpaceStation" => new Color(0.62f, 0.66f, 0.72f),
            "ResourceDrop" => new Color(0.5f, 0.9f, 1f),
            _ => new Color(0.85f, 0.2f, 0.2f),
        };

        private static GameObject Cube(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            StripCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }
        }

        private static Material Unlit(Color c)
        {
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque");
            return new Material(shader) { color = ShaderColor.Srgb(c) };
        }

        private Material _asteroidMat; // shared stone material for the field's asteroids (rebuilt per view)

        private Material AsteroidMat()
            => _asteroidMat ??= Lit(new Color(0.62f, 0.60f, 0.57f), LoadTex("asteroid_rock") ?? LoadTex("stone"), new Vector2(1.5f, 1.5f));

        /// <summary>Slowly tumbles an asteroid about a fixed random axis (purely cosmetic).</summary>
        private sealed class Spin : MonoBehaviour
        {
            private Vector3 _axis = Vector3.up;
            private float _speed = 12f;
            private bool _configured;

            /// <summary>Pins a fixed axis + speed (deg/s); otherwise a random tumble is chosen on Start.</summary>
            public void Configure(Vector3 axis, float speed)
            {
                _axis = axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.up;
                _speed = speed;
                _configured = true;
            }

            private void Start()
            {
                if (_configured)
                {
                    return;
                }

                _axis = Random.onUnitSphere;
                _speed = Random.Range(7f, 20f); // degrees / second
            }

            private void Update() => transform.Rotate(_axis, _speed * Time.deltaTime, Space.Self);
        }

        /// <summary>Lit material (fixed key light) with an optional tiled block texture.</summary>
        private static Material Lit(Color c, Texture2D tex = null, Vector2 tiling = default)
        {
            var shader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { color = ShaderColor.Srgb(c) };
            if (tex != null)
            {
                m.mainTexture = tex;
                if (tiling != default)
                {
                    m.mainTextureScale = tiling;
                }
            }

            return m;
        }

        /// <summary>Loads a bundled block texture (Resources/textures/&lt;key&gt;.bytes raw 64x64 RGBA32,
        /// via LoadRawTextureData from the core module — no ImageConversion dependency).</summary>
        private static Texture2D LoadTex(string key)
        {
            var asset = Resources.Load<TextAsset>("textures/" + key);
            if (asset == null || asset.bytes.Length != 64 * 64 * 4)
            {
                return null;
            }

            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
            };
            tex.LoadRawTextureData(asset.bytes);
            tex.Apply();
            return tex;
        }

        /// <summary>Maps a biome / planet-type key to a planet tint + a fitting block texture. Unknown
        /// keys get a stable hash-derived colour so every world type still looks distinct.</summary>
        /// <summary>Data-driven orbital look: sphere texture = the type's REAL surface block, tinted with a
        /// partial cast of the mixed orbit colour (ground + this world's own flora hue + water/lava) — so a
        /// mud flat reads brown and a lush world reads in ITS vegetation colour. Falls back to the legacy
        /// palette when the type is unknown (the old hardcoded map mis-coloured every newer planet type).</summary>
        private (Color tint, string tex) PlanetLookFor(string key, string locationName)
        {
            var legacy = PlanetLook(key);
            var planet = Game?.Content?.GetPlanet(key ?? string.Empty);
            if (planet == null)
            {
                return legacy;
            }

            Color ground = PlanetOrbitLook.GroundColor(
                Game.Content, Game.Atlas, Game.WorldSeed, locationName, key, legacy.tint);
            // Partial cast only: the surface texture already carries the ground colour — full
            // multiplication would double-saturate and darken the sphere.
            return (Color.Lerp(Color.white, ground, 0.55f), planet.SurfaceBlock);
        }

        private static (Color tint, string tex) PlanetLook(string key)
        {
            switch ((key ?? string.Empty).ToLowerInvariant())
            {
                case "jungle":
                case "forest": return (new Color(0.32f, 0.55f, 0.30f), "grass");
                case "desert": return (new Color(0.82f, 0.68f, 0.42f), "sand");
                case "ice":
                case "frozen": return (new Color(0.72f, 0.86f, 0.96f), "ice");
                case "lava":
                case "volcanic": return (new Color(0.78f, 0.32f, 0.18f), "lava");
                case "swamp": return (new Color(0.40f, 0.45f, 0.30f), "mud");
                case "crystal": return (new Color(0.55f, 0.65f, 0.92f), "crystal");
                case "ocean":
                case "water": return (new Color(0.24f, 0.46f, 0.72f), "water");
                case "barren":
                case "asteroid":
                case "rock": return (new Color(0.52f, 0.50f, 0.47f), "stone");
                default:
                    return (HashColor(key), "stone");
            }
        }

        private static Color HashColor(string key)
        {
            int h = 0;
            foreach (char c in key ?? string.Empty)
            {
                h = h * 31 + c;
            }

            var rng = new System.Random(h);
            return new Color(
                0.40f + 0.45f * (float)rng.NextDouble(),
                0.40f + 0.45f * (float)rng.NextDouble(),
                0.40f + 0.45f * (float)rng.NextDouble());
        }

        /// <summary>Per planet-type cloud cover seen from space (mirrors data/planets.json).</summary>
        private static (Color color, float density) PlanetCloudLook(string key)
        {
            switch ((key ?? string.Empty).ToLowerInvariant())
            {
                case "jungle":
                case "forest": return (Rgb(0xF2F4F6), 0.6f);
                case "desert": return (Rgb(0xE8D9B0), 0.3f);
                case "ice":
                case "frozen": return (Rgb(0xDCEAF5), 0.5f);
                case "lava":
                case "volcanic": return (Rgb(0x5A4A44), 0.7f);
                case "swamp": return (Rgb(0xC8CBC0), 0.75f);
                case "crystal": return (Rgb(0xE6D6F0), 0.4f);
                case "rocky":
                case "rock": return (Rgb(0xEDEFF2), 0.35f);
                default: return (Rgb(0xEDEFF2), 0f); // barren/asteroid → no clouds
            }
        }

        /// <summary>Adds a slowly-spinning, semi-transparent cloud shell over a planet sphere (the clouds
        /// you see from space). Density drives how much of the planet the cover hides.</summary>
        private void AddCloudShell(Transform planet, Color color, float density)
        {
            if (density <= 0.001f)
            {
                return;
            }

            var shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shell.name = "CloudShell";
            StripCollider(shell);
            shell.transform.SetParent(planet, false);
            shell.transform.localPosition = Vector3.zero;
            shell.transform.localScale = Vector3.one * 1.035f; // just above the surface

            var shader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
            var mat = new Material(shader) { mainTexture = CloudCoverTexture(density) };
            mat.renderQueue = 3000;
            var c = color;
            c.a = Mathf.Clamp01(0.55f + density * 0.4f);
            mat.SetColor(Shader.PropertyToID("_Color"), ShaderColor.Srgb(c));

            var mr = shell.GetComponent<Renderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _cloudShells.Add(shell.transform);
        }

        /// <summary>A wrapping cloud-cover tile: white patches with alpha gaps, coverage set by density.</summary>
        private static Texture2D CloudCoverTexture(float density)
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };

            // Sum a few sine bands (seamless across the wrap) into a soft field, then threshold by density.
            float threshold = Mathf.Lerp(0.85f, 0.30f, Mathf.Clamp01(density));
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n * Mathf.PI * 2f, v = y / (float)n * Mathf.PI * 2f;
                    float f = 0.5f
                        + 0.25f * Mathf.Sin(u * 3f + Mathf.Sin(v * 2f))
                        + 0.15f * Mathf.Sin(v * 4f + Mathf.Cos(u * 3f))
                        + 0.10f * Mathf.Sin((u + v) * 5f);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((f - threshold) * 3f));
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            }

            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        private static Color Rgb(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
