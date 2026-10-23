namespace Ashen;

/// <summary>Loads items, effects and lobby events once at startup.</summary>
public static class GameData
{
    public static string Root { get; private set; } = "";
    public static IReadOnlyDictionary<string, Item> Items { get; private set; } = new Dictionary<string, Item>();
    public static IReadOnlyDictionary<string, Effect> Effects { get; private set; } = new Dictionary<string, Effect>();
    public static IReadOnlyList<LobbyEvent> Events { get; private set; } = new List<LobbyEvent>();
    public static IReadOnlyList<Item> Generics { get; private set; } = new List<Item>();
    public static IReadOnlyList<Item> Specials { get; private set; } = new List<Item>();

    private static readonly object loadLock = new();
    private static bool loaded;

    public static void EnsureLoaded()
    {
        lock (loadLock)
        {
            if (loaded) return;
            Load(FindRoot());
        }
    }

    /// <summary>The repository root: the folder holding index.html and json/. ASHEN_ROOT overrides the search.</summary>
    public static string FindRoot()
    {
        var env = Environment.GetEnvironmentVariable("ASHEN_ROOT");
        if (!string.IsNullOrWhiteSpace(env)) return Path.GetFullPath(env);

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "json", "items.json"))) return dir.FullName;
                dir = dir.Parent;
            }
        }
        throw new DirectoryNotFoundException("Could not find json/items.json. Set ASHEN_ROOT to the repository folder.");
    }

    public static void Load(string root)
    {
        lock (loadLock)
        {
            Root = root;
            var json = Path.Combine(root, "json");
            var items = ItemLoader.ItemsFromJson(File.ReadAllText(Path.Combine(json, "items.json")));
            var effects = ItemLoader.EffectsFromJson(File.ReadAllText(Path.Combine(json, "effects.json")));
            var events = ItemLoader.EventsFromJson(File.ReadAllText(Path.Combine(json, "events.json")));

            var effectMap = effects.ToDictionary(e => e.Id);
            foreach (var effect in effects) effect.ParseTrigger();
            foreach (var item in items)
            {
                item.Effects = (item.EffectIds ?? new List<string>())
                    .Select(id => effectMap.TryGetValue(id, out var e)
                        ? e
                        : throw new InvalidDataException($"Item {item.Id}: unknown effect '{id}'"))
                    .ToList();
            }
            var itemMap = items.ToDictionary(i => i.Id);
            foreach (var ev in events)
            {
                foreach (var id in ev.RewardEffects ?? new List<string>())
                    if (!effectMap.ContainsKey(id)) throw new InvalidDataException($"Event {ev.Id}: unknown effect '{id}'");
                if (ev.Item is not null && !itemMap.ContainsKey(ev.Item))
                    throw new InvalidDataException($"Event {ev.Id}: unknown item '{ev.Item}'");
            }

            Items = itemMap;
            Effects = effectMap;
            Events = events;
            Generics = items.Where(i => !i.IsSpecial).ToList();
            Specials = items.Where(i => i.IsSpecial).ToList();
            loaded = true;
        }
    }
}
