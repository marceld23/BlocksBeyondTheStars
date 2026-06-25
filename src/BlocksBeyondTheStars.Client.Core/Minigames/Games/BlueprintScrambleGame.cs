// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Minigames.Games
{
    /// <summary>Port of <c>web/minigames/blueprint_scramble</c>: a procedurally-drawn schematic is split into a 4×4
    /// grid of fragments, each randomly turned; click a fragment to rotate it 90° until every fragment's bright
    /// corner marker sits top-left (all rotations back to 0). The web build rotated sub-images of an offscreen
    /// canvas; here the source is a <see cref="Canvas2D"/> and rotation is an exact quarter-turn pixel blit.</summary>
    public sealed class BlueprintScrambleGame : IMinigame
    {
        private const int Sz = 4, Tile = 92, W = Sz * Tile;

        private static readonly Rgba Paper = Rgba.Rgb(8, 20, 38);
        private static readonly Rgba Ink = Rgba.Rgb(70, 214, 255);
        private static readonly Rgba Accent = Rgba.Rgb(124, 255, 176);
        private static readonly Rgba CellEdge = new Rgba(70, 214, 255, 64);

        public string Key => "blueprint_scramble";
        public LocText Title => new LocText("Blueprint Scramble", "Blaupausen-Puzzle");
        public LocText Desc => new LocText(
            "A salvaged blueprint came in scrambled. Rotate each fragment until the schematic is whole again.",
            "Eine geborgene Blaupause ist verdreht. Drehe jedes Fragment, bis das Schema wieder stimmt.");
        public LocText Hint => new LocText("Click a fragment to rotate it (corner marker belongs top-left)", "Fragment anklicken zum Drehen (Eck-Marke gehört oben links)");
        public IReadOnlyList<LocText> Help { get; } = new[]
        {
            new LocText("Click a fragment to rotate it 90°", "Fragment anklicken, um es um 90° zu drehen"),
            new LocText("When every fragment's bright corner marker sits top-left, the blueprint is solved", "Wenn die helle Eck-Marke jedes Fragments oben links sitzt, ist die Blaupause gelöst"),
        };
        public int Difficulty => 2;

        public MinigameController Create(MinigameApi api)
        {
            var canvas = api.Canvas(W, W);
            var source = new Canvas2D(W, W);
            var rot = new int[Sz * Sz];
            int moves = 0;
            bool done = false;

            void PaintBlueprint()
            {
                source.Clear(Paper);
                for (int i = 0; i < 7; i++)
                {
                    source.DrawLine(api.Rand(W), api.Rand(W), api.Rand(W), api.Rand(W), Ink);
                }

                for (int j = 0; j < 6; j++)
                {
                    source.DrawCircle(api.Rand(W), api.Rand(W), 8 + api.Rand(20), Ink);
                }

                source.DrawLine(10, 10, W - 10, W - 14, Accent);
                for (int c = 0; c < Sz; c++)
                {
                    for (int r = 0; r < Sz; r++)
                    {
                        source.FillCircle(c * Tile + 12, r * Tile + 12, 5, Accent);
                    }
                }
            }

            // Exact quarter-turn source coordinate for dest (x,y) within a tile.
            static (int sx, int sy) Unrotate(int x, int y, int q) => q switch
            {
                1 => (y, Tile - 1 - x),
                2 => (Tile - 1 - x, Tile - 1 - y),
                3 => (Tile - 1 - y, x),
                _ => (x, y),
            };

            void Draw()
            {
                canvas.Clear(Paper);
                for (int c = 0; c < Sz; c++)
                {
                    for (int r = 0; r < Sz; r++)
                    {
                        int q = rot[r * Sz + c];
                        for (int y = 0; y < Tile; y++)
                        {
                            for (int x = 0; x < Tile; x++)
                            {
                                var (sx, sy) = Unrotate(x, y, q);
                                int si = ((r * Tile + sy) * W + (c * Tile + sx)) * 4;
                                canvas.SetPixel(c * Tile + x, r * Tile + y, new Rgba(source.Rgba[si], source.Rgba[si + 1], source.Rgba[si + 2], source.Rgba[si + 3]));
                            }
                        }

                        canvas.DrawRect(c * Tile, r * Tile, Tile, Tile, CellEdge);
                    }
                }
            }

            void Reset()
            {
                PaintBlueprint();
                moves = 0;
                done = false;
                for (int i = 0; i < rot.Length; i++)
                {
                    rot[i] = 1 + api.Rand(3); // never start solved
                }

                api.Hud("moves", 0);
                Draw();
            }

            api.Pointer(p =>
            {
                if (p.Phase != PointerPhase.Down || done)
                {
                    return;
                }

                int c = (int)(p.X / Tile), r = (int)(p.Y / Tile);
                if (c < 0 || r < 0 || c >= Sz || r >= Sz)
                {
                    return;
                }

                int idx = r * Sz + c;
                rot[idx] = (rot[idx] + 1) % 4;
                moves++;
                api.Hud("moves", moves);
                Draw();

                if (AllZero(rot))
                {
                    done = true;
                    int rating = moves <= Sz * Sz * 1.5 ? 3 : moves <= Sz * Sz * 3 ? 2 : 1;
                    api.After(() => api.Complete(System.Math.Max(50, 800 - moves * 12), rating), 0.25f);
                }
            });

            return new MinigameController { Start = Reset };
        }

        private static bool AllZero(int[] a)
        {
            foreach (int v in a)
            {
                if (v != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
