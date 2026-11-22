// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What each graphics preset meshes and builds of the look at all (#3946). The browser and Potato exist so the game
    /// runs on slow devices, so: Potato gets the cheapest form that still looks whole, Low a cheap one, and nothing is
    /// prepared for a drawer that does not run on the preset. Medium and High keep everything.
    /// <list type="bullet">
    ///   <item>The near drawers' data (crown cells, grass edges and ground cover, plant cells and the vines) is recorded
    ///   only where their range is above 0: a desktop-class client on Medium or High.</item>
    ///   <item>Needle and palm crowns: their far forms on every cell on Medium and High, on the crown's outer cells only on
    ///   Low (the cells inside are covered by them), the leaf block's shell on Potato as before the look.</item>
    ///   <item>The quiet natural emitters' longer reach: Medium and High only, fixed per world (the light index is built as
    ///   the chunks arrive).</item>
    ///   <item>Rounded and formed parts on figures and held tools: every tier but Potato, fixed for the run.</item>
    ///   <item>The plant far pictures: baked on a desktop client on Low and up; never on Potato or in the browser.</item>
    /// </list>
    /// The mesher reads its gates on worker threads, so they rest on <see cref="FxKit.Preset"/> and a main-thread snapshot
    /// of the device class, never on a Unity call. A preset change that changes what the mesher emits
    /// (<see cref="MeshTierKey"/>) has every loaded chunk meshed again (GameBootstrap.LookTier.cs).
    /// </summary>
    public static partial class GameLook
    {
        /// <summary>The crown far forms per tier (<see cref="CrownFarFormsTier"/>).</summary>
        public const int CrownFarNone = 0, CrownFarOuter = 1, CrownFarAll = 2;

        private static volatile bool _desktopClass;
        private static bool _figuresFixed;
        private static bool _plainFigures;
        private static volatile bool _longEmitters;
        private static volatile bool _worldLatched;

        /// <summary>Takes the preset on the main thread (<see cref="ClientSettings.Apply"/>: at start and after every
        /// settings change): the device class for the mesher's threads, and — at the first call — the figures' tier.</summary>
        public static void ConfigureTier(QualityPreset preset)
        {
            _desktopClass = HighDetails.DesktopClass;
            if (!_figuresFixed)
            {
                _figuresFixed = true;
                _plainFigures = preset == QualityPreset.Potato;
            }
        }

        /// <summary>Fixes the natural emitters' reach for the world about to load (GameBootstrap: world start and every
        /// world change, before the first chunk is indexed): the light index keeps each source's reach from the moment its
        /// chunk arrived, so the reach must not change while the world is loaded.</summary>
        public static void LatchWorldTier()
        {
            _longEmitters = FxKit.Preset >= QualityPreset.Medium;
            _worldLatched = true;
        }

        /// <summary>True when the quiet natural emitters reach further (<see cref="EmitterLight"/>): the world was entered
        /// on Medium or High; before the first world (the menu's scenes) the current preset decides. Safe on any thread.</summary>
        public static bool LongEmitterReach => _worldLatched ? _longEmitters : FxKit.Preset >= QualityPreset.Medium;

        /// <summary>True when figures and held tools keep plain boxes: the run started on Potato. Fixed for the run, so a
        /// figure built before a preset change never mixes round and sharp parts with one built after it.</summary>
        public static bool PlainFigures => _plainFigures;

        /// <summary>True where the near drawers run (their ranges, <see cref="CrownRangeNow"/>, <see cref="GrassRangeNow"/>,
        /// <see cref="PlantRangeNow"/>, are above 0): a desktop-class client on Medium or High. Safe on any thread.</summary>
        private static bool NearDrawersRun => _desktopClass && FxKit.Preset >= QualityPreset.Medium;

        /// <summary>True when the mesher records crown cells (and the conifer's spire) for <see cref="CrownClusters"/>.</summary>
        public static bool RecordCrownCells => StagedCrowns && NearDrawersRun;

        /// <summary>True when the mesher records the grass tops' open edges and the ground cover of bare ground for
        /// <see cref="GrassClumps"/> (the High carpet's tops are recorded on every preset as before).</summary>
        public static bool RecordGrassClumps => GrassClumps && NearDrawersRun;

        /// <summary>True when the mesher records plant cells (and the crown vines) for <see cref="PlantBodies"/> and marks
        /// their crossed pictures; otherwise the pictures stay unmarked and are never hidden near the eye.</summary>
        public static bool RecordPlantCells => PlantBodies && NearDrawersRun;

        /// <summary>How needle and palm crown cells are meshed: <see cref="CrownFarAll"/> on Medium and High (every cell its
        /// crown form's far form), <see cref="CrownFarOuter"/> on Low (a cell inside the crown draws nothing, the outer
        /// cells' forms cover it), <see cref="CrownFarNone"/> on Potato (the leaf block's shell, no spire). Safe on any
        /// thread.</summary>
        public static int CrownFarFormsTier
        {
            get
            {
                if (!CrownFormsOn)
                {
                    return CrownFarNone;
                }

                return FxKit.Preset switch
                {
                    QualityPreset.Potato => CrownFarNone,
                    QualityPreset.Low => CrownFarOuter,
                    _ => CrownFarAll,
                };
            }
        }

        /// <summary>True when the plant far pictures are baked (<c>PlantBodies.FarPictures.cs</c>): a desktop-class client on
        /// Low and up. On Potato and in the browser a plant keeps the crossed picture of its tile. Main thread.</summary>
        public static bool PlantFarPictures => HighDetails.DesktopClass && FxKit.Preset != QualityPreset.Potato;

        /// <summary>Everything the chunk mesher's output depends on by tier, as one number: when it changes, the loaded
        /// chunks are meshed again so no chunk keeps a tier its neighbours no longer share (a crown cut at a seam, a
        /// picture hidden near the eye with no body to take its place). Main thread.</summary>
        public static int MeshTierKey
            => (RecordCrownCells ? 1 : 0) | (RecordGrassClumps ? 2 : 0) | (RecordPlantCells ? 4 : 0)
               | (CrownFarFormsTier << 3) | (BlockShapeGeometry.SoftTierAllows ? 32 : 0);
    }
}
