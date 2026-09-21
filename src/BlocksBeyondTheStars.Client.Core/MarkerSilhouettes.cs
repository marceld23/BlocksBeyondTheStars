// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;
using System.Numerics;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What a build-editor marker or ship part LOOKS like in the build (#1975): a stylised box model in the
    /// marker's colour, standing in the authored cell, instead of the small cube the editors used to draw. An NPC
    /// post shows a figure two blocks tall, the mission board a board on a post, a chest a chest, a terminal a
    /// pedestal with a screen, an area marker (room, spawn) a plate on the floor, the ship hatch a frame around the
    /// opening, and a ship station keeps its block and gets the decor the game sets on the cell above (console
    /// and hologram, the heal capsule, the workbench). Doors are not here — they are the real
    /// <see cref="DoorGeometry"/>.
    /// <para>Boxes live in the cell's frame: the cell spans 0..1 on X and Z, the floor is y = 0, the cell above
    /// ends at y = 2. Unity-free so the headless suite pins that every marker id resolves and stays in its column.</para>
    /// </summary>
    public static class MarkerSilhouettes
    {
        /// <summary>The kinds of palette entry the editors have: a structure marker, a ship element, a ship station.</summary>
        public const string KindMarker = "marker", KindElement = "element", KindStation = "station";

        /// <summary>One axis-aligned box of a silhouette: centre and full size in the cell frame.</summary>
        public readonly struct Box
        {
            public Box(float cx, float cy, float cz, float sx, float sy, float sz)
            {
                Centre = new Vector3(cx, cy, cz);
                Size = new Vector3(sx, sy, sz);
            }

            public Vector3 Centre { get; }

            public Vector3 Size { get; }
        }

        /// <summary>A resolved silhouette: its boxes, and whether the cell's own cube is still drawn under them
        /// (a ship station is a real block with decor on top; a marker is a point, not a voxel).</summary>
        public readonly struct Silhouette
        {
            public Silhouette(IReadOnlyList<Box> boxes, bool keepsCube)
            {
                Boxes = boxes;
                KeepsCube = keepsCube;
            }

            public IReadOnlyList<Box> Boxes { get; }

            public bool KeepsCube { get; }
        }

        // A person: head, torso, two legs — about 1.75 tall, the height of the avatar rig.
        private static readonly Box[] Figure =
        {
            new Box(0.5f, 1.55f, 0.5f, 0.40f, 0.40f, 0.40f),   // head
            new Box(0.5f, 1.00f, 0.5f, 0.50f, 0.60f, 0.30f),   // torso
            new Box(0.38f, 0.35f, 0.5f, 0.20f, 0.70f, 0.25f),  // left leg
            new Box(0.62f, 0.35f, 0.5f, 0.20f, 0.70f, 0.25f),  // right leg
        };

        // The mission board: a post carrying a wide board at eye height.
        private static readonly Box[] Board =
        {
            new Box(0.5f, 0.50f, 0.5f, 0.10f, 1.00f, 0.10f),
            new Box(0.5f, 1.30f, 0.5f, 0.90f, 0.60f, 0.08f),
        };

        // A chest with its lid.
        private static readonly Box[] Chest =
        {
            new Box(0.5f, 0.30f, 0.5f, 0.80f, 0.60f, 0.60f),
            new Box(0.5f, 0.65f, 0.5f, 0.84f, 0.10f, 0.64f),
        };

        // A terminal: pedestal + tilted screen (drawn upright here).
        private static readonly Box[] Terminal =
        {
            new Box(0.5f, 0.40f, 0.5f, 0.50f, 0.80f, 0.40f),
            new Box(0.5f, 0.95f, 0.45f, 0.70f, 0.40f, 0.06f),
        };

        // An area marker: a thin plate on the floor — the cell is a seed / a point, not a thing.
        private static readonly Box[] Plate =
        {
            new Box(0.5f, 0.04f, 0.5f, 0.90f, 0.08f, 0.90f),
        };

        // The ship hatch: an opening — four bars framing the cell's face so the author sees "hole, not block".
        private static readonly Box[] Frame =
        {
            new Box(0.5f, 0.04f, 0.5f, 1.00f, 0.08f, 0.16f),
            new Box(0.5f, 0.96f, 0.5f, 1.00f, 0.08f, 0.16f),
            new Box(0.04f, 0.5f, 0.5f, 0.08f, 1.00f, 0.16f),
            new Box(0.96f, 0.5f, 0.5f, 0.08f, 1.00f, 0.16f),
        };

        // Ship-station decor sits on the cell ABOVE the station block (StationDecorView puts it at y + 1).
        private static readonly Box[] ConsoleDecor =
        {
            new Box(0.5f, 1.18f, 0.5f, 0.85f, 0.36f, 0.45f),  // console base
            new Box(0.5f, 1.52f, 0.45f, 0.72f, 0.34f, 0.05f), // screen
        };

        private static readonly Box[] CockpitDecor =
        {
            new Box(0.5f, 1.18f, 0.5f, 0.85f, 0.36f, 0.45f),  // console base
            new Box(0.5f, 1.52f, 0.45f, 0.72f, 0.34f, 0.05f), // screen
            new Box(0.5f, 2.15f, 0.5f, 0.50f, 0.50f, 0.50f),  // the holographic system map
        };

        private static readonly Box[] CapsuleDecor =
        {
            new Box(0.5f, 1.75f, 0.5f, 0.55f, 1.50f, 0.55f),  // the heal tank
        };

        private static readonly Box[] BenchDecor =
        {
            new Box(0.5f, 1.40f, 0.5f, 0.90f, 0.10f, 0.50f),  // bench top
            new Box(0.25f, 1.18f, 0.5f, 0.10f, 0.36f, 0.45f), // legs
            new Box(0.75f, 1.18f, 0.5f, 0.10f, 0.36f, 0.45f),
        };

        /// <summary>Structure markers that become a person standing there (an NPC post, a resident, a crew member).</summary>
        private static readonly HashSet<string> FigureMarkers = new HashSet<string>(System.StringComparer.Ordinal)
        {
            "npc", "vendor", "guard_post", "tavern", "workshop", "quarters", "cabin", "hangar", "greenhouse", "heal_tank", "lounge",
            "doctor", "grocer", "arms_dealer", "sage", "tamer", "blockfarmer", "streamer", "reporter",
        };

        /// <summary>
        /// The silhouette for a palette entry, or null when the entry keeps the plain cube it always had (a light,
        /// the engine, a station without decor, a block). Doors return null too: the editors draw those from
        /// <see cref="DoorGeometry"/>.
        /// </summary>
        public static Silhouette? For(string id, string kind)
        {
            if (id == null)
            {
                return null;
            }

            switch (kind)
            {
                case KindMarker:
                    if (FigureMarkers.Contains(id))
                    {
                        return new Silhouette(Figure, keepsCube: false);
                    }

                    return id switch
                    {
                        "mission_board" => new Silhouette(Board, keepsCube: false),
                        "chest" or "loot" => new Silhouette(Chest, keepsCube: false),
                        "data_terminal" or "console" => new Silhouette(Terminal, keepsCube: false),
                        "room" or "spawn" => new Silhouette(Plate, keepsCube: false),
                        _ => (Silhouette?)null,
                    };

                case KindElement:
                    return id == "hatch" ? new Silhouette(Frame, keepsCube: false) : (Silhouette?)null;

                case KindStation:
                    return id switch
                    {
                        "cockpit" => new Silhouette(CockpitDecor, keepsCube: true),
                        "console" or "lab" => new Silhouette(ConsoleDecor, keepsCube: true),
                        "medbay" => new Silhouette(CapsuleDecor, keepsCube: true),
                        "workshop" => new Silhouette(BenchDecor, keepsCube: true),
                        _ => (Silhouette?)null,
                    };

                default:
                    return null;
            }
        }
    }
}
