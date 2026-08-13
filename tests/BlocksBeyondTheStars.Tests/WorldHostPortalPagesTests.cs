// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.WorldHost;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// WorldHost portal pages: server-side localization (German default, every other game language via
/// ?lang= — issues #253 and #970), the Play-button browser deep-link + the join-grant rendering order
/// that made Play look like a no-op (issue #252), the game-logo branding (issue #254), and the /play
/// WebGL serving policy helpers.
/// </summary>
public sealed class WorldHostPortalPagesTests
{
    private static readonly WorldHostConfig Config = new();

    // ---------------- Localization (#253, #970) ----------------

    [Theory]
    [InlineData(null, "de")]
    [InlineData("", "de")]
    [InlineData("de", "de")]
    [InlineData("en", "en")]
    [InlineData("fr", "fr")] // every game language is a portal language now (#970)
    [InlineData("zh", "zh")]
    [InlineData("xx", "de")] // anything unknown still falls back to the German default
    [InlineData("EN", "de")] // deliberate exact match — no case folding surprises
    public void Normalize_AcceptsEveryGameLanguage_AndDefaultsToGerman(string? input, string expected)
        => Assert.Equal(expected, PortalLocales.Normalize(input));

    [Fact]
    public void Landing_German_HasNoMixedEnglish()
    {
        string html = WorldHostPortalPages.Landing(Config);
        Assert.Contains("lang='de'", html);
        Assert.Contains("Konto erstellen", html);
        Assert.Contains("Anmelden", html);
        Assert.DoesNotContain("Create account", html);
        Assert.DoesNotContain("Sign in", html);
    }

    [Fact]
    public void Landing_English_HasNoMixedGerman()
    {
        string html = WorldHostPortalPages.Landing(Config, "en");
        Assert.Contains("lang='en'", html);
        Assert.Contains("Create account", html);
        Assert.Contains("Sign in", html);
        Assert.DoesNotContain("Konto erstellen", html);
        Assert.DoesNotContain("Anmelden", html);
        // JS navigations must keep the explicit language choice.
        Assert.Contains("const LQ = '?lang=en'", html);
    }

    [Fact]
    public void Worlds_IsFullyLocalized_PerLanguage()
    {
        string de = WorldHostPortalPages.Worlds(Config);
        Assert.Contains("Neue Welt", de);
        Assert.Contains("Spieler melden", de);
        Assert.DoesNotContain("New world", de);
        Assert.DoesNotContain("Report a player", de);
        Assert.Contains("Welt wird gestartet…", de); // JS strings localize too (injected L map)

        string en = WorldHostPortalPages.Worlds(Config, "en");
        Assert.Contains("New world", en);
        Assert.Contains("Report a player", en);
        Assert.DoesNotContain("Neue Welt", en);
        Assert.Contains("Waking the world…", en);
    }

    [Fact]
    public void Rules_ShowsOnlyTheSelectedLanguage()
    {
        string de = WorldHostPortalPages.Rules(Config);
        Assert.Contains("Sei freundlich", de);
        Assert.DoesNotContain("Be friendly", de);

        string en = WorldHostPortalPages.Rules(Config, "en");
        Assert.Contains("Be friendly", en);
        Assert.DoesNotContain("Sei freundlich", en);
    }

    [Fact]
    public void Shell_TranslatesTheApiErrorTable_ForThePagesOwnLanguage()
    {
        // The error codes used to arrive as a DE/EN pair picked by a `var de` flag; the server now
        // injects one flat map in the page's language — anything else would be a silent English page.
        string de = WorldHostPortalPages.Landing(Config);
        Assert.Contains("\"name_taken\":\"Dieser Name ist schon vergeben.\"", de);

        string en = WorldHostPortalPages.Landing(Config, "en");
        Assert.Contains("\"name_taken\":\"This name is already taken.\"", en);

        string fr = WorldHostPortalPages.Landing(Config, "fr");
        Assert.DoesNotContain("Dieser Name ist schon vergeben.", fr);
        Assert.DoesNotContain("This name is already taken.", fr);
    }

