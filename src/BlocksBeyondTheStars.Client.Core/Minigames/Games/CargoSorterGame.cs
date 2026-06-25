using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Minigames.Games
{
    /// <summary>Port of <c>web/minigames/cargo_sorter</c> (was DOM/CSS): route each incoming crate to the matching
    /// cargo bay; five mistakes end the shift, otherwise survive 90 seconds. The crate symbols (⛏⚙❀⚡) map to the
    /// bay initials O/T/B/E. Redrawn on <see cref="Canvas2D"/> with self hit-tested bays.</summary>
    public sealed class CargoSorterGame : IMinigame
    {
        private const int W = 560, H = 360;
        private const int BinW = 120, BinH = 110, BinGap = 15, BinY = 210;
        private const int BinStart = (W - (4 * BinW + 3 * BinGap)) / 2;

        private static readonly Rgba Bg = Rgba.Rgb(7, 13, 22);
        private static readonly Rgba CrateBg = Rgba.Rgb(16, 36, 52);
        private static readonly Rgba CrateEdge = Rgba.Rgb(70, 214, 255);
        private static readonly Rgba BinBg = Rgba.Rgb(13, 28, 42);
        private static readonly Rgba BinEdge = new Rgba(70, 214, 255, 64);
        private static readonly Rgba BinSel = Rgba.Rgb(70, 214, 255);
        private static readonly Rgba Ink = Rgba.Rgb(207, 233, 245);
        private static readonly Rgba Dim = Rgba.Rgb(120, 150, 170);

        private static readonly char[] Letters = { 'O', 'T', 'B', 'E' };

        public string Key => "cargo_sorter";
        public LocText Title => new LocText("Cargo Sorter", "Frachtsortierer");
        public LocText Desc => new LocText(
            "Route each incoming crate to the matching cargo bay. Keep up as the belt speeds up — five mistakes ends the shift.",
            "Leite jede ankommende Kiste in die passende Frachtbucht. Bleib dran, wenn das Band schneller wird — fünf Fehler beenden die Schicht.");
        public LocText Hint => new LocText("← → choose bay · Enter confirm · or click a bay", "← → Bucht wählen · Enter bestätigen · oder Bucht anklicken");
        public IReadOnlyList<LocText> Help { get; } = new[]
        {
            new LocText("Match the crate's symbol to a cargo bay", "Symbol der Kiste einer Frachtbucht zuordnen"),
            new LocText("← → select, Enter confirm, or click the bay", "← → wählen, Enter bestätigen, oder Bucht klicken"),
        };
        public int Difficulty => 2;

        public MinigameController Create(MinigameApi api)
        {
            var canvas = api.Canvas(W, H);
            LocText[] names =
            {
                new LocText("Ore", "Erz"), new LocText("Tech", "Technik"),
                new LocText("Bio", "Bio"), new LocText("Energy", "Energie"),
            };

            int cur = 0, sel = 0, score = 0, mistakes = 0;
            float time = 0;
            bool done = false;

            void Render()
            {
                canvas.Clear(Bg);
                // Incoming crate.
                int cxBox = W / 2 - 60;
                canvas.FillRect(cxBox, 30, 120, 120, CrateBg);
                canvas.DrawRect(cxBox, 30, 120, 120, CrateEdge);
                canvas.DrawTextCentered(W / 2, 60, Letters[cur].ToString(), CrateEdge, 8);

                for (int i = 0; i < 4; i++)
                {
                    int x = BinStart + i * (BinW + BinGap);
                    canvas.FillRect(x, BinY, BinW, BinH, BinBg);
                    canvas.DrawRect(x, BinY, BinW, BinH, i == sel ? BinSel : BinEdge);
                    canvas.DrawTextCentered(x + BinW / 2, BinY + 22, Letters[i].ToString(), i == sel ? BinSel : Ink, 5);
                    canvas.DrawTextCentered(x + BinW / 2, BinY + BinH - 22, names[i].Get(api.German).ToUpperInvariant(), Dim, 2);
                }
            }

            void Spawn() => cur = api.Rand(4);

            void Assign(int i)
            {
                if (done)
                {
                    return;
                }

                if (i == cur)
                {
                    score += 10;
                    api.Hud("score", score);
                }
                else
                {
                    mistakes++;
                    api.Hud("errors", mistakes + "/5");
                }

                if (mistakes >= 5)
                {
                    done = true;
                    api.Fail(score);
                    return;
                }

                Spawn();
                Render();
            }

            void Reset()
            {
                score = 0;
                mistakes = 0;
                time = 0;
                sel = 0;
                done = false;
                Spawn();
                api.Hud("score", 0);
                api.Hud("errors", "0/5");
                Render();
            }

            api.Bind(MinigameAction.Left, () => { sel = (sel + 3) % 4; Render(); });
            api.Bind(MinigameAction.Right, () => { sel = (sel + 1) % 4; Render(); });
            api.Bind(MinigameAction.Confirm, () => Assign(sel));
            api.Bind(MinigameAction.Primary, () => Assign(sel));

            api.Pointer(p =>
            {
                if (p.Phase != PointerPhase.Down)
                {
                    return;
                }

                for (int i = 0; i < 4; i++)
                {
                    int x = BinStart + i * (BinW + BinGap);
                    if (p.X >= x && p.X < x + BinW && p.Y >= BinY && p.Y < BinY + BinH)
                    {
                        sel = i;
                        Assign(i);
                        return;
                    }
                }
            });

            api.Loop(dt =>
            {
                if (done)
                {
                    return;
                }

                time += dt;
                if (time > 90)
                {
                    done = true;
                    int rating = mistakes == 0 ? 3 : mistakes <= 2 ? 2 : 1;
                    api.Complete(score, rating);
                }
            });

            return new MinigameController { Start = Reset };
        }
    }
}
