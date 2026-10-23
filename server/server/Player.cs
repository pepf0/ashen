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
    public double SpecialChance
    {
        get
        {
            double chance = BaseSpecialChance;
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
        BonusEffects = BonusEffects.ToList()
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
