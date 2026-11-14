// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client.Core;

/// <summary>
/// #3580: when the browser build releases decoded sound data, and which clips. A browser decodes every clip on its first
/// play and keeps the decoded buffer — outside the WebAssembly heap, so the heap telemetry never sees it, while the
/// operating system of a 3–4 GB tablet counts all of it. The sound router survives travel, so without a release the
/// decoded set grows with every place visited.
/// <para>The release runs once per arrival at another place (another planet, a station) — never for the own ship's
/// interior, which is entered and left all the time, and never for a respawn at the same place. It only takes long clips
/// (ambience beds, weather, the night layer, festival music: 93 clips holding about two thirds of the decodable total)
/// that nothing holds right now; short, frequent clips — steps, mining, UI, creature and NPC voices — stay decoded, so they
/// never get a late first play. A released clip decodes again on its next play: the path every first play takes anyway,
/// because no bundled clip preloads.</para>
/// </summary>
public static class AudioReleaseRules
{
    /// <summary>Clips at least this large once decoded (float samples × channels) are released.</summary>
    public const long MinDecodedBytes = 1024 * 1024;

    /// <summary>Seconds to wait after the new world is ready: the beds and loops of the new place have taken over by then,
    /// and an arrival cue has finished.</summary>
    public const float SettleSeconds = 10f;

    /// <summary>The planet type of a ship interior (the server's <c>ShipInteriorType</c>).</summary>
    public const string ShipInteriorType = "ship_interior";

    /// <summary>The place a world reset leads to, or null for one that never counts as a move (a ship interior).</summary>
    public static string? PlaceKey(string? planetType, string? planetName, string? systemName)
        => string.Equals(planetType, ShipInteriorType, StringComparison.Ordinal)
            ? null
            : (systemName ?? string.Empty) + "|" + (planetName ?? string.Empty) + "|" + (planetType ?? string.Empty);

    /// <summary>Whether arriving at <paramref name="next"/> after <paramref name="last"/> is a move to another place. The
    /// first arrival of a session counts (the join's own place is not known here — at worst one release finds nothing).</summary>
    public static bool IsMove(string? last, string? next)
        => next != null && !string.Equals(last, next, StringComparison.Ordinal);

    /// <summary>The size of a clip's decoded data: 32-bit float samples per channel.</summary>
    public static long DecodedBytes(int samples, int channels) => (long)Math.Max(0, samples) * Math.Max(1, channels) * 4;

    /// <summary>Whether a bundled clip is released: it is decoded, no audio source holds it, and it is at least
    /// <paramref name="minBytes"/> large once decoded.</summary>
    public static bool ShouldRelease(bool loaded, bool heldBySource, long decodedBytes, long minBytes = MinDecodedBytes)
        => loaded && !heldBySource && decodedBytes >= minBytes;
}
