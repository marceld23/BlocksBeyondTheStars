using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A combined view over the inventories a player may currently draw from: their personal
/// inventory plus, when aboard the ship, the cargo hold (technical requirements §15 — ship
/// cargo counts toward crafting when the player is inside the ship). Adds prefer the
/// personal inventory and spill to cargo.
/// </summary>
public sealed class MaterialPool
{
    private readonly GameContent _content;
    private readonly Inventory _personal;
    private readonly Inventory? _cargo;

    public MaterialPool(GameContent content, PlayerState player, ShipState ship)
    {
        _content = content;
        _personal = player.Inventory;
        _cargo = player.AboardShip ? ship.Cargo : null;
    }

    public int Count(string item) => _personal.CountOf(item) + (_cargo?.CountOf(item) ?? 0);

    public bool Has(IEnumerable<ItemAmount> items)
    {
        foreach (var need in items)
        {
            if (Count(need.Item) < need.Count)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Removes the listed amounts (personal first, then cargo). Caller must check <see cref="Has"/> first.</summary>
    public void Remove(IEnumerable<ItemAmount> items)
    {
        foreach (var need in items)
        {
            int remaining = need.Count;
            int fromPersonal = System.Math.Min(remaining, _personal.CountOf(need.Item));
            if (fromPersonal > 0)
            {
                _personal.Remove(need.Item, fromPersonal);
                remaining -= fromPersonal;
            }

            if (remaining > 0)
            {
                _cargo?.Remove(need.Item, remaining);
            }
        }
    }

    /// <summary>
    /// Adds items, personal inventory first then cargo. Returns the amount that did not fit
    /// anywhere (0 = fully stored).
    /// </summary>
    public int Add(string item, int count)
    {
        int maxStack = _content.MaxStackOf(item);
        int leftover = _personal.Add(item, count, maxStack);
        if (leftover > 0 && _cargo is not null)
        {
            leftover = _cargo.Add(item, leftover, maxStack);
        }

        return leftover;
    }
}
