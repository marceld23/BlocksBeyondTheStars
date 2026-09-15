// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One resolved texture slot of a block's built-in form: which tile a part's face shows and, when
    /// <see cref="Stretch"/>, the region (texture coordinates, v up) the face is stretched onto.</summary>
    internal struct FaceSlot
    {
        public bool Valid;
        public ushort TileId; // 0 = the block's own tile
        public bool Stretch;
        public float U0, V0, U1, V1;
    }

    /// <summary>
    /// #1900: texture slots of blocks rendered as built-in forms (<see cref="BlockFaceTexture"/> in data/blocks.json),
    /// resolved once per content snapshot into a flat per-block table the chunk mesher reads from worker threads.
    /// A tile that is a picture of an object — the bed seen from above — used to be drawn whole on every face of the
    /// form; its slots now give the mattress tops one half of the drawing each and the boards the drawn frame.
    /// </summary>
    internal static class ShapeFaceTextures
    {
        private sealed class Table
        {
            public readonly GameContent Content;
            public readonly FaceSlot[][] Slots;

            public Table(GameContent content)
            {
                Content = content;
                int size = 1;
                foreach (var b in content.Blocks.Values)
                {
                    size = Mathf.Max(size, b.NumericId.Value + 1);
                }

                Slots = new FaceSlot[size][];
                foreach (var b in content.Blocks.Values)
                {
                    Slots[b.NumericId.Value] = Resolve(content, b);
                }
            }
        }

        private static volatile Table _table;
        private static readonly object TableLock = new object();

        /// <summary>The slots of a block indexed by part × side, or null when it declares none.</summary>
        public static FaceSlot[] SlotsFor(GameContent content, BlockId id)
        {
            if (content == null)
            {
                return null;
            }

            var t = _table;
            if (t == null || !ReferenceEquals(t.Content, content))
            {
                lock (TableLock)
                {
                    t = _table;
                    if (t == null || !ReferenceEquals(t.Content, content))
                    {
                        t = new Table(content);
                        _table = t;
                    }
                }
            }

            return id.Value < t.Slots.Length ? t.Slots[id.Value] : null;
        }

        private static FaceSlot[] Resolve(GameContent content, BlockDefinition def)
        {
            if (def.Faces == null || def.Faces.Count == 0)
            {
                return null;
            }

            var slots = new FaceSlot[ShapeParts.PartCount * ShapeParts.SideCount];
            for (int p = 0; p < ShapeParts.PartCount; p++)
            {
                for (int s = 0; s < ShapeParts.SideCount; s++)
                {
                    var face = BlockFaceTextures.Resolve(def.Faces, (ShapePart)p, (FaceSide)s);
                    if (face == null)
                    {
                        continue;
                    }

                    var slot = new FaceSlot
                    {
                        Valid = true,
                        TileId = face.Tile != null ? content.GetBlock(face.Tile)?.NumericId.Value ?? (ushort)0 : (ushort)0,
                    };
                    if (face.Rect != null && face.Rect.Length == 4)
                    {
                        (slot.U0, slot.V0, slot.U1, slot.V1) = BlockFaceTextures.ToUv(face.Rect);
                        slot.Stretch = true;
                    }

                    slots[p * ShapeParts.SideCount + s] = slot;
                }
            }

            return slots;
        }

        /// <summary>The atlas texture coordinates of a finished form face: the slice of <paramref name="tile"/> it covers,
        /// or — when the block has a slot for the face's part and side — the slot's tile, stretched over its region.</summary>
        public static void FaceUvs(in BlockShapeGeometry.Face face, Rect tile, FaceSlot[] slots, BlockTextureAtlas atlas,
            out Vector2 a, out Vector2 b, out Vector2 c, out Vector2 d)
        {
            Vector2 ua = face.UvA, ub = face.UvB, uc = face.UvC, ud = face.IsQuad ? face.UvD : face.UvC;
            if (slots != null)
            {
                var slot = slots[(int)face.Part * ShapeParts.SideCount + (int)face.Side];
                if (slot.Valid)
                {
                    if (slot.TileId != 0 && atlas != null)
                    {
                        tile = atlas.TileUv(slot.TileId);
                    }

                    if (slot.Stretch)
                    {
                        float uMin = Mathf.Min(Mathf.Min(ua.x, ub.x), Mathf.Min(uc.x, ud.x));
                        float uMax = Mathf.Max(Mathf.Max(ua.x, ub.x), Mathf.Max(uc.x, ud.x));
                        float vMin = Mathf.Min(Mathf.Min(ua.y, ub.y), Mathf.Min(uc.y, ud.y));
                        float vMax = Mathf.Max(Mathf.Max(ua.y, ub.y), Mathf.Max(uc.y, ud.y));
                        Vector2 Region(Vector2 p) => new Vector2(
                            Mathf.Lerp(slot.U0, slot.U1, uMax > uMin ? (p.x - uMin) / (uMax - uMin) : 0f),
                            Mathf.Lerp(slot.V0, slot.V1, vMax > vMin ? (p.y - vMin) / (vMax - vMin) : 0f));
                        ua = Region(ua);
                        ub = Region(ub);
                        uc = Region(uc);
                        ud = Region(ud);
                    }
                }
            }

            a = InTile(tile, ua);
            b = InTile(tile, ub);
            c = InTile(tile, uc);
            d = InTile(tile, ud);
        }

        private static Vector2 InTile(Rect tile, Vector2 fraction)
            => new Vector2(tile.xMin + fraction.x * tile.width, tile.yMin + fraction.y * tile.height);
    }
}
