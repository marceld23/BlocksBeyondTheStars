// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlocksBeyondTheStars.Shared.Configuration;

/// <summary>
/// Self-hosting server configuration (technical requirements §14). Loaded from
/// <c>config/server.json</c>; shared by the game server and the admin web UI, and
/// editable through the admin UI.
/// </summary>
public sealed class ServerConfig
{
    public const int DefaultGameplayPort = 31415;
    public const int DefaultAdminPort = 31416;

    public string ServerName { get; set; } = "Blocks Beyond the Stars Server";
    public string WorldName { get; set; } = "world_001";

    public int GameplayPort { get; set; } = DefaultGameplayPort;
    public int AdminPort { get; set; } = DefaultAdminPort;

    /// <summary>Also accept browser clients over WebSocket (on the gameplay port, TCP).</summary>
    public bool EnableWebSocket { get; set; }

    /// <summary>WebSocket bind host ("localhost"/LAN ip for safety, "+" for all interfaces).</summary>
    public string WebSocketBindAddress { get; set; } = "localhost";

    public int MaxPlayers { get; set; } = 12;
    public string ServerPassword { get; set; } = string.Empty;
    public bool WhitelistEnabled { get; set; }
    public List<string> Whitelist { get; set; } = new();
    public string AdminPassword { get; set; } = string.Empty;

    /// <summary>Player names granted the Admin role on join (the world creator becomes WorldAdmin).</summary>
    public List<string> AdminPlayers { get; set; } = new();

    /// <summary>Player names granted <b>fleet admin</b> on join — the operator of the hosting installation,
    /// as opposed to the owner of an individual world. Fleet admins are the only ones who may enter the
    /// invisible observer mode (issue #487), because that power reaches into worlds other people own.
    ///
    /// <para>Deliberately NOT a <see cref="Shared.State.PlayerRole"/> value: roles live in
    /// <see cref="Shared.State.PlayerState.Role"/> and are persisted in the save, and saves can be downloaded,
    /// edited and re-uploaded by players. A persisted fleet role would therefore travel into worlds the
    /// operator does not control. This list is config-only and re-evaluated on every join.</para></summary>
    public List<string> FleetAdminPlayers { get; set; } = new();

    public int AutoSaveIntervalMinutes { get; set; } = 5;

    /// <summary>Minutes of play between two rotating backups of the world (#2223); 0 = off. Play is real time
    /// with at least one player online, and the count is saved with the world — it carries across server
    /// restarts, so short sessions add up, and an idle server does not rotate its good copies away. A backup is
    /// a consistent copy of the save in its <c>backups/</c> folder (<c>auto_&lt;UTC time&gt;</c>), taken right
    /// after a full save and written on a background thread so the tick never waits for it. CLI
    /// <c>--backup-interval-minutes</c>, env <c>BBS_BACKUP_INTERVAL_MINUTES</c>.</summary>
    public int BackupIntervalMinutes { get; set; } = 60;

    /// <summary>How many rotating backups are kept (#2223): once more exist, the oldest are deleted, so the
    /// folder holds at most this many copies of the save per kind (<c>auto_</c> and <c>pre-remap_</c>). Clamped
    /// to 1..<see cref="BackupKeepCountCeiling"/>. Backups made by hand (admin UI, tools) are never deleted. A
    /// browser save keeps one <c>pre-remap_</c> copy whatever this says — it lives in the browser's storage.
    /// CLI <c>--backup-keep</c>, env <c>BBS_BACKUP_KEEP</c>.</summary>
    public int BackupKeepCount
    {
        get => _backupKeepCount;
        set => _backupKeepCount = Math.Clamp(value, 1, BackupKeepCountCeiling);
    }

    private int _backupKeepCount = 5;

    /// <summary>Upper bound for <see cref="BackupKeepCount"/> — the disk a save's backups may take stays bounded.</summary>
    public const int BackupKeepCountCeiling = 50;

    public int ViewDistanceChunks { get; set; } = 4;

