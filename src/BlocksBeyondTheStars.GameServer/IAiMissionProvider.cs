// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Net.Http;
using System.Text;
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Missions;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Context for an AI-authored NPC greeting line (item 15). All fields are plain data the backend
/// turns into a short, in-character line; the server fills it from live game state.
/// </summary>
public sealed class NpcLineRequest
{
    /// <summary>The NPC's coined personal name (e.g. "Vega-7").</summary>
    public string NpcName { get; set; } = string.Empty;

    /// <summary>The NPC role: "vendor", "quartermaster", "settler", "guardian" or "ship_ai" (VEGA banter).</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>#2590: the NPC's job when it has one ("guard", "gardener", "craftsman", "innkeeper" or a profession
    /// such as "doctor" / "grocer" / "reporter"), "" otherwise. Lets the backend describe a settler by what they do;
    /// older backends ignore the field.</summary>
    public string Job { get; set; } = string.Empty;

    /// <summary>The settlement's trade theme: "miners" / "traders" / "researchers" / "settlers".</summary>
    public string Theme { get; set; } = string.Empty;

    /// <summary>Whether the NPC is an android (researchers are robots) — flavours the tone.</summary>
    public bool IsRobot { get; set; }

    /// <summary>The settlement's display name (e.g. "Karth Town").</summary>
    public string Settlement { get; set; } = string.Empty;

    /// <summary>How the line names the player. Since #2590 the server always sends the placeholder
    /// <c>{player}</c> (<see cref="AiLineTokens.Player"/>) and fills in the RECEIVING player's name when it sends
    /// the line — a cached line is shared by every player at the same NPC/standing/language, so it must never
    /// carry a real name.</summary>
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>Relationship score (−100..+100; 0 = first meeting). Since #2618 the server sends the representative score
    /// of the player's standing TIER (hostile −30 / stranger 0 / known 25 / trusted 60, <c>GameServer.TierRepresentative</c>),
    /// never the exact score — the line is cached per tier and shared by every player in it.</summary>
    public int Relationship { get; set; }

    /// <summary>Past interactions with the NPC (0 = stranger). Since #2618 the tier's representative count (0 / 0 / 3 / 10),
    /// never this player's own count.</summary>
    public int PastInteractions { get; set; }

    /// <summary>Language to write the greeting in: "en" or "de".</summary>
    public string Language { get; set; } = "en";

    /// <summary>L2: a short persona descriptor the server derives deterministically per NPC
    /// (e.g. "gruff veteran who speaks in short sentences"). Empty = no specific persona.</summary>
    public string Persona { get; set; } = string.Empty;

    /// <summary>L2: a compact log of what this NPC remembers of the player recently, oldest first
    /// (e.g. "trade, trade, mission accepted"). Empty = nothing notable. Since #2618 the server always sends it empty:
    /// a cached greeting is shared by every player of the standing tier, so it must not recall one player's deeds.
    /// Kept in the contract for older/other callers.</summary>
    public string RecentEvents { get; set; } = string.Empty;

    /// <summary>Ship-AI banter: the current game situation in one compact line (world, time of day,
    /// progress) so VEGA can comment on it. Empty for settlement NPCs.</summary>
    public string Situation { get; set; } = string.Empty;
}

/// <summary>
/// #2591: context for ONE set of AI-personalised lines on the shared <c>POST /npc-lines</c> path — every new NPC/VEGA
/// line of the village-life package (passing hellos, welcome-backs, villager chatter, wishes, guests, VEGA news …)
/// uses this one contract. Serialized with its PascalCase property names; the backend answers <c>{ "Lines": [...] }</c>.
///
/// The authored, localized lines are always the truth (and what players see first). The model only re-words them:
/// every exact fact — a name, a number, an item, a place — travels as a placeholder token (<see cref="Tokens"/>,
/// e.g. <c>{player}</c>, <c>{count}</c>) that the server fills in per RECEIVING player, so one generated line serves
/// every player of that language and a cached line never carries a real name. Build it on the game thread through
/// <c>GameServer.RequestAiLines</c> / <c>AiLinesFor</c> (GameServerAiLines.cs), never call the provider directly.
/// </summary>
public sealed class NpcLinesRequest
{
    /// <summary>What the lines are for — one of <see cref="NpcLinePurposes"/> (e.g. "passing_hello", "chatter").</summary>
    public string Purpose { get; set; } = string.Empty;

