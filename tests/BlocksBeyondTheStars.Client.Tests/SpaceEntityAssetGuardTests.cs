// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.RegularExpressions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2938 — the flight view's entity models take the Materials and block tiles <c>ModelKit</c> made for them along when
/// they despawn. Unity never garbage-collects a code-made Material or Texture2D, so every drone, drop, pod or station
/// that came and went used to leave them behind until the return to the menu. The fix records what the kit makes between
/// <c>BeginOwned</c> and <c>EndOwned</c> and hands it to the model's parts (<c>ModelAssetLease</c>). This guard keeps the
/// three things that make it work in place, in PR CI (which never runs Unity): every kit helper records what it makes,
/// both spawn builders run inside an owned scope, and no entity builder caches a kit material in a field (it would die
/// with the first model and leave the next one without its look). The Unity scripts are read as text (the UI wiring
/// guards' pattern); the PlayMode twin, <c>SpaceEntityAssetsPlayModeTests</c>, counts the real objects.
/// </summary>
public sealed class SpaceEntityAssetGuardTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly Regex NewAsset = new(@"new\s+(?:Material|Texture2D)\s*\(", RegexOptions.None, RegexTimeout);

    private static readonly Regex OwnedNewAsset = new(@"Own\(\s*new\s+(?:Material|Texture2D)\s*\(", RegexOptions.None, RegexTimeout);

    private static readonly Regex BuilderCall = new(@"\b(?<name>Build\w+Model)\(", RegexOptions.ExplicitCapture, RegexTimeout);

    private static string Script(string name)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", name));

    [Fact]
    public void EveryModelKitAsset_IsRecordedForTheModelBeingBuilt()
    {
        string kit = Script("ModelKit.cs");
        int made = NewAsset.Matches(kit).Count;
        Assert.True(made >= 4, $"precondition: ModelKit makes its materials and tiles itself (found {made} 'new Material/Texture2D')");
        Assert.Equal(made, OwnedNewAsset.Matches(kit).Count);
    }

    [Fact]
    public void BothSpawnBuilders_RunInsideAnOwnedScope()
    {
        string view = ClientTestPaths.ScriptPartsText("SpaceView");
        Assert.Contains("go = BuildEntityModel(e);", Body(view, "private void SyncEntities()"), StringComparison.Ordinal);

        string entity = Body(view, "private GameObject BuildEntityModel(");
        Assert.Contains("var owned = BeginOwned();", entity, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"finally\s*\{\s*EndOwned\(owned, go\);", RegexOptions.None, RegexTimeout), entity);

        string avatar = Body(view, "private RemoteAvatar BuildRemoteAvatar()");
        Assert.Contains("var owned = BeginOwned();", avatar, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"finally\s*\{\s*EndOwned\(owned, ship\);", RegexOptions.None, RegexTimeout), avatar);
    }

    [Fact]
    public void NoEntityBuilder_CachesAKitMaterialInAField()
    {
        string view = ClientTestPaths.ScriptPartsText("SpaceView");
        var builders = BuilderCall.Matches(Body(view, "private GameObject BuildEntityModel("))
            .Select(m => m.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(builders.Count >= 10, $"precondition: BuildEntityModel dispatches to the per-kind builders (found {builders.Count})");

        var failures = new List<string>();
        foreach (string name in builders)
        {
            foreach (Match decl in Regex.Matches(view, $@"private GameObject {name}\(", RegexOptions.None, RegexTimeout))
            {
                if (Body(view, decl.Index).Contains("??=", StringComparison.Ordinal))
                {
                    failures.Add($"{name} caches something with '??=' — a kit material made in an owned build dies with the first model");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>The braced body of the member declared at the first <paramref name="signature"/>.</summary>
    private static string Body(string text, string signature)
    {
        int at = text.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{signature}' not found in the SpaceView parts — did it move or get renamed?");
        return Body(text, at);
    }

    /// <summary>The braced body of the member declared at <paramref name="at"/>.</summary>
    private static string Body(string text, int at)
    {
        int open = text.IndexOf('{', at);
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}' && --depth == 0)
            {
                return text.Substring(open, i - open + 1);
            }
        }

        throw new InvalidOperationException("unbalanced braces after index " + at);
    }
}
