// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The shared HTTP stub: answers every request with one canned status and body, and records the last request's
/// URL and body.</summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _code;
    private readonly string _body;
    public string? Url;
    public string? RequestBody;

    public StubHttpHandler(HttpStatusCode code, string body) { _code = code; _body = body; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Url = request.RequestUri?.ToString();
        RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(_code) { Content = new StringContent(_body) };
    }
}
