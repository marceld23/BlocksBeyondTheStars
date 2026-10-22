// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The server side of the tutorial's deep dives "More to discover" (#2482, synthesis §3.2/§3.4): C9 Animal friends, C10
/// Gardener and lab, C11 Neighbours, C12 Defence, C13 On the move, C15 Together, C16 Creative tools, C18 Explorer. The
/// <c>world:</c> predicates their offers, visibility and feasibility read (registered in <c>GameServerStoryConditions</c>) —
/// each reads the player's own body, never the active world cursor — and the small helpers their game events need.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How long after an animal came to beg <c>world:herd_begging</c> holds — long enough for the offer pass to find
    /// it, short enough that a herd long ago is no "moment of need".</summary>
    private const double HerdBeggingOfferWindowSeconds = 60.0;

    /// <summary>How close (blocks) something hostile has to be for <c>world:hostile_near</c>.</summary>
    private const double HostileNearRange = 40.0;

    /// <summary><c>world:herd_begging</c>: an animal came to beg this player within the last minute.</summary>
    private bool HerdBegging(PlayerSession who) => _uptime - who.Tutorial.HerdBeggedAt <= HerdBeggingOfferWindowSeconds;

    /// <summary><c>world:seed_carried</c>: the player carries something that plants — a sapling, a seed, a bred seedling (an
    /// item whose block is a plant).</summary>
    private bool SeedCarried(PlayerSession who)
    {
        foreach (var stack in who.State.Inventory.Slots)
        {
            if (stack is { IsEmpty: false }
                && _content.GetItem(ItemKey.Base(stack.Item))?.PlacesBlock is { Length: > 0 } block
                && _content.GetBlock(block) is { } def
                && IsFlora(def.NumericId.Value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>world:hostile_near</c>: a machine, a hostile animal or a robber turned hostile stands within
    /// <see cref="HostileNearRange"/> of the player on their own world (never in space, never aboard).</summary>
    private bool HostileNear(PlayerSession who)
    {
        var p = who.State;
        if (p.AboardShip || InSpace(p.PlayerId) || InStation(p.PlayerId) || _worlds.Find(who.CurrentLocationId) is not { } world)
        {
            return false;
        }

        int circ = world.World.Circumference;
        const double r2 = HostileNearRange * HostileNearRange;
        bool Near(CombatEntity e) => WorldConstants.WrapDistanceSquared(p.Position, e.Position, circ) <= r2;
        return world.PlanetEnemies.Any(Near)
               || world.Bandits.Any(b => b.Hostile && Near(b))
               || world.Creatures.Any(c => c.Hostile && !c.IsCompanion && Near(c));
    }

    /// <summary><c>world:train_here</c>: a monorail train runs on the player's world that they may board — a public one, their
    /// own or an ally's (the boarding rule of <c>HandleEnterTrain</c>).</summary>
    private bool TrainHere(PlayerSession who)
    {
        string me = who.State.PlayerId;
        return _worlds.Find(who.CurrentLocationId) is { } world
               && world.Rails.Trains.Any(t => RailRules.IsPublic(t.OwnerId) || t.OwnerId == me || AlliedWith(t.OwnerId, me));
    }

    /// <summary>The <c>biolab:used</c> argument of a bio-lab action.</summary>
    internal static string BioLabActionArg(int action) => action switch
    {
        BlocksBeyondTheStars.Networking.Messages.BioLabIntent.Analyse => "analyse",
        BlocksBeyondTheStars.Networking.Messages.BioLabIntent.Mix => "mix",
        BlocksBeyondTheStars.Networking.Messages.BioLabIntent.Change => "change",
        BlocksBeyondTheStars.Networking.Messages.BioLabIntent.WashOff => "wash",
        BlocksBeyondTheStars.Networking.Messages.BioLabIntent.Seedling => "seedling",
        _ => "other",
    };

    /// <summary>The <c>sample:taken</c> argument of a sample's kind.</summary>
    private static string SampleKindArg(BioKind kind) => kind switch
    {
        BioKind.Animal => "animal",
        BioKind.Mineral => "mineral",
        _ => "plant",
    };

    /// <summary>Test seam: a buried vault's entrance at <paramref name="cell"/> on the active world (what <c>NearVegaPoi</c> and
    /// VEGA's <c>poi</c> hint look for), without stamping a vault.</summary>
    internal void AddVaultEntranceForTest(Vector3i cell) => _vaultEntrances.Add(cell);
}
