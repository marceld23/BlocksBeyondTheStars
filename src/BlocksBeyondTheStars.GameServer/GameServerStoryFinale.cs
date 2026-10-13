// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
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
        foreach (var session in StoryListeners())
        {
            SpeakGuardianRevealedTo(session);
        }

        BroadcastToJoined(new GuardianSystemRevealed { LabelKey = "story.vega.guardian_system" });
        BroadcastStarMap(); // the finale system now appears as a jump target on everyone's travel screen
    }

    /// <summary>Per-player key: this player has heard the Guardian-system reveal (#2460, B10). The line is the only
    /// one that names the jump generator, so whoever was not online for the reveal hears it on their next join.</summary>
    private string GuardianRevealHeardKey => $"story:{_story?.Id ?? "none"}:finale:revealed";

    /// <summary>The Guardian-system reveal line (also a Journal note, #2471).</summary>
    private const string GuardianRevealedLine = "story.vega.guardian_revealed";

    /// <summary>The line that the finale was won (also a Journal note, #2471).</summary>
    private const string FinaleResolvedLine = "story.vega.finale_resolved";

    /// <summary>Speaks the Guardian-system reveal to one player, once (tracked by <see cref="GuardianRevealHeardKey"/>). Held
    /// until the player's story track starts (#2470): the key is not written, and the track start speaks it (the finale is
    /// open, so it carries the jump-generator line the player needs).</summary>
    private void SpeakGuardianRevealedTo(PlayerSession session)
    {
        if (StoryTrackStarted(session) && session.State.Milestones.Add(GuardianRevealHeardKey))
        {
            SendVegaLine(session, GuardianRevealedLine, 2); // ShipAiLine kind 2 = memory/story
        }
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
        // finale area. Hyperjump + body lookup are by id; the map position matters only for the hyperspace
        // chart (#1603), which draws every system by MapX/MapY — see GuardianFinaleMapPosition.
        var (mapX, mapY) = GuardianFinaleMapPosition(_galaxy);
        var system = new StarSystem { Id = GuardianFinaleSystemId, Name = "Guardian Core", MapX = mapX, MapY = mapY };
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

    /// <summary>How far out past the home system the finale sits on the star map, in map units. The
    /// procedural systems fill a 1000² box and grown systems (#1123) ride out to ~1520 units from home,
    /// so this clears them all; the distance is raised further if a galaxy ever reaches beyond it.</summary>
    internal const float GuardianFinaleMapDistance = 1600f;

    /// <summary>Where the finale system goes on the star map (#1605): out PAST every other star, in the
    /// direction that points from the galaxy's centre away through the home system — so it reads as
    /// "beyond the frontier" the way VEGA sells it, and never sits among (or on top of) the ordinary
    /// stars now that the hyperspace chart draws them. Seed-stable: it is a pure function of the galaxy,
    /// which is itself regenerated from the seed on every start. A degenerate galaxy (home at the exact
    /// centre, or a single system) falls back to a fixed diagonal.</summary>
    internal static (float X, float Y) GuardianFinaleMapPosition(Galaxy galaxy)
    {
        var home = galaxy.Systems.FirstOrDefault(s => s.Id == "sys0") ?? galaxy.Systems.FirstOrDefault();
        if (home is null)
        {
            return (GuardianFinaleMapDistance, GuardianFinaleMapDistance);
        }

        float sumX = 0f, sumY = 0f, farthest = 0f;
        int count = 0;
        foreach (var s in galaxy.Systems)
        {
            if (s.Id == GuardianFinaleSystemId)
            {
                continue;
            }

            sumX += s.MapX;
            sumY += s.MapY;
            count++;
            float dx = s.MapX - home.MapX, dy = s.MapY - home.MapY;
            farthest = MathF.Max(farthest, MathF.Sqrt(dx * dx + dy * dy));
        }

        float dirX = home.MapX - sumX / Math.Max(1, count);
        float dirY = home.MapY - sumY / Math.Max(1, count);
        float len = MathF.Sqrt(dirX * dirX + dirY * dirY);
        if (len < 1f)
        {
            dirX = 0.7071f;
            dirY = 0.7071f;
            len = 1f;
        }

        float distance = MathF.Max(GuardianFinaleMapDistance, farthest + 200f);
        return (home.MapX + dirX / len * distance, home.MapY + dirY / len * distance);
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

    /// <summary>#2506: the Guardian system belongs to the running story — while the world has none (an admin switched the
    /// story off with the finale open) it is off the star map and no jump leads there. It stays in the galaxy, so a player
    /// who is there is never stranded, and it is back on the map when the story is switched on again.</summary>
    private bool GuardianSystemHidden(string? systemId)
        => !StoryActive && string.Equals(systemId, GuardianFinaleSystemId, System.StringComparison.Ordinal);

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

        int floorY = surfaceY - (CoreShaftDepth + 1); // 24 down; the column's floor cell (the centre) 23 below the surface
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
        int shaftZ = az + CoreShaftOffsetZ;
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

    /// <summary>#1830: the heart of the Guardian core — the 3×3 pedestal, the core column, its corner pillars and
    /// panes, from the plated floor to the pillar tops — cannot be mined. The column is a bare-hand light block and
    /// the chamber stamp is one-shot (<see cref="StampGuardianCoreChamber"/>), so a mined core never came back:
    /// "3: Ich kann ihn abbauen." The shell stays diggable — Route B (mining down through it) is a documented way
    /// in, and the hack is a channel, not a demolition.</summary>
    private bool IsGuardianCoreProtected(Vector3i pos)
    {
        var aw = _worlds.Active;
        if (!aw.HasCoreChamber)
        {
            return false;
        }

        var c = aw.CoreChamberCenter; // (ax, floorY + 1, az) — see the stamp
        int dx = WorldConstants.WrapDeltaX(pos.X - c.X, _world.Circumference); // int overload: the wrapped column delta
        int dz = pos.Z - c.Z;
        int dy = pos.Y - (c.Y - 1); // relative to the chamber floor level
        return dx >= -1 && dx <= 1 && dz >= -1 && dz <= 1 && dy >= -1 && dy <= 5;
    }

    /// <summary>Test seam (#1830): whether the active world protects the cell as part of the Guardian core.</summary>
    public bool IsGuardianCoreProtectedForTest(int x, int y, int z) => IsGuardianCoreProtected(new Vector3i(x, y, z));

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
        BroadcastToFinaleAudience(new CoreHackProgress { Progress = _coreHackProgress, Complete = complete });

        if (complete)
        {
            _coreHacked = true;
            BeginCoreDuel();
        }
    }

    // ---------------- Stage 4: the argument duel ----------------

    /// <summary>Opens the duel at the first node (or wins immediately if the pack scripts no duel). #2478: before the first
    /// node, the finale's players get their scene — the breach (the core wakes, framed in its chamber) on the core world,
    /// the duel banner everywhere else (the dialogue itself shows only on the core world).</summary>
    private void BeginCoreDuel()
    {
        _duelNode = 0;
        if (_story is null || _story.CoreArguments.Count == 0)
        {
            WinDuel();
            return;
        }

        var column = CoreColumnAnchor();
        foreach (var session in _sessions.Values.Where(s => s.Joined && !s.Spectating && InFinaleAudience(s)).ToList())
        {
            if (OnGuardianCoreWorld(session))
            {
                PlayFinaleCutscene(session, StoryFinaleStages.Breach, column);
            }
            else
            {
                PlayFinaleCutscene(session, StoryFinaleStages.Duel);
            }
        }

        BroadcastDuelNode(string.Empty);
    }

    /// <summary>Handles a rebuttal pick (P6 stage 4). A correct (contradiction) choice advances the duel — and,
    /// at the last node, shuts the core down; a wrong choice is dismissed and the same node is re-presented.
    /// The duel cannot be lost, only stalled (weapons can't end the core — only its own contradiction can). #2478 (B50,
    /// E28): only a player standing on the core world answers — the dialogue shows only there, and a pick from anywhere
    /// else (an older client's number keys, a player at the other end of the galaxy) is dropped.</summary>
    private void HandleCoreDialogueChoice(PlayerSession session, CoreDialogueChoiceIntent intent)
    {
        if (!StoryActive || !_coreHacked || _storyState.GuardianDefeated || _story is null || !OnGuardianCoreWorld(session))
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

    /// <summary>The core is argued into shutdown: broadcast the won-duel message, pacify the galaxy
    /// (<see cref="MarkGuardianDefeated"/>) and deliver the ending. One-way per save. Since #2470 the lines and the cinematic
    /// go to players whose story track has started; for the others (a newcomer still in the basic training) the ending
    /// waits for their track start. The achievement is everyone's aboard, as before. #2478: a client that plays cutscenes
    /// gets the stand-down set piece (VEGA's resolution line) and the ending on the director instead of the old cinematic
    /// (<see cref="DeliverResolution"/>).</summary>
    private void WinDuel(string finalResponseKey = "")
    {
        if (_storyState.GuardianDefeated)
        {
            return;
        }

        BroadcastToFinaleAudience(new CoreDialogueMessage
        {
            Node = _duelNode,
            NodeCount = _story?.CoreArguments.Count ?? 0,
            ResponseKey = finalResponseKey,
            Won = true,
        });
        MarkGuardianDefeated();

        // Everyone aboard for the resolution earns the finale achievement — the arc is shared, so is the win (#1102).
        // An invisible observer is not aboard (#2460, N1). The resolution line, the ending and — live only — the epilogue,
        // which enters the story log like a beat (#1124), go to whoever's story track has started.
        foreach (var session in StoryListeners().ToList())
        {
            GrantFinaleAchievement(session);
            if (StoryTrackStarted(session))
            {
                DeliverResolution(session, achievement: false, live: true);
            }
        }
    }

    /// <summary>The finale achievement, once per player (#1102; E23: also for whoever heard the arc out but missed the win —
    /// the counter guards against a second count when both paths meet).</summary>
    private void GrantFinaleAchievement(PlayerSession session)
    {
        if (!session.State.AchievementCounters.TryGetValue(AchievementCounters.StoryFinale, out int counted) || counted <= 0)
        {
            OnAchievementStoryFinale(session);
        }
    }

    /// <summary>The legacy "ending handled" key (#1124): written whenever the ending is sent, and — without sending — for a
    /// player who first meets the story after the win (#2460, N4), so an OLDER build does not play it at the door. Since
    /// #2470 the server itself decides by <see cref="ResolutionWatchedKey"/>, because this key no longer means "watched".</summary>
    private string ResolutionSeenKey => $"story:{_story?.Id ?? "none"}:resolved";

    /// <summary>The player watched the ending (#2470, gap of #2460): the cutscene ledger's key of the ending
    /// (<c>story:&lt;id&gt;:cut:resolution</c>) — stamped whenever the cinematic is sent; an ending owed to a player whose
    /// track starts late plays at that start.</summary>
    private string ResolutionWatchedKey => StoryPackKeys.CutsceneSeen(_story?.Id ?? StoryRegistry.NoneStoryId, ResolutionLedgerId);

    /// <summary>#2478: the ending's ledger id — the scene the pack binds to <see cref="StoryFinaleStages.Resolution"/>, else
    /// <see cref="StoryCutsceneIds.Resolution"/> (VEGA binds exactly that, so the key of #2470 stays the same).</summary>
    private string ResolutionLedgerId
    {
        get
        {
            string bound = FinaleCutsceneId(StoryFinaleStages.Resolution);
            return bound.Length > 0 ? bound : StoryCutsceneIds.Resolution;
        }
    }

    /// <summary>The per-player key of the arc's last beat (B12 in VEGA) — hearing it is what "was there for the
    /// story" means for the resolution catch-up (#2460, N4).</summary>
    private string? FinalBeatKey => _story is { Beats.Count: > 0 } d ? BeatMilestoneKey(d.Beats[d.Beats.Count - 1]) : null;

    /// <summary>Sends the resolution cinematic (#1124) and remembers that this player has seen it.</summary>
    private void SendStoryResolved(PlayerSession session)
    {
        Send(session, new StoryResolved
        {
            StoryNameKey = _story?.NameKey ?? string.Empty,
            EpilogueTitle = _story?.EpilogueTitle ?? string.Empty,
            EpilogueTitleKey = _story?.EpilogueTitleKey ?? string.Empty, // #2461: the localized title (additive)
            EpilogueTextKey = _story?.EpilogueTextKey ?? string.Empty,
        });
        bool changed = session.State.Milestones.Add(ResolutionSeenKey);
        changed |= session.State.Milestones.Add(ResolutionWatchedKey);
        if (changed)
        {
            _repo.SavePlayer(session.State);
        }
    }

    /// <summary>Join catch-up (#1124): a player whose story track has started (#2470) and who has not watched the ending
    /// gets it on their next join — the resolution line and the cinematic, and the finale achievement the others earned at
    /// the win only when they had heard the arc out (the last beat) before this join (#2460, E23). A player whose track has
    /// not started — a newcomer on a won world, an arcade guest still in the basic training — is not played the ending at
    /// the door (#2460, N4): it waits for their track start (<see cref="StartStoryTrack"/>), and the legacy key marks it
    /// handled for older builds. The Story tab can replay it whenever they like.</summary>
    private void SendStoryResolvedOnJoinIfMissed(PlayerSession session)
    {
        if (!_storyState.GuardianDefeated || session.Spectating)
        {
            return;
        }

        bool heardFinal = FinalBeatKey is { } finalBeat && session.Join.HadStoryKey(finalBeat);
        if (!heardFinal)
        {
            session.State.Milestones.Add(ResolutionSeenKey); // N4 for older builds; persists with the player on the next save
        }

        if (StoryTrackStarted(session))
        {
            DeliverResolution(session, achievement: heardFinal);
        }
    }

    /// <summary>
    /// The ending for one player, once (#1124, #2470, #2478): <paramref name="achievement"/> grants the finale achievement too
    /// (E23). A client that plays cutscenes, on a pack that binds the ending (<see cref="StoryFinaleStages.Resolution"/>),
    /// gets the stand-down set piece and the ending on the director — committed BEFORE the lines they show, so the client
    /// keeps those out of VEGA's panel (a scene queued behind the cooldown holds them); the ledger's <c>:sent</c> key then
    /// means "handled" and an unconfirmed ending comes back at the next join through the cutscene catch-up. Any other client
    /// (an older build, the built-in pack) gets the resolution line, the epilogue (<paramref name="live"/>: at the win, where
    /// it enters the story log like a beat) and then the old cinematic (<see cref="StoryResolved"/>), which takes the epilogue
    /// back out of the panel. Never both.
    /// </summary>
    private void DeliverResolution(PlayerSession session, bool achievement, bool live = false)
    {
        if (!_storyState.GuardianDefeated || session.Spectating || ResolutionHandled(session))
        {
            return;
        }

        if (achievement)
        {
            GrantFinaleAchievement(session);
        }

        string epilogue = live ? _story?.EpilogueTextKey ?? string.Empty : string.Empty;
        if (ResolutionOnDirector(session))
        {
            PlayFinaleCutscene(session, StoryFinaleStages.Standdown); // the stand-down first (owed to whoever missed the win)
            PlayFinaleCutscene(session, StoryFinaleStages.Resolution); // chained to it: the ending follows at once
            SendVegaLine(session, FinaleResolvedLine, 2);
            if (epilogue.Length > 0)
            {
                SendVegaLine(session, epilogue, 2);
            }

            if (session.State.Milestones.Add(ResolutionSeenKey)) // an older build of this save must not replay it at the door
            {
                _repo.SavePlayer(session.State);
            }

            return;
        }

        SendVegaLine(session, FinaleResolvedLine, 2);
        if (epilogue.Length > 0)
        {
            SendVegaLine(session, epilogue, 2);
        }

        SendStoryResolved(session);
    }

    /// <summary>#2478: whether the player's ending is handled — watched (the ledger's seen key, also stamped by the old
    /// cinematic) or committed to their director (the <c>:sent</c> key; the join catch-up re-offers it until confirmed).</summary>
    private bool ResolutionHandled(PlayerSession session)
    {
        var m = session.State.Milestones;
        return m.Contains(ResolutionWatchedKey)
               || (_story is not null && m.Contains(StoryPackKeys.CutsceneSent(_story.Id, ResolutionLedgerId)));
    }

    /// <summary>#2478: the ending plays on the player's cutscene director: the client plays cutscenes and the pack binds a
    /// defined ending scene (the built-in fallback binds one but defines none — its players keep the old cinematic).</summary>
    private bool ResolutionOnDirector(PlayerSession session)
        => session.SupportsCutscenes && _story is not null
           && _story.FindCutscene(FinaleCutsceneId(StoryFinaleStages.Resolution)) is { Template: false };

    /// <summary>Story tab "watch the ending" (#1124): replays the resolution — only once the story IS
    /// resolved, so the button can never spoil anything. #2478: on the director (a replay, no ledger change) for a client
    /// that plays it there, else the old cinematic. #2497: the director gets the whole ending — the stand-down, then the
    /// ending (the client plays requested replays first-in, first-out) — so the Journal lists the ending once, as this
    /// button, and leaves both scenes out of "Watch again".</summary>
    private void HandleRequestStoryResolution(PlayerSession session)
    {
        if (!_storyState.GuardianDefeated)
        {
            return;
        }

        if (ResolutionOnDirector(session) && _story is not null)
        {
            foreach (string stage in new[] { StoryFinaleStages.Standdown, StoryFinaleStages.Resolution })
            {
                string id = FinaleCutsceneId(stage);
                if (id.Length > 0 && _story.FindCutscene(id) is { Template: false } def)
                {
                    PlayCutscene(session, _story.Id, id, CutsceneArgOf(session, _story, def, id), replay: true);
                }
            }

            return;
        }

        SendStoryResolved(session);
    }

    private void BroadcastDuelNode(string responseKey)
    {
        if (DuelNodeMessage(responseKey) is { } message)
        {
            BroadcastToFinaleAudience(message);
        }
    }

    /// <summary>The current duel node as the clients get it (#2478: with the node count and the core column for the dialogue's
    /// camera), or null when no duel node is open.</summary>
    private CoreDialogueMessage? DuelNodeMessage(string responseKey)
    {
        if (_story is null || _duelNode < 0 || _duelNode >= _story.CoreArguments.Count)
        {
            return null;
        }

        var node = _story.CoreArguments[_duelNode];
        var column = CoreColumnAnchor();
        return new CoreDialogueMessage
        {
            Node = _duelNode,
            NodeCount = _story.CoreArguments.Count,
            PromptKey = node.PromptKey,
            ChoiceKeys = node.Choices.Select(c => c.TextKey).ToArray(),
            ResponseKey = responseKey,
            Won = false,
            HasAnchor = column.HasValue,
            AnchorX = column?.X ?? 0f,
            AnchorY = column?.Y ?? 0f,
            AnchorZ = column?.Z ?? 0f,
        };
    }

    /// <summary>#2478: the open duel's current node for one player who just came to it — a join or a landing in the Guardian
    /// system while the duel runs. The nodes are only broadcast when they change, so a player who relogged mid-duel never saw
    /// the dialogue again (and alone on the core, nobody could answer it any more).</summary>
    private void SendDuelNodeTo(PlayerSession session)
    {
        if (!_coreHacked || _storyState.GuardianDefeated || !session.Joined || !InFinaleAudience(session)
            || DuelNodeMessage(string.Empty) is not { } message)
        {
            return;
        }

        Send(session, message);
    }

    // ---------------- #2478: the finale set pieces ----------------

    /// <summary>The cutscene the active pack binds to a finale stage (<see cref="StoryFinaleStages"/>), or "".</summary>
    private string FinaleCutsceneId(string stage)
        => _story?.Finale?.Cutscenes is { } bindings && bindings.TryGetValue(stage, out var id) && !string.IsNullOrEmpty(id) ? id : string.Empty;

    /// <summary>Plays the scene bound to a finale stage for one player (#2478) — once by the cutscene ledger, only once their
    /// story track has started, never for an observer; <paramref name="anchor"/> rides along as the <c>core</c> anchor (the
    /// shaft mouth for the descent, the core column for the breach). Returns whether it was sent or queued.</summary>
    private bool PlayFinaleCutscene(PlayerSession session, string stage, Vector3f? anchor = null)
    {
        var def = _story;
        string id = FinaleCutsceneId(stage);
        if (def is null || id.Length == 0 || !StoryTrackStarted(session))
        {
            return false;
        }

        if (def.FindCutscene(id) is not { Template: false } scene)
        {
            return PlayCutscene(session, def.Id, id); // not defined (the built-in pack): logs once, plays nothing
        }

        return PlayCutscene(session, def.Id, scene.Id, CutsceneArgOf(session, def, scene, scene.Id), anchor);
    }

    /// <summary>#2478 (B50): whether the player stands on the Guardian core world — landed there, not flying over it (a pilot
    /// in the finale system's flight is anchored on the same body).</summary>
    private bool OnGuardianCoreWorld(PlayerSession session)
        => session.CurrentLocationId == GuardianCoreBodyId && !InSpace(session.State.PlayerId);

    /// <summary>The loaded world that holds the Guardian core chamber, or null (the core world is not loaded).</summary>
    private LoadedWorld? CoreChamberWorld()
    {
        foreach (var w in _worlds.Loaded)
        {
            if (w.HasCoreChamber && w.LocationId == GuardianCoreBodyId)
            {
                return w;
            }
        }

        return null;
    }

    /// <summary>The core column on the core world — the centre of its floor cell (the chamber stamp's
    /// <see cref="LoadedWorld.CoreChamberCenter"/>) — the breach's and the duel dialogue's camera anchor; null while the core
    /// world is not loaded.</summary>
    private Vector3f? CoreColumnAnchor()
        => CoreChamberWorld() is { } w
            ? new Vector3f(w.CoreChamberCenter.X + 0.5f, w.CoreChamberCenter.Y, w.CoreChamberCenter.Z + 0.5f)
            : null;

    /// <summary>The mouth of the aperture shaft above the chamber — the descent's camera anchor. The stamp
    /// (<see cref="StampGuardianCoreChamber"/>) puts the column's floor cell 23 blocks under the surface it measured at the
    /// chamber's centre and carves the shaft 3 blocks towards +Z up to that surface.</summary>
    private Vector3f? CoreShaftAnchor()
        => CoreChamberWorld() is { } w
            ? new Vector3f(w.CoreChamberCenter.X + 0.5f, w.CoreChamberCenter.Y + CoreShaftDepth, w.CoreChamberCenter.Z + CoreShaftOffsetZ + 0.5f)
            : null;

    /// <summary>How far the surface lies above the column's floor cell (the stamp: floor 24 below the surface, the column one
    /// above the floor).</summary>
    private const int CoreShaftDepth = 23;

    /// <summary>How far the shaft sits from the column towards +Z (the stamp's <c>shaftZ = az + 3</c>).</summary>
    private const int CoreShaftOffsetZ = 3;

    // ---------------- Helpers ----------------

    private void BroadcastToJoined(object message)
    {
        foreach (var session in _sessions.Values.Where(s => s.Joined))
        {
            Send(session, message);
        }
    }

    /// <summary>The finale's live broadcasts (the hack meter, the duel nodes) go to the players who are part of it (#2470):
    /// whoever stands in the Guardian system or whose story track has started — and observers (staff see everything). A
    /// newcomer in the middle of the basic training on the other side of the galaxy is not handed the core's arguments.</summary>
    private void BroadcastToFinaleAudience(object message)
    {
        foreach (var session in _sessions.Values.Where(s => s.Joined && InFinaleAudience(s)))
        {
            Send(session, message);
        }
    }

    private bool InFinaleAudience(PlayerSession session)
        => session.Spectating || IsGuardianSystemLocation(session.CurrentLocationId) || StoryTrackStarted(session);

    /// <summary>Speaks a world-story line (Kind 2) to every story listener whose track has started (#2470: the others hold
    /// it — the finale's resolution and epilogue reach them with the ending at their track start, a relay insight through
    /// their log).</summary>
    private void SpeakStoryLineToStarted(string textKey)
    {
        foreach (var session in StoryListeners())
        {
            if (StoryTrackStarted(session))
            {
                SendVegaLine(session, textKey, 2); // ShipAiLine kind 2 = memory/story
            }
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

    /// <summary>Test seam (#2478): the player's flight warps into the Guardian system (the in-flight hyperjump's last step,
    /// past the jump-generator gate) — the arrival card's trigger.</summary>
    internal void JumpIntoGuardianSystemForTest(PlayerSession session)
    {
        EnsureGuardianSystemInGalaxy();
        var system = _galaxy!.Systems.First(s => s.Id == GuardianFinaleSystemId);
        JumpFlightToSystem(session, system, system.Bodies[0]);
    }

    /// <summary>Test seam (#2478): the core column's anchor (the breach's and the dialogue's camera), or null.</summary>
    internal Vector3f? CoreColumnAnchorForTest => CoreColumnAnchor();

    /// <summary>Test seam (#2478): the shaft mouth's anchor (the descent's camera), or null.</summary>
    internal Vector3f? CoreShaftAnchorForTest => CoreShaftAnchor();

    /// <summary>Test seam (#2478, B51): the live planet machines of a loaded world (-1 when it is not loaded).</summary>
    internal int PlanetEnemyCountOnForTest(string locationId)
        => _worlds.Find(locationId) is { } world ? world.PlanetEnemies.Count : -1;

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
