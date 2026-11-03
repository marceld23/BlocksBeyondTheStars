// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The ship's rear hatch drawn as the CLOSED door it is on the ground (#3159): the two slide panels, their seams and the
    /// jamb posts of the energy door's shared <see cref="DoorGeometry"/>, in the ground door's own materials
    /// (<see cref="PropTextures"/>) — no blue field, which a closed energy door never shows on the ground. One door per opening
    /// a <see cref="ShipHatch.Plan"/> names, scaled to the opening's height. Used by the flight view (cruise, the landing
    /// descent, the climb out) and by the transition cinematics' hulls, which carry no door of their own: the parked ship's
    /// hatch is a separate <see cref="DoorView"/> entity. The meshes are the door's own (never the shared primitive cube): a
    /// cinematic hull is torn down with <see cref="ShipCinematicFx.DestroyWithMeshes"/>.
    /// </summary>
    internal static class ShipHatchDoor
    {
        /// <summary>The hatch openings of a parked hull, guessed from its cells as the flight view does without server hatch
        /// cells (<see cref="ShipHatch.Find"/>). Cell (x, y, z) is the hull root's local (x, y, z).</summary>
        public static ShipHatch.Plan PlanFor(LandedShipModel model)
        {
            var cells = new List<(int X, int Y, int Z)>(model.Cells.Count);
            foreach (var c in model.Cells.Keys)
            {
                cells.Add((c.X, c.Y, c.Z));
            }

            return ShipHatch.Find(cells, model.Width, model.Height, model.Length);
        }

        /// <summary>Builds the closed doors under <paramref name="parent"/>, a cell's corner at <c>cell − <paramref name="origin"/></c>
        /// in the parent's space; null when the plan names no opening (a sealed stern). The flight view passes its own panel
        /// and trim materials (lit for the flight scene, like the doors the owner built); the surface uses the ground door's.</summary>
        public static GameObject Build(Transform parent, ShipHatch.Plan plan, Vector3 origin, Material panelMat = null, Material trimMat = null)
        {
            if (parent == null || plan == null || plan.Panels.Count == 0)
            {
                return null;
            }

            var door = new GameObject("HatchDoor");
            door.transform.SetParent(parent, false);
            panelMat ??= PropTextures.MaterialFor(PropTextures.DoorSlidePanel);
            trimMat ??= PropTextures.MaterialFor(PropTextures.DoorSlideTrim);
            var panels = new List<DoorGeometry.Box>();
            var trim = new List<DoorGeometry.Box>();
            foreach (var p in plan.Panels)
            {
                // The door stands in its wall plane: across X in the stern (the geometry's own frame), across Z in a side
                // wall (a self-built ship's side door) — the same door turned a quarter about Y. Its origin is the gap's
                // floor centre, as for every door.
                float w = p.FacesZ ? p.MaxX - p.MinX + 1 : p.MaxZ - p.MinZ + 1;
                float h = p.MaxY - p.MinY + 1;
                var holder = new GameObject("HatchPanel").transform;
                holder.SetParent(door.transform, false);
                holder.localPosition = new Vector3((p.MinX + p.MaxX + 1) * 0.5f, p.MinY, (p.MinZ + p.MaxZ + 1) * 0.5f) - origin;
                holder.localRotation = p.FacesZ ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
                // The ground door stands 2.8 m in a 3-high doorway: the same here, and a lower opening gets a shorter door.
                holder.localScale = new Vector3(1f, h / 3f, 1f);

                panels.Clear();
                trim.Clear();
                foreach (var box in DoorGeometry.Closed(DoorBlocks.Energy, w))
                {
                    if (box.Part == DoorGeometry.Part.Field)
                    {
                        continue; // closed: no field (it shows only in an open energy doorway)
                    }

                    bool panel = box.Part == DoorGeometry.Part.PanelMinus || box.Part == DoorGeometry.Part.PanelPlus;
                    (panel ? panels : trim).Add(box);
                }

                AddPart(holder, "Panels", panels, panelMat);
                AddPart(holder, "Trim", trim, trimMat); // the seams and the glowing jamb posts, as on the ground
            }

            return door;
        }

        private static void AddPart(Transform parent, string name, List<DoorGeometry.Box> boxes, Material mat)
        {
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();
            DoorMesh.Append(verts, cols, tris, boxes, axisX: true, Vector3.zero, _ => Color.white, withField: false);
            if (verts.Count == 0)
            {
                return;
            }

            var mesh = new Mesh { name = "Hatch" + name };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }
}
