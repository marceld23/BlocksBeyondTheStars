// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Sounders;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The Sounder instruments on the Crystal Net's melody block (#3345): besides crystal, bell, bass and blip (0–3) the block
/// can sing with the Sounder voice (4) and the Sounder choir (5) — chosen only by a player who has the hidden blueprint
/// <see cref="SoundersIds.BlueprintVoice"/> (or plays in a free game mode, like the bio lab's functions). A device a friend
/// with the blueprint set to 4 or 5 keeps its voice when someone without it changes only the note: the gate refuses a
/// NEW choice of 4/5, never one already stored.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The highest instrument a melody block plays.</summary>
    internal const int MelodyBlockMaxInstrument = SoundersIds.InstrumentChoir;

    /// <summary>Whether <paramref name="session"/> may choose the Sounder instruments on a melody block.</summary>
    internal bool MaySoundSounderInstruments(PlayerSession session)
        => !Rules.CraftingCostsMaterialsFor(session.State.ModeOverride)
           || session.State.UnlockedBlueprints.Contains(SoundersIds.BlueprintVoice);

    /// <summary>The configure path's gate (called with the cleaned config before it is stored): a melody block's
    /// <c>inst</c> of 4 or 5 the player may not choose falls back to what the block had.</summary>
    private string GateMelodyInstrument(PlayerSession session, ServerCrystalCell cell, string fresh)
    {
        if (cell.Kind != CrystalDeviceKind.MelodyBlock)
        {
            return fresh;
        }

        int wanted = CrystalConfigInt(fresh, "inst", 0);
        int before = CrystalConfigInt(cell.Config, "inst", 0);
        if (wanted < SoundersIds.InstrumentVoice || wanted == before || MaySoundSounderInstruments(session))
        {
            return fresh;
        }

        return CrystalConfigWith(fresh, "inst", before.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Test seam: the stored config line of the Crystal Net device at <paramref name="cell"/> (active world).</summary>
    internal string? CrystalDeviceConfigForTest(Shared.Geometry.Vector3i cell)
        => CrystalNet.Cells.TryGetValue(cell, out var c) ? c.Config : null;
}
