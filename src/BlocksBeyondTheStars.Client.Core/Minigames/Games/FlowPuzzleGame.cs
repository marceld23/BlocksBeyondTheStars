using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Minigames.Games
{
    /// <summary>Port of <c>web/minigames/_shared/flowpuzzle.js</c> — the rotate-the-tiles flow puzzle shared by
    /// Circuit Weaver (energy) and Oxygen Loop (life support). A guaranteed source→receiver path is generated,
    /// every tile is randomly rotated, and the player clicks tiles to rotate until flow reaches the receiver.
    /// Abstract base; the two games are thin subclasses differing only in size/colour/text.</summary>
    public abstract class FlowPuzzleGame : IMinigame
    {
        private const int N = 1, E = 2, S = 4, Wd = 8;
        private const int Cell = 64;

        private static readonly Rgba Bg = Rgba.Rgb(7, 13, 22);
        private static readonly Rgba GridCol = new Rgba(70, 214, 255, 26);
        private static readonly Rgba PipeOff = new Rgba(70, 214, 255, 90);
        private static readonly Rgba DstOff = Rgba.Rgb(119, 144, 160);

        private readonly int _size;
        private readonly Rgba _glow;

        protected FlowPuzzleGame(string key, LocText title, LocText desc, int difficulty, int size, Rgba glow)
        {
            Key = key;
            Title = title;
            Desc = desc;
            Difficulty = difficulty;
            _size = size;
            _glow = glow;
        }

        public string Key { get; }
        public LocText Title { get; }
        public LocText Desc { get; }
        public int Difficulty { get; }
        public LocText Hint => new LocText("Click a tile to rotate its lines · light up the path to the receiver", "Kachel anklicken zum Drehen · Pfad zum Empfänger erleuchten");
        public IReadOnlyList<LocText> Help => new[]
        {
            Desc,
            new LocText("Click any tile to rotate its lines 90°", "Beliebige Kachel anklicken, dreht ihre Leitungen um 90°"),
            new LocText("When a connected path glows from source to receiver, it's solved — fewer rotations score higher", "Wenn ein verbundener Pfad von der Quelle zum Empfänger leuchtet, ist es gelöst — weniger Drehungen geben mehr Punkte"),
        };

        private static int Opp(int d) => d switch { N => S, S => N, E => Wd, _ => E };
        private static int Rot(int m) => ((m << 1) | (m >> 3)) & 15;
        private static (int dx, int dy) Delta(int d) => d switch { N => (0, -1), E => (1, 0), S => (0, 1), _ => (-1, 0) };

        public MinigameController Create(MinigameApi api)
        {
            int sz = _size;
            int w = sz * Cell;
            var canvas = api.Canvas(w, w);

            var mask = new int[sz * sz];
            (int x, int y) src = (0, 0), dst = (0, 0);
            int rots = 0;
            bool done = false;

            HashSet<int> Powered()
            {
                var seen = new HashSet<int> { src.y * sz + src.x };
                var stack = new Stack<int>();
                stack.Push(src.y * sz + src.x);
                while (stack.Count > 0)
                {
                    int idx = stack.Pop();
                    int cx = idx % sz, cy = idx / sz, m = mask[idx];
                    foreach (int d in new[] { N, E, S, Wd })
                    {
                        if ((m & d) == 0)
                        {
                            continue;
                        }

                        var (dx, dy) = Delta(d);
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= sz || ny >= sz)
                        {
                            continue;
                        }

                        int ni = ny * sz + nx;
                        if ((mask[ni] & Opp(d)) != 0 && seen.Add(ni))
                        {
                            stack.Push(ni);
                        }
                    }
                }

                return seen;
            }

            void Draw(HashSet<int> seen)
            {
                canvas.Clear(Bg);
                for (int i = 0; i < sz * sz; i++)
                {
                    int cx = i % sz, cy = i / sz, m = mask[i];
                    bool on = seen.Contains(i);
                    int px = cx * Cell + Cell / 2, py = cy * Cell + Cell / 2;
                    canvas.DrawRect(cx * Cell, cy * Cell, Cell, Cell, GridCol);
                    var col = on ? _glow : PipeOff;
                    foreach (int d in new[] { N, E, S, Wd })
                    {
                        if ((m & d) == 0)
                        {
                            continue;
                        }

                        var (dx, dy) = Delta(d);
                        // A 5px pipe bar from the tile centre toward edge d.
                        if (dx != 0)
                        {
                            int x0 = dx > 0 ? px : px - Cell / 2;
                            canvas.FillRect(x0, py - 2, Cell / 2, 5, col);
                        }
                        else
                        {
                            int y0 = dy > 0 ? py : py - Cell / 2;
                            canvas.FillRect(px - 2, y0, 5, Cell / 2, col);
                        }
                    }
                }

                canvas.FillCircle(src.x * Cell + Cell / 2, src.y * Cell + Cell / 2, 10, _glow);
                bool dp = seen.Contains(dst.y * sz + dst.x);
                canvas.DrawCircle(dst.x * Cell + Cell / 2, dst.y * Cell + Cell / 2, 11, dp ? _glow : DstOff);
            }

            void Reset()
            {
                for (int i = 0; i < mask.Length; i++)
                {
                    mask[i] = 0;
                }

                rots = 0;
                done = false;
                api.Hud("rotations", 0);

                var y = new int[sz];
                for (int x = 0; x < sz; x++)
                {
                    y[x] = api.Rand(sz);
                }

                var path = new List<(int x, int y)>();
                for (int x = 0; x < sz; x++)
                {
                    int er = x == 0 ? y[0] : y[x - 1];
                    int step = er <= y[x] ? 1 : -1;
                    for (int yy = er; ; yy += step)
                    {
                        path.Add((x, yy));
                        if (yy == y[x])
                        {
                            break;
                        }
                    }
                }

                for (int k = 0; k < path.Count - 1; k++)
                {
                    var a = path[k];
                    var b = path[k + 1];
                    int dx = b.x - a.x, dy = b.y - a.y;
                    int d = dx == 1 ? E : dx == -1 ? Wd : dy == 1 ? S : N;
                    mask[a.y * sz + a.x] |= d;
                    mask[b.y * sz + b.x] |= Opp(d);
                }

                src = path[0];
                dst = path[path.Count - 1];

                int[] decoy = { 3, 6, 12, 9, 5, 10, 7, 14, 13, 11 };
                for (int i = 0; i < sz * sz; i++)
                {
                    if (mask[i] == 0)
                    {
                        mask[i] = decoy[api.Rand(decoy.Length)];
                    }
                }

                for (int i = 0; i < sz * sz; i++)
                {
                    int rr = api.Rand(4);
                    for (int t = 0; t < rr; t++)
                    {
                        mask[i] = Rot(mask[i]);
                    }
                }

                Draw(Powered());
            }

            api.Pointer(p =>
            {
                if (p.Phase != PointerPhase.Down || done)
                {
                    return;
                }

                int cx = (int)(p.X / Cell), cy = (int)(p.Y / Cell);
                if (cx < 0 || cy < 0 || cx >= sz || cy >= sz)
                {
                    return;
                }

                mask[cy * sz + cx] = Rot(mask[cy * sz + cx]);
                rots++;
                api.Hud("rotations", rots);
                var seen = Powered();
                Draw(seen);
                if (seen.Contains(dst.y * sz + dst.x))
                {
                    done = true;
                    int rating = rots <= sz * 1.5 ? 3 : rots <= sz * 3 ? 2 : 1;
                    api.After(() => api.Complete(System.Math.Max(50, 600 - rots * 15), rating), 0.25f);
                }
            });

            return new MinigameController { Start = Reset };
        }
    }

    /// <summary>Circuit Weaver — the energy-conduit flow puzzle.</summary>
    public sealed class CircuitWeaverGame : FlowPuzzleGame
    {
        public CircuitWeaverGame()
            : base("circuit_weaver", new LocText("Circuit Weaver", "Circuit Weaver"),
                new LocText("Rotate the conduit tiles so energy flows from the generator to the receiver.",
                    "Drehe die Leitungskacheln, bis Energie vom Generator zum Empfänger fließt."),
                2, 6, Rgba.Rgb(124, 255, 176))
        {
        }
    }

    /// <summary>Oxygen Loop — the life-support pipe flow puzzle (larger grid).</summary>
    public sealed class OxygenLoopGame : FlowPuzzleGame
    {
        public OxygenLoopGame()
            : base("oxygen_loop", new LocText("Oxygen Loop", "Sauerstoff-Kreislauf"),
                new LocText("Rotate the pipe tiles so oxygen flows from the life-support core to the habitat.",
                    "Drehe die Rohrkacheln, bis Sauerstoff vom Lebenserhaltungskern zum Habitat fließt."),
                3, 7, Rgba.Rgb(124, 255, 176))
        {
        }
    }
}
