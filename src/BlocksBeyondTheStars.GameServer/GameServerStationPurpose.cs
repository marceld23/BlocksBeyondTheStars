// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The lively places, what a space station is for (docs/developer/LIVELY_PLACES.md): a kit station that is composed for the
/// first time in a world of terrain generation 46 or later gets a purpose — trade, mining or research
/// (<see cref="StationPurposes"/>) — and a colour of its own. Both are pinned with its composition
/// (<c>StationKitRecord.Purpose / Colour</c>), so the station is the same one on every visit; every station that exists
/// already, and every station of an older world, has neither and stays exactly as it is.
/// <list type="bullet">
/// <item><b>Which.</b> The station of the start system trades (a new player's first station sells what they expect); in a
/// system with an asteroid belt mining is the likeliest; elsewhere one of the three by weight, from the station's seed.</item>
/// <item><b>What it changes.</b> The kit it is built from (a kit may name the purposes it is made for), the goods of its
/// first vendor and the clothes of its crew (the purpose's trade theme), on a trade station the order of the further
/// vendors' trades (all four in turn), and the head line a player reads when boarding (<see cref="NetPlace.Purpose"/>).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A system counts as one "with a belt" from this many asteroid fields.</summary>
    private const int BeltAsteroidFields = 2;

    /// <summary>The trades of a trade station's vendors, in the order they are handed out: all four are aboard.</summary>
    private static readonly string[] TradeStationVendorOrder = { "traders", "miners", "researchers", "settlers" };

    /// <summary>The system a station floats in ("" when the galaxy does not know its body and its id names none).</summary>
    private string StationSystemIdOf(string stationId)
    {
        if (_galaxy?.FindBody(stationId)?.SystemId is { Length: > 0 } known)
        {
            return known;
        }

        const string Local = "-st-local"; // the fallback station of a system that has none of its own
        return stationId.EndsWith(Local, System.StringComparison.Ordinal) ? stationId.Substring(0, stationId.Length - Local.Length) : string.Empty;
    }

    /// <summary>The purpose a station gets when it is composed for the first time: "" in a world before the lively places.</summary>
    private string NewStationPurpose(string stationId, long stationSeed)
    {
        if (!LivelyWorld)
        {
            return StationPurposes.None;
        }

        string systemId = StationSystemIdOf(stationId);
        int fields = _galaxy?.Systems.FirstOrDefault(s => s.Id == systemId)?.Bodies.Count(b => b.Kind == CelestialKind.AsteroidField) ?? 0;
        return StationPurposes.For(stationSeed, home: IsStartSystem(systemId), belt: fields >= BeltAsteroidFields);
    }

    /// <summary>The kits a station of a purpose draws from: the ones made for it when there are any, else the ones made
    /// for no purpose in particular (every kit from before the lively places) — never a kit made for another purpose.</summary>
    private static IReadOnlyList<StructureKit> KitsOfPurpose(IReadOnlyList<StructureKit> kits, string purpose)
    {
        var own = purpose.Length == 0 ? new List<StructureKit>() : kits.Where(k => k.Purposes.Contains(purpose)).ToList();
        return own.Count > 0 ? own : kits.Where(k => k.Purposes.Count == 0).ToList();
    }

    /// <summary>The fallback for a purpose's own kit that could not be assembled from a station's seed: the tier's kits made
    /// for no purpose, in their order, with the purpose and the colour all the same — the station keeps what it is for (its
    /// band, its crew, its first vendor, its board), in the rooms every station has. Null when none of them fits either.</summary>
    private StationStructure? ComposeWithoutPurposeRooms(BoardableStation station, long stationSeed, string purpose, out StationComposition? composition)
    {
        composition = null;
        var kits = _content.KitsFor(StructureKit.KindStation, station.SizeTier, _meta.Description.EnabledStructurePacks, null, _meta.Description.TerrainGeneration);
        foreach (var kit in KitsOfPurpose(kits, StationPurposes.None))
        {
            var built = StationKitComposer.Compose(kit, key => _content.TemplateByKey(StructureKit.KindStation, key), stationSeed, _content,
                out var composed, out _, purpose, StationPurposes.ColourFor(stationSeed));
            if (built is not null)
            {
                composition = composed;
                return built;
            }
        }

        return null;
    }

    /// <summary>The pinned purpose of a station ("" = none: a station from before, a template or a procedural one).</summary>
    private string StationPurposeOf(string stationId)
        => _meta.StationKits.TryGetValue(stationId, out var rec) && StationPurposes.IsKnown(rec.Purpose) ? rec.Purpose : StationPurposes.None;

    /// <summary>What a station's crew wears and its first vendor sells: the purpose's trade, "traders" without one.</summary>
    private string StationCrewTheme(string stationId) => StationPurposes.ThemeOf(StationPurposeOf(stationId));

    /// <summary>The trade of a kit station's <paramref name="vendorIndex"/>-th vendor: with a purpose the first one sells
    /// the purpose's goods and a trade station hands out all four trades in turn; without one, what it always was.</summary>
    private string KitStationVendorTheme(string stationId, int vendorIndex)
    {
        string purpose = StationPurposeOf(stationId);
        if (purpose == StationPurposes.Trade)
        {
            return TradeStationVendorOrder[vendorIndex % TradeStationVendorOrder.Length];
        }

        // A mining or a research station's first trader runs the station's own exchange: the trade's goods at better rates.
        return purpose.Length > 0 && vendorIndex == 0 ? StationPurposes.ServiceThemeOf(purpose) : StationVendorTheme(stationId, vendorIndex);
    }

    /// <summary>The boarded station as the one place of its world: its name, its purpose and its colour — the client
    /// greets the player with it and plays the deck's sound layer. Its box is the whole station.</summary>
    private NetPlace? StationPlaceOfActiveWorld()
    {
        if (ActiveStationInterior() is not { Structure: { } structure } station || IsGeneratedDerelictId(station.Id))
        {
            return null;
        }

        _meta.StationKits.TryGetValue(station.Id, out var rec);
        return new NetPlace
        {
            Key = "station:" + station.Id,
            Kind = PlaceKinds.Station,
            Name = station.Name,
            Tier = station.SizeTier,
            MinX = station.Origin.X,
            MinZ = station.Origin.Z,
            MaxX = station.Origin.X + structure.Width - 1,
            MaxZ = station.Origin.Z + structure.Length - 1,
            Y = station.Origin.Y + (structure.Height / 2),
            Color = rec?.Colour ?? 0,
            PlazaX = (int)System.Math.Floor(station.Spawn.X),
            PlazaY = (int)System.Math.Floor(station.Spawn.Y),
            PlazaZ = (int)System.Math.Floor(station.Spawn.Z),
            Purpose = StationPurposeOf(station.Id),
        };
    }

    /// <summary>How many delivery jobs and how many survey jobs a station's mission board shows at once: a mining station two
    /// more deliveries, a research station three surveys instead of one. The jobs come from the same streams as ever — a
    /// wider window only shows more of them at a time, so no job's id or roll moves.</summary>
    private (int Deliveries, int Surveys) StationBoardWindows(string stationId) => StationPurposeOf(stationId) switch
    {
        StationPurposes.Mining => (BoardWindow + 2, 1),
        StationPurposes.Research => (BoardWindow, 3),
        _ => (BoardWindow, 1),
    };

    /// <summary>Test seam: pins another purpose on a composed kit station (what a station in another system would have).</summary>
    internal void SetStationPurposeForTest(string stationId, string purpose)
    {
        if (_meta.StationKits.TryGetValue(stationId, out var rec))
        {
            rec.Purpose = purpose;
            _repo.SaveMetadata(_meta); // pinned, as a composed purpose is: the next server run reads it
        }
    }

    /// <summary>Test seam: sends this player the star map, as its request would.</summary>
    internal void SendStarMapForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            SendStarMap(session);
        }
    }

    /// <summary>Test seam: a station's pinned purpose and colour.</summary>
    internal (string Purpose, int Colour) StationPurposeForTest(string stationId)
        => _meta.StationKits.TryGetValue(stationId, out var rec) ? (rec.Purpose, rec.Colour) : (string.Empty, 0);
}
