// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Ratchet for the block-placement rule table (GameServerPlacement.cs): the checks run in the order the old
/// one-method HandlePlace had them — except the reach, which runs right after the height band since #3199 — and the
/// first refusal wins, so a reordered, dropped or renamed rule changes which reason a player reads (or whether the world
/// is touched before the height band and the reach) and must fail here first. The two guards that touch no world cell
/// lead: the rules after them read the target cell, which loads or generates its chunk (see FarBlockEditReachTests).
/// The reject strings themselves are covered by DoorwayTests, BedPairTests, LightMountTests,
/// StationProtectionScopeTests, BioFixFollowUpTests, UnderwaterPlacementTests, TorchAirTests and MultiCellFormTests.
/// </summary>
public sealed class PlacementRuleOrderTests
{
    [Fact]
    public void PlacementRules_RunInTheOrderHandlePlaceHadThem_WithTheReachRightAfterTheHeightBand()
    {
        Assert.Equal(
            new[]
            {
                "height_limit", "reach", "torch_air", "cell_free", "door_here", "fluid_cell", "above_head",
                "landing_pad", "station", "monument", "factory", "base", "ship_interior", "ship_core", "flora", "base_core",
                "base_pad", "light_mount", "bed_room", "form_room",
            },
            SvGameServer.PlacementRuleNamesForTest);
    }
}
