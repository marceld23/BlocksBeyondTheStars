// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Configuration;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// On-foot player-versus-player (#2972, Justus: "switch weapons and friendly fire on and off in the world settings"). The
/// world rules <see cref="GameRules.Pvp"/> and <see cref="GameRules.FriendlyFire"/> decide who may hurt whom; the server
/// adds the situational guards (aboard a ship, in space, a fresh spawn …) and resolves every hit. Kid-friendly by design:
/// off by default, a player defeated by another never loses an item, and a duel ends without a death.
/// </summary>
public static class PvpRules
{
    /// <summary>Entity-id prefix of a player as an attack target (<c>player:&lt;playerId&gt;</c>) — the client aims at an
    /// avatar with it, the server finds the session behind it.</summary>
    public const string PlayerTargetPrefix = "player:";

    /// <summary>Seconds a player cannot be hurt by another after joining or after being defeated by one.</summary>
    public const double SpawnProtectionSeconds = 10.0;

    /// <summary>Seconds a duel challenge waits for its answer.</summary>
    public const double DuelRequestSeconds = 30.0;

    /// <summary>A duel ends on its own after this long (nobody duels forever by accident).</summary>
    public const double DuelMaxSeconds = 300.0;

    /// <summary>The health a duel's loser gets back up with — a duel never kills.</summary>
    public const float DuelLoserHealth = 100f;

    public const string OffToken = "@srv.pvp.off";
    public const string DuelsOnlyToken = "@srv.pvp.duels_only";
    public const string NoCrewToken = "@srv.pvp.no_crew";
    public const string AllyToken = "@srv.pvp.ally";
    public const string ProtectedToken = "@srv.pvp.protected";

    /// <summary>The reason a player defeated by another respawns with — <c>@srv.pvp.defeated_you:&lt;winner&gt;</c>.</summary>
    public const string DefeatedYouToken = "@srv.pvp.defeated_you";

    /// <summary>The attack-target id of a player.</summary>
    public static string TargetId(string playerId) => PlayerTargetPrefix + playerId;

    /// <summary>The player id behind an attack-target id, or false for any other entity.</summary>
    public static bool TryPlayerId(string? entityId, out string playerId)
    {
        if (entityId is not null && entityId.Length > PlayerTargetPrefix.Length
            && entityId.StartsWith(PlayerTargetPrefix, System.StringComparison.Ordinal))
        {
            playerId = entityId.Substring(PlayerTargetPrefix.Length);
            return true;
        }

        playerId = string.Empty;
        return false;
    }

    /// <summary>
    /// Why the world's rules forbid this attacker to hurt this target — a reject token — or null when they allow it. The
    /// situation (ship, space, spawn protection) is the server's to check; this is only the rule table:
    /// <list type="bullet">
    /// <item><b>Off</b>: nobody.</item>
    /// <item><b>DuelsOnly</b>: only two players in a duel with each other.</item>
    /// <item><b>GroupBased</b>: players in different crews; a player without a crew takes no part.</item>
    /// <item><b>On</b>: everybody.</item>
    /// </list>
    /// Allies and crew mates are spared unless <paramref name="friendlyFire"/> is on. A duel is agreed by both, so it is
    /// fought in every mode but Off, allies or not.
    /// </summary>
    public static string? Refusal(PvpMode mode, bool friendlyFire, bool allied, string attackerCrew, string targetCrew, bool duelTogether)
    {
        if (mode == PvpMode.Off)
        {
            return OffToken;
        }

        if (duelTogether)
        {
            return null;
        }

        if (mode == PvpMode.DuelsOnly)
        {
            return DuelsOnlyToken;
        }

        if (mode == PvpMode.GroupBased)
        {
            if (string.IsNullOrEmpty(attackerCrew) || string.IsNullOrEmpty(targetCrew))
            {
                return NoCrewToken;
            }

            if (attackerCrew == targetCrew)
            {
                return friendlyFire ? null : AllyToken;
            }
        }

        return allied && !friendlyFire ? AllyToken : null;
    }
}
