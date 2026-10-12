// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2483 — step 22 of the story packs: the generated vocabulary tables of the authoring guide
/// (<c>docs/developer/STORY_VOCABULARY.md</c>, linked from <c>docs/developer/STORY_PACK_AUTHORING.md</c>). The document is
/// rendered from the code constants a pack may name — the game events, the condition atoms, the world predicates, the
/// camera types, anchors and caption styles, … — with each one's meaning taken from its XML doc comment in the source. The
/// test fails when the committed file drifts from the code; <c>BBTS_UPDATE_STORY_VOCABULARY=1</c> rewrites it.
/// </summary>
public sealed class StoryVocabularyDocTests
{
    [Fact]
    public void TheVocabularyDoc_MatchesTheCode()
    {
        string root = TestPaths.RepoRoot();
        string path = Path.Combine(root, StoryVocabularyDoc.RelativePath);
        string expected = StoryVocabularyDoc.Render(root);
        if (Environment.GetEnvironmentVariable(StoryVocabularyDoc.UpdateVariable) == "1")
        {
            File.WriteAllText(path, expected, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return;
        }

        string actual = File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal) : string.Empty;
        if (actual == expected)
        {
            return;
        }

        var want = expected.Split('\n');
        var have = actual.Split('\n');
        int line = Enumerable.Range(0, Math.Max(want.Length, have.Length))
            .First(i => i >= want.Length || i >= have.Length || want[i] != have[i]);
        Assert.Fail($"{StoryVocabularyDoc.RelativePath} is out of date with the code (first difference at line {line + 1}:\n"
                    + $"  expected: {(line < want.Length ? want[line] : "<end of file>")}\n"
                    + $"  found:    {(line < have.Length ? have[line] : "<end of file>")}).\n"
                    + $"Regenerate it: {StoryVocabularyDoc.UpdateCommand}");
    }

    [Fact]
    public void EveryVocabularyWord_HasAMeaningInItsDocComment()
    {
        // A new event, predicate, camera … without a <summary> would leave an empty row in the authors' reference.
        var missing = StoryVocabularyDoc.Undocumented(TestPaths.RepoRoot());
        Assert.True(missing.Count == 0, "Vocabulary constants without a <summary> doc comment: " + string.Join(", ", missing));
    }

