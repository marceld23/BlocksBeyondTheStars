using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Minigames.Games
{
    /// <summary>Port of <c>web/minigames/comet_courier</c>: steer a courier ship vertically through a debris field,
    /// scoop cyan data packets and dodge asteroids (each hit costs shield), survive the 3000 m run. Second native
    /// game, establishing the port pattern alongside <see cref="DataFishingGame"/>.</summary>
    public sealed class CometCourierGame : IMinigame
    {
        private const int W = 720, H = 440;

        private static readonly Rgba Bg = Rgba.Rgb(7, 13, 22);
        private static readonly Rgba Packet = Rgba.Rgb(70, 214, 255);
        private static readonly Rgba Rock = Rgba.Rgb(255, 107, 107);
        private static readonly Rgba Ship = Rgba.Rgb(124, 255, 176);

        public string Key => "comet_courier";
        public LocText Title => new LocText("Comet Courier", "Kometen-Kurier");
        public LocText Desc => new LocText(
            "Fly the courier through the debris field, scoop up data packets and dodge asteroids. Reach the end of the run.",
            "Flieg den Kurier durch das Trümmerfeld, sammle Datenpakete und weiche Asteroiden aus. Erreiche das Ende der Strecke.");
        public LocText Hint => new LocText("↑ ↓ / mouse steer · avoid asteroids · grab packets", "↑ ↓ / Maus steuern · Asteroiden meiden · Pakete sammeln");
        public IReadOnlyList<LocText> Help { get; } = new[]
        {
            new LocText("↑ ↓ or the mouse move the ship vertically", "↑ ↓ oder die Maus bewegen das Schiff senkrecht"),
            new LocText("Collect cyan data packets; asteroids cost shield. Survive the distance.", "Sammle cyanfarbene Datenpakete; Asteroiden kosten Schild. Überstehe die Strecke."),
        };
        public int Difficulty => 2;

        public MinigameController Create(MinigameApi api)
        {
            var canvas = api.Canvas(W, H);

            float shipY = 0f;
            var obs = new List<Obstacle>();
            var pks = new List<Packetf>();
            int shield = 0, score = 0;
            float dist = 0f, spd = 0f, sp1 = 0f, sp2 = 0f;
            bool done = false;

            void Reset()
            {
                shipY = H / 2f;
                obs.Clear();
                pks.Clear();
                shield = 3;
                score = 0;
                dist = 0f;
                done = false;
                spd = 230f;
                sp1 = 0f;
                sp2 = 0f;
                api.Hud("score", 0);
                api.Hud("shield", shield);
                api.Hud("dist", "0m");
            }

            api.Pointer(p =>
            {
                if (p.Phase == PointerPhase.Move || p.Phase == PointerPhase.Down)
                {
                    shipY = Clamp(p.Y, 20f, H - 20f);
                }
            });

            api.Loop(dt =>
            {
                if (done)
                {
                    return;
                }

                if (api.Held(MinigameAction.Up))
                {
                    shipY = System.Math.Max(20f, shipY - 280f * dt);
                }

                if (api.Held(MinigameAction.Down))
                {
                    shipY = System.Math.Min(H - 20f, shipY + 280f * dt);
                }

                dist += spd * dt;
                spd += dt * 4f;

                sp1 += dt;
                if (sp1 > 0.7f)
                {
                    sp1 = 0f;
                    obs.Add(new Obstacle { X = W + 30f, Y = 30f + api.Rand(H - 60), R = 14f + api.Rand(16) });
                }

                sp2 += dt;
                if (sp2 > 0.5f)
                {
                    sp2 = 0f;
                    pks.Add(new Packetf { X = W + 20f, Y = 30f + api.Rand(H - 60) });
                }

                for (int i = obs.Count - 1; i >= 0; i--)
                {
                    var o = obs[i];
                    o.X -= spd * dt;
                    obs[i] = o;
                    if (o.X < -40f)
                    {
                        obs.RemoveAt(i);
                        continue;
                    }

                    if (System.Math.Abs(o.X - 70f) < o.R + 10f && System.Math.Abs(o.Y - shipY) < o.R + 10f)
                    {
                        obs.RemoveAt(i);
                        shield--;
                        api.Hud("shield", shield);
                        if (shield <= 0)
                        {
                            done = true;
                            api.Fail(score);
                            return;
                        }
                    }
                }

                for (int i = pks.Count - 1; i >= 0; i--)
                {
                    var pk = pks[i];
                    pk.X -= spd * dt;
                    pks[i] = pk;
                    if (pk.X < -20f)
                    {
                        pks.RemoveAt(i);
                        continue;
                    }

                    if (System.Math.Abs(pk.X - 70f) < 18f && System.Math.Abs(pk.Y - shipY) < 18f)
                    {
                        pks.RemoveAt(i);
                        score += 15;
                        api.Hud("score", score);
                    }
                }

                api.Hud("dist", (int)System.Math.Round(dist) + "m");

                if (dist >= 3000f)
                {
                    done = true;
                    int rating = shield >= 3 ? 3 : shield == 2 ? 2 : 1;
                    api.Complete(score + shield * 50, rating);
                    return;
                }

                Draw();
            });

            void Draw()
            {
                canvas.Clear(Bg);
                foreach (var pk in pks)
                {
                    canvas.FillRect((int)(pk.X - 6), (int)(pk.Y - 6), 12, 12, Packet);
                }

                foreach (var o in obs)
                {
                    canvas.FillCircle((int)o.X, (int)o.Y, (int)o.R, Rock);
                }

                // Ship: a small triangle nose at x≈86 — approximated as a filled wedge of horizontal spans.
                int sy = (int)shipY;
                for (int dx = 0; dx <= 32; dx++)
                {
                    int half = 12 - (dx * 12 / 32);
                    canvas.FillRect(54 + dx, sy - half, 1, half * 2 + 1, Ship);
                }
            }

            return new MinigameController { Start = Reset };
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        private struct Obstacle
        {
            public float X, Y, R;
        }

        private struct Packetf
        {
            public float X, Y;
        }
    }
}
