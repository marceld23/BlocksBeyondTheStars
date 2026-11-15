// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The firewall verdict the host screen acts on (#3708). The rule strings are the ones Windows writes: the
/// first three were read off a machine after its firewall dialog was answered, the rest follow the same format.
/// A wrong verdict is silent either way — a host who is told nothing while friends cannot join, or a dialog
/// that nags a host whose world works.
/// </summary>
public sealed class HostFirewallRulesTests
{
    private const string Exe = @"C:\Users\kid\AppData\Local\BlocksBeyondTheStars\current\BlocksBeyondTheStars_Data\StreamingAssets\server\BlocksBeyondTheStars.GameServer.exe";
    private const int Port = 31550;

    /// <summary>What the Windows dialog leaves behind: one rule per protocol, the path in lower case.</summary>
    private static string[] DialogRules(string action, string profiles = "Profile=Private|Profile=Public|") => new[]
    {
        $"v2.33|Action={action}|Active=TRUE|Dir=In|Protocol=6|{profiles}App={Exe.ToLowerInvariant()}|Name=blocksbeyondthestars.gameserver.exe|Desc=blocksbeyondthestars.gameserver.exe|Defer=User|",
        $"v2.33|Action={action}|Active=TRUE|Dir=In|Protocol=17|{profiles}App={Exe.ToLowerInvariant()}|Name=blocksbeyondthestars.gameserver.exe|Desc=blocksbeyondthestars.gameserver.exe|Defer=User|",
    };

    [Fact]
    public void NoRuleForTheServer_IsNoRule()
    {
        var rules = new[]
        {
            @"v2.33|Action=Allow|Active=TRUE|Dir=In|Protocol=17|App=C:\Program Files\Other\other.exe|Name=Other|",
            @"v2.33|Action=Allow|Active=TRUE|Dir=In|Protocol=17|LPort=5353|App=%SystemRoot%\system32\svchost.exe|Svc=dnscache|Name=mDNS|",
        };

        Assert.Equal(HostFirewallState.NoRule, HostFirewallRules.Evaluate(rules, Exe, Port));
    }

    [Fact]
    public void TheDialogAnsweredWithAllow_IsAllowed()
        => Assert.Equal(HostFirewallState.Allowed, HostFirewallRules.Evaluate(DialogRules("Allow"), Exe, Port));

    [Fact]
    public void TheDialogDismissed_IsBlocked()
        => Assert.Equal(HostFirewallState.Blocked, HostFirewallRules.Evaluate(DialogRules("Block"), Exe, Port));

    [Fact]
    public void ABlockRuleBeatsAnAllowRuleInTheSameProfile()
    {
        var rules = new List<string>(DialogRules("Allow"));
        rules.AddRange(DialogRules("Block"));

        Assert.Equal(HostFirewallState.Blocked, HostFirewallRules.Evaluate(rules, Exe, Port));
    }

    [Fact]
    public void AllowedAtHomeBlockedInPublic_CountsAsAllowed()
    {
        // Windows 10's dialog with only "Private networks" ticked.
        var rules = new List<string>(DialogRules("Allow", "Profile=Private|"));
        rules.AddRange(DialogRules("Block", "Profile=Public|"));

        Assert.Equal(HostFirewallState.Allowed, HostFirewallRules.Evaluate(rules, Exe, Port));
    }

    [Fact]
    public void TheGamesOwnRule_IsAllowed()
    {
        var rules = new[]
        {
            $"v2.33|Action=Allow|Active=TRUE|Dir=In|Protocol=17|LPort={Port}|App={Exe}|Name={HostFirewallRules.RuleName}|",
        };

        Assert.Equal(HostFirewallState.Allowed, HostFirewallRules.Evaluate(rules, Exe, Port));
    }

