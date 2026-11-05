// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3233: the headlamp shadows of the High preset (desktop) — a 64³ single-channel 3D texture of the blocks around the
    /// camera (<see cref="LampOccupancy"/>), which the lit shaders march toward each lamp (<c>Headlamp.hlsl</c>). No shadow
    /// map and no extra geometry pass: the client already holds the blocks. The window follows the camera in 8-cell steps
    /// and only refills the slabs that came into it; one 16³ brick is re-read per frame (the whole volume about once a
    /// second), which picks up streamed chunks and mined or placed blocks. The texture is uploaded only when a cell changed.
    /// Runs only while a lamp is lit and <see cref="HighDetails.LampShadows"/> is on; otherwise the shaders get "off" and
    /// skip the march with one uniform compare.
    /// </summary>
    public sealed class LampVolume : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private static readonly int VolumeId = Shader.PropertyToID("_Sc_LampVolume");
        private static readonly int VoxelId = Shader.PropertyToID("_Sc_LampVoxel");

        private readonly List<CellBox> _dirty = new List<CellBox>();
        private Texture3D _texture;
        private LampOccupancy _occupancy;
        private int _epoch = -1;
        private GameContent _content;
        private byte[] _opacity;
        private System.Func<ushort, int, byte> _lookup;
        private int _brick;
        private bool _published;
        private bool _forceUpload;

        private void Awake() => _lookup = (id, shape) => ChunkMesher.LampOpacity(_opacity, id, shape);

        private void LateUpdate()
        {
            bool want = HighDetails.LampShadows && Headlamp.AnyLit && Game != null && Camera != null && Game.World != null
                        && Game.Content != null && !Game.SpaceViewActive && SystemInfo.supports3DTextures;
            if (!want)
            {
                Unpublish();
                return;
            }

            Prepare();
            var c = Camera.transform.position;
            _occupancy.Recenter(Mathf.FloorToInt(c.x), Mathf.FloorToInt(c.y), Mathf.FloorToInt(c.z), _dirty);
            bool changed = false;
            foreach (var box in _dirty)
            {
                changed |= _occupancy.Fill(box, Game.World, _lookup);
            }

            _dirty.Clear();
            changed |= _occupancy.Fill(_occupancy.Brick(_brick++), Game.World, _lookup);
            if (changed || _forceUpload)
            {
                _texture.SetPixelData(_occupancy.Cells, 0);
                _texture.Apply(false);
                _forceUpload = false;
            }

            Shader.SetGlobalTexture(VolumeId, _texture);
            Shader.SetGlobalVector(VoxelId, new Vector4(_occupancy.OriginX, _occupancy.OriginY, _occupancy.OriginZ, 1f));
            _published = true;
        }

        /// <summary>The texture once, the opacity table per content snapshot, a fresh window per world (the client reuses
        /// its <see cref="ClientWorld"/> across worlds, so the world epoch decides) and after every pause — a fresh window
        /// is uploaded whole even when it reads all open, so no earlier contents survive in the texture.</summary>
        private void Prepare()
        {
            if (_texture == null)
            {
                _texture = new Texture3D(LampOccupancy.Size, LampOccupancy.Size, LampOccupancy.Size, TextureFormat.R8, false)
                {
                    name = "LampVolume",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Repeat, // the cells are stored by world coordinate mod 64 (LampOccupancy)
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }

            if (!ReferenceEquals(_content, Game.Content))
            {
                _content = Game.Content;
                _opacity = ChunkMesher.LampOpacityTable(_content);
                _occupancy = null; // the ids may mean other blocks now
            }

            if (_occupancy == null || _epoch != Game.WorldEpoch)
            {
                _epoch = Game.WorldEpoch;
                _occupancy = new LampOccupancy();
                _forceUpload = true;
            }
        }

        private void Unpublish()
        {
            if (_published)
            {
                Shader.SetGlobalVector(VoxelId, Vector4.zero);
                _published = false;
                _occupancy = null; // blocks may change while no lamp is lit: start from a full read next time
            }
        }

        private void OnDisable() => Unpublish();

        private void OnDestroy()
        {
            Unpublish();
            if (_texture != null)
            {
                Destroy(_texture);
            }
        }
    }
}
