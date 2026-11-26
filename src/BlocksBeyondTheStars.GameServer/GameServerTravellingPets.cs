// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Travelling pets (#4025): a tamed animal of a species with the data flag <c>travels</c> (<see cref="CreatureSpecies.Travels"/>
/// — the wild Soundi of Soaniri, won in a duel) comes along with its tamer to every world instead of living only on the
/// world it was tamed on (<see cref="TamedCreature.Travels"/>):
/// <list type="bullet">
/// <item>While it <b>follows</b> it appears beside its owner on every world but a void one — never in space, on a station or
/// inside the ship, exactly like the story Soundi; it steps out beside its owner after a landing (the ordinary companion
/// spawn with its ground search). When it does <b>not follow</b> it stays home on its home world like any planet pet.</item>
/// <item>It is a <b>pet</b>, not a story companion: it eats, its bond grows and fades, it can be released — and it takes a
/// <b>pet slot on every world</b>, because it can be on every world. The cap rule: on each world, the pets that live there
/// plus all travelling pets stay within <see cref="MaxCompanionsPerWorld"/> (six). A tame needs a free slot on the world it
/// happens on; a travelling one must also fit on every other world, or it stays a planet pet of the world it was tamed on
/// (the player is told) — the duel is never lost.</item>
/// <item><b>Migration</b>: on every load (and after a release frees a slot) the records of a travelling species that are
/// still planet pets — tamed before #4025, or ones that found no room — become travelling pets, oldest first, as long as
/// they fit. The rest stay home, nothing is lost, and they come along as soon as there is room.</item>
/// </list>
/// The robot, Rudolpho and the story Soundi (<see cref="TamedCreature.Roams"/>) are untouched: they still take no pet slot.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The duel's tame line, and its travelling twin.</summary>
    private const string DuelTameKey = "creature.tame.msg.duel";

    private const string DuelTravelsTameKey = "creature.tame.msg.duel_travels";

    /// <summary>The tame line of any other travelling tame (a ritual or a feed-tame of a travelling species).</summary>
    private const string TravelsTameKey = "creature.tame.msg.travels";

    /// <summary>Whether a companion record is a travelling pet (a tamed animal, not the robot, Rudolpho or a story companion).</summary>
    internal static bool IsTravellingPet(TamedCreature tc) => tc.Travels && !tc.Roams && string.IsNullOrEmpty(tc.Kind);

    /// <summary>Whether a travelling pet comes along right now: it follows its owner.</summary>
    private static bool TravelsAlongNow(TamedCreature tc) => IsTravellingPet(tc) && tc.Following;

    /// <summary>Whether a record is a pet that lives on <paramref name="body"/> only (a planet pet there).</summary>
    private static bool PlanetPetOf(TamedCreature tc, string body) => !tc.Roams && !IsTravellingPet(tc) && tc.HomeBodyId == body;

    /// <summary>The pet slots the player uses on <paramref name="body"/>: the planet pets that live there plus every travelling
    /// pet (it can be on every world). The robot, Rudolpho and the story Soundi take none.</summary>
    internal static int PetSlotsUsedOn(PlayerState p, string body)
    {
        int n = 0;
        foreach (var tc in p.TamedCreatures)
        {
            if (IsTravellingPet(tc) || PlanetPetOf(tc, body))
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>The pet slots used on the player's fullest world: every travelling pet plus the most planet pets one world
    /// holds.</summary>
    private static int MostPetSlotsUsed(PlayerState p)
    {
        int travelling = 0, most = 0;
        var perWorld = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var tc in p.TamedCreatures)
        {
            if (IsTravellingPet(tc))
            {
                travelling++;
            }
            else if (!tc.Roams)
            {
                perWorld.TryGetValue(tc.HomeBodyId, out int n);
                perWorld[tc.HomeBodyId] = ++n;
                most = Math.Max(most, n);
            }
        }

        return travelling + most;
    }

    /// <summary>Makes a record of the player's (already in their list) a travelling pet when that keeps every world within the
    /// cap; otherwise it stays a planet pet. Returns whether it travels now.</summary>
    private static bool TryMakeTraveller(PlayerState p, TamedCreature tc)
    {
        tc.Travels = true;
        if (MostPetSlotsUsed(p) <= MaxCompanionsPerWorld)
        {
            return true;
        }

        tc.Travels = false;
        return false;
    }

    /// <summary>Whether a tamed record belongs to a travelling species: its species snapshot says so, or — a record tamed
    /// before the flag existed — the authored record behind its <c>au_</c> species id does. Only tamed animals qualify (no
    /// kind, not roaming), so the story Soundi stays the story companion it is.</summary>
    private bool OfTravellingSpecies(TamedCreature tc)
    {
        if (!string.IsNullOrEmpty(tc.Kind) || tc.Roams)
        {
            return false;
        }

        return tc.Species is { Travels: true } || AuthoredRecordTravels(tc.SpeciesId) || AuthoredRecordTravels(tc.Species?.Id);
    }

    private bool AuthoredRecordTravels(string? speciesId)
        => speciesId is not null && speciesId.StartsWith(AuthoredSpeciesPrefix, StringComparison.Ordinal)
           && _content.AuthoredCreatures.TryGetValue(speciesId.Substring(AuthoredSpeciesPrefix.Length), out var record)
           && record.Travels;

    /// <summary>The migration (see the type): every planet pet of a travelling species becomes a travelling pet, oldest first,
    /// while it fits. Returns the records that changed.</summary>
    private List<TamedCreature> PromoteTravellingPets(PlayerState p)
    {
        var promoted = new List<TamedCreature>();
        foreach (var tc in p.TamedCreatures.OrderBy(t => t.TamedAtUtc).ToList())
        {
            if (!tc.Travels && OfTravellingSpecies(tc) && TryMakeTraveller(p, tc))
            {
                promoted.Add(tc);
            }
        }

        return promoted;
    }

    /// <summary>The tame line of a fresh tame (already in the player's list): a pet of a travelling species becomes a
    /// travelling pet and says so; one that does not fit stays here and the player hears why.</summary>
    private string TravellingTameKey(PlayerSession session, TamedCreature tc, CreatureSpecies sp, string successKey)
    {
        if (!sp.Travels && !OfTravellingSpecies(tc))
        {
            return successKey;
        }

        if (TryMakeTraveller(session.State, tc))
        {
            return successKey == DuelTameKey ? DuelTravelsTameKey : TravelsTameKey;
        }

        Send(session, new ServerMessage { Text = "@srv.companion.travel_full:" + CompanionDisplayName(tc) });
        return successKey;
    }

    /// <summary>After a release: a planet pet of a travelling species that had no room comes along now (told).</summary>
    private void PromoteTravellingPetsAfterRelease(PlayerSession session)
    {
        foreach (var tc in PromoteTravellingPets(session.State))
        {
            Send(session, new ServerMessage { Text = "@srv.companion.travels_now:" + CompanionDisplayName(tc) });
        }
    }

    /// <summary>Test seam: the migration a load runs, on a player's current records. Returns how many changed.</summary>
    public int PromoteTravellingPetsForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s ? PromoteTravellingPets(s.State).Count : 0;
}