    /// <summary>How many chunks the server streams to each player per tick. Raised from the historical hard-coded
    /// 12 to keep the (larger, default-4) view filling promptly — a wider view distance has quadratically more
    /// chunks to send, so a too-small budget makes terrain "thaw in" slowly at the horizon. Each freshly streamed
    /// chunk that isn't cached is generated synchronously in the tick, so this also bounds first-visit gen cost:
    /// a host seeing tick overruns on weak hardware can lower it; a strong host can raise it for snappier fill.</summary>
    public int ChunkStreamPerTick
    {
        get => _chunkStreamPerTick;
        set => _chunkStreamPerTick = Math.Clamp(value, 1, ChunkStreamPerTickCeiling);
    }

    private int _chunkStreamPerTick = 16;

    /// <summary>Hard ceiling for <see cref="ChunkStreamPerTick"/> (#999): the client's receive budget drains
    /// a bounded number of chunks per frame (NetworkClient.MaxChunksPerPoll, 24 — asserted to stay above this
    /// in ReceiveBudgetTests), so a server configured to push more could out-send a low-fps client
    /// indefinitely — recreating exactly the backlog #963 removed. The setter clamps so the invariant holds
    /// for every config source (file, env var, code), not just the default value.</summary>
    public const int ChunkStreamPerTickCeiling = 20;

    /// <summary>Optional wall-clock budget (milliseconds) for chunk streaming per tick; 0 = off. When set,
    /// StreamChunks stops sending once the budget is spent — remaining chunks come next tick, nearest-first
    /// order unchanged. Meant for hosts where the tick shares a thread with rendering (the in-browser
    /// singleplayer): a cheap tick still streams the full ChunkStreamPerTick, but a burst of expensive
    /// first-visit generations can't stall the frame. Dedicated servers leave this off.</summary>
    public double ChunkStreamBudgetMs { get; set; }

    /// <summary>Background threads that generate first-visit chunks (#1817). The tick only queues the missing
    /// chunks of a view and adopts finished ones (persisted edits are applied on the tick thread), so exploring no
    /// longer stretches the tick. 0 = the historical inline generation — the in-browser singleplayer has no C#
    /// threads and always uses 0. Each worker owns its own generator (up to ~18 MB of column memos at view 8).
    /// Clamped to 0..<see cref="ChunkGenWorkersCeiling"/>. CLI <c>--chunk-gen-workers</c>, env
    /// <c>BBS_CHUNK_GEN_WORKERS</c>.</summary>
    public int ChunkGenWorkers
    {
        get => _chunkGenWorkers;
        set => _chunkGenWorkers = Math.Clamp(value, 0, ChunkGenWorkersCeiling);
    }

    private int _chunkGenWorkers = 2;

    /// <summary>Upper bound for <see cref="ChunkGenWorkers"/>.</summary>
    public const int ChunkGenWorkersCeiling = 8;

    /// <summary>Opt-in tick profiling (#1504): when > 0, the server accumulates the wall-clock time of every
    /// Guard-wrapped tick system and logs one summary line every this-many seconds of sim time — the top systems
    /// by milliseconds per second, tick p50/p95/max and how many ticks overran the tick budget. 0 (the default)
    /// = off: no timestamps and no allocation on the tick path. Meant for answering "where does the tick go" on a
    /// live host without attaching a profiler. Also settable via <c>BBS_TICK_TIMING_LOG_SECONDS</c>.</summary>
    public double TickTimingLogSeconds { get; set; }

    public string Difficulty { get; set; } = "normal";
    public bool AllowGuests { get; set; } = true;

    /// <summary>Bind address for the admin UI; defaults to loopback so it is not public (§13.3).</summary>
    public string AdminBindAddress { get; set; } = "127.0.0.1";

    /// <summary>Server simulation tick rate in Hz (10–20 recommended, §7.2).</summary>
    public int TickRate { get; set; } = 15;

    /// <summary>Long world seed; 0 means "derive one from the world name".</summary>
    public long Seed { get; set; }

    /// <summary>Planet type the world starts on. The default is a breathable, fertile type so a new
    /// player begins with air, food plants and the common ores (not a toxic survival-pressure world).</summary>
    public string StartPlanet { get; set; } = "varied";

    /// <summary>Authoritative world rules (mode, PvP, hazards, death penalty, cheats, ...).</summary>
    public GameRules Rules { get; set; } = new();

