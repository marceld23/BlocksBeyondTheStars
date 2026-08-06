// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Cinematic staging for the first-spawn VEGA prologue (#760). The server trigger, the three
    /// <c>vega.prologue.*</c> pages and the per-save <c>vega:intro</c> gate stay exactly as they are —
    /// this only adds the stage: letterbox bars, a slow exterior orbit of the player's ACTUAL landed
    /// ship for page 1, a push-in toward the cockpit for page 2, and a snap back to the seat view with
    /// a glitch flash + radio crackle as VEGA "boots" for page 3. Purely client-side (multiplayer-safe:
    /// others just see the player seated). <see cref="VegaPanel"/> drives it per prologue line; the
    /// panel itself keeps showing the text, subtitle-style.
    ///
    /// The orbit is terrain-aware (#777): <see cref="CinematicStageScan"/> probes the voxel world at
    /// Begin() to pick a clear height and open arc (ping-ponging across it when a mountainside blocks
    /// the full circle), a per-frame line-of-sight net pulls the camera in/up out of late-streamed
    /// terrain, and a ship with no clear shot at all falls back to the classic dim + panel prologue.
    ///
    /// The camera override runs in LateUpdate at execution order 210, after every gameplay writer, and
    /// only while <see cref="GameBootstrap.CinematicCameraActive"/> also freezes on-foot control — the
    /// moment it is released, <see cref="PlayerController"/> re-asserts the normal eye pose next frame.
    /// </summary>
    [DefaultExecutionOrder(210)]
    public sealed class PrologueCinematic : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        // Absolute safety net only: the veil's own MaxShow (25 s) always drops it first. The old 12 s
        // give-up was SHORTER than a fresh world's generation — the whole staged prologue then played
        // invisibly behind the curtain (chatter audible, page 1 already waiting on [N] at reveal).
        private const float HoldTimeout = 60f;
        private const float LetterboxSeconds = 0.5f;
        private const float OrbitDegPerSecond = 8f;

        // Terrain-aware staging (#777): the orbit ring is scanned against the voxel world before the
        // shot, and a per-frame line-of-sight net keeps the camera out of late-streamed terrain.
        private const int RingSamples = 72;      // 5° ring resolution for the pre-scan
        private const float MinArcSweep = 110f;  // an orbit needs at least this much open sky to read as one
        private const float ArcEdgeMargin = 10f; // ping-pong turnaround this far before the blocked edge
        private const int MaxPull = 6;           // safety-net levels: each pulls the radius in and raises the camera
        private const float PullRadiusStep = 0.13f;
        private const float PullHeightStep = 1.5f;

        private WorldLoadingOverlay _overlay;
        private CinematicFrame _frame;
        private bool _active;
        private bool _cameraOverride;
        private bool _ending;
        private float _holdStart = -1f;
        private Viewmodel _viewmodel; // hidden during the sequence (a floating drill breaks the shot)
        private Vector3 _center;
        private float _angle;
        private float _radius, _height, _targetRadius, _targetHeight;
        private float _letter;
        private float _flash;

        private CinematicStageScan.ClearSampler _sampler;
        private float _stageRadius, _stageHeight; // page-1 settle orbit (chosen by the pre-scan)
        private float _pushRadius, _pushHeight;   // page-2 push-in target
        private bool _arcLimited;
        private float _arcCenter, _arcHalf;       // degrees; only meaningful while _arcLimited
        private float _orbitDir = 1f;
        private float _safetyPull;                // smoothed 0..MaxPull terrain-avoidance blend
        private Vector3 _lastPos;                 // last known-clear pose (held if everything is blocked)
        private Quaternion _lastRot;
        private bool _hasLastPose;

        private void Start() => _overlay = FindAnyObjectByType<WorldLoadingOverlay>();

        public bool Active => _active;

        /// <summary>True while <see cref="VegaPanel"/> should wait before dequeuing ANY line: the world
        /// is behind the loading veil, so a page would type into blackness and burn its auto-advance.
        /// The timeout measures the CONTINUOUS hold (it resets whenever the veil is down), so later
        /// landings/boardings hold correctly too. The veil's own MaxShow (25 s) caps it in practice.</summary>
        public bool HoldQueue
        {
            get
            {
                bool veilUp = Game != null && !Game.SpaceViewActive && _overlay != null && _overlay.VeilActive;
                if (!veilUp)
                {
                    _holdStart = -1f;
                    return false;
                }

                if (_holdStart < 0f)
                {
                    _holdStart = Time.time;
                }

                return Time.time - _holdStart <= HoldTimeout;
            }
        }

        /// <summary>Tries to start the staged sequence. False when the scene can't carry it at all
        /// (space view active, camera missing) OR when the terrain pre-scan finds no clear shot around
        /// the ship (#777) — the caller then keeps today's dim + panel behaviour.
        /// Deliberately NOT gated on the authoritative Aboard flag: that packet can trail WorldReady by
        /// several seconds (the ScreenshotDirector lesson) and silently downgraded the whole staging.</summary>
        public bool Begin()
        {
            if (_active || Game == null || Camera == null || Game.SpaceViewActive)
            {
                Debug.Log($"[Cinematic] Prologue staging fallback (active={_active}, cam={Camera != null}, space={Game?.SpaceViewActive}).");
                return false;
            }

            // Orbit centre: the ship if its placement already arrived, else the player/camera — on a
            // fresh story world the player wakes at the helm, so all three coincide well enough.
            _center = Game.ShipPosition
                      ?? (Game.PlayerPosition != Vector3.zero ? Game.PlayerPosition : Camera.transform.position);
            Debug.Log($"[Cinematic] Prologue staged around {(Game.ShipPosition.HasValue ? "ship" : "player")} at {_center} (aboard={Game.Aboard}).");

            // Terrain-aware staging (#777): scan the orbit ring against the voxel world and pick a clear
            // height/arc up front — a ship on a mountainside must not put the camera inside the slope.
            // Unloaded chunks count as blocked, never as open space. No clear staging at all (crater,
            // canyon, world missing) → the caller keeps today's dim + panel behaviour.
            var world = Game.World;
            if (world == null)
            {
                Debug.Log("[Cinematic] Prologue staging fallback: no world to scan the orbit against.");
                return false;
            }

            _sampler = (x, y, z) => world.TryGetBlock(x, y, z, out var b) && b.IsAir;
            if (!ChooseStaging())
            {
                Debug.Log("[Cinematic] Prologue staging fallback: no terrain-clear orbit around the ship.");
                _sampler = null;
                return false;
            }

            // The first-person viewmodel rides the camera — a drill floating through an exterior orbit
            // shot breaks it. Hidden for the whole sequence, restored per camera mode on End().
            _viewmodel = Camera.GetComponent<Viewmodel>();
            _viewmodel?.SetVisible(false);
            _frame = CinematicFrame.Create("PrologueFrame", 65); // above HUD/VEGA (11), below the veil (75)
            _active = true;
            _ending = false;
            _letter = 0f;
            _flash = 0f;
            _safetyPull = 0f;
            _hasLastPose = false;
            _cameraOverride = true;
            Game.CinematicCameraActive = true;
            return true;
        }

        /// <summary>
        /// Picks the shot the terrain can carry: the classic wide orbit settling to radius 16 (at rising
        /// heights if the slope demands it), restricted to the widest open arc when a full circle is
        /// blocked (the orbit then ping-pongs across the open side); else a high crane shot; else false.
        /// </summary>
        private bool ChooseStaging()
        {
            foreach (float h in new[] { 7f, 12f, 18f })
            {
                // Per angle BOTH rings must clear: the wide entry (radius 24) and the settle orbit
                // (radius 16) — the camera damps from one to the other while sweeping.
                var ring = CinematicStageScan.ScanRing(_sampler, _center.x, _center.y, _center.z,
                    2f, 16f, h, RingSamples);
                var entry = CinematicStageScan.ScanRing(_sampler, _center.x, _center.y, _center.z,
                    2f, 24f, h + 2f, RingSamples);
                for (int i = 0; i < RingSamples; i++)
                {
                    ring[i] &= entry[i];
                }

                if (CinematicStageScan.TryFindWidestClearArc(ring, MinArcSweep, out float arcCenter, out float sweep))
                {
                    ApplyStaging(24f, h + 2f, 16f, h, 8f, Mathf.Max(3.5f, h - 3.5f), arcCenter, sweep);
                    Debug.Log($"[Cinematic] Prologue orbit staged at height {h} (arc {sweep:0}° around {arcCenter:0}°).");
                    return true;
                }
            }

            // Crane fallback: a slow near-top-down sweep for ships parked in craters/canyons.
            var crane = CinematicStageScan.ScanRing(_sampler, _center.x, _center.y, _center.z,
                2f, 6f, 26f, 36);
            if (CinematicStageScan.TryFindWidestClearArc(crane, 60f, out float craneCenter, out float craneSweep))
            {
                ApplyStaging(6f, 26f, 6f, 26f, 4.5f, 18f, craneCenter, craneSweep);
                Debug.Log($"[Cinematic] Prologue crane shot staged (arc {craneSweep:0}° around {craneCenter:0}°).");
                return true;
            }

            return false;
        }

        private void ApplyStaging(float startRadius, float startHeight, float stageRadius, float stageHeight,
            float pushRadius, float pushHeight, float arcCenter, float sweep)
        {
            _stageRadius = stageRadius;
            _stageHeight = stageHeight;
            _pushRadius = pushRadius;
            _pushHeight = pushHeight;
            _radius = startRadius;
            _height = startHeight;
            _targetRadius = stageRadius;
            _targetHeight = stageHeight;

            _arcLimited = sweep < 355f;
            _arcCenter = arcCenter;
            _arcHalf = sweep * 0.5f;
            _orbitDir = 1f;
            // Full circle: start wide behind the current view (the classic shot). Arc-limited: enter
            // near one edge of the open side and sweep across it.
            _angle = _arcLimited
                ? arcCenter - Mathf.Max(0f, _arcHalf - ArcEdgeMargin) * 0.8f
                : Game.PlayerYaw + 160f;
        }

        /// <summary>Advances the stage per prologue page: 0 = exterior orbit, 1 = push-in toward the
        /// cockpit, 2+ = snap back to the seat with a glitch flash as VEGA boots.</summary>
        public void OnPrologueLine(int index)
        {
            if (!_active)
            {
                return;
            }

            if (index <= 0)
            {
                _targetRadius = _stageRadius;
                _targetHeight = _stageHeight;
            }
            else if (index == 1)
            {
                _targetRadius = _pushRadius;
                _targetHeight = _pushHeight;
            }
            else
            {
                ReleaseCamera();
                _flash = 1f;
                ClientAudio.Instance?.Cue("ai_blip"); // the boot crackle, over the glitch flash
            }

            Debug.Log($"[Cinematic] Prologue line {index} (radius {_targetRadius}, override={_cameraOverride}).");
        }

        /// <summary>Ends the staging (prologue finished, skipped, or dismissed for a capture run):
        /// releases the camera instantly, animates the letterbox out, then tears the chrome down.</summary>
        public void End()
        {
            if (!_active)
            {
                return;
            }

            ReleaseCamera();
            RestoreViewmodel();
            _ending = true;
            Debug.Log("[Cinematic] Prologue staging ended.");
        }

        /// <summary>Restores the viewmodel per the player's camera mode (visible in first person only).</summary>
        private void RestoreViewmodel()
        {
            if (_viewmodel == null)
            {
                return;
            }

            var pc = Camera != null ? Camera.GetComponentInParent<PlayerController>() : null;
            _viewmodel.SetVisible(pc == null || !pc.ThirdPerson);
            _viewmodel = null;
        }

        private void ReleaseCamera()
        {
            _cameraOverride = false;
            if (Game != null)
            {
                Game.CinematicCameraActive = false;
            }
        }

        private void Update()
        {
            if (_frame == null)
            {
                return;
            }

            _letter = Mathf.MoveTowards(_letter, _ending ? 0f : 1f, Time.deltaTime / LetterboxSeconds);
            _frame.SetLetterbox(CinematicTimeline.EaseInOut(_letter));

            if (_flash > 0f)
            {
                _flash = Mathf.MoveTowards(_flash, 0f, Time.deltaTime * 2.2f);
                _frame.SetFlash(_flash * 0.7f);
            }

            if (_ending && _letter <= 0f)
            {
                Destroy(_frame.gameObject);
                _frame = null;
                _active = false;
                _ending = false;
            }
        }

        private void LateUpdate()
        {
            if (!_cameraOverride || Camera == null)
            {
                return;
            }

            float step = Time.deltaTime * OrbitDegPerSecond;
            if (_arcLimited)
            {
                // Ping-pong across the open arc instead of orbiting through the blocked side.
                _angle += step * _orbitDir;
                float half = Mathf.Max(ArcEdgeMargin, _arcHalf - ArcEdgeMargin);
                float rel = Mathf.DeltaAngle(_arcCenter, _angle);
                if (rel > half)
                {
                    _orbitDir = -1f;
                }
                else if (rel < -half)
                {
                    _orbitDir = 1f;
                }
            }
            else
            {
                _angle += step;
            }

            _radius = Damp(_radius, _targetRadius);
            _height = Damp(_height, _targetHeight);

            Vector3 look = _center + new Vector3(0f, 2f, 0f);

            // Terrain safety net: the pre-scan staged the shot, but a late-streamed chunk or the page-2
            // push-in can still put the ideal spot inside terrain. Each pull level trades radius for
            // height until the view is clear; blend levels smoothly so the correction reads as a camera
            // move, not a cut.
            int needed = RequiredPull(look);
            if (needed < 0)
            {
                // Nothing clear even fully pulled in — hold the last good pose rather than enter the rock.
                if (_hasLastPose)
                {
                    Camera.transform.SetPositionAndRotation(_lastPos, _lastRot);
                }

                return;
            }

            _safetyPull = Mathf.MoveTowards(_safetyPull, needed, Time.deltaTime * 3f);
            Vector3 pos = OrbitPos(_safetyPull);
            if (!CameraClear(look, pos))
            {
                _safetyPull = needed; // the smooth blend crossed a wall — snap to the verified level
                pos = OrbitPos(needed);
            }

            var rot = Quaternion.LookRotation(look - pos);
            Camera.transform.SetPositionAndRotation(pos, rot);
            _lastPos = pos;
            _lastRot = rot;
            _hasLastPose = true;
        }

        private Vector3 OrbitPos(float pull)
        {
            float rad = _angle * Mathf.Deg2Rad;
            float r = _radius * (1f - PullRadiusStep * pull);
            float h = _height + PullHeightStep * pull;
            return _center + new Vector3(Mathf.Sin(rad) * r, h, Mathf.Cos(rad) * r);
        }

        /// <summary>Smallest pull level whose camera spot is clear with line of sight to the ship, or
        /// -1 when even the fully pulled-in spot is blocked.</summary>
        private int RequiredPull(Vector3 look)
        {
            for (int k = 0; k <= MaxPull; k++)
            {
                if (CameraClear(look, OrbitPos(k)))
                {
                    return k;
                }
            }

            return -1;
        }

        private bool CameraClear(Vector3 look, Vector3 pos)
            => _sampler != null
               && CinematicStageScan.CameraClear(_sampler, look.x, look.y, look.z, pos.x, pos.y, pos.z);

        /// <summary>Framerate-independent exponential approach (≈ settles in ~2 s).</summary>
        private static float Damp(float current, float target)
            => Mathf.Lerp(current, target, 1f - Mathf.Exp(-2f * Time.deltaTime));

        private void OnDestroy()
        {
            ReleaseCamera();
            RestoreViewmodel();
            if (_frame != null)
            {
                Destroy(_frame.gameObject);
            }
        }
    }
}
