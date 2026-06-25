// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Renders the server's live creatures (<c>GameBootstrap.Creatures</c>) as parametric blocky
    /// bodies via <see cref="CreatureBuilder"/>, syncing the set each frame. The server is
    /// authoritative over spawns/positions/deaths; positions are interpolated for smoothness and
    /// the player attacks the nearest one with F (PlayerController). Render-only.
    /// </summary>
    public sealed class CreatureView : MonoBehaviour
    {
        public GameBootstrap Game;

        private sealed class Entry
        {
            public GameObject Root;
            public Vector3 Target;
            public string Bank;   // creature_{size}_{disposition} voice bank (hurt/alert/attack/die)
            public string Call;   // this species' signature idle call (creature_call_*)
            public float Pitch;   // per-species voice pitch (size + a per-species offset)
            public float NextCall; // Time.time of the next idle vocalisation
            public float NextAttack; // throttles the attack call while hostile + close
            public bool PrevHostile; // to detect the turn-hostile transition (alert)
            public float PrevHull;   // to detect a hull drop (hurt)
            public Vector3 Settled;  // smoothed position (the lunge is added on top for display)
            public Vector3 PrevSettled; // last frame's smoothed position → velocity for facing
            public Vector3 FaceDir;     // smoothed heading the body turns to face (so it doesn't moonwalk)
            public float AttackUntil; // a visible lunge window when attacking
            public GameObject Stasis; // icy-blue stasis shell shown while frozen (item 36)
            public bool Echo;     // cave dwellers' calls get a reverberant echo (item 21)
            public GameObject Nameplate; // floating name label shown above a tamed companion
            public TextMesh NameText;    // the label's text component (updated on rename)
            public GameObject Zzz;       // floating "z z z" shown above a sleeping (off-phase) creature
            public TextMesh ZzzText;     // the sleep label's text component
        }

        private readonly Dictionary<string, Entry> _creatures = new Dictionary<string, Entry>();

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            var seen = new HashSet<string>();
            foreach (var c in Game.Creatures)
            {
                seen.Add(c.Id);
                var pos = new Vector3(c.X, c.Y, c.Z); // canonical world space; smoothing stays in world space
                if (!_creatures.TryGetValue(c.Id, out var entry))
                {
                    var root = new GameObject("Creature_" + c.SpeciesId);
                    root.transform.SetParent(transform, true); // under the game root → destroyed on teardown (not leaked into menus/editors)
                    root.transform.position = Game.ScenePos(pos.x, pos.y, pos.z); // seam-aware (longitude wraps)
                    new CreatureBuilder().Build(root, c);
                    int idh = SpeciesHash(c.SpeciesId);
                    float sizePitch = Mathf.Clamp(1.5f - 0.35f * c.Size, 0.7f, 1.6f);
                    float speciesOffset = 0.82f + (idh % 37) / 37f * 0.45f; // 0.82..1.27, consistent per species
                    entry = new Entry
                    {
                        Root = root,
                        Target = pos,
                        Bank = Bank(c),
                        // The signature call gives each species a distinct voice; the size+species pitch keeps
                        // it consistent across all individuals of that species (never random per individual). The
                        // call pool is habitat-flavoured (item 21): cave dwellers moan/drone, amphibians croak.
                        Call = CallForHabitat(c.Habitat, idh),
                        Echo = string.Equals(c.Habitat, "Cave", System.StringComparison.OrdinalIgnoreCase),
                        Pitch = Mathf.Clamp(sizePitch * speciesOffset, 0.6f, 1.85f),
                        NextCall = Time.time + Random.Range(2f, 6f),
                        Settled = pos,
                        PrevSettled = pos,
                        FaceDir = Vector3.forward,
                        PrevHostile = c.Hostile,
                        PrevHull = c.Hull,
                    };
                    _creatures[c.Id] = entry;
                }

                entry.Target = pos;
                // Smoothly chase the authoritative position; a visible lunge toward the player is added
                // on top during an attack so attacks read clearly.
                entry.Settled = Vector3.Lerp(entry.Settled, entry.Target, Time.deltaTime * 8f);
                Vector3 lunge = Vector3.zero;
                if (Time.time < entry.AttackUntil)
                {
                    float k = 1f - (entry.AttackUntil - Time.time) / 0.22f;
                    var to = Game.PlayerPosition - Game.ScenePos(entry.Settled.x, entry.Settled.y, entry.Settled.z); to.y = 0f;
                    if (to.sqrMagnitude > 0.04f)
                    {
                        lunge = to.normalized * (Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI) * 0.6f);
                    }
                }

                // Smoothing is in world space; map to the scene at the copy nearest the player (longitude wraps).
                entry.Root.transform.position = Game.ScenePos(entry.Settled.x, entry.Settled.y, entry.Settled.z) + lunge;

                // Turn the body to face the way it's actually moving (it used to slide/moonwalk — the server never
                // sent a facing). Derived from the smoothed velocity; held when standing still. Direction is the
                // same in world + scene space (the scene only offsets for longitude wrap, it doesn't rotate).
                Vector3 vel = entry.Settled - entry.PrevSettled;
                entry.PrevSettled = entry.Settled;
                vel.y = 0f;
                if (vel.sqrMagnitude > 1e-5f)
                {
                    entry.FaceDir = Vector3.Slerp(entry.FaceDir, vel.normalized, 1f - Mathf.Exp(-8f * Time.deltaTime));
                    if (entry.FaceDir.sqrMagnitude > 1e-4f)
                    {
                        entry.Root.transform.rotation = Quaternion.LookRotation(entry.FaceDir, Vector3.up);
                    }
                }

                SetStasis(entry, c.Frozen, c.Size); // icy-blue shell while held in stasis (item 36)
                UpdateNameplate(entry, c);          // floating name label above a tamed companion
                UpdateSleep(entry, c);              // breathing bob + "z z z" while the creature is asleep (off-phase)

                // Periodic idle vocalisation, spatialised at the creature, pitched by its size. A sleeper is
                // quiet — only an occasional soft, low snore rather than its full waking call.
                if (Time.time >= entry.NextCall)
                {
                    if (c.Asleep)
                    {
                        entry.NextCall = Time.time + Random.Range(9f, 18f);
                        ClientAudio.Instance?.At(entry.Call, entry.Root.transform.position, entry.Pitch * 0.7f, 0.3f, entry.Echo);
                    }
                    else
                    {
                        entry.NextCall = Time.time + Random.Range(5f, 12f);
                        ClientAudio.Instance?.At(entry.Call, entry.Root.transform.position, entry.Pitch, 0.8f, entry.Echo);
                    }
                }

                // React to authoritative state: hurt on a hull drop, alert on turning hostile,
                // and a throttled attack call when a hostile creature is close to the player.
                var audio = ClientAudio.Instance;
                if (audio != null)
                {
                    if (c.Hull < entry.PrevHull - 0.5f)
                    {
                        audio.At(entry.Bank + "_hurt", entry.Root.transform.position, entry.Pitch, 0.9f, entry.Echo);
                    }

                    if (c.Hostile && !entry.PrevHostile)
                    {
                        audio.At(entry.Bank + "_alert", entry.Root.transform.position, entry.Pitch, 0.9f, entry.Echo);
                    }

                    // No bite-lunge once the player has fled into their ship: the server stops targeting a
                    // boarded player (no proximity damage), so the render side must not keep mauling the hull.
                    if (c.Hostile && !Game.Aboard && Time.time >= entry.NextAttack
                        && (entry.Root.transform.position - Game.PlayerPosition).sqrMagnitude < 9f)
                    {
                        entry.NextAttack = Time.time + Random.Range(1.5f, 3.5f);
                        audio.At(entry.Bank + "_attack", entry.Root.transform.position, entry.Pitch, 1f, entry.Echo);
                        entry.AttackUntil = Time.time + 0.22f;            // lunge
                        SpawnAttackFx(Vector3.Lerp(Game.PlayerPosition, entry.Settled, 0.35f) + Vector3.up * 0.9f);
                    }
                }

                entry.PrevHull = c.Hull;
                entry.PrevHostile = c.Hostile;
            }

            if (_creatures.Count > seen.Count)
            {
                var stale = new List<string>();
                foreach (var id in _creatures.Keys)
                {
                    if (!seen.Contains(id))
                    {
                        stale.Add(id);
                    }
                }

                foreach (var id in stale)
                {
                    var e = _creatures[id];
                    ClientAudio.Instance?.At(e.Bank + "_die", e.Root.transform.position, e.Pitch, 0.9f);
                    if (e.Nameplate != null) Destroy(e.Nameplate); // parented to the game root, not e.Root → free it too
                    if (e.Zzz != null) Destroy(e.Zzz);             // sleep label is under the game root too
                    Destroy(e.Root);
                    _creatures.Remove(id);
                }
            }
        }

        /// <summary>Shows a floating name label above a tamed companion (design: docs/developer/CREATURE_TAMING.md) so
        /// the player can pick their pet out of the wild fauna. Built lazily; billboarded to face the camera and
        /// kept under the game root (not the rotating creature rig) so the text stays upright + readable.</summary>
        private void UpdateNameplate(Entry e, NetCreature c)
        {
            bool owned = !string.IsNullOrEmpty(c.OwnerId);
            if (!owned)
            {
                if (e.Nameplate != null) e.Nameplate.SetActive(false);
                return;
            }

            if (e.Nameplate == null)
            {
                var np = new GameObject("CompanionName");
                np.transform.SetParent(transform, true); // game root, not the creature rig (no inherited rotation)
                var tm = np.AddComponent<TextMesh>();
                tm.font = UiKit.Font;
                var mr = np.GetComponent<MeshRenderer>();
                if (mr != null && UiKit.Font != null) mr.sharedMaterial = UiKit.Font.material;
                tm.fontSize = 48;
                tm.characterSize = 0.05f;
                tm.anchor = TextAnchor.LowerCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = new Color(0.6f, 0.96f, 0.8f); // friendly green-cyan
                e.Nameplate = np;
                e.NameText = tm;
            }

            float top = 1.5f * Mathf.Clamp(c.Size, 0.4f, 3f) + 0.4f;
            var platePos = e.Root.transform.position + Vector3.up * top;

            // Companion names only read up close, mirroring the NPC nameplates: fade between 18 m and 28 m,
            // and drop the label entirely beyond that so a distant pet stays anonymous in the fauna.
            var cam = Camera.main;
            const float fadeStart = 18f, fadeEnd = 28f;
            float alpha = 1f;
            if (cam != null)
            {
                float dist = Vector3.Distance(cam.transform.position, platePos);
                if (dist >= fadeEnd)
                {
                    if (e.Nameplate.activeSelf) e.Nameplate.SetActive(false);
                    return;
                }

                if (dist > fadeStart) alpha = 1f - (dist - fadeStart) / (fadeEnd - fadeStart);
            }

            if (!e.Nameplate.activeSelf) e.Nameplate.SetActive(true);

            string label = string.IsNullOrEmpty(c.CustomName) ? c.Name : c.CustomName;
            if (e.NameText.text != label) e.NameText.text = label;

            var col = new Color(0.6f, 0.96f, 0.8f, alpha); // friendly green-cyan, faded by distance
            if (e.NameText.color != col) e.NameText.color = col;

            e.Nameplate.transform.position = platePos;
            if (cam != null)
            {
                e.Nameplate.transform.rotation = Quaternion.LookRotation(e.Nameplate.transform.position - cam.transform.position);
            }
        }

        /// <summary>A creature in its off-phase (night for a diurnal animal, day for a nocturnal one) sleeps in
        /// place — the server flags it <see cref="NetCreature.Asleep"/>. Render it as resting: settle a touch
        /// lower with a slow breathing bob, and float a soft "z z z" above it so the player can read that it is
        /// asleep (and can be snuck up on, or woken by coming close / hitting it). Label is built lazily, kept
        /// under the game root (upright, not the creature rig) and billboarded + distance-faded like nameplates.</summary>
        private void UpdateSleep(Entry e, NetCreature c)
        {
            if (!c.Asleep)
            {
                if (e.Zzz != null && e.Zzz.activeSelf) e.Zzz.SetActive(false);
                return;
            }

            float s = Mathf.Clamp(c.Size, 0.4f, 3f);
            float breathe = Mathf.Sin(Time.time * 1.6f) * 0.03f * s;
            e.Root.transform.position += Vector3.up * (breathe - 0.12f * s); // settle low + gentle breathing

            if (e.Zzz == null)
            {
                var z = new GameObject("CreatureZzz");
                z.transform.SetParent(transform, true); // game root, upright (not the rotating creature rig)
                var tm = z.AddComponent<TextMesh>();
                tm.font = UiKit.Font;
                var mr = z.GetComponent<MeshRenderer>();
                if (mr != null && UiKit.Font != null) mr.sharedMaterial = UiKit.Font.material;
                tm.fontSize = 48;
                tm.characterSize = 0.05f;
                tm.anchor = TextAnchor.LowerCenter;
                tm.alignment = TextAlignment.Center;
                tm.text = "z z z";
                tm.color = new Color(0.8f, 0.88f, 1f, 0.85f); // soft sleepy blue-white
                e.Zzz = z;
                e.ZzzText = tm;
            }

            float top = 1.5f * s + 0.5f;
            float drift = Mathf.Repeat(Time.time * 0.4f, 1f) * 0.5f; // slow upward drift
            var platePos = e.Root.transform.position + Vector3.up * (top + drift);
            e.Zzz.transform.position = platePos;

            var cam = Camera.main;
            float alpha = 1f;
            if (cam != null)
            {
                e.Zzz.transform.rotation = Quaternion.LookRotation(platePos - cam.transform.position);
                float dist = Vector3.Distance(cam.transform.position, platePos);
                if (dist >= 28f)
                {
                    if (e.Zzz.activeSelf) e.Zzz.SetActive(false);
                    return;
                }

                if (dist > 18f) alpha = 1f - (dist - 18f) / 10f;
            }

            if (!e.Zzz.activeSelf) e.Zzz.SetActive(true);
            alpha *= 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 1.2f)); // gentle breathing pulse
            var col = new Color(0.8f, 0.88f, 1f, 0.85f * alpha);
            if (e.ZzzText.color != col) e.ZzzText.color = col;
        }

        /// <summary>Shows/hides an icy-blue stasis shell around a creature while it is frozen by the stasis
        /// projector (item 36). Built lazily on first freeze; the translucent shader can't strip to pink.</summary>
        private static void SetStasis(Entry e, bool frozen, float size)
        {
            if (frozen && e.Stasis == null)
            {
                e.Stasis = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var col = e.Stasis.GetComponent<Collider>();
                if (col != null)
                {
                    Destroy(col);
                }

                e.Stasis.transform.SetParent(e.Root.transform, false);
                float s = Mathf.Clamp(size, 0.4f, 3f);
                e.Stasis.transform.localPosition = new Vector3(0f, 0.55f * s, 0f);
                e.Stasis.transform.localScale = new Vector3(1.15f * s, 1.5f * s, 1.15f * s);
                e.Stasis.GetComponent<Renderer>().sharedMaterial = StasisMaterial();
            }

            if (e.Stasis != null && e.Stasis.activeSelf != frozen)
            {
                e.Stasis.SetActive(frozen);
            }
        }

        private static Material _stasisMat;

        private static Material StasisMaterial()
        {
            if (_stasisMat == null)
            {
                var sh = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
                _stasisMat = new Material(sh);
                _stasisMat.SetColor("_Color", ShaderColor.Srgb(new Color(0.5f, 0.8f, 1f, 0.32f))); // translucent icy blue
                _stasisMat.renderQueue = 3000;
            }

            return _stasisMat;
        }

        /// <summary>Voice bank for a creature: size tier (small/medium/large) x disposition (calm/hostile).</summary>
        private static string Bank(NetCreature c)
        {
            string size = c.Size < 0.8f ? "small" : c.Size < 1.6f ? "medium" : "large";
            return $"creature_{size}_{(c.Hostile ? "hostile" : "calm")}";
        }

        // Signature idle calls — each species picks one (by id), so a world's fauna sounds varied.
        private static readonly string[] Calls =
        {
            "creature_call_chirp", "creature_call_croak", "creature_call_growl", "creature_call_screech",
            "creature_call_warble", "creature_call_hoot", "creature_call_trill", "creature_call_click",
            "creature_call_rumble", "creature_call_bellow", "creature_call_hiss", "creature_call_chitter",
            // Task 6 — more creature voices.
            "creature_call_purr", "creature_call_moan", "creature_call_squeak", "creature_call_drone",
            "creature_call_gurgle", "creature_call_yelp", "creature_call_snarl", "creature_call_whistle",
            "creature_call_cluck", "creature_call_wail",
        };

        // Habitat-flavoured idle-call pools (item 21): cave dwellers sound deep + echoey, amphibians wet +
        // croaky, water creatures burble, lava critters hiss/rumble, fliers shriek/trill. Land uses the full
        // pool. The per-species pick stays deterministic (by id), so each species keeps one consistent voice.
        private static readonly string[] CaveCalls =
        {
            "creature_call_moan", "creature_call_drone", "creature_call_wail", "creature_call_hoot",
            "creature_call_whistle", "creature_call_click",
        };

        private static readonly string[] AmphibianCalls =
        {
            "creature_call_croak", "creature_call_gurgle", "creature_call_warble", "creature_call_trill",
            "creature_call_cluck",
        };

        private static readonly string[] WaterCalls =
        {
            "creature_call_gurgle", "creature_call_warble", "creature_call_click", "creature_call_whistle",
        };

        private static readonly string[] LavaCalls =
        {
            "creature_call_hiss", "creature_call_rumble", "creature_call_growl", "creature_call_snarl",
        };

        private static readonly string[] AirCalls =
        {
            "creature_call_screech", "creature_call_whistle", "creature_call_trill", "creature_call_chirp",
            "creature_call_warble",
        };

        /// <summary>The species' signature idle call, chosen deterministically (by species id) from its
        /// habitat's call pool (item 21).</summary>
        private static string CallForHabitat(string habitat, int idh)
        {
            var pool = (habitat ?? "Land").ToLowerInvariant() switch
            {
                "cave" => CaveCalls,
                "amphibian" => AmphibianCalls,
                "water" => WaterCalls,
                "lava" => LavaCalls,
                "air" => AirCalls,
                _ => Calls,
            };
            return pool[(idh & 0x7fffffff) % pool.Length];
        }

        private static int SpeciesHash(string id)
        {
            int h = 0;
            foreach (char ch in id ?? string.Empty)
            {
                h = h * 31 + ch;
            }

            return h & 0x7fffffff;
        }

        /// <summary>A brief red "claw slash" burst at the player so a creature's attack reads clearly.</summary>
        private void SpawnAttackFx(Vector3 at)
        {
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque");
            var mat = new Material(shader) { color = ShaderColor.Srgb(new Color(1f, 0.2f, 0.15f)) };
            for (int i = 0; i < 3; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "ClawFx";
                var col = go.GetComponent<Collider>();
                if (col != null)
                {
                    Destroy(col);
                }

                go.transform.SetParent(transform, true); // under the game root (no leak)
                go.transform.position = at + new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(-0.3f, 0.3f), Random.Range(-0.4f, 0.4f));
                go.transform.rotation = Quaternion.Euler(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(-40f, 40f));
                go.transform.localScale = new Vector3(0.06f, 0.5f, 0.06f); // a thin slash mark
                go.GetComponent<Renderer>().sharedMaterial = mat;
                Destroy(go, 0.22f);
            }
        }
    }
}
