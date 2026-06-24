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
                float horizon = Mathf.Clamp01((dir.y + 0.04f) / 0.12f);
                // Overall dim: bodies dominate the night sky and fade toward day — but keep a higher day-time floor
                // so they don't wash to black against the bright ACES-tonemapped daytime sky (they're a feature).
                b.Mat.color = ShaderColor.Srgb(b.Tint * Mathf.Lerp(1.25f, 0.7f, day) * horizon);
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

            foreach (var body in system.Bodies)
            {
                bool landable = body.Kind is "Planet" or "Moon"
                    || string.Equals(body.PlanetType, "asteroid", System.StringComparison.OrdinalIgnoreCase);
                if (!landable || body.Id == map.ActiveLocationId)
                {
                    continue; // stations etc. stay invisible from the surface; the body you stand on too
                }

                // Deterministic per (current planet, body): the sky choreography is unique to each world.
                int h = Hash(map.ActiveLocationId + "|" + body.Id);

                var cls = string.Equals(body.PlanetType, "asteroid", System.StringComparison.OrdinalIgnoreCase)
                    ? WorldConstants.WorldSizeClass.Asteroid
                    : body.Kind == "Moon" ? WorldConstants.WorldSizeClass.Moon : WorldConstants.WorldSizeClass.Planet;

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
                    int circ = WorldConstants.CircumferenceFor(body.Id, cls);
                    baked = WorldMinimap.Bake(Game.Content, Game.Atlas, Game.WorldSeed, locationKey, body.PlanetType, circ, 96, 48);
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

                // Apparent size from REAL system-space distance: a per-class "radius" divided by how far the
                // body is from the world we stand on. A moon's parent planet sits ~90-145 units away → it
                // fills a chunk of the sky; a neighbour planet hundreds of units out → a small disc; from an
                // asteroid, nearby planets/moons loom accordingly (the planet largest).
                float radius = cls switch
                {
                    WorldConstants.WorldSizeClass.Asteroid => 6f + (h % 3),
                    WorldConstants.WorldSizeClass.Moon => 16f + (h % 4),
                    _ => 36f + (h % 6),
                };
                float dist = 600f; // fallback when coords are missing
                if (current != null)
                {
                    float dx = body.SystemX - current.SystemX;
                    float dy = body.SystemY - current.SystemY;
                    float dz = body.SystemZ - current.SystemZ;
                    dist = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                }

                float apparent = Mathf.Clamp(250f * radius / Mathf.Max(dist, 40f), 4f, 120f);

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
            "skylands" or "highland" => new Color(0.62f, 0.72f, 0.66f),
            _ => new Color(0.62f, 0.58f, 0.52f), // rocky + unknown
        };

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