    [Fact]
    public void TheDocument_ListsEveryWordAPackMayName()
    {
        string doc = StoryVocabularyDoc.Render(TestPaths.RepoRoot());
        foreach (string word in GameEventKeys.All
                     .Concat(GameEventKeys.UiEvents.Select(id => GameEventKeys.UiPrefix + id))
                     .Concat(StoryWorldPredicates.All.Select(p => "world:" + p))
                     .Concat(CutsceneCameras.All).Concat(CutsceneAnchors.All).Concat(CutsceneCaptionStyles.All)
                     .Concat(CutsceneGrades.All).Concat(StoryStepKinds.All).Concat(StoryStepTiers.All)
                     .Concat(StoryLeadTypes.All).Concat(StoryFinaleStages.All).Concat(StoryRegistry.ReservedPackIds))
        {
            Assert.Contains("`" + word + "`", doc, StringComparison.Ordinal);
        }

        Assert.Contains("`step:<pack>:<step>`", doc, StringComparison.Ordinal);
        Assert.Contains("`pack:<pack>:optedout`", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void DocComments_BecomeMarkdown()
    {
        var docs = new StoryVocabularyDoc.SourceDocs(TestPaths.RepoRoot());
        // <see cref> to a constant reads as its value; <c> as code; the issue tag goes; a table cell's pipe is escaped.
        string moveJump = docs.Summary(nameof(GameEventKeys), nameof(GameEventKeys.MoveJump));
        Assert.Contains("`jump`", moveJump, StringComparison.Ordinal);
        Assert.Contains("`crouch`", moveJump, StringComparison.Ordinal);
        Assert.StartsWith("The world has animals", docs.Summary(nameof(StoryWorldPredicates), nameof(StoryWorldPredicates.Creatures)), StringComparison.Ordinal);
        Assert.Contains("`<systemA>\\|<systemB>`", StoryVocabularyDoc.Cell(docs.Summary(nameof(GameEventKeys), nameof(GameEventKeys.LaneLinked))), StringComparison.Ordinal);
    }
}

/// <summary>
/// Renders <c>docs/developer/STORY_VOCABULARY.md</c> (#2483) from the code: the lists come from the constants
/// (<see cref="GameEventKeys"/>, <see cref="StoryWorldPredicates"/>, <see cref="CutsceneCameras"/> …), the meanings from
/// the constants' <c>&lt;summary&gt;</c> doc comments, read from the source files.
/// </summary>
internal static class StoryVocabularyDoc
{
    internal const string RelativePath = "docs/developer/STORY_VOCABULARY.md";

    internal const string UpdateVariable = "BBTS_UPDATE_STORY_VOCABULARY";

    internal const string UpdateCommand = UpdateVariable + "=1 dotnet test tests/BlocksBeyondTheStars.Tests -c Release "
                                          + "--filter FullyQualifiedName~StoryVocabularyDocTests";

    /// <summary>The source files whose doc comments describe the vocabularies (repository-relative).</summary>
    private static readonly string[] SourceFiles =
    {
        "src/BlocksBeyondTheStars.Shared/Story/GameEventKeys.cs",
        "src/BlocksBeyondTheStars.Shared/Story/StoryCondition.cs",
        "src/BlocksBeyondTheStars.Shared/Story/CutsceneDefinition.cs",
        "src/BlocksBeyondTheStars.Shared/Story/StoryPackManifest.cs",
        "src/BlocksBeyondTheStars.Shared/Story/StoryLeads.cs",
        "src/BlocksBeyondTheStars.Shared/Story/StoryRegistry.cs",
        "src/BlocksBeyondTheStars.Shared/Story/StoryChapters.cs",
        "src/BlocksBeyondTheStars.Shared/Missions/Mission.cs",
    };

    /// <summary>The types whose constants a <c>&lt;see cref&gt;</c> may name (rendered as the constant's value).</summary>
    private static readonly Type[] ConstantTypes =
    {
        typeof(GameEventKeys), typeof(CircuitEvents), typeof(StoryWorldPredicates), typeof(CutsceneCameras),
        typeof(CutsceneAnchors), typeof(CutsceneCaptionStyles), typeof(CutsceneGrades), typeof(CutsceneArgSources),
        typeof(CutsceneTiers), typeof(StoryRevealProviders), typeof(StoryStepKinds), typeof(StoryStepTiers),
        typeof(StoryObjectiveTypes), typeof(StoryGateNames), typeof(StoryLeadTypes), typeof(StoryFinaleStages),
        typeof(StoryChapterEvents), typeof(StoryMemoryTriggers), typeof(StoryRegistry), typeof(StoryNarrator),
        typeof(StoryEscalation), typeof(StoryGraduation),
    };

    /// <summary>Manifest model types: a <c>&lt;see cref&gt;</c> to one of their properties names a JSON field (camelCase).</summary>
    private static readonly HashSet<string> ModelTypes = new(StringComparer.Ordinal)
    {
        nameof(StoryDefinition), nameof(StoryChapter), nameof(StoryStep), nameof(StoryStepObjective), nameof(CutsceneDefinition),
        nameof(CutsceneShot), nameof(CutsceneCaption), nameof(CutsceneOptions), nameof(StoryFinaleBinding),
    };

    /// <summary>The doc comments of the vocabulary sources, by (type, member); member "" = the type's own summary.</summary>
    internal sealed class SourceDocs
    {
        private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

        private static readonly Regex TypeLine = new(
            @"^\s*(public|internal)\s+((static|sealed|partial|abstract)\s+)*(?<kind>class|enum|struct|interface|record)\s+(?<name>\w+)", Options, Timeout);
        private static readonly Regex MemberLine = new(@"^\s*public\s+(const|static\s+readonly)\s+[\w<>\[\],.?\s]+?\s+(?<name>\w+)\s*=", Options, Timeout);
        private static readonly Regex EnumMemberLine = new(@"^\s*(?<name>\w+)\s*(=\s*[^,]+)?,?\s*$", Options, Timeout);
        private static readonly Regex SummaryBlock = new(@"<summary>(?<text>.*?)</summary>", Options | RegexOptions.Singleline, Timeout);
        private static readonly Regex SeeCref = new(@"<see\s+cref=""(?<v>[^""]+)""\s*/>", Options, Timeout);
        private static readonly Regex SeeLangword = new(@"<see\s+langword=""(?<v>[^""]+)""\s*/>", Options, Timeout);
        private static readonly Regex ParamRef = new(@"<(type)?paramref\s+name=""(?<v>[^""]+)""\s*/>", Options, Timeout);
        private static readonly Regex CodeTag = new(@"<c>(?<v>.*?)</c>", Options | RegexOptions.Singleline, Timeout);
        private static readonly Regex ParaTag = new(@"</?para\s*/?>", Options, Timeout);
        private static readonly Regex AnyTag = new(@"<[^>]+>", Options, Timeout);
        private static readonly Regex Spaces = new(@"\s+", Options, Timeout);
        private static readonly Regex IssueTag = new(@"^#\d+(\s*\([^)]*\))?:\s*", Options, Timeout);
        private static readonly Regex CodeSpan = new("`[^`]*`", Options, Timeout);

        private readonly Dictionary<(string Type, string Member), string> _raw = new();

        public SourceDocs(string repoRoot)
        {
            foreach (string file in SourceFiles)
            {
                Parse(File.ReadAllLines(Path.Combine(repoRoot, file)));
            }
        }

        private void Parse(string[] lines)
        {
            string? type = null;
            bool inEnum = false;
            var doc = new List<string>();
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("///", StringComparison.Ordinal))
                {
                    doc.Add(trimmed.Substring(3).Trim());
                    continue;
                }

                if (trimmed.StartsWith("[", StringComparison.Ordinal) && doc.Count > 0)
                {
                    continue; // an attribute between the doc comment and its member
                }

                var typeMatch = TypeLine.Match(line);
                if (typeMatch.Success)
                {
                    type = typeMatch.Groups["name"].Value;
                    inEnum = typeMatch.Groups["kind"].Value == "enum";
                    Store(type, string.Empty, doc);
                }
                else if (type is not null && doc.Count > 0)
                {
                    var member = MemberLine.Match(line);
                    if (member.Success)
                    {
                        Store(type, member.Groups["name"].Value, doc);
                    }
                    else if (inEnum && EnumMemberLine.Match(line) is { Success: true } enumMember)
                    {
                        Store(type, enumMember.Groups["name"].Value, doc);
                    }
                }

                doc.Clear();
            }
        }

        private void Store(string type, string member, List<string> doc)
        {
            var match = SummaryBlock.Match(string.Join(" ", doc));
            if (match.Success)
            {
                _raw[(type, member)] = match.Groups["text"].Value;
            }
        }

        /// <summary>Whether the member has a non-empty summary.</summary>
        public bool Has(string type, string member)
            => _raw.TryGetValue((type, member), out string? raw) && raw.Trim().Length > 0;

        /// <summary>The member's summary as one line of Markdown ("" when it has none).</summary>
        public string Summary(string type, string member)
            => _raw.TryGetValue((type, member), out string? raw) ? ToMarkdown(raw, type) : string.Empty;

        /// <summary>XML doc → Markdown: <c>&lt;c&gt;</c> becomes code, a <c>&lt;see cref&gt;</c> to a constant its value
        /// (a manifest property its JSON field, anything else its name), a leading issue tag ("#2482: ") goes.</summary>
        private static string ToMarkdown(string xml, string currentType)
        {
            string text = SeeCref.Replace(xml, m => Code(ResolveCref(m.Groups["v"].Value, currentType)));
            text = SeeLangword.Replace(text, m => Code(m.Groups["v"].Value));
            text = ParamRef.Replace(text, m => Code(m.Groups["v"].Value));
            text = CodeTag.Replace(text, m => Code(m.Groups["v"].Value)); // still &lt;-encoded
            text = ParaTag.Replace(text, " ");
            text = AnyTag.Replace(text, string.Empty); // any other tag (list, item, …)
            text = Spaces.Replace(text, " ").Trim();
            text = IssueTag.Replace(text, string.Empty);
            // Entities are decoded inside code spans only: outside them Markdown renders "&lt;" as "<" (and a bare "<"
            // could start an HTML tag).
            text = CodeSpan.Replace(text, m => Decode(m.Value));
            return text.Length > 0 ? char.ToUpperInvariant(text[0]) + text.Substring(1) : text;
        }

        private static string Code(string value) => "`" + value + "`";

        private static string Decode(string value)
            => value.Replace("&lt;", "<", StringComparison.Ordinal).Replace("&gt;", ">", StringComparison.Ordinal)
                .Replace("&quot;", "\"", StringComparison.Ordinal).Replace("&amp;", "&", StringComparison.Ordinal);

        private static string ResolveCref(string cref, string currentType)
        {
            string[] parts = cref.Split('.');
            string member = parts[^1];
            string? owner = parts.Length > 1 ? parts[^2] : null;
            if (owner is not null && ModelTypes.Contains(owner))
            {
                return char.ToLowerInvariant(member[0]) + member.Substring(1); // a manifest field
            }

            var candidates = owner is not null
                ? ConstantTypes.Where(t => t.Name == owner)
                : ConstantTypes.Where(t => t.Name == currentType).Concat(ConstantTypes.Where(t => t.Name != currentType));
            foreach (var type in candidates)
            {
                if (type.GetField(member, BindingFlags.Public | BindingFlags.Static) is { IsLiteral: true } field
                    && field.GetRawConstantValue() is { } value)
                {
                    return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? member;
                }
            }

            return member;
        }
    }

