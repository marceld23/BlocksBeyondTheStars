// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text;
using BlocksBeyondTheStars.Shared.Definitions;
using TMPro;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The pinned recipe of <see cref="HudUi"/>: a recipe the player pinned in the crafting menu stays on the HUD as a
    /// small card in the right column's context stack — what it makes and, per ingredient, how many are there of how
    /// many it needs — so nobody has to open the menu again to see what is still missing while out collecting. The
    /// pin is the player's own (<see cref="ClientSettings.PinnedRecipe"/>); the counts are what the client already
    /// knows, nothing is asked of the server. The card rebuilds its text only when a count changes.
    /// </summary>
    public sealed partial class HudUi
    {
        private GameObject _pinPanel;
        private TMP_Text _pinTitle, _pinBody;
        private int _pinStamp;
        private readonly StringBuilder _pinText = new StringBuilder(160);

        private void BuildPinnedRecipe(Transform root)
        {
            _pinPanel = Panel(root, W - 260f, 460, 250, 132).gameObject;
            UiText.Add(_pinPanel.transform, 10, 6, 230, 18, Game?.Localizer?.Get("ui.hud.pinned") ?? "PINNED RECIPE", 12, UiKit.CyanDim,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            _pinTitle = UiText.Add(_pinPanel.transform, 10, 24, 230, 20, string.Empty, 14, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _pinBody = UiText.Add(_pinPanel.transform, 10, 46, 230, 82, string.Empty, 12, UiKit.TextCol, TextAnchor.UpperLeft);
            UiText.Wrap(_pinBody);
            _pinPanel.SetActive(false);
        }

        private void RefreshPinnedRecipe(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            string key = Settings != null ? Settings.PinnedRecipe : null;
            var recipe = string.IsNullOrEmpty(key) ? null : Game.Content?.GetRecipe(key);
            bool show = recipe != null && recipe.Outputs.Count > 0 && !Game.SpaceViewActive;
            bool appeared = show && !_pinPanel.activeSelf;
            if (_pinPanel.activeSelf != show)
            {
                _pinPanel.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            StackContextCard(_pinPanel, appeared);

            // One stamp over the recipe and every count: the text is rebuilt only when something changed.
            int stamp = key.GetHashCode();
            bool all = true;
            foreach (var need in recipe.Inputs)
            {
                int have = OwnedForPin(need);
                stamp = (stamp * 397) ^ have;
                all &= have >= need.Count;
            }

            if (stamp == _pinStamp && !appeared)
            {
                return;
            }

            _pinStamp = stamp;
            WritePinnedRecipe(recipe, loc, all);
        }

        /// <summary>The card's text: what the recipe makes, then a line per ingredient with "have / need".</summary>
        private void WritePinnedRecipe(RecipeDefinition recipe, BlocksBeyondTheStars.Shared.Localization.Localizer loc, bool all)
        {
            var output = recipe.Outputs[0];
            string name = loc.Get($"item.{BlocksBeyondTheStars.Shared.State.ItemKey.Base(output.Item)}.name");
            _pinTitle.text = output.Count > 1 ? $"{name}  ×{output.Count}" : name;

            _pinText.Clear();
            string ok = "#" + ColorUtility.ToHtmlStringRGB(UiKit.Ok);
            foreach (var need in recipe.Inputs)
            {
                int have = OwnedForPin(need);
                string label = string.IsNullOrEmpty(need.Group)
                    ? loc.Get($"item.{BlocksBeyondTheStars.Shared.State.ItemKey.Base(need.Item)}.name")
                    : loc.Get(BlocksBeyondTheStars.Shared.Content.GameContent.ItemGroupNameKey(need.Group));
                if (_pinText.Length > 0)
                {
                    _pinText.Append('\n');
                }

                // The count carries the meaning (3/5), the colour only underlines it.
                if (have >= need.Count)
                {
                    _pinText.Append("<color=").Append(ok).Append('>').Append(label).Append("  ").Append(have).Append('/').Append(need.Count).Append("</color>");
                }
                else
                {
                    _pinText.Append(label).Append("  ").Append(have).Append('/').Append(need.Count);
                }
            }

            if (all)
            {
                _pinText.Append('\n').Append(loc.Get("ui.hud.pinned_ready"));
            }

            _pinBody.text = _pinText.ToString();
        }

        /// <summary>How many of an ingredient the player carries, wears or has in the hold — a group ingredient (any
        /// log) counts every member of its group, like the crafting menu does.</summary>
        private int OwnedForPin(ItemAmount need)
        {
            int n = CountForPin(need.Item);
            if (!string.IsNullOrEmpty(need.Group) && Game.Content != null)
            {
                foreach (var member in Game.Content.GroupMembersOf(need))
                {
                    if (member != need.Item)
                    {
                        n += CountForPin(member);
                    }
                }
            }

            return n;
        }

        private int CountForPin(string item)
        {
            int n = 0;
            if (Game.Personal != null)
            {
                foreach (var s in Game.Personal)
                {
                    if (s.Item == item)
                    {
                        n += s.Count;
                    }
                }
            }

            if (Game.Cargo != null)
            {
                foreach (var s in Game.Cargo)
                {
                    if (s.Item == item)
                    {
                        n += s.Count;
                    }
                }
            }

            return n;
        }
    }
}
