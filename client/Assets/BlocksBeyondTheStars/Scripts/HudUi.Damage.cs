// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The damage feedback of <see cref="HudUi"/>: the red flash and the cause label, the damage direction marker with
    /// its height chevron, the camera / pad shake and the low-oxygen alarm.
    /// </summary>
    public sealed partial class HudUi
    {
        // Damage feedback (B21): a red screen flash + a cause label when health drops.
        private Image _dmgFlash;
        private RectTransform _dmgDir;   // #2154: a red marker on a ring around the crosshair, pointing at the threat
        private Image _dmgDirImg;
        private Image _dmgDirArrow;      // #2564: the chevron beside it when the attacker is clearly above or below
        private float _dmgDirTimer;
        private Vector3 _dmgDirFrom;
        private string _dmgDirEntity = string.Empty; // #2564: the attacker the marker follows (empty: a fixed spot)
        private const float DamageDirSeconds = 2.2f;     // #2564: how long the marker shows (was 1.4 s)…
        private const float DamageDirFadeSeconds = 0.8f; // …fading out over its last part
        private TMP_Text _dmgCause;
        private float _flashTimer, _causeTimer;
        private readonly DamageFeedback _damageFeedback = new DamageFeedback(); // #2518: sums the 15 Hz health drops into one effect per half second
        private string _causeKey = string.Empty;
        private float _o2BeepTimer; // periodic low-oxygen warning tone (interval shrinks as O₂ drops)

        /// <summary>Tints the screen red + names the cause whenever the player's health drops (B21), so
        /// environmental damage (lava, suffocation, starvation) or a hit never kills you "out of nowhere".
        /// #2518: the 15 Hz health drops are summed into one effect per half second (<see cref="DamageFeedback"/>) and
        /// scaled by the share of max health — chip damage (a drone's aura, a bite, lava) pulses a red screen edge, only
        /// a hit of 5 % or more flashes the whole screen, and the damage shake stops at a light rattle.</summary>
        private void UpdateDamageFeedback()
        {
            float dt = Time.deltaTime;
            float h = Game.Health;
            if (_damageFeedback.Update(h, DamageFeedback.PlayerMaxHealth, dt, out var pulse))
            {
                if (pulse.FullScreenFlash)
                {
                    _flashTimer = Mathf.Max(_flashTimer, pulse.FlashSeconds); // a bigger hit flashes longer
                }

                _causeTimer = 2.2f;
                _causeKey = InferDamageCause();
                UrpScenePost.Instance?.PulseVignette(pulse.Vignette); // the red edge — all a chip of damage gets
                VisorHud.Kick(pulse.VisorGlitch); // the hologram glitches with the hit
                FxCamera.AddDamageTrauma(pulse.Trauma); // #2152: the hit rattles the view (capped, #2518)
                PadRumble.Damage(pulse.Trauma); // #2746: …and the pad, when vibration is on
                if (DamageSource(out var threat, out string attackerId))
                {
                    _dmgDirFrom = threat;
                    _dmgDirEntity = attackerId;
                    _dmgDirTimer = DamageDirSeconds;
                }
            }

            // Respawn: the suit "reboots" — replay the boot-up reveal once the HUD is visible again.
            bool dead = h <= 0f;
            if (_wasDead && !dead)
            {
                _bootPending = true;
                _damageFeedback.Reset(h); // damage held from before the death is not felt after it
            }

            _wasDead = dead;

            UpdateOxygenAlarm(dt);

            if (_flashTimer > 0f) { _flashTimer = Mathf.Max(0f, _flashTimer - dt); }
            if (_dmgFlash != null)
            {
                var c = _dmgFlash.color;
                // Peak ~0.38 — clear but not blinding; Reduce flashes (#2152) keeps it to a soft tint.
                c.a = Mathf.Clamp01(_flashTimer / 0.6f) * (FxKit.ReduceFlashes ? 0.14f : 0.38f);
                _dmgFlash.color = c;
            }

            UpdateDamageDirection(dt);

            if (_causeTimer > 0f) { _causeTimer -= dt; }
            if (_dmgCause != null)
            {
                bool showCause = _causeTimer > 0f && Game.Health > 0f && !Game.ScreenMenuOpen; // #2530: a bite mid-ritual still says so
                _dmgCause.text = showCause && Game.Localizer != null ? Game.Localizer.Get(_causeKey) : string.Empty;
            }
        }

        /// <summary>Turns and fades the damage direction marker (#2154). #2564: it follows the attacker while that one moves,
        /// and a chevron beside it points up or down when the attacker is clearly above or below (HitDirection).</summary>
        private void UpdateDamageDirection(float dt)
        {
            if (_dmgDir == null || _dmgDirImg == null)
            {
                return;
            }

            _dmgDirTimer = Mathf.Max(0f, _dmgDirTimer - dt);
            var cam = Camera.main;
            if (_dmgDirTimer <= 0f || cam == null || Game.Health <= 0f)
            {
                _dmgDirImg.color = new Color(1f, 0.25f, 0.2f, 0f);
                if (_dmgDirArrow != null)
                {
                    _dmgDirArrow.color = new Color(1f, 0.25f, 0.2f, 0f);
                }

                _dmgDirEntity = string.Empty;
                return;
            }

            if (_dmgDirEntity.Length > 0 && AttackerScenePos(_dmgDirEntity, out var live))
            {
                _dmgDirFrom = live; // the marker stays on the attacker while it circles or swoops
            }

            // Angle of the threat around the view axis: 0 = ahead (marker on top), 90 = to the right; plus above/below.
            var camT = cam.transform;
            var f = camT.forward;
            var u = camT.up;
            HitDirection.ScreenHeading(f.x, f.y, f.z, u.x, u.z, out float headX, out float headZ);
            var to = _dmgDirFrom - camT.position;
            var bearing = HitDirection.Classify(to.x, to.y, to.z, headX, headZ);
            _dmgDir.localRotation = Quaternion.Euler(0f, 0f, -bearing.AngleDegrees);
            float alpha = Mathf.Clamp01(_dmgDirTimer / DamageDirFadeSeconds) * 0.85f;
            _dmgDirImg.color = new Color(1f, 0.25f, 0.2f, alpha);
            if (_dmgDirArrow != null)
            {
                // Above/below is measured from the player's eyes — a third-person camera sits behind and above them.
                var fromEyes = _dmgDirFrom - (Game.PlayerPosition + (Vector3.up * 1.6f));
                var height = HitDirection.Classify(fromEyes.x, fromEyes.y, fromEyes.z, headX, headZ).Height;

                // The chevron rides the turning ring just outside the bar; turned back by the ring's angle it points
                // straight up (or down) on the screen wherever the bar stands.
                float flip = height == HitHeight.Below ? 180f : 0f;
                _dmgDirArrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, bearing.AngleDegrees + flip);
                _dmgDirArrow.color = new Color(1f, 0.25f, 0.2f, height == HitHeight.Level ? 0f : alpha);
            }
        }

        /// <summary>#2564: where the hit came from. A fresh report from the server names the real attacker — its live scene
        /// position while it is in view, else the spot the blow came from. Without one (lava, hunger, an older server)
        /// the marker falls back to the old guess: the nearest hostile within 24 blocks.</summary>
        private bool DamageSource(out Vector3 at, out string entityId)
        {
            var hit = Game.LastHit;
            if (hit.IsFresh(Time.time))
            {
                entityId = hit.EntityId;
                if (!AttackerScenePos(entityId, out at))
                {
                    at = Game.ScenePos(hit.X, hit.Y, hit.Z) + Vector3.up;
                }

                return true;
            }

            entityId = string.Empty;
            return NearestThreat(out at);
        }

        /// <summary>#2564: the live scene position (about the body's middle, like <see cref="NearestThreat"/>) of the
        /// planet enemy, bandit or creature with this id. False when it is not in view — and for a giant, whose blow came
        /// from a foot or a stretch of its long body that the reported spot pins down better than its root.</summary>
        private bool AttackerScenePos(string id, out Vector3 at)
        {
            at = default;
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            foreach (var e in Game.PlanetEnemies)
            {
                if (e.Id == id)
                {
                    at = Game.ScenePos(e.X, e.Y, e.Z) + Vector3.up;
                    return true;
                }
            }

            foreach (var c in Game.Creatures)
            {
                if (c.Id == id)
                {
                    if (c.GiantHeight > 0f)
                    {
                        return false;
                    }

                    at = Game.ScenePos(c.X, c.Y, c.Z) + Vector3.up;
                    return true;
                }
            }

            return false;
        }

        /// <summary>The closest hostile planet enemy or creature within 24 blocks (scene position), if any.</summary>
        private bool NearestThreat(out Vector3 at)
        {
            at = default;
            if (Game == null)
            {
                return false;
            }

            float best = 24f * 24f;
            bool found = false;
            var me = Game.PlayerPosition;
            foreach (var e in Game.PlanetEnemies)
            {
                if (!e.Hostile)
                {
                    continue;
                }

                var p = Game.ScenePos(e.X, e.Y, e.Z) + Vector3.up;
                float d = (p - me).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    at = p;
                    found = true;
                }
            }

            foreach (var c in Game.Creatures)
            {
                if (!c.Hostile)
                {
                    continue;
                }

                var p = Game.ScenePos(c.X, c.Y, c.Z) + Vector3.up;
                float d = (p - me).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    at = p;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>Low-oxygen warning: under 25% O₂ a pulsing blue vignette ramps in (UrpScenePost) and a
        /// periodic two-beep alarm plays, its interval shrinking as oxygen runs out. Silent when healthy,
        /// breathable air keeps O₂ at 100 so it never fires there.</summary>
        private void UpdateOxygenAlarm(float dt)
        {
            float o2 = Game.Oxygen;
            float alarm = o2 <= 25f && Game.Health > 0f ? Mathf.Clamp01((25f - o2) / 25f) : 0f;
            UrpScenePost.Instance?.SetOxygenAlarm(alarm);

            if (alarm <= 0f)
            {
                _o2BeepTimer = 0f; // first beep fires immediately when the alarm next trips
                return;
            }

            _o2BeepTimer -= dt;
            if (_o2BeepTimer <= 0f)
            {
                _o2BeepTimer = Mathf.Lerp(4f, 1.2f, alarm); // urgent = faster
                ClientAudio.Instance?.Cue("o2_warning", 0.45f + alarm * 0.35f);
            }
        }

        /// <summary>Best-effort cause of the latest health loss from local state — lava (most acute) first,
        /// then suffocation / starvation, else a generic hit (creature/fall).</summary>
        private string InferDamageCause()
        {
            var p = Game.PlayerPosition;
            if (Game.World != null && Game.Content != null)
            {
                // #1839: lava carries a collider, so the player stands ON it — the feet cell is air and the melt is
                // one below. The same two cells the server's InLava reads, so the cause matches the damage.
                int bx = Mathf.FloorToInt(p.x), by = Mathf.FloorToInt(p.y), bz = Mathf.FloorToInt(p.z);
                if (Game.Content.BlockById(Game.World.GetBlock(bx, by, bz))?.Key == "lava"
                    || Game.Content.BlockById(Game.World.GetBlock(bx, by - 1, bz))?.Key == "lava")
                {
                    return "ui.hud.dmg_lava";
                }
            }
            if (Game.Oxygen <= 0.5f) { return "ui.hud.dmg_suffocate"; }
            if (Game.Hunger <= 0.5f) { return "ui.hud.dmg_starve"; }
            // The exposure meter is full (2026-09, Titas): the cold or the heat itself is doing the damage.
            if (Game.ExposureActive && Game.Exposure >= 0.999f)
            {
                return Game.ExposureHot ? "ui.hud.dmg_exposure_hot" : "ui.hud.dmg_exposure_cold";
            }

            // Exposure damage (#666): the suit is out of energy and climate control lost the fight —
            // the environment temperature says which extreme is doing the damage.
            if (Game.SuitClimateActive && Game.SuitEnergy <= 0.5f)
            {
                return (Game.Environment?.Temperature ?? 15f) < 15f ? "ui.hud.dmg_freeze" : "ui.hud.dmg_overheat";
            }

            return "ui.hud.dmg_hit";
        }

        private void BuildDamage(Transform root)
        {
            // Damage feedback (B21): full-screen red flash (behind the HUD so bars stay readable) + a cause label.
            var flashGo = new GameObject("DamageFlash", typeof(RectTransform));
            flashGo.transform.SetParent(root, false);
            var frt = flashGo.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = frt.offsetMax = Vector2.zero;
            _dmgFlash = flashGo.AddComponent<Image>();
            _dmgFlash.sprite = UiKit.SolidSprite;
            _dmgFlash.color = new Color(0.85f, 0.06f, 0.05f, 0f);
            _dmgFlash.raycastTarget = false;
            _dmgCause = UiText.Add(root, W / 2f - 220, H / 2f - 90, 440, 28, string.Empty, 20, new Color(1f, 0.45f, 0.4f), TextAnchor.MiddleCenter, FontStyle.Bold);

            // #2154: the damage direction marker — a short red bar on a ring around the crosshair, turned toward the
            // threat that hit you. #2564: the server names the attacker now (the nearest hostile is only the fallback);
            // the bar grew from 70×9 to 96×12, and a chevron just outside it says "above" or "below".
            var dirGo = new GameObject("DamageDirection", typeof(RectTransform));
            dirGo.transform.SetParent(root, false);
            _dmgDir = dirGo.GetComponent<RectTransform>();
            _dmgDir.anchorMin = _dmgDir.anchorMax = new Vector2(0.5f, 0.5f);
            _dmgDir.sizeDelta = Vector2.zero;
            var bar = new GameObject("Bar", typeof(RectTransform));
            bar.transform.SetParent(_dmgDir, false);
            var brt = bar.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(96f, 12f);
            brt.anchoredPosition = new Vector2(0f, 150f);
            _dmgDirImg = bar.AddComponent<Image>();
            _dmgDirImg.sprite = UiKit.SolidSprite;
            _dmgDirImg.color = new Color(1f, 0.25f, 0.2f, 0f);
            _dmgDirImg.raycastTarget = false;
            var chevron = new GameObject("HeightChevron", typeof(RectTransform));
            chevron.transform.SetParent(_dmgDir, false);
            var crt = chevron.GetComponent<RectTransform>();
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(26f, 18f);
            crt.anchoredPosition = new Vector2(0f, 170f); // just outside the bar (150 + 6 + a gap + 9)
            _dmgDirArrow = chevron.AddComponent<Image>();
            _dmgDirArrow.sprite = UiKit.TriangleSprite; // points up; UpdateDamageDirection turns it
            _dmgDirArrow.color = new Color(1f, 0.25f, 0.2f, 0f);
            _dmgDirArrow.raycastTarget = false;
        }
    }
}
