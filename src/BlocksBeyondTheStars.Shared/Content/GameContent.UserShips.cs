// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>
/// #3520: the user ships — the ship editor's own designs from <c>usercontent/ship_types/</c>. They are kept apart from the
/// shipped <see cref="Ships"/> (the wreck pools of existing worlds and the creative "all ships" grant enumerate that one, so a
/// new file must not change them) and resolved after the shipped ones by <see cref="GetShip"/> and <see cref="GetShipLayout"/>.
/// The consumers opt in by use (<see cref="UserShipUses"/>): raiders and trader flights take <c>npc</c>, new wrecks
/// <c>wreck</c>, the craft list <c>craft</c> only with the server's permission.
/// </summary>
public sealed partial class GameContent
{
    /// <summary>The ship editor's stat ranges (its steppers): a user ship's values are clamped into them.</summary>
    public const float UserShipHullMin = 20f, UserShipHullMax = 500f, UserShipShieldMax = 300f;

    /// <inheritdoc cref="UserShipHullMin"/>
    public const float UserShipFlightMin = 0.4f, UserShipFlightMax = 2.5f;

    /// <summary>The highest pool weight a user ship may ask for.</summary>
    public const int UserShipWeightMax = 100;

    /// <summary>Station tile → start module, as <c>tools/merge_ship.py</c> derives them (its <c>MODULE_FOR</c>).</summary>
    private static readonly (string Station, string Module)[] StationStartModules =
    {
        ("cockpit", "cockpit"), ("reactor", "reactor"), ("life_support", "life_support"), ("workshop", "workshop"),
        ("medbay", "medbay"), ("quarters", "quarters"), ("cargo", "cargo_hold_basic"),
    };

    private readonly Dictionary<string, UserShip> _userShipsByKey = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<UserShip> _userShips = Array.Empty<UserShip>();

    /// <summary>The loaded user ships in ordinal file-name order (the order the NPC pools append them in).</summary>
    public IReadOnlyList<UserShip> UserShips => _userShips;

    /// <summary>The user ship with this key (case-insensitive), or null — also null for every shipped key.</summary>
    public UserShip? UserShipByKey(string? key)
        => !string.IsNullOrEmpty(key) && _userShipsByKey.TryGetValue(key!, out var u) ? u : null;

    /// <summary>The user ships carrying <paramref name="use"/>, in load order — of the packs in <paramref name="enabledPacks"/>
    /// only (null or empty = every pack, like the structure pools).</summary>
    public IReadOnlyList<UserShip> UserShipsFor(string use, IReadOnlyCollection<string>? enabledPacks = null)
        => _userShips.Where(u => u.Has(use) && (enabledPacks is null || enabledPacks.Count == 0 || enabledPacks.Contains(u.Pack))).ToList();

    /// <summary>Ship-type keys the game gives a meaning of its own: the self-built ship (<see cref="ShipState.CustomShipType"/>)
    /// and the starter every fallback resolves to.</summary>
    private static readonly string[] ReservedShipTypeKeys = { ShipState.CustomShipType, "starter" };