    /// <summary>Locale code to write in ("en", "de", "fr" …).</summary>
    public string Language { get; set; } = "en";

    /// <summary>Where it happens, as plain words for tone ("a farming village", "the player's home base"). Never a
    /// real place NAME the line should repeat — names go through a token such as <c>{place}</c>.</summary>
    public string Place { get; set; } = string.Empty;

    /// <summary>The settlement's theme ("miners" / "traders" / "researchers" / "settlers"), "" when none.</summary>
    public string Theme { get; set; } = string.Empty;

    /// <summary>The relationship stage the line is written for: "stranger", "known", "trusted", "resident" (the
    /// player's own base resident) or "" when it doesn't apply (VEGA, news).</summary>
    public string Stage { get; set; } = string.Empty;

    /// <summary>Whole days the player was away (welcome-back lines), 0 otherwise. Coarse on purpose (cache keys use
    /// buckets such as 1 / 2–6 / 7+ days).</summary>
    public int AwayDays { get; set; }

    /// <summary>The situation in one short line ("evening, light rain", "the player just boarded the ship").</summary>
    public string Situation { get; set; } = string.Empty;

    /// <summary>Who speaks, in order. One speaker for a single NPC's line; two for a villager conversation, whose
    /// lines alternate in this order (A, B, A, B …).</summary>
    public List<NpcLinesSpeaker> Speakers { get; set; } = new();

    /// <summary>The topic in a few English words for the model ("rain on the gardens", "a trader landed").</summary>
    public string TopicHint { get; set; } = string.Empty;

    /// <summary>Facts the lines may mention, written WITH tokens instead of real values ("{player} chased the bandits
    /// out of their camp near {place}"). The model may only state what is here.</summary>
    public List<string> Facts { get; set; } = new();

    /// <summary>The placeholder tokens the lines may use, written literally (e.g. "{player}", "{count}"). The server
    /// rejects an answer with any other token, a stray brace or a digit outside a token.</summary>
    public List<string> Tokens { get; set; } = new();

    /// <summary>The authored lines (already localized, tokens unfilled) as the meaning anchor — the model re-words
    /// them in the speaker's voice and keeps their meaning.</summary>
    public List<string> Authored { get; set; } = new();

    /// <summary>Exactly how many lines to return (a conversation keeps the authored line count so its pacing holds).</summary>
    public int LineCount { get; set; } = 1;

    /// <summary>Upper length of each line in characters (tokens counted as written).</summary>
    public int MaxChars { get; set; } = 120;
}

/// <summary>#2591: one speaker of an <see cref="NpcLinesRequest"/>.</summary>
public sealed class NpcLinesSpeaker
{
    /// <summary>The token that names this speaker inside the lines ("{a}", "{b}", "{npc}"), "" when unnamed.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>The NPC role ("vendor", "quartermaster", "settler", "ship_ai" …).</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>The NPC's job when it has one ("gardener", "guard", a profession …), "" otherwise.</summary>
    public string Job { get; set; } = string.Empty;

    /// <summary>The speaker's persona voice (<c>GameServer.PersonaFor</c>), "" for none.</summary>
    public string Persona { get; set; } = string.Empty;

    /// <summary>Whether the speaker is an android.</summary>
    public bool IsRobot { get; set; }
}

/// <summary>#2591: the purposes of the shared <c>/npc-lines</c> path. The backend has one sentence of guidance per
/// purpose; an unknown purpose still works (generic guidance).</summary>
public static class NpcLinePurposes
{
    /// <summary>A short hello as the player walks past (known+ only — strangers never get AI hellos).</summary>
    public const string PassingHello = "passing_hello";

    /// <summary>A known NPC greets the player back after a long time away (<see cref="NpcLinesRequest.AwayDays"/>).</summary>
    public const string WelcomeBack = "welcome_back";

    /// <summary>A resident of the player's own base says hello.</summary>
    public const string ResidentHello = "resident_hello";

    /// <summary>Re-words the opening line of an authored dialog (keeps its meaning; answers stay authored).</summary>
    public const string DialogOpen = "dialog_open";

