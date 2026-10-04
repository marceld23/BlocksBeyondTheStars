// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>An sRGB colour, 0..1 per channel (the caller converts to the shader's space).</summary>
    public readonly struct FxColor : IEquatable<FxColor>
    {
        public FxColor(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }

        public float R { get; }

        public float G { get; }

        public float B { get; }

        /// <summary>Blends toward white by <paramref name="t"/> (0 = unchanged, 1 = white) — the default hot core.</summary>
        public FxColor Lighten(float t)
        {
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return new FxColor(R + ((1f - R) * t), G + ((1f - G) * t), B + ((1f - B) * t));
        }

        public bool Equals(FxColor other) => R.Equals(other.R) && G.Equals(other.G) && B.Equals(other.B);

        public override bool Equals(object? obj) => obj is FxColor other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = R.GetHashCode();
                h = (h * 397) ^ G.GetHashCode();
                return (h * 397) ^ B.GetHashCode();
            }
        }

        public static bool operator ==(FxColor left, FxColor right) => left.Equals(right);

        public static bool operator !=(FxColor left, FxColor right) => !left.Equals(right);

        public override string ToString()
            => string.Format(CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", R, G, B);
    }

    /// <summary>
    /// The effect a tool or ship module plays, fully resolved (#2152): a known <see cref="FxStyles"/> style, both colours,
    /// and the numbers with their defaults applied. Built by <see cref="FxStyleResolver"/>.
    /// </summary>
    public sealed class ResolvedFx
    {
        public ResolvedFx(string style, FxColor color, FxColor color2, float charge, float size, float speed, bool fromData)
        {
            Style = style;
            Color = color;
            Color2 = color2;
            Charge = charge;
            Size = size;
            Speed = speed;
            FromData = fromData;
        }

        /// <summary>One of <see cref="FxStyles"/> — always a known one.</summary>
        public string Style { get; }

        /// <summary>The primary colour (glow, beam, trail).</summary>
        public FxColor Color { get; }

        /// <summary>The secondary colour (hot core / highlight); a lightened <see cref="Color"/> when the data has none.</summary>
        public FxColor Color2 { get; }

        /// <summary>Seconds of cosmetic charge-up before the shot (0 = none).</summary>
        public float Charge { get; }

        /// <summary>Effect scale (1 = normal).</summary>
        public float Size { get; }

        /// <summary>Projectile speed of a travelling style in blocks/s; 0 = the style's default (the client's choice).</summary>
        public float Speed { get; }

        /// <summary>True when the look came from the item's/module's <c>fx</c> data, false when from the heuristics.</summary>
        public bool FromData { get; }
    }

    /// <summary>
    /// Picks the effect for a held tool or a ship module (#2152). The <c>fx</c> object of the data
    /// (<see cref="ToolProperties.Fx"/>, <see cref="ShipModuleDefinition.Fx"/>) wins whenever it names a known style; an
    /// item without one (an older content pack, a mod) falls back to the heuristics the client used before the
    /// overhaul — by item key for weapons, by tool kind for everything else. Unity-free so the headless suite pins it and
    /// so the local player's effect and a remote <c>ActionFx</c> resolve exactly the same way.
    /// </summary>
    public static class FxStyleResolver
    {
        /// <summary>How far toward white the default secondary colour sits.</summary>
        private const float DefaultCoreLighten = 0.6f;

        /// <summary>Upper bound for a data charge-up (a typo must not hold a shot's flash back for minutes).</summary>
        private const float MaxCharge = 2f;

        /// <summary>Bounds for a data size factor.</summary>
        private const float MinSize = 0.1f;
        private const float MaxSize = 8f;

        /// <summary>The effect for a held tool. <paramref name="itemKey"/> null/"" or no <paramref name="tool"/> = bare
        /// hands (<see cref="FxStyles.Fist"/>).</summary>
        public static ResolvedFx ForTool(string? itemKey, ToolProperties? tool)
        {
            if (tool?.Fx is { } fx && FxStyles.IsKnown(fx.Style))
            {
                return FromData(fx);
            }

            return Heuristic(StyleForTool(itemKey, tool));
        }

        /// <summary>The effect for a ship module in space (weapons, tractor, scanner, shield, warp).</summary>
        public static ResolvedFx ForShipModule(string? moduleKey, ShipModuleDefinition? def)
        {
            if (def?.Fx is { } fx && FxStyles.IsKnown(fx.Style))
            {
                return FromData(fx);
            }

            return Heuristic(StyleForModule(moduleKey, def));
        }

        /// <summary>Reads <c>#rrggbb</c> (the leading <c>#</c> is required) into 0..1 channels. False for anything else —
        /// null, a wrong length, a non-hex digit.</summary>
        public static bool TryParseHex(string? hex, out FxColor color)
        {
            color = default;
            if (hex == null || hex.Length != 7 || hex[0] != '#')
            {
                return false;
            }

            int rgb = 0;
            for (int i = 1; i < 7; i++)
            {
                int digit = HexDigit(hex[i]);
                if (digit < 0)
                {
                    return false;
                }

                rgb = (rgb << 4) | digit;
            }

            color = new FxColor(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
            return true;
        }

        /// <summary>Close-combat styles: a swing arc (or the gloves' push and punches, #2278) at the hand, no projectile.</summary>
        public static bool IsMelee(string style)
            => style is FxStyles.Slash or FxStyles.Vibro or FxStyles.PlasmaBlade or FxStyles.Fist
                or FxStyles.ShockPush or FxStyles.EnergyFist;

        /// <summary>The two glove styles (#2278): drawn with both hands, no slash ribbon — the shock push sends a ring
        /// forward between the palms, the energy fist alternates left and right punches.</summary>
        public static bool IsGlove(string style) => style is FxStyles.ShockPush or FxStyles.EnergyFist;

        /// <summary>The sound of a melee swing in <paramref name="style"/> — for the player's own swing and for another
        /// player's (#2279), so both hear the same: the shock gloves' air blast, the energy gloves' whoosh, a blade's swish
        /// for every other close-combat look (and fists, tools swung at something).</summary>
        public static string MeleeSwingCue(string style) => style switch
        {
            FxStyles.ShockPush => "glove_shock_blast",
            FxStyles.EnergyFist => "glove_whoosh",
            _ => "melee_swing",
        };

        /// <summary>The sound of a melee hit landing in <paramref name="style"/> (#2278/#2279): the energy gloves' zap, the
        /// shock gloves' blast at the target (played quieter there), the classic hit for everything else.</summary>
        public static string MeleeHitCue(string style) => style switch
        {
            FxStyles.ShockPush => "glove_shock_blast",
            FxStyles.EnergyFist => "glove_energy_hit",
            _ => "melee_hit",
        };

        /// <summary>Styles that fly a visible projectile from muzzle to impact (use <see cref="ResolvedFx.Speed"/>).</summary>
        public static bool IsTravelling(string style)
            => style is FxStyles.Slug or FxStyles.Plasma or FxStyles.PlasmaBolt or FxStyles.TwinPulse;

        /// <summary>Styles drawn as an instant line from muzzle to impact.</summary>
        public static bool IsBeam(string style)
            => style is FxStyles.Laser or FxStyles.Rail or FxStyles.MiningBeam or FxStyles.HeavyBeam or FxStyles.DrillBeam;

        /// <summary>The colour a style has when the data names none (or an unreadable one).</summary>
        public static FxColor DefaultColor(string style) => style switch
        {
            FxStyles.Fist => new FxColor(1f, 0.95f, 0.8f),
            FxStyles.Slash => new FxColor(1f, 0.95f, 0.8f),
            FxStyles.Vibro => new FxColor(0.35f, 0.75f, 1f),
            FxStyles.PlasmaBlade => new FxColor(1f, 0.4f, 0.85f),
            FxStyles.ShockPush => new FxColor(0.435f, 0.906f, 1f),  // #2278 cyan #6fe7ff — a push of air
            FxStyles.EnergyFist => new FxColor(1f, 0.69f, 0.18f),   // #2278 gold #ffb02e — crackling energy
            FxStyles.Slug => new FxColor(0.95f, 0.82f, 0.5f),
            FxStyles.Rail => new FxColor(0.5f, 0.9f, 1f),
            FxStyles.Laser => new FxColor(1f, 0.42f, 0.36f),
            FxStyles.Plasma => new FxColor(0.92f, 0.45f, 1f),
            FxStyles.Drill => new FxColor(0.79f, 0.72f, 0.6f),
            FxStyles.DrillHot => new FxColor(1f, 0.6f, 0.24f),
            FxStyles.DrillCrystal => new FxColor(0.5f, 0.95f, 1f),
            FxStyles.MiningBeam => new FxColor(1f, 0.62f, 0.22f),
            FxStyles.Scan => new FxColor(0.37f, 0.85f, 1f),
            FxStyles.ScanPro => new FxColor(0.4f, 0.94f, 1f),
            FxStyles.Blueprint => new FxColor(0.3f, 0.65f, 1f),
            FxStyles.HealPulse => new FxColor(0.3f, 1f, 0.53f),
            FxStyles.Stasis => new FxColor(0.4f, 0.8f, 1f),
            FxStyles.Blast => new FxColor(1f, 0.54f, 0.2f),
            FxStyles.Pump => new FxColor(0.25f, 0.66f, 1f),
            FxStyles.TerrainScan => new FxColor(1f, 0.79f, 0.3f),
            FxStyles.Translate => new FxColor(0.7f, 0.53f, 1f),
            FxStyles.WeatherScan => new FxColor(0.56f, 0.83f, 1f),
            FxStyles.TwinPulse => new FxColor(0.37f, 0.95f, 1f),
            FxStyles.PlasmaBolt => new FxColor(1f, 0.6f, 0.24f),
            FxStyles.HeavyBeam => new FxColor(1f, 0.31f, 0.85f),
            FxStyles.DrillBeam => new FxColor(1f, 0.7f, 0.28f),
            FxStyles.Tractor => new FxColor(0.44f, 0.89f, 1f),
            FxStyles.PlanetScan => new FxColor(0.4f, 0.94f, 1f),
            FxStyles.Shield => new FxColor(0.35f, 0.78f, 1f),
            FxStyles.Warp => new FxColor(0.62f, 0.72f, 1f),
            _ => new FxColor(0.62f, 0.86f, 1f), // generic
        };

        private static ResolvedFx FromData(FxDefinition fx)
        {
            var color = TryParseHex(fx.Color, out var c) ? c : DefaultColor(fx.Style);
            var color2 = TryParseHex(fx.Color2, out var c2) ? c2 : color.Lighten(DefaultCoreLighten);
            float charge = Finite(fx.Charge) ? Clamp(fx.Charge, 0f, MaxCharge) : 0f;
            float size = Finite(fx.Size) && fx.Size > 0f ? Clamp(fx.Size, MinSize, MaxSize) : 1f;
            float speed = Finite(fx.Speed) && fx.Speed > 0f ? fx.Speed : 0f;
            return new ResolvedFx(fx.Style, color, color2, charge, size, speed, fromData: true);
        }

        private static ResolvedFx Heuristic(string style)
        {
            var color = DefaultColor(style);
            return new ResolvedFx(style, color, color.Lighten(DefaultCoreLighten), 0f, 1f, 0f, fromData: false);
        }

        /// <summary>The pre-overhaul guesses: weapons by their key (gauss/rail, slug/scrap, plasma blade, plasma,
        /// laser), then by reach; drills, scanners and gadgets by their kind. Key guesses apply to weapons only — a
        /// gadget called <c>rail_linker</c> is not a rail gun.</summary>
        private static string StyleForTool(string? itemKey, ToolProperties? tool)
        {
            if (string.IsNullOrEmpty(itemKey) || tool == null)
            {
                return FxStyles.Fist;
            }

            string key = (itemKey ?? string.Empty).ToLowerInvariant();
            switch (tool.Kind)
            {
                case ToolKind.Drill:
                    return key.Contains("beam", StringComparison.Ordinal) ? FxStyles.MiningBeam : FxStyles.Drill;
                case ToolKind.Scanner:
                    return FxStyles.Scan;
                case ToolKind.Weapon:
                    break;
                default:
                    return FxStyles.Generic; // gadgets, placers, repair tools
            }

            if (key.Contains("gauss", StringComparison.Ordinal) || key.Contains("rail", StringComparison.Ordinal))
            {
                return FxStyles.Rail;
            }

            if (key.Contains("slug", StringComparison.Ordinal) || key.Contains("scrap", StringComparison.Ordinal))
            {
                return FxStyles.Slug;
            }

            if (key.Contains("plasma", StringComparison.Ordinal))
            {
                return key.Contains("sword", StringComparison.Ordinal) || key.Contains("blade", StringComparison.Ordinal) ? FxStyles.PlasmaBlade : FxStyles.Plasma;
            }

            if (key.Contains("laser", StringComparison.Ordinal))
            {
                return FxStyles.Laser;
            }

            return tool.Range > 6f ? FxStyles.Laser : FxStyles.Slash;
        }

        /// <summary>A module without fx data: the asteroid breaker class (weapon_class 0) drills, the laser cannon is a
        /// heavy beam, any other cannon a plasma bolt, everything else the twin pulse.</summary>
        private static string StyleForModule(string? moduleKey, ShipModuleDefinition? def)
        {
            if (def != null && def.Stats.TryGetValue("weapon_class", out double weaponClass) && weaponClass == 0)
            {
                return FxStyles.DrillBeam;
            }

            string key = (moduleKey ?? def?.Key ?? string.Empty).ToLowerInvariant();
            if (key.Contains("laser_cannon", StringComparison.Ordinal))
            {
                return FxStyles.HeavyBeam;
            }

            return key.Contains("cannon", StringComparison.Ordinal) ? FxStyles.PlasmaBolt : FxStyles.TwinPulse;
        }

        private static int HexDigit(char ch)
        {
            if (ch >= '0' && ch <= '9')
            {
                return ch - '0';
            }

            if (ch >= 'a' && ch <= 'f')
            {
                return ch - 'a' + 10;
            }

            if (ch >= 'A' && ch <= 'F')
            {
                return ch - 'A' + 10;
            }

            return -1;
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