    [Fact]
    public void Shell_ShowsTheLanguagePicker_InTheHeader_AndEveryLanguageInTheFooter()
    {
        // The footer links alone were effectively invisible (below the fold, small grey text) — the
        // header control is the discoverable switcher, rendered before the page body. It is a plain
        // GET form so it still works without JavaScript.
        string de = WorldHostPortalPages.Landing(Config);
        Assert.Contains("class='langsw' method='get'", de);
        Assert.Contains("<select id='langsel' name='lang'", de);
        Assert.Contains("<noscript><button type='submit'>", de);
        Assert.Contains("<option value='de' lang='de' selected>Deutsch</option>", de);
        Assert.Contains("<option value='ja' lang='ja'>日本語</option>", de);
        Assert.True(de.IndexOf("class='langsw'", StringComparison.Ordinal)
            < de.IndexOf("<main>", StringComparison.Ordinal));

        // Footer: the current language is inert text, every other one a real link.
        Assert.Contains("<span class='cur' lang='de'>Deutsch</span>", de);
        Assert.Contains("<a href='?lang=en' lang='en'>English</a>", de);

        string en = WorldHostPortalPages.Landing(Config, "en");
        Assert.Contains("<option value='en' lang='en' selected>English</option>", en);
        Assert.Contains("<a href='?lang=de' lang='de'>Deutsch</a>", en);
    }

    [Fact]
    public void Shell_AnnouncesEveryLanguageAsAnAlternate()
    {
        string html = WorldHostPortalPages.Landing(Config);
        foreach (var locale in PortalLocales.Supported)
        {
            Assert.Contains($"<link rel='alternate' hreflang='{locale.Code()}' href='?lang={locale.Code()}'>", html);
        }

        Assert.Contains("hreflang='x-default'", html);
    }

    [Theory]
    [InlineData(null, "de")]
    [InlineData("", "de")]
    [InlineData("en", "en")]
    [InlineData("en-US,en;q=0.9", "en")]
    [InlineData("EN-us", "en")] // header tags are case-insensitive, unlike our own ?lang= values
    [InlineData("de-DE,de;q=0.9,en;q=0.8", "de")]
    [InlineData("fr-FR,fr;q=0.9,en;q=0.8", "fr")] // French is a portal language now (#970)
    [InlineData("zh-CN,zh;q=0.9,en;q=0.8", "zh")]
    [InlineData("sv-SE,sv;q=0.9,en;q=0.8", "en")] // first SUPPORTED tag wins, not just the first tag
    [InlineData("sv-SE,sv", "en")] // nothing supported → English, the game's fallback language
    [InlineData("*", "en")]
    [InlineData("eng-US", "en")] // only exact two-letter primary tags count
    public void LangFromAcceptHeader_PicksTheFirstSupportedLanguage(string? header, string expected)
        => Assert.Equal(expected, PortalLocales.LangFromAcceptHeader(header));

    [Fact]
    public void Privacy_NonGermanPutsTheSummaryFirst_GermanTextStaysAuthoritative()
    {
        // The German body is the legally authoritative text; a visitor who does not read German gets
        // the plain-language summary in their own language above it.
        foreach (string lang in new[] { "en", "fr", "ja" })
        {
            string html = WorldHostPortalPages.Privacy(Config, lang);
            Assert.True(html.IndexOf("class='card' lang='" + lang + "'", StringComparison.Ordinal)
                < html.IndexOf("Verantwortlicher", StringComparison.Ordinal),
                $"{lang}: the localized summary must come before the German body");
        }

        // German readers meet the authoritative text first; the English summary stays the appendix.
        string de = WorldHostPortalPages.Privacy(Config);
        Assert.True(de.IndexOf("Verantwortlicher", StringComparison.Ordinal)
            < de.IndexOf("class='card' lang='en'", StringComparison.Ordinal));
    }

    [Fact]
    public void Impressum_TellsNonGermanReaders_WhyTheNoticeIsGerman()
    {
        Assert.DoesNotContain("required by German law", WorldHostPortalPages.Impressum(Config));
        Assert.Contains("required by German law", WorldHostPortalPages.Impressum(Config, "en"));
        // The legal body itself never translates — it is the authoritative text.
        Assert.Contains("Angaben gemäß § 5 DDG", WorldHostPortalPages.Impressum(Config, "ja"));
    }

    // ---------------- Kid-friendly rework: feedback card + parental notice (#257) ----------------

