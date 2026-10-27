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

# #2773: the ground of a flora billboard tile drawn as ONE plant in side view. Every non-solid flora_* block renders as
# a cutout cross billboard, so its tile must be the plant alone: the black is cut away per pixel by bake_leaf_alpha.py
# (DARK_GROUND). A top-down "field of plants on a ground" tile lost its plants to the darkness cut instead.
ON_BLACK = "the entire rest of the image solid pitch-black #000000 with no gradient, no vignette and no ground"

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
    # #2977–#2980: the sapling kinds — one young tree per kind, a side-view plant on black rooted on the bottom edge
    # (cut by the dark-ground bake like the meadow plants, not chroma-keyed like the classic sapling).
    ("flora_sapling_conifer", f"side view of one small young fir tree sapling, a thin brown stem with a few tiers of short dark green needle branches forming a little cone, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}"),
    ("flora_sapling_fruit_round", f"side view of one small young fruit tree sapling, a thin brown stem with a few fresh bright green leaves and two tiny round red apples hanging from it, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}"),
    ("flora_sapling_fruit_long", f"side view of one small young fruit tree sapling, a thin brown stem with a few fresh bright green leaves and two small long slender yellow-green pods hanging from it, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}"),
    ("flora_sapling_fruit_grape", f"side view of one small young fruit tree sapling, a thin brown stem with a few fresh bright green leaves and one small cluster of round purple grapes hanging from it, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}"),
    ("flora_sapling_fruit_banana", f"side view of one small young banana palm sapling, a short brown stem with a few broad arching bright green fronds and one tiny bunch of yellow bananas, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}"),
    ("flora_sapling_fifi", f"side view of one small young sapling with a thin bright green stem, a little round crown of lemon-yellow leaves and one small glowing hot-pink blossom on top, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}"),
    ("flora_sapling_mahogany", f"side view of one small young mahogany tree sapling, a sturdy dark reddish-brown stem with a few large broad dark green leaves with pale veins, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}"),
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
    ("flora_lily", f"side view of one water lily plant, several round flat green lily pads on short stems spreading sideways along the bottom of the image and one white lily blossom with a yellow centre rising in the middle, {ON_BLACK}"),
    # Task 6 — more flora variety.
    ("flora_palm", f"side view of one small tropical palm plant, broad bright green fan fronds arching outwards from a short stem, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}"),
    ("flora_moss", "soft green moss patch carpeting rock with tiny fronds, top-down"),
    ("flora_orchid", f"side view of one exotic orchid plant, purple and pink blossoms on slender arching green stems above a few broad green leaves, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}"),
    ("flora_succulent", "fleshy green rosette succulent plant like aloe and agave, top-down"),
    ("flora_pitcher", "carnivorous pitcher plant with red-veined tubular traps, top-down"),
    ("flora_puffball", "cluster of round pale puffball mushrooms on a swamp floor, top-down"),
    ("flora_lichen", "crusty pale grey-green lichen growth spreading on frosty rock, top-down"),
    ("flora_coral", f"side view of one branching pink and orange coral growth with many rounded branch tips, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}"),
    ("flora_seagrass", f"side view of one tuft of long bright green seagrass blades swaying upwards, rooted at the bottom edge of the image and reaching almost to its top, {ON_BLACK}"),
    ("flora_sporepod", "bulbous alien spore pods with a faint glowing teal sheen, top-down"),
    ("flora_thornbush", "spiky tangled thorn bush with dark twigs and small leaves, top-down"),
    ("flora_bellflower", "drooping clusters of blue and violet bell-shaped flowers, top-down"),
    ("flora_ashweed", "scorched grey-black volcanic weed with brittle ashen stalks, top-down"),
    ("flora_glowvine", "bioluminescent glowing green-cyan vine with luminous leaves in the dark, top-down"),
    # Flora variety V2 — fill the thin biomes (rock / ice / snow / salt / ash) + signature tall grass.
    ("flora_grasstuft", "tall waving lush green grass blades in a tuft, top-down"),
    ("flora_rockflower", f"side view of one small hardy flowering plant, pink and white blossoms on short green stems growing out of a small grey rock that sits on the bottom edge of the image, {ON_BLACK}"),
    ("flora_snowbush", f"side view of one low round hardy shrub with green leaves dusted in white snow and frost, rooted at the bottom edge of the image, filling its lower two thirds and most of its width, {ON_BLACK}"),
    ("flora_icereed", "tall brittle pale blue frozen ice reeds and crystalline stalks, top-down"),
    ("flora_saltgrass", f"side view of one wiry tuft of tall thin pale grey-green salt grass blades with a few straw-coloured tips, rooted at the bottom edge of the image and reaching almost to its top, {ON_BLACK}"),
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
    # #2074 Arena Nigra (2026-09-27): the black volcanic sand of Theo's landmark — the sea the Ignivermis hunts in.
    ("black_sand", "fine jet-black volcanic sand, tiny glassy glints, faint dark-grey wind ripples, matte"),
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
    # #2210 the bio lab (2026-10-03): the front of the lab block.
    ("bio_lab", "a sci-fi biology lab bench block, dark metal machine front with a round glass sample chamber of glowing green fluid, a small teal status screen and a row of sample vials, front view"),
    ("auto_drill_1", "a sci-fi auto-drill mk1 block, compact dark metal rig with a single steel drill bit pointing down and one violet crystal light, front view"),
    ("auto_drill_2", "a sci-fi auto-drill mk2 block, heavier dark metal rig with twin steel drill bits pointing down, carbide edges and two violet crystal lights, front view"),
    ("auto_drill_3", "a sci-fi auto-drill mk3 block, massive dark metal rig with a wide diamond-tipped drill head pointing down and three glowing violet crystal lights, front view"),
    # #2108 the drill laser (2026-09-27): a Crystal Net machine that cuts a shaft straight down.
    ("drill_laser", "a sci-fi drill laser machine block, dark armoured metal housing with a large downward-pointing cyan laser emitter lens in the centre, thin glowing cyan light strips, three small violet crystal studs, front view"),
    # #2635 the derelict stations' guardian bay (2026-10-15): the broken maintenance dock the guardian robots come out of;
    # its sides wear the shared rust casing (face_rust_*), the front is this picture.
    ("guardian_spawner", "the front of an old broken sci-fi robot maintenance dock, a scratched dark grey steel frame around a recessed charging alcove that glows warm red, three small round red sensor lights in a row above it, a cracked hazard-striped hatch edge, loose sparking cables, rust spots, front view, flat lighting"),
    # #2113 the monorail (2026-09-27): the pylon, the stop and the rail dealer's post.
    ("rail_pylon", "a sci-fi monorail pylon block, a dark steel post with riveted bands and a glowing cyan energy emitter ring at the top, front view, flat lighting"),
    ("rail_stop", "a sci-fi monorail stop block, a dark steel signal post with a bright amber halt light and a small holographic timetable panel, front view, flat lighting"),
    ("rail_post", "a rail dealer's trading post block, a dark steel counter with a small model monorail wagon on top and a cyan rail-line sign, front view, flat lighting"),
    ("matter_sender", "a sci-fi matter sender block, dark metal pedestal with an upward violet crystal emitter ring and cyan energy motes rising, front view"),
    ("matter_receiver", "a sci-fi matter receiver block, dark metal pedestal with a downward cyan crystal collector ring and violet energy motes settling, front view"),
    # #2092 the Device Eye (2026-09-27): reads what a machine is doing and tells the wire.
    ("device_eye", "a sci-fi device eye block, a dark metal housing with one large round teal crystal lens like an eye looking forward, a thin brass rim and a small cable socket at the back, front view"),
    # #2270 Crystal Net 2 (2026-10-04): moving blocks and the new devices. The phase block is a pale neutral stone so dye
    # tints it to match any wall; its open twin is the shimmer it turns into.
    ("phase_block", "a pale neutral light-grey stone block with very faint thin violet crystal veins, smooth, evenly lit, low contrast"),
    ("phase_block_open", "a shimmering translucent violet energy veil, soft diagonal light streaks and tiny sparkles, mostly pale and airy"),
    ("trapdoor", "a sturdy wooden trapdoor hatch seen from above, dark oak planks in an iron frame with a ring handle and two hinges, top-down"),
    ("trapdoor_open", "a sturdy wooden trapdoor hatch seen from the side, dark oak planks in an iron frame with a ring handle, edge view"),
    ("bridge_motor", "a sci-fi drawbridge motor block, dark metal housing with a big geared winch drum, a violet crystal status light and an arrow plate, front view"),
    ("bridge_deck", "a sci-fi bridge deck plate, ribbed grey metal grating with yellow safety edge stripes, top-down"),
    ("piston", "a sci-fi piston block, dark steel housing with a thick polished steel push plate on its front face and a violet crystal power stripe, front view"),
    ("piston_head", "a polished steel piston push plate with a thick steel rod, riveted edges, front view"),
    ("lift_motor", "a sci-fi lift motor block, dark metal machine with a large cable pulley, a heavy gear and a cyan up-down arrow display, front view"),
    ("lift_stop", "a sci-fi lift call panel block, dark metal post with a round glowing call button and small up and down arrow lights, front view"),
    ("lift_platform", "a sci-fi elevator platform floor, dark steel plate with a raised diamond tread pattern and yellow-black hazard edge stripes, top-down"),
    ("signal_display", "a sci-fi display block, dark metal frame around a glowing dark-blue screen with a simple white pixel symbol, front view"),
    ("dice_block", "a sci-fi dice block, a white cube face with five round violet crystal pips set in a dark metal rim, front view"),
    ("signal_sender", "a sci-fi signal sender block, dark metal pedestal with a small upward antenna and violet crystal waves rising from it, front view"),
    ("signal_receiver", "a sci-fi signal receiver block, dark metal pedestal with a small dish antenna and cyan crystal waves arriving, front view"),
    ("environment_sensor", "a sci-fi environment sensor block, dark metal box with a thermometer, a tiny wind vane and a small air gauge dial, front view"),
    ("ship_sensor", "a sci-fi ship sensor block, dark metal panel with a glowing spaceship outline screen and a hull integrity bar, front view"),
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
    # 2026-09 Toxica-Maxima (#2066/#2067): the tainted ground, ores and wood of Justus' poisoned planet, and the decontaminator.
    ("tainted_soil", "dead grey-brown topsoil crusted with sickly luminous green slime veins and small glowing toxic puddles"),
    ("tainted_subsoil", "compacted dark clay earth streaked with dull green mineral poison seams"),
    ("tainted_stone", "cracked grey rock with glowing acid-green veins seeping through the cracks"),
    ("tainted_iron_ore", "grey stone with rusty orange iron ore flecks coated in a sickly glowing green toxic film"),
    ("tainted_copper_ore", "grey stone with orange copper veins corroded to a poisonous glowing green patina"),
    ("tainted_titanium_ore", "grey stone with silvery white titanium flecks under a glowing acid-green toxic crust"),
    ("tainted_diamond_ore", "dark grey rock with embedded pale diamond crystals clouded by a murky glowing green poison haze"),
    ("tainted_log", "dead grey tree bark with vertical wood grain, rotting luminous green sap seeping from the cracks"),
    ("decontaminator", "a sci-fi decontamination station, a sealed steel chamber with a round porthole of green rinse fluid, green-and-black warning stripes, pipes, valves and a rinse nozzle, top-down"),
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
    # The Fifi plant (#2085, generation 16) — Sophie's plant from the school club, in HER colours (authored: no world tint):
    # a green trunk, yellow leaves (bake_leaf_alpha punches the dark gaps), glowing pink blossoms, and a berry cluster
    # hanging from the top edge on black like the fruit tiles (the dark-ground bake cuts the black away).
    ("fifi_stem", "a smooth juicy bright green plant stalk surface like the stem of a giant flower, fine vertical fibres and a few lighter lime-green streaks, front view"),
    ("fifi_leaf", "a dense crown of many small overlapping bright lemon-yellow leaves with sunny highlights, dark olive shadows in the gaps between the leaves, top-down"),
    ("fifi_blossom", "densely packed bright pink flower blossoms filling the whole image edge to edge, overlapping soft hot-pink and light pink petals with small luminous pale pink centres, no background visible, top-down"),
    ("flora_fifi_berries", "a single hanging cluster of many small round shiny deep magenta-pink berries with bright highlights, hanging from a short green stem at the top edge of the image, the entire rest of the image solid pitch-black #000000 with no gradient and no vignette, side view"),
    # #2106 oil (2026-09-27): the finite underground fluid of the living worlds.
    ("oil", "thick black crude oil filling the whole image edge to edge, glossy near-black liquid surface with a faint iridescent oily sheen and a few slow dull bubbles, no background visible, no edges, no border, top-down"),
    # #2124 (2026-09-28) the object blocks' own faces — Justus: "still many blocks have the same texture on all sides".
    # A machine keeps its picture on its FRONT (or its top-view picture on top) and wears these on its other sides.
    # face_* keys are no blocks: data/blocks.json names them in faces[].tile and the atlas deals them into its extras
    # band. Every panel fills the whole tile edge to edge with its own border, so neighbouring blocks tile cleanly.
    # Shared casings — one look for a family of machines:
    ("face_tech_side", "the side panel of a sci-fi machine cabinet, a solid dark navy-grey metal casing panel that fills the entire image, a darker border frame, a thin glowing cyan light seam, recessed vent slots and small corner bolts, flat front view"),
    ("face_tech_top", "the top lid of a sci-fi machine cabinet seen from above, a solid dark navy-grey metal plate that fills the entire image, a darker border frame, a small grille of cooling vents in the middle and four corner bolts"),
    ("face_crystal_side", "the side panel of a sci-fi crystal device, a solid dark charcoal-grey metal housing panel that fills the entire image, a bevelled border frame, a thin glowing violet crystal inlay strip across the middle and small silver bolts, flat front view"),
    ("face_crystal_top", "the top plate of a sci-fi crystal device seen from above, a solid dark charcoal-grey metal plate that fills the entire image, a bevelled border frame, a small round glowing violet crystal stud in the centre and four corner bolts"),
    ("face_light_side", "the side panel of a clean clinic cabinet, white and pale mint-green enamel metal filling the whole tile edge to edge with a light grey border frame and a thin horizontal seam, no background, flat front view"),
    ("face_light_top", "the top of a clean clinic counter seen from above, a solid smooth white enamel worktop that fills the entire image with a thin pale grey rim at the edges"),
    ("face_wood_side", "the side panel of a sturdy wooden cabinet, warm brown vertical wooden planks filling the whole tile edge to edge inside a darker wooden frame with small iron corner brackets, no background, flat front view"),
    ("face_wood_top", "the top of a wooden counter seen from above, warm brown wooden planks lying side by side filling the whole tile edge to edge with a darker wooden edge, no background"),
    ("face_plastic_side", "the side of a black gaming computer case, a solid matte charcoal-black plastic panel that fills the entire image with subtle moulded ridges, a slightly lighter dark grey border and a thin bright rainbow RGB light strip glowing along the bottom edge, flat front view"),
    ("face_plastic_top", "the top of a black gaming computer case seen from above, a solid matte charcoal-black plastic panel that fills the entire image right up to its edges, a dark grey hexagonal mesh dust filter grille in the middle and a slightly lighter dark grey border"),
    ("face_rust_side", "the side panel of an old broken machine, rusty corroded orange-brown metal casing filling the whole tile edge to edge with dents, a few holes, flaking grey paint and loose rivets, no background, flat front view"),
    ("face_rust_top", "the top of an old broken machine seen from above, a rusty corroded metal plate filling the whole tile edge to edge with a cracked vent grille, dents and scattered rust, no background"),
    ("face_industrial_side", "the side panel of an industrial chemical machine, a solid dark grey steel casing panel that fills the entire image, a pipe with a red valve wheel running across it, bolts and a yellow and black hazard stripe band along the bottom edge, flat front view"),
    ("face_industrial_top", "the top of an industrial machine seen from above, a solid dark grey steel plate that fills the entire image, a round bolted inspection hatch in the middle, rivets and a thin yellow and black hazard stripe border"),
    # Bespoke sides and tops — a block that needs its own:
    ("face_workbench_side", "the side of a sci-fi metal workbench, a solid blue-grey steel cabinet that fills the entire image, two tool drawers with handles below and a pegboard with a hanging wrench and screwdriver above, a darker steel border, flat front view"),
    ("face_forge_side", "the side wall of a stone forge furnace, large dark grey stone bricks in neat staggered rows with light grey mortar joints and a few soot stains, the brick wall fills the entire image edge to edge, flat front view"),
    ("face_forge_top", "the top of a stone forge furnace seen from above, dark grey stone bricks filling the whole tile edge to edge around a small square chimney hole in the centre glowing orange with embers"),
    ("face_crate_top", "the lid of a sci-fi steel storage crate seen from above, a solid dark blue-grey metal lid that fills the entire image, a raised border, four corner latches and a recessed handle in the middle"),
    ("face_wood_crate_top", "the lid of a rustic wooden crate seen from above, rough brown wooden planks lying side by side inside a darker wooden frame filling the whole tile edge to edge, small nail heads at the corners, no background"),
    ("face_radio_beacon_top", "the top of a sci-fi radio beacon block seen from above, a tan sandstone-coloured square plate filling the whole tile edge to edge with a round dark metal mount in the centre holding a glowing cyan antenna ring and a small red status light"),
    ("face_base_core_side", "the side of a grey stone foundation cornerstone, smooth carved grey stone filling the whole tile edge to edge with a darker chiselled border and thin glowing teal-cyan energy lines running across it, no background, flat front view"),
    ("face_container_side", "the side wall of an orange cargo container, corrugated orange painted steel with vertical ribs filling the whole tile edge to edge, a dark border frame and a few scratches, no background, flat front view"),
    ("face_detoxifier_top", "the top of a round chemical detox vat seen from above, a dark metal rim holding bubbling glowing bright green liquid, small pipes at two corners, on a dark grey steel plate filling the whole tile edge to edge"),
    ("face_algae_tank_top", "the lid of an algae farm tank seen from above, a dark metal lid filling the whole tile edge to edge with a round glass porthole showing glowing green algae water and small bolts around it"),
    ("face_heal_tank_top", "the top cap of a medical regeneration capsule seen from above, a dark metal square plate filling the whole tile edge to edge with a round lid glowing soft teal-cyan in a ring around the centre and small bolts at the corners"),
    ("face_hydro_tray_side", "the side of a hydroponic plant tray, a solid dark grey metal trough wall that fills the entire image, a narrow glowing teal water level window along the top and two small drain pipes, flat front view"),
    ("face_gaming_pc_side", "a close-up of the tinted tempered glass side panel of a gaming computer tower, the dark glass and its thin black frame fill the entire image edge to edge with no margin, glowing rainbow RGB fans and a graphics card seen through the glass, flat front view"),
    ("face_sage_lectern_side", "the side panel of a dark violet armoured console cabinet, violet metal filling the whole tile edge to edge with engraved glowing circuit lines, a darker border and two small amber crystal indicator lights, no background, flat front view"),
    ("face_sage_lectern_top", "the top of a dark violet armoured console cabinet seen from above, violet metal filling the whole tile edge to edge with engraved circuit lines, a small vent grille and a round amber crystal in the centre, no background"),
    ("face_rail_pylon_top", "the top of a sci-fi monorail pylon seen from above, a round dark steel cap with a glowing cyan energy emitter ring and riveted bands, on a dark steel square plate filling the whole tile edge to edge"),
    ("face_alarm_siren_top", "the top of an alarm siren seen from above, a round red glass warning lamp dome with a bright highlight on a dark metal square base plate filling the whole tile edge to edge with corner bolts"),
    ("face_matter_sender_top", "the top of a sci-fi matter sender pedestal seen from above, a solid dark metal square plate that fills the entire image with a round violet crystal emitter ring glowing with small cyan energy motes in the centre"),
    ("face_matter_receiver_top", "the top of a sci-fi matter receiver pedestal seen from above, a round cyan crystal collector ring with small violet energy motes in the centre of a dark metal square plate filling the whole tile edge to edge"),
    *[(f"{block}_look{n}", desc) for block, looks in (
        # #2832 (2026-10-19) flora looks: each planet picks ONE of an archetype's looks (FloraLooks), so the same bush,
        # leaf crown or grass grows in another shape on the next world. The block's own tile is look 1; these are looks
        # 2..n. The shader keeps a flora tile's luminance and swaps its hue for the world colour, so a look differs by
        # SHAPE and light/dark pattern — every prompt asks for green, the colour comes from the planet.
        # Crowns are dense canopies edge to edge (bake_leaf_alpha cuts the darkest gaps between the leaves).
        ("tree_leaves", [
            "dense leafy tree foliage canopy of large broad heart-shaped green leaves overlapping, filling the whole image edge to edge with dark shadowed gaps between the leaves, top-down",
            "dense leafy tree foliage canopy of many small round coin-shaped green leaves like a birch, filling the whole image edge to edge with dark shadowed gaps between the leaves, top-down",
            "dense leafy tree foliage canopy of feathery pinnate compound green leaves with many small leaflets like an ash tree, filling the whole image edge to edge with dark shadowed gaps, top-down",
            "dense leafy tree foliage canopy of lobed five-pointed maple-like green leaves overlapping, filling the whole image edge to edge with dark shadowed gaps between the leaves, top-down",
            "dense leafy tree foliage canopy of long narrow slender willow-like green leaves hanging in strands, filling the whole image edge to edge with dark shadowed gaps, top-down",
            "dense leafy tree foliage canopy of wavy-edged round-lobed oak-like green leaves overlapping, filling the whole image edge to edge with dark shadowed gaps between the leaves, top-down",
            "dense leafy tree foliage canopy of big glossy oval jungle leaves with bright highlights, filling the whole image edge to edge with dark shadowed gaps between the leaves, top-down",
            "dense leafy tree foliage canopy of star-shaped pointed green leaves like a sweetgum tree, filling the whole image edge to edge with dark shadowed gaps between the leaves, top-down",
            "dense leafy tree foliage canopy of small fan-shaped ginkgo-like green leaves overlapping, filling the whole image edge to edge with dark shadowed gaps between the leaves, top-down",
            "dense leafy tree foliage canopy of fluffy rounded clusters of tiny scale-like green leaves like a cypress, filling the whole image edge to edge with dark shadowed gaps between the clusters, top-down",
            "dense leafy tree foliage canopy of crinkled spiky-edged holly-like green leaves overlapping, filling the whole image edge to edge with dark shadowed gaps between the leaves, top-down",
        ]),
        ("pine_needles", [
            "dense conifer foliage of long soft green pine needles in drooping tufts, filling the whole image edge to edge with dark shadowed gaps between the tufts, top-down",
            "dense conifer foliage of short stiff dark green spruce needles densely set on small twigs, filling the whole image edge to edge with dark shadowed gaps, top-down",
            "dense conifer foliage of flat sprays of green fir branches with neat rows of short needles, filling the whole image edge to edge with dark shadowed gaps, top-down",
            "dense conifer foliage of soft green larch needle tufts like little brushes, filling the whole image edge to edge with dark shadowed gaps between the tufts, top-down",
            "dense conifer foliage of round fluffy green pom-pom clusters of needles, filling the whole image edge to edge with dark shadowed gaps between the clusters, top-down",
        ]),
        ("palm_frond", [
            "dense tropical palm foliage of long feathery pinnate green fronds with many thin leaflets crossing each other, filling the whole image edge to edge with dark shadowed gaps, top-down",
            "dense tropical palm foliage of round pleated green fan-palm leaves overlapping, filling the whole image edge to edge with dark shadowed gaps between the fans, top-down",
            "dense tropical foliage of huge broad green banana-plant leaves with torn edges overlapping, filling the whole image edge to edge with dark shadowed gaps, top-down",
            "dense tropical palm foliage of arching coconut palm fronds with long drooping narrow leaflets, filling the whole image edge to edge with dark shadowed gaps, top-down",
            "dense foliage of stiff spiky green cycad fronds with sharp narrow leaflets radiating outwards, filling the whole image edge to edge with dark shadowed gaps, top-down",
        ]),
        ("giant_leaves", [
            "dense canopy of huge heart-shaped dark green elephant-ear leaves with pale veins overlapping, filling the whole image edge to edge with dark shadowed gaps, top-down",
            "dense canopy of giant deeply split dark green monstera-like leaves with holes and pale veins, filling the whole image edge to edge with dark shadowed gaps, top-down",
            "dense canopy of layered huge round flat dark green leaves like giant lily pads with radiating veins, filling the whole image edge to edge with dark shadowed gaps, top-down",
            "dense canopy of very long broad strap-shaped dark green leaves with parallel pale veins crossing each other, filling the whole image edge to edge with dark shadowed gaps, top-down",
            "dense canopy of enormous feathery dark green fern-like fronds of an ancient tree overlapping, filling the whole image edge to edge with dark shadowed gaps, top-down",
        ]),
        # The billboards are ONE plant in side view on black (#2773: ON_BLACK, cut by the dark-ground bake, rooted on the
        # bottom edge). Blossoms and berries are drawn bright and colourful: the accent shader (#2834) gives them a colour
        # of their own, apart from the leaves.
        ("flora_bush", [
            f"side view of one round leafy green bush with many small oval leaves, rooted at the bottom edge of the image, filling its lower two thirds and most of its width, {ON_BLACK}",
            f"side view of one leafy green bush with large broad rounded leaves, rooted at the bottom edge of the image, filling its lower two thirds and most of its width, {ON_BLACK}",
            f"side view of one green bush of narrow pointed spiky leaves fanning outwards, rooted at the bottom edge of the image, filling its lower two thirds and most of its width, {ON_BLACK}",
            f"side view of one dense compact green bush of tiny leaves like a boxwood, rooted at the bottom edge of the image, filling its lower two thirds and most of its width, {ON_BLACK}",
            f"side view of one airy green shrub of thin branching twigs with small leaf clusters, rooted at the bottom edge of the image, filling its lower two thirds and most of its width, {ON_BLACK}",
            f"side view of one green bush with long drooping arching leaves, rooted at the bottom edge of the image, filling its lower two thirds and most of its width, {ON_BLACK}",
            f"side view of one round leafy green bush dotted with many small bright yellow and orange round berries, rooted at the bottom edge of the image, filling its lower two thirds and most of its width, {ON_BLACK}",
        ]),
        ("flora_plant", [
            f"side view of one leafy green herb plant with a few big broad ribbed leaves on short stalks, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}",
            f"side view of one green plant with tall upright sword-shaped leaves, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}",
            f"side view of one low rosette of wavy-edged green leaves spreading outwards, rooted at the bottom edge of the image and filling its lower half and most of its width, {ON_BLACK}",
            f"side view of one green plant with heart-shaped leaves on long thin stalks, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}",
            f"side view of one green plant with finely divided feathery leaves like carrot tops, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}",
            f"side view of one green plant with big round flat leaves on thin stems at different heights, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}",
            f"side view of one green plant whose leaves grow in a spiral up a thick stem, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}",
        ]),
        ("flora_grasstuft", [
            f"side view of one tuft of tall thin straight green grass blades, rooted at the bottom edge of the image and reaching almost to its top, {ON_BLACK}",
            f"side view of one tuft of curly wavy green grass blades, rooted at the bottom edge of the image and reaching to its middle, {ON_BLACK}",
            f"side view of one tuft of green grass blades with small seed heads on thin stalks, rooted at the bottom edge of the image and reaching almost to its top, {ON_BLACK}",
            f"side view of one tuft of broad flat green grass blades, rooted at the bottom edge of the image and reaching almost to its top, {ON_BLACK}",
            f"side view of one tuft of green grass with soft feathery plumes on top, rooted at the bottom edge of the image and reaching almost to its top, {ON_BLACK}",
            f"side view of one short dense tuft of green grass, rooted at the bottom edge of the image and filling its lower half, {ON_BLACK}",
            f"side view of one fountain-shaped tuft of long green grass blades arching outwards and drooping, rooted at the bottom edge of the image, {ON_BLACK}",
        ]),
        ("flora_fern", [
            f"side view of one upright green shuttlecock fern with tall fronds forming a vase shape, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}",
            f"side view of one green sword fern with long arching fronds, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}",
            f"side view of one young green fern with curled fiddlehead spirals among unrolled fronds, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}",
            f"side view of one delicate lacy green maidenhair fern with tiny fan leaflets on thin dark stems, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}",
            f"side view of one small green tree fern with a short trunk and a crown of fronds, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}",
        ]),
        ("flora_flower", [
            f"side view of one clump of daisy-like flowers with bright pink petals and yellow centres on green stems, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}",
            f"side view of a few cup-shaped bright red tulip-like flowers on green stems with broad leaves, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}",
            f"side view of one plant with many small bright violet star-shaped flowers on green stems, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}",
            f"side view of one plant with drooping bright magenta bell-shaped flowers on arching green stems, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}",
            f"side view of one plant with flat umbrella clusters of tiny bright yellow flowers on green stems, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}",
            f"side view of one plant with tall spikes densely packed with small bright blue flowers above green leaves, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}",
            f"side view of a few big bright orange-red poppy-like flowers with dark centres on thin green stems, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}",
        ]),
    ) for n, desc in enumerate(looks, 2)],
    # #2833 (generation 28) the new wild species that grow side by side with the old ones: one side-view plant on black each,
    # rooted on the bottom edge; blossoms and berries bright for the accent shader (#2834).
    ("flora_berrybush", f"side view of one round green bush heavy with clusters of many small shiny bright red round berries among its leaves, rooted at the bottom edge of the image, filling its lower two thirds and most of its width, {ON_BLACK}"),
    ("flora_blossombush", f"side view of one green bush completely covered in many small bright pink five-petalled blossoms, rooted at the bottom edge of the image, filling its lower two thirds and most of its width, {ON_BLACK}"),
    ("flora_featherbush", f"side view of one bush of soft feathery green plume-like leaves fanning upwards like ostrich feathers, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}"),
    ("flora_droopbush", f"side view of one weeping green shrub whose long thin leafy strands hang down like a small willow, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}"),
    ("flora_needlebush", f"side view of one spiky dry-land shrub of stiff narrow needle-like grey-green leaves radiating outwards, rooted at the bottom edge of the image, filling its lower two thirds, {ON_BLACK}"),
    ("flora_frostberry", f"side view of one low hardy shrub with small frosted green leaves and clusters of bright pale blue round berries, rooted at the bottom edge of the image, filling its lower half and most of its width, {ON_BLACK}"),
    ("flora_clover", f"side view of one low patch of green three-lobed clover leaves with a few round bright pink clover flower heads on short stems, rooted at the bottom edge of the image, filling its lower half and most of its width, {ON_BLACK}"),
    ("flora_daisy", f"side view of one clump of daisies with bright yellow centres and bright orange petals on thin green stems, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}"),
    ("flora_thistle", f"side view of one thistle plant with spiky jagged green leaves and round bright purple spiky flower heads on top, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}"),
    ("flora_herb", f"side view of one bushy green kitchen herb like mint with many small oval leaves on several upright stems, rooted at the bottom edge of the image and filling most of it, {ON_BLACK}"),
    ("flora_foxtail", f"side view of one tuft of tall green grass with fluffy cylindrical bushy seed heads nodding on top, rooted at the bottom edge of the image and reaching almost to its top, {ON_BLACK}"),
    ("flora_lupine", f"side view of one lupine plant with tall upright spikes densely packed with small bright violet pea-shaped blossoms above palm-shaped green leaves, rooted at the bottom edge of the image and reaching almost to its top, {ON_BLACK}"),
    # #2851/#2855 (2026-10-20, generation 29) tree variety: two more crown slots (tree_leaves_b / _c) and twelve more crown
    # looks for the shared leaf pool — every planet gives its three crown slots three different looks from it. Like the
    # other crowns the colour comes from the planet (the shader keeps the luminance), so they differ by leaf SHAPE.
    ("tree_leaves_b", "dense leafy tree foliage canopy of tiny fine feathery bipinnate leaflets like an acacia in flat layered sprays, filling the whole image edge to edge with dark shadowed gaps between the leaves, top-down"),
    ("tree_leaves_c", "dense leafy tree foliage canopy of long narrow sickle-shaped drooping green leaves like a eucalyptus, filling the whole image edge to edge with dark shadowed gaps between the leaves, top-down"),
    *[(f"tree_leaves_look{n}", f"{desc}, filling the whole image edge to edge with dark shadowed gaps between the leaves, top-down") for n, desc in enumerate([
        "dense leafy tree foliage canopy of narrow pointed lance-shaped green bamboo leaves in fanned sprays",
        "dense leafy tree foliage canopy of arching green fern fronds with many small leaflets and curled tips like a tree fern",
        "dense leafy tree foliage canopy of small triangular diamond-shaped green poplar leaves on thin stalks",
        "dense leafy tree foliage canopy of tiny dense rounded green leaves clipped into soft round topiary clumps",
        "dense leafy tree foliage canopy of stiff sword-shaped spiky green leaves in starburst rosettes like a dragon tree",
        "dense alien tree foliage canopy of curled spiral green leaves rolled up like fiddleheads",
        "dense alien tree foliage canopy of round smooth bubble-like green leaf pods with soft highlights",
        "dense leafy tree foliage canopy of small toothed green leaves with hanging slender catkins like a hazel",
        "dense leafy tree foliage canopy of crinkled frilly curly-edged green leaves like kale",
        "dense alien tree foliage canopy of long twisting ribbon-like green leaves curling around each other",
        "dense leafy tree foliage canopy of small slender silvery grey-green olive leaves in sprays",
        "dense leafy tree foliage canopy of big round deeply lobed green fig leaves with pale veins",
    ], 13)],
    # The blossom tree's crown (#2853): blossoms bright and colourful between green leaves, so the accent shader (#2834)
    # gives the blossoms a colour of their own and the leaves the planet's colour.
    ("blossom_leaves", "dense tree crown covered in many five-petal bright pink cherry blossoms with a few small green leaves between them, filling the whole image edge to edge with dark shadowed gaps between the blossom clusters, top-down"),
    *[(f"blossom_leaves_look{n}", desc) for n, desc in enumerate([
        "dense tree crown covered in clusters of bright white and pink apple blossoms with round pink buds and small green leaves between them, filling the whole image edge to edge with dark shadowed gaps between the clusters, top-down",
        "dense tree crown covered in large cup-shaped bright pink and purple magnolia blossoms between a few green leaves, filling the whole image edge to edge with dark shadowed gaps between the blossoms, top-down",
        "dense tree crown covered in clusters of many small bright violet trumpet-shaped blossoms like a jacaranda between fine green leaves, filling the whole image edge to edge with dark shadowed gaps between the clusters, top-down",
    ], 2)],
    # Bark looks (#2855): wood_log gets looks for the first time, and the pale bark slot wood_log_b (#2851) its own set. The
    # bark tint keeps the luminance, so a look differs by its grain.
    *[(f"wood_log_look{n}", desc) for n, desc in enumerate([
        "rough deeply furrowed grey-brown oak tree bark with thick vertical ridges filling the whole image edge to edge",
        "smooth grey beech tree bark with faint horizontal marks and a few small knots filling the whole image edge to edge",
        "glossy reddish-brown cherry tree bark with horizontal lenticel stripes filling the whole image edge to edge",
        "shaggy tree bark peeling in long loose vertical strips filling the whole image edge to edge",
        "scaly pine tree bark of irregular thick flat plates with dark cracks between them filling the whole image edge to edge",
    ], 2)],
    ("wood_log_b", "white birch tree bark with short black horizontal dashes and a few dark knots, filling the whole image edge to edge"),
    *[(f"wood_log_b_look{n}", desc) for n, desc in enumerate([
        "pale silvery smooth aspen tree bark with dark diamond-shaped eye marks, filling the whole image edge to edge",
        "pale cream papery tree bark peeling in thin curling layers, filling the whole image edge to edge",
    ], 2)],
    # #2850: the saguaro's own block — a ribbed cactus skin (solid, opaque) instead of tree leaves.
    ("saguaro", "ribbed green saguaro cactus skin with straight vertical ribs and small pale spine clusters along each rib, filling the whole image edge to edge"),
    # #2853: the lantern tree's glowing fruit, hanging under its crown (a fruit on black, cut like the fruit trees' fruit).
    ("flora_lantern_fruit", "a single round glowing warm golden-orange lantern-like fruit with soft luminous ribbed skin, hanging from a short dark stem at the top edge of the image, one small dark leaf at the stem, the entire rest of the image solid pitch-black #000000 with no gradient and no vignette, side view"),
    # #2836 (2026-10-19) held-tool materials: a part of a held tool, gun or gadget samples one of these, tinted by the part's
    # colour (the shader multiplies), so each tile is light and mostly grayscale with a clear surface grain.
    ("held_metal", "brushed light grey steel metal surface with fine parallel brush lines and a few tiny scratches, light and mostly grayscale"),
    ("held_darkmetal", "light grey gunmetal with a fine knurled diamond grip pattern and small panel seams, light and mostly grayscale"),
    ("held_wood", "light pale wooden surface with clear long straight wood grain lines and a small knot, light and mostly grayscale"),
    ("held_plastic", "smooth light grey moulded plastic with a very faint fine speckle and a soft sheen, light and mostly grayscale"),
    ("held_rubber", "light grey rubber grip with a pattern of small raised ridges for a firm hold, light and mostly grayscale"),
    ("held_glass", "light pale glass lens surface with a soft round reflection highlight and faint concentric rings, light and mostly grayscale"),
    ("held_energy", "bright glowing energy surface with soft flowing light bands and small bright sparks, very light and mostly grayscale"),
    ("held_crystal", "light faceted crystal surface of sharp angular facets with bright edges, light and mostly grayscale"),
    ("held_leather", "close-up of light grey leather strap wrapped around a handle in overlapping diagonal bands, fine leather grain and a row of small stitches along each band, no bricks, no tiles, light and mostly grayscale"),
    ("held_stone", "light grey chipped stone surface with flaked knapped facets like a flint tool, light and mostly grayscale"),
    ("held_bone", "light ivory bone surface with fine cracks and a smooth worn sheen, light and mostly grayscale"),
    ("held_carbon", "light grey woven carbon fibre twill pattern with a soft sheen, light and mostly grayscale"),
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
                # background="opaque" (#2124): block tiles ship opaque (bundle_textures forces alpha 255), and a prompt
                # that says "no background" otherwise tends to come back as a mostly transparent PNG whose hidden
                # colours are a blurry grey once the alpha is dropped.
                resp = client.images.generate(
                    model="gpt-image-1-mini", prompt=prompt, size="1024x1024", quality="low", n=1, background="opaque")
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
