// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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

    /// <summary>Most greeting cache keys held (#2590 — FIFO beyond, so a long-running world can't grow it forever).</summary>
    private const int GreetingCacheCapacity = 2048;

    /// <summary>Cache of generated LLM lines, keyed by npcKey|locale|relationship-tier, so a repeat visit (and
    /// other players at the same standing + language) reuse a line instead of re-calling the backend. #2590: up to
    /// three variants per key, picked at random, and the lines hold the player only as the <c>{player}</c> token —
    /// the receiver's own name is filled in at send time (<see cref="FillPlayerToken"/>).</summary>
    private readonly AiVariantCache<string> _greetingCache =
        new(GreetingCacheCapacity, AiLineMaxVariants, (a, b) => string.Equals(a, b, System.StringComparison.Ordinal), seed: 0x6E37);

    /// <summary>Cache keys with an LLM request already running, so concurrent opens don't fan out duplicates.</summary>
    private readonly ConcurrentDictionary<string, byte> _greetingInFlight = new();

    /// <summary>Per (player|npc) cooldown so a re-opened panel doesn't re-trigger the bubble. Game-thread only.</summary>
    private readonly Dictionary<string, double> _lastGreetAt = new();

    /// <summary>#2593: the last <see cref="NpcGreeting.GreetingId"/> handed out (see <see cref="NextGreetingId"/>).</summary>
    private int _greetingIdCounter;

    /// <summary>
    /// #2593: a fresh id for a NEW speech bubble (<see cref="NpcGreeting.GreetingId"/>) — increasing across this server, never
    /// 0 (0 means "an older server" to the client). Stamp it on the first message of a bubble and reuse the SAME id on its
    /// AI upgrade: the client replaces the text of a known id without playing the voice or the gesture again, and plays
    /// both once for a new id. Thread-safe (Interlocked), so an off-thread AI task may also call it.
    /// </summary>
    internal int NextGreetingId()
    {
        int id = System.Threading.Interlocked.Increment(ref _greetingIdCounter);
        if (id <= 0)
        {
            // Wrapped after ~2 billion bubbles — start over at 1 (the client treats a smaller id after a pause as new).
            System.Threading.Interlocked.CompareExchange(ref _greetingIdCounter, 1, id);
            id = 1;
        }

        return id;
    }

    /// <summary>Normalizes a client-sent locale to a supported locale code; anything unknown → English.
    /// Delegates to <see cref="GameLocaleExtensions.Parse"/> so a language added to <see cref="GameLocale"/>
    /// survives into <c>PlayerSession.Locale</c> and reaches the server-side <see cref="Localize"/> path
    /// instead of being collapsed to "en" here.</summary>
    private static string NormalizeLocale(string? locale)
        => GameLocaleExtensions.Parse(locale).Code();

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

        // The stable NPC key — the ONE derivation (#2598): an authored character's global key (#1128), a base
        // resident's slot key, a villager's own person key, else the settlement/station role key. It also fixes station
        // crew away from the vendor, who used to fall back to the key of a settlement named "".
        string npcKey = NpcKeyForNpc(session, npc);

        var rel = player.NpcMemory.TryGetValue(npcKey, out var r) ? r : null;
        int relValue = RelationshipValue(player, npcKey); // #2598: a villager without an own entry reads the floor
        int interactions = rel?.Log.Count ?? 0;
        string cacheKey = $"{npcKey}|{session.Locale}|{RelationshipTier(relValue)}";

        var req = new NpcLineRequest
        {
            NpcName = npc.Name,
            Role = npc.Role,
            Job = npc.Job,
            Theme = npc.Theme,
            IsRobot = npc.IsRobot,
            Settlement = string.IsNullOrEmpty(npc.Settlement) ? _settlementName : npc.Settlement,
            // #2590: never the real name — the cache key has no player, so a line naming "Justus" would greet every
            // other player at this NPC/standing/language as Justus. The token is filled per receiver at send time.
            PlayerName = AiLineTokens.Player,
            Relationship = relValue,
            PastInteractions = interactions,
            Language = session.Locale,
            Persona = PersonaFor(npcKey, npc.Theme, npc.IsRobot, npc.Role), // L2: stable per-NPC voice
            RecentEvents = RecentEventsLine(rel),                        // L2: what they remember
        };
        return (req, npcKey, cacheKey);
    }

    // L2: a small pool of persona voices, assigned deterministically per NPC (stable across visits)
    // and flavoured by kind — androids get precise/literal voices, organics the warmer ones.
    // #2591 (E14): ten kid-friendly organic voices, so the villagers of one place rarely share a voice. Changing the
    // pool re-deals the voices once (tone only — nothing is stored).
    private static readonly string[] PersonaPoolOrganic =
    {
        "gruff old-timer who speaks in short sentences but has a warm heart",
        "cheerful chatterbox who loves to share the local news",
        "calm, patient helper who always finds a kind word",
        "curious tinkerer who gets excited about gadgets and how things work",
        "proud craftsman who respects good gear and hates waste",
        "dry humorist who deadpans even good news",
        "sunny optimist who sees the bright side of everything",
        "shy, gentle soul who speaks softly and smiles a lot",
        "storyteller who loves old tales of the frontier and the stars",
        "busy go-getter who is always in a hurry but never too busy to wave",
    };

    private static readonly string[] PersonaPoolRobot =
    {
        "overly precise android that quantifies everything it says",
        "politely literal android still learning small talk",
        "old service unit with glitchy, antiquated courtesy phrases",
    };

    /// <summary>L2: deterministic persona descriptor for an NPC — same NPC, same voice, every visit.</summary>
    /// <summary>#1793: the G.D.S. guardians speak with one voice — and never say what the letters mean.</summary>
    private const string PersonaGuardian =
        "a G.D.S. guardian machine: terse, formal, courteous, faintly menacing; never explains what G.D.S. stands for; calls the city 'ours'; two sentences at most";

    private static string PersonaFor(string npcKey, string theme, bool isRobot, string role = "")
    {
        if (role == "guardian")
        {
            return PersonaGuardian;
        }

        var pool = isRobot ? PersonaPoolRobot : PersonaPoolOrganic;
        string persona = pool[PersonaSlot(npcKey, pool.Length)];
        return string.IsNullOrEmpty(theme) ? persona : $"{persona}; lives among {theme}";
    }

    /// <summary>#2591: the persona of an NPC as a small number for AI-line cache keys (<c>p{index}</c>) — the same
    /// deterministic pick as <see cref="PersonaFor"/>: organic voices 0..9, robot voices after them, the guardian voice
    /// last. Two villagers with the same voice share cached lines; the key never names anyone.</summary>
    internal static int PersonaIndexFor(string npcKey, bool isRobot, string role)
    {
        if (role == "guardian")
        {
            return PersonaPoolOrganic.Length + PersonaPoolRobot.Length;
        }

        return isRobot
            ? PersonaPoolOrganic.Length + PersonaSlot(npcKey, PersonaPoolRobot.Length)
            : PersonaSlot(npcKey, PersonaPoolOrganic.Length);
    }

    private static int PersonaSlot(string npcKey, int poolSize)
    {
        ulong h = (ulong)BlocksBeyondTheStars.WorldGeneration.WorldGenerator.StableHash("persona:" + npcKey);
        return (int)(h % (ulong)poolSize);
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

        // NPC story threads (#1112): a person who KNOWS something — the settler legend from the elder, the
        // Guardian rumour from the quartermaster. Fires once per player, gated by relationship + knowledge;
        // beats hints and smalltalk because it is the rarest thing an NPC can say. Deterministic text (never
        // LLM), and a thread that carries a fragment records it like any other find.
        string thread = TryEmitNpcThread(session, npc, npcKey);
        if (thread.Length > 0)
        {
            Send(session, new NpcGreeting { NpcId = npc.Id, Name = npc.Name, Role = npc.Role, Text = thread, GreetingId = NextGreetingId() });
            return;
        }

        // NPC treasure hints: occasionally the NPC shares where the wreck / a hidden cache lies instead of a
        // greeting — revealing it on the map for everyone. Deterministic text; deliberately bypasses the LLM
        // path (the greeting cache is per relationship tier, so a cached line would replay another player's
        // coordinates to the wrong listener).
        string hint = TryEmitHint(session, npcKey);
        if (hint.Length > 0)
        {
            Send(session, new NpcGreeting { NpcId = npc.Id, Name = npc.Name, Role = npc.Role, Text = hint, GreetingId = NextGreetingId() });
            return;
        }

        // 1) Instant response: a cached LLM line if we have one (#2590: one of up to three variants, with THIS
        //    player's name filled into its {player} token), else empty → the client shows its localized fallback.
        //    Either way the player always gets a greeting, with or without an AI backend.
        string? cachedLine = _config.AiLevel == AiLevel.Off ? null : _greetingCache.Pick(cacheKey);
        string immediate = cachedLine is null ? string.Empty : FillPlayerToken(cachedLine, session);

        // P7: with no LLM backend, let settlement/station NPCs occasionally speak a story flavour line — gated
        // by the world's knowledge level + tags + the NPC's role — so villages react to the unfolding story.
        if (immediate.Length == 0 && _config.AiLevel == AiLevel.Off)
        {
            immediate = PickStoryFlavourText(session, npc);
        }

        // #2593: the bubble's id — the AI upgrade below reuses it, so the client swaps the text without a second voice.
        int greetingId = NextGreetingId();
        Send(session, new NpcGreeting { NpcId = npc.Id, Name = npc.Name, Role = npc.Role, Text = immediate, GreetingId = greetingId });

        // 2) With AI on: nothing cached → generate a line off the game thread and push it as the upgrade of this
        //    bubble when ready. A cache hit → while fewer than three variants exist, ask for one more in the
        //    background 30 % of the time (#2590), so an NPC doesn't say the same sentence on every visit. Both draw on
        //    the world's AI line budget (GameServerAiLines.cs). Decided by the CACHE, not by `immediate`, so an authored
        //    instant line never stops the AI line from being made.
        if (_config.AiLevel == AiLevel.Off)
        {
            return;
        }

        if (cachedLine is not null)
        {
            if (_greetingCache.VariantCount(cacheKey) < AiLineMaxVariants && AiTopUpRoll())
            {
                StartGreetingLine(cacheKey, req, AiLinePriority.Personal, session.ConnectionId, upgrade: null);
            }

            return;
        }

        // #2593: the upgrade of the bubble sent above — same id, no gesture (it played with the first message).
        var upgrade = new NpcGreeting { NpcId = npc.Id, Name = npc.Name, Role = npc.Role, GreetingId = greetingId, Gesture = NpcGestures.None };
        StartGreetingLine(cacheKey, req, AiLinePriority.PlayerInitiated, session.ConnectionId, upgrade);
    }

    /// <summary>#2590: generates one greeting variant off the game thread — deduped per cache key, on the world's AI
    /// line budget — and caches it with the player only as the <c>{player}</c> token. With <paramref name="upgrade"/>
    /// the line is also queued for <paramref name="connectionId"/> as the late upgrade of that bubble;
    /// <see cref="TickGreetings"/> fills in the receiver's own name when it sends it.</summary>
    private void StartGreetingLine(string cacheKey, NpcLineRequest req, AiLinePriority priority, int connectionId, NpcGreeting? upgrade)
    {
        if (!_greetingInFlight.TryAdd(cacheKey, 1))
        {
            return; // a request for this exact line is already running
        }

        if (!TryTakeAiBudget(priority))
        {
            _greetingInFlight.TryRemove(cacheKey, out _);
            return; // budget spent — the static fallback stays, the next visit tries again
        }

        RunAiOffThread(() =>
        {
            try
            {
                if (AcceptGreetingLine(_ai.GenerateNpcLine(req)) is { } line)
                {
                    _greetingCache.Add(cacheKey, line);
                    if (upgrade is not null)
                    {
                        upgrade.Text = line;
                        _greetingOutbox.Enqueue((connectionId, upgrade));
                    }
                }
            }
            catch
            {
                // best-effort — the player already has the static fallback; never surface AI failures
            }
            finally
            {
                _greetingInFlight.TryRemove(cacheKey, out _);
                _aiLineBudget.Release();
            }
        });
    }

    /// <summary>The only placeholder a /npc-line greeting may contain.</summary>
    private static readonly string[] GreetingTokens = { AiLineTokens.Player };

    /// <summary>#2590: a /npc-line answer we may cache — trimmed, wrapping quotes removed, and no placeholder but
    /// <c>{player}</c> (no stray brace). Anything else is dropped like a backend failure.</summary>
    private static string? AcceptGreetingLine(string? line)
    {
        string text = AiLineText.Normalize(line);
        return text.Length > 0 && AiLineText.TokensAllowed(text, GreetingTokens) ? text : null;
    }

    /// <summary>#2590: fills the <c>{player}</c> token of an AI line with THIS receiver's own name (screened at join).
    /// Text without a token passes through unchanged.</summary>
    private static string FillPlayerToken(string text, PlayerSession receiver)
    {
        if (text.IndexOf('{') < 0)
        {
            return text;
        }

        var values = new Dictionary<string, string>(System.StringComparer.Ordinal) { [AiLineTokens.Player] = receiver.State.Name ?? string.Empty };
        return AiLineText.FillTokens(text, values) ?? text;
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

    /// <summary>Server-side localized display name for a content definition: resolves its name key in the
    /// player's locale, falling back to the raw content key when no name key is defined.</summary>
    private string LocalizedName(string localeCode, string? nameKey, string fallbackKey)
        => string.IsNullOrEmpty(nameKey) ? fallbackKey : Localize(localeCode, nameKey);

    /// <summary>Drains LLM greetings produced off-thread and sends each to its (still-connected) player. Called
    /// once per server tick from <c>Tick</c>.</summary>
    private void TickGreetings()
    {
        while (_greetingOutbox.TryDequeue(out var pending))
        {
            if (_sessions.TryGetValue(pending.ConnectionId, out var session) && session.Joined)
            {
                // #2590: an AI line names the player only as {player} — fill in THIS receiver's name at send time.
                pending.Message.Text = FillPlayerToken(pending.Message.Text ?? string.Empty, session);
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
    /// would render its localized static fallback); otherwise the (now-cached) generated line — one of the cached
    /// variants, with THIS player's name filled into its <c>{player}</c> token, exactly as the player would see it.
    /// Draws on the AI line budget like the real path; never asks for an extra variant (see
    /// <see cref="EmitGreetingForTest"/> for that).</summary>
    public string? GreetingLineForTest(string playerId, string role)
    {
        if (FindSessionByPlayerId(playerId) is not { } session || GreetingNpcInReach(session, role) is not { } npc)
        {
            return null;
        }

        var (req, _, cacheKey) = NpcContext(session, npc);
        if (_config.AiLevel != AiLevel.Off && _greetingCache.Pick(cacheKey) is { } cached)
        {
            return FillPlayerToken(cached, session);
        }

        if (_config.AiLevel == AiLevel.Off || !TryTakeAiBudget(AiLinePriority.PlayerInitiated))
        {
            return string.Empty; // client shows its static localized fallback
        }

        try
        {
            if (AcceptGreetingLine(_ai.GenerateNpcLine(req)) is { } line)
            {
                _greetingCache.Add(cacheKey, line);
                return FillPlayerToken(line, session);
            }
        }
        finally
        {
            _aiLineBudget.Release();
        }

        return string.Empty;
    }

    /// <summary>Test seam: the real greeting path (<see cref="EmitGreeting"/>) for the nearest in-reach NPC of
    /// <paramref name="role"/> — what the client's NpcGreetIntent triggers. False when none is in reach. Pair with
    /// <see cref="RunAiLinesSynchronouslyForTest"/> + <see cref="TickGreetingsForTest"/> to see the AI upgrade.</summary>
    public bool EmitGreetingForTest(string playerId, string role)
    {
        if (FindSessionByPlayerId(playerId) is not { } session || GreetingNpcInReach(session, role) is not { } npc)
        {
            return false;
        }

        EmitGreeting(session, npc);
        return true;
    }

    /// <summary>Test seam: sends the AI greetings finished off-thread (what every tick does).</summary>
    public void TickGreetingsForTest() => TickGreetings();

    /// <summary>Test seam: the cached greeting variants (tokens unfilled) the nearest in-reach NPC of
    /// <paramref name="role"/> has for this player's cache key (their standing + language).</summary>
    public IReadOnlyList<string> GreetingVariantsForTest(string playerId, string role)
    {
        if (FindSessionByPlayerId(playerId) is not { } session || GreetingNpcInReach(session, role) is not { } npc)
        {
            return System.Array.Empty<string>();
        }

        return _greetingCache.Variants(NpcContext(session, npc).CacheKey);
    }

    /// <summary>Test seam: how many greeting cache keys the cache holds at most (#2590).</summary>
    public int GreetingCacheCapacityForTest => _greetingCache.Capacity;

    private ServerNpc? GreetingNpcInReach(PlayerSession session, string role)
        => NearestNpc(session.State, role) is { } npc
           && WrapDistSq(session.State.Position, npc.Pos) <= NpcGreetRange * NpcGreetRange
            ? npc
            : null;
}
