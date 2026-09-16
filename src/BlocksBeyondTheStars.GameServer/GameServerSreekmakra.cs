// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The Sreekmakra (2026-09, Justus' Valuma): one shapeshifter per world of a type that lists it among its authored
/// creatures. It never spawns like an animal; it walks among the herds in the shape of a land species of the roster
/// (three times that animal's health), and every few minutes, while nobody stands near, it takes another shape. Killing
/// an animal of the species it currently mimics — or hitting it — turns it on that player, with the shape's speed and
/// bite ×1.5, until it dies or the player leaves. At zero health its disguise breaks: the true form fights on (or, with
/// planet enemies off, flees and vanishes). Defeating the true form drops its loot, opens its Codex entry and an
/// achievement; the next one comes a few in-game days later. The hand scanner reads the disguised one as an anomaly.
/// Valuma's mood: after 20 minutes on the planet VEGA feels watched, after 35 the fog closes in and the music darkens.
/// </summary>
public sealed partial class GameServer
{
    internal const string SreekmakraSpeciesId = "au_sreekmakra";
    private const string SreekmakraAuthoredKey = "sreekmakra";

    private const float SreekmakraHealthFactor = 3f;
    private const float SreekmakraSpeedFactor = 1.5f;
    private const float SreekmakraBiteFactor = 1.5f;
    private const float SreekmakraFallbackBite = 4.5f; // a peaceful shape has no bite of its own — a rolled hunter's
    private const double SreekmakraShiftMinSeconds = 150.0, SreekmakraShiftJitterSeconds = 90.0;
    private const float SreekmakraObservedRange = 24f;
    private const float SreekmakraAnomalyRange = 24f;
    private const double SreekmakraFleeSeconds = 25.0;
    private const int SreekmakraReturnDays = 3;

    private const double ValumaWatchedSeconds = 20 * 60.0;
    private const double ValumaFogSeconds = 35 * 60.0;

    private readonly Dictionary<string, double> _sreekTickAt = new();

    private SreekmakraState Sreek => _worlds.Active.Sreekmakra;

    /// <summary>Whether the active world hosts a Sreekmakra (generation 8, the type lists it, the roster carries it).</summary>
    private bool SreekmakraWorld()
        => _world.Planet is { Void: false } planet && planet.AuthoredCreatures.Contains(SreekmakraAuthoredKey)
            && _generator.TerrainGeneration >= WorldDescription.ExtremePlanetsGeneration
            && _speciesById.ContainsKey(SreekmakraSpeciesId);

    private bool IsSreekmakra(CombatEntity c) => Sreek.CreatureId.Length > 0 && c.Id == Sreek.CreatureId;

    /// <summary>True while the shapeshifter hunts a player (the movement treats it as an aggressor, ×1.5 speed).</summary>
    private bool SreekmakraHunting(CombatEntity c) => IsSreekmakra(c) && Sreek.TargetPlayerId.Length > 0;

    private static long NowUnixSeconds => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>The land shapes a Sreekmakra may take: rolled land species, no titans, never its own kind.</summary>
    private List<CreatureSpecies> SreekmakraShapes()
        => _speciesRoster.Where(sp => sp.Habitat == CreatureHabitat.Land && sp.BodyPlan != CreatureBodyPlan.Titan
            && !sp.Id.StartsWith("au_", System.StringComparison.Ordinal)).ToList();

