// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The "beta" station (#2674): the simple boxy shape every NPC station showed in flight until v2026.9.9 — the old
/// client model (<c>SpaceView.BuildStationModel</c>): a cube hub with a dark collar ring and a glass view band, two
/// crossed arms with a box pod at each end, two large flat blue solar wings on ±X and a mast with a beacon on top. Here it
/// is a real, walkable voxel build at roughly four blocks per model unit — the outside is the inside, so mining shows
/// outside and a spacewalk meets the same hull:
/// <list type="bullet">
/// <item>the hub (15 wide, a cube): a solid machine base, a command deck at the arms' height with a console bank and a
/// panoramic glass band just above the collar ring;</item>
/// <item>four tube arms (inside 3 wide and 3 high) from the hub walls to the pods, a doorway cut at both joints
/// (<c>door_slide</c> markers whose <see cref="StationMarker.Data"/> names the wall's axis, <c>x</c> or <c>z</c>);</item>
/// <item>four pod rooms (9 wide, inside 7 × 5 × 7) at the arm ends: the hangar on +Z (its outer wall a force-field
/// mouth — it faces the launch point), and the trading corner (a counter with a <c>vendor</c> marker), the medbay and the
/// quarters, shuffled over +X, −X and −Z by the seed;</item>
/// <item>two flat solar wings — blue-tinted glass (<see cref="StationKitExterior.SolarTint"/>) in carbon frames — on the
/// outer faces of the ±X pods, and a carbon mast on the hub roof with a yellow beacon.</item>
/// </list>
/// The structure is intact; <see cref="DerelictGenerator"/> breaks it. Never rotated — the server stamps, collides and
/// maps the spacewalk on the unrotated grid. Deterministic from the seed.
/// </summary>
public static class BetaStationGenerator
{
    // ---- The shape, in blocks relative to the station centre (the middle of the hub at the arms' height) ----

    /// <summary>The hub reaches |dx|, |dz| ≤ this; its walls stand at exactly this distance.</summary>
    public const int HubHalf = 7;

    /// <summary>The hub's lowest and highest layer (its base and roof).</summary>
    public const int HubBottom = -7;

    public const int HubTop = 7;

    /// <summary>The deck layer of the hub, the arms and the pods — a boarder's feet stand one above it.</summary>
    public const int Deck = -2;

    /// <summary>The dark collar ring around the hub: out to |d| = this, layers <see cref="CollarBottom"/> … <see cref="CollarTop"/>.</summary>
    public const int CollarReach = 10;

    public const int CollarBottom = -3;
    public const int CollarTop = -1;

    /// <summary>The glass view band in the hub walls, right above the collar (eye height on the command deck).</summary>
    public const int BandBottom = 0;

    public const int BandTop = 3;

    /// <summary>A tube arm's cross-section: |side| ≤ this, layers <see cref="Deck"/> … <see cref="ArmTop"/>.</summary>
    public const int ArmHalf = 2;

    public const int ArmTop = 2;

    /// <summary>A pod's centre along its arm, and its half size (a 9-block cube from <see cref="PodBottom"/> to
    /// <see cref="PodTop"/>).</summary>
    public const int PodCentre = 20;

    public const int PodHalf = 4;
    public const int PodBottom = -4;
    public const int PodTop = 4;

    /// <summary>A solar wing: from <see cref="WingInner"/> to <see cref="WingOuter"/> along ±X, |dz| ≤ <see cref="WingHalfSpan"/>,
    /// one block thick on the layer <see cref="WingLayer"/>.</summary>
    public const int WingInner = PodCentre + PodHalf + 1;

    public const int WingOuter = WingInner + 12;
    public const int WingHalfSpan = 10;
    public const int WingLayer = 0;

    /// <summary>The mast on the hub roof (<see cref="MastBottom"/> … <see cref="MastTop"/>) and the beacon on its tip.</summary>
    public const int MastBottom = HubTop + 1;

    public const int MastTop = MastBottom + 8;
    public const int BeaconLayer = MastTop + 1;

    /// <summary>The beacon's yellow (the old model's beacon colour).</summary>
    public const int BeaconTint = 0xFFD966;

    /// <summary>The station tier the shape counts as (the scanner names the size from it).</summary>
    public const string SizeTier = "large";

    /// <summary>Free blocks around the shape on every side.</summary>
    private const int Margin = 1;

    /// <summary>The arm directions, in pod order: +X, −X, +Z, −Z. The hangar is always the +Z pod (index 2).</summary>
    public static readonly (int X, int Z)[] Arms = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>The arm index of the hangar pod (+Z — the side facing the launch point).</summary>
    public const int HangarArm = 2;

