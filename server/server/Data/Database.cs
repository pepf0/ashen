using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Ashen;

public record User(long Id, string Username, string DisplayName);

public class MatchRecord
{
    public string LobbyCode { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
    public int Rounds { get; set; }
    public LobbySettings Settings { get; set; } = new();
    public List<MatchPlayerRecord> Players { get; set; } = new();
}

public class MatchPlayerRecord
{
    public long UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public int Placement { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public List<ItemView> Build { get; set; } = new();
    public List<string> Titles { get; set; } = new();
    public string? Kit { get; set; }
    public List<string> Augments { get; set; } = new();
}

/// <summary>SQLite storage for accounts, sessions and match history. The schema is created on startup.</summary>
public partial class Database
{
    private const int HashIterations = 100_000;
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);
    private readonly string connectionString;

    public Database(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (dir is not null) Directory.CreateDirectory(dir);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = true }.ToString();
        Migrate();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    private void Migrate()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT NOT NULL UNIQUE COLLATE NOCASE,
                display_name TEXT NOT NULL,
                password_hash BLOB NOT NULL,
                salt BLOB NOT NULL,
                iterations INTEGER NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS sessions (
                token_hash TEXT PRIMARY KEY,
                user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                created_at TEXT NOT NULL,
                expires_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS matches (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                lobby_code TEXT NOT NULL,
                started_at TEXT NOT NULL,
                ended_at TEXT NOT NULL,
                rounds INTEGER NOT NULL,
                settings TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS match_players (
                match_id INTEGER NOT NULL REFERENCES matches(id) ON DELETE CASCADE,
                user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                display_name TEXT NOT NULL,
                placement INTEGER NOT NULL,
                wins INTEGER NOT NULL,
                losses INTEGER NOT NULL,
                build TEXT NOT NULL,
                titles TEXT NOT NULL,
                PRIMARY KEY (match_id, user_id)
            );
            CREATE INDEX IF NOT EXISTS idx_match_players_user ON match_players(user_id);
            CREATE TABLE IF NOT EXISTS user_settings (
                user_id INTEGER PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
                keybinds TEXT NOT NULL DEFAULT '{}'
            );
            """;
        cmd.ExecuteNonQuery();
        AddColumnIfMissing(conn, "match_players", "kit", "TEXT");
        AddColumnIfMissing(conn, "match_players", "augments", "TEXT NOT NULL DEFAULT '[]'");
        MigrateProgress(conn);
        MigrateFriends(conn);
    }

    private static void AddColumnIfMissing(SqliteConnection conn, string table, string column, string type)
    {
        using var check = conn.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'";
        if ((long)check.ExecuteScalar()! > 0) return;
        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
        alter.ExecuteNonQuery();
    }

    // ---------- kit mastery ----------

    public static readonly (int Wins, string Rank)[] MasteryRanks = { (50, "Master"), (20, "Expert"), (5, "Adept") };

    /// <summary>Match wins per kit for a user.</summary>
    public Dictionary<string, int> KitWins(long userId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT kit, COUNT(*) FROM match_players WHERE user_id = $u AND placement = 1 AND kit IS NOT NULL GROUP BY kit";
        cmd.Parameters.AddWithValue("$u", userId);
        using var rd = cmd.ExecuteReader();
        var wins = new Dictionary<string, int>();
        while (rd.Read()) wins[rd.GetString(0)] = rd.GetInt32(1);
        return wins;
    }

    /// <summary>The user's best mastery title, e.g. "Duelist Expert", or null below 5 wins with every kit.</summary>
    public string? MasteryTitle(long userId)
    {
        var best = KitWins(userId).OrderByDescending(kv => kv.Value).FirstOrDefault();
        if (best.Key is null) return null;
        foreach (var (wins, rank) in MasteryRanks)
            if (best.Value >= wins)
                return $"{(GameData.Kits.TryGetValue(best.Key, out var kit) ? kit.Name : best.Key)} {rank}";
        return null;
    }

    // ---------- accounts ----------

    public static string? ValidateUsername(string? username)
    {
        if (string.IsNullOrEmpty(username) || username.Length is < 3 or > 20 || !username.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            return "Usernames are 3-20 characters: letters, digits and _.";
        return null;
    }

    public static string? ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 6) return "Passwords need at least 6 characters.";
        if (password.Length > 128) return "That password is too long.";
        return null;
    }

    public static string? CleanDisplayName(string? name)
    {
        if (name is null) return null;
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return name.Length is >= 1 and <= 20 ? name : null;
    }

    private static byte[] Hash(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);

    /// <summary>Creates an account. Returns null if the username is taken.</summary>
    public User? Register(string username, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Hash(password, salt, HashIterations);
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO users (username, display_name, password_hash, salt, iterations, created_at)
            VALUES ($u, $u, $h, $s, $i, $c) ON CONFLICT(username) DO NOTHING RETURNING id
            """;
        cmd.Parameters.AddWithValue("$u", username);
        cmd.Parameters.AddWithValue("$h", hash);
        cmd.Parameters.AddWithValue("$s", salt);
        cmd.Parameters.AddWithValue("$i", HashIterations);
        cmd.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("O"));
        var id = cmd.ExecuteScalar();
        return id is null ? null : new User((long)id, username, username);
    }

