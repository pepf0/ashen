using System.Collections.Concurrent;

namespace Ashen;

/// <summary>Owns every lobby and remembers which lobby each user is in (for rejoining).</summary>
public class LobbyManager
{
    private const string CodeLetters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private readonly ConcurrentDictionary<string, Lobby> lobbies = new();
    private readonly ConcurrentDictionary<long, Lobby> userLobby = new();
    private readonly Database? database;

    public LobbyManager(Database? database)
    {
        this.database = database;
    }

    public Lobby? Find(string code) => lobbies.TryGetValue(code.Trim().ToUpperInvariant(), out var l) ? l : null;
    public Lobby? LobbyOf(long userId) => userLobby.TryGetValue(userId, out var l) ? l : null;

    public Lobby Create()
    {
        while (true)
        {
            var code = new string(Enumerable.Range(0, 5).Select(_ => CodeLetters[Random.Shared.Next(CodeLetters.Length)]).ToArray());
            var lobby = new Lobby(code, this, database);
            if (lobbies.TryAdd(code, lobby)) return lobby;
        }
    }

    public string? Join(Connection conn, Lobby lobby)
    {
        var current = LobbyOf(conn.User.Id);
        if (current is not null && current != lobby)
        {
            if (current.InMatch && current.HasMember(conn.User.Id))
                return "You are still in a running match. Leave it first.";
            current.Leave(conn);
        }
        var error = lobby.TryJoin(conn);
        if (error is null) userLobby[conn.User.Id] = lobby;
        else if (lobby.IsEmpty) Remove(lobby);
        return error;
    }

    public void Unbind(long userId, Lobby lobby)
    {
        userLobby.TryRemove(new KeyValuePair<long, Lobby>(userId, lobby));
    }

    public void Remove(Lobby lobby)
    {
        lobbies.TryRemove(new KeyValuePair<string, Lobby>(lobby.Code, lobby));
        lobby.Close();
        foreach (var kv in userLobby.Where(kv => kv.Value == lobby).ToList())
            userLobby.TryRemove(kv);
    }

    public List<object> PublicLobbies() => lobbies.Values
        .Select(l => l.BrowseEntry())
        .OfType<object>()
        .ToList();

    /// <summary>Deletes lobbies nobody has been connected to for a while.</summary>
    public async Task SweepAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            foreach (var lobby in lobbies.Values.Where(l => l.IsAbandoned(TimeSpan.FromMinutes(10))).ToList())
                Remove(lobby);
        }
    }
}
