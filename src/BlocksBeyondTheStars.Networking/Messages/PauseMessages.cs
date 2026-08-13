// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// "Hold the world while I'm in the menu." Asked for by a player: the Esc dialog is titled <i>Pause</i> and
/// offers <i>Resume</i>, but nothing ever stopped — hunger drained, creatures kept hunting and night kept
/// falling while he was reading the menu.
/// <para>
/// It has to be a server intent rather than a client-side <c>Time.timeScale = 0</c>, because singleplayer runs
/// the bundled server as a SEPARATE PROCESS: freezing the client would stop the camera while the world carried
/// on simulating, which is worse than not pausing at all.
/// </para>
/// The world only actually holds once EVERY joined player has asked for it (#973), so nobody can freeze a world
/// out from under anyone else on a dedicated or hosted server.
/// <para>
/// While the menu stays open the client REPEATS this intent every few seconds. Behind an open menu it sends
/// nothing else at all, so the repeat is the server's only proof that the client is still alive — without it a
/// player whose game crashes mid-pause would hold the world frozen for everyone else.
/// </para>
/// </summary>
public sealed class PauseIntent
{
    public bool Paused { get; set; }
}

/// <summary>
/// Whether the world is actually holding, so the client can show it (and stop pretending the menu paused
/// something when it did not — e.g. someone else is still playing). Broadcast to every client, because each
/// one stops its own world clock from this message.
/// </summary>
public sealed class PauseState
{
    public bool Paused { get; set; }

    /// <summary>Whether the request was recorded at all. Always true since #973 (the intent is always kept;
    /// only the WORLD can answer "not yet"), and kept so a pre-#973 client still reads a sane value.</summary>
    public bool Allowed { get; set; }

    // Appended fields (#973). MessagePack is contractless here, so adding them is not a protocol bump: an
    // older client simply ignores what it does not know and still pauses correctly on `Paused` alone.

    /// <summary>How many joined players are currently asking for the hold.</summary>
    public int HoldingPlayers { get; set; }

    /// <summary>How many players are joined at all (spectators excluded, as everywhere). Together with
    /// <see cref="HoldingPlayers"/> this is the "2 of 3 ready" the pause dialog shows.</summary>
    public int JoinedPlayers { get; set; }

    /// <summary>The players who are NOT holding, comma-separated — what the pause dialog is waiting for.
    /// Empty while the world is held (or while nobody is waiting on anybody).</summary>
    public string WaitingFor { get; set; } = string.Empty;
}