    public User? Login(string username, string password)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, username, display_name, password_hash, salt, iterations FROM users WHERE username = $u";
        cmd.Parameters.AddWithValue("$u", username);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
        {
            // Spend the same time as a real check so usernames can't be probed by timing.
            Hash(password, new byte[16], HashIterations);
            return null;
        }
        var expected = (byte[])r["password_hash"];
        var actual = Hash(password, (byte[])r["salt"], r.GetInt32(5));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual)) return null;
        return new User(r.GetInt64(0), r.GetString(1), r.GetString(2));
    }

    public bool ChangePassword(long userId, string oldPassword, string newPassword)
    {
        var user = GetUser(userId);
        if (user is null || Login(user.Username, oldPassword) is null) return false;
        var salt = RandomNumberGenerator.GetBytes(16);
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET password_hash = $h, salt = $s, iterations = $i WHERE id = $id";
        cmd.Parameters.AddWithValue("$h", Hash(newPassword, salt, HashIterations));
        cmd.Parameters.AddWithValue("$s", salt);
        cmd.Parameters.AddWithValue("$i", HashIterations);
        cmd.Parameters.AddWithValue("$id", userId);
        cmd.ExecuteNonQuery();
        return true;
    }

    public User? GetUser(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, username, display_name FROM users WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new User(r.GetInt64(0), r.GetString(1), r.GetString(2)) : null;
    }

    public void SetDisplayName(long userId, string name)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET display_name = $n WHERE id = $id";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$id", userId);
        cmd.ExecuteNonQuery();
    }

    // ---------- settings ----------

    /// <summary>Actions that can be bound to keys, with their default keys (KeyboardEvent.key, lower case).</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultKeybinds = new Dictionary<string, string>
    {
        ["skill_q"] = "q", ["skill_w"] = "w", ["skill_e"] = "e", ["skill_ult"] = "r",
        ["reroll"] = "d", ["ready"] = " ", ["hp"] = "h",
        ["buy1"] = "1", ["buy2"] = "2", ["buy3"] = "3", ["buy4"] = "4", ["buy5"] = "5", ["buy6"] = "6"
    };

    public Dictionary<string, string> GetKeybinds(long userId)
    {
        var binds = new Dictionary<string, string>(DefaultKeybinds);
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT keybinds FROM user_settings WHERE user_id = $u";
        cmd.Parameters.AddWithValue("$u", userId);
        if (cmd.ExecuteScalar() is string json)
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            foreach (var (action, key) in saved)
                if (binds.ContainsKey(action)) binds[action] = key;
        }
        return binds;
    }

    /// <summary>Saves keybinds; unknown actions and invalid keys are dropped.</summary>
    public Dictionary<string, string> SetKeybinds(long userId, Dictionary<string, string> binds)
    {
        var clean = binds
            .Where(kv => DefaultKeybinds.ContainsKey(kv.Key) && kv.Value is { Length: > 0 and <= 20 })
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToLowerInvariant());
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO user_settings (user_id, keybinds) VALUES ($u, $k)
            ON CONFLICT(user_id) DO UPDATE SET keybinds = excluded.keybinds
            """;
        cmd.Parameters.AddWithValue("$u", userId);
        cmd.Parameters.AddWithValue("$k", JsonSerializer.Serialize(clean));
        cmd.ExecuteNonQuery();
        return GetKeybinds(userId);
    }

    // ---------- sessions ----------

    private static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public string CreateSession(long userId)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM sessions WHERE expires_at < $now;
            INSERT INTO sessions (token_hash, user_id, created_at, expires_at) VALUES ($t, $u, $now, $e);
            """;
        cmd.Parameters.AddWithValue("$t", TokenHash(token));
        cmd.Parameters.AddWithValue("$u", userId);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$e", (DateTime.UtcNow + SessionLifetime).ToString("O"));
        cmd.ExecuteNonQuery();
        return token;
    }

    public User? UserForToken(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 200) return null;
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT u.id, u.username, u.display_name FROM sessions s JOIN users u ON u.id = s.user_id
            WHERE s.token_hash = $t AND s.expires_at > $now
            """;
        cmd.Parameters.AddWithValue("$t", TokenHash(token));
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        using var r = cmd.ExecuteReader();
        return r.Read() ? new User(r.GetInt64(0), r.GetString(1), r.GetString(2)) : null;
    }

    public void DeleteSession(string token)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sessions WHERE token_hash = $t";
        cmd.Parameters.AddWithValue("$t", TokenHash(token));
        cmd.ExecuteNonQuery();
    }

    // ---------- matches ----------

    public long SaveMatch(MatchRecord match)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        long matchId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO matches (lobby_code, started_at, ended_at, rounds, settings)
                VALUES ($c, $s, $e, $r, $set) RETURNING id
                """;
            cmd.Parameters.AddWithValue("$c", match.LobbyCode);
            cmd.Parameters.AddWithValue("$s", match.StartedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$e", match.EndedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$r", match.Rounds);
            cmd.Parameters.AddWithValue("$set", JsonSerializer.Serialize(match.Settings, Protocol.Json));
            matchId = (long)cmd.ExecuteScalar()!;
        }
        foreach (var p in match.Players)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO match_players (match_id, user_id, display_name, placement, wins, losses, build, titles, kit, augments)
                VALUES ($m, $u, $n, $p, $w, $l, $b, $t, $k, $a)
                """;
            cmd.Parameters.AddWithValue("$m", matchId);
            cmd.Parameters.AddWithValue("$u", p.UserId);
            cmd.Parameters.AddWithValue("$n", p.DisplayName);
            cmd.Parameters.AddWithValue("$p", p.Placement);
            cmd.Parameters.AddWithValue("$w", p.Wins);
            cmd.Parameters.AddWithValue("$l", p.Losses);
            cmd.Parameters.AddWithValue("$b", JsonSerializer.Serialize(p.Build, Protocol.Json));
            cmd.Parameters.AddWithValue("$t", JsonSerializer.Serialize(p.Titles, Protocol.Json));
            cmd.Parameters.AddWithValue("$k", (object?)p.Kit ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$a", JsonSerializer.Serialize(p.Augments, Protocol.Json));
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return matchId;
    }

    public List<object> Leaderboard(int limit = 50)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT u.id, u.display_name, COUNT(*) AS games,
                   SUM(CASE WHEN mp.placement = 1 THEN 1 ELSE 0 END) AS wins,
                   SUM(CASE WHEN mp.placement <= 3 THEN 1 ELSE 0 END) AS top3,
                   AVG(mp.placement) AS avg_placement
            FROM match_players mp JOIN users u ON u.id = mp.user_id
            GROUP BY u.id
            ORDER BY wins DESC, (wins * 1.0 / games) DESC, games DESC
            LIMIT $l
            """;
        cmd.Parameters.AddWithValue("$l", limit);
        using var r = cmd.ExecuteReader();
        var list = new List<object>();
        while (r.Read())
        {
            long games = r.GetInt64(2), wins = r.GetInt64(3);
            list.Add(new
            {
                id = r.GetInt64(0),
                name = r.GetString(1),
                mastery = MasteryTitle(r.GetInt64(0)),
                games,
                wins,
                winRate = games == 0 ? 0 : Math.Round((double)wins / games, 3),
                top3 = r.GetInt64(4),
                avgPlacement = Math.Round(r.GetDouble(5), 2)
            });
        }
        return list;
    }

    public List<object> RecentMatches(int limit, long? userId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT m.id, m.lobby_code, m.ended_at, m.rounds,
                   (SELECT COUNT(*) FROM match_players x WHERE x.match_id = m.id) AS players,
                   (SELECT group_concat(x.display_name, ', ') FROM match_players x WHERE x.match_id = m.id AND x.placement = 1) AS winners
            FROM matches m
            WHERE $u IS NULL OR EXISTS (SELECT 1 FROM match_players x WHERE x.match_id = m.id AND x.user_id = $u)
            ORDER BY m.id DESC LIMIT $l
            """;
        cmd.Parameters.AddWithValue("$u", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$l", limit);
        using var r = cmd.ExecuteReader();
        var list = new List<object>();
        while (r.Read())
        {
            list.Add(new
            {
                id = r.GetInt64(0),
                lobby = r.GetString(1),
                endedAt = r.GetString(2),
                rounds = r.GetInt32(3),
                players = r.GetInt32(4),
                winners = r.IsDBNull(5) ? "" : r.GetString(5)
            });
        }
        return list;
    }

    public object? MatchDetails(long id)
    {
        using var conn = Open();
        object? header;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, lobby_code, started_at, ended_at, rounds FROM matches WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            header = new { id = r.GetInt64(0), lobby = r.GetString(1), startedAt = r.GetString(2), endedAt = r.GetString(3), rounds = r.GetInt32(4) };
        }
        var players = new List<object>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT user_id, display_name, placement, wins, losses, build, titles, kit, augments
                FROM match_players WHERE match_id = $id ORDER BY placement
                """;
            cmd.Parameters.AddWithValue("$id", id);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                players.Add(new
                {
                    id = r.GetInt64(0),
                    name = r.GetString(1),
                    placement = r.GetInt32(2),
                    wins = r.GetInt32(3),
                    losses = r.GetInt32(4),
                    build = JsonSerializer.Deserialize<List<ItemView>>(r.GetString(5), Protocol.Json),
                    titles = JsonSerializer.Deserialize<List<string>>(r.GetString(6), Protocol.Json),
                    kit = r.IsDBNull(7) ? null : r.GetString(7),
                    augments = JsonSerializer.Deserialize<List<string>>(r.GetString(8), Protocol.Json)
                });
            }
        }
        return new { match = header, players };
    }
}
