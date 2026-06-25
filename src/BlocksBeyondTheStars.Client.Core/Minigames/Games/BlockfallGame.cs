// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Minigames.Games
{
    /// <summary>Port of <c>web/minigames/blockfall</c>: the falling-tetromino classic — slot pieces to clear full
    /// rows, it ends when a new piece can't spawn.</summary>
    public sealed class BlockfallGame : IMinigame
    {
        private const int Cols = 10, Rows = 20, Cell = 22;
        private const int W = Cols * Cell, H = Rows * Cell;

        private static readonly Rgba Bg = Rgba.Rgb(7, 13, 22);
        private static readonly Rgba GridCol = new Rgba(70, 214, 255, 20);

        private static readonly int[][][] Shapes =
        {
            new[] { new[] { 1, 1, 1, 1 } },
            new[] { new[] { 1, 1 }, new[] { 1, 1 } },
            new[] { new[] { 0, 1, 0 }, new[] { 1, 1, 1 } },
            new[] { new[] { 1, 0, 0 }, new[] { 1, 1, 1 } },
            new[] { new[] { 0, 0, 1 }, new[] { 1, 1, 1 } },
            new[] { new[] { 0, 1, 1 }, new[] { 1, 1, 0 } },
            new[] { new[] { 1, 1, 0 }, new[] { 0, 1, 1 } },
        };

        private static readonly Rgba[] Colors =
        {
            Rgba.Rgb(70, 214, 255), Rgba.Rgb(124, 255, 176), Rgba.Rgb(255, 192, 77), Rgba.Rgb(255, 138, 92),
            Rgba.Rgb(185, 140, 255), Rgba.Rgb(90, 209, 196), Rgba.Rgb(255, 111, 174),
        };

        public string Key => "blockfall";
        public LocText Title => new LocText("Blockfall", "Blockfall");
        public LocText Desc => new LocText(
            "Slot the falling blocks to clear full rows. Survive as long as you can.",
            "Setze die fallenden Blöcke so, dass volle Reihen verschwinden. Überlebe so lange wie möglich.");
        public LocText Hint => new LocText("← → move · ↑ rotate · ↓ soft drop · Space hard drop", "← → bewegen · ↑ drehen · ↓ sanft · Leertaste fallen");
        public IReadOnlyList<LocText> Help { get; } = new[]
        {
            new LocText("← → : move the piece", "← → : Stein bewegen"),
            new LocText("↑ : rotate", "↑ : drehen"),
            new LocText("↓ : soft drop", "↓ : schneller"),
            new LocText("Space : hard drop", "Leertaste : sofort fallen"),
        };
        public int Difficulty => 2;

        public MinigameController Create(MinigameApi api)
        {
            var canvas = api.Canvas(W, H);

            var grid = new int[Rows][];
            int[][] piece = System.Array.Empty<int[]>();
            int pcol = 0, px = 0, py = 0, score = 0, lines = 0;
            float fall = 0.8f, fallAcc = 0f, repAcc = 0f;
            bool over = false;

            bool Collide(int[][] p, int ox, int oy)
            {
                for (int r = 0; r < p.Length; r++)
                {
                    for (int cc = 0; cc < p[r].Length; cc++)
                    {
                        if (p[r][cc] == 0)
                        {
                            continue;
                        }

                        int x = ox + cc, y = oy + r;
                        if (x < 0 || x >= Cols || y >= Rows || (y >= 0 && grid[y][x] != 0))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            void Spawn()
            {
                int si = api.Rand(Shapes.Length);
                piece = CloneShape(Shapes[si]);
                pcol = si;
                px = 3;
                py = 0;
                if (Collide(piece, px, py))
                {
                    over = true;
                }
            }

            void Merge()
            {
                for (int r = 0; r < piece.Length; r++)
                {
                    for (int cc = 0; cc < piece[r].Length; cc++)
                    {
                        if (piece[r][cc] != 0)
                        {
                            int y = py + r;
                            if (y >= 0)
                            {
                                grid[y][px + cc] = pcol + 1;
                            }
                        }
                    }
                }

                int cleared = 0;
                for (int rr = Rows - 1; rr >= 0; rr--)
                {
                    if (RowFull(grid[rr]))
                    {
                        for (int y = rr; y > 0; y--)
                        {
                            grid[y] = grid[y - 1];
                        }

                        grid[0] = new int[Cols];
                        cleared++;
                        rr++;
                    }
                }

                if (cleared > 0)
                {
                    lines += cleared;
                    score += new[] { 0, 100, 300, 600, 1000 }[cleared];
                    fall = System.Math.Max(0.12f, 0.8f - lines * 0.02f);
                    api.Hud("score", score);
                    api.Hud("lines", lines);
                }

                Spawn();
            }

            void Rotate()
            {
                int n = piece.Length, m = piece[0].Length;
                var res = new int[m][];
                for (int x = 0; x < m; x++)
                {
                    res[x] = new int[n];
                    for (int y = n - 1; y >= 0; y--)
                    {
                        res[x][n - 1 - y] = piece[y][x];
                    }
                }

                if (!Collide(res, px, py))
                {
                    piece = res;
                }
            }

            void Move(int dx)
            {
                if (!Collide(piece, px + dx, py))
                {
                    px += dx;
                }
            }

            void SoftDrop()
            {
                if (!Collide(piece, px, py + 1))
                {
                    py++;
                }
                else
                {
                    Merge();
                }
            }

            void Reset()
            {
                for (int r = 0; r < Rows; r++)
                {
                    grid[r] = new int[Cols];
                }

                score = 0;
                lines = 0;
                fall = 0.8f;
                fallAcc = 0f;
                repAcc = 0f;
                over = false;
                Spawn();
                api.Hud("score", 0);
                api.Hud("lines", 0);
            }

            api.Bind(MinigameAction.Up, Rotate);
            api.Bind(MinigameAction.Left, () => Move(-1));
            api.Bind(MinigameAction.Right, () => Move(1));
            api.Bind(MinigameAction.Primary, () =>
            {
                while (!Collide(piece, px, py + 1))
                {
                    py++;
                }

                Merge();
            });

            api.Loop(dt =>
            {
                if (over)
                {
                    int rating = score >= 2500 ? 3 : score >= 1000 ? 2 : 1;
                    if (score > 0)
                    {
                        api.Complete(score, rating);
                    }
                    else
                    {
                        api.Fail(0);
                    }

                    return;
                }

                repAcc += dt;
                if (repAcc > 0.09f)
                {
                    repAcc = 0f;
                    if (api.Held(MinigameAction.Left))
                    {
                        Move(-1);
                    }

                    if (api.Held(MinigameAction.Right))
                    {
                        Move(1);
                    }

                    if (api.Held(MinigameAction.Down))
                    {
                        SoftDrop();
                    }
                }

                fallAcc += dt;
                if (fallAcc >= fall)
                {
                    fallAcc = 0f;
                    SoftDrop();
                }

                Draw();
            });

            void DrawCell(int x, int y, Rgba col) => canvas.FillRect(x * Cell + 1, y * Cell + 1, Cell - 2, Cell - 2, col);

            void Draw()
            {
                canvas.Clear(Bg);
                for (int i = 1; i < Cols; i++)
                {
                    canvas.DrawLine(i * Cell, 0, i * Cell, H - 1, GridCol);
                }

                for (int j = 1; j < Rows; j++)
                {
                    canvas.DrawLine(0, j * Cell, W - 1, j * Cell, GridCol);
                }

                for (int r = 0; r < Rows; r++)
                {
                    for (int cc = 0; cc < Cols; cc++)
                    {
                        if (grid[r][cc] != 0)
                        {
                            DrawCell(cc, r, Colors[grid[r][cc] - 1]);
                        }
                    }
                }

                for (int pr = 0; pr < piece.Length; pr++)
                {
                    for (int pc = 0; pc < piece[pr].Length; pc++)
                    {
                        if (piece[pr][pc] != 0)
                        {
                            DrawCell(px + pc, py + pr, Colors[pcol]);
                        }
                    }
                }
            }

            return new MinigameController { Start = Reset };
        }

        private static int[][] CloneShape(int[][] s)
        {
            var res = new int[s.Length][];
            for (int r = 0; r < s.Length; r++)
            {
                res[r] = (int[])s[r].Clone();
            }

            return res;
        }

        private static bool RowFull(int[] row)
        {
            foreach (int v in row)
            {
                if (v == 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
