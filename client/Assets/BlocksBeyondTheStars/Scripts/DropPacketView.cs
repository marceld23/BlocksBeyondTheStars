// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Renders the **ground drop packets** (#853): the little block bundles mining with a full backpack
    /// leaves lying in the world. Each is a small tumbling mini-block wearing its biggest stack's texture,
    /// so a pile of stone reads as stone from a distance.
    /// <para>
    /// Purely presentational — pickup is server-side and automatic (walk near it with room to spare), so
    /// unlike <see cref="NetFragmentView"/> and <see cref="DataCubeView"/> there is no prompt and no key.
    /// The collected items show up in the usual HUD pickup feed via the inventory diff; a packet
    /// disappearing from the list simply pops here.
    /// </para>
    /// </summary>
    public sealed class DropPacketView : MonoBehaviour
    {
        public GameBootstrap Game;

        private sealed class Packet
        {
            public GameObject Go;
            public Transform Spin;
            public Vector3 World;
            public float Phase;
        }

        private readonly Dictionary<string, Packet> _packets = new Dictionary<string, Packet>();
        private bool _subscribed;

        private const float Size = 0.42f;      // a mini block: clearly smaller than a real one
        private const float HoverHeight = 0.3f;

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.DropPacketsReceived += OnPackets;
                _subscribed = true;
            }

            float t = Time.time;
            foreach (var p in _packets.Values)
            {
                var basePos = Game != null ? Game.ScenePos(p.World.x, p.World.y, p.World.z) : p.World;
                p.Go.transform.position = basePos + Vector3.up * (HoverHeight + Mathf.Sin(t * 1.8f + p.Phase) * 0.07f);
                p.Spin.localRotation = Quaternion.Euler(18f, t * 45f + p.Phase * 20f, 10f);
            }
        }

        private void OnPackets(DropPacketList m)
        {
            var seen = new HashSet<string>();
            if (m.Packets != null)
            {
                foreach (var np in m.Packets)
                {
                    seen.Add(np.Id);
                    if (_packets.TryGetValue(np.Id, out var existing))
                    {
                        existing.World = new Vector3(np.X + 0.5f, np.Y + 0.5f, np.Z + 0.5f);
                    }
                    else
                    {
                        _packets[np.Id] = Build(np);
                    }
                }
            }

            if (_packets.Count > seen.Count)
            {
                var stale = new List<string>();
                foreach (var id in _packets.Keys)
                {
                    if (!seen.Contains(id)) stale.Add(id);
                }

                foreach (var id in stale)
                {
                    Destroy(_packets[id].Go);
                    _packets.Remove(id);
                }
            }
        }

        private Packet Build(NetDropPacket np)
        {
            var go = new GameObject($"DropPacket {np.Id}");
            go.transform.SetParent(transform, true);

            var spin = new GameObject("Spin").transform;
            spin.SetParent(go.transform, false);

            // A stack of two offset mini-cubes so it reads as a *bundle* rather than a single lost block.
            MakeCube(spin, Vector3.zero, Size, np.TopItem);
            if (np.StackCount > 1 || np.TotalCount > 1)
            {
                MakeCube(spin, new Vector3(0.16f, 0.26f, -0.1f), Size * 0.72f, np.TopItem);
            }

            return new Packet
            {
                Go = go,
                Spin = spin,
                World = new Vector3(np.X + 0.5f, np.Y + 0.5f, np.Z + 0.5f),
                Phase = (np.Id.GetHashCode() & 0x3ff) * 0.01f,
            };
        }

        /// <summary>One mini-cube wearing the packet's material: the block's atlas tile where the item places
        /// a block, otherwise a flat colour derived from the item key (tools, components, ...).</summary>
        private void MakeCube(Transform parent, Vector3 offset, float size, string item)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var col = cube.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col); // walked over, never bumped into — collecting is proximity, not physics
            }

            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = offset * size;
            cube.transform.localScale = Vector3.one * size;

            var def = BlockDefFor(item);
            if (def != null && Game?.Atlas != null && Game.ChunkMaterial != null)
            {
                cube.GetComponent<Renderer>().sharedMaterial = Game.ChunkMaterial;
                RemapToTile(cube.GetComponent<MeshFilter>(), Game.Atlas.TileUv(def.NumericId.Value));
            }
            else
            {
                if (_litShader == null)
                {
                    _litShader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
                }

                var tint = HashTint(item);
                cube.GetComponent<Renderer>().sharedMaterial = new Material(_litShader) { color = ShaderColor.Srgb(tint) };
            }
        }

        /// <summary>The block an item key stands for — through <c>PlacesBlock</c> and past any dye/glow/shape
        /// modifier, so a painted or shaped drop still shows its material rather than falling back to a swatch.</summary>
        private BlocksBeyondTheStars.Shared.Definitions.BlockDefinition BlockDefFor(string item)
        {
            if (Game?.Content == null || string.IsNullOrEmpty(item))
            {
                return null;
            }

            string plain = BlocksBeyondTheStars.Shared.State.ItemKey.Base(item);
            var def = Game.Content.GetBlock(plain);
            if (def == null && Game.Content.GetItem(plain)?.PlacesBlock is string places && places.Length > 0)
            {
                def = Game.Content.GetBlock(places);
            }

            return def;
        }

        /// <summary>Rewrites the primitive cube's 0..1 UVs onto one atlas tile — the same trick the chunk
        /// mesher does per face, minus the per-face variation a 0.4 m bundle would never show.</summary>
        private static void RemapToTile(MeshFilter filter, Rect uv)
        {
            if (filter == null)
            {
                return;
            }

            var mesh = Instantiate(filter.sharedMesh); // never edit the shared primitive mesh
            var uvs = mesh.uv;
            for (int i = 0; i < uvs.Length; i++)
            {
                uvs[i] = new Vector2(uv.x + uvs[i].x * uv.width, uv.y + uvs[i].y * uv.height);
            }

            mesh.uv = uvs;
            filter.sharedMesh = mesh;
        }

        private static Shader _litShader;

        /// <summary>Stable pseudo-colour for a non-block item, so two packets of the same thing look alike.</summary>
        private static Color HashTint(string item)
        {
            int h = string.IsNullOrEmpty(item) ? 0 : item.GetHashCode();
            float hue = ((h & 0x7fffffff) % 360) / 360f;
            return Color.HSVToRGB(hue, 0.35f, 0.75f);
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.DropPacketsReceived -= OnPackets;
            }
        }
    }
}
