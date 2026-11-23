// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The fixed values of the game's look, in one place: the sky gets a gradient, the shadows take the sky's colour, the
    /// sun's arc is tilted, natural tiles are calmed toward their average colour and models stand in the real sun; tree
    /// crowns, grass, small plants and fur gain real geometry near the camera. The shader side reads them through the
    /// globals <c>Sky.Look.cs</c> publishes every frame.
    /// <para>
    /// The values are fixed for the whole run, so the chunk mesher may read them on its worker threads. What follows the
    /// graphics preset is computed live: the ranges of the near geometry (crown clusters, grass clumps, plant bodies,
    /// felt) by preset, never on a light device (browser, phone), and each shrinks under its per-frame budget in a crowd
    /// (the <c>Adapt*</c> functions). What a preset meshes and builds at all — so Low and Potato never prepare data for
    /// drawers they do not run — is decided in <c>GameLook.Tiers.cs</c>.
    /// </para>
    /// </summary>
    public static partial class GameLook
    {
        public static readonly float AmbShare = 1.0f;
        public static readonly float SunShare = 1.1f;
        public static readonly float AmbSat = 0.7f;
        public static readonly float GroundLevel = 0.85f;
        public static readonly float Calm = 0.85f;
        public static readonly float ZenithAmount = 0.95f;
        public static readonly float ZenithDark = 0.5f;
        public static readonly float ZenithSat = 1.5f;

        /// <summary>The crown mode the shaders read (<c>_Sc_Look2.y</c>): 3 = staged — micro-block clusters near the camera
        /// (<see cref="CrownClusters"/>), a closed cube with a painted cube pattern beyond.</summary>
        public static readonly int CrownMode = 3;

        /// <summary>How far each small cube of a crown cluster rocks in the wind (1 = about 2 cm).</summary>
        public static readonly float Flutter = 3f;

        /// <summary>Staged crowns: the width of the cross-fade at the end of the clusters' range, and the distance from the
        /// eye at which a cube starts to shrink away (it is gone at 0.4 × that).</summary>
        public static readonly float CrownFade = 8f;
        public static readonly float CrownNear = 1.8f;

        /// <summary>Staged crowns: how many clusters a frame may submit before the range shrinks — a dense forest holds six
        /// times the crown cells of a jungle — and how far the clusters cast their own shadows (beyond it the closed far
        /// cube casts; the shadow passes are the larger half of the clusters' cost).</summary>
        public static readonly float CrownBudget = 20000f;
        public static readonly float CrownShadow = 28f;

        /// <summary>Soft blocks: the rounded border's width in tile units and the joint's darkness.</summary>
        public static readonly float SoftEdge = 0.11f;
        public static readonly float Joint = 0.45f;

        /// <summary>Fine grain on natural faces and the rim light on models and closed crowns.</summary>
        public static readonly float Grain = 1f;
        public static readonly float Rim = 0.6f;

        /// <summary>Grass clumps (<see cref="GrassClumps"/>): clumps per grass top (1..3) and a factor on the blades'
        /// height.</summary>
        public static readonly float GrassDensity = 2f;
        public static readonly float GrassHeight = 1f;

        /// <summary>How much the High preset's grass carpet is calmed under the clumps (1 = thicker strands and less sway):
        /// its fine strands flickered.</summary>
        public static readonly float GrassCalm = 1f;

        /// <summary>Plant bodies (<see cref="PlantBodies"/>): the width of the cross-fade to the crossed picture at the end
        /// of their range.</summary>
        public static readonly float PlantFade = 6f;

        /// <summary>How many plant bodies a frame may submit before their range shrinks.</summary>
        public static readonly float PlantBudget = 6000f;

        /// <summary>How many felt triangles a frame may submit before the felt's range shrinks: a herd of furry animals
        /// around the player must not cost the frame. The Medium preset takes half of it.</summary>
        public static readonly float CoatBudget = 400000f;

        /// <summary>Clear shallows: a factor on the glitter of the water surface toward the sun (Medium and High only —
        /// the water's screen-space part).</summary>
        public static readonly float WaterGlitter = 1f;

        /// <summary>Glowing haze: how much the haze and the valley mist take the colour of lava, crystals and glowing
        /// plants around the player (1 = fully where there are many of them).</summary>
        public static readonly float GlowHaze = 0.8f;

        /// <summary>The play's depth of field, a factor on the blur's radius: the view focuses on what is in the middle of
        /// the picture and the background well behind it goes soft (<c>UrpScenePost</c>).</summary>
        public static readonly float Dof = 1f;

        /// <summary>Calmer hides (0 = the tiles as they are … 1 = one flat colour): how far every texel of a creature's
        /// hide tile moves toward the tile's mean colour.</summary>
        public static readonly float HideCalm = 0.4f;

        /// <summary>Rounded edges on the creatures' box parts (<see cref="RoundedCube"/>): the radius as a share of a
        /// part's thinnest side. 0.2 → 0.12 on 2026-11-23 (Justus: "the rounded corners are cool, just a bit less please").</summary>
        public static readonly float Round = 0.12f;

        /// <summary>Rounded edges on the player avatar's boxes (<c>PlayerAvatar.Round.cs</c>), which every figure wears —
        /// players and NPCs alike: the radius as a share of a part's thinnest side. A painted part keeps its paint texels
        /// where they are, so its radius is capped at one texel of its paint tile. 0.16 → 0.10 on 2026-11-23 (Justus, as
        /// <see cref="Round"/>).</summary>
        public static readonly float AvatarRound = 0.10f;

        /// <summary>The calm nature tiles: the step in brightness between the three variants of a ground block (0.02 = 2 %
        /// darker and 2 % lighter).</summary>
        public static readonly float TileTone = 0.02f;

        /// <summary>How strong the calm tiles' pattern is (1 = as drawn, 0.5 = half, 0 = one flat colour): every texel
        /// moves toward its tile's mean colour by the rest. The relief the atlas derives from a tile follows.</summary>
        public static readonly float TilePattern = 1f;

        /// <summary>A factor on the size of every broken block's pieces (<c>ChunkMesher.DebrisFit</c>).</summary>
        public static readonly float DebrisSize = 1f;

        /// <summary>Rounded edges on the parts of a held tool: the radius as a share of a part's thinnest side. Drills,
        /// picks, guns, blades, scanners and gadgets in the first-person hand and in every figure's hand; a part thinner
        /// than 6 mm stays sharp.</summary>
        public static readonly float ToolRound = 0.18f;

        /// <summary>Flattened corners and a cone on held tools: 1 = barrels, shafts, antennas and rods get their long edges
        /// cut flat, square flanges, lens rings and caps too (an octagon seen face-on), and a drill's bit becomes a cone
        /// (<c>HeldPartForms</c>). A part may name its form in the data (<c>"f"</c>); a player's own tool look keeps its
        /// boxes. <see cref="ToolChamfer"/> is the cut as a share of the bar's thinner cross side (0.05 … 0.2929, where
        /// 0.2929 makes a square bar a regular octagon).</summary>
        public static readonly float ToolForms = 1f;
        public static readonly float ToolChamfer = 0.28f;

        /// <summary>The far stage of the rounded boxes (<c>RoundLod.cs</c>): a rounded part of a figure, an animal or a tool
        /// shows its plain cube from where the rounding is under half a pixel (a whole pixel on the low tiers); a factor on
        /// that distance.</summary>
        public static readonly float RoundFar = 1f;

        /// <summary>The leaf picture on the small cubes of a tree crown: the crown's leaf tile runs across all cubes of a
        /// cell, at this share of its strength. Near the eye only — it fades to the plain cubes between
        /// <see cref="LeafNear"/> and <see cref="LeafFar"/> metres, where a leaf would be smaller than a pixel and only
        /// shimmer.</summary>
        public static readonly float LeafPicture = 0.5f;
        public static readonly float LeafNear = 10f;
        public static readonly float LeafFar = 20f;

        /// <summary>How much of the world's light the first-person hand and tool take (0 = the neutral studio light, 1 = the
        /// world's light like every other model). Low, so the hand stays readable.</summary>
        public static readonly float Hand = 0.15f;

        private static readonly float Tilt = 32f;
        private static readonly float Twilight = 0.45f;
        private static readonly float Bodies = 2.2f;
        private static readonly float BloomGain = 1.6f;
        private static readonly float BloomThreshold = 0.75f;
        private static readonly float Reach = 2f;

        private static bool _carpetAoApplied;

        /// <summary>The High preset's SSAO (blue noise, a new pattern every frame, radius 0.3 m, tuned for block edges)
        /// flickers on the grass carpet: its sixteen layers of centimetre-small strand plates are a chaos of depth
        /// steps the SSAO's blur cannot smooth, so every carpet pixel got a different occlusion in every frame —
        /// measured on consecutive frames of a standing camera as per-frame noise around a fixed value, on the carpet
        /// only. So the carpet leaves the depth prepass the SSAO reads and is drawn right after the SSAO has been
        /// applied: the occlusion sees the plain ground and the carpet takes none. Called every frame; changes the
        /// material once.</summary>
        public static void ApplyCarpetAo(Material carpet)
        {
            if (carpet == null || _carpetAoApplied)
            {
                return;
            }

            _carpetAoApplied = true;
            carpet.SetShaderPassEnabled("DepthOnly", false);
            carpet.renderQueue = 2501; // first after the opaques (and the SSAO)
        }

        /// <summary>True: crowns are staged — the mesher records every crown cell for <see cref="CrownClusters"/> and
        /// meshes the closed far picture.</summary>
        public static bool StagedCrowns => true;

        /// <summary>How far the real clusters of staged crowns are drawn right now, in metres (0 = not at all): by graphics
        /// preset — High 48, Medium 24, Low and Potato none; never on a light device (browser, phone) — times the budget's
        /// factor. Follows a preset change live.</summary>
        public static float CrownRangeNow => CrownRangeWanted * _crownAdapt;

        private static float CrownRangeWanted
        {
            get
            {
                if (!HighDetails.DesktopClass)
                {
                    return 0f;
                }

                return FxKit.Preset switch
                {
                    QualityPreset.High => 48f,
                    QualityPreset.Medium => 24f,
                    _ => 0f,
                };
            }
        }

        private static float _crownAdapt = 1f;

        /// <summary>Keeps the near clusters inside <see cref="CrownBudget"/>: called once a frame with the clusters the
        /// last frame submitted. Over the budget the range shrinks by 3 % a frame (down to a third), well under it it
        /// grows back by 1 % — slow steps the cross-fade hides, with a dead band so the range does not swing.</summary>
        public static void AdaptCrownRange(int submitted)
        {
            if (CrownBudget <= 0f)
            {
                _crownAdapt = 1f;
            }
            else if (submitted > CrownBudget)
            {
                _crownAdapt = Mathf.Max(0.33f, _crownAdapt * 0.97f);
            }
            else if (submitted < CrownBudget * 0.8f)
            {
                _crownAdapt = Mathf.Min(1f, _crownAdapt * 1.01f);
            }
        }

        /// <summary>How far the near clusters cast their own shadows right now: <see cref="CrownShadow"/>, never beyond the
        /// middle of the cross-fade (there the far cube takes over anyway).</summary>
        public static float CrownShadowNow => Mathf.Min(CrownShadow, CrownRangeNow - (0.5f * CrownFade));

        /// <summary>True: grass tops grow clumps of blades (the chunk mesher records the open edges of a grass top).</summary>
        public static bool GrassClumps => true;

        /// <summary>True: strands hang under the tree crowns, drawn as plant bodies near the camera — display only, no
        /// block.</summary>
        public static bool CrownVines => true;

        /// <summary>True: the calm nature tiles stand in for the bundled pixel-art ones.</summary>
        public static bool CalmTiles => true;

        /// <summary>The brightness step between a calm ground tile and its two variants (0 … 0.2).</summary>
        public static float TileToneNow => Mathf.Clamp(TileTone, 0f, 0.2f);

        /// <summary>The share of a calm tile's pattern that is kept (0 … 3; the tiles are baked at the chosen strength, so 2
        /// would double it).</summary>
        public static float TilePatternNow => Mathf.Clamp(TilePattern, 0f, 3f);

        /// <summary>True: ground cover also grows under plants — the grass carpet and its clumps (and the dry tufts of bare
        /// ground) grow under a plant that stands on a narrow foot, lower under a plant with leaves near the ground, not
        /// at all under a low cushion or a solid plant. Display only.</summary>
        public static bool CoverUnderPlants => true;

        /// <summary>True: debris cubes (the pieces a broken block splits into, a drill's chips, the rubble in space) show a
        /// piece of their block's tile (<c>FxKit.Debris.cs</c>), on every tier and in the browser.</summary>
        public static bool TexturedDebris => true;

        /// <summary>The factor on the size of every broken block's pieces right now.</summary>
        public static float DebrisSizeNow => Mathf.Clamp(DebrisSize, 0.1f, 2f);

        /// <summary>True: the micro-fauna is drawn as small bodies — a butterfly, a beetle, a little fish is a few boxes and
        /// plates in its colours, with wings that beat and a tail that wags (<c>CritterBodies.cs</c>); the kinds that are
        /// lights stay glowing sprites. Every tier.</summary>
        public static bool CritterBodiesOn => true;

        /// <summary>The share of a held tool part's thinnest side its edges are rounded by: on every tier but Potato, which
        /// keeps plain boxes (<see cref="PlainFigures"/>).</summary>
        public static float ToolRoundShare => PlainFigures ? 0f : Mathf.Min(ToolRound, 0.45f);

        /// <summary>Which tool parts get their own form (<c>HeldPartForms.Shown</c>): 1 = bars, plates and the drill's cone,
        /// 2 = bars and the cone, 0 = none (Potato, <see cref="PlainFigures"/>).</summary>
        public static int ToolFormsMode => PlainFigures ? 0 : Mathf.Clamp(Mathf.RoundToInt(ToolForms), 0, 2);

        /// <summary>The chamfer's cut as a share of a part's smaller cross side, 0.05 … 0.2929 (a regular octagon).</summary>
        public static float ToolChamferShare => Mathf.Clamp(ToolChamfer, 0.05f, 0.2929f);

        /// <summary>The factor on the distance at which a rounded part shows its plain cube (0 = never).</summary>
        public static float RoundFarScale => Mathf.Clamp(RoundFar, 0f, 8f);

        /// <summary>True: the surface clouds are heaps of billows (<c>Clouds.cs</c>, <c>Cloud.shader</c>).</summary>
        public static bool BillowClouds => true;

        /// <summary>True: grass, alien grass and mycelium show earth on their sides with the grass hanging over the upper
        /// edge, and earth below (<c>LookSod.cs</c>; the side tile is composed from the calm tiles).</summary>
        public static bool GrassSod => true;

        /// <summary>True: the bottom face of a mushroom cap block shows pale gills (<c>LookSod.cs</c>).</summary>
        public static bool CapGills => true;

        /// <summary>True: a geyser vent wears the tile of the ground beside it and only its hole is laid on top
        /// (<c>ChunkMesher.Vent.cs</c>).</summary>
        public static bool VentGround => true;

        /// <summary>True: a conifer's needle cells are boughs that slope outward, cut to a cone, under a spire, and a palm's
        /// frond cells are long feathered fronds (<c>ChunkMesher.CrownForms.cs</c>). Near the camera only, like the
        /// clusters.</summary>
        public static bool CrownFormsOn => true;

        /// <summary>True: where a harvested plant regrows, a small version of that very plant stands and grows
        /// (<c>FloraGrowthFx.cs</c>).</summary>
        public static bool RegrowBodies => true;

        /// <summary>True: a plant, a crystal, a torch, a lantern, a ladder or a flower pot breaks into much smaller pieces
        /// that start close to it, a crown's leaves into medium ones; a full block keeps the 30 to 44 cm cubes
        /// (<c>ChunkMesher.DebrisFit</c>).</summary>
        public static bool SmallDebris => true;

        /// <summary>The leaf picture on the crowns' small cubes for the shader (<c>_Sc_LookLeaf</c>): x = its share, y and z
        /// = where it starts to fade and where it is gone (metres from the eye), w = how much darker the gaps between the
        /// leaves are drawn.</summary>
        public static Vector4 LeafPictureNow
        {
            get
            {
                float near = Mathf.Max(0f, LeafNear);
                return new Vector4(Mathf.Clamp01(LeafPicture), near, Mathf.Max(near + 0.5f, LeafFar), 0.22f);
            }
        }

        /// <summary>True: bare ground gets its cover of dry tufts and pebbles, drawn with the grass clumps.</summary>
        public static bool GroundCover => true;

        /// <summary>How far the grass clumps are drawn right now, in metres (0 = not at all): by graphics preset — High 24,
        /// Medium 12, Low and Potato none; never on a light device (browser, phone).</summary>
        public static float GrassRangeNow
        {
            get
            {
                if (!HighDetails.DesktopClass)
                {
                    return 0f;
                }

                return FxKit.Preset switch
                {
                    QualityPreset.High => 24f,
                    QualityPreset.Medium => 12f,
                    _ => 0f,
                };
            }
        }

        /// <summary>True: small plants of the species with a generated form are drawn as bodies near the camera (the chunk
        /// mesher records the plants and marks their crossed pictures).</summary>
        public static bool PlantBodies => true;

        /// <summary>How far the plant bodies are drawn right now, in metres (0 = not at all, the crossed pictures stay): by
        /// graphics preset — High 32, Medium 16, Low and Potato none; never on a light device (browser, phone) — times the
        /// budget's factor.</summary>
        public static float PlantRangeNow => PlantRangeWanted * _plantAdapt;

        private static float _plantAdapt = 1f;

        /// <summary>Keeps the plant bodies inside <see cref="PlantBudget"/>: called once a frame with the bodies the last
        /// frame submitted. Over the budget (a kelp forest, a dense jungle floor) the range shrinks by 3 % a frame, down
        /// to a third; well under it it grows back by 1 % — slow steps the cross-fade hides.</summary>
        public static void AdaptPlantRange(int submitted)
        {
            if (PlantBudget <= 0f)
            {
                _plantAdapt = 1f;
            }
            else if (submitted > PlantBudget)
            {
                _plantAdapt = Mathf.Max(0.33f, _plantAdapt * 0.97f);
            }
            else if (submitted < PlantBudget * 0.8f)
            {
                _plantAdapt = Mathf.Min(1f, _plantAdapt * 1.01f);
            }
        }

        private static float PlantRangeWanted
        {
            get
            {
                if (!HighDetails.DesktopClass)
                {
                    return 0f;
                }

                return FxKit.Preset switch
                {
                    QualityPreset.High => 32f,
                    QualityPreset.Medium => 16f,
                    _ => 0f,
                };
            }
        }

        /// <summary>True when the standard animals may wear fur and feathers as geometry (where the hide is a fur or a
        /// plumage): a desktop-class device on Medium or High. Read on the main thread when a creature's body is
        /// built.</summary>
        public static bool Coats => HighDetails.DesktopClass && FxKit.Preset >= QualityPreset.Medium;

        /// <summary>The share of a creature part's thinnest side its edges are rounded by right now (0 = plain cubes):
        /// <see cref="Round"/> on a desktop-class device on Medium or High, otherwise 0.</summary>
        public static float RoundShare
            => HighDetails.DesktopClass && FxKit.Preset >= QualityPreset.Medium ? Mathf.Min(Round, 0.45f) : 0f;

        /// <summary>The share of an avatar part's thinnest side its edges are rounded by (display only, a few hundred
        /// vertices a figure): on every tier but Potato, which keeps plain boxes. Fixed for the run
        /// (<see cref="PlainFigures"/>), so a figure never mixes round and sharp parts.</summary>
        public static float AvatarRoundShare => PlainFigures ? 0f : Mathf.Min(AvatarRound, 0.45f);

        /// <summary>How much the haze takes the colour of the natural lights around right now.</summary>
        public static float GlowHazeNow => Mathf.Clamp01(GlowHaze);

        /// <summary>True when the play's depth of field may run: a desktop-class device on Medium or High.</summary>
        public static bool FocusBlur => Dof > 0.01f && HighDetails.DesktopClass && FxKit.Preset >= QualityPreset.Medium;

        /// <summary>How far the creatures' hide tiles are calmed. Read once, when the hide tiles are loaded.</summary>
        public static float HideCalmNow => Mathf.Clamp01(HideCalm);

        /// <summary>True when the standard animals get three blunt dark toes in front of every sole: a desktop-class device
        /// on Medium or High.</summary>
        public static bool Toes => HighDetails.DesktopClass && FxKit.Preset >= QualityPreset.Medium;

        /// <summary>1 = a coat where the hide says so (a fur or a plumage).</summary>
        public static int CoatMode => 1;

        /// <summary>How far the short felt is drawn right now, in metres (strands and feathers have no range): High 18,
        /// otherwise 9, times the budget's factor; 0 where there are no coats.</summary>
        public static float CoatRangeNow
        {
            get
            {
                if (!Coats)
                {
                    return 0f;
                }

                return (FxKit.Preset == QualityPreset.High ? 18f : 9f) * _coatAdapt;
            }
        }

        private static float _coatAdapt = 1f;
        private static long _coatFrameTriangles;

        /// <summary>Counts the felt one animal draws this frame (main thread, <see cref="CoatMotion"/>).</summary>
        public static void CountCoat(int triangles) => _coatFrameTriangles += triangles;

        /// <summary>Keeps the felt inside <see cref="CoatBudget"/>: called once a frame. Over the budget the range shrinks
        /// by 3 % a frame (down to a third), well under it it grows back by 1 % — as for the crowns and the plants.</summary>
        public static void AdaptCoatRange()
        {
            float budget = CoatBudget * (FxKit.Preset == QualityPreset.High ? 1f : 0.5f);
            long submitted = _coatFrameTriangles;
            _coatFrameTriangles = 0;
            if (budget <= 0f)
            {
                _coatAdapt = 1f;
            }
            else if (submitted > budget)
            {
                _coatAdapt = Mathf.Max(0.33f, _coatAdapt * 0.97f);
            }
            else if (submitted < budget * 0.8f)
            {
                _coatAdapt = Mathf.Min(1f, _coatAdapt * 1.01f);
            }
        }

        /// <summary>The share of the full coat's blades a part grows (fixed when the body is built): High 1, otherwise
        /// 0.5.</summary>
        public static float CoatDensityNow => FxKit.Preset == QualityPreset.High ? 1f : 0.5f;

        /// <summary>How far away a coat still trails the animal's movement.</summary>
        public static float CoatMotionRange => FxKit.Preset == QualityPreset.High ? 45f : 25f;

        /// <summary>The cross-fade's width right now (never wider than the range).</summary>
        public static float PlantFadeNow => Mathf.Clamp(PlantFade, 0.5f, Mathf.Max(0.5f, PlantRangeNow));

        /// <summary>How far the bodies cast their own shadows right now: to the middle of the cross-fade — beyond it the
        /// crossed picture casts.</summary>
        public static float PlantShadowNow => Mathf.Max(0f, PlantRangeNow - (0.5f * PlantFadeNow));

        /// <summary>The play bloom: a lower threshold and a stronger, wider glow outdoors.</summary>
        public static void Bloom(ref float threshold, ref float intensity, float home = 0f)
        {
            // Aboard the ship (home = 1) the plain bloom stays: the cabin is full of lamps and lit panels, and the
            // stronger glow washed it out.
            float outdoors = 1f - Mathf.Clamp01(home);
            threshold = Mathf.Lerp(threshold, Mathf.Min(threshold, BloomThreshold), outdoors);
            intensity *= Mathf.Lerp(1f, Mathf.Max(0f, BloomGain), outdoors);
        }

        /// <summary>The bloom's scatter: wider outdoors, <paramref name="aboard"/> at home.</summary>
        public static float BloomScatter(float aboard, float home) => Mathf.Lerp(0.74f, aboard, Mathf.Clamp01(home));

        /// <summary>A packed block light (<c>BlockLight.Pack</c>: colour in bits 0–23, reach in bits 24–27 with 0 = the
        /// full reach, surface-only in bit 28) with a longer reach for the quiet natural emitters — lava and crystals
        /// light a cliff, not only the blocks beside them. Full-reach lights: unchanged. Medium and High only: on Low and
        /// Potato the emitters keep their classic reach (<see cref="LongEmitterReach"/>, fixed per world).</summary>
        public static int EmitterLight(int packed)
        {
            int reach = (packed >> 24) & 0xF;
            if (packed == 0 || reach == 0 || !LongEmitterReach)
            {
                return packed;
            }

            int longer = Mathf.Clamp(Mathf.RoundToInt(reach * Reach), 1, 9);
            return (packed & ~(0xF << 24)) | ((longer >= 9 ? 0 : longer) << 24);
        }

        /// <summary>How far the sky bodies are enlarged.</summary>
        public static float BodyScale => Mathf.Max(0.1f, Bodies);

        /// <summary>The golden hour's band of the sun height, wider than <paramref name="plain"/>, which it replaces.</summary>
        public static float TwilightBand(float plain) => Mathf.Max(0.05f, Twilight);

        /// <summary>The sun light's rotation at <paramref name="time"/> (0 = midnight, 0.5 = noon): the arc leans about the
        /// line from sunrise to sunset, so the sun still rises and sets at the same moments but stands lower at noon —
        /// side faces get sun all day.</summary>
        public static Quaternion SunRotation(float time)
            => Quaternion.Euler(0f, 160f, 0f) * Quaternion.AngleAxis(Tilt, Vector3.forward)
               * Quaternion.Euler(time * 360f - 90f, 0f, 0f);
    }
}
