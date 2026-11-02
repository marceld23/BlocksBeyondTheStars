// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3088 — the person under the crosshair (<see cref="NpcAim"/>): looking at somebody picks them for E, so a resident beside a
/// door can be talked to. The eye stands 1.6 m over the feet at (0, 70, 0) looking along +X; people stand on the same floor.
/// </summary>
public sealed class NpcAimTests
{
    private const int C = 3232;
    private const float Feet = 70f, Eye = 71.6f;

    private static NetNpc Npc(int id, float x, float z) => new() { Id = id, X = x, Y = Feet, Z = z };

    private static NetNpc? Look(NetNpc[] npcs, float dx, float dy, float dz, out float distance, float px = 0f, float pz = 0f, float ox = 0f)
    {
        float len = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        return NpcAim.Aimed(npcs, ox, Eye, 0f, dx / len, dy / len, dz / len, 8f, px, Feet, pz, C, out distance);
    }

    [Fact]
    public void ThePersonInFront_IsPicked_AtTheirBody()
    {
        var hit = Look(new[] { Npc(1, 3f, 0f) }, 1f, 0f, 0f, out float distance);
        Assert.Equal(1, hit?.Id);
        Assert.InRange(distance, 3f - NpcAim.BodyRadius - 0.01f, 3f - NpcAim.BodyRadius + 0.01f);
    }

    [Fact]
    public void LookingBeside_Over_OrAwayFromThem_PicksNobody()
    {
        var npcs = new[] { Npc(1, 3f, 0f) };
        Assert.Null(Look(npcs, 1f, 0f, 0.5f, out _));  // a metre and a half beside them at their distance
        Assert.Null(Look(npcs, 1f, 0.6f, 0f, out _));  // over their head
        Assert.Null(Look(npcs, -1f, 0f, 0f, out _));   // the other way
        Assert.Null(Look(npcs, 0f, 1f, 0f, out _));    // straight up
    }

    [Fact]
    public void TheNearerOfTwoInLine_IsPicked_AndOnlyWithinTalkReach()
    {
        var npcs = new[] { Npc(2, 4f, 0f), Npc(1, 2f, 0f) };
        Assert.Equal(1, Look(npcs, 1f, 0f, 0f, out _)?.Id);

        // Six metres off is beyond the talk reach (InteractionReach.Talk) — nobody to talk to, however well aimed.
        Assert.Null(Look(new[] { Npc(3, InteractionReach.Talk + 1f, 0f) }, 1f, 0f, 0f, out _));
    }

    [Fact]
    public void LookingDownAtSomebodyClose_StillPicksThem()
    {
        Assert.Equal(1, Look(new[] { Npc(1, 1.2f, 0f) }, 1f, -0.8f, 0f, out _)?.Id);
    }

    [Fact]
    public void APersonAcrossTheSeam_IsPicked()
    {
        // The player stands at scene X = -1 (west of the seam); the person is kept canonical at C - 3, two metres further west.
        var hit = Look(new[] { Npc(4, C - 3f, 0f) }, -1f, 0f, 0f, out float distance, px: -1f, ox: -1f);
        Assert.Equal(4, hit?.Id);
        Assert.InRange(distance, 2f - NpcAim.BodyRadius - 0.01f, 2f - NpcAim.BodyRadius + 0.01f);
    }

    [Fact]
    public void TheCylinder_ReportsZero_FromInside()
    {
        Assert.Equal(0f, NpcAim.RayCylinder(0.1, -1.0, 0.1, 1f, 0f, 0f, 0.45f, 1.9f));
        Assert.True(float.IsPositiveInfinity(NpcAim.RayCylinder(3, -1.0, 0, 1f, 0f, 0f, 0.45f, 0.5f))); // a short body under the ray
    }
}
