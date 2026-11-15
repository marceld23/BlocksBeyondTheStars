// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// An awake guardian giant (#3676) — see GameServerGuardianGiants.cs. It rises where it lay (the lying body's blocks go, the
/// boss stands at the anchor's column as a planet enemy of kind <see cref="CombatEntityKind.GuardianGiant"/>), then runs one
/// <see cref="GuardianGiantMove"/> after another on the snapshot clock (<see cref="SnapshotTime"/>, the clock the planet-enemy
/// lists carry). A timed move runs to its end; a looping one (idle, walk, search) is re-decided every tick:
/// <list type="number">
/// <item>everyone gone for <see cref="GuardianGiantLeashSeconds"/> (or the machines rule switched off) → it walks back and lies
/// down (lit, healed);</item>
/// <item>half the hull gone → the rage once;</item>
/// <item>the target: the nearest player on foot within <see cref="GuardianGiantRules.FightRange"/> it may hurt, seen from the
/// visor; only hidden ones → it stands and searches;</item>
/// <item>an attack when its pause is over (GameServerGuardianGiantsAttacks.cs), else it walks toward the target — never past
/// <see cref="GuardianGiantRules.LeashRange"/> from its resting place, up steps of <see cref="GuardianGiantRules.StepHeight"/>,
/// hopping up or down to <see cref="GuardianGiantRules.HopHeight"/>, never into deep water, lava or down a cliff.</item>
/// </list>
/// It never flinches on the server and never changes a block while it stands.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Seconds without a player it may fight in range before it walks home.</summary>
    private const double GuardianGiantLeashSeconds = 8.0;

    /// <summary>The status message's beat while awake (plus one on every move change).</summary>
    private const double GuardianGiantStatusSeconds = 0.5;

    /// <summary>The planet-enemy list's beat while it moves (the machines' 7.5 Hz).</summary>
    private const double GuardianGiantSyncSeconds = 0.125;

    /// <summary>Degrees per second it turns.</summary>
    private const float GuardianGiantTurnRate = 120f;

    /// <summary>Beyond this flat distance (and within <see cref="GuardianGiantRules.JumpRange"/>) it leaps at the target.</summary>
    private const float GuardianGiantJumpBand = 12f;

    /// <summary>Seconds between its two summons at the least.</summary>
    private const double GuardianGiantSummonGap = 25.0;

    /// <summary>How far a hop carries it.</summary>
    private const float GuardianGiantHopLength = 3f;

    /// <summary>The arcs' peak heights over the straight line: a hop, the jump attack.</summary>
    private const float GuardianGiantHopPeak = 2.5f, GuardianGiantJumpPeak = 6f;

    /// <summary>Seconds a walk home may stay stuck before it is set down at its resting place.</summary>
    private const double GuardianGiantStuckSeconds = 4.0;

    /// <summary>The entity's <c>Scale</c>: only the server's aim check reads it (a body this big is hit from further off the
    /// centre line); the client draws the giant from its own body.</summary>
    private const float GuardianGiantAimScale = 3f;

    /// <summary>One awake giant (runtime only).</summary>
    private sealed class GuardianGiantFight
    {
        public GuardianGiantFight(GuardianGiantRecord rec) => Rec = rec;

        public GuardianGiantRecord Rec { get; }

        public CombatEntity? Entity;
        public double RiseAt;
        public GuardianGiantMove Move;
        public double MoveStart;
        public bool Landed;
        public Vector3f Aim;
        public Vector3f From;
        public Vector3f Rest;
        public bool Rage;
        public int Summons;
        public double LastSummonAt;
        public double NextAttackAt;
        public double NextStatusAt;
        public double NextSyncAt;
        public bool Moved;
        public double LonelySince = -1;
        public double StuckSince = -1;
        public bool Returning;
        public int Turn;
        public int CorpseX, CorpseY, CorpseZ, CorpseFacing;

        /// <summary>Who fought it: hit it within its fight range, or was hit by it — each gets a loot crate.</summary>
        public readonly HashSet<string> Fighters = new(System.StringComparer.Ordinal);

        public readonly List<string> SummonIds = new();

        // The last ground probe (the walk asks the same column over and over).
        public int ProbeX = int.MinValue, ProbeZ = int.MinValue, ProbeRef = int.MinValue, ProbeFeet;
        public bool ProbeOk;
    }

    /// <summary>One tick of the awake giant: the rise once its lights are on, the arcs of a hop or a leap, the move's impact on
    /// time, the end of a collapse or a lying down, otherwise the presence and the next move — then the streams.</summary>
    private void TickGuardianGiantFight(GuardianGiantWorld gw, GuardianGiantFight f, double dt)
    {
        double now = SnapshotTime;
        if (f.Entity is not { } e)
        {
            if (now >= f.RiseAt)
            {
                RiseGuardianGiant(f, now);
            }

            return;
        }

        if (!_planetEnemies.Contains(e))
        {
            LayGuardianGiantToRest(gw, f, speak: false); // something else took it (the world's machines were cleared)
            return;
        }

        var world = _worlds.Active;
        double elapsed = now - f.MoveStart;
        var spec = GuardianGiantRules.Spec(f.Move);
        CarryGuardianGiantThroughMove(f, elapsed);
        if (spec.Impact >= 0f && !f.Landed && elapsed >= spec.Impact)
        {
            f.Landed = true;
            LandGuardianGiantMove(f);
            if (!ReferenceEquals(_worlds.Active, world) || !ReferenceEquals(gw.Fight, f))
            {
                return; // a blow carried its victim to another world, or the fight ended
            }
        }

        bool running = spec.Duration > 0f && elapsed < spec.Duration;
        if (f.Move is GuardianGiantMove.Collapse or GuardianGiantMove.LieDown)
        {
            if (!running)
            {
                if (f.Move == GuardianGiantMove.Collapse) { FinishGuardianGiantDefeat(gw, f); } else { LayGuardianGiantToRest(gw, f, speak: true); }
                return;
            }
        }
        else
        {
            UpdateGuardianGiantPresence(f, now);
            if (!running)
            {
                ChooseGuardianGiantMove(f, now, dt);
            }
        }

        StreamGuardianGiant(f, now);
    }

    /// <summary>The rise: the lying body's own blocks go, the boss stands at the anchor's column facing the way the body was
    /// turned, with the data's hull; the record is awake (a restart lays it down again, lit).</summary>
    private void RiseGuardianGiant(GuardianGiantFight f, double now)
    {
        var rec = f.Rec;
        RemoveGuardianGiantBody(rec);
        var root = GuardianGiantStandingRoot(rec);
        var e = NewEnemy(GuardianGiantRules.EnemyKey, EnemyTiers.Normal, CombatEntityKind.GuardianGiant, root);
        if (e.HullMax <= 0f)
        {
            e.Hull = e.HullMax = GuardianGiantRules.DefaultHull;
        }

        e.DamagePerSecond = 0f;
        e.Scale = GuardianGiantAimScale;
        e.Yaw = GuardianGiantYaw(rec.Facing * 90f);
        _planetEnemies.Add(e);
        f.Entity = e;
        f.Rest = root;
        f.NextAttackAt = now + GuardianGiantRules.Spec(GuardianGiantMove.Rise).Duration + 1.0;
        rec.State = GuardianGiantRecord.Awake;
        _repo.SaveMetadata(_meta);
        StartGuardianGiantMove(f, GuardianGiantMove.Rise, root, now);
        BroadcastPlanetPois(); // the marker turns "awake"
    }

    /// <summary>Who is here to fight: someone it may hurt on foot within its fight range keeps it fighting; nobody for
    /// <see cref="GuardianGiantLeashSeconds"/> — or the machines rule switched off — sends it home.</summary>
    private void UpdateGuardianGiantPresence(GuardianGiantFight f, double now)
    {
        CollectGuardianGiantPlayers();
        bool anyone = false;
        const float r = GuardianGiantRules.FightRange;
        foreach (var s in _guardianGiantOnFoot)
        {
            anyone |= GuardianGiantMayTarget(s) && FlatDistSq(s.State.Position, f.Entity!.Position) <= r * r;
        }

        if (anyone && GuardianGiantsMayWake)
        {
            f.LonelySince = -1;
            f.Returning = false;
        }
        else if (f.LonelySince < 0)
        {
            f.LonelySince = now;
        }
        else if (now - f.LonelySince >= GuardianGiantLeashSeconds || !GuardianGiantsMayWake)
        {
            f.Returning = true;
        }
    }

    /// <summary>A player the giant may aim at: on foot, alive, not waiting for a respawn choice, not one hostiles ignore
    /// (cloaked, god mode, Sandbox). A companion's ward does not hold it — it is no network machine.</summary>
    private bool GuardianGiantMayTarget(PlayerSession s)
        => !s.State.AboardShip && s.State.Health > 0f && !s.Respawn.ChoicePending && !HostilesIgnore(s.State);

    /// <summary>The next move of a giant whose move ended or loops: home, the rage, an attack, the walk, the search or idle.</summary>
    private void ChooseGuardianGiantMove(GuardianGiantFight f, double now, double dt)
    {
        var e = f.Entity!;
        if (f.Returning)
        {
            ReturnGuardianGiantStep(f, now, dt);
            return;
        }

        if (!f.Rage && e.Hull <= e.HullMax * GuardianGiantRules.RageAt)
        {
            f.Rage = true;
            StartGuardianGiantMove(f, GuardianGiantMove.Enrage, e.Position, now);
            foreach (var s in GuardianGiantAudience(e.Position))
            {
                SendGuardianGiantVega(s, GuardianGiantVegaMoment.Rage);
            }

            return;
        }

        var target = PickGuardianGiantTarget(f, out bool seen);
        if (target is null)
        {
            LoopGuardianGiant(f, GuardianGiantMove.Idle, now);
            return;
        }

        var at = Unwrapped(e.Position, target.State.Position);
        TurnGuardianGiantToward(f, at, dt);
        if (!seen)
        {
            LoopGuardianGiant(f, GuardianGiantMove.Search, now);
            return;
        }

        float d = (float)System.Math.Sqrt(FlatDistSq(e.Position, at));
        if (now >= f.NextAttackAt && TryStartGuardianGiantAttack(f, at, d, now))
        {
            return;
        }

        if (d > GuardianGiantRules.StompRadius * 0.6f && WalkGuardianGiantToward(f, at, dt, now, leashed: true))
        {
            return;
        }

        LoopGuardianGiant(f, GuardianGiantMove.Idle, now);
    }

    /// <summary>The nearest player in fight range it may hurt and can see from its visor (<paramref name="seen"/>); else the
    /// nearest hidden one (it searches), else nobody.</summary>
    private PlayerSession? PickGuardianGiantTarget(GuardianGiantFight f, out bool seen)
    {
        var e = f.Entity!;
        var visor = GuardianGiantVisor(e);
        PlayerSession? best = null, hidden = null;
        double bestD = GuardianGiantRules.FightRange * GuardianGiantRules.FightRange, hiddenD = bestD;
        foreach (var s in _guardianGiantOnFoot)
        {
            double d = FlatDistSq(e.Position, s.State.Position);
            if (!GuardianGiantMayTarget(s) || (d > bestD && d > hiddenD))
            {
                continue;
            }

            if (HasLineOfSight(visor, s.State.Position, 0f, SightEyeHeight, skipToCell: false))
            {
                if (d <= bestD)
                {
                    bestD = d;
                    best = s;
                }
            }
            else if (d <= hiddenD)
            {
                hiddenD = d;
                hidden = s;
            }
        }

        seen = best is not null;
        return best ?? hidden;
    }

    /// <summary>Starts a move: its clock, its aim, the attack pause behind an attack, the summon count — and tells the clients
    /// (the status and the enemy list).</summary>
    private void StartGuardianGiantMove(GuardianGiantFight f, GuardianGiantMove move, Vector3f aim, double now)
    {
        var e = f.Entity!;
        f.Move = move;
        f.MoveStart = now;
        f.Landed = false;
        f.Aim = aim;
        f.From = e.Position;
        if (GuardianGiantRules.IsAttack(move))
        {
            f.NextAttackAt = now + GuardianGiantRules.Spec(move).Duration
                             + (f.Rage ? GuardianGiantRules.RageAttackPause : GuardianGiantRules.AttackPause);
        }

        if (move == GuardianGiantMove.Summon)
        {
            f.Summons++;
            f.LastSummonAt = now;
        }

        SendGuardianGiantStatus(f, now);
        BroadcastPlanetEnemies();
    }

    /// <summary>Switches to a looping move unless it already runs.</summary>
    private void LoopGuardianGiant(GuardianGiantFight f, GuardianGiantMove move, double now)
    {
        if (f.Move != move)
        {
            StartGuardianGiantMove(f, move, f.Entity!.Position, now);
        }
    }

    /// <summary>A heading in degrees folded into [0, 360).</summary>
    private static float GuardianGiantYaw(float degrees)
    {
        float y = degrees % 360f;
        return y < 0f ? y + 360f : y;
    }

    /// <summary>Turns toward a point (in the giant's frame) at <see cref="GuardianGiantTurnRate"/>.</summary>
    private static void TurnGuardianGiantToward(GuardianGiantFight f, Vector3f at, double dt)
    {
        var e = f.Entity!;
        float dx = at.X - e.Position.X, dz = at.Z - e.Position.Z;
        if (dx * dx + dz * dz < 0.01f)
        {
            return;
        }

        float want = (float)(System.Math.Atan2(dx, dz) * 180.0 / System.Math.PI);
        float diff = GuardianGiantYaw(want - e.Yaw);
        if (diff > 180f)
        {
            diff -= 360f;
        }

        float step = (float)(GuardianGiantTurnRate * dt);
        e.Yaw = GuardianGiantYaw(e.Yaw + System.Math.Clamp(diff, -step, step));
    }

    /// <summary>The visor's world point (the laser's start, the sight's eye): <see cref="GuardianGiantBody.Visor"/> turned by
    /// the heading (Unity yaw: local +Z is the front).</summary>
    private static Vector3f GuardianGiantVisor(CombatEntity e)
    {
        var (vx, vy, vz) = GuardianGiantBody.Visor;
        double a = e.Yaw * System.Math.PI / 180.0;
        float sin = (float)System.Math.Sin(a), cos = (float)System.Math.Cos(a);
        return new Vector3f(e.Position.X + (vx * cos) + (vz * sin), e.Position.Y + vy, e.Position.Z - (vx * sin) + (vz * cos));
    }

    /// <summary>One step toward <paramref name="goal"/> (in the giant's frame): a walk on ground within a step, a hop over
    /// a higher step, nothing into deep water, lava, down a cliff, off its leash (when <paramref name="leashed"/>) or onto
    /// unloaded ground. True when it moved or started a hop.</summary>
    private bool WalkGuardianGiantToward(GuardianGiantFight f, Vector3f goal, double dt, double now, bool leashed)
    {
        var e = f.Entity!;
        float dx = goal.X - e.Position.X, dz = goal.Z - e.Position.Z;
        float d = (float)System.Math.Sqrt((dx * dx) + (dz * dz));
        if (d < 0.05f)
        {
            return false;
        }

        float speed = f.Rage ? GuardianGiantRules.RageWalkSpeed : GuardianGiantRules.WalkSpeed;
        float step = (float)System.Math.Min(d, speed * dt);
        float nx = e.Position.X + (dx / d * step), nz = e.Position.Z + (dz / d * step);
        if (leashed && FlatDistSq(new Vector3f(nx, 0f, nz), f.Rest) > GuardianGiantRules.LeashRange * GuardianGiantRules.LeashRange)
        {
            return false;
        }

        int y = (int)System.Math.Floor(e.Position.Y);
        if (!GuardianGiantGroundAt(f, nx, nz, y, out int feet) || System.Math.Abs(feet - y) > GuardianGiantRules.HopHeight)
        {
            return false;
        }

        if (System.Math.Abs(feet - y) > GuardianGiantRules.StepHeight)
        {
            return StartGuardianGiantHop(f, dx / d, dz / d, now, leashed);
        }

        e.Position = GuardianGiantWrapped(nx, feet, nz);
        f.Moved = true;
        LoopGuardianGiant(f, GuardianGiantMove.Walk, now);
        return true;
    }

    /// <summary>A hop of <see cref="GuardianGiantHopLength"/> along (ux, uz) onto ground within the hop height. False when the
    /// landing is no good.</summary>
    private bool StartGuardianGiantHop(GuardianGiantFight f, float ux, float uz, double now, bool leashed)
    {
        var e = f.Entity!;
        float lx = e.Position.X + (ux * GuardianGiantHopLength), lz = e.Position.Z + (uz * GuardianGiantHopLength);
        int y = (int)System.Math.Floor(e.Position.Y);
        bool inLeash = !leashed || FlatDistSq(new Vector3f(lx, 0f, lz), f.Rest) <= GuardianGiantRules.LeashRange * GuardianGiantRules.LeashRange;
        if (!inLeash || !GuardianGiantGroundAt(f, lx, lz, y, out int feet) || System.Math.Abs(feet - y) > GuardianGiantRules.HopHeight)
        {
            return false;
        }

        StartGuardianGiantMove(f, GuardianGiantMove.Hop, GuardianGiantWrapped(lx, feet, lz), now);
        return true;
    }

    /// <summary>A world point with its X and Z folded onto this world's circumference.</summary>
    private Vector3f GuardianGiantWrapped(float x, float y, float z)
        => new((float)WorldConstants.WrapX((double)x, _world.Circumference), y, (float)WorldConstants.WrapZ((double)z, _world.Circumference));

    /// <summary>The arc of a hop or a leap between its take-off and its landing (the landing itself is the impact's).</summary>
    private void CarryGuardianGiantThroughMove(GuardianGiantFight f, double elapsed)
    {
        float t0, t1, peak;
        if (f.Move == GuardianGiantMove.Hop)
        {
            var spec = GuardianGiantRules.Spec(GuardianGiantMove.Hop);
            (t0, t1, peak) = (spec.Warning, spec.Impact, GuardianGiantHopPeak);
        }
        else if (f.Move == GuardianGiantMove.JumpAttack)
        {
            (t0, t1, peak) = (GuardianGiantRules.JumpTakeOff, GuardianGiantRules.Spec(GuardianGiantMove.JumpAttack).Impact, GuardianGiantJumpPeak);
        }
        else
        {
            return;
        }

        if (f.Landed || elapsed < t0)
        {
            return;
        }

        float t = System.Math.Clamp((float)((elapsed - t0) / (t1 - t0)), 0f, 1f);
        var to = Unwrapped(f.From, f.Aim);
        float y = f.From.Y + ((to.Y - f.From.Y) * t) + (4f * peak * t * (1f - t));
        f.Entity!.Position = GuardianGiantWrapped(f.From.X + ((to.X - f.From.X) * t), y, f.From.Z + ((to.Z - f.From.Z) * t));
        f.Moved = true;
    }

    /// <summary>The walk home: straight to the resting place; there (or unwatched, or stuck for
    /// <see cref="GuardianGiantStuckSeconds"/>) it turns the way it lay and lies down.</summary>
    private void ReturnGuardianGiantStep(GuardianGiantFight f, double now, double dt)
    {
        var e = f.Entity!;
        var rest = Unwrapped(e.Position, f.Rest);
        bool arrived = FlatDistSq(e.Position, rest) <= 0.75f * 0.75f;
        bool stuck = f.StuckSince >= 0 && now - f.StuckSince >= GuardianGiantStuckSeconds;
        if (arrived || stuck || !AnyoneSeesGuardianGiant(e.Position))
        {
            e.Position = f.Rest;
            e.Yaw = GuardianGiantYaw(f.Rec.Facing * 90f);
            f.StuckSince = -1;
            f.Moved = true;
            StartGuardianGiantMove(f, GuardianGiantMove.LieDown, f.Rest, now);
            return;
        }

        TurnGuardianGiantToward(f, rest, dt);
        if (WalkGuardianGiantToward(f, rest, dt, now, leashed: false))
        {
            f.StuckSince = -1;
            return;
        }

        if (f.StuckSince < 0)
        {
            f.StuckSince = now;
        }

        LoopGuardianGiant(f, GuardianGiantMove.Idle, now);
    }

    /// <summary>The ground a step lands on (cached per column): a floor within the hop height of <paramref name="refY"/>, with
    /// no deep water and no lava on it. No-load reads — unloaded ground is no ground.</summary>
    private bool GuardianGiantGroundAt(GuardianGiantFight f, float x, float z, int refY, out int feet)
    {
        int cx = (int)System.Math.Floor(x), cz = (int)System.Math.Floor(z);
        if (cx != f.ProbeX || cz != f.ProbeZ || refY != f.ProbeRef)
        {
            f.ProbeX = cx;
            f.ProbeZ = cz;
            f.ProbeRef = refY;
            f.ProbeOk = GuardianGiantFloor(cx, cz, refY, GuardianGiantRules.HopHeight, out f.ProbeFeet) && !GuardianGiantWet(cx, cz, f.ProbeFeet);
        }

        feet = f.ProbeFeet;
        return f.ProbeOk;
    }

    /// <summary>The highest floor of column (x, z) within <paramref name="reach"/> rows of <paramref name="refY"/>: a colliding
    /// block that is no fluid under two cells a body passes (leaves and plants pass; a fluid passes too — the wet test is
    /// separate). No-load reads.</summary>
    private bool GuardianGiantFloor(int x, int z, int refY, int reach, out int feet)
    {
        for (int y = refY + reach + 1; y >= refY - reach - 1; y--)
        {
            var below = _world.GetBlockIfLoaded(new Vector3i(x, y - 1, z));
            if (below.IsAir || IsFluid(below.Value) || !IsCollidingBlock(below, fluidsPass: true, foliagePasses: true)
                || IsCollidingCellIfLoaded(x, y, z, foliagePasses: true) || IsCollidingCellIfLoaded(x, y + 1, z, foliagePasses: true))
            {
                continue;
            }

            feet = y;
            return true;
        }

        feet = refY;
        return false;
    }

    /// <summary>Whether feet at (x, y, z) would stand in deep water or in lava.</summary>
    private bool GuardianGiantWet(int x, int z, int feet)
        => MachineWaterDepth(x, z, feet) > MachineWadeDepth
           || (_lavaId != 0 && _world.GetBlockIfLoaded(new Vector3i(x, feet, z)).Value == _lavaId);

    /// <summary>The standing root at the anchor's column (the point between the feet): the anchor's X/Z, on the highest floor
    /// of the four columns around it.</summary>
    private Vector3f GuardianGiantStandingRoot(GuardianGiantRecord rec)
    {
        int best = int.MinValue;
        for (int dx = -1; dx <= 0; dx++)
        {
            for (int dz = -1; dz <= 0; dz++)
            {
                if (GuardianGiantFloor(rec.X + dx, rec.Z + dz, rec.Y, GuardianGiantRules.HopHeight, out int feet))
                {
                    best = System.Math.Max(best, feet);
                }
            }
        }

        return new Vector3f(rec.X, best == int.MinValue ? rec.Y + GuardianGiantBody.LyingBottom + 1 : best, rec.Z);
    }

    // ---------------------------------------------------------------- the streams

    /// <summary>The status twice a second, the enemy list at the machines' beat while it moves.</summary>
    private void StreamGuardianGiant(GuardianGiantFight f, double now)
    {
        if (now >= f.NextStatusAt)
        {
            SendGuardianGiantStatus(f, now);
        }

        if (f.Moved && now >= f.NextSyncAt)
        {
            f.Moved = false;
            f.NextSyncAt = now + GuardianGiantSyncSeconds;
            BroadcastPlanetEnemies();
        }
    }

    /// <summary>Sends the status to everyone within <see cref="GuardianGiantRules.StatusRange"/>; <paramref name="over"/> —
    /// the fight is over (<see cref="GuardianGiantMove.None"/>, no entity).</summary>
    private void SendGuardianGiantStatus(GuardianGiantFight f, double now, bool over = false)
    {
        f.NextStatusAt = now + GuardianGiantStatusSeconds;
        var msg = GuardianGiantStatusOf(f, now, over);
        foreach (var s in GuardianGiantAudience(f.Entity?.Position ?? GuardianGiantCentre(f.Rec)))
        {
            Send(s, msg);
        }
    }

    /// <summary>The status of a fight: the move and its clock, the aim, the boss bar, and the lying pose — the corpse's for a
    /// collapse, the resting place's otherwise.</summary>
    private static GuardianGiantStatus GuardianGiantStatusOf(GuardianGiantFight f, double now, bool over)
    {
        var rec = f.Rec;
        bool fall = f.Move == GuardianGiantMove.Collapse;
        var e = f.Entity;
        return new GuardianGiantStatus
        {
            SiteId = rec.SiteId,
            EntityId = over || e is null ? string.Empty : e.Id,
            Move = over ? (byte)GuardianGiantMove.None : (byte)f.Move,
            MoveElapsed = over ? 0f : (float)(now - f.MoveStart),
            ServerTime = now,
            AimX = f.Aim.X,
            AimY = f.Aim.Y,
            AimZ = f.Aim.Z,
            Rage = f.Rage,
            Hull = e is null ? 0f : System.Math.Max(0f, e.Hull),
            HullMax = e?.HullMax ?? 0f,
            AnchorX = fall ? f.CorpseX : rec.X,
            AnchorY = fall ? f.CorpseY : rec.Y,
            AnchorZ = fall ? f.CorpseZ : rec.Z,
            Facing = (byte)(fall ? f.CorpseFacing : rec.Facing),
            Seed = rec.Seed,
        };
    }

    /// <summary>The end of a fight without a defeat: the body lies at its resting place again, lit, the boss is gone, the
    /// record is lit (healed — it rises with a full hull). <paramref name="speak"/>: VEGA tells the players near (it lay down
    /// itself; not when something else took it away).</summary>
    private void LayGuardianGiantToRest(GuardianGiantWorld gw, GuardianGiantFight f, bool speak)
    {
        var rec = f.Rec;
        var at = f.Entity?.Position ?? GuardianGiantCentre(rec);
        StampGuardianGiantBody(rec.X, rec.Y, rec.Z, rec.Facing, rec.Seed, lit: true, overwrite: false);
        if (f.Entity is { } e && _planetEnemies.Remove(e))
        {
            BroadcastPlanetEnemies();
        }

        rec.State = GuardianGiantRecord.Lit;
        _repo.SaveMetadata(_meta);
        SendGuardianGiantStatus(f, SnapshotTime, over: true);
        gw.Fight = null;
        if (speak)
        {
            foreach (var s in GuardianGiantAudience(at))
            {
                SendGuardianGiantVega(s, GuardianGiantVegaMoment.LieDown);
            }
        }

        BroadcastPlanetPois();
        _log.Info($"The guardian giant '{rec.SiteId}' lay down again at {rec.X},{rec.Y},{rec.Z}.");
    }

    /// <summary>A record left awake (the server stopped mid-fight): the body lies at its resting place again, lit.</summary>
    private void RestoreGuardianGiant(GuardianGiantRecord rec)
    {
        StampGuardianGiantBody(rec.X, rec.Y, rec.Z, rec.Facing, rec.Seed, lit: true, overwrite: false);
        rec.State = GuardianGiantRecord.Lit;
        _repo.SaveMetadata(_meta);
        _log.Info($"The guardian giant '{rec.SiteId}' was awake when the world stopped — it lies at its resting place again.");
    }
}
