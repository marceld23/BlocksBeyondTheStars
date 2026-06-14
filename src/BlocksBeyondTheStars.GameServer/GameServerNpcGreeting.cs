using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Item 15: contextual NPC greeting lines. When a player opens an interaction with a settlement/station NPC
/// (a vendor's market stall, a quartermaster's mission board) the server sends a short greeting to show as a
/// speech bubble. With an LLM backend enabled (<see cref="AiLevel"/> != Off) the line is generated off the game
/// thread from live context (the NPC, the settlement, the player's relationship + language) and cached; when AI
/// is off/unreachable — and always as the instant first response — the client renders a localized static
/// fallback. The server stays authoritative and never blocks: the blocking HTTP call runs on a Task whose result
/// is drained into the outbox each tick.
/// </summary>
public sealed partial class GameServer
{
    private const float NpcGreetRange = 5f;          // just beyond the E-interaction reach; an anti-spam gate
    private const double GreetCooldownSeconds = 25.0; // don't re-emit the same NPC's line on every re-open

    /// <summary>Greetings produced off-thread, waiting to be sent on the next tick (connection id + message).</summary>
    private readonly ConcurrentQueue<(int ConnectionId, NpcGreeting Message)> _greetingOutbox = new();

    /// <summary>Cache of generated LLM lines, keyed by npcKey|locale|relationship-tier, so a repeat visit (and
    /// other players at the same standing + language) reuse a line instead of re-calling the backend.</summary>
    private readonly ConcurrentDictionary<string, string> _greetingCache = new();

    /// <summary>Cache keys with an LLM request already running, so concurrent opens don't fan out duplicates.</summary>
    private readonly ConcurrentDictionary<string, byte> _greetingInFlight = new();

    /// <summary>Per (player|npc) cooldown so a re-opened panel doesn't re-trigger the bubble. Game-thread only.</summary>
    private readonly Dictionary<string, double> _lastGreetAt = new();

    /// <summary>Normalizes a client-sent locale to one we support ("en"/"de"); anything else → English.</summary>
    private static string NormalizeLocale(string? locale)
        => string.Equals(locale, "de", System.StringComparison.OrdinalIgnoreCase) ? "de" : "en";

    /// <summary>Client asked for an NPC's greeting on interaction: pick the nearest matching, in-reach NPC and
    /// emit its line (item 15).</summary>
    private void HandleNpcGreet(PlayerSession session, NpcGreetIntent intent)
    {
        string role = intent.Role == "quartermaster" ? "quartermaster" : "vendor";
        if (NearestNpc(session.State, role) is { } npc
            && WrapDistSq(session.State.Position, npc.Pos) <= NpcGreetRange * NpcGreetRange)
        {
            EmitGreeting(session, npc);
        }
    }

    /// <summary>When a player opens a mission board, greet the nearest quartermaster in reach (item 15).
    /// Server-driven (no extra client intent) — called from <c>SendMissionList</c>; a no-op aboard ship / away
    /// from any settlement where no quartermaster is near.</summary>
    private void GreetBoardQuartermaster(PlayerSession session)
    {
        if (NearestNpc(session.State, "quartermaster") is { } qm
            && WrapDistSq(session.State.Position, qm.Pos) <= NpcGreetRange * NpcGreetRange)
        {
            EmitGreeting(session, qm);
        }
    }

    /// <summary>Builds the LLM context + cache key for greeting an NPC from live game state (the player's
    /// relationship, language, the settlement/station). Pure read of game state — call on the game thread.</summary>
    private (NpcLineRequest Req, string NpcKey, string CacheKey) NpcContext(PlayerSession session, ServerNpc npc)
    {
        var player = session.State;

        // The stable NPC key — settlement board vs. boarded-station board — matches the relationship memory key.
        string locationKey = NearSpaceStationVendor(player) && _boardedStation.TryGetValue(player.PlayerId, out var st)
            ? StationLocationKey(st)
            : SettlementLocationKey();
        string npcKey = NpcKey(locationKey, npc.Role);

        var rel = player.NpcMemory.TryGetValue(npcKey, out var r) ? r : null;
        int relValue = rel?.Value ?? 0;
        int interactions = rel?.Log.Count ?? 0;
        string cacheKey = $"{npcKey}|{session.Locale}|{RelationshipTier(relValue)}";

        var req = new NpcLineRequest
        {
            NpcName = npc.Name,
            Role = npc.Role,
            Theme = npc.Theme,
            IsRobot = npc.IsRobot,
            Settlement = _settlementName,
            PlayerName = player.Name,
            Relationship = relValue,
            PastInteractions = interactions,
            Language = session.Locale,
            Persona = PersonaFor(npcKey, npc.Theme, npc.IsRobot),       // L2: stable per-NPC voice
            RecentEvents = RecentEventsLine(rel),                        // L2: what they remember
        };
        return (req, npcKey, cacheKey);
    }

