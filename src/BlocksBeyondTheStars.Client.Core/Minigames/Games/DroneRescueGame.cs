using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Minigames.Games
{
    /// <summary>Port of <c>web/minigames/drone_rescue</c>: pilot a repair drone around a walled grid, refuel on
    /// blue energy cells, repair every orange node, then reach the green exit before energy runs out.</summary>
    public sealed class DroneRescueGame : IMinigame
    {
        private const int Cols = 14, Rows = 10, Cell = 40, W = Cols * Cell, H = Rows * Cell;

        private static readonly Rgba Bg = Rgba.Rgb(7, 13, 22);
        private static readonly Rgba Wall = new Rgba(70, 214, 255, 46);
        private static readonly Rgba CellCol = Rgba.Rgb(70, 214, 255);
        private static readonly Rgba Repair = Rgba.Rgb(255, 192, 77);
        private static readonly Rgba Exit = Rgba.Rgb(124, 255, 176);
        private static readonly Rgba ExitDim = new Rgba(124, 255, 176, 64);
        private static readonly Rgba Drone = Rgba.Rgb(124, 255, 176);

        public string Key => "drone_rescue";
        public LocText Title => new LocText("Drone Rescue", "Drohnen-Rettung");
        public LocText Desc => new LocText(
            "Pilot the repair drone to every damaged node, then reach the extraction zone — before energy runs out.",
            "Steuere die Reparaturdrohne zu allen beschädigten Knoten und erreiche dann die Extraktionszone — bevor die Energie ausgeht.");
        public LocText Hint => new LocText("← ↑ ↓ → move (collect energy cells, repair nodes, reach exit)", "← ↑ ↓ → bewegen (Energie sammeln, Knoten reparieren, Ausgang erreichen)");
        public IReadOnlyList<LocText> Help { get; } = new[]
        {
            new LocText("Arrow keys / WASD move the drone (costs energy)", "Pfeile / WASD bewegen die Drohne (kostet Energie)"),
            new LocText("Step on blue energy cells to refuel, orange nodes to repair", "Auf blaue Energiezellen treten zum Tanken, orange Knoten zum Reparieren"),
            new LocText("Repair all nodes, then reach the green exit", "Alle Knoten reparieren, dann zum grünen Ausgang"),
        };
        public int Difficulty => 3;

        public MinigameController Create(MinigameApi api)
        {
            var canvas = api.Canvas(W, H);

            var wall = new HashSet<int>();
            var cells = new HashSet<int>();
            var repairs = new HashSet<int>();
            int droneX = 0, droneY = 0, energy = 0;
            const int total = 4;
            (int x, int y) exitC = (0, 0);
            bool done = false;
            float repAcc = 0f;

            int K(int x, int y) => y * Cols + x;
            int RepairsLeft()
            {
                int n = 0;
                foreach (int k in repairs)
                {
                    n++;
                }

                return n;
            }

            void Draw()
            {
                canvas.Clear(Bg);
                int left = RepairsLeft();
                for (int x = 0; x < Cols; x++)
                {
                    for (int y = 0; y < Rows; y++)
                    {
                        int k = K(x, y);
                        if (wall.Contains(k))
                        {
                            canvas.FillRect(x * Cell + 1, y * Cell + 1, Cell - 2, Cell - 2, Wall);
                        }
                        else if (cells.Contains(k))
                        {
                            canvas.FillCircle(x * Cell + Cell / 2, y * Cell + Cell / 2, 7, CellCol);
                        }
                        else if (repairs.Contains(k))
                        {
                            canvas.FillRect(x * Cell + 9, y * Cell + 9, Cell - 18, Cell - 18, Repair);
                        }
                    }
                }

                int ex = exitC.x * Cell, ey = exitC.y * Cell;
                canvas.FillRect(ex + 6, ey + 6, Cell - 12, Cell - 12, left == 0 ? Exit : ExitDim);
                canvas.FillRect(droneX * Cell + 8, droneY * Cell + 8, Cell - 16, Cell - 16, Drone);
            }

            void Reset()
            {
                wall.Clear();
                cells.Clear();
                repairs.Clear();
                done = false;
                repAcc = 0f;
                energy = 100;

                for (int x = 0; x < Cols; x++)
                {
                    for (int y = 0; y < Rows; y++)
                    {
                        if (x == 0 || y == 0 || x == Cols - 1 || y == Rows - 1)
                        {
                            wall.Add(K(x, y));
                        }
                    }
                }

                for (int i = 0; i < 16; i++)
                {
                    int wx = 1 + api.Rand(Cols - 2), wy = 1 + api.Rand(Rows - 2);
                    if (!(wx == 1 && wy == 1))
                    {
                        wall.Add(K(wx, wy));
                    }
                }

                droneX = 1;
                droneY = 1;
                wall.Remove(K(1, 1));

                var taken = new HashSet<int>();
                void Place(HashSet<int> map, int n)
                {
                    int c = 0;
                    while (c < n)
                    {
                        int x = 1 + api.Rand(Cols - 2), y = 1 + api.Rand(Rows - 2), k = K(x, y);
                        if (wall.Contains(k) || taken.Contains(k) || (x == 1 && y == 1))
                        {
                            continue;
                        }

                        map.Add(k);
                        taken.Add(k);
                        c++;
                    }
                }

                Place(repairs, total);
                Place(cells, 6);
                exitC = (Cols - 2, Rows - 2);
                wall.Remove(K(exitC.x, exitC.y));

                api.Hud("energy", energy);
                api.Hud("repaired", "0/" + total);
                Draw();
            }

            void Step(int dx, int dy)
            {
                if (done)
                {
                    return;
                }

                int nx = droneX + dx, ny = droneY + dy, k = K(nx, ny);
                if (nx < 0 || ny < 0 || nx >= Cols || ny >= Rows || wall.Contains(k))
                {
                    return;
                }

                droneX = nx;
                droneY = ny;
                energy -= 1;
                if (cells.Remove(k))
                {
                    energy = System.Math.Min(150, energy + 25);
                }

                if (repairs.Remove(k))
                {
                    energy -= 4;
                }

                int left = RepairsLeft();
                api.Hud("energy", System.Math.Max(0, energy));
                api.Hud("repaired", (total - left) + "/" + total);

                if (energy <= 0)
                {
                    done = true;
                    api.Fail((total - left) * 50);
                    return;
                }

                if (left == 0 && nx == exitC.x && ny == exitC.y)
                {
                    done = true;
                    int rating = energy > 60 ? 3 : energy > 25 ? 2 : 1;
                    api.Complete(System.Math.Max(50, 300 + energy * 4), rating);
                    return;
                }

                Draw();
            }

            api.Bind(MinigameAction.Left, () => Step(-1, 0));
            api.Bind(MinigameAction.Right, () => Step(1, 0));
            api.Bind(MinigameAction.Up, () => Step(0, -1));
            api.Bind(MinigameAction.Down, () => Step(0, 1));

            api.Loop(dt =>
            {
                repAcc += dt;
                if (repAcc > 0.12f)
                {
                    repAcc = 0f;
                    if (api.Held(MinigameAction.Left))
                    {
                        Step(-1, 0);
                    }
                    else if (api.Held(MinigameAction.Right))
                    {
                        Step(1, 0);
                    }
                    else if (api.Held(MinigameAction.Up))
                    {
                        Step(0, -1);
                    }
                    else if (api.Held(MinigameAction.Down))
                    {
                        Step(0, 1);
                    }
                }
            });

            return new MinigameController { Start = Reset };
        }
    }
}
