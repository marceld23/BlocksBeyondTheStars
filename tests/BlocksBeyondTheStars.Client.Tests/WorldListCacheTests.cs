// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3259: the singleplayer world list is read off the main thread — one timestamp per world, the playtime sidecar,
/// newest first — and dropped when a world is created, played or deleted. Clicking "Singleplayer" used to read all of
/// it inside one frame (5.8 s on a cold file cache with 264 worlds). #3664: the sidecar also carries the world's mode
/// (explorer, creative, sandbox), read in the same single parse, for the picker to show.
/// </summary>
public sealed class WorldListCacheTests : IDisposable
{
    private static readonly DateTime Base = new(2026, 11, 6, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_worldlist_" + Guid.NewGuid().ToString("N"));

    public WorldListCacheTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    [Fact]
    public void Scan_ListsWorldFoldersNewestFirst_WithTheirPlaytime()
    {
        World("old", Base, playtime: 60);
        World("newest", Base.AddHours(2), playtime: 7200);
        World("middle", Base.AddHours(1));
        Directory.CreateDirectory(Path.Combine(_root, "not_a_world")); // no world.db

        var worlds = WorldListCache.Scan(_root);

        Assert.Equal(new[] { "newest", "middle", "old" }, worlds.Select(w => w.Name));
        Assert.Equal(7200, worlds[0].PlaytimeSeconds);
        Assert.Equal(-1, worlds[1].PlaytimeSeconds); // no sidecar: the row shows no figure
        Assert.Equal(60, worlds[2].PlaytimeSeconds);
        Assert.Equal(Base.AddHours(2), worlds[0].LastWriteUtc);
    }

    [Fact]
    public void Scan_OrdersEqualTimestampsByName_SoTwoScansAgree()
    {
        World("b", Base);
        World("A", Base);
        World("c", Base);

        Assert.Equal(new[] { "A", "b", "c" }, WorldListCache.Scan(_root).Select(w => w.Name));
    }

    [Fact]
    public void Scan_OfAMissingFolder_IsEmpty()
        => Assert.Empty(WorldListCache.Scan(Path.Combine(_root, "nowhere")));

    [Fact]
    public void Scan_WithoutPlaytime_SkipsTheSidecars()
    {
        World("w", Base, playtime: 99);
        File.WriteAllText(Path.Combine(_root, "w", WorldListCache.SidecarFile), "{\"PlaytimeSeconds\":99,\"Mode\":\"sandbox\"}");

        var world = WorldListCache.Scan(_root, withPlaytime: false)[0];
        Assert.Equal(-1, world.PlaytimeSeconds);
        Assert.Equal(string.Empty, world.Mode);
    }

    // ---------------- #3664: the world's mode in the sidecar ----------------

    [Fact]
    public void Scan_ReadsEachWorldsMode_AndAnOldSidecarHasNone()
    {
        Sidecar(World("explorer", Base.AddHours(3)), "{\"WorldName\":\"explorer\",\"PlaytimeSeconds\":10,\"LastPlayedUtc\":\"\",\"Mode\":\"explorer\"}");
        Sidecar(World("creative", Base.AddHours(2)), "{\"WorldName\":\"creative\",\"PlaytimeSeconds\":20,\"LastPlayedUtc\":\"\",\"Mode\":\"creative\"}");
        Sidecar(World("sandbox", Base.AddHours(1)), "{\"WorldName\":\"sandbox\",\"PlaytimeSeconds\":30,\"LastPlayedUtc\":\"\",\"Mode\":\"sandbox\"}");
        World("old", Base, playtime: 40); // a sidecar from before #3664: no Mode field
        World("bare", Base.AddMinutes(-5)); // no sidecar at all

        var worlds = WorldListCache.Scan(_root);

        Assert.Equal(new[] { "explorer", "creative", "sandbox", "old", "bare" }, worlds.Select(w => w.Name));
        Assert.Equal(new[] { "explorer", "creative", "sandbox", string.Empty, string.Empty }, worlds.Select(w => w.Mode));
        Assert.Equal(new[] { 10L, 20L, 30L, 40L, -1L }, worlds.Select(w => w.PlaytimeSeconds)); // the same single read
        Assert.Equal(
            new[] { "ui.save.mode_explorer", "ui.save.mode_creative", "ui.save.mode_sandbox", string.Empty, string.Empty },
            worlds.Select(w => w.ModeKey));
    }

    [Theory]
    [InlineData("{\"PlaytimeSeconds\":5,\"Mode\":\"creative\"}", 5, "creative")]
    [InlineData("{\"mode\":\"sandbox\",\"playtimeseconds\":6}", 6, "sandbox")] // read case-blind, in any order
    [InlineData("{\"PlaytimeSeconds\":7}", 7, "")] // an old sidecar
    [InlineData("{\"Mode\":3}", -1, "")] // not a string
    [InlineData("{ not json", -1, "")]
    [InlineData("[\"sandbox\"]", -1, "")]
    public void ReadSidecar_ReadsPlaytimeAndModeInOneParse_AndShrugsOffABrokenOne(string json, long seconds, string mode)
    {
        string dir = Sidecar(World("w", Base), json);

        var read = WorldListCache.ReadSidecar(dir);

        Assert.Equal(seconds, read.PlaytimeSeconds);
        Assert.Equal(mode, read.Mode);
        Assert.Equal(seconds, WorldListCache.ReadPlaytimeSeconds(dir));
    }

    [Fact]
    public void ReadSidecar_OfAFolderWithoutOne_IsNothing()
    {
        var read = WorldListCache.ReadSidecar(World("w", Base));

        Assert.Equal(-1, read.PlaytimeSeconds);
        Assert.Equal(string.Empty, read.Mode);
    }

    [Theory]
    [InlineData("explorer", "ui.save.mode_explorer")]
    [InlineData("creative", "ui.save.mode_creative")]
    [InlineData("sandbox", "ui.save.mode_sandbox")]
    [InlineData("SANDBOX", "ui.save.mode_sandbox")] // case-blind
    [InlineData("survival", "")] // unknown: the picker shows no mode rather than a raw key
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ModeKeyOf_NamesTheThreeModes_AndNothingElse(string? mode, string key)
    {
        Assert.Equal(key, SavedWorld.ModeKeyOf(mode));
        Assert.Equal(key, new SavedWorld("w", Base, -1, mode).ModeKey);
    }

    [Fact]
    public void TheOldConstructor_StillWorks_WithoutAMode()
    {
        var world = new SavedWorld("w", Base, 12);

        Assert.Equal(12, world.PlaytimeSeconds);
        Assert.Equal(string.Empty, world.Mode);
        Assert.Equal(string.Empty, world.ModeKey);
    }

    [Fact]
    public void TheClientsModeWords_AreTheServersSidecarWords()
    {
        Assert.Equal(WorldSaveSummary.ModeExplorer, SavedWorld.ModeExplorer);
        Assert.Equal(WorldSaveSummary.ModeCreative, SavedWorld.ModeCreative);
        Assert.Equal(WorldSaveSummary.ModeSandbox, SavedWorld.ModeSandbox);
    }

    [Theory]
    [InlineData(false, false, false, false, "explorer")]
    [InlineData(false, true, false, false, "creative")] // all blueprints
    [InlineData(false, false, true, false, "creative")] // all ships
    [InlineData(false, false, false, true, "creative")] // the starter kit
    [InlineData(true, false, false, false, "sandbox")]
    [InlineData(true, true, true, true, "sandbox")] // the rules in Creative beat the head-start options
    public void TheRepository_WritesTheWorldsMode_AndThePickerReadsIt(bool creativeRules, bool unlockAll, bool allShips, bool kit, string mode)
    {
        var meta = new WorldMetadata
        {
            WorldName = "moded",
            CreativeUnlockAllBlueprints = unlockAll,
            CreativeStartAllShips = allShips,
            CreativeStarterKit = kit,
            RulesOverride = creativeRules ? new GameRules { GameMode = GameMode.Creative } : null,
        };
        Assert.Equal(mode, WorldSaveSummary.ModeOf(meta));

        using (var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "moded")))
        {
            repo.Initialize();
            repo.SaveMetadata(meta);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); // let the temp folder go on Windows

