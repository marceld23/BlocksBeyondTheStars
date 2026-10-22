// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Measures the sizes <see cref="CodeHealthRatchetTests"/> ratchets: per <c>*.cs</c> file its line count and the methods,
/// constructors, destructors and operators whose declaration spans more than <see cref="MethodLineLimit"/> lines. Roslyn only
/// parses (no compilation), so the scan needs no build and no Unity. Local functions, lambdas and property accessors are not
/// members of their own here — they count toward the method that holds them, or not at all. Top-level-statement files
/// (the <c>Program.cs</c> hosts) therefore show no long methods.
/// </summary>
internal static class CodeHealthScanner
{
    /// <summary>A method whose declaration spans more lines than this is a long method.</summary>
    public const int MethodLineLimit = 60;

    /// <summary>A file with more lines than this is an oversized file.</summary>
    public const int FileLineLimit = 1000;

    /// <summary>The scanned directories, relative to the repository root, with forward slashes.</summary>
    public static readonly string[] ScanRoots = { "src", "client/Assets/BlocksBeyondTheStars/Scripts" };

    /// <summary>
    /// Generated files are skipped: the git-ignored <c>*.Generated.cs</c> build secrets exist only in some checkouts, so
    /// counting them would make the baseline depend on the machine that wrote it.
    /// </summary>
    private static readonly string[] GeneratedSuffixes = { ".Generated.cs", ".g.cs", ".Designer.cs" };

    /// <summary>The desktop player's symbol set: the build every player runs and the one the Unity scripts ship in.</summary>
    private static readonly string[] ClientSymbols = { "UNITY_STANDALONE", "UNITY_STANDALONE_WIN", "UNITY_64", "UNITY_2020_2_OR_NEWER", "BBS_VOICE" };

    /// <summary>
    /// Server sources parse with the newest language version this Roslyn knows (the build uses <c>LangVersion=latest</c>)
    /// and no preprocessor symbols. Keep the package at least as new as the SDK's compiler, or newer syntax shows up as a
    /// parse error in the ratchet.
    /// </summary>
    public static readonly CSharpParseOptions ServerParseOptions = CSharpParseOptions.Default
        .WithLanguageVersion(LanguageVersion.Latest)
        .WithDocumentationMode(DocumentationMode.None);

    /// <summary>Client sources parse like the server's, with the desktop player's preprocessor symbols.</summary>
    public static readonly CSharpParseOptions ClientParseOptions = ServerParseOptions.WithPreprocessorSymbols(ClientSymbols);

    /// <summary>A method over the limit: its name, 1-based start line and length in lines.</summary>
    public sealed record LongMethod(string Name, int Line, int Lines);

    /// <summary>One scanned file: its repository-relative path (forward slashes), line count, long methods and parse errors.</summary>
    public sealed record FileHealth(string Path, int Lines, IReadOnlyList<LongMethod> LongMethods, IReadOnlyList<string> ParseErrors);

    /// <summary>The number of lines in <paramref name="text"/>, as <c>wc -l</c> counts an LF file with a final newline.</summary>
    public static int CountLines(string text)
    {
        int newlines = text.AsSpan().Count('\n');
        return text.Length > 0 && text[^1] != '\n' ? newlines + 1 : newlines;
    }

    /// <summary>Parses <paramref name="text"/> and measures it.</summary>
    public static FileHealth ScanText(string relativePath, string text, CSharpParseOptions options)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(text, options, path: relativePath);
        var longMethods = new List<LongMethod>();
        foreach (BaseMethodDeclarationSyntax method in tree.GetRoot().DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
        {
            FileLinePositionSpan span = method.GetLocation().GetLineSpan();
            int lines = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
            if (lines > MethodLineLimit)
            {
                longMethods.Add(new LongMethod(NameOf(method), span.StartLinePosition.Line + 1, lines));
            }
        }

        List<string> parseErrors = tree.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();
        List<LongMethod> ordered = longMethods.OrderByDescending(m => m.Lines).ThenBy(m => m.Line).ToList();
        return new FileHealth(relativePath, CountLines(text), ordered, parseErrors);
    }

    /// <summary>Measures one file; client files parse with <see cref="ClientParseOptions"/>.</summary>
    public static FileHealth ScanFile(string repoRoot, string file)
    {
        string relativePath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
        CSharpParseOptions options = relativePath.StartsWith("client/", StringComparison.Ordinal) ? ClientParseOptions : ServerParseOptions;
        return ScanText(relativePath, File.ReadAllText(file), options);
    }

