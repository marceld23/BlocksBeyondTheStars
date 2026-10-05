// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// "Back to my ship" (#2286): the general way out for a player on foot who is stuck — lost, trapped in a cave they
/// cannot dig out of, stranded on a summit above the atmosphere. A button in the pause menu asks the server, and the
/// server (authoritative, as always) teleports the player back aboard their own ship when every gate below holds:
/// the world rule <c>GameRules.ReturnToShip</c> is on, the player is on foot (not aboard, not in space or EVA, not in a
/// speeder or a train), their own ship is landed on the body they stand on, they are not in a fight, they are not
/// falling, and the cooldown has run out. It costs nothing but time — the suit teleporter (10 energy, 30 s) stays the
/// faster device for those who crafted it, and the three-minute cooldown keeps the starter teleporter meaningful.
/// </summary>
public static class ReturnToShipRules
{
    /// <summary>Seconds between two uses per player. Long on purpose: a shorter one would make the suit teleporter
    /// pointless; three minutes is still far shorter than suffocating on a summit.</summary>
    public const double CooldownSeconds = 180.0;

    /// <summary>"In a fight" lasts this long after the last blow: the player hit a creature, a machine or a bandit, or
    /// one of them hurt the player. A pending bandit hold-up counts as a fight for as long as the robber waits.</summary>
    public const double CombatGraceSeconds = 10.0;

    /// <summary>Downward speed (blocks per second, from the player's own position reports) above which the player
    /// counts as falling — well above the zero-g sink above the atmosphere (1.5) and a hop down a block or two, well
    /// below a harmful landing (14 · √g). A fall must not end aboard the ship with no landing at all.</summary>
    public const float FallingSpeed = 8f;

    /// <summary>A position report older than this says nothing about falling any more (the client reports several
    /// times a second while it moves; a standing player may be silent longer).</summary>
    public const double FallSampleMaxAgeSeconds = 1.0;

    /// <summary>The remaining cooldown as the player sees it in the pause menu and in the refusal: <c>m:ss</c>, never
    /// below <c>0:01</c> for a positive remainder, so a button that is still grey never shows zero.</summary>
    public static string FormatRemaining(double seconds)
    {
        int whole = (int)System.Math.Ceiling(System.Math.Max(0.0, seconds));
        if (whole <= 0)
        {
            return "0:00";
        }

        return (whole / 60).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":"
            + (whole % 60).ToString("00", System.Globalization.CultureInfo.InvariantCulture);
    }
}
