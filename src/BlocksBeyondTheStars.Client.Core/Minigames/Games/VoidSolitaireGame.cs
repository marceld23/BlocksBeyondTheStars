using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Minigames.Games
{
    /// <summary>Port of <c>web/minigames/void_solitaire</c> (was DOM/CSS): a calm matching sweep — every card is
    /// face-up; tap two with the same symbol to clear them, clear the whole board, fewer mismatches scores higher.
    /// The 12 symbols map to letters A–L. Redrawn on <see cref="Canvas2D"/> with self hit-testing.</summary>
    public sealed class VoidSolitaireGame : IMinigame
    {
        private const int Cols = 6, Rows = 4, CardW = 84, CardH = 104, Gap = 10, Margin = 12;
        private const int W = Margin * 2 + Cols * CardW + (Cols - 1) * Gap;
        private const int H = Margin * 2 + Rows * CardH + (Rows - 1) * Gap;

        private static readonly Rgba Bg = Rgba.Rgb(7, 13, 22);
        private static readonly Rgba CardBg = Rgba.Rgb(12, 36, 52);
        private static readonly Rgba Edge = new Rgba(70, 214, 255, 80);
        private static readonly Rgba Sel = Rgba.Rgb(70, 214, 255);
        private static readonly Rgba BadCol = Rgba.Rgb(255, 107, 107);
        private static readonly Rgba Sym = Rgba.Rgb(70, 214, 255);

        public string Key => "void_solitaire";
        public LocText Title => new LocText("Void Solitaire", "Void-Solitär");
        public LocText Desc => new LocText(
            "A quiet card sweep for the long haul. Clear the board by tapping matching pairs of symbols.",
            "Ein ruhiges Kartenspiel für die lange Reise. Räume das Feld, indem du gleiche Symbol-Paare antippst.");
        public LocText Hint => new LocText("Click two matching cards to clear them", "Zwei gleiche Karten anklicken zum Entfernen");
        public IReadOnlyList<LocText> Help { get; } = new[]
        {
            new LocText("Click a card, then another with the same symbol — both clear", "Karte anklicken, dann eine mit gleichem Symbol — beide verschwinden"),
            new LocText("Clear the whole board; fewer mismatches scores higher", "Räume das ganze Feld; weniger Fehlversuche geben mehr Punkte"),
        };
        public int Difficulty => 1;

        private sealed class Card
        {
            public char Sym;
            public bool Gone;
            public bool Selected;
            public bool Bad;
        }

        public MinigameController Create(MinigameApi api)
        {
            var canvas = api.Canvas(W, H);
            var cards = new List<Card>();
            int first = -1, left = 0, miss = 0;
            bool locked = false;

            static (int x, int y) Pos(int i)
            {
                int col = i % Cols, row = i / Cols;
                return (Margin + col * (CardW + Gap), Margin + row * (CardH + Gap));
            }

            void Draw()
            {
                canvas.Clear(Bg);
                for (int i = 0; i < cards.Count; i++)
                {
                    var card = cards[i];
                    if (card.Gone)
                    {
                        continue;
                    }

                    var (x, y) = Pos(i);
                    canvas.FillRect(x, y, CardW, CardH, CardBg);
                    canvas.DrawRect(x, y, CardW, CardH, card.Bad ? BadCol : card.Selected ? Sel : Edge);
                    canvas.DrawTextCentered(x + CardW / 2, y + CardH / 2 - 21, card.Sym.ToString(), card.Bad ? BadCol : Sym, 6);
                }
            }

            void Reset()
            {
                cards.Clear();
                first = -1;
                locked = false;
                miss = 0;
                var deck = new List<char>();
                for (char s = 'A'; s < 'A' + 12; s++)
                {
                    deck.Add(s);
                    deck.Add(s);
                }

                api.Shuffle(deck);
                left = deck.Count;
                foreach (char s in deck)
                {
                    cards.Add(new Card { Sym = s });
                }

                api.Hud("left", left);
                api.Hud("misses", 0);
                Draw();
            }

            void Pick(int idx)
            {
                var card = cards[idx];
                if (locked || card.Gone || idx == first)
                {
                    return;
                }

                card.Selected = true;
                if (first < 0)
                {
                    first = idx;
                    Draw();
                    return;
                }

                int a = first;
                if (cards[a].Sym == card.Sym)
                {
                    cards[a].Gone = true;
                    card.Gone = true;
                    first = -1;
                    left -= 2;
                    api.Hud("left", left);
                    Draw();
                    if (left == 0)
                    {
                        int rating = miss == 0 ? 3 : miss <= 4 ? 2 : 1;
                        api.Complete(System.Math.Max(50, 1000 - miss * 40), rating);
                    }
                }
                else
                {
                    miss++;
                    api.Hud("misses", miss);
                    locked = true;
                    int b = idx;
                    cards[a].Bad = true;
                    card.Bad = true;
                    first = -1;
                    Draw();
                    api.After(() =>
                    {
                        cards[a].Selected = cards[a].Bad = false;
                        cards[b].Selected = cards[b].Bad = false;
                        locked = false;
                        Draw();
                    }, 0.5f);
                }
            }

            api.Pointer(p =>
            {
                if (p.Phase != PointerPhase.Down)
                {
                    return;
                }

                for (int i = 0; i < cards.Count; i++)
                {
                    if (cards[i].Gone)
                    {
                        continue;
                    }

                    var (x, y) = Pos(i);
                    if (p.X >= x && p.X < x + CardW && p.Y >= y && p.Y < y + CardH)
                    {
                        Pick(i);
                        return;
                    }
                }
            });

            return new MinigameController { Start = Reset };
        }
    }
}
