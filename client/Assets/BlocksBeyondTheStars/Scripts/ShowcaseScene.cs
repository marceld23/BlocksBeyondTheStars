// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Showcase;
using BlocksBeyondTheStars.Shared.Content;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The shell's one fixed motif in 3D (#3705): the thunderbolt in front of a ringed "varied" world with clouds and a small
    /// icy moon (<see cref="ShowcaseMotif"/>), built from the game's real parts — the planet and the moon by the flight
    /// view's own renderer (<see cref="OrbitBodyBuilder"/>: the world map the client bakes itself, the data clouds shaded
    /// toward the star, the atmosphere limb, the seeded ring of #596), the ship from its shipped layout through the block mesher (<see cref="ShipMeshBuilder"/>) with the
    /// flight view's soft engine plumes (<see cref="SpaceFx.EnginePlume"/>) and its closed rear hatch
    /// (<see cref="ShipHatchDoor"/>). The menu backdrop (<see cref="MenuBackground"/>, also behind the title splash and the
    /// loading screen) and the first-start intro film (<see cref="IntroCinematic"/>) place it in their own framing; the poster
    /// capture photographs the backdrop. No server, no world: the menu is up before either exists.
    /// <list type="bullet">
    /// <item><b>Small hardware:</b> Potato and Low drop the atmosphere limb (one transparent pass over the planet less) and
    /// the moon (one more sphere and map bake), bake the planet's map at half the size and thin the plumes; the planet, its
    /// ring (one flat disc), its clouds and the ship stay — it is still the scene the automatic quality calibration
    /// measures (<see cref="AutoQualityCalibrator"/>), at about the old backdrop's cost per preset.</item>
    /// <item><b>Browser:</b> nothing is downloaded; the map bake runs in the 4 ms frame slices of <see cref="WorldMinimap"/>
    /// (the planet shows its flat data colour until then), no thread, no new shader.</item>
    /// </list>
    /// </summary>
    internal sealed class ShowcaseScene
    {
        /// <summary>The planet's slow turn and the clouds' drift over it (degrees per second).</summary>
        private const float PlanetSpin = 1.2f;
        private const float CloudDrift = 0.7f;
        private const float PlanetTilt = 8f;
        private const float PlumeRate = 45f;

        /// <summary>The ring's size against the flight view's (a ring reaches 2.3 planet radii there — wider than the menu's
        /// screen; smaller would cut into the planet), its opacity and its pose: the seed's style and colour, but held at a flat
        /// 14° toward the camera with a slight lean — the seed's own 31° filled half the poster with ring.</summary>
        private const float RingScale = 0.85f;
        private const float RingAlpha = 0.32f;
        private static readonly Quaternion RingPose = Quaternion.AngleAxis(7f, Vector3.forward) * Quaternion.AngleAxis(-14f, Vector3.right);

        /// <summary>The moon's size, its orbit (clear of the ring's edge, inclined 35° toward the camera so its near half
        /// passes below the frame instead of past the lens) and its pace (one turn in 2.5 min). Phase 0 = right of the
        /// planet, 90 = behind it and up, 270 = in front of it and down.</summary>
        private const float MoonSize = 0.2f;       // × the planet's diameter
        private const float MoonOrbit = 1.35f;     // × the planet's diameter, from its centre (outside the ring's edge on screen too)
        private const float MoonPace = 2.4f;       // degrees per second
        private static readonly Quaternion MoonPlane = Quaternion.AngleAxis(-10f, Vector3.forward) * Quaternion.AngleAxis(-35f, Vector3.right);

        /// <summary>The planet's map: the desktop's rich look at four times the flight view's resolution (0.9 s on a worker
        /// thread), else twice (the browser bakes in 4 ms frame slices; small hardware waits less for the same picture).</summary>
        private const int RichMapWidth = 384, RichMapHeight = 192;
        private const int PlainMapWidth = 192, PlainMapHeight = 96;

        private static readonly Color PlumeColor = new Color(0.6f, 0.85f, 1f);

        private readonly Transform _root;
        private readonly GameContent _content;
        private readonly bool _rich;
        private readonly bool _calm;
        private readonly List<Object> _owned = new List<Object>();

        private BlockTextureAtlas _atlas;
        private Material _chunkMat;
        private Material _chunkMatT;
        private OrbitBodyBuilder.Body _planet;
        private OrbitBodyBuilder.Body _moon;
        private Vector3 _planetPos;
        private float _planetDiameter;
        private GameObject _hatchDoor;
        private float _planetYaw;

        /// <param name="root">The scene root everything is built under (the caller's camera space).</param>
        /// <param name="content">The game content (block atlas, planet types, the thunderbolt's layout).</param>
        /// <param name="preset">The quality preset: Medium and up draw every detail.</param>
        /// <param name="reducedEffects">The comfort setting: thinner plumes, no bobbing (the caller reads <see cref="Calm"/>).</param>
        public ShowcaseScene(Transform root, GameContent content, QualityPreset preset, bool reducedEffects)
        {
            _root = root;
            _content = content;
            _rich = RichDetail(preset);
            _calm = reducedEffects;
        }

        /// <summary>The ship's holder (scaled to its showcase length), or null before <see cref="BuildShip"/> made it.</summary>
        public Transform Ship { get; private set; }

        /// <summary>The planet sphere, or null before <see cref="BuildPlanet"/>.</summary>
        public Transform Planet => _planet?.Sphere;

        /// <summary>The moon sphere, or null (Potato and Low, or before <see cref="BuildMoon"/>).</summary>
        public Transform Moon => _moon?.Sphere;

        /// <summary>The moon's place on its orbit (degrees; 0 = right of the planet, 90 = behind it). Owners may set it — the
        /// poster holds it where it frames best — and <see cref="Tick"/> moves it on.</summary>
        public float MoonPhase { get; set; }

        /// <summary>True once the planet (and the moon, when there is one) wear their baked world maps (the poster capture
        /// waits for it).</summary>
        public bool MapReady => _planet != null && _planet.Map != null && (_moon == null || _moon.Map != null);

        /// <summary>The reduced-effects setting: owners keep the ship still.</summary>
        public bool Calm => _calm;

        /// <summary>Medium and up draw the full motif; Potato and Low the simpler one.</summary>
        public static bool RichDetail(QualityPreset preset) => preset >= QualityPreset.Medium;

        /// <summary>Builds the showcase planet at <paramref name="localPos"/> under the root, lit from <paramref name="sunDir"/>
        /// (world space, toward the star). Null without content.</summary>
        public Transform BuildPlanet(Vector3 localPos, float diameter, Vector3 sunDir)
        {
            if (_content == null || _planet != null)
            {
                return _planet?.Sphere;
            }

            EnsureAtlas();
            var spec = new OrbitBodySpec
            {
                Content = _content,
                Atlas = _atlas,
                WorldSeed = ShowcaseMotif.WorldSeed,
                Key = ShowcaseMotif.BodyId,
                FloraKey = ShowcaseMotif.FloraKey,
                PlanetType = ShowcaseMotif.PlanetType,
                Kind = "Planet",
                Circumference = ShowcaseMotif.Circumference,
                Continents = ShowcaseMotif.Continents,
                Generation = ShowcaseMotif.Generation,
                MapPriority = 10,
                Limb = _rich,
                RingSeed = ShowcaseMotif.RingSeed,
                MapWidth = _rich && !IsBrowser ? RichMapWidth : PlainMapWidth,
                MapHeight = _rich && !IsBrowser ? RichMapHeight : PlainMapHeight,
            };
            _planet = OrbitBodyBuilder.Build(spec, _root, "ShowcasePlanet", localPos, diameter, sunDir, _owned);
            _planet.Sphere.localRotation = Quaternion.Euler(PlanetTilt, 0f, 0f);
            if (_planet.Ring != null)
            {
                _planet.Ring.localScale = Vector3.one * RingScale;
                _planet.RingBase = RingPose;
                var ringMat = _planet.Ring.GetComponent<Renderer>().sharedMaterial;
                _owned.Add(ringMat); // PlanetRings made it for this ring: freed with the scene
                var tint = ringMat.color;
                tint.a = RingAlpha;
                ringMat.color = tint;
            }

            _planetPos = localPos;
            _planetDiameter = diameter;
            KeepRingSteady();
            return _planet.Sphere;
        }

        /// <summary>Builds the small icy moon on its orbit around the planet (call after <see cref="BuildPlanet"/>), lit
        /// from <paramref name="sunDir"/>. Medium and up only — null on Potato and Low or without a planet.</summary>
        public Transform BuildMoon(Vector3 sunDir)
        {
            if (!_rich || _planet == null || _moon != null)
            {
                return _moon?.Sphere;
            }

            var spec = new OrbitBodySpec
            {
                Content = _content,
                Atlas = _atlas,
                WorldSeed = ShowcaseMotif.WorldSeed,
                Key = ShowcaseMotif.MoonBodyId,
                FloraKey = ShowcaseMotif.FloraKey,
                PlanetType = ShowcaseMotif.MoonPlanetType,
                Kind = "Moon",
                Circumference = ShowcaseMotif.MoonCircumference,
                Generation = ShowcaseMotif.Generation,
                MapPriority = 9, // after the planet
            };
            _moon = OrbitBodyBuilder.Build(spec, _root, "ShowcaseMoon", MoonPosition(), _planetDiameter * MoonSize, sunDir, _owned);
            return _moon.Sphere;
        }

        /// <summary>Builds the thunderbolt, scaled so its longest side is <paramref name="length"/> units, painted with
        /// <paramref name="hull"/>, with its engine plumes and the closed rear hatch. Null when the content, the thunderbolt or the
        /// block atlas is missing — the scene then simply shows no ship.</summary>
        public Transform BuildShip(float length, Color hull)
        {
            if (Ship != null)
            {
                return Ship;
            }

            var design = ShowcaseMotif.ShipDesign(_content);
            if (design == null || !EnsureAtlas())
            {
                return null;
            }

            var holder = new GameObject("ShowcaseShip");
            holder.transform.SetParent(_root, false);
            var voxel = ShipMeshBuilder.BuildVoxelShip(_content, _atlas, _chunkMat, _chunkMatT, holder.transform, design, out float extent, hull);
            if (voxel == null)
            {
                Object.Destroy(holder);
                return null;
            }

            holder.transform.localScale = Vector3.one * (length / Mathf.Max(1f, extent));
            AddPlumes(voxel.transform, design);
            AddHatchDoor(voxel.transform, design);
            Ship = holder.transform;
            return Ship;
        }

        /// <summary>Per frame: drives the map bake (no world runs one in the shell), turns the planet and lets its clouds drift.</summary>
        public void Tick(float dt)
        {
            WorldMinimap.Pump();
            if (_planet == null || _planet.Sphere == null)
            {
                return;
            }

            _planetYaw = Mathf.Repeat(_planetYaw + dt * PlanetSpin, 360f);
            _planet.Sphere.localRotation = Quaternion.Euler(PlanetTilt, _planetYaw, 0f);
            KeepRingSteady();
            if (_planet.Shell != null)
            {
                _planet.ShellPhaseDeg = Mathf.Repeat(_planet.ShellPhaseDeg + dt * CloudDrift, 360f);
                _planet.Shell.localRotation = Quaternion.Euler(0f, _planet.ShellPhaseDeg, 0f);
            }

            if (_moon != null && _moon.Sphere != null)
            {
                MoonPhase = Mathf.Repeat(MoonPhase + dt * MoonPace * (_calm ? 0.5f : 1f), 360f);
                _moon.Sphere.localPosition = MoonPosition();
            }
        }

        /// <summary>The ring does not turn with the planet's day (as in the story's space vista): it keeps its seeded tilt in
        /// the scene's frame.</summary>
        private void KeepRingSteady()
        {
            if (_planet?.Ring != null)
            {
                _planet.Ring.localRotation = Quaternion.Inverse(_planet.Sphere.localRotation) * _planet.RingBase;
            }
        }

        /// <summary>The moon's place: on its inclined circle around the planet, at <see cref="MoonPhase"/>.</summary>
        private Vector3 MoonPosition()
        {
            float a = MoonPhase * Mathf.Deg2Rad;
            return _planetPos + MoonPlane * new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (_planetDiameter * MoonOrbit);
        }

        private static bool IsBrowser => Application.platform == RuntimePlatform.WebGLPlayer;

        /// <summary>Frees what the scene made beyond its GameObjects (materials, textures, the hatch's meshes, the atlas lease).
        /// The caller destroys the root.</summary>
        public void Dispose()
        {
            ShipCinematicFx.DestroyWithMeshes(_hatchDoor);
            _hatchDoor = null;
            foreach (var o in _owned)
            {
                if (o != null)
                {
                    Object.Destroy(o);
                }
            }

            _owned.Clear();
            if (_chunkMat != null)
            {
                Object.Destroy(_chunkMat);
                _chunkMat = null;
            }

            if (_chunkMatT != null)
            {
                Object.Destroy(_chunkMatT);
                _chunkMatT = null;
            }

            _atlas?.Release(); // shared per process (#1523)
            _atlas = null;
            _planet = null;
            _moon = null;
            Ship = null;
        }

        /// <summary>The block atlas + chunk materials (the GameBootstrap recipe), acquired once. False without content or the
        /// atlas shader.</summary>
        private bool EnsureAtlas()
        {
            if (_chunkMat != null)
            {
                return true;
            }

            var atlasShader = Shader.Find("BlocksBeyondTheStars/BlockAtlas");
            if (_content == null || atlasShader == null)
            {
                return false;
            }

            _atlas ??= BlockTextureAtlas.Acquire(_content); // shared with the bootstrap/editors (#1523)
            _chunkMat = new Material(atlasShader) { mainTexture = _atlas.Texture };
            _atlas.BindNormals(_chunkMat); // stays bound across an atlas repaint (#1952)
            var transparentShader = Shader.Find("BlocksBeyondTheStars/BlockAtlasTransparent");
            if (transparentShader != null)
            {
                _chunkMatT = new Material(transparentShader) { mainTexture = _atlas.Texture };
            }

            return true;
        }

        /// <summary>One soft plume per real engine (#2162), as on every ship in flight; they scale with the ship.</summary>
        private void AddPlumes(Transform voxel, BlocksBeyondTheStars.Networking.Messages.SpaceShipDesign design)
        {
            float rate = PlumeRate * (_rich ? 1f : 0.5f) * (_calm ? 0.5f : 1f);
            foreach (var (local, size) in ShipMeshBuilder.ExhaustPoints(_content, design))
            {
                var ps = SpaceFx.EnginePlume(voxel, local, size, PlumeColor);
                if (ps == null)
                {
                    continue;
                }

                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy; // the showcase ship is scaled down — so is its flame
                var emission = ps.emission;
                emission.rateOverTime = rate;
            }
        }

        /// <summary>The rear hatch as the closed door it is in flight (#3159) — the layout leaves the doorway open, and the intro
        /// film flies the ship away from the camera.</summary>
        private void AddHatchDoor(Transform voxel, BlocksBeyondTheStars.Networking.Messages.SpaceShipDesign design)
        {
            var cells = new List<(int X, int Y, int Z)>(design.Block.Length);
            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
            for (int i = 0; i < design.Block.Length; i++)
            {
                int x = design.X[i], y = design.Y[i], z = design.Z[i];
                cells.Add((x, y, z));
                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                minZ = Mathf.Min(minZ, z);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
                maxZ = Mathf.Max(maxZ, z);
            }

            // The mesher centres the hull on its bounding box; the door goes into the same frame.
            var centre = new Vector3((minX + maxX + 1) * 0.5f, (minY + maxY + 1) * 0.5f, (minZ + maxZ + 1) * 0.5f);
            var plan = ShipHatch.Find(cells, design.Width, design.Height, design.Length);
            var panel = ModelKit.Lit(new Color(0.62f, 0.68f, 0.76f)); // the flight scene's door look
            var trim = ModelKit.Unlit(new Color(0.2f, 0.85f, 1f));
            _owned.Add(panel);
            _owned.Add(trim);
            _hatchDoor = ShipHatchDoor.Build(voxel, plan, centre, panel, trim);
        }
    }
}
