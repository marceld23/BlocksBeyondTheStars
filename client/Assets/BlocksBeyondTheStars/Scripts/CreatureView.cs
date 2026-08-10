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
            public float NextCall; // world time of the next idle vocalisation
            public float AnswerAt; // pending call-answer time (#876) — 0 while none is scheduled
            public float NextAttack; // throttles the attack call while hostile + close
            public bool PrevHostile; // to detect the turn-hostile transition (alert)
            public float PrevHull;   // to detect a hull drop (hurt)
            public Vector3 Settled;  // smoothed position (the lunge is added on top for display)
            public Vector3 PrevSettled; // last frame's smoothed position → velocity for facing
            public Vector3 FaceDir;     // smoothed heading the body turns to face (so it doesn't moonwalk)
            public Vector3 TargetVel;     // velocity estimated from consecutive server updates (#654)
            public float LastTargetChange; // world time of the last authoritative position change (#654)
            public float PitchDeg;         // smoothed slope pitch (#652) — nose up/down along real motion
            public float RollDeg;          // smoothed banking roll (#652) — fliers lean into turns
            public Vector3 PrevFaceDir;    // last frame's facing → heading rate for the banking roll
            public float AttackUntil; // a visible lunge window when attacking
            public GameObject Stasis; // icy-blue stasis shell shown while frozen (item 36)
            public bool Echo;     // cave dwellers' calls get a reverberant echo (item 21)
            public GameObject Nameplate; // floating name label shown above a tamed companion
            public TextMesh NameText;    // the label's text component (updated on rename)
            public GameObject Zzz;       // floating "z z z" shown above a sleeping (off-phase) creature
            public TextMesh ZzzText;     // the sleep label's text component
        }

        private readonly Dictionary<string, Entry> _creatures = new Dictionary<string, Entry>();

        // Reused across frames: allocating these per Update is steady-state GC churn (worst on WebGL,
        // where all garbage lands on the single thread).
        private readonly HashSet<string> _seenScratch = new HashSet<string>();
        private readonly List<string> _staleScratch = new List<string>();

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            // World time, not frame time (#908). While the server holds the world for the pause menu both are
            // stationary, so animals stop walking, calling, answering and lunging with everything else — and
            // because WorldTime skips the paused stretch, the timers below do not all fire at once on resume.
            float now = Game.WorldTime;
            float dt = Game.WorldDeltaTime;

            var seen = _seenScratch;
            seen.Clear();
            var cam = Camera.main; // for the floating health bars (#692)
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
                        NextCall = now + Random.Range(2f, 6f),
                        Settled = pos,
                        PrevSettled = pos,
                        FaceDir = Vector3.forward,
                        PrevFaceDir = Vector3.forward,
                        LastTargetChange = now,
                        PrevHostile = c.Hostile,
                        PrevHull = c.Hull,
                    };
                    _creatures[c.Id] = entry;
                }

                // Dead reckoning (#654): positions arrive at ~2 Hz, so chasing the newest (stale) target
                // rounds every dart into the same soft curve. Estimate the velocity from consecutive
                // updates and extrapolate the target briefly (clamped) — motion reads crisp between
                // packets and degrades to plain smoothing when updates stall. Teleport-sized jumps
                // (spawn shove, eviction) reset the estimate instead of predicting through them.
                if ((pos - entry.Target).sqrMagnitude > 1e-6f)
                {
                    float gap = Mathf.Max(0.05f, now - entry.LastTargetChange);
                    var estimated = (pos - entry.Target) / gap;
                    entry.TargetVel = estimated.sqrMagnitude > 64f ? Vector3.zero : estimated;
                    entry.LastTargetChange = now;
                }

                entry.Target = pos;
                var predicted = entry.Target + entry.TargetVel * Mathf.Min(now - entry.LastTargetChange, 0.3f);
                // Smoothly chase the (extrapolated) authoritative position; a visible lunge toward the
                // player is added on top during an attack so attacks read clearly.
                entry.Settled = Vector3.Lerp(entry.Settled, predicted, dt * 8f);
                Vector3 lunge = Vector3.zero;
                if (now < entry.AttackUntil)
                {
                    float k = 1f - (entry.AttackUntil - now) / 0.22f;
                    var to = Game.PlayerPosition - Game.ScenePos(entry.Settled.x, entry.Settled.y, entry.Settled.z); to.y = 0f;
                    if (to.sqrMagnitude > 0.04f)
                    {
                        // Big bodies barely lunge (#749): the server now holds hunters at a size-scaled ring,
                        // and a full 0.6 lunge would shove a titan's bulk back through the player.
                        float amp = 0.6f * Mathf.Clamp01(2f / Mathf.Max(1f, c.Size));
                        lunge = to.normalized * (Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI) * amp);
                    }
                }

                // Smoothing is in world space; map to the scene at the copy nearest the player (longitude wraps).
                entry.Root.transform.position = Game.ScenePos(entry.Settled.x, entry.Settled.y, entry.Settled.z) + lunge;

                // Turn the body to face the way it's actually moving (it used to slide/moonwalk — the server never
                // sent a facing). Derived from the smoothed velocity; held when standing still. Direction is the
                // same in world + scene space (the scene only offsets for longitude wrap, it doesn't rotate).
                // On top of the yaw (#652): bodies pitch along their real vertical motion (a titan descending a
                // slope noses down instead of levitating level), and fliers bank into turns. Medusae are a bell —
                // no nose to pitch, no wings to bank.
                Vector3 vel3 = entry.Settled - entry.PrevSettled;
                entry.PrevSettled = entry.Settled;
                Vector3 vel = vel3;
                vel.y = 0f;
                if (vel.sqrMagnitude > 1e-5f)
                {
                    entry.FaceDir = Vector3.Slerp(entry.FaceDir, vel.normalized, 1f - Mathf.Exp(-8f * dt));
                    bool medusa = string.Equals(c.BodyPlan, "Medusa", System.StringComparison.OrdinalIgnoreCase);
                    float targetPitch = 0f, targetRoll = 0f;
                    if (!medusa)
                    {
                        float horiz = vel.magnitude;
                        targetPitch = Mathf.Clamp(
                            Mathf.Atan2(vel3.y, Mathf.Max(horiz, 0.01f)) * Mathf.Rad2Deg, -25f, 25f);
                        if (string.Equals(c.Habitat, "Air", System.StringComparison.OrdinalIgnoreCase))
                        {
                            float turnRate = Vector3.SignedAngle(entry.PrevFaceDir, entry.FaceDir, Vector3.up)
                                / Mathf.Max(dt, 1e-4f);
                            targetRoll = Mathf.Clamp(-turnRate * 0.25f, -20f, 20f);
                        }
                    }

                    entry.PrevFaceDir = entry.FaceDir;
                    entry.PitchDeg = Mathf.Lerp(entry.PitchDeg, targetPitch, 1f - Mathf.Exp(-6f * dt));
                    entry.RollDeg = Mathf.Lerp(entry.RollDeg, targetRoll, 1f - Mathf.Exp(-6f * dt));
                    if (entry.FaceDir.sqrMagnitude > 1e-4f)
                    {
                        entry.Root.transform.rotation = Quaternion.LookRotation(entry.FaceDir, Vector3.up)
                            * Quaternion.Euler(-entry.PitchDeg, 0f, entry.RollDeg);
                    }
                }

                SetStasis(entry, c.Frozen, c.Size); // icy-blue shell while held in stasis (item 36)
                UpdateNameplate(entry, c);          // floating name label above a tamed companion
                UpdateSleep(entry, c);              // breathing bob + "z z z" while the creature is asleep (off-phase)

                // Periodic idle vocalisation, spatialised at the creature, pitched by its size. A sleeper is
                // quiet — only an occasional soft, low snore rather than its full waking call. Every utterance
                // gets a small pitch/volume jitter and a random take of the species call (#876/#879) — the
                // species voice stays deterministic, only the individual utterance varies.
                if (now >= entry.NextCall)
                {
                    if (c.Asleep)
                    {
                        entry.NextCall = now + Random.Range(9f, 18f);
                        ClientAudio.Instance?.At(TakeOf(entry.Call), entry.Root.transform.position,
                            entry.Pitch * 0.7f * PitchJitter(), 0.3f * VolJitter(), entry.Echo);
                    }
                    else
                    {
                        entry.NextCall = now + Random.Range(5f, 12f);
                        ClientAudio.Instance?.At(TakeOf(entry.Call), entry.Root.transform.position,
                            entry.Pitch * PitchJitter(), 0.8f * VolJitter(), entry.Echo);
                        if (Random.value < 0.2f)
                        {
                            entry.AnswerAt = now + Random.Range(0.4f, 0.9f); // a second animal "answers"
                        }
                    }
                }

                // The scheduled answer call (#876): the same species voice from a slightly different spot at a
                // slightly different pitch — reads as a second animal answering the first.
                if (entry.AnswerAt > 0f && now >= entry.AnswerAt)
                {
                    entry.AnswerAt = 0f;
                    if (!c.Asleep)
                    {
                        var off = new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f));
                        ClientAudio.Instance?.At(TakeOf(entry.Call), entry.Root.transform.position + off,
                            entry.Pitch * PitchJitter(), 0.55f * VolJitter(), entry.Echo);
                    }
                }

                // React to authoritative state: hurt on a hull drop, alert on turning hostile,
                // and a throttled attack call when a hostile creature is close to the player.
                var audio = ClientAudio.Instance;
                if (audio != null)
                {
                    if (c.Hull < entry.PrevHull - 0.5f)
                    {
                        audio.At(entry.Bank + "_hurt", entry.Root.transform.position, entry.Pitch * PitchJitter(), 0.9f * VolJitter(), entry.Echo);
                    }

                    if (c.Hostile && !entry.PrevHostile)
                    {
                        audio.At(entry.Bank + "_alert", entry.Root.transform.position, entry.Pitch * PitchJitter(), 0.9f * VolJitter(), entry.Echo);
                    }

                    // No bite-lunge once the player has fled into their ship: the server stops targeting a
                    // boarded player (no proximity damage), so the render side must not keep mauling the hull.
                    if (c.Hostile && !Game.Aboard && now >= entry.NextAttack
                        && (entry.Root.transform.position - Game.PlayerPosition).sqrMagnitude < 9f)
                    {
                        entry.NextAttack = now + Random.Range(1.5f, 3.5f);
                        audio.At(entry.Bank + "_attack", entry.Root.transform.position, entry.Pitch * PitchJitter(), 1f * VolJitter(), entry.Echo);
                        entry.AttackUntil = now + 0.22f;            // lunge
                        SpawnAttackFx(Vector3.Lerp(Game.PlayerPosition, entry.Settled, 0.35f) + Vector3.up * 0.9f);
                    }
                }

                entry.PrevHull = c.Hull;
                entry.PrevHostile = c.Hostile;

                // Floating health bar (#692): sits just above where a companion nameplate would hang, height
                // scaled with the creature's size; companions read friendly cyan, wild fauna the health ramp.
                float barHeight = 1.5f * Mathf.Clamp(c.Size, 0.4f, 8f) + 0.7f;
                EnemyHealthBars.Push(Game, cam, c.Id,
                    entry.Root.transform.position + Vector3.up * barHeight,
                    c.Hull, c.HullMax, friendly: !string.IsNullOrEmpty(c.OwnerId),
                    fadeStart: 18f, fadeEnd: 28f);
            }

            if (_creatures.Count > seen.Count)
            {
                var stale = _staleScratch;
                stale.Clear();
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
                    ClientAudio.Instance?.At(e.Bank + "_die", e.Root.transform.position, e.Pitch * PitchJitter(), 0.9f * VolJitter());
                    if (e.Nameplate != null) Destroy(e.Nameplate); // parented to the game root, not e.Root → free it too
                    if (e.Zzz != null) Destroy(e.Zzz);             // sleep label is under the game root too
                    Destroy(e.Root);
                    _creatures.Remove(id);
                    EnemyHealthBars.Forget(id);
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

            float top = 1.5f * Mathf.Clamp(c.Size, 0.4f, 8f) + 0.4f; // 8: titans (#638) far exceed the old 3
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

            float s = Mathf.Clamp(c.Size, 0.4f, 8f); // 8: titans (#638)
            float breathe = Mathf.Sin(Game.WorldTime * 1.6f) * 0.03f * s;
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
            float drift = Mathf.Repeat(Game.WorldTime * 0.4f, 1f) * 0.5f; // slow upward drift
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
            alpha *= 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Game.WorldTime * 1.2f)); // gentle breathing pulse
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
                float s = Mathf.Clamp(size, 0.4f, 8f); // 8: titans (#638)
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
            "creature_call_whistle", "creature_call_click", "creature_call_thrum",
        };

        private static readonly string[] AmphibianCalls =
        {
            "creature_call_croak", "creature_call_gurgle", "creature_call_warble", "creature_call_trill",
            "creature_call_cluck", "creature_call_burble",
        };

        private static readonly string[] WaterCalls =
        {
            "creature_call_gurgle", "creature_call_warble", "creature_call_click", "creature_call_whistle",
            "creature_call_burble",
        };

        private static readonly string[] LavaCalls =
        {
            "creature_call_hiss", "creature_call_rumble", "creature_call_growl", "creature_call_snarl",
            "creature_call_sizzle",
        };

        private static readonly string[] AirCalls =
        {
            "creature_call_screech", "creature_call_whistle", "creature_call_trill", "creature_call_chirp",
            "creature_call_warble", "creature_call_keen",
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

        /// <summary>Per-utterance pitch jitter (#876): small enough to keep the species voice recognisable,
        /// large enough that two calls never sound bit-identical. Web only allows positive pitch.</summary>
        private static float PitchJitter() => Random.Range(0.93f, 1.07f);

        /// <summary>Per-utterance volume jitter (#876).</summary>
        private static float VolJitter() => Random.Range(0.85f, 1.15f);

        /// <summary>Picks a random take of the species call (#879): the second ElevenLabs take
        /// (<c>*_2</c>), when bundled, plays half the time — the species keeps its call TYPE but no
        /// longer repeats one identical file.</summary>
        private static string TakeOf(string call)
        {
            var audio = ClientAudio.Instance;
            string alt = call + "_2";
            return audio != null && audio.Has(alt) && Random.value < 0.5f ? alt : call;
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
