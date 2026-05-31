using Spacecraft.Shared.Configuration;
using Xunit;

namespace Spacecraft.Tests;

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
        });

        Assert.Equal(31550, config.GameplayPort);
        Assert.Equal("Singleplayer", config.ServerName);
        Assert.Equal("singleplayer", config.WorldName);
        Assert.Equal(@"C:\sp\saves", config.SavesRoot);
        Assert.Equal(@"C:\sp\data", config.DataDir);
        Assert.Equal(1, config.MaxPlayers);
        Assert.Contains("port", applied);
        Assert.Contains("max-players", applied);
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
