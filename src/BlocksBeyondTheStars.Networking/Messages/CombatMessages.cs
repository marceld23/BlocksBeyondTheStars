// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>Client attacks a planet enemy with the held tool/weapon. The server resolves the hit.
/// Contractless-additive aim fields (#693): the camera-ray direction of the shot, so the server can
/// validate the claimed target against where the player was actually looking (angle + line of sight).
/// An all-zero direction (older client) skips those checks.</summary>
public sealed class AttackEntityIntent
{
    public string EntityId { get; set; } = string.Empty;
    public float DirX { get; set; }
    public float DirY { get; set; }
    public float DirZ { get; set; }
}

/// <summary>Client's shot hit terrain instead of an entity, with a weapon that sets things alight (#788).
/// Carries the impact cell so the server can ignite it — a normal attack intent has no block coordinate, so
/// without this a shot at a tree was a purely cosmetic client-side particle. The server re-validates the
/// weapon, the range and the suit-energy cost, and ignition itself still runs the flammable + protection
/// guards, so a spoofed cell can at worst light a plant the player could have torched by hand.</summary>
public sealed class ShootBlockIntent
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

/// <summary>A combat entity in space (asteroid, drone, ufo, cruiser) or a planet enemy.</summary>
public sealed class NetCombatEntity
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Hostile { get; set; }
    public float Hull { get; set; }
    public float HullMax { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>Visual model scale (stations: by size tier — a colossal hull dwarfs a small one).
    /// Contractless-additive: payloads without the field leave the default 1.</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>#2278: dazed by a shock-glove push — a planet machine or a bandit neither moves nor attacks for a moment;
    /// the client draws sparks (machines) or circling stars (bandits) over it. Contractless-additive like
    /// <see cref="Scale"/>: an older client ignores it and an older server leaves it false, so no protocol bump.</summary>
    public bool Staggered { get; set; }

    /// <summary>#2357: the heading (degrees about Y, the client's Euler Y) of an entity that flies a real hull — the
    /// raider turns its voxel ship toward its course. 0 for everything else. Contractless-additive like <see cref="Scale"/>.</summary>
    public float Yaw { get; set; }

    /// <summary>#2670: a planet machine's or bandit's real velocity over the last server tick (blocks/s) — the tangent of the
    /// client's interpolation curve. 0 in space and for anything that did not move. Contractless-additive like
    /// <see cref="Scale"/>.</summary>
    public float VelX { get; set; }

    /// <inheritdoc cref="VelX"/>
    public float VelY { get; set; }

    /// <inheritdoc cref="VelX"/>
    public float VelZ { get; set; }
}

/// <summary>Snapshot of hostile entities near the player on the planet surface.</summary>
public sealed class PlanetEnemyList
{
    public NetCombatEntity[] Enemies { get; set; } = System.Array.Empty<NetCombatEntity>();

    /// <summary>#2670: the server clock of the tick these positions belong to (see <see cref="CreatureList.ServerTime"/>).
    /// Additive: an older server sends 0 and the client stamps on arrival.</summary>
    public double ServerTime { get; set; }
}

/// <summary>A planet enemy was defeated.</summary>
public sealed class PlanetEnemyDefeated
{
    public string Id { get; set; } = string.Empty;
}

/// <summary>A base sentry fired at a hostile (#1214) — purely cosmetic: the client draws a tracer from
/// the sentry cell to the target and plays a short shot. The damage itself is server-authoritative and
/// already reflected by the enemy/creature broadcasts, so a dropped tracer costs nothing.</summary>
public sealed class SentryShot
{
    /// <summary>The firing sentry block's cell centre.</summary>
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>The combat entity that was hit (planet enemy or bandit).</summary>
    public string TargetId { get; set; } = string.Empty;
}
