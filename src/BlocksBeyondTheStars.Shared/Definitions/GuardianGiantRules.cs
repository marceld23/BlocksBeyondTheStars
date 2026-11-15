// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// One move of an awake guardian giant (#3676). The server picks the move and streams it with its start
/// (<c>GuardianGiantStatus.Move</c> + <c>MoveElapsed</c>); every client plays the same animation from the same clock, and the
/// server lands the damage exactly at <see cref="GuardianGiantMoveSpec.Impact"/>. The numeric values are on the wire — append
/// only, never renumber.
/// </summary>
public enum GuardianGiantMove : byte
{
    /// <summary>Not awake: the giant is a block body (the status message then only tells the client the fight is over).</summary>
    None = 0,

    /// <summary>The block body rises: the lying pose turns into the standing one about the heels.</summary>
    Rise = 1,

    /// <summary>Standing, breathing, the head scanning (loops).</summary>
    Idle = 2,

    /// <summary>The heavy stride toward the target (loops).</summary>
    Walk = 3,

    /// <summary>A jump over a step or a tree while walking — no damage.</summary>
    Hop = 4,

    /// <summary>A foot rises (the warning), then slams: a shock wave around the giant.</summary>
    Stomp = 5,

    /// <summary>A crouch (the warning, with the red landing shadow at the aim point), a leap, a landing shock wave there.</summary>
    JumpAttack = 6,

    /// <summary>The visor glows (the warning), then a beam toward the aim point; cover blocks it.</summary>
    Laser = 7,

    /// <summary>The giant tears a rock (an effect, never a block) from the ground and throws it at the aim point.</summary>
    Throw = 8,

    /// <summary>Arms up, a light pulse: two Guardian robots appear beside it (from half hull on).</summary>
    Summon = 9,

    /// <summary>The target hid behind cover: it stands and sweeps its head with a search light (loops).</summary>
    Search = 10,

    /// <summary>The switch into the rage phase at half hull — the lights blink faster and the eyes burn brighter.</summary>
    Enrage = 11,

    /// <summary>Defeated: it sinks to its knees and falls — afterwards it is a block body again, lights out, for good.</summary>
    Collapse = 12,

    /// <summary>Everyone left: it walked back to its resting place and lies down again (lights still on); healed.</summary>
    LieDown = 13,
}

/// <summary>The timing of one <see cref="GuardianGiantMove"/> (seconds from the move's start).</summary>
public readonly struct GuardianGiantMoveSpec
{
    public GuardianGiantMoveSpec(float duration, float impact, float warning)
    {
        Duration = duration;
        Impact = impact;
        Warning = warning;
    }

    /// <summary>How long the move lasts; 0 = it loops until the server picks the next one.</summary>
    public float Duration { get; }

    /// <summary>When it lands (damage, shock wave, the rock's hit, the robots appear); a negative value = it never hits.</summary>
    public float Impact { get; }

    /// <summary>How long the visible warning lasts from the start (the raised foot, the crouch, the glowing visor).</summary>
    public float Warning { get; }
}

/// <summary>
/// The moments VEGA speaks about a guardian giant (#3676). The server sends every giant line through one method and picks the
/// locale key through one seam (<c>GuardianGiantVegaKey</c>), so the story can choose its own variants per moment; the neutral
/// keys are <c>vega.giant.found</c>, <c>.stir</c>, <c>.rise</c>, <c>.rage</c>, <c>.victory</c> and <c>.lie_down</c>.
/// </summary>
public enum GuardianGiantVegaMoment : byte
{
    /// <summary>The first giant a player finds (once per player).</summary>
    Found = 0,

    /// <summary>A sleeping giant's eye flickers as a player comes near (once per approach).</summary>
    Stir = 1,

    /// <summary>It wakes and stands up.</summary>
    Rise = 2,

    /// <summary>Half its hull is gone: the rage phase, the robots come.</summary>
    Rage = 3,

    /// <summary>It fell for good.</summary>
    Victory = 4,

    /// <summary>Everyone left: it lay down again at its resting place.</summary>
    LieDown = 5,
}

/// <summary>
/// The rules of the guardian giants (#3676, Justus' idea: huge fallen block-built hunter robots — rarely one wakes up when a
/// player comes near, stands up and is a boss with a boss bar). Pure and Unity-free so the server (when it wakes, what each
/// attack does and when), the client (the animation clock, the warnings, the boss bar) and the tests read the same numbers.
/// The body itself is <see cref="GuardianGiantBody"/>; the hull and the loot base are <c>data/enemies.json</c>'s
/// <see cref="EnemyKey"/> entry.
/// </summary>
public static class GuardianGiantRules
{
    /// <summary>The <c>data/enemies.json</c> key (the Codex entry, the hull, the base loot).</summary>
    public const string EnemyKey = "guardian_giant";

