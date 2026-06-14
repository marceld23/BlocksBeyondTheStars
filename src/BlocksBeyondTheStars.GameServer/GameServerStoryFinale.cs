using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The finale flow (implementation plan P6): once the story is complete the Guardian (finale) system appears
/// on the star map; at the inner core the player <b>hacks</b> it open (a channel action) and then wins a
/// <b>dialogue duel</b> — the core is argued into shutdown, never destroyed by weapons. Clearing the duel calls
/// <see cref="MarkGuardianDefeated"/> (pacification).
///
/// Reveal + defeat are persisted per-save (on <see cref="StoryState"/>); the transient hack/duel progress is
/// runtime-only — a server restart simply means re-approaching the core, which matches the "re-approach the
/// finale" rule. Story-agnostic: the duel is driven entirely by the active pack's
/// <see cref="StoryDefinition.CoreArguments"/>.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How much one hack channel-tick adds (server-authoritative; 10 ⇒ ~10 ticks to open the core).</summary>
    private const int CoreHackTickAmount = 10;

    /// <summary>Runtime core-hack channel progress (0..100); not persisted (re-approach on restart).</summary>
    private int _coreHackProgress;

    /// <summary>Runtime: the core has been hacked open and the argument duel is active.</summary>
    private bool _coreHacked;

    /// <summary>Runtime: the current duel node index into the pack's <see cref="StoryDefinition.CoreArguments"/>.</summary>
    private int _duelNode;

    /// <summary>Stable id of the finale star system + its single landable core body. The system is lazily added
    /// to the galaxy when revealed (see <see cref="EnsureGuardianSystemInGalaxy"/>) — before that it exists
    /// nowhere, so it never shows on the map nor affects ordinary generation.</summary>
    public const string GuardianFinaleSystemId = "guardian_finale";
    public const string GuardianCoreBodyId = "guardian_finale-core";

    /// <summary>Per-player return location recorded on hyperjump INTO the finale system, so a death there sends
    /// the clone back to the world it launched from (no boss-arena death-loop). Runtime-only — a restart means
    /// re-approaching the finale, which is the intended rule. Keyed by player id.</summary>
    private readonly System.Collections.Generic.Dictionary<string, string> _finaleReturn = new();

    /// <summary>Clears the transient finale flow (hack channel + duel position). Called when the active story
    /// resets; the persisted reveal/defeat flags live on <see cref="StoryState"/> and are reset there.</summary>
    private void ResetFinaleRuntime()
    {
        _coreHackProgress = 0;
        _coreHacked = false;
        _duelNode = 0;
    }

    // ---------------- Stage 0: reveal the Guardian system ----------------

    /// <summary>Places the finale system on the star map once the arc is complete (every beat revealed). Fires
    /// exactly once: speaks the reveal line to everyone and broadcasts the <see cref="GuardianSystemRevealed"/>
    /// marker. The caller (<see cref="AdvanceStory"/>) persists + broadcasts the meter afterwards.</summary>
    private void RevealGuardianSystemIfReady()
    {
        if (_story is null || _storyState.GuardianSystemRevealed || _storyState.GuardianDefeated)
        {
            return;
        }

        if (_story.Beats.Count == 0 || !StoryEngine.AllBeatsRevealed(_story, _storyState))
        {
            return;
        }

        _storyState.GuardianSystemRevealed = true;
        EnsureGuardianSystemInGalaxy();
        SpeakVegaLineToAll("story.vega.guardian_revealed");
        BroadcastToJoined(new GuardianSystemRevealed { LabelKey = "story.vega.guardian_system" });
        BroadcastStarMap(); // the finale system now appears as a jump target on everyone's travel screen
    }

    /// <summary>Idempotently adds the finale star system (a lone landable core body) to the galaxy. Called when
    /// the system is revealed, and again on restart for an already-revealed save (the galaxy is regenerated
    /// from seed each start, so the special system must be re-appended). Placed at the far edge of the star
    /// map so it reads as "out past the frontier"; reached by hyperjump (needs a jump generator).</summary>
    private void EnsureGuardianSystemInGalaxy()
    {
        if (_galaxy is null || _galaxy.Systems.Any(s => s.Id == GuardianFinaleSystemId))
        {
            return;
        }

        var system = new StarSystem { Id = GuardianFinaleSystemId, Name = "Guardian Core", MapX = 980f, MapY = 980f };
        system.Bodies.Add(new CelestialBody
        {
            Id = GuardianCoreBodyId,
            Name = "Guardian Core",
            Kind = CelestialKind.Planet,         // landable, so the ship can set down + the finale can play out
            PlanetType = GuardianCorePlanetType(),
            SystemId = GuardianFinaleSystemId,
            SystemX = 0f,
            SystemZ = 0f,                        // the core sits at the heart of its otherwise empty system
        });
        _galaxy.Systems.Add(system);
    }

    /// <summary>Picks a fitting stark planet type for the core body, falling back to a type that always exists.</summary>
    private string GuardianCorePlanetType()
    {
        foreach (var pref in new[] { "barren", "volcanic", "ashen", "metallic", "rocky" })
        {
            if (_content.GetPlanet(pref) is not null)
            {
                return pref;
            }
        }

        return "rocky";
    }

    /// <summary>True when the given body id belongs to the finale system (used to gate the respawn rule).</summary>
    private bool IsGuardianSystemLocation(string locationId)
        => !string.IsNullOrEmpty(locationId) && _galaxy?.FindBody(locationId)?.SystemId == GuardianFinaleSystemId;

    /// <summary>Applies the finale respawn rule (P6): if the ship would respawn the clone inside the Guardian
    /// system and a pre-finale return location was recorded for this player, returns that body instead (and
    /// consumes the record) so the clone re-grows on the world it launched from. Otherwise returns
    /// <paramref name="shipHome"/> unchanged.</summary>
    private string ResolveRespawnHome(string playerId, string shipHome)
    {
        if (IsGuardianSystemLocation(shipHome)
            && _finaleReturn.TryGetValue(playerId, out var back)
            && !string.IsNullOrEmpty(back)
            && _galaxy?.FindBody(back) is not null)
        {
            _finaleReturn.Remove(playerId);
            return back;
        }

        return shipHome;
    }

    // ---------------- Stage 3: hack the core open ----------------

    /// <summary>Handles one core-hack channel tick (P6 stage 3). Valid only once the finale system is revealed
    /// and before the core is defeated; the server owns the increment. Completing it opens the argument duel.</summary>
    private void HandleCoreHack(PlayerSession session, CoreHackIntent intent)
    {
        _ = intent;
        if (!StoryActive || !_storyState.GuardianSystemRevealed || _storyState.GuardianDefeated || _coreHacked)
        {
            return;
        }

        _coreHackProgress = System.Math.Min(100, _coreHackProgress + CoreHackTickAmount);
        bool complete = _coreHackProgress >= 100;
        BroadcastToJoined(new CoreHackProgress { Progress = _coreHackProgress, Complete = complete });

        if (complete)
        {
            _coreHacked = true;
            BeginCoreDuel();
        }
    }

    // ---------------- Stage 4: the argument duel ----------------

    /// <summary>Opens the duel at the first node (or wins immediately if the pack scripts no duel).</summary>
    private void BeginCoreDuel()
    {
        _duelNode = 0;
        if (_story is null || _story.CoreArguments.Count == 0)
        {
            WinDuel();
            return;
        }

        BroadcastDuelNode(string.Empty);
    }

    /// <summary>Handles a rebuttal pick (P6 stage 4). A correct (contradiction) choice advances the duel — and,
    /// at the last node, shuts the core down; a wrong choice is dismissed and the same node is re-presented.
    /// The duel cannot be lost, only stalled (weapons can't end the core — only its own contradiction can).</summary>
    private void HandleCoreDialogueChoice(PlayerSession session, CoreDialogueChoiceIntent intent)
    {
        if (!StoryActive || !_coreHacked || _storyState.GuardianDefeated || _story is null)
        {
            return;
        }

        if (_duelNode < 0 || _duelNode >= _story.CoreArguments.Count)
        {
            return;
        }

        var node = _story.CoreArguments[_duelNode];
        if (intent.ChoiceIndex < 0 || intent.ChoiceIndex >= node.Choices.Count)
        {
            return;
        }

        var choice = node.Choices[intent.ChoiceIndex];
        if (!choice.Correct)
        {
            // Dismissed — re-present the same node with the core's rebuttal to that pick.
            BroadcastDuelNode(choice.ResponseKey);
            return;
        }

        _duelNode++;
        if (_duelNode >= _story.CoreArguments.Count)
        {
            WinDuel(choice.ResponseKey);
            return;
        }

        BroadcastDuelNode(choice.ResponseKey);
    }

    /// <summary>The core is argued into shutdown: speak the resolution line, broadcast the won-duel message and
    /// pacify the galaxy (<see cref="MarkGuardianDefeated"/>). One-way per save.</summary>
    private void WinDuel(string finalResponseKey = "")
    {
        if (_storyState.GuardianDefeated)
        {
            return;
        }

        BroadcastToJoined(new CoreDialogueMessage { Node = _duelNode, ResponseKey = finalResponseKey, Won = true });
        SpeakVegaLineToAll("story.vega.finale_resolved");
        MarkGuardianDefeated();
    }

    private void BroadcastDuelNode(string responseKey)
    {
        if (_story is null || _duelNode < 0 || _duelNode >= _story.CoreArguments.Count)
        {
            return;
        }

        var node = _story.CoreArguments[_duelNode];
        BroadcastToJoined(new CoreDialogueMessage
        {
            Node = _duelNode,
            PromptKey = node.PromptKey,
            ChoiceKeys = node.Choices.Select(c => c.TextKey).ToArray(),
            ResponseKey = responseKey,
            Won = false,
        });
    }

    // ---------------- Helpers ----------------

    private void BroadcastToJoined(object message)
    {
        foreach (var session in _sessions.Values.Where(s => s.Joined))
        {
            Send(session, message);
        }
    }

    private void SpeakVegaLineToAll(string textKey)
    {
        foreach (var session in _sessions.Values.Where(s => s.Joined))
        {
            SendVegaLine(session, textKey, 2); // ShipAiLine kind 2 = memory/story
        }
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test/inspection: whether the finale system has been revealed on the map.</summary>
    public bool IsGuardianSystemRevealedForTest => _storyState.GuardianSystemRevealed;

    /// <summary>Test/inspection: whether the core has been hacked open (the duel is active).</summary>
    public bool IsCoreHackedForTest => _coreHacked;

    /// <summary>Test/inspection: the current duel node index.</summary>
    public int DuelNodeForTest => _duelNode;

    /// <summary>Test hook: channel one core-hack tick as the given player (mirrors <see cref="CoreHackIntent"/>).</summary>
    public void CoreHackTickForTest(string playerId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is not null)
        {
            HandleCoreHack(session, new CoreHackIntent());
        }
    }

    /// <summary>Test hook: offer a rebuttal as the given player (mirrors <see cref="CoreDialogueChoiceIntent"/>).</summary>
    public void CoreDialogueChoiceForTest(string playerId, int choiceIndex)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is not null)
        {
            HandleCoreDialogueChoice(session, new CoreDialogueChoiceIntent { ChoiceIndex = choiceIndex });
        }
    }

    /// <summary>Test/inspection: the finale system has been added to the galaxy (appears on the star map).</summary>
    public bool GalaxyHasGuardianSystemForTest => _galaxy?.Systems.Any(s => s.Id == GuardianFinaleSystemId) ?? false;

    /// <summary>Test/inspection: the finale core body exists and is landable (has a planet type).</summary>
    public bool GuardianCoreIsLandableForTest
        => _galaxy?.FindBody(GuardianCoreBodyId) is { } b && !string.IsNullOrEmpty(b.PlanetType);

    /// <summary>Test hook: record a pre-finale return location for a player (mirrors a hyperjump into the system).</summary>
    public void RecordFinaleReturnForTest(string playerId, string bodyId) => _finaleReturn[playerId] = bodyId;

    /// <summary>Test hook: resolve the respawn home a player would get, applying the finale return rule.</summary>
    public string ResolveRespawnHomeForTest(string playerId, string shipHome) => ResolveRespawnHome(playerId, shipHome);

    /// <summary>Test/inspection: the Stage-1 gauntlet roster (hostile count + the toughest hull) built without a
    /// full flight setup — verifies the finale system fields an elite wave, not the ambient hostile spawn.</summary>
    public (int Count, float MaxHull) GuardianGauntletPreviewForTest()
    {
        var inst = new SpaceInstance { Id = GuardianCoreBodyId, Kind = "orbit" };
        SpawnGuardianGauntlet(inst);
        var hostiles = inst.Entities.Where(e => e.Hostile).ToList();
        return (hostiles.Count, hostiles.Count == 0 ? 0f : hostiles.Max(e => e.HullMax));
    }
}
