using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Minigames.Games
{
    /// <summary>Port of <c>web/minigames/docking_sim</c>: inertial piloting — thrust + rotate to glide gently into
    /// the docking ring; a hard contact (or a wall/obstacle) costs one of three attempts.</summary>
    public sealed class DockingSimGame : IMinigame
    {
        private const int W = 700, H = 440;

        private static readonly Rgba Bg = Rgba.Rgb(7, 13, 22);
        private static readonly Rgba RingCol = Rgba.Rgb(124, 255, 176);
        private static readonly Rgba RingFill = new Rgba(124, 255, 176, 30);
        private static readonly Rgba ObsCol = Rgba.Rgb(255, 107, 107);
        private static readonly Rgba ObsFill = new Rgba(255, 107, 107, 64);
        private static readonly Rgba ShipCol = Rgba.Rgb(70, 214, 255);

        public string Key => "docking_sim";
        public LocText Title => new LocText("Docking Simulator", "Andock-Simulator");
        public LocText Desc => new LocText(
            "Nudge the ship onto the docking ring — slowly. Too much speed on contact and you bounce off. You have three attempts.",
            "Manövriere das Schiff sachte in den Andockring. Zu viel Tempo beim Kontakt und du prallst ab. Du hast drei Versuche.");
        public LocText Hint => new LocText("↑ thrust · ← → rotate · drift like in space", "↑ Schub · ← → drehen · Trägheit wie im All");
        public IReadOnlyList<LocText> Help { get; } = new[]
        {
            new LocText("↑ thrust in the facing direction; ← → rotate", "↑ Schub in Blickrichtung; ← → drehen"),
            new LocText("Glide into the green ring slowly — a hard hit costs an attempt", "Gleite langsam in den grünen Ring — ein harter Treffer kostet einen Versuch"),
        };
        public int Difficulty => 4;

        public MinigameController Create(MinigameApi api)
        {
            var canvas = api.Canvas(W, H);

            float sx = 0, sy = 0, svx = 0, svy = 0, sa = 0;
            float dockX = 0, dockY = 0;
            const float dockR = 30f;
            int attempts = 3;
            bool done = false;
            var obstacles = new List<(float x, float y, float r)>();

            void Respawn()
            {
                sx = 80;
                sy = H / 2f;
                svx = svy = sa = 0;
            }

            void Reset()
            {
                Respawn();
                dockX = W - 90f;
                dockY = 80f + api.Rand(H - 160);
                attempts = 3;
                done = false;
                obstacles.Clear();
                int n = api.Rand(3);
                for (int i = 0; i < n; i++)
                {
                    obstacles.Add((220 + api.Rand(300), 40 + api.Rand(H - 80), 22));
                }

                api.Hud("attempts", attempts);
                api.Hud("speed", "0");
            }

            void Lose()
            {
                attempts--;
                api.Hud("attempts", attempts);
                if (attempts <= 0)
                {
                    done = true;
                    api.Fail(0);
                }
                else
                {
                    Respawn();
                }
            }

            api.Loop(dt =>
            {
                if (done)
                {
                    return;
                }

                if (api.Held(MinigameAction.Left))
                {
                    sa -= 2.4f * dt;
                }

                if (api.Held(MinigameAction.Right))
                {
                    sa += 2.4f * dt;
                }

                if (api.Held(MinigameAction.Up))
                {
                    svx += (float)Math.Cos(sa) * 70f * dt;
                    svy += (float)Math.Sin(sa) * 70f * dt;
                }

                sx += svx * dt;
                sy += svy * dt;
                float sp = (float)Math.Sqrt(svx * svx + svy * svy);
                api.Hud("speed", (int)Math.Round(sp));

                if (sx < 8 || sx > W - 8 || sy < 8 || sy > H - 8)
                {
                    Lose();
                    return;
                }

                foreach (var o in obstacles)
                {
                    if (Math.Sqrt((o.x - sx) * (o.x - sx) + (o.y - sy) * (o.y - sy)) < o.r + 8)
                    {
                        Lose();
                        return;
                    }
                }

                if (Math.Sqrt((dockX - sx) * (dockX - sx) + (dockY - sy) * (dockY - sy)) < dockR)
                {
                    if (sp < 26)
                    {
                        done = true;
                        int rating = sp < 10 ? 3 : sp < 18 ? 2 : 1;
                        int score = Math.Max(50, 400 + attempts * 80 + (int)Math.Round((26 - sp) * 6));
                        api.Complete(score, rating);
                    }
                    else
                    {
                        Lose();
                    }

                    return;
                }

                Draw();
            });

            void Draw()
            {
                canvas.Clear(Bg);
                foreach (var o in obstacles)
                {
                    canvas.FillCircle((int)o.x, (int)o.y, (int)o.r, ObsFill);
                    canvas.DrawCircle((int)o.x, (int)o.y, (int)o.r, ObsCol);
                }

                canvas.FillCircle((int)dockX, (int)dockY, (int)dockR, RingFill);
                canvas.DrawCircle((int)dockX, (int)dockY, (int)dockR, RingCol);
                canvas.DrawCircle((int)dockX, (int)dockY, (int)dockR - 1, RingCol);

                // Ship triangle: nose (12,0), (-9,-8), (-9,8) rotated by sa about (sx,sy).
                float cos = (float)Math.Cos(sa), sin = (float)Math.Sin(sa);
                (int x, int y) Rot(float px, float py) => ((int)(sx + px * cos - py * sin), (int)(sy + px * sin + py * cos));
                var a = Rot(12, 0);
                var b = Rot(-9, -8);
                var c = Rot(-9, 8);
                canvas.FillTriangle(a.x, a.y, b.x, b.y, c.x, c.y, ShipCol);
            }

            return new MinigameController { Start = Reset };
        }
    }
}
