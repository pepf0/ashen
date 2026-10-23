using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ashen;

/// <summary>
/// Writes one JSON Lines file per match for balancing: every shop roll, purchase, fight and a per-round
/// snapshot of each player. Each line is one JSON object with a "type" and a "t" timestamp.
/// The folder comes from ASHEN_LOGS (default: "logs" next to the database).
/// </summary>
public sealed class BalanceLog : IDisposable
{
    public static string? Directory { get; set; }

    private readonly StreamWriter? writer;

    private BalanceLog(StreamWriter? writer)
    {
        this.writer = writer;
    }

    public static BalanceLog Open(string lobbyCode)
    {
        if (string.IsNullOrEmpty(Directory)) return new BalanceLog(null);
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = Path.Combine(Directory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{lobbyCode}.jsonl");
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            return new BalanceLog(new StreamWriter(stream) { AutoFlush = true });
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[balance log] could not open log for {lobbyCode}: {e.Message}");
            return new BalanceLog(null);
        }
    }

    public void Write(string type, object data)
    {
        if (writer is null) return;
        try
        {
            var node = JsonSerializer.SerializeToNode(data, Protocol.Json) as JsonObject ?? new JsonObject();
            var line = new JsonObject { ["type"] = type, ["t"] = DateTime.UtcNow.ToString("O") };
            foreach (var (key, value) in node.ToList())
            {
                node.Remove(key);
                line[key] = value;
            }
            writer.WriteLine(line.ToJsonString(Protocol.Json));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[balance log] write failed: {e.Message}");
        }
    }

    /// <summary>A player's build as logged: item ids with levels, per slot.</summary>
    public static object Build(Player p) => new
    {
        weapon = p.Weapon is { } w ? new { id = w.Id, level = w.Level } : null,
        shirt = p.Shirt is { } s ? new { id = s.Id, level = s.Level } : null,
        dynamics = p.Dynamics.Select(d => d is null ? null : new { id = d.Id, level = d.Level })
    };

    public void Dispose() => writer?.Dispose();
}
