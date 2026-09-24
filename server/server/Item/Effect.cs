namespace Ashen;

public class Effect
{
    public static Dictionary<string, Effect> All => ItemLoader.EffectsFromJson(File.ReadAllText(Item.JSONFolder + "/effects.json"))
        .ToDictionary(effect => effect.Id);
    public string Id { get; set; }
    public string? Trigger { get; set; }
    public double? Cooldown { get; set; }
    public TriggerCondition? Condition { get; set; }
    public EffectType Type { get; set; }
    public string? Target { get; set; }
    public string? Stat { get; set; }
    public double? Mult { get; set; }
    public double? Add { get; set; }
    public double? Duration { get; set; }
    public int? MaxStacks { get; set; }
    public double? Damage { get; set; }
    public double? Chance { get; set; }
    public double? StartMoney { get; set; }
    public double? WinMult { get; set; }
    public double? LoseMult { get; set; }
    public double? Gain { get; set; }
    public string Description { get; set; }
}
