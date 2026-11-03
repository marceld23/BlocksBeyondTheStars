// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>How a transition cover looks (#3140–#3142).</summary>
    public enum TransitionCoverStyle
    {
        /// <summary>A planet with air: the ship punches into the cloud layer — drifting puffs over a sky-tinted wash.</summary>
        Clouds,

        /// <summary>An airless body: the blinding glare of the retro burn (or the launch flame) fills the view.</summary>
        Glare,

        /// <summary>A station: armoured hangar doors close over the view and open again inside.</summary>
        Doors,
    }

    /// <summary>
    /// The seam of the seamless transitions (#3139–#3142): a full-screen cover that belongs to the scene — the cloud layer a
    /// landing ship punches through, the glare of a retro burn, a station's hangar doors — instead of a black loading screen.
    /// The flight view hands the picture over to the surface (or the station) behind it, and it stays shut exactly as long as
    /// the destination needs: the clouds keep drifting, the glare keeps flickering, the doors stay sealed with their light
    /// strip breathing, and the destination's name settles in as a caption.
    /// <para>Pure uGUI on its own canvas (just under the loading veil and above the HUD), no bundled art — the cloud puff,
    /// the glare streak and the door plating are drawn into small textures once. The glare honours Reduce flashes; the
    /// door clank, the hiss and the wind of the clouds come from the shared clips.</para>
    /// <para><see cref="TransitionDirector"/> drives it: <see cref="Close"/> when the flight view (or the surface) has to
    /// hand over, <see cref="Open"/> once the destination is there.</para>
    /// </summary>
    public sealed partial class TransitionCover : MonoBehaviour
    {
        public GameBootstrap Game;

        private const int PuffCount = 26;
        private const float CaptionDelay = 0.7f;   // seconds a cover must hold before the destination name comes in

        private Canvas _canvas;
        private RectTransform _root;
        private Image _wash;
        private RawImage _shade;
        private readonly RectTransform[] _puffs = new RectTransform[PuffCount];
        private readonly RawImage[] _puffImg = new RawImage[PuffCount];
        private readonly Vector2[] _puffPos = new Vector2[PuffCount];   // normalised screen position (0..1, may overshoot)
        private readonly float[] _puffDepth = new float[PuffCount];
        private readonly float[] _puffShade = new float[PuffCount];
        private RawImage _glareCore;
        private RawImage _glareStreak;
        private RawImage _glareStreak2;
        private Text _caption;
        private Text _captionSub;
        private Text _hint;
        private string _hintText = string.Empty;
        private AudioSource _wind;

        private TransitionCoverStyle _style;
        private Color _tint = Color.white;    // cloud colour / glare colour
        private Color _tint2 = Color.white;   // sky colour (the wash under the clouds)
        private float _amount;                // 0 open … 1 shut
        private float _target;
        private float _rate = 1f;             // amount per second
        private float _drift = 1f;            // +1 the clouds rush up (descending), -1 down (climbing)
        private float _held;                  // seconds fully shut
        private float _time;
        private bool _sealPlayed;

        /// <summary>True while any part of the cover shows.</summary>
        public bool Visible => _amount > 0.001f;

        /// <summary>True once the cover is fully shut — the scene behind it may change now.</summary>
        public bool Shut => _amount >= 0.999f;

        /// <summary>True while the cover is closing or shut (not opening, not open).</summary>
        public bool Closing => _target >= 1f;

        /// <summary>The style currently shown (meaningful while <see cref="Visible"/>).</summary>
        public TransitionCoverStyle Style => _style;

        /// <summary>Closes the cover over <paramref name="seconds"/>. <paramref name="tint"/> is the cloud (or glare) colour,
        /// <paramref name="sky"/> the sky behind the clouds; <paramref name="drift"/> +1 lets the clouds rush up past a
        /// descending camera, -1 down past a climbing one. <paramref name="caption"/> (and <paramref name="sub"/>) name the
        /// destination once the cover holds.</summary>
        public void Close(TransitionCoverStyle style, Color tint, Color sky, float drift, string caption, string sub, float seconds)
        {
            EnsureBuilt();
            bool restyle = !Visible || _style != style;
            _style = style;
            _tint = tint;
            _tint2 = sky;
            _drift = drift;
            _target = 1f;
            _rate = 1f / Mathf.Max(0.05f, seconds);
            _held = 0f;
            _sealPlayed = false;
            _caption.text = caption ?? string.Empty;
            _captionSub.text = sub ?? string.Empty;
            if (restyle)
            {
                ScatterPuffs();
            }

            _canvas.enabled = true;
            if (style == TransitionCoverStyle.Doors)
            {
                ClientAudio.Instance?.Cue("hangar_doors_close", 0.9f);
            }
        }

        /// <summary>Opens the cover over <paramref name="seconds"/>; <paramref name="drift"/> sets the direction the clouds part in.</summary>
        public void Open(float seconds, float drift)
        {
            if (!Visible)
            {
                return;
            }

            _drift = drift;
            _target = 0f;
            _rate = 1f / Mathf.Max(0.05f, seconds);
            if (_style == TransitionCoverStyle.Doors)
            {
                ClientAudio.Instance?.Cue("hangar_doors_open", 0.9f);
            }
        }

        /// <summary>The skip hint in the corner while a transition cinematic owns the camera (empty hides it).</summary>
        public void SetHint(string text)
        {
            EnsureBuilt();
            _hintText = text ?? string.Empty;
            _hint.text = _hintText;
            if (_hintText.Length > 0)
            {
                _canvas.enabled = true;
            }
        }

        /// <summary>Drops the cover at once (a world torn down under it).</summary>
        public void Clear()
        {
            _amount = 0f;
            _target = 0f;
            if (_canvas != null)
            {
                _canvas.enabled = false;
            }

            if (_wind != null)
            {
                _wind.volume = 0f;
            }
        }

        private void Update()
        {
            bool hint = _hintText.Length > 0;
            if (_canvas == null || (!Visible && _target <= 0f && !hint && (_hint == null || _hint.color.a <= 0f)))
            {
                return;
            }

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _time += dt;
            _amount = Mathf.MoveTowards(_amount, _target, dt * _rate);
            _held = Shut ? _held + dt : 0f;
            switch (_style)
            {
                case TransitionCoverStyle.Clouds: DrawClouds(dt); break;
                case TransitionCoverStyle.Glare: DrawGlare(); break;
                default: DrawDoors(); break;
            }

            DrawCaption();
            UpdateWind(dt);
            float hintAlpha = Mathf.MoveTowards(_hint.color.a, hint ? 0.75f : 0f, dt * 2f);
            _hint.color = new Color(0.9f, 0.95f, 1f, hintAlpha);
            if (!Visible && _target <= 0f && hintAlpha <= 0f)
            {
                _canvas.enabled = false;
            }
        }

        private void DrawCaption()
        {
            float a = Shut ? Mathf.Clamp01((_held - CaptionDelay) / 0.6f) : _target > 0f ? 0f : Mathf.Clamp01(_amount * 2f - 1f);
            a = Mathf.Min(a, _caption.color.a + 0.05f) * (string.IsNullOrEmpty(_caption.text) ? 0f : 1f);
            bool dark = _style == TransitionCoverStyle.Doors;
            var ink = dark ? new Color(0.86f, 0.95f, 1f) : new Color(0.14f, 0.18f, 0.26f);
            _caption.color = new Color(ink.r, ink.g, ink.b, a);
            _captionSub.color = new Color(ink.r, ink.g, ink.b, a * 0.75f);
        }

        private void UpdateWind(float dt)
        {
            if (_wind == null)
            {
                return;
            }

            float want = _style == TransitionCoverStyle.Clouds ? _amount * 0.55f * ClientAudio.EffectsVolume : 0f;
            _wind.volume = Mathf.MoveTowards(_wind.volume, want, dt * 1.5f);
            if (_wind.volume > 0.001f && !_wind.isPlaying)
            {
                _wind.Play();
            }
            else if (_wind.volume <= 0.001f && _wind.isPlaying)
            {
                _wind.Stop();
            }
        }

        private static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / Mathf.Max(1e-4f, b - a));
            return t * t * (3f - 2f * t);
        }

        private void OnDestroy()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }
    }
}
