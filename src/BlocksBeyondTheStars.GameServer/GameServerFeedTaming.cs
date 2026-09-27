// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Feed-taming (#2082, generation 16 — the school club idea of Paul and Ben: "tamed with bananas"). A begging species loves one
/// food (<see cref="CreatureSpecies.FavouriteFood"/>). When the winner of a squabble eats a piece that is its species' favourite,
/// the meal counts toward the player who threw it; after <see cref="HerdRules.FeedsToTameFor"/> favourite meals (two by default)
/// the animal that ate the last one becomes that player's companion — no translator, no ritual.
/// <para>The meals count per player and HERD (species on this world), not per animal: a squabble's winner is whoever gets there
/// first, and a child who threw two bananas must not be told that two different animals ate them. The count lives on the
/// session (transient, like the animals themselves) and restarts after a tame. Toxic food never counts — a toxic banana is a
/// different item, and the detoxifier washes it. The tame itself runs a tick later (<see cref="ResolvePendingFeedTames"/>):
/// the meal is eaten inside the creature loop, and the tame removes the wild animal from that list.</para>
/// </summary>
public sealed partial class GameServer
{
    private readonly List<(string ThrowerId, string CreatureId, int Meals)> _pendingFeedTames = new();

    /// <summary>The winner of a squabble ate <paramref name="food"/>: counts a favourite meal toward its thrower and queues the
    /// tame when the count is reached.</summary>
    private void OnCreatureAte(CombatEntity c, CreatureSpecies sp, ThrownFood food)
    {
        if (c.IsCompanion || !HerdRules.TamesByFeeding(sp) || !HerdRules.IsFavouriteFood(sp, food.Item) || IsSreekmakra(c))
        {
            return;
        }

        var thrower = FindSessionByPlayerId(food.ThrowerId);
        if (thrower is null)
        {
            return;
        }

        string key = FavouriteMealKey(sp);
        int meals = (thrower.FavouriteMeals.TryGetValue(key, out int m) ? m : 0) + 1;
        if (meals < HerdRules.FeedsToTameFor(sp))
        {
            thrower.FavouriteMeals[key] = meals;
            ShipAiHintOnce(thrower, "favourite_food"); // VEGA: keep feeding them what they love, and one of them stays
            return;
        }

        thrower.FavouriteMeals.Remove(key);
        _pendingFeedTames.Add((thrower.State.PlayerId, c.Id, meals));
    }

    /// <summary>Completes the queued feed-tames (called before the creature loop): the animal must still be wild and here, the
    /// thrower still in this world; the tame goes through the translator ritual's completion (companion cap, knowledge, name,
    /// the Companions tab) with its own message.</summary>
    private void ResolvePendingFeedTames()
    {
        if (_pendingFeedTames.Count == 0)
        {
            return;
        }

        foreach (var (throwerId, creatureId, meals) in _pendingFeedTames)
        {
            var session = FindSessionByPlayerId(throwerId);
            var creature = _creatures.Find(x => x.Id == creatureId && !x.IsCompanion);
            if (session is null || creature is null || !_speciesById.TryGetValue(creature.SpeciesId, out var sp))
            {
                continue;
            }

            var attempt = new TameAttempt { CreatureId = creature.Id, Trust = meals, Required = meals };
            CompleteTame(session, attempt, creature, sp, "creature.tame.msg.fed");
        }

        _pendingFeedTames.Clear();
    }

    /// <summary>The meal counter's key: the herd — this world's species.</summary>
    private string FavouriteMealKey(CreatureSpecies sp) => _world.LocationId + ":" + sp.Id;

    /// <summary>Test hook: how many favourite meals of this species the player has thrown toward the next tame.</summary>
    public int FavouriteMealsForTest(string playerId, string speciesId)
        => FindSessionByPlayerId(playerId) is { } s && s.FavouriteMeals.TryGetValue(_world.LocationId + ":" + speciesId, out int m) ? m : 0;
}