    /// <summary>The <c>NetCombatEntity.Kind</c> of an awake giant.</summary>
    public const string NetKind = "GuardianGiant";

    /// <summary>The structure kind of a lying giant (structure records, structure types, the source key).</summary>
    public const string StructureKind = "guardian_giant";

    /// <summary>The planet-map POI type of a giant (a found one; awake/defeated told apart by the POI's state).</summary>
    public const string PoiType = "guardian_giant";

    /// <summary><c>NetPoi.State</c> of a giant's map marker: it lies there (dormant or lit — whether it can wake is never
    /// revealed).</summary>
    public const string PoiLying = "lying";

    /// <summary><c>NetPoi.State</c>: it is awake and walks (the marker follows it).</summary>
    public const string PoiAwake = "awake";

    /// <summary><c>NetPoi.State</c>: defeated — the corpse lies where it fell.</summary>
    public const string PoiDefeated = "defeated";

    /// <summary>A player this close (flat distance to the body's centre) has found the giant: its map marker shows from then
    /// on, and VEGA names the first one a player finds.</summary>
    public const float FoundRange = 48f;

    /// <summary>Every player within this flat distance of an awake giant receives its <c>GuardianGiantStatus</c>.</summary>
    public const float StatusRange = 96f;

    /// <summary>The <c>WorldFx</c> kind of a shock wave landing (a stomp, the jump attack's landing): at the wave's centre,
    /// <c>Radius</c> = the wave's reach. The client pushes its own player away from the centre (<see cref="KnockSpeed"/>,
    /// <see cref="KnockLift"/>, falling off like the damage) and shakes the camera.</summary>
    public const string WaveFx = "guardian_giant_wave";

    /// <summary>The <c>WorldFx</c> kind of the thrown rock's impact at the aim point (<c>Radius</c> = <see cref="ThrowRadius"/>).</summary>
    public const string RockFx = "guardian_giant_rock";

    /// <summary>The share of placed giants that can wake (seeded per site): about one in four.</summary>
    public const double WakeShare = 0.25;

    /// <summary>A player on foot this close (flat distance to the body's centre) makes one eye flicker and VEGA warn.</summary>
    public const float StirRange = 40f;

    /// <summary>A player on foot this close wakes it: every light switches on and it rises.</summary>
    public const float WakeRange = 20f;

    /// <summary>It never follows anyone further than this from its resting place (flat distance).</summary>
    public const float LeashRange = 40f;

    /// <summary>Players within this flat distance of the awake giant take part (shared boss bar, loot crate each).</summary>
    public const float FightRange = 64f;

    /// <summary>The awake giant's hull when <c>data/enemies.json</c> has no entry (the shipped entry says the same).</summary>
    public const float DefaultHull = 600f;

    /// <summary>The share of the hull at which the rage phase starts and the robots come.</summary>
    public const float RageAt = 0.5f;

    /// <summary>How many Guardian robots one <see cref="GuardianGiantMove.Summon"/> brings.</summary>
    public const int SummonCount = 2;

    /// <summary>The most summons in one fight.</summary>
    public const int MaxSummons = 2;

    /// <summary>Walking speed (blocks/s): calm and in the rage phase. A running player is faster.</summary>
    public const float WalkSpeed = 2.4f;

    /// <summary>See <see cref="WalkSpeed"/>.</summary>
    public const float RageWalkSpeed = 3.2f;

    /// <summary>The pause between two attacks (seconds): calm and in the rage phase.</summary>
    public const float AttackPause = 2.6f;

    /// <summary>See <see cref="AttackPause"/>.</summary>
    public const float RageAttackPause = 1.6f;

    /// <summary>The stomp's shock wave reach around the giant's feet.</summary>
    public const float StompRadius = 6f;

    /// <summary>The stomp's damage at the centre (less toward the edge, <see cref="EdgeShare"/>).</summary>
    public const float StompDamage = 18f;

    /// <summary>The jump attack's farthest leap and its landing wave's reach and damage.</summary>
    public const float JumpRange = 18f;

    /// <summary>See <see cref="JumpRange"/>.</summary>
    public const float JumpRadius = 6f;

    /// <summary>See <see cref="JumpRange"/>.</summary>
    public const float JumpDamage = 22f;

    /// <summary>The visor laser's reach and its damage (once, at the beam's start; cover between visor and player stops it).</summary>
    public const float LaserRange = 28f;

    /// <summary>See <see cref="LaserRange"/>.</summary>
    public const float LaserDamage = 16f;

