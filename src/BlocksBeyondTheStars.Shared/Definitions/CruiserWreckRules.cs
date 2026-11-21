// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The vocabulary and the numbers of the ESCORT CRUISER'S WRECK (#3902, canon C22): a beaten escort cruiser does not vanish —
/// it goes dark and stays where it fell as a wreck the players can board. Only robots are aboard (the guardian bay), and on
/// its bridge stands a console with a red light that a hacking chip turns blue. The wreck is a derelict of its own family
/// (<see cref="IdPrefix"/>), so every derelict rule holds for it by its id — boardable by anyone, no air, nobody home, no
/// shield, falling out is a spacewalk — and its hull is the pin-only giant ship <see cref="TemplateKey"/>, built with the
/// "fresh" ruin option (shot a moment ago: holes and broken glass, no soot, no rust, a share of red lamps still burning).
/// Shared by the server and the client (which may tell the wreck from a derelict station by its id).
/// </summary>
public static class CruiserWreckRules
{
    /// <summary>The id prefix of a cruiser's wreck: <c>derelict:cruiser-&lt;system&gt;-&lt;n&gt;</c> — a derelict
    /// (<c>derelict:</c>) of its own family. <c>n</c> counts the wrecks a system has held, so a wreck that replaces an older
    /// one never inherits anything stored under the old id (its interior world, its chests, a player's milestones).</summary>
    public const string IdPrefix = "derelict:cruiser-";

    /// <summary>The giant ship template the wreck is built from (<c>data/giant_ships/escort_cruiser.json</c>, pin-only: never
    /// rolled as a planet or space giant wreck).</summary>
    public const string TemplateKey = "escort_cruiser";

    /// <summary>The locale key of the wreck's name on the star map and in flight.</summary>
    public const string NameKey = "station.cruiser_wreck.name";

    /// <summary>The size tier its boardable record carries.</summary>
    public const string SizeTier = "huge";

    /// <summary>The item that opens the bridge console.</summary>
    public const string ChipItem = "hacking_chip";

    /// <summary>ECHO's memory core — the keepsake every player aboard gets when she is freed.</summary>
    public const string EchoCoreItem = "echo_memory_core";

    /// <summary>The lore-site kind of the bridge (the VEGA pack's <c>lore_sites.json</c>): ECHO's core record
    /// (<see cref="EchoLoreKey"/>) and the ship's logs of the later cruisers.</summary>
    public const string LoreSite = "cruiser_bridge";

    /// <summary>The key of the one lore text ECHO's memory core carries (site <see cref="LoreSite"/>): never drawn at a later
    /// cruiser's console, always the text VEGA reads from the core.</summary>
    public const string EchoLoreKey = "cruiser_echo_core";

    /// <summary>The VEGA pack's cutscene of ECHO speaking (played for everyone aboard when she is freed).</summary>
    public const string EchoCutscene = "echo.speaks";

    /// <summary>The sound cue of the bridge's lamps turning blue.</summary>
    public const string LightsToBlueSound = "lights_to_blue";

    /// <summary>The argument of <c>cruiser:boarded</c> for the wreck that carries ECHO, and for every other.</summary>
    public const string BoardedEcho = "echo", BoardedPlain = "plain";

    /// <summary>How far round the console (blocks: sideways, up and down) the bridge's red lamps reach — what the "fresh"
    /// decay keeps burning for sure, and what the chip turns blue.</summary>
    public const int BridgeLampReach = 12, BridgeLampReachY = 6;

    /// <summary>Whether <paramref name="id"/> is an escort cruiser's wreck.</summary>
    public static bool IsCruiserWreckId(string? id) => id != null && id.StartsWith(IdPrefix, System.StringComparison.Ordinal);

    /// <summary>The id of the <paramref name="serial"/>-th wreck of a system.</summary>
    public static string IdOf(string systemId, int serial)
        => IdPrefix + systemId + "-" + serial.ToString(CultureInfo.InvariantCulture);
}
