// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Player presses E on a placed heal tank to make it their custom spawn point (client → server,
/// issue #461). The cell is the aimed heal-tank block; the server validates block + reach and stores
/// a body-qualified spawn that the death flow offers as a respawn option (issue #462).
/// </summary>
public sealed class SetSpawnPointIntent
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

/// <summary>
/// Death with a home spawn set (server → client, issue #462): the server has NOT relocated the player
/// yet — the death screen offers "wake up at your ship" vs "wake up at &lt;home&gt;" and answers with
/// <see cref="RespawnChoiceIntent"/>. Without a home spawn the classic instant respawn runs instead and
/// this message is never sent. The server falls back to the ship on a ~30 s timeout, so a stuck or
/// disconnected client can never wedge the death flow.
/// </summary>
public sealed class RespawnOptions
{
    /// <summary>Death reason line for the death screen.</summary>
    public string Reason { get; set; } = string.Empty;

    public bool SalvageCapsuleDropped { get; set; }

    /// <summary>Display label of the home spawn (base/station name; empty → client shows a generic word).</summary>
    public string CustomLabel { get; set; } = string.Empty;
}

/// <summary>The player's pick on the death screen (client → server): home spawn or the ship.</summary>
public sealed class RespawnChoiceIntent
{
    public bool UseCustomSpawn { get; set; }
}
