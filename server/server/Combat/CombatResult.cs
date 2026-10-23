namespace Ashen;

public class CombatResult
{
    public FighterInfo[] Fighters { get; set; } = new FighterInfo[2];
    public List<CombatEvent> Events { get; set; } = new();
    /// <summary>0 or 1, or -1 for a draw.</summary>
    public int Winner { get; set; }
    public double Duration { get; set; }
    public double[] EndHp { get; set; } = new double[2];
    public double[] DamageDealt { get; set; } = new double[2];
    public double[] DamageTaken { get; set; } = new double[2];
    public int[] Earnings { get; set; } = new int[2];
    public string? Arena { get; set; }
}

public class FighterInfo
{
    public string Name { get; set; } = "";
    public long? PlayerId { get; set; }
    public bool Ghost { get; set; }
    public double MaxHp { get; set; }
    public double DamageReduction { get; set; }
    public double DodgeChance { get; set; }
    public string? Kit { get; set; }
    public List<string> Augments { get; set; } = new();
    public List<ItemView> Items { get; set; } = new();
    /// <summary>Buffs active for the whole fight (static effects), as they are at the start.</summary>
    public List<BuffEvent> StartBuffs { get; set; } = new();
}

public record ItemView(string Id, int Level, string Slot);
