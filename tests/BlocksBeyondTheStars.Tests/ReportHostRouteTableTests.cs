// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlocksBeyondTheStars.ReportHost;
using BlocksBeyondTheStars.Shared.Notifications;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The ReportHost's complete route table (#2903): every METHOD + pattern <see cref="ReportHostApp.Create"/>
/// maps, read from the app's endpoint data sources. Pinned before <c>Create</c> was split into route groups
/// so the split provably neither lost, added nor renamed a route. Both sides are sorted, so the order in
/// which the groups register their routes is deliberately not pinned (ASP.NET routing is precedence-based).
/// </summary>
[Collection(RealTimeSensitiveCollection.Name)] // the host's async dispose is billed the parallel queue wait otherwise (#1362)
public sealed class ReportHostRouteTableTests
{
    private static readonly string[] Expected =
    {
        "DELETE /api/reports/{id}",
        "GET /",
        "GET /admin",
        "GET /admin/export",
        "GET /admin/report/{id}",
        "GET /admin/report/{id}/attachment/{index:int}",
        "GET /admin/report/{id}/attachment/{index:int}/view",
        "GET /admin/report/{id}/screenshot",
        "GET /api/replies",
        "GET /api/reports",
        "GET /api/reports/{id}",
        "GET /api/reports/{id}/attachment/{index:int}",
        "GET /api/reports/{id}/screenshot",
        "GET /healthz",
        "PATCH /api/reports/{id}",
        "POST /admin/report/{id}/delete",
        "POST /admin/report/{id}/reply",
        "POST /admin/report/{id}/status",
        "POST /api/bugreport",
        "POST /api/replies",
        "POST /api/replies/ack",
        "POST /api/reports/{id}/replies",
    };

    [Fact]
    public async Task Create_MapsExactlyTheDocumentedRoutesAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "bbts_rhr_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var config = new ReportHostConfig { BindAddress = "127.0.0.1", Port = 0, DataDir = root };
            using var store = new ReportStore(config, Path.Combine(root, "reports.db"));
            await using var app = ReportHostApp.Create(config, store, new AdminNotifier(string.Empty, "reports"), Array.Empty<string>());

            string[] routes = ((IEndpointRouteBuilder)app).DataSources
                .SelectMany(ds => ds.Endpoints)
                .OfType<RouteEndpoint>()
                .Select(e => string.Join(",", e.Metadata.GetRequiredMetadata<HttpMethodMetadata>().HttpMethods) + " " + e.RoutePattern.RawText)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(22, Expected.Length);
            Assert.Equal(Expected.OrderBy(x => x, StringComparer.Ordinal), routes);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Windows can hold SQLite WAL handles briefly; temp cleanup is best-effort.
            }
        }
    }
}
