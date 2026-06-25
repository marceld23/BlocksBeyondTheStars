using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Minigames.Games
{
    /// <summary>Port of <c>web/minigames/planet_scanner</c>: pulse-scan a planet disc to locate five hidden
    /// signatures. A near miss leaves a warmth ring (orange = close, blue = far); find all five before the scan
    /// energy runs out.</summary>
    public sealed class PlanetScannerGame : IMinigame
    {
        private const int W = 460, H = 460;
        private const int Cx = W / 2, Cy = H / 2, R = 200;

        private static readonly Rgba Bg = Rgba.Rgb(7, 13, 22);
        private static readonly Rgba Disc = Rgba.Rgb(13, 38, 60);
        private static readonly Rgba DiscInner = Rgba.Rgb(19, 49, 74);
        private static readonly Rgba DiscEdge = new Rgba(70, 214, 255, 102);
        private static readonly Rgba Found = Rgba.Rgb(124, 255, 176);

        public string Key => "planet_scanner";
        public LocText Title => new LocText("Planet Scanner", "Planeten-Scanner");
        public LocText Desc => new LocText(
            "Pulse-scan the planet to locate hidden signatures. Each scan reveals how close you are — find them all before the scan energy is gone.",
            "Scanne den Planeten, um verborgene Signaturen zu orten. Jeder Scan zeigt, wie nah du bist — finde alle, bevor die Scan-Energie aus ist.");
        public LocText Hint => new LocText("Click the planet to scan — warmer = closer", "Planet anklicken zum Scannen — wärmer = näher");
        public IReadOnlyList<LocText> Help { get; } = new[]
        {
            new LocText("Click to pulse-scan; a hit marks a signature", "Klicken zum Scannen; ein Treffer markiert eine Signatur"),
            new LocText("A miss shows a warmth ring — orange is close, blue is far", "Ein Fehlschlag zeigt einen Wärmering — orange ist nah, blau ist fern"),
        };
        public int Difficulty => 2;

        private struct Pt
        {
            public float X, Y;
            public bool Found;
        }

        private struct Ripple
        {
            public float X, Y, R, Warm, Life;
        }

        public MinigameController Create(MinigameApi api)
        {
            var canvas = api.Canvas(W, H);
            var pts = new List<Pt>();
            var ripples = new List<Ripple>();
            int found = 0, scans = 0;
            bool done = false;

            void Draw()
            {
                canvas.Clear(Bg);
                canvas.FillCircle(Cx, Cy, R, Disc);
                canvas.FillCircle(Cx - 40, Cy - 40, R - 70, DiscInner);
                canvas.DrawCircle(Cx, Cy, R, DiscEdge);

                foreach (var rp in ripples)
                {
                    if (rp.Life <= 0)
                    {
                        continue;
                    }

                    byte a = (byte)System.Math.Min(255, (int)(rp.Life * 255));
                    var col = new Rgba(
                        (byte)System.Math.Round(70 + rp.Warm * 185),
                        (byte)System.Math.Round(214 - rp.Warm * 100),
                        (byte)System.Math.Round(255 - rp.Warm * 180), a);
                    canvas.DrawCircle((int)rp.X, (int)rp.Y, (int)rp.R, col);
                }

                foreach (var pt in pts)
                {
                    if (pt.Found)
                    {
                        canvas.FillCircle((int)pt.X, (int)pt.Y, 7, Found);
                    }
                }
            }

            void Reset()
            {
                pts.Clear();
                ripples.Clear();
                found = 0;
                scans = 18;
                done = false;
                for (int i = 0; i < 5; i++)
                {
                    double a = api.Rand(10000) / 10000.0 * Math.PI * 2;
                    double r = api.Rand(10000) / 10000.0 * (R - 30);
                    pts.Add(new Pt { X = (float)(Cx + Math.Cos(a) * r), Y = (float)(Cy + Math.Sin(a) * r) });
                }

                api.Hud("found", "0/5");
                api.Hud("scans", scans);
                Draw();
            }

            api.Pointer(p =>
            {
                if (p.Phase != PointerPhase.Down || done)
                {
                    return;
                }

                if (Math.Sqrt((p.X - Cx) * (p.X - Cx) + (p.Y - Cy) * (p.Y - Cy)) > R)
                {
                    return;
                }

                int best = -1;
                double bd = 1e9;
                for (int i = 0; i < pts.Count; i++)
                {
                    if (pts[i].Found)
                    {
                        continue;
                    }

                    double d = Math.Sqrt((pts[i].X - p.X) * (pts[i].X - p.X) + (pts[i].Y - p.Y) * (pts[i].Y - p.Y));
                    if (d < bd)
                    {
                        bd = d;
                        best = i;
                    }
                }

                if (best >= 0 && bd < 26)
                {
                    var pt = pts[best];
                    pt.Found = true;
                    pts[best] = pt;
                    found++;
                    api.Hud("found", found + "/5");
                }
                else
                {
                    scans--;
                    api.Hud("scans", scans);
                    float warm = (float)Math.Max(0, 1 - bd / 180);
                    ripples.Add(new Ripple { X = p.X, Y = p.Y, R = 6, Warm = warm, Life = 1 });
                }

                if (found == 5)
                {
                    done = true;
                    int rating = scans >= 10 ? 3 : scans >= 4 ? 2 : 1;
                    api.Complete(300 + scans * 30, rating);
                    return;
                }

                if (scans <= 0)
                {
                    done = true;
                    api.Fail(found * 60);
                    return;
                }

                Draw();
            });

            api.Loop(dt =>
            {
                bool live = false;
                for (int i = ripples.Count - 1; i >= 0; i--)
                {
                    var rp = ripples[i];
                    rp.R += 90 * dt;
                    rp.Life -= dt * 1.2f;
                    ripples[i] = rp;
                    if (rp.Life <= 0)
                    {
                        ripples.RemoveAt(i);
                    }
                    else
                    {
                        live = true;
                    }
                }

                if (live)
                {
                    Draw();
                }
            });

            return new MinigameController { Start = Reset };
        }
    }
}
