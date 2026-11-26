// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The detail pass's Unity wiring, guarded as source text (the scripts cannot load headless) — #4046: the five chunk
/// drawers have no <c>Update()</c> of their own; GameBootstrap walks the chunks that carry their data once per frame; and
/// #4045: the chunk pipeline asks the cores rule, not a count per frame.
/// </summary>
public sealed class DetailPassWiringTests
{
    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    [Theory]
    [InlineData("GroundScatter.cs")]
    [InlineData("GrassShells.cs")]
    [InlineData("GrassClumps.cs")]
    [InlineData("PlantBodies.cs")]
    [InlineData("CrownClusters.cs")]
    public void ADrawer_HasNoUpdateOfItsOwn_AndOffersTheDetailPassItsReachAndItsDraw(string file)
    {
        string drawer = Script(file);

        // An Update() per chunk and drawer ran several thousand times a frame at view distance 8, mostly to return.
        Assert.DoesNotContain("void Update()", drawer);
        Assert.DoesNotContain("void LateUpdate()", drawer);
        Assert.Contains("public void Draw()", drawer);
        Assert.Contains("public static float FrameReach()", drawer);
        Assert.Contains("public bool HasData", drawer);
    }

    [Fact]
    public void GameBootstrap_RunsTheDetailPassOncePerFrame_AfterTheChunkPass()
    {
        string boot = Script("GameBootstrap.cs");
        int chunks = boot.IndexOf("TickChunkStreaming();", StringComparison.Ordinal);
        int detail = boot.IndexOf("DrawChunkDetail();", StringComparison.Ordinal);
        Assert.True(chunks > 0 && detail > chunks, "the detail pass follows the chunk pass in Update");

        string pass = Script("GameBootstrap.Detail.cs");
        foreach (string drawer in new[] { "GroundScatter", "GrassShells", "GrassClumps", "PlantBodies", "CrownClusters" })
        {
            Assert.Contains(drawer + ".FrameReach()", pass); // the once-a-frame step runs whether or not a chunk is in reach
        }

        Assert.Contains("ChunkWorkStats.DetailPass.AddSince(started);", pass);
    }

    [Fact]
    public void AChunkView_EntersTheDetailPassWithItsData_AndLeavesItBeforeItIsDestroyed()
    {
        string meadow = Script("GameBootstrap.Meadow.cs");
        Assert.Contains("TrackDetail(view);", meadow);
        Assert.DoesNotContain("GetComponent<", meadow); // the view caches its drawers; an upload looks none up

        string streaming = Script("GameBootstrap.ChunkStreaming.cs");
        int destroy = streaming.IndexOf("private void DestroyChunkView(", StringComparison.Ordinal);
        Assert.True(destroy > 0);
        Assert.Contains("ForgetDetail(view);", streaming.Substring(destroy, 500));
    }

    [Fact]
    public void TheChunkPipeline_AsksTheCoresRule_NotACountPerFrame()
    {
        string streaming = Script("GameBootstrap.ChunkStreaming.cs");
        string pipeline = Script("GameBootstrap.ChunkPipeline.cs");

        Assert.DoesNotContain("MeshChunksPerFrame", streaming);
        Assert.DoesNotContain("MaxChunkUploadsPerFrame", pipeline);
        Assert.Contains("ChunkBuildBudget.InFlight(System.Environment.ProcessorCount)", streaming);
        Assert.Contains("ChunkBuildBudget.MayDispatch(", pipeline);
        Assert.Contains("ChunkBuildBudget.MayUpload(", pipeline);
        Assert.Contains("ChunkBuildBudget.UploadMs(Time.unscaledDeltaTime * 1000d, ChunkLoadingBudget)", streaming);

        // The browser keeps its inline path: the rule is asked behind the worker-thread branch only.
        Assert.Contains("if (!InlineChunkWork)", pipeline);
        Assert.Contains("if (InlineChunkWork)", pipeline);
    }

    [Fact]
    public void TheFarView_SamplesOnTheWorkersTheRuleGives_AndRemembersItsColumnSurfaces()
    {
        string far = Script("FarTerrainView.cs");
        Assert.Contains("FarPatchWorkers.For(System.Environment.ProcessorCount, SystemInfo.systemMemorySize)", far);
        Assert.Contains("private void WorkerLoop(int slot)", far);
        Assert.Contains("int surface = ColumnSurface(src,", far); // #4052: the mask asks the generator once per column
    }
}
