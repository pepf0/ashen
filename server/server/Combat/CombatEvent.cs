using System.Text.Json.Serialization;

namespace Ashen;

/// <summary>
/// One thing that happened in a fight. Events only hold values (no live references), and
/// <see cref="Hp"/> is a snapshot of both fighters' HP right after the event.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(DamageEvent), "damage")]
[JsonDerivedType(typeof(StunEvent), "stun")]
[JsonDerivedType(typeof(BuffEvent), "buff")]
[JsonDerivedType(typeof(InvulnerabilityEvent), "invulnerability")]
[JsonDerivedType(typeof(MoneyEvent), "money")]
public abstract class CombatEvent
{
    public int Tick { get; set; }
    /// <summary>Seconds since the fight started.</summary>
    public double T { get; set; }
    /// <summary>Index (0/1) of the fighter that caused the event.</summary>
    public int Source { get; set; }
    /// <summary>Index (0/1) of the fighter the event affects.</summary>
    public int Target { get; set; }
    public string? ItemSource { get; set; }
    public string? EffectSource { get; set; }
    public double[] Hp { get; set; } = new double[2];
}

public class DamageEvent : CombatEvent
{
    public double Damage { get; set; }
    public double? CritMultiplier { get; set; }
    public bool Dodged { get; set; }
    public bool Blocked { get; set; }
    public bool SuddenDeath { get; set; }
    /// <summary>True for weapon swings (used by the client for cooldown bars).</summary>
    public bool WeaponAttack { get; set; }
}

public class StunEvent : CombatEvent
{
    public double Duration { get; set; }
    public bool Crit { get; set; }
    public bool Blocked { get; set; }
}

public class BuffEvent : CombatEvent
{
    public string? Stat { get; set; }
    /// <summary>Item or effect the buff modifies (resolved, e.g. "pepf.guitar" instead of "type.weapon").</summary>
    public string? BuffTarget { get; set; }
    public int Stacks { get; set; }
    /// <summary>Combined multiplier of all stacks of this effect, if it is a multiplier.</summary>
    public double? Mult { get; set; }
    /// <summary>Combined flat bonus of all stacks of this effect, if it is additive.</summary>
    public double? Add { get; set; }
    /// <summary>Seconds left before the stacks drop, or null if it lasts all fight.</summary>
    public double? Duration { get; set; }
}

public class InvulnerabilityEvent : CombatEvent
{
    public double Duration { get; set; }
}

public class MoneyEvent : CombatEvent
{
    /// <summary>Money gained by this event (Coin, Crown).</summary>
    public double Gain { get; set; }
    /// <summary>Black Market Chip: the payout after this gamble, and whether it doubled.</summary>
    public double? Payout { get; set; }
    public bool? Won { get; set; }
}
