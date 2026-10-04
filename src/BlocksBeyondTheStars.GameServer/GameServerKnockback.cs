// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The push of the shock gloves (#2278): a hit with a tool whose <see cref="ToolProperties.Knockback"/> is above zero shoves
/// a surviving creature, planet machine or bandit straight away from the attacker, and a tool with
/// <see cref="ToolProperties.StaggerSeconds"/> leaves it dazed for a moment. Each of the three kinds moves by its own rules
/// on the server (creatures through the locomotion step with the full creature collision, machines on the machine ground
/// probe, bandits with the bandit terrain gate), so each gets its own sweep with exactly those rules: the push is tested
/// every <see cref="KnockbackRules.SweepStep"/> and stops at the first step its own movement would refuse — a wall, a ship
/// hull, an energy fence, a shut door, a cliff edge over three blocks, and for land animals water and lava. Nothing ends up
/// inside a block, and a push is never a way to throw an animal off a cliff.
/// <para>Who is never pushed: players (there is no on-foot PvP and the parents' guide promises that friends cannot harm
/// each other), companions and pets (<see cref="ProtectedFromPlayers"/>), and the giants — far too big; a creature held in
/// stasis stays put too (it is still dazed), and a pushed machine never ends up in a player's body. The push needs no
/// new message: the positions travel in the creature and planet-enemy lists as always, and the daze rides on the
/// additive <c>Staggered</c> flag of both.</para>
/// <para>While dazed a creature neither moves nor bites (its vertical state still runs, so the little hop lands), a
/// machine neither moves nor hurts (its aura pauses), a bandit neither moves nor shoots. The rest of the reaction is the
/// ordinary hit's: the herd startles, a territorial animal is provoked, a pushed bandit counts as refusing its hold-up.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>True while <paramref name="e"/> is dazed by a push.</summary>
    private bool IsStaggered(CombatEntity e) => _uptime < e.StaggerUntil;

    /// <summary>True exactly once when a daze has run out (and clears it) — the caller broadcasts, so the clients stop
    /// drawing the stars even for a target that stands still afterwards.</summary>
    private bool StaggerJustEnded(CombatEntity e)
    {
        if (e.StaggerUntil <= 0.0 || _uptime < e.StaggerUntil)
        {
            return false;
        }

        e.StaggerUntil = 0.0;
        return true;
    }

    /// <summary>Applies the held tool's push and daze to a target that survived the hit. Returns true when the target
    /// moved or was dazed (the caller broadcasts the list anyway).</summary>
    private bool ApplyKnockback(PlayerSession session, CombatEntity target, ToolProperties tool, bool isCreature, Vector3f aimDir)
    {
        if (tool.Knockback <= 0f && tool.StaggerSeconds <= 0f)
        {
            return false;
        }

        if (isCreature && (ProtectedFromPlayers(target) || target.IsGiant))
        {
            return false; // a pet is never shoved (#2281), a giant is far too big
        }

        float mass = MassFactor(target, isCreature);
        if (mass <= 0f)
        {
            return false;
        }

        bool changed = false;
        float distance = tool.Knockback * mass;
        if (distance > 0f && PushDirection(session.State.Position, target.Position, aimDir, out float dirX, out float dirZ))
        {
            changed = isCreature ? KnockCreature(target, dirX, dirZ, distance)
                : target.IsBandit ? KnockBandit(target, dirX, dirZ, distance)
                : KnockMachine(target, dirX, dirZ, distance);
        }

        if (tool.StaggerSeconds > 0f && KnockbackRules.CanStagger(_uptime, target.StaggerImmuneUntil))
        {
            target.StaggerUntil = _uptime + tool.StaggerSeconds;
            target.StaggerImmuneUntil = target.StaggerUntil + KnockbackRules.StaggerImmuneSeconds;
            changed = true;
        }

        return changed;
    }

    /// <summary>How far this target flies per block of knockback: creatures by body size (giants 0), the heavy hunter robot
    /// a bit less than the others, bandits a little less than a small animal.</summary>
    private float MassFactor(CombatEntity target, bool isCreature)
    {
        if (isCreature)
        {
            float size = _speciesById.TryGetValue(target.SpeciesId, out var sp) ? sp.Size : 1f;
            return KnockbackRules.CreatureMass(size, target.IsGiant);
        }

        if (target.IsBandit)
        {
            return KnockbackRules.BanditMass;
        }

        return target.Kind == CombatEntityKind.AlienMonster ? KnockbackRules.ToughMachineMass : 1f;
    }

    /// <summary>The horizontal unit direction from the attacker to the target, measured the short way round the world
    /// seams; the attacker's aim when the two stand on the same spot. False when neither gives a direction — also for
    /// a length that is not finite (a huge aim overflows the square), so a push never carries a NaN.</summary>
    private bool PushDirection(Vector3f attacker, Vector3f target, Vector3f aimDir, out float dirX, out float dirZ)
    {
        var from = Unwrapped(target, attacker); // the attacker in the target's local frame
        float dx = target.X - from.X, dz = target.Z - from.Z;
        float len = (float)System.Math.Sqrt((dx * dx) + (dz * dz));
        if (len < 0.05f)
        {
            dx = aimDir.X;
            dz = aimDir.Z;
            len = (float)System.Math.Sqrt((dx * dx) + (dz * dz));
        }

        if (len < 1e-4f || !float.IsFinite(len))
        {
            dirX = dirZ = 0f;
            return false;
        }

        dirX = dx / len;
        dirZ = dz / len;
        return true;
    }

    /// <summary>Pushes a wild creature through every barrier its own step honours (<see cref="StepBlocked"/> with the
    /// terrain gate: ship, fence, shut doors, the swept body, rises, drops, water and lava). A one-block rise is taken
    /// only with head room, like a ledge it would climb. A walker or crawler on the ground makes a small hop. A creature
    /// held in stasis is not moved at all: its tick skips the vertical step while frozen, so one shoved over a ledge
    /// would hang in the air until the stasis ends (the daze still applies).</summary>
    private bool KnockCreature(CombatEntity c, float dirX, float dirZ, float distance)
    {
        if (c.FrozenTimer > 0 || !_speciesById.TryGetValue(c.SpeciesId, out var sp))
        {
            return false;
        }

        var motion = EffectiveMotion(c, sp);
        var cur = c.Position;
        bool moved = false;
        int steps = KnockbackRules.Steps(distance);
        for (int i = 1; i <= steps; i++)
        {
            float len = KnockbackRules.StepLength(distance, i);
            var stepped = new Vector3f(cur.X + (dirX * len), cur.Y, cur.Z + (dirZ * len));
            var cand = PreviewStep(sp, motion, cur, stepped, out bool needsRise, out int riseFeet, out int? nextFeet);
            if (needsRise && (riseFeet - (int)System.Math.Floor(cur.Y) > CreatureMotion.StepUpLimit(motion)
                || CreatureBodyBlocked(sp, new Vector3f(cur.X, riseFeet, cur.Z))))
            {
                break; // a wall, or a ledge without room above it
            }

            if (StepBlocked(c, sp, motion, cur, cand, needsRise, terrainGates: true, nextFeet))
            {
                break;
            }

            cur = needsRise ? cand : new Vector3f(cand.X, cur.Y, cand.Z);
            moved = true;
        }

        if (moved)
        {
            c.Position = new Vector3f((float)WorldConstants.WrapX(cur.X, _world.Circumference), cur.Y,
                (float)WorldConstants.WrapZ(cur.Z, _world.Circumference));
            c.Vert.ClimbTargetY = 0f;     // whatever ledge it was hauling up to is behind it now
            c.Loco.ModeTimer = 0f;        // a fresh heading once the daze is over
            c.NextBodyCheckAt = 0.0;      // re-validate the body on the next awake tick (#1357), just in case
        }

        // The little hop that makes the shove read as one (the client integrates the arc). Not mid-air, and only for the
        // ground classes (a frozen creature never got this far).
        if (CreatureMotion.IsGroundBound(motion) && !c.Vert.Airborne)
        {
            VerticalMotion.Launch(ref c.Vert, VerticalMotion.ImpulseFor(VerticalMotion.Gravity(_gravityFactor), KnockbackRules.HopHeight));
            moved = true;
        }

        return moved;
    }

    /// <summary>Pushes a planet machine by the rules of its own walk (<see cref="MovePlanetEnemy"/>): the real ground of
    /// the next column, a step up of two blocks at most (a drone's hover clears three), a drop of three at most, no ship
    /// hull and no energy fence. A drone keeps its hover height; machines cross water on the noise surface as always. Like
    /// its walk it never steps into a player's body (#749): the sweep stops before a step that would bring it within
    /// <see cref="EnemyStopRange"/> of a player — the attacker's friend standing behind it, say.</summary>
    private bool KnockMachine(CombatEntity enemy, float dirX, float dirZ, float distance)
    {
        bool drone = enemy.Kind == CombatEntityKind.ScanDrone;
        int hover = drone ? ScanDroneHover : 0;
        var cur = enemy.Position;
        bool moved = false;
        int steps = KnockbackRules.Steps(distance);
        for (int i = 1; i <= steps; i++)
        {
            float len = KnockbackRules.StepLength(distance, i);
            float nx = (float)WorldConstants.WrapX(cur.X + (dirX * len), _world.Circumference);
            float nz = (float)WorldConstants.WrapZ(cur.Z + (dirZ * len), _world.Circumference);
            int refY = (int)System.Math.Floor(cur.Y) - hover;
            int prevGround = GroundFeetYAt((int)System.Math.Floor(cur.X), (int)System.Math.Floor(cur.Z), refY);
            if (!MachineGroundAt((int)System.Math.Floor(nx), (int)System.Math.Floor(nz), refY, out int groundY)
                || groundY - prevGround > (drone ? ScanDroneHover - 1 : MachineStepUp)
                || prevGround - groundY > 3)
            {
                break;
            }

            var cand = new Vector3f(nx, groundY + hover, nz);
            if (EntityBlockedByShip(cand) || BlockedByEnergyFence(cur, cand) || PushIntoPlayer(cur, cand))
            {
                break;
            }

            cur = cand;
            moved = true;
        }

        if (moved)
        {
            enemy.Position = cur;
            enemy.Loco.ModeTimer = 0f;
        }

        return moved;
    }

    /// <summary>True when a pushed machine's step from <paramref name="from"/> to <paramref name="to"/> ends within
    /// <see cref="EnemyStopRange"/> of a player on this world and nearer to them than before — the distance its own walk
    /// keeps (#749). Moving away from a player it already stands close to stays allowed, so the attacker's own push
    /// (always straight away from them) is never cut short.</summary>
    private bool PushIntoPlayer(Vector3f from, Vector3f to)
    {
        const double stopSq = EnemyStopRange * EnemyStopRange;
        foreach (var s in JoinedInActiveWorld())
        {
            if (InSpace(s.State.PlayerId))
            {
                continue;
            }

            double toSq = WrapDistSq(s.State.Position, to);
            if (toSq <= stopSq && toSq < WrapDistSq(s.State.Position, from))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Pushes a bandit by the rules of its own walk (<see cref="MoveBandit"/>): the real ground, a drop of three
    /// at most, one block up, no body inside a block, no feet on water or lava, no ship hull, no energy fence — and a base
    /// scout stays fenced out of the zone it came to look at.</summary>
    private bool KnockBandit(CombatEntity bandit, float dirX, float dirZ, float distance)
    {
        var cur = bandit.Position;
        bool moved = false;
        int steps = KnockbackRules.Steps(distance);
        for (int i = 1; i <= steps; i++)
        {
            float len = KnockbackRules.StepLength(distance, i);
            float nx = (float)WorldConstants.WrapX(cur.X + (dirX * len), _world.Circumference);
            float nz = (float)WorldConstants.WrapZ(cur.Z + (dirZ * len), _world.Circumference);
            int refY = (int)System.Math.Floor(cur.Y);
            int prevGround = GroundFeetYAt((int)System.Math.Floor(cur.X), (int)System.Math.Floor(cur.Z), refY);
            int groundY = GroundFeetYAt((int)System.Math.Floor(nx), (int)System.Math.Floor(nz), refY);
            if (System.Math.Abs(groundY - prevGround) > 3)
            {
                break;
            }

            var cand = new Vector3f(nx, groundY, nz);
            if (EntityBlockedByShip(cand) || BlockedByEnergyFence(cur, cand)
                || BanditStepBlockedByTerrain(cur, cand, prevGround, groundY) || ScoutStepBlocked(bandit, cand))
            {
                break;
            }

            cur = cand;
            moved = true;
        }

        if (moved)
        {
            bandit.Position = cur;
            bandit.Loco.ModeTimer = 0f;
        }

        return moved;
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test seam (#2278): whether the creature, planet machine or bandit with this id is dazed right now.</summary>
    public bool IsStaggeredForTest(string entityId)
    {
        foreach (var list in new[] { _creatures, _planetEnemies, _bandits })
        {
            foreach (var e in list)
            {
                if (e.Id == entityId)
                {
                    return IsStaggered(e);
                }
            }
        }

        return false;
    }

    /// <summary>Test seam (#2278): applies the push and daze of the player's held tool to an entity directly, past the
    /// attack's own checks — so a test can prove the knockback rule itself refuses pets and giants. True when the target
    /// moved or was dazed.</summary>
    public bool KnockbackForTest(string playerId, string entityId)
    {
        if (FindSessionByPlayerId(playerId) is not { } session)
        {
            return false;
        }

        var creature = _creatures.Find(e => e.Id == entityId);
        var target = creature ?? _planetEnemies.Find(e => e.Id == entityId) ?? _bandits.Find(e => e.Id == entityId);
        return target is not null && ApplyKnockback(session, target, ActiveTool(session.State), creature is not null, default);
    }
}
