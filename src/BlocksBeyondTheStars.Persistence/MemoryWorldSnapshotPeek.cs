// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO.Compression;
using System.Text.Json;

namespace BlocksBeyondTheStars.Persistence;

/// <summary>
/// Reads ONE fact out of a browser-singleplayer save blob without deserializing the world: who the
/// player in it is. The browser client asks this before it knows a name — a <c>?singleplayer=1</c>
/// deep-link without one, or a second device restoring the Glitch cloud copy while the settings (and
/// the name) did not travel (#1322). In this game the name IS the player id (<c>LoadPlayer(name)</c>,
/// every owner field), so a world with exactly one player names its owner, and adopting that name is
/// the only way back into the same inventory, ship and base. No player, or more than one (a blob that
/// was once a shared world), gives no answer — the caller asks the player instead.
/// </summary>
public static class MemoryWorldSnapshotPeek
{
    /// <summary>The sole player id stored in <paramref name="blob"/> (a
    /// <see cref="MemoryWorldRepository.ExportSnapshotBlob"/> payload), or null when the blob is missing,
    /// unreadable, holds no player or more than one. Never throws — a damaged blob is the boot's problem,
    /// not the name lookup's.</summary>
    public static string? SolePlayerId(byte[]? blob)
    {
        if (blob == null || blob.Length == 0)
        {
            return null;
        }

        try
        {
            using var input = new MemoryStream(blob);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var plain = new MemoryStream();
            gzip.CopyTo(plain);
            return SolePlayerIdFromJson(plain.ToArray());
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Streams the snapshot document and stops at the top-level <c>Players</c> object: its keys
    /// are the player ids. Everything else (block edits can be megabytes) is skipped token-wise, never
    /// materialized.</summary>
    private static string? SolePlayerIdFromJson(byte[] json)
    {
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { AllowTrailingCommas = true });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return null;
        }

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return null; // the top-level object closed without a Players table
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            bool isPlayers = reader.ValueTextEquals("Players");
            if (!reader.Read())
            {
                return null;
            }

            if (!isPlayers)
            {
                reader.Skip(); // a whole nested table in one hop; a no-op on scalar values
                continue;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            string? sole = null;
            int count = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                if (++count > 1)
                {
                    return null; // two players: nobody's name is "the" name
                }

                sole = reader.GetString();
                if (reader.Read())
                {
                    reader.Skip(); // the player's own snapshot row
                }
            }

            return count == 1 && !string.IsNullOrWhiteSpace(sole) ? sole : null;
        }

        return null;
    }
}
