// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Minigames.Games
{
    /// <summary>Port of <c>web/minigames/star_memory</c> — a 4×4 memory match. The web build was DOM/CSS (card
    /// divs); the native version redraws the board on <see cref="Canvas2D"/> and hit-tests clicks itself. The 8
    /// star glyphs map to letters A–H (the bitmap font has no astral symbols).</summary>
    public sealed class StarMemoryGame : IMinigame
    {
        private const int Cols = 4, Rows = 4, CardSz = 96, Gap = 12, Margin = 12;
        private const int W = Margin * 2 + Cols * CardSz + (Cols - 1) * Gap;
        private const int H = Margin * 2 + Rows * CardSz + (Rows - 1) * Gap;

        private static readonly Rgba Bg = Rgba.Rgb(7, 13, 22);
        private static readonly Rgba FaceDown = Rgba.Rgb(12, 38, 52);
        private static readonly Rgba FaceDownEdge = Rgba.Rgb(40, 70, 90);
        private static readonly Rgba UpBg = Rgba.Rgb(22, 58, 78);
        private static readonly Rgba DoneBg = Rgba.Rgb(18, 56, 42);
        private static readonly Rgba Cyan = Rgba.Rgb(70, 214, 255);
        private static readonly Rgba Green = Rgba.Rgb(124, 255, 176);

        public string Key => "star_memory";
        public LocText Title => new LocText("Star Map Memory", "Sternkarten-Gedächtnis");
        public LocText Desc => new LocText(
            "Navigator training: flip the chart tiles two at a time and find every matching pair.",
            "Navigatoren-Training: drehe die Karten paarweise um und finde alle Paare.");
        public LocText Hint => new LocText("Click two cards to find a pair", "Zwei Karten anklicken, um ein Paar zu finden");
        public IReadOnlyList<LocText> Help { get; } = new[]
        {
            new LocText("Click a card to reveal it, then a second to find its match", "Karte aufdecken, dann zweite suchen"),
            new LocText("Fewer moves = a higher rating", "Weniger Züge = bessere Bewertung"),
        };
        public int Difficulty => 1;

        private sealed class Card
        {
            public char Glyph;
            public bool Up;
            public bool Done;
        }

        public MinigameController Create(MinigameApi api)
        {
            var canvas = api.Canvas(W, H);

            var cards = new List<Card>();
            int first = -1;
            bool locked = false;
            int moves = 0, matched = 0;

            static (int x, int y) CardPos(int i)
            {
                int col = i % Cols, row = i / Cols;
                return (Margin + col * (CardSz + Gap), Margin + row * (CardSz + Gap));
            }

            void Draw()
            {
                canvas.Clear(Bg);
                for (int i = 0; i < cards.Count; i++)
                {
                    var card = cards[i];
                    var (x, y) = CardPos(i);
                    Rgba bg = card.Done ? DoneBg : card.Up ? UpBg : FaceDown;
                    canvas.FillRect(x, y, CardSz, CardSz, bg);
                    canvas.DrawRect(x, y, CardSz, CardSz, card.Done ? Green : FaceDownEdge);
                    if (card.Up || card.Done)
                    {
                        canvas.DrawTextCentered(x + CardSz / 2, y + CardSz / 2 - 21, card.Glyph.ToString(), card.Done ? Green : Cyan, 6);
                    }
                }
            }

            void Reset()
            {
                cards.Clear();
                first = -1;
                locked = false;
                moves = 0;
                matched = 0;
                api.Hud("moves", 0);
                api.Hud("pairs", "0/8");

                var deck = new List<char>();
                for (char g = 'A'; g <= 'H'; g++)
                {
                    deck.Add(g);
                    deck.Add(g);
                }

                api.Shuffle(deck);
                foreach (char g in deck)
                {
                    cards.Add(new Card { Glyph = g });
                }

                Draw();
            }

            void Flip(int idx)
            {
                var card = cards[idx];
                if (locked || card.Up || card.Done)
                {
                    return;
                }

                card.Up = true;
                if (first < 0)
                {
                    first = idx;
                    Draw();
                    return;
                }

                moves++;
                api.Hud("moves", moves);
                int a = first;
                first = -1;
                if (cards[a].Glyph == card.Glyph)
                {
                    cards[a].Done = true;
                    card.Done = true;
                    matched++;
                    api.Hud("pairs", matched + "/8");
                    Draw();
                    if (matched == 8)
                    {
                        int rating = moves <= 10 ? 3 : moves <= 16 ? 2 : 1;
                        api.Complete(System.Math.Max(50, 2000 - (moves - 8) * 60), rating);
                    }
                }
                else
                {
                    locked = true;
                    int b = idx;
                    Draw();
                    api.After(() =>
                    {
                        cards[a].Up = false;
                        cards[b].Up = false;
                        locked = false;
                        Draw();
                    }, 0.65f);
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
                    var (x, y) = CardPos(i);
                    if (p.X >= x && p.X < x + CardSz && p.Y >= y && p.Y < y + CardSz)
                    {
                        Flip(i);
                        return;
                    }
                }
            });

            return new MinigameController { Start = Reset };
        }
    }
}