    /// <summary>Universe description used when first creating the world. System variance (#546) and
    /// asteroid belts (#683) are on here — every NEWLY created world gets archetype-varied star systems
    /// whose asteroids share real belt annuli — while the
    /// <see cref="BlocksBeyondTheStars.Shared.World.WorldDescription.SystemVariance"/> and
    /// <see cref="BlocksBeyondTheStars.Shared.World.WorldDescription.AsteroidBelts"/> properties
    /// default to false, so a loaded save whose metadata predates the features stays byte-identical.
    /// Space stations are Normal here for the same reason (#1114): new galaxies get a station in most
    /// systems, while the <see cref="BlocksBeyondTheStars.Shared.World.WorldDescription.SpaceStations"/>
    /// property keeps its Rare default so an old save whose metadata omitted the field is unchanged.
    /// Template use is Normal here too (#1115): with a real pool of hand-designed layouts, new worlds
    /// should actually show them — old saves keep the property's load-safe Rare.</summary>
    public BlocksBeyondTheStars.Shared.World.WorldDescription World { get; set; } = new()
    {
        SystemVariance = true,
        AsteroidBelts = true,
        TerrainContinents = true,
        LavaCoreVolcanoes = true,
        TerrainGeneration = BlocksBeyondTheStars.Shared.World.WorldDescription.CurrentTerrainGeneration,
        SpaceStations = BlocksBeyondTheStars.Shared.World.Frequency.Normal,
        StationTemplateUse = BlocksBeyondTheStars.Shared.World.Frequency.Normal,
        SettlementTemplateUse = BlocksBeyondTheStars.Shared.World.Frequency.Normal,
    };

    /// <summary>Optional AI mission backend level (Off keeps the game fully AI-free).</summary>
    public AiLevel AiLevel { get; set; } = AiLevel.Off;

    /// <summary>Base URL of the optional Python AI backend (used when <see cref="AiLevel"/> is not Off).</summary>
    public string AiBackendUrl { get; set; } = "http://127.0.0.1:8077";

    /// <summary>HTTP timeout (seconds) for AI-backend calls. Generous by design: players never wait on
    /// AI (they always get an instant static/template line; the LLM line upgrades it asynchronously), so
    /// this only bounds how late an upgrade may still arrive. Keep it ABOVE the backend's own LLM timeout
    /// (BBTS_AI_TIMEOUT, default 30 s) so the backend's template fallback beats this deadline.</summary>
    public int AiTimeoutSeconds { get; set; } = 35;

    /// <summary>#2591: how many AI-personalised NPC/VEGA lines this world may request per minute (the refill rate of
    /// a token bucket that holds at most 6; at most 2 requests run at once). Every line has an authored fallback, so
    /// a spent budget only means "the written line this time". 0 = no AI-personalised lines at all. Env
    /// <c>BBS_AI_LINES_PER_MINUTE</c>. Only read while <see cref="AiLevel"/> is not Off.</summary>
    public double AiLinesPerMinute { get; set; } = 4.0;

    /// <summary>Maximum time (seconds) to wait for the client to report one stage of an automatic landed-ship
    /// transit (#1614) — the take-off, or the warp into another system — as played before the server moves the
    /// transit on by itself. Each stage gets the full time.</summary>
    public double TransitLaunchTimeoutSeconds { get; set; } = 6.0;

    /// <summary>Endpoint the server POSTs automatic crash reports to — the ReportHost bug-report inbox, shared
    /// with player feedback + client crashes (server reports are shaped to the same contract). Uploading stays
    /// OFF until <see cref="CrashReportApiKey"/> is also set, so a self-hosted server never phones home unless
    /// its operator opts in; reports are written to the local <c>crashreports/</c> folder regardless.</summary>
    public string CrashReportEndpoint { get; set; } = "https://reports.blocksbeyondthestars.de/api/bugreport";

    /// <summary>Spam-gate key sent with an automatic crash report (the <c>x-bugreport-key</c> header). Empty
    /// (the default) leaves crash uploading disabled regardless of the endpoint — official builds inject it.</summary>
    public string CrashReportApiKey { get; set; } = string.Empty;

