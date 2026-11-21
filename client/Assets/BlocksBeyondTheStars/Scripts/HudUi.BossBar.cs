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
    /// The boss bar of <see cref="HudUi"/> (#3676, generic since #3901): while a boss fights — a guardian giant near the
    /// player, or a space boss (the escort cruiser) in the player's flight — its name and its hull, top centre under the
    /// toast lane, with a mark where its rage begins. Which boss that is, its name key and its numbers come from one model,
    /// <see cref="BossBarState"/> (<see cref="GameBootstrap.BossBar"/>), which merges the giant's own status with the
    /// generic boss status; the bar itself does not know which boss it shows. A boss with a shield gets a thin strip above
    /// the fill. The bar turns a deeper, pulsing red in the second phase (steady under reduced motion). It hides when the
    /// fight ends (the status says so, the hull is gone, or no status came for a few seconds). Self-contained: one position
    /// constant (<see cref="BossBarY"/>) places it.
    /// </summary>
    public sealed partial class HudUi
    {
        /// <summary>The boss bar's top edge (HUD reference units, y down): just under the research/achievement toast lane.</summary>
        private const float BossBarY = ResearchY + ResearchH + 6f;

        private const float BossBarW = 520f, BossBarH = 50f, BossFillH = 14f, BossFillPad = 14f;

        private static readonly Color BossNameCol = new Color(1f, 0.82f, 0.68f);
        private static readonly Color BossFillCalm = new Color(1f, 0.45f, 0.26f);
        private static readonly Color BossFillRage = new Color(1f, 0.16f, 0.14f);
        private static readonly Color BossRageMarkCol = new Color(1f, 0.9f, 0.8f, 0.85f);
        private static readonly Color BossShieldCol = new Color(0.45f, 0.85f, 1f, 0.95f);

        private GameObject _bossBar;
        private TMP_Text _bossName;
        private Image _bossFill;
        private Image _bossShield;       // the thin strip above the fill — a boss with a shield
        private RectTransform _bossMark; // where the rage begins
        private float _bossMarkX;        // the mark's place as built (at the giant's rage share)
        private float _bossShown = -1f;
        private object _bossLoc;
        private string _bossNameKey;
        private float _bossEnrageShare;  // of the boss named _bossNameKey, from the enemy data (0 = none)
        private float _bossMarkAt = -1f; // the hull share the mark stands at (0 = hidden)

        private void BuildBossBar(Transform root)
        {
            _bossBar = Panel(root, (W - BossBarW) / 2f, BossBarY, BossBarW, BossBarH).gameObject;
            _bossBar.name = "BossBar";
            var t = _bossBar.transform;
            _bossName = UiText.Add(t, 12, 3, BossBarW - 24, 20, string.Empty, 16, BossNameCol, TextAnchor.MiddleCenter, FontStyle.Bold, UiText.Look.Glow);
            float fillW = BossBarW - (2f * BossFillPad);
            var (_, fill) = UiHolo.AddBar(t, BossFillPad, 27, fillW, BossFillH, new Color(0.03f, 0.05f, 0.1f, 0.9f), BossFillCalm);
            _bossFill = fill;
            _bossShield = UiKit.AddImage(t, BossFillPad, 23, fillW, 3, UiKit.SolidSprite, BossShieldCol); // #3901
            _bossShield.gameObject.name = "BossShield";
            _bossShield.gameObject.SetActive(false);
            float markX = BossFillPad + (fillW * GuardianGiantRules.RageAt) - 1.5f;
            _bossMark = UiKit.AddImage(t, markX, 24, 3, BossFillH + 6, UiKit.SolidSprite, BossRageMarkCol).rectTransform; // the rage begins here
            _bossMarkX = _bossMark.anchoredPosition.x;
            _bossMarkAt = GuardianGiantRules.RageAt;
            UiKit.AddSubCanvas(_bossBar); // the fill and the rage pulse re-batch only the bar
            _bossBar.SetActive(false);
        }

        /// <summary>Per frame: shows the bar for the boss <see cref="BossBarState.TryGetBar"/> names, eases the fill toward
        /// its hull and pulses it in the second phase; hides it otherwise or while the HUD is hidden.</summary>
        private void UpdateBossBar(bool show)
        {
            if (_bossBar == null)
            {
                return;
            }

            BossBarView bar = default;
            bool on = show && Game.BossBar.TryGetBar(Game.GuardianGiantFight, Game.WorldTime, Game.SpaceViewActive || Game.InSpace, out bar);
            if (_bossBar.activeSelf != on)
            {
                _bossBar.SetActive(on);
            }

            if (!on)
            {
                _bossShown = -1f;
                return;
            }

            ShowBossIdentity(bar);
            float fillW = BossBarW - (2f * BossFillPad);
            float target = bar.Health;
            _bossShown = _bossShown < 0f || UiKit.ReducedMotion ? target : Mathf.MoveTowards(_bossShown, target, Time.deltaTime * 0.8f);
            UiHolo.SetBar(_bossFill, _bossShown, fillW);
            ShowBossShield(bar.Shield, fillW);
            bool rage = bar.Enraged;
            float pulse = rage && !UiKit.ReducedMotion ? 0.8f + (0.2f * Mathf.Sin(Time.time * 6f)) : 1f;
            var c = rage ? BossFillRage : BossFillCalm;
            _bossFill.color = new Color(c.r * pulse, c.g * pulse, c.b * pulse, 1f);
        }

        /// <summary>The boss's name (from its name key) and the rage mark's place. Both change only when another kind of
        /// boss takes the bar or the language changes; a new kind of boss also starts with its fill where its hull is.</summary>
        private void ShowBossIdentity(BossBarView bar)
        {
            var loc = Game.Localizer;
            string nameKey = bar.NameKey ?? string.Empty;
            bool otherBoss = nameKey != _bossNameKey;
            if (otherBoss || !ReferenceEquals(loc, _bossLoc))
            {
                _bossLoc = loc;
                _bossNameKey = nameKey;
                _bossName.text = loc.Get(nameKey);
                if (otherBoss)
                {
                    _bossShown = -1f;
                    _bossEnrageShare = BossBarState.EnrageShare(Game.Content, nameKey);
                }
            }

            // The giant's status brings its rage share along; a generic boss's comes from its enemy data.
            float markAt = bar.RageAt > 0f ? bar.RageAt : _bossEnrageShare;
            if (Mathf.Approximately(markAt, _bossMarkAt))
            {
                return;
            }

            _bossMarkAt = markAt;
            bool marked = markAt > 0f;
            if (_bossMark.gameObject.activeSelf != marked)
            {
                _bossMark.gameObject.SetActive(marked);
            }

            if (marked)
            {
                float fillW = BossBarW - (2f * BossFillPad);
                var at = _bossMark.anchoredPosition;
                at.x = _bossMarkX + (fillW * (markAt - GuardianGiantRules.RageAt));
                _bossMark.anchoredPosition = at;
            }
        }

        /// <summary>The thin shield strip above the fill: as wide as the shield's share, gone without a shield.</summary>
        private void ShowBossShield(float shield, float fillW)
        {
            bool shielded = shield > 0.001f;
            var go = _bossShield.gameObject;
            if (go.activeSelf != shielded)
            {
                go.SetActive(shielded);
            }

            if (!shielded)
            {
                return;
            }

            var rt = _bossShield.rectTransform;
            float width = Mathf.Max(3f, fillW * Mathf.Clamp01(shield));
            if (!Mathf.Approximately(rt.sizeDelta.x, width))
            {
                rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
            }
        }
    }
}
