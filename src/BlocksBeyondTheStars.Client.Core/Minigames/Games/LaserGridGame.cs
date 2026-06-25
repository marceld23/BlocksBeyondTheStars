using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Minigames.Games
{
    /// <summary>Port of <c>web/minigames/laser_grid</c>: toggle the diagonal mirrors so the emitter beam bends its
    /// way to the target node. Canvas-native in the web build; the beam trace + mirror reflection logic is
    /// faithfully reproduced.</summary>
    public sealed class LaserGridGame : IMinigame
    {
        private const int Sz = 8, Cell = 56, W = Sz * Cell;

        private static readonly Rgba Bg = Rgba.Rgb(7, 13, 22);
        private static readonly Rgba GridCol = new Rgba(70, 214, 255, 26);
        private static readonly Rgba BeamWin = Rgba.Rgb(124, 255, 176);
        private static readonly Rgba BeamNo = Rgba.Rgb(255, 107, 107);
        private static readonly Rgba MirrorCol = Rgba.Rgb(207, 233, 245);
        private static readonly Rgba Emitter = Rgba.Rgb(70, 214, 255);
        private static readonly Rgba TargetOff = Rgba.Rgb(119, 144, 160);

        public string Key => "laser_grid";
        public LocText Title => new LocText("Laser Mirror Grid", "Laser-Spiegelgitter");
        public LocText Desc => new LocText(
            "Toggle the mirrors so the beam from the emitter reaches the target node.",
            "Schalte die Spiegel um, damit der Strahl des Emitters den Zielknoten erreicht.");
        public LocText Hint => new LocText("Click a mirror to flip it ( / ↔ \\ )", "Spiegel anklicken zum Umschalten ( / ↔ \\ )");
        public IReadOnlyList<LocText> Help { get; } = new[]
        {
            new LocText("Click a mirror to switch between / and \\", "Spiegel anklicken: zwischen / und \\ wechseln"),
            new LocText("Route the beam from the emitter to the green target", "Leite den Strahl vom Emitter zum grünen Ziel"),
        };
        public int Difficulty => 3;

        private static (int dx, int dy) Delta(char dir) => dir switch
        {
            'E' => (1, 0),
            'W' => (-1, 0),
            'N' => (0, -1),
            _ => (0, 1), // S
        };

        // REF['/'] and REF['\\'] reflection tables.
        private static char Reflect(char mirror, char dir) => mirror == '/'
            ? dir switch { 'E' => 'N', 'N' => 'E', 'W' => 'S', _ => 'W' }   // S => W
            : dir switch { 'E' => 'S', 'S' => 'E', 'W' => 'N', _ => 'W' };  // N => W

        public MinigameController Create(MinigameApi api)
        {
            var canvas = api.Canvas(W, W);

            var mirror = new Dictionary<int, char>();
            (int x, int y) src = (0, 0), target = (0, 0);
            bool done = false;
            int moves = 0;

            int Key2(int x, int y) => y * Sz + x;

            (List<(int x, int y)> seg, bool win) Trace()
            {
                int x = src.x, y = src.y;
                char dir = 'E';
                var seg = new List<(int, int)> { (x, y) };
                bool win = false;
                for (int g = 0; g < 300; g++)
                {
                    var (dx, dy) = Delta(dir);
                    x += dx;
                    y += dy;
                    if (x < 0 || y < 0 || x >= Sz || y >= Sz)
                    {
                        break;
                    }

                    seg.Add((x, y));
                    if (x == target.x && y == target.y)
                    {
                        win = true;
                        break;
                    }

                    if (mirror.TryGetValue(Key2(x, y), out char m))
                    {
                        dir = Reflect(m, dir);
                    }
                }

                return (seg, win);
            }

            void Draw((List<(int x, int y)> seg, bool win) t)
            {
                canvas.Clear(Bg);
                for (int i = 1; i < Sz; i++)
                {
                    canvas.DrawLine(i * Cell, 0, i * Cell, W - 1, GridCol);
                    canvas.DrawLine(0, i * Cell, W - 1, i * Cell, GridCol);
                }

                var beam = t.win ? BeamWin : BeamNo;
                for (int s = 0; s + 1 < t.seg.Count; s++)
                {
                    int px = t.seg[s].x * Cell + Cell / 2, py = t.seg[s].y * Cell + Cell / 2;
                    int nx = t.seg[s + 1].x * Cell + Cell / 2, ny = t.seg[s + 1].y * Cell + Cell / 2;
                    canvas.DrawLine(px, py, nx, ny, beam);
                    canvas.DrawLine(px + 1, py, nx + 1, ny, beam); // 2px for visibility
                }

                foreach (var kv in mirror)
                {
                    int mx = (kv.Key % Sz) * Cell, my = (kv.Key / Sz) * Cell;
                    if (kv.Value == '/')
                    {
                        canvas.DrawLine(mx + 12, my + Cell - 12, mx + Cell - 12, my + 12, MirrorCol);
                        canvas.DrawLine(mx + 13, my + Cell - 12, mx + Cell - 11, my + 12, MirrorCol);
                    }
                    else
                    {
                        canvas.DrawLine(mx + 12, my + 12, mx + Cell - 12, my + Cell - 12, MirrorCol);
                        canvas.DrawLine(mx + 13, my + 12, mx + Cell - 11, my + Cell - 12, MirrorCol);
                    }
                }

                canvas.FillCircle(src.x * Cell + Cell / 2, src.y * Cell + Cell / 2, 9, Emitter);
                canvas.DrawCircle(target.x * Cell + Cell / 2, target.y * Cell + Cell / 2, 11, t.win ? BeamWin : TargetOff);
            }

            void Reset()
            {
                mirror.Clear();
                done = false;
                moves = 0;
                api.Hud("moves", 0);

                int sy = 1 + api.Rand(Sz - 2);
                src = (0, sy);
                int cx = 2 + api.Rand(Sz - 4);
                int ty;
                do
                {
                    ty = api.Rand(Sz);
                }
                while (ty == sy);

                target = (Sz - 1, ty);
                char correct = ty < sy ? '/' : '\\';
                mirror[Key2(cx, sy)] = correct;
                mirror[Key2(cx, ty)] = correct;

                var used = new HashSet<int> { Key2(cx, sy), Key2(cx, ty) };
                for (int x = 0; x <= cx; x++)
                {
                    used.Add(Key2(x, sy));
                }

                int lo = System.Math.Min(sy, ty), hi = System.Math.Max(sy, ty);
                for (int y = lo; y <= hi; y++)
                {
                    used.Add(Key2(cx, y));
                }

                for (int x = cx; x < Sz; x++)
                {
                    used.Add(Key2(x, ty));
                }

                int d = 0;
                while (d < 3)
                {
                    int rx = 1 + api.Rand(Sz - 2), ry = api.Rand(Sz), k = Key2(rx, ry);
                    if (used.Contains(k) || mirror.ContainsKey(k) || (rx == target.x && ry == target.y))
                    {
                        continue;
                    }

                    mirror[k] = api.Rand(2) != 0 ? '/' : '\\';
                    d++;
                }

                int tries = 0;
                do
                {
                    mirror[Key2(cx, sy)] = api.Rand(2) != 0 ? '/' : '\\';
                    mirror[Key2(cx, ty)] = api.Rand(2) != 0 ? '/' : '\\';
                }
                while (Trace().win && ++tries < 20);

                Draw(Trace());
            }

            api.Pointer(p =>
            {
                if (p.Phase != PointerPhase.Down || done)
                {
                    return;
                }

                int cx = (int)(p.X / Cell), cy = (int)(p.Y / Cell), k = Key2(cx, cy);
                if (!mirror.TryGetValue(k, out char m))
                {
                    return;
                }

                mirror[k] = m == '/' ? '\\' : '/';
                moves++;
                api.Hud("moves", moves);
                var t = Trace();
                Draw(t);
                if (t.win)
                {
                    done = true;
                    int rating = moves <= 2 ? 3 : moves <= 5 ? 2 : 1;
                    api.After(() => api.Complete(System.Math.Max(50, 500 - moves * 20), rating), 0.25f);
                }
            });

            return new MinigameController { Start = Reset };
        }
    }
}
