namespace Ashen;

/// <summary>An item in a player's build. Persists between rounds; combat never mutates it.</summary>
public class OwnedItem
{
    public const int MaxLevel = 5;

    public Item Item { get; }
    public int Level { get; set; } = 1;

    public OwnedItem(Item item, int level = 1)
    {
        Item = item;
        Level = level;
    }

    public string Id => Item.Id;

    /// <summary>Base stat plus level_stats for every level above 1.</summary>
    public double Stat(string stat) =>
        (Item.BaseStats?.Get(stat) ?? 0) + (Level - 1) * (Item.LevelStats?.Get(stat) ?? 0);

    public bool HasStat(string stat) => Item.BaseStats?.Get(stat) is not null || Item.LevelStats?.Get(stat) is not null;

    /// <summary>Effect strength multiplier: +50% of the base strength per level above 1.</summary>
    public double EffectScale => 1 + 0.5 * (Level - 1);

    public OwnedItem Clone() => new(Item, Level);
}
