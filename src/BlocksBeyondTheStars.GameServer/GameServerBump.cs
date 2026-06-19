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

    private void HandleBump(PlayerSession session, string description, byte[] image = null)
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

        var creatures = _creatures.Where(c => WrapDistSq(p.Position, c.Position) < r2)
            .Select(c => new { c.Id, kind = c.Kind.ToString(), species = c.SpeciesId, c.Hostile, x = c.Position.X, y = c.Position.Y, z = c.Position.Z }).ToList();
        var npcs = _npcs.Where(npc => WrapDistSq(p.Position, npc.Pos) < r2)
            .Select(npc => new { npc.Id, npc.Role, npc.Theme, x = npc.Pos.X, y = npc.Pos.Y, z = npc.Pos.Z }).ToList();
        var others = _sessions.Values.Where(o => o.Joined && o.ConnectionId != session.ConnectionId && WrapDistSq(p.Position, o.State.Position) < r2)
            .Select(o => new { o.State.Name, x = o.State.Position.X, y = o.State.Position.Y, z = o.State.Position.Z }).ToList();
        var containers = _containers.Where(c => Dist2(p.Position, c.Position) < r2)
            .Select(c => new { c.Id, c.Kind, items = c.Items.Count, c.Position.X, c.Position.Y, c.Position.Z }).ToList();

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
            string imageName = hasImage ? stem + ".jpg" : null;

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

            string file = Path.Combine(dir, stem + ".json");
            File.WriteAllText(file, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            if (hasImage)
            {
                File.WriteAllBytes(Path.Combine(dir, imageName), image);
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
