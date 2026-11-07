// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlocksBeyondTheStars.Shared.Story;

// What a SIDE pack brings besides its chapters (#3347, #3357, the Sounders story pack): readable texts for its Journal page,
// star systems that appear per player, and the milestone namespace its dialogs may write. The world story's lore sites,
// dialogs and characters (StoryDefinition) count for an active side pack too — see docs/developer/STORY_PACK_AUTHORING.md §14.

/// <summary>
/// A readable text of a pack (#3347): shown on the pack's Journal page (under <see cref="StoryDefinition.TextsTitleKey"/>,
/// the Sounders' "Song Archive") once <see cref="UnlockedBy"/> holds for the player and the pack is active for them.
/// </summary>
public sealed class StoryPackText
{
    /// <summary>Id, unique within the pack (key-safe).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Locale key of the row's title.</summary>
    public string TitleKey { get; set; } = string.Empty;

    /// <summary>Locale key of the text the reader opens.</summary>
    public string TextKey { get; set; } = string.Empty;

    /// <summary>The condition (story grammar) that unlocks it — typically <c>milestone:&lt;key&gt;</c>. Empty = shown as soon
    /// as the pack is active.</summary>
    public string UnlockedBy { get; set; } = string.Empty;
}

/// <summary>
/// A star system a pack adds to the galaxy (#3357; the Sounders' Ornemis): appended the first time any player satisfies
/// <see cref="VisibleIf"/> (an old save gets it too) and remembered by the save, so it is re-appended at every start. Per
/// player it is on the star map and a hyperjump target only while <see cref="VisibleIf"/> holds for them (or they are or
/// were in it — a crewmate who flew along). It holds one landable body; normal jump rules apply.
/// </summary>
public sealed class StoryPackSystem
{
    /// <summary>The system id — unique in the galaxy, never <c>sys…</c> (the procedural ids) and never the Guardian's.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Locale key of the system's name (each player's star map shows it in their language); empty = <see cref="Name"/>.</summary>
    public string NameKey { get; set; } = string.Empty;

    /// <summary>A fixed system name (a proper name, the same in every language); used when there is no <see cref="NameKey"/>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The planet type of the system's one landable body (<c>data/planets.json</c>); an unknown type falls back to a
    /// rocky world.</summary>
    public string PlanetType { get; set; } = string.Empty;

    /// <summary>The body's proper name (empty = the system's name).</summary>
    public string BodyName { get; set; } = string.Empty;

    /// <summary>Where the system goes on the star map (<see cref="StoryPackSystemPlacements"/>, default <c>edge</c>).</summary>
    public string Placement { get; set; } = StoryPackSystemPlacements.Edge;

    /// <summary>Conditions (story grammar; a string or a list = all of them) under which a player may see and target it.
    /// Empty = everyone for whom the pack is active.</summary>
    [JsonConverter(typeof(StoryConditionListConverter))]
    public List<string> VisibleIf { get; set; } = new();

    /// <summary>The body id of the system's one body (<c>&lt;id&gt;-world</c>).</summary>
    public static string BodyIdOf(string systemId) => systemId + "-world";
}

/// <summary>The values of <see cref="StoryPackSystem.Placement"/> (#3357).</summary>
public static class StoryPackSystemPlacements
{
    /// <summary>Out past every other star, on a bearing of its own (seed-stable) — "beyond the frontier", like the Guardian's.</summary>
    public const string Edge = "edge";

    /// <summary>Inside the galaxy, at the emptiest spot among the stars (seed-stable) — "hidden among them, detuned".</summary>
    public const string Inside = "inside";

    /// <summary>Every placement.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Edge, Inside };
}

/// <summary>The milestones a pack's dialog may set with a <c>milestone:&lt;key&gt;</c> consequence (#3347): only keys in the
/// pack's own namespace — <c>&lt;pack id&gt;:…</c> or <c>&lt;milestonePrefix&gt;:…</c> — never a family the game writes itself.</summary>
public static class StoryPackMilestones
{
    /// <summary>The dialog consequence that sets a milestone (<c>milestone:snd:echo:kamo</c>).</summary>
    public const string Consequence = "milestone";

    /// <summary>Milestone families the game writes; a pack's prefix may not be one of them.</summary>
    public static readonly IReadOnlyList<string> ReservedPrefixes = new[]
    {
        "story", "lore", "dialog", "dialogflag", "vega", "char", "npcthread", "festival_gift", "festival_gift_due", "tutorial",
        "insight", "mem", "obj", "track", "cut", "none",
    };

    /// <summary>Whether <paramref name="prefix"/> (without the colon) may be a pack's own milestone namespace.</summary>
    public static bool IsValidPrefix(string? prefix)
    {
        if (string.IsNullOrEmpty(prefix) || prefix!.IndexOf(':') >= 0 || !StoryPackValidator.IsKeySafeId(prefix))
        {
            return false;
        }

        foreach (string reserved in ReservedPrefixes)
        {
            if (string.Equals(reserved, prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a dialog of <paramref name="pack"/> may set the milestone <paramref name="key"/>: a key under
    /// <c>&lt;pack id&gt;:</c> or under the pack's own <see cref="StoryDefinition.MilestonePrefix"/>, with something after the
    /// colon. A dialog of no pack (<c>data/dialogs.json</c>) may set none.</summary>
    public static bool MaySet(StoryDefinition? pack, string? key)
    {
        if (pack is null || string.IsNullOrEmpty(key) || key!.EndsWith(":", StringComparison.Ordinal))
        {
            return false;
        }

        if (IsValidPrefix(pack.Id) && key.StartsWith(pack.Id + ":", StringComparison.Ordinal) && key.Length > pack.Id.Length + 1)
        {
            return true;
        }

        string own = pack.MilestonePrefix ?? string.Empty;
        return IsValidPrefix(own) && key.StartsWith(own + ":", StringComparison.Ordinal) && key.Length > own.Length + 1;
    }
}

/// <summary>Reads a condition field that may be written as one string (<c>"milestone:snd:song_learned"</c>) or as a list of
/// strings (all of them must hold) into a list (#3347: a choice's <c>visibleIf</c>, a system's <c>visibleIf</c>).</summary>
public sealed class StoryConditionListConverter : JsonConverter<List<string>>
{
    /// <inheritdoc/>
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var list = new List<string>();
        if (reader.TokenType == JsonTokenType.Null)
        {
            return list;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            string? one = reader.GetString();
            if (!string.IsNullOrWhiteSpace(one))
            {
                list.Add(one!);
            }

            return list;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("expected a condition string or a list of condition strings");
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("expected a condition string");
            }

            list.Add(reader.GetString() ?? string.Empty);
        }

        return list;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        writer.WriteStartArray();
        foreach (string s in value ?? new List<string>())
        {
            writer.WriteStringValue(s);
        }

        writer.WriteEndArray();
    }
}
