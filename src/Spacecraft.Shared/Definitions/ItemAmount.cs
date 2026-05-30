namespace Spacecraft.Shared.Definitions;

/// <summary>
/// A quantity of an item referenced by its string key. Used for recipe inputs/outputs,
/// block drops, build costs and blueprint unlock costs.
/// </summary>
public sealed class ItemAmount
{
    public string Item { get; set; } = string.Empty;
    public int Count { get; set; } = 1;

    public ItemAmount() { }

    public ItemAmount(string item, int count)
    {
        Item = item;
        Count = count;
    }
}
