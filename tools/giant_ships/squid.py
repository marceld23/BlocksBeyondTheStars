"""The CRYSTAL SQUID (#3622): the alien giant ship — a squid of dyed crystal that nobody knows the builders of. It is
rarer than the human ships (weight 1) and its wrecks are crystal wrecks (`style: alien`: shards instead of scrap).

    uv run --no-project python tools/gen_giant_ships.py --only squid --out artifacts/gs/ships --preview

Layout (x across, y up, z along; +z is the mantle tip — a squid swims mantle first, its arms trail behind):
- z ~0..90: six arms and two longer tentacles with clubs, rooted in a ring at the back of the head. They fan out and sag
  toward the ground (the lower ones lie on it), get thinner toward the tips, some tips curl; suckers underneath, a
  glowing vein on top, glowing tips.
- z 81..117: the head, an ellipsoid with two big round eyes — glass bubbles with a dark pupil, a teal iris and a glint.
- z 110..247: the mantle, a lofted crystal body — darker on the back, paler on the belly, pink spots, six glowing veins
  and a ridge along the back — with two wide fins at the rear that make the arrowhead silhouette.
- Inside, two levels (walk rows W1 = 5 and W2 = 20): round crystal tunnels that wind gently (the spine below, the back
  tunnel above), bubble pods that bulge out of the flanks as glass blisters (one room each, behind a sliding doorway),
  the heart hall in the middle with a glowing core and a spiral ramp around it up to a ledge (kept: the ruin pass never
  cuts the upper level off), the head chamber behind the eyes, a tail pod at the back.
- Three ways in at ground level: the flanks of the heart hall (the first is the boarding arrival in space) and the
  right side of the head.

Materials: crystal (dyed violet / teal / pale pink) and dyed glass only — no metal, no screens, no ladders. The space
between the organs inside the mantle is closed air (the organs float in the body like in a real animal). Ground row 4:
on a planet the belly sinks four rows into the ground.
"""
import itertools
import math

from ships import CLEAR, Hull, Mat, ShipDesign, ellipse, shipgeom, solids

CX, CY = 64, 22            # the body axis (x, y); normalize() moves the arms' far tips to x >= 0 at the end
GROUND = 4                 # groundRow: rows 0..3 are buried on a planet
W1, W2 = 5, 20             # walk rows of the lower and the upper level (their floors are rows W - 1)
HEAD_C, HEAD_R = (CX, 15, 99), (16, 14, 18)
HALL_C, HALL_R = (CX, 14, 165), (16, 14, 18)
TAIL_C = (CX, W2 + 3, 230)

# --- colours (tints of the crystal block; the dye keeps the crystal's scales and recolours them) --------------------
DORSAL = 0x5E3FD0          # the back: deep violet
VIOLET = 0x8A5CFF          # the flanks
BELLY = 0xD9C9FF           # the belly: pale lilac (a squid's counter-shading)
SPOT = 0xF27FCF            # the pink spots (chromatophores) and the fin rims
VEIN = 0x7FF0F0            # the veins: pale teal
VEIN_GLOW = 0x6FF4FF
TIP_GLOW = 0xFF8FE6
TUBE = 0x7B58EE            # tunnel walls
POD = 0xF4B9E2             # pod walls: pale pink
HALL = 0xC9B4FF            # the heart hall's wall: pale lilac
FLOOR = 0xE6DCFF           # floors and the spiral ramp
TEAL = 0x4CCFC8
EYE_DARK = 0x2A2466        # the pupils (dark glass)
IRIS = 0x9DF5EE
SCLERA = 0xEDE6FF
CORE_GLOW = 0xFFB3F0


def crystal(tint: int, glow: int = 0, keep: bool = False) -> Mat:
    return Mat("crystal", tint=tint, glow=glow, keep=keep)


def glass(tint: int) -> Mat:
    return Mat("glass", tint=tint)


def skin(cy: float, seed: int):
    """The outer skin of a body part centred on row cy: counter-shaded bands (long runs — a small file) with pink spots
    from smooth noise (blobs, not speckle)."""
    def at(x: int, y: int, z: int) -> Mat:
        if solids.value_noise(x * 0.21, y * 0.21, z * 0.15, seed) > 0.76:
            return crystal(SPOT)
        if y >= cy + 9:
            return crystal(DORSAL)
        if y <= cy - 11:
            return crystal(BELLY)
        return crystal(VIOLET)
    return at


