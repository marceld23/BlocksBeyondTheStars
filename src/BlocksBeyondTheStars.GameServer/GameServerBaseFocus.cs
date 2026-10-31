// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A base's focus (#3036–#3040, Justus 2026-10-30: "a base can only have one upgrade, which can be changed"): the owner picks
/// one at the core screen, for free (Marcel). It lives with the core's yield store (<see cref="BaseYieldStore.Focus"/>, saved
/// with the metadata) and steers what already exists rather than adding new machinery:
/// <list type="bullet">
/// <item><b>Miner</b> (#3037): the yield (#2999) follows the shaft the players dug below the core — the deepest player edit in
/// the column under the core zone (<see cref="BaseShaftDepth"/>, one bounded query per seam piece, cached 10 s). The rate is
/// × <see cref="BaseFocus.MinerRateFactor"/>, and every vein whose start depth the shaft reaches joins the table, the
/// rare-tier ones too.</item>
/// <item><b>Lumberjack</b> (#3038): a resident becomes the woodcutter (<see cref="TickBaseWoodcutter"/>, the lodges' felling
/// core, never inside a base zone nor on a player's tree, always replanting) and the core gathers wood — on a body with
/// trees; on a barren one the core keeps gathering ore and the screen says so.</item>
/// <item><b>Market</b> (#3039, <c>GameServerBaseGuests</c>) and <b>Radio centre</b> (#3040, <c>GameServerNpcRadio</c>) work
/// on the visitors and the radio.</item>
/// </list>
/// Switching accrues the store first, so the old focus pays the time it ran for and the new one only what comes after.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>#3038: the job of a Lumberjack base's woodcutter. Its own key, not the lodges' "woodcutter": a base resident
    /// talks and is listed as a settler at work, never with a lodge's dialogues.</summary>
    internal const string BaseLumberjackJob = "lumberjack";

    /// <summary>How long a measured shaft depth is trusted (a Miner's screen and its yield read it).</summary>
    private const double BaseShaftCacheSeconds = 10.0;

    /// <summary>Base id → the shaft depth below its core and when it was measured.</summary>
    private readonly Dictionary<int, (int Depth, double At)> _baseShaftDepths = new();

    /// <summary>The base's focus (<see cref="BaseFocus"/>), read without creating a store.</summary>
    private string BaseFocusOf(ServerBase b)
        => _meta.BaseYields.TryGetValue(BaseYieldKey(b), out var store) ? BaseFocus.Normalize(store.Focus) : BaseFocus.None;

    /// <summary>Whether the base's focus is the Market (#3039): only such a base gets travelling merchants and draws traders.</summary>
    private bool IsMarketBase(ServerBase b) => BaseFocusOf(b) == BaseFocus.Market;

    /// <summary>The owner sets one of their bases' focus (#3036) — any time, for free. Unknown values mean none.</summary>
    private void SetBaseFocus(PlayerSession session, string? bodyId, Vector3i cell, string? focus)
    {
        string body = string.IsNullOrEmpty(bodyId) ? session.CurrentLocationId : bodyId!;
        var b = FindOwnBase(session.State.PlayerId, body, true, cell);
        if (b is null)
        {
            Reject(session, "base", "@srv.base.none_here");
            return;
        }

        string next = BaseFocus.Normalize(focus);
        var store = AccrueBaseYield(b, out _); // the old focus pays the time it ran for
        if (store.Focus == next)
        {
            return;
        }

        store.Focus = next;
        _baseShaftDepths.Remove(b.Id);
        _repo.SaveMetadata(_meta);
        BroadcastBasesOn(body);
        Send(session, new ServerMessage { Text = "@" + BaseFocus.NameKey(next) + "_set" });
    }

    /// <summary>The table and the rate multiple a base's store fills from under its focus.</summary>
    private (KeyValuePair<string, int>[] Table, double Factor) BaseYieldPlan(ServerBase b, string? focus)
    {
        switch (BaseFocus.Normalize(focus))
        {
            case BaseFocus.Miner:
                int depth = BaseShaftDepth(b);
                return (YieldTableOf(BaseYieldVeins(b.Planet).Where(v => BaseFocus.MinerReaches(depth, v.MinDepth))),
                    BaseFocus.MinerRateFactor(depth));
            case BaseFocus.Lumberjack when BodyHasTrees(b.Planet):
                return (BaseFocus.LumberjackYield, 1.0);
            default:
                return (BaseYieldTable(b.Planet), 1.0);
        }
    }

    /// <summary>Whether trees grow on the body (a living, non-void world) — a Lumberjack base gathers wood only there.</summary>
    private bool BodyHasTrees(string body)
    {
        var celestial = _galaxy?.FindBody(body);
        var planet = celestial is null || string.IsNullOrEmpty(celestial.PlanetType) ? null : _content.GetPlanet(celestial.PlanetType!);
        return planet is { Void: false, HasLife: true };
    }

    /// <summary>How deep the players dug below the base core (#3037): the core's Y minus the lowest player edit in the column
    /// under the core zone, at most <see cref="BaseFocus.MinerMaxDepth"/>; 0 without a shaft.</summary>
    private int BaseShaftDepth(ServerBase b)
    {
        if (_baseShaftDepths.TryGetValue(b.Id, out var cached) && _uptime - cached.At < BaseShaftCacheSeconds)
        {
            return cached.Depth;
        }

        int circ = BodyCircumference(b.Planet);
        int r = WorldConstants.BaseZoneRadius;
        int deepest = int.MaxValue;
        foreach (var (xLo, xHi) in CanonicalRanges(b.Cell.X - r, b.Cell.X + r, 0, circ))
        {
            foreach (var (zLo, zHi) in CanonicalRanges(b.Cell.Z - r, b.Cell.Z + r,
                         -WorldConstants.LatitudePeriodFor(circ) / 2, WorldConstants.LatitudePeriodFor(circ)))
            {
                if (_repo.TryGetPlayerBlockEditBounds(b.Planet, new Vector3i(xLo, b.Cell.Y - BaseFocus.MinerMaxDepth, zLo),
                        new Vector3i(xHi, b.Cell.Y - 1, zHi), out var lo, out _))
                {
                    deepest = Math.Min(deepest, lo.Y);
                }
            }
        }

        int depth = deepest == int.MaxValue ? 0 : b.Cell.Y - deepest;
        _baseShaftDepths[b.Id] = (depth, _uptime);
        return depth;
    }

    /// <summary>The focus part of a core screen's status: the focus, the rate under it, a Miner's depth and the next ore down,
    /// a Lumberjack on a barren body.</summary>
    private void FillFocusStatus(YieldStatus reply, ServerBase b, BaseYieldStore store)
    {
        string focus = BaseFocus.Normalize(store.Focus);
        reply.Focus = focus;
        var (_, factor) = BaseYieldPlan(b, focus);
        reply.PerHour = (float)(BaseRules.YieldPerHour(reply.Helpers, Rules.BaseYieldPercent) * factor);
        if (focus == BaseFocus.Miner)
        {
            reply.Depth = BaseShaftDepth(b);
            var next = BaseYieldVeins(b.Planet).Where(v => !BaseFocus.MinerReaches(reply.Depth, v.MinDepth))
                .OrderBy(v => v.MinDepth).ThenBy(v => v.Item, StringComparer.Ordinal).FirstOrDefault();
            reply.NextOre = next.Item ?? string.Empty;
            reply.NextOreDepth = next.Item is null ? 0 : next.MinDepth;
        }
        else if (focus == BaseFocus.Lumberjack)
        {
            reply.NoTrees = !BodyHasTrees(b.Planet);
        }
    }

    // ---------------- the woodcutter (#3038) ----------------

    /// <summary>A Lumberjack base's woodcutter on duty: the lodges' felling machine (<see cref="NpcWorkSystem"/>) round the core
    /// — trees within <see cref="BaseFocus.LumberjackRadius"/>, never in a base zone nor a player's (the felling core's
    /// protection), a sapling planted for each — and the logs go into a base crate, else into the core's store.</summary>
    private void TickBaseWoodcutter(ServerNpc npc, ServerBase b, BaseIndex idx, List<PlayerSession> targets)
    {
        if (npc.WorkState?.Plan is not { Kind: NpcWorkKind.Chop } plan)
        {
            npc.WorkState ??= new NpcWorkState();
            plan = new NpcWorkPlan
            {
                Kind = NpcWorkKind.Chop,
                Center = BaseCorePos(b),
                Radius = BaseFocus.LumberjackRadius,
                Bank = (item, count) => BankBaseWood(b, idx, item, count),
                ActivityKey = "npc.activity.chopping",
                Held = "npc_axe",
            };
            npc.WorkState.Plan = plan;
        }

        TickNpcWork(npc, plan, targets);
    }

    /// <summary>Logs a base's woodcutter brought: into a base crate that takes them, else into the core's store (up to its
    /// capacity) — nothing beyond is kept.</summary>
    private void BankBaseWood(ServerBase b, BaseIndex idx, string item, int count)
    {
        var drop = new[] { new ItemAmount(item, count) };
        if (BaseCrateFor(idx, drop) is { } crate && NpcDepositToContainer(crate, drop))
        {
            return;
        }

        var store = YieldStore(BaseYieldKey(b));
        int room = Math.Max(0, BaseRules.YieldCapacity - store.CountReady());
        int put = Math.Min(room, count);
        if (put > 0)
        {
            store.Items[item] = store.Items.TryGetValue(item, out int n) ? n + put : put;
        }
    }

    /// <summary>Test seam (#3036): the focus of the player's base at a cell on the active world.</summary>
    public string BaseFocusForTest(PlayerSession session, Vector3i cell)
        => FindOwnBase(session.State.PlayerId, session.CurrentLocationId, true, cell) is { } b ? BaseFocusOf(b) : "?";

    /// <summary>Test seam (#3037): forgets the measured shaft depths (a test digs, then reads at once).</summary>
    public void ForgetBaseShaftsForTest() => _baseShaftDepths.Clear();
}
