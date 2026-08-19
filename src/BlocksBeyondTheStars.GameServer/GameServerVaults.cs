// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

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

    // --- One-time stamp registry (#467). The in-memory guards (WreckStamped & co.) die when the world
    // unloads — and a world unloads as soon as its last player leaves — so re-entering used to re-run the
    // whole stamp chain: mined ruin/vault/wreck blocks resurrected (a repeatable material farm), player
    // builds inside the footprints were carved away, and the wreck became claimable again. The registry
    // persists "this feature's blocks are already in the world" per location; the stampers still RE-DERIVE
    // their runtime state (positions, markers) every entry — they just skip the block writes. ---

    /// <summary>True once the one-time feature ("ruins"/"vaults"/"wreck") was stamped on this world.</summary>
    private bool FeatureStamped(string feature)
        => _meta.StampedFeatures.Contains(_world.LocationId + "|" + feature);

    private void MarkFeatureStamped(string feature)
    {
        string key = _world.LocationId + "|" + feature;
        if (!_meta.StampedFeatures.Contains(key))
        {
            _meta.StampedFeatures.Add(key);
            _repo.SaveMetadata(_meta);
        }
    }

    /// <summary>Stamps this world's buried vaults (once per world, #467). Moderate density: most worlds have
    /// one, some a second, a few none — finds stay special but exploration is regularly rewarded. On
    /// re-entry the same deterministic rolls re-derive the entrances, but no blocks are written — a mined
    /// vault shell stays mined.</summary>
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

        bool write = !FeatureStamped("vaults");
        long vSeed = _meta.Seed ^ WorldGenerator.StableHash("vault:" + _world.LocationId);
        var rng = new System.Random(unchecked((int)(vSeed ^ (vSeed >> 32))));
        // World options: the chosen vault frequency scales both rolls (Off ⇒ none; Frequent ⇒ most
        // worlds carry one and a second becomes common).
        double f = _meta.Description.Vaults.StructureFactor();
        int count = rng.NextDouble() < System.Math.Min(0.95, 0.75 * f)
            ? (rng.NextDouble() < System.Math.Min(0.8, 0.35 * f) ? 2 : 1)
            : 0;


        for (int i = 0; i < count; i++)
        {
            // A deterministic spot away from the spawn/landing area, each vault in its own direction. The
            // rolls are consumed unconditionally so every path (fresh/legacy/replay) walks the same stream.
            int ax = (120 + rng.Next(320)) * (rng.Next(2) == 0 ? 1 : -1);
            int az = (90 + rng.Next(280)) * (rng.Next(2) == 0 ? 1 : -1);
            int wx = WorldConstants.WrapX(ax, _world.Circumference);

            // #586: pinned record → legacy re-derive → guaranteed placement (fresh worlds).
            var rec = FindPlacementRecord("vault", i);
            if (rec is not null)
            {
                if (rec.Placed)
                {
                    StampVault(rec.X, rec.Z, rng, write, rec.Seat == "wellhead", legacyGate: false);
                }

                continue;
            }

            if (!_worlds.Active.VirginAtLoad)
            {
                // Legacy world: the frozen behaviour (silent skips included), recorded for future loads.
                if (OverlapsAnySettlement(wx, az, 6))
                {
                    RecordPlacementSkip("vault", i);
                    continue;
                }

                int before = _vaultEntrances.Count;
                StampVault(wx, az, rng, write, wellhead: false, legacyGate: true);
                if (_vaultEntrances.Count > before)
                {
                    var e = _vaultEntrances[^1];
                    RecordPlacement("vault", i, e, e.Y, onIsland: false, "buried", string.Empty);
                }
                else
                {
                    RecordPlacementSkip("vault", i);
                }

                continue;
            }

            // Fresh world, guaranteed: nudge off reserved footprints deterministically; a dry column always
            // works (the old `surfaceY < 24` gate predates the deep floor — a chamber fits under any dry
            // ground now), a water column gets the WELLHEAD variant (chamber under the seabed, cased shaft
            // rising through the water). Lava columns are skipped by the nudge (nobody dives into lava).
            bool Fits(int x, int z) => !OverlapsAnySettlement(x, z, 6) && !_generator.IsSurfaceLava(planet, x, z);
            int vx = wx, vz = az;
            if (!Fits(vx, vz))
            {
                bool found = false;
                for (int radius = 12; radius <= 96 && !found; radius += 12)
                {
                    for (int step = 0; step < 8 && !found; step++)
                    {
                        double a = step * System.Math.PI / 4.0;
                        int nx = WorldConstants.WrapX(wx + (int)(System.Math.Cos(a) * radius), _world.Circumference);
                        int nz = az + (int)(System.Math.Sin(a) * radius);
                        if (Fits(nx, nz))
                        {
                            vx = nx;
                            vz = nz;
                            found = true;
                        }
                    }
                }

                if (!found)
                {
                    RecordPlacementSkip("vault", i); // all-lava/all-reserved carve-out
                    continue;
                }
            }

            bool wet = _generator.TryGetWaterSurface(planet, vx, vz, out _, out _);
            int cnt = _vaultEntrances.Count;
            StampVault(vx, vz, rng, write, wellhead: wet, legacyGate: false);
            if (_vaultEntrances.Count > cnt)
            {
                var e = _vaultEntrances[^1];
                RecordPlacement("vault", i, e, e.Y, onIsland: false, wet ? "wellhead" : "buried", string.Empty);
            }
            else
            {
                RecordPlacementSkip("vault", i);
            }
        }

        // Frontier scaling (#1122): a full-frontier world buries one vault MORE — in its OWN placement
        // slot ("vault_frontier"), so the base loop, its rng draws and every existing record replay
        // unchanged. Worlds stamped BEFORE the feature record a one-time skip instead (no vault ever
        // materialises retroactively under someone's base); only newly stamped worlds carry the extra one.
        int requested = count;
        if (count > 0 && FrontierTierForBody(_world.LocationId) >= 2)
        {
            // Draws consumed unconditionally (the stream contract above) — the tier is seed-stable.
            int fx = (120 + rng.Next(320)) * (rng.Next(2) == 0 ? 1 : -1);
            int fz = (90 + rng.Next(280)) * (rng.Next(2) == 0 ? 1 : -1);
            int fwx = WorldConstants.WrapX(fx, _world.Circumference);
            var extraRec = FindPlacementRecord("vault_frontier", 0);
            if (extraRec is not null)
            {
                if (extraRec.Placed)
                {
                    requested++;
                    StampVault(extraRec.X, extraRec.Z, rng, write, extraRec.Seat == "wellhead", legacyGate: false);
                }
            }
            else if (!_worlds.Active.VirginAtLoad)
            {
                RecordPlacementSkip("vault_frontier", 0);
            }
            else
            {
                bool FitsExtra(int x, int z) => !OverlapsAnySettlement(x, z, 6) && !_generator.IsSurfaceLava(planet, x, z);
                int vx = fwx, vz = fz;
                bool ok = FitsExtra(vx, vz);
                for (int radius = 12; radius <= 96 && !ok; radius += 12)
                {
                    for (int step = 0; step < 8 && !ok; step++)
                    {
                        double a = step * System.Math.PI / 4.0;
                        int nx = WorldConstants.WrapX(fwx + (int)(System.Math.Cos(a) * radius), _world.Circumference);
                        int nz = fz + (int)(System.Math.Sin(a) * radius);
                        if (FitsExtra(nx, nz))
                        {
                            vx = nx;
                            vz = nz;
                            ok = true;
                        }
                    }
                }

                if (!ok)
                {
                    RecordPlacementSkip("vault_frontier", 0);
                }
                else
                {
                    requested++;
                    bool wet = _generator.TryGetWaterSurface(planet, vx, vz, out _, out _);
                    int cnt = _vaultEntrances.Count;
                    StampVault(vx, vz, rng, write, wellhead: wet, legacyGate: false);
                    if (_vaultEntrances.Count > cnt)
                    {
                        var e = _vaultEntrances[^1];
                        RecordPlacement("vault_frontier", 0, e, e.Y, onIsland: false, wet ? "wellhead" : "buried", string.Empty);
                    }
                    else
                    {
                        RecordPlacementSkip("vault_frontier", 0);
                    }
                }
            }
        }

        SavePlacementRecords();
        ReportStamp("vault", requested, _vaultEntrances.Count);
        if (write && _vaultEntrances.Count > 0)
        {
            _log.Info($"Stamped {_vaultEntrances.Count} buried vault(s) on '{_world.LocationId}'.");
        }

        if (write)
        {
            MarkFeatureStamped("vaults");
        }
    }

    /// <summary>Carves one vault: surface pillar ring → 2×2 shaft → buried 9×9 chamber (deepslate shell, air
    /// inside) with data caches + two loot containers and a data terminal. With <paramref name="write"/>
    /// false only the runtime state (entrances, loot markers) is re-derived (#467). The WELLHEAD variant
    /// (#586) buries the chamber under a water body's seabed and cases the shaft up through the water as a
    /// stone well tube. <paramref name="legacyGate"/> keeps the pre-deep-floor `surfaceY &lt; 24` skip for
    /// legacy worlds whose stamped state depends on it; fresh worlds bury under any dry column.</summary>
    private void StampVault(int ax, int az, System.Random rng, bool write, bool wellhead, bool legacyGate)
    {
        var planet = _world.Planet;
        int surfaceY;
        int waterTop = int.MinValue;
        if (wellhead && _generator.TryGetWaterSurface(planet, ax, az, out int wTop, out int seabed))
        {
            surfaceY = seabed; // the chamber hides under the seabed; the shaft cases up through the water
            waterTop = wTop;
        }
        else
        {
            surfaceY = _generator.SurfaceHeight(planet, ax, az);
        }

        if (legacyGate && surfaceY < 24)
        {
            return; // frozen legacy behaviour: too low to bury a chamber under (sea floors etc.)
        }

        var shell = (_content.GetBlock("deepslate") ?? _content.GetBlock("stone"))!.NumericId;
        var pillar = (_content.GetBlock("granite") ?? _content.GetBlock("stone"))!.NumericId;
        var cache = _content.GetBlock("data_cache")?.NumericId ?? BlockId.Air;

        // #467: on re-entry only the runtime state is re-derived; block writes are one-time. Every rng
        // consumption below stays unconditional so write/no-write walks the identical roll sequence.
        void Put(Vector3i p, BlockId b)
        {
            if (write)
            {
                _world.SetBlock(p, b);
            }
        }

        int floorY = surfaceY - 16;

        // Chamber: a 9×9 outer shell (7×7 inside), 4 air-high, deepslate walls/floor/ceiling.
        for (int dx = -4; dx <= 4; dx++)
            for (int dz = -4; dz <= 4; dz++)
                for (int dy = -1; dy <= 4; dy++)
                {
                    var p = new Vector3i(WorldConstants.WrapX(ax + dx, _world.Circumference), floorY + dy, az + dz);
                    bool isShell = dx is -4 or 4 || dz is -4 or 4 || dy is -1 or 4;
                    Put(p, isShell ? shell : BlockId.Air);
                }

        // Shaft: a 2×2 drop from the surface into the chamber's ceiling corner (the way in — bring a jetpack
        // or dig steps back out).
        for (int dy = floorY + 4; dy <= surfaceY + 1; dy++)
        {
            for (int dx = -1; dx <= 0; dx++)
                for (int dz = -1; dz <= 0; dz++)
                {
                    Put(new Vector3i(WorldConstants.WrapX(ax + dx, _world.Circumference), dy, az + dz), BlockId.Air);
                }
        }

        // Wellhead (#586): case the shaft up through the water column as a 4×4 stone well tube with the
        // 2×2 drop inside, mouth one block above the water line — a diveable ancient well.
        if (waterTop != int.MinValue)
        {
            for (int dy = surfaceY + 2; dy <= waterTop + 1; dy++)
            {
                for (int dx = -2; dx <= 1; dx++)
                    for (int dz = -2; dz <= 1; dz++)
                    {
                        bool casing = dx is -2 or 1 || dz is -2 or 1;
                        Put(new Vector3i(WorldConstants.WrapX(ax + dx, _world.Circumference), dy, az + dz),
                            casing ? shell : BlockId.Air);
                    }
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
                Put(new Vector3i(px, py + dy, az + rz), pillar);
            }
        }

        // Treasure: data caches in the chamber corners + lootable containers via the structure-loot system
        // (salvage capsule + a data terminal — generated once, persisted, looted with G like wreck loot).
        if (!cache.IsAir)
        {
            Put(new Vector3i(WorldConstants.WrapX(ax + 3, _world.Circumference), floorY, az + 3), cache);
            Put(new Vector3i(WorldConstants.WrapX(ax - 3, _world.Circumference), floorY, az + 3), cache);
            Put(new Vector3i(WorldConstants.WrapX(ax + 3, _world.Circumference), floorY, az - 3), cache);
        }

        // Loot markers stay unconditional — SpawnStructureLoot is idempotent via GeneratedLoot.
        SpawnStructureLoot("vault", "loot", new Vector3f(ax - 2, floorY, az - 2), rng);
        SpawnStructureLoot("vault", "loot", new Vector3f(ax + 2, floorY, az - 3), rng);
        SpawnStructureLoot("vault", "data_terminal", new Vector3f(ax, floorY, az), rng);

        _vaultEntrances.Add(new Vector3i(ax, surfaceY, az));
    }
}
