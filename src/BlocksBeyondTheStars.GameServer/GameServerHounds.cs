// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

public sealed partial class CombatEntity
{
    /// <summary>#3326: seconds until a creature that bites in chunks (<see cref="CreatureReach.BitesInChunks"/>, the
    /// Schuppenhund) may bite again. Unused by every other creature.</summary>
    public double BiteCooldown { get; set; }
}

/// <summary>
/// The Schuppenhund (#3325–#3328, epic #3329 — Jafar's scale hound): the server rules of a very dangerous, solitary hunter of
/// the dry worlds, kept out of the creature state machine so nothing about any other species changes.
/// <list type="bullet">
/// <item><b>Bites in chunks</b> — <see cref="HoundRules.BiteDamage"/> once per <see cref="CreatureSpecies.BiteInterval"/>,
/// within reach and in sight, instead of the per-second drain every other hostile deals: one lunge, one sound, one chunk.</item>
/// <item><b>The charge</b> — within <see cref="HoundRules.ChargeRange"/> of its prey it runs ×<see cref="HoundRules.ChargeFactor"/>,
/// still below the player's walk; it notices prey from <see cref="HoundRules.AggroRange"/>.</item>
/// <item><b>Alone</b> — the spawner keeps <see cref="HoundRules.MaxWildAtOnce"/> wild hound at a time.</item>
/// <item><b>VEGA</b> warns the first time a player comes near one; <c>/hound</c> summons one for testing.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The damage a hostile deals a player in its reach and sight this tick: the classic per-second drain, or — for a
    /// species that bites in chunks — one whole bite when its cooldown has run out (which restarts it), else nothing.</summary>
    private static float BiteThisTick(CombatEntity creature, CreatureSpecies sp, double dt)
    {
        if (!CreatureReach.BitesInChunks(sp))
        {
            return (float)(creature.DamagePerSecond * dt);
        }

        if (creature.BiteCooldown > 0 || creature.DamagePerSecond <= 0f)
        {
            return 0f; // between two bites — or something (a stagger, a stasis field, a calming) switched the bite off
        }

        creature.BiteCooldown = sp.BiteInterval;
        return sp.BiteDamage;
    }

    /// <summary>Runs a chunk-biter's cooldown down — every tick, whatever else keeps it from biting right now.</summary>
    private static void TickBiteCooldown(CombatEntity creature, double dt)
    {
        if (creature.BiteCooldown > 0)
        {
            creature.BiteCooldown = System.Math.Max(0, creature.BiteCooldown - dt);
        }
    }

    /// <summary>How far a hunter notices its prey: the hound further than the classic range.</summary>
    private static float HoundAggro(CreatureSpecies sp, float aggro)
        => HoundRules.IsHound(sp) ? System.Math.Max(aggro, HoundRules.AggroRange) : aggro;

    /// <summary>The charge (#3326): a hunting hound within <see cref="HoundRules.ChargeRange"/> of its prey runs faster.</summary>
    private void ApplyHoundCharge(CombatEntity creature, CreatureSpecies sp, bool aggressor, Vector3f? prey, ref LocomotionProfile profile)
    {
        if (!aggressor || !HoundRules.IsHound(sp) || creature.IsCompanion || creature.GiveUpTimer > 0 || prey is not { } p
            || WrapDistSq(creature.Position, p) > HoundRules.ChargeRange * HoundRules.ChargeRange)
        {
            return;
        }

        profile.CruiseSpeed *= HoundRules.ChargeFactor;
        profile.BurstSpeed *= HoundRules.ChargeFactor;
    }

    /// <summary>Whether the spawner must skip <paramref name="sp"/> because it lives alone and one is already about.</summary>
    internal bool HoundAlreadyAbout(CreatureSpecies sp)
        => HoundRules.IsHound(sp) && WildCountOf(sp.Id) >= HoundRules.MaxWildAtOnce;

    /// <summary>#3326: VEGA warns the first time a hound comes within <see cref="HoundRules.SightingRange"/> of a player — once
    /// per session, so the line is a warning, not a nag.</summary>
    private void AnnounceHound(CombatEntity creature, List<PlayerSession> targets)
    {
        float rangeSq = HoundRules.SightingRange * HoundRules.SightingRange;
        foreach (var s in targets)
        {
            if (!s.Vega.HoundSighted && WrapDistSq(creature.Position, s.State.Position) <= rangeSq)
            {
                s.Vega.HoundSighted = true;
                SendVegaLine(s, "vega.sys.hound_sighted", 3);
            }
        }
    }

    /// <summary>#3326: <c>/hound</c> — puts this world's Schuppenhund near an admin for testing. The roster's own when the
    /// world rolled one; otherwise this world's hound joins the roster (so it can be scanned, tamed and respawned like any
    /// species). Placed 14–22 blocks out — far enough to see it come — on a spot the spawner would accept.</summary>
    private void AdminSummonHound(PlayerSession session)
    {
        var sp = _speciesRoster.FirstOrDefault(HoundRules.IsHound);
        if (sp is null)
        {
            var planet = _content.GetPlanet(_worlds.Active.PlanetType);
            if (planet is null)
            {
                return;
            }

            sp = CreatureGenerator.GenerateScaleHound(planet, WorldGenerator.RosterSeedFor(_meta.Seed, _world.LocationId));
            _speciesRoster = _speciesRoster.Append(sp).ToArray();
            _speciesById[sp.Id] = sp;
            _locoProfiles[sp.Id] = LocomotionController.ForSpecies(sp);
        }

        var at = session.State.Position;
        var rng = new System.Random(unchecked((int)(_uptime * 1000.0)));
        bool placed = false;
        for (int attempt = 0; attempt < 24 && !placed; attempt++)
        {
            double angle = rng.NextDouble() * System.Math.PI * 2.0;
            float dist = 14f + (float)rng.NextDouble() * 8f;
            int x = (int)System.Math.Floor(at.X + System.Math.Cos(angle) * dist);
            int z = (int)System.Math.Floor(at.Z + System.Math.Sin(angle) * dist);
            int surface = _generator.SurfaceHeight(_world.Planet, x, z);
            var pos = new Vector3f(x + 0.5f, GroundFeetYAt(x, z, surface + 1), z + 0.5f);
            if (SpawnSpotClear(sp, pos, x, z, surface))
            {
                SpawnCreature(sp, pos);
                placed = true;
            }
        }

        Send(session, new ServerMessage { Text = placed ? "@srv.admin.hound_summoned" : "@srv.admin.hound_no_room" });
        CheatLog(session.State, $"summoned a Schuppenhund ({(placed ? "placed" : "no room")})");
    }

    /// <summary>Test seam: /hound without the chat.</summary>
    public void SummonHoundForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            AdminSummonHound(s);
        }
    }
}