    /// <summary>Operator push-notification URL (<c>BBS_NOTIFY_URL</c>, issue #938) — an ntfy topic URL or
    /// any webhook that accepts a plain-text POST. Pinged on <c>/reportpaint</c>/<c>/reportshape</c> and on
    /// watch-list name flags. Empty (the default) = off; the hosted fleet leaves this unset because the
    /// WorldHost/ReportHost carry their own notify hooks there.</summary>
    public string NotifyUrl { get; set; } = string.Empty;

    /// <summary>Name block list enforced at join (shared semantics with the WorldHost gates — see
    /// <see cref="Moderation.NameScreen"/>). <c>BBS_BLOCKED_WORDS</c> (comma-separated) EXTENDS the
    /// defaults; substring-matched, so number codes or short abbreviations never belong here.</summary>
    public List<string> BlockedNameWords { get; set; } = new(Moderation.NameScreen.DefaultBlockedWords);

    /// <summary>Name watch list: a join under a matching name is ALLOWED but logged + pushed to
    /// <see cref="NotifyUrl"/> — the human decides, not the filter (issue #938). <c>BBS_WATCH_WORDS</c>
    /// (comma-separated) EXTENDS the defaults; a leading '=' pins an entry to whole-token matching.</summary>
    public List<string> WatchNameWords { get; set; } = new(Moderation.NameScreen.DefaultWatchWords);

    /// <summary>Operator-level chat screening switch (#1207, <c>BBS_CHAT_FILTER</c> / <c>--chat-filter</c>):
    /// <see cref="Moderation.ChatFilterLevel.Mask"/> (default) lets each world's <see cref="GameRules.ChatMode"/>
    /// decide, <see cref="Moderation.ChatFilterLevel.Off"/> disables screening for every world on this server
    /// (a private family LAN), <see cref="Moderation.ChatFilterLevel.Strict"/> forces the Safe mode everywhere
    /// (a public kids' fleet). Never persisted into a save — it is the operator's, not the world's.</summary>
    public Moderation.ChatFilterLevel ChatFilter { get; set; } = Moderation.ChatFilterLevel.Mask;

    /// <summary>Chat hate/slur list — a matching line is dropped (<c>BBS_CHAT_BLOCKED_WORDS</c> EXTENDS the
    /// defaults; whole-token matching, long entries also as substrings). See <see cref="Moderation.ChatScreen"/>.</summary>
    public List<string> ChatBlockedWords { get; set; } = new(Moderation.ChatScreen.DefaultBlockedWords);

    /// <summary>Chat profanity list — a matching word is replaced by asterisks, the line is relayed
    /// (<c>BBS_CHAT_MASKED_WORDS</c> EXTENDS the defaults; whole-token matching).</summary>
    public List<string> ChatMaskedWords { get; set; } = new(Moderation.ChatScreen.DefaultMaskedWords);

    /// <summary>Chat watch list — a matching line is relayed but logged and pushed to <see cref="NotifyUrl"/>
    /// (<c>BBS_CHAT_WATCH_WORDS</c> EXTENDS the defaults; whole-token matching).</summary>
    public List<string> ChatWatchWords { get; set; } = new(Moderation.ChatScreen.DefaultWatchWords);

    /// <summary>Chat allow list — tokens that never match any chat list (<c>BBS_CHAT_ALLOW_WORDS</c>); the
    /// operator's escape hatch for a false positive in their community's language.</summary>
    public List<string> ChatAllowWords { get; set; } = new();

    /// <summary>Opt-in live voice chat. When false (the default on dedicated servers) the server rejects/ignores
    /// voice frames and tells clients voice is unavailable; text chat is unaffected. Voice is relayed live and
    /// never recorded. The bundled singleplayer/host launcher may turn this on for local co-op.</summary>
    public bool VoiceChatEnabled { get; set; }

    // --- Hosted-worlds fleet support (operator-set, never player-facing). A control plane that spawns one
    // server container per world uses these to make instances stop when unused, gate joins to players it
    // vouched for, and hand the uploading owner their world back. All three default OFF, so singleplayer,
    // LAN hosting and classic self-hosting behave exactly as before. ---

