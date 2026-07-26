// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Concurrent;
using BlocksBeyondTheStars.Shared.Security;

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>Everything a client needs to enter a hosted world: the wss endpoint for browsers, the
/// host:port for native UDP clients, and a short-lived join token proving the control plane vouched
/// for this player.</summary>
public sealed record JoinGrant(
    string WorldId,
    string DisplayName,
    string WssUrl,
    string NativeHost,
    int NativePort,
    string JoinToken,
    long TokenExpiresUnix);

/// <summary>
/// The allocation core of the control plane: "give me world X" — route to the running instance, or wake
/// it (start container, wait for its /status to answer) and then route. Per-world locking serializes
/// concurrent wakes of the same world; different worlds wake in parallel. The instance does the rest
/// itself (idle shutdown, join-token enforcement, owner bootstrap — the Phase-0 server features).
/// </summary>
public sealed class WorldOrchestrator
{
    /// <summary>Join tokens are one-shot handshake material, so a short life keeps a leaked token
    /// near-useless — but the browser deep-link must survive the FIRST WebGL download (tens of MB on a
    /// slow line): the token is minted before the page loads and verified only once the engine joins.
    /// 10 minutes covers that cold path; native joins still use it within seconds.</summary>
    private const int JoinTokenTtlSeconds = 600;

    /// <summary>Wrong-password budget per account+world before the cooldown answer: generous enough for a
    /// family fumbling a shared password, tight enough to make guessing pointless (the window is 15 min).</summary>
    private const int PasswordAttemptsPerWindow = 10;

    private readonly WorldHostConfig _config;
    private readonly HostRegistry _registry;
    private readonly IInstanceLauncher _launcher;
    private readonly Func<WorldRecord, Task<bool>> _healthProbe;
    private readonly WorldHostMetrics _metrics;
    private readonly RateLimiter _passwordAttempts;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _wakeLocks = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public WorldOrchestrator(
        WorldHostConfig config,
        HostRegistry registry,
        IInstanceLauncher launcher,
        Func<WorldRecord, Task<bool>>? healthProbe = null,
        WorldHostMetrics? metrics = null,
        RateLimiter? passwordAttempts = null)
    {
        _config = config;
        _registry = registry;
        _launcher = launcher;
        _healthProbe = healthProbe ?? DefaultProbeAsync;
        _metrics = metrics ?? new WorldHostMetrics();
        _passwordAttempts = passwordAttempts ?? new RateLimiter(PasswordAttemptsPerWindow, TimeSpan.FromMinutes(15));
    }

