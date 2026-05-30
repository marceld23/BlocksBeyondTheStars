namespace Spacecraft.Shared.State;

/// <summary>A stack of identical items in an inventory slot.</summary>
public sealed class ItemStack
{
    public string Item { get; set; } = string.Empty;
    public int Count { get; set; }

    public ItemStack() { }

    public ItemStack(string item, int count)
    {
        Item = item;
        Count = count;
    }

    public bool IsEmpty => Count <= 0 || string.IsNullOrEmpty(Item);

    public ItemStack Clone() => new(Item, Count);
}
