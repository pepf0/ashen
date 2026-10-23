using System.Text.Json;

namespace Ashen;

/// <summary>Bans, user search and server statistics for the admin panel.</summary>
public partial class Database
{
    public bool IsBanned(long userId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT banned FROM users WHERE id = $u";
        cmd.Parameters.AddWithValue("$u", userId);
        return cmd.ExecuteScalar() is long b && b != 0;
    }

    public void SetBanned(long userId, bool banned, string? reason)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE users SET banned = $b, ban_reason = $r WHERE id = $u;
            DELETE FROM sessions WHERE user_id = $u AND $b = 1;
            """;
        cmd.Parameters.AddWithValue("$b", banned ? 1 : 0);
        cmd.Parameters.AddWithValue("$r", (object?)reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$u", userId);
        cmd.ExecuteNonQuery();
    }

    public List<object> SearchUsers(string query, int limit = 50)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, username, display_name, created_at, banned, ban_reason FROM users
            WHERE username LIKE $q OR display_name LIKE $q ORDER BY id DESC LIMIT $l
            """;
        cmd.Parameters.AddWithValue("$q", $"%{query}%");
        cmd.Parameters.AddWithValue("$l", limit);
        using var r = cmd.ExecuteReader();
        var list = new List<object>();
        while (r.Read())
            list.Add(new
            {
                id = r.GetInt64(0), username = r.GetString(1), name = r.GetString(2), createdAt = r.GetString(3),
                banned = r.GetInt64(4) != 0, reason = r.IsDBNull(5) ? null : r.GetString(5)
            });
        return list;
    }

    /// <summary>Totals plus kit, item and augment statistics from counted matches.</summary>
    public object AdminStats()
    {
        using var conn = Open();
        long Scalar(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$today", DateTime.UtcNow.Date.ToString("O"));
            return cmd.ExecuteScalar() is long v ? v : 0;
        }

        var kits = new List<object>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT kit, COUNT(*), SUM(placement = 1), AVG(placement) FROM match_players
                WHERE kit IS NOT NULL GROUP BY kit ORDER BY COUNT(*) DESC
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
                kits.Add(new { kit = r.GetString(0), picks = r.GetInt64(1), wins = r.GetInt64(2), avgPlacement = Math.Round(r.GetDouble(3), 2) });
        }

        var items = new Dictionary<string, (int Picks, int Wins)>();
        var augments = new Dictionary<string, (int Picks, int Wins)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT build, augments, placement FROM match_players";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                bool won = r.GetInt32(2) == 1;
                foreach (var it in JsonSerializer.Deserialize<List<ItemView>>(r.GetString(0), Protocol.Json) ?? new())
                {
                    var c = items.GetValueOrDefault(it.Id);
                    items[it.Id] = (c.Picks + 1, c.Wins + (won ? 1 : 0));
                }
                foreach (var a in JsonSerializer.Deserialize<List<string>>(r.GetString(1)) ?? new())
                {
                    var c = augments.GetValueOrDefault(a);
                    augments[a] = (c.Picks + 1, c.Wins + (won ? 1 : 0));
                }
            }
        }

        return new
        {
            users = Scalar("SELECT COUNT(*) FROM users"),
            matches = Scalar("SELECT COUNT(*) FROM matches"),
            matchesToday = Scalar("SELECT COUNT(*) FROM matches WHERE ended_at >= $today"),
            kits,
            items = items.OrderByDescending(kv => kv.Value.Picks).Select(kv => new { id = kv.Key, picks = kv.Value.Picks, wins = kv.Value.Wins }),
            augments = augments.OrderByDescending(kv => kv.Value.Picks).Select(kv => new { id = kv.Key, picks = kv.Value.Picks, wins = kv.Value.Wins })
        };
    }
}
