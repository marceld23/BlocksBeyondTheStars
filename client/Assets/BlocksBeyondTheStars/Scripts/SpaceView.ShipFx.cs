// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The own ship's flight effects (#2156/#2157): space dust and trail, the shield bubble and the engine light, one
    /// plume per engine, the engine-flame thrusters and the hostile ships' plumes.
    /// </summary>
    public sealed partial class SpaceView
    {
        /// <summary>The per-frame flight effects (#2157): the ship's velocity feeds the space dust and the flight motion
        /// blur (#2151: its driver was never called), the engines leave a world-space trail and light the hull, a
        /// throttle surge punches the FOV, a badly damaged hull smokes, and a fresh planet-scanner report sweeps its body.</summary>
        private void UpdateFlightFx(float throttle)
        {
            if (_ship == null)
            {
                return;
            }

            var shipWorld = _ship.transform.position;
            Sky.SpaceSunDir = _hasStar && _root != null
                ? _root.transform.TransformPoint(_starLocal) - shipWorld // the hull is lit from the system's star
                : Vector3.zero;
            var vel = _hasLastShipWorld && Time.deltaTime > 1e-4f ? (shipWorld - _lastShipWorld) / Time.deltaTime : Vector3.zero;
            if (vel.sqrMagnitude > 400f * 400f)
            {
                vel = Vector3.zero; // a teleport (resume, jump) is not speed
            }

            _lastShipWorld = shipWorld;
            _hasLastShipWorld = true;
            bool cruising = _phase == Phase.Cruise && !_eva;
            FxSpaceDust.Velocity = cruising ? vel : Vector3.zero;
            UrpScenePost.Instance?.SetMotion(cruising ? Mathf.Clamp01(vel.magnitude / 45f) : 0f);

            // A throttle surge punches the view out a little.
            if (cruising && throttle > 0.85f && _prevThrottle < 0.5f)
            {
                FxCamera.FovPunch(3.5f);
            }

            _prevThrottle = throttle;

            // Engine trail: weightless glow motes left behind at each nozzle while thrusting.
            if (cruising && throttle > 0.05f)
            {
                _trailAcc += Time.deltaTime * (12f + 40f * throttle) * FxKit.Density;
                while (_trailAcc >= 1f)
                {
                    _trailAcc -= 1f;
                    var local = _nozzles.Count > 0 ? _nozzles[Random.Range(0, _nozzles.Count)] : (_exhaust != null ? _exhaust.localPosition : Vector3.zero);
                    var at = _ship.transform.TransformPoint(local);
                    FxKit.Emit(FxKit.Kind.Motes, at, -_ship.transform.forward * (2f + 6f * throttle) + Random.insideUnitSphere * 0.5f,
                        Random.Range(0.12f, 0.24f), Random.Range(0.5f, 0.9f), Color.Lerp(new Color(0.5f, 0.75f, 1f), Color.white, Random.value * 0.4f));
                }
            }

            if (_engineLight != 0 && _phase != Phase.Landing)
            {
                FxLights.Set(_engineLight, new Color(0.55f, 0.8f, 1f), cruising ? 0.6f + 1.6f * throttle : 0.25f);
            }

            // Below half hull the ship smokes and sparks from somewhere on its skin.
            float hullFrac = Game.ShipCombat != null && Game.ShipCombat.HullMax > 0f ? Game.ShipCombat.Hull / Game.ShipCombat.HullMax : 1f;
            if (hullFrac < 0.5f && _phase == Phase.Cruise)
            {
                _smokeTimer -= Time.deltaTime;
                if (_smokeTimer <= 0f)
                {
                    _smokeTimer = Mathf.Lerp(0.05f, 0.3f, hullFrac * 2f) / Mathf.Max(0.35f, FxKit.Density);
                    var b = ShipLocalBounds();
                    var local = b.center + Vector3.Scale(Random.insideUnitSphere, b.extents);
                    var at = _ship.transform.TransformPoint(local);
                    FxKit.Emit(FxKit.Kind.Smoke, at, -_ship.transform.forward * 2f + Random.insideUnitSphere * 0.6f,
                        Random.Range(0.5f, 1f), Random.Range(1f, 1.8f), new Color(0.32f, 0.31f, 0.33f));
                    if (Random.value < 0.35f)
                    {
                        FxKit.Burst(FxKit.Kind.Motes, at, 3, Vector3.zero, 0f, 2f, 5f, 0.06f, 0.1f, 0.2f, 0.4f, new Color(1f, 0.7f, 0.3f));
                    }
                }
            }

            // The planet scanner (#2153): a fresh report sweeps the scanned body and lights its resources on the globe.
            if (Game.PlanetScanVersion != _seenPlanetScan)
            {
                bool first = _seenPlanetScan < 0;
                _seenPlanetScan = Game.PlanetScanVersion;
                var scan = Game.LastPlanetScan;
                if (!first && scan != null && _bodySpheres.TryGetValue(scan.BodyId ?? string.Empty, out var body) && body != null)
                {
                    var ores = new List<Color>();
                    foreach (var ore in scan.Ores ?? System.Array.Empty<BlocksBeyondTheStars.Networking.Messages.NetPlanetOre>())
                    {
                        ores.Add(OreTint(ore.Block));
                        if (ores.Count >= 4)
                        {
                            break;
                        }
                    }

                    var look = FxLook.ForModule(Game.Content, "planet_scanner");
                    SpaceFx.PlanetScan(_ship.transform, body, body.lossyScale.x * 0.5f, look.Color, ores, (scan.BodyId ?? string.Empty).GetHashCode());
                    ClientAudio.Instance?.Cue("terrain_scan", 0.7f);
                }
            }
        }

        /// <summary>The colour a resource glows in on the scanned globe (the terrain scanner's ore tints).</summary>
        private static Color OreTint(string key)
        {
            key ??= string.Empty;
            if (key.Contains("gold")) return new Color(1f, 0.84f, 0.2f);
            if (key.Contains("copper")) return new Color(1f, 0.55f, 0.25f);
            if (key.Contains("iron")) return new Color(0.95f, 0.45f, 0.35f);
            if (key.Contains("titanium")) return new Color(0.8f, 0.85f, 0.95f);
            if (key.Contains("crystal")) return new Color(0.45f, 0.95f, 1f);
            return new Color(1f, 0.75f, 0.3f);
        }

        /// <summary>The ship's bounds in its own local space (cached per ship model) — muzzles, the shield, smoke.</summary>
        private Bounds ShipLocalBounds()
        {
            if (_ship == null)
            {
                return new Bounds(Vector3.zero, Vector3.one * 4f);
            }

            if (_shipBoundsFor == _ship)
            {
                return _shipBounds;
            }

            bool any = false;
            var b = new Bounds(Vector3.zero, Vector3.one);
            foreach (var mf in _ship.GetComponentsInChildren<MeshFilter>(false))
            {
                if (mf.sharedMesh == null || mf.GetComponent<MeshRenderer>() == null || mf.GetComponent<FxShield>() != null)
                {
                    continue;
                }

                var mb = mf.sharedMesh.bounds;
                var m = _ship.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    var lp = m.MultiplyPoint3x4(corner);
                    if (!any)
                    {
                        b = new Bounds(lp, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        b.Encapsulate(lp);
                    }
                }
            }

            _shipBounds = any ? b : new Bounds(Vector3.zero, Vector3.one * 4f);
            _shipBoundsFor = _ship;
            return _shipBounds;
        }

        /// <summary>Hangs the per-ship effects on a freshly built hull: the shield bubble and the engine glow light.</summary>
        private void AttachShipFx()
        {
            if (_ship == null)
            {
                return;
            }

            _shipBoundsFor = null;
            var bounds = ShipLocalBounds();
            _shield = FxShield.Attach(_ship.transform, bounds);
            if (_engineLight != 0)
            {
                FxLights.Remove(_engineLight);
            }

            _engineLight = FxLights.Attach(_ship.transform, EngineLightPoint(bounds), new Color(0.55f, 0.8f, 1f), 0.6f, 9f, flicker: true);
        }

        /// <summary>One plume per ENGINE (#2157, #2162): the hull's engine blocks, grouped into engines by
        /// <c>ShipMeshBuilder.ExhaustPoints</c>, each get a thruster sized by the engine (a 2×2 engine block flames twice as wide as a single nozzle). The biggest one
        /// is the throttle-driven <see cref="_thruster"/>, the rest follow it. The stock ships' engines used to be
        /// plain hull blocks, so this found none and the one centred plume sat on the rear door; a hull without any
        /// engine now flames from its lower rear corners instead.</summary>
        private void BuildNozzlePlumes(Transform ship, Dictionary<Vector3i, BlockId> cells)
        {
            _nozzles.Clear();
            _extraThrusters.Clear();
            _thruster = null;
            _exhaust = null; // the engine blocks glow themselves — no stand-in exhaust cube on the voxel ship
            foreach (var (local, size) in ShipMeshBuilder.ExhaustPoints(Game?.Content, cells, _shipCentre))
            {
                var ps = BuildThruster(ship, local, size);
                _nozzles.Add(local);
                if (ps == null)
                {
                    continue;
                }

                if (_thruster == null)
                {
                    _thruster = ps;
                }
                else
                {
                    _extraThrusters.Add(ps);
                }
            }
        }

        /// <summary>The ship-local point the engine light hangs at: the middle of the exhausts (#2162); the stand-in
        /// model's exhaust cube, or else behind the lower stern, when there are none.</summary>
        private Vector3 EngineLightPoint(Bounds bounds)
        {
            if (_nozzles.Count == 0)
            {
                return _exhaust != null ? _exhaust.localPosition : new Vector3(bounds.center.x, bounds.min.y + 0.5f, bounds.min.z - 0.5f);
            }

            var sum = Vector3.zero;
            foreach (var n in _nozzles)
            {
                sum += n;
            }

            return sum / _nozzles.Count + Vector3.back * 0.4f;
        }

        /// <summary>Engine plumes on the hostile ship models at their engine glow parts (#2157).</summary>
        private static void AttachHostilePlumes(GameObject model, string kind)
        {
            if (kind != "BanditShip" && kind != "Cruiser")
            {
                return; // drones and UFOs hover on repulsors
            }

            var color = kind == "Cruiser" ? new Color(1f, 0.55f, 0.35f) : new Color(1f, 0.35f, 0.25f);
            foreach (Transform child in model.transform)
            {
                if (child.name == "EngineL" || child.name == "EngineR")
                {
                    SpaceFx.EnginePlume(model.transform, child.localPosition + Vector3.back * (child.localScale.z * 0.5f + 0.1f), kind == "Cruiser" ? 1.3f : 0.8f, color);
                }
            }
        }

        /// <summary>Builds the real engine-flame thruster: a backward cone of additive blue-white particles that
        /// fade + shrink, parented at the ship's exhaust vent. Emission + jet speed are driven each frame in
        /// LateUpdate from the throttle. Replaces the old stretched cube + Unlit cube exhaust bits. <paramref name="size"/>
        /// widens and lengthens the jet for a bigger engine (#2162: a 2×2 engine block = 2). Null without the shader.</summary>
        private static ParticleSystem BuildThruster(Transform ship, Vector3 localPos, float size = 1f)
        {
            var shader = Shader.Find("BlocksBeyondTheStars/Particle");
            if (shader == null)
            {
                return null;
            }

            var go = new GameObject("Thruster");
            go.transform.SetParent(ship, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // cone emits along +Z → ship's −Z (astern)

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.startLifetime = 0.35f * (0.6f + 0.4f * size);
            main.startSpeed = 7f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f * size, 0.42f * size);
            main.startColor = new Color(0.6f, 0.85f, 1f, 1f);
            main.maxParticles = 240;
            main.simulationSpace = ParticleSystemSimulationSpace.Local; // the flame stays attached to the engine
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.rateOverTime = 0f; // driven by throttle in LateUpdate

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 11f;
            shape.radius = 0.22f * size;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.85f, 0.93f, 1f), 0f),
                    new GradientColorKey(new Color(0.35f, 0.6f, 1f), 0.55f),
                    new GradientColorKey(new Color(0.15f, 0.3f, 0.8f), 1f),
                },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f));

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = new Material(shader) { mainTexture = ThrusterDot() };
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.None;

            ps.Play();
            return ps;
        }

        private static Texture2D _thrusterDot;

        /// <summary>A soft round flame dot (bright core → transparent rim), shared by every thruster instance.</summary>
        private static Texture2D ThrusterDot()
        {
            if (_thrusterDot != null)
            {
                return _thrusterDot;
            }

            const int n = 16;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
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
            _thrusterDot = tex;
            return tex;
        }
    }
}
