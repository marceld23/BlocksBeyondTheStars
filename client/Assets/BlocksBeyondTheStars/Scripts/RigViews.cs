// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The world rig's views and panels, one set per rig: WorldRig.Build registers each right after AddComponent (after its
    /// Awake), VegaPanel registers itself in Start; reached through <see cref="GameBootstrap.Views"/> (#2914). It replaces
    /// the views' former <c>static Instance</c> singletons; the modal in-world panels have their own registry,
    /// <see cref="ClientContext"/> (#2913). Plain C#: no Unity lifecycle.
    /// </summary>
    public sealed class RigViews
    {
        private HudUi _hud;
        private PaintToolUi _paintTool;
        private ShapeToolUi _shapeTool;
        private BeamView _beams;
        private VegaPanel _vega;
        private NpcView _npcs;
        private DoorView _doors;
        private DataCubeView _dataCubes;
        private FactoryView _factories;
        private NetFragmentView _netFragments;
        private FinaleView _finale;
        private ResolutionCinematic _resolution;
        private CutsceneDirector _director;
        private LaunchPrompt _launchPrompt;
        private VendorChoicePrompt _vendorChoice;
        private MicroFaunaView _microFauna;
        private OreScanView _oreScan;
        private ThermalVision _thermal;
        private MiningFx _mining;

        /// <summary>Set while a HUD exists so world-side FX (MiningFx) can hand off pickup fly-ins.</summary>
        public HudUi Hud { get => Live(_hud); set => _hud = value; }

        /// <summary>The block-paint editor host (#818): opens when the paint tool is used on a placed block.</summary>
        public PaintToolUi PaintTool { get => Live(_paintTool); set => _paintTool = value; }

        /// <summary>The form editor host (#845): opens when the shaping tool or a stencil is used.</summary>
        public ShapeToolUi ShapeTool { get => Live(_shapeTool); set => _shapeTool = value; }

        /// <summary>The beam blocks (teleporter pads) — their reach answers the HUD prompt and the E press.</summary>
        public BeamView Beams { get => Live(_beams); set => _beams = value; }

        /// <summary>The live panel (one per rig), for client-side one-shot hints (<see cref="VegaPanel.SayLocal"/>).
        /// Registered by the panel itself in its Start, not by WorldRig.</summary>
        public VegaPanel Vega { get => Live(_vega); set => _vega = value; }

        /// <summary>#2592: the live view (one per game root) — the dialog panel and other client code reach the NPCs' voices
        /// and gestures through it.</summary>
        public NpcView Npcs { get => Live(_npcs); set => _npcs = value; }

        /// <summary>Set so the player controller can find the hinge door it should toggle on E.</summary>
        public DoorView Doors { get => Live(_doors); set => _doors = value; }

        /// <summary>The minigame data cubes on the surface (E downloads the nearest one).</summary>
        public DataCubeView DataCubes { get => Live(_dataCubes); set => _dataCubes = value; }

        /// <summary>The factories' machines and their production-terminal prompt.</summary>
        public FactoryView Factories { get => Live(_factories); set => _factories = value; }

        /// <summary>The story net fragments on the surface (E recovers the nearest one).</summary>
        public NetFragmentView NetFragments { get => Live(_netFragments); set => _netFragments = value; }

        /// <summary>So the music director can read the current finale phase.</summary>
        public FinaleView Finale { get => Live(_finale); set => _finale = value; }

        /// <summary>So the finale banner (IMGUI draws above every canvas) can yield to the ending.</summary>
        public ResolutionCinematic Resolution { get => Live(_resolution); set => _resolution = value; }

        /// <summary>The live director (one per world rig), or null.</summary>
        public CutsceneDirector Director { get => Live(_director); set => _director = value; }

        /// <summary>The "Launch into space?" question at the cockpit of a landed ship (#1455).</summary>
        public LaunchPrompt LaunchPrompt { get => Live(_launchPrompt); set => _launchPrompt = value; }

        /// <summary>The "Trade or talk?" question at a vendor NPC.</summary>
        public VendorChoicePrompt VendorChoice { get => Live(_vendorChoice); set => _vendorChoice = value; }

        /// <summary>Set while a view exists so the handheld scanner and thermal vision (#757) can query the live critters —
        /// they are client-local, so no other system knows they exist.</summary>
        public MicroFaunaView MicroFauna { get => Live(_microFauna); set => _microFauna = value; }

        /// <summary>The scene's overlay (one per world rig) — lets the Tab menu drop a station marker (#1072).</summary>
        public OreScanView OreScan { get => Live(_oreScan); set => _oreScan = value; }

        /// <summary>The overlay of the running world rig (null outside a world).</summary>
        public ThermalVision Thermal { get => Live(_thermal); set => _thermal = value; }

        /// <summary>The world's mining effects (one per world rig).</summary>
        public MiningFx Mining { get => Live(_mining); set => _mining = value; }

        // UnityEngine.Object's overloaded != reads a destroyed component as null, so a getter hands back null once its
        // component is gone — exactly what the former static Instance did after OnDestroy.
        private static T Live<T>(T component) where T : UnityEngine.Object => component != null ? component : null;
    }
}
