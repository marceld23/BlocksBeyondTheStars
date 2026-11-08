// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3416 — the server's seam for the story: <c>NearestRuneShrine</c> finds the altar of the nearest complete rune shrine of the
/// active world (terrain generation 36), and the generated ring there is the Sounders' ring — striking one of its stones in
/// chapter 2 starts the shrine's echo game (s2.1), exactly as a ring the player built would. An older world has none.
/// </summary>
public sealed class RuneShrineServerTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static NpcLifeWorld World(string name, int generation = WorldDescription.CurrentTerrainGeneration)
        => new(Content.Value, name, padHalf: 4, configure: c =>
        {
            c.StartPlanet = "meadowlands";
            c.Seed = 20261108;
            c.World.TerrainGeneration = generation;
        });

    private static void Use(NpcLifeWorld w, Vector3i cell)
        => w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new UseBlockIntent { X = cell.X, Y = cell.Y, Z = cell.Z }));

    /// <summary>The first rune stone around the altar (the ring is four out; a stone is two or three tall).</summary>
    private static Vector3i FirstRingStone(NpcLifeWorld w, Vector3i altar)
    {
        var rune = w.Block("rune_stone");
        for (int dx = -5; dx <= 5; dx++)
            for (int dz = -5; dz <= 5; dz++)
            {
                var cell = new Vector3i(altar.X + dx, altar.Y, altar.Z + dz);
                if (w.Server.World.GetBlock(cell) == rune)
                {
                    return cell;
                }
            }

        throw new InvalidOperationException("no rune stone around the altar");
    }

    [Fact]
    public void TheNearestCompleteShrine_IsAnAltarInARing_AndItsRingPlaysTheShrinesEchoGameInChapterTwo()
    {
        using var w = World("rune_shrine_nearest");
        var altar = w.Server.NearestRuneShrine(new Vector3f(0.5f, 70f, 0.5f));
        Assert.NotNull(altar);
        Assert.Equal(w.Block("ancient_brick"), w.Server.World.GetBlock(altar!.Value));

        var stone = FirstRingStone(w, altar.Value);
        var site = w.Server.MelodySiteAt(stone, out int pillar);
        Assert.NotNull(site);
        Assert.Equal(SvGameServer.MelodySiteKindRing, site!.Kind);
        Assert.InRange(pillar, 0, 7);

        // s2.1 "Answer the Singing Shrine": the generated ring starts the echo game like the one-of-a-kind shrine would.
        w.Player.State.Position = new Vector3f(stone.X + Math.Sign(altar.Value.X - stone.X) + 0.5f, stone.Y,
            stone.Z + Math.Sign(altar.Value.Z - stone.Z) + 0.5f);
        Assert.StartsWith("Chapter 2", w.Server.SoundersAdminForTest(w.Player, "chapter 2"), StringComparison.Ordinal);
        Use(w, stone);
        Assert.Equal(SoundersIds.PuzzleShrineEcho, w.Server.RunningMelodyPuzzle(w.Player));
    }

    [Fact]
    public void AnOlderWorld_HasNoRuneShrine()
    {
        using var w = World("rune_shrine_old", WorldDescription.RuneShrineGeneration - 1);
        Assert.Null(w.Server.NearestRuneShrine(new Vector3f(0.5f, 70f, 0.5f), 2000));
    }
}