    [Theory]
    [InlineData("Action=Allow|Active=FALSE|Dir=In|Protocol=17|")]            // switched off
    [InlineData("Action=Allow|Active=TRUE|Dir=Out|Protocol=17|")]            // outbound
    [InlineData("Action=Allow|Active=TRUE|Dir=In|Protocol=6|")]              // TCP only
    [InlineData("Action=Allow|Active=TRUE|Dir=In|Protocol=17|LPort=31551|")] // another port
    [InlineData("Action=Allow|Active=TRUE|Dir=In|Protocol=17|LPort=RPC|")]   // a keyword port
    public void ARuleThatDoesNotCoverInboundUdpOnThePort_DoesNotCount(string fields)
    {
        var rules = new[] { $"v2.33|{fields}App={Exe}|Name=x|" };

        Assert.Equal(HostFirewallState.NoRule, HostFirewallRules.Evaluate(rules, Exe, Port));
    }

    [Theory]
    [InlineData("Action=Allow|Active=TRUE|Dir=In|")]                                  // every protocol, every port
    [InlineData("Action=Allow|Active=TRUE|Dir=In|Protocol=17|LPort=80|LPort=31550|")]  // a port list
    [InlineData("Action=Allow|Active=TRUE|Dir=In|Protocol=17|LPort2_10=31000-32000|")] // a port range
    public void ARuleThatCoversThePort_Counts(string fields)
    {
        var rules = new[] { $"v2.33|{fields}App={Exe}|Name=x|" };

        Assert.Equal(HostFirewallState.Allowed, HostFirewallRules.Evaluate(rules, Exe, Port));
    }

    [Fact]
    public void APortRuleWithoutAProgram_AppliesToTheServerToo()
    {
        var rules = new[] { $"v2.33|Action=Allow|Active=TRUE|Dir=In|Protocol=17|LPort={Port}|Name=Game port|" };

        Assert.Equal(HostFirewallState.Allowed, HostFirewallRules.Evaluate(rules, Exe, Port));
    }

    [Theory]
    // A Store app's rule, as Windows writes it: no App=, scoped by its package family name.
    [InlineData("Action=Allow|Active=TRUE|Dir=In|Profile=Domain|Profile=Private|Name=Xbox|PFN=Microsoft.GamingApp_8wekyb3d8bbwe|LUOwn=S-1-5-21-1-2-3-1130|Platform=2:6:2|")]
    // A program-less rule without a port: scoped by something this reader does not model.
    [InlineData("Action=Allow|Active=TRUE|Dir=In|Protocol=17|RA4=LocalSubnet|Name=Something|")]
    public void AProgramLessRuleThatIsNoPortRule_DoesNotCount(string fields)
    {
        var rules = new[] { $"v2.33|{fields}" };

        Assert.Equal(HostFirewallState.NoRule, HostFirewallRules.Evaluate(rules, Exe, Port));
    }

    [Fact]
    public void ThePathIsComparedWithoutCaseAndWithEitherSlash()
    {
        // Unity hands out the StreamingAssets path with forward slashes.
        Assert.Equal(HostFirewallState.Allowed, HostFirewallRules.Evaluate(DialogRules("Allow"), Exe.Replace('\\', '/'), Port));
    }

    [Fact]
    public void AllowCommand_ReplacesTheServersRulesByOneNarrowRule()
    {
        string? command = HostFirewallRules.AllowCommand(Exe.Replace('\\', '/'), Port);

        Assert.Equal(
            "/c \"netsh advfirewall firewall delete rule name=all program=\"" + Exe + "\""
            + " & netsh advfirewall firewall add rule name=\"Blocks Beyond the Stars (hosting)\" dir=in action=allow program=\"" + Exe + "\""
            + " protocol=UDP localport=31550 profile=any\"",
            command);
    }

    [Theory]
    [InlineData(@"C:\Games\100%\server.exe")]   // cmd.exe would expand the percent sign
    [InlineData("C:\\Games\\a\"b\\server.exe")] // a quote would end the quoted path
    [InlineData("")]
    public void AllowCommand_RefusesAPathItCannotQuote(string exe)
        => Assert.Null(HostFirewallRules.AllowCommand(exe, Port));

    [Fact]
    public void AllowCommand_KeepsAnAmpersandInsideTheQuotedPath()
    {
        string? command = HostFirewallRules.AllowCommand(@"C:\Users\Tom & Ann\server.exe", Port);

        Assert.Contains("program=\"C:\\Users\\Tom & Ann\\server.exe\"", command);
    }
}
