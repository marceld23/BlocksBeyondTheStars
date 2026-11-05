// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The distant-fauna roster (#3245): a client only learns about the animals near it, so the server tells it the species
/// of the world it is in — the voice seed and voice traits of each, plus when it is awake — and the client lets those
/// species call from far away. Sent once per world entry: every path that puts a player into a world (the join, a travel,
/// a respawn, a station or ship interior, a ship visit) sends the environment after its <c>WorldReset</c>, and the
/// environment send carries the roster whenever this client has not been sent the active world's table yet. The
/// environment heartbeat therefore costs one reference compare per player; a roster that changed under a player (an
/// admin summon adding a species) reaches them on the next beat.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Sends <paramref name="session"/> the active world's <see cref="FaunaRoster"/> unless it already has it. The
    /// caller has made the session's world the active one (as every environment send does). With the creature-abundance
    /// rule off no animal lives anywhere, so none may call from afar either: the roster is empty — and since the table
    /// compared is then the shared empty one, switching the rule live re-sends the roster on the next beat.</summary>
    private void SendFaunaRosterIfDue(PlayerSession session)
    {
        var roster = Rules.CreatureAbundance == Shared.Configuration.AlienActivity.Off
            ? System.Array.Empty<CreatureSpecies>()
            : _speciesRoster;
        if (ReferenceEquals(session.FaunaRosterSent, roster))
        {
            return;
        }

        session.FaunaRosterSent = roster;
        Send(session, BuildFaunaRoster(roster));
    }

    /// <summary>The wire form of a species table — the world's own species, in roster order.</summary>
    internal static FaunaRoster BuildFaunaRoster(IReadOnlyList<CreatureSpecies> roster)
    {
        var species = new List<NetFaunaSpecies>(roster.Count);
        foreach (var sp in roster)
        {
            species.Add(new NetFaunaSpecies
            {
                SpeciesId = sp.Id,
                VoiceSeed = sp.VoiceSeed,
                Habitat = sp.Habitat.ToString(),
                Temperament = sp.Temperament.ToString(),
                BodyPlan = sp.BodyPlan.ToString(),
                Size = sp.Size,
                Legs = sp.Legs,
                Eyes = sp.Eyes,
                Horns = sp.Horns,
                BodySegments = sp.BodySegments,
                Tentacles = sp.Tentacles,
                HasGasSac = sp.HasGasSac,
                Glows = sp.Glows,
                Activity = sp.Activity.ToString(),
            });
        }

        return new FaunaRoster { Species = species };
    }
}
