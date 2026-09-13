// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Player notes (#1844): titled free-text pages a player keeps for themselves under the Story tab. Private —
/// the server only ever sends a player their OWN notes. Persisted on the player blob, capped at 20.
/// </summary>
public sealed class NetNote
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Title ≤ 40 chars (screened server-side; a refused title refuses the whole save).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Body ≤ 2000 chars, newlines kept, profanity masked like chat. May carry §-markup.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Unix ms the note was created — the list is ordered newest-first by this.</summary>
    public long CreatedUtc { get; set; }

    /// <summary>Unix ms of the last save.</summary>
    public long UpdatedUtc { get; set; }
}

/// <summary>The player's full note set (server → client): sent on join and after every set/remove. Replaces
/// the previous set wholesale.</summary>
public sealed class NoteList
{
    public NetNote[] Notes { get; set; } = System.Array.Empty<NetNote>();
}

/// <summary>
/// Every note verb in one envelope (client → server), one NetCodec tag. <see cref="Kind"/> picks the verb:
/// <c>set</c> (create with empty <see cref="Id"/>, or update an own note by id — title + body),
/// <c>remove</c> (own note by id).
/// </summary>
public sealed class NoteActionIntent
{
    public string Kind { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;
}
