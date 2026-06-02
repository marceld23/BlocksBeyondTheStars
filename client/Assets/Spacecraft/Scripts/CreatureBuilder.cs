using System.Collections.Generic;
using Spacecraft.Networking.Messages;
using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Builds a blocky creature from a server <see cref="NetCreature"/> descriptor — the parametric
    /// counterpart to <see cref="PlayerAvatar"/> (cubes in code, no art asset). The same descriptor
    /// always yields the same body, so every client draws a species identically. Body segments,
    /// head, legs, optional wings/tail, colour and a bioluminescent glow all come from the species.
    /// The server stays authoritative over which creatures exist and where; this is render-only.
    /// </summary>
    public sealed class CreatureBuilder
    {
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private Material _bodyMat;
        private Light _glow;

        /// <summary>Builds the body under <paramref name="root"/> from the descriptor.</summary>
        public void Build(GameObject root, NetCreature c)
        {
            EnsureTextures();
            float unit = 0.5f * Mathf.Clamp(c.Size, 0.4f, 3f);
            Color baseColor = Rgb(c.ColorRgb);

            // Hostiles read a touch more aggressive; sleepers are dimmed.
            if (c.Hostile)
            {
                baseColor = Color.Lerp(baseColor, new Color(0.85f, 0.2f, 0.15f), 0.25f);
            }

            if (c.Asleep)
            {
                baseColor *= 0.6f;
            }

            _bodyMat = Lit(c.Glows ? baseColor * 1.6f : baseColor, PickHide(c));

            // Body: a row of segments along +Z (forward). The front segment is the head.
            int segments = Mathf.Clamp(c.BodySegments, 1, 4);
            float segLen = unit * 1.1f;
            float bodyY = unit * (c.Legs > 0 ? 1.1f : 0.7f);
            for (int i = 0; i < segments; i++)
            {
                float z = (i - (segments - 1) * 0.5f) * segLen;
                float taper = 1f - 0.12f * i; // slimmer toward the tail
                AddPart(root, "Body" + i, new Vector3(0f, bodyY, z),
                    new Vector3(unit * 1.1f * taper, unit * 0.95f * taper, segLen), _bodyMat);
            }

            float frontZ = (segments - 1) * 0.5f * segLen + segLen * 0.6f;
            AddPart(root, "Head", new Vector3(0f, bodyY + unit * 0.2f, frontZ),
                new Vector3(unit * 0.9f, unit * 0.85f, unit * 0.8f), _bodyMat);

            // Eyes (small bright cubes) so it reads as a face — emissive-ish when glowing.
            var eyeMat = Unlit(c.Glows ? new Color(0.8f, 1f, 0.9f) : new Color(0.95f, 0.95f, 0.8f));
            float eyeX = unit * 0.28f;
            AddPart(root, "EyeL", new Vector3(-eyeX, bodyY + unit * 0.35f, frontZ + unit * 0.25f), Vector3.one * unit * 0.16f, eyeMat);
            AddPart(root, "EyeR", new Vector3(eyeX, bodyY + unit * 0.35f, frontZ + unit * 0.25f), Vector3.one * unit * 0.16f, eyeMat);

            // Legs: paired under the body along its length.
            int legs = Mathf.Clamp(c.Legs, 0, 8);
            int pairs = legs / 2;
            for (int p = 0; p < pairs; p++)
            {
                float z = pairs == 1 ? 0f : Mathf.Lerp(-segLen * 0.7f, segLen * 0.7f, p / (float)(pairs - 1));
                AddPart(root, "LegL" + p, new Vector3(-unit * 0.5f, bodyY * 0.45f, z),
                    new Vector3(unit * 0.18f, bodyY * 0.9f, unit * 0.18f), _bodyMat);
                AddPart(root, "LegR" + p, new Vector3(unit * 0.5f, bodyY * 0.45f, z),
                    new Vector3(unit * 0.18f, bodyY * 0.9f, unit * 0.18f), _bodyMat);
            }

            if (c.HasWings)
            {
                AddPart(root, "WingL", new Vector3(-unit * 0.9f, bodyY + unit * 0.2f, 0f),
                    new Vector3(unit * 0.9f, unit * 0.08f, unit * 1.2f), _bodyMat);
                AddPart(root, "WingR", new Vector3(unit * 0.9f, bodyY + unit * 0.2f, 0f),
                    new Vector3(unit * 0.9f, unit * 0.08f, unit * 1.2f), _bodyMat);
            }

            if (c.HasTail)
            {
                float tailZ = -(segments - 1) * 0.5f * segLen - segLen * 0.6f;
                AddPart(root, "Tail", new Vector3(0f, bodyY, tailZ),
                    new Vector3(unit * 0.35f, unit * 0.35f, segLen * 0.9f), _bodyMat);
            }

            if (c.Glows)
            {
                var go = new GameObject("Glow");
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(0f, bodyY, 0f);
                _glow = go.AddComponent<Light>();
                _glow.type = LightType.Point;
                _glow.range = unit * 6f;
                _glow.intensity = 1.1f;
                _glow.color = Rgb(c.ColorRgb);
                _glow.shadows = LightShadows.None;
            }
        }

        private void AddPart(GameObject root, string partName, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = partName;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Object.Destroy(col); // render-only; never blocks the player
            }

            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            _renderers.Add(go.GetComponent<Renderer>());
        }

        private static Color Rgb(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        private static Material Unlit(Color color)
        {
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Spacecraft/VertexColorOpaque");
            return new Material(shader) { color = color };
        }

        // Shared (loaded once) tintable grayscale hide tiles; the body multiplies them by the species colour.
        private static Texture2D _scales, _fur, _chitin, _hide, _slime;
        private static bool _texLoaded;

        private static void EnsureTextures()
        {
            if (_texLoaded)
            {
                return;
            }

            _texLoaded = true;
            _scales = LoadTex("creature_scales");
            _fur = LoadTex("creature_fur");
            _chitin = LoadTex("creature_chitin");
            _hide = LoadTex("creature_hide");
            _slime = LoadTex("creature_slime");
        }

        /// <summary>Picks a hide tile for the species: glowing → slime, hostile → chitin, winged → scales,
        /// otherwise a stable choice from the species id so each species looks consistent.</summary>
        private static Texture2D PickHide(NetCreature c)
        {
            if (c.Glows && _slime != null) return _slime;
            if (c.Hostile && _chitin != null) return _chitin;
            if (c.HasWings && _scales != null) return _scales;

            var opts = new[] { _fur, _hide, _scales };
            int h = 0;
            foreach (char ch in c.SpeciesId ?? string.Empty)
            {
                h = h * 31 + ch;
            }

            return opts[(h & 0x7fffffff) % opts.Length] ?? _hide;
        }

        private static Texture2D LoadTex(string key)
        {
            var asset = Resources.Load<TextAsset>("textures/" + key);
            if (asset == null || asset.bytes.Length != 64 * 64 * 4)
            {
                return null;
            }

            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
            };
            tex.LoadRawTextureData(asset.bytes);
            tex.Apply();
            return tex;
        }

        private static Material Lit(Color color, Texture2D tex)
        {
            var shader = Shader.Find("Spacecraft/LitColor") ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { color = color };
            if (tex != null)
            {
                m.mainTexture = tex;
            }

            return m;
        }
    }
}