    public static int Width => 2 * (WingOuter + Margin) + 1;

    public static int Height => BeaconLayer - HubBottom + 1 + (2 * Margin);

    public static int Length => 2 * (PodCentre + PodHalf + Margin) + 1;

    /// <summary>The structure cell of the station centre — the middle of the hub, at the arms' height.</summary>
    public static Vector3i Centre => new(WingOuter + Margin, Margin - HubBottom, PodCentre + PodHalf + Margin);

    /// <summary>Builds the intact beta station.</summary>
    public static StationStructure Generate(long seed, GameContent content)
    {
        var rng = new Random(unchecked((int)(seed ^ (seed >> 32)) ^ (int)WorldGenerator.StableHash("beta-station")));
        int w = Width, h = Height, l = Length;
        var c = Centre;
        var blocks = new ushort[w * h * l];
        var mods = new Dictionary<int, (int, int)>();

        ushort Id(string key, ushort fallback = 0) => content.GetBlock(key)?.NumericId.Value is { } v && v != 0 ? v : fallback;
        ushort hull = Id("iron_wall");
        ushort dark = Id("carbon", hull);
        ushort glass = Id("glass", hull);
        ushort floor = Id("steel_floor", hull);
        ushort cargoFloor = Id("cargo_floor", floor);
        ushort light = Id("data_cache");
        ushort field = Id("force_field", glass);
        ushort beacon = Id("light_white");
        ushort healTank = Id("heal_tank", dark);
        ushort medPanel = Id("medbay_panel", hull);

        int Index(int dx, int dy, int dz) => ((c.X + dx) * h + c.Y + dy) * l + c.Z + dz;
        bool Inside(int dx, int dy, int dz)
        {
            int x = c.X + dx, y = c.Y + dy, z = c.Z + dz;
            return x >= 0 && y >= 0 && z >= 0 && x < w && y < h && z < l;
        }

        void Set(int dx, int dy, int dz, ushort block, int tint = 0)
        {
            if (!Inside(dx, dy, dz))
            {
                return;
            }

            int i = Index(dx, dy, dz);
            blocks[i] = block;
            if (tint != 0 && block != 0)
            {
                mods[i] = (tint, 0);
            }
            else
            {
                mods.Remove(i);
            }
        }

        // A cell on an arm: `a` blocks out from the centre along the arm, `s` across it (to the side).
        void SetOnArm(int arm, int a, int s, int dy, ushort block, int tint = 0)
        {
            var (ax, az) = Arms[arm];
            Set(ax != 0 ? ax * a : s, dy, az != 0 ? az * a : s, block, tint);
        }

        // ---- The hub: a solid base, the command deck, walls with the glass band, the roof ----
        for (int dx = -HubHalf; dx <= HubHalf; dx++)
            for (int dz = -HubHalf; dz <= HubHalf; dz++)
                for (int dy = HubBottom; dy <= HubTop; dy++)
                {
                    bool wallX = Math.Abs(dx) == HubHalf, wallZ = Math.Abs(dz) == HubHalf;
                    bool wall = wallX || wallZ;
                    if (dy < Deck)
                    {
                        Set(dx, dy, dz, hull); // the machine base under the deck
                    }
                    else if (dy == Deck)
                    {
                        Set(dx, dy, dz, wall ? hull : floor);
                    }
                    else if (dy == HubTop)
                    {
                        Set(dx, dy, dz, hull);
                    }
                    else if (wall)
                    {
                        bool band = dy >= BandBottom && dy <= BandTop && !(wallX && wallZ); // the corner columns frame it
                        Set(dx, dy, dz, band ? glass : hull);
                    }
                }

        // ---- The collar: a dark ring round the hub, just under the view band ----
        for (int dx = -CollarReach; dx <= CollarReach; dx++)
            for (int dz = -CollarReach; dz <= CollarReach; dz++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) <= HubHalf)
                {
                    continue;
                }

