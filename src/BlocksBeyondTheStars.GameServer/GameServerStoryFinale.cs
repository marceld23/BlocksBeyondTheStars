using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
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

        // The finale id is RESERVED: the procedural generator only emits "sys{i}" systems / "sys{i}-…" bodies,
        // so it can never produce this system (proven by UniverseTests) — the random galaxy never spawns the
        // finale area. Hyperjump + body lookup are by id, and the client never renders systems by MapX/MapY, so
        // the map position is purely nominal and cannot clash with a procedural system either.
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

    // ---------------- Stage 2: the inner-core chamber + its two routes ----------------

    /// <summary>How close to the core terminal the player must be for the breach to channel (chamber-sized).</summary>
    private const float CoreChamberReach = 7f;

    /// <summary>Stamps the buried Guardian-core chamber on the finale body (idempotent per world). The chamber
    /// sits ~24 blocks down with an iron shell and a glowing red core column at its centre (the terminal you
    /// breach). Two routes reach it: <b>Route A</b> drops down a pre-carved 3×3 <b>aperture shaft</b> from the
    /// surface (ringed by plating so the maw is visible); <b>Route B</b> simply mines down through the shell.
    /// The finale body carries no other structures (see the per-world init), so nothing collides with it.</summary>
    private void StampGuardianCoreChamber()
    {
        var aw = _worlds.Active;
        if (aw.GuardianCoreStamped)
        {
            return;
        }

        aw.GuardianCoreStamped = true;
        var planet = _world.Planet;
        if (planet.Void)
        {
            return;
        }

        var shell = (_content.GetBlock("iron_wall") ?? _content.GetBlock("deepslate") ?? _content.GetBlock("stone"))!.NumericId;
        var core = _content.GetBlock("light_red")?.NumericId ?? BlockId.Air;
        var floorBlk = _content.GetBlock("steel_floor")?.NumericId ?? shell;
        var frame = _content.GetBlock("metal_panel")?.NumericId ?? shell;
        var window = _content.GetBlock("glass")?.NumericId ?? BlockId.Air;

        int ax = WorldConstants.WrapX(48, _world.Circumference);
        int az = 24;
        int surfaceY = _generator.SurfaceHeight(planet, ax, az);
        if (surfaceY < 40)
        {
            surfaceY = 64; // the finale core must bury cleanly even over low terrain
        }

        int floorY = surfaceY - 24;
        aw.CoreChamberCenter = new Vector3i(ax, floorY + 1, az);
        aw.HasCoreChamber = true;

        // Chamber: an 11×11 outer shell (9×9 inside), 6 air high — the inner core, walled in iron with a
        // plated floor.
        const int R = 5;
        for (int dx = -R; dx <= R; dx++)
            for (int dz = -R; dz <= R; dz++)
                for (int dy = -1; dy <= 6; dy++)
                {
                    var p = new Vector3i(WorldConstants.WrapX(ax + dx, _world.Circumference), floorY + dy, az + dz);
                    bool wall = dx == -R || dx == R || dz == -R || dz == R;
                    if (dy == -1)
                    {
                        _world.SetBlock(p, floorBlk); // plated walkable floor
                    }
                    else if (dy == 6 || wall)
                    {
                        _world.SetBlock(p, shell);
                    }
                    else
                    {
                        _world.SetBlock(p, BlockId.Air);
                    }
                }

        // Glow strips: red lights set into the middle of each wall at eye level.
        if (!core.IsAir)
        {
            foreach (var (wx, wz) in new[] { (-R, 0), (R, 0), (0, -R), (0, R) })
            {
                _world.SetBlock(new Vector3i(WorldConstants.WrapX(ax + wx, _world.Circumference), floorY + 2, az + wz), core);
            }
        }

        // The dormant core: a glowing red heart on a plated pedestal, framed by four metal pillars and glass
        // windows — the terminal the player breaches (centre of the chamber).
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                _world.SetBlock(new Vector3i(WorldConstants.WrapX(ax + dx, _world.Circumference), floorY, az + dz), frame);
            }

        if (!core.IsAir)
        {
            for (int dy = 1; dy <= 4; dy++)
            {
                _world.SetBlock(new Vector3i(ax, floorY + dy, az), core);
            }
        }

        foreach (var (cx, cz) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
        {
            for (int dy = 1; dy <= 5; dy++)
            {
                _world.SetBlock(new Vector3i(WorldConstants.WrapX(ax + cx, _world.Circumference), floorY + dy, az + cz), frame);
            }
        }

        if (!window.IsAir)
        {
            foreach (var (ex, ez) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
            {
                for (int dy = 2; dy <= 3; dy++)
                {
                    _world.SetBlock(new Vector3i(WorldConstants.WrapX(ax + ex, _world.Circumference), floorY + dy, az + ez), window);
                }
            }
        }

        // Route A — the aperture: a 3×3 open shaft from the surface down into the chamber, offset toward the
        // +Z wall so you drop onto open floor (not onto the core) and walk in to the heart.
        int shaftZ = az + 3;
        for (int dy = floorY + 6; dy <= surfaceY + 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    _world.SetBlock(new Vector3i(WorldConstants.WrapX(ax + dx, _world.Circumference), dy, shaftZ + dz), BlockId.Air);
                }

        // A low ring of plating around the shaft mouth so the opening reads as the core's maw from the surface.
        for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
            {
                if (System.Math.Abs(dx) != 2 && System.Math.Abs(dz) != 2)
                {
                    continue;
                }

                int rx = WorldConstants.WrapX(ax + dx, _world.Circumference);
                int ry = _generator.SurfaceHeight(planet, rx, shaftZ + dz);
                _world.SetBlock(new Vector3i(rx, ry + 1, shaftZ + dz), shell);
                _world.SetBlock(new Vector3i(rx, ry + 2, shaftZ + dz), shell);
            }

        _log.Info($"Stamped the Guardian core chamber on '{_world.LocationId}' at ({ax},{floorY},{az}).");
    }

    /// <summary>True once the player has reached the inner-core chamber on the finale body (within
    /// <see cref="CoreChamberReach"/> of the terminal). On any other world (no chamber) this defers to the
    /// system gate, so it never blocks the hack where a chamber was never stamped.</summary>
    private bool IsAtCoreChamber(PlayerSession session) => IsWithinCoreChamber(session.State.Position);

    private bool IsWithinCoreChamber(Vector3f pos)
    {
        var aw = _worlds.Active;
        if (!aw.HasCoreChamber)
        {
            return true; // not on the finale body → the system gate already governs
        }

        var c = aw.CoreChamberCenter;
        double dx = WorldConstants.WrapDeltaX(pos.X - c.X, _world.Circumference);
        double dy = pos.Y - c.Y;
        double dz = pos.Z - c.Z;
        return dx * dx + dy * dy + dz * dz <= (double)CoreChamberReach * CoreChamberReach;
    }

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

    /// <summary>Admin QA: skip the cross-galaxy hyperjump AND the orbital gauntlet and set the player down right
    /// inside the Guardian core chamber (revealing the finale first if needed). The hack + duel then play out
    /// normally from the terminal. No-op when no story is active.</summary>
    public void AdminGotoCore(PlayerSession session)
    {
        if (_story is null)
        {
            return;
        }

        if (!_storyState.GuardianSystemRevealed)
        {
            AdminRevealFinale(); // drive the arc to completion so the finale system exists + is on the map
        }

        EnsureGuardianSystemInGalaxy(); // defensive: the finale body must exist before we land on it

        // Land straight on the core body (bypassing the jump-generator gate). This goes through the normal
        // landing path — which stamps the chamber via LoadWorld — but skips the space instance, so no gauntlet.
        HandleTravel(session, new TravelIntent { DestinationBodyId = GuardianCoreBodyId }, quickTravel: false, adminBypass: true);

        // Drop the player from the surface pad down into the chamber, a couple of blocks off the core terminal
        // (on open floor, within the breach reach) so the hack can channel immediately.
        var aw = _worlds.Active;
        if (aw.HasCoreChamber && session.CurrentLocationId == GuardianCoreBodyId)
        {
            var c = aw.CoreChamberCenter;
            session.State.Position = new Vector3f(c.X + 2.5f, c.Y + 0.5f, c.Z + 2.5f);
            session.State.RespawnPoint = session.State.Position;
            session.SentChunks.Clear();
            SendPlayerState(session);
        }
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

        // Anti-cheat / correctness: the breach only channels while the player is actually in the Guardian system.
        if (!IsGuardianSystemLocation(session.CurrentLocationId))
        {
            return;
        }

        // Stage 2: you must have REACHED the inner core chamber — down the aperture shaft or by digging to it.
        if (!IsAtCoreChamber(session))
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

    /// <summary>Test hook: load the Guardian core world (stamping its chamber) and return the terminal centre.</summary>
    public Vector3i LoadGuardianCoreForTest()
    {
        EnsureGuardianSystemInGalaxy();
        LoadWorld(GuardianCorePlanetType(), GuardianCoreBodyId);
        return _worlds.Active.CoreChamberCenter;
    }

    /// <summary>Test/inspection: the active world has a stamped Guardian-core chamber.</summary>
    public bool HasCoreChamberForTest => _worlds.Active.HasCoreChamber;

    /// <summary>Test hook: whether a position is within breach range of the core terminal on the active world.</summary>
    public bool IsWithinCoreChamberForTest(Vector3f pos) => IsWithinCoreChamber(pos);

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
