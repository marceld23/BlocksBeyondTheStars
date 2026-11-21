// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The lively places, the errand rounds (docs/developer/LIVELY_PLACES.md): by day a villager without a post does not stand
/// at home — it goes round its settlement. A round is three to five errands, each to an anchor near its home
/// (<see cref="PlaceAnchorsOf"/>: the plaza, the market stall, the notice board, a field's edge, a bench, the well …), where
/// it stays half a minute or so with something to do on its nameplate and, where it fits, something in its hand; then it
/// goes home for a while and starts another round. Around noon the plaza draws more of them.
/// <list type="bullet">
/// <item><b>It makes the village talk.</b> A conversation needs two people standing within a few blocks of each other
/// (<c>CanStartChatter</c>) — at an anchor that happens by itself. Arriving next to a neighbour, both nod.</item>
/// <item><b>The weather sends them home.</b> In rain, snow or a storm nobody sets out, and whoever stands outside goes
/// home and waits it out.</item>
/// <item><b>Cheap by construction.</b> Called from the routine pass, so only for people in a player's area of interest and
/// only while a player is on foot on the world. A new errand starts only while the route queue is nearly empty
/// (<see cref="ErrandQueueSlack"/>) and while fewer than a third of the settlement is on the way; the first errand of
/// each villager is put off by a hash of its id, so a village does not set out as one when a player walks in.</item>
/// <item><b>Nothing changes hands.</b> An errand reads and writes no inventory, no crop, no stock.</item>
/// </list>
/// Everything else has the right of way: a talk with a player, a chat, a startle, a wish, a festival, a train trip and the
/// evening simply end or pause the errand (<see cref="ClearErrand"/>).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Seconds a villager stays at an anchor.</summary>
    private const double ErrandStayMin = 20.0;
    private const double ErrandStayMax = 55.0;

    /// <summary>Seconds between two errands of a round (it strolls where it stands meanwhile).</summary>
    private const double ErrandPauseMin = 3.0;
    private const double ErrandPauseMax = 12.0;

    /// <summary>Seconds at home between two rounds.</summary>
    private const double ErrandHomeMin = 35.0;
    private const double ErrandHomeMax = 90.0;

    /// <summary>Seconds within which the villagers of a place set out for the first time (each by a hash of its id).</summary>
    private const double ErrandFirstSpread = 45.0;

    /// <summary>How far from home an errand leads (the route search covers 64 blocks) and how much up or down.</summary>
    private const float ErrandReach = 48f;
    private const float ErrandRise = 6f;

    /// <summary>How far somebody strolls around an anchor while it stays there.</summary>
    private const float ErrandLeash = 1.2f;

    /// <summary>A new errand starts only while at most this many route searches wait (one runs per tick).</summary>
    private const int ErrandQueueSlack = 2;

    /// <summary>The local day fractions between which the plaza draws the village.</summary>
    private const double ErrandNoonStart = 0.42;
    private const double ErrandNoonEnd = 0.52;

    /// <summary>Seconds between two looks at the sky of somebody standing outside.</summary>
    private const double ErrandWeatherInterval = 5.0;

    /// <summary>Seconds before a villager nods to a neighbour again.</summary>
    private const double NodCooldown = 40.0;

    /// <summary>How close a neighbour must stand to be nodded to.</summary>
    private const float NodRange = 3.5f;

    /// <summary>How far an NPC's gesture is sent (as far as a line of chatter is heard).</summary>
    private const float NpcEmoteRange = 24f;

    private const string ShelteringActivityKey = "npc.activity.sheltering";
    private const string SweepingActivityKey = "npc.activity.sweeping";

    /// <summary>The broom a villager sweeps with at home between two morning rounds (a client render key, no item).</summary>
    internal const string Broom = "npc_broom";

    /// <summary>The share of morning rounds after which a villager sweeps at home.</summary>
    private const double SweepChance = 0.5;

    private readonly Random _errandRng = new(0x0E22A1D);

    /// <summary>Whether this NPC is somebody who goes on errands at all: a villager of a settlement without a post and
    /// without a story face (an authored character stays where the player expects it), not a guest, not away.</summary>
    private static bool GoesOnErrands(ServerNpc npc)
        => npc.BaseId == 0 && npc.Role == "settler" && npc.Job.Length == 0 && npc.Settlement.Length > 0 && npc.LodgeKey.Length == 0
           && npc.CharacterId.Length == 0 && npc.GuestKey.Length == 0 && !npc.VisitingAway;

    /// <summary>The errand pass of one NPC (from <see cref="TickNpcJob"/>, every tick for the people in a player's reach).</summary>
    private void TickNpcErrand(ServerNpc npc)
    {
        if (npc.Phase != NpcPhase.Day || npc.Pose != 0)
        {
            return; // the evening, the night and a festival are the routine's (it clears the errand when the phase turns)
        }

        if ((npc.TalkWith is not null && _uptime < npc.TalkUntil) || _uptime < npc.AttendUntil || _uptime < npc.StartledUntil || npc.SeatWait)
        {
            return; // chatting, greeting a passer-by, startled — the stay simply lasts that much longer
        }

        if (npc.Goal is not null)
        {
            return; // on the way
        }

        if (npc.OnErrand)
        {
            StepErrandStay(npc);
            return;
        }

        if (npc.HomeChore)
        {
            StartSweeping(npc);
            return;
        }

        if (npc.ErrandNextAt <= 0)
        {
            // The first look: every villager sets out at its own moment.
            npc.ErrandNextAt = _uptime + 2.0 + (StableUnit("errand:first", npc.Id) * ErrandFirstSpread);
            return;
        }

        if (_uptime >= npc.ErrandNextAt)
        {
            StartErrand(npc);
        }
    }

    /// <summary>Somebody on an errand who is not walking: arrives, stays, leaves when the stay is over — or when the sky
    /// turns on somebody standing in the open.</summary>
    private void StepErrandStay(ServerNpc npc)
    {
        if (npc.ErrandUntil <= 0)
        {
            ArriveAtErrand(npc);
        }
        else if (_uptime >= npc.ErrandUntil)
        {
            LeaveErrand(npc);
        }
        else if (_uptime >= npc.ErrandWeatherAt)
        {
            npc.ErrandWeatherAt = _uptime + ErrandWeatherInterval;
            if (ErrandWeatherIsBad(npc.Pos) && !RoofedAt(npc.Pos))
            {
                LeaveErrand(npc);
                ShelterAtHome(npc);
            }
        }
    }

    /// <summary>Home from a morning round: the broom, until the next round starts (not in the rain, and not with a tool
    /// already in the hand).</summary>
    private void StartSweeping(ServerNpc npc)
    {
        npc.HomeChore = false;
        if (ErrandWeatherIsBad(npc.Pos) || npc.Held.Length > 0)
        {
            return;
        }

        npc.ErrandHeld = npc.Held;
        npc.Held = Broom;
        npc.ActivityKey = SweepingActivityKey;
        BroadcastNpcs();
    }

    /// <summary>A stable number in [0, 1) for an NPC and a purpose (not a stream: the same for the same id).</summary>
    private static double StableUnit(string purpose, int id)
        => (uint)WorldGenerator.StableHash(purpose + ":" + id.ToString(System.Globalization.CultureInfo.InvariantCulture)) / ((double)uint.MaxValue + 1.0);

    /// <summary>Rain, snow or a storm at a position: nobody runs errands in it.</summary>
    private bool ErrandWeatherIsBad(Vector3f at)
        => ChatterWeatherNeed(at) is VillageChatterNeeds.WeatherWet or VillageChatterNeeds.WeatherStorm or VillageChatterNeeds.WeatherSnow;

    private Vector3f ErrandHomeOf(ServerNpc npc) => npc.HasWork ? npc.Work : npc.Rest;

    private void StartErrand(ServerNpc npc)
    {
        if (!GoesOnErrands(npc) || SettlementOfNpc(npc) is not { Ruined: false } settlement || NpcInPlayerDialog(npc))
        {
            npc.ErrandNextAt = _uptime + 30.0;
            return;
        }

        if (ErrandWeatherIsBad(npc.Pos))
        {
            ShelterAtHome(npc);
            npc.ErrandNextAt = _uptime + 15.0;
            return;
        }

        if (npc.ActivityKey == ShelteringActivityKey || npc.ActivityKey == SweepingActivityKey)
        {
            ClearErrand(npc); // the sky cleared, or the broom goes back behind the door
            npc.ActivityKey = string.Empty;
            BroadcastNpcs();
        }

        var home = ErrandHomeOf(npc);
        if (npc.ErrandRound > 0 && npc.ErrandCount >= npc.ErrandRound)
        {
            // The round is done: home for a while — in the morning every other one takes the broom there.
            npc.ErrandCount = 0;
            npc.ErrandRound = 0;
            npc.ErrandNextAt = _uptime + ErrandHomeMin + (_errandRng.NextDouble() * (ErrandHomeMax - ErrandHomeMin));
            npc.HomeChore = LocalDayFraction(npc.Pos) < ErrandNoonStart && _errandRng.NextDouble() < SweepChance;
            GoTo(npc, home, NpcArrival.None, NpcWanderLeash);
            return;
        }

        if (_worlds.Active.NpcPathQueue.Count > ErrandQueueSlack || ErrandWalkersOf(settlement) >= ErrandWalkerCap(settlement))
        {
            npc.ErrandNextAt = _uptime + 3.0 + (_errandRng.NextDouble() * 5.0); // the routes are busy — a little later
            return;
        }

        if (PickErrand(npc, settlement, home) is not { } anchor)
        {
            npc.ErrandNextAt = _uptime + 30.0; // nowhere to go from here (a lone house, a place not loaded yet)
            return;
        }

        if (npc.ErrandRound == 0)
        {
            npc.ErrandRound = 3 + _errandRng.Next(3);
            npc.ErrandCount = 0;
        }

        npc.OnErrand = true;
        npc.ErrandKind = anchor.Kind;
        npc.ErrandUntil = 0;
        SetNpcGoal(npc, anchor.Spot, NpcArrival.None, ErrandLeash);
    }

    /// <summary>How many villagers of a settlement are walking to an anchor right now.</summary>
    private int ErrandWalkersOf(SettlementInstance settlement)
    {
        int n = 0;
        foreach (var other in _npcs)
        {
            if (other.OnErrand && other.Goal is not null && string.Equals(other.Settlement, settlement.Name, StringComparison.Ordinal))
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>At most a third of a settlement is on the way at once (at least two).</summary>
    private static int ErrandWalkerCap(SettlementInstance settlement) => Math.Max(2, ResidentCap(settlement.Tier) / 3);

    /// <summary>
    /// The next anchor for a villager: one near its home that nobody stands at or walks to, never the kind it just came
    /// from, never a lantern (the night watch's); around noon the meeting place counts four times.
    /// </summary>
    private PlaceAnchor? PickErrand(ServerNpc npc, SettlementInstance settlement, Vector3f home)
    {
        var anchors = PlaceAnchorsOf(settlement);
        if (anchors.Count == 0)
        {
            return null;
        }

        double t = LocalDayFraction(npc.Pos);
        bool noon = (t >= ErrandNoonStart && t < ErrandNoonEnd) || MarketOpenAt(settlement); // a market day draws the village like noon does
        int total = 0;
        Span<int> weights = anchors.Count <= 128 ? stackalloc int[anchors.Count] : new int[anchors.Count];
        for (int i = 0; i < anchors.Count; i++)
        {
            var a = anchors[i];
            weights[i] = 0;
            if (a.Kind == PlaceAnchorKind.Lamp || (npc.ErrandCount > 0 && a.Kind == npc.ErrandKind)
                || WrapDistSq(a.Spot, home) > ErrandReach * ErrandReach || Math.Abs(a.Spot.Y - home.Y) > ErrandRise
                || WrapDistSq(a.Spot, npc.Pos) < 2.0 * 2.0 || ErrandSpotTaken(npc, a.Spot))
            {
                continue;
            }

            bool meetingPlace = a.Kind is PlaceAnchorKind.Plaza or PlaceAnchorKind.Market or PlaceAnchorKind.Stall or PlaceAnchorKind.Well
                or PlaceAnchorKind.Fire or PlaceAnchorKind.Bench;
            weights[i] = noon && meetingPlace ? 4 : 1;
            total += weights[i];
        }

        if (total == 0)
        {
            return null;
        }

        int roll = _errandRng.Next(total);
        for (int i = 0; i < anchors.Count; i++)
        {
            roll -= weights[i];
            if (roll < 0)
            {
                return anchors[i];
            }
        }

        return null;
    }

    /// <summary>Whether somebody (other than <paramref name="npc"/>) stands on an anchor's spot or is walking to it.</summary>
    private bool ErrandSpotTaken(ServerNpc? npc, Vector3f spot)
    {
        foreach (var other in _npcs)
        {
            if (ReferenceEquals(other, npc) || other.VisitingAway)
            {
                continue;
            }

            if ((other.Goal is { } goal && WrapDistSq(goal, spot) < 0.9 * 0.9) || WrapDistSq(other.Pos, spot) < 0.9 * 0.9)
            {
                return true;
            }
        }

        return false;
    }

    private void ArriveAtErrand(ServerNpc npc)
    {
        npc.ErrandUntil = _uptime + ErrandStayMin + (_errandRng.NextDouble() * (ErrandStayMax - ErrandStayMin));
        npc.ErrandWeatherAt = _uptime + ErrandWeatherInterval;
        npc.ActivityKey = ErrandActivityKey(npc.ErrandKind);
        string held = ErrandHeldFor(npc.ErrandKind);
        if (held.Length > 0 && npc.Held.Length == 0)
        {
            npc.ErrandHeld = npc.Held;
            npc.Held = held;
        }

        if (SettlementOfNpc(npc) is { } settlement && npc.ErrandKind is PlaceAnchorKind.Plaza or PlaceAnchorKind.Fire)
        {
            FaceTowards(npc, SettlementSquareCentre(settlement)); // a loose group looks at its middle
        }

        NodToANeighbour(npc);
        BroadcastNpcs();
    }

    private void LeaveErrand(ServerNpc npc)
    {
        bool changed = npc.ActivityKey.Length > 0 || npc.ErrandHeld is not null;
        ClearErrand(npc);
        npc.ErrandCount++;
        npc.ErrandNextAt = _uptime + ErrandPauseMin + (_errandRng.NextDouble() * (ErrandPauseMax - ErrandPauseMin));
        if (changed)
        {
            BroadcastNpcs();
        }
    }

    /// <summary>Ends whatever errand an NPC is on: what it held before is back in its hand, its nameplate is its own again.
    /// The routine calls this whenever the phase turns; the round and the next start are left as they are.</summary>
    private void ClearErrand(ServerNpc npc)
    {
        if (npc.ErrandHeld is { } before)
        {
            npc.Held = before;
            npc.ErrandHeld = null;
        }

        if ((npc.OnErrand && npc.ActivityKey == ErrandActivityKey(npc.ErrandKind)) || npc.ActivityKey == SweepingActivityKey
            || npc.ActivityKey == PlayingActivityKey)
        {
            npc.ActivityKey = string.Empty;
        }

        npc.OnErrand = false;
        npc.Playing = false; // a child's run ends with the day too
        npc.HomeChore = false;
        npc.ErrandUntil = 0;
    }

    /// <summary>Home, to wait out the weather.</summary>
    private void ShelterAtHome(ServerNpc npc)
    {
        if (npc.ActivityKey == SweepingActivityKey)
        {
            ClearErrand(npc); // nobody sweeps in the rain
        }

        if (npc.ActivityKey != ShelteringActivityKey)
        {
            npc.ActivityKey = ShelteringActivityKey;
            BroadcastNpcs();
        }

        var home = ErrandHomeOf(npc);
        if (WrapDistSq(npc.Pos, home) > NpcWanderLeash * NpcWanderLeash)
        {
            SetNpcGoal(npc, home, NpcArrival.None, NpcWanderLeash);
        }
    }

    /// <summary>What a villager does at an anchor, on its nameplate.</summary>
    private static string ErrandActivityKey(PlaceAnchorKind kind) => kind switch
    {
        PlaceAnchorKind.Market or PlaceAnchorKind.Stall => "npc.activity.shopping",
        PlaceAnchorKind.Board => "npc.activity.reading_board",
        PlaceAnchorKind.Workshop or PlaceAnchorKind.Greenhouse => "npc.activity.visiting",
        PlaceAnchorKind.Field => "npc.activity.checking_crops",
        PlaceAnchorKind.Pen => "npc.activity.watching_animals",
        PlaceAnchorKind.Well => "npc.activity.fetching_water",
        PlaceAnchorKind.Bench => "npc.activity.resting",
        PlaceAnchorKind.Fire => "npc.activity.warming",
        PlaceAnchorKind.Exit => "npc.activity.looking_out",
        _ => "npc.activity.strolling",
    };

    /// <summary>What a villager carries at an anchor (a client render key, "" = empty-handed).</summary>
    private static string ErrandHeldFor(PlaceAnchorKind kind)
        => kind is PlaceAnchorKind.Market or PlaceAnchorKind.Stall or PlaceAnchorKind.Field ? "npc_basket" : string.Empty;

    /// <summary>Arriving beside somebody who stands there: both nod — no line, no voice.</summary>
    private void NodToANeighbour(ServerNpc npc)
    {
        if (_uptime < npc.NodReadyAt)
        {
            return;
        }

        foreach (var other in _npcs)
        {
            if (ReferenceEquals(other, npc) || other.Pose != 0 || other.Goal is not null || other.VisitingAway || other.Role == "guardian"
                || _uptime < other.NodReadyAt || WrapDistSq(other.Pos, npc.Pos) > NodRange * NodRange || Math.Abs(other.Pos.Y - npc.Pos.Y) > 2f)
            {
                continue;
            }

            npc.NodReadyAt = other.NodReadyAt = _uptime + NodCooldown;
            BroadcastNpcEmote(npc, NpcGestures.Nod);
            BroadcastNpcEmote(other, NpcGestures.Nod);
            return;
        }
    }

    /// <summary>Sends an NPC's gesture (no line) to every player on foot within <see cref="NpcEmoteRange"/>.</summary>
    internal void BroadcastNpcEmote(ServerNpc npc, byte gesture)
    {
        if (gesture == NpcGestures.None)
        {
            return;
        }

        NpcEmote? message = null;
        foreach (var s in JoinedInActiveWorld())
        {
            if (OnFootHere(s) && WrapDistSq(s.State.Position, npc.Pos) <= NpcEmoteRange * NpcEmoteRange
                && (!npc.OtherSideOnly || s.OtherSide)) // #3905: somebody who lives over there is seen only over there
            {
                Send(s, message ??= new NpcEmote { NpcId = npc.Id, Gesture = gesture });
            }
        }
    }

    /// <summary>Test seam: an NPC's errand state.</summary>
    internal (bool OnErrand, string Kind, bool Arrived, int Count, int Round) NpcErrandForTest(int id)
    {
        foreach (var n in _npcs)
        {
            if (n.Id == id)
            {
                return (n.OnErrand, n.ErrandKind.ToString(), n.ErrandUntil > 0, n.ErrandCount, n.ErrandRound);
            }
        }

        return (false, string.Empty, false, 0, 0);
    }
}