    /// <summary>A villager who is the player's friend says hello.</summary>
    public const string FriendHello = "friend_hello";

    /// <summary>A friend hands the player a small gift (the item line itself stays authored).</summary>
    public const string FriendGift = "friend_gift";

    /// <summary>Two villagers talk to each other (alternating speakers).</summary>
    public const string Chatter = "chatter";

    /// <summary>Villagers cheer at a festival.</summary>
    public const string Cheer = "cheer";

    /// <summary>The quartermaster thanks the hero of a festival.</summary>
    public const string FestivalThanks = "festival_thanks";

    /// <summary>A news headline about a player's deed.</summary>
    public const string NewsDeed = "news_deed";

    /// <summary>A villager asks the player for a small favour (a wish).</summary>
    public const string WishAsk = "wish_ask";

    /// <summary>A villager thanks the player for a fulfilled wish.</summary>
    public const string WishThanks = "wish_thanks";

    /// <summary>A guest arriving at the player's base greets them.</summary>
    public const string GuestGreet = "guest_greet";

    /// <summary>A guest offers a gift, goods or a wish.</summary>
    public const string GuestOffer = "guest_offer";

    /// <summary>A landed trader hails the player.</summary>
    public const string TraderHail = "trader_hail";

    /// <summary>VEGA welcomes the player back aboard / after a break.</summary>
    public const string VegaWelcome = "vega_welcome";

    /// <summary>VEGA relays news from the player's base.</summary>
    public const string VegaNews = "vega_news";

    /// <summary>VEGA praises an achievement.</summary>
    public const string VegaPraise = "vega_praise";
}

/// <summary>L3: flavour-text context for ONE board mission whose objective/reward are already fixed
/// by the server — the backend only writes Title + Description around them.</summary>
public sealed class MissionTextRequest
{
    public string GiverName { get; set; } = string.Empty;
    public string Place { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public string NeedItem { get; set; } = string.Empty;
    public int Required { get; set; }
    public string RewardItem { get; set; } = string.Empty;
    public int RewardCount { get; set; }
    public string Language { get; set; } = "en";

    /// <summary>The job's shape: "gather" (deliver goods), "bounty_camp" (drive bandits from their camp)
    /// or "bounty_ship" (drive off a raider ship). Older backends ignore the field and treat everything
    /// as a gather job — the deterministic locale fallback still fits then.</summary>
    public string Kind { get; set; } = "gather";
}

/// <summary>L3: the generated board-mission posting text.</summary>
public sealed class MissionTextResult
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Source of AI-generated text. The server stays authoritative: it validates <see cref="MissionPlan"/>s and
/// only ever shows greeting lines as flavour. Implementations must never throw fatally — a failure returns
/// null so the game keeps working without AI (`anf_mission_editor.md` §14).
/// </summary>
public interface IAiMissionProvider
{
    /// <summary>Returns a mission plan for the given context, or null if unavailable/disabled.</summary>
    MissionPlan? Generate(string context);

    /// <summary>Returns a short, in-character NPC greeting for the context, or null if unavailable/disabled
    /// (item 15). The caller falls back to a static localized line when this is null.</summary>
    string? GenerateNpcLine(NpcLineRequest request);

    /// <summary>Returns LLM flavour text for a fixed board mission (L3), or null if unavailable/disabled.
    /// The caller keeps its localized static board text when this is null.</summary>
    MissionTextResult? GenerateMissionText(MissionTextRequest request);

    /// <summary>#2591: returns the AI-personalised lines for one <see cref="NpcLinesRequest"/> (<c>POST /npc-lines</c>),
    /// or null if unavailable/disabled/refused — an old backend without the endpoint (404) is null too. The caller
    /// validates the answer (count, length, tokens) and keeps its authored lines whenever this is null.
    /// Declared explicitly (no default interface member): the GameServer also builds for netstandard2.1.</summary>
    string[]? GenerateNpcLines(NpcLinesRequest request);
}

/// <summary>AI disabled — always returns null. Default provider.</summary>
public sealed class NullAiMissionProvider : IAiMissionProvider
{
    public MissionPlan? Generate(string context) => null;

    public string? GenerateNpcLine(NpcLineRequest request) => null;

    public MissionTextResult? GenerateMissionText(MissionTextRequest request) => null;

