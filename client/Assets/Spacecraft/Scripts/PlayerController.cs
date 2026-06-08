using Spacecraft.Shared.World;
using UnityEngine;

namespace Spacecraft.Client
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
        public bool ThirdPerson = false;
        public float Reach = 8f; // match the server's MaxReach (8) — a shorter client reach left a silent dead-band (B32)

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
            var (kind, tint) = HeldItem.For(Game?.Content, key);
            Avatar?.SetHeldItem(kind, tint);
            _viewmodel?.SetHeldItem(kind, tint);
        }

        private void Update()
        {
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
                _settleTimer += Time.deltaTime;

                // Solid ground loaded somewhere below the spawn? (the chunk's MeshCollider exists)
                bool groundBelow = Physics.Raycast(_spawnPos + Vector3.up * 0.5f, Vector3.down, out var gHit, 10f)
                                   && gHit.collider != _controller;

                // Reveal the world (dismiss the loading overlay) as soon as the ground is under us, or after a
                // short grace period even if the spawn chunk is still catching up — so the overlay never overstays
                // and the player isn't left staring at a "Loading world" screen (B39).
                if (!_worldRevealed && (groundBelow || _settleTimer > 8f))
                {
                    _worldRevealed = true;
                    Game.NotifyWorldReady();
                }

                // Release on-foot control ONLY once there is real ground under the spawn — never drop the player
                // into the bottomless void if the chunk is late (the B39 softlock was an unconditional release
                // that let a slow spawn-chunk become an endless fall). As an absolute last resort after a long
                // wait, release anyway and let the server's runtime void-rescue teleport us back to safe ground,
                // so a never-arriving chunk can't freeze us at spawn forever either.
                if (groundBelow || _settleTimer > 30f)
                {
                    _settling = false;
                    _settleTimer = 0f;
                }
                else
                {
                    return; // stay frozen at spawn (no fall) until the ground chunk streams in
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

            if (Input.GetKeyDown(KeyCode.V))
            {
                ThirdPerson = !ThirdPerson;
                ApplyCameraMode();
            }

            RefreshHeldItem();

            if (Input.GetKeyDown(KeyCode.F))
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
                Shader.SetGlobalColor(LampColId, new Color(1.6f, 1.5f, 1.3f, 1f));    // warm, HDR intensity
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
            var shader = Shader.Find("Spacecraft/Cloud") ?? Shader.Find("Unlit/Color");
            mr.sharedMaterial = new Material(shader) { color = new Color(1f, 0.94f, 0.76f, 0.06f) };
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
                && Game.Content?.GetItem(held)?.Tool?.Kind == Spacecraft.Shared.Definitions.ToolKind.Drill;
        }

        /// <summary>True if the selected hotbar item is a handheld scanner (its primary action scans).</summary>
        private bool HoldingScanner()
        {
            string held = Game.ItemInSlot(Game.SelectedHotbarSlot);
            return !string.IsNullOrEmpty(held)
                && Game.Content?.GetItem(held)?.Tool?.Kind == Spacecraft.Shared.Definitions.ToolKind.Scanner;
        }

        /// <summary>True if the selected hotbar item is a weapon (its primary action attacks, like F).</summary>
        private bool HoldingWeapon()
        {
            string held = Game.ItemInSlot(Game.SelectedHotbarSlot);
            return !string.IsNullOrEmpty(held)
                && Game.Content?.GetItem(held)?.Tool?.Kind == Spacecraft.Shared.Definitions.ToolKind.Weapon;
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

            if (string.IsNullOrEmpty(Game.NearbyStation))
            {
                // No station here — maybe a hinge door to open/close (sci-fi sliders open themselves).
                int door = DoorView.Instance != null ? DoorView.Instance.NearestHinge(transform.position, 3f) : 0;
                if (door != 0)
                {
                    Game.Network?.SendDoorInteract(door);
                }

                return;
            }

            // Stations that open a client UI panel; the rest are resolved server-side.
            switch (Game.NearbyStation)
            {
                case "cockpit": Menu?.OpenMap(); break;
                case "workshop": Menu?.OpenCrafting(); break;
                case "market": Menu?.OpenMarket(); break;
                case "cargo": Menu?.OpenInventory(); break;
                default:
                    if (Game.NearbyStation == "medbay") ClientAudio.Instance?.Cue("heal");
                    Game.Network?.SendUseStation(Game.NearbyStation);
                    break; // medbay, quarters
            }
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

        private void ApplyGravityOnly()
        {
            if (_controller.isGrounded)
            {
                _verticalVelocity = -1f;
            }
            else
            {
                _verticalVelocity -= Gravity * Time.deltaTime;
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

        private void Move()
        {
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            Vector3 move = (transform.right * h + transform.forward * v) * MoveSpeed;

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

                _verticalVelocity = Input.GetButton("Jump") ? JumpSpeed : -1f;
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
                _verticalVelocity -= Gravity * Time.deltaTime;

                // Jetpack: hold Jump in the air to thrust upward (needs the item + suit energy). The server
                // drains energy on the reported state and forces it off when empty (SuitEnergy then hits 0).
                if (Input.GetButton("Jump") && CanJetpack())
                {
                    jetpacking = true;
                    _verticalVelocity += JetpackAccel * Time.deltaTime;
                    if (_verticalVelocity > JetpackMaxRise)
                    {
                        _verticalVelocity = JetpackMaxRise;
                    }
                }
            }

            UpdateJetpack(jetpacking);

            move.y = _verticalVelocity;
            _controller.Move(move * Time.deltaTime);

            // Pole barrier: longitude wraps but latitude doesn't, so bound Z with an invisible wall at
            // ±LatitudeLimit (you slide along it). Stations/space use tiny coords, so this never bites there.
            float zl = WorldConstants.LatitudeLimit;
            if (transform.position.z > zl || transform.position.z < -zl)
            {
                var pp = transform.position;
                pp.z = Mathf.Clamp(pp.z, -zl, zl);
                transform.position = pp;
            }

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
                if (-prevVy > SafeFallSpeed)
                {
                    Game?.Network?.SendFallDamage(-prevVy);
                }
            }

            _wasGrounded = grounded;
        }

        /// <summary>True when the player's upper body sits in a water block — the cue to switch to swimming
        /// (sampled at chest height, so wading through shallow water still walks; only deep water swims).</summary>
        private bool IsSubmerged()
        {
            if (Game?.World == null || Game.Content == null)
            {
                return false;
            }

            var c = transform.position + Vector3.up * 1.1f;
            var def = Game.Content.BlockById(Game.World.GetBlock(
                Mathf.FloorToInt(c.x), Mathf.FloorToInt(c.y), Mathf.FloorToInt(c.z)));
            return def?.Key == "water";
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

            float amt = _moving ? 1f : 0f;
            _bobPhase += dt * (_moving ? 9f : 0f);
            float bobY = Mathf.Sin(_bobPhase * 2f) * 0.035f * amt;
            float bobX = Mathf.Cos(_bobPhase) * 0.025f * amt;
            Camera.transform.localPosition = FirstPersonEye + new Vector3(bobX, bobY, 0f);

            Camera.fieldOfView = Mathf.MoveTowards(Camera.fieldOfView, _baseFov + (_moving ? 4f : 0f), dt * 40f);

            float s = _camShake;
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
            if (mine && HoldingWeapon())
            {
                AttackNearestEnemy();
                TriggerSwing();
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

                // Right-click a held gadget (item 36) → use it: the medkit heals around you, the stasis
                // projector + terrain blaster act at the aim point. The server validates energy + cooldown.
                if (hdef?.Tool != null && hdef.Tool.Kind == Spacecraft.Shared.Definitions.ToolKind.Gadget)
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
            if (!AimBlock(out var hitCell, out var placeCell))
            {
                return;
            }

            if (mine)
            {
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
                    Game.Network.SendPlace(placeCell.x, placeCell.y, placeCell.z, item);
                    TriggerSwing();
                }
            }
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
        private bool IsFluidBlock(Spacecraft.Shared.Primitives.BlockId id)
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