class Squid:
    """Builds the squid step by step; `air` collects every cell carved as walkable air, so a passage's walls never
    fill another room."""

    def __init__(self) -> None:
        self.d = ShipDesign("squid", "Crystal Squid", weight=1, groundRow=GROUND, style="alien")
        self.air: set = set()
        self.mantle: Hull | None = None

    # --- helpers ----------------------------------------------------------------------------------------------------
    def carve(self, cells) -> None:
        cells = set(cells)
        self.d.fill(cells, CLEAR)
        self.air |= cells

    def passage(self, axis: str, a0: int, a1: int, c: int, y: int, door_at: int | None = None,
                wall: Mat | None = None) -> None:
        """A doorway passage 2 wide and 3 high along `axis` ('x' or 'z') from a0 to a1, its two columns at c and c + 1
        across, walking on row y. Its walls, floor and ceiling fill every cell that is not another room's air (it runs
        through the closed air between the organs); a `door_slide` marker at `door_at` along the axis (the derelict
        decay reads the rooms from it)."""
        wall = wall or crystal(TUBE)
        lo, hi = sorted((a0, a1))

        def at(a: int, u: int, yy: int) -> tuple:
            return (a, yy, u) if axis == "x" else (u, yy, a)
        opening = {at(a, u, yy) for a in range(lo, hi + 1) for u in (c, c + 1) for yy in range(y, y + 3)}
        around = {at(a, u, yy) for a in range(lo, hi + 1) for u in range(c - 1, c + 3) for yy in range(y - 1, y + 4)}
        self.d.fill([p for p in around - opening if p not in self.air], wall)
        self.carve(opening)
        if door_at is not None:
            self.d.slide_door(*at(door_at, c, y), axis)

    def tube(self, zs: tuple[int, int], y: int, wiggle) -> shipgeom.Tube:
        """A round crystal tunnel along z at path row y (bore 2.5: a 3-wide floor on row y - 2, 5 rows of headroom in
        the middle), its centre line winding by `wiggle(z)`. The ends are closed caps; passages and halls open them."""
        z0, z1 = zs
        pts = [(CX + wiggle(z), y, z) for z in range(z0, z1, 4)] + [(CX + wiggle(z1), y, z1)]
        t = self.d.tube(pts, crystal(TUBE), radius=3.5, hollow=1, kind="catmull", open_ends=False)
        self.air |= t.bore
        # Glowing photophores in the ceiling every eight cells.
        for k, (px, py, pz) in enumerate(t.path):
            if k % 16 == 8:
                p = (round(px), round(py) + 3, round(pz))
                if self.d.is_block(*p):
                    self.d.put(*p, crystal(SPOT, glow=TIP_GLOW))
        return t

    def pod(self, c: tuple[float, float, float], r: float, walk: int, inside_tint: int = POD,
            glass_tint: int = TEAL) -> None:
        """A bubble room: a sphere shell, crystal where it lies inside the mantle and dyed glass where it bulges out
        of the hull (a blister you look out of), its floor filled flat on row walk - 1."""
        shell = solids.sphere(*c, r, wall=1)
        inner = solids.sphere(*c, r) - shell
        m = self.mantle
        self.d.fill(shell, lambda x, y, z: crystal(inside_tint) if m.contains((x, y, z), 2) else glass(glass_tint))
        self.d.fill([p for p in inner if p[1] < walk], crystal(FLOOR))
        self.carve(p for p in inner if p[1] >= walk)

    # --- the body ---------------------------------------------------------------------------------------------------
    def build_mantle(self) -> None:
        stations = [(110, 16, 15), (116, 20, 19), (128, 23.5, 21.5), (150, 24.5, 22.5), (175, 24, 22),
                    (200, 21, 19), (222, 15, 13.5), (238, 8, 7.5), (247, 2.5, 2.5)]
        self.mantle = self.d.loft([(z, ellipse(CX, CY, rx, ry)) for z, rx, ry in stations], crystal(VIOLET),
                                  wall=2, skin=skin(CY, 7))

    def build_veins(self) -> None:
        """Six glowing veins along the mantle (wavy lines of pale teal, every fifth cell glowing) and the ridge of the
        squid's pen along the back."""
        veins = [math.radians(a) for a in (12, 58, 122, 168, -32, -148)]
        for x, y, z in self.mantle.outer():
            if not 118 <= z <= 243 or not self._is_crystal((x, y, z)):
                continue
            ang = math.atan2(y - CY, x - CX)
            rad = math.hypot(x - CX, y - CY)
            for k, v in enumerate(veins):
                dv = (ang - v - 0.07 * math.sin(z / 7 + k) + math.pi) % (2 * math.pi) - math.pi
                if abs(dv) * rad < 0.75:
                    self.d.put(x, y, z, crystal(VEIN, glow=VEIN_GLOW if z % 8 == k % 8 else 0))
                    break
        for x, y, z in self.mantle.outer():
            if z <= 111 and self._is_crystal((x, y, z)):
                self.d.put(x, y, z, crystal(SPOT))
        for z in range(116, 242):
            top = self.mantle.column(CX, z)
            if top is not None:
                self.d.put(CX, top[1] + 1, z, crystal(TEAL, glow=VEIN_GLOW if z % 6 == 0 else 0))
        # A row of small glowing photophores along each lower flank.
        for z in range(124, 214, 10):
            r = self.mantle.row(CY - 13, z)
            if r is not None:
                for x in r:
                    if self._is_crystal((x, CY - 13, z)):
                        self.d.put(x, CY - 13, z, crystal(SPOT, glow=TIP_GLOW))

    def _is_crystal(self, p) -> bool:
        v = self.d.get(*p)
        return v is not None and v[0] == "block" and v[1] == "crystal"

    def build_fins(self) -> None:
        """Two wide fins at the rear (a rounded arrowhead seen from above), in the body's middle plane: 5 cells thick
        at the root, 1 at the rim; ribs fan out from the body, the rim is pink."""
        z0, z1, span = 176, 247, 32.0
        for side in (-1, 1):
            for z in range(z0, z1 + 1):
                r = self.mantle.row(CY, z)
                if r is None:
                    continue
                base = r[1] if side > 0 else r[0]
                u = (z - z0) / (z1 - z0)
                ext = span * math.sin(math.pi * u ** 0.85) ** 0.8
                if ext < 1:
                    continue
                for k in range(int(ext) + 1):
                    x = base + side * k
                    q = k / ext
                    h = 2 if q < 0.35 else 1 if q < 0.75 else 0
                    rib = (z + round(k * 0.45)) % 9 == 0
                    for y in range(CY - h, CY + h + 1):
                        if self.mantle.contains((x, y, z)):
                            continue
                        if q > 0.86:
                            m = crystal(SPOT)
                        elif rib:
                            m = crystal(VEIN, glow=VEIN_GLOW if y == CY and k % 4 == 2 else 0)
                        else:
                            m = crystal(0xA27BFF if y >= CY else 0xC4AEFF)
                        self.d.put(x, y, z, m)

    def build_head(self) -> None:
        """The head: an ellipsoid shell (it closes the mantle's front and swallows the arm roots), its chamber with a
        flat floor on row W1 - 1, and the two eyes."""
        cells = solids.ellipsoid(*HEAD_C, *HEAD_R)
        head = Hull.from_cells(cells, axis_x=CX)
        self.d.add_hull(head, crystal(VIOLET), wall=2, skin=skin(HEAD_C[1], 11))
        inside = head.interior(2)
        self.d.fill([p for p in inside if p[1] < W1], crystal(FLOOR))
        self.carve(p for p in inside if p[1] >= W1)
        for side in (-1, 1):
            self.eye(side)
        # The pilot crystal: a pedestal with a glowing crystal floating above it, and crystal shards along the walls.
        hx, _, hz = HEAD_C
        self.d.cylinder(hx, hz + 2, W1, W1 + 1, 1.5, crystal(TEAL))
        self.d.sphere(hx, W1 + 4, hz + 2, 1.6, crystal(SPOT, glow=CORE_GLOW))
        for dx, dz in ((-6, -4), (6, -4), (-5, 6), (5, 6)):
            self.d.cone(hx + dx, hz + dz, W1, W1 + 3, 1.2, crystal(TEAL), r1=0.3)

    def eye(self, side: int) -> None:
        """A big round eye bulging out of the head: a glass bubble with a dark see-through pupil, a teal iris ring, a
        pale rim and a glint (glowing), hollow and open to the head chamber — a window."""
        c = (CX + side * 14.5, 12.0, 100.0)
        r = 6.5
        shell = solids.sphere(*c, r, wall=1)
        inner = solids.sphere(*c, r) - shell
        glint = (side * 0.80, 0.50, -0.33)
        for x, y, z in shell:
            if (x, y, z) in self.air:
                continue
            v = ((x - c[0]) / r, (y - c[1]) / r, (z - c[2]) / r)
            cos = v[0] * side
            if cos > 0.8:
                near = math.dist(v, glint) < 0.28
                m = crystal(0xFFFFFF, glow=0xFFFFFF) if near else glass(EYE_DARK)
            elif cos > 0.55:
                m = crystal(IRIS)
            else:
                m = crystal(SCLERA)
            self.d.put(x, y, z, m)
        self.carve(inner)

    # --- the arms ---------------------------------------------------------------------------------------------------
    @staticmethod
    def arm_points(angle: float, length: float, spread: tuple[float, float], sag: float, phase: float,
                   curl: tuple[str, float, float] | None) -> list[tuple[float, float, float]]:
        """The control points of an arm rooted on the crown ring at the back of the head: it runs back (-z), its
        distance from the axis grows (`spread` across, up), it sags by `sag` toward the tip, it wiggles a little
        sideways. `curl` = (plane, radius, turns): the last fifth rolls up — 'up' toward the sky, 'out' sideways."""
        a = math.radians(angle)
        n = 12
        t_end = 0.8 if curl else 1.0
        pts = []
        for i in range(n + 1):
            t = t_end * i / n
            rho_x = 7 + spread[0] * t ** 1.15
            rho_y = 7 + spread[1] * t ** 1.15
            x = CX + math.cos(a) * rho_x + 4.0 * t * math.sin(2 * math.pi * 1.1 * t + phase)
            y = 13 + math.sin(a) * rho_y - sag * t * t
            pts.append((x, y, 88 - length * t))
        if curl:
            plane, radius, turns = curl
            p, q = pts[-1], pts[-2]
            tx, ty, tz = (p[0] - q[0], p[1] - q[1], p[2] - q[2])
            norm = math.sqrt(tx * tx + ty * ty + tz * tz) or 1.0
            tx, ty, tz = tx / norm, ty / norm, tz / norm
            if plane == "up":
                ux, uy, uz = 0.0, 1.0, 0.0
            else:  # sideways, away from the axis
                ux, uy, uz = (math.copysign(1.0, p[0] - CX), 0.0, 0.0)
            steps = 10
            for i in range(1, steps + 1):
                th = turns * 2 * math.pi * i / steps
                rr = radius * (1 - 0.45 * i / steps)
                s, c = math.sin(th), 1 - math.cos(th)
                pts.append((p[0] + rr * (s * tx + c * ux), p[1] + rr * (s * ty + c * uy), p[2] + rr * (s * tz + c * uz)))
        return pts

    def arm(self, points, radii, seed: int, tentacle: bool = False) -> None:
        """Sweeps a ball along the arm's curve (lying on the ground where it reaches it) and colours each cell by where
        it sits on the arm: suckers underneath, a glowing vein on top, glowing tips, the inside one plain material."""
        path = shipgeom.curve(points, "catmull", 0.5)
        lengths = [0.0]
        for a, b in itertools.pairwise(path):
            lengths.append(lengths[-1] + math.dist(a, b))
        total = lengths[-1] or 1.0
        rf = shipgeom.radius_profile(radii)
        best: dict = {}
        for i, (p, s) in enumerate(zip(path, lengths)):
            t = s / total
            r = rf(t)
            px, py, pz = p[0], max(p[1], GROUND - 1 + r), p[2]   # an arm rests on the ground, it never sinks deeper
            rr = r * r + 1e-6
            for x in range(math.ceil(px - r), math.floor(px + r) + 1):
                for y in range(math.ceil(py - r), math.floor(py + r) + 1):
                    for z in range(math.ceil(pz - r), math.floor(pz + r) + 1):
                        d2 = (x - px) ** 2 + (y - py) ** 2 + (z - pz) ** 2
                        if d2 <= rr and (x, y, z) not in self.air:
                            old = best.get((x, y, z))
                            if old is None or d2 / rr < old[0]:
                                best[(x, y, z)] = (d2 / rr, i, t, (y - py) / max(r, 0.5), r)
        for cell, (f, i, t, ny, r) in best.items():
            surface = math.sqrt(f) * r > r - 1.2
            if not surface:
                m = crystal(VIOLET)
            elif t > 0.965:
                m = crystal(SPOT, glow=TIP_GLOW)
            elif ny < -0.62 and (i // (5 if tentacle and t > 0.7 else 7)) % 2 == 0:
                m = crystal(SPOT)                                   # the suckers underneath
            elif ny > 0.82:
                m = crystal(VEIN, glow=VEIN_GLOW if i % 22 == seed % 22 else 0)
            elif ny > 0.1:
                m = crystal(DORSAL if ny > 0.5 else VIOLET)
            else:
                m = crystal(0xB89CFF)
            self.d.put(*cell, m)

    def build_arms(self) -> None:
        arm_r = [(0, 5.2), (0.25, 4.3), (0.6, 2.9), (0.85, 1.9), (1, 1.0)]
        club_r = [(0, 3.6), (0.15, 2.6), (0.62, 1.9), (0.72, 3.0), (0.86, 3.6), (0.95, 2.4), (1, 1.0)]
        arms = [  # (angle round the axis, length back, (spread across, up), sag, wiggle phase, curl)
            (198, 74, (40, 14), 6, 0.3, ("out", 6.0, 0.9)),
            (155, 76, (36, 18), 9, 2.1, None),
            (118, 70, (24, 26), 12, 4.0, ("up", 7.0, 1.0)),
            (62, 72, (24, 26), 10, 1.2, None),
            (25, 78, (36, 18), 9, 5.0, ("out", 6.5, 0.85)),
            (-18, 74, (40, 14), 6, 3.3, None),
        ]
        for k, (ang, ln, spread, sag, ph, curl) in enumerate(arms):
            self.arm(self.arm_points(ang, ln, spread, sag, ph, curl), arm_r, seed=k)
        for k, (ang, ph) in enumerate(((-112, 0.8), (-68, 3.6))):
            pts = self.arm_points(ang, 86, (24, 6), 2, ph, None)
            self.arm(pts, club_r, seed=k + 7, tentacle=True)

    # --- the inside -------------------------------------------------------------------------------------------------
    def build_tubes(self) -> dict:
        """The spine on the lower level (from the head to the heart hall, on to the rear pods) and the back tunnel on
        the upper level (from the front pods through the hall to the tail pod)."""
        return {
            "spine_front": self.tube((112, 155), W1 + 2, lambda z: 3 * math.sin((z - 112) / 13)),
            "spine_rear": self.tube((176, 196), W1 + 2, lambda z: 2.5 * math.sin((z - 176) / 9)),
            "back_front": self.tube((124, 157), W2 + 2, lambda z: -3 * math.sin((z - 124) / 11)),
            "back_rear": self.tube((173, 223), W2 + 2, lambda z: 3 * math.sin((z - 173) / 12)),
        }

    def build_hall(self, tubes: dict) -> None:
        """The heart hall: a big lilac bubble on the lower level, a ledge round its upper wall (the upper level), a
        glowing core on a pedestal with crystal tendrils to the ceiling and a kept spiral ramp round the core."""
        cx, cy, cz = HALL_C
        rx, ry, rz = HALL_R
        solid = solids.ellipsoid(cx, cy, cz, rx, ry, rz)
        inner = solids.ellipsoid(cx, cy, cz, rx - 2, ry - 2, rz - 2)
        self.d.fill(solid - inner, crystal(HALL))
        self.d.fill([p for p in inner if p[1] < W1], crystal(FLOOR))
        self.carve(p for p in inner if p[1] >= W1)
        # The tunnels' mouths: the hall wall closed them again — carve their bores once more.
        for t in tubes.values():
            self.d.fill(t.bore, CLEAR)
        # The ledge: the outer band of row W2 - 1.
        ledge = [p for p in inner if p[1] == W2 - 1
                 and ((p[0] - cx) / (rx - 2)) ** 2 + ((p[2] - cz) / (rz - 2)) ** 2 >= 0.66 ** 2]
        self.d.fill(ledge, crystal(FLOOR))
        self.air -= set(ledge)
        # The core and its tendrils.
        self.d.cylinder(cx, cz, W1, W1 + 4, 1.6, crystal(TEAL))
        core = solids.sphere(cx, 13, cz, 3.5)
        core_shell = solids.sphere(cx, 13, cz, 3.5, wall=1)
        self.d.fill(core - core_shell, crystal(SPOT))
        self.d.fill(core_shell, lambda x, y, z: crystal(SPOT, glow=CORE_GLOW if (x + y + z) % 2 == 0 else 0))
        # The spiral ramp: from the floor round the core up to the ledge; every tread kept.
        treads = self.d.spiral_stair(cx, cz, W1, W2 - 1, 4.6, 8.4, crystal(FLOOR, keep=True), steps_per_turn=26,
                                     start=-math.pi / 2)
        self.d.keep(treads)
        self.air -= treads
        top = -math.pi / 2 + (W2 - 1 - W1 + 0.5) / 26 * 2 * math.pi
        bridge = {(round(cx + rr * math.cos(top + da)), W2 - 1, round(cz + rr * math.sin(top + da)))
                  for rr in (6.5, 7.5, 8.5, 9.5, 10.5, 11.5) for da in (-0.12, 0.0, 0.12)}
        self.d.fill(bridge, crystal(FLOOR, keep=True))
        self.air -= bridge
        for k in range(4):
            a = math.pi / 4 + k * math.pi / 2
            pts = [(cx, 16.0, cz), (cx + 1.5 * math.cos(a), 21.0, cz + 1.5 * math.sin(a)),
                   (cx + 5 * math.cos(a), 24.0, cz + 6 * math.sin(a)), (cx + 7 * math.cos(a), 27.0, cz + 8 * math.sin(a))]
            self.d.tube(pts, crystal(TEAL, glow=VEIN_GLOW if k % 2 else 0), radius=0.9)

    def build_rooms(self, tubes: dict) -> None:
        """The pods along both flanks (glass blisters), the passages with their doorways, the head and tail doors."""
        m = self.mantle
        spine_x = {**{z: 3 * math.sin((z - 112) / 13) for z in range(112, 156)},
                   **{z: 2.5 * math.sin((z - 176) / 9) for z in range(176, 197)}}
        back_x = {**{z: -3 * math.sin((z - 124) / 11) for z in range(124, 158)},
                  **{z: 3 * math.sin((z - 173) / 12) for z in range(173, 224)}}
        self.pods = []
        for level, walk, zs, table in ((1, W1, (126, 141, 192), spine_x), (2, W2, (130, 162, 196), back_x)):
            for side in (-1, 1):
                for z in zs:
                    r = m.row(walk + 3, z)
                    inset = 4 if level == 1 else 1   # the lower blisters barely bulge (they are half buried on a planet)
                    px = (r[1] - inset) if side > 0 else (r[0] + inset)
                    if level == 2 and z > 176:
                        px = CX + side * 12         # beside the fin roots: a pod inside the body, no blister
                    self.pod((px, walk + 3, z), 6, walk, glass_tint=0xC7A6FF if level == 1 else 0xB58CFF)
                    start = CX + round(table[z]) if z in table else CX + side * 10   # z 162: from the hall's ledge
                    a0 = start
                    a1 = px - side * 3
                    door = round((a0 + px - side * 6) / 2)
                    self.passage("x", a0, a1, z - 1, walk, door_at=door, wall=crystal(POD))
                    self.pods.append((side, px, walk, z))
        # The head chamber's doorway into the spine, the tail pod's doorway off the back tunnel.
        self.passage("z", 106, 116, CX, W1, door_at=110)
        self.pod(TAIL_C, 5.5, W2, inside_tint=POD)
        tx = CX + round(back_x[223])
        self.passage("z", 219, 227, tx, W2, door_at=224)

    def build_entrances(self) -> None:
        """Three tunnels from the outside at ground level, with a pink lip round the outer opening: the first (left
        flank of the heart hall) is the boarding arrival in space."""
        hz = HALL_C[2]
        self.passage("x", CX - 26, CX - 6, hz - 5, W1, wall=crystal(HALL))
        self.passage("x", CX + 6, CX + 26, hz + 4, W1, wall=crystal(HALL))
        self.passage("x", CX + 4, CX + 22, 92, W1, wall=crystal(TUBE))
        # Trim whatever the passages pushed out beyond the hull, then frame the openings.
        for z0, side in ((hz - 5, -1), (hz + 4, 1), (92, 1)):
            for x in range(CX + side * 12, CX + side * 30, side):
                for y in range(W1 - 1, W1 + 4):
                    for z in range(z0 - 1, z0 + 3):
                        if not self.mantle.contains((x, y, z)) and not self._in_head((x, y, z)):
                            self.d.put(x, y, z, CLEAR)
                            self.air.discard((x, y, z))
            self._lip(z0, side)
        self.d.entrance(CX - 7, W1, hz - 5)
        self.d.entrance(CX + 7, W1, hz + 4)
        self.d.entrance(CX + 4, W1, 92)

    @staticmethod
    def _in_head(p) -> bool:
        (hx, hy, hz), (rx, ry, rz) = HEAD_C, HEAD_R
        return ((p[0] - hx) / rx) ** 2 + ((p[1] - hy) / ry) ** 2 + ((p[2] - hz) / rz) ** 2 <= 1.0

    def _lip(self, z0: int, side: int) -> None:
        """A pink rim round an opening's outer face (restyles the hull blocks there)."""
        for y in range(W1 - 1, W1 + 4):
            for z in range(z0 - 1, z0 + 3):
                xs = [x for x in range(CX + side * 30, CX, -side) if self.d.is_block(x, y, z)]
                if xs and (y in (W1 - 1, W1 + 3) or z in (z0 - 1, z0 + 2)):
                    self.d.put(xs[0], y, z, crystal(SPOT, glow=TIP_GLOW if y == W1 + 3 and z == z0 else 0))

    def build_loot(self) -> None:
        """Chests (giant_squid) in every pod, the head chamber, the hall and the tunnel ends; three hidden caches
        (giant_cache) — behind the pilot crystal, on the hall's ledge, in the tail pod. Ambience: the crystal hum."""
        d = self.d
        for side, px, walk, z in self.pods:
            d.chest(px + side * 2, walk, z + 1, table="giant_squid")
        hx, _, hz = HEAD_C
        d.chest(hx - 4, W1, hz - 1, table="giant_squid")
        d.chest(hx - 3, W1, hz + 7, table="giant_cache", hidden=True)
        cx, _, cz = HALL_C
        d.chest(cx - 8, W1, cz + 5, table="giant_squid")
        d.chest(cx + 11, W2, cz, table="giant_cache", hidden=True)
        d.chest(CX + round(2.5 * math.sin(20 / 9)), W1, 194, table="giant_squid")
        d.chest(CX - round(3 * math.sin(0)), W2, 126, table="giant_squid")
        d.chest(TAIL_C[0] + 1, W2, TAIL_C[2] + 2, table="giant_cache", hidden=True)
        d.sound(cx, 24, cz, "amb_giant_crystal", radius=64, volume=0.6)
        d.sound(hx, 12, hz, "amb_giant_crystal", radius=36, volume=0.4)
        d.sound(TAIL_C[0], W2 + 4, TAIL_C[2], "amb_giant_crystal", radius=30, volume=0.35)

    def build(self) -> ShipDesign:
        self.build_arms()          # first: the head shell and its chamber cut the roots off cleanly
        self.build_mantle()
        self.build_fins()
        self.build_head()
        tubes = self.build_tubes()
        self.build_hall(tubes)
        self.build_rooms(tubes)
        self.build_entrances()
        self.build_veins()
        self.build_loot()
        self.d.normalize()
        return self.d


def build() -> ShipDesign:
    return Squid().build()
