// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The on-screen touch controls (TouchControlsUi) — the move stick and every button.</summary>
    public enum TouchControl
    {
        Stick,
        Prev,
        Next,
        Menu,
        SlotActions,
        ContextActions,
        VegaNext,
        Jump,
        Mine,
        Place,
        Use,
        Descend,
        Chat,
        FootView,
        FootMap,
        Rotate,
        Attack,
        Feed,
        Run,
        Fire,
        FlyUse,
        Land,
        Ship,
        Auto,
        FlyView,
        FlyMap,
        Up,
        Down,
        EvaPlace,
        EvaDeploy,
        Target,
        Boost,
        Hop,
        Exit,
        Fuel,
    }

    /// <summary>Which control context shows a control: always (<see cref="Shared"/>), on foot, in flight and on an EVA (one
    /// cluster — the EVA pair takes the helm-only spots), or driving a speeder.</summary>
    public enum TouchCluster
    {
        Shared,
        OnFoot,
        Flight,
        Speeder,
    }

    /// <summary>One touch control: its context, its anchor on the 1920 × 1080 touch canvas (0…1 per axis, uGUI style — y up),
    /// the offset of its centre from that anchor, its side length, and its label (a locale key with an English fallback, or a
    /// symbol with no key).</summary>
    public readonly struct TouchControlSpec
    {
        public TouchControlSpec(TouchControl control, TouchCluster cluster, float anchorX, float anchorY, float x, float y, float size,
            string? labelKey, string fallback)
        {
            Control = control;
            Cluster = cluster;
            AnchorX = anchorX;
            AnchorY = anchorY;
            X = x;
            Y = y;
            Size = size;
            LabelKey = labelKey;
            Fallback = fallback;
        }

        public TouchControl Control { get; }

        public TouchCluster Cluster { get; }

        public float AnchorX { get; }

        public float AnchorY { get; }

        /// <summary>The centre's offset from the anchor in touch units (+y up, as uGUI's anchoredPosition).</summary>
        public float X { get; }

        public float Y { get; }

        public float Size { get; }

        public string? LabelKey { get; }

        public string Fallback { get; }
    }

    /// <summary>
    /// The touch layout (#202, #1041, #1042, #3376) as one table: TouchControlsUi builds every button from it, and the HUD's
    /// zones are kept clear of the same rectangles (<see cref="TouchReservation"/>). The touch canvas is a 1920 × 1080
    /// reference with <c>ScreenMatchMode.Expand</c> that does NOT follow the player's UI scale; the HUD canvases are 1536 × 864
    /// divided by the UI scale, also Expand — so one touch unit is always <c>0.8 / uiScale</c> HUD units, whatever the screen's
    /// aspect (<see cref="HudPerTouch"/>). Unity-free so the headless suite pins the geometry at every aspect and scale.
    /// </summary>
    public static class TouchLayout
    {
        /// <summary>The touch canvas's reference resolution.</summary>
        public const float RefW = 1920f, RefH = 1080f;

        /// <summary>The HUD's reference resolution at UI scale 1 (UiKit.HudRefW × HudRefH).</summary>
        public const float HudRefW = 1536f, HudRefH = 864f;

        private static readonly TouchControlSpec[] Table =
        {
            // Shared: the move stick bottom left, the hotbar cycle and the two list openers bottom centre, the menu top right,
            // VEGA's NEXT (its default spot — the touch layer moves it beside VEGA's speech panel).
            Spec(TouchControl.Stick, TouchCluster.Shared, 0f, 0f, 230f, 230f, 320f, null, string.Empty),
            Spec(TouchControl.Prev, TouchCluster.Shared, 0.5f, 0f, -360f, 130f, 90f, null, "◄"),
            Spec(TouchControl.Next, TouchCluster.Shared, 0.5f, 0f, 360f, 130f, 90f, null, "►"),
            Spec(TouchControl.Menu, TouchCluster.Shared, 1f, 1f, -90f, -90f, 96f, null, "≡"),
            Spec(TouchControl.SlotActions, TouchCluster.Shared, 0.5f, 0f, 480f, 130f, 90f, null, "…"),
            Spec(TouchControl.ContextActions, TouchCluster.Shared, 0.5f, 0f, -480f, 130f, 90f, "ui.touch.actions", "ACT"),
            Spec(TouchControl.VegaNext, TouchCluster.Shared, 0.5f, 1f, 0f, -90f, 96f, "ui.touch.next", "NEXT ▶"),

            // On foot: the action cluster bottom right, the chat / view / map row top right, RUN above-right of the stick.
            Spec(TouchControl.Jump, TouchCluster.OnFoot, 1f, 0f, -140f, 150f, 120f, "ui.touch.jump", "JUMP"),
            Spec(TouchControl.Mine, TouchCluster.OnFoot, 1f, 0f, -280f, 260f, 120f, "ui.touch.mine", "MINE"),
            Spec(TouchControl.Place, TouchCluster.OnFoot, 1f, 0f, -140f, 300f, 120f, "ui.touch.place", "PLACE"),
            Spec(TouchControl.Use, TouchCluster.OnFoot, 1f, 0f, -300f, 130f, 110f, "ui.touch.use", "USE"),
            Spec(TouchControl.Descend, TouchCluster.OnFoot, 1f, 0f, -430f, 170f, 96f, "ui.touch.down", "DOWN"),
            Spec(TouchControl.Chat, TouchCluster.OnFoot, 1f, 1f, -200f, -90f, 88f, "ui.touch.chat", "CHAT"),
            Spec(TouchControl.FootView, TouchCluster.OnFoot, 1f, 1f, -310f, -90f, 88f, "ui.touch.view", "VIEW"),
            Spec(TouchControl.FootMap, TouchCluster.OnFoot, 1f, 1f, -420f, -90f, 88f, "ui.touch.map", "MAP"),
            Spec(TouchControl.Rotate, TouchCluster.OnFoot, 1f, 0f, -420f, 300f, 96f, "ui.touch.rotate", "ROTATE"),
            Spec(TouchControl.Attack, TouchCluster.OnFoot, 1f, 0f, -560f, 240f, 100f, "ui.touch.attack", "ATTACK"),
            Spec(TouchControl.Feed, TouchCluster.OnFoot, 1f, 0f, -560f, 360f, 96f, "ui.touch.feed", "FEED"),
            Spec(TouchControl.Run, TouchCluster.OnFoot, 0f, 0f, 450f, 430f, 96f, "ui.touch.run", "RUN"),

            // Flight and EVA: PLACE + DEPLOY take the helm-only LAND / SHIP spots on an EVA.
            Spec(TouchControl.Fire, TouchCluster.Flight, 1f, 0f, -140f, 150f, 130f, "ui.touch.fire", "FIRE"),
            Spec(TouchControl.FlyUse, TouchCluster.Flight, 1f, 0f, -300f, 130f, 110f, "ui.touch.use", "USE"),
            Spec(TouchControl.Land, TouchCluster.Flight, 1f, 0f, -290f, 265f, 110f, "ui.touch.land", "LAND"),
            Spec(TouchControl.Ship, TouchCluster.Flight, 1f, 0f, -150f, 305f, 100f, "ui.touch.ship", "SHIP"),
            Spec(TouchControl.Auto, TouchCluster.Flight, 1f, 0f, -420f, 190f, 92f, "ui.touch.auto", "AUTO"),
            Spec(TouchControl.FlyView, TouchCluster.Flight, 1f, 1f, -200f, -90f, 88f, "ui.touch.view", "VIEW"),
            Spec(TouchControl.FlyMap, TouchCluster.Flight, 1f, 1f, -310f, -90f, 88f, "ui.touch.map", "MAP"),
            Spec(TouchControl.Up, TouchCluster.Flight, 0f, 0f, 430f, 300f, 96f, "ui.touch.up", "UP"),
            Spec(TouchControl.Down, TouchCluster.Flight, 0f, 0f, 430f, 190f, 96f, "ui.touch.down", "DOWN"),
            Spec(TouchControl.EvaPlace, TouchCluster.Flight, 1f, 0f, -290f, 265f, 110f, "ui.touch.place", "PLACE"),
            Spec(TouchControl.EvaDeploy, TouchCluster.Flight, 1f, 0f, -150f, 305f, 100f, "ui.touch.deploy", "DEPLOY"),
            Spec(TouchControl.Target, TouchCluster.Flight, 1f, 0f, -420f, 310f, 92f, "ui.touch.target", "TARGET"),

            // Speeder (a boat hides HOP and FUEL).
            Spec(TouchControl.Boost, TouchCluster.Speeder, 1f, 0f, -140f, 150f, 130f, "ui.touch.boost", "BOOST"),
            Spec(TouchControl.Hop, TouchCluster.Speeder, 1f, 0f, -300f, 130f, 110f, "ui.touch.jump", "JUMP"),
            Spec(TouchControl.Exit, TouchCluster.Speeder, 1f, 0f, -290f, 265f, 110f, "ui.touch.exit", "EXIT"),
            Spec(TouchControl.Fuel, TouchCluster.Speeder, 1f, 0f, -150f, 305f, 100f, "ui.touch.fuel", "FUEL"),
        };

        /// <summary>Every control, in the order the touch layer builds them.</summary>
        public static IReadOnlyList<TouchControlSpec> All => Table;

        /// <summary>One control's entry.</summary>
        public static TouchControlSpec Of(TouchControl control)
        {
            foreach (var spec in Table)
            {
                if (spec.Control == control)
                {
                    return spec;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(control), control, "No touch layout entry.");
        }

        /// <summary>HUD units per touch unit: the HUD's reference is 0.8 of the touch canvas's and shrinks by the UI scale.</summary>
        public static float HudPerTouch(float uiScale) => HudRefH / RefH / Math.Max(0.01f, uiScale);

        /// <summary>The size of an Expand canvas with reference <paramref name="refW"/> × <paramref name="refH"/> on a screen of
        /// <paramref name="screenW"/> × <paramref name="screenH"/> pixels — what uGUI's CanvasScaler makes of it.</summary>
        public static (float W, float H) ExpandCanvas(float screenW, float screenH, float refW, float refH)
        {
            float scale = Math.Min(screenW / refW, screenH / refH);
            return (screenW / scale, screenH / scale);
        }

        /// <summary>A control's rectangle on a touch canvas of <paramref name="canvasW"/> × <paramref name="canvasH"/> touch
        /// units, top-left origin and y down like a <see cref="HudRect"/>.</summary>
        public static HudRect RectOnCanvas(TouchControlSpec spec, float canvasW, float canvasH)
        {
            float cx = (spec.AnchorX * canvasW) + spec.X;
            float cyUp = (spec.AnchorY * canvasH) + spec.Y;
            float half = spec.Size / 2f;
            return new HudRect(cx - half, canvasH - cyUp - half, spec.Size, spec.Size);
        }

        private static TouchControlSpec Spec(TouchControl control, TouchCluster cluster, float ax, float ay, float x, float y, float size,
            string? key, string fallback)
            => new TouchControlSpec(control, cluster, ax, ay, x, y, size, key, fallback);
    }
}
