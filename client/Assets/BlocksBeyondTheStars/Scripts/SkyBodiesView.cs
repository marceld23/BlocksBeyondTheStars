// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Orbital bodies in the planet sky: the system's OTHER landable bodies — moons, neighbour planets and
    /// landable asteroids (never stations) — hang visibly in the sky from the surface. Each body follows its
    /// OWN deterministic sky cycle like the sun does: an orbit speed (fraction/multiple of the local day), a
    /// phase and a tilted path, all hashed from the system + current planet + body — so every planet has its
    /// own unique sky choreography (a slow huge moon here, two fast crossing asteroids there), stable across
    /// sessions. Bodies rise and set, are tinted by their planet type, sized by their real walkable size, and
    /// read a touch brighter at night. Pure client ambience driven by the star map; terrain occludes them.
    /// </summary>
    public sealed class SkyBodiesView : MonoBehaviour
    {
        public GameBootstrap Game;

        /// <summary>Reference seconds per "system day" — MUST match the server's GameServerWeather.SystemDaySeconds
        /// so the locally-advanced orbital clock stays in lockstep with the authoritative SystemTimeDays.</summary>
        private const float SystemDaySeconds = 600f;

        private sealed class SkyBody
        {
            public GameObject Go;
            public Material Mat;
            public Color Tint;
            public Material RingMat;      // planetary ring disc (#596), null for ring-less bodies
            public Color RingTint;        // the ring's seeded base colour (re-tinted per frame for daylight)
            public float Phase;          // 0..1 initial rise-time offset (hashed) — spreads bodies across the day
            public float OrbitPeriodDays; // signed synodic period; the body drifts relative to the sun by this →
                                          // its sun-lit phase waxes/wanes once per |period| (0 = no drift)
            public float BaseAz;         // compass bearing from the body's REAL relative system position (deg)
            public float Peak;           // max elevation of its daily arc (deg) — never the zenith, so paths spread
            public float Sweep;          // azimuth travel across the visible arc (deg) — an east→up→west drift
            public float Size;           // world-space sphere scale at the fixed sky distance
        }

        private static readonly int SunDirId = Shader.PropertyToID("_Sc_SunDir");
        private static readonly int PhaseSunDirId = Shader.PropertyToID("_PhaseSunDir");
        private static readonly int DayLightId = Shader.PropertyToID("_DayLight");

        private readonly List<SkyBody> _bodies = new();
        private string _builtFor = "\0"; // ActiveLocationId the current set was built for
        private float _requestTimer;
        private bool _subscribed;
        private float _tod = -1f; // continuous local day clock (the env's TimeOfDay only steps per update)
        private double _sysDays = -1.0; // continuous local copy of the authoritative SystemTimeDays orbital clock

        private void Update()
        {
            if (Game == null || Game.Network == null)
            {
                return;
            }

            if (!_subscribed)
            {
                // Travelling to another body: the cached star map still carries the OLD ActiveLocationId, so
                // pin the built-for id to it (suppressing a stale rebuild), clear the sky and ask for a fresh
                // map — the new id then triggers the proper rebuild.
                Game.Network.WorldResetReceived += _ =>
                {
                    Clear();
                    _builtFor = Game.StarMap?.ActiveLocationId ?? "\0";
                    _requestTimer = 1.5f;
                    _tod = -1f; // new world, new day clock — resync from its first env update
                    // (_sysDays is server-wide + monotonic, so it stays — the orbits keep their continuity.)
                };
                _subscribed = true;
            }

            var map = Game.StarMap;
            if (map == null)
            {
                // The map normally arrives when the player first opens the cockpit chart — request it once
                // (retrying slowly) so the sky works straight after spawn too.
                _requestTimer -= Time.deltaTime;
                if (_requestTimer <= 0f)
                {
                    _requestTimer = 5f;
                    Game.Network.SendRequestStarMap();
                }

                return;
            }

            if (map.ActiveLocationId != _builtFor)
            {
                Rebuild(map);
            }

            bool show = !Game.SpaceViewActive && Game.Environment != null && _bodies.Count > 0;

            // Continuous day clock: the server's TimeOfDay only arrives in periodic steps — the slow sun hides
            // that, but our faster bodies would visibly JUMP between updates. Advance a local clock with real
            // time (the world's day length) and softly resync it to the authoritative value (wrap-aware).
            float target = Game.LocalTimeOfDay;
            if (_tod < 0f)
            {
                _tod = target;
            }

            float dayLen = Mathf.Max(30f, Game.Environment?.DayLengthSeconds > 1 ? (float)Game.Environment.DayLengthSeconds : 600f);
            _tod = Mathf.Repeat(_tod + Time.deltaTime / dayLen, 1f);
            float err = Mathf.DeltaAngle(_tod * 360f, target * 360f) / 360f;
            _tod = Mathf.Repeat(_tod + err * Mathf.Min(1f, Time.deltaTime * 0.4f), 1f);

            // The monotonic orbital clock: advance locally (fixed reference day) and softly resync to the
            // authoritative SystemTimeDays so the slow phase drift is a glide, not 5-second broadcast steps.
            double sysTarget = Game.Environment != null ? Game.Environment.SystemTimeDays : -1.0;
            if (_sysDays < 0.0)
            {
                _sysDays = sysTarget >= 0.0 ? sysTarget : 0.0;
            }

            _sysDays += Time.deltaTime / SystemDaySeconds;
            if (sysTarget >= 0.0)
            {
                _sysDays += (sysTarget - _sysDays) * Mathf.Min(1f, Time.deltaTime * 0.4f);
            }

            float day = Mathf.Clamp01(Mathf.Sin(_tod * Mathf.PI));
            // Direction TO the sun in the sky (set by Sky.cs each frame); lighting the body sphere with it makes
            // the correct phase + terminator emerge, with the bright limb pointing at the sun.
            Vector3 sunDir = Shader.GetGlobalVector(SunDirId);
            if (sunDir.sqrMagnitude < 1e-4f)
            {
                sunDir = Vector3.up; // before the first lighting update — harmless fallback
            }

            var cam = Camera.main;
            Color skyCol = SkyBase();

            foreach (var b in _bodies)
            {
                if (!show || cam == null)
                {
                    if (b.Go.activeSelf)
                    {
                        b.Go.SetActive(false);
                    }

                    continue;
                }

                // The body's own sky cycle: a tilted arc, NOT a great circle through the zenith (which made
                // every body climb straight overhead and stack vertically). Elevation rides a sine — up over
                // the first half of the cycle, below the horizon (hidden) the second — capped at the body's
                // own Peak so it never reaches the zenith. Azimuth drifts across the sky during the visible
                // arc, centred on the body's REAL bearing at its peak. Uses the smoothed continuous clock so
                // motion is a glide, not server-update steps.
                // Crosses ~once per local day (the planet's rotation), but its rise time drifts slowly by its
                // orbital rate — so it rises a little earlier/later each day and, crucially, its angle to the
                // sun sweeps through a full cycle once per |OrbitPeriodDays|, driving the visible phase change.
                float drift = b.OrbitPeriodDays != 0f ? (float)(_sysDays / b.OrbitPeriodDays) : 0f;
                float c = Mathf.Repeat(_tod + b.Phase + drift, 1f);
                float el = b.Peak * Mathf.Sin(c * Mathf.PI * 2f);
                float az = b.BaseAz + (c - 0.25f) * b.Sweep;
                float elRad = el * Mathf.Deg2Rad, azRad = az * Mathf.Deg2Rad;
                float cosEl = Mathf.Cos(elRad);
                var dir = new Vector3(cosEl * Mathf.Sin(azRad), Mathf.Sin(elRad), cosEl * Mathf.Cos(azRad));

                bool up = dir.y > -0.04f;
                if (b.Go.activeSelf != up)
                {
                    b.Go.SetActive(up);
                }

                if (!up)
                {
                    continue;
                }

                float dist = Mathf.Clamp(cam.farClipPlane * 0.45f, 60f, 460f);
                b.Go.transform.position = cam.transform.position + dir * dist;
                b.Go.transform.localScale = Vector3.one * (b.Size * dist / 460f);

                // The phase shader does the sun-lit shading; feed it the current sky sun direction. Brightness is
                // now just an overall dim: bodies dominate the dark night sky and wash out toward day, fading at
                // the horizon. (The lit/unlit split across the disc is the phase, handled in the shader.)
                b.Mat.SetVector(PhaseSunDirId, sunDir);
                // Daytime the sun and every visible body share the upper sky, so the pure sun-lit phase only shows
                // the body's unlit far side ("new moon") — a black silhouette against the bright day sky. Ramp the
                // shader from its true phase (night) toward a fully front-lit disc (day) so bodies stay a visible
                // feature; the crescent/phase still reads at twilight and night. `day` is 0 at night → 1 at noon.
                b.Mat.SetFloat(DayLightId, day);
                float horizon = Mathf.Clamp01((dir.y + 0.04f) / 0.12f);
                // Overall brightness: a night boost so bodies dominate the dark sky, full strength by day — the
                // old 0.7 noon dim left the disc 3-4x darker than the ACES-tonemapped daytime sky, which still
                // read as a silhouette (#585). The shader's sky-colour atmosphere wash now balances the day look.
                b.Mat.color = ShaderColor.Srgb(b.Tint * Mathf.Lerp(1.25f, 1f, day) * horizon);

                // The ring shader is unlit, so it needs the disc's daytime treatment CPU-side (#585): by day
                // the ring washes toward the sky colour and fades (the real look — pale, translucent bands),
                // at night it keeps its seeded ice tint with the same night boost the disc gets.
                if (b.RingMat != null)
                {
                    var ringCol = Color.Lerp(b.RingTint * Mathf.Lerp(1.25f, 1f, day), skyCol * 1.05f, day * 0.55f);
                    ringCol.a = Mathf.Lerp(0.62f, 0.38f, day) * horizon;
                    b.RingMat.color = ShaderColor.Srgb(ringCol);
                }
            }
        }

        /// <summary>(Re)builds the sky set for the current body's system: every OTHER landable body — planets,
        /// moons, landable asteroids — with its own hashed sky-cycle parameters. Stations are never shown.</summary>
        private void Clear()
        {
            foreach (var b in _bodies)
            {
                if (b.Go != null)
                {
                    Destroy(b.Go);
                }
            }

            _bodies.Clear();
        }

        private void Rebuild(StarMapData map)
        {
            _builtFor = map.ActiveLocationId;
            Clear();

            // Find the system the player is currently in.
            NetStarSystem system = null;
            foreach (var s in map.Systems)
            {
                foreach (var body in s.Bodies)
                {
                    if (body.Id == map.ActiveLocationId)
                    {
                        system = s;
                        break;
                    }
                }

                if (system != null)
                {
                    break;
                }
            }

            if (system == null)
            {
                return;
            }

            // The body the player stands on — its system position anchors the perspective: apparent sizes
            // follow real system-space distance (size ≈ radius / distance), so from a MOON its parent planet
            // looms huge overhead, from an asteroid nearby bodies read large, and from a planet the rest of
            // the system stays suitably small.
            NetBody current = null;
            foreach (var body in system.Bodies)
            {
                if (body.Id == map.ActiveLocationId)
                {
                    current = body;
                    break;
                }
            }

            // Collect candidates first and CAP them (#548): an archetype system can carry many moons, and
            // every sky body costs a sphere + a texture bake per world load. Keep the most prominent ones
            // (apparent size ≈ real size / real distance); the far tail wouldn't read as more than a dot.
            const int MaxSkyBodies = 14;
            var candidates = new List<NetBody>();
            foreach (var body in system.Bodies)
            {
                bool isLandable = body.Kind is "Planet" or "Moon"
                    || WorldConstants.IsAsteroidType(body.PlanetType);
                if (isLandable && body.Id != map.ActiveLocationId)
                {
                    candidates.Add(body);
                }
            }

            if (candidates.Count > MaxSkyBodies)
            {
                float Prominence(NetBody b)
                {
                    var c = WorldConstants.IsAsteroidType(b.PlanetType)
                        ? WorldConstants.WorldSizeClass.Asteroid
                        : b.Kind == "Moon" ? WorldConstants.WorldSizeClass.Moon : WorldConstants.WorldSizeClass.Planet;
                    float d = 600f;
                    if (current != null)
                    {
                        float dx = b.SystemX - current.SystemX, dy = b.SystemY - current.SystemY, dz = b.SystemZ - current.SystemZ;
                        d = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                    }

                    return WorldConstants.CircumferenceFor(b.Id, c, b.SizeBias) / Mathf.Max(d, 40f);
                }

                candidates.Sort((a, b) => Prominence(b).CompareTo(Prominence(a)));
                candidates.RemoveRange(MaxSkyBodies, candidates.Count - MaxSkyBodies);
            }

            foreach (var body in candidates)
            {

                // Deterministic per (current planet, body): the sky choreography is unique to each world.
                int h = Hash(map.ActiveLocationId + "|" + body.Id);

                var cls = WorldConstants.IsAsteroidType(body.PlanetType)
                    ? WorldConstants.WorldSizeClass.Asteroid
                    : body.Kind == "Moon" ? WorldConstants.WorldSizeClass.Moon : WorldConstants.WorldSizeClass.Planet;
                // The body's REAL walkable circumference (incl. its archetype size bias, #549) — sizes the
                // sky disc below, so a 12000 giant genuinely looms larger than a 5000 dwarf.
                int bodyCirc = WorldConstants.CircumferenceFor(body.Id, cls, body.SizeBias);

                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "SkyBody_" + body.Id;
                var col = go.GetComponent<Collider>();
                if (col != null)
                {
                    Destroy(col);
                }

                go.transform.SetParent(transform, true);
                var shader = Shader.Find("BlocksBeyondTheStars/SkyBodyPhase")
                    ?? Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");

                // Match the orbit/space view: a known planet type gets its REAL generated world map baked as a
                // texture (seas/ground/this world's vegetation), washed a touch toward the system star's hue, so
                // the same body reads the same from the surface as from orbit. Bake is cached + keyed identically
                // to the space view, so it's shared (no extra cost), and mipmaps collapse a tiny disc to its
                // average colour anyway. An unknown type falls back to the data-driven flat GroundColor.
                string locationKey = PlanetOrbitLook.LocationKeyFor(system.Name, body.Name);
                Color sunHue = SunHue();
                var planet = Game.Content?.GetPlanet(body.PlanetType ?? string.Empty);
                Color tint;
                Texture2D baked = null;
                if (planet != null)
                {
                    baked = WorldMinimap.Bake(Game.Content, Game.Atlas, Game.WorldSeed, locationKey, body.PlanetType, bodyCirc, 96, 48,
                        bodyId: body.Id, continents: Game.TerrainContinents);
                    tint = Color.Lerp(Color.white, sunHue, 0.35f); // light star-hue wash over the real map
                }
                else
                {
                    // Data-driven flat colour (surface block + per-planet flora hue + water/lava blend), star-hue washed.
                    Color ground = PlanetOrbitLook.GroundColor(
                        Game.Content, Game.Atlas, Game.WorldSeed, locationKey, body.PlanetType, TintFor(body.PlanetType));
                    tint = Color.Lerp(ground, ground * sunHue, 0.35f);
                }

                var mat = new Material(shader) { color = ShaderColor.Srgb(tint) };
                if (baked != null)
                {
                    mat.mainTexture = baked;
                    mat.mainTextureScale = Vector2.one;
                }

                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                // Apparent size from REAL system-space distance: the body's true circumference (not just a
                // per-class band — #549: a 12000 giant should dwarf a 5000 world) divided by how far the body
                // is from the world we stand on. A moon's parent planet sits ~90-145 units away → it fills a
                // chunk of the sky; a neighbour planet hundreds of units out → a small disc; from an asteroid,
                // nearby planets/moons loom accordingly. 0.004 keeps the classic per-class midpoints
                // (asteroid ~10, moon ~18, planet ~39).
                float radius = 5f + bodyCirc * 0.004f;
                float dist = 600f; // fallback when coords are missing
                if (current != null)
                {
                    float dx = body.SystemX - current.SystemX;
                    float dy = body.SystemY - current.SystemY;
                    float dz = body.SystemZ - current.SystemZ;
                    dist = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                }

                float apparent = Mathf.Clamp(250f * radius / Mathf.Max(dist, 40f), 4f, 120f);

                // Planetary rings (#596): attach the shared ring disc — it inherits the sphere's per-frame
                // position/scale. Only above ~14 units apparent size; a smaller disc mips the bands to mush.
                Material ringMat = null;
                Color ringTint = Color.clear;
                if (body.RingSeed != 0 && apparent >= 14f)
                {
                    PlanetRings.Attach(go.transform, body.RingSeed, sunHue, 0.6f, 3001, out ringMat);
                    ringTint = PlanetRings.TintFor(body.RingSeed, sunHue);
                }

                // Where in the sky it sits: the compass bearing of its REAL position relative to us, so each
                // body genuinely hangs in its own direction (this is what kills the vertical-line stacking the
                // old raw-hash azimuth caused). A small hashed jitter separates any two near-co-directional
                // bodies; a fallback bearing covers missing coords.
                float baseAz;
                if (current != null)
                {
                    float adx = body.SystemX - current.SystemX;
                    float adz = body.SystemZ - current.SystemZ;
                    baseAz = Mathf.Atan2(adx, adz) * Mathf.Rad2Deg + ((h % 37) - 18);
                }
                else
                {
                    baseAz = (h >> 3) % 360;
                }

                _bodies.Add(new SkyBody
                {
                    Go = go,
                    Mat = mat,
                    Tint = tint,
                    RingMat = ringMat,
                    RingTint = ringTint,
                    // Initial rise-time offset spreads the bodies across the day; the authoritative per-system
                    // orbital period (signed) then drifts each one relative to the sun → its phase waxes/wanes.
                    Phase = (h >> 7) % 1000 / 1000f,
                    OrbitPeriodDays = body.OrbitPeriodDays,
                    BaseAz = baseAz,
                    Peak = 28f + (h % 47),          // 28..74° — well below the zenith, so arcs spread out
                    Sweep = 150f + (h % 5) * 12f,   // 150..198° east→up→west drift across the sky
                    Size = apparent,
                });
            }
        }

        /// <summary>Sky tint per planet type — fallback only (the data-driven
        /// <see cref="PlanetOrbitLook.GroundColor"/> is the primary source).</summary>
        private static Color TintFor(string planetType) => (planetType ?? string.Empty).ToLowerInvariant() switch
        {
            "ice" or "tundra" => new Color(0.82f, 0.90f, 0.96f),
            "lava" or "volcanic" => new Color(0.72f, 0.32f, 0.20f),
            "desert" or "salt_flats" or "savanna" => new Color(0.85f, 0.72f, 0.48f),
            "jungle" or "forest" or "swamp" => new Color(0.42f, 0.62f, 0.38f),
            "ocean" => new Color(0.32f, 0.50f, 0.78f),
            "crystal" or "crystal_living" => new Color(0.62f, 0.82f, 0.88f),
            "fungal" or "corrupted" => new Color(0.62f, 0.48f, 0.72f),
            "ashen" => new Color(0.45f, 0.42f, 0.42f),
            "asteroid" => new Color(0.55f, 0.52f, 0.48f),
            "asteroid_metallic" => new Color(0.42f, 0.41f, 0.44f),
            "asteroid_icy" => new Color(0.76f, 0.86f, 0.92f),
            "asteroid_carbon" => new Color(0.26f, 0.25f, 0.24f),
            "asteroid_crystal" => new Color(0.62f, 0.82f, 0.88f),
            "skylands" or "highland" => new Color(0.62f, 0.72f, 0.66f),
            _ => new Color(0.62f, 0.58f, 0.52f), // rocky + unknown
        };

        /// <summary>The current daytime sky base colour (packed 0xRRGGBB from the env), the same value the
        /// camera clear colour builds on — the ring's daylight wash target. Classic blue fallback.</summary>
        private Color SkyBase()
        {
            int packed = Game?.Environment != null ? Game.Environment.SkyColor : 0x8CBFF2;
            return new Color(((packed >> 16) & 0xFF) / 255f, ((packed >> 8) & 0xFF) / 255f, (packed & 0xFF) / 255f);
        }

        /// <summary>The system star's colour normalised to a pure hue (brightness removed), so it tints the body
        /// without darkening it — same light star-hue wash the orbit/space view applies. White when unknown.</summary>
        private Color SunHue()
        {
            int packed = Game?.Environment != null ? Game.Environment.SunColor : 0xFFF6E8;
            var c = new Color(((packed >> 16) & 0xFF) / 255f, ((packed >> 8) & 0xFF) / 255f, (packed & 0xFF) / 255f);
            float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            return m > 0.001f ? new Color(c.r / m, c.g / m, c.b / m) : Color.white;
        }

        private static int Hash(string s)
        {
            int h = 0;
            foreach (char c in s ?? string.Empty)
            {
                h = h * 31 + c;
            }

            return h & 0x7fffffff;
        }
    }
}