    [Fact]
    public void Landing_CarriesTheParentalNotice_InBothLanguages()
    {
        Assert.Contains("Frag bitte zuerst deine Eltern", WorldHostPortalPages.Landing(Config));
        Assert.Contains("Please ask your parents first", WorldHostPortalPages.Landing(Config, "en"));
    }

    [Fact]
    public void Rules_OpenWithTheParentalNotice_AndPointToTheInGameReportPaths()
    {
        string de = WorldHostPortalPages.Rules(Config);
        Assert.Contains("Frag bitte zuerst deine Eltern", de);
        Assert.Contains("/report", de); // reporting is explained via the in-game paths first

        string en = WorldHostPortalPages.Rules(Config, "en");
        Assert.Contains("Please ask your parents first", en);
        Assert.Contains("/report", en);
    }

    [Fact]
    public void Worlds_OffersFeedback_AndPostsItAsTheFeedbackCategory()
    {
        string de = WorldHostPortalPages.Worlds(Config);
        Assert.Contains("Feedback & Ideen", de);
        Assert.Contains("category:'feedback'", de); // sendFeedback() rides the reports pipe

        string en = WorldHostPortalPages.Worlds(Config, "en");
        Assert.Contains("Feedback & ideas", en);
        Assert.Contains("category:'feedback'", en);
    }

    [Fact]
    public void Worlds_DemotesReportAndDeleteAccount_IntoCollapsedDetails()
    {
        string html = WorldHostPortalPages.Worlds(Config);

        // The report form and the account-deletion button live inside <details> now — reachable, but
        // no longer the page's centerpiece (kids first see feedback, not complaint machinery).
        int reportField = html.IndexOf("id='r-name'", StringComparison.Ordinal);
        int deleteButton = html.IndexOf("deleteAccount()", StringComparison.Ordinal);
        Assert.True(reportField >= 0 && deleteButton >= 0);
        Assert.True(html.LastIndexOf("<details>", reportField, StringComparison.Ordinal) >= 0,
            "the report form must sit inside a collapsed <details>");
        Assert.True(html.LastIndexOf("<details>", deleteButton, StringComparison.Ordinal)
            > html.LastIndexOf("</details>", reportField, StringComparison.Ordinal),
            "the delete-account button must sit inside its own collapsed <details>");
    }

    [Fact]
    public void Worlds_JoinPrompt_RemembersTheLastPlayerName()
    {
        string html = WorldHostPortalPages.Worlds(Config);
        Assert.Contains("localStorage.getItem('bbs_player_name')", html); // prefill on the next join
        Assert.Contains("localStorage.setItem('bbs_player_name'", html);  // remembered on success
    }

    [Fact]
    public void Landing_PutsCreateAccountFirst()
    {
        // New (young) visitors should meet "create account" before "sign in".
        string html = WorldHostPortalPages.Landing(Config);
        Assert.True(html.IndexOf("id='su-name'", StringComparison.Ordinal)
            < html.IndexOf("id='li-name'", StringComparison.Ordinal));
    }

    // ---------------- Accessibility contract (#574) ----------------
    // A community accessibility review of the public portal found four semantic gaps. These tests pin the
    // fixes so a later edit cannot quietly drop them again — the markup is hand-written in C# strings,
    // where nothing else would catch a lost label or a form turning back into a div.

