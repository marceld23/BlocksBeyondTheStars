// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The lively places, what a settlement sounds like from close by (docs/developer/LIVELY_PLACES.md): the tavern murmurs in
/// the evening, the workshop rings by day, the well drips round the clock. The emitters are DERIVED from the markers every
/// settlement has anyway — nothing is authored and nothing is stored — so they exist in every world, old or new, and they
/// ride along in the landmark list like an authored sound marker (<see cref="LandmarkEmitter"/>). The client plays the
/// loudest few (one voice per sound) and keeps their hours (<see cref="SoundEmitterTimes"/>).
/// <para>The broad murmur of a place — far voices, steps, a market's bustle — is NOT an emitter: the client plays it as a
/// bed for the place the player stands in (<c>PlaceAmbience</c>).</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The most derived emitters one settlement adds (a metropolis has a dozen workshops; one voice plays anyway).</summary>
    private const int PlaceEmittersPerKind = 3;

    internal const string TavernSound = "amb_place_tavern";
    internal const string WorkshopSound = "amb_place_workshop";
    internal const string WellSound = "amb_place_well";

    /// <summary>#3868: a lively place's market stall by day — crates, pots and baskets close by (the market day's bustle is
    /// the place's bed, not this).</summary>
    internal const string StallSound = "amb_place_stall";

    /// <summary>The sound a settlement marker makes, or null: its clip, how far it carries, how loud, and its hours.</summary>
    private static (string Sound, float Radius, float Volume, byte When)? PlaceSoundOf(string marker) => marker switch
    {
        SettlementGenerator.TavernMarker => (TavernSound, 20f, 0.8f, SoundEmitterTimes.Evening),
        SettlementGenerator.WorkshopMarker => (WorkshopSound, 22f, 0.7f, SoundEmitterTimes.Day),
        SettlementPlanBaker.WellMarker => (WellSound, 12f, 0.55f, SoundEmitterTimes.Always),
        SettlementPlanBaker.StallMarker => (StallSound, 14f, 0.5f, SoundEmitterTimes.Day),
        _ => null,
    };

    /// <summary>The derived emitters of every intact settlement of the active world.</summary>
    private List<LandmarkEmitter> PlaceEmittersOfActiveWorld()
    {
        var emitters = new List<LandmarkEmitter>();
        var counts = new Dictionary<string, int>(System.StringComparer.Ordinal);
        foreach (var s in _settlements)
        {
            if (s.Ruined)
            {
                continue; // nobody drinks in a ruin
            }

            counts.Clear();
            foreach (var (type, pos) in s.Markers)
            {
                if (PlaceSoundOf(type) is not { } sound)
                {
                    continue;
                }

                counts.TryGetValue(sound.Sound, out int count);
                if (count >= PlaceEmittersPerKind)
                {
                    continue;
                }

                counts[sound.Sound] = count + 1;
                var feet = MarkerFeet(s, pos);
                emitters.Add(new LandmarkEmitter
                {
                    Position = new Vector3f(feet.X, feet.Y + 1f, feet.Z),
                    Sound = sound.Sound,
                    Radius = sound.Radius,
                    Volume = sound.Volume,
                    When = sound.When,
                });
            }
        }

        AddStationEmitters(emitters); // aboard a station: its hangar, its canteen, its equipment (GameServerStationLife.cs)
        return emitters;
    }

    /// <summary>Test seam: the derived emitters of the active world's settlements.</summary>
    internal IReadOnlyList<LandmarkEmitter> PlaceEmittersForTest => PlaceEmittersOfActiveWorld();
}
