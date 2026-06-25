using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Minigames.Games
{
    /// <summary>Port of <c>web/minigames/nanobot_repair</c>: route a nanobot across a connected node graph,
    /// clicking a linked node to hop there (energy by distance), to repair every orange damaged node before the
    /// energy runs out. The graph is a random spanning tree (always solvable) enriched with each node's two
    /// nearest neighbours.</summary>
    public sealed class NanobotRepairGame : IMinigame
    {
        private const int W = 660, H = 440, N = 9;

        private static readonly Rgba Bg = Rgba.Rgb(7, 13, 22);
        private static readonly Rgba Edge = new Rgba(70, 214, 255, 56);
        private static readonly Rgba Reach = Rgba.Rgb(70, 214, 255);
        private static readonly Rgba NodeDim = new Rgba(70, 214, 255, 102);
        private static readonly Rgba Damaged = Rgba.Rgb(255, 192, 77);
        private static readonly Rgba Fixed = Rgba.Rgb(124, 255, 176);
        private static readonly Rgba Bot = Rgba.White;

        public string Key => "nanobot_repair";
        public LocText Title => new LocText("Nanobot Repair", "Nanobot-Reparatur");
        public LocText Desc => new LocText(
            "Route the nanobot across the circuit graph to repair every damaged node before its energy runs out. Plan the shortest path.",
            "Leite den Nanobot über den Schaltungsgraphen, um jeden beschädigten Knoten zu reparieren, bevor die Energie ausgeht. Plane den kürzesten Weg.");
        public LocText Hint => new LocText("Click a connected node to move there (costs energy)", "Verbundenen Knoten anklicken zum Hinbewegen (kostet Energie)");
        public IReadOnlyList<LocText> Help { get; } = new[]
        {
            new LocText("Click a node linked to the bot to move there; moving costs energy by distance", "Mit dem Bot verbundenen Knoten anklicken; Bewegung kostet Energie je Distanz"),
            new LocText("Reach every orange damaged node to repair it", "Erreiche jeden orangen beschädigten Knoten zur Reparatur"),
        };
        public int Difficulty => 3;

        private struct Node
        {
            public float X, Y;
            public bool Repair, Fixed;
        }

        public MinigameController Create(MinigameApi api)
        {
            var canvas = api.Canvas(W, H);
            var nodes = new Node[N];
            var edges = new HashSet<int>();
            int bot = 0, energy = 0;
            bool done = false;

            int EdgeKey(int a, int b) => a * 100 + b;
            bool HasEdge(int a, int b) => edges.Contains(EdgeKey(a, b));
            void AddEdge(int a, int b) { edges.Add(EdgeKey(a, b)); edges.Add(EdgeKey(b, a)); }
            float Dist(int a, int b) => (float)Math.Sqrt((nodes[a].X - nodes[b].X) * (nodes[a].X - nodes[b].X) + (nodes[a].Y - nodes[b].Y) * (nodes[a].Y - nodes[b].Y));

            void Draw()
            {
                canvas.Clear(Bg);
                for (int a = 0; a < N; a++)
                {
                    for (int b = a + 1; b < N; b++)
                    {
                        if (HasEdge(a, b))
                        {
                            canvas.DrawLine((int)nodes[a].X, (int)nodes[a].Y, (int)nodes[b].X, (int)nodes[b].Y, Edge);
                        }
                    }
                }

                for (int i = 0; i < N; i++)
                {
                    if (HasEdge(bot, i))
                    {
                        canvas.DrawLine((int)nodes[bot].X, (int)nodes[bot].Y, (int)nodes[i].X, (int)nodes[i].Y, Reach);
                    }
                }

                for (int i = 0; i < N; i++)
                {
                    var col = nodes[i].Repair ? (nodes[i].Fixed ? Fixed : Damaged) : NodeDim;
                    canvas.FillCircle((int)nodes[i].X, (int)nodes[i].Y, 11, col);
                }

                canvas.FillCircle((int)nodes[bot].X, (int)nodes[bot].Y, 7, Bot);
            }

            void Reset()
            {
                edges.Clear();
                done = false;
                for (int i = 0; i < N; i++)
                {
                    nodes[i] = new Node { X = 60 + api.Rand(W - 120), Y = 50 + api.Rand(H - 100) };
                }

                for (int st = 1; st < N; st++)
                {
                    AddEdge(st, api.Rand(st));
                }

                for (int a = 0; a < N; a++)
                {
                    var d = new List<(float dist, int b)>();
                    for (int b = 0; b < N; b++)
                    {
                        if (b != a)
                        {
                            d.Add((Dist(a, b), b));
                        }
                    }

                    d.Sort((p, q) => p.dist.CompareTo(q.dist));
                    for (int k = 0; k < 2; k++)
                    {
                        AddEdge(a, d[k].b);
                    }
                }

                var order = new List<int>();
                for (int i = 0; i < N; i++)
                {
                    order.Add(i);
                }

                api.Shuffle(order);
                bot = order[0];
                for (int t = 1; t <= 4; t++)
                {
                    nodes[order[t]].Repair = true;
                }

                energy = 120;
                api.Hud("energy", energy);
                api.Hud("repaired", "0/4");
                Draw();
            }

            api.Pointer(p =>
            {
                if (p.Phase != PointerPhase.Down || done)
                {
                    return;
                }

                int target = -1;
                double bestD = 26 * 26;
                for (int i = 0; i < N; i++)
                {
                    double dd = (nodes[i].X - p.X) * (nodes[i].X - p.X) + (nodes[i].Y - p.Y) * (nodes[i].Y - p.Y);
                    if (dd < bestD)
                    {
                        bestD = dd;
                        target = i;
                    }
                }

                if (target < 0 || target == bot || !HasEdge(bot, target))
                {
                    return;
                }

                energy -= (int)Math.Round(Dist(bot, target) / 12);
                bot = target;
                if (nodes[bot].Repair && !nodes[bot].Fixed)
                {
                    nodes[bot].Fixed = true;
                    energy -= 4;
                }

                int rc = 0, total = 0;
                for (int j = 0; j < N; j++)
                {
                    if (nodes[j].Repair)
                    {
                        total++;
                        if (nodes[j].Fixed)
                        {
                            rc++;
                        }
                    }
                }

                api.Hud("energy", Math.Max(0, energy));
                api.Hud("repaired", rc + "/" + total);
                Draw();

                if (rc == total)
                {
                    done = true;
                    int rating = energy > 70 ? 3 : energy > 35 ? 2 : 1;
                    api.Complete(Math.Max(50, 200 + energy * 4), rating);
                    return;
                }

                if (energy <= 0)
                {
                    done = true;
                    api.Fail(rc * 50);
                }
            });

            return new MinigameController { Start = Reset };
        }
    }
}
