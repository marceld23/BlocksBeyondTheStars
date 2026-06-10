using System.Collections.Generic;
using Spacecraft.Networking.Messages;
using Spacecraft.Shared.World;
using UnityEngine;

namespace Spacecraft.Client
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

        private sealed class SkyBody
        {
            public GameObject Go;
            public Material Mat;
            public Color Tint;
            public float CyclesPerDay;  // own sky cycle: how many times it crosses per local day
            public float Phase;         // 0..1 cycle offset
            public float Azimuth;       // orbital path bearing (deg) — each body rises somewhere else
            public float Size;          // world-space sphere scale at the fixed sky distance
        }

        private readonly List<SkyBody> _bodies = new();
        private string _builtFor = "\0"; // ActiveLocationId the current set was built for
        private float _requestTimer;
        private bool _subscribed;
        private float _tod = -1f; // continuous local day clock (the env's TimeOfDay only steps per update)

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

            float day = Mathf.Clamp01(Mathf.Sin(_tod * Mathf.PI));
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

                // The body's own sky cycle: its angle advances with the LOCAL day at its own rate + phase,
                // on its own tilted path — the same maths family as the sun's arc, but per body. Uses the
                // smoothed continuous clock so motion is glide, not server-update steps.
                float t = _tod * b.CyclesPerDay + b.Phase;
                var dir = -(Quaternion.Euler(t * 360f - 90f, b.Azimuth, 0f) * Vector3.forward);

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

                // A touch brighter at night (like a real moon dominating the dark sky), dimmer by day,
                // fading out right at the horizon.
                float horizon = Mathf.Clamp01((dir.y + 0.04f) / 0.12f);
                b.Mat.color = b.Tint * Mathf.Lerp(1.25f, 0.85f, day) * horizon;
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
                var shader = Shader.Find("Spacecraft/LitColor") ?? Shader.Find("Unlit/Color");
                var tint = TintFor(body.PlanetType);
                var mat = new Material(shader) { color = tint };

                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                _bodies.Add(new SkyBody
                {
                    Go = go,
                    Mat = mat,
                    Tint = tint,
                    // Slow stately giants to fast low skimmers: asteroids cross quickest, planets drift.
                    CyclesPerDay = cls switch
                    {
                        WorldConstants.WorldSizeClass.Asteroid => 1.0f + (h % 100) / 100f * 1.5f,  // 1.0..2.5
                        WorldConstants.WorldSizeClass.Moon => 0.4f + (h % 90) / 90f * 0.9f,        // 0.4..1.3
                        _ => 0.15f + (h % 80) / 80f * 0.45f,                                       // 0.15..0.6
                    },
                    Phase = (h >> 7) % 1000 / 1000f,
                    Azimuth = (h >> 3) % 360,
                    Size = cls switch
                    {
                        WorldConstants.WorldSizeClass.Asteroid => 5f + (h % 7),    //  5..11
                        WorldConstants.WorldSizeClass.Moon => 14f + (h % 10),      // 14..23
                        _ => 22f + (h % 14),                                       // 22..35
                    },
                });
            }
        }

        /// <summary>Sky tint per planet type — a readable wash of the world's character.</summary>
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
