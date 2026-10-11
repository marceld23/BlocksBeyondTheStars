// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.WorldHost;
using Microsoft.Data.Sqlite;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Hosting safety for saves across builds (#2464). A save reaches an older server build for real — a singleplayer
/// save uploaded while the fleet image lags behind, a fleet or store rollback — and the game server then refuses it
/// with exit code 3 (#2223). The WorldHost must say so: refuse a too-new upload up front, report "this world needs
/// a newer server" instead of a wake timeout, and never restart the same image on the same save in a loop.
/// </summary>
public sealed class WorldHostSaveVersionTests : IDisposable
{
    /// <summary>Signups in tests accept the default rules version 1.</summary>
    private const int Terms = 1;

    private readonly string _root;
    private readonly List<HostRegistry> _registries = new();

    public WorldHostSaveVersionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_wh_savever_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        foreach (var registry in _registries)
        {
            registry.Dispose();
        }

        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort (Windows may still hold a handle for a moment)
        }
    }

    /// <summary>Stand-in for Docker whose containers either boot (and "run" until stopped) or die right away with
    /// a chosen exit code — what a game server that refuses its save looks like to the control plane.</summary>
    private sealed class ExitingLauncher : IInstanceLauncher
    {
        public int StartCount;

        /// <summary>null = the next containers boot fine; a value = they exit at once with it.</summary>
        public int? DieWithExitCode;

        public readonly HashSet<string> Running = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> ExitCodes = new(StringComparer.Ordinal);

        public string Start(WorldRecord world)
        {
            StartCount++;
            string id = "container-" + StartCount;
            if (DieWithExitCode is { } code)
            {
                ExitCodes[id] = code;
            }
            else
            {
                Running.Add(id);
            }

            return id;
        }

        public void Stop(string containerId) => Running.Remove(containerId);

        public void Remove(string worldId)
        {
        }

        public bool IsRunning(string containerId) => containerId != null && Running.Contains(containerId);

        public int? ExitCode(string containerId)
            => !IsRunning(containerId) && ExitCodes.TryGetValue(containerId, out int code) ? code : null;

        public IReadOnlyList<ContainerStat> ContainerStats() => Array.Empty<ContainerStat>();
    }

    private HostRegistry NewRegistry(WorldHostConfig config)
    {
        var registry = new HostRegistry(config, Path.Combine(_root, Guid.NewGuid().ToString("N") + ".db"));
        _registries.Add(registry);
        return registry;
    }

    private static WorldOrchestrator NewOrchestrator(WorldHostConfig config, HostRegistry registry, ExitingLauncher launcher)
        => new(config, registry, launcher, w => Task.FromResult(launcher.IsRunning(w.ContainerId)));

    private static WorldHostConfig NewConfig(string image = "ghcr.io/example/bbs-server:2026.10.12")
        => new() { WakeTimeoutSeconds = 5, ServerImage = image };

    private static (AccountRecord Account, WorldRecord World) NewWorld(HostRegistry registry)
    {
        var (ok, error, accountId, session) = registry.CreateAccount("Owner", "super-secret-1", acceptedTermsVersion: Terms);
        Assert.True(ok, error);
        return (registry.ResolveSession(session)!, registry.CreateWorld(accountId, "My World").World!);
    }

    // ---------------- exit code 3: "this world needs a newer server" ----------------

    [Fact]
    public async Task Wake_RefusedSave_AnswersNeedsNewerServer_AndIsNotRestartedOnTheSameImageAsync()
    {
        var config = NewConfig();
        var registry = NewRegistry(config);
        var launcher = new ExitingLauncher { DieWithExitCode = WorldOrchestrator.SaveTooNewExitCode };
        var orchestrator = NewOrchestrator(config, registry, launcher);
        var (account, world) = NewWorld(registry);

        var (grant, error) = await orchestrator.JoinAsync(world.Id, account, "Pilot");

        Assert.Null(grant);
        Assert.Equal(WorldOrchestrator.SaveTooNewError, error); // not "did not come up in time"
        Assert.Equal(1, launcher.StartCount);
        var marked = registry.GetWorld(world.Id)!;
        Assert.Equal(WorldStatus.Stopped, marked.Status);
        Assert.Equal(config.ServerImage, marked.SaveTooNewImage);
        Assert.True(orchestrator.NeedsNewerServer(marked));
        Assert.True(orchestrator.WakeBlockedBySaveTooNew(marked));

        // The next joins — and an arcade keep-awake pass would be the same call — answer at once, without a start.
        Assert.Equal(WorldOrchestrator.SaveTooNewError, (await orchestrator.JoinAsync(world.Id, account, "Pilot")).Error);
        Assert.Equal(WorldOrchestrator.SaveTooNewError, (await orchestrator.EnsureRunningAsync(world.Id)).Error);
        Assert.Equal(1, launcher.StartCount);

        // The operator hears about it once per refusal (the reaper logs the drained list).
        Assert.Equal(new[] { (world.Id, config.ServerImage) }, orchestrator.DrainSaveTooNewWorlds());
        Assert.Empty(orchestrator.DrainSaveTooNewWorlds());
    }

    [Fact]
    public async Task Wake_OtherExitCode_StaysAGenericWakeFailure_AndIsRetriedAsync()
    {
        var config = NewConfig();
        var registry = NewRegistry(config);
        var launcher = new ExitingLauncher { DieWithExitCode = 1 };
        var orchestrator = NewOrchestrator(config, registry, launcher);
        var (account, world) = NewWorld(registry);

        Assert.Equal("The world did not come up in time — please try again.", (await orchestrator.JoinAsync(world.Id, account, "Pilot")).Error);
        Assert.Equal(string.Empty, registry.GetWorld(world.Id)!.SaveTooNewImage);
        Assert.Empty(orchestrator.DrainSaveTooNewWorlds());

        await orchestrator.JoinAsync(world.Id, account, "Pilot");
        Assert.Equal(2, launcher.StartCount); // a crash is worth another try; a refused save is not
    }

    [Fact]
    public async Task RefusedWorld_OpensOnANewerImage_AndTheMarkIsClearedAsync()
    {
        var oldImage = NewConfig("ghcr.io/example/bbs-server:2026.10.11");
        var registry = NewRegistry(oldImage);
        var launcher = new ExitingLauncher { DieWithExitCode = WorldOrchestrator.SaveTooNewExitCode };
        var (account, world) = NewWorld(registry);
        Assert.Null((await NewOrchestrator(oldImage, registry, launcher).JoinAsync(world.Id, account, "Pilot")).Grant);

        // The fleet rolls to a newer image (a WorldHost restart with a new BBS_WH_SERVER_IMAGE): the old mark no
        // longer applies — the world is shown as normal and the next join tries at once.
        var newImage = NewConfig("ghcr.io/example/bbs-server:2026.10.12");
        var updated = NewOrchestrator(newImage, registry, launcher);
        Assert.False(updated.NeedsNewerServer(registry.GetWorld(world.Id)!));
        launcher.DieWithExitCode = null;

        var (grant, error) = await updated.JoinAsync(world.Id, account, "Pilot");

        Assert.NotNull(grant);
        Assert.Equal(string.Empty, error);
        Assert.Equal(2, launcher.StartCount);
        Assert.Equal(WorldStatus.Running, registry.GetWorld(world.Id)!.Status);
        Assert.Equal(string.Empty, registry.GetWorld(world.Id)!.SaveTooNewImage);
    }

    [Fact]
    public async Task RefusedWorld_IsTriedAgain_OnTheOperatorsWake_AfterAnUpload_AndOnceTheRecheckWindowPassedAsync()
    {
        var config = NewConfig();
        var registry = NewRegistry(config);
        var launcher = new ExitingLauncher { DieWithExitCode = WorldOrchestrator.SaveTooNewExitCode };
        var orchestrator = NewOrchestrator(config, registry, launcher);
        var (account, world) = NewWorld(registry);
        await orchestrator.JoinAsync(world.Id, account, "Pilot");
        Assert.Equal(1, launcher.StartCount);

        // The admin page's wake button: a deliberate fresh try (e.g. after re-pulling a floating image tag).
        Assert.Equal(WorldOrchestrator.SaveTooNewError, (await orchestrator.EnsureRunningAsync(world.Id, retryRefusedSave: true)).Error);
        Assert.Equal(2, launcher.StartCount);
        Assert.True(orchestrator.NeedsNewerServer(registry.GetWorld(world.Id)!)); // refused again → marked again

        // An uploaded save is a different save (the upload endpoint clears the mark).
        registry.ClearSaveTooNew(world.Id);
        await orchestrator.JoinAsync(world.Id, account, "Pilot");
        Assert.Equal(3, launcher.StartCount);

        // A mark older than the recheck window lets one join try again by itself (a floating tag may now be newer).
        long stale = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - WorldOrchestrator.SaveTooNewRecheckSeconds - 5;
        registry.MarkSaveTooNew(world.Id, config.ServerImage, stale);
        Assert.False(orchestrator.WakeBlockedBySaveTooNew(registry.GetWorld(world.Id)!));
        Assert.True(orchestrator.NeedsNewerServer(registry.GetWorld(world.Id)!)); // still shown until it opens
        launcher.DieWithExitCode = null;
        Assert.NotNull((await orchestrator.JoinAsync(world.Id, account, "Pilot")).Grant);
        Assert.Equal(4, launcher.StartCount);
        Assert.Equal(string.Empty, registry.GetWorld(world.Id)!.SaveTooNewImage);
    }

    [Fact]
    public void Registry_KeepsTheMark_AcrossAReopen_AndInTheFleetList()
    {
        var config = NewConfig();
        string path = Path.Combine(_root, "registry.db");
        string worldId;
        using (var registry = new HostRegistry(config, path))
        {
            var (_, world) = NewWorld(registry);
            worldId = world.Id;
            Assert.Equal(string.Empty, world.SaveTooNewImage);
            registry.MarkSaveTooNew(worldId, config.ServerImage, 1_790_000_000);
        }

        var reopened = new HostRegistry(config, path); // the column migration is idempotent
        _registries.Add(reopened);
        var row = reopened.GetWorld(worldId)!;
        Assert.Equal((config.ServerImage, 1_790_000_000L), (row.SaveTooNewImage, row.SaveTooNewUnix));
        Assert.True(row.NeedsNewerServerThan(config.ServerImage));
        Assert.False(row.NeedsNewerServerThan("ghcr.io/example/bbs-server:2026.11.1"));
        Assert.Equal(config.ServerImage, Assert.Single(reopened.ListAllWorldsAdmin()).World.SaveTooNewImage);

        reopened.ClearSaveTooNew(worldId);
        Assert.Equal((string.Empty, 0L), (reopened.GetWorld(worldId)!.SaveTooNewImage, reopened.GetWorld(worldId)!.SaveTooNewUnix));
    }

    [Theory]
    [InlineData("false 3\n", 3)]
    [InlineData("false 0", 0)]
    [InlineData("false 137", 137)]
    [InlineData("true 0", null)] // still running: no exit code yet
    [InlineData("", null)]
    [InlineData("Error: No such object", null)]
    public void ParseInspectExitCode_ReadsOnlyAStoppedContainersCode(string output, int? expected)
        => Assert.Equal(expected, DockerCliLauncher.ParseInspectExitCode(output));

    [Fact]
    public void ExitCode_IsTheGameServersSaveTooNewCode()
    {
        // WorldHost does not reference the game server; the mirrored number must not drift.
        Assert.Equal(SvGameServer.SaveTooNewExitCode, WorldOrchestrator.SaveTooNewExitCode);
    }

    // ---------------- the upload check ----------------

    /// <summary>A real world save as the game writes it, with the given stored save version (null = the metadata
    /// carries no version at all, like every save from before the check; no metadata row when
    /// <paramref name="withMetadata"/> is false).</summary>
    private string RealSave(string name, int? saveVersion, bool withMetadata = true)
    {
        var paths = new SaveGamePaths(Path.Combine(_root, "saves"), name);
        using (var repo = new SqliteWorldRepository(paths))
        {
            repo.Initialize();
            if (withMetadata)
            {
                repo.SaveMetadata(new WorldMetadata { WorldName = name, Seed = 7, SaveVersion = saveVersion ?? 1 });
            }
        }

        if (withMetadata && saveVersion is null)
        {
            using var connection = new SqliteConnection($"Data Source={paths.DatabaseFile};Pooling=False");
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE world_meta SET json = json_remove(json, '$.SaveVersion');";
            Assert.Equal(1, cmd.ExecuteNonQuery());
        }

        SqliteConnection.ClearAllPools();
        return paths.DatabaseFile;
    }

    [Fact]
    public void Upload_FromANewerBuild_IsRefusedWithAFriendlyMessage()
    {
        var result = SavePaths.ValidateUploadedSave(RealSave("tomorrow", WorldMetadata.CurrentSaveVersion + 1));

        Assert.False(result.Ok);
        Assert.Equal(SavePaths.SaveTooNewError, result.Error);
        Assert.Contains("newer version of the game", result.Error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(WorldMetadata.CurrentSaveVersion)]
    [InlineData(null)]
    public void Upload_OfTheCurrentOrAnOlderBuild_IsAccepted(int? saveVersion)
    {
        // Older saves stay welcome: the game server copies them and stamps them on their first open.
        var result = SavePaths.ValidateUploadedSave(RealSave("older_" + (saveVersion?.ToString() ?? "none"), saveVersion));
        Assert.True(result.Ok, result.Error);
    }

    [Fact]
    public void Upload_WithoutAMetadataRow_IsAccepted()
    {
        var result = SavePaths.ValidateUploadedSave(RealSave("bare", saveVersion: null, withMetadata: false));
        Assert.True(result.Ok, result.Error);
    }

    [Fact]
    public void Upload_CheckFollowsTheSupportedVersionItIsGiven()
    {
        string save = RealSave("v3", 3);
        Assert.True(SavePaths.ValidateUploadedSave(save, supportedSaveVersion: 3).Ok);
        Assert.Equal(SavePaths.SaveTooNewError, SavePaths.ValidateUploadedSave(save, supportedSaveVersion: 2).Error);
    }

    [Fact]
    public void Upload_WithAForeignWorldMetaTable_OrBrokenMetadata_IsRefused()
    {
        // A world_meta table of another shape is not one of our saves.
        string foreign = Path.Combine(_root, "foreign_meta.db");
        using (var db = new SqliteConnection($"Data Source={foreign};Pooling=False"))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "CREATE TABLE world_meta(key TEXT PRIMARY KEY, value TEXT)";
            cmd.ExecuteNonQuery();
        }

        Assert.Equal("This database is not a Blocks Beyond the Stars world save.", SavePaths.ValidateUploadedSave(foreign).Error);

        // Metadata the game could not parse either.
        string broken = RealSave("broken", 1);
        using (var db = new SqliteConnection($"Data Source={broken};Pooling=False"))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE world_meta SET json = '{not json';";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        Assert.Equal("The save file could not be read.", SavePaths.ValidateUploadedSave(broken).Error);
    }

    [Theory]
    [InlineData("{\"WorldName\":\"x\",\"SaveVersion\":7}", 7)]
    [InlineData("{\"WorldName\":\"x\"}", 1)]           // absent ⇒ 1, as the game reads it
    [InlineData("{\"saveVersion\":9}", 1)]             // the repository writes PascalCase; other casing is not the field
    [InlineData("{\"SaveVersion\":\"3\"}", 1)]
    [InlineData("[]", 1)]
    public void StoredSaveVersion_ReadsTheMetadataLikeTheGame(string json, int expected)
        => Assert.Equal(expected, SavePaths.StoredSaveVersion(json));

    // ---------------- what the portal shows ----------------

    [Theory]
    [InlineData("en", "needs a server update", "This world was saved by a newer version of the game")]
    [InlineData("de", "braucht ein Server-Update", "Diese Welt wurde mit einer neueren Version des Spiels gespeichert")]
    public void WorldsPage_CarriesTheChip_AndBothNewErrors(string lang, string chip, string wakeError)
    {
        string html = WorldHostPortalPages.Worlds(new WorldHostConfig(), lang);

        Assert.Contains(chip, html, StringComparison.Ordinal);
        Assert.Contains("needsNewerServer", html, StringComparison.Ordinal);
        Assert.Contains("\"world_needs_newer_server\":\"" + wakeError, html, StringComparison.Ordinal);
        Assert.Contains("\"save_too_new\":", html, StringComparison.Ordinal);
    }
}
