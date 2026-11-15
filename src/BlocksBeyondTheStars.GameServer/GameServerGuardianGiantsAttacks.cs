// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The guardian giant's blows (#3676) — see GameServerGuardianGiantsFight.cs for when it strikes. Every attack is announced:
/// the move starts with its aim point in the status message and its <see cref="GuardianGiantMoveSpec.Warning"/> plays before
/// anything lands, and the damage lands exactly at <see cref="GuardianGiantMoveSpec.Impact"/>:
/// <list type="bullet">
/// <item><b>Stomp</b> (target within <see cref="GuardianGiantRules.StompRadius"/>) and <b>the leap</b> (target beyond
/// <see cref="GuardianGiantJumpBand"/>, within <see cref="GuardianGiantRules.JumpRange"/>; the landing is the aim): a shock
/// wave on the ground — <see cref="GuardianGiantRules.WaveDamage"/> by flat distance, nobody more than
/// <see cref="GuardianGiantWaveHeight"/> above or below the wave, a <see cref="GuardianGiantRules.WaveFx"/> for the clients'
/// push and shake.</item>
/// <item><b>Laser</b> / <b>Throw</b> (alternating, mid range): the beam from the visor toward the target's chest (the aim is
/// where it meets the first solid block, else its full reach) hurts every player within
/// <see cref="GuardianGiantRules.LaserWidth"/> of the beam with nothing solid between the visor and them; the rock splashes
/// at the aim point (the target's feet when it was thrown) for everyone within <see cref="GuardianGiantRules.ThrowRadius"/>
/// whom no wall shelters.</item>
/// <item><b>Summon</b> (from the rage on, at most <see cref="GuardianGiantRules.MaxSummons"/> times): two Guardian robots
/// beside it, the ordinary planet machines.</item>
/// </list>
/// The damage takes the planet machines' path (<see cref="HurtPlayer"/>: armour, god mode, the HUD's attacker marker, the
/// recovery) and <b>no attack ever changes a block</b>. Whom it hits counts as one of its fighters (a loot crate each).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A shock wave runs along the ground: nobody further than this above or below its centre feels it.</summary>
    private const float GuardianGiantWaveHeight = GuardianGiantRules.WaveHeight;

    /// <summary>The body's half-width for the aim check and the reach of a blow (a vertical capsule about the root).</summary>
    private const float GuardianGiantBodyRadius = 2f;

    /// <summary>The death line of a player its blow knocked out (the giants' line).</summary>
    private const string GuardianGiantDeathReason = "@srv.death.giant";

    /// <summary>Where its robots appear: (side, distance) along its own left-right axis — right and left first, then a little
    /// further out.</summary>
    private static readonly (float Side, float Dist)[] GuardianGiantSummonSpots = { (1f, 5f), (-1f, 5f), (1f, 7f), (-1f, 7f) };

    /// <summary>What happens at a move's impact.</summary>
    private void LandGuardianGiantMove(GuardianGiantFight f)
    {
        var e = f.Entity!;
        switch (f.Move)
        {
            case GuardianGiantMove.Stomp:
                GuardianGiantWave(f, e.Position, GuardianGiantRules.StompRadius, GuardianGiantRules.StompDamage);
                break;
            case GuardianGiantMove.JumpAttack:
                e.Position = f.Aim;
                f.Moved = true;
                GuardianGiantWave(f, f.Aim, GuardianGiantRules.JumpRadius, GuardianGiantRules.JumpDamage);
                break;
            case GuardianGiantMove.Hop:
                e.Position = f.Aim;
                f.Moved = true;
                break;
            case GuardianGiantMove.Laser:
                GuardianGiantLaser(f);
                break;
            case GuardianGiantMove.Throw:
                GuardianGiantRock(f);
                break;
            case GuardianGiantMove.Summon:
                SummonGuardianGiantRobots(f);
                break;
        }
    }

    /// <summary>The players a blow may reach: on this surface, on foot, alive — those hostiles ignore are passed by.</summary>
    private System.Collections.Generic.List<PlayerSession> GuardianGiantVictims()
    {
        var list = new System.Collections.Generic.List<PlayerSession>();
        foreach (var s in JoinedInActiveWorld())
        {
            var p = s.State;
            if (!s.Spectating && !p.AboardShip && !InSpace(p.PlayerId) && p.Health > 0f && !HostilesIgnore(p))
            {
                list.Add(s);
            }
        }

        return list;
    }

    /// <summary>A shock wave at <paramref name="centre"/>: the effect for everyone, the damage falling off with the flat distance
    /// for everyone on the ground near it. False when a knock-out carried a victim to another world (stop there).</summary>
    private bool GuardianGiantWave(GuardianGiantFight f, Vector3f centre, float radius, float full)
    {
        BroadcastToWorld(new WorldFx { Kind = GuardianGiantRules.WaveFx, X = centre.X, Y = centre.Y, Z = centre.Z, Strength = 1f, Radius = radius });
        var world = _worlds.Active;
        foreach (var s in GuardianGiantVictims())
        {
            var p = Unwrapped(centre, s.State.Position);
            float dx = p.X - centre.X, dz = p.Z - centre.Z;
            float amount = GuardianGiantRules.WaveDamage(full, radius, (float)System.Math.Sqrt((dx * dx) + (dz * dz)));
            if (amount <= 0f || System.Math.Abs(p.Y - centre.Y) > GuardianGiantWaveHeight)
            {
                continue;
            }

            HurtByGuardianGiant(f, s, centre, amount);
            if (!ReferenceEquals(_worlds.Active, world))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The beam: from the visor toward the aim — every player within <see cref="GuardianGiantRules.LaserWidth"/> of it
    /// (along its reach) whom nothing solid shelters from the visor.</summary>
    private void GuardianGiantLaser(GuardianGiantFight f)
    {
        var visor = GuardianGiantVisor(f.Entity!);
        var aim = Unwrapped(visor, f.Aim);
        float ax = aim.X - visor.X, ay = aim.Y - visor.Y, az = aim.Z - visor.Z;
        float len = (float)System.Math.Sqrt((ax * ax) + (ay * ay) + (az * az));
        if (len < 0.01f)
        {
            return;
        }

        (ax, ay, az) = (ax / len, ay / len, az / len);
        var world = _worlds.Active;
        foreach (var s in GuardianGiantVictims())
        {
            var p = Unwrapped(visor, s.State.Position);
            float cx = p.X - visor.X, cy = p.Y + 1f - visor.Y, cz = p.Z - visor.Z; // the chest
            float along = (cx * ax) + (cy * ay) + (cz * az);
            float offSq = (cx * cx) + (cy * cy) + (cz * cz) - (along * along);
            if (along < 0f || along > GuardianGiantRules.LaserRange || offSq > GuardianGiantRules.LaserWidth * GuardianGiantRules.LaserWidth
                || !HasLineOfSight(visor, s.State.Position, 0f, 1f, skipToCell: false))
            {
                continue;
            }

            HurtByGuardianGiant(f, s, visor, GuardianGiantRules.LaserDamage);
            if (!ReferenceEquals(_worlds.Active, world))
            {
                return;
            }
        }
    }

    /// <summary>The rock lands at the aim: everyone whose body is within <see cref="GuardianGiantRules.ThrowRadius"/> of it and
    /// in its sight takes the splash (less toward the edge).</summary>
    private void GuardianGiantRock(GuardianGiantFight f)
    {
        var at = f.Aim;
        BroadcastToWorld(new WorldFx { Kind = GuardianGiantRules.RockFx, X = at.X, Y = at.Y, Z = at.Z, Strength = 1f, Radius = GuardianGiantRules.ThrowRadius });
        var world = _worlds.Active;
        foreach (var s in GuardianGiantVictims())
        {
            var p = Unwrapped(at, s.State.Position);
            float dx = p.X - at.X, dy = p.Y + 0.9f - at.Y, dz = p.Z - at.Z;
            float amount = GuardianGiantRules.WaveDamage(GuardianGiantRules.ThrowDamage, GuardianGiantRules.ThrowRadius,
                (float)System.Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)));
            if (amount <= 0f || !HasLineOfSight(at, s.State.Position, 0.5f, 0.9f, skipToCell: false))
            {
                continue;
            }

            HurtByGuardianGiant(f, s, at, amount);
            if (!ReferenceEquals(_worlds.Active, world))
            {
                return;
            }
        }
    }

    /// <summary>One blow on a player (the machines' path), who counts as one of its fighters from now on.</summary>
    private void HurtByGuardianGiant(GuardianGiantFight f, PlayerSession s, Vector3f from, float amount)
    {
        f.Fighters.Add(s.State.PlayerId);
        HurtPlayer(s, f.Entity!, from, amount, GuardianGiantDeathReason);
    }

    /// <summary>Two Guardian robots on free ground to its left and right (the ordinary walking machines).</summary>
    private void SummonGuardianGiantRobots(GuardianGiantFight f)
    {
        var e = f.Entity!;
        double a = e.Yaw * System.Math.PI / 180.0;
        float rx = (float)System.Math.Cos(a), rz = (float)-System.Math.Sin(a); // the giant's right (local +X)
        int placed = 0;
        foreach (var (side, dist) in GuardianGiantSummonSpots)
        {
            int x = (int)System.Math.Floor(e.Position.X + (rx * side * dist));
            int z = (int)System.Math.Floor(e.Position.Z + (rz * side * dist));
            if (placed >= GuardianGiantRules.SummonCount
                || !GuardianGiantFloor(x, z, (int)System.Math.Floor(e.Position.Y), GuardianGiantRules.HopHeight, out int feet)
                || GuardianGiantWet(x, z, feet))
            {
                continue;
            }

            var robot = NewPlanetMachine(GuardianGiantWrapped(x + 0.5f, feet, z + 0.5f), asDrone: false, tougher: false);
            _planetEnemies.Add(robot);
            f.SummonIds.Add(robot.Id);
            placed++;
        }

        BroadcastPlanetEnemies();
    }

    /// <summary>How many of its summoned robots are still about.</summary>
    private int AliveGuardianGiantSummons(GuardianGiantFight f)
    {
        f.SummonIds.RemoveAll(id => !_planetEnemies.Exists(m => m.Id == id));
        return f.SummonIds.Count;
    }

    /// <summary>Picks and starts an attack for a seen target at flat distance <paramref name="d"/>: a summon when due, the stomp
    /// up close, the leap from <see cref="GuardianGiantJumpBand"/> on, the laser and the rock in turn at mid range. False when
    /// none fits (it walks on).</summary>
    private bool TryStartGuardianGiantAttack(GuardianGiantFight f, Vector3f target, float d, double now)
    {
        var e = f.Entity!;
        bool summon = f.Rage && f.Summons < GuardianGiantRules.MaxSummons
                      && (f.Summons == 0 || now - f.LastSummonAt >= GuardianGiantSummonGap) && AliveGuardianGiantSummons(f) == 0;
        if (summon)
        {
            StartGuardianGiantMove(f, GuardianGiantMove.Summon, e.Position, now);
            return true;
        }

        if (d <= GuardianGiantRules.StompRadius)
        {
            StartGuardianGiantMove(f, GuardianGiantMove.Stomp, e.Position, now);
            return true;
        }

        if (d > GuardianGiantJumpBand && d <= GuardianGiantRules.JumpRange && TryGuardianGiantLanding(f, target, out var landing))
        {
            StartGuardianGiantMove(f, GuardianGiantMove.JumpAttack, landing, now);
            return true;
        }

        if (d > System.Math.Min(GuardianGiantRules.LaserRange, GuardianGiantRules.ThrowRange))
        {
            return false;
        }

        bool laser = (f.Turn++ % 2) == 0;
        StartGuardianGiantMove(f, laser ? GuardianGiantMove.Laser : GuardianGiantMove.Throw,
            laser ? GuardianGiantBeamEnd(f, target) : target, now);
        return true;
    }

    /// <summary>The leap's landing: the target's spot on dry ground within the leash, no further than eight rows up or down.</summary>
    private bool TryGuardianGiantLanding(GuardianGiantFight f, Vector3f target, out Vector3f landing)
    {
        landing = default;
        const float leash = GuardianGiantRules.LeashRange;
        int y = (int)System.Math.Floor(f.Entity!.Position.Y);
        int x = (int)System.Math.Floor(target.X), z = (int)System.Math.Floor(target.Z);
        if (FlatDistSq(target, f.Rest) > leash * leash || !GuardianGiantFloor(x, z, (int)System.Math.Floor(target.Y), 4, out int feet)
            || GuardianGiantWet(x, z, feet) || System.Math.Abs(feet - y) > 8)
        {
            return false;
        }

        landing = GuardianGiantWrapped(target.X, feet, target.Z);
        return true;
    }

    /// <summary>Where the beam ends: from the visor toward the target's chest, at the first solid block or at its full reach.</summary>
    private Vector3f GuardianGiantBeamEnd(GuardianGiantFight f, Vector3f target)
    {
        var visor = GuardianGiantVisor(f.Entity!);
        float dx = target.X - visor.X, dy = target.Y + 1f - visor.Y, dz = target.Z - visor.Z;
        float len = (float)System.Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (len < 0.01f)
        {
            return target;
        }

        (dx, dy, dz) = (dx / len, dy / len, dz / len);
        for (float t = 1f; t <= GuardianGiantRules.LaserRange; t += 0.5f)
        {
            int x = (int)System.Math.Floor(visor.X + (dx * t)), y = (int)System.Math.Floor(visor.Y + (dy * t)), z = (int)System.Math.Floor(visor.Z + (dz * t));
            if (IsSolidBlock(_world.GetBlockIfLoaded(new Vector3i(x, y, z))))
            {
                return GuardianGiantWrapped(visor.X + (dx * t), visor.Y + (dy * t), visor.Z + (dz * t));
            }
        }

        float r = GuardianGiantRules.LaserRange;
        return GuardianGiantWrapped(visor.X + (dx * r), visor.Y + (dy * r), visor.Z + (dz * r));
    }

    // ---------------------------------------------------------------- taking damage

    /// <summary>The fight of an awake giant entity on the active world, or null.</summary>
    private GuardianGiantFight? GuardianGiantFightOf(CombatEntity e)
        => _guardianGiantWorlds.TryGetValue(_world.LocationId, out var gw) && gw.Fight is { } f && ReferenceEquals(f.Entity, e) ? f : null;

    /// <summary>The one hook every damage path calls after taking hull off a planet enemy (a player's blow, a sentry's or a
    /// hunter robot's finishing shot). True when <paramref name="target"/> is a guardian giant: the caller stops — no push, no
    /// loot, no removal; the attacker within its fight range counts as a fighter, and at zero hull it starts to collapse.</summary>
    private bool OnGuardianGiantDamaged(CombatEntity target, string attackerId)
    {
        if (target.Kind != CombatEntityKind.GuardianGiant)
        {
            return false;
        }

        if (GuardianGiantFightOf(target) is not { } f)
        {
            return true;
        }

        const float r = GuardianGiantRules.FightRange;
        if (attackerId.Length > 0 && FindSessionByPlayerId(attackerId) is { } who && FlatDistSq(who.State.Position, target.Position) <= r * r)
        {
            f.Fighters.Add(attackerId);
        }

        if (target.Hull <= 0f)
        {
            target.Hull = 0f;
            if (f.Move != GuardianGiantMove.Collapse)
            {
                StartGuardianGiantCollapse(f, SnapshotTime);
            }
        }

        BroadcastPlanetEnemies();
        return true;
    }

    /// <summary>Where a blow at the giant is measured to (reach, sight, aim): the point of its body — a vertical capsule of
    /// <see cref="GuardianGiantBodyRadius"/> from a block above its root to a block below its crest — nearest the attacker's
    /// aim ray (the eye height without one), lowered by the eye height like every giant aim point.</summary>
    private Vector3f GuardianGiantAimPoint(CombatEntity giant, Vector3f from, Vector3f aimDir)
    {
        var eye = new Vector3f(from.X, from.Y + SightEyeHeight, from.Z);
        var root = Unwrapped(eye, giant.Position);
        float lo = root.Y + 1f, hi = root.Y + GuardianGiantBody.Height - 1f;
        float y = System.Math.Clamp(eye.Y, lo, hi);
        float flatSq = (aimDir.X * aimDir.X) + (aimDir.Z * aimDir.Z);
        if (flatSq > 1e-4f)
        {
            float t = System.Math.Max(0f, (((root.X - eye.X) * aimDir.X) + ((root.Z - eye.Z) * aimDir.Z)) / flatSq);
            y = System.Math.Clamp(eye.Y + (aimDir.Y * t), lo, hi);
        }

        float dx = eye.X - root.X, dz = eye.Z - root.Z;
        float flat = (float)System.Math.Sqrt((dx * dx) + (dz * dz));
        float pull = flat > 1e-3f ? System.Math.Min(GuardianGiantBodyRadius, flat) / flat : 0f;
        return new Vector3f(root.X + (dx * pull), y - SightEyeHeight, root.Z + (dz * pull));
    }
}
