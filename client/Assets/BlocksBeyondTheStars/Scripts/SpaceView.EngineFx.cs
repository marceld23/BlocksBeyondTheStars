// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Feel;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What the own ship's engines show:
    /// <list type="bullet">
    /// <item>the thrust the helm really applied this frame (#3920) — the flame, the trail, the engine light, the throttle's
    /// FOV punch, the engine sound and the THR readout all follow it, never the raw stick: behind the F1 dialog, a menu,
    /// the pad chooser or a visit on its way W moves nothing, so it shows nothing either, and the autopilot shows the
    /// full thrust it flies with;</item>
    /// <item>the boost's hot look (#3925) — <see cref="BoostLook"/> eases the flame toward the orange of the boost gauge,
    /// with more, bigger and faster flame, an orange and brighter engine light, a denser trail, the space dust's speed
    /// lines, a burst of motes and a glow at the nozzles when it lights, and the gauge pulsing gently while it burns.</item>
    /// </list>
    /// Per graphics tier: High gets all of it; Medium and Low get the extra flame and trail particles and the ignition
    /// burst scaled by their particle density (which "Reduced effects" halves); Potato keeps the colour, the jet speed,
    /// the light, the streaks, the camera and the sound but no extra particles and no ignition burst. The camera parts
    /// go through <see cref="FxCamera"/>, so "Camera motion" off removes them; "Reduce flashes" drops the ignition glow and
    /// the gauge's pulse.
    /// </summary>
    public sealed partial class SpaceView
    {
        private float _helmThrottle;          // #3920: the forward thrust (-1…1) the cruise step applied …
        private int _helmThrottleFrame = -10; // … on this frame; on every other frame the helm applied none
        private BoostLook _boostLook;         // #3925: how hot the boost looks, 0…1

        private float _flameLookShown = -1f;  // the boost look the thrusters' colours were last set for
        private ParticleSystem _flameLookFor; // … and the thruster they were set on (a rebuilt ship gets its colours again)
        private Gradient _flameGradient;
        private readonly GradientColorKey[] _flameColorKeys = new GradientColorKey[3];
        private readonly GradientAlphaKey[] _flameAlphaKeys =
        {
            new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f), // as BuildThruster
        };

        private static readonly int FlameGlowId = Shader.PropertyToID("_Intensity");

        // The cold flame is BuildThruster's blue; the hot one runs from an orange-white core through the boost gauge's
        // orange (BoostCol) to a deep orange tip.
        private static readonly Color FlameStartCold = new Color(0.6f, 0.85f, 1f, 1f);
        private static readonly Color FlameStartHot = new Color(1f, 0.95f, 0.85f, 1f);
        private static readonly Color[] FlameKeysCold = { new Color(0.85f, 0.93f, 1f), new Color(0.35f, 0.6f, 1f), new Color(0.15f, 0.3f, 0.8f) };
        private static readonly Color[] FlameKeysHot = { new Color(1f, 0.93f, 0.75f), new Color(1f, 0.72f, 0.25f), new Color(0.95f, 0.38f, 0.08f) };
        private static readonly float[] FlameKeyTimes = { 0f, 0.55f, 1f };
        private static readonly Color EngineLightCold = new Color(0.55f, 0.8f, 1f);
        private static readonly Color EngineLightHot = new Color(1f, 0.62f, 0.25f);
        private static readonly Color TrailHot = new Color(1f, 0.7f, 0.3f);
        private static readonly Color BoostGaugeHot = new Color(1f, 0.92f, 0.65f, 0.95f);

        /// <summary>#3920: the forward thrust (-1…1) the helm really applied this frame — 0 behind a menu or a dialog, the
        /// pad chooser, a visit on its way, on a spacewalk and in every phase but cruising; the autopilot's full thrust
        /// while it flies.</summary>
        private float HelmThrottle => _helmThrottleFrame == Time.frameCount ? _helmThrottle : 0f;

        /// <summary>#3920: the cruise step flies with <paramref name="fwd"/> this frame.</summary>
        private void ApplyHelmThrottle(float fwd)
        {
            _helmThrottle = Mathf.Clamp(fwd, -1f, 1f);
            _helmThrottleFrame = Time.frameCount;
        }

        /// <summary>The boost is on AND the helm flew with it this frame — behind a menu that holds the world the meter
        /// keeps its state, but nothing burns.</summary>
        private bool BoostBurning => _boost.Active && _maneuverFrame == Time.frameCount;

        /// <summary>The share of the boost's extra particles this graphics tier gets (none on Potato).</summary>
        private static float BoostParticleShare => BoostLook.ParticleShare(FxKit.Preset == QualityPreset.Potato, FxKit.Density);

        /// <summary>The engine light's colour: cold blue, orange while the boost burns.</summary>
        private Color EngineLightColour => Color.Lerp(EngineLightCold, EngineLightHot, _boostLook.Value);

        /// <summary>The factor on the engine light's brightness (1 when cold).</summary>
        private float EngineLightBoost => _boostLook.Scale(BoostLook.LightBrightness);

        /// <summary>The factor on the trail's mote rate (1 when cold; the tier's share of ×2 at full boost).</summary>
        private float TrailBoost => _boostLook.Scale(BoostLook.TrailRate, BoostParticleShare);

        /// <summary>A trail mote's colour: <paramref name="cold"/>, turning orange with the boost.</summary>
        private Color TrailColour(Color cold) => Color.Lerp(cold, Color.Lerp(TrailHot, Color.white, Random.value * 0.3f), _boostLook.Value);

        /// <summary>The boost gauge's colour while the boost burns: its orange, pulsing gently toward orange-white (steady
        /// with reduced effects or reduce flashes).</summary>
        private Color BoostGaugeColour(Color baseColour)
        {
            float pulse = BoostLook.BarPulse(Time.time, UiKit.ReducedMotion || FxKit.ReduceFlashes);
            return Color.Lerp(baseColour, BoostGaugeHot, 0.5f * pulse * _boostLook.Value);
        }

        /// <summary>The frame's boost picture (from <see cref="TickManeuvers"/>): the look eases on the frame clock, so it
        /// also cools behind a pause menu that holds the world; the speed lines follow it, and the view stays wider while
        /// the boost burns.</summary>
        private void TickBoostLook(bool burning)
        {
            _boostLook.Tick(Time.deltaTime, burning);
            if (burning)
            {
                FxCamera.FovPunch(BoostLook.BurnFovDegrees); // held: a punch only ever grows to its value, then eases back
            }

            FxSpaceDust.FlightBoost = _phase == Phase.Cruise && !_eva ? BoostLook.DustShare * _boostLook.Value : 0f;
        }

        /// <summary>The boost lights (from <see cref="TickManeuvers"/>, after the server was told): the view widens with a
        /// small jolt — through <see cref="FxCamera"/>, so "Camera motion" off removes it; the kick rumbles the pad — and
        /// each nozzle throws out a burst of hot motes with a short glow (no glow with "Reduce flashes", no burst on Potato).</summary>
        private void BoostIgnition()
        {
            FxCamera.FovPunch(BoostLook.IgnitionFovDegrees);
            FxCamera.Kick(0.7f);
            FxCamera.AddTrauma(BoostLook.IgnitionTrauma, rumble: false); // the kick already rumbles the pad
            if (_ship == null || !BoostLook.IgnitionBurst(FxKit.Preset == QualityPreset.Potato, FxKit.Density))
            {
                return;
            }

            var back = -_ship.transform.forward;
            int count = Mathf.Min(_nozzles.Count, 4);
            for (int i = 0; i < Mathf.Max(1, count); i++)
            {
                var local = count > 0 ? _nozzles[i] : (_exhaust != null ? _exhaust.localPosition : Vector3.zero);
                var at = _ship.transform.TransformPoint(local);
                // FxKit.Burst scales the count by the tier's particle density itself.
                FxKit.Burst(FxKit.Kind.Motes, at, BoostLook.IgnitionMotes, back, 28f, 4f, 12f, 0.1f, 0.22f, 0.25f, 0.55f,
                    new Color(1f, 0.72f, 0.25f), new Color(1f, 0.95f, 0.8f));
                if (!FxKit.ReduceFlashes)
                {
                    FxKit.Flash(at, new Color(1f, 0.8f, 0.45f), 1.2f, 0.16f); // a short glow card, no light, no screen flash
                }
            }
        }

        /// <summary>The engine flame this frame: the throttle the helm applied sets its rate and jet speed (#3920), the
        /// boost look makes it more, bigger, faster and hot orange (#3925) — the extra particles by the tier's share.</summary>
        private void UpdateEngineFlame(float throttle)
        {
            float share = BoostParticleShare;
            if (_exhaust != null)
            {
                _exhaust.localScale = new Vector3(0.5f, 0.5f, (0.55f + throttle * 0.5f) * _boostLook.Scale(BoostLook.FlameSize)); // steady nozzle glow core
            }

            if (_thruster == null)
            {
                return;
            }

            bool firing = _phase == Phase.Cruise && !_eva;
            float rate = firing ? Mathf.Lerp(20f, 130f, throttle) * _boostLook.Scale(BoostLook.FlameEmission, share) : 0f;
            float speed = (5f + throttle * 9f) * _boostLook.Scale(BoostLook.FlameSpeed);
            float size = _boostLook.Scale(BoostLook.FlameSize, share);
            ApplyFlameLook();
            SetFlame(_thruster, rate, speed, size);
            foreach (var extra in _extraThrusters)
            {
                if (extra != null)
                {
                    SetFlame(extra, rate, speed, size);
                }
            }
        }

        private static void SetFlame(ParticleSystem ps, float rate, float speed, float size)
        {
            var em = ps.emission;
            em.rateOverTime = rate;
            var main = ps.main;
            main.startSpeed = speed;
            var sol = ps.sizeOverLifetime;
            sol.sizeMultiplier = size; // BuildThruster's size curve has a multiplier of 1
        }

        /// <summary>The flame's colours for the boost look, in twentieths — a new gradient only when the look moved a
        /// step (a few times while it eases, never while it holds), and again on a rebuilt ship's new thrusters.</summary>
        private void ApplyFlameLook()
        {
            float q = Mathf.Round(_boostLook.Value * 20f) / 20f;
            if (Mathf.Approximately(q, _flameLookShown) && ReferenceEquals(_flameLookFor, _thruster))
            {
                return;
            }

            _flameLookShown = q;
            _flameLookFor = _thruster;
            _flameGradient ??= new Gradient();
            for (int i = 0; i < _flameColorKeys.Length; i++)
            {
                _flameColorKeys[i] = new GradientColorKey(Color.Lerp(FlameKeysCold[i], FlameKeysHot[i], q), FlameKeyTimes[i]);
            }

            _flameGradient.SetKeys(_flameColorKeys, _flameAlphaKeys);
            var start = Color.Lerp(FlameStartCold, FlameStartHot, q);
            float glow = Mathf.Lerp(1f, BoostLook.FlameGlow, q);
            ColourFlame(_thruster, start, glow);
            foreach (var extra in _extraThrusters)
            {
                if (extra != null)
                {
                    ColourFlame(extra, start, glow);
                }
            }
        }

        private void ColourFlame(ParticleSystem ps, Color start, float glow)
        {
            var main = ps.main;
            main.startColor = start;
            var col = ps.colorOverLifetime;
            col.color = new ParticleSystem.MinMaxGradient(_flameGradient);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            if (r != null && r.sharedMaterial != null)
            {
                r.sharedMaterial.SetFloat(FlameGlowId, glow);
            }
        }
    }
}
