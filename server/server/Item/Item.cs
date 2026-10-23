using System.Text.Json.Serialization;

namespace Ashen;

public class Item
{
    public static IReadOnlyDictionary<string, Item> All => GameData.Items;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public ItemPack Pack { get; set; }
    public ItemType Type { get; set; }
    public string Sprite { get; set; } = "";
    public int Price { get; set; }
    public Stats? BaseStats { get; set; }
    public LevelStats? LevelStats { get; set; }
    [JsonPropertyName("effects")]
    public List<string>? EffectIds { get; set; }
    [JsonIgnore]
    public List<Effect> Effects { get; set; } = new();
    public string Description { get; set; } = "";

    [JsonIgnore]
    public bool IsSpecial => Pack != ItemPack.Generic;
    [JsonIgnore]
    public bool IsDynamic => Type is ItemType.Buff or ItemType.Charm or ItemType.Passive;
}
