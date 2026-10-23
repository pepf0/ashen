namespace Ashen;

public class Achievement
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
}

public class CosmeticUnlock
{
    public int? Level { get; set; }
    public string? Achievement { get; set; }
}

/// <summary>Purely visual reward (name color, fighter frame, victory effect). No gameplay effect.</summary>
public class Cosmetic
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public CosmeticUnlock Unlock { get; set; } = new();
}

/// <summary>What a player shows to others: chosen cosmetics and title.</summary>
public record Appearance(string? NameColor, string? Frame, string? Victory, string? Title, int Level);

/// <summary>Everything that happened to one player in one match, for XP and achievements.</summary>
public class MatchStats
{
    public int FightsWon { get; set; }
    public int Flawless { get; set; }
    public bool Clutch { get; set; }
    public double MaxFightDamage { get; set; }
    public int Stuns { get; set; }
    public int Abilities { get; set; }
    public int Ultimates { get; set; }
    public bool HeadshotFinish { get; set; }
    public bool SmashFinish { get; set; }
    public bool SecondWindWin { get; set; }
    public int MaxWinStreak { get; set; }
    public int BountiesCollected { get; set; }
    public int MaxBounty { get; set; }
    public bool OwnedDragon { get; set; }
    public bool FullPack { get; set; }
    public bool MaxLevelReached { get; set; }
    public int MaxMoney { get; set; }
    public int Spent { get; set; }
    public int Rerolls { get; set; }
    public double MaxHp { get; set; }
    public bool EverShirt { get; set; }
    public bool WeaponlessTaken { get; set; }
    public bool Prismatic { get; set; }
    public int GambleBestWin { get; set; }
    public bool Shrine { get; set; }
    public int EventsClaimed { get; set; }
    public int MinLives { get; set; } = int.MaxValue;
    public int LivesLost { get; set; }
    public HashSet<string> FightWinKits { get; } = new();
}

/// <summary>Result of a match for one player's account, sent to the game over screen.</summary>
public record ProgressResult(int XpGained, int Xp, int Level, int LevelXp, int NextLevelXp, int LevelsGained, List<string> NewAchievements, List<string> NewCosmetics);

public static class Progression
{
    public const int MaxLevel = 50;

    /// <summary>XP needed to go from <paramref name="level"/> to the next: 300, then +10% per level.</summary>
    public static int XpForLevel(int level) => (int)Math.Round(300 * Math.Pow(1.1, level - 1));

    public static (int Level, int IntoLevel, int Needed) LevelOf(int xp)
    {
        int level = 1;
        while (level < MaxLevel && xp >= XpForLevel(level))
        {
            xp -= XpForLevel(level);
            level++;
        }
        return (level, xp, level < MaxLevel ? XpForLevel(level) : 0);
    }

    /// <summary>100 for playing, +50 per placement above last, +10 per fight won (achievements add 25 each later).</summary>
    public static int MatchXp(int placement, int players, int fightsWon) =>
        100 + 50 * Math.Max(0, players - placement) + 10 * fightsWon;

    public sealed record Context(MatchStats Stats, int Placement, int Players, int Humans, bool SoloVsHard, int Augments,
        IReadOnlyDictionary<string, long> Counters);

    /// <summary>Achievement ids whose condition is met by this match plus the (already updated) lifetime counters.</summary>
    public static IEnumerable<string> Earned(Context c)
    {
        var s = c.Stats;
        long Count(string key) => c.Counters.TryGetValue(key, out var v) ? v : 0;
        bool won = c.Placement == 1;
        var checks = new Dictionary<string, bool>
        {
            ["first_match"] = true,
            ["first_win"] = won,
            ["win_5"] = Count("wins") >= 5,
            ["win_25"] = Count("wins") >= 25,
            ["play_10"] = Count("matches") >= 10,
            ["play_50"] = Count("matches") >= 50,
            ["play_100"] = Count("matches") >= 100,
            ["top3_10"] = Count("top3") >= 10,
            ["full_lobby_win"] = won && c.Players >= 8,
            ["untouched"] = won && s.LivesLost == 0,
            ["comeback"] = won && s.MinLives <= 1 && s.LivesLost > 0,
            ["bot_slayer"] = won && c.SoloVsHard,
            ["flawless"] = s.Flawless > 0,
            ["clutch"] = s.Clutch,
            ["heavy_hitter"] = s.MaxFightDamage >= 500,
            ["stunlock"] = s.Stuns >= 10,
            ["button_masher"] = s.Abilities >= 50,
            ["ultimate_power"] = s.Ultimates >= 5,
            ["headshot_finish"] = s.HeadshotFinish,
            ["smash_finish"] = s.SmashFinish,
            ["not_today"] = s.SecondWindWin,
            ["streak_7"] = s.MaxWinStreak >= 7,
            ["bounty_hunter"] = s.BountiesCollected > 0,
            ["most_wanted"] = s.MaxBounty >= 50,
            ["every_kit"] = GameData.Kits.Keys.All(k => Count($"fightwin_kit_{k}") > 0),
            ["dragon"] = s.OwnedDragon,
            ["dragon_5"] = Count("dragon_matches") >= 5,
            ["full_pack"] = s.FullPack,
            ["max_level"] = s.MaxLevelReached,
            ["hoarder"] = s.MaxMoney >= 300,
            ["big_spender"] = s.Spent >= 500,
            ["reroll_addict"] = s.Rerolls >= 30,
            ["tank"] = s.MaxHp >= 500,
            ["shirtless"] = won && !s.EverShirt,
            ["weaponless"] = s.WeaponlessTaken,
            ["prismatic"] = s.Prismatic,
            ["augmented"] = c.Augments >= 3,
            ["high_roller"] = s.GambleBestWin >= 50,
            ["devotee"] = s.Shrine,
            ["event_collector"] = s.EventsClaimed >= 3
        };
        return checks.Where(kv => kv.Value).Select(kv => kv.Key);
    }

    public static bool Unlocked(Cosmetic c, int level, ISet<string> achievements) =>
        (c.Unlock.Level is int l && level >= l) || (c.Unlock.Achievement is string a && achievements.Contains(a));
}
