// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The shared effect toolkit of the VFX overhaul (#2152) — every weapon, scanner, mining, gadget and flight effect
    /// is built from these parts instead of spawning its own GameObjects, ParticleSystems and Materials:
    /// <list type="bullet">
    /// <item><b>Shared emitters</b>: one world-space <see cref="ParticleSystem"/> per particle kind (<see cref="Kind"/>),
    /// fed through <see cref="ParticleSystem.Emit(ParticleSystem.EmitParams, int)"/> — no GameObject churn, one draw call
    /// per kind, a budget per quality preset.</item>
    /// <item><b>Cached materials</b>: one per shader + style (+ colour where a colour must be baked in), never one per spawn
    /// (the old <c>WeaponFx.Mat</c> / <c>SpaceView.Unlit</c> leaked a Material per effect cube, #2151).</item>
    /// <item><b>Pooled renderers</b> for beams (LineRenderer + <c>FxBeam</c>), rings (<c>FxRing</c> quads), shells
    /// (<c>FxShell</c> spheres) and holograms (<c>FxHolo</c> cubes), animated by a single tween list.</item>
    /// <item><b>Density</b>: every count goes through <see cref="Scaled"/> (quality preset × Reduced effects), and every
    /// large flash through <see cref="FlashScale"/> (the Reduce-flashes setting, XAG 118 / WCAG 2.3.1).</item>
    /// </list>
    /// Render-only: nothing here touches game state. Lives on the world root (WorldRig); <see cref="Ensure"/> creates a
    /// stand-alone host for callers outside a world.
    /// </summary>
    public sealed class FxKit : MonoBehaviour
    {
        public static FxKit Instance { get; private set; }

        /// <summary>The live kit, created on demand (a world rig normally adds it up front).</summary>
        public static FxKit Ensure()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var go = new GameObject("FxKit");
            return go.AddComponent<FxKit>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            Instance = null;
            FxLights.Reset();
            FxScanWave.Stop();
            _tweens.Clear();
        }

        // ---------------------------------------------------------------- configuration

        public static QualityPreset Preset { get; private set; } = QualityPreset.Medium;
        public static bool ReducedEffects { get; private set; }

        /// <summary>Screen-shake strength 0..1 (the accessibility slider, default 0.7).</summary>
        public static float ScreenShake { get; private set; } = 0.7f;

        /// <summary>The Reduce-flashes setting: large and full-screen flashes are clamped (photosensitivity).</summary>
        public static bool ReduceFlashes { get; private set; }

        /// <summary>The Camera-motion comfort setting — off flattens every camera shake, kick and FOV punch.</summary>
        public static bool CameraMotion { get; private set; } = true;

        /// <summary>Pushes the player's settings in (world start and every live settings change).</summary>
        public static void Configure(QualityPreset preset, bool reducedEffects, float screenShake, bool reduceFlashes, bool cameraMotion)
        {
            Preset = preset;
            ReducedEffects = reducedEffects;
            ScreenShake = Mathf.Clamp01(screenShake);
            ReduceFlashes = reduceFlashes;
            CameraMotion = cameraMotion;
            FxLights.MaxLights = preset switch
            {
                QualityPreset.Potato => 2,
                QualityPreset.Low => 4,
                _ => FxLights.Slots,
            };
            Instance?.ApplyBudgets();
        }

        /// <summary>Particle density for the current preset (Potato 0.35 … High 1), halved by Reduced effects.</summary>
        public static float Density
        {
            get
            {
                float d = Preset switch
                {
                    QualityPreset.Potato => 0.35f,
                    QualityPreset.Low => 0.6f,
                    QualityPreset.Medium => 0.85f,
                    _ => 1f,
                };
                return ReducedEffects ? d * 0.5f : d;
            }
        }

        /// <summary>A particle count scaled by <see cref="Density"/> (never below one).</summary>
        public static int Scaled(int count) => Mathf.Max(1, Mathf.RoundToInt(count * Density));

        /// <summary>Brightness factor for large flashes — 1, or a clamp when Reduce flashes is on.</summary>
        public static float FlashScale => ReduceFlashes ? 0.35f : 1f;

        /// <summary>True when the optional extra layers (secondary sparks, afterimages, smoke) are worth drawing.</summary>
        public static bool Rich => Preset >= QualityPreset.Medium && !ReducedEffects;

        /// <summary>Linear colour for a mesh/particle stream from an sRGB-authored colour (the project renders Linear).</summary>
        public static Color Lin(Color c) => ShaderColor.Srgb(c);

        // ---------------------------------------------------------------- shared emitters

        /// <summary>The shared particle kinds. Each is ONE world-space ParticleSystem.</summary>
        public enum Kind
        {
            /// <summary>Additive streaks stretched along their velocity, falling under gravity (impacts, drills).</summary>
            Sparks,

            /// <summary>Additive soft dots without gravity (sparkles, data motes, embers, trails).</summary>
            Motes,

            /// <summary>Additive glow cards that swell and fade fast (muzzle flashes, impact flashes, cores).</summary>
            Glow,

            /// <summary>Alpha-blended dust that drifts out and settles.</summary>
            Dust,

            /// <summary>Alpha-blended soft puffs that rise and grow (smoke, steam, the defeat puff).</summary>
            Smoke,

            /// <summary>Lit mesh cubes tumbling under gravity (block chips, robot parts, rubble).</summary>
            Debris,

            /// <summary>Lit mesh cubes tumbling WITHOUT gravity (space rubble, wreck parts).</summary>
            DebrisFloat,
        }

        private readonly Dictionary<Kind, ParticleSystem> _emitters = new Dictionary<Kind, ParticleSystem>();

        private static readonly Dictionary<Kind, int> BaseBudget = new Dictionary<Kind, int>
        {
            [Kind.Sparks] = 600,
            [Kind.Motes] = 700,
            [Kind.Glow] = 160,
            [Kind.Dust] = 300,
            [Kind.Smoke] = 160,
            [Kind.Debris] = 220,
            [Kind.DebrisFloat] = 160,
        };

        /// <summary>Emits ONE particle of <paramref name="kind"/>. Colours are sRGB-authored (converted here).</summary>
        public static void Emit(Kind kind, Vector3 position, Vector3 velocity, float size, float life, Color color)
        {
            var ps = Ensure().Emitter(kind);
            if (ps == null)
            {
                return;
            }

            var ep = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = velocity,
                startSize = size,
                startLifetime = Mathf.Max(0.02f, life),
                startColor = Lin(color),
                applyShapeToPosition = false,
            };
            if (kind == Kind.Debris || kind == Kind.DebrisFloat)
            {
                ep.rotation3D = new Vector3(UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(0f, 360f));
                ep.angularVelocity3D = UnityEngine.Random.insideUnitSphere * 540f;
            }

            ps.Emit(ep, 1);
        }

        /// <summary>A burst of <paramref name="count"/> (density-scaled) particles flung out of a point inside a cone
        /// around <paramref name="direction"/> (zero = all around).</summary>
        public static void Burst(Kind kind, Vector3 at, int count, Vector3 direction, float coneDegrees, float speedMin, float speedMax,
            float sizeMin, float sizeMax, float lifeMin, float lifeMax, Color color, Color? color2 = null)
        {
            int n = Scaled(count);
            for (int i = 0; i < n; i++)
            {
                Vector3 dir = direction.sqrMagnitude > 1e-6f
                    ? RandomInCone(direction.normalized, coneDegrees)
                    : UnityEngine.Random.onUnitSphere;
                var c = color2.HasValue ? Color.Lerp(color, color2.Value, UnityEngine.Random.value) : color;
                Emit(kind, at, dir * UnityEngine.Random.Range(speedMin, speedMax), UnityEngine.Random.Range(sizeMin, sizeMax),
                    UnityEngine.Random.Range(lifeMin, lifeMax), c);
            }
        }

        /// <summary>A soft glow card at a point (muzzle / impact flash): additive, swells and fades within <paramref name="life"/>.</summary>
        public static void Flash(Vector3 at, Color color, float size, float life = 0.12f)
            => Emit(Kind.Glow, at, Vector3.zero, size, life, color);

        /// <summary>A random direction within <paramref name="degrees"/> of <paramref name="axis"/>.</summary>
        public static Vector3 RandomInCone(Vector3 axis, float degrees)
        {
            var rot = Quaternion.FromToRotation(Vector3.forward, axis);
            float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float r = Mathf.Tan(Mathf.Clamp(degrees, 0f, 89f) * Mathf.Deg2Rad) * Mathf.Sqrt(UnityEngine.Random.value);
            return (rot * new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 1f)).normalized;
        }

        private ParticleSystem Emitter(Kind kind)
        {
            if (_emitters.TryGetValue(kind, out var ps) && ps != null)
            {
                return ps;
            }

            ps = BuildEmitter(kind);
            _emitters[kind] = ps;
            return ps;
        }

        private void ApplyBudgets()
        {
            foreach (var kv in _emitters)
            {
                if (kv.Value != null)
                {
                    var main = kv.Value.main;
                    main.maxParticles = Mathf.Max(16, Mathf.RoundToInt(BaseBudget[kv.Key] * Mathf.Max(0.35f, Density)));
                }
            }
        }

        private ParticleSystem BuildEmitter(Kind kind)
        {
            bool debris = kind == Kind.Debris || kind == Kind.DebrisFloat;
            bool alpha = kind == Kind.Dust || kind == Kind.Smoke;
            Material mat = debris ? DebrisMaterial() : alpha ? SoftAlphaMaterial() : kind == Kind.Glow ? GlowMaterial() : SparkMaterial();
            if (mat == null)
            {
                return null;
            }

            var go = new GameObject("Fx" + kind);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(16, Mathf.RoundToInt(BaseBudget[kind] * Mathf.Max(0.35f, Density)));
            main.gravityModifier = kind switch
            {
                Kind.Sparks => 1.1f,
                Kind.Dust => 0.18f,
                Kind.Smoke => -0.06f,
                Kind.Debris => 1.3f,
                _ => 0f,
            };
            if (debris)
            {
                main.startRotation3D = true;
                main.startSize3D = false;
            }

            var em = ps.emission;
            em.enabled = false; // everything comes through Emit()

            var shape = ps.shape;
            shape.enabled = false;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            switch (kind)
            {
                case Kind.Glow:
                    grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.35f), new GradientAlphaKey(0f, 1f) });
                    break;
                case Kind.Smoke:
                case Kind.Dust:
                    grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                        new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.75f, 0.12f), new GradientAlphaKey(0.45f, 0.6f), new GradientAlphaKey(0f, 1f) });
                    break;
                case Kind.Debris:
                case Kind.DebrisFloat:
                    grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
                    break;
                default:
                    grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
                    break;
            }

            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = kind switch
            {
                Kind.Glow => new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(0.25f, 1f), new Keyframe(1f, 1.15f))),
                Kind.Smoke => new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 1.8f))),
                Kind.Dust => new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.7f, 1f, 1.4f)),
                Kind.Debris or Kind.DebrisFloat => new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0f))),
                _ => new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f)),
            };

            if (debris)
            {
                var rot = ps.rotationOverLifetime;
                rot.enabled = false; // angular velocity comes per particle through EmitParams
            }

            if (kind == Kind.Smoke || kind == Kind.Dust)
            {
                var limit = ps.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.limit = 0.6f;
                limit.dampen = 0.08f;
            }

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.None;
            if (debris)
            {
                r.renderMode = ParticleSystemRenderMode.Mesh;
                r.mesh = CubeMesh;
                r.alignment = ParticleSystemRenderSpace.World;
            }
            else if (kind == Kind.Sparks)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.035f;
                r.lengthScale = 1.6f;
            }
            else
            {
                r.renderMode = ParticleSystemRenderMode.Billboard;
            }

            ps.Play();
            return ps;
        }

        // ---------------------------------------------------------------- materials, textures, meshes

        private static Material _sparkMat, _glowMat, _softAlphaMat, _debrisMat;
        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        private static Texture2D _dot, _softDot;
        private static Mesh _cube, _sphere, _quad;

        /// <summary>Additive soft-dot particle material at a moderate HDR boost (sparks, motes).</summary>
        public static Material SparkMaterial() => _sparkMat != null ? _sparkMat : (_sparkMat = ParticleMat("BlocksBeyondTheStars/Particle", Dot, 2.2f));

        /// <summary>Additive glow-card material with a strong HDR boost so flashes bloom.</summary>
        public static Material GlowMaterial() => _glowMat != null ? _glowMat : (_glowMat = ParticleMat("BlocksBeyondTheStars/Particle", SoftDot, 3.2f));

        /// <summary>Alpha-blended soft-dot material (dust, smoke).</summary>
        public static Material SoftAlphaMaterial() => _softAlphaMat != null ? _softAlphaMat : (_softAlphaMat = ParticleMat("BlocksBeyondTheStars/ParticleAlpha", SoftDot, 1f));

        /// <summary>Lit vertex-colour material for mesh-particle debris.</summary>
        public static Material DebrisMaterial()
        {
            if (_debrisMat != null)
            {
                return _debrisMat;
            }

            var shader = Shader.Find("BlocksBeyondTheStars/FxDebris") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque");
            if (shader == null)
            {
                return null;
            }

            _debrisMat = new Material(shader) { name = "FxDebris" };
            return _debrisMat;
        }

        private static Material ParticleMat(string shaderName, Texture2D tex, float intensity)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                return null;
            }

            var m = new Material(shader) { mainTexture = tex, name = shaderName + "@" + intensity };
            if (m.HasProperty("_Intensity"))
            {
                m.SetFloat("_Intensity", intensity);
            }

            return m;
        }

        /// <summary>A cached material for <paramref name="shaderName"/>, keyed by <paramref name="key"/>; <paramref name="setup"/>
        /// runs once when it is created. Returns null when the shader is missing from the build.</summary>
        public static Material Cached(string shaderName, string key, Action<Material> setup = null)
        {
            string k = shaderName + "|" + key;
            if (Cache.TryGetValue(k, out var m) && m != null)
            {
                return m;
            }

            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                return null;
            }

            m = new Material(shader) { name = k };
            setup?.Invoke(m);
            Cache[k] = m;
            return m;
        }

        /// <summary>A cached opaque <c>Unlit/Color</c> material per colour (the legacy cube parts and fallbacks).</summary>
        public static Material Unlit(Color srgb)
        {
            var c32 = (Color32)srgb;
            string key = c32.r + "," + c32.g + "," + c32.b;
            return Cached("Unlit/Color", key, m => m.color = Lin(srgb))
                   ?? Cached("BlocksBeyondTheStars/VertexColorOpaque", key);
        }

        /// <summary>A soft round dot with a bright core (sparks, motes).</summary>
        public static Texture2D Dot => _dot != null ? _dot : (_dot = MakeDot(32, 1.8f));

        /// <summary>A wider, softer dot (glow cards, smoke, dust).</summary>
        public static Texture2D SoftDot => _softDot != null ? _softDot : (_softDot = MakeDot(32, 2.6f));

        private static Texture2D MakeDot(int n, float power)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Clamp01(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c);
                    float a = Mathf.Pow(Mathf.Clamp01(1f - d), power);
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            }

            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }

        public static Mesh CubeMesh => _cube != null ? _cube : (_cube = PrimitiveMesh(PrimitiveType.Cube));

        public static Mesh SphereMesh => _sphere != null ? _sphere : (_sphere = PrimitiveMesh(PrimitiveType.Sphere));

        /// <summary>A unit quad in the XY plane (normal −Z), UV 0..1.</summary>
        public static Mesh QuadMesh => _quad != null ? _quad : (_quad = PrimitiveMesh(PrimitiveType.Quad));

        private static Mesh PrimitiveMesh(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Destroy(go);
            return mesh;
        }

        // ---------------------------------------------------------------- pooled renderers

        private readonly Stack<LineRenderer> _beamPool = new Stack<LineRenderer>();
        private readonly Stack<MeshRenderer> _meshPool = new Stack<MeshRenderer>();
        private static MaterialPropertyBlock _mpb;

        public static MaterialPropertyBlock Block => _mpb ??= new MaterialPropertyBlock();

        /// <summary>A pooled LineRenderer (world space, Tile texture mode, no shadows) with <paramref name="material"/>.
        /// Give it back with <see cref="ReleaseBeam"/>.</summary>
        public LineRenderer RentBeam(Material material)
        {
            LineRenderer lr = null;
            while (_beamPool.Count > 0 && lr == null)
            {
                lr = _beamPool.Pop();
            }

            if (lr == null)
            {
                var go = new GameObject("FxBeam");
                go.transform.SetParent(transform, false);
                lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.textureMode = LineTextureMode.Tile;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.numCapVertices = 2;
                lr.alignment = LineAlignment.View;
            }

            lr.gameObject.SetActive(true);
            lr.sharedMaterial = material;
            lr.positionCount = 2;
            return lr;
        }

        public void ReleaseBeam(LineRenderer lr)
        {
            if (lr == null)
            {
                return;
            }

            lr.gameObject.SetActive(false);
            _beamPool.Push(lr);
        }

        /// <summary>A pooled MeshRenderer showing <paramref name="mesh"/> with <paramref name="material"/>; per-instance
        /// values go through <see cref="Block"/>. Give it back with <see cref="ReleaseMesh"/>.</summary>
        public MeshRenderer RentMesh(Mesh mesh, Material material)
        {
            MeshRenderer mr = null;
            while (_meshPool.Count > 0 && mr == null)
            {
                mr = _meshPool.Pop();
            }

            if (mr == null)
            {
                var go = new GameObject("FxMesh");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            mr.GetComponent<MeshFilter>().sharedMesh = mesh;
            mr.sharedMaterial = material;
            mr.SetPropertyBlock(null);
            mr.transform.SetParent(transform, false);
            mr.transform.localScale = Vector3.one;
            mr.transform.rotation = Quaternion.identity;
            mr.gameObject.SetActive(true);
            return mr;
        }

        public void ReleaseMesh(MeshRenderer mr)
        {
            if (mr == null)
            {
                return;
            }

            mr.SetPropertyBlock(null);
            mr.gameObject.SetActive(false);
            mr.transform.SetParent(transform, false);
            _meshPool.Push(mr);
        }

        // ---------------------------------------------------------------- tweens

        private sealed class Tween
        {
            public float Age;
            public float Duration;
            public Action<float> Step;
            public Action Done;
            public UnityEngine.Object Owner;
            public bool HasOwner;
        }

        private readonly List<Tween> _tweens = new List<Tween>();
        private readonly List<Tween> _adding = new List<Tween>();

        /// <summary>Runs <paramref name="step"/>(t = 0..1) every frame for <paramref name="duration"/> seconds, then
        /// <paramref name="done"/>. With an <paramref name="owner"/>, the tween stops silently once the owner is destroyed.</summary>
        public static void Animate(float duration, Action<float> step, Action done = null, UnityEngine.Object owner = null)
        {
            var kit = Ensure();
            kit._adding.Add(new Tween { Duration = Mathf.Max(0.0001f, duration), Step = step, Done = done, Owner = owner, HasOwner = owner != null });
        }

        /// <summary>Runs <paramref name="action"/> after <paramref name="seconds"/>.</summary>
        public static void Delay(float seconds, Action action, UnityEngine.Object owner = null)
            => Animate(Mathf.Max(0.0001f, seconds), null, action, owner);

        private void Update()
        {
            if (_adding.Count > 0)
            {
                _tweens.AddRange(_adding);
                _adding.Clear();
            }

            float dt = Time.deltaTime;
            for (int i = _tweens.Count - 1; i >= 0; i--)
            {
                var tw = _tweens[i];
                if (tw.HasOwner && tw.Owner == null)
                {
                    _tweens.RemoveAt(i);
                    continue;
                }

                tw.Age += dt;
                float t = Mathf.Clamp01(tw.Age / tw.Duration);
                try
                {
                    tw.Step?.Invoke(t);
                    if (t >= 1f)
                    {
                        _tweens.RemoveAt(i);
                        tw.Done?.Invoke();
                    }
                }
                catch (Exception e)
                {
                    _tweens.Remove(tw);
                    Debug.LogWarning("[FxKit] effect step failed: " + e.Message);
                }
            }

            FxScanWave.Tick(dt);
        }

        private void LateUpdate()
        {
            FxLights.Upload(Camera.main != null ? Camera.main.transform.position : Vector3.zero, Time.deltaTime);
        }

        // ---------------------------------------------------------------- composite helpers

        /// <summary>A layered beam from <paramref name="from"/> to <paramref name="to"/> that fades over <paramref name="life"/>.
        /// <paramref name="material"/> comes from <see cref="BeamMaterial"/>.</summary>
        public static void Beam(Vector3 from, Vector3 to, Color color, float width, float life, Material material, Func<Vector3> follow = null)
        {
            if (material == null)
            {
                return;
            }

            var kit = Ensure();
            var lr = kit.RentBeam(material);
            var lin = Lin(color);
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            Animate(life, t =>
            {
                if (follow != null)
                {
                    lr.SetPosition(0, follow());
                }

                float k = 1f - t;
                lr.widthMultiplier = width * Mathf.Lerp(1f, 0.25f, t * t);
                var c = new Color(lin.r, lin.g, lin.b, k * k);
                lr.startColor = c;
                lr.endColor = new Color(c.r, c.g, c.b, c.a * 0.85f);
            }, () => kit.ReleaseBeam(lr), lr);
        }

        /// <summary>The FxBeam material for a beam look; one per style key.</summary>
        public static Material BeamMaterial(string style, Color core, float coreWidth = 0.3f, float noiseScale = 1.2f, float noiseSpeed = 9f,
            float pulse = 0f, float intensity = 2.5f)
        {
            var c32 = (Color32)core;
            return Cached("BlocksBeyondTheStars/FxBeam", style + "/" + c32.r + "," + c32.g + "," + c32.b, m =>
            {
                m.SetColor("_Color2", Lin(core));
                m.SetFloat("_CoreWidth", coreWidth);
                m.SetFloat("_NoiseScale", noiseScale);
                m.SetFloat("_NoiseSpeed", noiseSpeed);
                m.SetFloat("_Pulse", pulse);
                m.SetFloat("_Intensity", intensity);
            });
        }

        /// <summary>An expanding ring: flat on a surface (<paramref name="normal"/> set) or facing the camera (normal zero).</summary>
        public static void Ring(Vector3 at, Vector3 normal, Color color, float startRadius, float endRadius, float life,
            float thickness = 0.12f, float fill = 0.06f, float lines = 0f, float intensity = 2f)
        {
            var mat = Cached("BlocksBeyondTheStars/FxRing", "ring");
            if (mat == null)
            {
                return;
            }

            var kit = Ensure();
            var mr = kit.RentMesh(QuadMesh, mat);
            var tr = mr.transform;
            tr.position = at;
            bool billboard = normal.sqrMagnitude < 1e-6f;
            var lin = Lin(color);
            Animate(life, t =>
            {
                float e = 1f - (1f - t) * (1f - t); // ease out
                float r = Mathf.Lerp(startRadius, endRadius, e);
                tr.localScale = new Vector3(r * 2f, r * 2f, 1f);
                if (billboard)
                {
                    var cam = Camera.main;
                    if (cam != null)
                    {
                        tr.rotation = Quaternion.LookRotation(tr.position - cam.transform.position);
                    }
                }
                else
                {
                    tr.rotation = Quaternion.LookRotation(-normal);
                }

                var b = Block;
                b.Clear();
                b.SetColor("_Color", new Color(lin.r, lin.g, lin.b, (1f - t) * (1f - t)));
                b.SetFloat("_Thickness", thickness);
                b.SetFloat("_Fill", fill);
                b.SetFloat("_Lines", lines);
                b.SetFloat("_Intensity", intensity);
                mr.SetPropertyBlock(b);
            }, () => kit.ReleaseMesh(mr), mr);
        }

        /// <summary>An expanding fresnel shell (scan pulses, shockwave spheres).</summary>
        public static void Shell(Vector3 at, Color color, float startRadius, float endRadius, float life, float rimPower = 2.5f,
            float fill = 0.02f, float intensity = 1.4f)
        {
            var mat = Cached("BlocksBeyondTheStars/FxShell", "pulse");
            if (mat == null)
            {
                return;
            }

            var kit = Ensure();
            var mr = kit.RentMesh(SphereMesh, mat);
            var tr = mr.transform;
            tr.position = at;
            var lin = Lin(color);
            Animate(life, t =>
            {
                float e = 1f - (1f - t) * (1f - t);
                float r = Mathf.Lerp(startRadius, endRadius, e);
                tr.localScale = Vector3.one * (r * 2f);
                var b = Block;
                b.Clear();
                b.SetColor("_Color", new Color(lin.r, lin.g, lin.b, 1f - t));
                b.SetFloat("_RimPower", rimPower);
                b.SetFloat("_Fill", fill);
                b.SetFloat("_Intensity", intensity);
                mr.SetPropertyBlock(b);
            }, () => kit.ReleaseMesh(mr), mr);
        }
    }
}
