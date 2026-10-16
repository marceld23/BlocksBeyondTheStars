// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Renders settlement + space-station NPCs (<see cref="NpcList"/>): a blocky humanoid avatar per
    /// NPC with its skin/outfit colours, heading and a role nameplate. Positions interpolate toward the
    /// latest authoritative value; NPCs no longer in the list are removed. Cosmetic — the server owns
    /// NPC state. Mirrors <see cref="RemotePlayers"/>.
    /// #2592/#2593/#2605: NPCs also speak — a speech bubble (<see cref="ScreenLabelLayer.Bubble"/>, wrapped) with a wordless
    /// babble in the NPC's own voice (<see cref="NpcVoice"/>) through the voice budget (<see cref="NpcVoiceGate"/>: one babble
    /// at a time, quiet in crowds and under VEGA), a wave/nod/cheer, a head turning to a player nearby, and an optional
    /// layer of reactions (a trade confirmation, an alarm, a rare idle murmur). An AI upgrade of a bubble only swaps its
    /// text (<see cref="NpcGreetingTracker"/>).
    /// </summary>
    public sealed class NpcView : MonoBehaviour
    {
        public GameBootstrap Game;

        /// <summary>#2592: the live view (one per game root) — the dialog panel and other client code reach the NPCs' voices
        /// and gestures through it.</summary>
        public static NpcView Instance { get; private set; }

        private sealed class Npc
        {
            public GameObject Go;
            public PlayerAvatar Avatar;
            public Vector3 Target;        // canonical world space (the newest snapshot)
            public Vector3 SettledWorld;   // interpolated world position (mapped to scene for display)
            public float Yaw;              // the drawn yaw, degrees
            public float TargetYaw;        // the newest snapshot's yaw, degrees (the fallback before any snapshot is drawn)

            // #2666: buffered snapshots on the server clock, drawn ~0.3 s behind the newest along the curve through them.
            public MotionInterpolator Interp = new MotionInterpolator(initialInterval: 0.2, minDelay: 0.15, maxDelay: 0.8);
            public string Label;
            public float GestureTimer;     // counts down to the next work gesture (mine/place/talk swing)
            public float GestureLo, GestureHi; // cadence range from the NPC's theme/role
            public string Greeting;        // item 15: a contextual speech-bubble line (empty = none showing)

            // #1516: the per-frame nameplate/bubble strings, rebuilt only when their inputs change.
            public string CachedLabel;
            public string CachedLabelBase;
            public string CachedStageKey;
            public object CachedLabelLoc;
            public string CachedGreetingSource;
            public string CachedGreetingQuoted;
            public float GreetingUntil;    // world time after which the bubble fades out

            // #1869: the routine as the server reports it.
            public byte Pose;              // 0 stand, 1 sit, 2 lie asleep
            public string Held = string.Empty;
            public string ActivityKey = string.Empty;
            public string NameKey = string.Empty; // 2026-09: a profession's nameplate key also picks its greeting
            public string CachedActivityKey;
            public string CachedActivityText;
            public GameObject Zzz;         // the soft "z z z" over a sleeper
            public TextMesh ZzzText;       // its text component (faded with distance, #2531)

            // #2592/#2593: the voice (fixed per NPC from its face seed), the head turn and the idle murmur's schedule.
            public string Role = string.Empty;
            public bool IsRobot;
            public int Seed;
            public NpcVoiceBank Voice;
            public float Pitch = 1f;
            public int LastClip = -1;      // the clip its last babble ended with (the next one starts with another)
            public float NextIdleAt;       // unscaled time of its next idle-murmur try
            public int IdleCount;
            public bool Looking;           // its head follows the player

            // #2647: a hunter turns to face its shot for the length of the bow gesture (the server's facing may lag).
            public float AimYaw;
            public float AimUntil;         // Time.time until which AimYaw overrides the server's facing
        }

        // #2593: NPCs turn their head to a player this close; #2592: the idle murmur and a trade's thanks reach this far.
        private const float LookRange = 6f;
        private const float IdleMurmurRange = 6f;
        private const float TradeReach = 4.5f;

        // #2592: the voice budget, the bubble ids and the two voice channels — one babble at a time (two sources so a cut
        // babble fades while the next starts) plus one reaction channel. Unscaled time: sound runs in real time.
        private readonly NpcGreetingTracker _greetings = new NpcGreetingTracker();
        private readonly NpcVoiceGate _voiceGate = new NpcVoiceGate();
        private readonly NpcChopGate _chopGate = new NpcChopGate(); // #2647: the woodcutters' chop sounds
        private readonly System.Random _voiceRng = new System.Random();
        private readonly List<PendingBabble> _pendingBabbles = new List<PendingBabble>();
        private readonly VoiceChannel[] _babbleChannels = new VoiceChannel[2];
        private VoiceChannel _reactionChannel;
        private int _babbleCur;
        private int _babbleNpc = NpcVoiceGate.NoNpc;
        private int[] _babblePlan = System.Array.Empty<int>();
        private int _babbleIndex;
        private float _babbleNextAt;
        private NpcVoicePriority _babblePriority;
        private float _babbleBase = 1f;        // the grant's volume without the VEGA duck (crowd ×0.6, sleepy ×0.5)
        private float _babblePitchScale = 1f;  // a sleepy NPC speaks lower
        private float _babbleClipVolume;       // the playing clip's volume before the duck (to re-duck it live)
        private bool _crowd;
        private float _crowdCheckAt;           // the crowd count and the idle murmurs run twice a second

        private struct PendingBabble
        {
            public int NpcId;
            public NpcVoicePriority Priority;
            public int Length;
        }

        /// <summary>#2592: one NPC voice channel — a positional source that can fade out over
        /// <see cref="NpcVoiceGate.CutFadeSeconds"/> when the voice budget cuts it.</summary>
        private sealed class VoiceChannel
        {
            public AudioSource Src;
            private float _fadeLeft;
            private float _fadeFrom;

            public bool Fading => _fadeLeft > 0f;

            public void FadeOut()
            {
                if (Src != null && Src.isPlaying && _fadeLeft <= 0f)
                {
                    _fadeLeft = NpcVoiceGate.CutFadeSeconds;
                    _fadeFrom = Src.volume;
                }
            }

            public void StopNow()
            {
                _fadeLeft = 0f;
                if (Src != null && Src.isPlaying)
                {
                    Src.Stop();
                }
            }

            public void Tick(float dt)
            {
                if (_fadeLeft <= 0f || Src == null)
                {
                    return;
                }

                _fadeLeft -= dt;
                if (_fadeLeft <= 0f)
                {
                    Src.Stop();
                    return;
                }

                Src.volume = _fadeFrom * (_fadeLeft / NpcVoiceGate.CutFadeSeconds);
            }
        }

        // #2531: the sleep sign reads only up close, on the same 18–28 m fade as the animals' "z z z" and the nameplates.
        private const float ZzzFadeStart = 18f;
        private const float ZzzFadeEnd = 28f;

        private readonly Dictionary<int, Npc> _npcs = new Dictionary<int, Npc>();
        private bool _subscribed;

        // World time rather than frame time (#908): settlement NPCs stop walking and gesturing while the server
        // holds the world, and a greeting bubble that was up stays up for the rest of its seven seconds instead
        // of expiring behind the pause menu. Falls back to frame time before the bootstrap is wired.
        private float WorldNow => Game != null ? Game.WorldTime : Time.time;
        private float WorldDelta => Game != null ? Game.WorldDeltaTime : Time.deltaTime;

        private void Awake()
        {
            Instance = this;

            // #2592: the NPC voice channels — two babble sources (a cut one fades while the next starts) and one for reactions.
            for (int i = 0; i < _babbleChannels.Length; i++)
            {
                _babbleChannels[i] = new VoiceChannel { Src = NewVoiceSource("NpcVoice" + i) };
            }

            _reactionChannel = new VoiceChannel { Src = NewVoiceSource("NpcReaction") };
        }

        private AudioSource NewVoiceSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            return src;
        }

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.NpcsReceived += OnNpcs;
                Game.Network.WorldResetReceived += OnWorldReset;
                Game.Network.NpcGreetingReceived += OnGreeting;
                Game.Network.CraftCompleted += OnCraftCompleted;
                _subscribed = true;
            }

            TickVoices();

            int circ = Game != null ? Game.Circumference : BlocksBeyondTheStars.Shared.World.WorldConstants.Circumference;
            foreach (var n in _npcs.Values)
            {
                if (n.Pose != 0)
                {
                    n.SettledWorld = n.Target; // #1869: sitting down / lying down is a snap onto the furniture, not a glide
                    // #2666: a seated NPC still turns to a visitor or a tablemate — along the interpolated yaw (the buffer
                    // was reset when it sat down), not in one step per list.
                    n.Yaw = n.Interp.Sample(WorldNow, circ, out var seated) ? seated.Yaw * Mathf.Rad2Deg : n.TargetYaw;
                }
                else if (n.Interp.Sample(WorldNow, circ, out var drawn))
                {
                    // #2666: drawn a short delay behind the newest snapshot, along the curve through them. The old lerp
                    // toward the newest one made a stroll pulse 0.6x-1.6x five times a second, and the yaw turned in steps.
                    n.SettledWorld = new Vector3(drawn.Position.X, drawn.Position.Y, drawn.Position.Z);
                    n.Yaw = drawn.Yaw * Mathf.Rad2Deg;
                }

                var scene = Game != null ? Game.ScenePos(n.SettledWorld.x, n.SettledWorld.y, n.SettledWorld.z) : n.SettledWorld;
                if (n.Pose == 1)
                {
                    // Seated (#1869), exactly like a seated remote player (#806): the pelvis drops onto the seat.
                    n.Go.transform.position = scene + Vector3.down * 0.45f;
                    n.Go.transform.rotation = Quaternion.Euler(0f, n.Yaw, 0f);
                }
                else if (n.Pose == 2)
                {
                    // Asleep in bed (#1869): the server's position is the middle of the bed and its facing points from
                    // the foot to the head. The avatar's pivot is its feet, so they go half a body toward the foot;
                    // the root lies on its back (pitched up) with the head toward the headboard, on the mattress.
                    var headward = Quaternion.Euler(0f, n.Yaw, 0f) * Vector3.forward;
                    n.Go.transform.position = scene - headward * (0.9f * n.Go.transform.localScale.y) + Vector3.up * 0.62f;
                    n.Go.transform.rotation = Quaternion.Euler(-90f, n.Yaw + 180f, 0f);
                }
                else
                {
                    n.Go.transform.position = scene;
                    n.Go.transform.rotation = Quaternion.Euler(0f, n.AimUntil > Time.time ? n.AimYaw : n.Yaw, 0f); // #2647
                }

                UpdateZzz(n);
                UpdateLook(n);

                // Ambient work gestures: a periodic tool/arm swing so settlement NPCs look busy (miners chip
                // away often, settlers/builders place now and then, vendors gesture occasionally). Nobody swings a
                // hammer from a chair or in their sleep (#1869), and the hunter's bow only moves for a shot (#2647).
                if (n.Pose != 0 || !NpcWorkTools.SwingsPeriodically(n.Held))
                {
                    continue;
                }

                n.GestureTimer -= WorldDelta;
                if (n.GestureTimer <= 0f)
                {
                    if (n.Avatar != null && !n.Avatar.Gesturing) // #2593: no hammer blow in the middle of a wave
                    {
                        n.Avatar.Swing();
                        if (n.Held == NpcWorkTools.Axe)
                        {
                            Chop(n); // #2647
                        }
                    }

                    n.GestureTimer = Random.Range(n.GestureLo, n.GestureHi);
                }
            }
        }

        /// <summary>#2593: an NPC within <see cref="LookRange"/> of the player turns its head towards them (not in its sleep,
        /// not over its shoulder — <see cref="AvatarGestures.HeadLook"/>); out of range the head is free again.</summary>
        private void UpdateLook(Npc n)
        {
            if (n.Avatar == null)
            {
                return;
            }

            bool look = false;
            float yaw = 0f, pitch = 0f;
            if (Game != null && n.Pose != 2)
            {
                var head = n.Go.transform.position + Vector3.up * (HeadHeight(n) * n.Go.transform.localScale.y);
                var d = Game.PlayerPosition + Vector3.up * 1.5f - head;
                if (d.sqrMagnitude <= LookRange * LookRange)
                {
                    look = AvatarGestures.HeadLook(n.AimUntil > Time.time ? n.AimYaw : n.Yaw, d.x, d.y, d.z, out yaw, out pitch);
                }
            }

            if (look)
            {
                n.Avatar.SetLookYaw(yaw);
                n.Avatar.SetLookPitch(pitch);
                n.Looking = true;
            }
            else if (n.Looking)
            {
                n.Avatar.SetLookYaw(float.NaN);
                n.Avatar.SetLookPitch(0f);
                n.Looking = false;
            }
        }

        /// <summary>The head's height over the avatar root at scale 1 — a sitter's root is lowered with the pelvis, so the head
        /// keeps its height over it; a sleeper lies flat on the mattress.</summary>
        private static float HeadHeight(Npc n) => n.Pose == 2 ? 0.6f : 1.6f;

        /// <summary>Where an NPC's voice comes from: its head.</summary>
        private static Vector3 VoiceAnchor(Npc n)
            => n.Go.transform.position + Vector3.up * (HeadHeight(n) * n.Go.transform.localScale.y);

        /// <summary>A soft "z z z" floats over a sleeping NPC (#1869), the sleeping animals' sign (CreatureView).
        /// #2531: drawn depth-tested (<see cref="WorldTextMaterial"/>) so walls hide it, and faded out between 18 m and
        /// 28 m like the animals' sign and the nameplates — a sleeping village no longer shows across the map.</summary>
        private void UpdateZzz(Npc n)
        {
            if (n.Pose != 2)
            {
                if (n.Zzz != null && n.Zzz.activeSelf)
                {
                    n.Zzz.SetActive(false);
                }

                return;
            }

            if (n.Zzz == null)
            {
                var z = new GameObject("NpcZzz");
                z.transform.SetParent(transform, true);
                var tm = z.AddComponent<TextMesh>();
                tm.font = UiKit.Font;
                var mr = z.GetComponent<MeshRenderer>();
                if (mr != null && UiKit.Font != null)
                {
                    mr.sharedMaterial = WorldTextMaterial.Shared;
                }

                tm.fontSize = 48;
                tm.characterSize = 0.05f;
                tm.anchor = TextAnchor.LowerCenter;
                tm.alignment = TextAlignment.Center;
                tm.text = "z z z";
                tm.color = new Color(0.8f, 0.88f, 1f, 0.85f);
                n.Zzz = z;
                n.ZzzText = tm;
            }

            var cam = Camera.main;
            float bob = Mathf.Sin(WorldNow * 1.2f) * 0.08f;
            var headward = Quaternion.Euler(0f, n.Yaw, 0f) * Vector3.forward;
            var pos = n.Go.transform.position + headward * (1.6f * n.Go.transform.localScale.y) + Vector3.up * (0.7f + bob);
            float alpha = 1f;
            if (cam != null)
            {
                float dist = Vector3.Distance(cam.transform.position, pos);
                if (dist >= ZzzFadeEnd)
                {
                    if (n.Zzz.activeSelf)
                    {
                        n.Zzz.SetActive(false);
                    }

                    return;
                }

                if (dist > ZzzFadeStart)
                {
                    alpha = 1f - (dist - ZzzFadeStart) / (ZzzFadeEnd - ZzzFadeStart);
                }
            }

            if (!n.Zzz.activeSelf)
            {
                n.Zzz.SetActive(true);
            }

            n.Zzz.transform.position = pos;
            if (cam != null)
            {
                n.Zzz.transform.rotation = Quaternion.LookRotation(pos - cam.transform.position);
            }

            var col = new Color(0.8f, 0.88f, 1f, 0.85f * alpha);
            if (n.ZzzText != null && n.ZzzText.color != col)
            {
                n.ZzzText.color = col;
            }
        }

        /// <summary>
        /// A contextual line for an NPC (item 15; #2593: also passing greetings, welcomes, chatter, cheers, guests, alarms):
        /// show it as a speech bubble over that NPC. When the server sends an empty line (AI off/unreachable) we render a
        /// localized static fallback by kind and role, so a greeting always appears regardless of whether an LLM backend is
        /// running. A NEW bubble (<see cref="NpcGreetingTracker"/>) plays its gesture and its babble once; the AI upgrade of
        /// a bubble (same id) only swaps the text — never a second voice or wave.
        /// </summary>
        private void OnGreeting(NpcGreeting m)
        {
            if (!_npcs.TryGetValue(m.NpcId, out var n))
            {
                return; // greeting for an NPC we aren't rendering (left the area) — ignore
            }

            string raw = string.IsNullOrWhiteSpace(m.Text) ? FallbackGreeting(m.Role, n.NameKey, m.Kind) : m.Text;
            string text = NpcBubbleText.Clamp(raw); // #2605: at most 200 characters, cut at a sentence or a word
            if (text.Length == 0)
            {
                return;
            }

            var decision = _greetings.OnGreeting(m.NpcId, m.GreetingId, WorldNow, NpcBubbleText.HoldSeconds(text.Length));
            if (decision == NpcGreetingDecision.Drop)
            {
                return; // a late or overtaken upgrade — the bubble is gone or a newer one shows
            }

            n.Greeting = text;
            n.GreetingUntil = _greetings.BubbleUntil(m.NpcId);
            if (decision == NpcGreetingDecision.Upgrade)
            {
                return; // the AI's better words for the same bubble: text only
            }

            // A cheer always comes with its arms up; everything else plays the gesture the server sent (none for chatter).
            byte gesture = m.Kind == NpcGreetingKinds.Cheer && m.Gesture == NpcGestures.None ? NpcGestures.Cheer : m.Gesture;
            Gesture(m.NpcId, gesture);

            if (m.Kind == NpcGreetingKinds.Alert)
            {
                React(m.NpcId, NpcReaction.Alert); // an alarm sounds, it does not chatter
                return;
            }

            QueueBabble(m.NpcId, NpcVoiceGate.PriorityOf(m.Kind), text.Length);
        }

        /// <summary>The localized static greeting shown when no AI line is available: a passing greeting/welcome/guest line
        /// says hello (<c>npc.greet.passing</c>), else by role — a profession's own line, the quartermaster's, the guardians',
        /// the vendor's; any other resident (a settler) its own hello (<c>npc.greet.settler</c>, #2593 — it used to offer to
        /// trade like a vendor).</summary>
        private string FallbackGreeting(string role, string nameKey, byte kind)
        {
            var loc = Game?.Localizer;
            if (loc == null)
            {
                return string.Empty;
            }

            if (kind != NpcGreetingKinds.Addressed)
            {
                return loc.Get("npc.greet.passing");
            }

            // A profession (2026-09) greets in its own words: npc.role.doctor → npc.greet.doctor, when that line exists.
            if (!string.IsNullOrEmpty(nameKey) && nameKey.StartsWith("npc.role.", System.StringComparison.Ordinal))
            {
                string own = "npc.greet." + nameKey.Substring("npc.role.".Length);
                string line = loc.Get(own);
                if (!string.IsNullOrEmpty(line) && line != own)
                {
                    return line;
                }
            }

            string key = role == "quartermaster" ? "npc.greet.quartermaster"
                : role == "guardian" ? "npc.greet.guardian" // #1793
                : role == "vendor" || string.IsNullOrEmpty(role) ? "npc.greet.vendor"
                : "npc.greet.settler";
            return loc.Get(key);
        }

        // ---------------- #2592/#2593: voices, reactions, gestures (public API) ----------------

        /// <summary>
        /// #2592: NPC <paramref name="npcId"/> says a line of <paramref name="text"/> without a bubble of its own — a
        /// babble in its voice through the voice budget at <paramref name="priority"/> (requests of one frame are resolved
        /// nearest-first within a priority). Lines that show a bubble go through <see cref="NpcGreeting"/> instead.
        /// </summary>
        public void Speak(int npcId, string text, NpcVoicePriority priority)
            => QueueBabble(npcId, priority, string.IsNullOrEmpty(text) ? 0 : text.Length);

        /// <summary>
        /// #2592: a line of the NPC dialog panel (<see cref="NpcDialogUi"/>): the NPC babbles it as the addressed speaker;
        /// every line after the conversation's <paramref name="opening"/> one also comes with a nod.
        /// </summary>
        public void DialogLine(int npcId, string text, bool opening)
        {
            Speak(npcId, text, NpcVoicePriority.Addressed);
            if (!opening && _npcs.TryGetValue(npcId, out var n))
            {
                n.Avatar?.Nod();
            }
        }

        /// <summary>#2593: NPC <paramref name="npcId"/> plays a gesture (<see cref="NpcGestures"/>): a wave with the free hand
        /// (the left one while the right holds a tool), a nod, a cheer. Its work swings wait meanwhile.</summary>
        public void Gesture(int npcId, byte gesture)
        {
            if (gesture == NpcGestures.None || !_npcs.TryGetValue(npcId, out var n) || n.Avatar == null)
            {
                return;
            }

            switch (gesture)
            {
                case NpcGestures.Wave:
                    n.Avatar.Wave(leftHand: n.Held.Length > 0);
                    n.GestureTimer = Mathf.Max(n.GestureTimer, 1.5f);
                    break;
                case NpcGestures.Nod:
                    n.Avatar.Nod();
                    break;
                case NpcGestures.Cheer:
                    n.Avatar.Cheer();
                    n.GestureTimer = Mathf.Max(n.GestureTimer, 1.5f);
                    break;
            }
        }

        /// <summary>#2592: NPC <paramref name="npcId"/> plays a reaction sound (trade thanks, alarm, idle murmur) — the
        /// optional layer behind <see cref="NpcVoice.ReactionsEnabled"/>, through the voice budget (never over a babble or
        /// another reaction, within 10 m). A trade's thanks comes with a nod.</summary>
        public void React(int npcId, NpcReaction kind)
        {
            if (!_npcs.TryGetValue(npcId, out var n) || n.Go == null)
            {
                return;
            }

            if (kind == NpcReaction.Trade)
            {
                n.Avatar?.Nod();
            }

            var audio = ClientAudio.Instance;
            if (audio == null || _reactionChannel?.Src == null)
            {
                return;
            }

            string id = NpcVoice.ReactionClip(n.IsRobot, kind);
            var clip = audio.Clip(id);
            if (clip == null)
            {
                return;
            }

            float pitch = NpcVoice.ClipPitch(n.Pitch, (float)_voiceRng.NextDouble());
            float now = Time.unscaledTime;
            var grant = _voiceGate.RequestReaction(npcId, kind, now, clip.length / pitch, ListenerDistance(n), _crowd, NarratorBusy);
            if (!grant.Granted)
            {
                return;
            }

            _reactionChannel.StopNow();
            audio.PlayVoiceOn(_reactionChannel.Src, id, VoiceAnchor(n), pitch, NpcVoice.ReactionVolume(kind) * grant.Volume);
        }

        /// <summary>#2592: the head position of NPC <paramref name="npcId"/> in scene space (for markers over it), false when
        /// it is not shown.</summary>
        public bool TryGetHead(int npcId, out Vector3 scenePos)
        {
            if (_npcs.TryGetValue(npcId, out var n) && n.Go != null)
            {
                scenePos = VoiceAnchor(n);
                return true;
            }

            scenePos = default;
            return false;
        }

        /// <summary>True while VEGA's voice or a cutscene speaks — the NPCs hold back (<see cref="NpcVoiceGate"/>).</summary>
        private static bool NarratorBusy
            => NarratorVoice.AnySpeaking || GameBootstrap.CutsceneActive
               || (CutsceneDirector.Instance != null && CutsceneDirector.Instance.Playing);

        /// <summary>The NPC's distance from the audio listener (the camera; the player's position before it exists).</summary>
        private float ListenerDistance(Npc n)
        {
            var cam = Camera.main;
            var ear = cam != null ? cam.transform.position : (Game != null ? Game.PlayerPosition : Vector3.zero);
            return Vector3.Distance(ear, VoiceAnchor(n));
        }

        private void QueueBabble(int npcId, NpcVoicePriority priority, int length)
            => _pendingBabbles.Add(new PendingBabble { NpcId = npcId, Priority = priority, Length = length });

        /// <summary>Resolves this frame's babble requests — the most important first, then the nearest — against the voice
        /// budget; the first one granted speaks, the rest stay silent bubbles.</summary>
        private void FlushBabbles()
        {
            if (_pendingBabbles.Count == 0)
            {
                return;
            }

            if (_pendingBabbles.Count > 1)
            {
                var dist = new Dictionary<int, float>();
                foreach (var p in _pendingBabbles)
                {
                    dist[p.NpcId] = _npcs.TryGetValue(p.NpcId, out var pn) && pn.Go != null ? ListenerDistance(pn) : float.MaxValue;
                }

                _pendingBabbles.Sort((a, b) => a.Priority != b.Priority ? b.Priority.CompareTo(a.Priority) : dist[a.NpcId].CompareTo(dist[b.NpcId]));
            }

            foreach (var p in _pendingBabbles)
            {
                StartBabble(p);
            }

            _pendingBabbles.Clear();
        }

        private void StartBabble(PendingBabble p)
        {
            var audio = ClientAudio.Instance;
            if (audio == null || !_npcs.TryGetValue(p.NpcId, out var n) || n.Go == null)
            {
                return;
            }

            var clips = NpcVoice.Clips(n.Voice);
            var plan = NpcVoice.BabblePlan(p.Length, _voiceRng, n.LastClip);
            bool sleepy = n.Pose == 2;
            float pitchScale = sleepy ? NpcVoice.SleepyPitch : 1f;
            float seconds = 0f;
            foreach (int i in plan)
            {
                var clip = audio.Clip(clips[i]);
                seconds += (clip != null ? clip.length : NpcVoice.ClipSeconds) / Mathf.Max(0.5f, n.Pitch * pitchScale);
            }

            float now = Time.unscaledTime;
            bool narrator = NarratorBusy;
            var grant = _voiceGate.RequestBabble(p.NpcId, p.Priority, now, seconds, ListenerDistance(n), _crowd, narrator);
            if (!grant.Granted)
            {
                return; // the bubble shows, silently
            }

            if (grant.CutReaction)
            {
                _reactionChannel?.FadeOut();
            }

            // Whatever still sounds on the current channel (the cut NPC, this NPC's own previous line, a fading tail) fades
            // out there; the new line starts on the other channel.
            var current = _babbleChannels[_babbleCur];
            if (current.Fading || (current.Src != null && current.Src.isPlaying))
            {
                current.FadeOut();
                _babbleCur = 1 - _babbleCur;
            }

            _babbleChannels[_babbleCur].StopNow();
            float duck = NpcVoiceGate.NarratorDuck(p.Priority, narrator);
            _babbleNpc = p.NpcId;
            _babblePlan = plan;
            _babbleIndex = 0;
            _babblePriority = p.Priority;
            _babbleBase = (duck > 0f ? grant.Volume / duck : grant.Volume) * (sleepy ? NpcVoice.SleepyVolume : 1f);
            _babblePitchScale = pitchScale;
            _babbleNextAt = now;
            TickBabble(now); // the first clip starts at once
        }

        /// <summary>Chains the babble's clips (a new one when the last has played), keeps the channel at the speaker's head,
        /// re-ducks it while VEGA speaks and cuts it when it must not be heard any more; frees the budget at its end.</summary>
        private void TickBabble(float now)
        {
            if (_babbleNpc == NpcVoiceGate.NoNpc)
            {
                return;
            }

            var channel = _babbleChannels[_babbleCur];
            if (!_npcs.TryGetValue(_babbleNpc, out var n) || n.Go == null)
            {
                EndBabble(now, fade: true); // the speaker left the area
                return;
            }

            float duck = NpcVoiceGate.NarratorDuck(_babblePriority, NarratorBusy);
            if (duck <= 0f)
            {
                EndBabble(now, fade: true); // VEGA or a cutscene started speaking
                return;
            }

            var audio = ClientAudio.Instance;
            if (channel.Src != null)
            {
                channel.Src.transform.position = VoiceAnchor(n);
                if (channel.Src.isPlaying && !channel.Fading && audio != null)
                {
                    channel.Src.volume = Mathf.Clamp01(_babbleClipVolume * duck * audio.SfxLevel);
                }
            }

            if (now < _babbleNextAt)
            {
                return;
            }

            if (_babbleIndex >= _babblePlan.Length || audio == null)
            {
                EndBabble(now, fade: false); // said and done
                return;
            }

            int clip = _babblePlan[_babbleIndex++];
            n.LastClip = clip;
            float pitch = NpcVoice.ClipPitch(n.Pitch, (float)_voiceRng.NextDouble()) * _babblePitchScale;
            _babbleClipVolume = NpcVoice.SpeechVolume * _babbleBase;
            float length = audio.PlayVoiceOn(channel.Src, NpcVoice.Clips(n.Voice)[clip], VoiceAnchor(n), pitch, _babbleClipVolume * duck);
            _babbleNextAt = now + (length > 0f ? length / Mathf.Max(0.5f, pitch) : NpcVoice.ClipSeconds);
        }

        private void EndBabble(float now, bool fade)
        {
            if (_babbleNpc == NpcVoiceGate.NoNpc)
            {
                return;
            }

            if (fade)
            {
                _babbleChannels[_babbleCur].FadeOut();
            }

            _voiceGate.EndBabble(_babbleNpc, now);
            _babbleNpc = NpcVoiceGate.NoNpc;
            _babblePlan = System.Array.Empty<int>();
        }

        /// <summary>Per frame: the channel fades, the babble chain, and twice a second the crowd count and the idle murmurs
        /// (an NPC within 6 m, awake, no menu open — the voice budget decides the rest).</summary>
        private void TickVoices()
        {
            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;
            foreach (var channel in _babbleChannels)
            {
                channel?.Tick(dt);
            }

            _reactionChannel?.Tick(dt); // the budget frees the reaction channel by the clip's length (a cut frees it at once)
            TickBabble(now);
            if (Game == null || now < _crowdCheckAt)
            {
                return;
            }

            _crowdCheckAt = now + 0.5f;
            var player = Game.PlayerPosition;
            int around = 0;
            foreach (var n in _npcs.Values)
            {
                if (n.Go != null && (n.Go.transform.position - player).sqrMagnitude <= NpcVoiceGate.CrowdRadius * NpcVoiceGate.CrowdRadius)
                {
                    around++;
                }
            }

            _crowd = NpcVoiceGate.IsCrowd(around);
            if (!NpcVoice.ReactionsEnabled || Game.MenuOpen)
            {
                return;
            }

            foreach (var pair in _npcs)
            {
                var n = pair.Value;
                if (n.Go == null || n.Pose == 2 || now < n.NextIdleAt
                    || (n.Go.transform.position - player).sqrMagnitude > IdleMurmurRange * IdleMurmurRange)
                {
                    continue;
                }

                n.NextIdleAt = now + NpcVoice.IdleGapSeconds(n.Seed, ++n.IdleCount);
                React(pair.Key, NpcReaction.Idle);
            }
        }

        /// <summary>#2592: a finished market trade — the vendor nearest the player (within 4.5 m) nods and thanks.</summary>
        private void OnCraftCompleted(CraftResult m)
        {
            if (m == null || !m.Success || Game?.Content == null
                || Game.Content.GetRecipe(m.RecipeKey)?.Station != BlocksBeyondTheStars.Shared.Definitions.CraftingStation.Market)
            {
                return;
            }

            int best = NpcVoiceGate.NoNpc;
            float bestSq = TradeReach * TradeReach;
            foreach (var pair in _npcs)
            {
                var n = pair.Value;
                if (n.Go == null || n.Role != "vendor")
                {
                    continue;
                }

                float sq = (n.Go.transform.position - Game.PlayerPosition).sqrMagnitude;
                if (sq <= bestSq)
                {
                    bestSq = sq;
                    best = pair.Key;
                }
            }

            if (best != NpcVoiceGate.NoNpc)
            {
                React(best, NpcReaction.Trade);
            }
        }

        /// <summary>Changing world (planet ↔ station ↔ space) wipes the NPCs: each world keeps its own list with
        /// IDs that restart at 1, so without this the previous world's NPCs would linger — e.g. a space station's
        /// inhabitants left hanging in a planet's sky (B33). The new world's <see cref="NpcList"/> repopulates.</summary>
        private void OnWorldReset(WorldReset m)
        {
            foreach (var n in _npcs.Values)
            {
                if (n.Go != null)
                {
                    Destroy(n.Go);
                }

                if (n.Zzz != null)
                {
                    Destroy(n.Zzz);
                }
            }

            _npcs.Clear();

            // #2592: the new world's NPC ids restart at 1 — forget every bubble id and fall silent.
            EndBabble(Time.unscaledTime, fade: true);
            _reactionChannel?.FadeOut();
            _pendingBabbles.Clear();
            _greetings.Clear();
            _voiceGate.Reset();
            _chopGate.Reset();
        }

        private readonly HashSet<int> _seenScratch = new HashSet<int>(); // #1555: one set, not one per list

        private void OnNpcs(NpcList m)
        {
            var seen = _seenScratch;
            seen.Clear();
            // #2666: one stamp for the whole list — the server tick its positions belong to (an older server: arrival).
            bool stamped = m.ServerTime > 0;
            double at = Game != null ? Game.EntityClock.ToLocal(m.ServerTime, WorldNow) : WorldNow;
            int circ = Game != null ? Game.Circumference : BlocksBeyondTheStars.Shared.World.WorldConstants.Circumference;
            foreach (var nd in m.Npcs)
            {
                seen.Add(nd.Id);
                if (!_npcs.TryGetValue(nd.Id, out var n))
                {
                    var go = new GameObject($"NPC {nd.Role}");
                    go.transform.SetParent(transform, true); // under the game root → not leaked into menus/editors
                    go.transform.position = Game != null ? Game.ScenePos(nd.X, nd.Y, nd.Z) : new Vector3(nd.X, nd.Y, nd.Z);

                    var avatar = go.AddComponent<PlayerAvatar>();
                    // The server owns the whole palette (#711): skin covers robot chassis tones too, and
                    // trousers are an independent colour (LegsRgb 0 = older server → derive from the outfit).
                    // The fallback only fires for a server too old to send a palette; a HUMAN painted in the
                    // robot-chassis grey read as "not a person at all", so people fall back to a mid skin tone
                    // and only androids keep the grey.
                    Color skin = nd.SkinRgb != 0 ? Rgb(nd.SkinRgb)
                        : nd.IsRobot ? new Color(0.75f, 0.78f, 0.82f) : Rgb(0xC68642);
                    Color outfit = Rgb(nd.OutfitRgb);
                    Color legs = nd.LegsRgb != 0 ? Rgb(nd.LegsRgb) : outfit * 0.7f;
                    // Per-NPC variety (#711), deterministic from the NPC id + name so it survives rejoins:
                    // a face-feature jitter and a hair colour (some stay bald); androids keep the uniform
                    // manufactured look. Deliberately NO spacesuit: civilians stay bare-headed so suited
                    // players are recognisable at a glance (suit = player, bare face = NPC, mask = bandit).
                    // An authored story character (#1128) carries a FIXED face seed so they are the same
                    // person at every place they occupy; everyone else derives from id + name as before.
                    int seed = nd.FaceVariant != 0 ? nd.FaceVariant : unchecked((nd.Id * 486187739) ^ StableHash(nd.Name));
                    Color? hair = !nd.IsRobot && (seed & 0x7) != 0 ? HairTones[(int)((uint)(seed >> 8) % (uint)HairTones.Length)] : (Color?)null;
                    // #2123: no breather strip on NPCs — a civilian's own facial hair (about 65 % none since #2433) in its hair
                    // tone, an android's speaker grille. Same seed, so the face stays the same every visit.
                    var lowerFace = NpcLooks.LowerFaceFor(seed, nd.IsRobot);
                    avatar.Build(skin, outfit, outfit * 0.9f, legs, spacesuit: false, variantSeed: nd.IsRobot ? 0 : seed, hair: hair,
                        lowerFace: lowerFace);
                    if (nd.Look == "gds_guard")
                    {
                        avatar.SetGuardianLook(); // #1793: the G.D.S. machines — red stripe band, glowing eyes
                    }
                    avatar.SetVisible(true);
                    avatar.NeverAirborne = true; // #2667: NPCs never jump — a smoothed step up is not a leap

                    if (nd.Size > 0f && !Mathf.Approximately(nd.Size, 1f))
                    {
                        go.transform.localScale = Vector3.one * nd.Size;
                    }

                    var (lo, hi) = WorkCadence(nd);
                    // #2592: the voice matches the look — the same face seed picks the bank (a bearded face never speaks
                    // female, androids robotic, the guardians deep) and a stable pitch (a bigger body speaks lower).
                    bool guardian = nd.Look == "gds_guard";
                    n = new Npc
                    {
                        Go = go,
                        Avatar = avatar,
                        SettledWorld = new Vector3(nd.X, nd.Y, nd.Z),
                        GestureLo = lo,
                        GestureHi = hi,
                        GestureTimer = Random.Range(lo, hi),
                        IsRobot = nd.IsRobot,
                        Seed = seed,
                        Voice = NpcVoice.VoiceFor(seed, nd.IsRobot, guardian),
                        Pitch = NpcVoice.Pitch(seed, nd.IsRobot, nd.Size > 0f ? nd.Size : 1f),
                        NextIdleAt = Time.unscaledTime + NpcVoice.IdleGapSeconds(seed, 0) * Random.Range(0.3f, 1f),
                    };
                    _npcs[nd.Id] = n;
                    PreloadVoice(n.Voice);
                }

                n.Role = nd.Role ?? string.Empty;

                n.Target = new Vector3(nd.X, nd.Y, nd.Z);
                n.TargetYaw = nd.Facing * Mathf.Rad2Deg;
                var snapPos = new BlocksBeyondTheStars.Shared.Geometry.Vector3f(nd.X, nd.Y, nd.Z);
                var snapVel = new BlocksBeyondTheStars.Shared.Geometry.Vector3f(nd.VelX, 0f, nd.VelZ); // vertical 0: a step up eases
                n.Interp.Push(at, snapPos, snapVel, stamped, nd.Facing, 0, circ);
                n.Label = Label(nd);

                // #1869: pose, tool and activity — applied only when they change (a held mesh is rebuilt on change).
                if (nd.Pose != n.Pose)
                {
                    n.Pose = nd.Pose;
                    n.Avatar.SetSeated(nd.Pose == 1);
                    n.Avatar.SetLying(nd.Pose == 2);
                    // #2666: a pose change is a snap — the interpolation starts afresh where the NPC now is.
                    n.Interp.Reset(at, snapPos, snapVel, stamped, nd.Facing, 0);
                    if (nd.Pose == 0)
                    {
                        n.SettledWorld = n.Target; // standing up starts from where it got up, not from the mattress
                        n.Yaw = n.TargetYaw;
                    }
                }

                string held = nd.Pose == 0 ? nd.Held ?? string.Empty : string.Empty; // the tool is put away to sit or sleep
                if (held != n.Held)
                {
                    n.Held = held;
                    var (kind, tint) = HeldItem.ForNpc(held);
                    n.Avatar.SetHeldItem(kind, tint);
                    var (lo, hi) = WorkCadence(nd);
                    n.GestureLo = lo;
                    n.GestureHi = hi;
                }

                n.ActivityKey = nd.ActivityKey ?? string.Empty;
                n.NameKey = nd.NameKey ?? string.Empty;
            }

            if (_npcs.Count > seen.Count)
            {
                var stale = new List<int>();
                foreach (var id in _npcs.Keys)
                {
                    if (!seen.Contains(id))
                    {
                        stale.Add(id);
                    }
                }

                foreach (var id in stale)
                {
                    Destroy(_npcs[id].Go);
                    if (_npcs[id].Zzz != null)
                    {
                        Destroy(_npcs[id].Zzz);
                    }

                    _npcs.Remove(id);
                    _greetings.Forget(id);
                    _voiceGate.Forget(id, Time.unscaledTime); // TickBabble fades a babble whose speaker left
                }
            }
        }

        private readonly HashSet<NpcVoiceBank> _preloaded = new HashSet<NpcVoiceBank>();

        /// <summary>#2592: loads a voice bank's clips the first time an NPC with it shows up, so its first babble is not
        /// late (a browser fetches the audio data on demand).</summary>
        private void PreloadVoice(NpcVoiceBank bank)
        {
            var audio = ClientAudio.Instance;
            if (audio == null || !_preloaded.Add(bank))
            {
                return;
            }

            foreach (string id in NpcVoice.Clips(bank))
            {
                var clip = audio.Clip(id);
                if (clip != null && clip.loadState == AudioDataLoadState.Unloaded)
                {
                    clip.LoadAudioData();
                }
            }
        }

        /// <summary>How often an NPC plays a work gesture (seconds, min/max): its tool sets the pace, else its theme/role
        /// (the table lives in Client.Core since #2647 — <see cref="NpcWorkTools.Cadence"/>).</summary>
        private static (float Lo, float Hi) WorkCadence(NetNpc nd) => NpcWorkTools.Cadence(nd.Held, nd.Theme, nd.Role);

        /// <summary>#2647: the woodcutter's swing lands with a quiet chop — only while it is felling, within
        /// <see cref="NpcWorkTools.ChopRadius"/> of the listener, never two at once and never over VEGA
        /// (<see cref="NpcChopGate"/>). Heard when the blow lands, <see cref="NpcWorkTools.ChopImpactSeconds"/> into the
        /// swing, in front of the woodcutter where the trunk is.</summary>
        private void Chop(Npc n)
        {
            float volume = _chopGate.Request(Time.unscaledTime, ListenerDistance(n), n.ActivityKey, NarratorBusy);
            if (volume <= 0f || ClientAudio.Instance == null)
            {
                return;
            }

            var go = n.Go;
            float pitch = Random.Range(0.92f, 1.08f);
            FxKit.Delay(NpcWorkTools.ChopImpactSeconds, () =>
            {
                if (go != null)
                {
                    float s = go.transform.localScale.y;
                    var at = go.transform.position + (go.transform.forward * (0.8f * s)) + (Vector3.up * (1.1f * s));
                    ClientAudio.Instance?.At(NpcWorkTools.ChopClip, at, pitch, volume);
                }
            }, go);
        }

        /// <summary>
        /// #2647: hunter <paramref name="npcId"/> shoots at <paramref name="targetScene"/> (the server's arrow
        /// <c>WorldFx</c>): it turns to the target, raises its bow and lets go after
        /// <see cref="AvatarGestures.BowReleaseSeconds"/>. True when the hunter is shown standing with its bow —
        /// <paramref name="nockScene"/> is then where the arrow leaves the aimed bow; false otherwise (the caller flies the
        /// arrow from the server's spot at once).
        /// </summary>
        public bool BowShot(int npcId, Vector3 targetScene, out Vector3 nockScene)
        {
            nockScene = default;
            if (!_npcs.TryGetValue(npcId, out var n) || n.Go == null || n.Avatar == null || n.Pose != 0 || n.Held != NpcWorkTools.Bow)
            {
                return false;
            }

            var flat = targetScene - n.Go.transform.position;
            flat.y = 0f;
            n.AimYaw = flat.sqrMagnitude > 0.01f ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg : n.Yaw;
            n.AimUntil = Time.time + AvatarGestures.BowShotSeconds;
            n.Avatar.BowShot();
            n.GestureTimer = Mathf.Max(n.GestureTimer, AvatarGestures.BowShotSeconds);

            // The aimed bow: the right hand straight out at shoulder height, the arrow on the string just behind the grip.
            float s = n.Go.transform.localScale.y;
            nockScene = n.Go.transform.position + (Quaternion.Euler(0f, n.AimYaw, 0f) * new Vector3(0.32f, 1.52f, 0.92f) * s);
            return true;
        }

        private string Label(NetNpc nd)
        {
            var loc = Game?.Localizer;
            string role = loc != null ? loc.Get(nd.NameKey) : nd.Role;
            // "Name · Role" (item 12); fall back to just the role for any NPC without a coined name.
            return string.IsNullOrEmpty(nd.Name) ? role : $"{nd.Name} · {role}";
        }

        private void LateUpdate()
        {
            // #2592: this frame's babble requests (every Update has run, so all of the frame's lines are in) — nearest first.
            FlushBabbles();

            // Modern uGUI nameplates via the shared label layer (replaces IMGUI GUI.Label).
            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            var labels = ScreenLabelLayer.Instance;
            foreach (var pair in _npcs)
            {
                var n = pair.Value;

                // A known face reads as one (#1118): the relationship stage joins the nameplate. Absent
                // from the standings = stranger, which deliberately shows nothing extra.
                string label = n.Label;
                if (Game != null && Game.NpcStandings.TryGetValue(pair.Key, out string stageKey))
                {
                    // #1516: format once per (name, stage, locale) instead of once per NPC per frame.
                    var loc = Game.Localizer;
                    if (n.CachedLabel == null || n.CachedStageKey != stageKey || !ReferenceEquals(n.CachedLabelBase, n.Label) || !ReferenceEquals(n.CachedLabelLoc, loc))
                    {
                        n.CachedStageKey = stageKey;
                        n.CachedLabelBase = n.Label;
                        n.CachedLabelLoc = loc;
                        n.CachedLabel = $"{n.Label} · {loc?.Get(stageKey) ?? stageKey}";
                    }

                    label = n.CachedLabel;
                }

                // #1869: what they are doing joins the plate ("asleep", "at the counter", "on patrol").
                if (n.ActivityKey.Length > 0)
                {
                    if (!ReferenceEquals(n.CachedActivityKey, n.ActivityKey) || n.CachedActivityText == null
                        || !n.CachedActivityText.StartsWith(label, System.StringComparison.Ordinal))
                    {
                        n.CachedActivityKey = n.ActivityKey;
                        n.CachedActivityText = $"{label} · {Game?.Localizer?.Get(n.ActivityKey) ?? n.ActivityKey}";
                    }

                    label = n.CachedActivityText;
                }

                // Names only read up close: fade out between 18 m and 28 m so distant NPCs stay anonymous. A sleeper's
                // plate sits low over the bed instead of two metres over an empty mattress.
                var plateAnchor = n.Pose == 2 ? n.Go.transform.position + Vector3.up * 0.9f
                    : n.Pose == 1 ? n.Go.transform.position + Vector3.up * 1.7f
                    : n.Go.transform.position + Vector3.up * 2.1f;
                labels.World(cam, plateAnchor, label, UiKit.Cyan, false, 18f, 28f, occludeIn: Game); // #2531: not through walls

                // A live greeting bubble sits just above the nameplate (item 15). #2605: wrapped, growing upward from just
                // above the plate, so a long line no longer runs across the name.
                if (!string.IsNullOrEmpty(n.Greeting) && WorldNow < n.GreetingUntil)
                {
                    if (!ReferenceEquals(n.CachedGreetingSource, n.Greeting))
                    {
                        n.CachedGreetingSource = n.Greeting;
                        n.CachedGreetingQuoted = $"“{n.Greeting}”";
                    }

                    labels.Bubble(cam, plateAnchor + Vector3.up * 0.3f, n.CachedGreetingQuoted, UiKit.TextCol, 18f, 28f, occludeIn: Game);
                }
                else if (n.Pose != 2 && Game != null && Game.NpcWishMarks.TryGetValue(pair.Key, out byte wish))
                {
                    // #2601: a villager with a personal wish for this player — a gold "!" (asks) / "?" (you have it all) over
                    // the head, readable from further away than the name (30 m, gone at 45 m), not through walls. Hidden
                    // while they speak (the bubble sits there) and while they sleep (they only mumble).
                    labels.World(cam, plateAnchor + Vector3.up * 0.8f, wish == 2 ? "?" : "!", WishMarkGold, true, 30f, 45f,
                        occludeIn: Game, scale: 1.8f);
                }
            }
        }

        /// <summary>#2601: the colour of a villager's wish marker.</summary>
        private static readonly Color WishMarkGold = new Color(1.00f, 0.82f, 0.22f);

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.NpcsReceived -= OnNpcs;
                Game.Network.WorldResetReceived -= OnWorldReset;
                Game.Network.NpcGreetingReceived -= OnGreeting;
                Game.Network.CraftCompleted -= OnCraftCompleted;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private static Color Rgb(uint rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        /// <summary>Order-dependent string hash that is stable across sessions and machines — .NET string
        /// hash codes are randomised per process, which would re-roll every NPC's hair each login.</summary>
        private static int StableHash(string s)
        {
            int h = 17;
            foreach (char c in s ?? string.Empty)
            {
                h = unchecked(h * 31 + c);
            }

            return h;
        }

        /// <summary>Civilian hair tones — muted human colours (no reds: red accents belong to the Guardians).</summary>
        private static readonly Color[] HairTones =
        {
            new Color(0.10f, 0.09f, 0.08f), // black
            new Color(0.28f, 0.19f, 0.12f), // dark brown
            new Color(0.45f, 0.32f, 0.18f), // chestnut
            new Color(0.62f, 0.48f, 0.25f), // dark blond
            new Color(0.75f, 0.65f, 0.40f), // blond
            new Color(0.55f, 0.55f, 0.55f), // grey
            new Color(0.35f, 0.20f, 0.12f), // auburn
        };
    }
}
