// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// <c>/tp</c> on its own prints the whole list of targets (#4034): it used to stop after 24 lines with "… N more",
/// which hid exactly the targets an admin was looking for — the chat scrolls, so nothing is cut any more.
/// </summary>
public sealed class AdminTeleportListTests
{
    [Fact]
    public void ALongList_PrintsEveryTarget_InOrder_WithoutAMoreLine()
    {
        var targets = Enumerable.Range(1, 60)
            .Select(i => new SvGameServer.TeleportTarget("lodge", i, $"Hut {i}", new Vector3f(i * 10, 70, 0)))
            .ToList();

        var lines = SvGameServer.TeleportListLines(targets, new Vector3f(0, 70, 0), "in space").ToList();

        Assert.Equal(60, lines.Count);
        for (int i = 0; i < lines.Count; i++)
        {
            Assert.StartsWith($"/tp lodge{i + 1} · Hut {i + 1} · ", lines[i]);
        }

        Assert.EndsWith("· 600 m", lines[^1]);
    }

    [Fact]
    public void AGiantWreckInSpace_SaysWhereItIs_InsteadOfADistance()
    {
        var targets = new[] { new SvGameServer.TeleportTarget("giantwreck", 1, "Ark", new Vector3f(5000, 0, 0), "station:derelict:x") };

        string line = Assert.Single(SvGameServer.TeleportListLines(targets, new Vector3f(0, 70, 0), "in space"));

        Assert.Equal("/tp giantwreck1 · Ark · in space", line);
    }
}
