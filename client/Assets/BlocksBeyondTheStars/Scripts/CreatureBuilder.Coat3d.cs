// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Fur and feathers as geometry on the standard body plan. While a body is built every cube part is
    /// noted; at the end each part whose hide is a fur wears a coat mesh (<see cref="CoatForms"/>: felt, and strands
    /// at the places the species rolled), and a winged species with a feather hide gets wings of single feathers, a
    /// tail fan, a crest and a plumage of small tabs. The coat hangs under its part, so it moves with the rig; the
    /// meshes are built once per species and shared. Only where <see cref="GameLook.Coats"/> allows it.
    /// </summary>
    public sealed partial class CreatureBuilder
    {
        private struct CoatPartRecord
        {
            public GameObject Go;
            public Vector3 Scale;
            public Material Mat;
            public bool Pinned;
        }

        private struct WingPanels
        {
            public Transform Shoulder, Wrist;
            public float Inner, Outer, Chord, Thick, Side;
        }

        private List<CoatPartRecord> _coatParts; // non-null while a body that may wear a coat is being built
        private readonly List<WingPanels> _wingPanels = new List<WingPanels>();
        private bool _feathered;
        private bool _coatEveryone;

        private static readonly Dictionary<string, Mesh> CoatMeshes = new Dictionary<string, Mesh>();
        private static readonly Dictionary<(Material Source, Color Accent), Material> CoatMaterials = new Dictionary<(Material, Color), Material>();
        private static Shader _coatShader;
        private static readonly int LookCoatId = Shader.PropertyToID("_Sc_LookCoat");

        private static int FurKind(string hide) => hide switch
        {
            "fur" or "velvet" or "down" => CoatForms.Felt,
            "bristles" => CoatForms.Bristle,
            "shaggy" or "wool" => CoatForms.Shaggy,
            _ => -1,
        };

        private static bool IsFeatherHide(string hide) => hide is "feathers" or "finefeathers" or "down" or "featherscales";

        /// <summary>Three blunt, dark toes at the front of every foot — the feet of the concept pictures'
        /// animals. Standard animals only, and none where the hound's claws already sit.</summary>
        private void AddLabToes(NetCreature c, float legThick)
        {
            if (!GameLook.Toes || c.HasClaws || !(string.IsNullOrEmpty(c.BodyPlan) || c.BodyPlan == "Standard"))
            {
                return;
            }

            var toeMat = DarkHornMaterial(c);
            float soleFront = (legThick * 0.3f) + (legThick * 0.9f); // the sole box sits 0.3·thick forward, 1.8·thick deep
            foreach (var leg in _legs)
            {
                if (leg?.Foot == null)
                {
                    continue;
                }

                for (int k = 0; k < 3; k++)
                {
                    AddPartTo(leg.Foot, "Toe" + k, new Vector3((k - 1) * legThick * 0.47f, -legThick * 0.31f, soleFront + (legThick * 0.14f)),
                        new Vector3(legThick * 0.43f, legThick * 0.42f, legThick * 0.62f), toeMat);
                }
            }
        }

        /// <summary>Decides whether this body may wear a coat and starts noting its parts. Called once the build's
        /// <see cref="_coat"/> exists and before the first part.</summary>
        private void BeginCoat(NetCreature c)
        {
            _coatParts = null;
            _feathered = false;
            _coatEveryone = false;
            _wingPanels.Clear();
            if (!GameLook.Coats || !(string.IsNullOrEmpty(c.BodyPlan) || c.BodyPlan == "Standard"))
            {
                return;
            }

            _coatShader ??= Shader.Find("BlocksBeyondTheStars/CreatureCoat");
            if (_coatShader == null)
            {
                return;
            }

            // "Everyone" mode (<c>GameLook.CoatMode</c> 2): every standard animal, except what lives in water.
            _coatEveryone = GameLook.CoatMode == 2 && !c.HasFins && c.Habitat != "Water";
            _feathered = c.HasWings && (_coatEveryone || IsFeatherHide(_coat.HideOf(CoatPart.Wing)) || IsFeatherHide(_coat.HideOf(CoatPart.Torso)));
            _coatParts = new List<CoatPartRecord>();
        }

        private void RecordCoatPart(GameObject go, Vector3 scale, Material mat)
        {
            if (_coatParts != null)
            {
                _coatParts.Add(new CoatPartRecord { Go = go, Scale = scale, Mat = mat, Pinned = _pinV || (_coat != null && _coat.Pins(mat)) });
            }
        }

        /// <summary>Hangs the coat on the noted parts. <paramref name="unit"/> is this animal's unit; the meshes are
        /// built at the species' size, so the whole herd shares them.</summary>
        private void FinishCoat(GameObject root, NetCreature c, Transform bodyRig, float unit)
        {
            var parts = _coatParts;
            _coatParts = null;
            if (parts == null || parts.Count == 0)
            {
                return;
            }

            try
            {
                HangCoat(root, c, bodyRig, unit, parts);
            }
            catch (System.Exception e)
            {
                // Look code: a fault in the coat must never cost the animal its body.
                Debug.LogWarning("[Look] coat failed for " + c.SpeciesId + ": " + e);
            }
        }

        private void HangCoat(GameObject root, NetCreature c, Transform bodyRig, float unit, List<CoatPartRecord> parts)
        {
            RemoveOldCoatDrivers(root);
            uint seed = CreatureCoat.SpeciesSeed(c.SpeciesId, c.VoiceSeed);
            float speciesUnit = 0.5f * Mathf.Clamp(c.SpeciesSize > 0f ? c.SpeciesSize : c.Size, 0.4f, 3f);
            int baseKind = _feathered ? CoatForms.Plumage : FurKind(_coat.HideOf(CoatPart.Torso));
            int styles = CoatForms.StylesFor(seed, baseKind < 0 ? CoatForms.Felt : baseKind);
            Color accent = _coat.Mat(CoatPart.Ornament).color;
            TiledCube.Placement(_placement, out float du, out float dv, out bool mirror);
            var entries = new List<CoatMotion.Entry>();
            CoatPartRecord? lastTail = HangFur(parts, seed, speciesUnit, unit, baseKind, styles, accent, du, dv, mirror, entries);

            if (_feathered)
            {
                AddFeathers(c, parts, lastTail, bodyRig, seed, speciesUnit, unit, accent, entries);
            }

            if (entries.Count > 0)
            {
                root.AddComponent<CoatMotion>().Init(entries);

                // The felt's range is published with the sky's look on a planet; aboard a ship nothing publishes it,
                // and a pet there must not lose its felt.
                float range = GameLook.CoatRangeNow;
                Shader.SetGlobalVector(LookCoatId, new Vector4(range, Mathf.Min(4f, Mathf.Max(0.5f, range * 0.25f)), 0f, 0f));
            }
        }

        /// <summary>A body is rebuilt on the same root when its mood changes (CreatureView destroys the old parts and the
        /// animator): the coat driver of the old body goes with them, before the new coat is hung.</summary>
        private static void RemoveOldCoatDrivers(GameObject root)
        {
            foreach (var old in root.GetComponents<CoatMotion>())
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(old);
                }
                else
                {
                    Object.DestroyImmediate(old);
                }
            }
        }

        /// <summary>The fur step of <see cref="HangCoat"/>: a felt and a strand mesh on every noted part whose hide is a
        /// fur, in the parts' order. Returns the last tail link (a bird's tail fan grows from it), or null.</summary>
        private CoatPartRecord? HangFur(List<CoatPartRecord> parts, uint seed, float speciesUnit, float unit, int baseKind, int styles,
            Color accent, float du, float dv, bool mirror, List<CoatMotion.Entry> entries)
        {
            int segments = 0, links = 0;
            foreach (var p in parts)
            {
                segments += p.Go != null && p.Go.name.StartsWith("Body", System.StringComparison.Ordinal) ? 1 : 0;
                links += p.Go != null && p.Go.name.StartsWith("TailSeg", System.StringComparison.Ordinal) ? 1 : 0;
            }

            CoatPartRecord? lastTail = null;
            foreach (var p in parts)
            {
                if (p.Go == null || !Classify(p.Go.name, segments, links, out int zone, out bool first, out bool last, out int skip, out CoatPart role))
                {
                    continue;
                }

                if (zone == CoatForms.Tail && last)
                {
                    lastTail = p;
                }

                int kind = PartKind(role, zone, baseKind);
                if (kind < 0)
                {
                    continue;
                }

                var box = FurBox(p, kind, zone, first, last, skip, styles, seed, speciesUnit, unit, out string key);
                var uv = new Vector4(du, p.Pinned ? 0f : dv, mirror ? 1f : 0f, 0f);
                var material = CoatMaterial(p.Mat, accent);
                var boxCopy = box;
                AddCoatObject(p.Go.transform, Vector3.zero, 1f, CoatMesh(key + "|felt", () => CoatForms.Fur(boxCopy, true, false)), material, entries, uv, true);
                AddCoatObject(p.Go.transform, Vector3.zero, 1f, CoatMesh(key + "|strands", () => CoatForms.Fur(boxCopy, false, true)), material, entries, uv, false);
            }

            return lastTail;
        }

        /// <summary>The coat box of one furred part at the species' size, with its rounding and its eyes, and the key its
        /// two meshes are shared under (every value the meshes depend on).</summary>
        private CoatForms.Box FurBox(CoatPartRecord p, int kind, int zone, bool first, bool last, int skip, int styles, uint seed,
            float speciesUnit, float unit, out string key)
        {
            float metres = _coat.MetresOf(p.Mat);
            var box = new CoatForms.Box
            {
                Size = new Vector3(Mathf.Abs(p.Scale.x), Mathf.Abs(p.Scale.y), Mathf.Abs(p.Scale.z)) * (speciesUnit / unit),
                Unit = speciesUnit,
                Kind = kind,
                Zone = zone,
                First = first,
                Last = last,
                Styles = styles,
                Seed = seed,
                Density = GameLook.CoatDensityNow,
                Rx = TiledCube.Repeats(Mathf.Abs(p.Scale.x), metres),
                Ry = TiledCube.Repeats(Mathf.Abs(p.Scale.y), metres),
                Rz = TiledCube.Repeats(Mathf.Abs(p.Scale.z), metres),
                PinV = p.Pinned,
                Skip = skip,
            };
            box.Round = Rounds(p.Scale) ? RoundedCube.Radius(box.Size, GameLook.RoundShare) : 0f;
            string eyes = zone == CoatForms.Head ? Eyes(p, speciesUnit / unit, ref box) : string.Empty;
            key = string.Format(CultureInfo.InvariantCulture, "fur|{0}|{1}|{2}|{3}{4}|{5}|{6}|{7}{8}{9}{10}|{11:F3},{12:F3},{13:F3}|{14:F3}|{15:F2}|{16}|{17:F4}",
                seed, kind, zone, first ? 1 : 0, last ? 1 : 0, styles, skip, box.Rx, box.Ry, box.Rz, box.PinV ? 1 : 0,
                box.Size.x, box.Size.y, box.Size.z, speciesUnit, box.Density, eyes, box.Round);
            return box;
        }

        /// <summary>Which part of the body a cube is, by the name the build path gave it; false for everything that
        /// stays bare (toes, horns, fins, eyes, lids, stalks, wing arms).</summary>
        private static bool Classify(string name, int segments, int links, out int zone, out bool first, out bool last, out int skip, out CoatPart role)
        {
            zone = 0;
            first = last = false;
            skip = 0;
            role = CoatPart.Torso;
            if (name.StartsWith("Body", System.StringComparison.Ordinal) && int.TryParse(name.Substring(4), out int segment))
            {
                // Segment 0 is the rear one; its neighbour in front hides the back cap of every other.
                zone = CoatForms.Torso;
                first = segment == segments - 1;
                skip = CoatForms.FaceBottom | (segment > 0 ? CoatForms.FaceBack : 0);
                return true;
            }

            if (name.StartsWith("TailSeg", System.StringComparison.Ordinal) && int.TryParse(name.Substring(7), out int link))
            {
                zone = CoatForms.Tail;
                role = CoatPart.Tail;
                last = link == links - 1;
                skip = CoatForms.FaceFront | (last ? 0 : CoatForms.FaceBack);
                return true;
            }

            if (name.StartsWith("CoatStripe", System.StringComparison.Ordinal) || name == "CoatSaddle"
                || name.StartsWith("CoatCape", System.StringComparison.Ordinal))
            {
                zone = CoatForms.Slab;
                skip = CoatForms.FaceBottom;
                return true;
            }

            switch (name)
            {
                case "Belly": zone = CoatForms.Belly; skip = CoatForms.FaceTop; return true;
                case "HeadUpper": zone = CoatForms.Head; role = CoatPart.Head; skip = CoatForms.FaceBottom; return true;
                case "JawMesh": zone = CoatForms.Jaw; role = CoatPart.Head; skip = CoatForms.FaceTop; return true;
                case "Thigh": zone = CoatForms.Thigh; role = CoatPart.Limb; skip = CoatForms.FaceBottom; return true;
                case "Shin": zone = CoatForms.Shin; role = CoatPart.Limb; skip = CoatForms.FaceTop | CoatForms.FaceBottom; return true;
                case "Sole": zone = CoatForms.Paw; role = CoatPart.Limb; skip = CoatForms.FaceBottom; return true;
                default: return false;
            }
        }

        /// <summary>The cover of one part (−1 = bare): a bird wears plumage but for its lower legs; a furred animal
        /// wears fur wherever the part's own hide is one — a furry torso on scaly legs keeps the scaly legs.</summary>
        private int PartKind(CoatPart role, int zone, int baseKind)
        {
            if (_feathered)
            {
                return zone == CoatForms.Shin || zone == CoatForms.Paw ? -1 : CoatForms.Plumage; // a bird's legs and feet stay bare
            }

            int kind = FurKind(_coat.HideOf(role));
            if (kind < 0 && _coatEveryone)
            {
                kind = baseKind >= 0 ? baseKind : CoatForms.Felt;
            }

            return kind;
        }

        /// <summary>Notes the eyes on a head's front face (in the head cube's unit space) so no felt grows over them.</summary>
        private static string Eyes(CoatPartRecord head, float toSpecies, ref CoatForms.Box box)
        {
            var parent = head.Go.transform.parent;
            if (parent == null)
            {
                return string.Empty;
            }

            var eyes = new List<Vector2>();
            float radius = 0f;
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (!child.name.StartsWith("Eye", System.StringComparison.Ordinal) || child.name.StartsWith("Eyelid", System.StringComparison.Ordinal))
                {
                    continue;
                }

                var rel = child.localPosition - head.Go.transform.localPosition;
                var at = new Vector2(rel.x / head.Scale.x, rel.y / head.Scale.y);
                eyes.Add(at);
                radius = Mathf.Max(radius, Mathf.Abs(child.localScale.x) * 0.62f * toSpecies);
                text.AppendFormat(CultureInfo.InvariantCulture, "{0:F2},{1:F2};", at.x, at.y);
            }

            box.Eyes = eyes.ToArray();
            box.EyeRadius = radius;
            text.AppendFormat(CultureInfo.InvariantCulture, "r{0:F3}", radius);
            return text.ToString();
        }

        /// <summary>A bird's feathers: both panels of every wing, the tail fan and — on a crested species — the crest.</summary>
        private void AddFeathers(NetCreature c, List<CoatPartRecord> parts, CoatPartRecord? lastTail, Transform bodyRig, uint seed,
            float speciesUnit, float unit, Color accent, List<CoatMotion.Entry> entries)
        {
            var look = CoatForms.PlumesFor(seed);
            AddWingFeathers(look, seed, speciesUnit, unit, accent, entries);
            AddTailFan(lastTail, bodyRig, look, seed, speciesUnit, unit, accent, entries);
            if (!c.HasCrest)
            {
                return;
            }

            foreach (var p in parts)
            {
                if (p.Go == null || p.Go.name != "HeadUpper" || p.Go.transform.parent == null)
                {
                    continue;
                }

                float depth = Mathf.Abs(p.Scale.z) / unit, height = Mathf.Abs(p.Scale.y) / 0.7f / unit;
                string crestKey = string.Format(CultureInfo.InvariantCulture, "crest|{0}|{1:F3}|{2:F3}|{3:F3}", seed, depth, height, speciesUnit);
                AddCoatObject(p.Go.transform.parent, p.Go.transform.localPosition + new Vector3(0f, Mathf.Abs(p.Scale.y) * 0.5f, 0f), unit,
                    CoatMesh(crestKey, () => CoatForms.Crest(seed, look, depth, height, speciesUnit)), CoatMaterial(p.Mat, accent), entries, Vector4.zero, false);
            }
        }

        /// <summary>The feather step for the wings: both panels of every wing (inner on the shoulder, outer on the wrist),
        /// each trailing a point out on its panel and folding with its wrist.</summary>
        private void AddWingFeathers(CoatForms.Plumes look, uint seed, float speciesUnit, float unit, Color accent, List<CoatMotion.Entry> entries)
        {
            var wingMat = CoatMaterial(_coat.Mat(CoatPart.Wing), accent);
            foreach (var w in _wingPanels)
            {
                for (int panel = 0; panel < 2; panel++)
                {
                    bool outer = panel == 1;
                    float span = (outer ? w.Outer : w.Inner) / unit;
                    var wing = new CoatForms.Wing { Span = span, Chord = w.Chord / unit, Thick = w.Thick / unit, Side = w.Side, Unit = speciesUnit, Seed = seed };
                    string key = string.Format(CultureInfo.InvariantCulture, "wing|{0}|{1}|{2}|{3:F3}|{4:F3}|{5:F3}|{6:F3}",
                        seed, panel, w.Side > 0f ? 1 : 0, wing.Span, wing.Chord, wing.Thick, speciesUnit);
                    var entry = AddCoatObject(outer ? w.Wrist : w.Shoulder, Vector3.zero, unit, CoatMesh(key, () => CoatForms.WingPanel(wing, look, outer)),
                        wingMat, entries, Vector4.zero, false);
                    if (entry != null)
                    {
                        entry.Probe = new Vector3(w.Side * span * (outer ? 0.8f : 0.6f), 0f, -0.3f * wing.Chord);
                        entry.Gain = 0.09f;
                        entry.Wrist = w.Wrist;
                        entry.WristRest = w.Wrist.localRotation;
                    }
                }
            }
        }

        /// <summary>The feather step for the tail: the fan grows from the end of the tail, or from the rump of a tailless bird.</summary>
        private void AddTailFan(CoatPartRecord? lastTail, Transform bodyRig, CoatForms.Plumes look, uint seed, float speciesUnit, float unit,
            Color accent, List<CoatMotion.Entry> entries)
        {
            Transform tailParent = bodyRig;
            Vector3 tailAt = Vector3.zero;
            if (lastTail.HasValue && lastTail.Value.Go.transform.parent != null)
            {
                var s = lastTail.Value.Scale;
                tailParent = lastTail.Value.Go.transform.parent;
                tailAt = new Vector3(0f, s.y * 0.2f, -s.z * 0.75f);
            }
            else if (_torsoBoxes.Count > 0)
            {
                var (pos, size) = _torsoBoxes[0];
                tailAt = new Vector3(0f, pos.y + (size.y * 0.25f), pos.z - (size.z * 0.5f));
            }

            string tailKey = string.Format(CultureInfo.InvariantCulture, "tailfan|{0}|{1:F3}", seed, speciesUnit);
            var tail = AddCoatObject(tailParent, tailAt, unit, CoatMesh(tailKey, () => CoatForms.TailFan(seed, look, 0.88f, speciesUnit)),
                CoatMaterial(_coat.Mat(CoatPart.Tail), accent), entries, Vector4.zero, false);
            if (tail != null)
            {
                tail.Probe = new Vector3(0f, 0f, -0.6f);
                tail.Gain = 0.12f;
            }
        }

        /// <summary>One coat object under <paramref name="parent"/>; null when the mesh is empty or the shader is missing.</summary>
        private static CoatMotion.Entry AddCoatObject(Transform parent, Vector3 at, float scale, Mesh mesh, Material material,
            List<CoatMotion.Entry> entries, Vector4 uv, bool felt)
        {
            if (mesh == null || material == null || parent == null)
            {
                return null;
            }

            var go = new GameObject(CoatMotion.Mark + "Coat");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            var entry = new CoatMotion.Entry { Renderer = renderer, Uv = uv, Felt = felt };
            entries.Add(entry);
            return entry;
        }

        /// <summary>A coat mesh by its key, built once (an empty coat is remembered as null).</summary>
        private static Mesh CoatMesh(string key, System.Func<CoatForms.Shape> build)
        {
            if (CoatMeshes.TryGetValue(key, out var cached) && (cached != null || ReferenceEquals(cached, null)))
            {
                return cached;
            }

            var shape = build();
            Mesh mesh = null;
            if (shape.Verts.Count > 0)
            {
                mesh = shape.ToMesh("Coat");
                mesh.UploadMeshData(true);
            }

            CoatMeshes[key] = mesh;
            return mesh;
        }

        /// <summary>The coat material of a part: its texture, colour and light settings on the coat shader (shared per
        /// part material, like the parts' own).</summary>
        private static Material CoatMaterial(Material source, Color accent)
        {
            if (source == null || _coatShader == null)
            {
                return null;
            }

            if (CoatMaterials.TryGetValue((source, accent), out var cached) && cached != null)
            {
                return cached;
            }

            var m = new Material(_coatShader) { color = source.color, mainTexture = source.mainTexture };
            foreach (string name in new[] { "_Floor", "_Fill", "_Glow" })
            {
                if (source.HasProperty(name))
                {
                    m.SetFloat(name, source.GetFloat(name));
                }
            }

            m.SetColor("_CoatAccent", accent);
            CoatMaterials[(source, accent)] = m;
            return m;
        }
    }
}
