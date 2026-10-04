// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The lifts of the Crystal Net (#2266): a 3×3 platform of <c>lift_platform</c> blocks over each lift motor, drawn and
    /// collided here — the platform is no block of the world grid, the server only owns its height
    /// (<see cref="GameBootstrap.Lifts"/>). While a platform moves the client eases it on at the lift's speed from the last
    /// list (the server sends one about five times a second), and a player standing on it rides along by the platform's
    /// height change: a vertical ride needs no moving frame. The platform is meshed once with the world's block atlas and
    /// only moved afterwards.
    /// </summary>
    public sealed class LiftView : MonoBehaviour
    {
        public GameBootstrap Game;
        public PlayerController Player;

        private sealed class Platform
        {
            public GameObject Go;
            public float ShownY;
        }

        private readonly Dictionary<int, Platform> _platforms = new Dictionary<int, Platform>();
        private Mesh _mesh, _collider;

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            var lifts = Game.Lifts;
            var seen = new HashSet<int>();
            if (lifts != null && !Game.SpaceViewActive)
            {
                float since = Time.unscaledTime - Game.LiftsReceivedAt;
                foreach (var l in lifts)
                {
                    seen.Add(l.Id);
                    if (!_platforms.TryGetValue(l.Id, out var p) || p.Go == null)
                    {
                        p = new Platform { Go = MakePlatform(l.Id), ShownY = l.PlatformY };
                        if (p.Go == null)
                        {
                            continue;
                        }

                        _platforms[l.Id] = p;
                    }

                    float y = l.PlatformY;
                    if (l.Moving)
                    {
                        float delta = l.TargetY - l.PlatformY;
                        float step = Mathf.Min(Mathf.Abs(delta), l.Speed * since);
                        y = l.PlatformY + Mathf.Sign(delta) * step;
                    }

                    float dy = y - p.ShownY;
                    if (Mathf.Abs(dy) > 0.0001f)
                    {
                        CarryRider(l, p.ShownY, dy);
                    }

                    p.ShownY = y;
                    p.Go.SetActive(true);
                    p.Go.transform.position = new Vector3(Game.SceneX(l.X - CrystalNetRules.LiftPlatformRadius), y,
                        Game.SceneZ(l.Z - CrystalNetRules.LiftPlatformRadius));
                }
            }

            if (_platforms.Count > seen.Count)
            {
                var gone = new List<int>();
                foreach (var kv in _platforms)
                {
                    if (!seen.Contains(kv.Key))
                    {
                        gone.Add(kv.Key);
                    }
                }

                foreach (int id in gone)
                {
                    if (_platforms[id].Go != null)
                    {
                        Destroy(_platforms[id].Go);
                    }

                    _platforms.Remove(id);
                }
            }
        }

        /// <summary>The local player standing on this platform rides along by its height change.</summary>
        private void CarryRider(NetLift l, float shownY, float dy)
        {
            if (Player == null)
            {
                return;
            }

            var feet = Player.transform.position;
            float top = shownY + 1f;
            float r = CrystalNetRules.LiftPlatformRadius + 0.5f;
            var centre = Game.ScenePos(l.X + 0.5f, 0f, l.Z + 0.5f);
            if (Mathf.Abs(feet.x - centre.x) <= r && Mathf.Abs(feet.z - centre.z) <= r && feet.y >= top - 0.35f && feet.y <= top + 0.6f)
            {
                Player.RideBy(dy);
            }
        }

        /// <summary>One platform object: the shared 3×3 platform mesh (meshed once) with its collider.</summary>
        private GameObject MakePlatform(int id)
        {
            if (!EnsureMesh())
            {
                return null;
            }

            var go = new GameObject("LiftPlatform " + id);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshCollider>().sharedMesh = _collider; // the collider first (see LandedShipView)
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = Game.ChunkMaterialTransparent != null
                ? new[] { Game.ChunkMaterial, Game.ChunkMaterialTransparent }
                : new[] { Game.ChunkMaterial };
            return go;
        }

        private bool EnsureMesh()
        {
            if (_mesh != null)
            {
                return true;
            }

            var def = Game.Content?.GetBlock("lift_platform");
            if (def == null || Game.ChunkMaterial == null || Game.Atlas == null)
            {
                return false;
            }

            int side = 2 * CrystalNetRules.LiftPlatformRadius + 1;
            var chunk = new ChunkData(new ChunkCoord(0, 0, 0));
            for (int x = 0; x < side; x++)
            {
                for (int z = 0; z < side; z++)
                {
                    chunk.Set(x, 0, z, def.NumericId);
                }
            }

            BlockId CellAt(int x, int y, int z) => y == 0 && x >= 0 && x < side && z >= 0 && z < side ? def.NumericId : BlockId.Air;
            (_mesh, _collider) = ChunkMesher.Build(chunk, Game.Content, CellAt, Game.Atlas);
            return _mesh != null && _mesh.vertexCount > 0;
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_collider != null) Destroy(_collider);
        }
    }
}
