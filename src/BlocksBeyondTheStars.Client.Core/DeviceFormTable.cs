// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    // Device forms: the first set of forms. Coordinates are eighths of the cell in the form's frame — x to the
    // right and z into the depth for someone standing in front, the front being the z = 0 side. Every part lies on the
    // 8-grid, so tiles drawn on the micro raster (1 micro cell = 8 px: deck buttons, vent slats, plate joints) come out whole
    // when sliced. The rules the forms follow:
    //   - a device's own PICTURE is shown only on faces that lie in, or close to, the cube face it was drawn for, at the place
    //     where they are (Own, sliced) — a part further in wears a material instead;
    //   - pictures of an OBJECT on a background (radio beacon, power relay, thumper, heal tank, rail stop) are no material:
    //     those forms are built from material tiles;
    //   - the marker blocks of the ship stations (workbench, heal_tank, data_cache, ship_helm) keep their whole top face at
    //     full height, because the station's decor stands on it (StationDecorView);
    //   - some part reaches full height wherever it can: a player standing on a device stands on the cube's top.
    public static partial class DeviceForms
    {
        /// <summary>The device tiles made for the forms (Resources/textures/&lt;key&gt;.bytes). A missing one falls back to
        /// the block's own tile at run time.</summary>
        public static readonly IReadOnlyList<string> NewTiles = new[]
        {
            MetalDark, Deck, Screen, Embers, LiquidTeal, LiquidGreen, Vent, LampAmber,
        };

        public const string MetalDark = "devform_metal_dark";
        public const string Deck = "devform_deck";
        public const string Screen = "devform_screen";
        public const string Embers = "devform_embers";
        public const string LiquidTeal = "devform_liquid_teal";
        public const string LiquidGreen = "devform_liquid_green";
        public const string Vent = "devform_vent";
        public const string LampAmber = "devform_lamp_amber";

        private static DeviceFormBox B(int x0, int y0, int z0, int x1, int y1, int z1, int slot)
            => new DeviceFormBox(x0, y0, z0, x1, y1, z1, slot);

        private static DeviceFormTile Sl(string key) => DeviceFormTile.Slice(key);

        private static DeviceFormSlot Mat(string key, bool glow = false) => DeviceFormSlot.All(Sl(key), glow);

        private static Dictionary<string, DeviceForm> BuildTable()
        {
            var t = new Dictionary<string, DeviceForm>(StringComparer.Ordinal);
            void Add(DeviceForm f) => t[f.Key] = f;

            Add(Workbench());
            Add(Forge("forge", lit: true));
            Add(Forge("forge_off", lit: false));
            Add(Tank("heal_tank", Sl(LiquidTeal), glow: true));
            Add(Tank("clone_tank", Sl(LiquidTeal), glow: true));
            Add(Tank("capsule_tank", Sl(LiquidTeal), glow: true));
            Add(Tank("capsule_tank_open", Sl(MetalDark), glow: false)); // the open capsule: a dark hollow
            Add(Tank("algae_tank", Sl(LiquidGreen), glow: true));
            Add(Tank("power_relay", Sl(LiquidTeal), glow: true));
            foreach (string key in new[] { "sps_console", "factory_terminal", "bio_lab", "press_desk" })
            {
                Add(ControlDesk(key));
            }

            Add(HydroTray());
            Add(Stretcher());
            Add(SageLectern());
            Add(RadioBeacon());
            Add(RailStop());
            Add(Thumper());
            foreach (string key in new[] { "fabricator", "detoxifier", "decontaminator", "machine_block" })
            {
                Add(Housing(key));
            }

            Add(Crate("crate", MetalDark));
            Add(Crate("transport_crate", MetalDark));
            Add(Crate("wood_crate", "wood_planks"));
            Add(Pedestal("data_cache"));
            Add(Pedestal("ship_helm"));
            return t;
        }

        /// <summary>Worktop at full height (the workshop station's marker) over a drawer block whose front shows the drawer
        /// row of <c>face_workbench_side</c> whole, four dark legs and a low shelf.</summary>
        private static DeviceForm Workbench()
        {
            var drawers = new DeviceFormSlot(Sl("metal_panel"), Sl("metal_panel"), Sl("metal_panel"),
                DeviceFormTile.Fit("face_workbench_side", 0.06f, 0.12f, 0.94f, 0.42f));
            return new DeviceForm("workbench", new[]
            {
                B(0, 7, 0, 8, 8, 8, 1),                                  // worktop: tools on top, the side tile's rim on its edges
                B(0, 4, 1, 8, 7, 8, 2),                                  // drawer block, set back under the worktop's front edge
                B(0, 0, 1, 1, 4, 2, 3), B(7, 0, 1, 8, 4, 2, 3),          // front legs
                B(0, 0, 7, 1, 4, 8, 3), B(7, 0, 7, 8, 4, 8, 3),          // back legs
                B(1, 1, 1, 7, 2, 8, 4),                                  // low shelf between the legs
            }, new[] { DeviceFormSlot.Own(), drawers, Mat(MetalDark), Mat("metal_panel") });
        }

        /// <summary>Brick body with the fire mouth where the picture draws its arch (x 3..5, y 2..5, two cells deep): the
        /// niche's back wall shows the picture there, its walls and ceiling the dark bricks (the cube's own side and bottom
        /// tiles), its floor glowing coals; a narrower chimney on top reaches full height.</summary>
        private static DeviceForm Forge(string key, bool lit)
        {
            var bed = lit ? Mat(Embers, glow: true) : Mat("face_forge_side");
            var chimney = DeviceFormSlot.OwnTop(Sl("face_forge_side"));
            return new DeviceForm(key, new[]
            {
                B(0, 0, 0, 8, 1, 8, 1),     // base
                B(3, 1, 0, 5, 2, 2, 2),     // the bed of coals: the niche's floor
                B(0, 1, 0, 3, 5, 8, 1),     // left of the mouth
                B(5, 1, 0, 8, 5, 8, 1),     // right of the mouth
                B(3, 1, 2, 5, 5, 8, 1),     // behind the mouth: its back wall shows the fire arch of the picture
                B(0, 5, 0, 8, 6, 8, 1),     // over the mouth
                B(2, 6, 2, 6, 8, 7, 3),     // chimney
            }, new[] { DeviceFormSlot.Own(), bed, chimney });
        }

        /// <summary>Base plate and cap in dark metal (the cap's top keeps the block's top tile, full and at full height — the
        /// medbay's marker), a narrower column of liquid between them and four thin corner posts that keep the outline.</summary>
        private static DeviceForm Tank(string key, DeviceFormTile liquid, bool glow)
        {
            return new DeviceForm(key, new[]
            {
                B(0, 0, 0, 8, 1, 8, 2),     // base plate
                B(0, 7, 0, 8, 8, 8, 1),     // cap
                B(2, 1, 2, 6, 7, 6, 3),     // the column
                B(0, 1, 0, 1, 7, 1, 2), B(7, 1, 0, 8, 7, 1, 2), B(0, 1, 7, 1, 7, 8, 2), B(7, 1, 7, 8, 7, 8, 2),
            }, new[] { DeviceFormSlot.OwnTop(Sl(MetalDark)), Mat(MetalDark), DeviceFormSlot.All(liquid, glow) });
        }

        /// <summary>Desk body (its front shows the lower part of the picture where it is), a control deck in front of an
        /// upright screen housing at the back that reaches full height and shows the picture's screen where it is.</summary>
        private static DeviceForm ControlDesk(string key)
        {
            var deck = new DeviceFormSlot(Sl(Deck), DeviceFormTile.Own, DeviceFormTile.Own, DeviceFormTile.Own, glow: true);
            return new DeviceForm(key, new[]
            {
                B(0, 0, 1, 8, 1, 8, 3),     // toe plinth, set back
                B(0, 1, 0, 8, 4, 8, 1),     // desk body
                B(0, 4, 0, 8, 5, 4, 2),     // control deck
                B(0, 4, 4, 8, 8, 8, 1),     // screen housing
            }, new[] { DeviceFormSlot.Own(), deck, Mat(MetalDark) });
        }

        /// <summary>Tray body on a dark plinth with a raised rim; the plant bed (the own top tile) lies one cell below it.</summary>
        private static DeviceForm HydroTray()
        {
            return new DeviceForm("hydro_tray", new[]
            {
                B(1, 0, 1, 7, 1, 7, 2),     // plinth
                B(0, 1, 0, 8, 7, 8, 1),     // tray body; its top is the plant bed
                B(0, 7, 0, 8, 8, 1, 1), B(0, 7, 7, 8, 8, 8, 1),   // rim front and back
                B(0, 7, 1, 1, 8, 7, 1), B(7, 7, 1, 8, 8, 7, 1),   // rim left and right
            }, new[] { DeviceFormSlot.Own(), Mat(MetalDark) });
        }

        /// <summary>Mattress slab (the own tile on top, the steel side tile around) on four legs.</summary>
        private static DeviceForm Stretcher()
        {
            return new DeviceForm("stretcher", new[]
            {
                B(0, 5, 0, 8, 7, 8, 1),
                B(0, 0, 0, 1, 5, 1, 2), B(7, 0, 0, 8, 5, 1, 2), B(0, 0, 7, 1, 5, 8, 2), B(7, 0, 7, 8, 5, 8, 2),
            }, new[] { DeviceFormSlot.Own(), Mat(MetalDark) });
        }

        /// <summary>Foot, post and a stepped reading desk; the desk's front lies just behind the cube front and shows the
        /// picture there, the post wears the purple circuit side tile.</summary>
        private static DeviceForm SageLectern()
        {
            return new DeviceForm("sage_lectern", new[]
            {
                B(1, 0, 1, 7, 1, 7, 1),     // foot
                B(3, 1, 3, 5, 5, 5, 2),     // post
                B(0, 5, 1, 8, 7, 8, 1),     // reading desk
                B(0, 7, 4, 8, 8, 8, 1),     // back step
            }, new[] { DeviceFormSlot.Own(), Mat("face_sage_lectern_side") });
        }

        /// <summary>Metal base, a slim mast, a dark collar and a glowing head whose top shows the lens of the beacon's top
        /// picture whole. The picture itself (an object on a background) is used nowhere else.</summary>
        private static DeviceForm RadioBeacon()
        {
            var head = new DeviceFormSlot(DeviceFormTile.Fit(null, 0.2f, 0.2f, 0.8f, 0.8f), Sl(MetalDark), Sl(LiquidTeal),
                Sl(LiquidTeal), glow: true);
            return new DeviceForm("radio_beacon", new[]
            {
                B(1, 0, 1, 7, 2, 7, 1),     // base
                B(3, 2, 3, 5, 5, 5, 2),     // mast
                B(2, 5, 2, 6, 6, 6, 3),     // collar
                B(2, 6, 2, 6, 8, 6, 4),     // head
            }, new[] { Mat("metal_panel"), Mat("steel_wall"), Mat(MetalDark), head });
        }

        /// <summary>Base, post, a small glowing timetable screen on the post's front (the screen tile whole, in its 2:1 middle
        /// band) and an amber lamp head at full height.</summary>
        private static DeviceForm RailStop()
        {
            return new DeviceForm("rail_stop", new[]
            {
                B(1, 0, 1, 7, 1, 7, 1),     // base
                B(3, 1, 3, 5, 6, 5, 2),     // post
                B(2, 3, 2, 6, 5, 3, 3),     // timetable screen, on the post's front
                B(2, 6, 2, 6, 8, 6, 4),     // lamp head
            }, new[]
            {
                Mat("metal_panel"), Mat("steel_wall"),
                DeviceFormSlot.All(DeviceFormTile.Fit(Screen, 0f, 0.25f, 1f, 0.75f), glow: true),
                Mat(LampAmber, glow: true),
            });
        }

        /// <summary>Base plate with the hazard border, a narrower body, a wide hammer head at full height (the cube's top on
        /// top) and two guide columns.</summary>
        private static DeviceForm Thumper()
        {
            return new DeviceForm("thumper", new[]
            {
                B(0, 0, 0, 8, 1, 8, 1),     // base plate
                B(2, 1, 2, 6, 5, 6, 2),     // body
                B(1, 5, 1, 7, 8, 7, 3),     // hammer head
                B(0, 1, 3, 1, 8, 5, 4), B(7, 1, 3, 8, 8, 5, 4),   // guide columns
            }, new[]
            {
                new DeviceFormSlot(Sl("face_industrial_top"), Sl(MetalDark), Sl(MetalDark), Sl(MetalDark)),
                Mat("metal_panel"), DeviceFormSlot.OwnTop(Sl("iron_wall")), Mat(MetalDark),
            });
        }

        /// <summary>Four feet, a louvre band, the body (front picture where it is) and an inset top plate at full height.</summary>
        private static DeviceForm Housing(string key)
        {
            return new DeviceForm(key, new[]
            {
                B(0, 0, 0, 2, 1, 2, 2), B(6, 0, 0, 8, 1, 2, 2), B(0, 0, 6, 2, 1, 8, 2), B(6, 0, 6, 8, 1, 8, 2),
                B(0, 1, 0, 8, 2, 8, 3),     // louvre band
                B(0, 2, 0, 8, 7, 8, 1),     // body
                B(1, 7, 1, 7, 8, 7, 1),     // top plate
            }, new[] { DeviceFormSlot.Own(), Mat(MetalDark), Mat(Vent) });
        }

        /// <summary>The full crate body (players stack crates) on two skids.</summary>
        private static DeviceForm Crate(string key, string skid)
        {
            return new DeviceForm(key, new[]
            {
                B(0, 1, 0, 8, 8, 8, 1),
                B(0, 0, 1, 8, 1, 2, 2), B(0, 0, 6, 8, 1, 7, 2),
            }, new[] { DeviceFormSlot.Own(), Mat(skid) });
        }

        /// <summary>The cockpit / console marker (a code-painted tile): foot, a slightly narrower column with a lit band and a
        /// full top plate at full height for the station's decor.</summary>
        private static DeviceForm Pedestal(string key)
        {
            return new DeviceForm(key, new[]
            {
                B(0, 0, 0, 8, 1, 8, 1),     // foot
                B(1, 1, 1, 7, 3, 7, 1),     // column, lower
                B(1, 3, 1, 7, 4, 7, 2),     // lit band
                B(1, 4, 1, 7, 7, 7, 1),     // column, upper
                B(0, 7, 0, 8, 8, 8, 1),     // top plate
            }, new[] { DeviceFormSlot.Own(), DeviceFormSlot.All(Sl(Screen), glow: true, minEmission: 0.6f) });
        }
    }
}
