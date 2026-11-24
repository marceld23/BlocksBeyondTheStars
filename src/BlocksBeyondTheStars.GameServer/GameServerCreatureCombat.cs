// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A creature's reactions to a fight: provoked retaliation, startled kin, the arachnid's announcement and the admin
/// summons of an arachnid or a biped.
/// </summary>
public sealed partial class GameServer
{
    private const float CreaturePanicRadius = 12f;    // how far a startle (#653) spreads to same-species kin
    private const double CreaturePanicSeconds = 4.0;  // how long the startle lasts
    private const float CreaturePanicFleeRange = 24f; // a panicked animal flees players well past the skittish reflex

    /// <summary>Startles a hurt/bolting creature's same-species kin within <see cref="CreaturePanicRadius"/>
    /// (#653): non-retaliators among them flee the nearest player while their timer runs. The source itself is
    /// startled too (a hurt passive grazer bolts even though nothing else scares it).</summary>
    private void StartleKin(CombatEntity source)
    {
        // #2081: a hurt biped sends its whole herd running (decision 2026-09-27) — longer and wider than the classic startle,
        // and the herd wants no food for a while.
        bool biped = _speciesById.TryGetValue(source.SpeciesId, out var sp) && BipedRules.IsBiped(sp);
        double seconds = biped ? BipedRules.PanicSeconds : CreaturePanicSeconds;
        float radius = biped ? BipedRules.PanicRadius : CreaturePanicRadius;
        Startle(source, seconds, biped);
        foreach (var other in _creatures)
        {
            if (ReferenceEquals(other, source) || other.IsCompanion || other.SpeciesId != source.SpeciesId)
            {
                continue;
            }

            if (WrapDistSq(other.Position, source.Position) <= radius * radius)
            {
                Startle(other, seconds, biped);
            }
        }
    }

    private void Startle(CombatEntity c, double seconds, bool dropFood)
    {
        c.PanicTimer = System.Math.Max(c.PanicTimer, seconds);
        if (dropFood)
        {
            c.BegPhase = BegPhase.None;
            c.BegCooldownUntil = System.Math.Max(c.BegCooldownUntil, _uptime + HerdRules.CooldownSeconds);
        }
    }

    /// <summary>Marks an attacked creature as provoked if its species retaliates; pack-hunters rally kin.</summary>
    private void ProvokeCreature(CombatEntity target)
    {
        if (!_speciesById.TryGetValue(target.SpeciesId, out var sp)
            || !CreatureBehaviour.RetaliatesWhenAttacked(sp))
        {
            return;
        }

        target.ProvokeTimer = CreatureProvokeSeconds;

        if (sp.Temperament == CreatureTemperament.PackHunter)
        {
            foreach (var other in _creatures)
            {
                if (!ReferenceEquals(other, target) && other.SpeciesId == target.SpeciesId
                    && WrapDistSq(other.Position, target.Position) <= CreaturePackRallyRange * CreaturePackRallyRange)
                {
                    other.ProvokeTimer = CreatureProvokeSeconds;
                }
            }
        }
    }

    /// <summary>#2009: VEGA points out an arachnid the first time one comes within <see cref="ArachnidRules.SightingRange"/> of a
    /// player — once per session, so the line is a sighting, not a nag.</summary>
    private void AnnounceArachnid(CombatEntity creature, List<PlayerSession> targets)
    {
        float rangeSq = ArachnidRules.SightingRange * ArachnidRules.SightingRange;
        foreach (var s in targets)
        {
            if (!s.Vega.ArachnidSighted && WrapDistSq(creature.Position, s.State.Position) <= rangeSq)
            {
                s.Vega.ArachnidSighted = true;
                SendVegaLine(s, "vega.sys.arachnid_sighted", 3);
            }
        }
    }

