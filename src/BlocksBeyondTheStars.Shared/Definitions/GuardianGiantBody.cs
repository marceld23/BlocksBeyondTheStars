// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>The six rigid limbs of a guardian giant (#3676). Each is one small voxel grid; the client meshes each with the
/// block atlas and moves it as a bone, the server stamps all of them as the lying block body.</summary>
public enum GuardianGiantPart : byte
{
    Torso = 0,
    Head = 1,
    ArmLeft = 2,
    ArmRight = 3,
    LegLeft = 4,
    LegRight = 5,
}

/// <summary>One cell of a guardian giant's body in the <b>standing</b> pose, body-local (see <see cref="GuardianGiantBody"/>).</summary>
public readonly struct GuardianGiantCell
{
    public GuardianGiantCell(int x, int y, int z, string block, GuardianGiantPart part, bool isLight)
    {
        X = x;
        Y = y;
        Z = z;
        Block = block;
        Part = part;
        IsLight = isLight;
    }

    public int X { get; }

    public int Y { get; }

    public int Z { get; }

    /// <summary>The block key (an existing block — the giant adds none).</summary>
    public string Block { get; }

    /// <summary>The limb the cell moves with.</summary>
    public GuardianGiantPart Part { get; }

    /// <summary>One of the giant's lights (the visor, the chest core, the reactor, the shoulder lamps): <see
    /// cref="GuardianGiantBody.LightOn"/> while lit, <see cref="GuardianGiantBody.LightOff"/> otherwise.</summary>
    public bool IsLight { get; }
}

/// <summary>
/// The guardian giant's body (#3676): the Guardian's hunter robot — plated legs and torso, broad shoulder plates, forearm
/// blades, one wide visor under a crest, a reactor on the back — 14 blocks tall and built only from existing blocks. One
/// definition for both sides: the server stamps it as the lying block body (<see cref="Lying"/>), the client meshes each
/// <see cref="GuardianGiantPart"/> as a rigid limb and animates it (<see cref="Standing"/>, <see cref="Joint"/>), so the
/// giant that stands up is cell for cell the one that lay there.
/// <para><b>Coordinates.</b> Standing, body-local: Y up, the front faces +Z, the root (0, 0, 0) is the ground point between
/// the feet; cell <c>i</c> spans <c>[i, i + 1]</c> on each axis, so the body is symmetric about x = 0 (cells −6 … 5).</para>
/// <para><b>Lying.</b> The body fell backwards about its root: a standing point (x, y, z) lies at (x, z, −y) — the front faces
/// up, the head points to −Z (<see cref="ToLying"/>; in Unity terms <c>Quaternion.Euler(-90, 0, 0)</c> about the root). Then
/// the facing turns it about +Y in quarter turns: (x, z) → (z, −x) per turn (<see cref="Face"/>; Unity yaw +90° per turn).
/// A record's anchor is the root's world cell in that lying pose; the lowest lying row is <see cref="LyingBottom"/> below it.</para>
/// </summary>
public static class GuardianGiantBody
{
    /// <summary>The standing height in blocks (crest top).</summary>
    public const int Height = 14;

    /// <summary>The lowest lying row relative to the anchor (the reactor on the back): the stamper sets the anchor this many
    /// rows above the row the back rests in.</summary>
    public const int LyingBottom = -2;

    /// <summary>The highest lying row relative to the anchor (the visor, the toes, the blades).</summary>
    public const int LyingTop = 1;

    /// <summary>The plated cells.</summary>
    public const string Plate = "metal_panel";

    /// <summary>The plated cells that rusted (seeded per cell, <see cref="RustShare"/>).</summary>
    public const string Rust = "rusted_panel";

    /// <summary>Joints, pelvis, neck, hands, feet, blades, horns.</summary>
    public const string Dark = "steel_wall";

    /// <summary>Shoulder plates, knee plates, the crest.</summary>
    public const string Trim = "titanium_block";

    /// <summary>A light while the giant is lit.</summary>
    public const string LightOn = "light_red";

    /// <summary>A light while the giant is dark (dead or not yet woken).</summary>
    public const string LightOff = "light_red_off";

    /// <summary>The share of plated cells that are rusted.</summary>
    public const double RustShare = 0.3;

    private static readonly GuardianGiantPart[] AllParts =
    {
        GuardianGiantPart.Torso, GuardianGiantPart.Head, GuardianGiantPart.ArmLeft, GuardianGiantPart.ArmRight,
        GuardianGiantPart.LegLeft, GuardianGiantPart.LegRight,
    };

    /// <summary>Every limb, torso first.</summary>
    public static IReadOnlyList<GuardianGiantPart> Parts => AllParts;

