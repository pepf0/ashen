using System.Text.Json.Serialization;

namespace Ashen;

public class Effect
{
    public static IReadOnlyDictionary<string, Effect> All => GameData.Effects;
    public string Id { get; set; } = "";
    [JsonPropertyName("trigger")]
    public string? TriggerRaw { get; set; }
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
    public string Description { get; set; } = "";

    /// <summary>Parsed from <see cref="TriggerRaw"/> when the data is loaded.</summary>
    [JsonIgnore]
    public TriggerKind Trigger { get; set; } = TriggerKind.Static;
    /// <summary>For Hit/Crit triggers: the weapon whose hits/crits fire this effect.</summary>
    [JsonIgnore]
    public string? TriggerItemId { get; set; }

    /// <summary>Effects with no max_stacks: timed buffs act as on/off, untimed ones stack forever.</summary>
    [JsonIgnore]
    public int StackLimit => MaxStacks ?? (Duration is not null ? 1 : int.MaxValue);

    public void ParseTrigger()
    {
        switch (TriggerRaw)
        {
            case null:
                Trigger = TriggerKind.Static;
                break;
            case "cooldown":
                Trigger = TriggerKind.Cooldown;
                break;
            case "round-start":
                Trigger = TriggerKind.RoundStart;
                break;
            case var t when t.EndsWith(".hit"):
                Trigger = TriggerKind.Hit;
                TriggerItemId = t[..^".hit".Length];
                break;
            case var t when t.EndsWith(".crit"):
                Trigger = TriggerKind.Crit;
                TriggerItemId = t[..^".crit".Length];
                break;
            default:
                throw new InvalidDataException($"Effect {Id}: unknown trigger '{TriggerRaw}'");
        }
    }
}

public enum TriggerKind
{
    Static,
    Cooldown,
    RoundStart,
    Hit,
    Crit
}
