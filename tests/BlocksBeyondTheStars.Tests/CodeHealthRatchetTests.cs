// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Diagnostics;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using FileHealth = BlocksBeyondTheStars.Tests.CodeHealthScanner.FileHealth;
using LongMethod = BlocksBeyondTheStars.Tests.CodeHealthScanner.LongMethod;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The code-health ratchet (#2896): methods and files may not grow past the size they had when the baseline was written.
/// MA0051 ("method is too long") stays off in .editorconfig because the tree already holds about a thousand methods over its
/// 60-line default — a build-breaking analyzer could only be switched on after every one of them is split. Instead,
/// <see cref="CodeHealthScanner"/> counts per file (under <c>src/</c> and <c>client/Assets/BlocksBeyondTheStars/Scripts/</c>)
/// the methods longer than 60 lines and whether the file is longer than 1000 lines, and the committed baseline
/// (<c>tests/BlocksBeyondTheStars.Tests/Fixtures/code_health_baseline.json</c>) records those counts. The gate fails when a
/// file's long-method count GROWS or a file newly crosses 1000 lines; a file already over 1000 lines may keep growing (its
/// line count in the baseline is informational). When counts SHRINK the gate passes and prints the rebase command, so the
/// change that split a method records the win in the same PR:
/// <c>BBS_CODE_HEALTH_REBASE=1 dotnet test tests/BlocksBeyondTheStars.Tests/BlocksBeyondTheStars.Tests.csproj --filter
/// FullyQualifiedName~CodeHealthRatchetTests.SourceTree_DoesNotGrowPastTheBaseline</c>. The baseline is never edited by hand.
/// </summary>
public sealed class CodeHealthRatchetTests
{
    private const string Indent = "    ";
    private const string ServerSample = "src/Sample.cs";
    private const string ClientSample = "client/Assets/BlocksBeyondTheStars/Scripts/Sample.cs";

    private static readonly string[] LongMembersOfTheMixedSample = { "Outer", "Sample..ctor", "Pick" };
    private static readonly string[] LongMembersOfTheDesktopPlayer = { "DesktopPlayer", "Voice" };

    private readonly ITestOutputHelper _output;

    public CodeHealthRatchetTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Scanner_CountsMethodsOverTheLimit_ByDeclarationLines()
    {
        string text = Class(
            Member(Indent, "public void Sixty()", 60),
            Member(Indent, "public static void SixtyOne()", 61));

        FileHealth health = CodeHealthScanner.ScanText(ServerSample, text, CodeHealthScanner.ServerParseOptions);

        Assert.Empty(health.ParseErrors);
        LongMethod method = Assert.Single(health.LongMethods);
        Assert.Equal("SixtyOne", method.Name);
        Assert.Equal(61, method.Lines);
        Assert.Equal(LineOf(text, "public static void SixtyOne()"), method.Line);
    }

    [Fact]
    public void Scanner_IgnoresLocalFunctionsLambdasAndAccessors_AndCountsConstructors()
    {
        string localFunction = Member(Indent + Indent, "int Local()", 70, Indent + Indent + Indent + "return 1;");
        string text = Class(
            Member(Indent, "public Sample()", 70),
            Member(Indent, "public int Outer()", 75, localFunction + "\n" + Indent + Indent + "return Local();"),
            Member(Indent, "public int Prop", 74, Member(Indent + Indent, "get", 70, Indent + Indent + Indent + "return 1;")),
            Member(Indent, "private static readonly System.Func<int> Lambda = () =>", 70, Indent + Indent + "return 1;", "};"),
            Member(Indent, "public static int Pick(int x) => x switch", 65, Indent + Indent + "_ => x,", "};"));

        FileHealth health = CodeHealthScanner.ScanText(ServerSample, text, CodeHealthScanner.ServerParseOptions);

        Assert.Empty(health.ParseErrors);
        Assert.Equal(LongMembersOfTheMixedSample, health.LongMethods.Select(m => m.Name));
        Assert.Equal(75, health.LongMethods[0].Lines);
        Assert.Equal(65, health.LongMethods[2].Lines);
    }

    [Fact]
    public void Scanner_CountsLines_LikeWcL()
    {
        Assert.Equal(2, CodeHealthScanner.CountLines("a\nb\n"));
        Assert.Equal(2, CodeHealthScanner.CountLines("a\nb"));
        Assert.Equal(0, CodeHealthScanner.CountLines(""));
        Assert.Equal(2, CodeHealthScanner.CountLines("a\r\nb\r\n"));
    }

    [Fact]
    public void Scanner_SkipsCodeInDisabledPreprocessorBranches()
    {
        string text = Class(
            "#if UNITY_WEBGL && !UNITY_EDITOR\n" + Member(Indent, "public void Browser()", 70) + "\n#endif",
            "#if !UNITY_EDITOR && !UNITY_WEBGL\n" + Member(Indent, "public void DesktopPlayer()", 70) + "\n#endif",
            "#if UNITY_EDITOR\n" + Member(Indent, "public void EditorOnly()", 70) + "\n#endif",
            "#if BBS_VOICE\n" + Member(Indent, "public void Voice()", 70) + "\n#endif");

        FileHealth health = CodeHealthScanner.ScanText(ClientSample, text, CodeHealthScanner.ClientParseOptions);

        Assert.Empty(health.ParseErrors);
        Assert.Equal(LongMembersOfTheDesktopPlayer, health.LongMethods.Select(m => m.Name));
    }

    [Fact]
    public void Baseline_RendersOneLinePerFile_SortedAndStable()
    {
        LongMethod[] twoLongMethods = { new("First", 10, 80), new("Second", 100, 61) };
        var files = new List<FileHealth>
        {
            new("src/alpha.cs", 300, twoLongMethods, Array.Empty<string>()),
            new("src/Zeta.cs", 1200, Array.Empty<LongMethod>(), Array.Empty<string>()),
            new("src/Small.cs", 100, Array.Empty<LongMethod>(), Array.Empty<string>()),
        };
        string expected =
            "{\n" +
            "  \"_comment\": \"" + CodeHealthBaseline.Comment + "\",\n" +
            "  \"methodLines\": 60,\n" +
            "  \"fileLines\": 1000,\n" +
            "  \"files\": {\n" +
            "    \"src/Zeta.cs\": { \"lines\": 1200, \"longMethods\": 0 },\n" +
            "    \"src/alpha.cs\": { \"lines\": 300, \"longMethods\": 2 }\n" +
            "  }\n" +
            "}\n";

        string rendered = CodeHealthBaseline.Render(files);

        Assert.Equal(expected, rendered);
        Assert.Equal(rendered, CodeHealthBaseline.Render(files.AsEnumerable().Reverse().ToList()));

        string path = Path.Combine(Path.GetTempPath(), $"code_health_baseline_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, rendered);
            (int methodLines, int fileLines, SortedDictionary<string, CodeHealthBaseline.Entry> entries) = CodeHealthBaseline.Read(path);
            Assert.Equal(CodeHealthScanner.MethodLineLimit, methodLines);
            Assert.Equal(CodeHealthScanner.FileLineLimit, fileLines);
            Assert.Equal(2, entries.Count);
            Assert.Equal(new CodeHealthBaseline.Entry(1200, 0), entries["src/Zeta.cs"]);
            Assert.Equal(new CodeHealthBaseline.Entry(300, 2), entries["src/alpha.cs"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SourceTree_DoesNotGrowPastTheBaseline()
    {
        string repoRoot = TestPaths.RepoRoot();
        var clock = Stopwatch.StartNew();
        IReadOnlyList<FileHealth> scanned = CodeHealthScanner.ScanRepository(repoRoot);
        _output.WriteLine($"Scanned {scanned.Count} files in {clock.ElapsedMilliseconds} ms.");

        int serverFiles = scanned.Count(f => f.Path.StartsWith("src/", StringComparison.Ordinal));
        int clientFiles = scanned.Count(f => f.Path.StartsWith("client/", StringComparison.Ordinal));
        Assert.True(serverFiles >= 500 && clientFiles >= 200,
            $"only {serverFiles} server and {clientFiles} client files scanned — did the scan miss the sources?");

        var broken = scanned.Where(f => f.ParseErrors.Count > 0).ToList();
        Assert.True(broken.Count == 0,
            "Roslyn could not parse these files. Fix the syntax — or, if the compiler accepts it, raise the " +
            "Microsoft.CodeAnalysis.CSharp version in the test project to the SDK's:\n" +
            string.Join('\n', broken.Select(f => $"{f.Path}: {f.ParseErrors[0]}")));

        string rendered = CodeHealthBaseline.Render(scanned);
        string baselinePath = Path.Combine(repoRoot, CodeHealthBaseline.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (string.Equals(Environment.GetEnvironmentVariable(CodeHealthBaseline.RebaseVariable), "1", StringComparison.Ordinal))
        {
            Rebase(baselinePath, rendered);
            return;
        }

        Assert.True(File.Exists(baselinePath),
            $"{CodeHealthBaseline.RelativePath} is missing — write it with:\n{RebaseCommands()}");
        (int methodLines, int fileLines, SortedDictionary<string, CodeHealthBaseline.Entry> baseline) = CodeHealthBaseline.Read(baselinePath);
        Assert.True(methodLines == CodeHealthScanner.MethodLineLimit && fileLines == CodeHealthScanner.FileLineLimit,
            $"The limits changed (baseline {methodLines}/{fileLines} lines, scanner {CodeHealthScanner.MethodLineLimit}/" +
            $"{CodeHealthScanner.FileLineLimit}) — rebase:\n{RebaseCommands()}");

        var growth = new List<string>();
        var stale = new List<string>();
        var scannedPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (FileHealth file in scanned)
        {
            scannedPaths.Add(file.Path);
            CodeHealthBaseline.Entry entry = baseline.TryGetValue(file.Path, out CodeHealthBaseline.Entry? known)
                ? known
                : new CodeHealthBaseline.Entry(0, 0);

            if (file.LongMethods.Count > entry.LongMethods)
            {
                string methods = string.Join(", ", file.LongMethods.Take(10).Select(m => $"{m.Name} ({m.Lines} lines, line {m.Line})"));
                growth.Add($"{file.Path}: {file.LongMethods.Count} long methods (baseline {entry.LongMethods}) — {methods}");
            }
            else if (file.LongMethods.Count < entry.LongMethods)
            {
                stale.Add($"{file.Path} ({file.LongMethods.Count} long methods, baseline {entry.LongMethods})");
            }

            if (file.Lines > CodeHealthScanner.FileLineLimit && entry.Lines <= CodeHealthScanner.FileLineLimit)
            {
                growth.Add($"{file.Path}: {file.Lines} lines, newly over the {CodeHealthScanner.FileLineLimit}-line limit");
            }
            else if (file.Lines <= CodeHealthScanner.FileLineLimit && entry.Lines > CodeHealthScanner.FileLineLimit)
            {
                stale.Add($"{file.Path} ({file.Lines} lines, no longer over {CodeHealthScanner.FileLineLimit})");
            }
        }

        stale.AddRange(baseline.Keys.Where(path => !scannedPaths.Contains(path)).Select(path => $"{path} (no longer scanned)"));
        if (stale.Count > 0)
        {
            _output.WriteLine($"Baseline is stale (counts shrank) for: {string.Join("; ", stale)} — rebase with: " +
                              $"{CodeHealthBaseline.BashRebaseCommand} or {CodeHealthBaseline.PwshRebaseCommand}");
        }

        Assert.True(growth.Count == 0,
            $"Code-health ratchet: these files got worse — split the method (limit {CodeHealthScanner.MethodLineLimit} lines) " +
            $"or the file (limit {CodeHealthScanner.FileLineLimit} lines) instead of growing it:\n" +
            string.Join('\n', growth) + "\n" +
            "If the growth is deliberate and reviewed, or a refactor moved a long method here, rebase the baseline in the same PR:\n" +
            RebaseCommands());
    }

    /// <summary>Rebase mode: writes the baseline when it differs and fails once, so the rewrite is never silent.</summary>
    private void Rebase(string baselinePath, string rendered)
    {
        if (File.Exists(baselinePath) && string.Equals(File.ReadAllText(baselinePath), rendered, StringComparison.Ordinal))
        {
            _output.WriteLine($"{CodeHealthBaseline.RelativePath} is up to date.");
            return;
        }

        File.WriteAllText(baselinePath, rendered, new UTF8Encoding(false));
        Assert.Fail($"Baseline rewritten at {CodeHealthBaseline.RelativePath} — review the diff, commit it, and run the test " +
                    $"again without {CodeHealthBaseline.RebaseVariable}.");
    }

    private static string RebaseCommands()
        => $"  bash: {CodeHealthBaseline.BashRebaseCommand}\n  pwsh: {CodeHealthBaseline.PwshRebaseCommand}";

    /// <summary>A sample class holding <paramref name="members"/>, separated by blank lines.</summary>
    private static string Class(params string[] members) => "class Sample\n{\n" + string.Join("\n\n", members) + "\n}\n";

    /// <summary>
    /// A member of exactly <paramref name="totalLines"/> lines: the header, an opening brace, <paramref name="body"/>, filler
    /// comments, and <paramref name="close"/>.
    /// </summary>
    private static string Member(string indent, string header, int totalLines, string body = "", string close = "}")
    {
        var lines = new List<string> { indent + header, indent + "{" };
        if (body.Length > 0)
        {
            lines.AddRange(body.Split('\n'));
        }

        while (lines.Count < totalLines - 1)
        {
            lines.Add(indent + Indent + "// line");
        }

        lines.Add(indent + close);
        Assert.True(lines.Count == totalLines, $"the body of '{header}' does not fit into {totalLines} lines");
        return string.Join('\n', lines);
    }

    /// <summary>The 1-based line on which <paramref name="marker"/> first appears in <paramref name="text"/>.</summary>
    private static int LineOf(string text, string marker)
        => text.AsSpan(0, text.IndexOf(marker, StringComparison.Ordinal)).Count('\n') + 1;
}