        var world = Assert.Single(WorldListCache.Scan(_root));
        Assert.Equal(mode, world.Mode);
        Assert.Equal("ui.save.mode_" + mode, world.ModeKey);
    }

    [Fact]
    public void ARulesOverrideInSurvival_IsAnExplorerWorld()
        => Assert.Equal(WorldSaveSummary.ModeExplorer, WorldSaveSummary.ModeOf(new WorldMetadata { RulesOverride = new GameRules() }));

    private static string Sidecar(string dir, string json)
    {
        File.WriteAllText(Path.Combine(dir, WorldListCache.SidecarFile), json);
        return dir;
    }

    [Theory]
    [InlineData("{\"WorldName\":\"w\",\"PlaytimeSeconds\":42,\"LastPlayedUtc\":\"\"}", 42)]
    [InlineData("{\"playtimeSeconds\":7}", 7)] // read case-blind
    [InlineData("{\"WorldName\":\"w\"}", -1)]
    [InlineData("{ not json", -1)]
    [InlineData("[1,2]", -1)]
    [InlineData("{\"PlaytimeSeconds\":\"12\"}", -1)]
    public void ReadPlaytimeSeconds_ReadsTheSidecar_AndShrugsOffABrokenOne(string json, long expected)
    {
        string dir = World("w", Base);
        File.WriteAllText(Path.Combine(dir, WorldListCache.SidecarFile), json);

        Assert.Equal(expected, WorldListCache.ReadPlaytimeSeconds(dir));
    }

    [Fact]
    public void Scan_StillListsAWorldWhoseSidecarIsLocked()
    {
        string dir = World("locked", Base, playtime: 42);
        string sidecar = Path.Combine(dir, WorldListCache.SidecarFile);
        using (new FileStream(sidecar, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            bool lockHolds = !CanRead(sidecar); // Windows refuses the read; not every platform enforces the share mode

            var world = Assert.Single(WorldListCache.Scan(_root));

            Assert.Equal("locked", world.Name);
            Assert.Equal(lockHolds ? -1 : 42, world.PlaytimeSeconds);
        }
    }

    [Fact]
    public void Cache_ScansInTheBackground_AndAnswersFromMemoryAfterwards()
    {
        World("one", Base);
        int scans = 0;
        var cache = new WorldListCache(_root, root =>
        {
            Interlocked.Increment(ref scans);
            return WorldListCache.Scan(root);
        });

        cache.Prefetch();
        var worlds = WaitForList(cache);

        Assert.Equal("one", Assert.Single(worlds).Name);
        cache.Prefetch(); // the menu shows again: nothing changed, nothing is read again
        Assert.Same(worlds, cache.TryGet());
        Assert.Equal(1, Volatile.Read(ref scans));
    }

    [Fact]
    public void Cache_Invalidate_ScansAgain_AndSeesTheNewWorld()
    {
        World("one", Base);
        var cache = new WorldListCache(_root);
        WaitForList(cache);

        World("two", Base.AddMinutes(5));
        cache.Invalidate();

        Assert.False(cache.IsLoaded);
        Assert.Equal(new[] { "two", "one" }, WaitForList(cache).Select(w => w.Name));
    }

    [Fact]
    public void Cache_DropsAScanThatWasInvalidatedWhileItRan()
    {
        using var firstStarted = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        int call = 0;
        var cache = new WorldListCache(_root, _ =>
        {
            if (Interlocked.Increment(ref call) == 1)
            {
                firstStarted.Set();
                release.Wait(TimeSpan.FromSeconds(10)); // the first scan is still on the disk…
                return new[] { new SavedWorld("stale", Base, -1) };
            }

            return new[] { new SavedWorld("fresh", Base, -1) };
        });

        cache.Prefetch();
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(10)));
        cache.Invalidate(); // …when a world is deleted
        Assert.Null(cache.TryGet()); // starts the second scan
        var worlds = WaitForList(cache);
        release.Set();
        Thread.Sleep(100); // the stale scan finishes now — and must not overwrite the fresh list

        Assert.Equal("fresh", Assert.Single(worlds).Name);
        Assert.Equal("fresh", Assert.Single(cache.TryGet()!).Name);
    }

    [Fact]
    public void Cache_Forget_DropsADeletedWorldWithoutScanningAgain()
    {
        World("keep", Base.AddHours(1));
        World("gone", Base);
        int scans = 0;
        var cache = new WorldListCache(_root, root =>
        {
            Interlocked.Increment(ref scans);
            return WorldListCache.Scan(root);
        });
        WaitForList(cache);

        cache.Forget("GONE"); // the file system ignores case, so the picker does too

        Assert.Equal("keep", Assert.Single(cache.TryGet()!).Name);
        Assert.Equal(1, Volatile.Read(ref scans));
    }

    [Fact]
    public void Cache_AScanThatThrows_ListsNothing_InsteadOfHanging()
    {
        var cache = new WorldListCache(_root, _ => throw new UnauthorizedAccessException("no"));

        Assert.Empty(WaitForList(cache));
    }

    private string World(string name, DateTime lastWriteUtc, long playtime = -1)
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        string db = Path.Combine(dir, WorldListCache.WorldFile);
        File.WriteAllText(db, "sqlite");
        File.SetLastWriteTimeUtc(db, lastWriteUtc);
        if (playtime >= 0)
        {
            File.WriteAllText(Path.Combine(dir, WorldListCache.SidecarFile),
                "{\"WorldName\":\"" + name + "\",\"PlaytimeSeconds\":" + playtime + ",\"LastPlayedUtc\":\"\"}");
        }

        return dir;
    }

    private static IReadOnlyList<SavedWorld> WaitForList(WorldListCache cache)
    {
        IReadOnlyList<SavedWorld>? worlds = null;
        Assert.True(SpinWait.SpinUntil(() => (worlds = cache.TryGet()) != null, TimeSpan.FromSeconds(10)), "the background scan never finished");
        return worlds!;
    }

    private static bool CanRead(string path)
    {
        try
        {
            File.ReadAllText(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
