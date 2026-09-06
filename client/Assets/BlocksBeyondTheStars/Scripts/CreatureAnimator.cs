// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Procedural creature animation. The legs run a real gait (<see cref="CreatureGait"/>): the cycle rate
    /// follows from speed ÷ stride length, so a planted foot stays put on the ground instead of sliding — the
    /// old sine wave beat at <c>3 + speed·2.2</c>, unrelated to the distance the body covered, and every
    /// animal in the game skated. On top of that: wings flap (only in the air since #1333 — a perched bird
    /// folds them), the tail sways, the legs tuck mid-jump and the body squashes on landing, crawlers and
    /// swimmers undulate. Self-driven from the root's world movement — the same approach as
    /// <see cref="PlayerAvatar"/> — plus the motion class + airborne/perched flags the server streams
    /// (<see cref="SetMotion"/>). The rig comes from <see cref="CreatureBuilder"/> as a
    /// <see cref="RigDescription"/>.
    /// </summary>
    public sealed class CreatureAnimator : MonoBehaviour
    {
        private enum Idle { Breathe, Graze, Alert, Lunge }

        // --- tuning ---
        private const float IdleAmpDeg = 12f;      // stride amplitude at a crawl …
        private const float WalkAmpDeg = 30f;      // … and at full speed. Deliberately a narrow band: the
                                                   // cycle RATE carries the speed, the stride length only
                                                   // stretches ~2× so the frequency range stays believable.
        private const float UndulateAmpScale = 0.45f; // swimmers/crawlers barely stride — fins and body do the work
        private const float GaitFadeDuration = 0.25f; // a gait switch re-phases every leg; fade it or they pop
        private const float SquashDuration = 0.18f;
        private const float KneeStandBend = 8f;       // a standing leg is not ruler-straight
        private const float WingFoldYaw = 140f;       // how far the wrist folds the outer panel back along the body

        private RigDescription _rig;
        private LegRig[] _legs = System.Array.Empty<LegRig>();
        private WingRig[] _wings = System.Array.Empty<WingRig>();
        private Transform[] _tail = System.Array.Empty<Transform>();
        private Transform[] _neck = System.Array.Empty<Transform>();
        private Transform[] _trunk = System.Array.Empty<Transform>();
        private Transform[] _fins = System.Array.Empty<Transform>();
        private Transform[][] _tentacles = System.Array.Empty<Transform[]>();
        private Transform _head;
        private Transform _jaw;
        private Quaternion _jawRest = Quaternion.identity;
        private Transform[] _eyelids = System.Array.Empty<Transform>();
        private Transform[] _ears = System.Array.Empty<Transform>();
        private Quaternion[] _earRest = System.Array.Empty<Quaternion>();
        private Transform _body;   // the body rig — undulated for swimming / crawling, squashed on landing
        private Transform _bell;   // medusa plan (#637): pulses; the rim arms trail it
        private Vector3 _bellBaseScale = Vector3.one;
        private float _bellPulse;  // this frame's contraction, so the arms can trail it
        private float _finFold;    // 0 = fins spread and rowing, 1 = folded flat (an amphibian ashore)
        private bool _aquatic;     // swimmers undulate + flutter instead of striding

        // --- jaw (the voice finally moves a mouth) ---
        private float _jawT = 999f;   // time into the current open (large = closed)
        private float _jawDur;
        private float _jawDeg;

        // --- blink ---
        private float _blinkTimer;
        private float _blinkT = 999f;
        private int _blinksLeft;
        private const float BlinkDuration = 0.11f;

        // --- gaze ---
        private Vector3 _gazeTarget;
        private bool _gazeValid;
        private float _gazeHold;    // > 0 while actively looking at the target
        private float _gazeTimer;   // counts down to the next look
        private float _gazeYaw;     // smoothed

        // --- rest pose (a sleeping animal lies down instead of dozing on its feet) ---
        private float _rest;
        private float _restSide;    // which way the head tucks — stable per creature

        // --- long-idle flourishes ---
        private enum Flourish { None, TailSwat, EarFlick, WeightShift }
        private Flourish _flourish = Flourish.None;
        private float _flourishT;
        private float _flourishDur;
        private float _flourishTimer;
        private float _idleTime;

        // --- foot planting (near LOD only) ---
        private readonly CreatureFeet _feet = new CreatureFeet();
        private float _cycleRate;
        private float _stride = 1f;
        private Vector3 _rootVel;

        /// <summary>True while the feet are tilting the body from the ground they are standing on, so
        /// <see cref="CreatureView"/> stops applying its own velocity-derived slope pitch on top.</summary>
        public bool FootPitchActive => _feet.Active;

        // --- level of detail ---
        private CreatureLod _lod = CreatureLod.Near;
        private float _lodAccum;   // dt banked while skipping frames at the Far tier
        private int _lodSkip;

        /// <summary>The detail tier this rig is currently animating at.</summary>
        public CreatureLod Lod => _lod;

        /// <summary>Face detail (blink, gaze, jaw, flourishes) is only worth posing where it can be seen.</summary>
        private bool FaceDetail => _lod == CreatureLod.Near || _lod == CreatureLod.Mid;

        /// <summary>Walk-cadence multiplier: a small extra drag on the beat for bodies whose stride length
        /// alone does not carry the scale read. Default 1 = the geometry decides.</summary>
        public float CadenceScale = 1f;

        // Motion class + vertical state from the server (#1333). Defaults read as a grounded walker, which is
        // what a legacy server (no fields) sends.
        private string _motion = "walker";
        private MotionClass _motionClass = MotionClass.Walker;
        private bool _airborne;
        private bool _perched;
        private bool _gliding;   // a ground bird mid-bound holds its wings spread instead of beating them
        private bool _prevAirborne;
        private float _squashT = 999f;   // time into the landing squash (large = none)
        private float _legTuck;          // 0..1 smoothed leg tuck while airborne
        private float _wingFold;         // 0..1 smoothed fold while perched / grounded flier

        // Gait state.
        private Gait _gait = Gait.Walk;
        private Gait _prevGait = Gait.Walk;
        private float _gaitFade = 1f;    // 1 = fully on _gait
        private float _walk;             // gait phase, in CYCLES (not radians)

        private float _phase;     // per-creature offset so they don't move in lockstep
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

        /// <summary>Takes the rig over from the builder. Rest poses are already captured on each part, so
        /// every effect below poses additively rather than overwriting a rotation.</summary>
        public void Init(RigDescription rig)
        {
            _rig = rig ?? new RigDescription();
            _legs = _rig.Legs ?? System.Array.Empty<LegRig>();
            _wings = _rig.Wings ?? System.Array.Empty<WingRig>();
            _tail = _rig.Tail ?? System.Array.Empty<Transform>();
            _neck = _rig.Neck ?? System.Array.Empty<Transform>();
            _trunk = _rig.Trunk ?? System.Array.Empty<Transform>();
            _fins = _rig.Fins ?? System.Array.Empty<Transform>();
            _tentacles = _rig.Tentacles ?? System.Array.Empty<Transform[]>();
            _head = _rig.Head;
            _jaw = _rig.Jaw;
            _jawRest = _jaw != null ? _jaw.localRotation : Quaternion.identity;
            _eyelids = _rig.Eyelids ?? System.Array.Empty<Transform>();
            _ears = _rig.Ears ?? System.Array.Empty<Transform>();
            _earRest = new Quaternion[_ears.Length];
            for (int i = 0; i < _ears.Length; i++)
            {
                _earRest[i] = _ears[i] != null ? _ears[i].localRotation : Quaternion.identity;
            }

            _body = _rig.Body;
            _bell = _rig.Bell;
            _bellBaseScale = _bell != null ? _bell.localScale : Vector3.one;
            _hostile = _rig.Hostile;
            _asleep = _rig.Asleep;
            _aquatic = _rig.Aquatic;
            _phase = (GetEntityId().GetHashCode() & 0x3ff) * 0.1f; // stable pseudo-random offset
            _walk = (_rig.IdHash & 0xff) / 255f;                   // and a stable gait phase, so a herd is not in lockstep
            _restSide = (_rig.IdHash & 0x100) == 0 ? -1f : 1f;
            _rest = _asleep ? 1f : 0f;                             // spawned asleep → already lying down
            _blinkTimer = Random.Range(1.5f, 5f);
            _gazeTimer = Random.Range(2f, 6f);
            _flourishTimer = Random.Range(4f, 9f);
            _feet.Init(_legs, _rig.Ground, _rig.LegLength);

            // Map the species temperament to its resting idle gesture.
            string t = (_rig.Temperament ?? string.Empty).ToLowerInvariant();
            _idleKind = _hostile || t.Contains("aggress") || t.Contains("hostile") ? Idle.Lunge
                : t.Contains("skittish") || t.Contains("timid") || t.Contains("wary") || t.Contains("flighty") ? Idle.Alert
                : t.Contains("passive") || t.Contains("docile") || t.Contains("calm") || t.Contains("placid") ? Idle.Graze
                : Idle.Breathe;
            _gestureTimer = Random.Range(1.5f, 4f);
        }

        /// <summary>Feeds the streamed motion class ("walker" | "crawler" | "flier" | "hoverer" | "swimmer"),
        /// the vertical flags (#1333) and the live sleep state each frame. A landing (airborne → grounded)
        /// starts the squash; sleep is fed here rather than baked at build time because a creature dozes off
        /// and wakes while its rig lives, and it should lie down and get up when it does.</summary>
        public void SetMotion(string motion, bool airborne, bool perched, bool asleep = false, bool gliding = false)
        {
            _gliding = gliding;
            _motion = string.IsNullOrEmpty(motion) ? "walker" : motion;
            _motionClass = CreatureMotion.Parse(_motion);
            _asleep = asleep;
            if (_prevAirborne && !airborne && (_motionClass == MotionClass.Walker || _motionClass == MotionClass.Crawler))
            {
                _squashT = 0f; // just landed
            }

            _prevAirborne = airborne;
            _airborne = airborne;
            _perched = perched;
        }

        /// <summary>Opens the jaw for one vocalisation pulse. <see cref="CreatureView"/> already knows exactly
        /// when a phrase pulse fires (#902) — this is what turns that into a moving mouth.</summary>
        public void Pulse(float strength)
        {
            if (_jaw == null)
            {
                return;
            }

            float deg = Mathf.Lerp(20f, 38f, Mathf.Clamp01(strength));
            if (_jawT < _jawDur && _jawDeg > deg)
            {
                return; // don't cut a bigger open short with a smaller one
            }

            _jawDeg = deg;
            _jawDur = 0.17f;
            _jawT = 0f;
        }

        /// <summary>A hard snap of the jaw for an attack.</summary>
        public void Bite()
        {
            if (_jaw == null)
            {
                return;
            }

            _jawDeg = 55f;
            _jawDur = 0.22f;
            _jawT = 0f;
        }

        /// <summary>Where the player is, so an idle animal can look up at them. Passing
        /// <paramref name="valid"/> false (out of range, or the rig is beyond the near LOD) releases the gaze.</summary>
        public void SetGazeTarget(Vector3 worldPos, bool valid)
        {
            _gazeTarget = worldPos;
            _gazeValid = valid;
        }

        /// <summary>Sets how much of the rig to animate at this distance (<see cref="CreatureView"/> decides).
        /// Frozen switches the component off entirely — the body keeps moving, it just stops posing itself —
        /// and coming back re-syncs the speed estimate so a creature does not sprint on its first frame.</summary>
        public void SetLod(CreatureLod lod)
        {
            if (_lod == lod)
            {
                return;
            }

            _lod = lod;
            _lodAccum = 0f;
            _lodSkip = 0;
            _feet.Reset(); // the body moved while we were coarse; re-plant rather than drag the old targets
            if (lod == CreatureLod.Frozen)
            {
                enabled = false;
            }
            else if (!enabled)
            {
                enabled = true;
                _hasPrev = false; // the root moved while we were off; do not read that as speed
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            // Far away, pose every third frame with the banked time. The gait is a function of phase, and the
            // phase advances by the same amount either way, so the walk stays in step — it just updates
            // coarsely, which at that distance is a few pixels.
            if (_lod == CreatureLod.Far)
            {
                _lodAccum += dt;
                if (++_lodSkip < 3)
                {
                    return;
                }

                _lodSkip = 0;
                dt = _lodAccum;
                _lodAccum = 0f;
            }

            var pos = transform.position;
            float speed = 0f;
            _rootVel = Vector3.zero;
            if (_hasPrev)
            {
                var d = pos - _lastPos;
                _rootVel = d / dt;
                d.y = 0f;
                speed = d.magnitude / dt;
            }

            _lastPos = pos;
            _hasPrev = true;

            bool crawler = _motionClass == MotionClass.Crawler;
            bool flier = _motionClass == MotionClass.Flier;
            bool hoverer = _motionClass == MotionClass.Hoverer;
            bool undulates = _aquatic || crawler; // swimmers and land crawlers both move by the body, not the legs

            float moving = Mathf.Clamp01(speed / 3f);
            float t = Time.time + _phase;

            // Lying down / getting up. Ground movers only — a sleeping flier is on a perch and a hoverer
            // never touches down, so neither has anywhere to lie.
            bool canRest = _motionClass == MotionClass.Walker || _motionClass == MotionClass.Crawler;
            float restTarget = _asleep && canRest && !_airborne ? 1f : 0f;
            _rest = Mathf.MoveTowards(_rest, restTarget, dt / (restTarget > _rest ? 1.2f : 0.6f));

            // Long-idle flourishes: only once an animal has genuinely settled, and only close enough to see.
            _idleTime = moving < 0.05f && _rest < 0.2f ? _idleTime + dt : 0f;
            if (FaceDetail)
            {
                StepFlourish(dt);
            }
            else
            {
                _flourish = Flourish.None;
            }

            float gaitAmp = StepGait(dt, speed, moving, undulates);

            // Foot planting, near LOD only and only for bodies that actually stand on the ground. Runs before
            // the body is posed, because the body's tilt comes out of where the feet ended up.
            bool groundBound = _motionClass == MotionClass.Walker || _motionClass == MotionClass.Crawler;
            bool wantFeet = _lod == CreatureLod.Near && groundBound && !_airborne && _rest < 0.05f
                && _legs.Length > 0 && _legs[0]?.Knee != null;
            _feet.Plan(transform, dt, _gait, _walk, _cycleRate, _stride, _rootVel, wantFeet);

            PoseBell(t);
            PoseTentacles(t, moving);
            PoseFins(t, moving);
            // The body is posed before the legs: the hips move with it, and the IK below solves against where
            // they actually end up this frame rather than where they were last frame.
            PoseBody(dt, t, moving, undulates);
            PoseLegs(dt, gaitAmp, moving, crawler, t);
            PoseWings(dt, t, moving, flier, hoverer);
            PoseTail(t, moving, undulates);
            float neckShare = PoseHead(dt, t, moving);
            PoseNeck(t, neckShare);
            if (FaceDetail)
            {
                PoseJaw(dt, t);
                PoseEyelids(dt);
                PoseEars(dt, t);
                PoseTrunk(t, neckShare);
            }
        }

        /// <summary>Advances the gait phase. The rate is <c>speed ÷ stride length</c> — that division is the
        /// whole anti-skate mechanism — and a gait change is held off until the speed is clear of the
        /// transition band, then cross-faded so the re-phased legs do not pop.</summary>
        private float StepGait(float dt, float speed, float moving, bool undulates)
        {
            float ampScale = undulates ? UndulateAmpScale : 1f;
            float amp = Mathf.Lerp(IdleAmpDeg, WalkAmpDeg, moving) * ampScale;
            float speedNorm = Mathf.Clamp01(speed / Mathf.Max(1f, 2f + _rig.Size));

            var want = CreatureGait.Select(_motionClass, _rig.LegCount, _rig.Giant, speedNorm);
            if (want != _gait
                && CreatureGait.Select(_motionClass, _rig.LegCount, _rig.Giant, Mathf.Clamp01(speedNorm - CreatureGait.TransitionHysteresis)) == want
                && CreatureGait.Select(_motionClass, _rig.LegCount, _rig.Giant, Mathf.Clamp01(speedNorm + CreatureGait.TransitionHysteresis)) == want)
            {
                _prevGait = _gait;
                _gait = want;
                _gaitFade = 0f;
            }

            _gaitFade = Mathf.Min(1f, _gaitFade + dt / GaitFadeDuration);

            _stride = CreatureGait.StrideLength(_rig.LegLength, amp);
            _cycleRate = CreatureGait.CycleRate(speed, _stride, CadenceScale);
            _walk += dt * _cycleRate;
            if (_walk > 1f)
            {
                _walk -= Mathf.Floor(_walk); // keep the accumulator in [0,1) — it runs for the whole session
            }

            return amp;
        }

        /// <summary>Legs: each one runs the shared cycle at its own offset. Standing still, the gait is faded
        /// out to a barely-there idle sway (the cycle rate is zero by then, so the pose would otherwise
        /// freeze mid-stride). Airborne the legs tuck back under the body.</summary>
        private void PoseLegs(float dt, float amp, float moving, bool crawler, float t)
        {
            bool groundAirborne = _airborne && (_motionClass == MotionClass.Walker || crawler);
            _legTuck = Mathf.Lerp(_legTuck, groundAirborne ? 1f : 0f, 1f - Mathf.Exp(-12f * dt));
            if (_legs.Length == 0)
            {
                return;
            }

            // How much of the gait to show: none while standing (the phase is frozen), all once walking.
            float gaitWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.04f, 0.3f, moving)) * (1f - _legTuck);
            float dutyNow = CreatureGait.DutyFactor(_gait);
            float dutyPrev = CreatureGait.DutyFactor(_prevGait);
            bool fading = _gaitFade < 1f;
            float splay = crawler && _legs.Length >= 6 ? 22f : 0f; // many-legged bodies stand wide, not underneath

            for (int i = 0; i < _legs.Length; i++)
            {
                var leg = _legs[i];
                if (leg?.Hip == null)
                {
                    continue;
                }

                float phase = _walk + CreatureGait.PhaseOffset(_gait, leg.Side, leg.Row, leg.Rows);
                var pose = CreatureGait.Evaluate(phase, dutyNow, amp);
                float swing = pose.SwingDeg;
                float lift = pose.Lift01;
                if (fading)
                {
                    float pPrev = _walk + CreatureGait.PhaseOffset(_prevGait, leg.Side, leg.Row, leg.Rows);
                    var posePrev = CreatureGait.Evaluate(pPrev, dutyPrev, amp);
                    swing = Mathf.Lerp(posePrev.SwingDeg, swing, _gaitFade);
                    lift = Mathf.Lerp(posePrev.Lift01, lift, _gaitFade);
                }

                // Idle sway: a slow, tiny shuffle so a standing animal is not a statue.
                float idle = Mathf.Sin(t * 1.1f + i * 0.7f) * 1.5f;
                float pitch = Mathf.Lerp(idle, swing, gaitWeight) - 35f * _legTuck;

                // Lying down: the legs tuck under the belly — the front pair folds back, the rear pair
                // forward — so the body can settle onto the ground instead of dozing bolt upright.
                if (_rest > 0f)
                {
                    float tuckDir = leg.Rows <= 1 ? 1f : leg.Row == 0 ? -1f : 1f;
                    pitch = Mathf.Lerp(pitch, tuckDir * 70f, _rest);
                }

                float side = leg.Side == 0 ? 1f : -1f;

                // With foot planting on, the leg is solved backwards from where the foot actually is —
                // a real spot on a real block, which is the only way legs can follow a slope or a ledge.
                if (_feet.Active && leg.Knee != null && _feet.HasTarget(i))
                {
                    var target = _feet.LocalTarget(i);
                    var s = CreatureIk.SolveTwoBone(target.x, target.y, target.z,
                        leg.UpperLen, leg.LowerLen, leg.KneeSign);
                    leg.Hip.localRotation = leg.HipRest * Quaternion.Euler(s.HipPitchDeg, s.HipYawDeg, side * splay);
                    leg.Knee.localRotation = Quaternion.Euler(s.KneeDeg, 0f, 0f);
                    if (leg.Foot != null)
                    {
                        leg.Foot.localRotation = Quaternion.Euler(-(s.HipPitchDeg + s.KneeDeg), 0f, 0f);
                    }

                    continue;
                }

                leg.Hip.localRotation = leg.HipRest
                    * Quaternion.Euler(pitch, 0f, side * splay);

                if (leg.Knee == null)
                {
                    // Jointless fallback: the leg cannot shorten, so lift the hip enough that the foot clears
                    // the ground instead of sweeping through it.
                    leg.Hip.localPosition = leg.HipRestPos + new Vector3(0f, lift * gaitWeight * leg.Length * 0.12f, 0f);
                    continue;
                }

                // The knee. Straight (bar a slight standing bend) through the stance so the leg carries
                // weight at full extension, folding through the swing so the foot clears the ground — which
                // is the thing a single rigid stick fundamentally cannot do.
                float fold = KneeStandBend + pose.Fold01 * Mathf.Lerp(18f, 52f, moving) * gaitWeight;
                fold += 70f * _legTuck;                                    // tucked up mid-jump
                if (_rest > 0f)
                {
                    fold = Mathf.Lerp(fold, 105f, _rest);                  // folded right up while lying down
                }

                float knee = leg.KneeSign * fold;
                leg.Knee.localRotation = Quaternion.Euler(knee, 0f, 0f);

                // Keep the sole flat: cancel everything the hip and knee did, so the foot meets the ground
                // instead of pointing at it.
                if (leg.Foot != null)
                {
                    leg.Foot.localRotation = Quaternion.Euler(-(pitch + knee), 0f, 0f);
                }
            }
        }

        /// <summary>Wings (#1333): beat only in the air — a hard flap when flying, a calm idle beat for a
        /// hoverer's vanes; on the ground (perched flier, a ground bird between bounds) they fold up over the
        /// back.</summary>
        private void PoseWings(float dt, float t, float moving, bool flier, bool hoverer)
        {
            bool wingsBeat = flier ? _airborne
                : hoverer || (_airborne && _motionClass == MotionClass.Walker) || _motionClass == MotionClass.Swimmer;
            bool gliding = _gliding && _airborne;
            _wingFold = Mathf.Lerp(_wingFold, wingsBeat || gliding ? 0f : 1f, 1f - Mathf.Exp(-8f * dt));
            if (_wings.Length == 0)
            {
                return;
            }

            float rate = hoverer ? 3f : 6f + moving * 6f;
            float beat = Mathf.Sin(t * rate);
            float amp = Mathf.Lerp(14f, hoverer ? 22f : 42f, moving) * (1f - _wingFold);
            float shoulderZ = beat * amp;
            // The wrist trails the shoulder by roughly a fifth of a beat, which is what gives a wingbeat its
            // whip instead of the flat see-saw a single rigid slab produced.
            float wristZ = Mathf.Sin(t * rate - 1.3f) * amp * 0.45f;
            float twist = Mathf.Max(0f, -beat) * 10f * (1f - _wingFold); // angle of attack on the downstroke

            if (gliding)
            {
                // Spread and still, with a little dihedral — a ground bird's long flat bound (#1334).
                shoulderZ = 8f;
                wristZ = Mathf.Sin(t * 1.6f) * 3f;
                twist = 0f;
            }

            for (int i = 0; i < _wings.Length; i++)
            {
                var wing = _wings[i];
                if (wing?.Shoulder == null)
                {
                    continue;
                }

                float side = wing.Side == 0 ? 1f : -1f; // mirror left/right

                // Folding is the wrist's job: the outer panel swings back along the flank and the inner one
                // tips up a little. Rotating the whole wing 70° over the back — the old fold — is not what a
                // bird does when it lands.
                wing.Shoulder.localRotation = wing.ShoulderRest
                    * Quaternion.Euler(twist, 0f, (shoulderZ + 25f * _wingFold) * side);
                if (wing.Wrist != null)
                {
                    wing.Wrist.localRotation = wing.WristRest
                        * Quaternion.Euler(0f, -WingFoldYaw * _wingFold * side, wristZ * side);
                }
            }
        }

        /// <summary>Tail: a slow side-to-side sway, a touch livelier on the move (and quicker on hostiles).
        /// For a swimmer or crawler the tail is the motor — it beats faster and wider, leading the body's
        /// undulation. A multi-segment tail runs the beat as a wave, each link lagging the one before it.</summary>
        private void PoseTail(float t, float moving, bool undulates)
        {
            if (_tail.Length == 0)
            {
                return;
            }

            float rate = undulates ? 3.4f : (_hostile ? 2.6f : 1.8f);
            float span = Mathf.Lerp(8f, undulates ? 34f : 18f, moving);
            float swat = _flourish == Flourish.TailSwat
                ? Mathf.Sin(Mathf.Clamp01(_flourishT / _flourishDur) * Mathf.PI * 2f) * 26f
                : 0f;
            for (int i = 0; i < _tail.Length; i++)
            {
                if (_tail[i] == null)
                {
                    continue;
                }

                // Each link lags its parent, so the beat travels outward instead of the tail swinging rigidly.
                float lag = i * 0.55f;
                float sway = (Mathf.Sin(t * rate - lag) * span + swat) / Mathf.Max(1, _tail.Length);
                sway *= _tail.Length == 1 ? 1f : 1.6f;
                if (_rest > 0f)
                {
                    sway = Mathf.Lerp(sway, 38f * _restSide / Mathf.Max(1, _tail.Length), _rest); // curled around the body
                }

                _tail[i].localRotation = Quaternion.Euler(0f, sway, 0f);
            }
        }

        /// <summary>Swim / crawl: undulate the whole body rig — a yaw weave (the tail leads, the body follows a
        /// beat behind), a gentle counter-roll and a slow vertical glide (swimmers only). On top of that the
        /// gait's own weight shift: a vertical bob and a roll toward the loaded side, which is what sells the
        /// body's mass. A landing squash (#1333) rides over everything for ground movers.</summary>
        private void PoseBody(float dt, float t, float moving, bool undulates)
        {
            if (_body == null)
            {
                return;
            }

            float weave = 0f, roll = 0f, glide = 0f;
            if (undulates)
            {
                float sp = t * 3.4f;
                weave = Mathf.Sin(sp - 0.7f) * Mathf.Lerp(5f, 15f, moving); // body lags the tail beat
                roll = Mathf.Sin(sp - 1.2f) * Mathf.Lerp(2f, 7f, moving);
                glide = _aquatic ? Mathf.Sin(t * 1.1f) * 0.05f : 0f;    // slow rise/sink bob
            }

            // The gait's weight shift. Scaled by how much of the gait is actually showing, so a standing
            // animal does not bob on the spot.
            float gaitWeight = _legs.Length > 0 && !_airborne
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.04f, 0.3f, moving))
                : 0f;
            float bob = CreatureGait.BodyBob(_walk, _gait) * gaitWeight * _rig.LegLength * 0.045f;
            roll += CreatureGait.BodyRoll(_walk, _gait) * gaitWeight * 3.5f;

            // The weight-shift flourish: a slow lean while standing around.
            if (_flourish == Flourish.WeightShift)
            {
                roll += Mathf.Sin(Mathf.Clamp01(_flourishT / _flourishDur) * Mathf.PI) * 3f * _restSide;
            }

            float squash = 0f;
            if (_squashT < SquashDuration)
            {
                _squashT += dt;
                squash = Mathf.Sin(Mathf.Clamp01(_squashT / SquashDuration) * Mathf.PI) * 0.12f; // 0 → 0.12 → 0
            }

            // Lying down drops the body onto the ground; the tucked legs (PoseLegs) raise the feet to meet it,
            // so the two together land the belly just above the surface. A perched bird settles a little too.
            float restDrop = _rest * _rig.LegLength * (_rig.Giant ? 0.4f : 0.6f);
            if (_perched && !_airborne)
            {
                restDrop += _rig.LegLength * 0.04f;
            }

            // Standing on a slope: the body tilts to the plane through the planted feet. Without this the
            // root pitches but the legs do not, so half the feet hang in the air and half sink into the hill.
            float footPitch = 0f;
            float footLift = 0f;
            if (_feet.Active)
            {
                footPitch = _feet.BodyPitchDeg;
                roll += _feet.BodyRollDeg;
                footLift = _feet.BodyOffsetY;
            }

            _body.localRotation = Quaternion.Euler(-footPitch, weave, roll);
            _body.localPosition = new Vector3(0f, glide + bob - restDrop + footLift, 0f);
            _body.localScale = new Vector3(1f + squash * 0.6f, 1f - squash, 1f + squash * 0.6f);
        }

        /// <summary>Head: breathing + a per-temperament idle gesture (graze / alert / lunge) while stationary.</summary>
        private float PoseHead(float dt, float t, float moving)
        {
            if (_head == null)
            {
                return 0f;
            }

            // A gesture is shared out over the neck joints and the head, so a long-necked animal bends its
            // whole neck to reach the ground instead of nodding at the top of a rigid column.
            int gestureJoints = _neck.Length + 1;
            float gesture = 0f;
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
                        case Idle.Graze: gesture += 52f * p; break;                         // dip head to the ground
                        case Idle.Alert: gesture -= 16f * p; yaw += _gestureLook * Mathf.Sin(f * Mathf.PI * 2f); break; // snap up + look
                        case Idle.Lunge: gesture += 34f * p * (0.6f + 0.4f * Mathf.Sin(f * Mathf.PI * 3f)); break;       // sharp aggressive thrust
                        default: gesture += 4f * p; break;
                    }
                }

                yaw += StepGaze(dt, moving);
            }

            pitch += gesture / gestureJoints;

            // Lying down: the head lowers and tucks to one side.
            if (_rest > 0f)
            {
                pitch = Mathf.Lerp(pitch, 34f, _rest);
                yaw = Mathf.Lerp(yaw, 26f * _restSide, _rest);
            }

            _head.localRotation = Quaternion.Euler(pitch, yaw, 0f);
            return gesture / gestureJoints;
        }

        /// <summary>An idle animal turns its head to look at the player — passives glance over now and then,
        /// a hostile tracks. Yaw only, clamped, eased, and released as soon as it starts moving.</summary>
        private float StepGaze(float dt, float moving)
        {
            bool canLook = _gazeValid && moving < 0.25f && _rest < 0.2f && !_asleep;
            if (!canLook)
            {
                _gazeHold = 0f;
            }
            else if (_hostile)
            {
                _gazeHold = 0.2f; // a hostile keeps its eyes on you
            }
            else if (_gazeHold > 0f)
            {
                _gazeHold -= dt;
            }
            else
            {
                _gazeTimer -= dt;
                if (_gazeTimer <= 0f)
                {
                    _gazeHold = Random.Range(1.5f, 3f);
                    _gazeTimer = Random.Range(4f, 9f);
                }
            }

            float want = 0f;
            if (_gazeHold > 0f)
            {
                var to = _gazeTarget - _head.position;
                to.y = 0f;
                if (to.sqrMagnitude > 1e-4f)
                {
                    want = Mathf.Clamp(Vector3.SignedAngle(transform.forward, to.normalized, Vector3.up), -42f, 42f);
                }
            }

            _gazeYaw = Mathf.Lerp(_gazeYaw, want, 1f - Mathf.Exp(-5f * dt));
            return _gazeYaw;
        }

        /// <summary>The jaw: one open per vocalisation pulse, a hard snap for a bite, and a slack breathing
        /// mouth while asleep.</summary>
        private void PoseJaw(float dt, float t)
        {
            if (_jaw == null)
            {
                return;
            }

            float open = 0f;
            if (_jawT < _jawDur)
            {
                _jawT += dt;
                open = Mathf.Sin(Mathf.Clamp01(_jawT / _jawDur) * Mathf.PI) * _jawDeg;
            }

            if (_asleep)
            {
                open = Mathf.Max(open, 5f + Mathf.Sin(t * 0.6f) * 3f);
            }

            _jaw.localRotation = _jawRest * Quaternion.Euler(open, 0f, 0f);
        }

        /// <summary>Blinking. Held shut while asleep; otherwise an occasional single or double blink. The lid
        /// is a skin-coloured box scaled up over the eye, so "open" costs nothing to draw.</summary>
        private void PoseEyelids(float dt)
        {
            if (_eyelids.Length == 0)
            {
                return;
            }

            float closed;
            if (_asleep || _rest > 0.5f)
            {
                closed = 1f;
            }
            else if (_blinkT < BlinkDuration)
            {
                _blinkT += dt;
                closed = Mathf.Sin(Mathf.Clamp01(_blinkT / BlinkDuration) * Mathf.PI);
                if (_blinkT >= BlinkDuration && _blinksLeft > 0)
                {
                    _blinksLeft--;
                    _blinkT = 0f;
                }
            }
            else
            {
                closed = 0f;
                _blinkTimer -= dt;
                if (_blinkTimer <= 0f)
                {
                    _blinkT = 0f;
                    _blinksLeft = Random.value < 0.2f ? 1 : 0; // the odd double blink
                    _blinkTimer = Random.Range(2.5f, 6f);
                }
            }

            for (int i = 0; i < _eyelids.Length; i++)
            {
                if (_eyelids[i] != null)
                {
                    _eyelids[i].localScale = new Vector3(1f, closed, 1f);
                }
            }
        }

        /// <summary>Ears: a slow idle sway, plus the flick flourish.</summary>
        private void PoseEars(float dt, float t)
        {
            if (_ears.Length == 0)
            {
                return;
            }

            float flick = _flourish == Flourish.EarFlick
                ? Mathf.Sin(Mathf.Clamp01(_flourishT / _flourishDur) * Mathf.PI * 3f) * 18f
                : 0f;
            float sway = Mathf.Sin(t * 0.8f) * 2f;
            for (int i = 0; i < _ears.Length; i++)
            {
                if (_ears[i] == null)
                {
                    continue;
                }

                float side = (i & 1) == 0 ? 1f : -1f;
                _ears[i].localRotation = _earRest[i] * Quaternion.Euler(0f, 0f, (sway + flick) * side);
            }
        }

        /// <summary>Picks and runs the small things an animal does while standing around doing nothing —
        /// a tail swat, an ear flick, a shift of weight. Only after it has genuinely settled.</summary>
        private void StepFlourish(float dt)
        {
            if (_flourish != Flourish.None)
            {
                _flourishT += dt;
                if (_flourishT >= _flourishDur)
                {
                    _flourish = Flourish.None;
                }

                return;
            }

            if (_idleTime < 4f)
            {
                return;
            }

            _flourishTimer -= dt;
            if (_flourishTimer > 0f)
            {
                return;
            }

            _flourishTimer = Random.Range(5f, 11f);
            _flourishT = 0f;
            int roll = Random.Range(0, 3);
            _flourish = roll == 0 && _tail.Length > 0 ? Flourish.TailSwat
                : roll == 1 && _ears.Length > 0 ? Flourish.EarFlick
                : Flourish.WeightShift;
            _flourishDur = _flourish == Flourish.TailSwat ? 0.7f : _flourish == Flourish.EarFlick ? 0.5f : 0.9f;
        }

        /// <summary>Medusa (#637): the bell pulses (squash-and-stretch around its base scale). There are no
        /// legs or head, so this and the trailing rim arms are the whole performance.</summary>
        private void PoseBell(float t)
        {
            if (_bell == null)
            {
                return;
            }

            _bellPulse = Mathf.Sin(t * (_asleep ? 0.9f : 1.8f));
            float sink = _rest * 0.15f;
            _bell.localScale = new Vector3(
                _bellBaseScale.x * (1f - 0.06f * _bellPulse),
                _bellBaseScale.y * (1f + 0.12f * _bellPulse) * (1f - sink),
                _bellBaseScale.z * (1f - 0.06f * _bellPulse));
        }

        /// <summary>Tentacles and arms: a wave that travels outward along each chain, out of phase between
        /// arms. On a medusa the bell's own contraction drives the amplitude, so the arms trail the pulse
        /// instead of waving independently of it.</summary>
        private void PoseTentacles(float t, float moving)
        {
            bool medusa = _bell != null;
            for (int i = 0; i < _tentacles.Length; i++)
            {
                var arm = _tentacles[i];
                if (arm == null || arm.Length == 0)
                {
                    continue;
                }

                float span = (medusa ? 8f + 7f * moving + 4f * Mathf.Max(0f, -_bellPulse) : 6f + 9f * moving)
                    / Mathf.Max(1, arm.Length);
                for (int seg = 0; seg < arm.Length; seg++)
                {
                    if (arm[seg] == null)
                    {
                        continue;
                    }

                    float ph = t * (medusa ? 1.3f : 1.9f) + i * 0.9f - seg * 0.55f; // the wave runs outward
                    arm[seg].localRotation = Quaternion.Euler(
                        Mathf.Sin(ph) * span, 0f, Mathf.Cos(ph * 0.8f) * span);
                }
            }
        }

        /// <summary>Fins beat on the paddle phase: pectorals sculling out of phase with each other, the tail
        /// fin sweeping with the body's undulation, the dorsal barely moving. Ashore (an amphibian out of the
        /// water) they fold flat against the body instead of rowing at nothing.</summary>
        private void PoseFins(float t, float moving)
        {
            if (_fins.Length == 0)
            {
                return;
            }

            bool inWater = _motionClass == MotionClass.Swimmer;
            float folded = inWater ? 0f : 1f;
            _finFold = Mathf.MoveTowards(_finFold, folded, Time.deltaTime * 2.5f);

            float rate = 5f + moving * 4f;
            float amp = Mathf.Lerp(9f, 26f, moving) * (1f - _finFold);
            for (int i = 0; i < _fins.Length; i++)
            {
                if (_fins[i] == null)
                {
                    continue;
                }

                // 0/1 are the pectorals (mirrored), 2 the caudal, 3 the dorsal.
                if (i < 2)
                {
                    float side = i == 0 ? 1f : -1f;
                    float beat = Mathf.Sin(t * rate + (i == 0 ? 0f : Mathf.PI)) * amp;
                    _fins[i].localRotation = Quaternion.Euler(beat * 0.5f, 0f, (beat + 55f * _finFold) * side);
                }
                else if (i == 2)
                {
                    _fins[i].localRotation = Quaternion.Euler(0f, Mathf.Sin(t * rate * 0.6f) * amp * 0.8f, 0f);
                }
                else
                {
                    _fins[i].localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.4f) * 3f);
                }
            }
        }

        /// <summary>The trunk: a slow idle curl and sway, dipping with a graze and lifting on a call.</summary>
        private void PoseTrunk(float t, float gesturePitch)
        {
            if (_trunk.Length == 0)
            {
                return;
            }

            float jawLift = _jawT < _jawDur ? Mathf.Sin(Mathf.Clamp01(_jawT / _jawDur) * Mathf.PI) * 12f : 0f;
            for (int i = 0; i < _trunk.Length; i++)
            {
                if (_trunk[i] == null)
                {
                    continue;
                }

                float ph = t * 0.9f - i * 0.6f;
                float curl = (8f + gesturePitch * 0.35f - jawLift) * (0.5f + 0.5f * i / Mathf.Max(1, _trunk.Length));
                _trunk[i].localRotation = Quaternion.Euler(curl, Mathf.Sin(ph) * 5f, Mathf.Sin(ph * 0.7f) * 4f);
            }
        }

        /// <summary>The neck: it carries its share of the head gesture, so lowering the head lowers the whole
        /// neck. As a rigid stack a giraffe's graze could only nod the head at the top of a column.</summary>
        private void PoseNeck(float t, float share)
        {
            for (int i = 0; i < _neck.Length; i++)
            {
                if (_neck[i] == null)
                {
                    continue;
                }

                float sway = Mathf.Sin(t * 0.7f - i * 0.4f) * 1.6f;
                _neck[i].localRotation = Quaternion.Euler(share, sway, 0f);
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
