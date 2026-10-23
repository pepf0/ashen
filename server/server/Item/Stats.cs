namespace Ashen;

public class Stats
{
    public double? Damage { get; set; }
    public double? Cooldown { get; set; }
    public double? CritChance { get; set; }
    public double? CritDamage { get; set; }
    public double? DamageReduction { get; set; }
    public double? DodgeChance { get; set; }

    public double? Get(string stat) => stat switch
    {
        StatNames.Damage => Damage,
        StatNames.Cooldown => Cooldown,
        StatNames.CritChance => CritChance,
        StatNames.CritDamage => CritDamage,
        StatNames.DamageReduction => DamageReduction,
        StatNames.DodgeChance => DodgeChance,
        _ => null
    };
}

public class LevelStats
{
    public double? DamageAdd { get; set; }
    public double? CooldownAdd { get; set; }
    public double? CritChanceAdd { get; set; }
    public double? CritDamageAdd { get; set; }
    public double? DamageReductionAdd { get; set; }
    public double? DodgeChanceAdd { get; set; }

    public double? Get(string stat) => stat switch
    {
        StatNames.Damage => DamageAdd,
        StatNames.Cooldown => CooldownAdd,
        StatNames.CritChance => CritChanceAdd,
        StatNames.CritDamage => CritDamageAdd,
        StatNames.DamageReduction => DamageReductionAdd,
        StatNames.DodgeChance => DodgeChanceAdd,
        _ => null
    };

    public bool IsEmpty => DamageAdd is null && CooldownAdd is null && CritChanceAdd is null
        && CritDamageAdd is null && DamageReductionAdd is null && DodgeChanceAdd is null;
}

public static class StatNames
{
    public const string Damage = "damage";
    public const string Cooldown = "cooldown";
    public const string CritChance = "crit_chance";
    public const string CritDamage = "crit_damage";
    public const string DamageReduction = "damage_reduction";
    public const string DodgeChance = "dodge_chance";
    public const string HitCondition = "hit_condition";
}
