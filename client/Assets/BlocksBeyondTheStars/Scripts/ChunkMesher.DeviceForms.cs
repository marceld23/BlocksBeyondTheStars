// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    // ---------------------------------------------------------------- device forms
    //
    // A device with a form in DeviceForms.Table (Client.Core) is drawn from the form's boxes instead of its six cube faces
    // when the cell's descriptor is a cube (a player's own shape wins) and no design is painted on it (a painted device
    // stays a cube). Display only:
    //   - the collider: the build loop records the device cell's collider faces with the cube's own rule (full unit faces,
    //     toward a neighbour that leaves them open, a formed neighbour not counting) — the plain cube's;
    //   - neighbours: a formed device does not fill its cell, so ShapedNeighbourOpens says so by block id alone
    //     (BlockTraits.DeviceFormOf, the same in every chunk) and the neighbours draw their faces toward it; their collider
    //     faces toward it are not added (FormOnlyOpens);
    //   - orientation: the cube path puts the front picture on face `front` (stored in the descriptor since #2124, else
    //     derived from the neighbours); the form's front (its −Z side) is turned onto that same face
    //     (DeviceFormFaces.YawForFront: −Z → 0, +X → 1, +Z → 2, −X → 3 quarter turns). A device without a front turns its
    //     form toward the open side the cube path would pick for it (CubeFacing.DeriveFront), its tiles stay the cube's;
    //   - tiles: a face of an "own" slot shows what the cube shows on the side the face looks to — the same tile, the same
    //     quarter turns of the top picture, sliced by position with the cube face's own mapping, so a part lying in the
    //     cube face shows exactly the piece of the cube it replaces. A named tile is sliced the same way, or laid whole
    //     onto the face (fit);
    //   - light: each face takes the skylight and block light of the cell it looks into, like a cube face; a face on the
    //     cell's surface takes the cube face's corner AO at its position, an inner face a fixed inner shade.
    public static partial class ChunkMesher
    {
        /// <summary>Shade of a face inside the cell (not on its surface): a little darker than an open cube face, so a
        /// niche or the space under a worktop reads as set back.</summary>
        private const float DeviceInnerShade = 0.88f;

        private sealed partial class BlockTraits
        {
            private DeviceFormInfo[] _deviceForms;

            /// <summary>The resolved device form of a block, null when it has none. Depends on the block id alone, so a neighbour's face decision is the same in every chunk.</summary>
            public DeviceFormInfo DeviceFormOf(BlockId id)
            {
                var table = _deviceForms ?? BuildDeviceForms();
                return id.Value < table.Length ? table[id.Value] : null;
            }

            private DeviceFormInfo[] BuildDeviceForms()
            {
                var table = new DeviceFormInfo[_flags.Length];
                var textureSlots = ShapeFaceTextures.TextureSlots(Content, out _);
                for (int i = 0; i < table.Length; i++)
                {
                    string key = Content.BlockById(new BlockId((ushort)i))?.Key;
                    if (key != null && (_flags[i] & TraitTransparent) == 0 && DeviceForms.TryGet(key, out var form))
                    {
                        table[i] = DeviceFormInfo.Resolve(form, Content, textureSlots);
                    }
                }

                _deviceForms = table;
                return table;
            }
        }

        /// <summary>What the build loop knows about a formed device's cell, per cube face (0..5).</summary>
        private sealed class DeviceCell
        {
            public readonly bool[] Open = new bool[6];
            public readonly float[] Sky = new float[6];
            public readonly Vector3[] Light = new Vector3[6];
            public readonly Vector3[] LightDir = new Vector3[6];
            public readonly Vector4[] Ao = new Vector4[6];
            public readonly Rect[] Tile = new Rect[6];
            public readonly int[] Rot = new int[6];
            public readonly bool[] Anim = new bool[6];

            public void Set(int f, bool open, float sky, Vector3 light, Vector3 lightDir, Vector4 ao)
            {
                Open[f] = open;
                Sky[f] = sky;
                Light[f] = light;
                LightDir[f] = lightDir;
                Ao[f] = ao;
            }

            /// <summary>The tile and quarter turns each cube face shows — the cube loop's choice, restated: the cap tile on
            /// top and bottom, a per-side tile (#2124) where the block names one, the own tile elsewhere; the top picture
            /// turned upright for someone in front. <see cref="Anim"/> = the face shows the block's own tile (#1957).</summary>
            public void SetCubeTiles(BlockTextureAtlas atlas, Rect uv, bool hasCap, Rect capUv, int uvRot,
                CubeFaceTiles cubeTiles, int front)
            {
                for (int f = 0; f < 6; f++)
                {
                    bool vertical = f <= 1;
                    Tile[f] = hasCap && vertical ? capUv : uv;
                    Anim[f] = !(hasCap && vertical);
                    Rot[f] = vertical ? uvRot : 0;
                    if (cubeTiles != null)
                    {
                        ushort sideSlot = cubeTiles.SlotOf(CubeFacing.SideOf(f, front));
                        if (sideSlot != 0)
                        {
                            Tile[f] = atlas.TileUv(sideSlot);
                            Anim[f] = false;
                        }

                        if (f == 0 && front >= 0)
                        {
                            Rot[f] = CubeFacing.TopUvTurns(front);
                        }
                    }
                }
            }
        }

        [System.ThreadStatic] private static DeviceCell _deviceCellScratch;
        [System.ThreadStatic] private static Vector2[] _deviceUvScratch;

        private static DeviceCell DeviceCellScratch() => _deviceCellScratch ??= new DeviceCell();

        /// <summary>The faces of a formed device into the opaque submesh — the streams a cube face
        /// carries (gloss/metal, shade × AO and emission in the colour; skylight and tint mode; tint; block light and its
        /// direction). A face on the cell's surface toward a neighbour that seals that cube face is left out.</summary>
        private static void AddDeviceForm(ChunkMeshData data, BlockTextureAtlas atlas, DeviceFormInfo info, Vector3 cell, int yaw,
            DeviceCell c, float matR, float matG, float emission, float tintMode, float animCode, Vector4 leaf)
        {
            if (info.SoftFaces != null && LookDeviceSoft.Now)
            {
                // Soft edges (ChunkMesher.DeviceSoft.cs, Medium and High on a desktop): the chamfered polygons instead, same tiles, light and culling.
                AddSoftDeviceForm(data, atlas, info, cell, yaw, c, matR, matG, emission, tintMode, animCode, leaf);
                return;
            }

            var faces = info.Faces[yaw & 3];
            var fr = _deviceUvScratch ??= new Vector2[4];
            for (int i = 0; i < faces.Length; i++)
            {
                var q = faces[i];
                int dir = q.Dir;
                if (q.Boundary && !c.Open[dir])
                {
                    continue;
                }

                var t = info.TileOf(q.Slot, q.Side);
                bool own = t.Slot == 0;
                Rect tile = own ? c.Tile[dir] : atlas.TileUv(t.Slot);
                float em = own ? emission : info.EmissionOf(q.Slot, emission);
                int rot = c.Rot[dir];
                DeviceFaceFractions(q, rot, t, fr);
                float mode = tintMode + (own && c.Anim[dir] ? animCode : 0f);
                EmitDeviceFace(data, cell, q, tile, rot, fr, c, new Color(matR, matG, FaceShade(dir), em), mode, leaf);
            }
        }

        /// <summary>The tile fractions of a face's four corners: the cube face's own mapping of the position (u to the right
        /// as seen from outside, v up; the top turned like the cube's top), or — fit — the face's extent laid onto the
        /// tile region.</summary>
        private static void DeviceFaceFractions(in DeviceFormFace q, int rot, DeviceTileRef t, Vector2[] fr)
        {
            fr[0] = TurnFraction(CubeFaceFraction(q.Dir, q.A), rot);
            fr[1] = TurnFraction(CubeFaceFraction(q.Dir, q.B), rot);
            fr[2] = TurnFraction(CubeFaceFraction(q.Dir, q.C), rot);
            fr[3] = TurnFraction(CubeFaceFraction(q.Dir, q.D), rot);
            if (!t.Fit)
            {
                return;
            }

            float uMin = Mathf.Min(Mathf.Min(fr[0].x, fr[1].x), Mathf.Min(fr[2].x, fr[3].x));
            float uMax = Mathf.Max(Mathf.Max(fr[0].x, fr[1].x), Mathf.Max(fr[2].x, fr[3].x));
            float vMin = Mathf.Min(Mathf.Min(fr[0].y, fr[1].y), Mathf.Min(fr[2].y, fr[3].y));
            float vMax = Mathf.Max(Mathf.Max(fr[0].y, fr[1].y), Mathf.Max(fr[2].y, fr[3].y));
            for (int i = 0; i < 4; i++)
            {
                float su = uMax > uMin ? (fr[i].x - uMin) / (uMax - uMin) : 0f;
                float sv = vMax > vMin ? (fr[i].y - vMin) / (vMax - vMin) : 0f;
                fr[i] = new Vector2(Mathf.Lerp(t.U0, t.U1, su), Mathf.Lerp(t.V0, t.V1, sv));
            }
        }

        /// <summary>Where a cell-local point lies on cube face <paramref name="dir"/>'s tile, unturned — the mapping of
        /// <c>FaceQuad</c> + <c>AddFace</c> (corner 0 → 3 is u, corner 0 → 1 is v).</summary>
        private static Vector2 CubeFaceFraction(int dir, Vector3 p) => dir switch
        {
            0 => new Vector2(p.x, p.z),
            1 => new Vector2(p.x, 1f - p.z),
            2 => new Vector2(p.z, p.y),
            3 => new Vector2(1f - p.z, p.y),
            4 => new Vector2(1f - p.x, p.y),
            _ => new Vector2(p.x, p.y),
        };

        /// <summary><c>AddFace</c>'s quarter turns of the UV corners, as a map of a fraction: one turn takes (u, v) to
        /// (v, 1 − u).</summary>
        private static Vector2 TurnFraction(Vector2 f, int rot)
        {
            for (int i = 0; i < (rot & 3); i++)
            {
                f = new Vector2(f.y, 1f - f.x);
            }

            return f;
        }

        /// <summary>One face of a formed device: four vertices with every per-vertex stream, two triangles (the winding of
        /// <c>AddFace</c>). The tangent follows <c>AddFace</c> for the same quarter turns.</summary>
        private static void EmitDeviceFace(ChunkMeshData data, Vector3 cell, in DeviceFormFace q, Rect tile, int rot, Vector2[] fr,
            DeviceCell c, Color col, float mode, Vector4 leaf)
        {
            int dir = q.Dir;
            int baseIndex = data.Verts.Count;
            data.Verts.Add(cell + q.A); data.Verts.Add(cell + q.B); data.Verts.Add(cell + q.C); data.Verts.Add(cell + q.D);
            Vector3 e1 = q.B - q.A, e3 = q.D - q.A;
            Vector3 nrm = Vector3.Cross(e1, e3).normalized;
            Vector3 tan = ((rot & 3) switch { 1 => e1, 2 => -e3, 3 => -e1, _ => e3 }).normalized;
            Vector3 vDir = (rot & 3) switch { 1 => -e3, 2 => -e1, 3 => e3, _ => e1 };
            float hand = Vector3.Dot(Vector3.Cross(nrm, tan), vDir) < 0f ? -1f : 1f;
            var tv = new Vector4(tan.x, tan.y, tan.z, hand);
            Vector4 ao = c.Ao[dir];
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = i == 0 ? q.A : i == 1 ? q.B : i == 2 ? q.C : q.D;
                float shade = q.Boundary ? CornerAo(ao, CubeFaceFraction(dir, p)) : DeviceInnerShade;
                data.Colors.Add(new Color(col.r, col.g, col.b * shade, col.a));
                data.Uvs.Add(new Vector2(tile.xMin + (fr[i].x * tile.width), tile.yMin + (fr[i].y * tile.height)));
                data.Tangents.Add(tv);
                data.SkyUv.Add(new Vector2(c.Sky[dir], mode));
                data.LeafUv.Add(leaf);
                data.BlockLight.Add(c.Light[dir]);
                data.BlockLightDir.Add(c.LightDir[dir]);
            }

            var tris = data.OpaqueTris;
            tris.Add(baseIndex); tris.Add(baseIndex + 1); tris.Add(baseIndex + 2);
            tris.Add(baseIndex); tris.Add(baseIndex + 2); tris.Add(baseIndex + 3);
        }

        /// <summary>The cube face's corner AO (FaceQuad order: (0,0), (0,1), (1,1), (1,0) in its unturned tile fractions)
        /// at a point of the face, bilinear.</summary>
        private static float CornerAo(Vector4 ao, Vector2 f)
        {
            float bottom = Mathf.Lerp(ao.x, ao.w, f.x);
            float top = Mathf.Lerp(ao.y, ao.z, f.x);
            return Mathf.Lerp(bottom, top, f.y);
        }
    }
}
