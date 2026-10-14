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
    /// <summary>The reason of the <c>RespawnNotice</c> snap that puts the player back aboard — the client's sign that the
    /// trip went ahead (#2559: the pause menu closes on it, and only on it).</summary>
    public const string DoneToken = "@srv.return_ship.done";

    /// <summary>The refusal while a fight blocks the trip. Since #2559 a blow's grace carries the seconds left as its
    /// argument (<c>@srv.return_ship.in_combat:7</c>); a waiting robber sends the bare token (no countdown — the hold-up
    /// ends with the player's answer, not with a clock). The key's own text has no slot, so every client reads a plain
    /// sentence from it; a client that knows <see cref="InCombatLeftKey"/> shows the seconds instead
    /// (<see cref="ReasonKey"/>) — and an older server's bare token still reads the plain sentence there.</summary>
    public const string InCombatToken = "@srv.return_ship.in_combat";

    /// <summary>The locale key of the fight refusal WITH its seconds (<c>{name}</c>) — see <see cref="ReasonKey"/>.</summary>
    public const string InCombatLeftKey = "srv.return_ship.in_combat_left";

    /// <summary>The <c>PlayerStateUpdate.ReturnToShipFightSeconds</c> value that says "a robber waits for the answer":
    /// the pause-menu row is blocked and names the robber, without a countdown (#2559).</summary>
    public const float RobberWaitingFightSeconds = -1f;

    /// <summary>The locale key a client shows for a server token's key and argument: the fight refusal with seconds reads
    /// <see cref="InCombatLeftKey"/>; every other token is its own key.</summary>
    public static string ReasonKey(string key, string? arg)
        => key == InCombatToken.Substring(1) && !string.IsNullOrEmpty(arg) ? InCombatLeftKey : key;

    /// <summary>Seconds between two uses per player. Long on purpose: a shorter one would make the suit teleporter
    /// pointless; three minutes is still far shorter than suffocating on a summit.</summary>
    public const double CooldownSeconds = 180.0;

    /// <summary>"In a fight" lasts this long after the last blow: the player hit a creature, a machine or a bandit, or
    /// one of them hurt the player. A pending bandit hold-up counts as a fight for as long as the robber waits. Time the
    /// world stands held by the pause menu counts toward it (#2558): a held world stops every attacker, and the button
    /// lives in exactly that menu — a grace that froze with the world refused every click for as long as the menu stayed
    /// open.</summary>
    public const double CombatGraceSeconds = 10.0;

    /// <summary>Downward speed (blocks per second, from the player's own position reports) above which the player
    /// counts as falling — well above the zero-g sink above the atmosphere (1.5) and a hop down a block or two, well
    /// below a harmful landing (14 · √g). A fall must not end aboard the ship with no landing at all.</summary>
    public const float FallingSpeed = 8f;

    /// <summary>A position report older than this says nothing about falling any more (the client reports several
    /// times a second while it moves; a standing player may be silent longer). It ages behind the pause menu too (#2558):
    /// a menu opened mid-fall no longer reads "falling" for as long as it stays open.</summary>
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

    /// <summary>The fight that still blocks the trip (#2559), as the pause-menu row and the refusal show it: whole
    /// seconds, rounded up and never below 1 — a fight is short, so no <c>m:ss</c>, and a row that is still grey never
    /// shows zero.</summary>
    public static string FormatFightSeconds(double seconds)
    {
        int whole = (int)System.Math.Ceiling(System.Math.Max(1.0, seconds));
        return whole.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The seconds a fight refusal (<see cref="InCombatToken"/> with its argument) says are left, or 0 for any
    /// other reason — an older server's bare token included. The client re-arms the pause-menu countdown from it.</summary>
    public static double FightSecondsOf(string? reason)
    {
        if (reason is null || reason.Length <= InCombatToken.Length + 1
            || !reason.StartsWith(InCombatToken + ":", System.StringComparison.Ordinal))
        {
            return 0.0;
        }

        return double.TryParse(reason.Substring(InCombatToken.Length + 1), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double seconds) && seconds > 0.0 && seconds < 3600.0
            ? seconds
            : 0.0; // a garbled or absurd number must not grey the row out for good
    }
}
