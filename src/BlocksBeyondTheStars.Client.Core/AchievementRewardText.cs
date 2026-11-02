// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The words around an achievement's reward (#3082): the unlock banner's "Reward: Pumpkin lantern ×4, Lantern ×6" line, and
    /// the notice when the reward does not fit yet — which reward, and how many slots to free. The notice used to be one
    /// status line ("no room for the reward"), easy to miss and silent about what was waiting; Justus' Halloween present waited
    /// four hours. The rewards come from the client's own content (the same <c>data/achievements.json</c> the server reads),
    /// so the server only sends the key and the slot count. Unity-free so the wording is covered by plain .NET tests.
    /// </summary>
    public static class AchievementRewardText
    {
        public const string RewardLineKey = "ui.achv.reward_line";
        public const string WaitingHeadKey = "ui.achv.reward_waiting";
        public const string RoomOneKey = "ui.achv.reward_room_one";
        public const string RoomManyKey = "ui.achv.reward_room_many";
        public const string RoomKey = "ui.achv.reward_room";
        public const string StatusKey = "ui.achv.reward_full_named";

        /// <summary>The rewards of an achievement from the client's content; empty when it is unknown or pays nothing.</summary>
        public static IReadOnlyList<ItemAmount> RewardsOf(GameContent? content, string achievementKey)
        {
            if (content is null || string.IsNullOrEmpty(achievementKey))
            {
                return Array.Empty<ItemAmount>();
            }

            var def = content.Achievements.FirstOrDefault(a => a.Key == achievementKey);
            return def?.Rewards is { Count: > 0 } rewards ? rewards : (IReadOnlyList<ItemAmount>)Array.Empty<ItemAmount>();
        }

        /// <summary>"Pumpkin lantern ×4, Lantern ×6" — every reward by its display name and count; empty for none.</summary>
        public static string Items(IEnumerable<ItemAmount>? rewards, Func<string, string> itemName)
        {
            if (rewards is null)
            {
                return string.Empty;
            }

            return string.Join(", ", rewards
                .Where(r => r is not null && r.Count > 0 && !string.IsNullOrEmpty(r.Item))
                .Select(r => itemName(r.Item) + " ×" + r.Count));
        }

        /// <summary>The unlock banner's reward line ("Reward: …"), or empty when the achievement pays nothing.</summary>
        public static string RewardLine(IEnumerable<ItemAmount>? rewards, Func<string, string> itemName, Func<string, string> localize)
        {
            string items = Items(rewards, itemName);
            return items.Length == 0 ? string.Empty : localize(RewardLineKey).Replace("{items}", items);
        }

        /// <summary>What to do so the reward fits: "Free 2 slots for …" (one slot has its own wording; an older server that
        /// sends no count gets "Make room for …").</summary>
        public static string RoomLine(IEnumerable<ItemAmount>? rewards, int slots, Func<string, string> itemName, Func<string, string> localize)
        {
            string items = Items(rewards, itemName);
            string key = slots <= 0 ? RoomKey : slots == 1 ? RoomOneKey : RoomManyKey;
            // Replace, not string.Format: a translation with a stray brace must not throw inside a network callback.
            return localize(key).Replace("{slots}", slots.ToString(System.Globalization.CultureInfo.InvariantCulture)).Replace("{items}", items);
        }

        /// <summary>The status line under the banner: "<name>: no room for the reward yet. <room line> — it arrives by itself
        /// then."</summary>
        public static string StatusLine(string achievementName, IEnumerable<ItemAmount>? rewards, int slots,
            Func<string, string> itemName, Func<string, string> localize)
            => localize(StatusKey)
                .Replace("{name}", achievementName ?? string.Empty)
                .Replace("{room}", RoomLine(rewards, slots, itemName, localize));
    }
}
