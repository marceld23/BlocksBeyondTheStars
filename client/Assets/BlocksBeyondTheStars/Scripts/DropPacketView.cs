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
    /// so a pile of stone reads as stone from a distance; a packet of something that is no block (fruit,
    /// meat, a component) is its item icon on a little card that faces the camera (#2105).
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
            public bool Card; // an icon card (#2105): faces the camera instead of tumbling
        }

        private readonly Dictionary<string, Packet> _packets = new Dictionary<string, Packet>();
        private bool _subscribed;

        // Shared per-tile cube meshes and per-item tint materials (#1564). Each packet used to instantiate its
        // own remapped Mesh (and every non-block item its own Material) while removal only destroyed the
        // GameObject — so mining with a full backpack leaked a mesh per collected packet for the whole
        // session. Now a tile / item key is built once, every packet shares it, and OnDestroy releases them.
        private readonly Dictionary<Rect, Mesh> _tileMeshes = new Dictionary<Rect, Mesh>();
        private readonly Dictionary<string, Material> _tintMaterials = new Dictionary<string, Material>();
        private readonly Dictionary<string, Material> _iconMaterials = new Dictionary<string, Material>();

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
            var cam = Camera.main;
            foreach (var p in _packets.Values)
            {
                var basePos = Game != null ? Game.ScenePos(p.World.x, p.World.y, p.World.z) : p.World;
                p.Go.transform.position = basePos + Vector3.up * (HoverHeight + Mathf.Sin(t * 1.8f + p.Phase) * 0.07f);
                if (p.Card && cam != null)
                {
                    // The icon card always shows its face (a quad's front is its -Z side, so look AWAY from the camera).
                    p.Spin.rotation = Quaternion.LookRotation(p.Spin.position - cam.transform.position, Vector3.up);
                }
                else
                {
                    p.Spin.localRotation = Quaternion.Euler(18f, t * 45f + p.Phase * 20f, 10f);
                }
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

            // A block shows its tile on a mini-cube; anything else shows its item icon on a card (#2105); an
            // item with neither (no icon generated yet) keeps the old flat-colour cube.
            var def = BlockDefFor(np.TopItem);
            var icon = def == null ? IconResolver.ItemTexture(np.TopItem) : null;
            bool bundle = np.StackCount > 1 || np.TotalCount > 1;
            if (icon != null)
            {
                // Two offset cards so a stack still reads as a *bundle*, like the cubes.
                MakeCard(spin, Vector3.zero, Size, np.TopItem, icon);
                if (bundle)
                {
                    MakeCard(spin, new Vector3(0.18f, 0.22f, 0.12f), Size * 0.72f, np.TopItem, icon);
                }
            }
            else
            {
                // A stack of two offset mini-cubes so it reads as a *bundle* rather than a single lost block.
                MakeCube(spin, Vector3.zero, Size, np.TopItem, def);
                if (bundle)
                {
                    MakeCube(spin, new Vector3(0.16f, 0.26f, -0.1f), Size * 0.72f, np.TopItem, def);
                }
            }

            return new Packet
            {
                Go = go,
                Spin = spin,
                World = new Vector3(np.X + 0.5f, np.Y + 0.5f, np.Z + 0.5f),
                Phase = (np.Id.GetHashCode() & 0x3ff) * 0.01f,
                Card = icon != null,
            };
        }

        /// <summary>One mini-cube wearing the packet's material: the block's atlas tile where the item places
        /// a block, otherwise a flat colour derived from the item key (a tool or component without an icon).</summary>
        private void MakeCube(Transform parent, Vector3 offset, float size, string item, BlocksBeyondTheStars.Shared.Definitions.BlockDefinition def)
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

            if (def != null && Game?.Atlas != null && Game.ChunkMaterial != null && Game.Content != null)
            {
                cube.GetComponent<Renderer>().sharedMaterial = Game.ChunkMaterial;
                var filter = cube.GetComponent<MeshFilter>();
                if (filter != null)
                {
                    filter.sharedMesh = TileMesh(filter.sharedMesh, Game.Atlas.TileUv(def.NumericId.Value),
                        ChunkMesher.MaterialFor(Game.Content, def.NumericId), ChunkMesher.EmissionFor(Game.Content, def.NumericId));
                }
            }
            else
            {
                cube.GetComponent<Renderer>().sharedMaterial = TintMaterial(item);
            }
        }

        /// <summary>One icon card (#2105): a quad wearing the item's generated icon, unlit so the art reads as
        /// drawn; the view turns it toward the camera every frame.</summary>
        private void MakeCard(Transform parent, Vector3 offset, float size, string item, Texture2D icon)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var col = quad.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = offset * size;
            quad.transform.localScale = Vector3.one * size;
            quad.GetComponent<Renderer>().sharedMaterial = IconMaterial(item, icon);
        }

        /// <summary>The cube mesh remapped onto one atlas tile — built once per tile rect and shared by every
        /// packet showing that block (#1564).</summary>
        private Mesh TileMesh(Mesh source, Rect uv, Vector2 material, float emission)
        {
            if (_tileMeshes.TryGetValue(uv, out var cached) && cached != null)
            {
                return cached;
            }

            var mesh = RemapToTile(source, uv, material, emission);
            _tileMeshes[uv] = mesh;
            return mesh;
        }

        /// <summary>The icon-card material for a non-block item — one per item key, shared (#1564).</summary>
        private Material IconMaterial(string item, Texture2D icon)
        {
            string key = item ?? string.Empty;
            if (_iconMaterials.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            if (_iconShader == null)
            {
                _iconShader = Shader.Find("Unlit/Transparent");
            }

            var mat = new Material(_iconShader) { mainTexture = icon };
            _iconMaterials[key] = mat;
            return mat;
        }

        /// <summary>The flat-colour material for a non-block item — one per item key, shared (#1564).</summary>
        private Material TintMaterial(string item)
        {
            string key = item ?? string.Empty;
            if (_tintMaterials.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            if (_litShader == null)
            {
                _litShader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            }

            var mat = new Material(_litShader) { color = ShaderColor.Srgb(HashTint(item)) };
            _tintMaterials[key] = mat;
            return mat;
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

        /// <summary>Copies the primitive cube mesh with its 0..1 UVs rewritten onto one atlas tile — the same
        /// trick the chunk mesher does per face, minus the per-face variation a 0.4 m bundle would never show.
        /// The copy is owned by the caller's cache (<see cref="TileMesh"/>); the shared primitive is never edited.
        /// <para>
        /// The chunk shader reads the packed vertex channels a Unity primitive does not carry (#972): skylight and
        /// tint mode in TEXCOORD1, foliage/tint in TEXCOORD2, block light in TEXCOORD3/4 and gloss / metal / AO /
        /// emission in the vertex colour. Left unset, the skylight reads 0 and the colour white — a half-dark,
        /// fully metallic, glowing cube ("one half is purple", #2105). So every channel is filled the way the
        /// mesher fills a sunlit face of that block.
        /// </para></summary>
        private static Mesh RemapToTile(Mesh source, Rect uv, Vector2 material, float emission)
        {
            var mesh = Instantiate(source);
            var uvs = mesh.uv;
            int n = uvs.Length;
            for (int i = 0; i < n; i++)
            {
                uvs[i] = new Vector2(uv.x + uvs[i].x * uv.width, uv.y + uvs[i].y * uv.height);
            }

            mesh.uv = uvs;

            var sky = new Vector2[n];
            var zero4 = new Vector4[n];
            var colors = new Color[n];
            var col = new Color(material.x, material.y, 1f, emission); // gloss, metal, no AO shade, emission
            for (int i = 0; i < n; i++)
            {
                sky[i] = new Vector2(1f, 0f); // full skylight, plain tint mode
                colors[i] = col;
            }

            mesh.SetUVs(1, sky);
            mesh.SetUVs(2, zero4);
            mesh.SetUVs(3, zero4);
            mesh.SetUVs(4, zero4);
            mesh.colors = colors;
            return mesh;
        }

        private static Shader _litShader;
        private static Shader _iconShader;

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

            // Release the shared meshes/materials — the packet GameObjects go down with this view's own
            // hierarchy, but Mesh/Material assets are only freed by an explicit Destroy (#1564).
            foreach (var mesh in _tileMeshes.Values)
            {
                if (mesh != null) Destroy(mesh);
            }

            foreach (var mat in _tintMaterials.Values)
            {
                if (mat != null) Destroy(mat);
            }

            foreach (var mat in _iconMaterials.Values)
            {
                if (mat != null) Destroy(mat);
            }

            _tileMeshes.Clear();
            _tintMaterials.Clear();
            _iconMaterials.Clear();
        }
    }
}
