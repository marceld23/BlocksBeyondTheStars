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
    /// The toasts of <see cref="HudUi"/>: the status toast with its lifetime (<see cref="HudToastPolicy"/>, #1860) and
    /// the celebration slot for new research (#763) and unlocked achievements (#2597).
    /// </summary>
    public sealed partial class HudUi
    {
        private TMP_Text _toast;

        // Research toast (#763): "New research available!" with the blueprint's icon, top-centre under
        // the IN SPACE/observer lines. One toast at a time; further keys wait in Game.ResearchAvailable.
        // #2597: the slot celebrates achievements too (Game.AchievementsUnlocked, after any waiting research) — gold glow,
        // a star, 4 s, the success sound as it appears.
        private GameObject _researchGo;
        private RectTransform _researchRect;
        private CanvasGroup _researchFade;
        private RawImage _researchIcon;
        private Image _researchGlow;
        private RectTransform _researchShine;
        private TMP_Text _researchHead, _researchName;
        private float _researchAge = -1f; // <0 = idle, no toast up
        private const float ResearchW = 480f, ResearchH = 86f, ResearchY = 64f;
        private const float ResearchPop = 0.25f, ResearchHold = 3.0f, ResearchFade = 0.5f;
        private const float AchievementHold = 4.0f;
        private float _toastHoldNow = ResearchHold;                   // the hold of the toast that is up
        private Color _toastGlow = new Color(0.4f, 0.82f, 1f);        // its glow: research cyan, achievement gold
        private static readonly Color AchievementGold = new Color(1f, 0.82f, 0.3f);

        private CanvasGroup _toastGroup;  // toast slide/fade
        private Vector2 _toastBasePos;
        private string _lastToastMsg = string.Empty;
        private int _lastToastSeq = -1;  // GameBootstrap.LastMessageSeq of the toast up (#1860)
        private float _toastAge = -1f;   // seconds since the toast came up; -1 = none up
        private float _toastHold;        // HudToastPolicy hold of the toast up (before the fade-out)

        private void BuildToast(Transform root)
        {
            _toast = UiText.Add(root, 14, 268, W - 28, 22, string.Empty, 15, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold, UiText.Look.Outline);
            _toast.richText = false; // carries developer/player text (reply toasts, chat echoes) — shown verbatim (#1368)
            _toastGroup = _toast.gameObject.AddComponent<CanvasGroup>();
            _toastGroup.blocksRaycasts = false;
            _toastBasePos = _toast.rectTransform.anchoredPosition;
        }

        /// <summary>Toast slide-in: a new message eases in from the left and fades up.</summary>
        private void ShowToast(string msg)
        {
            _toast.text = msg;
            _toastAge = msg.Length == 0 ? -1f : 0f; // #1860: arm the lifetime; an empty message has none
            _toastHold = HudToastPolicy.HoldSeconds(msg);
            if (_toastGroup == null || msg.Length == 0)
            {
                return;
            }

            _toast.rectTransform.anchoredPosition = _toastBasePos;
            if (!UiKit.ReducedMotion)
            {
                _toastGroup.alpha = 0f;
                UiTween.Move(_toast.rectTransform, _toastBasePos + new Vector2(-16f, 0f), _toastBasePos, 0.30f, UiTween.Ease.OutCubic);
            }

            UiTween.Alpha(_toastGroup, 1f, 0.22f, UiTween.Ease.OutQuad);
        }

        /// <summary>Toast lifetime (#1860): the status line used to stay until the next server message replaced
        /// it — "Life support lost" sat over a player breathing station air for the rest of the session. After
        /// <see cref="HudToastPolicy.HoldSeconds"/> it fades over <see cref="HudToastPolicy.FadeSeconds"/>, then
        /// the message is blanked on the game state (so the SAME line re-sent later shows again — Refresh compares
        /// text and sequence). The slide-in tween is long finished by the time the fade drives the alpha.</summary>
        private void UpdateToast(float dt)
        {
            if (_toastAge < 0f)
            {
                return;
            }

            _toastAge += dt;
            if (_toastAge > _toastHold && _toastGroup != null)
            {
                _toastGroup.alpha = HudToastPolicy.Alpha(_toastAge, _toastHold);
            }

            if (HudToastPolicy.Expired(_toastAge, _toastHold))
            {
                _toastAge = -1f;
                Game?.ExpireMessage(_lastToastSeq); // the next Refresh sees an empty message and blanks the label
            }
        }

        // --- research toast (#763) ---

        /// <summary>Drives the HUD's celebration slot — the "new research available" toast (#763) and, #2597, the achievement
        /// banner: pops the next queued item when idle and the HUD is visible (research first, then achievements; nothing
        /// starts while a cutscene plays), animates the pop/glow/shine, then fades out. Queued keys survive menus and
        /// flight — the announcement is rare enough to arrive late rather than be dropped.</summary>
        private void UpdateResearchToast(float dt)
        {
            if (_researchAge >= 0f && _researchGo == null)
            {
                _researchAge = -1f; // the canvas (and toast) got torn down mid-toast, e.g. on a world change
            }

            if (_researchAge < 0f)
            {
                bool cutscene = GameBootstrap.CutsceneActive || (CutsceneDirector.Instance != null && CutsceneDirector.Instance.Playing);
                if ((Game.ResearchAvailable.Count == 0 && Game.AchievementsUnlocked.Count == 0)
                    || _canvas == null || !_canvas.enabled || cutscene)
                {
                    return;
                }

                if (Game.ResearchAvailable.Count > 0)
                {
                    StartResearchToast(Game.ResearchAvailable.Dequeue());
                }
                else
                {
                    StartAchievementToast(Game.AchievementsUnlocked.Dequeue());
                }
            }

            _researchAge += dt;
            if (_researchAge >= _toastHoldNow + ResearchFade)
            {
                _researchAge = -1f;
                _researchGo.SetActive(false);
                return;
            }

            float alpha = _researchAge >= _toastHoldNow
                ? 1f - (_researchAge - _toastHoldNow) / ResearchFade
                : (UiKit.ReducedMotion ? Mathf.Clamp01(_researchAge / 0.2f) : 1f);
            _researchFade.alpha = alpha;

            if (UiKit.ReducedMotion)
            {
                _researchRect.localScale = Vector3.one;
                _researchGlow.color = new Color(_toastGlow.r, _toastGlow.g, _toastGlow.b, 0.22f * alpha);
                _researchShine.gameObject.SetActive(false);
                return;
            }

            // Pop-in: overshoot past 1.0 and settle by the end of the pop window (sin term ends at 0).
            float p = Mathf.Clamp01(_researchAge / ResearchPop);
            _researchRect.localScale = Vector3.one * (Mathf.Lerp(0.75f, 1f, p) + Mathf.Sin(p * Mathf.PI) * 0.08f);

            // Soft glow pulsing behind the icon for the toast's whole life.
            float pulse = Mathf.Sin(_researchAge * 4.2f) * 0.5f + 0.5f;
            _researchGlow.color = new Color(_toastGlow.r, _toastGlow.g, _toastGlow.b, (0.14f + pulse * 0.24f) * alpha);
            float gs = 1f + pulse * 0.18f;
            _researchGlow.rectTransform.localScale = new Vector3(gs, gs, 1f);

            // One diagonal shine sweep across the panel just after the pop (clipped by the RectMask2D).
            float sweep = (_researchAge - 0.15f) / 0.6f;
            bool sweeping = sweep >= 0f && sweep <= 1f;
            _researchShine.gameObject.SetActive(sweeping);
            if (sweeping)
            {
                var ap = _researchShine.anchoredPosition;
                ap.x = Mathf.Lerp(-80f, ResearchW + 40f, sweep);
                _researchShine.anchoredPosition = ap;
            }
        }

        /// <summary>Fills and shows the toast for one blueprint key and plays the discovery chime.</summary>
        private void StartResearchToast(string bpKey)
        {
            EnsureResearchToast();
            var loc = Game.Localizer;
            _researchHead.text = loc.Get("ui.tech.research_available").ToUpperInvariant();
            _researchName.text = loc.Get($"blueprint.{bpKey}.name");
            _researchHead.color = UiKit.CyanDim; // #2597: the slot is shared — research keeps its cyan
            _researchName.color = UiKit.Cyan;
            _researchIcon.enabled = true;
            SetBlueprintIcon(_researchIcon, bpKey);
            _toastHoldNow = ResearchHold;
            _toastGlow = new Color(0.4f, 0.82f, 1f);
            _researchGo.SetActive(true);
            _researchAge = 0f;
            _researchFade.alpha = UiKit.ReducedMotion ? 0f : 1f;
            _researchRect.localScale = UiKit.ReducedMotion ? Vector3.one : Vector3.one * 0.75f;
            ClientAudio.Instance?.Cue("research_available", 0.9f);
        }

        /// <summary>#2597: fills and shows the celebration banner for one unlocked achievement — "ACHIEVEMENT UNLOCKED!", its
        /// name, a gold star with a gold glow, held 4 s — and plays the success sound as it appears (it replaced the status
        /// line and the sound at the unlock). Never shows a raw key: a nameless achievement reads as "Achievements".</summary>
        private void StartAchievementToast(string key)
        {
            EnsureResearchToast();
            var loc = Game.Localizer;
            _researchHead.text = loc.Get("ui.achv.banner").ToUpperInvariant();
            string nameKey = $"achv.{key}.name";
            string name = loc.Get(nameKey);
            _researchName.text = string.IsNullOrEmpty(name) || name == nameKey ? loc.Get("ui.achv.title") : name;
            _researchHead.color = new Color(AchievementGold.r * 0.85f, AchievementGold.g * 0.85f, AchievementGold.b * 0.85f);
            _researchName.color = AchievementGold;
            var star = UiKit.Icon("map_mark_star");
            _researchIcon.enabled = star != null;
            if (star != null)
            {
                _researchIcon.texture = star.texture;
                _researchIcon.uvRect = new Rect(0, 0, 1, 1);
                _researchIcon.color = AchievementGold;
            }

            _toastHoldNow = AchievementHold;
            _toastGlow = AchievementGold;
            _researchGo.SetActive(true);
            _researchAge = 0f;
            _researchFade.alpha = UiKit.ReducedMotion ? 0f : 1f;
            _researchRect.localScale = UiKit.ReducedMotion ? Vector3.one : Vector3.one * 0.75f;
            ClientAudio.Instance?.Cue("ui_success", 0.9f);
        }

        /// <summary>Builds the toast hierarchy once: panel + glow disc + icon + two text lines + the
        /// shine bar. Center-pivoted (unlike the usual top-left Place) so the pop scales in place.</summary>
        private void EnsureResearchToast()
        {
            if (_researchGo != null)
            {
                return;
            }

            _researchGo = new GameObject("research_toast", typeof(RectTransform));
            _researchGo.transform.SetParent(_canvas.transform, false);
            _researchRect = UiKit.Place(_researchGo, (W - ResearchW) / 2f, ResearchY, ResearchW, ResearchH);
            _researchRect.pivot = new Vector2(0.5f, 0.5f);
            _researchRect.anchoredPosition += new Vector2(ResearchW / 2f, -ResearchH / 2f);
            _researchFade = _researchGo.AddComponent<CanvasGroup>();
            _researchFade.blocksRaycasts = false;
            _researchFade.interactable = false;

            var bg = _researchGo.AddComponent<Image>();
            bg.sprite = UiKit.PanelSprite;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.05f, 0.12f, 0.24f, 0.9f);
            bg.raycastTarget = false;
            UiHolo.Apply(bg, UiHolo.Style.Panel, 12f, 1.5f, 1.6f); // no-op on the bitmap fallback
            _researchGo.AddComponent<RectMask2D>(); // clips the shine sweep to the panel

            _researchGlow = UiKit.AddImage(_researchGo.transform, 8f, 8f, 70f, 70f, UiKit.DiscSprite, new Color(0.4f, 0.82f, 1f, 0.3f));
            var glowRt = _researchGlow.rectTransform; // centre-pivot so the pulse grows around the icon
            glowRt.pivot = new Vector2(0.5f, 0.5f);
            glowRt.anchoredPosition += new Vector2(35f, -35f);
            var iconGo = new GameObject("icon", typeof(RectTransform));
            iconGo.transform.SetParent(_researchGo.transform, false);
            UiKit.Place(iconGo, 15f, 15f, 56f, 56f);
            _researchIcon = iconGo.AddComponent<RawImage>();
            _researchIcon.raycastTarget = false;

            _researchHead = UiText.Add(_researchGo.transform, 92f, 16f, ResearchW - 104f, 22f, string.Empty, 15, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            _researchName = UiText.Add(_researchGo.transform, 92f, 40f, ResearchW - 104f, 30f, string.Empty, 20, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiText.Style(_researchHead, UiText.Look.Outline);
            UiText.Style(_researchName, UiText.Look.Outline);

            var shine = UiKit.AddImage(_researchGo.transform, -80f, -20f, 46f, ResearchH + 40f, UiKit.SolidSprite, new Color(1f, 1f, 1f, 0.16f));
            shine.transform.localRotation = Quaternion.Euler(0f, 0f, 18f);
            _researchShine = shine.rectTransform;
            _researchShine.gameObject.SetActive(false);

            _researchGo.SetActive(false);
        }

        /// <summary>Icon for a blueprint key: most blueprint keys ARE item keys (heal_tank, binoculars…)
        /// and reuse the item resolution; module/expansion blueprints fall back to their tech-category
        /// sprite, tinted cyan like the tech tree's own nodes.</summary>
        private void SetBlueprintIcon(RawImage img, string bpKey)
        {
            if (Game.Content?.GetItem(bpKey) != null || Game.Content?.GetBlock(bpKey) != null)
            {
                SetItemIcon(img, bpKey);
                return;
            }

            string cat = Game.Content != null && Game.Content.Blueprints.TryGetValue(bpKey, out var bp) && bp.Category == "ShipExpansion"
                ? "cat_modules"
                : "cat_tech";
            var sprite = UiKit.Icon(cat);
            if (sprite != null)
            {
                img.texture = sprite.texture;
                img.uvRect = new Rect(0, 0, 1, 1);
                img.color = UiKit.Cyan;
            }
            else
            {
                img.texture = IconFactory.ForItem(bpKey, BlocksBeyondTheStars.Shared.Definitions.ToolKind.None);
                img.uvRect = new Rect(0, 0, 1, 1);
                img.color = Color.white;
            }
        }
    }
}