                for (int dy = CollarBottom; dy <= CollarTop; dy++)
                {
                    Set(dx, dy, dz, dark);
                }
            }

        // ---- The four tube arms (dark, as on the old model), a light in each roof ----
        int armEnd = PodCentre - PodHalf - 1;
        for (int arm = 0; arm < Arms.Length; arm++)
        {
            for (int a = HubHalf + 1; a <= armEnd; a++)
                for (int s = -ArmHalf; s <= ArmHalf; s++)
                    for (int dy = Deck; dy <= ArmTop; dy++)
                    {
                        bool hollow = Math.Abs(s) < ArmHalf && dy > Deck && dy < ArmTop;
                        SetOnArm(arm, a, s, dy, hollow ? (ushort)0 : dark);
                    }

            if (light != 0)
            {
                SetOnArm(arm, (HubHalf + armEnd) / 2, 0, ArmTop, light);
            }
        }

        // ---- The pods: a cube at each arm end, a base under the deck, small windows in the side walls ----
        int podIn = PodCentre - PodHalf, podOut = PodCentre + PodHalf;
        for (int arm = 0; arm < Arms.Length; arm++)
        {
            bool hangar = arm == HangarArm;
            for (int a = podIn; a <= podOut; a++)
                for (int s = -PodHalf; s <= PodHalf; s++)
                    for (int dy = PodBottom; dy <= PodTop; dy++)
                    {
                        bool wall = a == podIn || a == podOut || Math.Abs(s) == PodHalf;
                        ushort b;
                        if (dy < Deck)
                        {
                            b = hull;
                        }
                        else if (dy == Deck)
                        {
                            b = wall ? hull : hangar ? cargoFloor : floor;
                        }
                        else if (dy == PodTop)
                        {
                            b = hull;
                        }
                        else if (wall)
                        {
                            bool window = Math.Abs(s) == PodHalf && Math.Abs(a - PodCentre) <= 1 && dy >= 0 && dy <= 1;
                            b = window ? glass : hull;
                        }
                        else
                        {
                            b = 0;
                        }

                        SetOnArm(arm, a, s, dy, b);
                    }

            if (light != 0)
            {
                foreach (int a in new[] { podIn + 1, podOut - 1 })
                {
                    foreach (int s in new[] { -(PodHalf - 1), PodHalf - 1 })
                    {
                        SetOnArm(arm, a, s, PodTop - 1, light); // ceiling corner lights
                    }
                }
            }
        }

        // ---- Doorways: a 2-wide, 3-high cut at both joints of every arm (the standard station airlock cut) ----
        var markers = new List<StationMarker>();
        for (int arm = 0; arm < Arms.Length; arm++)
        {
            string axis = Arms[arm].X != 0 ? "x" : "z"; // the wall faces along the arm
            foreach (int a in new[] { HubHalf, podIn })
            {
                for (int s = -1; s <= 0; s++)
                    for (int dy = Deck + 1; dy <= Deck + 3; dy++)
                    {
                        SetOnArm(arm, a, s, dy, 0);
                    }

                var (ax, az) = Arms[arm];
                // The marker column is the −1 side; the decay's second column is the +1 neighbour across the wall.
                var marker = new Vector3i(c.X + (ax != 0 ? ax * a : -1), c.Y + Deck + 1, c.Z + (az != 0 ? az * a : -1));
                markers.Add(new StationMarker("door_slide", marker, axis));
            }
        }

        // ---- The hangar mouth: the outer wall of the +Z pod is a force field ----
        for (int s = -(PodHalf - 1); s <= PodHalf - 1; s++)
            for (int dy = Deck + 1; dy <= Deck + 4; dy++)
            {
                SetOnArm(HangarArm, podOut, s, dy, field);
            }

        // ---- The solar wings on the outer faces of the ±X pods ----
        foreach (int side in new[] { 1, -1 })
        {
            for (int a = WingInner; a <= WingOuter; a++)
                for (int dz = -WingHalfSpan; dz <= WingHalfSpan; dz++)
                {
                    bool frame = a == WingInner || a == WingOuter || Math.Abs(dz) == WingHalfSpan
                        || (a - WingInner) % 4 == 0 || (dz + WingHalfSpan) % 5 == 0;
                    Set(side * a, WingLayer, dz, frame ? dark : glass, frame ? 0 : StationKitExterior.SolarTint);
                }
        }

        // ---- The mast with its beacon ----
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                Set(dx, MastBottom, dz, dark); // its foot
            }

        for (int dy = MastBottom; dy <= MastTop; dy++)
        {
            Set(0, dy, 0, dark);
        }

        if (beacon != 0)
        {
            Set(0, BeaconLayer, 0, beacon, BeaconTint);
        }

        // ---- Furniture: the hub's console bank, then each pod by its room ----
        int inner = HubHalf - 1;
        for (int arm = 0; arm < Arms.Length; arm++)
        {
            foreach (int s in new[] { -5, -4, -3, 3, 4, 5 })
            {
                SetOnArm(arm, inner, s, Deck + 1, dark); // a console along every wall, the doorway kept clear
            }
        }

        if (light != 0)
        {
            foreach (int dx in new[] { -inner, inner })
            {
                foreach (int dz in new[] { -inner, inner })
                {
                    Set(dx, HubTop - 1, dz, light);
                }
            }
        }

        // The pods: the hangar on +Z, the other three rooms shuffled over +X, −X and −Z.
        var rooms = new List<string> { "market", "medbay", "quarters" };
        for (int i = rooms.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (rooms[i], rooms[j]) = (rooms[j], rooms[i]);
        }

        var podArms = new[] { 0, 1, 3 };
        var podType = new string[Arms.Length];
        podType[HangarArm] = "hangar";
        for (int i = 0; i < podArms.Length; i++)
        {
            podType[podArms[i]] = rooms[i];
        }

        int feet = Deck + 1;
        for (int arm = 0; arm < Arms.Length; arm++)
        {
            switch (podType[arm])
            {
                case "market": // the old trading corner: a counter along one side, the stall in its middle
                    foreach (int a in new[] { PodCentre - 2, PodCentre - 1, PodCentre + 1, PodCentre + 2 })
                    {
                        SetOnArm(arm, a, PodHalf - 1, feet, dark);
                    }

                    markers.Add(new StationMarker("vendor", ArmCell(arm, PodCentre, PodHalf - 1, feet)));
                    break;

                case "medbay": // treatment benches and two heal tanks
                    SetOnArm(arm, PodCentre - 2, -(PodHalf - 1), feet, medPanel);
                    SetOnArm(arm, PodCentre - 1, -(PodHalf - 1), feet, medPanel);
                    SetOnArm(arm, PodCentre + 2, -(PodHalf - 1), feet, healTank);
                    SetOnArm(arm, PodCentre + 2, PodHalf - 1, feet, healTank);
                    break;

                case "quarters": // bunks along both sides, the middle lane free
                    foreach (int a in new[] { PodCentre - 2, PodCentre - 1, PodCentre + 1, PodCentre + 2 })
                    {
                        SetOnArm(arm, a, -(PodHalf - 1), feet, dark);
                        SetOnArm(arm, a, PodHalf - 1, feet, dark);
                    }

                    break;

                case "hangar": // cargo stacked by the inner wall, away from the mouth
                    SetOnArm(arm, podIn + 1, -(PodHalf - 1), feet, dark);
                    SetOnArm(arm, podIn + 1, PodHalf - 1, feet, dark);
                    SetOnArm(arm, podIn + 1, PodHalf - 1, feet + 1, dark);
                    markers.Add(new StationMarker("hangar", ArmCell(arm, PodCentre, 0, feet)));
                    break;
            }
        }

        markers.Insert(0, new StationMarker("spawn", new Vector3i(c.X, c.Y + feet, c.Z))); // the command deck's middle

        // The rooms as modules (hub, the four tubes, the four pods): origin = the room's min corner from its deck.
        var modules = new List<StationModule>
        {
            new("hub", new Vector3i(0, 0, 0), new Vector3i(c.X - HubHalf, c.Y + Deck, c.Z - HubHalf)),
        };
        for (int arm = 0; arm < Arms.Length; arm++)
        {
            var (ax, az) = Arms[arm];
            var tubeMin = ArmBoxMin(arm, HubHalf + 1, armEnd, ArmHalf);
            var podMin = ArmBoxMin(arm, podIn, podOut, PodHalf);
            modules.Add(new StationModule("corridor", new Vector3i(ax, 0, az), new Vector3i(tubeMin.X, c.Y + Deck, tubeMin.Z)));
            modules.Add(new StationModule(podType[arm], new Vector3i(2 * ax, 0, 2 * az), new Vector3i(podMin.X, c.Y + Deck, podMin.Z)));
        }

        int podSize = (2 * PodHalf) + 1;
        return new StationStructure(w, h, l, SizeTier, podSize, PodTop - Deck + 1, podSize, blocks, markers, modules, mods);

        Vector3i ArmCell(int arm, int a, int s, int dy)
        {
            var (ax, az) = Arms[arm];
            return new Vector3i(c.X + (ax != 0 ? ax * a : s), c.Y + dy, c.Z + (az != 0 ? az * a : s));
        }

        // The min corner (x, z) of the box an arm segment spans from `a0` to `a1`, `half` to each side.
        Vector3i ArmBoxMin(int arm, int a0, int a1, int half)
        {
            var (ax, az) = Arms[arm];
            int x = ax != 0 ? c.X + Math.Min(ax * a0, ax * a1) : c.X - half;
            int z = az != 0 ? c.Z + Math.Min(az * a0, az * a1) : c.Z - half;
            return new Vector3i(x, 0, z);
        }
    }
}
