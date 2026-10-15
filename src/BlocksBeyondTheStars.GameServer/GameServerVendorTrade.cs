// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Trading with the vendor you chose (Screelit 2026-10-15, "the doctor doesn't sell what he should"):
/// <list type="bullet">
/// <item><b>One reach rule (#2626).</b> A barter is accepted within <see cref="MarketStock.TradeReach"/> of a trading post
/// or within <see cref="MarketStock.VendorReach"/> of a vendor (<see cref="NearVendorNpc"/>); a themed deal needs a vendor
/// within <see cref="MarketStock.VendorReach"/> that sells it (<see cref="ThemedMarketRefusal"/>) — ANY such vendor, not
/// just the nearest one. The client binds its trade screen to the vendor the player picked and keeps it while that vendor
/// stands within the same reach (Client.Core <c>VendorReach</c>), so a docked pilot beside the station's keeper, or two
/// stalls side by side, trade their own goods no matter who happens to stand a step closer.</item>
/// <item><b>Keepers hold the counter (#2627).</b> A trading keeper at work stands at its work spot facing a customer within
/// <see cref="KeeperHoldRange"/> instead of strolling off mid-trade (<see cref="KeeperCustomer"/>, used by MoveNpcs).</item>
/// <item><b>The doctor treats (#2628).</b> Dialogue consequence <see cref="TreatConsequence"/>: full health, free, once per
/// in-game day and player (a saved milestone); a healthy patient keeps today's visit.</item>
/// <item><b>Visitors bring their own goods (#2630).</b> A docked or landed trader pilot draws its trade theme from
/// <see cref="VisitingTraderThemes"/> per visit (a stable hash of the visit) — a docked one never the station shop's own
/// theme. The travelling merchant at a base sells the <see cref="MarketStock.MixedTheme"/> stock.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    // ---------------- reach (#2626) ----------------

    /// <summary>The vendor NPCs within <see cref="MarketStock.VendorReach"/> of the player, nearest first.</summary>
    private List<ServerNpc> VendorsInReach(PlayerState player)
    {
        var found = new List<(ServerNpc Npc, double DistSq)>();
        double reachSq = MarketStock.VendorReach * (double)MarketStock.VendorReach;
        foreach (var npc in _npcs)
        {
            if (npc.Role != "vendor" || npc.VisitingAway)
            {
                continue;
            }

            double d = WrapDistSq(player.Position, npc.Pos);
            if (d <= reachSq)
            {
                found.Add((npc, d));
            }
        }

        return found.OrderBy(f => f.DistSq).Select(f => f.Npc).ToList();
    }

    /// <summary>True if a vendor stands within <see cref="MarketStock.VendorReach"/> of the player — barter works beside any
    /// trader, the same reach the client keeps its trade screen bound to the chosen one (#2626).</summary>
    private bool NearVendorNpc(PlayerState player)
    {
        double reachSq = MarketStock.VendorReach * (double)MarketStock.VendorReach;
        foreach (var npc in _npcs)
        {
            if (npc.Role == "vendor" && !npc.VisitingAway && WrapDistSq(player.Position, npc.Pos) <= reachSq)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Why a themed market recipe is refused here, or "" when a vendor within <see cref="MarketStock.VendorReach"/>
    /// deals in it today (<see cref="MarketStock.SellsKind"/>) — any of them, nearest first, so the vendor the client's screen
    /// is bound to counts even when another one stands a step closer. The grocer (2026-09) only sells inside the shop: the
    /// customer must stand in the keeper's closed room. The rotation ("not today") is checked by the caller.</summary>
    private string ThemedMarketRefusal(PlayerState player, RecipeDefinition recipe)
    {
        bool sells = false;
        foreach (var vendor in VendorsInReach(player))
        {
            if (!MarketStock.SellsKind(_content.Recipes.Values, vendor.Theme, recipe, MarketDay))
            {
                continue;
            }

            sells = true;
            if (NpcProfessions.ByJob(vendor.Job) is { ShopOnly: true } && !InSameClosedRoom(player.Position, vendor.Pos))
            {
                continue;
            }

            return string.Empty;
        }

        return sells ? "@srv.craft.shop_only" : "@srv.craft.wrong_vendor";
    }

    // ---------------- keepers hold the counter (#2627) ----------------

    /// <summary>How close to a keeper's work spot a player counts as a customer — the 4-block trade reach and a little more,
    /// so a customer at its edge still holds the keeper.</summary>
    private const float KeeperHoldRange = 4.5f;

    /// <summary>How far from its work spot a holding keeper may stand before it walks back.</summary>
    private const float KeeperHoldSlack = 0.35f;

    /// <summary>The customer a trading keeper (a vendor of any kind, the quartermaster) stands still for: the nearest player
    /// within <see cref="KeeperHoldRange"/> of its spot while it is at work — idle there by day (a routine NPC) or at any
    /// time (a visiting pilot or merchant, which has no routine). Null otherwise: at night, in the evening, on its way
    /// somewhere, or for anyone who doesn't trade, nothing changes.</summary>
    private Vector3f? KeeperCustomer(ServerNpc npc, List<PlayerSession> targets)
    {
        if (npc.Role is not ("vendor" or "quartermaster") || npc.Goal is not null || npc.Pose != 0
            || (npc.RoutineEnabled && npc.Phase != NpcPhase.Day))
        {
            return null;
        }

        Vector3f? best = null;
        double bestSq = KeeperHoldRange * (double)KeeperHoldRange;
        foreach (var s in targets)
        {
            if (s.Spectating)
            {
                continue;
            }

            double d = WrapDistSq(s.State.Position, npc.Home);
            if (d <= bestSq)
            {
                bestSq = d;
                best = s.State.Position;
            }
        }

        return best;
    }

    /// <summary>Whether a holding keeper stands at its spot (within <see cref="KeeperHoldSlack"/>, horizontally).</summary>
    private bool KeeperAtSpot(ServerNpc npc)
    {
        double dx = WorldConstants.WrapDeltaX((double)npc.Pos.X - npc.Home.X, _world.Circumference);
        double dz = npc.Pos.Z - npc.Home.Z;
        return dx * dx + dz * dz <= KeeperHoldSlack * (double)KeeperHoldSlack;
    }

    // ---------------- the doctor treats (#2628) ----------------

    /// <summary>The doctor's "Treat me" consequence.</summary>
    internal const string TreatConsequence = "treat";

    /// <summary>Milestone prefix of the in-game day a player was last treated (<c>doctor:treated:&lt;day&gt;</c>) — saved
    /// with the player, so leaving and coming back the same day does not reset it. Only the latest day is kept.</summary>
    internal const string TreatedMilestonePrefix = "doctor:treated:";

    /// <summary>"Treat me": full health, free, once per in-game day (<see cref="MarketDay"/>) per player. A second ask the same
    /// day gets "come back tomorrow"; a healthy patient hears so and keeps today's visit. The closing line carries the
    /// <c>heal</c> action (the client plays the heal sound).</summary>
    private void ApplyTreatConsequence(PlayerSession session)
    {
        var p = session.State;
        string today = TreatedMilestonePrefix + MarketDay.ToString(CultureInfo.InvariantCulture);
        if (p.Milestones.Contains(today))
        {
            _dialogResponseOverride = Localize(session.Locale, "dlg.doctor.r_treat_again");
            return;
        }

        if (p.Health >= PlayerFullHealth)
        {
            _dialogResponseOverride = Localize(session.Locale, "dlg.doctor.r_treat_healthy");
            return;
        }

        p.Milestones.RemoveWhere(m => m.StartsWith(TreatedMilestonePrefix, System.StringComparison.Ordinal));
        p.Milestones.Add(today);
        p.Health = PlayerFullHealth;
        SendPlayerState(session);
        _dialogAction = "heal";
    }

    /// <summary>A player's full health (the medbay, the heal tank and the bed all fill to it).</summary>
    private const float PlayerFullHealth = 100f;

    // ---------------- visiting traders bring their own goods (#2630) ----------------

    /// <summary>The trades a visiting trader pilot may bring — the four settlement trades plus the arms dealer's, the
    /// doctor's and the blockfarmer's goods (a ship can carry those). Never the rail parts, the grocer's shop goods, the
    /// sage's lore or the tamer's baits: those belong to their keepers at home.</summary>
    internal static readonly string[] VisitingTraderThemes = { "traders", "miners", "researchers", "settlers", "arms", "medics", "blocks" };

    /// <summary>The trade theme a visiting trader brings on the visit named <paramref name="visitKey"/> — a stable hash of
    /// the visit, so it stays the same for the whole stay. <paramref name="shopTheme"/> (the station shop's own theme) is
    /// never drawn: a docked visitor brings something the shop does not have.</summary>
    internal static string VisitingTraderTheme(string visitKey, string? shopTheme)
    {
        var pool = VisitingTraderThemes.Where(t => !string.Equals(t, shopTheme, System.StringComparison.Ordinal)).ToArray();
        return pool[(int)((uint)WorldGenerator.StableHash("visit-theme:" + visitKey) % (uint)pool.Length)];
    }

    /// <summary>The station shop's theme: a station's first vendor always keeps <c>traders</c> (its identity).</summary>
    private const string StationShopTheme = "traders";

    /// <summary>The goods a trader landed on a planet's pad brings (#2630): drawn per landing from the body and the
    /// trader's id — there is no shop beside a pad, so any of <see cref="VisitingTraderThemes"/>.</summary>
    private static string LandedTraderTheme(NpcLandedTrader lt)
        => VisitingTraderTheme("landed:" + lt.BodyId + ":" + lt.Id + ":" + lt.Name, shopTheme: null);

    /// <summary>Test seam (#2630): the goods a body's landed (or approaching) trader brings, or null if none.</summary>
    public string? LandedTraderThemeForTest(string bodyId)
        => _landedTraders.TryGetValue(bodyId, out var lt) ? LandedTraderTheme(lt) : null;

    /// <summary>Test seam (#2630): the theme of the trader docked at a station, or null if none is docked.</summary>
    public string? VisitingTraderThemeForTest(string stationId)
        => _visitingTraders.TryGetValue(stationId, out var vt) ? vt.Theme : null;
}
