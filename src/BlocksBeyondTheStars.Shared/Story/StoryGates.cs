// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlocksBeyondTheStars.Shared.Story;

/// <summary>
/// A tutorial pack's <c>gates</c> (#2469): gate name (<see cref="StoryGateNames"/>) → the condition that opens it — and,
/// since #2549, the one-time VEGA hints the basic training holds back until it reaches their topic
/// (<see cref="HoldHints"/>, JSON <c>gates.holdHints</c>). The dictionary itself holds only the named gates, so a reader
/// that walks the gates never meets the hold list.
/// </summary>
[JsonConverter(typeof(StoryGatesJsonConverter))]
public sealed class StoryGates : Dictionary<string, string>
{
    /// <summary>The JSON field of <see cref="HoldHints"/> inside <c>gates</c>.</summary>
    public const string HoldHintsField = "holdHints";

    /// <summary>Creates an empty gate set (ordinal keys, like every other id map of a pack).</summary>
    public StoryGates()
        : base(StringComparer.Ordinal)
    {
    }

    /// <summary>#2549: hint id (the <c>&lt;id&gt;</c> of <c>vega:hint:&lt;id&gt;</c> — a once-hint or a context tip) → the
    /// condition that releases it. While the condition fails for a player whose basic training runs, VEGA keeps that hint
    /// back <b>without spending it</b>, so it comes at its next occasion after the condition holds. Never held when the
    /// tutorial is off for the world or the player, done or skipped, or for an older client. Empty = nothing held.</summary>
    public Dictionary<string, string> HoldHints { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Reads and writes <see cref="StoryGates"/>: every string member is a gate, the object member
/// <c>holdHints</c> (any casing, like the other pack fields) the hold list. Anything else is a format error.</summary>
public sealed class StoryGatesJsonConverter : JsonConverter<StoryGates>
{
    /// <inheritdoc/>
    public override StoryGates Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("gates: expected an object of gate name → condition");
        }

        var gates = new StoryGates();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return gates;
            }

            string name = reader.GetString() ?? string.Empty;
            reader.Read();
            if (string.Equals(name, StoryGates.HoldHintsField, StringComparison.OrdinalIgnoreCase))
            {
                gates.HoldHints = ReadConditionMap(ref reader, "gates." + StoryGates.HoldHintsField);
            }
            else if (reader.TokenType == JsonTokenType.String)
            {
                gates[name] = reader.GetString() ?? string.Empty;
            }
            else if (reader.TokenType != JsonTokenType.Null)
            {
                throw new JsonException($"gates.{name}: expected a condition string");
            }
        }

        throw new JsonException("gates: unexpected end of the object");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, StoryGates value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var gate in value)
        {
            writer.WriteString(gate.Key, gate.Value);
        }

        if (value.HoldHints is { Count: > 0 })
        {
            writer.WriteStartObject(StoryGates.HoldHintsField);
            foreach (var hold in value.HoldHints)
            {
                writer.WriteString(hold.Key, hold.Value);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static Dictionary<string, string> ReadConditionMap(ref Utf8JsonReader reader, string path)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (reader.TokenType == JsonTokenType.Null)
        {
            return map;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"{path}: expected an object of hint id → condition");
        }

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return map;
            }

            string id = reader.GetString() ?? string.Empty;
            reader.Read();
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"{path}.{id}: expected a condition string");
            }

            map[id] = reader.GetString() ?? string.Empty;
        }

        throw new JsonException($"{path}: unexpected end of the object");
    }
}