    /// <summary>How close to the beam's line a player must be to be hit.</summary>
    public const float LaserWidth = 1.2f;

    /// <summary>The rock's reach (the aim point's farthest distance), its splash and its damage.</summary>
    public const float ThrowRange = 30f;

    /// <summary>See <see cref="ThrowRange"/>.</summary>
    public const float ThrowRadius = 2.5f;

    /// <summary>See <see cref="ThrowRange"/>.</summary>
    public const float ThrowDamage = 15f;

    /// <summary>The share of a shock wave's damage left at its very edge.</summary>
    public const float EdgeShare = 0.35f;

    /// <summary>A shock wave's push (blocks/s) at its centre, and its upward part.</summary>
    public const float KnockSpeed = 7f;

    /// <summary>See <see cref="KnockSpeed"/>.</summary>
    public const float KnockLift = 4f;

    /// <summary>A shock wave runs along the ground: nobody further than this above or below its centre feels it (the server's
    /// damage and the client's push read the same number).</summary>
    public const float WaveHeight = 3f;

    /// <summary>The highest step (blocks) it walks up; anything higher up to <see cref="HopHeight"/> it jumps.</summary>
    public const int StepHeight = 2;

    /// <summary>See <see cref="StepHeight"/>.</summary>
    public const int HopHeight = 6;

    /// <summary>The camera shake (0–1) a player feels at the centre of a stomp or a landing; it falls off to 0 at twice the
    /// wave's radius. The client skips it when reduced motion is on.</summary>
    public const float ShakeStrength = 0.6f;

    /// <summary>The timing of each move.</summary>
    public static GuardianGiantMoveSpec Spec(GuardianGiantMove move) => move switch
    {
        GuardianGiantMove.Rise => new GuardianGiantMoveSpec(3.0f, -1f, 0f),
        GuardianGiantMove.Idle => new GuardianGiantMoveSpec(0f, -1f, 0f),
        GuardianGiantMove.Walk => new GuardianGiantMoveSpec(0f, -1f, 0f),
        GuardianGiantMove.Hop => new GuardianGiantMoveSpec(1.2f, 1.0f, 0.3f),
        GuardianGiantMove.Stomp => new GuardianGiantMoveSpec(1.6f, 1.0f, 1.0f),
        GuardianGiantMove.JumpAttack => new GuardianGiantMoveSpec(2.6f, 2.0f, 1.0f),
        GuardianGiantMove.Laser => new GuardianGiantMoveSpec(2.6f, 1.5f, 1.5f),
        GuardianGiantMove.Throw => new GuardianGiantMoveSpec(2.2f, 1.8f, 1.0f),
        GuardianGiantMove.Summon => new GuardianGiantMoveSpec(2.0f, 1.2f, 1.2f),
        GuardianGiantMove.Search => new GuardianGiantMoveSpec(0f, -1f, 0f),
        GuardianGiantMove.Enrage => new GuardianGiantMoveSpec(1.5f, -1f, 0f),
        GuardianGiantMove.Collapse => new GuardianGiantMoveSpec(3.0f, 2.4f, 0f),
        GuardianGiantMove.LieDown => new GuardianGiantMoveSpec(3.0f, -1f, 0f),
        _ => new GuardianGiantMoveSpec(0f, -1f, 0f),
    };

    /// <summary>When the thrown rock leaves the hand (seconds from the start of <see cref="GuardianGiantMove.Throw"/>); it
    /// lands at <see cref="GuardianGiantMoveSpec.Impact"/>.</summary>
    public const float ThrowRelease = 1.0f;

    /// <summary>When the jump attack leaves the ground (it lands at the move's impact).</summary>
    public const float JumpTakeOff = 1.0f;

    /// <summary>How long the laser beam is drawn from the impact on.</summary>
    public const float LaserBeamSeconds = 1.0f;

    /// <summary>Whether a move is one of the attacks (the server never starts one while another runs).</summary>
    public static bool IsAttack(GuardianGiantMove move)
        => move is GuardianGiantMove.Stomp or GuardianGiantMove.JumpAttack or GuardianGiantMove.Laser
            or GuardianGiantMove.Throw or GuardianGiantMove.Summon;

    /// <summary>A shock wave's damage at <paramref name="distance"/> from its centre: full there, <see cref="EdgeShare"/> of it
    /// at <paramref name="radius"/>, none beyond.</summary>
    public static float WaveDamage(float full, float radius, float distance)
    {
        if (distance > radius || radius <= 0f)
        {
            return 0f;
        }

        float t = distance / radius;
        return full * (1f - ((1f - EdgeShare) * t));
    }
}
