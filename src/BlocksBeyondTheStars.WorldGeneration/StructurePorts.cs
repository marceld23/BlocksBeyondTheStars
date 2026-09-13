// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>The six outer faces of a template's bounding box, numbered like the shape up-faces.</summary>
public enum PortFace
{
    PlusY = 0,
    MinusY = 1,
    PlusX = 2,
    MinusX = 3,
    PlusZ = 4,
    MinusZ = 5,
}

/// <summary>
/// One docking port of a module (#1873): a filled rectangle of wall cells on one outer face, tagged so only equal
/// ports dock (same tag, same size, opposite faces). Positions are template-local; <see cref="A0"/>/<see cref="B0"/>
/// span the face plane — y and z on an X face, y and x on a Z face, x and z on a Y face.
/// </summary>
public sealed class PortRect
{
    public string Tag = string.Empty;
    public string Door = StructurePorts.DoorSlide;
    public PortFace Face;
    public int A0, B0, SizeA, SizeB;
    public readonly List<Vector3i> Cells = new();

    /// <summary>The face's fixed coordinate (x on an X face, z on a Z face, y on a Y face).</summary>
    public int Depth;

    /// <summary>The unit step from this face toward the outside of the template.</summary>
    public Vector3i Outward => StructurePorts.Outward(Face);

    /// <summary>Whether <paramref name="other"/> may dock onto this port: same tag, same rectangle, opposite faces.</summary>
    public bool Compatible(PortRect other)
        => Tag == other.Tag && SizeA == other.SizeA && SizeB == other.SizeB && StructurePorts.Opposite(Face) == other.Face;
}

/// <summary>The port grammar, collection and validation shared by the editor's checks and the composers.</summary>
public static class StructurePorts
{
    public const string DoorSlide = "slide";
    public const string DoorEnergy = "energy";
    public const string DoorHinge = "hinge";
    public const string DoorOpen = "open";
    public const string TagDoor = "door";
    public const string TagWide = "wide";
    public const string TagLadder = "ladder";

    public static readonly string[] DoorOptions = { DoorSlide, DoorEnergy, DoorHinge, DoorOpen };

    /// <summary>Splits <c>tag[:door]</c>; an absent door option means a slide door. Returns false for an empty tag or an
    /// unknown door option.</summary>
    public static bool TryParse(string? port, out string tag, out string door)
    {
        tag = string.Empty;
        door = DoorSlide;
        if (string.IsNullOrWhiteSpace(port))
        {
            return false;
        }

        var parts = port!.Trim().ToLowerInvariant().Split(':');
        tag = parts[0].Trim();
        if (tag.Length == 0)
        {
            return false;
        }

        if (parts.Length > 1)
        {
            door = parts[1].Trim();
            if (System.Array.IndexOf(DoorOptions, door) < 0)
            {
                return false;
            }
        }

        return parts.Length <= 2;
    }

    public static PortFace Opposite(PortFace f) => f switch
    {
        PortFace.PlusY => PortFace.MinusY,
        PortFace.MinusY => PortFace.PlusY,
        PortFace.PlusX => PortFace.MinusX,
        PortFace.MinusX => PortFace.PlusX,
        PortFace.PlusZ => PortFace.MinusZ,
        _ => PortFace.PlusZ,
    };

    public static Vector3i Outward(PortFace f) => f switch
    {
        PortFace.PlusY => new Vector3i(0, 1, 0),
        PortFace.MinusY => new Vector3i(0, -1, 0),
        PortFace.PlusX => new Vector3i(1, 0, 0),
        PortFace.MinusX => new Vector3i(-1, 0, 0),
        PortFace.PlusZ => new Vector3i(0, 0, 1),
        _ => new Vector3i(0, 0, -1),
    };

    /// <summary>The outer face a cell lies on, or null (inside, or on two faces at once — a corner never ports).</summary>
    public static PortFace? FaceOf(StructureTemplate t, int x, int y, int z)
    {
        PortFace? face = null;
        int n = 0;
        if (x == 0) { face = PortFace.MinusX; n++; }
        if (x == t.Width - 1) { face = PortFace.PlusX; n++; }
        if (z == 0) { face = PortFace.MinusZ; n++; }
        if (z == t.Length - 1) { face = PortFace.PlusZ; n++; }
        if (y == 0) { face = PortFace.MinusY; n++; }
        if (y == t.Height - 1) { face = PortFace.PlusY; n++; }
        return n == 1 ? face : null;
    }

    /// <summary>The (A, B) plane coordinates of a cell on a face.</summary>
    public static (int A, int B) PlaneOf(PortFace face, int x, int y, int z) => face switch
    {
        PortFace.PlusX or PortFace.MinusX => (y, z),
        PortFace.PlusZ or PortFace.MinusZ => (y, x),
        _ => (x, z),
    };

