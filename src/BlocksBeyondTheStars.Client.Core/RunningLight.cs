// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What the main menu's "Join in" button shows about the playtest tasks behind it.</summary>
    public enum JoinInMarkKind
    {
        /// <summary>No task is waiting: the button looks like its neighbours.</summary>
        None,

        /// <summary>Tasks are under way (started, or ready for their report) and none is new: a quiet amber frame.</summary>
        Open,

        /// <summary>A task is new or asks for a re-test: the amber frame with the running light, and their number.</summary>
        New,

        /// <summary>This install has never opened the Test Pilot page: the frame with the running light, without a
        /// number — the game has not asked for the tasks yet, so it knows none.</summary>
        Invite,
    }

    /// <summary>The rule behind the "Join in" button's mark — engine-free, so the headless suite pins it.</summary>
    public static class JoinInMark
    {
        /// <summary>The mark for <paramref name="newCount"/> new (or re-test) tasks and <paramref name="openCount"/> tasks
        /// under way. New wins: it is what the player has not seen yet.</summary>
        public static JoinInMarkKind Kind(int newCount, int openCount)
            => newCount > 0 ? JoinInMarkKind.New : openCount > 0 ? JoinInMarkKind.Open : JoinInMarkKind.None;

        /// <summary>
        /// The mark on an install that may never have opened the Test Pilot page. Such an install fetches no catalogue
        /// when the game starts (nothing phones home without a reason), so it would never learn of a task and the
        /// button would stay plain for exactly the players who have not found the page. Until the first visit the
        /// button therefore invites by itself (<see cref="JoinInMarkKind.Invite"/>) — without asking anyone, and only
        /// where the page can fetch at all (<paramref name="canFetch"/>: a build without the inbox key would lead to
        /// a page that says so). After the first visit the counts decide.
        /// </summary>
        public static JoinInMarkKind Kind(int newCount, int openCount, bool visited, bool canFetch)
        {
            var byCount = Kind(newCount, openCount);
            return byCount == JoinInMarkKind.None && !visited && canFetch ? JoinInMarkKind.Invite : byCount;
        }

        /// <summary>The number on the mark: the count itself up to nine, "9+" beyond — the pill stays two characters wide.</summary>
        public static string CountLabel(int count)
            => count > 9 ? "9+" : Math.Max(0, count).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>One point of a <see cref="RunningLight.Outline"/>: where it is (the rect's centre is the origin, y up),
    /// the outward normal there, and how far along the outline it lies (0 at the start, 1 back at the start).</summary>
    public readonly struct RunningLightPoint
    {
        public RunningLightPoint(float x, float y, float normalX, float normalY, float at)
        {
            X = x;
            Y = y;
            NormalX = normalX;
            NormalY = normalY;
            At = at;
        }

        public float X { get; }

        public float Y { get; }

        public float NormalX { get; }

        public float NormalY { get; }

        public float At { get; }
    }

    /// <summary>
    /// The maths of a light that runs around a rounded button frame: the frame as a closed row of points, and how
    /// bright a point is while the light's head is somewhere on it. Engine-free; <c>UiRunningLight</c> turns it into a
    /// mesh with vertex colours on the default UI material — no shader of its own, so nothing to always-include in the
    /// build and nothing a browser's graphics backend could disagree with.
    /// </summary>
    public static class RunningLight
    {
        /// <summary>Where the head is after <paramref name="seconds"/>, as a fraction of the outline (0…1), for one lap
        /// every <paramref name="period"/> seconds.</summary>
        public static float Head(double seconds, float period)
        {
            if (period <= 0f)
            {
                return 0f;
            }

            double lap = seconds / period;
            return (float)(lap - Math.Floor(lap));
        }

        /// <summary>
        /// The light at outline position <paramref name="at"/> (0…1) while the head is at <paramref name="head"/>: 1 at
        /// the head, falling off along a tail of <paramref name="tail"/> (a fraction of the outline) behind it, 0 on the
        /// rest. A short lead of <paramref name="lead"/> ahead of the head keeps it from being a hard edge. The fall-off
        /// is squared, so the tail thins out instead of ending in a step.
        /// </summary>
        public static float Glow(float at, float head, float tail, float lead = 0.02f)
        {
            float behind = head - at;
            behind -= (float)Math.Floor(behind); // 0…1: how far the point lies behind the head, going back along the outline
            if (tail > 0f && behind <= tail)
            {
                float k = 1f - (behind / tail);
                return k * k;
            }

            float ahead = 1f - behind;
            return lead > 0f && ahead < lead ? 1f - (ahead / lead) : 0f;
        }

        /// <summary>
        /// The outline of a <paramref name="width"/> × <paramref name="height"/> rect with corners of
        /// <paramref name="radius"/>, clockwise from the left end of the top edge, closed (the last point is the first
        /// again, at 1). Straight edges are cut into pieces of at most <paramref name="maxStep"/>: the light is a vertex
        /// colour, and between two far-apart vertices a head in the middle of an edge would not show at all.
        /// </summary>
        public static RunningLightPoint[] Outline(float width, float height, float radius, float maxStep = 6f, int cornerSteps = 6)
        {
            float halfW = Math.Max(0f, width) * 0.5f, halfH = Math.Max(0f, height) * 0.5f;
            float r = Math.Max(0f, Math.Min(radius, Math.Min(halfW, halfH)));
            maxStep = Math.Max(1f, maxStep);
            cornerSteps = Math.Max(1, cornerSteps);

            var points = new List<(float X, float Y, float Nx, float Ny)>();
            // Each side: a straight run from `from` to `to` with one normal, then the corner turning that normal a quarter
            // clockwise around the corner's centre.
            Side(points, -halfW + r, halfH, halfW - r, halfH, 0f, 1f, halfW - r, halfH - r, r, maxStep, cornerSteps);   // top, then top right
            Side(points, halfW, halfH - r, halfW, -halfH + r, 1f, 0f, halfW - r, -halfH + r, r, maxStep, cornerSteps);   // right, then bottom right
            Side(points, halfW - r, -halfH, -halfW + r, -halfH, 0f, -1f, -halfW + r, -halfH + r, r, maxStep, cornerSteps); // bottom, then bottom left
            Side(points, -halfW, -halfH + r, -halfW, halfH - r, -1f, 0f, -halfW + r, halfH - r, r, maxStep, cornerSteps);  // left, then top left
            points.Add(points[0]);

            var lengths = new float[points.Count];
            float total = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                float dx = points[i].X - points[i - 1].X, dy = points[i].Y - points[i - 1].Y;
                total += (float)Math.Sqrt((dx * dx) + (dy * dy));
                lengths[i] = total;
            }

            var outline = new RunningLightPoint[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                float at = i == points.Count - 1 ? 1f : total > 0f ? lengths[i] / total : 0f;
                outline[i] = new RunningLightPoint(points[i].X, points[i].Y, points[i].Nx, points[i].Ny, at);
            }

            return outline;
        }

        private static void Side(List<(float X, float Y, float Nx, float Ny)> points, float fromX, float fromY, float toX, float toY,
            float nx, float ny, float cornerX, float cornerY, float r, float maxStep, int cornerSteps)
        {
            float dx = toX - fromX, dy = toY - fromY;
            float length = (float)Math.Sqrt((dx * dx) + (dy * dy));
            int pieces = Math.Max(1, (int)Math.Ceiling(length / maxStep));
            for (int i = 0; i < pieces; i++) // the run's end point is the corner's first point, added below
            {
                float t = i / (float)pieces;
                points.Add((fromX + (dx * t), fromY + (dy * t), nx, ny));
            }

            // The corner: the normal turns clockwise from (nx, ny) by a quarter; its last point is the next side's first.
            for (int i = 0; i < cornerSteps; i++)
            {
                double a = i / (double)cornerSteps * (Math.PI / 2.0);
                float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
                float cnx = (nx * cos) + (ny * sin), cny = (ny * cos) - (nx * sin); // rotate clockwise by a
                points.Add((cornerX + (cnx * r), cornerY + (cny * r), cnx, cny));
            }
        }
    }
}
