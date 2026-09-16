// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Moderation;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// NPC professions (Justus' ideas, 2026-09-15): doctor, grocer, arms dealer, sage, animal tamer, blockfarmer, streamer and
/// reporter (<see cref="NpcProfessions"/>). They staff a post marker — in a settlement building of their own function, in an
/// author's structure or station template, or at the post block a player builds at home or on their station. A trading
/// profession is a <c>vendor</c> with its own job and market theme, so the market, the trade-or-talk question and the
/// relationship memory work unchanged; everything the profession adds keys off <see cref="ServerNpc.Job"/>.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Dresses an NPC for its profession: job, nameplate key, market theme (trading professions) and what it
    /// carries. The role must already match (<see cref="MakeNpc"/> was called with <see cref="NpcProfession.Role"/>).</summary>
    private static void ApplyProfession(ServerNpc npc, NpcProfession profession)
    {
        npc.Role = profession.Role;
        npc.Job = profession.Job;
        npc.NameKey = profession.NameKey;
        npc.Held = profession.Held;
        if (profession.Trades)
        {
            npc.Theme = profession.Theme;
        }
    }

    /// <summary>A profession that works standing at its post (the tight work leash) — not the blockfarmer, who is out
    /// in the field.</summary>
    private static bool IsStandingProfession(string job) => NpcProfessions.ByJob(job) is { WorksOutside: false };

    /// <summary>
    /// One resident per profession post of a settlement, spawned after its regular residents with a generator seeded
    /// from the settlement and the post's index — never the shared world generator, so the names, looks and beds of
    /// every other resident stay exactly what they were. Each takes the free bed nearest its post (the building's own
    /// back room or upstairs flat), or none.
    /// </summary>
    private void SpawnProfessionResidents(SettlementInstance settlement, List<Vector3i> freeBeds, List<Vector3i> seats, ref int seatCursor)
    {
        string settlementTheme = SettlementTradeFor(settlement.Name);
        int index = 0;
        foreach (var (type, pos) in settlement.Markers)
        {
            if (NpcProfessions.ByMarker(type) is not { } profession)
            {
                continue;
            }

            var rng = new System.Random(unchecked((int)(_meta.Seed ^ WorldGenerator.StableHash("profession:" + settlement.Name + ":" + index++))));
            var standing = new Vector3f(pos.X, (float)System.Math.Floor(System.Math.Max(settlement.Min.Y + 1f, pos.Y)), pos.Z);
            var npc = MakeNpc(profession.Role, profession.Trades ? profession.Theme : settlementTheme, robotic: false, standing, rng);
            npc.Settlement = settlement.Name;
            ApplyProfession(npc, profession);
            npc.Work = profession.WorksOutside ? OutsideSettlementWorkSpot(settlement, standing) : standing; // the blockfarmer's quarry
            npc.HasWork = true;
            npc.Bed = TakeNearestBed(freeBeds, standing);
            npc.FurnitureScanned = true;
            if (seats.Count > 0)
            {
                npc.Seat = seats[seatCursor % seats.Count];
                seatCursor++;
            }
            else if (npc.Bed is { } own)
            {
                npc.Seat = NearestLayoutSeat(settlement, own);
            }

            npc.RoutineEnabled = true;
            _npcs.Add(npc);
        }
    }

    /// <summary>How far past a settlement's edge (or a base's wall reach) the blockfarmer quarries.</summary>
    private const int QuarryDistance = 10;

    /// <summary>The blockfarmer's day spot: <see cref="QuarryDistance"/> blocks beyond the settlement edge nearest their
    /// post, on the generated ground there (spawn time — its chunks need not be loaded). A void world keeps the post.</summary>
    private Vector3f OutsideSettlementWorkSpot(SettlementInstance s, Vector3f post)
    {
        if (_world.Planet is not { } planet || planet.Void)
        {
            return post;
        }

        int circ = _world.Circumference;
        int px = (int)System.Math.Floor(post.X), pz = (int)System.Math.Floor(post.Z);
        int toMinX = System.Math.Abs(WorldConstants.WrapDeltaX(px - s.Min.X, circ));
        int toMaxX = System.Math.Abs(WorldConstants.WrapDeltaX(s.Max.X - px, circ));
        int toMinZ = System.Math.Abs(pz - s.Min.Z);
        int toMaxZ = System.Math.Abs(s.Max.Z - pz);
        int nearest = System.Math.Min(System.Math.Min(toMinX, toMaxX), System.Math.Min(toMinZ, toMaxZ));
        int x = px, z = pz;
        if (nearest == toMinX)
        {
            x = s.Min.X - QuarryDistance;
        }
        else if (nearest == toMaxX)
        {
            x = s.Max.X + QuarryDistance;
        }
        else if (nearest == toMinZ)
        {
            z = s.Min.Z - QuarryDistance;
        }
        else
        {
            z = s.Max.Z + QuarryDistance;
        }

        x = WorldConstants.WrapX(x, circ);
        return new Vector3f(x + 0.5f, _generator.SurfaceHeight(planet, x, z) + 1, z + 0.5f);
    }

    /// <summary>A base blockfarmer's day spot: beyond the base's wall reach (east of its core), on standable ground there
    /// when the chunk is loaded, else on the generated ground. Null off a planet.</summary>
    private Vector3f? OutsideBaseWorkSpot(ServerNpc npc)
    {
        if (_world.Planet is not { } planet || planet.Void || _bases.FirstOrDefault(x => x.Id == npc.BaseId) is not { } b)
        {
            return null;
        }

        int circ = _world.Circumference;
        int x = WorldConstants.WrapX(b.Cell.X + BaseWallReach(b) + QuarryDistance, circ);
        int z = b.Cell.Z;
        int y = _generator.SurfaceHeight(planet, x, z) + 1;
        return StandableSpot(x, y, z) ?? new Vector3f(x + 0.5f, y, z + 0.5f);
    }

    /// <summary>The in-game day index the rotating market offers go by (<see cref="RecipeDefinition.MarketRotation"/>) —
    /// the same system clock the client receives in <c>WorldEnvironment.SystemTimeDays</c>.</summary>
    private long MarketDay => (long)System.Math.Floor(_systemTimeDays);

    /// <summary>The vendor NPC (classic or trading profession) the player trades with right now: the nearest one within
    /// <see cref="VendorThemeReach"/>, or null.</summary>
    private ServerNpc? TradingVendorAt(Shared.State.PlayerState player)
        => NearestNpc(player, "vendor") is { } v && WrapDistSq(player.Position, v.Pos) <= VendorThemeReach * VendorThemeReach ? v : null;

    /// <summary>Whether two positions share one closed room: a flood from the first one's head cell that stays inside
    /// walls, floor, roof and doors (the base index's room rule) and reaches the second one's feet or head cell.</summary>
    private bool InSameClosedRoom(Vector3f a, Vector3f b)
    {
        var start = new Vector3i((int)System.Math.Floor(a.X), (int)System.Math.Floor(a.Y) + 1, (int)System.Math.Floor(a.Z));
        var goalFeet = new Vector3i((int)System.Math.Floor(b.X), (int)System.Math.Floor(b.Y), (int)System.Math.Floor(b.Z));
        var goalHead = new Vector3i(goalFeet.X, goalFeet.Y + 1, goalFeet.Z);
        var doors = DoorCellsWhere(_ => true);
        int circ = _world.Circumference;
        var seen = new HashSet<Vector3i> { start };
        var frontier = new Queue<Vector3i>();
        frontier.Enqueue(start);
        bool reached = start.Equals(goalFeet) || start.Equals(goalHead);
        while (frontier.Count > 0)
        {
            var c = frontier.Dequeue();
            for (int i = 0; i < 6; i++)
            {
                var n = i switch
                {
                    0 => new Vector3i(c.X + 1, c.Y, c.Z),
                    1 => new Vector3i(c.X - 1, c.Y, c.Z),
                    2 => new Vector3i(c.X, c.Y + 1, c.Z),
                    3 => new Vector3i(c.X, c.Y - 1, c.Z),
                    4 => new Vector3i(c.X, c.Y, c.Z + 1),
                    _ => new Vector3i(c.X, c.Y, c.Z - 1),
                };

                if (seen.Contains(n) || doors.Contains(WorldConstants.CanonicalBlock(n, circ))
                    || IsCollidingBlock(_world.GetBlockIfLoaded(n), fluidsPass: false, foliagePasses: false))
                {
                    continue;
                }

                if (System.Math.Abs(n.X - start.X) > ClosedRoomReach || System.Math.Abs(n.Y - start.Y) > ClosedRoomReach
                    || System.Math.Abs(n.Z - start.Z) > ClosedRoomReach || seen.Count >= ClosedRoomCellBudget)
                {
                    return false; // out in the open: no shop around the customer
                }

                reached |= n.Equals(goalFeet) || n.Equals(goalHead);
                seen.Add(n);
                frontier.Enqueue(n);
            }
        }

        return reached;
    }

    // ---------------------------------------------------------------------------------------------
    // The streamer, the reporter and the tamer's pet (tick + dialogue consequences).
    // ---------------------------------------------------------------------------------------------

    /// <summary>A live creature owned by an NPC (the tamer's pet) carries this owner prefix + the NPC id — never a player
    /// id, so the player companion code (roster, payoff, reconcile) leaves it alone.</summary>
    internal const string NpcPetOwnerPrefix = "npc:";

    /// <summary>How close a player walks by before the streamer calls out.</summary>
    private const float StreamerAskRange = 8f;

    /// <summary>The player said "never ask me again" to a streamer.</summary>
    internal const string StreamerNeverMilestone = "streamer:never";

    /// <summary>The Safe-chat interview answers (locale keys, resolved per reader). Free text is refused in Safe mode.</summary>
    internal static readonly string[] InterviewPresets =
    {
        "news.preset.explored", "news.preset.built", "news.preset.guardians", "news.preset.tamed",
    };

    /// <summary>Set by a dialogue consequence while a choice is applied (single-threaded tick): the client action and any
    /// extra text the closing line carries.</summary>
    private string _dialogAction = string.Empty;
    private string _dialogExtraText = string.Empty;

    private readonly Dictionary<string, double> _professionTickAt = new(System.StringComparer.Ordinal);

    /// <summary>Once a second per world: streamers call out to passers-by, tamers keep their pet at their side.</summary>
    private void TickProfessions(double dt)
    {
        string world = _world.LocationId;
        if (_professionTickAt.TryGetValue(world, out double due) && _uptime < due)
        {
            return;
        }

        _professionTickAt[world] = _uptime + 1.0;
        TickStreamers();
        SyncTamerPets();
    }

    /// <summary>A streamer asks each player walking by for a photo — at most once per in-game day per player and place,
    /// never after "never again", never at night (in bed).</summary>
    private void TickStreamers()
    {
        long day = MarketDay;
        foreach (var npc in _npcs)
        {
            if (npc.Job != "streamer" || npc.Pose != 0)
            {
                continue;
            }

            foreach (var s in JoinedInActiveWorld())
            {
                var p = s.State;
                if (s.Spectating || InSpace(p.PlayerId) || p.Milestones.Contains(StreamerNeverMilestone)
                    || WrapDistSq(p.Position, npc.Pos) > StreamerAskRange * StreamerAskRange)
                {
                    continue;
                }

                if (!s.StreamerAskedToday.Add(day + "|" + NewsPlaceKey(npc)))
                {
                    continue;
                }

                Send(s, new NpcGreeting { NpcId = npc.Id, Name = npc.Name, Role = npc.Role, Text = Localize(s.Locale, "npc.streamer.ask") });
            }
        }
    }

    /// <summary>"Yes, a photo!": the streamer strikes a pose (nameplate), the client takes the picture.</summary>
    private void StartStreamerPhoto(PlayerSession session, ServerNpc npc)
    {
        _dialogAction = "photo";
        npc.ActivityKey = "npc.activity.posing";
        npc.PhaseCheckedAt = _uptime + 6.0 - RoutineCheckInterval; // hold the pose a few seconds before the routine resumes
        BroadcastNpcs();
    }

    /// <summary>The place a reporter's news (and a streamer's daily question) belongs to: the settlement, the base, else the
    /// world (a station).</summary>
    private string NewsPlaceKey(ServerNpc npc)
        => npc.Settlement.Length > 0 ? "s:" + npc.Settlement : npc.BaseId > 0 ? "b:" + npc.BaseId : "w:" + _world.LocationId;

    /// <summary>The latest articles of the reporter's place, newest first, for the "read the news" answer.</summary>
    private string NewsDigest(PlayerSession session, ServerNpc npc)
    {
        if (!_meta.News.TryGetValue(NewsPlaceKey(npc), out var list) || list.Count == 0)
        {
            return Localize(session.Locale, "dlg.reporter.no_news");
        }

        var sb = new System.Text.StringBuilder();
        foreach (var article in Enumerable.Reverse(list).Take(3))
        {
            string text = article.Text.StartsWith("@", System.StringComparison.Ordinal) ? Localize(session.Locale, article.Text.Substring(1)) : article.Text;
            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            sb.Append("• ").Append(article.PlayerName).Append(": ").Append(text);
        }

        return sb.ToString();
    }

    /// <summary>The player's interview answer (2026-09): accepted only right after that reporter asked, trimmed, screened
    /// like chat (a refused line is not stored; Safe chat mode takes only the preset answers), then kept as the place's
    /// news (the latest <see cref="NewsArticle.MaxPerPlace"/>).</summary>
    private void HandleInterviewAnswer(PlayerSession session, InterviewAnswerIntent intent)
    {
        if (session.PendingInterviewNpcId == 0 || session.PendingInterviewNpcId != intent.NpcId)
        {
            return;
        }

        session.PendingInterviewNpcId = 0;
        var npc = _npcs.FirstOrDefault(n => n.Id == intent.NpcId && n.Job == "reporter");
        if (npc is null || npc.Pos.DistanceSquared(session.State.Position) > NpcTalkRange * NpcTalkRange * 4)
        {
            return;
        }

        string raw = StripControlChars(intent.Text).Trim();
        string? text;
        if (raw.StartsWith("@", System.StringComparison.Ordinal))
        {
            text = System.Array.IndexOf(InterviewPresets, raw.Substring(1)) >= 0 ? raw : null; // a preset answer, by key
        }
        else if (EffectiveChatMode == BlocksBeyondTheStars.Shared.Configuration.ChatMode.Safe)
        {
            text = null; // Safe chat: only the preset answers
        }
        else
        {
            text = ScreenInterviewText(session, raw.Length > NewsArticle.MaxTextLength ? raw.Substring(0, NewsArticle.MaxTextLength) : raw);
        }

        if (string.IsNullOrEmpty(text))
        {
            Send(session, new NpcDialogState { NpcId = npc.Id, Name = npc.Name, Text = Localize(session.Locale, "dlg.reporter.r_refused"), End = true });
            return;
        }

        string key = NewsPlaceKey(npc);
        if (!_meta.News.TryGetValue(key, out var list))
        {
            _meta.News[key] = list = new List<NewsArticle>();
        }

        list.Add(new NewsArticle { PlayerName = session.State.Name, Text = text!, Day = MarketDay });
        while (list.Count > NewsArticle.MaxPerPlace)
        {
            list.RemoveAt(0);
        }

        _repo.SaveMetadata(_meta);
        Send(session, new NpcDialogState { NpcId = npc.Id, Name = npc.Name, Text = Localize(session.Locale, "dlg.reporter.r_thanks"), End = true });
    }

    /// <summary>Screens a free-text answer like a chat line: a blocked one is refused (null), a masked one kept masked.</summary>
    private string? ScreenInterviewText(PlayerSession session, string text)
    {
        if (text.Length == 0)
        {
            return null;
        }

        var result = ChatContentScreen.Screen(text, EffectiveChatMode);
        switch (result.Verdict)
        {
            case ChatVerdict.Block:
                _log.Info($"Interview filter: refused an answer from '{session.State.Name ?? "?"}' ({(result.Pii ? "personal data: " : "term: ")}{result.MatchedTerm}).");
                return null;
            case ChatVerdict.Mask:
                return result.Text;
            default:
                return text;
        }
    }

    /// <summary>Keeps one tame animal beside every animal tamer on a planet (none on stations): a passive land species of
    /// the world, picked from the tamer's name, invulnerable, owned by the NPC. A pet whose tamer is gone disappears.</summary>
    private void SyncTamerPets()
    {
        bool changed = false;
        for (int i = _creatures.Count - 1; i >= 0; i--)
        {
            var c = _creatures[i];
            if (c.OwnerId.StartsWith(NpcPetOwnerPrefix, System.StringComparison.Ordinal)
                && (!int.TryParse(c.OwnerId.Substring(NpcPetOwnerPrefix.Length), out int ownerNpc)
                    || !_npcs.Any(n => n.Id == ownerNpc && n.Job == "tamer")))
            {
                _creatures.RemoveAt(i);
                changed = true;
            }
        }

        if (_world.Planet?.Void == true)
        {
            if (changed)
            {
                BroadcastCreatures();
            }

            return; // a station has no animals
        }

        var candidates = _speciesRoster
            .Where(sp => sp.Habitat == CreatureHabitat.Land && sp.Temperament is CreatureTemperament.Passive or CreatureTemperament.Skittish)
            .ToList();
        foreach (var npc in _npcs)
        {
            if (npc.Job != "tamer" || candidates.Count == 0)
            {
                continue;
            }

            string owner = NpcPetOwnerPrefix + npc.Id;
            if (_creatures.Any(c => c.OwnerId == owner))
            {
                continue;
            }

            var sp = candidates[(int)((uint)WorldGenerator.StableHash("tamer-pet:" + npc.Name) % (uint)candidates.Count)];
            _creatures.Add(new CombatEntity
            {
                Id = NextEntityId(),
                Kind = CombatEntityKind.Creature,
                SpeciesId = sp.Id,
                Hostile = false,
                Hull = sp.MaxHealth,
                HullMax = sp.MaxHealth,
                Position = CompanionSpotNear(sp, owner, npc.Pos),
                DamagePerSecond = 0f,
                SizeScale = 0.8f,
                OwnerId = owner,
                CompanionId = owner,
            });
            changed = true;
        }

        if (changed)
        {
            BroadcastCreatures();
        }
    }

    /// <summary>The NPC a tamer's pet follows, or null when the creature is no NPC's pet.</summary>
    private ServerNpc? NpcPetOwner(CombatEntity c)
        => c.OwnerId.StartsWith(NpcPetOwnerPrefix, System.StringComparison.Ordinal)
           && int.TryParse(c.OwnerId.Substring(NpcPetOwnerPrefix.Length), out int id)
            ? _npcs.FirstOrDefault(n => n.Id == id)
            : null;

    /// <summary>Test seam: the tame animals walking with NPCs on the active world (owner NPC id, species).</summary>
    public IReadOnlyList<(int NpcId, string SpeciesId)> NpcPetsForTest
        => _creatures.Where(c => c.OwnerId.StartsWith(NpcPetOwnerPrefix, System.StringComparison.Ordinal))
            .Select(c => (int.Parse(c.OwnerId.Substring(NpcPetOwnerPrefix.Length), System.Globalization.CultureInfo.InvariantCulture), c.SpeciesId)).ToList();

    /// <summary>Test seam: a player's interview answer to a reporter (the network intent's path).</summary>
    public void InterviewAnswerForTest(string playerId, int npcId, string text)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleInterviewAnswer(s, new InterviewAnswerIntent { NpcId = npcId, Text = text });
        }
    }

    /// <summary>Test seam: every stored news article (place key, author, text).</summary>
    public IReadOnlyList<(string Place, string PlayerName, string Text)> NewsForTest
        => _meta.News.SelectMany(kv => kv.Value.Select(a => (kv.Key, a.PlayerName, a.Text))).ToList();

    /// <summary>Test seam: an NPC's position, work spot and activity key.</summary>
    public (Vector3f Pos, Vector3f Work, string ActivityKey)? NpcStateForTest(int npcId)
        => _npcs.FirstOrDefault(n => n.Id == npcId) is { } n ? (n.Pos, n.Work, n.ActivityKey) : null;

    /// <summary>Test seam: runs the professions tick now (streamer calls, pets).</summary>
    public void TickProfessionsForTest()
    {
        _professionTickAt.Clear();
        TickProfessions(1.0);
    }

    /// <summary>Test seam: whether two positions share one closed room on the active world.</summary>
    public bool InSameClosedRoomForTest(Vector3f a, Vector3f b) => InSameClosedRoom(a, b);

    /// <summary>Test seam: the in-game day index the rotating market offers use.</summary>
    public long MarketDayForTest => MarketDay;

    /// <summary>Test seam: every NPC's job, role, theme, nameplate key and held item on the active world.</summary>
    public IReadOnlyList<(string Job, string Role, string Theme, string NameKey, string Held, string Settlement)> NpcJobsForTest
        => _npcs.Select(n => (n.Job, n.Role, n.Theme, n.NameKey, n.Held, n.Settlement)).ToList();
}
