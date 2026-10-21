// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Weather;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The admin commands (<see cref="AdminCommandIntent"/>, #2886): one table of verbs instead of one long switch. Three
/// gates stand in front of a command, in this order: the role (a world admin, a fleet admin, or the owner of a base on
/// this body asking <c>/basewalls</c>); then, for <c>/spectate</c> and <c>/goto</c>, the fleet admin
/// (<c>observer_reserved</c>); then, for the cheat commands, the world's cheat rule (<c>Rules.CheatsAllowed</c>,
/// <c>cheats_disabled</c>). An unknown verb answers <c>cheats_disabled</c> on a world without cheats and
/// <c>unknown_cmd</c> on one with them — the old switch reached its default only past the cheat gate, and the table keeps
/// that answer. A new command is one line in <see cref="AdminCommandTable"/> (plus a method here when its body is more
/// than one call).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The gate an admin command sits behind once the caller has passed the role gate.</summary>
    internal enum AdminCommandGate
    {
        /// <summary>The role is the gate (moderation, world management, read-only reports) — open on worlds without cheats.</summary>
        Role,

        /// <summary>Fleet admins only: the command reaches into worlds other people own.</summary>
        FleetAdmin,

        /// <summary>A cheat: only where the world's rules allow cheats.</summary>
        Cheats,
    }

    /// <summary>One admin verb: its gate and what it runs (the server comes in as the first argument).</summary>
    private readonly record struct AdminCommandEntry(AdminCommandGate Gate, Action<GameServer, PlayerSession, AdminCommandIntent> Run);

    /// <summary>
    /// Every admin verb, matched in any letter case, with its gate and what it runs. The lambdas are static and get the
    /// server as their first argument: the table is shared by every server in the process.
    /// </summary>
    private static readonly Dictionary<string, AdminCommandEntry> AdminCommandTable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ai_mission"] = new(AdminCommandGate.Role, static (s, session, cmd) => s.AdminCommandAiMission(session, cmd)),
        ["announce"] = new(AdminCommandGate.Role, static (s, session, cmd) => s.AdminCommandAnnounce(session, cmd)),
        ["kick"] = new(AdminCommandGate.Role, static (s, session, cmd) => s.AdminCommandKick(session, cmd)),
        ["silence"] = new(AdminCommandGate.Role, static (s, session, cmd) => s.AdminCommandSilence(session, cmd, lift: false)),
        ["unsilence"] = new(AdminCommandGate.Role, static (s, session, cmd) => s.AdminCommandSilence(session, cmd, lift: true)),
        ["schedule_restart"] = new(AdminCommandGate.Role, static (s, session, cmd) => s.AdminCommandScheduleRestart(session, cmd)),
        ["cancel_restart"] = new(AdminCommandGate.Role, static (s, session, _) => s.AdminCommandCancelRestart(session)),

        // Inspection (issues #487/#488) — deliberately above the CheatsAllowed gate, like `announce`. That world
        // option defaults to OFF and hosted worlds never enable it, so gating oversight on it would make these
        // dead on arrival exactly where they matter. The role is the gate.
        ["players"] = new(AdminCommandGate.Role, static (s, session, _) => s.AdminListPlayers(session)),
        ["builds"] = new(AdminCommandGate.Role, static (s, session, cmd) => s.AdminListBuilds(session, cmd.StringArg)),

        // Base-wall diagnostics (#1452) — a read-only report, not a cheat: the role is the gate.
        ["basewalls"] = new(AdminCommandGate.Role, static (s, session, _) => s.AdminBaseWalls(session)),

        // Creature-footing diagnostics (#1489) — feet vs real ground per animal nearby, same read-only idea.
        ["creatures"] = new(AdminCommandGate.Role, static (s, session, _) => s.AdminCreatures(session)),

        // Paint moderation (#821) — like kick/announce, moderation is not a cheat: the role is the gate.
        // #2783: a `break` fell through into the cheat gate and answered a done wipe with a refusal
        ["shapewipe"] = new(AdminCommandGate.Role, static (s, session, cmd) => s.AdminCustomShapeWipe(session, cmd.StringArg)),
        ["paintwipe"] = new(AdminCommandGate.Role, static (s, session, cmd) => s.AdminPaintWipe(session, cmd.StringArg)),

        // #1958: world texture moderation — the role is the gate, like the two above
        ["texturewipe"] = new(AdminCommandGate.Role, static (s, session, cmd) => s.AdminWorldTextureWipe(session, cmd.StringArg)),
        ["where"] = new(AdminCommandGate.Role, static (s, session, cmd) => s.AdminWhere(session, cmd.StringArg ?? cmd.TargetPlayer)),

        // Per-player mode override (#1121) — world management, not a cheat: family worlds keep
        // AdminCheats off, and exactly there a parent needs to hand the kid Creative. The role is the gate.
        ["set_mode"] = new(AdminCommandGate.Role, static (s, session, cmd) => s.AdminSetPlayerMode(session, cmd.TargetPlayer, cmd.StringArg)),

        // The whole world's mode (#1927) — the same kind of world management as set_mode above.
        ["set_world_mode"] = new(AdminCommandGate.Role, static (s, session, cmd) => s.AdminSetWorldMode(session, cmd.StringArg)),

        // Tutorial controls (#2466): skip / restart / a chapter for a player, the world's tutorial rule. Self-service
        // world management, not a cheat (d3 §6.8) — family worlds keep cheats off. The role is the gate.
        ["tutorial"] = new(AdminCommandGate.Role, static (s, session, cmd) => s.HandleTutorialAdmin(session, cmd)),

        // The story report (#2466: now with the player packs) — read-only, like the reports above.
        ["story_status"] = new(AdminCommandGate.Role, static (s, session, cmd) => s.AdminStoryStatus(session, cmd.TargetPlayer)),

        // Cutscene preview (#2468): plays a scene for the asking admin only, changes nothing — the role is the gate.
        ["cutscene"] = new(AdminCommandGate.Role, static (s, session, cmd) => s.AdminPreviewCutscene(session, cmd.StringArg)),

        // Observer mode + its jump command are fleet-admin only: they reach into worlds other people own,
        // so the owner of a single world must not be able to use them (issue #487).
        ["spectate"] = new(AdminCommandGate.FleetAdmin, static (s, session, cmd) => s.HandleSpectateCommand(session, cmd.StringArg)),
        ["goto"] = new(AdminCommandGate.FleetAdmin, static (s, session, cmd) => s.AdminGoto(session, cmd.StringArg)),

        // The cheats: past the world's cheat rule only.
        ["give_item"] = new(AdminCommandGate.Cheats, static (s, session, cmd) => s.AdminCommandGiveItem(session, cmd)),
        ["teleport_to_location"] = new(AdminCommandGate.Cheats, static (s, session, cmd) => s.AdminCommandTeleportToLocation(session, cmd)),

        // The named form of the same command ("/tp village2"). Same-body only and therefore the same
        // gate as the coordinate teleport it extends — cross-body jumping stays fleet-admin `/goto`.
        ["teleport_to_named"] = new(AdminCommandGate.Cheats, static (s, session, cmd) => s.AdminTeleportNamed(session, cmd.StringArg)),
        ["teleport_to_player"] = new(AdminCommandGate.Cheats, static (s, session, cmd) => s.AdminCommandTeleportToPlayer(session, cmd)),
        ["set_time"] = new(AdminCommandGate.Cheats, static (s, session, cmd) => s.AdminCommandSetTime(session, cmd)),
        ["set_weather"] = new(AdminCommandGate.Cheats, static (s, session, cmd) => s.AdminCommandSetWeather(session, cmd)),
        ["fly"] = new(AdminCommandGate.Cheats, static (s, session, _) => s.AdminCommandToggleFly(session)),
        ["godmode"] = new(AdminCommandGate.Cheats, static (s, session, _) => s.AdminCommandToggleGodMode(session)),

        // #1998: /giant colossus|sandworm — for testing
        ["summon_giant"] = new(AdminCommandGate.Cheats, static (s, session, cmd) => s.AdminSummonGiant(session, cmd.StringArg)),

        // #2009: /arachnid — for testing
        ["summon_arachnid"] = new(AdminCommandGate.Cheats, static (s, session, _) => s.AdminSummonArachnid(session)),

        // #2081: /biped — a begging herd of bipeds, for testing
        ["summon_biped"] = new(AdminCommandGate.Cheats, static (s, session, _) => s.AdminSummonBiped(session)),

        // #2646: /wanderer — a wanderer comes by right now, for testing
        ["summon_wanderer"] = new(AdminCommandGate.Cheats, static (s, session, _) => s.AdminSummonWanderer(session)),
        ["instant_build"] = new(AdminCommandGate.Cheats, static (s, session, _) => s.AdminCommandToggleInstantBuild(session)),

        // ---- Story QA (P8 telemetry): jump around the arc for testing ----
        ["advance_story"] = new(AdminCommandGate.Cheats, static (s, session, cmd) => s.AdminCommandAdvanceStory(session, cmd)),
        ["reveal_finale"] = new(AdminCommandGate.Cheats, static (s, session, _) => s.AdminCommandRevealFinale(session)),

        // ---- Finale QA: fit a ship module (e.g. the jump generator), reveal all lore, or drop into the core ----
        ["grant_module"] = new(AdminCommandGate.Cheats, static (s, session, cmd) => s.AdminCommandGrantModule(session, cmd)),
        ["reveal_lore"] = new(AdminCommandGate.Cheats, static (s, session, _) => s.AdminCommandRevealLore(session)),
        ["goto_core"] = new(AdminCommandGate.Cheats, static (s, session, _) => s.AdminCommandGotoCore(session)),
    };

    /// <summary>
    /// An admin command: the role gate, then the verb's entry in <see cref="AdminCommandTable"/> and its own gate, then
    /// the command. Every refusal is an <see cref="ActionRejected"/> for "admin".
    /// </summary>
    private void HandleAdminCommand(PlayerSession session, AdminCommandIntent cmd)
    {
        var p = session.State;

        // #1728: /basewalls is a read-only report about the caller's OWN walls, and the one question a builder
        // cannot otherwise answer. A player whose ring keeps letting animals in has exactly two possible
        // reasons — a gap somewhere, or a compound larger than the fill's 48-block reach — and no way to tell
        // them apart. She had to guess, and guessed at both. The owner of a base on this body may ask about it.
        bool ownsABaseHere = _bases.Any(b => b.Planet == _world.LocationId && b.OwnerId == p.PlayerId);
        bool basewallsForOwner = ownsABaseHere
            && string.Equals(cmd.Command, "basewalls", StringComparison.OrdinalIgnoreCase);

        // A fleet admin is an admin everywhere by definition — they are the operator of the installation, not
        // a guest on someone's world. Checked as a session flag rather than by writing PlayerRole.Admin into
        // the save, so the elevation never travels with an exported world (see ServerConfig.FleetAdminPlayers).
        if (!p.IsAdmin && !session.IsFleetAdmin && !basewallsForOwner)
        {
            Reject(session, "admin", "@srv.admin.not_admin");
            return;
        }

        // An unknown verb (or none) answers the way the old switch did: it fell past the role-gated commands, met the
        // cheat gate first and only then the "unknown" default — so a world without cheats says cheats_disabled.
        if (!AdminCommandTable.TryGetValue(cmd.Command ?? string.Empty, out var entry))
        {
            Reject(session, "admin", Rules.CheatsAllowed ? "@srv.admin.unknown_cmd" : "@srv.admin.cheats_disabled");
            return;
        }

        switch (entry.Gate)
        {
            case AdminCommandGate.FleetAdmin when !session.IsFleetAdmin:
                Reject(session, "admin", "@srv.admin.observer_reserved");
                return;

            case AdminCommandGate.Cheats when !Rules.CheatsAllowed:
                Reject(session, "admin", "@srv.admin.cheats_disabled");
                return;
        }

        entry.Run(this, session, cmd);
    }

    // Admin content tooling (not a cheat): AI mission generation.
    private void AdminCommandAiMission(PlayerSession session, AdminCommandIntent cmd)
    {
        var p = session.State;

        // Generation runs off the tick thread (the LLM call blocks up to the backend timeout); the
        // published/rejected result is pushed to the admin later by TickAiMissions. Acknowledge now.
        string ack = RequestAiMission(session, cmd.StringArg ?? string.Empty);
        Send(session, new ServerMessage { Text = ack });
        CheatLog(p, "requested an AI mission");
    }

    // Maintenance announcements (not cheats): the world admin may warn players before a restart even on
    // servers that have cheats disabled.
    private void AdminCommandAnnounce(PlayerSession session, AdminCommandIntent cmd)
    {
        var p = session.State;

        if (!EnqueueMaintenance(MaintenanceNotice.KindInfo, cmd.StringArg, -1))
        {
            Reject(session, "admin", "@srv.admin.usage_announce");
            return;
        }

        CheatLog(p, "posted a maintenance announcement");
    }

    // Kicking (not a cheat either): the world admin ends a session right now. Deliberately momentary —
    // a lasting block is the world owner's ban list on the portal, so there is exactly ONE ban store and
    // it lives where the world's identity does. Kicking yourself is refused: it reads as a bug report.
    private void AdminCommandKick(PlayerSession session, AdminCommandIntent cmd)
    {
        var p = session.State;

        string target = (cmd.StringArg ?? string.Empty).Trim();
        if (string.Equals(target, p.Name, StringComparison.OrdinalIgnoreCase))
        {
            Reject(session, "admin", "@srv.admin.kick_self");
            return;
        }

        if (!EnqueueKick(target, "@ui.kick.by_admin"))
        {
            Reject(session, "admin", "@srv.admin.usage_kick");
            return;
        }

        Send(session, new ServerMessage { Text = "@srv.admin.kick_sent:" + target });
        CheatLog(p, $"kicked {target}");
    }

    // Pausing someone's chat (#1223) — moderation, not a cheat, so the role is the gate like kick and
    // announce. Deliberately NOT called "mute": that word already belongs to the player's own local mute
    // list (#1209), which never reaches the server, and one word must not mean two different things
    // depending on who typed it.
    private void AdminCommandSilence(PlayerSession session, AdminCommandIntent cmd, bool lift)
    {
        var p = session.State;

        string target = (cmd.StringArg ?? string.Empty).Trim();
        string answer = ApplyChatSilence(session, target, cmd.IntArg, lift);
        if (answer.StartsWith("@srv.admin.usage", StringComparison.Ordinal)
            || answer.StartsWith("@srv.admin.silence_", StringComparison.Ordinal))
        {
            Reject(session, "admin", answer);
            return;
        }

        Send(session, new ServerMessage { Text = answer });
        CheatLog(p, lift ? $"un-silenced {target}" : $"silenced {target}");
    }

    private void AdminCommandScheduleRestart(PlayerSession session, AdminCommandIntent cmd)
    {
        var p = session.State;

        int minutes = cmd.IntArg;
        if (!EnqueueMaintenance(MaintenanceNotice.KindRestartCountdown, cmd.StringArg, minutes * 60))
        {
            Reject(session, "admin", Localize(session.Locale, "srv.admin.usage_restart")
                .Replace("{max}", MaxMaintenanceRestartMinutes.ToString()));
            return;
        }

        CheatLog(p, $"scheduled a restart in {minutes} min");
    }

    private void AdminCommandCancelRestart(PlayerSession session)
    {
        var p = session.State;

        EnqueueMaintenance(MaintenanceNotice.KindCancelled, null, -1);
        CheatLog(p, "cancelled the scheduled restart");
    }

    private void AdminCommandGiveItem(PlayerSession session, AdminCommandIntent cmd)
    {
        var p = session.State;

        if (_content.GetItem(cmd.StringArg ?? string.Empty) is null)
        {
            Reject(session, "admin", "@srv.admin.unknown_item");
            return;
        }

        // #2216: a sample, a seedling or a preparation is nothing without what its key carries (a
        // species, a compound) — the rule of the Sandbox catalog. A blank one is dead weight in the
        // pack, so it is not given; the same key with its payload is an item like any other.
        if (Shared.Bio.BioItems.NeedsPayload(cmd.StringArg))
        {
            Reject(session, "admin", "@srv.catalog.needs_content");
            return;
        }

        var target = FindSessionByName(cmd.TargetPlayer) ?? session;
        int amount = System.Math.Max(1, cmd.IntArg);
        // Resolve the TARGET's own ship, not the admin's cursor ship (`_ship`): a give to an
        // aboard-ship target must spill into that player's cargo, not the admin's.
        var targetShip = target.Ships.TryGetValue(target.ActiveShipId, out var ts) ? ts : _noShip;
        // #2220: Add returns what found no room. That used to be dropped without a word — and a give
        // that worked said nothing either. The admin is told what arrived and what did not fit.
        int given = amount - new MaterialPool(_content, target.State, targetShip).Add(cmd.StringArg!, amount);
        SendInventory(target);
        Send(session, new ServerMessage
        {
            Text = Localize(session.Locale, given == amount ? "srv.admin.gave" : "srv.admin.gave_partial")
                .Replace("{count}", amount.ToString())
                .Replace("{given}", given.ToString())
                .Replace("{item}", ItemDisplayName(session, cmd.StringArg!))
                .Replace("{player}", target.State.Name),
        });
        CheatLog(p, $"gave {given} of {amount} {cmd.StringArg} to {target.State.Name}");
    }

    private void AdminCommandTeleportToLocation(PlayerSession session, AdminCommandIntent cmd)
    {
        var p = session.State;

        p.Position = new Vector3f(cmd.X, cmd.Y, cmd.Z);
        // A plain PlayerStateUpdate position is ignored by the client and then reverted by its
        // client-authoritative move stream — every server-side teleport must go through the
        // RespawnNotice snap channel or it silently does nothing (#414 M7).
        Send(session, new RespawnNotice { X = p.Position.X, Y = p.Position.Y, Z = p.Position.Z, Reason = "@srv.tp.done" });
        SendPlayerState(session);
        CheatLog(p, $"teleported to ({cmd.X:0.#}, {cmd.Y:0.#}, {cmd.Z:0.#})");
    }

    private void AdminCommandTeleportToPlayer(PlayerSession session, AdminCommandIntent cmd)
    {
        var p = session.State;

        var target = FindSessionByName(cmd.TargetPlayer);
        if (target is null)
        {
            Reject(session, "admin", "@srv.admin.no_target");
            return;
        }

        // A position is only meaningful inside its own scene: while flying a space instance the
        // snap channel would fight the flight scene (same guard as /tp), and a target who is in
        // space or on another body has coordinates that mean nothing on the admin's body — copying
        // them raw dropped the admin at a spot picked from the wrong scene (#1030).
        if (InSpace(p.PlayerId))
        {
            Reject(session, "admin", "@srv.tp.no_surface_targets");
            return;
        }

        if (InSpace(target.State.PlayerId)
            || !string.Equals(target.CurrentLocationId, session.CurrentLocationId, System.StringComparison.Ordinal))
        {
            Reject(session, "admin", "@srv.tpp.not_here:" + target.State.Name);
            return;
        }

        // Beside the target, not inside their capsule (#1055).
        p.Position = LandingSpotNear(target.State.Position, target.State.Yaw);
        // Same snap-channel rule as teleport_to_location (#414 M7).
        Send(session, new RespawnNotice { X = p.Position.X, Y = p.Position.Y, Z = p.Position.Z, Reason = "@srv.tp.to:" + target.State.Name });
        SendPlayerState(session);
        UpdateAboard(session); // jumping onto/off a ship must flip the aboard state now, not on the next move (parity with /tp)
        CheatLog(p, $"teleported to player {target.State.Name}");
    }

    private void AdminCommandSetTime(PlayerSession session, AdminCommandIntent cmd)
    {
        var p = session.State;

        // #2220: the command used to write a field nothing read and still answered "time set". It sets this
        // world's clock now (a word, a clock time, an hour or a day fraction — the local time where the admin
        // stands) and refuses anything else. The clock is this world's alone, so the line goes to the players
        // whose sky changed — not to those on another body. A refusal goes out as an admin rejection, like every
        // other refused command: the client's chat puts those into the scrollback, where the command was typed,
        // while an "@srv." token in a plain message only flashes past as a toast.
        if (AdminSetTime(session, cmd.StringArg, out string timeLabel) is { } timeRefusal)
        {
            Reject(session, "admin", timeRefusal);
        }
        else
        {
            BroadcastToWorld(new ServerMessage { Text = "@srv.admin.time_set:" + timeLabel });
            CheatLog(p, $"set time to {timeLabel}");
        }
    }

    private void AdminCommandSetWeather(PlayerSession session, AdminCommandIntent cmd)
    {
        var p = session.State;

        // #2065: the command used to write a field nothing read. It forces the simulation's state now (a ladder
        // state or an event of the catalogue) and refuses anything the catalogue does not know. #2220: the name
        // is read the way it is typed — any letter case, and "cloudy" for "clouds". The forced weather is
        // this world's alone, so — like the clock above — the line goes to the players under this sky, not
        // to those on another body.
        if (WeatherCatalog.FindByName(cmd.StringArg) is { } weatherDef)
        {
            _planetWeatherMode = "dynamic";
            _sim.Force(weatherDef.Key);
            BroadcastToWorld(new ServerMessage { Text = "@srv.admin.weather_set:" + weatherDef.Key });
            CheatLog(p, $"set weather to {weatherDef.Key}");
        }
        else
        {
            // An admin rejection, so the answer stays readable in the chat: it is the line that names every
            // weather key, and the usage line points to it ("/setweather ?" lists all).
            Reject(session, "admin", "@srv.admin.weather_unknown");
        }
    }

    private void AdminCommandToggleFly(PlayerSession session)
    {
        var p = session.State;

        p.Fly = !p.Fly;
        Send(session, new ServerMessage { Text = p.Fly ? "@srv.admin.fly_on" : "@srv.admin.fly_off" });
        CheatLog(p, $"toggled fly to {p.Fly}");
    }

    private void AdminCommandToggleGodMode(PlayerSession session)
    {
        var p = session.State;

        p.GodMode = !p.GodMode;
        Send(session, new ServerMessage { Text = p.GodMode ? "@srv.admin.god_on" : "@srv.admin.god_off" });
        CheatLog(p, $"toggled god mode to {p.GodMode}");
    }

    private void AdminCommandToggleInstantBuild(PlayerSession session)
    {
        var p = session.State;

        p.InstantBuild = !p.InstantBuild;
        Send(session, new ServerMessage { Text = p.InstantBuild ? "@srv.admin.build_on" : "@srv.admin.build_off" });
        CheatLog(p, $"toggled instant build to {p.InstantBuild}");
    }

    private void AdminCommandAdvanceStory(PlayerSession session, AdminCommandIntent cmd)
    {
        var p = session.State;

        int beats = AdminAdvanceStory(cmd.IntArg);
        Send(session, new ServerMessage { Text = "@srv.admin.story_advanced:" + beats });
        CheatLog(p, $"advanced story by {System.Math.Max(1, cmd.IntArg)} (beats {beats})");
    }

    private void AdminCommandRevealFinale(PlayerSession session)
    {
        var p = session.State;

        AdminRevealFinale();
        Send(session, new ServerMessage
        {
            Text = _storyState.GuardianSystemRevealed
                ? "@srv.admin.finale_revealed"
                : "@srv.admin.no_story_finale",
        });
        CheatLog(p, "revealed the Guardian finale system");
    }

    private void AdminCommandGrantModule(PlayerSession session, AdminCommandIntent cmd)
    {
        var p = session.State;

        var key = cmd.StringArg ?? string.Empty;
        if (_ship is null)
        {
            Reject(session, "admin", "@srv.admin.no_ship");
            return;
        }

        if (_content.GetShipModule(key) is null)
        {
            Reject(session, "admin", "@srv.module.unknown");
            return;
        }

        if (!_ship.HasModule(key))
        {
            _ship.Modules.Add(key);
            ResizeCargo(_ship);
            RecomputeShipCombatStats();
            SaveFleet(session); // through the fleet, so the per-ship row and the legacy key agree (#848)
            SendShipCombatStatus(session);
            SendPlayerState(session);
        }

        Send(session, new ServerMessage { Text = "@srv.admin.module_fitted:" + key });
        CheatLog(p, $"fitted ship module {key}");
    }

    private void AdminCommandRevealLore(PlayerSession session)
    {
        var p = session.State;

        int n = AdminRevealAllLore(session);
        Send(session, new ServerMessage { Text = "@srv.admin.lore_revealed:" + n });
        CheatLog(p, "revealed all story lore");
    }

    private void AdminCommandGotoCore(PlayerSession session)
    {
        var p = session.State;

        AdminGotoCore(session);
        Send(session, new ServerMessage
        {
            Text = _storyState.GuardianSystemRevealed
                ? "@srv.admin.core_dropped"
                : "@srv.admin.no_story_core",
        });
        CheatLog(p, "teleported to the Guardian core chamber");
    }

    /// <summary>Test seam: every verb of <see cref="AdminCommandTable"/> with its gate.</summary>
    internal static IEnumerable<KeyValuePair<string, AdminCommandGate>> AdminCommandTableForTest()
        => AdminCommandTable.Select(kv => new KeyValuePair<string, AdminCommandGate>(kv.Key, kv.Value.Gate));
}
