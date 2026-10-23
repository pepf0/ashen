using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ashen;

public static class ItemLoader
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public static List<Item> ItemsFromJson(string json)
    {
        return JsonSerializer.Deserialize<List<Item>>(json, Options)
            ?? throw new JsonException("items.json is empty or invalid.");
    }

    public static List<Effect> EffectsFromJson(string json)
    {
        return JsonSerializer.Deserialize<List<Effect>>(json, Options)
            ?? throw new JsonException("effects.json is empty or invalid.");
    }

    public static List<LobbyEvent> EventsFromJson(string json)
    {
        return JsonSerializer.Deserialize<List<LobbyEvent>>(json, Options)
            ?? throw new JsonException("events.json is empty or invalid.");
    }
}
