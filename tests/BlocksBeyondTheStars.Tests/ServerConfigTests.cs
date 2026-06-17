using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Command-line overrides used to embed/launch the server (e.g. local singleplayer host).</summary>
public sealed class ServerConfigTests
{
    [Fact]
    public void ApplyCommandLine_OverridesKnownKeys()
    {
        var config = new ServerConfig();
        var applied = config.ApplyCommandLine(new[]
        {
            "--port", "31550",
            "--name", "Singleplayer",
            "--world", "singleplayer",
            "--saves", @"C:\sp\saves",
            "--data", @"C:\sp\data",
            "--max-players", "1",
            "--view-distance", "3",
        });

        Assert.Equal(31550, config.GameplayPort);
        Assert.Equal("Singleplayer", config.ServerName);
        Assert.Equal("singleplayer", config.WorldName);
        Assert.Equal(@"C:\sp\saves", config.SavesRoot);
        Assert.Equal(@"C:\sp\data", config.DataDir);
        Assert.Equal(1, config.MaxPlayers);
        Assert.Equal(3, config.ViewDistanceChunks);
        Assert.Contains("port", applied);
        Assert.Contains("max-players", applied);
        Assert.Contains("view-distance", applied);
    }

    [Fact]
    public void ApplyCommandLine_OverridesSpaceRules()
    {
        var config = new ServerConfig();
        var applied = config.ApplyCommandLine(new[] { "--free-flight", "true", "--space-combat", "PvE", "--space-npcs", "Normal" });

        Assert.True(config.Rules.FreeSpaceFlight);
        Assert.Equal(SpaceCombatMode.PvE, config.Rules.SpaceCombat);
        Assert.Equal(AlienActivity.Normal, config.Rules.SpaceNpcEnemies);
        Assert.Contains("free-flight", applied);
    }

    [Fact]
    public void ApplyCommandLine_OverridesShipWeaponsAndKeepRules()
    {
        var config = new ServerConfig();
        var applied = config.ApplyCommandLine(new[]
        {
            "--ship-weapons", "NpcsOnly", "--keep-ship", "false", "--keep-inventory", "true",
        });

        Assert.Equal(ShipWeaponMode.NpcsOnly, config.Rules.ShipWeapons);
        Assert.False(config.Rules.KeepShipOnDeath);
        Assert.True(config.Rules.KeepInventoryOnDeath);
        Assert.Contains("ship-weapons", applied);
        Assert.Contains("keep-ship", applied);
        Assert.Contains("keep-inventory", applied);
    }

    [Fact]
    public void ApplyCommandLine_OverridesStructureTemplateOptions()
    {
        var config = new ServerConfig();
        var applied = config.ApplyCommandLine(new[]
        {
            "--station-templates", "Frequent",
            "--settlement-templates", "Off",
            "--structure-packs", "vanilla,mybuilds",
            "--usercontent", @"C:\sp\usercontent",
        });

        Assert.Equal(Frequency.Frequent, config.World.StationTemplateUse);
        Assert.Equal(Frequency.Off, config.World.SettlementTemplateUse);
        Assert.Equal(new[] { "vanilla", "mybuilds" }, config.World.EnabledStructurePacks);
        Assert.Equal(@"C:\sp\usercontent", config.UserContentDir);
        Assert.Contains("station-templates", applied);
        Assert.Contains("structure-packs", applied);
        Assert.Contains("usercontent", applied);
    }

    [Fact]
    public void ApplyCommandLine_IgnoresUnknownKeysAndKeepsDefaults()
    {
        var config = new ServerConfig();
        int defaultPort = config.GameplayPort;

        var applied = config.ApplyCommandLine(new[] { "--unknown", "x", "--also-unknown", "y" });

        Assert.Empty(applied);
        Assert.Equal(defaultPort, config.GameplayPort);
    }

    [Fact]
    public void ApplyCommandLine_HandlesMissingValueAndNullSafely()
    {
        var config = new ServerConfig();
        int defaultPort = config.GameplayPort;

        // Trailing flag with no value must not throw or change anything.
        var applied = config.ApplyCommandLine(new[] { "--port" });
        Assert.Empty(applied);
        Assert.Equal(defaultPort, config.GameplayPort);

        Assert.Empty(config.ApplyCommandLine(null));
    }

    [Fact]
    public void ApplyCommandLine_RejectsNonNumericPort()
    {
        var config = new ServerConfig();
        int defaultPort = config.GameplayPort;

        var applied = config.ApplyCommandLine(new[] { "--port", "notaport" });

        Assert.Empty(applied);
        Assert.Equal(defaultPort, config.GameplayPort);
    }
}
