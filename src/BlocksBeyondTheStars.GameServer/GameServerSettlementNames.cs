// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The lively places, the names (terrain generation 46): the classic name table coins about fifty names per size
/// (<see cref="SettlementDisplayName"/>: ten roots, four or five endings), so a galaxy is full of "Karth Village". A world
/// of the lively places draws from forty roots, and the ending tells something about the place — its landmark first ("Mira
/// Mill", "Tarn Light"), else now and then the form it grew in ("Dorn Green", "Olun Crossing"), else its size. A name is a
/// coined English proper name like the classic ones (no locale key), drawn from the instance's own name lane and pinned in
/// the placement record: an older world keeps coining and replaying its classic names.
/// </summary>
public sealed partial class GameServer
{
    private static readonly string[] LivelyNameRoots =
    {
        "Karth", "Vega", "Mira", "Dorn", "Ysel", "Tarn", "Olun", "Reth", "Sabik", "Cael",
        "Alba", "Bren", "Corin", "Dessa", "Elun", "Faro", "Galen", "Harl", "Isen", "Jora",
        "Kessa", "Lorn", "Mavi", "Nerin", "Orla", "Pell", "Quill", "Rona", "Selk", "Ulda",
        "Varn", "Wren", "Xira", "Yarrow", "Zell", "Arden", "Bryn", "Calo", "Eska", "Fenn",
    };

    /// <summary>Whether this world is one of the lively places (terrain generation 46 or later): its fresh settlements are
    /// planned with plan revision 2, a hand-built village stands at a settlement's heart, its names come from the larger
    /// table, its stations get a purpose and a colour.</summary>
    private bool LivelyWorld => _meta.Description.TerrainGeneration >= WorldDescription.LivelyPlacesGeneration;

    /// <summary>Whether this world coins its settlement names from the larger table.</summary>
    private bool HasLivelyNames => LivelyWorld;

    /// <summary>The ending a landmark gives a place's name ("" = a landmark without one, or no landmark).</summary>
    internal static string LandmarkNameEnding(string landmark) => landmark switch
    {
        "landmark_mill" => " Mill",
        "landmark_bell" => " Bell",
        "landmark_water_tower" => " Tower",
        "landmark_jetty" => " Landing",
        "landmark_light" => " Light",
        "landmark_hall" => " Market",
        "landmark_obelisk" => " Spire",
        "landmark_clock" => " Clock",
        "landmark_dome" => " Dome",
        _ => string.Empty,
    };

    /// <summary>The endings a village form gives ("" form = none).</summary>
    private static string[] FormNameEndings(string form) => form switch
    {
        "street" => new[] { " Row", " Lane" },
        "green" => new[] { " Green", " Meadow" },
        "round" => new[] { " Ring", " Round" },
        "cluster" => new[] { " Corner", " Nook" },
        "cross" => new[] { " Crossing", " Fourways" },
        "bank" => new[] { " Bank", " Ford", " Lakeside" },
        _ => Array.Empty<string>(),
    };

    private static string[] TierNameEndings(string tier) => tier switch
    {
        "city" => new[] { " City", " Metropolis", " Prime", " Central", " Gate" },
        "town" => new[] { " Town", " Colony", " Outpost", " Heights", " Haven" },
        "hamlet" => new[] { " Hamlet", " Cross", " Camp", " Rest", " Stead" },
        _ => new[] { " Village", " Hollow", " Glen", " Stead", " End", " Dale" },
    };

    /// <summary>
    /// A lively place's name: a root and an ending — the landmark's when the place has one, in two of five cases the
    /// form's, else the size's. The draws come from <paramref name="rng"/> in a fixed order (root, form coin, ending).
    /// </summary>
    internal static string LivelySettlementName(string tier, bool ruined, Random rng, string form = "", string landmark = "")
    {
        string root = LivelyNameRoots[rng.Next(LivelyNameRoots.Length)];
        bool byForm = rng.Next(5) < 2;
        var formEndings = FormNameEndings(form);
        var tierEndings = TierNameEndings(tier);
        int pick = rng.Next(60); // one draw whatever the table: 60 divides by 2, 3, 5 and 6
        string landmarkEnding = LandmarkNameEnding(landmark);
        string ending = landmarkEnding.Length > 0 ? landmarkEnding
            : byForm && formEndings.Length > 0 ? formEndings[pick % formEndings.Length]
            : tierEndings[pick % tierEndings.Length];
        return ruined ? $"Ruins of {root}{ending}" : root + ending;
    }

    /// <summary>The coined name of a fresh settlement on this world: the lively table from terrain generation 46, the
    /// classic one before.</summary>
    private string CoinSettlementName(string tier, bool ruined, Random rng, string form = "", string landmark = "")
        => HasLivelyNames ? LivelySettlementName(tier, ruined, rng, form, landmark) : SettlementDisplayName(tier, ruined, rng);
}
