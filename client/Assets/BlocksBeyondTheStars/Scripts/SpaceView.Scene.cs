// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;
using static BlocksBeyondTheStars.Client.ModelKit;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Building the flight scene: the star system's bodies and their live look (clouds, haze, storms — #2175), the sun,
    /// and the player's ship as a cube fallback or a voxel mesh.
    /// </summary>
    public sealed partial class SpaceView
    {
        private void BuildScene()
        {
            _root = new GameObject("SpaceScene");
            _root.transform.position = SceneOrigin;
            _keepOut.Clear();

            // Background stars are drawn by the shared Starfield dome (soft round dots, varied colours, a
            // gentle twinkle) which follows the camera and is at full brightness in space — so the space view
            // no longer spawns its own blocky star cubes.
            BuildSun(); // the system's star, in its own colour (lens flare added in LateUpdate)

            // Only REAL bodies: the planet you launched from + the system's actual planets/moons (from the
            // star map), all textured + landable. No decorative filler planets.
            BuildSystemBodies();

            _ship = BuildShip(_root.transform);
            AttachShipFx();
        }

        /// <summary>Places the other bodies of the current star system at their (scaled) system coordinates,
        /// relative to the body you launched from, so you can fly between them and land on any one.</summary>
        private void BuildSystemBodies()
        {
            _landables.Clear();
            _asteroidBodies.Clear();
            _bounds = Bounds;

            var map = Game?.StarMap;
            BlocksBeyondTheStars.Networking.Messages.NetBody current = null;
            BlocksBeyondTheStars.Networking.Messages.NetStarSystem system = null;
            if (map?.Systems != null)
            {
                foreach (var sys in map.Systems)
                {
                    foreach (var b in sys.Bodies)
                    {
                        if (b.Id == map.ActiveLocationId)
                        {
                            current = b;
                            system = sys;
                            break;
                        }
                    }

                    if (system != null)
                    {
                        break;
                    }
                }
            }

            // The star sits at the system origin; the scene is centred on the body you launched from, so the
            // star renders at -currentSystemPos·scale. Bodies are sun-lit from here (true per-body phase), and
            // the visible sun billboard is aimed this way too. Without a star map we keep the stylised fixed sun.
            _hasStar = current != null;
            _starLocal = _hasStar
                ? new Vector3(-current.SystemX * SystemViewScale, 0f, -current.SystemZ * SystemViewScale)
                : Vector3.zero;

            // The planet you launched from: a real body, rendered "below" you (you rose off its surface),
            // landable (E returns home). Always present, even without a star map, so there is always a real
            // planet to fly back to — never a decorative filler.
            string homeType = !string.IsNullOrEmpty(current?.PlanetType) ? current.PlanetType : Game?.Environment?.Biome;
            var homePos = new Vector3(0f, -150f, -20f);
            // Every body has its own size (the server's walkable circumference, #2691 — incl. the archetype bias), so
            // worlds + moons look big or small in orbit instead of all identical — an approximate reflection of how varied
            // they are.
            // The home body IS the world we launched from: missing from the star map, it takes the size the server sent for it.
            int homeCirc = BodySize.CircumferenceOf(current, current?.Id ?? Game?.LocationName ?? "home", current?.Kind, current?.PlanetType,
                Game?.Circumference ?? 0);
            float homeDiameter = OrbitDiameterFor(homeCirc) * 3.2f;
            // Label the home body by what the star map calls it; LocationName only as a fallback — after an
            // in-flight hyperjump it still named the DEPARTURE planet, so the alien anchor world was drawn and
            // land-prompted as "Crossway Reach · Seana" (#1565).
            string homeName = !string.IsNullOrEmpty(current?.Name) ? current.Name : (Game?.LocationName ?? "home");
            // #2170: the flora key is "System · Body" like every other body — the bare name rolled a different
            // vegetation hue than the plants on the ground you just left.
            string homeFloraKey = current != null ? PlanetOrbitLook.LocationKeyFor(system?.Name, homeName) : homeName;
            SpawnBody("HomePlanet", current?.Id, current?.Kind, homeFloraKey, homePos, homeDiameter, homeType, homeCirc, current?.RingSeed ?? 0);
            // #2952: the label is the player's own name for the body; homeName above stays the flora key's name.
            _landables.Add((string.Empty, current.IsRenamed() ? current.Shown() : homeName, homePos, homeDiameter * 0.5f));
            _keepOut.Add((homePos, homeDiameter * 0.5f + KeepOutMargin));
            float maxDist = homePos.magnitude;

            // The system's other planets/moons at their (scaled) orbit coords — all real, all landable.
            if (current != null && system != null)
            {
                // Plan every body first (positions + radii), then nudge any overlaps apart, THEN spawn — so no
                // two planets/moons ever clip into each other at the compact view scale. How much clear space
                // "apart" means comes from SystemBodyLayout: proportional to the two bodies, because a flat gap
                // left every moon hugging its planet (#493).
                var ids = new List<string>();
                var names = new List<string>();
                var locKeys = new List<string>(); // "System · Body" — the key the body's WORLD seeds flora hues with
                var positions = new List<Vector3>();
                var radii = new List<float>();
                var bodyTypes = new List<string>();
                var kinds = new List<string>(); // star-map Kind — keys each body's true circumference
                var circs = new List<int>(); // per-body walkable circumference (#2691: the server's value) — keys the map bake
                var rings = new List<int>(); // per-planet ring style seed (#596), 0 = no rings
                var homeMoons = new List<int>(); // planned in home's plane → must also clear home itself

                void Plan(string id, string name, string kind, Vector3 pos, float diameter, string type, int circumference, int ringSeed = 0)
                {
                    ids.Add(id); names.Add(name); kinds.Add(kind); positions.Add(pos); radii.Add(diameter * 0.5f); bodyTypes.Add(type);
                    circs.Add(circumference);
                    rings.Add(ringSeed);
                    locKeys.Add(PlanetOrbitLook.LocationKeyFor(system?.Name, name));
                }

                // Pass 1: planets at their scaled orbit coords. Keep system coords + render pos so each moon
                // can be parented to its planet (by ParentId, with a nearest-planet fallback).
                var planets = new List<(float Sx, float Sz, Vector3 Render, float Radius)>
                {
                    (current.SystemX, current.SystemZ, homePos, homeDiameter * 0.5f),
                };
                var planetIndexById = new Dictionary<string, int> { [current.Id ?? string.Empty] = 0 };
                foreach (var b in system.Bodies)
                {
                    // Render every real planet in the system — even one whose PlanetType string didn't come
                    // through (fall back to the home type for colour); only skip the body you launched from.
                    if (b.Id == current.Id || b.Kind != "Planet")
                    {
                        continue;
                    }

                    string type = string.IsNullOrEmpty(b.PlanetType) ? homeType : b.PlanetType;
                    var pos = new Vector3((b.SystemX - current.SystemX) * SystemViewScale, 0f, (b.SystemZ - current.SystemZ) * SystemViewScale);
                    int circ = BodySize.CircumferenceOf(b); // #2691: the server's size of the body
                    float diameter = OrbitDiameterFor(circ);
                    Plan(b.Id, b.Name, b.Kind, pos, diameter, type, circ, b.RingSeed);
                    planetIndexById[b.Id] = planets.Count;
                    planets.Add((b.SystemX, b.SystemZ, pos, diameter * 0.5f));
                }

                // Pass 2: moons, each placed just outside its parent planet's surface. The star map's ParentId
                // is authoritative (#548) — the old nearest-planet scan attached a moon to whichever planet
                // happened to render closest, which breaks as soon as sizes/orbits vary. Because ALL moons
                // orbit below any planet's rendered radius, each parent hands out ascending orbit SLOTS: the
                // first moon sits at the minimum clear orbit, every further one a clear gap above the last —
                // a Lone Giant's 8 moons read as a family of rings instead of one crowded shell.
                var moonLadder = new Dictionary<int, (float Orbit, float Radius)>(); // parent → outermost slot so far
                foreach (var b in system.Bodies)
                {
                    if (b.Id == current.Id || b.Kind != "Moon")
                    {
                        continue;
                    }

                    string type = string.IsNullOrEmpty(b.PlanetType) ? homeType : b.PlanetType;
                    int parentIndex;
                    if (string.IsNullOrEmpty(b.ParentId) || !planetIndexById.TryGetValue(b.ParentId, out parentIndex))
                    {
                        // Fallback for maps without ParentId (old servers): the nearest planet, as before.
                        parentIndex = 0;
                        float bestSq = float.MaxValue;
                        for (int p = 0; p < planets.Count; p++)
                        {
                            float dx = b.SystemX - planets[p].Sx, dz = b.SystemZ - planets[p].Sz;
                            float dsq = dx * dx + dz * dz;
                            if (dsq < bestSq) { bestSq = dsq; parentIndex = p; }
                        }
                    }

                    var parent = planets[parentIndex];

                    var rel = new Vector3((b.SystemX - parent.Sx) * SystemViewScale, 0f, (b.SystemZ - parent.Sz) * SystemViewScale);
                    int moonCirc = BodySize.CircumferenceOf(b);
                    float moonDia = OrbitDiameterFor(moonCirc);
                    // Outside the planet's surface, with room to spare — and above this parent's previous
                    // moon's ring, so siblings stack outward instead of sharing one radius (#548).
                    float minClear = SystemBodyLayout.MinOrbitFor(parent.Radius, moonDia * 0.5f);
                    if (moonLadder.TryGetValue(parentIndex, out var last))
                    {
                        minClear = Mathf.Max(minClear,
                            last.Orbit + last.Radius + moonDia * 0.5f + SystemBodyLayout.ClearGapFor(last.Radius, moonDia * 0.5f));
                    }

                    if (rel.magnitude < minClear)
                    {
                        Vector3 dir = rel.sqrMagnitude > 0.0001f ? rel.normalized : Vector3.right;
                        rel = dir * minClear;
                    }

                    moonLadder[parentIndex] = (rel.magnitude, moonDia * 0.5f);

                    Plan(b.Id, b.Name, b.Kind, parent.Render + rel, moonDia, type, moonCirc);
                    if (parentIndex == 0)
                    {
                        // A moon of the body you launched from: planned in HOME'S plane (far below the flight
                        // plane), so it is the one case where the relax pass can shove a body back into home.
                        homeMoons.Add(positions.Count - 1);
                    }
                }

                // Pass 3: large landable asteroids — scattered like planets; you fly up + press E to land on
                // them (ship or EVA → a small walkable asteroid world). The small mineable rocks are separate
                // space entities; these are the sized, landable bodies.
                foreach (var b in system.Bodies)
                {
                    if (b.Id == current.Id || b.Kind != "AsteroidField")
                    {
                        continue;
                    }

                    string type = string.IsNullOrEmpty(b.PlanetType) ? "asteroid" : b.PlanetType;
                    var pos = new Vector3((b.SystemX - current.SystemX) * SystemViewScale, 0f, (b.SystemZ - current.SystemZ) * SystemViewScale);
                    int circ = BodySize.CircumferenceOf(b, b.Id, b.Kind, type);
                    float diameter = OrbitDiameterFor(circ);
                    Plan(b.Id, b.Name, b.Kind, pos, diameter, type, circ);
                }

                // Separation pass: nudge any overlapping pair apart in the x-z plane until every body has
                // its clear gap (SystemBodyLayout — shared with the tests that assert the guarantee).
                // Home is passed as the fixed body: it never moves (the scene is centred on it) and its
                // own moons are the ones laid out in its plane, so they are the ones it has to push out.
                var flatX = new float[positions.Count];
                var flatZ = new float[positions.Count];
                for (int k = 0; k < positions.Count; k++)
                {
                    flatX[k] = positions[k].x;
                    flatZ[k] = positions[k].z;
                }

                SystemBodyLayout.SeparateXZ(
                    flatX, flatZ, radii.ToArray(),
                    homePos.x, homePos.z, homeDiameter * 0.5f, homeMoons.ToArray());

                for (int k = 0; k < positions.Count; k++)
                {
                    positions[k] = new Vector3(flatX[k], positions[k].y, flatZ[k]);
                }

                // Spawn the separated bodies + register them as landable / keep-out.
                for (int k = 0; k < positions.Count; k++)
                {
                    var bodyGo = SpawnBody("SystemBody_" + ids[k], ids[k], kinds[k], locKeys[k], positions[k], radii[k] * 2f, bodyTypes[k], circs[k], rings[k]);
                    if (kinds[k] == "AsteroidField")
                    {
                        _asteroidBodies.Add((bodyGo.transform, radii[k] * 2f)); // #1663: held findable from afar
                    }

                    var shown = Game?.FindMapBody(ids[k]); // #2952: label with the player's own name; locKeys keep the generated one
                    _landables.Add((ids[k], shown.IsRenamed() ? shown.Shown() : names[k], positions[k], radii[k]));
                    _keepOut.Add((positions[k], radii[k] + KeepOutMargin)); // can't fly into it — slide + press E to land
                    maxDist = Mathf.Max(maxDist, positions[k].magnitude);
                }
            }

            _bounds = Mathf.Max(Bounds, maxDist + 140f); // keep the whole system reachable
        }

        /// <summary>#1663: holds every landable asteroid body at a minimum apparent size. A rock's true orbit
        /// diameter (11–15 units) projects to 8–20 px at belt distances, so the belt you were told to fly to
        /// was invisible until you were nearly in it. The sphere is scaled up only as far as it takes to fill
        /// <see cref="MinAsteroidScreenPx"/> on screen (perspective: world units per pixel = 2·d·tan(fov/2)/
        /// screen height); once you are close enough that its true size covers that, it is exactly its true
        /// size again — near-range looks and keep-out spheres are untouched. A handful of bodies, one
        /// distance each: negligible per frame.</summary>
        private void KeepFarAsteroidsVisible()
        {
            if (_asteroidBodies.Count == 0 || Camera == null)
            {
                return;
            }

            float tanHalf = Mathf.Tan(Camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float screenH = Mathf.Max(1f, Screen.height);
            Vector3 cam = Camera.transform.localPosition; // the flight camera lives in the scene-root frame, like the bodies
            for (int i = 0; i < _asteroidBodies.Count; i++)
            {
                var (body, diameter) = _asteroidBodies[i];
                if (body == null)
                {
                    continue;
                }

                float dist = (body.localPosition - cam).magnitude;
                float minWorld = MinAsteroidScreenPx * 2f * dist * tanHalf / screenH;
                float want = Mathf.Max(diameter, minWorld);
                if (Mathf.Abs(body.localScale.x - want) > 0.01f)
                {
                    body.localScale = Vector3.one * want;
                }
            }
        }

        /// <summary>The orbit-view diameter for a body, derived from its real walkable circumference — so a
        /// tiny asteroid reads small, a moon medium and a big planet large, matching how long it'd take to walk
        /// around. Same body → same size, on the surface (server) and in orbit (here). <paramref name="circumference"/>
        /// is the body's walkable circumference as the server sized it (<see cref="BodySize"/>,
        /// #2691 — incl. its archetype size identity, #549).</summary>
        private static float OrbitDiameterFor(int circumference)
            => 8f + circumference / 220f; // ~13 (asteroid) .. ~23 (moon) .. ~46-62 (planet; a lone giant reaches ~81)

        /// <summary>Spawns one real celestial body: a sphere textured with its REAL generated world map (seas, the
        /// biomes' ground, snow and ice, this world's water and vegetation colours — what you see from orbit IS the world
        /// you land on), plus a cloud shell and an atmosphere haze rim from the planet's DATA (#2170). The map is baked
        /// off the main thread (#2172): the sphere starts in the flat data-driven colour and swaps the map in when it is
        /// ready. The cloud shell follows the body's live weather (#2175) and the sphere turns to its time of day (#2177)
        /// — both in <see cref="UpdateOrbitBodies"/>. <paramref name="circumference"/> is the body's true walkable
        /// circumference as the server sized it (#2691) — the map bake covers exactly that world;
        /// <paramref name="locationName"/> ("System · Body") seeds the per-world flora hue.</summary>
        private GameObject SpawnBody(string name, string bodyId, string kind, string locationName, Vector3 pos, float diameter, string planetType, int circumference, int ringSeed = 0)
        {
            // B37 rest: planets + cloud shells in the orbit view are lit by THIS system's star, so under a
            // red sun the whole system reads warm (a light wash — the biome tint stays recognisable).
            float sm = Mathf.Max(_sunColor.r, Mathf.Max(_sunColor.g, _sunColor.b));
            Color sunHue = sm > 0.001f ? new Color(_sunColor.r / sm, _sunColor.g / sm, _sunColor.b / sm) : Color.white;

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = name;
            StripCollider(sphere);
            sphere.transform.SetParent(_root.transform, false);
            sphere.transform.localPosition = pos;
            if (!string.IsNullOrEmpty(bodyId))
            {
                _bodySpheres[bodyId] = sphere.transform; // #2153: the planet scanner sweeps this sphere
            }
            sphere.transform.localScale = Vector3.one * diameter;

            // World-space direction from this body to the system star → its sun-lit phase. The star sits at
            // _starLocal in the scene; without a star map fall back to the stylised fixed sun direction.
            Vector3 sunLocal = _hasStar ? _starLocal - pos : SunDir;
            Vector3 sunDir = _root.transform.TransformDirection(sunLocal);

            var planet = Game?.Content?.GetPlanet(planetType ?? string.Empty);
            string key = string.IsNullOrEmpty(bodyId) ? (locationName ?? "home") : bodyId;
            var ob = new OrbitBody
            {
                BodyId = bodyId ?? string.Empty,
                Key = key,
                Planet = planet,
                Sphere = sphere.transform,
                Pos = pos,
                Diameter = diameter,
                SunLocal = sunLocal,
            };

            // The flat data-driven look first (ground + this world's flora + water/lava) — the real map swaps in once
            // its bake finishes off the main thread, so building a whole system no longer freezes the frame.
            var look = PlanetLookFor(planetType, locationName);
            Color bodyTint = Color.Lerp(look.tint, look.tint * sunHue, 0.35f);
            var flatTex = LoadTex(look.tex);
            var mat = LitPhase(bodyTint, sunDir, flatTex, new Vector2(3f, 2f));
            sphere.GetComponent<Renderer>().sharedMaterial = mat;
            _orbitOwned.Add(mat);
            if (flatTex != null)
            {
                _orbitOwned.Add(flatTex); // replaced by the baked map later — destroyed with the scene, not leaked
            }
            if (planet != null && Game != null)
            {
                // Airless moons carry craters, as the server generates them (#2170).
                bool cratered = kind == "Moon" && planet.IsAirless;
                var request = WorldMinimap.MakeRequest(Game.Content, Game.Atlas, Game.WorldSeed, locationName, planetType, circumference, 96, 48,
                    key, Game.TerrainContinents, Game.TerrainGeneration, cratered, Game.TerrainLavaCoreVolcanoes);
                Color washTint = Color.Lerp(Color.white, sunHue, 0.35f);
                WorldMinimap.Request(request, (tex, data) =>
                {
                    if (mat == null || ob.Sphere == null)
                    {
                        return; // the scene was torn down while the map baked
                    }

                    mat.mainTexture = tex;
                    mat.mainTextureScale = Vector2.one;
                    mat.color = ShaderColor.Srgb(washTint);
                    ob.Map = data;
                    ob.SeenVersion = -1; // project the weather onto the fresh map
                }, priority: name == "HomePlanet" ? 10 : 1);
            }

            // Atmosphere from DATA (#2170): no clouds on airless bodies, the type's cloud cover otherwise, the world's
            // own cloud and sky tints — the hard-coded table left 33 of 51 types cloudless from orbit.
            ob.Atmosphere = OrbitLook.For(planet, Game?.WorldSeed ?? 0L, key, Game?.TerrainGeneration ?? 0); // #2692: the save's generation
            if (ob.Atmosphere.HasClouds)
            {
                AddCloudShell(ob, sunHue, sunDir, CloudShellSeed(key));
            }

            // Atmosphere limb (#2400): a thin shell whose glow sits on the rim and the sun-facing side, warm along the
            // terminator, dark on the night side (PlanetLimb.shader) — a breathable atmosphere reads as a denser glow
            // than a toxic one; airless bodies stay crisp bare rock.
            if (ob.Atmosphere.HasAir)
            {
                float atm = ob.Atmosphere.Breathable ? 1f : 0.7f;
                var haze = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                haze.name = "Atmosphere";
                StripCollider(haze);
                haze.transform.SetParent(sphere.transform, false);
                haze.transform.localScale = Vector3.one * 1.06f;
                var hShader = Shader.Find("BlocksBeyondTheStars/PlanetLimb") ?? Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
                var sky = PlanetWeatherVisuals.Rgb(ob.Atmosphere.SkyRgb);
                var hCol = Color.Lerp(Color.Lerp(sky, Color.white, 0.25f), sunHue, 0.25f);
                hCol.a = 0.35f + 0.35f * atm;
                var hMat = new Material(hShader) { mainTexture = Texture2D.whiteTexture, renderQueue = 2999 };
                hMat.SetColor("_Color", ShaderColor.Srgb(hCol));
                hMat.SetVector("_SunDir", sunDir);
                hMat.SetColor("_Warm", ShaderColor.Srgb(Color.Lerp(new Color(1f, 0.6f, 0.3f), sunHue, 0.4f)));
                var hMr = haze.GetComponent<Renderer>();
                hMr.sharedMaterial = hMat;
                hMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                hMr.receiveShadows = false;
                ob.HazeMat = hMat;
                ob.HazeColor = hCol;
                _orbitOwned.Add(hMat);
            }

            // Planetary rings (#596): a seeded, tilted band disc — queue 3001 so it draws after the
            // haze (2999) and cloud shell (3000); the opaque planet sphere occludes the far half.
            if (ringSeed != 0)
            {
                var ring = PlanetRings.Attach(sphere.transform, ringSeed, sunHue, 0.55f, 3001, out _);
                ob.Ring = ring != null ? ring.transform : null;
                ob.RingBase = ring != null ? ring.transform.localRotation : Quaternion.identity;
            }

            _orbitBodies.Add(ob);
            return sphere;
        }

        private GameObject BuildShip(Transform parent)
        {
            // item 20 S1: if the server sent the ship's voxel design, render it as a real 1:1 voxel mesh of the
            // player's editor design instead of the hand-built cube silhouette below.
            var design = Game?.ShipDesign;
            _builtDesign = design;
            if (design != null && design.Block != null && design.Block.Length > 0)
            {
                return BuildVoxelShip(parent, design);
            }

            // Cube fallback (no design yet): no voxel grid to collide/build against.
            _shipCells = null;
            _shipVox = null;
            _shipStructureId = null;

            var ship = new GameObject("Ship");
            ship.transform.SetParent(parent, false);

            // Same block textures as the ship you walk on a planet: iron_wall hull, glass canopy, carbon
            // engine nozzles (the station model already uses these), so it reads as a real hull, not a flat cube.
            // The hull tint is the player's chosen hull colour (item 32; default = the old steel tint).
            var hull = LitTex("iron_wall", Rgb(Game.HullRgb));
            _hullMat = hull;
            _appliedHullRgb = Game.HullRgb;
            var glass = LitTex("glass", new Color(0.7f, 0.9f, 1f));
            var engine = LitTex("carbon", new Color(0.78f, 0.78f, 0.82f));

            Cube("Body", ship.transform, new Vector3(0f, 0f, 0f), new Vector3(1.6f, 0.9f, 3.4f), hull);
            Cube("WingL", ship.transform, new Vector3(-1.3f, 0f, -0.3f), new Vector3(1.2f, 0.2f, 1.4f), hull);
            Cube("WingR", ship.transform, new Vector3(1.3f, 0f, -0.3f), new Vector3(1.2f, 0.2f, 1.4f), hull);
            Cube("Cockpit", ship.transform, new Vector3(0f, 0.5f, 1.2f), new Vector3(0.9f, 0.6f, 1.0f), glass);
            Cube("Engine", ship.transform, new Vector3(0f, 0f, -1.9f), new Vector3(1.0f, 0.7f, 0.5f), engine);

            // Navigation lights at the wingtips (port red / starboard green), like the landed ship.
            Cube("NavL", ship.transform, new Vector3(-1.85f, 0f, -0.3f), new Vector3(0.22f, 0.22f, 0.22f), Unlit(new Color(1f, 0.25f, 0.2f)));
            Cube("NavR", ship.transform, new Vector3(1.85f, 0f, -0.3f), new Vector3(0.22f, 0.22f, 0.22f), Unlit(new Color(0.3f, 1f, 0.3f)));

            // Entry hatch on the tail (where the voxel hatch is) — a glowing cyan frame so you can find where
            // to board back in. It pulses brightly while you're on an EVA (see the hatch pulse in LateUpdate).
            _hatchMat = Unlit(new Color(0.2f, 0.85f, 1f));
            Cube("Hatch", ship.transform, new Vector3(0f, -0.32f, -1.6f), new Vector3(0.95f, 0.5f, 0.18f), _hatchMat);

            // Glowing thruster exhaust (stretches with throttle in Update).
            var ex = Cube("Exhaust", ship.transform, new Vector3(0f, 0f, -2.4f), new Vector3(0.6f, 0.6f, 1f), Unlit(new Color(0.6f, 0.85f, 1f)));
            _exhaust = ex.transform;
            _thruster = BuildThruster(ship.transform, ex.transform.localPosition);
            return ship;
        }

        /// <summary>Builds the player's ship as a 1:1 voxel mesh from the server's design (item 20 S1): meshes the
        /// sparse block grid with the same <see cref="ChunkMesher"/> + block atlas the planet world uses, centred on
        /// the ship pivot so it flies + rotates like the old cube model. Keeps the glowing tail hatch marker +
        /// thruster exhaust so the EVA/throttle FX still work. The hull colour (item 32) is painted into the mesh's
        /// tint stream (see <see cref="ShipMeshBuilder.HullPaint"/>) — _hullMat stays null (its use sites are
        /// null-guarded); a live colour change re-meshes the ship voxels instead.</summary>
        private GameObject BuildVoxelShip(Transform parent, BlocksBeyondTheStars.Networking.Messages.SpaceShipDesign d)
        {
            var ship = new GameObject("Ship");
            ship.transform.SetParent(parent, false);

            // Index the cells (kept client-side for EVA collision + build/mine aim, S2) and find the bounds.
            var cells = new Dictionary<Vector3i, BlockId>(d.Block.Length);
            _shipMods = new Dictionary<Vector3i, (int, int)>();
            _shipShapes = new Dictionary<Vector3i, int>();
            bool hasTint = d.Tint != null && d.Tint.Length == d.Block.Length;
            bool hasGlow = d.Glow != null && d.Glow.Length == d.Block.Length;
            bool hasShape = d.Shape != null && d.Shape.Length == d.Block.Length;
            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
            for (int i = 0; i < d.Block.Length; i++)
            {
                int bx = d.X[i], by = d.Y[i], bz = d.Z[i];
                var key = new Vector3i(bx, by, bz);
                cells[key] = new BlockId(d.Block[i]);
                int tint = hasTint ? d.Tint[i] : 0, glow = hasGlow ? d.Glow[i] : 0;
                if (tint != 0 || glow != 0) _shipMods[key] = (tint, glow);
                if (hasShape && d.Shape[i] != 0) _shipShapes[key] = d.Shape[i];
                if (bx < minX) minX = bx; if (by < minY) minY = by; if (bz < minZ) minZ = bz;
                if (bx > maxX) maxX = bx; if (by > maxY) maxY = by; if (bz > maxZ) maxZ = bz;
            }

            // Centre the design on the ship pivot (so it rotates about its middle, like the old model). Front = +Z.
            _shipCells = cells;
            _shipCentre = new Vector3((minX + maxX + 1) * 0.5f, (minY + maxY + 1) * 0.5f, (minZ + maxZ + 1) * 0.5f);
            _shipHalf = new Vector3((maxX - minX + 1) * 0.5f, (maxY - minY + 1) * 0.5f, (maxZ - minZ + 1) * 0.5f);
            _shipStructureId = d.Id;

            // A child container holds the voxel chunk meshes so an EVA block edit (S2) can rebuild just the voxels.
            _shipVox = new GameObject("Vox").transform;
            _shipVox.SetParent(ship.transform, false);
            RebuildShipVoxels();

            // #2119 / #2607: the doors the owner built (their cells are air, the door fills its doorway three high) — drawn
            // as doors in flight, aimed at on a spacewalk.
            _shipDoors = SpaceDoors.DoorsOf(d);
            _shipDoorCells = SpaceDoors.DoorwayCells(_shipDoors);

            // #2587: where the rear hatch is — the server's hatch cells, or (an older server) the robust guess over the
            // design box's cells. The owner's own doors are wall to the guess: they show their own field.
            var tuples = new List<(int X, int Y, int Z)>(cells.Count);
            foreach (var c in cells.Keys)
            {
                tuples.Add((c.X, c.Y, c.Z));
            }

            var ownerDoors = new List<(int X, int Y, int Z)>(_shipDoors.Count);
            foreach (var door in _shipDoors)
            {
                ownerDoors.Add((door.Cell.X, door.Cell.Y, door.Cell.Z));
            }

            var hatch = ShipHatch.Find(tuples, d.Width, d.Height, d.Length, SpaceDoors.HatchOf(d), ownerDoors);

            // Tail FX: the glowing marker that pulses on an EVA ("board here"). #2587: on the stern wall under the hatch;
            // the hull's global rear only when no wall was found — a leaf behind the stern used to push it into space.
            Vector3 marker = hatch.HasMarker
                ? new Vector3(hatch.MarkerX - _shipCentre.x, hatch.MarkerY - _shipCentre.y, hatch.MarkerZ - _shipCentre.z + 0.1f)
                : new Vector3(0f, minY - _shipCentre.y + 0.5f, minZ - _shipCentre.z + 0.1f);
            _hatchMat = Unlit(new Color(0.2f, 0.85f, 1f));
            Cube("Hatch", ship.transform, marker, new Vector3(1.0f, 0.6f, 0.25f), _hatchMat);
            BuildNozzlePlumes(ship.transform, cells); // #2157/#2162: one plume per real engine, none on the door

            // Cap the rear hatch opening with a powered energy door so the ship isn't a hole at the stern (you
            // used to see straight into the hull). The field drops (hides) on an EVA so you can still board.
            AddRearHatchDoor(ship, hatch, _shipCentre);
            AddPlacedDoors(ship.transform, _shipDoors, _shipCentre); // #2119: the doors the owner built are doors in flight too
            return ship;
        }

        /// <summary>Caps the hatch openings <paramref name="hatch"/> names (#2587, <see cref="ShipHatch"/>) with a powered
        /// energy door each — a translucent blue field in an emissive rim — under one <see cref="_hatchDoor"/> parented to
        /// the ship (hidden on an EVA). No-op when there is nothing to cap (a sealed stern).</summary>
        private void AddRearHatchDoor(GameObject ship, ShipHatch.Plan hatch, Vector3 centre)
        {
            _hatchDoor = null;
            if (hatch.Panels.Count == 0)
            {
                return;
            }

            var door = new GameObject("HatchDoor");
            door.transform.SetParent(ship.transform, false);
            _hatchDoor = door;

            // A powered ENERGY door (same look as the on-foot energy doors, item 35): a translucent blue field
            // fills the opening, framed by a thin emissive cyan rim, so the stern clearly reads as a closed,
            // powered airlock instead of the bare hull hole you used to see straight through. The rim + field
            // slightly over-fill the opening so there's no visible seam gap at the half flight scale.
            var rim = Unlit(new Color(0.2f, 0.85f, 1f));
            var fieldMat = EnergyField(new Color(0.35f, 0.80f, 1f, 0.6f));
            const float t = 0.16f; // rim bar thickness
            foreach (var p in hatch.Panels)
            {
                // The panel stands in its wall plane (centred like the mesh): across Z in the stern, across X in a
                // side wall (a self-built ship's side door) — the same panel turned a quarter about Y.
                float w = p.FacesZ ? p.MaxX - p.MinX + 1 : p.MaxZ - p.MinZ + 1;
                float h = p.MaxY - p.MinY + 1;
                var panel = new GameObject("HatchPanel");
                panel.transform.SetParent(door.transform, false);
                panel.transform.localPosition = new Vector3(
                    (p.MinX + p.MaxX + 1) * 0.5f - centre.x,
                    (p.MinY + p.MaxY + 1) * 0.5f - centre.y,
                    (p.MinZ + p.MaxZ + 1) * 0.5f - centre.z);
                panel.transform.localRotation = p.FacesZ ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);

                Cube("RimTop", panel.transform, new Vector3(0f, h * 0.5f - t * 0.5f, 0f), new Vector3(w, t, 0.26f), rim);
                Cube("RimBottom", panel.transform, new Vector3(0f, -h * 0.5f + t * 0.5f, 0f), new Vector3(w, t, 0.26f), rim);
                Cube("RimLeft", panel.transform, new Vector3(-w * 0.5f + t * 0.5f, 0f, 0f), new Vector3(t, h, 0.26f), rim);
                Cube("RimRight", panel.transform, new Vector3(w * 0.5f - t * 0.5f, 0f, 0f), new Vector3(t, h, 0.26f), rim);
                Cube("EnergyField", panel.transform, Vector3.zero, new Vector3(w - t, h - t, 0.12f), fieldMat);
            }
        }

        /// <summary>(Re)builds the ship's voxel chunk meshes + colliders from <see cref="_shipCells"/> (item 20
        /// S1/S2). Called on first build and after each EVA block edit. The pivot centre stays fixed so the ship
        /// never jumps when you add/remove a block.</summary>
        private void RebuildShipVoxels()
        {
            if (_shipVox != null)
            {
                BuildVoxChunks(_shipVox, _shipCells, _shipCentre,
                    ShipMeshBuilder.HullPaint(Game.Content, Rgb(Game.HullRgb)), _shipMods, _shipShapes); // hull paint (item 32) + authored dye/shape
                _appliedHullRgb = Game.HullRgb;
            }
        }

        /// <summary>Rebuilds the ship GameObject from the latest design while preserving its current pose (item 20
        /// S1 — the design message lands a frame or two after entering space).</summary>
        private void RebuildShipModel()
        {
            var pos = _ship != null ? _ship.transform.localPosition : Vector3.zero;
            var rot = _ship != null ? _ship.transform.localRotation : Quaternion.identity;
            if (_ship != null)
            {
                Destroy(_ship);
            }

            _exhaust = null;
            _thruster = null;
            _hatchMat = null;
            _hullMat = null;
            _appliedHullRgb = -1;
            _shipVox = null;      // destroyed with the old ship; BuildShip makes a fresh one
            _extraThrusters.Clear();
            _ship = BuildShip(_root.transform);
            _ship.transform.localPosition = pos;
            _ship.transform.localRotation = rot;
            AttachShipFx();
        }

        /// <summary>Builds the system's star far off in a fixed direction as stacked additive billboards: a
        /// coloured corona (the star tint) with a white-hot core on top, so even a deep-red or blue star
        /// reads as a real bright sun disc you can fly toward — not a vague coloured wash.</summary>
        private void BuildSun()
        {
            _sunColor = Game?.Environment != null ? Rgb(Game.Environment.SunColor) : new Color(1f, 0.96f, 0.88f);

            var go = new GameObject("Sun");
            go.transform.SetParent(_root.transform, false);
            _sun = go.transform;

            _sunMat = MakeSunLayer(_sun, _sunColor, 320f);                          // outer corona (star colour)
            MakeSunLayer(_sun, Color.Lerp(_sunColor, Color.white, 0.85f), 150f);    // hot near-white core
            MakeSunLayer(_sun, Color.white, 64f);                                   // blazing white centre
        }

        private Material MakeSunLayer(Transform parent, Color color, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "SunLayer";
            StripCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * size;

            var shader = Shader.Find("BlocksBeyondTheStars/SunGlow") ?? Shader.Find("Unlit/Color");
            var mat = new Material(shader) { mainTexture = GenerateGlowTexture() };
            mat.SetColor("_Color", ShaderColor.Srgb(color));

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mat;
        }

        /// <summary>Keeps the sun at a fixed direction + far distance from the camera (so it stays at the
        /// "horizon" of space) and faces it square-on as a billboard (its layers keep their own sizes).</summary>
        private void BillboardSun()
        {
            if (_sun == null || Camera == null)
            {
                return;
            }

            // Aim the visible sun at the true system star (so its direction agrees with the bodies' sun-lit
            // phases), keeping it far off at the "horizon" of space. Fall back to the stylised fixed direction.
            Vector3 dir = _hasStar ? (_starLocal - Camera.transform.localPosition) : SunDir;
            if (dir.sqrMagnitude < 1e-4f)
            {
                dir = SunDir;
            }

            _sun.localPosition = Camera.transform.localPosition + dir.normalized * 1500f;
            _sun.rotation = Quaternion.LookRotation(_sun.position - Camera.transform.position, Vector3.up);
            _sun.localScale = Vector3.one;
        }

        /// <summary>Data-driven orbital look: sphere texture = the type's REAL surface block, tinted with a
        /// partial cast of the mixed orbit colour (ground + this world's own flora hue + water/lava) — so a
        /// mud flat reads brown and a lush world reads in ITS vegetation colour. Falls back to the legacy
        /// palette when the type is unknown (the old hardcoded map mis-coloured every newer planet type).</summary>
        private (Color tint, string tex) PlanetLookFor(string key, string locationName)
        {
            var legacy = PlanetLook(key);
            var planet = Game?.Content?.GetPlanet(key ?? string.Empty);
            if (planet == null)
            {
                return legacy;
            }

            if (planet.IsGasWorld)
            {
                return (new Color(0.96f, 0.9f, 0.84f), "gas"); // #2112: the banded cloud tops are the whole face of a gas giant
            }

            Color ground = PlanetOrbitLook.GroundColor(
                Game.Content, Game.Atlas, Game.WorldSeed, locationName, key, legacy.tint);
            // Partial cast only: the surface texture already carries the ground colour — full
            // multiplication would double-saturate and darken the sphere.
            return (Color.Lerp(Color.white, ground, 0.55f), planet.SurfaceBlock);
        }

        /// <summary>Adds a semi-transparent cloud shell over a planet sphere (#2170/#2175). It starts at the planet's base
        /// cover and takes the live weather once the map and the system's weather are known; its texture's colour channel
        /// carries the weather tint (storm-dark, snow-white, acid, ash, sand, spores), the material only the star's light
        /// and the night-side shade, so the day/night terminator matches the planet's own.</summary>
        private void AddCloudShell(OrbitBody ob, Color sunHue, Vector3 sunDir, int seed)
        {
            var shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shell.name = "CloudShell";
            StripCollider(shell);
            shell.transform.SetParent(ob.Sphere, false);
            shell.transform.localPosition = Vector3.zero;
            shell.transform.localScale = Vector3.one * 1.035f; // just above the surface

            var tex = PlanetWeatherVisuals.NewShellTexture("CloudShell_" + ob.Key);
            var shader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
            var mat = new Material(shader) { mainTexture = tex, renderQueue = 3000 };
            var lit = Color.Lerp(Color.white, sunHue, 0.35f);
            lit.a = 0.9f;
            mat.SetColor(Shader.PropertyToID("_Color"), ShaderColor.Srgb(lit));
            var shade = new Color(0.22f, 0.23f, 0.27f, lit.a);
            mat.SetColor(Shader.PropertyToID("_ShadeColor"), ShaderColor.Srgb(shade));
            mat.SetVector(Shader.PropertyToID("_CloudSunDir"), sunDir.sqrMagnitude > 1e-4f ? sunDir.normalized : Vector3.up);
            mat.SetFloat(Shader.PropertyToID("_SunShade"), 1f);
            mat.SetFloat(Shader.PropertyToID("_Bulge"), 0f);

            var mr = shell.GetComponent<Renderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            ob.Shell = shell.transform;
            ob.ShellMat = mat;
            ob.ShellTex = tex;
            ob.ShellBuffer = new Color32[PlanetWeatherVisuals.ShellW * PlanetWeatherVisuals.ShellH];
            ob.Noise = PlanetWeatherVisuals.Noise(seed);
            ob.ShellPhaseDeg = Mathf.Repeat(seed * 0.37f, 360f); // every planet's pattern starts elsewhere
            _orbitOwned.Add(mat);
            _orbitOwned.Add(tex);
            PlanetWeatherVisuals.ComposeShell(tex, ob.ShellBuffer, ob.Noise, null, null, ob.Atmosphere.CloudDensity,
                ob.Atmosphere.CloudRgb, ob.Atmosphere.CloudRgb, ob.Atmosphere.CloudRgb, 0f);
        }

        /// <summary>Per-frame upkeep of the scene's bodies (#2175/#2177). Cheap per frame (a rotation each); the weather
        /// projection and the shell texture are recomposed only when a weather snapshot arrives, and between snapshots
        /// every few seconds (1.5 s for a planet you are near, 6 s for the rest) as fronts drift and the shell turns.
        /// The nearest planet's storms flicker with lightning; a meteor shower streaks into its atmosphere.</summary>
        private void UpdateOrbitBodies()
        {
            if (Game == null || _orbitBodies.Count == 0 || Camera == null)
            {
                return;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            int version = Game.SystemWeather.Version;
            Vector3 cam = Camera.transform.localPosition; // the flight camera lives in the scene-root frame, like the bodies
            OrbitBody nearest = null;
            float nearestGap = float.MaxValue;
            foreach (var ob in _orbitBodies)
            {
                if (ob.Sphere == null)
                {
                    continue;
                }

                float radius = ob.Sphere.localScale.x * 0.5f;
                float gap = (ob.Pos - cam).magnitude - radius;
                if (gap < nearestGap)
                {
                    nearestGap = gap;
                    nearest = ob;
                }

                bool hasWeather = PlanetWeatherVisuals.TryBodyWeather(Game, ob.BodyId, out var bw);

                // #2177: the lit side is the day side — the noon meridian faces the star. A running clock turns the
                // sphere continuously; a body nobody stands on waits at its arrival hour.
                if (hasWeather && (ob.SpunVersion != version || bw.ClockRunning))
                {
                    PlanetWeatherVisuals.Spin(ob.Sphere, PlanetWeatherVisuals.TimeOfDay(Game, bw, ob.Planet), ob.SunLocal);
                    ob.SpunVersion = version;
                    if (ob.Ring != null)
                    {
                        ob.Ring.localRotation = Quaternion.Inverse(ob.Sphere.localRotation) * ob.RingBase; // rings don't spin with the day
                    }
                }

                // The cloud pattern drifts with the wind, slowly, relative to the ground — the weather regions are
                // recomposed against that drift, so a storm stays over the land it sits on.
                if (ob.Shell != null)
                {
                    float wind = hasWeather ? bw.WindSpeed : 0.2f;
                    ob.ShellPhaseDeg = Mathf.Repeat(ob.ShellPhaseDeg + Time.deltaTime * (0.2f + 0.8f * wind), 360f);
                    ob.Shell.localRotation = Quaternion.Euler(0f, ob.ShellPhaseDeg, 0f);
                }

                if (!hasWeather || ob.Map == null || (ob.SeenVersion == version && now < ob.NextCompose))
                {
                    continue;
                }

                bool near = gap < radius * 6f;
                ob.Pixels ??= new WeatherMapPixels();
                PlanetWeatherVisuals.Project(Game, bw, ob.Map, ob.Planet, ob.Atmosphere.CloudDensity, ob.Pixels);
                if (ob.Shell != null)
                {
                    PlanetWeatherVisuals.ComposeShell(ob.ShellTex, ob.ShellBuffer, ob.Noise, ob.Map, ob.Pixels, ob.Atmosphere.CloudDensity,
                        ob.Atmosphere.CloudRgb, ob.Map.GroundRgb, ob.Map.FloraRgb, ob.ShellPhaseDeg / 360f);
                }

                PlanetWeatherVisuals.CollectStormPixels(ob.Pixels, ob.StormPixels);
                ob.State = bw.State ?? "clear";
                ob.SeenVersion = version;
                ob.NextCompose = now + (near ? 1.5 : 6.0);
                UpdateHaze(ob);
            }

            if (nearest != null && nearest.HazeMat != null && nearest.State == "ion_storm")
            {
                // An ion storm makes the nearest world's rim shimmer cyan ↔ violet.
                float k = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.2f);
                var c = Color.Lerp(new Color(0.45f, 0.95f, 1f), new Color(0.75f, 0.5f, 1f), k);
                c.a = nearest.HazeColor.a + 0.08f;
                nearest.HazeMat.SetColor("_Color", ShaderColor.Srgb(c));
                nearest.HazeState = string.Empty; // re-applied by UpdateHaze once the storm ends
            }

            if (nearest != null && nearestGap < nearest.Sphere.localScale.x * 4f)
            {
                StormFx(nearest);
            }
        }

        /// <summary>The haze rim under the body's world-level weather: fog thickens it, a heatwave warms it.</summary>
        private static void UpdateHaze(OrbitBody ob)
        {
            if (ob.HazeMat == null || ob.HazeState == ob.State)
            {
                return;
            }

            ob.HazeState = ob.State;
            var c = ob.HazeColor;
            switch (ob.State)
            {
                case "fog":
                case "ground_fog":
                    c = Color.Lerp(c, new Color(0.92f, 0.94f, 0.97f, c.a), 0.5f);
                    c.a += 0.07f;
                    break;
                case "heatwave":
                    c = Color.Lerp(c, new Color(1f, 0.72f, 0.42f, c.a), 0.45f);
                    break;
            }

            ob.HazeMat.SetColor("_Color", ShaderColor.Srgb(c));
        }

        /// <summary>Lightning in the storm regions of the planet you are near (a few pooled glow flashes — fewer and
        /// softer with "reduce flashes"), and meteor streaks during a meteor shower.</summary>
        private void StormFx(OrbitBody ob)
        {
            float radius = ob.Sphere.lossyScale.x * 0.5f;
            if (ob.StormPixels.Count > 0 && ob.Map != null && Time.time >= _nextLightning)
            {
                int i = ob.StormPixels[Random.Range(0, ob.StormPixels.Count)];
                int px = i % ob.Map.Width, py = i / ob.Map.Width;
                var local = PlanetWeatherVisuals.LocalPoint((px + Random.value) / ob.Map.Width, (py + Random.value) / ob.Map.Height) * 1.05f;
                var at = ob.Sphere.TransformPoint(local);
                FxKit.Flash(at, new Color(0.82f, 0.88f, 1f) * FxKit.FlashScale, radius * 0.09f, 0.12f);
                _nextLightning = Time.time + Random.Range(0.35f, 1.6f) * (FxKit.ReduceFlashes ? 3f : 1f);
            }

            if (ob.State == "meteor_shower" && Time.time >= _nextMeteor)
            {
                Vector3 center = ob.Sphere.position;
                Vector3 from = center + Random.onUnitSphere * radius * 1.7f;
                Vector3 to = center + (from - center).normalized * radius * 1.02f + Random.insideUnitSphere * radius * 0.2f;
                const float life = 0.6f;
                FxKit.Emit(FxKit.Kind.Sparks, from, (to - from) / life, radius * 0.03f, life, new Color(1f, 0.8f, 0.55f));
                _nextMeteor = Time.time + Random.Range(0.4f, 1.2f);
            }
        }
    }
}
