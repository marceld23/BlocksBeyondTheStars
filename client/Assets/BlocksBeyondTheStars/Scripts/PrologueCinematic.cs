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
    /// The camera override runs in LateUpdate at execution order 210, after every gameplay writer, and
    /// only while <see cref="GameBootstrap.CinematicCameraActive"/> also freezes on-foot control — the
    /// moment it is released, <see cref="PlayerController"/> re-asserts the normal eye pose next frame.
    /// </summary>
    [DefaultExecutionOrder(210)]
    public sealed class PrologueCinematic : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private const float HoldTimeout = 12f;   // never gate the story on a stuck reveal
        private const float AboardWait = 6f;     // the Aboard flag rides a later PlayerState packet
        private const float LetterboxSeconds = 0.5f;
        private const float OrbitDegPerSecond = 8f;

        private WorldLoadingOverlay _overlay;
        private CinematicFrame _frame;
        private bool _active;
        private bool _cameraOverride;
        private bool _ending;
        private float _holdStart = -1f;
        private Vector3 _center;
        private float _angle;
        private float _radius, _height, _targetRadius, _targetHeight;
        private float _letter;
        private float _flash;

        private void Start() => _overlay = FindAnyObjectByType<WorldLoadingOverlay>();

        public bool Active => _active;

        /// <summary>True while <see cref="VegaPanel"/> should wait before dequeuing the FIRST prologue
        /// page: the world is still behind the loading veil / not settled, or the authoritative Aboard
        /// flag hasn't arrived yet. Bounded — a stuck reveal never gates the story forever.</summary>
        public bool HoldQueue
        {
            get
            {
                if (Game == null)
                {
                    return false;
                }

                if (_holdStart < 0f)
                {
                    _holdStart = Time.time;
                }

                float waited = Time.time - _holdStart;
                if (waited > HoldTimeout || Game.SpaceViewActive)
                {
                    return false; // give up holding — the panel falls back to the plain dim behaviour
                }

                bool veilUp = _overlay != null && _overlay.VeilActive;
                bool aboardPending = !Game.Aboard && waited < AboardWait;
                return veilUp || !Game.WorldReady || aboardPending;
            }
        }

        /// <summary>Tries to start the staged sequence. False when the scene can't carry it (not aboard,
        /// space view active, camera missing) — the caller then keeps today's dim + panel behaviour.</summary>
        public bool Begin()
        {
            if (_active || Game == null || Camera == null || Game.SpaceViewActive || !Game.Aboard)
            {
                return false;
            }

            _center = Game.ShipPosition ?? Game.PlayerPosition;
            _frame = CinematicFrame.Create("PrologueFrame", 65); // above HUD/VEGA (11), below the veil (75)
            _active = true;
            _ending = false;
            _letter = 0f;
            _flash = 0f;

            // Start wide behind the current view and settle onto the orbit.
            _angle = Game.PlayerYaw + 160f;
            _radius = 24f;
            _height = 9f;
            _targetRadius = 16f;
            _targetHeight = 7f;
            _cameraOverride = true;
            Game.CinematicCameraActive = true;
            return true;
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
                _targetRadius = 16f;
                _targetHeight = 7f;
            }
            else if (index == 1)
            {
                _targetRadius = 8f;
                _targetHeight = 3.5f;
            }
            else
            {
                ReleaseCamera();
                _flash = 1f;
                ClientAudio.Instance?.Cue("ai_blip"); // the boot crackle, over the glitch flash
            }
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
            _ending = true;
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

            _angle += Time.deltaTime * OrbitDegPerSecond;
            _radius = Damp(_radius, _targetRadius);
            _height = Damp(_height, _targetHeight);

            float rad = _angle * Mathf.Deg2Rad;
            Vector3 pos = _center + new Vector3(Mathf.Sin(rad) * _radius, _height, Mathf.Cos(rad) * _radius);
            Vector3 look = _center + new Vector3(0f, 2f, 0f);
            Camera.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
        }

        /// <summary>Framerate-independent exponential approach (≈ settles in ~2 s).</summary>
        private static float Damp(float current, float target)
            => Mathf.Lerp(current, target, 1f - Mathf.Exp(-2f * Time.deltaTime));

        private void OnDestroy()
        {
            ReleaseCamera();
            if (_frame != null)
            {
                Destroy(_frame.gameObject);
            }
        }
    }
}
