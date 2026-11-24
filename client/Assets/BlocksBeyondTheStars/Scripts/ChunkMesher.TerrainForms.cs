// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    // ---------------------------------------------------------------- terrain forms (#3964, LOOK.md §17)
    //
    // A natural terrain cell (TerrainForms in Client.Core decides which, and carves the form) is drawn from merged micro
    // faces instead of its six cube faces. Display only:
    //   - the collider: the build loop records the cell's collider faces with the cube rule, as for a device form;
    //   - neighbours: the cell still counts as a full opaque cube, so its neighbours cull their faces toward it. Where the
    //     form leaves a micro cell empty against such a neighbour, this cell draws the back wall itself. A formed neighbour
    //     in the same chunk is read micro cell by micro cell, so steps and grooves run on across blocks; a neighbour in
    //     another chunk counts as full (this build cannot see its edited marks) — a back wall too many, never a hole;
    //   - carved rock (#4011): a natural cell with an open side into a DUG cell (ChunkData.IsDugLocal — natural terrain a
    //     stored edit replaced: a tunnel, a room cut out of a mountain) keeps its cube, so player-cut walls stay flat;
    //   - tiles and light: each face takes the tile the cube shows on the side it looks to, sliced by position; faces on
    //     the cell's surface take that cube face's light and corner AO, inner faces the light of the cell's first open
    //     side and the device forms' inner shade.
    // Planet streaming only (the `terrainForms` argument): ships, stations, editors and miniatures never form terrain.
    public static partial class ChunkMesher
    {
        /// <summary>Faces one formed cell may emit; the recipes stay far below it (the tests pin their maximum).</summary>
        private const int TerrainFaceBudget = 256;

        private sealed partial class BlockTraits
        {
            private byte[] _terrainFamily;

            /// <summary>The terrain-form family of a block (0 = never formed).</summary>
            public byte TerrainFamilyOf(BlockId id)
            {
                var table = _terrainFamily ?? BuildTerrainFamilies();
                return id.Value < table.Length ? table[id.Value] : (byte)0;
            }

            private byte[] BuildTerrainFamilies()
            {
                var table = new byte[_flags.Length];
                for (int i = 0; i < table.Length; i++)
                {
                    string key = Content.BlockById(new BlockId((ushort)i))?.Key;
                    if (key != null && (_flags[i] & TraitTransparent) == 0)
                    {
                        table[i] = TerrainForms.FamilyOf(key);
                    }
                }

                _terrainFamily = table;
                return table;
            }
        }

        /// <summary>Per-build state of the terrain forms: which cell is formed, its neighbours, its faces.</summary>
        private sealed class TerrainFormContext
        {
            public ChunkData Chunk;
            public Vector3i Origin;
            public System.Func<int, int, int, BlockId> World;
            public BlockTraits Traits;
            public long Salt;
            public System.Func<int, int, int, bool> WorldDug; // #4011: dug cells of the other chunks (null = none known)

            public readonly bool[] Own = new bool[TerrainForms.Cells];
            public readonly bool[][] Side =
            {
                new bool[TerrainForms.Cells], new bool[TerrainForms.Cells], new bool[TerrainForms.Cells],
                new bool[TerrainForms.Cells], new bool[TerrainForms.Cells], new bool[TerrainForms.Cells],
            };

            public readonly byte[] Kinds = new byte[6];
            public readonly List<TerrainForms.Rect3> Rects = new List<TerrainForms.Rect3>(64);

            /// <summary>Whether the world cell — one of this chunk's own — is a formed terrain cell, its form carved into
            /// <paramref name="vox"/>. A cell of another chunk answers false: its edited marks are not known here.</summary>
            public bool FormAt(int wx, int wy, int wz, bool[] vox, out bool topCarved)
            {
                topCarved = false;
                int n = WorldConstants.ChunkSize;
                int lx = wx - Origin.X, ly = wy - Origin.Y, lz = wz - Origin.Z;
                if (lx < 0 || ly < 0 || lz < 0 || lx >= n || ly >= n || lz >= n)
                {
                    return false;
                }

                // Natural ground only: a placed, stamped, snowed-on (#3118), shaped, fronted, painted or dyed cell keeps its cube,
                // and so does natural rock facing a dug cell (#4011, below).
                int idx = WorldConstants.LocalIndex(lx, ly, lz);
                byte family = Traits.TerrainFamilyOf(Chunk.Get(lx, ly, lz));
                if (family == TerrainForms.FamilyNone || Chunk.IsEditedLocal(idx) || Chunk.GetShapeLocal(idx) != 0
                    || Chunk.GetModifierLocal(idx).Tint != 0)
                {
                    return false;
                }

                int open = 0;
                for (int f = 0; f < 6; f++)
                {
                    var d = Faces[f];
                    var nb = World(wx + d.X, wy + d.Y, wz + d.Z);
                    if (nb.IsAir)
                    {
                        if (DugAt(wx + d.X, wy + d.Y, wz + d.Z))
                        {
                            return false; // #4011: rock that faces a carved-out cell is a wall a player cut — it stays flat
                        }

                        open |= 1 << f;
                    }
                    else if ((Traits.FlagsOf(nb) & TraitFluid) != 0)
                    {
                        return false; // shores and sea floors stay cubes
                    }
                }

                return TerrainForms.TrySelect(family, open, wx, wy, wz, Salt, GameLook.TerrainFormsDensity, vox, out topCarved);
            }

            /// <summary>#4011: whether a world cell is a dug cell — this chunk's own mark, else the neighbourhood's.</summary>
            private bool DugAt(int wx, int wy, int wz)
            {
                int n = WorldConstants.ChunkSize;
                int lx = wx - Origin.X, ly = wy - Origin.Y, lz = wz - Origin.Z;
                if (lx >= 0 && ly >= 0 && lz >= 0 && lx < n && ly < n && lz < n)
                {
                    return Chunk.IsDugLocal(WorldConstants.LocalIndex(lx, ly, lz));
                }

                return WorldDug != null && WorldDug(wx, wy, wz);
            }

            /// <summary>The faces of the formed cell (its form already in <see cref="Own"/>) into <see cref="Rects"/>;
            /// <paramref name="exposed"/> bit f = the neighbour across face f leaves a cube face visible (air, glass, a plant, a
            /// shaped block or a device form). False when the faces would pass the budget (the cell then stays a cube).</summary>
            public bool BuildFaces(int wx, int wy, int wz, int exposed)
            {
                for (int f = 0; f < 6; f++)
                {
                    var d = Faces[f];
                    Kinds[f] = (exposed & (1 << f)) != 0 ? TerrainForms.SideOpen
                        : FormAt(wx + d.X, wy + d.Y, wz + d.Z, Side[f], out _) ? TerrainForms.SideFormed
                        : TerrainForms.SideSolid;
                }

                return TerrainForms.BuildFaces(Own, Kinds, Side, Rects, TerrainFaceBudget);
            }
        }

        [System.ThreadStatic] private static TerrainFormContext _terrainFormScratch;

        /// <summary>The build's terrain-form state, or null when this build forms no terrain.</summary>
        private static TerrainFormContext BeginTerrainForms(bool enabled, ChunkData chunk, Vector3i origin,
            System.Func<int, int, int, BlockId> world, BlockTraits traits, System.Func<int, int, int, bool> worldDug)
        {
            if (!enabled)
            {
                return null;
            }

            var c = _terrainFormScratch ??= new TerrainFormContext();
            c.Chunk = chunk;
            c.Origin = origin;
            c.World = world;
            c.Traits = traits;
            c.Salt = GameLook.TerrainFormsSalt;
            c.WorldDug = worldDug;
            return c;
        }

        /// <summary>Emits a formed terrain cell's faces into the opaque submesh with the streams of a device form face.</summary>
        private static void AddTerrainForm(ChunkMeshData data, List<TerrainForms.Rect3> rects, Vector3 cell, DeviceCell c,
            float matR, float matG, float emission, float tintMode, float animCode, Vector4 leaf)
        {
            var fr = _deviceUvScratch ??= new Vector2[4];
            for (int i = 0; i < rects.Count; i++)
            {
                var q = ToTerrainFace(rects[i]);
                int dir = q.Dir;
                int rot = c.Rot[dir];
                fr[0] = TurnFraction(CubeFaceFraction(dir, q.A), rot);
                fr[1] = TurnFraction(CubeFaceFraction(dir, q.B), rot);
                fr[2] = TurnFraction(CubeFaceFraction(dir, q.C), rot);
                fr[3] = TurnFraction(CubeFaceFraction(dir, q.D), rot);
                float mode = tintMode + (c.Anim[dir] ? animCode : 0f);
                EmitDeviceFace(data, cell, q, c.Tile[dir], rot, fr, c, new Color(matR, matG, FaceShade(dir), emission), mode, leaf);
            }
        }

        /// <summary>A cell's closed sides take the light of its first open side (the top first), so the inner faces of a
        /// notch are lit like the open face they belong to.</summary>
        private static void ShareTerrainLight(DeviceCell c)
        {
            int src = -1;
            for (int f = 0; f < 6 && src < 0; f++)
            {
                if (c.Open[f])
                {
                    src = f;
                }
            }

            if (src < 0)
            {
                return;
            }

            for (int f = 0; f < 6; f++)
            {
                if (!c.Open[f])
                {
                    c.Sky[f] = c.Sky[src];
                    c.Light[f] = c.Light[src];
                    c.LightDir[f] = c.LightDir[src];
                }
            }
        }

        private static DeviceFormFace ToTerrainFace(TerrainForms.Rect3 q)
        {
            const float Eighth = 1f / TerrainForms.G;
            Vector3 Corner(int i)
            {
                var u = BlockCubeFaces.Corner(q.Dir, i);
                return new Vector3(
                    (q.X0 + (u.x * (q.X1 - q.X0))) * Eighth,
                    (q.Y0 + (u.y * (q.Y1 - q.Y0))) * Eighth,
                    (q.Z0 + (u.z * (q.Z1 - q.Z0))) * Eighth);
            }

            return new DeviceFormFace(Corner(0), Corner(1), Corner(2), Corner(3), q.Dir, FaceSide.Side, 0, q.Outer);
        }
    }
}
