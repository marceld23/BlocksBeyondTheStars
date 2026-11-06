// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The sea of clouds (#3272): climb above the world's cloud band (a summit, a tower, the lift to 256) and the band lies
    /// below you as a sheet of clouds the summits rise out of. A flat disc (dense rings in the middle, ~90 % of the far plane
    /// wide) follows the camera in XZ at <see cref="WorldEnvironment.CloudTopY"/>; the <c>CloudSea</c> shader cuts the
    /// cloud out of world-fixed noise that drifts with the wind, so the holes follow the weather's cover
    /// (<c>Sky.CloudBandCover01</c>, the one <see cref="CloudBandRules.Cover"/> the haze band uses too): nothing on a clear
    /// day, patchy with clouds, nearly closed in rain. Visible only from above — it fades in from just under the top to
    /// fully there 6 m above it (<see cref="CloudSeaRules.DeckVisibility"/>); inside the band the haze shader's band term
    /// is the cloud. Its colour is the band's own lit cloud colour (the shader reads it from the shared atmosphere include),
    /// so the cloud you climb out of is the cloud below; the colours set here — lit like the dome clouds — stand in while no
    /// band colour is published. Medium+ on a desktop behind the "Cloud fog" switch (<see cref="EnabledFor"/>); never in space, on a station, aboard
    /// or underground. Purely visual.
    /// <para>The name: <see cref="CloudDeck"/> (Client.Core, #3202) is the dome clouds' drift — this is the sheet.</para>
    /// </summary>
    public sealed class CloudSea : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;
        public AtmosphereProbe Probe;
        public Sky Sky;

        /// <summary>The "Cloud fog" switch on Medium+ on a desktop (<see cref="EnabledFor"/>) — WorldRig sets it, the settings live.</summary>
        public bool Enabled;

        private const int Rings = 40, Segments = 64;
        private const float ShowRate = 0.5f; // per second
        private static readonly int LitColorId = Shader.PropertyToID("_LitColor");
        private static readonly int ShadeColorId = Shader.PropertyToID("_ShadeColor");
        private static readonly int ParamsId = Shader.PropertyToID("_Params");
        private static readonly int ScrollId = Shader.PropertyToID("_Scroll");
        private static readonly int ShapeId = Shader.PropertyToID("_Shape");

        private Material _mat;
        private Mesh _disc;
        private MeshRenderer _renderer;
        private Transform _sheet;
        private Vector2 _wind;        // the coarse octave's wind offset (blocks), wrapped at one noise tile
        private Vector2 _evolve;      // the fine octave's own drift (uv), wrapped at one tile
        private float _shown;         // eased 0..1
        private Color _lit, _shade;   // eased colours (sRGB-authored)
        private bool _colourValid;
        private bool _snapPending;

        /// <summary>Whether a client draws the deck: the "Cloud fog" switch, Medium or better, never in the browser or on
        /// another light device.</summary>
        public static bool EnabledFor(ClientSettings s)
            => s != null && s.CloudFog && CloudSeaRules.Allowed((int)s.Preset, ClientSettings.FarViewLightDevice);

        private void Awake()
        {
            var shader = Shader.Find("BlocksBeyondTheStars/CloudSea");
            if (shader == null)
            {
                enabled = false;
                return;
            }

            _mat = new Material(shader) { mainTexture = MistNoiseTexture.Get(), renderQueue = 3000 };
            _disc = BuildDisc();
            var go = new GameObject("CloudSea");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _disc;
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _mat;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _renderer.enabled = false;
            _sheet = go.transform;
        }

        private void OnEnable() => GameBootstrap.CaptureSnap += OnCaptureSnap;

        private void OnDisable()
        {
            GameBootstrap.CaptureSnap -= OnCaptureSnap;
            Hide();
            _shown = 0f;
        }

        private void OnDestroy()
        {
            if (_mat != null)
            {
                Destroy(_mat);
            }

            if (_disc != null)
            {
                Destroy(_disc);
            }
        }

        /// <summary>A capture asks every eased look to settle (#2405).</summary>
        private void OnCaptureSnap() => _snapPending = true;

        private void LateUpdate()
        {
            if (Game == null || Camera == null || _mat == null)
            {
                return;
            }

            var env = Game.Environment;
            if (env == null)
            {
                Hide();
                _shown = 0f;
                return;
            }

            Vector3 cam = Camera.transform.position;
            float cover = Sky != null ? Sky.CloudBandCover01 : 0f;
            float target = Enabled ? PlaceGate(env) * CloudSeaRules.Opacity(cover) : 0f;
            bool snap = _snapPending;
            _snapPending = false;
            _shown = snap ? target : Mathf.MoveTowards(_shown, target, Time.deltaTime * ShowRate);
            // The camera height applies at once (it changes smoothly anyway): sinking into the band, the deck is gone before
            // it could hang overhead.
            float opacity = _shown * CloudSeaRules.DeckVisibility(cam.y, env.CloudBaseY, env.CloudTopY);
            if (opacity <= 0.002f)
            {
                Hide();
                return;
            }

            float radius = CloudSeaRules.Radius(Camera.farClipPlane);
            _sheet.SetPositionAndRotation(new Vector3(cam.x, env.CloudTopY, cam.z), Quaternion.identity);
            _sheet.localScale = new Vector3(radius, 1f, radius);
            Drift(Game.WorldDeltaTime);
            Colours(env, snap);
            _mat.SetVector(ParamsId, new Vector4(CloudSeaRules.Threshold(cover), CloudSeaRules.EdgeSoftness, opacity,
                1f / CloudSeaRules.NoiseTileBlocks));
            _mat.SetVector(ScrollId, new Vector4(_wind.x, _wind.y, _evolve.x, _evolve.y));
            _mat.SetVector(ShapeId, new Vector4(radius, 0.55f, 8f, 0f));
            if (!_renderer.enabled)
            {
                _renderer.enabled = true;
            }
        }

        /// <summary>0..1 by where the player is (the camera height is applied separately): never in space, on a station,
        /// aboard, on a spacewalk or underground.</summary>
        private float PlaceGate(WorldEnvironment env)
        {
            if (env.SpaceSky || Game.SpaceViewActive || Game.Aboard || Game.InEva || !string.IsNullOrEmpty(Game.StationName))
            {
                return 0f;
            }

            return Probe != null ? 1f - Probe.UndergroundFactor : 1f;
        }

        /// <summary>The coarse octave rides the smoothed wind (a slow drift even when calm), the fine one evolves on its own —
        /// on the world clock, so the deck parks while the world is held. Both wrap at one tile (seamless).</summary>
        private void Drift(float worldDt)
        {
            float dir = Game.WindDirection;
            float speed = 1.5f + 6f * Game.WindSpeed; // blocks per second
            _wind += new Vector2(Mathf.Cos(dir), Mathf.Sin(dir)) * (speed * worldDt);
            _wind.x = Mathf.Repeat(_wind.x, CloudSeaRules.NoiseTileBlocks);
            _wind.y = Mathf.Repeat(_wind.y, CloudSeaRules.NoiseTileBlocks);
            _evolve += new Vector2(0.004f, -0.0027f) * worldDt;
            _evolve.x = Mathf.Repeat(_evolve.x, 1f);
            _evolve.y = Mathf.Repeat(_evolve.y, 1f);
        }

        /// <summary>The deck's stand-in colours (the shader prefers the band's own, <c>_Sc_CloudBandColor</c>), like the dome
        /// clouds' (<c>Clouds.CloudTint</c>): the planet's cloud colour with a quarter of the world's own mist colour mixed in,
        /// darker at night and in storms, a touch of the star's colour, more at low sun. The shaded side is the same, dimmer.
        /// Eased over a few seconds.</summary>
        private void Colours(WorldEnvironment env, bool snap)
        {
            float tod = Sky != null ? Sky.DayFraction : Game.LocalTimeOfDay;
            float day = Mathf.Clamp01(Mathf.Sin((tod - 0.25f) * Mathf.PI * 2f) * 0.5f + 0.5f);
            Color baseColor = Rgb(env.CloudColor);
            if (env.MistColor >= 0)
            {
                baseColor = Color.Lerp(baseColor, Rgb(env.MistColor), 0.25f);
            }

            float bright = (0.35f + 0.65f * day) * CloudDeck.Target(env.Weather, env.CloudDensity).Darken;
            Color lit = baseColor * bright;
            lit = Color.Lerp(lit, lit * Rgb(env.SunColor), 0.1f + 0.25f * (1f - day));
            Color shade = lit * 0.62f;
            lit.a = 1f;
            shade.a = 1f;

            float k = snap || !_colourValid ? 1f : Mathf.Clamp01(Time.deltaTime * 0.5f);
            _lit = Color.Lerp(_lit, lit, k);
            _shade = Color.Lerp(_shade, shade, k);
            _colourValid = true;
            _mat.SetColor(LitColorId, ShaderColor.Srgb(_lit));
            _mat.SetColor(ShadeColorId, ShaderColor.Srgb(_shade));
        }

        /// <summary>A unit disc in XZ: a centre vertex and <see cref="Rings"/> rings of <see cref="Segments"/> vertices whose
        /// radii grow as (k/Rings)^1.6 — dense under the camera, where the per-vertex haze and the depth fade matter most.</summary>
        private static Mesh BuildDisc()
        {
            var verts = new Vector3[1 + Rings * Segments];
            verts[0] = Vector3.zero;
            for (int k = 1; k <= Rings; k++)
            {
                float r = Mathf.Pow(k / (float)Rings, 1.6f);
                for (int s = 0; s < Segments; s++)
                {
                    float a = s * Mathf.PI * 2f / Segments;
                    verts[1 + (k - 1) * Segments + s] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                }
            }

            var tris = new int[Segments * 3 + (Rings - 1) * Segments * 6];
            int t = 0;
            for (int s = 0; s < Segments; s++)
            {
                tris[t++] = 0;
                tris[t++] = 1 + (s + 1) % Segments;
                tris[t++] = 1 + s;
            }

            for (int k = 1; k < Rings; k++)
            {
                int inner = 1 + (k - 1) * Segments, outer = 1 + k * Segments;
                for (int s = 0; s < Segments; s++)
                {
                    int s1 = (s + 1) % Segments;
                    tris[t++] = inner + s;
                    tris[t++] = inner + s1;
                    tris[t++] = outer + s;
                    tris[t++] = outer + s;
                    tris[t++] = inner + s1;
                    tris[t++] = outer + s1;
                }
            }

            var mesh = new Mesh { name = "CloudSeaDisc", vertices = verts, triangles = tris };
            mesh.RecalculateBounds();
            return mesh;
        }

        private void Hide()
        {
            if (_renderer != null && _renderer.enabled)
            {
                _renderer.enabled = false;
            }
        }

        private static Color Rgb(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
