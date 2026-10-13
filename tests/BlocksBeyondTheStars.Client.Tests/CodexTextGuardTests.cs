// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Localization;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2508: guards over the Codex article texts — the per-language tables <c>data/wiki/locales/&lt;code&gt;.json</c>
/// (<c>wiki.&lt;id&gt;.title</c> / <c>wiki.&lt;id&gt;.body</c>) and the article list <c>data/wiki/articles.json</c>.
/// <list type="bullet">
/// <item>English and German name no key literally — "press E", "Tab menu", "(M)", "right-click", "hold Jump" are wrong after
/// a rebind, on a pad and on a tablet; a <c>{key:Action}</c> token names the player's own control (the patterns of
/// <see cref="VegaLineBudgetTests"/> plus keys named by their job or by the action's name);</item>
/// <item>every token names a real action, and that action has its bracket word (<c>ui.vega.act.&lt;Action&gt;</c>) in all
/// fourteen core tables;</item>
/// <item>the list and the English and German tables name exactly the same articles;</item>
/// <item>every table — the twelve community languages included — holds only articles English has, no blank text, the
/// English tokens and the English markup: the same paragraphs, list items and pictures in the same order, within each the
/// same inline tags (their order may follow the language's word order), properly nested.</item>
/// </list>
/// That every table is in the WebGL fallback list is <see cref="StoryUiFixesClientTests.TheBuiltInFallbackList_MatchesTheDataFolder"/>.
/// </summary>
public sealed class CodexTextGuardTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly string[] CoreLanguages = { "en", "de", "es", "fr", "it", "ja", "ko", "nl", "pl", "pt", "ru", "tr", "uk", "zh" };

    private static readonly Regex Placeholder = new(@"\{(?:key:)?[A-Za-z0-9_]+\}", RegexOptions.CultureInvariant, RegexTimeout);

    /// <summary>An article id: lower-case words joined by hyphens — it becomes part of the keys <c>wiki.&lt;id&gt;.title/body</c>.</summary>
    private static readonly Regex ArticleId = new(@"^[a-z0-9-]+$", RegexOptions.CultureInvariant, RegexTimeout);

    /// <summary>What <see cref="WikiMarkup"/> treats as a tag: "&lt;", an optional slash, a letter.</summary>
    private static readonly Regex MarkupTag = new(@"<\s*/?\s*[A-Za-z][^>]*>", RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex TagName = new(
        @"^<\s*(?<close>/)?\s*(?<name>[A-Za-z][A-Za-z0-9]*)", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, RegexTimeout);

    /// <summary>Tags that shape the page (paragraphs, lists, pictures): their sequence must match English exactly.</summary>
    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "ul", "ol", "li", "br", "img", "div", "h1", "h2", "h3", "h4", "h5", "h6",
    };

    private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase) { "br", "img" };

    /// <summary>A key named by its job or by the action's name: "hold the mine button", "die Abbau-Taste", "the action key",
    /// "(middle mouse)", "Q on the keyboard", "hold Jump", "press Feed", "halt Springen gedrückt", "(Benutzen)". "Hold the
    /// fire button" stays legal: in flight the fire control has no token of its own (a tablet labels it FIRE, the Mine
    /// pseudo-action reads MINE there), and the line under the scan target names the exact button.</summary>
    internal static readonly Regex LiteralKeyInCodex = new(
        @"(?i:\b(?:mine|action|use)\s+(?:button|key)\b|\b(?:abbau|aktions)-?taste\b|\bmiddle mouse\b|\bmittlere\w*\s+Maustaste\b"
        + @"|\bon the keyboard\b|\bauf der Tastatur\b)"
        + @"|\b(?i:hold|holding|press|use|let go of)\s+(?:Jump|Crouch|Feed|Interact)\b"
        + @"|\b(?i:halt|halte|hältst|drück|drücke|lässt du)\s+(?:Springen|Ducken|Füttern|Benutzen)\b|\((?:Benutzen|Füttern|Interact|Feed)\b",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, RegexTimeout);

    /// <summary>Text that looks like a key to the patterns but is none: the e-mail, "z. B.", degrees Celsius and the N that
    /// marks north on the compass (fast-travel).</summary>
    private static readonly string[] NotAKey = { "E-Mail", "z. B.", "°C", "the <b>N</b> that", "wandernde <b>N</b>" };

    private static string WikiDir => Path.Combine(ClientTestPaths.DataDir(), "wiki");

    private static Dictionary<string, string> WikiTable(string code)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(WikiDir, "locales", code + ".json")))
           ?? new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Every language with a Codex table on disk — English and German always, the community languages once
    /// translated.</summary>
    public static TheoryData<string> WikiTables()
    {
        var data = new TheoryData<string>();
        foreach (string file in Directory.GetFiles(Path.Combine(ClientTestPaths.DataDir(), "wiki", "locales"), "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            data.Add(Path.GetFileNameWithoutExtension(file));
        }

        return data;
    }

    [Fact]
    public void EveryCodexTable_IsALanguageTheGameHas()
    {
        var known = Enum.GetValues<GameLocale>().Select(l => l.Code()).ToHashSet(StringComparer.Ordinal);
        var codes = WikiTables().Select(row => (string)row[0]).ToList();

        Assert.Contains("en", codes);
        Assert.Contains("de", codes);
        Assert.All(codes, code => Assert.Contains(code, known));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void TheArticleList_AndTheTable_NameTheSameArticles(string code)
    {
        string listJson = File.ReadAllText(Path.Combine(WikiDir, "articles.json"));
        var ids = WikiArticleText.ParseArticleIds(listJson);
        using (var doc = JsonDocument.Parse(listJson))
        {
            Assert.Equal(doc.RootElement.GetArrayLength(), ids.Count); // no blank or repeated id
        }

        Assert.True(ids.Count >= 30, $"only {ids.Count} articles listed");
        Assert.All(ids, id => Assert.Matches(ArticleId, id));

        var table = WikiTable(code);
        var expected = ids.SelectMany(id => new[] { WikiArticleText.TitleKey(id), WikiArticleText.BodyKey(id) }).ToList();
        var missing = expected.Where(k => !table.TryGetValue(k, out var v) || string.IsNullOrWhiteSpace(v)).ToList();
        var extra = table.Keys.Except(expected, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0 && extra.Count == 0,
            $"{code}: data/wiki/locales/{code}.json and data/wiki/articles.json disagree — missing or blank: [{string.Join(", ", missing)}]; "
            + $"not in the list: [{string.Join(", ", extra)}]. English and German hold every listed article.");
    }

    [Theory]
    [MemberData(nameof(WikiTables))]
    public void EveryTable_KeepsTheEnglishArticles_TokensAndMarkup(string code)
    {
        var english = WikiTable("en");
        var bad = new List<string>();
        foreach (var (key, value) in WikiTable(code))
        {
            if (!english.TryGetValue(key, out var source))
            {
                bad.Add($"{key}: no such article in en.json");
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                bad.Add($"{key}: blank (leave the key out — the article then reads English)");
                continue;
            }

            var want = Placeholders(source);
            var got = Placeholders(value);
            if (!want.SetEquals(got))
            {
                bad.Add($"{key}: tokens {Format(got)} instead of the English {Format(want)}");
            }

            string rest = Placeholder.Replace(value, string.Empty);
            if (rest.Contains('{', StringComparison.Ordinal) || rest.Contains('}', StringComparison.Ordinal))
            {
                bad.Add($"{key}: a brace outside a {{key:Action}} token");
            }

            if (MarkupDifference(source, value) is { } difference)
            {
                bad.Add($"{key}: {difference}");
            }

            if (NestingProblem(value) is { } nesting)
            {
                bad.Add($"{key}: {nesting}");
            }
        }

        Assert.True(bad.Count == 0,
            $"{code}: Codex texts that do not match the English article — keep every tag (attributes included), the paragraph "
            + "and list structure and every {key:Action} token; translate only the words: " + string.Join(" | ", bad));
    }

    [Theory]
    [MemberData(nameof(WikiTables))]
    public void EveryArticle_RendersWithoutABraceLeft(string code)
    {
        foreach (var (key, value) in WikiTable(code))
        {
            string rendered = WikiMarkup.ToUnityRichText(value, text => LineText.ExpandKeyTokens(text, action => "[" + action + "]"));
            Assert.False(rendered.Contains('{', StringComparison.Ordinal) || rendered.Contains('}', StringComparison.Ordinal), $"{code}:{key} → {rendered}");
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void EnglishAndGerman_NameNoKeyLiterally(string code)
    {
        var bad = new List<string>();
        foreach (var (key, value) in WikiTable(code))
        {
            if (FirstLiteralKey(value) is { } hit)
            {
                bad.Add($"{key}: \"…{hit}…\"");
            }
        }

        Assert.True(
            bad.Count == 0,
            $"{code}: Codex texts that name a key literally — use a {{key:Action}} token (\"press {{key:Interact}}\", "
            + "\"hold {key:Jump}\", \"({key:PlanetMap})\"; WikiUI fills it with the player's key, pad button or touch button): "
            + string.Join(" | ", bad));
    }

    [Fact]
    public void CodexKeyPattern_CatchesTheOldTexts_ButNotTokensOrWords()
    {
        foreach (string literal in new[]
                 {
                     "Aim at a block and hold the mine button.", "halte die Abbau-Taste", "press the action key (the one you land with)",
                     "drück die Aktionstaste", "open the slot actions (middle mouse)", "(mittlere Maustaste)", "Q on the keyboard",
                     "Q auf der Tastatur", "and hold Jump", "Let go of Jump", "use Interact", "Press Feed", "halt Springen gedrückt",
                     "Lässt du Springen los", "benutze es (Benutzen)", "(Füttern, Q)",
                 })
        {
            Assert.Matches(LiteralKeyInCodex, literal);
        }

        foreach (string fine in new[]
                 {
                     "hold {key:Mine}", "press {key:Interact}", "Holding {key:Jump} at a wall", "Let go of {key:Jump}",
                     "hold the fire button until the ring is full", "übernimmt das Springen für dich", "a lower jump",
                     "Speed, Jump, Feather Fall", "press the button and the lamp lights", "before you press the button",
                     "halte {key:Mine} gedrückt", "drück {key:FeedCreature}",
                 })
        {
            Assert.DoesNotMatch(LiteralKeyInCodex, fine);
        }

        // The old article texts the shared patterns already catch, and the exempted look-alikes.
        Assert.NotNull(FirstLiteralKey("<b>Heal Tank / Bed</b>: press E to set your spawn point"));
        Assert.NotNull(FirstLiteralKey("<b>Crate</b>: G/H to stash and take."));
        Assert.NotNull(FirstLiteralKey("Most of the <b>Tab menu</b> works anywhere"));
        Assert.NotNull(FirstLiteralKey("your <b>planet map (M)</b> shows"));
        Assert.NotNull(FirstLiteralKey("glow through the rock (Rechtsklick)"));
        Assert.Null(FirstLiteralKey("deiner E-Mail, z. B. bei 40 °C — the <b>N</b> that moves around it"));
    }

    [Fact]
    public void EveryKeyToken_NamesARealAction_WithItsBracketWordInAllFourteenTables()
    {
        var actions = VegaLineBudgetTests.InputActionNames();
        var used = new SortedSet<string>(StringComparer.Ordinal);
        var bad = new List<string>();
        foreach (string code in new[] { "en", "de" })
        {
            foreach (var (key, text) in WikiTable(code))
            {
                foreach (Match m in VegaLineBudgetTests.KeyToken.Matches(text))
                {
                    string action = m.Groups["action"].Value;
                    used.Add(action);
                    if (!actions.Contains(action) && !VegaText.IsPseudoAction(action))
                    {
                        bad.Add($"{code}:{key} {{key:{action}}} names no InputAction");
                    }
                }
            }
        }

        Assert.NotEmpty(used);
        foreach (string code in CoreLanguages)
        {
            var core = VegaLineBudgetTests.Core(code);
            foreach (string action in used)
            {
                string wordKey = LineText.NeutralActionKeyPrefix + action;
                if (!core.TryGetValue(wordKey, out var word) || !(word.StartsWith('[') && word.EndsWith(']')))
                {
                    bad.Add($"{code}: {wordKey} missing or not a [bracket word]");
                }
            }
        }

        Assert.True(bad.Count == 0, "Codex tokens without a real action or its bracket word: " + string.Join(", ", bad));
    }

    [Fact]
    public void MarkupCheck_AllowsInlineTagsToMove_ButNotParagraphsOrAttributes()
    {
        const string source = "<p>Point a <span class=\"link\" data-link=\"cat:tech\">scanner</span> at the runes <i>where they stand</i>.</p><ul><li>A</li></ul>";

        // Word order may move the inline tags inside one paragraph (Japanese, German).
        Assert.Null(MarkupDifference(source, "<p><i>その場</i>のルーンに<span class=\"link\" data-link=\"cat:tech\">スキャナー</span>を向ける。</p><ul><li>A</li></ul>"));

        Assert.NotNull(MarkupDifference(source, "<p>x <span class=\"link\" data-link=\"cat:items\">y</span> <i>z</i></p><ul><li>A</li></ul>")); // attribute
        Assert.NotNull(MarkupDifference(source, "<p>x <span class=\"link\" data-link=\"cat:tech\">y</span> z</p><ul><li>A</li></ul>")); // lost <i>
        Assert.NotNull(MarkupDifference(source, "<p>x <span class=\"link\" data-link=\"cat:tech\">y</span></p><p><i>z</i></p><ul><li>A</li></ul>")); // a new paragraph
        Assert.NotNull(MarkupDifference(source, "<p><i>z</i></p><ul><li>A <span class=\"link\" data-link=\"cat:tech\">y</span></li></ul>")); // tag moved to another block
        Assert.NotNull(NestingProblem("<p><b>x <i>y</b></i></p>"));
        Assert.NotNull(NestingProblem("<p><b>x</p>"));
        Assert.Null(NestingProblem("<p><b>x</b> <img src=\"wiki/img/a.png\"> <i>y</i></p>"));
    }

    /// <summary>The first literal key name in a text (a short excerpt around it), or null: the shared patterns of
    /// <see cref="VegaLineBudgetTests"/> and <see cref="LiteralKeyInCodex"/>, over the raw text and over the text without its
    /// tags ("press <b>E</b>" → "press E"), after removing the <see cref="NotAKey"/> look-alikes.</summary>
    private static string? FirstLiteralKey(string value)
    {
        string text = value;
        foreach (string fine in NotAKey)
        {
            text = text.Replace(fine, " ", StringComparison.Ordinal);
        }

        foreach (string variant in new[] { text, MarkupTag.Replace(text, string.Empty) })
        {
            foreach (var pattern in new[]
                     {
                         VegaLineBudgetTests.LiteralKey, VegaLineBudgetTests.LiteralKeyWide, VegaLineBudgetTests.LiteralKeyInDescription,
                         VegaLineBudgetTests.LiteralPadControl, LiteralKeyInCodex,
                     })
            {
                var m = pattern.Match(variant);
                if (m.Success)
                {
                    int start = Math.Max(0, m.Index - 30);
                    return variant.Substring(start, Math.Min(variant.Length, m.Index + m.Length + 30) - start);
                }
            }
        }

        return null;
    }

    /// <summary>Why <paramref name="value"/>'s markup differs from <paramref name="source"/>'s, or null: the block tags
    /// (<see cref="BlockTags"/>) in the same order, and between two of them the same inline tags in any order.</summary>
    private static string? MarkupDifference(string source, string value)
    {
        var (sourceBlocks, sourceInline) = Skeleton(source);
        var (blocks, inline) = Skeleton(value);
        if (!sourceBlocks.SequenceEqual(blocks, StringComparer.Ordinal))
        {
            return $"paragraphs/lists/pictures differ: [{string.Join("", blocks)}] instead of [{string.Join("", sourceBlocks)}]";
        }

        for (int i = 0; i < sourceInline.Count; i++)
        {
            if (!string.Equals(sourceInline[i], inline[i], StringComparison.Ordinal))
            {
                return $"inline tags of block {i + 1} differ: [{inline[i]}] instead of [{sourceInline[i]}]";
            }
        }

        return null;
    }

    /// <summary>The block tags in order, and for each stretch before, between and after them its inline tags, sorted.</summary>
    private static (List<string> Blocks, List<string> Inline) Skeleton(string text)
    {
        var blocks = new List<string>();
        var inline = new List<string>();
        var current = new List<string>();
        foreach (Match m in MarkupTag.Matches(text))
        {
            if (BlockTags.Contains(TagName.Match(m.Value).Groups["name"].Value))
            {
                blocks.Add(m.Value);
                inline.Add(Sorted(current));
                current.Clear();
            }
            else
            {
                current.Add(m.Value);
            }
        }

        inline.Add(Sorted(current));
        return (blocks, inline);

        static string Sorted(List<string> tags) => string.Join(" ", tags.OrderBy(t => t, StringComparer.Ordinal));
    }

    /// <summary>Null when every tag closes in order (void tags — picture, line break — need no close), otherwise the first
    /// problem. Mis-nested tags show up as raw text in uGUI rich text.</summary>
    private static string? NestingProblem(string text)
    {
        var open = new Stack<string>();
        foreach (Match m in MarkupTag.Matches(text))
        {
            var tag = TagName.Match(m.Value);
            string name = tag.Groups["name"].Value.ToLowerInvariant();
            if (VoidTags.Contains(name) || m.Value.EndsWith("/>", StringComparison.Ordinal))
            {
                continue;
            }

            if (!tag.Groups["close"].Success)
            {
                open.Push(name);
            }
            else if (open.Count == 0 || !string.Equals(open.Pop(), name, StringComparison.Ordinal))
            {
                return $"'{m.Value}' closes a tag that is not the innermost open one";
            }
        }

        return open.Count == 0 ? null : $"'<{open.Peek()}>' is never closed";
    }

    private static HashSet<string> Placeholders(string text)
        => Placeholder.Matches(text).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);

    private static string Format(IEnumerable<string> set) => "{" + string.Join(", ", set.OrderBy(s => s, StringComparer.Ordinal)) + "}";
}
