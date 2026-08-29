// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Procedural creature animation: swings the legs while the body moves, flaps the wings (only in the
    /// air since #1333 — a perched bird folds them), sways the tail, tucks the legs mid-jump and squashes on
    /// landing, undulates crawlers and swimmers. Self-driven from the root's world movement — the same
    /// approach as <see cref="PlayerAvatar"/> — plus the motion class + airborne/perched flags the server
    /// streams (<see cref="SetMotion"/>). Pivots are supplied by <see cref="CreatureBuilder"/>.
    /// </summary>
    public sealed class CreatureAnimator : MonoBehaviour
    {
        private enum Idle { Breathe, Graze, Alert, Lunge }

        private Transform[] _legs;
        private Transform[] _wings;
        private Transform _tail;
        private Transform _head;
        private Transform _body;   // the body rig — undulated for swimming / crawling, squashed on landing
        private bool _aquatic;     // swimmers undulate + flutter instead of striding

        // Medusa plan (#637): the bell pulses (scale) and the rim tentacles sway out of phase.
        private Transform _bell;
        private Transform[] _tentacles;
        private Vector3 _bellBaseScale = Vector3.one;

        /// <summary>Walk-cadence multiplier (#638): titans set this below 1 so a giant strides slowly —
        /// a huge body taking sheep-paced steps is the classic scale-breaking tell. Default 1 = unchanged.</summary>
        public float CadenceScale = 1f;

        // Motion class + vertical state from the server (#1333). Defaults read as a grounded walker, which is
        // what a legacy server (no fields) sends.
        private string _motion = "walker";
        private bool _airborne;
        private bool _perched;
        private bool _prevAirborne;
        private float _squashT = 999f;   // time into the landing squash (large = none)
        private float _legTuck;          // 0..1 smoothed leg tuck while airborne
        private float _wingFold;         // 0..1 smoothed fold while perched / grounded flier
        private const float SquashDuration = 0.18f;

        /// <summary>Registers the medusa parts (#637) — call after <see cref="Init"/> on medusa builds.</summary>
        public void InitMedusa(Transform bell, Transform[] tentacles)
        {
            _bell = bell;
            _tentacles = tentacles;
            _bellBaseScale = bell != null ? bell.localScale : Vector3.one;
        }

        /// <summary>Feeds the streamed motion class ("walker" | "crawler" | "flier" | "hoverer" | "swimmer") and
        /// vertical flags each frame (#1333). A landing (airborne → grounded) starts the squash.</summary>
        public void SetMotion(string motion, bool airborne, bool perched)
        {
            _motion = string.IsNullOrEmpty(motion) ? "walker" : motion;
            if (_prevAirborne && !airborne && (_motion == "walker" || _motion == "crawler"))
            {
                _squashT = 0f; // just landed
            }

            _prevAirborne = airborne;
            _airborne = airborne;
            _perched = perched;
        }

        private float _phase;     // per-creature offset so they don't move in lockstep
        private float _walk;      // leg-swing phase
        private Vector3 _lastPos;
        private bool _hasPrev;

        // Per-temperament idle head gestures.
        private bool _hostile;
        private bool _asleep;
        private Idle _idleKind = Idle.Breathe;
        private float _gestureTimer;   // counts down to the next gesture
        private float _gestureT = 999f; // time into the current gesture (large = none active)
        private float _gestureDur;
        private float _gestureLook;    // a random look direction for the alert gesture

        public void Init(Transform[] legs, Transform[] wings, Transform tail, Transform head, Transform body,
            bool hostile, bool asleep, bool aquatic, string temperament)
        {
            _legs = legs;
            _wings = wings;
            _tail = tail;
            _head = head;
            _body = body;
            _hostile = hostile;
            _asleep = asleep;
            _aquatic = aquatic;
            _phase = (GetEntityId().GetHashCode() & 0x3ff) * 0.1f; // stable pseudo-random offset

            // Map the species temperament to its resting idle gesture.
            string t = (temperament ?? string.Empty).ToLowerInvariant();
            _idleKind = hostile || t.Contains("aggress") || t.Contains("hostile") ? Idle.Lunge
                : t.Contains("skittish") || t.Contains("timid") || t.Contains("wary") || t.Contains("flighty") ? Idle.Alert
                : t.Contains("passive") || t.Contains("docile") || t.Contains("calm") || t.Contains("placid") ? Idle.Graze
                : Idle.Breathe;
            _gestureTimer = Random.Range(1.5f, 4f);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            var pos = transform.position;
            float speed = 0f;
            if (_hasPrev)
            {
                var d = pos - _lastPos;
                d.y = 0f;
                speed = d.magnitude / dt;
            }

            _lastPos = pos;
            _hasPrev = true;

            bool crawler = _motion == "crawler";
            bool flier = _motion == "flier";
            bool hoverer = _motion == "hoverer";
            bool undulates = _aquatic || crawler; // swimmers and land crawlers both move by the body, not the legs
            int legCount = _legs != null ? _legs.Length : 0;
            float scuttle = crawler && legCount >= 6 ? 1.8f : 1f; // many short legs beat faster

            float moving = Mathf.Clamp01(speed / 3f);
            _walk += dt * (3f + speed * 2.2f) * CadenceScale * scuttle; // titans stride slowly (#638)
            float t = Time.time + _phase;

            // Medusa (#637): pulse the bell (squash-and-stretch around its base scale) and sway each rim
            // tentacle on its own phase; there are no legs/head to animate, so this replaces the gait.
            if (_bell != null)
            {
                float pulse = Mathf.Sin(t * (_asleep ? 0.9f : 1.8f));
                _bell.localScale = new Vector3(
                    _bellBaseScale.x * (1f - 0.06f * pulse),
                    _bellBaseScale.y * (1f + 0.12f * pulse),
                    _bellBaseScale.z * (1f - 0.06f * pulse));

                if (_tentacles != null)
                {
                    for (int i = 0; i < _tentacles.Length; i++)
                    {
                        if (_tentacles[i] == null)
                        {
                            continue;
                        }

                        float ph = t * 1.3f + i * 0.9f;
                        _tentacles[i].localRotation = Quaternion.Euler(
                            Mathf.Sin(ph) * (7f + 6f * moving), 0f, Mathf.Cos(ph * 0.8f) * (7f + 6f * moving));
                    }
                }
            }

            // Legs: alternate front/back, amplitude scales with speed (plus a tiny idle shuffle). Aquatic
            // species and crawlers barely stride — their limbs read as fins paddling / short scuttling legs.
            // Airborne (#1333) the legs tuck back under the body; a perched flier just stands.
            bool groundAirborne = _airborne && (_motion == "walker" || crawler);
            _legTuck = Mathf.Lerp(_legTuck, groundAirborne ? 1f : 0f, 1f - Mathf.Exp(-12f * dt));
            if (_legs != null)
            {
                float amp = Mathf.Lerp(2f, undulates ? 12f : 32f, moving) * (1f - _legTuck);
                for (int i = 0; i < _legs.Length; i++)
                {
                    if (_legs[i] == null)
                    {
                        continue;
                    }

                    float dir = ((i & 1) == 0) ? 1f : -1f;     // left/right out of phase
                    float row = ((i >> 1) & 1) == 0 ? 0f : Mathf.PI; // alternate leg pairs
                    float swing = Mathf.Sin(_walk + row) * amp * dir;
                    _legs[i].localRotation = Quaternion.Euler(swing - 35f * _legTuck, 0f, 0f);
                }
            }

            // Wings (#1333): beat only in the air — a hard flap when flying, a calm idle beat for a hoverer's
            // vanes; on the ground (perched flier, a ground bird between bounds) they fold up over the back.
            bool wingsBeat = flier ? _airborne : hoverer || (_airborne && _motion == "walker") || _motion == "swimmer";
            _wingFold = Mathf.Lerp(_wingFold, wingsBeat ? 0f : 1f, 1f - Mathf.Exp(-8f * dt));
            if (_wings != null)
            {
                float rate = hoverer ? 3f : 6f + moving * 6f;
                float flap = Mathf.Sin(t * rate) * Mathf.Lerp(14f, hoverer ? 22f : 42f, moving) * (1f - _wingFold);
                for (int i = 0; i < _wings.Length; i++)
                {
                    if (_wings[i] != null)
                    {
                        float side = ((i & 1) == 0) ? 1f : -1f; // mirror left/right
                        _wings[i].localRotation = Quaternion.Euler(0f, 0f, (flap + 70f * _wingFold) * side);
                    }
                }
            }

            // Tail: a slow side-to-side sway, a touch livelier on the move (and quicker on hostiles). For a
            // swimmer or crawler the tail is the motor — it beats faster and wider, leading the body's undulation.
            if (_tail != null)
            {
                float rate = undulates ? 3.4f : (_hostile ? 2.6f : 1.8f);
                float sway = Mathf.Sin(t * rate) * Mathf.Lerp(8f, undulates ? 34f : 18f, moving);
                _tail.localRotation = Quaternion.Euler(0f, sway, 0f);
            }

            // Swim / crawl: undulate the whole body rig — a yaw weave (the tail leads, the body follows a beat
            // behind), a gentle counter-roll and a slow vertical glide (swimmers only — a crawler stays pressed to
            // the ground). Present even while drifting, stronger on the move. A landing squash (#1333) rides on
            // top for ground movers: the body compresses and springs back over a fraction of a second.
            if (_body != null)
            {
                float weave = 0f, roll = 0f, glide = 0f;
                if (undulates)
                {
                    float sp = t * 3.4f;
                    weave = Mathf.Sin(sp - 0.7f) * Mathf.Lerp(5f, 15f, moving); // body lags the tail beat
                    roll = Mathf.Sin(sp - 1.2f) * Mathf.Lerp(2f, 7f, moving);
                    glide = _aquatic ? Mathf.Sin(t * 1.1f) * 0.05f : 0f;    // slow rise/sink bob
                }

                float squash = 0f;
                if (_squashT < SquashDuration)
                {
                    _squashT += dt;
                    squash = Mathf.Sin(Mathf.Clamp01(_squashT / SquashDuration) * Mathf.PI) * 0.12f; // 0 → 0.12 → 0
                }

                _body.localRotation = Quaternion.Euler(0f, weave, roll);
                _body.localPosition = new Vector3(0f, glide, 0f);
                _body.localScale = new Vector3(1f + squash * 0.6f, 1f - squash, 1f + squash * 0.6f);
            }

            // Head: breathing + a per-temperament idle gesture (graze / alert / lunge) while stationary.
            if (_head != null)
            {
                float pitch = 0f, yaw = 0f;
                if (_asleep)
                {
                    pitch = 22f + Mathf.Sin(t * 0.6f) * 2f; // head rests low, slow sleeping breath
                }
                else
                {
                    pitch += Mathf.Sin(t * 1.6f) * 3f * (1f - moving); // gentle idle breathing

                    if (moving < 0.25f)
                    {
                        _gestureTimer -= dt;
                        if (_gestureTimer <= 0f && _gestureT >= _gestureDur)
                        {
                            StartGesture();
                        }
                    }

                    if (_gestureT < _gestureDur)
                    {
                        _gestureT += dt;
                        float f = Mathf.Clamp01(_gestureT / _gestureDur);
                        float p = Mathf.Sin(f * Mathf.PI); // 0 → 1 → 0
                        switch (_idleKind)
                        {
                            case Idle.Graze: pitch += 52f * p; break;                         // dip head to the ground
                            case Idle.Alert: pitch -= 16f * p; yaw += _gestureLook * Mathf.Sin(f * Mathf.PI * 2f); break; // snap up + look
                            case Idle.Lunge: pitch += 34f * p * (0.6f + 0.4f * Mathf.Sin(f * Mathf.PI * 3f)); break;       // sharp aggressive thrust
                            default: pitch += 4f * p; break;
                        }
                    }
                }

                _head.localRotation = Quaternion.Euler(pitch, yaw, 0f);
            }
        }

        private void StartGesture()
        {
            _gestureT = 0f;
            switch (_idleKind)
            {
                case Idle.Graze: _gestureDur = Random.Range(1.6f, 2.6f); _gestureTimer = Random.Range(3f, 6f); break;
                case Idle.Alert: _gestureDur = Random.Range(0.5f, 0.9f); _gestureTimer = Random.Range(1.5f, 4f);
                    _gestureLook = Random.Range(0, 2) == 0 ? -26f : 26f; break;
                case Idle.Lunge: _gestureDur = Random.Range(0.4f, 0.7f); _gestureTimer = Random.Range(2.5f, 5f); break;
                default: _gestureDur = Random.Range(1f, 1.6f); _gestureTimer = Random.Range(4f, 7f); break;
            }
        }
    }
}