    /// <summary>The limb a limb hangs from (the client's bone parent): the head and the arms hang from the torso; the torso
    /// and the legs from the root.</summary>
    public static GuardianGiantPart? Parent(GuardianGiantPart part) => part switch
    {
        GuardianGiantPart.Head or GuardianGiantPart.ArmLeft or GuardianGiantPart.ArmRight => GuardianGiantPart.Torso,
        _ => null,
    };

    /// <summary>The joint a limb turns about, in standing body-local coordinates (block units): the pelvis, the neck, the
    /// shoulders, the hips.</summary>
    public static (float X, float Y, float Z) Joint(GuardianGiantPart part) => part switch
    {
        GuardianGiantPart.Torso => (0f, 6.5f, 0f),
        GuardianGiantPart.Head => (0f, 11f, 0f),
        GuardianGiantPart.ArmLeft => (-4f, 9f, 0f),
        GuardianGiantPart.ArmRight => (4f, 9f, 0f),
        GuardianGiantPart.LegLeft => (-2f, 6f, 0f),
        GuardianGiantPart.LegRight => (2f, 6f, 0f),
        _ => (0f, 0f, 0f),
    };

    /// <summary>The visor's centre (standing body-local): where the laser starts and the search light shines from.</summary>
    public static (float X, float Y, float Z) Visor => (0f, 12.5f, 2f);

    /// <summary>The body in the standing pose: every cell, its block and its limb. <paramref name="seed"/> picks the rusted
    /// plates (the same seed gives the same body on the server and every client); <paramref name="lit"/> picks the lights'
    /// block.</summary>
    public static List<GuardianGiantCell> Standing(int seed, bool lit)
    {
        var cells = new List<GuardianGiantCell>(260);
        var b = new Builder(cells, seed, lit);
        BuildLeg(b, GuardianGiantPart.LegLeft, -3);
        BuildLeg(b, GuardianGiantPart.LegRight, 1);
        BuildTorso(b);
        BuildArm(b, GuardianGiantPart.ArmLeft, mirrored: false);
        BuildArm(b, GuardianGiantPart.ArmRight, mirrored: true);
        BuildHead(b);
        return cells;
    }

    /// <summary>A standing body-local cell in the lying pose (before the facing), relative to the anchor.</summary>
    public static (int X, int Y, int Z) ToLying(int x, int y, int z) => (x, z, -y - 1);

    /// <summary>A lying cell's (x, z) turned by <paramref name="facing"/> quarter turns about +Y (Unity yaw +90° each).</summary>
    public static (int X, int Z) Face(int x, int z, int facing)
    {
        switch (((facing % 4) + 4) % 4)
        {
            case 1:
                return (z, -x - 1);
            case 2:
                return (-x - 1, -z - 1);
            case 3:
                return (-z - 1, x);
            default:
                return (x, z);
        }
    }

    /// <summary>The lying body relative to its anchor: every cell turned to <paramref name="facing"/>, with its block.</summary>
    public static List<(int Dx, int Dy, int Dz, string Block, bool IsLight)> Lying(int seed, bool lit, int facing)
    {
        var standing = Standing(seed, lit);
        var lying = new List<(int, int, int, string, bool)>(standing.Count);
        foreach (var c in standing)
        {
            var (lx, ly, lz) = ToLying(c.X, c.Y, c.Z);
            var (fx, fz) = Face(lx, lz, facing);
            lying.Add((fx, ly, fz, c.Block, c.IsLight));
        }

        return lying;
    }

    /// <summary>The lying body's flat box relative to the anchor at <paramref name="facing"/> (inclusive cells).</summary>
    public static (int MinX, int MinZ, int MaxX, int MaxZ) LyingFootprint(int facing)
    {
        int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
        foreach (var (dx, _, dz, _, _) in Lying(0, lit: false, facing))
        {
            minX = System.Math.Min(minX, dx);
            minZ = System.Math.Min(minZ, dz);
            maxX = System.Math.Max(maxX, dx);
            maxZ = System.Math.Max(maxZ, dz);
        }

        return (minX, minZ, maxX, maxZ);
    }

    /// <summary>The lying body's flat centre relative to the anchor (the point the wake and stir ranges are measured from).</summary>
    public static (float X, float Z) LyingCentre(int facing)
    {
        var (minX, minZ, maxX, maxZ) = LyingFootprint(facing);
        return ((minX + maxX + 1) * 0.5f, (minZ + maxZ + 1) * 0.5f);
    }

    // ---------------------------------------------------------------- the limbs

