// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The escort cruiser's mega laser (#3900) — a hostile's heavy shot (#3756) whose <c>SpaceHeavyShot.Style</c> is
    /// <c>mega_laser</c>; the classic heavy shot stays in SpaceView.Maneuvers.cs untouched.
    /// <list type="bullet">
    /// <item><b>Charge</b>: a glow gathers at the barrel's muzzle and a thin pulsing aiming line runs from there to the
    /// targeted ship, following both — every pilot sees it, the targeted one strongest (<see cref="MegaLaserAim"/>). That
    /// pilot also gets the warning line with a countdown (the heavy shot's warning widget) and hears the charge as a 2D
    /// cue.</item>
    /// <item><b>Hit</b>: one thick beam from the muzzle to the ship, the hit on the shield or the hull, a camera jolt
    /// (<see cref="FxCamera"/> honours the shake and camera-motion settings).</item>
    /// <item><b>Miss</b>: the beam ends on the rock the pilot hid behind (<c>Blocked</c>), stops short when the ship flew
    /// out of its reach, or passes the ship that rolled away and runs on into space.</item>
    /// </list>
    /// The server decides all of it; this draws it.
    /// </summary>
    public sealed partial class SpaceView
    {
        private static readonly Color MegaCol = new Color(1f, 0.22f, 0.14f);

        private int _megaRun; // counts up with every charge, shot and closed flight: an older charge's picture ends
        private string _megaEntityId = string.Empty;
        private string _megaTargetId = string.Empty;
        private float _megaUntil;     // on the world clock: the barrel points at the charge's target until then
        private float _megaWarnUntil; // on the world clock: the warning's countdown runs until then
        private string _megaWarnText = string.Empty;
        private int _megaWarnShown = -1;

        /// <summary>The server announced a mega laser's charge, or fired it.</summary>
        private void OnMegaLaser(SpaceHeavyShot m)
        {
            bool atMe = m.TargetPlayerId == Game.LocalPlayerId;
            if (m.Phase == SpaceCombatRules.HeavyPhaseCharge)
            {
                BeginMegaLaserCharge(m, atMe, Mathf.Clamp(m.Seconds, 0.2f, 8f));
            }
            else
            {
                FireMegaLaser(m, atMe);
            }
        }

        /// <summary>A point of the instance's flight coordinates in the world (the scene root is the instance's frame).</summary>
        private Vector3 FlightPoint(float x, float y, float z) => _root.transform.TransformPoint(new Vector3(x, y, z));

        /// <summary>Where the mega laser leaves the cruiser: the barrel's muzzle of the boss model, the centre of a cruiser
        /// drawn without one, null once the cruiser is gone (destroyed, or out of the scene).</summary>
        private Vector3? MegaMuzzle(string entityId)
        {
            var rig = EscortRig(entityId);
            if (rig != null && rig.gameObject.activeSelf)
            {
                return rig.Muzzle;
            }

            return entityId != null && _entities.TryGetValue(entityId, out var go) && go != null && go.activeSelf
                ? go.transform.position
                : (Vector3?)null;
        }

        private void BeginMegaLaserCharge(SpaceHeavyShot m, bool atMe, float seconds)
        {
            int run = ++_megaRun;
            string entityId = m.EntityId ?? string.Empty;
            string targetId = m.TargetPlayerId ?? string.Empty;
            _megaEntityId = entityId;
            _megaTargetId = targetId;
            _megaUntil = Game.WorldTime + seconds;

            // The aim follows the targeted ship until it fires; the point the server named is the start, and the fallback
            // for a ship this client does not draw.
            Vector3 aimedAt = FlightPoint(m.TargetX, m.TargetY, m.TargetZ);
            Vector3? muzzle = MegaMuzzle(entityId);
            if (muzzle != null)
            {
                SpaceCombatFx.MegaLaserCharge(
                    () => run == _megaRun && _root != null ? MegaMuzzle(entityId) : (Vector3?)null,
                    () => TryHeavyTarget(targetId, atMe, out var ship) ? ship.position : aimedAt,
                    seconds, MegaCol, atMe);
            }

            var audio = ClientAudio.Instance;
            if (audio != null)
            {
                string cue = audio.Has("mega_laser_charge") ? "mega_laser_charge"
                    : audio.Has("heavy_shot_charge") ? "heavy_shot_charge" : "weapon_charge";
                if (atMe)
                {
                    audio.Cue(cue, 1f); // the pilot it is aimed at hears the warning whatever the distance
                }
                else
                {
                    audio.At(cue, muzzle ?? FlightPoint(m.OriginX, m.OriginY, m.OriginZ), 1f, 1f);
                }
            }

            if (atMe && !_eva)
            {
                ShowMegaLaserWarning(seconds);
            }
        }

        private void FireMegaLaser(SpaceHeavyShot m, bool atMe)
        {
            Vector3 from = MegaMuzzle(m.EntityId ?? string.Empty) ?? FlightPoint(m.OriginX, m.OriginY, m.OriginZ);
            _megaRun++; // the charge's glow and its aiming line end here
            _megaUntil = 0f;
            if (atMe)
            {
                _heavyWarnUntil = 0f;
                _megaWarnUntil = 0f;
            }

            bool hit = m.Phase == SpaceCombatRules.HeavyPhaseHit;
            bool known = TryHeavyTarget(m.TargetPlayerId, atMe, out var ship);
            Vector3 aimedAt = FlightPoint(m.TargetX, m.TargetY, m.TargetZ);
            bool onRock = !hit && m.Blocked;
            Vector3 to;
            if (hit)
            {
                to = known ? ship.position : aimedAt;
            }
            else
            {
                to = onRock ? aimedAt : MegaMissEnd(from, known ? ship.position : aimedAt, m.Range, atMe);
            }

            SpaceCombatFx.MegaLaserBeam(from, to, MegaCol, onRock);
            var audio = ClientAudio.Instance;
            if (audio != null)
            {
                string cue = audio.Has("mega_laser_fire") ? "mega_laser_fire"
                    : audio.Has("heavy_shot_fire") ? "heavy_shot_fire" : "ship_weapon";
                audio.At(cue, from, 1f, 1f);
            }

            // The jolt: a real one for the pilot it hit, a faint one for whoever watches from near the barrel.
            float away = Camera != null ? Vector3.Distance(Camera.transform.position, from) : 400f;
            FxCamera.AddTrauma(hit && atMe ? 0.35f : Mathf.Clamp01(0.2f - (away / 400f)));
            if (hit && atMe && known)
            {
                HeavyImpactOnOwnShip(ship.position, (to - from).normalized, MegaCol);
            }
        }

        /// <summary>Where a missed mega laser runs to. A ship beyond the shot's reach: the beam stops short at its reach.
        /// Else it passes the ship it was meant for — the own one on the side it rolled away from, like the classic heavy
        /// shot — and runs on into space.</summary>
        private Vector3 MegaMissEnd(Vector3 from, Vector3 past, float reach, bool atMe)
        {
            Vector3 dir = past - from;
            float distance = dir.magnitude;
            dir = distance > 0.01f ? dir / distance : Vector3.forward;
            if (reach > 0f && distance > reach)
            {
                return from + (dir * reach);
            }

            Vector3 across = Vector3.Cross(dir, Vector3.up);
            across = across.sqrMagnitude > 1e-4f ? across.normalized : Vector3.right;
            float side = atMe && _roll.Direction != 0 ? -_roll.Direction * 8f : 8f;
            return past + (across * side) + (dir * 90f);
        }

        /// <summary>The warning for the pilot the mega laser is aimed at, on the heavy shot's warning widget: the line and
        /// the seconds left.</summary>
        private void ShowMegaLaserWarning(float seconds)
        {
            BuildManeuverHud();
            if (_heavyWarn == null)
            {
                return;
            }

            var loc = Game.Localizer;
            _megaWarnText = loc != null ? HudUi.ExpandKeyTokens(loc, loc.Get("ui.space.mega_laser_warn")) : "Mega laser charging — roll!";
            _megaWarnShown = -1;
            _megaWarnUntil = Game.WorldTime + seconds;
            _heavyWarnUntil = _megaWarnUntil;
            UpdateMegaLaserWarning();
        }

        /// <summary>Per frame while the warning shows: the countdown beside the line (the text changes once a second, not
        /// every frame). The widget is the heavy shot's — once that sets another end time, the widget is its again.</summary>
        private void UpdateMegaLaserWarning()
        {
            if (_heavyWarn == null || _megaWarnUntil <= 0f || _megaWarnUntil != _heavyWarnUntil)
            {
                return;
            }

            int left = MegaLaserAim.Countdown(_megaWarnUntil - Game.WorldTime);
            if (left == _megaWarnShown)
            {
                return;
            }

            _megaWarnShown = left;
            _heavyWarn.text = left > 0 ? _megaWarnText + "   " + left.ToString(CultureInfo.InvariantCulture) : _megaWarnText;
        }
    }
}
