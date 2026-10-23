namespace Ashen;

/// <summary>Match/round rules that change how a shop rolls.</summary>
public class ShopRollOptions
{
    public string? Modifier { get; set; }
    public int Slots { get; set; } = Shop.Slots;
    public int MaxLevel { get; set; } = OwnedItem.MaxLevel;
    public bool NoShirts { get; set; }
    public double SpecialMult { get; set; } = 1;
    /// <summary>Price discount (0.1 = 10% off) from augments.</summary>
    public double Discount { get; set; }
}

public class ShopSlot
{
    public string ItemId { get; set; } = "";
    public bool Bought { get; set; }
    /// <summary>Price if it differs from the item's (pity offers, discounts, free mystery rewards).</summary>
    public int? Price { get; set; }
    /// <summary>For bonus offers: where it came from ("weaponless", "chest", "shrine").</summary>
    public string? Source { get; set; }
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
    /// <summary>Shop 3 offer for players who fought rounds 1 and 2 without a weapon: a special weapon and its
    /// pack's buff at a third of the price. Buying these doesn't lock the roll.</summary>
    public List<ShopSlot> Pity { get; set; } = new();
    public const int PityRound = 3;

    /// <summary>
    /// Rolls a fresh shop. <paramref name="unavailableSpecials"/> are specials that can't appear
    /// (already obtained by this player, or by anyone in "per lobby" mode).
    /// </summary>
    public static List<ShopSlot> Roll(Player player, ISet<string> unavailableSpecials, Random rng, string? modifier = null)
        => Roll(player, unavailableSpecials, rng, new ShopRollOptions { Modifier = modifier });

    public static List<ShopSlot> Roll(Player player, ISet<string> unavailableSpecials, Random rng, ShopRollOptions options)
    {
        var modifier = options.Modifier;
        var slots = new List<ShopSlot>();
        var inShop = new HashSet<string>();
        double specialChance = player.SpecialChanceWith(modifier, options.SpecialMult);
        var generics = GameData.Generics
            .Where(g => player.FindOwned(g.Id) is not { } owned || owned.Level < options.MaxLevel)
            .Where(g => !(options.NoShirts && g.Type == ItemType.Shirt))
            .ToList();

        for (int i = 0; i < options.Slots; i++)
        {
            Item? pick = null;
            if (rng.NextDouble() < specialChance)
            {
                var pool = GameData.Specials
                    .Where(s => !unavailableSpecials.Contains(s.Id) && !inShop.Contains(s.Id))
                    .Where(s => !(options.NoShirts && s.Type == ItemType.Shirt))
                    .ToList();
                pick = WeightedPick(pool, rng);
            }
            if (pick is null && generics.Count > 0)
                pick = generics[rng.Next(generics.Count)];
            if (pick is null) continue;
            if (pick.IsSpecial) inShop.Add(pick.Id);
            // Clearance (30% off this round) and augment discounts.
            double priceMult = (modifier == "clearance" ? 0.7 : 1) * (1 - options.Discount);
            slots.Add(new ShopSlot { ItemId = pick.Id, Price = priceMult < 1 ? (int)Math.Round(pick.Price * priceMult) : null });
        }
        return slots;
    }

    /// <summary>A random special weapon that isn't unavailable, plus its pack's buff item (if still available).</summary>
    public static List<ShopSlot> PityOffer(ISet<string> unavailableSpecials, Random rng)
    {
        var weapons = GameData.Specials.Where(i => i.Type == ItemType.Weapon && !unavailableSpecials.Contains(i.Id)).ToList();
        if (weapons.Count == 0) return new List<ShopSlot>();
        var weapon = weapons[rng.Next(weapons.Count)];
        var offer = new List<ShopSlot> { new() { ItemId = weapon.Id, Price = ThirdPrice(weapon), Source = "weaponless" } };
        var buff = GameData.Specials.FirstOrDefault(i => i.Pack == weapon.Pack && i.Type == ItemType.Buff);
        if (buff is not null && !unavailableSpecials.Contains(buff.Id))
            offer.Add(new ShopSlot { ItemId = buff.Id, Price = ThirdPrice(buff), Source = "weaponless" });
        return offer;
    }

    public static int ThirdPrice(Item item) => (int)Math.Ceiling(item.Price / 3.0);

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
