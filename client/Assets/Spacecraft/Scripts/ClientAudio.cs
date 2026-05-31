using Spacecraft.Networking.Messages;
using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Procedural sound effects (M26): short tones generated in code (no bundled audio assets
    /// yet) played on gameplay events — mining/placing, craft success/failure, rejections and
    /// ship hits. Respects the master/SFX volumes from <see cref="ClientSettings"/>. Real
    /// recorded SFX + music replace these later (see CLIENT_COMPLETION_PLAN / NOTICES).
    /// </summary>
    public sealed class ClientAudio : MonoBehaviour
    {
        public GameBootstrap Game;
        public ClientSettings Settings;

        private AudioSource _src;
        private AudioClip _dig, _place, _ok, _err, _hit;
        private bool _subscribed;
        private float _lastHull = -1f;

        private void Awake()
        {
            _src = gameObject.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0f; // 2D UI/feedback sounds

            _dig = Tone(180f, 0.08f, 0.4f);
            _place = Tone(330f, 0.07f, 0.4f);
            _ok = Tone(660f, 0.12f, 0.4f);
            _err = Tone(110f, 0.20f, 0.5f);
            _hit = Tone(90f, 0.16f, 0.6f);
        }

        private void Update()
        {
            if (_subscribed || Game?.Network == null)
            {
                return;
            }

            Game.Network.BlockChanged += OnBlock;
            Game.Network.CraftCompleted += m => Play(m.Success ? _ok : _err);
            Game.Network.ActionRejected += _ => Play(_err);
            Game.Network.ShipCombatStatusChanged += OnShip;
            _subscribed = true;
        }

        private void OnBlock(BlockChanged m) => Play(m.Block == 0 ? _dig : _place); // 0 = air ⇒ mined

        private void OnShip(ShipCombatStatus s)
        {
            if (_lastHull >= 0f && s.Hull < _lastHull - 0.1f)
            {
                Play(_hit);
            }

            _lastHull = s.Hull;
        }

        private void Play(AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            float master = Settings?.MasterVolume ?? 0.8f;
            float sfx = Settings?.SfxVolume ?? 0.8f;
            _src.PlayOneShot(clip, Mathf.Clamp01(master * sfx));
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.BlockChanged -= OnBlock;
                Game.Network.ShipCombatStatusChanged -= OnShip;
            }
        }

        /// <summary>Generates a short decaying sine tone as an <see cref="AudioClip"/>.</summary>
        private static AudioClip Tone(float frequency, float duration, float volume)
        {
            const int rate = 44100;
            int samples = Mathf.CeilToInt(rate * duration);
            var clip = AudioClip.Create("tone", samples, 1, rate, false);
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Exp(-t * 12f); // quick percussive decay
                data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * volume;
            }

            clip.SetData(data, 0);
            return clip;
        }
    }
}
