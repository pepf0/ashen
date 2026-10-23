using Microsoft.Data.Sqlite;

namespace Ashen;

public record FriendRow(long UserId, string Name, string Status, bool Incoming);

/// <summary>Friend requests and friendships (one row per pair; status "pending" or "accepted").</summary>
public partial class Database
{
    private void MigrateFriends(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS friendships (
                requester_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                addressee_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                status TEXT NOT NULL,
                created_at TEXT NOT NULL,
                PRIMARY KEY (requester_id, addressee_id)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public long? UserIdByName(string username)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM users WHERE username = $u";
        cmd.Parameters.AddWithValue("$u", username.Trim());
        return cmd.ExecuteScalar() is long id ? id : null;
    }

    /// <summary>Sends a request, or accepts the other side's pending request. Returns an error or null.</summary>
    public string? RequestFriend(long from, long to)
    {
        if (from == to) return "You can't add yourself.";
        using var conn = Open();
        using (var check = conn.CreateCommand())
        {
            check.CommandText = """
                SELECT requester_id, status FROM friendships
                WHERE (requester_id = $a AND addressee_id = $b) OR (requester_id = $b AND addressee_id = $a)
                """;
            check.Parameters.AddWithValue("$a", from);
            check.Parameters.AddWithValue("$b", to);
            using var r = check.ExecuteReader();
            if (r.Read())
            {
                if (r.GetString(1) == "accepted") return "You are already friends.";
                if (r.GetInt64(0) == from) return "Request already sent.";
                r.Close();
                RespondFriend(to, from, true);
                return null;
            }
        }
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO friendships (requester_id, addressee_id, status, created_at) VALUES ($a, $b, 'pending', $t)";
        cmd.Parameters.AddWithValue("$a", from);
        cmd.Parameters.AddWithValue("$b", to);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
        return null;
    }

    /// <summary>The addressee accepts or declines a pending request from <paramref name="requester"/>.</summary>
    public void RespondFriend(long requester, long addressee, bool accept)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = accept
            ? "UPDATE friendships SET status = 'accepted' WHERE requester_id = $a AND addressee_id = $b"
            : "DELETE FROM friendships WHERE requester_id = $a AND addressee_id = $b AND status = 'pending'";
        cmd.Parameters.AddWithValue("$a", requester);
        cmd.Parameters.AddWithValue("$b", addressee);
        cmd.ExecuteNonQuery();
    }

    public void RemoveFriend(long a, long b)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM friendships WHERE (requester_id = $a AND addressee_id = $b) OR (requester_id = $b AND addressee_id = $a)";
        cmd.Parameters.AddWithValue("$a", a);
        cmd.Parameters.AddWithValue("$b", b);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Friends and requests involving the user. Status: "accepted", "incoming" or "outgoing".</summary>
    public List<FriendRow> Friends(long userId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT f.requester_id, f.addressee_id, f.status, u.id, u.display_name
            FROM friendships f JOIN users u ON u.id = CASE WHEN f.requester_id = $u THEN f.addressee_id ELSE f.requester_id END
            WHERE f.requester_id = $u OR f.addressee_id = $u
            ORDER BY u.display_name COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("$u", userId);
        using var r = cmd.ExecuteReader();
        var list = new List<FriendRow>();
        while (r.Read())
        {
            bool incoming = r.GetInt64(1) == userId;
            string status = r.GetString(2) == "accepted" ? "accepted" : incoming ? "incoming" : "outgoing";
            list.Add(new FriendRow(r.GetInt64(3), r.GetString(4), status, incoming));
        }
        return list;
    }

    public bool AreFriends(long a, long b) => Friends(a).Any(f => f.UserId == b && f.Status == "accepted");
}
