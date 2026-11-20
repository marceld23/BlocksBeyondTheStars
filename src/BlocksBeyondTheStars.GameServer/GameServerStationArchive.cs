// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3870, the lively places: a research station takes samples (docs/developer/LIVELY_PLACES.md §8). A market row cannot
/// do it — a sample carries its species in its key and lies in the sample case, which no recipe reads — so the station
/// has an archive of its own. Aboard a research station a player hands ONE sample of a kind to it from the Samples page;
/// the archive pays <see cref="ArchiveFragments"/> by the species' rarity, once per species and player (samples are
/// collected endlessly; the archive wants every species once, which makes it a collection goal and nothing to farm).
/// <list type="bullet">
/// <item>Only aboard a research station (the purpose of the active station world), only a sample the case holds, only a
/// species the register knows.</item>
/// <item>One sample leaves the case; the rest stays, and the research book keeps what was learned.</item>
/// <item>The given species are remembered in the player's research record (<see cref="BioResearch.GivenToStations"/>).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>What the archive pays in.</summary>
    internal const string ArchiveReward = "data_fragment";

    /// <summary>The data fragments the archive pays for a species of <paramref name="rarity"/> (0 common .. 4 legendary).</summary>
    internal static int ArchiveFragments(int rarity) => 1 + System.Math.Clamp(rarity, 0, BioRules.MaxRarity);

    private void HandleGiveSampleToStation(PlayerSession session, GiveSampleToStationIntent intent)
    {
        string item = intent.Item ?? string.Empty;
        string baseKey = ItemKey.Base(item);
        if ((baseKey != BioItems.Sample && baseKey != BioItems.MineralSample) || ItemKey.Seed(item) == 0)
        {
            return; // not a sample key: the client offers the button only for what lies in the case
        }

        if (ActiveStationInterior() is not { } station || session.State.CurrentLocationId != _world.LocationId
            || StationPurposeOf(station.Id) != StationPurposes.Research)
        {
            Reject(session, "sample", "@srv.station.archive_not_here");
            return;
        }

        uint seed = ItemKey.Seed(item);
        var p = session.State;
        if (BioEntry(seed) is not { } entry || SampleKey(entry) != item || p.SampleCase.CountOf(item) <= 0)
        {
            return;
        }

        var research = ResearchOf(p.PlayerId);
        if (research.GivenToStations.Contains(seed))
        {
            Reject(session, "sample", "@srv.station.archive_known");
            return;
        }

        int fragments = ArchiveFragments(RarityOf(entry));
        if (!p.SampleCase.Remove(item, 1))
        {
            return;
        }

        research.GivenToStations.Add(seed);
        SaveResearch(p.PlayerId);
        var pool = new MaterialPool(_content, p, _ship);
        pool.Add(ArchiveReward, fragments);
        WarnIfPoolOverflowed(session, pool);
        SendInventory(session);
        Send(session, new ServerMessage { Text = "@srv.station.archive_thanks:" + fragments });
        _log.Info($"'{p.Name}' gave a sample of {item} to the archive of {station.Id} for {fragments} {ArchiveReward}.");
    }

    /// <summary>A species' rarity tier: a plant's or an animal's profile, a deposit's by its rarity points.</summary>
    private int RarityOf(BioSpeciesEntry entry)
        => entry.Kind != BioKind.Mineral && BioProfileOf(entry.Seed) is { } profile
            ? profile.Rarity
            : BioRules.RarityOfPoints(entry.Context?.RarityPoints ?? 0);

    /// <summary>Test seam: hands a sample to the archive as the intent would.</summary>
    internal void GiveSampleToStationForTest(PlayerSession session, string item)
        => HandleGiveSampleToStation(session, new GiveSampleToStationIntent { Item = item });

    /// <summary>Test seam: the species a player has given to station archives.</summary>
    internal int GivenToStationsForTest(string playerId) => ResearchOf(playerId).GivenToStations.Count;
}
