// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Wires the REAL Unity-free <see cref="NetworkClient"/> to the REAL in-process
/// <see cref="SvGameServer"/> through a single <see cref="LoopbackLink"/> — the exact same
/// authoritative server + client code the shipping game runs, minus Unity and sockets. Pump the
/// world with <see cref="Tick"/> / <see cref="PumpUntil"/>; the client's authoritative state is
/// captured into the public fields as the server reports it.
/// </summary>
public sealed class ClientServerHarness : IDisposable
{
    private readonly string _root;
    private readonly SqliteWorldRepository _repo;
    private readonly LoopbackLink _link;

    public SvGameServer Server { get; }
    public NetworkClient Client { get; }

    // --- Captured authoritative state (what the client actually received) ---
    public JoinAccepted? JoinAccepted { get; private set; }
    public JoinRejected? JoinRejected { get; private set; }
    public InventoryUpdate? LastInventory { get; private set; }
    public CraftResult? LastCraftResult { get; private set; }
    public readonly List<BlockChanged> BlockChanges = new();
    public readonly List<ActionRejected> Rejections = new();
    public readonly Dictionary<(int, int, int), ChunkDataMessage> Chunks = new();

    /// <summary>The client-side world view, fed from the captured chunk + block-change messages.</summary>
    public ClientWorld World { get; } = new();

    public ClientServerHarness(GameContent content, Action<ServerConfig>? configure = null)
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_client_" + Guid.NewGuid().ToString("N"));

        var config = new ServerConfig
        {
            WorldName = "client_it",
            Seed = 123456,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999, // never auto-save during the test
            ViewDistanceChunks = 1,
            MaxPlayers = 4,
            PlaceStarterShip = false, // bare terrain at spawn, like the server integration tests
        };
        configure?.Invoke(config);

        _repo = new SqliteWorldRepository(new SaveGamePaths(_root, config.WorldName));
        _link = new LoopbackLink();
        Server = new SvGameServer(config, content, new LoopbackServerTransport(_link), _repo);
        Server.Start();

        Client = new NetworkClient(new LoopbackClientTransport(_link));
        WireCapture();
    }

    private void WireCapture()
    {
        Client.JoinAccepted += m => JoinAccepted = m;
        Client.JoinRejected += m => JoinRejected = m;
        Client.InventoryUpdated += m => LastInventory = m;
        Client.CraftCompleted += m => LastCraftResult = m;
        Client.ActionRejected += m => Rejections.Add(m);
        Client.BlockChanged += m =>
        {
            BlockChanges.Add(m);
            World.ApplyBlockChange(m.X, m.Y, m.Z, m.Block, m.Tint, m.Glow, out _);
        };
        Client.WorldEnvironmentReceived += m => World.SetCircumference(m.Circumference);
        Client.ChunkReceived += m =>
        {
            Chunks[(m.Cx, m.Cy, m.Cz)] = m;
            World.StoreChunk(
                new Shared.World.ChunkCoord(m.Cx, m.Cy, m.Cz),
                m.Blocks, m.ModIndex, m.ModTint, m.ModGlow, m.ShapeIndex, m.ShapeData);
        };
    }

    /// <summary>Connects + joins, then pumps until the server accepts the join (or a tick budget runs out).</summary>
    public void Join(string playerName = "Tester", int maxTicks = 20)
    {
        Client.Connect("loopback", 0);
        Client.Join(playerName);
        PumpUntil(() => JoinAccepted != null || JoinRejected != null, maxTicks);
    }

    /// <summary>Advances the server one tick and drains the client's inbox once.</summary>
    public void Tick(double dt = 0.1)
    {
        Server.Tick(dt);
        Client.Poll();
    }

    /// <summary>Pumps tick/poll cycles until <paramref name="condition"/> holds or the budget is spent.
    /// Returns whether the condition was met.</summary>
    public bool PumpUntil(Func<bool> condition, int maxTicks = 50, double dt = 0.1)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            if (condition())
            {
                return true;
            }

            Tick(dt);
        }

        return condition();
    }

    public void Dispose()
    {
        try { Client.Dispose(); } catch { /* ignore */ }
        try { Server.Stop(); } catch { /* ignore */ }
        try { _repo.Dispose(); } catch { /* ignore */ }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // ignore Windows file-lock cleanup races
        }
    }
}
