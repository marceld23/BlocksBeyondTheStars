// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Drives the real NetworkClient against the real in-process GameServer: join handshake and the
/// authoritative chunk stream that follows, asserting from the CLIENT'S point of view (events fired,
/// ClientWorld populated) — not the server's internal state.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class JoinAndStreamingTests
{
    private static GameContent LoadContent() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Fact]
    public void Join_RaisesJoinAccepted_OnTheClient()
    {
        using var h = new ClientServerHarness(LoadContent());

        h.Join("Pilot");

        Assert.NotNull(h.JoinAccepted);
        Assert.Null(h.JoinRejected);
        Assert.False(string.IsNullOrEmpty(h.JoinAccepted!.PlayerId));
        Assert.True(h.Client.Connected);
    }

    [Fact]
    public void AfterJoin_TheClientReceivesChunks_AndClientWorldMatchesTheServer()
    {
        using var h = new ClientServerHarness(LoadContent());
        h.Join("Pilot");

        // Pump until the player's own chunk column has streamed in.
        bool got = h.PumpUntil(() => h.Chunks.Count > 0, maxTicks: 60);
        Assert.True(got, "Expected at least one chunk to reach the client after join.");

        // The client's world view agrees with the authoritative server at the player's feet.
        var session = h.Server.Sessions[1];
        int px = (int)System.Math.Floor(session.State.Position.X);
        int pz = (int)System.Math.Floor(session.State.Position.Z);

        // Find a non-air ground cell below the player on the server, then confirm the client sees the same id.
        int topY = (int)System.Math.Ceiling(session.State.Position.Y);
        bool checkedAny = false;
        for (int y = topY; y > topY - 8; y--)
        {
            var serverBlock = h.Server.World.GetBlock(new Vector3i(px, y, pz));
            // Only compare cells whose chunk actually reached the client.
            var coord = Shared.World.WorldConstants.WorldToChunk(new Vector3i(px, y, pz));
            if (!h.Chunks.ContainsKey((coord.X, coord.Y, coord.Z)))
            {
                continue;
            }

            var clientBlock = h.World.GetBlock(px, y, pz);
            Assert.Equal(serverBlock.Value, clientBlock.Value);
            checkedAny = true;
        }

        Assert.True(checkedAny, "Expected the player's own chunk to be among those streamed to the client.");
    }
}
