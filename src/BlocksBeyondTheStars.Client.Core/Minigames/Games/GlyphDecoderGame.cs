// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Minigames.Games
{
    /// <summary>Port of <c>web/minigames/glyph_decoder</c> (was DOM/CSS): a study phase shows four glyph→meaning
    /// pairs, then a quiz asks the meaning of each glyph from memory. The alien glyphs (⟁⏃⍟…) map to distinct
    /// letters. Redrawn on <see cref="Canvas2D"/> with self hit-tested option buttons.</summary>
    public sealed class GlyphDecoderGame : IMinigame
    {
        private const int W = 560, H = 380;
        private const int OptW = 250, OptH = 46, OptGapX = 16, OptGapY = 14, OptTop = 230;
        private static readonly int OptStartX = (W - (2 * OptW + OptGapX)) / 2;

        private static readonly char[] Glyphs = { 'Q', 'Z', 'X', 'Y', 'J', 'K', 'V', 'W' };

        private static readonly Rgba Bg = Rgba.Rgb(7, 13, 22);
        private static readonly Rgba Info = Rgba.Rgb(120, 150, 170);
        private static readonly Rgba GlyphCol = Rgba.Rgb(70, 214, 255);
        private static readonly Rgba Ink = Rgba.Rgb(207, 233, 245);
        private static readonly Rgba BtnBg = Rgba.Rgb(13, 28, 42);
        private static readonly Rgba BtnEdge = new Rgba(70, 214, 255, 80);
        private static readonly Rgba Ok = Rgba.Rgb(124, 255, 176);
        private static readonly Rgba Bad = Rgba.Rgb(255, 107, 107);

        public string Key => "glyph_decoder";
        public LocText Title => new LocText("Alien Glyph Decoder", "Alien-Glyphen-Entschlüssler");
        public LocText Desc => new LocText(
            "Study the glyph translations, then decode them from memory before the signal fades.",
            "Präge dir die Glyphen-Übersetzungen ein und entschlüssle sie dann aus dem Gedächtnis.");
        public LocText Hint => new LocText("Memorise the pairs, then pick the meaning for each glyph", "Paare merken, dann Bedeutung pro Glyphe wählen");
        public IReadOnlyList<LocText> Help { get; } = new[]
        {
            new LocText("First a study phase shows each glyph's meaning", "Zuerst zeigt eine Lernphase die Bedeutung jeder Glyphe"),
            new LocText("Then choose the right meaning for each glyph", "Dann die richtige Bedeutung pro Glyphe wählen"),
        };
        public int Difficulty => 2;

        private static readonly LocText[] Meanings =
        {
            new LocText("Star", "Stern"), new LocText("Home", "Heimat"), new LocText("Danger", "Gefahr"),
            new LocText("Water", "Wasser"), new LocText("Energy", "Energie"), new LocText("Void", "Leere"),
            new LocText("Ally", "Verbündeter"), new LocText("Path", "Pfad"),
        };

        public MinigameController Create(MinigameApi api)
        {
            var canvas = api.Canvas(W, H);

            var pairGlyph = new char[4];
            var pairMeaning = new int[4];
            int qi = 0, correct = 0, studyCount = 5;
            bool studying = true, asking = false;
            var options = new List<int>();
            int feedbackOpt = -1;
            bool feedbackOk = false;

            string T(string en, string de) => api.German ? de : en;

            (int x, int y) OptPos(int i) => (OptStartX + (i % 2) * (OptW + OptGapX), OptTop + (i / 2) * (OptH + OptGapY));

            void DrawStudy()
            {
                canvas.Clear(Bg);
                canvas.DrawText(20, 20, (T("DECODING IN ", "ENTSCHLUESSELUNG IN ") + studyCount).ToUpperInvariant(), Info, 2);
                for (int k = 0; k < 4; k++)
                {
                    int y = 80 + k * 60;
                    canvas.DrawText(120, y, pairGlyph[k].ToString(), GlyphCol, 4);
                    canvas.DrawText(210, y + 7, Meanings[pairMeaning[k]].Get(api.German).ToUpperInvariant(), Ink, 2);
                }
            }

            void DrawAsk()
            {
                canvas.Clear(Bg);
                canvas.DrawText(20, 20, T("WHAT DOES THIS GLYPH MEAN?", "WAS BEDEUTET DIESE GLYPHE?"), Info, 2);
                canvas.DrawTextCentered(W / 2, 80, pairGlyph[qi].ToString(), GlyphCol, 9);
                for (int i = 0; i < options.Count; i++)
                {
                    var (x, y) = OptPos(i);
                    Rgba edge = BtnEdge;
                    if (feedbackOpt == i)
                    {
                        edge = feedbackOk ? Ok : Bad;
                    }

                    canvas.FillRect(x, y, OptW, OptH, BtnBg);
                    canvas.DrawRect(x, y, OptW, OptH, edge);
                    canvas.DrawTextCentered(x + OptW / 2, y + OptH / 2 - 7, Meanings[options[i]].Get(api.German).ToUpperInvariant(), feedbackOpt == i ? edge : Ink, 2);
                }
            }

            void Ask()
            {
                if (qi >= 4)
                {
                    int rating = correct == 4 ? 3 : correct >= 3 ? 2 : 1;
                    api.Complete(correct * 250, rating);
                    return;
                }

                asking = true;
                feedbackOpt = -1;
                options.Clear();
                options.Add(pairMeaning[qi]);
                while (options.Count < 4)
                {
                    int r = api.Rand(Meanings.Length);
                    if (!options.Contains(r))
                    {
                        options.Add(r);
                    }
                }

                api.Shuffle(options);
                DrawAsk();
            }

            void Answer(int optIndex)
            {
                if (!asking)
                {
                    return;
                }

                asking = false;
                feedbackOpt = optIndex;
                feedbackOk = options[optIndex] == pairMeaning[qi];
                if (feedbackOk)
                {
                    correct++;
                }

                api.Hud("decoded", (qi + 1) + "/4");
                DrawAsk(); // show the feedback on the CURRENT question before advancing
                qi++;
                api.After(Ask, 0.5f);
            }

            void StartStudy()
            {
                studying = true;
                studyCount = 5;
                DrawStudy();
                int iv = 0;
                iv = api.Every(() =>
                {
                    studyCount--;
                    DrawStudy();
                    if (studyCount <= 0)
                    {
                        api.StopTimer(iv);
                        studying = false;
                        Ask();
                    }
                }, 1f);
            }

            void Reset()
            {
                var gi = new List<int>();
                var mi = new List<int>();
                for (int i = 0; i < Glyphs.Length; i++)
                {
                    gi.Add(i);
                }

                for (int i = 0; i < Meanings.Length; i++)
                {
                    mi.Add(i);
                }

                api.Shuffle(gi);
                api.Shuffle(mi);
                for (int k = 0; k < 4; k++)
                {
                    pairGlyph[k] = Glyphs[gi[k]];
                    pairMeaning[k] = mi[k];
                }

                qi = 0;
                correct = 0;
                asking = false;
                feedbackOpt = -1;
                api.Hud("decoded", "0/4");
                StartStudy();
            }

            api.Pointer(p =>
            {
                if (p.Phase != PointerPhase.Down || studying || !asking)
                {
                    return;
                }

                for (int i = 0; i < options.Count; i++)
                {
                    var (x, y) = OptPos(i);
                    if (p.X >= x && p.X < x + OptW && p.Y >= y && p.Y < y + OptH)
                    {
                        Answer(i);
                        return;
                    }
                }
            });

            return new MinigameController { Start = Reset };
        }
    }
}
