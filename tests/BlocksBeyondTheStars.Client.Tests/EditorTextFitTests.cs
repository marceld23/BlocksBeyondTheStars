// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text;
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Shared.Content;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The editors' texts fit their boxes in all 14 languages (#3263) — without Unity, so PR CI runs it. Each entry of the
/// catalogue (<c>EditorTextFitTests.*.cs</c>) is one place a text is drawn: its box as the client code lays it out, its
/// font size and the rule it follows — <see cref="FitRule.Button"/> (<c>UiKit.AddFitButton</c>: one line ≥ 14 px, else
/// two lines without a broken word at ≥ 13 px), a plain one-line <see cref="FitRule.Label"/> that must not run past its
/// room, a <see cref="FitRule.Wrap"/> paragraph with a line budget, <c>UiKit.FitLabelSingleLine</c>
/// (<see cref="FitRule.FitLine"/>), a hint line (<see cref="FitRule.Lines"/>) and an input's placeholder. Widths come
/// from the bundled fonts' advances (<see cref="UiTextMeasure"/>); a community language's missing key reads English, as
/// in the game. Moving or resizing a control in an editor means updating its entry here.
/// </summary>
public sealed partial class EditorTextFitTests
{
    /// <summary>Plain labels may use their whole room but for the estimate's ±2 %.</summary>
    private const float LabelSlack = 0.98f;

