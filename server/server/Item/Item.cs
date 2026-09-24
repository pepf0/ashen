using System.Text.Json.Serialization;

namespace Ashen;

public class Item
{
    public static string JSONFolder => "../../json";
    public static Dictionary<string, Item> All => ItemLoader.ItemsFromJson(File.ReadAllText(JSONFolder + "/items.json"))
        .ToDictionary(item => item.Id);
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
    public List<Effect>? Effects => EffectIds?.Select(effectId => Effect.All.Values.Single(effect => effectId == effect.Id)).ToList();
    public string Description { get; set; }
}
