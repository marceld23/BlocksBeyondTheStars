// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The platform-scaling package (#3579–#3581): the bundled server's worker count follows the machine, the browser releases
/// long decoded clips after a move to another place, and the client unloads chunks without a GameObject and the old world's
/// block arrays. The rules are tested directly; the Unity half is guarded as source text (the Unity scripts cannot load
/// headless — the wiring-test pattern).
/// </summary>
public sealed class PlatformScalingTests
{
    private const int Gb = 1024;

    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    // ------------------------------------------------------------------ #3581 workers

    [Theory]
    [InlineData(2, 2)]
    [InlineData(4, 2)]
    [InlineData(8, 2)]
    [InlineData(10, 3)]
    [InlineData(12, 4)]
    [InlineData(16, 4)]
    [InlineData(32, 4)]
    [InlineData(128, 4)]
    public void Workers_FollowTheProcessors_BetweenTheDefaultAndTheCap(int logical, int expected)
        => Assert.Equal(expected, BundledServerWorkers.For(logical, 32 * Gb));

    [Theory]
    [InlineData(0)]          // unknown
    [InlineData(-1)]
    [InlineData(8 * Gb)]
    [InlineData(12 * Gb - 1)]
    public void Workers_StayAtTheDefault_WithLittleOrUnknownMemory(int memoryMb)
        => Assert.Equal(BundledServerWorkers.Minimum, BundledServerWorkers.For(32, memoryMb));

    [Fact]
    public void Workers_NeverGoBelowTheServerDefault_NorAboveTheCap()
    {
        for (int logical = 0; logical <= 256; logical++)
        {
            int w = BundledServerWorkers.For(logical, 64 * Gb);
            Assert.InRange(w, BundledServerWorkers.Minimum, BundledServerWorkers.Maximum);
        }

        // The minimum IS the server's default — the bundled server never runs fewer workers than a dedicated one.
        Assert.Equal(BundledServerWorkers.Minimum, new BlocksBeyondTheStars.Shared.Configuration.ServerConfig().ChunkGenWorkers);
    }

