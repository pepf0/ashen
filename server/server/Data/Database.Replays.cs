using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Ashen;

/// <summary>A finished fight waiting to be saved with its match.</summary>
public record PendingReplay(int Round, string NameA, string NameB, int Winner, double Duration, byte[] Data);

/// <summary>Stored fight replays (gzip-compressed JSON) for the most recent matches.</summary>
public partial class Database
{
    public const int ReplayMatchesKept = 100;

    private void MigrateReplays(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS fight_replays (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                match_id INTEGER NOT NULL REFERENCES matches(id) ON DELETE CASCADE,
                round INTEGER NOT NULL,
                name_a TEXT NOT NULL,
                name_b TEXT NOT NULL,
                winner INTEGER NOT NULL,
                duration REAL NOT NULL,
                data BLOB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_fight_replays_match ON fight_replays(match_id);
            """;
        cmd.ExecuteNonQuery();
    }

    public static byte[] Compress(string json)
    {
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionLevel.SmallestSize))
            gz.Write(Encoding.UTF8.GetBytes(json));
        return output.ToArray();
    }

    private static string Decompress(byte[] data)
    {
        using var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var reader = new StreamReader(gz, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Stores a match's fights and drops replays of matches older than the newest 100.</summary>
    public void SaveReplays(long matchId, IEnumerable<PendingReplay> replays)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        foreach (var r in replays)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO fight_replays (match_id, round, name_a, name_b, winner, duration, data)
                VALUES ($m, $r, $a, $b, $w, $d, $data)
                """;
            cmd.Parameters.AddWithValue("$m", matchId);
            cmd.Parameters.AddWithValue("$r", r.Round);
            cmd.Parameters.AddWithValue("$a", r.NameA);
            cmd.Parameters.AddWithValue("$b", r.NameB);
            cmd.Parameters.AddWithValue("$w", r.Winner);
            cmd.Parameters.AddWithValue("$d", r.Duration);
            cmd.Parameters.AddWithValue("$data", r.Data);
            cmd.ExecuteNonQuery();
        }
        using (var prune = conn.CreateCommand())
        {
            prune.Transaction = tx;
            prune.CommandText = $"DELETE FROM fight_replays WHERE match_id NOT IN (SELECT id FROM matches ORDER BY id DESC LIMIT {ReplayMatchesKept})";
            prune.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public List<object> ReplayList(long matchId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, round, name_a, name_b, winner, duration FROM fight_replays WHERE match_id = $m ORDER BY round, id";
        cmd.Parameters.AddWithValue("$m", matchId);
        using var r = cmd.ExecuteReader();
        var list = new List<object>();
        while (r.Read())
            list.Add(new { id = r.GetInt64(0), round = r.GetInt32(1), names = new[] { r.GetString(2), r.GetString(3) }, winner = r.GetInt32(4), duration = r.GetDouble(5) });
        return list;
    }

    /// <summary>The replay JSON (as stored), or null.</summary>
    public string? Replay(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data FROM fight_replays WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteScalar() is byte[] data ? Decompress(data) : null;
    }
}
