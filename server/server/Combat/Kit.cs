namespace Ashen;

/// <summary>A class players pick before the match: three skills (Q/W/E) and an ultimate (R). From kits.json.</summary>
public class Kit
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<Ability> Abilities { get; set; } = new();

    public static IReadOnlyDictionary<string, Kit> All => GameData.Kits;
    public const string Default = "duelist";
    public const int SwapCost = 15;
}

/// <summary>One kit ability. Which fields matter depends on the ability (see Combat.UseAbility).</summary>
public class Ability
{
    public string Id { get; set; } = "";
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Ultimate { get; set; }
    public bool NeedsWeapon { get; set; }
    public bool UsableWhileStunned { get; set; }
    public double? Cooldown { get; set; }
    public double? Duration { get; set; }
    public double? Mult { get; set; }
    public double? Add { get; set; }
    public double? Taken { get; set; }
    public double? Reflect { get; set; }
    public double? Fraction { get; set; }
    public int? Hits { get; set; }
    public double? Chance { get; set; }
    public double? WinMult { get; set; }
    public double? LoseMult { get; set; }
    public double? Money { get; set; }
    public double? Windup { get; set; }
    public string Description { get; set; } = "";
}

/// <summary>A lobby-wide twist for one round, from modifiers.json. Effects are implemented by id.</summary>
public class RoundModifier
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Positive { get; set; }

    public static IReadOnlyList<RoundModifier> All => GameData.Modifiers;
    /// <summary>Share of rounds (from round 2) without any modifier.</summary>
    public const double CalmChance = 0.3;
}

/// <summary>A match-long rule rolled at match start (mutators.json). Effects are implemented by id.</summary>
public class Mutator
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    public static IReadOnlyList<Mutator> All => GameData.Mutators;
    public const int PerMatch = 2;
}

/// <summary>Where a single fight takes place (arenas.json). Effects are implemented by id.</summary>
public class Arena
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    public static IReadOnlyList<Arena> All => GameData.Arenas;
    /// <summary>Share of fights without an arena.</summary>
    public const double PlainChance = 0.4;
}