    /// <summary>Once a second per world: keep the one shapeshifter alive, let it change shape, keep its hunt going; and
    /// run the Valuma mood clock for everyone on the world.</summary>
    private void TickSreekmakra(double dt)
    {
        string world = _world.LocationId;
        if (_sreekTickAt.TryGetValue(world, out double due) && _uptime < due)
        {
            return;
        }

        _sreekTickAt[world] = _uptime + 1.0;
        TickValumaMood();
        if (!SreekmakraWorld() || Rules.CreatureAbundance == AlienActivity.Off)
        {
            return;
        }

        var state = Sreek;
        var creature = state.CreatureId.Length > 0 ? _creatures.FirstOrDefault(c => c.Id == state.CreatureId) : null;
        if (creature is null)
        {
            state.Clear(); // pruned with the far fauna, or never there — a new one walks in near a player
            if (!_meta.SreekmakraBackAt.TryGetValue(world, out long back) || NowUnixSeconds >= back)
            {
                TrySpawnSreekmakra(state);
            }

            return;
        }

        if (state.FleeUntil > 0)
        {
            if (_uptime >= state.FleeUntil)
            {
                _creatures.Remove(creature); // gone into the grass — back in a day
                _meta.SreekmakraBackAt[world] = NowUnixSeconds + (long)DayLengthSecondsForSreek();
                _repo.SaveMetadata(_meta);
                state.Clear();
                BroadcastCreatures();
            }
            else
            {
                creature.PanicTimer = System.Math.Max(creature.PanicTimer, 3.0);
            }

            return;
        }

        if (state.TargetPlayerId.Length > 0)
        {
            var target = JoinedInActiveWorld().FirstOrDefault(s => s.State.PlayerId == state.TargetPlayerId);
            if (target is null || target.State.AboardShip || InSpace(target.State.PlayerId) || !PlanetEnemiesActive)
            {
                // The player left the planet (or the rules turned peaceful): it melts back into the herd.
                state.TargetPlayerId = string.Empty;
                creature.ProvokeTimer = 0;
                creature.DamagePerSecond = state.Revealed ? creature.DamagePerSecond : 0f;
            }
            else
            {
                creature.ProvokeTimer = System.Math.Max(creature.ProvokeTimer, 3.0);
                creature.AwakeOverrideTimer = System.Math.Max(creature.AwakeOverrideTimer, 3.0);
                creature.ChaseTimer = 0;
                creature.GiveUpTimer = 0;
            }

            return;
        }

        if (!state.Revealed && _uptime >= state.NextShiftAt
            && !JoinedInActiveWorld().Any(s => WrapDistSq(s.State.Position, creature.Position) <= SreekmakraObservedRange * SreekmakraObservedRange))
        {
            ShiftSreekmakra(creature, state);
        }
    }

    private double DayLengthSecondsForSreek() => System.Math.Max(60.0, _world.Planet?.DayLengthSeconds ?? 600.0);

    /// <summary>Places the shapeshifter 40–60 blocks from a player on foot, disguised as a land animal of the roster.</summary>
    private bool TrySpawnSreekmakra(SreekmakraState state)
    {
        var shapes = SreekmakraShapes();
        var players = JoinedInActiveWorld().Where(s => !s.State.AboardShip && !InSpace(s.State.PlayerId)).ToList();
        if (shapes.Count == 0 || players.Count == 0)
        {
            return false;
        }

        var rng = new System.Random(unchecked((int)(_meta.Seed ^ WorldGenerator.StableHash("sreek:" + _world.LocationId) ^ (long)_uptime)));
        var shape = shapes[rng.Next(shapes.Count)];
        var near = players[rng.Next(players.Count)].State.Position;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            double angle = rng.NextDouble() * System.Math.PI * 2.0;
            float dist = 40f + (float)rng.NextDouble() * 20f;
            int x = (int)System.Math.Floor(near.X + System.Math.Cos(angle) * dist);
            int z = (int)System.Math.Floor(near.Z + System.Math.Sin(angle) * dist);
            int surface = _generator.SurfaceHeight(_world.Planet, x, z);
            var pos = new Vector3f(x + 0.5f, GroundFeetYAt(x, z, surface + 1), z + 0.5f);
            if (!SpawnSpotClear(shape, pos, x, z, surface))
            {
                continue;
            }

            PlaceSreekmakra(state, shape, pos, rng);
            return true;
        }

