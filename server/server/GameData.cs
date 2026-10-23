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
    public static IReadOnlyDictionary<string, Kit> Kits { get; private set; } = new Dictionary<string, Kit>();
    public static IReadOnlyList<RoundModifier> Modifiers { get; private set; } = new List<RoundModifier>();
    public static IReadOnlyList<Augment> Augments { get; private set; } = new List<Augment>();
    public static IReadOnlyList<Mutator> Mutators { get; private set; } = new List<Mutator>();
    public static IReadOnlyList<Arena> Arenas { get; private set; } = new List<Arena>();
    public static IReadOnlyList<Achievement> Achievements { get; private set; } = new List<Achievement>();
    public static IReadOnlyList<Cosmetic> Cosmetics { get; private set; } = new List<Cosmetic>();

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

    /// <summary>Every game data file, as served under /json/ and editable in the admin panel.</summary>
    public static readonly string[] Files =
    {
        "items.json", "effects.json", "events.json", "kits.json", "modifiers.json", "augments.json",
        "mutators.json", "arenas.json", "achievements.json", "cosmetics.json"
    };

    /// <summary>Folder with admin-edited copies of data files; a file here wins over json/ in the repository.</summary>
    public static string? OverrideDir { get; set; }

    /// <summary>Path a data file is currently read from (override or repository).</summary>
    public static string PathOf(string file)
    {
        if (OverrideDir is not null && File.Exists(Path.Combine(OverrideDir, file))) return Path.Combine(OverrideDir, file);
        return Path.Combine(Root, "json", file);
    }

    /// <summary>Loads (or, with apply = false, only validates) all data. <paramref name="pending"/> replaces file contents.</summary>
    public static void Load(string root, IReadOnlyDictionary<string, string>? pending = null, bool apply = true)
    {
        lock (loadLock)
        {
            var oldRoot = Root;
            Root = root;
            string Read(string file) => pending is not null && pending.TryGetValue(file, out var text) ? text : File.ReadAllText(PathOf(file));
            try
            {
                LoadFrom(Read, apply);
            }
            finally
            {
                if (!apply && oldRoot.Length > 0) Root = oldRoot;
            }
        }
    }

    private static void LoadFrom(Func<string, string> read, bool apply)
    {
        {
            var items = ItemLoader.ItemsFromJson(read("items.json"));
            var effects = ItemLoader.EffectsFromJson(read("effects.json"));
            var events = ItemLoader.EventsFromJson(read("events.json"));
            var kits = ItemLoader.ListFromJson<Kit>(read("kits.json"), "kits.json");
            var modifiers = ItemLoader.ListFromJson<RoundModifier>(read("modifiers.json"), "modifiers.json");
            var augments = ItemLoader.ListFromJson<Augment>(read("augments.json"), "augments.json");
            var mutators = ItemLoader.ListFromJson<Mutator>(read("mutators.json"), "mutators.json");
            var arenas = ItemLoader.ListFromJson<Arena>(read("arenas.json"), "arenas.json");
            var achievements = ItemLoader.ListFromJson<Achievement>(read("achievements.json"), "achievements.json");
            var cosmetics = ItemLoader.ListFromJson<Cosmetic>(read("cosmetics.json"), "cosmetics.json");
            foreach (var c in cosmetics)
                if (c.Unlock.Achievement is string a && !achievements.Any(x => x.Id == a))
                    throw new InvalidDataException($"Cosmetic {c.Id}: unknown achievement '{a}'");
            foreach (var kit in kits)
                if (kit.Abilities.Count != 4 || kit.Abilities.Count(a => a.Ultimate) != 1 || !kit.Abilities[3].Ultimate)
                    throw new InvalidDataException($"Kit {kit.Id}: needs 3 skills followed by 1 ultimate.");

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

            foreach (var id in items.GroupBy(i => i.Id).Where(g => g.Count() > 1).Select(g => g.Key))
                throw new InvalidDataException($"Duplicate item id '{id}'");
            if (!apply) return;
            Items = itemMap;
            Effects = effectMap;
            Events = events;
            Generics = items.Where(i => !i.IsSpecial).ToList();
            Specials = items.Where(i => i.IsSpecial).ToList();
            Kits = kits.ToDictionary(k => k.Id);
            Modifiers = modifiers;
            Augments = augments;
            Mutators = mutators;
            Arenas = arenas;
            Achievements = achievements;
            Cosmetics = cosmetics;
            loaded = true;
        }
    }
}
