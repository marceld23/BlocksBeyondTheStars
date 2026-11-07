// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>#3361: a story companion's song clock on its live entity (server-only, never persisted).</summary>
public sealed partial class CombatEntity
{
    /// <summary>Uptime of the companion's next song; 0 = not armed yet.</summary>
    public double NextSongAt { get; set; }

    /// <summary>How many songs it sang on this visit — picks the next tune.</summary>
    public int SongsSung { get; set; }
}

/// <summary>
/// Story companions (#3361): a companion a story grants — the Sounders pack's Soundi after the sing duel. It is a
/// <see cref="TamedCreature"/> with a <see cref="TamedCreature.Kind"/> that is no authored companion's (no item behind it):
/// <list type="bullet">
/// <item>It <b>travels</b> (<see cref="TamedCreature.Roams"/>): it follows its owner to every world but a void one, stays
/// outside the six-pet cap and waits <b>on board</b> when it does not follow (#3344) — "release" puts it on board instead,
/// it is never lost.</item>
/// <item><b>One per player and kind</b>; a fixed bond of 100 that never fades (it is not fed); passive and invulnerable like
/// every companion; nothing to bio-sample.</item>
/// <item>Its species is the authored record of <c>data/creatures.json</c> (the Soundi: light blue fur with mint patches, huge
/// pointed ears, no eyes, a wide smile, a bushy tail), so it looks exactly like the wild ones; a code-built stand-in only
/// when the record is missing.</item>
/// <item>The client's name prompt opens with the story's suggested name ("Muffin") filled in
/// (<see cref="TameResult.SuggestedName"/>).</item>
/// <item>The Soundi sings now and then (<c>soundi_call_01</c> … <c>soundi_call_14</c>, a <see cref="SoundFx"/> at its
/// position).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The roster id prefix of an authored species (<c>"au_" + key</c>, as <c>CreatureGenerator</c> builds it).</summary>
    private const string AuthoredSpeciesPrefix = "au_";

    /// <summary>The Soundi's song cues (the assets agent's <c>client/Assets/Resources/audio/soundi_call_NN.mp3</c>).</summary>
    internal const string SoundiCallPrefix = "soundi_call_";

    /// <summary>How many song cues the Soundi has.</summary>
    internal const int SoundiCallCount = 14;

    /// <summary>The first song comes this long after the companion appears…</summary>
    private const double StorySongFirstSeconds = 20.0;

    /// <summary>…and the next ones every <see cref="StorySongMinSeconds"/> plus up to <see cref="StorySongSpreadSeconds"/>.</summary>
    private const double StorySongMinSeconds = 45.0;

    private const double StorySongSpreadSeconds = 60.0;

    /// <summary>Per world: the uptime of its next song scan (1 Hz).</summary>
    private readonly Dictionary<string, double> _storySongNextAt = new(StringComparer.Ordinal);

    /// <summary>Whether a companion was granted by a story (a kind that is not the robot's or Rudolpho's).</summary>
    internal static bool IsStoryCompanion(TamedCreature tc) => !string.IsNullOrEmpty(tc.Kind) && AuthoredCompanionOf(tc.Kind) is null;

    /// <summary>Whether a companion's bond is fixed (never fades, not fed): the authored and the story companions.</summary>
    internal static bool HasFixedBond(TamedCreature tc) => IsAuthoredCompanion(tc) || IsStoryCompanion(tc);

    /// <summary>
    /// Grants a story companion — the lead's call after the sing duel: <c>GrantStoryCompanion(session, "soundi", "soundi",
    /// "Muffin")</c>. Creates a travelling companion of <paramref name="kind"/> with the species of the authored record
    /// <paramref name="speciesId"/> (a key of <c>data/creatures.json</c>, with or without the <c>au_</c> prefix), puts it
    /// beside the player when they stand on a world, saves the player and opens the client's name prompt with
    /// <paramref name="suggestedName"/> filled in (the companion is called that until the player picks another name).
    /// Returns the new record, or null when the player already has a companion of that kind (one per player and kind) or the
    /// kind is the robot's or Rudolpho's (those come from their items).
    /// </summary>
    public TamedCreature? GrantStoryCompanion(PlayerSession session, string kind, string speciesId, string suggestedName)
    {
        var p = session.State;
        kind = (kind ?? string.Empty).Trim();
        if (kind.Length == 0 || AuthoredCompanionOf(kind) is not null || p.TamedCreatures.Any(t => t.Kind == kind))
        {
            return null;
        }

        var species = StoryCompanionSpecies(speciesId ?? string.Empty);
        string name = SanitizeName(suggestedName);
        if (name.Length == 0)
        {
            name = string.IsNullOrEmpty(species.Name) ? kind : species.Name;
        }

        long now = NowUnixMs();
        var tc = new TamedCreature
        {
            Id = kind + "_" + Guid.NewGuid().ToString("N").Substring(0, 12),
            HomeBodyId = session.CurrentLocationId,
            Name = name,
            SpeciesId = species.Id,
            Species = species,
            SizeScale = 1f,
            Bond = 100,
            TamedAtUtc = now,
            LastFedUtc = now,
            Kind = kind,
            Roams = true,
        };
        p.TamedCreatures.Add(tc);
        _repo.SavePlayer(p);
        SpawnStoryCompanion(session, tc);
        SendCompanions(session);
        Send(session, new TameResult
        {
            Success = true,
            CompanionId = tc.Id,
            CompanionName = tc.Name,
            SuggestedName = name,
            MessageKey = "creature.tame.msg.story",
            KnowledgeTotal = p.KnowledgePoints,
        });
        return tc;
    }

    /// <summary>Puts a freshly granted story companion beside its owner — when the owner stands on the world being ticked
    /// (never in space or on a void world; it appears at the next landing otherwise).</summary>
    private void SpawnStoryCompanion(PlayerSession session, TamedCreature tc)
    {
        if (session.CurrentLocationId != _world.LocationId || InSpace(session.State.PlayerId) || !CompanionLivesOn(tc, _world.LocationId))
        {
            return;
        }

        SpawnCompanionEntity(session.State.PlayerId, tc, session.State.Position);
        BroadcastCreatures();
    }

    /// <summary>The species snapshot of a story companion: this world's own animal of the authored record when it lives here
    /// (the Soundi on Soaniri), else the record built as on its home world, else a code-built stand-in.</summary>
    private CreatureSpecies StoryCompanionSpecies(string speciesId)
    {
        string key = speciesId.StartsWith(AuthoredSpeciesPrefix, StringComparison.Ordinal) ? speciesId.Substring(AuthoredSpeciesPrefix.Length) : speciesId;
        string id = AuthoredSpeciesPrefix + key;
        if (_speciesById.TryGetValue(id, out var live))
        {
            return SnapshotSpecies(live);
        }

        if (_content.AuthoredCreatures.TryGetValue(key, out var record) && AuthoredSpeciesOffWorld(record) is { } built)
        {
            return built;
        }

        return StandInStorySpecies(id, key);
    }

    /// <summary>An authored record's species built away from its home world: the roster generator on a stand-in of the
    /// type that hosts it (only the authored record, no rolled species), wearing that type's look.</summary>
    private CreatureSpecies? AuthoredSpeciesOffWorld(AuthoredCreature record)
    {
        var host = _content.Planets.Values.FirstOrDefault(pt => pt.AuthoredCreatures.Contains(record.Key));
        var planet = new PlanetType
        {
            Key = host?.Key ?? SoundersIds.PlanetType,
            Atmosphere = host is { IsAirless: false } ? host.Atmosphere : "breathable",
            CreatureAbundance = "authored",
        };
        int generation = Math.Max(WorldDescription.CurrentTerrainGeneration, record.MinGeneration);
        var built = CreatureGenerator.GenerateRoster(planet, _meta.Seed, generation, new[] { record })
            .FirstOrDefault(s => s.Id == AuthoredSpeciesPrefix + record.Key);
        return built is null
            ? null
            : WithFaunaLook(CloneSpecies(built), (planet.Key, FaunaLooks.WorldLookSeed(_meta.Seed, "story · " + record.Key)));
    }

    /// <summary>A stand-in species for a story companion whose record is missing (an older data set, a test): the Soundi's
    /// look as the animated series has it — light blue fur with mint patches, no eyes, a bushy tail.</summary>
    private static CreatureSpecies StandInStorySpecies(string id, string key) => new()
    {
        Id = id,
        NameKey = "creature.generic.name",
        Name = key == SoundersIds.Soundi ? "Soundi" : key,
        Habitat = CreatureHabitat.Land,
        Activity = CreatureActivity.Cathemeral,
        Temperament = CreatureTemperament.Passive,
        LocoStyle = LocomotionStyle.Strider,
        Size = 0.9f,
        MaxHealth = 30f,
        Speed = 4.5f, // keeps up with its friend
        Legs = 4,
        HasTail = true,
        BodySegments = 1,
        ColorRgb = 0x9FD8F5, // light blue
        BellyRgb = 0xA8F0CF, // mint
        Eyes = 0,            // the Soundi has no eyes — it hears the world
        Hide = "fur",
        BodyPlan = Enum.TryParse<CreatureBodyPlan>("Soundi", out var plan) ? plan : CreatureBodyPlan.Standard,
        VoiceSeed = 0x3361,
    };

    // ---------------- Songs ----------------

    /// <summary>World tick: the duels and the pack defence (every tick), and the story companions' songs (1 Hz).</summary>
    private void TickStoryCompanions()
    {
        TickCreatureFights();
        string world = _world.LocationId;
        if (_storySongNextAt.TryGetValue(world, out double next) && _uptime < next)
        {
            return;
        }

        _storySongNextAt[world] = _uptime + 1.0;
        foreach (var c in _creatures)
        {
            if (c.IsCompanion && c.CompanionKind == TamedCreature.SoundiKind)
            {
                SingNowAndThen(c, SoundiCallPrefix, SoundiCallCount);
            }
        }
    }

    /// <summary>One companion's song clock: armed when first seen, then a tune every 45–105 s (picked per companion and
    /// song, so two Soundis never sing in lock-step).</summary>
    private void SingNowAndThen(CombatEntity c, string prefix, int count)
    {
        if (c.NextSongAt <= 0)
        {
            c.NextSongAt = _uptime + StorySongFirstSeconds + StableHashing.Index(Hash(c.Id, "song-first"), 20);
            return;
        }

        if (_uptime < c.NextSongAt)
        {
            return;
        }

        int tune = 1 + StableHashing.Index(Hash(c.Id, "song:" + c.SongsSung), count);
        c.SongsSung++;
        c.NextSongAt = _uptime + StorySongMinSeconds + StableHashing.Index(Hash(c.Id, "song-gap:" + c.SongsSung), (int)StorySongSpreadSeconds);
        BroadcastToWorld(new SoundFx
        {
            SoundId = prefix + tune.ToString("00", System.Globalization.CultureInfo.InvariantCulture),
            X = c.Position.X,
            Y = c.Position.Y + 0.6f,
            Z = c.Position.Z,
        });
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: grants a story companion as the lead's story code would.</summary>
    public TamedCreature? GrantStoryCompanionForTest(string playerId, string kind, string speciesId, string suggestedName)
        => FindSessionByPlayerId(playerId) is { } s ? GrantStoryCompanion(s, kind, speciesId, suggestedName) : null;

    /// <summary>Test seam: runs the story-companion tick now (fights every call, songs past their 1 Hz gate).</summary>
    public void TickStoryCompanionsForTest()
    {
        _storySongNextAt.Remove(_world.LocationId);
        TickStoryCompanions();
    }
}
