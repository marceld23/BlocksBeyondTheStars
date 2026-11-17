// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Feel;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The pilot's two manoeuvres at the helm (#3753, #3754, #3755) and what they are for (#3756):
    /// <list type="bullet">
    /// <item>the <b>barrel roll</b> — the picture (<see cref="ShipRollState"/>: a full turn about the nose with a sideways
    /// slide), the client's own cooldown clock and the intent to the server, which decides whether it evades;</item>
    /// <item>the <b>boost</b> — the same <see cref="ShipBoostMeter"/> the server runs, so the ship is exactly as fast as
    /// the server allows; the keyboard holds it, the pad and the tablet switch it on;</item>
    /// <item>their <b>gauge</b> above the flight instruments;</item>
    /// <item>a hostile's <b>heavy shot</b>: the charge the server announces (the warning to roll) and the beam that
    /// hits or misses.</item>
    /// </list>
    /// </summary>
    public sealed partial class SpaceView
    {
        /// <summary>The boost is on — the touch layer lights its BOOST button by it.</summary>
        public static bool BoostLit;

        private ShipRollState _roll;
        private ShipBoostMeter _boost;
        private ShipManeuverStats _maneuver = ShipManeuverStats.Default;
        private bool _boostLatched;    // the pad's / the tablet's switch
        private bool _boostSpent;      // ran dry while still asked for: a new boost needs a new press
        private bool _boostWant;       // what the helm asked for this frame
        private bool _boostSent;       // what the server was told last
        private int _maneuverFrame = -10;
        private float _slideShare;     // the share of the roll's slide to move this frame
        private Vector3 _slideStep;    // … as the step FlightStep added
        private Vector3 _thrustVel;    // the ship's own velocity before that step

        private GameObject _maneuverHud;
        private Image _boostFill, _rollFill;
        private Text _boostCaption, _rollCaption;
        private Text _heavyWarn;
        private float _heavyWarnUntil;

        private static readonly Color BoostCol = new Color(1f, 0.72f, 0.25f, 0.95f);
        private static readonly Color RollCol = new Color(0.45f, 0.9f, 1f, 0.95f);
        private static readonly Color GaugeBack = new Color(0.03f, 0.07f, 0.13f, 0.75f);
        private static readonly Color HeavyCol = new Color(1f, 0.3f, 0.18f);

        /// <summary>The factor on the ship's top speed right now.</summary>
        private float BoostFactor => _boost.Active ? _maneuver.BoostPower : 1f;

        /// <summary>Whether hostile fire misses the ship right now (the roll's evade window) — the bolts are drawn past it.</summary>
        private bool Evading => _roll.Evading;

        /// <summary>A new flight: a full charge, no roll, nothing asked for.</summary>
        private void ResetManeuvers()
        {
            ReadManeuverStats();
            _roll.Reset();
            _boost.Fill(_maneuver);
            _boostLatched = _boostSpent = _boostWant = _boostSent = false;
            _slideShare = 0f;
            _slideStep = Vector3.zero;
            BoostLit = false;
        }

        /// <summary>The flown ship's values as the server sent them (an older server sends none: the defaults).</summary>
        private void ReadManeuverStats()
        {
            var c = Game != null ? Game.ShipCombat : null;
            _maneuver = c != null && c.BoostSeconds > 0f
                ? new ShipManeuverStats(Mathf.Max(1f, c.BoostPower), c.BoostSeconds, Mathf.Max(0.1f, c.BoostRecharge), Mathf.Max(0.5f, c.RollCooldown))
                : ShipManeuverStats.Default;
            _boost.Charge = Mathf.Min(_boost.Charge, _maneuver.BoostSeconds);
        }

        /// <summary>The helm's frame: reads the two controls. Called before <see cref="FlightStep"/>, which flies by
        /// what this decides.</summary>
        private void UpdateManeuvers(float fwd, float strafe)
        {
            _maneuverFrame = Time.frameCount;
            ReadManeuverStats();
            bool thrusting = Mathf.Abs(fwd) + Mathf.Abs(strafe) > 0.05f;
            bool keyHeld = InputMap.BoostKeyHeld();
            if (InputMap.BoostToggleDown() && thrusting)
            {
                _boostLatched = !_boostLatched;
                _boostSpent = false;
            }

            if (!thrusting || _autopilot)
            {
                _boostLatched = false;
            }

            if (!keyHeld && !_boostLatched)
            {
                _boostSpent = false; // let go: the next press may boost again
            }

            _boostWant = !_autopilot && thrusting && !_boostSpent && (keyHeld || _boostLatched);

            if (!_autopilot && InputMap.Down(InputAction.FlightRoll) && _roll.TryStart(strafe < -0.3f ? -1 : 1, _maneuver.RollCooldown))
            {
                Game.Network?.SendShipRoll(_roll.Direction > 0);
                ClientAudio.Instance?.Cue(ClientAudio.Instance.Has("ship_roll") ? "ship_roll" : "rcs_puff", 1f);
                FxCamera.Kick(1.1f); // also the pad's rumble
            }

            _slideShare = _roll.Tick(Time.deltaTime);
        }

        /// <summary>Every frame of the flight view, after the helm (if it steered): the boost charge drains or refills,
        /// the server hears when the boost goes on or off, and the gauge follows.</summary>
        private void TickManeuvers()
        {
            float dt = Time.deltaTime;
            bool helm = _maneuverFrame == Time.frameCount;
            if (!helm)
            {
                _roll.Tick(dt);   // the cooldown runs on behind a menu
                _slideShare = 0f;
                _boostLatched = false;
            }

            bool was = _boost.Active;
            _boost.Tick(dt, helm && _boostWant, _maneuver);
            if (was && !_boost.Active && _boostWant)
            {
                _boostSpent = true; // ran dry while held
                _boostLatched = false;
            }

            if (_boost.Active != _boostSent)
            {
                _boostSent = _boost.Active;
                Game.Network?.SendShipBoost(_boostSent);
                if (_boostSent)
                {
                    FxCamera.FovPunch(5f);
                    FxCamera.Kick(0.7f);
                    ClientAudio.Instance?.Cue(ClientAudio.Instance.Has("ship_boost") ? "ship_boost" : "engine_ignite", 1f);
                }
            }

            if (_boost.Active)
            {
                FxCamera.FovPunch(4f); // holds the wider view while the boost burns (a punch only ever grows to its value)
            }

            BoostLit = _boost.Active;
            UpdateManeuverHud();
        }

        /// <summary>The roll's sideways slide this frame, in the flight space.</summary>
        private Vector3 RollSlide(Quaternion heading)
        {
            _slideStep = _slideShare > 0f
                ? heading * Vector3.right * (_roll.Direction * _slideShare * ShipManeuverRules.RollSlideDistance)
                : Vector3.zero;
            return _slideStep;
        }

        /// <summary>The slide is no speed of the ship's own: after the hull, rock and keep-out rules moved the position,
        /// as much of the slide as really happened is taken out of the velocity again — the ship does not coast on
        /// sideways after a roll, and a roll against a wall does not bounce back.</summary>
        private void SettleRollSlide(float dt)
        {
            if (_slideStep.sqrMagnitude <= 1e-10f || dt <= 0f)
            {
                return;
            }

            var dir = _slideStep.normalized;
            float slideSpeed = _slideStep.magnitude / dt;
            float extra = Vector3.Dot(_flightVel, dir) - Vector3.Dot(_thrustVel, dir);
            _flightVel -= dir * Mathf.Clamp(extra, 0f, slideSpeed);
            _slideStep = Vector3.zero;
        }

        // ---------------------------------------------------------------- the gauge

        private void BuildManeuverHud()
        {
            if (_maneuverHud != null || _ui == null)
            {
                return;
            }

            _maneuverHud = new GameObject("Maneuvers", typeof(RectTransform));
            _maneuverHud.transform.SetParent(_ui.transform, false);
            var rt = _maneuverHud.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(320f, 22f);
            rt.anchoredPosition = new Vector2(0f, 188f); // right above the SPD line
            _boostFill = GaugeBar(_maneuverHud.transform, -84f, 150f, BoostCol, out _boostCaption);
            _rollFill = GaugeBar(_maneuverHud.transform, 84f, 150f, RollCol, out _rollCaption);
            RefreshGaugeCaptions();

            var warnGo = new GameObject("HeavyWarn", typeof(RectTransform));
            warnGo.transform.SetParent(_ui.transform, false);
            var wrt = warnGo.GetComponent<RectTransform>();
            wrt.anchorMin = wrt.anchorMax = new Vector2(0.5f, 0.5f);
            wrt.pivot = new Vector2(0.5f, 0.5f);
            wrt.sizeDelta = new Vector2(900f, 40f);
            wrt.anchoredPosition = new Vector2(0f, 150f);
            _heavyWarn = warnGo.AddComponent<Text>();
            _heavyWarn.font = UiKit.Font;
            _heavyWarn.fontSize = 26;
            _heavyWarn.fontStyle = FontStyle.Bold;
            _heavyWarn.color = HeavyCol;
            _heavyWarn.alignment = TextAnchor.MiddleCenter;
            _heavyWarn.horizontalOverflow = HorizontalWrapMode.Overflow;
            _heavyWarn.raycastTarget = false;
            warnGo.SetActive(false);
        }

        /// <summary>The captions name the manoeuvre and its control for the device in hand — "BOOST  Shift", "ROLL  (A)" —
        /// so the two long control lines at the bottom need no room for them. On a tablet the buttons carry the names.</summary>
        private void RefreshGaugeCaptions()
        {
            if (_boostCaption == null || _rollCaption == null)
            {
                return;
            }

            SetCaption(_boostCaption, GaugeCaption("ui.space.boost", "BOOST", "{key:FlightBoost}"));
            SetCaption(_rollCaption, GaugeCaption("ui.space.roll", "ROLL", "{key:FlightRoll}"));
        }

        private string GaugeCaption(string nameKey, string fallback, string keyToken)
        {
            string name = Loc(nameKey, fallback);
            var loc = Game.Localizer;
            if (loc == null || InputMap.ActiveDevice == InputDeviceKind.Touch)
            {
                return name;
            }

            string control = HudUi.ExpandKeyTokens(loc, keyToken);
            return string.IsNullOrEmpty(control) || control.IndexOf('{') >= 0 ? name : name + "  " + control;
        }

        private static void SetCaption(Text label, string text)
        {
            if (label.text != text)
            {
                label.text = text;
            }
        }

        /// <summary>One slim bar with its caption above it; returns the fill.</summary>
        private static Image GaugeBar(Transform parent, float x, float width, Color color, out Text caption)
        {
            var go = new GameObject("Gauge", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(width, 22f);
            rt.anchoredPosition = new Vector2(x, 0f);

            var back = NewGaugeImage(go.transform, width, GaugeBack);
            back.raycastTarget = false;
            var fill = NewGaugeImage(go.transform, width, color);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;

            var label = new GameObject("Caption", typeof(RectTransform));
            label.transform.SetParent(go.transform, false);
            var lrt = label.GetComponent<RectTransform>();
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.sizeDelta = new Vector2(width, 14f);
            lrt.anchoredPosition = new Vector2(0f, 8f);
            var text = label.AddComponent<Text>();
            text.font = UiKit.Font;
            text.fontSize = 12;
            text.fontStyle = FontStyle.Bold;
            text.color = new Color(color.r, color.g, color.b, 0.9f);
            text.alignment = TextAnchor.LowerCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            caption = text;
            return fill;
        }

        private static Image NewGaugeImage(Transform parent, float width, Color color)
        {
            var go = new GameObject("Bar", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(width, 6f);
            rt.anchoredPosition = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.sprite = UiKit.SolidSprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private void UpdateManeuverHud()
        {
            BuildManeuverHud();
            if (_maneuverHud == null)
            {
                return;
            }

            bool show = _phase == Phase.Cruise && !_eva && !Game.MenuOpen && Camera != null;
            if (_maneuverHud.activeSelf != show)
            {
                _maneuverHud.SetActive(show);
            }

            bool warn = show && Time.time < _heavyWarnUntil;
            if (_heavyWarn.gameObject.activeSelf != warn)
            {
                _heavyWarn.gameObject.SetActive(warn);
            }

            if (!show)
            {
                return;
            }

            if (Time.frameCount % 30 == 0)
            {
                RefreshGaugeCaptions(); // the device in hand or a binding may have changed
            }

            _boostFill.fillAmount = _maneuver.BoostSeconds > 0f ? Mathf.Clamp01(_boost.Charge / _maneuver.BoostSeconds) : 0f;
            // Below the share a boost needs to start the bar dims: pressing the key now does nothing yet.
            bool canStart = _boost.Active || _boost.Charge >= _maneuver.BoostSeconds * ShipManeuverRules.BoostStartShare;
            _boostFill.color = canStart ? BoostCol : new Color(BoostCol.r, BoostCol.g, BoostCol.b, 0.4f);
            float ready = _roll.Readiness(_maneuver.RollCooldown);
            _rollFill.fillAmount = ready;
            _rollFill.color = ready >= 1f ? RollCol : new Color(RollCol.r, RollCol.g, RollCol.b, 0.45f);
            if (warn)
            {
                float k = UiKit.ReducedMotion ? 1f : 0.55f + (0.45f * Mathf.PingPong(Time.time * 5f, 1f));
                _heavyWarn.color = new Color(HeavyCol.r, HeavyCol.g, HeavyCol.b, k);
            }
        }

        // ---------------------------------------------------------------- a hostile's heavy shot (#3756)

        /// <summary>The server announced a heavy shot's charge, or fired it.</summary>
        private void OnHeavyShot(SpaceHeavyShot m)
        {
            if (!_active || _root == null || m == null || !_entities.TryGetValue(m.EntityId, out var hostile) || hostile == null)
            {
                return;
            }

            bool atMe = m.TargetPlayerId == Game.LocalPlayerId;
            var from = hostile.transform;
            if (m.Phase == SpaceCombatRules.HeavyPhaseCharge)
            {
                ShowHeavyCharge(from, Mathf.Clamp(m.Seconds, 0.2f, 6f), atMe);
                return;
            }

            _heavyWarnUntil = atMe ? 0f : _heavyWarnUntil;
            if (!TryHeavyTarget(m.TargetPlayerId, atMe, out var target))
            {
                return;
            }

            bool hit = m.Phase == SpaceCombatRules.HeavyPhaseHit;
            Vector3 dir = (target.position - from.position).normalized;
            // A miss passes the ship on the side it rolled away from and runs on into space.
            Vector3 end = hit
                ? target.position
                : target.position + (Vector3.Cross(dir, Vector3.up).normalized * (atMe ? -_roll.Direction * 5f : 5f)) + (dir * 60f);
            SpaceCombatFx.HeavyBeam(from.position, end, HeavyCol);
            ClientAudio.Instance?.At(ClientAudio.Instance.Has("heavy_shot_fire") ? "heavy_shot_fire" : "ship_weapon", from.position, 0.7f, 1f);
            if (!hit || !atMe)
            {
                return;
            }

            var status = Game.ShipCombat;
            float shieldFrac = status != null && status.ShieldMax > 0f ? status.Shield / status.ShieldMax : 0f;
            var at = target.position - (dir * ShipLocalBounds().extents.magnitude * 0.6f);
            if (_shield != null && shieldFrac > 0.01f)
            {
                _shield.Hit(at, shieldFrac);
                SpaceCombatFx.ShieldDeflect(at, -dir, HeavyCol);
            }
            else
            {
                HullHit(at, 1f);
            }
        }

        /// <summary>The charge: the glow gathering at the hostile, its sound, and — aimed at this pilot — the warning.</summary>
        private void ShowHeavyCharge(Transform from, float seconds, bool atMe)
        {
            SpaceCombatFx.HeavyCharge(() => from != null ? from.position : Vector3.zero, seconds, HeavyCol);
            ClientAudio.Instance?.At(ClientAudio.Instance.Has("heavy_shot_charge") ? "heavy_shot_charge" : "weapon_charge", from.position, 0.8f, 1f);
            if (!atMe || _eva)
            {
                return;
            }

            BuildManeuverHud();
            if (_heavyWarn != null)
            {
                var loc = Game.Localizer;
                _heavyWarn.text = loc != null ? HudUi.ExpandKeyTokens(loc, loc.Get("ui.space.heavy_warn")) : "Heavy shot incoming — roll!";
                _heavyWarnUntil = Time.time + seconds;
            }
        }

        /// <summary>The ship a heavy shot is aimed at: the own one, or another pilot's.</summary>
        private bool TryHeavyTarget(string playerId, bool atMe, out Transform target)
        {
            target = null;
            if (atMe)
            {
                target = _ship != null ? _ship.transform : null;
            }
            else if (playerId != null && _remotePlayers.TryGetValue(playerId, out var av) && av.Root != null)
            {
                target = av.Root.transform;
            }

            return target != null;
        }
    }
}
