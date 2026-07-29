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
/// The server only honours it while this is the sole joined player, so nobody can freeze a world out from under
/// anyone else on a dedicated or hosted server.
/// </summary>
public sealed class PauseIntent
{
    public bool Paused { get; set; }
}

/// <summary>
/// Whether the world is actually holding, so the client can show it (and stop pretending the menu paused
/// something when it did not — e.g. a second player joined and the pause was lifted).
/// </summary>
public sealed class PauseState
{
    public bool Paused { get; set; }

    /// <summary>False when the server declined to pause — more than one player is joined. The client leaves the
    /// menu behaving exactly as it did before instead of claiming a pause it did not get.</summary>
    public bool Allowed { get; set; }
}
