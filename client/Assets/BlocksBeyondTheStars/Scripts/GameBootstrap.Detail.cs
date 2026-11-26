// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// GameBootstrap's detail pass (#4046): one walk per frame over the chunks that carry ground detail or near-drawer
    /// data — tufts (<see cref="GroundScatter"/>), the grass carpet and its flowers (<see cref="GrassShells"/>), grass
    /// clumps (<see cref="GrassClumps"/>), plant bodies (<see cref="PlantBodies"/>) and crown clusters
    /// (<see cref="CrownClusters"/>). Before, each of the five ran its own <c>Update()</c> on every loaded chunk every
    /// frame, also far out of its range; at view distance 8 that was several thousand script calls a frame that each read
    /// a few engine properties only to return. Here a chunk out of range costs one distance sum in managed code, a preset
    /// or a platform on which no drawer runs costs nothing per chunk, and what is drawn — the same instanced calls, by the
    /// drawers' own rules — does not change.
    /// <para>What is left is the hand-over itself: every instanced call copies its matrices and per-instance values to
    /// the engine. Measured on the reference laptop on High (2026-11-26): about 130 chunks in reach, 2.6 ms per frame.
    /// Skipping the chunks outside the camera's view was tried and dropped — a third of the chunks, 4 % of the time,
    /// because the crowns and plants beside and behind the player still cast their shadows into the view and so are
    /// handed over all the same. The lever that is left is instance data that stays on the graphics card.</para>
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>The chunk views with data for at least one drawer (their <c>ChunkView.DetailIndex</c> is the slot here).</summary>
        private readonly List<ChunkView> _detailViews = new List<ChunkView>();

        /// <summary>Chunks that carry data for at least one drawer (diagnostics).</summary>
        public int DetailChunkCount => _detailViews.Count;

        /// <summary>The last frame's chunks inside the widest reach of a drawer (diagnostics).</summary>
        public int DetailChunksInReach { get; private set; }

        /// <summary>Puts a view into the detail pass when one of its drawers holds data, and takes it out when none does.
        /// Called after every upload (the drawers' data comes with the mesh build).</summary>
        private void TrackDetail(ChunkView view)
        {
            bool has = (view.Scatter != null && view.Scatter.HasData) || (view.Shells != null && view.Shells.HasData)
                || (view.Clumps != null && view.Clumps.HasData) || (view.Plants != null && view.Plants.HasData)
                || (view.Crowns != null && view.Crowns.HasData);
            if (has == (view.DetailIndex >= 0))
            {
                return;
            }

            if (has)
            {
                view.DetailIndex = _detailViews.Count;
                _detailViews.Add(view);
            }
            else
            {
                ForgetDetail(view);
            }
        }

        /// <summary>Takes a view out of the detail pass (it is being destroyed, or its last drawer ran out of data).</summary>
        private void ForgetDetail(ChunkView view)
        {
            int slot = view.DetailIndex;
            if (slot < 0)
            {
                return;
            }

            int last = _detailViews.Count - 1;
            var moved = _detailViews[last];
            _detailViews[slot] = moved;
            moved.DetailIndex = slot;
            _detailViews.RemoveAt(last);
            view.DetailIndex = -1;
        }

        /// <summary>Update's detail pass. Every drawer's once-a-frame step runs first (camera, range, the budgets that
        /// steer the ranges), whether or not a chunk is in reach; then each chunk within the widest reach hands itself to
        /// the drawers whose own reach it is inside. The pre-check uses the chunk's box one block taller than it is (the
        /// carpet stands on a lid cell), so it never drops a chunk a drawer's own test would have drawn.</summary>
        private void DrawChunkDetail()
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            float scatter = GroundScatter.FrameReach(), shells = GrassShells.FrameReach(), clumps = GrassClumps.FrameReach();
            float plants = PlantBodies.FrameReach(), crowns = CrownClusters.FrameReach();
            float reach = Mathf.Max(Mathf.Max(scatter, shells), Mathf.Max(clumps, Mathf.Max(plants, crowns)));
            var cam = reach > 0f && _detailViews.Count > 0 ? Camera.main : null;
            int inReach = 0;
            if (cam != null)
            {
                Vector3 eye = cam.transform.position;
                float all = Reach(reach), s = Reach(scatter), g = Reach(shells), c = Reach(clumps), p = Reach(plants), k = Reach(crowns);
                for (int i = 0; i < _detailViews.Count; i++)
                {
                    var view = _detailViews[i];
                    float d = DistanceSqToChunkBox(eye, view.ScenePos);
                    if (d > all)
                    {
                        continue;
                    }

                    inReach++;
                    if (d <= s && view.Scatter != null && view.Scatter.HasData) { view.Scatter.Draw(); }
                    if (d <= g && view.Shells != null && view.Shells.HasData) { view.Shells.Draw(); }
                    if (d <= c && view.Clumps != null && view.Clumps.HasData) { view.Clumps.Draw(); }
                    if (d <= p && view.Plants != null && view.Plants.HasData) { view.Plants.Draw(); }
                    if (d <= k && view.Crowns != null && view.Crowns.HasData) { view.Crowns.Draw(); }
                }
            }

            DetailChunksInReach = inReach;
            ChunkWorkStats.DetailPass.AddSince(started);
        }

        /// <summary>A drawer's reach as the squared distance the pre-check compares with: one block of slack, and below
        /// zero for a drawer that draws nothing this frame.</summary>
        private static float Reach(float range) => range > 0f ? (range + 1f) * (range + 1f) : -1f;

        /// <summary>Squared distance from a point to a chunk's box, one block taller than the chunk.</summary>
        private static float DistanceSqToChunkBox(Vector3 p, Vector3 origin)
        {
            const float n = WorldConstants.ChunkSize;
            float dx = Mathf.Max(0f, Mathf.Max(origin.x - p.x, p.x - (origin.x + n)));
            float dy = Mathf.Max(0f, Mathf.Max(origin.y - p.y, p.y - (origin.y + n + 1f)));
            float dz = Mathf.Max(0f, Mathf.Max(origin.z - p.z, p.z - (origin.z + n)));
            return (dx * dx) + (dy * dy) + (dz * dz);
        }
    }
}
