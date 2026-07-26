// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// Operator configuration for the hosted-worlds control plane. Everything here is set by the OPERATOR
/// through <c>BBS_WH_*</c> environment variables — none of it is ever exposed to or changeable by players
/// (the quota values in particular are policy, not preferences). Defaults are development-friendly
/// (localhost, local image); a real deployment sets the domain, image and public host.
/// </summary>
public sealed class WorldHostConfig
{
    /// <summary>Bind address for the WorldHost API; loopback by default so it is not public — in the
    /// intended deployment Caddy proxies the public portal/API domain onto it.</summary>
    public string BindAddress { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 31417;

    /// <summary>Base domain under which every world gets its subdomain (<c>w-&lt;id&gt;.&lt;BaseDomain&gt;</c>),
    /// e.g. <c>play.blocksbeyondthestars.de</c>. "localhost" keeps development self-contained.</summary>
    public string BaseDomain { get; set; } = "localhost";

    /// <summary>Public hostname/IP native (UDP) clients connect to. Browser clients go through the
    /// per-world subdomain + Caddy; native UDP bypasses the proxy and needs the machine itself.</summary>
    public string PublicHost { get; set; } = "localhost";

    /// <summary>Game website the portal links out to (footer + landing page). Defaults to the project's
    /// own site; a self-hoster who does not want to advertise it sets the variable empty, which drops the
    /// link entirely.</summary>
    public string WebsiteUrl { get; set; } = "https://www.blocksbeyondthestars.com/";

    /// <summary>English entry point of <see cref="WebsiteUrl"/> — a separate value rather than a
    /// "+ /en" rule, because that suffix is a property of OUR site, not of websites in general. Empty
    /// falls back to <see cref="WebsiteUrl"/>.</summary>
    public string WebsiteUrlEn { get; set; } = "https://www.blocksbeyondthestars.com/en";

    /// <summary>Dedicated-server image each world instance runs (one container per world).</summary>
    public string ServerImage { get; set; } = "blocks-beyond-the-stars-server:local";

    /// <summary>Docker network shared with the caddy-docker-proxy container so the proxy can reach the
    /// per-world WebSocket gateways by container name.</summary>
    public string DockerNetwork { get; set; } = "bbs-hosted";

    /// <summary>First host port handed to world instances. Each world gets ONE stable port from this range,
    /// published as both udp (native gameplay) and tcp (WS gateway → /status health probe).</summary>
    public int PortRangeStart { get; set; } = 32000;

    public int PortRangeSize { get; set; } = 1000;

    // --- Quotas (operator policy; see the hosted-worlds plan: free tier with tight limits) ---

    public int MaxWorldsPerAccount { get; set; } = 2;

    public int MaxPlayersPerWorld { get; set; } = 12;

    /// <summary>Idle minutes passed to each instance (BBS_IDLE_SHUTDOWN_MINUTES) — a world with no players
    /// saves and exits after this long; the reaper then marks it stopped in the registry.</summary>
    public int IdleShutdownMinutes { get; set; } = 20;

    /// <summary>Per-tick wall-clock budget (ms) each hosted world spends streaming chunks, forwarded as
    /// <c>BBS_CHUNK_STREAM_BUDGET_MS</c> (<c>BBS_WH_CHUNK_STREAM_BUDGET_MS</c>). A hosted world runs its tick
    /// on a single thread with no render loop, so a burst of cold first-visit worldgen (a fresh join, fast
    /// flight over new terrain) would otherwise run all ChunkStreamPerTick generations in one tick and push
    /// it over the ~66 ms tick window — stalling simulation for the OTHER players in that world. This caps
    /// that: at least one chunk always streams, the rest resume next tick (nearest-first order unchanged).
    /// The default 25 ms is generous headroom under the tick window — it only trims pathological bursts, not
    /// normal fill. 0 = off (unbounded, the historical behaviour).</summary>
    public double ChunkStreamBudgetMs { get; set; } = 25.0;

    /// <summary>How long a join request waits for a woken instance to answer its /status probe.</summary>
    public int WakeTimeoutSeconds { get; set; } = 90;

    public int SessionDays { get; set; } = 30;

    /// <summary>Names reserved for the developers — nobody else may register them as an account name or
    /// use them as an in-game player name on hosted worlds. Matched normalized (case-insensitive, with
    /// spaces/'-'/'_' stripped), so "ju ju" or "J_ustus" are caught too. Operator-extendable via
    /// <c>BBS_WH_RESERVED_NAMES</c> (comma-separated, replaces the default list).</summary>
    public List<string> ReservedNames { get; set; } = new()
    {
        "Marcel", "Justus", "Verena", "juju", "JuMaVe Games", "FlashMiner", "JustusJulius", "BloddyMary",
    };

    /// <summary>Claim code (BBS_WH_RESERVED_CLAIM_CODE) a developer presents once at signup to register a
    /// reserved name; the account is then permanently flagged as a developer account (which also unlocks
    /// reserved in-game names). Empty (default) = reserved names cannot be claimed at all.</summary>
    public string ReservedClaimCode { get; set; } = string.Empty;

    /// <summary>Directory holding the registry database (worldhost.db).</summary>
    public string DataDir { get; set; } = "worldhost";

    /// <summary>Base directory for per-world save data, bind-mounted into each instance at /app/saves —
    /// a bind mount (not a named volume) so THIS process can implement save upload/export directly.</summary>
    public string WorldsDir { get; set; } = Path.Combine("worldhost", "worlds");

    /// <summary>Version of the community rules text. Bump when the rules change: accounts that accepted an
    /// older version are asked to re-accept before they can create/join worlds.</summary>
    public int TermsVersion { get; set; } = 1;

    /// <summary>Upload size cap for a world.db save (bytes). Saves are block-edit deltas, so even large
    /// builds stay small; the cap mainly bounds abuse.</summary>
    public long UploadMaxBytes { get; set; } = 50 * 1024 * 1024;

    /// <summary>Operator token for the admin endpoints (report review, account bans). Empty (default)
    /// disables the admin API entirely.</summary>
    public string AdminToken { get; set; } = string.Empty;

    // --- Phase 3: lifecycle & abuse hardening (all operator policy) ---

    /// <summary>Months of inactivity after which a stopped world is archived: its saves move to the
    /// archive folder and its instance claim ends. Joining an archived world transparently restores it
    /// (it just takes a moment longer to wake). 0 = never archive.</summary>
    public int ArchiveAfterMonths { get; set; } = 6;

    /// <summary>Words that may not appear in account names, world names or in-game player names —
    /// matched against the same normalization as reserved names (lowercase, separators stripped), so
    /// "H-i-t-l-e-r" is caught too. Kid-facing service: better safe. <c>BBS_WH_BLOCKED_WORDS</c>
    /// (comma-separated) EXTENDS this list. Deliberately short and unambiguous to avoid Scunthorpe-style
    /// false positives.</summary>
    public List<string> BlockedNameWords { get; set; } = new()
    {
        "hitler", "nazi", "nigger", "neger", "fuck", "bitch", "hurensohn", "fotze", "wichser", "arschloch",
    };

    // Rate limits (fixed windows). Signup/login key on the caller IP, uploads/reports on the account —
    // they exist to blunt scripted abuse, not to inconvenience players.

    public int SignupPerHourPerIp { get; set; } = 5;

    public int LoginPerMinutePerIp { get; set; } = 10;

    /// <summary>Failed login attempts per account per 15 minutes before further tries are refused
    /// (BBS_WH_LOGIN_FAILS_PER_15_MIN). Complements the per-IP login limit: that one keys on a value an
    /// attacker with many addresses (or a spoofable proxy chain) controls, this one keys on the TARGET
    /// account, so a distributed brute force against one name still stalls. Only failures consume budget —
    /// a player who knows their password is never locked out. Non-positive disables it.</summary>
    public int LoginFailsPer15MinPerAccount { get; set; } = 10;

    /// <summary>Proxies whose <c>X-Forwarded-For</c> is honored as the real client IP — the rate limits
    /// above key on it (BBS_WH_TRUSTED_PROXIES, comma-separated IPs and/or CIDRs; the literal <c>none</c>
    /// empties the list, which turns forwarded headers OFF entirely). The default trusts loopback and the
    /// private ranges: the fleet's Caddy is a sibling container on the docker network (an address in
    /// 172.16/12 assigned at network creation), while a caller on the public internet can never inject a
    /// spoofed header this way (#418). Operators who know their proxy's exact address can tighten this.</summary>
    public List<string> TrustedProxies { get; set; } = new()
    {
        "127.0.0.0/8", "::1", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7",
    };

    public int UploadsPerHourPerAccount { get; set; } = 6;

    public int ReportsPerHourPerAccount { get; set; } = 10;

    // --- Public aggregate stats (GET /api/stats): four numbers for the website/client. Public and
    // unauthenticated, therefore doubly guarded: a cached single-flight snapshot plus a per-IP limit. ---

    /// <summary>Per-IP request limit for /api/stats (BBS_WH_STATS_PER_MINUTE); non-positive disables the limiter.</summary>
    public int StatsPerMinutePerIp { get; set; } = 30;

    /// <summary>Seconds the /api/stats snapshot is served from cache (BBS_WH_STATS_CACHE_SECONDS) —
    /// the instance /status probes behind the online-player count never run more often than this.</summary>
    public int StatsCacheSeconds { get; set; } = 30;

    // --- Legal pages (§5 DDG Impressum + DSGVO privacy). Operator-set on purpose: a SELF-HOSTED
    // WorldHost must carry ITS operator's data, never the project authors' — empty values make the
    // pages render a clear "not configured" notice instead of wrong legal information. ---

    /// <summary>Legal operator name shown on /impressum and /datenschutz (BBS_WH_LEGAL_NAME).</summary>
    public string LegalName { get; set; } = string.Empty;

    /// <summary>Postal address, comma-separated lines (BBS_WH_LEGAL_ADDRESS).</summary>
    public string LegalAddress { get; set; } = string.Empty;

    /// <summary>Contact email — §5 DDG requires one (BBS_WH_LEGAL_EMAIL).</summary>
    public string LegalEmail { get; set; } = string.Empty;

    // --- Fleet AI texts (optional). When AiBackendUrl is set, every world instance receives it as
    // BBS_AI_BACKEND_URL + BBS_AI_LEVEL, enabling LLM-authored NPC lines/mission flavour. The game
    // degrades gracefully either way (instant static line, async LLM upgrade), so this is pure opt-in. ---

    /// <summary>AI-backend URL passed to world instances (BBS_WH_AI_BACKEND_URL) — on the fleet the
    /// internal-only sibling container, e.g. <c>http://ai:8077</c>. Empty (default) = AI off.</summary>
    public string AiBackendUrl { get; set; } = string.Empty;

    /// <summary>AI level passed to world instances (BBS_WH_AI_LEVEL). TextOnly = NPC lines + board
    /// flavour text but no auto-published AI missions — the right fleet default.</summary>
    public string AiLevel { get; set; } = "TextOnly";

    /// <summary>Shared secret for pushing maintenance announcements into running world instances
    /// (BBS_WH_ANNOUNCE_TOKEN). Forwarded to every world container as BBS_ANNOUNCE_TOKEN, presented back
    /// on POST /announce. Empty (default) = announcements off (both sides keep the endpoint disabled).</summary>
    public string AnnounceToken { get; set; } = string.Empty;

    /// <summary>Player names granted <b>fleet admin</b> inside every hosted world (BBS_WH_FLEET_ADMINS,
    /// comma-separated). Forwarded to each container as BBS_FLEET_ADMINS. These names — and only these — may
    /// use the invisible observer mode (issue #487); the owner of an individual world keeps their WorldAdmin
    /// powers but cannot observe. Empty (default) = nobody, i.e. the feature is off for the whole fleet.</summary>
    public string FleetAdmins { get; set; } = string.Empty;

    // --- Server crash reports (optional). The dedicated server queues crash reports locally and only
    // uploads them when it has an API key (ServerConfig.CrashReportApiKey — deliberate no-phone-home
    // default). With the key set here, every world instance receives it as BBS_CRASH_REPORT_KEY, so
    // fleet crashes land in the ReportHost inbox (docs/developer/REPORT_HOST.md). ---

    /// <summary>Crash-report write key forwarded to world instances as BBS_CRASH_REPORT_KEY
    /// (BBS_WH_CRASH_REPORT_KEY) — the ReportHost's <c>BBS_REPORTS_WRITE_KEY</c>. Empty (default) =
    /// crash upload stays off in the instances.</summary>
    public string CrashReportKey { get; set; } = string.Empty;

    /// <summary>Optional crash-report endpoint override forwarded as BBS_CRASH_REPORT_ENDPOINT
    /// (BBS_WH_CRASH_REPORT_ENDPOINT). Empty (default) keeps the server's built-in default (the
    /// official inbox) — a self-hosted fleet points this at ITS OWN ReportHost.</summary>
    public string CrashReportEndpoint { get; set; } = string.Empty;

    // --- Per-instance resource limits. One runaway world must never take down the host: each world
    // container gets a hard memory cap (which .NET's cgroup-aware GC also uses to apply pressure
    // BEFORE the OOM kill), a CPU ceiling and a pids cap. The capacity gate bounds the SUM. ---

    /// <summary>Hard memory cap per world container, docker syntax (BBS_WH_INSTANCE_MEMORY). The same
    /// value is set as --memory-swap, so a capped instance cannot push the host into swap thrash.
    /// Empty = no limit (dev). An OOM-killed world is simply marked stopped by the reaper; the next
    /// join wakes it fresh.</summary>
    public string InstanceMemory { get; set; } = "768m";

    /// <summary>CPU ceiling per world container (BBS_WH_INSTANCE_CPUS, docker --cpus syntax). Empty = no limit.</summary>
    public string InstanceCpus { get; set; } = "2";

    /// <summary>Maximum world instances awake at the same time (BBS_WH_MAX_ACTIVE); wake requests beyond
    /// it get the friendly no-capacity error. Sized so MaxActive × InstanceMemory fits the host
    /// (default: 10 × 768m ≈ 7.5 GB on the 8 GB VPS). 0 = unlimited.</summary>
    public int MaxActiveInstances { get; set; } = 10;

    /// <summary>How the orchestrator probes an instance's /status: false (default, dev) = host loopback
    /// (127.0.0.1:hostPort); true (BBS_WH_PROBE_VIA_NETWORK, REQUIRED when WorldHost itself runs in a
    /// container) = the world container's name on the shared docker network — a containerized WorldHost's
    /// loopback can never reach host-published ports.</summary>
    public bool ProbeViaDockerNetwork { get; set; }

    // --- Admin web UI (Basic Auth, /admin). Separate from AdminToken (the script/API credential):
    // browsers can't send custom headers. Empty user or password (default) = admin UI off. ---

    /// <summary>Admin UI user (BBS_WH_ADMIN_USER).</summary>
    public string AdminUser { get; set; } = string.Empty;

    /// <summary>Admin UI password (BBS_WH_ADMIN_PASSWORD).</summary>
    public string AdminPassword { get; set; } = string.Empty;

    /// <summary>Directory holding the Unity WebGL browser build served at <c>/play</c>
    /// (BBS_WH_WEBGL_DIR; the fleet bind-mounts a host folder here). The portal's Play button
    /// deep-links into this page with the world's wss URL + join token; without a build the page
    /// shows a friendly "not installed" notice.</summary>
    public string WebGlDir { get; set; } = "webgl";

    // --- glitch.fun arcade (all optional; the whole gateway stays off without the credentials).
    // A small pool of persistent multiplayer worlds that exist ONLY for the glitch.fun platform:
    // hidden from every portal listing, joinable solely through POST /api/glitch/session. The
    // published Baumhaus rule is amended publicly for this channel (separate arcade context under
    // Glitch's platform accounts/rules) — see the hosted-worlds doc. ---

    /// <summary>Master switch (BBS_WH_GLITCH_ENABLED). Effective only when the title id + token are
    /// also configured — see <see cref="GlitchConfigured"/>.</summary>
    public bool GlitchEnabled { get; set; }

    /// <summary>This game's title UUID on glitch.fun (BBS_WH_GLITCH_TITLE_ID).</summary>
    public string GlitchTitleId { get; set; } = string.Empty;

    /// <summary>Server-side Glitch title token (BBS_WH_GLITCH_TITLE_TOKEN) used for install
    /// validation and the heartbeat relay. Lives ONLY here — it is deliberately never baked into the
    /// public WebGL build (the client heartbeats through our relay instead).</summary>
    public string GlitchTitleToken { get; set; } = string.Empty;

    /// <summary>Size of the arcade world pool (BBS_WH_GLITCH_WORLDS); the gateway lazily creates
    /// missing pool worlds on first use. Sized small on purpose — arcade wakes share BBS_WH_MAX_ACTIVE.</summary>
    public int GlitchWorldCount { get; set; } = 2;

    /// <summary>Player cap per arcade world (BBS_WH_GLITCH_MAX_PLAYERS), passed to the instance as
    /// BBS_MAX_PLAYERS. Applied on the next container start.</summary>
    public int GlitchMaxPlayers { get; set; } = 8;

    /// <summary>Origins allowed to call the /api/glitch endpoints cross-origin
    /// (BBS_WH_GLITCH_ALLOWED_ORIGINS, comma-separated). Note the S3 origin: Glitch serves the
    /// actual game files from its content bucket, so THAT is the page origin the browser sends —
    /// not play.glitch.fun (launch-day lesson: every preflight failed without it).</summary>
    public List<string> GlitchAllowedOrigins { get; set; } = new()
    {
        "https://play.glitch.fun", "https://glitch.fun", "https://www.glitch.fun",
        "https://glitch-game-content.s3.amazonaws.com",
    };

    /// <summary>Keep the arcade pool awake permanently (BBS_WH_GLITCH_KEEP_AWAKE, default true):
    /// pool worlds never idle-exit and are woken at WorldHost startup + re-woken by the reaper, so
    /// a store visitor never waits out a cold worldgen. Costs GlitchWorldCount × InstanceMemory of
    /// standing RAM; disable for tight hosts.</summary>
    public bool GlitchKeepAwake { get; set; } = true;

    /// <summary>Glitch platform API base (BBS_WH_GLITCH_API_URL) — overridable so tests can point the
    /// gateway at a fake.</summary>
    public string GlitchApiBaseUrl { get; set; } = "https://api.glitch.fun";

    /// <summary>Per-IP session-grant limit for POST /api/glitch/session (BBS_WH_GLITCH_SESSIONS_PER_MINUTE).</summary>
    public int GlitchSessionsPerMinutePerIp { get; set; } = 10;

    /// <summary>Per-install limit for cloud-save relay writes (BBS_WH_GLITCH_SAVES_PER_HOUR). The browser
    /// singleplayer uploads on its ~2-min durable-save cadence plus tab-hide saves; 40/h leaves headroom
    /// without letting a script hammer Glitch through us.</summary>
    public int GlitchSavesPerHourPerInstall { get; set; } = 40;

    /// <summary>True when the arcade gateway is switched on AND has its credentials.</summary>
    public bool GlitchConfigured =>
        GlitchEnabled && GlitchTitleId.Length > 0 && GlitchTitleToken.Length > 0;

    /// <summary>Splits <see cref="TrustedProxies"/> into the two shapes the ForwardedHeaders middleware
    /// takes: CIDR entries (containing '/') become networks, bare addresses become single proxies. Parsed
    /// eagerly at startup so a typo'd BBS_WH_TRUSTED_PROXIES fails the launch loudly — a silently dropped
    /// entry would either reopen the spoofing hole or collapse every player onto the proxy's rate-limit
    /// bucket, and neither failure announces itself.</summary>
    public static (List<System.Net.IPNetwork> Networks, List<System.Net.IPAddress> Proxies) ParseTrustedProxies(IEnumerable<string> entries)
    {
        var networks = new List<System.Net.IPNetwork>();
        var proxies = new List<System.Net.IPAddress>();
        foreach (string entry in entries)
        {
            try
            {
                if (entry.Contains('/'))
                {
                    networks.Add(System.Net.IPNetwork.Parse(entry));
                }
                else
                {
                    proxies.Add(System.Net.IPAddress.Parse(entry));
                }
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException($"BBS_WH_TRUSTED_PROXIES entry '{entry}' is not a valid IP address or CIDR network.", ex);
            }
        }

        return (networks, proxies);
    }

    /// <summary>Loads config from BBS_WH_* environment variables over the defaults.</summary>
    public static WorldHostConfig FromEnvironment()
    {
        var c = new WorldHostConfig();

        static string? Env(string name)
        {
            var v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(v) ? null : v;
        }

        if (Env("BBS_WH_BIND") is { } bind) { c.BindAddress = bind; }
        if (Env("BBS_WH_PORT") is { } portStr && int.TryParse(portStr, out var port)) { c.Port = port; }
        if (Env("BBS_WH_BASE_DOMAIN") is { } domain) { c.BaseDomain = domain; }
        if (Env("BBS_WH_PUBLIC_HOST") is { } publicHost) { c.PublicHost = publicHost; }

        // "-" turns the website link off. An UNSET (or empty) variable deliberately keeps the built-in
        // default instead: the fleet compose forwards these unconditionally, and an operator who never
        // filled them in must not silently lose the link.
        if (Env("BBS_WH_WEBSITE_URL") is { } site) { c.WebsiteUrl = site.Trim() == "-" ? string.Empty : site.Trim(); }
        if (Env("BBS_WH_WEBSITE_URL_EN") is { } siteEn) { c.WebsiteUrlEn = siteEn.Trim() == "-" ? string.Empty : siteEn.Trim(); }
        if (Env("BBS_WH_SERVER_IMAGE") is { } image) { c.ServerImage = image; }
        if (Env("BBS_WH_DOCKER_NETWORK") is { } network) { c.DockerNetwork = network; }
        if (Env("BBS_WH_PORT_RANGE_START") is { } rsStr && int.TryParse(rsStr, out var rs)) { c.PortRangeStart = rs; }
        if (Env("BBS_WH_PORT_RANGE_SIZE") is { } rzStr && int.TryParse(rzStr, out var rz)) { c.PortRangeSize = rz; }
        if (Env("BBS_WH_MAX_WORLDS_PER_ACCOUNT") is { } mwStr && int.TryParse(mwStr, out var mw)) { c.MaxWorldsPerAccount = mw; }
        if (Env("BBS_WH_MAX_PLAYERS") is { } mpStr && int.TryParse(mpStr, out var mp)) { c.MaxPlayersPerWorld = mp; }
        if (Env("BBS_WH_IDLE_MINUTES") is { } idleStr && int.TryParse(idleStr, out var idle)) { c.IdleShutdownMinutes = idle; }
        if (Env("BBS_WH_CHUNK_STREAM_BUDGET_MS") is { } csbStr && double.TryParse(csbStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var csb) && csb >= 0) { c.ChunkStreamBudgetMs = csb; }
        if (Env("BBS_WH_WAKE_TIMEOUT_SECONDS") is { } wtStr && int.TryParse(wtStr, out var wt)) { c.WakeTimeoutSeconds = wt; }
        if (Env("BBS_WH_SESSION_DAYS") is { } sdStr && int.TryParse(sdStr, out var sd)) { c.SessionDays = sd; }
        if (Env("BBS_WH_RESERVED_NAMES") is { } reserved)
        {
            c.ReservedNames = reserved.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
        }

        if (Env("BBS_WH_RESERVED_CLAIM_CODE") is { } claimCode) { c.ReservedClaimCode = claimCode; }
        if (Env("BBS_WH_DATA_DIR") is { } dataDir) { c.DataDir = dataDir; }
        if (Env("BBS_WH_WORLDS_DIR") is { } worldsDir) { c.WorldsDir = worldsDir; }
        if (Env("BBS_WH_TERMS_VERSION") is { } tvStr && int.TryParse(tvStr, out var tv)) { c.TermsVersion = tv; }
        if (Env("BBS_WH_UPLOAD_MAX_BYTES") is { } upStr && long.TryParse(upStr, out var up)) { c.UploadMaxBytes = up; }
        if (Env("BBS_WH_ADMIN_TOKEN") is { } adminToken) { c.AdminToken = adminToken; }
        if (Env("BBS_WH_ARCHIVE_MONTHS") is { } amStr && int.TryParse(amStr, out var am)) { c.ArchiveAfterMonths = am; }
        if (Env("BBS_WH_BLOCKED_WORDS") is { } blocked)
        {
            c.BlockedNameWords.AddRange(blocked.Split(',').Select(w => w.Trim()).Where(w => w.Length > 0));
        }

        if (Env("BBS_WH_SIGNUPS_PER_HOUR") is { } suStr && int.TryParse(suStr, out var su)) { c.SignupPerHourPerIp = su; }
        if (Env("BBS_WH_LOGINS_PER_MINUTE") is { } liStr && int.TryParse(liStr, out var li)) { c.LoginPerMinutePerIp = li; }
        if (Env("BBS_WH_LOGIN_FAILS_PER_15_MIN") is { } lfStr && int.TryParse(lfStr, out var lf)) { c.LoginFailsPer15MinPerAccount = lf; }
        if (Env("BBS_WH_TRUSTED_PROXIES") is { } proxies)
        {
            c.TrustedProxies = string.Equals(proxies.Trim(), "none", StringComparison.OrdinalIgnoreCase)
                ? new List<string>()
                : proxies.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        }
        if (Env("BBS_WH_UPLOADS_PER_HOUR") is { } ulStr && int.TryParse(ulStr, out var ul)) { c.UploadsPerHourPerAccount = ul; }
        if (Env("BBS_WH_REPORTS_PER_HOUR") is { } rpStr && int.TryParse(rpStr, out var rp)) { c.ReportsPerHourPerAccount = rp; }
        if (Env("BBS_WH_STATS_PER_MINUTE") is { } spStr && int.TryParse(spStr, out var sp)) { c.StatsPerMinutePerIp = sp; }
        if (Env("BBS_WH_STATS_CACHE_SECONDS") is { } scStr && int.TryParse(scStr, out var sc)) { c.StatsCacheSeconds = sc; }
        if (Env("BBS_WH_LEGAL_NAME") is { } legalName) { c.LegalName = legalName; }
        if (Env("BBS_WH_LEGAL_ADDRESS") is { } legalAddress) { c.LegalAddress = legalAddress; }
        if (Env("BBS_WH_LEGAL_EMAIL") is { } legalEmail) { c.LegalEmail = legalEmail; }
        if (Env("BBS_WH_ANNOUNCE_TOKEN") is { } announceToken) { c.AnnounceToken = announceToken; }
        if (Env("BBS_WH_FLEET_ADMINS") is { } fleetAdmins) { c.FleetAdmins = fleetAdmins; }
        if (Env("BBS_WH_CRASH_REPORT_KEY") is { } crashKey) { c.CrashReportKey = crashKey; }
        if (Env("BBS_WH_CRASH_REPORT_ENDPOINT") is { } crashEndpoint) { c.CrashReportEndpoint = crashEndpoint; }
        if (Env("BBS_WH_AI_BACKEND_URL") is { } aiUrl) { c.AiBackendUrl = aiUrl; }
        if (Env("BBS_WH_AI_LEVEL") is { } aiLevel) { c.AiLevel = aiLevel; }
        if (Env("BBS_WH_INSTANCE_MEMORY") is { } mem) { c.InstanceMemory = mem == "none" ? string.Empty : mem; }
        if (Env("BBS_WH_INSTANCE_CPUS") is { } cpus) { c.InstanceCpus = cpus == "none" ? string.Empty : cpus; }
        if (Env("BBS_WH_MAX_ACTIVE") is { } maStr && int.TryParse(maStr, out var ma)) { c.MaxActiveInstances = ma; }
        if (Env("BBS_WH_PROBE_VIA_NETWORK") is { } pvnStr && bool.TryParse(pvnStr, out var pvn)) { c.ProbeViaDockerNetwork = pvn; }
        if (Env("BBS_WH_ADMIN_USER") is { } adminUser) { c.AdminUser = adminUser; }
        if (Env("BBS_WH_ADMIN_PASSWORD") is { } adminPassword) { c.AdminPassword = adminPassword; }
        if (Env("BBS_WH_WEBGL_DIR") is { } webglDir) { c.WebGlDir = webglDir; }
        if (Env("BBS_WH_GLITCH_ENABLED") is { } geStr && bool.TryParse(geStr, out var ge)) { c.GlitchEnabled = ge; }
        if (Env("BBS_WH_GLITCH_TITLE_ID") is { } gTitleId) { c.GlitchTitleId = gTitleId; }
        if (Env("BBS_WH_GLITCH_TITLE_TOKEN") is { } gTitleToken) { c.GlitchTitleToken = gTitleToken; }
        if (Env("BBS_WH_GLITCH_WORLDS") is { } gwStr && int.TryParse(gwStr, out var gw)) { c.GlitchWorldCount = gw; }
        if (Env("BBS_WH_GLITCH_MAX_PLAYERS") is { } gmpStr && int.TryParse(gmpStr, out var gmp)) { c.GlitchMaxPlayers = gmp; }
        if (Env("BBS_WH_GLITCH_ALLOWED_ORIGINS") is { } gOrigins)
        {
            c.GlitchAllowedOrigins = gOrigins.Split(',').Select(o => o.Trim().TrimEnd('/')).Where(o => o.Length > 0).ToList();
        }

        if (Env("BBS_WH_GLITCH_API_URL") is { } gApi) { c.GlitchApiBaseUrl = gApi; }
        if (Env("BBS_WH_GLITCH_SESSIONS_PER_MINUTE") is { } gspStr && int.TryParse(gspStr, out var gsp)) { c.GlitchSessionsPerMinutePerIp = gsp; }
        if (Env("BBS_WH_GLITCH_SAVES_PER_HOUR") is { } gsvStr && int.TryParse(gsvStr, out var gsv)) { c.GlitchSavesPerHourPerInstall = gsv; }
        if (Env("BBS_WH_GLITCH_KEEP_AWAKE") is { } gkaStr && bool.TryParse(gkaStr, out var gka)) { c.GlitchKeepAwake = gka; }

        c.ReserveFleetAdminNames();
        return c;
    }

    /// <summary>True when <paramref name="playerName"/> is one of the configured fleet-admin names.
    /// Case-insensitive on purpose: the game server matches the name the same way, and a silent
    /// `marcel` ≠ `Marcel` mismatch would grant nothing without any error anywhere.</summary>
    public bool IsFleetAdminName(string? playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName) || string.IsNullOrWhiteSpace(FleetAdmins))
        {
            return false;
        }

        string wanted = playerName.Trim();
        foreach (var name in FleetAdmins.Split(','))
        {
            if (string.Equals(name.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Adds every fleet-admin name to <see cref="ReservedNames"/> (child-safety hardening,
    /// issue #495): fleet-admin power is granted by player NAME, so the name itself must be unclaimable
    /// by anyone but a developer account. Without this, an operator who sets <c>BBS_WH_FLEET_ADMINS</c>
    /// to a name outside the default reserved list would leave that name — and with it invisible-observer
    /// access — open for any kid to register. Called at config load; idempotent.</summary>
    public void ReserveFleetAdminNames()
    {
        if (string.IsNullOrWhiteSpace(FleetAdmins))
        {
            return;
        }

        foreach (var name in FleetAdmins.Split(','))
        {
            string trimmed = name.Trim();
            if (trimmed.Length > 0
                && !ReservedNames.Any(r => string.Equals(r, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                ReservedNames.Add(trimmed);
            }
        }
    }
}