    /// <summary>A Markdown table cell (a pipe would end the cell).</summary>
    internal static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal);

    /// <summary>The string constant of <paramref name="type"/> whose value is <paramref name="value"/>.</summary>
    private static string MemberOf(Type type, string value, Func<string, bool>? nameFilter = null)
    {
        var field = type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(f => f.IsLiteral && f.FieldType == typeof(string) && (string?)f.GetRawConstantValue() == value
                                 && (nameFilter is null || nameFilter(f.Name)));
        return field?.Name ?? throw new InvalidOperationException($"{type.Name} has no constant \"{value}\"");
    }

    /// <summary>Every (type, member) the document describes, for the "has a summary" guard.</summary>
    private static IEnumerable<(string Type, string Member)> DescribedMembers()
    {
        foreach (string key in GameEventKeys.All)
        {
            yield return key.StartsWith(GameEventKeys.CrystalPrefix, StringComparison.Ordinal)
                ? (nameof(CircuitEvents), MemberOf(typeof(CircuitEvents), key.Substring(GameEventKeys.CrystalPrefix.Length)))
                : (nameof(GameEventKeys), MemberOf(typeof(GameEventKeys), key));
        }

        foreach (string id in GameEventKeys.UiEvents)
        {
            yield return (nameof(GameEventKeys), MemberOf(typeof(GameEventKeys), GameEventKeys.UiPrefix + id));
        }

        foreach (string name in Enum.GetNames(typeof(StoryConditionKind)))
        {
            yield return (nameof(StoryConditionKind), name);
        }

        foreach (string name in Enum.GetNames(typeof(StoryPackState)))
        {
            yield return (nameof(StoryPackState), name);
        }

        foreach (string p in StoryWorldPredicates.All)
        {
            yield return (nameof(StoryWorldPredicates), MemberOf(typeof(StoryWorldPredicates), p));
        }

        foreach (var (type, values) in ListedVocabularies())
        {
            foreach (string v in values)
            {
                yield return (type.Name, MemberOf(type, v));
            }
        }

        foreach (string name in new[] { nameof(CutsceneTiers.Banner), nameof(CutsceneTiers.Card), nameof(CutsceneTiers.Scene), nameof(CutsceneTiers.SetPiece) })
        {
            yield return (nameof(CutsceneTiers), name);
        }
    }

    /// <summary>The string vocabularies rendered as plain "word | meaning" tables.</summary>
    private static IEnumerable<(Type Type, IReadOnlyList<string> Values)> ListedVocabularies()
    {
        yield return (typeof(StoryRegistry), new[] { StoryRegistry.KindStory, StoryRegistry.KindTutorial, StoryRegistry.KindSide });
        yield return (typeof(StoryRegistry), new[] { StoryRegistry.ScopeWorld, StoryRegistry.ScopePlayer });
        yield return (typeof(StoryStepKinds), StoryStepKinds.All);
        yield return (typeof(StoryStepTiers), StoryStepTiers.All);
        yield return (typeof(StoryObjectiveTypes), new[] { StoryObjectiveTypes.Mission, StoryObjectiveTypes.Lead });
        yield return (typeof(StoryRevealProviders), RevealProviders());
        yield return (typeof(StoryGateNames), StoryGateNames.All);
        yield return (typeof(CutsceneCameras), CutsceneCameras.All);
        yield return (typeof(CutsceneAnchors), CutsceneAnchors.All);
        yield return (typeof(CutsceneCaptionStyles), CutsceneCaptionStyles.All);
        yield return (typeof(CutsceneGrades), CutsceneGrades.All);
        yield return (typeof(CutsceneArgSources), new[] { CutsceneArgSources.Chapter, CutsceneArgSources.Body, CutsceneArgSources.System });
        yield return (typeof(StoryLeadTypes), StoryLeadTypes.All);
        yield return (typeof(StoryChapterEvents), StoryChapterEvents.All);
        yield return (typeof(StoryFinaleStages), StoryFinaleStages.All);
        yield return (typeof(StoryMemoryTriggers), StoryMemoryTriggers.All);
    }

    /// <summary>The marker providers (no list in code: every string constant of <see cref="StoryRevealProviders"/>).</summary>
    private static IReadOnlyList<string> RevealProviders()
        => typeof(StoryRevealProviders).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

    /// <summary>The members the document describes that have no summary in their source.</summary>
    internal static List<string> Undocumented(string repoRoot)
    {
        var docs = new SourceDocs(repoRoot);
        return DescribedMembers().Distinct().Where(m => !docs.Has(m.Type, m.Member)).Select(m => m.Type + "." + m.Member).ToList();
    }

    /// <summary>The whole document, LF line ends.</summary>
    internal static string Render(string repoRoot)
    {
        var docs = new SourceDocs(repoRoot);
        var md = new StringBuilder();
        void Line(string text = "") => md.Append(text).Append('\n');
        void Table(string left, string right, IEnumerable<(string Word, string Meaning)> rows)
        {
            Line($"| {left} | {right} |");
            Line("|---|---|");
            foreach (var (word, meaning) in rows)
            {
                Line($"| {Cell(word)} | {Cell(meaning)} |");
            }

            Line();
        }

        IEnumerable<(string, string)> Words(Type type, IEnumerable<string> values, Func<string, string>? show = null)
            => values.Select(v => ((show ?? (x => "`" + x + "`"))(v), docs.Summary(type.Name, MemberOf(type, v))));

        Line("# Story pack vocabulary (generated)");
        Line();
        Line("<!-- GENERATED from the code constants and their doc comments by StoryVocabularyDocTests (#2483) — do not edit by");
        Line("     hand. To change a meaning, edit the <summary> of the constant in src/BlocksBeyondTheStars.Shared/Story/ (or");
        Line("     Missions/Mission.cs for the Crystal Net events) and regenerate:");
        Line("     " + UpdateCommand + " -->");
        Line();
        Line("Every word a story pack may name in its data, with what it means — the reference tables of");
        Line("[STORY_PACK_AUTHORING.md](STORY_PACK_AUTHORING.md). The lists come straight from the code, so this page is never");
        Line("behind: a test (`StoryVocabularyDocTests`) fails when a constant is added, renamed or redescribed without");
        Line("regenerating it. Where each event is raised (file and method) is in");
        Line("[STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md#game-events-story-conditions-and-the-world-story-guard-2465).");
        Line();

        // ---- Packs ----
        Line("## Packs");
        Line();
        Line("### Kinds (`kind`)");
        Line();
        Table("Kind", "Meaning", Words(typeof(StoryRegistry), new[] { StoryRegistry.KindStory, StoryRegistry.KindTutorial, StoryRegistry.KindSide }));
        Line("### Scopes (`scope`)");
        Line();
        Table("Scope", "Meaning", Words(typeof(StoryRegistry), new[] { StoryRegistry.ScopeWorld, StoryRegistry.ScopePlayer }));
        Line("### Reserved pack ids");
        Line();
        Line("No pack may use these ids (`StoryRegistry.ReservedPackIds`): "
             + string.Join(", ", StoryRegistry.ReservedPackIds.Select(id => "`" + id + "`")) + ".");
        Line();
        Line("### Defaults");
        Line();
        Table("Field", "Built-in value", new[]
        {
            ("`narrator.id`", "`" + StoryNarrator.DefaultId + "`"),
            ("`escalate.hintAfter`", StoryEscalation.DefaultHintAfter + " s of play without progress"),
            ("`escalate.markerAfter`", StoryEscalation.DefaultMarkerAfter + " s"),
            ("`escalate.offerSkipAfter`", StoryEscalation.DefaultOfferSkipAfter + " s"),
            ("`graduation.suggest`", "at most " + StoryGraduation.MaxSuggestions + " chapters"),
        });

        // ---- Steps ----
        Line("## Steps");
        Line();
        Line("### Step kinds (`kind`)");
        Line();
        Table("Kind", "Meaning", Words(typeof(StoryStepKinds), StoryStepKinds.All));
        Line("### Step tiers (`tier`)");
        Line();
        Table("Tier", "Meaning", Words(typeof(StoryStepTiers), StoryStepTiers.All));
        Line("### Objective types (`objective.type`)");
        Line();
        Line("Without a `type` the objective is a game event (`objective.event`, below).");
        Line();
        Table("Type", "Meaning", Words(typeof(StoryObjectiveTypes), new[] { StoryObjectiveTypes.Mission, StoryObjectiveTypes.Lead }));
        Line("### Marker providers (`onStart.reveal`)");
        Line();
        Table("Provider", "Where the help marker points", Words(typeof(StoryRevealProviders), RevealProviders()));
        Line("### Gates (`gates`, tutorial packs only)");
        Line();
        Table("Gate", "What it opens", Words(typeof(StoryGateNames), StoryGateNames.All));

        // ---- Events ----
        Line("## Game events (`objective.event`)");
        Line();
        Line($"{GameEventKeys.All.Count} event keys (`GameEventKeys.All`). The key names the verb, the argument the thing: a step");
        Line("names a key (or a `prefix*` pattern) and, optionally, the arguments it accepts (`\"arg\": \"a|b\"`); the amount");
        Line("adds up towards `required`. Nothing is raised for an observer, before the join is complete, or for a non-positive");
        Line("amount.");
        Line();
        var events = GameEventKeys.All.OrderBy(k => k, StringComparer.Ordinal).Select(key =>
        {
            if (key.StartsWith(GameEventKeys.CrystalPrefix, StringComparison.Ordinal))
            {
                string circuit = key.Substring(GameEventKeys.CrystalPrefix.Length);
                return ("`" + key + "`", "Crystal Net (raised for the circuit's owner): "
                                         + docs.Summary(nameof(CircuitEvents), MemberOf(typeof(CircuitEvents), circuit)));
            }

            return ("`" + key + "`", docs.Summary(nameof(GameEventKeys), MemberOf(typeof(GameEventKeys), key)));
        });
        Table("Event", "Raised when (argument, amount)", events);
        Line("### UI teaching flags (`objective.event: \"ui:<id>\"`)");
        Line();
        Line("Reported by the client (tag 298 `UiTutorialEvent`): allowed only as a player pack's step objective — never in a");
        Line("condition, never paid, never stored.");
        Line();
        Table("Flag", "Raised when", GameEventKeys.UiEvents.Select(id =>
            ("`" + GameEventKeys.UiPrefix + id + "`",
             docs.Summary(nameof(GameEventKeys), MemberOf(typeof(GameEventKeys), GameEventKeys.UiPrefix + id, n => !n.EndsWith("Id", StringComparison.Ordinal))))));

        // ---- Conditions ----
        Line("## Conditions");
        Line();
        Line("One grammar for `requires`, `feasibleIf`, `alreadyDoneIf`, `visibleIf`, `offerWhen`, `autoCompleteIf`, `start.after`,");
        Line("`gates` and `variants[].when` (`StoryConditionParser`); a JSON list of conditions means all of them. Nesting is");
        Line($"limited to {StoryConditionParser.MaxDepth} levels.");
        Line();
        Line("```");
        Line("condition := atom | all:[c, c, …] | any:[c, c, …] | not:c");
        Line("```");
        Line();
        Line("### Condition forms");
        Line();
        Table("Form", "Holds when", Enum.GetNames(typeof(StoryConditionKind)).Select(name =>
        {
            string summary = docs.Summary(nameof(StoryConditionKind), name);
            int dash = summary.IndexOf(" — ", StringComparison.Ordinal);
            return dash > 0
                ? (summary.Substring(0, dash), char.ToUpperInvariant(summary[dash + 3]) + summary.Substring(dash + 4))
                : ("`" + name.ToLowerInvariant() + "`", summary);
        }));
        Line("### Pack states (`pack:<pack>:<state>`)");
        Line();
        Table("Form", "Holds when", Enum.GetValues(typeof(StoryPackState)).Cast<StoryPackState>().Select(state =>
            ("`pack:<pack>:" + StoryConditionParser.PackStateName(state) + "`", docs.Summary(nameof(StoryPackState), state.ToString()))));
        Line("### World predicates (`world:<predicate>`)");
        Line();
        Line($"{StoryWorldPredicates.All.Count} predicates (`StoryWorldPredicates.All`, held equal to the server's registry by");
        Line("`StoryConditionTests`). A predicate reads the player's own body and system, never another player's.");
        Line();
        Table("Predicate", "Holds when", StoryWorldPredicates.All.OrderBy(p => p, StringComparer.Ordinal).Select(p =>
            ("`world:" + p + "`", docs.Summary(nameof(StoryWorldPredicates), MemberOf(typeof(StoryWorldPredicates), p)))));

        // ---- Cutscenes ----
        Line("## Cutscenes (`cutscenes.json`)");
        Line();
        Line("### Tiers (`tier`)");
        Line();
        Table("Tier", "Meaning", new[]
        {
            (CutsceneTiers.Banner.ToString(System.Globalization.CultureInfo.InvariantCulture) + " — banner", docs.Summary(nameof(CutsceneTiers), nameof(CutsceneTiers.Banner))),
            (CutsceneTiers.Card.ToString(System.Globalization.CultureInfo.InvariantCulture) + " — card", docs.Summary(nameof(CutsceneTiers), nameof(CutsceneTiers.Card))),
            (CutsceneTiers.Scene.ToString(System.Globalization.CultureInfo.InvariantCulture) + " — scene", docs.Summary(nameof(CutsceneTiers), nameof(CutsceneTiers.Scene))),
            (CutsceneTiers.SetPiece.ToString(System.Globalization.CultureInfo.InvariantCulture) + " — set piece", docs.Summary(nameof(CutsceneTiers), nameof(CutsceneTiers.SetPiece))),
        });
        Line("### Camera types (`shots[].camera.type`)");
        Line();
        Line("A tier 1 or 2 scene never takes the camera: it may only use the types marked \"no\".");
        Line();
        Line("| Type | Takes the camera | Meaning |");
        Line("|---|---|---|");
        foreach (string camera in CutsceneCameras.All)
        {
            string takes = !CutsceneCameras.TakesCamera(camera) ? "no" : CutsceneCameras.IsSynthetic(camera) ? "yes (synthetic stage)" : "yes";
            Line($"| `{camera}` | {takes} | {Cell(docs.Summary(nameof(CutsceneCameras), MemberOf(typeof(CutsceneCameras), camera)))} |");
        }

        Line();
        Line("A `diorama` shows one of these anchors: " + string.Join(", ", CutsceneCameras.DioramaAnchors.Select(a => "`" + a + "`")) + ".");
        Line();
        Line("### Anchors (`shots[].camera.anchor`)");
        Line();
        Table("Anchor", "What the camera is placed around", Words(typeof(CutsceneAnchors), CutsceneAnchors.All));
        Line("### Caption styles (`shots[].captions[].style`)");
        Line();
        Line("| Style | Text budget EN / DE (shown characters) | Meaning |");
        Line("|---|---|---|");
        foreach (string style in CutsceneCaptionStyles.All)
        {
            Line($"| `{style}` | {CaptionBudget(style)} | {Cell(docs.Summary(nameof(CutsceneCaptionStyles), MemberOf(typeof(CutsceneCaptionStyles), style)))} |");
        }

        Line();
        Line("### Colour grades (`shots[].grade`)");
        Line();
        Table("Grade", "Meaning", Words(typeof(CutsceneGrades), CutsceneGrades.All));
        Line("### Argument sources (`arg`)");
        Line();
        Line("What fills a caption's `{0}` (and, for a chapter, `{1}`/`{2}`).");
        Line();
        Table("Value", "Meaning", Words(typeof(CutsceneArgSources), new[] { CutsceneArgSources.Chapter, CutsceneArgSources.Body, CutsceneArgSources.System },
            v => v.Length == 0 ? "(left out)" : "`" + v + "`"));

        // ---- Text budgets ----
        Line("## Text budgets");
        Line();
        Line($"Checked by both validators. A `{{key:…}}` token counts {StoryPackValidator.TokenWidth} characters, a `{{0}}` slot {StoryPackValidator.SlotWidth}.");
        Line();
        Table("Text", "Longest EN / DE (shown characters)", new[]
        {
            ("`shortNameKey`", $"{StoryPackValidator.ShortNameMax} / {StoryPackValidator.ShortNameMax}"),
            ("a basic-training line (a tutorial's non-optional chapters)", $"{StoryPackValidator.BasicLineMaxEn} / {StoryPackValidator.BasicLineMaxDe}"),
            ("any other step line (start, hint, done)", $"{StoryPackValidator.LineMaxEn} / {StoryPackValidator.LineMaxDe}"),
            ("a chip text (`chipKey`)", $"{StoryPackValidator.ChipMaxEn} / {StoryPackValidator.ChipMaxDe}"),
            ("a cutscene shot", $"{StoryPackValidator.ShotMaxSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} s"),
        });

        // ---- World stories ----
        Line("## World stories only");
        Line();
        Line("Read for a `scope: world` pack (the save's one narrative arc, today VEGA); a player pack never uses them.");
        Line();
        Line("### Lead types (`chapters[].leads[].type`)");
        Line();
        Table("Lead", "Meaning", Words(typeof(StoryLeadTypes), StoryLeadTypes.All));
        Line("### Chapter events (`chapters[].fromEvent`)");
        Line();
        Table("Event", "Meaning", Words(typeof(StoryChapterEvents), StoryChapterEvents.All));
        Line("### Finale stages (`finale.cutscenes`)");
        Line();
        Table("Stage", "When it plays", Words(typeof(StoryFinaleStages), StoryFinaleStages.All));
        Line("### Memory triggers (`memoryTriggers`)");
        Line();
        Table("Trigger", "Meaning", Words(typeof(StoryMemoryTriggers), StoryMemoryTriggers.All));

        string text = md.ToString();
        return text.TrimEnd('\n') + "\n";
    }

    /// <summary>The validator's budget of a caption style.</summary>
    private static string CaptionBudget(string style) => style switch
    {
        CutsceneCaptionStyles.Subtitle or CutsceneCaptionStyles.Speaker => $"{StoryPackValidator.CaptionLineMaxEn} / {StoryPackValidator.CaptionLineMaxDe} per page",
        CutsceneCaptionStyles.TitleCard => $"{StoryPackValidator.TitleCardMaxEn} / {StoryPackValidator.TitleCardMaxDe} (title and kicker)",
        CutsceneCaptionStyles.ReadingFrame => $"{StoryPackValidator.ReadingFrameMaxEn} / {StoryPackValidator.ReadingFrameMaxDe}",
        CutsceneCaptionStyles.Credits => "none",
        _ => throw new InvalidOperationException($"caption style '{style}' has no budget here — add it to CaptionBudget"),
    };
}
