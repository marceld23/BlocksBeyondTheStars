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

        /// <summary>#2477: a straight, eased move from <see cref="StagedCamera.StartX"/>… to <see cref="StagedCamera.EndX"/>…,
        /// looking at the anchor.</summary>
        Dolly,

        /// <summary>#2477: turning in place at <see cref="StagedCamera.StartX"/>… from <see cref="StagedCamera.StartAngle"/> to
        /// <see cref="StagedCamera.EndAngle"/> (and back), pitched by <see cref="StagedCamera.Pitch"/>.</summary>
        Pan,

        /// <summary>#2477: a high arc around the anchor from <see cref="StagedCamera.StartAngle"/> to
        /// <see cref="StagedCamera.EndAngle"/>, rising by <see cref="StagedCamera.Rise"/> in the middle.</summary>
        Flyover,

        /// <summary>#2477: the synthetic space panorama — needs no terrain, only a quality preset that allows it.</summary>
        SpaceVista,

        /// <summary>#2477: a small synthetic stage (the sleeping core, VEGA's memory chart) — needs no terrain.</summary>
        Diorama,
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

        /// <summary>#2474: a close framing of a small object inside a room (<see cref="CutsceneStaging.TryCloseUp"/>) — flown
        /// with the room's own line of sight (<see cref="CutsceneStaging.ObjectLine"/>).</summary>
        public bool CloseUp { get; set; }

        /// <summary>#2477: where a dolly starts, or the pivot a pan turns on (world coordinates).</summary>
        public float StartX { get; set; }
        public float StartY { get; set; }
        public float StartZ { get; set; }

        /// <summary>#2477: where a dolly ends (world coordinates).</summary>
        public float EndX { get; set; }
        public float EndY { get; set; }
        public float EndZ { get; set; }

        /// <summary>#2477: the last angle of a pan (its yaw) or a flyover (around the anchor), degrees.</summary>
        public float EndAngle { get; set; }

        /// <summary>#2477: a pan's pitch (degrees, positive looks down — Unity's convention).</summary>
        public float Pitch { get; set; }

        /// <summary>#2477: how far a flyover rises above <see cref="Height"/> in the middle of its arc.</summary>
        public float Rise { get; set; }

        /// <summary>Whether the shot takes the camera away from the player.</summary>
        public bool TakesCamera => Kind != StagedCameraKind.PlayerView;

        /// <summary>#2477: whether the shot shows a synthetic stage instead of the world.</summary>
        public bool Synthetic => Kind == StagedCameraKind.SpaceVista || Kind == StagedCameraKind.Diorama;

        /// <summary>#2477: whether the shot is a camera move along a path (dolly, pan, flyover) that
        /// <see cref="CutsceneMoves"/> flies.</summary>
        public bool Moves => Kind == StagedCameraKind.Dolly || Kind == StagedCameraKind.Pan || Kind == StagedCameraKind.Flyover;

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
            StagedCameraKind.Dolly => CutsceneCameras.Dolly,
            StagedCameraKind.Pan => CutsceneCameras.Pan,
            StagedCameraKind.Flyover => CutsceneCameras.Flyover,
            StagedCameraKind.SpaceVista => CutsceneCameras.SpaceVista,
            StagedCameraKind.Diorama => CutsceneCameras.Diorama,
            _ => CutsceneCameras.PlayerView,
        };
    }

    /// <summary>
    /// Picks the shot the world can carry (#2468, synthesis §6.3 "Ausweichkette"): the requested camera, then a raised
    /// orbit, a crane, the sky and finally the player's own view. Built on <see cref="CinematicStageScan"/> (unloaded chunks
    /// count as blocked; the landed hull blocks the camera spot) and the prologue's tuned starter-ship numbers, scaled by
    /// the anchor's size (<see cref="PrologueOrbit.Scale"/>). #2477 adds the moves — a <c>dolly</c> (straight, eased),
    /// a <c>pan</c> (in place) and a <c>flyover</c> (a high arc), each checked along its whole path and falling back to the
    /// still framing of the same place — and the synthetic stages (<c>space_vista</c>, <c>diorama</c>), which need no
    /// terrain but a quality preset that builds them, else the sky. Unity-free so the fallback chain is covered by plain
    /// .NET tests; the director's camera rig flies the result (<see cref="CutsceneMoves"/>, <see cref="SyntheticStages"/>).
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

        /// <summary>The close-up's look target above the object's floor (#2474: the middle of the heal tank).</summary>
        public const float CloseUpLookHeight = 0.8f;

        /// <summary>The close-up's camera distances from the object, the preferred first.</summary>
        private static readonly float[] CloseUpRadii = { 3f, 2.4f, 4f };

        /// <summary>The close-up's camera heights above the object's floor, the preferred (standing eye height) first.</summary>
        private static readonly float[] CloseUpHeights = { 1.6f, 1.1f, 2.2f };

        /// <summary>#2477: the dolly's legs as (start radius, start height, end radius, end height), scaled — the low push-in
        /// first, a higher one over hills and roofs.</summary>
        private static readonly (float R0, float H0, float R1, float H1)[] DollyLegs = { (14f, 5f, 8f, 3f), (18f, 9f, 11f, 6f) };

        /// <summary>#2477: how far the dolly's end slides around the anchor from its start (degrees) — a push-in that also
        /// turns a little reads as a move, not a zoom.</summary>
        public const float DollySlide = 20f;

        /// <summary>#2477: the samples along a dolly or flyover path that must all be clear camera spots with a line of sight
        /// to the anchor (about one per block on the dolly).</summary>
        public const int PathSamples = 12;

        /// <summary>#2477: the in-place pan's pivot above the player's feet — just above the head, so a third-person avatar is
        /// never in the frame.</summary>
        public const float PanPivotHeight = 2.2f;

        /// <summary>#2477: the pan's half sweeps (degrees), the wide one first.</summary>
        private static readonly float[] PanHalves = { 35f, 20f };

        /// <summary>#2477: the in-place pan's centre directions relative to the player's view: ahead, the sides, behind.</summary>
        private static readonly float[] PanTurns = { 0f, 90f, -90f, 180f };

        /// <summary>#2477: how many blocks the view must stay open in every direction of a pan's sweep.</summary>
        public const float PanReach = 6f;

        /// <summary>#2477: the in-place pan looks this far down (degrees, positive = down).</summary>
        public const float PanPitch = 4f;

        /// <summary>#2477: the flyover's radius around the anchor, its base heights (the low one first) and its rise, scaled.</summary>
        public const float FlyoverRadius = 18f;
        private static readonly float[] FlyoverHeights = { 16f, 24f };
        public const float FlyoverRise = 6f;

        /// <summary>#2477: the flyover's sweep around the anchor (degrees).</summary>
        public const float FlyoverSweep = 110f;

        /// <summary>
        /// Stages a shot of type <paramref name="cameraType"/> around (<paramref name="cx"/>, <paramref name="cy"/>,
        /// <paramref name="cz"/>). <paramref name="path"/> answers "known air" for the line of sight, <paramref name="spot"/>
        /// for the camera position (terrain and parked hulls); either may be null (no world — only the sky, the chart, the
        /// synthetic stages and the player's view remain). <paramref name="skyOpen"/>: the player stands under the open sky;
        /// <paramref name="chartAvailable"/>: the star map has arrived. <paramref name="closeUp"/> (#2474): the anchor is a
        /// small object inside a room (<see cref="WantsCloseUp"/>, the heal tank) — a <c>static</c> shot first tries a close
        /// framing inside the room (<see cref="TryCloseUp"/>) before the usual chain, whose wide shots then show the room's
        /// outside (the ship). <paramref name="syntheticAllowed"/> (#2477): the quality preset allows the synthetic stages
        /// (<see cref="CutsceneRules.SyntheticStagesAllowed"/>) and the client could build them — else a <c>space_vista</c> or
        /// <c>diorama</c> falls back to the sky or the player's view.
        /// </summary>
        public static StagedCamera Stage(string? cameraType, CinematicStageScan.ClearSampler? path, CinematicStageScan.ClearSampler? spot,
            float cx, float cy, float cz, float scale, float startYawDeg, bool skyOpen, bool chartAvailable, bool closeUp = false,
            bool syntheticAllowed = true)
        {
            string requested = string.IsNullOrEmpty(cameraType) ? CutsceneCameras.PlayerView : cameraType!;
            float s = Math.Max(1f, scale);
            if (closeUp && requested == CutsceneCameras.Static && TryCloseUp(spot, cx, cy, cz, startYawDeg) is { } close)
            {
                close.Requested = requested;
                return close;
            }

            foreach (var kind in Chain(requested))
            {
                StagedCamera? staged = kind switch
                {
                    StagedCameraKind.Orbit => TryOrbit(path, spot, cx, cy, cz, s, startYawDeg),
                    StagedCameraKind.Crane => TryCrane(path, spot, cx, cy, cz, s),
                    StagedCameraKind.Static => TryStatic(path, spot, cx, cy, cz, s, startYawDeg),
                    StagedCameraKind.Sky => skyOpen ? Simple(StagedCameraKind.Sky, cx, cy, cz, startYawDeg) : null,
                    StagedCameraKind.Chart => chartAvailable ? Simple(StagedCameraKind.Chart, cx, cy, cz, startYawDeg) : null,
                    StagedCameraKind.Dolly => TryDolly(path, spot, cx, cy, cz, s, startYawDeg),
                    StagedCameraKind.Pan => TryPan(path, spot, cx, cy, cz, s, startYawDeg),
                    StagedCameraKind.Flyover => TryFlyover(path, spot, cx, cy, cz, s, startYawDeg),
                    StagedCameraKind.SpaceVista or StagedCameraKind.Diorama
                        => syntheticAllowed ? Simple(kind, cx, cy, cz, startYawDeg) : null,
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
            // #2477: a blocked move becomes the still framing of the same place, then the usual chain.
            CutsceneCameras.Dolly => new[] { StagedCameraKind.Dolly, StagedCameraKind.Static, StagedCameraKind.Orbit, StagedCameraKind.Crane, StagedCameraKind.Sky, StagedCameraKind.PlayerView },
            CutsceneCameras.Pan => new[] { StagedCameraKind.Pan, StagedCameraKind.Static, StagedCameraKind.Sky, StagedCameraKind.PlayerView },
            CutsceneCameras.Flyover => new[] { StagedCameraKind.Flyover, StagedCameraKind.Crane, StagedCameraKind.Orbit, StagedCameraKind.Sky, StagedCameraKind.PlayerView },
            // #2477: a synthetic stage needs no terrain; without it (low preset, a stage that cannot be built) the sky.
            CutsceneCameras.SpaceVista => new[] { StagedCameraKind.SpaceVista, StagedCameraKind.Sky, StagedCameraKind.PlayerView },
            CutsceneCameras.Diorama => new[] { StagedCameraKind.Diorama, StagedCameraKind.Sky, StagedCameraKind.PlayerView },
            _ => new[] { StagedCameraKind.PlayerView }, // player_view, overlay — and an unknown type from a newer pack
        };

        private static StagedCamera Simple(StagedCameraKind kind, float cx, float cy, float cz, float yaw)
            => new StagedCamera { Kind = kind, CenterX = cx, CenterY = cy, CenterZ = cz, StartAngle = yaw };

        /// <summary>Whether an anchor is a small object inside a room that a shot frames up close (#2474: the heal tank in the
        /// ship's medbay) rather than from afar.</summary>
        public static bool WantsCloseUp(string? anchor) => anchor == CutsceneAnchors.HealTank;

        /// <summary>
        /// The line of sight to an object inside a room (#2474): every cell must be open by <paramref name="spot"/> — the
        /// camera-spot sampler, which reads the parked hull as solid, so the line stays inside the room — except the object's
        /// own cells (its floor cell at (<paramref name="cx"/>, <paramref name="cy"/>, <paramref name="cz"/>) and the one
        /// above), which hold the look target. The director flies the close-up with it too.
        /// </summary>
        public static CinematicStageScan.ClearSampler ObjectLine(CinematicStageScan.ClearSampler spot, float cx, float cy, float cz)
        {
            int ox = (int)Math.Floor(cx), oy = (int)Math.Floor(cy), oz = (int)Math.Floor(cz);
            return (x, y, z) => (x == ox && z == oz && (y == oy || y == oy + 1)) || spot(x, y, z);
        }

        /// <summary>
        /// A close framing of a small object inside a room (#2474, VEGA's "What You Are" at the heal tank): the camera
        /// 2.4–4 blocks away at standing height, tried around the object starting behind the player's view; the camera spot
        /// clear by <paramref name="spot"/> (the room, not the hull) and the whole line to the object open
        /// (<see cref="ObjectLine"/>). A slow push-in keeps the still frame alive. Null when the room has no such spot.
        /// </summary>
        public static StagedCamera? TryCloseUp(CinematicStageScan.ClearSampler? spot, float cx, float cy, float cz, float yaw)
        {
            if (spot is null)
            {
                return null;
            }

            var line = ObjectLine(spot, cx, cy, cz);
            foreach (float r in CloseUpRadii)
            {
                foreach (float h in CloseUpHeights)
                {
                    for (int k = 0; k < 12; k++)
                    {
                        float angle = yaw + 150f + 30f * k;
                        double rad = angle * Math.PI / 180.0;
                        float px = cx + (float)Math.Sin(rad) * r;
                        float pz = cz + (float)Math.Cos(rad) * r;
                        if (CinematicStageScan.CameraClear(line, spot, cx, cy + CloseUpLookHeight, cz, px, cy + h, pz))
                        {
                            return new StagedCamera
                            {
                                Kind = StagedCameraKind.Static,
                                CenterX = cx,
                                CenterY = cy,
                                CenterZ = cz,
                                LookHeight = CloseUpLookHeight,
                                StartRadius = r,
                                StartHeight = h,
                                Radius = r * 0.85f, // the push-in toward the tank
                                Height = h,
                                StartAngle = angle,
                                CloseUp = true,
                            };
                        }
                    }
                }
            }

            return null;
        }

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

        /// <summary>
        /// #2477: a straight move between two anchor-relative points — from (angle a, 14·s out, 5·s up) to (a + 20°, 8·s out,
        /// 3·s up), the higher leg when hills or roofs block the low one — tried around the anchor starting behind the
        /// player's view. Every one of <see cref="PathSamples"/> + 1 points along the line must be a clear camera spot with a
        /// line of sight to the anchor (<see cref="CinematicStageScan.CameraClear(CinematicStageScan.ClearSampler, CinematicStageScan.ClearSampler, float, float, float, float, float, float)"/>).
        /// </summary>
        private static StagedCamera? TryDolly(CinematicStageScan.ClearSampler? path, CinematicStageScan.ClearSampler? spot,
            float cx, float cy, float cz, float s, float yaw)
        {
            if (path is null || spot is null)
            {
                return null;
            }

            float look = LookHeight * s;
            foreach (var (r0, h0, r1, h1) in DollyLegs)
            {
                for (int k = 0; k < 12; k++)
                {
                    float a0 = yaw + 150f + 30f * k;
                    float a1 = a0 + DollySlide;
                    Polar(cx, cz, a0, r0 * s, out float x0, out float z0);
                    Polar(cx, cz, a1, r1 * s, out float x1, out float z1);
                    float y0 = cy + h0 * s, y1 = cy + h1 * s;
                    if (!LineClear(path, spot, cx, cy + look, cz, x0, y0, z0, x1, y1, z1))
                    {
                        continue;
                    }

                    return new StagedCamera
                    {
                        Kind = StagedCameraKind.Dolly,
                        CenterX = cx,
                        CenterY = cy,
                        CenterZ = cz,
                        LookHeight = look,
                        StartX = x0,
                        StartY = y0,
                        StartZ = z0,
                        EndX = x1,
                        EndY = y1,
                        EndZ = z1,
                        StartAngle = a0,
                        EndAngle = a1,
                        StartRadius = r0 * s,
                        StartHeight = h0 * s,
                        Radius = r1 * s,
                        Height = h1 * s,
                    };
                }
            }

            return null;
        }

        /// <summary>
        /// #2477: turning in place. First at a pivot just above the player's head (<see cref="PanPivotHeight"/> over the
        /// anchor — "look around"), facing the player's view, then to the sides, then behind; the sweep must stay open
        /// <see cref="PanReach"/> blocks in every sampled direction (a pan into a wall a block away is no shot). Else from a
        /// clear vantage point 9·s from the anchor, sweeping across it. Wide sweep (±35°) before the narrow one (±20°).
        /// </summary>
        private static StagedCamera? TryPan(CinematicStageScan.ClearSampler? path, CinematicStageScan.ClearSampler? spot,
            float cx, float cy, float cz, float s, float yaw)
        {
            if (path is null || spot is null)
            {
                return null;
            }

            float px = cx, py = cy + PanPivotHeight, pz = cz;
            if (CinematicStageScan.SpotClear(spot, px, py, pz))
            {
                foreach (float half in PanHalves)
                {
                    foreach (float turn in PanTurns)
                    {
                        float centre = yaw + turn;
                        // In place the hull counts as a wall (the spot sampler): a cabin wall a block away is no view.
                        if (SweepOpen(spot, px, py, pz, centre, half, PanPitch, PanReach))
                        {
                            return Pan(cx, cy, cz, px, py, pz, centre, half, PanPitch, LookHeight * s);
                        }
                    }
                }
            }

            // A vantage point across the anchor (a ship's centre sits inside its hull — no pivot there).
            float look = LookHeight * s;
            float r = 9f * s, h = 3.5f * s;
            foreach (float half in PanHalves)
            {
                for (int k = 0; k < 8; k++)
                {
                    float angle = yaw + 150f + 45f * k;
                    Polar(cx, cz, angle, r, out float vx, out float vz);
                    float vy = cy + h;
                    if (!CinematicStageScan.CameraClear(path, spot, cx, cy + look, cz, vx, vy, vz))
                    {
                        continue;
                    }

                    float centre = angle + 180f; // facing the anchor
                    float pitch = (float)(Math.Atan2(h - look, r) * 180.0 / Math.PI);
                    // Across the anchor the terrain alone counts (the line of sight's rule): the hull is the subject.
                    if (SweepOpen(path, vx, vy, vz, centre, half, pitch, Math.Min(PanReach, r * 0.6f)))
                    {
                        return Pan(cx, cy, cz, vx, vy, vz, centre, half, pitch, look);
                    }
                }
            }

            return null;
        }

        private static StagedCamera Pan(float cx, float cy, float cz, float px, float py, float pz, float centre, float half,
            float pitch, float look) => new StagedCamera
            {
                Kind = StagedCameraKind.Pan,
                CenterX = cx,
                CenterY = cy,
                CenterZ = cz,
                LookHeight = look,
                StartX = px,
                StartY = py,
                StartZ = pz,
                EndX = px,
                EndY = py,
                EndZ = pz,
                StartAngle = centre - half,
                EndAngle = centre + half,
                Pitch = pitch,
            };

        /// <summary>Whether the view from (<paramref name="px"/>, <paramref name="py"/>, <paramref name="pz"/>) stays open
        /// <paramref name="reach"/> blocks in seven directions across centre ± half at the pitch.</summary>
        private static bool SweepOpen(CinematicStageScan.ClearSampler path, float px, float py, float pz, float centre, float half,
            float pitch, float reach)
        {
            double pr = pitch * Math.PI / 180.0;
            float down = (float)Math.Sin(pr) * reach, flat = (float)Math.Cos(pr) * reach;
            for (int i = 0; i <= 6; i++)
            {
                float a = centre - half + 2f * half * i / 6f;
                Polar(px, pz, a, flat, out float ex, out float ez);
                if (!CinematicStageScan.PathClear(path, px, py, pz, ex, py - down, ez))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// #2477: a high arc around the anchor — <see cref="FlyoverRadius"/>·s out, at 16·s (then 24·s) up and rising by
        /// <see cref="FlyoverRise"/>·s in the middle, over <see cref="FlyoverSweep"/> degrees — always looking at the anchor;
        /// tried starting behind the player's view. Every sampled point of the arc must be a clear camera spot with a line
        /// of sight to the anchor.
        /// </summary>
        private static StagedCamera? TryFlyover(CinematicStageScan.ClearSampler? path, CinematicStageScan.ClearSampler? spot,
            float cx, float cy, float cz, float s, float yaw)
        {
            if (path is null || spot is null)
            {
                return null;
            }

            float look = LookHeight * s;
            float r = FlyoverRadius * s, rise = FlyoverRise * s;
            foreach (float h0 in FlyoverHeights)
            {
                float h = h0 * s;
                for (int k = 0; k < 12; k++)
                {
                    var arc = new StagedCamera
                    {
                        Kind = StagedCameraKind.Flyover,
                        CenterX = cx,
                        CenterY = cy,
                        CenterZ = cz,
                        LookHeight = look,
                        StartAngle = yaw + 150f + 30f * k,
                        EndAngle = yaw + 150f + 30f * k + FlyoverSweep,
                        StartRadius = r,
                        Radius = r,
                        StartHeight = h,
                        Height = h,
                        Rise = rise,
                    };
                    if (ArcClear(path, spot, arc))
                    {
                        return arc;
                    }
                }
            }

            return null;
        }

        private static bool ArcClear(CinematicStageScan.ClearSampler path, CinematicStageScan.ClearSampler spot, StagedCamera arc)
        {
            for (int i = 0; i <= PathSamples; i++)
            {
                CutsceneMoves.Position(arc, (float)i / PathSamples, out float x, out float y, out float z);
                if (!CinematicStageScan.CameraClear(path, spot, arc.CenterX, arc.CenterY + arc.LookHeight, arc.CenterZ, x, y, z))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Every sampled point of the straight line is a clear camera spot that sees the look target.</summary>
        private static bool LineClear(CinematicStageScan.ClearSampler path, CinematicStageScan.ClearSampler spot,
            float lx, float ly, float lz, float x0, float y0, float z0, float x1, float y1, float z1)
        {
            for (int i = 0; i <= PathSamples; i++)
            {
                float t = (float)i / PathSamples;
                if (!CinematicStageScan.CameraClear(path, spot, lx, ly, lz, x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, z0 + (z1 - z0) * t))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The orbit convention: <paramref name="angleDeg"/> around (<paramref name="cx"/>, <paramref name="cz"/>) at
        /// <paramref name="radius"/> — x = sin, z = cos.</summary>
        internal static void Polar(float cx, float cz, float angleDeg, float radius, out float x, out float z)
        {
            double rad = angleDeg * Math.PI / 180.0;
            x = cx + (float)Math.Sin(rad) * radius;
            z = cz + (float)Math.Cos(rad) * radius;
        }
    }
}
