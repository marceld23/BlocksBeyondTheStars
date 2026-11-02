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
            TryAward(session, def, praise: true); // #2597: earned just now — VEGA may say "well done"
        }

        if (anyChange)
        {
            SendAchievements(session);
        }
    }

    /// <summary>
    /// Awards one achievement if its counter has reached the target and it hasn't been earned yet. True when it was
    /// awarded just now.
    /// <para>
    /// If the reward will not fit, the achievement is left UNEARNED and the player is asked to make room. Unlocking it
    /// and dropping the reward on the floor would repeat the exact bug this batch of feedback started with ("Items
    /// futsch"), and the reward is the whole point. #3082: it used to be paid out only on the next bump of the same
    /// counter or at the next join — and a festival's graduation counter bumps exactly once, so Justus' Halloween
    /// present waited four hours and a restart although he had freed slots long before. Now the waiting key sits in
    /// <see cref="PlayerSession.DeferredRewards"/> and <see cref="TickDeferredRewards"/> hands it over as soon as there is
    /// room; the notice (with the slots still missing) goes out once per waiting reward, not on every retry.
    /// </para>
    /// <para>#2597: <paramref name="praise"/> lets VEGA praise the unlock (<see cref="QueueVegaPraise"/>) — true for an
    /// achievement earned right now (a waiting reward handed over later counts as now), false for the retro-awards
    /// settled at join (those stay silent).</para>
    /// </summary>
    private bool TryAward(PlayerSession session, AchievementDefinition def, bool praise)
    {
        var p = session.State;
        if (p.Achievements.Contains(def.Key) || CounterOf(p, def.Counter) < def.Target)
        {
            return false;
        }

        var rewards = def.Rewards ?? new List<ItemAmount>();
        if (rewards.Count > 0)
        {
            var pool = new MaterialPool(_content, p, _ship);
            if (!pool.CanFit(rewards))
            {
                if (session.DeferredRewards.Add(def.Key))
                {
                    Send(session, new AchievementRewardDeferred { Key = def.Key, SlotsNeeded = pool.SlotsShort(rewards) });
                }

                return false; // stays claimable — nothing is lost, nothing is falsely marked earned
            }

            session.DeferredRewards.Remove(def.Key);
            foreach (var r in rewards)
            {
                pool.Add(r.Item, r.Count);
            }

            SendInventory(session);
        }

        p.Achievements.Add(def.Key);
        Send(session, new AchievementUnlocked { Key = def.Key });
        _log.Info($"'{p.Name}' earned achievement '{def.Key}'.");
        if (praise)
        {
            QueueVegaPraise(session, def);
        }

        return true;
    }

    /// <summary>Seconds between two retries of a waiting reward without an inventory change — the reward may fit because
    /// the player boarded their ship (the cargo hold counts) or a path changed the inventory without telling it.</summary>
    private const double DeferredRewardRetrySeconds = 5.0;

    private double _sinceDeferredRewardRetry;

    /// <summary>#3082: called from <see cref="SendInventory"/> — the one place every inventory change reports to. Only
    /// marks the session; the retry itself runs in <see cref="TickDeferredRewards"/>, after the handler that changed the
    /// inventory has finished (a craft that consumed its inputs and still has to add its output must not find its freed
    /// slot taken by a present). Costs one count check while nothing waits.</summary>
    private static void NoteInventoryChangedForRewards(PlayerSession session)
    {
        if (session.DeferredRewards.Count > 0)
        {
            session.RewardRetryDue = true;
        }
    }

    /// <summary>#3082, post-world tick step: hands over the rewards that were waiting for room — for a player whose
    /// inventory changed, at once; for every other waiting player every <see cref="DeferredRewardRetrySeconds"/> s. The
    /// award is a fresh one: the unlock banner, the refreshed list and VEGA's praise.</summary>
    private void TickDeferredRewards(double dt)
    {
        _sinceDeferredRewardRetry += dt;
        bool periodic = _sinceDeferredRewardRetry >= DeferredRewardRetrySeconds;
        if (periodic)
        {
            _sinceDeferredRewardRetry = 0;
        }

        foreach (var s in _sessions.Values)
        {
            if (!s.Joined || s.DeferredRewards.Count == 0 || !(s.RewardRetryDue || periodic))
            {
                continue;
            }

            s.RewardRetryDue = false;
            RetryDeferredRewards(s);
        }
    }

    private void RetryDeferredRewards(PlayerSession session)
    {
        Serve(session); // the pool reads the ship cursor (the cargo hold while aboard)
        bool any = false;
        foreach (string key in session.DeferredRewards.ToArray())
        {
            var def = _content.Achievements.FirstOrDefault(a => a.Key == key);
            if (def is null || session.State.Achievements.Contains(key))
            {
                session.DeferredRewards.Remove(key);
                continue;
            }

            any |= TryAward(session, def, praise: true);
        }

        if (any)
        {
            SendAchievements(session);
        }
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

        Send(session, BuildAchievementList(session)); // items + the raw journey counters (#1103)
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
            TryAward(session, def, praise: false); // #2597: retro-awards at join stay silent
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

    // Late-game counters (#1102) — each is one line at an existing gameplay event.

    /// <summary>A first-time scan; monuments also bump their own tally.</summary>
    private void OnAchievementScan(PlayerSession session, string kind)
    {
        Advance(session, AchievementCounters.ScanAny);
        if (kind == "monument")
        {
            Advance(session, AchievementCounters.ScanMonument);
        }
    }

    private void OnAchievementResearch(PlayerSession session) => Advance(session, AchievementCounters.ResearchAny);

    private void OnAchievementStationCommissioned(PlayerSession session) => Advance(session, AchievementCounters.StationCommissioned);

    private void OnAchievementShipCommissioned(PlayerSession session) => Advance(session, AchievementCounters.ShipCommissioned);

    private void OnAchievementTame(PlayerSession session) => Advance(session, AchievementCounters.TameAny);

    private void OnAchievementLoot(PlayerSession session) => Advance(session, AchievementCounters.LootAny);

    private void OnAchievementVisitSystem(PlayerSession session) => Advance(session, AchievementCounters.VisitSystem);

    private void OnAchievementMissionCompleted(PlayerSession session) => Advance(session, AchievementCounters.MissionCompleted);

    private void OnAchievementHyperjump(PlayerSession session) => Advance(session, AchievementCounters.Hyperjump);

    private void OnAchievementStoryFinale(PlayerSession session) => Advance(session, AchievementCounters.StoryFinale);

    private void OnAchievementRelayCommissioned(PlayerSession session) => Advance(session, AchievementCounters.RelayCommissioned);

    private void OnAchievementLaneLinked(PlayerSession session) => Advance(session, AchievementCounters.LaneLinked);

    // --- Test entrypoints -----------------------------------------------------------------------------

    /// <summary>Re-checks and pays out anything due (the join path), for tests.</summary>
    public void SettleAchievementsForTest(PlayerSession session) => SettleAchievements(session);

    /// <summary>Test seam (#3082): run the waiting-reward retry now, as the next tick would.</summary>
    public void RetryDeferredRewardsForTest() => TickDeferredRewards(0);

    /// <summary>Drives the "arrived on a body" bookkeeping, for tests.</summary>
    public void MarkArrivedOnBodyForTest(PlayerSession session, string bodyId) => MarkArrivedOnBody(session, bodyId);

    /// <summary>The achievement list exactly as the client receives it, for tests.</summary>
    public AchievementList AchievementListForTest(PlayerSession session) => BuildAchievementList(session);

    /// <summary>The player's full achievement list with live (capped) progress plus the raw counters (#1103).</summary>
    private AchievementList BuildAchievementList(PlayerSession session)
        => new()
        {
            Items = _content.Achievements.Select(a => new NetAchievement
            {
                Key = a.Key,
                Category = a.Category ?? string.Empty,
                Target = a.Target,
                Progress = System.Math.Min(CounterOf(session.State, a.Counter), a.Target),
                Earned = session.State.Achievements.Contains(a.Key),
            }).ToList(),
            Counters = new Dictionary<string, int>(session.State.AchievementCounters),
        };
}
