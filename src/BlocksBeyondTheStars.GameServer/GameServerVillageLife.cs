// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Weather;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// "The village lives" (epic #2589): villagers talk to each other (#2600) and the world remembers what players did
/// (#2599).
/// <para><b>Villager chatter.</b> Once a second per world (a scan every <see cref="ChatterScanInterval"/> s), two awake
/// people of one place — standing or sitting, not walking, not in a dialogue, not just resting from their last talk —
/// within <see cref="ChatterPairReach"/> of each other, with a player on foot within <see cref="ChatterHearRange"/> of
/// both, start a little conversation: 2–4 lines of a topic from <c>data/village_life.json</c>, alternating every
/// <see cref="ChatterLineGap"/> s, sent to every player within <see cref="ChatterRecipientRange"/> in their OWN language
/// (<see cref="BroadcastNpcLine"/>, Kind Chatter, no gesture), the two facing each other (<c>MoveNpcs</c>). Never at
/// night. Topics read the real world: the weather at the village, a landed trader, a bandit camp nearby, the deed log
/// (×5 while the hero listens — a child hears their own name), a known player standing by, a planned festival, plus
/// everyday topics. No sound jumble: one conversation per place at a time (two in a settlement of 30+ people, never in
/// earshot of each other; a station is one place), 25–45 s rest per place — 45–75 s in a crowd (more than six people
/// within 15 m of the listener) — and 90 s per person.</para>
/// <para><b>Deed log and news.</b> <see cref="RecordDeed(string, PlayerSession, string, string)"/> writes
/// <see cref="WorldMetadata.Deeds"/> (16 per world): a bandit camp cleared (the attacker; a sentry's base owner; fire →
/// the nearest player within 64 m — the villagers within 400 m who know the hero are glad, +2), a giant or the
/// Sreekmakra defeated, a first landing on an inhabited world; the festival package records "settlement_helped" and
/// "festival". The reporter's and the innkeeper's "what's in the news?" (<see cref="LatestNews"/>) merge interviews and
/// deeds, newest four first.</para>
/// <para><b>AI (hosted worlds).</b> Both run on the shared <c>/npc-lines</c> path (GameServerAiLines.cs): a conversation
/// uses an AI variant only when one is already cached for that topic, both speakers' voices and the language (the
/// first one is authored and warms the cache); a news headline is re-worded the same way around the exact facts. Names
/// and places travel as placeholder tokens, filled in on the server — the cache never holds a player name.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Seconds between two looks for new conversations in one world.</summary>
    private const double ChatterScanInterval = 3.0;

    /// <summary>A conversation starts only while a player on foot stands this close to BOTH speakers.</summary>
    private const float ChatterHearRange = 16f;

    /// <summary>Who receives a conversation line: every player on foot this close to the speaker.</summary>
    private const float ChatterRecipientRange = 24f;

    /// <summary>The farthest two people may stand apart to talk (horizontally).</summary>
    private const float ChatterPairReach = 3.5f;

    /// <summary>Seconds between two lines of a conversation.</summary>
    private const double ChatterLineGap = 3.6;

    /// <summary>A place's rest after a conversation (seconds, rolled between min and max).</summary>
    private const double ChatterPlaceRestMin = 25.0, ChatterPlaceRestMax = 45.0;

    /// <summary>A place's rest after a conversation in a crowd (NO SOUND JUMBLE).</summary>
    private const double ChatterCrowdRestMin = 45.0, ChatterCrowdRestMax = 75.0;

    /// <summary>More than this many people within <see cref="ChatterCrowdRange"/> of the listener is a crowd.</summary>
    private const int ChatterCrowdPeople = 6;

    /// <summary>The radius around the listener a crowd is counted in.</summary>
    private const float ChatterCrowdRange = 15f;

    /// <summary>Seconds a person rests from talking after a conversation.</summary>
    private const double ChatterPersonRest = 90.0;

    /// <summary>The last topics of a place that are not picked again.</summary>
    private const int ChatterRecentTopics = 4;

    /// <summary>A settlement with at least this many people may hold two conversations at once (out of earshot).</summary>
    private const int ChatterBigPlacePeople = 30;

    /// <summary>A deed is chatter material for this long (seconds of world playtime).</summary>
    private const long ChatterDeedWindowSeconds = 3 * 3600;

    /// <summary>A deed topic counts this many times while its hero listens.</summary>
    private const int ChatterHeroWeight = 5;

    /// <summary>A camp, a giant, a trader within this range belongs to a settlement (the radio's "near a settlement").</summary>
    private const float DeedSettlementRange = 400f;

    /// <summary>Nobody fired (fire burned the last bandit): the nearest player within this range is the hero.</summary>
    private const float DeedWitnessRange = 64f;

    /// <summary>The most first-landing deeds one world's log keeps (the oldest landing makes room for a new one).</summary>
    private const int MaxLandingDeeds = 4;

    /// <summary>How many stories "what's in the news?" tells.</summary>
    private const int NewsDigestCount = 4;

    /// <summary>The longest AI news headline (characters, tokens as written).</summary>
    private const int NewsDeedMaxChars = 140;

    private readonly Dictionary<string, VillageLifeState> _villageLife = new(System.StringComparer.Ordinal);
    private readonly List<PlayerSession> _chatterListeners = new();
    private readonly System.Random _villageRng = new(0x51A77E);
    private string? _chatterTopicForTest;
    private bool _chatterFixedOrderForTest;

    /// <summary>One world's village life (runtime only — a restart starts quiet).</summary>
    private sealed class VillageLifeState
    {
        public double NextPassAt;
        public double NextScanAt;
        public readonly Dictionary<string, VillagePlaceState> Places = new(System.StringComparer.Ordinal);
        public readonly List<VillageChat> Chats = new();
    }

    /// <summary>One place (settlement, base, station — <see cref="NewsPlaceKey"/>) of a world.</summary>
    private sealed class VillagePlaceState
    {
        public double NextChatAt;
        public readonly List<string> RecentTopics = new();
        public readonly HashSet<long> DeedsTalkedAbout = new();
    }

    /// <summary>A running conversation.</summary>
    private sealed class VillageChat
    {
        public VillageChat(ServerNpc a, ServerNpc b, string place, VillageChatterTopic topic, Dictionary<string, string> values,
            string keyA, string keyB, string situation, string fact, double rest)
        {
            A = a;
            B = b;
            Place = place;
            Topic = topic;
            Values = values;
            KeyA = keyA;
            KeyB = keyB;
            Situation = situation;
            Fact = fact;
            Rest = rest;
        }

        public ServerNpc A { get; }
        public ServerNpc B { get; }
        public string Place { get; }
        public VillageChatterTopic Topic { get; }

        /// <summary>The placeholder values of this conversation ({a}, {b}, {place}, {player}, {trader}).</summary>
        public Dictionary<string, string> Values { get; }

        /// <summary>The speakers' memory keys (their AI voices).</summary>
        public string KeyA { get; }
        public string KeyB { get; }
        public string Situation { get; }
        public string Fact { get; }

        /// <summary>The place's rest once this conversation is over.</summary>
        public double Rest { get; }

        /// <summary>The lines per language, resolved on first use (authored, or a cached AI variant).</summary>
        public Dictionary<string, string[]> LinesByLocale { get; } = new(System.StringComparer.Ordinal);
        public int Next;
        public double NextLineAt;
    }

    /// <summary>A topic that fits right now, with its weight and placeholder values.</summary>
    private readonly struct ChatterCandidate
    {
        public ChatterCandidate(VillageChatterTopic topic, int weight, Dictionary<string, string> values, DeedRecord? deed, string fact)
        {
            Topic = topic;
            Weight = weight;
            Values = values;
            Deed = deed;
            Fact = fact;
        }

        public VillageChatterTopic Topic { get; }
        public int Weight { get; }
        public Dictionary<string, string> Values { get; }
        public DeedRecord? Deed { get; }
        public string Fact { get; }
    }

    private VillageLifeState VillageLifeOf(string locationId)
    {
        if (!_villageLife.TryGetValue(locationId, out var state))
        {
            _villageLife[locationId] = state = new VillageLifeState();
        }

        return state;
    }

    private static VillagePlaceState PlaceStateOf(VillageLifeState state, string placeKey)
    {
        if (!state.Places.TryGetValue(placeKey, out var place))
        {
            state.Places[placeKey] = place = new VillagePlaceState();
        }

        return place;
    }

    // =====================================================================================================
    // The tick (#2600)
    // =====================================================================================================

    /// <summary>Per world (Guard-registered after the professions): running conversations say their next line when it is
    /// due (and are checked once a second); a scan for new ones every <see cref="ChatterScanInterval"/> s.</summary>
    private void TickVillageLife(double dt)
    {
        if (_world is null)
        {
            return;
        }

        var state = VillageLifeOf(_world.LocationId);
        if (_npcs.Count == 0 && state.Chats.Count == 0)
        {
            return;
        }

        bool pass = _uptime >= state.NextPassAt;
        if (pass)
        {
            state.NextPassAt = _uptime + 1.0;
        }

        for (int i = state.Chats.Count - 1; i >= 0; i--)
        {
            var chat = state.Chats[i];
            if (pass || _uptime >= chat.NextLineAt)
            {
                StepVillageChat(state, chat);
            }
        }

        if (pass && _uptime >= state.NextScanAt)
        {
            state.NextScanAt = _uptime + ChatterScanInterval;
            ScanForChatter(state);
        }
    }

    /// <summary>Players who can start a conversation by standing near: joined on this world, on foot (not aboard the
    /// parked ship, not in space) and not an invisible observer.</summary>
    private void CollectChatterListeners(List<PlayerSession> into)
    {
        into.Clear();
        foreach (var s in JoinedInActiveWorld())
        {
            if (!s.Spectating && OnFootHere(s))
            {
                into.Add(s);
            }
        }
    }

    /// <summary>On the surface of the active world: not aboard the parked ship, not in space.</summary>
    private bool OnFootHere(PlayerSession s) => (!_shipPlaced || !s.State.AboardShip) && !InSpace(s.State.PlayerId);

    /// <summary>Whether a player is talking to this NPC (an open dialogue panel) — #2616: that very NPC object, in a
    /// conversation the one validity rule still accepts (a stale talk from another world or a reused id never counts).</summary>
    private bool NpcInPlayerDialog(ServerNpc npc)
    {
        foreach (var s in JoinedInActiveWorld())
        {
            if (s.Conversation is { } c && ReferenceEquals(c.Npc, npc) && NpcConversationValid(s, c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Free to start a conversation: a routine person, awake, not walking, not waiting for a seat, not posing,
    /// not greeting a passer-by, not already talking and rested from the last talk — and not at the festival (#2604: the
    /// ring cheers, one voice at a time).</summary>
    private bool CanStartChatter(ServerNpc npc)
        => npc.RoutineEnabled && npc.Pose != 2 && npc.Goal is null && !npc.SeatWait && npc.TalkWith is null
           && _uptime >= npc.ChatRestUntil && _uptime >= npc.AttendUntil
           && npc.ActivityKey != "npc.activity.posing" && npc.Role != "guardian" && npc.Phase != NpcPhase.Festival;

    /// <summary>The conversation partner an NPC faces right now, or null.</summary>
    private ServerNpc? TalkPartner(ServerNpc npc)
        => npc.TalkWith is { } partner && _uptime < npc.TalkUntil ? partner : null;

    /// <summary>Turns an NPC toward a position (the short way round the east–west seam).</summary>
    private void FaceTowards(ServerNpc npc, Vector3f at) // #2666: sets where the NPC turns to; MoveNpcs turns it there
        => npc.FaceGoal = (float)System.Math.Atan2(WorldConstants.WrapDeltaX((double)at.X - npc.Pos.X, _world.Circumference), at.Z - npc.Pos.Z);

    private bool AnyListenerWithin(List<PlayerSession> listeners, Vector3f at, float range)
    {
        double rangeSq = (double)range * range;
        foreach (var s in listeners)
        {
            if (WrapDistSq(s.State.Position, at) <= rangeSq)
            {
                return true;
            }
        }

        return false;
    }

    private Vector3f Midpoint(Vector3f a, Vector3f b)
    {
        var bu = Unwrapped(a, b);
        return new Vector3f((a.X + bu.X) * 0.5f, (a.Y + bu.Y) * 0.5f, (a.Z + bu.Z) * 0.5f);
    }

    /// <summary>Looks for pairs to talk, one place at a time.</summary>
    private void ScanForChatter(VillageLifeState state)
    {
        if (_content.VillageLife.Chatter.Count == 0 || _npcs.Count < 2)
        {
            return;
        }

        CollectChatterListeners(_chatterListeners);
        if (_chatterListeners.Count == 0)
        {
            return;
        }

        var byPlace = new Dictionary<string, List<ServerNpc>>(System.StringComparer.Ordinal);
        foreach (var npc in _npcs)
        {
            if (!CanStartChatter(npc) || !AnyListenerWithin(_chatterListeners, npc.Pos, ChatterHearRange) || NpcInPlayerDialog(npc))
            {
                continue;
            }

            string placeKey = NewsPlaceKey(npc);
            if (!byPlace.TryGetValue(placeKey, out var list))
            {
                byPlace[placeKey] = list = new List<ServerNpc>();
            }

            list.Add(npc);
        }

        foreach (var entry in byPlace)
        {
            if (entry.Value.Count < 2)
            {
                continue;
            }

            var place = PlaceStateOf(state, entry.Key);
            if (_uptime < place.NextChatAt)
            {
                continue;
            }

            int running = state.Chats.Count(c => c.Place == entry.Key);
            if (running > 0 && running >= MaxChatsAt(entry.Key))
            {
                continue;
            }

            TryStartChatter(state, place, entry.Key, entry.Value);
        }
    }

    /// <summary>One conversation at a time per place — two in a settlement of <see cref="ChatterBigPlacePeople"/>+ people
    /// (a station or a base is always one place with one conversation).</summary>
    private int MaxChatsAt(string placeKey)
    {
        if (!placeKey.StartsWith("s:", System.StringComparison.Ordinal))
        {
            return 1;
        }

        int people = 0;
        foreach (var npc in _npcs)
        {
            if (npc.BaseId == 0 && npc.Settlement.Length > 0 && NewsPlaceKey(npc) == placeKey)
            {
                people++;
            }
        }

        return people >= ChatterBigPlacePeople ? 2 : 1;
    }

    /// <summary>Picks a pair at the place (at random among those that fit), a topic from the real world, and starts.</summary>
    private void TryStartChatter(VillageLifeState state, VillagePlaceState place, string placeKey, List<ServerNpc> people)
    {
        var pairs = new List<(ServerNpc A, ServerNpc B, Vector3f Mid)>();
        for (int i = 0; i < people.Count; i++)
        {
            for (int j = i + 1; j < people.Count; j++)
            {
                var a = people[i];
                var b = people[j];
                var bu = Unwrapped(a.Pos, b.Pos);
                float dx = bu.X - a.Pos.X, dz = bu.Z - a.Pos.Z;
                if (dx * dx + dz * dz > ChatterPairReach * ChatterPairReach || System.Math.Abs(bu.Y - a.Pos.Y) > 2f)
                {
                    continue;
                }

                var mid = Midpoint(a.Pos, b.Pos);
                if (HearingListeners(a, b).Count == 0 || InEarshotOfAChat(state, mid))
                {
                    continue;
                }

                pairs.Add((a, b, mid));
            }
        }

        if (pairs.Count == 0)
        {
            return;
        }

        var pick = pairs[_villageRng.Next(pairs.Count)];
        bool swap = _chatterFixedOrderForTest ? pick.A.Id > pick.B.Id : _villageRng.Next(2) != 0;
        var (first, second) = swap ? (pick.B, pick.A) : (pick.A, pick.B);
        StartChatter(state, place, placeKey, first, second, pick.Mid);
    }

    /// <summary>Listeners on foot within <see cref="ChatterHearRange"/> of both people.</summary>
    private List<PlayerSession> HearingListeners(ServerNpc a, ServerNpc b)
    {
        var hearing = new List<PlayerSession>();
        double rangeSq = (double)ChatterHearRange * ChatterHearRange;
        foreach (var s in _chatterListeners)
        {
            if (WrapDistSq(s.State.Position, a.Pos) <= rangeSq && WrapDistSq(s.State.Position, b.Pos) <= rangeSq)
            {
                hearing.Add(s);
            }
        }

        return hearing;
    }

    /// <summary>NO SOUND JUMBLE: a new conversation never starts where someone could hear a running one too.</summary>
    private bool InEarshotOfAChat(VillageLifeState state, Vector3f mid)
    {
        double reach = 2.0 * ChatterRecipientRange;
        foreach (var chat in state.Chats)
        {
            if (WrapDistSq(chat.A.Pos, mid) <= reach * reach || WrapDistSq(chat.B.Pos, mid) <= reach * reach)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Night by the local sun at a position (the routine's hours: people are in bed).</summary>
    private bool ChatterNightAt(Vector3f at)
    {
        double t = LocalDayFraction(at);
        return t >= RoutineNightStart || t < RoutineNightEnd;
    }

    private void StartChatter(VillageLifeState state, VillagePlaceState place, string placeKey, ServerNpc a, ServerNpc b, Vector3f mid)
    {
        if (ChatterNightAt(mid))
        {
            return; // never at night — the village sleeps
        }

        var hearing = HearingListeners(a, b);
        if (hearing.Count == 0)
        {
            return;
        }

        bool evening = LocalDayFraction(mid) >= RoutineEveningStart;
        string weather = ChatterWeatherNeed(mid);
        var candidates = ChatterCandidates(place, a, b, mid, hearing, evening, weather);
        if (candidates.Count == 0)
        {
            return;
        }

        int total = 0;
        foreach (var c in candidates)
        {
            total += c.Weight;
        }

        int roll = _villageRng.Next(total);
        var chosen = candidates[candidates.Count - 1];
        foreach (var c in candidates)
        {
            roll -= c.Weight;
            if (roll < 0)
            {
                chosen = c;
                break;
            }
        }

        place.RecentTopics.Add(chosen.Topic.Key);
        while (place.RecentTopics.Count > ChatterRecentTopics)
        {
            place.RecentTopics.RemoveAt(0);
        }

        if (chosen.Deed is { } deed)
        {
            place.DeedsTalkedAbout.Add(deed.Id);
        }

        // The crowd decides the place's rest (NO SOUND JUMBLE): a busy square or a station deck stays calmer.
        var listener = hearing.OrderBy(s => WrapDistSq(s.State.Position, mid)).First();
        int crowd = 0;
        foreach (var npc in _npcs)
        {
            if (WrapDistSq(npc.Pos, listener.State.Position) <= (double)ChatterCrowdRange * ChatterCrowdRange)
            {
                crowd++;
            }
        }

        double rest = crowd > ChatterCrowdPeople
            ? ChatterCrowdRestMin + _villageRng.NextDouble() * (ChatterCrowdRestMax - ChatterCrowdRestMin)
            : ChatterPlaceRestMin + _villageRng.NextDouble() * (ChatterPlaceRestMax - ChatterPlaceRestMin);

        string situation = (evening ? "evening" : "daytime") + WeatherWords(weather);
        var chat = new VillageChat(a, b, placeKey, chosen.Topic, chosen.Values, NpcKeyForNpc(listener, a), NpcKeyForNpc(listener, b),
            situation, chosen.Fact, rest);

        int lines = chosen.Topic.Lines.Count;
        a.TalkWith = b;
        b.TalkWith = a;
        a.TalkUntil = b.TalkUntil = _uptime + lines * ChatterLineGap + 2.0;
        FaceTowards(a, b.Pos);
        FaceTowards(b, a.Pos);

        // The AI variant per language of everyone in earshot: a cached one is used now, a miss asks for one in the
        // background (the next conversation about this topic in these voices may use it).
        foreach (var s in JoinedInActiveWorld())
        {
            if (OnFootHere(s) && WrapDistSq(s.State.Position, mid) <= (double)ChatterRecipientRange * ChatterRecipientRange
                && !chat.LinesByLocale.ContainsKey(s.Locale))
            {
                chat.LinesByLocale[s.Locale] = ChatterLinesFor(chat, s.Locale, requestOnMiss: true);
            }
        }

        state.Chats.Add(chat);
        SayNextChatterLine(chat);
    }

    /// <summary>Runs one conversation: ends it when someone fell asleep, walked off, was spoken to or is gone (or night
    /// fell), else says the next line when it is due, and closes it after the last.</summary>
    private void StepVillageChat(VillageLifeState state, VillageChat chat)
    {
        bool broken = ChatterBroken(chat.A, chat.B) || ChatterBroken(chat.B, chat.A) || ChatterNightAt(chat.A.Pos);
        if (broken || (_uptime >= chat.NextLineAt && chat.Next >= chat.Topic.Lines.Count))
        {
            EndVillageChat(state, chat);
            return;
        }

        if (_uptime >= chat.NextLineAt)
        {
            SayNextChatterLine(chat);
        }
    }

    private bool ChatterBroken(ServerNpc npc, ServerNpc partner)
        => npc.TalkWith != partner || npc.Pose == 2 || npc.Goal is not null || !_npcs.Contains(npc)
           || npc.ActivityKey == "npc.activity.posing" || NpcInPlayerDialog(npc);

    private void SayNextChatterLine(VillageChat chat)
    {
        int index = chat.Next++;
        chat.NextLineAt = _uptime + ChatterLineGap;
        var speaker = index % 2 == 0 ? chat.A : chat.B;
        BroadcastNpcLine(speaker, ChatterRecipientRange, s => ChatterLineFor(chat, s.Locale, index),
            NpcGreetingKinds.Chatter, NpcGestures.None);
    }

    private string ChatterLineFor(VillageChat chat, string locale, int index)
    {
        if (!chat.LinesByLocale.TryGetValue(locale, out var lines))
        {
            chat.LinesByLocale[locale] = lines = ChatterLinesFor(chat, locale, requestOnMiss: false);
        }

        return index >= 0 && index < lines.Length ? lines[index] : string.Empty;
    }

    /// <summary>Ends a conversation: both people stop facing each other and rest; the place rests.</summary>
    private void EndVillageChat(VillageLifeState state, VillageChat chat)
    {
        foreach (var (npc, partner) in new[] { (chat.A, chat.B), (chat.B, chat.A) })
        {
            if (npc.TalkWith == partner)
            {
                npc.TalkWith = null;
                npc.TalkUntil = 0;
            }

            npc.ChatRestUntil = _uptime + ChatterPersonRest;
        }

        var place = PlaceStateOf(state, chat.Place);
        place.NextChatAt = System.Math.Max(place.NextChatAt, _uptime + chat.Rest);
        state.Chats.Remove(chat);
    }

    /// <summary>A player started talking to <paramref name="npc"/> (E): a conversation it is in ends at once, so the
    /// villager turns to the player.</summary>
    private void StopVillageChat(ServerNpc npc)
    {
        if (npc.TalkWith is null || _world is null || !_villageLife.TryGetValue(_world.LocationId, out var state))
        {
            return;
        }

        foreach (var chat in state.Chats)
        {
            if (chat.A == npc || chat.B == npc)
            {
                EndVillageChat(state, chat);
                return;
            }
        }
    }

    // =====================================================================================================
    // Topics from the real world (#2600)
    // =====================================================================================================

    /// <summary>The weather need the sky at a position satisfies ("weather_wet", "weather_storm", "weather_snow",
    /// "weather_clear", "weather_fog"), or "" (clouds, wind, exotic weather — and a station, which has no sky).</summary>
    private string ChatterWeatherNeed(Vector3f at)
    {
        if (_world.Planet is null || _world.Planet.Void)
        {
            return string.Empty;
        }

        var (state, _) = BiomeWeatherAt(at);
        if (WeatherCatalog.Find(state) is not { } def)
        {
            return string.Empty;
        }

        if (def.Family == WeatherFamily.Obscuring)
        {
            return VillageChatterNeeds.WeatherFog;
        }

        if (def.Key == "clear")
        {
            return VillageChatterNeeds.WeatherClear;
        }

        if (def.Family is not (WeatherFamily.Wet or WeatherFamily.Violent))
        {
            return string.Empty;
        }

        string precip = PrecipitationFor(state, CurrentTemperature(state, _dayFraction, at));
        if (def.Key == "blizzard" || precip is "snow" or "hail")
        {
            return VillageChatterNeeds.WeatherSnow;
        }

        if (def.Key == "storm")
        {
            return VillageChatterNeeds.WeatherStorm;
        }

        return def.Family == WeatherFamily.Wet && precip is "rain" or "drizzle" or "sleet" ? VillageChatterNeeds.WeatherWet : string.Empty;
    }

    /// <summary>The name of a trader parked on this world within <see cref="DeedSettlementRange"/> (any parked trader while
    /// its pilot is not on the ground yet), or "". A trader still on its approach (#2603) has not landed — it is no talk yet.</summary>
    private string LandedTraderNear(Vector3f at)
        => TouchedDownTraderHere() is { } t
           && (t.PilotNpcId == 0 || WrapDistSq(t.PilotPos, at) <= (double)DeedSettlementRange * DeedSettlementRange)
            ? t.Name
            : string.Empty;

    /// <summary>An uncleared bandit camp within <see cref="DeedSettlementRange"/> on a world with bandits.</summary>
    private bool BanditCampNear(Vector3f at)
        => BanditsActive && _banditCamps.Any(c => !c.Cleared && WrapDistSq(c.Center, at) <= (double)DeedSettlementRange * DeedSettlementRange);

    /// <summary>The display name of a place for <c>{place}</c>: the NPC's settlement (or base), else this world's name.</summary>
    private string ChatterPlaceName(ServerNpc npc)
        => npc.Settlement.Length > 0 ? npc.Settlement : WorldDisplayName();

    /// <summary>This world's display name (a planet, a moon, a station), "" when unknown.</summary>
    private string WorldDisplayName() => _galaxy?.FindBody(_world.LocationId)?.Name ?? string.Empty;

    /// <summary>Whether a deed concerns the people of a settlement: their own settlement's, or the whole world's. People
    /// without a settlement (a station crew) hear every deed of their world.</summary>
    private static bool DeedConcerns(DeedRecord deed, string settlementName)
        => string.IsNullOrEmpty(settlementName) || deed.Settlement.Length == 0
           || string.Equals(deed.Settlement, settlementName, System.StringComparison.Ordinal);

    /// <summary>
    /// Every topic that fits this pair right now, with its weight and placeholder values: the time of day, the place's
    /// last <see cref="ChatterRecentTopics"/> topics skipped, and the need met by the real world.
    /// </summary>
    private List<ChatterCandidate> ChatterCandidates(VillagePlaceState place, ServerNpc a, ServerNpc b,
        Vector3f mid, List<PlayerSession> hearing, bool evening, string weather)
    {
        var result = new List<ChatterCandidate>();
        string trader = LandedTraderNear(mid);
        bool campNear = BanditCampNear(mid);
        string festivalHero = FestivalPendingHero(a.Settlement);
        string lodgeResident = a.LodgeKey.Length == 0 ? LodgeResidentNear(mid) : string.Empty; // #2641: never about themselves

        // Someone the first speaker knows stands by — a friend of the village.
        string knownPlayer = string.Empty;
        foreach (var s in hearing)
        {
            if (RelationshipTier(RelationshipValue(s.State, NpcKeyForNpc(s, a))) is "known" or "trusted")
            {
                knownPlayer = s.State.Name ?? string.Empty;
                break;
            }
        }

        // The world's recent deeds this place has not talked about yet, newest first.
        var deeds = new List<DeedRecord>();
        long now = _meta.CumulativePlaytimeSeconds;
        if (_meta.Deeds.TryGetValue(_world.LocationId, out var log))
        {
            for (int i = log.Count - 1; i >= 0; i--)
            {
                var d = log[i];
                if (d.At >= now - ChatterDeedWindowSeconds && !place.DeedsTalkedAbout.Contains(d.Id) && DeedConcerns(d, a.Settlement))
                {
                    deeds.Add(d);
                }
            }
        }

        foreach (var topic in _content.VillageLife.Chatter)
        {
            if ((_chatterTopicForTest is not null && topic.Key != _chatterTopicForTest)
                || topic.Lines.Count < VillageChatterTopic.MinLines
                || (topic.When == "day" && evening) || (topic.When == "evening" && !evening)
                || place.RecentTopics.Contains(topic.Key))
            {
                continue;
            }

            var values = new Dictionary<string, string>(System.StringComparer.Ordinal)
            {
                [AiLineTokens.A] = a.Name,
                [AiLineTokens.B] = b.Name,
                [AiLineTokens.Place] = ChatterPlaceName(a),
            };
            int weight = System.Math.Max(1, topic.Weight);
            DeedRecord? deed = null;
            string fact;
            string needs = topic.Needs;
            switch (needs)
            {
                case "":
                    fact = string.Empty;
                    break;
                case VillageChatterNeeds.WeatherWet:
                case VillageChatterNeeds.WeatherStorm:
                case VillageChatterNeeds.WeatherSnow:
                case VillageChatterNeeds.WeatherClear:
                case VillageChatterNeeds.WeatherFog:
                    if (weather != needs)
                    {
                        continue;
                    }

                    fact = WeatherFact(needs, evening);
                    break;
                case VillageChatterNeeds.Trader:
                    if (trader.Length == 0)
                    {
                        continue;
                    }

                    values[AiLineTokens.Trader] = trader;
                    fact = "{trader} landed a trader ship at the landing pad";
                    break;
                case VillageChatterNeeds.CampNear:
                    if (!campNear)
                    {
                        continue;
                    }

                    fact = "bandits have a camp in the hills near {place}";
                    break;
                case VillageChatterNeeds.LodgeNear:
                    if (lodgeResident.Length == 0)
                    {
                        continue;
                    }

                    values[AiLineTokens.Npc] = lodgeResident;
                    fact = "{npc} lives in a little hut out in the wild near {place} and trades with the village";
                    break;
                case VillageChatterNeeds.PlayerKnown:
                    if (knownPlayer.Length == 0)
                    {
                        continue;
                    }

                    values[AiLineTokens.Player] = knownPlayer;
                    fact = "{player}, someone the village knows, is standing nearby";
                    break;
                case VillageChatterNeeds.FestivalPending:
                    if (festivalHero.Length == 0)
                    {
                        continue;
                    }

                    values[AiLineTokens.Player] = festivalHero;
                    fact = "the village will soon celebrate {player} with a festival";
                    break;
                default:
                    if (!needs.StartsWith(VillageChatterNeeds.DeedPrefix, System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string kind = needs.Substring(VillageChatterNeeds.DeedPrefix.Length);
                    deed = deeds.FirstOrDefault(d => d.Kind == kind);
                    if (deed is null)
                    {
                        continue;
                    }

                    values[AiLineTokens.Player] = deed.PlayerName;
                    if (hearing.Any(s => s.State.PlayerId == deed.PlayerId))
                    {
                        weight *= ChatterHeroWeight; // the hero hears the village talk about them
                    }

                    fact = ChatterDeedFact(kind);
                    break;
            }

            result.Add(new ChatterCandidate(topic, weight, values, deed, fact));
        }

        return result;
    }

    private static string WeatherFact(string need, bool evening) => need switch
    {
        VillageChatterNeeds.WeatherWet => "it is raining in {place}",
        VillageChatterNeeds.WeatherStorm => "a thunderstorm rolls over {place}",
        VillageChatterNeeds.WeatherSnow => "it is snowing in {place}",
        VillageChatterNeeds.WeatherFog => "thick fog lies over {place}",
        _ => evening ? "the evening sky over {place} is clear and full of stars" : "the sun shines over {place}",
    };

    /// <summary>Plain words for the weather in the AI's situation line ("" for none).</summary>
    private static string WeatherWords(string need) => need switch
    {
        VillageChatterNeeds.WeatherWet => ", rain",
        VillageChatterNeeds.WeatherStorm => ", a thunderstorm",
        VillageChatterNeeds.WeatherSnow => ", snow",
        VillageChatterNeeds.WeatherClear => ", clear sky",
        VillageChatterNeeds.WeatherFog => ", fog",
        _ => string.Empty,
    };

    /// <summary>A deed as one plain English fact for villagers talking about it (<c>{place}</c> is THEIR village there,
    /// so a deed that may have happened elsewhere on the world does not name it).</summary>
    private static string ChatterDeedFact(string kind) => kind switch
    {
        DeedKinds.CampCleared => "{player} chased the bandits out of their camp near the village",
        DeedKinds.SettlementHelped => "{player} helped {place} with everything the village needed",
        DeedKinds.Festival => "{place} held a big festival for {player}",
        DeedKinds.Giant => "{player} defeated a giant",
        DeedKinds.Sreekmakra => "{player} unmasked the Sreekmakra, a shapeshifting creature",
        DeedKinds.Landing => "{player} landed on this world for the first time",
        _ => "{player} did something brave",
    };

    /// <summary>A deed as one plain English fact for its news headline (<c>{place}</c> = where it happened,
    /// <c>{thing}</c> = the giant).</summary>
    private static string NewsDeedFact(string kind) => kind switch
    {
        DeedKinds.CampCleared => "{player} chased the bandits out of their camp near {place}",
        DeedKinds.SettlementHelped => "{player} helped {place} with everything the village needed",
        DeedKinds.Festival => "{place} held a big festival for {player}",
        DeedKinds.Giant => "{player} defeated {thing} near {place}",
        DeedKinds.Sreekmakra => "{player} unmasked the Sreekmakra, a shapeshifting creature, near {place}",
        DeedKinds.Landing => "{player} landed on {place} for the first time",
        _ => "{player} did something brave near {place}",
    };

    // =====================================================================================================
    // Lines per language — authored, or a cached AI variant (#2600)
    // =====================================================================================================

    /// <summary>The conversation's lines in one language: a cached AI variant for this topic, both voices and the
    /// language when one exists (filled with this conversation's names), else the authored lines. With
    /// <paramref name="requestOnMiss"/> a miss asks the backend for a variant in the background (budget permitting).</summary>
    private string[] ChatterLinesFor(VillageChat chat, string locale, bool requestOnMiss)
    {
        var raw = new string[chat.Topic.Lines.Count];
        for (int i = 0; i < raw.Length; i++)
        {
            raw[i] = Localize(locale, chat.Topic.Lines[i]);
        }

        if (AiLinesEnabled)
        {
            string key = ChatterCacheKey(chat, locale);
            var allowed = VillageChatterNeeds.TokensFor(chat.Topic.Needs);
            string[]? ai = requestOnMiss
                ? AiLinesFor(key, AiLinePriority.Chatter, () => ChatterRequest(chat, locale, raw, allowed),
                    requiredTokens: AiLineText.TokensIn(raw), allowedTokens: allowed)
                : CachedAiLines(key);
            if (ai is { } lines && lines.Length == raw.Length && AiLineText.FillTokens(lines, chat.Values) is { } filled)
            {
                return filled;
            }
        }

        if (AiLineText.FillTokens(raw, chat.Values) is { } authored)
        {
            return authored;
        }

        // A placeholder without a value (a data slip the content tests should have caught): say nothing rather than braces.
        return Enumerable.Repeat(string.Empty, raw.Length).ToArray();
    }

    /// <summary><c>chatter|&lt;topic&gt;|p&lt;voice A&gt;|&lt;locale&gt;|p&lt;voice B&gt;</c> — the topic and both voices,
    /// never a name.</summary>
    private static string ChatterCacheKey(VillageChat chat, string locale)
        => AiLineCacheKey(NpcLinePurposes.Chatter, chat.Topic.Key, PersonaIndexFor(chat.KeyA, chat.A.IsRobot, chat.A.Role), locale,
            "p" + PersonaIndexFor(chat.KeyB, chat.B.IsRobot, chat.B.Role).ToString(CultureInfo.InvariantCulture));

    private NpcLinesRequest ChatterRequest(VillageChat chat, string locale, string[] authored, IReadOnlyList<string> tokens)
    {
        var request = new NpcLinesRequest
        {
            Purpose = NpcLinePurposes.Chatter,
            Language = locale,
            Place = ChatterPlaceWords(chat.Place),
            Theme = chat.A.Theme,
            Situation = chat.Situation,
            TopicHint = chat.Topic.Hint.Length > 0 ? chat.Topic.Hint : chat.Topic.Key.Replace('_', ' '),
            LineCount = authored.Length,
            MaxChars = VillageChatterTopic.MaxLineChars,
            Speakers =
            {
                ChatterSpeaker(chat.A, chat.KeyA, AiLineTokens.A),
                ChatterSpeaker(chat.B, chat.KeyB, AiLineTokens.B),
            },
        };
        if (chat.Fact.Length > 0)
        {
            request.Facts.Add(chat.Fact);
        }

        request.Tokens.AddRange(tokens);
        request.Authored.AddRange(authored);
        return request;
    }

    private static NpcLinesSpeaker ChatterSpeaker(ServerNpc npc, string key, string token) => new()
    {
        Token = token,
        Role = npc.Role,
        Job = npc.Job,
        IsRobot = npc.IsRobot,
        Persona = PersonaFor(key, npc.Theme, npc.IsRobot, npc.Role),
    };

    /// <summary>Plain words for where it happens (tone only — the name travels as <c>{place}</c>).</summary>
    private string ChatterPlaceWords(string placeKey)
        => placeKey.StartsWith("b:", System.StringComparison.Ordinal) ? "the player's home base"
            : _world.Planet?.Void == true ? "a space station"
            : placeKey.StartsWith("s:", System.StringComparison.Ordinal) ? "a small village on a frontier planet"
            : "a settlement";

    // =====================================================================================================
    // Speech bubbles for everyone near (#2600, shared with the festival)
    // =====================================================================================================

    /// <summary>
    /// #2600: one speech bubble over <paramref name="npc"/> for EVERY player who can see it: joined on the active world, on
    /// foot (not aboard the parked ship, not in space) and within <paramref name="range"/> blocks. Each receiver gets the
    /// text <paramref name="textFor"/> returns for THEM — it runs once per receiver, so localize there (and memoize per
    /// locale when the text does not depend on the receiver); an empty text skips that receiver. All receivers share one
    /// fresh <see cref="NpcGreeting.GreetingId"/> (<see cref="NextGreetingId"/>), so a later AI upgrade of the same
    /// bubble could reuse it. <paramref name="kind"/> is one of <see cref="NpcGreetingKinds"/> — never
    /// <see cref="NpcGreetingKinds.Addressed"/> for a background line: the client's voice budget ranks by kind;
    /// <paramref name="gesture"/> one of <see cref="NpcGestures"/>. Returns the bubble's id, or 0 when nobody got it.
    /// <para>Users: the villager chatter (Kind Chatter, no gesture); the festival cheers (Kind Cheer, Gesture Cheer).</para>
    /// </summary>
    internal int BroadcastNpcLine(ServerNpc npc, float range, System.Func<PlayerSession, string> textFor, byte kind, byte gesture)
    {
        int greetingId = 0;
        double rangeSq = (double)range * range;
        foreach (var s in JoinedInActiveWorld())
        {
            if (!OnFootHere(s) || WrapDistSq(s.State.Position, npc.Pos) > rangeSq)
            {
                continue;
            }

            string text = textFor(s);
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            if (greetingId == 0)
            {
                greetingId = NextGreetingId();
            }

            Send(s, new NpcGreeting
            {
                NpcId = npc.Id,
                Name = npc.Name,
                Role = npc.Role,
                Text = text,
                GreetingId = greetingId,
                Kind = kind,
                Gesture = gesture,
            });
        }

        return greetingId;
    }

    // =====================================================================================================
    // The deed log (#2599)
    // =====================================================================================================

    /// <summary>
    /// #2599: writes a deed into this world's log (<see cref="WorldMetadata.Deeds"/>, the latest
    /// <see cref="DeedRecord.MaxPerWorld"/>) and saves the metadata. <paramref name="kind"/> is one of
    /// <see cref="DeedKinds"/>; <paramref name="settlement"/> the settlement it happened at ("" = the whole world talks
    /// about it); <paramref name="arg"/> its "thing" (a giant's kind). The reporter's and the innkeeper's news and the
    /// villagers' chatter pick it up on their own. Returns the record, or null for an invisible observer (who leaves no
    /// trace) or a missing hero.
    /// <para>The festival package (C6) calls <c>RecordDeed(DeedKinds.SettlementHelped, hero, settlementName)</c> when the
    /// "settlement needs" chain is done and <c>RecordDeed(DeedKinds.Festival, hero, settlementName)</c> when the festival
    /// starts — on the game thread, with the settlement's world active.</para>
    /// </summary>
    internal DeedRecord? RecordDeed(string kind, PlayerSession hero, string settlement, string arg = "")
        => hero.Spectating ? null : RecordDeed(kind, hero.State.PlayerId, hero.State.Name ?? string.Empty, settlement, arg);

    /// <summary>#2599: <see cref="RecordDeed(string, PlayerSession, string, string)"/> for a hero who may be offline (by id
    /// and name).</summary>
    internal DeedRecord? RecordDeed(string kind, string playerId, string playerName, string settlement, string arg = "")
    {
        if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(playerId) || _world is null)
        {
            return null;
        }

        string world = _world.LocationId;
        if (!_meta.Deeds.TryGetValue(world, out var log))
        {
            _meta.Deeds[world] = log = new List<DeedRecord>();
        }

        var deed = new DeedRecord
        {
            Id = log.Count == 0 ? 1 : log.Max(d => d.Id) + 1,
            Kind = kind,
            PlayerId = playerId,
            PlayerName = playerName ?? string.Empty,
            Settlement = settlement ?? string.Empty,
            Arg = arg ?? string.Empty,
            At = System.Math.Max(1, _meta.CumulativePlaytimeSeconds),
        };
        // A busy hosted world sees many first landings: they make room among themselves, so the log keeps its camps,
        // giants and festivals (and the news is not only "welcome!").
        if (kind == DeedKinds.Landing && log.Count(d => d.Kind == DeedKinds.Landing) >= MaxLandingDeeds)
        {
            log.RemoveAt(log.FindIndex(d => d.Kind == DeedKinds.Landing));
        }

        log.Add(deed);
        while (log.Count > DeedRecord.MaxPerWorld)
        {
            log.RemoveAt(0);
        }

        _repo.SaveMetadata(_meta);
        _log.Info($"Deed on '{world}': {kind} by '{deed.PlayerName}'{(deed.Settlement.Length > 0 ? " at " + deed.Settlement : string.Empty)}.");
        return deed;
    }

    /// <summary>The inhabited, named settlements within <paramref name="range"/> of a position, nearest first.</summary>
    private List<SettlementInstance> SettlementsNear(Vector3f at, float range)
    {
        double rangeSq = (double)range * range;
        return _settlements
            .Where(s => !s.Ruined && s.Name.Length > 0)
            .Select(s => (S: s, D: WrapDistSq(at, new Vector3f((s.Min.X + s.Max.X) * 0.5f, (s.Min.Y + s.Max.Y) * 0.5f, (s.Min.Z + s.Max.Z) * 0.5f))))
            .Where(x => x.D <= rangeSq)
            .OrderBy(x => x.D)
            .Select(x => x.S)
            .ToList();
    }

    /// <summary>The hero of a deed: the player who did it (an invisible observer leaves no trace — no hero then), else —
    /// nobody did it by hand, e.g. fire burned the last bandit — the nearest player on foot on this world within
    /// <see cref="DeedWitnessRange"/>. Null when nobody was there.</summary>
    private PlayerSession? DeedHero(PlayerSession? doer, Vector3f at)
    {
        if (doer is not null)
        {
            return doer is { Joined: true, Spectating: false } ? doer : null;
        }

        PlayerSession? nearest = null;
        double best = (double)DeedWitnessRange * DeedWitnessRange;
        foreach (var s in JoinedInActiveWorld())
        {
            double d = WrapDistSq(s.State.Position, at);
            if (!s.Spectating && OnFootHere(s) && d <= best)
            {
                best = d;
                nearest = s;
            }
        }

        return nearest;
    }

    /// <summary>#2599: the last bandit of a camp went down — a "camp_cleared" deed for the hero (the attacker; a sentry's
    /// base owner; fire → the nearest player within 64 m), at the nearest settlement within 400 m, and every villager of
    /// the settlements within 400 m who already knows the hero is glad (+<see cref="CampClearedStandingBonus"/>).</summary>
    private void OnCampClearedDeed(BanditCampInstance camp, Vector3f at, PlayerSession? killer)
    {
        if (DeedHero(killer, at) is not { } hero)
        {
            return;
        }

        var near = SettlementsNear(camp.Center, DeedSettlementRange);
        RecordDeed(DeedKinds.CampCleared, hero, near.Count > 0 ? near[0].Name : string.Empty, camp.Key);

        int glad = 0;
        foreach (var s in near)
        {
            glad += AddStandingToKnownVillagers(hero.State, s.Name, CampClearedStandingBonus, "camp cleared");
        }

        if (glad > 0)
        {
            _repo.SavePlayer(hero.State);
            if (hero.CurrentLocationId == _world.LocationId)
            {
                SendNpcStandings(hero); // the nameplates of THIS world's people (a hero elsewhere gets theirs on arrival)
            }
        }
    }

    /// <summary>#2599: a giant or the Sreekmakra was defeated — a deed for the killer (else the nearest player within
    /// 64 m), at the nearest settlement within 400 m.</summary>
    private void OnBeastDefeatedDeed(string kind, PlayerSession? killer, Vector3f at, string arg)
    {
        if (DeedHero(killer, at) is not { } hero)
        {
            return;
        }

        var near = SettlementsNear(at, DeedSettlementRange);
        RecordDeed(kind, hero, near.Count > 0 ? near[0].Name : string.Empty, arg);
    }

    /// <summary>#2599: a player's first landing on a world with an inhabited settlement (never the spawn world, which the
    /// caller excludes) — the whole world hears that someone new arrived.</summary>
    private void OnFirstLandingDeed(PlayerSession session, CelestialBody body)
    {
        if (_world is null || body.Id != _world.LocationId || !_settlements.Any(s => !s.Ruined && s.Name.Length > 0))
        {
            return;
        }

        RecordDeed(DeedKinds.Landing, session, string.Empty);
    }

    // =====================================================================================================
    // The news: interviews + deeds (#2599)
    // =====================================================================================================

    /// <summary>One story of the news: an interview or a deed.</summary>
    private readonly struct NewsStory
    {
        public NewsStory(long at, int order, NewsArticle? article, DeedRecord? deed)
        {
            At = at;
            Order = order;
            Article = article;
            Deed = deed;
        }

        public long At { get; }
        public int Order { get; }
        public NewsArticle? Article { get; }
        public DeedRecord? Deed { get; }
    }

    /// <summary>
    /// #2599: the latest <paramref name="count"/> stories for the news a reporter or an innkeeper tells — the place's
    /// interviews (<see cref="WorldMetadata.News"/>) and the deeds of this world that concern the place
    /// (<see cref="DeedConcerns"/>), newest first by world playtime; interviews from before the deed log (no time) last.
    /// </summary>
    private List<NewsStory> LatestNews(ServerNpc npc, int count)
    {
        var stories = new List<NewsStory>();
        if (_meta.News.TryGetValue(NewsPlaceKey(npc), out var articles))
        {
            for (int i = 0; i < articles.Count; i++)
            {
                stories.Add(new NewsStory(articles[i].At, i, articles[i], null));
            }
        }

        if (_meta.Deeds.TryGetValue(_world.LocationId, out var log))
        {
            for (int i = 0; i < log.Count; i++)
            {
                if (DeedConcerns(log[i], npc.Settlement))
                {
                    stories.Add(new NewsStory(log[i].At, i, null, log[i]));
                }
            }
        }

        return stories.OrderByDescending(s => s.At).ThenByDescending(s => s.Order).Take(count).ToList();
    }

    /// <summary>The digest lines of the news for one reader ("• Name: text" for an interview, "• headline" for a deed).</summary>
    private List<string> NewsDigestLines(PlayerSession session, ServerNpc npc)
    {
        var lines = new List<string>();
        foreach (var story in LatestNews(npc, NewsDigestCount))
        {
            if (story.Article is { } article)
            {
                string text = article.Text.StartsWith("@", System.StringComparison.Ordinal) ? Localize(session.Locale, article.Text.Substring(1)) : article.Text;
                lines.Add("• " + article.PlayerName + ": " + text);
            }
            else if (story.Deed is { } deed)
            {
                lines.Add("• " + DeedHeadline(session, npc, deed));
            }
        }

        return lines;
    }

    /// <summary>The place a deed's headline names: its settlement, else this world.</summary>
    private string DeedPlaceName(DeedRecord deed) => deed.Settlement.Length > 0 ? deed.Settlement : WorldDisplayName();

    /// <summary>The deed's "thing" in the reader's language (a giant: "a colossus"), "" when it has none.</summary>
    private string DeedThingName(string locale, DeedRecord deed)
    {
        if (deed.Kind != DeedKinds.Giant)
        {
            return string.Empty;
        }

        string key = "news.thing." + deed.Arg;
        return Localize(locale, deed.Arg.Length > 0 && EnglishHas(key) ? key : "news.thing.giant");
    }

    /// <summary>
    /// A deed's headline for one reader: the authored <c>news.deed.&lt;kind&gt;</c> line ({0} hero, {1} place, {2} thing)
    /// — or, with AI on, a cached re-wording in the teller's voice around the same exact facts (one line,
    /// ≤ <see cref="NewsDeedMaxChars"/>; a miss asks for one in the background, so the authored line shows first).
    /// </summary>
    private string DeedHeadline(PlayerSession session, ServerNpc teller, DeedRecord deed)
    {
        string locale = session.Locale;
        string template = Localize(locale, "news.deed." + deed.Kind);
        string place = DeedPlaceName(deed);
        string thing = DeedThingName(locale, deed);
        string authored = template.Replace("{0}", deed.PlayerName).Replace("{1}", place).Replace("{2}", thing);
        if (!AiLinesEnabled)
        {
            return authored;
        }

        string tokenized = template.Replace("{0}", AiLineTokens.Player).Replace("{1}", AiLineTokens.Place).Replace("{2}", AiLineTokens.Thing);
        var tokens = AiLineText.TokensIn(new[] { tokenized });
        string tellerKey = NpcKeyForNpc(session, teller);
        string cacheKey = AiLineCacheKey(NpcLinePurposes.NewsDeed,
            deed.Kind + "#" + _world.LocationId + "#" + deed.Id.ToString(CultureInfo.InvariantCulture),
            PersonaIndexFor(tellerKey, teller.IsRobot, teller.Role), locale, string.Empty);
        var ai = AiLinesFor(cacheKey, AiLinePriority.News, () =>
        {
            var request = new NpcLinesRequest
            {
                Purpose = NpcLinePurposes.NewsDeed,
                Language = locale,
                Place = ChatterPlaceWords(NewsPlaceKey(teller)),
                Theme = teller.Theme,
                TopicHint = "local news about a player's deed",
                LineCount = 1,
                MaxChars = NewsDeedMaxChars,
                Speakers = { ChatterSpeaker(teller, tellerKey, string.Empty) },
            };
            request.Facts.Add(NewsDeedFact(deed.Kind));
            request.Tokens.AddRange(tokens);
            request.Authored.Add(tokenized);
            return request;
        }, requiredTokens: tokens, allowedTokens: tokens);

        var values = new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            [AiLineTokens.Player] = deed.PlayerName,
            [AiLineTokens.Place] = place,
            [AiLineTokens.Thing] = thing,
        };
        return ai is { Length: 1 } && AiLineText.FillTokens(ai[0], values) is { Length: > 0 } filled ? filled : authored;
    }

    // =====================================================================================================
    // Test seams
    // =====================================================================================================

    /// <summary>Test seam (#2600): registers a settlement box on the active world (fixtures without generated settlements).</summary>
    public void RegisterTestSettlementForTest(string name, Vector3i min, Vector3i max)
        => _settlements.Add(new SettlementInstance { Name = name, Min = min, Max = max, Tier = "village", GroundY = min.Y });

    /// <summary>Test seam (#2600): spawns a villager of <paramref name="settlement"/> with a daily routine and a person key,
    /// standing still at <paramref name="pos"/> (its home, work and rest spot). Returns its id. With
    /// <paramref name="looksForFurniture"/> it looks for a bed and a seat around itself like a villager (#2655); without, it
    /// never does and rests where it stands.</summary>
    public int SpawnVillagerForTest(string settlement, Vector3f pos, string role = "settler", string job = "", bool robot = false,
        bool looksForFurniture = false)
    {
        var npc = MakeNpc(role, "settlers", robot, pos, new System.Random(_nextNpcId * 7919));
        npc.Settlement = settlement;
        npc.Job = job;
        npc.RoutineEnabled = true;
        npc.FurnitureGen = looksForFurniture ? 0 : long.MaxValue; // MaxValue: never older than the world's furniture — no look
        npc.Leash = 0f;
        npc.Work = pos;
        npc.HasWork = true;
        npc.PersonKey = SettlementLocationKey(settlement) + "#test#" + npc.Id.ToString(CultureInfo.InvariantCulture) + ":" + role;
        _npcs.Add(npc);
        return npc.Id;
    }

    /// <summary>Test seam (#2600): runs the village-life pass of the active world now (lines due, checks, a scan).</summary>
    public void TickVillageLifeForTest()
    {
        var state = VillageLifeOf(_world.LocationId);
        state.NextPassAt = 0;
        state.NextScanAt = 0;
        TickVillageLife(0.0);
    }

    /// <summary>Test seam (#2600): only this topic may be picked (null = every topic) — it still has to fit.</summary>
    public void SetChatterTopicForTest(string? topicKey) => _chatterTopicForTest = topicKey;

    /// <summary>Test seam (#2600): the person with the lower id always speaks first (instead of a coin flip).</summary>
    public void SetChatterOrderForTest(bool lowerIdFirst) => _chatterFixedOrderForTest = lowerIdFirst;

    /// <summary>Test seam (#2600): the AI cache key of a conversation between two NPCs about a topic in a language.</summary>
    public string ChatterCacheKeyForTest(string playerId, int firstNpcId, int secondNpcId, string topicKey, string locale)
    {
        var s = FindSessionByPlayerId(playerId);
        var a = _npcs.FirstOrDefault(n => n.Id == firstNpcId);
        var b = _npcs.FirstOrDefault(n => n.Id == secondNpcId);
        var topic = _content.VillageLife.Chatter.FirstOrDefault(t => t.Key == topicKey);
        if (s is null || a is null || b is null || topic is null)
        {
            return string.Empty;
        }

        var chat = new VillageChat(a, b, NewsPlaceKey(a), topic, new Dictionary<string, string>(System.StringComparer.Ordinal),
            NpcKeyForNpc(s, a), NpcKeyForNpc(s, b), string.Empty, string.Empty, 0);
        return ChatterCacheKey(chat, locale);
    }

    /// <summary>Test seam (#2600): clears every place's rest and every person's rest on the active world.</summary>
    public void ClearChatterRestForTest()
    {
        foreach (var place in VillageLifeOf(_world.LocationId).Places.Values)
        {
            place.NextChatAt = 0;
            place.RecentTopics.Clear();
        }

        foreach (var npc in _npcs)
        {
            npc.ChatRestUntil = 0;
        }
    }

    /// <summary>Test seam (#2600): the running conversations of the active world (speaker ids, topic, next line).</summary>
    public IReadOnlyList<(int A, int B, string Topic, int NextLine)> VillageChatsForTest
        => VillageLifeOf(_world.LocationId).Chats.Select(c => (c.A.Id, c.B.Id, c.Topic.Key, c.Next)).ToList();

    /// <summary>Test seam (#2600): the place's rest left in seconds (0 = free).</summary>
    public double ChatterPlaceRestForTest(string placeKey)
        => VillageLifeOf(_world.LocationId).Places.TryGetValue(placeKey, out var p) ? System.Math.Max(0, p.NextChatAt - _uptime) : 0;

    /// <summary>Test seam (#2600): the topics that fit the pair now as heard by the player (key, weight).</summary>
    public IReadOnlyList<(string Topic, int Weight)> ChatterTopicsForTest(int npcA, int npcB, string playerId, bool evening = false)
    {
        var a = _npcs.FirstOrDefault(n => n.Id == npcA);
        var b = _npcs.FirstOrDefault(n => n.Id == npcB);
        if (a is null || b is null || FindSessionByPlayerId(playerId) is not { } s)
        {
            return System.Array.Empty<(string, int)>();
        }

        var place = PlaceStateOf(VillageLifeOf(_world.LocationId), NewsPlaceKey(a));
        var mid = Midpoint(a.Pos, b.Pos);
        return ChatterCandidates(place, a, b, mid, new List<PlayerSession> { s }, evening, ChatterWeatherNeed(mid))
            .Select(c => (c.Topic.Key, c.Weight)).ToList();
    }

    /// <summary>Test seam (#2600): an NPC's facing (yaw) and whom it talks with (0 = nobody).</summary>
    public (float Facing, int TalkWith)? NpcTalkStateForTest(int npcId)
        => _npcs.FirstOrDefault(n => n.Id == npcId) is { } n ? (n.Facing, n.TalkWith?.Id ?? 0) : null;

    /// <summary>Test seam (#2599): the deed log of the active world, oldest first.</summary>
    public IReadOnlyList<(long Id, string Kind, string PlayerName, string Settlement, string Arg, long At)> DeedsForTest
    {
        get
        {
            var result = new List<(long Id, string Kind, string PlayerName, string Settlement, string Arg, long At)>();
            if (_meta.Deeds.TryGetValue(_world.LocationId, out var log))
            {
                result.AddRange(log.Select(d => (d.Id, d.Kind, d.PlayerName, d.Settlement, d.Arg, d.At)));
            }

            return result;
        }
    }

    /// <summary>Test seam (#2599): <see cref="RecordDeed(string, string, string, string, string)"/> for a joined player.</summary>
    public bool RecordDeedForTest(string kind, string playerId, string settlement = "", string arg = "")
        => FindSessionByPlayerId(playerId) is { } s && RecordDeed(kind, s, settlement, arg) is not null;

    /// <summary>Test seam (#2599): the news digest the NPC would tell this player (the lines, newest first).</summary>
    public IReadOnlyList<string> NewsDigestForTest(string playerId, int npcId)
        => FindSessionByPlayerId(playerId) is { } s && _npcs.FirstOrDefault(n => n.Id == npcId) is { } npc
            ? NewsDigestLines(s, npc)
            : System.Array.Empty<string>();

    /// <summary>Test seam (#2599): an interview article for a place with a given playtime stamp (0 = an old article).</summary>
    public void AddNewsArticleForTest(string placeKey, string playerName, string text, long at)
    {
        if (!_meta.News.TryGetValue(placeKey, out var list))
        {
            _meta.News[placeKey] = list = new List<NewsArticle>();
        }

        list.Add(new NewsArticle { PlayerName = playerName, Text = text, Day = MarketDay, At = at });
    }

    /// <summary>Test seam (#2599): sets the world's playtime clock (the deed log's time).</summary>
    public void SetPlaytimeForTest(long seconds) => _meta.CumulativePlaytimeSeconds = seconds;

    /// <summary>Test seam (#2599): a giant of <paramref name="kind"/> ("colossus", "sandworm", "leviathan", "sky_giant")
    /// falls at <paramref name="at"/> to <paramref name="killerPlayerId"/> ("" = nobody) — the defeat hook, without the
    /// spawn and the fight.</summary>
    public void DefeatGiantForTest(string kind, string killerPlayerId, Vector3f at)
    {
        var plan = kind switch
        {
            "sandworm" => CreatureBodyPlan.Sandworm,
            "leviathan" => CreatureBodyPlan.Leviathan,
            "sky_giant" => CreatureBodyPlan.SkyGiant,
            _ => CreatureBodyPlan.Colossus,
        };
        var giant = new CombatEntity
        {
            Id = NextEntityId(),
            Kind = CombatEntityKind.Creature,
            Position = at,
            Giant = new GiantRuntime { Kind = plan },
        };
        OnGiantDefeated(giant, killerPlayerId.Length > 0 ? FindSessionByPlayerId(killerPlayerId) : null);
    }
}