    /// <summary>Shut the server down cleanly (drain + save, like Ctrl+C) after this many minutes with no
    /// joined player — counted from startup too, so a woken instance nobody joins also stops. 0 (default)
    /// = never; a container running this must NOT use an auto-restart policy or it defeats the idle stop.</summary>
    public int IdleShutdownMinutes { get; set; }

    /// <summary>When set, every network join must carry a valid HMAC join token issued for this world by the
    /// control plane (see <c>HostedJoinToken</c>). Empty (default) = joins work as before. Local/singleplayer
    /// sessions are exempt — the bundled host never sets this.</summary>
    public string JoinTokenSecret { get; set; } = string.Empty;

    /// <summary>Account id of the world's owner in the control plane. A token-verified join whose account id
    /// matches is granted WorldAdmin — required for uploaded saves, where someone else may already hold that
    /// role. While this is set, the "first joiner becomes WorldAdmin" rule is OFF (#2486): on a hosted world the
    /// owner rule is the only way to WorldAdmin, so a stranger who happens to join before the owner (or the first
    /// guest on an arcade pool world, whose owner id never matches a guest) stays a Player. Empty (default) = no
    /// owner mapping, and the first joiner of a fresh world becomes its WorldAdmin as before.</summary>
    public string WorldOwnerAccountId { get; set; } = string.Empty;

    /// <summary>No player admins on this instance (#2486, env <c>BBS_NO_PLAYER_ADMINS=1</c>, CLI
    /// <c>--no-player-admins true</c>). A save's player records cannot hold an admin role here: at join, a player
    /// whose saved role is Admin or WorldAdmin is demoted to Player — saved and logged — unless the name is in
    /// <see cref="AdminPlayers"/> or the join is the token-verified <see cref="WorldOwnerAccountId"/>. Admin then
    /// comes only from this instance's configuration. The WorldHost sets it for the glitch.fun arcade worlds,
    /// whose players are anonymous guests; fleet admins (<see cref="FleetAdminPlayers"/>) are not a role and keep
    /// their powers. Off (default) everywhere else — singleplayer, LAN and self-hosting are unchanged.</summary>
    public bool NoPlayerAdmins { get; set; }

    /// <summary>Shared secret the control plane must present (X-Announce-Token header) to push a maintenance
    /// announcement via the WebSocket gateway's <c>POST /announce</c>. Empty (default) = endpoint disabled;
    /// the bundled/self-hosted server never sets this (admins use the in-game /announce commands instead).</summary>
    public string AnnounceToken { get; set; } = string.Empty;

    // --- Filesystem locations (resolved relative to the server install dir) ---

    public string SavesRoot { get; set; } = "saves";
    public string DataDir { get; set; } = "data";

    /// <summary>
    /// Persistence backend for authoritative world state. "sqlite" is the portable default; "postgresql"
    /// uses <see cref="PostgresConnectionString"/> and is intended for hosted dedicated/MMO-style servers.
    /// </summary>
    public string DatabaseProvider { get; set; } = "sqlite";

    /// <summary>PostgreSQL connection string. Prefer supplying this through BBS_POSTGRES_CONNECTION_STRING
    /// or DATABASE_URL in hosted deployments instead of committing it to server.json.</summary>
    public string PostgresConnectionString { get; set; } = string.Empty;

    /// <summary>Optional writable folder holding in-game-editor structure templates
    /// (<c>station_templates/*.json</c>, <c>settlement_templates/*.json</c>). When set, they are merged
    /// into the template pools at load so player-authored structures appear in new worlds without a
    /// rebuild. Empty ⇒ only the shipped <c>data/</c> pools are used.</summary>
    public string UserContentDir { get; set; } = string.Empty;

    /// <summary>Whether to stamp the enterable starter-ship hull at the start landing zone (M23a).</summary>
    public bool PlaceStarterShip { get; set; } = true;

    /// <summary>
    /// Whether the server may stamp a procedural settlement on the start planet's surface (away
    /// from the landing zone) when the planet + seed call for one.
    /// </summary>
    public bool PlaceSettlements { get; set; } = true;

    /// <summary>
    /// Whether the server may stamp a rare crashed-ship wreck on the start planet's surface (away
    /// from the landing zone). Wrecks are uncommon and left scavengeable (not protected).
    /// </summary>
    public bool PlaceWrecks { get; set; } = true;

