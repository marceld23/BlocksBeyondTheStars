// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Text;

namespace BlocksBeyondTheStars.Shared.Story;

/// <summary>The forms a <see cref="StoryCondition"/> node can take (#2465).</summary>
public enum StoryConditionKind
{
    /// <summary><c>step:&lt;pack&gt;:&lt;step&gt;</c> — that step of a player pack is done or skipped.</summary>
    Step,

    /// <summary><c>chapter:&lt;pack&gt;:&lt;chapter&gt;</c> — that chapter is done or skipped.</summary>
    Chapter,

    /// <summary><c>pack:&lt;pack&gt;:&lt;started|done|enabled|optedout&gt;</c> — a per-player pack state.</summary>
    Pack,

    /// <summary><c>blueprint:&lt;key&gt;</c> — the player owns that blueprint.</summary>
    Blueprint,

    /// <summary><c>world:&lt;predicate&gt;</c> — a live fact about the world or the player's place in it
    /// (<see cref="StoryWorldPredicates"/>).</summary>
    World,

    /// <summary><c>lore:&lt;key&gt;</c> — the player has read that lore text (a <c>lore_sites.json</c> entry, #2476).</summary>
    Lore,

    /// <summary><c>all:[a,b,…]</c> — every child holds.</summary>
    All,

    /// <summary><c>any:[a,b,…]</c> — at least one child holds.</summary>
    Any,

    /// <summary><c>not:&lt;condition&gt;</c> — the child does not hold.</summary>
    Not,
}

/// <summary>The per-player pack states a <c>pack:</c> condition can ask about.</summary>
public enum StoryPackState
{
    /// <summary>The pack has started for this player (<c>story:&lt;pack&gt;:started</c>).</summary>
    Started,

    /// <summary>The player finished the pack (<c>story:&lt;pack&gt;:done</c>).</summary>
    Done,

    /// <summary>The pack runs for this player in this world (the world story, or an enabled player pack).</summary>
    Enabled,

    /// <summary>The player switched the pack off (<c>story:&lt;pack&gt;:optout</c>).</summary>
    OptedOut,
}

/// <summary>What a condition is evaluated against: one player's milestones and the world they are in. The server's
/// implementation answers from <c>PlayerState.Milestones</c> (<see cref="StoryPackKeys"/>) and its world-predicate
/// registry; tests plug in a dictionary.</summary>
public interface IStoryConditionContext
{
    /// <summary>Whether the step <paramref name="stepId"/> (e.g. <c>b2.2</c>) of <paramref name="packId"/> is done or skipped.</summary>
    bool IsStepDone(string packId, string stepId);

    /// <summary>Whether the chapter <paramref name="chapterId"/> of <paramref name="packId"/> is done or skipped.</summary>
    bool IsChapterDone(string packId, string chapterId);

    /// <summary>Whether <paramref name="packId"/> is in <paramref name="state"/> for this player.</summary>
    bool IsPackInState(string packId, StoryPackState state);

    /// <summary>Whether the player owns the blueprint <paramref name="blueprintKey"/>.</summary>
    bool HasBlueprint(string blueprintKey);

    /// <summary>The value of the world predicate <paramref name="predicate"/> (one of <see cref="StoryWorldPredicates.All"/>).</summary>
    bool IsWorldPredicateTrue(string predicate);

    /// <summary>Whether the player has read the lore text <paramref name="loreKey"/> (#2476: the <c>lore:&lt;key&gt;</c> milestone the
    /// reader writes).</summary>
    bool HasReadLore(string loreKey);
}

