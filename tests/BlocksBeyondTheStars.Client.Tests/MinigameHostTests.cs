using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Minigames;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Headless tests for the native-Arcade framework (Stream D Phase 2) — the pure C# port of framework.js. The
/// shell state machine, paused-aware clock/timers, input dispatch, HUD model and the score→reward rule all run
/// without Unity, so they are asserted exactly here. A tiny in-test game drives the host.
/// </summary>
public sealed class MinigameHostTests
{
    /// <summary>A configurable probe game: counts loop ticks, exposes a Confirm press handler, and finishes on
    /// command, so the tests can drive every shell transition.</summary>
    private sealed class ProbeGame : IMinigame
    {
        public int StartCount;
        public int PauseCount;
        public int ResumeCount;
        public int Ticks;
        public int Confirms;
        public float LastNow;
        private MinigameApi _api = null!;

        public string Key => "probe";
        public LocText Title => new LocText("Probe", "Probe");
        public LocText Desc => new LocText("d", "d");
        public LocText Hint => new LocText("h", "h");
        public IReadOnlyList<LocText> Help { get; } = new[] { new LocText("a", "a") };
        public int Difficulty => 1;

        public MinigameController Create(MinigameApi api)
        {
            _api = api;
            api.Canvas(32, 16);
            api.Bind(MinigameAction.Confirm, () => Confirms++);
            api.Loop(dt => { Ticks++; LastNow = api.Now(); });
            return new MinigameController
            {
                Start = () => StartCount++,
                Pause = () => PauseCount++,
                Resume = () => ResumeCount++,
            };
        }

        public void FinishWin(int score, int rating) => _api.Complete(score, rating);
        public void FinishLose(int score) => _api.Fail(score);
    }

    [Fact]
    public void StartsOnStartScreen_AndStartGameBeginsPlaying()
    {
        var g = new ProbeGame();
        var host = new MinigameHost(g, seed: 1);
        Assert.Equal(MinigameState.Start, host.State);

        host.StartGame();
        Assert.Equal(MinigameState.Playing, host.State);
        Assert.Equal(1, g.StartCount);
        Assert.NotNull(host.Api.Surface);
    }

    [Fact]
    public void Tick_RunsLoop_AndAdvancesClock_OnlyWhilePlaying()
    {
        var g = new ProbeGame();
        var host = new MinigameHost(g, seed: 1);

        host.Tick(0.1f); // ignored before start
        Assert.Equal(0, g.Ticks);

        host.StartGame();
        host.Tick(0.02f);
        host.Tick(0.02f);
        Assert.Equal(2, g.Ticks);
        Assert.InRange(g.LastNow, 0.039f, 0.041f);
    }

    [Fact]
    public void Tick_ClampsLargeDt()
    {
        var g = new ProbeGame();
        var host = new MinigameHost(g, seed: 1);
        host.StartGame();
        host.Tick(10f); // a stalled frame must not teleport the clock
        Assert.InRange(g.LastNow, 0.049f, 0.051f);
    }

    [Fact]
    public void Pause_FreezesClockAndLoop_ResumeContinues()
    {
        var g = new ProbeGame();
        var host = new MinigameHost(g, seed: 1);
        host.StartGame();
        host.Tick(0.05f);

        host.Pause();
        Assert.Equal(MinigameState.Paused, host.State);
        Assert.Equal(1, g.PauseCount);

        host.Tick(0.05f); // no-op while paused
        Assert.Equal(1, g.Ticks);

        host.Resume();
        Assert.Equal(1, g.ResumeCount);
        host.Tick(0.05f);
        Assert.Equal(2, g.Ticks);
        Assert.InRange(g.LastNow, 0.099f, 0.101f); // paused span did not count
    }

    [Fact]
    public void PausePress_Toggles()
    {
        var g = new ProbeGame();
        var host = new MinigameHost(g, seed: 1);
        host.StartGame();
        host.Press(MinigameAction.Pause);
        Assert.Equal(MinigameState.Paused, host.State);
        host.Press(MinigameAction.Pause);
        Assert.Equal(MinigameState.Playing, host.State);
    }

    [Fact]
    public void HelpPress_OpensAndReturnsToStartOrPause()
    {
        var g = new ProbeGame();
        var host = new MinigameHost(g, seed: 1);

        host.Press(MinigameAction.Help); // from start
        Assert.Equal(MinigameState.Help, host.State);
        host.CloseHelp();
        Assert.Equal(MinigameState.Start, host.State);

        host.StartGame();
        host.ShowHelp(); // from playing → pauses, returns to pause
        Assert.Equal(MinigameState.Help, host.State);
        host.CloseHelp();
        Assert.Equal(MinigameState.Paused, host.State);
    }

