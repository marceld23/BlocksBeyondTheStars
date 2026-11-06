// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Drives the visible day/night + weather + sun colour from the server's `WorldEnvironment`
    /// (World systems). Because the world uses unlit shaders, the main effect is a **global tint**
    /// (`_Sc_Light`) multiplied into the block shaders — sun colour × day brightness × weather dim.
    /// Also drives the sky (camera background) and a rotating directional sun light. Time is
    /// advanced locally between the server's periodic updates. Suspended while the space view owns
    /// the camera.
    /// </summary>
    public sealed partial class Sky : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        /// <summary>The player's view distance in chunks (from settings) — the linear distance fog is scaled to
        /// this so the haze engages within the streamed terrain (otherwise it sits beyond the render distance and
        /// is never seen) and softly hides the chunk pop-in at the edge.</summary>
        public int ViewChunks = 4;

        /// <summary>Player "Volumetric fog / light shafts" graphics toggle (wired from WorldRig) — gates the visible
        /// distance haze + the god-ray shafts. (The old full-screen volumetric pass is gone.)</summary>
        public bool FogEnabled = true;

        /// <summary>World direction TO the system's star while the flight view is up (#2157; zero = unknown) — the space
        /// branch lights the hull from it instead of keeping the last planet's sun direction.</summary>
        public static Vector3 SpaceSunDir;

        private static readonly int LightId = Shader.PropertyToID("_Sc_Light");
        private static readonly int SunDirId = Shader.PropertyToID("_Sc_SunDir");
        private static readonly int SkyId = Shader.PropertyToID("_Sc_Sky");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int IndoorId = Shader.PropertyToID("_Sc_Indoor");
        private static readonly int FloraTintId = Shader.PropertyToID("_Sc_FloraTint");
        private static readonly int WaterTintId = Shader.PropertyToID("_Sc_WaterTint"); // #1758: per-world water colour
        private static readonly int WaterModeId = Shader.PropertyToID("_Sc_WaterMode"); // #1758: 0 classic, 1 tint, 2 rainbow
        // Explicit distance haze for the block shaders (Unity's MixFog doesn't engage on the unlit voxels):
        // x=start, y=end, z=max strength (already faded out indoors), w=on.
        private static readonly int FogId = Shader.PropertyToID("_Sc_Fog");
        // Atmosphere package (#2408) — the globals AtmosphereCommon.hlsl reads, see its header for the layouts.
        private static readonly int FogHeightId = Shader.PropertyToID("_Sc_FogHeight");
        private static readonly int FogSunId = Shader.PropertyToID("_Sc_FogSun");
        private static readonly int FogSkyId = Shader.PropertyToID("_Sc_FogSky");
        private static readonly int FogSunDirId = Shader.PropertyToID("_Sc_FogSunDir");
        private static readonly int CloudShadowId = Shader.PropertyToID("_Sc_CloudShadow");
        private static readonly int CloudNoiseId = Shader.PropertyToID("_Sc_CloudNoise");
        private static readonly int FlickerScaleId = Shader.PropertyToID("_Sc_FlickerScale");
        private static readonly int WindId = Shader.PropertyToID("_Sc_Wind");
        private static readonly Vector4 FogOff = new Vector4(0f, 1f, 0f, 0f);

        /// <summary>Wind sway of leaves, grass and flora (#2397), from the settings.</summary>
        public bool WindSwayEnabled = true;
        private float _windTime; // the sway clock: world time, so a held world holds its leaves too

        /// <summary>Wet ground after rain and snow caps after snowfall (#2398), from the settings.</summary>
        public bool WetSurfacesEnabled = true;
        private static readonly int SurfaceId = Shader.PropertyToID("_Sc_Surface");
        private float _wet, _snow; // the eased traces the weather leaves (0..1)

        /// <summary>Underwater visibility (#2401): the haze closes in to a few metres in the water's colour while the
        /// camera is submerged; caustics and bubbles ride on the same state.</summary>
        public bool UnderwaterEnabled = true;
        private static readonly int UnderwaterId = Shader.PropertyToID("_Sc_Underwater");
        private static readonly int SeaLevelId = Shader.PropertyToID("_Sc_SeaLevel"); // #2414: the sea surface's world Y
        private float _submerged; // eased 0..1

        /// <summary>The eased underwater-haze share (0..1, #2416): the weather overlay reads it to thin its own
        /// submerged wash while the shader-side haze carries the look.</summary>
        public static float UnderwaterFog { get; private set; }

        /// <summary>The surroundings probe (fog floor, camera exposure) — wired by <see cref="WorldRig"/>.</summary>
        public AtmosphereProbe Probe;

        /// <summary>Height fog (#2406), cloud shadows (#2394) and the torch-light flicker scale (#2396), from the settings.</summary>
        public bool HeightFogEnabled = true;
        /// <summary>#3271 fog wisps and #3272 cloud fog (the shader band) — from the settings, cheap: every preset.</summary>
        public bool FogWispsEnabled = true;
        public bool CloudFogEnabled = true;
        public bool CloudShadowsEnabled = true;
        public float FlickerScale = 1f;

        /// <summary>Cameras that render the WORLD and get the haze: the gameplay camera is in by itself; the photo item
        /// and the clip recorder register their clones. Every other camera (avatar / ship / structure previews, the menu)
        /// renders with the haze off, so a preview never fogs (#2393).</summary>
        public static readonly System.Collections.Generic.HashSet<Camera> WorldCameras = new System.Collections.Generic.HashSet<Camera>();

        private Vector4 _fogVector = FogOff;          // the live haze for world cameras (see OnBeginCamera)
        private Vector4 _cloudShadowVector;           // cover, scroll offset, strength
        private Vector2 _cloudOffset;                 // the cloud-shadow sheet's wind drift (world blocks)
        private Texture2D _cloudNoise;
        private float _indoor; // smoothed ship-interior fill (0 outside → 1 aboard)
        private float _stationDim = 1f; // #1869: smoothed station deck light (1 by day → StationNightFloor at night)

        // Cozy package (#2802): the home blend (#2813, eased over ~1.5 s so stepping in is felt), the place factor the
        // cosiness slider is scaled by (#2820) and the cabin's evening share (#2815).
        private static readonly int IndoorColorId = Shader.PropertyToID("_Sc_IndoorColor");
        private static readonly int GlowPulseId = Shader.PropertyToID("_Sc_GlowPulse");
        private float _homeBlend;
        private float _cozyPlace = 0.5f;
        private float _evening;
        private string _placeBiome;
        private float _placeCosiness = 0.5f;

        /// <summary>Glowing plants and mushrooms breathe slowly (#2822); off with Reduce flashes. Wired from WorldRig.</summary>
        public bool GlowPulseEnabled = true;

        /// <summary>The cabin fill by day (warm white) and in the evening (amber, ~2700 K) — sRGB, converted on upload.</summary>
        private static readonly Color HomeFillDay = new Color(1f, 0.96f, 0.9f);
        private static readonly Color HomeFillEvening = new Color(1f, 0.76f, 0.5f);

        /// <summary>How far a station's deck light drops at station night (#1869) — dim, never dark: the corridors
        /// stay walkable, the strip lights still glow.</summary>
        private const float StationNightFloor = 0.45f;

        /// <summary>Twilight band half-width in sun-height units: dusk/dawn is active while the sun is within this
        /// of the horizon (|sin(sunAngle)| ≤ band). 0.34 ≈ the sun within ~20° of the horizon — a civil+nautical
        /// dusk wide enough to read as a real sunset. Larger = a longer, softer golden hour.</summary>
        private const float TwilightBand = 0.34f;

        private Light _sun;
        private Transform _sunDisc;     // visible glowing sun billboard in the sky
        private Material _sunDiscMat;
        private Transform _sunRays;     // additive god-ray streaks (depth-tested → occluded by terrain → shafts)
        private Material _sunRaysMat;
        private float _time;        // local 0..1 day fraction
        private float _dayLength = 600f;
        private bool _haveEnv;

        /// <summary>The smoothed local day fraction 0..1 the sky is lit by (advanced every frame, re-synced to the server's
        /// heartbeat) — the cloud deck's day tint reads it instead of the raw 5 s steps (#3202).</summary>
        public float DayFraction => _time;

        private void Awake()
        {
            var go = new GameObject("Sun");
            go.transform.SetParent(transform, false);
            _sun = go.AddComponent<Light>();
            _sun.type = LightType.Directional;
            // Real-time sun shadows under URP (the headline of the migration); Built-in RP stays shadowless
            // (its custom block shader doesn't receive shadow maps, so they'd cost without showing).
            bool urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
            _sun.shadows = urp ? LightShadows.Soft : LightShadows.None;
            _sun.shadowStrength = 0.7f; // soft, not pitch-black shadows

            BuildSunDisc();
            BuildSunRays();
            _cloudNoise = BuildCloudNoise();
            Shader.SetGlobalTexture(CloudNoiseId, _cloudNoise);
        }

        private void OnEnable() => UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnBeginCamera;

        /// <summary>Per-camera haze (#2393): the world cameras get the live haze and cloud shadows, every other camera
        /// (previews, menus) renders without them — set right before each camera draws.</summary>
        private void OnBeginCamera(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera cam)
        {
            bool world = cam == Camera || WorldCameras.Contains(cam);
            Shader.SetGlobalVector(FogId, world ? _fogVector : FogOff);
            Shader.SetGlobalVector(CloudShadowId, world ? _cloudShadowVector : Vector4.zero);
            Shader.SetGlobalFloat(ModelLightId, world ? _modelLight : 1f); // #3265: a preview keeps the studio light
        }

        private void SetFog(Vector4 fog)
        {
            _fogVector = fog;
            Shader.SetGlobalVector(FogId, fog);
        }

        private void SetCloudShadow(Vector4 shadow)
        {
            _cloudShadowVector = shadow;
            Shader.SetGlobalVector(CloudShadowId, shadow);
        }

        // BuildCloudNoise lives in Sky.Mist.cs (#3271: the texture gained two even channels for the wisps and the cloud band).

        private void BuildSunRays()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "SunRays";
            var col = quad.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            var shader = Shader.Find("BlocksBeyondTheStars/SunRays");
            if (shader == null)
            {
                Destroy(quad); // no shader → skip god-rays (the disc still shows)
                return;
            }

            _sunRaysMat = new Material(shader) { mainTexture = GenerateRayTexture() };
            _sunRaysMat.SetColor(ColorId, Color.white);

            var mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _sunRaysMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            _sunRays = quad.transform;
            _sunRays.SetParent(transform, false);
            _sunRays.gameObject.SetActive(false);
        }

        /// <summary>A radial god-ray texture: soft bright spokes fanning out from the centre, fading outward — the
        /// "shafts" that get occluded by foreground terrain via the depth-tested SunRays billboard. Shared with the
        /// menu attract scene via <see cref="SkyVisuals"/>.</summary>
        private static Texture2D GenerateRayTexture() => SkyVisuals.RayTexture();

        private void BuildSunDisc()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "SunDisc";
            var col = quad.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            var shader = Shader.Find("BlocksBeyondTheStars/SunGlow");
            _sunDiscMat = new Material(shader != null ? shader : Shader.Find("Unlit/Color"))
            {
                mainTexture = GenerateGlowTexture(),
            };
            _sunDiscMat.SetColor(ColorId, Color.white);

            var mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _sunDiscMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            _sunDisc = quad.transform;
            _sunDisc.SetParent(transform, false);
            _sunDisc.gameObject.SetActive(false);
        }

        private static Texture2D GenerateGlowTexture() => SkyVisuals.GlowTexture();

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            ApplySunShadowQuality(); // #3054: High (desktop) shadows soften with the High PCF kernel

            // The space view manages its own dark background + body lighting. Don't bleed the planet's
            // day/night tint or the biome colour-grade into it — that turned the whole space view reddish.
            if (Game.SpaceViewActive)
            {
                Shader.SetGlobalColor(LightId, new Color(1f, 1f, 1f, 1f));   // neutral, full-bright
                // #2157: light the hull from the system's star (set by the flight view), not from the direction the last
                // planet's sun happened to have when the player took off.
                if (SpaceSunDir.sqrMagnitude > 1e-4f)
                {
                    Shader.SetGlobalVector(SunDirId, SpaceSunDir.normalized);
                }

                _homeBlend = 0f; // the flight view is the void outside, never the cabin (#2813)
                UrpScenePost.Instance?.SetCozyContext(0.4f, 0f, 0f);
                UrpScenePost.Instance?.ApplyGrade(Color.white, 1f, 1f);        // colour grade off on the URP volume
                UrpScenePost.Instance?.SetMoodLut(null);                       // …and drop the biome mood LUT in space
                Headlamp.AllOff(); // #3229: no suit lamp (own or another player's) in the flight view
                ResetDarkGlobals(); // #3265: the hull in the flight view keeps the studio light
                Shader.SetGlobalFloat(IndoorId, 0f);
                Shader.SetGlobalColor(IndoorColorId, Color.white);
                Shader.SetGlobalColor(FloraTintId, new Color(0f, 0f, 0f, 0f)); // no planet flora tint in space
                SetFog(FogOff); // distance haze off in space
                SetCloudShadow(Vector4.zero);
                ClearMist(); // #3271/#3272: no mist, no cloud band, Unity's legacy fog off (Sky.Mist.cs)
                if (_sunDisc != null)
                {
                    _sunDisc.gameObject.SetActive(false);
                }

                if (_sunRays != null)
                {
                    _sunRays.gameObject.SetActive(false);
                }

                return;
            }

            var env = Game.Environment;
            if (env != null)
            {
                // Local time-of-day: the server's global day fraction shifted by the player's longitude
                // (world X), so the planet has a day/night terminator across its surface.
                float localTime = Game.LocalTimeOfDay;
                if (!_haveEnv)
                {
                    _time = localTime;
                    _haveEnv = true;
                }

                _dayLength = Mathf.Max(10f, env.DayLengthSeconds);

                // Capture mode (marketing screenshots): snap straight to the pinned time so the shot is taken at
                // exactly that time of day instead of drifting toward it over a couple of seconds.
                if (Game.CaptureEnvActive)
                {
                    _time = localTime;
                }
                else
                {
                    // Re-sync toward the local time (server broadcasts + the player's longitude); advance locally.
                    _time = Mathf.LerpAngle(_time * 360f, localTime * 360f, 0.02f) / 360f;
                }
            }

            // World time, not frame time (#908): this local advance is what kept the sun crawling — and night
            // falling — behind the pause menu, long after the server had stopped sending a new TimeOfDay.
            float worldDt = Game != null ? Game.WorldDeltaTime : Time.deltaTime;
            _time = Mathf.Repeat(_time + worldDt / _dayLength, 1f);

            // #2394: the cloud-shadow sheet drifts with the wind, like the cloud billboards (a calm day still creeps).
            // #2397: the same wind drives the leaf sway (direction × strength, plus its own world-time clock).
            if (Game != null)
            {
                Vector3 wind = Game.WindVector;
                float speed = 0.8f + 4f * Game.WindSpeed;
                Vector2 dir = wind.sqrMagnitude > 1e-6f ? new Vector2(wind.x, wind.z).normalized : new Vector2(0.7f, 0.7f);
                // #3272: the sheet is sampled at (position + offset) / 96, so the offset runs AGAINST the wind for the shadows
                // (and the cloud band's holes, which drift with the same offset) to travel downwind like the grit and the leaves.
                _cloudOffset -= dir * (speed * worldDt);
                _windTime += worldDt;
                TickMistDrift(dir, worldDt); // #3271: the wisps drift with the wind (Sky.Mist.cs)
                bool sway = WindSwayEnabled && !(Game.Environment?.SpaceSky ?? false);
                Shader.SetGlobalVector(WindId, new Vector4(dir.x * Game.WindSpeed, _windTime, dir.y * Game.WindSpeed, sway ? 1f : 0f));
                TickWeatherTraces(worldDt);
                TickSeasonLook(worldDt); // #3042: autumn leaves, winter paleness, spring bloom (Sky.Seasons.cs)
            }

            // #3202: the SMOOTHED intensity (#900) — the raw value steps when a front edge passes or a heartbeat lands, and
            // it drives the block light dim, the overcast sky, the fog distance and the cloud-shadow cover below.
            float intensity = env != null ? Game.WeatherIntensity : 0f;
            // #3075: in flight the light is the star of the system flown in — the environment describes the world last landed on.
            Color sun = env != null ? Rgb(Game.SpaceViewActive ? FlightStar.ColorOf(Game.StarMap, env.SunColor) : env.SunColor)
                : new Color(1f, 0.96f, 0.88f); // match Space/Station fallback
            // Per-world daytime sky/atmosphere hue (server-seeded; blue → green → yellow → red). Only worlds with an
            // atmosphere actually show it (airless bodies go to the space sky below). Fallback = the old fixed blue.
            Color skyBase = env != null ? Rgb(env.SkyColor) : new Color(0.55f, 0.75f, 0.95f);
            // Boarded on an orbital station: it floats free in space, so show the space sky (black, no fog)
            // and treat it like a lit, life-supported interior — independent of the planet far below.
            bool boarded = !string.IsNullOrEmpty(Game.StationName);
            // Built above the atmosphere (item 10): the sky turns to space even on a normal planet.
            bool spaceSky = (env != null && env.SpaceSky) || boarded || Game.OnFootInSpace;

            // Planet flora re-tint hue for the block shader (a>0.5 = active). Off on stations (no planet flora).
            if (env != null && !boarded)
            {
                Color flora = Rgb(env.FloraTint);
                flora.a = 1f;
                Shader.SetGlobalColor(FloraTintId, ShaderColor.Srgb(flora));
                // #1758: the water colour of this world (mode 0 = the classic blue, so an older server changes nothing).
                Color water = Rgb(env.WaterTint);
                water.a = 1f;
                Shader.SetGlobalColor(WaterTintId, ShaderColor.Srgb(water));
                Shader.SetGlobalFloat(WaterModeId, env.WaterTintMode);
            }
            else
            {
                Shader.SetGlobalColor(FloraTintId, new Color(0f, 0f, 0f, 0f));
                Shader.SetGlobalFloat(WaterModeId, 0f);
            }

            ApplyLighting(_time, intensity, sun, skyBase, spaceSky, constantLight: boarded);
        }

        private void ApplyLighting(float time, float weatherIntensity, Color sunColor, Color skyBase, bool spaceSky, bool constantLight = false)
        {
            // Sun height: peaks at noon (0.5), lowest at midnight (0/1).
            float sunHeight = Mathf.Sin((time - 0.25f) * Mathf.PI * 2f);
            float day = Mathf.Clamp01(sunHeight * 0.5f + 0.5f);
            // Brighten the day curve for the LIGHT level only: an ease-out bias (1-(1-day)^2) lifts the long
            // dawn/dusk shoulders toward daylight while preserving true noon (1) and midnight (0), so the world
            // spends less of the cycle reading as dark. The sky colour + fog below still use the raw `day` so the
            // visible terminator stays tied to the real sun height.
            float dayLit = day * (2f - day);
            // Inside an orbital station there is no sun — it's lit by its own lighting. Since #1869 that lighting keeps
            // the station clock: the deck dims at station night (the crew is asleep) and comes back up by morning.
            // Emissive strip lights stay bright (they are the lamps); only the fill drops. Smoothed so a clock step
            // or boarding at night fades instead of snapping.
            float stationTarget = constantLight ? Mathf.Lerp(StationNightFloor, 1f, dayLit) : 1f;
            _stationDim = Mathf.MoveTowards(_stationDim, stationTarget, Time.deltaTime * 0.5f);
            float stationDim = constantLight ? _stationDim : 1f;
            float brightness = constantLight ? stationDim : Mathf.Lerp(0.35f, 1f, dayLit); // night floor → noon
            // Storms darken — but only to 0.78 now (was 0.65, #1457): the block light goes through the sRGB→linear
            // conversion below, so 0.65 landed in the shader as ~0.38 of noon, and on the overcast-only worlds
            // (swamp, ashen, fungal never clear) every daytime read as dusk. A grey day should still be a day.
            float weatherDim = constantLight ? 1f : Mathf.Lerp(1f, 0.78f, weatherIntensity);

            // Twilight (golden hour): peaks with the sun sitting on the horizon (sunHeight → 0) and fades out by
            // full day / deep night. Off in airless (space) skies — no atmosphere means no scattering, so there the
            // terminator IS a hard line (which also sets airless worlds apart). Because it's a function of the sun
            // HEIGHT, its real-time length scales with each planet's day length on its own: a long day gets a long,
            // lazy dusk, a short day a quick one — no per-planet tuning needed. Smoothstepped into a soft ramp so
            // there's no hard edge where the band begins.
            float twilight = spaceSky ? 0f : Mathf.Clamp01(1f - Mathf.Abs(sunHeight) / TwilightBand);
            twilight = twilight * twilight * (3f - 2f * twilight);

            // Star hue (the sun colour normalised to a pure hue, brightness removed) — reused for the daytime sky
            // tint and the dusk glow so a red star burns a redder sunset and a blue-white star a cooler one.
            float sunMax = Mathf.Max(sunColor.r, Mathf.Max(sunColor.g, sunColor.b));
            Color sunHue = sunMax > 0.001f ? new Color(sunColor.r / sunMax, sunColor.g / sunMax, sunColor.b / sunMax) : Color.white;

            // The star warms toward amber as it nears the horizon — the whole world (block light, sun disc, god-rays)
            // takes on the golden-hour cast, then cools back to its true colour high in the sky. Kept off on stations.
            Color warmSun = constantLight ? sunColor : Color.Lerp(sunColor, sunColor * new Color(1f, 0.72f, 0.5f), twilight * 0.7f);

            // #1610: the star colour should TINT the world, not dim it (Sky.StarLight.cs). Measured on the RAW star (not
            // warmSun) so the golden-hour warming still dims dusk as before.
            Color litSun = constantLight ? warmSun : warmSun * StarLift(sunColor);

            // Stations use a clean neutral interior light (not the system sun's tint).
            Color tint = constantLight ? new Color(0.95f, 0.96f, 1f) * stationDim : litSun * (brightness * weatherDim);

            // A lightning strike lights the WORLD for a moment (#900), not just the screen: the block light
            // global is pushed toward a cold white, so the whole landscape flares out of a dark storm.
            float bolt = constantLight ? 0f : Mathf.Clamp01(WeatherFx.LightningFlash);
            if (bolt > 0.001f)
            {
                tint = Color.Lerp(tint, new Color(1.15f, 1.18f, 1.30f), bolt * 0.8f);
            }

            tint.a = 1f; // marks the global as "set" for the shaders
            Shader.SetGlobalColor(LightId, ShaderColor.Srgb(tint));

            // Ship interior lighting: the block shader darkens indoors/underground by skylight occlusion
            // (so caves need a lamp). The ship is your home, though, so feed an "indoor fill" the shader
            // adds to skylight-occluded faces only — lighting the cabin (day or night) without touching the
            // sunlit outdoors seen through the windows. Smoothed so boarding/leaving fades.
            // Interior fill light: aboard your ship, or boarded on a station (its life-support lighting).
            bool litInterior = Game != null && (Game.Aboard || !string.IsNullOrEmpty(Game.StationName));

            // Cozy package (#2802): at home (aboard, or under a roof in your own base) the grade turns warm over ~1.5 s
            // (#2813), the cabin fill takes a warm white by day and amber in the evening (#2814/#2815), and the
            // cosiness slider is scaled by how cosy this place may get (#2820).
            bool home = Game != null && Game.AtHome;
            _homeBlend = Mathf.MoveTowards(_homeBlend, home ? 1f : 0f, Time.deltaTime / 1.5f);
            float eveningTarget = CabinEvening(sunHeight);
            _evening = Mathf.MoveTowards(_evening, eveningTarget, Time.deltaTime * 0.25f);
            _cozyPlace = Mathf.MoveTowards(_cozyPlace, CozyPlaceTarget(weatherIntensity), Time.deltaTime * 0.5f);
            UrpScenePost.Instance?.SetCozyContext(_cozyPlace, _homeBlend, _evening);

            // The evening amber is a little brighter than the day fill, so the cabin does not sink with the outdoor night.
            float indoorTarget = litInterior ? stationDim * (constantLight ? 1f : 1f + 0.2f * _evening) : 0f; // #1869: a station's fill follows its night
            _indoor = Mathf.MoveTowards(_indoor, indoorTarget, Time.deltaTime * 3f);
            float interiorFill = InteriorFill(litInterior, home); // #3265: + a gentle fill in the own base's rooms (Sky.Dark.cs)
            Shader.SetGlobalFloat(IndoorId, interiorFill);
            PublishDarkGlobals(ShaderColor.Srgb(tint), interiorFill);
            // A station keeps its clean neutral deck light; the ship and the home base get the warm fill.
            float warmFill = constantLight ? 0f : _homeBlend;
            Color fill = Color.Lerp(Color.white, Color.Lerp(HomeFillDay, HomeFillEvening, _evening), warmFill);
            fill.a = 1f;
            Shader.SetGlobalColor(IndoorColorId, ShaderColor.Srgb(fill));
            Shader.SetGlobalFloat(GlowPulseId, GlowPulseEnabled ? 1f : 0f);

            // Sky colour: drives both the camera background and the global the lit shader samples
            // for grazing-angle environment reflections (glossy/metallic blocks).
            Color sky;
            if (spaceSky)
            {
                // Airless body (asteroid): the sky stays space-black even on the surface.
                sky = new Color(0.01f, 0.01f, 0.03f);
            }
            else
            {
                // The per-world atmosphere hue (server-seeded; blue → green → yellow → red) is the daytime base,
                // washing toward overcast grey with weather. Each world's sky now reads distinct instead of a
                // single fixed blue.
                // #2063: the wash keeps a share of the world's own hue, so a storm sky stays recognisably THIS world's — a
                // green sky under a permanent thunderstorm (Toxica-Maxima) reads green-grey, not the same grey as everywhere.
                Color overcast = Color.Lerp(new Color(0.6f, 0.62f, 0.68f), skyBase, 0.35f);
                Color daySky = Color.Lerp(skyBase, overcast, weatherIntensity);

                // Tint the daytime sky toward the system star's hue, so a warm / red star gives a warmer sky and a
                // blue-white star a cooler one (B37). Kept moderate (0.4, #3125: was 0.3, 0.2 before #3077) so the per-world
                // atmosphere colour above reads through. The directional sun light + block diffuse already use the star colour.
                daySky = Color.Lerp(daySky, daySky * sunHue, 0.4f);

                // Night sky: a dark base nudged toward the world's atmosphere hue, so a green/red-skied world also
                // tints its night instead of always reading the same blue-black.
                Color nightSky = Color.Lerp(new Color(0.03f, 0.04f, 0.09f), skyBase * 0.12f, 0.5f);
                sky = Color.Lerp(nightSky, daySky, day);

                // Golden-hour horizon glow: a warm sunset amber (star-hue biased), washed out by heavy weather.
                // Blended over the day/night lerp so dusk and dawn read as a real colour change that lifts the sky
                // through the fast darkening at the terminator — turning the old snap-to-black into a sunset. The
                // fog below inherits this `sky`, so the far horizon warms with it too.
                Color glow = new Color(1f, 0.45f, 0.22f);
                glow = Color.Lerp(glow, glow * sunHue, 0.5f);
                sky = Color.Lerp(sky, glow, twilight * 0.6f * (1f - weatherIntensity));

                // #3270: a fog, dust or ion-storm episode tints the sky — and with it the haze — toward its colour (Sky.Mist.cs).
                sky = WeatherHazeSky(sky, weatherIntensity);
            }
            sky.a = 1f;
            // The shader global gets the linear value; the engine-managed consumers below (ambient, camera
            // background, fog) keep the sRGB-composed `sky` — Unity converts those itself.
            Shader.SetGlobalColor(SkyId, ShaderColor.Srgb(sky));
            Shader.SetGlobalColor(FogSkyId, ShaderColor.Srgb(sky)); // the haze's own copy (AtmosphereCommon)

            // #2406: looking toward a low sun, the haze warms to the star's golden-hour colour (in-scatter); strongest in
            // the twilight band, gone at night, washed out by heavy weather, and never in an airless sky.
            float scatter = spaceSky ? 0f
                : Mathf.Clamp01(sunHeight * 4f + 0.25f) * (0.35f + 0.45f * twilight) * (1f - weatherIntensity * 0.6f);
            Color scatterCol = ShaderColor.Srgb(Color.Lerp(warmSun, new Color(1f, 0.85f, 0.6f), 0.3f));
            Shader.SetGlobalVector(FogSunId, new Vector4(scatterCol.r, scatterCol.g, scatterCol.b, scatter));
            Shader.SetGlobalFloat(FlickerScaleId, FlickerScale);

            // Star-tinted flat ambient (B37 rest): the custom block shader ignores Unity's ambient, but
            // standard/Lit-shaded props (and URP's ambient term) pick it up — so even those follow the
            // system star + time of day instead of Unity's fixed grey.
            UnityEngine.RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            UnityEngine.RenderSettings.ambientLight = Color.Lerp(sky, tint, 0.35f);

            // Apply to the camera background (skip while the space view controls the camera).
            if (Camera != null && (Game == null || !Game.SpaceViewActive))
            {
                Camera.clearFlags = CameraClearFlags.SolidColor;
                Camera.backgroundColor = InCloudSky(sky); // #3272: standing in a cloud, the sky behind is cloud too
            }

            ApplyFog(sky, warmSun, weatherIntensity, day, spaceSky); // Sky.Mist.cs

            if (_sun != null)
            {
                _sun.color = litSun; // Lit-shaded props / creatures follow the same normalised light as the blocks
                _sun.intensity = brightness;
                _sun.transform.rotation = Quaternion.Euler(time * 360f - 90f, 160f, 0f);
                // The lit block shader reads the sun direction from this global (direction TO the sun).
                Shader.SetGlobalVector(SunDirId, -_sun.transform.forward);
                Shader.SetGlobalVector(FogSunDirId, -_sun.transform.forward);
            }

            UpdateSunDisc(sunHeight, warmSun, spaceSky);
            SetGrade(Game?.Environment?.Biome, sunColor);
        }

        /// <summary>Drives the post-FX colour grade (the per-system/biome "mood LUT"): a biome tint +
        /// saturation/contrast, folded with the star system's sun colour as a subtle hue shift. Applied via the
        /// URP <c>UrpScenePost</c> volume (ColorAdjustments + mood LUT); only visible when tonemapping/post is on.</summary>
        private string _gradeBiome;
        private (Color tint, float sat, float contrast) _grade = (Color.white, 1f, 1.03f);

        private void SetGrade(string biome, Color sunColor)
        {
            // #1516: GradeFor lower-cases the biome key — once per biome change, not once per frame.
            if (!ReferenceEquals(biome, _gradeBiome) && biome != _gradeBiome)
            {
                _gradeBiome = biome;
                _grade = GradeFor(biome);
            }

            var (tint, sat, contrast) = _grade;
            // #3004: a festival's warmth and saturation fold into the grade — a warm wash for the lights festivals, a dusky
            // one for Halloween, more colour for Holi. Cosmetic and cheap: two lerps on the cached biome grade.
            var festival = FestivalMood.Effects(Game);
            if (festival != null)
            {
                tint = Color.Lerp(tint, festival.Warmth >= 0f ? FestivalMood.WarmTint : FestivalMood.CoolTint, Mathf.Clamp01(Mathf.Abs(festival.Warmth)) * 0.6f);
                sat *= Mathf.Clamp(festival.Saturation, 0.5f, 1.6f);
            }

            (tint, sat) = SeasonGrade(tint, sat); // #3042: summer and autumn warm, winter cool (Sky.Seasons.cs)

            float m = Mathf.Max(sunColor.r, Mathf.Max(sunColor.g, sunColor.b));
            Color norm = m > 0.001f ? new Color(sunColor.r / m, sunColor.g / m, sunColor.b / m) : Color.white;
            // The star's hue folds into the grade so each system visibly tints its worlds (0.65 keeps it an
            // atmospheric wash — raised from 0.25, then 0.4 (#3077), then 0.55 (#3125), so the per-system sun colour reads).
            Color blended = tint * Color.Lerp(Color.white, norm, 0.65f);
            blended.a = 0.7f; // grade strength
            // #1612: the old _Sc_GradeTint / _Sc_GradeParams shader globals are gone — no shader read them since
            // the grade moved onto the URP volume below.
            // ApplyGrade keeps the sRGB value: URP's ColorAdjustments colorFilter converts internally.
            UrpScenePost.Instance?.ApplyGrade(blended, sat, contrast); // URP colour grade (ColorAdjustments)
            UrpScenePost.Instance?.SetMoodLut(biome); // WS4: layer the per-biome cinematic mood LUT on top
        }

        private static float Smooth01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        /// <summary>#2815: how far into the evening the cabin light is (0 day … 1 night). Landed, the planet's own sun
        /// decides, so the cabin matches the view through the window; in the ship-interior world out in space there is no
        /// sun to go by, so the player's real clock does (warming 18–20 h, cooling back 6–8 h).</summary>
        private float CabinEvening(float sunHeight)
        {
            if (Game != null && Game.LoadingPlanetType == "ship_interior")
            {
                var now = System.DateTime.Now;
                float hour = now.Hour + now.Minute / 60f;
                return hour >= 12f ? Smooth01((hour - 18f) / 2f) : 1f - Smooth01((hour - 6f) / 2f);
            }

            return 1f - Smooth01((sunHeight + 0.05f) / 0.35f);
        }

        /// <summary>#2820: how cosy the current place may get, 0..1 — the cosiness slider is multiplied by it, so the home
        /// stands out against the outside and each world keeps its own colour: home 1, a lit camp or village 0.8, gentle
        /// worlds 0.6, a station 0.5, harsh worlds less, a storm halves it, and danger (a hostile close by, Valuma's
        /// watching) takes it away.</summary>
        private float CozyPlaceTarget(float weatherIntensity)
        {
            if (Game == null)
            {
                return 0.5f;
            }

            if (Game.AtHome)
            {
                return 1f;
            }

            if (Game.Uneasy || Game.NearestHostileSqr < 24f * 24f)
            {
                return 0f;
            }

            if (!string.IsNullOrEmpty(Game.StationName))
            {
                return 0.5f;
            }

            // Cached per biome like the grade (#1516): the lookup lower-cases the key, which allocates.
            string biome = Game.Environment?.Biome;
            if (!ReferenceEquals(biome, _placeBiome) && biome != _placeBiome)
            {
                _placeBiome = biome;
                _placeCosiness = PlaceCosinessFor(biome);
            }

            float place = _placeCosiness;
            bool lampLit = Probe != null && Probe.LampLight > 0.3f;
            if (lampLit)
            {
                place = Mathf.Max(place, 0.8f); // a campfire, a lantern-lit hut, a village street
            }

            place *= DarkPlaceFactor(lampLit); // #3265: no cosy lift in a dark cave (Sky.Dark.cs)

            string weather = Game.Environment?.Weather ?? string.Empty;
            bool rough = weather is "storm" or "gale" or "blizzard" or "ion_storm" or "toxic_storm" or "acid_rain" or "meteor_shower";
            return rough ? place * Mathf.Lerp(1f, 0.5f, Mathf.Clamp01(weatherIntensity)) : place;
        }

        /// <summary>#2820: a world's own cosiness — gentle green and blue worlds take the slider well, the cold, hot and
        /// toxic ones keep most of their bite.</summary>
        private static float PlaceCosinessFor(string biome)
        {
            switch ((biome ?? string.Empty).ToLowerInvariant())
            {
                case "jungle": case "forest": case "meadowlands": case "flower_fields": case "boreal": case "savanna":
                case "ocean": case "coral_sea": case "rainbow_sea": case "river_lowlands": case "archipelago": case "karst":
                case "highland": case "skylands": case "gamer_hills": case "varied":
                    return 0.6f;
                case "ice": case "frozen": case "tundra": case "glacier": case "icecap": case "frozen_ocean": case "titas":
                case "asteroid": case "asteroid_metallic": case "asteroid_icy": case "asteroid_carbon": case "asteroid_crystal":
                    return 0.4f;
                case "lava": case "volcanic": case "arena_nigra": case "ashen": case "ashen_ocean": case "toxic_world":
                case "toxica_maxima": case "corrupted": case "scrapyard": case "bone_desert": case "valuma":
                    return 0.3f;
                case "gas_giant":
                    return 0.25f;
                default:
                    return 0.5f; // deserts, swamps, fungal and crystal worlds, the odd shapes
            }
        }

        /// <summary>Per-biome colour-grade mood: (tint multiply, saturation, contrast).</summary>
        private static (Color tint, float sat, float contrast) GradeFor(string biome)
        {
            switch ((biome ?? string.Empty).ToLowerInvariant())
            {
                case "jungle": case "forest": return (new Color(0.98f, 1.05f, 0.96f), 1.12f, 1.05f);
                case "desert": return (new Color(1.07f, 1.00f, 0.90f), 0.95f, 1.12f);
                case "ice": case "frozen": return (new Color(0.94f, 1.00f, 1.09f), 0.90f, 1.06f);
                case "lava": case "volcanic": case "arena_nigra": return (new Color(1.10f, 0.95f, 0.86f), 1.05f, 1.14f); // #2078: the black dunes take the volcanic grade
                case "gas_giant": return (new Color(1.04f, 0.96f, 1.02f), 0.92f, 1.08f); // #2112: a cold amber-violet dusk over the gas
                case "swamp": return (new Color(0.97f, 1.03f, 0.95f), 0.85f, 1.03f);
                case "crystal": return (new Color(1.04f, 0.97f, 1.09f), 1.10f, 1.05f);
                default: return (new Color(1f, 1f, 1f), 1.00f, 1.03f);
            }
        }

        /// <summary>#2398: the traces the weather leaves. Wet rises within ~20 s of rain and dries over minutes (faster
        /// by day); snow builds over a minute of snowfall, stays while it is cold and melts over minutes above
        /// freezing. Client-side only, derived from the environment the server already sends; a capture snaps them.</summary>
        private void TickWeatherTraces(float dt)
        {
            var env = Game?.Environment;
            bool on = WetSurfacesEnabled && env != null && !env.SpaceSky && !Game.SpaceViewActive;
            if (!on)
            {
                _wet = Mathf.MoveTowards(_wet, 0f, dt);
                _snow = Mathf.MoveTowards(_snow, 0f, dt);
                Shader.SetGlobalVector(SurfaceId, new Vector4(0f, 0f, 0f, 0f));
                return;
            }

            string precip = env.Precipitation ?? "none";
            float strength = Mathf.Clamp01(Game.WeatherIntensity);
            bool raining = precip is "rain" or "drizzle" or "sleet" or "acid";
            bool snowing = precip is "snow" or "sleet" or "hail";
            float day = Mathf.Clamp01(Mathf.Sin((_time - 0.25f) * Mathf.PI * 2f) * 0.5f + 0.5f);
            bool snap = Game.CaptureEnvActive;

            float wetTarget = raining ? Mathf.Clamp01(0.5f + strength) : 0f;
            float wetRate = raining ? 1f / 20f : (1f / 180f) * (0.5f + day);
            _wet = snap ? wetTarget : Mathf.MoveTowards(_wet, wetTarget, dt * wetRate);

            float temp = env.Temperature;
            float snowTarget = snowing ? Mathf.Clamp01(0.4f + strength) : (temp < 0.5f ? _snow : 0f);
            if (FestivalMood.Effects(Game)?.SnowCap == true)
            {
                snowTarget = Mathf.Max(snowTarget, 0.55f); // #3004: the winter festivals dust the roofs even on a warm world
            }

            if (SeasonLook.Frost(env.Season, temp))
            {
                snowTarget = Mathf.Max(snowTarget, SeasonLook.FrostAmount); // #3042: a cold winter's frost on the tops
            }

            float snowRate = snowing ? 1f / 60f : 1f / 240f;
            _snow = snap ? snowTarget : Mathf.MoveTowards(_snow, snowTarget, dt * snowRate);

            Shader.SetGlobalVector(SurfaceId, new Vector4(_wet, _snow, 1f, 0f));
        }

        /// <summary>Places the glowing sun billboard in the sky in the sun direction, tinted by the
        /// system sun colour and faded near/below the horizon. Hidden during the space view.</summary>
        private void UpdateSunDisc(float sunHeight, Color sunColor, bool spaceSky)
        {
            if (_sunDisc == null || _sun == null)
            {
                return;
            }

            bool active = Camera != null && sunHeight > -0.12f && (Game == null || !Game.SpaceViewActive);
            _sunDisc.gameObject.SetActive(active);
            if (!active)
            {
                if (_sunRays != null)
                {
                    _sunRays.gameObject.SetActive(false);
                }

                return;
            }

            Vector3 camPos = Camera.transform.position;
            Vector3 dir = (-_sun.transform.forward).normalized; // direction TO the sun
            // Kept comfortably inside the frustum (the disc isn't depth-tested, so distance only sets
            // its placement + angular size); avoids clipping against the camera's far plane.
            float dist = Mathf.Clamp(Camera.farClipPlane * 0.5f, 60f, 400f);
            _sunDisc.position = camPos + dir * dist;
            _sunDisc.rotation = Quaternion.LookRotation(camPos - _sunDisc.position); // billboard (Cull Off)
            float size = dist * (spaceSky ? 0.26f : 0.20f);
            _sunDisc.localScale = new Vector3(size, size, size);

            // Fade in as the sun rises; a touch brighter against an airless black sky.
            float a = Mathf.Clamp01(sunHeight * 1.6f + 0.3f) * (spaceSky ? 1.25f : 1f);
            Color c = sunColor;
            c.a = Mathf.Clamp01(a) * (1f - 0.85f * _inCloud); // #3272: inside a cloud the sun is only a glow
            _sunDiscMat.SetColor(ColorId, ShaderColor.Srgb(c));

            // God-ray shafts: a big additive ray fan at the sun, depth-tested (SunRays shader) so foreground
            // terrain occludes it → shafts above ridgelines/canopies. Atmospheric scattering, so only with air
            // (off in space/airless), stronger when the sun is lowish and the air is thick. Purely additive.
            if (_sunRays != null)
            {
                bool raysOn = !spaceSky && FogEnabled;
                _sunRays.gameObject.SetActive(raysOn);
                if (raysOn)
                {
                    _sunRays.position = camPos + dir * dist;
                    _sunRays.rotation = Quaternion.LookRotation(camPos - _sunRays.position);
                    float rsize = dist * 0.9f; // a wide fan around the disc
                    _sunRays.localScale = new Vector3(rsize, rsize, rsize);

                    float airDensity = Game?.Environment?.AtmosphereDensity ?? 0.4f;
                    float lowBoost = Mathf.Lerp(1.3f, 0.7f, Mathf.Clamp01(sunHeight)); // longer shafts near the horizon
                    float strength = Mathf.Clamp01(sunHeight * 2.5f) * lowBoost * Mathf.Lerp(0.35f, 0.9f, Mathf.Clamp01(airDensity));
                    Color rc = sunColor;
                    rc.a = Mathf.Clamp01(strength * 0.14f) * (1f - _inCloud); // overall additive ray brightness (subtle); none in a cloud (#3272)
                    _sunRaysMat.SetColor(ColorId, ShaderColor.Srgb(rc));
                }
            }
        }

        private void OnDisable()
        {
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            // Clear the tint so other scenes (menu) aren't affected.
            Shader.SetGlobalColor(LightId, new Color(1f, 1f, 1f, 0f));
            Headlamp.AllOff(); // every headlamp off (#3229: the other players' too)
            UrpScenePost.Instance?.SetMoodLut(null); // drop the biome mood LUT (menu/space)
            Shader.SetGlobalFloat(IndoorId, 0f); // interior fill off (menu/space)
            ResetDarkGlobals(); // #3265: the menu's models keep the studio light
            Shader.SetGlobalColor(FloraTintId, new Color(0f, 0f, 0f, 0f)); // flora tint off (menu/space)
            SetFog(FogOff); // distance haze off (menu/space)
            SetCloudShadow(Vector4.zero);
            Shader.SetGlobalFloat(FlickerScaleId, 0f);
            Shader.SetGlobalVector(WindId, Vector4.zero); // no sway in the menu / space view
            Shader.SetGlobalVector(SurfaceId, Vector4.zero); // dry ground in the menu / space view
            ClearMist(); // #3271/#3272: no mist or cloud band, and Unity's legacy fog off — nothing leaks into the menu / space view
            if (_sunDisc != null)
            {
                _sunDisc.gameObject.SetActive(false);
            }
        }

        private static Color Rgb(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
