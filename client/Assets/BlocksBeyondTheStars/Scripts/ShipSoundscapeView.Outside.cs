// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3247: the own ship after a landing and seen from outside. A landing is the own parked hull showing up on a world
    /// shortly after a flight (not the in-space interior, which is no landing): once the touchdown cinematic has handed the
    /// camera back, the hull ticks as it cools — sparse metal pings around the engines that slow down over half a minute
    /// (<see cref="ShipSoundscape.CoolingSchedule"/>), heard aboard and outside. Outside, within 15 m of the own hull, its
    /// quiet idle hum plays from the hull's nearest point — so a child finds the ship by ear at night.
    /// </summary>
    public sealed partial class ShipSoundscapeView
    {
        /// <summary>The own hull showing up this soon after the last flight frame is a landing (a world load fits in).</summary>
        private const float LandingWindowSeconds = 45f;

        /// <summary>The ticks wait for the touchdown cinematic to end — but never longer than this.</summary>
        private const float TouchdownWaitSeconds = 15f;

        /// <summary>A cooling tick farther away than this is not played at all (the pooled one-shots fade out by 20 m).</summary>
        private const float CoolingEarshot = 22f;

        private const float ExteriorFadePerSecond = 0.5f;

        private readonly List<Vector3> _coolingEngines = new List<Vector3>(); // the own hull's engine cells (world)
        private AudioSource _exterior;
        private float _flightSeen = -999f;
        private LandedShipModel _ownSeen;
        private LandedShipModel _coolingShip;
        private bool _coolingArmed;
        private float _armedAt;
        private IReadOnlyList<ShipSoundscape.CoolingTick> _ticks;
        private float _coolingStart;
        private int _tickNext;

        /// <summary>Spots a landing (the own hull arriving soon after a flight) and starts its cooling ticks once the touchdown
        /// cinematic is over.</summary>
        private void TickLanding(LandedShipModel own)
        {
            if (Game.InSpace || Game.SpaceViewActive)
            {
                _flightSeen = Time.time;
            }

            if (own != null && !ReferenceEquals(own, _ownSeen) && Time.time - _flightSeen < LandingWindowSeconds
                && _worldType != ShipInteriorWorld)
            {
                _coolingArmed = true;
                _armedAt = Time.time;
                _ticks = null;
            }

            _ownSeen = own;
            if (!_coolingArmed)
            {
                return;
            }

            if (own == null)
            {
                _coolingArmed = false; // off again before it ever cooled
                return;
            }

            var transitions = Game.Views.Transitions;
            if (transitions != null && transitions.Busy && Time.time - _armedAt < TouchdownWaitSeconds)
            {
                return; // the stand-in is still coming down, or the camera still gliding home
            }

            _coolingArmed = false;
            _ticks = ShipSoundscape.CoolingSchedule(_rng.Next());
            _coolingStart = Time.time;
            _tickNext = 0;
        }

        /// <summary>Plays the cooling ticks that are due, each at a random engine cell of the own hull (its middle without
        /// engines), within earshot.</summary>
        private void TickCooling(LandedShipModel own, Vector3 listener, ClientAudio audio)
        {
            if (_ticks == null)
            {
                return;
            }

            if (own == null)
            {
                _ticks = null; // the hull left (a launch): nothing left to cool
                return;
            }

            float t = Time.time - _coolingStart;
            while (_tickNext < _ticks.Count && _ticks[_tickNext].At <= t)
            {
                var tick = _ticks[_tickNext++];
                var at = CoolingSpot(own);
                if ((at - listener).sqrMagnitude > CoolingEarshot * CoolingEarshot)
                {
                    continue;
                }

                string id = ShipSoundscape.CoolingTickClips[tick.Variant];
                if (!audio.Has(id))
                {
                    id = PickClip(ShipSoundscape.CoolingTickClips);
                }

                if (id != null)
                {
                    audio.At(id, at, tick.Pitch, ShipSoundscape.CoolingTickLevel * tick.Volume);
                }
            }

            if (_tickNext >= _ticks.Count)
            {
                _ticks = null;
            }
        }

        /// <summary>Where a cooling tick pings: a random engine cell of the own hull (or its middle), a little jittered, in
        /// the wrapped scene.</summary>
        private Vector3 CoolingSpot(LandedShipModel own)
        {
            if (!ReferenceEquals(own, _coolingShip))
            {
                _coolingShip = own;
                CollectEngineCells(own, _coolingEngines);
            }

            var w = _coolingEngines.Count > 0
                ? _coolingEngines[_rng.Next(_coolingEngines.Count)]
                : new Vector3(own.Origin.X + own.Width * 0.5f, own.Origin.Y + own.Height * 0.5f, own.Origin.Z + own.Length * 0.5f);
            return Game.ScenePos(w.x, w.y, w.z)
                + new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(-0.3f, 0.6f), Random.Range(-0.6f, 0.6f));
        }

        /// <summary>The own parked hull's idle hum outside (<paramref name="own"/> is null aboard and in the flight view): from
        /// the hull's nearest point, faded out by 15 m; silent while a cinematic flies the ship and for a downed wreck.</summary>
        private void TickExterior(LandedShipModel own, Vector3 listener, float sfx, bool dead, ClientAudio audio)
        {
            if (_exterior.clip == null)
            {
                _exterior.clip = audio.Clip(ShipSoundscape.ExteriorIdleLoop);
            }

            float target = 0f;
            var transitions = Game.Views.Transitions;
            if (own != null && !dead && _exterior.clip != null && (transitions == null || !transitions.Busy))
            {
                var min = Game.ScenePos(own.Origin.X, own.Origin.Y, own.Origin.Z);
                var box = new Bounds();
                box.SetMinMax(min, min + new Vector3(own.Width, own.Height, own.Length));
                var near = box.ClosestPoint(listener);
                target = ShipSoundscape.ExteriorIdleVolume(Vector3.Distance(near, listener)) * sfx;
                _exterior.transform.position = near;
            }

            _exterior.volume = Mathf.MoveTowards(_exterior.volume, target, Time.deltaTime * ExteriorFadePerSecond);
            if (_exterior.volume > 0f && !_exterior.isPlaying)
            {
                _exterior.Play();
            }
            else if (_exterior.volume <= 0f && _exterior.isPlaying)
            {
                _exterior.Stop();
            }
        }
    }
}
