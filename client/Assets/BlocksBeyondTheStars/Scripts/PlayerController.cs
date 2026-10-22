// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
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
    public sealed partial class PlayerController : MonoBehaviour
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
        // #2276: above a PLANET's line, with no vertical control held, the suit sinks gently back toward the air instead
        // of hovering forever — a way down that needs no knowledge (a station's zero-g keeps hovering). Consts, not
        // serialized fields, so no scene can override them.
        private const float SpaceSinkSpeed = 1.5f;
        // #2276: after sinking back below the line ("re-entry") the suit's brake thrusters cap the fall until the first
        // landing — always below the safe landing speed, so the descent from a summit or a tower never hurts.
        private const float ReentryMaxFallSpeed = 10f;
        private bool _reentryBrake;      // armed at re-entry, cleared at the first landing (ground, water, ladder, wall, flight)
        private bool _wasAbovePlanet;    // last frame's Game.OnFootAbovePlanet, to see the re-entry edge
        // Swimming: in water the player drifts down slowly and holds Jump to rise / surface (no fast falls).
        public float SwimUpSpeed = 4f;     // rise speed while holding Jump underwater
        public float SwimSinkSpeed = 1.5f; // gentle idle sink toward the seabed
        public float SwimAccel = 12f;      // how fast vertical speed eases toward the swim target
        public float SwimSpeedMul = 0.62f; // horizontal movement is slower in water
        // #2134: the dense gas under a gas giant's gas sea — consts, not serialized fields, so no scene can override them.
        private const float DenseGasSinkSpeed = 3.5f; // idle, it pulls you down more than twice as fast as water
        private const float DenseGasRiseSpeed = 1.2f; // holding Jump only wins a slow climb
        private const float DenseGasSpeedMul = 0.4f;  // every stroke is heavy
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
        private const float SneakEdgeOverhang = 0.2f; // how far the centre may hang past an edge while sneaking before the stop fires (#1309, #1486)
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

        /// <summary>Other players (wired by WorldRig) — the quick "is somebody sitting there?" look (#2122).</summary>
        public RemotePlayers Remotes;

        private CharacterController _controller;
        private float _pitch;

        private float _verticalVelocity;
        private float _moveSendTimer;
        private bool _spawned;
        private Vector3 _spawnPos;
        private bool _settling;
        private float _settleTimer; // how long we've been frozen at spawn waiting for the floor to stream
        private bool _worldRevealed; // settle: has the loading overlay been dismissed for this spawn yet
        private bool _awaitingFloor;   // released on the grace timer with no floor yet — hover instead of falling
        private bool _snapOntoFloor;   // this settle came from a server snap onto a floor cell (respawn / beam / landing)

        /// <summary>How far below a server snap the ground may be before it counts as "the floor under us":
        /// the ray starts 0.5 m up, so this allows the pad itself plus a one-block step down. A collider
        /// further down (the room under a one-block slab, in a chunk that had already meshed) used to
        /// release the settle freeze, and the player fell through the slab whose own chunk had not
        /// streamed yet — Lyxette's beam pad on a thin second-floor ceiling (#1276).</summary>
        private const float SnapFloorMaxDrop = 1.6f;

        /// <summary>A ground hit this close under a snapped body is the floor cell the server put the feet
        /// on — no further proof needed. Anything deeper (up to <see cref="SnapFloorMaxDrop"/>) must be backed
        /// by a streamed chunk under the feet (#1449).</summary>
        private const float SnapFloorOnIt = 0.6f;

        /// <summary>Longest a same-world snap (heal-tank, beam, respawn) waits for the view-settle gate once
        /// the floor is confirmed (#1462) — the world is already on screen, there is no veil to hold.</summary>
        private const float SnapQuietCapSeconds = 1.0f;

        /// <summary>Settle freezes shorter than this pass unnoticed; longer ones show the HUD hint.</summary>
        private const float SettlingHintAfterSeconds = 0.4f;
        private float _awaitFloorTimer;

        /// <summary>Whether the client actually holds the chunk under <paramref name="feet"/> — false while it
        /// is still streaming, in which case "air below" means "unknown", not "open" (#1449). Since #1529 a
        /// streamed chunk may still carry an un-cooked collider, so the chunk must also have its collision
        /// cooked (#1583): a collider metres further down answering the ray is not this chunk's floor.</summary>
        private bool FootingKnown(Vector3 feet)
            => Game?.World == null
               || (Game.World.TryGetBlock(Mathf.FloorToInt(feet.x), Mathf.FloorToInt(feet.y - 0.05f), Mathf.FloorToInt(feet.z), out _)
                   && Game.ChunkColliderReadyAt(new Vector3(feet.x, feet.y - 0.05f, feet.z)));

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

        private float _stepTimer;
        private int _lastWorldEpoch;

        /// <summary>#1998: a push from a stomp or a sandworm strike (horizontal, blocks/s), easing off.</summary>
        private Vector3 _knock;

        /// <summary>#1998: every environment raycast of the player (ground snap, spawn search, headroom, camera boom)
        /// ignores the giants' colliders — they block the capsule, but nobody should "stand" on a foot or snap onto a leg.</summary>
        private static int WorldRayMask => ~(1 << CreatureView.GiantLayer);

        /// <summary>#1998: shakes the camera (0..1), e.g. a giant's footfall or a sandworm breaching nearby. Since #2152 it
        /// adds trauma to <see cref="FxCamera"/> (trauma² rotational shake); the Camera-motion comfort setting and the
        /// Screen-shake slider still scale it away.</summary>
        public void AddCameraShake(float amount) => FxCamera.AddTrauma(Mathf.Clamp01(amount) * 0.85f);

        /// <summary>#1998: knocks the player away (a stomp, a strike): an upward pop plus a horizontal push that eases off.</summary>
        public void ApplyKnock(Vector3 impulse)
        {
            _knock = new Vector3(impulse.x, 0f, impulse.z);
            if (impulse.y > 0f)
            {
                _verticalVelocity = Mathf.Max(_verticalVelocity, impulse.y);
            }
        }

        private bool _moving;

        private void Awake() => _controller = GetComponent<CharacterController>();

        private void Start()
        {
            // First-person viewmodel lives on the camera (shown when the avatar is hidden).
            if (Camera != null)
            {
                _viewmodel = Camera.gameObject.AddComponent<Viewmodel>();
                _viewmodel.Game = Game;
                _baseFov = Camera.fieldOfView; // WorldRig applied the settings value (#1589) before this runs
                _viewmodel.SetReferenceFov(_baseFov);
            }

            ApplyCameraMode();
        }

        private void Update()
        {
            RefreshLiquidKeys(); // #2106: cheap (a reference compare) once the content is known
            RecomputeGravity(); // keep the live movement constants in step with this world's gravity factor
            UpdateRopeFrame(); // #2321: the rope's line, poses and the states it cannot survive — before any early return

            // #2291: our own cloak, seen in third person — the figure turns to shimmering glass (a no-op unless it flips).
            Avatar?.SetStealthShimmer(Game != null && Game.Stealthed);

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
                _snapOntoFloor = false; // a world spawn may sit a little above the terrain — any ground below counts
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
                _snapOntoFloor = true; // heal-tank / beam pad / landing: the feet are ON a floor cell (#1276)
            }

            TickRest(); // #2823: before any early return — a snap above, the flight view or a cutscene ends the rest

            // Until the server has told us where we are, the controller must not exist as far as the
            // simulation is concerned: the scene-default transform sits near the world origin, and letting
            // gravity + SendMovement run from there streamed that bogus position to the server at 10 Hz —
            // which trusted it, overwrote the freshly computed ship spawn, and left new players entombed in
            // the origin column for the void rescue to dig into a random cave (#865, the root cause of #834).
            if (!_spawned && Game != null)
            {
                UpdateJetpack(false);
                UpdateGlide(false);
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
                UpdateGlide(false); // #2296: a teleport folds the wing too
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

                // Solid ground loaded below the spawn? (the chunk's MeshCollider exists). After a server snap
                // onto a floor cell it has to be THAT floor — a collider metres further down is the wrong
                // chunk answering (#1276).
                // A step down (0.6 m < hit ≤ 1.6 m) only counts when the cell under the feet is KNOWN — its chunk
                // has streamed and says "air". Otherwise the collider answering is terrain under a slab whose
                // chunk has not arrived yet, and releasing on it drops the player through that slab once it
                // does (#1449, the beam pad on a mine ceiling). A hit right under the feet is the floor itself.
                bool groundBelow = Physics.Raycast(_spawnPos + Vector3.up * 0.5f, Vector3.down, out var gHit, 10f, WorldRayMask)
                                   && gHit.collider != _controller
                                   && (!_snapOntoFloor
                                       || gHit.distance <= SnapFloorOnIt
                                       || (gHit.distance <= SnapFloorMaxDrop && FootingKnown(_spawnPos)));

                // The streamed view has finished arriving AND meshing — so the reveal shows a populated world
                // instead of one that visibly assembles over the next few seconds (#390). While the server is
                // still streaming, chunks keep arriving each tick and reset TimeSinceLastChunk; the gap only opens
                // once the frozen spawn view is complete, and the backlog check confirms the last ones are meshed.
                bool viewSettled = Game.TimeSinceLastChunk >= ViewSettleQuietSeconds
                                   && Game.PendingMeshCount <= ViewSettleBacklog;

                // A snap inside an already-revealed world (heal-tank retrieval, beam, same-world respawn) has
                // nothing to reveal: the view-settle gate is about the veil, and a browser client meshing two
                // chunks a frame rarely drains its backlog below the threshold — so the player stood frozen
                // in a fully drawn ship for the whole 8 s grace with no hint why (#1462). With the floor under
                // the feet confirmed, a short quiet window is all that snap needs.
                bool quickSnap = _snapOntoFloor && Game.WorldReady && _settleTimer > SnapQuietCapSeconds;

                // Something to read while the freeze lasts longer than a blink — "Stabilising…" beats a mute
                // body that ignores WASD (#1462).
                Game.SettlingHint = !awaitingConfirm && _settleTimer > SettlingHintAfterSeconds;

                // Reveal the world + release control once there is real ground under the spawn AND the view has
                // settled, or after a short grace so the veil never lingers or feels stuck. Releasing on that
                // grace alone used to hand the player straight into free fall through terrain that had not
                // streamed yet — an 8-second drop into the void followed by the server's rescue teleports, which
                // is what a slow (browser) client got on every first join (#773). Gravity is therefore held off
                // separately until a floor actually exists: the veil lifts on time, the player just doesn't fall.
                if (!awaitingConfirm && ((groundBelow && (viewSettled || quickSnap)) || _settleTimer > SettleGraceSeconds))
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
                    Game.SettlingHint = false;
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
                UpdateGlide(false);
                // Entering space skips the per-frame UpdateLamp() below, so the suit headlamp's
                // shader global would otherwise stay lit. Turn it off once on the way in.
                if (_lampOn)
                {
                    _lampOn = false;
                    UpdateLamp();
                }
                return;
            }

            // #2129: aboard a train the body is a child of the moving wagon. Physics.autoSyncTransforms is off in this
            // project, so CharacterController.Move starts from the last SIMULATED position and would throw away the
            // wagon's motion since the last physics step — the rider drifted to the rear and fell out. Sync the moved
            // wagon (its floor collider and our body) into physics before anything below moves us, the menu branch
            // (the cab panel) included. TrainView runs earlier in the frame, so the wagons are already posed.
            if (_trainFrame != null)
            {
                Physics.SyncTransforms();
            }

            // A UI panel or the chat input is open: don't steer/interact, just settle by gravity — unless
            // we're driving a speeder: on-foot gravity would drag the player out of hover under the menu
            // and yank them back up on close (#413 N4). Hold the hover position instead. A running camera
            // cinematic (#760) freezes control the same way — and by skipping the per-frame eye-pose
            // re-assert below, it leaves the camera to the cinematic's LateUpdate.
            //
            // The on-screen keyboard (#1211) belongs in this list for a reason of its own: it picks letters
            // with the SAME left stick that walks the player, so without this the pad would spell a word and
            // wander off at once.
            //
            // Every cutscene freezes control the same way (#2461): the ending used to let the player walk and mine
            // "blind" behind its darkened stage.
            if (Game != null && (Game.MenuOpen || Game.ChatTyping || Game.CinematicCameraActive
                                 || GameBootstrap.CutsceneActive || OnScreenKeyboardUi.IsOpen))
            {
                // The jetpack stops (it drains energy server-side). The glider costs nothing, so a wing open when the
                // panel came up stays open and ApplyGravityOnly keeps its slow sink (#2296) — a plunge at full gravity
                // behind a menu would be a fall the player never chose.
                UpdateJetpack(false);
                if (string.IsNullOrEmpty(Game.InSpeeder) && _seatCell is null && _restCell is null)
                {
                    ApplyGravityOnly(); // seated: the controller is disabled and the chair holds us anyway; resting: we stand still
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
                UpdateGlide(false);
                EndClimb();
                DriveSpeeder();
                SendMovement();
                Game.PlayerPosition = transform.position;
                Game.PlayerYaw = transform.eulerAngles.y;
                Game.PlayerVerticalVelocity = 0f;
                return;
            }

            // #2113: aboard a train the wagon is the reference frame — the body is parented to it, so its motion carries
            // us; a seated rider is parked on the seat, a standing one walks on with the ordinary on-foot code below.
            if (UpdateTrainFrame())
            {
                UpdateJetpack(false);
                UpdateGlide(false);
                EndClimb(); // no wall climbing aboard a moving wagon
                if (Game.TrainSeat >= 0)
                {
                    HoldTrainSeat();
                    LookAround();
                    if (Camera != null && !ThirdPerson)
                    {
                        Camera.transform.localPosition = Vector3.Lerp(Camera.transform.localPosition, SeatedEye, Time.deltaTime * 8f);
                    }

                    bool wantsUp = InputMap.JumpDown() || InputMap.CrouchHeld() || Mathf.Abs(InputMap.MoveX()) > 0.3f || Mathf.Abs(InputMap.MoveY()) > 0.3f;
                    if (InputMap.Down(InputAction.SpeederExit))
                    {
                        Game.Network?.SendExitTrain();
                    }
                    else if (wantsUp && RailRules.TryParseFrame(Game.InTrain, out string sitTrain, out int sitWagon))
                    {
                        Game.Network?.SendEnterTrain(sitTrain, sitWagon, -1); // stand up in the same wagon
                    }

                    SendMovement();
                    Game.PlayerPosition = transform.position;
                    Game.PlayerYaw = transform.eulerAngles.y;
                    Game.PlayerVerticalVelocity = 0f;
                    return;
                }

                if (InputMap.Down(InputAction.SpeederExit))
                {
                    Game.Network?.SendExitTrain();
                }
                else if (InputMap.Down(InputAction.Interact) && TrainInteract())
                {
                    return;
                }
            }

            // Sitting on a chair (#806): control frozen, look free. Stand with E/jump/crouch/movement —
            // or when the chair vanishes under us (mined, reshaped, chunk unloaded).
            if (_seatCell is { } seat)
            {
                UpdateJetpack(false);
                UpdateGlide(false);
                EndClimb();
                LookAround();
                if (Camera != null && !ThirdPerson)
                {
                    Camera.transform.localPosition = Vector3.Lerp(Camera.transform.localPosition, SeatedEye, Time.deltaTime * 8f);
                }

                bool chairGone = Game?.World == null
                    || Game.Health <= 0f // dying stands you up so the respawn teleport gets a live controller
                    || Game.SeatRejections != _seatRejectionsSeen // #2122: the server says somebody sits there (toast shown)
                    || !FurnitureShapes.IsSeat(ShapeCode.ShapeOf(Game.World.GetShape(seat.x, seat.y, seat.z)));
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
                Game.PlayerVerticalVelocity = 0f;
                return;
            }

            // Lying in a bed (#2823): control frozen, look free. Up again with E/jump/crouch/movement — the same keys and
            // sticks that stand you up from a chair; TickRest gets you up when the bed vanishes or anything else takes over.
            if (_restCell is { } bed)
            {
                UpdateJetpack(false);
                UpdateGlide(false);
                EndClimb();
                LookAround();
                HoldRestView(bed);
                if (RestWantsUp())
                {
                    GetUp(instant: false);
                }

                SendMovement(); // the position stays where we stood: only the keep-alive (#964)
                Game.PlayerPosition = transform.position;
                Game.PlayerYaw = transform.eulerAngles.y;
                Game.PlayerVerticalVelocity = 0f;
                return;
            }

            // Just stepped out of a speeder → restore the on-foot camera + viewmodel.
            if (_wasDriving)
            {
                _wasDriving = false;
                ApplyCameraMode();
                ClientAudio.Instance?.SpeederStop(_drivingBoat);
                if (Game != null)
                {
                    Game.VehicleAground = false;
                }
            }

            // On foot: board a speeder you own that you're standing next to (E), or pack one up (X). Checked
            // before the generic E interact so boarding the speeder beside you wins.
            if (InputMap.Down(InputAction.Interact) && TryBoardNearbySpeeder())
            {
                return;
            }

            if (InputMap.Down(InputAction.Interact) && TryBoardNearbyTrain()) // #2113
            {
                return;
            }

            StepAboardTrain(); // #2129: walking or jumping into a wagon boards it too

            if (InputMap.Down(InputAction.StowVehicle) && TryStowNearbySpeeder())
            {
                return;
            }

            // Same key at the own cockpit/console: ask the ship for a vehicle left out on this world (#1661).
            if (InputMap.Down(InputAction.RecallVehicle) && TryRecallVehiclesAtConsole())
            {
                return;
            }

            // "Look here!" ping (#1217): a transient marker at the crosshair for allies + crew. The server
            // owns TTL + rate limit, so mashing the key stays harmless.
            if (InputMap.Down(InputAction.PingMarker))
            {
                PingAtCrosshair();
            }

            // #2568: not while the same key is the held push-to-talk key (a rebind onto it) — talking must not flip the view.
            if (InputMap.Down(InputAction.ToggleThirdPerson) && !VoiceChat.TalkKeyClaims(InputAction.ToggleThirdPerson))
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
                if (HeldOrientable(held, out var heldCycle))
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
                TriggerSwing(); // first: the gloves pick the punching hand the effect starts at (#2278)
                AttackNearestEnemy();
            }

            if (InputMap.Down(InputAction.LootContainer))
            {
                LootNearestContainer();
            }

            // #2018: Feed — one piece of the held food flies to the begging herd. The server validates food + a beggar near;
            // the probe keeps the key silent when nothing is hungry, so Q never eats or wastes anything by accident.
            if (InputMap.Down(InputAction.FeedCreature) && CanFeedCreature)
            {
                Game.Network?.SendThrowFood(Game.SelectedHotbarSlot);
                ClientAudio.Instance?.Cue("loot");
            }

            if (InputMap.Down(InputAction.DepositToCrate))
            {
                DepositToNearestCrate();
            }

            if (InputMap.Down(InputAction.RepairWreck))
            {
                // R at your own cockpit / ship console repairs the SHIP (#1561): the HUD panel's button was the
                // only sender of the repair intent, and it cannot be clicked while the cursor is locked.
                if (!RepairOwnShipAtConsole())
                {
                    if (Game.ShipRepair != null && !NearWreck)
                    {
                        // The repair panel is up but this is neither a terminal nor a wreck: say where to go. R used to
                        // do nothing here, which read as "the repair cannot be triggered" (2026-10-09).
                        Game.ShowMessage(Game.Localizer?.Get("ui.shiprepair.go_to_console")
                            ?? "To repair, stand at the cockpit or the ship console inside your ship.");
                    }
                    else
                    {
                        RepairWreckCell();
                    }
                }
            }

            if (InputMap.Down(InputAction.ToggleLamp))
            {
                ToggleLamp();
            }

            if (InputMap.Down(InputAction.ToggleStealth))
            {
                ToggleStealth(); // #2291: on foot only — in EVA the same B deploys a station (SpaceView owns that frame)
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

            // Publish local pose for the HUD minimap/compass — and the vertical speed for the collider cook-ahead (#1583).
            if (Game != null)
            {
                Game.PlayerPosition = transform.position;
                Game.PlayerYaw = transform.eulerAngles.y;
                Game.PlayerVerticalVelocity = _verticalVelocity;
                HandleStations();
            }
        }

        /// <summary>Capture hook (#2405): true while the spawn snap still owns the body — before the server's spawn
        /// arrived, or during the settle freeze that pins the transform to the spawn every frame until the floor chunk
        /// is in (or the 8 s grace runs out). A pose taken during the freeze is undone the next frame, so a capture
        /// waits for this to clear first.</summary>
        public bool IsSettling => !_spawned || _settling;

        /// <summary>Capture diagnostics (#2405): the control state a look-check run logs when a pose does not take —
        /// which early return in <see cref="Update"/> owns the body, and whether the server still counts us aboard.</summary>
        public string CaptureDebugState()
            => $"pos={transform.position} published={Game?.PlayerPosition} spawned={_spawned} settling={_settling} awaitingFloor={_awaitingFloor} "
               + $"controller={(_controller != null && _controller.enabled)} grounded={IsCaptureGrounded} seat={_seatCell} "
               + $"menu={Game?.MenuOpen} chat={Game?.ChatTyping} cinematic={Game?.CinematicCameraActive} osk={OnScreenKeyboardUi.IsOpen} "
               + $"aboard={Game?.Aboard} epoch={Game?.WorldEpoch} space={Game?.SpaceViewActive} train='{Game?.InTrain}' speeder='{Game?.InSpeeder}'";

        /// <summary>Capture hook: the player's head is under water (so the shot would be a submerged murk).</summary>
        public bool IsHeadUnderwater() => IsSubmerged();

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
            if (_restLevel > 0f)
            {
                ApplyRestLook(0f); // #2823: leaving the world mid-rest must not keep the music quiet or the next world dimmed
            }
        }

        private void SendMovement()
        {
            _moveSendTimer += Time.deltaTime;
            if (_moveSendTimer < 0.1f || Game?.Network == null)
            {
                return; // ~10 position updates per second (unreliable channel)
            }

            _moveSendTimer = 0f;
            if (_trainFrame != null && !string.IsNullOrEmpty(Game.InTrain))
            {
                // #2113: aboard — the pose is the wagon-local offset in the train's frame. Out of the box = we walked off.
                var local = _trainFrame.InverseTransformPoint(transform.position);
                var l = new Vector3f(local.x, local.y, local.z);
                if (RailRules.OutsideWagon(l))
                {
                    // #2129: through the open end into the next wagon of the same train is not leaving — the frame moves
                    // there. Meanwhile the report stays inside the old wagon's box (the raw spot out here would read as
                    // walking off), and keeps flowing: the server drops a client silent for 90 s (#964).
                    if (CrossToNeighbourWagon())
                    {
                        Game.Network.SendFramedMove(Game.InTrain, RailRules.ClampLocal(l), transform.eulerAngles.y, _pitch);
                        return;
                    }

                    // Walked off: report the spot in the frame, so the server sets us down where we stepped off (an
                    // ExitTrain would put us beside the wagon instead), and stop riding on our side at once.
                    Game.Network.SendFramedMove(Game.InTrain, l, transform.eulerAngles.y, _pitch);
                    _leavingFrame = Game.InTrain;
                    LeaveTrainFrame();
                    return;
                }

                Game.Network.SendFramedMove(Game.InTrain, l, transform.eulerAngles.y, _pitch);
                return;
            }

            Game.Network.SendMove(transform.position, transform.eulerAngles.y, _pitch, _climbPose, _crouched); // #2193: others see us climb; #2435: and crouch
        }

        private static Vector3Int FloorVec(Vector3 v)
            => new Vector3Int(Mathf.FloorToInt(v.x), Mathf.FloorToInt(v.y), Mathf.FloorToInt(v.z));
    }
}
