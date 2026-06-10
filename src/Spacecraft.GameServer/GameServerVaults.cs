using System.Collections.Generic;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Primitives;
using Spacecraft.Shared.World;
using Spacecraft.WorldGeneration;

namespace Spacecraft.GameServer;

/// <summary>
/// Buried vault ruins ("Welten reicher" W-R3): 0–2 ancient vaults per world — a broken pillar ring on the
/// surface hints at a 2×2 shaft dropping into a buried stone chamber holding data caches + lootable
/// containers (the existing structure-loot/container system). Deterministic from the world seed, stamped
/// once per world like settlements/wrecks, and listed on the planet map as a "vault_ruin" POI.
/// </summary>
public sealed partial class GameServer
{
    private bool _vaultsStamped { get => _worlds.Active.VaultsStamped; set => _worlds.Active.VaultsStamped = value; }
    private List<Vector3i> _vaultEntrances => _worlds.Active.VaultEntrances;

    /// <summary>Surface entrances of this world's stamped vaults (tests/inspection).</summary>
    public IReadOnlyList<Vector3i> VaultEntrances => _vaultEntrances;

    /// <summary>Stamps this world's buried vaults (idempotent per world). Moderate density: most worlds have
    /// one, some a second, a few none — finds stay special but exploration is regularly rewarded.</summary>
    private void StampVaults()
    {
        if (_vaultsStamped)
        {
            return;
        }

        _vaultsStamped = true;
        var planet = _world.Planet;
        if (planet.Void)
        {
            return; // stations have no terrain to bury anything in
        }

        long vSeed = _meta.Seed ^ WorldGenerator.StableHash("vault:" + _world.LocationId);
        var rng = new System.Random(unchecked((int)(vSeed ^ (vSeed >> 32))));
        int count = rng.NextDouble() < 0.75 ? (rng.NextDouble() < 0.35 ? 2 : 1) : 0;
        for (int i = 0; i < count; i++)
        {
            // A deterministic spot away from the spawn/landing area, each vault in its own direction.
            int ax = (120 + rng.Next(320)) * (rng.Next(2) == 0 ? 1 : -1);
            int az = (90 + rng.Next(280)) * (rng.Next(2) == 0 ? 1 : -1);
            StampVault(WorldConstants.WrapX(ax, _world.Circumference), az, rng);
        }

        if (_vaultEntrances.Count > 0)
        {
            _log.Info($"Stamped {_vaultEntrances.Count} buried vault(s) on '{_world.LocationId}'.");
        }
    }

    /// <summary>Carves one vault: surface pillar ring → 2×2 shaft → buried 9×9 chamber (deepslate shell, air
    /// inside) with data caches + two loot containers and a data terminal.</summary>
    private void StampVault(int ax, int az, System.Random rng)
    {
        var planet = _world.Planet;
        int surfaceY = _generator.SurfaceHeight(planet, ax, az);
        if (surfaceY < 24)
        {
            return; // too low to bury a chamber under (sea floors etc.)
        }

        var shell = (_content.GetBlock("deepslate") ?? _content.GetBlock("stone"))!.NumericId;
        var pillar = (_content.GetBlock("granite") ?? _content.GetBlock("stone"))!.NumericId;
        var cache = _content.GetBlock("data_cache")?.NumericId ?? BlockId.Air;

        int floorY = surfaceY - 16;

        // Chamber: a 9×9 outer shell (7×7 inside), 4 air-high, deepslate walls/floor/ceiling.
        for (int dx = -4; dx <= 4; dx++)
        for (int dz = -4; dz <= 4; dz++)
        for (int dy = -1; dy <= 4; dy++)
        {
            var p = new Vector3i(WorldConstants.WrapX(ax + dx, _world.Circumference), floorY + dy, az + dz);
            bool isShell = dx is -4 or 4 || dz is -4 or 4 || dy is -1 or 4;
            _world.SetBlock(p, isShell ? shell : BlockId.Air);
        }

        // Shaft: a 2×2 drop from the surface into the chamber's ceiling corner (the way in — bring a jetpack
        // or dig steps back out).
        for (int dy = floorY + 4; dy <= surfaceY + 1; dy++)
        {
            for (int dx = -1; dx <= 0; dx++)
            for (int dz = -1; dz <= 0; dz++)
            {
                _world.SetBlock(new Vector3i(WorldConstants.WrapX(ax + dx, _world.Circumference), dy, az + dz), BlockId.Air);
            }
        }

        // Surface hint: a broken ring of weathered pillars (1–2 tall, some missing) around the shaft mouth.
        (int X, int Z)[] ring = { (3, 0), (2, 2), (0, 3), (-2, 2), (-3, 0), (-2, -2), (0, -3), (2, -2) };
        foreach (var (rx, rz) in ring)
        {
            if (rng.NextDouble() < 0.3)
            {
                continue; // a collapsed pillar — the ring reads ancient, not freshly built
            }

            int px = WorldConstants.WrapX(ax + rx, _world.Circumference);
            int py = _generator.SurfaceHeight(planet, px, az + rz);
            int h = 1 + rng.Next(2);
            for (int dy = 1; dy <= h; dy++)
            {
                _world.SetBlock(new Vector3i(px, py + dy, az + rz), pillar);
            }
        }

        // Treasure: data caches in the chamber corners + lootable containers via the structure-loot system
        // (salvage capsule + a data terminal — generated once, persisted, looted with G like wreck loot).
        if (!cache.IsAir)
        {
            _world.SetBlock(new Vector3i(WorldConstants.WrapX(ax + 3, _world.Circumference), floorY, az + 3), cache);
            _world.SetBlock(new Vector3i(WorldConstants.WrapX(ax - 3, _world.Circumference), floorY, az + 3), cache);
            _world.SetBlock(new Vector3i(WorldConstants.WrapX(ax + 3, _world.Circumference), floorY, az - 3), cache);
        }

        SpawnStructureLoot("vault", "loot", new Vector3f(ax - 2, floorY, az - 2), rng);
        SpawnStructureLoot("vault", "loot", new Vector3f(ax + 2, floorY, az - 3), rng);
        SpawnStructureLoot("vault", "data_terminal", new Vector3f(ax, floorY, az), rng);

        _vaultEntrances.Add(new Vector3i(ax, surfaceY, az));
    }
}
