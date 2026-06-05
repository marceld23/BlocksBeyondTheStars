using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Spacecraft.Client
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

        private enum Phase { Launch, Cruise, Landing, Boarding }

        private GameObject _root;
        private GameObject _ship;
        private Transform _exhaust;
        private AudioSource _engine;
        private readonly Dictionary<string, GameObject> _entities = new Dictionary<string, GameObject>();
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

        private bool _confirmLand;        // L asks "land here?" — Enter confirms, Esc cancels
        private string _boardTargetId;    // station being boarded during the dock-approach animation
        private Vector3 _boardTargetPos, _boardStartPos;
        private bool _boardSent;          // the board intent was sent (now waiting for the server)
        private float _boardWait;         // safety timeout so a rejected board never leaves a black screen

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

            // Server says we left. A hyperspace jump (its full-screen warp covers the transition) or a
            // station board tears down immediately — no surface-landing descent. Otherwise fly the
            // landing sequence back down to the body we launched from, then tear down.
            if (!Game.InSpace)
            {
                if (_hyperjumping || _phase == Phase.Boarding)
                {
                    Exit();
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
            UpdateBeams(Time.deltaTime);

            switch (_phase)
            {
                case Phase.Launch: UpdateSequence(rising: true); break;
                case Phase.Landing: UpdateSequence(rising: false); break;
                case Phase.Boarding: UpdateBoarding(); break;
                default: UpdateCruise(); break;
            }

            PlaceCamera();
            BillboardSun();

            // Engine loop: swells in cruise, throttle nudges the pitch.
            if (_engine != null)
            {
                _engine.volume = Mathf.MoveTowards(_engine.volume, _phase == Phase.Cruise ? 0.25f : 0.12f, Time.deltaTime * 0.5f);
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

            // Landing asks for confirmation first so you don't drop to the surface by accident. The target
            // is the body you flew up to (if any) — otherwise you land back on the body you launched from.
            if (_confirmLand)
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    _confirmLand = false;
                    Game.Network?.SendLeaveSpace(string.Empty); // L always returns to the body you launched from
                }
                else if (Input.GetKeyDown(KeyCode.Escape))
                {
                    _confirmLand = false;
                }

                return; // hold position while the prompt is up
            }

            // L opens the "return to the body you launched from" confirmation (landing on a nearby body is E).
            if (Input.GetKeyDown(KeyCode.L))
            {
                _confirmLand = true;
                return;
            }

            _yaw += Input.GetAxis("Mouse X") * LookSpeed * _shipTurnMul;
            _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * LookSpeed * _shipTurnMul, -80f, 80f);
            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            _ship.transform.localRotation = rot;

            float fwd = Input.GetAxis("Vertical");
            float strafe = Input.GetAxis("Horizontal");
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
                    if (dist < StationKeepOut && dist > 0.0001f)
                    {
                        pos = sp + d / dist * StationKeepOut;
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

            // Report our position so the server runs authoritative collisions against asteroids/entities.
            _moveSendTimer -= Time.deltaTime;
            if (_moveSendTimer <= 0f)
            {
                _moveSendTimer = 0.08f; // ~12 Hz
                Game.Network?.SendShipMove(_ship.transform.localPosition);
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
                    Game.Network?.SendLeaveSpace(_landTargetId); // land on the nearby body
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
        private Spacecraft.Networking.Messages.NetCombatEntity BestFireTarget()
        {
            var space = Game.Space;
            if (space == null || _ship == null)
            {
                return null;
            }

            Vector3 shipPos = _ship.transform.localPosition;
            Vector3 fwd = _ship.transform.localRotation * Vector3.forward;
            Spacecraft.Networking.Messages.NetCombatEntity best = null;
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
        private void FireAt(Spacecraft.Networking.Messages.NetCombatEntity target, string weaponKey)
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
            _phase = Phase.Launch;
            _seq = 0f;
            _yaw = 0f;
            _pitch = 0f;
            _confirmLand = false;
            _boardSent = false;
            _hyperjumping = false;
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

            // Launch roar + a looping engine bed for the flight.
            ClientAudio.Instance?.Cue("ship_launch");
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
            _ship = null;
            _exhaust = null;
            _sun = null; // sun billboard lived under _root (destroyed); flare sprites persist on _ui
            _sunMat = null;
            _active = false;
            _shake = 0f;
            _hitFlash = 0f;
            _cargoFlash = 0f;
            Game.SpaceViewActive = false;
        }

        private void OnHyperjump() => _hyperjumping = true;

        private void OnShipCombat(Spacecraft.Networking.Messages.ShipCombatStatus s)
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
            Spacecraft.Networking.Messages.NetBody current = null;
            Spacecraft.Networking.Messages.NetStarSystem system = null;
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
            const float homeDiameter = 150f;
            SpawnBody("HomePlanet", homePos, homeDiameter, homeType);
            _landables.Add((string.Empty, Game?.LocationName ?? "home", homePos, homeDiameter * 0.5f));
            _keepOut.Add((homePos, homeDiameter * 0.5f + KeepOutMargin));
            float maxDist = homePos.magnitude;

            // The system's other planets/moons at their (scaled) orbit coords — all real, all landable.
            if (current != null && system != null)
            {
                const float PlanetDiameter = 46f, MoonDiameter = 28f;
                const float BodyGap = 8f; // clear space kept between two bodies' surfaces

                // Plan every body first (positions + radii), then nudge any overlaps apart, THEN spawn — so no
                // two planets/moons ever clip into each other at the compact view scale.
                var ids = new List<string>();
                var names = new List<string>();
                var positions = new List<Vector3>();
                var radii = new List<float>();
                var bodyTypes = new List<string>();

                void Plan(string id, string name, Vector3 pos, float diameter, string type)
                {
                    ids.Add(id); names.Add(name); positions.Add(pos); radii.Add(diameter * 0.5f); bodyTypes.Add(type);
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
                    Plan(b.Id, b.Name, pos, PlanetDiameter, type);
                    planets.Add((b.SystemX, b.SystemZ, pos, PlanetDiameter * 0.5f));
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
                    float minClear = parent.Radius + MoonDiameter * 0.5f + BodyGap; // outside the planet's surface
                    if (rel.magnitude < minClear)
                    {
                        Vector3 dir = rel.sqrMagnitude > 0.0001f ? rel.normalized : Vector3.right;
                        rel = dir * minClear;
                    }

                    Plan(b.Id, b.Name, parent.Render + rel, MoonDiameter, type);
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
                    SpawnBody("SystemBody_" + ids[k], positions[k], radii[k] * 2f, bodyTypes[k]);
                    _landables.Add((ids[k], names[k], positions[k], radii[k]));
                    _keepOut.Add((positions[k], radii[k] + KeepOutMargin)); // can't fly into it — slide + press E to land
                    maxDist = Mathf.Max(maxDist, positions[k].magnitude);
                }
            }

            _bounds = Mathf.Max(Bounds, maxDist + 140f); // keep the whole system reachable
        }

        /// <summary>Spawns one real celestial body: a lit, textured sphere with a per-type cloud shell.</summary>
        private void SpawnBody(string name, Vector3 pos, float diameter, string planetType)
        {
            var look = PlanetLook(planetType);
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = name;
            StripCollider(sphere);
            sphere.transform.SetParent(_root.transform, false);
            sphere.transform.localPosition = pos;
            sphere.transform.localScale = Vector3.one * diameter;
            sphere.GetComponent<Renderer>().sharedMaterial = Lit(look.tint, LoadTex(look.tex), new Vector2(3f, 2f));

            var (cloudCol, cloudDen) = PlanetCloudLook(planetType);
            AddCloudShell(sphere.transform, cloudCol, cloudDen);
        }

        private GameObject BuildShip(Transform parent)
        {
            var ship = new GameObject("Ship");
            ship.transform.SetParent(parent, false);

            var hull = Unlit(new Color(0.6f, 0.62f, 0.68f));
            var glass = Unlit(new Color(0.5f, 0.85f, 0.95f));
            var engine = Unlit(new Color(0.9f, 0.55f, 0.2f));

            Cube("Body", ship.transform, new Vector3(0f, 0f, 0f), new Vector3(1.6f, 0.9f, 3.4f), hull);
            Cube("WingL", ship.transform, new Vector3(-1.3f, 0f, -0.3f), new Vector3(1.2f, 0.2f, 1.4f), hull);
            Cube("WingR", ship.transform, new Vector3(1.3f, 0f, -0.3f), new Vector3(1.2f, 0.2f, 1.4f), hull);
            Cube("Cockpit", ship.transform, new Vector3(0f, 0.5f, 1.2f), new Vector3(0.9f, 0.6f, 1.0f), glass);
            Cube("Engine", ship.transform, new Vector3(0f, 0f, -1.9f), new Vector3(1.0f, 0.7f, 0.5f), engine);

            // Glowing thruster exhaust (stretches with throttle in Update).
            var ex = Cube("Exhaust", ship.transform, new Vector3(0f, 0f, -2.4f), new Vector3(0.6f, 0.6f, 1f), Unlit(new Color(0.6f, 0.85f, 1f)));
            _exhaust = ex.transform;
            return ship;
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

            var shader = Shader.Find("Spacecraft/SunGlow") ?? Shader.Find("Unlit/Color");
            var mat = new Material(shader) { mainTexture = GenerateGlowTexture() };
            mat.SetColor("_Color", color);

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

        private void SyncEntities()
        {
            var space = Game.Space;
            var seen = new HashSet<string>();
            if (space != null)
            {
                foreach (var e in space.Entities)
                {
                    seen.Add(e.Id);
                    if (!_entities.TryGetValue(e.Id, out var go))
                    {
                        if (e.Kind == "Asteroid")
                        {
                            // A rocky, slowly-tumbling chunk: stone-textured + an irregular shape so it
                            // reads as an asteroid rather than a flat cube.
                            go = Cube("Asteroid", _root.transform, Vector3.zero, EntityScale(e.Kind), AsteroidMat());
                            int h = e.Id.GetHashCode();
                            var sc = go.transform.localScale;
                            go.transform.localScale = Vector3.Scale(sc, new Vector3(
                                0.85f + ((h >> 2) & 7) * 0.04f,
                                0.80f + ((h >> 5) & 7) * 0.05f,
                                0.90f + ((h >> 8) & 7) * 0.04f));
                            go.AddComponent<Spin>();
                        }
                        else
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

            _ui = UiKit.CreateCanvas("Space View Overlay");
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
                // Return-to-launch-body confirmation (key-driven, no cursor): Enter confirms, Esc cancels.
                _fade.color = new Color(0f, 0f, 0f, 0f);
                var loc = Game.Localizer;
                _hint.text = loc != null ? loc.Get("ui.space.land_confirm") : "Return to the surface?   Enter = yes · Esc = no";
                _hint.gameObject.SetActive(true);
                _board.gameObject.SetActive(false);
                _cargo.gameObject.SetActive(false);
            }
            else
            {
                _fade.color = new Color(0f, 0f, 0f, 0f);
                var loc = Game.Localizer;
                _hint.text = loc != null ? loc.Get("ui.space.controls") : "WASD/Mouse fly · V view · E land/dock · L return";
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

            // Lens flare only during free flight (hidden behind the launch/landing/boarding fades).
            if (_phase == Phase.Cruise)
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
                bool show = _phase == Phase.Cruise && !_confirmLand;
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
                bool show = _phase == Phase.Cruise && !_confirmLand && _systems.Count > 0;
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
            }
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
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Spacecraft/VertexColorOpaque");
            return new Material(shader) { color = c };
        }

        private Material _asteroidMat; // shared stone material for the field's asteroids (rebuilt per view)

        private Material AsteroidMat()
            => _asteroidMat ??= Lit(new Color(0.52f, 0.50f, 0.47f), LoadTex("stone"), new Vector2(2f, 2f));

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
            var shader = Shader.Find("Spacecraft/LitColor") ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { color = c };
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

            var shader = Shader.Find("Spacecraft/Cloud") ?? Shader.Find("Unlit/Transparent");
            var mat = new Material(shader) { mainTexture = CloudCoverTexture(density) };
            mat.renderQueue = 3000;
            var c = color;
            c.a = Mathf.Clamp01(0.55f + density * 0.4f);
            mat.SetColor(Shader.PropertyToID("_Color"), c);

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
