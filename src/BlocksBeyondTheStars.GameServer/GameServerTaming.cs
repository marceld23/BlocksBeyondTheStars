// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Creature taming + companions (design: <c>docs/developer/CREATURE_TAMING.md</c>). A <b>Creature Translator</b>
/// gadget starts a short bonding ritual on a wild creature: decode its mood/need, respond the right way
/// (bait / calm / approach / give space) to earn hidden <b>trust</b>; reach the threshold and it becomes a
/// named <b>companion</b> bound to the world it was tamed on. Difficulty scales with the species'
/// temperament + exotic-ness, and a per-individual seed randomises its personality (preferred bait,
/// patience). Companions follow their owner on their home world, are invulnerable + harmless, are saved as
/// per-player state, and a first tame of a species grants research knowledge.
/// </summary>
public sealed partial class GameServer
{
    // --- balance ---
    private const float TameDecodeRange = 6f;        // a wild creature this close to the aim point can be engaged
    private const float TameLoseRange = 12f;         // an attempt drops if the creature drifts this far from the player
    private const double CompanionHoldSeconds = 5.0; // a creature in an active attempt is held in place this long
    private const float CompanionFollowDistance = 4f; // a companion keeps roughly this gap from its owner
    private const int MaxCompanionsPerWorld = 6;     // per player, per body

    /// <summary>One in-progress taming attempt per player (transient — never persisted).</summary>
    private sealed class TameAttempt
    {
        public string CreatureId = string.Empty;
        public int Trust;
        public int Required;
        public int Strikes;
        public int Patience;
        public int Step;
    }

    private readonly Dictionary<string, TameAttempt> _tameAttempts = new();

    // ---------------------------------------------------------------------------------------------
    // Decode (translator gadget) → start / refresh an attempt on the nearest wild creature.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Routed from the gadget handler when the player uses the <c>creature_translator</c>: finds the
    /// nearest wild creature to the aim point, starts (or refreshes) a taming attempt on it and reports the
    /// decoded mood + what it wants now.</summary>
    private void UseCreatureTranslator(PlayerSession session, Vector3f target)
    {
        var p = session.State;
        var creature = NearestWildCreature(target, TameDecodeRange)
                       ?? NearestWildCreature(p.Position, TameDecodeRange);
        if (creature is null || !_speciesById.TryGetValue(creature.SpeciesId, out var sp))
        {
            Send(session, new TameProgress { Active = false, MessageKey = "creature.tame.msg.gone" });
            _tameAttempts.Remove(p.PlayerId);
            return;
        }

        if (!_tameAttempts.TryGetValue(p.PlayerId, out var attempt) || attempt.CreatureId != creature.Id)
        {
            attempt = new TameAttempt
            {
                CreatureId = creature.Id,
                Required = RequiredTrust(sp, creature.Id, creature.SizeScale),
                Patience = PatienceFor(sp, creature.Id),
            };
            _tameAttempts[p.PlayerId] = attempt;
        }

        creature.FrozenTimer = System.Math.Max(creature.FrozenTimer, CompanionHoldSeconds); // hold its attention
        SendTameStep(session, creature, sp, attempt,
            attempt.Step == 0 ? "creature.tame.msg.start" : string.Empty);
    }

    // ---------------------------------------------------------------------------------------------
    // Respond → resolve one step of the ritual.
    // ---------------------------------------------------------------------------------------------

    private void HandleTameRespond(PlayerSession session, TameRespondIntent intent)
    {
        var p = session.State;
        if (intent.Response == "cancel")
        {
            _tameAttempts.Remove(p.PlayerId);
            Send(session, new TameProgress { Active = false });
            return;
        }

        if (!_tameAttempts.TryGetValue(p.PlayerId, out var attempt) || attempt.CreatureId != intent.CreatureId)
        {
            Send(session, new TameProgress { Active = false }); // must decode with the translator first
            return;
        }

        var creature = _creatures.FirstOrDefault(c => c.Id == intent.CreatureId && !c.IsCompanion);
        if (creature is null || !_speciesById.TryGetValue(creature.SpeciesId, out var sp)
            || WrapDistSq(p.Position, creature.Position) > TameLoseRange * TameLoseRange)
        {
            _tameAttempts.Remove(p.PlayerId);
            Send(session, new TameResult { CreatureId = intent.CreatureId, Success = false, MessageKey = "creature.tame.msg.gone" });
            return;
        }

        string need = NeedForStep(sp, creature.Id, attempt.Step);
        if (intent.Response == need)
        {
            ResolveCorrect(session, attempt, creature, sp, need);
        }
        else
        {
            ResolveWrong(session, attempt, creature, sp);
        }
    }

