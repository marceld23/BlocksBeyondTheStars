// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The two hooks the other-side core (<c>GameServerOtherSide*.cs</c>) calls into the sound layer, as defining
/// declarations — the sound layer implements them (GameServerSoundLayer.Chunks.cs). C# allows ONE defining declaration
/// per partial method, and this file is their home (the core only calls them). The third hook runs the other way:
/// <c>SoundBlockCountChanged</c> is declared in GameServerSoundLayer.cs and implemented by the core.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Called right after <see cref="PlayerSession.OtherSide"/> flipped for a joined player: the sound layer
    /// re-sends the chunks of the player's sent set that carry sound cells, in the variant of the new side.</summary>
    partial void SoundLayerSideChanged(PlayerSession s);

    /// <summary>Fills how many sound blocks the player has standing in their world and the cap, for the state message.</summary>
    partial void FillSoundBlockCount(PlayerSession s, ref int used, ref int cap);
}
