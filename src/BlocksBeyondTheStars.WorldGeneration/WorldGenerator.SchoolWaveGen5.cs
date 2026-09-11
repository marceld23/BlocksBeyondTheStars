// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// School club wave 3 (#1756, terrain generation 5) — the set-dressing rows and the mountain-sized landmarks the
/// children asked for (partial of <see cref="WorldGenerator"/>). Scrap heaps, wreck hulls and girders on the scrap
/// planet (#1761) and, much rarer, strewn across every other solid-ground world; PC desks and PC heaps on the
/// gaming planet (#1762) together with a monitor, a keyboard and a mouse the size of mountains. Every gate here
/// is false below generation 5, and every function is a pure function of the body seed — the rules of every
/// earlier wave. Kept in its own file so the JIT size of the grown column methods is untouched (#1740).
/// </summary>
public sealed partial class WorldGenerator
{
    // ---------------- prop gates (generation ≥ 5) ----------------

    /// <summary>The scrap planet's own rows: dense scrap wherever the ground is solid.</summary>
    private static bool PropScrap(WonderProfile w, PlanetType p)
        => w.Generation >= WorldDescription.AuthoredContentGeneration && PropSolidGround(w, p) && p.HasTag(TerrainTag.Scrap);

    /// <summary>Stray scrap on every OTHER solid-ground world with air (Marcel, 2026-09-11: "auch random auf anderen
    /// Welten, an der Oberfläche, wo es passt, aber selten") — the same shapes at a fortieth of the density.</summary>
    private static bool PropStrayScrap(WonderProfile w, PlanetType p)
        => w.Generation >= WorldDescription.AuthoredContentGeneration && PropSolidGround(w, p) && !p.Cratered && HasAir(p) && !p.HasTag(TerrainTag.Scrap);

    /// <summary>The gaming planet's desks and PC heaps.</summary>
    private static bool PropGaming(WonderProfile w, PlanetType p)
        => w.Generation >= WorldDescription.AuthoredContentGeneration && PropSolidGround(w, p) && p.HasTag(TerrainTag.Gaming);

    // ---------------- prop shapes (fill air only, never carve) ----------------

    /// <summary>A scrap heap: a 3×2 mound of scrap piles with a scrap-metal core, one block taller in the middle.</summary>
    private static void StampScrapHeap(PropStamp s)
    {
        var core = s.Secondary.IsAir ? s.Material : s.Secondary;
        bool alongX = (s.ShapeHash & 1) == 0;
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 2; j++)
            {
                int px = s.Wx + (alongX ? i : j), pz = s.Wz + (alongX ? j : i);
                int py = s.Generator.SurfaceHeight(s.Planet, px, pz);
                s.Set(px, py + 1, pz, (i + j + s.ShapeHash) % 3 == 0 ? core : s.Material);
            }

