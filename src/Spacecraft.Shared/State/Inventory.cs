namespace Spacecraft.Shared.State;

/// <summary>
/// A slot-based item container used for the player's personal inventory and the ship's
/// cargo hold. Stacking rules depend on per-item max stack sizes, which the caller
/// supplies (the registry knows them) so this type stays free of definition lookups.
/// </summary>
public sealed class Inventory
{
    private readonly ItemStack?[] _slots;

    public Inventory(int slotCount)
    {
        if (slotCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slotCount));
        }

        _slots = new ItemStack?[slotCount];
    }

    public int SlotCount => _slots.Length;

    public IReadOnlyList<ItemStack?> Slots => _slots;

    public int CountOf(string item)
    {
        int total = 0;
        foreach (var slot in _slots)
        {
            if (slot is { } s && s.Item == item)
            {
                total += s.Count;
            }
        }

        return total;
    }

    /// <summary>
    /// Adds up to <paramref name="count"/> items, respecting <paramref name="maxStack"/>.
    /// Returns the number that did NOT fit (0 means everything was added).
    /// </summary>
    public int Add(string item, int count, int maxStack)
    {
        if (count <= 0)
        {
            return 0;
        }

        if (maxStack < 1)
        {
            maxStack = 1;
        }

        // Top up existing stacks first.
        for (int i = 0; i < _slots.Length && count > 0; i++)
        {
            if (_slots[i] is { } s && s.Item == item && s.Count < maxStack)
            {
                int space = maxStack - s.Count;
                int moved = System.Math.Min(space, count);
                s.Count += moved;
                count -= moved;
            }
        }

        // Then fill empty slots.
        for (int i = 0; i < _slots.Length && count > 0; i++)
        {
            if (_slots[i] is null)
            {
                int moved = System.Math.Min(maxStack, count);
                _slots[i] = new ItemStack(item, moved);
                count -= moved;
            }
        }

        return count;
    }

    /// <summary>Returns true if at least <paramref name="count"/> of the item is present.</summary>
    public bool Has(string item, int count) => CountOf(item) >= count;

    /// <summary>
    /// Removes <paramref name="count"/> items. Returns false and changes nothing if there
    /// are not enough.
    /// </summary>
    public bool Remove(string item, int count)
    {
        if (count <= 0)
        {
            return true;
        }

        if (!Has(item, count))
        {
            return false;
        }

        int remaining = count;
        for (int i = 0; i < _slots.Length && remaining > 0; i++)
        {
            if (_slots[i] is { } s && s.Item == item)
            {
                int taken = System.Math.Min(s.Count, remaining);
                s.Count -= taken;
                remaining -= taken;
                if (s.Count <= 0)
                {
                    _slots[i] = null;
                }
            }
        }

        return true;
    }

    /// <summary>Directly sets a slot (used when loading from storage).</summary>
    public void SetSlot(int index, ItemStack? stack) => _slots[index] = stack;
}
