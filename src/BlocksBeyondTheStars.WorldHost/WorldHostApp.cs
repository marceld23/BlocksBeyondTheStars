// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Microsoft.AspNetCore.HttpOverrides;

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// The WorldHost's HTTP surface (#2902) — <see cref="Create"/> wires the <see cref="WebApplication"/> the entry point
/// runs and the tests start in-process on a loopback port: the builder, the trusted-proxy forwarded headers, then the
/// six route groups (<see cref="PortalEndpoints"/>, <see cref="GlitchEndpoints"/>, <see cref="AccountEndpoints"/>,
/// <see cref="ModerationEndpoints"/>, <see cref="AdminUiEndpoints"/>, <see cref="WorldEndpoints"/>) over one
/// <see cref="WorldHostServices"/>. Program.cs only reads the environment, builds the collaborators and calls
/// <see cref="Create"/>; the background work (glitch keep-awake pass, reaper) stays there, so a test never starts it.
/// </summary>
public static class WorldHostApp
{
    /// <summary>Builds the configured app (not yet started). <paramref name="services"/> stays owned by the caller;
    /// <paramref name="args"/> are the command-line arguments handed to the host builder.</summary>
    public static WebApplication Create(WorldHostServices services, string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.WebHost.UseUrls($"http://{services.Config.BindAddress}:{services.Config.Port}");

        // Honor X-Forwarded-* from the fronting Caddy so rate limits key on the real client IP, not the proxy —
        // but ONLY from the configured trusted proxies (#418). The lists were previously cleared, which in this
        // middleware means "trust ANY peer": whoever could reach the bind directly could rotate a fabricated
        // X-Forwarded-For per request and mint a fresh rate-limit bucket every time. ForwardLimit stays at its
        // default 1, so even a proxy that appends to an attacker-supplied header only ever advances one hop —
        // the one the trusted proxy itself wrote. An empty trusted list (BBS_WH_TRUSTED_PROXIES=none) skips the
        // middleware entirely: limits then key on the immediate peer, which is at least never attacker-chosen.
        var (trustedNetworks, trustedProxies) = WorldHostConfig.ParseTrustedProxies(services.Config.TrustedProxies);
        bool honorForwardedHeaders = trustedNetworks.Count + trustedProxies.Count > 0;
        if (honorForwardedHeaders)
        {
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
                trustedNetworks.ForEach(options.KnownIPNetworks.Add);
                trustedProxies.ForEach(options.KnownProxies.Add);
            });
        }

        var app = builder.Build();
        if (honorForwardedHeaders)
        {
            app.UseForwardedHeaders();
        }
        var log = app.Logger;

        PortalEndpoints.Map(app, services, log);
        GlitchEndpoints.Map(app, services, log);
        AccountEndpoints.Map(app, services, log);
        ModerationEndpoints.Map(app, services, log);
        AdminUiEndpoints.Map(app, services, log);
        WorldEndpoints.Map(app, services, log);

        return app;
    }
}
