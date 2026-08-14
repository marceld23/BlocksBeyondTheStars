// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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

        /// <summary>Infrared overlay for the upgraded binoculars (wired by <see cref="WorldRig"/>); handed to
        /// the optic when the player first raises it.</summary>
        public ThermalVision Thermal;

        public float MoveSpeed = 6f;
        public float JumpSpeed = 7f;
        public float Gravity = 20f;
        public float SafeFallSpeed = 14f; // impact speed you can land at unharmed (~5 blocks); faster hurts
        public float JetpackAccel = 26f;   // upward acceleration while the jetpack fires
        public float JetpackMaxRise = 6.5f; // cap on jetpack-driven rise speed

        /// <summary>How hard a full gale pushes a hovering (jetpacking) player downwind, in m/s (#900).
        /// Deliberately small: readable drift, never a loss of control.</summary>
        private const float WindDriftPerSecond = 3.2f;
        // Zero-g (above the atmosphere): float instead of fall — Jump rises, crouch sinks, else drift to a stop.
        public float SpaceFloatSpeed = 4f;
        public float SpaceFloatAccel = 14f;
        // Swimming: in water the player drifts down slowly and holds Jump to rise / surface (no fast falls).
        public float SwimUpSpeed = 4f;     // rise speed while holding Jump underwater
        public float SwimSinkSpeed = 1.5f; // gentle idle sink toward the seabed
        public float SwimAccel = 12f;      // how fast vertical speed eases toward the swim target
        public float SwimSpeedMul = 0.62f; // horizontal movement is slower in water
        public float ClimbSpeed = 4f;      // up/down speed while on a ladder (Minecraft-style climbing, #126)
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

        // Crouch / sneak (hold Ctrl or C on the ground): shrink the capsule so you fit tight spaces, walk slower,
        // and — crucially — refuse to step off a ledge. That edge-stop lets you lean out over an edge and lay a
        // bridging block against the side of the block you stand on (Severin playtest #2: "I miss crouching from
        // Minecraft" / "I can't build forward beneath me to make a bridge").
        private const float StandHeight = 1.8f;
        private const float CrouchHeight = 1.2f;
        private const float CrouchSpeedMul = 0.4f;
        private static readonly Vector3 CrouchEye = new Vector3(0f, 1.0f, 0f);
        private bool _crouched;
        private float _crouchT; // 0 = standing, 1 = fully crouched (eases the camera; the collider snaps)

        // Creative flight (#836). Only offered when the SERVER says this world allows it (GameBootstrap.CanFly:
        // Creative/Sandbox worlds, or the /fly cheat) — the client never grants itself flight.
        //
        // Double-tapping jump toggles it, because that is the gesture every Minecraft player already has in
        // their fingers, and the command it replaces (/fly) was so undiscoverable that a tester asked for the
        // feature in capitals while sitting in a Creative world that technically had it.
        private const float FlySpeed = 9f;          // vertical blocks/s while holding jump / crouch
        private const float FlyHorizontalMul = 1.8f; // flying is a way to COVER ground, so it outruns walking
        private const float FlyDoubleTapWindow = 0.35f;
        private bool _flying;
        private float _lastJumpTapTime = -1f;

        // Sit on a chair-shaped cell (#806): E on a chair seats the player — control freezes (the look
        // stays free), the eye eases to seat height, and the Seated flag rides the presence broadcast so
        // other players see the pose. The CharacterController is disabled while seated: the capsule sits
        // INSIDE the chair cell and must not fight the seat's own collider boxes.
        private static readonly Vector3 SeatedEye = new Vector3(0f, 1.05f, 0f);
        private Vector3Int? _seatCell;
        private int _satFrame; // debounce: the E that sat us down must not also stand us up

        private CharacterController _controller;
        private float _pitch;
        // Placement orientation override for shaped building blocks: -1 = auto (the server orients from the
        // surface built against), 0..5 = a forced up-face cycled with the RotateShape key. Sent on each place.
        private int _placeUpFace = -1;

        /// <summary>Explicit quarter-turn (0..3) for the next shaped placement; -1 = derive it from facing.</summary>
        private int _placeYaw = -1;

        // The translucent placement preview (#863) + the frame it was last refreshed. The on-foot update
        // stamps the frame; LateUpdate hides the ghost whenever a frame went by without a stamp (menu open,
        // space view, driving, seated, …) so no stale hologram lingers over the world.
        private PlacementGhost _placementGhost;
        private int _ghostFrame = -1;
        private float _verticalVelocity;
        private float _moveSendTimer;
        private bool _spawned;
        private Vector3 _spawnPos;
        private bool _settling;
        private float _settleTimer; // how long we've been frozen at spawn waiting for the floor to stream
        private bool _worldRevealed; // settle: has the loading overlay been dismissed for this spawn yet
        private bool _awaitingFloor;   // released on the grace timer with no floor yet — hover instead of falling
        private float _awaitFloorTimer;

        // View-settle gate (#390): hold the reveal until the streamed view has finished arriving AND meshing, so
        // the world doesn't visibly assemble after the veil lifts. "No new chunk for this long" is the reliable
        // "server finished the frozen spawn view" signal (streaming keeps chunks arriving every tick); the backlog
        // check confirms those last arrivals are meshed. The spawn grace below still hard-caps the wait.
        private const float ViewSettleQuietSeconds = 0.6f;
        private const int ViewSettleBacklog = 6; // ~a frame's worth of the mesh budget (MeshChunksPerFrame)

        /// <summary>How long the spawn freeze may hold the veil up before the world is revealed regardless.</summary>
        private const float SettleGraceSeconds = 8f;

        /// <summary>Upper bound on hovering while waiting for a floor that never arrives (#773). Only reached
        /// when the spawn chunk truly never streams; the server's void rescue then takes over as before.</summary>
        private const float AwaitFloorMaxSeconds = 30f;
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
            if (ThirdPerson)
            {
                _optic?.Lower(); // the scope is a first-person instrument; UpdateCameraFeel ignores zoom in 3rd
            }

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
            _optic?.SetHeldItem(key); // swapping away from the binoculars can never strand a zoomed view
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
                _awaitingFloor = false;
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
                _awaitingFloor = false; // the freeze owns us again; the release below re-decides
            }

            // On death the server respawns us at the ship's heal-tank — teleport the body there.
            if (Game != null && Game.RespawnTarget.HasValue)
            {
                _spawnPos = Game.RespawnTarget.Value;
                SnapTo(_spawnPos);
                Game.RespawnTarget = null;
                _spawned = true; // a server snap is an authoritative position — never wait for ServerSpawn past it
                _settling = true; // hold at the heal-tank until its chunk is streamed
                _settleTimer = 0f;
                _worldRevealed = false;
                _awaitingFloor = false;
            }

            // Until the server has told us where we are, the controller must not exist as far as the
            // simulation is concerned: the scene-default transform sits near the world origin, and letting
            // gravity + SendMovement run from there streamed that bogus position to the server at 10 Hz —
            // which trusted it, overwrote the freshly computed ship spawn, and left new players entombed in
            // the origin column for the void rescue to dig into a random cave (#865, the root cause of #834).
            if (!_spawned && Game != null)
            {
                UpdateJetpack(false);
                return;
            }

            // Hold the player frozen at the spawn (no gravity, no control, no movement sent) until the
            // floor chunk's collider has actually streamed in below them — then release. Reacting only
            // after a fall let a far teleport (boarding a station, travel) drop through while chunks loaded.
            if (_settling && Game != null)
            {
                // The settle freeze was the one early-return that skipped this: jetpack-thrusting onto a
                // beam pad froze the player with the jetpack still "on" server-side, draining suit energy
                // the whole wait (#413 N3).
                UpdateJetpack(false);
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

                // The streamed view has finished arriving AND meshing — so the reveal shows a populated world
                // instead of one that visibly assembles over the next few seconds (#390). While the server is
                // still streaming, chunks keep arriving each tick and reset TimeSinceLastChunk; the gap only opens
                // once the frozen spawn view is complete, and the backlog check confirms the last ones are meshed.
                bool viewSettled = Game.TimeSinceLastChunk >= ViewSettleQuietSeconds
                                   && Game.PendingMeshCount <= ViewSettleBacklog;

                // Reveal the world + release control once there is real ground under the spawn AND the view has
                // settled, or after a short grace so the veil never lingers or feels stuck. Releasing on that
                // grace alone used to hand the player straight into free fall through terrain that had not
                // streamed yet — an 8-second drop into the void followed by the server's rescue teleports, which
                // is what a slow (browser) client got on every first join (#773). Gravity is therefore held off
                // separately until a floor actually exists: the veil lifts on time, the player just doesn't fall.
                if (!awaitingConfirm && ((groundBelow && viewSettled) || _settleTimer > SettleGraceSeconds))
                {
                    if (!_worldRevealed)
                    {
                        _worldRevealed = true;
                        Game.NotifyWorldReady();
                    }

                    _awaitingFloor = !groundBelow;
                    _awaitFloorTimer = 0f;
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
                // Entering space skips the per-frame UpdateLamp() below, so the suit headlamp's
                // shader global would otherwise stay lit. Turn it off once on the way in.
                if (_lampOn)
                {
                    _lampOn = false;
                    UpdateLamp();
                }
                return;
            }

            // A UI panel or the chat input is open: don't steer/interact, just settle by gravity — unless
            // we're driving a speeder: on-foot gravity would drag the player out of hover under the menu
            // and yank them back up on close (#413 N4). Hold the hover position instead. A running camera
            // cinematic (#760) freezes control the same way — and by skipping the per-frame eye-pose
            // re-assert below, it leaves the camera to the cinematic's LateUpdate.
            if (Game != null && (Game.MenuOpen || Game.ChatTyping || Game.CinematicCameraActive))
            {
                UpdateJetpack(false);
                if (string.IsNullOrEmpty(Game.InSpeeder) && _seatCell is null)
                {
                    ApplyGravityOnly(); // seated: the controller is disabled and the chair holds us anyway
                }

                // Keep the position stream flowing (position simply frozen): behind an open panel this is
                // the client's ONLY payload, and the server drops sessions silent for 90 s (#964) — browsing
                // the crafting menu or painting an avatar must not read as a dead client (#1008).
                SendMovement();
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

            // Sitting on a chair (#806): control frozen, look free. Stand with E/jump/crouch/movement —
            // or when the chair vanishes under us (mined, reshaped, chunk unloaded).
            if (_seatCell is { } seat)
            {
                UpdateJetpack(false);
                LookAround();
                if (Camera != null && !ThirdPerson)
                {
                    Camera.transform.localPosition = Vector3.Lerp(Camera.transform.localPosition, SeatedEye, Time.deltaTime * 8f);
                }

                bool chairGone = Game?.World == null
                    || Game.Health <= 0f // dying stands you up so the respawn teleport gets a live controller
                    || ShapeCode.ShapeOf(Game.World.GetShape(seat.x, seat.y, seat.z)) != (int)BlockShape.Chair;
                bool wantsUp = Time.frameCount != _satFrame
                    && (InputMap.JumpDown() || InputMap.CrouchHeld() || InputMap.Down(InputAction.Interact)
                        || Mathf.Abs(InputMap.MoveX()) > 0.3f || Mathf.Abs(InputMap.MoveY()) > 0.3f);
                if (chairGone || wantsUp)
                {
                    StandUp();
                }

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
            if (InputMap.Down(InputAction.Interact) && TryBoardNearbySpeeder())
            {
                return;
            }

            if (InputMap.Down(InputAction.StowVehicle) && TryStowNearbySpeeder())
            {
                return;
            }

            if (InputMap.Down(InputAction.ToggleThirdPerson))
            {
                ThirdPerson = !ThirdPerson;
                ApplyCameraMode();
            }

            // Rotate the held building shape's placement orientation. The key walks the orientations in the order
            // a builder actually wants them: first the four QUARTER TURNS about the current up-face (turning a
            // staircase to face another way — what a player asked for: "Ich will das Treppen in verschiedenen
            // Winkeln platzierbar sind"), then on to the next up-face, and finally back to Auto. Shift+R walks
            // the same cycle backwards (#863) — one overshoot no longer costs a full lap through 24 states.
            //
            // Yaw used to be taken solely from where the player was looking, so getting the turn you wanted meant
            // standing in a particular direction — which does not work when you are building into a corner. The
            // shape descriptor has always stored yaw × up-face (24 orientations); this just hands the player the
            // controls. Furniture (bed/campfire/rug/pot) cycles too, but yaw-only: the server pins its up-face
            // to +Y so sit/heal/warmth keep working — the cycle mirrors that instead of promising a tip that
            // the place would ignore. The ladder gets its own five-state cycle (#909): the four walls it can
            // hug plus free-standing, because quarter turns of its square plate are four identical states.
            // Only cycles for a rotatable block, so it never clashes with RepairWreck on the same key.
            if (InputMap.Down(InputAction.RotateShape))
            {
                string held = Game != null ? Game.ItemInSlot(Game.SelectedHotbarSlot) : null;
                if (HeldPlaceShape(held, out var heldCycle) > 0)
                {
                    bool backwards = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                    StepPlaceOrientation(backwards, heldCycle);
                    Game.ShowMessage(string.Format(
                        Game.Localizer?.Get("hud.shape.orient") ?? "Shape orientation: {0}",
                        OrientationLabel(heldCycle)));
                }
            }

            RefreshHeldItem();

            if (InputMap.Down(InputAction.PrimaryFire) && WeaponSwingReady())
            {
                AttackNearestEnemy();
                TriggerSwing();
            }

            if (InputMap.Down(InputAction.LootContainer))
            {
                LootNearestContainer();
            }

            if (InputMap.Down(InputAction.DepositToCrate))
            {
                DepositToNearestCrate();
            }

            if (InputMap.Down(InputAction.RepairWreck))
            {
                RepairWreckCell();
            }

            if (InputMap.Down(InputAction.ToggleLamp))
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
            UpdatePlacementGhost();
            HandleDrillAudio();
            UpdateGearPeriodically();
            SendMovement();
            UpdateEnemyAim();

            // Publish local pose for the HUD minimap/compass.
            if (Game != null)
            {
                Game.PlayerPosition = transform.position;
                Game.PlayerYaw = transform.eulerAngles.y;
                HandleStations();
            }
        }

        // Crosshair aiming (#693): auto-aim acquires inside a forward cone only (never behind the back);
        // a melee swing sweeps a wider arc; with the AutoAim world rule OFF a ranged shot needs a genuine
        // crosshair hit — nothing under the reticle means the shot misses.
        private const float AutoAimCone = 0.819f; // cos ~35°
        private const float MeleeCone = 0.5f;     // cos ~60°

        private void AttackNearestEnemy()
        {
            if (Game?.Network == null)
            {
                return;
            }

            PlayWeaponSound();

            // Reach follows the equipped weapon: a ranged weapon must let you hit at its full range, not the bare
            // melee reach — otherwise a "gun" only ever fires point-blank (where the enemy's own bite already
            // reaches you). Melee weapons / fists stay at the default 6-block reach.
            var heldTool = Game.Content?.GetItem(Game.ItemInSlot(Game.SelectedHotbarSlot))?.Tool;
            float reach = heldTool != null && heldTool.Kind == BlocksBeyondTheStars.Shared.Definitions.ToolKind.Weapon
                ? Mathf.Max(6f, heldTool.Range)
                : 6f;
            var kind = HeldWeaponFx();
            bool melee = kind == WeaponFxKind.Melee;

            // The entity under the crosshair always wins — it is what the player is looking at.
            string targetId = null;
            Vector3 targetPos = default;
            if (AimEnemy(reach, out var aimId, out var aimPos, out float terrainDist))
            {
                targetId = aimId;
                targetPos = aimPos;
            }
            else if (melee || Game.AutoAimOn)
            {
                // Auto-aim (and every melee swing): nearest target inside the forward cone. The old
                // 360° nearest-anywhere selection is gone — no more kills behind your back.
                targetId = BestConeTarget(reach, melee ? MeleeCone : AutoAimCone, out targetPos);
            }

            var ct = Camera != null ? Camera.transform : transform;
            if (targetId != null)
            {
                var f = ct.forward;
                Game.Network.SendAttackEntity(targetId,
                    new BlocksBeyondTheStars.Shared.Geometry.Vector3f(f.x, f.y, f.z));
                Game.LastShotTargetId = targetId;
                Game.LastShotTime = Time.time;
            }
            else if (!melee && heldTool != null && heldTool.Ignites
                     && TerrainHit(ct.position, ct.forward, reach, out var burnCell, out _))
            {
                // A laser/plasma bolt that hits terrain instead of a creature can set it alight (#788). Only
                // the impact cell travels; the server decides whether anything actually burns there.
                Game.Network.SendShootBlock(burnCell.x, burnCell.y, burnCell.z);
            }

            if (Weapons != null && Camera != null)
            {
                var from = ct.position + ct.forward * 0.4f - ct.up * 0.15f;
                var col = WeaponColor();
                if (kind == WeaponFxKind.Melee)
                {
                    // A melee slash sweeps whether or not it connects (whiff still reads).
                    Weapons.MeleeArc(from, ct.forward, ct.up, col);
                }
                else
                {
                    // A hit flies to the body; a miss still leaves the muzzle and dies on the terrain
                    // (or at max range) — with manual aiming, "wide" has to read as wide.
                    var target = targetId != null
                        ? targetPos + Vector3.up * 0.4f
                        : ct.position + ct.forward * Mathf.Min(reach, terrainDist);
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

        /// <summary>Finds the enemy/creature under the crosshair (#693): an analytic ray-vs-sphere sweep over
        /// the replicated entities — their meshes deliberately carry no colliders — occluded by terrain via a
        /// voxel march. Hitboxes are deliberately generous (kid-friendly forgiveness). Also reports how far
        /// the ray flies before hitting terrain, for the miss tracer.</summary>
        private bool AimEnemy(float maxRange, out string id, out Vector3 pos, out float terrainDist)
        {
            id = null;
            pos = default;
            terrainDist = maxRange;
            if (Game == null || Camera == null)
            {
                return false;
            }

            Vector3 o = Camera.transform.position;
            Vector3 dir = Camera.transform.forward;
            terrainDist = TerrainDistance(o, dir, maxRange);

            float best = terrainDist + 0.5f; // a body right at the wall still counts
            foreach (var e in Game.PlanetEnemies)
            {
                var basePos = Game.ScenePos(e.X, e.Y, e.Z); // seam-aware (longitude wraps)
                var center = basePos + Vector3.up * 0.9f;
                float r = 1.1f * Mathf.Max(1f, e.Scale);
                if (RayHitsSphere(o, dir, center, r, out float d) && d < best)
                {
                    best = d;
                    id = e.Id;
                    pos = basePos;
                }
            }

            foreach (var c in Game.Creatures)
            {
                float size = Mathf.Clamp(c.Size, 0.4f, 8f);
                var basePos = Game.ScenePos(c.X, c.Y, c.Z);
                var center = basePos + Vector3.up * (0.6f * size);
                float r = Mathf.Max(0.8f, 0.9f * size);
                if (RayHitsSphere(o, dir, center, r, out float d) && d < best)
                {
                    best = d;
                    id = c.Id;
                    pos = basePos;
                }
            }

            return id != null;
        }

        /// <summary>Nearest attackable entity inside the camera-forward cone (auto-aim / melee sweep).
        /// Point-blank targets (&lt; 1.5 blocks) ignore the cone — something chewing on your boots is hittable
        /// even while you look past it.</summary>
        private string BestConeTarget(float reach, float cone, out Vector3 pos)
        {
            string bestId = null;
            Vector3 bestPos = default; // out params can't be captured by the local function below
            float bestSq = reach * reach;
            Vector3 eye = Camera != null ? Camera.transform.position : transform.position;
            Vector3 fwd = Camera != null ? Camera.transform.forward : transform.forward;

            void Consider(string cid, Vector3 p)
            {
                var to = p + Vector3.up * 0.9f - eye;
                float d = to.sqrMagnitude;
                if (d >= bestSq || d < 0.0001f)
                {
                    return;
                }

                if (d > 2.25f && Vector3.Dot(to.normalized, fwd) < cone)
                {
                    return;
                }

                bestSq = d;
                bestId = cid;
                bestPos = p;
            }

            foreach (var e in Game.PlanetEnemies)
            {
                Consider(e.Id, Game.ScenePos(e.X, e.Y, e.Z));
            }

            // Creatures (fauna) are attackable too — the server shares the hit path.
            foreach (var c in Game.Creatures)
            {
                Consider(c.Id, Game.ScenePos(c.X, c.Y, c.Z));
            }

            pos = bestPos;
            return bestId;
        }

        /// <summary>Distance the aim ray travels before hitting solid terrain (voxel DDA like
        /// <see cref="AimBlock"/>, but with a caller-chosen range — weapon range exceeds block reach).
        /// Fluids are passed through, matching the block-aim behaviour.</summary>
        private float TerrainDistance(Vector3 o, Vector3 dir, float maxDist)
            => TerrainHit(o, dir, maxDist, out _, out float dist) ? dist : maxDist;

        /// <summary>The first solid terrain cell along the aim ray within <paramref name="maxDist"/>, and how
        /// far along the ray it sits. Used for the miss tracer's endpoint and, for an igniting weapon, as the
        /// cell a missed shot sets alight (#788).</summary>
        private bool TerrainHit(Vector3 o, Vector3 dir, float maxDist, out Vector3Int cell, out float distance)
        {
            cell = default;
            distance = maxDist;
            if (Game?.World == null)
            {
                return false;
            }

            int x = Mathf.FloorToInt(o.x), y = Mathf.FloorToInt(o.y), z = Mathf.FloorToInt(o.z);
            int sx = dir.x >= 0 ? 1 : -1, sy = dir.y >= 0 ? 1 : -1, sz = dir.z >= 0 ? 1 : -1;
            float invx = Mathf.Abs(dir.x) > 1e-6f ? 1f / Mathf.Abs(dir.x) : float.PositiveInfinity;
            float invy = Mathf.Abs(dir.y) > 1e-6f ? 1f / Mathf.Abs(dir.y) : float.PositiveInfinity;
            float invz = Mathf.Abs(dir.z) > 1e-6f ? 1f / Mathf.Abs(dir.z) : float.PositiveInfinity;
            float tMaxX = float.IsInfinity(invx) ? float.PositiveInfinity : (dir.x > 0 ? (x + 1 - o.x) : (o.x - x)) * invx;
            float tMaxY = float.IsInfinity(invy) ? float.PositiveInfinity : (dir.y > 0 ? (y + 1 - o.y) : (o.y - y)) * invy;
            float tMaxZ = float.IsInfinity(invz) ? float.PositiveInfinity : (dir.z > 0 ? (z + 1 - o.z) : (o.z - z)) * invz;

            float t = 0f;
            for (int i = 0; i < 160 && t <= maxDist; i++)
            {
                var id = Game.World.GetBlock(x, y, z);
                if (!id.IsAir && !IsFluidBlock(id))
                {
                    cell = new Vector3Int(x, y, z);
                    distance = t;
                    return true;
                }

                if (tMaxX <= tMaxY && tMaxX <= tMaxZ) { x += sx; t = tMaxX; tMaxX += invx; }
                else if (tMaxY <= tMaxZ) { y += sy; t = tMaxY; tMaxY += invy; }
                else { z += sz; t = tMaxZ; tMaxZ += invz; }
            }

            return false;
        }

        /// <summary>Ray-vs-sphere with the hit reported at the centre plane — plenty for ordering targets
        /// against each other and the terrain at gameplay scales.</summary>
        private static bool RayHitsSphere(Vector3 o, Vector3 dir, Vector3 center, float radius, out float dist)
        {
            dist = 0f;
            Vector3 to = center - o;
            float along = Vector3.Dot(to, dir);
            if (along < 0f)
            {
                return false;
            }

            if (to.sqrMagnitude - along * along > radius * radius)
            {
                return false;
            }

            dist = along;
            return true;
        }

        /// <summary>Publishes which enemy sits under the crosshair (every frame) — the HUD tints the reticle
        /// and the health-bar layer keeps that entity's bar visible.</summary>
        private void UpdateEnemyAim()
        {
            if (Game == null)
            {
                return;
            }

            var heldTool = Game.Content?.GetItem(Game.ItemInSlot(Game.SelectedHotbarSlot))?.Tool;
            float reach = heldTool != null && heldTool.Kind == BlocksBeyondTheStars.Shared.Definitions.ToolKind.Weapon
                ? Mathf.Max(6f, heldTool.Range)
                : 6f;
            Game.AimedEnemyId = AimEnemy(reach, out var id, out _, out _) ? id : null;
        }

        private enum WeaponFxKind { Beam, Projectile, Melee }

        /// <summary>Classifies the held weapon's effect: kinetic guns fire a flying bolt, energy guns an
        /// instant beam, and short-range weapons (or bare fists) a melee slash arc.</summary>
        private WeaponFxKind HeldWeaponFx()
        {
            string key = Game.ItemInSlot(Game.SelectedHotbarSlot) ?? string.Empty;
            if (key.Contains("gauss") || key.Contains("rail") || key.Contains("slug") || key.Contains("scrap"))
            {
                return WeaponFxKind.Projectile; // kinetic slug-throwers fire a flying bolt (the scrap pistol included)
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
            if (held.Contains("scrap")) return new Color(0.95f, 0.82f, 0.5f); // dull brass muzzle spark — a cheap kinetic round
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
                // Success cue moved to the container-broadcast diff (#751): playing it here, before the
                // request even left, made a rejected/no-op loot sound exactly like a successful one.
                Game.NoteLootRequested(nearest);
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

        /// <summary>Keeps the mining loop alive while the player holds left-click: a drill cuts any block it is
        /// allowed to, while bare hands can only keep digging the soft, hand-mineable blocks (earth, sand,
        /// plants) — hard materials need a drill. (#128)</summary>
        private void HandleDrillAudio()
        {
            if (Camera == null || !InputMap.PrimaryHeld())
            {
                return;
            }

            bool drill = HoldingDrill();
            // A weapon/scanner left-click is an attack/scan (handled as a tap in HandleInteract), never a mine hold.
            if (!drill && (HoldingWeapon() || HoldingScanner()))
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

            // By hand, only soft hand-mineable blocks keep digging; hard blocks reject without a drill, so don't
            // hammer the server with a hold the server will only refuse (the initial tap already surfaces the hint).
            if (!drill && !IsHandMineable(hitCell))
            {
                return;
            }

            TriggerSwing(); // keep the mining chop going while held
            var center = new Vector3(hitCell.x + 0.5f, hitCell.y + 0.5f, hitCell.z + 0.5f);
            if (drill)
            {
                ClientAudio.Instance?.DrillTick();
                if (Weapons != null && Time.time >= _nextDrillSpark)
                {
                    _nextDrillSpark = Time.time + 0.07f;
                    Weapons.Sparks(center, new Color(1f, 0.85f, 0.5f), 3);
                }
            }
            else if (Weapons != null && Time.time >= _nextDrillSpark)
            {
                _nextDrillSpark = Time.time + 0.09f;
                Weapons.Dust(center); // bare-hand digging kicks up dust instead of drill sparks
            }

            // Hard blocks need several hits — keep sending mine attempts while held (the server accumulates
            // effort until the block breaks); soft hand-digging breaks in one but the loop lets you sweep along.
            if (Time.time >= _nextDrillMine)
            {
                SendMineHit(hitCell, drill);
            }
        }

        /// <summary>Sends ONE mine attempt and arms the shared cooldown. Both mining paths — the click in
        /// <see cref="HandleInteract"/> and the hold loop in <see cref="HandleDrillAudio"/> — go through here
        /// (#965): they ran in the SAME Update, and because only the hold loop armed the cooldown, the first
        /// frame of every click sent the cell TWICE. The second intent hit air on the server, which answered
        /// with a ghost-block heal + a full chunk re-stream — roughly one per block mined.</summary>
        private void SendMineHit(Vector3Int cell, bool drill)
        {
            _nextDrillMine = Time.time + (drill ? 0.28f : 0.22f); // slower, weightier mining (was 0.18)
            Game.LastMineCell = cell; // so an "already empty" rejection can clear the ghost here (B32)
            Game.Network?.SendMine(cell.x, cell.y, cell.z);
        }

        /// <summary>True when the block at a cell can be broken by bare hands — mineable and requiring no tool
        /// (earth, sand, plants). Hard materials declare a required tool and are excluded. (#128)</summary>
        private bool IsHandMineable(Vector3Int cell)
        {
            if (Game?.World == null || Game.Content == null)
            {
                return false;
            }

            var def = Game.Content.BlockById(Game.World.GetBlock(cell.x, cell.y, cell.z));
            return def != null && def.Mineable
                && def.RequiredTool == BlocksBeyondTheStars.Shared.Definitions.ToolKind.None;
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

        private const float ScanConeDegrees = 25f;      // generous — creatures move; still excludes behind/beside
        private const float ScanPointBlankSq = 2f * 2f; // this close the angle test degenerates — always scannable

        /// <summary>Whether a scan target is inside the aim cone around the view direction, or point-blank
        /// (standing on it). Proximity alone must not qualify a target: a creature idling anywhere within
        /// reach — behind the player, through a wall — used to capture every scan press, which read as
        /// "the scanner is stuck on the last readout" (#1005).</summary>
        private static bool InScanCone(Vector3 eye, Vector3 fwd, Vector3 at)
        {
            var to = at - eye;
            return to.sqrMagnitude <= ScanPointBlankSq || Vector3.Angle(fwd, to) <= ScanConeDegrees;
        }

        /// <summary>Scans the aimed-at creature (threat assessment) or, failing that, the block in view.</summary>
        private void ScanTarget()
        {
            if (Game?.Network == null || Camera == null)
            {
                return;
            }

            Vector3 eye = Camera.transform.position;
            Vector3 fwd = Camera.transform.forward;

            string speciesId = null;
            Vector3 scanPos = default;
            float bestSq = Reach * Reach;
            foreach (var c in Game.Creatures)
            {
                var cp = Game.ScenePos(c.X, c.Y, c.Z); // seam-aware (longitude wraps)
                float d = (cp - transform.position).sqrMagnitude;
                if (d < bestSq && InScanCone(eye, fwd, cp))
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

            // Micro-fauna (#757): when no real creature is in view, the nearest aimed-at critter answers.
            // Critters are client-local, so the kind is resolved here and the server only validates that it
            // exists (same trust level as the creature scan above). Shorter reach — they're tiny.
            if (MicroFaunaView.Instance != null
                && MicroFaunaView.Instance.NearestCritter(Game.PlayerPosition, 5f, out string critterKey, out var critterAt,
                    world => InScanCone(eye, fwd, Game.ScenePos(world.x, world.y, world.z))))
            {
                Game.Network.SendScan("microfauna", critterKey);
                Weapons?.Pulse(Game.ScenePos(critterAt.x, critterAt.y, critterAt.z), new Color(0.4f, 0.85f, 1f));
                return;
            }

            // Voxel ray-march INCLUDING fluids, so you can scan a water/lava block too (they have no collider, so
            // a Physics.Raycast passes straight through them — that's why water "couldn't be scanned", B26).
            if (AimBlock(out var b, out _, includeFluids: true)
                && Game.Content?.BlockById(Game.World.GetBlock(b.x, b.y, b.z)) is { } def)
            {
                Game.Network.SendScan("block", def.Key);
                Weapons?.Pulse(new Vector3(b.x + 0.5f, b.y + 0.5f, b.z + 0.5f), new Color(0.4f, 0.85f, 1f));
                return;
            }

            // Nothing scannable in view — say so. The scan panel stays pinned on the previous readout while
            // the scanner is held, so a silent miss looks like the scanner stopped working (#1005).
            Game.ShowMessage(Game.Localizer?.Get("ui.scan.no_target") ?? "Scanner: no target in view.");
        }

        private BinocularOptic _optic;

        /// <summary>Lazily builds the client-side binocular optic (zoom + the thermal overlay it drives).</summary>
        private BinocularOptic EnsureOptic()
        {
            if (_optic == null)
            {
                _optic = gameObject.AddComponent<BinocularOptic>();
                _optic.Game = Game;
                _optic.Thermal = Thermal;
                _optic.SetHeldItem(Game?.ItemInSlot(Game.SelectedHotbarSlot));
            }

            return _optic;
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

            if (!InputMap.Down(InputAction.Interact))
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

            // A heal tank — or its low-tech precursor, the bed (#804) — you're aiming at → make it your
            // home spawn point (base/station, issue #461).
            if (AimBlock(out var tankHit, out _)
                && Game.Content?.BlockById(Game.World.GetBlock(tankHit.x, tankHit.y, tankHit.z))?.Key is "heal_tank" or "bed")
            {
                Game.Network?.SendSetSpawnPoint(tankHit.x, tankHit.y, tankHit.z);
                ClientAudio.Instance?.Cue("heal");
                return;
            }

            // A chair-shaped cell in any material seats the player (#806).
            if (_seatCell is null && AimBlock(out var chairHit, out _)
                && ShapeCode.ShapeOf(Game.World.GetShape(chairHit.x, chairHit.y, chairHit.z)) == (int)BlockShape.Chair)
            {
                SitDown(chairHit);
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

            // A claimable factory terminal within reach → claim it with an access code (it becomes your base).
            if (FactoryView.Instance != null)
            {
                int factory = FactoryView.Instance.NearestClaimable(transform.position, 4f);
                if (factory != 0)
                {
                    bool hasCode = false;
                    if (Game.Personal != null)
                    {
                        foreach (var s in Game.Personal)
                        {
                            if (s.Item == "access_code" && s.Count > 0) { hasCode = true; break; }
                        }
                    }

                    if (hasCode)
                    {
                        Game.Network?.SendClaimStructure(factory);
                    }
                    else
                    {
                        Game.ShowMessage(Game.Localizer?.Get("ui.factory.need_code") ?? "You need an access code to claim this.");
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
                case "console": Menu?.OpenShip(); Game.Network?.SendUseStation("console"); break; // ship status/repairs (#463)
                case "lab": Menu?.OpenTech(); break; // research tab (#463)
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

            int pick = InputMap.HotbarSlotDown();
            if (pick >= 0 && pick < HotbarSlots)
            {
                SelectSlot(pick);
            }

            float scroll = InputMap.HotbarScroll();
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

        // --- Never fall out of the world -----------------------------------------------------------------
        // Reported by a player: "Beim Bauen bin ich von einem Block gefallen, ich wollte mich mit einem Block
        // retten, doch beim Platzieren war ich an der Stelle des Blocks und bin durch ihn durchgefallen.
        // Daraufhin bin ich durch die Blöcke durchgefallen ohne Ende."
        //
        // The server deliberately ALLOWS placing into your own feet cell so you can pillar-jump, on the
        // assumption that "the client collider just lifts you onto the new block". That holds at rest or on the
        // way up — but not in a fast fall: the capsule ends up inside the new solid cell with a large downward
        // velocity, and Unity's depenetration resolves it downward, straight through the blocks below.

        /// <summary>The last position at which the player stood safely on the ground — the anchor the
        /// out-of-world guard restores to.</summary>
        private Vector3 _lastSafeGround;
        private bool _hasSafeGround;

        /// <summary>Consecutive frames spent stuck inside solid geometry (see <see cref="GuardAgainstFallingOut"/>).</summary>
        private int _embeddedFrames;

        /// <summary>Frames of being inside a block before the guard yanks the player back. A couple of frames of
        /// overlap is normal while a chunk re-meshes; a persistent overlap is a desync we must not ride out.</summary>
        private const int EmbeddedFramesBeforeRescue = 12;

        /// <summary>Below this the player has left the world for good — nothing is generated down here (it mirrors
        /// the server's build-band floor), so there is nothing left to land on.</summary>
        private const float OutOfWorldY = -2100f;

        /// <summary>
        /// A block just became solid where the player is standing. Lift them onto its top instead of leaving
        /// Unity's depenetration to guess a direction — guessing "down" is what made a player fall through the
        /// world after saving themselves mid-fall with a block. Called for every block change, so it also covers
        /// another player or a server path filling the cell.
        /// </summary>
        public void LiftOutOfBlockAt(int cellX, int cellY, int cellZ)
        {
            if (_controller == null || !_controller.enabled || (Game != null && Game.Spectating))
            {
                return;
            }

            // Does the cell overlap the capsule's own column? Feet at transform.position, head StandHeight above.
            var pos = transform.position;
            if (Mathf.FloorToInt(pos.x) != cellX || Mathf.FloorToInt(pos.z) != cellZ)
            {
                return;
            }

            float feet = pos.y;
            float head = pos.y + (_crouched ? CrouchHeight : StandHeight);
            if (cellY + 1f <= feet + 0.001f || cellY >= head - 0.001f)
            {
                return; // entirely below the feet or above the head — no overlap
            }

            SnapTo(new Vector3(pos.x, cellY + 1f, pos.z));
        }

        /// <summary>
        /// Last line of defence against ending up outside the world: remembers the last spot the player stood on
        /// safely and restores it if they spend several frames inside solid geometry or drop below the world
        /// floor. Independent of any particular cause — whatever desync puts the player inside a block, this
        /// gets them out instead of letting them fall forever.
        /// </summary>
        private void GuardAgainstFallingOut(bool grounded, bool inWater, bool onLadder)
        {
            // A climbing player legitimately stands INSIDE the ladder cell, and a swimmer inside water — neither
            // is "stuck in geometry". Observers pass through everything by design.
            if (onLadder || inWater || (Game != null && Game.Spectating))
            {
                _embeddedFrames = 0;
                return;
            }

            // Fell out of the bottom of the world — nothing below will ever stop us.
            if (transform.position.y < OutOfWorldY)
            {
                if (_hasSafeGround)
                {
                    SnapTo(_lastSafeGround);
                }

                _embeddedFrames = 0;
                return;
            }

            // Solid at chest height means the capsule is inside geometry, not merely brushing it. Only blocks
            // that really collide count — walking through grass or past a wall torch is not being stuck.
            bool embedded = IsCollidingKey(BlockKeyAt(transform.position + Vector3.up * 0.9f));
            if (embedded)
            {
                if (++_embeddedFrames >= EmbeddedFramesBeforeRescue && _hasSafeGround)
                {
                    SnapTo(_lastSafeGround);
                    _embeddedFrames = 0;
                }

                return;
            }

            _embeddedFrames = 0;

            // Standing on real ground with clear space around us: this is a spot worth coming back to.
            if (grounded && _verticalVelocity <= 0f)
            {
                _lastSafeGround = transform.position;
                _hasSafeGround = true;
            }
        }

        /// <summary>
        /// Keeps the auto-step from punching the player's head into a low ceiling. The 1.8 m capsule leaves only
        /// ~0.14 m of clearance in a 2-block-high gap, and a 0.6 m step sweep eats far more than that — so the
        /// player wedged and could not walk through a 2-high opening they had built (reported as "Das 2 Blöcke
        /// Problem"; the earlier skin-width fix only removed part of the cause). Step height is therefore capped
        /// to the headroom actually available, which still climbs slabs and stair treads in the open.
        /// </summary>
        private void UpdateStepOffset()
        {
            float capsuleTop = _crouched ? CrouchHeight : StandHeight;

            // Walk upward from the head in step-sized samples and stop at the first solid cell.
            float headroom = DefaultStepOffset;
            for (float probe = 0.1f; probe <= DefaultStepOffset + 0.05f; probe += 0.1f)
            {
                if (IsCollidingKey(BlockKeyAt(transform.position + Vector3.up * (capsuleTop + probe))))
                {
                    headroom = Mathf.Max(0f, probe - 0.1f);
                    break;
                }
            }

            _controller.stepOffset = Mathf.Min(DefaultStepOffset, headroom);
        }

        /// <summary>The step height used in the open — matches the value WorldRig sets up so a slab (0.5) and each
        /// stair tread are walked up without jumping.</summary>
        private const float DefaultStepOffset = 0.6f;

        // --- Observer mode (issue #487) -------------------------------------------------------------

        /// <summary>Base flight speed while observing (blocks/s). Deliberately not much faster than a walk by
        /// default: every metre flown streams chunks, and a fast observer is a chunk-generation firehose on the
        /// server (see the planet-movement-lag findings). <see cref="SpectatorBoost"/> is the opt-in burst.</summary>
        private const float SpectatorSpeed = 12f;
        private const float SpectatorBoost = 3f;      // hold sprint
        private const float SpectatorMaxSpeed = 60f;  // hard cap, wheel adjustment included
        private float _spectatorSpeedScale = 1f;

        /// <summary>Free flight with no collision and no gravity. The CharacterController is switched off
        /// entirely (the same trick <see cref="SnapTo"/> uses) so walls, terrain and ship hulls are simply not
        /// there — an observer inspecting a sealed base must be able to get inside it.</summary>
        private void SpectatorMove(float h, float v)
        {
            if (_controller.enabled)
            {
                _controller.enabled = false; // noclip: nothing to collide with while observing
            }

            _verticalVelocity = 0f;
            _moving = Mathf.Abs(h) + Mathf.Abs(v) > 0.1f;

            // The wheel tunes cruise speed so a single base and a whole continent are both comfortable. Reusing
            // the hotbar scroll is free here: the hotbar is hidden while observing, so nothing else wants it.
            float wheel = InputMap.HotbarScroll();
            if (Mathf.Abs(wheel) > 0.01f)
            {
                _spectatorSpeedScale = Mathf.Clamp(_spectatorSpeedScale * (wheel > 0f ? 1.25f : 0.8f), 0.25f, 5f);
            }

            float speed = SpectatorSpeed * _spectatorSpeedScale;
            if (InputMap.Held(InputAction.SpeederBoost)) // LeftShift by default — "go faster" in both contexts
            {
                speed *= SpectatorBoost;
            }

            speed = Mathf.Min(speed, SpectatorMaxSpeed);

            // Fly where you look (including pitch) — a strictly horizontal WASD would make descending into a
            // cave needlessly fiddly.
            Vector3 forward = Camera != null ? Camera.transform.forward : transform.forward;
            Vector3 move = (transform.right * h + forward * v) * speed;
            if (InputMap.JumpHeld())
            {
                move += Vector3.up * speed;
            }

            if (InputMap.CrouchHeld())
            {
                move -= Vector3.up * speed;
            }

            transform.position += move * Time.deltaTime;
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

        /// <summary>Cinematic-capture hooks (<see cref="ClipDirector"/>): drive the on-foot player's walk + look
        /// without keyboard input so a recorded clip shows the character actually moving. <see cref="_captureWalk"/>
        /// gates this — when false the normal <c>InputMap.MoveX/MoveY</c> path is untouched in regular play.</summary>
        private bool _captureWalk;
        private float _captureH, _captureV;
        public void SetWalkInput(float horizontal, float vertical)
        {
            _captureH = horizontal;
            _captureV = vertical;
            _captureWalk = true;
        }

        public void ClearWalkInput() => _captureWalk = false;

        /// <summary>Set the body yaw + look pitch directly (same as the rotation half of <see cref="SetCapturePose"/>),
        /// for a cinematic look-around while walking.</summary>
        public void SetLookAngles(float yaw, float pitch)
        {
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
            // Stand a good way BACK from the ship on OPEN terrain, then face AWAY from the hull so the shot shows the
            // planet's landscape (the world-variety point) instead of the ship's wall/door filling the frame. The
            // spawn sits the player INSIDE the ship interior, whose glass skylight leaves "open sky" overhead — so the
            // sky check alone can't tell inside-the-hull from outside; the enclosure check below (walls on most sides →
            // indoors) is what reliably rejects an interior spot. Radii stay within the streamed-in chunk ring around
            // spawn (a far spot lands over an unbaked chunk and the down-ray simply misses); nearer radii are fallbacks
            // for small floating islands.
            float[] dists = { 20f, 17f, 24f, 14f, 28f, 12f, 32f };
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

                    // Lava has no collider either, so the down-ray hits the rock UNDER a lava lake and the spot
                    // would leave the player standing in lava — burning, with the damage flash in the frame.
                    string feet = BlockKeyAt(stand + Vector3.up * 0.3f);
                    if (feet == "lava" || BlockKeyAt(stand + Vector3.up * 1.1f) == "lava")
                    {
                        continue;
                    }

                    // Open sky overhead? A hit means a solid ceiling above us (cave / overhang / hull under a solid
                    // roof) → indoors. (The ship's glass skylight has no collider, so this passes for an interior spot
                    // under it — the enclosure check below is what catches those.)
                    if (Physics.Raycast(stand + Vector3.up * 0.3f, Vector3.up, out var up, 5f, ~0, QueryTriggerInteraction.Ignore)
                        && up.collider != _controller)
                    {
                        continue;
                    }

                    // Boxed in by walls? Cast head-height rays outward on all four sides; a spot inside the ship's
                    // interior room hits a wall on (almost) every side within a couple of blocks, while an outdoor
                    // spot sees open space (a single hit — e.g. the hull on the ship-facing side — is fine). This is
                    // the check that keeps the shot OUTSIDE, since the skylight fools the open-sky test above.
                    Vector3 eye = stand + Vector3.up * (half * 0.8f);
                    int walls = 0;
                    Vector3[] sides = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
                    foreach (var s in sides)
                    {
                        if (Physics.Raycast(eye, s, out var w, 5f, ~0, QueryTriggerInteraction.Ignore) && w.collider != _controller)
                        {
                            walls++;
                        }
                    }
                    if (walls >= 3)
                    {
                        continue; // enclosed on three+ sides → inside the hull, not out on the surface
                    }

                    SnapTo(stand);
                    // Face AWAY from the ship (from hull → player), so the camera looks out over the terrain and the
                    // ship falls behind the player, out of frame.
                    Vector3 d = transform.position - anchor;
                    float yaw = (Mathf.Abs(d.x) + Mathf.Abs(d.z) > 0.01f)
                        ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg
                        : transform.eulerAngles.y;
                    transform.eulerAngles = new Vector3(0f, yaw, 0f);
                    _pitch = Mathf.Clamp(pitch, -89f, 89f);
                    if (Camera != null)
                    {
                        Camera.transform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
                    }

                    Debug.Log($"[Capture] PlaceForCaptureNear: placed at dist={dists[di]} ang={a * 45}° walls={walls} y={stand.y:F1}");
                    return true;
                }
            }

            Debug.LogWarning("[Capture] PlaceForCaptureNear: no open outdoor spot found around the ship.");
            return false;
        }

        /// <summary>Capture hook: the on-foot player is standing on solid ground this frame.</summary>
        public bool IsCaptureGrounded => _controller != null && _controller.enabled && _controller.isGrounded;

        /// <summary>Capture hook: the player's head is under water (so the shot would be a submerged murk).</summary>
        public bool IsHeadUnderwater() => IsSubmerged();

        private void ApplyGravityOnly()
        {
            // A held world (#908) holds the body too. Without this, opening the pause menu mid-jump or mid-fall
            // kept dropping the player through a world that had otherwise stopped — the single most obvious way
            // the "pause" failed to look like one. The vertical velocity is left untouched, so resuming carries
            // the fall on from exactly where it stopped instead of restarting it.
            if (Game != null && Game.WorldPaused)
            {
                return;
            }

            bool grounded = _controller.isGrounded;
            UpdateFloorWait(grounded);
            if (grounded)
            {
                _verticalVelocity = -1f;
            }
            else if (_awaitingFloor)
            {
                _verticalVelocity = 0f; // no floor streamed yet — a menu open at spawn must not drop us either
            }
            else
            {
                _verticalVelocity -= _effGravity * Time.deltaTime;
            }

            _controller.Move(new Vector3(0f, _verticalVelocity, 0f) * Time.deltaTime);
        }

        /// <summary>Ends the post-spawn hover (#773) as soon as there is something to stand on — the collider
        /// under our feet, or one streaming in below us — and hard-caps it so a spawn chunk that never arrives
        /// falls back to the old behaviour (drop, then the server's void rescue) instead of hovering forever.</summary>
        private void UpdateFloorWait(bool grounded)
        {
            if (!_awaitingFloor)
            {
                return;
            }

            _awaitFloorTimer += Time.deltaTime;
            if (grounded || _awaitFloorTimer > AwaitFloorMaxSeconds || ColliderBelow())
            {
                _awaitingFloor = false;
            }
        }

        /// <summary>True when a streamed collider (terrain, ship deck, pad) sits within a short drop below us.</summary>
        private bool ColliderBelow()
            => Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out var hit, 10f)
               && hit.collider != _controller;

        private void LookAround()
        {
            // A magnified view multiplies every mouse movement by the same factor, so the sensitivity has to
            // come down with the field of view — at 6× an unscaled mouse is unusable.
            float sens = MouseSensitivity * (_optic != null && _optic.Raised ? _optic.SensitivityScale : 1f);
            float mx = InputMap.LookX() * sens;
            float my = InputMap.LookY() * sens * (InvertY ? -1f : 1f);
            transform.Rotate(0f, mx, 0f);
            _pitch = Mathf.Clamp(_pitch - my, -89f, 89f);
            if (Camera != null)
            {
                Camera.transform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
            }
        }

        /// <summary>Seats the player on a chair-shaped cell (#806): parks the capsule centred on the cell
        /// (controller off — the seat's own collider boxes must not fight the snap) and reports the pose.</summary>
        private void SitDown(Vector3Int cell)
        {
            _seatCell = cell;
            _satFrame = Time.frameCount;
            _verticalVelocity = 0f;
            UpdateJetpack(false);
            _controller.enabled = false;
            transform.position = Game != null
                ? Game.ScenePos(cell.x + 0.5f, cell.y, cell.z + 0.5f)
                : new Vector3(cell.x + 0.5f, cell.y, cell.z + 0.5f);
            Avatar?.SetSeated(true);
            Game?.Network?.SendSetSeated(true);
        }

        /// <summary>Stands the player back up from a chair (#806) and re-enables normal movement.</summary>
        private void StandUp()
        {
            _seatCell = null;
            _controller.enabled = true;
            Avatar?.SetSeated(false);
            Game?.Network?.SendSetSeated(false);
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
            float my = InputMap.LookY() * MouseSensitivity * (InvertY ? -1f : 1f);
            _speederCamPitch = Mathf.Clamp(_speederCamPitch - my, -8f, 35f);
            if (Camera != null)
            {
                Camera.transform.localPosition = new Vector3(0f, 2.4f, -5.5f);
                Camera.transform.localEulerAngles = new Vector3(_speederCamPitch, 0f, 0f);
            }

            var driven = Game.DrivenSpeeder;
            bool outOfFuel = driven != null && driven.Fuel <= 0.01f;

            float throttle = InputMap.MoveY();   // W = +1, S = -1 (brake / reverse)
            float steer = InputMap.MoveX();    // A = -1, D = +1
            bool boosting = InputMap.Held(InputAction.SpeederBoost) && !outOfFuel && throttle > 0.1f;

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

            if (InputMap.JumpDown() && !outOfFuel)
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

            if (InputMap.Down(InputAction.SpeederExit))
            {
                Game.Network?.SendExitSpeeder();
            }
            else if (InputMap.Down(InputAction.SpeederRefuel))
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
            float h = _captureWalk ? _captureH : InputMap.MoveX();
            float v = _captureWalk ? _captureV : InputMap.MoveY();

            // Observer mode (issue #487): free flight straight through geometry. Handled before every normal
            // movement branch because none of them apply — no gravity, no ground, no water, no fall damage,
            // no footsteps to give the invisible admin away.
            if (Game != null && Game.Spectating)
            {
                SpectatorMove(h, v);
                return;
            }

            if (!_controller.enabled)
            {
                // Left observer mode (or a capture snap): the controller owns collision again from here.
                _controller.enabled = true;
                _verticalVelocity = 0f;
            }

            Vector3 move = (transform.right * h + transform.forward * v) * _effMoveSpeed;

            float prevVy = _verticalVelocity; // captured before the grounded reset (for landing shake)
            bool grounded = _controller.isGrounded;
            bool inWater = IsSubmerged();
            bool onLadder = !inWater && OnLadder();
            UpdateFloorWait(grounded);
            _moving = (inWater || grounded || onLadder) && (Mathf.Abs(h) + Mathf.Abs(v) > 0.1f);

            // Crouch/sneak: shrink the capsule + slow the walk while held on the ground.
            UpdateCrouch(grounded, inWater, onLadder);
            if (_crouched)
            {
                move *= CrouchSpeedMul;
            }

            UpdateFlight(grounded, inWater, onLadder);
            if (_flying)
            {
                move *= FlyHorizontalMul;
            }

            bool jetpacking = false;
            if (inWater)
            {
                // Buoyant swimming: drift down slowly when idle, hold Jump to rise and surface; water also
                // breaks a fall (the big downward speed eases out instead of slamming the seabed). No jetpack.
                //
                // Climb-out assist (#131): the slow swim-up (4/s, no jump impulse) can't clear a 1-block bank, so
                // you bob against the shore and slide back. At the surface, when you press Jump while pushing
                // forward (A) — or simply swim into a low ≤1-block bank (B) — give a real jump impulse and keep
                // full forward speed so you mount the land instead.
                bool pushing = Mathf.Abs(h) + Mathf.Abs(v) > 0.1f;
                bool atSurface = BlockKeyAt(transform.position + Vector3.up * 1.9f) != "water"; // open air above the head
                bool climbingOut = pushing && atSurface && (InputMap.JumpHeld() || LedgeAhead(move));
                if (climbingOut)
                {
                    _verticalVelocity = _effJumpSpeed; // real hop out of the water (full forward speed kept below)
                }
                else
                {
                    float target = InputMap.JumpHeld() ? SwimUpSpeed : -SwimSinkSpeed;
                    _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, target, SwimAccel * Time.deltaTime);
                    move *= SwimSpeedMul;
                }
            }
            else if (onLadder)
            {
                // Climbing (Minecraft-style): no gravity while on a ladder. Hold Jump or push forward to go up,
                // crouch (Ctrl/C) or pull back to go down; otherwise cling with a gentle slide. (#126)
                bool up = InputMap.JumpHeld() || v > 0.1f;
                bool down = InputMap.CrouchHeld() || v < -0.1f;
                _verticalVelocity = up ? ClimbSpeed : (down ? -ClimbSpeed : -1f);
            }
            else if (_flying)
            {
                // Creative flight: no gravity at all. Jump rises, crouch (Ctrl/C) sinks, and letting go holds
                // altitude — the same feel as Minecraft's creative flight. Collision stays ON (this is flight,
                // not the observer mode's noclip), so you can still land on and build against things.
                _verticalVelocity = (InputMap.JumpHeld() ? FlySpeed : 0f) - (InputMap.CrouchHeld() ? FlySpeed : 0f);
            }
            else if (grounded)
            {
                if (InputMap.JumpDown())
                {
                    ClientAudio.Instance?.Cue("jump", 0.6f);
                }

                _verticalVelocity = InputMap.JumpHeld() ? _effJumpSpeed : -1f;
            }
            else if (Game != null && Game.OnFootInSpace)
            {
                // Above the atmosphere there is no gravity: float, never fall. Jump rises, crouch (Ctrl/C)
                // sinks, otherwise the suit drifts to a gentle stop. (Set by item 10 — building up into space.)
                float lift = (InputMap.JumpHeld() ? SpaceFloatSpeed : 0f)
                           - ((InputMap.CrouchHeld()) ? SpaceFloatSpeed : 0f);
                _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, lift, SpaceFloatAccel * Time.deltaTime);
            }
            else if (_awaitingFloor)
            {
                _verticalVelocity = 0f; // the spawn floor hasn't streamed in yet — hold altitude, don't drop (#773)
            }
            else
            {
                _verticalVelocity -= _effGravity * Time.deltaTime;

                // Jetpack: hold Jump in the air to thrust upward (needs the item + suit energy). The server
                // drains energy on the reported state and forces it off when empty (SuitEnergy then hits 0).
                if (InputMap.JumpHeld() && CanJetpack())
                {
                    jetpacking = true;
                    _verticalVelocity += _effJetpackAccel * Time.deltaTime;
                    if (_verticalVelocity > JetpackMaxRise)
                    {
                        _verticalVelocity = JetpackMaxRise;
                    }

                    // #900: hanging in the air with no ground under you, the wind has a hold on you. A gale
                    // pushes a hovering player noticeably downwind — enough to matter when lining up a
                    // landing, never enough to take control away.
                    if (Game != null && Game.WindSpeed > 0.05f && Game.ExposedToSky)
                    {
                        var gust = Game.WindVector * (WindDriftPerSecond * Time.deltaTime);
                        move.x += gust.x;
                        move.z += gust.z;
                    }
                }
            }

            UpdateJetpack(jetpacking);

            move.y = _verticalVelocity;

            // Sneak edge-stop: while crouched and standing on the ground, cancel any horizontal component that
            // would carry the feet off a ledge into open air (checked per axis so you can still slide ALONG the
            // edge). This is what lets the player lean out over a cave/ledge to place a bridging block instead of
            // walking off and falling. Jumping (upward velocity) is exempt so you can still hop off deliberately.
            if (_crouched && grounded && _verticalVelocity <= 0f)
            {
                if (Mathf.Abs(move.x) > 0.01f && !GroundAhead(new Vector3(move.x, 0f, 0f)))
                {
                    move.x = 0f;
                }

                if (Mathf.Abs(move.z) > 0.01f && !GroundAhead(new Vector3(0f, 0f, move.z)))
                {
                    move.z = 0f;
                }

                // …and then the DIAGONAL, which the per-axis pass alone lets through. Walking off an outside
                // corner, each single axis still finds floor along its own edge, so neither test fires — while
                // the combined step lands over the void and the player falls anyway. A player found it and put
                // it plainly: "In Minecraft fällt man nicht runter wenn man sneakt aber hir schon."
                if (Mathf.Abs(move.x) > 0.01f && Mathf.Abs(move.z) > 0.01f
                    && !GroundAhead(new Vector3(move.x, 0f, move.z)))
                {
                    move.x = 0f;
                    move.z = 0f;
                }
            }

            // Cap the auto-step to the headroom above the head before moving, so a 2-block-high opening stays
            // walkable instead of wedging the capsule (see UpdateStepOffset).
            UpdateStepOffset();

            _controller.Move(move * Time.deltaTime);

            // Last line of defence: if that move left us inside geometry (or below the world), get back out.
            GuardAgainstFallingOut(grounded, inWater, onLadder);

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
                // fall damage. Small drops/jumps stay below the safe threshold and do nothing. Deep water breaks
                // the fall via the swim branch — but landing in even ONE block of water should cushion it too,
                // like Minecraft (Severin playtest: shallow water still hurt because the chest wasn't submerged).
                // Flying down onto the ground is a landing, not a fall — the descent is powered, not a drop.
                if (-prevVy > _effSafeFallSpeed && !FeetInWater() && !_flying)
                {
                    Game?.Network?.SendFallDamage(-prevVy);
                }
            }

            _wasGrounded = grounded;
        }

        /// <summary>True when the player's upper body sits in a water block — the cue to switch to swimming
        /// (sampled at chest height, so wading through shallow water still walks; only deep water swims).</summary>
        private bool IsSubmerged() => BlockKeyAt(transform.position + Vector3.up * 1.1f) == "water";

        /// <summary>True when the player's feet touch water on landing — sampled low so even a single block of
        /// water counts. Used to cushion the fall (no splash damage) the way any depth of water does in Minecraft;
        /// <see cref="IsSubmerged"/> alone (chest height) missed shallow pools (Severin playtest).</summary>
        private bool FeetInWater() =>
            BlockKeyAt(transform.position + Vector3.up * 0.1f) == "water"
            || BlockKeyAt(transform.position + Vector3.up * 0.6f) == "water";

        /// <summary>True when a low (≤1 block) solid bank sits directly ahead of the swimmer — a wall at knee
        /// height with clear space just above it — the cue to mantle out of the water onto land (#131).</summary>
        private bool LedgeAhead(Vector3 moveDir)
        {
            Vector3 flat = new Vector3(moveDir.x, 0f, moveDir.z);
            if (flat.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            Vector3 ahead = transform.position + flat.normalized * 0.7f;
            return IsBlockingAt(ahead + Vector3.up * 0.5f)        // bank face at ~knee height
                && !IsBlockingAt(ahead + Vector3.up * 1.6f);      // and open space to stand on top
        }

        /// <summary>True when a solid (standable/blocking) block sits at this world position. Air, water and lava
        /// don't block; everything else does — a coarse check used to spot a low bank when climbing out of water.</summary>
        private bool IsBlockingAt(Vector3 world)
        {
            if (Game?.World == null || Game.Content == null)
            {
                return false;
            }

            var id = Game.World.GetBlock(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y), Mathf.FloorToInt(world.z));
            if (id.IsAir)
            {
                return false;
            }

            string key = Game.Content.BlockById(id)?.Key;
            return key != "water" && key != "lava";
        }

        /// <summary>True when the player overlaps a ladder block (sampled at shin + chest height), the cue to
        /// switch to climbing instead of falling/walking. (#126)</summary>
        private bool OnLadder() =>
            BlockKeyAt(transform.position + Vector3.up * 0.3f) == "ladder"
            || BlockKeyAt(transform.position + Vector3.up * 1.1f) == "ladder";

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

        /// <summary>A block key that gives solid footing to stand on (anything placed, but not air or a fluid you'd
        /// sink through).</summary>
        private static bool IsSolidKey(string key)
            => !string.IsNullOrEmpty(key) && key != "air" && key != "water" && key != "lava";

        /// <summary>
        /// A block that actually has a COLLIDER — i.e. one the capsule can be blocked by or stuck inside.
        /// Cross-billboard props (small flora and the torch) are meshed without a collider on purpose, so the
        /// player walks straight through them; treating them as solid would make the out-of-world guard think a
        /// player standing in a grass tuft (or next to a wall torch) was embedded in geometry, and would make a
        /// torch overhead cancel the auto-step.
        /// </summary>
        internal static bool IsCollidingKey(string key)
            => IsSolidKey(key)
               && key != "torch"
               && key != "lantern" // slim prop like the torch (#809)
               && key != "ladder"  // walk-through since #803 — a climber stands INSIDE the ladder cell
               && key != "fire"        // burns, doesn't block — meshed without a collider
               && key != "energy_gate" // walk-through membrane — meshed without a collider
               && !key.StartsWith("flora_", System.StringComparison.Ordinal);

        /// <summary>Whether there is solid footing just past the player's edge in a horizontal direction — used by
        /// the sneak edge-stop. Samples a little ahead of the capsule and allows a single step-down (so you can
        /// still sneak down a slab/stair), but reports "no ground" over a real drop into open air.</summary>
        private bool GroundAhead(Vector3 horizontalDir)
        {
            if (horizontalDir.sqrMagnitude < 0.0001f)
            {
                return true;
            }

            Vector3 ahead = transform.position + horizontalDir.normalized * (_controller.radius + 0.15f);
            return IsSolidKey(BlockKeyAt(ahead - Vector3.up * 0.5f))
                || IsSolidKey(BlockKeyAt(ahead - Vector3.up * 1.2f));
        }

        /// <summary>
        /// Drives creative flight (#836): a double-tap of jump toggles it, and it ends when the world stops
        /// allowing it, when the player touches down, or in water / on a ladder, which have their own vertical
        /// rules and would fight it.
        /// <para>
        /// Landing turns it off deliberately: without that, walking around a base means constantly holding
        /// crouch to stay down. Toggling back on is one double-tap away.
        /// </para>
        /// </summary>
        private void UpdateFlight(bool grounded, bool inWater, bool onLadder)
        {
            bool allowed = Game != null && Game.CanFly;
            if (!allowed || inWater || onLadder)
            {
                _flying = false;
                return;
            }

            if (InputMap.JumpDown())
            {
                // A tap within the window of the previous one = the toggle. Taking OFF from the ground needs the
                // first tap's jump to have happened, which is why this reads the tap times rather than grounded.
                if (_lastJumpTapTime > 0f && Time.time - _lastJumpTapTime <= FlyDoubleTapWindow)
                {
                    _flying = !_flying;
                    _lastJumpTapTime = -1f; // consumed: a third tap starts a fresh pair, not another toggle
                    _verticalVelocity = 0f;
                    ClientAudio.Instance?.Cue("jump", 0.5f);
                }
                else
                {
                    _lastJumpTapTime = Time.time;
                }
            }

            if (_flying && grounded && _verticalVelocity <= 0f && !InputMap.JumpHeld())
            {
                _flying = false; // touched down under our own weight — walk from here
            }
        }

        /// <summary>True while the player is in creative flight — the HUD shows the mode, and fall damage
        /// never applies.</summary>
        public bool Flying => _flying;

        /// <summary>Drives the crouch/sneak state each frame: sets it from the input while grounded, keeps it on if a
        /// ceiling would block standing back up, snaps the collider to the crouched capsule, and eases the camera.</summary>
        private void UpdateCrouch(bool grounded, bool inWater, bool onLadder)
        {
            bool want = InputMap.CrouchHeld() && grounded && !inWater && !onLadder;

            // Don't stand up into a block: if crouched under a low ceiling, stay crouched until there's headroom.
            if (_crouched && !want && IsSolidKey(BlockKeyAt(transform.position + Vector3.up * (StandHeight - 0.1f))))
            {
                want = true;
            }

            _crouched = want;
            _crouchT = Mathf.MoveTowards(_crouchT, _crouched ? 1f : 0f, Time.deltaTime * 8f);

            // The collider snaps to the crouched capsule (physics can't lerp cleanly). Lowering the centre with the
            // height keeps the feet planted, so shrinking/growing never pops the player up or down.
            float targetH = _crouched ? CrouchHeight : StandHeight;
            if (!Mathf.Approximately(_controller.height, targetH))
            {
                _controller.height = targetH;
                _controller.center = new Vector3(0f, targetH * 0.5f, 0f);
            }
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

            // The optic's zoom is applied HERE because this method owns the field of view: it rewrites it every
            // frame, so a zoom written from anywhere else would be eased straight back out.
            bool zoomed = _optic != null && _optic.Raised;

            // Comfort: with CameraMotion off, bob/FOV-kick/shake all flatten to a steady camera. A magnified
            // view amplifies the bob just like it amplifies the mouse, so damp it by the same factor.
            float motion = (CameraMotion ? 1f : 0f) * (zoomed ? _optic.MotionScale : 1f);
            float amt = (_moving ? 1f : 0f) * motion;
            _bobPhase += dt * (_moving ? 9f : 0f) * motion;
            float bobY = Mathf.Sin(_bobPhase * 2f) * 0.035f * amt;
            float bobX = Mathf.Cos(_bobPhase) * 0.025f * amt;
            // Ease the eye down toward the crouch height (_crouchT set in UpdateCrouch) as the baseline the bob rides on.
            Vector3 eye = Vector3.Lerp(FirstPersonEye, CrouchEye, _crouchT);
            Camera.transform.localPosition = eye + new Vector3(bobX, bobY, 0f);

            // A big jump (raising, stepping or dropping the optic) travels fast so it feels like a click; the
            // small walking kick keeps its original gentle 40°/s drift.
            float targetFov = zoomed ? _optic.TargetFov(_baseFov) : _baseFov + (_moving ? 4f * motion : 0f);
            float fovRate = Mathf.Abs(Camera.fieldOfView - targetFov) > 6f ? 160f : 40f;
            Camera.fieldOfView = Mathf.MoveTowards(Camera.fieldOfView, targetFov, dt * fovRate);

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
                case "scrap_pistol": audio.Cue("weapon_scrap"); break;
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

            bool mine = InputMap.PrimaryDown();
            bool place = InputMap.SecondaryDown();
            if (!mine && !place)
            {
                return;
            }

            // Left-click with the optic raised puts it away instead of swinging at whatever the magnified
            // crosshair happens to cover — the natural "lower the binoculars and get to work" gesture.
            if (mine && _optic != null && _optic.Raised)
            {
                _optic.Lower();
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

                // Right-click the held suit teleporter → recall to the ship. The item was craftable but
                // had no client-side trigger at all (#414 N17); the server validates ownership, suit
                // energy and the cooldown, and answers with a RespawnNotice snap (or a reject toast).
                if (held == "suit_teleporter")
                {
                    Game.Network?.SendTeleportToShip();
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

                // Right-click the binoculars → raise the optic / step the magnification. Also client-only:
                // the zoom costs nothing and the thermal contacts are all drawn from state the client already
                // has, so there is nothing for the server to validate.
                if (held == "binoculars" || held == "thermal_binoculars")
                {
                    EnsureOptic().Step();
                    return;
                }

                // Right-click the paint tool on a block → open the 32×32 paint editor for that cell (#818).
                // World blocks only for now — parked-ship cells keep their hull paint (structure designs are
                // a follow-up); the server enforces solidity, reach and protection on Apply anyway.
                if (held == "paint_tool")
                {
                    if (AimTarget(out var paintCell, out _, out var paintShip) && paintShip == null)
                    {
                        PaintToolUi.Instance?.OpenFor(paintCell);
                    }

                    return;
                }

                // Right-click the shaping tool → the form editor (#845). Aimed at a block that already
                // carries a player-designed form it opens pre-loaded with THAT form, which is how forms are
                // copied off other people's builds; anywhere else it starts on an empty grid.
                // A stencil hands a form to whoever holds it: right-click puts it in their own library (#846).
                if (BlocksBeyondTheStars.Shared.State.ItemKey.Base(held) == "shape_stencil")
                {
                    ShapeToolUi.Instance?.UseStencil(held);
                    return;
                }

                if (held == "shape_tool")
                {
                    bool copied = AimTarget(out var formCell, out _, out var formShip)
                        && formShip == null
                        && (ShapeToolUi.Instance?.OpenForCell(formCell) ?? false);
                    if (!copied)
                    {
                        ShapeToolUi.Instance?.OpenNew();
                    }

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

                SendMineHit(hitCell, HoldingDrill()); // arms the cooldown too — see SendMineHit (#965)
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
                    // block becomes part of the ship and persists with it), not a world place. Only when the
                    // aim ray hit THAT ship, though — the bounding box also covers the ground-level air ring
                    // around the hull, and rerouting a place that targets a world block (the ground beside a
                    // parked ship) made any block "unplaceable" there: a foreign ship answers none_here, the
                    // own hull no_anchor (#1023). Aiming at the ground → world place; the server still guards
                    // the real interior authoritatively.
                    var boundsShip = Game.LandedShipBoundsAt(placeCell.x, placeCell.y, placeCell.z, out var lp);
                    if (boundsShip != null && boundsShip == aimedShip)
                    {
                        Game.Network.SendStructureEdit(boundsShip.StructureId, lp.X, lp.Y, lp.Z, mine: false, item);
                        TriggerSwing();
                        return;
                    }

                    // Growing a ship CONSTRUCTION SITE (#948): aiming at the half-built hull but the target
                    // cell lies outside its current bounds — still a structure edit (the server re-anchors
                    // the grid and re-broadcasts), never a world place next to it.
                    if (aimedShip != null && aimedShip.StructureId.StartsWith("shipyard:", System.StringComparison.Ordinal))
                    {
                        var gl = ShipLocal(aimedShip, placeCell);
                        Game.Network.SendStructureEdit(aimedShip.StructureId, gl.x, gl.y, gl.z, mine: false, item);
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
                        // Send the orientation the ghost was showing (an explicit rotate-key state, or the
                        // client's own Auto answer — see PendingPlacement). An item with nothing to orient
                        // sends the raw override fields and lets the server derive, exactly as before.
                        bool orientable = PendingPlacement(item, hitCell, placeCell, out _, out int upFace, out int yaw);
                        Game.Network.SendPlace(placeCell.x, placeCell.y, placeCell.z, item,
                            upFace: orientable ? upFace : _placeUpFace,
                            yaw: orientable ? yaw : _placeYaw);
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

        /// <summary>The shape index the held item would place: the crafted form carried in the item key
        /// (slabs, stairs, player-designed forms, …), or a prop block's server-stamped default form
        /// (bed/campfire/rug/pot, the ladder's wall plate, the crafted staircase). 0 = places a plain cube
        /// or no block at all, i.e. nothing the rotate key or the placement ghost should react to.
        /// <paramref name="cycle"/> says how far this item's orientation may be steered — the rotate key and
        /// the ghost must offer exactly what the server will honour.</summary>
        private int HeldPlaceShape(string held, out PropOrientation cycle)
        {
            cycle = PropOrientation.None;
            if (string.IsNullOrEmpty(held))
            {
                return 0;
            }

            int shape = BlocksBeyondTheStars.Shared.State.ItemKey.Shape(held);
            if (shape > 0)
            {
                cycle = PropOrientation.Full; // a crafted form is a building block: all 24 orientations
                return shape;
            }

            var def = Game?.Content?.GetItem(held);
            if (def == null || string.IsNullOrEmpty(def.PlacesBlock))
            {
                return 0;
            }

            cycle = PropShapes.OrientationOf(def.PlacesBlock);
            return cycle == PropOrientation.None ? 0 : PropShapes.DefaultPlaceShape(def.PlacesBlock);
        }

        /// <summary>The ladder's rotate-key states, in cycle order: the four walls it can hug, then
        /// free-standing (#909). Auto sits in front of them as index -1. Its plate is a square Panel, so the
        /// quarter turns the other shapes cycle through would be four identical states here, and the two
        /// vertical up-faces are the two a ladder has no use for.</summary>
        private static readonly int[] LadderCycle = { 2, 3, 4, 5, ShapeCode.UpPlusY };

        /// <summary>One step of the rotate-key cycle. Shaped blocks and the crafted staircase walk
        /// Auto → the 24 up-face × quarter-turn orientations → Auto; furniture walks Auto → the four quarter
        /// turns → Auto (its up-face is pinned to +Y server-side, see the rotate-key comment); the ladder
        /// walks its own five mount states. <paramref name="backwards"/> reverses the walk.</summary>
        private void StepPlaceOrientation(bool backwards, PropOrientation cycle)
        {
            if (cycle == PropOrientation.LadderMount)
            {
                _placeYaw = 0; // meaningless for both ladder forms — never send a turn the server drops
                int at = _placeUpFace < 0 ? -1 : System.Array.IndexOf(LadderCycle, _placeUpFace);
                int next = at + (backwards ? -1 : 1);
                if (next >= LadderCycle.Length) { next = -1; }        // past the last state → back to Auto
                else if (next < -1) { next = LadderCycle.Length - 1; } // before Auto → wrap to the last state
                _placeUpFace = next < 0 ? -1 : LadderCycle[next];
                return;
            }

            bool yawOnly = cycle == PropOrientation.YawOnly;
            int lastUpFace = yawOnly ? 0 : 5;
            if (_placeUpFace < 0)
            {
                // Leaving Auto: forwards starts at the first orientation, backwards at the last.
                _placeUpFace = backwards ? lastUpFace : 0;
                _placeYaw = backwards ? 3 : 0;
                return;
            }

            if (yawOnly)
            {
                _placeUpFace = 0; // a stale up-face from a previously held shaped block never sticks to furniture
            }

            if (!backwards)
            {
                if (_placeYaw < 3) { _placeYaw++; }
                else if (_placeUpFace < lastUpFace) { _placeUpFace++; _placeYaw = 0; }
                else { _placeUpFace = -1; _placeYaw = -1; } // back to Auto
            }
            else
            {
                if (_placeYaw > 0) { _placeYaw--; }
                else if (_placeUpFace > 0) { _placeUpFace--; _placeYaw = 3; }
                else { _placeUpFace = -1; _placeYaw = -1; } // back to Auto
            }
        }

        /// <summary>
        /// The exact form + orientation the next place would carry, for the aim the player is holding right
        /// now. Returns false when the held item places nothing orientable, in which case the place sends the
        /// raw override fields and the server decides — same as before #909.
        /// <para>
        /// In Auto the client answers the orientation question ITSELF instead of leaving it to the server,
        /// because it knows one thing the intent cannot carry: which block FACE the player clicked. That is
        /// what makes a ladder hug the wall you aimed at rather than whichever neighbour wins a fixed scan
        /// order. The server keeps its own derivation for intents that carry no up-face (older clients, its
        /// own internal placements), so nothing depends on this being sent.
        /// </para>
        /// </summary>
        private bool PendingPlacement(string held, Vector3Int hitCell, Vector3Int placeCell,
            out int shape, out int upFace, out int yaw)
        {
            shape = HeldPlaceShape(held, out var cycle);
            upFace = _placeUpFace;
            yaw = _placeYaw;
            if (shape <= 0)
            {
                return false;
            }

            // The face the player clicked: the step from the block they aimed at to the cell being filled.
            int clicked = ShapeCode.FaceFromDirection(
                placeCell.x - hitCell.x, placeCell.y - hitCell.y, placeCell.z - hitCell.z);

            if (yaw < 0)
            {
                yaw = ((int)Mathf.Round(transform.eulerAngles.y / 90f)) & 3;
            }

            switch (cycle)
            {
                case PropOrientation.LadderMount:
                {
                    if (upFace < 0)
                    {
                        upFace = PropShapes.DeriveLadderMount(face => IsLadderMountWall(placeCell, face), clicked);
                    }

                    var form = PropShapes.LadderForm(upFace);
                    shape = form.Shape;
                    upFace = form.UpFace;
                    yaw = 0;
                    break;
                }

                case PropOrientation.YawOnly:
                    upFace = ShapeCode.UpPlusY; // the server pins it; promising anything else would be a lie
                    break;

                default:
                    if (upFace < 0)
                    {
                        upFace = DeriveAutoUpFace(placeCell, clicked);
                    }

                    break;
            }

            return true;
        }

        /// <summary>True when the cell on the far side of <paramref name="upFace"/> is a wall a ladder plate
        /// can hang on. The up-face points AWAY from the plate's support, so the wall sits at the opposite
        /// offset. Uses the mesher's own classification, so the preview and the drawn ladder cannot disagree.</summary>
        private bool IsLadderMountWall(Vector3Int cell, int upFace)
        {
            var dir = ShapeCode.FaceDirection(upFace);
            return ChunkMesher.IsLadderMountWall(
                Game.Content, Game.World.GetBlock(cell.x - dir.X, cell.y - dir.Y, cell.z - dir.Z));
        }

        /// <summary>Refreshes the placement ghost (#863): while a rotatable block is held and the aim ray
        /// has a place cell, the exact form + pending orientation hovers there translucently. Runs only on
        /// the on-foot path — every other state (menus, space view, driving, seated) is caught by the
        /// frame-stamp check in <see cref="LateUpdate"/>, which hides the ghost after any frame that never
        /// reached this method. Parked-ship cells get no ghost: placing there routes through a structure
        /// edit whose own rules (hull protection) this preview cannot promise.</summary>
        private void UpdatePlacementGhost()
        {
            _ghostFrame = Time.frameCount;
            string held = Game != null ? Game.ItemInSlot(Game.SelectedHotbarSlot) : null;
            bool rotatable = HeldPlaceShape(held, out _) > 0;
            if (Game != null)
            {
                // Drives the HUD's "R — rotate" control hint. Answered from the held item alone, so the hint
                // does not flicker while the crosshair sweeps past the sky.
                Game.HoldingRotatableBlock = rotatable;
            }

            if (!rotatable || !AimTarget(out var hitCell, out var placeCell, out var aimedShip) || aimedShip != null
                || !PendingPlacement(held, hitCell, placeCell, out int shape, out int upFace, out int yaw))
            {
                _placementGhost?.Hide();
                return;
            }

            // Shows exactly what the place will send (PendingPlacement feeds both), so the hologram cannot
            // promise a form or an orientation the placed block then contradicts.
            _placementGhost ??= new PlacementGhost();
            _placementGhost.Show(placeCell, shape, yaw, upFace);
        }

        /// <summary>The up-face for an Auto placement: the shape's base rests on the surface it was built
        /// against — the floor first (→ +Y up, the common case of laying a slab on the ground), then the WALL
        /// THE PLAYER CLICKED, then the remaining walls in the server's fixed scan order, then the ceiling.
        /// Fluids are no surface to build against, same as the server.
        /// <para>
        /// The clicked-face preference (#909) is the one thing the client can answer better than
        /// <c>GameServer.DeriveShapeUpFace</c>, whose intent carries no aim: it is what lets you build a ramp
        /// against the wall you are looking at instead of whichever neighbour the scan order happens to reach
        /// first. The floor still wins over it — extending a floor by clicking the side of the last slab must
        /// keep laying it flat, not stand it up against that slab.
        /// </para></summary>
        private int DeriveAutoUpFace(Vector3Int cell, int clickedFace)
        {
            bool Supported(int face)
            {
                var dir = ShapeCode.FaceDirection(face);
                var id = Game.World.GetBlock(cell.x - dir.X, cell.y - dir.Y, cell.z - dir.Z);
                return !id.IsAir && !IsFluidBlock(id);
            }

            if (Supported(ShapeCode.UpPlusY))
            {
                return ShapeCode.UpPlusY; // floor below → upright
            }

            if (clickedFace >= 2 && clickedFace <= 5 && Supported(clickedFace))
            {
                return clickedFace; // the wall under the crosshair
            }

            foreach (int face in ShapeCode.WallFaces)
            {
                if (Supported(face))
                {
                    return face;
                }
            }

            return Supported(1) ? 1 : ShapeCode.UpPlusY; // ceiling, else nothing to rest on
        }

        private void LateUpdate()
        {
            // The on-foot update never ran this frame (menu, space view, speeder, seat, veil, …): whatever
            // the ghost was showing is stale — hide it and drop the HUD rotate hint with it.
            if (_ghostFrame != Time.frameCount)
            {
                _placementGhost?.Hide();
                if (Game != null)
                {
                    Game.HoldingRotatableBlock = false;
                }
            }
        }

        private void OnDestroy()
        {
            _placementGhost?.Destroy();
            _placementGhost = null;
        }

        /// <summary>Localized, kid-readable label for the pending orientation: "Auto", or an up-face word
        /// (upright / upside down / on its side) plus the quarter-turn in degrees — instead of the old
        /// axis-speak ("+X · 90°") nobody without a maths degree could predict (#863). The placement ghost
        /// shows the exact result; this label just needs to say roughly what changed.</summary>
        private string OrientationLabel(PropOrientation cycle)
        {
            var loc = Game?.Localizer;
            if (_placeUpFace < 0)
            {
                return loc?.Get("hud.shape.auto") ?? "Auto";
            }

            if (cycle == PropOrientation.LadderMount)
            {
                // Which of the four walls it is naming would need compass words nobody can map to a key press —
                // the ghost hanging on that exact wall says it better than any label could.
                return _placeUpFace >= 2
                    ? loc?.Get("hud.shape.on_wall") ?? "On the wall"
                    : loc?.Get("hud.shape.free_standing") ?? "Free-standing";
            }

            int upFace = cycle == PropOrientation.YawOnly ? 0 : _placeUpFace;
            string word = upFace switch
            {
                0 => loc?.Get("hud.shape.upright") ?? "Upright",
                1 => loc?.Get("hud.shape.upside_down") ?? "Upside down",
                _ => loc?.Get("hud.shape.sideways") ?? "On its side",
            };
            return _placeYaw > 0 ? $"{word} · {_placeYaw * 90}°" : word;
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
