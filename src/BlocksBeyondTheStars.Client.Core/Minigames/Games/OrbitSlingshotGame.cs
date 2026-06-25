using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Minigames.Games
{
    /// <summary>Port of <c>web/minigames/orbit_slingshot</c>: drag from the probe to aim, release to launch, and
    /// use the planets' gravity to curve it into the target zone (five attempts). A dotted trajectory preview is
    /// computed by the same integrator the live shot uses.</summary>
    public sealed class OrbitSlingshotGame : IMinigame
    {
        private const int W = 720, H = 460;

        private static readonly Rgba Bg = Rgba.Rgb(7, 13, 22);
        private static readonly Rgba PlanetFill = new Rgba(70, 214, 255, 46);
        private static readonly Rgba PlanetEdge = Rgba.Rgb(70, 214, 255);
        private static readonly Rgba TargetFill = new Rgba(124, 255, 176, 38);
        private static readonly Rgba TargetEdge = Rgba.Rgb(124, 255, 176);
        private static readonly Rgba PreviewCol = new Rgba(70, 214, 255, 128);
        private static readonly Rgba ProbeCol = Rgba.Rgb(124, 255, 176);

        public string Key => "orbit_slingshot";
        public LocText Title => new LocText("Orbit Slingshot", "Orbit-Schleuder");
        public LocText Desc => new LocText(
            "Drag from the probe to aim, then release. Use the planets' gravity to curve the probe into the target zone.",
            "Ziehe von der Sonde, um zu zielen, dann loslassen. Nutze die Gravitation der Planeten, um die Sonde ins Ziel zu lenken.");
        public LocText Hint => new LocText("Drag from the probe & release to launch", "Von der Sonde ziehen & loslassen zum Start");
        public IReadOnlyList<LocText> Help { get; } = new[]
        {
            new LocText("Click-drag from the probe to set direction and power", "Von der Sonde ziehen, um Richtung und Kraft zu setzen"),
            new LocText("Release to launch; gravity bends the path. Reach the green target.", "Loslassen zum Start; Gravitation krümmt die Bahn. Erreiche das grüne Ziel."),
        };
        public int Difficulty => 3;

        private struct Planet
        {
            public float X, Y, Mass, R;
        }

        public MinigameController Create(MinigameApi api)
        {
            var canvas = api.Canvas(W, H);
            float startX = 70, startY = H / 2f;

            var planets = new List<Planet>();
            (float x, float y, float r) target = (0, 0, 26);
            float pbx = 0, pby = 0, pvx = 0, pvy = 0;
            int attempts = 5;
            bool flying = false, done = false, aiming = false;
            float aimX = 0, aimY = 0;

            (float ax, float ay) Grav(float x, float y)
            {
                float ax = 0, ay = 0;
                foreach (var p in planets)
                {
                    float dx = p.X - x, dy = p.Y - y, d2 = dx * dx + dy * dy;
                    float d = (float)Math.Sqrt(d2);
                    if (d == 0)
                    {
                        d = 1;
                    }

                    float f = 2600f * p.Mass / Math.Max(d2, 400f);
                    ax += f * dx / d;
                    ay += f * dy / d;
                }

                return (ax, ay);
            }

            List<(float x, float y)> Simulate(float vx, float vy, int steps)
            {
                var pts = new List<(float, float)>();
                float x = startX, y = startY, dt = 0.5f;
                for (int i = 0; i < steps; i++)
                {
                    var (gx, gy) = Grav(x, y);
                    vx += gx * dt * 0.02f;
                    vy += gy * dt * 0.02f;
                    x += vx * dt;
                    y += vy * dt;
                    pts.Add((x, y));
                    if (x < 0 || x > W || y < 0 || y > H)
                    {
                        break;
                    }

                    bool hit = false;
                    foreach (var p in planets)
                    {
                        if ((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y) < p.R * p.R)
                        {
                            hit = true;
                            break;
                        }
                    }

                    if (hit)
                    {
                        break;
                    }
                }

                return pts;
            }

            void Draw()
            {
                canvas.Clear(Bg);
                foreach (var p in planets)
                {
                    canvas.FillCircle((int)p.X, (int)p.Y, (int)p.R, PlanetFill);
                    canvas.DrawCircle((int)p.X, (int)p.Y, (int)p.R, PlanetEdge);
                }

                canvas.FillCircle((int)target.x, (int)target.y, (int)target.r, TargetFill);
                canvas.DrawCircle((int)target.x, (int)target.y, (int)target.r, TargetEdge);

                if (aiming)
                {
                    var pts = Simulate((aimX - startX) * 0.06f, (aimY - startY) * 0.06f, 160);
                    for (int k = 0; k < pts.Count; k += 4)
                    {
                        canvas.FillCircle((int)pts[k].x, (int)pts[k].y, 2, PreviewCol);
                    }
                }

                canvas.FillCircle((int)pbx, (int)pby, 6, ProbeCol);
            }

            void Reset()
            {
                attempts = 5;
                done = false;
                flying = false;
                aiming = false;
                planets.Clear();
                int n = 1 + api.Rand(2);
                for (int i = 0; i < n; i++)
                {
                    planets.Add(new Planet { X = 240 + api.Rand(280), Y = 80 + api.Rand(H - 160), Mass = 1.2f + api.Rand(160) / 100f, R = 26 + api.Rand(14) });
                }

                target = (W - 70, 80 + api.Rand(H - 160), 26);
                pbx = startX;
                pby = startY;
                pvx = pvy = 0;
                api.Hud("attempts", attempts);
                Draw();
            }

            api.Pointer(p =>
            {
                if (done || flying)
                {
                    return;
                }

                if (p.Phase == PointerPhase.Down)
                {
                    aiming = true;
                    aimX = p.X;
                    aimY = p.Y;
                }
                else if (p.Phase == PointerPhase.Move && aiming)
                {
                    aimX = p.X;
                    aimY = p.Y;
                    Draw();
                }
                else if (p.Phase == PointerPhase.Up && aiming)
                {
                    aiming = false;
                    pvx = (aimX - startX) * 0.06f;
                    pvy = (aimY - startY) * 0.06f;
                    pbx = startX;
                    pby = startY;
                    flying = true;
                }
            });

            api.Loop(dt =>
            {
                if (!flying || done)
                {
                    return;
                }

                for (int s = 0; s < 3; s++)
                {
                    var (gx, gy) = Grav(pbx, pby);
                    pvx += gx * 0.01f;
                    pvy += gy * 0.01f;
                    pbx += pvx;
                    pby += pvy;
                    if ((target.x - pbx) * (target.x - pbx) + (target.y - pby) * (target.y - pby) < target.r * target.r)
                    {
                        done = true;
                        int used = 6 - attempts;
                        int rating = used <= 1 ? 3 : used <= 2 ? 2 : 1;
                        api.Complete(Math.Max(50, 200 + attempts * 80), rating);
                        return;
                    }

                    bool bad = pbx < -20 || pbx > W + 20 || pby < -20 || pby > H + 20;
                    foreach (var pl in planets)
                    {
                        if ((pl.X - pbx) * (pl.X - pbx) + (pl.Y - pby) * (pl.Y - pby) < pl.R * pl.R)
                        {
                            bad = true;
                        }
                    }

                    if (bad)
                    {
                        flying = false;
                        attempts--;
                        api.Hud("attempts", attempts);
                        pbx = startX;
                        pby = startY;
                        pvx = pvy = 0;
                        if (attempts <= 0)
                        {
                            done = true;
                            api.Fail(0);
                        }

                        break;
                    }
                }

                Draw();
            });

            return new MinigameController { Start = Reset };
        }
    }
}
