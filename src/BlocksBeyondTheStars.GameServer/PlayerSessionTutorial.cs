// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>Per-connection tutorial bookkeeping of a <see cref="PlayerSession"/> (RAM only, never persisted).</summary>
public sealed class PlayerSessionTutorial
{
    /// <summary>#2469: metres walked on foot not yet reported as <c>move:walked</c> (only counted while a step listens). RAM only.</summary>
    public double TutorialWalkAccum { get; set; }

    /// <summary>#2469: flight units flown not yet reported as <c>flight:flown</c> (only counted while a step listens). RAM only.</summary>
    public double TutorialFlightAccum { get; set; }

    /// <summary>#2469: the last ship position the flight counter measured from; null after a jump or a fresh entry. RAM only.</summary>
    public BlocksBeyondTheStars.Shared.Geometry.Vector3f? TutorialFlightLast { get; set; }

    /// <summary>#2469: server uptime of the last <c>move:jump</c> (one a second at most). RAM only.</summary>
    public double TutorialJumpAt { get; set; } = double.NegativeInfinity;

    /// <summary>#2469: whether the last move report was crouched (a crouch is reported on the change). RAM only.</summary>
    public bool TutorialWasCrouching { get; set; }

    /// <summary>#2469: the player has been far from the landed ship (the "walk back" check of <c>ship:returned</c>). RAM only.</summary>
    public bool TutorialFarFromShip { get; set; }

    /// <summary>#2469: per teaching-flag id, the server uptime of the last <c>UiTutorialEvent</c> accepted (rate limit). RAM only.</summary>
    public Dictionary<string, double> UiTutorialEventAt { get; } = new(System.StringComparer.Ordinal);

    /// <summary>#2480: server uptime of the last swing a tool-tier gate turned away (a better tool exists) — the moment the
    /// tutorial's "Workshop and titanium" course is offered at (<c>world:tier_gate</c>), independent of the tip's pacing and
    /// the hints switch. Session-only; the default lies far in the past.</summary>
    public double TierGateRefusedAt { get; set; } = double.NegativeInfinity;

    /// <summary>#2482: server uptime when an animal of a begging herd last came to beg this player — the moment the tutorial's
    /// "Animal friends" course is offered at (<c>world:herd_begging</c>), whatever VEGA's once-hint about it says.
    /// Session-only; the default lies far in the past.</summary>
    public double HerdBeggedAt { get; set; } = double.NegativeInfinity;

    /// <summary>#2482: metres climbed on a wall not yet reported as <c>move:climbed</c> (only counted while a step listens).
    /// RAM only.</summary>
    public double TutorialClimbAccum { get; set; }
}
