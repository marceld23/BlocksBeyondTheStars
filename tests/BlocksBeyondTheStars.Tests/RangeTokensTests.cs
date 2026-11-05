// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3206/#3207: the player-facing texts name a range by a <c>{range_…}</c> token the locale loader fills from the constant
/// (<see cref="RangeTokens"/>), so changing a range is a one-line constant change — and the design-only radii are one
/// consistent 64 blocks.
/// </summary>
public sealed class RangeTokensTests
{
    private static readonly Regex AnyRangeToken = new(@"\{range_[A-Za-z0-9_]*\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    [Fact]
    public void EveryToken_IsFilledWithTheConstantItNames()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{range_base_pad}"] = Number(BasePadRules.ReachXZ),
            ["{range_base_discovery}"] = Number(BaseRules.DiscoveryRadius),
            ["{range_base_no_spawn}"] = Number(WorldConstants.BaseSpawnExclusionRadius),
            ["{range_base_zone}"] = Number(WorldConstants.BaseZoneRadius),
            ["{range_sentry}"] = Number(WorldConstants.SentryRange),
            ["{range_rail_autolink}"] = Number(RailRules.AutoLinkRange),
            ["{range_sensor_near}"] = Number(CrystalNetRules.ProximityRadii[0]),
            ["{range_sensor_mid}"] = Number(CrystalNetRules.ProximityRadii[1]),
            ["{range_sensor_far}"] = Number(CrystalNetRules.ProximityRadii[2]),
        };

        Assert.Equal(expected.Count, RangeTokens.All.Count);
        foreach (var token in RangeTokens.All)
        {
            Assert.Equal(expected[token.Key], token.Value);
            Assert.Equal("within " + token.Value + " blocks", RangeTokens.Fill("within " + token.Key + " blocks"));
        }

        Assert.Equal("Near (4)", RangeTokens.Fill("Near ({range_sensor_near})"));
        Assert.Equal("no token {name} here", RangeTokens.Fill("no token {name} here"));
        Assert.Equal(string.Empty, RangeTokens.Fill(string.Empty));
    }

    [Fact]
    public void TheLocaleLoader_FillsTheTokens_OncePerTable()
    {
        var table = ContentLoader.ParseLocaleTable(
            "{ \"a\": \"Place it within {range_base_pad} blocks.\", \"b\": \"{range_base_zone}/{range_base_zone}\", \"c\": \"plain {0}\" }");

        Assert.Equal("Place it within 64 blocks.", table["a"]);
        Assert.Equal("8/8", table["b"]);
        Assert.Equal("plain {0}", table["c"]);
    }

    /// <summary>A misspelt token would reach the player as raw braces: every <c>{range_…}</c> in every shipped table — the
    /// core locales, the Codex and every story pack, all languages — is one the loader knows.</summary>
    [Fact]
    public void EveryRangeToken_InTheShippedTexts_IsOneTheLoaderFills()
    {
        string data = TestPaths.DataDir();
        var files = Directory.GetFiles(Path.Combine(data, "locales"), "*.json")
            .Concat(Directory.GetFiles(Path.Combine(data, "wiki", "locales"), "*.json"))
            .Concat(Directory.GetDirectories(Path.Combine(data, "stories"))
                .Select(d => Path.Combine(d, "locales"))
                .Where(Directory.Exists)
                .SelectMany(d => Directory.GetFiles(d, "*.json")));
        var unknown = new List<string>();
        foreach (string file in files)
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? new Dictionary<string, string>();
            foreach (var (key, value) in raw)
            {
                if (value is not null && AnyRangeToken.IsMatch(RangeTokens.Fill(value)))
                {
                    unknown.Add($"{Path.GetFileName(Path.GetDirectoryName(file))}/{Path.GetFileName(file)}:{key}");
                }
            }
        }

        Assert.True(unknown.Count == 0, "Unknown {range_…} tokens (add them to RangeTokens or fix the spelling): " + string.Join(", ", unknown));
    }

    /// <summary>English and German name the same range tokens in every text (the community languages are held to the
    /// English set by <c>CommunityLocaleTests</c>).</summary>
    [Fact]
    public void EnglishAndGerman_NameTheSameRangeTokens()
    {
        var en = TestLocales.Load("en");
        var de = TestLocales.Load("de");
        var differ = new List<string>();
        foreach (var (key, english) in en)
        {
            if (!de.TryGetValue(key, out var german))
            {
                continue;
            }

            var want = AnyRangeToken.Matches(english).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
            var got = AnyRangeToken.Matches(german).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
            if (!want.SetEquals(got))
            {
                differ.Add(key);
            }
        }

        Assert.True(differ.Count == 0, "en/de range tokens differ: " + string.Join(", ", differ));
    }

    [Fact]
    public void TheGame_ShowsTheConstants_InItsTexts()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        foreach (var locale in new[] { GameLocale.English, GameLocale.German })
        {
            var loc = content.CreateLocalizer(locale);
            string reach = Number(BasePadRules.ReachXZ);
            Assert.Contains(" " + reach + " ", loc.Get("item.base_pad.desc"), StringComparison.Ordinal);
            Assert.Contains(" " + Number(WorldConstants.BaseSpawnExclusionRadius) + " ", loc.Get("vega.hint.base_walls"), StringComparison.Ordinal);
            Assert.Contains("(" + Number(CrystalNetRules.ProximityRadii[2]) + ")", loc.Get("ui.crystal.radius.2"), StringComparison.Ordinal);
            Assert.DoesNotContain("{range_", loc.Get("item.sentry_post.desc"), StringComparison.Ordinal);

            // A server message names the server's own rule as its argument (@srv.basepad.no_base:64 → {name}).
            Assert.Contains("{name}", loc.Get("srv.basepad.no_base"), StringComparison.Ordinal);
            Assert.Contains("{name}", loc.Get("srv.sentry.outside_base"), StringComparison.Ordinal);
        }
    }

    /// <summary>#3214: the base-walls hint no longer claims a fixed 48-block reach — the walled reach grows with the base's
    /// builds since #1862 (48 up to 192 blocks).</summary>
    [Fact]
    public void TheBaseWallsHint_NamesNoStaleReach()
    {
        foreach (string code in new[] { "en", "de" })
        {
            Assert.DoesNotContain("48", TestLocales.Load(code)["vega.hint.base_walls"], StringComparison.Ordinal);
        }
    }

    /// <summary>#3207: the design-only radii that were 48 are one consistent 64 — the guaranteed full-column streaming radius.</summary>
    [Fact]
    public void TheDesignRadii_AreOneConsistent64()
    {
        Assert.Equal(64, BasePadRules.ReachXZ);
        Assert.Equal(64, BaseRules.DiscoveryRadius);
        Assert.Equal(64f, RailRules.LinkerRange);
        Assert.Equal(64, NpcGridPath.Limits.Default.HalfXZ);
        Assert.Equal(64f, SvGameServer.CreatureMotionRange);
        Assert.Equal(64f, SvGameServer.CreatureReportRange);
    }
}
