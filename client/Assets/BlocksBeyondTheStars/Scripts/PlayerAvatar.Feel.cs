// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Feel;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What makes the blocky figure read as alive (the feel pass): the emotes a player can play (<see cref="PlayEmote"/>
    /// — wave, nod, cheer, point, shake the head, dance, clap, sit, bow), the body's own reactions (a flinch when
    /// hurt, sinking down when defeated, a swimming stroke in water, a give in the knees on landing), a face that
    /// blinks and a mouth that moves while its owner speaks, and softer legs: the leg joints are eased instead of
    /// snapping from one pose into the next. Everything rides on the pose <see cref="Animate"/> computed, the way the
    /// greeting gestures do, and starts and ends at rest. The timing is pure (<see cref="EmoteCurves"/>).
    /// </summary>
    public sealed partial class PlayerAvatar
    {
        private const float LegEaseRate = 22f; // how fast the leg joints follow their pose (per second, exponential)

        private float _pointTimer, _shakeTimer, _danceTimer, _clapTimer, _bowEmoteTimer, _flinchTimer, _landTimer;
        private bool _sitting, _down, _swimming, _wasAirborne, _wasFalling;
        private float _downUntil;
        private Vector3 _downAt;
        private float _talkUntil;
        private float _legLEased, _legREased, _kneeLEased, _kneeREased;
        private bool _legsEased;

        // The face.
        private Transform _eyeL, _eyeR, _pupilL, _pupilR, _mouth;
        private Vector3 _eyeScale, _pupilScale, _mouthScale;
        private bool _faceFound;
        private float _nextBlink = -1f, _blinkUntil;
        private int _blinkCount;

        /// <summary>The figure is shown (not the own body in first person, not a cloaked player).</summary>
        public bool IsVisible => _visible;

        /// <summary>Sitting on the ground by the sit emote.</summary>
        public bool EmoteSitting => _sitting;

        /// <summary>#2593: a greeting wave (~1.2 s): one arm out to the side, the forearm up and wagging. The free hand —
        /// <paramref name="leftHand"/> when the right one holds a tool. Ignored while lying or while a wave plays.</summary>
        public void Wave(bool leftHand = false)
        {
            if (_lying || _waveTimer > 0f)
            {
                return;
            }

            _waveLeft = leftHand;
            _waveTimer = AvatarGestures.WaveSeconds;
            _cheerTimer = 0f; // one arm gesture at a time
        }

        /// <summary>#2593: a short nod (~0.6 s) — a dialog answer, a trade. Ignored while lying.</summary>
        public void Nod()
        {
            if (!_lying)
            {
                _nodTimer = AvatarGestures.NodSeconds;
            }
        }

        /// <summary>#2593: a cheer (~1.2 s): both arms up in a V, pumping — the village festival. Ignored while lying.</summary>
        public void Cheer()
        {
            if (_lying)
            {
                return;
            }

            _startled = false; // the arms-up gesture is the cheer again, not a startle (#3769)
            _armsUpSeconds = AvatarGestures.CheerSeconds;
            _cheerTimer = AvatarGestures.CheerSeconds;
            _waveTimer = 0f;
        }

        /// <summary>Plays an emote or shows a body state (<see cref="PlayerEmotes"/>). One timed emote at a time: a new
        /// one replaces the one that plays, and every one of them gets a sitting figure up again — except "sit" itself,
        /// which sits it down; "stand" gets it up and does nothing else. Ignored while lying in a bed.</summary>
        public void PlayEmote(byte emote)
        {
            if (_lying)
            {
                return;
            }

            switch (emote)
            {
                case PlayerEmotes.Hurt:
                    _flinchTimer = EmoteCurves.FlinchSeconds; // a reaction, not an emote: it leaves the rest alone
                    return;
                case PlayerEmotes.Down:
                    SetDown(true);
                    return;
                case PlayerEmotes.Up:
                    SetDown(false);
                    return;
                case PlayerEmotes.Sit:
                case PlayerEmotes.Stand:
                    // Said outright, never a switch: the relay is one-shot, so a player who came near later (or one press
                    // the server's pace dropped) saw a switched figure sit when it stood.
                    ClearEmotes();
                    _sitting = emote == PlayerEmotes.Sit;
                    return;
            }

            if (emote < PlayerEmotes.Wave || emote > PlayerEmotes.LastPlayable)
            {
                return; // an id this client does not know (a newer server): nothing
            }

            ClearEmotes();
            _sitting = false;
            switch (emote)
            {
                case PlayerEmotes.Wave: Wave(leftHand: _held != null); break;
                case PlayerEmotes.Nod: Nod(); break;
                case PlayerEmotes.Cheer: Cheer(); break;
                case PlayerEmotes.Point: _pointTimer = EmoteCurves.PointSeconds; break;
                case PlayerEmotes.HeadShake: _shakeTimer = EmoteCurves.HeadShakeSeconds; break;
                case PlayerEmotes.Dance: _danceTimer = EmoteCurves.DanceSeconds; break;
                case PlayerEmotes.Clap: _clapTimer = EmoteCurves.ClapSeconds; break;
                case PlayerEmotes.Bow: _bowEmoteTimer = EmoteCurves.BowSeconds; break;
            }
        }

        private void ClearEmotes()
        {
            _waveTimer = _cheerTimer = _nodTimer = 0f;
            _pointTimer = _shakeTimer = _danceTimer = _clapTimer = _bowEmoteTimer = 0f;
        }

        /// <summary>The figure is in water up to the chest: it swims instead of walking through it.</summary>
        public void SetSwimming(bool swimming) => _swimming = swimming;

        /// <summary>Defeated: the figure sinks to its knees, head hanging — until it is told otherwise, moves away (a
        /// respawn puts it somewhere else) or <see cref="EmoteCurves.DownSeconds"/> have passed.</summary>
        public void SetDown(bool down)
        {
            _down = down;
            if (down)
            {
                ClearEmotes();
                _sitting = false;
                _downUntil = Time.time + EmoteCurves.DownSeconds;
                _downAt = transform.position;
            }
        }

        /// <summary>A short flinch — the figure was hurt.</summary>
        public void Flinch() => _flinchTimer = EmoteCurves.FlinchSeconds;

        /// <summary>The figure's owner speaks (a voice-chat frame, a clip of a villager's line): the mouth moves for
        /// <paramref name="seconds"/>. Call it again while the speech lasts.</summary>
        public void Talk(float seconds = 0.25f) => _talkUntil = Mathf.Max(_talkUntil, Time.time + seconds);

        /// <summary>The feel layer of the pose: body states first (down, sitting, swimming replace the legs and arms),
        /// then the timed emotes and reactions on top, then the eased legs and the face.</summary>
        private void PoseFeel(float dt, float t, float moving, bool airborne,
            ref float armL, ref float armR, ref float elbowL, ref float elbowR, ref float legL, ref float legR,
            ref float kneeL, ref float kneeR, ref float rollL, ref float rollR, ref float yawL, ref float yawR,
            ref float headYaw, ref float nod)
        {
            bool free = !_lying && !_climbing && !_seated && !_gliding;
            PoseBodyState(free, t, moving, ref armL, ref armR, ref elbowL, ref elbowR, ref legL, ref legR, ref kneeL, ref kneeR,
                ref rollL, ref rollR, ref yawL, ref yawR, ref headYaw, ref nod);
            PoseLanding(dt, airborne, free, ref legL, ref legR, ref kneeL, ref kneeR);
            if (free)
            {
                PoseArmEmotes(dt, ref armL, ref armR, ref elbowL, ref elbowR, ref kneeL, ref kneeR, ref yawL, ref yawR, ref nod);
                PoseBodyEmotes(dt, ref armL, ref armR, ref elbowL, ref elbowR, ref legL, ref legR, ref kneeL, ref kneeR,
                    ref rollL, ref rollR, ref headYaw, ref nod);
            }
            else
            {
                // Lying, climbing, seated or gliding: these cannot play now, and one that waited played when the figure got
                // off — the flinch of a hit on a ladder, a dance picked on a chair — possibly much later.
                _pointTimer = _shakeTimer = _danceTimer = _clapTimer = _bowEmoteTimer = _flinchTimer = 0f;
            }

            EaseLegs(dt, ref legL, ref legR, ref kneeL, ref kneeR);
            PoseFace(dt);
        }

        /// <summary>The body states that replace the legs and the arms: defeated, sitting, swimming.</summary>
        private void PoseBodyState(bool free, float t, float moving,
            ref float armL, ref float armR, ref float elbowL, ref float elbowR, ref float legL, ref float legR,
            ref float kneeL, ref float kneeR, ref float rollL, ref float rollR, ref float yawL, ref float yawR,
            ref float headYaw, ref float nod)
        {
            // Sitting ends with the first step; a defeated figure gets up when it is somewhere else or its time is over.
            if (_sitting && (moving > 0.25f || !free))
            {
                _sitting = false;
            }

            if (_down && (Time.time > _downUntil || (transform.position - _downAt).sqrMagnitude > 2.25f))
            {
                _down = false;
            }

            if (free && _down)
            {
                legL = legR = -78f;
                kneeL = kneeR = 104f;
                armL = armR = 14f;
                elbowL = elbowR = -6f;
                rollL = rollR = yawL = yawR = 0f;
                headYaw = 0f;
                nod = 42f; // the head hangs
            }
            else if (free && _sitting)
            {
                float breath = Mathf.Sin(t * 1.2f) * 2f;
                legL = legR = -88f;
                kneeL = kneeR = 98f;
                armL = armR = -22f + breath;
                elbowL = elbowR = -38f;
            }
            else if (free && _swimming && !_crouched)
            {
                // A breast stroke: the arms reach forward and sweep out, the legs kick in turn.
                float stroke = Mathf.Sin(t * 3.2f);
                float kick = Mathf.Sin(t * 6.4f);
                armL = armR = -92f + (28f * stroke);
                elbowL = elbowR = -18f - (22f * Mathf.Max(0f, -stroke));
                rollL = -(18f + (26f * Mathf.Max(0f, -stroke)));
                rollR = 18f + (26f * Mathf.Max(0f, -stroke));
                legL = 12f * kick;
                legR = -12f * kick;
                kneeL = 14f + (12f * Mathf.Max(0f, kick));
                kneeR = 14f + (12f * Mathf.Max(0f, -kick));
                headYaw = 0f;
                PoseCrawl(t, ref armL, ref armR, ref elbowL, ref elbowR, ref legL, ref legR, ref kneeL, ref kneeR, ref rollL, ref rollR, ref nod);
            }
        }

        /// <summary>A landing gives in the knees for a moment (only a real one: the air pose ended on the ground, and it
        /// ended a fall). The air pose also ends at the top of every jump — the vertical speed passes through zero there —
        /// so the way the figure moved in its last airborne frame tells the two apart: up at the top, down before a
        /// landing.</summary>
        private void PoseLanding(float dt, bool airborne, bool free, ref float legL, ref float legR, ref float kneeL, ref float kneeR)
        {
            if (_wasAirborne && !airborne && _wasFalling && free && !_swimming)
            {
                _landTimer = EmoteCurves.LandSeconds;
            }

            _wasAirborne = airborne;
            if (airborne)
            {
                _wasFalling = _vySmooth < 0f; // the smoothed vertical speed the air pose itself keys off (Animate)
            }

            if (_landTimer > 0f)
            {
                _landTimer -= dt;
                float k = EmoteCurves.Envelope(1f - Mathf.Clamp01(_landTimer / EmoteCurves.LandSeconds), 0.15f, 0.6f);
                kneeL += 30f * k;
                kneeR += 30f * k;
                legL -= 16f * k;
                legR -= 16f * k;
            }
        }

        /// <summary>Softer legs: the joints follow their pose instead of jumping to it (walk → tuck → landing → stand).</summary>
        private void EaseLegs(float dt, ref float legL, ref float legR, ref float kneeL, ref float kneeR)
        {
            float ease = _legsEased && dt > 0f ? 1f - Mathf.Exp(-LegEaseRate * dt) : 1f;
            _legLEased = Mathf.Lerp(_legLEased, legL, ease);
            _legREased = Mathf.Lerp(_legREased, legR, ease);
            _kneeLEased = Mathf.Lerp(_kneeLEased, kneeL, ease);
            _kneeREased = Mathf.Lerp(_kneeREased, kneeR, ease);
            _legsEased = true;
            legL = _legLEased;
            legR = _legREased;
            kneeL = _kneeLEased;
            kneeR = _kneeREased;
        }

        /// <summary>The emotes that are mostly arms: point, clap, bow.</summary>
        private void PoseArmEmotes(float dt, ref float armL, ref float armR, ref float elbowL, ref float elbowR,
            ref float kneeL, ref float kneeR, ref float yawL, ref float yawR, ref float nod)
        {
            if (_pointTimer > 0f)
            {
                _pointTimer -= dt;
                float k = EmoteCurves.Envelope(1f - Mathf.Clamp01(_pointTimer / EmoteCurves.PointSeconds));
                armR = Mathf.Lerp(armR, -90f, k); // straight out ahead
                elbowR = Mathf.Lerp(elbowR, 0f, k);
            }

            if (_clapTimer > 0f)
            {
                _clapTimer -= dt;
                float p = 1f - Mathf.Clamp01(_clapTimer / EmoteCurves.ClapSeconds);
                float k = EmoteCurves.Envelope(p, 0.15f, 0.2f);
                float meet = Mathf.Abs(Mathf.Sin(p * Mathf.PI * 6f)); // six claps
                armL = Mathf.Lerp(armL, -58f, k);
                armR = Mathf.Lerp(armR, -58f, k);
                elbowL = Mathf.Lerp(elbowL, -68f, k);
                elbowR = Mathf.Lerp(elbowR, -68f, k);
                yawL = (18f + (14f * meet)) * k;  // the hands swing in to each other
                yawR = -(18f + (14f * meet)) * k;
            }

            if (_bowEmoteTimer > 0f)
            {
                _bowEmoteTimer -= dt;
                float k = EmoteCurves.Envelope(1f - Mathf.Clamp01(_bowEmoteTimer / EmoteCurves.BowSeconds), 0.3f, 0.3f);
                nod += 40f * k; // the figure has no waist: it bows with a deep head and the arms held back
                armL = Mathf.Lerp(armL, 16f, k);
                armR = Mathf.Lerp(armR, 16f, k);
                elbowL = Mathf.Lerp(elbowL, -8f, k);
                elbowR = Mathf.Lerp(elbowR, -8f, k);
                kneeL += 8f * k;
                kneeR += 8f * k;
            }
        }

        /// <summary>The emotes and reactions of the whole body: the head shake, the dance, the flinch.</summary>
        private void PoseBodyEmotes(float dt, ref float armL, ref float armR, ref float elbowL, ref float elbowR,
            ref float legL, ref float legR, ref float kneeL, ref float kneeR, ref float rollL, ref float rollR,
            ref float headYaw, ref float nod)
        {
            if (_shakeTimer > 0f)
            {
                _shakeTimer -= dt;
                float p = 1f - Mathf.Clamp01(_shakeTimer / EmoteCurves.HeadShakeSeconds);
                headYaw += Mathf.Sin(p * Mathf.PI * 5f) * 28f * EmoteCurves.Envelope(p, 0.15f, 0.2f);
            }

            if (_danceTimer > 0f)
            {
                _danceTimer -= dt;
                float p = 1f - Mathf.Clamp01(_danceTimer / EmoteCurves.DanceSeconds);
                float k = EmoteCurves.Envelope(p, 0.12f, 0.15f);
                float beat = Mathf.Sin(p * Mathf.PI * 12f);
                armL = Mathf.Lerp(armL, -115f + (28f * beat), k);
                armR = Mathf.Lerp(armR, -115f - (28f * beat), k);
                elbowL = Mathf.Lerp(elbowL, -42f, k);
                elbowR = Mathf.Lerp(elbowR, -42f, k);
                rollL = -32f * k;
                rollR = 32f * k;
                kneeL += Mathf.Max(0f, beat) * 34f * k;
                kneeR += Mathf.Max(0f, -beat) * 34f * k;
                legL -= Mathf.Max(0f, beat) * 22f * k;
                legR -= Mathf.Max(0f, -beat) * 22f * k;
                headYaw += Mathf.Sin(p * Mathf.PI * 6f) * 14f * k;
            }

            if (_flinchTimer > 0f)
            {
                _flinchTimer -= dt;
                float k = EmoteCurves.Envelope(1f - Mathf.Clamp01(_flinchTimer / EmoteCurves.FlinchSeconds), 0.15f, 0.6f);
                armL -= 28f * k;
                armR -= 28f * k;
                rollL -= 18f * k;
                rollR += 18f * k;
                kneeL += 10f * k;
                kneeR += 10f * k;
                nod -= 14f * k; // the head snaps back
            }
        }

        /// <summary>The stock face blinks and its mouth moves while its owner speaks. A drawn pixel face hides the
        /// stock features (SetFace), and then there is nothing to move.</summary>
        private void PoseFace(float dt)
        {
            if (!_faceFound)
            {
                _faceFound = true;
                if (_head != null)
                {
                    _eyeL = _head.Find("EyeL");
                    _eyeR = _head.Find("EyeR");
                    _pupilL = _head.Find("PupilL");
                    _pupilR = _head.Find("PupilR");
                    _mouth = _head.Find("Mouth");
                    _eyeScale = _eyeL != null ? _eyeL.localScale : Vector3.one;
                    _pupilScale = _pupilL != null ? _pupilL.localScale : Vector3.one;
                    _mouthScale = _mouth != null ? _mouth.localScale : Vector3.one;
                }
            }

            if (_eyeL == null || !_visible)
            {
                return;
            }

            float now = Time.time;
            if (_nextBlink < 0f)
            {
                _nextBlink = now + EmoteCurves.BlinkGap(GetHashCode(), _blinkCount);
            }

            if (now >= _nextBlink && !_lying)
            {
                _blinkUntil = now + EmoteCurves.BlinkSeconds;
                _blinkCount++;
                _nextBlink = now + EmoteCurves.BlinkGap(GetHashCode(), _blinkCount);
            }

            // Eyes shut: asleep, defeated, or in a blink.
            bool shut = _lying || _down || now < _blinkUntil;
            float lid = shut ? 0.12f : 1f;
            SetScaleY(_eyeL, _eyeScale, lid);
            SetScaleY(_eyeR, _eyeScale, lid);
            SetScaleY(_pupilL, _pupilScale, lid);
            SetScaleY(_pupilR, _pupilScale, lid);

            if (_mouth != null)
            {
                float open = Mathf.Max(GaspFactor(), now < _talkUntil ? 1f + (2.6f * EmoteCurves.MouthOpen(now)) : 1f); // #3769: a startle holds it wide
                SetScaleY(_mouth, _mouthScale, open);
            }
        }

        private static void SetScaleY(Transform part, Vector3 rest, float factor)
        {
            if (part == null)
            {
                return;
            }

            float y = rest.y * factor;
            if (!Mathf.Approximately(part.localScale.y, y))
            {
                part.localScale = new Vector3(rest.x, y, rest.z);
            }
        }
    }
}