    private static readonly Regex Placeholder = new(@"\{[^{}]*\}", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private static readonly Lazy<IReadOnlyDictionary<string, Dictionary<string, string>>> LocaleTables = new(LoadLocales);

    private readonly ITestOutputHelper _output;

    public EditorTextFitTests(ITestOutputHelper output) => _output = output;

    public enum FitRule
    {
        /// <summary><c>UiKit.AddFitButton</c> / <c>FitButtonLabel</c>: box = button − 18 px left (56 with an icon) − 10 px right.</summary>
        Button,

        /// <summary><c>UiKit.FitLabelLines</c>: <see cref="FitSite.Size"/> down to <see cref="FitSite.SingleMin"/> on one line,
        /// then two lines at ≥ <see cref="FitSite.MinSize"/> — a hint line.</summary>
        Lines,

        /// <summary>A plain overflow label: one line at <see cref="FitSite.Size"/> within <see cref="FitSite.W"/>.</summary>
        Label,

        /// <summary><c>UiKit.FitLabelSingleLine(label, W, MinSize, Size)</c>: fits at the floor size.</summary>
        FitLine,

        /// <summary>A wrapping text: at most <see cref="FitSite.MaxLines"/> lines of width W, no word broken.</summary>
        Wrap,

        /// <summary>An input's placeholder text: one line within W − 20 px of padding.</summary>
        Input,
    }

    /// <summary>One place a text is drawn. <see cref="Keys"/> are locale keys; a trailing or inner <c>*</c> matches every
    /// key of that shape in <c>en.json</c>.</summary>
    public sealed record FitSite(string Where, FitRule Rule, float W, float H, int Size, IReadOnlyList<string> Keys)
    {
        public string Prefix { get; init; } = string.Empty;

        public string Suffix { get; init; } = string.Empty;

        public float TextX { get; init; } = 18f;

        public int MinSize { get; init; } = ButtonLabelFit.TwoLineMin;

        public int SingleMin { get; init; } = ButtonLabelFit.SingleLineMin;

        public int MaxLines { get; init; } = 1;

        /// <summary>What a <c>{0}</c>-style placeholder stands for — the longest value the game puts there.</summary>
        public string Sample { get; init; } = "88";

        /// <summary>A size below 13 px is allowed only for decoration (a glyph, a number) — never for words.</summary>
        public bool Decorative { get; init; }
    }

    private static FitSite Button(string where, float w, float h, params string[] keys) => new(where, FitRule.Button, w, h, ButtonLabelFit.MaxSize, keys);

    private static FitSite IconButton(string where, float w, float h, params string[] keys) => Button(where, w, h, keys) with { TextX = 56f };

    private static FitSite Label(string where, float room, int size, params string[] keys) => new(where, FitRule.Label, room, 0f, size, keys);

    private static FitSite FitLine(string where, float width, int min, int max, params string[] keys)
        => new(where, FitRule.FitLine, width, 0f, max, keys) { MinSize = min };

    private static FitSite Wrap(string where, float w, int maxLines, int size, params string[] keys)
        => new(where, FitRule.Wrap, w, 0f, size, keys) { MaxLines = maxLines };

    private static FitSite Lines(string where, float w, float h, int max, int singleMin, int min, params string[] keys)
        => new(where, FitRule.Lines, w, h, max, keys) { SingleMin = singleMin, MinSize = min };

    private static FitSite Input(string where, float w, int size, params string[] keys) => new(where, FitRule.Input, w, 0f, size, keys);

    /// <summary>Every catalogued place, editor by editor.</summary>
    public static IEnumerable<FitSite> AllSites()
        => HubSites().Concat(TextureSites()).Concat(StructureSites()).Concat(StructureSourceSites()).Concat(KitPanelSites()).Concat(ShipSites())
            .Concat(AvatarSites()).Concat(PaintSites()).Concat(FormSites()).Concat(DeveloperToolSites())
            .Concat(SubmitSites()).Concat(OtherScreenSites()).Concat(HudFrameSites());

    [Fact]
    public void EveryEditorText_FitsItsBox_InEveryLanguage()
    {
        var failures = new List<string>();
        var perLocale = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int checks = 0;
        foreach (var site in AllSites())
        {
            foreach (string key in Expand(site.Keys))
            {
                foreach (var (locale, table) in LocaleTables.Value)
                {
                    checks++;
                    string text = Text(site, key, locale);
                    string? problem = Check(site, text);
                    perLocale[locale] = perLocale.GetValueOrDefault(locale) + (problem == null ? 0 : 1);
                    if (problem != null)
                    {
                        failures.Add($"{site.Where} | {key} | {locale} | \"{text}\" | {problem}");
                    }
                }
            }
        }

        _output.WriteLine($"{checks} checks; failures per locale: " + string.Join(", ", perLocale.Select(kv => $"{kv.Key} {kv.Value}")));
        Assert.True(failures.Count == 0,
            $"{failures.Count} editor text(s) do not fit (widen the box, give it a second line, or shorten the text):\n"
            + string.Join("\n", failures.Take(120)));
    }

    [Fact]
    public void EveryCataloguedKey_Exists()
    {
        var english = LocaleTables.Value["en"];
        var missing = AllSites().SelectMany(s => s.Keys.Select(k => (s.Where, Key: k)))
            .Where(x => !Matches(x.Key, english.Keys).Any())
            .Select(x => $"{x.Where}: {x.Key}")
            .ToList();
        Assert.True(missing.Count == 0, "catalogue keys missing from en.json (renamed or removed?):\n" + string.Join("\n", missing));
    }

    [Fact]
    public void NoEditorText_IsSmallerThan13Px_UnlessDecorative()
    {
        var small = AllSites()
            .Where(s => !s.Decorative && (s.MinSize < ButtonLabelFit.TwoLineMin
                                          || (s.Rule is FitRule.Label or FitRule.Wrap or FitRule.Input && s.Size < ButtonLabelFit.TwoLineMin)))
            .Select(s => s.Where)
            .ToList();
        Assert.True(small.Count == 0, "editor texts below 13 px (#3263):\n" + string.Join("\n", small));
    }

    /// <summary>The problem with <paramref name="text"/> at <paramref name="site"/>, or null when it fits.</summary>
    public static string? Check(FitSite site, string text)
    {
        switch (site.Rule)
        {
            case FitRule.Button:
            case FitRule.Lines:
                {
                    float w = site.Rule == FitRule.Button ? site.W - site.TextX - 10f : site.W;
                    var fit = ButtonLabelFit.Fit(text, w, site.H, UiTextMeasure.Width, site.Size, site.SingleMin, site.MinSize, Math.Max(2, site.MaxLines));
                    return fit.Fits ? null
                        : $"{UiTextMeasure.Width(text, site.SingleMin):0} px at {site.SingleMin} px in {w * ButtonLabelFit.WidthSlack:0}×{site.H:0}; "
                          + $"{fit.Lines} lines at {fit.Size} px";
                }

            case FitRule.Label:
                {
                    float width = UiTextMeasure.WidestLine(text, site.Size);
                    return width <= site.W * LabelSlack ? null : $"{width:0} px at {site.Size} px, room {site.W:0}";
                }

            case FitRule.FitLine:
                {
                    float width = UiTextMeasure.WidestLine(text, site.MinSize);
                    return width <= site.W * LabelSlack ? null : $"{width:0} px even at {site.MinSize} px, room {site.W:0}";
                }

            case FitRule.Wrap:
                {
                    int lines = ButtonLabelFit.CountLines(text, site.W * ButtonLabelFit.WidthSlack, site.Size, UiTextMeasure.Width, out bool broke);
                    return broke ? $"a word wider than {site.W:0} px at {site.Size} px"
                        : lines > site.MaxLines ? $"{lines} lines at {site.Size} px, room for {site.MaxLines}" : null;
                }

            case FitRule.Input:
                {
                    float width = UiTextMeasure.Width(text, site.Size);
                    return width <= (site.W - 20f) * LabelSlack ? null : $"{width:0} px at {site.Size} px, room {site.W - 20f:0}";
                }

            default:
                return "unknown rule";
        }
    }

    /// <summary>The text a site shows for <paramref name="key"/> in <paramref name="locale"/>: the locale's own, else
    /// English; placeholders filled with the site's sample, rich-text tags removed.</summary>
    public static string Text(FitSite site, string key, string locale)
    {
        var tables = LocaleTables.Value;
        string raw = tables[locale].TryGetValue(key, out var own) && !string.IsNullOrEmpty(own) ? own : tables["en"][key];
        string filled = Placeholder.Replace(raw, site.Sample);
        return site.Prefix + UiTextMeasure.StripTags(filled) + site.Suffix;
    }

    /// <summary>The en.json keys the patterns stand for.</summary>
    public static IEnumerable<string> Expand(IEnumerable<string> patterns)
    {
        var english = LocaleTables.Value["en"].Keys;
        return patterns.SelectMany(p => Matches(p, english)).Distinct(StringComparer.Ordinal);
    }

    private static IEnumerable<string> Matches(string pattern, IEnumerable<string> keys)
    {
        if (!pattern.Contains('*'))
        {
            return keys.Contains(pattern) ? new[] { pattern } : Array.Empty<string>();
        }

        var regex = new Regex("^" + Regex.Escape(pattern).Replace("\\*", "[a-z0-9_]+") + "$", RegexOptions.None, TimeSpan.FromSeconds(1));
        return keys.Where(k => regex.IsMatch(k)).OrderBy(k => k, StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, Dictionary<string, string>> LoadLocales()
    {
        var tables = new SortedDictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (string file in Directory.GetFiles(Path.Combine(ClientTestPaths.DataDir(), "locales"), "*.json"))
        {
            tables[Path.GetFileNameWithoutExtension(file)] = ContentLoader.ParseLocaleTable(File.ReadAllText(file, Encoding.UTF8));
        }

        Assert.Equal(14, tables.Count);
        return tables;
    }

    /// <summary>The English table (the catalogue's tests and the coverage guard read it).</summary>
    internal static IReadOnlyDictionary<string, string> English => LocaleTables.Value["en"];
}
