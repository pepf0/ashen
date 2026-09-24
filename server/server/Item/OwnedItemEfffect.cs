namespace Ashen;

public class OwnedItemEffect
{
    public required Effect Effect { get; set; }
    public double RealCooldown { get; set; }
    public double CurrentCooldown { get; set; }
    public double? RealDamage { get; set; }
    public int? RealEveryNHits { get; set; }
    public int CurrentStacks { get; set; }
    public double? BuffExpiresAt { get; set; }
}