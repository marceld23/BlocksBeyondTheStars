// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One face of a device form in the cell (unit coordinates), corners in the chunk mesher's
    /// <c>FaceQuad</c> order for its direction, so it winds and maps exactly like a cube face of that direction.</summary>
    internal readonly struct DeviceFormFace
    {
        public DeviceFormFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int dir, FaceSide side, int slot, bool boundary)
        {
            A = a;
            B = b;
            C = c;
            D = d;
            Dir = dir;
            Side = side;
            Slot = slot;
            Boundary = boundary;
        }

        public readonly Vector3 A, B, C, D;
        public readonly int Dir;
        public readonly FaceSide Side;
        public readonly int Slot;
        public readonly bool Boundary;
    }

    /// <summary>What one face side of a slot shows, resolved to the atlas. <see cref="Slot"/> 0 = the
    /// device's own cube face on that side.</summary>
    internal struct DeviceTileRef
    {
        public ushort Slot;
        public bool Fit;
        public float U0, V0, U1, V1;
    }

    /// <summary>
    /// A device form resolved for one content snapshot — its faces for each of the four turns
    /// (<see cref="DeviceFormFaces"/>, cached here once per block) and the atlas slot of every tile its slots name. Read
    /// from the mesher's worker threads; never changed after it is built.
    /// </summary>
    internal sealed class DeviceFormInfo
    {
        private const float Eighth = 1f / DeviceForms.Grid;

        public readonly DeviceForm Form;

        /// <summary>[yaw] → the faces of the form turned by that many quarter turns.</summary>
        public readonly DeviceFormFace[][] Faces = new DeviceFormFace[4][];

        /// <summary>Soft edges (ChunkMesher.DeviceSoft.cs): [yaw] → the chamfered polygons of the form, or null when the form
        /// passes the triangle cap (it keeps <see cref="Faces"/>).</summary>
        public readonly DeviceFormFacet[][] SoftFaces;

        /// <summary>[(slot − 1) × 4 + face side] → the tile.</summary>
        private readonly DeviceTileRef[] _tiles;
        private readonly bool[] _glow;
        private readonly float[] _minEmission;

        private DeviceFormInfo(DeviceForm form, DeviceTileRef[] tiles)
        {
            Form = form;
            _tiles = tiles;
            _glow = new bool[form.Slots.Count];
            _minEmission = new float[form.Slots.Count];
            for (int s = 0; s < form.Slots.Count; s++)
            {
                _glow[s] = form.Slots[s].Glow;
                _minEmission[s] = form.Slots[s].MinEmission;
            }

            for (int yaw = 0; yaw < 4; yaw++)
            {
                var quads = DeviceFormFaces.Build(form, yaw);
                var faces = new DeviceFormFace[quads.Count];
                for (int i = 0; i < faces.Length; i++)
                {
                    faces[i] = ToFace(quads[i]);
                }

                Faces[yaw] = faces;
            }

            var soft = DeviceFormSoft.BuildAllTurns(form, LookDeviceSoft.Width, LookDeviceSoft.Cap);
            if (soft != null)
            {
                SoftFaces = new DeviceFormFacet[4][];
                for (int yaw = 0; yaw < 4; yaw++)
                {
                    SoftFaces[yaw] = soft[yaw].ToArray();
                }
            }
        }

        public DeviceTileRef TileOf(int slot, FaceSide side) => _tiles[((slot - 1) * 4) + (int)side];

        /// <summary>The emission of a face with a named tile: the block's own when the slot glows (at least the slot's
        /// floor), else none.</summary>
        public float EmissionOf(int slot, float blockEmission)
            => _glow[slot - 1] ? Mathf.Max(blockEmission, _minEmission[slot - 1]) : 0f;

        /// <summary>Resolves a form against a content snapshot: a tile key that names a block takes that block's slot, a face
        /// tile its slot in the extras band (<paramref name="textureSlots"/>); a key that is neither — a tile missing from this
        /// build — shows the own cube face, sliced, so nothing ever renders grey.</summary>
        public static DeviceFormInfo Resolve(DeviceForm form, GameContent content, IReadOnlyDictionary<string, int> textureSlots)
        {
            var tiles = new DeviceTileRef[form.Slots.Count * 4];
            for (int s = 0; s < form.Slots.Count; s++)
            {
                for (int side = 0; side < 4; side++)
                {
                    var tile = form.Slots[s].For((FaceSide)side);
                    var r = new DeviceTileRef { U0 = tile.U0, V0 = tile.V0, U1 = tile.U1, V1 = tile.V1 };
                    if (tile.IsOwn)
                    {
                        r.Fit = tile.Mode == DeviceTileMode.Fit;
                    }
                    else if (content.GetBlock(tile.Key) is { } block && !block.NumericId.IsAir)
                    {
                        r.Slot = block.NumericId.Value;
                        r.Fit = tile.Mode == DeviceTileMode.Fit;
                    }
                    else if (textureSlots != null && textureSlots.TryGetValue(tile.Key, out int slot))
                    {
                        r.Slot = (ushort)slot;
                        r.Fit = tile.Mode == DeviceTileMode.Fit;
                    }

                    tiles[(s * 4) + side] = r;
                }
            }

            return new DeviceFormInfo(form, tiles);
        }

        private static DeviceFormFace ToFace(DeviceFormQuad q)
        {
            Vector3 Corner(int i)
            {
                var u = BlockCubeFaces.Corner(q.Dir, i); // the unit cube's corner in FaceQuad order (0 or 1 per axis)
                return new Vector3(
                    (q.X0 + (u.x * (q.X1 - q.X0))) * Eighth,
                    (q.Y0 + (u.y * (q.Y1 - q.Y0))) * Eighth,
                    (q.Z0 + (u.z * (q.Z1 - q.Z0))) * Eighth);
            }

            return new DeviceFormFace(Corner(0), Corner(1), Corner(2), Corner(3), q.Dir, q.Side, q.Slot, q.Boundary);
        }
    }
}