    [Fact]
    public void WorkersArgument_IsTheServersOption_AndYieldsToThePlayersOwnChoice()
    {
        Assert.Equal(" --chunk-gen-workers 4", BundledServerWorkers.Argument(16, 32 * Gb, null));
        Assert.Equal(" --chunk-gen-workers 2", BundledServerWorkers.Argument(8, 32 * Gb, "  "));
        Assert.Equal(string.Empty, BundledServerWorkers.Argument(16, 32 * Gb, "6")); // the command line would override it

        // The option and the variable are the server's own names.
        var config = new BlocksBeyondTheStars.Shared.Configuration.ServerConfig();
        config.ApplyCommandLine(BundledServerWorkers.Argument(16, 32 * Gb, null).Trim().Split(' '));
        Assert.Equal(4, config.ChunkGenWorkers);
        string options = File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "src", "BlocksBeyondTheStars.Shared", "Configuration", "ServerConfigOptions.cs"));
        Assert.Contains($"\"{BundledServerWorkers.EnvironmentVariable}\"", options);
    }

    [Fact]
    public void Launcher_PassesTheWorkerCount_WithTheMachinesNumbers()
    {
        string src = Script("LocalServerLauncher.cs");
        Assert.Contains("BundledServerWorkers.Argument(SystemInfo.processorCount,", src);
        Assert.Contains("SystemInfo.systemMemorySize, Environment.GetEnvironmentVariable(", src);
        Assert.Contains("+ viewArg + workersArg +", src);
    }

    // ------------------------------------------------------------------ #3580 audio

    [Fact]
    public void PlaceKey_TheShipInteriorIsNoPlace_EverythingElseIs()
    {
        Assert.Null(AudioReleaseRules.PlaceKey(AudioReleaseRules.ShipInteriorType, "", ""));
        Assert.Equal("Sol|Terra|varied", AudioReleaseRules.PlaceKey("varied", "Terra", "Sol"));
        Assert.Equal("Sol|Hub|orbital_station", AudioReleaseRules.PlaceKey("orbital_station", "Hub", "Sol"));
        Assert.Equal("||", AudioReleaseRules.PlaceKey(null, null, null));

        // The server's interior type, spelled the same.
        string interior = File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "src", "BlocksBeyondTheStars.GameServer", "GameServerShipInterior.cs"));
        Assert.Contains($"ShipInteriorType = \"{AudioReleaseRules.ShipInteriorType}\"", interior);
    }

    [Fact]
    public void IsMove_OnlyAnotherPlaceCounts()
    {
        string terra = AudioReleaseRules.PlaceKey("varied", "Terra", "Sol")!;
        string mars = AudioReleaseRules.PlaceKey("desert", "Mars", "Sol")!;
        Assert.True(AudioReleaseRules.IsMove(null, terra));    // the first arrival of a session
        Assert.True(AudioReleaseRules.IsMove(terra, mars));
        Assert.False(AudioReleaseRules.IsMove(terra, terra));  // a respawn, or back out of the ship
        Assert.False(AudioReleaseRules.IsMove(terra, null));   // into the ship
        Assert.False(AudioReleaseRules.IsMove(null, null));
    }

    [Fact]
    public void Release_TakesOnlyLongDecodedClipsNoSourceHolds()
    {
        long bed = AudioReleaseRules.DecodedBytes(20 * 44100, 2);   // a 20 s stereo bed: ~6.7 MB
        long step = AudioReleaseRules.DecodedBytes(22050, 1);       // a half-second mono step: ~88 KB
        Assert.Equal(20L * 44100 * 2 * 4, bed);
        Assert.Equal(0L, AudioReleaseRules.DecodedBytes(-5, 2));
        Assert.Equal(400L, AudioReleaseRules.DecodedBytes(100, 0)); // a channel count of 0 reads as mono

        Assert.True(AudioReleaseRules.ShouldRelease(loaded: true, heldBySource: false, bed));
        Assert.False(AudioReleaseRules.ShouldRelease(loaded: true, heldBySource: true, bed));   // a loop at volume 0, the bed
        Assert.False(AudioReleaseRules.ShouldRelease(loaded: false, heldBySource: false, bed)); // nothing to release
        Assert.False(AudioReleaseRules.ShouldRelease(loaded: true, heldBySource: false, step)); // frequent short sounds stay
        Assert.True(AudioReleaseRules.ShouldRelease(loaded: true, heldBySource: false, step, minBytes: 0));
        Assert.True(AudioReleaseRules.ShouldRelease(true, false, AudioReleaseRules.MinDecodedBytes));
        Assert.False(AudioReleaseRules.ShouldRelease(true, false, AudioReleaseRules.MinDecodedBytes - 1));
    }

    [Fact]
    public void ClientAudio_ReleasesInTheBrowserOnly_AfterAMove_AndKeepsWhatSourcesHold()
    {
        string audio = Script("ClientAudio.cs");
        Assert.Contains("_bundled.Add(clip);", audio);
        Assert.Contains("n.WorldResetReceived += OnPlaceReset;", audio);
        Assert.Contains("Game.Network.WorldResetReceived -= OnPlaceReset;", audio);
        Assert.Contains("TickAudioRelease(Time.unscaledDeltaTime);", audio);

        string release = Script("ClientAudio.Release.cs");
        Assert.Contains("ReleasesDecodedClips => Application.platform == RuntimePlatform.WebGLPlayer", release);
        Assert.Contains("AudioReleaseRules.IsMove(_placeKey, next)", release);
        Assert.Contains("!Game.WorldReady", release);
        Assert.Contains("FindObjectsByType<AudioSource>(FindObjectsInactive.Include)", release);
        Assert.Contains("foreach (var clip in _bundled)", release); // never a code-created clip: it has nothing to reload from
        Assert.Contains("clip.UnloadAudioData()", release);
        Assert.Contains("_bed?.DropIdleCopies();", release);
        Assert.Contains("_night?.DropIdleCopies();", release);

        string bed = Script("MuffledBed.cs");
        int drop = bed.IndexOf("public void DropIdleCopies()", StringComparison.Ordinal);
        Assert.True(drop > 0);
        Assert.Contains("kv.Key != _dry.clip", bed.Substring(drop)); // the current bed keeps its copy
    }

    // ------------------------------------------------------------------ #3579 chunks

    [Fact]
    public void CollectChunkCoords_ListsEveryStoredChunk_Canonical_AndReplacesTheList()
    {
        var w = new ClientWorld();
        w.SetCircumference(512);
        var blocks = new ushort[WorldConstants.BlocksPerChunk];
        w.StoreChunk(new ChunkCoord(1, 2, 3), blocks);
        w.StoreChunk(new ChunkCoord(-1, 0, 0), (ushort[])blocks.Clone()); // west of the seam: stored canonical

        var into = new List<ChunkCoord> { new ChunkCoord(99, 99, 99) };
        w.CollectChunkCoords(into);
        Assert.Equal(2, into.Count);
        Assert.Contains(new ChunkCoord(1, 2, 3), into);
        Assert.Contains(WorldConstants.CanonicalChunk(new ChunkCoord(-1, 0, 0), 512), into);

        Assert.True(w.RemoveChunk(new ChunkCoord(1, 2, 3)));
        w.CollectChunkCoords(into);
        Assert.Single(into);
    }

    [Fact]
    public void ChunkUnload_ReachesTheChunksWithoutAGameObject_AndAWorldChangeRetiresTheArrays()
    {
        string streaming = Script("GameBootstrap.ChunkStreaming.cs");
        Assert.Contains("SweepViewlessChunks(pp, unloadSq);", streaming);

        int sweep = streaming.IndexOf("private void SweepViewlessChunks(", StringComparison.Ordinal);
        Assert.True(sweep > 0);
        string sweepBody = streaming.Substring(sweep, 1400);
        Assert.Contains("World.CollectChunkCoords(_viewlessScratch);", sweepBody);
        Assert.Contains("view?.Go != null", sweepBody);     // a chunk with a GameObject stays the reposition loop's
        Assert.Contains("UnloadChunk(coord);", sweepBody);

        int unload = streaming.IndexOf("private void UnloadChunk(", StringComparison.Ordinal);
        Assert.True(unload > 0);
        string unloadBody = streaming.Substring(unload, 1600);
        foreach (string step in new[]
                 {
                     "DestroyChunkView(view);", "_chunkObjects.Remove(coord);", "_colliderGen.Remove(coord);",
                     "_colliderAppliedGen.Remove(coord);", "_meshGen.Remove(coord);", "ForgetConnectivity(coord);",
                     "World.RemoveChunk(coord);", "RetireChunkArray(WorldConstants.CanonicalChunk(coord, Circumference));",
                 })
        {
            Assert.Contains(step, unloadBody);
        }

        Assert.Contains("private void RetireAllChunkArrays()", streaming);
        string boot = Script("GameBootstrap.cs");
        int clear = boot.IndexOf("World.Clear();", boot.IndexOf("private void OnWorldReset(", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(clear > 0);
        Assert.Contains("RetireAllChunkArrays();", boot.Substring(clear, 200));
    }
}
