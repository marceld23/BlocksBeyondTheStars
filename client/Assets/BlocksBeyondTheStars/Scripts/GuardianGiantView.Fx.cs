// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What <see cref="GuardianGiantView"/>'s moves show and sound like, timed on the move's clock: the warnings (a red ring
    /// at the aim point of a jump or a throw, a ring around the feet while the stomp's foot is up), the impacts at
    /// <see cref="GuardianGiantMoveSpec.Impact"/> (dust, a shock ring, cracks, a camera shake that falls off with distance and
    /// stays off under reduced motion), the rock in the hand and in the air, the visor beam, the footfalls, and the cues
    /// (<c>giant_wake</c>, <c>giant_rise</c>, <c>giant_step</c>, <c>giant_stomp</c>, <c>giant_laser_charge</c>,
    /// <c>giant_laser_beam</c>, <c>giant_throw</c>, <c>giant_rock_impact</c>, <c>giant_summon</c>, <c>giant_collapse</c>; a
    /// missing clip is simply not played). Kid-readable on purpose: warnings are rings and glows, impacts are dust.
    /// </summary>
    public sealed partial class GuardianGiantView
    {
        private static readonly Color WarnRed = new Color(1f, 0.22f, 0.15f);
        private static readonly Color LaserRed = new Color(1f, 0.25f, 0.16f);
        private static readonly Color Dust = new Color(0.62f, 0.56f, 0.48f);

        private int _serial = -1;
        private float _t;
        private float _prevT;
        private float _nextSpark;
        private GameObject _rock;
        private Vector3 _rockFrom;
        private bool _rockFlying;
        private MeshRenderer _aimRing;
        private MeshRenderer _footRing;
        private bool _aimRingWanted;
        private bool _footRingWanted;
        private WeaponFx _weapons;
        private PlayerController _player;

        private WeaponFx Weapons => _weapons != null ? _weapons : (_weapons = Object.FindAnyObjectByType<WeaponFx>());

        private PlayerController Player => _player != null ? _player : (_player = Object.FindAnyObjectByType<PlayerController>());

        /// <summary>The move's events of this frame. A move heard of late (more than a second in, a player arriving mid-fight)
        /// skips what already happened; a move heard of in its first second still plays its start.</summary>
        private void Events(GuardianGiantTrack t, float now)
        {
            _t = t.Elapsed(now);
            if (t.MoveSerial != _serial)
            {
                _serial = t.MoveSerial;
                _prevT = _t < 1f ? -0.001f : _t;
                _jumpFromSet = false; // a second leap starts from where the giant stands now
                DropRock();
            }

            _aimRingWanted = _footRingWanted = false;
            var s = GuardianGiantRules.Spec(t.Move);
            switch (t.Move)
            {
                case GuardianGiantMove.Rise:
                case GuardianGiantMove.LieDown:
                case GuardianGiantMove.Collapse:
                case GuardianGiantMove.Enrage:
                case GuardianGiantMove.Summon:
                case GuardianGiantMove.Hop:
                    BodyEvents(t.Move, s);
                    break;
                case GuardianGiantMove.Stomp:
                    StompEvents(s);
                    break;
                case GuardianGiantMove.JumpAttack:
                    JumpEvents(t, s);
                    break;
                case GuardianGiantMove.Laser:
                    LaserEvents(t, s);
                    break;
                case GuardianGiantMove.Throw:
                    ThrowEvents(t, s);
                    break;
            }

            ShowRing(ref _aimRing, _aimRingWanted);
            ShowRing(ref _footRing, _footRingWanted);
            _prevT = _t;
        }

        private bool Crossed(float at) => _prevT < at && _t >= at;

        /// <summary>The moves that are the body itself: waking, lying down, falling, the rage, the summons, the hop.</summary>
        private void BodyEvents(GuardianGiantMove move, GuardianGiantMoveSpec s)
        {
            var chest = _rig.Bone(GuardianGiantPart.Torso).TransformPoint(GuardianGiantModel.ChestLocal);
            switch (move)
            {
                case GuardianGiantMove.Rise when Crossed(0f):
                    Cue("giant_wake", chest);
                    FxLights.Flash(chest, WarnRed, 2.5f, 14f, 0.6f);
                    break;
                case GuardianGiantMove.Rise when Crossed(0.6f):
                    Cue("giant_rise", chest);
                    break;
                case GuardianGiantMove.Rise when Crossed(s.Duration - 0.15f):
                    Footfall(GuardianGiantPart.LegLeft, 0.6f);
                    Footfall(GuardianGiantPart.LegRight, 0.6f);
                    break;
                case GuardianGiantMove.LieDown when Crossed(0f):
                    Cue("giant_rise", chest, 0.85f);
                    break;
                case GuardianGiantMove.LieDown when Crossed(s.Duration - 0.25f):
                case GuardianGiantMove.Collapse when Crossed(s.Impact):
                    BodyLands(move == GuardianGiantMove.Collapse);
                    break;
                case GuardianGiantMove.Collapse when Crossed(0f):
                    Cue("giant_collapse", chest);
                    break;
                case GuardianGiantMove.Enrage when Crossed(0f):
                    Cue("giant_wake", chest, 1.15f);
                    FxKit.Shell(chest, WarnRed, 1f, 9f, 0.8f);
                    break;
                case GuardianGiantMove.Summon when Crossed(0f):
                    Cue("giant_summon", chest);
                    break;
                case GuardianGiantMove.Summon when Crossed(s.Impact):
                    FxKit.Shell(chest, WarnRed, 1.5f, 12f, 0.9f);
                    FxLights.Flash(chest, WarnRed, 3f, 18f, 0.5f);
                    break;
                case GuardianGiantMove.Hop when Crossed(s.Impact):
                    Footfall(GuardianGiantPart.LegLeft, 1f);
                    Footfall(GuardianGiantPart.LegRight, 1f);
                    break;
            }
        }

        /// <summary>The stomp: a red ring of <see cref="GuardianGiantRules.StompRadius"/> around the feet while the foot is up,
        /// the shock wave at the impact.</summary>
        private void StompEvents(GuardianGiantMoveSpec s)
        {
            var feet = _rig.Root.transform.position;
            if (_t < s.Impact)
            {
                _footRingWanted = true;
                PlaceRing(FootRing(), feet, GuardianGiantRules.StompRadius);
            }

            if (Crossed(s.Impact))
            {
                Impact(feet, GuardianGiantRules.StompRadius, "giant_stomp");
            }
        }

        /// <summary>The jump attack: the landing shadow at the aim until it lands, the push-off at the take-off, the landing's
        /// shock wave at the impact.</summary>
        private void JumpEvents(GuardianGiantTrack t, GuardianGiantMoveSpec s)
        {
            var aim = AimScene(t.Status);
            if (_t < s.Impact)
            {
                _aimRingWanted = true;
                PlaceRing(AimRing(), aim, GuardianGiantRules.JumpRadius);
            }

            if (Crossed(GuardianGiantRules.JumpTakeOff))
            {
                var feet = _rig.Root.transform.position;
                Cue("giant_step", feet, 0.8f);
                Weapons?.Dust(feet + (Vector3.up * 0.3f), 14, Dust);
            }

            if (Crossed(s.Impact))
            {
                Impact(aim, GuardianGiantRules.JumpRadius, "giant_stomp");
            }
        }

        /// <summary>The laser: the charge while the visor glows, the beam from the visor to the aim at the impact (it follows the
        /// visor while it burns), sparks where it ends.</summary>
        private void LaserEvents(GuardianGiantTrack t, GuardianGiantMoveSpec s)
        {
            var head = _rig.Bone(GuardianGiantPart.Head);
            if (Crossed(0f))
            {
                Cue("giant_laser_charge", head.position);
            }

            var aim = AimScene(t.Status);
            if (Crossed(s.Impact))
            {
                var visor = head.TransformPoint(GuardianGiantModel.VisorLocal);
                Cue("giant_laser_beam", visor);
                var mat = FxKit.BeamMaterial("giant_laser", LaserRed, 0.35f, 1.4f, 10f, 0.25f, 3f);
                FxKit.Beam(visor, aim, LaserRed, 1.1f, GuardianGiantRules.LaserBeamSeconds, mat,
                    () => head != null ? head.TransformPoint(GuardianGiantModel.VisorLocal) : visor);
                FxLights.Flash(aim, LaserRed, 2f, 8f, 0.4f);
            }

            if (_t >= s.Impact && _t < s.Impact + GuardianGiantRules.LaserBeamSeconds && Time.time >= _nextSpark)
            {
                _nextSpark = Time.time + 0.12f;
                Weapons?.Sparks(aim, LaserRed, 6);
            }
        }

        /// <summary>The throw: the target ring until the rock lands; the rock torn from the ground into the hand, hurled at the
        /// release along an arc, its impact at the aim.</summary>
        private void ThrowEvents(GuardianGiantTrack t, GuardianGiantMoveSpec s)
        {
            var aim = AimScene(t.Status);
            if (_t < s.Impact)
            {
                _aimRingWanted = true;
                PlaceRing(AimRing(), aim, GuardianGiantRules.ThrowRadius);
            }

            var hand = _rig.Bone(GuardianGiantPart.ArmRight);
            if (Crossed(GuardianGiantMotion.RockGrab) && _rock == null)
            {
                _rock = GuardianGiantModel.BuildRock(_game.Content, _game.Atlas, _game.ChunkMaterial, hand);
                if (_rock != null)
                {
                    _rock.transform.localPosition = GuardianGiantModel.RightHandLocal;
                    Weapons?.Dust(_rock.transform.position, 12, Dust);
                }
            }

            if (Crossed(GuardianGiantRules.ThrowRelease) && _rock != null)
            {
                _rockFrom = _rock.transform.position;
                _rock.transform.SetParent(_parent, true);
                _rockFlying = true;
                Cue("giant_throw", _rockFrom);
            }

            if (_rockFlying && _rock != null)
            {
                float u = (_t - GuardianGiantRules.ThrowRelease) / Mathf.Max(0.05f, s.Impact - GuardianGiantRules.ThrowRelease);
                float peak = GuardianGiantPose.ArcPeak(Vector3.Distance(_rockFrom, aim), 3f);
                _rock.transform.position = ToUnity(GuardianGiantPose.Arc(ToShared(_rockFrom), ToShared(aim), u, peak));
                _rock.transform.rotation = Quaternion.Euler(_t * 220f, _t * 140f, 0f);
            }

            if (Crossed(s.Impact))
            {
                DropRock();
                Impact(aim, GuardianGiantRules.ThrowRadius, "giant_rock_impact");
            }
        }

        /// <summary>A shock wave lands at <paramref name="at"/>: its sound, dust around it, a ring running out to its reach,
        /// cracks in the ground and the camera shake.</summary>
        private void Impact(Vector3 at, float radius, string clip)
        {
            Cue(clip, at);
            for (int i = 0; i < 5; i++)
            {
                var off = Random.insideUnitCircle * Mathf.Max(1f, radius * 0.6f);
                Weapons?.Dust(at + new Vector3(off.x, 0.3f, off.y), 8, Dust);
            }

            FxKit.Ring(at + (Vector3.up * 0.15f), Vector3.up, new Color(1f, 0.82f, 0.6f), 0.5f, radius, 0.7f, 0.1f, 0.05f);
            FxDecals.Crater(at, radius * 0.5f, FxDecals.CraterKind.Quake);
            Shake(at, radius, 1f);
        }

        /// <summary>The body hits the ground (the end of a lie-down, the collapse's landing): dust along it, a deep thud, a
        /// shake — and the collapse puts its lights out for good.</summary>
        private void BodyLands(bool collapse)
        {
            var torso = _rig.Bone(GuardianGiantPart.Torso).position;
            var feet = _rig.Root.transform.position;
            for (int i = 0; i <= 4; i++)
            {
                Weapons?.Dust(Vector3.Lerp(feet, torso, i / 4f) + (Vector3.up * 0.3f), 9, Dust);
            }

            Cue("giant_stomp", torso, collapse ? 0.6f : 0.75f);
            Shake(torso, collapse ? 8f : 5f, collapse ? 1f : 0.5f);
            if (collapse)
            {
                _dead = true;
            }
        }

        /// <summary>The walk's footfalls between two readings of the gait phase: dust and <c>giant_step</c> at each planted foot.</summary>
        private void Footfalls(GuardianGiantTrack t, float prevWalk, float moving)
        {
            if (t.Move != GuardianGiantMove.Walk || moving < 0.25f)
            {
                return;
            }

            int mask = GuardianGiantPose.Footfalls(prevWalk, _walk);
            if ((mask & 1) != 0)
            {
                Footfall(GuardianGiantPart.LegLeft, 0.5f);
            }

            if ((mask & 2) != 0)
            {
                Footfall(GuardianGiantPart.LegRight, 0.5f);
            }
        }

        private void Footfall(GuardianGiantPart leg, float strength)
        {
            var foot = _rig.Bone(leg).TransformPoint(new Vector3(0f, 0.2f - GuardianGiantPose.LegLength, 0.5f));
            Weapons?.Dust(foot, 10, Dust);
            Cue("giant_step", foot, Random.Range(0.9f, 1.05f), 0.7f + (0.3f * strength));
            Shake(foot, 3f, 0.5f * strength);
        }

        /// <summary>The camera shake of an impact at <paramref name="at"/> (<see cref="GuardianGiantPose.Shake"/>: full at the
        /// centre, none at twice the radius) — never under reduced motion; the camera-motion and shake settings apply on top.</summary>
        private void Shake(Vector3 at, float radius, float scale)
        {
            if (UiKit.ReducedMotion)
            {
                return;
            }

            float s = GuardianGiantPose.Shake(Vector3.Distance(at, _game.PlayerPosition), radius) * scale;
            if (s > 0.01f)
            {
                Player?.AddCameraShake(s);
            }
        }

        private static void Cue(string clip, Vector3 at, float pitch = 1f, float volume = 1f)
            => ClientAudio.Instance?.At(clip, at, pitch, volume);

        /// <summary>A warning ring: a flat red circle with a faint fill on the ground (the FxRing shader), hidden until placed.</summary>
        private MeshRenderer NewRing(string name)
        {
            var mat = FxKit.Cached("BlocksBeyondTheStars/FxRing", "ring");
            if (mat == null)
            {
                return null;
            }

            var go = new GameObject(name);
            go.transform.SetParent(_parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = FxKit.QuadMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);
            return mr;
        }

        /// <summary>The ring around the feet (the stomp), made on first use. Unity's null: a destroyed ring is made again.</summary>
        private MeshRenderer FootRing()
        {
            if (_footRing == null)
            {
                _footRing = NewRing("GiantStompRing");
            }

            return _footRing;
        }

        /// <summary>The ring at the aim point (the jump's landing shadow, the rock's target), made on first use.</summary>
        private MeshRenderer AimRing()
        {
            if (_aimRing == null)
            {
                _aimRing = NewRing("GiantAimRing");
            }

            return _aimRing;
        }

        /// <summary>Lays a ring of <paramref name="radius"/> flat on the ground at <paramref name="at"/>, pulsing gently.</summary>
        private static void PlaceRing(MeshRenderer ring, Vector3 at, float radius)
        {
            if (ring == null)
            {
                return;
            }

            var tr = ring.transform;
            tr.position = at + (Vector3.up * 0.08f);
            tr.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            tr.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            float pulse = UiKit.ReducedMotion ? 0.85f : 0.7f + (0.3f * Mathf.Sin(Time.time * 7f));
            var lin = FxKit.Lin(WarnRed);
            var b = FxKit.Block;
            b.Clear();
            b.SetColor("_Color", new Color(lin.r, lin.g, lin.b, pulse));
            b.SetFloat("_Thickness", 0.07f);
            b.SetFloat("_Fill", 0.22f);
            b.SetFloat("_Lines", 0f);
            b.SetFloat("_Intensity", 1.6f);
            ring.SetPropertyBlock(b);
        }

        private static void ShowRing(ref MeshRenderer ring, bool on)
        {
            if (ring != null && ring.gameObject.activeSelf != on)
            {
                ring.gameObject.SetActive(on);
            }
        }

        private void HideWarnings()
        {
            ShowRing(ref _aimRing, false);
            ShowRing(ref _footRing, false);
            DropRock();
        }

        private void DropRock()
        {
            if (_rock != null)
            {
                Object.Destroy(_rock);
            }

            _rock = null;
            _rockFlying = false;
        }

        private void DisposeFx()
        {
            DropRock();
            if (_aimRing != null)
            {
                Object.Destroy(_aimRing.gameObject);
            }

            if (_footRing != null)
            {
                Object.Destroy(_footRing.gameObject);
            }
        }
    }
}