    /// <summary>True when a user ship may not take <paramref name="key"/> (case-insensitive): a reserved ship-type key
    /// (<c>custom</c>, <c>starter</c>) or the key of a shipped ship or layout. A user ship keyed <c>custom</c> would otherwise
    /// resolve for every self-built ship.</summary>
    public bool IsReservedUserShipKey(string? key)
    {
        string k = (key ?? string.Empty).Trim();
        return ReservedShipTypeKeys.Concat(_ships.Keys).Concat(_shipLayouts.Keys)
            .Any(r => string.Equals(r, k, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>#427: whether a layout cell names something the server can stamp — any station tile (an unknown type becomes
    /// a hull marker by design), a special element, or a real block.</summary>
    internal bool IsKnownLayoutCell(ShipLayoutCell cell)
        => cell.Kind == "station" || (!string.IsNullOrEmpty(cell.Id) && (ShipLayoutCell.IsElementId(cell.Id) || _blocks.ContainsKey(cell.Id)));

    /// <summary>
    /// Populates the user ships (called after <see cref="Validate"/>, so the checks see the final content). A file is dropped
    /// with a warning — never a failed load — when its key is malformed, reserved or taken by a shipped ship or layout
    /// (case-insensitive, <see cref="IsReservedUserShipKey"/>) or by an earlier user ship, its layout is empty, names an unknown block or element, or has a cell outside the ship
    /// editor's room, it names an unknown module, item or blueprint, or it is craftable but wider than the 17 × 17 landing
    /// square. A ship that is kept gets its stats clamped to the editor's ranges and the defaults <c>tools/merge_ship.py</c>
    /// fills: the interior from the layout, the layout under its own key, start modules from its station tiles if it names none.
    /// </summary>
    public void SetUserShips(IReadOnlyList<UserShipFile>? files, Action<string>? warn = null)
    {
        _userShipsByKey.Clear();
        var kept = new List<UserShip>();
        foreach (var f in files ?? Array.Empty<UserShipFile>())
        {
            if (f == null)
            {
                continue;
            }

            string key = (f.Key ?? string.Empty).Trim();
            var uses = UserShipUsesOf(f, key, warn);
            var problems = UserShipProblems(f, key, uses);
            if (problems.Count > 0)
            {
                warn?.Invoke($"Skipping user ship '{key}': {string.Join("; ", problems)}.");
                continue;
            }

            var ship = BuildUserShip(f, key, uses);
            _userShipsByKey[key] = ship;
            kept.Add(ship);
        }

        _userShips = kept;
    }

    /// <summary>The file's known use tokens (lower-case, each once); no list = <see cref="UserShipUses.Default"/>. An unknown
    /// token is reported and ignored.</summary>
    private static IReadOnlyList<string> UserShipUsesOf(UserShipFile f, string key, Action<string>? warn)
    {
        if (f.Uses is null)
        {
            return UserShipUses.Default;
        }

        var uses = new List<string>();
        foreach (var raw in f.Uses)
        {
            string use = (raw ?? string.Empty).Trim().ToLowerInvariant();
            if (!UserShipUses.IsKnown(use))
            {
                warn?.Invoke($"User ship '{key}': unknown use '{raw}' ignored (npc | wreck | craft).");
            }
            else if (!uses.Contains(use))
            {
                uses.Add(use);
            }
        }

        return uses;
    }

    /// <summary>Why a user ship file cannot be loaded (empty = it can).</summary>
    private List<string> UserShipProblems(UserShipFile f, string key, IReadOnlyList<string> uses)
    {
        var problems = new List<string>();
        if (!IsValidUserShipKey(key))
        {
            problems.Add("the key must be 1–64 letters, digits, '_' or '-'");
            return problems;
        }

        if (IsReservedUserShipKey(key))
        {
            problems.Add("the key is reserved (custom, starter) or taken by a shipped ship or layout");
        }
        else if (_userShipsByKey.ContainsKey(key))
        {
            problems.Add("a user ship with that key is already loaded");
        }

        var cells = f.Layout?.Cells;
        if (cells is null || cells.Count == 0)
        {
            problems.Add("the layout has no cells");
            return problems;
        }

        AddUserShipCellProblems(cells, problems);
        AddUserShipReferenceProblems(f, problems);
        if (uses.Contains(UserShipUses.Craft) && !FitsLandingSquare(cells))
        {
            problems.Add($"a craftable ship must fit the {BasePadRules.Size} × {BasePadRules.Size} landing square");
        }

        return problems;
    }

    /// <summary>Unknown cell ids (the shipped layouts' #427 check) and cells outside the ship editor's room.</summary>
    private void AddUserShipCellProblems(List<ShipLayoutCell> cells, List<string> problems)
    {
        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        int outside = 0;
        foreach (var c in cells)
        {
            if (c is null)
            {
                unknown.Add("(null)");
                continue;
            }

            if (!IsKnownLayoutCell(c))
            {
                unknown.Add(string.IsNullOrEmpty(c.Id) ? "(empty)" : c.Id);
            }

            if (!ShipLayout.FitsEditorRoom(c.X, c.Y, c.Z))
            {
                outside++;
            }
        }

        if (unknown.Count > 0)
        {
            problems.Add("unknown block(s) or element(s) " + string.Join(", ", unknown));
        }

        if (outside > 0)
        {
            problems.Add($"{outside} cell(s) outside the ship editor's room");
        }
    }

    /// <summary>Start modules, craft-cost items and the blueprint must exist.</summary>
    private void AddUserShipReferenceProblems(UserShipFile f, List<string> problems)
    {
        foreach (var m in f.StartModules ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(m) && !_shipModules.ContainsKey(m.Trim()))
            {
                problems.Add($"unknown module '{m}'");
            }
        }

        foreach (var cost in f.CraftCost ?? new List<ItemAmount>())
        {
            if (cost != null && !string.IsNullOrWhiteSpace(cost.Item) && !_items.ContainsKey(cost.Item.Trim()))
            {
                problems.Add($"unknown craft-cost item '{cost.Item}'");
            }
        }

        if (!string.IsNullOrWhiteSpace(f.RequiredBlueprint) && !_blueprints.ContainsKey(f.RequiredBlueprint!.Trim()))
        {
            problems.Add($"unknown blueprint '{f.RequiredBlueprint}'");
        }
    }

    /// <summary>A key the ship ids, save rows and log lines can carry: 1–64 letters, digits, <c>_</c> or <c>-</c>.</summary>
    private static bool IsValidUserShipKey(string key)
        => key.Length is > 0 and <= 64 && key.All(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-');

    /// <summary>True when the cells' x/z extent fits a 17 × 17 landing square (<see cref="BasePadRules.Size"/>).</summary>
    public static bool FitsLandingSquare(IEnumerable<ShipLayoutCell?> cells)
    {
        int minX = 0, maxX = 0, minZ = 0, maxZ = 0;
        bool any = false;
        foreach (var c in cells)
        {
            if (c is null)
            {
                continue;
            }

            if (!any)
            {
                (minX, maxX, minZ, maxZ, any) = (c.X, c.X, c.Z, c.Z, true);
            }

            minX = Math.Min(minX, c.X);
            maxX = Math.Max(maxX, c.X);
            minZ = Math.Min(minZ, c.Z);
            maxZ = Math.Max(maxZ, c.Z);
        }

        return maxX - minX + 1 <= BasePadRules.Size && maxZ - minZ + 1 <= BasePadRules.Size;
    }

    /// <summary>The checked file as a loaded ship: clamped stats, interior from the layout, start modules filled.</summary>
    private UserShip BuildUserShip(UserShipFile f, string key, IReadOnlyList<string> uses)
    {
        var src = f.Layout!;
        var layout = new ShipLayout
        {
            Key = key,
            Width = InteriorExtent(src.Width, src.Cells, c => c.X, ShipLayout.EditorRoomWidth - ShipLayout.EditorOriginX),
            Height = InteriorExtent(src.Height, src.Cells, c => c.Y, ShipLayout.EditorRoomHeight - 1),
            Length = InteriorExtent(src.Length, src.Cells, c => c.Z, ShipLayout.EditorRoomLength - ShipLayout.EditorOriginZ),
            Cells = src.Cells,
        };

        var def = new ShipDefinition
        {
            Key = key,
            NameKey = string.Empty,
            DescriptionKey = string.Empty,
            DisplayName = string.IsNullOrWhiteSpace(f.Name) ? key : f.Name.Trim(),
            DisplayDescription = (f.Description ?? string.Empty).Trim(),
            BaseHull = ClampStat(f.BaseHull, UserShipHullMin, UserShipHullMax),
            BaseShield = ClampStat(f.BaseShield, 0f, UserShipShieldMax),
            FlightSpeed = ClampStat(f.FlightSpeed, UserShipFlightMin, UserShipFlightMax),
            Handling = ClampStat(f.Handling, UserShipFlightMin, UserShipFlightMax),
            InteriorWidth = layout.Width,
            InteriorLength = layout.Length,
            Height = layout.Height,
            RequiredBlueprint = string.IsNullOrWhiteSpace(f.RequiredBlueprint) ? null : f.RequiredBlueprint!.Trim(),
            CraftCost = (f.CraftCost ?? new List<ItemAmount>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Item) && c.Count > 0)
                .Select(c => new ItemAmount(c.Item.Trim(), c.Count))
                .ToList(),
            StartModules = UserShipStartModules(f, src.Cells),
            Layout = key,
        };

        string pack = string.IsNullOrWhiteSpace(f.Pack) ? "default" : f.Pack.Trim();
        return new UserShip(def, layout, uses, pack, Math.Max(1, Math.Min(UserShipWeightMax, f.Weight)), FitsLandingSquare(src.Cells));
    }

    /// <summary>The named start modules (trimmed, each once), else the modules of the station tiles (merge_ship's mapping).</summary>
    private List<string> UserShipStartModules(UserShipFile f, List<ShipLayoutCell> cells)
    {
        var modules = new List<string>();
        foreach (var m in f.StartModules ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(m) && !modules.Contains(m.Trim()))
            {
                modules.Add(m.Trim());
            }
        }

        if (modules.Count > 0)
        {
            return modules;
        }

        foreach (var c in cells)
        {
            foreach (var (station, module) in StationStartModules)
            {
                if (c.Kind == "station" && c.Id == station && _shipModules.ContainsKey(module) && !modules.Contains(module))
                {
                    modules.Add(module);
                }
            }
        }

        return modules;
    }

    /// <summary>An interior size: the file's own when positive, else the extent of the cells at 0 and above — capped to the
    /// editor room.</summary>
    private static int InteriorExtent(int declared, List<ShipLayoutCell> cells, Func<ShipLayoutCell, int> axis, int max)
    {
        int size = declared;
        if (size <= 0)
        {
            size = cells.Select(axis).Where(v => v >= 0).DefaultIfEmpty(0).Max() + 1;
        }

        return Math.Max(1, Math.Min(max, size));
    }

    /// <summary>A stat clamped into the editor's range (a NaN reads as the minimum).</summary>
    private static float ClampStat(float value, float min, float max)
        => float.IsNaN(value) ? min : Math.Max(min, Math.Min(max, value));
}
