// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// GameBootstrap's knowledge of the universe (#2912): star map, missions, rules, alliances, crew, markers, notes,
    /// planet scans, discoveries, explored maps and the wiki state.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        // Navigation, missions & rules (M23).
        public StarMapData StarMap { get; private set; }
        public MissionList Missions { get; private set; }
        public ServerRules Rules { get; private set; }

        /// <summary>The mission with this id from the last mission list (active first, then available), or null.</summary>
        private NetMission FindMission(string missionId)
        {
            if (Missions == null || string.IsNullOrEmpty(missionId))
            {
                return null;
            }

            foreach (var list in new[] { Missions.Active, Missions.Available })
            {
                if (list == null)
                {
                    continue;
                }

                foreach (var mission in list)
                {
                    if (mission != null && mission.Id == missionId)
                    {
                        return mission;
                    }
                }
            }

            return null;
        }

        /// <summary>This player's alliance roster (mutual allies + pending requests), for the Alliances menu tab.</summary>
        public AllianceList Alliances { get; private set; } = new AllianceList();

        /// <summary>This player's crew state (#1216): the crew they are in (empty CrewId = none) + open invites.</summary>
        public CrewList Crew { get; private set; } = new CrewList();

        /// <summary>Map markers + live pings visible on the current world (#1217): own + shared-by-allies/crew.
        /// Replaced wholesale by every <see cref="MarkerList"/>; cleared on a world switch.</summary>
        public NetMarker[] Markers { get; private set; } = System.Array.Empty<NetMarker>();

        /// <summary>This player's own notes (#1844), newest first. Replaced wholesale by every <see cref="NoteList"/>.</summary>
        public NetNote[] Notes { get; private set; } = System.Array.Empty<NetNote>();

        /// <summary>Bumped on every <see cref="NoteList"/> — the menu hashes THIS (plus the count) instead of the
        /// note text, so the open editor is only rebuilt when the server actually answered a save/delete.</summary>
        public int NotesVersion { get; private set; }

        /// <summary>The Instant Travel world option: when on, the travel screen may quick-travel anywhere.</summary>
        public bool InstantTravel => Rules?.InstantTravel ?? false;

        /// <summary>True if THIS player has landed on the body (a quick-travel target when Instant Travel is off).</summary>
        public bool HasLandedOn(string bodyId)
            => StarMap?.LandedBodyIds != null && System.Array.IndexOf(StarMap.LandedBodyIds, bodyId) >= 0;

        /// <summary>True if THIS player has entered the system (its bodies + mini map are revealed; else it is
        /// a single "hyperjump here" entry).</summary>
        public bool KnowsSystem(string systemId)
            => StarMap?.KnownSystemIds != null && System.Array.IndexOf(StarMap.KnownSystemIds, systemId) >= 0;

        /// <summary>True if THIS player has a commissioned space station orbiting the given host body.</summary>
        public bool HasMyStation(string bodyId)
            => StarMap?.MyStationBodyIds != null && System.Array.IndexOf(StarMap.MyStationBodyIds, bodyId) >= 0;

        /// <summary>The name of THIS player's base on the given body, or null if they have no base there.</summary>
        public string MyBaseName(string bodyId)
        {
            if (StarMap?.MyBases == null)
            {
                return null;
            }

            foreach (var b in StarMap.MyBases)
            {
                if (b.BodyId == bodyId)
                {
                    return b.Name;
                }
            }

            return null;
        }

        /// <summary>True if THIS player has founded a base on the given body.</summary>
        public bool HasMyBase(string bodyId) => MyBaseName(bodyId) != null;

        private StarMapData _shownLocMap;
        private string _shownLocFor, _shownLoc, _shownLocOriginal = string.Empty;

        /// <summary>#2952: <see cref="LocationName"/> as the player reads it — their own names for the body they are on
        /// and its system where they gave one (<see cref="PlaceNameDisplay.ShownLocation"/>). LocationName itself stays
        /// the generated line: it keys the world's flora colours and looks.</summary>
        public string ShownLocationName
        {
            get
            {
                if (!ReferenceEquals(_shownLocMap, StarMap) || _shownLocFor != LocationName)
                {
                    _shownLocMap = StarMap;
                    _shownLocFor = LocationName;
                    _shownLoc = PlaceNameDisplay.ShownLocation(StarMap, LocationName, out _shownLocOriginal);
                }

                return _shownLoc ?? string.Empty;
            }
        }

        /// <summary>#2952: the generated location line when <see cref="ShownLocationName"/> differs from it, else empty —
        /// shown small next to the player's own name, so friends still meet up on the place's real name.</summary>
        public string ShownLocationOriginal
        {
            get
            {
                _ = ShownLocationName;
                return _shownLocOriginal ?? string.Empty;
            }
        }

        /// <summary>#2952: the body with this id on the last star map, or null.</summary>
        public NetBody FindMapBody(string bodyId)
        {
            if (StarMap?.Systems == null || string.IsNullOrEmpty(bodyId))
            {
                return null;
            }

            foreach (var system in StarMap.Systems)
            {
                foreach (var body in system.Bodies ?? System.Array.Empty<NetBody>())
                {
                    if (body.Id == bodyId)
                    {
                        return body;
                    }
                }
            }

            return null;
        }

        /// <summary>#2140: the last planet-scanner report (any body) and a counter that moves with every report, so the
        /// open star map / Ship tab rebuilds the moment it arrives.</summary>
        public PlanetScanResult LastPlanetScan { get; private set; }
        public int PlanetScanVersion { get; private set; }

        /// <summary>Most recent handheld/ship scan readout for the HUD, and when it arrived (for auto-hide).</summary>
        public ScanResult LastScan { get; private set; }
        public float LastScanAt { get; private set; }

        /// <summary>#2247: <see cref="LastScan"/> came in while piloting — a ship-scanner readout. It was shown on the
        /// right-hand card, so the hand scanner's HUD panel must not replay it after landing.</summary>
        public bool LastScanFromShip { get; private set; }

        /// <summary>The player's first-scan ledger: <c>kind:key</c> → the display name captured at scan time
        /// (empty for entries scanned before the game recorded names). Mirrors the server's
        /// <c>PlayerState.Scanned</c>/<c>ScannedNames</c> and backs the Codex "Discoveries" chapter (#484) —
        /// before that the ledger never left the server, so a scan left no permanent record in the UI.</summary>
        public readonly System.Collections.Generic.Dictionary<string, string> Discoveries
            = new System.Collections.Generic.Dictionary<string, string>();

        /// <summary>Where each discovery was made (#1843): ledger key → (body name, system name) as the server
        /// recorded them at scan time. Entries without a known site — scanned before the game recorded sites,
        /// or somewhere the galaxy cannot place — are simply absent, and the Codex shows no location line for
        /// them. Cleared together with <see cref="Discoveries"/> on the join snapshot.</summary>
        public readonly System.Collections.Generic.Dictionary<string, (string Body, string System)> DiscoveryWhere
            = new System.Collections.Generic.Dictionary<string, (string Body, string System)>();

        /// <summary>The enemies this player has met (#2971) — the Codex's "Enemies" chapter fills their entries. Mirrors the
        /// server's <c>PlayerState.EnemiesMet</c>.</summary>
        public readonly System.Collections.Generic.HashSet<string> EnemiesMet = new System.Collections.Generic.HashSet<string>();

        /// <summary>Persisted explored-map cells per body (#1113), as the server sent them on arrival —
        /// the planet map draws these as "remembered" ground under its live fog. Keyed by body id so an
        /// out-of-order WorldReset can never show another body's fog.</summary>
        public readonly System.Collections.Generic.Dictionary<string, ExploredMapData> ExploredMaps
            = new System.Collections.Generic.Dictionary<string, ExploredMapData>();

        /// <summary>Explored cells seen live THIS session on the current body (cell indices, see
        /// <c>ExploredMap.CellIndex</c>) — chunks unload again behind the player, but a cell once streamed
        /// stays lifted on the map even before the server's next snapshot. Cleared on world change.</summary>
        public readonly System.Collections.Generic.HashSet<int> SessionExploredCells
            = new System.Collections.Generic.HashSet<int>();

        /// <summary>Pre-built JSON of the player's discovered systems/worlds + language for the in-game wiki's
        /// discovery-gated chapters. Built on the main thread (so the loopback content server can read this
        /// immutable string race-free) whenever the star map, language or unlocks change.</summary>
        public string WikiStateJson { get; private set; } = "{}";

        /// <summary>Current knowledge total, kept in sync via inventory updates (item 11 — shown in the trade UI).</summary>
        public int Knowledge { get; private set; }

        /// <summary>Rebuilds <see cref="WikiStateJson"/> from the current star map for the wiki's discovery-gated
        /// Systems &amp; Worlds chapters: only systems the player has entered and bodies they have landed on are
        /// included. Cheap; called on the main thread when the map/language/unlocks change.</summary>
        private void RebuildWikiState()
        {
            var sb = new System.Text.StringBuilder(512);
            sb.Append("{\"lang\":\"").Append(Localizer?.Locale.Code() ?? "en").Append("\",\"systems\":[");
            var systems = StarMap?.Systems ?? System.Array.Empty<NetStarSystem>();
            var knownSys = new System.Collections.Generic.HashSet<string>(StarMap?.KnownSystemIds ?? System.Array.Empty<string>());
            var landed = new System.Collections.Generic.HashSet<string>(StarMap?.LandedBodyIds ?? System.Array.Empty<string>());

            bool firstSys = true;
            var worlds = new System.Text.StringBuilder(512);
            bool firstWorld = true;
            foreach (var sys in systems)
            {
                if (sys == null || !knownSys.Contains(sys.Id)) continue;
                if (!firstSys) sb.Append(','); firstSys = false;
                sb.Append("{\"id\":\"").Append(JsonEscape(sys.Id)).Append("\",\"name\":\"").Append(JsonEscape(sys.Name)).Append("\"}");

                foreach (var body in sys.Bodies ?? System.Array.Empty<NetBody>())
                {
                    if (body == null || !landed.Contains(body.Id)) continue;
                    if (!firstWorld) worlds.Append(','); firstWorld = false;
                    worlds.Append("{\"id\":\"").Append(JsonEscape(body.Id))
                          .Append("\",\"name\":\"").Append(JsonEscape(body.Name))
                          .Append("\",\"type\":\"").Append(JsonEscape(body.PlanetType ?? string.Empty))
                          .Append("\",\"systemId\":\"").Append(JsonEscape(sys.Id))
                          .Append("\",\"systemName\":\"").Append(JsonEscape(sys.Name)).Append("\"}");
                }
            }

            sb.Append("],\"worlds\":[").Append(worlds).Append("]}");
            WikiStateJson = sb.ToString();
        }

        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new System.Text.StringBuilder(s.Length + 4);
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') { sb.Append('\\').Append(c); }
                else if (c == '\n') { sb.Append("\\n"); }
                else if (c >= ' ') { sb.Append(c); }
            }

            return sb.ToString();
        }

        /// <summary>Start's subscriptions for what the player knows: star map, explored maps, missions, alliances,
        /// crew, markers, notes, scans, discoveries and the server rules. Called once from <see cref="Start"/>,
        /// before the transport connects.</summary>
        private void WireKnowledge()
        {
            Network.StarMapReceived += m => { StarMap = m; RebuildWikiState(); };
            Network.ExploredMapReceived += m => { if (!string.IsNullOrEmpty(m.BodyId)) ExploredMaps[m.BodyId] = m; };
            Network.MissionsReceived += m => Missions = m;
            Network.AllianceListReceived += m => Alliances = m ?? new AllianceList();
            WirePvp(); // #2972: whom this player may hit, and duels
            Network.CrewListReceived += m => Crew = m ?? new CrewList();
            Network.CrewInviteReceived += m =>
            {
                string t = Localizer?.Get("ui.crew.invite_from") ?? "{name} invites you to “{crew}”";
                ShowMessage(t.Replace("{name}", m.FromName).Replace("{crew}", m.CrewName));
            };
            Network.MarkerListReceived += m => Markers = m?.Markers ?? System.Array.Empty<NetMarker>();
            Network.NoteListReceived += m =>
            {
                Notes = m?.Notes ?? System.Array.Empty<NetNote>();
                NotesVersion++;
            };
            Network.AllianceRequestReceived += m =>
            {
                string who = string.IsNullOrEmpty(m.RequesterName) ? m.RequesterId : m.RequesterName;
                ShowMessage((Localizer?.Get("ui.alliance.request_from") ?? "Alliance request from {name}").Replace("{name}", who));
            };
            Network.PlanetScanReceived += m =>
            {
                LastPlanetScan = m;
                PlanetScanVersion++;
                if (!string.IsNullOrEmpty(m.BodyId))
                {
                    PlanetOverviews[m.BodyId] = m; // #2239: kept for the Map tab
                }

                if (m.KnowledgeGained > 0)
                {
                    LastMessage = string.Format(Localizer?.Get("ui.overview.knowledge_gain") ?? "+{0} knowledge", m.KnowledgeGained);
                }

                if (OpenOverviewOnNextPlanetScan)
                {
                    OpenOverviewOnNextPlanetScan = false;
                    PlanetOverviewCard.Show(this, m); // #2239: the flight scanner read a planet — what awaits there
                    // #2247: the first card says once what the colours mean and where the report lives on — "once"
                    // counting only a line that came up (#2462, B31: muted, it waits for a later card; #2551: dropped
                    // unseen after waiting behind other lines, too).
                    if (Settings != null && !Settings.PlanetCardHintShown && m.Rows != null && m.Rows.Length > 0)
                    {
                        var settings = Settings;
                        Views.Vega?.SayLocal("vega.hint.planet_card", () =>
                        {
                            settings.PlanetCardHintShown = true;
                            settings.Save();
                        });
                    }
                }
            };
            Network.ScanResultReceived += m =>
            {
                LastScan = m;
                LastScanAt = Time.time;
                LastScanFromShip = SpaceViewActive && !InEva;
                if (LastScanFromShip)
                {
                    PlanetOverviewCard.ShowReadout(this, m); // #2247: the ship scanner's readouts open on the right
                }

                if (m.FirstTime && m.KnowledgeGained > 0)
                {
                    // Localized (#484) — this toast used to be an English literal in the German build too.
                    LastMessage = string.Format(
                        Localizer?.Get("ui.scan.knowledge_gain") ?? "+{0} knowledge ({1})",
                        m.KnowledgeGained,
                        m.KnowledgeTotal);
                }
            };
            Network.DiscoveryLogReceived += m =>
            {
                if (m.Full)
                {
                    Discoveries.Clear(); // the join snapshot replaces whatever a previous session left
                    DiscoveryWhere.Clear();
                }

                var entries = m.Entries ?? System.Array.Empty<string>();
                var names = m.Names ?? System.Array.Empty<string>();
                // Where each was found (#1843) — parallel arrays an older server leaves empty, so index-guard.
                var bodyNames = m.BodyNames ?? System.Array.Empty<string>();
                var systemNames = m.SystemNames ?? System.Array.Empty<string>();
                for (int i = 0; i < entries.Length; i++)
                {
                    Discoveries[entries[i]] = i < names.Length ? names[i] ?? string.Empty : string.Empty;

                    string bodyName = i < bodyNames.Length ? bodyNames[i] ?? string.Empty : string.Empty;
                    string systemName = i < systemNames.Length ? systemNames[i] ?? string.Empty : string.Empty;
                    if (bodyName.Length > 0 || systemName.Length > 0)
                    {
                        DiscoveryWhere[entries[i]] = (bodyName, systemName);
                    }
                }
            };
            Network.EnemyLogReceived += m =>
            {
                if (m.Full)
                {
                    EnemiesMet.Clear(); // the join snapshot replaces whatever a previous session left
                }

                foreach (var key in m.Keys ?? System.Array.Empty<string>())
                {
                    if (EnemiesMet.Add(key) && !m.Full && Content != null && Localizer != null)
                    {
                        // #2971: a first meeting says so — the Codex has a new page.
                        LastMessage = string.Format(Localizer.Get("ui.wiki.enemies.new"), Localizer.Get(Content.Enemy(key).NameKey));
                    }
                }
            };
            // #1071: rejections arrive as @srv.mission.* tokens — localize them instead of pushing the raw token at the player.
            // #2462 (B24): localized, named after the mission, and "complete" only on a real turn-in.
            Network.MissionResultReceived += m => LastMessage = m.Success
                ? MissionToast.Text(m, FindMission(m.MissionId), key => Localizer?.Get(key) ?? key, key => Localizer?.Has(key) ?? false)
                : ServerMessageText(m.Reason);
            Network.ServerRulesReceived += m => { Rules = m; LastMessage = $"Mode: {m.GameMode} · PvP: {m.Pvp}"; };
        }
    }
}
