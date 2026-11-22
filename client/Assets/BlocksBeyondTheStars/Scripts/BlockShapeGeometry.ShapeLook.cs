// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Player forms with colour slots and soft edges. The VISIBLE faces of a form come from the per-slot merge of its
    /// cells (<see cref="ShapeColourSlots"/>) and, where the tier allows, from the chamfer on the convex outer edges
    /// (<see cref="ShapeChamfer"/>, <see cref="SoftShare"/> of a micro cell = 6 cm on the fine grid, never wider than the
    /// cube bevel); the COLLIDER keeps the value-blind micro boxes (<see cref="ColliderFaces"/>), so physics, the
    /// server's budget and the format do not move.
    /// <para>
    /// Tiers: the chamfer is built on a desktop client on Medium and High; Low, Potato and the browser keep hard edges
    /// (the soft border of the block shader still rounds a form where it reaches the cell's border). Colour slots cost no
    /// geometry beyond the per-slot boxes (capped at <see cref="CustomShape.MaxBoxes"/>) and are on every tier.
    /// </para>
    /// </summary>
    internal static partial class BlockShapeGeometry
    {
        /// <summary>The chamfer width as a share of a micro cell.</summary>
        internal const float SoftShare = ShapeChamfer.DefaultShare;

        /// <summary>Marks a cache key whose faces were built with the chamfer — the tier can change while a world runs.</summary>
        private const int SoftKeyBit = 1 << 30;

        /// <summary>[cache key] → the collider faces of a player form, or the visible list itself when both are the same.</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, List<Face>> _colliderCache = new();

        /// <summary>True when player forms get soft edges now: the preset is Medium or High on a desktop client (never in
        /// the browser).</summary>
        internal static bool SoftEdgesNow => SoftTierAllows;

        /// <summary>The tier rule of every soft edge on a form (player forms here, device forms in ChunkMesher.DeviceSoft.cs):
        /// Medium or High on a desktop client, never in the browser.</summary>
        internal static bool SoftTierAllows
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return false;
#else
                return FxKit.Preset >= QualityPreset.Medium;
#endif
            }
        }

        private static int CacheKey(int shapeIndex, int orientation, int upFace, int cell, out bool soft)
        {
            soft = ShapeCode.IsCustomShape(shapeIndex) && SoftEdgesNow;
            int key = ((cell & 0xF) << 11) | (shapeIndex << 5) | ((upFace & 7) << 2) | (orientation & 3);
            return soft ? key | SoftKeyBit : key;
        }

        /// <summary>The visible faces of one cell of a player form (unturned): per-slot boxes, chamfered when
        /// <paramref name="soft"/> — the polygons a preview can use as well (<see cref="LookFacets"/>).</summary>
        private static void AddCustomLookFaces(List<Face> faces, string cellVoxels, bool soft)
        {
            foreach (var facet in LookFacets(cellVoxels, soft))
            {
                faces.Add(ToFace(facet));
            }
        }

        /// <summary>The polygons the world draws for one cell bitmap of a player form, in the unit cell and unturned, each
        /// with its colour slot — for the editors' previews too, so they show what the world shows.</summary>
        internal static List<ShapeFacet> LookFacets(string cellVoxels, bool soft)
        {
            var visible = ShapeColourSlots.VisibleBoxes(cellVoxels, true);
            float width = soft ? ShapeChamfer.WidthFor(SoftShare, CustomShape.GridOf(cellVoxels), ChunkMesher.BevelAmount) : 0f;
            return width > 0f ? ShapeChamfer.BuildCapped(visible, width) : ShapeChamfer.Plain(visible);
        }

        internal static Face ToFace(ShapeFacet f)
            => new Face(V(f.A), V(f.B), V(f.C), V(f.D), f.IsQuad, U(f.UvA), U(f.UvB), U(f.UvC), U(f.UvD), f.Slot);

        private static Vector3 V(System.Numerics.Vector3 p) => new Vector3(p.X, p.Y, p.Z);

        private static Vector2 U(System.Numerics.Vector2 p) => new Vector2(p.X, p.Y);

        /// <summary>
        /// The faces the collider takes for a shape — <paramref name="visible"/> itself for everything but a player form,
        /// and for a player form (drawn with colour slots and soft edges) its value-blind micro boxes (turned like the
        /// visible ones). When those are the same as the visible faces the visible list comes back, so the mesher keeps
        /// its single pass.
        /// </summary>
        public static List<Face> ColliderFaces(int shapeIndex, int orientation, int upFace, int cell, List<Face> visible)
        {
            if (visible == null || !ShapeCode.IsCustomShape(shapeIndex))
            {
                return visible;
            }

            int key = CacheKey(shapeIndex, orientation, upFace, cell, out _);
            if (_colliderCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var plain = BuildPlainCustom(shapeIndex, orientation, upFace, cell);
            var result = plain == null || SameFaces(plain, visible) ? visible : plain;
            _colliderCache[key] = result;
            return result;
        }

        private static List<Face> BuildPlainCustom(int shapeIndex, int orientation, int upFace, int cell)
        {
            if (!_customVoxels.TryGetValue(shapeIndex, out string voxels))
            {
                return null;
            }

            string cellVoxels = CustomShape.CellVoxels(voxels, cell);
            if (cellVoxels.Length == 0)
            {
                return null;
            }

            var faces = new List<Face>();
            foreach (var box in CustomShape.Merge(cellVoxels))
            {
                MicroBox(faces, box);
            }

            // The same finish, yaw and tilt as BuildUncached, so the collider lies exactly under the visible faces.
            for (int i = 0; i < faces.Count; i++)
            {
                var face = faces[i].Finish(faces[i].Part);
                if ((orientation & 3) != 0)
                {
                    face = face.Map(p => Yaw(p, orientation));
                }

                if (upFace != 0)
                {
                    face = face.Map(p => Tilt(p, upFace));
                }

                faces[i] = face;
            }

            return faces;
        }

        private static bool SameFaces(List<Face> a, List<Face> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].IsQuad != b[i].IsQuad || a[i].A != b[i].A || a[i].B != b[i].B || a[i].C != b[i].C || a[i].D != b[i].D
                    || a[i].ColourSlot > 1 || b[i].ColourSlot > 1)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
