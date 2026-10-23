// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Cutscenes;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The cutscene director's synthetic stages (#2477, synthesis §6.3/§6.5, d4 §3.2): a <c>space_vista</c> — the menu's
    /// attract-scene recipe in-game (the real <see cref="Starfield"/> and <see cref="NebulaField"/> in menu mode, the layered
    /// sun glow, a phase-lit planet with its cloud shell from <see cref="MenuBackground"/>'s recipes, and with the <c>ship</c>
    /// anchor the player's own voxel ship through <see cref="ShipMeshBuilder"/>) — and the small dioramas: the sleeping
    /// Guardian core (a voxel column of red light in an iron shell, meshed by the same ship mesher, with a breathing glow)
    /// and VEGA's memory of the star chart (the star map's systems as soft glows on a disc, the camera drifting out to a faint
    /// red pulse beyond the rim). Independent of where the player is: the stage stands far above the world
    /// (<see cref="Origin"/>) with its own camera drawn over the world camera, whose culling mask is emptied meanwhile (it
    /// keeps running — <c>Camera.main</c> users stay intact — but draws nothing). The layout and the camera paths are the
    /// Unity-free <see cref="SyntheticStages"/>; this class only builds and flies them.
    /// <list type="bullet">
    /// <item><b>Small hardware / WebGL:</b> existing, always-included shaders only (Starfield, Nebula, SunGlow, SkyBodyPhase,
    /// Cloud, Particle, the block atlas) — no new shader, no keyword, no texture asset, no light; the backdrop is built once
    /// per world and kept inactive between shots; a set is destroyed when its shot ends. The director never asks for it on
    /// the Potato/Low presets (<see cref="CutsceneRules.SyntheticStagesAllowed"/>), and a stage that cannot be built (a
    /// stripped shader, content not there) reports false so the shot falls back to the sky or the player's view.</item>
    /// <item><b>Lighting:</b> the block shaders read the planet's globals (<c>_Sc_Light</c>, the sun direction, the headlamp,
    /// wet/snow, under water); the stage sets space values right before its own camera draws
    /// (<c>RenderPipelineManager.beginCameraRendering</c>, the per-camera pattern of <see cref="Sky"/>'s haze) — the world's
    /// owners write theirs again every frame. Its camera is no world camera, so the haze is off for it.</item>
    /// </list>
    /// </summary>
    internal sealed class CutsceneStage
    {
        /// <summary>Where the stage stands: far above any world, the flight view (y ≈ 6000) and the previews (y 100000).</summary>
        private static readonly Vector3 Origin = new Vector3(0f, 40000f, 0f);

        private const float CameraFar = 900f;
        private const float CameraFov = 55f;
        private const int CoreGlowRgb = (0xFF << 16) | (0x34 << 8) | 0x24; // the column's own red glow (mesher glow channel)

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int LightId = Shader.PropertyToID("_Sc_Light");
        private static readonly int SunDirId = Shader.PropertyToID("_Sc_SunDir");
        private static readonly int IndoorId = Shader.PropertyToID("_Sc_Indoor");
        private static readonly int LampColorId = Shader.PropertyToID("_Sc_LampColor");
        private static readonly int FloraTintId = Shader.PropertyToID("_Sc_FloraTint");
        private static readonly int SurfaceId = Shader.PropertyToID("_Sc_Surface");
        private static readonly int UnderwaterId = Shader.PropertyToID("_Sc_Underwater");

        private static readonly Color SpaceLight = Color.white;                       // full-bright, like the flight view
        private static readonly Color CoreLight = new Color(0.30f, 0.30f, 0.36f, 1f); // dim: the column's red light carries it
        private static readonly Color CoreRed = new Color(1f, 0.22f, 0.16f, 1f);
        private static readonly Color PlanetColor = new Color(0.30f, 0.52f, 0.66f);  // a living world
        private static readonly Color SunColor = new Color(1f, 0.95f, 0.86f);

        private readonly GameBootstrap _game;
        private readonly Camera _world;
        private readonly List<Object> _owned = new List<Object>();    // the backdrop's assets (the stage's lifetime)
        private readonly List<Object> _setOwned = new List<Object>(); // the current set's assets (one shot)

        private GameObject _root;
        private Camera _cam;
        private Starfield _stars;
        private NebulaField _nebula;
        private GameObject _sun;
        private Texture2D _glowTex;
        private Texture2D _cloudTex;
        private Shader _particle;
        private Vector3 _sunDir;

        private GameObject _set;
        private SyntheticSet _kind;
        private MemoryChart _chart;
        private Transform _planet;
        private Transform _clouds;
        private Transform _ship;
        private Transform _shipGlow;
        private float _shipRear;
        private Material _pulseMat;
        private float _pulseBase;
        private float _pulseSwing;
        private Color _light = Color.white;

        private bool _shown;
        private bool _hooked;
        private bool _maskSaved;
        private int _savedMask;

        public CutsceneStage(GameBootstrap game, Camera worldCamera)
        {
            _game = game;
            _world = worldCamera;
        }

        /// <summary>Whether a set is on screen.</summary>
        public bool Showing => _shown;

        /// <summary>Builds <paramref name="set"/> and puts it on screen; <paramref name="memory"/> (the shot's memory or
        /// flashback grade) tints the memory chart's stars warm. False when it cannot be built — nothing changes then.</summary>
        public bool Show(SyntheticSet set, bool memory)
        {
            if (!EnsureBackdrop())
            {
                return false;
            }

            ClearSet();
            _set = new GameObject("CutsceneStageSet");
            _set.transform.SetParent(_root.transform, false);
            bool built = set switch
            {
                SyntheticSet.Core => BuildCore(),
                SyntheticSet.MemoryChart => BuildChart(memory),
                _ => BuildVista(set),
            };
            if (!built)
            {
                ClearSet();
                return false;
            }

            _kind = set;
            bool vista = set != SyntheticSet.Core && set != SyntheticSet.MemoryChart;
            _sun.SetActive(vista);
            _stars.MenuBrightness = vista ? 1f : 0.55f;   // a diorama is a memory: the sky behind it dimmer
            _nebula.MenuBrightness = vista ? 0.85f : 0.35f;
            _light = set == SyntheticSet.Core ? CoreLight : SpaceLight;
            _root.SetActive(true);
            TakeScreen();
            _shown = true;
            Tick(0f, 0f);
            return true;
        }

        /// <summary>Flies the camera to progress <paramref name="k"/> (0..1, eased by the rig) and animates the set at
        /// <paramref name="seconds"/> into the shot. No allocation.</summary>
        public void Tick(float k, float seconds)
        {
            if (!_shown || _cam == null)
            {
                return;
            }

            var t = _cam.transform;
            switch (_kind)
            {
                case SyntheticSet.Core:
                    {
                        SyntheticStages.CoreCamera(k, out float px, out float py, out float pz, out float lx, out float ly, out float lz);
                        var pos = new Vector3(px, py, pz);
                        t.localPosition = pos;
                        t.localRotation = Quaternion.LookRotation(new Vector3(lx, ly, lz) - pos);
                        break;
                    }

                case SyntheticSet.MemoryChart:
                    {
                        SyntheticStages.MemoryChartCamera(_chart, k, out float px, out float py, out float pz, out float lx, out float ly, out float lz);
                        var pos = new Vector3(px, py, pz);
                        t.localPosition = pos;
                        t.localRotation = Quaternion.LookRotation(new Vector3(lx, ly, lz) - pos);
                        break;
                    }

                default:
                    {
                        SyntheticStages.VistaCamera(_kind, k, out float x, out float y, out float z, out float yaw, out float pitch);
                        t.localPosition = new Vector3(x, y, z);
                        t.localRotation = Quaternion.Euler(pitch, yaw, 0f);
                        if (_planet != null)
                        {
                            _planet.localRotation = Quaternion.Euler(8f, seconds * 1.6f, 0f);
                        }

                        if (_clouds != null)
                        {
                            _clouds.localRotation = Quaternion.Euler(0f, seconds * 2.6f, 0f); // drift over the surface
                        }

                        if (_ship != null)
                        {
                            SyntheticStages.VistaShip(k, out float sx, out float sy, out float sz);
                            _ship.localPosition = new Vector3(sx, sy + Mathf.Sin(seconds * 0.6f) * 0.25f, sz);
                            _ship.localRotation = Quaternion.Euler(5f + Mathf.Sin(seconds * 0.5f) * 1.5f, 70f, -4f);
                            if (_shipGlow != null)
                            {
                                _shipGlow.position = _ship.TransformPoint(new Vector3(0f, 0f, _shipRear));
                            }
                        }

                        break;
                    }
            }

            if (_pulseMat != null)
            {
                _pulseMat.SetFloat(IntensityId, _pulseBase + _pulseSwing * SyntheticStages.CorePulse(seconds));
            }
        }

        /// <summary>Takes the set off screen and frees it; the backdrop stays (inactive) for the next shot.</summary>
        public void Hide()
        {
            _shown = false;
            if (_root != null)
            {
                _root.SetActive(false);
            }

            ClearSet();
            GiveScreenBack();
        }

        /// <summary>Frees everything (the director's rig goes with the world).</summary>
        public void Dispose()
        {
            Hide();
            if (_root != null)
            {
                Object.Destroy(_root); // the starfield and the nebula free their own mesh and material (OnDestroy)
                _root = null;
            }

            foreach (var o in _owned)
            {
                if (o != null)
                {
                    Object.Destroy(o);
                }
            }

            _owned.Clear();
        }

        // ------------------------------------------------------------------------------------------
        // The backdrop: camera, stars, nebula, sun — built once.
        // ------------------------------------------------------------------------------------------

        private bool EnsureBackdrop()
        {
            if (_root != null)
            {
                return true;
            }

            var glowShader = Shader.Find("BlocksBeyondTheStars/SunGlow");
            _particle = Shader.Find("BlocksBeyondTheStars/Particle");
            if (glowShader == null || _particle == null || Shader.Find("BlocksBeyondTheStars/Starfield") == null)
            {
                Debug.Log("[Cutscene] The synthetic stage's shaders are missing — its shots fall back.");
                return false;
            }

            _root = new GameObject("CutsceneStage");
            _root.transform.position = Origin;

            var camGo = new GameObject("CutsceneStageCamera");
            camGo.transform.SetParent(_root.transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.01f, 0.015f, 0.04f, 1f);
            _cam.nearClipPlane = 0.2f;
            _cam.farClipPlane = CameraFar;
            _cam.fieldOfView = CameraFov;
            _cam.depth = (_world != null ? _world.depth : 0f) + 1f; // drawn after (over) the world camera's empty frame
            var data = _cam.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                var settings = _game != null ? _game.Settings : null;
                if (settings != null)
                {
                    settings.ApplyCameraLook(data); // the preset's renderer and post — the scene's grade reaches the stage too
                }
                else
                {
                    data.renderPostProcessing = true;
                }

                data.renderShadows = false;
            }

            var sky = new GameObject("Backdrop");
            sky.transform.SetParent(_root.transform, false);
            _stars = sky.AddComponent<Starfield>();
            _stars.Camera = _cam;
            _stars.MenuBrightness = 1f;
            _nebula = sky.AddComponent<NebulaField>();
            _nebula.Camera = _cam;
            _nebula.MenuBrightness = 0.85f;
            _nebula.MenuSeed = 377; // its own sky — the menu uses 142, the intro 271

            _glowTex = SkyVisuals.GlowTexture();
            _owned.Add(_glowTex);
            SyntheticStages.SunDirection(out float sx, out float sy, out float sz);
            _sunDir = new Vector3(sx, sy, sz);
            _sun = new GameObject("Sun");
            _sun.transform.SetParent(_root.transform, false);
            Vector3 sunPos = _sunDir * SyntheticStages.SunDistance;
            var billboard = Quaternion.LookRotation(-sunPos);
            SunLayer(glowShader, "SunCorona", sunPos, billboard, SyntheticStages.SunDistance * 0.34f, SunColor);
            SunLayer(glowShader, "SunCore", sunPos, billboard, SyntheticStages.SunDistance * 0.18f, Color.Lerp(SunColor, Color.white, 0.85f));
            SunLayer(glowShader, "SunCenter", sunPos, billboard, SyntheticStages.SunDistance * 0.085f, Color.white);

            _root.SetActive(false);
            return true;
        }

        /// <summary>One layer of the sun glow (the menu's corona → core → centre recipe).</summary>
        private void SunLayer(Shader shader, string name, Vector3 pos, Quaternion rot, float size, Color color)
        {
            var mat = new Material(shader) { mainTexture = _glowTex };
            mat.SetColor(ColorId, ShaderColor.Srgb(color));
            _owned.Add(mat);
            var quad = Primitive(PrimitiveType.Quad, name, _sun.transform, mat);
            quad.transform.localPosition = pos;
            quad.transform.localRotation = rot;
            quad.transform.localScale = Vector3.one * size;
        }

        // ------------------------------------------------------------------------------------------
        // The sets.
        // ------------------------------------------------------------------------------------------

        /// <summary>The space vista: a phase-lit planet with clouds and a thin atmosphere; with the <c>ship</c> anchor the
        /// player's own ship drifting by in front of it.</summary>
        private bool BuildVista(SyntheticSet set)
        {
            SyntheticStages.VistaPlanet(set, out float px, out float py, out float pz, out float size);
            var pos = new Vector3(px, py, pz);
            var mat = ModelKit.LitPhase(PlanetColor, _sunDir * SyntheticStages.SunDistance - pos);
            _setOwned.Add(mat);
            var planet = Primitive(PrimitiveType.Sphere, "Planet", _set.transform, mat);
            planet.transform.localPosition = pos;
            planet.transform.localScale = Vector3.one * size;
            _planet = planet.transform;

            var cloudShader = Shader.Find("BlocksBeyondTheStars/Cloud");
            if (cloudShader != null)
            {
                if (_cloudTex == null)
                {
                    _cloudTex = MenuBackground.CloudTexture();
                    _owned.Add(_cloudTex);
                }

                _clouds = Shell(cloudShader, "Clouds", 1.04f, _cloudTex, new Color(0.95f, 0.97f, 1f, 0.7f), 3000);
                Shell(cloudShader, "Atmosphere", 1.06f, Texture2D.whiteTexture, new Color(0.55f, 0.75f, 1f, 0.12f), 2999);
            }

            if (set == SyntheticSet.VistaShip)
            {
                BuildShip();
            }

            return true;
        }

        private Transform Shell(Shader shader, string name, float scale, Texture tex, Color tint, int queue)
        {
            var mat = new Material(shader) { mainTexture = tex, renderQueue = queue };
            mat.SetColor(ColorId, ShaderColor.Srgb(tint));
            _setOwned.Add(mat);
            var go = Primitive(PrimitiveType.Sphere, name, _planet, mat);
            go.transform.localScale = Vector3.one * scale;
            return go.transform;
        }

        /// <summary>The player's own voxel ship (its design and hull colour), framed to ~6 units with a soft engine glow. A
        /// player who never flew has no design yet — the vista then shows no ship rather than one that is not theirs.</summary>
        private void BuildShip()
        {
            var design = _game != null ? _game.ShipDesign : null;
            if (!ShipMeshBuilder.HasDesign(design))
            {
                return;
            }

            var holder = new GameObject("Ship");
            holder.transform.SetParent(_set.transform, false);
            Color hull = _game.Settings != null ? _game.Settings.HullColor : new Color(0.82f, 0.84f, 0.88f);
            var voxel = ShipMeshBuilder.BuildVoxelShip(_game, holder.transform, design, out float extent, hull);
            if (voxel == null)
            {
                Object.Destroy(holder);
                return;
            }

            holder.transform.localScale = Vector3.one * (6f / Mathf.Max(1f, extent));
            _ship = holder.transform;
            _shipRear = -extent * 0.5f - 0.6f;
            var quads = new List<(Vector3 Center, float Size, Color Color, bool Flat)>
            {
                (Vector3.zero, 0.9f, new Color(0.5f, 0.85f, 1f, 0.9f), false),
            };
            _shipGlow = GlowObject("EngineGlow", _set.transform, GlowQuads(quads), GlowMaterial(1.6f)).transform;
        }

        /// <summary>The sleeping Guardian core (synthesis §6.5 ch3): <see cref="SyntheticStages.CoreCells"/> meshed by the ship
        /// mesher — a steel floor, the iron shell open to the camera, the column of red light — and a breathing red halo
        /// around the column. The finale chamber's palette, so the finale looks familiar.</summary>
        private bool BuildCore()
        {
            var content = _game != null ? _game.Content : null;
            if (content == null)
            {
                return false;
            }

            ushort floor = BlockOf(content, "steel_floor", "metal_panel", "iron_wall");
            ushort shell = BlockOf(content, "iron_wall", "deepslate", "stone");
            ushort column = BlockOf(content, "light_red", "iron_wall");
            if (floor == 0 || shell == 0 || column == 0)
            {
                return false;
            }

            var cells = SyntheticStages.CoreCells();
            int n = cells.Count;
            var xs = new int[n];
            var ys = new int[n];
            var zs = new int[n];
            var blocks = new ushort[n];
            var glows = new int[n];
            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
            for (int i = 0; i < n; i++)
            {
                var c = cells[i];
                xs[i] = c.X;
                ys[i] = c.Y;
                zs[i] = c.Z;
                blocks[i] = c.Part == CoreDioramaPart.Floor ? floor : c.Part == CoreDioramaPart.Shell ? shell : column;
                glows[i] = c.Part == CoreDioramaPart.Column ? CoreGlowRgb : 0;
                minX = Mathf.Min(minX, c.X);
                minY = Mathf.Min(minY, c.Y);
                minZ = Mathf.Min(minZ, c.Z);
                maxX = Mathf.Max(maxX, c.X);
                maxY = Mathf.Max(maxY, c.Y);
                maxZ = Mathf.Max(maxZ, c.Z);
            }

            var design = new SpaceShipDesign
            {
                Id = "cutscene:core",
                Kind = "ship",
                X = xs,
                Y = ys,
                Z = zs,
                Block = blocks,
                Glow = glows,
                Width = maxX - minX + 1,
                Height = maxY - minY + 1,
                Length = maxZ - minZ + 1,
            };
            var holder = new GameObject("Core");
            holder.transform.SetParent(_set.transform, false);
            var voxel = ShipMeshBuilder.BuildVoxelShip(_game, holder.transform, design, out _, default);
            if (voxel == null)
            {
                return false;
            }

            // The mesher centres a build on its bounding box: put it back so block coordinates are stage coordinates (the
            // layout's camera path is written in them).
            voxel.transform.localPosition = new Vector3((minX + maxX + 1) * 0.5f, (minY + maxY + 1) * 0.5f, (minZ + maxZ + 1) * 0.5f);

            // The halo stands in the column's plane, facing the open front: the column hides its middle, the rest glows
            // around it and over the shell behind — breathing slowly (SyntheticStages.CorePulse, far below 3 Hz).
            float top = SyntheticStages.ColumnHeight + 1f;
            var quads = new List<(Vector3 Center, float Size, Color Color, bool Flat)>
            {
                (new Vector3(0.5f, (1f + top) * 0.5f, 0.5f), 2.8f, CoreRed, false),
            };
            _pulseMat = GlowMaterial(1f);
            _pulseBase = 0.7f;
            _pulseSwing = 1.3f;
            GlowObject("CoreGlow", _set.transform, GlowQuads(quads), _pulseMat);
            return true;
        }

        /// <summary>VEGA's memory of the star chart (synthesis §6.5 ch6): every system of the star map as a soft glow lying on
        /// the chart's disc (the player's system brighter; warm sepia under a memory grade), and the faint red pulse beyond
        /// the rim — no names, no marker. Nothing to show before the star map arrived (the shot falls back).</summary>
        private bool BuildChart(bool memory)
        {
            var map = _game != null ? _game.StarMap : null;
            var systems = new List<(string Id, float MapX, float MapY, int Color)>();
            foreach (var sys in map?.Systems ?? System.Array.Empty<NetStarSystem>())
            {
                if (sys != null)
                {
                    systems.Add((sys.Id, sys.MapX, sys.MapY, sys.StarColor));
                }
            }

            if (systems.Count == 0)
            {
                return false;
            }

            _chart = SyntheticStages.LayoutMemoryChart(systems, CutsceneDirector.SystemOfBody(map, map?.ActiveLocationId));
            var stars = new List<(Vector3 Center, float Size, Color Color, bool Flat)>(_chart.Stars.Count);
            foreach (var s in _chart.Stars)
            {
                Color c = s.Color != 0 ? CutsceneDirector.Rgb(s.Color) : new Color(1f, 0.86f, 0.55f);
                if (s.Current)
                {
                    c = Color.Lerp(c, Color.white, 0.5f);
                }

                if (memory)
                {
                    c = CutsceneScreen.Sepia(c);
                }

                c.a = s.Current ? 1f : 0.85f;
                stars.Add((new Vector3(s.X, 0f, s.Z), s.Current ? 1.6f : 1.1f, c, true));
            }

            GlowObject("ChartStars", _set.transform, GlowQuads(stars), GlowMaterial(1.4f));

            var rim = new List<(Vector3 Center, float Size, Color Color, bool Flat)>
            {
                (new Vector3(_chart.PulseX, 0f, _chart.PulseZ), 6f, new Color(CoreRed.r, CoreRed.g, CoreRed.b, 0.6f), true),
            };
            _pulseMat = GlowMaterial(0.5f);
            _pulseBase = 0.25f;
            _pulseSwing = 0.5f;
            GlowObject("RimGlow", _set.transform, GlowQuads(rim), _pulseMat);
            return true;
        }

        private void ClearSet()
        {
            if (_set != null)
            {
                Object.Destroy(_set); // the voxel chunks free their meshes themselves (OwnedProceduralMesh)
                _set = null;
            }

            foreach (var o in _setOwned)
            {
                if (o != null)
                {
                    Object.Destroy(o);
                }
            }

            _setOwned.Clear();
            _planet = null;
            _clouds = null;
            _ship = null;
            _shipGlow = null;
            _pulseMat = null;
            _chart = null;
        }

        // ------------------------------------------------------------------------------------------
        // The screen and the lighting.
        // ------------------------------------------------------------------------------------------

        /// <summary>The world camera keeps running but draws nothing; the stage's globals are set right before its camera.</summary>
        private void TakeScreen()
        {
            if (_world != null && !_maskSaved)
            {
                _savedMask = _world.cullingMask;
                _maskSaved = true;
                _world.cullingMask = 0;
            }

            if (!_hooked)
            {
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnBeginCamera;
                _hooked = true;
            }
        }

        private void GiveScreenBack()
        {
            if (_maskSaved)
            {
                _maskSaved = false;
                if (_world != null && _world.cullingMask == 0)
                {
                    _world.cullingMask = _savedMask; // unless somebody else changed it meanwhile
                }
            }

            if (_hooked)
            {
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
                _hooked = false;
            }
        }

        /// <summary>Space lighting for the block shaders, set right before the stage's camera draws — the planet's owners
        /// (<see cref="Sky"/>, the headlamp) write their values again every frame before the world camera's next draw.</summary>
        private void OnBeginCamera(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)
        {
            if (camera != _cam)
            {
                return;
            }

            Shader.SetGlobalColor(LightId, _light);
            Shader.SetGlobalVector(SunDirId, _sunDir);
            Shader.SetGlobalFloat(IndoorId, 0f);
            Shader.SetGlobalColor(LampColorId, new Color(0f, 0f, 0f, 0f));
            Shader.SetGlobalColor(FloraTintId, new Color(0f, 0f, 0f, 0f));
            Shader.SetGlobalVector(SurfaceId, Vector4.zero);
            Shader.SetGlobalVector(UnderwaterId, Vector4.zero);
        }

        // ------------------------------------------------------------------------------------------
        // Small builders.
        // ------------------------------------------------------------------------------------------

        private static ushort BlockOf(BlocksBeyondTheStars.Shared.Content.GameContent content, params string[] keys)
        {
            foreach (string key in keys)
            {
                var def = content.GetBlock(key);
                if (def != null)
                {
                    return def.NumericId.Value;
                }
            }

            return 0;
        }

        private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                Object.Destroy(collider);
            }

            go.transform.SetParent(parent, false);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        /// <summary>An additive glow material (the Particle shader: vertex colour × the soft glow sprite × intensity, depth
        /// tested, after the opaque blocks).</summary>
        private Material GlowMaterial(float intensity)
        {
            var mat = new Material(_particle) { mainTexture = _glowTex };
            mat.SetFloat(IntensityId, intensity);
            _setOwned.Add(mat);
            return mat;
        }

        /// <summary>One mesh of glow sprites: a quad per entry (half size <c>Size</c>), lying on the XZ plane when
        /// <c>Flat</c>, else standing in the XY plane (facing the stage's cameras, which look along +z).</summary>
        private Mesh GlowQuads(IReadOnlyList<(Vector3 Center, float Size, Color Color, bool Flat)> quads)
        {
            int n = quads.Count;
            var verts = new Vector3[n * 4];
            var uvs = new Vector2[n * 4];
            var cols = new Color[n * 4];
            var tris = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                var (c, s, color, flat) = quads[i];
                int v = i * 4;
                if (flat)
                {
                    verts[v] = c + new Vector3(-s, 0f, -s);
                    verts[v + 1] = c + new Vector3(-s, 0f, s);
                    verts[v + 2] = c + new Vector3(s, 0f, s);
                    verts[v + 3] = c + new Vector3(s, 0f, -s);
                }
                else
                {
                    verts[v] = c + new Vector3(-s, -s, 0f);
                    verts[v + 1] = c + new Vector3(-s, s, 0f);
                    verts[v + 2] = c + new Vector3(s, s, 0f);
                    verts[v + 3] = c + new Vector3(s, -s, 0f);
                }

                uvs[v] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(0f, 1f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(1f, 0f);
                var col = ShaderColor.Srgb(color);
                for (int k = 0; k < 4; k++)
                {
                    cols[v + k] = col;
                }

                int t = i * 6;
                tris[t] = v;
                tris[t + 1] = v + 1;
                tris[t + 2] = v + 2;
                tris[t + 3] = v;
                tris[t + 4] = v + 2;
                tris[t + 5] = v + 3;
            }

            var mesh = new Mesh { name = "CutsceneStageGlow" };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors = cols;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            _setOwned.Add(mesh);
            return mesh;
        }

        private static GameObject GlowObject(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }
    }
}
