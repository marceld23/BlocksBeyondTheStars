// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Cutscenes
{
    /// <summary>What the director's camera actually does in a shot.</summary>
    public enum StagedCameraKind
    {
        /// <summary>The player's own view (letterbox and captions only) — the last fallback, always possible.</summary>
        PlayerView,

        /// <summary>Circling the anchor (ping-pong across the open arc when the full circle is blocked).</summary>
        Orbit,

        /// <summary>Rising above the anchor, looking down.</summary>
        Crane,

        /// <summary>A fixed framing of the anchor with a slow push-in.</summary>
        Static,

        /// <summary>From the player's eye, tilted up to the sky — needs no terrain.</summary>
        Sky,

        /// <summary>The galaxy chart over the frame — needs no terrain.</summary>
        Chart,
    }

    /// <summary>A staged shot: the kind and its numbers (orbit convention: position = centre + (sin·r, h, cos·r), looking at
    /// centre + look height).</summary>
    public sealed class StagedCamera
    {
        public StagedCameraKind Kind { get; set; }

        /// <summary>The camera type the shot asked for.</summary>
        public string Requested { get; set; } = CutsceneCameras.PlayerView;

        public float CenterX { get; set; }
        public float CenterY { get; set; }
        public float CenterZ { get; set; }

        /// <summary>The look target's height above the centre.</summary>
        public float LookHeight { get; set; }

        /// <summary>Where the move starts (radius, height) and where it settles.</summary>
        public float StartRadius { get; set; }
        public float StartHeight { get; set; }
        public float Radius { get; set; }
        public float Height { get; set; }

        /// <summary>The first angle (degrees).</summary>
        public float StartAngle { get; set; }

        /// <summary>The orbit may only sweep across an open arc of <see cref="ArcHalf"/>·2 degrees around
        /// <see cref="ArcCenter"/>.</summary>
        public bool ArcLimited { get; set; }
        public float ArcCenter { get; set; }
        public float ArcHalf { get; set; }

        /// <summary>Whether the shot takes the camera away from the player.</summary>
        public bool TakesCamera => Kind != StagedCameraKind.PlayerView;

        /// <summary>Whether the director had to take a fallback for the requested camera.</summary>
        public bool FellBack => !string.Equals(KindName(Kind), Requested, StringComparison.Ordinal);

        /// <summary>The camera type name of a staged kind.</summary>
        public static string KindName(StagedCameraKind kind) => kind switch
        {
            StagedCameraKind.Orbit => CutsceneCameras.Orbit,
            StagedCameraKind.Crane => CutsceneCameras.Crane,
            StagedCameraKind.Static => CutsceneCameras.Static,
            StagedCameraKind.Sky => CutsceneCameras.Sky,
            StagedCameraKind.Chart => CutsceneCameras.Chart,
            _ => CutsceneCameras.PlayerView,
        };
    }

    /// <summary>
    /// Picks the shot the world can carry (#2468, synthesis §6.3 "Ausweichkette"): the requested camera, then a raised
    /// orbit, a crane, the sky and finally the player's own view. Built on <see cref="CinematicStageScan"/> (unloaded chunks
    /// count as blocked; the landed hull blocks the camera spot) and the prologue's tuned starter-ship numbers, scaled by
    /// the anchor's size (<see cref="PrologueOrbit.Scale"/>). Unity-free so the fallback chain is covered by plain .NET
    /// tests; the director's camera rig flies the result.
    /// </summary>
    public static class CutsceneStaging
    {
        /// <summary>Ring resolution of the orbit pre-scan (5°).</summary>
        public const int RingSamples = 72;

        /// <summary>An orbit needs at least this much open sky to read as one.</summary>
        public const float MinArcSweep = 110f;

        /// <summary>A crane needs at least this much open arc.</summary>
        public const float MinCraneSweep = 60f;

        /// <summary>The ping-pong turns this far before the blocked edge.</summary>
        public const float ArcEdgeMargin = 10f;

        /// <summary>Look target height above the anchor (scaled).</summary>
        public const float LookHeight = 2f;

        /// <summary>
        /// Stages a shot of type <paramref name="cameraType"/> around (<paramref name="cx"/>, <paramref name="cy"/>,
        /// <paramref name="cz"/>). <paramref name="path"/> answers "known air" for the line of sight, <paramref name="spot"/>
        /// for the camera position (terrain and parked hulls); either may be null (no world — only the sky, the chart and
        /// the player's view remain). <paramref name="skyOpen"/>: the player stands under the open sky;
        /// <paramref name="chartAvailable"/>: the star map has arrived.
        /// </summary>
        public static StagedCamera Stage(string? cameraType, CinematicStageScan.ClearSampler? path, CinematicStageScan.ClearSampler? spot,
            float cx, float cy, float cz, float scale, float startYawDeg, bool skyOpen, bool chartAvailable)
        {
            string requested = string.IsNullOrEmpty(cameraType) ? CutsceneCameras.PlayerView : cameraType!;
            float s = Math.Max(1f, scale);
            foreach (var kind in Chain(requested))
            {
                StagedCamera? staged = kind switch
                {
                    StagedCameraKind.Orbit => TryOrbit(path, spot, cx, cy, cz, s, startYawDeg),
                    StagedCameraKind.Crane => TryCrane(path, spot, cx, cy, cz, s),
                    StagedCameraKind.Static => TryStatic(path, spot, cx, cy, cz, s, startYawDeg),
                    StagedCameraKind.Sky => skyOpen ? Simple(StagedCameraKind.Sky, cx, cy, cz, startYawDeg) : null,
                    StagedCameraKind.Chart => chartAvailable ? Simple(StagedCameraKind.Chart, cx, cy, cz, startYawDeg) : null,
                    _ => Simple(StagedCameraKind.PlayerView, cx, cy, cz, startYawDeg),
                };
                if (staged != null)
                {
                    staged.Requested = requested;
                    return staged;
                }
            }

            var view = Simple(StagedCameraKind.PlayerView, cx, cy, cz, startYawDeg);
            view.Requested = requested;
            return view;
        }

        /// <summary>The fallback chain of a camera type, ending in the player's view.</summary>
        public static IReadOnlyList<StagedCameraKind> Chain(string? cameraType) => cameraType switch
        {
            CutsceneCameras.Orbit => new[] { StagedCameraKind.Orbit, StagedCameraKind.Crane, StagedCameraKind.Sky, StagedCameraKind.PlayerView },
            CutsceneCameras.Static => new[] { StagedCameraKind.Static, StagedCameraKind.Orbit, StagedCameraKind.Crane, StagedCameraKind.Sky, StagedCameraKind.PlayerView },
            CutsceneCameras.Crane => new[] { StagedCameraKind.Crane, StagedCameraKind.Orbit, StagedCameraKind.Sky, StagedCameraKind.PlayerView },
            CutsceneCameras.Sky => new[] { StagedCameraKind.Sky, StagedCameraKind.PlayerView },
            CutsceneCameras.Chart => new[] { StagedCameraKind.Chart, StagedCameraKind.Sky, StagedCameraKind.PlayerView },
            _ => new[] { StagedCameraKind.PlayerView }, // player_view, overlay — and an unknown type from a newer pack
        };

        private static StagedCamera Simple(StagedCameraKind kind, float cx, float cy, float cz, float yaw)
            => new StagedCamera { Kind = kind, CenterX = cx, CenterY = cy, CenterZ = cz, StartAngle = yaw };

        /// <summary>The prologue's orbit (#777/#2461): settle ring 16·s, entry ring 22·s two blocks higher, at rising heights;
        /// the widest open arc of both.</summary>
        private static StagedCamera? TryOrbit(CinematicStageScan.ClearSampler? path, CinematicStageScan.ClearSampler? spot,
            float cx, float cy, float cz, float s, float yaw)
        {
            if (path is null || spot is null)
            {
                return null;
            }

            float look = LookHeight * s;
            foreach (float h0 in new[] { 7f, 12f, 18f })
            {
                float h = h0 * s;
                var ring = CinematicStageScan.ScanRing(path, spot, cx, cy, cz, look, 16f * s, h, RingSamples);
                var entry = CinematicStageScan.ScanRing(path, spot, cx, cy, cz, look, 22f * s, h + 2f * s, RingSamples);
                for (int i = 0; i < RingSamples; i++)
                {
                    ring[i] &= entry[i];
                }

                if (CinematicStageScan.TryFindWidestClearArc(ring, MinArcSweep, out float arcCenter, out float sweep))
                {
                    bool limited = sweep < 355f;
                    float half = sweep * 0.5f;
                    return new StagedCamera
                    {
                        Kind = StagedCameraKind.Orbit,
                        CenterX = cx,
                        CenterY = cy,
                        CenterZ = cz,
                        LookHeight = look,
                        StartRadius = 22f * s,
                        StartHeight = h + 2f * s,
                        Radius = 16f * s,
                        Height = h,
                        ArcLimited = limited,
                        ArcCenter = arcCenter,
                        ArcHalf = half,
                        StartAngle = limited ? arcCenter - Math.Max(0f, half - ArcEdgeMargin) * 0.8f : yaw + 160f,
                    };
                }
            }

            return null;
        }

        /// <summary>A near-top-down rise for anchors in craters and canyons: from 14·s up to 26·s at radius 6·s.</summary>
        private static StagedCamera? TryCrane(CinematicStageScan.ClearSampler? path, CinematicStageScan.ClearSampler? spot,
            float cx, float cy, float cz, float s)
        {
            if (path is null || spot is null)
            {
                return null;
            }

            float look = LookHeight * s;
            var high = CinematicStageScan.ScanRing(path, spot, cx, cy, cz, look, 6f * s, 26f * s, 36);
            var low = CinematicStageScan.ScanRing(path, spot, cx, cy, cz, look, 6f * s, 14f * s, 36);
            for (int i = 0; i < high.Length; i++)
            {
                high[i] &= low[i];
            }

            if (!CinematicStageScan.TryFindWidestClearArc(high, MinCraneSweep, out float arcCenter, out _))
            {
                return null;
            }

            return new StagedCamera
            {
                Kind = StagedCameraKind.Crane,
                CenterX = cx,
                CenterY = cy,
                CenterZ = cz,
                LookHeight = look,
                StartRadius = 6f * s,
                StartHeight = 14f * s,
                Radius = 6f * s,
                Height = 26f * s,
                StartAngle = arcCenter,
            };
        }

        /// <summary>A fixed framing 9·s away, tried around the anchor starting behind the player's view, at two heights.</summary>
        private static StagedCamera? TryStatic(CinematicStageScan.ClearSampler? path, CinematicStageScan.ClearSampler? spot,
            float cx, float cy, float cz, float s, float yaw)
        {
            if (path is null || spot is null)
            {
                return null;
            }

            float look = LookHeight * s;
            float r = 9f * s;
            foreach (float h in new[] { 3.5f * s, 6f * s })
            {
                for (int k = 0; k < 8; k++)
                {
                    float angle = yaw + 150f + 45f * k;
                    double rad = angle * Math.PI / 180.0;
                    float px = cx + (float)Math.Sin(rad) * r;
                    float pz = cz + (float)Math.Cos(rad) * r;
                    if (CinematicStageScan.CameraClear(path, spot, cx, cy + look, cz, px, cy + h, pz))
                    {
                        return new StagedCamera
                        {
                            Kind = StagedCameraKind.Static,
                            CenterX = cx,
                            CenterY = cy,
                            CenterZ = cz,
                            LookHeight = look,
                            StartRadius = r,
                            StartHeight = h,
                            Radius = r * 0.92f,
                            Height = h, // a slow push-in keeps the still frame alive
                            StartAngle = angle,
                        };
                    }
                }
            }

            return null;
        }
    }
}
