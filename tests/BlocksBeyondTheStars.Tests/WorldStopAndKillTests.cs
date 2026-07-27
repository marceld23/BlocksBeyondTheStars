// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.WorldHost;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Stopping and killing an instance (issue #519). `docker stop` waits out the world's drain + save — up to
/// the container's 180 s stop-timeout — so it must never run on a request thread: the admin UI used to hang
/// for two minutes and the browser gave up before the redirect, which read as "stop does nothing". The
/// emergency hard kill is the opposite: instant, and it costs everything since the last autosave.
/// </summary>
public sealed class WorldStopAndKillTests : IDisposable
{
    private readonly string _root;
    private readonly List<HostRegistry> _registries = new();
    private readonly List<GatedLauncher> _launchers = new();

    public WorldStopAndKillTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_stop_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    /// <summary>Launcher whose <see cref="Stop"/> blocks until the test releases it — stands in for the
    /// real one sitting inside a container's drain. <see cref="Kill"/> deliberately does NOT fall back to
    /// Stop here, so a test can tell the two apart.</summary>
    private sealed class GatedLauncher : IInstanceLauncher, IDisposable
    {
        private readonly SemaphoreSlim _gate = new(0, 1);
        private readonly ManualResetEventSlim _stopped = new(false);

        public string? StoppedContainerId;
        public string? KilledContainerId;

        public string Start(WorldRecord world) => "container-1";

        public void Stop(string containerId)
        {
            // Bounded, so a failing test cannot leave a thread parked forever.
            _gate.Wait(TimeSpan.FromSeconds(30));
            StoppedContainerId = containerId;
            _stopped.Set();
        }

        /// <summary>Blocks the CALLING thread until the drain has run. Deliberately not an awaited task: a
        /// continuation would need a thread-pool slot, and under a loaded CI agent this test then measures
        /// the pool's queue rather than the code under test (it once took 123 s that way).</summary>
        public bool WaitUntilStopped(TimeSpan timeout) => _stopped.Wait(timeout);

        public void Kill(string containerId) => KilledContainerId = containerId;

        public void Remove(string worldId)
        {
        }

        public bool IsRunning(string containerId) => true;

        public IReadOnlyList<ContainerStat> ContainerStats() => Array.Empty<ContainerStat>();

        public void ReleaseStop() => _gate.Release();

        public void Dispose()
        {
            _gate.Dispose();
            _stopped.Dispose();
        }
    }

    private (HostRegistry Registry, WorldOrchestrator Orchestrator, GatedLauncher Launcher, WorldRecord World) NewRunningWorld()
    {
        var config = new WorldHostConfig();
        var registry = new HostRegistry(config, Path.Combine(_root, Guid.NewGuid().ToString("N") + ".db"));
        _registries.Add(registry);

        var (ok, error, accountId, _) = registry.CreateAccount("Owner", "super-secret-1", acceptedTermsVersion: 1);
        Assert.True(ok, error);

        var created = registry.CreateWorld(accountId!, "My World").World!;
        registry.SetWorldStatus(created.Id, WorldStatus.Running, "container-1");

        var launcher = new GatedLauncher();
        _launchers.Add(launcher);
        var orchestrator = new WorldOrchestrator(config, registry, launcher,
            w => Task.FromResult(launcher.IsRunning(w.ContainerId)));

        return (registry, orchestrator, launcher, registry.GetWorld(created.Id)!);
    }

    [Fact]
    public void StopWorldInBackground_ReturnsBeforeDockerDoes_AndReadsStoppedAtOnce()
    {
        var (registry, orchestrator, launcher, world) = NewRunningWorld();

        orchestrator.StopWorldInBackground(world);

        // The container is still draining (the launcher is blocked), yet the caller is already free and the
        // next page render tells the operator the truth instead of showing the world as running.
        Assert.False(orchestrator.BackgroundStopForTest!.IsCompleted);
        Assert.Equal(WorldStatus.Stopped, registry.GetWorld(world.Id)!.Status);

        launcher.ReleaseStop();
        Assert.True(launcher.WaitUntilStopped(TimeSpan.FromSeconds(30)), "the background drain never ran");

        // Stopped by the id the world had BEFORE the registry row was cleared — the container must not
        // outlive the row that pointed at it.
        Assert.Equal("container-1", launcher.StoppedContainerId);
        Assert.Null(launcher.KilledContainerId);
        Assert.Equal(string.Empty, registry.GetWorld(world.Id)!.ContainerId);
    }

    [Fact]
    public void KillWorld_SkipsTheDrainEntirely()
    {
        var (registry, orchestrator, launcher, world) = NewRunningWorld();

        // No release needed anywhere: a hard kill must never touch the blocking stop path.
        orchestrator.KillWorld(world);

        Assert.Equal("container-1", launcher.KilledContainerId);
        Assert.Null(launcher.StoppedContainerId);
        Assert.Equal(WorldStatus.Stopped, registry.GetWorld(world.Id)!.Status);
    }

    public void Dispose()
    {
        foreach (var registry in _registries)
        {
            registry.Dispose();
        }

        foreach (var launcher in _launchers)
        {
            launcher.Dispose();
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // a leftover temp db on a locked file system is not worth failing a test over
        }
    }
}