    /// <summary>
    /// Whether the server may stamp buried vault ruins ("Welten reicher" W-R3) — 0–2 per world: a surface
    /// pillar ring over a shaft down to a stone chamber with data caches + lootable containers.
    /// </summary>
    public bool PlaceVaults { get; set; } = true;

    /// <summary>
    /// Whether the server may scatter "data cubes" on a body's surface — 0–N per world (some bodies get
    /// none): glowing download terminals that grant the player a small bundled minigame for their personal
    /// arcade collection. Deterministic from the world seed; carry no gameplay effect.
    /// </summary>
    public bool PlaceDataCubes { get; set; } = true;

    /// <summary>
    /// Whether the server may stamp procedural <b>factories</b> on a body's surface — rare industrial buildings
    /// (0–N per world, most get none) housing animated machines and a production terminal. Protected like a
    /// settlement until claimed with an access code. Deterministic from the world seed.
    /// </summary>
    public bool PlaceFactories { get; set; } = true;

    /// <summary>
    /// Whether the server may stamp randomised <b>ruins</b> of fallen settlements on a body's surface — partial
    /// walls, a half-collapsed tower, rubble. Unlike intact settlements, ruins are NOT protected (their blocks
    /// are freely mineable). Deterministic from the world seed.
    /// </summary>
    public bool PlaceRuins { get; set; } = true;

    /// <summary>
    /// Whether the server may scatter standalone <b>treasure chests</b> on a body's surface — rare lootable
    /// caches independent of any structure (0–N per world, most get none). Looted once, then gone.
    /// Deterministic from the world seed.
    /// </summary>
    public bool PlaceChests { get; set; } = true;

    /// <summary>
    /// Whether the server may stamp <b>bandit camps</b> on a body's surface — small hostile outposts
    /// (huts + palisade) guarded by bandit NPCs, with a loot stash as the raid reward. Like ruins the
    /// blocks are NOT protected; a razed camp stays razed and cleared bandits stay gone. Camps also
    /// require <see cref="GameRules.Bandits"/> to be enabled. Deterministic from the world seed.
    /// </summary>
    public bool PlaceBanditCamps { get; set; } = true;

    /// <summary>
    /// Whether the server may stamp <b>lodges</b> (#2640) — small inhabited huts out in the open land, each with one or
    /// two residents who gather, mine, chop or hunt around it. Only on worlds of terrain generation 25 or newer
    /// (<see cref="BlocksBeyondTheStars.Shared.World.WorldDescription.WildernessFolkGeneration"/>), only while
    /// <see cref="PlaceSettlements"/> is on too, and as many as the world's "Settlements" option allows. Protected like a
    /// village; the voxels are stamped once, the residents re-derive on every load. Deterministic from the world seed.
    /// </summary>
    public bool PlaceLodges { get; set; } = true;

    /// <summary>
    /// Whether the server may stamp <b>pioneer outposts</b> (#2681) — the lodge variant a player-like NPC lives in: a small
    /// closed base with a decorative base core, owned crates, a ladder shaft down to a cellar and, by the water, a boat. One
    /// or two per world at most, only where lodges may stand (so <see cref="PlaceLodges"/> off switches them off too). Like
    /// a lodge, the voxels are stamped once on a body nobody has visited yet and the resident re-derives on every load.
    /// </summary>
    public bool PlacePioneers { get; set; } = true;

    /// <summary>
    /// Whether <b>wanderers</b> (#2646) may come by — named travellers who gather, prospect, chop or hunt in the open land
    /// near a player on foot, trade and talk, and move on (RAM only, at most two per world at once). The same worlds as
    /// <see cref="PlaceLodges"/>: terrain generation 25 or newer, never a void, airless, ruined-only, gas or city world, and
    /// not with the world's "Settlements" option off. Each world's small cast is derived from the world seed.
    /// </summary>
    public bool PlaceWanderers { get; set; } = true;

    /// <summary>
    /// Whether the server may stamp <b>monuments</b> on a body's surface — eroded relics of a vanished
    /// civilisation (arcade arches, a free-standing gate, a stone circle, an obelisk, a rune altar), carved
    /// with glowing runes that grant knowledge points when scanned. Like ruins the blocks are NOT protected,
    /// so a razed monument stays razed. Unlike every other surface feature these also appear on airless
    /// bodies. Deterministic from the world seed.
    /// </summary>
    public bool PlaceMonuments { get; set; } = true;

