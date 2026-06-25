// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// "Kleinstlebewesen" — tiny, purely-cosmetic micro-fauna that make planets feel alive: fluttering
    /// butterflies / moths / fireflies, crawling beetles / ants / worms, schooling little fish, and glow-worms
    /// clinging to cave ceilings. Entirely client-side (no server / netcode / save) — the same philosophy as
    /// <see cref="AmbientParticles"/>: each client renders its own, they never attack, and nothing is synced.
    ///
    /// A pooled set of camera-facing sprite billboards (one shared atlas material → cheap batching) is kept
    /// populated around the player, gated by biome, day/night and habitat (surface vs in-water vs cave). Three
    /// light-weight motion models drive them: airborne flutter, surface crawl (height-following) and in-water
    /// schooling; cave glow-worms cling near the ceiling and pulse. Suppressed in space / stations / airless
    /// worlds, and thinned out under reduced-effects. Wired up beside the ambient dust in <see cref="WorldRig"/>.
    /// </summary>
    public sealed class MicroFaunaView : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;
        public bool ReducedEffects;

        // --- tuning ---
        private const float CullRadius = 32f;      // despawn beyond this horizontal distance from the player
        private const float SpawnInner = 12f;      // spawn ring: nearest …
        private const float SpawnOuter = 26f;      // … and farthest distance from the player
        private const int BasePopulation = 64;     // target around the player on a normal-richness world by day
        private const int MaxAlive = 150;          // hard ceiling (reduced-effects halves the target, not this)
        private const int SpawnsPerFrame = 3;      // ease the population up rather than popping in all at once
        private const float WaterScanInterval = 1.1f;

        private Transform _container;
        private Material _matAlpha;   // alpha-blended sprites (most critters)
        private Material _matGlow;    // additive sprites (fireflies / glow-worms)
        private readonly List<Critter> _alive = new();
        private readonly Stack<GameObject> _pool = new();

        private readonly List<int> _surfaceKinds = new();
        private readonly List<int> _waterKinds = new();
        private readonly List<Vector3Int> _waterCells = new(); // cached nearby water cells to spawn fish into
        private float _waterScanTimer;
        private int _groupCounter;

        // scratch reused each frame (no per-frame allocations)
        private readonly Dictionary<int, Vector3> _groupSum = new();
        private readonly Dictionary<int, int> _groupCount = new();
        private readonly Dictionary<int, Vector3> _groupCentroid = new();
        private readonly List<Critter> _despawn = new();

        private void Awake()
        {
            var alpha = Shader.Find("BlocksBeyondTheStars/ParticleAlpha");
            var add = Shader.Find("BlocksBeyondTheStars/Particle");
            if (alpha == null || add == null)
            {
                enabled = false; // build-stripped shaders → quietly do nothing rather than throw
                return;
            }

            var atlas = MicroFaunaAtlas.Texture;
            _matAlpha = new Material(alpha) { mainTexture = atlas };
            _matGlow = new Material(add) { mainTexture = atlas };

            var go = new GameObject("MicroFauna");
            go.transform.SetParent(transform, false); // under the world rig → no leak into menus
            _container = go.transform;
        }

        private void Update()
        {
            if (Game?.World == null || Game.Content == null || Camera == null)
            {
                return;
            }

            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            bool enclosed = !Game.ExposedToSky; // in a cave / under a roof
            bool active = ContextAllows(out bool surfaceOk, enclosed);

            if (!active)
            {
                if (_alive.Count > 0) ClearAll();
                return;
            }

            RefreshWaterCells(dt);
            MaintainPopulation(surfaceOk, enclosed);
            MoveAndRender(dt);
        }

        /// <summary>Whether micro-fauna may appear at all right now, and (out) whether surface kinds are valid
        /// here. False in space / stations / airless worlds; surface-off when the player is in a cave.</summary>
        private bool ContextAllows(out bool surfaceOk, bool enclosed)
        {
            surfaceOk = false;
            if (Game.SpaceViewActive || Game.OnFootInSpace || !string.IsNullOrEmpty(Game.StationName))
            {
                return false; // not on a planet surface
            }

            var env = Game.Environment;
            if (env == null || env.SpaceSky)
            {
                return false; // airless / asteroid skies have no air for critters
            }

            // Underground we only show cave glow-worms; in the open we show the surface roster (+ water).
            surfaceOk = !enclosed;
            return true;
        }

        private int TargetPopulation(bool surfaceOk)
        {
            float richness = MicroFauna.Richness(Game.Environment?.Biome);
            float density = ReducedEffects ? 0.42f : 1f;
            float baseN = surfaceOk ? BasePopulation : BasePopulation * 0.35f; // caves are sparser
            int n = Mathf.RoundToInt(baseN * richness * density);
            return Mathf.Clamp(n, 0, ReducedEffects ? MaxAlive / 2 : MaxAlive);
        }

        private void MaintainPopulation(bool surfaceOk, bool enclosed)
        {
            int target = TargetPopulation(surfaceOk);

            // Despawn anything that wandered too far (and over-population after a density drop).
            _despawn.Clear();
            foreach (var c in _alive)
            {
                if (HorizDistance(c.WorldPos) > CullRadius)
                {
                    _despawn.Add(c);
                }
            }

            foreach (var c in _despawn) Recycle(c);
            while (_alive.Count > target) Recycle(_alive[_alive.Count - 1]);

            bool night = IsNight();
            int budget = SpawnsPerFrame;
            while (_alive.Count < target && budget-- > 0)
            {
                if (enclosed)
                {
                    if (!SpawnCave()) break;
                }
                else if (!SpawnSurfaceOrWater(night))
                {
                    break;
                }
            }
        }

        // --- spawning ---------------------------------------------------------------------------------------

        private bool SpawnSurfaceOrWater(bool night)
        {
            // Roughly a third of spawns go to the water roster when a pond/sea is nearby.
            if (_waterCells.Count > 0 && Random.value < 0.33f)
            {
                return SpawnWater();
            }

            MicroFauna.SurfaceKinds(Game.Environment?.Biome, night, _surfaceKinds);
            if (_surfaceKinds.Count == 0) return false;
            int kindIdx = _surfaceKinds[Random.Range(0, _surfaceKinds.Count)];
            var kind = MicroFauna.Kinds[kindIdx];

            if (!RingPosition(out float wx, out float wz)) return false;
            int gy = GroundTopY(Mathf.FloorToInt(wx), Mathf.FloorToInt(wz));
            if (gy == int.MinValue) return false;

            float baseY = gy + 1f;
            if (kind.Motion == CritterMotion.Fly) baseY = gy + 1.5f + Random.Range(0.5f, 2.2f);

            int group = kind.Groups && Random.value < 0.7f ? ++_groupCounter : -1;
            int n = group >= 0 ? Random.Range(4, kind.Motion == CritterMotion.Fly ? 9 : 6) : 1;
            for (int i = 0; i < n && _alive.Count < MaxAlive; i++)
            {
                float jx = wx + Random.Range(-1.6f, 1.6f), jz = wz + Random.Range(-1.6f, 1.6f);
                float y = baseY + (kind.Motion == CritterMotion.Fly ? Random.Range(-0.4f, 0.6f) : 0f);
                if (kind.Motion != CritterMotion.Fly)
                {
                    int g2 = GroundTopY(Mathf.FloorToInt(jx), Mathf.FloorToInt(jz));
                    if (g2 != int.MinValue) y = g2 + 0.12f;
                }

                Spawn(kindIdx, new Vector3(jx, y, jz), group, gy + 1f);
            }

            return true;
        }

        private bool SpawnWater()
        {
            if (_waterCells.Count == 0) return false;
            MicroFauna.WaterKinds(_waterKinds);
            int kindIdx = _waterKinds[Random.Range(0, _waterKinds.Count)];
            var kind = MicroFauna.Kinds[kindIdx];

            var cell = _waterCells[Random.Range(0, _waterCells.Count)];
            int group = kind.Groups && Random.value < 0.8f ? ++_groupCounter : -1;
            int n = group >= 0 ? Random.Range(4, 10) : 1;
            for (int i = 0; i < n && _alive.Count < MaxAlive; i++)
            {
                float jx = cell.x + 0.5f + Random.Range(-1.4f, 1.4f);
                float jz = cell.z + 0.5f + Random.Range(-1.4f, 1.4f);
                float jy = cell.y + 0.5f + Random.Range(-0.8f, 0.8f);
                if (!IsWater(Mathf.FloorToInt(jx), Mathf.FloorToInt(jy), Mathf.FloorToInt(jz))) { jx = cell.x + 0.5f; jz = cell.z + 0.5f; jy = cell.y + 0.5f; }
                Spawn(kindIdx, new Vector3(jx, jy, jz), group, cell.y + 0.5f);
            }

            return true;
        }

        private bool SpawnCave()
        {
            // Glow-worms cling just under a cave ceiling; the odd cave beetle scuttles the floor.
            bool worm = Random.value < 0.75f;
            int kindIdx = worm ? MicroFauna.Index("glowworm") : MicroFauna.Index("beetle");

            if (!RingPosition(out float wx, out float wz)) return false;
            int ix = Mathf.FloorToInt(wx), iz = Mathf.FloorToInt(wz);

            if (worm)
            {
                int ceil = CeilingY(ix, iz);
                if (ceil == int.MinValue) return false;
                int group = Random.value < 0.6f ? ++_groupCounter : -1;
                int n = group >= 0 ? Random.Range(3, 7) : 1;
                for (int i = 0; i < n && _alive.Count < MaxAlive; i++)
                {
                    float jx = wx + Random.Range(-1.2f, 1.2f), jz = wz + Random.Range(-1.2f, 1.2f);
                    Spawn(kindIdx, new Vector3(jx, ceil - 0.4f - Random.Range(0f, 0.5f), jz), group, ceil - 0.4f);
                }

                return true;
            }

            int gy = GroundTopY(ix, iz);
            if (gy == int.MinValue) return false;
            Spawn(kindIdx, new Vector3(wx, gy + 0.12f, wz), -1, gy + 1f);
            return true;
        }

        private bool RingPosition(out float wx, out float wz)
        {
            float ang = Random.value * Mathf.PI * 2f;
            float dist = Random.Range(SpawnInner, SpawnOuter);
            wx = Game.PlayerPosition.x + Mathf.Cos(ang) * dist;
            wz = Game.PlayerPosition.z + Mathf.Sin(ang) * dist;
            return true;
        }

        private void Spawn(int kindIdx, Vector3 worldPos, int group, float baseY)
        {
            var kind = MicroFauna.Kinds[kindIdx];
            var c = Acquire();
            c.KindIndex = kindIdx;
            c.Kind = kind;
            c.WorldPos = worldPos;
            c.BaseY = baseY;
            c.Heading = Random.value * Mathf.PI * 2f;
            c.Speed = kind.Speed * Random.Range(0.8f, 1.2f);
            c.Phase = Random.value * 10f;
            c.BobPhase = Random.value * Mathf.PI * 2f;
            c.FlapPhase = Random.value * Mathf.PI * 2f;
            c.RepathTimer = Random.Range(0.6f, 2.5f);
            c.PauseTimer = 0f;
            c.Group = group;
            c.FacingSign = 1f;
            // Glowing kinds keep their full saturated colour (additive). For the rest the tint is softened
            // toward white so the generated sprite's own colours dominate (a hint of variety, not a muddy
            // multiply) — while a procedural white-silhouette fallback still picks up a pastel of the palette.
            Color pick = kind.Palette[Random.Range(0, kind.Palette.Length)];
            c.Tint = kind.Glow ? pick : Color.Lerp(Color.white, pick, 0.5f);

            BuildQuad(c);
            c.Mr.sharedMaterial = kind.Glow ? _matGlow : _matAlpha;
            c.Go.SetActive(true);
            _alive.Add(c);
        }

        // --- movement + render -----------------------------------------------------------------------------

        private void MoveAndRender(float dt)
        {
            ComputeGroupCentroids();

            Vector3 camPos = Camera.transform.position;
            Vector3 camUp = Camera.transform.up;
            Vector3 camRight = Camera.transform.right;

            foreach (var c in _alive)
            {
                switch (c.Kind.Motion)
                {
                    case CritterMotion.Fly: MoveFly(c, dt); break;
                    case CritterMotion.Crawl: MoveCrawl(c, dt); break;
                    case CritterMotion.Swim: MoveSwim(c, dt); break;
                    case CritterMotion.Cling: MoveCling(c, dt); break;
                }

                Render(c, camPos, camUp, camRight);
            }
        }

        private void MoveFly(Critter c, float dt)
        {
            c.Phase += dt;
            c.FlapPhase += dt * 18f;
            Steer(c, dt, weave: 1.4f, cohesion: 0.6f);

            Vector3 vel = Heading(c) * c.Speed;
            c.WorldPos.x += vel.x * dt;
            c.WorldPos.z += vel.z * dt;

            // Gentle altitude wave around a ground-relative cruising height.
            c.BobPhase += dt * 2.2f;
            int gy = GroundTopY(Mathf.FloorToInt(c.WorldPos.x), Mathf.FloorToInt(c.WorldPos.z));
            if (gy != int.MinValue) c.BaseY = Mathf.Lerp(c.BaseY, gy + 2.0f, 0.04f);
            c.WorldPos.y = c.BaseY + Mathf.Sin(c.BobPhase) * 0.45f;
            c.LastVelX = vel.x;
        }

        private void MoveCrawl(Critter c, float dt)
        {
            c.Phase += dt;
            if (c.PauseTimer > 0f)
            {
                c.PauseTimer -= dt;
                c.LastVelX = 0f;
                return; // foraging pause
            }

            Steer(c, dt, weave: 0.8f, cohesion: 0.25f);
            Vector3 vel = Heading(c) * c.Speed;
            float nx = c.WorldPos.x + vel.x * dt, nz = c.WorldPos.z + vel.z * dt;
            int gy = GroundTopY(Mathf.FloorToInt(nx), Mathf.FloorToInt(nz));
            if (gy == int.MinValue || Mathf.Abs((gy + 0.12f) - c.WorldPos.y) > 1.6f)
            {
                c.Heading += Mathf.PI * 0.6f; // a ledge / wall — turn away
                c.LastVelX = 0f;
                return;
            }

            c.WorldPos.x = nx; c.WorldPos.z = nz;
            c.WorldPos.y = Mathf.Lerp(c.WorldPos.y, gy + 0.12f, 0.3f);
            c.LastVelX = vel.x;

            c.RepathTimer -= dt;
            if (c.RepathTimer <= 0f)
            {
                c.RepathTimer = Random.Range(1.2f, 3.5f);
                if (Random.value < 0.35f) c.PauseTimer = Random.Range(0.6f, 2.2f);
            }
        }

        private void MoveSwim(Critter c, float dt)
        {
            c.Phase += dt;
            c.FlapPhase += dt * 8f;
            Steer(c, dt, weave: 1.0f, cohesion: 0.9f); // schools cohere strongly

            Vector3 vel = Heading(c) * c.Speed;
            float nx = c.WorldPos.x + vel.x * dt, nz = c.WorldPos.z + vel.z * dt;
            c.BobPhase += dt * 1.6f;
            float ny = c.WorldPos.y + Mathf.Sin(c.BobPhase) * 0.12f * dt * 10f;

            if (IsWater(Mathf.FloorToInt(nx), Mathf.FloorToInt(ny), Mathf.FloorToInt(nz)))
            {
                c.WorldPos.x = nx; c.WorldPos.z = nz; c.WorldPos.y = ny;
                c.LastVelX = vel.x;
            }
            else
            {
                c.Heading += Mathf.PI * (0.7f + Random.value * 0.6f); // turn back into the water
                c.LastVelX = 0f;
            }
        }

        private void MoveCling(Critter c, float dt)
        {
            // Glow-worms barely move; they sway a hair and pulse their glow.
            c.Phase += dt;
            c.BobPhase += dt * 1.1f;
            c.WorldPos.x += Mathf.Sin(c.Phase * 0.7f) * 0.03f * dt;
            c.LastVelX = 0f;
        }

        /// <summary>Shared wander steering: random heading drift, a sine weave, and (for grouped critters) a pull
        /// toward the swarm/school centroid plus a soft turn back toward the player when straying too far.</summary>
        private void Steer(Critter c, float dt, float weave, float cohesion)
        {
            c.RepathTimer -= dt;
            if (c.RepathTimer <= 0f)
            {
                c.RepathTimer = Random.Range(0.8f, 2.6f);
                c.Heading += Random.Range(-0.9f, 0.9f);
            }

            c.Heading += Mathf.Sin(c.Phase * 2.3f) * weave * dt;

            if (c.Group >= 0 && cohesion > 0f && _groupCentroid.TryGetValue(c.Group, out var centroid))
            {
                Vector3 to = centroid - c.WorldPos;
                float d = new Vector2(to.x, to.z).magnitude;
                if (d > 2.5f)
                {
                    float want = Mathf.Atan2(to.z, to.x);
                    c.Heading = Mathf.LerpAngle(c.Heading * Mathf.Rad2Deg, want * Mathf.Rad2Deg, cohesion * dt) * Mathf.Deg2Rad;
                }
            }

            // Keep the cloud near the player so it doesn't all drift off and despawn.
            Vector3 sp = Game.ScenePos(c.WorldPos.x, c.WorldPos.y, c.WorldPos.z);
            float dx = sp.x - Game.PlayerPosition.x, dz = sp.z - Game.PlayerPosition.z;
            if (dx * dx + dz * dz > (CullRadius - 4f) * (CullRadius - 4f))
            {
                float home = Mathf.Atan2(-dz, -dx);
                c.Heading = Mathf.LerpAngle(c.Heading * Mathf.Rad2Deg, home * Mathf.Rad2Deg, 2f * dt) * Mathf.Deg2Rad;
            }
        }

        private static Vector3 Heading(Critter c) => new(Mathf.Cos(c.Heading), 0f, Mathf.Sin(c.Heading));

        private void Render(Critter c, Vector3 camPos, Vector3 camUp, Vector3 camRight)
        {
            Vector3 sp = Game.ScenePos(c.WorldPos.x, c.WorldPos.y, c.WorldPos.z);
            c.Go.transform.position = sp;
            c.Go.transform.rotation = Quaternion.LookRotation(sp - camPos, camUp);

            float size = c.Kind.Size * 2f;
            float flap = 1f;
            if (c.Kind.Motion == CritterMotion.Fly) flap = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(c.FlapPhase));
            else if (c.Kind.Motion == CritterMotion.Swim) flap = 0.85f + 0.15f * Mathf.Sin(c.FlapPhase);

            // Glow kinds pulse their scale so the light reads as a soft blink.
            float pulse = c.Kind.Glow ? 0.7f + 0.3f * Mathf.Sin(c.Phase * (c.Kind.Motion == CritterMotion.Cling ? 1.6f : 4f)) : 1f;

            // Face travel direction on screen by flipping horizontally.
            if (Mathf.Abs(c.LastVelX) > 0.01f)
            {
                Vector3 velScene = new(c.LastVelX, 0f, 0f);
                c.FacingSign = Vector3.Dot(velScene, camRight) >= 0f ? 1f : -1f;
            }

            c.Go.transform.localScale = new Vector3(size * flap * pulse * c.FacingSign, size * pulse, 1f);
        }

        private void ComputeGroupCentroids()
        {
            _groupSum.Clear();
            _groupCount.Clear();
            _groupCentroid.Clear();
            foreach (var c in _alive)
            {
                if (c.Group < 0) continue;
                _groupSum.TryGetValue(c.Group, out var s);
                _groupSum[c.Group] = s + c.WorldPos;
                _groupCount.TryGetValue(c.Group, out int n);
                _groupCount[c.Group] = n + 1;
            }

            foreach (var kv in _groupSum)
            {
                _groupCentroid[kv.Key] = kv.Value / Mathf.Max(1, _groupCount[kv.Key]);
            }
        }

        // --- world sampling ---------------------------------------------------------------------------------

        private void RefreshWaterCells(float dt)
        {
            _waterScanTimer -= dt;
            if (_waterScanTimer > 0f) return;
            _waterScanTimer = WaterScanInterval;

            _waterCells.Clear();
            var p = Game.PlayerPosition;
            int px = Mathf.FloorToInt(p.x), py = Mathf.FloorToInt(p.y), pz = Mathf.FloorToInt(p.z);
            const int r = 14;
            for (int dx = -r; dx <= r; dx += 2)
            for (int dz = -r; dz <= r; dz += 2)
            for (int dy = -6; dy <= 3; dy += 2)
            {
                int wx = px + dx, wy = py + dy, wz = pz + dz;
                if (IsWater(wx, wy, wz) && _waterCells.Count < 48)
                {
                    _waterCells.Add(new Vector3Int(wx, wy, wz));
                }
            }
        }

        /// <summary>Top solid surface Y near the player's vertical band at a column, or int.MinValue if none.</summary>
        private int GroundTopY(int wx, int wz)
        {
            int py = Mathf.FloorToInt(Game.PlayerPosition.y);
            for (int y = py + 8; y >= py - 12; y--)
            {
                if (!Game.World.GetBlock(wx, y, wz).IsAir && Game.World.GetBlock(wx, y + 1, wz).IsAir)
                {
                    return y;
                }
            }

            return int.MinValue;
        }

        /// <summary>First solid ceiling Y above the player at a column (for hanging glow-worms), or int.MinValue.</summary>
        private int CeilingY(int wx, int wz)
        {
            int py = Mathf.FloorToInt(Game.PlayerPosition.y);
            for (int y = py + 2; y <= py + 14; y++)
            {
                if (!Game.World.GetBlock(wx, y, wz).IsAir && Game.World.GetBlock(wx, y - 1, wz).IsAir)
                {
                    return y;
                }
            }

            return int.MinValue;
        }

        private bool IsWater(int wx, int wy, int wz)
            => Game.Content.BlockById(Game.World.GetBlock(wx, wy, wz))?.Key == "water";

        private bool IsNight()
        {
            float t = Game.LocalTimeOfDay;
            return t < 0.24f || t > 0.78f;
        }

        private float HorizDistance(Vector3 worldPos)
        {
            Vector3 sp = Game.ScenePos(worldPos.x, worldPos.y, worldPos.z);
            float dx = sp.x - Game.PlayerPosition.x, dz = sp.z - Game.PlayerPosition.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // --- pooling + mesh ---------------------------------------------------------------------------------

        private Critter Acquire()
        {
            GameObject go;
            if (_pool.Count > 0)
            {
                go = _pool.Pop();
            }
            else
            {
                go = new GameObject("critter");
                go.transform.SetParent(_container, false);
                go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            var c = new Critter
            {
                Go = go,
                Mf = go.GetComponent<MeshFilter>(),
                Mr = go.GetComponent<MeshRenderer>(),
            };
            return c;
        }

        private void Recycle(Critter c)
        {
            _alive.Remove(c);
            if (c.Mesh != null) Destroy(c.Mesh);
            if (c.Go != null)
            {
                c.Go.SetActive(false);
                _pool.Push(c.Go);
            }
        }

        private void ClearAll()
        {
            foreach (var c in _alive)
            {
                if (c.Mesh != null) Destroy(c.Mesh);
                if (c.Go != null) { c.Go.SetActive(false); _pool.Push(c.Go); }
            }

            _alive.Clear();
        }

        private void BuildQuad(Critter c)
        {
            if (c.Mesh == null)
            {
                c.Mesh = new Mesh { name = "critter_quad" };
                c.Mf.sharedMesh = c.Mesh;
            }

            Rect uv = MicroFaunaAtlas.UvRect(c.Kind.Tile);
            var verts = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            };
            var uvs = new[]
            {
                new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMax, uv.yMin),
                new Vector2(uv.xMax, uv.yMax), new Vector2(uv.xMin, uv.yMax),
            };
            Color tint = ShaderColor.Srgb(c.Tint);
            var cols = new[] { tint, tint, tint, tint };
            var tris = new[] { 0, 2, 1, 0, 3, 2 };

            c.Mesh.Clear();
            c.Mesh.vertices = verts;
            c.Mesh.uv = uvs;
            c.Mesh.colors = cols;
            c.Mesh.triangles = tris;
            c.Mesh.RecalculateBounds();
        }

        private sealed class Critter
        {
            public GameObject Go;
            public MeshFilter Mf;
            public MeshRenderer Mr;
            public Mesh Mesh;
            public int KindIndex;
            public CritterKind Kind;
            public Vector3 WorldPos;
            public float BaseY;        // ground-relative cruising / cling height
            public float Heading;      // radians, atan2(z, x)
            public float Speed;
            public float Phase;
            public float BobPhase;
            public float FlapPhase;
            public float RepathTimer;
            public float PauseTimer;
            public int Group;          // swarm/school id, or -1 for a loner
            public float FacingSign;
            public float LastVelX;
            public Color Tint;
        }
    }
}
