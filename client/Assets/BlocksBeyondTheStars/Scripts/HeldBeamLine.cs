// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The held beam of a weapon while it is on (#3898, the Guardian laser): one rented line of the <c>FxBeam</c> shader
    /// the laser's shot uses — in the item's own fx colours, wider than the shot (<see cref="Width"/> against 0.075) and
    /// pulsing — with a small glow at the muzzle, a bigger one where it ends, sparks and a short light where it hits
    /// something, and the weapon's loop sound for as long as it is held. The player's own beam and every other player's
    /// are the same object; only the sound differs (2D for the own one, positional for another's).
    /// <para>Cheap by construction: one line, a few pooled particles a second, no persistent light. Built from the
    /// <see cref="FxKit"/> toolkit alone — no new shader, no new material type. A missing shader or a missing sound clip
    /// leaves that part out and never fails.</para>
    /// </summary>
    public sealed class HeldBeamLine
    {
        /// <summary>The beam's width in metres at an fx size of 1 (the laser's single shot is 0.075).</summary>
        public const float Width = 0.11f;

        private readonly FxLook _look;
        private readonly bool _local;
        private LineRenderer _line;
        private GameObject _loop;
        private float _nextGlow;
        private float _nextSparks;
        private Vector3 _from;
        private Vector3 _to;
        private bool _drawn;
        private bool _ended;

        /// <summary>Starts a beam in the look of an item. <paramref name="local"/> = the player's own beam.</summary>
        public HeldBeamLine(FxLook look, bool local)
        {
            _look = look;
            _local = local;
            var mat = FxKit.BeamMaterial("held_beam", look.Color2, coreWidth: 0.42f, noiseScale: 1.6f, noiseSpeed: 18f, pulse: 1.2f, intensity: 3.2f);
            if (mat != null)
            {
                _line = FxKit.Ensure().RentBeam(mat);
                _line.widthMultiplier = Width * look.Size;
                var c = FxKit.Lin(look.Color);
                _line.startColor = c;
                _line.endColor = c;
            }

            StartLoop();
        }

        /// <summary>True until <see cref="End"/>.</summary>
        public bool Alive => !_ended;

        private void StartLoop()
        {
            var audio = ClientAudio.Instance;
            var clip = audio != null ? audio.Clip(BeamWeaponRules.LoopCue) : null;
            if (clip == null)
            {
                return; // no clip in this build: the beam is silent, nothing else changes
            }

            _loop = new GameObject("HeldBeamLoop");
            _loop.transform.SetParent(FxKit.Ensure().transform, false);
            var src = _loop.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = _local ? 0f : 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 3f;
            src.maxDistance = 40f;
            src.dopplerLevel = 0f;
            src.volume = ClientAudio.EffectsVolume * (_local ? 0.4f : 0.5f);
            src.Play();
        }

        /// <summary>Lays the beam for this frame from <paramref name="from"/> (the muzzle) to <paramref name="to"/>.
        /// <paramref name="hit"/> = it ends on something (an entity or a block), <paramref name="normal"/> the surface's
        /// outward normal there (zero = unknown: the sparks fly back toward the muzzle).</summary>
        public void Draw(Vector3 from, Vector3 to, bool hit, Vector3 normal)
        {
            if (_ended)
            {
                return;
            }

            _from = from;
            _to = to;
            _drawn = true;
            if (_line != null)
            {
                _line.SetPosition(0, from);
                _line.SetPosition(1, to);
                _line.widthMultiplier = Width * _look.Size * (1f + (0.12f * Mathf.Sin(Time.time * 38f)));
            }

            if (_loop != null)
            {
                _loop.transform.position = from;
            }

            float now = Time.time;
            if (now >= _nextGlow)
            {
                _nextGlow = now + 0.045f;
                FxKit.Emit(FxKit.Kind.Glow, from, Vector3.zero, 0.14f * _look.Size, 0.09f, _look.Color2);
                FxKit.Emit(FxKit.Kind.Glow, to + (Random.insideUnitSphere * 0.04f), Vector3.zero, (hit ? 0.42f : 0.2f) * _look.Size, 0.14f, _look.Color);
            }

            if (hit && now >= _nextSparks)
            {
                _nextSparks = now + 0.11f;
                var back = normal.sqrMagnitude < 0.01f ? from - to : normal;
                if (back.sqrMagnitude < 1e-6f)
                {
                    back = Vector3.up;
                }

                back.Normalize();
                FxKit.Burst(FxKit.Kind.Sparks, to + (back * 0.05f), 3, back, 60f, 2f, 5.5f, 0.03f, 0.05f, 0.15f, 0.32f, _look.Color, _look.Color2);
                FxLights.Flash(to + (back * 0.4f), _look.Color, 1.3f, 5f, 0.16f);
            }
        }

        /// <summary>Takes the beam away without a fade or a sound (the world is going away) — never creates anything.</summary>
        public void Drop()
        {
            if (_ended)
            {
                return;
            }

            _ended = true;
            if (_line != null && FxKit.Instance != null)
            {
                FxKit.Instance.ReleaseBeam(_line);
            }

            _line = null;
            if (_loop != null)
            {
                UnityEngine.Object.Destroy(_loop);
                _loop = null;
            }
        }

        /// <summary>The beam goes off: the line fades, the loop stops and the end sound plays once.</summary>
        public void End()
        {
            if (_ended)
            {
                return;
            }

            _ended = true;
            if (_line != null)
            {
                var mat = _line.sharedMaterial;
                FxKit.Ensure().ReleaseBeam(_line);
                _line = null;
                if (_drawn && mat != null)
                {
                    FxKit.Beam(_from, _to, _look.Color, Width * _look.Size, 0.16f, mat); // a short fading copy
                }
            }

            if (_loop != null)
            {
                UnityEngine.Object.Destroy(_loop);
                _loop = null;
            }

            if (!_drawn)
            {
                return;
            }

            var audio = ClientAudio.Instance;
            if (audio == null)
            {
                return;
            }

            if (_local)
            {
                audio.Cue(BeamWeaponRules.EndCue, 0.6f);
            }
            else
            {
                audio.At(BeamWeaponRules.EndCue, _from, 1f, 0.7f);
            }
        }
    }
}
