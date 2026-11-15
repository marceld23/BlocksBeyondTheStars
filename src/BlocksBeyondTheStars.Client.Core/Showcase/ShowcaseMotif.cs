// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.Client.Showcase
{
    /// <summary>
    /// The one fixed motif of the shell (#3705, maintainer decision 2026-11-15): the corvette — its shipped design from
    /// <c>data/ship_layouts/ship_corvette.json</c> — in front of a "varied" world with clouds. The menu backdrop, the title
    /// splash, the loading screen, the first-start intro film and the poster capture all show it; nothing here needs a server
    /// or a world (the menu is up before either exists). The planet is a world the client generates itself from the constants
    /// below, through the same map bake and orbit look as the flight view; the ship is built cell by cell with the server's
    /// own mapping (<see cref="ShipCellBlocks"/>), so it is the corvette a player flies.
    /// <para>The seed and the body key are art-tunable: they pick which continents and seas the planet shows. Changing the
    /// terrain generator (a new generation) may redraw the planet — on purpose: the menu shows today's worlds.</para>
    /// </summary>
    public static class ShowcaseMotif
    {
        /// <summary>The ship type (<c>data/ships.json</c>) whose layout the motif shows.</summary>
        public const string ShipType = "corvette";

        /// <summary>The planet type (<c>data/planets.json</c>): a breathable world of grass, sand, swamp and stone biomes.</summary>
        public const string PlanetType = "varied";

        /// <summary>The world seed of the showcase planet (art-tunable).</summary>
        public const long WorldSeed = 32L;

        /// <summary>The planet's body id — salts its terrain and keys its sky, cloud and flora colours.</summary>
        public const string BodyId = "showcase-p1";

        /// <summary>The "System · Body" key its vegetation colour is rolled with.</summary>
        public const string FloraKey = "Showcase · Home";

        /// <summary>Continents on (the save option): large land masses and open seas read best from far away.</summary>
        public const bool Continents = true;

        /// <summary>The terrain generation the planet is generated with — always today's.</summary>
        public static int Generation => WorldDescription.CurrentTerrainGeneration;

        /// <summary>The planet's walkable circumference, sized like any start planet of today's generation.</summary>
        public static int Circumference
            => WorldConstants.CircumferenceFor(BodyId, WorldConstants.WorldSizeClass.Planet, 0f, WorldSeed, Generation);

        /// <summary>The corvette as a ship design message, or null when the content has no corvette (or no hull block).</summary>
        public static SpaceShipDesign? ShipDesign(GameContent content)
        {
            if (content == null || !content.Ships.TryGetValue(ShipType, out var ship))
            {
                return null;
            }

            var layout = content.GetShipLayout(ship.Layout);
            return layout == null ? null : DesignFromLayout(content, layout, "showcase:" + ShipType);
        }

        /// <summary>
        /// A layout's flyable hull as a design message — exactly the cells the server stamps for a ship of that layout
        /// (<c>GameServer.BuildShipStructureFrom</c>): every cell through <see cref="ShipCellBlocks.Map"/>, hatches and doors left
        /// open, the authored dye, glow and shape on plain block cells, then the solid floor across the enclosed footprint. The
        /// quarters' beds the server adds inside are not drawn (no one looks into the menu ship). The hatch is not named, so a
        /// viewer guesses it from the stern like an older server's ship (<see cref="SpaceShipDesign.HatchKnown"/> false).
        /// </summary>
        public static SpaceShipDesign? DesignFromLayout(GameContent content, ShipLayout layout, string id)
        {
            if (content == null || layout == null || layout.Cells.Count == 0)
            {
                return null;
            }

            var palette = ShipCellPalette.From(content);
            if (palette.Wall.IsAir)
            {
                return null;
            }

            var cells = new Dictionary<(int X, int Y, int Z), (ushort Block, int Tint, int Glow, int Shape)>();
            var order = new List<(int X, int Y, int Z)>();
            void Set((int X, int Y, int Z) p, (ushort Block, int Tint, int Glow, int Shape) value)
            {
                if (!cells.ContainsKey(p))
                {
                    order.Add(p);
                }

                cells[p] = value;
            }

            foreach (var cell in layout.Cells)
            {
                var mapped = ShipCellBlocks.Map(cell, palette, content);
                if (mapped.Role == ShipCellRole.Opening || mapped.Role == ShipCellRole.Door)
                {
                    continue; // an open hatch or a doorway: no block
                }

                var p = (cell.X, cell.Y, cell.Z);
                Set(p, mapped.CarriesModifiers
                    ? (mapped.Block.Value, mapped.Tint, mapped.Glow, mapped.Shape)
                    : (mapped.Block.Value, 0, 0, 0));
            }

            var footprint = ShipCellBlocks.FloorFootprint(layout);
            for (int fx = 0; fx < layout.Width; fx++)
            {
                for (int fz = 0; fz < layout.Length; fz++)
                {
                    var fp = (fx, 0, fz);
                    if (footprint.Contains((fx, fz)) && (!cells.TryGetValue(fp, out var at) || at.Block == 0))
                    {
                        Set(fp, (palette.Wall.Value, 0, 0, 0));
                    }
                }
            }

            return ToDesign(id, layout, cells, order);
        }

        private static SpaceShipDesign ToDesign(string id, ShipLayout layout,
            Dictionary<(int X, int Y, int Z), (ushort Block, int Tint, int Glow, int Shape)> cells, List<(int X, int Y, int Z)> order)
        {
            int n = 0;
            foreach (var p in order)
            {
                if (cells[p].Block != 0)
                {
                    n++;
                }
            }

            var xs = new int[n];
            var ys = new int[n];
            var zs = new int[n];
            var blocks = new ushort[n];
            var tints = new int[n];
            var glows = new int[n];
            var shapes = new int[n];
            bool anyMod = false;
            int i = 0;
            foreach (var p in order)
            {
                var c = cells[p];
                if (c.Block == 0)
                {
                    continue;
                }

                xs[i] = p.X;
                ys[i] = p.Y;
                zs[i] = p.Z;
                blocks[i] = c.Block;
                tints[i] = c.Tint;
                glows[i] = c.Glow;
                shapes[i] = c.Shape;
                anyMod |= c.Tint != 0 || c.Glow != 0 || c.Shape != 0;
                i++;
            }

            return new SpaceShipDesign
            {
                Id = id,
                Kind = "ship",
                Width = layout.Width,
                Height = layout.Height,
                Length = layout.Length,
                X = xs,
                Y = ys,
                Z = zs,
                Block = blocks,
                Tint = anyMod ? tints : System.Array.Empty<int>(),
                Glow = anyMod ? glows : System.Array.Empty<int>(),
                Shape = anyMod ? shapes : System.Array.Empty<int>(),
            };
        }
    }
}
