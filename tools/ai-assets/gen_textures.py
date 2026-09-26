# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Generate the block texture set (approved batch) via OpenAI images — seamless 64px pixel-art tiles.

One API call per texture; resumable (existing out/textures/<key>.png skipped) and tolerant of single
failures. After generating, run `bundle_textures.py --from-out` to write the chosen tiles into
client/Assets/Resources/textures as raw RGBA32 .bytes (the client decodes them with
Texture2D.LoadRawTextureData; LoadImage isn't available from the client asmdef). Logged in NOTICES.md.

Usage:
    uv run gen_textures.py
    uv run gen_textures.py --dry-run
"""
from __future__ import annotations

import argparse
import base64
import os
import sys
import time
from collections.abc import Callable
from io import BytesIO
from pathlib import Path
from typing import TYPE_CHECKING

from dotenv import load_dotenv

OUT = Path("out/textures")
STYLE = "seamless tileable pixel-art texture, top-down flat, retro 16-bit voxel game block tile, no text, no words"

TEXTURES = [
    ("stone", "rough grey stone rock surface with subtle cracks"),
    ("dirt", "brown soil dirt earth with small pebbles"),
    ("grass", "lush green grass top with blades"),
    ("sand", "fine yellow desert sand grains"),
    ("mud", "dark wet brown mud"),
    ("basalt", "dark grey volcanic basalt rock"),
    ("ice", "pale blue cracked ice"),
    ("iron_ore", "grey stone with rusty orange iron ore flecks"),
    ("copper_ore", "grey stone with bright orange copper ore veins"),
    ("titanium_ore", "grey stone with silvery white titanium ore flecks"),
    ("silicate", "pale sandy silicate mineral rock"),
    ("carbon", "black carbon coal rock with shiny flecks"),
    ("wood_log", "brown tree bark wood log surface with vertical wood grain"),
    ("tree_leaves", "dense leafy green tree foliage canopy with small overlapping leaves, top-down"),
    # #1783 generation 6 — the giant trees' own block pair (a scan names them as their own species).
    ("giant_log", "ancient giant tree bark, deeply furrowed dark reddish-brown wood with thick vertical ridges and moss in the grooves"),
    ("giant_leaves", "dense canopy of large broad dark green leaves of an ancient giant tree with pale veins, overlapping, top-down"),
    ("iron_wall", "grey sci-fi metal hull plate with rivets and panel seams"),
    ("crystal", "pale glowing blue crystal facets"),
    ("glass", "clear pale blue glass pane with a faint reflection"),
    ("glass_clear", "one single seamless sheet of perfectly clear colourless glass filling the whole image edge to edge, no frame, no grid, no bars, no window panes, uniform near-white with one very faint soft diagonal reflection streak"),
    ("lava", "dark cooled crust with glowing orange molten lava cracks"),
    ("water", "blue rippling water surface"),
    ("fire", "bright orange and yellow flames, licking tongues of fire with hot white centres, on a solid pure black background, top-down"),
    ("ash", "dark grey and black charred ash, soot and burnt embers, fine powdery texture"),
    # Item 21 — world variety: new surface + deep-crust blocks for the new world types.
    ("snow", "fresh clean white snow surface with a faint sparkle and soft powdery drifts"),
    ("salt", "white and pale grey crystalline salt flat crust with polygonal dried cracks"),
    ("mycelium", "purple-grey fungal mycelium soil threaded with pale filaments and tiny spores, top-down"),
    ("alien_grass", "alien turf of violet and magenta grass blades with a faint eerie glow, top-down"),
    ("deepslate", "very dark grey-blue deepslate stone with fine layered banding"),
    ("granite", "speckled pink and grey granite rock with glinting mineral flecks"),
    # Item 21 V3 — alien flora archetypes + giant-mushroom structure blocks.
    ("flora_tendril", "writhing alien tendril plant with violet finger-like fronds, top-down"),
    ("flora_bulb", "glowing bulbous alien pod plant with a luminous teal sac, top-down"),
    ("flora_gasbloom", "alien plant with translucent gas-filled bladders and pink veins, top-down"),
    ("flora_alienfern", "exotic alien fern with iridescent blue-violet feathery fronds, top-down"),
    ("flora_shardbloom", "flower of sharp glowing cyan crystal shards on a thin stem, top-down"),
    ("mushroom_stem", "pale fibrous giant mushroom stalk surface with vertical fibres"),
    ("mushroom_cap", "domed red-orange giant mushroom cap with white speckles, top-down"),
    ("flora_plant", "lush green leafy plant with fronds and small bush foliage, top-down"),
    ("flora_crystal", "cluster of glowing cyan crystal shards growing from rock"),
    # Complete flora set — biome-appropriate species (full colour; the atlas tints nothing).
    ("flora_fern", "lush green fern with feathery fronds, top-down"),
    ("flora_flower", "small wildflowers with pink and yellow blossoms on green stems, top-down"),
    ("flora_bush", "round leafy green bush with small red berries, top-down"),
    # #1774: the plantable sapling — a young tree, read as a small plant billboard like the other flora_* tiles.
    ("flora_sapling", "small young tree sapling with a thin brown stem and a few fresh bright green leaves, seen from the side, on a plain flat background"),
    ("flora_vine", "tangled green climbing vines with leaves, top-down"),
    ("flora_mushroom", "cluster of small mushrooms with red caps and white stems, top-down"),
    ("flora_cactus", "green desert cactus with spines and small flowers, top-down"),
    ("flora_dryshrub", "dry brittle brown desert shrub with bare twigs, top-down"),
    ("flora_reed", "tall thin green and teal marsh reeds and cattails, top-down"),
    ("flora_glowcap", "bioluminescent glowing cyan mushrooms in the dark, top-down"),
    ("flora_frostflower", "pale icy blue crystalline frost flower with frosty petals, top-down"),
    ("flora_emberbloom", "charred dark plant with glowing orange ember blossoms, top-down"),
    # Aquatic flora (the planet flora tint re-colours these on the client; the tile gives the pattern).
    ("flora_kelp", "tall ribbon-like green seaweed kelp blades swaying underwater, top-down"),
    ("flora_lily", "round flat green lily pads floating on water with a small white blossom, top-down"),
    # Task 6 — more flora variety.
    ("flora_palm", "small tropical palm plant with broad green fan fronds, top-down"),
    ("flora_moss", "soft green moss patch carpeting rock with tiny fronds, top-down"),
    ("flora_orchid", "exotic purple and pink orchid blossoms on slender green stems, top-down"),
    ("flora_succulent", "fleshy green rosette succulent plant like aloe and agave, top-down"),
    ("flora_pitcher", "carnivorous pitcher plant with red-veined tubular traps, top-down"),
    ("flora_puffball", "cluster of round pale puffball mushrooms on a swamp floor, top-down"),
    ("flora_lichen", "crusty pale grey-green lichen growth spreading on frosty rock, top-down"),
    ("flora_coral", "branching pink and orange coral reef growth, top-down"),
    ("flora_seagrass", "tufts of bright green underwater seagrass blades on the seabed, top-down"),
    ("flora_sporepod", "bulbous alien spore pods with a faint glowing teal sheen, top-down"),
    ("flora_thornbush", "spiky tangled thorn bush with dark twigs and small leaves, top-down"),
    ("flora_bellflower", "drooping clusters of blue and violet bell-shaped flowers, top-down"),
    ("flora_ashweed", "scorched grey-black volcanic weed with brittle ashen stalks, top-down"),
    ("flora_glowvine", "bioluminescent glowing green-cyan vine with luminous leaves in the dark, top-down"),
    # Flora variety V2 — fill the thin biomes (rock / ice / snow / salt / ash) + signature tall grass.
    ("flora_grasstuft", "tall waving lush green grass blades in a tuft, top-down"),
    ("flora_rockflower", "hardy small flower with pink and white blossoms growing from grey rock, top-down"),
    ("flora_snowbush", "low hardy shrub with dark green leaves dusted in white snow and frost, top-down"),
    ("flora_icereed", "tall brittle pale blue frozen ice reeds and crystalline stalks, top-down"),
    ("flora_saltgrass", "wiry sparse pale grey-green salt grass tufts on a white salt crust, top-down"),
    ("flora_cinderbush", "charred dark volcanic bush with smouldering glowing orange embers, top-down"),
    # Greenhouse farming (#627): the cultivated crop + its hydroponic tray. The crop is the ONE plant that
    # never takes a world's flora hue, so this tile is what the player sees on every world — it has to read
    # as ripe, edible fruit at a glance.
    ("flora_cropberry", "cultivated berry bush heavy with plump ripe red berries among glossy green leaves, a tended crop plant, top-down"),
    ("hydro_tray", "a shallow metal hydroponics tray filled with glowing teal-green nutrient gel, small sprouting seedlings and bubbles, top-down"),
    # Two more cultivated crops (#1204): like the berry bush these never take a world's flora hue, so each
    # tile must read as its own kind of food across a greenhouse aisle — golden ears vs. tan mushroom caps.
    ("flora_cropgrain", "cultivated grain crop, dense tall stalks of ripe golden wheat ears with slender green leaves, a tended cereal field plant, top-down"),
    ("flora_cropshroom", "cultivated edible mushrooms, a tight cluster of plump tan and cream button mushroom caps with thick pale stems on dark soil, top-down"),
    # Distinct tree crowns for the new conifer + palm archetypes.
    ("pine_needles", "dense dark green pine conifer needle foliage canopy, small overlapping needles, top-down"),
    ("palm_frond", "broad green tropical palm tree fronds, long radiating feathered leaves, top-down"),
    # Task 5 — new metal / rare-earth / raw-resource ores (grey host rock with the metal's veins).
    ("gold_ore", "grey stone with bright shiny yellow gold ore veins"),
    ("silver_ore", "grey stone with bright metallic silver-white ore veins"),
    ("aluminium_ore", "reddish-brown bauxite rock with dull silvery aluminium ore flecks"),
    ("tin_ore", "grey stone with dull pale silvery tin ore flecks"),
    ("nickel_ore", "grey stone with pale greenish-silver nickel ore veins"),
    ("cobalt_ore", "grey stone with deep blue cobalt ore veins"),
    ("lithium_ore", "pale grey rock with soft pinkish-white lithium ore streaks"),
    ("uranium_ore", "dark grey rock with faintly glowing yellow-green uranium ore flecks"),
    ("platinum_ore", "grey stone with bright silvery-white platinum ore flecks"),
    ("lead_ore", "dark grey stone with dull bluish-grey lead ore flecks"),
    ("zinc_ore", "grey stone with bluish-silver zinc ore crystals"),
    ("tungsten_ore", "dark grey rock with hard dark metallic tungsten ore flecks"),
    ("sulfur_ore", "grey rock with bright yellow sulfur crystal deposits"),
    ("neodymium_ore", "dark grey rock with purple-grey rare-earth neodymium ore veins"),
    # Task 5 — craftable building blocks from the new alloys.
    ("steel_wall", "brushed steel sci-fi wall plate with panel seams and bolts"),
    ("bronze_block", "polished bronze metal block with a warm golden-brown sheen"),
    ("brass_block", "polished brass metal block with a bright yellow-gold sheen"),
    # Task 5 Stage 3 — buildable world objects (placeable functional + decorative blocks).
    ("workbench", "a sci-fi metal workbench worktop with hand tools, a vice and scattered parts, top-down"),
    ("forge", "a stone and metal forge furnace with glowing orange molten metal inside, top-down"),
    ("steel_floor", "an industrial steel floor grating panel with diamond tread plate, top-down"),
    ("metal_panel", "a riveted dark metal wall panel with seams and bolts, top-down"),
    ("concrete", "a plain light grey concrete surface with a subtle rough texture, top-down"),
    ("crate", "a sci-fi metal storage crate container box with a hinged lid and corner latches, top-down"),
    ("door_hinge", "a closed metal hinged door panel set in a frame, with a round handle and visible hinges, front view"),
    ("door_slide", "a closed sci-fi sliding blast door with a centre seam and glowing status light, front view"),
    # Energy door — the walk-through air curtain that seals a base room (#793).
    ("door_energy", "a sci-fi doorway, a dark metal frame filled with a softly glowing translucent blue energy field with faint horizontal shimmer lines, front view"),
    ("asteroid_rock", "a rough grey pitted space asteroid rock surface, cratered stony texture with mineral flecks, seamless"),
    ("radio_beacon", "a sci-fi radio beacon transmitter tower, a slim metal pole on a base with a glowing cyan antenna ring and blinking status light, front view"),
    # #2002 the thumper (2026-09-26): its own tile at last — it shipped as a copy of the radio beacon's.
    ("thumper", "a sci-fi sand thumper machine, a squat dark metal piston rig with a heavy hammer head on a wide riveted base plate, orange warning stripes and a small amber status light, front view"),
    # #2058 Crystal Net (2026-09-27): the conduit, its sources, gates, sinks and machines — one tile each.
    ("crystal_conduit", "a sci-fi crystal conduit block, translucent violet crystal rod embedded in a dark metal frame with small silver clamps, faint inner glow, front view"),
    ("crystal_switch", "a sci-fi wall switch block, dark metal plate with a chunky violet crystal lever and a small green status light, front view"),
    ("crystal_button", "a sci-fi push button block, dark metal plate with a large round orange crystal button in the middle, front view"),
    ("step_plate", "a sci-fi floor pressure plate, brushed steel square plate with a violet crystal rim and four corner rivets, top-down"),
    ("proximity_sensor", "a sci-fi motion sensor block, dark metal housing with a glowing cyan eye lens and small antenna fins, front view"),
    ("daylight_sensor", "a sci-fi daylight sensor block, dark metal frame around a pale yellow crystal solar cell with a sun emblem, top-down"),
    ("storage_sensor", "a sci-fi storage sensor block, dark metal box with a small crate icon screen and three level lights, front view"),
    ("watcher", "a sci-fi watcher block, dark metal housing with one large forward-looking violet crystal eye and a thin eyelid ring, front view"),
    ("logic_block", "a sci-fi logic gate block, dark metal cube with a glowing blue circuit-trace pattern and an ampersand-like gate symbol, front view"),
    ("timer_block", "a sci-fi timer block, dark metal cube with a glowing amber clock face and tick marks, front view"),
    ("alarm_siren", "a sci-fi alarm siren block, dark metal base with a rotating red warning lamp dome and a small horn, front view"),
    ("chime", "a sci-fi doorbell chime block, warm brass bell in a dark metal frame with a violet crystal striker, front view"),
    ("horn", "a sci-fi signal horn block, large brass horn bell mounted on a dark metal box, front view"),
    ("melody_block", "a sci-fi melody block, dark metal cube with glowing pastel crystal keys like a tiny xylophone on its face, front view"),
    ("announcer", "a sci-fi announcer block, dark metal speaker box with a violet crystal grille and a small radio dish, front view"),
    ("fabricator", "a sci-fi automatic workbench block, dark metal machine with a small robotic arm over a work surface and a violet crystal status window, front view"),
    ("caller", "a sci-fi animal caller block, dark metal post with a brass whistle and a violet crystal tuning fork, front view"),
    ("clone_tank", "a sci-fi cloning tank block, dark metal frame around a glass cylinder of glowing teal fluid with rising bubbles, front view"),
    ("auto_drill_1", "a sci-fi auto-drill mk1 block, compact dark metal rig with a single steel drill bit pointing down and one violet crystal light, front view"),
    ("auto_drill_2", "a sci-fi auto-drill mk2 block, heavier dark metal rig with twin steel drill bits pointing down, carbide edges and two violet crystal lights, front view"),
    ("auto_drill_3", "a sci-fi auto-drill mk3 block, massive dark metal rig with a wide diamond-tipped drill head pointing down and three glowing violet crystal lights, front view"),
    ("matter_sender", "a sci-fi matter sender block, dark metal pedestal with an upward violet crystal emitter ring and cyan energy motes rising, front view"),
    ("matter_receiver", "a sci-fi matter receiver block, dark metal pedestal with a downward cyan crystal collector ring and violet energy motes settling, front view"),
    ("base_core", "a sci-fi base foundation cornerstone block, a carved grey stone slab with a glowing teal-cyan claim emblem and faint engraved energy lines, top-down"),
    ("beam_block", "a sci-fi teleporter pad, a dark metal floor plate with a glowing cyan hexagonal grid, concentric light rings and small status lights around the rim, top-down"),
    # Materialvielfalt — dead-end fixes + new tiers + metal storage blocks.
    ("detoxifier", "a sci-fi chemical detox station, a metal vat of bubbling glowing green fluid with pipes and valves, top-down"),
    # Algae tank — base food machine (grows rations from water).
    ("algae_tank", "a sci-fi algae farm tank, a dark metal frame around a glass vat of glowing green algae water with rising bubbles and small feed pipes, top-down"),
    # Heal tank — base/station regen unit (heals, feeds, recharges nearby players; holds the home spawn).
    ("heal_tank", "a sci-fi regeneration tank, a dark metal frame around a glass vat of glowing teal-cyan fluid with rising bubbles, soft white status lights and small pipes, front view"),
    # Energy fence + gate — creature pen (fauna blocked server-side; gate membrane is walk-through).
    ("energy_fence", "a sci-fi energy fence pylon, a dark metal frame with vertical glowing cyan energy beams in a lattice and small warning lights, front view"),
    ("energy_gate", "a sci-fi energy gate, a dark metal archway frame filled with a soft translucent teal energy membrane with gentle ripples, front view"),
    ("diamond_ore", "dark grey rock with glittering pale-blue and white embedded diamond crystals"),
    ("insulated_wall", "a matte dark polymer-coated wall panel with rubber seams and rounded bolts, top-down"),
    ("iron_block", "a solid polished grey iron metal block, seamless"),
    ("copper_block", "a solid polished orange-brown copper metal block, seamless"),
    ("gold_block", "a solid polished bright yellow gold metal block, seamless"),
    ("silver_block", "a solid polished metallic silver-white metal block, seamless"),
    ("aluminium_block", "a solid brushed pale silver aluminium metal block, seamless"),
    ("tin_block", "a solid dull pale silvery tin metal block, seamless"),
    ("nickel_block", "a solid polished pale greenish-silver nickel metal block, seamless"),
    ("cobalt_block", "a solid polished deep blue cobalt metal block, seamless"),
    ("platinum_block", "a solid polished bright silvery-white platinum metal block, seamless"),
    ("lead_block", "a solid dull bluish-grey lead metal block, seamless"),
    ("zinc_block", "a solid bluish-silver zinc metal block, seamless"),
    ("tungsten_block", "a solid hard dark metallic grey tungsten metal block, seamless"),
    ("titanium_block", "a solid brushed silvery-grey titanium metal block, seamless"),
    # Monuments — the masonry of a vanished civilisation (arcade arches, gates, stone circles).
    ("ancient_brick", "weathered pale sandy-grey ancient stone masonry blocks with mortar joints, chipped and lichen-stained, seamless"),
    ("rune_stone", "a carved grey slate stone slab with deep chiselled glowing angular alien runes, mysterious inscriptions, seamless"),
    # Low-tech furniture & survival batch (#803-#809).
    ("bed", "a simple rustic cot bed seen from above, a wooden log frame with a woven pale-green blanket and a small white pillow, top-down"),
    ("campfire", "a small stone-ringed campfire seen from above, charred crossed logs with bright glowing orange embers and small flames, top-down"),
    ("wood_crate", "a rustic wooden storage box of rough-hewn planks with a simple lid seam and wooden pegs, front view"),
    ("lantern", "a warm rustic lantern, a small wooden frame with glowing amber glass panes and a little flame inside, front view"),
    ("rug", "a hand-woven flat fibre rug with a simple geometric border pattern in muted warm tones, top-down"),
    ("flower_pot", "a small round terracotta clay flower pot filled with dark soil, front view"),
    # Missing block-item images (#868): these placeable blocks only had the procedural flat-colour
    # tile, so their inventory icons (block-tile fallback) and in-world faces looked blank.
    ("ladder", "a sturdy metal ladder with two vertical side rails and evenly spaced horizontal rungs, mounted on a dark metal wall panel, front view"),
    ("stairs", "an industrial metal staircase of layered step treads with grip ridges, front view"),
    ("station_core", "a sci-fi space station core block, a dark armoured metal cube with a glowing teal reactor heart window and engraved energy conduits, front view"),
    ("station_vendor", "a sci-fi trading post kiosk counter, a dark metal booth with a warm amber-lit display shelf of goods and a small price screen, front view"),
    ("mission_board", "a sci-fi mission notice board, a dark metal panel covered in small glowing blue holographic job postings and status lights, front view"),
    ("station_container", "a sci-fi station storage container, an orange ribbed metal cargo locker with a hinged front hatch and a small status light, front view"),
    # 2026-09 NPC professions: the post blocks a player builds at home / on a station, plus the doctor's stretcher.
    ("clinic_post", "a sci-fi clinic reception counter, a white and mint-green medical cabinet with a glowing green cross sign, small medicine drawers and a vital-signs screen, front view"),
    ("shop_counter", "a cosy shop counter, a wooden sales counter with baskets of fresh fruit, bread and jars on shelves behind it and a small hanging chalkboard, front view"),
    ("arms_rack", "a sci-fi weapon rack, a dark gunmetal wall rack holding futuristic blaster pistols and energy cells on hooks behind a steel grille, small red status lights, front view"),
    # #1938: the sage's post reads as a COMPUTER now. A lectern drawn on a background looked like a picture
    # glued onto a cube from every side ("einfach nur eine Textur auf Blöcken"); a terminal cabinet is a box.
    ("sage_lectern", "a sage's data terminal, a dark violet armoured console cabinet filling the whole tile edge to edge with no background, a glowing amber screen of scrolling data runes in the middle, engraved circuit lines, small crystal indicator lights, front view"),
    ("tamer_post", "an animal tamer's post, a wooden stable gate with leather leashes, a coiled whip, bags of animal feed and a small paw print sign, front view"),
    # #1939: these three were scenes with floors and furniture legs — redrawn as the block itself.
    ("quarry_post", "a quarry supply cabinet, a rough timber crate with iron corner bands filling the whole tile edge to edge with no background and no floor, a hanging pickaxe and chisel on its front, stone dust and small ore chunks, front view"),
    ("streamer_post", "a streaming console cabinet, a dark purple metal housing filling the whole tile edge to edge with no background and no legs, a glowing magenta ring light, a small camera lens and a screen with live chat lines, front view"),
    ("press_desk", "a newsroom press terminal cabinet, a blue-grey metal housing filling the whole tile edge to edge with no background and no legs, a glowing blue screen with headline bars, a paper tray with a printed sheet and a small desk microphone, front view"),
    # #1942: the starter cabin's wall bunk — the one-cell sleeping place a cramped ship has room for.
    ("crew_bunk", "a spaceship wall sleeping bunk, a white metal capsule frame filling the whole tile edge to edge with no background, a padded grey-blue mattress with a folded blanket and a small pillow inside it, a soft cyan reading light strip along the top edge, front view"),
    ("stretcher", "white medical stretcher canvas fabric filling the whole tile edge to edge with no background, a faint green cross in the middle and grey stitched seams, top-down"),
    # 2026-09 Titas: the yellow sulfur stone under the snow.
    ("sulfur_stone", "bright yellow sulfur stone rock, crystalline sulfur crust with pale yellow and ochre patches and small dark pores"),
    # Factory look (#1050): the machine housing, its pipe stack and the production terminal had no tile.
    ("machine_block", "a heavy sci-fi industrial machine housing, dark grey armoured metal casing with rivets, bolted seams, ventilation slits and a small amber indicator light, front view"),
    ("factory_pipe", "an industrial factory pipe duct, a thick riveted olive-grey metal pipe with flanged joints and a pressure valve, front view"),
    ("factory_terminal", "a sci-fi factory production terminal, a dark metal console with a glowing cyan holographic screen showing production graphs, buttons and a status light, front view"),
    # #1714: carries base power out to a sentry post beyond the base zone. Reads as a relay, not a generator —
    # heavy insulators and a conduit running through, so a chain of them looks like a power line.
    ("power_relay", "a sci-fi power relay pylon, a dark grey metal housing with two ceramic insulator rings, a thick cable conduit running through it and a bright glowing cyan energy core between the rings, small status lights, front view"),
    # #1726: the waterfall spout — a machine block that pours a column of water straight down and never sideways.
    # Reads as plumbing, not as water: a riveted housing with a wide nozzle underneath, so the player can see
    # which way it pours before placing it over an edge.
    ("water_spout", "a sci-fi water spout block, a dark blue-grey riveted metal housing with a wide round nozzle opening at the bottom edge, a short pipe running down into the nozzle, clear blue-white water pouring out of the nozzle underneath, a small blue status light, front view"),
    # Guardian machines (#1338): the plating tile WorldEntities/SpaceView load for the robot, scan-drone,
    # space drone, UFO and cruiser hulls — grey circuit-board armour, no lights (the red eyes are separate).
    # Post-processed by `guardian_plating` (see POST_PROCESS) before the tile is written, since the entity
    # loaders do NOT brighten tiles like CreatureBuilder does: desaturated 70 % and lifted
    # v' = 1 - (1 - v) * 0.76 so the plates sit at ~0.40 mean grey.
    ("enemy_robot", "dark grey armoured sci-fi robot plating with bolted panel seams and etched light-grey circuit-board traces and solder pads, matte metal, coarse large panels, no lights, no glow, no colour"),
    # Landscape variety 4/6 (#1647): the five terrain blocks of the generation-1 paints, fluids and props.
    ("moss_stone", "grey cobbled stone rock surface thickly overgrown with soft green moss patches in the cracks, damp, top-down"),
    ("tar", "glossy black tar pitch surface with a few slow dull bubbles and faint oily sheen, very dark, top-down"),
    ("bone", "bleached pale ivory bone surface, a dense mass of old dry bones and skull fragments, slightly yellowed, top-down"),
    ("sandstone", "warm ochre sandstone rock with fine horizontal sediment banding in tan, rust and cream, top-down"),
    ("scree", "loose grey and brown angular rock fragments and gravel, a talus slope of broken stone, top-down"),
    # Cave flora (terrain generation 11). Wild flora is re-coloured by the world's hue in the shader, so these carry the
    # light/dark pattern: moss + threads are cutout billboards (dark background → bake_leaf_alpha punches it out), the
    # threads hang from the TOP edge (a hanging billboard roots in the tile's top row), the prism bloom is near-white
    # so each plant's own rainbow colour comes through at full strength.
    ("flora_cavecap", "a cluster of dark cave mushrooms with broad dusky grey-violet caps speckled with small pale spots, damp cave floor, top-down"),
    ("flora_glowmoss", "side view of one low wide mound of softly glowing pale green-white cave moss tufts with tiny luminous tips, growing along the bottom edge of the image, the rest of the image solid pitch-black empty background"),
    ("flora_glowthread", "many thin softly glowing pale silky threads hanging straight down from the top edge of the image, each ending in a small bright luminous bead, on a pure black background, side view"),
    ("flora_prismbloom", "densely packed luminous pale white flower petals filling the whole image edge to edge, overlapping soft shining petals with small bright white centres, no background visible, top-down"),
    # Fruit trees (#2038, generation 14): one pale fruit hanging from a short dark stem on a black ground, side view like the
    # glow threads — the dye tint colours the body per tree kind and world, the alpha bake cuts the black away.
    ("flora_fruit_round", "a single plump round apple-like fruit in pale ivory white with soft grey shading, hanging from a short dark brown stem at the top edge of the image, one small dark leaf at the stem, on a pure black background, side view"),
    ("flora_fruit_long", "a single long slender smooth pod-like fruit in pale ivory white with soft grey shading, hanging straight down from a short dark brown stem at the top edge of the image, on a pure black background, side view"),
    ("flora_fruit_grape", "a single hanging cluster of many small round pale ivory white grapes with soft grey shading, hanging from a short dark brown stem at the top edge of the image, the entire rest of the image solid pitch-black #000000 with no gradient and no vignette, side view"),
    ("flora_fruit_banana", "a single curved banana-shaped fruit in pale ivory white with soft grey shading, hanging from a short dark brown stem at the top edge of the image, on a pure black background, side view"),
]

if TYPE_CHECKING:
    from PIL import Image


def guardian_plating(img: Image.Image) -> Image.Image:
    """The Guardian plating post-process (#1338, made reproducible in #1369).

    Desaturates by 70 % (blend towards the luminance with weight 0.7) and lifts every RGB channel
    towards white with v' = 1 - (1 - v) * 0.76, alpha untouched. Applied to the 64 px tile right after
    the resize, so out/textures/enemy_robot.png — and therefore `bundle_textures.py --from-out` — carries
    the processed plates that the committed .bytes tile shows.
    """
    from PIL import ImageEnhance

    rgba = img.convert("RGBA")
    rgb = ImageEnhance.Color(rgba.convert("RGB")).enhance(1.0 - 0.70)
    lifted = rgb.point(lambda v: round(255 - (255 - v) * 0.76))
    lifted.putalpha(rgba.getchannel("A"))
    return lifted


# Per-tile post-processing applied to the generated 64 px tile before it is saved (key -> function).
# Anything not listed here is written as generated.
POST_PROCESS: dict[str, Callable[[Image.Image], Image.Image]] = {
    "enemy_robot": guardian_plating,
}


def main() -> None:
    ap = argparse.ArgumentParser(description="Generate the block texture set (approved batch).")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--only", default=None,
                    help="Generate only these comma-separated keys (e.g. a single approval test tile).")
    args = ap.parse_args()

    textures = TEXTURES
    if args.only:
        wanted = {k.strip() for k in args.only.split(",")}
        textures = [t for t in TEXTURES if t[0] in wanted]

    print(f"[tex] {len(textures)} textures")
    if args.dry_run:
        for i, (key, desc) in enumerate(textures, 1):
            post = f"  [post: {POST_PROCESS[key].__name__}]" if key in POST_PROCESS else ""
            print(f"  {i:2d}. {key:16s} {desc}{post}")
        return

    load_dotenv()
    key = os.environ.get("OPENAI_API_KEY")
    if not key:
        sys.exit("OPENAI_API_KEY is not set.")

    from openai import OpenAI
    from PIL import Image

    client = OpenAI(api_key=key)
    OUT.mkdir(parents=True, exist_ok=True)

    done = skipped = failed = 0
    fails: list[str] = []
    total = len(textures)

    for i, (block, desc) in enumerate(textures, 1):
        out = OUT / f"{block}.png"
        if out.exists() and out.stat().st_size > 0:
            skipped += 1
            print(f"[{i}/{total}] {block}: skip (exists)")
            continue

        prompt = f"{STYLE}, of {desc}"
        ok = False
        for attempt in (1, 2):
            try:
                resp = client.images.generate(
                    model="gpt-image-1-mini", prompt=prompt, size="1024x1024", quality="low", n=1)
                raw = base64.b64decode(resp.data[0].b64_json)
                img = Image.open(BytesIO(raw)).convert("RGBA").resize((64, 64), Image.NEAREST)
                if block in POST_PROCESS:
                    img = POST_PROCESS[block](img)
                img.save(out)
                done += 1
                ok = True
                print(f"[{i}/{total}] {block}: ok ({out.stat().st_size} bytes)")
                break
            except Exception as exc:  # noqa: BLE001
                print(f"[{i}/{total}] {block}: attempt {attempt} failed: {exc}")
                time.sleep(2)

        if not ok:
            failed += 1
            fails.append(block)

    print(f"\n[tex] done. generated={done} skipped={skipped} failed={failed} of {total}")
    if fails:
        print("[tex] failed: " + ", ".join(fails))


if __name__ == "__main__":
    main()
