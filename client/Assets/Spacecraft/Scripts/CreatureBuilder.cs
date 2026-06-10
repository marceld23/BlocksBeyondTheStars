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
        private readonly List<Transform> _legPivots = new List<Transform>();
        private readonly List<Transform> _wingPivots = new List<Transform>();
        private Transform _tailPivot;
        private Transform _headPivot;
        private Material _bodyMat;
        private Light _glow;

        /// <summary>Builds the body under <paramref name="root"/> from the descriptor.</summary>
        public void Build(GameObject root, NetCreature c)
        {
            EnsureTextures();
            float unit = 0.5f * Mathf.Clamp(c.Size, 0.4f, 3f);
            Color baseColor = Rgb(c.ColorRgb);
            Color bellyColor = Rgb(c.BellyRgb);

            // Per-species proportion jitter (from the id) so two species with the same parts still differ in
            // build — lanky vs squat, big-headed vs small, slim vs broad.
            int idh = StableIdHash(c.SpeciesId);
            float headScale = 0.75f + ((idh >> 2) & 7) / 7f * 0.65f; // 0.75..1.40
            float bodyWide = 0.85f + ((idh >> 5) & 7) / 7f * 0.55f;  // 0.85..1.40
            float legLong = 0.75f + ((idh >> 8) & 7) / 7f * 0.65f;   // 0.75..1.40

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

            // All parts hang off a body rig (a child of the root) so the animator can undulate the whole
            // creature for swimming without disturbing the root's movement-driven facing.
            var body = new GameObject("BodyRig");
            body.transform.SetParent(root.transform, false);

            // Body: a row of segments along +Z (forward). The front segment is the head.
            int segments = Mathf.Clamp(c.BodySegments, 1, 4);
            float segLen = unit * 1.1f;
            float bodyY = unit * (c.Legs > 0 ? 1.1f : 0.7f);
            for (int i = 0; i < segments; i++)
            {
                float z = (i - (segments - 1) * 0.5f) * segLen;
                float taper = 1f - 0.12f * i; // slimmer toward the tail
                AddPart(body, "Body" + i, new Vector3(0f, bodyY, z),
                    new Vector3(unit * 1.1f * taper * bodyWide, unit * 0.95f * taper, segLen), _bodyMat);
            }

            // Two-tone underside: a flatter belly slab in the accent colour, so the body isn't one flat hue.
            var bellyMat = Lit(c.Glows ? bellyColor * 1.4f : bellyColor, PickHide(c));
            AddPart(body, "Belly", new Vector3(0f, bodyY - unit * 0.42f, 0f),
                new Vector3(unit * 1.02f * bodyWide, unit * 0.32f, segments * segLen * 0.9f), bellyMat);

            float frontZ = (segments - 1) * 0.5f * segLen + segLen * 0.6f;
            // Head on a neck pivot (behind the head) so it can bob/graze/lunge as an idle gesture.
            _headPivot = AddPivotPart(body, "Head", new Vector3(0f, bodyY + unit * 0.2f, frontZ - unit * 0.45f),
                new Vector3(0f, 0f, unit * 0.45f), new Vector3(unit * 0.9f * headScale, unit * 0.85f * headScale, unit * 0.8f * headScale), _bodyMat);

            // Eyes: optional (0 = eyeless) and a random count (often two, sometimes three/four/six). Bigger,
            // with a dark pupil so they clearly read as eyes — spread in a row across the head front.
            int eyes = Mathf.Clamp(c.Eyes, 0, 8);
            if (eyes > 0)
            {
                // Rounded (sphere) eyes — bigger, with a dark pupil + a small white glint so they look glossy (B17).
                var eyeMat = Unlit(c.Glows ? new Color(0.85f, 1f, 0.95f) : new Color(0.97f, 0.97f, 0.88f));
                var pupilMat = Unlit(new Color(0.04f, 0.04f, 0.06f));
                var glintMat = Unlit(Color.white);
                float eyeSize = unit * 0.32f * headScale; // bigger (was 0.24)
                float spread = unit * headScale * (0.30f + 0.05f * eyes); // wider span the more eyes there are
                for (int e = 0; e < eyes; e++)
                {
                    float fx = eyes == 1 ? 0f : Mathf.Lerp(-spread, spread, e / (float)(eyes - 1));
                    var pos = new Vector3(fx, unit * 0.16f * headScale, unit * 0.70f * headScale);
                    AddPartTo(_headPivot, "Eye" + e, pos, Vector3.one * eyeSize, eyeMat, PrimitiveType.Sphere);
                    AddPartTo(_headPivot, "Pupil" + e, pos + new Vector3(0f, 0f, eyeSize * 0.42f), Vector3.one * (eyeSize * 0.55f), pupilMat, PrimitiveType.Sphere);
                    AddPartTo(_headPivot, "Glint" + e, pos + new Vector3(eyeSize * 0.16f, eyeSize * 0.18f, eyeSize * 0.5f), Vector3.one * (eyeSize * 0.16f), glintMat, PrimitiveType.Sphere);
                }
            }

            // Horns/spikes on top of the head — silhouette variety.
            int horns = Mathf.Clamp(c.Horns, 0, 4);
            if (horns > 0)
            {
                var hornMat = Lit(new Color(0.20f, 0.17f, 0.15f), null);
                float hornH = unit * 0.5f * headScale;
                for (int hn = 0; hn < horns; hn++)
                {
                    float hx = horns == 1 ? 0f : Mathf.Lerp(-unit * 0.30f * headScale, unit * 0.30f * headScale, hn / (float)(horns - 1));
                    AddPartTo(_headPivot, "Horn" + hn, new Vector3(hx, unit * (0.5f * headScale + 0.25f * legLong), unit * 0.05f),
                        new Vector3(unit * 0.13f, hornH, unit * 0.13f), hornMat);
                }
            }

            // Legs: paired under the body along its length, each on a hip pivot so it can swing.
            int legs = Mathf.Clamp(c.Legs, 0, 8);
            int pairs = legs / 2;
            float legH = bodyY * 0.9f * legLong;
            for (int p = 0; p < pairs; p++)
            {
                float z = pairs == 1 ? 0f : Mathf.Lerp(-segLen * 0.7f, segLen * 0.7f, p / (float)(pairs - 1));
                _legPivots.Add(AddPivotPart(body, "LegL" + p, new Vector3(-unit * 0.5f, legH, z),
                    new Vector3(0f, -legH * 0.5f, 0f), new Vector3(unit * 0.18f, legH, unit * 0.18f), _bodyMat));
                _legPivots.Add(AddPivotPart(body, "LegR" + p, new Vector3(unit * 0.5f, legH, z),
                    new Vector3(0f, -legH * 0.5f, 0f), new Vector3(unit * 0.18f, legH, unit * 0.18f), _bodyMat));
            }

            if (c.HasWings)
            {
                float wingW = unit * 0.9f;
                _wingPivots.Add(AddPivotPart(body, "WingL", new Vector3(-unit * 0.45f, bodyY + unit * 0.2f, 0f),
                    new Vector3(-wingW * 0.5f, 0f, 0f), new Vector3(wingW, unit * 0.08f, unit * 1.2f), _bodyMat));
                _wingPivots.Add(AddPivotPart(body, "WingR", new Vector3(unit * 0.45f, bodyY + unit * 0.2f, 0f),
                    new Vector3(wingW * 0.5f, 0f, 0f), new Vector3(wingW, unit * 0.08f, unit * 1.2f), _bodyMat));
            }

            if (c.HasTail)
            {
                float tailLen = segLen * 0.9f;
                float tailZ = -(segments - 1) * 0.5f * segLen - segLen * 0.6f;
                _tailPivot = AddPivotPart(body, "Tail", new Vector3(0f, bodyY, tailZ + tailLen * 0.5f),
                    new Vector3(0f, 0f, -tailLen * 0.5f), new Vector3(unit * 0.35f, unit * 0.35f, tailLen), _bodyMat);
            }

            // Dorsal crest: a row of spiny plates along the spine, tallest at the shoulders — silhouette variety.
            if (c.HasCrest)
            {
                var crestMat = Lit(c.Glows ? baseColor * 1.4f : baseColor * 0.7f, null);
                int fins = Mathf.Max(3, segments * 2);
                float z0 = -(segments - 1) * 0.5f * segLen - segLen * 0.2f;
                float z1 = (segments - 1) * 0.5f * segLen + segLen * 0.2f;
                float topY = bodyY + unit * 0.5f;
                for (int f = 0; f < fins; f++)
                {
                    float t = fins == 1 ? 0.5f : f / (float)(fins - 1);
                    float finH = unit * (0.5f - 0.22f * Mathf.Abs(t - 0.32f)); // peak near the front
                    AddPart(body, "Crest" + f, new Vector3(0f, topY, Mathf.Lerp(z0, z1, t)),
                        new Vector3(unit * 0.10f, Mathf.Max(unit * 0.2f, finH), unit * 0.42f), crestMat);
                }
            }

            if (c.Glows)
            {
                var go = new GameObject("Glow");
                go.transform.SetParent(body.transform, false);
                go.transform.localPosition = new Vector3(0f, bodyY, 0f);
                _glow = go.AddComponent<Light>();
                _glow.type = LightType.Point;
                _glow.range = unit * 6f;
                _glow.intensity = 1.1f;
                _glow.color = Rgb(c.ColorRgb);
                _glow.shadows = LightShadows.None;
            }

            // Procedural limb animation (leg swing while moving, wing flap, tail sway) + per-temperament
            // idle head gestures (graze / alert / lunge); aquatic species also undulate the body rig (swim).
            var anim = root.AddComponent<CreatureAnimator>();
            anim.Init(_legPivots.ToArray(), _wingPivots.ToArray(), _tailPivot, _headPivot, body.transform,
                c.Hostile, c.Asleep, c.Habitat == "Water" || c.Habitat == "Amphibian", c.Temperament);
        }

        /// <summary>Adds a part on its own pivot (hinge) so it can be rotated for animation. The cube hangs
        /// at <paramref name="cubeOffset"/> from the pivot; returns the pivot transform.</summary>
        private Transform AddPivotPart(GameObject root, string partName, Vector3 pivotPos, Vector3 cubeOffset, Vector3 scale, Material mat)
        {
            var pivot = new GameObject(partName).transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = pivotPos;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = partName + "Mesh";
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Object.Destroy(col);
            }

            go.transform.SetParent(pivot, false);
            go.transform.localPosition = cubeOffset;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            _renderers.Add(go.GetComponent<Renderer>());
            return pivot;
        }

        /// <summary>Adds a render-only cube parented to an arbitrary transform (e.g. eyes on the head pivot).</summary>
        private void AddPartTo(Transform parent, string partName, Vector3 localPos, Vector3 scale, Material mat, PrimitiveType shape = PrimitiveType.Cube)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = partName;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Object.Destroy(col);
            }

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            _renderers.Add(go.GetComponent<Renderer>());
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

        private static int StableIdHash(string s)
        {
            int h = 0;
            foreach (char ch in s ?? string.Empty)
            {
                h = h * 31 + ch;
            }

            return h & 0x7fffffff;
        }

        private static Material Unlit(Color color)
        {
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Spacecraft/VertexColorOpaque");
            return new Material(shader) { color = color };
        }

        // Shared (loaded once) tintable grayscale hide tiles; the body multiplies them by the species colour.
        private static Texture2D _scales, _fur, _chitin, _hide, _slime;
        private static Texture2D _feathers, _spots, _stripes, _warty, _plated;
        private static Texture2D _finned, _tentacled;
        // Task 6 — more skin variety.
        private static Texture2D _mossy, _crystalline, _metallic, _banded, _shaggy;
        private static Texture2D _spined, _mottled, _iridescent, _barkskin, _veined;
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
            _feathers = LoadTex("creature_feathers");
            _spots = LoadTex("creature_spots");
            _stripes = LoadTex("creature_stripes");
            _warty = LoadTex("creature_warty");
            _plated = LoadTex("creature_plated");
            _finned = LoadTex("creature_finned");
            _tentacled = LoadTex("creature_tentacled");
            _mossy = LoadTex("creature_mossy");
            _crystalline = LoadTex("creature_crystalline");
            _metallic = LoadTex("creature_metallic");
            _banded = LoadTex("creature_banded");
            _shaggy = LoadTex("creature_shaggy");
            _spined = LoadTex("creature_spined");
            _mottled = LoadTex("creature_mottled");
            _iridescent = LoadTex("creature_iridescent");
            _barkskin = LoadTex("creature_barkskin");
            _veined = LoadTex("creature_veined");
        }

        /// <summary>Picks a hide tile for the species: glowing → slime, winged → feathers, hostile → chitin/
        /// plated, otherwise a stable choice from a wide pool keyed off the species id (so each species looks
        /// consistent but the world's fauna spans many skins).</summary>
        private static Texture2D PickHide(NetCreature c)
        {
            int h = StableIdHash(c.SpeciesId);
            if (c.Glows)
            {
                var glow = new[] { _slime, _veined, _crystalline, _iridescent };
                return glow[h % glow.Length] ?? _slime ?? _hide;
            }

            if (c.HasWings && _feathers != null)
            {
                var winged = new[] { _feathers, _iridescent, _mottled };
                return winged[h % winged.Length] ?? _feathers;
            }

            if (c.Habitat == "Water" || c.Habitat == "Amphibian")
            {
                var aquatic = new[] { _finned, _tentacled, _slime, _scales, _iridescent, _banded };
                return aquatic[h % aquatic.Length] ?? _finned ?? _hide;
            }

            if (c.Hostile)
            {
                var hostileOpts = new[] { _chitin, _plated, _scales, _spined, _metallic, _crystalline, _barkskin };
                return hostileOpts[h % hostileOpts.Length] ?? _chitin ?? _hide;
            }

            var opts = new[]
            {
                _fur, _hide, _scales, _spots, _stripes, _warty, _plated,
                _mossy, _shaggy, _mottled, _banded, _barkskin, _veined,
            };
            return opts[h % opts.Length] ?? _hide;
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
