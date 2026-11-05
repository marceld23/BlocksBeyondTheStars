// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The flight's sound (#3248): the own ship's layered engine (an idle bed, a thrust layer that swells with the throttle,
    /// an afterburner surge on a quick push), the cockpit electronics and a darker engine in the first-person view, a
    /// manoeuvring-thruster puff when a strafe or a turn starts, the nearest other ships' engines in 3D with a hand-made
    /// Doppler, the shots of other pilots and of enemies, and the shield recharge. The rules live in the Unity-free
    /// <see cref="FlightEngineMix"/> (and its cue helpers); every level here is scaled by the player's effects volume.
    /// Only volume and pitch are used — Unity Web ignores audio filters and has no Doppler of its own.
    /// </summary>
    public sealed partial class SpaceView
    {
        private const int RemoteEngineVoices = 3;       // the nearest other ships heard at once
        private const float RemoteEngineRange = 90f;    // flight units: farther engines are silent
        private const float HostileShotGap = 0.22f;     // the shortest gap between two enemy-bolt sounds
        private const float TeleportSpeed = 300f;       // a jump faster than this (a cut, a resume) is not motion

        private AudioSource _engine;       // the idle bed (engine_idle)
        private AudioSource _engineThrust; // swells with the throttle (engine_thrust)
        private AudioSource _engineBoost;  // the afterburner surge (engine_boost)
        private AudioSource _cockpitBed;   // the cockpit electronics, first person only (cockpit_ambience)
        private float _enginePitch = 1f;
        private float _audioSpeed;         // the own ship's normalised speed, eased
        private Vector3 _audioLastShipPos;
        private bool _audioHasShipPos;
        private float _rcsLastYaw, _rcsLastPitch;
        private bool _rcsPrimed;
        private float _hostileShotAt = -10f;
        private readonly RcsPuffTrigger _rcs = new RcsPuffTrigger();
        private readonly FlightBoostSurge _boostSurge = new FlightBoostSurge();
        private readonly ShieldRechargeCue _shieldRecharge = new ShieldRechargeCue();

        /// <summary>One 3D engine loop following another ship (pilot, NPC trader or a hostile).</summary>
        private sealed class EngineVoice
        {
            public AudioSource Src;
            public string Id;          // the ship it follows; null = free
            public Transform Target;
            public float Size = 1f;
            public Vector3 LastPos;
            public bool HasLast;
            public Vector3 Vel;
            public float Doppler = 1f;
            public float Fade;
            public bool Wanted;        // among the nearest ships this frame (else it fades out)
        }

        private readonly EngineVoice[] _remoteVoices = new EngineVoice[RemoteEngineVoices];
        private readonly string[] _nearIds = new string[RemoteEngineVoices];
        private readonly Transform[] _nearTargets = new Transform[RemoteEngineVoices];
        private readonly float[] _nearSizes = new float[RemoteEngineVoices]; // < 0 = a pilot's hull, measured on demand
        private readonly float[] _nearDistSq = new float[RemoteEngineVoices];
        private readonly bool[] _nearMatched = new bool[RemoteEngineVoices];
        private int _nearCount;
        private readonly Dictionary<string, (GameObject Ship, float Size)> _remoteEngineSize = new Dictionary<string, (GameObject, float)>();
        private Vector3 _listenerLastPos;
        private Vector3 _listenerVel;
        private bool _listenerHasLast;

        /// <summary>Starts the own ship's engine layers (silent until the first update eases them in) — on entering the
        /// flight view. A layer whose clip is missing simply stays out.</summary>
        private void StartFlightAudio()
        {
            StopFlightAudio();
            var audio = ClientAudio.Instance;
            _engine = AddFlightLoop(FlightClip(audio, "engine_idle") ?? ProceduralAudio.Generate("engine_idle"));
            _engineThrust = AddFlightLoop(FlightClip(audio, "engine_thrust"));
            _engineBoost = AddFlightLoop(FlightClip(audio, "engine_boost"));
            _cockpitBed = AddFlightLoop(FlightClip(audio, "cockpit_ambience"));
            _enginePitch = 1f;
            _audioSpeed = 0f;
            _audioHasShipPos = false;
            _rcsPrimed = false;
            _listenerHasLast = false;
            _listenerVel = Vector3.zero;
            _rcs.Reset();
            _boostSurge.Reset();
            _shieldRecharge.Reset(Game.ShipCombat != null ? Game.ShipCombat.Shield : -1f);
        }

        /// <summary>Tears every flight sound down — on leaving the flight view.</summary>
        private void StopFlightAudio()
        {
            DestroyFlightLoop(ref _engine);
            DestroyFlightLoop(ref _engineThrust);
            DestroyFlightLoop(ref _engineBoost);
            DestroyFlightLoop(ref _cockpitBed);
            for (int i = 0; i < _remoteVoices.Length; i++)
            {
                if (_remoteVoices[i] != null && _remoteVoices[i].Src != null)
                {
                    Destroy(_remoteVoices[i].Src.gameObject);
                }

                _remoteVoices[i] = null;
            }

            _nearCount = 0;
            _remoteEngineSize.Clear();
        }

        private static AudioClip FlightClip(ClientAudio audio, string id)
        {
            var clip = audio != null ? audio.Clip(id) : null;
            return clip != null ? clip : Resources.Load<AudioClip>("audio/" + id);
        }

        private AudioSource AddFlightLoop(AudioClip clip)
        {
            if (clip == null)
            {
                return null;
            }

            var src = gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.dopplerLevel = 0f;
            src.volume = 0f;
            src.Play();
            return src;
        }

        private void DestroyFlightLoop(ref AudioSource src)
        {
            if (src != null)
            {
                Destroy(src);
            }

            src = null;
        }

        /// <summary>The per-frame flight mix (after the camera is placed): the own engine layers eased toward the
        /// <see cref="FlightEngineMix"/> targets, the RCS puffs and the other ships' engines.</summary>
        private void UpdateFlightAudio()
        {
            float dt = Time.deltaTime;
            float sfx = ClientAudio.EffectsVolume;
            bool cruise = _phase == Phase.Cruise && !_eva && !_shipDestroyed;
            float throttle = cruise ? Mathf.Clamp01(Mathf.Abs(CaptureForward())) : 0f;
            float strafe = MeasureShipMotion(dt, cruise);
            bool boost = _boostSurge.Step(dt, throttle);
            var phase = _shipDestroyed ? FlightEnginePhase.Off
                : _eva ? FlightEnginePhase.Eva
                : _phase == Phase.Cruise ? FlightEnginePhase.Cruise
                : FlightEnginePhase.Sequence;
            bool firstPerson = _viewMode == 1;
            var mix = FlightEngineMix.Target(phase, throttle, _audioSpeed, boost, firstPerson);
            _enginePitch = FlightEngineMix.Ease(_enginePitch, mix.Pitch, dt, FlightEngineMix.PitchSeconds, FlightEngineMix.PitchSeconds);
            EaseFlightLoop(_engine, mix.Idle * sfx, _enginePitch, dt, FlightEngineMix.IdleSeconds, FlightEngineMix.IdleSeconds);
            EaseFlightLoop(_engineThrust, mix.Thrust * sfx, _enginePitch, dt, FlightEngineMix.ThrustRiseSeconds, FlightEngineMix.ThrustFallSeconds);
            EaseFlightLoop(_engineBoost, mix.Boost * sfx, _enginePitch, dt, FlightEngineMix.BoostRiseSeconds, FlightEngineMix.BoostFallSeconds);
            EaseFlightLoop(_cockpitBed, mix.Cockpit * sfx, 1f, dt, FlightEngineMix.CockpitSeconds, FlightEngineMix.CockpitSeconds);
            UpdateRcsPuffs(cruise, strafe, dt, firstPerson);
            UpdateRemoteEngines(dt, sfx, muffled: firstPerson || _eva); // heard from the cockpit or through the suit
        }

        private static void EaseFlightLoop(AudioSource src, float target, float pitch, float dt, float rise, float fall)
        {
            if (src == null)
            {
                return;
            }

            src.volume = Mathf.Clamp01(FlightEngineMix.Ease(src.volume, target, dt, rise, fall));
            src.pitch = pitch;
        }

        /// <summary>Measures the own ship's motion this frame: eases <see cref="_audioSpeed"/> (0..1 of the ship's cruise
        /// speed) and returns the sideways part of it (−1..1) — a strafe, as the RCS puffs hear it. A scripted flight or a
        /// teleport is no motion.</summary>
        private float MeasureShipMotion(float dt, bool cruise)
        {
            if (_ship == null || dt <= 0f)
            {
                return 0f;
            }

            Vector3 pos = _ship.transform.localPosition;
            Vector3 vel = _audioHasShipPos ? (pos - _audioLastShipPos) / dt : Vector3.zero;
            _audioLastShipPos = pos;
            _audioHasShipPos = true;
            float full = MoveSpeed * Mathf.Max(0.1f, _shipSpeedMul);
            if (!cruise || vel.sqrMagnitude > 9f * full * full)
            {
                vel = Vector3.zero;
            }

            _audioSpeed = FlightEngineMix.Ease(_audioSpeed, Mathf.Clamp01(vel.magnitude / full), dt, 0.15f, 0.3f);
            return Mathf.Clamp(Vector3.Dot(vel, _ship.transform.localRotation * Vector3.right) / full, -1f, 1f);
        }

        /// <summary>A short thruster hiss when a strafe, a yaw or a pitch starts or flips (<see cref="RcsPuffTrigger"/>) —
        /// read from what the ship actually does, so a held stick, the pad chooser or the chart never puff.</summary>
        private void UpdateRcsPuffs(bool cruise, float strafe, float dt, bool firstPerson)
        {
            if (!cruise || !_rcsPrimed)
            {
                _rcsLastYaw = _yaw;
                _rcsLastPitch = _pitch;
                _rcsPrimed = cruise;
                if (!cruise)
                {
                    _rcs.Reset();
                }

                return;
            }

            float yaw = FlightEngineMix.TurnAxis(Mathf.DeltaAngle(_rcsLastYaw, _yaw), dt);
            float pitch = FlightEngineMix.TurnAxis(_pitch - _rcsLastPitch, dt);
            _rcsLastYaw = _yaw;
            _rcsLastPitch = _pitch;
            var audio = ClientAudio.Instance;
            if (!_rcs.Step(Time.time, strafe, yaw, pitch, out int take) || audio == null)
            {
                return;
            }

            string id = take == 1 && audio.Has("rcs_puff_2") ? "rcs_puff_2" : "rcs_puff";
            audio.Cue(id, FlightEngineMix.RcsVolume(firstPerson));
        }

        /// <summary>Our own shot (#3238): the cannons fire the recorded ship-weapon shot, the twin laser its zap, a shot at a
        /// rock the mining zap — chosen from the module's fx style (<see cref="FlightWeaponCue"/>).</summary>
        private static void PlayOwnShotSound(string style, bool mining)
        {
            var audio = ClientAudio.Instance;
            if (audio == null)
            {
                return;
            }

            string id = FlightWeaponCue.For(style, mining);
            if (!audio.Has(id))
            {
                id = mining ? FlightWeaponCue.Mine : FlightWeaponCue.Laser;
            }

            audio.Cue(id, id == FlightWeaponCue.Cannon ? 0.85f : 1f);
        }

        /// <summary>Another pilot's shot (#3248), heard where their ship is.</summary>
        private static void PlayRemoteShotSound(string style, Vector3 at)
        {
            var audio = ClientAudio.Instance;
            if (audio == null)
            {
                return;
            }

            string id = FlightWeaponCue.For(style, mining: false);
            audio.At(audio.Has(id) ? id : FlightWeaponCue.Laser, at, FlightWeaponCue.Pitch(style), 0.7f);
        }

        /// <summary>An enemy's bolt (#3248), heard at the enemy — lower than our laser, and rate-limited so a pack of
        /// attackers is a few zaps, not a machine gun.</summary>
        private void PlayHostileShotSound(Vector3 at)
        {
            if (Time.time - _hostileShotAt < HostileShotGap)
            {
                return;
            }

            _hostileShotAt = Time.time;
            ClientAudio.Instance?.At(FlightWeaponCue.Laser, at, Random.Range(0.78f, 0.86f), 0.55f);
        }

        /// <summary>The shield starts to recharge after a drop (#3248): a rising hum, once (<see cref="ShieldRechargeCue"/>).</summary>
        private void PlayShieldRechargeCue(BlocksBeyondTheStars.Networking.Messages.ShipCombatStatus s)
        {
            if (_shieldRecharge.Step(Time.time, s.Shield, s.ShieldMax) && _active && !_shipDestroyed)
            {
                ClientAudio.Instance?.Cue("shield_recharge", 0.6f);
            }
        }

        /// <summary>The nearest few other ships (pilots at the helm, NPC traders, hostiles) each get a 3D engine loop, pitched
        /// by their size, faded by distance and bent by the Doppler of their motion relative to the camera.</summary>
        private void UpdateRemoteEngines(float dt, float sfx, bool muffled)
        {
            if (Camera == null || _root == null)
            {
                return;
            }

            Vector3 ear = Camera.transform.position;
            TrackListener(ear, dt);
            GatherNearEngines(ear);
            AssignEngineVoices();
            foreach (var voice in _remoteVoices)
            {
                if (voice != null && voice.Id != null && voice.Src != null)
                {
                    UpdateEngineVoice(voice, ear, dt, sfx, muffled);
                }
            }
        }

        /// <summary>The listener's (camera's) velocity, smoothed — a camera cut (V, a cinematic) is no motion.</summary>
        private void TrackListener(Vector3 ear, float dt)
        {
            Vector3 vel = _listenerHasLast && dt > 1e-4f ? (ear - _listenerLastPos) / dt : Vector3.zero;
            if (vel.sqrMagnitude > TeleportSpeed * TeleportSpeed)
            {
                vel = Vector3.zero;
            }

            _listenerVel = Vector3.Lerp(_listenerVel, vel, dt > 0f ? 1f - Mathf.Exp(-dt / 0.15f) : 0f);
            _listenerLastPos = ear;
            _listenerHasLast = true;
        }

        /// <summary>Collects the up to <see cref="RemoteEngineVoices"/> nearest engines within range, nearest first.</summary>
        private void GatherNearEngines(Vector3 ear)
        {
            _nearCount = 0;
            float rangeSq = RemoteEngineRange * RemoteEngineRange;
            foreach (var kv in _remotePlayers)
            {
                // A pilot at the helm only: a parked ship (its pilot walks inside) and a spacewalk have the engines cut (#2431).
                var av = kv.Value;
                if (av.Root != null && av.Ship != null && !av.Eva && !av.Parked && av.Ship.activeInHierarchy)
                {
                    OfferEngine(kv.Key, av.Ship.transform, -1f, ear, rangeSq);
                }
            }

            foreach (var kv in _entities)
            {
                if (kv.Value != null && kv.Value.activeInHierarchy && _entityKinds.TryGetValue(kv.Key, out var kind))
                {
                    float size = FlightEngineMix.EngineSizeForKind(kind);
                    if (size > 0f)
                    {
                        OfferEngine(kv.Key, kv.Value.transform, size, ear, rangeSq);
                    }
                }
            }
        }

        private void OfferEngine(string id, Transform target, float size, Vector3 ear, float rangeSq)
        {
            float distSq = (target.position - ear).sqrMagnitude;
            if (distSq > rangeSq)
            {
                return;
            }

            int at = _nearCount;
            while (at > 0 && _nearDistSq[at - 1] > distSq)
            {
                at--;
            }

            if (at >= RemoteEngineVoices)
            {
                return;
            }

            for (int i = Mathf.Min(_nearCount, RemoteEngineVoices - 1); i > at; i--)
            {
                _nearIds[i] = _nearIds[i - 1];
                _nearTargets[i] = _nearTargets[i - 1];
                _nearSizes[i] = _nearSizes[i - 1];
                _nearDistSq[i] = _nearDistSq[i - 1];
            }

            _nearIds[at] = id;
            _nearTargets[at] = target;
            _nearSizes[at] = size;
            _nearDistSq[at] = distSq;
            _nearCount = Mathf.Min(_nearCount + 1, RemoteEngineVoices);
        }

        /// <summary>Keeps each voice on its ship while that ship stays among the nearest; a ship new to the set takes a free
        /// voice (or one that has faded out). A voice whose ship left the set fades out in <see cref="UpdateEngineVoice"/>.</summary>
        private void AssignEngineVoices()
        {
            foreach (var voice in _remoteVoices)
            {
                if (voice != null)
                {
                    voice.Wanted = false;
                }
            }

            for (int n = 0; n < _nearCount; n++)
            {
                var voice = VoiceFor(_nearIds[n]);
                _nearMatched[n] = voice != null;
                if (voice != null)
                {
                    TakeVoice(voice, n);
                }
            }

            for (int n = 0; n < _nearCount; n++)
            {
                var voice = _nearMatched[n] ? null : FreeVoice();
                if (voice == null)
                {
                    continue; // matched already — or every voice still fades out another ship (next frame)
                }

                voice.Id = _nearIds[n];
                voice.HasLast = false;
                voice.Vel = Vector3.zero;
                voice.Doppler = 1f;
                voice.Fade = 0f;
                TakeVoice(voice, n);
            }
        }

        private void TakeVoice(EngineVoice voice, int n)
        {
            voice.Target = _nearTargets[n]; // a pilot's hull swapped for its voxel design is a new transform
            voice.Size = _nearSizes[n] > 0f ? _nearSizes[n] : RemoteShipEngineSize(_nearIds[n], _nearTargets[n].gameObject);
            voice.Wanted = true;
        }

        private EngineVoice VoiceFor(string id)
        {
            foreach (var voice in _remoteVoices)
            {
                if (voice != null && voice.Id == id)
                {
                    return voice;
                }
            }

            return null;
        }

        private EngineVoice FreeVoice()
        {
            for (int i = 0; i < _remoteVoices.Length; i++)
            {
                var voice = _remoteVoices[i];
                if (voice == null)
                {
                    return _remoteVoices[i] = NewEngineVoice();
                }

                if (voice.Id == null || (!voice.Wanted && voice.Fade < 0.05f))
                {
                    return voice;
                }
            }

            return null;
        }

        private EngineVoice NewEngineVoice()
        {
            var clip = FlightClip(ClientAudio.Instance, "engine_idle");
            if (clip == null)
            {
                return null;
            }

            var go = new GameObject("RemoteEngine");
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.dopplerLevel = 0f;                     // the Doppler is ours: Unity Web has none, the desktop would double it
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = RemoteEngineRange;       // Unity only pans — the distance fade is FlightEngineMix's curve
            src.maxDistance = RemoteEngineRange + 1f;
            src.volume = 0f;
            return new EngineVoice { Src = src };
        }

        /// <summary>A pilot's ship size from its drawn hull (the box around its meshes), measured once per hull.</summary>
        private float RemoteShipEngineSize(string id, GameObject ship)
        {
            if (_remoteEngineSize.TryGetValue(id, out var known) && known.Ship == ship)
            {
                return known.Size;
            }

            bool any = false;
            var box = new Bounds(ship.transform.position, Vector3.zero);
            foreach (var r in ship.GetComponentsInChildren<MeshRenderer>())
            {
                if (any)
                {
                    box.Encapsulate(r.bounds);
                }
                else
                {
                    box = r.bounds;
                    any = true;
                }
            }

            float size = FlightEngineMix.SizeFromExtent(any ? box.extents.magnitude : 0f);
            _remoteEngineSize[id] = (ship, size);
            return size;
        }

        private void UpdateEngineVoice(EngineVoice voice, Vector3 ear, float dt, float sfx, bool muffled)
        {
            bool alive = voice.Target != null;
            voice.Wanted &= alive;
            Vector3 pos = alive ? voice.Target.position : voice.LastPos;
            Vector3 vel = voice.HasLast && dt > 1e-4f ? (pos - voice.LastPos) / dt : Vector3.zero;
            if (vel.sqrMagnitude > TeleportSpeed * TeleportSpeed)
            {
                vel = Vector3.zero;
            }

            voice.Vel = Vector3.Lerp(voice.Vel, vel, dt > 0f ? 1f - Mathf.Exp(-dt / 0.2f) : 0f);
            voice.LastPos = pos;
            voice.HasLast = true;

            Vector3 to = pos - ear;
            Vector3 rel = voice.Vel - _listenerVel;
            float closing = FlightEngineMix.ClosingSpeed(to.x, to.y, to.z, rel.x, rel.y, rel.z);
            voice.Doppler = FlightEngineMix.Ease(voice.Doppler, FlightEngineMix.DopplerPitch(closing), dt, 0.25f, 0.25f);
            voice.Fade = FlightEngineMix.Ease(voice.Fade, voice.Wanted ? 1f : 0f, dt, 0.5f, 0.4f);
            if (!voice.Wanted && voice.Fade < 0.01f)
            {
                voice.Src.Stop();
                voice.Id = null;
                voice.Target = null;
                return;
            }

            float level = FlightEngineMix.RemoteEngineVolume(to.magnitude, RemoteEngineRange, voice.Size, muffled);
            voice.Src.transform.position = pos;
            voice.Src.pitch = FlightEngineMix.RemoteEnginePitch(voice.Size) * voice.Doppler;
            voice.Src.volume = Mathf.Clamp01(level * voice.Fade * sfx);
            if (!voice.Src.isPlaying)
            {
                voice.Src.Play();
            }
        }
    }
}