    /// <summary>Default probe: the instance's WS gateway answers /status once the server is up. Two
    /// routes to it: host loopback via the world's published tcp port (WorldHost running ON the host,
    /// dev), or the container's name on the shared docker network (WorldHost running IN a container —
    /// its loopback can never reach host-published ports, so BBS_WH_PROBE_VIA_NETWORK is required there).</summary>
    private async Task<bool> DefaultProbeAsync(WorldRecord world)
    {
        string url = _config.ProbeViaDockerNetwork
            ? $"http://bbs-world-{world.Id}:31415/status"
            : $"http://127.0.0.1:{world.HostPort}/status";
        try
        {
            using var response = await Http.GetAsync(url).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Reads an instance's live /status JSON (joined players etc.) for the admin UI; null when
    /// the instance is unreachable. Same routing rule as the health probe.</summary>
    public async Task<string?> ReadInstanceStatusAsync(WorldRecord world)
    {
        string url = _config.ProbeViaDockerNetwork
            ? $"http://bbs-world-{world.Id}:31415/status"
            : $"http://127.0.0.1:{world.HostPort}/status";
        try
        {
            using var response = await Http.GetAsync(url).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync().ConfigureAwait(false) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Ensures the world's instance is up (waking it if needed) and returns the join grant for
    /// this player, or a player-safe error.</summary>
    public async Task<(JoinGrant? Grant, string Error)> JoinAsync(string worldId, AccountRecord account, string playerName, string? password = null)
    {
        playerName = (playerName ?? string.Empty).Trim();
        if (playerName.Length is < 1 or > 24 || playerName.Any(char.IsControl))
        {
            return (null, "Player name must be 1-24 printable characters.");
        }

        // Developer-reserved names are protected as IN-GAME identities too, not only as account names —
        // otherwise any account could impersonate "Justus" inside a world. Developer accounts (claimed
        // with the operator's code at signup) may use them freely.
        if (!account.IsDeveloper && _registry.IsReservedName(playerName))
        {
            return (null, "This player name is reserved.");
        }

        // Kid-facing name hygiene applies to in-game identities too, not only account/world names.
        if (_registry.IsBlockedName(playerName))
        {
            return (null, "Please choose a different player name.");
        }

        // Community-rules enforcement happens at the join grant — the choke point every hosted-world
        // entry goes through, regardless of which client asked.
        if (account.IsBanned)
        {
            return (null, string.IsNullOrEmpty(account.BanReason)
                ? "This account is banned."
                : $"This account is banned: {account.BanReason}");
        }

        if (account.AcceptedTermsVersion < _config.TermsVersion)
        {
            return (null, "The community rules have changed — please accept them on the portal first.");
        }

        // World password gate (#250/#251) — enforced BEFORE the wake, at token issuance (the one choke
        // point every hosted join passes), so an unauthorized join can neither enter nor wake the world.
        // The owner always bypasses their own world's password.
        if (_registry.GetWorld(worldId) is { } gated
            && !string.IsNullOrEmpty(gated.PasswordHash)
            && gated.OwnerAccountId != account.Id)
        {
            string attemptKey = account.Id + "|" + gated.Id;
            if (_passwordAttempts.IsExhausted(attemptKey))
            {
                return (null, "Too many password attempts — please wait a few minutes.");
            }

            if (string.IsNullOrEmpty(password))
            {
                // The normal first contact with a protected world — the client shows the prompt on this
                // answer, so an empty try costs no attempt budget.
                return (null, "This world needs a password.");
            }

            if (!PasswordHasher.Verify(password, gated.PasswordHash))
            {
                _passwordAttempts.TryPass(attemptKey); // burn budget on WRONG guesses only
                return (null, "Wrong world password.");
            }
        }

        var (world, error) = await EnsureRunningAsync(worldId).ConfigureAwait(false);
        if (world is null)
        {
            return (null, error);
        }

        _metrics.JoinGranted();
        _registry.TouchWorldActive(world.Id); // real player interest — resets the archive-inactivity clock

        long expires = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + JoinTokenTtlSeconds;
        string token = HostedJoinToken.Create(world.JoinSecret, world.Id, account.Id, playerName, expires);
        return (new JoinGrant(
            WorldId: world.Id,
            DisplayName: world.DisplayName,
            WssUrl: $"wss://{world.Subdomain}.{_config.BaseDomain}",
            NativeHost: _config.PublicHost,
            NativePort: world.HostPort,
            JoinToken: token,
            TokenExpiresUnix: expires), string.Empty);
    }

    /// <summary>Route-or-wake: the running instance is reused; a stopped (or crashed-out) one is started
    /// and awaited until its /status probe answers or the wake timeout expires.</summary>
    public async Task<(WorldRecord? World, string Error)> EnsureRunningAsync(string worldId)
    {
        if (_registry.GetWorld(worldId) is not { } world)
        {
            return (null, "World not found.");
        }

        var gate = _wakeLocks.GetOrAdd(world.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            world = _registry.GetWorld(world.Id)!; // re-read under the lock: a parallel join may have woken it

            if (world.Status is WorldStatus.Running or WorldStatus.Starting && _launcher.IsRunning(world.ContainerId))
            {
                if (world.Status == WorldStatus.Starting)
                {
                    return await AwaitHealthyAsync(world).ConfigureAwait(false);
                }

                return (world, string.Empty);
            }

            // Archived world: transparently restore its saves first — from the player's side an archived
            // world is just one that takes a moment longer to wake.
            if (world.Status == WorldStatus.Archived)
            {
                SavePaths.RestoreFromArchive(_config, world.Id);
                _registry.SetWorldStatus(world.Id, WorldStatus.Stopped, string.Empty);
            }

            // Registry says active but the container is gone (idle shutdown, crash, host reboot) — reconcile,
            // then fall through to a fresh start.
            if (world.Status != WorldStatus.Stopped && world.Status != WorldStatus.Archived)
            {
                _registry.SetWorldStatus(world.Id, WorldStatus.Stopped, string.Empty);
            }

            // Capacity gate: per-instance memory caps bound ONE world, this bounds the SUM — MaxActive ×
            // InstanceMemory is sized to fit the host, so overload becomes a friendly "try again" instead
            // of an OOM lottery. Already-running worlds are unaffected (they returned above).
            if (_config.MaxActiveInstances > 0 && _registry.ListActiveWorlds().Count >= _config.MaxActiveInstances)
            {
                return (null, "No capacity available right now — please try again later.");
            }

            string containerId;
            try
            {
                _registry.SetWorldStatus(world.Id, WorldStatus.Starting, string.Empty);
                containerId = _launcher.Start(world);
                _registry.SetWorldStatus(world.Id, WorldStatus.Starting, containerId);
            }
            catch (Exception)
            {
                _registry.SetWorldStatus(world.Id, WorldStatus.Stopped, string.Empty);
                return (null, "The world could not be started — please try again in a moment.");
            }

            _metrics.Woke();
            return await AwaitHealthyAsync(_registry.GetWorld(world.Id)!).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<(WorldRecord? World, string Error)> AwaitHealthyAsync(WorldRecord world)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(_config.WakeTimeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await _healthProbe(world).ConfigureAwait(false))
            {
                _registry.SetWorldStatus(world.Id, WorldStatus.Running, world.ContainerId);
                return (_registry.GetWorld(world.Id), string.Empty);
            }

            if (!_launcher.IsRunning(world.ContainerId))
            {
                break; // died during boot — no point waiting out the timeout
            }

            await Task.Delay(500).ConfigureAwait(false);
        }

        _launcher.Stop(world.ContainerId);
        _registry.SetWorldStatus(world.Id, WorldStatus.Stopped, string.Empty);
        return (null, "The world did not come up in time — please try again.");
    }

    /// <summary>Stops a world's instance on request (the owner's "stop now"; the usual path is the
    /// instance's own idle shutdown).</summary>
    public void StopWorld(WorldRecord world)
    {
        _launcher.Stop(world.ContainerId);
        _registry.SetWorldStatus(world.Id, WorldStatus.Stopped, string.Empty);
    }

    /// <summary>
    /// Removes a world for good: stop the instance first (so no container keeps running under a deleted
    /// registry row), drop the now-unreachable container object, optionally erase the saves, then delete
    /// the registry row last — a crash mid-way leaves a stopped world, never an orphan container.
    /// <paramref name="purgeSaves"/> false keeps <c>&lt;WorldsDir&gt;/&lt;id&gt;</c> (and the archive copy)
    /// on disk for manual recovery, matching the owner-facing delete; true erases both.
    /// </summary>
    public void DeleteWorld(WorldRecord world, bool purgeSaves)
    {
        StopWorld(world);
        _launcher.Remove(world.Id);
        if (purgeSaves)
        {
            SavePaths.DeleteWorldData(_config, world.Id);
        }

        _registry.DeleteWorld(world.Id);
    }

    /// <summary>Pushes a maintenance announcement (#249) into one running instance via its token-gated
    /// POST /announce. Kind: 0 = info, 1 = restart countdown of <paramref name="seconds"/>, 2 = cancel.
    /// False when announcements are unconfigured or the instance did not accept.</summary>
    public async Task<bool> AnnounceInstanceAsync(WorldRecord world, byte kind, string? text, int seconds)
    {
        if (string.IsNullOrEmpty(_config.AnnounceToken))
        {
            return false;
        }

        string url = _config.ProbeViaDockerNetwork
            ? $"http://bbs-world-{world.Id}:31415/announce"
            : $"http://127.0.0.1:{world.HostPort}/announce";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("X-Announce-Token", _config.AnnounceToken);
            request.Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new { kind, text = text ?? string.Empty, seconds }),
                System.Text.Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Fans a maintenance announcement out to one world or the whole fleet (every active
    /// instance), in parallel with the shared 3 s HTTP timeout so one dead instance can't stall the
    /// operation. Returns (reached, targeted) for the operator's feedback line.</summary>
    public async Task<(int Reached, int Targets)> AnnounceAsync(byte kind, string? text, int seconds, string? worldId = null)
    {
        var targets = worldId is null
            ? _registry.ListActiveWorlds()
            : _registry.GetWorld(worldId) is { } single ? new List<WorldRecord> { single } : new List<WorldRecord>();

        if (targets.Count == 0)
        {
            return (0, 0);
        }

        var results = await Task.WhenAll(targets.Select(w => AnnounceInstanceAsync(w, kind, text, seconds))).ConfigureAwait(false);
        return (results.Count(ok => ok), targets.Count);
    }

    /// <summary>Reconciles registry state with reality: a world marked active whose container has exited
    /// (idle shutdown is the normal case) is marked stopped, so joins wake it cleanly and world lists tell
    /// the truth. Called periodically by the host's background loop. Each world is reconciled under its
    /// wake lock (#415): the container probe takes seconds, and writing a stale Stopped/"" over a wake
    /// that happened in that window would make the next join `docker rm -f` the freshly started container.
    /// A held lock means a join is waking or probing the world right now — skip it this pass.</summary>
    public int Reap()
    {
        int reaped = 0;
        foreach (var world in _registry.ListActiveWorlds())
        {
            var gate = _wakeLocks.GetOrAdd(world.Id, _ => new SemaphoreSlim(1, 1));
            if (!gate.Wait(0))
            {
                continue;
            }

            try
            {
                // Re-read under the lock: the listed row may predate a wake that has since run.
                if (_registry.GetWorld(world.Id) is not { } fresh
                    || fresh.Status is not (WorldStatus.Running or WorldStatus.Starting))
                {
                    continue;
                }

                if (!_launcher.IsRunning(fresh.ContainerId))
                {
                    _registry.SetWorldStatus(fresh.Id, WorldStatus.Stopped, string.Empty);
                    _registry.TouchWorldActive(fresh.Id); // it WAS just running — inactivity starts now
                    reaped++;
                }
            }
            finally
            {
                gate.Release();
            }
        }

        _metrics.Reaped(reaped);
        return reaped;
    }

    /// <summary>Archives stopped worlds whose last activity predates the configured window: saves move
    /// to the archive folder, status flips to archived. Joining later restores them transparently.
    /// Time is passed in so the sweep is testable; returns the number archived.</summary>
    public int ArchiveSweep(long nowUnix)
    {
        if (_config.ArchiveAfterMonths <= 0)
        {
            return 0;
        }

        long cutoff = nowUnix - (long)_config.ArchiveAfterMonths * 30 * 86400;
        int archived = 0;
        foreach (var world in _registry.ListArchiveCandidates(cutoff))
        {
            var gate = _wakeLocks.GetOrAdd(world.Id, _ => new SemaphoreSlim(1, 1));
            if (!gate.Wait(0))
            {
                continue; // a join is waking this world right now — clearly not archive material
            }

            try
            {
                // Re-read under the lock (#416): the candidate row may predate a wake. A woken world is
                // no longer Stopped; one that woke and idle-stopped again since the listing shows a fresh
                // last-start. Both must be spared, and the live-container guard must use the FRESH
                // container id — the stale candidate row always carries "", so IsRunning("") proves nothing.
                if (_registry.GetWorld(world.Id) is not { } fresh
                    || fresh.Status != WorldStatus.Stopped
                    || fresh.LastStartedUnix >= cutoff
                    || _launcher.IsRunning(fresh.ContainerId))
                {
                    continue;
                }

                SavePaths.MoveToArchive(_config, world.Id);
                _registry.SetWorldStatus(world.Id, WorldStatus.Archived, string.Empty);
                archived++;
            }
            finally
            {
                gate.Release();
            }
        }

        _metrics.Archived(archived);
        return archived;
    }
}
