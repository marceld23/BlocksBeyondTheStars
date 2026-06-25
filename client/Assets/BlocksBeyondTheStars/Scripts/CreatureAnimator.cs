// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Procedural creature animation: swings the legs while the body moves, flaps the wings (a gentle
    /// idle flap that strengthens with speed), and sways the tail. Self-driven from the root's world
    /// movement — the same approach as <see cref="PlayerAvatar"/> — so it needs no per-frame data from
    /// the server. Pivots are supplied by <see cref="CreatureBuilder"/>.
    /// </summary>
    public sealed class CreatureAnimator : MonoBehaviour
    {
        private enum Idle { Breathe, Graze, Alert, Lunge }

        private Transform[] _legs;
        private Transform[] _wings;
        private Transform _tail;
        private Transform _head;
        private Transform _body;   // the body rig — undulated for swimming
        private bool _aquatic;     // swimmers undulate + flutter instead of striding

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
            _phase = (GetInstanceID() & 0x3ff) * 0.1f; // stable pseudo-random offset

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

            float moving = Mathf.Clamp01(speed / 3f);
            _walk += dt * (3f + speed * 2.2f);
            float t = Time.time + _phase;

            // Legs: alternate front/back, amplitude scales with speed (plus a tiny idle shuffle). Aquatic
            // species barely stride — their limbs read as fins paddling, so the swing stays small.
            if (_legs != null)
            {
                float amp = Mathf.Lerp(2f, _aquatic ? 12f : 32f, moving);
                for (int i = 0; i < _legs.Length; i++)
                {
                    if (_legs[i] == null)
                    {
                        continue;
                    }

                    float dir = ((i & 1) == 0) ? 1f : -1f;     // left/right out of phase
                    float row = ((i >> 1) & 1) == 0 ? 0f : Mathf.PI; // alternate leg pairs
                    _legs[i].localRotation = Quaternion.Euler(Mathf.Sin(_walk + row) * amp * dir, 0f, 0f);
                }
            }

            // Wings: flap around Z — a calm idle beat that picks up when flying.
            if (_wings != null)
            {
                float flap = Mathf.Sin(t * (6f + moving * 6f)) * Mathf.Lerp(14f, 42f, moving);
                for (int i = 0; i < _wings.Length; i++)
                {
                    if (_wings[i] != null)
                    {
                        float side = ((i & 1) == 0) ? 1f : -1f; // mirror left/right
                        _wings[i].localRotation = Quaternion.Euler(0f, 0f, flap * side);
                    }
                }
            }

            // Tail: a slow side-to-side sway, a touch livelier on the move (and quicker on hostiles). For a
            // swimmer the tail is the motor — it beats faster and wider, leading the body's undulation.
            if (_tail != null)
            {
                float rate = _aquatic ? 3.4f : (_hostile ? 2.6f : 1.8f);
                float sway = Mathf.Sin(t * rate) * Mathf.Lerp(8f, _aquatic ? 34f : 18f, moving);
                _tail.localRotation = Quaternion.Euler(0f, sway, 0f);
            }

            // Swim: undulate the whole body rig — a yaw weave (the tail leads, the body follows a beat behind),
            // a gentle counter-roll and a slow vertical glide. Present even while drifting, stronger on the move.
            if (_aquatic && _body != null)
            {
                float sp = t * 3.4f;
                float weave = Mathf.Sin(sp - 0.7f) * Mathf.Lerp(5f, 15f, moving); // body lags the tail beat
                float roll = Mathf.Sin(sp - 1.2f) * Mathf.Lerp(2f, 7f, moving);
                float glide = Mathf.Sin(t * 1.1f) * 0.05f;                          // slow rise/sink bob
                _body.localRotation = Quaternion.Euler(0f, weave, roll);
                _body.localPosition = new Vector3(0f, glide, 0f);
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
