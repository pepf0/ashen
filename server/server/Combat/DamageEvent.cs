namespace Ashen;

public class DamageEvent : CombatEvent
{
    public double Damage { get; set; }
    public double? CritMultiplier { get; set; } = null; 
}
