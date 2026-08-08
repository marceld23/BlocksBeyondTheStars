// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The client's view of this save's player-designed forms (#844) — the geometry sibling of
    /// <see cref="PaintDesignAtlas"/>. Holds the bitmaps the server registered (pushed in full on join, then
    /// one message per new or wiped form) and republishes them to <see cref="BlockShapeGeometry"/> as an
    /// immutable snapshot, because the mesher reads them off the main thread.
    ///
    /// Unlike the paint atlas there is no texture: a form is geometry, so a client that meets an unknown id
    /// simply renders a plain cube until the registry message arrives — no pink, no flicker.
    /// </summary>
    public sealed class CustomShapeRegistry
    {
        private readonly Dictionary<int, string> _voxels = new();
        private readonly Dictionary<int, string> _names = new();
        private readonly Dictionary<int, string> _owners = new();

        /// <summary>Fires after a form is registered or wiped, so open UI (the crafting list, the library)
        /// can refresh and the world can be re-meshed.</summary>
        public event System.Action Changed;

        /// <summary>Registers or wipes one form. Empty voxels = wiped: the id is dropped, and every block
        /// still holding it falls back to a plain cube on the next remesh.</summary>
        public void Register(int id, string voxels, string name, string owner)
        {
            if (!ShapeCode.IsCustomShape(id))
            {
                return;
            }

            if (string.IsNullOrEmpty(voxels))
            {
                _voxels.Remove(id);
                _names.Remove(id);
                _owners.Remove(id);
            }
            else
            {
                if (!CustomShape.IsValidVoxels(voxels))
                {
                    return; // a malformed payload would mesh as nonsense — ignore it, the server validates too
                }

                _voxels[id] = voxels;
                _names[id] = string.IsNullOrEmpty(name) ? $"Form {id}" : name;
                _owners[id] = owner ?? string.Empty;
            }

            Publish();
        }

        /// <summary>Bulk registration from the join push (one snapshot publish, not one per form).</summary>
        public void RegisterAll(int[] ids, string[] voxels, string[] names, string[] owners)
        {
            if (ids == null || voxels == null)
            {
                return;
            }

            for (int i = 0; i < ids.Length && i < voxels.Length; i++)
            {
                if (!ShapeCode.IsCustomShape(ids[i]) || !CustomShape.IsValidVoxels(voxels[i]))
                {
                    continue;
                }

                _voxels[ids[i]] = voxels[i];
                _names[ids[i]] = names != null && i < names.Length && !string.IsNullOrEmpty(names[i]) ? names[i] : $"Form {ids[i]}";
                _owners[ids[i]] = owners != null && i < owners.Length ? owners[i] ?? string.Empty : string.Empty;
            }

            Publish();
        }

        /// <summary>The form bitmap registered under a shape index, or false when this save has no such form.</summary>
        public bool TryGetVoxels(int id, out string voxels) => _voxels.TryGetValue(id, out voxels);

        /// <summary>The designer's name for a form (empty when unknown).</summary>
        public string NameOf(int id) => _names.TryGetValue(id, out var name) ? name : string.Empty;

        /// <summary>Who registered the form (empty when unknown) — the attribution a copy keeps.</summary>
        public string OwnerOf(int id) => _owners.TryGetValue(id, out var owner) ? owner : string.Empty;

        /// <summary>Every registered form, lowest id first: (id, name, voxels).</summary>
        public List<(int Id, string Name, string Voxels)> All()
            => _voxels.Keys.OrderBy(id => id).Select(id => (id, NameOf(id), _voxels[id])).ToList();

        /// <summary>Drops everything (session teardown — the next world registers its own forms).</summary>
        public void Clear()
        {
            _voxels.Clear();
            _names.Clear();
            _owners.Clear();
            BlockShapeGeometry.ClearCache();
        }

        private void Publish()
        {
            // Copy-on-write: hand the geometry layer a fresh dictionary it can read from worker threads while
            // this one keeps changing.
            BlockShapeGeometry.PublishCustomShapes(new Dictionary<int, string>(_voxels));
            Changed?.Invoke();
        }
    }
}