/// <summary>
/// One parsed condition of a story pack (#2465): the small server-evaluated grammar behind a step's <c>requires</c> /
/// <c>feasibleIf</c> / <c>alreadyDoneIf</c>, a chapter's <c>offerWhen</c>, a pack's <c>start.after</c> and its
/// <c>gates</c>. Parse with <see cref="StoryConditionParser"/>; evaluation is pure — everything it reads comes through
/// the <see cref="IStoryConditionContext"/>.
/// <para>Grammar (no spaces inside an atom; spaces around <c>[</c>, <c>,</c>, <c>]</c> are fine):</para>
/// <code>
/// condition := atom | "all:[" list "]" | "any:[" list "]" | "not:" condition
/// list      := condition ("," condition)*
/// atom      := "step:" pack ":" step | "chapter:" pack ":" chapter
///            | "pack:" pack ":" ("started" | "done" | "enabled" | "optedout")
///            | "blueprint:" key | "world:" predicate | "lore:" key
/// </code>
/// Ids are letters, digits, <c>_</c>, <c>.</c> and <c>-</c> (a step id is <c>b2.2</c>). <c>ui:*</c> events are never a
/// condition (they are client claims, synthesis §5.5).
/// </summary>
public sealed class StoryCondition
{
    private static readonly IReadOnlyList<StoryCondition> NoChildren = Array.Empty<StoryCondition>();

    private StoryCondition(StoryConditionKind kind, string packId, string id, StoryPackState packState, IReadOnlyList<StoryCondition> children)
    {
        Kind = kind;
        PackId = packId;
        Id = id;
        PackState = packState;
        Children = children;
    }

    /// <summary>The node's form.</summary>
    public StoryConditionKind Kind { get; }

    /// <summary>The pack of a <c>step:</c>, <c>chapter:</c> or <c>pack:</c> atom; empty otherwise.</summary>
    public string PackId { get; }

    /// <summary>The step id, chapter id, blueprint key or world predicate of an atom; empty for combinators and
    /// <c>pack:</c> atoms.</summary>
    public string Id { get; }

    /// <summary>The asked state of a <c>pack:</c> atom.</summary>
    public StoryPackState PackState { get; }

    /// <summary>The operands of <c>all</c> / <c>any</c> (one or more) and <c>not</c> (exactly one); empty for atoms.</summary>
    public IReadOnlyList<StoryCondition> Children { get; }

    /// <summary>True for the six atom kinds.</summary>
    public bool IsAtom => Kind is not (StoryConditionKind.All or StoryConditionKind.Any or StoryConditionKind.Not);

    internal static StoryCondition Atom(StoryConditionKind kind, string packId, string id, StoryPackState state = StoryPackState.Started)
        => new(kind, packId, id, state, NoChildren);

    internal static StoryCondition Group(StoryConditionKind kind, IReadOnlyList<StoryCondition> children)
        => new(kind, string.Empty, string.Empty, StoryPackState.Started, children);

    /// <summary>Evaluates the condition for one player. <c>all</c>/<c>any</c> short-circuit left to right.</summary>
    public bool Evaluate(IStoryConditionContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        switch (Kind)
        {
            case StoryConditionKind.Step:
                return context.IsStepDone(PackId, Id);
            case StoryConditionKind.Chapter:
                return context.IsChapterDone(PackId, Id);
            case StoryConditionKind.Pack:
                return context.IsPackInState(PackId, PackState);
            case StoryConditionKind.Blueprint:
                return context.HasBlueprint(Id);
            case StoryConditionKind.World:
                return context.IsWorldPredicateTrue(Id);
            case StoryConditionKind.Lore:
                return context.HasReadLore(Id);
            case StoryConditionKind.All:
                foreach (var child in Children)
                {
                    if (!child.Evaluate(context))
                    {
                        return false;
                    }
                }

                return true;
            case StoryConditionKind.Any:
                foreach (var child in Children)
                {
                    if (child.Evaluate(context))
                    {
                        return true;
                    }
                }

                return false;
            case StoryConditionKind.Not:
                return !Children[0].Evaluate(context);
            default:
                return false;
        }
    }

    /// <summary>Every atom in this condition, depth first — for the pack validator (do the named packs, steps,
    /// chapters and blueprints exist?).</summary>
    public IEnumerable<StoryCondition> Atoms()
    {
        if (IsAtom)
        {
            yield return this;
            yield break;
        }

        foreach (var child in Children)
        {
            foreach (var atom in child.Atoms())
            {
                yield return atom;
            }
        }
    }

