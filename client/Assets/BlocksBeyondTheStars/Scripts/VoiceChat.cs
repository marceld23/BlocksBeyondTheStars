// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BlocksBeyondTheStars.Networking.Messages;
#if BBS_VOICE
using Concentus.Enums;
using Concentus.Structs;
#endif

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Live push-to-talk voice chat, layered on top of the existing text radio chat. While the push-to-talk key
    /// is held, the local microphone is captured at 48 kHz mono, split into 20 ms frames, Opus-encoded and
    /// uploaded as <see cref="VoiceFrame"/>s. The server relays them opaquely to the player's tiered radio
    /// audience (same world / system / galaxy, exactly like <see cref="ChatUi"/> text), and each incoming frame
    /// is decoded and played through a per-speaker streaming <see cref="AudioSource"/>.
    ///
    /// Voice is doubly gated: the server must opt in (<see cref="ServerRules.VoiceChatEnabled"/>) AND the player
    /// must hold a radio (server-enforced, same as chat). It is also a build-time opt-in: the Opus codec is
    /// provided by the Concentus plugin, compiled in via the <c>BBS_VOICE</c> scripting define (mirroring the
    /// <c>BBS_UWB</c> browser plugin). Without that define the component stays completely inert — no microphone
    /// access, no capture — so a default build is unchanged. See docs/developer/VOICE_CHAT.md.
    ///
    /// Privacy: audio is relayed live and never recorded or stored, by the client or the server.
    /// </summary>
    public sealed class VoiceChat : MonoBehaviour
    {
        public GameBootstrap Game;
        public ClientSettings Settings;

        // Opus works natively at 48 kHz; 20 ms frames (960 samples) balance latency against packet overhead.
        private const int SampleRate = 48000;
        private const int FrameSamples = 960;       // 20 ms @ 48 kHz
        private const float MicLoopSeconds = 1f;    // mic ring-buffer length

        // Jitter buffer: hold this many frames before a speaker starts playing, to ride out network jitter
        // (~60 ms). Larger = smoother but more delay; smaller = snappier but more dropouts.
        private const int JitterFrames = 3;
        private const int PlaybackClipSeconds = 2;  // per-speaker streaming ring buffer length

        private bool _available;                    // server enabled + codec compiled in + a mic exists
        private bool _transmitting;
        private int _sequence;

        // Players currently producing audio (for nameplate / HUD "speaking" indicators).
        private readonly HashSet<string> _speaking = new HashSet<string>();
        private readonly Dictionary<string, float> _lastFrameTime = new Dictionary<string, float>();

        // Small bottom-centre "Talking…" HUD indicator (built lazily).
        private Canvas _hud;
        private Text _indicator;
        private bool _hudBuilt;

#if BBS_VOICE
        private OpusEncoder _encoder;
        private string _micDevice;
        private AudioClip _micClip;
        private int _micReadPos;
        private readonly float[] _frameFloat = new float[FrameSamples];
        private readonly short[] _framePcm = new short[FrameSamples];
        private readonly byte[] _encodeBuffer = new byte[4000];

        private sealed class Speaker
        {
            public OpusDecoder Decoder;
            public AudioSource Source;
            public AudioClip Clip;
            public int WritePos;
            public int Buffered;   // frames written but not yet (estimated) played
            public bool Playing;
        }

        private readonly Dictionary<string, Speaker> _speakers = new Dictionary<string, Speaker>();
        private readonly float[] _decodeFloat = new float[FrameSamples];
        private readonly short[] _decodePcm = new short[FrameSamples];
#endif

        /// <summary>True while the local player is transmitting (for a HUD mic indicator).</summary>
        public bool IsTransmitting => _transmitting;

        /// <summary>True if voice is usable right now (server-enabled, codec present, mic available).</summary>
        public bool Available => _available;

        /// <summary>Whether a given player is currently speaking (for nameplate indicators).</summary>
        public bool IsSpeaking(string playerId) => _speaking.Contains(playerId);

        private void Start()
        {
            if (Game?.Network != null)
            {
                Game.Network.VoiceReceived += OnVoiceFrame;
            }
        }

        private void OnDestroy()
        {
            if (Game?.Network != null)
            {
                Game.Network.VoiceReceived -= OnVoiceFrame;
            }

            StopTransmit();

            if (_hud != null)
            {
                Destroy(_hud.gameObject);
            }
        }

        private void Update()
        {
            if (Game?.Network == null)
            {
                return;
            }

            // Voice is on when: the player hasn't switched it off (master setting), the server enabled it
            // (arrives with ServerRules after join), and the codec + a mic are present.
            bool userOn = Settings == null || Settings.VoiceEnabled;
            bool serverOn = Game.Rules != null && Game.Rules.VoiceChatEnabled;
            _available = userOn && serverOn && CodecAndMicReady();

            // Expire stale "speaking" markers (~250 ms after the last frame from a speaker).
            if (_speaking.Count > 0)
            {
                _expireScratch.Clear();
                foreach (var kv in _lastFrameTime)
                {
                    if (Time.unscaledTime - kv.Value > 0.25f)
                    {
                        _expireScratch.Add(kv.Key);
                    }
                }

                foreach (var id in _expireScratch)
                {
                    _speaking.Remove(id);
                    _lastFrameTime.Remove(id);
                }
            }

            if (!_available)
            {
                if (_transmitting)
                {
                    StopTransmit();
                }

                return;
            }

            HandlePushToTalk();
            UpdateIndicator();
#if BBS_VOICE
            if (_transmitting)
            {
                PumpMicrophone();
            }

            PumpPlayback();
#endif
        }

        private readonly List<string> _expireScratch = new List<string>();

        private void UpdateIndicator()
        {
            EnsureHud();
            if (_indicator == null)
            {
                return;
            }

            if (_transmitting)
            {
                _indicator.text = Game?.Localizer?.Get("ui.voice.talking") ?? "● Talking…";
                _indicator.enabled = true;
            }
            else
            {
                _indicator.enabled = false;
            }
        }

        private void EnsureHud()
        {
            if (_hudBuilt)
            {
                return;
            }

            _hud = UiKit.CreateCanvas("VoiceHUD");
            _hud.sortingOrder = 26; // just above the chat overlay (25)
            _indicator = UiKit.AddText(_hud.transform, 760f, 120f, 400f, 32f, string.Empty, 20,
                new Color(0.55f, 1f, 0.7f, 0.95f), TextAnchor.MiddleCenter, FontStyle.Bold);
            _indicator.enabled = false;
            _hudBuilt = true;
        }

        private bool CodecAndMicReady()
        {
#if BBS_VOICE
            // Playback works regardless; a mic is only needed to transmit. Treat "voice available" as having a
            // capture device — VoiceInputEnabled then gates whether we actually transmit (see HandlePushToTalk).
            return Microphone.devices.Length > 0;
#else
            return false; // voice requires the Concentus plugin (BBS_VOICE) — see docs/developer/VOICE_CHAT.md
#endif
        }

        private KeyCode PushToTalkKey()
        {
            string name = Settings?.PushToTalkKey;
            if (!string.IsNullOrEmpty(name) && System.Enum.TryParse(name, ignoreCase: true, out KeyCode k))
            {
                return k;
            }

            return KeyCode.V;
        }

        private void HandlePushToTalk()
        {
            // Don't capture while the player is typing in chat or a menu is open.
            bool blocked = (Game != null && (Game.ChatTyping || Game.MenuOpen))
                           || (Settings != null && !Settings.VoiceInputEnabled);
            var key = PushToTalkKey();

            if (!blocked && Input.GetKey(key))
            {
                if (!_transmitting)
                {
                    StartTransmit();
                }
            }
            else if (_transmitting)
            {
                StopTransmit();
            }
        }

        private void StartTransmit()
        {
#if BBS_VOICE
            _micDevice = string.IsNullOrEmpty(Settings?.MicrophoneDevice) ? null : Settings.MicrophoneDevice;
            _encoder ??= new OpusEncoder(SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP)
            {
                Bitrate = 20000,           // ~20 kbps voice
                UseVBR = true,
                UseDTX = true,             // skip near-silent frames
            };
            _micClip = Microphone.Start(_micDevice, true, Mathf.CeilToInt(MicLoopSeconds), SampleRate);
            _micReadPos = 0;
#endif
            _transmitting = true;
        }

        private void StopTransmit()
        {
            _transmitting = false;
#if BBS_VOICE
            if (_micClip != null)
            {
                Microphone.End(_micDevice);
                Destroy(_micClip);
                _micClip = null;
            }
#endif
        }

#if BBS_VOICE
        /// <summary>Reads any whole 20 ms frames the mic has captured since last call, encodes and uploads them.</summary>
        private void PumpMicrophone()
        {
            if (_micClip == null)
            {
                return;
            }

            int micPos = Microphone.GetPosition(_micDevice);
            int total = _micClip.samples;

            // Number of new samples available (handles ring-buffer wrap).
            int available = micPos - _micReadPos;
            if (available < 0)
            {
                available += total;
            }

            while (available >= FrameSamples)
            {
                _micClip.GetData(_frameFloat, _micReadPos); // mono clip → FrameSamples floats from read head
                for (int i = 0; i < FrameSamples; i++)
                {
                    _framePcm[i] = (short)Mathf.Clamp((int)(_frameFloat[i] * short.MaxValue), short.MinValue, short.MaxValue);
                }

                int bytes = _encoder.Encode(_framePcm, 0, FrameSamples, _encodeBuffer, 0, _encodeBuffer.Length);
                if (bytes > 0)
                {
                    var opus = new byte[bytes];
                    System.Buffer.BlockCopy(_encodeBuffer, 0, opus, 0, bytes);
                    Game.Network.SendVoice(opus, unchecked(_sequence++));
                }

                _micReadPos = (_micReadPos + FrameSamples) % total;
                available -= FrameSamples;
            }
        }
#endif

        private void OnVoiceFrame(VoiceFrame frame)
        {
            if (frame == null || string.IsNullOrEmpty(frame.FromPlayerId) || frame.Opus == null || frame.Opus.Length == 0)
            {
                return;
            }

            // Respect the master voice switch for playback too (not just capture).
            if (Settings != null && !Settings.VoiceEnabled)
            {
                return;
            }

            // Local mute + don't play our own (the server already excludes the sender, but be defensive).
            if (frame.FromPlayerId == Game.LocalPlayerId)
            {
                return;
            }

            if (Settings != null && Settings.MutedVoicePlayers != null && Settings.MutedVoicePlayers.Contains(frame.FromPlayerId))
            {
                return;
            }

            _speaking.Add(frame.FromPlayerId);
            _lastFrameTime[frame.FromPlayerId] = Time.unscaledTime;

#if BBS_VOICE
            EnqueueForPlayback(frame);
#endif
        }

#if BBS_VOICE
        private void EnqueueForPlayback(VoiceFrame frame)
        {
            if (!_speakers.TryGetValue(frame.FromPlayerId, out var sp))
            {
                sp = CreateSpeaker(frame.FromPlayerId);
                _speakers[frame.FromPlayerId] = sp;
            }

            int decoded = sp.Decoder.Decode(frame.Opus, 0, frame.Opus.Length, _decodePcm, 0, FrameSamples, false);
            if (decoded <= 0)
            {
                return;
            }

            for (int i = 0; i < decoded; i++)
            {
                _decodeFloat[i] = _decodePcm[i] / (float)short.MaxValue;
            }

            // Write into the speaker's streaming ring buffer at the moving write head.
            sp.Clip.SetData(_decodeFloat, sp.WritePos);
            sp.WritePos = (sp.WritePos + decoded) % sp.Clip.samples;
            sp.Buffered++;

            // Start playback once a small jitter buffer has accumulated.
            if (!sp.Playing && sp.Buffered >= JitterFrames)
            {
                sp.Source.Play();
                sp.Playing = true;
            }
        }

        private Speaker CreateSpeaker(string playerId)
        {
            var go = new GameObject($"Voice:{playerId}");
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.loop = true;
            src.spatialBlend = 0f; // flat 2D in v1 (no positional voice yet)
            src.clip = AudioClip.Create($"voice_{playerId}", SampleRate * PlaybackClipSeconds, 1, SampleRate, false);

            return new Speaker
            {
                Decoder = new OpusDecoder(SampleRate, 1),
                Source = src,
                Clip = src.clip,
            };
        }

        /// <summary>Per-frame playback upkeep: applies the voice volume bus and recycles idle speakers.</summary>
        private void PumpPlayback()
        {
            if (_speakers.Count == 0)
            {
                return;
            }

            float vol = Mathf.Clamp01((Settings?.VoiceVolume ?? 1f) * (Settings?.MasterVolume ?? 1f));
            _recycleScratch.Clear();
            foreach (var kv in _speakers)
            {
                kv.Value.Source.volume = vol;

                // No fresh frames for a while → stop and tear the speaker down so it can re-buffer cleanly.
                if (kv.Value.Playing && _lastFrameTime.TryGetValue(kv.Key, out var t) && Time.unscaledTime - t > 0.5f)
                {
                    _recycleScratch.Add(kv.Key);
                }
                else if (!_lastFrameTime.ContainsKey(kv.Key))
                {
                    _recycleScratch.Add(kv.Key);
                }
            }

            foreach (var id in _recycleScratch)
            {
                if (_speakers.TryGetValue(id, out var sp))
                {
                    if (sp.Source != null)
                    {
                        Destroy(sp.Source.gameObject);
                    }

                    _speakers.Remove(id);
                }
            }
        }

        private readonly List<string> _recycleScratch = new List<string>();
#endif
    }
}
