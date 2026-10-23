namespace Ashen;

public enum AugmentTier
{
    Silver,
    Gold,
    Prismatic
}

/// <summary>
/// A run-long bonus picked at rounds 3, 6 and 9 (augments.json). Mods are generic keys the server understands:
/// keys ending in "_mult" multiply, everything else adds up across augments.
/// </summary>
public class Augment
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public AugmentTier Tier { get; set; }
    public string Description { get; set; } = "";
    public Dictionary<string, double> Mods { get; set; } = new();

    public static IReadOnlyList<Augment> All => GameData.Augments;
    public static readonly int[] DefaultRounds = { 3, 6, 9 };

    /// <summary>Combined mods of several augments: "_mult" keys multiply, others add.</summary>
    public static Dictionary<string, double> Combine(IEnumerable<string> augmentIds)
    {
        var total = new Dictionary<string, double>();
        foreach (var id in augmentIds)
        {
            var aug = GameData.Augments.FirstOrDefault(a => a.Id == id);
            if (aug is null) continue;
            foreach (var (key, value) in aug.Mods)
            {
                bool mult = key.EndsWith("_mult");
                total[key] = total.TryGetValue(key, out var v) ? (mult ? v * value : v + value) : value;
            }
        }
        return total;
    }

    /// <summary>Rolls a tier (60/30/10) and 3 different augments of it the player doesn't have yet.</summary>
    public static List<string> RollOffer(ICollection<string> owned, Random rng, int count = 3)
    {
        double roll = rng.NextDouble();
        var tier = roll < 0.6 ? AugmentTier.Silver : roll < 0.9 ? AugmentTier.Gold : AugmentTier.Prismatic;
        var pool = GameData.Augments.Where(a => a.Tier == tier && !owned.Contains(a.Id)).ToList();
        if (pool.Count < count) pool = GameData.Augments.Where(a => !owned.Contains(a.Id)).ToList();
        return pool.OrderBy(_ => rng.Next()).Take(count).Select(a => a.Id).ToList();
    }
}

public static class Mods
{
    public static double Get(this IReadOnlyDictionary<string, double> mods, string key) =>
        mods.TryGetValue(key, out var v) ? v : (key.EndsWith("_mult") ? 1 : 0);
}
