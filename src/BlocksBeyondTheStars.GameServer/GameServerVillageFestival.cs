// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #2604 — the village festival: a settlement throws a party for the player who finished its four-step "settlement
/// needs" chain (<c>data/missions.json</c>).
/// <list type="bullet">
/// <item><b>Planned</b> when the chain's last step is turned in (<see cref="OnSettlementChainDone"/>, from
/// <c>OnChainStepTurnedIn</c>): a "settlement_helped" deed, and <see cref="WorldMetadata.Festivals"/> gets a PENDING
/// record for the settlement — once per settlement, ever. The quartermaster says so (a direct reply to the turn-in) and
/// VEGA repeats it. A second player who finishes the chain at the same settlement later gets the thanks and the present
/// right away, but no second festival. With AI on, the cheers and the quartermaster's thanks are asked for now, in the
/// hero's language, so they are ready when the party starts.</item>
/// <item><b>Waits</b> without a deadline (no FOMO) until the hero stands within <see cref="FestivalHeroRange"/> of the
/// festival square in the late afternoon or evening (local day <see cref="FestivalWindowStart"/>–
/// <see cref="FestivalWindowEnd"/>); the villagers' "festival_pending" chatter names the hero meanwhile.</item>
/// <item><b>The party</b> (<see cref="FestivalSeconds"/>): up to <see cref="FestivalMaxAttendees"/> residents within
/// <see cref="FestivalAttendeeRange"/> walk into a ring around the square (the routine phase
/// <see cref="NpcPhase.Festival"/>, nameplate "celebrating") and face its middle; every <see cref="FestivalCheerGap"/> s
/// ONE of them cheers (Kind Cheer + the cheer gesture — one voice at a time, no jumble); a little bell tune plays at the
/// start and every <see cref="FestivalJingleGap"/> s (positional <see cref="SoundFx"/> notes, nothing new on the client);
/// the quartermaster hands over the present (a full bag: at the next talk to them); every attendee likes the hero a bit
/// more (+<see cref="FestivalStandingBonus"/>); a "festival" deed for the news. No block is changed. Afterwards the
/// routine takes everyone back at its next check.</item>
/// </list>
/// The record is persisted; the party itself is runtime only (a restart in the middle simply ends it — everything
/// once-only happened at its start).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The chain whose end throws the festival.</summary>
    internal const string SettlementNeedsChainId = "settlement_needs";

    /// <summary>The festival starts once its hero stands this close to the festival square.</summary>
    private const float FestivalHeroRange = 48f;

    /// <summary>The local day fraction from which a waiting festival may start (late afternoon) …</summary>
    private const double FestivalWindowStart = 0.60;

    /// <summary>… and until which (deep night is for sleeping).</summary>
    private const double FestivalWindowEnd = 0.95;

    /// <summary>How long the party lasts (seconds).</summary>
    private const double FestivalSeconds = 180.0;

    /// <summary>The most residents who join the ring.</summary>
    private const int FestivalMaxAttendees = 24;

    /// <summary>Residents within this range of the square join (nearest first).</summary>
    private const float FestivalAttendeeRange = 64f;

    /// <summary>The ring's radius: 1.5 + 0.25 per attendee, clamped to this range.</summary>
    private const float FestivalRingMin = 3f, FestivalRingMax = 6f;

    /// <summary>Seconds between two cheers of one festival (NO SOUND JUMBLE: one voice at a time).</summary>
    private const double FestivalCheerGap = 5.0;

    /// <summary>Who hears a cheer: every player on foot this close to the cheering villager.</summary>
    private const float FestivalCheerRange = 32f;

    /// <summary>The bell tune plays at the start and every this many seconds.</summary>
    private const double FestivalJingleGap = 45.0;

    /// <summary>Seconds between two notes of the tune.</summary>
    private const double FestivalNoteGap = 0.35;

    /// <summary>The tune: indices into the melody block's C-major scale (C E G C' G E G C').</summary>
    private static readonly int[] FestivalTune = { 0, 2, 4, 7, 4, 2, 4, 7 };

    /// <summary>The instrument of the tune — the melody block's bell (<c>note_1_&lt;n&gt;</c>, synthesized on the client).</summary>
    private const string FestivalNotePrefix = "note_1_";

    /// <summary>Players this close to the square get the tune.</summary>
    private const float FestivalSoundRange = 64f;

    /// <summary>The quartermaster's direct words reach a player this close (the client's addressed voice range).</summary>
    private const float FestivalSpeakRange = 20f;

    /// <summary>VEGA's festival lines go out on the system channel (never muted with the hints).</summary>
    private const byte FestivalVegaKind = 3;

    /// <summary>VEGA's "the party started" waits a moment so the quartermaster's thanks is heard first.</summary>
    private const double FestivalVegaDelay = 6.0;

    /// <summary>The first cheer comes a moment after the start (the quartermaster speaks first).</summary>
    private const double FestivalFirstCheerDelay = 5.0;

    /// <summary>The nameplate activity of a villager at the festival.</summary>
    private const string FestivalActivityKey = "npc.activity.celebrating";

    /// <summary>The "persona" slot of the cheering crowd in AI-line cache keys (outside the NPC range 0..13 and VEGA's 100).</summary>
    private const int FestivalCrowdPersonaIndex = 200;

    /// <summary>The voice of the cheering crowd for the AI line path.</summary>
    private const string FestivalCrowdPersona = "the happy people of a small frontier village, cheering together at their festival";

    /// <summary>Most characters of an AI cheer (a speech bubble).</summary>
    private const int FestivalCheerMaxChars = 60;

    /// <summary>Most characters of the quartermaster's AI thanks (the present list follows on its own line).</summary>
    private const int FestivalThanksMaxChars = 110;

    /// <summary>"festival_gift:&lt;festival key&gt;" — the player got this festival's present.</summary>
    private const string FestivalGiftMilestonePrefix = "festival_gift:";

    /// <summary>"festival_gift_due:&lt;festival key&gt;" — the present waits for room in the player's bag.</summary>
    private const string FestivalGiftDueMilestonePrefix = "festival_gift_due:";

    private readonly Dictionary<string, FestivalRun> _festivalRuns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _festivalTickAt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector3f> _festivalAnchors = new(StringComparer.Ordinal);
    private readonly Random _festivalRng = new(0xFE5717);

    /// <summary>One running festival (runtime only).</summary>
    private sealed class FestivalRun
    {
        public FestivalRun(string key, string locationId, string settlement, string heroId, string heroName, Vector3f anchor, double endsAt)
        {
            Key = key;
            LocationId = locationId;
            Settlement = settlement;
            HeroId = heroId;
            HeroName = heroName;
            Anchor = anchor;
            EndsAt = endsAt;
        }

        public string Key { get; }
        public string LocationId { get; }
        public string Settlement { get; }
        public string HeroId { get; }
        public string HeroName { get; }

        /// <summary>The middle of the festival square (feet level).</summary>
        public Vector3f Anchor { get; }

        public double EndsAt { get; }

        /// <summary>The attendees and their ring spots (reference keys: a reloaded world has new NPC objects).</summary>
        public Dictionary<ServerNpc, Vector3f> Spots { get; } = new();

        /// <summary>The tune's notes still to play: (uptime, sound id).</summary>
        public Queue<(double At, string SoundId)> Notes { get; } = new();

        public double NextCheerAt;
        public double NextJingleAt;
        public double VegaDueAt;
        public int CheerCursor;
        public ServerNpc? LastCheer;
    }

    /// <summary>Where an attendee stands and what it looks at.</summary>
    private readonly record struct FestivalPlace(Vector3f Spot, Vector3f Anchor);

    /// <summary>The persisted key of a settlement's festival: "&lt;locationId&gt;|&lt;settlement name&gt;".</summary>
    private static string FestivalKey(string locationId, string settlementName) => locationId + "|" + settlementName;

    // =====================================================================================================
    // Planned: the chain is done (#2604)
    // =====================================================================================================

    /// <summary>
    /// The last step of a chain was turned in and nothing follows (called by <c>OnChainStepTurnedIn</c>). For the
    /// "settlement needs" chain: a "settlement_helped" deed, and the settlement's festival is planned for this hero — or,
    /// when the settlement already has one (another player's), this hero gets the thanks and the present right away.
    /// </summary>
    private void OnSettlementChainDone(PlayerSession session, MissionDefinition def, MissionProgress pr)
    {
        if (!string.Equals(def.ChainId, SettlementNeedsChainId, StringComparison.Ordinal) || session.Spectating || _world is null)
        {
            return;
        }

        string place = string.IsNullOrEmpty(pr.AcceptedFrom) ? CurrentPlaceKey(session.State) : pr.AcceptedFrom;
        var settlement = _settlements.FirstOrDefault(s => !s.Ruined && s.Name.Length > 0
            && string.Equals(SettlementLocationKey(s.Name), place, StringComparison.Ordinal));
        if (settlement is null)
        {
            return;
        }

        RecordDeed(DeedKinds.SettlementHelped, session, settlement.Name);
        ScheduleFestival(session, settlement, place);
    }

    /// <summary>Plans the settlement's festival for the hero (once per settlement), or thanks a second hero with the present.</summary>
    private void ScheduleFestival(PlayerSession hero, SettlementInstance settlement, string placeKey)
    {
        string key = FestivalKey(_world.LocationId, settlement.Name);
        var quartermaster = FestivalQuartermaster(settlement.Name);
        if (_meta.Festivals.TryGetValue(key, out var existing))
        {
            if (!string.Equals(existing.HeroId, hero.State.PlayerId, StringComparison.Ordinal))
            {
                // A second hero: thanks and the present now — the village celebrates only once (E12).
                GiveFestivalGift(hero, quartermaster, key, "village.festival.thanks_again", NpcGreetingKinds.Addressed, NpcGestures.Wave);
            }

            return;
        }

        _meta.Festivals[key] = new FestivalRecord
        {
            State = FestivalRecord.Pending,
            HeroId = hero.State.PlayerId,
            HeroName = hero.State.Name ?? string.Empty,
            ScheduledAt = Math.Max(1, _meta.CumulativePlaytimeSeconds),
        };
        _repo.SaveMetadata(_meta);
        _log.Info($"Festival planned at '{settlement.Name}' on '{_world.LocationId}' for '{hero.State.Name}'.");

        PrewarmFestivalLines(hero, settlement, placeKey, quartermaster);

        // Already there in the late afternoon? Then the party starts right away (no "come back this evening" first).
        if (TryStartFestival(key, _meta.Festivals[key]))
        {
            return;
        }

        // The quartermaster answers the turn-in in person (a direct reply — the client's addressed voice), VEGA repeats it.
        if (quartermaster is not null && WrapDistSq(quartermaster.Pos, hero.State.Position) <= (double)FestivalSpeakRange * FestivalSpeakRange)
        {
            SayToHero(hero, quartermaster, FillHero(Localize(hero.Locale, "village.festival.planned"), hero.State.Name ?? string.Empty),
                NpcGreetingKinds.Addressed, NpcGestures.Cheer);
        }

        SpeakVegaChatter(hero, "vega.festival.planned", FestivalVegaKind, new[] { settlement.Name }, ask: null);
    }

    /// <summary>
    /// #2604 (the chatter hook of #2600): the name of the hero a planned festival at this settlement still waits for, or
    /// "" — lights up the villagers' "festival_pending" topic.
    /// </summary>
    private string FestivalPendingHero(string settlementName)
    {
        if (_meta.Festivals.Count == 0 || string.IsNullOrEmpty(settlementName) || _world is null)
        {
            return string.Empty;
        }

        return _meta.Festivals.TryGetValue(FestivalKey(_world.LocationId, settlementName), out var record)
               && string.Equals(record.State, FestivalRecord.Pending, StringComparison.Ordinal)
            ? record.HeroName
            : string.Empty;
    }

    // =====================================================================================================
    // The tick (#2604)
    // =====================================================================================================

    /// <summary>Per world (Guard-registered after the village life): the tune's notes every tick; once a second the
    /// running festivals (cheers, the next tune, the end) and the waiting ones (does the hero stand there in the late
    /// afternoon?).</summary>
    private void TickVillageFestivals(double dt)
    {
        if (_world is null || (_festivalRuns.Count == 0 && _meta.Festivals.Count == 0))
        {
            return;
        }

        string world = _world.LocationId;
        PlayDueFestivalNotes(world);
        if (_festivalTickAt.TryGetValue(world, out double due) && _uptime < due)
        {
            return;
        }

        _festivalTickAt[world] = _uptime + 1.0;
        RunFestivalPass(world);
    }

    private void RunFestivalPass(string world)
    {
        foreach (var run in _festivalRuns.Values.Where(r => string.Equals(r.LocationId, world, StringComparison.Ordinal)).ToList())
        {
            StepFestival(run);
        }

        if (_meta.Festivals.Count == 0)
        {
            return;
        }

        string prefix = world + "|";
        foreach (var entry in _meta.Festivals.Where(e => e.Key.StartsWith(prefix, StringComparison.Ordinal)
                     && string.Equals(e.Value.State, FestivalRecord.Pending, StringComparison.Ordinal)).ToList())
        {
            TryStartFestival(entry.Key, entry.Value);
        }
    }

    /// <summary>Starts a waiting festival when its hero — joined on this world, on foot, not observing — stands within
    /// <see cref="FestivalHeroRange"/> of the square in the late afternoon or evening there. Returns true when it started.</summary>
    private bool TryStartFestival(string key, FestivalRecord record)
    {
        if (!string.Equals(record.State, FestivalRecord.Pending, StringComparison.Ordinal) || _festivalRuns.ContainsKey(key))
        {
            return false;
        }

        string name = key.Substring(key.IndexOf('|', StringComparison.Ordinal) + 1);
        var settlement = _settlements.FirstOrDefault(s => !s.Ruined && string.Equals(s.Name, name, StringComparison.Ordinal));
        if (settlement is null)
        {
            return false;
        }

        PlayerSession? hero = null;
        foreach (var s in JoinedInActiveWorld())
        {
            if (string.Equals(s.State.PlayerId, record.HeroId, StringComparison.Ordinal) && !s.Spectating && OnFootHere(s))
            {
                hero = s;
                break;
            }
        }

        if (hero is null)
        {
            return false;
        }

        // A cheap look first: the square is somewhere in the settlement's box — only near it is the square worked out
        // (that reads blocks), and then remembered for this run.
        var box = SettlementBoxCentre(settlement);
        float reach = FestivalHeroRange + 0.5f * Math.Max(settlement.Max.X - settlement.Min.X, settlement.Max.Z - settlement.Min.Z) + 2f;
        if (WrapDistSq(hero.State.Position, box) > (double)reach * reach)
        {
            return false;
        }

        if (!_festivalAnchors.TryGetValue(key, out var anchor))
        {
            _festivalAnchors[key] = anchor = FestivalAnchorFor(settlement);
        }

        if (WrapDistSq(hero.State.Position, anchor) > (double)FestivalHeroRange * FestivalHeroRange)
        {
            return false;
        }

        double t = LocalDayFraction(anchor);
        if (t < FestivalWindowStart || t >= FestivalWindowEnd)
        {
            return false;
        }

        StartFestival(key, record, settlement, anchor, hero);
        return true;
    }

    /// <summary>The party begins: the ring, the tune, the present, the friendship, VEGA and the deed.</summary>
    private void StartFestival(string key, FestivalRecord record, SettlementInstance settlement, Vector3f anchor, PlayerSession hero)
    {
        record.State = FestivalRecord.Held;
        record.HeldAt = Math.Max(1, _meta.CumulativePlaytimeSeconds);
        _repo.SaveMetadata(_meta);

        var run = new FestivalRun(key, _world.LocationId, settlement.Name, record.HeroId, record.HeroName, anchor, _uptime + FestivalSeconds)
        {
            NextCheerAt = _uptime + FestivalFirstCheerDelay,
            NextJingleAt = _uptime + FestivalJingleGap,
            VegaDueAt = _uptime + FestivalVegaDelay,
        };
        SeatFestivalRing(run, FestivalAttendees(settlement.Name, anchor));
        _festivalRuns[key] = run;
        foreach (var npc in run.Spots.Keys)
        {
            ApplyRoutinePhase(npc, NpcPhase.Festival);
            npc.PhaseCheckedAt = _uptime;
        }

        QueueFestivalTune(run);

        // The quartermaster's thanks and present (once per player and festival).
        GiveFestivalGift(hero, FestivalQuartermaster(settlement.Name), key, "village.festival.thanks", NpcGreetingKinds.Welcome, NpcGestures.Wave);

        // Everyone at the party likes the hero a little more.
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var npc in run.Spots.Keys)
        {
            string npcKey = NpcKeyForNpc(hero, npc);
            if (keys.Add(npcKey))
            {
                AddStanding(hero.State, npcKey, FestivalStandingBonus, "festival");
            }
        }

        if (keys.Count > 0)
        {
            _repo.SavePlayer(hero.State);
            SendNpcStandings(hero);
        }

        RecordDeed(DeedKinds.Festival, hero, settlement.Name);
        _log.Info($"Festival at '{settlement.Name}' for '{record.HeroName}': {run.Spots.Count} villagers celebrate.");
    }

    /// <summary>One second of a running festival: villagers who are gone (a world reload) leave the ring; VEGA's line, a
    /// cheer, the next tune when due; the end after <see cref="FestivalSeconds"/>.</summary>
    private void StepFestival(FestivalRun run)
    {
        foreach (var gone in run.Spots.Keys.Where(n => !_npcs.Contains(n)).ToList())
        {
            run.Spots.Remove(gone);
        }

        if (_uptime >= run.EndsAt)
        {
            EndFestival(run);
            return;
        }

        if (run.VegaDueAt > 0 && _uptime >= run.VegaDueAt)
        {
            run.VegaDueAt = 0;
            if (FindSessionByPlayerId(run.HeroId) is { Joined: true, Spectating: false } hero
                && string.Equals(hero.CurrentLocationId, run.LocationId, StringComparison.Ordinal))
            {
                SpeakVegaChatter(hero, "vega.festival.started", FestivalVegaKind, new[] { run.Settlement }, ask: null);
            }
        }

        if (_uptime >= run.NextJingleAt)
        {
            run.NextJingleAt = _uptime + FestivalJingleGap;
            QueueFestivalTune(run);
        }

        if (_uptime >= run.NextCheerAt)
        {
            run.NextCheerAt = _uptime + FestivalCheerGap;
            FestivalCheer(run);
        }
    }

    /// <summary>The party is over: the routine takes every attendee back at its next check (<see cref="RoutinePhaseFor"/>
    /// no longer answers <see cref="NpcPhase.Festival"/>).</summary>
    private void EndFestival(FestivalRun run)
    {
        _festivalRuns.Remove(run.Key);
        run.Notes.Clear();
        _log.Info($"Festival at '{run.Settlement}' is over.");
    }

    /// <summary>The festival's ring spot and square for an attendee of a running festival on this world, or null.</summary>
    private FestivalPlace? FestivalPlaceOf(ServerNpc npc)
    {
        if (_festivalRuns.Count == 0 || _world is null)
        {
            return null;
        }

        foreach (var run in _festivalRuns.Values)
        {
            if (string.Equals(run.LocationId, _world.LocationId, StringComparison.Ordinal) && run.Spots.TryGetValue(npc, out var spot))
            {
                return new FestivalPlace(spot, run.Anchor);
            }
        }

        return null;
    }

    // =====================================================================================================
    // The square and the ring (#2604)
    // =====================================================================================================

    /// <summary>The middle of a settlement's box at the feet level of its ground row.</summary>
    private static Vector3f SettlementBoxCentre(SettlementInstance s)
        => new((s.Min.X + s.Max.X) / 2 + 0.5f, s.GroundY + 1, (s.Min.Z + s.Max.Z) / 2 + 0.5f);

    /// <summary>
    /// Where the festival gathers: the layout's centre (the plaza <c>SettlementGenerator.StampCentralFeature</c> paves
    /// there) when a ring fits around it in the open; else the villagers' "npc" marker nearest that centre; else the
    /// mission board; else the centre anyway. A ring "fits" when at least half of eight spots at the smallest radius can
    /// be stood on with no wall between them and the middle (a kit grid with a closed centre puts a house there).
    /// </summary>
    private Vector3f FestivalAnchorFor(SettlementInstance s)
    {
        var centre = s.Layout is { } layout
            ? new Vector3f(s.Min.X + layout.Width / 2 + 0.5f, s.GroundY + 1, s.Min.Z + layout.Length / 2 + 0.5f)
            : SettlementBoxCentre(s);
        var candidates = new List<Vector3f> { centre };
        candidates.AddRange(s.Markers.Where(m => string.Equals(m.Type, "npc", StringComparison.Ordinal))
            .Select(m => MarkerFeet(s, m.Pos)).OrderBy(p => WrapDistSq(p, centre)));
        candidates.AddRange(s.Markers.Where(m => string.Equals(m.Type, "mission_board", StringComparison.Ordinal))
            .Select(m => MarkerFeet(s, m.Pos)));
        foreach (var candidate in candidates)
        {
            if (FestivalRingFits(candidate))
            {
                return candidate;
            }
        }

        return centre;
    }

    /// <summary>The feet position on a marker's floor (markers sit centred in the air cell above it).</summary>
    private static Vector3f MarkerFeet(SettlementInstance s, Vector3f marker)
        => new(marker.X, (float)Math.Floor(Math.Max(s.Min.Y + 1f, marker.Y)), marker.Z);

    private bool FestivalRingFits(Vector3f anchor)
    {
        int open = 0;
        var taken = new HashSet<Vector3i>();
        for (int i = 0; i < 8; i++)
        {
            if (FestivalSpotAt(anchor, i * Math.PI / 4.0, FestivalRingMin, taken) is not null)
            {
                open++;
            }
        }

        return open >= 4;
    }

    /// <summary>The residents who join: this settlement's people with a routine (never a guardian, never someone living at
    /// a player's base, never a villager away visiting a base or a guest), within <see cref="FestivalAttendeeRange"/> of
    /// the square, nearest first, at most <see cref="FestivalMaxAttendees"/>.</summary>
    private List<ServerNpc> FestivalAttendees(string settlementName, Vector3f anchor)
    {
        double rangeSq = (double)FestivalAttendeeRange * FestivalAttendeeRange;
        return _npcs
            .Where(n => n.RoutineEnabled && n.BaseId == 0 && !n.VisitingAway && n.GuestKey.Length == 0
                        && !string.Equals(n.Role, "guardian", StringComparison.Ordinal)
                        && string.Equals(n.Settlement, settlementName, StringComparison.Ordinal)
                        && WrapDistSq(n.Pos, anchor) <= rangeSq)
            .OrderBy(n => WrapDistSq(n.Pos, anchor))
            .Take(FestivalMaxAttendees)
            .ToList();
    }

    /// <summary>Gives every attendee a spot on the ring — radius clamp(1.5 + 0.25·n, 3, 6) — in the order they stand
    /// around the square, so nobody crosses the circle to reach theirs. A spot that cannot be stood on is looked for a
    /// little further in or out and to the sides; without any, the attendee celebrates where it stands.</summary>
    private void SeatFestivalRing(FestivalRun run, List<ServerNpc> attendees)
    {
        int n = attendees.Count;
        if (n == 0)
        {
            return;
        }

        float radius = Math.Max(FestivalRingMin, Math.Min(FestivalRingMax, 1.5f + 0.25f * n));
        var ordered = attendees.OrderBy(a => AngleAround(run.Anchor, a.Pos)).ToList();
        double phase = AngleAround(run.Anchor, ordered[0].Pos);
        double step = 2.0 * Math.PI / n;
        var taken = new HashSet<Vector3i>();
        for (int i = 0; i < n; i++)
        {
            double angle = phase + i * step;
            Vector3f? spot = null;
            foreach (float r in new[] { radius, radius - 0.8f, radius + 0.8f, radius - 1.6f })
            {
                foreach (double nudge in new[] { 0.0, step * 0.3, -step * 0.3 })
                {
                    if (r >= 2.5f && (spot = FestivalSpotAt(run.Anchor, angle + nudge, r, taken)) is not null)
                    {
                        break;
                    }
                }

                if (spot is not null)
                {
                    break;
                }
            }

            var npc = ordered[i];
            run.Spots[npc] = spot ?? npc.Pos;
        }
    }

    /// <summary>The yaw of <paramref name="pos"/> as seen from <paramref name="anchor"/> (the NPC facing convention).</summary>
    private double AngleAround(Vector3f anchor, Vector3f pos)
        => Math.Atan2(WorldConstants.WrapDeltaX((double)pos.X - anchor.X, _world.Circumference), pos.Z - anchor.Z);

    /// <summary>A free standable spot <paramref name="radius"/> from the square at <paramref name="angle"/> (feet level, one
    /// up or down), with no wall between it and the square's edge; claims its cell in <paramref name="taken"/>.</summary>
    private Vector3f? FestivalSpotAt(Vector3f anchor, double angle, float radius, HashSet<Vector3i> taken)
    {
        int x = (int)Math.Floor(anchor.X + Math.Sin(angle) * radius);
        int z = (int)Math.Floor(anchor.Z + Math.Cos(angle) * radius);
        int y0 = (int)Math.Floor(anchor.Y);
        foreach (int dy in new[] { 0, 1, -1 })
        {
            var cell = new Vector3i(x, y0 + dy, z);
            if (taken.Contains(cell) || StandableSpot(cell.X, cell.Y, cell.Z) is not { } spot)
            {
                continue;
            }

            // The line to the square: stop 2 m short of its middle (a well, a statue or a flower bed stands there).
            float dx = spot.X - anchor.X, dz = spot.Z - anchor.Z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d > 2.2f && PathBlockedByTerrain(spot, new Vector3f(anchor.X + dx / d * 2f, spot.Y, anchor.Z + dz / d * 2f)))
            {
                continue;
            }

            taken.Add(cell);
            return spot;
        }

        return null;
    }

    // =====================================================================================================
    // Cheers and the tune (#2604)
    // =====================================================================================================

    /// <summary>One cheer from a random attendee (one who already stands in the ring while two or more do, never the same
    /// voice twice in a row) to every player on foot within <see cref="FestivalCheerRange"/>, each in their language.</summary>
    private void FestivalCheer(FestivalRun run)
    {
        var cheers = _content.VillageLife.Festival.Cheers;
        if (cheers.Count == 0 || run.Spots.Count == 0)
        {
            return;
        }

        var awake = run.Spots.Keys.Where(n => n.Pose != 2 && !n.VisitingAway).ToList();
        var arrived = awake.Where(n => n.Goal is null).ToList();
        var pool = arrived.Count > 1 ? arrived : awake;
        if (pool.Count > 1 && run.LastCheer is { } last)
        {
            pool.Remove(last);
        }

        if (pool.Count == 0)
        {
            return;
        }

        var speaker = pool[_festivalRng.Next(pool.Count)];
        int index = run.CheerCursor++ % cheers.Count;
        var memo = new Dictionary<string, string>(StringComparer.Ordinal);
        int id = BroadcastNpcLine(speaker, FestivalCheerRange, s =>
        {
            if (!memo.TryGetValue(s.Locale, out var text))
            {
                memo[s.Locale] = text = FestivalCheerText(run, s.Locale, index);
            }

            return text;
        }, NpcGreetingKinds.Cheer, NpcGestures.Cheer);
        if (id != 0)
        {
            run.LastCheer = speaker;
        }
    }

    /// <summary>A cheer in one language, the hero named: a cached AI cheer for this village and language (warmed when the
    /// festival was planned), else the authored line.</summary>
    private string FestivalCheerText(FestivalRun run, string locale, int index)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { [AiLineTokens.Player] = run.HeroName };
        if (CachedAiLines(FestivalCheerCacheKey(run.Settlement, locale), index) is { Length: > 0 } ai
            && AiLineText.FillTokens(ai[index % ai.Length], values) is { Length: > 0 } worded)
        {
            return worded;
        }

        var cheers = _content.VillageLife.Festival.Cheers;
        return FillHero(Localize(locale, cheers[index % cheers.Count]), run.HeroName);
    }

    /// <summary>Queues the bell tune (<see cref="FestivalTune"/>, <see cref="FestivalNoteGap"/> s apart).</summary>
    private void QueueFestivalTune(FestivalRun run)
    {
        for (int i = 0; i < FestivalTune.Length; i++)
        {
            run.Notes.Enqueue((_uptime + i * FestivalNoteGap, FestivalNotePrefix + FestivalTune[i].ToString(CultureInfo.InvariantCulture)));
        }
    }

    /// <summary>Plays the notes that are due: a positional <see cref="SoundFx"/> at the square for every player of this
    /// world (not in space) within <see cref="FestivalSoundRange"/>.</summary>
    private void PlayDueFestivalNotes(string world)
    {
        if (_festivalRuns.Count == 0)
        {
            return;
        }

        foreach (var run in _festivalRuns.Values)
        {
            if (!string.Equals(run.LocationId, world, StringComparison.Ordinal))
            {
                continue;
            }

            while (run.Notes.Count > 0 && run.Notes.Peek().At <= _uptime)
            {
                var note = run.Notes.Dequeue();
                var fx = new SoundFx { SoundId = note.SoundId, X = run.Anchor.X, Y = run.Anchor.Y + 1.5f, Z = run.Anchor.Z };
                foreach (var s in JoinedInActiveWorld())
                {
                    if (!InSpace(s.State.PlayerId)
                        && WrapDistSq(s.State.Position, run.Anchor) <= (double)FestivalSoundRange * FestivalSoundRange)
                    {
                        Send(s, fx);
                    }
                }
            }
        }
    }

    // =====================================================================================================
    // The present (#2604)
    // =====================================================================================================

    /// <summary>The settlement's quartermaster (the board's keeper), or null.</summary>
    private ServerNpc? FestivalQuartermaster(string settlementName)
        => _npcs.FirstOrDefault(n => n.BaseId == 0 && string.Equals(n.Settlement, settlementName, StringComparison.Ordinal)
                                     && string.Equals(n.Job, "quartermaster", StringComparison.Ordinal));

    /// <summary>
    /// Hands the festival's present (<c>village_life.json</c> <c>festival.gift</c>) to a player — once per player and
    /// festival (milestone <c>festival_gift:&lt;key&gt;</c>) — with the quartermaster's <paramref name="lineKey"/> and the
    /// list of what was given. A full bag keeps it for later (milestone <c>festival_gift_due:&lt;key&gt;</c>): the next talk
    /// to the quartermaster hands it over (<see cref="TryGiveOwedFestivalGift"/>). The words come as the quartermaster's
    /// bubble when they stand near the player, else as a message. Returns true when the present was handed over.
    /// </summary>
    private bool GiveFestivalGift(PlayerSession session, ServerNpc? quartermaster, string festivalKey, string lineKey, byte kind, byte gesture)
    {
        var p = session.State;
        string given = FestivalGiftMilestonePrefix + festivalKey;
        string due = FestivalGiftDueMilestonePrefix + festivalKey;
        if (p.Milestones.Contains(given))
        {
            return false;
        }

        string name = p.Name ?? string.Empty;
        var gift = _content.VillageLife.Festival.Gift.Where(g => g is not null && g.Count > 0 && _content.GetItem(g.Item) is not null).ToList();
        var bag = new MaterialPool(_content, p, _ship);
        if (gift.Count > 0 && !bag.CanFit(gift))
        {
            p.Milestones.Add(due);
            _repo.SavePlayer(p);
            SpeakFestivalLine(session, quartermaster, FillHero(Localize(session.Locale, "village.festival.bag_full"), name), kind, gesture);
            return false;
        }

        foreach (var g in gift)
        {
            bag.Add(g.Item, g.Count);
        }

        p.Milestones.Add(given);
        p.Milestones.Remove(due);
        _repo.SavePlayer(p);
        SendInventory(session);

        string words = FestivalThanksText(session, quartermaster, festivalKey, lineKey);
        string list = string.Join(", ", gift.Select(g => g.Count.ToString(CultureInfo.InvariantCulture) + "× "
            + LocalizedName(session.Locale, _content.GetItem(g.Item)?.NameKey, g.Item)));
        SpeakFestivalLine(session, quartermaster, list.Length > 0 ? words + "\n" + list : words, kind, gesture);
        _log.Info($"Festival present for '{name}' ({festivalKey}).");
        return true;
    }

    /// <summary>The quartermaster's words to a player: the thanks at the festival's start may be a cached AI re-wording in
    /// the quartermaster's voice (warmed when it was planned); every other line is authored.</summary>
    private string FestivalThanksText(PlayerSession session, ServerNpc? quartermaster, string festivalKey, string lineKey)
    {
        string name = session.State.Name ?? string.Empty;
        if (string.Equals(lineKey, "village.festival.thanks", StringComparison.Ordinal))
        {
            string settlement = festivalKey.Substring(festivalKey.IndexOf('|', StringComparison.Ordinal) + 1);
            string qmKey = quartermaster is not null ? NpcKeyForNpc(session, quartermaster) : NpcKey(SettlementLocationKey(settlement), "quartermaster");
            if (CachedAiLines(FestivalThanksCacheKey(settlement, qmKey, quartermaster?.IsRobot ?? false, session.Locale)) is { Length: > 0 } ai
                && FillAiLine(ai[0], session) is { Length: > 0 } worded)
            {
                return worded;
            }
        }

        return FillHero(Localize(session.Locale, lineKey), name);
    }

    /// <summary>
    /// #2604: the player talks to a quartermaster whose festival present waits for room in their bag — hand it over now
    /// (the talk is the hand-over, a direct reply). Returns true when it was handed over (the caller ends the talk there);
    /// still no room → false, the ordinary talk goes on and the present keeps waiting.
    /// </summary>
    private bool TryGiveOwedFestivalGift(PlayerSession session, ServerNpc npc)
    {
        if (npc.BaseId != 0 || npc.Settlement.Length == 0 || !string.Equals(npc.Job, "quartermaster", StringComparison.Ordinal)
            || _world is null)
        {
            return false;
        }

        string key = FestivalKey(_world.LocationId, npc.Settlement);
        if (!session.State.Milestones.Contains(FestivalGiftDueMilestonePrefix + key))
        {
            return false;
        }

        var gift = _content.VillageLife.Festival.Gift.Where(g => g is not null && g.Count > 0 && _content.GetItem(g.Item) is not null).ToList();
        if (gift.Count > 0 && !new MaterialPool(_content, session.State, _ship).CanFit(gift))
        {
            return false;
        }

        return GiveFestivalGift(session, npc, key, "village.festival.gift_later", NpcGreetingKinds.Addressed, NpcGestures.Nod);
    }

    /// <summary>A line of the quartermaster to one player: their bubble when they stand within
    /// <see cref="FestivalSpeakRange"/>, else a message (the words must arrive either way).</summary>
    private void SpeakFestivalLine(PlayerSession session, ServerNpc? quartermaster, string text, byte kind, byte gesture)
    {
        if (quartermaster is not null && _npcs.Contains(quartermaster)
            && WrapDistSq(quartermaster.Pos, session.State.Position) <= (double)FestivalSpeakRange * FestivalSpeakRange)
        {
            SayToHero(session, quartermaster, text, kind, gesture);
            return;
        }

        Send(session, new ServerMessage { Text = quartermaster is not null ? quartermaster.Name + ": " + text : text });
    }

    /// <summary>One bubble over an NPC for one player (a fresh greeting id).</summary>
    private void SayToHero(PlayerSession session, ServerNpc npc, string text, byte kind, byte gesture)
        => Send(session, new NpcGreeting
        {
            NpcId = npc.Id,
            Name = npc.Name,
            Role = npc.Role,
            Text = text,
            GreetingId = NextGreetingId(),
            Kind = kind,
            Gesture = gesture,
        });

    /// <summary>Fills <c>{player}</c> in an authored line (a stray brace keeps the line as written).</summary>
    private static string FillHero(string line, string heroName)
        => AiLineText.FillTokens(line, new Dictionary<string, string>(StringComparer.Ordinal) { [AiLineTokens.Player] = heroName })
           ?? line;

    // =====================================================================================================
    // AI (hosted worlds) — warmed when the festival is planned (#2604)
    // =====================================================================================================

    /// <summary><c>cheer|&lt;settle key&gt;|p200|&lt;locale&gt;|</c> — the village's cheers in one language (no player in it).</summary>
    private static string FestivalCheerCacheKey(string settlement, string locale)
        => AiLineCacheKey(NpcLinePurposes.Cheer, SettlementLocationKey(settlement), FestivalCrowdPersonaIndex, locale, string.Empty);

    /// <summary><c>festival_thanks|&lt;settle key&gt;|p&lt;voice&gt;|&lt;locale&gt;|</c> — the quartermaster's thanks.</summary>
    private static string FestivalThanksCacheKey(string settlement, string quartermasterKey, bool robot, string locale)
        => AiLineCacheKey(NpcLinePurposes.FestivalThanks, SettlementLocationKey(settlement),
            PersonaIndexFor(quartermasterKey, robot, "quartermaster"), locale, string.Empty);

    /// <summary>
    /// With AI personalisation on: asks for the village's cheers (<c>cheer</c>, as many lines as the authored ones) and
    /// the quartermaster's thanks (<c>festival_thanks</c>, one line) in the HERO's language, with the chain's steps as
    /// facts ("{player} brought the village iron ore" …) — so they are cached when the party starts. Fire and forget; the
    /// authored lines are used whenever nothing is cached.
    /// </summary>
    private void PrewarmFestivalLines(PlayerSession hero, SettlementInstance settlement, string placeKey, ServerNpc? quartermaster)
    {
        if (!AiLinesEnabled)
        {
            return;
        }

        string locale = hero.Locale;
        var facts = FestivalFacts(hero.State, placeKey);
        string theme = quartermaster?.Theme ?? SettlementTradeFor(settlement.Name);
        var cheerKeys = _content.VillageLife.Festival.Cheers;
        if (cheerKeys.Count > 0)
        {
            var authored = cheerKeys.Select(k => Localize(locale, k)).ToList();
            var tokens = AiLineText.TokensIn(authored);
            var request = new NpcLinesRequest
            {
                Purpose = NpcLinePurposes.Cheer,
                Language = locale,
                Place = "a small village on a frontier planet",
                Theme = theme,
                Situation = "the village festival for the player who helped the village with everything",
                TopicHint = "short happy cheers for the hero of the festival",
                LineCount = authored.Count,
                MaxChars = FestivalCheerMaxChars,
                Speakers = { new NpcLinesSpeaker { Role = "villagers", Persona = FestivalCrowdPersona } },
            };
            request.Facts.AddRange(facts);
            request.Tokens.AddRange(tokens);
            request.Authored.AddRange(authored);
            RequestAiLines(FestivalCheerCacheKey(settlement.Name, locale), request, AiLinePriority.Personal, tokens, tokens);
        }

        string qmKey = quartermaster is not null ? NpcKeyForNpc(hero, quartermaster) : NpcKey(placeKey, "quartermaster");
        bool robot = quartermaster?.IsRobot ?? false;
        string thanks = Localize(locale, "village.festival.thanks");
        var thanksTokens = AiLineText.TokensIn(new[] { thanks });
        var thanksRequest = new NpcLinesRequest
        {
            Purpose = NpcLinePurposes.FestivalThanks,
            Language = locale,
            Place = "a small village on a frontier planet",
            Theme = theme,
            Situation = "the village festival starts; the quartermaster thanks the player and hands over a present from everyone",
            TopicHint = "warm thanks for helping the village",
            LineCount = 1,
            MaxChars = FestivalThanksMaxChars,
            Speakers =
            {
                new NpcLinesSpeaker
                {
                    Role = "quartermaster",
                    Job = "quartermaster",
                    IsRobot = robot,
                    Persona = PersonaFor(qmKey, theme, robot, "quartermaster"),
                },
            },
        };
        thanksRequest.Facts.AddRange(facts);
        thanksRequest.Tokens.AddRange(thanksTokens);
        thanksRequest.Authored.Add(thanks);
        RequestAiLines(FestivalThanksCacheKey(settlement.Name, qmKey, robot, locale), thanksRequest, AiLinePriority.Personal,
            thanksTokens, thanksTokens);
    }

    /// <summary>The chain's steps the hero finished at this place, as plain English facts with the <c>{player}</c> token —
    /// no numbers (an AI line may not carry digits).</summary>
    private List<string> FestivalFacts(PlayerState p, string placeKey)
    {
        var facts = new List<string> { "{player} did every job the village asked for, so the whole village throws a festival" };
        var steps = p.Missions
            .Where(m => m.Status == MissionStatus.TurnedIn && string.Equals(m.ChainId, SettlementNeedsChainId, StringComparison.Ordinal)
                        && (string.IsNullOrEmpty(m.AcceptedFrom) || string.Equals(m.AcceptedFrom, placeKey, StringComparison.Ordinal)))
            .Select(m => GetMissionDef(m.MissionId))
            .Where(d => d is not null)
            .OrderBy(d => d!.Step);
        foreach (var def in steps)
        {
            if (def!.Objectives.Count > 0)
            {
                facts.Add(ChainStepFact(def.Objectives[0]));
            }
        }

        return facts;
    }

    /// <summary>One finished chain step as a plain English fact.</summary>
    private string ChainStepFact(MissionObjective o) => o.Type switch
    {
        MissionObjectiveType.Deliver or MissionObjectiveType.Collect =>
            "{player} brought the village " + EnglishItemName(o.Target),
        MissionObjectiveType.Build => string.Equals(o.Target, "light", StringComparison.Ordinal)
            ? "{player} built new lights for the village"
            : "{player} built something new for the village",
        MissionObjectiveType.Scan => o.Target.StartsWith("creature", StringComparison.Ordinal)
            ? "{player} studied the animals around the village"
            : "{player} scanned the land around the village",
        MissionObjectiveType.Defeat => "{player} chased the bandits out of their camp",
        MissionObjectiveType.Travel => "{player} explored another world for the village",
        _ => "{player} helped the village",
    };

    /// <summary>An item's English name in lower case ("iron ore"), the key when unknown.</summary>
    private string EnglishItemName(string itemKey)
        => (_content.GetItem(itemKey) is { } item ? Localize("en", item.NameKey) : itemKey.Replace('_', ' ')).ToLowerInvariant();

    // =====================================================================================================
    // Test seams (#2604)
    // =====================================================================================================

    /// <summary>Test seam: runs the festival pass of the active world now (start checks, cheers, tune, end) and plays the
    /// notes that are due.</summary>
    public void TickVillageFestivalsForTest()
    {
        _festivalTickAt.Remove(_world.LocationId);
        TickVillageFestivals(0.0);
    }

    /// <summary>Test seam: the festival of a settlement on the active world — its state ("pending"/"held"), hero and
    /// whether it is running now — or null when none was planned.</summary>
    public (string State, string HeroId, string HeroName, bool Running)? FestivalForTest(string settlement)
    {
        string key = FestivalKey(_world.LocationId, settlement);
        return _meta.Festivals.TryGetValue(key, out var r) ? (r.State, r.HeroId, r.HeroName, _festivalRuns.ContainsKey(key)) : null;
    }

    /// <summary>Test seam: how many festivals this save knows (all worlds).</summary>
    public int FestivalCountForTest => _meta.Festivals.Count;

    /// <summary>Test seam: an NPC at a running festival — its ring spot and the square — or null when it is not attending.</summary>
    public (Vector3f Spot, Vector3f Anchor)? FestivalPlaceForTest(int npcId)
        => _npcs.FirstOrDefault(n => n.Id == npcId) is { } npc && FestivalPlaceOf(npc) is { } place ? (place.Spot, place.Anchor) : null;

    /// <summary>Test seam: whether an NPC's routine phase is the festival's.</summary>
    public bool NpcInFestivalPhaseForTest(int npcId) => _npcs.FirstOrDefault(n => n.Id == npcId)?.Phase == NpcPhase.Festival;

    /// <summary>Test seam: the AI cache keys of a settlement's cheers and of its quartermaster's thanks in a language (the
    /// quartermaster's voice as the player would meet them).</summary>
    public (string Cheer, string Thanks) FestivalCacheKeysForTest(string playerId, string settlement, string locale)
    {
        var qm = FestivalQuartermaster(settlement);
        string qmKey = qm is not null && FindSessionByPlayerId(playerId) is { } s
            ? NpcKeyForNpc(s, qm)
            : NpcKey(SettlementLocationKey(settlement), "quartermaster");
        return (FestivalCheerCacheKey(settlement, locale), FestivalThanksCacheKey(settlement, qmKey, qm?.IsRobot ?? false, locale));
    }
}