    /// <summary>The canonical text: the grammar above without optional spaces (parsing it gives an equal tree).</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        Write(sb);
        return sb.ToString();
    }

    private void Write(StringBuilder sb)
    {
        switch (Kind)
        {
            case StoryConditionKind.Step:
                sb.Append("step:").Append(PackId).Append(':').Append(Id);
                break;
            case StoryConditionKind.Chapter:
                sb.Append("chapter:").Append(PackId).Append(':').Append(Id);
                break;
            case StoryConditionKind.Pack:
                sb.Append("pack:").Append(PackId).Append(':').Append(StoryConditionParser.PackStateName(PackState));
                break;
            case StoryConditionKind.Blueprint:
                sb.Append("blueprint:").Append(Id);
                break;
            case StoryConditionKind.World:
                sb.Append("world:").Append(Id);
                break;
            case StoryConditionKind.Lore:
                sb.Append("lore:").Append(Id);
                break;
            case StoryConditionKind.Not:
                sb.Append("not:");
                Children[0].Write(sb);
                break;
            default:
                sb.Append(Kind == StoryConditionKind.All ? "all:[" : "any:[");
                for (int i = 0; i < Children.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    Children[i].Write(sb);
                }

                sb.Append(']');
                break;
        }
    }
}

/// <summary>A condition string that does not follow the grammar of <see cref="StoryCondition"/>. The message names the
/// problem and the character position.</summary>
public sealed class StoryConditionException : FormatException
{
    public StoryConditionException(string message, int position)
        : base(message)
    {
        Position = position;
    }

    public StoryConditionException()
    {
    }

    public StoryConditionException(string message)
        : base(message)
    {
    }

    public StoryConditionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The 0-based character position the parser stopped at.</summary>
    public int Position { get; }
}

/// <summary>Parses story-pack condition strings into <see cref="StoryCondition"/> trees (#2465). Unknown forms, unknown
/// world predicates, reserved pack ids, <c>ui:</c> claims and empty groups are content errors.</summary>
public static class StoryConditionParser
{
    /// <summary>Deepest allowed nesting of <c>all</c>/<c>any</c>/<c>not</c> — a guard, real conditions nest two levels.</summary>
    public const int MaxDepth = 16;

    /// <summary>Parses one condition. <paramref name="worldPredicates"/> is the set of known <c>world:</c> names
    /// (default <see cref="StoryWorldPredicates.All"/>).</summary>
    /// <exception cref="StoryConditionException">The text is not a valid condition.</exception>
    public static StoryCondition Parse(string text, IReadOnlyCollection<string>? worldPredicates = null)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        var reader = new Reader(text, worldPredicates ?? StoryWorldPredicates.All);
        reader.SkipSpaces();
        var condition = reader.ReadCondition(0);
        reader.SkipSpaces();
        if (!reader.AtEnd)
        {
            throw reader.Error("unexpected text after the condition");
        }