    /// <summary>#2009: <c>/arachnid</c> — puts an arachnid of this world near an admin for testing. The roster's own species
    /// when the world rolled one; otherwise a rolled one joins the roster (so it can be scanned, tamed and respawned like
    /// any species): this world's Land roll with the plan forced. Placed 12–22 blocks out on a spot the spawner would
    /// accept (habitat, body volume, ship margin, sealed rooms).</summary>
    private void AdminSummonArachnid(PlayerSession session)
    {
        var sp = _speciesRoster.FirstOrDefault(s => s.BodyPlan == CreatureBodyPlan.Arachnid);
        if (sp is null)
        {
            var planet = _content.GetPlanet(_worlds.Active.PlanetType);
            if (planet is null)
            {
                return;
            }

            sp = CreatureGenerator.GenerateArachnid(planet,
                BlocksBeyondTheStars.WorldGeneration.WorldGenerator.RosterSeedFor(_meta.Seed, _world.LocationId));
            FaunaTints.Dress(new[] { sp }, planet); // #3448: a rolled species wears the type's fauna palette, like the roster's
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
            float dist = 12f + (float)rng.NextDouble() * 10f;
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

        Send(session, new ServerMessage { Text = placed ? "@srv.admin.arachnid_summoned" : "@srv.admin.arachnid_no_room" });
        CheatLog(session.State, $"summoned an arachnid ({(placed ? "placed" : "no room")})");
    }

    /// <summary>#2081: <c>/biped</c> — puts a herd of this world's bipeds near an admin for testing: the roster's own begging
    /// biped with a favourite food when the world has one (Mini-Michi-Paul on a tropical world), otherwise a rolled one that
    /// begs joins the roster (a biped of the roster that does not beg would not show the feeding loop)
    /// (so it can be scanned, fed, tamed and respawned like any species). Up to six members, 8–16 blocks out, on spots the
    /// spawner would accept.</summary>
    private void AdminSummonBiped(PlayerSession session)
    {
        var sp = _speciesRoster.FirstOrDefault(s => s.BodyPlan == CreatureBodyPlan.Biped && HerdRules.TamesByFeeding(s));
        if (sp is null)
        {
            var planet = _content.GetPlanet(_worlds.Active.PlanetType);
            if (planet is null)
            {
                return;
            }

            sp = CreatureGenerator.GenerateBiped(planet,
                BlocksBeyondTheStars.WorldGeneration.WorldGenerator.RosterSeedFor(_meta.Seed, _world.LocationId),
                _meta.Description.TerrainGeneration);
            FaunaTints.Dress(new[] { sp }, planet); // #3448: a rolled species wears the type's fauna palette, like the roster's
            _speciesRoster = _speciesRoster.Append(sp).ToArray();
            _speciesById[sp.Id] = sp;
            _locoProfiles[sp.Id] = LocomotionController.ForSpecies(sp);
        }

        var at = session.State.Position;
        var rng = new System.Random(unchecked((int)(_uptime * 1000.0)));
        int placed = 0;
        for (int attempt = 0; attempt < 40 && placed < 6; attempt++)
        {
            double angle = rng.NextDouble() * System.Math.PI * 2.0;
            float dist = 8f + (float)rng.NextDouble() * 8f;
            int x = (int)System.Math.Floor(at.X + System.Math.Cos(angle) * dist);
            int z = (int)System.Math.Floor(at.Z + System.Math.Sin(angle) * dist);
            int surface = _generator.SurfaceHeight(_world.Planet, x, z);
            var pos = new Vector3f(x + 0.5f, GroundFeetYAt(x, z, surface + 1), z + 0.5f);
            if (SpawnSpotClear(sp, pos, x, z, surface))
            {
                SpawnCreature(sp, pos);
                placed++;
            }
        }

        Send(session, new ServerMessage { Text = placed > 0 ? "@srv.admin.biped_summoned" : "@srv.admin.biped_no_room" });
        CheatLog(session.State, $"summoned bipeds ({placed} placed)");
    }

    /// <summary>Test seam: /biped without the chat.</summary>
    public void SummonBipedForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            AdminSummonBiped(s);
        }
    }

    /// <summary>Test seam: /arachnid without the chat.</summary>
    public void SummonArachnidForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            AdminSummonArachnid(s);
        }
    }
}
