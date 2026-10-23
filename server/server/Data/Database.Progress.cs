using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Ashen;

/// <summary>Account progression: XP and levels, achievements, lifetime counters, cosmetics and profiles.</summary>
public partial class Database
{
    private void MigrateProgress(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS user_progress (
                user_id INTEGER PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
                xp INTEGER NOT NULL DEFAULT 0,
                equipped TEXT NOT NULL DEFAULT '{}'
            );
            CREATE TABLE IF NOT EXISTS user_achievements (
                user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                achievement_id TEXT NOT NULL,
                unlocked_at TEXT NOT NULL,
                PRIMARY KEY (user_id, achievement_id)
            );
            CREATE TABLE IF NOT EXISTS user_counters (
                user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                key TEXT NOT NULL,
                value INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (user_id, key)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static int ReadXp(SqliteConnection conn, long userId, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT xp FROM user_progress WHERE user_id = $u";
        cmd.Parameters.AddWithValue("$u", userId);
        return cmd.ExecuteScalar() is long xp ? (int)xp : 0;
    }

    private static HashSet<string> ReadAchievements(SqliteConnection conn, long userId, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT achievement_id FROM user_achievements WHERE user_id = $u";
        cmd.Parameters.AddWithValue("$u", userId);
        using var r = cmd.ExecuteReader();
        var set = new HashSet<string>();
        while (r.Read()) set.Add(r.GetString(0));
        return set;
    }

    private static Dictionary<string, string> ReadEquipped(SqliteConnection conn, long userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT equipped FROM user_progress WHERE user_id = $u";
        cmd.Parameters.AddWithValue("$u", userId);
        return cmd.ExecuteScalar() is string json
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new()
            : new();
    }

    /// <summary>Applies a finished match to an account: counters, achievements, XP. Returns what changed.</summary>
    public ProgressResult ApplyMatch(long userId, MatchStats stats, int placement, int players, int humans, bool soloVsHard, int augments)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        void Bump(string key, long by = 1)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO user_counters (user_id, key, value) VALUES ($u, $k, $v)
                ON CONFLICT(user_id, key) DO UPDATE SET value = value + excluded.value
                """;
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", by);
            cmd.ExecuteNonQuery();
        }
        Bump("matches");
        if (placement == 1) Bump("wins");
        if (placement <= 3) Bump("top3");
        if (stats.OwnedDragon) Bump("dragon_matches");
        Bump("fights_won", stats.FightsWon);
        foreach (var kit in stats.FightWinKits) Bump($"fightwin_kit_{kit}");

        var counters = new Dictionary<string, long>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT key, value FROM user_counters WHERE user_id = $u";
            cmd.Parameters.AddWithValue("$u", userId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) counters[r.GetString(0)] = r.GetInt64(1);
        }

        var before = ReadAchievements(conn, userId, tx);
        int xpBefore = ReadXp(conn, userId, tx);
        var earned = Progression.Earned(new Progression.Context(stats, placement, players, humans, soloVsHard, augments, counters))
            .Where(id => !before.Contains(id)).ToList();
        foreach (var id in earned)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR IGNORE INTO user_achievements (user_id, achievement_id, unlocked_at) VALUES ($u, $a, $t)";
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.Parameters.AddWithValue("$a", id);
            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        int gained = Progression.MatchXp(placement, players, stats.FightsWon) + 25 * earned.Count;
        int xp = xpBefore + gained;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO user_progress (user_id, xp) VALUES ($u, $x)
                ON CONFLICT(user_id) DO UPDATE SET xp = excluded.xp
                """;
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.Parameters.AddWithValue("$x", xp);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();

        var oldLevel = Progression.LevelOf(xpBefore).Level;
        var (level, into, needed) = Progression.LevelOf(xp);
        var after = new HashSet<string>(before.Concat(earned));
        var newCosmetics = GameData.Cosmetics
            .Where(c => Progression.Unlocked(c, level, after) && !Progression.Unlocked(c, oldLevel, before))
            .Select(c => c.Id).ToList();
        return new ProgressResult(gained, xp, level, into, needed, level - oldLevel, earned, newCosmetics);
    }

    /// <summary>What others see: equipped cosmetics (only if still unlocked), chosen title and level.</summary>
    public Appearance GetAppearance(long userId)
    {
        using var conn = Open();
        int level = Progression.LevelOf(ReadXp(conn, userId)).Level;
        var achievements = ReadAchievements(conn, userId);
        var equipped = ReadEquipped(conn, userId);
        string? Pick(string kind)
        {
            if (!equipped.TryGetValue(kind, out var id)) return null;
            var c = GameData.Cosmetics.FirstOrDefault(x => x.Id == id && x.Kind == kind);
            return c is not null && Progression.Unlocked(c, level, achievements) ? c.Id : null;
        }
        string? title = equipped.TryGetValue("title", out var t) && AvailableTitles(userId, achievements).Contains(t) ? t : null;
        return new Appearance(Pick("name_color"), Pick("frame"), Pick("victory"), title, level);
    }

    private List<string> AvailableTitles(long userId, ISet<string> achievements)
    {
        var titles = GameData.Achievements.Where(a => achievements.Contains(a.Id)).Select(a => a.Name).ToList();
        if (MasteryTitle(userId) is { } mastery) titles.Insert(0, mastery);
        return titles;
    }

    /// <summary>Locker: every cosmetic with its unlock state, available titles and what's equipped.</summary>
    public object GetLocker(long userId)
    {
        using var conn = Open();
        int xp = ReadXp(conn, userId);
        var (level, into, needed) = Progression.LevelOf(xp);
        var achievements = ReadAchievements(conn, userId);
        return new
        {
            level,
            xp,
            levelXp = into,
            nextLevelXp = needed,
            equipped = ReadEquipped(conn, userId),
            titles = AvailableTitles(userId, achievements),
            cosmetics = GameData.Cosmetics.Select(c => new
            {
                c.Id, c.Kind, c.Name, c.Unlock,
                unlocked = Progression.Unlocked(c, level, achievements)
            })
        };
    }

    public void SetEquipped(long userId, Dictionary<string, string?> wanted)
    {
        using var conn = Open();
        int level = Progression.LevelOf(ReadXp(conn, userId)).Level;
        var achievements = ReadAchievements(conn, userId);
        var titles = AvailableTitles(userId, achievements);
        var equipped = new Dictionary<string, string>();
        foreach (var (kind, id) in wanted)
        {
            if (id is null) continue;
            if (kind == "title" ? titles.Contains(id)
                : GameData.Cosmetics.Any(c => c.Id == id && c.Kind == kind && Progression.Unlocked(c, level, achievements)))
                equipped[kind] = id;
        }
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO user_progress (user_id, equipped) VALUES ($u, $e)
            ON CONFLICT(user_id) DO UPDATE SET equipped = excluded.equipped
            """;
        cmd.Parameters.AddWithValue("$u", userId);
        cmd.Parameters.AddWithValue("$e", JsonSerializer.Serialize(equipped));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Share of accounts that unlocked each achievement (0-1).</summary>
    public Dictionary<string, double> AchievementRarity()
    {
        using var conn = Open();
        using var total = conn.CreateCommand();
        total.CommandText = "SELECT COUNT(*) FROM users";
        double users = Math.Max(1, (long)total.ExecuteScalar()!);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT achievement_id, COUNT(*) FROM user_achievements GROUP BY achievement_id";
        using var r = cmd.ExecuteReader();
        var rarity = GameData.Achievements.ToDictionary(a => a.Id, _ => 0.0);
        while (r.Read()) rarity[r.GetString(0)] = Math.Round(r.GetInt64(1) / users, 4);
        return rarity;
    }

    /// <summary>Public profile: level, appearance, stats, kit stats, favourite items, achievements, recent matches.</summary>
    public object? Profile(long userId)
    {
        var user = GetUser(userId);
        if (user is null) return null;
        using var conn = Open();
        int xp = ReadXp(conn, userId);
        var (level, into, needed) = Progression.LevelOf(xp);

        long games = 0, wins = 0, top3 = 0;
        double avg = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT COUNT(*), SUM(mp.placement = 1), SUM(mp.placement <= 3), AVG(mp.placement)
                FROM match_players mp JOIN matches m ON m.id = mp.match_id AND m.counted = 1 WHERE mp.user_id = $u
                """;
            cmd.Parameters.AddWithValue("$u", userId);
            using var r = cmd.ExecuteReader();
            if (r.Read() && r.GetInt64(0) > 0)
            {
                games = r.GetInt64(0);
                wins = r.GetInt64(1);
                top3 = r.GetInt64(2);
                avg = Math.Round(r.GetDouble(3), 2);
            }
        }

        var kits = new List<object>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT mp.kit, COUNT(*), SUM(mp.placement = 1) FROM match_players mp JOIN matches m ON m.id = mp.match_id AND m.counted = 1
                WHERE mp.user_id = $u AND mp.kit IS NOT NULL GROUP BY mp.kit ORDER BY COUNT(*) DESC
                """;
            cmd.Parameters.AddWithValue("$u", userId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) kits.Add(new { kit = r.GetString(0), games = r.GetInt64(1), wins = r.GetInt64(2) });
        }

        var itemCounts = new Dictionary<string, int>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT build FROM match_players WHERE user_id = $u";
            cmd.Parameters.AddWithValue("$u", userId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                foreach (var item in JsonSerializer.Deserialize<List<ItemView>>(r.GetString(0), Protocol.Json) ?? new())
                    itemCounts[item.Id] = itemCounts.GetValueOrDefault(item.Id) + 1;
        }

        var unlocked = new List<object>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT achievement_id, unlocked_at FROM user_achievements WHERE user_id = $u ORDER BY unlocked_at";
            cmd.Parameters.AddWithValue("$u", userId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) unlocked.Add(new { id = r.GetString(0), at = r.GetString(1) });
        }

        long fightsWon = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT value FROM user_counters WHERE user_id = $u AND key = 'fights_won'";
            cmd.Parameters.AddWithValue("$u", userId);
            if (cmd.ExecuteScalar() is long v) fightsWon = v;
        }

        return new
        {
            id = userId,
            name = user.DisplayName,
            username = user.Username,
            level,
            xp,
            levelXp = into,
            nextLevelXp = needed,
            appearance = GetAppearance(userId),
            mastery = MasteryTitle(userId),
            stats = new { games, wins, top3, winRate = games == 0 ? 0 : Math.Round((double)wins / games, 3), avgPlacement = avg, fightsWon },
            kits,
            favouriteItems = itemCounts.OrderByDescending(kv => kv.Value).Take(6).Select(kv => new { id = kv.Key, count = kv.Value }),
            achievements = unlocked,
            recentMatches = RecentMatches(5, userId)
        };
    }
}