    private void ResolveCorrect(PlayerSession session, TameAttempt attempt, CombatEntity creature, CreatureSpecies sp, string need)
    {
        var p = session.State;
        if (need == "feed")
        {
            string bait = PreferredBait(creature.Id);
            if (!p.Inventory.Has(bait, 1))
            {
                // right idea, no bait in hand — no progress, tell them exactly what it wants.
                SendTameStep(session, creature, sp, attempt, "creature.tame.msg.nobait");
                return;
            }

            p.Inventory.Remove(bait, 1);
            SendInventory(session);
        }

        attempt.Trust++;
        attempt.Step++;
        creature.FrozenTimer = System.Math.Max(creature.FrozenTimer, CompanionHoldSeconds);

        if (attempt.Trust >= attempt.Required)
        {
            CompleteTame(session, attempt, creature, sp);
            return;
        }

        SendTameStep(session, creature, sp, attempt, "creature.tame.msg.good");
    }

    private void ResolveWrong(PlayerSession session, TameAttempt attempt, CombatEntity creature, CreatureSpecies sp)
    {
        var p = session.State;
        attempt.Strikes++;

        bool bolts = sp.Temperament == CreatureTemperament.Skittish || attempt.Strikes > attempt.Patience;
        if (!bolts)
        {
            SendTameStep(session, creature, sp, attempt, "creature.tame.msg.wrong"); // forgiving — try again
            return;
        }

        _tameAttempts.Remove(p.PlayerId);
        if (CreatureBehaviour.RetaliatesWhenAttacked(sp.Temperament) && sp.Temperament != CreatureTemperament.Skittish)
        {
            // territorial / aggressive / pack-hunter: a botched approach provokes it.
            creature.FrozenTimer = 0;
            ProvokeCreature(creature);
            BroadcastCreatures();
            Send(session, new TameResult { CreatureId = creature.Id, Success = false, MessageKey = "creature.tame.msg.provoked" });
        }
        else
        {
            // skittish / passive: it bolts away.
            creature.FrozenTimer = 0;
            ShoveCreatureFrom(creature, p.Position, 6f);
            BroadcastCreatures();
            Send(session, new TameResult { CreatureId = creature.Id, Success = false, MessageKey = "creature.tame.msg.spooked" });
        }
    }

