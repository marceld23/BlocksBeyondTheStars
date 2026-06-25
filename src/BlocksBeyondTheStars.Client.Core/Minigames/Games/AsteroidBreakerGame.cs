using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Minigames.Games
{
    /// <summary>Port of <c>web/minigames/asteroid_breaker</c>: a breakout — bounce the core off the platform to
    /// shatter every asteroid, three cores before it ends. The ball steps per frame (not dt-scaled), matching the
    /// web loop.</summary>
    public sealed class AsteroidBreakerGame : IMinigame
    {
        private const int W = 600, H = 460;
        private const int Cols = 10, Rows = 6, Pad = 6, BH = 18, Top = 46;
        private const float BW = (W - Pad * (Cols + 1)) / (float)Cols;

        private static readonly Rgba Bg = Rgba.Rgb(7, 13, 22);
        private static readonly Rgba PadCol = Rgba.Rgb(70, 214, 255);
        private static readonly Rgba Ball = Rgba.Rgb(124, 255, 176);
        private static readonly Rgba HintCol = Rgba.Rgb(207, 233, 245);

        public string Key => "asteroid_breaker";
        public LocText Title => new LocText("Asteroid Breaker", "Asteroid Breaker");
        public LocText Desc => new LocText(
            "Bounce the core off your platform to shatter every asteroid. Clear the field before your cores run out.",
            "Lenke den Kern mit der Plattform und zertrümmere jeden Asteroiden. Räume das Feld, bevor die Kerne ausgehen.");
        public LocText Hint => new LocText("Mouse / ← → move the platform · Space / click to serve", "Maus / ← → bewegen · Leertaste / Klick zum Abschuss");
        public IReadOnlyList<LocText> Help { get; } = new[]
        {
            new LocText("Move the platform with the mouse or ← →", "Plattform mit Maus oder ← → bewegen"),
            new LocText("The core serves itself after a moment — or press Space / click to launch it immediately", "Der Kern startet nach kurzem von selbst — oder Leertaste / Klick für sofortigen Abschuss"),
        };
        public int Difficulty => 2;

        public MinigameController Create(MinigameApi api)
        {
            var canvas = api.Canvas(W, H);

            float padX = 0f;
            const float padW = 100f, padH = 14f, padY = H - 26f;
            float bx = 0f, by = 0f, bvx = 0f, bvy = 0f;
            const float br = 7f, bsp = 5.2f;
            var bricks = new List<Brick>();
            int score = 0, lives = 3;
            bool stuck = true, done = false;
            int autoId = 0;

            void Launch()
            {
                if (!stuck)
                {
                    return;
                }

                stuck = false;
                bvx = bsp * 0.4f * (api.Rand(2) != 0 ? 1f : -1f);
                bvy = -bsp;
            }

            void ResetBall()
            {
                stuck = true;
                bx = padX + padW / 2f;
                by = padY - 9f;
                bvx = 0f;
                bvy = 0f;
                if (autoId != 0)
                {
                    api.StopTimer(autoId);
                }

                autoId = api.After(Launch, 1f);
            }

            void Reset()
            {
                padX = W / 2f - 50f;
                score = 0;
                lives = 3;
                done = false;
                bricks.Clear();
                for (int r = 0; r < Rows; r++)
                {
                    for (int cc = 0; cc < Cols; cc++)
                    {
                        bricks.Add(new Brick { X = Pad + cc * (BW + Pad), Y = Top + r * (BH + Pad), Row = r, Alive = true });
                    }
                }

                ResetBall();
                api.Hud("score", 0);
                api.Hud("cores", lives);
            }

            api.Bind(MinigameAction.Primary, Launch);
            api.Bind(MinigameAction.Confirm, Launch);
            api.Pointer(p =>
            {
                if (p.Phase == PointerPhase.Move || p.Phase == PointerPhase.Down)
                {
                    padX = System.Math.Max(0f, System.Math.Min(W - padW, p.X - padW / 2f));
                }

                if (p.Phase == PointerPhase.Down)
                {
                    Launch();
                }
            });

            api.Loop(dt =>
            {
                if (done)
                {
                    return;
                }

                if (api.Held(MinigameAction.Left))
                {
                    padX = System.Math.Max(0f, padX - 8f);
                }

                if (api.Held(MinigameAction.Right))
                {
                    padX = System.Math.Min(W - padW, padX + 8f);
                }

                if (stuck)
                {
                    bx = padX + padW / 2f;
                    by = padY - 9f;
                }
                else
                {
                    bx += bvx;
                    by += bvy;
                    if (bx < br || bx > W - br)
                    {
                        bvx = -bvx;
                    }

                    if (by < br)
                    {
                        bvy = -bvy;
                    }

                    if (by > H + 18)
                    {
                        lives--;
                        api.Hud("cores", lives);
                        if (lives <= 0)
                        {
                            done = true;
                            api.Fail(score);
                            return;
                        }

                        ResetBall();
                    }

                    if (bvy > 0 && by + br >= padY && bx >= padX && bx <= padX + padW)
                    {
                        float hit = (bx - (padX + padW / 2f)) / (padW / 2f);
                        bvx = (float)System.Math.Sin(hit * 1.05) * bsp;
                        bvy = -(float)System.Math.Abs(System.Math.Cos(hit * 1.05) * bsp);
                    }

                    for (int i = 0; i < bricks.Count; i++)
                    {
                        var b = bricks[i];
                        if (!b.Alive)
                        {
                            continue;
                        }

                        if (bx > b.X && bx < b.X + BW && by > b.Y && by < b.Y + BH)
                        {
                            b.Alive = false;
                            bricks[i] = b;
                            bvy = -bvy;
                            score += (Rows - b.Row) * 5;
                            api.Hud("score", score);
                            break;
                        }
                    }

                    if (!AnyAlive(bricks))
                    {
                        done = true;
                        int rating = lives >= 3 ? 3 : lives == 2 ? 2 : 1;
                        api.Complete(score, rating);
                        return;
                    }
                }

                Draw();
            });

            void Draw()
            {
                canvas.Clear(Bg);
                foreach (var b in bricks)
                {
                    if (!b.Alive)
                    {
                        continue;
                    }

                    canvas.FillRect((int)b.X, (int)b.Y, (int)BW, BH, Rgba.Hsl(190 + b.Row * 7, 0.80, (62 - b.Row * 5) / 100.0));
                }

                canvas.FillRect((int)padX, (int)padY, (int)padW, (int)padH, PadCol);
                canvas.FillCircle((int)bx, (int)by, (int)br, Ball);
                if (stuck)
                {
                    canvas.DrawTextCentered(W / 2, H - 64, api.German ? "AUTOSTART  ODER LEERTASTE" : "AUTOLAUNCH  OR SPACE TO SERVE", HintCol);
                }
            }

            return new MinigameController { Start = Reset };
        }

        private static bool AnyAlive(List<Brick> bricks)
        {
            foreach (var b in bricks)
            {
                if (b.Alive)
                {
                    return true;
                }
            }

            return false;
        }

        private struct Brick
        {
            public float X, Y;
            public int Row;
            public bool Alive;
        }
    }
}
