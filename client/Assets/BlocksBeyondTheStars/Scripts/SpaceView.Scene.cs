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
            var homePos = new Vector3(SystemBodyLayout.HomeX, SystemBodyLayout.HomeY, SystemBodyLayout.HomeZ); // shared: the server sends landing traders here (#3824)
            // Every body has its own size (the server's walkable circumference, #2691 — incl. the archetype bias), so
            // worlds + moons look big or small in orbit instead of all identical — an approximate reflection of how varied
            // they are.
            // The home body IS the world we launched from: missing from the star map, it takes the size the server sent for it.
            int homeCirc = BodySize.CircumferenceOf(current, current?.Id ?? Game?.LocationName ?? "home", current?.Kind, current?.PlanetType,
                Game?.Circumference ?? 0);
            float homeDiameter = OrbitDiameterFor(homeCirc) * SystemBodyLayout.HomeDiameterFactor;
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
                var moonLadder = new Dictionary<int, (float Orbit, float Radius)>(); // parent → outermost slot so far (pass 2)
                // #3919: launched from a moon, the planet it orbits would hang at the star map's moon orbit — 21.6 units
                // from the launch column for a first moon, inside the planet's keep-out shell, so the climb ended in it
                // and the keep-out threw the ship away. It keeps its true direction at a clear distance (the server
                // lays it out the same way for its traders), and the moon you launched from holds that first orbit
                // slot, so the planet's other moons ring above it.
                string homeParentId = current.Kind == "Moon" && !string.IsNullOrEmpty(current.ParentId) ? current.ParentId : null;
                float homeTrueRadius = OrbitDiameterFor(homeCirc) * 0.5f;
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
                    if (b.Id == homeParentId)
                    {
                        var (px, pz) = SystemBodyLayout.HomeParentPosition(pos.x, pos.z, diameter * 0.5f, homeTrueRadius);
                        pos = new Vector3(px, 0f, pz);
                        moonLadder[planets.Count] = (pos.magnitude, homeTrueRadius); // the launch moon's slot is taken
                    }

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
                    float minClear = moonLadder.TryGetValue(parentIndex, out var last)
                        ? SystemBodyLayout.NextMoonOrbit(parent.Radius, moonDia * 0.5f, last.Orbit, last.Radius)
                        : SystemBodyLayout.MinOrbitFor(parent.Radius, moonDia * 0.5f);

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
                // The launch column never gives way either (#3919): every other body is held clear of it.
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
            => SystemBodyLayout.OrbitDiameter(circumference); // ~13 (asteroid) .. ~23 (moon) .. ~46-62 (planet; a lone giant reaches ~81)

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

            // World-space direction from this body to the system star → its sun-lit phase. The star sits at
            // _starLocal in the scene; without a star map fall back to the stylised fixed sun direction.
            Vector3 sunLocal = _hasStar ? _starLocal - pos : SunDir;
            Vector3 sunDir = _root.transform.TransformDirection(sunLocal);

            string key = string.IsNullOrEmpty(bodyId) ? (locationName ?? "home") : bodyId;
            var ob = new OrbitBody
            {
                BodyId = bodyId ?? string.Empty,
                Key = key,
                Pos = pos,
                Diameter = diameter,
                SunLocal = sunLocal,
            };

            // #3705: the body itself — globe, flat look then the baked map, data clouds, limb, rings — comes from the shared
            // orbit renderer (the shell backdrop, the intro film and the story's space vista draw the same planets).
            var spec = new OrbitBodySpec
            {
                Content = Game?.Content,
                Atlas = Game?.Atlas,
                WorldSeed = Game?.WorldSeed ?? 0L,
                Key = key,
                FloraKey = locationName,
                PlanetType = planetType,
                Kind = kind,
                Circumference = circumference,
                Continents = Game?.TerrainContinents ?? false,
                Generation = Game?.TerrainGeneration ?? 0, // #2692: the save's generation
                LavaCoreVolcanoes = Game?.TerrainLavaCoreVolcanoes ?? false,
                RingSeed = ringSeed,
                SunHue = sunHue,
                MapPriority = name == "HomePlanet" ? 10 : 1,
            };
            var body = OrbitBodyBuilder.Build(spec, _root.transform, name, pos, diameter, sunDir, _orbitOwned, b =>
            {
                ob.Map = b.Map;
                ob.SeenVersion = -1; // project the weather onto the fresh map
            });
            if (!string.IsNullOrEmpty(bodyId))
            {
                _bodySpheres[bodyId] = body.Sphere; // #2153: the planet scanner sweeps this sphere
            }

            ob.Planet = body.Planet;
            ob.Sphere = body.Sphere;
            ob.Atmosphere = body.Atmosphere;
            ob.Shell = body.Shell;
            ob.ShellMat = body.ShellMat;
            ob.ShellTex = body.ShellTex;
            ob.ShellBuffer = body.ShellBuffer;
            ob.Noise = body.Noise;
            ob.ShellPhaseDeg = body.ShellPhaseDeg;
            ob.HazeMat = body.HazeMat;
            ob.HazeColor = body.HazeColor;
            ob.Ring = body.Ring;
            ob.RingBase = body.RingBase;
            _orbitBodies.Add(ob);
            return body.Sphere.gameObject;
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
            _hatchMarker = Cube("Hatch", ship.transform, marker, new Vector3(1.0f, 0.6f, 0.25f), _hatchMat); // #3159: EVA only (Hud)
            BuildNozzlePlumes(ship.transform, cells); // #2157/#2162: one plume per real engine, none on the door

            // Close the rear hatch opening so the ship isn't a hole at the stern (you used to see straight into the hull) —
            // #3159: with the closed door it is on the ground, not the blue field of an energy door. It opens (hides) on an
            // EVA so you can still board.
            _doorPanelMat ??= Lit(new Color(0.62f, 0.68f, 0.76f)); // the flight scene's door look (AddPlacedDoors)
            _doorRimMat ??= Unlit(new Color(0.2f, 0.85f, 1f));
            _hatchDoor = ShipHatchDoor.Build(ship.transform, hatch, _shipCentre, _doorPanelMat, _doorRimMat);
            AddPlacedDoors(ship.transform, _shipDoors, _shipCentre); // #2119: the doors the owner built are doors in flight too
            return ship;
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
                ShipCinematicFx.DestroyWithMeshes(_hatchDoor); // #3159: the hatch door's meshes are its own
                Destroy(_ship);
            }

            _exhaust = null;
            _thruster = null;
            _hatchMat = null;
            _hatchMarker = null;
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
            _sunColor = FlightStarColor(); // #3075: the star of the system the flight is in (SpaceView.Sun.cs)

            var go = new GameObject("Sun");
            go.transform.SetParent(_root.transform, false);
            _sun = go.transform;

            _sunMat = MakeSunLayer(_sun, _sunColor, 320f);                          // outer corona (star colour)
            _sunCoreMat = MakeSunLayer(_sun, SunCoreColor(_sunColor), 150f);        // hot core, still in the star's colour (#3077)
            MakeSunLayer(_sun, Color.white, SunCentreSize);                         // blazing white centre
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