        int mx = s.Wx + (alongX ? 1 : 0), mz = s.Wz + (alongX ? 0 : 1);
        s.Set(mx, s.Generator.SurfaceHeight(s.Planet, mx, mz) + 2, mz, (s.ShapeHash & 2) == 0 ? s.Material : core);
        if (!s.Cache.IsAir && s.ShapeHash % 20 == 0)
        {
            s.Set(s.Wx - 1, s.Sy + 1, s.Wz, s.Cache); // one heap in twenty hides a data cache
        }
    }

    /// <summary>A wreck hull: a 5-long arc of rusted panels standing on edge, a broken machine wedged inside.</summary>
    private static void StampWreckHull(PropStamp s)
    {
        var machine = s.Secondary.IsAir ? s.Material : s.Secondary;
        bool alongX = (s.ShapeHash & 1) == 0;
        int[] rise = { 1, 2, 3, 2, 1 };
        for (int i = 0; i < 5; i++)
        {
            int px = s.Wx + (alongX ? i : 0), pz = s.Wz + (alongX ? 0 : i);
            int py = s.Generator.SurfaceHeight(s.Planet, px, pz);
            for (int dy = 1; dy <= rise[i]; dy++)
            {
                s.Set(px, py + dy, pz, s.Material);
            }
        }

        // the far wall of the hull, two blocks over, half fallen
        for (int i = 1; i < 4; i++)
        {
            int px = s.Wx + (alongX ? i : 2), pz = s.Wz + (alongX ? 2 : i);
            int py = s.Generator.SurfaceHeight(s.Planet, px, pz);
            s.Set(px, py + 1, pz, s.Material);
            if (((s.ShapeHash >> i) & 1) == 0)
            {
                s.Set(px, py + 2, pz, s.Material);
            }
        }

        int cx = s.Wx + (alongX ? 2 : 1), cz = s.Wz + (alongX ? 1 : 2);
        s.Set(cx, s.Generator.SurfaceHeight(s.Planet, cx, cz) + 1, cz, machine);
        if (!s.Cache.IsAir && s.ShapeHash % 10 < 3)
        {
            s.Set(cx, s.Generator.SurfaceHeight(s.Planet, cx, cz) + 2, cz, s.Cache);
        }
    }

    /// <summary>A fallen girder: a 4–6-long scrap-metal beam lying on the ground, one end propped a block up on a pile.</summary>
    private static void StampGirder(PropStamp s)
    {
        int len = 4 + s.ShapeHash % 3;
        bool alongX = (s.ShapeHash & 8) == 0;
        var prop = s.Secondary.IsAir ? s.Material : s.Secondary;
        for (int i = 0; i < len; i++)
        {
            int px = s.Wx + (alongX ? i : 0), pz = s.Wz + (alongX ? 0 : i);
            int py = s.Generator.SurfaceHeight(s.Planet, px, pz);
            bool propped = i == len - 1;
            if (propped)
            {
                s.Set(px, py + 1, pz, prop);
            }

            s.Set(px, py + (propped ? 2 : 1), pz, s.Material);
        }
    }

    /// <summary>A desk setup: a gaming PC with the monitor on top, keyboard and mouse in front of it.</summary>
    private static void StampDeskSetup(PropStamp s)
    {
        var monitor = s.Secondary.IsAir ? s.Material : s.Secondary;
        var keyboard = s.Generator._content.GetBlock("gaming_keyboard")?.NumericId ?? s.Material;
        var mouse = s.Generator._content.GetBlock("gaming_mouse")?.NumericId ?? s.Material;
        int fx = (s.ShapeHash & 1) == 0 ? 1 : -1; // which way the desk faces
        s.Set(s.Wx, s.Sy + 1, s.Wz, s.Material);
        s.Set(s.Wx, s.Sy + 2, s.Wz, monitor);
        int kx = s.Wx + fx, kz = s.Wz;
        s.Set(kx, s.Generator.SurfaceHeight(s.Planet, kx, kz) + 1, kz, keyboard);
        int mz = s.Wz + ((s.ShapeHash & 2) == 0 ? 1 : -1);
        s.Set(kx, s.Generator.SurfaceHeight(s.Planet, kx, mz) + 1, mz, mouse);
    }

    /// <summary>A PC heap: a 2×2×2 pile of towers with a keyboard or two dropped on top.</summary>
    private static void StampPcHeap(PropStamp s)
    {
        var keyboard = s.Secondary.IsAir ? s.Material : s.Secondary;
        for (int dx = 0; dx <= 1; dx++)
            for (int dz = 0; dz <= 1; dz++)
            {
                int py = s.Generator.SurfaceHeight(s.Planet, s.Wx + dx, s.Wz + dz);
                s.Set(s.Wx + dx, py + 1, s.Wz + dz, s.Material);
                if (((s.ShapeHash >> (dx * 2 + dz)) & 1) == 0)
                {
                    s.Set(s.Wx + dx, py + 2, s.Wz + dz, s.Material);
                }
            }

        s.Set(s.Wx, s.Sy + 3, s.Wz, keyboard);
        if ((s.ShapeHash & 16) == 0)
        {
            s.Set(s.Wx + 1, s.Sy + 2, s.Wz + 1, keyboard);
        }
    }

    // ---------------- gaming landmarks (#1762): a monitor, a keyboard and a mouse the size of mountains ----------------

    private const long GiantMonitorSalt = 0x6A3E10;
    private const long GiantKeyboardSalt = 0x6A3E20;
    private const long GiantMouseSalt = 0x6A3E30;
    private const double GamingCellSize = 2400.0; // ≈ 2–3 candidate cells on a default world → about one of each
    private const double GamingChance = 0.6;
    private const double GamingMargin = 72.0;     // the widest extent (the keyboard's 40 + its cable) plus slack

    /// <summary>The gaming landmarks grow on generation-5 worlds carrying the <c>gaming</c> tag — solid ground, never
    /// a cratered, floating or void body.</summary>
    private bool HasGamingLandmarks(PlanetType planet)
        => _terrainGeneration >= WorldDescription.AuthoredContentGeneration && planet.HasTag(TerrainTag.Gaming)
            && !planet.Void && !planet.Cratered && !_crateredWorld && !planet.FloatingIslands;

    /// <summary>The giant monitor: a 60 × 40 slab, 8 thick, on a 4-high stand plate. The screen faces −Z.</summary>
    private double GiantMonitorOffset(WonderProfile w, int worldX, int worldZ)
    {
        if (!TryGetHotspot(w.Seed ^ GiantMonitorSalt, GamingCellSize, GamingChance, GamingMargin, worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double half = 26.0 + ((h >> 16) & 0xF);      // 26..41 → 52..82 wide
        double height = 36.0 + ((h >> 20) & 0x7);    // 36..43 tall
        double ax = System.Math.Abs(dx), az = System.Math.Abs(dz);
        if (ax <= half && az <= 4.0)
        {
            return height;                           // the panel
        }

        if (ax <= 8.0 && az <= 10.0)
        {
            return 4.0;                              // the stand plate
        }

        return 0.0;
    }

    private BlockId? GiantMonitorPaint(WonderProfile w, int worldX, int worldZ, int surfaceY, out int fillToY)
    {
        fillToY = int.MinValue;
        double rise = GiantMonitorOffset(w, worldX, worldZ);
        if (rise <= 0.0)
        {
            return null;
        }

        var monitor = _content.GetBlock("gaming_monitor")?.NumericId ?? BlockId.Air;
        if (monitor.IsAir)
        {
            return null;
        }

        fillToY = surfaceY - (int)rise;              // the whole slab is monitor, not a skin over rock
        return monitor;
    }

    /// <summary>The giant keyboard: an 80 × 30 plateau 6 high with 4 × 4 key bumps on an 8-block grid, +2 each.</summary>
    private double GiantKeyboardOffset(WonderProfile w, int worldX, int worldZ)
    {
        if (!TryGetHotspot(w.Seed ^ GiantKeyboardSalt, GamingCellSize, GamingChance, GamingMargin, worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double halfX = 36.0 + ((h >> 16) & 0x7), halfZ = 13.0 + ((h >> 20) & 0x3);
        if (System.Math.Abs(dx) > halfX || System.Math.Abs(dz) > halfZ)
        {
            return 0.0;
        }

        // key bumps: a 6-wide key with a 2-wide gap, inset from the plateau's rim
        int kx = ((int)System.Math.Floor(dx + halfX) % 8 + 8) % 8, kz = ((int)System.Math.Floor(dz + halfZ) % 8 + 8) % 8;
        bool key = kx >= 1 && kx <= 6 && kz >= 1 && kz <= 6
            && System.Math.Abs(dx) < halfX - 2.0 && System.Math.Abs(dz) < halfZ - 2.0;
        return key ? 8.0 : 6.0;
    }

    private BlockId? GiantKeyboardPaint(WonderProfile w, int worldX, int worldZ, int surfaceY, out int fillToY)
    {
        fillToY = int.MinValue;
        double rise = GiantKeyboardOffset(w, worldX, worldZ);
        if (rise <= 0.0)
        {
            return null;
        }

        var keyboard = _content.GetBlock("gaming_keyboard")?.NumericId ?? BlockId.Air;
        if (keyboard.IsAir)
        {
            return null;
        }

        fillToY = surfaceY - (int)rise;
        return keyboard;
    }

    /// <summary>The giant mouse: an ellipsoid dome 30 × 48 in plan and 14 high, a 2-high cable ridge trailing behind it.</summary>
    private double GiantMouseOffset(WonderProfile w, int worldX, int worldZ)
    {
        if (!TryGetHotspot(w.Seed ^ GiantMouseSalt, GamingCellSize, GamingChance, GamingMargin, worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double rx = 14.0 + ((h >> 16) & 0x3), rz = 22.0 + ((h >> 18) & 0x3), height = 13.0 + ((h >> 20) & 0x3);
        double u = dx * dx / (rx * rx) + dz * dz / (rz * rz);
        if (u < 1.0)
        {
            return height * System.Math.Sqrt(1.0 - u);
        }

        // the cable: a thin ridge running away from the tail, wandering a little
        if (dz > rz && dz < rz + 40.0 && System.Math.Abs(dx - 3.0 * System.Math.Sin(dz * 0.15)) <= 1.0)
        {
            return 2.0;
        }

        return 0.0;
    }

    private BlockId? GiantMousePaint(WonderProfile w, int worldX, int worldZ, int surfaceY, out int fillToY)
    {
        fillToY = int.MinValue;
        double rise = GiantMouseOffset(w, worldX, worldZ);
        if (rise <= 0.0)
        {
            return null;
        }

        var mouse = _content.GetBlock("gaming_mouse")?.NumericId ?? BlockId.Air;
        var cable = _content.GetBlock("obsidian")?.NumericId ?? BlockId.Air;
        if (mouse.IsAir)
        {
            return null;
        }

        fillToY = surfaceY - (int)rise;
        return rise <= 2.0 && !cable.IsAir ? cable : mouse;
    }
}
