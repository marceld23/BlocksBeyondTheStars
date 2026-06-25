// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Minigames.Games
{
    /// <summary>Port of <c>web/minigames/reactor_balance</c> (was DOM/CSS gauges): each of three gauges drifts;
    /// keep all of them inside their green zones for 60 seconds without spending too long in the red (a meltdown
    /// trips at 6 s of cumulative danger). Redrawn on <see cref="Canvas2D"/> as zoned bars.</summary>
    public sealed class ReactorBalanceGame : IMinigame
    {
        private const int W = 560, H = 320;
        private const int BarX = 150, BarW = W - BarX - 20, BarH = 22, RowH = 56, Top = 24;

        private static readonly Rgba Bg = Rgba.Rgb(7, 13, 22);
        private static readonly Rgba BarBg = Rgba.Rgb(16, 32, 46);
        private static readonly Rgba Zone = new Rgba(124, 255, 176, 60);
        private static readonly Rgba Ok = Rgba.Rgb(124, 255, 176);
        private static readonly Rgba Bad = Rgba.Rgb(255, 107, 107);
        private static readonly Rgba Stab = Rgba.Rgb(70, 214, 255);
        private static readonly Rgba LabelSel = Rgba.Rgb(70, 214, 255);
        private static readonly Rgba LabelDim = Rgba.Rgb(120, 150, 170);

        public string Key => "reactor_balance";
        public LocText Title => new LocText("Reactor Balance", "Reaktor-Balance");
        public LocText Desc => new LocText(
            "Hold every reactor gauge inside its green zone as it drifts. Keep it stable for 60 seconds without a meltdown.",
            "Halte jede Reaktoranzeige in ihrer grünen Zone, während sie driftet. Bleib 60 Sekunden stabil ohne Kernschmelze.");
        public LocText Hint => new LocText("← → select gauge · ↑ ↓ adjust", "← → Anzeige wählen · ↑ ↓ justieren");
        public IReadOnlyList<LocText> Help { get; } = new[]
        {
            new LocText("← → choose a gauge, ↑ ↓ raise/lower it", "← → Anzeige wählen, ↑ ↓ erhöhen/senken"),
            new LocText("Keep all gauges in their green zones; too long in the red trips a meltdown", "Halte alle in den grünen Zonen; zu lange im Roten löst eine Kernschmelze aus"),
        };
        public int Difficulty => 3;

        public MinigameController Create(MinigameApi api)
        {
            var canvas = api.Canvas(W, H);
            string[] labels = api.German
                ? new[] { "TEMPERATUR", "LEISTUNG", "KUEHLUNG", "STABILITAET" }
                : new[] { "TEMPERATURE", "POWER", "COOLANT", "STABILITY" };

            var val = new[] { 0.5f, 0.5f, 0.5f };
            var lo = new float[3];
            var hi = new float[3];
            var drift = new float[3];
            int sel = 0;
            float time = 0, danger = 0, inzone = 0, rep = 0;
            bool done = false;

            float Rnd() => api.Rand(10000) / 10000f;

            void Render()
            {
                canvas.Clear(Bg);
                for (int i = 0; i < 3; i++)
                {
                    int y = Top + i * RowH;
                    canvas.DrawText(10, y + 4, labels[i], i == sel ? LabelSel : LabelDim, 2);
                    canvas.FillRect(BarX, y, BarW, BarH, BarBg);
                    canvas.FillRect(BarX + (int)(lo[i] * BarW), y, (int)((hi[i] - lo[i]) * BarW), BarH, Zone);
                    bool ok = val[i] >= lo[i] && val[i] <= hi[i];
                    canvas.FillRect(BarX, y, (int)(val[i] * BarW), BarH, ok ? Ok : Bad);
                }

                int sy = Top + 3 * RowH;
                canvas.DrawText(10, sy + 4, labels[3], LabelDim, 2);
                canvas.FillRect(BarX, sy, BarW, 14, BarBg);
                canvas.FillRect(BarX, sy, (int)(Math.Min(1f, inzone / 60f) * BarW), 14, Stab);
            }

            void Reset()
            {
                sel = 0;
                time = 0;
                danger = 0;
                inzone = 0;
                rep = 0;
                done = false;
                for (int i = 0; i < 3; i++)
                {
                    val[i] = 0.5f;
                    float c = 0.3f + Rnd() * 0.4f;
                    lo[i] = c - 0.11f;
                    hi[i] = c + 0.11f;
                    drift[i] = (Rnd() - 0.5f) * 0.06f;
                }

                api.Hud("stable", "0%");
                Render();
            }

            api.Bind(MinigameAction.Left, () => { sel = (sel + 2) % 3; });
            api.Bind(MinigameAction.Right, () => { sel = (sel + 1) % 3; });

            api.Loop(dt =>
            {
                if (done)
                {
                    return;
                }

                time += dt;
                rep += dt;
                if (rep > 0.05f)
                {
                    rep = 0;
                    if (api.Held(MinigameAction.Up))
                    {
                        val[sel] = Math.Min(1, val[sel] + 0.025f);
                    }

                    if (api.Held(MinigameAction.Down))
                    {
                        val[sel] = Math.Max(0, val[sel] - 0.025f);
                    }
                }

                bool allok = true;
                for (int i = 0; i < 3; i++)
                {
                    val[i] = Math.Max(0, Math.Min(1, val[i] + drift[i] * dt + (Rnd() - 0.5f) * 0.004f));
                    if (Rnd() < 0.01f)
                    {
                        drift[i] = (Rnd() - 0.5f) * 0.08f;
                    }

                    if (!(val[i] >= lo[i] && val[i] <= hi[i]))
                    {
                        allok = false;
                    }
                }

                if (allok)
                {
                    inzone += dt;
                    danger = Math.Max(0, danger - dt * 1.5f);
                }
                else
                {
                    danger += dt;
                }

                api.Hud("stable", (int)Math.Round(Math.Min(1f, time / 60f) * 100) + "%");
                Render();

                if (danger > 6)
                {
                    done = true;
                    api.Fail((int)Math.Round(inzone * 20));
                }
                else if (time >= 60)
                {
                    done = true;
                    float frac = inzone / time;
                    int rating = frac > 0.85f ? 3 : frac > 0.6f ? 2 : 1;
                    api.Complete((int)Math.Round(inzone * 30), rating);
                }
            });

            return new MinigameController { Start = Reset };
        }
    }
}
