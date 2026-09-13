// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.State;

/// <summary>
/// A titled free-text note a player wrote for themselves (#1844): a shopping list, a base plan, a diary page —
/// shown under the Story tab's "Notes" category. At most 20 per player, persisted on the owner's player blob
/// (additive, like <see cref="PlayerMarker"/>), never shown to anyone else. The body may carry the tiny
/// §-markup (colour / bold / reset) that the client renders in its preview; the server stores it as plain text.
/// </summary>
public sealed class PlayerNote
{
    /// <summary>Stable id (server-issued), used to update / delete the note.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Player-typed title, ≤ 40 chars, control-char-stripped and content-screened like a base name.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Player-typed body, ≤ 2000 chars. Newlines are kept; every other control character is dropped.
    /// Profanity is masked (like chat), never refused.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Unix ms the note was created (newest-first ordering in the list).</summary>
    public long CreatedUtc { get; set; }

    /// <summary>Unix ms of the last save.</summary>
    public long UpdatedUtc { get; set; }
}
