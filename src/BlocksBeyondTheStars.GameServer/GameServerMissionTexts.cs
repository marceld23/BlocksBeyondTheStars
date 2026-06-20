using System.Collections.Concurrent;
using System.Linq;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Missions;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// L3: LLM flavour text for board (giver) missions. The mission itself stays server-coined and
/// validated — objectives, rewards and ids never change; the LLM only writes a Title + Description
/// around the fixed job, in the player's language. Same resilience pattern as the NPC greetings:
/// generated off the game thread, cached per (mission, locale), localized static board text as the
/// instant/offline fallback, and a mission-list refresh pushed when a text arrives.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Generated texts keyed by "missionId|locale" (board missions are deterministic per
    /// slot, so a text stays valid as long as the board offers the mission).</summary>
    private readonly ConcurrentDictionary<string, MissionTextResult> _missionTextCache = new();

    private readonly ConcurrentDictionary<string, byte> _missionTextInFlight = new();

    /// <summary>Players whose open mission list should refresh because a text just arrived.</summary>
    private readonly ConcurrentQueue<int> _missionTextRefresh = new();

    private static string MissionTextKey(string missionId, string locale) => missionId + "|" + locale;

    /// <summary>The cached LLM text for a board mission in this locale, or null (static board text).</summary>
    private MissionTextResult? BoardMissionText(string missionId, string locale)
        => _missionTextCache.TryGetValue(MissionTextKey(missionId, locale), out var text) ? text : null;

    /// <summary>Builds the flavour-text request for a fixed board mission from its definition.</summary>
    private MissionTextRequest BoardMissionTextRequest(MissionDefinition def, string locale)
    {
        var objective = def.Objectives.FirstOrDefault();
        var reward = def.Rewards.FirstOrDefault();
        bool settlementBoard = def.Id.StartsWith("settle_", System.StringComparison.Ordinal);
        string settlementName = settlementBoard ? SettlementForBoardMission(def.Id)?.Name ?? _settlementName : string.Empty;
        return new MissionTextRequest
        {
            GiverName = def.GiverName,
            Place = settlementBoard && !string.IsNullOrEmpty(settlementName) ? settlementName : "an orbital station",
            Theme = settlementBoard ? SettlementTradeFor(settlementName) : "traders",
            NeedItem = objective?.Target ?? string.Empty,
            Required = objective?.Required ?? 0,
            RewardItem = reward?.Item ?? string.Empty,
            RewardCount = reward?.Count ?? 0,
            Language = locale,
        };
    }

    /// <summary>Kicks off the off-thread text generation for a board mission if AI is on and nothing
    /// is cached or in flight yet. The player keeps the static text until the refresh lands.</summary>
    private void RequestBoardMissionText(PlayerSession session, MissionDefinition def)
    {
        if (_config.AiLevel == AiLevel.Off)
        {
            return;
        }

        string key = MissionTextKey(def.Id, session.Locale);
        if (_missionTextCache.ContainsKey(key) || !_missionTextInFlight.TryAdd(key, 1))
        {
            return;
        }

        var req = BoardMissionTextRequest(def, session.Locale);
        int connId = session.ConnectionId;

        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var text = _ai.GenerateMissionText(req);
                if (text is not null)
                {
                    _missionTextCache[key] = text;
                    _missionTextRefresh.Enqueue(connId); // re-send the list so the open board updates live
                }
            }
            catch
            {
                // best-effort flavour — the static board text is already on screen
            }
            finally
            {
                _missionTextInFlight.TryRemove(key, out _);
            }
        });
    }

    /// <summary>Drains pending mission-list refreshes (a generated text arrived). Called per tick.</summary>
    private void TickMissionTexts()
    {
        while (_missionTextRefresh.TryDequeue(out int connId))
        {
            if (_sessions.TryGetValue(connId, out var session) && session.Joined)
            {
                SendMissionList(session);
            }
        }
    }

    /// <summary>Test seam: synchronously generates + caches the L3 text for one board mission — the
    /// same context/provider/cache path the async flow uses. Null when AI is off, the mission is
    /// unknown, or the provider declines (static board text stays).</summary>
    public MissionTextResult? MissionTextForTest(string playerId, string missionId)
    {
        if (FindSessionByPlayerId(playerId) is not { } session || _config.AiLevel == AiLevel.Off
            || !_missionDefs.TryGetValue(missionId, out var def))
        {
            return null;
        }

        string key = MissionTextKey(missionId, session.Locale);
        if (_missionTextCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var text = _ai.GenerateMissionText(BoardMissionTextRequest(def, session.Locale));
        if (text is not null)
        {
            _missionTextCache[key] = text;
        }

        return text;
    }
}
