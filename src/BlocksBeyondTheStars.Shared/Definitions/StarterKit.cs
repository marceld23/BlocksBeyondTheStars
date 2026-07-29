// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The gear every fresh pilot is handed on their first join, in quick-bar slot order (the server stocks it in
/// <c>GameServer.CreatePlayer</c>).
///
/// It lives in Shared rather than in the server because both sides need the same answer: the server refuses a
/// discard of these items (#599) and the client hides the Discard button for them. Two hand-maintained lists
/// would drift, and the drifting half would be the client's — which is the half the player sees.
///
/// Deliberately NOT included: the starter berries and the emergency rations. Those are food you re-gather, and
/// a toxic batch is exactly the kind of thing a player wants to bin. Only the equipment is protected, because
/// a player who threw away their drill or their lamp would have no way to craft a replacement.
/// </summary>
public static class StarterKit
{
    /// <summary>The protected starter equipment, in the slot order <c>CreatePlayer</c> stocks it.</summary>
    public static readonly string[] Items =
    {
        "basic_drill", "hand_scanner", "suit_lamp", "machete", "scrap_pistol",
    };

    /// <summary>True for an item the player may never throw away (#599). Compares the base key, so a dyed or
    /// shaped variant of one — should any ever exist — is covered too.</summary>
    public static bool IsProtected(string item)
        => System.Array.IndexOf(Items, ItemKey.Base(item)) >= 0;
}
