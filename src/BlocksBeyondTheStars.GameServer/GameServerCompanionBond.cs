// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Feeding a companion, and what its bond is FOR (#1225).
///
/// <c>TamedCreature.Bond</c> was written once at taming and read in exactly one place. Its own doc comment
/// said "room to grow" — this is that growth: the number now goes up when you look after the animal, down
/// slowly when you do not, and three things hang off it that a player can actually notice.
///
/// <list type="bullet">
/// <item><b>50 — a wider reach.</b> The fetch radius of #1210 grows by half again, so a well-fed pet is
/// visibly better at hoovering up what you mined.</item>
/// <item><b>70 — the bandit ward.</b> Already shipped with #1210; the tier table just gives it a name and a
/// place in the ladder rather than being an unexplained magic number.</item>
/// <item><b>90 — a nose for the place.</b> Every few minutes a present companion may reveal what the world's
/// NPCs would share — in practice the crashed wreck, since a companion keeps no relationship memory and the
/// deeper hints are reserved for people an NPC knows. Bounded on purpose: one landmark, once.</item>
/// </list>
///
/// Decay is one point per real day since the last meal, with a hard floor at
/// <see cref="TamedCreature.BondFloor"/> — where a freshly tamed animal starts. Time away can cost the
/// perks; it can never cost the friendship, and a child who was on holiday does not come back to a pet that
/// has forgotten them.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Bond gained per feeding.</summary>
    private const int BondPerFeed = 5;

    /// <summary>Shortest gap between two meals for one companion. The bait is the real cost; this only
    /// stops a double-click from spending two of them.</summary>
    private const double FeedCooldownSeconds = 60.0;

    /// <summary>Bond at which the fetch radius grows.</summary>
    private const int CompanionFetchBond = 50;

    /// <summary>How much wider the fetch reach gets at <see cref="CompanionFetchBond"/>.</summary>
    private const float CompanionFetchBondBonus = 1.5f;

    /// <summary>Bond at which a present companion occasionally shares what it has sniffed out.</summary>
    private const int CompanionScoutBond = 90;

    /// <summary>How often a scouting companion may share something.</summary>
    private const double CompanionScoutInterval = 300.0;

    /// <summary>The bait items a companion accepts — any of them. Taming already hides a per-animal
    /// preference; making the pet refuse the food a child actually brought is a bad second lesson.</summary>
    private static readonly string[] CompanionFoods = { "forage_bait", "meat_bait", "nectar_lure" };

    /// <summary>The first accepted bait the player is carrying, or null.</summary>
    private static string? CarriedCompanionFood(PlayerSession session)
    {
        foreach (string food in CompanionFoods)
        {
            if (session.State.Inventory.Has(food, 1))
            {
                return food;
            }
        }

        return null;
    }

    /// <summary>Whether the Feed button should be live: bait in the pack, bond not already full, and the
    /// last meal long enough ago. Sent per companion so the button can be dimmed instead of failing.</summary>
    private bool CanFeedCompanion(PlayerSession session, TamedCreature tc)
        => tc.Bond < 100
           && CarriedCompanionFood(session) is not null
           && NowUnixMs() - tc.LastFedUtc >= (long)(FeedCooldownSeconds * 1000);

    /// <summary>Feeds one companion: spends a bait, raises the bond, tells the player what it changed.</summary>
    private void HandleFeedCompanion(PlayerSession session, FeedCompanionIntent intent)
    {
        var tc = session.State.TamedCreatures.FirstOrDefault(t => t.Id == intent.CompanionId);
        if (tc is null)
        {
            return;
        }

        ApplyBondDecay(tc); // count the time apart BEFORE the meal, or a feed would paper over it silently

        if (tc.Bond >= 100)
        {
            Reject(session, "companion", "@srv.companion.bond_full");
            return;
        }

        long now = NowUnixMs();
        if (now - tc.LastFedUtc < (long)(FeedCooldownSeconds * 1000))
        {
            Reject(session, "companion", "@srv.companion.fed_recently");
            return;
        }

        if (CarriedCompanionFood(session) is not { } food)
        {
            Reject(session, "companion", "@srv.companion.no_food");
            return;
        }

        session.State.Inventory.Remove(food, 1);
        int before = tc.Bond;
        tc.Bond = Math.Min(100, tc.Bond + BondPerFeed);
        tc.LastFedUtc = now;

        _repo.SavePlayer(session.State);
        SendInventory(session);
        SendCompanions(session);

        // Say what it bought, not just that it happened: the tiers are the whole point of the number.
        string name = string.IsNullOrEmpty(tc.Name) ? tc.Species.Name : tc.Name;
        Send(session, new ServerMessage { Text = "@srv.companion.fed:" + name });
        if (CrossedTier(before, tc.Bond, CompanionFetchBond))
        {
            Send(session, new ServerMessage { Text = "@srv.companion.tier_fetch:" + name });
        }
        else if (CrossedTier(before, tc.Bond, CompanionBanditWardBond))
        {
            Send(session, new ServerMessage { Text = "@srv.companion.tier_ward:" + name });
        }
        else if (CrossedTier(before, tc.Bond, CompanionScoutBond))
        {
            Send(session, new ServerMessage { Text = "@srv.companion.tier_scout:" + name });
        }
    }

    private static bool CrossedTier(int before, int after, int tier) => before < tier && after >= tier;

    /// <summary>Applies the bond a companion lost while it was not being fed: one point per whole real day
    /// since its last meal, never below the floor. Called on join and before a feeding — both are moments
    /// that already touch the animal, and keying the loss on <see cref="TamedCreature.LastFedUtc"/> (which
    /// is advanced by the same amount) makes it impossible to charge the same day twice.</summary>
    private static void ApplyBondDecay(TamedCreature tc)
    {
        long since = tc.LastFedUtc > 0 ? tc.LastFedUtc : tc.TamedAtUtc;
        if (since <= 0 || tc.Bond <= TamedCreature.BondFloor)
        {
            return;
        }

        const long day = 24L * 60 * 60 * 1000;
        long elapsed = NowUnixMs() - since;
        if (elapsed < day)
        {
            return;
        }

        int days = (int)Math.Min(elapsed / day, 100);
        tc.Bond = Math.Max(TamedCreature.BondFloor, tc.Bond - days);
        tc.LastFedUtc = since + (days * day); // charge whole days only; the remainder keeps ticking
    }

    /// <summary>Applies decay to every companion of a session (join). Returns true when anything changed, so
    /// the caller only writes the save when it must.</summary>
    private static bool ApplyBondDecay(PlayerState player)
    {
        bool changed = false;
        foreach (var tc in player.TamedCreatures)
        {
            int before = tc.Bond;
            ApplyBondDecay(tc);
            changed |= before != tc.Bond;
        }

        return changed;
    }

    /// <summary>This companion's fetch radius — wider once the animal is well looked after (#1225).</summary>
    private static float CompanionFetchRadiusFor(TamedCreature? tc)
        => tc is not null && tc.Bond >= CompanionFetchBond
            ? CompanionFetchRadius * CompanionFetchBondBonus
            : CompanionFetchRadius;

    /// <summary>The scouting tier: every <see cref="CompanionScoutInterval"/> s a present, deeply bonded
    /// companion may reveal a landmark. It borrows the NPC hint pool, and because a companion keeps no
    /// relationship memory only the "anyone may know this" entry — the world's crashed wreck — can ever come
    /// out of it. That is the intended size of the perk, not a limitation to work around.</summary>
    private void TickCompanionScouting()
    {
        if (_uptime < _nextCompanionScoutAt)
        {
            return;
        }

        _nextCompanionScoutAt = _uptime + CompanionScoutInterval;
        foreach (var creature in _creatures)
        {
            if (!creature.IsCompanion || CompanionOwnerHere(creature) is not { } owner)
            {
                continue;
            }

            var tc = owner.State.TamedCreatures.FirstOrDefault(t => t.Id == creature.CompanionId);
            if (tc is null || tc.Bond < CompanionScoutBond)
            {
                continue;
            }

            string line = TryEmitHint(owner, "companion:" + tc.Id, forceRoll: true);
            if (line.Length == 0)
            {
                continue;
            }

            string name = string.IsNullOrEmpty(tc.Name) ? tc.Species.Name : tc.Name;
            Send(owner, new ServerMessage { Text = "@srv.companion.scouted:" + name });
            Send(owner, new ServerMessage { Text = line });
            return; // one find per window, whoever's pet got there first
        }
    }

    private double _nextCompanionScoutAt;

    /// <summary>Wall-clock unix ms — the bond clock, like <c>TamedAtUtc</c>. Overridable for tests so decay
    /// can be exercised without waiting a day.</summary>
    private static long NowUnixMs() => UnixMsOverrideForTest ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Test seam: pins the bond clock (#1225). Null = the real clock.</summary>
    public static long? UnixMsOverrideForTest { get; set; }

    /// <summary>Test seam: feeds a companion as the intent would (#1225).</summary>
    public void FeedCompanionForTest(string playerId, string companionId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleFeedCompanion(s, new FeedCompanionIntent { CompanionId = companionId });
        }
    }

    /// <summary>Test seam: the fetch radius a companion currently has (#1225).</summary>
    public float CompanionFetchRadiusForTest(string playerId, string companionId)
        => CompanionFetchRadiusFor(FindSessionByPlayerId(playerId)?.State.TamedCreatures.FirstOrDefault(t => t.Id == companionId));
}