    [Theory]
    [InlineData("su-name")]
    [InlineData("su-pass")]
    [InlineData("li-name")]
    [InlineData("li-pass")]
    [InlineData("rc-name")]
    [InlineData("rc-code")]
    [InlineData("rc-pass")]
    public void Landing_EveryAccountField_HasAVisibleLabel(string field)
    {
        foreach (string lang in new[] { "de", "en" })
        {
            string html = WorldHostPortalPages.Landing(Config, lang);
            Assert.Contains($"for='{field}'", html, StringComparison.Ordinal);
            Assert.Contains($"id='{field}'", html, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("player-name")]
    [InlineData("w-name")]
    [InlineData("w-pass")]
    [InlineData("w-pass2")]
    [InlineData("f-msg")]
    [InlineData("r-name")]
    [InlineData("r-cat")]
    [InlineData("r-world")]
    [InlineData("r-msg")]
    public void Worlds_EveryField_HasAVisibleLabel(string field)
    {
        foreach (string lang in new[] { "de", "en" })
        {
            string html = WorldHostPortalPages.Worlds(Config, lang);
            Assert.Contains($"for='{field}'", html, StringComparison.Ordinal);
            Assert.Contains($"id='{field}'", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Landing_PasswordFields_CarryTheirAutocompleteRole()
    {
        string html = WorldHostPortalPages.Landing(Config);

        // Sign-in offers the SAVED password, account creation and recovery ask for a NEW one — the wrong
        // hint here makes a password manager fight the player.
        Assert.Contains("id='li-pass' name='password' type='password' autocomplete='current-password'", html);
        Assert.Contains("id='su-pass' name='new-password' type='password' autocomplete='new-password'", html);
        Assert.Contains("id='rc-pass' name='new-password' type='password' autocomplete='new-password'", html);
    }

    [Fact]
    public void BothPortalPages_AnnounceTheStatusLine()
    {
        // Every result of signup/login/recovery/world actions lands in #msg. Without live-region
        // semantics a screen reader never learns the page said anything.
        foreach (string html in new[] { WorldHostPortalPages.Landing(Config), WorldHostPortalPages.Worlds(Config) })
        {
            Assert.Contains("<div id='msg' role='status' aria-live='polite' aria-atomic='true'></div>", html);
        }
    }

    [Fact]
    public void Landing_SubmitsThroughRealForms_WithRecoveryAsASiblingNotANestedForm()
    {
        string html = WorldHostPortalPages.Landing(Config);

        Assert.Contains("<form class='card' id='su-form' novalidate>", html);
        Assert.Contains("<form id='li-form' novalidate>", html);
        Assert.Contains("wire('su-form', signup)", html);
        Assert.Contains("wire('li-form', login)", html);
        Assert.Contains("wire('li-recover', recover)", html);
        Assert.Contains("wire('li-terms', reaccept)", html);

        // HTML forbids nested forms, and inside the sign-in form the recovery button would submit the
        // LOGIN instead: the recovery/re-accept panels must close after the sign-in form has closed.
        int loginForm = html.IndexOf("<form id='li-form'", StringComparison.Ordinal);
        int loginFormEnd = html.IndexOf("</form>", loginForm, StringComparison.Ordinal);
        Assert.True(loginFormEnd < html.IndexOf("<form id='li-recover'", StringComparison.Ordinal),
            "the recovery form must be a sibling of the sign-in form, never nested inside it");
        Assert.True(loginFormEnd < html.IndexOf("<form id='li-terms'", StringComparison.Ordinal),
            "the re-accept form must be a sibling of the sign-in form, never nested inside it");
    }

    [Fact]
    public void Worlds_SubmitsThroughRealForms()
    {
        string html = WorldHostPortalPages.Worlds(Config);

        Assert.Contains("<form class='card' id='w-form' novalidate>", html);
        Assert.Contains("wire('w-form', createWorld)", html);
        Assert.Contains("wire('f-form', sendFeedback)", html);
        Assert.Contains("wire('r-form', report)", html);
    }

    [Fact]
    public void Landing_PasswordResetIsADisclosure_NotALinkToNowhere()
    {
        string html = WorldHostPortalPages.Landing(Config);

        // A <a href='#'> that only runs script is announced as a link that goes nowhere and cannot be
        // triggered with Space; the trigger also has to carry the panel's open/closed state.
        Assert.Contains("aria-expanded='false' aria-controls='li-recover'", html);
        Assert.Contains("<button type='button' class='linky' id='li-recover-toggle'", html);
        Assert.DoesNotContain("<a href='#'", html);
    }

    [Fact]
    public void Worlds_SignOutIsAButton_NotALinkToNowhere()
        => Assert.DoesNotContain("<a href='#'", WorldHostPortalPages.Worlds(Config));

    [Fact]
    public void PortalShell_KeepsAVisibleFocusIndicator()
    {
        // Keyboard-only players lose their place entirely without this — and the portal sets no other
        // outline, so removing the rule silently falls back to whatever the browser draws on dark blue.
        string html = WorldHostPortalPages.Landing(Config);
        Assert.Contains(":focus-visible{outline:3px solid var(--cyan)", html);
    }

    [Fact]
    public void AdminPage_SeparatesFeedbackFromPlayerReports()
    {
        var reports = new List<ReportRecord>
        {
            new(1, "aabbccddee11", "acct1", "Meanie", "chat", "insults", "open", 0),
            new(2, "", "acct2", "", "feedback", "please add space whales!", "open", 0),
        };

        string html = WorldHostAdminPages.Index(
            Config, Array.Empty<AdminWorldRow>(), reports, Array.Empty<AccountRecord>(), null, null);
        Assert.Contains("Feedback &amp; ideas", html);
        Assert.Contains("please add space whales!", html);
        Assert.Contains("1 open report(s)", html);
        Assert.Contains("1 open feedback", html);

        // The feedback row must not sit in the player-report table (no reported-name lookup link).
        int reportsCard = html.IndexOf("Open player reports", StringComparison.Ordinal);
        int feedbackCard = html.IndexOf("Feedback &amp; ideas", StringComparison.Ordinal);
        int feedbackRow = html.IndexOf("please add space whales!", StringComparison.Ordinal);
        Assert.True(reportsCard < feedbackCard && feedbackCard < feedbackRow);
    }

    // ---------------- Fleet admin: world deletion ----------------

    private static AdminWorldRow AdminRow(string name, string status = WorldStatus.Stopped, string channel = WorldChannel.Portal)
        => new(new WorldRecord("aabbccddee11", "acct1", name, "secret", 32000, status, "", 0, 0, "", false, channel), "Owner", null);

    [Fact]
    public void AdminPage_DeleteNeedsTheWorldNameTyped_AndOffersAPurgeVariant()
    {
        string html = WorldHostAdminPages.Index(
            Config, new[] { AdminRow("Justus' Welt") }, Array.Empty<ReportRecord>(),
            Array.Empty<AccountRecord>(), null, null);

        Assert.Contains("action='/admin/worlds/aabbccddee11/delete'", html);
        Assert.Contains("name='confirm'", html);
        Assert.Contains("name='purge' value='true'", html);

        // The world name reaches the placeholder HTML-encoded (apostrophes would break the attribute).
        Assert.Contains("placeholder='type: Justus&#39; Welt'", html);

        // Folded away so it cannot be hit while aiming for stop/wake.
        int deleteForm = html.IndexOf("/delete'", StringComparison.Ordinal);
        Assert.True(html.LastIndexOf("<details>", deleteForm, StringComparison.Ordinal)
            > html.LastIndexOf("</details>", deleteForm, StringComparison.Ordinal),
            "the delete form must sit inside its own collapsed <details>");
    }

    // ---------------- Fleet admin: stop vs. emergency kill (issue #519) ----------------

    [Fact]
    public void AdminPage_OffersAHardKill_OnlyWhileAnInstanceIsUp_AndConfirmsFirst()
    {
        string running = WorldHostAdminPages.Index(
            Config, new[] { AdminRow("Justus' Welt", WorldStatus.Running) }, Array.Empty<ReportRecord>(),
            Array.Empty<AccountRecord>(), null, null);

        Assert.Contains("action='/admin/worlds/aabbccddee11/kill'", running, StringComparison.Ordinal);
        Assert.Contains("return confirm(", running, StringComparison.Ordinal);

        // The confirm text carries the world ID, never the display name: an apostrophe in a player-chosen
        // name would break the JS string and silently skip the confirmation.
        Assert.Contains("Hard kill world aabbccddee11?", running, StringComparison.Ordinal);
        Assert.DoesNotContain("Hard kill world Justus", running, StringComparison.Ordinal);

        // Nothing to kill on a sleeping world — that cell only offers "wake".
        string stopped = WorldHostAdminPages.Index(
            Config, new[] { AdminRow("Justus' Welt") }, Array.Empty<ReportRecord>(),
            Array.Empty<AccountRecord>(), null, null);
        Assert.DoesNotContain("/kill'", stopped, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminPage_LabelsArcadeWorldDeletionAsAReset()
    {
        string glitch = WorldHostAdminPages.Index(
            Config, new[] { AdminRow("Glitch Arcade 1", channel: WorldChannel.Glitch) },
            Array.Empty<ReportRecord>(), Array.Empty<AccountRecord>(), null, null);
        Assert.Contains(">reset…</summary>", glitch);
        Assert.Contains("arcade pool refills itself", glitch);

        string portal = WorldHostAdminPages.Index(
            Config, new[] { AdminRow("Justus Welt") }, Array.Empty<ReportRecord>(),
            Array.Empty<AccountRecord>(), null, null);
        Assert.Contains(">delete…</summary>", portal);
        Assert.DoesNotContain("arcade pool refills itself", portal);
    }

    [Theory]
    [InlineData("confirm", "did not match")]
    [InlineData("deleted", "still on disk")]
    [InlineData("purged", "saves erased")]
    [InlineData("stopping", "draining and saving")]
    [InlineData("killed", "no drain, no save")]
    public void AdminPage_ReportsTheOutcomeOfTheLastAction(string notice, string expected)
    {
        string html = WorldHostAdminPages.Index(
            Config, Array.Empty<AdminWorldRow>(), Array.Empty<ReportRecord>(),
            Array.Empty<AccountRecord>(), null, null, null, null, notice);
        Assert.Contains(expected, html);
    }

    // ---------------- Link out to the game website ----------------

    [Fact]
    public void Portal_LinksToTheGameWebsite_PerLanguage()
    {
        string de = WorldHostPortalPages.Landing(Config);
        Assert.Contains("https://www.blocksbeyondthestars.com/'", de); // footer + landing line
        Assert.Contains("Spiel-Website", de);
        Assert.Contains("Alles über das Spiel", de);
        Assert.DoesNotContain("blocksbeyondthestars.com/en", de);

        string en = WorldHostPortalPages.Landing(Config, "en");
        Assert.Contains("https://www.blocksbeyondthestars.com/en'", en);
        Assert.Contains("Game website", en);
        Assert.Contains("Everything about the game", en);

        // New tab, and never a referrer/opener handle into the portal session.
        Assert.Contains("rel='noopener noreferrer'", de);

        // The shared shell carries it, so every portal page has it — not just the landing page.
        Assert.Contains("Spiel-Website", WorldHostPortalPages.Worlds(Config));
        Assert.Contains("Game website", WorldHostPortalPages.Rules(Config, "en"));
    }

    [Fact]
    public void Portal_OmitsTheWebsiteLink_WhenTheOperatorClearedIt()
    {
        var noSite = new WorldHostConfig { WebsiteUrl = string.Empty, WebsiteUrlEn = string.Empty };
        foreach (string html in new[] { WorldHostPortalPages.Landing(noSite), WorldHostPortalPages.Landing(noSite, "en") })
        {
            Assert.DoesNotContain("blocksbeyondthestars.com", html);
            Assert.DoesNotContain("Spiel-Website", html);
            Assert.DoesNotContain("Game website", html);
        }
    }

    [Fact]
    public void Portal_WebsiteLink_UsesTheOperatorsOwnDomainAsLabel()
    {
        var own = new WorldHostConfig { WebsiteUrl = "https://www.example.org/spiel", WebsiteUrlEn = string.Empty };
        string de = WorldHostPortalPages.Landing(own);
        Assert.Contains("https://www.example.org/spiel", de);
        Assert.Contains(">example.org ↗<", de); // label without the "www."

        // No EN entry point configured — English falls back to the one URL rather than dropping the link.
        Assert.Contains("https://www.example.org/spiel", WorldHostPortalPages.Landing(own, "en"));
    }

    // ---------------- Play button: deep-link + grant rendering order (#252) ----------------

    [Fact]
    public void Worlds_PlayButton_DeepLinksIntoTheBrowserClient()
    {
        string html = WorldHostPortalPages.Worlds(Config);
        Assert.Contains("/play/?auto_join=1", html);
        Assert.Contains("hosted_token=", html);
        Assert.Contains("world_id=", html);
        Assert.Contains("server_host=", html);
        Assert.Contains("class='playnow'", html);
    }

    [Fact]
    public void Worlds_JoinFlow_RefreshesTheListBeforeRenderingTheGrant()
    {
        // Regression (#252): joinWorld once rendered the grant info and THEN called load(), which
        // rebuilds every card with an empty grant div — wiping the info milliseconds after it appeared.
        string html = WorldHostPortalPages.Worlds(Config);
        int refresh = html.IndexOf("await load();", StringComparison.Ordinal);
        int grant = html.IndexOf("document.getElementById(grantId)", StringComparison.Ordinal);
        Assert.True(refresh >= 0 && grant >= 0 && refresh < grant,
            "joinWorld() must await load() BEFORE rendering the grant block");
    }

    [Fact]
    public void Worlds_StatusMessages_RenderAboveTheFold()
    {
        // The #msg div (progress + errors) must come before the world list — at the old bottom-of-page
        // position, "Waking the world…" and join errors were invisible without scrolling.
        string html = WorldHostPortalPages.Worlds(Config);
        Assert.True(html.IndexOf("id='msg'", StringComparison.Ordinal)
            < html.IndexOf("id='list'", StringComparison.Ordinal));
    }

    // ---------------- Branding (#254) ----------------

    [Fact]
    public void Shell_ShowsTheGameLogo_AndTheWebsiteFavicon()
    {
        string html = WorldHostPortalPages.Landing(Config);
        Assert.Contains("href='/favicon.ico'", html);
        Assert.Contains("class='brand'", html);
        Assert.Contains("<b>Blocks</b> Beyond the Stars", html);
        Assert.Contains("<svg class='mark'", html);
    }

    [Fact]
    public void Favicon_IsAValidEmbeddedIco()
    {
        // .ico magic: reserved 0x0000, type 0x0001, then a nonzero image count.
        byte[] ico = PortalFavicon.Bytes;
        Assert.True(ico.Length > 1000);
        Assert.Equal(0, BitConverter.ToUInt16(ico, 0));
        Assert.Equal(1, BitConverter.ToUInt16(ico, 2));
        Assert.True(BitConverter.ToUInt16(ico, 4) >= 1);
    }

    // ---------------- /play WebGL serving policy ----------------

    [Fact]
    public void PlayPage_StampsAssetUrls_WithTheNewestBuildTimestamp()
    {
        string root = Path.Combine(Path.GetTempPath(), "bbts_play_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Build"));
            File.WriteAllText(Path.Combine(root, "index.html"), "<html>var buildStamp = \"\";</html>");
            File.WriteAllText(Path.Combine(root, "Build", "WebGL.wasm.br"), "x");

            string? html = PlayPage.StampIndexHtml(root);
            Assert.NotNull(html);
            Assert.Contains("var buildStamp = \"?v=", html);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PlayPage_WithoutABuild_ServesALocalizedFriendlyPage()
    {
        string root = Path.Combine(Path.GetTempPath(), "bbts_play_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Null(PlayPage.StampIndexHtml(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        Assert.Contains("noch nicht installiert", PlayPage.NotInstalledHtml("de"));
        Assert.Contains("not installed", PlayPage.NotInstalledHtml("en"));
        Assert.Contains("noch nicht installiert", PlayPage.NotInstalledHtml("whatever")); // German default
    }

    [Theory]
    [InlineData("WebGL.wasm.br", "br", "application/wasm")]
    [InlineData("WebGL.framework.js.br", "br", "application/javascript")]
    [InlineData("WebGL.data.br", "br", "application/octet-stream")]
    [InlineData("WebGL.data.gz", "gzip", null)]
    [InlineData("WebGL.wasm", null, null)]
    public void PlayPage_AnnouncesUnityPrecompressedEncodings(string file, string? encoding, string? contentType)
    {
        var (enc, type) = PlayPage.EncodingFor(file);
        Assert.Equal(encoding, enc);
        Assert.Equal(contentType, type);
    }

    [Fact]
    public void PlayPage_OnlyVersionStampedAssets_MayCacheLongTerm()
    {
        // Unity's build file names are stable, not content-addressed — blanket immutable caching once
        // mixed old/new wasm+data pairs across rebuilds and crashed the engine.
        Assert.Equal("public, max-age=31536000, immutable", PlayPage.CacheControlFor("WebGL.wasm.br", hasVersionQuery: true));
        Assert.Equal("no-cache", PlayPage.CacheControlFor("WebGL.wasm.br", hasVersionQuery: false));
        Assert.Equal("no-cache", PlayPage.CacheControlFor("index.html", hasVersionQuery: true));
    }
}