    [Fact]
    public void Press_DispatchesBoundHandler_AndHeldLatches()
    {
        var g = new ProbeGame();
        var host = new MinigameHost(g, seed: 1);
        host.StartGame();

        host.Press(MinigameAction.Confirm);
        host.Press(MinigameAction.Confirm); // still held → no second fire
        Assert.Equal(1, g.Confirms);
        Assert.True(host.Api.Held(MinigameAction.Confirm));

        host.Release(MinigameAction.Confirm);
        host.Press(MinigameAction.Confirm);
        Assert.Equal(2, g.Confirms);
    }

    [Fact]
    public void Timers_FireOnGameClock_AndPauseAware()
    {
        var g = new ProbeGame();
        var host = new MinigameHost(g, seed: 1);
        int fired = 0, ticks = 0;
        var local = new IMinigameStub(api =>
        {
            api.After(() => fired++, 0.1f);
            api.Every(() => ticks++, 0.05f);
        });
        var h2 = new MinigameHost(local, seed: 1);
        h2.StartGame();

        h2.Tick(0.04f);
        Assert.Equal(0, fired);
        Assert.Equal(0, ticks);

        h2.Tick(0.02f); // now=0.06 → one Every fired
        Assert.Equal(1, ticks);
        Assert.Equal(0, fired);

        h2.Pause();
        h2.Tick(1f); // frozen
        Assert.Equal(1, ticks);

        h2.Resume();
        h2.Tick(0.05f); // now=0.11 → After fires, Every fires again
        Assert.Equal(1, fired);
        Assert.Equal(2, ticks);
    }

    [Fact]
    public void Complete_ComputesRating_NewBest_AndKnowledge()
    {
        var g = new ProbeGame();
        var host = new MinigameHost(g, best: 50, seed: 1);
        host.StartGame();
        g.FinishWin(120, 3);

        Assert.Equal(MinigameState.Result, host.State);
        Assert.Equal(120, host.Result.Score);
        Assert.Equal(3, host.Result.Rating);
        Assert.True(host.Result.Completed);
        Assert.True(host.Result.IsNewBest);
        Assert.Equal(15, host.Result.Knowledge); // rating 3 × 5
        Assert.Equal(120, host.Best);
        Assert.Equal(50, host.PreviousBest);
    }

    [Fact]
    public void Complete_BelowBest_GrantsNoKnowledge()
    {
        var g = new ProbeGame();
        var host = new MinigameHost(g, best: 200, seed: 1);
        host.StartGame();
        g.FinishWin(120, 2);
        Assert.False(host.Result.IsNewBest);
        Assert.Equal(0, host.Result.Knowledge);
        Assert.Equal(200, host.Best);
    }

    [Fact]
    public void Fail_HasRatingZero_AndNoKnowledge_EvenAboveBest()
    {
        var g = new ProbeGame();
        var host = new MinigameHost(g, best: 0, seed: 1);
        host.StartGame();
        g.FinishLose(90);
        Assert.False(host.Result.Completed);
        Assert.Equal(0, host.Result.Rating);
        Assert.Equal(0, host.Result.Knowledge);
    }

    [Fact]
    public void OnResult_RaisedOnce()
    {
        var g = new ProbeGame();
        var host = new MinigameHost(g, seed: 1);
        int raised = 0;
        host.OnResult += _ => raised++;
        host.StartGame();
        g.FinishWin(10, 1);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Restart_ClearsRoundState()
    {
        var g = new ProbeGame();
        var host = new MinigameHost(g, seed: 1);
        host.StartGame();
        host.Tick(0.02f);
        Assert.Equal(1, g.Ticks);

        host.StartGame(); // replay
        Assert.Equal(2, g.StartCount);
        host.Tick(0.02f);
        Assert.InRange(g.LastNow, 0.019f, 0.021f); // clock reset
    }

    /// <summary>Inline game whose setup is a delegate — lets a test wire timers/handlers without a named class.</summary>
    private sealed class IMinigameStub : IMinigame
    {
        private readonly System.Action<MinigameApi> _setup;
        public IMinigameStub(System.Action<MinigameApi> setup) => _setup = setup;
        public string Key => "stub";
        public LocText Title => new LocText("s", "s");
        public LocText Desc => new LocText("s", "s");
        public LocText Hint => new LocText("s", "s");
        public IReadOnlyList<LocText> Help { get; } = System.Array.Empty<LocText>();
        public int Difficulty => 1;
        public MinigameController Create(MinigameApi api) { _setup(api); return new MinigameController(); }
    }
}