    // L2: a small pool of persona voices, assigned deterministically per NPC (stable across visits)
    // and flavoured by kind — androids get precise/literal voices, organics the warmer ones.
    private static readonly string[] PersonaPoolOrganic =
    {
        "gruff veteran who speaks in short sentences but means well",
        "cheerful chatterbox who loves local gossip",
        "weary pragmatist counting the days to retirement",
        "superstitious frontier soul who reads omens into everything",
        "proud craftsman who respects good gear and hates waste",
        "dry humorist who deadpans even good news",
    };

    private static readonly string[] PersonaPoolRobot =
    {
        "overly precise android that quantifies everything it says",
        "politely literal android still learning small talk",
        "old service unit with glitchy, antiquated courtesy phrases",
    };

    /// <summary>L2: deterministic persona descriptor for an NPC — same NPC, same voice, every visit.</summary>
    private static string PersonaFor(string npcKey, string theme, bool isRobot)
    {
        var pool = isRobot ? PersonaPoolRobot : PersonaPoolOrganic;
        ulong h = (ulong)BlocksBeyondTheStars.WorldGeneration.WorldGenerator.StableHash("persona:" + npcKey);
        string persona = pool[(int)(h % (ulong)pool.Length)];
        return string.IsNullOrEmpty(theme) ? persona : $"{persona}; lives among {theme}";
    }

