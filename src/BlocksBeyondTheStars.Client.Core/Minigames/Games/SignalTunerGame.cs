// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Minigames.Games
{
    /// <summary>Port of <c>web/minigames/signal_tuner</c> (was DOM/CSS bars): adjust three parameters toward
    /// their hidden targets to raise the signal quality above the threshold, then hold it there until the decode
    /// bar fills. Redrawn on <see cref="Canvas2D"/> as labelled bars.</summary>
    public sealed class SignalTunerGame : IMinigame
    {
        private const int W = 560, H = 340;
        private const int BarX = 150, BarW = W - BarX - 20, BarH = 22, RowH = 56, Top = 24;

        private static readonly Rgba Bg = Rgba.Rgb(7, 13, 22);
        private static readonly Rgba BarBg = Rgba.Rgb(16, 32, 46);
        private static readonly Rgba FillGood = Rgba.Rgb(124, 255, 176);
        private static readonly Rgba FillNorm = Rgba.Rgb(70, 214, 255);
        private static readonly Rgba Tick = Rgba.Rgb(124, 255, 176);
        private static readonly Rgba Req = Rgba.Rgb(255, 192, 77);
        private static readonly Rgba LabelSel = Rgba.Rgb(70, 214, 255);
        private static readonly Rgba LabelDim = Rgba.Rgb(120, 150, 170);

        public string Key => "signal_tuner";
        public LocText Title => new LocText("Signal Tuner", "Signal-Tuner");
        public LocText Desc => new LocText(
            "Adjust the parameters to lock onto the hidden signal, then hold the quality high until it decodes.",
            "Justiere die Parameter auf das verborgene Signal und halte die Qualität hoch, bis es dekodiert ist.");
        public LocText Hint => new LocText("← → select · ↑ ↓ adjust", "← → wählen · ↑ ↓ justieren");
        public IReadOnlyList<LocText> Help { get; } = new[]
        {
            new LocText("← → : choose a parameter", "← → : Parameter wählen"),
            new LocText("↑ ↓ : raise / lower it", "↑ ↓ : erhöhen / senken"),
            new LocText("Keep quality above the line until the decode bar fills", "Halte die Qualität über der Linie, bis der Dekodierbalken voll ist"),
        };
        public int Difficulty => 3;

        public MinigameController Create(MinigameApi api)
        {
            var canvas = api.Canvas(W, H);
            string[] labels = api.German
                ? new[] { "FREQUENZ", "AMPLITUDE", "FILTER", "QUALITAET", "DEKODIERUNG" }
                : new[] { "FREQUENCY", "AMPLITUDE", "FILTER", "QUALITY", "DECODE" };

            var val = new[] { 0.5f, 0.5f, 0.5f };
            var target = new float[3];
            int sel = 0;
            float decode = 0, time = 0, rep = 0;
            const float req = 0.85f;
            bool done = false;

            void Bar(int row, float fill, Rgba fillCol, float? tick, Rgba tickCol, bool selected)
            {
                int y = Top + row * RowH;
                canvas.DrawText(10, y + 4, labels[row], selected ? LabelSel : LabelDim, 2);
                canvas.FillRect(BarX, y, BarW, BarH, BarBg);
                canvas.FillRect(BarX, y, (int)(fill * BarW), BarH, fillCol);
                if (tick.HasValue)
                {
                    canvas.FillRect(BarX + (int)(tick.Value * BarW) - 1, y - 3, 3, BarH + 6, tickCol);
                }
            }

            void Render(float q)
            {
                canvas.Clear(Bg);
                for (int i = 0; i < 3; i++)
                {
                    Bar(i, val[i], FillNorm, target[i], Tick, i == sel);
                }

                Bar(3, q, q >= req ? FillGood : FillNorm, req, Req, false);
                Bar(4, decode, FillGood, null, Tick, false);
            }

            void Reset()
            {
                for (int i = 0; i < 3; i++)
                {
                    val[i] = 0.5f;
                    target[i] = 0.15f + api.Rand(10000) / 10000f * 0.7f;
                }

                sel = 0;
                decode = 0;
                time = 0;
                rep = 0;
                done = false;
                api.Hud("quality", "0%");
                Render(0);
            }

            api.Bind(MinigameAction.Left, () => { sel = (sel + 2) % 3; });
            api.Bind(MinigameAction.Right, () => { sel = (sel + 1) % 3; });

            api.Loop(dt =>
            {
                if (done)
                {
                    return;
                }

                rep += dt;
                if (rep > 0.05f)
                {
                    rep = 0;
                    if (api.Held(MinigameAction.Up))
                    {
                        val[sel] = Math.Min(1, val[sel] + 0.02f);
                    }

                    if (api.Held(MinigameAction.Down))
                    {
                        val[sel] = Math.Max(0, val[sel] - 0.02f);
                    }
                }

                float err = 0;
                for (int i = 0; i < 3; i++)
                {
                    err += Math.Abs(val[i] - target[i]);
                }

                float q = Math.Max(0, 1 - err / 1.6f);
                api.Hud("quality", (int)Math.Round(q * 100) + "%");
                decode = q >= req ? Math.Min(1, decode + dt / 5f) : Math.Max(0, decode - dt * 0.25f);
                time += dt;
                Render(q);

                if (decode >= 1)
                {
                    done = true;
                    int rating = time < 35 ? 3 : time < 70 ? 2 : 1;
                    api.Complete(Math.Max(50, 1200 - (int)Math.Round(time * 8)), rating);
                }
                else if (time > 120)
                {
                    done = true;
                    api.Fail((int)Math.Round(decode * 200));
                }
            });

            return new MinigameController { Start = Reset };
        }
    }
}
