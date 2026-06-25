using BlocksBeyondTheStars.Client.Minigames;
using BlocksBeyondTheStars.Client.Minigames.Games;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Headless behaviour tests for the first two ported native games (data_fishing, comet_courier). They exercise
/// the full host+game loop deterministically (fixed RNG seed): the surface is created, drawing happens, HUD is
/// populated, and the run reaches a result without an exception — proving the port pattern end to end.
/// </summary>
public sealed class MinigamePortTests
{
    private static bool DrewSomething(Canvas2D? c)
    {
        Assert.NotNull(c);
        for (int i = 0; i < c.Rgba.Length; i += 4)
        {
            if (c.Rgba[i] != 0 || c.Rgba[i + 1] != 0 || c.Rgba[i + 2] != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Run a game with no player input until it finishes (or the time budget runs out), at a fixed 60 Hz.</summary>
    private static MinigameHost RunToEnd(IMinigame game, int seed, float maxSeconds = 90f)
    {
        var host = new MinigameHost(game, seed: seed);
        host.StartGame();
        Assert.NotNull(host.Api.Surface);

        int steps = (int)(maxSeconds * 60);
        for (int i = 0; i < steps && host.State == MinigameState.Playing; i++)
        {
            host.Tick(1f / 60f);
        }

        return host;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void DataFishing_RunsAndReachesAResult(int seed)
    {
        var host = RunToEnd(new DataFishingGame(), seed);
        Assert.Equal(MinigameState.Result, host.State);
        Assert.True(DrewSomething(host.Api.Surface));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void CometCourier_RunsAndReachesAResult(int seed)
    {
        var host = RunToEnd(new CometCourierGame(), seed);
        Assert.Equal(MinigameState.Result, host.State);
        Assert.True(DrewSomething(host.Api.Surface));
    }

    [Fact]
    public void DataFishing_HasExpectedSurfaceAndHud()
    {
        var host = new MinigameHost(new DataFishingGame(), seed: 1);
        host.StartGame();
        Assert.Equal(640, host.Api.Surface!.Width);
        Assert.Equal(440, host.Api.Surface!.Height);
        Assert.Contains(host.HudFields, f => f.key == "integrity");
        Assert.Contains(host.HudFields, f => f.key == "data");
    }

    [Fact]
    public void CometCourier_PointerSteersAndStaysInBounds()
    {
        var host = new MinigameHost(new CometCourierGame(), seed: 1);
        host.StartGame();
        // Pointer way past the bottom edge must clamp, not throw, and keep drawing valid pixels.
        host.Pointer(PointerPhase.Move, 360f, 9999f);
        host.Tick(1f / 60f);
        Assert.True(DrewSomething(host.Api.Surface));
    }

    [Fact]
    public void Game_Metadata_IsBilingual()
    {
        var g = new DataFishingGame();
        Assert.Equal("data_fishing", g.Key);
        Assert.NotEqual(g.Title.Get(false), string.Empty);
        Assert.NotEqual(g.Title.Get(true), string.Empty);
        Assert.NotEmpty(g.Help);
    }

    // asteroid_breaker auto-launches and, with no paddle input, drains its cores to a fail; blockfall's gravity
    // tops the stack out — both reach a result with no input.
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void AsteroidBreaker_RunsToResult(int seed)
    {
        var host = RunToEnd(new AsteroidBreakerGame(), seed, maxSeconds: 180f);
        Assert.Equal(MinigameState.Result, host.State);
        Assert.True(DrewSomething(host.Api.Surface));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void Blockfall_RunsToResult(int seed)
    {
        var host = RunToEnd(new BlockfallGame(), seed, maxSeconds: 300f);
        Assert.Equal(MinigameState.Result, host.State);
        Assert.True(DrewSomething(host.Api.Surface));
    }

    [Fact]
    public void DockingSim_HoldingThrust_RamsWalls_AndFailsOutAttempts()
    {
        var host = new MinigameHost(new DockingSimGame(), seed: 3);
        host.StartGame();
        host.Press(MinigameAction.Up); // continuous thrust → drifts into a wall, three times
        for (int i = 0; i < 60 * 60 && host.State == MinigameState.Playing; i++)
        {
            host.Tick(1f / 60f);
        }

        Assert.Equal(MinigameState.Result, host.State);
        Assert.False(host.Result.Completed); // ramming walls can't dock
    }

    [Fact]
    public void MicroMiner_DiggingDrainsEnergyToAResult()
    {
        var host = new MinigameHost(new MicroMinerGame(), seed: 5);
        host.StartGame();
        // Sweep up and down the shaft, pressing each step so every move costs energy until it ends.
        for (int trip = 0; trip < 200 && host.State == MinigameState.Playing; trip++)
        {
            var dir = trip % 2 == 0 ? MinigameAction.Down : MinigameAction.Up;
            for (int step = 0; step < 11 && host.State == MinigameState.Playing; step++)
            {
                host.Press(dir);
                host.Release(dir);
            }
        }

        Assert.Equal(MinigameState.Result, host.State);
        Assert.True(DrewSomething(host.Api.Surface));
    }
}
