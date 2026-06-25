// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Tool/weapon visual effects (M27 polish): a beam/tracer + muzzle flash + impact spark burst when
    /// the player attacks, and spark showers while drilling. Code-built from cubes on the always-included
    /// Unlit/Color shader — no assets, no new shader. Render-only; combat stays server-authoritative.
    /// </summary>
    public sealed class WeaponFx : MonoBehaviour
    {
        /// <summary>Fires a shot effect: a fading beam from the muzzle to the target, a muzzle flash and
        /// an impact spark burst (works for ranged tracers and short melee strikes alike).</summary>
        public void Shoot(Vector3 from, Vector3 to, Color color)
        {
            float len = Vector3.Distance(from, to);
            if (len > 0.05f)
            {
                var beam = Cube("Beam", (from + to) * 0.5f, new Vector3(0.05f, 0.05f, len), color);
                beam.transform.rotation = Quaternion.LookRotation(to - from);
                beam.AddComponent<FadeKill>().Life = 0.12f;
            }

            Flash(from, color, 0.28f);
            Sparks(to, color, 8);
        }

        /// <summary>Fires a travelling projectile bolt from the muzzle that flies to the target and bursts
        /// on arrival (kinetic weapons — gauss/slug). Leaves a short tracer trail as it goes.</summary>
        public void Projectile(Vector3 from, Vector3 to, Color color)
        {
            var dir = to - from;
            var bolt = Cube("Bolt", from, new Vector3(0.09f, 0.09f, 0.5f), color);
            bolt.transform.rotation = Quaternion.LookRotation(dir.sqrMagnitude > 1e-4f ? dir : Vector3.forward);
            var pr = bolt.AddComponent<Bolt>();
            pr.Target = to;
            pr.Color = color;
            pr.Fx = this;
            Flash(from, color, 0.22f); // muzzle flash
        }

        /// <summary>A quick horizontal slash arc swept in front of the player (melee weapons / fists).</summary>
        public void MeleeArc(Vector3 center, Vector3 forward, Vector3 up, Color color)
        {
            var parent = new GameObject("MeleeArc");
            parent.transform.position = center;
            parent.transform.rotation = Quaternion.LookRotation(forward, up);

            const int seg = 6;
            for (int i = 0; i < seg; i++)
            {
                float r = Mathf.Lerp(0.55f, 1.7f, i / (float)(seg - 1));
                var bit = Cube("ArcBit", Vector3.zero, Vector3.one * 0.13f, color);
                bit.transform.SetParent(parent.transform, false);
                bit.transform.localPosition = new Vector3(0f, 0f, r); // radial streak along the look direction
            }

            parent.AddComponent<Sweep>();
        }

        /// <summary>A brief muzzle/impact flash: a soft additive glow billboard that fades fast (was a cube).</summary>
        public void Flash(Vector3 at, Color color, float size)
        {
            if (!ParticleBurst(at, color, 1, 0f, 0.12f, size * 1.6f, false))
            {
                var f = Cube("Flash", at, Vector3.one * size, color); // fallback if the particle shader is missing
                var fk = f.AddComponent<FadeKill>();
                fk.Life = 0.1f;
                fk.Shrink = true;
            }
        }

        /// <summary>A small shower of additive spark particles at a point (impacts, drilling).</summary>
        public void Sparks(Vector3 at, Color color, int count = 5)
        {
            if (ParticleBurst(at, color, count, 3.6f, 0.45f, 0.13f, true))
            {
                return;
            }

            for (int i = 0; i < count; i++) // fallback: the old Unlit cube sparks
            {
                var p = Cube("Spark", at, Vector3.one * 0.07f, color);
                var s = p.AddComponent<Spark>();
                s.Vel = new Vector3(Random.Range(-2.2f, 2.2f), Random.Range(0.5f, 3f), Random.Range(-2.2f, 2.2f));
            }
        }

        private static Material _sparkMat;

        /// <summary>One-shot additive particle burst (sparks / flashes): <paramref name="count"/> glowing bits in
        /// <paramref name="color"/> flying out of a point, fading + shrinking, optionally arcing under gravity, then
        /// self-destroying (stopAction = Destroy). Returns false if the additive particle shader is unavailable so
        /// callers can fall back to the legacy cubes.</summary>
        private bool ParticleBurst(Vector3 at, Color color, int count, float speed, float life, float size, bool gravity)
        {
            var shader = Shader.Find("BlocksBeyondTheStars/Particle");
            if (shader == null)
            {
                return false;
            }

            _sparkMat ??= new Material(shader) { mainTexture = SparkDot() };

            var go = new GameObject("WeaponBurst");
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = speed > 0f ? new ParticleSystem.MinMaxCurve(speed * 0.3f, speed) : new ParticleSystem.MinMaxCurve(0f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startColor = color;
            main.maxParticles = count + 2;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity ? 0.9f : 0f;
            main.stopAction = ParticleSystemStopAction.Destroy;

            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = _sparkMat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.None;
            ps.Play();
            return true;
        }

        private static Texture2D _sparkDot;

        /// <summary>A soft round glow dot (bright core → transparent rim) shared by the weapon bursts.</summary>
        private static Texture2D SparkDot()
        {
            if (_sparkDot != null)
            {
                return _sparkDot;
            }

            const int n = 16;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Clamp01(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c);
                px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - d), 1.8f));
            }

            tex.SetPixels(px);
            tex.Apply();
            _sparkDot = tex;
            return tex;
        }

        /// <summary>An expanding horizontal ring of bits — a scanner ping.</summary>
        public void Pulse(Vector3 at, Color color)
        {
            const int n = 16;
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                var p = Cube("Pulse", at, Vector3.one * 0.08f, color);
                var s = p.AddComponent<Spark>();
                s.Vel = new Vector3(Mathf.Cos(a), 0.15f, Mathf.Sin(a)) * 4.2f;
                s.Gravity = false;
                s.LifeOverride = 0.45f;
            }
        }

        /// <summary>A low dust puff (landing / footfall).</summary>
        public void Dust(Vector3 at, int count = 7)
        {
            if (DustPuff(at, count))
            {
                return;
            }

            for (int i = 0; i < count; i++) // fallback: the old Unlit cube dust
            {
                var p = Cube("Dust", at + Vector3.up * 0.05f, Vector3.one * 0.1f, new Color(0.62f, 0.57f, 0.47f));
                var s = p.AddComponent<Spark>();
                s.Vel = new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(0.3f, 1.2f), Random.Range(-1.5f, 1.5f));
            }
        }

        private static Material _dustMat;

        /// <summary>A one-shot alpha-blended dust puff: soft tan bits that drift up + out and settle, self-destroying.
        /// Returns false if the alpha particle shader is missing (caller falls back to cubes).</summary>
        private bool DustPuff(Vector3 at, int count)
        {
            var shader = Shader.Find("BlocksBeyondTheStars/ParticleAlpha");
            if (shader == null)
            {
                return false;
            }

            _dustMat ??= new Material(shader) { mainTexture = SparkDot() };

            var go = new GameObject("DustPuff");
            go.transform.position = at + Vector3.up * 0.05f;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.duration = 0.15f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.3f);
            main.startColor = new Color(0.66f, 0.6f, 0.5f, 0.7f);
            main.maxParticles = count + 2;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.25f;
            main.stopAction = ParticleSystemStopAction.Destroy;

            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.2f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.7f, 1f, 1.3f)); // expand as it settles

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = _dustMat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.None;
            ps.Play();
            return true;
        }

        private static GameObject Cube(string name, Vector3 pos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Mat(color);
            return go;
        }

        private static Material Mat(Color c)
        {
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque");
            return new Material(shader) { color = ShaderColor.Srgb(c) };
        }

        /// <summary>Fades/shrinks an effect cube out over a short life, then destroys it.</summary>
        private sealed class FadeKill : MonoBehaviour
        {
            public float Life = 0.12f;
            public bool Shrink;
            private float _t;
            private Vector3 _scale0;

            private void Start() => _scale0 = transform.localScale;

            private void Update()
            {
                _t += Time.deltaTime;
                if (Shrink)
                {
                    transform.localScale = _scale0 * Mathf.Max(0f, 1f - _t / Life);
                }

                if (_t >= Life)
                {
                    Destroy(gameObject);
                }
            }
        }

        /// <summary>A short-lived spark: arcs out under gravity, shrinks, self-destroys.</summary>
        private sealed class Spark : MonoBehaviour
        {
            public Vector3 Vel;
            public bool Gravity = true;
            public float LifeOverride;
            private float _life = 0.45f;
            private float _t;
            private Vector3 _scale0;

            private void Start()
            {
                _scale0 = transform.localScale;
                if (LifeOverride > 0f)
                {
                    _life = LifeOverride;
                }
            }

            private void Update()
            {
                _t += Time.deltaTime;
                if (Gravity)
                {
                    Vel += Vector3.down * 9f * Time.deltaTime;
                }

                transform.position += Vel * Time.deltaTime;
                transform.localScale = _scale0 * Mathf.Max(0f, 1f - _t / _life);
                if (_t >= _life)
                {
                    Destroy(gameObject);
                }
            }
        }

        /// <summary>A bolt that flies straight to its target, trails sparks, and bursts on arrival.</summary>
        private sealed class Bolt : MonoBehaviour
        {
            public Vector3 Target;
            public Color Color;
            public WeaponFx Fx;
            public float Speed = 55f;
            private float _t;
            private float _trail;

            private void Update()
            {
                _t += Time.deltaTime;
                var pos = transform.position;
                var to = Target - pos;
                float step = Speed * Time.deltaTime;

                if (to.magnitude <= step || _t >= 1.2f)
                {
                    if (Fx != null)
                    {
                        Fx.Flash(Target, Color, 0.26f);
                        Fx.Sparks(Target, Color, 9);
                    }

                    Destroy(gameObject);
                    return;
                }

                transform.position = pos + to.normalized * step;
                transform.rotation = Quaternion.LookRotation(to);

                _trail -= Time.deltaTime;
                if (_trail <= 0f && Fx != null)
                {
                    _trail = 0.03f;
                    var t = Cube("BoltTrail", pos, Vector3.one * 0.07f, Color);
                    t.AddComponent<FadeKill>().Life = 0.12f;
                }
            }
        }

        /// <summary>Sweeps a melee slash arc through ~110° in front of the player, fading as it goes.</summary>
        private sealed class Sweep : MonoBehaviour
        {
            private const float Life = 0.18f;
            private float _t;
            private Quaternion _base;

            private void Start() => _base = transform.rotation;

            private void Update()
            {
                _t += Time.deltaTime;
                float f = Mathf.Clamp01(_t / Life);
                transform.rotation = _base * Quaternion.Euler(0f, Mathf.Lerp(-55f, 55f, f), 0f);

                float k = 0.13f * (1f - f); // shrink the streak as the swing finishes
                foreach (Transform child in transform)
                {
                    child.localScale = Vector3.one * k;
                }

                if (_t >= Life)
                {
                    Destroy(gameObject);
                }
            }
        }
    }
}
