// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Portal;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Response parsing of the worlds-portal client (Official Worlds menu): the exact JSON shapes the
/// WorldHost API emits, plus the error paths (server error body, unauthorized, offline).
/// </summary>
public sealed class PortalClientTests
{
    [Fact]
    public void ParseLogin_ReadsSessionAndTermsFlag()
    {
        var ok = PortalClient.ParseLogin(200, "{\"accountId\":\"acc-1\",\"sessionToken\":\"tok\",\"termsOutdated\":false}");
        Assert.True(ok.Ok);
        Assert.Equal("acc-1", ok.AccountId);
        Assert.Equal("tok", ok.SessionToken);
        Assert.False(ok.TermsOutdated);

        var outdated = PortalClient.ParseLogin(200, "{\"accountId\":\"acc-1\",\"sessionToken\":\"tok\",\"termsOutdated\":true}");
        Assert.True(outdated.Ok);
        Assert.True(outdated.TermsOutdated);
    }

    [Fact]
    public void ParseLogin_ReadsCanonicalAccountName_AndToleratesItsAbsence()
    {
        // Logins are case-insensitive server-side; the answer carries the stored casing for the client
        // to persist (sign-in form prefill). Older/other answers without the field must keep parsing.
        var named = PortalClient.ParseLogin(200, "{\"accountId\":\"acc-1\",\"sessionToken\":\"tok\",\"accountName\":\"Pilot\"}");
        Assert.True(named.Ok);
        Assert.Equal("Pilot", named.AccountName);

        var unnamed = PortalClient.ParseLogin(200, "{\"accountId\":\"acc-1\",\"sessionToken\":\"tok\"}");
        Assert.True(unnamed.Ok);
        Assert.Equal(string.Empty, unnamed.AccountName);
    }

    [Fact]
    public void ParseLogin_ReadsRescueCodes_AndMustChangeFlag()
    {
        // Signup answers the one-time rescue-code plaintexts; login answers the must-change nag after
        // an operator reset. Both ride the same login shape.
        var signup = PortalClient.ParseLogin(200,
            "{\"accountId\":\"acc-1\",\"sessionToken\":\"tok\",\"recoveryCodes\":[\"AB2C-DEF3\",\"GH4J-KM5N\",\"PQ6R-ST7U\"]}");
        Assert.True(signup.Ok);
        Assert.Equal(new[] { "AB2C-DEF3", "GH4J-KM5N", "PQ6R-ST7U" }, signup.RecoveryCodes);
        Assert.False(signup.MustChangePassword);

        var reset = PortalClient.ParseLogin(200,
            "{\"accountId\":\"acc-1\",\"sessionToken\":\"tok\",\"mustChangePassword\":true}");
        Assert.True(reset.Ok);
        Assert.True(reset.MustChangePassword);
        Assert.Empty(reset.RecoveryCodes);
    }

    [Fact]
    public void ParseLogin_FailurePaths()
    {
        Assert.Equal("unauthorized", PortalClient.ParseLogin(401, "").Error);
        Assert.Equal("offline", PortalClient.ParseLogin(0, "").Error);
        Assert.Equal("http_502", PortalClient.ParseLogin(502, "<html>bad gateway</html>").Error); // non-JSON proxy page
        Assert.False(PortalClient.ParseLogin(401, "").Ok);
    }

    [Fact]
    public void ParseWorlds_ReadsTheList()
    {
        var r = PortalClient.ParseWorlds(200,
            "{\"worlds\":[{\"id\":\"aabbccddee11\",\"name\":\"My World\",\"status\":\"stopped\",\"subdomain\":\"w-aabbccddee11\"}]}");
        Assert.True(r.Ok);
        var world = Assert.Single(r.Worlds);
        Assert.Equal("aabbccddee11", world.Id);
        Assert.Equal("My World", world.Name);
        Assert.Equal("stopped", world.Status);
        Assert.False(world.IsPublic); // absent flag → private
    }

    [Fact]
    public void ParseWorlds_ReadsPasswordAndPublicFlags()
    {
        var r = PortalClient.ParseWorlds(200,
            "{\"worlds\":[{\"id\":\"aabbccddee11\",\"name\":\"Family\",\"status\":\"running\",\"hasPassword\":true,\"isPublic\":true}]}");
        Assert.True(r.Ok);
        var world = Assert.Single(r.Worlds);
        Assert.True(world.HasPassword);
        Assert.True(world.IsPublic);
    }

