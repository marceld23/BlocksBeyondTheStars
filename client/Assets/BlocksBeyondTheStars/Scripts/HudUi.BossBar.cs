// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The boss bar of <see cref="HudUi"/> (#3676): while a guardian giant fights near the player — the server sends its
    /// status to everyone in range (<see cref="GameBootstrap.GuardianGiantFight"/>, <see cref="GuardianGiantFight.BossBar"/>) —
    /// its name and its hull, top centre under the toast lane, with a mark at half the hull where its rage begins. The bar
    /// turns a deeper, pulsing red in the rage phase (steady under reduced motion). It hides when the fight ends (the status
    /// says so, the hull is gone, or no status came for a few seconds). Self-contained: one position constant
    /// (<see cref="BossBarY"/>) places it.
    /// </summary>
    public sealed partial class HudUi
    {
        /// <summary>The boss bar's top edge (HUD reference units, y down): just under the research/achievement toast lane.</summary>
        private const float BossBarY = ResearchY + ResearchH + 6f;

        private const float BossBarW = 520f, BossBarH = 50f, BossFillH = 14f, BossFillPad = 14f;

        /// <summary>The giant's name (the Codex entry's).</summary>
        private const string BossNameKey = "enemy.guardian_giant.name";

        private static readonly Color BossNameCol = new Color(1f, 0.82f, 0.68f);
        private static readonly Color BossFillCalm = new Color(1f, 0.45f, 0.26f);
        private static readonly Color BossFillRage = new Color(1f, 0.16f, 0.14f);
        private static readonly Color BossRageMarkCol = new Color(1f, 0.9f, 0.8f, 0.85f);

        private GameObject _bossBar;
        private TMP_Text _bossName;
        private Image _bossFill;
        private float _bossShown = -1f;
        private object _bossLoc;

        private void BuildBossBar(Transform root)
        {
            _bossBar = Panel(root, (W - BossBarW) / 2f, BossBarY, BossBarW, BossBarH).gameObject;
            _bossBar.name = "BossBar";
            var t = _bossBar.transform;
            _bossName = UiText.Add(t, 12, 3, BossBarW - 24, 20, string.Empty, 16, BossNameCol, TextAnchor.MiddleCenter, FontStyle.Bold, UiText.Look.Glow);
            float fillW = BossBarW - (2f * BossFillPad);
            var (_, fill) = UiHolo.AddBar(t, BossFillPad, 27, fillW, BossFillH, new Color(0.03f, 0.05f, 0.1f, 0.9f), BossFillCalm);
            _bossFill = fill;
            float markX = BossFillPad + (fillW * GuardianGiantRules.RageAt) - 1.5f;
            UiKit.AddImage(t, markX, 24, 3, BossFillH + 6, UiKit.SolidSprite, BossRageMarkCol); // the rage begins here
            UiKit.AddSubCanvas(_bossBar); // the fill and the rage pulse re-batch only the bar
            _bossBar.SetActive(false);
        }

        /// <summary>Per frame: shows the bar for the fight <see cref="GuardianGiantFight.BossBar"/> names, eases the fill toward
        /// its hull and pulses it in the rage phase; hides it otherwise or while the HUD is hidden.</summary>
        private void UpdateBossBar(bool show)
        {
            if (_bossBar == null)
            {
                return;
            }

            var track = show ? Game.GuardianGiantFight.BossBar(Game.WorldTime) : null;
            bool on = track != null;
            if (_bossBar.activeSelf != on)
            {
                _bossBar.SetActive(on);
            }

            if (!on)
            {
                _bossShown = -1f;
                return;
            }

            var loc = Game.Localizer;
            if (!ReferenceEquals(loc, _bossLoc))
            {
                _bossLoc = loc;
                _bossName.text = loc.Get(BossNameKey);
            }

            float target = track.HullFraction;
            _bossShown = _bossShown < 0f || UiKit.ReducedMotion ? target : Mathf.MoveTowards(_bossShown, target, Time.deltaTime * 0.8f);
            UiHolo.SetBar(_bossFill, _bossShown, BossBarW - (2f * BossFillPad));
            bool rage = track.Status.Rage;
            float pulse = rage && !UiKit.ReducedMotion ? 0.8f + (0.2f * Mathf.Sin(Time.time * 6f)) : 1f;
            var c = rage ? BossFillRage : BossFillCalm;
            _bossFill.color = new Color(c.r * pulse, c.g * pulse, c.b * pulse, 1f);
        }
    }
}
