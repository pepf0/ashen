using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ashen;

public class Item
{
    public string Id { get; set; }
    public string Name { get; set; }
    public ItemPack Pack { get; set; }
    public ItemType Type { get; set; }
    public string Sprite { get; set; }
    public int Price { get; set; }
    public Stats? BaseStats { get; set; }
    public Stats? LevelStats { get; set; }
    [JsonPropertyName("effects")]
    public List<string>? EffectIds { get; set; }
    [JsonIgnore]
    public List<Effect>? Effects => EffectIds.Select(effectId => Program.Effects.Values.Single(effect => effectId == effect.Id )).ToList();
    public string Description { get; set; }
}

public class Stats
{
    public double? Damage { get; set; }
    public double? Cooldown { get; set; }
    public double? CritChance { get; set; }
    public double? CritDamage { get; set; }
    public double? DamageReduction { get; set; }
}

public class Effect
{
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

public class TriggerCondition
{
    public double? HPBelow { get; set; }
    public int? EveryNHits { get; set; }
}

public enum ItemPack
{
    Pepf,
    Z7fox,
    Ely7,
    Xxzweiradxx,
    Generic
}
public enum ItemType
{
    Weapon,
    Buff,
    Shirt,
    Charm,
    Passive
}

public enum EffectType
{
    Buff,
    Attack,
    Stun,
    Invulnerability,
    Gamble,
    Money,
    Interest
}

public static class ItemLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public static List<Item> ItemsFromJson(string json)
    {
        return JsonSerializer.Deserialize<List<Item>>(json, Options)
            ?? throw new JsonException("items.json ist leer oder ungueltig.");
    }

    public static List<Effect> EffectsFromJson(string json)
    {
        return JsonSerializer.Deserialize<List<Effect>>(json, Options)
            ?? throw new JsonException("effects.json ist leer oder ungueltig.");
    }
}