// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Missions;

/// <summary>
/// #3180: one authored story behind a delivery job of a mission board, from <c>data/board_stories.json</c> — who needs
/// the goods and why, and what came of it once they were handed in (Justus, school club: "more story with the jobs — why
/// the deliveries, and who wants them"). The board's roll stays exactly as it was (item, amount, reward); the server
/// picks one of the stories of the job's item (<see cref="Need"/>) and <see cref="Place"/> by a stable hash of the job's
/// id, so a job keeps its story across reloads. The texts live in the locale tables (English and German mandatory):
/// <see cref="TitleKey"/>, <see cref="DescriptionKey"/> and <see cref="ThanksKey"/>; <see cref="GiverToken"/> in them is
/// the board's giver (the quartermaster). Optional content: no file → the plain, place-correct board texts.
/// </summary>
public sealed class BoardStory
{
    /// <summary>A settlement's mission board (job ids <c>settle_…</c>).</summary>
    public const string PlaceSettlement = "settlement";

    /// <summary>A space station's mission board (job ids <c>station_…</c>).</summary>
    public const string PlaceStation = "station";

    /// <summary>The mission board in the player's own base, run by a resident quartermaster (job ids <c>home_…</c>).</summary>
    public const string PlaceBase = "base";

    /// <summary>The mission board in the player's own ship — cargo orders radioed in (job ids <c>shipboard_…</c>). Nobody
    /// stands at it, so a ship story never names a giver.</summary>
    public const string PlaceShip = "ship";

    /// <summary>Every place a story may name.</summary>
    public static readonly string[] Places = { PlaceSettlement, PlaceStation, PlaceBase, PlaceShip };

    /// <summary>The placeholder the server fills with the board giver's name.</summary>
    public const string GiverToken = "{giver}";

    /// <summary>The prefix of every story locale key (<c>mission.board.&lt;place&gt;.&lt;key&gt;.title</c> …).</summary>
    public const string KeyPrefix = "mission.board.";

    /// <summary>The item the delivery asks for (one of the board's delivery templates).</summary>
    public string Need { get; set; } = string.Empty;

    /// <summary>Which board tells it — one of <see cref="Places"/>.</summary>
    public string Place { get; set; } = string.Empty;

    /// <summary>The story's key, unique per place (lower case, digits and underscores).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>A few English words on who needs the goods and why — the AI wording's brief (the backend tells the same
    /// story in its own words). Never shown to the player.</summary>
    public string Brief { get; set; } = string.Empty;

    /// <summary>The locale key of the job's title.</summary>
    public string TitleKey => KeyPrefix + Place + "." + Key + ".title";

    /// <summary>The locale key of the job's description: who needs it and why.</summary>
    public string DescriptionKey => KeyPrefix + Place + "." + Key + ".desc";

    /// <summary>The locale key of the thank-you on hand-in: what came of the delivery.</summary>
    public string ThanksKey => KeyPrefix + Place + "." + Key + ".thanks";

    /// <summary>Whether a mission's locale key is a board story's (<see cref="KeyPrefix"/>).</summary>
    public static bool IsStoryKey(string? key) => key is not null && key.StartsWith(KeyPrefix, System.StringComparison.Ordinal);
}
