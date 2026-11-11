// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The client side of the landmarks (#3494, #3497): the sound emitter mix (louder as the listener comes closer, three
/// voices, one per sound), the far silhouette's mesh (the skin of the coarse model) and where it is cut away, and —
/// through the real wire — the list a client is sent when it joins a world that carries a landmark.
/// </summary>
public sealed class LandmarkClientTests
{
    private static SoundEmitterMix.Emitter E(float x, string sound, float radius = 100f, float volume = 1f)
        => new(x, 0f, 0f, sound, radius, volume);

    [Fact]
    public void AnEmitter_IsSilentAtItsRim_SwellsOnTheWayIn_AndIsFullNearTheSource()
    {
        Assert.Equal(0f, SoundEmitterMix.Level(100f, 100f, 0.8f));
        Assert.Equal(0f, SoundEmitterMix.Level(250f, 100f, 0.8f));
        Assert.Equal(0.8f, SoundEmitterMix.Level(0f, 100f, 0.8f), 4);
        Assert.Equal(0.8f, SoundEmitterMix.Level(12f, 100f, 0.8f), 4); // within the full ring (12 % of the radius)
        Assert.Equal(0f, SoundEmitterMix.Level(10f, 0f, 1f));
        Assert.Equal(0f, SoundEmitterMix.Level(10f, 100f, 0f));
        Assert.Equal(1f, SoundEmitterMix.Level(0f, 100f, 3f)); // a volume above 1 is capped

        float last = 0f;
        for (float d = 99f; d >= 12f; d -= 3f)
        {
            float level = SoundEmitterMix.Level(d, 100f, 0.8f);
            Assert.True(level > last, $"the level did not rise between {d + 3} and {d} blocks: {last} → {level}");
            last = level;
        }

        Assert.InRange(SoundEmitterMix.Level(56f, 100f, 1f), 0.45f, 0.55f); // halfway in, about half
    }

    [Fact]
    public void TheLoudestThreePlay_OnePerSound_AndSilentOnesNever()
    {
        var emitters = new List<SoundEmitterMix.Emitter>
        {
            E(90f, "amb_sounder"), E(20f, "amb_sounder"), E(50f, "soundi_call_01"), E(70f, "plant_harp_vine_loop"),
            E(30f, "plant_chime_reed_loop"), E(400f, "amb_sounder"), E(5f, string.Empty),
        };
        var picks = new List<SoundEmitterMix.Voice>();
        SoundEmitterMix.Pick(emitters, 0f, 0f, 0f, picks);
        Assert.Equal(new[] { 1, 4, 2 }, picks.Select(p => p.Emitter)); // 20, 30 and 50 blocks away; the farther amb_sounder is the same sound
        Assert.True(picks[0].Level > picks[1].Level && picks[1].Level > picks[2].Level);

        SoundEmitterMix.Pick(emitters, 1000f, 0f, 0f, picks);
        Assert.Empty(picks);
    }

    private static NetLandmark Model(int w, int h, int l, params (int X, int Y, int Z, byte P)[] filled)
    {
        var cells = new byte[w * h * l];
        foreach (var (x, y, z, p) in filled)
        {
            cells[(((x * h) + y) * l) + z] = p;
        }

        return new NetLandmark { Width = w, Height = h, Length = l, Step = 4, Cells = cells, Blocks = new ushort[] { 1, 2 }, Tints = new int[2] };
    }

    [Fact]
    public void TheSilhouetteMesh_IsTheSkinOfTheModel()
    {
        var one = LandmarkSilhouetteMesh.Build(Model(3, 3, 3, (1, 1, 1, 1)));
        Assert.Equal(6, one.QuadCount);
        Assert.Equal(24, one.VertexCount);
        Assert.Equal(36, one.Triangles.Length);
        Assert.Equal(one.VertexCount * 3, one.Positions.Length);
        Assert.Equal(one.VertexCount * 3, one.Normals.Length);
        Assert.All(one.Palette, p => Assert.Equal(1, p));
        Assert.Equal(4f, one.Positions.Min()); // the cell spans 4..8 blocks on every axis
        Assert.Equal(8f, one.Positions.Max());

        // Two cells side by side share a face that is never drawn; each keeps its own palette index.
        var two = LandmarkSilhouetteMesh.Build(Model(3, 1, 1, (0, 0, 0, 1), (1, 0, 0, 2)));
        Assert.Equal(10, two.QuadCount);
        Assert.Equal(20, two.Palette.Count(p => p == 1));
        Assert.Equal(20, two.Palette.Count(p => p == 2));

        // A solid block of cells costs only its skin.
        var solid = new List<(int, int, int, byte)>();
        for (int x = 0; x < 4; x++)
        {
            for (int y = 0; y < 4; y++)
            {
                for (int z = 0; z < 4; z++)
                {
                    solid.Add((x, y, z, 1));
                }
            }
        }

        Assert.Equal(6 * 16, LandmarkSilhouetteMesh.Build(Model(4, 4, 4, solid.ToArray())).QuadCount);

        // Every face looks away from its cell: the normal points from the cell's centre to the face's centre.
        for (int q = 0; q < one.QuadCount; q++)
        {
            float cx = 0, cy = 0, cz = 0;
            for (int v = q * 4; v < (q * 4) + 4; v++)
            {
                cx += one.Positions[v * 3] / 4f;
                cy += one.Positions[(v * 3) + 1] / 4f;
                cz += one.Positions[(v * 3) + 2] / 4f;
            }

            float dot = ((cx - 6f) * one.Normals[q * 12]) + ((cy - 6f) * one.Normals[(q * 12) + 1]) + ((cz - 6f) * one.Normals[(q * 12) + 2]);
            Assert.Equal(2f, dot, 3);
        }

        // An empty or malformed model draws nothing.
        Assert.Equal(0, LandmarkSilhouetteMesh.Build(Model(2, 2, 2)).QuadCount);
        Assert.Equal(0, LandmarkSilhouetteMesh.Build(new NetLandmark { Width = 2, Height = 2, Length = 2, Cells = new byte[3] }).QuadCount);
        Assert.Equal(0, LandmarkSilhouetteMesh.Build(new NetLandmark()).QuadCount);
    }

