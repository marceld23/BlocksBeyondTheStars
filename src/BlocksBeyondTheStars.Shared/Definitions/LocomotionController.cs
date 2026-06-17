using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>The broad movement signature of an actor — chosen randomly per creature species (so a world's
/// fauna move in recognisably different ways), or fixed per NPC / planet-enemy kind. Drives the numeric
/// <see cref="LocomotionProfile"/> (cadence, pausing, turn rate, weave, vertical life).</summary>
public enum LocomotionStyle
{
    Strider,    // steady medium roaming, long gentle arcs, rarely stops
    Grazer,     // slow, frequent long pauses, short moves between (head-down feeders)
    Darter,     // quick bursts then freeze, sudden direction flips (skittish small things)
    Prowler,    // slow steady stalk, pause-and-scan (predators)
    Hopper,     // discrete hops — a vertical pop synced to the move/pause cadence
    Drifter,    // lazy floaty bobbing curves (gas-sac grazers / air drifters)
    Slitherer,  // continuous high-amplitude serpentine weave (limbless, segmented)
    Glider,     // sinusoidal altitude swoops (fliers)
    Schooler,   // steady cruiser that aligns with nearby kin (fish/birds; herding is caller-side)
}

/// <summary>What an actor wants to do this tick. <see cref="MoveMode.Pause"/> is decided internally by the
/// controller during <see cref="MoveMode.Roam"/>; callers only ever request Roam / Seek / Flee.</summary>
public enum MoveMode : byte { Roam, Pause, Seek, Flee }

/// <summary>Per-actor movement tuning. Seeded per species for creatures; a fixed constant per NPC / enemy
/// kind (no per-individual variation there). All times in seconds, speeds in blocks/s, angles in radians.</summary>
public struct LocomotionProfile
{
    public LocomotionStyle Style;
    public float CruiseSpeed;   // baseline roam speed
    public float BurstSpeed;    // chase/flee sprint speed
    public float Accel;         // blocks/s^2 — how fast Speed eases toward its target (momentum/weight)
    public float TurnRate;      // rad/s — max heading change per second (turn inertia, no instant snaps)
    public float HoldMin, HoldMax;   // how long a roam heading is held before re-rolling
    public float PauseChance;        // 0..1 chance a finished roam segment becomes a Pause instead of a new heading
    public float PauseMin, PauseMax; // pause (stand-still) duration range
    public float WeaveAmp, WeaveFreq;// gentle in-segment heading weave so paths aren't dead straight
    public float VertAmp, VertFreq;  // vertical-life wave (caller applies to Y: swoop / dive / hop)
}

/// <summary>Mutable per-actor movement state, carried on the entity (CombatEntity / ServerNpc) and stepped
/// each tick by <see cref="LocomotionController.Step"/>. A default (all-zero) value is valid and initialises
/// itself on first step.</summary>
public struct LocomotionState
{
    public MoveMode Mode;
    public float ModeTimer;  // seconds left in the current roam segment or pause
    public float Heading;    // current heading (radians; dirX = cos, dirZ = sin — matches CreatureBehaviour)
    public float Speed;      // current eased speed
    public float WeavePhase; // in-segment weave accumulator
    public float VertPhase;  // vertical-life accumulator
    public uint Seq;         // deterministic roll counter (seed source for the next segment)
    public bool Initialized;
}

/// <summary>The result of one movement step.</summary>
public readonly struct LocomotionResult
{
    public LocomotionResult(LocomotionState state, Vector3f position, float facing, float vertWave, bool moving)
    {
        State = state; Position = position; Facing = facing; VertWave = vertWave; Moving = moving;
    }

    public LocomotionState State { get; }
    public Vector3f Position { get; }
    public float Facing { get; }    // = State.Heading (math convention: atan2(dirZ, dirX))
    public float VertWave { get; }  // sin(VertPhase) ∈ [-1,1] — caller scales into a Y offset (swoop/dive/hop)
    public bool Moving { get; }     // true while actually moving (callers skip re-facing when standing still)
}

/// <summary>
/// Pure, deterministic locomotion: turns an actor's current spot + a desired intent into the next spot, with
/// <b>stop-and-go</b> (roam ↔ pause), <b>speed easing</b> (momentum), <b>turn-rate inertia</b> (no instant
/// heading snaps), an in-segment <b>weave</b>, and a <b>vertical-life</b> wave — so movement reads alive rather
/// than gliding mechanically in straight lines. Stateless apart from the <see cref="LocomotionState"/> the
/// caller carries, and seeded from a per-actor <c>seed</c> (no <c>Random</c>), so it's trivially unit-testable
/// and reproducible. Horizontal only (X/Z); the caller keeps Y (surface/hover/fluid level), optionally using
/// <see cref="LocomotionResult.VertWave"/> for habitat-appropriate vertical motion.
/// </summary>
public static class LocomotionController
{
    private const float Tau = 6.2831853f;

    public static LocomotionResult Step(
        LocomotionState s, in LocomotionProfile p, Vector3f cur,
        MoveMode intent, Vector3f? target, double dt, uint seed)
    {
        float ft = (float)dt;
        if (ft <= 0f)
        {
            return new LocomotionResult(s, cur, s.Heading, (float)System.Math.Sin(s.VertPhase), false);
        }

        if (!s.Initialized)
        {
            s.Initialized = true;
            s.Seq = 1;
            s.Heading = Unit(Mix(seed, s.Seq++)) * Tau;
            s.Mode = MoveMode.Roam;
            s.ModeTimer = Lerp(p.HoldMin, p.HoldMax, Unit(Mix(seed, s.Seq++)));
            s.Speed = 0f;
        }

        float desired = s.Heading;
        float targetSpeed;

        if ((intent == MoveMode.Seek || intent == MoveMode.Flee) && target is { } tp)
        {
            // Pursue / evade: head toward (or directly away from) the target, with a slight weave so it isn't a
            // laser-straight beeline. Sprint at the burst speed.
            s.Mode = intent;
            float dx = tp.X - cur.X, dz = tp.Z - cur.Z;
            if (dx * dx + dz * dz > 1e-6f)
            {
                float toTarget = (float)System.Math.Atan2(dz, dx);
                desired = intent == MoveMode.Seek ? toTarget : toTarget + (float)System.Math.PI;
            }

            s.WeavePhase += ft * p.WeaveFreq;
            desired += (float)System.Math.Sin(s.WeavePhase) * p.WeaveAmp * 0.4f;
            targetSpeed = p.BurstSpeed > 0f ? p.BurstSpeed : p.CruiseSpeed;
        }
        else
        {
            // Roam ↔ Pause internal state machine. Coming back from a chase resumes roaming with a fresh segment.
            if (s.Mode != MoveMode.Roam && s.Mode != MoveMode.Pause)
            {
                s.Mode = MoveMode.Roam;
                s.ModeTimer = 0f;
            }

            s.ModeTimer -= ft;
            if (s.ModeTimer <= 0f)
            {
                if (s.Mode == MoveMode.Roam && Unit(Mix(seed, s.Seq++)) < p.PauseChance)
                {
                    s.Mode = MoveMode.Pause;
                    s.ModeTimer = Lerp(p.PauseMin, p.PauseMax, Unit(Mix(seed, s.Seq++)));
                }
                else
                {
                    s.Mode = MoveMode.Roam;
                    s.Heading = Unit(Mix(seed, s.Seq++)) * Tau;
                    s.ModeTimer = Lerp(p.HoldMin, p.HoldMax, Unit(Mix(seed, s.Seq++)));
                }
            }

            if (s.Mode == MoveMode.Roam)
            {
                s.WeavePhase += ft * p.WeaveFreq;
                desired = s.Heading + (float)System.Math.Sin(s.WeavePhase) * p.WeaveAmp;
                targetSpeed = p.CruiseSpeed;
            }
            else
            {
                desired = s.Heading;
                targetSpeed = 0f; // standing still
            }
        }

        // Ease speed toward its target (momentum), and turn toward the desired heading at a clamped rate (inertia).
        float accel = p.Accel > 0f ? p.Accel : 999f;
        s.Speed = MoveToward(s.Speed, targetSpeed, accel * ft);
        float turn = p.TurnRate > 0f ? p.TurnRate : 999f;
        s.Heading = TurnToward(s.Heading, desired, turn * ft);
        s.VertPhase += ft * p.VertFreq;

        float move = s.Speed * ft;
        var pos = new Vector3f(
            cur.X + (float)System.Math.Cos(s.Heading) * move,
            cur.Y,
            cur.Z + (float)System.Math.Sin(s.Heading) * move);

        return new LocomotionResult(s, pos, s.Heading, (float)System.Math.Sin(s.VertPhase), s.Speed > 0.05f);
    }

    /// <summary>A tamed companion's movement: hurry toward the owner when it has fallen behind, otherwise loiter
    /// nearby (a reduced-pace roam with pauses), reusing the same easing/turning so pets move as smoothly as wild
    /// fauna. Mirrors the old <see cref="CreatureBehaviour.FollowStep"/> intent, now stateful.</summary>
    public static LocomotionResult FollowStep(
        LocomotionState s, in LocomotionProfile p, Vector3f cur, Vector3f owner,
        float followDistance, double dt, uint seed)
    {
        float dx = owner.X - cur.X, dz = owner.Z - cur.Z;
        float dist = (float)System.Math.Sqrt(dx * dx + dz * dz);
        if (dist > followDistance)
        {
            var pp = p;
            pp.BurstSpeed = dist > followDistance * 3f
                ? System.Math.Max(p.CruiseSpeed, p.BurstSpeed) * 1.3f // really far behind → put on a sprint
                : System.Math.Max(p.CruiseSpeed, p.BurstSpeed);
            return Step(s, pp, cur, MoveMode.Seek, owner, dt, seed);
        }

        var lp = p;
        lp.CruiseSpeed = p.CruiseSpeed * 0.45f; // close enough → mill gently by the owner
        return Step(s, lp, cur, MoveMode.Roam, null, dt, seed);
    }

    /// <summary>Builds a creature species' movement profile from its randomly-chosen <see cref="LocomotionStyle"/>,
    /// scaled by its <see cref="CreatureSpecies.Speed"/> and <see cref="CreatureSpecies.Size"/> (bigger animals
    /// turn + accelerate more sluggishly), with a small per-species jitter (hashed from the id) so two species of
    /// the same style still differ. Deterministic — same species → same profile.</summary>
    public static LocomotionProfile ForSpecies(CreatureSpecies sp)
    {
        // base table per style: (cruiseFrac, burstFrac, turn, holdMin, holdMax, pauseChance, pauseMin, pauseMax, weaveAmp, weaveFreq, vertAmp, vertFreq)
        var b = sp.LocoStyle switch
        {
            LocomotionStyle.Grazer    => (0.55f, 1.2f, 2.2f, 1.2f, 2.5f, 0.65f, 1.5f, 4.0f, 0.25f, 1.4f, 0f,    0f),
            LocomotionStyle.Darter    => (1.00f, 1.6f, 4.5f, 0.5f, 1.4f, 0.55f, 0.4f, 1.2f, 0.35f, 2.6f, 0f,    0f),
            LocomotionStyle.Prowler   => (0.60f, 1.4f, 2.4f, 2.0f, 4.0f, 0.45f, 1.0f, 2.5f, 0.15f, 1.0f, 0f,    0f),
            LocomotionStyle.Hopper    => (0.90f, 1.3f, 3.0f, 0.5f, 1.0f, 0.55f, 0.5f, 1.2f, 0.10f, 1.0f, 0.55f, 3.0f),
            LocomotionStyle.Drifter   => (0.50f, 1.0f, 1.2f, 2.5f, 5.0f, 0.25f, 1.5f, 3.5f, 0.40f, 0.6f, 0.25f, 0.8f),
            LocomotionStyle.Slitherer => (0.80f, 1.2f, 2.6f, 2.0f, 4.5f, 0.15f, 0.8f, 2.0f, 0.60f, 2.2f, 0f,    0f),
            LocomotionStyle.Glider    => (0.90f, 1.3f, 1.4f, 3.0f, 6.0f, 0.10f, 1.0f, 2.5f, 0.30f, 0.7f, 1.0f,  0.5f),
            LocomotionStyle.Schooler  => (0.85f, 1.3f, 2.8f, 1.5f, 3.5f, 0.20f, 0.5f, 1.5f, 0.20f, 1.4f, 0.3f,  0.9f),
            _                         => (0.85f, 1.3f, 2.0f, 2.5f, 5.0f, 0.20f, 0.6f, 1.5f, 0.18f, 1.2f, 0f,    0f), // Strider
        };

        uint h = (uint)StableHash(sp.Id);
        float j = 0.85f + Unit(h) * 0.30f;          // ±15% per-species jitter on cadence/turn
        float size = System.Math.Clamp(sp.Size, 0.6f, 2.2f);
        float weight = 1f / (0.5f + size);          // bigger → slower accel + slower turns

        return new LocomotionProfile
        {
            Style = sp.LocoStyle,
            CruiseSpeed = sp.Speed * b.Item1,
            BurstSpeed = sp.Speed * b.Item2,
            Accel = System.Math.Max(2f, sp.Speed * 4f * weight),
            TurnRate = b.Item3 * j * (0.6f + weight),
            HoldMin = b.Item4 * j,
            HoldMax = b.Item5 * j,
            PauseChance = b.Item6,
            PauseMin = b.Item7 * j,
            PauseMax = b.Item8 * j,
            WeaveAmp = b.Item9,
            WeaveFreq = b.Item10 * j,
            VertAmp = b.Item11,
            VertFreq = b.Item12,
        };
    }

    // --- small deterministic helpers ---

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float MoveToward(float cur, float target, float maxDelta)
    {
        float d = target - cur;
        if (System.Math.Abs(d) <= maxDelta) return target;
        return cur + System.Math.Sign(d) * maxDelta;
    }

    private static float TurnToward(float cur, float target, float maxDelta)
    {
        float diff = WrapAngle(target - cur);
        if (System.Math.Abs(diff) <= maxDelta) return WrapAngle(target);
        return WrapAngle(cur + System.Math.Sign(diff) * maxDelta);
    }

    private static float WrapAngle(float a)
    {
        while (a > System.Math.PI) a -= Tau;
        while (a < -System.Math.PI) a += Tau;
        return a;
    }

    private static uint Mix(uint a, uint b)
    {
        unchecked
        {
            uint h = a * 2654435761u ^ (b + 0x9E3779B9u + (a << 6) + (a >> 2));
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
            return h;
        }
    }

    private static float Unit(uint h) => (h & 0xFFFFFFu) / (float)0x1000000;

    private static int StableHash(string s)
    {
        unchecked
        {
            int h = 17;
            foreach (char c in s ?? string.Empty) h = h * 31 + c;
            return h;
        }
    }
}
