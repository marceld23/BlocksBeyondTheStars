using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Spacecraft.Networking.Messages;
using Spacecraft.Shared.Geometry;

namespace Spacecraft.GameServer;

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
                    X = p.Position.X, Y = p.Position.Y, Z = p.Position.Z,
                    Health = p.Health, Oxygen = p.Oxygen, Energy = p.SuitEnergy, Hunger = p.Hunger,
                    Aboard = p.AboardShip,
                });

                while (s.History.Count > 30)
                {
                    s.History.RemoveAt(0); // keep the last ~30 s
                }
            }
        }
    }

    private void HandleBump(PlayerSession session, string description)
    {
        var p = session.State;
        var (systemName, planetName) = ActiveLocationNames();
        float r2 = 24f * 24f;

        // Surrounding non-air blocks in a box around the player.
        var blocks = new List<object>();
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

        var creatures = _creatures.Where(c => p.Position.DistanceSquared(c.Position) < r2)
            .Select(c => new { c.Id, kind = c.Kind.ToString(), species = c.SpeciesId, c.Hostile, x = c.Position.X, y = c.Position.Y, z = c.Position.Z }).ToList();
        var npcs = _npcs.Where(npc => p.Position.DistanceSquared(npc.Pos) < r2)
            .Select(npc => new { npc.Id, npc.Role, npc.Theme, x = npc.Pos.X, y = npc.Pos.Y, z = npc.Pos.Z }).ToList();
        var others = _sessions.Values.Where(o => o.Joined && o.ConnectionId != session.ConnectionId && p.Position.DistanceSquared(o.State.Position) < r2)
            .Select(o => new { o.State.Name, x = o.State.Position.X, y = o.State.Position.Y, z = o.State.Position.Z }).ToList();
        var containers = _containers.Where(c => Dist2(p.Position, c.Position) < r2)
            .Select(c => new { c.Id, c.Kind, items = c.Items.Count, c.Position.X, c.Position.Y, c.Position.Z }).ToList();

        var snapshot = new
        {
            timestampUtc = DateTime.UtcNow.ToString("o"),
            uptime = Math.Round(_uptime, 1),
            description,
            world = _meta.WorldName,
            location = new { system = systemName, planet = planetName, activeLocationId = _meta.ActiveLocationId, planetType = _meta.DefaultPlanetType },
            player = new
            {
                p.PlayerId, p.Name,
                x = p.Position.X, y = p.Position.Y, z = p.Position.Z, p.Yaw, p.Pitch,
                p.Health, p.Oxygen, energy = p.SuitEnergy, p.Hunger,
                aboardShip = p.AboardShip, station = CurrentStationName(p.PlayerId),
                role = p.Role.ToString(), p.GodMode, p.Fly, p.Stealthed,
            },
            environment = BuildEnvironment(p.Position), // the player's local biome weather
            ship = new { _ship.ShipType, _ship.Hull, hullMax = _shipHullMax, _ship.Shield, shieldMax = _shipShieldMax, modules = _ship.Modules },
            surroundings = blocks,
            nearby = new { creatures, npcs, players = others, containers },
            historyBefore = session.History,
        };

        try
        {
            string dir = Path.Combine(_repo.WorldDirectory, "bumps");
            Directory.CreateDirectory(dir);
            _bumpCount++;
            string file = Path.Combine(dir, $"bump_{_bumpCount:D3}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            File.WriteAllText(file, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            _log.Info($"Bump #{_bumpCount} captured for {p.Name}: \"{description}\" -> {file}");
            Send(session, new ServerMessage { Text = $"Debug snapshot #{_bumpCount} saved: {Path.GetFileName(file)}" });
        }
        catch (Exception e)
        {
            _log.Info($"Bump capture failed: {e.Message}");
            Send(session, new ServerMessage { Text = "Bump failed: " + e.Message });
        }
    }

    private static float Dist2(Vector3f a, Vector3i b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }
}