    [Fact]
    public void ParseJoin_ReadsTheGrant_AndSurfacesPlayerSafeErrors()
    {
        var r = PortalClient.ParseJoin(200,
            "{\"worldId\":\"aabbccddee11\",\"displayName\":\"My World\",\"wssUrl\":\"wss://w-aabbccddee11.play.example.de\"," +
            "\"nativeHost\":\"play.example.de\",\"nativePort\":32000,\"joinToken\":\"v1.a.b.1.C\",\"tokenExpiresUnix\":1}");
        Assert.True(r.Ok);
        Assert.Equal("play.example.de", r.NativeHost);
        Assert.Equal(32000, r.NativePort);
        Assert.Equal("v1.a.b.1.C", r.JoinToken);
        Assert.StartsWith("wss://", r.WssUrl, System.StringComparison.Ordinal);

        // The wake-failed path: WorldHost answers 503 with a player-safe error text.
        var failed = PortalClient.ParseJoin(503, "{\"error\":\"The world did not come up in time — please try again.\"}");
        Assert.False(failed.Ok);
        Assert.Contains("did not come up", failed.Error, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ParseSimple_CoversReportOutcomes()
    {
        Assert.True(PortalClient.ParseSimple(200, "").Ok);
        var bad = PortalClient.ParseSimple(400, "{\"error\":\"Unknown report category.\"}");
        Assert.False(bad.Ok);
        Assert.Equal("Unknown report category.", bad.Error);
    }

    [Fact]
    public void ParseKick_SeparatesTheRequestFromTheOutcome()
    {
        // The action succeeding says nothing about whether anyone was thrown out (#502): the player may be
        // offline, the world asleep, or the instance still on an image without the kick endpoint. The UI
        // has to report `kicked`, not the HTTP status, or a block reads as "gone" when nothing happened.
        Assert.True(PortalClient.ParseKick(200, "{\"kicked\":true}").Kicked);
        var missed = PortalClient.ParseKick(200, "{\"ok\":true,\"kicked\":false}");
        Assert.True(missed.Ok);
        Assert.False(missed.Kicked);

        // An older WorldHost that answers a bare 200 must read as "not kicked", never as success.
        Assert.False(PortalClient.ParseKick(200, "{}").Kicked);

        var denied = PortalClient.ParseKick(403, "{\"error\":\"This player name is reserved.\",\"code\":\"name_reserved\"}");
        Assert.False(denied.Ok);
        Assert.Equal("name_reserved", denied.Code);
    }

    // ---------------- Portal parity (#268-#270) ----------------

    [Fact]
    public void ParseTerms_ReadsVersionAndBothLanguages()
    {
        var r = PortalClient.ParseTerms(200, "{\"version\":3,\"textDe\":\"Sei nett.\",\"textEn\":\"Be kind.\"}");
        Assert.True(r.Ok);
        Assert.Equal(3, r.Version);
        Assert.Equal("Sei nett.", r.TextDe);
        Assert.Equal("Be kind.", r.TextEn);

        Assert.Equal("offline", PortalClient.ParseTerms(0, "").Error); // portal unreachable → signup blocked with a clear message
    }

    [Fact]
    public void ParseLogin_CoversSignupOutcomes()
    {
        // A successful signup answers exactly like a login (fresh session, no termsOutdated flag).
        var ok = PortalClient.ParseLogin(200, "{\"accountId\":\"acc-9\",\"sessionToken\":\"tok-9\"}");
        Assert.True(ok.Ok);
        Assert.Equal("tok-9", ok.SessionToken);
        Assert.False(ok.TermsOutdated);

        // Server-side validation errors surface their stable machine code for localization.
        var taken = PortalClient.ParseLogin(400, "{\"error\":\"This name is already taken.\",\"code\":\"name_taken\"}");
        Assert.False(taken.Ok);
        Assert.Equal("name_taken", taken.Code);
        var rules = PortalClient.ParseLogin(400, "{\"error\":\"Please accept the community rules to create an account.\",\"code\":\"accept_rules\"}");
        Assert.Equal("accept_rules", rules.Code);
    }

    [Fact]
    public void ParseWorld_ReadsTheCreatedWorld_AndTheQuotaError()
    {
        var r = PortalClient.ParseWorld(200,
            "{\"id\":\"ffeeddccbb22\",\"name\":\"New Home\",\"status\":\"stopped\",\"subdomain\":\"w-ffeeddccbb22\",\"hasPassword\":true}");
        Assert.True(r.Ok);
        Assert.NotNull(r.World);
        Assert.Equal("ffeeddccbb22", r.World!.Id);
        Assert.Equal("New Home", r.World.Name);
        Assert.Equal("stopped", r.World.Status);
        Assert.True(r.World.HasPassword);

        var full = PortalClient.ParseWorld(400, "{\"error\":\"World limit reached (2 per account).\",\"code\":\"world_limit\"}");
        Assert.False(full.Ok);
        Assert.Null(full.World);
        Assert.Equal("world_limit", full.Code);
    }

    [Fact]
    public void ParseSave_ReturnsRawBytes_AndDecodesErrorEnvelopes()
    {
        byte[] payload = { 0x53, 0x51, 0x4C, 0x69, 0x74, 0x65 }; // "SQLite" — save downloads are raw bytes, not JSON
        var ok = PortalClient.ParseSave(200, payload);
        Assert.True(ok.Ok);
        Assert.Equal(payload, ok.Bytes);

        var running = PortalClient.ParseSave(400,
            System.Text.Encoding.UTF8.GetBytes("{\"error\":\"Stop the world before downloading its save.\",\"code\":\"stop_first\"}"));
        Assert.False(running.Ok);
        Assert.Equal("stop_first", running.Code);
        Assert.Empty(running.Bytes);

        Assert.Equal("offline", PortalClient.ParseSave(0, System.Array.Empty<byte>()).Error);
    }
}
