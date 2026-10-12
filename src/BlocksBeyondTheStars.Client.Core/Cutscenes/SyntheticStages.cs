// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client.Cutscenes
{
    /// <summary>What a synthetic stage shows (#2477).</summary>
    public enum SyntheticSet
    {
        /// <summary><c>space_vista</c>: a wide space panorama — stars, nebula, the sun, a distant planet.</summary>
        Vista,

        /// <summary><c>space_vista</c> at the <c>ship</c> anchor: the player's own ship drifting by in front of the planet.</summary>
        VistaShip,

        /// <summary><c>space_vista</c> at the <c>planet</c> anchor: a planet close, filling the frame.</summary>
        VistaPlanet,

        /// <summary><c>diorama</c> at the <c>core</c> anchor: the sleeping Guardian core — a red glowing column in an iron shell.</summary>
        Core,

        /// <summary><c>diorama</c> at the <c>star_map</c> anchor: VEGA's memory of the star chart, drifting out to the rim.</summary>
        MemoryChart,
    }

    /// <summary>The part of the core diorama a block belongs to (its block and glow on the client).</summary>
    public enum CoreDioramaPart
    {
        Floor,
        Shell,
        Column,
    }

    /// <summary>One block of a diorama, in the stage's own block coordinates.</summary>
    public readonly struct DioramaCell
    {
        public DioramaCell(int x, int y, int z, CoreDioramaPart part)
        {
            X = x;
            Y = y;
            Z = z;
            Part = part;
        }

        public int X { get; }
        public int Y { get; }
        public int Z { get; }
        public CoreDioramaPart Part { get; }
    }

    /// <summary>One star of the memory chart, on the chart plane (y = 0).</summary>
    public readonly struct MemoryChartStar
    {
        public MemoryChartStar(float x, float z, bool current, int color)
        {
            X = x;
            Z = z;
            Current = current;
            Color = color;
        }

        public float X { get; }
        public float Z { get; }

        /// <summary>The player's system — where the drift starts.</summary>
        public bool Current { get; }

        /// <summary>The star's colour as 0xRRGGBB (0 = the default warm white).</summary>
        public int Color { get; }
    }

    /// <summary>The memory chart's layout: the stars on a disc of <see cref="SyntheticStages.ChartRadius"/>, where the drift
    /// starts (the player's system), where it ends (the star farthest from it — the rim) and where the faint red glow beyond
    /// the rim sits (no star, no name: "the edge of her memory").</summary>
    public sealed class MemoryChart
    {
        internal MemoryChart(IReadOnlyList<MemoryChartStar> stars, float startX, float startZ, float rimX, float rimZ, float pulseX, float pulseZ)
        {
            Stars = stars;
            StartX = startX;
            StartZ = startZ;
            RimX = rimX;
            RimZ = rimZ;
            PulseX = pulseX;
            PulseZ = pulseZ;
        }

        public IReadOnlyList<MemoryChartStar> Stars { get; }
        public float StartX { get; }
        public float StartZ { get; }
        public float RimX { get; }
        public float RimZ { get; }
        public float PulseX { get; }
        public float PulseZ { get; }
    }

    /// <summary>
    /// The synthetic stages of the cutscene director (#2477, synthesis §6.3/§6.5, d4 §3.2): what a <c>space_vista</c> or
    /// <c>diorama</c> shot shows, laid out in the stage's own coordinates (its origin at (0, 0, 0), +z into the picture),
    /// and how its camera moves over the shot. The client builds the stage far from the world from existing renderers only
    /// (the menu's starfield, nebula, sun glow and phase-lit planet, the voxel mesher for the ship and the diorama blocks)
    /// and flies these numbers; keeping them here makes the layout rules testable without Unity — a diorama stays within
    /// <see cref="DioramaBlockBudget"/> blocks, the core's pulse stays far below 3 Hz, the memory chart never names a star.
    /// </summary>
    public static class SyntheticStages
    {
        /// <summary>The most blocks a diorama may have (synthesis §6.6: "synthetic stages ≤ 200 blocks").</summary>
        public const int DioramaBlockBudget = 200;

        /// <summary>How far the stage's sun sits from the origin (the menu's recipe).</summary>
        public const float SunDistance = 260f;

        /// <summary>The core's breathing glow (Hz) — calm, and far below the 3 Hz photosensitivity limit (synthesis §6.6).</summary>
        public const float CorePulseHz = 0.4f;

        /// <summary>The memory chart's radius (stage units).</summary>
        public const float ChartRadius = 30f;

        /// <summary>How far beyond the rim star the faint red glow sits.</summary>
        public const float PulseBeyondRim = 7f;

        /// <summary>The memory chart's camera: this far behind and above the drifting look point.</summary>
        public const float ChartCameraBack = 15f;
        public const float ChartCameraUp = 11f;

        /// <summary>The core diorama's floor radius² and the shell's band (blocks²), the column's height.</summary>
        private const int FloorRadiusSq = 20;
        private const int ShellInnerSq = 12;
        private const int ShellOuterSq = 20;
        private const int ShellHeight = 3;
        public const int ColumnHeight = 4;

        private static readonly IReadOnlyList<DioramaCell> CoreLayout = BuildCore();

        /// <summary>The set a shot shows, or null when the camera is no synthetic stage (or a diorama names an anchor that has
        /// none — the validator rejects that; at run time the shot falls back).</summary>
        public static SyntheticSet? SetOf(string? camera, string? anchor)
        {
            if (camera == CutsceneCameras.SpaceVista)
            {
                return anchor switch
                {
                    CutsceneAnchors.Ship => SyntheticSet.VistaShip,
                    CutsceneAnchors.Planet => SyntheticSet.VistaPlanet,
                    _ => SyntheticSet.Vista,
                };
            }

            if (camera == CutsceneCameras.Diorama)
            {
                return anchor switch
                {
                    CutsceneAnchors.Core => SyntheticSet.Core,
                    CutsceneAnchors.StarMap => SyntheticSet.MemoryChart,
                    _ => null,
                };
            }

            return null;
        }

        /// <summary>The direction to the stage's sun (upper left of the panorama), normalised.</summary>
        public static void SunDirection(out float x, out float y, out float z)
        {
            const float sx = -0.45f, sy = 0.30f, sz = 0.84f;
            float len = (float)Math.Sqrt(sx * sx + sy * sy + sz * sz);
            x = sx / len;
            y = sy / len;
            z = sz / len;
        }

        /// <summary>Where a vista's planet sits and how big it is (diameter): far and large for the panorama, close for the
        /// <c>planet</c> anchor, behind the ship for the <c>ship</c> anchor.</summary>
        public static void VistaPlanet(SyntheticSet set, out float x, out float y, out float z, out float size)
        {
            switch (set)
            {
                case SyntheticSet.VistaPlanet:
                    (x, y, z, size) = (26f, -9f, 84f, 58f);
                    break;
                case SyntheticSet.VistaShip:
                    (x, y, z, size) = (62f, -26f, 230f, 100f);
                    break;
                default:
                    (x, y, z, size) = (70f, -24f, 260f, 110f);
                    break;
            }
        }

        /// <summary>The ship's drift across the frame at progress <paramref name="k"/> (0..1): from the left, a little rising,
        /// in front of the planet; heading +x.</summary>
        public static void VistaShip(float k, out float x, out float y, out float z)
        {
            k = Clamp01(k);
            x = -8f + 15f * k;
            y = -1.6f + 1.4f * k;
            z = 22f + 3f * k;
        }

        /// <summary>The vista's camera at progress <paramref name="k"/>: at the origin, pushing slowly into the picture while
        /// it turns a few degrees across it (yaw in degrees, the orbit convention; pitch positive = down).</summary>
        public static void VistaCamera(SyntheticSet set, float k, out float x, out float y, out float z, out float yaw, out float pitch)
        {
            k = Clamp01(k);
            float baseYaw = set switch
            {
                SyntheticSet.VistaPlanet => 12f,
                SyntheticSet.VistaShip => 3f,
                _ => 6f,
            };
            x = 0f;
            y = 0f;
            z = 6f * k;
            yaw = baseYaw - 6f + 12f * k;
            pitch = set == SyntheticSet.VistaShip ? 3f : 2f;
        }

        /// <summary>The core diorama's blocks (cached): a round steel floor, an iron shell three blocks high and open to the
        /// front (−z, where the camera looks in), and the red column in the middle, one block taller than the shell.</summary>
        public static IReadOnlyList<DioramaCell> CoreCells() => CoreLayout;

        private static IReadOnlyList<DioramaCell> BuildCore()
        {
            var cells = new List<DioramaCell>();
            for (int x = -4; x <= 4; x++)
            {
                for (int z = -4; z <= 4; z++)
                {
                    int d = x * x + z * z;
                    if (d <= FloorRadiusSq)
                    {
                        cells.Add(new DioramaCell(x, 0, z, CoreDioramaPart.Floor));
                    }

                    if (d > ShellInnerSq && d <= ShellOuterSq && z > -2)
                    {
                        for (int y = 1; y <= ShellHeight; y++)
                        {
                            cells.Add(new DioramaCell(x, y, z, CoreDioramaPart.Shell));
                        }
                    }
                }
            }

            for (int y = 1; y <= ColumnHeight; y++)
            {
                cells.Add(new DioramaCell(0, y, 0, CoreDioramaPart.Column));
            }

            return cells;
        }

        /// <summary>The core diorama's camera at progress <paramref name="k"/>: in front of the shell's opening, it rises
        /// along the red column (synthesis §6.5 "die Kamera steigt an der roten Säule auf") with a slow slide and push-in,
        /// always looking at the column a little above its own height. Block coordinates (a block's centre is +0.5).</summary>
        public static void CoreCamera(float k, out float px, out float py, out float pz, out float lx, out float ly, out float lz)
        {
            k = Clamp01(k);
            float angle = 168f + 24f * k; // through the open front (the −z side)
            float radius = 8f - 1.5f * k;
            CutsceneStaging.Polar(0.5f, 0.5f, angle, radius, out px, out pz);
            py = 1.4f + 3.6f * k;
            lx = 0.5f;
            ly = 1.6f + 3.0f * k;
            lz = 0.5f;
        }

        /// <summary>The core's glow at <paramref name="seconds"/> (0..1): a slow breath at <see cref="CorePulseHz"/>.</summary>
        public static float CorePulse(float seconds) => 0.5f + 0.5f * (float)Math.Sin(2.0 * Math.PI * CorePulseHz * seconds);

        /// <summary>
        /// Lays VEGA's memory chart out (#2477, synthesis §6.5 ch6: "the memory star chart drifts to the galaxy's edge, a
        /// faint red pulse, no marker"): every system of the star map on a disc (the chart's own fitting,
        /// <see cref="GalaxyChartLayout"/>), the drift from the player's system (<paramref name="currentId"/>; else the first)
        /// to the star farthest from it, and the red glow a little beyond that rim star. Names are never part of it.
        /// </summary>
        public static MemoryChart LayoutMemoryChart(IReadOnlyList<(string Id, float MapX, float MapY, int Color)>? systems, string? currentId)
        {
            if (systems is null || systems.Count == 0)
            {
                return new MemoryChart(Array.Empty<MemoryChartStar>(), 0f, 0f, 0f, ChartRadius, 0f, ChartRadius + PulseBeyondRim);
            }

            int n = systems.Count;
            var xs = new float[n];
            var ys = new float[n];
            for (int i = 0; i < n; i++)
            {
                xs[i] = systems[i].MapX;
                ys[i] = systems[i].MapY;
            }

            GalaxyChartLayout.Centre(xs, ys, out float centreX, out float centreY);
            float scale = GalaxyChartLayout.FitScale(ChartRadius, xs, ys, centreX, centreY);
            int current = 0;
            for (int i = 0; i < n; i++)
            {
                if (!string.IsNullOrEmpty(currentId) && systems[i].Id == currentId)
                {
                    current = i;
                    break;
                }
            }

            var stars = new MemoryChartStar[n];
            for (int i = 0; i < n; i++)
            {
                GalaxyChartLayout.ToChart(xs[i], ys[i], scale, centreX, centreY, out float cx, out float cy);
                stars[i] = new MemoryChartStar(cx, cy, i == current, systems[i].Color);
            }

            var start = stars[current];
            int rim = current;
            float best = -1f;
            for (int i = 0; i < n; i++)
            {
                float dx = stars[i].X - start.X, dz = stars[i].Z - start.Z;
                float d = dx * dx + dz * dz;
                if (d > best)
                {
                    best = d;
                    rim = i;
                }
            }

            float rimX = stars[rim].X, rimZ = stars[rim].Z;
            if (rim == current)
            {
                rimX = start.X;
                rimZ = start.Z + ChartRadius; // a single star: drift straight out
            }

            // The glow lies beyond the rim, outward from the chart's centre (the galaxy's edge) — or along the drift.
            float ox = rimX, oz = rimZ;
            float len = (float)Math.Sqrt(ox * ox + oz * oz);
            if (len < 0.001f)
            {
                ox = rimX - start.X;
                oz = rimZ - start.Z;
                len = (float)Math.Sqrt(ox * ox + oz * oz);
            }

            float pulseX = len > 0.001f ? rimX + ox / len * PulseBeyondRim : rimX;
            float pulseZ = len > 0.001f ? rimZ + oz / len * PulseBeyondRim : rimZ + PulseBeyondRim;
            return new MemoryChart(stars, start.X, start.Z, rimX, rimZ, pulseX, pulseZ);
        }

        /// <summary>The memory chart's camera at progress <paramref name="k"/>: the look point drifts from the player's system
        /// to the rim; the camera follows behind and above it, so the chart slides under the picture toward its edge.</summary>
        public static void MemoryChartCamera(MemoryChart chart, float k, out float px, out float py, out float pz, out float lx,
            out float ly, out float lz)
        {
            k = Clamp01(k);
            float dx = chart.RimX - chart.StartX, dz = chart.RimZ - chart.StartZ;
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len < 0.001f)
            {
                dx = 0f;
                dz = 1f;
                len = 1f;
            }

            dx /= len;
            dz /= len;
            lx = chart.StartX + (chart.RimX - chart.StartX) * k;
            ly = 0f;
            lz = chart.StartZ + (chart.RimZ - chart.StartZ) * k;
            px = lx - dx * ChartCameraBack;
            py = ChartCameraUp;
            pz = lz - dz * ChartCameraBack;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
