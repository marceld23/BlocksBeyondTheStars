// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Tests.Helpers;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3607: a giant wreck is open to mining, so a giant ship never carries a DEVICE block (machine, light, door) whose drop
/// only a blueprint recipe makes — a heal tank or a fabricator in a wreck would hand a blueprint-locked machine to anyone
/// with a drill. Building materials and plants are not devices and stay allowed. Every shipped giant ship passes.
/// </summary>
public sealed class GiantShipDeviceTests
{
    [Theory]
    [InlineData("heal_tank")]
    [InlineData("fabricator")]
    public void ABlueprintLockedMachine_IsAProblem(string block)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var t = GiantShipTemplateTests.Ship();
        t.Cells.Add(LandmarkShapes.Block(4, 1, 1, block));

        Assert.Contains(content.GiantShipProblems(t), p => p.Contains("blueprint-locked", StringComparison.Ordinal) && p.Contains(block, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("workbench")]
    [InlineData("bed")]
    [InlineData("glass_clear")]
    public void AFreeMachine_OrABuildingMaterial_IsFine(string block)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var t = GiantShipTemplateTests.Ship();
        t.Cells.Add(LandmarkShapes.Block(4, 1, 1, block));

        Assert.DoesNotContain(content.GiantShipProblems(t), p => p.Contains("blueprint-locked", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryShippedGiantShip_LoadsWithoutAProblem()
    {
        var warnings = new List<string>();
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir(), warn: warnings.Add);

        Assert.DoesNotContain(warnings, w => w.Contains("giant", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(content.GiantShips);
        foreach (var ship in content.GiantShips)
        {
            Assert.Empty(content.GiantShipProblems(ship));
        }
    }
}
