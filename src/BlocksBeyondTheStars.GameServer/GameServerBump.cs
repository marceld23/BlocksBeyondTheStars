// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>One compact sample of a player's recent state, kept in a short rolling history so a
/// <c>/bump</c> snapshot can show "the situation just before" alongside the situation now.</summary>
public sealed class BumpSample
{
    public double T { get; set; } // server uptime seconds at this sample
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Health { get; set; }
    public float Oxygen { get; set; }
    public float Energy { get; set; }
    public float Hunger { get; set; }
    public bool Aboard { get; set; }
}

/// <summary>
/// The <c>/bump</c> debug command: a player types <c>/bump &lt;description&gt;</c> in chat (works without
/// a comm radio) and the server writes a detailed JSON snapshot of their situation — player state,
/// environment, surrounding blocks, nearby entities and a short rolling history (the situation before)
/// — to <c>&lt;world&gt;/bumps/</c> so a developer can reproduce and fix the reported bug.
/// </summary>
public sealed partial class GameServer
{
    private double _uptime;
    private int _bumpCount;

    /// <summary>Number of bump snapshots written this session (test hook).</summary>
    public int BumpsWritten => _bumpCount;

    private void SampleHistories(double dt)
    {
        _uptime += dt;
        foreach (var s in _sessions.Values)
        {
            if (!s.Joined)
            {
                continue;
            }

            s.SinceHistorySample += dt;
            if (s.SinceHistorySample >= 1.0)
            {
                s.SinceHistorySample = 0;
                var p = s.State;
                s.History.Add(new BumpSample
                {
                    T = Math.Round(_uptime, 1),
                    X = p.Position.X,
                    Y = p.Position.Y,
                    Z = p.Position.Z,
                    Health = p.Health,
                    Oxygen = p.Oxygen,
                    Energy = p.SuitEnergy,
                    Hunger = p.Hunger,
                    Aboard = p.AboardShip,
                });

                while (s.History.Count > 30)
                {
                    s.History.RemoveAt(0); // keep the last ~30 s
                }
            }
        }
    }

    /// <summary>The <c>/bump</c> bug report variant that carries an in-game screenshot (JPG bytes). Oversized
    /// images are dropped (anti-abuse) so a snapshot is still written, just without the picture.</summary>
    private void HandleBumpReport(PlayerSession session, BumpReport report)
    {
        // Rate-limit like chat so a snapshot (and its screenshot) can't be spammed per packet.
        int now = Environment.TickCount;
        if (now - session.LastChatTick < 700)
        {
            return;
        }

        session.LastChatTick = now;

        const int MaxImageBytes = 2 * 1024 * 1024; // 2 MB cap
        var image = report.Image;
        if (image != null && image.Length > MaxImageBytes)
        {
            _log.Info($"Bump screenshot from {session.State.Name} dropped ({image.Length} bytes > {MaxImageBytes}).");
            image = null;
        }

        HandleBump(session, report.Description ?? string.Empty, image);
    }