    private void CompleteTame(PlayerSession session, TameAttempt attempt, CombatEntity creature, CreatureSpecies sp)
    {
        var p = session.State;
        string body = _world.LocationId;

        if (p.TamedCreatures.Count(t => t.HomeBodyId == body) >= MaxCompanionsPerWorld)
        {
            _tameAttempts.Remove(p.PlayerId);
            Send(session, new TameResult { CreatureId = creature.Id, Success = false, MessageKey = "creature.tame.msg.full" });
            return;
        }

        var tc = new TamedCreature
        {
            Id = NextEntityId(),
            HomeBodyId = body,
            Name = DefaultCompanionName(sp, p),
            SpeciesId = creature.SpeciesId,
            Species = CloneSpecies(sp),
            SizeScale = creature.SizeScale,
            Bond = System.Math.Clamp(40 + attempt.Trust * 8, 0, 100),
            TamedAtUtc = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        p.TamedCreatures.Add(tc);

        // First tame of this species (per world) pays full research knowledge; later ones a small trickle.
        string signature = body + ":" + creature.SpeciesId;
        int reward;
        if (p.TamedSpecies.Add(signature))
        {
            reward = TameKnowledgeReward(sp, creature.SizeScale);
        }
        else
        {
            reward = 1;
        }

        p.KnowledgePoints += reward;

        // The wild animal becomes the companion in place.
        _creatures.Remove(creature);
        SpawnCompanionEntity(p.PlayerId, tc, creature.Position);
        _tameAttempts.Remove(p.PlayerId);
        _repo.SavePlayer(p);

        BroadcastCreatures();
        SendPlayerState(session); // push the new knowledge total to the HUD
        Send(session, new TameResult
        {
            CreatureId = creature.Id,
            Success = true,
            CompanionId = tc.Id,
            CompanionName = tc.Name,
            MessageKey = "creature.tame.msg.success",
            KnowledgeGained = reward,
            KnowledgeTotal = p.KnowledgePoints,
        });
        SendCompanions(session);
    }

    private void SendTameStep(PlayerSession session, CombatEntity creature, CreatureSpecies sp, TameAttempt attempt, string messageKey)
    {
        string need = NeedForStep(sp, creature.Id, attempt.Step);
        var msg = new TameProgress
        {
            CreatureId = creature.Id,
            CreatureName = sp.Name,
            Active = true,
            MoodKey = MoodForNeed(need),
            NeedKey = "creature.tame.need." + need,
            MessageKey = messageKey,
            Trust = attempt.Trust,
            Required = attempt.Required,
        };
        if (need == "feed")
        {
            msg.BaitItem = PreferredBait(creature.Id);
            msg.BaitKey = "creature.tame.bait." + msg.BaitItem;
        }

        Send(session, msg);
    }

    // ---------------------------------------------------------------------------------------------
    // Difficulty + per-individual personality (deterministic from species + entity id).
    // ---------------------------------------------------------------------------------------------

    /// <summary>How much trust (correct steps) taming this individual needs — harder for wary/hostile
    /// temperaments, exotic habitats, glowing + oversized animals, plus a ±1 per-individual "shyness".</summary>
    private static int RequiredTrust(CreatureSpecies sp, string creatureId, float sizeScale)
    {
        int t = sp.Temperament switch
        {
            CreatureTemperament.Passive => 2,
            CreatureTemperament.Skittish => 3,
            CreatureTemperament.Territorial => 3,
            CreatureTemperament.Aggressive => 4,
            CreatureTemperament.PackHunter => 5,
            _ => 3,
        };
        if (sp.Habitat is CreatureHabitat.Cave or CreatureHabitat.Lava or CreatureHabitat.Air)
        {
            t += 1;
        }

        if (sp.Glows)
        {
            t += 1;
        }

        if (sizeScale >= 1.2f)
        {
            t += 1;
        }

        t += (int)(Hash(creatureId, "shy") % 3) - 1; // -1 / 0 / +1
        return System.Math.Clamp(t, 2, 7);
    }

    /// <summary>Wrong responses tolerated before the creature gives up/bolts. Skittish species bolt at the
    /// first mistake; others forgive 0–4 depending on temperament + a small per-individual variance.</summary>
    private static int PatienceFor(CreatureSpecies sp, string creatureId)
    {
        if (sp.Temperament == CreatureTemperament.Skittish)
        {
            return 0;
        }

        int basePatience = sp.Temperament switch
        {
            CreatureTemperament.Passive => 3,
            CreatureTemperament.Territorial => 1,
            CreatureTemperament.Aggressive => 1,
            CreatureTemperament.PackHunter => 0,
            _ => 2,
        };
        return basePatience + (int)(Hash(creatureId, "pat") % 2);
    }

    /// <summary>This individual's preferred bait item (randomised per animal so same-species creatures differ).</summary>
    private static string PreferredBait(string creatureId)
    {
        string[] baits = { "forage_bait", "meat_bait", "nectar_lure" };
        return baits[Hash(creatureId, "bait") % (uint)baits.Length];
    }

    /// <summary>What the creature wants on a given step (deterministic per creature + step), skewed by
    /// temperament: grazers want feeding/approach, shy ones want space, hostile ones want calming.</summary>
    private static string NeedForStep(CreatureSpecies sp, string creatureId, int step)
    {
        uint h = Hash(creatureId, "need:" + step);
        return sp.Temperament switch
        {
            CreatureTemperament.Skittish => (h % 3) == 1 ? "feed" : "space",
            CreatureTemperament.Territorial or CreatureTemperament.Aggressive or CreatureTemperament.PackHunter
                => (h % 3) == 1 ? "feed" : "calm",
            _ => (h % 3) == 1 ? "approach" : "feed",
        };
    }

    private static string MoodForNeed(string need) => need switch
    {
        "feed" => "creature.tame.mood.hungry",
        "calm" => "creature.tame.mood.hostile",
        "approach" => "creature.tame.mood.curious",
        _ => "creature.tame.mood.wary",
    };

    /// <summary>Research knowledge from a first tame of a species — scaled by the same difficulty inputs.</summary>
    private static int TameKnowledgeReward(CreatureSpecies sp, float sizeScale)
    {
        int k = sp.Temperament switch
        {
            CreatureTemperament.Passive => 2,
            CreatureTemperament.Skittish => 3,
            CreatureTemperament.Territorial => 4,
            CreatureTemperament.Aggressive => 5,
            CreatureTemperament.PackHunter => 6,
            _ => 3,
        };
        if (sp.Habitat is CreatureHabitat.Cave or CreatureHabitat.Lava or CreatureHabitat.Air)
        {
            k += 2;
        }

        if (sp.Glows)
        {
            k += 1;
        }

        if (sizeScale >= 1.2f)
        {
            k += 1;
        }

        return k;
    }

    private static uint Hash(string id, string salt)
        => (uint)StableStringHash(id + ":" + salt);

    // ---------------------------------------------------------------------------------------------
    // Companion entities (live followers) + reconciliation with player presence.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Players currently on this world (on foot or aboard their parked ship — but not in space),
    /// i.e. those whose companions should be materialised here.</summary>
    private IEnumerable<PlayerSession> CompanionOwnersHere()
        => JoinedInActiveWorld().Where(s => !InSpace(s.State.PlayerId));

    private int CompanionCountFor(string ownerId) => _creatures.Count(c => c.OwnerId == ownerId);

    /// <summary>Despawns companions whose owner left this world and (re)spawns each present owner's home-world
    /// companions. Called every creature tick; returns true if the live creature list changed.</summary>
    private bool ReconcileCompanions()
    {
        var present = CompanionOwnersHere().ToList();
        string body = _world.LocationId;

        // A live companion is valid here only if its owner is present AND it is still a record bound to this
        // world — so it despawns when the owner leaves, when it's released, or if it isn't a home-world pet.
        var valid = new HashSet<string>();
        foreach (var s in present)
        {
            foreach (var tc in s.State.TamedCreatures)
            {
                if (tc.HomeBodyId == body)
                {
                    valid.Add(tc.Id);
                }
            }
        }

        int before = _creatures.Count;
        _creatures.RemoveAll(c => c.IsCompanion && !valid.Contains(c.CompanionId));
        bool changed = _creatures.Count != before;

        foreach (var s in present)
        {
            foreach (var tc in s.State.TamedCreatures)
            {
                if (tc.HomeBodyId != body)
                {
                    continue;
                }

                if (_creatures.Any(c => c.CompanionId == tc.Id))
                {
                    continue;
                }

                if (CompanionCountFor(s.State.PlayerId) >= MaxCompanionsPerWorld)
                {
                    break;
                }

                SpawnCompanionEntity(s.State.PlayerId, tc, s.State.Position);
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>Immediately materialises one session's home-world companions so they appear in the first
    /// creature snapshot on entry (join / land / respawn). Idempotent; despawn of departed owners is left to
    /// <see cref="ReconcileCompanions"/> on the next tick.</summary>
    private void SpawnCompanionsForSession(PlayerSession session)
    {
        if (InSpace(session.State.PlayerId))
        {
            return;
        }

        string body = _world.LocationId;
        foreach (var tc in session.State.TamedCreatures)
        {
            if (tc.HomeBodyId != body || _creatures.Any(c => c.CompanionId == tc.Id))
            {
                continue;
            }

            if (CompanionCountFor(session.State.PlayerId) >= MaxCompanionsPerWorld)
            {
                break;
            }

            SpawnCompanionEntity(session.State.PlayerId, tc, session.State.Position);
        }
    }

    private void SpawnCompanionEntity(string ownerId, TamedCreature tc, Vector3f near)
    {
        // Make the species resolvable for movement/rendering even if it isn't in the live roster.
        if (!_speciesById.ContainsKey(tc.SpeciesId))
        {
            _speciesById[tc.SpeciesId] = tc.Species;
        }

        var sp = _speciesById[tc.SpeciesId];
        float ang = (Hash(tc.Id, "spawn") % 360) * (float)(System.Math.PI / 180.0);
        var pos = new Vector3f(near.X + (float)System.Math.Cos(ang) * 3f, near.Y, near.Z + (float)System.Math.Sin(ang) * 3f);
        pos = AdjustHabitatHeight(sp, pos);

        _creatures.Add(new CombatEntity
        {
            Id = NextEntityId(),
            Kind = CombatEntityKind.Creature,
            SpeciesId = tc.SpeciesId,
            Hostile = false,
            Hull = sp.MaxHealth,
            HullMax = sp.MaxHealth,
            Position = pos,
            DamagePerSecond = 0f,
            SizeScale = tc.SizeScale,
            OwnerId = ownerId,
            CompanionId = tc.Id,
            CustomName = tc.Name,
        });
    }

    /// <summary>Advances one companion: it follows its owner (or holds position if the owner isn't here).</summary>
    private void MoveCompanion(CombatEntity c, double moveDt)
    {
        if (!_speciesById.TryGetValue(c.SpeciesId, out var sp))
        {
            return;
        }

        var owner = FindSessionByPlayerId(c.OwnerId);
        if (owner is null || InSpace(c.OwnerId))
        {
            return; // owner not on this world — hold (reconciliation will despawn it)
        }

        var profile = ProfileFor(c.SpeciesId);
        var res = LocomotionController.FollowStep(
            c.Loco, profile, c.Position, owner.State.Position, CompanionFollowDistance, moveDt, Hash(c.Id, "wander"));
        c.Loco = res.State;
        var next = AdjustHabitatHeight(sp, res.Position, res.VertWave, profile);
        c.Position = EntityBlockedByShip(next) ? c.Position : next;
    }

    // ---------------------------------------------------------------------------------------------
    // Rename / release / roster (Companions menu).
    // ---------------------------------------------------------------------------------------------

    private void HandleSetCompanionName(PlayerSession session, SetCompanionNameIntent intent)
    {
        var tc = session.State.TamedCreatures.FirstOrDefault(t => t.Id == intent.CompanionId);
        if (tc is null)
        {
            return;
        }

        tc.Name = SanitizeName(intent.Name);
        foreach (var c in _creatures.Where(c => c.CompanionId == tc.Id))
        {
            c.CustomName = tc.Name;
        }

        _repo.SavePlayer(session.State);
        BroadcastCreatures();
        SendCompanions(session);
    }

    private void HandleReleaseCompanion(PlayerSession session, ReleaseCompanionIntent intent)
    {
        var tc = session.State.TamedCreatures.FirstOrDefault(t => t.Id == intent.CompanionId);
        if (tc is null)
        {
            return;
        }

        session.State.TamedCreatures.Remove(tc);
        _creatures.RemoveAll(c => c.CompanionId == tc.Id);
        _repo.SavePlayer(session.State);
        BroadcastCreatures();
        SendCompanions(session);
    }

    private void HandleRequestCompanions(PlayerSession session) => SendCompanions(session);

    private void SendCompanions(PlayerSession session)
    {
        var list = session.State.TamedCreatures.Select(tc =>
        {
            var sp = tc.Species;
            return new NetCompanion
            {
                Id = tc.Id,
                Name = tc.Name,
                SpeciesName = sp.Name,
                HomeBodyId = tc.HomeBodyId,
                HomeBodyName = BodyDisplayName(tc.HomeBodyId),
                Present = _creatures.Any(c => c.CompanionId == tc.Id),
                Bond = tc.Bond,
                Temperament = sp.Temperament.ToString(),
                Habitat = sp.Habitat.ToString(),
                Size = sp.Size * tc.SizeScale,
                Legs = sp.Legs,
                HasWings = sp.HasWings,
                HasTail = sp.HasTail,
                BodySegments = sp.BodySegments,
                ColorRgb = sp.ColorRgb,
                BellyRgb = sp.BellyRgb,
                Glows = sp.Glows,
                Eyes = sp.Eyes,
                Horns = sp.Horns,
                HasCrest = sp.HasCrest,
                Tentacles = sp.Tentacles,
                EyeStalks = sp.EyeStalks,
                HasGasSac = sp.HasGasSac,
            };
        }).ToArray();

        Send(session, new CompanionList { Companions = list });
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers.
    // ---------------------------------------------------------------------------------------------

    private CombatEntity? NearestWildCreature(Vector3f at, float range)
    {
        CombatEntity? best = null;
        double bestSq = range * range;
        foreach (var c in _creatures)
        {
            if (c.IsCompanion)
            {
                continue;
            }

            double d = WrapDistSq(at, c.Position);
            if (d <= bestSq)
            {
                bestSq = d;
                best = c;
            }
        }

        return best;
    }

    /// <summary>Pushes a creature a few blocks directly away from a point (a visible "bolt" when spooked).</summary>
    private void ShoveCreatureFrom(CombatEntity creature, Vector3f from, float blocks)
    {
        float dx = creature.Position.X - from.X;
        float dz = creature.Position.Z - from.Z;
        double len = System.Math.Sqrt(dx * dx + dz * dz);
        if (len < 1e-3)
        {
            return;
        }

        var fled = new Vector3f(
            creature.Position.X + (float)(dx / len) * blocks,
            creature.Position.Y,
            creature.Position.Z + (float)(dz / len) * blocks);
        creature.Position = _speciesById.TryGetValue(creature.SpeciesId, out var sp) ? AdjustHabitatHeight(sp, fled) : fled;
    }

    private string BodyDisplayName(string bodyId)
        => _galaxy?.FindBody(bodyId) is { } b && !string.IsNullOrEmpty(b.Name) ? b.Name : bodyId;

    private static string DefaultCompanionName(CreatureSpecies sp, Shared.State.PlayerState p)
    {
        int n = p.TamedCreatures.Count(t => t.SpeciesId == sp.Id) + 1;
        string baseName = string.IsNullOrEmpty(sp.Name) ? "Companion" : sp.Name;
        return n <= 1 ? baseName : $"{baseName} {n}";
    }

    private static string SanitizeName(string? name)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length > 24)
        {
            name = name[..24];
        }

        return name;
    }

    /// <summary>Deep-copies a species descriptor so a companion's snapshot is independent of the live roster.</summary>
    private static CreatureSpecies CloneSpecies(CreatureSpecies s) => new()
    {
        Id = s.Id,
        NameKey = s.NameKey,
        Name = s.Name,
        Habitat = s.Habitat,
        Activity = s.Activity,
        Temperament = s.Temperament,
        Size = s.Size,
        MaxHealth = s.MaxHealth,
        Speed = s.Speed,
        AttackDamage = s.AttackDamage,
        Legs = s.Legs,
        HasWings = s.HasWings,
        HasTail = s.HasTail,
        BodySegments = s.BodySegments,
        ColorRgb = s.ColorRgb,
        Eyes = s.Eyes,
        Horns = s.Horns,
        HasCrest = s.HasCrest,
        Tentacles = s.Tentacles,
        EyeStalks = s.EyeStalks,
        HasGasSac = s.HasGasSac,
        BellyRgb = s.BellyRgb,
        Glows = s.Glows,
        BiomeAffinity = s.BiomeAffinity,
        DropItem = s.DropItem,
        DropCount = s.DropCount,
        DropKind = s.DropKind,
    };

    // ---------------------------------------------------------------------------------------------
    // Test hooks.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Test/util: run the translator decode on the nearest creature to the player.</summary>
    public void TameDecodeForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            UseCreatureTranslator(s, s.State.Position);
        }
    }

    /// <summary>Test/util: send a taming response for the player's current attempt; returns the creature id
    /// being tamed (empty if no attempt).</summary>
    public string TameRespondForTest(string playerId, string response)
    {
        if (FindSessionByPlayerId(playerId) is not { } s || !_tameAttempts.TryGetValue(playerId, out var a))
        {
            return string.Empty;
        }

        string id = a.CreatureId;
        HandleTameRespond(s, new TameRespondIntent { CreatureId = id, Response = response });
        return id;
    }

    /// <summary>Test/util: the need ("feed"/"calm"/"approach"/"space") the player's current attempt wants now.</summary>
    public string TameCurrentNeedForTest(string playerId)
    {
        if (!_tameAttempts.TryGetValue(playerId, out var a))
        {
            return string.Empty;
        }

        var creature = _creatures.FirstOrDefault(c => c.Id == a.CreatureId);
        if (creature is null || !_speciesById.TryGetValue(creature.SpeciesId, out var sp))
        {
            return string.Empty;
        }

        return NeedForStep(sp, creature.Id, a.Step);
    }

    /// <summary>Test/util: the player's tamed-creature records.</summary>
    public IReadOnlyList<TamedCreature> TamedCreaturesForTest(string playerId)
        => FindSessionByPlayerId(playerId)?.State.TamedCreatures ?? (IReadOnlyList<TamedCreature>)System.Array.Empty<TamedCreature>();

    /// <summary>Test/util: live companion entities owned by the player on the active world.</summary>
    public IReadOnlyList<CombatEntity> CompanionEntitiesForTest(string ownerId)
        => _creatures.Where(c => c.OwnerId == ownerId).ToList();

    /// <summary>Test/util: force companion reconciliation (spawn present owners' pets / despawn absent ones).</summary>
    public void ReconcileCompanionsForTest() => ReconcileCompanions();

    /// <summary>Test/util: rename a companion as the rename intent would.</summary>
    public void RenameCompanionForTest(string playerId, string companionId, string name)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleSetCompanionName(s, new SetCompanionNameIntent { CompanionId = companionId, Name = name });
        }
    }

    /// <summary>Test/util: release a companion as the release intent would.</summary>
    public void ReleaseCompanionForTest(string playerId, string companionId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleReleaseCompanion(s, new ReleaseCompanionIntent { CompanionId = companionId });
        }
    }
}
