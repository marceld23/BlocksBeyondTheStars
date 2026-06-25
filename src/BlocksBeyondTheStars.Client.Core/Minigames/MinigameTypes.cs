using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Minigames
{
    /// <summary>The abstract minigame inputs (keyboard now, gamepad later). The Unity host maps physical keys
    /// onto these; a game only ever asks for the action, never a <c>KeyCode</c>. Mirrors the web framework's
    /// KEYMAP (Left/Right/Up/Down/Confirm/Cancel/Pause/Restart/Help/Primary/Secondary).</summary>
    public enum MinigameAction
    {
        Left,
        Right,
        Up,
        Down,
        Confirm,
        Cancel,
        Primary,
        Secondary,
        Pause,
        Restart,
        Help,
    }

    /// <summary>A pointer (mouse/touch) phase, in the canvas' own pixel space.</summary>
    public enum PointerPhase
    {
        Down,
        Move,
        Up,
    }

    public readonly struct PointerInput
    {
        public readonly PointerPhase Phase;
        public readonly float X;
        public readonly float Y;

        public PointerInput(PointerPhase phase, float x, float y)
        {
            Phase = phase;
            X = x;
            Y = y;
        }
    }

    /// <summary>The shell state the host is in. The host drives the start/help/pause/result overlays off this,
    /// exactly like the web framework's four overlays.</summary>
    public enum MinigameState
    {
        Start,
        Help,
        Playing,
        Paused,
        Result,
    }

    /// <summary>A bilingual string carried by a game's own metadata so each game stays one self-contained class
    /// (the web games declared <c>{en,de}</c> inline). The logic core never localizes; the Unity host calls
    /// <see cref="Get"/> when it draws the title/help.</summary>
    public readonly struct LocText
    {
        public readonly string En;
        public readonly string De;

        public LocText(string en, string de)
        {
            En = en;
            De = de;
        }

        public string Get(bool german) => german && !string.IsNullOrEmpty(De) ? De : (En ?? string.Empty);

        public bool IsEmpty => string.IsNullOrEmpty(En) && string.IsNullOrEmpty(De);
    }

    /// <summary>The outcome of a finished run, computed by the host the moment a game calls
    /// <c>api.Complete</c>/<c>api.Fail</c>. <see cref="IsNewBest"/> and <see cref="Knowledge"/> mirror the web
    /// reward rule: knowledge is granted ONLY when a completed run beats the player's previous best, +5/+10/+15
    /// by rating.</summary>
    public readonly struct MinigameResult
    {
        public readonly int Score;
        public readonly int Rating;     // 0 on fail, else 1..3
        public readonly bool Completed;
        public readonly bool IsNewBest;
        public readonly int Knowledge;  // points to grant (0 unless a completed new best)

        public MinigameResult(int score, int rating, bool completed, bool isNewBest)
        {
            Score = score;
            Rating = rating;
            Completed = completed;
            IsNewBest = isNewBest;
            Knowledge = completed && isNewBest ? rating * 5 : 0;
        }
    }

    /// <summary>A game's lifecycle hooks, returned from <see cref="IMinigame.Create"/>. All optional: a game that
    /// sets everything up in <c>Create</c> can leave them null. The host calls <see cref="Start"/> at the moment
    /// of (re)play, and <see cref="Pause"/>/<see cref="Resume"/> when the shell pauses.</summary>
    public sealed class MinigameController
    {
        public System.Action? Start;
        public System.Action? Pause;
        public System.Action? Resume;
    }

    /// <summary>One minigame. A game declares its own (bilingual) presentation and difficulty, and in
    /// <see cref="Create"/> wires its mechanic onto the supplied <see cref="MinigameApi"/> (canvas, input, loop,
    /// timers) — the C# equivalent of the web <c>create(api)</c>. <see cref="Create"/> is re-invoked on every
    /// (re)start so a fresh closure of game state is the natural pattern, just like the web games.</summary>
    public interface IMinigame
    {
        string Key { get; }
        LocText Title { get; }
        LocText Desc { get; }
        LocText Hint { get; }
        IReadOnlyList<LocText> Help { get; }
        int Difficulty { get; }   // 1..5, for the star row on the start screen

        MinigameController Create(MinigameApi api);
    }
}