    /// <summary>Measures every source file under <see cref="ScanRoots"/>, ordered by path (ordinal).</summary>
    public static IReadOnlyList<FileHealth> ScanRepository(string repoRoot)
    {
        var files = new List<string>();
        foreach (string root in ScanRoots)
        {
            string directory = Path.Combine(repoRoot, root.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException($"Code-health scan root '{root}' does not exist under {repoRoot}.");
            }

            // An absolute directory makes SourceFiles' Path.Combine ignore its own repository root.
            files.AddRange(TestPaths.SourceFiles(directory).Where(file => !IsGenerated(file)));
        }

        return files.AsParallel()
            .Select(file => ScanFile(repoRoot, file))
            .OrderBy(health => health.Path, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsGenerated(string file)
        => GeneratedSuffixes.Any(suffix => file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    private static string NameOf(BaseMethodDeclarationSyntax method) => method switch
    {
        MethodDeclarationSyntax m => m.Identifier.Text,
        ConstructorDeclarationSyntax c => c.Identifier.Text + "..ctor",
        DestructorDeclarationSyntax d => "~" + d.Identifier.Text,
        OperatorDeclarationSyntax o => "operator " + o.OperatorToken.Text,
        ConversionOperatorDeclarationSyntax c => c.ImplicitOrExplicitKeyword.Text + " operator",
        _ => method.Kind().ToString(),
    };
}

/// <summary>
/// The committed baseline of <see cref="CodeHealthRatchetTests"/>: one line per file that has a long method or is over the
/// file limit, ordinal-sorted, LF line endings, so a diff shows exactly which files changed and merges stay line-local.
/// </summary>
internal static class CodeHealthBaseline
{
    /// <summary>The baseline file, relative to the repository root.</summary>
    public const string RelativePath = "tests/BlocksBeyondTheStars.Tests/Fixtures/code_health_baseline.json";

    /// <summary>Set to <c>1</c> to make the gate rewrite the baseline from the current tree.</summary>
    public const string RebaseVariable = "BBS_CODE_HEALTH_REBASE";

    /// <summary>The test that compares the tree with the baseline (and rewrites it in rebase mode).</summary>
    public const string GateFilter = "FullyQualifiedName~CodeHealthRatchetTests.SourceTree_DoesNotGrowPastTheBaseline";

    /// <summary>The rebase command for bash.</summary>
    public const string BashRebaseCommand =
        RebaseVariable + "=1 dotnet test tests/BlocksBeyondTheStars.Tests/BlocksBeyondTheStars.Tests.csproj --filter " + GateFilter;

    /// <summary>The rebase command for PowerShell.</summary>
    public const string PwshRebaseCommand =
        "$env:" + RebaseVariable + "=1; dotnet test tests/BlocksBeyondTheStars.Tests/BlocksBeyondTheStars.Tests.csproj --filter " +
        GateFilter + "; Remove-Item Env:" + RebaseVariable;

    /// <summary>The <c>_comment</c> written at the top of the baseline (plain ASCII, nothing to escape).</summary>
    public const string Comment =
        "Code-health ratchet (CodeHealthRatchetTests): per file the number of methods longer than methodLines and the line " +
        "count of files longer than fileLines. Counts may only shrink. Rebase: " + BashRebaseCommand;

    /// <summary>A baseline entry: the file's line count when the baseline was written and its long-method count.</summary>
    public sealed record Entry(int Lines, int LongMethods);

    /// <summary>Renders the baseline text for <paramref name="files"/> (byte-stable: '\n' only, final newline).</summary>
    public static string Render(IReadOnlyList<CodeHealthScanner.FileHealth> files)
    {
        List<string> rows = files
            .Where(f => f.Lines > CodeHealthScanner.FileLineLimit || f.LongMethods.Count > 0)
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .Select(f => string.Create(CultureInfo.InvariantCulture,
                $"    \"{JsonEncodedText.Encode(f.Path)}\": {{ \"lines\": {f.Lines}, \"longMethods\": {f.LongMethods.Count} }}"))
            .ToList();

        var text = new StringBuilder();
        text.Append("{\n");
        text.Append("  \"_comment\": \"").Append(Comment).Append("\",\n");
        text.Append(string.Create(CultureInfo.InvariantCulture, $"  \"methodLines\": {CodeHealthScanner.MethodLineLimit},\n"));
        text.Append(string.Create(CultureInfo.InvariantCulture, $"  \"fileLines\": {CodeHealthScanner.FileLineLimit},\n"));
        text.Append("  \"files\": {\n");
        if (rows.Count > 0)
        {
            text.Append(string.Join(",\n", rows)).Append('\n');
        }

        text.Append("  }\n");
        text.Append("}\n");
        return text.ToString();
    }

    /// <summary>Reads a baseline written by <see cref="Render"/>; the <c>_comment</c> is ignored.</summary>
    public static (int MethodLines, int FileLines, SortedDictionary<string, Entry> Files) Read(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        var files = new SortedDictionary<string, Entry>(StringComparer.Ordinal);
        foreach (JsonProperty file in root.GetProperty("files").EnumerateObject())
        {
            files[file.Name] = new Entry(file.Value.GetProperty("lines").GetInt32(), file.Value.GetProperty("longMethods").GetInt32());
        }

        return (root.GetProperty("methodLines").GetInt32(), root.GetProperty("fileLines").GetInt32(), files);
    }
}