    [Theory]
    [InlineData(288f, 16, 272f)]  // a desktop at 16 chunks: the draw distance decides
    [InlineData(1000f, 8, 160f)]  // eight chunks: the server's sphere (8 + 4 − 1 = 11 chunks) decides
    [InlineData(64f, 3, 48f)]     // the browser: fog at four chunks
    [InlineData(8f, 1, 0f)]       // never negative
    public void TheSilhouette_IsCutAwayWhereTheRealBlocksAreDrawn(float draw, int viewChunks, float expected)
        => Assert.Equal(expected, LandmarkSilhouetteMesh.NearCut(draw, viewChunks));

    /// <summary>A small hollow tower with a sound marker — built here, the client tests do not see the server tests' shapes.</summary>
    private static StructureTemplate Tower()
    {
        var t = new StructureTemplate { Key = "client_tower", Name = "Client Tower", Landmark = true, Width = 12, Height = 24, Length = 12 };
        for (int y = 0; y < 24; y++)
        {
            for (int x = 0; x < 12; x++)
            {
                for (int z = 0; z < 12; z++)
                {
                    if (y == 0 || x == 0 || z == 0 || x == 11 || z == 11)
                    {
                        t.Cells.Add(new TemplateCell { X = x, Y = y, Z = z, Kind = "block", Id = "stone" });
                    }
                }
            }
        }

        t.Cells.Add(new TemplateCell { X = 6, Y = 1, Z = 6, Kind = "marker", Id = LandmarkRules.SoundMarker, Config = "sound=amb_sounder;radius=80;volume=0.7" });
        return t;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)] // the encoded wire, as on a socket: the model's bytes and the palette must survive MessagePack
    public void AJoiningClient_IsSentTheWorldsLandmark_ItsSilhouetteAndItsEmitter(bool passObjects)
    {
        var content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        content.SetLandmarks(new[] { Tower() });
        using var h = new ClientServerHarness(content, c =>
        {
            c.StartPlanet = "meadowlands";
            c.PlaceSettlements = false;
            c.LandmarkChance = 1.0;
        }, passObjects: passObjects);
        LandmarkList? list = null;
        h.Client.LandmarksReceived += m => list = m;
        h.Join();
        Assert.NotNull(h.JoinAccepted);
        Assert.True(h.PumpUntil(() => list != null), "no LandmarkList arrived after the join");

        var lm = Assert.Single(list!.Landmarks);
        Assert.Equal("client_tower", lm.Key);
        Assert.Equal("Client Tower", lm.Name);
        Assert.Equal((3, 6, 3), (lm.Width, lm.Height, lm.Length));
        Assert.Equal(lm.Width * lm.Height * lm.Length, lm.Cells.Length);
        Assert.Equal(content.GetBlock("stone")!.NumericId.Value, Assert.Single(lm.Blocks));

        var mesh = LandmarkSilhouetteMesh.Build(lm);
        Assert.True(mesh.QuadCount > 50, $"the silhouette has only {mesh.QuadCount} quads");

        var emitter = Assert.Single(list.Emitters);
        Assert.Equal(("amb_sounder", 80f), (emitter.Sound, emitter.Radius));
        Assert.Equal(0.7f, emitter.Volume, 3);
    }

    [Fact]
    public void AWorldWithoutALandmark_SendsAnEmptyList()
    {
        var content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        using var h = new ClientServerHarness(content);
        LandmarkList? list = null;
        h.Client.LandmarksReceived += m => list = m;
        h.Join();
        Assert.True(h.PumpUntil(() => list != null), "no LandmarkList arrived after the join");
        Assert.Empty(list!.Landmarks);
        Assert.Empty(list.Emitters);
    }
}
