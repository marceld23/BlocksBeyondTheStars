// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Mining/placing feedback (M27 polish): a wireframe selection box on the block the player is
    /// looking at, and a small debris burst when a block is mined or placed. Code-built (12 thin
    /// edge cubes + cube particles) on the always-included Unlit/Color shader — no assets, no new
    /// shader. Render-only; the server stays authoritative over the actual block changes.
    /// </summary>
    public sealed class MiningFx : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;
        public float Reach = 6f;

        private GameObject _outline;
        private Material _outlineMat;
        private Material _digMat;
        private Material _placeMat;
        private Material _flashMat;
        private bool _subscribed;

        private void Start()
        {
            _outlineMat = Mat(new Color(0.05f, 0.05f, 0.06f));
            _digMat = Mat(new Color(0.65f, 0.58f, 0.48f));
            _placeMat = Mat(new Color(0.80f, 0.85f, 0.95f));
            _flashMat = Mat(new Color(1f, 0.86f, 0.5f));
            _outline = BuildWireCube(_outlineMat);
            _outline.SetActive(false);
        }

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.BlockChanged += OnBlock;
                Game.Network.MiningProgressReceived += OnMineProgress;
                _subscribed = true;
            }

            UpdateOutline();
        }

        private void UpdateOutline()
        {
            if (_outline == null || Camera == null || Game == null || Game.MenuOpen || Game.SpaceViewActive)
            {
                if (_outline != null)
                {
                    _outline.SetActive(false);
                }

                return;
            }

            if (AimVoxel(out int bx, out int by, out int bz))
            {
                _outline.transform.position = new Vector3(bx + 0.5f, by + 0.5f, bz + 0.5f);
                _outline.SetActive(true);

                // Tint the box from dark toward hot orange as the block cracks under the drill.
                float frac = (bx == _crackX && by == _crackY && bz == _crackZ && Time.time - _crackAt < 0.4f) ? _crackFrac : 0f;
                _outlineMat.color = Color.Lerp(new Color(0.05f, 0.05f, 0.06f), new Color(1f, 0.45f, 0.12f), frac);
            }
            else
            {
                _outline.SetActive(false);
            }
        }

        /// <summary>Marches the voxel grid (Amanatides &amp; Woo) along the aim ray and returns the first
        /// targetable cell within <see cref="Reach"/> — mirroring <c>PlayerController.AimTarget</c> so the
        /// selection box highlights EXACTLY the block the mine/place click would hit (voxel world OR a parked
        /// ship cell). Reading the world directly instead of <see cref="Physics.Raycast"/> is what kills the
        /// "black outline flickers in the air" glitch: the old ray could snap the box onto a creature/ship/
        /// speeder collider hovering in mid-air, linger on a just-mined cell whose collider hadn't re-baked
        /// yet, or blink off for a frame while a chunk collider was mid-rebuild (the same B32 desync the drill
        /// already side-steps). The march sees none of that — it only knows the authoritative block data.</summary>
        private bool AimVoxel(out int bx, out int by, out int bz)
        {
            bx = by = bz = 0;
            if (Game?.World == null || Camera == null)
            {
                return false;
            }

            Vector3 o = Camera.transform.position;
            Vector3 dir = Camera.transform.forward;
            int x = Mathf.FloorToInt(o.x), y = Mathf.FloorToInt(o.y), z = Mathf.FloorToInt(o.z);

            int sx = dir.x >= 0 ? 1 : -1, sy = dir.y >= 0 ? 1 : -1, sz = dir.z >= 0 ? 1 : -1;
            float invx = Mathf.Abs(dir.x) > 1e-6f ? 1f / Mathf.Abs(dir.x) : float.PositiveInfinity;
            float invy = Mathf.Abs(dir.y) > 1e-6f ? 1f / Mathf.Abs(dir.y) : float.PositiveInfinity;
            float invz = Mathf.Abs(dir.z) > 1e-6f ? 1f / Mathf.Abs(dir.z) : float.PositiveInfinity;
            float tMaxX = float.IsInfinity(invx) ? float.PositiveInfinity : (dir.x > 0 ? (x + 1 - o.x) : (o.x - x)) * invx;
            float tMaxY = float.IsInfinity(invy) ? float.PositiveInfinity : (dir.y > 0 ? (y + 1 - o.y) : (o.y - y)) * invy;
            float tMaxZ = float.IsInfinity(invz) ? float.PositiveInfinity : (dir.z > 0 ? (z + 1 - o.z) : (o.z - z)) * invz;

            float t = 0f;
            for (int i = 0; i < 80 && t <= Reach; i++)
            {
                var id = Game.World.GetBlock(x, y, z);
                if ((!id.IsAir && !IsFluid(id)) || !Game.LandedShipBlockAt(x, y, z, out _, out _).IsAir)
                {
                    bx = x; by = y; bz = z;
                    return true;
                }

                if (tMaxX <= tMaxY && tMaxX <= tMaxZ) { x += sx; t = tMaxX; tMaxX += invx; }
                else if (tMaxY <= tMaxZ) { y += sy; t = tMaxY; tMaxY += invy; }
                else { z += sz; t = tMaxZ; tMaxZ += invz; }
            }

            return false;
        }

        /// <summary>Water/lava are passed through when aiming (no collider — you swim/sink into them), matching
        /// the mine/place march so the box never sits on a fluid you cannot target.</summary>
        private bool IsFluid(BlocksBeyondTheStars.Shared.Primitives.BlockId id)
        {
            var key = Game?.Content?.BlockById(id)?.Key;
            return key is "water" or "lava";
        }

        private int _crackX, _crackY, _crackZ;
        private float _crackFrac, _crackAt;
        private BlocksBeyondTheStars.Shared.Primitives.BlockId _crackBlock; // what is being mined (sampled pre-break)

        private void OnMineProgress(MiningProgress m)
        {
            _crackX = m.X;
            _crackY = m.Y;
            _crackZ = m.Z;
            _crackFrac = Mathf.Clamp01(m.Fraction);
            _crackAt = Time.time;
            if (Game?.World != null)
            {
                // Sample the block while it still exists — by the time BlockChanged arrives the world
                // has already been updated, so this is the only place the mined id is observable.
                _crackBlock = Game.World.GetBlock(m.X, m.Y, m.Z);
            }
        }

        private void OnBlock(BlockChanged m)
        {
            var pos = new Vector3(m.X + 0.5f, m.Y + 0.5f, m.Z + 0.5f);
            SpawnBurst(pos, m.Block == 0 ? _digMat : _placeMat);
            if (m.Block != 0)
            {
                return;
            }

            FlashAt(pos); // the final-hit pop
            if (m.X == _crackX && m.Y == _crackY && m.Z == _crackZ && _crackBlock.Value != 0)
            {
                HudUi.Instance?.FlyPickup(pos, _crackBlock); // mined tile flies into the hotbar
                _crackBlock = default;
            }
        }

        /// <summary>A bright one-shot pop slightly proud of the broken block — the "final hit" flash.</summary>
        private void FlashAt(Vector3 pos)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            StripCollider(p);
            p.transform.position = pos;
            p.transform.localScale = Vector3.one * 1.06f;
            p.GetComponent<Renderer>().sharedMaterial = _flashMat;
            p.AddComponent<FlashFx>();
        }

        private static Material _burstMat;

        /// <summary>A small debris/dust puff when a block is mined or placed: a one-shot ParticleSystem burst of
        /// soft alpha bits in the dig/place colour that arc out under gravity and fade, then self-destroys. Replaces
        /// the old Unlit debris cubes.</summary>
        private void SpawnBurst(Vector3 pos, Material mat)
        {
            var shader = Shader.Find("BlocksBeyondTheStars/ParticleAlpha");
            if (shader == null)
            {
                return;
            }

            _burstMat ??= new Material(shader) { mainTexture = SoftDot() };

            var go = new GameObject("MineBurst");
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.duration = 0.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            main.startColor = mat != null ? mat.color : new Color(0.6f, 0.55f, 0.45f, 1f);
            main.maxParticles = 24;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.8f; // chips arc and fall
            main.stopAction = ParticleSystemStopAction.Destroy;

            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)10) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere; // bias the spray upward/outward from the face
            shape.radius = 0.25f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.4f));

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = _burstMat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.None;
            ps.Play();
        }

        private static Texture2D _softDot;

        /// <summary>A soft round dot (opaque core → transparent rim) for the debris puff bits.</summary>
        private static Texture2D SoftDot()
        {
            if (_softDot != null)
            {
                return _softDot;
            }

            const int n = 16;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Clamp01(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c);
                px[y * n + x] = new Color(1f, 1f, 1f, Mathf.SmoothStep(1f, 0f, d));
            }

            tex.SetPixels(px);
            tex.Apply();
            _softDot = tex;
            return tex;
        }

        private static GameObject BuildWireCube(Material mat)
        {
            const float t = 0.05f;   // edge thickness
            const float s = 1.04f;    // edge length (slightly proud of the block)
            var root = new GameObject("BlockOutline");

            // Twelve edges of a unit cube centred on the root: 4 along each axis.
            for (int a = 0; a < 3; a++)
            {
                for (int i = 0; i < 4; i++)
                {
                    float u = (i & 1) == 0 ? -0.5f : 0.5f;
                    float v = (i & 2) == 0 ? -0.5f : 0.5f;
                    Vector3 pos, scale;
                    if (a == 0) { pos = new Vector3(0f, u, v); scale = new Vector3(s, t, t); }
                    else if (a == 1) { pos = new Vector3(u, 0f, v); scale = new Vector3(t, s, t); }
                    else { pos = new Vector3(u, v, 0f); scale = new Vector3(t, t, s); }

                    var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    StripCollider(edge);
                    edge.transform.SetParent(root.transform, false);
                    edge.transform.localPosition = pos;
                    edge.transform.localScale = scale;
                    edge.GetComponent<Renderer>().sharedMaterial = mat;
                }
            }

            return root;
        }

        private static Material Mat(Color c)
        {
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque");
            return new Material(shader) { color = ShaderColor.Srgb(c) };
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }
        }

        /// <summary>The final-hit flash: a bright shell that swells slightly and vanishes in ~0.12 s.</summary>
        private sealed class FlashFx : MonoBehaviour
        {
            private const float Life = 0.12f;
            private float _t;

            private void Update()
            {
                _t += Time.deltaTime;
                transform.localScale = Vector3.one * Mathf.Lerp(1.06f, 1.22f, _t / Life);
                if (_t >= Life)
                {
                    Destroy(gameObject);
                }
            }
        }

        /// <summary>A short-lived debris cube: arcs out under gravity, shrinks, then self-destroys.</summary>
        private sealed class FxParticle : MonoBehaviour
        {
            public Vector3 Vel;

            private const float Life = 0.6f;
            private float _t;

            private void Update()
            {
                _t += Time.deltaTime;
                Vel += Vector3.down * 9f * Time.deltaTime;
                transform.position += Vel * Time.deltaTime;
                transform.localScale = Vector3.one * 0.12f * Mathf.Max(0f, 1f - _t / Life);
                if (_t >= Life)
                {
                    Destroy(gameObject);
                }
            }
        }
    }
}
