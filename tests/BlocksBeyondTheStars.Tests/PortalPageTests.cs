// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Api;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

public sealed class PortalPageTests
{
    [Theory]
    [InlineData("/download")]        // Windows
    [InlineData("/download-linux")]  // Linux AppImage
    [InlineData("/download-mac")]    // macOS zip (experimental)
    public void Render_OffersEveryPlatformDownloadLink(string route)
    {
        string html = PortalPage.Render("TestServer", "TestWorld", 31415, "http://localhost:31416");

        Assert.Contains($"href='{route}'", html);
    }

    [Fact]
    public void Render_SubstitutesServerWorldAndBaseUrl()
    {
        string html = PortalPage.Render("MyServer", "MyWorld", 31415, "http://example:31416");

        Assert.Contains("MyServer", html);
        Assert.Contains("MyWorld", html);
        Assert.Contains("http://example:31416/updates", html);
        // The known placeholder tokens must all be replaced in the served page.
        Assert.DoesNotContain("__SERVER__", html);
        Assert.DoesNotContain("__WORLD__", html);
        Assert.DoesNotContain("__PORT__", html);
        Assert.DoesNotContain("__BASEURL__", html);
    }
}
