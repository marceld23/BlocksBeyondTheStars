// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.State;

/// <summary>The place a cutscene's <c>custom</c> anchor framed when the scene was committed to a player (#2459), kept in
/// <see cref="PlayerState.CutsceneAnchors"/> until the client confirms the scene: a scene owed across a disconnect is
/// re-offered at the next join with the same place — on the same world only (a position means nothing on another
/// body, so the client then falls back to the player).</summary>
public sealed class CutsceneAnchor
{
    /// <summary>The body the player was on when the scene was committed (<see cref="PlayerState.CurrentLocationId"/>).</summary>
    public string LocationId { get; set; } = string.Empty;

    /// <summary>The anchor position in that body's world coordinates.</summary>
    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }
}
