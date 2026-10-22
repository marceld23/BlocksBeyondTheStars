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
    /// The automation and capture hooks that <see cref="ScreenshotDirector"/> and <see cref="ClipDirector"/> drive:
    /// a forced heading, throttle and pitch, aiming and steering at the nearest body, and the hands-off landing on a pad
    /// with a drawn cursor. Regular play never calls them.
    /// </summary>
    public sealed partial class SpaceView
    {
        /// <summary>Automation/capture hook (<see cref="ScreenshotDirector"/>): force the flight heading so the
        /// marketing vantage shots differ. Safe because in manual flight <c>_yaw</c> only changes from mouse
        /// input, which is absent during an unattended capture run.</summary>
        public void SetFlightYaw(float yawDeg) => _yaw = yawDeg;

        /// <summary>Cinematic-capture hooks (<see cref="ClipDirector"/>): force forward thrust + pitch without
        /// keyboard input so a recorded clip shows the ship actually cruising. <see cref="_captureThrottle"/> is
        /// &lt; 0 when disabled, so the normal input-driven flight path is untouched in regular play.</summary>
        private float _captureThrottle = -1f;
        public void SetFlightThrottle(float throttle) => _captureThrottle = throttle < 0f ? -1f : Mathf.Clamp01(throttle);
        public void SetFlightPitch(float pitchDeg) => _pitch = Mathf.Clamp(pitchDeg, -80f, 80f);
        private float CaptureForward() => _captureThrottle >= 0f ? _captureThrottle : InputMap.MoveY();

        // --- Cinematic landing hooks (ClipDirector): a hands-off version of the player's fly-in → pick-pad → land. ---
        public bool CaptureLandMapShowing => _landMapGo != null;
        public void CaptureLandOnPad(int padIndex) => LandOnPad(padIndex);

        /// <summary>Capture hook (<see cref="ScreenshotDirector"/>): snap the flight heading so the nearest
        /// landable body (usually the home planet the ship just rose off) sits in frame — the marketing space
        /// shot should show a planet, not empty starfield. Unlike <see cref="CaptureSteerToNearestBody"/> this
        /// snaps yaw/pitch instantly and adds no thrust. Returns false when no body exists to aim at.</summary>
        public bool CaptureAimAtNearestBody()
        {
            if (_ship == null || _landables == null)
            {
                return false;
            }

            Vector3 pos = _ship.transform.localPosition;
            Vector3 target = Vector3.zero;
            float bestSq = float.MaxValue;
            foreach (var b in _landables)
            {
                float sq = (b.Pos - pos).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    target = b.Pos;
                }
            }

            if (bestSq == float.MaxValue)
            {
                return false;
            }

            Vector3 to = (target - pos).normalized;
            _yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            _pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(to.y, -1f, 1f)) * Mathf.Rad2Deg, -80f, 80f);
            return true;
        }

        /// <summary>Steer + cruise toward the nearest landable body (planets/moons; stations skipped). Returns the
        /// distance to it as a MULTIPLE of its landing range (1 = at landing range, larger = farther), or -1 if
        /// there's none. The caller throttles down before it reaches ~1 so the ship doesn't dive into the surface
        /// (a full-speed dive auto-leaves space, which is why the descent never played before).</summary>
        public float CaptureSteerToNearestBody(float throttle)
        {
            if (_ship == null || _landables == null)
            {
                return -1f;
            }

            Vector3 pos = _ship.transform.localPosition;
            Vector3 target = Vector3.zero;
            float bestSq = float.MaxValue;
            float landRange = 1f;
            foreach (var b in _landables)
            {
                float sq = (b.Pos - pos).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    target = b.Pos;
                    landRange = b.Radius + KeepOutMargin + LandBand;
                }
            }

            if (bestSq == float.MaxValue)
            {
                return -1f;
            }

            Vector3 to = (target - pos).normalized;
            float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float wantPitch = -Mathf.Asin(Mathf.Clamp(to.y, -1f, 1f)) * Mathf.Rad2Deg;
            _yaw = Mathf.MoveTowardsAngle(_yaw, wantYaw, 60f * Time.deltaTime);
            _pitch = Mathf.MoveTowards(_pitch, Mathf.Clamp(wantPitch, -80f, 80f), 45f * Time.deltaTime);
            _captureThrottle = Mathf.Clamp01(throttle);
            return Mathf.Sqrt(bestSq) / Mathf.Max(1f, landRange);
        }

        /// <summary>Open the planet/pad map for the body the server already has pads for (it auto-sends them for
        /// the current body). Returns false if no pads are known yet. ShowLandMap renders next frame because the
        /// chooser's body id now matches the server's.</summary>
        public bool CaptureOpenLandMap()
        {
            if (Game == null || Game.LandingPads == null || Game.LandingPads.Length == 0)
            {
                return false;
            }

            OpenPadChooser(Game.LandingPadsBody ?? string.Empty);
            return true;
        }

        // Synthetic pad-click cursor — the OS cursor isn't in a ScreenCapture, so we draw + animate our own onto
        // the pad map and "press" a free pad. Populated by ShowLandMap (free-pad centres in the panel's space).
        private readonly System.Collections.Generic.List<(Vector2 center, int index)> _captureFreePads
            = new System.Collections.Generic.List<(Vector2, int)>();
        private RectTransform _captureCursor;
        private Vector2 _cursorStart, _cursorTarget;
        private int _capturePadIndex = -1;

        /// <summary>Spawn the cursor and aim it at the first free pad. Returns false if the map/free pad isn't ready.</summary>
        public bool CaptureBeginPadClick()
        {
            if (_landMapGo == null || _captureFreePads.Count == 0)
            {
                return false;
            }

            var first = _captureFreePads[0];
            _capturePadIndex = first.index;
            _cursorTarget = first.center;
            _cursorStart = new Vector2(first.center.x, 60f); // glide down from the top of the panel

            var go = new GameObject("CaptureCursor", typeof(RectTransform));
            go.transform.SetParent(_landMapGo.transform, false);
            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(1f, 1f, 1f, 0.95f);
            img.raycastTarget = false;
            _captureCursor = go.GetComponent<RectTransform>();
            CaptureCursorProgress(0f);
            return true;
        }

        /// <summary>Move the cursor from its start toward the pad (t 0..1), shrinking a touch as it "presses".</summary>
        public void CaptureCursorProgress(float t)
        {
            if (_captureCursor == null)
            {
                return;
            }

            Vector2 p = Vector2.Lerp(_cursorStart, _cursorTarget, Mathf.Clamp01(t));
            float s = Mathf.Lerp(26f, 18f, Mathf.Clamp01(t));
            UiKit.Place(_captureCursor.gameObject, p.x - s * 0.5f, p.y - s * 0.5f, s, s);
        }

        /// <summary>Commit the click: land on the chosen pad (HideLandMap tears down the cursor with the panel).</summary>
        public void CaptureCommitPadClick()
        {
            if (_capturePadIndex >= 0)
            {
                LandOnPad(_capturePadIndex);
            }

            _capturePadIndex = -1;
            _captureCursor = null;
        }
    }
}
