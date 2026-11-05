// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3247: the ship as a place, by ear. Pure presentation over what the client already knows — the parked hull (its box
    /// and engine cells), the station list, the fitted modules, the hull fraction; the rules live in
    /// <see cref="ShipSoundscape"/> (Client.Core, unit-tested).
    /// <list type="bullet">
    /// <item>Aboard: the hum synthesized from the hull's size and modules replaces the router's one-size hum (it sets
    /// <see cref="ClientAudio.ShipHumOverride"/> while it plays); the nearest module emitters — reactor, life support,
    /// medbay tank, cargo hold — play quietly at their spots (this file's Modules partial); below 30 % hull the structure
    /// creaks now and then; a downed wreck has no power and so no hum.</item>
    /// <item>After a landing: metal cooling ticks around the engines for half a minute, aboard and outside; outside, within
    /// 15 m of the own parked hull, its quiet idle hum from the nearest point of the hull (the Outside partial).</item>
    /// </list>
    /// Volume, pitch and position only — Unity Web ignores filter components and has no Doppler. A recording that is not
    /// bundled simply stays silent.
    /// </summary>
    public sealed partial class ShipSoundscapeView : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        /// <summary>The hum swells and fades at the old router hum's pace.</summary>
        private const float HumFadePerSecond = 0.6f;

        /// <summary>The void world type that holds the own ship's interior while it floats in space.</summary>
        private const string ShipInteriorWorld = "ship_interior";

        private readonly System.Random _rng = new System.Random();
        private AudioSource _hum;
        private AudioClip _humClip;
        private ShipSoundscape.HumParams _humParams;
        private LandedShipModel _humShip;
        private SpaceShipDesign _humDesign;
        private string[] _humModules;
        private bool _subscribed;
        private string _worldType = string.Empty;

        private void Awake()
        {
            _hum = MakeSource("ShipHum", 0f, 1f);
            _exterior = MakeSource("ShipExteriorIdle", 1f, ShipSoundscape.ExteriorReach);
            _voiceSources = new AudioSource[_voices.Count];
            _nextCargoCreak = new float[_voices.Count];
            for (int v = 0; v < _voiceSources.Length; v++)
            {
                // Mostly positional, with a little 2D bleed so a tank right beside the ear is not panned hard to one side.
                _voiceSources[v] = MakeSource("ShipModule" + v, 0.85f, ShipSoundscape.EmitterReach);
            }
        }

        private void Update()
        {
            var audio = ClientAudio.Instance;
            if (Game == null || audio == null)
            {
                return;
            }

            Subscribe();
            float sfx = audio.BedLevel; // the ship's hum and rooms are ambience beds: they duck under a cutscene (#2495)
            var listener = Camera != null ? Camera.transform.position : Game.PlayerPosition;
            var own = ShipOf(Game.LocalPlayerId);
            var aboardShip = Game.Aboard ? ShipAboard(own) : null;
            float ownHull = HullFraction();
            float hull = aboardShip == null || ReferenceEquals(aboardShip, own) ? ownHull : 1f; // a visited hull: no readout
            bool dead = ShipSoundscape.IsDead(hull);
            bool walking = Game.Aboard && !Game.SpaceViewActive && aboardShip != null;

            TickHum(audio, aboardShip, sfx, dead);
            TickModules(walking && !dead ? aboardShip : null, listener, sfx);
            TickHullCreaks(walking, hull, listener, audio);
            TickLanding(own);
            TickCooling(own, listener, audio);
            TickExterior(Game.Aboard || Game.SpaceViewActive ? null : own, listener, sfx, ShipSoundscape.IsDead(ownHull), audio);
        }

        /// <summary>The design hum while aboard. It takes over from the router's hum as soon as a design is known (the parked
        /// hull, or in the flight view the own ship's voxel design); until then the old hum keeps playing.</summary>
        private void TickHum(ClientAudio audio, LandedShipModel ship, float sfx, bool dead)
        {
            bool aboard = Game.Aboard;
            if (aboard)
            {
                RefreshHum(ship);
            }

            bool mine = aboard && _humClip != null;
            audio.ShipHumOverride = mine;
            float target = mine && !dead ? sfx * ShipSoundscape.HumBedShare * _humParams.Level : 0f;
            _hum.volume = Mathf.MoveTowards(_hum.volume, target, Time.deltaTime * HumFadePerSecond);
            if (mine && !_hum.isPlaying)
            {
                _hum.Play();
            }
            else if (!mine && _hum.isPlaying && _hum.volume <= 0f)
            {
                _hum.Stop();
            }
        }

        /// <summary>Re-synthesizes the hum loop when the hull, its design or its modules changed into a different sound.</summary>
        private void RefreshHum(LandedShipModel ship)
        {
            var design = Game.ShipDesign;
            string[] modules = ship == null || ship.OwnerId == Game.LocalPlayerId ? Game.ShipCombat?.Modules : null;
            if (_humClip != null && ReferenceEquals(ship, _humShip) && ReferenceEquals(design, _humDesign)
                && ReferenceEquals(modules, _humModules))
            {
                return;
            }

            int w, h, l;
            if (ship != null)
            {
                (w, h, l) = (ship.Width, ship.Height, ship.Length);
            }
            else if (design != null && design.Width > 0)
            {
                (w, h, l) = (design.Width, design.Height, design.Length);
            }
            else
            {
                return; // no design known yet: the router's own hum keeps playing
            }

            _humShip = ship;
            _humDesign = design;
            _humModules = modules;
            var p = ShipSoundscape.HumFor(w, h, l, modules);
            if (_humClip != null && p.SameAs(_humParams))
            {
                return;
            }

            var data = ShipSoundscape.SynthesizeHum(p);
            var clip = AudioClip.Create("ship_hum", data.Length, 1, ShipSoundscape.HumSampleRate, false);
            clip.SetData(data, 0);
            var old = _humClip;
            _humParams = p;
            _humClip = clip;
            _hum.clip = clip;
            _hum.Play();
            if (old != null)
            {
                Destroy(old); // a code-created clip is never collected
            }
        }

        /// <summary>The hull the player stands in: the parked ship whose box holds them, else the visited host's or the own.</summary>
        private LandedShipModel ShipAboard(LandedShipModel own)
        {
            var p = Game.PlayerPosition;
            var inside = Game.LandedShipBoundsAt(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z), out _);
            return inside ?? (Game.Visiting ? ShipOf(Game.VisitingHost) : own);
        }

        /// <summary>The ship this player has parked on the current world, or null.</summary>
        private LandedShipModel ShipOf(string ownerId)
        {
            if (string.IsNullOrEmpty(ownerId))
            {
                return null;
            }

            foreach (var m in Game.LandedShips.Values)
            {
                if (m.OwnerId == ownerId)
                {
                    return m;
                }
            }

            return null;
        }

        /// <summary>The own ship's hull fraction (1 while no readout came in) — the same reading as the damage dressing.</summary>
        private float HullFraction()
        {
            var combat = Game.ShipCombat;
            return combat != null && combat.HullMax > 0f ? Mathf.Clamp01(combat.Hull / combat.HullMax) : 1f;
        }

        /// <summary>One of the clip ids that is bundled (or synthesized), picked at random; null when none is.</summary>
        private string PickClip(string[] ids)
        {
            var audio = ClientAudio.Instance;
            if (audio == null)
            {
                return null;
            }

            int count = 0;
            foreach (var id in ids)
            {
                if (audio.Has(id))
                {
                    count++;
                }
            }

            int pick = count > 0 ? _rng.Next(count) : -1;
            foreach (var id in ids)
            {
                if (audio.Has(id) && pick-- == 0)
                {
                    return id;
                }
            }

            return null;
        }

        /// <summary>A looping source on its own child object. Its distance range is wide on purpose: the rules fade the
        /// volume by distance themselves, Unity only pans.</summary>
        private AudioSource MakeSource(string name, float spatialBlend, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.volume = 0f;
            src.spatialBlend = spatialBlend;
            src.dopplerLevel = 0f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = range;
            src.maxDistance = range + 1f;
            return src;
        }

        private void Subscribe()
        {
            if (_subscribed || Game.Network == null)
            {
                return;
            }

            Game.Network.WorldResetReceived += OnWorldReset;
            _subscribed = true;
        }

        /// <summary>Another world: its own ticks (if this was a landing, the hull arriving next arms them again).</summary>
        private void OnWorldReset(WorldReset m)
        {
            _worldType = m.PlanetType ?? string.Empty;
            _ticks = null;
            _coolingArmed = false;
        }

        private void OnDisable()
        {
            if (ClientAudio.Instance != null)
            {
                ClientAudio.Instance.ShipHumOverride = false; // the router's hum takes over again
            }

            if (_hum != null)
            {
                _hum.volume = 0f;
                _hum.Stop();
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.WorldResetReceived -= OnWorldReset;
            }

            if (_humClip != null)
            {
                Destroy(_humClip);
                _humClip = null;
            }
        }
    }
}
