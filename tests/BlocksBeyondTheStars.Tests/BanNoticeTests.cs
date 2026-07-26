// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.WorldHost;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Telling a player what happened to them (#496/#497): the notice inbox behind ban/unban and operator world
/// deletion, timeouts that end by themselves, the world owner's own ban list at the join grant, and the
/// kick that ends a session the moment a ban lands (a ban alone only ever decides the NEXT join).
/// </summary>
public sealed class BanNoticeTests : IDisposable
{
    private const int Terms = 1;

    private readonly string _root;
    private readonly List<HostRegistry> _registries = new();

    public BanNoticeTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_ban_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(_root);
    }

    private HostRegistry NewRegistry(WorldHostConfig? config = null)
    {
        var registry = new HostRegistry(
            config ?? new WorldHostConfig { TermsVersion = Terms },
            System.IO.Path.Combine(_root, Guid.NewGuid().ToString("N") + ".db"));
        _registries.Add(registry);
        return registry;
    }

    /// <summary>In-memory stand-in for Docker (mirrors WorldHostTests' fake): containers "run" until stopped.</summary>
    private sealed class FakeLauncher : IInstanceLauncher
    {
        private int _started;
        private readonly HashSet<string> _running = new(StringComparer.Ordinal);

        public string Start(WorldRecord world)
        {
            string id = "container-" + ++_started;
            _running.Add(id);
            return id;
        }

        public void Stop(string containerId) => _running.Remove(containerId);

        public void Remove(string worldId) { }

        public bool IsRunning(string containerId) => containerId != null && _running.Contains(containerId);

        public IReadOnlyList<ContainerStat> ContainerStats() => Array.Empty<ContainerStat>();
    }

    private static WorldOrchestrator NewOrchestrator(HostRegistry registry, FakeLauncher launcher, WorldHostConfig config)
        => new(config, registry, launcher, w => Task.FromResult(launcher.IsRunning(w.ContainerId)));

    private static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // ---------------- Bans leave a message behind ----------------

    [Fact]
    public void Ban_WritesANotice_CarryingReasonCodeAndEnd()
    {
        var registry = NewRegistry();
        var (_, _, accountId, session) = registry.CreateAccount("Pilot", "super-secret-1", acceptedTermsVersion: Terms);

        registry.SetBanned(accountId, banned: true, "no shouting in chat", "chat", days: 3);

        var account = registry.ResolveSession(session)!;
        Assert.True(account.IsBanned);
        Assert.True(account.BanExpires);
        Assert.Equal("chat", account.BanReasonCode);
        Assert.Equal("no shouting in chat", account.BanReason);
        Assert.InRange(account.BannedUntilUnix, NowUnix() + (3 * 86400) - 60, NowUnix() + (3 * 86400) + 60);

        var notice = Assert.Single(registry.ListNotices(accountId));
        Assert.Equal(NoticeRecord.KindBanned, notice.Kind);
        Assert.Equal("chat", notice.ReasonCode);
        Assert.Equal("no shouting in chat", notice.Reason);
        Assert.Equal(account.BannedUntilUnix, notice.UntilUnix);
    }

    [Fact]
    public void Unban_WritesItsOwnNotice_AndClearsTheState()
    {
        var registry = NewRegistry();
        var (_, _, accountId, session) = registry.CreateAccount("Pilot", "super-secret-1", acceptedTermsVersion: Terms);

        registry.SetBanned(accountId, banned: true, "griefing", "griefing", days: 0);
        registry.SetBanned(accountId, banned: false, string.Empty);

        Assert.False(registry.ResolveSession(session)!.IsBanned);
        Assert.Empty(registry.ListBannedAccounts());
        Assert.Equal(
            new[] { NoticeRecord.KindUnbanned, NoticeRecord.KindBanned },
            registry.ListNotices(accountId).Select(n => n.Kind).ToArray()); // newest first
    }

    [Fact]
    public void ExpiredTimeout_LiftsItself_WithoutAnOperator()
    {
        var registry = NewRegistry();
        var (_, _, accountId, session) = registry.CreateAccount("Pilot", "super-secret-1", acceptedTermsVersion: Terms);

        // A timeout whose end has passed: the row still says banned = 1, but nothing may act on it any
        // more — otherwise every "3 days out" would silently become permanent.
        registry.SetBannedUntil(accountId, banned: true, "cool off", "chat", NowUnix() - 60);

        Assert.False(registry.ResolveSession(session)!.IsBanned);
        Assert.False(registry.FindAccountByName("Pilot")!.IsBanned);
        Assert.Empty(registry.ListBannedAccounts());

        registry.SetBannedUntil(accountId, banned: true, "cool off", "chat", NowUnix() + 60);
        Assert.True(registry.ResolveSession(session)!.IsBanned);
        Assert.Single(registry.ListBannedAccounts());
    }

    [Fact]
    public void Notices_AreScopedToTheirAccount_AndAcknowledgedOnce()
    {
        var registry = NewRegistry();
        var (_, _, mine, _) = registry.CreateAccount("Pilot", "super-secret-1", acceptedTermsVersion: Terms);
        var (_, _, other, _) = registry.CreateAccount("Nosy", "super-secret-1", acceptedTermsVersion: Terms);

        registry.AddNotice(mine, NoticeRecord.KindWorldDeleted, "Justus-Basis", "server cleanup");
        long noticeId = registry.ListNotices(mine).Single().Id;

        Assert.Empty(registry.ListNotices(other));

        // Another account cannot acknowledge (and thereby hide) someone else's message.
        registry.MarkNoticesSeen(other, noticeId);
        Assert.Single(registry.ListNotices(mine));

        registry.MarkNoticesSeen(mine, noticeId);
        Assert.Empty(registry.ListNotices(mine));
        var read = Assert.Single(registry.ListNotices(mine, unseenOnly: false));
        Assert.Equal("Justus-Basis", read.Subject);
        Assert.Equal("server cleanup", read.Reason);
    }

    [Fact]
    public void DeletingAnAccount_TakesItsNoticesWithIt()
    {
        var registry = NewRegistry();
        var (_, _, accountId, _) = registry.CreateAccount("Pilot", "super-secret-1", acceptedTermsVersion: Terms);
        registry.AddNotice(accountId, NoticeRecord.KindWorldDeleted, "Gone", string.Empty);

        registry.DeleteAccount(accountId);

        Assert.Empty(registry.ListNotices(accountId, unseenOnly: false));
    }

    // ---------------- The world owner's own ban list ----------------

    [Fact]
    public async Task WorldBan_RefusesTheJoinGrant_AndNamesTheReasonAsync()
    {
        var config = new WorldHostConfig { TermsVersion = Terms, WorldsDir = System.IO.Path.Combine(_root, "worlds") };
        var registry = NewRegistry(config);
        var launcher = new FakeLauncher();
        var orchestrator = NewOrchestrator(registry, launcher, config);
        var (_, _, ownerId, _) = registry.CreateAccount("Owner", "super-secret-1", acceptedTermsVersion: Terms);
        var (_, _, guestId, guestSession) = registry.CreateAccount("Guest", "super-secret-1", acceptedTermsVersion: Terms);
        var guest = registry.ResolveSession(guestSession)!;
        var world = registry.CreateWorld(ownerId, "Justus-Basis").World!;

        var (granted, _) = await orchestrator.JoinAsync(world.Id, guest, "Guest");
        Assert.NotNull(granted); // baseline: an unblocked guest gets in

        Assert.True(registry.AddWorldBan(world.Id, guestId, "Guest", "kept blowing up the base"));
        var (blocked, error) = await orchestrator.JoinAsync(world.Id, guest, "Guest");

        Assert.Null(blocked);
        Assert.Contains("blocked you", error, StringComparison.Ordinal);
        Assert.Contains("kept blowing up the base", error, StringComparison.Ordinal);

        // Lifting it lets them back in — and the owner's other world was never affected.
        registry.RemoveWorldBan(world.Id, registry.ListWorldBans(world.Id).Single().Id);
        var (again, _) = await orchestrator.JoinAsync(world.Id, guest, "Guest");
        Assert.NotNull(again);
    }

    [Fact]
    public async Task WorldBan_StaysOnItsOwnWorld_AndCatchesARenamedReturnAsync()
    {
        var config = new WorldHostConfig { TermsVersion = Terms, WorldsDir = System.IO.Path.Combine(_root, "worlds") };
        var registry = NewRegistry(config);
        var launcher = new FakeLauncher();
        var orchestrator = NewOrchestrator(registry, launcher, config);
        var (_, _, ownerId, _) = registry.CreateAccount("Owner", "super-secret-1", acceptedTermsVersion: Terms);
        var (_, _, guestId, guestSession) = registry.CreateAccount("Guest", "super-secret-1", acceptedTermsVersion: Terms);
        var guest = registry.ResolveSession(guestSession)!;
        var blockedWorld = registry.CreateWorld(ownerId, "Justus-Basis").World!;
        var otherWorld = registry.CreateWorld(ownerId, "Zweite Welt").World!;

        registry.AddWorldBan(blockedWorld.Id, guestId, "Guest", string.Empty);

        // Same account under a fresh in-game name is still blocked (the ban keys on the account)…
        var (renamed, _) = await orchestrator.JoinAsync(blockedWorld.Id, guest, "Guest2");
        Assert.Null(renamed);

        // …while the owner's other world is untouched: this is a per-world lever, not a fleet ban.
        var (elsewhere, _) = await orchestrator.JoinAsync(otherWorld.Id, guest, "Guest");
        Assert.NotNull(elsewhere);
    }

    [Fact]
    public async Task JoinGrant_RecordsTheVisitor_SoTheOwnerCanPickAndTheFleetCanKickAsync()
    {
        var config = new WorldHostConfig { TermsVersion = Terms, WorldsDir = System.IO.Path.Combine(_root, "worlds") };
        var registry = NewRegistry(config);
        var launcher = new FakeLauncher();
        var orchestrator = NewOrchestrator(registry, launcher, config);
        var (_, _, ownerId, _) = registry.CreateAccount("Owner", "super-secret-1", acceptedTermsVersion: Terms);
        var (_, _, guestId, guestSession) = registry.CreateAccount("Guest", "super-secret-1", acceptedTermsVersion: Terms);
        var guest = registry.ResolveSession(guestSession)!;
        var world = registry.CreateWorld(ownerId, "Justus-Basis").World!;

        await orchestrator.JoinAsync(world.Id, guest, "Sternenfuchs");
        await orchestrator.JoinAsync(world.Id, guest, "Sternenfuchs"); // a second visit must not duplicate the row

        var visitor = Assert.Single(registry.ListWorldVisitors(world.Id));
        Assert.Equal("Sternenfuchs", visitor.PlayerName);
        Assert.Equal(guestId, visitor.AccountId);
        Assert.Equal((world.Id, "Sternenfuchs"), registry.ListVisitorNamesForAccount(guestId).Single());
    }

    [Fact]
    public void DeletingAWorld_TakesItsBansAndVisitorsWithIt()
    {
        var config = new WorldHostConfig { TermsVersion = Terms, WorldsDir = System.IO.Path.Combine(_root, "worlds") };
        var registry = NewRegistry(config);
        var (_, _, ownerId, _) = registry.CreateAccount("Owner", "super-secret-1", acceptedTermsVersion: Terms);
        var world = registry.CreateWorld(ownerId, "Doomed").World!;
        registry.AddWorldBan(world.Id, "acc-someone", "Troll", "griefing");
        registry.RecordWorldVisitor(world.Id, "acc-someone", "Troll");

        registry.DeleteWorld(world.Id);

        Assert.Empty(registry.ListWorldBans(world.Id));
        Assert.Empty(registry.ListWorldVisitors(world.Id));
    }

    [Fact]
    public void AddWorldBan_IsIdempotent_AndRefusesAnEmptyTarget()
    {
        var registry = NewRegistry();
        var (_, _, ownerId, _) = registry.CreateAccount("Owner", "super-secret-1", acceptedTermsVersion: Terms);
        var world = registry.CreateWorld(ownerId, "Justus-Basis").World!;

        Assert.True(registry.AddWorldBan(world.Id, "acc-1", "Troll", "griefing"));
        Assert.True(registry.AddWorldBan(world.Id, "acc-1", "Troll", "griefing")); // double click
        Assert.Single(registry.ListWorldBans(world.Id));

        Assert.False(registry.AddWorldBan(world.Id, string.Empty, string.Empty, "nothing to ban"));
        Assert.False(registry.AddWorldBan("not-a-world-id", "acc-1", "Troll", string.Empty));
    }

    // ---------------- The kick behind the ban ----------------

    [Fact]
    public void Kick_TellsThePlayerWhy_ThenClosesTheConnection()
    {
        string worldName = "kick_" + Guid.NewGuid().ToString("N");
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, worldName));
        var link = new LoopbackLink();
        var transport = new LoopbackServerTransport(link);
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var server = new SvGameServer(
            new ServerConfig { WorldName = worldName, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false },
            content, transport, repo);
        server.Start();

        var rejections = new List<JoinRejected>();
        bool disconnected = false;
        var client = new LoopbackClientTransport(link);
        client.PayloadReceived += payload =>
        {
            if (NetCodec.Decode(payload) is JoinRejected r)
            {
                rejections.Add(r);
            }
        };
        client.Disconnected += () => disconnected = true;

        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Troll" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
        Assert.Empty(rejections); // the join itself was fine

        Assert.False(server.EnqueueKick("  ", null));                      // no name, nothing queued
        Assert.True(server.EnqueueKick("Troll", "@ui.kick.banned"));

        server.Tick(0.1);
        client.Poll();
        var kick = Assert.Single(rejections);
        Assert.Equal("@ui.kick.banned", kick.Reason); // '@key' = the client shows it in the player's language

        // The pipe closes a moment later, so the message is out before the socket goes: a modified client
        // must not be able to ignore the rejection and keep playing.
        Assert.False(disconnected);
        Assert.NotEmpty(server.Sessions);
        server.Tick(1.5);  // arms the close…
        client.Poll();
        server.Tick(0.1);  // …and the transport surfaces it on the next poll
        Assert.True(disconnected);
        Assert.Empty(server.Sessions);

        server.Stop();
        repo.Dispose();
    }

    public void Dispose()
    {
        foreach (var registry in _registries)
        {
            registry.Dispose();
        }

        try
        {
            System.IO.Directory.Delete(_root, recursive: true);
        }
        catch (System.IO.IOException)
        {
            // best effort — a stray temp dir must never fail a test run
        }
    }
}