        return false;
    }

    private void PlaceSreekmakra(SreekmakraState state, CreatureSpecies shape, Vector3f pos, System.Random rng)
    {
        SpawnCreature(shape, pos);
        var creature = _creatures[^1];
        var trueForm = _speciesById[SreekmakraSpeciesId];
        state.Clear();
        state.CreatureId = creature.Id;
        TakeShape(creature, shape);
        creature.Loot.Clear();
        creature.Loot.Add(new ItemAmount(trueForm.DropItem, trueForm.DropCount)); // whatever shape it wears, it drops its own
        state.NextShiftAt = _uptime + SreekmakraShiftMinSeconds + rng.NextDouble() * SreekmakraShiftJitterSeconds;
        BroadcastCreatures();
    }

    /// <summary>Wears a shape: the species, a calm temper and three times that animal's health.</summary>
    private static void TakeShape(CombatEntity creature, CreatureSpecies shape)
    {
        creature.SpeciesId = shape.Id;
        creature.Kind = CombatEntityKind.Creature;
        creature.Hostile = false;
        creature.HullMax = shape.MaxHealth * SreekmakraHealthFactor;
        creature.Hull = creature.HullMax;
        creature.DamagePerSecond = 0f;
        creature.ProvokeTimer = 0;
        creature.Loco = default; // the new body moves by its own profile from a standing start
    }

    private void ShiftSreekmakra(CombatEntity creature, SreekmakraState state)
    {
        var rng = new System.Random(unchecked((int)(StableStringHash(creature.Id) ^ (long)_uptime)));
        var shapes = SreekmakraShapes().Where(sp => sp.Id != creature.SpeciesId).ToList();
        state.NextShiftAt = _uptime + SreekmakraShiftMinSeconds + rng.NextDouble() * SreekmakraShiftJitterSeconds;
        if (shapes.Count == 0)
        {
            return;
        }

        TakeShape(creature, shapes[rng.Next(shapes.Count)]);
        BroadcastCreatures();
    }

    /// <summary>A player's hit on the shapeshifter (after the damage). Returns true when the hit is fully handled — the
    /// disguise broke instead of the creature dying.</summary>
    private bool OnSreekmakraHit(PlayerSession session, CombatEntity creature)
    {
        var state = Sreek;
        if (creature.Hull <= 0f && !state.Revealed)
        {
            RevealSreekmakra(creature, state, session);
            return true;
        }

        if (creature.Hull > 0f)
        {
            TurnSreekmakraOn(session, creature, state);
        }

        return false;
    }

    /// <summary>Every creature death with its killer (null for sentries and fire): the shapeshifter's own death, or a
    /// player killing an animal of the shape it currently wears.</summary>
    private void OnCreatureKilled(CombatEntity dead, PlayerSession? killer)
    {
        var state = Sreek;
        if (state.CreatureId.Length == 0)
        {
            return;
        }

        if (dead.Id == state.CreatureId)
        {
            OnSreekmakraDefeated(dead, state, killer);
            return;
        }

        if (killer is not null && !state.Revealed && _creatures.FirstOrDefault(c => c.Id == state.CreatureId) is { } sreek
            && sreek.SpeciesId == dead.SpeciesId)
        {
            TurnSreekmakraOn(killer, sreek, state); // "you killed one of mine"
        }
    }

    private void TurnSreekmakraOn(PlayerSession session, CombatEntity creature, SreekmakraState state)
    {
        if (state.FleeUntil > 0)
        {
            return;
        }

        if (!PlanetEnemiesActive)
        {
            RevealSreekmakra(creature, state, session); // peaceful rules: it only shows itself and flees
            return;
        }

        bool fresh = state.TargetPlayerId != session.State.PlayerId;
        state.TargetPlayerId = session.State.PlayerId;
        if (!state.Revealed && _speciesById.TryGetValue(creature.SpeciesId, out var shape))
        {
            creature.DamagePerSecond = (shape.AttackDamage > 0f ? shape.AttackDamage : SreekmakraFallbackBite) * SreekmakraBiteFactor;
        }

        creature.ProvokeTimer = System.Math.Max(creature.ProvokeTimer, 5.0);
        creature.AwakeOverrideTimer = System.Math.Max(creature.AwakeOverrideTimer, 5.0);
        creature.GiveUpTimer = 0;
        if (fresh)
        {
            SendVegaLine(session, "vega.sys.sreekmakra_hostile", 3);
        }

        BroadcastCreatures();
    }

    /// <summary>The disguise breaks: the true form stands there with its own health — it fights on, or flees when the rules
    /// keep the planet peaceful.</summary>
    private void RevealSreekmakra(CombatEntity creature, SreekmakraState state, PlayerSession? witness)
    {
        var trueForm = _speciesById[SreekmakraSpeciesId];
        state.Revealed = true;
        creature.SpeciesId = SreekmakraSpeciesId;
        creature.HullMax = trueForm.MaxHealth;
        creature.Hull = trueForm.MaxHealth;
        creature.Loco = default;
        bool fights = PlanetEnemiesActive;
        creature.Hostile = fights;
        creature.Kind = fights ? CombatEntityKind.AlienMonster : CombatEntityKind.Creature;
        creature.DamagePerSecond = fights ? trueForm.AttackDamage * SreekmakraBiteFactor : 0f;
        if (!fights)
        {
            state.TargetPlayerId = string.Empty;
            state.FleeUntil = _uptime + SreekmakraFleeSeconds;
            creature.PanicTimer = SreekmakraFleeSeconds;
        }
        else if (witness is not null)
        {
            state.TargetPlayerId = witness.State.PlayerId;
        }

        foreach (var s in JoinedInActiveWorld())
        {
            if (WrapDistSq(s.State.Position, creature.Position) <= 64f * 64f)
            {
                SendVegaLine(s, fights ? "vega.sys.sreekmakra_revealed" : "vega.sys.sreekmakra_fled", 3);
            }
        }

        BroadcastCreatures();
    }

    private void OnSreekmakraDefeated(CombatEntity dead, SreekmakraState state, PlayerSession? killer)
    {
        var trueForm = _speciesById[SreekmakraSpeciesId];
        string world = _world.LocationId;
        _meta.SreekmakraBackAt[world] = NowUnixSeconds + (long)(SreekmakraReturnDays * DayLengthSecondsForSreek());
        _repo.SaveMetadata(_meta);
        state.Clear();

        // Everyone close enough to see it fall learns what it was: the Codex entry of its true form.
        string ledgerKey = "creature:" + SreekmakraSpeciesId;
        string name = string.IsNullOrEmpty(trueForm.Name) ? SreekmakraSpeciesId : trueForm.Name;
        foreach (var s in JoinedInActiveWorld())
        {
            if (s != killer && WrapDistSq(s.State.Position, dead.Position) > 64f * 64f)
            {
                continue;
            }

            var p = s.State;
            SendVegaLine(s, "vega.sys.sreekmakra_defeated", 3);
            if (p.Scanned.Add(ledgerKey))
            {
                p.ScannedNames[ledgerKey] = name;
                var site = SiteFor(s);
                if (site is not null)
                {
                    p.ScannedWhere[ledgerKey] = site;
                }

                Send(s, DiscoveryDelta(ledgerKey, name, site));
                _repo.SavePlayer(p);
            }
        }

        if (killer is not null)
        {
            Advance(killer, "defeat:sreekmakra");
        }

        _log.Info($"The Sreekmakra on '{world}' was defeated{(killer is null ? string.Empty : " by " + killer.State.Name)}.");
    }

    /// <summary>The hand scanner on the disguised shapeshifter: the nearest animal of the scanned species within reach is it.</summary>
    private bool SreekmakraAnomalyFor(PlayerSession session, string speciesId)
    {
        var state = Sreek;
        if (state.CreatureId.Length == 0 || state.Revealed
            || _creatures.FirstOrDefault(c => c.Id == state.CreatureId) is not { } sreek || sreek.SpeciesId != speciesId)
        {
            return false;
        }

        var at = session.State.Position;
        double sreekSq = WrapDistSq(at, sreek.Position);
        if (sreekSq > SreekmakraAnomalyRange * SreekmakraAnomalyRange)
        {
            return false;
        }

        return !_creatures.Any(c => c.Id != sreek.Id && c.SpeciesId == speciesId && !c.IsCompanion && WrapDistSq(at, c.Position) < sreekSq);
    }

    // ---------------- Valuma's mood ----------------

    /// <summary>The mood clock: time on a Valuma world (aboard the landed ship too) — VEGA feels watched at 20 minutes,
    /// the fog closes in and the music darkens at 35. Leaving the world resets it.</summary>
    private void TickValumaMood()
    {
        bool valuma = _world.Planet is { Void: false } planet && planet.AuthoredCreatures.Contains(SreekmakraAuthoredKey)
            && _generator.TerrainGeneration >= WorldDescription.ExtremePlanetsGeneration;
        foreach (var session in JoinedInActiveWorld())
        {
            if (session.MoodLocationId != _world.LocationId || InSpace(session.State.PlayerId))
            {
                ResetValumaMood(session);
                session.MoodLocationId = InSpace(session.State.PlayerId) ? string.Empty : _world.LocationId;
            }

            if (!valuma || session.MoodLocationId.Length == 0)
            {
                continue;
            }

            session.MoodSeconds += 1.0;
            if (!session.MoodWatchedTold && session.MoodSeconds >= ValumaWatchedSeconds)
            {
                session.MoodWatchedTold = true;
                SendVegaLine(session, "vega.sys.valuma_watching", 3);
            }

            if (!session.MoodUneasy && session.MoodSeconds >= ValumaFogSeconds)
            {
                session.MoodUneasy = true;
                SendEnvironment(session);
                SendPlayerState(session);
            }
        }
    }

    private void ResetValumaMood(PlayerSession session)
    {
        bool wasUneasy = session.MoodUneasy;
        session.MoodSeconds = 0;
        session.MoodWatchedTold = false;
        session.MoodUneasy = false;
        if (wasUneasy)
        {
            SendEnvironment(session);
            SendPlayerState(session);
        }
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test/util: the shapeshifter on the active world, or null.</summary>
    public (string Id, string SpeciesId, bool Revealed, string TargetPlayerId, float Hull, float HullMax, bool Fleeing)? SreekmakraForTest()
    {
        var state = Sreek;
        return state.CreatureId.Length > 0 && _creatures.FirstOrDefault(c => c.Id == state.CreatureId) is { } c
            ? (c.Id, c.SpeciesId, state.Revealed, state.TargetPlayerId, c.Hull, c.HullMax, state.FleeUntil > 0)
            : null;
    }

    /// <summary>Test/util: runs the once-a-second shapeshifter tick now.</summary>
    public void TickSreekmakraForTest()
    {
        _sreekTickAt.Remove(_world.LocationId);
        TickSreekmakra(1.0);
    }

    /// <summary>Test/util: forces the next shape change (nobody may stand near).</summary>
    public void ShiftSreekmakraForTest()
    {
        if (_creatures.FirstOrDefault(c => c.Id == Sreek.CreatureId) is { } c)
        {
            ShiftSreekmakra(c, Sreek);
        }
    }

    /// <summary>Test/util: whether a scan of this species by this player would read the anomaly.</summary>
    public bool SreekmakraAnomalyForTest(string playerId, string speciesId)
        => FindSessionByPlayerId(playerId) is { } s && SreekmakraAnomalyFor(s, speciesId);

    /// <summary>Test/util: the unix second the next shapeshifter may appear on the active world (0 = no wait).</summary>
    public long SreekmakraBackAtForTest() => _meta.SreekmakraBackAt.TryGetValue(_world.LocationId, out long at) ? at : 0;

    /// <summary>Test/util: jumps a player's mood clock.</summary>
    public void SetValumaMoodSecondsForTest(string playerId, double seconds)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            s.MoodLocationId = _world.LocationId;
            s.MoodSeconds = seconds;
        }
    }
}

/// <summary>The one shapeshifter of a world (2026-09, Valuma) — runtime only; the return time is saved in the metadata.</summary>
internal sealed class SreekmakraState
{
    public string CreatureId { get; set; } = string.Empty;
    public string TargetPlayerId { get; set; } = string.Empty;
    public bool Revealed { get; set; }
    public double NextShiftAt { get; set; }
    public double FleeUntil { get; set; }

    public void Clear()
    {
        CreatureId = string.Empty;
        TargetPlayerId = string.Empty;
        Revealed = false;
        NextShiftAt = 0;
        FleeUntil = 0;
    }
}
