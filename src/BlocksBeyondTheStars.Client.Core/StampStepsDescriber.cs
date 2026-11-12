// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.WorldGeneration;
using BlocksBeyondTheStars.WorldGeneration.Sources;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One argument of a step's text: a plain value, or a locale key the editor translates (a block, a profile …).</summary>
    public readonly struct StampArg
    {
        private StampArg(string text, bool isKey)
        {
            Text = text ?? string.Empty;
            IsKey = isKey;
        }

        public string Text { get; }

        /// <summary>True when <see cref="Text"/> is a locale key.</summary>
        public bool IsKey { get; }

        public static StampArg Of(string text) => new StampArg(text, false);

        public static StampArg Of(int value) => new StampArg(value.ToString(CultureInfo.InvariantCulture), false);

        public static StampArg Key(string key) => new StampArg(key, true);

        public override string ToString() => IsKey ? "@" + Text : Text;
    }

    /// <summary>One step of the stamp: its title key, its text key and the text's arguments (<c>{0}</c> …).</summary>
    public class StampStep
    {
        public StampStep(string title, string text, params StampArg[] args)
        {
            Title = title;
            Text = text;
            Args = args;
        }

        public string Title { get; }

        public string Text { get; }

        public IReadOnlyList<StampArg> Args { get; }

        public override string ToString() => Text + "(" + string.Join(", ", Args) + ")";
    }

    /// <summary>
    /// "What happens when it is stamped" (#3523): the stamp-time steps that apply to a design of the structure editor, in the
    /// order the server runs them — what builds it (a generator entry), where and how it is seated, its material tokens, the
    /// furniture, the quarter turn, the ruin pass, its chests, sounds and dens, who lives there and how far other structures
    /// stay away. The rules mirror the server's code paths on a world of terrain generation 38 (<see cref="StructureStampRules"/>
    /// for what each target reads); the values come from the design and the content (the token resolvers, the placement rules
    /// of <c>data/structure_types.json</c>). Unity-free so the headless suite checks it; the editor only translates and draws.
    /// </summary>
    public static class StampStepsDescriber
    {
        public const string TitleBuild = "ui.stamp.title.build";
        public const string TitleSeat = "ui.stamp.title.seat";
        public const string TitleTokens = "ui.stamp.title.tokens";
        public const string TitleFurnish = "ui.stamp.title.furnish";
        public const string TitleTurn = "ui.stamp.title.turn";
        public const string TitleRuin = "ui.stamp.title.ruin";
        public const string TitleChests = "ui.stamp.title.chests";
        public const string TitleLife = "ui.stamp.title.life";
        public const string TitleResidents = "ui.stamp.title.residents";
        public const string TitleKeepClear = "ui.stamp.title.keep_clear";

        /// <summary>The loot table of a landmark chest that names neither items nor a table (the server's
        /// <c>GameServer.LandmarkDefaultLootTable</c>).</summary>
        public const string DefaultLootTable = "ruin_common";

        /// <summary>The chests listed one by one; the rest are counted.</summary>
        public const int ListedChests = 6;

        /// <summary>The container markers an underground structure turns into loot (the server's <c>UndergroundLootMarkers</c>).</summary>
        private static readonly string[] UndergroundContainers = { "loot", LandmarkRules.ChestMarker, "data_terminal", "relic_cache" };

        /// <summary>The steps of <paramref name="d"/>, in stamp order.</summary>
        public static List<StampStep> Describe(StampDesign d, GameContent content)
        {
            if (d is null)
            {
                throw new ArgumentNullException(nameof(d));
            }

            if (content is null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            var steps = new List<StampStep>();
            if (StructureStampRules.IsEntry(d.Target))
            {
                steps.Add(new StampStep(TitleBuild, "ui.stamp.build.entry", StampArg.Key(StructureSources.LabelKeyPrefix + d.Source),
                    StampArg.Of(Math.Max(1, d.Weight))));
            }

            steps.Add(Seat(d));
            AddIfAny(steps, Tokens(d, content));
            AddIfAny(steps, Furnish(d));
            steps.Add(Turn(d.Target));
            steps.Add(Ruin(d));
            AddIfAny(steps, Chests(d));
            AddIfAny(steps, Life(d));
            steps.Add(Residents(d));
            AddIfAny(steps, KeepClear(d.Target, content));
            return steps;
        }

        private static void AddIfAny(List<StampStep> steps, StampStep? step)
        {
            if (step != null)
            {
                steps.Add(step);
            }
        }

        // ---------------------------------------------------------------- seat

        private static StampStep Seat(StampDesign d)
        {
            string text = d.Target switch
            {
                StampTarget.SettlementWhole or StampTarget.SettlementEntry => "ui.stamp.seat.settlement",
                StampTarget.SettlementModule => "ui.stamp.seat.module",
                StampTarget.CityModule => "ui.stamp.seat.city_module",
                StampTarget.UndergroundWhole or StampTarget.UndergroundEntry => "ui.stamp.seat.underground",
                StampTarget.StationModule => "ui.stamp.seat.station_module",
                StampTarget.StationWhole or StampTarget.StationEntry => "ui.stamp.seat.space",
                StampTarget.Landmark when d.GroundRow > 0 => "ui.stamp.seat.landmark",
                _ => "ui.stamp.seat.landmark_flat",
            };
            return new StampStep(TitleSeat, text, StampArg.Of(d.GroundRow));
        }

        // ---------------------------------------------------------------- material tokens

        /// <summary>The token step: what the five tokens resolve to for the design's target, surface and style — or, for a
        /// target that resolves nothing, that its token cells would be air. Null for a generator entry (no cells).</summary>
        private static StampStep? Tokens(StampDesign d, GameContent content)
        {
            if (StructureStampRules.IsEntry(d.Target))
            {
                return null;
            }

            if (!StructureStampRules.ResolvesTokens(d.Target))
            {
                return d.TokenCount > 0 ? new StampStep(TitleTokens, "ui.stamp.tokens.air", StampArg.Of(d.TokenCount)) : null;
            }

            var materials = MaterialsFor(d, content);
            string Block(string token) => BlockName(content, materials.Resolve(token));
            var args = new List<StampArg> { StampArg.Of(d.TokenCount) };
            bool bySurface = d.Target is not (StampTarget.StationWhole or StampTarget.CityModule);
            if (bySurface)
            {
                args.Add(StampArg.Key(BlockNameKey(d.Surface)));
            }

            foreach (string token in MaterialTokens.All)
            {
                args.Add(StampArg.Key(Block(token)));
            }

            return new StampStep(TitleTokens, bySurface ? "ui.stamp.tokens.resolved" : "ui.stamp.tokens.fixed", args.ToArray());
        }

        /// <summary>The resolver the server uses for the target: the station hull, the G.D.S. city's materials, a landmark's
        /// village style, else the settlement style of the tier (aliens for an alien module).</summary>
        public static ModuleMaterials MaterialsFor(StampDesign d, GameContent content) => d.Target switch
        {
            StampTarget.StationWhole => ModuleMaterials.ForStation(content),
            StampTarget.CityModule => ModuleMaterials.ForCity(content),
            StampTarget.Landmark => ModuleMaterials.ForSettlement("village", d.Surface, alien: false, content),
            StampTarget.SettlementModule => ModuleMaterials.ForSettlement(d.Tier, d.Surface, d.Alien, content),
            _ => ModuleMaterials.ForSettlement(d.Tier, d.Surface, alien: false, content),
        };

        private static string BlockName(GameContent content, ushort id)
            => id == 0 ? "ui.stamp.air" : BlockNameKey(content.BlockById(new BlockId(id))?.Key ?? string.Empty);

        private static string BlockNameKey(string key) => string.IsNullOrEmpty(key) ? "ui.stamp.air" : "block." + key + ".name";

        // ---------------------------------------------------------------- furnishing

        private static StampStep? Furnish(StampDesign d)
        {
            int rooms = d.CountMarkers("room");
            switch (d.Target)
            {
                case StampTarget.SettlementWhole:
                case StampTarget.UndergroundWhole:
                case StampTarget.SettlementModule:
                case StampTarget.CityModule:
                    return rooms > 0
                        ? new StampStep(TitleFurnish, "ui.stamp.furnish.rooms", StampArg.Of(rooms), StampArg.Key(FurnishStyle(d)))
                        : new StampStep(TitleFurnish, "ui.stamp.furnish.none");
                case StampTarget.StationModule:
                    return new StampStep(TitleFurnish, "ui.stamp.furnish.station_module",
                        StampArg.Key("ui.function." + (string.IsNullOrEmpty(d.Function) ? StructureRoles.Room : d.Function)));
                case StampTarget.StationWhole:
                case StampTarget.Landmark:
                    return new StampStep(TitleFurnish, "ui.stamp.furnish.never");
                default:
                    return null; // a generator entry furnishes as its generator does
            }
        }

        private static string FurnishStyle(StampDesign d)
        {
            if (d.Target == StampTarget.CityModule)
            {
                return "ui.stamp.style.town";
            }

            if (d.Target == StampTarget.SettlementModule && d.Alien)
            {
                return "ui.stamp.style.alien";
            }

            return StructureRoles.IsTownStyleTier(d.Tier) ? "ui.stamp.style.town" : "ui.stamp.style.village";
        }

        // ---------------------------------------------------------------- the quarter turn

        private static StampStep Turn(StampTarget t) => new StampStep(TitleTurn, t switch
        {
            StampTarget.SettlementWhole or StampTarget.Landmark => "ui.stamp.turn.pinned",
            StampTarget.StationModule => "ui.stamp.turn.dock",
            _ => "ui.stamp.turn.never",
        });

        // ---------------------------------------------------------------- the ruin pass

        private static StampStep Ruin(StampDesign d)
        {
            switch (d.Target)
            {
                case StampTarget.SettlementWhole:
                    {
                        var (profile, strength) = Profile(d);
                        return new StampStep(TitleRuin, "ui.stamp.ruin.rolled", StampArg.Key(profile), StampArg.Of(strength), StampArg.Of(d.KeepCells));
                    }

                case StampTarget.SettlementModule:
                    return new StampStep(TitleRuin, "ui.stamp.ruin.module", StampArg.Of(d.KeepCells));
                case StampTarget.Landmark when d.Ruin:
                    {
                        var (profile, strength) = Profile(d);
                        return new StampStep(TitleRuin, "ui.stamp.ruin.always", StampArg.Key(profile), StampArg.Of(strength), StampArg.Of(d.KeepCells));
                    }

                case StampTarget.SettlementEntry when d.Tier != StructureRoles.MetropolisTier:
                    return new StampStep(TitleRuin, "ui.stamp.ruin.entry",
                        StampArg.Of(Percent(DefaultStrength(LandmarkRules.ProfileMasonry))));
                default:
                    return new StampStep(TitleRuin, "ui.stamp.ruin.never");
            }
        }

        /// <summary>The ruin profile's name key and the strength in percent the server's pass runs with: the template's own
        /// (masonry and the profile's default when it names none — a whole settlement template from the editor names none).</summary>
        private static (string ProfileKey, int Percent) Profile(StampDesign d)
        {
            string profile = LandmarkRules.ProfileOf(new StructureTemplate { RuinProfile = d.RuinProfile ?? string.Empty });
            double strength = d.RuinStrength > 0 ? Math.Min(1.0, d.RuinStrength) : DefaultStrength(profile);
            return ("ui.ed.landmark.profile." + profile, Percent(strength));
        }

        /// <summary>The strength a ruin profile runs with when the template names none — the shared ruin pass's own default
        /// (<c>StructureRuiner.DefaultStrength</c>, internal to WorldGeneration): lattice work 0.4, everything else 0.5.</summary>
        public static double DefaultStrength(string profile) => profile == LandmarkRules.ProfileLattice ? 0.4 : 0.5;

        private static int Percent(double share) => (int)Math.Round(share * 100.0, MidpointRounding.AwayFromZero);

        // ---------------------------------------------------------------- chests

        private static StampStep? Chests(StampDesign d)
        {
            if (StructureStampRules.IsEntry(d.Target))
            {
                return null;
            }

            if (d.Target == StampTarget.UndergroundWhole)
            {
                int containers = d.CountMarkers(UndergroundContainers);
                return containers > 0 ? new StampStep(TitleChests, "ui.stamp.chest.underground", StampArg.Of(containers)) : null;
            }

            var chests = d.Markers.FindAll(m => m.Id == LandmarkRules.ChestMarker);
            if (chests.Count == 0)
            {
                return null;
            }

            if (!StructureStampRules.ReadsMarkerSettings(d.Target))
            {
                return new StampStep(TitleChests, "ui.stamp.chest.ignored", StampArg.Of(chests.Count));
            }

            return ChestList(d.Target, chests);
        }

        /// <summary>One line per chest (the first <see cref="ListedChests"/>): where, the chance, what it holds — and how many
        /// chests without settings stay furniture (outside a landmark a chest needs settings to become a loot chest).</summary>
        private static StampStep ChestList(StampTarget t, List<StampMarker> chests)
        {
            var lines = new List<StampStep>();
            int inert = 0;
            foreach (var c in chests)
            {
                var settings = MarkerSettings.Parse(c.Config);
                if (settings.IsEmpty && t != StampTarget.Landmark)
                {
                    inert++;
                    continue;
                }

                lines.Add(ChestLine(c, settings));
            }

            var args = new List<StampArg> { StampArg.Of(chests.Count - inert), StampArg.Of(inert) };
            var step = new StampStep(TitleChests, "ui.stamp.chest.summary", args.ToArray());
            return lines.Count == 0 ? step : new StampListStep(step, lines.GetRange(0, Math.Min(ListedChests, lines.Count)),
                Math.Max(0, lines.Count - ListedChests));
        }

        private static StampStep ChestLine(StampMarker c, MarkerSettings settings)
        {
            var at = StampArg.Of($"{c.X}, {c.Y}, {c.Z}");
            var chance = StampArg.Of(Percent(Math.Max(0.0, Math.Min(1.0, settings.GetDouble("chance", 1.0)))));
            string items = settings.Get("items");
            if (items.Trim().Length > 0)
            {
                return new StampStep(TitleChests, "ui.stamp.chest.items", at, chance, StampArg.Of(items));
            }

            string table = settings.Get("table", DefaultLootTable);
            return new StampStep(TitleChests, "ui.stamp.chest.table", at, chance, StampArg.Of(table.Length > 0 ? table : DefaultLootTable));
        }

        // ---------------------------------------------------------------- sounds and dens

        private static StampStep? Life(StampDesign d)
        {
            int sounds = d.CountMarkers(LandmarkRules.SoundMarker), dens = d.CountMarkers(LandmarkRules.DenMarker);
            if (sounds + dens == 0 || StructureStampRules.IsEntry(d.Target))
            {
                return null;
            }

            return StructureStampRules.ReadsMarkerSettings(d.Target)
                ? new StampStep(TitleLife, "ui.stamp.life.read", StampArg.Of(sounds), StampArg.Of(dens))
                : new StampStep(TitleLife, "ui.stamp.life.ignored", StampArg.Of(sounds + dens));
        }

        // ---------------------------------------------------------------- residents

        private static StampStep Residents(StampDesign d)
        {
            var (vendors, residents, posts) = People(d);
            string text = d.Target switch
            {
                StampTarget.SettlementWhole => "ui.stamp.residents.settlement",
                StampTarget.SettlementModule or StampTarget.CityModule => "ui.stamp.residents.module",
                StampTarget.StationWhole => "ui.stamp.residents.station",
                StampTarget.StationModule => "ui.stamp.residents.station_module",
                StampTarget.UndergroundWhole or StampTarget.UndergroundEntry => "ui.stamp.residents.underground",
                StampTarget.Landmark when d.Protected && !d.Ruin => "ui.stamp.residents.landmark_inhabited",
                StampTarget.Landmark => "ui.stamp.residents.landmark",
                StampTarget.StationEntry => "ui.stamp.residents.station_entry",
                _ => "ui.stamp.residents.entry",
            };
            return new StampStep(TitleResidents, text, StampArg.Of(vendors), StampArg.Of(residents), StampArg.Of(posts));
        }

        /// <summary>Vendors, residents (an <c>npc</c> or a station <c>cabin</c>) and posts (a mission board, a keeper's post, a
        /// guard, a profession) of the design.</summary>
        public static (int Vendors, int Residents, int Posts) People(StampDesign d)
        {
            int vendors = 0, residents = 0, posts = 0;
            foreach (var m in d.Markers)
            {
                if (m.Id == "vendor")
                {
                    vendors++;
                }
                else if (m.Id is "npc" or "cabin")
                {
                    residents++;
                }
                else if (m.Id is "mission_board" or "tavern" or "workshop" or "guard_post" || NpcProfessions.ByMarker(m.Id) != null)
                {
                    posts++;
                }
            }

            return (vendors, residents, posts);
        }

        // ---------------------------------------------------------------- keep-clear

        /// <summary>How far other structures stay away (the placement rules' <c>keepClear</c>, generation 38): a whole settlement,
        /// an underground structure, a landmark. A module goes with its settlement, a station hangs in space.</summary>
        private static StampStep? KeepClear(StampTarget t, GameContent content)
        {
            string? kind = t switch
            {
                StampTarget.SettlementWhole or StampTarget.SettlementEntry => StructureTypeRules.Settlement,
                StampTarget.UndergroundWhole or StampTarget.UndergroundEntry => StructureTypeRules.Underground,
                StampTarget.Landmark or StampTarget.LandmarkEntry => StructureTypeRules.Landmark,
                _ => null,
            };
            return kind is null ? null
                : new StampStep(TitleKeepClear, "ui.stamp.keep_clear", StampArg.Of(content.StructureTypes.Get(kind).KeepClear));
        }
    }

    /// <summary>A step with a list under it (the chests one by one) and how many more were left out.</summary>
    public sealed class StampListStep : StampStep
    {
        public StampListStep(StampStep head, IReadOnlyList<StampStep> lines, int more)
            : base(head.Title, head.Text, ToArray(head.Args))
        {
            Lines = lines;
            More = more;
        }

        public IReadOnlyList<StampStep> Lines { get; }

        /// <summary>Lines left out (<c>ui.stamp.chest.more</c>).</summary>
        public int More { get; }

        private static StampArg[] ToArray(IReadOnlyList<StampArg> args)
        {
            var a = new StampArg[args.Count];
            for (int i = 0; i < a.Length; i++)
            {
                a[i] = args[i];
            }

            return a;
        }
    }
}