    /// <summary>A leg: a broad foot with a toe forward, the shin, the knee plate, the thigh (cells y 0–5).</summary>
    private static void BuildLeg(Builder b, GuardianGiantPart part, int x0)
    {
        for (int x = x0; x <= x0 + 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                b.Add(x, 0, z, Dark, part); // the foot, its toe at z = 1
            }

            for (int y = 1; y <= 5; y++)
            {
                for (int z = -1; z <= 0; z++)
                {
                    b.Add(x, y, z, y == 3 && z == 0 ? Trim : Plate, part); // the knee plate in front
                }
            }
        }
    }

    /// <summary>The torso: the dark pelvis, the plated chest with the core light in front and the reactor lights behind, the
    /// neck (cells y 6–10).</summary>
    private static void BuildTorso(Builder b)
    {
        const GuardianGiantPart part = GuardianGiantPart.Torso;
        for (int x = -3; x <= 2; x++)
        {
            for (int z = -1; z <= 0; z++)
            {
                b.Add(x, 6, z, Dark, part);
            }

            for (int y = 7; y <= 9; y++)
            {
                for (int z = -2; z <= 0; z++)
                {
                    bool core = z == 0 && y == 8 && (x == -1 || x == 0);
                    bool reactor = z == -2 && (y == 7 || y == 8) && (x == -1 || x == 0);
                    if (core || reactor)
                    {
                        b.Light(x, y, z, part);
                    }
                    else
                    {
                        b.Add(x, y, z, Plate, part);
                    }
                }
            }
        }

        for (int x = -1; x <= 0; x++)
        {
            for (int z = -1; z <= 0; z++)
            {
                b.Add(x, 10, z, Dark, part);
            }
        }
    }

    /// <summary>An arm: the broad shoulder plate with its lamp, the upper arm, the elbow, the forearm with its blade forward,
    /// the hand (cells y 2–9). The right arm is the left one mirrored (x → −x − 1).</summary>
    private static void BuildArm(Builder b, GuardianGiantPart part, bool mirrored)
    {
        int Mx(int x) => mirrored ? -x - 1 : x;
        for (int x = -6; x <= -4; x++)
        {
            for (int z = -2; z <= 0; z++)
            {
                if (x == -6 && z == 0)
                {
                    b.Light(Mx(x), 9, z, part); // the shoulder lamp
                }
                else
                {
                    b.Add(Mx(x), 9, z, Trim, part);
                }
            }
        }

        for (int x = -5; x <= -4; x++)
        {
            for (int z = -1; z <= 0; z++)
            {
                for (int y = 2; y <= 8; y++)
                {
                    string block = y == 2 || y == 5 ? Dark : Plate; // the hand, the elbow
                    b.Add(Mx(x), y, z, block, part);
                }
            }
        }

        b.Add(Mx(-5), 3, 1, Dark, part); // the forearm blade
        b.Add(Mx(-5), 4, 1, Dark, part);
    }

    /// <summary>The head: a plated box with the wide visor in its upper front row, a dark jaw below it, the crest and two short
    /// horns on top (cells y 11–13).</summary>
    private static void BuildHead(Builder b)
    {
        const GuardianGiantPart part = GuardianGiantPart.Head;
        for (int x = -2; x <= 1; x++)
        {
            for (int y = 11; y <= 12; y++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    if (z == 1 && y == 12)
                    {
                        b.Light(x, y, z, part); // the visor
                    }
                    else
                    {
                        b.Add(x, y, z, z == 1 ? Dark : Plate, part);
                    }
                }
            }
        }

        for (int x = -1; x <= 0; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                b.Add(x, 13, z, Trim, part); // the crest, front to back
            }
        }

        b.Add(-2, 13, -1, Dark, part); // the horns
        b.Add(1, 13, -1, Dark, part);
    }

    /// <summary>Collects the cells; plates rust by a hash of the seed and the cell, lights take the lit or dark block.</summary>
    private sealed class Builder
    {
        private readonly List<GuardianGiantCell> _cells;
        private readonly int _seed;
        private readonly bool _lit;

        public Builder(List<GuardianGiantCell> cells, int seed, bool lit)
        {
            _cells = cells;
            _seed = seed;
            _lit = lit;
        }

        public void Add(int x, int y, int z, string block, GuardianGiantPart part)
        {
            if (block == Plate && Rusted(x, y, z))
            {
                block = Rust;
            }

            _cells.Add(new GuardianGiantCell(x, y, z, block, part, isLight: false));
        }

        public void Light(int x, int y, int z, GuardianGiantPart part)
            => _cells.Add(new GuardianGiantCell(x, y, z, _lit ? LightOn : LightOff, part, isLight: true));

        private bool Rusted(int x, int y, int z)
        {
            unchecked
            {
                uint h = (uint)_seed * 0x9E3779B1u;
                h ^= (uint)(x + 64) * 0x85EBCA6Bu;
                h ^= (uint)(y + 64) * 0xC2B2AE35u;
                h ^= (uint)(z + 64) * 0x27D4EB2Fu;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                return (h % 1000u) < (uint)(RustShare * 1000.0);
            }
        }
    }
}
