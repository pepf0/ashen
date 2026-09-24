using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ashen;

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
