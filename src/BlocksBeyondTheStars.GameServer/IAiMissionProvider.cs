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

    /// <summary>The NPC role: "vendor" or "quartermaster".</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>The settlement's trade theme: "miners" / "traders" / "researchers" / "settlers".</summary>
    public string Theme { get; set; } = string.Empty;

    /// <summary>Whether the NPC is an android (researchers are robots) — flavours the tone.</summary>
    public bool IsRobot { get; set; }

    /// <summary>The settlement's display name (e.g. "Karth Town").</summary>
    public string Settlement { get; set; } = string.Empty;

    /// <summary>The player's display name.</summary>
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>Relationship score with this player (−100..+100; 0 = first meeting).</summary>
    public int Relationship { get; set; }

    /// <summary>How many past interactions this player has had with the NPC (0 = stranger).</summary>
    public int PastInteractions { get; set; }

    /// <summary>Language to write the greeting in: "en" or "de".</summary>
    public string Language { get; set; } = "en";

    /// <summary>L2: a short persona descriptor the server derives deterministically per NPC
    /// (e.g. "gruff veteran who speaks in short sentences"). Empty = no specific persona.</summary>
    public string Persona { get; set; } = string.Empty;

    /// <summary>L2: a compact log of what this NPC remembers of the player recently, oldest first
    /// (e.g. "trade, trade, mission accepted"). Empty = nothing notable.</summary>
    public string RecentEvents { get; set; } = string.Empty;

    /// <summary>Ship-AI banter: the current game situation in one compact line (world, time of day,
    /// progress) so VEGA can comment on it. Empty for settlement NPCs.</summary>
    public string Situation { get; set; } = string.Empty;
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
}

/// <summary>AI disabled — always returns null. Default provider.</summary>
public sealed class NullAiMissionProvider : IAiMissionProvider
{
    public MissionPlan? Generate(string context) => null;

    public string? GenerateNpcLine(NpcLineRequest request) => null;

    public MissionTextResult? GenerateMissionText(MissionTextRequest request) => null;
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

    public HttpAiMissionProvider(string baseUrl, HttpClient? http = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _missionUrl = _baseUrl + "/mission-plan";
        _npcLineUrl = _baseUrl + "/npc-line";
        _missionTextUrl = _baseUrl + "/mission-text";
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    public MissionPlan? Generate(string context)
    {
        try
        {
            var body = new StringContent(JsonSerializer.Serialize(new { context }), Encoding.UTF8, "application/json");
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

    public string? GenerateNpcLine(NpcLineRequest request)
    {
        try
        {
            var body = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
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

    public MissionTextResult? GenerateMissionText(MissionTextRequest request)
    {
        try
        {
            var body = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
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

    private sealed class NpcLineResponse
    {
        public string Text { get; set; } = string.Empty;
    }
}
