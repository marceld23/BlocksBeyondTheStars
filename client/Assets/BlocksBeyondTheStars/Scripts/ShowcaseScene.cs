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
    /// The shell's one fixed motif in 3D (#3705): the thunderbolt in front of a "varied" world with clouds
    /// (<see cref="ShowcaseMotif"/>), built from the game's real parts — the planet by the flight view's own renderer
    /// (<see cref="OrbitBodyBuilder"/>: the world map the client bakes itself, the data clouds shaded toward the star, the
    /// atmosphere limb), the ship from its shipped layout through the block mesher (<see cref="ShipMeshBuilder"/>) with the
    /// flight view's soft engine plumes (<see cref="SpaceFx.EnginePlume"/>) and its closed rear hatch
    /// (<see cref="ShipHatchDoor"/>). The menu backdrop (<see cref="MenuBackground"/>, also behind the title splash and the
    /// loading screen) and the first-start intro film (<see cref="IntroCinematic"/>) place it in their own framing; the poster
    /// capture photographs the backdrop. No server, no world: the menu is up before either exists.
    /// <list type="bullet">
    /// <item><b>Small hardware:</b> Potato and Low drop the atmosphere limb (one transparent pass over the planet less) and
    /// thin the plumes; the planet, its clouds and the ship stay — it is still the scene the automatic quality calibration
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

        /// <summary>True once the planet wears its baked world map (the poster capture waits for it).</summary>
        public bool MapReady => _planet != null && _planet.Map != null;

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
            };
            _planet = OrbitBodyBuilder.Build(spec, _root, "ShowcasePlanet", localPos, diameter, sunDir, _owned);
            _planet.Sphere.localRotation = Quaternion.Euler(PlanetTilt, 0f, 0f);
            return _planet.Sphere;
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
            if (_planet.Shell != null)
            {
                _planet.ShellPhaseDeg = Mathf.Repeat(_planet.ShellPhaseDeg + dt * CloudDrift, 360f);
                _planet.Shell.localRotation = Quaternion.Euler(0f, _planet.ShellPhaseDeg, 0f);
            }
        }

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