        return condition;
    }

    /// <summary>Like <see cref="Parse"/>, without throwing: false with the reason in <paramref name="error"/>.</summary>
    public static bool TryParse(string text, out StoryCondition? condition, out string error, IReadOnlyCollection<string>? worldPredicates = null)
    {
        try
        {
            condition = Parse(text, worldPredicates);
            error = string.Empty;
            return true;
        }
        catch (StoryConditionException ex)
        {
            condition = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Parses a JSON condition list (<c>"feasibleIf": ["a", "b"]</c>): every entry must hold, so the list is
    /// one <c>all</c> — or the single condition itself. An empty list is null ("no condition": always true).</summary>
    /// <exception cref="StoryConditionException">An entry is not a valid condition.</exception>
    public static StoryCondition? ParseList(IEnumerable<string>? conditions, IReadOnlyCollection<string>? worldPredicates = null)
    {
        if (conditions is null)
        {
            return null;
        }

        var parsed = new List<StoryCondition>();
        foreach (string text in conditions)
        {
            parsed.Add(Parse(text, worldPredicates));
        }

        return parsed.Count switch
        {
            0 => null,
            1 => parsed[0],
            _ => StoryCondition.Group(StoryConditionKind.All, parsed),
        };
    }

    /// <summary>Evaluates an optional condition: a missing one (null) always holds.</summary>
    public static bool Holds(StoryCondition? condition, IStoryConditionContext context)
        => condition is null || condition.Evaluate(context);

    /// <summary>The grammar word of a pack state (<c>started</c>, <c>done</c>, <c>enabled</c>, <c>optedout</c>).</summary>
    public static string PackStateName(StoryPackState state) => state switch
    {
        StoryPackState.Done => "done",
        StoryPackState.Enabled => "enabled",
        StoryPackState.OptedOut => "optedout",
        _ => "started",
    };

    private static bool TryPackState(string word, out StoryPackState state)
    {
        StoryPackState? parsed = word switch
        {
            "started" => StoryPackState.Started,
            "done" => StoryPackState.Done,
            "enabled" => StoryPackState.Enabled,
            "optedout" => StoryPackState.OptedOut,
            _ => null,
        };
        state = parsed ?? StoryPackState.Started;
        return parsed.HasValue;
    }

    private static bool IsIdChar(char c)
        => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '.' or '-';

    /// <summary>A cursor over one condition string.</summary>
    private sealed class Reader
    {
        private readonly string _text;
        private readonly IReadOnlyCollection<string> _predicates;
        private int _pos;

        public Reader(string text, IReadOnlyCollection<string> predicates)
        {
            _text = text;
            _predicates = predicates;
        }

        public bool AtEnd => _pos >= _text.Length;

        public StoryConditionException Error(string what)
            => new($"Story condition \"{_text}\": {what} (at position {_pos}).", _pos);

        public void SkipSpaces()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
            {
                _pos++;
            }
        }

        public StoryCondition ReadCondition(int depth)
        {
            if (depth > MaxDepth)
            {
                throw Error($"nested deeper than {MaxDepth} levels");
            }

            int start = _pos;
            string word = ReadId();
            if (word.Length == 0)
            {
                throw Error(AtEnd ? "a condition is missing" : $"unexpected '{_text[_pos]}'");
            }

            Expect(':', $"'{word}' must be followed by ':'");
            switch (word)
            {
                case "all":
                    return StoryCondition.Group(StoryConditionKind.All, ReadList(depth));
                case "any":
                    return StoryCondition.Group(StoryConditionKind.Any, ReadList(depth));
                case "not":
                    return StoryCondition.Group(StoryConditionKind.Not, new[] { ReadCondition(depth + 1) });
                case "step":
                case "chapter":
                    {
                        string pack = ReadPackId(word);
                        Expect(':', $"'{word}:{pack}' needs ':<{word}>'");
                        string id = ReadRequiredId($"the {word} id");
                        return StoryCondition.Atom(word == "step" ? StoryConditionKind.Step : StoryConditionKind.Chapter, pack, id);
                    }

                case "pack":
                    {
                        string pack = ReadPackId(word);
                        Expect(':', $"'pack:{pack}' needs ':started', ':done', ':enabled' or ':optedout'");
                        int at = _pos;
                        string stateWord = ReadId();
                        if (!TryPackState(stateWord, out var state))
                        {
                            _pos = at;
                            throw Error($"unknown pack state '{stateWord}' (started, done, enabled, optedout)");
                        }

                        return StoryCondition.Atom(StoryConditionKind.Pack, pack, string.Empty, state);
                    }

                case "blueprint":
                    return StoryCondition.Atom(StoryConditionKind.Blueprint, string.Empty, ReadRequiredId("the blueprint key"));
                case "lore":
                    return StoryCondition.Atom(StoryConditionKind.Lore, string.Empty, ReadRequiredId("the lore key"));
                case "world":
                    {
                        int at = _pos;
                        string predicate = ReadRequiredId("the world predicate");
                        if (!Contains(_predicates, predicate))
                        {
                            _pos = at;
                            throw Error($"unknown world predicate '{predicate}' (known: {string.Join(", ", _predicates)})");
                        }

                        return StoryCondition.Atom(StoryConditionKind.World, string.Empty, predicate);
                    }

                case "ui":
                    _pos = start;
                    throw Error("'ui:' events are client claims and never a condition");
                default:
                    _pos = start;
                    throw Error($"unknown condition '{word}:' (step, chapter, pack, blueprint, world, lore, all, any, not)");
            }
        }

        private List<StoryCondition> ReadList(int depth)
        {
            SkipSpaces();
            Expect('[', "a group needs '[' … ']'");
            var children = new List<StoryCondition>();
            SkipSpaces();
            if (Peek(']'))
            {
                throw Error("an empty group never says what it waits for");
            }

            while (true)
            {
                SkipSpaces();
                children.Add(ReadCondition(depth + 1));
                SkipSpaces();
                if (Peek(','))
                {
                    _pos++;
                    continue;
                }

                Expect(']', "expected ',' or ']'");
                return children;
            }
        }

        private string ReadPackId(string form)
        {
            int at = _pos;
            string pack = ReadRequiredId($"the pack id of '{form}:'");
            if (StoryRegistry.IsReservedPackId(pack))
            {
                _pos = at;
                throw Error($"'{pack}' is a reserved pack id");
            }

            return pack;
        }

        private string ReadRequiredId(string what)
        {
            string id = ReadId();
            if (id.Length == 0)
            {
                throw Error($"{what} is missing");
            }

            return id;
        }

        private string ReadId()
        {
            int start = _pos;
            while (_pos < _text.Length && IsIdChar(_text[_pos]))
            {
                _pos++;
            }

            return _text.Substring(start, _pos - start);
        }

        private bool Peek(char c) => _pos < _text.Length && _text[_pos] == c;

        private void Expect(char c, string what)
        {
            if (!Peek(c))
            {
                throw Error(what);
            }

            _pos++;
        }

        private static bool Contains(IReadOnlyCollection<string> names, string name)
        {
            foreach (string n in names)
            {
                if (string.Equals(n, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

/// <summary>
/// The <c>world:</c> predicates a story condition may name (#2465). The server evaluates each one for the player
/// (<c>GameServer.WorldPredicate</c>); a test keeps this list and the server's registry identical, so a new predicate is
/// one name here plus one line there. Rule predicates read the save's world rules; place predicates read the player's
/// current body or system.
/// </summary>
public static class StoryWorldPredicates
{
    /// <summary>A boardable space station orbits in the player's system (the dock lesson's feasibility, #1924).</summary>
    public const string StationInSystem = "station_in_system";

    /// <summary>The player's body has a settlement.</summary>
    public const string SettlementReachable = "settlement_reachable";

    /// <summary>The player's body has no breathable air (or is a player-built station deck).</summary>
    public const string Airless = "airless";

    /// <summary>A story net fragment lies on the player's body.</summary>
    public const string FragmentOnWorld = "fragment_on_world";

    /// <summary>Another (visible) player is within 32 blocks on the same body.</summary>
    public const string PlayerNear = "player_near";

    /// <summary>Planet enemies (Guardian machines) are switched on.</summary>
    public const string Enemies = "enemies";

    /// <summary>Hostile machines in space are switched on (space combat PvE with drones or UFOs).</summary>
    public const string SpaceEnemies = "space_enemies";

    /// <summary>Bandits are switched on (survival only).</summary>
    public const string Bandits = "bandits";

    /// <summary>Free space flight is allowed (launch, dock, land).</summary>
    public const string FreeFlight = "free_flight";

    /// <summary>Hunger drains (food matters).</summary>
    public const string Hunger = "hunger";

    /// <summary>Oxygen drains (air matters).</summary>
    public const string Oxygen = "oxygen";

    /// <summary>Environmental hazards are on.</summary>
    public const string Hazards = "hazards";

    /// <summary>Crafting costs nothing for this player (Creative, incl. a per-player mode override).</summary>
    public const string Creative = "creative";

    /// <summary>The Creative flight rule is on (double jump flies).</summary>
    public const string CreativeFlight = "creative_flight";

    /// <summary>The Instant Travel rule is on (the map reaches unvisited worlds).</summary>
    public const string InstantTravel = "instant_travel";

    /// <summary>The pause menu offers "Back to my ship".</summary>
    public const string ReturnToShip = "return_to_ship";

    /// <summary>Players get the starter teleporter.</summary>
    public const string StarterTeleporter = "starter_teleporter";

    /// <summary>Visitors (settlers, scouts, robbers) come to player bases.</summary>
    public const string BaseVisitors = "base_visitors";

    /// <summary>The world generates space stations.</summary>
    public const string SpaceStations = "space_stations";

    /// <summary>A world-shared story (VEGA) is active — false on a "none" sandbox.</summary>
    public const string StoryActive = "story_active";

    /// <summary>#2469: a plant whose harvest is food (berries …) grows within a short walk of the player — the berry
    /// lesson's feasibility (a bounded probe of the loaded blocks, evaluated when a step activates).</summary>
    public const string FoodPlantsNear = "food_plants_near";

    /// <summary>#2469: a tree (a block that drops wood) stands within a short walk of the player — the campfire lesson.</summary>
    public const string TreesNear = "trees_near";

    /// <summary>#2469: the player carries a shaped block (stairs, slopes …) the rotate key can turn.</summary>
    public const string ShapeBlockCarried = "shape_block_carried";

    /// <summary>#2469: the player's knowledge already covers a blueprint they could research now (prerequisites met, no
    /// data fragment needed).</summary>
    public const string ResearchReady = "research_ready";

    /// <summary>#2469: this world hands every blueprint out (Creative mode or the head-start option) — nothing to research.</summary>
    public const string AllBlueprints = "all_blueprints";

    /// <summary>#2469: the player's system has a planet, moon or asteroid they have never landed on — the landing lesson.</summary>
    public const string NewWorldInSystem = "new_world_in_system";

    /// <summary>#2476: the world story still has a net fragment nobody has found (a fragment mission can be done).</summary>
    public const string FragmentsLeft = "fragments_left";

    /// <summary>#2476: the Sealed Observatory exists in this save (a world stamped before the unique sites skipped it) and the
    /// player can get there (same system, a jump generator, a landing there before, instant travel) — the lead's rule.</summary>
    public const string ObservatoryReachable = "observatory_reachable";

    /// <summary>#2476: the derelict <i>The Long Quiet</i> exists and the player can get to it — the lead's rule.</summary>
    public const string DerelictReachable = "derelict_reachable";

    /// <summary>#2476: the world generates settlements (the world option is not off).</summary>
    public const string Settlements = "settlements";

    /// <summary>#2476: the world generates buried vaults (the world option is not off).</summary>
    public const string Vaults = "vaults";

    /// <summary>#2476: the world generates wrecks drifting in space (the world option is not off).</summary>
    public const string SpaceWrecks = "space_wrecks";

    /// <summary>Every known predicate, in documentation order.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        StationInSystem, SettlementReachable, Airless, FragmentOnWorld, PlayerNear,
        Enemies, SpaceEnemies, Bandits, FreeFlight, Hunger, Oxygen, Hazards, Creative, CreativeFlight,
        InstantTravel, ReturnToShip, StarterTeleporter, BaseVisitors, SpaceStations, StoryActive,
        FoodPlantsNear, TreesNear, ShapeBlockCarried, ResearchReady, AllBlueprints, NewWorldInSystem,
        FragmentsLeft, ObservatoryReachable, DerelictReachable, Settlements, Vaults, SpaceWrecks,
    };
}
