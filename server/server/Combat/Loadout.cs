namespace Ashen;

/// <summary>A frozen copy of a player's build that a fight is simulated with.</summary>
public class Loadout
{
    public string Name { get; set; } = "";
    public long? PlayerId { get; set; }
    public bool Ghost { get; set; }
    public double MaxHealth { get; set; } = 1000;
    /// <summary>Money held when the fight starts (for interest).</summary>
    public int Money { get; set; }
    public OwnedItem? Weapon { get; set; }
    public OwnedItem? Shirt { get; set; }
    public OwnedItem?[] Dynamics { get; set; } = new OwnedItem?[4];
    /// <summary>Kit id (kits.json), or null for no abilities.</summary>
    public string? Kit { get; set; }
    /// <summary>Abilities are pressed by the built-in bot (ghosts, disconnected players, tests).</summary>
    public bool Bot { get; set; }
    /// <summary>Extra always-on effects, e.g. lobby event rewards.</summary>
    public List<string> BonusEffects { get; set; } = new();

    public IEnumerable<OwnedItem> AllItems()
    {
        if (Weapon is not null) yield return Weapon;
        if (Shirt is not null) yield return Shirt;
        foreach (var item in Dynamics)
            if (item is not null) yield return item;
    }
}
