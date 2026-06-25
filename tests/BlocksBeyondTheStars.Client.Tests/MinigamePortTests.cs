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

    private static int HudInt(MinigameHost host, string key)
    {
        foreach (var f in host.HudFields)
        {
            if (f.key == key)
            {
                // Values may be "n" or "n/8" — take the leading integer.
                int slash = f.value.IndexOf('/');
                string s = slash >= 0 ? f.value.Substring(0, slash) : f.value;
                return int.TryParse(s, out int v) ? v : 0;
            }
        }

        return 0;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FlowPuzzle_ClickRotatesTiles_AndRunsClean(bool circuit)
    {
        IMinigame game = circuit ? new CircuitWeaverGame() : new OxygenLoopGame();
        var host = new MinigameHost(game, seed: 2);
        host.StartGame();
        Assert.NotNull(host.Api.Surface);

        // Click the top-left tile a few times — each rotation bumps the counter; nothing throws.
        for (int i = 1; i <= 4; i++)
        {
            host.Pointer(PointerPhase.Down, 10f, 10f);
            host.Tick(1f / 60f);
            Assert.Equal(i, HudInt(host, "rotations"));
        }

        Assert.True(DrewSomething(host.Api.Surface));
    }

    [Fact]
    public void LaserGrid_ClicksAllCells_RunsClean_AndCountsMirrorFlips()
    {
        var host = new MinigameHost(new LaserGridGame(), seed: 4);
        host.StartGame();
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                host.Pointer(PointerPhase.Down, x * 56 + 28f, y * 56 + 28f);
                host.Tick(1f / 60f);
            }
        }

        Assert.True(HudInt(host, "moves") >= 1); // at least one cell held a mirror
        Assert.True(DrewSomething(host.Api.Surface));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    public void StarMemory_BruteForceSolverWinsTheBoard(int seed)
    {
        var host = new MinigameHost(new StarMemoryGame(), seed: seed);
        host.StartGame();

        const int n = 16;
        var done = new bool[n];

        (int x, int y) Pos(int i) => (12 + (i % 4) * (96 + 12) + 48, 12 + (i / 4) * (96 + 12) + 48);

        void Click(int i)
        {
            var (x, y) = Pos(i);
            host.Pointer(PointerPhase.Down, x, y);
        }

        for (int i = 0; i < n && host.State == MinigameState.Playing; i++)
        {
            if (done[i])
            {
                continue;
            }

            for (int j = i + 1; j < n && host.State == MinigameState.Playing; j++)
            {
                if (done[j])
                {
                    continue;
                }

                int before = HudInt(host, "pairs");
                Click(i);
                Click(j);
                // Resolve: a mismatch flips back after 0.65s game-time.
                for (int t = 0; t < 60 && host.State == MinigameState.Playing; t++)
                {
                    host.Tick(1f / 60f);
                }

                if (HudInt(host, "pairs") > before)
                {
                    done[i] = done[j] = true;
                    break;
                }
            }
        }

        Assert.Equal(MinigameState.Result, host.State);
        Assert.True(host.Result.Completed);
    }

    private static void TickSeconds(MinigameHost h, float seconds)
    {
        int n = (int)(seconds * 60);
        for (int i = 0; i < n && h.State == MinigameState.Playing; i++)
        {
            h.Tick(1f / 60f);
        }
    }

    [Fact]
    public void SignalTuner_TimesOutToAResult()
    {
        var host = new MinigameHost(new SignalTunerGame(), seed: 1);
        host.StartGame();
        TickSeconds(host, 130f); // no input → quality stays low → decode never fills → fail at 120s
        Assert.Equal(MinigameState.Result, host.State);
    }

    [Fact]
    public void ReactorBalance_ReachesAResult()
    {
        var host = new MinigameHost(new ReactorBalanceGame(), seed: 1);
        host.StartGame();
        TickSeconds(host, 70f); // drift trips a meltdown, or survives 60s — either way a result
        Assert.Equal(MinigameState.Result, host.State);
    }

    [Fact]
    public void CargoSorter_SurvivesTheShift_NoInput()
    {
        var host = new MinigameHost(new CargoSorterGame(), seed: 1);
        host.StartGame();
        TickSeconds(host, 91f);
        Assert.Equal(MinigameState.Result, host.State);
        Assert.True(host.Result.Completed); // no wrong assignments → survives to 90s
    }

    [Fact]
    public void OrbitSlingshot_MissingShotsExhaustAttempts()
    {
        var host = new MinigameHost(new OrbitSlingshotGame(), seed: 6);
        host.StartGame();
        for (int a = 0; a < 8 && host.State == MinigameState.Playing; a++)
        {
            host.Pointer(PointerPhase.Down, 70f, 230f);
            host.Pointer(PointerPhase.Move, 70f, 450f);
            host.Pointer(PointerPhase.Up, 70f, 450f); // aim straight down → flies off-screen
            TickSeconds(host, 12f);
        }

        Assert.Equal(MinigameState.Result, host.State);
        Assert.False(host.Result.Completed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    public void VoidSolitaire_BruteForceSolverClearsBoard(int seed)
    {
        var host = new MinigameHost(new VoidSolitaireGame(), seed: seed);
        host.StartGame();

        const int n = 24;
        var done = new bool[n];
        (int x, int y) Pos(int i) => (12 + (i % 6) * (84 + 10) + 42, 12 + (i / 6) * (104 + 10) + 52);
        void Click(int i) { var (x, y) = Pos(i); host.Pointer(PointerPhase.Down, x, y); }

        for (int i = 0; i < n && host.State == MinigameState.Playing; i++)
        {
            if (done[i])
            {
                continue;
            }

            for (int j = i + 1; j < n && host.State == MinigameState.Playing; j++)
            {
                if (done[j])
                {
                    continue;
                }

                int before = HudInt(host, "left");
                Click(i);
                Click(j);
                for (int t = 0; t < 45 && host.State == MinigameState.Playing; t++)
                {
                    host.Tick(1f / 60f);
                }

                if (HudInt(host, "left") < before)
                {
                    done[i] = done[j] = true;
                    break;
                }
            }
        }

        Assert.Equal(MinigameState.Result, host.State);
        Assert.True(host.Result.Completed);
    }

    [Fact]
    public void GlyphDecoder_StudyThenAnswerEachQuestion_Completes()
    {
        var host = new MinigameHost(new GlyphDecoderGame(), seed: 2);
        host.StartGame();
        TickSeconds(host, 5.2f); // wait out the study countdown

        // Answer all four questions (clicking the first option each time — right or wrong, it advances).
        int optX = (560 - (2 * 250 + 16)) / 2 + 125, optY = 230 + 23;
        for (int q = 0; q < 4 && host.State == MinigameState.Playing; q++)
        {
            host.Pointer(PointerPhase.Down, optX, optY);
            TickSeconds(host, 0.7f);
        }

        Assert.Equal(MinigameState.Result, host.State);
        Assert.True(host.Result.Completed);
    }

    [Fact]
    public void BlueprintScramble_ClickRotates_RunsClean()
    {
        var host = new MinigameHost(new BlueprintScrambleGame(), seed: 3);
        host.StartGame();
        for (int i = 1; i <= 5; i++)
        {
            host.Pointer(PointerPhase.Down, 10f, 10f);
            host.Tick(1f / 60f);
            Assert.Equal(i, HudInt(host, "moves"));
        }

        Assert.True(DrewSomething(host.Api.Surface));
    }

    [Fact]
    public void DroneRescue_PressingMoves_RunsClean()
    {
        var host = new MinigameHost(new DroneRescueGame(), seed: 3);
        host.StartGame();
        var dirs = new[] { MinigameAction.Right, MinigameAction.Down, MinigameAction.Left, MinigameAction.Up };
        for (int i = 0; i < 80 && host.State == MinigameState.Playing; i++)
        {
            var d = dirs[i % 4];
            host.Press(d);
            host.Release(d);
        }

        Assert.True(DrewSomething(host.Api.Surface));
    }

    [Fact]
    public void PlanetScanner_ClickingScans_ReachesAResult()
    {
        var host = new MinigameHost(new PlanetScannerGame(), seed: 3);
        host.StartGame();
        for (int y = 40; y <= 420 && host.State == MinigameState.Playing; y += 16)
        {
            for (int x = 40; x <= 420 && host.State == MinigameState.Playing; x += 16)
            {
                host.Pointer(PointerPhase.Down, x, y);
                host.Tick(1f / 60f);
            }
        }

        Assert.Equal(MinigameState.Result, host.State); // finds all five or runs the scans dry
    }

    [Fact]
    public void NanobotRepair_ClickingNodes_RunsClean()
    {
        var host = new MinigameHost(new NanobotRepairGame(), seed: 3);
        host.StartGame();
        for (int y = 40; y <= 420 && host.State == MinigameState.Playing; y += 18)
        {
            for (int x = 40; x <= 640 && host.State == MinigameState.Playing; x += 18)
            {
                host.Pointer(PointerPhase.Down, x, y);
                host.Tick(1f / 60f);
            }
        }

        Assert.True(DrewSomething(host.Api.Surface));
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
