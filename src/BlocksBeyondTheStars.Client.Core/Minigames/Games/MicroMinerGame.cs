// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Minigames.Games
{
    /// <summary>Port of <c>web/minigames/micro_miner</c>: dig through rock for ore and haul it up to the surface to
    /// bank it; fill the depot quota before the limited energy runs out.</summary>
    public sealed class MicroMinerGame : IMinigame
    {
        private const int Cols = 16, Rows = 12, Cell = 34;
        private const int W = Cols * Cell, H = Rows * Cell, Quota = 8;

        private static readonly Rgba Bg = Rgba.Rgb(7, 13, 22);
        private static readonly Rgba Stone = new Rgba(70, 214, 255, 33);
        private static readonly Rgba Ore = new Rgba(124, 255, 176, 217);
        private static readonly Rgba Surface = new Rgba(124, 255, 176, 30);
        private static readonly Rgba Drone = Rgba.Rgb(70, 214, 255);

        public string Key => "micro_miner";
        public LocText Title => new LocText("Micro Miner", "Mikro-Miner");
        public LocText Desc => new LocText(
            "Dig for ore and haul it back to the surface to bank it. Fill the quota before your energy runs dry — you'll need several trips and a tight route.",
            "Grab nach Erz und bring es zur Oberfläche, um es einzuzahlen. Erfülle die Quote, bevor die Energie leer ist — du brauchst mehrere Fahrten und eine sparsame Route.");
        public LocText Hint => new LocText("← ↑ ↓ → dig / move · surface (top row) to bank · fill the DEPOT quota", "← ↑ ↓ → graben / bewegen · Oberfläche (oben) zahlt ein · DEPOT-Quote füllen");
        public IReadOnlyList<LocText> Help { get; } = new[]
        {
            new LocText("Move into rock to dig it (costs energy); bright cells are ore.", "In Gestein hineinbewegen, um zu graben (kostet Energie); helle Zellen sind Erz."),
            new LocText("Return to the top surface to bank your carried ore. Reach the DEPOT quota to win — energy is limited, so plan short trips.", "Kehre zur Oberfläche zurück, um getragenes Erz einzuzahlen. Erreiche die DEPOT-Quote zum Sieg — Energie ist begrenzt, plane kurze Wege."),
        };
        public int Difficulty => 2;

        public MinigameController Create(MinigameApi api)
        {
            var canvas = api.Canvas(W, H);

            var cellv = new int[Cols * Rows]; // 0 empty, 1 stone, 2 ore
            int droneX = 0, droneY = 0;
            int energy = 0, carry = 0, banked = 0;
            bool done = false;
            float repAcc = 0f;

            int K(int x, int y) => y * Cols + x;

            void Draw()
            {
                canvas.Clear(Bg);
                for (int y = 0; y < Rows; y++)
                {
                    for (int x = 0; x < Cols; x++)
                    {
                        int v = cellv[K(x, y)];
                        if (v == 1)
                        {
                            canvas.FillRect(x * Cell + 1, y * Cell + 1, Cell - 2, Cell - 2, Stone);
                        }
                        else if (v == 2)
                        {
                            canvas.FillRect(x * Cell + 6, y * Cell + 6, Cell - 12, Cell - 12, Ore);
                        }
                    }
                }

                canvas.FillRect(0, 0, W, Cell, Surface);
                canvas.FillRect(droneX * Cell + 5, droneY * Cell + 5, Cell - 10, Cell - 10, Drone);
            }

            void Reset()
            {
                energy = 90;
                carry = 0;
                banked = 0;
                done = false;
                repAcc = 0f;
                for (int y = 0; y < Rows; y++)
                {
                    for (int x = 0; x < Cols; x++)
                    {
                        cellv[K(x, y)] = y == 0 ? 0 : (api.Rand(100) < 20 ? 2 : 1);
                    }
                }

                droneX = Cols / 2;
                droneY = 0;
                api.Hud("energy", energy);
                api.Hud("ore", 0);
                api.Hud("depot", "0/" + Quota);
                Draw();
            }

            void Move(int dx, int dy)
            {
                if (done)
                {
                    return;
                }

                int nx = droneX + dx, ny = droneY + dy;
                if (nx < 0 || ny < 0 || nx >= Cols || ny >= Rows)
                {
                    return;
                }

                int v = cellv[K(nx, ny)];
                if (v == 1)
                {
                    energy -= 2;
                    cellv[K(nx, ny)] = 0;
                }
                else if (v == 2)
                {
                    energy -= 3;
                    cellv[K(nx, ny)] = 0;
                    carry += 1;
                    api.Hud("ore", carry);
                }
                else
                {
                    energy -= 1;
                }

                droneX = nx;
                droneY = ny;
                api.Hud("energy", System.Math.Max(0, energy));

                if (ny == 0 && carry > 0)
                {
                    banked += carry;
                    carry = 0;
                    api.Hud("ore", 0);
                    api.Hud("depot", banked + "/" + Quota);
                }

                if (banked >= Quota)
                {
                    done = true;
                    int rating = banked >= 14 ? 3 : banked >= 10 ? 2 : 1;
                    api.Complete(banked * 30, rating);
                    return;
                }

                if (energy <= 0)
                {
                    done = true;
                    api.Fail(banked * 10);
                    return;
                }

                Draw();
            }

            api.Bind(MinigameAction.Left, () => Move(-1, 0));
            api.Bind(MinigameAction.Right, () => Move(1, 0));
            api.Bind(MinigameAction.Up, () => Move(0, -1));
            api.Bind(MinigameAction.Down, () => Move(0, 1));

            api.Loop(dt =>
            {
                repAcc += dt;
                if (repAcc > 0.12f)
                {
                    repAcc = 0f;
                    if (api.Held(MinigameAction.Left))
                    {
                        Move(-1, 0);
                    }
                    else if (api.Held(MinigameAction.Right))
                    {
                        Move(1, 0);
                    }
                    else if (api.Held(MinigameAction.Up))
                    {
                        Move(0, -1);
                    }
                    else if (api.Held(MinigameAction.Down))
                    {
                        Move(0, 1);
                    }
                }
            });

            return new MinigameController { Start = Reset };
        }
    }
}
