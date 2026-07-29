// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Achievements: lifetime counters on the player, a data-driven table of goals over those counters, and an item
/// reward on unlock. Asked for by a player in exactly these terms — <i>"Ich möchte, dass es Erfolge gibt wie
/// 'Baue 5 Eisen ab' und dafür gibt's eine Belohnung."</i>
/// <para>
/// The design is deliberately dumb: the server bumps a NAMED COUNTER when something happens and every
/// achievement watching that counter re-checks itself. Adding an achievement over an existing counter is a
/// data-only change (see <c>data/achievements.json</c>), and progress is a plain tally so the panel can show
/// "3/5" instead of just locked/unlocked.
/// </para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>
    /// Records progress on a counter and awards anything that just came due. Safe to call from any game system;
    /// does nothing when no achievements are authored.
    /// </summary>
    private void Advance(PlayerSession session, string counter, int amount = 1)
    {
        if (session == null || amount <= 0 || string.IsNullOrEmpty(counter) || _content.Achievements.Count == 0)
        {
            return;
        }

        var counters = session.State.AchievementCounters;
        counters.TryGetValue(counter, out int now);
        counters[counter] = now + amount;

        // Only the achievements watching THIS counter can have changed.
        bool anyChange = false;
        foreach (var def in _content.Achievements)
        {
            if (def.Counter != counter)
            {
                continue;
            }

            anyChange = true;
            TryAward(session, def);
        }

        if (anyChange)
        {
            SendAchievements(session);
        }
    }

    /// <summary>
    /// Awards one achievement if its counter has reached the target and it hasn't been earned yet.
    /// <para>
    /// If the reward will not fit, the achievement is left UNEARNED and the player is asked to make room — it is
    /// then awarded on the next counter bump. Unlocking it and dropping the reward on the floor would repeat the
    /// exact bug this batch of feedback started with ("Items futsch"), and the reward is the whole point.
    /// </para>
    /// </summary>
    private void TryAward(PlayerSession session, AchievementDefinition def)
    {
        var p = session.State;
        if (p.Achievements.Contains(def.Key) || CounterOf(p, def.Counter) < def.Target)
        {
            return;
        }

        var rewards = def.Rewards ?? new List<ItemAmount>();
        if (rewards.Count > 0)
        {
            var pool = new MaterialPool(_content, p, _ship);
            if (!pool.CanFit(rewards))
            {
                Send(session, new AchievementRewardDeferred { Key = def.Key });
                return; // stays claimable — nothing is lost, nothing is falsely marked earned
            }

            foreach (var r in rewards)
            {
                pool.Add(r.Item, r.Count);
            }

            SendInventory(session);
        }

        p.Achievements.Add(def.Key);
        Send(session, new AchievementUnlocked { Key = def.Key });
        _log.Info($"'{p.Name}' earned achievement '{def.Key}'.");
    }

    private static int CounterOf(Shared.State.PlayerState p, string counter)
        => p.AchievementCounters.TryGetValue(counter, out int v) ? v : 0;

    /// <summary>Sends the player their full achievement list with live progress (join snapshot + every update).</summary>
    private void SendAchievements(PlayerSession session)
    {
        if (_content.Achievements.Count == 0)
        {
            return;
        }

        var p = session.State;
        var list = new AchievementList
        {
            Items = _content.Achievements.Select(a => new NetAchievement
            {
                Key = a.Key,
                Category = a.Category ?? string.Empty,
                Target = a.Target,
                Progress = System.Math.Min(CounterOf(p, a.Counter), a.Target),
                Earned = p.Achievements.Contains(a.Key),
            }).ToList(),
        };

        Send(session, list);
    }

    /// <summary>
    /// Re-checks every achievement against the counters as they stand. Used on join: it pays out anything that
    /// came due while a reward could not be handed over (see <see cref="TryAward"/>), and it retro-awards
    /// achievements added to the data file after the save was made.
    /// </summary>
    private void SettleAchievements(PlayerSession session)
    {
        foreach (var def in _content.Achievements)
        {
            TryAward(session, def);
        }

        SendAchievements(session);
    }

    // --- Where the counters come from -----------------------------------------------------------------

    /// <summary>A block was mined: bumps the "any block" tally and the per-block one, so both
    /// <c>mine:any</c> and e.g. <c>mine:iron_ore</c> achievements advance off one event.</summary>
    private void OnAchievementMine(PlayerSession session, string blockKey)
    {
        Advance(session, AchievementCounters.MineAny);
        if (!string.IsNullOrEmpty(blockKey))
        {
            Advance(session, AchievementCounters.Mine(blockKey));
        }
    }

    private void OnAchievementBuild(PlayerSession session) => Advance(session, AchievementCounters.BuildAny);

    /// <summary>A successful craft: the generic tally plus the specific recipe.</summary>
    private void OnAchievementCraft(PlayerSession session, string recipeKey)
    {
        Advance(session, AchievementCounters.CraftAny);
        if (!string.IsNullOrEmpty(recipeKey))
        {
            Advance(session, AchievementCounters.Craft(recipeKey));
        }
    }

    private void OnAchievementDefeat(PlayerSession session) => Advance(session, AchievementCounters.Defeat);

    private void OnAchievementVisit(PlayerSession session) => Advance(session, AchievementCounters.VisitBody);

    // --- Test entrypoints -----------------------------------------------------------------------------

    /// <summary>Re-checks and pays out anything due (the join path), for tests.</summary>
    public void SettleAchievementsForTest(PlayerSession session) => SettleAchievements(session);

    /// <summary>Drives the "arrived on a body" bookkeeping, for tests.</summary>
    public void MarkArrivedOnBodyForTest(PlayerSession session, string bodyId) => MarkArrivedOnBody(session, bodyId);
}