    /// <summary>
    /// Whether the server may lay a generated <b>intercity monorail</b> (#2125) on a new world (terrain generation 19+) — a
    /// train line with a station at each end between the two closest inhabited towns or cities, and a public train that
    /// shuttles between them. Only decides for worlds that have not decided yet; a line a world already has stays. The
    /// stations and pylons are protected like a settlement. Deterministic from the world seed.
    /// </summary>
    public bool PlaceIntercityRail { get; set; } = true;

    /// <summary>
    /// The chance (0..1) that a world with at least two inhabited towns or cities gets its intercity line (#2125) — 0.6 by
    /// default (Marcel's decision). Rolled once per world, deterministically from the seed and the body id, on a lane of its
    /// own; tests pin 1.0 to get a line wherever one fits.
    /// </summary>
    public double IntercityRailChance { get; set; } = 0.6;

    /// <summary>
    /// Whether the server may stamp the ruin of an <b>abandoned monorail station</b> (#2166) on a new world (terrain
    /// generation 20+) — a fallen station hall with a derelict wagon, the dead line beyond it, salvage and station notices.
    /// Like the other ruins its blocks are NOT protected. Only decides for worlds that have not decided yet; a ruin a world
    /// already has stays. Deterministic from the world seed.
    /// </summary>
    public bool PlaceRailRuins { get; set; } = true;

    /// <summary>
    /// The chance (0..1) that a world which could carry one gets its abandoned monorail station (#2166) — 0.35 by default,
    /// scaled by the world's structures frequency. Rolled once per world, deterministically from the seed and the body id,
    /// on a lane of its own; tests pin 1.0 to get a ruin wherever one fits.
    /// </summary>
    public double RailRuinChance { get; set; } = 0.35;

    /// <summary>
    /// Singleplayer/admin convenience: guarantee one data cube right next to the start world's landing pad, so
    /// a solo player can always reach a minigame near spawn. Set only by the bundled singleplayer launcher;
    /// left off on shared/dedicated servers (where the random scatter applies as normal).
    /// </summary>
    public bool GuaranteeStartDataCube { get; set; }

    // --- Singleplayer "Creative" world options (the player picks these at world creation). They are a
    // head-start sandbox: everything available + a starter set, while survival mechanics stay ON. Default
    // false = the normal "Explorer" experience. Persisted per world in WorldMetadata so they reapply on load. ---

    /// <summary>Start with every blueprint unlocked (re-applied each join; idempotent).</summary>
    public bool CreativeUnlockAllBlueprints { get; set; }

    /// <summary>Own every ship type from the start (re-applied each join; idempotent).</summary>
    public bool CreativeStartAllShips { get; set; }

