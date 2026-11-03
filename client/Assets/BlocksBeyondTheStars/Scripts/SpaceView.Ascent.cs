// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The flight view's half of a departure (#3141/#3142). The lift-off played on the surface and ended in the clouds
    /// (<see cref="TransitionDirector"/>); here the cover opens on the ship breaking out above the cloud layer, nose up and
    /// levelling out, while the sky behind it fades from the planet's colour through deep blue to black and the stars come
    /// out — a camera low behind the hull looking up past it, settling into the usual chase as the pilot gets the
    /// controls. Undocking instead opens the hangar doors on the ship sliding out of that station's hangar mouth, watched
    /// from outside the mouth.
    /// </summary>
    public sealed partial class SpaceView
    {
        private const float AscentSeconds = 3f;
        private const float UndockSeconds = 3.2f;
        private const float UndockWaitMax = 1.5f;   // the station's hull design may still be on its way

        private TransitionEmergence _emergence;
        private bool _undockReady;
        private float _undockWait;
        private Vector3 _undockFrom, _undockTo, _undockOut, _undockCam;
        private string _lastDockedStationId;

        /// <summary>Sets the climb up: the sky it starts in and the cover it opens.</summary>
        private void BeginAscent()
        {
            if (_emergence.FromStation)
            {
                _undockReady = false;
                _undockWait = 0f;
                return; // the doors stay shut until the ship sits in the hangar mouth
            }

            if (!_emergence.Active)
            {
                var env = Game.Environment;
                _emergence.Airless = TransitionDirector.IsAirless(env);
                _emergence.Sky = env != null ? TransitionDirector.Rgb(env.SkyColor) : SpaceBackground;
            }

            if (!_emergence.Airless)
            {
                Camera.backgroundColor = _emergence.Sky;
                Starfield.SkyFade = 0f;
            }

            OpenTransitionCover(0.9f, -1f);
        }

        private void UpdateAscent()
        {
            _seq += Time.deltaTime;
            bool done = _emergence.FromStation ? UpdateUndock() : ClimbOut();
            if (!done)
            {
                return;
            }

            if (Game.SpaceAutomaticTransit)
            {
                // #1614: hold at the top of the climb, hands-off, until the server answers with the next stage — the
                // landing (the descent starts when the flight closes) or the warp into the destination's system.
                HoldForTransit();
                return;
            }

            _phase = Phase.Cruise;
        }

        /// <summary>Out of the clouds: the ship rises from below, nose up and levelling, the sky fading to black.</summary>
        private bool ClimbOut()
        {
            float t = Mathf.Clamp01(_seq / AscentSeconds);
            if (!_emergence.Airless)
            {
                LaunchClouds(t);
                Camera.backgroundColor = Color.Lerp(_emergence.Sky, SpaceBackground, Smooth(0.08f, 0.75f, t));
                Starfield.SkyFade = Smooth(0.35f, 0.95f, t);
            }

            float ease = 1f - (1f - t) * (1f - t) * (1f - t);
            if (_ship != null)
            {
                _ship.transform.localPosition = new Vector3(_launchOffset.x, Mathf.Lerp(-80f, 0f, ease), _launchOffset.z); // #2440: beside the others
                _ship.transform.localRotation = Quaternion.Euler(-28f * (1f - Smooth(0.25f, 0.9f, t)), 0f, 0f);
                ClimbFire(1f - Smooth(0.5f, 1f, t), -_ship.transform.forward + Vector3.down * 0.6f);
            }

            return t >= 1f;
        }

        /// <summary>The engines' glow streaming out of the ship's own nozzles while it climbs or slides out.</summary>
        private void ClimbFire(float power, Vector3 push)
        {
            if (power <= 0.02f || _ship == null)
            {
                return;
            }

            int n = Mathf.RoundToInt(3f * power * FxKit.Density) + 1;
            for (int i = 0; i < n; i++)
            {
                var local = _nozzles.Count > 0 ? _nozzles[Random.Range(0, _nozzles.Count)] : (_exhaust != null ? _exhaust.localPosition : Vector3.zero);
                var hot = Color.Lerp(new Color(0.55f, 0.75f, 1f), new Color(1f, 0.9f, 0.75f), Random.value * 0.5f);
                FxKit.Emit(FxKit.Kind.Glow, _ship.transform.TransformPoint(local), push.normalized * Random.Range(6f, 12f), Random.Range(0.35f, 0.6f) * power, Random.Range(0.12f, 0.22f), hot);
            }
        }

        /// <summary>The launch rise (#2157): cloud wisps rush down past the camera while the ship climbs out of the air.</summary>
        private void LaunchClouds(float progress)
        {
            if (Camera == null || progress > 0.75f || Random.value > 0.6f * FxKit.Density)
            {
                return;
            }

            float air = Game.Environment != null ? Game.Environment.AtmosphereDensity : 0.4f;
            if (air < 0.05f)
            {
                return; // an airless body has no clouds to break through
            }

            var cam = Camera.transform;
            var at = cam.position + cam.forward * Random.Range(6f, 20f) + cam.right * Random.Range(-12f, 12f) + Vector3.up * Random.Range(4f, 10f);
            var tint = Color.Lerp(new Color(0.92f, 0.94f, 0.98f), _emergence.Sky, 0.2f);
            FxKit.Emit(FxKit.Kind.Smoke, at, Vector3.down * Random.Range(22f, 34f), Random.Range(2.5f, 5f), 0.9f, tint);
        }

        // ---- undocking -----------------------------------------------------------------------------------------------

        /// <summary>Out of the hangar: waits (doors shut) until the station's hull and mouth are known, then slides the ship
        /// out along the mouth's axis and hands the pilot the controls facing away from the station.</summary>
        private bool UpdateUndock()
        {
            if (!_undockReady)
            {
                _undockWait += Time.deltaTime;
                if (TryPlaceUndock())
                {
                    _undockReady = true;
                    _seq = 0f;
                    OpenTransitionCover(1.0f, 0f);
                }
                else if (_undockWait > UndockWaitMax)
                {
                    _emergence = default; // no hangar to leave from (an old station without a mouth): the plain climb
                    _seq = 0f;
                    BeginAscent();
                }

                return false;
            }

            float t = Mathf.Clamp01(_seq / UndockSeconds);
            float e = t * t * (3f - 2f * t);
            if (_ship != null)
            {
                _ship.transform.localPosition = Vector3.Lerp(_undockFrom, _undockTo, e);
                _ship.transform.localRotation = Quaternion.LookRotation(_undockOut, Vector3.up);
                ClimbFire(0.4f + 0.6f * t, -_undockOut);
            }

            if (t < 1f)
            {
                return false;
            }

            _yaw = Mathf.Atan2(_undockOut.x, _undockOut.z) * Mathf.Rad2Deg;
            _pitch = 0f;
            return true;
        }

        private bool TryPlaceUndock()
        {
            VoxStruct station = null;
            if (!string.IsNullOrEmpty(_lastDockedStationId) && _structs.TryGetValue(_lastDockedStationId, out var known) && known.HasDock)
            {
                station = known;
            }
            else
            {
                float best = float.MaxValue;
                foreach (var vs in _structs.Values)
                {
                    float sq = vs.HasDock ? vs.Dock.sqrMagnitude : float.MaxValue;
                    if (sq < best)
                    {
                        best = sq;
                        station = vs;
                    }
                }
            }

            if (station == null || _ship == null)
            {
                return false;
            }

            var outward = Vector3.ProjectOnPlane(station.DockOut, Vector3.up);
            _undockOut = outward.sqrMagnitude > 1e-3f ? outward.normalized : Vector3.forward;
            var side = Vector3.Cross(Vector3.up, _undockOut).normalized;
            _undockFrom = station.Dock - _undockOut * 4f;
            _undockTo = station.Dock + _undockOut * 30f;
            _undockCam = station.Dock + _undockOut * 22f + side * 9f + Vector3.up * 3.5f;
            _ship.transform.localPosition = _undockFrom;
            _ship.transform.localRotation = Quaternion.LookRotation(_undockOut, Vector3.up);
            return true;
        }

        // ---- cameras -------------------------------------------------------------------------------------------------

        /// <summary>#3140–#3142: the transition phases fly their own cameras. True when one did.</summary>
        private bool PlaceTransitionCamera()
        {
            switch (_phase)
            {
                case Phase.Landing:
                    PlaceDescentCamera();
                    return true;
                case Phase.Launch:
                    return PlaceAscentCamera();
                case Phase.Boarding:
                    return PlaceDockCamera();
                default:
                    return false;
            }
        }

        /// <summary>Low behind the climbing hull looking up past it, settling into the usual chase pose by the end.</summary>
        private bool PlaceAscentCamera()
        {
            if (_ship == null)
            {
                return false;
            }

            var sp = _ship.transform.localPosition;
            var chase = sp + Quaternion.Euler(0f, _yaw, 0f) * new Vector3(0f, 4.5f, -13f);
            var chaseLook = sp + Vector3.up * 0.5f;
            if (_emergence.FromStation)
            {
                float settle = _undockReady ? Smooth(0.7f, 1f, Mathf.Clamp01(_seq / UndockSeconds)) : 0f;
                var from = _undockReady ? _undockCam : Camera.transform.localPosition;
                chase = sp - _undockOut * 13f + Vector3.up * 4.5f;
                Aim(Vector3.Lerp(from, chase, settle), Vector3.Lerp(sp, chaseLook, settle));
                return true;
            }

            float k = Smooth(0.55f, 1f, Mathf.Clamp01(_seq / AscentSeconds));
            Aim(Vector3.Lerp(sp + new Vector3(2.5f, -4f, -12f), chase, k), Vector3.Lerp(sp + new Vector3(0f, 5f, 4f), chaseLook, k));
            return true;
        }

        private void Aim(Vector3 pos, Vector3 look)
        {
            Camera.transform.localPosition = pos;
            var dir = look - pos;
            if (dir.sqrMagnitude > 1e-4f)
            {
                Camera.transform.localRotation = Quaternion.LookRotation(dir, Vector3.up);
            }
        }

        private void OpenTransitionCover(float seconds, float drift)
        {
            var cover = Transitions != null ? Transitions.Cover : null;
            if (cover != null && cover.Visible)
            {
                cover.Open(seconds, drift);
            }
        }
    }
}
