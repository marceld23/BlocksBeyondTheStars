// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Minigames.Games
{
    /// <summary>Port of <c>web/minigames/data_fishing</c>: steer a collector to catch cyan packets (gold = bonus)
    /// and let red ones pass; three red hits ends it, reach the quota to win. First native-Arcade game alongside
    /// <see cref="CometCourierGame"/>, kept as the porting pattern — pure logic + <see cref="Canvas2D"/> drawing,
    /// so it is unit-tested without Unity.</summary>
    public sealed class DataFishingGame : IMinigame
    {
        private const int W = 640, H = 440;

        private static readonly Rgba Bg = Rgba.Rgb(7, 13, 22);
        private static readonly Rgba Good = Rgba.Rgb(70, 214, 255);
        private static readonly Rgba Bad = Rgba.Rgb(255, 107, 107);
        private static readonly Rgba Rare = Rgba.Rgb(255, 216, 107);
        private static readonly Rgba Cat = Rgba.Rgb(124, 255, 176);

        public string Key => "data_fishing";
        public LocText Title => new LocText("Data Fishing", "Daten-Fang");
        public LocText Desc => new LocText(
            "Catch the clean data packets streaming down and let the corrupted ones pass — recover enough before the integrity is lost.",
            "Fang die sauberen Datenpakete und lass die beschädigten durch — berge genug, bevor die Integrität verloren ist.");
        public LocText Hint => new LocText("← → / mouse move the collector · catch cyan, avoid red", "← → / Maus bewegen · cyan fangen, rot meiden");
        public IReadOnlyList<LocText> Help { get; } = new[]
        {
            new LocText("Move the collector to catch cyan packets (gold = bonus)", "Sammler bewegen, um cyan Pakete zu fangen (gold = Bonus)"),
            new LocText("Catching a red packet costs integrity — three losses ends it", "Ein rotes Paket kostet Integrität — drei Verluste beenden es"),
        };
        public int Difficulty => 2;

        public MinigameController Create(MinigameApi api)
        {
            var canvas = api.Canvas(W, H);

            float catX = 0f;
            const float catW = 90f;
            var pks = new List<Pk>();
            int integ = 0, prog = 0;
            const int need = 100;
            bool done = false;
            float spawn = 0f;

            void Reset()
            {
                catX = W / 2f;
                pks.Clear();
                integ = 3;
                prog = 0;
                done = false;
                spawn = 0f;
                api.Hud("data", "0/" + need);
                api.Hud("integrity", integ);
            }

            api.Pointer(p =>
            {
                if (p.Phase == PointerPhase.Move || p.Phase == PointerPhase.Down)
                {
                    catX = Clamp(p.X, catW / 2f, W - catW / 2f);
                }
            });

            api.Loop(dt =>
            {
                if (done)
                {
                    return;
                }

                if (api.Held(MinigameAction.Left))
                {
                    catX = System.Math.Max(catW / 2f, catX - 320f * dt);
                }

                if (api.Held(MinigameAction.Right))
                {
                    catX = System.Math.Min(W - catW / 2f, catX + 320f * dt);
                }

                spawn += dt;
                if (spawn > 0.55f)
                {
                    spawn = 0f;
                    int r = api.Rand(100);
                    int type = r < 62 ? 0 : r < 90 ? 1 : 2; // good / bad / rare
                    pks.Add(new Pk { X = 24f + api.Rand(W - 48), Y = -10f, Vy = 120f + api.Rand(90), Type = type });
                }

                for (int i = pks.Count - 1; i >= 0; i--)
                {
                    var pk = pks[i];
                    pk.Y += pk.Vy * dt;
                    pks[i] = pk;
                    if (pk.Y > H - 34 && pk.Y < H - 8 && System.Math.Abs(pk.X - catX) < catW / 2f)
                    {
                        if (pk.Type == 1)
                        {
                            integ--;
                            api.Hud("integrity", integ);
                            if (integ <= 0)
                            {
                                done = true;
                                api.Fail(prog);
                                return;
                            }
                        }
                        else
                        {
                            prog += pk.Type == 2 ? 18 : 8;
                            api.Hud("data", System.Math.Min(need, prog) + "/" + need);
                        }

                        pks.RemoveAt(i);
                        continue;
                    }

                    if (pk.Y > H + 12)
                    {
                        pks.RemoveAt(i);
                    }
                }

                if (prog >= need)
                {
                    done = true;
                    int rating = integ >= 3 ? 3 : integ == 2 ? 2 : 1;
                    api.Complete(prog * 4 + integ * 40, rating);
                    return;
                }

                Draw();
            });

            void Draw()
            {
                canvas.Clear(Bg);
                foreach (var pk in pks)
                {
                    var c = pk.Type == 1 ? Bad : pk.Type == 2 ? Rare : Good;
                    canvas.FillRect((int)(pk.X - 7), (int)(pk.Y - 7), 14, 14, c);
                }

                canvas.FillRect((int)(catX - catW / 2f), H - 22, (int)catW, 10, Cat);
                canvas.FillRect((int)(catX - catW / 2f), H - 12, (int)catW, 4, new Rgba(124, 255, 176, 89));
            }

            return new MinigameController { Start = Reset };
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        private struct Pk
        {
            public float X, Y, Vy;
            public int Type; // 0 good, 1 bad, 2 rare
        }
    }
}