    public string[]? GenerateNpcLines(NpcLinesRequest request) => null;
}

/// <summary>
/// Calls the optional Python AI backend over HTTP (POST {BaseUrl}/mission-plan with the
/// context). Any error (backend down, bad response) is swallowed and returns null so the
/// server falls back to no-AI behaviour.
/// </summary>
public sealed class HttpAiMissionProvider : IAiMissionProvider
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _missionUrl;
    private readonly string _npcLineUrl;

    private readonly string _missionTextUrl;
    private readonly string _npcLinesUrl;

    public HttpAiMissionProvider(string baseUrl, HttpClient? http = null, int timeoutSeconds = 35)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _missionUrl = _baseUrl + "/mission-plan";
        _npcLineUrl = _baseUrl + "/npc-line";
        _missionTextUrl = _baseUrl + "/mission-text";
        _npcLinesUrl = _baseUrl + "/npc-lines";

        // Generous by design (ServerConfig.AiTimeoutSeconds): callers never block gameplay on this —
        // NPC lines upgrade asynchronously — so the timeout only bounds how late a line may still land.
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)) };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD002:Avoid problematic synchronous waits",
        Justification = "Synchronous provider contract; invoked off the game tick / on background threads where the server has no SynchronizationContext, so GetAwaiter().GetResult() cannot deadlock.")]
    public MissionPlan? Generate(string context)
    {
        try
        {
            using var body = new StringContent(JsonSerializer.Serialize(new { context }), Encoding.UTF8, "application/json");
            using var response = _http.PostAsync(_missionUrl, body).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonSerializer.Deserialize<MissionPlan>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            });
        }
        catch
        {
            return null; // backend unreachable / invalid -> graceful fallback
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD002:Avoid problematic synchronous waits",
        Justification = "Synchronous provider contract; invoked off the game tick / on background threads where the server has no SynchronizationContext, so GetAwaiter().GetResult() cannot deadlock.")]
    public string? GenerateNpcLine(NpcLineRequest request)
    {
        try
        {
            using var body = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
            using var response = _http.PostAsync(_npcLineUrl, body).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var line = JsonSerializer.Deserialize<NpcLineResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            return string.IsNullOrWhiteSpace(line?.Text) ? null : line!.Text.Trim();
        }
        catch
        {
            return null; // backend unreachable / invalid -> static fallback
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD002:Avoid problematic synchronous waits",
        Justification = "Synchronous provider contract; invoked off the game tick / on background threads where the server has no SynchronizationContext, so GetAwaiter().GetResult() cannot deadlock.")]
    public MissionTextResult? GenerateMissionText(MissionTextRequest request)
    {
        try
        {
            using var body = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
            using var response = _http.PostAsync(_missionTextUrl, body).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var text = JsonSerializer.Deserialize<MissionTextResult>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            return string.IsNullOrWhiteSpace(text?.Title) || string.IsNullOrWhiteSpace(text?.Description)
                ? null   // backend has no LLM (empty fields) or replied oddly → keep the static text
                : text;
        }
        catch
        {
            return null; // backend unreachable / invalid -> static board text
        }
    }

    /// <summary>#2591: POST {base}/npc-lines. A backend from before the endpoint answers 404, which is null like
    /// any other failure; a backend without a model answers an empty list, which is null too — the caller keeps its
    /// authored lines in every one of those cases.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD002:Avoid problematic synchronous waits",
        Justification = "Synchronous provider contract; invoked off the game tick / on background threads where the server has no SynchronizationContext, so GetAwaiter().GetResult() cannot deadlock.")]
    public string[]? GenerateNpcLines(NpcLinesRequest request)
    {
        try
        {
            using var body = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
            using var response = _http.PostAsync(_npcLinesUrl, body).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return null; // 404 = a backend from before /npc-lines
            }

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var lines = JsonSerializer.Deserialize<NpcLinesResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            return lines?.Lines is { Count: > 0 } list ? list.ToArray() : null;
        }
        catch
        {
            return null; // backend unreachable / invalid -> authored lines
        }
    }

    private sealed class NpcLineResponse
    {
        public string Text { get; set; } = string.Empty;
    }

    private sealed class NpcLinesResponse
    {
        public List<string>? Lines { get; set; }
    }
}
