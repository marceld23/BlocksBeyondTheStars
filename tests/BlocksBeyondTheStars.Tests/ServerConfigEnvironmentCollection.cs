// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The home of every test class that touches the process-wide <c>BBS_*</c> server variables (#2923).
/// <see cref="ServerConfigTests"/> and <see cref="ServerConfigOptionsTests"/> set them for a moment through
/// <see cref="ServerConfigTests.WithEnvironment"/>, and <c>ServerConfig.ApplyEnvironment</c> reads them — directly, or on
/// every <c>AdminService.LoadConfig</c>. xunit runs the classes of one collection one after another, so a class that
/// loads the config can no longer pick up a value another class set for its own test (a <c>BBS_DATABASE_PROVIDER</c> or
/// <c>BBS_WORLD</c> meant for <see cref="ServerConfigTests"/> used to reach <see cref="AdminServiceTests"/> that way now
/// and then). The collection still runs beside every other collection; it only orders its own members.
/// </summary>
[CollectionDefinition(Name)]
public static class ServerConfigEnvironmentCollection
{
    public const string Name = "ServerConfigEnvironment";
}

/// <summary>
/// Keeps <see cref="ServerConfigEnvironmentCollection"/> complete: every test class declared in a file that reads or sets
/// the <c>BBS_*</c> server variables — it uses <c>AdminService</c> (each config load applies the environment), calls
/// <c>ApplyEnvironment</c>, uses the <c>WithEnvironment</c> helper, or sets a <c>BBS_*</c> server variable itself — must
/// be a member. The WorldHost (<c>BBS_WH_*</c>) and ReportHost (<c>BBS_REPORTS_*</c>) variables belong to other configs and
/// are not covered. This file names the patterns itself, so its own class is a member as well.
/// </summary>
[Collection(ServerConfigEnvironmentCollection.Name)]
public sealed class ServerConfigEnvironmentCollectionTests
{
    /// <summary>Source text that loads <c>ServerConfig</c> from the environment, or sets the variables it reads.</summary>
    private static readonly Regex TouchesServerVariables = new(
        @"\bAdminService\b|\.ApplyEnvironment\(|\bWithEnvironment\(|SetEnvironmentVariable\(\s*""BBS_(?!WH_|REPORTS_)",
        RegexOptions.None, TimeSpan.FromSeconds(5));

    /// <summary>A top-level class declaration: the test files use file-scoped namespaces, so it starts in column 0.</summary>
    private static readonly Regex TopLevelClass = new(
        @"^(?:(?:public|internal|static|sealed|abstract|partial)\s+)*class\s+(?<name>[A-Za-z_]\w*)",
        RegexOptions.Multiline | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(5));

    [Fact]
    public void EveryTestClassThatTouchesTheServerVariables_JoinsTheCollection()
    {
        var topLevelTypes = typeof(ServerConfigEnvironmentCollectionTests).Assembly.GetTypes()
            .Where(t => !t.IsNested)
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var touching = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in TestPaths.SourceFiles(Path.Combine("tests", "BlocksBeyondTheStars.Tests")))
        {
            string source = File.ReadAllText(file);
            if (!TouchesServerVariables.IsMatch(source))
            {
                continue;
            }

            foreach (Match match in TopLevelClass.Matches(source))
            {
                if (topLevelTypes.TryGetValue(match.Groups["name"].Value, out var types) && types.Any(IsTestClass))
                {
                    touching.Add(match.Groups["name"].Value);
                }
            }
        }

        // The scan must still find the classes it was written for — a moved file or a renamed helper must not let the
        // guard pass on nothing.
        Assert.Superset(
            new SortedSet<string>(StringComparer.Ordinal)
            {
                nameof(ServerConfigTests), nameof(ServerConfigOptionsTests), nameof(AdminServiceTests), nameof(AdminContentTests),
            },
            touching);

        var outside = touching.Where(name => !topLevelTypes[name].Where(IsTestClass).All(InTheCollection)).ToList();
        Assert.True(outside.Count == 0,
            $"These test classes read or set the BBS_* server variables but run outside [Collection(ServerConfigEnvironmentCollection.Name)], " +
            "so a parallel ServerConfigTests can hand them its values (#2923) — add the attribute: " + string.Join(", ", outside));
    }

    private static bool IsTestClass(Type type)
        => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Any(m => m.IsDefined(typeof(FactAttribute), inherit: true));

    private static bool InTheCollection(Type type)
        => type.GetCustomAttributesData().Any(a =>
            a.AttributeType == typeof(CollectionAttribute)
            && a.ConstructorArguments.Count == 1
            && a.ConstructorArguments[0].Value is string name
            && string.Equals(name, ServerConfigEnvironmentCollection.Name, StringComparison.Ordinal));
}
