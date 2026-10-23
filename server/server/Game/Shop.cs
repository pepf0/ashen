namespace Ashen;

public class ShopSlot
{
    public string ItemId { get; set; } = "";
    public bool Bought { get; set; }
}

/// <summary>A player's shop for the current shop phase.</summary>
public class Shop
{
    public const int Slots = 6;
    public const int FreeRerolls = 5;
    public const double DragonWeight = 0.25;
    public const string DragonId = "pepf.dragon";

    public List<ShopSlot> Items { get; set; } = new();
    public int RerollsLeft { get; set; } = FreeRerolls;
    /// <summary>True once something was bought from this roll: no more rerolls this phase.</summary>
    public bool Locked { get; set; }

    /// <summary>
    /// Rolls a fresh shop. <paramref name="unavailableSpecials"/> are specials that can't appear
    /// (already obtained by this player, or by anyone in "per lobby" mode).
    /// </summary>
    public static List<ShopSlot> Roll(Player player, ISet<string> unavailableSpecials, Random rng)
    {
        var slots = new List<ShopSlot>();
        var inShop = new HashSet<string>();
        double specialChance = player.SpecialChance;
        var generics = GameData.Generics
            .Where(g => player.FindOwned(g.Id) is not { Level: >= OwnedItem.MaxLevel })
            .ToList();

        for (int i = 0; i < Slots; i++)
        {
            Item? pick = null;
            if (rng.NextDouble() < specialChance)
            {
                var pool = GameData.Specials
                    .Where(s => !unavailableSpecials.Contains(s.Id) && !inShop.Contains(s.Id))
                    .ToList();
                pick = WeightedPick(pool, rng);
            }
            if (pick is null && generics.Count > 0)
                pick = generics[rng.Next(generics.Count)];
            if (pick is null) continue;
            if (pick.IsSpecial) inShop.Add(pick.Id);
            slots.Add(new ShopSlot { ItemId = pick.Id });
        }
        return slots;
    }

    public static double Weight(Item item) => item.Id == DragonId ? DragonWeight : 1;

    public static Item? WeightedPick(IReadOnlyList<Item> pool, Random rng)
    {
        double total = pool.Sum(Weight);
        if (total <= 0) return null;
        double roll = rng.NextDouble() * total;
        foreach (var item in pool)
        {
            roll -= Weight(item);
            if (roll < 0) return item;
        }
        return pool[^1];
    }
}
