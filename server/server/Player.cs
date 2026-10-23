namespace Ashen;

/// <summary>A player in a running match: build, money, lives and shop state. Persists between rounds.</summary>
public class Player
{
    public const int DynamicSlots = 4;
    public const double BaseSpecialChance = 0.01;

    public long Id { get; set; }
    public string Name { get; set; } = "";
    public OwnedItem? Weapon { get; set; }
    public OwnedItem? Shirt { get; set; }
    public OwnedItem?[] Dynamics { get; set; } = new OwnedItem?[DynamicSlots];
    public double MaxHealth { get; set; } = 1000;
    public int HpUpgrades { get; set; }
    public int Money { get; set; } = 100;
    public int Lives { get; set; } = 5;
    public HashSet<string> ObtainedSpecials { get; } = new();
    public List<string> BonusEffects { get; } = new();
    public List<string> Titles { get; } = new();
    public Shop Shop { get; } = new();

    public bool Ready { get; set; }
    public bool Connected { get; set; }
    public bool Eliminated => Lives <= 0;
    public int? Placement { get; set; }
    public int EliminatedInRound { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    /// <summary>How often this player fought each opponent (for rotating pairings).</summary>
    public Dictionary<long, int> TimesFought { get; } = new();
    public int GhostFights { get; set; }
    public string Kit { get; set; } = Ashen.Kit.Default;
    /// <summary>Losses in a row; from 2 on the next shop pays a comeback bonus. Wins and draws reset it.</summary>
    public int LossStreak { get; set; }
    /// <summary>What the player got when the current shop opened (shown in the shop).</summary>
    public Dictionary<string, int> LastIncome { get; set; } = new();
    public List<string> Augments { get; } = new();
    /// <summary>Augments offered in this shop (rounds 3/6/9), or null.</summary>
    public List<string>? AugmentOffer { get; set; }
    /// <summary>Mystery event offered in this shop, or null.</summary>
    public MysteryOffer? Mystery { get; set; }
    public int WinStreak { get; set; }
    /// <summary>Set by the Library mystery: the next kit change is free.</summary>
    public bool FreeKitSwap { get; set; }
    public const int BountyStreak = 3;
    public const int BountyPerWin = 10;
    /// <summary>Money paid to whoever ends this player's win streak (3+ wins).</summary>
    public int Bounty => WinStreak >= BountyStreak ? BountyPerWin * WinStreak : 0;
    public Dictionary<string, double> AugmentMods => Augment.Combine(Augments);
    /// <summary>Fights in rounds 1 and 2 fought without a weapon (2 = gets the pity offer in shop 3).</summary>
    public int EarlyWeaponlessFights { get; set; }
    /// <summary>Set after a fight, used to rank players eliminated in the same round.</summary>
    public double LastFightHpPercent { get; set; }
    public double LastFightDamageDealt { get; set; }

    public IEnumerable<OwnedItem> AllItems()
    {
        if (Weapon is not null) yield return Weapon;
        if (Shirt is not null) yield return Shirt;
        foreach (var item in Dynamics)
            if (item is not null) yield return item;
    }

    public OwnedItem? FindOwned(string itemId) => AllItems().FirstOrDefault(i => i.Id == itemId);

    /// <summary>Base 1%, times every special_chance buff the player owns (different effects multiply).</summary>
    public double SpecialChance => SpecialChanceWith(null);

    public double SpecialChanceWith(string? modifier, double extraMult = 1)
    {
        {
            if (modifier == "unlucky") return 0;
            double chance = BaseSpecialChance * (modifier == "lucky_day" ? 3 : 1) * extraMult * AugmentMods.Get("special_chance_mult");
            foreach (var item in AllItems())
                foreach (var effect in item.Item.Effects.Where(e => e.Type == EffectType.Buff && e.Target == "special_chance"))
                {
                    if (effect.Mult is double m) chance *= 1 + (m - 1) * item.EffectScale;
                    if (effect.Add is double a) chance += a * item.EffectScale;
                }
            return Math.Clamp(chance, 0, 1);
        }
    }

    public Loadout ToLoadout(bool ghost = false) => new()
    {
        Name = ghost ? $"Ghost of {Name}" : Name,
        PlayerId = ghost ? null : Id,
        Ghost = ghost,
        MaxHealth = MaxHealth,
        Money = Money,
        Weapon = Weapon?.Clone(),
        Shirt = Shirt?.Clone(),
        Dynamics = Dynamics.Select(d => d?.Clone()).ToArray(),
        BonusEffects = BonusEffects.ToList(),
        Kit = Kit,
        Bot = ghost,
        AugmentMods = AugmentMods,
        AugmentIds = Augments.ToList()
    };

    public List<ItemView> BuildView()
    {
        var list = new List<ItemView>();
        if (Weapon is not null) list.Add(new ItemView(Weapon.Id, Weapon.Level, "weapon"));
        if (Shirt is not null) list.Add(new ItemView(Shirt.Id, Shirt.Level, "shirt"));
        for (int i = 0; i < Dynamics.Length; i++)
            if (Dynamics[i] is { } d) list.Add(new ItemView(d.Id, d.Level, $"dynamic{i}"));
        return list;
    }
}

/// <summary>A one-off choice next to the shop (about 25% of shops).</summary>
public class MysteryOffer
{
    public string Kind { get; set; } = "";
    /// <summary>Shrine: the specials to choose from.</summary>
    public List<string> Choices { get; set; } = new();
    public bool Used { get; set; }
    /// <summary>Outcome text after using it (e.g. the gamble result).</summary>
    public string? Result { get; set; }

    public static readonly string[] Kinds = { "chest", "shrine", "gamble", "blacksmith", "fountain", "library" };
    public const double Chance = 0.25;
    public const int ChestCost = 25;
    public const double ChestSpecialChance = 0.1;
    public const int BlacksmithCost = 15;
    public const int BlacksmithSpecialMaxLevel = 3;
    public const int FountainCost = 40;
    public const int LibraryMoney = 10;
}