    /// <summary>Grant a curated kit (all tools + generous stacks of key materials) once, at first spawn.</summary>
    public bool CreativeStarterKit { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Resolves the startup config. Normally this is <see cref="Load"/> (which reads — and on first run
    /// auto-creates — <c>config/server.json</c> next to the exe, the documented dedicated-server flow).
    /// When <c>--no-config</c> is present it returns pure in-memory defaults instead and touches no file.
    ///
    /// The bundled singleplayer host passes <c>--no-config</c> (see <c>LocalServerLauncher</c>): its server
    /// exe lives in the read-only, reused-across-rebuilds bundle folder, where a leftover <c>server.json</c>
    /// from an earlier run/playtest would otherwise be read verbatim and override newer code defaults such
    /// as the start planet — the exact reason a freshly-built client could still spawn the old "rocky" start
    /// world. The host relies purely on code defaults plus its explicit CLI overrides, so it is immune to a
    /// stale bundled config on every install path (local build, installer, portable, Velopack update).
    /// Dedicated servers omit the flag and keep the auto-generated, editable <c>config/server.json</c>.
    /// </summary>
    public static ServerConfig LoadForStartup(string[]? args, string path)
        => (args is not null && Array.IndexOf(args, "--no-config") >= 0) ? new ServerConfig() : Load(path);

    public static ServerConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            var fresh = new ServerConfig();
            fresh.Save(path);
            return fresh;
        }

        return JsonSerializer.Deserialize<ServerConfig>(File.ReadAllText(path), Options) ?? new ServerConfig();
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static ServerConfig FromJson(string json)
        => JsonSerializer.Deserialize<ServerConfig>(json, Options) ?? new ServerConfig();

    /// <summary>
    /// Applies <c>--key value</c> command-line overrides onto this config. Used to embed/launch
    /// the server programmatically — e.g. the Unity client's local singleplayer host passes a
    /// port, a private saves dir and the data dir. Unknown keys are ignored; missing trailing
    /// values are safe. Returns the canonical names of the keys that were applied.
    /// The accepted names are the rows of <see cref="ServerConfigOptions.All"/>.
    /// </summary>
    public IReadOnlyList<string> ApplyCommandLine(string[]? args) => ServerConfigOptions.ApplyCommandLine(this, args);

    /// <summary>
    /// Applies <c>BBS_*</c> environment-variable overrides onto this config — the configuration
    /// channel used when running in a container (Docker/Compose/Kubernetes), where mounting a
    /// <c>server.json</c> is awkward. Precedence is <c>server.json</c> &lt; environment &lt;
    /// command-line, so call this after <see cref="Load"/> and before <see cref="ApplyCommandLine"/>.
    /// Empty/unset variables are ignored; unparseable values are skipped. Returns the canonical
    /// names of the keys that were applied.
    /// The accepted names are the rows of <see cref="ServerConfigOptions.All"/>.
    /// </summary>
    public IReadOnlyList<string> ApplyEnvironment() => ServerConfigOptions.ApplyEnvironment(this);

    /// <summary>Parses an on/off switch: <c>true</c>/<c>false</c> (case-insensitive) like every other boolean option,
    /// plus the shell-style <c>1</c>/<c>0</c>, <c>yes</c>/<c>no</c> and <c>on</c>/<c>off</c>. Anything else is refused,
    /// so a typo leaves the default in place rather than guessing.</summary>
    public static bool TryParseFlag(string? value, out bool flag)
    {
        string word = (value ?? string.Empty).Trim();
        if (bool.TryParse(word, out flag))
        {
            return true;
        }

        if (word == "1" || string.Equals(word, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(word, "on", StringComparison.OrdinalIgnoreCase))
        {
            flag = true;
            return true;
        }

        if (word == "0" || string.Equals(word, "no", StringComparison.OrdinalIgnoreCase) || string.Equals(word, "off", StringComparison.OrdinalIgnoreCase))
        {
            flag = false;
            return true;
        }

        return false;
    }

    /// <summary>Parses a tutorial policy word (#2466): <c>auto</c>, <c>optin</c> (also <c>opt-in</c>/<c>opt_in</c>),
    /// <c>off</c> or <c>courses</c> ("Short courses only", #2498), case-insensitive. Numbers are refused (an enum parse would
    /// take any integer).</summary>
    public static bool TryParseTutorialPolicy(string? value, out TutorialPolicy policy)
    {
        string word = (value ?? string.Empty).Trim().Replace("-", string.Empty).Replace("_", string.Empty);
        policy = TutorialPolicy.Auto;
        if (word.Length == 0 || char.IsDigit(word[0]))
        {
            return false;
        }

        return Enum.TryParse(word, ignoreCase: true, out policy) && Enum.IsDefined(typeof(TutorialPolicy), policy);
    }

    /// <summary>Parses a story pace word (#2472, the live world-rules edit): <c>Sparse</c>, <c>Normal</c> or <c>Dense</c>,
    /// case-insensitive. Numbers are refused (an enum parse would take any integer).</summary>
    public static bool TryParseStoryDensity(string? value, out StoryDensity density)
    {
        string word = (value ?? string.Empty).Trim();
        density = StoryDensity.Normal;
        if (word.Length == 0 || char.IsDigit(word[0]) || word[0] == '-' || word[0] == '+')
        {
            return false;
        }

        return Enum.TryParse(word, ignoreCase: true, out density) && Enum.IsDefined(typeof(StoryDensity), density);
    }
}