    /// <summary>
    /// Groups the template's port cells into rectangles: same face, same port string, 4-connected in the face plane.
    /// Cells that violate the rules (a corner, a marker, an unparsable port) are reported in <paramref name="errors"/>
    /// and left out; a component that is not a filled rectangle, or one whose inner side is not air, is reported too
    /// (and still returned, so the editor can highlight it).
    /// </summary>
    public static List<PortRect> Collect(StructureTemplate t, List<string>? errors = null)
    {
        var groups = new Dictionary<(PortFace Face, string Port), List<TemplateCell>>();
        var occupied = new HashSet<Vector3i>();
        foreach (var c in t.Cells)
        {
            if (c.Kind == "block")
            {
                occupied.Add(new Vector3i(c.X, c.Y, c.Z));
            }
        }

        foreach (var c in t.Cells)
        {
            if (string.IsNullOrWhiteSpace(c.Port))
            {
                continue;
            }

            if (c.Kind != "block")
            {
                errors?.Add($"port '{c.Port}' at ({c.X},{c.Y},{c.Z}): a port sits on a wall block, not on a marker");
                continue;
            }

            if (!TryParse(c.Port, out _, out _))
            {
                errors?.Add($"port '{c.Port}' at ({c.X},{c.Y},{c.Z}): unknown port (use tag or tag:slide|energy|hinge|open)");
                continue;
            }

            var face = FaceOf(t, c.X, c.Y, c.Z);
            if (face is null)
            {
                errors?.Add($"port '{c.Port}' at ({c.X},{c.Y},{c.Z}): a port must lie on exactly one outer face of the module");
                continue;
            }

            var key = (face.Value, c.Port.Trim().ToLowerInvariant());
            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = new List<TemplateCell>();
            }

            list.Add(c);
        }

        var result = new List<PortRect>();
        foreach (var kv in groups)
        {
            var (face, port) = kv.Key;
            TryParse(port, out string tag, out string door);
            var remaining = new HashSet<Vector3i>();
            foreach (var c in kv.Value)
            {
                remaining.Add(new Vector3i(c.X, c.Y, c.Z));
            }

            while (remaining.Count > 0)
            {
                // Flood one 4-connected component in the face plane.
                var component = new List<Vector3i>();
                var queue = new Queue<Vector3i>();
                var first = default(Vector3i);
                foreach (var c in remaining)
                {
                    first = c;
                    break;
                }

                remaining.Remove(first);
                queue.Enqueue(first);
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    component.Add(c);
                    foreach (var n in PlaneNeighbours(face, c))
                    {
                        if (remaining.Remove(n))
                        {
                            queue.Enqueue(n);
                        }
                    }
                }

                var rect = new PortRect { Tag = tag, Door = door, Face = face };
                int a0 = int.MaxValue, b0 = int.MaxValue, a1 = int.MinValue, b1 = int.MinValue;
                foreach (var c in component)
                {
                    var (a, b) = PlaneOf(face, c.X, c.Y, c.Z);
                    a0 = System.Math.Min(a0, a); a1 = System.Math.Max(a1, a);
                    b0 = System.Math.Min(b0, b); b1 = System.Math.Max(b1, b);
                    rect.Cells.Add(c);
                }

                rect.A0 = a0;
                rect.B0 = b0;
                rect.SizeA = a1 - a0 + 1;
                rect.SizeB = b1 - b0 + 1;
                rect.Depth = face switch
                {
                    PortFace.PlusX or PortFace.MinusX => component[0].X,
                    PortFace.PlusZ or PortFace.MinusZ => component[0].Z,
                    _ => component[0].Y,
                };

                if (rect.Cells.Count != rect.SizeA * rect.SizeB)
                {
                    errors?.Add($"port '{port}' on the {face} face at ({component[0].X},{component[0].Y},{component[0].Z}): the port cells must form a filled rectangle");
                }

                var inward = Outward(face) * -1;
                foreach (var c in rect.Cells)
                {
                    if (occupied.Contains(c + inward))
                    {
                        errors?.Add($"port '{port}' at ({c.X},{c.Y},{c.Z}): the cell behind a port must be air (the way in)");
                        break;
                    }
                }

                result.Add(rect);
            }
        }

        // Stable order: by face, then plane position — the composer's determinism contract.
        result.Sort((p, q) =>
        {
            int d = p.Face.CompareTo(q.Face);
            if (d != 0) return d;
            d = p.A0.CompareTo(q.A0);
            if (d != 0) return d;
            d = p.B0.CompareTo(q.B0);
            return d != 0 ? d : string.CompareOrdinal(p.Tag, q.Tag);
        });
        return result;
    }

    private static IEnumerable<Vector3i> PlaneNeighbours(PortFace face, Vector3i c)
    {
        switch (face)
        {
            case PortFace.PlusX:
            case PortFace.MinusX:
                yield return new Vector3i(c.X, c.Y + 1, c.Z);
                yield return new Vector3i(c.X, c.Y - 1, c.Z);
                yield return new Vector3i(c.X, c.Y, c.Z + 1);
                yield return new Vector3i(c.X, c.Y, c.Z - 1);
                break;
            case PortFace.PlusZ:
            case PortFace.MinusZ:
                yield return new Vector3i(c.X, c.Y + 1, c.Z);
                yield return new Vector3i(c.X, c.Y - 1, c.Z);
                yield return new Vector3i(c.X + 1, c.Y, c.Z);
                yield return new Vector3i(c.X - 1, c.Y, c.Z);
                break;
            default:
                yield return new Vector3i(c.X + 1, c.Y, c.Z);
                yield return new Vector3i(c.X - 1, c.Y, c.Z);
                yield return new Vector3i(c.X, c.Y, c.Z + 1);
                yield return new Vector3i(c.X, c.Y, c.Z - 1);
                break;
        }
    }

    /// <summary>Every port error of a template (empty = the ports are fine; a template without ports is fine too).</summary>
    public static List<string> Validate(StructureTemplate t)
    {
        var errors = new List<string>();
        Collect(t, errors);
        return errors;
    }
}
