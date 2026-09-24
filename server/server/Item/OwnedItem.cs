namespace Ashen;

public class OwnedItem : Item
{
    public int Level { get; set; } = 1;

public double RealDamage { get; set; }
public double RealCooldown { get; set; }
public double RealCritChance { get; set; }
public double RealCritDamage { get; set; }
public double RealDamageReduction { get; set; }
public double RealDodgeChance { get; set; }

    public double CurrentCooldown { get; set; }

    public List<OwnedItemEffect> ActiveEffects { get; set; }

    public OwnedItem(Item baseItem)
    {
        Id = baseItem.Id;
        Name = baseItem.Name;
        Pack = baseItem.Pack;
        Type = baseItem.Type;
        Sprite = baseItem.Sprite;
        Price = baseItem.Price;
        BaseStats = baseItem.BaseStats;
        LevelStats = baseItem.LevelStats;
        EffectIds = baseItem.EffectIds;
        Description = baseItem.Description;

        RealDamage = BaseStats?.Damage ?? 0;
        RealCooldown = BaseStats?.Cooldown ?? 0;
        RealCritChance = BaseStats?.CritChance ?? 0;
        RealCritDamage = BaseStats?.CritDamage ?? 0;
        RealDamageReduction = BaseStats?.DamageReduction ?? 0;
        CurrentCooldown = RealCooldown;

        ActiveEffects = (Effects ?? new List<Effect>())
            .Select(e => new OwnedItemEffect
            {
                Effect = e,
                RealCooldown = e.Cooldown ?? 0,
                CurrentCooldown = e.Cooldown ?? 0
            })
            .ToList();
    }
    // ...
}