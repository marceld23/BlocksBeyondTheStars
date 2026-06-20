using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// First-person controller: WASD + mouse look, jump, and left/right-click to mine/place.
    /// Mining and placing only *send intents* — the server validates and the world updates
    /// when the resulting <c>BlockChanged</c> message arrives.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;
        public PlayerAvatar Avatar;
        public GameMenu Menu;
        public WeaponFx Weapons;

        public float MoveSpeed = 6f;
        public float JumpSpeed = 7f;
        public float Gravity = 20f;
        public float SafeFallSpeed = 14f; // impact speed you can land at unharmed (~5 blocks); faster hurts
        public float JetpackAccel = 26f;   // upward acceleration while the jetpack fires
        public float JetpackMaxRise = 6.5f; // cap on jetpack-driven rise speed
        // Zero-g (above the atmosphere): float instead of fall — Jump rises, crouch sinks, else drift to a stop.
        public float SpaceFloatSpeed = 4f;
        public float SpaceFloatAccel = 14f;
        // Swimming: in water the player drifts down slowly and holds Jump to rise / surface (no fast falls).
        public float SwimUpSpeed = 4f;     // rise speed while holding Jump underwater
        public float SwimSinkSpeed = 1.5f; // gentle idle sink toward the seabed
        public float SwimAccel = 12f;      // how fast vertical speed eases toward the swim target
        public float SwimSpeedMul = 0.62f; // horizontal movement is slower in water
        public float MouseSensitivity = 2f;
        public bool InvertY = false;

        // --- Per-world gravity ---
        // The movement fields above are the BASELINE (1.0× gravity). These are the live values actually used by
        // Move()/ApplyGravityOnly(), recomputed from this world's WorldEnvironment.GravityFactor: lighter worlds
        // jump higher and walk faster, heavier worlds jump only ~1 block and walk slower. A ≥1-block jump is
        // always preserved, and jetpack net-thrust + safe fall distance stay constant so nothing breaks.
        private float _gFactor = 1f;           // last applied factor (only redo the maths when it moves)
        private float _effGravity = 20f;       // live gravity accel
        private float _effJumpSpeed = 7f;      // live jump impulse (sized to clear the target jump height)
        private float _effMoveSpeed = 6f;      // live walk speed
        private float _effJetpackAccel = 26f;  // live jetpack accel (net thrust kept constant vs gravity)
        private float _effSafeFallSpeed = 14f; // live fall-damage threshold (keeps ~constant fall distance)

        /// <summary>Comfort toggle (settings): head bob + FOV kick + impact shake. Off = steady camera.</summary>
        public bool CameraMotion = true;
        public bool ThirdPerson = false;
        public float Reach = 8f; // match the server's MaxReach (8) — a shorter client reach left a silent dead-band (B32)

        // Hover speeder (arcade, car-style): W gas / S brake-reverse / A,D steer / Space hop / Shift boost.
        public float SpeederMaxSpeed = 13f;
        public float SpeederBoostSpeed = 20f;
        public float SpeederAccel = 16f;
        public float SpeederTurnSpeed = 95f;     // degrees/sec, scaled by current speed
        public float SpeederHoverHeight = 1.3f;  // metres held above the ground below
        public float SpeederHopSpeed = 6f;       // Space gives a quick lift over a low obstacle
        public float SpeederImpactThreshold = 9f; // a hard stop above this speed reports a collision
        public float SpeederBoardRange = 3.2f;
        public float SpeederStowRange = 3.5f;

        private const int HotbarSlots = 9;

        private static readonly Vector3 FirstPersonEye = new Vector3(0f, 1.6f, 0f);
        private static readonly Vector3 ThirdPersonEye = new Vector3(0f, 1.9f, -3.5f);

        private CharacterController _controller;
        private float _pitch;
        private float _verticalVelocity;
        private float _moveSendTimer;
        private bool _spawned;
        private Vector3 _spawnPos;
        private bool _settling;
        private float _settleTimer; // how long we've been frozen at spawn waiting for the floor to stream
        private bool _worldRevealed; // settle: has the loading overlay been dismissed for this spawn yet
        private bool _wasGrounded = true;
        private bool _jetpackActive; // last reported jetpack thrust state (server drains energy on this)
        private float _stepTimer;
        private int _lastWorldEpoch;

        // Speeder drive state.
        private float _speederSpeed;
        private bool _wasDriving;
        private float _speederCamPitch = 10f;

        // Camera feel (first-person head-bob, FOV kick, landing shake).
        private float _bobPhase;
        private float _camShake;
        private float _baseFov = 60f;
        private bool _moving;

        private Viewmodel _viewmodel;
        private string _heldKey = "\0"; // forces the first refresh

        private void Awake() => _controller = GetComponent<CharacterController>();

        private void Start()
        {
            // First-person viewmodel lives on the camera (shown when the avatar is hidden).
            if (Camera != null)
            {
                _viewmodel = Camera.gameObject.AddComponent<Viewmodel>();
                _viewmodel.Game = Game;
                _baseFov = Camera.fieldOfView;
            }

            ApplyCameraMode();
        }

        private void ApplyCameraMode()
        {
            if (Camera != null)
            {
                Camera.transform.localPosition = ThirdPerson ? ThirdPersonEye : FirstPersonEye;
            }

            // Show the avatar only in third-person (otherwise the camera is inside the head); the
            // first-person viewmodel is the opposite.
            Avatar?.SetVisible(ThirdPerson);
            _viewmodel?.SetVisible(!ThirdPerson);
        }

        /// <summary>Plays the tool swing on both the third-person avatar and the first-person viewmodel.</summary>
        private void TriggerSwing()
        {
            Avatar?.Swing();
            _viewmodel?.Swing();
        }

        /// <summary>Mirrors the selected hotbar item into the avatar hand + viewmodel (rebuilds on change).</summary>
        private void RefreshHeldItem()
        {
            string key = Game?.ItemInSlot(Game.SelectedHotbarSlot) ?? string.Empty;
            if (key == _heldKey)
            {
                return;
            }

            _heldKey = key;
            var (kind, tint, blockKey) = HeldItem.For(Game?.Content, key);
            Avatar?.SetHeldItem(kind, tint, blockKey);
            _viewmodel?.SetHeldItem(kind, tint, blockKey);
        }

        private void Update()
        {
            RecomputeGravity(); // keep the live movement constants in step with this world's gravity factor

            // On travel the world is rebuilt at a new location: re-run the spawn snap there.
            if (Game != null && Game.WorldEpoch != _lastWorldEpoch)
            {
                _lastWorldEpoch = Game.WorldEpoch;
                _spawned = false;
                _settling = false;
            }

            // Snap to the server's authoritative spawn once it is known, then take over.
            if (!_spawned && Game != null && Game.ServerSpawn.HasValue)
            {
                _spawnPos = Game.ServerSpawn.Value;
                SnapTo(_spawnPos);
                _spawned = true;
                _settling = true; // hold at spawn until the ground/ship chunk streams in
                _settleTimer = 0f;
                _worldRevealed = false;
            }

            // On death the server respawns us at the ship's heal-tank — teleport the body there.
            if (Game != null && Game.RespawnTarget.HasValue)
            {
                _spawnPos = Game.RespawnTarget.Value;
                SnapTo(_spawnPos);
                Game.RespawnTarget = null;
                _settling = true; // hold at the heal-tank until its chunk is streamed
                _settleTimer = 0f;
                _worldRevealed = false;
            }

            // Hold the player frozen at the spawn (no gravity, no control, no movement sent) until the
            // floor chunk's collider has actually streamed in below them — then release. Reacting only
            // after a fall let a far teleport (boarding a station, travel) drop through while chunks loaded.
            if (_settling && Game != null)
            {
                transform.position = _spawnPos;
                _verticalVelocity = 0f;

                // While the "Du bist gestorben" prompt is up, stay frozen at the heal-tank and do NOT reveal
                // the world yet — the player only "appears" in the ship once they click Weiter. Freeze the
                // settle timer too so the void-rescue grace doesn't fire the instant they confirm.
                bool awaitingConfirm = Game.AwaitingRespawnConfirm;
                if (!awaitingConfirm)
                {
                    _settleTimer += Time.deltaTime;
                }

                // Publish the spawn position NOW (before the settling return below) so the world's seam-aware
                // chunk placement (GameBootstrap.SceneX uses PlayerPosition) renders the chunks AROUND the spawn
                // and the ground-check raycast lines up with their colliders. Without this, a spawn far from X=0
                // (e.g. a landing pad near the longitude-wrap seam) left PlayerPosition stale at the origin, so
                // chunks rendered far away ("only sky") and the raycast missed the ground → frozen at spawn.
                Game.PlayerPosition = transform.position;

                // Solid ground loaded somewhere below the spawn? (the chunk's MeshCollider exists)
                bool groundBelow = Physics.Raycast(_spawnPos + Vector3.up * 0.5f, Vector3.down, out var gHit, 10f)
                                   && gHit.collider != _controller;

                // Reveal the world + release control TOGETHER — as soon as there is real ground under the spawn,
                // or after a short grace (then the server's void-rescue recovers a still-streaming spawn chunk by
                // teleporting onto the ship). Tying reveal to release means you never see a "loaded" world you
                // can't move in; the short grace means the veil never lingers and feels stuck either.
                if (!awaitingConfirm && (groundBelow || _settleTimer > 8f))
                {
                    if (!_worldRevealed)
                    {
                        _worldRevealed = true;
                        Game.NotifyWorldReady();
                    }

                    _settling = false;
                    _settleTimer = 0f;
                }
                else
                {
                    return; // stay frozen behind the veil (no fall) until the ground chunk streams in
                }
            }

            // The space view owns the camera and freezes on-foot control entirely.
            if (Game != null && Game.SpaceViewActive)
            {
                UpdateJetpack(false);
                return;
            }

            // A UI panel or the chat input is open: don't steer/interact, just settle by gravity.
            if (Game != null && (Game.MenuOpen || Game.ChatTyping))
            {
                UpdateJetpack(false);
                ApplyGravityOnly();
                return;
            }

            // Driving a hover speeder takes over movement + camera entirely (arcade hover, car-style).
            if (Game != null && !string.IsNullOrEmpty(Game.InSpeeder))
            {
                UpdateJetpack(false);
                DriveSpeeder();
                SendMovement();
                Game.PlayerPosition = transform.position;
                Game.PlayerYaw = transform.eulerAngles.y;
                return;
            }

            // Just stepped out of a speeder → restore the on-foot camera + viewmodel.
            if (_wasDriving)
            {
                _wasDriving = false;
                ApplyCameraMode();
                ClientAudio.Instance?.SpeederStop();
            }

            // On foot: board a speeder you own that you're standing next to (E), or pack one up (X). Checked
            // before the generic E interact so boarding the speeder beside you wins.
            if (Input.GetKeyDown(KeyCode.E) && TryBoardNearbySpeeder())
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.X) && TryStowNearbySpeeder())
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.V))
            {
                ThirdPerson = !ThirdPerson;
                ApplyCameraMode();
            }

            RefreshHeldItem();

            if (Input.GetKeyDown(KeyCode.F) && WeaponSwingReady())
            {
                AttackNearestEnemy();
                TriggerSwing();
            }

            if (Input.GetKeyDown(KeyCode.G))
            {
                LootNearestContainer();
            }

            if (Input.GetKeyDown(KeyCode.H))
            {
                DepositToNearestCrate();
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                RepairWreckCell();
            }

            if (Input.GetKeyDown(KeyCode.L))
            {
                _lampOn = !_lampOn;
                ClientAudio.Instance?.Cue("lamp_toggle");
            }

            UpdateLamp();
            HandleHotbar();
            LookAround();
            Move();
            UpdateCameraFeel();
            HandleInteract();
            HandleDrillAudio();
            UpdateGearPeriodically();
            SendMovement();

            // Publish local pose for the HUD minimap/compass.
            if (Game != null)
            {
                Game.PlayerPosition = transform.position;
                Game.PlayerYaw = transform.eulerAngles.y;
                HandleStations();
            }
        }

        private void AttackNearestEnemy()
        {
            if (Game?.Network == null)
            {
                return;
            }

            PlayWeaponSound();
            string nearest = null;
            Vector3 nearestPos = default;
            float bestSq = 6f * 6f; // attack reach
            foreach (var e in Game.PlanetEnemies)
            {
                var ep = Game.ScenePos(e.X, e.Y, e.Z); // seam-aware (longitude wraps)
                float d = (ep - transform.position).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    nearest = e.Id;
                    nearestPos = ep;
                }
            }

            // Creatures (fauna) are attackable too — the server shares the hit path.
            foreach (var c in Game.Creatures)
            {
                var cp = Game.ScenePos(c.X, c.Y, c.Z); // seam-aware (longitude wraps)
                float d = (cp - transform.position).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    nearest = c.Id;
                    nearestPos = cp;
                }
            }

            if (nearest != null)
            {
                Game.Network.SendAttackEntity(nearest);
            }

            if (Weapons != null && Camera != null)
            {
                var ct = Camera.transform;
                var from = ct.position + ct.forward * 0.4f - ct.up * 0.15f;
                var col = WeaponColor();
                var kind = HeldWeaponFx();
                if (kind == WeaponFxKind.Melee)
                {
                    // A melee slash sweeps whether or not it connects (whiff still reads).
                    Weapons.MeleeArc(from, ct.forward, ct.up, col);
                }
                else if (nearest != null)
                {
                    var target = nearestPos + Vector3.up * 0.4f;
                    if (kind == WeaponFxKind.Projectile)
                    {
                        Weapons.Projectile(from, target, col); // kinetic bolt that flies + bursts
                    }
                    else
                    {
                        Weapons.Shoot(from, target, col); // instant energy beam/tracer
                    }
                }
            }
        }

        private enum WeaponFxKind { Beam, Projectile, Melee }

        /// <summary>Classifies the held weapon's effect: kinetic guns fire a flying bolt, energy guns an
        /// instant beam, and short-range weapons (or bare fists) a melee slash arc.</summary>
        private WeaponFxKind HeldWeaponFx()
        {
            string key = Game.ItemInSlot(Game.SelectedHotbarSlot) ?? string.Empty;
            if (key.Contains("gauss") || key.Contains("rail") || key.Contains("slug"))
            {
                return WeaponFxKind.Projectile;
            }

            if (key.Contains("laser") || key.Contains("blaster") || key.Contains("beam"))
            {
                return WeaponFxKind.Beam;
            }

            float range = Game.Content?.GetItem(key)?.Tool?.Range ?? 0f;
            return range > 6f ? WeaponFxKind.Beam : WeaponFxKind.Melee;
        }

        /// <summary>The beam/spark colour for the held weapon (energy types tint their bolts).</summary>
        private Color WeaponColor()
        {
            string held = Game.ItemInSlot(Game.SelectedHotbarSlot) ?? string.Empty;
            if (held.Contains("plasma")) return new Color(0.92f, 0.45f, 1f);
            if (held.Contains("laser")) return new Color(1f, 0.42f, 0.36f);
            if (held.Contains("gauss")) return new Color(0.5f, 0.9f, 1f);
            return new Color(1f, 0.95f, 0.8f); // melee / default
        }

        private void LootNearestContainer()
        {
            if (Game?.Network == null)
            {
                return;
            }

            string nearest = null;
            float bestSq = 6f * 6f; // loot reach
            foreach (var c in Game.Containers)
            {
                float d = (Game.ScenePos(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f) - transform.position).sqrMagnitude; // seam-aware
                if (d < bestSq)
                {
                    bestSq = d;
                    nearest = c.Id;
                }
            }

            if (nearest != null)
            {
                ClientAudio.Instance?.Cue("loot");
                Game.Network.SendLootContainer(nearest);
            }
        }

        /// <summary>Stash loose materials into the nearest storage crate (Task 5 Stage 3b).</summary>
        private void DepositToNearestCrate()
        {
            if (Game?.Network == null)
            {
                return;
            }

            string nearest = null;
            float bestSq = 6f * 6f;
            foreach (var c in Game.Containers)
            {
                if (c.Kind != "crate")
                {
                    continue;
                }

                float d = (Game.ScenePos(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f) - transform.position).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    nearest = c.Id;
                }
            }

            if (nearest != null)
            {
                ClientAudio.Instance?.Cue("loot");
                Game.Network.SendDepositContainer(nearest);
            }
        }

        private bool _gearHelmet, _gearChest, _gearLegs, _gearPack, _gearLamp;
        private float _gearTimer;

        /// <summary>Mirrors the player's carried gear onto the third-person avatar (helmet/chest/legs/
        /// pack), refreshed a couple of times a second so it tracks pickups/crafts without polling hard.</summary>
        private void UpdateGearPeriodically()
        {
            _gearTimer -= Time.deltaTime;
            if (_gearTimer > 0f || Avatar == null || Game?.Personal == null)
            {
                return;
            }

            _gearTimer = 0.5f;
            bool helmet = HasItem("helmet");
            bool chest = HasItem("armor_chest") || HasItem("stealth_suit");
            bool legs = HasItem("armor_legs");
            bool pack = HasItem("oxygen_tank_2") || HasItem("jetpack");
            bool lamp = HasItem("suit_lamp");

            if (helmet != _gearHelmet || chest != _gearChest || legs != _gearLegs || pack != _gearPack || lamp != _gearLamp)
            {
                _gearHelmet = helmet;
                _gearChest = chest;
                _gearLegs = legs;
                _gearPack = pack;
                _gearLamp = lamp;
                Avatar.SetGear(helmet, chest, legs, pack, lamp);
            }
        }

        private bool _lampOn;
        private GameObject _lampCone; // visible warm light shaft, shown while the lamp is on
        private static readonly int LampPosId = Shader.PropertyToID("_Sc_LampPos");
        private static readonly int LampDirId = Shader.PropertyToID("_Sc_LampDir");
        private static readonly int LampColId = Shader.PropertyToID("_Sc_LampColor");

        /// <summary>Feeds the suit headlamp / flashlight (toggle L) into the world shaders as globals — the
        /// block + lit shaders run their own lighting, so the lamp is a shader spotlight cast from the
        /// camera, not a Unity Light. Requires the <c>suit_lamp</c> equipment to be carried.</summary>
        private void UpdateLamp()
        {
            bool on = _lampOn && Camera != null && HasItem("suit_lamp");
            if (on)
            {
                var t = Camera.transform;
                Vector3 p = t.position, f = t.forward;
                Shader.SetGlobalVector(LampPosId, new Vector4(p.x, p.y, p.z, 26f));   // range
                Shader.SetGlobalVector(LampDirId, new Vector4(f.x, f.y, f.z, 0.80f)); // cone cos (~37°)
                Shader.SetGlobalColor(LampColId, ShaderColor.Srgb(new Color(1.6f, 1.5f, 1.3f, 1f))); // warm, HDR intensity
                EnsureLampCone();
            }
            else
            {
                Shader.SetGlobalColor(LampColId, new Color(0f, 0f, 0f, 0f));
            }

            if (_lampCone != null)
            {
                _lampCone.SetActive(on);
            }
        }

        /// <summary>Builds the visible light shaft (a faint warm translucent cone) once, parented to the
        /// camera so it always points where the player looks. The actual lighting is the shader spotlight
        /// above; this is the volumetric beam you see in the dark.</summary>
        private void EnsureLampCone()
        {
            if (_lampCone != null || Camera == null)
            {
                return;
            }

            _lampCone = new GameObject("LampCone");
            _lampCone.transform.SetParent(Camera.transform, false);
            _lampCone.transform.localPosition = new Vector3(0f, -0.08f, 0.4f);
            _lampCone.transform.localRotation = Quaternion.identity;

            _lampCone.AddComponent<MeshFilter>().sharedMesh = BuildConeMesh(10f, 1.9f, 24);
            var mr = _lampCone.AddComponent<MeshRenderer>();
            var shader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Color");
            mr.sharedMaterial = new Material(shader) { color = ShaderColor.Srgb(new Color(1f, 0.94f, 0.76f, 0.06f)) };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>A hollow cone mesh: apex at the origin opening along +Z to a base ring (light shaft).</summary>
        private static Mesh BuildConeMesh(float length, float radius, int seg)
        {
            var verts = new Vector3[seg + 1];
            var uvs = new Vector2[seg + 1];
            verts[0] = Vector3.zero; // apex (at the lamp)
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, length);
            }

            var tris = new int[seg * 3];
            for (int i = 0; i < seg; i++)
            {
                tris[i * 3] = 0;
                tris[i * 3 + 1] = 1 + i;
                tris[i * 3 + 2] = 1 + (i + 1) % seg;
            }

            var m = new Mesh { vertices = verts, uv = uvs, triangles = tris };
            m.RecalculateBounds();
            return m;
        }

        private bool HasItem(string key)
        {
            foreach (var s in Game.Personal)
            {
                if (s.Item == key && s.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Keeps the drill loop alive while the player holds mine with a drill aimed at a block.</summary>
        private void HandleDrillAudio()
        {
            if (Camera == null || !Input.GetMouseButton(0) || !HoldingDrill())
            {
                return;
            }

            // Target via the voxel grid, NOT Physics.Raycast: right after a block breaks the chunk collider is
            // rebuilt, and a raycast against it can miss for a frame — which stalled the WHOLE drill (no tick, no
            // mine, no sparks) until it settled, then everything resumed. That stall was the "mining gets stuck,
            // then a block suddenly mines and the stuck ones work too" bug (B32). The voxel world never stalls.
            if (!AimBlock(out var hitCell, out _))
            {
                return;
            }

            ClientAudio.Instance?.DrillTick();
            TriggerSwing(); // keep the mining chop going while the drill is held
            var center = new Vector3(hitCell.x + 0.5f, hitCell.y + 0.5f, hitCell.z + 0.5f);
            if (Weapons != null && Time.time >= _nextDrillSpark)
            {
                _nextDrillSpark = Time.time + 0.07f;
                Weapons.Sparks(center, new Color(1f, 0.85f, 0.5f), 3);
            }

            // Hard blocks need several hits — keep sending mine attempts while the drill is held
            // (the server accumulates effort until the block breaks).
            if (Time.time >= _nextDrillMine)
            {
                _nextDrillMine = Time.time + 0.28f; // slower, weightier mining (was 0.18)
                Game.LastMineCell = hitCell; // so an "already empty" rejection can clear the ghost here (B32)
                Game.Network?.SendMine(hitCell.x, hitCell.y, hitCell.z);
            }
        }

        private float _nextDrillSpark;
        private float _nextDrillMine;

        /// <summary>True if the selected hotbar item is a drill (its primary action mines).</summary>
        private bool HoldingDrill()
        {
            string held = Game.ItemInSlot(Game.SelectedHotbarSlot);
            return !string.IsNullOrEmpty(held)
                && Game.Content?.GetItem(held)?.Tool?.Kind == BlocksBeyondTheStars.Shared.Definitions.ToolKind.Drill;
        }

        /// <summary>True if the selected hotbar item is a handheld scanner (its primary action scans).</summary>
        private bool HoldingScanner()
        {
            string held = Game.ItemInSlot(Game.SelectedHotbarSlot);
            return !string.IsNullOrEmpty(held)
                && Game.Content?.GetItem(held)?.Tool?.Kind == BlocksBeyondTheStars.Shared.Definitions.ToolKind.Scanner;
        }

        /// <summary>True if the selected hotbar item is a weapon (its primary action attacks, like F).</summary>
        private bool HoldingWeapon()
        {
            string held = Game.ItemInSlot(Game.SelectedHotbarSlot);
            return !string.IsNullOrEmpty(held)
                && Game.Content?.GetItem(held)?.Tool?.Kind == BlocksBeyondTheStars.Shared.Definitions.ToolKind.Weapon;
        }

        private float _nextWeaponSwing; // Time.time when the held weapon may swing again (client-side cooldown)
        private const float DefaultMeleeCooldown = 1.5f; // mirrors the server default for energy-free melee (B44)

        /// <summary>Whether the held weapon's swing cooldown has elapsed; if so, arms the next swing. Mirrors the
        /// server cooldown so the swing animation/sound + attack intent are gated too (so the machete's 1.5s
        /// cooldown is actually felt, not just silently dropped server-side).</summary>
        private bool WeaponSwingReady()
        {
            if (Time.time < _nextWeaponSwing)
            {
                return false;
            }

            var tool = Game.Content?.GetItem(Game.ItemInSlot(Game.SelectedHotbarSlot))?.Tool;
            float cd = tool == null ? 0f
                : tool.CooldownSeconds > 0f ? tool.CooldownSeconds
                : tool.EnergyPerUse <= 0f ? DefaultMeleeCooldown : 0f;
            _nextWeaponSwing = Time.time + cd;
            return true;
        }

        /// <summary>Scans the nearest creature (threat assessment) or, failing that, the block in view.</summary>
        private void ScanTarget()
        {
            if (Game?.Network == null || Camera == null)
            {
                return;
            }

            string speciesId = null;
            Vector3 scanPos = default;
            float bestSq = Reach * Reach;
            foreach (var c in Game.Creatures)
            {
                var cp = Game.ScenePos(c.X, c.Y, c.Z); // seam-aware (longitude wraps)
                float d = (cp - transform.position).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    speciesId = c.SpeciesId;
                    scanPos = cp;
                }
            }

            if (speciesId != null)
            {
                Game.Network.SendScan("creature", speciesId);
                Weapons?.Pulse(scanPos, new Color(0.4f, 0.85f, 1f));
                return;
            }

            // Voxel ray-march INCLUDING fluids, so you can scan a water/lava block too (they have no collider, so
            // a Physics.Raycast passes straight through them — that's why water "couldn't be scanned", B26).
            if (!AimBlock(out var b, out _, includeFluids: true))
            {
                return;
            }

            var def = Game.Content?.BlockById(Game.World.GetBlock(b.x, b.y, b.z));
            if (def != null)
            {
                Game.Network.SendScan("block", def.Key);
                Weapons?.Pulse(new Vector3(b.x + 0.5f, b.y + 0.5f, b.z + 0.5f), new Color(0.4f, 0.85f, 1f));
            }
        }

        private CameraTool _cameraTool;

        /// <summary>Lazily builds the client-side camera tool (HUD-free photo capture), wired to the view camera.</summary>
        private CameraTool EnsureCameraTool()
        {
            if (_cameraTool == null)
            {
                _cameraTool = gameObject.AddComponent<CameraTool>();
                _cameraTool.Game = Game;
                _cameraTool.Source = Camera;
            }

            return _cameraTool;
        }

        /// <summary>Right-click use of a held gadget (item 36): sends the use intent at the aim point and plays
        /// the local effect + sound. The server validates suit energy + cooldown and applies the real effect.</summary>
        private void UseGadget(string key)
        {
            if (Game?.Network == null || Camera == null)
            {
                return;
            }

            // Aim point: the block under the crosshair, else a point a few metres ahead of the camera.
            Vector3 target = AimBlock(out var cell, out _, includeFluids: true)
                ? new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z + 0.5f)
                : transform.position + Camera.transform.forward * 5f;

            Game.Network.SendUseGadget(key, target);

            var self = transform.position + Vector3.up;
            switch (key)
            {
                case "field_medkit":
                    Weapons?.Pulse(self, new Color(0.35f, 1f, 0.5f)); // a green first-aid pulse around you
                    ClientAudio.Instance?.Cue("medkit_heal");
                    break;
                case "stasis_projector":
                    Weapons?.Pulse(target, new Color(0.4f, 0.8f, 1f)); // a cyan stasis burst at the aim point
                    ClientAudio.Instance?.At("stasis_activate", target);
                    break;
                case "terrain_blaster":
                    Weapons?.Flash(target, new Color(1f, 0.6f, 0.2f), 1.3f);  // an orange detonation flash
                    Weapons?.Sparks(target, new Color(1f, 0.5f, 0.2f), 18);   // flying rubble/debris
                    ClientAudio.Instance?.At("terrain_blast", target);
                    break;
                case "terrain_scanner":
                    Weapons?.Pulse(self, new Color(1f, 0.8f, 0.25f)); // an amber prospecting pulse around you
                    ClientAudio.Instance?.Cue("terrain_scan");        // the sonar sweep (Feature 40)
                    break;
            }
        }

        /// <summary>Fills the targeted breach cell of a crashed wreck with the selected hotbar block (server validates).</summary>
        private void RepairWreckCell()
        {
            if (Game?.Network == null || Camera == null)
            {
                return;
            }

            string item = Game.ItemInSlot(Game.SelectedHotbarSlot);
            if (string.IsNullOrEmpty(item))
            {
                return; // need a block in hand to rebuild the hull
            }

            var ray = new Ray(Camera.transform.position, Camera.transform.forward);
            if (!Physics.Raycast(ray, out var hit, Reach))
            {
                return;
            }

            // Fill the empty cell against the hit face — the server checks it against the wreck's intact mask.
            var t = FloorVec(hit.point + hit.normal * 0.5f);
            Game.Network.SendRepairWreck(t.x, t.y, t.z, item);
        }

        private void HandleStations()
        {
            // Prefer the station you're looking at; fall back to the nearest one you're standing by. (Pure
            // proximity made a cramped ship always read as the central station, "whatever you looked at".)
            Game.NearbyStation = Game.LookedStationType(Camera, Reach);
            if (string.IsNullOrEmpty(Game.NearbyStation))
            {
                Game.NearbyStation = Game.NearestStationType(transform.position, 3f);
            }

            if (string.IsNullOrEmpty(Game.NearbyStation) && Game.NearVendor)
            {
                Game.NearbyStation = "market"; // a settlement/station vendor → "trade" prompt + E opens the market
            }

            if (!Input.GetKeyDown(KeyCode.E))
            {
                return;
            }

            // A radio beacon you own that you're aiming at → rename it (item 37).
            if (TryAimOwnedBeacon(out int beaconId, out string current))
            {
                BeaconLabelUi.Instance?.Open(
                    Game.Localizer?.Get("ui.beacon.rename_prompt") ?? "Rename beacon",
                    current,
                    label => Game.Network?.SendSetBeaconLabel(beaconId, label));
                return;
            }

            // A planet base you own (Grundstein) that you're aiming at → rename it.
            if (TryAimOwnedBase(out string baseBodyId, out string baseName))
            {
                BeaconLabelUi.Instance?.Open(
                    Game.Localizer?.Get("ui.base.rename_prompt") ?? "Rename base",
                    baseName,
                    name => Game.Network?.SendSetBaseName(baseBodyId, name));
                return;
            }

            // Inside your own boarded station, aiming at the station core → rename the station (server checks owner).
            if (!string.IsNullOrEmpty(Game.CurrentStationId)
                && AimBlock(out var coreHit, out _)
                && Game.Content?.BlockById(Game.World.GetBlock(coreHit.x, coreHit.y, coreHit.z))?.Key == "station_core")
            {
                string stationId = Game.CurrentStationId;
                BeaconLabelUi.Instance?.Open(
                    Game.Localizer?.Get("ui.map.rename") ?? "Rename",
                    Game.StationName,
                    name => Game.Network?.SendSetStationName(stationId, name));
                return;
            }

            // A beam block (teleporter pad) you're standing on / next to opens the transporter — pick a destination
            // among your own + allied pads on this world, then beam to it.
            int beam = BeamView.Instance != null ? BeamView.Instance.NearestUsableBeam(transform.position, 2.2f) : 0;
            if (beam != 0)
            {
                BeamPadUi.Instance?.Open(beam);
                return;
            }

            // A settlement hinge door you're standing at opens/closes with E — checked BEFORE stations so a door
            // next to a market stall still opens (sci-fi slide doors open themselves; this is for village doors) (B47).
            int door = DoorView.Instance != null ? DoorView.Instance.NearestHinge(transform.position, 3f) : 0;
            if (door != 0)
            {
                Game.Network?.SendDoorInteract(door);
                return;
            }

            // A data cube within reach downloads its minigame into the Arcade collection (item: arcade).
            if (DataCubeView.Instance != null)
            {
                int cube = DataCubeView.Instance.NearestDataCube(transform.position, 3.2f, out string gameKey, out bool owned);
                if (cube != 0)
                {
                    if (owned)
                    {
                        Game.ShowMessage(Game.Localizer?.Get("ui.datacube.already") ?? "Already in your Arcade.");
                    }
                    else if (!string.IsNullOrEmpty(gameKey))
                    {
                        Game.Network?.SendUnlockGame(cube, gameKey);
                        ClientAudio.Instance?.At("data_cube_download", transform.position, 1f, 1f);
                    }

                    return;
                }
            }

            // A net fragment within reach → recover it (text-only story find; reveals its archive + advances the story).
            if (NetFragmentView.Instance != null)
            {
                int frag = NetFragmentView.Instance.NearestNetFragment(transform.position, 3.2f, out _);
                if (frag != 0)
                {
                    Game.Network?.SendNetFragmentFound(frag);
                    ClientAudio.Instance?.At("data_cube_download", transform.position, 1f, 1f);
                    return;
                }
            }

            if (string.IsNullOrEmpty(Game.NearbyStation))
            {
                return;
            }

            // Stations that open a client UI panel; the rest are resolved server-side.
            switch (Game.NearbyStation)
            {
                case "cockpit": Menu?.OpenMap(); break;
                case "workshop": Menu?.OpenCrafting(); break;
                case "market": Menu?.OpenMarket(); Game.Network?.SendNpcGreet("vendor"); break; // item 15: vendor greeting
                case "cargo": Menu?.OpenInventory(); break;
                default:
                    if (Game.NearbyStation == "medbay") ClientAudio.Instance?.Cue("heal");
                    Game.Network?.SendUseStation(Game.NearbyStation);
                    break; // medbay, quarters
            }
        }

        /// <summary>True if the player is looking at a radio beacon block they own — returns its id + current label
        /// so E can open the rename overlay (item 37). Only the owner gets the rename prompt; everyone sees markers.</summary>
        private bool TryAimOwnedBeacon(out int beaconId, out string label)
        {
            beaconId = 0;
            label = string.Empty;
            if (Game?.Beacons == null || Game.Beacons.Length == 0 || string.IsNullOrEmpty(Game.LocalPlayerId))
            {
                return false;
            }

            if (!AimBlock(out var hit, out _))
            {
                return false; // not looking at a block within reach
            }

            foreach (var b in Game.Beacons)
            {
                if (b.OwnerId == Game.LocalPlayerId
                    && Mathf.FloorToInt(b.X) == hit.x
                    && Mathf.FloorToInt(b.Y) == hit.y
                    && Mathf.FloorToInt(b.Z) == hit.z)
                {
                    beaconId = b.Id;
                    label = b.Label ?? string.Empty;
                    return true;
                }
            }

            return false;
        }

        /// <summary>True if the player is looking at a base core (Grundstein) they own — returns its body id + the
        /// current base name so E can open the rename overlay. Only the owner gets the prompt; everyone sees the marker.</summary>
        private bool TryAimOwnedBase(out string bodyId, out string name)
        {
            bodyId = string.Empty;
            name = string.Empty;
            if (Game?.Bases == null || Game.Bases.Length == 0 || string.IsNullOrEmpty(Game.LocalPlayerId))
            {
                return false;
            }

            if (!AimBlock(out var hit, out _))
            {
                return false;
            }

            foreach (var b in Game.Bases)
            {
                if (b.OwnerId == Game.LocalPlayerId
                    && Mathf.FloorToInt(b.X) == hit.x
                    && Mathf.FloorToInt(b.Y) == hit.y
                    && Mathf.FloorToInt(b.Z) == hit.z)
                {
                    bodyId = b.BodyId;
                    name = b.Name ?? string.Empty;
                    return true;
                }
            }

            return false;
        }

        private void HandleHotbar()
        {
            if (Game == null)
            {
                return;
            }

            for (int i = 0; i < HotbarSlots; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    SelectSlot(i);
                }
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll > 0f)
            {
                SelectSlot((Game.SelectedHotbarSlot + HotbarSlots - 1) % HotbarSlots);
            }
            else if (scroll < 0f)
            {
                SelectSlot((Game.SelectedHotbarSlot + 1) % HotbarSlots);
            }
        }

        private void SelectSlot(int slot)
        {
            if (slot == Game.SelectedHotbarSlot)
            {
                return;
            }

            Game.SelectedHotbarSlot = slot;
            Game.Network?.SendSelectHotbar(slot);
        }

        /// <summary>Teleports the player (CharacterController toggled so the move isn't blocked) and zeroes fall speed.</summary>
        private void SnapTo(Vector3 pos)
        {
            _controller.enabled = false;
            transform.position = pos;
            _controller.enabled = true;
            _verticalVelocity = 0f;
        }

        /// <summary>Automation/capture hook (<see cref="ScreenshotDirector"/>): pose the on-foot player at a world
        /// position + facing so an outdoor planet shot can step out of the landed ship onto open terrain. SnapTo
        /// bypasses collision for the move; with no mouse input during a capture run the look sticks, and gravity
        /// then settles the player onto the ground.</summary>
        public void SetCapturePose(Vector3 pos, float yaw, float pitch)
        {
            SnapTo(pos);
            transform.eulerAngles = new Vector3(0f, yaw, 0f);
            _pitch = Mathf.Clamp(pitch, -89f, 89f);
            if (Camera != null)
            {
                Camera.transform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
            }
        }

        /// <summary>Capture hook (<see cref="ScreenshotDirector"/>): place the on-foot player on REAL terrain near
        /// an anchor (the landed ship) and face back toward it, for a per-planet surface shot. Unlike the blind
        /// <see cref="SetCapturePose"/>, this is terrain-aware. It probes a ring of spots around the ship (radii
        /// chosen to clear the ship's footprint — the hull is stamped into the world voxels, so it can't be told
        /// apart from terrain by collider) and for each:
        /// <list type="bullet">
        ///   <item>raycasts DOWN from high above to find the true surface Y (a probe over the void or an unbaked
        ///   far chunk simply misses → the player is never dropped through the floor / off a floating island),</item>
        ///   <item>rejects deep water (chest-height block is water → would be a submerged murk),</item>
        ///   <item>requires OPEN SKY above the spot (a short up-ray that hits a ceiling means we're under the ship
        ///   hull / in a cave / under an overhang → reject), which is what reliably keeps the shot OUTDOORS.</item>
        /// </list>
        /// Returns false when no safe, open, dry footing exists near the ship (tiny island / all-water world) so
        /// the caller can skip the shot instead of writing a broken frame.</summary>
        public bool PlaceForCaptureNear(Vector3 anchor, float pitch)
        {
            if (_controller == null)
            {
                return false;
            }

            float half = _controller.height * 0.5f + _controller.skinWidth;
            // Start clear of the hull (~16), then nearer (for small floating islands) and a little farther; every
            // candidate is gated by the open-sky check below, so a too-near one that lands inside the hull is
            // rejected rather than shot.
            float[] dists = { 16f, 13f, 19f, 11f, 22f, 25f };
            for (int di = 0; di < dists.Length; di++)
            {
                for (int a = 0; a < 8; a++)
                {
                    float ang = a * 45f * Mathf.Deg2Rad;
                    float x = anchor.x + Mathf.Sin(ang) * dists[di];
                    float z = anchor.z + Mathf.Cos(ang) * dists[di];

                    // Surface under this spot? Start the ray well above any local terrain so a hill doesn't make us
                    // start inside a collider.
                    if (!Physics.Raycast(new Vector3(x, anchor.y + 60f, z), Vector3.down, out var hit, 120f, ~0, QueryTriggerInteraction.Ignore)
                        || hit.collider == _controller)
                    {
                        continue;
                    }

                    var stand = new Vector3(x, hit.point.y + half + 0.05f, z);
                    if (BlockKeyAt(stand + Vector3.up * 1.1f) == "water")
                    {
                        continue; // solid floor but chest-deep underwater — not a usable surface shot
                    }

                    // Open sky overhead? A hit means a ceiling above us (ship hull / cave / overhang) → indoors.
                    if (Physics.Raycast(stand + Vector3.up * 0.3f, Vector3.up, out var up, 5f, ~0, QueryTriggerInteraction.Ignore)
                        && up.collider != _controller)
                    {
                        continue;
                    }

                    SnapTo(stand);
                    Vector3 d = anchor - transform.position;
                    float yaw = (Mathf.Abs(d.x) + Mathf.Abs(d.z) > 0.01f)
                        ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg
                        : transform.eulerAngles.y;
                    transform.eulerAngles = new Vector3(0f, yaw, 0f);
                    _pitch = Mathf.Clamp(pitch, -89f, 89f);
                    if (Camera != null)
                    {
                        Camera.transform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
                    }

                    return true;
                }
            }

            return false;
        }

        /// <summary>Capture hook: the on-foot player is standing on solid ground this frame.</summary>
        public bool IsCaptureGrounded => _controller != null && _controller.enabled && _controller.isGrounded;

        /// <summary>Capture hook: the player's head is under water (so the shot would be a submerged murk).</summary>
        public bool IsHeadUnderwater() => IsSubmerged();

        private void ApplyGravityOnly()
        {
            if (_controller.isGrounded)
            {
                _verticalVelocity = -1f;
            }
            else
            {
                _verticalVelocity -= _effGravity * Time.deltaTime;
            }

            _controller.Move(new Vector3(0f, _verticalVelocity, 0f) * Time.deltaTime);
        }

        private void LookAround()
        {
            float mx = Input.GetAxis("Mouse X") * MouseSensitivity;
            float my = Input.GetAxis("Mouse Y") * MouseSensitivity * (InvertY ? -1f : 1f);
            transform.Rotate(0f, mx, 0f);
            _pitch = Mathf.Clamp(_pitch - my, -89f, 89f);
            if (Camera != null)
            {
                Camera.transform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
            }
        }

        /// <summary>True when the player can fire the jetpack: carries one and has suit energy left.</summary>
        private bool CanJetpack() => Game != null && Game.SuitEnergy > 0f && HasItem("jetpack");

        /// <summary>Drives the jetpack thrust VFX/audio while firing and reports state edges to the server
        /// (which is authoritative for the suit-energy drain).</summary>
        private void UpdateJetpack(bool active)
        {
            if (active)
            {
                ClientAudio.Instance?.JetTick();
                if (Weapons != null)
                {
                    // Twin thrust flames at the player's feet (offset left/right of the pack).
                    var feet = transform.position + Vector3.down * 0.1f;
                    Weapons.Sparks(feet - transform.right * 0.2f, new Color(1f, 0.72f, 0.3f), 3);
                    Weapons.Sparks(feet + transform.right * 0.2f, new Color(1f, 0.55f, 0.2f), 3);
                }
            }

            if (active != _jetpackActive)
            {
                _jetpackActive = active;
                Game?.Network?.SendSetJetpack(active);
            }
        }

        /// <summary>Arcade hover driving (car-style): W/S throttle, A/D steer, Space hop, Shift boost. Holds a
        /// fixed height over the ground, can't climb steep walls (a hard stop reports a collision), and runs on
        /// the speeder's own energy cell (empty = no propulsion). F dismounts, R refuels.</summary>
        private void DriveSpeeder()
        {
            if (!_wasDriving)
            {
                _wasDriving = true;
                _speederSpeed = 0f;
                Avatar?.SetVisible(true);   // sit visibly in the speeder
                _viewmodel?.SetVisible(false);
                ClientAudio.Instance?.SpeederStart();
            }

            // Chase camera behind + above the speeder; mouse Y tilts it.
            float my = Input.GetAxis("Mouse Y") * MouseSensitivity * (InvertY ? -1f : 1f);
            _speederCamPitch = Mathf.Clamp(_speederCamPitch - my, -8f, 35f);
            if (Camera != null)
            {
                Camera.transform.localPosition = new Vector3(0f, 2.4f, -5.5f);
                Camera.transform.localEulerAngles = new Vector3(_speederCamPitch, 0f, 0f);
            }

            var driven = Game.DrivenSpeeder;
            bool outOfFuel = driven != null && driven.Fuel <= 0.01f;

            float throttle = Input.GetAxis("Vertical");   // W = +1, S = -1 (brake / reverse)
            float steer = Input.GetAxis("Horizontal");    // A = -1, D = +1
            bool boosting = Input.GetKey(KeyCode.LeftShift) && !outOfFuel && throttle > 0.1f;

            // Steering scales with speed (no pirouetting while parked).
            float speedFrac = Mathf.Clamp01(Mathf.Abs(_speederSpeed) / SpeederMaxSpeed);
            transform.Rotate(0f, steer * SpeederTurnSpeed * (0.35f + 0.65f * speedFrac) * Time.deltaTime, 0f);

            float maxSpeed = boosting ? SpeederBoostSpeed : SpeederMaxSpeed;
            float targetSpeed = outOfFuel ? 0f : (throttle >= 0f ? throttle * maxSpeed : throttle * SpeederMaxSpeed * 0.45f);
            _speederSpeed = Mathf.MoveTowards(_speederSpeed, targetSpeed, SpeederAccel * Time.deltaTime);

            // Hover: hold a fixed height above whatever ground is below; sink gently over a void/edge.
            float vSpeed;
            if (Physics.Raycast(transform.position + Vector3.up * 2.5f, Vector3.down, out var hit, 12f, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider != _controller)
            {
                float targetY = hit.point.y + SpeederHoverHeight;
                vSpeed = Mathf.Clamp((targetY - transform.position.y) * 6f, -10f, 8f);
            }
            else
            {
                vSpeed = -Gravity * 0.2f;
            }

            if (Input.GetButtonDown("Jump") && !outOfFuel)
            {
                vSpeed = SpeederHopSpeed; // a quick hover-hop over a low obstacle
            }

            Vector3 before = transform.position;
            _controller.Move((transform.forward * _speederSpeed + Vector3.up * vSpeed) * Time.deltaTime);

            // A hard horizontal stop at speed = ran into a wall/cliff → report the impact (server scales the hull
            // damage from the speed and jolts the driver).
            float wanted = Mathf.Abs(_speederSpeed);
            Vector3 moved = transform.position - before;
            moved.y = 0f;
            float actual = moved.magnitude / Mathf.Max(1e-4f, Time.deltaTime);
            if (wanted > SpeederImpactThreshold && actual < wanted * 0.45f)
            {
                Game.Network?.SendSpeederImpact(Game.InSpeeder, wanted);
                _speederSpeed *= 0.15f;
                ClientAudio.Instance?.Cue("vehicle_impact", 0.85f);
            }

            ClientAudio.Instance?.SpeederTick(speedFrac, boosting);
            Game.SpeederSpeed = _speederSpeed; // publish for the vehicle HUD speed readout

            if (Input.GetKeyDown(KeyCode.F))
            {
                Game.Network?.SendExitSpeeder();
            }
            else if (Input.GetKeyDown(KeyCode.R))
            {
                Game.Network?.SendRefuelSpeeder(Game.InSpeeder);
            }
        }

        /// <summary>Boards the nearest parked speeder the player owns within reach (on-foot E). Returns true if one
        /// was found and a board intent sent.</summary>
        private bool TryBoardNearbySpeeder()
        {
            if (Game?.Network == null || Game.Speeders == null)
            {
                return false;
            }

            string best = null;
            float bestSq = SpeederBoardRange * SpeederBoardRange;
            foreach (var s in Game.Speeders)
            {
                if (s == null || s.OwnerId != Game.LocalPlayerId || !string.IsNullOrEmpty(s.DriverId))
                {
                    continue;
                }

                float d = (Game.ScenePos(s.X, s.Y, s.Z) - transform.position).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    best = s.Id;
                }
            }

            if (best == null)
            {
                return false;
            }

            Game.Network.SendEnterSpeeder(best);
            return true;
        }

        /// <summary>Packs the nearest parked speeder the player owns back into the item (on-foot X).</summary>
        private bool TryStowNearbySpeeder()
        {
            if (Game?.Network == null || Game.Speeders == null)
            {
                return false;
            }

            string best = null;
            float bestSq = SpeederStowRange * SpeederStowRange;
            foreach (var s in Game.Speeders)
            {
                if (s == null || s.OwnerId != Game.LocalPlayerId || !string.IsNullOrEmpty(s.DriverId))
                {
                    continue;
                }

                float d = (Game.ScenePos(s.X, s.Y, s.Z) - transform.position).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    best = s.Id;
                }
            }

            if (best == null)
            {
                return false;
            }

            Game.Network.SendStowSpeeder(best);
            return true;
        }

        /// <summary>Recompute the live movement constants from this world's gravity multiplier (sent in
        /// <see cref="WorldEnvironment.GravityFactor"/>). Lighter worlds → higher jumps + faster walk; heavier
        /// worlds → still ≥1-block jumps but slower walk + faster falls. Jetpack net thrust and the safe fall
        /// distance are held constant so nothing breaks at the extremes. Cheap: the trig only runs when the
        /// factor actually changes (once per world, not per frame).</summary>
        private void RecomputeGravity()
        {
            float f = Game?.Environment != null ? Game.Environment.GravityFactor : 1f;
            if (f <= 0.05f) f = 1f;          // guard a missing/zero value — fall back to the baseline
            f = Mathf.Clamp(f, 0.2f, 2.5f);  // safety rails beyond the server's authored band
            if (Mathf.Approximately(f, _gFactor)) return;
            _gFactor = f;

            _effGravity = Gravity * f;

            // Jump: keep today's ~1.2-block jump as the FLOOR (so one block is always clearable) and let lighter
            // worlds jump proportionally higher. targetHeight = baseHeight × max(1, 1/f); impulse = √(2·g·h).
            float baseHeight = (JumpSpeed * JumpSpeed) / (2f * Gravity); // = 1.225 blocks at the inspector defaults
            float targetHeight = baseHeight * Mathf.Max(1f, 1f / f);
            _effJumpSpeed = Mathf.Sqrt(2f * _effGravity * targetHeight);

            // Walk: lighter gravity → floatier, faster strides (1/√f), clamped so it never gets silly.
            _effMoveSpeed = Mathf.Clamp(MoveSpeed / Mathf.Sqrt(f), MoveSpeed * 0.55f, MoveSpeed * 1.6f);

            // Jetpack: preserve the baseline NET thrust (accel − gravity) so it still lifts under heavy gravity
            // (a fixed 26 accel can't beat a >26 pull) and doesn't rocket away under light gravity.
            _effJetpackAccel = _effGravity + (JetpackAccel - Gravity);

            // Fall damage: scale the safe-impact speed by √f so the number of blocks you can fall unharmed stays
            // about the same regardless of how fast this world accelerates you downward.
            _effSafeFallSpeed = SafeFallSpeed * Mathf.Sqrt(f);
        }

        private void Move()
        {
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            Vector3 move = (transform.right * h + transform.forward * v) * _effMoveSpeed;

            float prevVy = _verticalVelocity; // captured before the grounded reset (for landing shake)
            bool grounded = _controller.isGrounded;
            bool inWater = IsSubmerged();
            _moving = (inWater || grounded) && (Mathf.Abs(h) + Mathf.Abs(v) > 0.1f);
            bool jetpacking = false;
            if (inWater)
            {
                // Buoyant swimming: drift down slowly when idle, hold Jump to rise and surface; water also
                // breaks a fall (the big downward speed eases out instead of slamming the seabed). No jetpack.
                float target = Input.GetButton("Jump") ? SwimUpSpeed : -SwimSinkSpeed;
                _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, target, SwimAccel * Time.deltaTime);
                move *= SwimSpeedMul;
            }
            else if (grounded)
            {
                if (Input.GetButtonDown("Jump"))
                {
                    ClientAudio.Instance?.Cue("jump", 0.6f);
                }

                _verticalVelocity = Input.GetButton("Jump") ? _effJumpSpeed : -1f;
            }
            else if (Game != null && Game.OnFootInSpace)
            {
                // Above the atmosphere there is no gravity: float, never fall. Jump rises, crouch (Ctrl/C)
                // sinks, otherwise the suit drifts to a gentle stop. (Set by item 10 — building up into space.)
                float lift = (Input.GetButton("Jump") ? SpaceFloatSpeed : 0f)
                           - ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C)) ? SpaceFloatSpeed : 0f);
                _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, lift, SpaceFloatAccel * Time.deltaTime);
            }
            else
            {
                _verticalVelocity -= _effGravity * Time.deltaTime;

                // Jetpack: hold Jump in the air to thrust upward (needs the item + suit energy). The server
                // drains energy on the reported state and forces it off when empty (SuitEnergy then hits 0).
                if (Input.GetButton("Jump") && CanJetpack())
                {
                    jetpacking = true;
                    _verticalVelocity += _effJetpackAccel * Time.deltaTime;
                    if (_verticalVelocity > JetpackMaxRise)
                    {
                        _verticalVelocity = JetpackMaxRise;
                    }
                }
            }

            UpdateJetpack(jetpacking);

            move.y = _verticalVelocity;
            _controller.Move(move * Time.deltaTime);

            // Round worlds: latitude (Z) wraps seamlessly like longitude now — the old invisible pole
            // barrier is gone. The transform runs unbounded in both axes; the server canonicalises the
            // authoritative position and chunks reposition to the nearest copy (SceneX/SceneZ).

            // Footsteps while walking on the ground; landing thud after a fall.
            if (grounded && Mathf.Abs(h) + Mathf.Abs(v) > 0.1f)
            {
                _stepTimer -= Time.deltaTime;
                if (_stepTimer <= 0f)
                {
                    _stepTimer = 0.45f;
                    ClientAudio.Instance?.Cue(SurfaceStep(), 0.45f);
                }
            }
            else
            {
                _stepTimer = 0f;
            }

            if (grounded && !_wasGrounded && !inWater)
            {
                ClientAudio.Instance?.Cue("land", 0.6f);
                Weapons?.Dust(transform.position);
                _camShake = Mathf.Max(_camShake, Mathf.Clamp01(-prevVy / 12f) * 0.7f); // impact kick

                // A hard landing hurts: report the impact speed so the server (which owns health) applies
                // fall damage. Small drops/jumps stay below the safe threshold and do nothing. Water breaks
                // the fall (you enter the swim branch instead of becoming grounded), so no splash damage.
                if (-prevVy > _effSafeFallSpeed)
                {
                    Game?.Network?.SendFallDamage(-prevVy);
                }
            }

            _wasGrounded = grounded;
        }

        /// <summary>True when the player's upper body sits in a water block — the cue to switch to swimming
        /// (sampled at chest height, so wading through shallow water still walks; only deep water swims).</summary>
        private bool IsSubmerged() => BlockKeyAt(transform.position + Vector3.up * 1.1f) == "water";

        /// <summary>The content key of the block at a world position (null if the world/content isn't ready).</summary>
        private string BlockKeyAt(Vector3 world)
        {
            if (Game?.World == null || Game.Content == null)
            {
                return null;
            }

            var def = Game.Content.BlockById(Game.World.GetBlock(
                Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y), Mathf.FloorToInt(world.z)));
            return def?.Key;
        }

        /// <summary>First-person camera feel: a subtle walking head-bob, a small forward FOV kick while
        /// moving, and a decaying shake on landing/impacts. Composed over the look pitch each frame.</summary>
        private void UpdateCameraFeel()
        {
            if (Camera == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            _camShake = Mathf.MoveTowards(_camShake, 0f, dt * 1.8f);

            if (ThirdPerson)
            {
                Camera.fieldOfView = Mathf.MoveTowards(Camera.fieldOfView, _baseFov, dt * 30f);
                return;
            }

            // Comfort: with CameraMotion off, bob/FOV-kick/shake all flatten to a steady camera.
            float motion = CameraMotion ? 1f : 0f;
            float amt = (_moving ? 1f : 0f) * motion;
            _bobPhase += dt * (_moving ? 9f : 0f) * motion;
            float bobY = Mathf.Sin(_bobPhase * 2f) * 0.035f * amt;
            float bobX = Mathf.Cos(_bobPhase) * 0.025f * amt;
            Camera.transform.localPosition = FirstPersonEye + new Vector3(bobX, bobY, 0f);

            Camera.fieldOfView = Mathf.MoveTowards(Camera.fieldOfView, _baseFov + (_moving ? 4f * motion : 0f), dt * 40f);

            float s = _camShake * motion;
            float sp = Mathf.Sin(Time.time * 80f) * s * 3f;
            float sr = Mathf.Cos(Time.time * 67f) * s * 2.5f;
            Camera.transform.localEulerAngles = new Vector3(_pitch + sp, 0f, sr);
        }

        /// <summary>Picks the footstep clip from the block under the player's feet (key heuristic).</summary>
        private string SurfaceStep()
        {
            if (Game?.World == null || Game.Content == null)
            {
                return "step_rock";
            }

            var p = transform.position;
            var def = Game.Content.BlockById(Game.World.GetBlock(
                Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y - 0.5f), Mathf.FloorToInt(p.z)));
            string k = def?.Key ?? string.Empty;
            if (k.Contains("iron") || k.Contains("metal") || k.Contains("steel")) return "step_metal";
            if (k.Contains("sand")) return "step_sand";
            if (k.Contains("grass")) return "step_grass";
            if (k.Contains("snow") || k.Contains("ice")) return "step_snow";
            return "step_rock";
        }

        /// <summary>Plays the firing/swing sound for the currently held weapon (if any).</summary>
        private void PlayWeaponSound()
        {
            var audio = ClientAudio.Instance;
            if (audio == null)
            {
                return;
            }

            switch (Game.ItemInSlot(Game.SelectedHotbarSlot))
            {
                case "gauss_pistol": audio.Cue("weapon_gauss"); break;
                case "laser_pistol": audio.Cue("weapon_laser"); break;
                case "plasma_blaster": audio.Cue("weapon_plasma"); break;
                default: audio.Cue("melee_swing"); break; // melee weapons, tools, fists
            }
        }

        private void HandleInteract()
        {
            if (Game?.Network == null || Camera == null)
            {
                return;
            }

            bool mine = Input.GetMouseButtonDown(0);
            bool place = Input.GetMouseButtonDown(1);
            if (!mine && !place)
            {
                return;
            }

            // Holding a scanner turns the primary action into a scan (select it in the hotbar, then aim + click).
            if (mine && HoldingScanner())
            {
                ScanTarget();
                return;
            }

            // Holding a weapon turns the primary action (left-click) into an attack — the same swing as F —
            // so a melee weapon like the machete actually hits creatures instead of trying to mine a block.
            // Gated by the weapon's swing cooldown so it can't be spam-clicked (the machete's 1.5s, etc.).
            if (mine && HoldingWeapon())
            {
                if (WeaponSwingReady())
                {
                    AttackNearestEnemy();
                    TriggerSwing();
                }

                return;
            }

            // Right-click a held consumable → eat/use it (no aiming needed); the server applies the effect and
            // the client plays an eat sound. Consumables don't place a block, so right-click is free for this (B16).
            if (place)
            {
                string held = Game.ItemInSlot(Game.SelectedHotbarSlot);
                var hdef = string.IsNullOrEmpty(held) ? null : Game.Content?.GetItem(held);
                if (hdef != null && hdef.Category.ToString() == "Consumable")
                {
                    Game.Network?.SendConsume(held);
                    ClientAudio.Instance?.Cue("eat");
                    return;
                }

                // Right-click the camera → photograph the current view (HUD-free), saved to disk locally.
                // Handled entirely on the client (no server round-trip), so it's intercepted before the
                // generic gadget path below.
                if (held == "camera")
                {
                    EnsureCameraTool().TryCapture();
                    return;
                }

                // Right-click a held gadget (item 36) → use it: the medkit heals around you, the stasis
                // projector + terrain blaster act at the aim point. The server validates energy + cooldown.
                if (hdef?.Tool != null && hdef.Tool.Kind == BlocksBeyondTheStars.Shared.Definitions.ToolKind.Gadget)
                {
                    UseGadget(held);
                    return;
                }
            }

            // Target the block under the crosshair by ray-marching the voxel world itself, not a Physics.Raycast.
            // The collider is a mesh that gets rebuilt right after every dig; a raycast against it can silently
            // miss a block that's clearly there (the rebuild's re-cook, a seam, or just its shorter reach) — which
            // is exactly the "I aim at the next block and nothing happens" bug (B32). The voxel grid is the source
            // of truth and always in sync, so this never silently fails when a block is in front of you.
            // Parked ship OBJECTS (ship-as-object) live outside the world grid, so the march tests them too.
            if (!AimTarget(out var hitCell, out var placeCell, out var aimedShip))
            {
                return;
            }

            if (mine)
            {
                if (aimedShip != null)
                {
                    // A parked ship's cell: a structure edit, not a world dig. The server enforces the rules
                    // (hull + modules protected; only player-added blocks come out again).
                    var l = ShipLocal(aimedShip, hitCell);
                    Game.Network.SendStructureEdit(aimedShip.StructureId, l.x, l.y, l.z, mine: true);
                    TriggerSwing();
                    return;
                }

                Game.LastMineCell = hitCell; // so an "already empty" rejection can clear the ghost here (B32)
                Game.Network.SendMine(hitCell.x, hitCell.y, hitCell.z);
                TriggerSwing();
            }
            else
            {
                // Place the item in the selected hotbar slot — only if it actually places a
                // block (tools like the drill/scanner don't), so we don't spam server rejects.
                string item = Game.ItemInSlot(Game.SelectedHotbarSlot);
                var def = string.IsNullOrEmpty(item) ? null : Game.Content?.GetItem(item);
                if (def != null && !string.IsNullOrEmpty(def.PlacesBlock))
                {
                    // Placing INSIDE a parked ship furnishes the cabin: route to a structure edit (the
                    // block becomes part of the ship and persists with it), not a world place.
                    var boundsShip = Game.LandedShipBoundsAt(placeCell.x, placeCell.y, placeCell.z, out var lp);
                    if (boundsShip != null)
                    {
                        Game.Network.SendStructureEdit(boundsShip.StructureId, lp.X, lp.Y, lp.Z, mine: false, item);
                        TriggerSwing();
                        return;
                    }

                    if (def.PlacesBlock == "radio_beacon" && BeaconLabelUi.Instance != null)
                    {
                        // Name the beacon before placing it — the typed label travels with the place (item 37).
                        var cell = placeCell;
                        BeaconLabelUi.Instance.Open(
                            Game.Localizer?.Get("ui.beacon.name_prompt") ?? "Name this beacon",
                            string.Empty,
                            label => Game.Network.SendPlace(cell.x, cell.y, cell.z, item, label));
                    }
                    else if (def.PlacesBlock == "beam_block" && BeaconLabelUi.Instance != null)
                    {
                        // Name the beam block before placing it — the typed name travels with the place (teleporter pad).
                        var cell = placeCell;
                        BeaconLabelUi.Instance.Open(
                            Game.Localizer?.Get("ui.beam.name_prompt") ?? "Name this beam block",
                            string.Empty,
                            label => Game.Network.SendPlace(cell.x, cell.y, cell.z, item, label));
                    }
                    else
                    {
                        Game.Network.SendPlace(placeCell.x, placeCell.y, placeCell.z, item);
                    }

                    TriggerSwing();
                }
            }
        }

        /// <summary>A world cell mapped into a parked ship's structure-local grid (wrap-aware on X).</summary>
        private Vector3Int ShipLocal(LandedShipModel ship, Vector3Int worldCell)
            => new Vector3Int(
                BlocksBeyondTheStars.Shared.World.WorldConstants.WrapDeltaX(worldCell.x - ship.Origin.X, Game.Circumference),
                worldCell.y - ship.Origin.Y,
                worldCell.z - ship.Origin.Z);

        /// <summary>Like <see cref="AimBlock"/>, but the march also targets the cells of parked ship OBJECTS
        /// (ship-as-object): whichever solid cell the ray reaches first wins. <paramref name="ship"/> is set
        /// when the hit belongs to a parked ship — mine/place then route to a structure edit.</summary>
        private bool AimTarget(out Vector3Int hitCell, out Vector3Int placeCell, out LandedShipModel ship)
        {
            hitCell = default;
            placeCell = default;
            ship = null;
            if (Game?.World == null || Camera == null)
            {
                return false;
            }

            Vector3 o = Camera.transform.position;
            Vector3 dir = Camera.transform.forward;
            int x = Mathf.FloorToInt(o.x), y = Mathf.FloorToInt(o.y), z = Mathf.FloorToInt(o.z);
            int px = x, py = y, pz = z;

            int sx = dir.x >= 0 ? 1 : -1, sy = dir.y >= 0 ? 1 : -1, sz = dir.z >= 0 ? 1 : -1;
            float invx = Mathf.Abs(dir.x) > 1e-6f ? 1f / Mathf.Abs(dir.x) : float.PositiveInfinity;
            float invy = Mathf.Abs(dir.y) > 1e-6f ? 1f / Mathf.Abs(dir.y) : float.PositiveInfinity;
            float invz = Mathf.Abs(dir.z) > 1e-6f ? 1f / Mathf.Abs(dir.z) : float.PositiveInfinity;
            float tMaxX = float.IsInfinity(invx) ? float.PositiveInfinity : (dir.x > 0 ? (x + 1 - o.x) : (o.x - x)) * invx;
            float tMaxY = float.IsInfinity(invy) ? float.PositiveInfinity : (dir.y > 0 ? (y + 1 - o.y) : (o.y - y)) * invy;
            float tMaxZ = float.IsInfinity(invz) ? float.PositiveInfinity : (dir.z > 0 ? (z + 1 - o.z) : (o.z - z)) * invz;

            float t = 0f;
            for (int i = 0; i < 80 && t <= Reach; i++)
            {
                var id = Game.World.GetBlock(x, y, z);
                if (!id.IsAir && !IsFluidBlock(id))
                {
                    hitCell = new Vector3Int(x, y, z);
                    placeCell = new Vector3Int(px, py, pz);
                    return true;
                }

                if (!Game.LandedShipBlockAt(x, y, z, out var s, out _).IsAir)
                {
                    hitCell = new Vector3Int(x, y, z);
                    placeCell = new Vector3Int(px, py, pz);
                    ship = s;
                    return true;
                }

                px = x; py = y; pz = z;
                if (tMaxX <= tMaxY && tMaxX <= tMaxZ) { x += sx; t = tMaxX; tMaxX += invx; }
                else if (tMaxY <= tMaxZ) { y += sy; t = tMaxY; tMaxY += invy; }
                else { z += sz; t = tMaxZ; tMaxZ += invz; }
            }

            return false;
        }

        /// <summary>Ray-marches the voxel grid (Amanatides &amp; Woo) along the aim ray and returns the first
        /// targetable block within <see cref="Reach"/> — <paramref name="hitCell"/> is the block to mine, and
        /// <paramref name="placeCell"/> the empty cell just before its hit face (where a placed block goes).
        /// Fluids (water/lava) are passed through, matching the collider (which excludes them). Cells are in the
        /// same space the dig intents use; the server + <see cref="ClientWorld"/> both wrap X, so the seam is fine.</summary>
        private bool AimBlock(out Vector3Int hitCell, out Vector3Int placeCell, bool includeFluids = false)
        {
            hitCell = default;
            placeCell = default;
            if (Game?.World == null || Camera == null)
            {
                return false;
            }

            Vector3 o = Camera.transform.position;
            Vector3 dir = Camera.transform.forward;
            int x = Mathf.FloorToInt(o.x), y = Mathf.FloorToInt(o.y), z = Mathf.FloorToInt(o.z);
            int px = x, py = y, pz = z;

            int sx = dir.x >= 0 ? 1 : -1, sy = dir.y >= 0 ? 1 : -1, sz = dir.z >= 0 ? 1 : -1;
            float invx = Mathf.Abs(dir.x) > 1e-6f ? 1f / Mathf.Abs(dir.x) : float.PositiveInfinity;
            float invy = Mathf.Abs(dir.y) > 1e-6f ? 1f / Mathf.Abs(dir.y) : float.PositiveInfinity;
            float invz = Mathf.Abs(dir.z) > 1e-6f ? 1f / Mathf.Abs(dir.z) : float.PositiveInfinity;
            // Parametric distance to the first cell boundary on each axis.
            float tMaxX = float.IsInfinity(invx) ? float.PositiveInfinity : (dir.x > 0 ? (x + 1 - o.x) : (o.x - x)) * invx;
            float tMaxY = float.IsInfinity(invy) ? float.PositiveInfinity : (dir.y > 0 ? (y + 1 - o.y) : (o.y - y)) * invy;
            float tMaxZ = float.IsInfinity(invz) ? float.PositiveInfinity : (dir.z > 0 ? (z + 1 - o.z) : (o.z - z)) * invz;

            float t = 0f;
            for (int i = 0; i < 80 && t <= Reach; i++)
            {
                var id = Game.World.GetBlock(x, y, z);
                if (!id.IsAir && (includeFluids || !IsFluidBlock(id)))
                {
                    hitCell = new Vector3Int(x, y, z);
                    placeCell = new Vector3Int(px, py, pz);
                    return true;
                }

                px = x; py = y; pz = z;
                if (tMaxX <= tMaxY && tMaxX <= tMaxZ) { x += sx; t = tMaxX; tMaxX += invx; }
                else if (tMaxY <= tMaxZ) { y += sy; t = tMaxY; tMaxY += invy; }
                else { z += sz; t = tMaxZ; tMaxZ += invz; }
            }

            return false;
        }

        /// <summary>Water/lava are passed through when aiming (they have no collider — you swim/sink into them).</summary>
        private bool IsFluidBlock(BlocksBeyondTheStars.Shared.Primitives.BlockId id)
        {
            var key = Game.Content?.BlockById(id)?.Key;
            return key is "water" or "lava";
        }

        private void SendMovement()
        {
            _moveSendTimer += Time.deltaTime;
            if (_moveSendTimer < 0.1f || Game?.Network == null)
            {
                return; // ~10 position updates per second (unreliable channel)
            }

            _moveSendTimer = 0f;
            Game.Network.SendMove(transform.position, transform.eulerAngles.y, _pitch);
        }

        private static Vector3Int FloorVec(Vector3 v)
            => new Vector3Int(Mathf.FloorToInt(v.x), Mathf.FloorToInt(v.y), Mathf.FloorToInt(v.z));
    }
}