    private void HandleBump(PlayerSession session, string description, byte[]? image = null)
    {
        var p = session.State;
        var (systemName, planetName) = ActiveLocationNames();
        float r2 = 24f * 24f;
        bool inSpace = InSpace(p.PlayerId);

        // Player inventory + suit ration dispenser, so a report is self-contained.
        var inventory = ItemList(p.Inventory);
        var rations = ItemList(p.RationStore);

        // Planet surroundings + nearby entities are only meaningful on a surface/interior. While flying in
        // space the on-foot Position is stale, so we capture the space instance (ship flight pos + entities)
        // instead and leave the planet scans empty.
        var blocks = new List<object>();
        List<object> census = new();
        object? space = null;
        object creatures = new List<object>();
        object npcs = new List<object>();
        object others = new List<object>();
        object containers = new List<object>();

        if (inSpace && _playerInstance.TryGetValue(p.PlayerId, out var instanceId)
            && _spaceInstances.TryGetValue(instanceId, out var instance))
        {
            var pose = instance.PlayerPoses.TryGetValue(p.PlayerId, out var pp)
                ? pp
                : new SpacePlayerPose(instance.ShipPosition, p.Yaw, p.InEva);
            var sp = pose.Pos;
            space = new
            {
                instanceId = instance.Id,
                kind = instance.Kind,
                shipX = sp.X,
                shipY = sp.Y,
                shipZ = sp.Z,
                yaw = pose.Yaw,
                eva = pose.Eva,
                entities = instance.Entities
                    .OrderBy(e => e.Position.DistanceSquared(sp))
                    .Take(40)
                    .Select(e => new
                    {
                        e.Id,
                        kind = e.Kind.ToString(),
                        e.Name,
                        e.Hostile,
                        species = e.SpeciesId,
                        e.Hull,
                        e.HullMax,
                        dist = (float)Math.Sqrt(e.Position.DistanceSquared(sp)),
                        x = e.Position.X,
                        y = e.Position.Y,
                        z = e.Position.Z,
                    }).ToList(),
            };
        }
        else
        {
            // Detailed non-air blocks in a tight box...
            int px = (int)Math.Floor(p.Position.X), py = (int)Math.Floor(p.Position.Y), pz = (int)Math.Floor(p.Position.Z);
            for (int dx = -4; dx <= 4; dx++)
                for (int dy = -2; dy <= 4; dy++)
                    for (int dz = -4; dz <= 4; dz++)
                    {
                        var id = _world.GetBlock(new Vector3i(px + dx, py + dy, pz + dz));
                        if (id.IsAir)
                        {
                            continue;
                        }

                        blocks.Add(new { x = px + dx, y = py + dy, z = pz + dz, block = _content.BlockById(id)?.Key ?? id.Value.ToString() });
                    }

            // ...plus a wider, compact census (block-type → count) so terrain + voxel flora composition
            // (trees/leaves/planted crops) is captured without listing every cell. NB: small client-side
            // billboard undergrowth is procedural and not server voxel data, so it can't appear here.
            var tally = new Dictionary<string, int>();
            for (int dx = -12; dx <= 12; dx++)
                for (int dz = -12; dz <= 12; dz++)
                    for (int dy = -4; dy <= 8; dy++)
                    {
                        var id = _world.GetBlock(new Vector3i(px + dx, py + dy, pz + dz));
                        if (id.IsAir)
                        {
                            continue;
                        }

                        string key = _content.BlockById(id)?.Key ?? id.Value.ToString();
                        tally[key] = tally.TryGetValue(key, out var n) ? n + 1 : 1;
                    }

            census = tally.OrderByDescending(kv => kv.Value)
                .Select(kv => (object)new { block = kv.Key, count = kv.Value }).ToList();

            creatures = _creatures.Where(c => WrapDistSq(p.Position, c.Position) < r2)
                .Select(c => new { c.Id, kind = c.Kind.ToString(), species = c.SpeciesId, c.Hostile, x = c.Position.X, y = c.Position.Y, z = c.Position.Z }).ToList();
            npcs = _npcs.Where(npc => WrapDistSq(p.Position, npc.Pos) < r2)
                .Select(npc => new { npc.Id, npc.Role, npc.Theme, x = npc.Pos.X, y = npc.Pos.Y, z = npc.Pos.Z }).ToList();
            others = _sessions.Values.Where(o => o.Joined && o.ConnectionId != session.ConnectionId && WrapDistSq(p.Position, o.State.Position) < r2)
                .Select(o => new { o.State.Name, x = o.State.Position.X, y = o.State.Position.Y, z = o.State.Position.Z }).ToList();
            containers = _containers.Where(c => Dist2(p.Position, c.Position) < r2)
                .Select(c => new { c.Id, c.Kind, items = c.Items.Count, c.Position.X, c.Position.Y, c.Position.Z }).ToList();
        }

        try
        {
            // Dev (running inside the repo) → <repoRoot>/bugreports/server/; installed → <world>/bumps as before.
            string dir = BugReportPaths.Resolve(Path.Combine(_repo.WorldDirectory, "bumps"));
            Directory.CreateDirectory(dir);
            _bumpCount++;

            // The dev folder is shared across worlds + sessions and _bumpCount resets each launch, so tag the
            // stem with world + UTC timestamp to keep files unique.
            string stem = $"bump_{SanitizeFileStem(_meta.WorldName)}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{_bumpCount:D3}";
            bool hasImage = image != null && image.Length > 0;
            string? imageName = hasImage ? stem + ".jpg" : null;

            var snapshot = new
            {
                timestampUtc = DateTime.UtcNow.ToString("o"),
                uptime = Math.Round(_uptime, 1),
                description,
                screenshot = imageName, // null when the client could not capture one
                world = _meta.WorldName,
                location = new { system = systemName, planet = planetName, activeLocationId = _meta.ActiveLocationId, planetType = _meta.DefaultPlanetType },
                player = new
                {
                    p.PlayerId,
                    p.Name,
                    x = p.Position.X,
                    y = p.Position.Y,
                    z = p.Position.Z,
                    p.Yaw,
                    p.Pitch,
                    p.Health,
                    p.Oxygen,
                    energy = p.SuitEnergy,
                    p.Hunger,
                    aboardShip = p.AboardShip,
                    station = CurrentStationName(p.PlayerId),
                    currentLocation = p.CurrentLocationId,
                    inEva = p.InEva,
                    aboveAtmosphere = p.AboveAtmosphere,
                    inSpace,
                    role = p.Role.ToString(),
                    p.GodMode,
                    p.Fly,
                    p.Stealthed,
                    selectedHotbarSlot = p.SelectedHotbarSlot,
                    inventory,
                    rations,
                },
                environment = BuildEnvironment(p.Position), // the player's local biome weather
                ship = new { _ship.ShipType, _ship.Hull, hullMax = _shipHullMax, _ship.Shield, shieldMax = _shipShieldMax, modules = _ship.Modules },
                surroundings = blocks,
                surroundingsCensus = census, // wider block-type histogram (terrain + voxel flora); empty in space
                nearby = new { creatures, npcs, players = others, containers },
                space, // ship flight position + nearby space entities when flying; null on a surface/interior
                historyBefore = session.History,
            };

            string file = Path.Combine(dir, stem + ".json");
            File.WriteAllText(file, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            if (hasImage)
            {
                File.WriteAllBytes(Path.Combine(dir, imageName!), image!);
            }

            _log.Info($"Bump #{_bumpCount} captured for {p.Name}: \"{description}\"{(hasImage ? " (+screenshot)" : string.Empty)} -> {file}");
            Send(session, new ServerMessage { Text = $"Debug snapshot #{_bumpCount} saved: {Path.GetFileName(file)}" });
        }
        catch (Exception e)
        {
            _log.Info($"Bump capture failed: {e.Message}");
            Send(session, new ServerMessage { Text = "Bump failed: " + e.Message });
        }
    }

    /// <summary>Flattens an inventory's non-empty slots into a serialisable list (slot index + item + count).</summary>
    private static List<object> ItemList(BlocksBeyondTheStars.Shared.State.Inventory inv)
    {
        var list = new List<object>();
        var slots = inv.Slots;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] is { } s)
            {
                list.Add(new { slot = i, item = s.Item, count = s.Count });
            }
        }

        return list;
    }

    private static string SanitizeFileStem(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "world";
        }

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return name;
    }

    private float Dist2(Vector3f a, Vector3i b) => (float)WrapDistSq(a, b); // longitude-wrap aware
}
