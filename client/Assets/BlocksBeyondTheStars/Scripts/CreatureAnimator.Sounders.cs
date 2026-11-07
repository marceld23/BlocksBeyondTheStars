// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The body language of Soaniri (#3355, #3359, #3366), layered on the shared poses (which set every rotation fresh each
    /// frame, so everything here adds on top):
    /// <list type="bullet">
    /// <item><b>Sounder</b>: the lower arm pair waves a beat behind the upper one and every elbow sways a little — the sketches'
    /// slightly wavy arms. An <b>Echo</b> (<see cref="RigDescription.Drift"/>) floats a hand above the ground with a slow bob and
    /// lets all four arms drift slowly, as if under water.</item>
    /// <item><b>Soundi</b>: the bushy tail carried up (lowered when it lies down).</item>
    /// <item><b>Soundrak</b>: the bud's lips open with every call and the scream and close slowly again; a provoked Soundrak
    /// holds them a little open.</item>
    /// <item><b>Soundeling</b>: asleep (or hiding in the dark) it curls into a squashed fluff ball with its feet tucked in; while
    /// it carries a light it holds the lamp up (<see cref="SetCarrying"/>).</item>
    /// </list>
    /// </summary>
    public sealed partial class CreatureAnimator
    {
        private const int KindNone = 0, KindSounder = 1, KindSoundi = 2, KindSoundrak = 3, KindSoundeling = 4;

        private int _soundersKind = -1;     // which body of Soaniri this rig is (-1 = not looked yet)
        private Quaternion[] _petalRest;    // the Soundrak's lips at rest (closed)
        private float _budOpen;             // degrees the bud is open, decaying after a call

        /// <summary>#3366: whether the Soundeling carries a light (the view sets it every update) — its lamp shows meanwhile.</summary>
        public void SetCarrying(bool carrying)
        {
            var lamp = _rig?.CarriedLight;
            if (lamp != null && lamp.activeSelf != carrying)
            {
                lamp.SetActive(carrying);
            }
        }

        private void PoseSounders(float dt, float t, float moving)
        {
            if (_soundersKind < 0)
            {
                _soundersKind = _rig == null ? KindNone : _rig.BodyPlan switch
                {
                    "Sounder" => KindSounder,
                    "Soundi" => KindSoundi,
                    "Soundrak" => KindSoundrak,
                    "Soundeling" => KindSoundeling,
                    _ => KindNone,
                };
            }

            switch (_soundersKind)
            {
                case KindSounder:
                    PoseSounderArms(t, moving);
                    break;
                case KindSoundi:
                    PoseSoundiTail();
                    break;
                case KindSoundrak:
                    PoseSoundrakBud(dt, t);
                    break;
                case KindSoundeling:
                    PoseSoundelingCurl();
                    break;
            }
        }

        /// <summary>The Sounder's four arms on top of the biped arm swing; an Echo floats and drifts.</summary>
        private void PoseSounderArms(float t, float moving)
        {
            bool drift = _rig.Drift;
            float awake = 1f - _rest;
            for (int i = 0; i < _arms.Length; i++)
            {
                var arm = _arms[i];
                if (arm?.Shoulder == null)
                {
                    continue;
                }

                float side = arm.Side == 0 ? -1f : 1f;
                float phase = (arm.Side * Mathf.PI) + (arm.Row * 0.9f);
                float lift = arm.Row == 1 ? Mathf.Sin((t * 1.1f) + phase) * 6f * (1f - (0.5f * moving)) : 0f;
                float roll = 0f;
                if (drift)
                {
                    lift += Mathf.Sin((t * 0.5f) + (i * 0.8f)) * 14f;
                    roll = Mathf.Sin((t * 0.4f) + i) * 10f * side;
                }

                arm.Shoulder.localRotation *= Quaternion.Euler(lift * awake, 0f, roll * awake);
                if (arm.Elbow != null)
                {
                    float wave = Mathf.Sin((t * (drift ? 0.6f : 1.3f)) + i) * (drift ? 12f : 4f);
                    arm.Elbow.localRotation *= Quaternion.Euler(wave * awake, 0f, 0f);
                }
            }

            if (drift && _body != null)
            {
                _body.localPosition += Vector3.up * (_rig.Hover + (Mathf.Sin(t * 0.9f) * 0.05f));
            }
        }

        /// <summary>The Soundi carries its bushy tail up — PoseTail sets the links fresh, so the lift goes on the root link.</summary>
        private void PoseSoundiTail()
        {
            if (_tail.Length > 0 && _tail[0] != null)
            {
                _tail[0].localRotation = Quaternion.Euler(-38f * (1f - _rest), 0f, 0f) * _tail[0].localRotation;
            }
        }

        /// <summary>The Soundrak's bud: the jaw pulse opens the lips wide, they close again over half a second; provoked, they stay
        /// a little open.</summary>
        private void PoseSoundrakBud(float dt, float t)
        {
            var petals = _rig.Petals;
            if (petals == null || petals.Length == 0)
            {
                return;
            }

            if (_petalRest == null)
            {
                _petalRest = new Quaternion[petals.Length];
                for (int i = 0; i < petals.Length; i++)
                {
                    _petalRest[i] = petals[i] != null ? petals[i].localRotation : Quaternion.identity;
                }
            }

            float pulse = _jawT < _jawDur ? Mathf.Sin(Mathf.Clamp01(_jawT / _jawDur) * Mathf.PI) * _jawDeg * 1.6f : 0f;
            _budOpen = Mathf.Max(pulse, Mathf.MoveTowards(_budOpen, _hostile ? 15f : 0f, dt * 60f));
            for (int i = 0; i < petals.Length; i++)
            {
                if (petals[i] != null)
                {
                    float breathe = Mathf.Sin((t * 0.7f) + i) * 3f;
                    petals[i].localRotation = _petalRest[i] * Quaternion.Euler(_budOpen + breathe, 0f, 0f); // + tips the lip outward
                }
            }
        }

        /// <summary>The Soundeling curled up: the ball squashed a little, the feet tucked into the fluff.</summary>
        private void PoseSoundelingCurl()
        {
            if (_body != null && _rest > 0f)
            {
                _body.localScale = Vector3.Scale(_body.localScale, new Vector3(1f + (0.12f * _rest), 1f - (0.2f * _rest), 1f + (0.12f * _rest)));
            }

            float feet = Mathf.Lerp(1f, 0.1f, _rest);
            for (int i = 0; i < _legs.Length; i++)
            {
                if (_legs[i]?.Hip != null)
                {
                    _legs[i].Hip.localScale = Vector3.one * feet;
                }
            }
        }
    }
}
