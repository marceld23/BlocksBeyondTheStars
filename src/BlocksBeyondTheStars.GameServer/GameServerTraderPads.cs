// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Where an arriving trader sets down (#2603 K12, #3073, #3175) — planet-side and from the flight scene alike. A tester set his
/// base to Market and waited a 74-minute session for a trader that never came near him: his pads were on the default
/// "Me + allies" (only "Everyone" pads took traders), his own ship stood on one, and the communal fallback took the next free
/// public pad wherever it lay — a thousand blocks off on a large planet — while the toasts still said "look up!" and "landed
/// nearby". Now:
/// <list type="bullet">
/// <item>A Market base's own free pads take traders on <b>Allies and Everyone</b> (<see cref="BasePadRules.AdmitsTraders"/>) —
/// never on "Only me".</item>
/// <item>For a Market base the communal fallback takes a pad only within <see cref="TraderCommunalPadReach"/> of the base;
/// beyond it the trader does not come this time. Without a Market base the nearest free pad is taken wherever it lies.</item>
/// <item>The toasts and the hail are honest: a pad farther than <see cref="TraderNearbyRadius"/> from the player — or from the
/// owner's base, for the hail — is named with its distance and compass direction.</item>
/// <item>A present Market base owner whose base offers the traders no pad hears VEGA say why, once per base and reason.</item>
/// <item>Every decision is logged: the pad chosen, or why there is none.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far (blocks, the short way round both seams, height included) the communal pad an arriving trader takes may lie
    /// from the nearest present owner's Market base core (#3175). About a minute's walk:
    /// a base built beside a public pad has it in reach, a pad on the far side of the planet does not, so "look up!" and "parked
    /// near your base" stay true. Beyond it the trader does not come this time — a Market base builds a pad of its own instead.
    /// Without a Market base anchor (players only, or a flight-scene landing nobody watches) there is no cap: the toasts name
    /// the far pad's distance and direction instead.</summary>
    internal const int TraderCommunalPadReach = 250;

    /// <summary>A pad within this horizontal distance (blocks) of a player — or of the owner's base, for the hail — is "right
    /// here" (#3175): the toast says "look up!", the hail "parked near your base". Farther away both name the distance and the
    /// direction. A base's own pads always count as near it (they stand at most <see cref="BasePadRules.ReachXZ"/> out per axis).</summary>
    internal const float TraderNearbyRadius = 80f;

    /// <summary>The once-flag id prefix of the #3175 VEGA hint: <c>market_pad:&lt;body&gt;|x|y|z:&lt;reason&gt;</c>, one per base and
    /// reason; the line is <c>vega.hint.market_pad_&lt;reason&gt;</c> (<see cref="MarketPadHintLine"/>).</summary>
    private const string MarketPadHintPrefix = "market_pad:";

    /// <summary>The pad an arriving trader takes on <paramref name="bodyId"/> (#3175): a free Market base pad first
    /// (<see cref="MarketBasePadFor"/>), else the nearest free dry communal pad within <see cref="TraderCommunalPadReach"/>
    /// (<see cref="NearestFreeDryPad"/>). Logs the decision; without a Market base pad the present Market base owners hear once
    /// why their base offers none. <paramref name="found"/> is false when there is no pad — the trader does not come.</summary>
    private (Vector3i? BasePad, int PadIndex) PickTraderPad(string bodyId, List<PlayerSession> present, bool hailedOwnersFirst, out bool found)
    {
        var marketPad = MarketBasePadFor(bodyId, present, hailedOwnersFirst);
        if (marketPad is { } cell)
        {
            found = true;
            _log.Info(FormattableString.Invariant($"Trader pad on {bodyId}: the Market base pad at {cell.X},{cell.Y},{cell.Z}."));
            return (cell, -1);
        }

        int pad = NearestFreeDryPad(bodyId, present, hailedOwnersFirst, out int nearest, out double distance);
        found = pad >= 0;
        if (found)
        {
            _log.Info(FormattableString.Invariant($"Trader pad on {bodyId}: communal pad {pad} ({distance:0} blocks from the nearest anchor)."));
        }
        else if (nearest >= 0)
        {
            _log.Info(FormattableString.Invariant(
                $"Trader pad on {bodyId}: none — no free Market base pad, and the nearest free dry pad ({nearest}) lies {distance:0} blocks off (more than {TraderCommunalPadReach}); no trader this time."));
        }
        else
        {
            _log.Info($"Trader pad on {bodyId}: none — no free Market base pad and no free dry pad; no trader this time.");
        }

        HintMarketPadTrouble(bodyId, present, found);
        return (null, pad);
    }

    /// <summary>The free pad (#2603, K12) an arriving trader takes — planet-side and from the flight scene alike: not held
    /// by a player or another trader, not under water, not in lava, not built over (#3146) — the one nearest a present
    /// player's Market base core on this body, else nearest a player here, so "we're parked near your base" is true; and
    /// (#3175) for a Market base no farther than <see cref="TraderCommunalPadReach"/> from it. With nobody to be near, the lowest free
    /// dry pad. -1 when none qualifies; <paramref name="nearest"/> and <paramref name="distance"/> name the nearest free dry pad
    /// even beyond the reach (for the log), -1 when there is none.</summary>
    /// <param name="hailedOwnersFirst">The flight scene's landing: nobody here watched it come in, its only "near" claim is
    /// the base owners' hail — so, without a present owner, the base of any joined owner (the hail reaches them wherever
    /// they are, up in space too) anchors the pad before the players standing here.</param>
    private int NearestFreeDryPad(string bodyId, List<PlayerSession> present, bool hailedOwnersFirst, out int nearest, out double distance)
    {
        var anchors = TraderPadAnchors(bodyId, present, hailedOwnersFirst, out bool marketAnchored);
        int circumference = BodyCircumference(bodyId);
        int best = -1;
        double bestSq = double.MaxValue;
        foreach (var pad in PadsForBody(bodyId))
        {
            // #3146: nor a pad somebody built over — the hull would be stamped into the building.
            if (pad.Wet || pad.Molten || PadOccupiedByOther(bodyId, pad.Index, string.Empty) || PadBuiltOver(bodyId, pad))
            {
                continue;
            }

            if (anchors.Count == 0)
            {
                best = best < 0 ? pad.Index : System.Math.Min(best, pad.Index);
                continue;
            }

            var centre = new Vector3f(pad.CenterX + 0.5f, pad.CenterY, pad.CenterZ + 0.5f);
            foreach (var a in anchors)
            {
                double d = WorldConstants.WrapDistanceSquared(a, centre, circumference);
                if (d < bestSq)
                {
                    bestSq = d;
                    best = pad.Index;
                }
            }
        }

        nearest = best;
        distance = best >= 0 && anchors.Count > 0 ? System.Math.Sqrt(bestSq) : 0.0;
        return marketAnchored && distance > TraderCommunalPadReach ? -1 : best;
    }

    /// <summary>The points a trader's communal pad is measured from: the cores of the present owners' Market bases; with
    /// <paramref name="hailedOwnersFirst"/> and none, those of any joined owner; else the players standing there.
    /// <paramref name="marketAnchored"/> is true when the anchors are Market bases: only then does the
    /// <see cref="TraderCommunalPadReach"/> cap apply — a Market base was promised a trader by its door, while a player without
    /// one is told the far pad's distance and direction instead (the communal pads lie far apart on a large planet; a cap there
    /// would keep traders away from most of the surface).</summary>
    private List<Vector3f> TraderPadAnchors(string bodyId, List<PlayerSession> present, bool hailedOwnersFirst, out bool marketAnchored)
    {
        var anchors = new List<Vector3f>();
        AddBaseAnchors(anchors, bodyId, ownerId => present.Any(s => s.State.PlayerId == ownerId));
        if (anchors.Count == 0 && hailedOwnersFirst)
        {
            AddBaseAnchors(anchors, bodyId, ownerId => FindSessionByPlayerId(ownerId) is { Spectating: false });
        }

        marketAnchored = anchors.Count > 0;
        if (anchors.Count == 0)
        {
            anchors.AddRange(present.Select(s => s.State.Position));
        }

        return anchors;
    }

    /// <summary>#3073/#3175: a free pad of a Market base on the body for an arriving trader — on <b>Allies or Everyone</b>
    /// (<see cref="BasePadRules.AdmitsTraders"/>; never "Only me"), held by nobody (the owner's own parked ship holds its pad),
    /// and not built over (#3146). The bases of owners standing there first, as for the communal pick; with
    /// <paramref name="hailedOwnersFirst"/> and nobody present, any joined owner's. A fixed order by cell. Null when no Market
    /// base offers one: the trader takes a communal pad instead.</summary>
    private Vector3i? MarketBasePadFor(string bodyId, List<PlayerSession> present, bool hailedOwnersFirst = false)
    {
        bool Present(string ownerId) => present.Any(s => s.State.PlayerId == ownerId);
        bool Joined(string ownerId) => FindSessionByPlayerId(ownerId) is { Spectating: false };
        var free = _basePads
            .Where(p => p.Planet == bodyId && BasePadRules.AdmitsTraders(p.Access) && BasePadHolderName(bodyId, p.Cell, string.Empty) is null
                        && BaseOfPad(p) is { } home && IsMarketBase(home))
            .OrderBy(p => p.Cell.X).ThenBy(p => p.Cell.Y).ThenBy(p => p.Cell.Z)
            .ToList();
        // #3146: an admin owner may have built in a pad's square — the first candidate that is still clear.
        var pick = free.FirstOrDefault(p => Present(p.OwnerId) && !BasePadBuiltOver(p))
                   ?? (hailedOwnersFirst ? free.FirstOrDefault(p => Joined(p.OwnerId) && !BasePadBuiltOver(p)) : null);
        return pick?.Cell;
    }

    /// <summary>Adds the core cell of every base on <paramref name="bodyId"/> whose owner passes <paramref name="ownerMatches"/>.</summary>
    private void AddBaseAnchors(List<Vector3f> anchors, string bodyId, System.Func<string, bool> ownerMatches)
    {
        foreach (var b in _bases)
        {
            if (b.Planet == bodyId && IsMarketBase(b) && ownerMatches(b.OwnerId)) // #3036: traders park by a Market base
            {
                anchors.Add(new Vector3f(b.Cell.X + 0.5f, b.Cell.Y, b.Cell.Z + 0.5f));
            }
        }
    }

    // ---------------- #3175: VEGA says why a Market base gets no trader ----------------

    /// <summary>Tells every present owner of a Market base on the body — once per base and reason — why traders cannot use the
    /// base's pads: their own ship stands on it, it is built over, it is set to "Only me", another ship holds it; or, when the
    /// base has no pad at all and no trader comes, that it needs one.</summary>
    private void HintMarketPadTrouble(string bodyId, List<PlayerSession> present, bool traderComes)
    {
        foreach (var b in _bases)
        {
            if (b.Planet != bodyId || !IsMarketBase(b) || present.FirstOrDefault(s => s.State.PlayerId == b.OwnerId) is not { } owner)
            {
                continue;
            }

            if (MarketPadTrouble(b, owner, traderComes) is not { } reason)
            {
                continue;
            }

            _log.Info(FormattableString.Invariant(
                $"Trader pad on {bodyId}: {owner.State.Name}'s Market base at {b.Cell.X},{b.Cell.Y},{b.Cell.Z} offers no pad ({reason})."));
            ShipAiHintOnce(owner, MarketPadHintId(b, reason));
        }
    }

    /// <summary>Why a Market base's pads take no trader (a reason id), or null when one of them could — or when the base has no
    /// pad at all but the trader comes anyway (a public pad nearby). Reasons by priority: <c>own_ship</c>, <c>built_over</c>,
    /// <c>owner_only</c>, <c>taken</c>; <c>none</c> for a base without a pad.</summary>
    private string? MarketPadTrouble(ServerBase home, PlayerSession owner, bool traderComes)
    {
        bool ownShip = false, builtOver = false, ownerOnly = false, taken = false, any = false;
        foreach (var p in _basePads)
        {
            if (p.Planet != home.Planet || p.OwnerId != home.OwnerId || BaseOfPad(p) != home)
            {
                continue;
            }

            any = true;
            if (!BasePadRules.AdmitsTraders(p.Access))
            {
                ownerOnly = true;
            }
            else if (BasePadHolderName(p.Planet, p.Cell, string.Empty) is not null)
            {
                ownShip |= HoldsBasePad(owner, p.Planet, p.Cell);
                taken |= !HoldsBasePad(owner, p.Planet, p.Cell);
            }
            else if (BasePadBuiltOver(p))
            {
                builtOver = true;
            }
            else
            {
                return null; // a pad a trader could take (another owner's base got this one)
            }
        }

        if (!any)
        {
            return traderComes ? null : "none";
        }

        return ownShip ? "own_ship" : builtOver ? "built_over" : ownerOnly ? "owner_only" : taken ? "taken" : null;
    }

    /// <summary>The once-flag id of the #3175 hint for one base and reason.</summary>
    private static string MarketPadHintId(ServerBase home, string reason)
        => FormattableString.Invariant($"{MarketPadHintPrefix}{home.Planet}|{home.Cell.X}|{home.Cell.Y}|{home.Cell.Z}:{reason}");

    /// <summary>The line id of a VEGA hint id: <c>market_pad_&lt;reason&gt;</c> for the #3175 hint (a once-flag per base, one line
    /// per reason), every other id unchanged — the tail of <c>HintLineId</c>.</summary>
    private static string MarketPadHintLine(string hintId)
        => hintId.StartsWith(MarketPadHintPrefix, System.StringComparison.Ordinal)
            ? "market_pad_" + hintId[(hintId.LastIndexOf(':') + 1)..]
            : hintId;

    /// <summary>#3177: the trader's approach as seen from the ground — a landing <see cref="ShipTransitFx"/> with
    /// <see cref="ShipTransitFx.ApproachSeconds"/> set, sent to the players on the body when the trader decides to land, so the
    /// client draws a glint and a contrail towards the pad while the toast says "look up!". The real descent follows at
    /// touchdown (<c>BroadcastNpcShipTransit</c>); no design is sent here and no animal is scared yet.</summary>
    private void BroadcastTraderApproach(string bodyId, NpcLandedTrader lt, Vector3f at)
    {
        ShipTransitFx? msg = null;
        foreach (var session in _sessions.Values)
        {
            if (!session.Joined || session.CurrentLocationId != bodyId || InSpace(session.State.PlayerId))
            {
                continue;
            }

            msg ??= new ShipTransitFx
            {
                PlayerId = lt.OwnerId,
                Name = lt.Name,
                X = at.X,
                Y = at.Y,
                Z = at.Z,
                Landing = true,
                Hull = lt.HullRgb,
                ApproachSeconds = (float)PlanetTraderApproachSeconds,
            };
            Send(session, msg);
        }
    }

    // ---------------- #3175: honest toasts and hails ----------------

    /// <summary>The centre of the pad a body's trader holds (its surface, where the hull stands), or null when it is unknown.</summary>
    private Vector3f? TraderPadCentre(NpcLandedTrader lt)
    {
        if (lt.BasePad is { } cell)
        {
            return new Vector3f(cell.X + 0.5f, cell.Y + 1f, cell.Z + 0.5f);
        }

        return PadsForBody(lt.BodyId).FirstOrDefault(p => p.Index == lt.PadIndex) is { } pad
            ? new Vector3f(pad.CenterX + 0.5f, pad.CenterY + 1f, pad.CenterZ + 0.5f)
            : null;
    }

    /// <summary>Whether <paramref name="pad"/> lies farther than <see cref="TraderNearbyRadius"/> (horizontally, the short way round
    /// both seams) from <paramref name="from"/> on the body — with the distance rounded to 10 m and the 8-way direction key from
    /// <paramref name="from"/> to the pad.</summary>
    private bool PadFarFrom(string bodyId, Vector3f from, Vector3f pad, out int distance, out string directionKey)
    {
        int circ = BodyCircumference(bodyId);
        double dx = (double)pad.X - from.X, dz = (double)pad.Z - from.Z;
        if (circ > 0)
        {
            dx = WorldConstants.WrapDeltaX(dx, circ);
            dz = WorldConstants.WrapDeltaZ(dz, circ);
        }

        double d = System.Math.Sqrt((dx * dx) + (dz * dz));
        distance = System.Math.Max(10, (int)(System.Math.Round(d / 10.0) * 10));
        directionKey = DirectionKey(dx, dz);
        return d > TraderNearbyRadius;
    }

    /// <summary>A far-pad line for <paramref name="s"/>: <c>{0}</c> the distance, <c>{1}</c> the direction, <c>{name}</c> the trader.</summary>
    private string TraderFarText(PlayerSession s, string key, int distance, string directionKey, string name = "")
        => Localize(s.Locale, key)
            .Replace("{0}", distance.ToString(CultureInfo.InvariantCulture))
            .Replace("{1}", Localize(s.Locale, directionKey))
            .Replace("{name}", name);

    /// <summary>The "coming in to land" toast for a player on the body: "look up!" when the pad is right here, else how far and
    /// which way.</summary>
    private ServerMessage TraderApproachMessage(PlayerSession s, string bodyId, Vector3f? pad)
        => pad is { } at && PadFarFrom(bodyId, s.State.Position, at, out int distance, out string direction)
            ? new ServerMessage { Text = TraderFarText(s, "srv.trader.approaching_far", distance, direction) }
            : new ServerMessage { Text = "@srv.trader.approaching" };

    /// <summary>The touchdown toast for a player on the body: "landed nearby" when it did, else how far and which way.</summary>
    private ServerMessage TraderLandedMessage(PlayerSession s, NpcLandedTrader lt, Vector3f? pad)
        => pad is { } at && PadFarFrom(lt.BodyId, s.State.Position, at, out int distance, out string direction)
            ? new ServerMessage { Text = TraderFarText(s, "srv.trader.landed_far", distance, direction, lt.Name) }
            : new ServerMessage { Text = "@srv.trader.landed:" + lt.Name };

    /// <summary>Whether the trader's pad lies far from <paramref name="owner"/>'s nearest base on its body — with the distance and
    /// the direction from that base. False when the owner has no base there (the hail keeps its line) or the pad is one of
    /// their own base pads (it IS their base).</summary>
    private bool TraderFarFromOwnBase(PlayerSession owner, NpcLandedTrader lt, out int distance, out string directionKey)
    {
        distance = 0;
        directionKey = string.Empty;
        string me = owner.State.PlayerId;
        if (TraderPadCentre(lt) is not { } pad || (lt.BasePad is { } cell && BasePadAt(lt.BodyId, cell)?.OwnerId == me))
        {
            return false;
        }

        int circ = BodyCircumference(lt.BodyId);
        Vector3f? home = null;
        double bestSq = double.MaxValue;
        foreach (var b in _bases)
        {
            if (b.Planet != lt.BodyId || b.OwnerId != me)
            {
                continue;
            }

            var core = new Vector3f(b.Cell.X + 0.5f, b.Cell.Y, b.Cell.Z + 0.5f);
            double d = circ > 0 ? WorldConstants.WrapDistanceSquared(core, pad, circ) : 0.0;
            if (d < bestSq)
            {
                bestSq = d;
                home = core;
            }
        }

        return home is { } from && PadFarFrom(lt.BodyId, from, pad, out distance, out directionKey);
    }

    /// <summary>The radio hail of a trader parked far from the owner's base — how far and which way — or null (the usual line).</summary>
    private string? FarTraderHail(PlayerSession owner, NpcLandedTrader lt, string bodyName)
        => TraderFarFromOwnBase(owner, lt, out int distance, out string direction)
            ? Localize(owner.Locale, "npc.call.trader_far")
                .Replace("{1}", distance.ToString(CultureInfo.InvariantCulture))
                .Replace("{2}", Localize(owner.Locale, direction))
                .Replace("{0}", bodyName)
            : null;

    /// <summary>VEGA's relay of a trader landing for an owner without a radio (#2596): "near your base" when the trader's pad is,
    /// else the line with how far and which way from the owner's base. <paramref name="args"/> are the line's display values.</summary>
    private string TraderNewsKey(PlayerSession owner, string bodyId, string bodyName, out string[] args)
    {
        if (_landedTraders.TryGetValue(bodyId, out var lt) && TraderFarFromOwnBase(owner, lt, out int distance, out string direction))
        {
            args = new[] { bodyName, distance.ToString(CultureInfo.InvariantCulture), Localize(owner.Locale, direction) };
            return "vega.news.trader_far";
        }

        args = new[] { bodyName };
        return "vega.news.trader";
    }

    // ---------------- test seams ----------------

    /// <summary>Test seam (#3175): the communal pad an arriving trader would take on the active world with these players standing
    /// there (the reach applied), or -1.</summary>
    public int TraderCommunalPadForTest(params PlayerSession[] present)
        => NearestFreeDryPad(_world.LocationId, present.ToList(), hailedOwnersFirst: false, out _, out _);

    /// <summary>Test/inspection (#3175): the base pad a body's landed (or approaching) trader holds, or null (a communal pad, none).</summary>
    public Vector3i? LandedTraderBasePadForTest(string bodyId) => _landedTraders.TryGetValue(bodyId, out var lt) ? lt.BasePad : null;
}