    /// <summary>L2: the NPC's recent memory of this player as a compact line ("trade, mission accepted").</summary>
    private static string RecentEventsLine(BlocksBeyondTheStars.Shared.State.NpcRelationship? rel)
    {
        if (rel is null || rel.Log.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        int from = System.Math.Max(0, rel.Log.Count - 5);
        for (int i = from; i < rel.Log.Count; i++)
        {
            parts.Add(rel.Log[i].Kind switch
            {
                BlocksBeyondTheStars.Shared.State.NpcInteractionKind.Trade => "a trade",
                BlocksBeyondTheStars.Shared.State.NpcInteractionKind.MissionAccepted => "took a mission",
                _ => "a chat",
            });
        }

        return string.Join(", ", parts);
    }

    /// <summary>Sends a greeting for an NPC to a player: a cached/static line immediately, plus (when AI is on and
    /// nothing is cached yet) an LLM line generated off-thread that replaces it when ready.</summary>
    private void EmitGreeting(PlayerSession session, ServerNpc npc)
    {
        var (req, npcKey, cacheKey) = NpcContext(session, npc);

        string coolKey = session.State.PlayerId + "|" + npcKey;
        if (_lastGreetAt.TryGetValue(coolKey, out var last) && _uptime - last < GreetCooldownSeconds)
        {
            return; // greeted this NPC very recently — don't re-spam on a panel re-open
        }

        _lastGreetAt[coolKey] = _uptime;

        // 1) Instant response: a cached LLM line if we have one, else empty → the client shows its localized
        //    fallback. Either way the player always gets a greeting, with or without an AI backend.
        string immediate = _greetingCache.TryGetValue(cacheKey, out var cached) ? cached : string.Empty;

        // P7: with no LLM backend, let settlement/station NPCs occasionally speak a story flavour line — gated
        // by the world's knowledge level + tags + the NPC's role — so villages react to the unfolding story.
        if (immediate.Length == 0 && _config.AiLevel == AiLevel.Off)
        {
            immediate = PickStoryFlavourText(session, npc);
        }

        Send(session, new NpcGreeting { NpcId = npc.Id, Name = npc.Name, Role = npc.Role, Text = immediate });

        // 2) If AI is enabled and nothing is cached, generate a line off the game thread and push it when ready.
        if (_config.AiLevel == AiLevel.Off || immediate.Length > 0)
        {
            return;
        }

        if (!_greetingInFlight.TryAdd(cacheKey, 1))
        {
            return; // a request for this exact line is already running
        }

        int connId = session.ConnectionId;
        int npcId = npc.Id;
        string npcName = npc.Name;
        string role = npc.Role;

        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var line = _ai.GenerateNpcLine(req);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    _greetingCache[cacheKey] = line!;
                    _greetingOutbox.Enqueue((connId, new NpcGreeting { NpcId = npcId, Name = npcName, Role = role, Text = line! }));
                }
            }
            catch
            {
                // best-effort — the player already has the static fallback; never surface AI failures
            }
            finally
            {
                _greetingInFlight.TryRemove(cacheKey, out _);
            }
        });
    }

    // ---------------- P7: story flavour lines ----------------

    private readonly System.Random _flavourRng = new(0x5F3C7A);
    private readonly Dictionary<GameLocale, Localizer> _localizers = new();

    /// <summary>P7: picks an eligible story flavour line for this NPC — filtered by the world's knowledge level,
    /// the world's tags and the NPC's role — and localizes it for the player. Empty when no story is active or
    /// nothing is eligible (the client then shows its static fallback).</summary>
    private string PickStoryFlavourText(PlayerSession session, ServerNpc npc)
    {
        if (_story is null || _story.FlavourLines.Count == 0)
        {
            return string.Empty;
        }

        int knowledge = WorldKnowledgeLevel();
        var tags = WorldStoryTags();

        var eligible = new List<FlavourLine>();
        int totalWeight = 0;
        foreach (var fl in _story.FlavourLines)
        {
            if (fl.MinKnowledge > knowledge)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(fl.Role) && !string.Equals(fl.Role, npc.Role, System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (fl.WorldTags.Count > 0 && !fl.WorldTags.Any(t => tags.Contains(t)))
            {
                continue;
            }

            eligible.Add(fl);
            totalWeight += System.Math.Max(1, fl.Weight);
        }

        if (eligible.Count == 0)
        {
            return string.Empty;
        }

        int roll = _flavourRng.Next(totalWeight);
        var pick = eligible[eligible.Count - 1];
        foreach (var fl in eligible)
        {
            roll -= System.Math.Max(1, fl.Weight);
            if (roll < 0)
            {
                pick = fl;
                break;
            }
        }

        return Localize(session.Locale, pick.TextKey);
    }

    /// <summary>The active world's story tags (today: its planet-type key) for flavour-line filtering.</summary>
    private HashSet<string> WorldStoryTags()
    {
        var tags = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var key = _world?.Planet?.Key;
        if (!string.IsNullOrEmpty(key))
        {
            tags.Add(key);
        }

        return tags;
    }

    /// <summary>Server-side localize (cached per locale) — flavour lines are sent as resolved text, like the LLM
    /// greeting lines, not as keys. <paramref name="localeCode"/> is the player's locale string ("en"/"de").</summary>
    private string Localize(string localeCode, string key)
    {
        GameLocaleExtensions.TryParse(localeCode, out var locale); // defaults to English on an unknown code
        if (!_localizers.TryGetValue(locale, out var loc))
        {
            loc = _content.CreateLocalizer(locale);
            _localizers[locale] = loc;
        }

        return loc.Get(key);
    }

    /// <summary>Drains LLM greetings produced off-thread and sends each to its (still-connected) player. Called
    /// once per server tick from <c>Tick</c>.</summary>
    private void TickGreetings()
    {
        while (_greetingOutbox.TryDequeue(out var pending))
        {
            if (_sessions.TryGetValue(pending.ConnectionId, out var session) && session.Joined)
            {
                Send(session, pending.Message);
            }
        }
    }

    /// <summary>A coarse relationship bucket — used both as a cache key and as LLM tone context.</summary>
    private static string RelationshipTier(int value) => value switch
    {
        <= -20 => "hostile",
        < 10 => "stranger",
        < 40 => "known",
        _ => "trusted",
    };

    /// <summary>The NPC greeting cache size (test/inspection).</summary>
    public int GreetingCacheCount => _greetingCache.Count;

    /// <summary>Test seam: synchronously resolves the greeting line the nearest in-reach NPC of <paramref
    /// name="role"/> would show this player — the same context + provider + cache path <see cref="EmitGreeting"/>
    /// uses off-thread. Returns null if no such NPC is in reach; an empty string means "no AI line" (the client
    /// would render its localized static fallback); otherwise the (now-cached) generated line.</summary>
    public string? GreetingLineForTest(string playerId, string role)
    {
        if (FindSessionByPlayerId(playerId) is not { } session)
        {
            return null;
        }

        if (NearestNpc(session.State, role) is not { } npc
            || WrapDistSq(session.State.Position, npc.Pos) > NpcGreetRange * NpcGreetRange)
        {
            return null;
        }

        var (req, _, cacheKey) = NpcContext(session, npc);
        if (_greetingCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        if (_config.AiLevel == AiLevel.Off)
        {
            return string.Empty; // client shows its static localized fallback
        }

        var line = _ai.GenerateNpcLine(req);
        if (!string.IsNullOrWhiteSpace(line))
        {
            _greetingCache[cacheKey] = line!;
            return line;
        }

        return string.Empty;
    }
}
