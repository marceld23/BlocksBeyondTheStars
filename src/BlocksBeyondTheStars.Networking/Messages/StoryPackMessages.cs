// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Server → client (#3350, tag 328): which hidden blueprints (a <c>hiddenUntil</c> in <c>data/blueprints.json</c>) this player
/// may see in the research tree now — their condition holds. Sent after the join and whenever the set changes. A blueprint
/// with a <c>hiddenUntil</c> that is neither listed here nor owned stays out of the tree; an older server sends nothing (and
/// has no hidden blueprints the client must show). No protocol bump — an older client drops the tag.
/// </summary>
public sealed class BlueprintVisibility
{
    /// <summary>The keys of the hidden blueprints this player may see now.</summary>
    public string[] Shown { get; set; } = System.Array.Empty<string>();
}

/// <summary>One readable text of a story pack on its Journal page (#3347: the Sounders' Song Archive) — unlocked for this player.
/// Part of <see cref="NetStoryPack.Texts"/>.</summary>
public sealed class NetStoryText
{
    /// <summary>The text's id within the pack.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Locale key of the row's title.</summary>
    public string TitleKey { get; set; } = string.Empty;

    /// <summary>Locale key of the text the reader opens.</summary>
    public string TextKey { get; set; } = string.Empty;
}
