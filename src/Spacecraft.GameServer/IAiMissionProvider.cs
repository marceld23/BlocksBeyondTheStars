using System.Net.Http;
using System.Text;
using System.Text.Json;
using Spacecraft.Shared.Missions;

namespace Spacecraft.GameServer;

/// <summary>
/// Source of AI-generated <see cref="MissionPlan"/>s. The server stays authoritative: it
/// validates whatever the provider returns. Implementations must never throw fatally — a
/// failure returns null so the game keeps working without AI (`anf_mission_editor.md` §14).
/// </summary>
public interface IAiMissionProvider
{
    /// <summary>Returns a mission plan for the given context, or null if unavailable/disabled.</summary>
    MissionPlan? Generate(string context);
}

/// <summary>AI disabled — always returns null. Default provider.</summary>
public sealed class NullAiMissionProvider : IAiMissionProvider
{
    public MissionPlan? Generate(string context) => null;
}

/// <summary>
/// Calls the optional Python AI backend over HTTP (POST {BaseUrl}/mission-plan with the
/// context). Any error (backend down, bad response) is swallowed and returns null so the
/// server falls back to no-AI behaviour.
/// </summary>
public sealed class HttpAiMissionProvider : IAiMissionProvider
{
    private readonly HttpClient _http;
    private readonly string _url;

    public HttpAiMissionProvider(string baseUrl, HttpClient? http = null)
    {
        _url = baseUrl.TrimEnd('/') + "/mission-plan";
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    public MissionPlan? Generate(string context)
    {
        try
        {
            var body = new StringContent(JsonSerializer.Serialize(new { context }), Encoding.UTF8, "application/json");
            using var response = _http.PostAsync(_url, body).GetAwaiter().GetResult();
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
}
